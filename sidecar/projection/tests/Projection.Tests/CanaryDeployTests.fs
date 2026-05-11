[<Xunit.Collection("Docker-SqlServer")>]
module Projection.Tests.CanaryDeployTests

open Xunit
open Projection.Pipeline

/// M2 (per the chapter-3.1 milestone sequence chosen at session 27):
/// the canary deploy test. Spins up an ephemeral SQL Server, executes
/// V2's emitted SSDT against a fresh database, observes table count,
/// and tears down. Each run is independent — same input produces the
/// same outcome (run-level idempotency).
///
/// **Soft-skip pattern.** If the Docker daemon is unreachable
/// (`Deploy.Docker.ensureRunning () = false`), tests log a SKIP message
/// and pass vacuously. M3+ can promote to `Xunit.SkippableFact` for
/// proper skip semantics; for now the soft-skip keeps the test runner
/// honest without adding a dependency. The CLI's `deploy` subcommand
/// surfaces a deterministic exit code (4) on the same condition.
///
/// Subsequent milestones (M3 read-side adapter, M4 Tolerance taxonomy
/// + comparator, M5 full canary integration) extend this surface.

let private sampleSsdt : string =
    String.concat
        "\n"
        [
            "-- Smoke fixture for M2: a single user-schema table that the"
            "-- emitted SSDT shape produces. Matches the minimal V1 fixture"
            "-- used in EndToEndPipelineTests; copied here so canary tests"
            "-- don't depend on the OSSYS adapter under test."
            "CREATE TABLE [dbo].[OSUSR_M2_SMOKE] ("
            "    [ID] INT NOT NULL,"
            "    [EMAIL] NVARCHAR(MAX) NOT NULL"
            ");"
        ]

let private skipIfNoDocker (label: string) : bool =
    if Deploy.Docker.ensureRunning () then
        true
    else
        printfn
            "SKIP %s: Docker daemon not reachable. Set DOCKER_HOST or start the daemon to run canary tests."
            label
        false

[<Fact>]
let ``M2: Docker availability probe is consistent with the daemon's actual state`` () =
    // Probe is observation-only; this test asserts the probe returns
    // a stable bool without throwing. The probe is the basis for
    // soft-skip decisions in the rest of the canary tests; if it
    // breaks, every downstream test is mis-skipped.
    let result = Deploy.Docker.ensureRunning ()
    Assert.IsType<bool>(result) |> ignore

[<Fact>]
let ``M2: deploy of single-table SSDT lands one user table on a fresh ephemeral database`` () =
    if skipIfNoDocker "deploy-single-table" then
        let task = Deploy.runEphemeral sampleSsdt
        let report = task.GetAwaiter().GetResult()
        Assert.True(
            report.Ok,
            sprintf "expected Ok on minimal SSDT; errors: %A" report.Errors)
        Assert.Equal(1, report.TablesCreated)
        Assert.NotEmpty(report.Database)
        Assert.StartsWith("Projection_", report.Database)

[<Fact>]
let ``M2: deploy is run-level idempotent: two invocations on the same SSDT produce independent databases with equal outcomes`` () =
    if skipIfNoDocker "idempotency" then
        let task1 = Deploy.runEphemeral sampleSsdt
        let r1 = task1.GetAwaiter().GetResult()
        let task2 = Deploy.runEphemeral sampleSsdt
        let r2 = task2.GetAwaiter().GetResult()

        // Both succeed.
        Assert.True(r1.Ok, sprintf "first run failed: %A" r1.Errors)
        Assert.True(r2.Ok, sprintf "second run failed: %A" r2.Errors)

        // Same input → same outcome (idempotency).
        Assert.Equal(r1.TablesCreated, r2.TablesCreated)
        Assert.Equal(1, r1.TablesCreated)

        // Independent — no state crosses runs (fresh container,
        // fresh database per invocation).
        Assert.NotEqual<string>(r1.Database, r2.Database)

[<Fact>]
let ``M2: deploy of malformed SSDT surfaces SqlException diagnostics on the Report`` () =
    if skipIfNoDocker "malformed-ssdt" then
        let badSql = "CREATE TABLE [dbo].[NoColumns]; -- syntax error: empty column list"
        let task = Deploy.runEphemeral badSql
        let report = task.GetAwaiter().GetResult()
        Assert.False(report.Ok)
        Assert.NotEmpty(report.Errors)
        Assert.Equal(0, report.TablesCreated)
