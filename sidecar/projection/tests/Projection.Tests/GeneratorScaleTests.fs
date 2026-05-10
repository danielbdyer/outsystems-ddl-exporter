module Projection.Tests.GeneratorScaleTests

open Xunit
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Pipeline
open Projection.Targets.SSDT
open Projection.Tests.SourceFixtures

/// Per session-31 operator framing — "what about with 200 entities
/// and 100 static entities" — these tests exercise the canary's
/// round-trip property against procedurally-generated fixtures of
/// varying sizes. The bench surface (`Projection.Core.Bench`)
/// captures per-label scaling so we can see *which* labels grow
/// linearly with N and which stay constant.
///
/// The forcing function from VISION.md is a 300-table OutSystems
/// 11 system. The `realistic` spec generates 200 + 100 = 300
/// tables. Smaller specs (`small` ~12, `medium` ~75) are the dev-
/// loop fast lane; the realistic run is for periodic full-scale
/// verification (likely too slow for every test invocation —
/// gated to the canary CLI rather than the unit-test suite).

let private skipIfNoDocker (label: string) : bool =
    if Deploy.Docker.ensureRunning () then
        true
    else
        printfn
            "SKIP %s: Docker daemon not reachable. Set DOCKER_HOST or start the daemon."
            label
        false

/// Per session-33 — the canary runs against `fixture.Combined`
/// (DDL + seed-data INSERTs) instead of `fixture.Ddl` alone, so the
/// source DB carries variegated rows in static tables. ReadSide
/// projects those rows into V2 IR via `Kind.Modality = [Static …]`,
/// the emitter re-emits them as INSERTs, the target DB receives them,
/// and `PhysicalSchema.Rows` (SHA256-hashed) flags any drift.
let private runCanaryAgainst
    (label: string)
    (spec: GenerateSpec)
    : Deploy.WideCanaryReport option =
    if not (skipIfNoDocker label) then
        None
    else
        let fixture = FixtureGenerator.generate spec
        printfn
            "Generated fixture: %d tables, %d bytes of DDL, %d bytes of seed data, %d seed rows"
            fixture.TableCount
            fixture.Ddl.Length
            fixture.SeedData.Length
            fixture.SeedRowCount
        let task =
            Deploy.runWideCanary fixture.Combined RawTextEmitter.statements
        let result = task.GetAwaiter().GetResult()
        match result with
        | Ok report -> Some report
        | Error errors ->
            let codes = errors |> List.map (fun e -> e.Code) |> String.concat ", "
            Assert.Fail(sprintf "%s: canary failed: %s" label codes)
            None

/// Per session-35 — bulk-source variant. Schema deploys via
/// `executeBatch` (small text); each static table's seed rows
/// flow through `Bulk.copyRows` (SqlBulkCopy with KeepIdentity).
/// Same canary contract; ~10x faster source loading at 100k+ row
/// scale because we skip the text-INSERT round-trips entirely.
let private runBulkLoaderCanaryAgainst
    (label: string)
    (spec: GenerateSpec)
    : Deploy.WideCanaryReport option =
    if not (skipIfNoDocker label) then
        None
    else
        let fixture = FixtureGenerator.generate spec
        printfn
            "Generated fixture (bulk): %d tables, %d bytes of DDL, %d bulk seeds, %d seed rows"
            fixture.TableCount
            fixture.Ddl.Length
            fixture.BulkSeeds.Length
            fixture.SeedRowCount
        let loadSource (cnn: SqlConnection) =
            task {
                use _ = Bench.scope "fixture.bulkLoader"
                do! Deploy.executeBatch cnn fixture.Ddl
                for seed in fixture.BulkSeeds do
                    do! Bulk.copyRows cnn seed.Table seed.Rows
            }
        let task =
            Deploy.runWideCanaryWithLoader loadSource RawTextEmitter.statements
        let result = task.GetAwaiter().GetResult()
        match result with
        | Ok report -> Some report
        | Error errors ->
            let codes = errors |> List.map (fun e -> e.Code) |> String.concat ", "
            Assert.Fail(sprintf "%s: canary failed: %s" label codes)
            None

let private summarizeDiff (label: string) (report: Deploy.WideCanaryReport) : unit =
    printfn
        "%s canary: source=%d tables, target=%d tables; diff missing-cols=%d, extra-cols=%d, missing-fks=%d, extra-fks=%d, missing-rows=%d, extra-rows=%d"
        label
        report.SourceReport.TablesCreated
        report.TargetReport.TablesCreated
        (List.length report.Diff.MissingColumns)
        (List.length report.Diff.ExtraColumns)
        (List.length report.Diff.MissingForeignKeys)
        (List.length report.Diff.ExtraForeignKeys)
        (List.length report.Diff.MissingRows)
        (List.length report.Diff.ExtraRows)

