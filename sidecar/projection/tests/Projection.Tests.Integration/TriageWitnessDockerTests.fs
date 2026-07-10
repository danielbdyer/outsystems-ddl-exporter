namespace Projection.Tests

// THE TRIAGE WITNESS (2026-07-10, the manifest program, slice 1 —
// THE_TRANSFER_MANIFEST.md §9.1): the impact artifact, triaged by coupling,
// over a live two-cell peer estate.
//
// One board, two relational units:
//   - `RefData.Country` — a standalone static-lookup table holding the
//     IDENTICAL dataset on both sides (sink surrogates deliberately differ, so
//     the identity is proven by business key, not by luck) → SettledStatic,
//     folded to one line whose badge states the verdict.
//   - `AppCore.Customer` — the payload, whose CityId escapes the subset with
//     no strategy → OpenEscaping, ranked first, opened by default.
//
// The machine twin carries BOTH units with their triage + couplingWeight, and
// the folded render hides scroll, never tally.

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Adapters.OssysSql
open Projection.Cli.Faces.Transfer

module private TriageFixtures =

    let skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else printfn "SKIP %s: Docker daemon not reachable." label; false

    /// Source: cities (the FK targets), two customers, and the Country
    /// static-lookup rows (PT / ES).
    let sourceRows =
        [ "SET IDENTITY_INSERT [dbo].[OSUSR_DEF_CITY] ON; \
           INSERT INTO [dbo].[OSUSR_DEF_CITY] ([ID],[NAME],[ISACTIVE]) VALUES (1, N'Lisbon', 1), (2, N'Porto', 1); \
           SET IDENTITY_INSERT [dbo].[OSUSR_DEF_CITY] OFF; \
           SET IDENTITY_INSERT [dbo].[OSUSR_ABC_CUSTOMER] ON; \
           INSERT INTO [dbo].[OSUSR_ABC_CUSTOMER] ([ID],[EMAIL],[FIRSTNAME],[LASTNAME],[CITYID]) VALUES \
               (10, N'alice@x', N'Alice', N'Almeida', 1), (11, N'bob@x', N'Bob', N'Barbosa', 2); \
           SET IDENTITY_INSERT [dbo].[OSUSR_ABC_CUSTOMER] OFF; \
           SET IDENTITY_INSERT [dbo].[OSUSR_REF_COUNTRY] ON; \
           INSERT INTO [dbo].[OSUSR_REF_COUNTRY] ([ID],[CODE],[NAME]) VALUES (1, N'PT', N'Portugal'), (2, N'ES', N'Spain'); \
           SET IDENTITY_INSERT [dbo].[OSUSR_REF_COUNTRY] OFF;" ]

    /// Sink: the SAME Country dataset under DIFFERENT surrogates (901/902) —
    /// identity must be proven by the Code business key, never by key luck.
    let sinkRows =
        [ "SET IDENTITY_INSERT [dbo].[OSUSR_XREF_COUNTRY] ON; \
           INSERT INTO [dbo].[OSUSR_XREF_COUNTRY] ([ID],[CODE],[NAME]) VALUES (901, N'PT', N'Portugal'), (902, N'ES', N'Spain'); \
           SET IDENTITY_INSERT [dbo].[OSUSR_XREF_COUNTRY] OFF;" ]

[<Xunit.Collection("Docker-SqlServer")>]
type TriageWitnessDockerTests(fixture: EphemeralContainerFixture) =
    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``triage witness: the impact artifact folds a proven-identical static unit to one line and foregrounds the escaping unit — live two-cell pair`` () =
        if not (TriageFixtures.skipIfNoDocker "TriageWitness") then () else
        TaskSync.run (fun () ->
            MockOutSystemsEnv.withMockEnvPair fixture "Triage"
                "" TriageFixtures.sourceRows MockOutSystemsEnv.ManagedDml
                "X" TriageFixtures.sinkRows MockOutSystemsEnv.ManagedDml
                (fun src snk ->
                    task {
                        let htmlPath = System.IO.Path.Combine ("go-board", "golden.impact.html")
                        let jsonPath = System.IO.Path.Combine ("go-board", "golden.impact.json")
                        if System.IO.File.Exists htmlPath then System.IO.File.Delete htmlPath
                        if System.IO.File.Exists jsonPath then System.IO.File.Delete jsonPath
                        // Customer transferred; City deliberately UN-strategized
                        // (the escape); Country declared a static lookup.
                        let opts =
                            { GoBoardFixtures.optsWith [ "Customer" ] [] with
                                SupportingScope =
                                    [ { Table = "RefData.Country"
                                        Relationship = SupportingScope.SupportingRelationship.StaticLookup "Code"
                                        Reason = "shared reference data, held identical" } ] }
                        let planned = PlanAction.TransferPeer (src.EngineConnStr, snk.EngineConnStr, opts, false)
                        // --impact on; the un-strategized escape reds the board
                        // (exit 5) — the DRY RUN still produces the artifact
                        // (pre-write gates fire only at Execute).
                        let exit, _ = GoBoardFixtures.captureBoard (fun () -> GoBoardFixtures.checkGo false false true planned)
                        Assert.Equal(5, exit)

                        // -- the machine twin: every unit, with triage + weight --
                        Assert.True(System.IO.File.Exists jsonPath, "--impact must write the JSON twin")
                        use doc = System.Text.Json.JsonDocument.Parse (System.IO.File.ReadAllText jsonPath)
                        let segs = doc.RootElement.GetProperty "segments"
                        Assert.Equal(2, segs.GetArrayLength())
                        // rank order: the escaping unit FIRST, the settled-static after
                        let first = segs.[0]
                        let second = segs.[1]
                        Assert.Equal("open-escaping", first.GetProperty("triage").GetString())
                        Assert.True(first.GetProperty("couplingWeight").GetInt32() >= TransferTriage.ForegroundPenalty)
                        Assert.Equal("settled-static", second.GetProperty("triage").GetString())
                        // the fold hides scroll, never tally: segment context sums
                        // equal the artifact totals
                        let sumOver (field: string) =
                            [ for i in 0 .. segs.GetArrayLength() - 1 do
                                let ctx = segs.[i].GetProperty "context"
                                for j in 0 .. ctx.GetArrayLength() - 1 ->
                                    ctx.[j].GetProperty(field).GetInt32() ]
                            |> List.sum
                        let totals = doc.RootElement.GetProperty "totals"
                        Assert.Equal(totals.GetProperty("added").GetInt32(), sumOver "added")
                        Assert.Equal(totals.GetProperty("deleted").GetInt32(), sumOver "deleted")
                        Assert.Equal(totals.GetProperty("changed").GetInt32(), sumOver "changed")
                        Assert.Equal(totals.GetProperty("unchanged").GetInt32(), sumOver "unchanged")

                        // -- the pretty artifact: badges + the foregrounded open --
                        // The badge copy is THE_VOICE-audited (plain, exacting,
                        // the precise mechanism): assert the exact statements.
                        let html = System.IO.File.ReadAllText htmlPath
                        Assert.Contains("source and target hold the same rows, verified column by column — nothing is written", html)
                        Assert.Contains("a column points at a table outside the transfer — a decision is required", html)
                        // the top-ranked open unit is revealed; the settled one is not
                        Assert.Contains("<details class=\"segment\" open>", html)
                        Assert.Contains("Open units", html)
                        Assert.Contains("Settled units", html)
                        return ()
                    }))
