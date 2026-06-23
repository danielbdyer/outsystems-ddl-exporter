module Projection.Cli.RunFaces

// STOPGAP (2026-06-18 — pre-existing, NOT part of the Spectre render chapter): `task{}`
// bodies in this file (a tightening-violation check; the streaming-load loop) trip
// FS3511 (an `await` inside a branch/loop) under Release's static state-machine
// optimization, which TreatWarningsAsErrors promotes to an error — breaking the Release
// build (and the perf-gate). The code is correct (dynamic state-machine fallback).
// Suppressed to unblock Release; the proper restructure is tracked as a separate task.
#nowarn "3511"

// LINT-ALLOW-FILE: CLI run-face operator-facing prose and terminal SQL-text at
//   the CLI boundary use string composition; the structural argument surface is
//   the typed MovementSpec / Intent (Projection.Pipeline).

// The proven `run*` faces — the CLI realization of each `PlanAction` ("the
// runner (`runPlan`) executes the action against the proven `run*` faces" —
// MovementSpec's contract). One face per engine action, each narrating to the
// operator console and mapping its outcome to the documented exit codes.
// Extracted from Program.fs (2026-06-10 decomposition); `Program.runPlan`
// stays the one dispatcher.

open System
open System.Diagnostics
open System.IO
open Projection.Core
open Projection.Adapters.Sql
open Projection.Pipeline
open Projection.Targets.SSDT
open Projection.Cli.OperatorConsole

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

let runEmit (shaping: Config.Config) (catalog: Catalog) (outputDir: string) : int =
    let exitCode =
        match Compose.runFromCatalogWith shaping catalog outputDir with
        | Ok paths ->
            printfn "%d artifact(s) written to %s." paths.Length outputDir
            paths
            |> List.iter (fun p ->
                let info = FileInfo p
                printfn "  %s (%d bytes)" p info.Length)
            0
        | Error errors ->
            (
                printErrors Console.Error errors
                2
            )
    dumpBench "emit"
    exitCode

/// Chapter A.4.7' slice ζ — `emit --skeleton-only`. Reads V1 JSON,
/// projects through `RegisteredTransforms.skeletonChainSteps` (the
/// four pure-DataIntent passes), writes the resulting bundle.
/// Operator-intent passes (Selection / Emission / Insertion /
/// Tightening / Ordering overlays) are excluded from the emit; the
/// resulting artifacts are the V2 baseline before any operator
/// opinion lands.
let runEmitSkeletonOnly (catalog: Catalog) (outputDir: string) : int =
    let exitCode =
        match Compose.runSkeletonOnlyFromCatalog catalog outputDir with
        | Ok paths ->
            printfn
                "%d skeleton-only artifact(s) written to %s."
                paths.Length
                outputDir
            paths
            |> List.iter (fun p ->
                let info = FileInfo p
                printfn "  %s (%d bytes)" p info.Length)
            0
        | Error errors ->
            (
                printErrors Console.Error errors
                2
            )
    dumpBench "emit-skeleton-only"
    exitCode

/// `shape: manifest` — the applied-transforms manifest alone. The full
/// (shaped) pass chain runs; only `manifest.json` lands in the out dir.
let runEmitManifestOnly (shaping: Config.Config) (catalog: Catalog) (outputDir: string) : int =
    let exitCode =
        match Compose.runManifestOnlyFromCatalogWith shaping catalog outputDir with
        | Ok paths ->
            printfn "%d manifest artifact(s) written to %s." paths.Length outputDir
            paths
            |> List.iter (fun p ->
                let info = FileInfo p
                printfn "  %s (%d bytes)" p info.Length)
            0
        | Error errors ->
            (
                printErrors Console.Error errors
                2
            )
    dumpBench "emit-manifest-only"
    exitCode

/// Card S3 — the deploy face's stop channel: what a closed-`failed` deploy
/// stage carries to the exit-code mapping (the `staged { }` Error lane).
type private DeployStop =
    | SsdtRejected of outputs: Compose.Outputs * report: Deploy.Report
    | DeployCatalogInvalid of errors: ValidationError list

let runDeploy (shaping: Config.Config) (catalog: Catalog) : int =
    if not (Deploy.Docker.isAvailable ()) then
        // §14 required-and-missing, voiced by code (`docker.unavailable`).
        TtyRenderer.renderVoicedTo Console.Error "docker.unavailable"
            (Map.ofList [ "purpose", box "deploy" ])
        4
    else
        let runBody () =
            TtyRenderer.renderVoicedTo Console.Out "container.starting"
                (Map.ofList [ "purpose", box "deploy" ])
            // Card S3 — the face rides the spine: the `staged { }` CE owns the
            // deploy stage's bracket (started/completed envelopes + the §10
            // stage table + `Bench.scope "stage.deploy"`); the face maps the
            // disposition to its narration + documented exit codes.
            let verdict =
                (staged Spines.deploy {
                    let! landed =
                        Staged.stage Stages.deploy (fun () ->
                            task {
                                let! result = Deploy.runFromCatalogWith shaping catalog
                                return
                                    match result with
                                    | Ok (outputs, report) when report.Ok -> Ok (outputs, report)
                                    | Ok (outputs, report) -> Error (SsdtRejected (outputs, report))
                                    | Error errors -> Error (DeployCatalogInvalid errors)
                            })
                    return landed
                }).GetAwaiter().GetResult()
            let emittedLine (outputs: Compose.Outputs) =
                // §13 resultative stage line, voiced by code.
                TtyRenderer.renderVoicedTo Console.Out "deploy.bundleEmitted"
                    (Map.ofList [ "entryCount", box (Map.count outputs.SsdtBundle) ])
            match verdict.Disposition with
            | RunCompleted (outputs, report) ->
                emittedLine outputs
                // §3 — the deploy verdict, voiced (`deploy.completed`); the
                // ephemeral database name is the substantiation.
                TtyRenderer.renderVoicedTo Console.Out "deploy.completed"
                    (Map.ofList
                        [ "database",   box report.Database
                          "tableCount", box report.TablesCreated ])
                0
            | RunStopped (SsdtRejected (outputs, report)) ->
                emittedLine outputs
                // §10 — the rejection verdict, voiced (`deploy.ssdtRejected`):
                // the statement is the register's finding; the server's own
                // error lines are demoted into the disclosure beneath, the
                // exact shape `canary.divergence` set. The newline join is
                // data marshalling into the envelope payload, not prose.
                TtyRenderer.renderVoicedTo Console.Error "deploy.ssdtRejected"
                    (Map.ofList
                        [ "database",     box report.Database
                          "serverErrors", box (String.concat "\n" report.Errors) ])
                3
            | RunStopped (DeployCatalogInvalid errors) ->
                printErrors Console.Error errors
                2
            | RunAborted (refusal, cause) ->
                // The spine already closed the books (the stage's bracket
                // closed `aborted` on the wire); the face preserves the
                // verb's pre-spine crash semantics.
                match cause with
                | Some ex -> raise ex
                | None    -> failwith refusal
        // --pretty + a real TTY → a live deploy stage (§13). The schema deploy is
        // one aggregated batch (no per-table count to honestly report), so the
        // board shows the stage going Applying → Deploy complete, not a bar.
        let exitCode =
            if Watch.shouldWatch prettyMode.Value then
                Watch.renderWatch Spines.deploy (Watch.resolveDwellMs ()) runBody
            else runBody ()
        dumpBench "deploy"
        exitCode

/// Card S3 — the canary face's stop channel: a structurally-divergent
/// round-trip (exit 5) or an invalid run (exit 2), carried out of the
/// closed-`failed` canary stage.
type private CanaryStop =
    | CanaryDiverged of report: Deploy.WideCanaryReport
    | CanaryRunInvalid of errors: ValidationError list

let runCanary (sourceDdlPath: string) : int =
    if not (File.Exists sourceDdlPath) then
        // §14 — concrete and located; the path is the substantiation.
        TtyRenderer.renderVoicedTo Console.Error "canary.sourceMissing"
            (Map.ofList [ "path", box sourceDdlPath ])
        1
    elif not (Deploy.Docker.isAvailable ()) then
        TtyRenderer.renderVoicedTo Console.Error "docker.unavailable"
            (Map.ofList [ "purpose", box "round-trip verification" ])
        4
    else
        let sourceDdl = File.ReadAllText sourceDdlPath
        TtyRenderer.renderVoicedTo Console.Out "container.starting"
            (Map.ofList [ "purpose", box "round-trip verification" ])
        // E4 — the production canary deploys the canonical schema-then-data
        // form (DDL + StaticPopulationEmitter's InsertRow realization into the
        // fresh-empty target). Schema-only when the source carries no static
        // populations. See `Deploy.schemaWithStaticPopulation`.
        //
        // Card S3 — the face rides the spine. This closes the slice-7
        // discrepancy: the prior bare `recordStage` fed the §10 stage table
        // but never the live stream; the CE's bracket emits the
        // `canary.started` / `summary.stageCompleted` pair AND the table
        // entry from one site (`recordStageEvent` semantics).
        let verdict =
            (staged Spines.canary {
                let! report =
                    Staged.stage Stages.canary (fun () ->
                        task {
                            let! result = Deploy.runWideCanary sourceDdl Deploy.schemaWithStaticPopulation
                            return
                                match result with
                                | Ok r when PhysicalSchema.isEqual r.Diff -> Ok r
                                | Ok r -> Error (CanaryDiverged r)
                                | Error errors -> Error (CanaryRunInvalid errors)
                        })
                return report
            }).GetAwaiter().GetResult()
        let deployedLine (report: Deploy.WideCanaryReport) =
            // §13 resultative — both sides of the round-trip are in place.
            TtyRenderer.renderVoicedTo Console.Out "canary.deployed"
                (Map.ofList
                    [ "sourceTables", box report.SourceReport.TablesCreated
                      "targetTables", box report.TargetReport.TablesCreated ])
            // Tier-1 reporting (§7.7) — emit the structured fidelity verdict
            // (canary.diffEmpty / canary.divergence) alongside the prose, so
            // CI can gate on it and the run ledger can record it.
            EventProjection.canaryEnvelopes report.TargetReport.TablesCreated report.Diff
            |> List.iter LogSink.emit
        let exitCode =
            match verdict.Disposition with
            | RunCompleted report ->
                deployedLine report
                TtyRenderer.renderVoicedTo Console.Out "canary.diffEmpty"
                    (Map.ofList [ "tableCount", box report.TargetReport.TablesCreated ])
                0
            | RunStopped (CanaryDiverged report) ->
                deployedLine report
                TtyRenderer.renderVoicedTo Console.Error "canary.divergence"
                    (Map.ofList [ "renderedDiff", box (PhysicalSchema.renderDiff report.Diff) ])
                5
            | RunStopped (CanaryRunInvalid errors) ->
                printErrors Console.Error errors
                2
            | RunAborted (refusal, cause) ->
                match cause with
                | Some ex -> raise ex
                | None    -> failwith refusal
        dumpBench "canary"
        exitCode

/// AC-X8 — `projection canary <source.sql> --cdc-silence`. The wide canary
/// PLUS the protein P-9 assertion: after the source≈target round-trip, enable
/// CDC on the deployed target and measure an *idempotent redeploy*
/// (`MigrationRun.execute tgt tgt`, an empty differential) with the production
/// reader. Green iff the PhysicalSchema diff is empty AND the redeploy fired
/// ZERO CDC captures — the `V2_DRIVER.md` highest-leverage property surfaced by
/// the canary itself, not a harness.
let runCanaryCdcSilence (sourceDdlPath: string) : int =
    if not (File.Exists sourceDdlPath) then
        TtyRenderer.renderVoicedTo Console.Error "canary.sourceMissing"
            (Map.ofList [ "path", box sourceDdlPath ])
        1
    elif not (Deploy.Docker.isAvailable ()) then
        TtyRenderer.renderVoicedTo Console.Error "docker.unavailable"
            (Map.ofList [ "purpose", box "CDC-silence verification" ])
        4
    else
        let sourceDdl = File.ReadAllText sourceDdlPath
        let enableCdc cnn (cat: Catalog) =
            task {
                do! Deploy.executeBatch cnn "EXEC sys.sp_cdc_enable_db;"
                for k in Catalog.allKinds cat do
                    do! Deploy.executeBatch cnn
                            (String.Concat(   // LINT-ALLOW: terminal SQL-text boundary; sp_cdc_enable_table takes schema/table as N'...' literals
                                "EXEC sys.sp_cdc_enable_table @source_schema=N'", TableId.schemaText k.Physical,
                                "', @source_name=N'", TableId.tableText k.Physical,
                                "', @role_name=NULL, @supports_net_changes=0;"))
            }
        let redeploy cnn (cat: Catalog) =
            task {
                // The idempotent redeploy: migrate the deployed schema against
                // itself — an empty differential, so zero DDL and zero DML.
                let! _ = MigrationRun.execute true DeclareNone cat cat cnn
                return ()
            }
        TtyRenderer.renderVoicedTo Console.Out "container.starting"
            (Map.ofList [ "purpose", box "CDC-silence verification" ])
        let work =
            Deploy.runWideCanaryWithCdcSilence
                (fun cnn -> Deploy.executeBatch cnn sourceDdl)
                SsdtDdlEmitter.statements
                enableCdc
                redeploy
        let result = work.GetAwaiter().GetResult()
        let exitCode =
            match result with
            | Ok (report, cdcDelta) ->
                // §6 — the CDC measure rides channel 1 (structured) so the verdict
                // panel + ledger read the data norm; the console gets the Voice
                // surface below. Sibling of the canary diff projection.
                LogSink.emit (EventProjection.cdcMeasureEnvelope cdcDelta)
                TtyRenderer.renderVoicedTo Console.Out "canary.deployed"
                    (Map.ofList
                        [ "sourceTables", box report.SourceReport.TablesCreated
                          "targetTables", box report.TargetReport.TablesCreated ])
                let schemaOk = PhysicalSchema.isEqual report.Diff
                if not schemaOk then
                    TtyRenderer.renderVoicedTo Console.Error "canary.divergence"
                        (Map.ofList [ "renderedDiff", box (PhysicalSchema.renderDiff report.Diff) ])
                if cdcDelta <> 0 then
                    // §6 — the silence proof failed; the finding carries its measure.
                    TtyRenderer.renderVoicedTo Console.Error "canary.cdcCaptured"
                        (Map.ofList [ "capturedRows", box cdcDelta ])
                if schemaOk && cdcDelta = 0 then
                    // §6 CDC-silence proof — the deepest fidelity finding, said plain.
                    TtyRenderer.renderVoicedTo Console.Out "canary.cdcSilent" Map.empty
                    0
                else 5
            | Error errors ->
                printErrors Console.Error errors
                2
        dumpBench "canary"
        exitCode

