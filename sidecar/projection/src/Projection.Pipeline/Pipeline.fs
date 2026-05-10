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
    /// disk. Tests assert against these values without round-tripping
    /// through the file system.
    ///
    /// Per the chapter 4.1.A close arc + Tier-1 #2 RawTextEmitter
    /// retirement transition: `SsdtBundle` is the production-shape
    /// per-table file map (per `SsdtBundle.compose`) — `Map<RelativePath
    /// , string>` containing one `.sql` per kind plus `manifest.json`.
    /// This retires the chapter-3-era `Sql : string` single-blob
    /// dogfood output in favor of the per-table file shape the
    /// operator's Azure DevOps pipeline consumes.
    type Outputs =
        {
            /// Production-shape SSDT bundle: per-kind `.sql` files
            /// (under `Modules/<Module>/<Schema>.<Table>.sql` per V1
            /// convention) + `manifest.json` (per V1 SsdtManifest
            /// schema). Keyed by relative path; values are file
            /// contents. Iterate to write to disk; consumers needing
            /// a single concatenated SQL string call `aggregateSsdt`.
            SsdtBundle : Map<string, string>
            /// V2 IR JSON from `JsonEmitter`. Distinct from
            /// `manifest.json` (the V1-mirror SSDT manifest).
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
        let json = "projection.json"
        [<Literal>]
        let distributions = "distributions.json"

    /// Aggregate the SSDT bundle's per-table SQL files into one
    /// concatenated SQL string (manifest.json excluded; iterates the
    /// bundle in deterministic SsKey-derived order via Map's natural
    /// ordering). Used by Deploy.runEphemeral and any consumer that
    /// needs the single-string deploy form (e.g., the V1↔V2
    /// differential dogfood tests). The per-file shape stays canonical
    /// for production deploys.
    let aggregateSsdt (bundle: Map<string, string>) : string =
        bundle
        |> Map.toSeq
        |> Seq.filter (fun (path, _) -> path.EndsWith(".sql"))
        |> Seq.map snd
        |> String.concat "\nGO\n"  // LINT-ALLOW: terminal SQL-batch joiner across per-table SsdtBundle entries; segments are typed (each `Body` is the rendered ScriptDom output from SsdtDdlEmitter); BCL `String.concat` IS the use-case-specific library at the SQL-batch concatenation boundary

    /// Run the three sibling Π's against a Catalog. Pure: same Catalog
    /// → same Outputs (T1 byte-determinism). Profile is `Profile.empty`
    /// for the dogfood frame; M2 onward will thread real profile
    /// evidence.
    let project (catalog: Catalog) : Outputs =
        use _ = Bench.scope "compose.project"
        let bundle =
            (use _ = Bench.scope "emit.ssdtBundle.compose"
             match SsdtDdlEmitter.emitSlices catalog with
             | Ok ssdtFiles ->
                 let manifest = ManifestEmitter.emit catalog
                 SsdtBundle.compose ssdtFiles manifest
             | Error err ->
                 // Unreachable in production paths: Catalog.allKinds is
                 // the keyset by construction; SsdtDdlEmitter.emitSlices
                 // produces one slice per kind (smart-constructor
                 // strict-equality); error states surface only on
                 // pathological catalogs that bypass smart constructors.
                 invalidOp (sprintf "Compose.project: SsdtDdlEmitter.emitSlices: %A" err))
        let json =
            (use _ = Bench.scope "emit.json"
             JsonEmitter.emit catalog)
        let distributions =
            (use _ = Bench.scope "emit.distributions"
             DistributionsEmitter.emit catalog Profile.empty)
        {
            SsdtBundle    = bundle
            Json          = json
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

    /// Write the SSDT bundle (per-kind .sql + manifest.json) plus the
    /// V2 IR JSON + Distributions artifacts to a directory. Creates
    /// the directory + bundle subdirectories as needed. Returns the
    /// absolute paths of every written file for operator-side
    /// validation.
    let write (outputDir: string) (outputs: Outputs) : string list =
        use _ = Bench.scope "compose.write"
        Directory.CreateDirectory outputDir |> ignore
        let bundlePaths =
            outputs.SsdtBundle
            |> Map.toList
            |> List.map (fun (relPath, body) ->
                let absPath = Path.Combine(outputDir, relPath)
                match Path.GetDirectoryName absPath with
                | null -> ()
                | parent when System.String.IsNullOrEmpty parent -> ()
                | parent -> Directory.CreateDirectory parent |> ignore
                File.WriteAllText(absPath, body)
                absPath)
        let jsonPath = Path.Combine(outputDir, ArtifactPath.json)
        let distributionsPath = Path.Combine(outputDir, ArtifactPath.distributions)
        File.WriteAllText(jsonPath, outputs.Json)
        File.WriteAllText(distributionsPath, outputs.Distributions)
        bundlePaths @ [ jsonPath; distributionsPath ]

    /// Full end-to-end: read V1 JSON from disk, project, write
    /// artifacts to the output directory. Returns the artifact paths
    /// on success; surfaces parse failures via the result.
    let run (jsonPath: string) (outputDir: string) : Task<Result<string list>> =
        task {
            let! parsed = read jsonPath
            match parsed with
            | Ok catalog ->
                let outputs = project catalog
                let paths = write outputDir outputs
                return Result.success paths
            | Error errors ->
                return Result.failure errors
        }
