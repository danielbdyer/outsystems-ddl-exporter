namespace Projection.Tests

// Physical-claim adjudication over the REAL lifecycle seed (the data-sink
// chapter, S11): seed → total read → witness (explicit temp store) →
// journal-assembled claim sets → the ladder. The four staged scenarios land
// on their four outcomes — and the TombstoneOnly case carries the chapter's
// original incident, proven: the deleted entity's full shape is addressable
// in the witnessed edition after the tombstone.

open System
open System.IO
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Adapters.OssysSql

[<Xunit.Collection("Docker-SqlServer")>]
type SinkClaimsSeedTests(fixture: EphemeralContainerFixture) =

    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``the lifecycle seed adjudicates: Adopted / TombstoneOnly (recoverable) / Adopted-over-tombstone / Contested`` () =
        if not (Deploy.Docker.ensureRunning ()) then
            printfn "SKIP SinkClaimsSeed: Docker daemon not reachable."
        else
        let tempStore = Path.Combine(Path.GetTempPath(), "sink-claims-tests", Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory tempStore |> ignore
        try
            TaskSync.run (fun () ->
                fixture.WithEphemeralDatabase "SinkClaims" (fun cnn _ -> task {
                    do! Deploy.executeBatch cnn (MetadataExtractionSql.readLifecycleSeed ())
                    let! snapshotResult = MetadataSnapshotRunner.runAsync cnn MetadataSnapshotRunner.defaultParameters
                    let snapshot =
                        match snapshotResult with
                        | Ok s -> s
                        | Error es -> failwithf "acquisition refused: %A" es
                    let nowUtc = DateTimeOffset.Parse("2026-08-15T12:00:00Z", Globalization.CultureInfo.InvariantCulture)
                    SinkStore.witnessWith (Some tempStore) nowUtc cnn.DataSource cnn.Database None [] MetadataSnapshotRunner.defaultParameters snapshot |> ignore
                    let digest = SinkStore.connDigest16 cnn.DataSource cnn.Database
                    let journal =
                        SinkJournal.load (SinkStore.journalPath tempStore digest)
                        |> Result.defaultValue []
                    let outcomes =
                        SinkClaims.adjudicateAll snapshot journal
                        |> List.map (fun (s, o) -> s.Table.ToUpperInvariant(), o)
                        |> Map.ofList

                    // 1) The healthy baseline adopts.
                    let _ =
                        match Map.tryFind "OSUSR_FUL_ORDER" outcomes with
                        | Some (PhysicalClaimRules.PhysicalClaimOutcome.Adopted (w, [])) -> Assert.Equal("Order", w.EntityName)
                        | other -> Assert.Fail (sprintf "Order: expected sole adoption, got %A" other)

                    // 2) The tombstone-only table — the original incident:
                    //    deleted from the eSpace, table intact, and the
                    //    entity's SHAPE still addressable in the witnessed
                    //    edition (its attributes ride the total read).
                    let _ =
                        match Map.tryFind "OSUSR_FUL_INVOICE" outcomes with
                        | Some (PhysicalClaimRules.PhysicalClaimOutcome.TombstoneOnly [ t ]) ->
                            Assert.Equal("Invoice", t.EntityName)
                            Assert.False(t.IsActive)
                            let invoiceAttrs =
                                snapshot.Attributes |> List.filter (fun a -> a.EntityId = t.EntityId)
                            Assert.True(List.length invoiceAttrs >= 1,
                                "the tombstoned entity's attribute shape must be recoverable from the witnessed edition")
                        | other -> Assert.Fail (sprintf "Invoice: expected TombstoneOnly, got %A" other)

                    // 3) The cutover pair: the live extension re-registration
                    //    adopts; the tombstoned original rides as outranked.
                    let _ =
                        match Map.tryFind "OSUSR_FUL_SHIPMENT" outcomes with
                        | Some (PhysicalClaimRules.PhysicalClaimOutcome.Adopted (w, [ o ])) ->
                            Assert.True(w.IsExternalRegistration)
                            Assert.True(w.IsActive)
                            Assert.False(o.IsActive)
                        | other -> Assert.Fail (sprintf "Shipment: expected re-registration adopted over the tombstone, got %A" other)

                    // 4) Two LIVE claims: Contested, eSpace-led recommendation.
                    let _ =
                        match Map.tryFind "OSUSR_FUL_CARRIER" outcomes with
                        | Some (PhysicalClaimRules.PhysicalClaimOutcome.Contested rivals) ->
                            Assert.Equal(2, List.length rivals)
                            Assert.False((List.head rivals).IsExternalRegistration)
                        | other -> Assert.Fail (sprintf "Carrier: expected Contested, got %A" other)

                    // 5) The orphan physical table has NO metadata claim, so
                    //    no set assembles from the edition — the S12 residue
                    //    sweep beside the OSSYS read finds it, and ONLY it:
                    //    the four claimed tables (tombstones included) are
                    //    not residue.
                    Assert.False(Map.containsKey "OSUSR_FUL_ARCHIVE" outcomes)
                    let! residue = SinkResidue.sweep cnn snapshot
                    let _ =
                        match residue with
                        | Ok [ archive ] ->
                            Assert.Equal("OSUSR_FUL_ARCHIVE", archive.Table)
                            match PhysicalClaimRules.adjudicate archive with
                            | PhysicalClaimRules.PhysicalClaimOutcome.Unclaimed -> ()
                            | other -> Assert.Fail (sprintf "expected Unclaimed, got %A" other)
                        | Ok other -> Assert.Fail (sprintf "expected exactly the orphan, got %A" (other |> List.map (fun s -> s.Table)))
                        | Error es -> Assert.Fail (sprintf "the sweep refused: %A" es)
                    return ()
                }))
        finally
            try Directory.Delete(tempStore, true) with _ -> ()