/// H-086 / Wave-3 slice 3.2: `projection approve <policyVersion> --approver
/// <name> [--rationale <text>] [--store <path>]`. Creates an `ApprovalRecord`
/// for the given policy version and prints it. When `--store <path>` is given,
/// the record is *persisted* (load → `ApprovalRegistry.record` → save) so R6
/// operator sign-off is recorded and consultable across runs — closing the
/// prior "constructs and discards" gap. Without `--store`, prints only
/// (backward-compatible). The policy version string is the hex SHA-256 digest
/// from `VersionedPolicy.digestOf` (or any opaque version the operator tracks).
let runApprove
    (policyVersion: string)
    (approver: string)
    (rationale: string option)
    (store: string option)
    : int =
    // Slice 0 (2026-06-02): Core retired `*Now` wrappers; CLI is the
    // boundary that supplies `DateTimeOffset.UtcNow` per the Episode.fs
    // canonical "boundary-supplied at" pattern. One reading of UtcNow
    // shared across both transitions for a consistent record.
    let now = System.DateTimeOffset.UtcNow
    let record =
        ApprovalWorkflow.pending now policyVersion
        |> ApprovalWorkflow.approve approver rationale now
    printfn "Policy version %s approved." policyVersion
    printfn "  approver  : %s" approver
    printfn "  decision  : Approved"
    printfn "  at        : %s" (record.At.ToString "o")
    match rationale with
    | Some r -> printfn "  rationale : %s" r
    | None   -> ()
    let persisted =
        match store with
        | None -> Ok ()
        | Some path ->
            match ApprovalStore.load path with
            | Error e -> Error (ApprovalStore.describe e)
            | Ok registry ->
                match ApprovalStore.save path (ApprovalRegistry.record record registry) with
                | Ok () -> printfn "  store     : %s (recorded)" path; Ok ()
                | Error e -> Error (ApprovalStore.describe e)
    match persisted with
    | Ok () -> dumpBench "approve"; 0
    | Error msg ->
        Console.Error.WriteLine(sprintf "projection approve: store failed: %s" msg)
        6

// ----------------------------------------------------------------------
// `transfer` (Phase 11 Slice D) — bidirectional data-load CLI verb.
// Default DryRun (no Sink writes); `--execute` is gated behind
// PROJECTION_ALLOW_EXECUTE=1 (R6). Reconciliation per `--reconcile
// <table>:<match-column>` (MatchByColumn) — rows whose FK targets an
// unmatched identity are skip-and-diagnosed (the C′.2a default; the
// operator's headline Dev→UAT User re-key shape).
// ----------------------------------------------------------------------

let dispositionName (d: IdentityDisposition) : string =
    match d with
    | IdentityDisposition.ReconciledByRule    -> "re-keyed by rule"
    | IdentityDisposition.AssignedBySink      -> "assigned by the target"
    | IdentityDisposition.PreservedFromSource -> "preserved from source"

// --- name resolution for the run/apply narration surfaces -------------------
// The reconciliation / integrity / load-plan reports are keyed by `SsKey`, and
// `SsKey.rootOriginal` is a bare GUID for an `OssysOriginal` key — so on a real
// OSSYS estate these surfaces were a wall of hex. The diff surface already names
// by `Name`; these faces share the SAME `Catalog.nameIndex` primitive (Core).
// `rootOriginal` is the honest fallback for a key absent from the index.

let private nameOf (names: Map<SsKey, string>) (key: SsKey) : string =
    Map.tryFind key names |> Option.defaultValue (SsKey.rootOriginal key)

/// The load-plan / cycle-FK / unmatched-identity narration, named by the transfer
/// report's own `Names` index (populated from the engine's contract catalog;
/// empty ⇒ `rootOriginal` fallback, byte-identical to pre-displayName behaviour).
let narrateTransferReport (report: Transfer.TransferReport) : unit =
    let nm = nameOf report.Names
    // §4 Move — lead with the finding (rows moved, in dependency order); the
    // load plan rides beneath. Dependency order is the engine's guarantee that
    // a row never lands before the rows it points to.
    let totalWritten = report.Kinds |> List.sumBy (fun k -> k.RowsWritten)
    (match report.Mode with
     | Transfer.DryRun  ->
         printfn "Preview — %d row(s) would move across %d table(s), in dependency order. No rows written." totalWritten report.Kinds.Length
     | Transfer.Execute ->
         printfn "%d row(s) moved across %d table(s), in dependency order." totalWritten report.Kinds.Length)
    printfn ""
    printfn "The load plan (%d table(s)):" report.Kinds.Length
    for k in report.Kinds do
        printfn "  %-40s %-22s ingested=%d written=%d deferred-fk-columns=%d"
            (nm k.Kind)
            (dispositionName k.Disposition)
            k.RowsIngested
            k.RowsWritten
            (Set.count k.DeferredFkColumns)
    if not (List.isEmpty report.UnbreakableCycleFks) then
        printfn ""
        printfn
            "%d relationship cycle(s) cannot be broken — the load cannot run as planned:"
            report.UnbreakableCycleFks.Length
        for u in report.UnbreakableCycleFks do
            printfn
                "  %s.%s → %s"
                (nm u.Kind)
                (Name.value u.Column)
                (nm u.Target)
    if not (List.isEmpty report.UnmatchedIdentities) then
        printfn ""
        printfn
            "%d identity(ies) unmatched — source records with no match in the target:"
            report.UnmatchedIdentities.Length
        for (k, s) in report.UnmatchedIdentities do
            printfn "  %s source '%s'" (nm k) (SourceKey.value s)
    if not (List.isEmpty report.AmbiguousIdentities) then
        printfn ""
        printfn
            "%d source record(s) had a non-unique reconcile key — the first binding was kept:"
            report.AmbiguousIdentities.Length
        for (k, s) in report.AmbiguousIdentities do
            printfn "  %s source '%s'" (nm k) (SourceKey.value s)
    if not (List.isEmpty report.AmbiguousTargetMatchKeys) then
        printfn ""
        printfn
            "%d target record(s) shared a reconcile key with an older record — the oldest was kept (supply an override if the wrong one won):"
            report.AmbiguousTargetMatchKeys.Length
        for (k, a) in report.AmbiguousTargetMatchKeys do
            printfn "  %s target '%s' (displaced)" (nm k) (AssignedKey.value a)
    if not (List.isEmpty report.SkippedReferences) then
        printfn ""
        printfn
            "%d row(s) dropped — a relationship points to an unmatched record:"
            report.SkippedReferences.Length
        for (owner, r) in report.SkippedReferences do
            printfn
                "  %s.%s → %s (unmatched source '%s')"
                (nm owner)
                (Name.value r.Column)
                (nm r.Target)
                (SourceKey.value r.UnresolvedSource)
    // NM-53 — a resumable G10 no-op re-run replays the prior run's drop count
    // (the marker persists the count, not the exact references), so the re-run
    // is not silently clean. Surfaced explicitly as a replay, not freshly
    // observed drops.
    match report.ReplayedPriorDrops with
    | Some n when n > 0 ->
        printfn ""
        printfn
            "already complete; prior run dropped %d row(s) — re-surfacing that verdict (exact references not replayed)."
            n
    | _ -> ()

/// 6.A.1 — the drop-set is fail-loud, not exit-0. A successful write that
/// dropped FK-orphan rows or left reconciled-kind sources unmatched surfaces
/// a distinct non-zero exit unless the operator declared the drops
/// acceptable via --allow-drops; the dropped/unmatched kinds are narrated.
let private narrateDropExit (allowDrops: bool) (report: Transfer.TransferReport) : int =
    let dropCode = Transfer.exitCodeForReport allowDrops report
    if dropCode <> 0 then
        Console.Error.WriteLine
            (sprintf
                "%d row(s) would be dropped — a relationship points to an unmatched record. Pass --allow-drops to accept the loss, or resolve the records."
                (Transfer.droppedRowCount report))
        let nm = nameOf report.Names
        let kindCount (label: string) (keys: SsKey seq) =
            keys
            |> Seq.countBy nm
            |> Seq.iter (fun (k, n) ->
                Console.Error.WriteLine (sprintf "  %s %s: %d" label k n))
        kindCount "dropped in" (report.SkippedReferences |> List.map fst)
        kindCount "unmatched in" (report.UnmatchedIdentities |> List.map fst)
    dropCode

/// Parse an optional `--source-env` / `--sink-env` label into the
/// apparatus's `Environment`. The four named environments resolve
/// case-insensitively; anything else is a `Named` escape hatch; absence
/// keeps the default role-named label.
let runTransfer
    (sourceSpec: string)
    (sinkSpec: string)
    (sourceEnv: string option)
    (sinkEnv: string option)
    (reconcileSpecs: string list)
    (userMapPath: string option)
    (executeRequested: bool)
    (allowCdc: bool)
    (allowDrops: bool)
    (emission: EmissionMode)
    (resumable: bool)
    (tables: string list)
    (revertPolicy: RevertPolicy)
    (revertDir: string option)
    (surveyAdvisory: string list)
    : int =
    let collect = function Ok _ -> [] | Error es -> es
    let parsedSource    = TransferSpec.parseConnectionSpec sourceSpec
    let parsedSink      = TransferSpec.parseConnectionSpec sinkSpec
    let parsedReconciles = reconcileSpecs |> List.map TransferSpec.parseReconcileSpec
    // Slice 4.2 — read + parse the optional --user-map CSV (boundary I/O).
    let parsedUserMap : Result<TransferSpec.UserMapEntry list> =
        match userMapPath with
        | None -> Result.success []
        | Some path ->
            if not (System.IO.File.Exists path) then
                Result.failureOf
                    (ValidationError.create "transfer.userMap.fileMissing"
                        (sprintf "user-map file '%s' not found." path))
            else TransferSpec.parseUserMapCsv (System.IO.File.ReadAllText path)
    let specErrors =
        collect parsedSource
        @ collect parsedSink
        @ (parsedReconciles |> List.collect collect)
        @ collect parsedUserMap
    if not (List.isEmpty specErrors) then
        Console.Error.WriteLine "projection transfer: argument error:"
        printErrors Console.Error specErrors
        dumpBench "transfer"
        2
    else

    let executeGated =
        if executeRequested then
            System.Environment.GetEnvironmentVariable "PROJECTION_ALLOW_EXECUTE" = "1"
        else false
    if executeRequested && not executeGated then
        TtyRenderer.renderVoicedError (ValidationError.create "gate.intent" "PROJECTION_ALLOW_EXECUTE is not set in the environment.")
        dumpBench "transfer"
        7
    else

    let sourceRef    = Result.value parsedSource
    let sinkRef      = Result.value parsedSink
    let entries      = parsedReconciles |> List.map Result.value
    let userMapEntries = Result.value parsedUserMap
    let reconcile    = not (List.isEmpty entries) || not (List.isEmpty userMapEntries)

    // Bind the apparatus and DRIVE the run through it (D9: openSubstrate
    // resolves the OOB credentials; the apparatus validates roles + records
    // the ProfiledForIdentity set — Source always; Sink too when reconciling).
    let sourceSub : Substrate =
        { Environment   = parseEnvironment "Source" sourceEnv
          Role          = SubstrateRole.Source
          ConnectionRef = sourceRef }
    let sinkSub : Substrate =
        { Environment   = parseEnvironment "Sink" sinkEnv
          Role          = SubstrateRole.Sink
          ConnectionRef = sinkRef }
    match TransferConnections.create sourceSub sinkSub reconcile with
    | Error es ->
        Console.Error.WriteLine "projection transfer: apparatus invariant violation:"
        printErrors Console.Error es
        dumpBench "transfer"
        3
    | Ok connections ->

    let mode = if executeGated then Transfer.Execute else Transfer.DryRun
    let resolveReconciliation (contract: Catalog) =
        TransferSpec.resolveAllReconciliation contract entries userMapEntries
    // M23 — collapse the revert policy to the engine's (autoRevert, dir) levers;
    // a Script/Auto policy with no explicit --revert-dir defaults to the cwd.
    let revertAuto, revertOut = RevertPolicy.toEngine (revertDir |> Option.orElse (Some ".")) revertPolicy
    let runBody () =
        let result =
            (Transfer.runThroughConnectionsResumable mode emission resumable allowCdc allowDrops tables connections resolveReconciliation revertAuto revertOut)
                .GetAwaiter().GetResult()
        match result with
        | Ok report ->
            narrateTransferReport report
            narrateDropExit allowDrops report
        | Error errors ->
            printErrors Console.Error errors
            // A1 — single-source the refusal exit through `Preflight.refusalOf`
            // (the canonical `classify` seam over the primary error code) rather
            // than a hand-derived if/elif. The prior chain lacked an arm for
            // `transfer.cdcTrackedSink` and silently dropped it to the generic
            // `else 3`; `classify` maps it to 9 (`CdcTrackedSink`). Connection(6) /
            // grant(7) / reconcile+userMap(2) / unmappedIdentities(9) classify
            // byte-identically; a genuinely unclassified code stays at the named
            // `(3, UnclassifiedRefusal)` default. The AC-I5 pre-write halt
            // (`transfer.unmappedIdentities → 9`) and the post-write drop
            // (`Transfer.DroppedReferencesExit`) still coincide at 9 via classify.
            (Preflight.refusalOf errors).ExitCode
    // G0c — the advisory capability survey (R6 warn-not-stop). Before a live
    // Execute, surface any blocked-capability / unreachable findings the survey
    // raised over the touched environments as a STDERR ADVISORY WARNING, then
    // PROCEED regardless. V2 owns no production write path during dual-track
    // (CLAUDE.md R6; DECISIONS 2026-06-09 S3), so the gate is advisory until the
    // per-pair flip — the run's own exit stands; the advisory never borrows the
    // standalone `survey` verb's exit-7. Computed at the dispatch layer (where
    // config is in scope) and threaded in as `surveyAdvisory`; a dry-run carries
    // no advisory (the empty list), so the preview path is byte-identical.
    if executeGated then for line in surveyAdvisory do Console.Error.WriteLine line
    // --pretty + a real TTY → the live data-load board (§13); the transfer leg
    // streams the "load" stage with per-table progress. Only on a real --execute
    // (a dry-run writes no rows, so the load stage would never advance).
    let exitCode =
        if executeGated && Watch.shouldWatch prettyMode.Value then
            Watch.renderWatch Spines.transfer (Watch.resolveDwellMs ()) runBody
        else runBody ()
    dumpBench "transfer"
    exitCode

// ---------------------------------------------------------------------------
// J3 closed — the `legacy` B→A reverse-leg face (THE_DATA_PRODUCERS §6 LE-1).
// The two SsKey-aligned contracts arrive RENDERED from the one authored model
// (`CatalogRendition`, produced at the dispatch arm); the face owns the
// operator gates — the execute env-gate, and a NAMED refusal for
// reconcile/rekey (the reconcile + rename combination is the documented
// follow-on; refusing is the honest boundary, never a silent straight-load) —
// and drives `Transfer.runReverseLegThroughConnections` through the apparatus.
// ---------------------------------------------------------------------------

