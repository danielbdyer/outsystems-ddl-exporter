namespace Projection.Tests

// THE PER-ACT CONSENT LEDGER, end-to-end against two real mock OutSystems
// environments (2026-07-10, the transfer-manifest program, slice 4a —
// narrate-only): the board's `consent` axis enumerates every destructive /
// creative act the dry-run plan performs — the wipe pinned to the sink's
// probed population, the mint pinned to the plan's own rows, the match pinned
// to the effect hash over the REAL matched pairs — and verdicts each against
// the flow's `signoff` act blessings.
//
//   1. OPEN — no blessings: the ledger names each act, what it does, and the
//      exact paste-able `{ "act": …, "fingerprint": … }` entry. Advisory:
//      the board's exit code is untouched.
//   2. BLESSED — the same fingerprints the board printed, blessed verbatim:
//      the ledger reads fully blessed.
//   3. RE-OPENED — the sink's population changes under the blessing (one row
//      inserted into the wiped table): the wipe's fingerprint drifts, and the
//      ledger says the blessing on file was captured at a different
//      fingerprint — a blessing can never rubber-stamp a changed reality.
//
// Serial via Docker-SqlServer; blocking wait via TaskSync.

open System.Text.RegularExpressions
open Xunit
open Projection.Core
open Projection.Pipeline

