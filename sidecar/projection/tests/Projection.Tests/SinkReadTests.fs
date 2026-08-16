namespace Projection.Tests

// `sink:<env>[@<syncId>]` resolution (the data-sink chapter, S7;
// CHAPTER_SINK_OPEN.md): the manifest env-label scan is config-free and
// offline-true, and every miss is its own named refusal — no store, unknown
// name, ambiguous name, out-of-range pin. Pure-pool over temp stores; the
// env-var manipulation rides the Global-MutableState collection (the RefTests
// precedent) and restores in finally.

open System
open System.IO
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Adapters.OssysSql

[<Xunit.Collection("Global-MutableState")>]
module SinkReadTests =

    // align-III.1: expected-ordinal literal for asserts.
    let private ord (n: int) : SyncOrdinal =
        match SyncOrdinal.create n with
        | Ok o -> o
        | Error m -> failwith m

    let private t1 = DateTimeOffset.Parse("2026-08-10T12:00:00Z", Globalization.CultureInfo.InvariantCulture)
    let private t2 = DateTimeOffset.Parse("2026-08-14T12:00:00Z", Globalization.CultureInfo.InvariantCulture)

    let private withStoreEnv (root: string option) (body: unit -> unit) =
        let priorEstate = Environment.GetEnvironmentVariable "PROJECTION_ESTATE_DIR"
        let priorLedger = Environment.GetEnvironmentVariable "PROJECTION_LEDGER_DIR"
        try
            Environment.SetEnvironmentVariable("PROJECTION_ESTATE_DIR", Option.toObj root)
            Environment.SetEnvironmentVariable("PROJECTION_LEDGER_DIR", Option.toObj None)
            body ()
        finally
            Environment.SetEnvironmentVariable("PROJECTION_ESTATE_DIR", priorEstate)
            Environment.SetEnvironmentVariable("PROJECTION_LEDGER_DIR", priorLedger)

    let private withTempStore (test: string -> unit) =
        let root = Path.Combine(Path.GetTempPath(), "sink-read-tests", Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory root |> ignore
        try withStoreEnv (Some root) (fun () -> test root)
        finally
            try Directory.Delete(root, true) with _ -> ()

    let private snapshotA () =
        OssysSnapshotBuilders.snapshotOf
            [ OssysSnapshotBuilders.moduleRow 800 "Fulfillment" ]
            [ OssysSnapshotBuilders.entityRow 8000 800 "Order" "OSUSR_FUL_ORDER" ]
            [ OssysSnapshotBuilders.identifierRow 80001 8000 ]

    let private snapshotB () =
        OssysSnapshotBuilders.snapshotOf
            [ OssysSnapshotBuilders.moduleRow 800 "Fulfillment" ]
            [ { OssysSnapshotBuilders.entityRow 8000 800 "Order" "OSUSR_FUL_ORDER" with IsActive = false } ]
            [ OssysSnapshotBuilders.identifierRow 80001 8000 ]

    /// Witness one source and stamp its label — the store a `sink:<env>` ref
    /// resolves against (the sync verb's two acts, replayed directly).
    let private witnessNamed (root: string) (at: DateTimeOffset) (server: string) (db: string) (label: string) snapshot =
        SinkStore.witnessWith (Some root) at server db None [] MetadataSnapshotRunner.defaultParameters snapshot |> ignore
        let digest = SinkStore.connDigest16 server db
        SinkStore.nameEnvironment root digest label |> ignore
        digest

    let private primaryCode (r: Result<'a>) : string =
        match r with
        | Error (e :: _) -> e.Code
        | Error [] -> "<empty>"
        | Ok _ -> "<ok>"

    [<Fact>]
    let ``resolve with no store configured is the named sink.storeDisabled refusal`` () =
        withStoreEnv None (fun () ->
            Assert.Equal("sink.storeDisabled", primaryCode (SinkRead.resolve "uat" None)))

    [<Fact>]
    let ``resolve of an unstamped name is the named sink.envUnknown refusal`` () =
        withTempStore (fun root ->
            witnessNamed root t1 "server-a" "db" "uat" (snapshotA ()) |> ignore
            Assert.Equal("sink.envUnknown", primaryCode (SinkRead.resolve "prod" None)))

    [<Fact>]
    let ``two sources claiming one name is the named sink.envAmbiguous refusal`` () =
        withTempStore (fun root ->
            witnessNamed root t1 "server-a" "db" "uat" (snapshotA ()) |> ignore
            witnessNamed root t1 "server-b" "db" "uat" (snapshotA ()) |> ignore
            Assert.Equal("sink.envAmbiguous", primaryCode (SinkRead.resolve "uat" None)))

    [<Fact>]
    let ``a pin outside the witnessed range is the named sink.syncNotFound refusal`` () =
        withTempStore (fun root ->
            witnessNamed root t1 "server-a" "db" "uat" (snapshotA ()) |> ignore
            Assert.Equal("sink.syncNotFound", primaryCode (SinkRead.resolve "uat" (Some (ord 2)))))
            // align-III.1: a below-1 pin is UNREPRESENTABLE here — the
            // ordinal VO stops it at the ref grammar (RefTests pins that
            // "sink:uat@0" stays label text), so `resolve` no longer owns
            // that refusal.

    [<Fact>]
    let ``resolve reads latest by default and a pinned edition by its own capture time`` () =
        withTempStore (fun root ->
            let digest = witnessNamed root t1 "server-a" "db" "uat" (snapshotA ())
            // The estate moves; the second witness is sync 2 at t2.
            SinkStore.witnessWith (Some root) t2 "server-a" "db" None [] MetadataSnapshotRunner.defaultParameters (snapshotB ()) |> ignore
            match SinkRead.resolve "uat" None with
            | Error es -> Assert.Fail (sprintf "latest resolve refused: %A" es)
            | Ok latest ->
                Assert.Equal(digest, latest.Digest)
                Assert.Equal(ord 2, latest.SyncId)
                Assert.Equal(t2, latest.CapturedAtUtc)
            match SinkRead.resolve "uat" (Some (ord 1)) with
            | Error es -> Assert.Fail (sprintf "pinned resolve refused: %A" es)
            | Ok pinned ->
                Assert.Equal(ord 1, pinned.SyncId)
                // The pinned edition's age comes from ITS journal line, not
                // the manifest's latest capture time (the freshness line must
                // describe the edition the read actually rides).
                Assert.Equal(t1, pinned.CapturedAtUtc))

    [<Fact>]
    let ``identityOf round-trips through Ref.parse (one scheme, one writer)`` () =
        Assert.Equal(Ref.Sink ("uat", None), Ref.parse (SinkRead.identityOf "uat" None))
        Assert.Equal(Ref.Sink ("uat", Some (ord 7)), Ref.parse (SinkRead.identityOf "uat" (Some (ord 7))))

    [<Fact>]
    let ``resolveByConnectionString derives the SAME digest the witness hook records (R3 — the two sides agree)`` () =
        withTempStore (fun root ->
            witnessNamed root t1 "server-a" "db" "uat" (snapshotA ()) |> ignore
            match SinkRead.resolveByConnectionString "Server=server-a;Database=db;TrustServerCertificate=True" None with
            | Ok resolved ->
                Assert.Equal(SinkStore.connDigest16 "server-a" "db", resolved.Digest)
                Assert.Equal(ord 1, resolved.SyncId)
            | Error es -> Assert.Fail (sprintf "digest-side resolve refused: %A" es))

    [<Fact>]
    let ``resolveByConnectionString refuses by name: unparseable string; no witnessed state`` () =
        withTempStore (fun _ ->
            Assert.Equal("sink.connUnresolvable", primaryCode (SinkRead.resolveByConnectionString "not==a;;;valid==string" None))
            Assert.Equal("sink.noWitnessedState", primaryCode (SinkRead.resolveByConnectionString "Server=nowhere;Database=nada" None)))