[<Fact>]
let ``Generator scale: SMALL fixture (~12 tables) round-trips green with FK round-trip enabled`` () =
    match runCanaryAgainst "small" GenerateSpec.small with
    | None -> ()
    | Some report ->
        summarizeDiff "small" report
        Assert.True(
            report.SourceReport.TablesCreated > 0,
            "source should deploy at least one table")
        Assert.Equal(
            report.SourceReport.TablesCreated,
            report.TargetReport.TablesCreated)
        // Per session-31 Session B: FK round-trip is now closed.
        // The diff should be empty across all four axes.
        Assert.True(
            PhysicalSchema.isEqual report.Diff,
            sprintf
                "small fixture round-trip failed:\n%s"
                (PhysicalSchema.renderDiff report.Diff))

[<Fact>]
let ``Generator scale: MEDIUM fixture (~75 tables) round-trips green with FK round-trip`` () =
    match runCanaryAgainst "medium" GenerateSpec.medium with
    | None -> ()
    | Some report ->
        summarizeDiff "medium" report
        Assert.Equal(
            report.SourceReport.TablesCreated,
            report.TargetReport.TablesCreated)
        Assert.True(
            PhysicalSchema.isEqual report.Diff,
            sprintf
                "medium fixture round-trip failed:\n%s"
                (PhysicalSchema.renderDiff report.Diff))

/// Per session-34 — bulk-path stress canaries. Five static tables
/// × N rows per table validates the `Deploy.executeStream` /
/// `Bulk.copyRows` realization layer at enterprise volumes. The
/// canary's six-axis empty diff property holds; the bench surface
/// surfaces throughput (`deploy.bulk.copyRows.batchSize`,
/// `deploy.executeStream.input.elements`) so cross-run regression
/// is structural. Each gated on `PROJECTION_RUN_BULK_CANARY=1`.
let private runBulkCanary (label: string) (spec: GenerateSpec) : unit =
    let envVar =
        match System.Environment.GetEnvironmentVariable "PROJECTION_RUN_BULK_CANARY" with
        | null -> ""
        | v -> v
    if envVar <> "1" then
        printfn "SKIP bulk canary [%s]: set PROJECTION_RUN_BULK_CANARY=1 to run." label
    else
        Bench.reset ()
        match runBulkLoaderCanaryAgainst label spec with
        | None -> ()
        | Some report ->
            summarizeDiff label report
            Assert.Equal(
                report.SourceReport.TablesCreated,
                report.TargetReport.TablesCreated)
            Assert.True(
                PhysicalSchema.isEqual report.Diff,
                sprintf
                    "bulk canary [%s] failed:\n%s"
                    label
                    (PhysicalSchema.renderDiff report.Diff))
            // First-class stream observability: surface the bench
            // table at end so operators see throughput per realization
            // layer (Π statement stream → executeStream input →
            // bulk.copyRows batches) on the same run.
            let top =
                Bench.snapshot ()
                |> List.truncate 25
            printfn "\nBench (top 25, label=%s):\n%s\n" label (Bench.renderTable top)