[<Xunit.Collection("Docker-SqlServer")>]
type ActConsentDockerTests (fixture: EphemeralContainerFixture) =

    /// Every paste-able blessing the board printed: the OPEN lines carry
    /// `{ "act": "<token>", "fingerprint": "<text>" }` verbatim.
    let printedBlessings (boardOut: string) : (string * ActConsent.ActFingerprint) list =
        Regex.Matches(boardOut, "\\{ \"act\": \"([^\"]+)\", \"fingerprint\": \"([^\"]+)\" \\}")
        |> Seq.map (fun m ->
            let token = m.Groups.[1].Value
            match ActConsent.parseFingerprint m.Groups.[2].Value with
            | Some fp -> token, fp
            | None -> failwithf "the board printed an unparseable fingerprint for %s: %s" token m.Groups.[2].Value)
        |> List.ofSeq

    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``consent ledger: the board names every act with its exact fingerprint; blessing verbatim closes it; a population change re-opens it`` () =
        if not (GoBoardFixtures.skipIfNoDocker "ActConsent") then () else
        TaskSync.run (fun () ->
            MockOutSystemsEnv.withMockEnvPair fixture "ActConsent"
                "" GoBoardFixtures.sourceRows MockOutSystemsEnv.ManagedDml
                "X" GoBoardFixtures.sinkCityRows MockOutSystemsEnv.ManagedDml
                (fun src snk ->
                    task {
                        let planned opts = PlanAction.TransferPeer (src.EngineConnStr, snk.EngineConnStr, opts, false)
                        let wipeOpts (actSignoff: WriteSignoff.ActBlessing list) =
                            { GoBoardFixtures.optsWith [ "Customer" ] [ "AppCore.City:Name" ] with
                                Emission = EmissionMode.WipeAndLoad
                                Signoff  = [ WriteSignoff.greenlit WriteSignoff.WriteMode.Replace ]
                                ActSignoff = actSignoff }

                        // 1. OPEN — every act enumerated, none blessed; the exit
                        // code is the board's own (advisory never blocks).
                        let open1, openOut = GoBoardFixtures.captureBoard (fun () -> GoBoardFixtures.checkGo false false false (planned (wipeOpts [])))
                        Assert.Equal(0, open1)
                        Assert.Contains("awaiting a blessing", openOut)
                        Assert.Contains("wipe:", openOut)
                        Assert.Contains("mint:", openOut)
                        Assert.Contains("match:", openOut)
                        Assert.Contains("deleted child-first before the reload", openOut)   // the wipe's statement
                        Assert.Contains("mints a new primary key", openOut)                 // the mint's statement
                        Assert.Contains("population:", openOut)                             // the wipe/mint pins
                        Assert.Contains("effect:", openOut)                                 // the match pin
                        let blessings = printedBlessings openOut
                        Assert.True(blessings.Length >= 3,
                                    sprintf "expected at least wipe+mint+match open blessings, got %A" (blessings |> List.map fst))

                        // 2. BLESSED — the printed fingerprints, verbatim.
                        let blessed = blessings |> List.map (fun (t, fp) -> WriteSignoff.blessed t fp)
                        let ok, blessedOut = GoBoardFixtures.captureBoard (fun () -> GoBoardFixtures.checkGo false false false (planned (wipeOpts blessed)))
                        Assert.Equal(0, ok)
                        Assert.Contains("every act this run performs is blessed at its current fingerprint", blessedOut)
                        Assert.DoesNotContain("awaiting a blessing", blessedOut)

                        // The JSON twin carries the same ledger (headless-total).
                        let _, jsonOut = GoBoardFixtures.captureBoard (fun () -> GoBoardFixtures.checkGo true false false (planned (wipeOpts blessed)))
                        Assert.Contains("consent", jsonOut)
                        Assert.Contains("population:", jsonOut)

                        // 3. RE-OPENED — the sink population changes under the
                        // blessing: one row lands in the wiped table, so the
                        // wipe's population fingerprint drifts.
                        do! GoBoardFixtures.exec snk.Admin
                                "SET IDENTITY_INSERT [dbo].[OSUSR_XABC_CUSTOMER] ON; \
                                 INSERT INTO [dbo].[OSUSR_XABC_CUSTOMER] ([ID],[EMAIL],[FIRSTNAME],[LASTNAME],[CITYID]) VALUES (601, N'zoe@x', N'Zoe', N'Zink', 501); \
                                 SET IDENTITY_INSERT [dbo].[OSUSR_XABC_CUSTOMER] OFF;"
                        let _, driftOut = GoBoardFixtures.captureBoard (fun () -> GoBoardFixtures.checkGo false false false (planned (wipeOpts blessed)))
                        Assert.Contains("captured at a different fingerprint", driftOut)
                        Assert.Contains("bless it again", driftOut)

                        // The board's dry runs never wrote: the sink still holds
                        // exactly the one row this test inserted.
                        let! customers = GoBoardFixtures.countRows snk.Admin "[dbo].[OSUSR_XABC_CUSTOMER]"
                        Assert.Equal(1, customers)
                        return ()
                    }))

    /// THE ENFORCEMENT WITNESS (slice 4b — always-on for the peer path): a
    /// live Incremental peer Execute whose acts are mints and a match REFUSES
    /// `transfer.writeSignoff.actUnblessed` with the FULL sorted unblessed set
    /// in metadata; blessed VERBATIM from that metadata it proceeds and
    /// writes; and a source edit after the blessing re-opens EXACTLY the
    /// affected act — the untouched acts stay blessed.
    [<Fact>]
    member _.``consent gate: an unblessed peer Execute refuses with the full act set; blessed verbatim it writes; a source edit re-opens exactly the touched act`` () =
        if not (GoBoardFixtures.skipIfNoDocker "ActConsentGate") then () else
        TaskSync.run (fun () ->
            MockOutSystemsEnv.withMockEnvPair fixture "ActGate"
                "" GoBoardFixtures.sourceRows MockOutSystemsEnv.ManagedDml
                "X" GoBoardFixtures.sinkCityRows MockOutSystemsEnv.ManagedDml
                (fun src snk ->
                    task {
                        let! contractsR = PeerTransfer.acquireContracts src.EngineConnStr snk.EngineConnStr
                        let (srcContract, sinkContract) = Result.value contractsR
                        let cityKind =
                            Catalog.allKinds sinkContract
                            |> List.find (fun k -> Name.value k.Name = "City")
                        let cityName = cityKind.Attributes |> List.find (fun a -> Name.value a.Name = "Name")
                        let reconciliation = Map.ofList [ cityKind.SsKey, ReconciliationStrategy.MatchByColumn cityName.Name ]
                        let srcSub : Substrate = { Environment = Projection.Core.Environment.Qa; Role = SubstrateRole.Source; ConnectionRef = ConnectionRef.Raw src.EngineConnStr }
                        let sinkSub : Substrate = { Environment = Projection.Core.Environment.Uat; Role = SubstrateRole.Sink; ConnectionRef = ConnectionRef.Raw snk.EngineConnStr }
                        let connections = TransferConnections.create srcSub sinkSub true |> Result.value
                        let runWith (blessings: WriteSignoff.ActBlessing list) =
                            Transfer.runReverseLegThroughConnections
                                Transfer.Execute EmissionMode.Incremental false true false
                                [ "Customer" ] connections srcContract sinkContract reconciliation Set.empty [] blessings Set.empty

                        // 1. UNBLESSED — the gate refuses by name, LAST in the
                        // chain, with the full sorted act set in metadata.
                        let! unblessed = runWith []
                        let refusal =
                            match unblessed with
                            | Ok _ -> failwith "an unblessed peer Execute must refuse transfer.writeSignoff.actUnblessed"
                            | Error es -> es |> List.find (fun e -> e.Code = "transfer.writeSignoff.actUnblessed")
                        Assert.Contains("not blessed at their current fingerprint", refusal.Message)
                        let tokens = refusal.Metadata |> Map.toList |> List.map fst
                        Assert.Contains(tokens, fun (t: string) -> t.StartsWith "mint:")
                        Assert.Contains(tokens, fun (t: string) -> t.StartsWith "match:")
                        // no write happened: the sink's Customer table is untouched.
                        let! before = GoBoardFixtures.countRows snk.Admin "[dbo].[OSUSR_XABC_CUSTOMER]"
                        Assert.Equal(0, before)

                        // 2. BLESSED VERBATIM from the refusal's own metadata —
                        // the run proceeds and writes the two customers.
                        let blessings = TransferActs.blessingsOf refusal
                        Assert.Equal(tokens.Length, blessings.Length)
                        let! blessedRun = runWith blessings
                        let report = Result.value blessedRun
                        Assert.Equal(2, report.Kinds |> List.sumBy (fun k -> k.RowsWritten))
                        let! after = GoBoardFixtures.countRows snk.Admin "[dbo].[OSUSR_XABC_CUSTOMER]"
                        Assert.Equal(2, after)

                        // 3. A SOURCE EDIT under the blessing — a third customer
                        // appears, so the mint act's population fingerprint
                        // drifts. The same blessings now refuse, and the refusal
                        // names the mint act; the match act (untouched) is NOT
                        // in the re-opened set.
                        do! GoBoardFixtures.exec src.Admin
                                "SET IDENTITY_INSERT [dbo].[OSUSR_ABC_CUSTOMER] ON; \
                                 INSERT INTO [dbo].[OSUSR_ABC_CUSTOMER] ([ID],[EMAIL],[FIRSTNAME],[LASTNAME],[CITYID]) VALUES (12, N'carla@x', N'Carla', N'Cruz', 1); \
                                 SET IDENTITY_INSERT [dbo].[OSUSR_ABC_CUSTOMER] OFF;"
                        // clear the sink so the populated-sink gate (an earlier,
                        // louder slot) does not mask the consent verdict.
                        do! GoBoardFixtures.exec snk.Admin "DELETE FROM [dbo].[OSUSR_XABC_CUSTOMER];"
                        let! reopened = runWith blessings
                        match reopened with
                        | Ok _ -> failwith "a source edit under the blessing must re-open the affected act"
                        | Error es ->
                            let r = es |> List.find (fun e -> e.Code = "transfer.writeSignoff.actUnblessed")
                            let reopenedTokens = r.Metadata |> Map.toList |> List.map fst
                            Assert.Contains(reopenedTokens, fun (t: string) -> t.StartsWith "mint:")
                            Assert.DoesNotContain(reopenedTokens, fun (t: string) -> t.StartsWith "match:")
                        return ()
                    }))
