namespace Projection.Pipeline

open Projection.Core
open FsToolkit.ErrorHandling

/// Chapter C slice C.3 — operator-supplied emission-folder targeting.
/// The binder resolves textual config entries (`Overrides
/// .EmissionFolders : EmissionFolderEntry list`) into a typed
/// `Map<SsKey, string>` consumed by the post-emit, pre-compose rewrite
/// step in `Compose.applyEmissionFolderOverrides`.
///
/// **Pillar 9 classification** — `EmissionFolders` is an
/// `OperatorIntent of Emission` overlay (operator chooses where a
/// kind's SSDT `.sql` file lands). Lives parallel to other Overrides
/// axes (TableRenames, AllowMissingPrimaryKey); the rewrite fires at
/// the Pipeline-layer realization boundary, outside Π per A18
/// amended.
///
/// **Validation** at bind time, not at use time:
///   - Folder must be non-empty.
///   - Folder must not begin with `/` (absolute path) or match a
///     Windows drive-letter prefix.
///   - Folder must not contain `\\` (forces cross-platform-
///     deterministic forward-slash separators; `SsdtFile.RelativePath`
///     is forward-slash by V2 convention per
///     `SsdtDdlEmitter.relativePath` LINT-ALLOW rationale).
///   - No segment may be empty (no leading slash, trailing slash, or
///     consecutive slashes).
///   - No segment may be `..` (parent traversal forbidden — V2's
///     output directory is V2-owned per
///     `Compose.writeWith` semantics).
///   - No segment may contain platform-reserved chars
///     (`<`, `>`, `:`, `"`, `|`, `?`, `*`, control chars) — these
///     would break SSDT consumers across Windows / Linux / DacFx.

/// Typed runtime form of `Overrides.EmissionFolders`. Each entry
/// remaps one kind's SSDT `.sql` file from its default `Modules/
/// <Module>/` directory to the operator-named folder; the basename
/// (`<Schema>.<Table>.sql`) is preserved. `empty` is the no-overrides
/// default; consuming sites must produce identical bundles for
/// `empty` vs unpopulated.
type EmissionFolders = {
    ByKind : Map<SsKey, string>
}

[<RequireQualifiedAccess>]
module EmissionFolders =

    let empty : EmissionFolders = {
        ByKind = Map.empty
    }

    let isEmpty (folders: EmissionFolders) : bool =
        Map.isEmpty folders.ByKind


[<RequireQualifiedAccess>]
module EmissionFoldersBinding =

    let private bindError = Binding.error ConfigAxis.EmissionFolders

    let private invalidSegmentChars : Set<char> =
        Set.ofList [ '<'; '>'; ':'; '"'; '|'; '?'; '*' ]

    let private segmentHasInvalidChar (segment: string) : bool =
        segment
        |> Seq.exists (fun c ->
            Set.contains c invalidSegmentChars || System.Char.IsControl c)

    /// Detect a Windows-style absolute path (drive letter + colon
    /// prefix, e.g. `C:`). Per V2's cross-platform-deterministic
    /// forward-slash convention, all absolute-path shapes are
    /// rejected.
    let private hasDriveLetterPrefix (folder: string) : bool =
        folder.Length >= 2
        && System.Char.IsLetter(folder.[0])
        && folder.[1] = ':'

    /// Validate one folder string against the segment-shape contract.
    /// Returns the validated folder string on success; structured
    /// `pipeline.emissionFolders.invalidFolder.*` errors on failure.
    /// Each failure carries the offending entry's ref + folder in
    /// the message for operator diagnosis.
    let private validateFolder
        (ref: Config.LogicalName)
        (folder: string)
        : Result<string> =
        let refStr = sprintf "%s.%s" ref.Module ref.Entity
        if System.String.IsNullOrEmpty folder then
            Result.failureOf (
                bindError
                    "invalidFolder.empty"
                    (sprintf "overrides.emissionFolders entry %s has an empty folder." refStr))
        elif folder.StartsWith "/" || hasDriveLetterPrefix folder then
            Result.failureOf (
                bindError
                    "invalidFolder.absolute"
                    (sprintf
                        "overrides.emissionFolders entry %s folder '%s' is absolute; relative folders only."
                        refStr folder))
        elif folder.Contains '\\' then
            Result.failureOf (
                bindError
                    "invalidFolder.backslash"
                    (sprintf
                        "overrides.emissionFolders entry %s folder '%s' contains a backslash; use forward slashes (V2 cross-platform-deterministic convention)."
                        refStr folder))
        else
            let segments = folder.Split('/')
            let segmentErrors =
                segments
                |> Array.mapi (fun i s -> i, s)
                |> Array.choose (fun (i, s) ->
                    if System.String.IsNullOrEmpty s then
                        Some (
                            bindError
                                "invalidFolder.emptySegment"
                                (sprintf
                                    "overrides.emissionFolders entry %s folder '%s' has an empty segment at position %d (leading, trailing, or double slash)."
                                    refStr folder i))
                    elif s = ".." then
                        Some (
                            bindError
                                "invalidFolder.parentTraversal"
                                (sprintf
                                    "overrides.emissionFolders entry %s folder '%s' contains a '..' parent-traversal segment; V2 output is V2-owned."
                                    refStr folder))
                    elif segmentHasInvalidChar s then
                        Some (
                            bindError
                                "invalidFolder.invalidChar"
                                (sprintf
                                    "overrides.emissionFolders entry %s folder '%s' segment '%s' contains a platform-reserved character."
                                    refStr folder s))
                    else None)
                |> Array.toList
            match segmentErrors with
            | [] -> Result.success folder
            | errs -> Error errs

    /// Resolve an operator-supplied `LogicalName` (Module.Entity) to
    /// the matching kind's `SsKey` via the shared `Binding.requireKindByLogical`.
    /// Errors structurally if no kind in `catalog` matches the (module, entity)
    /// logical pair.
    let private resolveKindByLogical
        (catalog: Catalog)
        (ref: Config.LogicalName)
        : Result<SsKey> =
        Binding.requireKindByLogical catalog ref.Module ref.Entity
            (bindError
                "unresolved"
                (sprintf
                    "overrides.emissionFolders entry %s.%s did not match any catalog kind."
                    ref.Module ref.Entity))

    let private bindEntry
        (catalog: Catalog)
        (entry: Config.EmissionFolderEntry)
        : Result<SsKey * string> =
        validation {
            let! key    = resolveKindByLogical catalog entry.Ref
            and! folder = validateFolder entry.Ref entry.Folder
            return (key, folder)
        }

    /// Build the typed `EmissionFolders` runtime value from a parsed
    /// `Config`. Aggregates all binder errors so the operator sees
    /// every malformed entry in one pass. Duplicate refs (two entries
    /// targeting the same kind) take the LAST entry — `Map.ofList`
    /// semantics. The principal-PO accepted this on the parser-side
    /// (no duplicate-entry rejection): an operator hand-editing the
    /// config can reorder freely; duplicate-resolution at the binder
    /// is the simpler shape.
    let fromConfig
        (catalog: Catalog)
        (cfg: Config.Config)
        : Result<EmissionFolders> =
        cfg.Overrides.EmissionFolders
        |> List.map (bindEntry catalog)
        |> Result.aggregate
        |> Result.map (fun pairs ->
            { ByKind = Map.ofList pairs })
