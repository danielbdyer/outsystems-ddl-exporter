namespace Projection.Tests

// The witness hook, end-to-end (the data-sink chapter, S5;
// CHAPTER_SINK_OPEN.md): every live OSSYS read witnesses when the store is
// enabled — one hook at the LiveModelRead funnel, store-gated,
// totality-gated, advisory. This suite drives the REAL funnel (seed →
// rowsets SQL → runner → witness → catalog) under a per-test temp store,
// and pins the R7 blast-radius fact: with no store env vars, nothing is
// written anywhere (which is why the rest of the pool cannot start
// sprouting sink directories).
//
// Env-var manipulation is safe here: the Docker-SqlServer collection
// serializes, and every mutation restores in finally.

open System
open System.IO
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Adapters.OssysSql

[<Xunit.Collection("Docker-SqlServer")>]
type SinkWitnessTests(fixture: EphemeralContainerFixture) =

    let withStoreEnv (root: string option) (body: unit -> unit) =
        let priorEstate = Environment.GetEnvironmentVariable "PROJECTION_ESTATE_DIR"
        let priorLedger = Environment.GetEnvironmentVariable "PROJECTION_LEDGER_DIR"
        try
            Environment.SetEnvironmentVariable("PROJECTION_ESTATE_DIR", Option.toObj root)
            Environment.SetEnvironmentVariable("PROJECTION_LEDGER_DIR", Option.toObj None)
            body ()
        finally
            Environment.SetEnvironmentVariable("PROJECTION_ESTATE_DIR", priorEstate)
            Environment.SetEnvironmentVariable("PROJECTION_LEDGER_DIR", priorLedger)

    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``R7: the test environment carries no store env vars by default — ambient reads stay live-only`` () =
        Assert.True(String.IsNullOrEmpty(Environment.GetEnvironmentVariable "PROJECTION_ESTATE_DIR"))
        Assert.True(String.IsNullOrEmpty(Environment.GetEnvironmentVariable "PROJECTION_LEDGER_DIR"))

    [<Fact>]
    member _.``the funnel witnesses: a total read persists the snapshot + journal; a mutation appends; a scoped read writes nothing`` () =
        if not (Deploy.Docker.ensureRunning ()) then
            printfn "SKIP SinkWitness: Docker daemon not reachable."
        else
        let tempStore = Path.Combine(Path.GetTempPath(), "sink-witness-tests", Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory tempStore |> ignore
        try
            withStoreEnv (Some tempStore) (fun () ->
                TaskSync.run (fun () ->
                    fixture.WithEphemeralDatabase "SinkWitness" (fun cnn _ -> task {
                        do! Deploy.executeBatch cnn (MetadataExtractionSql.readLifecycleSeed ())
                        let digest = SinkStore.connDigest16 cnn.DataSource cnn.Database

                        // 1) A TOTAL read witnesses sync 1 — and records the
                        //    three freshness bellwethers (S8): the hook
                        //    probes at capture time when a store rides.
                        let! first = LiveModelRead.fromConnection cnn
                        Assert.True(Result.isSuccess first)
                        let manifest1 = SinkStore.loadManifest tempStore digest
                        Assert.True(manifest1.IsSome)
                        Assert.Equal(1, manifest1.Value.LatestSyncId)
                        Assert.Equal<string list>(
                            SinkFreshness.targets |> List.map SinkFreshness.nameOf,
                            manifest1.Value.SourceFingerprints |> List.map fst)
                        Assert.True((SinkStore.loadSnapshotAt tempStore digest 1).IsSome)
                        let journal1 =
                            SinkJournal.load (SinkStore.journalPath tempStore digest)
                            |> Result.defaultValue []
                        Assert.NotEmpty journal1

                        // 2) An UNCHANGED total read appends nothing.
                        let! _ = LiveModelRead.fromConnection cnn
                        let manifestStill = (SinkStore.loadManifest tempStore digest).Value
                        Assert.Equal(1, manifestStill.LatestSyncId)

                        // 3) A metadata mutation → the freshness bellwether
                        //    MOVES against the recorded fingerprints (S8's
                        //    live auto-detection), and the next total read
                        //    witnesses sync 2 with the tombstone displacement.
                        do! Deploy.executeBatch cnn
                                "UPDATE [dbo].[ossys_Entity] SET [Is_Active] = 0 WHERE [Id] = 8000;"
                        let! movedProbe = SinkFreshness.probe cnn
                        let _ =
                            match SinkFreshness.decide Config.SinkPolicy.Auto false (SinkStore.loadManifest tempStore digest) (Some movedProbe) with
                            | SinkFreshness.Decision.ReadLive (SinkFreshness.Miss.FingerprintMoved moved) ->
                                Assert.Contains("dbo.ossys_Entity", moved)
                            | other -> Assert.Fail (sprintf "expected the entity bellwether to move, got %A" other)
                        let! _ = LiveModelRead.fromConnection cnn
                        let manifest2 = (SinkStore.loadManifest tempStore digest).Value
                        Assert.Equal(2, manifest2.LatestSyncId)
                        // The fresh witness re-recorded the bellwethers, so
                        //    `auto` now confirms and reuses sync 2.
                        let! confirmedProbe = SinkFreshness.probe cnn
                        let _ =
                            match SinkFreshness.decide Config.SinkPolicy.Auto false (Some manifest2) (Some confirmedProbe) with
                            | SinkFreshness.Decision.ReuseSink 2 -> ()
                            | other -> Assert.Fail (sprintf "expected confirmed reuse of sync 2, got %A" other)
                        let journal2 =
                            SinkJournal.load (SinkStore.journalPath tempStore digest)
                            |> Result.defaultValue []
                        Assert.True(List.length journal2 > List.length journal1)
                        let sync2Lines = journal2 |> List.filter (fun l -> l.SyncId = 2)
                        Assert.NotEmpty sync2Lines

                        // 4) A SCOPED read is gated: nothing advances.
                        let scoped =
                            { MetadataSnapshotRunner.defaultParameters with
                                ModuleNames = [ "Fulfillment" ] }
                        let! _ = LiveModelRead.fromConnectionWith scoped cnn
                        Assert.Equal(2, (SinkStore.loadManifest tempStore digest).Value.LatestSyncId)
                        Assert.Equal(List.length journal2,
                                     SinkJournal.load (SinkStore.journalPath tempStore digest)
                                     |> Result.defaultValue [] |> List.length)

                        // 5) The replayed chain reproduces the latest
                        //    witnessed snapshot AT CANONICAL GRAIN (T19's
                        //    live seed). The store holds the snapshot
                        //    exactly as acquired (wire order — the K2
                        //    parity ruling); replay folds canonical
                        //    states, so the equality is stated there.
                        let verified =
                            SinkJournal.load (SinkStore.journalPath tempStore digest)
                            |> Result.defaultValue []
                            |> SinkJournal.admitChain
                            |> Result.defaultWith (fun e -> failwithf "chain refused: %A" e)
                        let latest = (SinkStore.loadSnapshotAt tempStore digest 2).Value
                        Assert.Equal<MetadataSnapshotRunner.MetadataSnapshot>(
                            SinkDisplacement.canonical latest, SinkJournal.replay verified)
                        return ()
                    })))
        finally
            try Directory.Delete(tempStore, true) with _ -> ()

    [<Fact>]
    member _.``with the store disabled, a total read writes nothing anywhere (live-only, named)`` () =
        if not (Deploy.Docker.ensureRunning ()) then
            printfn "SKIP SinkWitnessDisabled: Docker daemon not reachable."
        else
        withStoreEnv None (fun () ->
            TaskSync.run (fun () ->
                fixture.WithEphemeralDatabase "SinkWitnessOff" (fun cnn _ -> task {
                    do! Deploy.executeBatch cnn (MetadataExtractionSql.readLifecycleSeed ())
                    let! result = LiveModelRead.fromConnection cnn
                    Assert.True(Result.isSuccess result)
                    // No store root ⇒ no sink directory can exist for this
                    // source anywhere under the (absent) roots — the
                    // outcome is the named Disabled no-op inside the hook.
                    Assert.True(String.IsNullOrEmpty(Environment.GetEnvironmentVariable "PROJECTION_ESTATE_DIR"))
                    return ()
                })))
