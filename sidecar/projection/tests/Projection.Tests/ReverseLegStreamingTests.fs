namespace Projection.Tests

// The streaming realization + chunk resume (the 288M-row program, slices
// A+C): `Transfer.runStreamingWithRenames` moves the LE-3 estate with
// bounded memory (per-kind chunks; only the packed remap and the chunk in
// flight are resident) and, given a journal directory, resumes a crashed
// run at chunk granularity — completed chunks skip, their journaled pairs
// rebuild the remap, drift refuses by name, and a COMPLETED run re-runs
// as a full skip (the streaming path's idempotent re-run, closing G3
// whenever a journal is supplied). Serial via Docker-SqlServer.

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Targets.SSDT

[<Xunit.Collection("Docker-SqlServer")>]
type ReverseLegStreamingTests(fixture: EphemeralContainerFixture) =
    interface IClassFixture<EphemeralContainerFixture>

    /// Deploy B (rendered logical, seeded) and A (physical, empty); hand
    /// both to the body — the streaming sibling of WithReverseLegEstates.
    member private _.WithEstates
        (label: string)
        (seed: string)
        (body: Microsoft.Data.SqlClient.SqlConnection ->
               Microsoft.Data.SqlClient.SqlConnection ->
               Catalog -> Catalog -> System.Threading.Tasks.Task<unit>) =
        let model = ReverseLegFixtures.authoredModel
        let logicalContract = CatalogRendition.logical model
        let physicalContract = CatalogRendition.physical model
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase (label + "Src") (fun src _ ->
                task {
                    do! Deploy.executeBatch src (SsdtDdlEmitter.statements logicalContract |> Render.toText)
                    do! Deploy.executeBatch src seed
                    return!
                        fixture.WithEphemeralDatabase (label + "Sink") (fun sink _ ->
                            task {
                                do! Deploy.executeBatch sink (SsdtDdlEmitter.statements physicalContract |> Render.toText)
                                do! body src sink logicalContract physicalContract
                            })
                }))

    member private _.FreshJournalDir (label: string) : string =
        let dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "projection-journal-" + label + "-" + System.Guid.NewGuid().ToString("N").Substring(0, 8))
        System.IO.Directory.CreateDirectory dir |> ignore
        dir

    [<Fact>]
    member this.``streaming equivalence: the full-shape leg via runStreamingWithRenames matches the materialized path — joins, minted keys, the named orphan drop, exit 9`` () =
        if not (ReverseLegFixtures.skipIfNoDocker "L3StreamEq") then () else
        this.WithEstates "L3StreamEq" (ReverseLegFixtures.seedClean + " " + ReverseLegFixtures.seedOrphan)
            (fun src sink logicalContract physicalContract ->
                task {
                    let! reportR =
                        Transfer.runStreamingWithRenames Transfer.Execute true src sink logicalContract physicalContract None
                    let report = ReverseLegFixtures.value reportR

                    // The orphan (Payment -> Invoice 9999) is dropped BY NAME
                    // through the streaming path too, with the same exit policy.
                    Assert.Contains(report.SkippedReferences, fun (owner, r: UnresolvedReference) ->
                        owner = ReverseLegFixtures.kKey "Payment"
                        && r.Target = ReverseLegFixtures.kKey "Invoice"
                        && r.UnresolvedSource = SourceKey.ofString "9999")
                    Assert.Equal(Transfer.DroppedReferencesExit, Transfer.exitCodeForReport false report)

                    // Per-kind counts stream-accumulated: 5 payments ingested,
                    // 4 written (the named orphan dropped).
                    let payment = report.Kinds |> List.find (fun k -> k.Kind = ReverseLegFixtures.kKey "Payment")
                    Assert.Equal(5, payment.RowsIngested)
                    Assert.Equal(4, payment.RowsWritten)

                    // Identical relational outcome to the materialized path.
                    let! preserved = ReverseLegFixtures.preservedKeyCount sink
                    Assert.Equal(0, preserved)
                    let! (aAccCust, aInvAcc, aInvCust, aPayInv, _) = ReverseLegFixtures.sinkEdgeJoins sink
                    Assert.Equal<(string * string option) list>(
                        [ ("acc-a1", Some "alice@x"); ("acc-a2", Some "alice@x"); ("acc-b1", Some "bob@x") ], aAccCust)
                    Assert.Equal<(string * string option) list>(
                        [ ("inv-1", Some "acc-a1"); ("inv-2", Some "acc-a2"); ("inv-3", Some "acc-b1") ], aInvAcc)
                    Assert.Equal<(string * string option) list>(
                        [ ("inv-1", Some "alice@x"); ("inv-2", None); ("inv-3", Some "bob@x") ], aInvCust)
                    Assert.Equal(4, aPayInv.Length)
                })

    [<Fact>]
    member this.``chunk resume: a mid-load crash resumes from the journal — completed kinds skip, the remap rebuilds from journaled pairs, no duplicates, joins hold`` () =
        if not (ReverseLegFixtures.skipIfNoDocker "L3Resume") then () else
        let journalDir = this.FreshJournalDir "resume"
        this.WithEstates "L3Resume" ReverseLegFixtures.seedClean
            (fun src sink logicalContract physicalContract ->
                task {
                    // Simulate the mid-load crash: the LAST kind's sink table is
                    // missing, so Customer/Account/Invoice journal their chunks
                    // and the Payment write dies with a raw SqlException.
                    do! Deploy.executeBatch sink "DROP TABLE [dbo].[OSUSR_L3_PAYMENT];"
                    // SqlBulkCopy wraps the missing-table SqlException in an
                    // InvalidOperationException; the crash shape is the point,
                    // not the wrapper type.
                    let! _ =
                        Assert.ThrowsAnyAsync<System.Exception>(fun () ->
                            Transfer.runStreamingWithRenames Transfer.Execute true src sink
                                logicalContract physicalContract (Some journalDir)
                            :> System.Threading.Tasks.Task)

                    // The upstream kinds landed before the crash.
                    let! customersAfterCrash = ReverseLegFixtures.countRows sink "[dbo].[OSUSR_L3_CUSTOMER]"
                    Assert.Equal(2, customersAfterCrash)

                    // Remediate (the fixture's admin act) and RESUME with the
                    // SAME journal: the journaled chunks skip — no duplicates —
                    // and their pairs rebuild the remap so Payment's FKs
                    // re-point correctly even though Invoice was never
                    // re-inserted on this run.
                    do! Deploy.executeBatch sink
                            ("CREATE TABLE [dbo].[OSUSR_L3_PAYMENT] (" +
                             "[ID] INT IDENTITY(1,1) NOT NULL PRIMARY KEY, " +
                             "[INVOICE_ID] INT NOT NULL, [ACCOUNT_ID] INT NOT NULL, [PAYREF] NVARCHAR(MAX) NOT NULL, " +
                             "CONSTRAINT [FK_L3R_Pay_Inv] FOREIGN KEY ([INVOICE_ID]) REFERENCES [dbo].[OSUSR_L3_INVOICE] ([ID]), " +
                             "CONSTRAINT [FK_L3R_Pay_Acc] FOREIGN KEY ([ACCOUNT_ID]) REFERENCES [dbo].[OSUSR_L3_ACCOUNT] ([ID]));")
                    let! reportR =
                        Transfer.runStreamingWithRenames Transfer.Execute true src sink
                            logicalContract physicalContract (Some journalDir)
                    let report = ReverseLegFixtures.value reportR
                    Assert.Empty(report.SkippedReferences)

                    // No duplicates anywhere; everything landed exactly once.
                    let! customers = ReverseLegFixtures.countRows sink "[dbo].[OSUSR_L3_CUSTOMER]"
                    let! accounts  = ReverseLegFixtures.countRows sink "[dbo].[OSUSR_L3_ACCOUNT]"
                    let! invoices  = ReverseLegFixtures.countRows sink "[dbo].[OSUSR_L3_INVOICE]"
                    let! payments  = ReverseLegFixtures.countRows sink "[dbo].[OSUSR_L3_PAYMENT]"
                    Assert.Equal(2, customers)
                    Assert.Equal(3, accounts)
                    Assert.Equal(3, invoices)
                    Assert.Equal(4, payments)

                    // The remap rebuilt from the journal: Payment's business-key
                    // joins land on the FIRST run's minted keys.
                    let! (aAccCust, _, _, aPayInv, aPayAcc) = ReverseLegFixtures.sinkEdgeJoins sink
                    Assert.Equal<(string * string option) list>(
                        [ ("acc-a1", Some "alice@x"); ("acc-a2", Some "alice@x"); ("acc-b1", Some "bob@x") ], aAccCust)
                    Assert.Equal<(string * string option) list>(
                        [ ("pay-1", Some "inv-1"); ("pay-2", Some "inv-2"); ("pay-3", Some "inv-3"); ("pay-4", Some "inv-1") ], aPayInv)
                    Assert.Equal<(string * string option) list>(
                        [ ("pay-1", Some "acc-a1"); ("pay-2", Some "acc-a2"); ("pay-3", Some "acc-b1"); ("pay-4", Some "acc-a1") ], aPayAcc)
                })

    [<Fact>]
    member this.``journaled idempotent re-run: a COMPLETED streaming run re-runs as a full skip — zero duplicates (closing G3 under a journal)`` () =
        if not (ReverseLegFixtures.skipIfNoDocker "L3Idem") then () else
        let journalDir = this.FreshJournalDir "idem"
        this.WithEstates "L3Idem" ReverseLegFixtures.seedClean
            (fun src sink logicalContract physicalContract ->
                task {
                    let runLeg () =
                        Transfer.runStreamingWithRenames Transfer.Execute true src sink
                            logicalContract physicalContract (Some journalDir)
                    let! firstR = runLeg ()
                    let _ = ReverseLegFixtures.value firstR
                    let! secondR = runLeg ()
                    let second = ReverseLegFixtures.value secondR

                    // The second run is a full journal skip: the sink is
                    // untouched (no duplicates — contrast the unjournaled
                    // Incremental re-run, which DOUBLES every AssignedBySink
                    // kind), and the report still accounts the journaled work.
                    let! customers = ReverseLegFixtures.countRows sink "[dbo].[OSUSR_L3_CUSTOMER]"
                    let! payments  = ReverseLegFixtures.countRows sink "[dbo].[OSUSR_L3_PAYMENT]"
                    Assert.Equal(2, customers)
                    Assert.Equal(4, payments)
                    let payment = second.Kinds |> List.find (fun k -> k.Kind = ReverseLegFixtures.kKey "Payment")
                    Assert.Equal(4, payment.RowsWritten)
                })

    [<Fact>]
    member this.``resume guard: source drift under the journal refuses by name (transfer.resume.sourceDrift) — never a silent re-run over changed data`` () =
        if not (ReverseLegFixtures.skipIfNoDocker "L3Drift") then () else
        let journalDir = this.FreshJournalDir "drift"
        this.WithEstates "L3Drift" ReverseLegFixtures.seedClean
            (fun src sink logicalContract physicalContract ->
                task {
                    let runLeg () =
                        Transfer.runStreamingWithRenames Transfer.Execute true src sink
                            logicalContract physicalContract (Some journalDir)
                    let! firstR = runLeg ()
                    let _ = ReverseLegFixtures.value firstR

                    // The source changes under the journal: Customer's chunk
                    // fingerprint (first/last PK + raw count) no longer matches.
                    do! Deploy.executeBatch src
                            ("SET IDENTITY_INSERT [dbo].[Customer] ON; " +
                             "INSERT INTO [dbo].[Customer] ([Id],[Email]) VALUES (1002,N'carol@x'); " +
                             "SET IDENTITY_INSERT [dbo].[Customer] OFF;")
                    let! secondR = runLeg ()
                    match secondR with
                    | Error es ->
                        Assert.True(
                            es |> List.exists (fun e -> e.Code = "transfer.resume.sourceDrift"),
                            sprintf "expected transfer.resume.sourceDrift, got %A" (es |> List.map (fun e -> e.Code)))
                    | Ok _ -> Assert.Fail("expected the source-drift refusal under the journal")
                })
