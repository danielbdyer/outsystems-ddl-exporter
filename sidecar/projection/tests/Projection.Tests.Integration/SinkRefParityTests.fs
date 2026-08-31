namespace Projection.Tests

// K2 — the sink-read parity law (THE_DATA_SINK acceptance; the data-sink
// chapter, S7): a `sink:` read is the exact live pipeline minus the wire, so
// over one estate the live total read and the sink read of the state it
// witnessed are EQUAL as catalogs — total `Assert.Equal<Catalog>`, not a
// field sample. The pinned form (`sink:<env>@<syncId>`) carries the same
// guarantee per edition: after the estate moves, @1 still reproduces the
// first witnessed catalog byte-for-byte at the IR grain.
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
type SinkRefParityTests(fixture: EphemeralContainerFixture) =

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
    member _.``K2: a sink read equals the live total read it witnessed — total Catalog parity, latest and pinned`` () =
        if not (Deploy.Docker.ensureRunning ()) then
            printfn "SKIP SinkRefParity: Docker daemon not reachable."
        else
        let tempStore = Path.Combine(Path.GetTempPath(), "sink-parity-tests", Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory tempStore |> ignore
        try
            withStoreEnv (Some tempStore) (fun () ->
                TaskSync.run (fun () ->
                    fixture.WithEphemeralDatabase "SinkParity" (fun cnn _ -> task {
                        do! Deploy.executeBatch cnn (MetadataExtractionSql.readLifecycleSeed ())
                        let digest = SinkStore.connDigest16 cnn.DataSource cnn.Database

                        // 1) The live total read — it witnesses sync 1 inside
                        //    the funnel. Its catalog is the parity referent.
                        let! liveFirst = LiveModelRead.fromConnection cnn
                        let liveCatalog1 =
                            match liveFirst with
                            | Ok c -> c
                            | Error es -> failwithf "live read refused: %A" es
                        Assert.True(SinkStore.nameEnvironment tempStore digest "uat")

                        // 2) K2, latest form: sink:uat replays the witnessed
                        //    state to the SAME catalog.
                        let! sinkLatest = Source.read (Source.ofSink "uat" None)
                        match sinkLatest with
                        | Error es -> Assert.Fail (sprintf "sink read refused: %A" es)
                        | Ok sinkCatalog -> Assert.Equal<Catalog>(liveCatalog1, sinkCatalog)

                        // 3) The estate moves (tombstone) and is re-witnessed;
                        //    the PINNED read still reproduces edition 1, and
                        //    the new latest equals the new live read.
                        do! Deploy.executeBatch cnn
                                "UPDATE [dbo].[ossys_Entity] SET [Is_Active] = 0 WHERE [Id] = 8000;"
                        let! liveSecond = LiveModelRead.fromConnection cnn
                        let liveCatalog2 =
                            match liveSecond with
                            | Ok c -> c
                            | Error es -> failwithf "second live read refused: %A" es
                        let! sinkPinned = Source.read (Source.ofSink "uat" (Some SyncOrdinal.genesis))
                        match sinkPinned with
                        | Error es -> Assert.Fail (sprintf "pinned sink read refused: %A" es)
                        | Ok pinnedCatalog -> Assert.Equal<Catalog>(liveCatalog1, pinnedCatalog)
                        let! sinkLatest2 = Source.read (Source.ofSink "uat" None)
                        match sinkLatest2 with
                        | Error es -> Assert.Fail (sprintf "second sink read refused: %A" es)
                        | Ok latestCatalog -> Assert.Equal<Catalog>(liveCatalog2, latestCatalog)

                        // 4) The ref algebra resolves the same operands (the
                        //    diff/compare faces ride Ref.resolveCatalog).
                        let! viaRef = Ref.resolveCatalog (Ref.parse "sink:uat@1")
                        match viaRef with
                        | Error es -> Assert.Fail (sprintf "ref resolve refused: %A" es)
                        | Ok c -> Assert.Equal<Catalog>(liveCatalog1, c)
                        return ()
                    })))
        finally
            try Directory.Delete(tempStore, true) with _ -> ()
