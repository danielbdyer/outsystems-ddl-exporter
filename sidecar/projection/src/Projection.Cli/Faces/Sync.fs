module Projection.Cli.Faces.Sync
// LINT-ALLOW-FILE: CLI run-face operator-facing prose + Voice payload boxing at the terminal CLI boundary; the structural surface is the typed MovementSpec / Intent / Voice catalog, BCL primitives only at this terminal text edge.

// The sink's naming face — `projection sync <env>` (the data-sink chapter, S6;
// CHAPTER_SINK_OPEN.md). The run itself lives in Pipeline (`SinkSyncRun.run` —
// a forced TOTAL witnessed read through the one LiveModelRead funnel + the
// env-label stamp); this face renders the verdict pair through the Voice
// catalog and classifies refusals through the one `CliExit` table.

open System
open System.Text.Json.Nodes
open Projection.Core
open Projection.Pipeline
open Projection.Cli
open Projection.Cli.OperatorConsole

/// The machine lens (`--format json`): the report as one small envelope —
/// outcome ("witnessed" | "unchanged"), the sync ordinal, and the journaled
/// displacement count for THIS sync (zero under CDC-silence).
let private reportJson (report: SinkSyncRun.SyncReport) : string =
    let o = JsonObject()
    o["env"] <- JsonValue.Create report.EnvLabel
    o["connDigest"] <- JsonValue.Create report.ConnDigest
    match report.Outcome with
    | SinkSyncRun.SyncOutcome.Witnessed (syncId, displacements) ->
        o["outcome"] <- JsonValue.Create "witnessed"
        o["syncId"] <- JsonValue.Create syncId
        o["displacements"] <- JsonValue.Create displacements
    | SinkSyncRun.SyncOutcome.Silent syncId ->
        o["outcome"] <- JsonValue.Create "unchanged"
        o["syncId"] <- JsonValue.Create syncId
        o["displacements"] <- JsonValue.Create 0
    o.ToJsonString()

/// `projection sync <env> [--format json]` — witness the environment's OSSYS
/// metadata into the local sink and stamp the environment's name onto the
/// manifest (the act that makes `sink:<env>` addressable). Read-only against
/// the source (no execute gate); writes only under the sink store root.
let runSync (args: SyncArgs) : int =
    let result = (SinkSyncRun.run args.ConnSpec args.EnvLabel).GetAwaiter().GetResult()
    let exitCode =
        match result with
        | Ok report ->
            if args.AsJson then
                printfn "%s" (reportJson report)
            else
                match report.Outcome with
                | SinkSyncRun.SyncOutcome.Witnessed (syncId, displacements) ->
                    let journal =
                        EstateStoreLocation.storeDir ()
                        |> Option.map (fun root -> SinkStore.journalPath root report.ConnDigest)
                    TtyRenderer.renderVoicedTo Console.Out "sync.completed"
                        (Map.ofList
                            [ "env", box report.EnvLabel
                              "syncId", box syncId
                              "displacements", box displacements
                              yield! (journal |> Option.map (fun p -> "journal", box p) |> Option.toList) ] : Voice.Payload)
                | SinkSyncRun.SyncOutcome.Silent syncId ->
                    TtyRenderer.renderVoicedTo Console.Out "sync.unchanged"
                        (Map.ofList
                            [ "env", box report.EnvLabel
                              "syncId", box syncId ] : Voice.Payload)
            0
        | Error errors ->
            match errors with
            | e :: _ when e.Code = "sink.storeDisabled" ->
                // The one voiced refusal on this face — the store is a
                // configuration prerequisite, so the copy names the remedy
                // (§14) instead of the raw error dump.
                TtyRenderer.renderVoicedTo Console.Error "sink.storeDisabled" Map.empty
            | _ -> printErrors Console.Error errors
            CliExit.classify errors
    dumpBench "sync"
    exitCode