let runReverseLegTransfer
    (sourceSpec: string)
    (sinkSpec: string)
    (logicalSourceContract: Catalog)
    (physicalSinkContract: Catalog)
    (reconcileSpecs: string list)
    (userMapPath: string option)
    (executeRequested: bool)
    (allowCdc: bool)
    (allowDrops: bool)
    (emission: EmissionMode)
    (resumable: bool)
    (streaming: bool)
    (journalDirectory: string option)
    (tables: string list)
    (revertPolicy: RevertPolicy)
    (revertDir: string option)
    (sinkCapability: SinkLoadCapability)
    (surveyAdvisory: string list)
    : int =
    let collect = function Ok _ -> [] | Error es -> es
    let parsedSource = TransferSpec.parseConnectionSpec sourceSpec
    let parsedSink   = TransferSpec.parseConnectionSpec sinkSpec
    // Phase 2 (the charter): reconcile on the reverse leg is no longer
    // refused — the User family re-keys by business key on the up-leg. The
    // specs parse exactly as the forward face's; the named refusal that stood
    // here is lifted (DECISIONS 2026-06-15 — reconcile ∘ reverse leg).
    let parsedReconciles = reconcileSpecs |> List.map TransferSpec.parseReconcileSpec
    let parsedUserMap : Result<TransferSpec.UserMapEntry list> =
        match userMapPath with
        | None -> Result.success []
        | Some path ->
            if not (System.IO.File.Exists path) then
                Result.failureOf
                    (ValidationError.create "transfer.userMap.fileMissing"
                        (sprintf "user-map file '%s' not found." path))
            else TransferSpec.parseUserMapCsv (System.IO.File.ReadAllText path)
    let specErrors =
        collect parsedSource @ collect parsedSink
        @ (parsedReconciles |> List.collect collect)
        @ collect parsedUserMap
    if not (List.isEmpty specErrors) then
        Console.Error.WriteLine "projection move (reverse leg): argument error:"
        printErrors Console.Error specErrors
        dumpBench "transfer"
        2
    else

    // Resolve the reconcile / user-map specs against the PHYSICAL sink
    // contract (the rendition the reverse leg writes into; `findKindByTable`
    // matches physical names, consistent with the forward face's live-read
    // contract). A bad spec refuses by name before any connection opens.
    let entries        = parsedReconciles |> List.map Result.value
    let userMapEntries = Result.value parsedUserMap
    let reconcile      = not (List.isEmpty entries) || not (List.isEmpty userMapEntries)
    match TransferSpec.resolveAllReconciliation physicalSinkContract entries userMapEntries with
    | Error es ->
        Console.Error.WriteLine "projection move (reverse leg): reconcile resolution error:"
        printErrors Console.Error es
        dumpBench "transfer"
        2
    | Ok reconciliation ->

    // The realization SELECTOR (DECISIONS 2026-06-11): the engine chooses
    // the best realization the request admits — streaming whenever
    // admissible (it dominates on every measured axis), the materialized
    // path for the combinations streaming does not yet support. An
    // explicit --streaming on an inadmissible combination refuses BY
    // NAME, never a silent downgrade.
    match ReverseLegRealization.choose emission resumable tables streaming journalDirectory sinkCapability.SinkResidentResume with
    | Error errors ->
        errors |> List.iter TtyRenderer.renderVoicedError
        dumpBench "transfer"
        2
    | Ok realization ->

    let executeGated =
        if executeRequested then
            System.Environment.GetEnvironmentVariable "PROJECTION_ALLOW_EXECUTE" = "1"
        else false
    if executeRequested && not executeGated then
        TtyRenderer.renderVoicedError (ValidationError.create "gate.intent" "PROJECTION_ALLOW_EXECUTE is not set in the environment.")
        dumpBench "transfer"
        7
    else

    // Phase 3 — the duplicate-hazard close (the charter's "small lever"): a
    // journal-less streaming EXECUTE has no idempotent envelope, so refuse by
    // name (the pure `executeJournalGate`) and force `--journal <dir>`.
    match ReverseLegRealization.executeJournalGate realization executeGated with
    | Some refusal ->
        TtyRenderer.renderVoicedError refusal
        dumpBench "transfer"
        2
    | None ->

    let sourceSub : Substrate =
        { Environment   = parseEnvironment "Source" None
          Role          = SubstrateRole.Source
          ConnectionRef = Result.value parsedSource }
    let sinkSub : Substrate =
        { Environment   = parseEnvironment "Sink" None
          Role          = SubstrateRole.Sink
          ConnectionRef = Result.value parsedSink }
    match TransferConnections.create sourceSub sinkSub reconcile with
    | Error es ->
        Console.Error.WriteLine "projection move (reverse leg): apparatus invariant violation:"
        printErrors Console.Error es
        dumpBench "transfer"
        3
    | Ok connections ->

    let mode = if executeGated then Transfer.Execute else Transfer.DryRun
    // M23 — collapse the revert policy to the engine's (autoRevert, dir) levers.
    let revertAuto, revertOut = RevertPolicy.toEngine (revertDir |> Option.orElse (Some ".")) revertPolicy
    let runBody () =
        let result =
            match realization with
            // Phase 2 (NM-31 closed on the streaming arm): both arms now thread
            // `allowDrops` + `reconciliation` into the engine. A reconciling run
            // takes the `validateUserMap` PRE-write orphan halt (AC-I5) on either
            // arm; `allowDrops` downgrades it to the POST-write reported-drop path
            // (`narrateDropExit`). A non-reconciling run carries an empty
            // reconciliation, so the halt never fires (byte-identical straight load).
            | ReverseLegRealization.Streaming journal ->
                // D — the streaming arm now carries the same per-environment revert
                // policy the materialized branch consumes (`revertAuto`/`revertOut`
                // derived above via `RevertPolicy.toEngine`): a mid-stream crash
                // reverts (auto) or scripts (script) the partial sink-minted rows.
                (Transfer.runStreamingReverseLegThroughConnections mode allowCdc allowDrops journal connections logicalSourceContract physicalSinkContract reconciliation revertAuto revertOut)
                    .GetAwaiter().GetResult()
            | ReverseLegRealization.Materialized ->
                (Transfer.runReverseLegThroughConnectionsWith sinkCapability.IdentityPolicy mode emission resumable allowCdc allowDrops tables connections logicalSourceContract physicalSinkContract reconciliation revertAuto revertOut)
                    .GetAwaiter().GetResult()
        match result with
        | Ok report ->
            narrateTransferReport report
            narrateDropExit allowDrops report
        | Error errors ->
            printErrors Console.Error errors
            (Preflight.refusalOf errors).ExitCode
    // G0c — the advisory capability survey (R6 warn-not-stop); same channel
    // and same advisory-only posture as the peer transfer's Execute path.
    if executeGated then for line in surveyAdvisory do Console.Error.WriteLine line
    let exitCode =
        if executeGated && Watch.shouldWatch prettyMode.Value then
            Watch.renderWatch Spines.transfer (Watch.resolveDwellMs ()) runBody
        else runBody ()
    dumpBench "transfer"
    exitCode

// ---------------------------------------------------------------------------
// Slice 4.4 — `projection verify-data`: post-deploy data-integrity gate.
// Compares two deployments of the same schema contract on exact per-table
// row counts + per-column null counts (the data-fidelity complement to the
// canary's structural equivalence). Read-only — no execute gate. The schema
// contract is read from the before deployment via `ReadSide.read`.
// ---------------------------------------------------------------------------

/// The §6 data-fidelity verdict pair, voiced (`verifyData.matched` /
/// `verifyData.diverged`): the statement leads; the per-table deltas are
/// demoted into counted disclosures beneath. The newline joins are data
/// marshalling into the envelope payload, not prose — the catalog owns the
/// framing and the disclosure headlines.
/// The §6 divergence payload, tables/columns named by `Name` (the contract IS the
/// before deployment's catalog), not the GUID `rootOriginal` they used to show.
/// Pure (no I/O) so the legibility is unit-testable without the Voice / Console.
let integrityPayload (contract: Catalog) (report: IntegrityReport) : Voice.Payload =
    let names = Catalog.nameIndex contract
    let joined (lines: string list) = lines |> String.concat "\n"
    [ if not (List.isEmpty report.RowCountDeltas) then
        "rowDeltas",
        box (report.RowCountDeltas
             |> List.map (fun d ->
                 sprintf "%-40s before=%d after=%d (change=%+d)"
                     (nameOf names d.Kind) (int64 d.Before) (int64 d.After) (int64 (d.After - d.Before)))
             |> joined)
      if not (List.isEmpty report.NullCountDeltas) then
        "nullDeltas",
        box (report.NullCountDeltas
             |> List.map (fun d ->
                 sprintf "%-40s %-30s before=%d after=%d (change=%+d)"
                     (nameOf names d.Kind) (nameOf names d.Attribute)
                     (int64 d.Before) (int64 d.After) (int64 (d.After - d.Before)))
             |> joined)
      if not (List.isEmpty report.Warnings) then
        "schemaWarnings",
        box (report.Warnings
             |> List.map (fun w -> sprintf "%s (%s)" w.Message w.Code)
             |> joined) ]
    |> Map.ofList

let narrateIntegrityReport (contract: Catalog) (report: IntegrityReport) : unit =
    if DataIntegrityChecker.isClean report then
        TtyRenderer.renderVoicedTo Console.Out "verifyData.matched" Map.empty
    else
        TtyRenderer.renderVoicedTo Console.Out "verifyData.diverged" (integrityPayload contract report)

let runVerifyData (beforeSpec: string) (afterSpec: string) : int =
    let collect = function Ok _ -> [] | Error es -> es
    let parsedBefore = TransferSpec.parseConnectionSpec beforeSpec
    let parsedAfter  = TransferSpec.parseConnectionSpec afterSpec
    let specErrors = collect parsedBefore @ collect parsedAfter
    if not (List.isEmpty specErrors) then
        // The voiced §10/§14 error surface is the whole error face — no
        // command-prefixed header; exits unchanged.
        printErrors Console.Error specErrors
        dumpBench "verify-data"
        2
    else

    let beforeRef = Result.value parsedBefore
    let afterRef  = Result.value parsedAfter
    let beforeStrR = ConnectionResolver.resolve "Before" beforeRef
    let afterStrR  = ConnectionResolver.resolve "After"  afterRef
    let connErrors = collect beforeStrR @ collect afterStrR
    if not (List.isEmpty connErrors) then
        printErrors Console.Error connErrors
        dumpBench "verify-data"
        6
    else

    let work =
        task {
            use before = new Microsoft.Data.SqlClient.SqlConnection(Result.value beforeStrR)
            use after  = new Microsoft.Data.SqlClient.SqlConnection(Result.value afterStrR)
            do! before.OpenAsync()
            do! after.OpenAsync()
            // The schema contract is the before deployment's reconstructed
            // catalog; both sides are profiled against it.
            let! contractR = ReadSide.read before
            match contractR with
            | Error es -> return Result.failure es
            | Ok contract ->
                // Carry the contract OUT alongside the report so the narration can
                // name tables/columns (the report itself is SsKey-keyed only).
                let! cmp = DataIntegrityChecker.compare before after contract
                match cmp with
                | Ok report -> return Ok (report, contract)
                | Error es  -> return Result.failure es
        }
    let exitCode =
        match work.GetAwaiter().GetResult() with
        | Ok (report, contract) ->
            narrateIntegrityReport contract report
            // The gate fails closed: any divergence is a non-zero exit so a
            // CI step / cutover gate trips on data drift.
            if DataIntegrityChecker.isClean report then 0 else 8
        | Error errors ->
            printErrors Console.Error errors
            3
    dumpBench "verify-data"
    exitCode

/// AC-X7 — `projection drift --to <model.json> --conn <ref>`. Reads the
/// deployed schema and diffs it against THE MODEL (the authored Catalog), not
/// a second deployed substrate. Exit 0 = no drift; 5 = drift detected (the diff
/// is rendered). This is the deployed-vs-model check `verify-data`
/// (deployed-vs-deployed) structurally cannot perform.
let runDrift (toPath: string) (connSpec: string) : int =
    // The error face is the voiced §10/§14 surface alone — no command-prefixed
    // headers: the catalog's frame for the primary code is the statement
    // (malformed reference / unresolved secret / model failed to load /
    // deployed schema unreadable), the located causes ride beneath.
    let collect = function Ok _ -> [] | Error es -> es
    let parsedConn = TransferSpec.parseConnectionSpec connSpec
    if not (List.isEmpty (collect parsedConn)) then
        printErrors Console.Error (collect parsedConn)
        dumpBench "drift"
        2
    else
    let connStrR = ConnectionResolver.resolve "Deployed" (Result.value parsedConn)
    if not (List.isEmpty (collect connStrR)) then
        printErrors Console.Error (collect connStrR)
        dumpBench "drift"
        6
    else
    let work =
        task {
            let! modelR = Compose.read toPath
            match modelR with
            | Error es ->
                printErrors Console.Error es
                return 6
            | Ok model ->
                use cnn = new Microsoft.Data.SqlClient.SqlConnection(Result.value connStrR)
                do! cnn.OpenAsync()
                match! DriftRun.detect model cnn with
                | Error es ->
                    printErrors Console.Error es
                    return 3
                | Ok diff ->
                    if PhysicalSchema.isEqual diff then
                        // §6 — the no-drift verdict, voiced (`drift.none`).
                        TtyRenderer.renderVoicedTo Console.Out "drift.none" Map.empty
                        return 0
                    else
                        // §5 drift gate — the finding leads; the rendered
                        // difference is demoted into the disclosure beneath;
                        // the surface ends on the operator's levers.
                        TtyRenderer.renderVoicedTo Console.Error "drift.diverged"
                            (Map.ofList [ "renderedDiff", box (PhysicalSchema.renderDiff diff) ])
                        return 5
        }
    let code = work.GetAwaiter().GetResult()
    dumpBench "drift"
    code

/// AC-X6 — `projection eject --store <path>`. Reads the durable timeline and
/// assembles the append-forever provenance package: every episode is preserved
/// (no collapse at freeze), the full refactorlog reference chain is accumulated,
/// and the package self-verifies (the FTC reconstruction from genesis reproduces
/// the frozen state). Exit 0 = ejected + self-verified; 5 = reconstruction does
/// not reproduce the frozen state; 2 = store error.
let runEject (storePath: string) : int =
    match EjectRun.fromStore storePath with
    | Error msg ->
        // §14 — the finding leads; the raw store error is the located cause.
        TtyRenderer.renderVoicedTo Console.Error "eject.storeUnreadable"
            (Map.ofList [ "cause", box msg ])
        2
    | Ok pkg ->
        // §13 resultative — the package line, voiced; the timeline beneath.
        TtyRenderer.renderVoicedTo Console.Out "eject.packaged"
            (Map.ofList
                [ "timeline",         box (Timeline.name pkg.Timeline)
                  "episodeCount",     box (List.length pkg.Episodes)
                  "refactorLogCount", box (List.length pkg.RefactorLogRefs) ])
        if EjectRun.isFaithful pkg then
            // §6 — the freeze's self-verification, asserted.
            TtyRenderer.renderVoicedTo Console.Out "eject.verified" Map.empty
            0
        else
            TtyRenderer.renderVoicedTo Console.Error "eject.unverified" Map.empty
            5

/// Tier-4 reporting — `readiness`. Read the cross-run ledger
/// (`PROJECTION_LEDGER_DIR`) and report the R6 cutover gauge: how many
/// consecutive green canaries, and whether the gate is eligible. Human
/// gauge to stdout; one structured `summary.readiness` event to stderr so
/// CI can branch on it. Read-only (no ledger append for the query itself).
let runReadiness () : int =
    match RunLedger.configuredDir () with
    | None ->
        eprintfn "projection: no run ledger configured. Set PROJECTION_LEDGER_DIR to accumulate run history."
        4
    | Some dir ->
        // R1b — the orphan `beginRun` is retired: the run-envelope bracket
        // (the ONE owner, S4) brackets the readiness query directly, so
        // `summary.readiness` rides a CONFORMING stream (runStart first,
        // §10 terminal always) and the run is capturable. Bracketed here
        // rather than via `withRun` to honor the documented contract above:
        // no ledger append for the query itself.
        // NM-34b — `check ready` reads the ledger; it has no hashable run input
        // and touches no ledger (the documented no-append contract above), so it
        // declares the empty digest + no `LedgerRef`.
        RunEnvelope.bracket "projection check ready" ignore Map.empty (fun () -> "", []) (fun () ->
            let records = RunLedger.read dir
            let r = RunLedger.readiness records
            let recent =
                records |> List.choose (fun e -> e.Canary) |> List.rev |> List.truncate 16 |> List.rev
            // #14 — the changeset trend: registered transforms per run over the last 16
            // runs, as a sparkline beside the dots (a settling model trends down toward
            // cutover). Same window as `recent`.
            let series =
                records |> List.map (fun e -> e.Registered) |> List.rev |> List.truncate 16 |> List.rev
            // Human channel — the themed cutover board (color on a TTY, plain piped).
            TtyRenderer.renderReadinessBoard r recent series (RunLedger.ledgerPath dir)
            // Machine channel — one structured summary.readiness event (CI gates
            // on `eligible`).
            LogSink.emit
                { LogSink.envelope LogSink.Info LogSink.Summary "summary.readiness"
                    (Map.ofList [
                        "totalRuns",        box r.TotalRuns
                        "canaryRuns",       box r.CanaryRuns
                        "consecutiveGreen", box r.ConsecutiveGreen
                        "threshold",        box r.Threshold
                        "lastCanary",       (match r.LastCanary with Some c -> box c | None -> null)
                        "recentCanaries",   box recent
                        "eligible",         box r.Eligible ]) with
                    Phase = LogSink.End }
            0, LogSink.Succeeded)

