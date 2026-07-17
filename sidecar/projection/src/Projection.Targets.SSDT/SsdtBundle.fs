namespace Projection.Targets.SSDT

open Projection.Core

/// Π_SSDT-Bundle composition layer — chapter 4.1.A slice 10 substantive
/// deliverable. Composes the production-deployment SSDT directory bundle
/// V2 emits under V2-driver mode: per-table .sql files (from
/// `SsdtDdlEmitter.emitSlices`) + manifest.json (from
/// `ManifestEmitter.toJson`) into one `Map<RelativePath, string>`.
///
/// **F# core never touches the file system.** This module produces the
/// in-memory directory representation; a downstream Pipeline / CLI host
/// consumes the map and writes the files (per chapter pre-scope §2 +
/// A35 chapter-3.1 contribution: Π's canonical output is a typed
/// deterministic map; realization layers consume the map and choose
/// their emission form).
///
/// **V0 slice 10 scope (per chapter 4.1.A pre-scope §8 slice 10 +
/// V2-driver KPI smart-product-choices):** the in-flight composition
/// covers `(ArtifactByKind<SsdtFile>, Manifest)`. The RefactorLog
/// conditional integration ("if `CatalogDiff` carries renames, include
/// `<projectName>.refactorlog`") + post-deploy split (Tolerance
/// .PostDeployForeignKeys = true) defer to follow-on slices when chapter
/// 3.5 cross-version diff threading + Tolerance taxonomy (M4) land.
/// The chapter 4.1.A close ritual operates with v0; subsequent chapters
/// reopen for the conditional surfaces.
///
/// **Why a separate file:** F# compile order in `Projection.Targets.SSDT
/// .fsproj` puts `Render.fs` before the Π emitter modules
/// (`SsdtDdlEmitter.fs`, `ManifestEmitter.fs`); composition must load
/// after both. `SsdtBundle` is the natural concept-shaped name (per
/// pillar 8) for "the SSDT directory bundle V2 emits" — the bundle IS
/// the composed artifact.
[<RequireQualifiedAccess>]
module SsdtBundle =

    /// Compose the SSDT directory bundle from Π port outputs. Per A35
    /// (chapter 3.1 contribution): Π's canonical output is a typed
    /// deterministic map; this composition layer IS the realization
    /// that produces the in-memory directory representation; downstream
    /// hosts choose the file-system emission form.
    ///
    /// The `manifest.json` always lives at the directory root; per-
    /// table .sql files live at their `SsdtFile.RelativePath` per V1
    /// convention (cross-platform-deterministic forward slashes);
    /// per-schema `Schemas/<name>.sql` files (G6, DECISIONS 2026-07-16)
    /// carry the non-dbo `CREATE SCHEMA` objects — empty for dbo-only
    /// estates (the byte-identical default).
    let composeWithSchemas
        (schemaFiles: (string * string) list)
        (ssdtFiles: ArtifactByKind<SsdtDdlEmitter.SsdtFile>)
        (manifest: ManifestEmitter.Manifest)
        : Map<string, string> =
        use _ = Bench.scope "ssdt.bundle.compose"
        let perTableEntries =
            ssdtFiles
            |> ArtifactByKind.toMap
            |> Map.toSeq
            |> Seq.map (fun (_ssKey, file) -> file.RelativePath, file.Body)
        let manifestEntry = "manifest.json", ManifestEmitter.toJson manifest
        Seq.append (Seq.ofList schemaFiles) (Seq.append perTableEntries (Seq.singleton manifestEntry))
        |> Map.ofSeq

    /// The schema-less form — the pre-G6 shape, byte-identical; callers
    /// with a catalog in hand thread `SsdtDdlEmitter.schemaFiles` via
    /// `composeWithSchemas`.
    let compose
        (ssdtFiles: ArtifactByKind<SsdtDdlEmitter.SsdtFile>)
        (manifest: ManifestEmitter.Manifest)
        : Map<string, string> =
        composeWithSchemas [] ssdtFiles manifest
