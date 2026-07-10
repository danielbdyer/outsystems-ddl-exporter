namespace Projection.Tests

// THE EVIDENCE-CACHE FIDELITY WITNESS (2026-07-10, the manifest program,
// slice 2 — THE_TRANSFER_MANIFEST.md §4.4 / §9.2): the cache-computed delta
// EQUALS the authoritative dry run, over a live two-cell peer pair.
//
// The coupled shape: SalesOrder references BOTH Customer and Category
// (mandatory). The sink holds a match for only one of two customers, so one
// order drops on the Customer edge — and the cache's Category count must
// shrink with it (the component recomputes as a unit), exactly as the
// engine's plan does. Toggling Customer's answer to Pin releases the row and
// Category's count grows — the live §4.3 coupling.

open Xunit
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Pipeline

module private EvidenceCacheFixtures =

    let skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else printfn "SKIP %s: Docker daemon not reachable." label; false

    /// Source: two customers (alice, bob), two categories (red, blue), three
    /// orders — 1000 (alice, red), 1001 (bob, blue), 1002 (alice, blue).
    let sourceRows =
        [ "SET IDENTITY_INSERT [dbo].[OSUSR_DEF_CITY] ON; \
           INSERT INTO [dbo].[OSUSR_DEF_CITY] ([ID],[NAME],[ISACTIVE]) VALUES (1, N'Lisbon', 1); \
           SET IDENTITY_INSERT [dbo].[OSUSR_DEF_CITY] OFF; \
           SET IDENTITY_INSERT [dbo].[OSUSR_ABC_CUSTOMER] ON; \
           INSERT INTO [dbo].[OSUSR_ABC_CUSTOMER] ([ID],[EMAIL],[FIRSTNAME],[LASTNAME],[CITYID]) VALUES \
               (10, N'alice@x', N'Alice', N'Almeida', 1), (11, N'bob@x', N'Bob', N'Barbosa', 1); \
           SET IDENTITY_INSERT [dbo].[OSUSR_ABC_CUSTOMER] OFF; \
           SET IDENTITY_INSERT [dbo].[OSUSR_SAL_CATEGORY] ON; \
           INSERT INTO [dbo].[OSUSR_SAL_CATEGORY] ([ID],[NAME],[PARENTCATEGORYID]) VALUES (100, N'red', NULL), (101, N'blue', NULL); \
           SET IDENTITY_INSERT [dbo].[OSUSR_SAL_CATEGORY] OFF; \
           SET IDENTITY_INSERT [dbo].[OSUSR_SAL_ORDER] ON; \
           INSERT INTO [dbo].[OSUSR_SAL_ORDER] ([ID],[CUSTOMERID],[CATEGORYID],[ORDERTOTAL],[PLACEDAT]) VALUES \
               (1000, 10, 100, 12.50, '2026-01-01'), (1001, 11, 101, 8.00, '2026-01-02'), (1002, 10, 101, 30.00, '2026-01-03'); \
           SET IDENTITY_INSERT [dbo].[OSUSR_SAL_ORDER] OFF;" ]

    /// Sink: alice EXISTS (under its own surrogate), bob does NOT; both
    /// categories exist. One order will drop on the Customer edge.
    let sinkRows =
        [ "SET IDENTITY_INSERT [dbo].[OSUSR_XDEF_CITY] ON; \
           INSERT INTO [dbo].[OSUSR_XDEF_CITY] ([ID],[NAME],[ISACTIVE]) VALUES (501, N'Lisbon', 1); \
           SET IDENTITY_INSERT [dbo].[OSUSR_XDEF_CITY] OFF; \
           SET IDENTITY_INSERT [dbo].[OSUSR_XABC_CUSTOMER] ON; \
           INSERT INTO [dbo].[OSUSR_XABC_CUSTOMER] ([ID],[EMAIL],[FIRSTNAME],[LASTNAME],[CITYID]) VALUES \
               (901, N'alice@x', N'Alice', N'Almeida', 501); \
           SET IDENTITY_INSERT [dbo].[OSUSR_XABC_CUSTOMER] OFF; \
           SET IDENTITY_INSERT [dbo].[OSUSR_XSAL_CATEGORY] ON; \
           INSERT INTO [dbo].[OSUSR_XSAL_CATEGORY] ([ID],[NAME],[PARENTCATEGORYID]) VALUES (801, N'red', NULL), (802, N'blue', NULL); \
           SET IDENTITY_INSERT [dbo].[OSUSR_XSAL_CATEGORY] OFF;" ]

    let kindBy (contract: Catalog) (name: string) : Kind =
        Catalog.allKinds contract |> List.find (fun k -> Name.value k.Name = name)

    let nm (s: string) : Name = Name.create s |> Result.value

