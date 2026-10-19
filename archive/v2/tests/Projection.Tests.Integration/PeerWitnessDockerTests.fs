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
                            [ "City" ] connections srcContract sinkContract Map.empty Set.empty [] [] Set.empty)
                match r with
                | Ok _ -> failwith "the replace-wipe must refuse — a sink dependent references the wiped parent"
                | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "transfer.supportingScope.inboundOrphan")
                // The sink is untouched — City 501 still present (the wipe never ran).
                let! cities = countRows sink "[dbo].[OSUSR_XDEF_CITY]"
                Assert.Equal(1, cities)
            })

    /// The operator-walk estate loop (ideation §12) over LIVE cells: the daily
    /// convergence instrument (`check environments`) reads two espace-variant
    /// cells of one model and finds ONE shape — A45 (espace invariance) proven
    /// over real OSSYS reads, not fixtures. `computeWith` normalizes each cell to
    /// its logical shape, so the per-espace physical names fall away and the
    /// estate is unified; the board renders over the live-read report.
    [<Fact>]
    member _.``operator walk: check environments reads two live espace-variant cells as one shape — unified (A45 over live reads), the board renders`` () =
        if not (skipIfNoDocker "PeerEstateWalk") then () else
        run2Cell fixture "PeerEstateWalk" (fun _src _sink _srcConnStr _sinkConnStr srcContract sinkContract ->
            task {
                // Cell A is the agreed target; cell B is the confirm environment.
                // Both are live-read contracts of one model, espace-shifted
                // (SsKey-aligned) — `toLogicalShape` normalizes the physical names.
                let operandB : Compare.Operand = { Label = "cell-b"; Catalog = sinkContract; Profile = None }
                let report =
                    Estate.computeWith Estate.Posture.defaults Estate.StaticContent.empty
                        (Estate.TargetOperand.AgreedEnv "cell-a") srcContract [ "cell-b", operandB ]
                // The espace-invariance law over LIVE reads: one shape, no findings.
                Assert.True(Estate.isUnified report,
                            sprintf "two espace-variant live cells must read as one shape; findings: %A"
                                (report.Findings |> List.map (fun f -> f.Statement)))
                Assert.Empty(report.Findings)
                // The daily instrument's board renders over the live reads.
                let board = Estate.render report
                Assert.Contains(board, fun (l: string) -> l.StartsWith "ENVIRONMENTS")
                return ()
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
                        TransferActs.blessAllAndRun (fun blessings ->
                            Transfer.runReverseLegThroughConnections
                                Transfer.Execute EmissionMode.Incremental false true false
                                [ "City"; "Customer" ] connections srcContract sinkContract Map.empty Set.empty [] blessings Set.empty))
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

    /// `transfer.staticLookup.diverged` — a kind declared `static-lookup` (reference
    /// data asserted IDENTICAL across the environments, matched by a business key)
    /// refuses at the live seam when the datasets differ. Here the sink holds an
    /// EXTRA city the source does not (so every source city still MATCHES — no
    /// unmatched drop precedes it — but the set is not identical). Pinned pure in
    /// ReconciliationTests / TransferRefusalTests; here it fires against a live pair.
    [<Fact>]
    member _.``witness: a static-lookup kind whose sink dataset diverges refuses transfer.staticLookup.diverged`` () =
        if not (skipIfNoDocker "PeerStaticLookup") then () else
        run2Cell fixture "PeerStaticLookup" (fun src sink srcConnStr sinkConnStr srcContract sinkContract ->
            task {
                do! Deploy.executeBatch src sourceRows
                // The sink's City reference data is a SUPERSET (Lisbon/Porto match
                // the source; Madrid is extra) — every source city matches (no
                // unmatched), but the datasets are NOT identical.
                do! Deploy.executeBatch sink
                        "SET IDENTITY_INSERT [dbo].[OSUSR_XDEF_CITY] ON; INSERT INTO [dbo].[OSUSR_XDEF_CITY] ([ID],[NAME],[ISACTIVE]) VALUES (501, N'Lisbon', 1), (502, N'Porto', 1), (503, N'Madrid', 1); SET IDENTITY_INSERT [dbo].[OSUSR_XDEF_CITY] OFF;"
                let cityKind = kindByLogicalName sinkContract "City"
                let cityNameAttr =
                    cityKind.Attributes |> List.find (fun a -> ColumnRealization.columnNameText a.Column = "NAME")
                let reconciliation = Map.ofList [ cityKind.SsKey, ReconciliationStrategy.MatchByColumn cityNameAttr.Name ]
                let staticLookup = Set.ofList [ cityKind.SsKey ]
                let! r =
                    throughConnections srcConnStr sinkConnStr true (fun connections ->
                        Transfer.runReverseLegThroughConnections
                            Transfer.Execute EmissionMode.Incremental false true false
                            [ "Customer" ] connections srcContract sinkContract reconciliation Set.empty [] [] staticLookup)
                match r with
                | Ok _ -> failwith "a diverged static-lookup dataset must refuse at the live seam"
                | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "transfer.staticLookup.diverged")
            })

    /// `check go --impact` (`TransferImpact`) — the add/delete/change/unchanged
    /// classification against a REAL two-DB delta (pinned pure against hand-built
    /// rows; here it reads live rows and classifies). City is matched by NAME under
    /// a wipe: source {Lisbon, Porto, Faro} vs sink {Lisbon, Porto(ISACTIVE flipped),
    /// Madrid} ⇒ Faro Added, Madrid Deleted, Porto Changed, Lisbon Unchanged.
    [<Fact>]
    member _.``witness: TransferImpact classifies a real two-DB delta (add/delete/change/unchanged)`` () =
        if not (skipIfNoDocker "PeerImpact") then () else
        run2Cell fixture "PeerImpact" (fun src sink srcConnStr sinkConnStr srcContract sinkContract ->
            task {
                do! Deploy.executeBatch src
                        "SET IDENTITY_INSERT [dbo].[OSUSR_DEF_CITY] ON; INSERT INTO [dbo].[OSUSR_DEF_CITY] ([ID],[NAME],[ISACTIVE]) VALUES (1, N'Lisbon', 1), (2, N'Porto', 1), (3, N'Faro', 1); SET IDENTITY_INSERT [dbo].[OSUSR_DEF_CITY] OFF;"
                do! Deploy.executeBatch sink
                        "SET IDENTITY_INSERT [dbo].[OSUSR_XDEF_CITY] ON; INSERT INTO [dbo].[OSUSR_XDEF_CITY] ([ID],[NAME],[ISACTIVE]) VALUES (501, N'Lisbon', 1), (502, N'Porto', 0), (503, N'Madrid', 1); SET IDENTITY_INSERT [dbo].[OSUSR_XDEF_CITY] OFF;"
                let sinkCity = kindByLogicalName sinkContract "City"
                let srcCity  = kindByLogicalName srcContract "City"
                let nameAttr = sinkCity.Attributes |> List.find (fun a -> ColumnRealization.columnNameText a.Column = "NAME")
                let! before = readKindRows sink sinkCity   // the sink's current rows
                let! after  = readKindRows src srcCity      // the rows the transfer would load
                let inputs : TransferImpact.Inputs =
                    { Catalog      = sinkContract
                      Scope        = Set.ofList [ sinkCity.SsKey ]
                      Reconciled   = Set.empty
                      Wiped        = Set.ofList [ sinkCity.SsKey ]
                      BusinessKeys = Map.ofList [ sinkCity.SsKey, nameAttr.Name ]
                      Before       = Map.ofList [ sinkCity.SsKey, before ]
                      After        = Map.ofList [ sinkCity.SsKey, after ]
                      Ignore       = Set.empty
                      Roles        = Map.empty }
                let impact = TransferImpact.build "witness" "replace" inputs
                Assert.Equal(1, impact.Totals.Added)     // Faro
                Assert.Equal(1, impact.Totals.Deleted)   // Madrid
                Assert.Equal(1, impact.Totals.Changed)   // Porto (ISACTIVE flipped)
                Assert.Equal(1, impact.Totals.Unchanged) // Lisbon
            })
