namespace Projection.Pipeline

open System
open System.IO
open System.Diagnostics
open Projection.Core

/// The full-export run lifecycle, expressed as the structured LogSink
/// envelope stream around a `Compose.runWithConfig` composition. This is
/// the *single* implementation of that orchestration: `Program.fs` (the
/// CLI) and the test harness both consume it, so the NDJSON contract
/// (`docs/logging-format.md` §7 + §10–§11) has no second, drift-prone
/// copy. Per the Pipeline composition-surface philosophy, the LogSink
/// emission lives here; `Program.fs` owns argv parsing, exit codes, and
/// console narration, choosing those from the returned `RunOutcome`.
[<RequireQualifiedAccess>]
module FullExportRun =

    /// What the run produced, for the CLI's console layer to narrate and
    /// map to an exit code. The LogSink envelope stream is emitted by
    /// `execute` itself (the part tests capture); this DU carries only
    /// what the console needs to reproduce the operator-facing output.
    [<RequireQualifiedAccess>]
    type RunOutcome =
        | Succeeded of report: Compose.RunReport * effectiveOutput: string
        | ConfigInvalid of errors: ValidationError list
        | RunFailed of errors: ValidationError list
        | Aborted of error: exn

    /// CLI exit code per the §7 contract: 0 success, 6 config-invalid,
    /// 2 run-failure / abort.
    let exitCode (outcome: RunOutcome) : int =
        match outcome with
        | RunOutcome.Succeeded _    -> 0
        | RunOutcome.ConfigInvalid _ -> 6
        | RunOutcome.RunFailed _     -> 2
        | RunOutcome.Aborted _       -> 2

    /// Map a stage's duration to a `LogSink.recordStage` +
    /// `summary.stageCompleted` envelope pair (both ends, so the
    /// runSummary stage table is populated alongside the per-stage end
    /// event).
    let private recordStage (stageName: string) (outcome: LogSink.Outcome) (durationMs: int64) : unit =
        LogSink.recordStage stageName durationMs outcome
        let payload : Map<string, objnull> =
            Map.ofList [
                "stage",      box stageName
                "durationMs", box durationMs
                "outcome",    box (LogSink.outcomeToString outcome)
            ]
        LogSink.emit
            { LogSink.envelope LogSink.Info LogSink.Summary "summary.stageCompleted" payload with
                Phase  = LogSink.End
                StepId = Some stageName }

    /// One `config.validationFailed` envelope per config ValidationError
    /// (§7.1) so the operator can grep / jq each independently.
    let private emitConfigErrors (errors: ValidationError list) : unit =
        for e in errors do
            let payload : Map<string, objnull> =
                Map.ofList [ "code", box e.Code; "reason", box e.Message ]
            LogSink.emit
                { LogSink.envelope LogSink.Error LogSink.Config "config.validationFailed" payload with
                    Phase = LogSink.ErrorPhase }

    /// One `transform.diagnostic` envelope per emit-phase ValidationError
    /// (§7.4 — level matches severity; Error → `error`).
    let private emitTransformErrors (errors: ValidationError list) : unit =
        for e in errors do
            let payload : Map<string, objnull> =
                Map.ofList [ "code", box e.Code; "message", box e.Message ]
            LogSink.emit
                { LogSink.envelope LogSink.Error LogSink.Transform "transform.diagnostic" payload with
                    Phase = LogSink.ErrorPhase }

    /// One `transform.diagnostic` envelope per `SpecialCircumstances`
    /// entry (§7.4; level mirrors `Severity`; typed `Metadata` —
    /// including `acceptedVia` on operator-allowlisted findings —
    /// flattens into the payload).
    let private emitSpecialCircumstancesDiagnostics (entries: DiagnosticEntry list) : unit =
        for entry in entries do
            let level =
                match entry.Severity with
                | DiagnosticSeverity.Info    -> LogSink.Info
                | DiagnosticSeverity.Warning -> LogSink.Warn
                | DiagnosticSeverity.Error   -> LogSink.Error
            let basePayload : Map<string, objnull> =
                Map.ofList [
                    "source",  box entry.Source
                    "code",    box entry.Code
                    "message", box entry.Message
                ]
            let withSsKey =
                match entry.SsKey with
                | Some k -> basePayload |> Map.add "ssKey" (box (SsKey.rootOriginal k))
                | None   -> basePayload
            let payload =
                entry.Metadata
                |> Map.fold (fun acc k v -> Map.add k (box v) acc) withSsKey
            LogSink.emit
                { LogSink.envelope level LogSink.Transform "transform.diagnostic" payload with
                    Phase = LogSink.End }

    /// CLI `--output` override wins over the config's `Output.Dir`.
    let private resolveOutputDir (cfg: Config.Config) (outputOverride: string option) : string =
        match outputOverride with
        | Some dir when not (String.IsNullOrWhiteSpace dir) -> dir
        | _ -> cfg.Output.Dir

    /// `config.runStart` (command + configPath) then
    /// `config.connectionResolved` (SnapshotJson source; secrets absent
    /// by construction per D9) at run start (§7.1).
    let private emitConfigSnapshot (cfg: Config.Config) (configPath: string) (effectiveOutput: string) : unit =
        let startPayload : Map<string, objnull> =
            Map.ofList [
                "command",    box "projection full-export"
                "configPath", box configPath
                "outputDir",  box effectiveOutput
            ]
        LogSink.emit
            { LogSink.envelope LogSink.Info LogSink.Config "config.runStart" startPayload with
                Phase = LogSink.Start }
        let connPayload : Map<string, objnull> =
            Map.ofList [ "kind", box "SnapshotJson"; "modelPath", box cfg.Model.Path ]
        LogSink.emit
            { LogSink.envelope LogSink.Info LogSink.Config "config.connectionResolved" connPayload with
                Phase  = LogSink.Start
                Source = Some LogSink.Configuration }

    /// Run a full export under the structured LogSink stream. Resets the
    /// sink + bench state, applies verbosity / muted categories, emits the
    /// config snapshot, delegates composition to `Compose.runWithConfig`,
    /// records the pipeline stage + diagnostics + artifacts, and always
    /// emits the terminal `summary.runComplete` (the §10 mandatory exit
    /// event). Synchronous (drives the `runWithConfig` task to completion)
    /// to keep the resumable-state-machine surface minimal. Console
    /// narration + bench dump are the caller's (CLI's) concern.
    let execute
        (configPath: string)
        (outputOverride: string option)
        (verbosity: LogSink.Verbosity)
        (mutedCategories: Set<LogSink.Category>)
        : RunOutcome =
        LogSink.reset ()
        LogSink.setVerbosity verbosity
        LogSink.setMutedCategories mutedCategories
        Bench.reset ()
        let mutable outcome : LogSink.Outcome = LogSink.Succeeded
        try
            try
                match Config.fromFile configPath with
                | Error errors ->
                    emitConfigErrors errors
                    outcome <- LogSink.Failed
                    RunOutcome.ConfigInvalid errors
                | Ok cfg ->
                    let effectiveOutput = resolveOutputDir cfg outputOverride
                    let cfgForRun = { cfg with Output = { Dir = effectiveOutput } }
                    emitConfigSnapshot cfgForRun configPath effectiveOutput
                    let sw = Stopwatch.StartNew()
                    let result = (Compose.runWithConfig cfgForRun).GetAwaiter().GetResult()
                    sw.Stop()
                    match result with
                    | Ok report ->
                        recordStage "pipeline" LogSink.Succeeded sw.ElapsedMilliseconds
                        emitSpecialCircumstancesDiagnostics report.Diagnostics
                        report.Paths
                        |> List.iter (fun p ->
                            let info = FileInfo p
                            LogSink.recordArtifact {
                                Kind      = Path.GetFileName p |> nonNull
                                Path      = p
                                SizeBytes = Some info.Length
                                FileCount = None
                            })
                        RunOutcome.Succeeded (report, effectiveOutput)
                    | Error errors ->
                        recordStage "pipeline" LogSink.Failed sw.ElapsedMilliseconds
                        emitTransformErrors errors
                        outcome <- LogSink.Failed
                        RunOutcome.RunFailed errors
            with ex ->
                outcome <- LogSink.Failed
                let payload : Map<string, objnull> =
                    Map.ofList [
                        "exception", box (ex.GetType().Name)
                        "message",   box ex.Message
                    ]
                LogSink.emit
                    { LogSink.envelope LogSink.Error LogSink.Config "config.validationFailed" payload with
                        Phase = LogSink.ErrorPhase }
                RunOutcome.Aborted ex
        finally
            let benchStats = Bench.snapshot ()
            LogSink.runComplete outcome "projection full-export" benchStats |> ignore
