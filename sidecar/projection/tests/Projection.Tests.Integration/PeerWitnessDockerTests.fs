namespace Projection.Tests

open Xunit
open Projection.Core
open Projection.Pipeline
open PeerEstateHarness

// Docker witnesses over the SHARED two-cell peer harness (`PeerEstateHarness`,
// T3) — engine refusals the audit flagged as shipped-but-unproven at the live
// seam. Each is one `run2Cell` call: the harness bootstraps the two SsKey-aligned
// cells; the body declares its own data + the transfer + the by-name assertion.
// Serial via the Docker-SqlServer collection.
[<Xunit.Collection("Docker-SqlServer")>]
type PeerWitnessDockerTests(fixture: EphemeralContainerFixture) =
    interface IClassFixture<EphemeralContainerFixture>

    /// `transfer.supportingScope.inboundOrphan` — a replace-wipe of an in-payload
    /// parent (City) refuses when an OUT-of-payload dependent (Customer) already
    /// holds referencing rows on the sink; wiping the parent would orphan them
    /// (FK 547). Pinned pure elsewhere; here it fires against a live pair.
    [<Fact>]
    member _.``witness: a WipeAndLoad whose subset parent is referenced by an out-of-subset sink dependent refuses transfer.supportingScope.inboundOrphan`` () =
        if not (skipIfNoDocker "PeerInboundOrphan") then () else
        run2Cell fixture "PeerInbound" (fun src sink srcConnStr sinkConnStr srcContract sinkContract ->
            task {
                do! Deploy.executeBatch src sourceRows
                // The sink already holds a Customer (out of the [City] subset)
                // referencing a City row — a replace-wipe of City would orphan it.
                do! Deploy.executeBatch sink
                        "SET IDENTITY_INSERT [dbo].[OSUSR_XDEF_CITY] ON; INSERT INTO [dbo].[OSUSR_XDEF_CITY] ([ID],[NAME],[ISACTIVE]) VALUES (501, N'Lisbon', 1); SET IDENTITY_INSERT [dbo].[OSUSR_XDEF_CITY] OFF; \
                         SET IDENTITY_INSERT [dbo].[OSUSR_XABC_CUSTOMER] ON; INSERT INTO [dbo].[OSUSR_XABC_CUSTOMER] ([ID],[EMAIL],[FIRSTNAME],[LASTNAME],[CITYID]) VALUES (901, N'carol@x', N'Carol', N'Costa', 501); SET IDENTITY_INSERT [dbo].[OSUSR_XABC_CUSTOMER] OFF;"
                let! r =
                    throughConnections srcConnStr sinkConnStr false (fun connections ->
                        Transfer.runReverseLegThroughConnections
                            Transfer.Execute EmissionMode.WipeAndLoad false true false
                            [ "City" ] connections srcContract sinkContract Map.empty Set.empty [] Set.empty)
                match r with
                | Ok _ -> failwith "the replace-wipe must refuse — a sink dependent references the wiped parent"
                | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "transfer.supportingScope.inboundOrphan")
                // The sink is untouched — City 501 still present (the wipe never ran).
                let! cities = countRows sink "[dbo].[OSUSR_XDEF_CITY]"
                Assert.Equal(1, cities)
            })

    /// `transfer.incremental.populatedSink` (T1.8) — a second merge/Incremental
    /// Execute into a populated sink refuses (it would re-mint every AssignedBySink
    /// row, duplicating them), on the peer path.
    [<Fact>]
    member _.``witness: a second Incremental Execute into a populated peer sink refuses transfer.incremental.populatedSink`` () =
        if not (skipIfNoDocker "PeerPopulated") then () else
        run2Cell fixture "PeerPopulated" (fun src sink srcConnStr sinkConnStr srcContract sinkContract ->
            task {
                do! Deploy.executeBatch src sourceRows
                let runOnce () =
                    throughConnections srcConnStr sinkConnStr false (fun connections ->
                        Transfer.runReverseLegThroughConnections
                            Transfer.Execute EmissionMode.Incremental false true false
                            [ "City"; "Customer" ] connections srcContract sinkContract Map.empty Set.empty [] Set.empty)
                let! first = runOnce ()
                let _ = value first     // the first load into the empty sink succeeds
                let! second = runOnce ()
                match second with
                | Ok _ -> failwith "the second Incremental Execute into a populated sink must refuse (T1.8)"
                | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "transfer.incremental.populatedSink")
                // No duplication — the sink holds exactly the first load's 2 customers.
                let! customers = countRows sink "[dbo].[OSUSR_XABC_CUSTOMER]"
                Assert.Equal(2, customers)
            })
