module Projection.Cli.Faces.Export
// LINT-ALLOW-FILE: CLI run-face operator-facing prose + Voice payload boxing at the terminal CLI boundary; the structural surface is the typed MovementSpec / Intent / Voice catalog, BCL primitives only at this terminal text edge.

// The `full-export` face (chapter B.4) — the Phase B structural-exit subcommand.
// Extracted verbatim from the RunFaces wall (recon #3 — per-verb file split);
// zero behavior change. Uses the shared `Face` combinator + `nameOf` from
// `Faces.Common`.

open System
open System.Diagnostics
open System.IO
open Projection.Core
open Projection.Adapters.Sql
open Projection.Pipeline
open Projection.Targets.SSDT
open Projection.Cli
open Projection.Cli.OperatorConsole
open Projection.Cli.Faces.Common


// ----------------------------------------------------------------------
// `full-export` (chapter B.4 slice 7) — Phase B structural-exit
// subcommand. Per chapter B.4 mid-rescope + `DECISIONS 2026-05-19
// (slice B.4.{4-7}.rescope)`: THIN scope; wraps today's three live
// `Pipeline.Config` consumers + slice 6's actionable-diagnostics + slice
// 6.5's LogSink emission. NDJSON event stream to stderr conforms to
// `docs/logging-format.md` §3-§13 + §15.1; runSummary terminal event
// per §10.
// ----------------------------------------------------------------------

/// `projection full-export` entry. Delegates the LogSink envelope
/// orchestration (config snapshot, stage timing, diagnostics, terminal
/// `summary.runComplete`) + composition to `FullExportRun.execute` — the
/// single implementation, shared with the test harness so the NDJSON
/// contract has no drift-prone second copy. Here the CLI narrates the
/// operator-facing console output and maps the `RunOutcome` to an exit
/// code. Per §5: NDJSON to stderr (via LogSink, inside `execute`);
/// artifact-path narration to stdout here.
let runFullExport
    (configPath: string)
    (outputOverride: string option)
    (verbosity: LogSink.Verbosity)
    (mutedCategories: Set<LogSink.Category>)
    (storePath: string option)
    (envLabel: string option)
    : int =
    // AC-X3 — the publication bundle. With --lifecycle-store the run reads the
    // prior emission, measures the displacement, accumulates the refactorlog,
    // emits the ChangeManifest, and records one new episode (the bundle a
    // downstream SSIS consumer reconstructs prior state from). Without a store
    // it is the genesis emission — byte-identical to before (storeLeg = None).
    let outcome, storeLeg =
        match storePath with
        | Some store when not (System.String.IsNullOrWhiteSpace store) ->
            let env = parseEnvironment "DEV" envLabel
            match Timeline.create (Projection.Core.Environment.name env) with
            | Error es ->
                // NM-56: name the downgrade. The operator asked for --store, but
                // the env label cannot name a timeline; surface the error (not
                // silent) then fall back to the genesis emission (store leg absent).
                printErrors Console.Error es
                FullExportRun.execute configPath outputOverride verbosity mutedCategories, None
            | Ok tl ->
                let at = System.DateTimeOffset.UtcNow
                FullExportRun.executeWithStore configPath outputOverride verbosity mutedCategories (Some store) tl env at
        | _ ->
            FullExportRun.execute configPath outputOverride verbosity mutedCategories, None
    match outcome with
    | FullExportRun.RunOutcome.Succeeded (report, effectiveOutput) ->
        printfn "%d artifact(s) written to %s." report.Paths.Length effectiveOutput
        report.Paths
        |> List.iter (fun p ->
            let info = FileInfo p
            printfn "  %s (%d bytes)" p info.Length)
        storeLeg
        |> Option.iter (fun leg ->
            printfn "This run recorded — episode %d on timeline %s; %d refactorlog entr(ies) accumulated."
                (EpisodicLifecycle.episodes leg.Chain |> List.length)
                (Timeline.name (EpisodicLifecycle.timeline leg.Chain))
                (List.length leg.AccumulatedRefactorLog))
    | FullExportRun.RunOutcome.ConfigInvalid _ ->
        // config.validationFailed envelopes already emitted by `execute`.
        ()
    | FullExportRun.RunOutcome.RunFailed errors ->
        printErrors Console.Error errors
    | FullExportRun.RunOutcome.Aborted ex ->
        Console.Error.WriteLine ("projection: full-export aborted: " + ex.Message)
    dumpBench "full-export"
    FullExportRun.exitCode outcome

