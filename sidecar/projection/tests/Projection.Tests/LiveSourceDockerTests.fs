namespace Projection.Tests

// Docker-gated end-to-end witness for the LIVE-SOURCE path
// (`PLAN_2026_06_14_LIVE_SOURCE_AND_BOOTSTRAP.md` §2). The live-source
// adapter (`Source.ofLive`; `Ref` `live:` resolution) and `compare`
// live-profiling shipped build-verified; these tests prove they work
// end-to-end against a real SQL Server — the witness the build-only
// verification could not give.
//
// The pure capability surface is already covered (`SourceTests` —
// `canProfile` false for a snapshot, true for a live source;
// `CompareTests` — a no-profile operand is advisory-silent). What needs
// a real database, and is asserted here:
//
//   1. **Live catalog readback** — `live:<connStr>` resolves through
//      `Ref.resolveCatalog` → `Source.ofLive` → `ReadSide.read`, and the
//      reconstructed `Catalog` carries the deployed table.
//   2. **`compare` live-profiling dealbreaker** — A's live data is
//      profiled against B's declared model, and a row in A that violates
//      B's NOT NULL declaration surfaces as a data dealbreaker. This is
//      the test that catches the 4.4 trap: `ReadSide` marks every
//      reconstructed kind `Static`, and `LiveProfiler` SKIPS static kinds
//      — so without `Source.ofLive` stripping the Static mark before it
//      profiles, the dealbreaker section would be silently empty and
//      `compare`'s live-profiling feature would be inert. The assertion
//      `DataDealbreakers` is NON-empty is the executable guard on that.
//
// Soft-skip on `Deploy.Docker.ensureRunning ()` (mirrors
// `CanaryRoundTripTests`). `IClassFixture<EphemeralContainerFixture>`
// shares one container; each test (and each operand) takes its own
// `<prefix>_<guid>` database via `WithEphemeralDatabase`.

open Microsoft.Data.SqlClient
open Xunit
open Projection.Core
open Projection.Pipeline

[<Xunit.Collection("Docker-SqlServer")>]
type LiveSourceDockerTests(fixture: EphemeralContainerFixture) =

    let skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else
            printfn "SKIP %s: Docker daemon not reachable." label
            false

    let mustOk (r: Result<'a>) : 'a =
        match r with
        | Ok v -> v
        | Error es ->
            let detail =
                es
                |> List.map (fun e -> System.String.Concat(e.Code, ": ", e.Message))
                |> String.concat " | "
            invalidOp (System.String.Concat("expected Ok; got: ", detail))

    interface IClassFixture<EphemeralContainerFixture>

    // -- Test 1 — live catalog readback -----------------------------------

    [<Fact>]
    member _.``live: ref resolves a deployed schema's catalog back through ReadSide`` () =
        if not (skipIfNoDocker "live-catalog-readback") then () else
        let found =
            fixture.WithEphemeralDatabase "LiveSrcRead" (fun cnn connStr ->
                task {
                    do!
                        Deploy.executeBatch cnn
                            ("CREATE TABLE [dbo].[OSUSR_LIVE_WIDGET] (" +
                             "[ID] INT NOT NULL PRIMARY KEY, " +
                             "[NAME] NVARCHAR(50) NOT NULL);")
                    // Resolve the live ref exactly as the CLI does.
                    let! catR = Ref.resolveCatalog (Ref.parse ("live:" + connStr))
                    let catalog = mustOk catR
                    return
                        Catalog.allKinds catalog
                        |> List.exists (fun k -> TableId.tableText k.Physical = "OSUSR_LIVE_WIDGET")
                })
            |> fun t -> t.GetAwaiter().GetResult()
        Assert.True(
            found,
            "live: ref did not reconstruct the deployed OSUSR_LIVE_WIDGET table via ReadSide")

    // -- Test 2 — compare live-profiling end-to-end -----------------------

    [<Fact>]
    member _.``compare live-profiling: A's NULL data against B's NOT NULL model surfaces a dealbreaker`` () =
        if not (skipIfNoDocker "compare-live-dealbreaker") then () else
        // Target B (the declared model): STATUS is NOT NULL.
        // Source A (the live env):       STATUS is NULLABLE and holds a NULL —
        // a row B's declared model would reject. Both tables share physical
        // coordinates, so ReadSide synthesizes identical `READSIDE_ATTR` keys
        // and A's profile correlates with B's declared columns.
        let report =
            fixture.WithEphemeralDatabase "CmpTarget" (fun cnnB connB ->
                task {
                    do!
                        Deploy.executeBatch cnnB
                            ("CREATE TABLE [dbo].[OSUSR_CMP_ORDER] (" +
                             "[ID] INT NOT NULL PRIMARY KEY, " +
                             "[STATUS] INT NOT NULL);")
                    return!
                        fixture.WithEphemeralDatabase "CmpSource" (fun cnnA connA ->
                            task {
                                do!
                                    Deploy.executeBatch cnnA
                                        ("CREATE TABLE [dbo].[OSUSR_CMP_ORDER] (" +
                                         "[ID] INT NOT NULL PRIMARY KEY, " +
                                         "[STATUS] INT NULL);")
                                // The dealbreaker: a NULL in a column B declares NOT NULL.
                                do!
                                    Deploy.executeBatch cnnA
                                        ("INSERT INTO [dbo].[OSUSR_CMP_ORDER] ([ID], [STATUS]) " +
                                         "VALUES (1, NULL);")
                                // Resolve A as a profilable Source, B's catalog only —
                                // mirroring `RunFaces.runCompare`.
                                let! srcA = Ref.resolveSource (Ref.parse ("live:" + connA))
                                let srcA = mustOk srcA
                                let! aCatR = Source.read srcA
                                let aCat = mustOk aCatR
                                let! bCatR = Ref.resolveCatalog (Ref.parse ("live:" + connB))
                                let bCat = mustOk bCatR
                                let! profileA =
                                    match Source.profile srcA with
                                    | None -> task { return None }
                                    | Some acquire ->
                                        task {
                                            let! p = acquire aCat
                                            return (match p with Ok v -> Some v | Error _ -> None)
                                        }
                                let source : Compare.Operand =
                                    { Label = "live:A"; Catalog = aCat; Profile = profileA }
                                let target : Compare.Operand =
                                    { Label = "live:B"; Catalog = bCat; Profile = None }
                                return Compare.compute source target
                            })
                })
            |> fun t -> t.GetAwaiter().GetResult()
        // A is a live env → evidence is available (the source was profiled).
        Assert.True(
            report.DataEvidenceAvailable,
            "expected live source A to carry profiled data evidence")
        // The injected NULL violates B's NOT NULL STATUS → at least one dealbreaker.
        // (Empty here would mean the 4.4 trap is back: the Static mark was not
        // stripped before profiling, so LiveProfiler skipped every kind.)
        Assert.True(
            (not (List.isEmpty report.DataDealbreakers)),
            "expected a NOT-NULL data dealbreaker from A's NULL row against B's model")
