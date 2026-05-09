module Projection.Tests.GeneratorScaleTests

open Xunit
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
    if Deploy.Docker.isAvailable () then
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
            Deploy.runWideCanary fixture.Combined RawTextEmitter.emit
        let result = task.GetAwaiter().GetResult()
        match result with
        | Success report -> Some report
        | Failure errors ->
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
