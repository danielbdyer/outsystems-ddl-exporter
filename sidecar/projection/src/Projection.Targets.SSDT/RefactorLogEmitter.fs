namespace Projection.Targets.SSDT

open Projection.Core

/// Π_RefactorLog — chapter 3.5 substantive deliverable. The fourth
/// sibling Π. Consumes a `CatalogDiff` (per `EmitterOverDiff
/// <'element>` declared at `Types.fs:62-63`) and produces typed
/// refactor-log entries — the structural input to SSDT's
/// `.refactorlog` XML document that DacFx's incremental deploy
/// planner reads to convert `DROP COLUMN` + `ADD COLUMN` into
/// `sp_rename`.
///
/// First-slice scope: kind-level renames (i.e., table renames in
/// SSDT vocabulary). The `CatalogDiff` is kind-level (per chapter
/// 3.5 prescope §2 first-slice scope); each `Renamed` entry produces
/// one `RefactorLogEntry` of `ElementType = SqlTable`. Column-level
/// renames defer to a follow-on slice gated on `CatalogDiff` carrying
/// attribute-level keys (the closed-DU empirical-test discipline
/// applies — adding `SqlSimpleColumn` exercises the variant only at
/// match sites within this module).
///
/// **Per A35 / T11 amended again (diff-typed inputs)**: T11
/// (sibling-Π commutativity) extends to diff-typed inputs by typing
/// `ArtifactByKind` over the diff's *target* Catalog. Every kind in
/// the target is in the artifact's keyset; renamed kinds carry
/// non-empty entry lists; everything else carries the empty list.
/// The chapter 3.5 close cashes the amendment.
///
/// **Per A18**: the emitter consumes `CatalogDiff` (which carries
/// `Source: Catalog` and `Target: Catalog` as fields) — never
/// `Policy`. Catalog and Profile are evidence; Policy is intent.

/// SSDT-native operation kind. The `.refactorlog` XML format
/// supports several refactor kinds (`RenameRefactor`,
/// `MoveSchemaRefactor`, `WildcardExpansionRefactor`,
/// `ChangeColumnTypeRefactor`); chapter 3.5 first slice ships
/// `RenameRefactor` only. New variants land under closed-DU
/// expansion empirical-test discipline when a real consumer
/// surfaces them.
type RefactorOperationKind =
    | RenameRefactor

/// SSDT element-type discriminator. SSDT enumerates these as
/// strings (`SqlTable`, `SqlSimpleColumn`, `SqlSchema`, …); V2
/// keeps a closed DU and renders to string at emission time.
/// First slice carries the three variants that table-level rename
/// evidence touches; column-level renames extend the DU with
/// further parent shapes if they materialize.
type RefactorElementType =
    | SqlTable
    | SqlSimpleColumn
    | SqlSchema

/// One record's worth of refactor evidence — exactly what becomes
/// one `<Operation>` element in the rendered `.refactorlog` XML.
/// `OperationKey` is the deterministic UUIDv5-derived stable
/// identifier; two emit runs against the same `CatalogDiff` produce
/// the same `OperationKey` (T1).
type RefactorLogEntry =
    {
        /// Deterministic UUIDv5(`namespace`, `"rename:<rootOriginal>:<oldName>:<newName>"`).
        /// Stable across emit runs; SSDT's GUI generates random
        /// GUIDs but DacFx accepts any stable Guid as long as it's
        /// unique per operation.
        OperationKey      : System.Guid
        OperationKind     : RefactorOperationKind
        /// SSDT element reference. Bracket-quoted form per
        /// `[schema].[table]` (table) or `[schema].[table].[col]`
        /// (column).
        ElementName       : string
        ElementType       : RefactorElementType
        /// Owner of the element — the schema for a table, the
        /// table for a column.
        ParentElementName : string
        ParentElementType : RefactorElementType
        /// The new (post-rename) leaf name — the new table name
        /// for a table rename; the new column name for a column
        /// rename.
        NewName           : string
        /// Pass version of the emitter that produced this entry,
        /// per `RefactorLogEmitter.version`.
        PassVersion       : int
    }

