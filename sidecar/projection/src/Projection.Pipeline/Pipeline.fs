namespace Projection.Pipeline

open System.IO
open System.Threading.Tasks
open Projection.Core
open Projection.Adapters.Osm
open Projection.Targets.SSDT
open Projection.Targets.Json
open Projection.Targets.Distributions

/// End-to-end pipeline composition: V1 `osm_model.json` →
/// `Projection.Adapters.Osm.CatalogReader.parse` → V2 Catalog →
/// three sibling Π's (RawText / JSON / Distributions) → on-disk
/// artifacts. The dogfood frame (per `VISION.md` §"Chapter 3 plan"
/// and `DECISIONS 2026-05-22 — Chapter 3 sequencing`): V2 verifies
/// V1 by parsing V1's JSON, projecting through V2's IR, and emitting
/// the chorus.
///
/// This module is the composition surface; `Program.fs` owns argv
/// parsing and exit codes. Tests bypass `Program.fs` and invoke
/// `Pipeline.run` directly with a `Composition` value, so the
/// golden-file regression surface exercises the same code path the
/// CLI does.
[<RequireQualifiedAccess>]
module Compose =

    /// Per-emitter output captured into memory before being written to
    /// disk. Tests assert against these strings without round-tripping
    /// through the file system.
    type Outputs =
        {
            /// Raw .sql text from `RawTextEmitter` — the synthetic-
            /// milestone SSDT shape per `DECISIONS 2026-05-06 — Π_SSDT
            /// first emission target is raw .sql-style text`.
            Sql : string
            /// V2 IR JSON from `JsonEmitter`.
            Json : string
            /// Distributions JSON from `DistributionsEmitter`,
            /// consuming `Profile.empty` since the dogfood frame does
            /// not yet thread profile evidence end-to-end.
            Distributions : string
        }

    /// Per-artifact relative path. Centralized so tests and the CLI
    /// agree on naming.
    [<RequireQualifiedAccess>]
    module ArtifactPath =
        [<Literal>]
        let sql = "projection.sql"
        [<Literal>]
        let json = "projection.json"
        [<Literal>]
        let distributions = "distributions.json"

    /// Run the three sibling Π's against a Catalog. Pure: same Catalog
    /// → same Outputs (T1 byte-determinism). Profile is `Profile.empty`
    /// for the dogfood frame; M2 onward will thread real profile
    /// evidence.
    let project (catalog: Catalog) : Outputs =
        use _ = Bench.scope "compose.project"
        let sql =
            (use _ = Bench.scope "emit.rawText"
             RawTextEmitter.emit catalog)
        let json =
            (use _ = Bench.scope "emit.json"
             JsonEmitter.emit catalog)
        let distributions =
            (use _ = Bench.scope "emit.distributions"
             DistributionsEmitter.emit catalog Profile.empty)
        {
            Sql = sql
            Json = json
            Distributions = distributions
        }

    /// Read a V1 `osm_model.json` from disk and parse it into a V2
    /// Catalog. Errors are surfaced via the codebase's single-arity
    /// `Result<'a>` (see `Result.fs` arity-coexistence note).
    let read (jsonPath: string) : Task<Result<Catalog>> =
        task {
            use _ = Bench.scope "compose.read"
            return! CatalogReader.parse (CatalogReader.SnapshotFile jsonPath)
        }

    /// Read a V1 `osm_model.json` from an in-memory string and parse
    /// it into a V2 Catalog. Used by the golden-file test to keep the
    /// regression surface hermetic.
    let readJson (json: string) : Task<Result<Catalog>> =
        task {
            use _ = Bench.scope "compose.readJson"
            return! CatalogReader.parse (CatalogReader.SnapshotJson json)
        }

    /// Write the three artifacts to a directory. Creates the directory
    /// if it does not exist. Returns the absolute paths of the written
    /// files for operator-side validation.
    let write (outputDir: string) (outputs: Outputs) : string list =
        use _ = Bench.scope "compose.write"
        Directory.CreateDirectory outputDir |> ignore
        let sqlPath = Path.Combine(outputDir, ArtifactPath.sql)
        let jsonPath = Path.Combine(outputDir, ArtifactPath.json)
        let distributionsPath = Path.Combine(outputDir, ArtifactPath.distributions)
        File.WriteAllText(sqlPath, outputs.Sql)
        File.WriteAllText(jsonPath, outputs.Json)
        File.WriteAllText(distributionsPath, outputs.Distributions)
        [ sqlPath; jsonPath; distributionsPath ]

    /// Full end-to-end: read V1 JSON from disk, project, write
    /// artifacts to the output directory. Returns the artifact paths
    /// on success; surfaces parse failures via the result.
    let run (jsonPath: string) (outputDir: string) : Task<Result<string list>> =
        task {
            let! parsed = read jsonPath
            match parsed with
            | Success catalog ->
                let outputs = project catalog
                let paths = write outputDir outputs
                return Result.success paths
            | Failure errors ->
                return Result.failure errors
        }
