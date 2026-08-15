namespace Projection.Tests

// Offline operation through the sink (the data-sink chapter, S13): the
// CONFIG POLICY is the offline lever — no new verb flag. Under
// `sink.policy = pinned` a model read serves the witnessed edition
// WITHOUT touching the wire (stale BY CHOICE — the pin's contract; the
// freshness line says so); `refresh` (the per-run override) beats the
// pin, pays the wire, and witnesses the fresh edition; under `auto` an
// unchanged estate is bellwether-confirmed and reused (the pay-once
// fast path). The offline serving is K2-honest: the sink-served
// catalog EQUALS the catalog the live read produced when the state was
// witnessed. And two witnessed editions give the temporal diff two
// `live:` reads can't legitimately give (S10's derived view).
//
// The model section binds the TOTAL attribute axis
// (`onlyActiveAttributes = false`): the attribute axis is A49's named
// residual, so only a total-shaped model read is sink-servable at all.
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
type SinkOfflineReadTests(fixture: EphemeralContainerFixture) =

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
    member _.``the policy is the lever: pinned serves the witnessed edition offline; refresh beats the pin; auto confirms and reuses`` () =
        if not (Deploy.Docker.ensureRunning ()) then
            printfn "SKIP SinkOfflineRead: Docker daemon not reachable."
        else
        let tempStore = Path.Combine(Path.GetTempPath(), "sink-offline-tests", Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory tempStore |> ignore
        try
            withStoreEnv (Some tempStore) (fun () ->
                TaskSync.run (fun () ->
                    fixture.WithEphemeralDatabase "SinkOffline" (fun cnn connStr -> task {
                        do! Deploy.executeBatch cnn (MetadataExtractionSql.readLifecycleSeed ())
                        let digest = SinkStore.connDigest16 cnn.DataSource cnn.Database
                        let pinned =
                            { Config.defaultConfig with
                                Model =
                                    { Config.defaultConfig.Model with
                                        Ossys = Some connStr
                                        OnlyActiveAttributes = false }
                                Sink = { Policy = Config.SinkPolicy.Pinned; PerEnvironment = [] } }
                        let orderOf (c: Catalog) =
                            Catalog.allKinds c |> List.find (fun k -> Name.value k.Name = "Order")

                        // 1) The FIRST pinned read: no witnessed state exists,
                        //    so the miss (NoWitnessedState) pays the wire —
                        //    and the total read WITNESSES sync 1 (pay once).
                        //    R3 proven live here: the digest the STRING side
                        //    derives (resolveByConnectionString inside the
                        //    read) agrees with the one the HOOK derived off
                        //    the open connection, or step 3 could not serve.
                        let! first = Compose.readConfigModelWith false pinned
                        let c1 =
                            match first with
                            | Ok c -> c
                            | Error es -> failwithf "first read refused: %A" es
                        Assert.Equal(1, (SinkStore.loadManifest tempStore digest).Value.LatestSyncId)
                        Assert.True((orderOf c1).IsActive)

                        // 2) The estate mutates AFTER the witness — the
                        //    tombstone the chapter's incident starts with.
                        do! Deploy.executeBatch cnn
                                "UPDATE [dbo].[ossys_Entity] SET [Is_Active] = 0 WHERE [Id] = 8000;"

                        // 3) The SECOND pinned read serves sync 1 WITHOUT
                        //    touching the wire: stale BY CHOICE. The mutation
                        //    is invisible, the manifest hasn't advanced (no
                        //    wire was paid), and the serving is K2-honest —
                        //    the catalog EQUALS the live read's, totally.
                        let! second = Compose.readConfigModelWith false pinned
                        let c2 =
                            match second with
                            | Ok c -> c
                            | Error es -> failwithf "pinned read refused: %A" es
                        Assert.Equal<Catalog>(c1, c2)
                        Assert.True((orderOf c2).IsActive,
                            "the pin serves the WITNESSED edition — the mutation stays invisible until refresh")
                        Assert.Equal(1, (SinkStore.loadManifest tempStore digest).Value.LatestSyncId)

                        // 4) REFRESH beats the pin: the wire is paid, the
                        //    tombstone is visible, and the fresh edition is
                        //    witnessed as sync 2.
                        let! third = Compose.readConfigModelWith true pinned
                        let c3 =
                            match third with
                            | Ok c -> c
                            | Error es -> failwithf "refresh read refused: %A" es
                        Assert.False((orderOf c3).IsActive, "refresh pays the wire — the tombstone is visible")
                        Assert.Equal(2, (SinkStore.loadManifest tempStore digest).Value.LatestSyncId)

                        // 5) AUTO over the unchanged estate: the bellwethers
                        //    confirm sync 2 and the read serves it — no third
                        //    witness, and K2 parity again at the model seam.
                        let auto = { pinned with Sink = { Policy = Config.SinkPolicy.Auto; PerEnvironment = [] } }
                        let! fourth = Compose.readConfigModelWith false auto
                        let c4 =
                            match fourth with
                            | Ok c -> c
                            | Error es -> failwithf "auto read refused: %A" es
                        Assert.Equal<Catalog>(c3, c4)
                        Assert.Equal(2, (SinkStore.loadManifest tempStore digest).Value.LatestSyncId)

                        // 6) The temporal dividend: two witnessed editions
                        //    carry the diff two `live:` reads can't
                        //    legitimately give — and the window's substance
                        //    is exactly the activity flip (norm > 0).
                        let s1 = (SinkStore.loadSnapshotAt tempStore digest 1).Value
                        let s2 = (SinkStore.loadSnapshotAt tempStore digest 2).Value
                        match! SinkDiffView.catalogDiffOf s1 s2 with
                        | Ok view -> Assert.True(CatalogDiff.norm view > 0, "the witnessed window carries the activity flip")
                        | Error es -> failwithf "the temporal view refused: %A" es
                        return ()
                    })))
        finally
            try Directory.Delete(tempStore, true) with _ -> ()