/// AC-X1 (part B) — `projection full-export --load --conn <ref> [--lifecycle-store
/// <path>] [--env <label>] [--out <dir>]`. Publishes the bundle AND loads the
/// idempotent seed into the (already-deployed) sink, measuring the data
/// movement's CDC capture count; with a store, records the episode with the
/// MEASURED `DataObservation`. The seed is a MERGE, so re-running is
/// non-overwriting and CDC-silent.
let runFullExportLoad
    (configPath: string)
    (connSpec: string)
    (outputOverride: string option)
    (storePath: string option)
    (envLabel: string option)
    : int =
    // The error face is the voiced §10/§14 surface alone (`printErrors` →
    // `Voice.errorsSurface`): the statement is the catalog's frame for the
    // primary code, the located causes ride beneath. No command-prefixed
    // header — the frame already names the finding and the next move.
    match TransferSpec.parseConnectionSpec connSpec with
    | Error es ->
        printErrors Console.Error es
        2
    | Ok connRef ->
        match ConnectionResolver.resolve "Sink" connRef with
        | Error es ->
            printErrors Console.Error es
            6
        | Ok connStr ->
            match Config.fromFile configPath with
            | Error es ->
                printErrors Console.Error es
                6
            | Ok cfg0 ->
                let cfg =
                    match outputOverride with
                    | Some o -> { cfg0 with Output = { cfg0.Output with Dir = o } }
                    | None   -> cfg0
                let env = parseEnvironment "DEV" envLabel
                match Timeline.create (Projection.Core.Environment.name env) with
                | Error es ->
                    printErrors Console.Error es
                    2
                | Ok tl ->
                    let at = System.DateTimeOffset.UtcNow
                    let work =
                        task {
                            // The connection OPEN is guarded so a dead/unreachable sink
                            // surfaces as the named connection-axis refusal (exit 6,
                            // matching transfer/migrate's `openSubstrate`) rather than an
                            // uncaught `SqlException` crashing the verb. The ref-resolve
                            // axis was already handled above (exit 6); this closes the
                            // open-failure leg that previously escaped the Result channel.
                            let sink = new Microsoft.Data.SqlClient.SqlConnection(connStr)
                            let! opened =
                                task {
                                    try
                                        do! sink.OpenAsync()
                                        return Ok ()
                                    with ex ->
                                        return Result.failure [ ValidationError.create "transfer.connection.openFailed" (sprintf "Sink connection: failed to open — %s" ex.Message) ]
                                }
                            match opened with
                            | Error es ->
                                sink.Dispose()
                                return Error es
                            | Ok () ->
                                use sink = sink
                                // Card P2 — the seed loads leveled-parallel: the executor
                                // closes over the connection STRING (per-segment pooled
                                // opens); `sink` stays the CDC measure's connection.
                                return! Compose.runWithConfigAndLoad Deploy.cdcCaptureTotal (Deploy.executeLeveledSeed connStr) cfg sink storePath tl env at
                        }
                    let code =
                        match work.GetAwaiter().GetResult() with
                        | Ok (report, legOpt, cdcDelta) ->
                            // §6 — the CDC measure rides channel 1 (structured) so
                            // the verdict panel reads the data norm; the console
                            // gets the Voice surface below.
                            LogSink.emit (EventProjection.cdcMeasureEnvelope cdcDelta)
                            // §4/§6 — the publish-and-load verdict, voiced by code
                            // (`load.completed`): statement first, the CDC measure
                            // as the grounding evidence beneath.
                            TtyRenderer.renderVoicedTo Console.Out "load.completed"
                                (Map.ofList
                                    [ "artifactCount", box report.Paths.Length
                                      "capturedRows",  box cdcDelta ])
                            legOpt
                            |> Option.iter (fun leg ->
                                // §13 — the durable record, stated plainly.
                                TtyRenderer.renderVoicedTo Console.Out "episode.recorded"
                                    (Map.ofList
                                        [ "episodeCount", box (EpisodicLifecycle.episodes leg.Chain |> List.length)
                                          "timeline",     box (Timeline.name (EpisodicLifecycle.timeline leg.Chain)) ]))
                            0
                        | Error es ->
                            // Single-source the exit through `classify`: a guarded
                            // connection-open failure is the connection axis (6, matching
                            // transfer/migrate); a genuine load/execution error keeps the
                            // unclassified-refusal default (3). No silent flattening.
                            printErrors Console.Error es
                            (Preflight.refusalOf es).ExitCode
                    dumpBench "full-export"
                    code