/// Live-probe a target for the setup readback:
/// `(ref, reachable, grants)` where `grants` pairs each planned write action
/// (ALTER / INSERT / DELETE / CREATE TABLE) with its database-scope grant
/// status. Reuses the same machinery the migrate pre-flights use — resolve the
/// ref, open the connection (reachability), capture the grant evidence (the §14
/// / A.6 readback). D3 — the broader grants (INSERT / CREATE TABLE / DELETE) are
/// surfaced, not collapsed to ALTER alone, so the setup view names exactly which
/// writes a target permits. A failed resolve / open is `unreachable`, never a
/// stack trace; every grant reads false when the target is unreachable.
let probeTarget (connRef: string) : string * bool * (Preflight.WriteAction * bool) list =
    let unreachable = (connRef, false, [])
    match TransferSpec.parseConnectionSpec connRef with
    | Error _ -> unreachable
    | Ok ref ->
        match ConnectionResolver.resolve "Setup" ref with
        | Error _ -> unreachable
        | Ok connStr ->
            try
                use cnn = new Microsoft.Data.SqlClient.SqlConnection(connStr)
                cnn.OpenAsync().GetAwaiter().GetResult()
                let grants =
                    match (Preflight.captureGrantEvidence cnn).GetAwaiter().GetResult() with
                    | Ok g    -> Preflight.allWriteActions |> List.map (fun a -> a, Preflight.coversAtDatabaseScope a g)
                    | Error _ -> Preflight.allWriteActions |> List.map (fun a -> a, false)
                (connRef, true, grants)
            with _ -> unreachable

/// `projection setup [--conn <ref>]` — the arrival/setup readback (§14 /
/// Appendix A.6): a plain read of the operator switches that are set and those
/// that are not, in the same calm voice. With `--conn`, it also probes the
/// target — reachability + the ALTER grant. Reads the env + probes at the
/// boundary; the view is pure.
let runSetup (connRef: string option) : int =
    let envOpt (k: string) =
        match System.Environment.GetEnvironmentVariable k with
        | null | "" -> None
        | v         -> Some v
    let view =
        TtyRenderer.buildSetupView
            (RunLedger.configuredDir ())
            (envOpt "PROJECTION_ALLOW_EXECUTE" = Some "1")
            (Watch.resolveDwellMs ())
            (envOpt "PROJECTION_BENCH_DIR")
            (connRef |> Option.map probeTarget)
    TtyRenderer.renderAnswer false View.defaultDepth view
    0

/// R1d — `projection inspect <runId> [<runId>]`: the stored Run aggregate,
/// rendered (D5 — the store already resolves; this verb renders). One id
/// answers "what was this run?"; two ids answer "what moved between these
/// runs?" through `Run.diff` (the UoM delta surface; the harness's
/// before/after protocol is its restriction to a key-label set).
/// Read-only and envelope-free — stays outside the bracket per the R1b law.
/// NOTE: the card named this verb `diff <runA> <runB>`; that name is held
/// by the shipped catalog-refs diff, so the run-grain projection lands
/// under `inspect` — one noun per surface, the collision named here.
/// A stored run as a navigable `View` (#18/#11) — the inspect surface joins the
/// one substrate (it gained the json / `--query` lens the old printf face never
/// had — the one-substrate law). A `Doc` of the essence (a `Hero` verdict + the
/// counts) over diggable `Disclosure`s (transforms / artifacts / ledgers / bench),
/// which the Navigator's `→` opens one at a time. `toJson` carries the whole tree.
let buildInspectView (r: Run.Run) : View.View =
    let outcomeStatus =
        match r.Outcome.ToLowerInvariant() with
        | "ok" | "success" | "succeeded" | "green" -> View.Ok
        | "error" | "failed" | "failure" | "red" -> View.Bad
        | _ -> View.Neutral
    let header =
        [ View.Hero (outcomeStatus, sprintf "%s — %s" r.RunId r.Command)
          View.Field ("at", r.Ts, View.Neutral)
          View.Field (
              "outcome",
              r.Outcome + (match r.Canary with Some c -> sprintf "   ·   canary %s" c | None -> ""),
              outcomeStatus)
          View.Field ("events", string (List.length r.Events), View.Neutral) ]
    let transforms =
        View.Disclosure (
            sprintf "transforms   ·   %d registered, %d applied, %d declined" r.Registered r.Applied r.Declined,
            View.Neutral,
            [ View.Field ("registered", string r.Registered, View.Neutral)
              View.Field ("applied", string r.Applied, View.Neutral)
              View.Field ("declined", string r.Declined, View.Neutral) ])
    let artifacts =
        let arts = r.Artifacts |> Map.toList
        View.Disclosure (
            sprintf "artifacts   ·   %d" (List.length arts),
            View.Neutral,
            (if List.isEmpty arts then [ View.Note "none" ]
             else arts |> List.map (fun (k, _) -> View.Field (k, "", View.Neutral))))
    let ledgers =
        match r.Ledgers with
        | [] -> []
        | ls ->
            [ View.Disclosure (
                  sprintf "ledgers extended   ·   %d" (List.length ls),
                  View.Neutral,
                  (ls
                   |> List.map (fun l ->
                       match l with
                       | Run.JournalRef d -> View.Field ("journal", d, View.Neutral)
                       | Run.EpisodeRef (t, o) -> View.Field ("episode", sprintf "%s ordinal %d" t o, View.Neutral)))) ]
    let bench =
        match r.Bench with
        | None -> []
        | Some b ->
            let top = b.Stats |> List.sortByDescending (fun s -> s.TotalMs) |> List.truncate 8
            if List.isEmpty top then []
            else
                [ View.Disclosure (
                      sprintf "slowest labels   ·   top %d" (List.length top),
                      View.Neutral,
                      (top |> List.map (fun s -> View.Field (s.Label, sprintf "%d ms" s.TotalMs, View.Neutral)))) ]
    View.Doc (header @ [ transforms; artifacts ] @ ledgers @ bench)

/// `inspect` with NO id (#10 — the time axis) — open the LATEST run and walk the
/// ledger with `PgUp`/`PgDn`. The runs are sorted newest-first (ISO `Ts` sorts
/// chronologically as text); the interactive Navigator scrubs them, each frame
/// re-`buildInspectView`'d on demand (the I/O closure the Navigator stays free of).
/// Piped / `--json` / `--query` render the newest run's document one-shot — same
/// one-substrate fallback as `inspect <id>`.
let runInspectHistory (asJson: bool) : int =
    match Run.storeDir () with
    | None ->
        eprintfn "No run store is configured. Set PROJECTION_LEDGER_DIR to capture runs, then inspect."
        4
    | Some dir ->
        match Run.list dir |> List.sortByDescending (fun r -> r.Ts) with
        | [] ->
            eprintfn "No runs in the store at %s." dir
            1
        | (newest :: _) as runs ->
            let interactive =
                Intervene.isInteractive ()
                && not System.Console.IsOutputRedirected
                && not asJson
                && Option.isNone TtyRenderer.queryPath.Value
            if interactive then
                let arr = List.toArray runs
                Navigator.runHistory arr.Length 0 (fun i -> buildInspectView arr.[i])
            else
                TtyRenderer.renderAnswer asJson View.defaultDepth (buildInspectView newest)
                0

let runInspect (idA: string) (idB: string option) (asJson: bool) : int =
    match Run.storeDir () with
    | None ->
        eprintfn "No run store is configured. Set PROJECTION_LEDGER_DIR to capture runs, then inspect by run id."
        4
    | Some dir ->
        let load (id: string) : Run.Run option = Run.load dir id
        match idB with
        | None ->
            match load idA with
            | None ->
                eprintfn "Run %s is not in the store at %s." idA dir
                1
            | Some r ->
                // The dig-as-motion Navigator on a real terminal (#11/#18); piped / --json
                // / --query render the SAME document one-shot (L2 `present` owns the choice).
                Navigator.present asJson View.defaultDepth (buildInspectView r)
        | Some idB ->
            match load idA, load idB with
            | None, _ ->
                eprintfn "Run %s is not in the store at %s." idA dir
                1
            | _, None ->
                eprintfn "Run %s is not in the store at %s." idB dir
                1
            | Some a, Some b ->
                let d = Run.diff None a b
                printfn "Runs %s → %s" (fst d.RunIds) (snd d.RunIds)
                printfn "  commands: %s → %s" (fst d.Commands) (snd d.Commands)
                printfn "  outcomes: %s → %s%s" (fst d.Outcomes) (snd d.Outcomes)
                    (match d.Canaries with
                     | Some ca, Some cb -> sprintf " (canary %s → %s)" ca cb
                     | _ -> "")
                printfn "  transform deltas: %+d registered, %+d applied, %+d declined   events: %+d"
                    d.Registered d.Applied d.Declined d.Events
                let moved = d.BenchDeltas |> List.filter (fun bd -> bd.DeltaMs <> 0L<Run.ms>) |> List.truncate 10
                if List.isEmpty moved then
                    printfn "  wall times: no label moved."
                else
                    printfn "  wall-time movement (largest first):"
                    for bd in moved do
                        let fmt (v: int64<Run.ms> option) = match v with Some x -> sprintf "%d" (int64 x) | None -> "—"
                        printfn "    %-44s %s → %s ms (%+d)" bd.Label (fmt bd.BeforeMs) (fmt bd.AfterMs) (int64 bd.DeltaMs)
                0

/// `diff <refA> <refB>` — change, rendered essence-first (INSTRUMENT slice 1,
/// the first surface of the instrument). Resolves both refs through `Ref`
/// (file / `@runId` / `json:` / `live:`) and renders the catalog change: the
/// plain verdict that leads, then the per-channel dig beneath. `--format json`
/// emits the same `View` as structure. `--module <name>` scopes the COMPUTATION
/// to one module (a smaller, reviewable diff); `--only <channel>` scopes the
/// DISPLAY to one channel (columns / relationships / indexes / sequences / tables).
let runDiff (refAText: string) (refBText: string) (asJson: bool) (depth: int) (channel: string option) (onlyModule: string option) : int =
    let refA, refB = Ref.parse refAText, Ref.parse refBText
    // Espace posture (CROSS_ENVIRONMENT_READINESS.md): two `live:` (physical)
    // OutSystems reads do not share identity — `ReadSide` synthesizes SsKeys from
    // the physical name, so the same entity in two environments will not align.
    // Name it (never a silent, wrong diff); steer to the espace-safe operands.
    if Ref.bothLive refA refB then
        Console.Error.WriteLine "projection diff: comparing two `live:` reads by PHYSICAL identity is espace-unsafe — SsKeys are synthesized from physical names and will not align across OutSystems environments. Use `ossys:<conn>` operands (native GUID identity) for a cross-environment diff, or `projection check shape` for the readiness gate."
    // Both OSSYS-sourced ⇒ the operator wants the espace-safe LOGICAL shape:
    // normalize away the realization-name artifacts `CatalogDiff` compares.
    let norm (c: Catalog) : Catalog = if Ref.bothOssys refA refB then Readiness.toLogicalShape c else c
    let resolve (s: string) = (Ref.resolveCatalog (Ref.parse s)).GetAwaiter().GetResult()
    // `--module <name>` keeps only the named module's kinds before diffing —
    // sequences are catalog-level, so a module scope drops them. Case-insensitive
    // name match; a name that matches nothing yields an empty scope (the diff reads
    // "no differences") — the operator's signal to correct the flag. The raw record
    // update is safe here: the diff only OBSERVES the catalogs (no re-validation /
    // FK closure needed — `CatalogDiff.between` compares by SsKey).
    let scopeModule (cat: Catalog) : Catalog =
        match onlyModule with
        | None -> cat
        | Some name ->
            { cat with
                Modules   = cat.Modules |> List.filter (fun m -> System.String.Equals(Name.value m.Name, name, System.StringComparison.OrdinalIgnoreCase))
                Sequences = [] }
    match resolve refAText with
    | Error errs ->
        Console.Error.WriteLine "projection diff: could not resolve the first reference:"
        printErrors Console.Error errs
        2
    | Ok a ->
        match resolve refBText with
        | Error errs ->
            Console.Error.WriteLine "projection diff: could not resolve the second reference:"
            printErrors Console.Error errs
            2
        | Ok b ->
            match Comparison.catalog.Between (scopeModule (norm a)) (scopeModule (norm b)) with
            | Error e ->
                Console.Error.WriteLine(sprintf "projection diff: %s" e)
                2
            | Ok d ->
                // L2 — the changeset becomes a CONTROL surface: dig the move-lanes live on
                // a terminal, the same document one-shot when piped / --json / --query.
                Navigator.present asJson depth (Comparison.renderCatalogChangeScoped channel d)

/// `projection compare <A> <B>` — NM-71/WP9: the read-only multi-environment
/// readiness check. Resolves both operands to catalogs (the `Ref` machinery,
/// like `diff`), runs the schema-delta + data-dealbreaker compare, prints the
/// roll-up (or `--format json`), and writes `compare.json`. Advisory — exits 0
/// (the report carries the readiness verdict); a malformed operand exits 2.
/// The SOURCE operand resolves to a `Source` so a live env can be PROFILED —
/// the data-dealbreaker section reads A's data against B's declared model. A
/// static source (file / `@runId` / json) carries no profile, so the section
/// stays honestly advisory-silent; a live env supplies it. A profiling failure
/// degrades to advisory-silent (never aborts — the schema delta still leads).
let runCompare (refAText: string) (refBText: string) (asJson: bool) : int =
    let refA, refB = Ref.parse refAText, Ref.parse refBText
    // Espace posture — see `runDiff`. Two `live:` reads of OutSystems environments
    // do not share identity; name the hazard rather than emit a silently-wrong compare.
    if Ref.bothLive refA refB then
        Console.Error.WriteLine "projection compare: comparing two `live:` reads by PHYSICAL identity is espace-unsafe — SsKeys are synthesized from physical names and will not align across OutSystems environments. Use `ossys:<conn>` operands (native GUID identity) for a cross-environment comparison, or `projection check shape` for the readiness gate."
    let norm (c: Catalog) : Catalog = if Ref.bothOssys refA refB then Readiness.toLogicalShape c else c
    let resolve (s: string) = (Ref.resolveCatalog (Ref.parse s)).GetAwaiter().GetResult()
    let resolveSrc (s: string) = (Ref.resolveSource (Ref.parse s)).GetAwaiter().GetResult()
    match resolveSrc refAText with
    | Error errs ->
        Console.Error.WriteLine "projection compare: could not resolve the first reference:"
        printErrors Console.Error errs
        2
    | Ok srcA ->
        match (Source.read srcA).GetAwaiter().GetResult() with
        | Error errs ->
            Console.Error.WriteLine "projection compare: could not read the first reference's catalog:"
            printErrors Console.Error errs
            2
        | Ok a ->
            match resolve refBText with
            | Error errs ->
                Console.Error.WriteLine "projection compare: could not resolve the second reference:"
                printErrors Console.Error errs
                2
            | Ok b ->
                // Live-profile the source when it can (a live env). The acquire
                // is the reified capability (`Source.profile` = `Some f` iff
                // profilable); a failure → advisory-silent, not a hard error.
                let profileA =
                    match Source.profile srcA with
                    | None -> None
                    | Some acquire ->
                        match (acquire a).GetAwaiter().GetResult() with
                        | Ok p -> Some p
                        | Error _ -> None
                let source : Compare.Operand = { Label = refAText; Catalog = norm a; Profile = profileA }
                let target : Compare.Operand = { Label = refBText; Catalog = norm b; Profile = None }
                let report = Compare.compute source target
                if asJson then printfn "%s" (Compare.toJsonString report)
                else Compare.render report |> List.iter (fun line -> printfn "%s" line)
                System.IO.File.WriteAllText("compare.json", Compare.toJsonString report)
                0