[<Xunit.Collection("Docker-SqlServer")>]
type EvidenceCacheDockerTests(fixture: EphemeralContainerFixture) =
    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``evidence witness: the cache-computed delta equals the authoritative dry run, and toggling one edge recomputes its coupled sibling — live two-cell pair`` () =
        if not (EvidenceCacheFixtures.skipIfNoDocker "EvidenceCacheFidelity") then () else
        TaskSync.run (fun () ->
            MockOutSystemsEnv.withMockEnvPair fixture "EvCache"
                "" EvidenceCacheFixtures.sourceRows MockOutSystemsEnv.ManagedDml
                "X" EvidenceCacheFixtures.sinkRows MockOutSystemsEnv.ManagedDml
                (fun src snk ->
                    task {
                        let! contractsR = PeerTransfer.acquireContracts src.EngineConnStr snk.EngineConnStr
                        let (srcContract, sinkContract) = Result.value contractsR
                        let customer = EvidenceCacheFixtures.kindBy srcContract "Customer"
                        let category = EvidenceCacheFixtures.kindBy srcContract "Category"
                        let order = EvidenceCacheFixtures.kindBy srcContract "SalesOrder"
                        let loadSet = Set.singleton order.SsKey
                        let escapes = PeerTransfer.escapingFks srcContract loadSet Set.empty
                        // the two coupled edges: SalesOrder -> Customer, -> Category
                        Assert.Equal(2, List.length escapes)

                        // -- the cache, read once from the live pair ----------
                        use srcCnn = new SqlConnection(src.EngineConnStr)
                        use snkCnn = new SqlConnection(snk.EngineConnStr)
                        do! srcCnn.OpenAsync()
                        do! snkCnn.OpenAsync()
                        let! cache = EvidenceCache.fill srcCnn snkCnn srcContract sinkContract escapes
                        let selections =
                            Map.ofList
                                [ customer.SsKey, EvidenceCache.Answer.Reconcile (EvidenceCacheFixtures.nm "Email")
                                  category.SsKey, EvidenceCache.Answer.Reconcile (EvidenceCacheFixtures.nm "Name") ]
                        let coupled = EvidenceCache.componentDeltas cache srcContract loadSet Set.empty escapes selections
                        // Customer: alice's two orders re-key; bob's order drops.
                        Assert.Equal(2, coupled.[customer.SsKey].Delta.RowsRekeyed)
                        Assert.Equal(1, coupled.[customer.SsKey].Delta.RowsDropped)
                        Assert.Equal(Some true, coupled.[customer.SsKey].SinkUnique)
                        Assert.Equal<(string * string) list>([ "alice@x", "901" ], coupled.[customer.SsKey].MatchedPairs)
                        // Category: every value matches, but bob's dropped order
                        // takes one reference with it — 2, not 3 (the coupling).
                        Assert.Equal(2, coupled.[category.SsKey].Delta.RowsRekeyed)
                        Assert.Equal(0, coupled.[category.SsKey].Delta.RowsDropped)

                        // -- the AUTHORITATIVE dry run, same selections --------
                        let srcSub : Substrate = { Environment = Projection.Core.Environment.Qa; Role = SubstrateRole.Source; ConnectionRef = ConnectionRef.Raw src.EngineConnStr }
                        let sinkSub : Substrate = { Environment = Projection.Core.Environment.Uat; Role = SubstrateRole.Sink; ConnectionRef = ConnectionRef.Raw snk.EngineConnStr }
                        let connections = TransferConnections.create srcSub sinkSub true |> Result.value
                        let reconciliation =
                            Map.ofList
                                [ customer.SsKey, ReconciliationStrategy.MatchByColumn (EvidenceCacheFixtures.nm "Email")
                                  category.SsKey, ReconciliationStrategy.MatchByColumn (EvidenceCacheFixtures.nm "Name") ]
                        let! runR =
                            Transfer.runReverseLegThroughConnectionsWith
                                IdentityPolicy.Structural Transfer.DryRun EmissionMode.Incremental false true false
                                [ "SalesOrder" ] connections srcContract sinkContract reconciliation Set.empty Set.empty Set.empty
                                [] [] true Set.empty Set.empty false None
                        let report = Result.value runR
                        // cache ≡ authoritative: the engine's surviving plan rows
                        // equal the cache's re-keyed count; the engine's dropped
                        // references equal the cache's drop count.
                        let plan = report.Plan |> Option.get
                        let orderLoad = plan.Loads |> List.find (fun l -> l.Kind = order.SsKey)
                        Assert.Equal(coupled.[customer.SsKey].Delta.RowsRekeyed, orderLoad.Rows.Length)
                        Assert.Equal(coupled.[customer.SsKey].Delta.RowsDropped, report.SkippedReferences.Length)

                        // -- the coupling, live: toggle Customer to Pin ----------
                        let released =
                            EvidenceCache.componentDeltas cache srcContract loadSet Set.empty escapes
                                (Map.add customer.SsKey (EvidenceCache.Answer.Pin None) selections)
                        Assert.Equal(3, released.[category.SsKey].Delta.RowsRekeyed)
                        return ()
                    }))