[<RequireQualifiedAccess>]
module RefactorLogEmitter =

    /// Emitter version. Bumped when the entry shape changes meaning
    /// (e.g., when column-level renames land). Stamped onto every
    /// `RefactorLogEntry` so cross-version triage can distinguish
    /// entries from older emit-versions.
    [<Literal>]
    let version : int = 1

    /// Namespace Guid for refactor-log `OperationKey` derivation.
    /// This is the **stable V2-side namespace** for refactor-log
    /// identity threading — once chosen, never change without an
    /// explicit DECISIONS amendment. Per the chapter 3.5 prescope
    /// §10 risk R3 mitigation, the namespace is the cross-version
    /// disambiguator; changing it invalidates every `OperationKey`
    /// ever emitted.
    [<Literal>]
    let private namespaceString : string =
        "5d3f9f5c-1a2b-4c8d-9e7f-3b6a8c4d2e1f"

    let namespaceGuid : System.Guid =
        System.Guid.Parse namespaceString

    /// `[schema]` — bracket-quoted schema name. SSDT's `SqlSchema`
    /// element-name convention.
    let private schemaQualified (table: TableId) : string =
        Render.quote table.Schema

    /// Derive a deterministic `OperationKey` for a kind-level rename.
    /// Same diff → same `(rootOriginal, OldName, NewName)` triple →
    /// same UUIDv5 → same `OperationKey` (T1).
    ///
    /// The basis-string is composed via `String.concat` rather than
    /// `sprintf` per the codebase's no-string-concatenation discipline:
    /// structured composition over format strings, built-in collection
    /// joiners over manual interpolation. The component list `[ "rename";
    /// rootOriginal; oldName; newName ]` is the typed quadruple; the
    /// `":"` separator is the field delimiter.
    let private renameOperationKey
        (kindKey: SsKey)
        (record: RenameRecord)
        : System.Guid =
        let basis =
            [
                "rename"
                SsKey.rootOriginal kindKey
                Name.value record.OldName
                Name.value record.NewName
            ]
            |> String.concat ":"
        UuidV5.create namespaceGuid basis

    /// Build the per-kind refactor-entry slice for one kind. Returns
    /// the empty list when the kind is not renamed in the diff
    /// (`Unchanged`, `Added`, or `Removed` partitions); a one-entry
    /// list when the kind is in `Renamed`.
    ///
    /// Per T11 amended again, the artifact's keyset is the *target*
    /// Catalog's kind set — every kind in the target appears, with
    /// possibly empty per-kind evidence. Empty per-key payload still
    /// satisfies T11 (the keyset is what the structural type
    /// guarantees, not non-emptiness of values).
    let private kindRefactorEntries
        (renames: Map<SsKey, RenameRecord>)
        (k: Kind)
        : RefactorLogEntry list =
        match Map.tryFind k.SsKey renames with
        | None -> []
        | Some record ->
            let target : TableId =
                { Schema = k.Physical.Schema; Table = k.Physical.Table }
            [
                {
                    OperationKey = renameOperationKey k.SsKey record
                    OperationKind = RenameRefactor
                    ElementName = TableId.qualified target
                    ElementType = SqlTable
                    ParentElementName = schemaQualified target
                    ParentElementType = SqlSchema
                    NewName = Name.value record.NewName
                    PassVersion = version
                }
            ]

    /// Π port realization for the diff-typed sibling. Per
    /// `EmitterOverDiff<RefactorLogEntry list>`, the artifact's
    /// keyset is the diff's *target* Catalog's kind set —
    /// `ArtifactByKind.create` enforces strict equality between the
    /// slice's keys and `Catalog.allKinds target`'s SsKey set.
    /// T11 (sibling-Π commutativity, structural type encoding,
    /// extended to diff-typed inputs) holds by construction: any two
    /// `ArtifactByKind` values built from the same target Catalog
    /// have equal keysets.
    ///
    /// Big-O: O(N log N) where N = |target kinds|. `Catalog.allKinds`
    /// is O(N); per-kind `Map.tryFind` lookup is O(log R) where R =
    /// |renames|; `ArtifactByKind.create`'s set-difference
    /// O(N log N). Renames map is amortized O(R) for a kind-by-kind
    /// scan (each lookup O(log R), total O(N log R)).
    let emit : EmitterOverDiff<RefactorLogEntry list> = fun diff ->
        use _ = Bench.scope "emit.refactorLog.emit"
        let target = CatalogDiff.target diff
        let renames = CatalogDiff.renamed diff
        let slices =
            Catalog.allKinds target
            |> List.map (fun k -> k.SsKey, kindRefactorEntries renames k)
            |> Map.ofList
        ArtifactByKind.create target slices