/// `projection check shape` — the espace-safe cross-environment readiness gate
/// (CROSS_ENVIRONMENT_READINESS.md §4/§5). Reads the agreed shape + every
/// `confirm` environment via OSSYS (`Source.ofOssys` → native GUID identity, so
/// the comparison is espace-safe), profiles each env's data, rolls a
/// `Readiness.ReadinessReport`, prints the roll-up (or `--format json`), writes
/// `readiness.json`, and exits 0 (estate ready) / 5 (not ready — a real schema
/// divergence or a data dealbreaker) / 6 (an env could not be read). Read-only.
let runCheckShape (agreedLabel: string) (agreedRef: string) (confirm: (string * string) list) (asJson: bool) : int =
    let readCatalog (refStr: string) = (Source.read (Source.ofOssys refStr)).GetAwaiter().GetResult()
    match readCatalog agreedRef with
    | Error errs ->
        Console.Error.WriteLine(sprintf "projection check shape: could not read the agreed shape '%s':" agreedLabel)
        printErrors Console.Error errs
        6
    | Ok agreedCatalog ->
        let agreedOperand : Compare.Operand = { Label = agreedLabel; Catalog = agreedCatalog; Profile = None }
        // Each confirm env: its OSSYS catalog (schema) + a profile of its live
        // data (the dealbreaker evidence). A profile failure degrades to
        // advisory-silent (the schema verdict still leads), never aborts.
        let resolveEnv (label: string, refStr: string) : Result<string * Compare.Operand> =
            let src = Source.ofOssys refStr
            match (Source.read src).GetAwaiter().GetResult() with
            | Error errs -> Result.failure errs
            | Ok catalog ->
                let profile =
                    match Source.profile src with
                    | None -> None
                    | Some acquire ->
                        match (acquire catalog).GetAwaiter().GetResult() with
                        | Ok p    -> Some p
                        | Error _ -> None
                Result.success (label, ({ Label = label; Catalog = catalog; Profile = profile } : Compare.Operand))
        let resolved = confirm |> List.map resolveEnv
        let readErrors = resolved |> List.collect (function Error es -> es | Ok _ -> [])
        match readErrors with
        | _ :: _ ->
            Console.Error.WriteLine "projection check shape: could not read one or more confirm environments:"
            printErrors Console.Error readErrors
            6
        | [] ->
            let envs = resolved |> List.choose (function Ok v -> Some v | Error _ -> None)
            let report = Readiness.compute agreedLabel agreedOperand envs
            if asJson then printfn "%s" (Readiness.toJsonString report)
            else Readiness.render report |> List.iter (fun line -> printfn "%s" line)
            System.IO.File.WriteAllText("readiness.json", Readiness.toJsonString report)
            if Readiness.isReady report then 0 else 5

/// NM-37 — the explain story as a `View` (the masterful base #3 substrate),
/// built PURELY from the filtered transform trail + findings so it is testable
/// without the projection I/O. The transform trail uses the `View.Trail` block
/// (built for exactly this surface, previously zero producers); the
/// empty/findings states use `View.Note`/`View.Field`/`View.Action`. The whole
/// document routes through `TtyRenderer.renderAnswer`, so explain gains the
/// pretty + plain + JSON (`--format json` / `--query`) lenses every other answer
/// surface carries — the human and machine views are one value, never a parallel
/// print path. The trail is rendered through the SAME
/// `EventProjection.transformKindRender` the event stream uses, so the two
/// trails cannot drift.
let explainView
    (ssKeyText: string)
    (trail: LineageEvent list)
    (diags: DiagnosticEntry list)
    : View.View =
    let header = View.Field ("explain", ssKeyText, View.Neutral)
    if List.isEmpty trail && List.isEmpty diags then
        View.Doc
            [ header
              View.Blank
              View.Note "no transforms or findings matched"
              View.Action "try a fuller name, or a model that exercises this node" ]
    else
        // The transform trail: one step per touching transform, the step label
        // carrying the pass name and the rendered kind tag, the optional detail
        // its decision/rationale.
        let trailBlock =
            if List.isEmpty trail then []
            else
                let steps =
                    trail
                    |> List.map (fun e ->
                        let tag, detail = EventProjection.transformKindRender e.TransformKind
                        let stepLabel = sprintf "%s %s %s" e.PassName Theme.dot tag
                        stepLabel, detail)
                [ View.Trail ("transforms", steps); View.Blank ]
        // The findings: each a status-glyphed field (severity → status), its
        // suggested fix the next-action line beneath it.
        let findingBlocks =
            if List.isEmpty diags then []
            else
                let rows =
                    diags
                    |> List.collect (fun d ->
                        let st =
                            match d.Severity with
                            | DiagnosticSeverity.Error   -> View.Bad
                            | DiagnosticSeverity.Warning -> View.Warn
                            | _                          -> View.Neutral
                        let field = View.Field (d.Code, d.Message, st)
                        match d.SuggestedConfig with
                        | Some c -> [ field; View.Action (sprintf "fix: %s = %s" c.Path c.Value) ]
                        | None   -> [ field ])
                View.Note "findings" :: rows @ [ View.Blank ]
        View.Doc ([ header; View.Blank ] @ trailBlock @ findingBlocks)

/// P3 (REPORTING_HORIZON polish) — `explain <config> <ssKey>`. The drill-down
/// doorway: run the projection, then tell the full story for ONE node — every
/// transform that touched it (with the decision + rationale, rendered through
/// the SAME `EventProjection.transformKindRender` the event stream uses) and
/// every finding (with its suggested fix). "Every number is a doorway."
/// `ssKey` matches by exact root or substring, so `CustomerId` finds
/// `OSUSR_FOO.OrderHeader.CustomerId`.
let runExplain (configPath: string) (ssKeyText: string) (asJson: bool) (depth: int) : int =
    match Config.fromFile configPath with
    | Error errs ->
        printErrors Console.Error errs
        2
    | Ok config ->
        match (Compose.runWithConfig config).GetAwaiter().GetResult() with
        | Error errs ->
            printErrors Console.Error errs
            2
        | Ok report ->
            let matchesKey (k: SsKey) =
                let s = SsKey.rootOriginal k
                s = ssKeyText || s.Contains(ssKeyText)
            let trail = report.Trail |> List.filter (fun e -> matchesKey e.SsKey)
            let diags =
                (report.Diagnostics @ report.PassDiagnostics)
                |> List.filter (fun d -> match d.SsKey with Some k -> matchesKey k | None -> false)
            // L2 — explain is a read surface too: dig the transform trail + findings live
            // on a terminal, one-shot when piped. `present` returns 0; the empty-match case
            // keeps its 1 exit (the "nothing found" signal) after the view is shown either way.
            let shown = Navigator.present asJson depth (explainView ssKeyText trail diags)
            if List.isEmpty trail && List.isEmpty diags then 1 else shown

/// P4 (REPORTING_HORIZON polish) — `suggest-config <config> [--apply <out>]`.
/// Run the projection, collect every actionable `SuggestedConfig` from the
/// diagnostic streams, merge by path (dedup), **rank by impact** (how many
/// nodes each edit touches), and present the to-do list highest-leverage
/// first. `--apply` writes the merged patch JSON. This is principle #5 made
/// concrete: don't just describe — recommend, ranked, and hand over the patch.
let runSuggestConfig (configPath: string) (applyTo: string option) : int =
    match Config.fromFile configPath with
    | Error errs ->
        printErrors Console.Error errs
        2
    | Ok config ->
        let task = Compose.runWithConfig config
        match task.GetAwaiter().GetResult() with
        | Error errs ->
            printErrors Console.Error errs
            2
        | Ok report ->
            // Name the touched nodes by `Name` (the projection's read catalog), not the
            // GUID `rootOriginal` — the same legibility the diff + verify-data carry.
            let names = Catalog.nameIndex report.ReadCatalog
            let merged =
                (report.Diagnostics @ report.PassDiagnostics)
                |> List.choose (fun e -> e.SuggestedConfig |> Option.map (fun c -> c, e.SsKey))
                |> List.groupBy (fun (c, _) -> c.Path)
                |> List.map (fun (path, items) ->
                    let c0 = fst (List.head items)
                    let ssKeys =
                        items
                        |> List.choose (fun (_, k) -> k |> Option.map (nameOf names))
                        |> List.distinct
                    {| Path = path; Value = c0.Value; Note = c0.Note
                       Count = List.length items; SsKeys = ssKeys |})
                |> List.sortByDescending (fun s -> s.Count)
            printfn ""
            if List.isEmpty merged then
                printfn "  %s no actionable config edits — nothing to apply" Theme.ok
                0
            else
                printfn "  %d config edit(s) suggested, by impact:" (List.length merged)
                printfn ""
                for s in merged do
                    printfn "  %s %s = %s   (%d node%s)"
                        Theme.arrow s.Path s.Value s.Count (if s.Count = 1 then "" else "s")
                    match s.Note with
                    | Some n -> printfn "      %s %s" Theme.dot n
                    | None   -> ()
                printfn ""
                match applyTo with
                | Some out ->
                    let patch = System.Text.Json.Nodes.JsonObject()
                    for s in merged do
                        patch.[s.Path] <- System.Text.Json.Nodes.JsonValue.Create(s.Value)
                    let json =
                        patch.ToJsonString(
                            System.Text.Json.JsonSerializerOptions(WriteIndented = true))
                    File.WriteAllText(out, json)
                    printfn "  %s merged patch (%d edits) written to %s" Theme.ok (List.length merged) out
                | None ->
                    printfn "  %s --apply <out.json> to write the merged patch" Theme.dot
                0

/// §5.6 — `policy-diff <config-a> <config-b>`. Diff what two configs would
/// project over the shared Catalog (read from config-a's Model.Path). Renders
/// the five-axis structural delta + the changed-kind set. Pure/structural —
/// no live DB (Profile.empty); the operator's "diff policy A vs B" question.
let runPolicyDiff (configAPath: string) (configBPath: string) : int =
    match Config.fromFile configAPath, Config.fromFile configBPath with
    | Error errors, _
    | _, Error errors ->
        printErrors Console.Error errors
        6
    | Ok cfgA, Ok cfgB ->
        let result = (PolicyDiff.diffConfigs cfgA cfgB).GetAwaiter().GetResult()
        let exitCode =
            match result with
            | Error errors ->
                printErrors Console.Error errors
                2
            | Ok diff ->
                let s = diff.StructuralDiff
                printfn "%s"
                    (if s.AnyChanged then "The two policies differ." else "The two policies are identical.")
                let axis (name: string) (changed: bool) =
                    printfn "  %-13s %s" name (if changed then "changed" else "same")
                axis "selection"    s.Selection.Changed
                axis "emission"     s.Emission.Changed
                axis "insertion"    s.Insertion.Changed
                axis "tightening"   s.Tightening.Changed
                axis "userMatching" s.UserMatching.Changed
                printfn "  changed tables: %d" (List.length diff.ChangedKinds)
                for k in diff.ChangedKinds do
                    printfn "    - %s" (SsKey.rootOriginal k)
                0
        dumpBench "policy-diff"
        exitCode

/// `projection migrate --from <modelA.json> --to <modelB.json> [--allow-drops]`
/// — the L3 bullseye's dry-run: diff the two states and print the
/// minimum-viable migration plan (the change-manifest of δ) that `migrate A B`
/// would execute. Fail-loud: a destructive drop (without `--allow-drops`) or a
/// non-shape facet change is refused with a non-zero exit and an explanation,
/// never a silent plan. The `--execute` leg (against a live deployed DB) is
/// `MigrationRun.execute`; this surface previews what it would do.

/// The §10 inexpressible-change verdict, voiced (`migrate.inexpressible`): the
/// statement carries the count; each refusing entry is demoted into the
/// disclosure beneath, its code beside its cause. The newline join is data
/// marshalling into the envelope payload, not prose.
let private renderInexpressible (entries: DiagnosticEntry list) : unit =
    TtyRenderer.renderVoicedTo Console.Error "migrate.inexpressible"
        (Map.ofList
            [ "entryCount", box (List.length entries)
              "entries",
              box (entries
                   |> List.map (fun e -> sprintf "%s (%s)" e.Message e.Code)
                   |> String.concat "\n") ])

/// The §10 stop for a `MigrationError` the gates do not own, voiced
/// (`migrate.stopped`): the statement frames the stop; the plain located cause
/// is `Voice.migrationStopDetail` (the catalog's typed projection over the DU).
let private renderMigrateStopped (e: MigrationError) : unit =
    TtyRenderer.renderVoicedTo Console.Error "migrate.stopped"
        (Map.ofList [ "cause", box (Voice.migrationStopDetail e) ])

/// The migrate preview as a §6/§9 minimality Surface — the smallest faithful
/// change, said plain (statement first, the per-move breakdown beneath), never
/// `norm=`. The schema change-manifest of δ: exactly the difference, and no more.
let migratePreviewSurface (artifacts: MigrationArtifacts) : Surface.Surface =
    let p = artifacts.Plan.Preview
    let c = p.Channels
    if Migration.isIdempotent artifacts.Plan then
        { Statement      = View.Hero(View.Ok, "Nothing to apply. The two states are already identical.")
          Substantiation = []
          Action         = None }
    else
        let removed = c.RemovedKinds + c.RemovedAttributes
        let status  = if removed > 0 then View.Warn else View.Ok
        let renames =
            p.RenamedKinds
            |> List.map (fun (_, fromN, toN) -> sprintf "%s → %s" (Name.value fromN) (Name.value toN))
        let h = Theme.humane
        { Statement      =
            View.Hero(status, sprintf "%s changes to apply — exactly the difference between the two states, and no others." (h p.Norm))
          Substantiation =
            [ View.Field("tables", sprintf "%s added · %s dropped · %s renamed" (h c.AddedKinds) (h c.RemovedKinds) (h c.RenamedKinds), View.Neutral)
              View.Field("columns", sprintf "%s added · %s dropped · %s renamed · %s changed" (h c.AddedAttributes) (h c.RemovedAttributes) (h c.RenamedAttributes) (h c.ChangedAttributes), View.Neutral) ]
            @ (if List.isEmpty renames then [] else [ View.Lane("⟲", "rename", View.Ok, renames) ])
            @ [ View.Field("to run", sprintf "%s statement(s) · %s rename(s) recorded" (h (List.length artifacts.SchemaStatements)) (h (List.length artifacts.RefactorLog)), View.Neutral) ]
          Action         = Some(View.Action "Apply against the target database with --execute.") }

/// Voice the §5 declared-loss gate for undeclared destructive removals — the
/// consent moment a drop must pass. The exit (9) is unchanged; the §5 statement
/// and the approval lever lead, the named removals ride in the substantiation.
let renderUndeclaredDropGate (violations: SchemaLoss list) : unit =
    let tokens = violations |> List.map Migration.lossToken |> String.concat ", "
    let detail = sprintf "%d removal(s) await approval: %s" (List.length violations) tokens
    TtyRenderer.renderGate "projection migrate"
        (Preflight.refusalOf [ ValidationError.create "migrate.undeclaredDestructiveChange" detail ])

