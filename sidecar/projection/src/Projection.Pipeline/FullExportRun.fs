namespace Projection.Pipeline

// LINT-ALLOW-FILE: pipeline orchestration at the boundary — function-local mutables for the
//   full-export run state and `box`/`unbox` at the SqlParameter / JSON-payload
//   boundary (BCL APIs that take `obj`). No module-level mutable state; the
//   run output is immutable.

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
        // The "pipeline" umbrella stage. The granular extract / profile /
        // emit sub-stages are emitted inside `Compose.runWithConfig`
        // (Slice 2); this records the end-to-end envelope (which also
        // covers the store-leg path that doesn't route through
        // `runWithConfig`).
        LogSink.recordStageEvent stageName durationMs outcome

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
    /// The shared run core, parameterized by the composition runner. The runner
    /// drives the (genesis or store-leg) composition to completion and returns
    /// the `RunReport` plus the optional `FullExportStoreLeg` (always `None` for
    /// the genesis runner). Factored so the genesis `execute` and the
    /// Track-W1-B `executeWithStore` share one LogSink envelope contract — the
    /// §7/§10/§11 NDJSON surface has no second, drift-prone copy.
    let private executeCore
        (configPath: string)
        (outputOverride: string option)
        (verbosity: LogSink.Verbosity)
        (mutedCategories: Set<LogSink.Category>)
        (runComposition:
            Config.Config -> Result<Compose.RunReport * Compose.FullExportStoreLeg option>)
        : RunOutcome * Compose.FullExportStoreLeg option =
        LogSink.reset ()
        LogSink.setVerbosity verbosity
        LogSink.setMutedCategories mutedCategories
        Bench.reset ()
        let mutable outcome : LogSink.Outcome = LogSink.Succeeded
        let mutable storeLeg : Compose.FullExportStoreLeg option = None
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
                    // §7.4 transform.registered — the run's complete classified
                    // transform inventory (pillar-9 totality surface), emitted at
                    // start from the same registry that drives the run. Debug
                    // level: default-hidden, surfaces under --verbose / --debug.
                    EventProjection.ofRegistry RegisteredAllTransforms.all |> List.iter LogSink.emit
                    let sw = Stopwatch.StartNew()
                    let result = runComposition cfgForRun
                    sw.Stop()
                    match result with
                    | Ok (report, leg) ->
                        storeLeg <- leg
                        recordStage "pipeline" LogSink.Succeeded sw.ElapsedMilliseconds
                        emitSpecialCircumstancesDiagnostics report.Diagnostics
                        // §16 egress projection — surface the pass chain's
                        // accumulated writers as `transform.*` events. The
                        // trail projects to `transform.applied` / `.declined`
                        // (info) + `transform.lineage` (debug); the chain's
                        // full diagnostics project to `transform.diagnostic`
                        // (disjoint from the curated set emitted above).
                        EventProjection.ofLineageTrail report.Trail |> List.iter LogSink.emit
                        EventProjection.ofDiagnostics report.PassDiagnostics |> List.iter LogSink.emit
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
        |> fun runOutcome -> runOutcome, storeLeg

    let execute
        (configPath: string)
        (outputOverride: string option)
        (verbosity: LogSink.Verbosity)
        (mutedCategories: Set<LogSink.Category>)
        : RunOutcome =
        // The genesis composition: byte-identical to the pre-W1-B path (no store
        // read, no diff-vs-prior, no episode record). The store leg is `None`.
        executeCore configPath outputOverride verbosity mutedCategories
            (fun cfgForRun ->
                (Compose.runWithConfig cfgForRun).GetAwaiter().GetResult()
                |> Result.map (fun report -> report, None))
        |> fst

    /// Track W1-B (seam T2) — `execute` with the optional diff-vs-prior store
    /// leg. When `storePath` is `None` / empty the composition is byte-identical
    /// to `execute` (and the returned `FullExportStoreLeg option` is `None`);
    /// when a store is supplied, the genesis emission lands first, then the run
    /// loads the prior emission, measures the displacement, accumulates the
    /// refactorlog, builds the `ChangeManifest`, and records exactly one new
    /// episode. The boundary supplies `timeline` / `environment` / `at` (Core
    /// holds no clock). Returns the `RunOutcome` plus the store leg for the
    /// caller (the X3 publication bundle).
    let executeWithStore
        (configPath: string)
        (outputOverride: string option)
        (verbosity: LogSink.Verbosity)
        (mutedCategories: Set<LogSink.Category>)
        (storePath: string option)
        (timeline: Timeline)
        (environment: Environment)
        (at: System.DateTimeOffset)
        : RunOutcome * Compose.FullExportStoreLeg option =
        executeCore configPath outputOverride verbosity mutedCategories
            (fun cfgForRun ->
                (Compose.runWithConfigAndStore cfgForRun storePath timeline environment at)
                    .GetAwaiter().GetResult())