/// **Operator-reality canary** — the chapter-3.6 perf-gate baseline
/// (per the 2026-05-09 operator directive: "canary-gate.sql is
/// inappropriate for stop hooks ... 50k records, variegated, 300
/// tables. Full stop."). Always runs (no env-var gating) so the
/// pre-commit hook + Stop hook gate every commit / agent stop on
/// production-shape fidelity AND production-shape perf. **Persists
/// bench JSON** to `bench/canary/<utc>.json` via `BenchSink.persistJson`
/// so the perf-gate's statistical comparator can pick up the latest
/// snapshot under the "canary" tag and compare against the rolling
/// history. Bench history at `bench/history-canary.jsonl`.
[<Fact>]
let ``Operator-reality canary: 50k rows × 300 tables, variegated, round-trips via bulk path`` () =
    if not (Deploy.Docker.ensureRunning ()) then
        printfn
            "SKIP operator-reality canary: Docker daemon not reachable. Set DOCKER_HOST or start the daemon."
    else
        Bench.reset ()
        match runBulkLoaderCanaryAgainst "operatorReality" GenerateSpec.operatorReality with
        | None -> ()
        | Some report ->
            summarizeDiff "operatorReality" report
            Assert.Equal(
                report.SourceReport.TablesCreated,
                report.TargetReport.TablesCreated)
            Assert.True(
                PhysicalSchema.isEqual report.Diff,
                sprintf
                    "operator-reality canary failed:\n%s"
                    (PhysicalSchema.renderDiff report.Diff))
            // Persist bench snapshot to the same path convention the CLI
            // canary uses, so `scripts/perf-gate.sh` picks it up and
            // gates against `bench/baseline-canary.json` per the
            // statistical `μ + Kσ` model.
            //
            // Path resolution: the test process's `Directory
            // .GetCurrentDirectory()` is the test bin dir
            // (`tests/Projection.Tests/bin/Release/net9.0/`), NOT the
            // projection root. Use the env var `PROJECTION_BENCH_DIR`
            // (set by `scripts/perf-gate.sh`) when present; fallback
            // to walking up from the test assembly to find the
            // `bench/` directory adjacent to `Projection.sln`.
            let stats = Bench.snapshot ()
            let benchRoot =
                match System.Environment.GetEnvironmentVariable "PROJECTION_BENCH_DIR" with
                | null | "" ->
                    // Walk up from the test bin dir to find the
                    // projection root (where `Projection.sln` lives).
                    let rec findRoot (dir: System.IO.DirectoryInfo | null) : string =
                        match dir with
                        | null -> System.IO.Directory.GetCurrentDirectory()
                        | d when System.IO.File.Exists(System.IO.Path.Combine(d.FullName, "Projection.sln")) ->
                            d.FullName
                        | d -> findRoot d.Parent
                    findRoot (System.IO.DirectoryInfo(System.IO.Directory.GetCurrentDirectory()))
                | v -> v
            let path = BenchSink.defaultPath benchRoot "canary"
            BenchSink.persistJson path "operatorReality" stats
            printfn "operatorReality bench snapshot: %s" path

[<Fact>]
let ``Generator bulk: 1k rows/table (5 tables = 5k rows) round-trips via bulk path`` () =
    runBulkCanary "bulk1k" GenerateSpec.bulk1k

[<Fact>]
let ``Generator bulk: 10k rows/table (5 tables = 50k rows) round-trips via bulk path`` () =
    runBulkCanary "bulk10k" GenerateSpec.bulk10k

[<Fact>]
let ``Generator bulk: 100k rows/table (5 tables = 500k rows) round-trips via bulk path`` () =
    runBulkCanary "bulk100k" GenerateSpec.bulk100k

/// Per session-33 — the 300-table forcing-function canary, gated
/// behind `PROJECTION_RUN_REALISTIC_CANARY` so the unit-test loop
/// stays fast. CI / SessionEnd may set the env var to exercise the
/// full surface; the dev loop runs it on demand. The test asserts
/// the same six-axis empty diff property as the smaller sizes —
/// the forcing function's value is wall-time bench data, not new
/// failure surfaces.
[<Fact>]
let ``Generator scale: REALISTIC fixture (300 tables) round-trips green when PROJECTION_RUN_REALISTIC_CANARY=1`` () =
    let envVar =
        match System.Environment.GetEnvironmentVariable "PROJECTION_RUN_REALISTIC_CANARY" with
        | null -> ""
        | v -> v
    if envVar <> "1" then
        printfn "SKIP realistic canary: set PROJECTION_RUN_REALISTIC_CANARY=1 to run."
    else
        match runCanaryAgainst "realistic" GenerateSpec.realistic with
        | None -> ()
        | Some report ->
            summarizeDiff "realistic" report
            Assert.Equal(300, report.SourceReport.TablesCreated)
            Assert.Equal(
                report.SourceReport.TablesCreated,
                report.TargetReport.TablesCreated)
            Assert.True(
                PhysicalSchema.isEqual report.Diff,
                sprintf
                    "realistic fixture round-trip failed:\n%s"
                    (PhysicalSchema.renderDiff report.Diff))

[<Fact>]
let ``Generator: deterministic output — same seed produces byte-identical DDL`` () =
    let f1 = FixtureGenerator.generate GenerateSpec.small
    let f2 = FixtureGenerator.generate GenerateSpec.small
    Assert.Equal(f1.Ddl, f2.Ddl)
    Assert.Equal(f1.TableCount, f2.TableCount)

[<Fact>]
let ``Generator: spec scaling — table count matches Entities + StaticEntities`` () =
    let small = FixtureGenerator.generate GenerateSpec.small
    Assert.Equal(GenerateSpec.small.Entities + GenerateSpec.small.StaticEntities, small.TableCount)
    let medium = FixtureGenerator.generate GenerateSpec.medium
    Assert.Equal(GenerateSpec.medium.Entities + GenerateSpec.medium.StaticEntities, medium.TableCount)
    let realistic = FixtureGenerator.generate GenerateSpec.realistic
    Assert.Equal(GenerateSpec.realistic.Entities + GenerateSpec.realistic.StaticEntities, realistic.TableCount)
    Assert.Equal(300, realistic.TableCount)