/// Shared dry-run renderer for a `migrate` preview outcome — the change-manifest
/// of δ, or a fail-loud refusal (undeclared losses / inexpressible change).
let reportPreviewOutcome (header: string) (result: Result<MigrationArtifacts, MigrationError>) : int =
    let exitCode =
        match result with
        | Error (RefusedByViolations violations) ->
            renderUndeclaredDropGate violations
            9
        | Error (RefusedBySchemaErrors entries) ->
            renderInexpressible entries
            9
        | Error other ->
            renderMigrateStopped other
            2
        | Ok artifacts ->
            printfn "%s" header
            TtyRenderer.renderAnswer false View.defaultDepth (Surface.render (migratePreviewSurface artifacts))
            0
    dumpBench "migrate"
    exitCode

let runMigratePreview (fromPath: string) (toPath: string) (declaration: LossDeclaration) : int =
    let loaded =
        task {
            let! a = Compose.read fromPath
            let! b = Compose.read toPath
            return a, b
        }
    let a, b = loaded.GetAwaiter().GetResult()
    match a, b with
    | Error errors, _ ->
        printErrors Console.Error errors
        6
    | _, Error errors ->
        printErrors Console.Error errors
        6
    | Ok source, Ok target ->
        reportPreviewOutcome
            (sprintf "projection migrate %s -> %s  (dry-run)" fromPath toPath)
            (MigrationRun.preview declaration source target)

/// `projection migrate --to <modelB.json> --store <lifecycle.json> [--allow-drops
/// | --declare-drop <token>...]` — the **snapshot⊖snapshot** dry-run (6.H). State
/// A is the prior emission's schema, reconstructed from the durable
/// `LifecycleStore`; B is the new authored model. Closes the emission→snapshot→
/// diff loop: the diff basis is provenance, not a second hand-authored model. A
/// missing store is genesis (A = ∅).
let runMigrateFromStore (storePath: string) (toPath: string) (declaration: LossDeclaration) (forceGenesis: bool) : int =
    let bRead = (Compose.read toPath).GetAwaiter().GetResult()
    match bRead with
    | Error errors ->
        printErrors Console.Error errors
        6
    | Ok target ->
        // `--from empty` forces A = ∅ (genesis) against a populated store — the
        // banner names the forced from-scratch basis so the displacement is not
        // mistaken for a store-derived diff.
        let banner =
            if forceGenesis then sprintf "projection migrate (from empty) -> %s  (dry-run, genesis)" toPath
            else sprintf "projection migrate (store:%s) -> %s  (dry-run, snapshot⊖snapshot)" storePath toPath
        reportPreviewOutcome
            banner
            (MigrationRun.previewFromStoreForcing forceGenesis storePath declaration target)

/// Derive the A2 pre-flight's planned-writes from a migration's schema
/// statements: every DDL statement maps to the write it performs at the sink
/// (ALTER on its table; CREATE for new tables/sequences). Drives the permission
/// gate before any mutation.
let plannedWritesOf (stmts: Statement list) : Preflight.PlannedWrite list =
    let alterOn (t: TableId) : Preflight.PlannedWrite =
        { Schema = TableId.schemaText t; Table = TableId.tableText t; Action = Preflight.Alter }
    stmts
    |> List.choose (fun s ->
        match s with
        | AlterTableAddColumn (t, _) | AlterTableAlterColumn (t, _) | AlterTableDropColumn (t, _)
        | AlterTableAddForeignKey (t, _) | AlterTableDropConstraint (t, _)
        | AlterTableNoCheckConstraint (t, _) | AlterTableDisableConstraint (t, _)
        | DropIndex (t, _) -> Some (alterOn t)
        | CreateIndex idx -> Some (alterOn idx.Table)
        | CreateTable (t, _, _, _, _, _) ->
            Some { Schema = TableId.schemaText t; Table = TableId.tableText t; Action = Preflight.CreateTable }
        | CreateSequence seq -> Some { Schema = seq.Schema; Table = Name.value seq.Name; Action = Preflight.CreateTable }
        | DropSequence (schema, name) -> Some { Schema = schema; Table = name; Action = Preflight.CreateTable }
        | _ -> None)
    |> List.distinct

/// Print a `MigrationError` and map it to an exit code (shared by the
/// schema-only and cross-substrate migrate executors).
let reportMigrationError (e: MigrationError) : int =
    match e with
    | RefusedByViolations violations ->
        renderUndeclaredDropGate violations
        9
    | RefusedBySchemaErrors entries ->
        renderInexpressible entries
        9
    | RefusedByCdc tracked ->
        // §5 CDC gate — the consent surface (consequence as meaning + the one
        // lever), keyed by the closed GateLabel DU; the tracked count and the
        // --allow-cdc lever ride in the located detail. Exit 9 unchanged.
        TtyRenderer.renderGate "projection migrate"
            (Preflight.refusalOf
                [ ValidationError.create "migrate.cdcTrackedSink"
                    (sprintf "%d table(s) are CDC-tracked; --allow-cdc accepts the capture." (List.length tracked)) ])
        9
    | RefusedByCdcUnverifiable msg ->
        // NM-54 — the CDC probe could not run; an unverifiable CDC state is
        // UNSAFE, so the schema change is REFUSED through the same §5 gate
        // surface as an observed-CDC refusal. Exit 9 (a clean named refusal),
        // never a crash.
        TtyRenderer.renderGate "projection migrate"
            (Preflight.refusalOf
                [ ValidationError.create "migrate.cdcStateUnverifiable" msg ])
        9
    | RefusedByTightening msg ->
        // §5 data-compat gate — the same surface the pre-flight probe renders.
        TtyRenderer.renderGate "projection migrate"
            (Preflight.refusalOf [ ValidationError.create "migrate.dataViolatesTightening" msg ])
        9
    | SchemaReadFailed es ->
        // The §10 schema-read frame states the finding; the located causes
        // ride beneath (the raw header line is retired).
        printErrors Console.Error es
        6
    | ExecutionRolledBack (msg, n) ->
        // M21 — the destructive write failed but the compensating-undo arm
        // (M12's groupoid inverse) returned the substrate to A; no changes
        // remain. A clean named refusal on the destructive axis (exit 9), not a
        // corruption — "refuses without damage."
        TtyRenderer.renderGate "projection migrate"
            (Preflight.refusalOf
                [ ValidationError.create "migrate.executionRolledBack"
                    (sprintf "the migration was rolled back to its original state (%d rename(s) reverted): %s" n msg) ])
        9
    | PartialWriteUnrecovered (msg, residual) ->
        // M21 — the loudest honest outcome: a non-rename residual could not be
        // safely auto-inverted (the inverse would be a destructive op the engine
        // refuses by policy). Name the residual; never claim success. Exit 9
        // (destructive axis) with the exact divergence from A surfaced.
        TtyRenderer.renderGate "projection migrate"
            (Preflight.refusalOf
                [ ValidationError.create "migrate.partialWriteUnrecovered" msg ])
        Console.Error.WriteLine(PhysicalSchema.renderDiff residual)
        9
    | other ->
        renderMigrateStopped other
        2

/// The A1 connection + A2 permission pre-flights against a sink connection,
/// given the planned schema statements. Returns `Ok ()` to proceed or a printed
/// refusal + exit code. A2's grant capture is database-scope (survey-gated
/// object-scope refinement, OPEN-2 / P1).
let migratePreflights (label: string) (cnn: Microsoft.Data.SqlClient.SqlConnection) (planned: Preflight.PlannedWrite list) : System.Threading.Tasks.Task<Result<unit, int>> =
    // Each pre-flight refusal renders through the §5 Gate surface — the
    // consequence as meaning + the one plain imperative — never a raw header +
    // dump. NM-61: the exit HONORS `classify` (the A1 single-source seam the
    // transfer path routes through at `runCore`) rather than flattening every
    // refusal to 7 — so the connection axis exits 6 (its own axis) while the
    // permission/grant axis stays 7 (`migrate.insufficientGrant` /
    // `migrate.grantProbeFailed` classify to 7). The displayed exit matches the
    // returned one because both come from the one `refusalOf` classification.
    let refuse (es: ValidationError list) : Result<unit, int> =
        let refusal = Preflight.refusalOf es
        TtyRenderer.renderGate "projection migrate" refusal
        Error refusal.ExitCode
    task {
        // G0 (AC-G0) — the migrate pre-flights compose through the ONE mandatory
        // `Preflight.all`, mirroring the transfer Execute path (`runCore`). The
        // permission gate sequences on the connection gate's task (a hot task is
        // awaitable more than once) so the two probes never run concurrently on
        // the one `cnn` — SqlClient forbids concurrent commands on a connection.
        let connectionGate : System.Threading.Tasks.Task<Result<unit>> = Preflight.connectionPreflight cnn cnn
        let permissionGate : System.Threading.Tasks.Task<Result<unit>> =
            task {
                match! connectionGate with
                | Error es -> return Error es   // never observed: `all` short-circuits on the connection gate first.
                | Ok () ->
                    match! Preflight.captureGrantEvidence cnn with
                    | Error es -> return Error es
                    | Ok grant -> return Preflight.permissionPreflight grant planned
            }
        match! Preflight.all [ connectionGate; permissionGate ] with
        | Error es -> return refuse es
        | Ok () -> return Ok ()
    }

/// G7 — the Decision↔Data tightening pre-flight, wired into the migrate verbs.
/// The operator's response at the §5 tightening gate (the relax-decision the
/// Intervene choice carries): halt, relax just this run, or relax + persist the
/// blessing to projection.json so future headless runs honor it.
type private RelaxDecision =
    | Halt
    | RelaxOnce
    | RelaxAlways

/// Derives the narrowing-to-NOT-NULL overlay from the A→B displacement and, when
/// it is NON-EMPTY, probes the live data source's null counts via
/// `Preflight.tighteningPreflight`, refusing (exit 9 / migrate.dataViolatesTightening)
/// before any write when a tightened column carries NULLs. When the overlay is
/// empty (a non-tightening migration) the probe is SKIPPED entirely — the
/// self-probing pre-flight surveys every kind, a cost a non-tightening migration
/// must not pay. `dataCnn` is the connection whose data is at risk: the in-place
/// `cnn` for MC (state A lives in the sink), the SOURCE for MX.
let tighteningPreflight
    (sourceA: Catalog)
    (target: Catalog)
    (dataCnn: Microsoft.Data.SqlClient.SqlConnection)
    : System.Threading.Tasks.Task<Result<Catalog, int>> =
    task {
        let overlay = Preflight.tighteningOverlay sourceA target
        if Set.isEmpty overlay.EnforceNotNull then return Ok target
        else
            match! Preflight.tighteningViolations dataCnn sourceA overlay with
            | Error es ->
                printErrors Console.Error es
                return Error (Preflight.refusalOf es).ExitCode
            | Ok [] -> return Ok target
            | Ok violations ->
                // §5 Data-compat gate — the live data violates the model's
                // narrowing to NOT NULL. The operator is the STEWARD of the team's
                // model: HALT (the default, and the headless fallback when no
                // blessing exists) remediates the data; RELAX loosens those columns
                // to nullable so the emitted schema fits the data — a NAMED, tracked
                // override, never a silent edit; RELAX-ALWAYS also records the
                // blessing in projection.json so a future HEADLESS run honors it.
                let keys = violations |> List.map (fun v -> v.AttributeKey) |> Set.ofList
                let violationIds = violations |> List.map Preflight.violationKey |> Set.ofList
                // The relaxation, applied + TRACKED (channel 1 / the ledger).
                let applyRelaxation () : Result<Catalog, int> =
                    LogSink.emit (EventProjection.tighteningRelaxedEnvelope keys)
                    // LINT-ALLOW: register-clean operator acknowledgment at the boundary.
                    eprintfn
                        "projection migrate: relaxed %d column(s) to nullable to fit the data; the model still declares NOT NULL — remediate the source and re-tighten."
                        (Set.count keys)
                    Ok (Preflight.relaxTightening keys target)
                let halt () : Result<Catalog, int> =
                    TtyRenderer.renderGate "projection migrate"
                        (Preflight.refusalOf
                            [ ValidationError.create "migrate.dataViolatesTightening" (Preflight.describe violations) ])
                    Error 9
                // Headless honoring (A44): a previously-blessed relaxation covering
                // EVERY violating column lets any run proceed — relaxed + tracked —
                // without prompting. The persisted exception is the reachable
                // equivalent of the interactive choice ("downgrades never silent").
                let configFile = RelaxationStore.configPath ()
                if Set.isSubset violationIds (RelaxationStore.read configFile) then
                    return applyRelaxation ()
                else
                    // LINT-ALLOW: register-clean intervention labels at the CLI boundary
                    // (THE_VOICE §1 — plain imperative, no pronouns, no system-shout).
                    let haltChoice : Intervene.Choice<RelaxDecision> =
                        { Code = "migrate.dataViolatesTightening"
                          Label = "Halt — remediate the data, then re-run"
                          Value = Halt }
                    let relaxOnceChoice : Intervene.Choice<RelaxDecision> =
                        { Code = "migrate.tighteningRelaxed"
                          Label = "Relax once — emit a nullable schema that fits the data"
                          Value = RelaxOnce }
                    let relaxAlwaysChoice : Intervene.Choice<RelaxDecision> =
                        { Code = "migrate.tighteningRelaxed"
                          Label = "Relax always — also record this in projection.json"
                          Value = RelaxAlways }
                    match Intervene.chooseOrDefault
                            (Preflight.describe violations)
                            [ haltChoice; relaxOnceChoice; relaxAlwaysChoice ]
                            haltChoice with
                    | Intervene.Chosen RelaxOnce -> return applyRelaxation ()
                    | Intervene.Chosen RelaxAlways ->
                        // LINT-ALLOW: register-clean boundary acknowledgment.
                        if RelaxationStore.persist configFile violationIds then
                            eprintfn "projection migrate: recorded the relaxation in %s — future runs honor it without prompting." configFile
                        else
                            eprintfn "projection migrate: could not write %s; relaxed for this run only." configFile
                        return applyRelaxation ()
                    | Intervene.Chosen Halt
                    | Intervene.Degraded _ -> return halt ()
    }

/// The migrate EXECUTE leg — apply A→B (the durable-record arm with a
/// `--lifecycle-store`, else the CDC-measure arm) and verify. Extracted so the
/// §5 tightening gate's INTERACTIVE prompt runs on plain stderr BEFORE the live
/// board: a Spectre `Live` region and an interactive prompt cannot share the
/// terminal, so only this leg streams under the board (#9).
let private runMigrateExecuteLeg
    (atomic: bool)
    (allowCdc: bool)
    (declaration: LossDeclaration)
    (sourceA: Catalog)
    (target: Catalog)
    (storePath: string option)
    (envLabel: string option)
    (cnn: Microsoft.Data.SqlClient.SqlConnection)
    : System.Threading.Tasks.Task<int> =
    task {
        match storePath with
        | Some store ->
            let env = parseEnvironment "DEV" envLabel
            match Timeline.create (Projection.Core.Environment.name env) with
            | Error es ->
                printErrors Console.Error es
                return 2
            | Ok tl ->
                let at = System.DateTimeOffset.UtcNow
                let! recorded =
                    MigrationRun.executeAndRecord atomic allowCdc declaration sourceA target store tl env at None cnn
                match recorded with
                | Ok (o, Some chain) ->
                    printfn "Applied and verified — the database now matches the model. %d statement(s) applied; recorded to %s (%d episode(s) on timeline %s)."
                        (List.length o.Artifacts.SchemaStatements) store
                        (EpisodicLifecycle.episodes chain |> List.length) (Timeline.name tl)
                    return 0
                | Ok (_, None) ->
                    Console.Error.WriteLine "The changes were applied, but the read-back does not match the model. No run was recorded."
                    return 9
                | Error e -> return reportMigrationError e
        | None ->
            let! outcome = MigrationRun.executeAndMeasureCdc atomic allowCdc declaration sourceA target cnn
            match outcome with
            | Ok (o, cdcDelta) when o.Verified ->
                printfn "Applied and verified — the database now matches the model. %d statement(s) applied; %d row(s) captured."
                    (List.length o.Artifacts.SchemaStatements) cdcDelta
                eprintfn "projection migrate: note — no --lifecycle-store supplied; no episode persisted (the next diff has no prior to load)."
                return 0
            | Ok _ ->
                Console.Error.WriteLine "The changes were applied, but the read-back does not match the model."
                return 9
            | Error e -> return reportMigrationError e
    }

