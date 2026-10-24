namespace Projection.Pipeline

// LINT-ALLOW-FILE: pipeline orchestration at the boundary — function-local mutables for the
//   full-export run state and `box`/`unbox` at the SqlParameter / JSON-payload
//   boundary (BCL APIs that take `obj`). No module-level mutable state; the
//   run output is immutable.

open System
open System.IO
open System.Diagnostics
open Projection.Core
// NM-34b (live) — `CatalogCodec.serialize` is the deterministic canonical
// byte form hashed into the live-OSSYS input digest's model half.
open Projection.Targets.Json

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

    /// NM-34b (live) — the model half of the `Run.inputDigest`. The digest's
    /// first half is always the config-file text; this picks the second half
    /// (the model-content input) by the model source:
    ///   * PATH-sourced (`cfg.Model.Path = Some _`): the model FILE text — the
    ///     deterministic on-disk model input. Byte-identical to the pre-NM-34b-
    ///     live behavior (`readFileText` reads the same file the run read).
    ///   * LIVE-OSSYS (`cfg.Model.Path = None`): the CANONICAL serialization of
    ///     the READ source `Catalog` (`CatalogCodec.serialize`) — there is no
    ///     on-disk model file, so the read model's deterministic byte form is the
    ///     model-digest input. `None` (no report produced) ⇒ the honest empty
    ///     model half.
    /// `readFileText` is injected so the path branch stays testable without a
    /// real file on disk (the live branch is fully pure given the read catalog).
    let modelDigestInput
        (readFileText: string -> string)
        (modelPath: string option)
        (readCatalog: Catalog option)
        : string =
        match modelPath with
        | Some p  -> readFileText p
        | None    ->
            match readCatalog with
            | Some catalog -> CatalogCodec.serialize catalog
            | None         -> ""

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

    /// `config.connectionResolved` (SnapshotJson source + the resolved
    /// output dir; secrets absent by construction per D9) once the config
    /// has loaded (§7.1). The run's `config.runStart` is the
    /// `RunEnvelope.bracket`'s — emitted before the config loads, so it is
    /// the first event of EVERY run, including failed-config runs (card
    /// S4a; `outputDir` rides here now, since it is config-resolved).
    let private emitConfigSnapshot (cfg: Config.Config) (effectiveOutput: string) : unit =
        let modelSource =
            match cfg.Model.Ossys, cfg.Model.Path with
            | Some _, _ -> "LiveOssys"
            | None, Some p -> p
            | None, None -> "(none)"
        let connPayload : Map<string, objnull> =
            Map.ofList
                [ "kind",      box "SnapshotJson"
                  "modelPath", box modelSource
                  "outputDir", box effectiveOutput ]
        LogSink.emit
            { LogSink.envelope LogSink.Info LogSink.Config "config.connectionResolved" connPayload with
                Phase  = LogSink.Start
                Source = Some LogSink.Configuration }

    /// Run a full export under the structured LogSink stream. The run
    /// envelope (fresh runId + Bench, `config.runStart` first, the §7.4
    /// registry inventory, the mandatory terminal `summary.runComplete`) is
    /// `RunEnvelope.bracket`'s — the ONE owner (card S4a; the prior
    /// self-reset is retired). This core loads the config, emits the
    /// snapshot, delegates composition to the runner (whose stage spine —
    /// the "pipeline" umbrella + extract/profile/emit — rides
    /// `Compose.runWithConfig`'s `staged { }`), and projects diagnostics +
    /// artifacts. Synchronous (drives the `runWithConfig` task to
    /// completion) to keep the resumable-state-machine surface minimal.
    /// Console narration + bench dump are the caller's (CLI's) concern.
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
        let mutable storeLeg : Compose.FullExportStoreLeg option = None
        // NM-34b — the resolved config the body validated, captured so the
        // post-body `captureInputs` can compute the REAL `Run.inputDigest`
        // (config text + source-model content) and the touched `LedgerRef`.
        // `None` ⇒ the config never validated, so there is no stable input
        // content to hash (the honest empty digest).
        let mutable resolvedConfig : Config.Config option = None
        // NM-34b (live) — the source `Catalog` the run READ, captured from the
        // `RunReport`. `captureInputs` hashes its CANONICAL form
        // (`CatalogCodec.serialize`) as the model-digest input on the live-OSSYS
        // path (where `cfg.Model.Path = None`, so there is no on-disk model file
        // to hash). `None` ⇒ the composition never produced a report (config
        // invalid / aborted), so there is no read model to hash.
        let mutable readCatalog : Catalog option = None
        // NM-34b — the input digest + touched ledger refs, evaluated AFTER the
        // body so it reads what the run resolved. The digest is `Run.inputDigest
        // configText sourceModelContent`:
        //   * configText  — the raw unified-config file (a genuine run input);
        //   * sourceModelContent — the second input the digest contract names:
        //       - PATH-sourced model (`cfg.Model.Path = Some _`): the source
        //         model FILE content (byte-identical to the pre-NM-34b-live
        //         behavior — file text is the deterministic model input);
        //       - LIVE-OSSYS model (`cfg.Model.Path = None`): the CANONICAL
        //         serialization of the read source `Catalog`
        //         (`CatalogCodec.serialize report.ReadCatalog`) — there is no
        //         on-disk model file, so the read model's deterministic byte
        //         form is the model-digest input. This closes the NM-34b-live
        //         FLAG: a live run now yields a non-empty, model-SENSITIVE
        //         digest (changing the model changes it).
        // The ledger ref is the recorded episode's `EpisodeRef(timeline,
        // ordinal)` from the store leg, when a store was threaded.
        let captureInputs () : string * Run.LedgerRef list =
            let digest =
                match resolvedConfig with
                | None -> ""   // config never validated ⇒ no stable inputs to hash
                | Some cfg ->
                    let configText =
                        try File.ReadAllText configPath with _ -> ""
                    // NM-34b — path-sourced ⇒ file text (byte-identical);
                    // live-OSSYS ⇒ canonical serialization of the read catalog
                    // (model-SENSITIVE digest). See `modelDigestInput`.
                    let safeReadFile (p: string) : string =
                        try File.ReadAllText p with _ -> ""
                    let sourceModelContent =
                        modelDigestInput safeReadFile cfg.Model.Path readCatalog
                    if configText = "" then "" else Run.inputDigest configText sourceModelContent
            let ledgers =
                match storeLeg with
                | Some leg ->
                    let latest = EpisodicLifecycle.latest leg.Chain
                    let timeline = EpisodicLifecycle.timeline leg.Chain |> Timeline.name
                    let ordinal = Episode.version latest |> Version.ordinal
                    [ Run.EpisodeRef (timeline, ordinal) ]
                | None -> []
            digest, ledgers
        let runOutcome =
            RunEnvelope.bracket
                "projection full-export"
                (fun () ->
                    LogSink.setVerbosity verbosity
                    LogSink.setMutedCategories mutedCategories)
                (Map.ofList [ "configPath", box configPath ])
                captureInputs
                (fun () ->
                    try
                        match Config.fromFile configPath with
                        | Error errors ->
                            emitConfigErrors errors
                            RunOutcome.ConfigInvalid errors, LogSink.Failed
                        | Ok cfg ->
                            let effectiveOutput = resolveOutputDir cfg outputOverride
                            let cfgForRun = { cfg with Output = { Dir = effectiveOutput } }
                            // NM-34b — record the validated config so the
                            // post-body `captureInputs` can hash the real inputs.
                            resolvedConfig <- Some cfgForRun
                            emitConfigSnapshot cfgForRun effectiveOutput
                            match runComposition cfgForRun with
                            | Ok (report, leg) ->
                                storeLeg <- leg
                                // NM-34b (live) — capture the read source model so
                                // the post-body `captureInputs` can hash its
                                // canonical form into the live-path input digest.
                                readCatalog <- Some report.ReadCatalog
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
                                RunOutcome.Succeeded (report, effectiveOutput), LogSink.Succeeded
                            | Error errors ->
                                emitTransformErrors errors
                                RunOutcome.RunFailed errors, LogSink.Failed
                    with ex ->
                        let payload : Map<string, objnull> =
                            Map.ofList [
                                "exception", box (ex.GetType().Name)
                                "message",   box ex.Message
                            ]
                        LogSink.emit
                            { LogSink.envelope LogSink.Error LogSink.Config "config.validationFailed" payload with
                                Phase = LogSink.ErrorPhase }
                        RunOutcome.Aborted ex, LogSink.Failed)
        runOutcome, storeLeg

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
