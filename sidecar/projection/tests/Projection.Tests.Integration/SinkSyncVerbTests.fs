namespace Projection.Tests

// `projection sync <env>` end-to-end (the data-sink chapter, S6;
// CHAPTER_SINK_OPEN.md): the sink's naming verb drives the REAL funnel over
// the lifecycle seed under a per-test temp store — first sync witnesses (and
// stamps the operator's environment label onto the manifest), an unchanged
// re-sync is the metadata plane's CDC-silence (Silent, nothing journaled), a
// mutation is witnessed as the next sync, and a run with no store root is the
// named `sink.storeDisabled` refusal.
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
type SinkSyncVerbTests(fixture: EphemeralContainerFixture) =

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
    member _.``sync witnesses + names the environment; an unchanged re-sync is Silent; a mutation is the next sync`` () =
        if not (Deploy.Docker.ensureRunning ()) then
            printfn "SKIP SinkSyncVerb: Docker daemon not reachable."
        else
        let tempStore = Path.Combine(Path.GetTempPath(), "sink-sync-verb-tests", Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory tempStore |> ignore
        try
            withStoreEnv (Some tempStore) (fun () ->
                TaskSync.run (fun () ->
                    fixture.WithEphemeralDatabase "SinkSync" (fun cnn connStr -> task {
                        do! Deploy.executeBatch cnn (MetadataExtractionSql.readLifecycleSeed ())

                        // 1) The first sync witnesses sync 1 and stamps the label.
                        let! first = SinkSyncRun.run connStr "uat"
                        match first with
                        | Error es -> Assert.Fail (sprintf "first sync refused: %A" es)
                        | Ok report ->
                            Assert.Equal("uat", report.EnvLabel)
                            match report.Outcome with
                            | SinkSyncRun.SyncOutcome.Witnessed (s, displacements) when s = SyncOrdinal.genesis ->
                                Assert.True(displacements > 0, "genesis sync journals the appearing rows")
                            | other -> Assert.Fail (sprintf "expected Witnessed sync 1, got %A" other)
                            let manifest = SinkStore.loadManifest tempStore report.ConnDigest
                            Assert.True(manifest.IsSome)
                            Assert.Equal(Some "uat", manifest.Value.EnvLabel)

                        // 2) An unchanged re-sync is the CDC-silence: Silent, sync 1 stands.
                        let! second = SinkSyncRun.run connStr "uat"
                        match second |> Result.map (fun r -> r.Outcome) with
                        | Ok (SinkSyncRun.SyncOutcome.Silent s) when s = SyncOrdinal.genesis -> ()
                        | other -> Assert.Fail (sprintf "expected Silent sync 1, got %A" other)

                        // 3) A metadata mutation → the next sync witnesses sync 2.
                        do! Deploy.executeBatch cnn
                                "UPDATE [dbo].[ossys_Entity] SET [Is_Active] = 0 WHERE [Id] = 8000;"
                        let! third = SinkSyncRun.run connStr "uat"
                        match third |> Result.map (fun r -> r.Outcome) with
                        | Ok (SinkSyncRun.SyncOutcome.Witnessed (s, displacements)) when SyncOrdinal.value s = 2 ->
                            Assert.True(displacements > 0, "the tombstone displacement is journaled")
                        | other -> Assert.Fail (sprintf "expected Witnessed sync 2, got %A" other)
                        return ()
                    })))
        finally
            try Directory.Delete(tempStore, true) with _ -> ()

    [<Fact>]
    member _.``sync with no store root is the named sink.storeDisabled refusal`` () =
        if not (Deploy.Docker.ensureRunning ()) then
            printfn "SKIP SinkSyncVerbDisabled: Docker daemon not reachable."
        else
        withStoreEnv None (fun () ->
            TaskSync.run (fun () ->
                fixture.WithEphemeralDatabase "SinkSyncOff" (fun cnn connStr -> task {
                    do! Deploy.executeBatch cnn (MetadataExtractionSql.readLifecycleSeed ())
                    let! result = SinkSyncRun.run connStr "uat"
                    match result with
                    | Error (e :: _) -> Assert.Equal("sink.storeDisabled", e.Code)
                    | other -> Assert.Fail (sprintf "expected the storeDisabled refusal, got %A" other)
                    return ()
                })))