/// `projection migrate --to <modelB.json> --conn <env|file:ref> --execute
/// [--allow-drops] [--allow-cdc] [--lifecycle-store <path>] [--env <label>]` —
/// B1, the LIVE in-place L3 execution (Promise 8). Reads the deployed state A,
/// runs the A1 connection + A2 permission pre-flights before any mutation,
/// evolves A→B in place, reads B' back, and verifies. Gated by
/// `PROJECTION_ALLOW_EXECUTE=1` (R6). When `--lifecycle-store` (alias `--store`)
/// is supplied, a verified execute durably records the episode onto the timeline
/// (AC-P8) so the next sprint's diff loads it as the prior; absent, behavior is
/// unchanged and a one-line note says no episode was persisted.
let runMigrateExecute (target: Catalog) (connSpec: string) (declaration: LossDeclaration) (allowCdc: bool) (atomic: bool) (storePath: string option) (envLabel: string option) : int =
    if System.Environment.GetEnvironmentVariable "PROJECTION_ALLOW_EXECUTE" <> "1" then
        TtyRenderer.renderVoicedError (ValidationError.create "gate.intent" "PROJECTION_ALLOW_EXECUTE is not set in the environment.")
        dumpBench "migrate"
        7
    else
    let work =
        task {
            match TransferSpec.parseConnectionSpec connSpec with
            | Error es ->
                printErrors Console.Error es
                return 2
            | Ok connRef ->
                let sub : Substrate =
                    { Environment = parseEnvironment "migrate-sink" None; Role = SubstrateRole.Sink; ConnectionRef = connRef }
                match! ConnectionResolver.openSubstrate sub with
                | Error es ->
                    // NM-61 (extended) — the connection axis exits 6 on migrate too,
                    // matching `transfer`. `openSubstrate` surfaces `transfer.connection.*`
                    // (ref-resolve + open), which `classify` maps to 6; single-sourcing
                    // through `refusalOf` keeps this short-circuit in step with the
                    // in-flight `migratePreflights` connection gate (also 6) — closing the
                    // residual where the open-failure path hardcoded 3 while transfer's
                    // identical probe returned 6.
                    printErrors Console.Error es
                    return (Preflight.refusalOf es).ExitCode
                | Ok cnn ->
                    use cnn = cnn
                    // Read state A live, then run the pre-flights on the planned writes.
                    let! readA = ReadSide.read cnn
                    match readA with
                    | Error es -> return reportMigrationError (SchemaReadFailed es)
                    | Ok sourceA ->
                        match MigrationRun.preview declaration sourceA target with
                        | Error e -> return reportMigrationError e
                        | Ok artifacts ->
                            match! migratePreflights "sink" cnn (plannedWritesOf artifacts.SchemaStatements) with
                            | Error code -> return code
                            | Ok () ->
                                // G7 — refuse a narrowing-to-NOT-NULL on NULL-bearing
                                // data before any write (probe the in-place cnn).
                                match! tighteningPreflight sourceA target cnn with
                                | Error code -> return code
                                | Ok effectiveTarget ->
                                    // Relax-once (if chosen) loosened the tightening:
                                    // deploy the EFFECTIVE target so the schema fits
                                    // the data. Clean / halt-headless returns `target`.
                                    let target = effectiveTarget
                                    // #9 — the §5 gate prompt (above) ran on plain
                                    // stderr BEFORE the board; ONLY the execute leg
                                    // streams under the live board (a Spectre Live
                                    // region and an interactive prompt cannot share
                                    // the terminal).
                                    let executeBody () =
                                        (runMigrateExecuteLeg atomic allowCdc declaration sourceA target storePath envLabel cnn)
                                            .GetAwaiter().GetResult()
                                    return
                                        if Watch.shouldWatch prettyMode.Value then
                                            Watch.renderWatch Spines.migrate (Watch.resolveDwellMs ()) executeBody
                                        else executeBody ()
        }
    let code = work.GetAwaiter().GetResult()
    dumpBench "migrate"
    code

/// The migrate-with-data EXECUTE leg — apply the sink schema A→B, then load rows
/// from the source over contract B (durable-record arm with a `--lifecycle-store`,
/// else the straight load) and verify. Extracted for the same reason as
/// `runMigrateExecuteLeg` (#9): the §5 gate prompt runs on plain stderr BEFORE
/// the board; only this leg streams under the live board.
let private runMigrateWithDataLeg
    identityPolicy
    (atomic: bool)
    (allowCdc: bool)
    (declaration: LossDeclaration)
    (sinkSourceA: Catalog)
    (target: Catalog)
    reconciliation
    (storePath: string option)
    (envLabel: string option)
    (dataSource: Microsoft.Data.SqlClient.SqlConnection)
    (sink: Microsoft.Data.SqlClient.SqlConnection)
    : System.Threading.Tasks.Task<int> =
    task {
        match storePath with
        | Some store ->
            let env = parseEnvironment "DEV" envLabel
            match Timeline.create (Projection.Core.Environment.name env) with
            | Error es ->
                printErrors Console.Error es
                return 2
            | Ok tl ->
                let at = System.DateTimeOffset.UtcNow
                let! recorded =
                    MigrationRun.executeWithDataAndRecordWith identityPolicy atomic declaration Transfer.Execute allowCdc
                        sinkSourceA target reconciliation store tl env at None dataSource sink
                match recorded with
                | Ok (o, chain) ->
                    printfn "Schema applied and data loaded — %d table(s) transferred; recorded to %s (%d row(s) captured; %d episode(s) on timeline %s)."
                        (List.length o.Transfer.Kinds) store
                        (EpisodicLifecycle.latest chain).Data.CdcCaptureCount
                        (EpisodicLifecycle.episodes chain |> List.length) (Timeline.name tl)
                    return 0
                | Error e -> return reportMigrationError e
        | None ->
            let! outcome =
                MigrationRun.executeWithDataWith identityPolicy atomic declaration Transfer.Execute allowCdc
                    sinkSourceA target reconciliation dataSource sink
            match outcome with
            | Ok o when o.Schema.Verified ->
                printfn "Schema verified and data loaded — %d table(s) transferred."
                    (List.length o.Transfer.Kinds)
                return 0
            | Ok _ ->
                Console.Error.WriteLine "The schema changes were applied, but the read-back does not match the model. The data load was skipped."
                return 9
            | Error e -> return reportMigrationError e
    }

/// `projection migrate --to <modelB.json> --sink-conn <ref> --source-conn <ref>
/// --execute [--reconcile <table>:<match-column>]... [--user-map <csv>]
/// [--allow-drops] [--allow-cdc]` — the cross-substrate composition (AC-X2):
/// evolve the SINK's schema A→B in place, then transfer rows from the SOURCE
/// substrate into the migrated sink over contract B. When `--reconcile` /
/// `--user-map` entries are present the data leg RE-KEYS user FKs (the
/// Dev→UAT re-key), resolved against contract B via the same
/// `TransferSpec.resolveAllReconciliation` the `transfer` verb uses, and the
/// AC-I5 `validate-user-map` pre-write halt gates first; absent, it is a
/// straight load. Schema is fail-loud + minimum-viable; the data leg runs
/// only if the schema verified.
let runMigrateWithData (target: Catalog) (sinkSpec: string) (sourceSpec: string) (reconcileSpecs: string list) (userMapPath: string option) (declaration: LossDeclaration) (allowCdc: bool) (atomic: bool) (storePath: string option) (envLabel: string option) (sinkCapability: SinkLoadCapability) : int =
    if System.Environment.GetEnvironmentVariable "PROJECTION_ALLOW_EXECUTE" <> "1" then
        TtyRenderer.renderVoicedError (ValidationError.create "gate.intent" "PROJECTION_ALLOW_EXECUTE is not set in the environment.")
        dumpBench "migrate"
        7
    else
    // AC-X2 re-key — parse `--reconcile` (repeatable, MatchByColumn) + the
    // optional `--user-map` CSV (ManualOverride), mirroring the `transfer`
    // verb's parsing. The resolved map is threaded to `executeWithData`
    // (non-empty ⇒ `runReconciling` with the AC-I5 pre-write gate).
    let collectErrs = function Ok _ -> [] | Error es -> es
    let parsedReconciles = reconcileSpecs |> List.map TransferSpec.parseReconcileSpec
    let parsedUserMap : Result<TransferSpec.UserMapEntry list> =
        match userMapPath with
        | None -> Result.success []
        | Some path ->
            if not (System.IO.File.Exists path) then
                Result.failureOf
                    (ValidationError.create "transfer.userMap.fileMissing"
                        (sprintf "user-map file '%s' not found." path))
            else TransferSpec.parseUserMapCsv (System.IO.File.ReadAllText path)
    let specErrors = (parsedReconciles |> List.collect collectErrs) @ collectErrs parsedUserMap
    if not (List.isEmpty specErrors) then
        printErrors Console.Error specErrors
        dumpBench "migrate"
        2
    else
    let reconcileEntries = parsedReconciles |> List.choose (function Ok e -> Some e | _ -> None)
    let userMapEntries   = match parsedUserMap with Ok es -> es | _ -> []
    let work =
        task {
            match TransferSpec.parseConnectionSpec sinkSpec, TransferSpec.parseConnectionSpec sourceSpec with
            | Error es, _ ->
                printErrors Console.Error es
                return 2
            | _, Error es ->
                printErrors Console.Error es
                return 2
            | Ok sinkRef, Ok sourceRef ->
                let sinkSub : Substrate = { Environment = parseEnvironment "migrate-sink" None; Role = SubstrateRole.Sink; ConnectionRef = sinkRef }
                let sourceSub : Substrate = { Environment = parseEnvironment "migrate-source" None; Role = SubstrateRole.Source; ConnectionRef = sourceRef }
                match! ConnectionResolver.openSubstrate sinkSub with
                | Error es ->
                    // NM-61 (extended) — connection axis → 6 (matches transfer + the
                    // in-flight gate); single-sourced through `refusalOf`.
                    printErrors Console.Error es
                    return (Preflight.refusalOf es).ExitCode
                | Ok sink ->
                    use sink = sink
                    match! ConnectionResolver.openSubstrate sourceSub with
                    | Error es ->
                        printErrors Console.Error es
                        return (Preflight.refusalOf es).ExitCode
                    | Ok dataSource ->
                        use dataSource = dataSource
                        let! readA = ReadSide.read sink
                        match readA with
                        | Error es -> return reportMigrationError (SchemaReadFailed es)
                        | Ok sinkSourceA ->
                            // Pre-flight the SOURCE (read) + SINK (write) before any mutation.
                            match! Preflight.connectionPreflight dataSource sink with
                            | Error es ->
                                // §5 connection gate. NM-61: HONOR `classify` — a
                                // connection refusal (`migrate.connectionUnavailable`)
                                // is its own axis (exit 6), not the permission/credential
                                // class (7). Single-sourced through `refusalOf` so the
                                // displayed and returned exits agree and match `transfer`.
                                let refusal = Preflight.refusalOf es
                                TtyRenderer.renderGate "projection migrate" refusal
                                return refusal.ExitCode
                            | Ok () ->
                                match MigrationRun.preview declaration sinkSourceA target with
                                | Error e -> return reportMigrationError e
                                | Ok artifacts ->
                                    match! migratePreflights "sink" sink (plannedWritesOf artifacts.SchemaStatements) with
                                    | Error code -> return code
                                    | Ok () ->
                                    // G7 — the rows loaded from the SOURCE must
                                    // satisfy any column the sink schema narrows to
                                    // NOT NULL. Probe the data source before any write.
                                    match! tighteningPreflight sinkSourceA target dataSource with
                                    | Error code -> return code
                                    | Ok effectiveTarget ->
                                      // Relax-once (if chosen) loosened the tightening:
                                      // the EFFECTIVE target (contract B) is what the
                                      // re-key resolves against and the load deploys.
                                      let target = effectiveTarget
                                      // AC-X2 — resolve the re-key map against
                                      // contract B (reuse the transfer verb's
                                      // resolver). Non-empty ⇒ executeWithData
                                      // runs the reconciling load whose AC-I5
                                      // pre-write gate composes first.
                                      match TransferSpec.resolveAllReconciliation target reconcileEntries userMapEntries with
                                      | Error es ->
                                          printErrors Console.Error es
                                          return 2
                                      | Ok reconciliation ->
                                        // #9 — the §5 gate prompt (above) ran on plain
                                        // stderr BEFORE the board; only the data-load
                                        // leg streams under the live board (a Spectre
                                        // Live region and an interactive prompt cannot
                                        // share the terminal).
                                        let executeBody () =
                                            (runMigrateWithDataLeg sinkCapability.IdentityPolicy atomic allowCdc declaration sinkSourceA target reconciliation storePath envLabel dataSource sink)
                                                .GetAwaiter().GetResult()
                                        return
                                            if Watch.shouldWatch prettyMode.Value then
                                                Watch.renderWatch Spines.migrateData (Watch.resolveDwellMs ()) executeBody
                                            else executeBody ()
        }
    let code = work.GetAwaiter().GetResult()
    dumpBench "migrate"
    code

// ----------------------------------------------------------------------
// THE_CLI.md — the four-verb operator surface. `Surface.parse` (Pipeline)
// turns argv into a typed `Intent`; these executors translate the `Intent`
// to the proven engine faces above (the run* functions), so exit codes and
// behavior are preserved by construction. `project` is the hero — every
// emission-family verb is one `MovementSpec` point; deploy / migrate / load /
// export collapse into it, distinguished only by the destination and the
// auto-read baseline A.
// ----------------------------------------------------------------------


/// project --to <live> with no --go — preview the minimal change against the
/// deployed state A, never writing (THE_CLI.md §5). Reads A live, previews
/// B ⊖ A, renders the plan via the shared migrate renderer.
let runProjectLivePreview (target: Catalog) (connSpec: string) (declaration: LossDeclaration) : int =
    let work =
        task {
            match TransferSpec.parseConnectionSpec connSpec with
            | Error es ->
                // A malformed connection SPEC is the parse axis (exit 2), matching
                // `transfer` (spec errors → 2) and `runMigrateExecute`. (The prior 6
                // conflated spec-parse with the connection-reach axis.)
                printErrors Console.Error es
                return 2
            | Ok connRef ->
                let sub : Substrate =
                    { Environment = parseEnvironment "preview" None; Role = SubstrateRole.Sink; ConnectionRef = connRef }
                match! ConnectionResolver.openSubstrate sub with
                | Error es ->
                    // NM-61 (extended) — the connection-reach axis exits 6, matching
                    // transfer + the migrate execute/with-data faces (single-sourced
                    // through `refusalOf`); the prior hardcoded 3 diverged.
                    printErrors Console.Error es
                    return (Preflight.refusalOf es).ExitCode
                | Ok cnn ->
                    use cnn = cnn
                    match! ReadSide.read cnn with
                    | Error es -> return reportMigrationError (SchemaReadFailed es)
                    | Ok sourceA ->
                        return reportPreviewOutcome
                            (sprintf "projection project -> %s  (preview; re-run with --go to apply)" connSpec)
                            (MigrationRun.preview declaration sourceA target)
        }
    work.GetAwaiter().GetResult()

/// Execute a planned `Intent` against the proven `run*` engine faces. Planning
/// is pure + totality-tested (`Command.plan`); this is the single effectful
/// seam — it voices every refusal through `Voice.errorSurface` to stderr
/// (fidelity #1 + #3) and surfaces the unhonored-axis notes (#2 — no silent drop).
/// `from: synthetic` — generate from the durable profile and load (S3 flow
/// front-end). `execute = false` previews (DryRun); `execute = true` writes,
/// gated by `PROJECTION_ALLOW_EXECUTE=1` (R6), and is fail-loud on dropped
/// rows (mirrors `runTransfer`).
let runSyntheticLoad (model: ModelSource) (modelOssys: string option) (profileRef: string) (connSpec: string) (opts: LoadOpts) (execute: bool) (modelSection: Config.ModelSection) (syntheticSection: Config.SyntheticSection) : int =
    let executeGated =
        if execute then System.Environment.GetEnvironmentVariable "PROJECTION_ALLOW_EXECUTE" = "1" else false
    if execute && not executeGated then
        TtyRenderer.renderVoicedError (ValidationError.create "gate.intent" "PROJECTION_ALLOW_EXECUTE is not set in the environment.")
        dumpBench "synthetic"
        7
    else
    let allowDrops = (opts.Declaration = DeclareAll)
    // The model file is the fallback; `modelOssys` (when set) is the live-OSSYS
    // primary. ModelResolution applies the policy.
    let modelFile =
        match model with
        | ModelSource.ModelFile p | ModelSource.ConfigFile p -> Some p
        | ModelSource.Unspecified -> None
    // §11 — the base SyntheticConfig + seed resolve from the declarative
    // `synthetic` config block (τ / preserve / synthesize / scale / seed), with the
    // per-run `--scale` / `--seed` CLI flags overriding it (config-primary). The
    // blessed `correction` is layered on top inside `SyntheticLoadRun.run`.
    let syntheticConfig = SyntheticLoadRun.resolveConfig syntheticSection opts.Scale
    let seed = SyntheticLoadRun.resolveSeed syntheticSection opts.Seed
    let result =
        (SyntheticLoadRun.run
            modelOssys modelFile profileRef opts.Correction connSpec opts.Emission opts.AllowCdc
            syntheticConfig seed executeGated modelSection)
            .GetAwaiter().GetResult()
    let exitCode =
        match result with
        | Ok report ->
            narrateTransferReport report
            let dropCode = Transfer.exitCodeForReport allowDrops report
            if dropCode <> 0 then
                Console.Error.WriteLine
                    (sprintf
                        "%d row(s) would be dropped — a relationship points to an unmatched record. Pass --allow-drops to accept the loss, or resolve the records."
                        (Transfer.droppedRowCount report))
            dropCode
        | Error errors ->
            printErrors Console.Error errors
            let anyCode (prefix: string) =
                errors |> List.exists (fun (e: ValidationError) -> e.Code.StartsWith prefix)
            if anyCode "synthetic.profileRef" || anyCode "model." then 2
            elif anyCode "synthetic.insufficientGrant" || anyCode "synthetic.grantProbeFailed" || anyCode "synthetic.cdcTrackedSink" then 7
            else 3
    dumpBench "synthetic"
    exitCode

/// `projection profile <env> --out <path>` — capture the durable Profile
/// artifact (THE_SYNTHETIC_DATA_DESIGN §2.2). Read-only (no execute gate);
/// reads the deployed catalog, profiles it, and writes the serialized form.
let runCaptureProfile (connSpec: string) (outPath: string) : int =
    let result = (ProfileCaptureRun.captureToFile connSpec outPath).GetAwaiter().GetResult()
    let exitCode =
        match result with
        | Ok () ->
            eprintfn "Profile written to %s." outPath
            0
        | Error errors ->
            printErrors Console.Error errors
            let anyCode (prefix: string) =
                errors |> List.exists (fun (e: ValidationError) -> e.Code.StartsWith prefix)
            if anyCode "profile.writeFailed" then 1
            elif anyCode "connectionSpec" || anyCode "connection" then 6
            else 3
    dumpBench "profile"
    exitCode

/// `projection synth-correct --out <path>` — propose a first-draft blessed
/// -correction artifact from the configured model's catalog (FUZZING §2.2,
/// slice F0c-I/O). Read-only (no execute gate); resolves the catalog (live
/// OSSYS primary, the model file fallback), proposes heuristic PII typing, and
/// writes the serialized form for the operator to review / edit / bless.
let runProposeCorrection (model: ModelSource) (modelOssys: string option) (outPath: string) : int =
    let modelFile =
        match model with
        | ModelSource.ModelFile p | ModelSource.ConfigFile p -> Some p
        | ModelSource.Unspecified -> None
    let result = (CorrectionProposeRun.proposeToFile modelOssys modelFile outPath).GetAwaiter().GetResult()
    let exitCode =
        match result with
        | Ok () ->
            eprintfn "Correction proposal written to %s." outPath
            0
        | Error errors ->
            printErrors Console.Error errors
            let anyCode (prefix: string) =
                errors |> List.exists (fun (e: ValidationError) -> e.Code.StartsWith prefix)
            if anyCode "correction.writeFailed" then 1
            elif anyCode "model." then 2
            else 3
    dumpBench "synth-correct"
    exitCode

/// `projection slice-extract --source <ref> --root <Entity> [--where <sql>]
/// --out <path>` — extract a use-case-scoped, referentially-closed data slice
/// from a live source and write the portable golden dataset (Slice 3). Read-
/// only against the source; the closure census prints to stderr, and a
/// dangling-mandatory-FK warning flags a slice that is not referentially self-
/// contained (the golden is still written — completeness is gated at apply).
let runSliceExtract (args: string list) : int =
    let arr = List.toArray args
    let flagValue (flag: string) : string option =
        arr
        |> Array.tryFindIndex ((=) flag)
        |> Option.bind (fun i -> if i + 1 < arr.Length then Some arr.[i + 1] else None)
    let usage = "usage: projection slice-extract --source <ref> (--slice <file> | --root <Entity> [--where <sql>]) --out <path>"
    let reportResult (out: string) (result: Result<(string * int) list * int>) : int =
        match result with
        | Ok (census, dangling) ->
            eprintfn "Slice golden written to %s." out
            for (entity, n) in census do eprintfn "  %s: %d row(s)" entity n
            if dangling > 0 then
                eprintfn
                    "  WARNING: %d dangling mandatory FK(s) — the slice is NOT referentially self-contained (gated at apply)."
                    dangling
            0
        | Error errors ->
            printErrors Console.Error errors
            let anyCode (prefix: string) =
                errors |> List.exists (fun (e: ValidationError) -> e.Code.StartsWith prefix)
            if anyCode "slice.writeFailed" then 1
            elif anyCode "slice.root" || anyCode "slice.spec" || anyCode "slice." then 2
            elif anyCode "connectionSpec" || anyCode "connection" then 6
            else 3
    match flagValue "--source", flagValue "--out" with
    | Some src, Some out ->
        // Config-driven (`--slice <file>`, the multi-root SliceSpec) takes
        // precedence; else the thin single-root `--root [--where]` form.
        let runner =
            match flagValue "--slice" with
            | Some sliceRef ->
                // A NAMED slice declared in projection.json (the config-primary
                // home) wins; else `--slice` is treated as a file path.
                let named =
                    match ProjectionConfig.fromFile "projection.json" with
                    | Ok cfg   -> Map.tryFind sliceRef cfg.Slices
                    | Error _  -> None
                match named with
                | Some spec -> Some (SliceExtractRun.extractSpec src spec out)
                | None      -> Some (SliceExtractRun.extractSpecFromFile src sliceRef out)
            | None ->
                match flagValue "--root" with
                | Some root -> Some (SliceExtractRun.extract src root (flagValue "--where") out)
                | None      -> None
        match runner with
        | Some t ->
            let code = reportResult out (t.GetAwaiter().GetResult())
            dumpBench "slice-extract"
            code
        | None -> eprintfn "%s" usage; 1
    | _ -> eprintfn "%s" usage; 1

/// `projection slice-apply --golden <p> --target <ref> --out <sql>` (additive
/// capture-and-remap MERGE) and `projection slice-reset … --delete-scope
/// COL=VAL[,...] --allow-drops` (the authoritative scoped DELETE, bounded to
/// the root predicate) — Slice 7. Emits the self-contained DML-only T-SQL
/// load/reset artifact from a golden against the target schema.
let runSliceApply (reset: bool) (args: string list) : int =
    let arr = List.toArray args
    let flagValue (flag: string) : string option =
        arr
        |> Array.tryFindIndex ((=) flag)
        |> Option.bind (fun i -> if i + 1 < arr.Length then Some arr.[i + 1] else None)
    let hasFlag (flag: string) : bool = Array.contains flag arr
    let exitForErrors (errors: ValidationError list) : int =
        printErrors Console.Error errors
        let anyCode (prefix: string) = errors |> List.exists (fun (e: ValidationError) -> e.Code.StartsWith prefix)
        if anyCode "slice.schemaParity" || anyCode "slice.golden" then 2
        elif anyCode "slice.writeFailed" || anyCode "slice.emitFailed" then 1
        elif anyCode "slice.apply.cdcTrackedSink" || anyCode "slice.apply.insufficientGrant" then 7
        elif anyCode "connection" || anyCode "slice.apply.grantProbeFailed" then 6
        else 3
    match flagValue "--golden", flagValue "--target" with
    | Some golden, Some target ->
        if reset && not (hasFlag "--allow-drops") then
            eprintfn "slice-reset performs a scoped DELETE on the target. Re-run with --allow-drops to acknowledge the loss."
            7
        elif (not reset) && hasFlag "--go" then
            // LIVE additive apply — Execute the capture-and-hoist write (the
            // golden lands in the target; no IDENTITY_INSERT).
            let result = (SliceApplyRun.applyLive target golden true (hasFlag "--allow-cdc")).GetAwaiter().GetResult()
            let code =
                match result with
                | Ok report ->
                    let skipped = List.length report.SkippedReferences
                    eprintfn "Slice applied to %s (live)." target
                    if skipped > 0 then
                        eprintfn "  WARNING: %d reference(s) skipped as unresolved orphans." skipped
                        9
                    else 0
                | Error errors -> exitForErrors errors
            dumpBench "slice-apply"
            code
        else
            // EMIT the self-contained T-SQL artifact (additive or reset) to --out.
            match flagValue "--out" with
            | None ->
                let resetFlags = if reset then "--delete-scope COL=VAL[,...] --allow-drops " else ""
                eprintfn
                    "usage: projection %s --golden <path> --target <ref> %s--out <path>%s"
                    (if reset then "slice-reset" else "slice-apply")
                    resetFlags
                    (if reset then "" else "   (or add --go to apply live)")
                1
            | Some out ->
                let deleteScope : DeleteScopePolicy option =
                    if not reset then None
                    else
                        let terms =
                            match flagValue "--delete-scope" with
                            | Some spec ->
                                spec.Split(',')
                                |> Array.toList
                                |> List.choose (fun kv ->
                                    match kv.Split('=') with
                                    | [| c; v |] -> Some ({ Column = c; Value = v } : DeleteScopeTerm)
                                    | _          -> None)
                            | None -> []
                        Some ({ Terms = terms } : DeleteScopePolicy)
                let result = (SliceApplyRun.applyToFile target golden deleteScope out).GetAwaiter().GetResult()
                let code =
                    match result with
                    | Ok n -> eprintfn "Slice %s artifact written to %s (%d row(s))." (if reset then "reset" else "load") out n; 0
                    | Error errors -> exitForErrors errors
                dumpBench (if reset then "slice-reset" else "slice-apply")
                code
    | _ ->
        eprintfn "usage: projection %s --golden <path> --target <ref> [--out <path> | --go]" (if reset then "slice-reset" else "slice-apply")
        1

/// `projection slice-run <name> [--go]` — run a named extract→apply slice flow
/// from projection.json's `sliceFlows` block (flow-binding). Extract the slice
/// from its source and apply it to its target in ONE command. `--go` lands the
/// rows; otherwise a live preview (extract + plan, no write).
let runSliceFlow (args: string list) : int =
    let arr = List.toArray args
    let hasFlag (flag: string) : bool = Array.contains flag arr
    match arr |> Array.tryFind (fun a -> not (a.StartsWith "-")) with
    | None -> eprintfn "usage: projection slice-run <name> [--go]"; 1
    | Some name ->
        match ProjectionConfig.fromFile "projection.json" with
        | Error es -> printErrors Console.Error es; 6
        | Ok cfg ->
            match Map.tryFind name cfg.SliceFlows with
            | None ->
                eprintfn "unknown slice flow '%s' — declare it in projection.json under \"sliceFlows\"." name
                2
            | Some sf ->
                match Map.tryFind sf.Slice cfg.Slices with
                | None ->
                    eprintfn "slice flow '%s' references unknown slice '%s' (add it to \"slices\")." name sf.Slice
                    2
                | Some spec ->
                    // A sliceFlow endpoint is an ENVIRONMENT NAME (resolved to its
                    // live conn — espace-safe, like flow.from / model.env) or a
                    // conn-ref (env:/file:/live:, passed through verbatim).
                    let resolveEndpoint (s: string) : Result<string> =
                        if s.Contains ":" then Result.success s
                        else
                            match Map.tryFind s cfg.Environments with
                            | Some env ->
                                match env.Access with
                                | Access.Direct r -> Result.success (Command.connSpecOf r)
                                | Access.Bundle (_, Some r) -> Result.success (Command.connSpecOf r)
                                | _ -> Result.failureOf (ValidationError.create "cli.sliceFlow.envNotLive" (sprintf "sliceFlow '%s' endpoint env '%s' has no live connection (use access:direct, or add a `conn` to the bundle environment)." name s))
                            | None -> Result.failureOf (ValidationError.create "cli.sliceFlow.endpointUnknown" (sprintf "sliceFlow '%s' endpoint '%s' is neither a known environment nor a conn-ref (env:/file:/live:)." name s))
                    match resolveEndpoint sf.Source, resolveEndpoint sf.Target with
                    | Error es, _ | _, Error es -> printErrors Console.Error es; dumpBench "slice-run"; 6
                    | Ok srcConn, Ok tgtConn ->
                    let execute = hasFlag "--go"
                    let result =
                        (SliceFlowRun.run srcConn spec tgtConn execute (hasFlag "--allow-cdc")).GetAwaiter().GetResult()
                    let code =
                        match result with
                        | Ok report ->
                            let skipped = List.length report.SkippedReferences
                            eprintfn
                                "Slice flow '%s' %s: %s → %s."
                                name (if execute then "applied" else "previewed") sf.Source sf.Target
                            if skipped > 0 then
                                eprintfn "  WARNING: %d reference(s) skipped as unresolved orphans." skipped
                                9
                            else 0
                        | Error errors ->
                            printErrors Console.Error errors
                            let anyCode (prefix: string) =
                                errors |> List.exists (fun (e: ValidationError) -> e.Code.StartsWith prefix)
                            if anyCode "slice.schemaParity" || anyCode "slice.root" then 2
                            elif anyCode "connection" || anyCode "slice.apply" then 6
                            else 3
                    dumpBench "slice-run"
                    code
