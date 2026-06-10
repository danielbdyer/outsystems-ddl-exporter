module Projection.Cli.Program

// LINT-ALLOW-FILE: CLI dispatcher operator-facing prose. Help/usage and terminal SQL-text at
//   the CLI boundary use string composition; the structural argument surface is the typed
//   MovementSpec / Intent (Projection.Pipeline). Terminal operator-facing text is the allowed exception.

open System
open System.Diagnostics
open System.IO
open Projection.Core
open Projection.Adapters.Sql
open Projection.Pipeline
open Projection.Targets.SSDT

/// Usage lines. Per chapter 3.5 deep audit (2026-05-09): the lines
/// are a typed `string list` carrying the structured help-page
/// content. Emission to the terminal is via per-line BCL
/// `TextWriter.WriteLine` rather than concatenation into a
/// multi-line string. The typed list IS the data; each line is
/// emitted independently; no intermediate concatenation.
let private usageLines : string list =
    [
        "projection — move a model from a source environment to a target (THE_CLI.md)."
        "  The daily act is `projection <flow>`: a flow is a named source→target recipe in"
        "  projection.json (environments + flows). Preview is the default; --go applies a live"
        "  write (and needs PROJECTION_ALLOW_EXECUTE=1). Conn refs are env:/file: only (D9)."
        ""
        "USAGE:"
        "    projection <flow> [--go] [--fresh] [--allow-drops] [--allow-cdc] [--resumable]   the daily surface"
        "    projection                                           list flows (name: from → to)"
        "    projection check  ( <source.sql> [--cdc-silence] | drift --model <m> --to <t>"
        "                      | data --before <t> --after <t> | ready )"
        "    projection diff <a> <b> [--format json] [--depth N]   change between two refs"
        "    projection explain ( diff <a> <b> [--format json] [--depth N] | policy <a> <b>"
        "                       | node <config> <ssKey> | suggest <config> [--apply <out>] | registry"
        "                       | migrate --to <b> ( --from <a> | --from empty | --store <s> ) [--allow-drops] )"
        "    projection seal ( --store <path> | approve <version> --approver <name> ... )"
        "    projection report <flow>        the on-prem migration-team change bundle"
        "    projection init                 scaffold a projection.json"
        "    projection setup [--conn <ref>] read back what is configured (history, writes, board);"
        "                                    --conn also probes a target (reachable + ALTER grant)"
        ""
        "FLOW — the hero. Move a model from `from` to `to`; the target decides the form."
        "  Environments (places) carry access (bundle → SSDT for Octopus | direct → live |"
        "  docker) and grant (schema+data | data — a refusal gate). Flows are named source→"
        "  target recipes (from/to/rekey/tables). A bundle target produces files (always"
        "  safe); a direct target previews until --go (which also needs"
        "  PROJECTION_ALLOW_EXECUTE=1, R6). --fresh wipes-and-loads (the rare from-scratch);"
        "  --allow-drops accepts declared loss; --allow-cdc overrides the CDC-tracked-sink"
        "  pre-flight gate; --resumable routes the data leg through the resumable upsert"
        "  envelope; a schema-from-model flow against a data-only target is refused."
        ""
        "CHECK — assert fidelity.  fidelity canary (default; --cdc-silence adds the redeploy"
        "  silence assertion) · drift (deployed vs model) · data (row/null counts) · ready"
        "  (the run-ledger readiness gauge; needs PROJECTION_LEDGER_DIR)."
        ""
        "EXPLAIN — understand before shipping.  diff (two refs) · policy (two configs) · node"
        "  (one node's transforms + findings) · suggest (ranked config edits) · migrate"
        "  (the dry-run plan: two-model or snapshot⊖snapshot)."
        ""
        "SEAL — provenance.  eject (the append-forever package; default) · approve (record a"
        "  policy-version decision)."
        ""
        "Every verb persists a bench snapshot to bench/<verb>/<utc-iso>.json; -v surfaces the"
        "table. --pretty / --json force the channel (default AUTO: a TTY gets the panel, a pipe"
        "gets NDJSON). --watch shows the live stage board on a folder-bundle flow run."
        ""
        "Exit codes:"
        "    0  succeeded"
        "    1  argv error (missing input / unknown flow or environment)"
        "    2  parse error (model JSON / spec / config-parse)"
        "    3  execution error (SQL rejected the change; connection open; unbreakable cycle)"
        "    4  Docker unavailable (a docker target; check fidelity)"
        "    5  fidelity divergence (check canary / check drift)"
        "    6  config error (file missing / unparseable / D9; connection-ref resolve)"
        "    7  gate refusal (--go without PROJECTION_ALLOW_EXECUTE=1; permission pre-flight)"
        "    8  data divergence (check data row / null)"
        "    9  refused, fail-loud (undeclared drop; inexpressible ALTER; tightening; verify-failed)"
    ]

/// Print each usage line directly to the writer via the BCL
/// `WriteLine` primitive. Per the data-structure-oriented
/// discipline: typed list flows in; per-line writes flow out; no
/// intermediate concatenation.
let private printLines (writer: TextWriter) (lines: string list) : unit =
    for line in lines do writer.WriteLine line

let private die (code: int) (message: string) : int =
    Console.Error.WriteLine message
    code

/// Render a `ValidationError list` to the writer as the **voiced** §10/§14
/// surface (`THE_VOICE.md`; `THE_CLI.md` §5) — a plain statement, the located
/// cause + code in the substantiation (never the code on the lead line), and the
/// next move. Delegates to `TtyRenderer.renderErrorsTo` (the `Voice.errorsSurface`
/// projection). The structured `config.validationFailed` / `transfer.*` NDJSON
/// stays the machine channel, unchanged — only the operator copy moves to the
/// register. (Replaces the prior `  [<code>] <message>` form, which led with the
/// code — a §10 violation.)
let private printErrors (writer: TextWriter) (errors: ValidationError list) : unit =
    TtyRenderer.renderErrorsTo writer errors

/// Print the bench table to stdout AND persist a JSON snapshot.
/// Called at the tail of every successful subcommand so the perf
/// surface is in the operator's attention.
/// Polish — `-v` / `--verbose` surfaces the per-label bench table (and other
/// depth); set in `main`. Default is calm: the bench snapshot persists for the
/// perf gate + rides the `runComplete` aggregates, but the table is opt-in.
let private verboseMode = ref false

let private dumpBench (tag: string) : unit =
    let stats = Bench.snapshot ()
    if not (List.isEmpty stats) then
        let path = BenchSink.defaultPath (Directory.GetCurrentDirectory()) tag
        try BenchSink.persistJson path tag stats
        with ex -> eprintfn "  WARNING: failed to persist bench snapshot: %s" ex.Message
        // Calm by default (REPORTING_HORIZON polish) — the table is depth,
        // shown only under -v. The snapshot is always persisted.
        if verboseMode.Value then
            printfn ""
            printfn "Bench (sorted by total time):"
            printfn "%s" (Bench.renderTable stats)
            printfn ""
            printfn "  bench snapshot: %s" path

/// Slice 4 (verb coverage) — bracket a verb body in the structured
/// LogSink run envelope so EVERY emitting verb (not just `full-export`)
/// produces a conforming NDJSON stream: a `config.runStart` first event
/// and the mandatory terminal `summary.runComplete` (§10), even when the
/// verb's middle is sparse. `full-export` is NOT wrapped here — it
/// self-brackets via `FullExportRun`. NDJSON goes to stderr (channel 1);
/// the verb's human narration stays on stdout (the §5 split).
/// Tier-3 — `--pretty` is a global channel-2 flag, stripped from argv in
/// `main` and recorded here. When set (and stderr is a real TTY), `withRun`
/// suppresses the NDJSON stream and renders a Spectre verdict panel instead.
let private prettyMode = ref false

/// Tier-3 — `--watch` opts into the live stage board (`Watch`, the §13 Watch
/// surface). Opt-in (default behavior unchanged), stripped from argv in `main`.
/// When set + a real TTY, `dispatchFullExport` runs the export under a live board
/// on stderr instead of the terminal-summary-only path.
let private watchMode = ref false

/// The publish pipeline's planned stage arc, in order — the keys it streams
/// (`extract.started` / `summary.stageCompleted{stage}`). The live Watch board
/// pre-seeds these as `Pending` so the whole arc shows from the first frame
/// (`THE_STORYBOARD.md` Appendix A.3).
let private pipelineStages : string list = [ "extract"; "profile"; "emit" ]

/// The in-place migrate leg's stage arc — build → apply → verify — that
/// `MigrationRun.execute` streams at its phase boundaries (Appendix A.3).
let private migrateStages : string list = [ "emit"; "deploy"; "canary" ]

/// The cross-substrate migrate's arc — the schema leg (build → apply → verify)
/// then the data leg's load (`Transfer.run` streams "load" with per-table
/// progress).
let private migrateDataStages : string list = [ "emit"; "deploy"; "canary"; "load" ]

let private withRun (command: string) (body: unit -> int) : int =
    LogSink.beginRun () |> ignore
    Bench.reset ()
    // Tier-3 channel 2 (§15.1) — when --pretty + a real TTY, the panel
    // REPLACES the NDJSON on stderr (never both on the same TTY); route
    // channel 1 to the null sink for this run.
    let pretty = TtyRenderer.shouldRender prettyMode.Value
    if pretty then LogSink.setWriter System.IO.TextWriter.Null
    LogSink.emit
        { LogSink.envelope LogSink.Info LogSink.Config "config.runStart"
            (Map.ofList [ "command", box command ]) with
            Phase = LogSink.Start }
    // §7.4 — every run publishes its classified transform inventory
    // (the registry that drives the pass chain), not just full-export.
    EventProjection.ofRegistry RegisteredAllTransforms.all |> List.iter LogSink.emit
    let code = body ()
    let outcome = if code = 0 then LogSink.Succeeded else LogSink.Failed
    LogSink.runComplete outcome command (Bench.snapshot ()) |> ignore
    // Tier-4 reporting — append this run to the cross-run ledger when one is
    // configured (`PROJECTION_LEDGER_DIR`), so the readiness gauge can read
    // the canary streak. Opt-in: default runs don't accumulate.
    (match RunLedger.configuredDir () with
     | Some dir ->
         let registered, applied, declined = LogSink.transformCounts ()
         try
             RunLedger.append dir
                 { RunId      = LogSink.runId ()
                   Ts         = System.DateTime.UtcNow.ToString("o")
                   Command    = command
                   Outcome    = (if code = 0 then "succeeded" else "failed")
                   Canary     = LogSink.canaryVerdict ()
                   Registered = registered
                   Applied    = applied
                   Declined   = declined }
         with ex -> eprintfn "  WARNING: failed to append ledger record: %s" ex.Message
     | None -> ())
    if pretty then TtyRenderer.renderSummary command code
    code

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
let private parseEnvironment (defaultLabel: string) (label: string option) : Projection.Core.Environment =
    match label with
    | None -> Projection.Core.Environment.Named defaultLabel
    | Some s ->
        match s.Trim().ToUpperInvariant() with
        | "DEV"  -> Projection.Core.Environment.Dev
        | "QA"   -> Projection.Core.Environment.Qa
        | "UAT"  -> Projection.Core.Environment.Uat
        | "PROD" -> Projection.Core.Environment.Prod
        | _      -> Projection.Core.Environment.Named (s.Trim())

let private runFullExport
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
            | Error _ ->
                // A malformed timeline name falls back to the genesis path rather
                // than aborting the emission; the store leg is simply absent.
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
let private runFullExportLoad
    (configPath: string)
    (connSpec: string)
    (outputOverride: string option)
    (storePath: string option)
    (envLabel: string option)
    : int =
    match TransferSpec.parseConnectionSpec connSpec with
    | Error es ->
        Console.Error.WriteLine "projection full-export --load: --conn argument error:"
        printErrors Console.Error es
        2
    | Ok connRef ->
        match ConnectionResolver.resolve "Sink" connRef with
        | Error es ->
            Console.Error.WriteLine "projection full-export --load: connection error:"
            printErrors Console.Error es
            6
        | Ok connStr ->
            match Config.fromFile configPath with
            | Error es ->
                Console.Error.WriteLine "projection full-export --load: config error:"
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
                    Console.Error.WriteLine "projection full-export --load: --env timeline name error:"
                    printErrors Console.Error es
                    2
                | Ok tl ->
                    let at = System.DateTimeOffset.UtcNow
                    let work =
                        task {
                            use sink = new Microsoft.Data.SqlClient.SqlConnection(connStr)
                            do! sink.OpenAsync()
                            return! Compose.runWithConfigAndLoad Deploy.cdcCaptureTotal Deploy.executeBatch cfg sink storePath tl env at
                        }
                    let code =
                        match work.GetAwaiter().GetResult() with
                        | Ok (report, legOpt, cdcDelta) ->
                            printfn "%d artifact(s) published; seed loaded (%d row(s) captured)."
                                report.Paths.Length cdcDelta
                            legOpt
                            |> Option.iter (fun leg ->
                                printfn "  this run recorded — episode %d on timeline %s."
                                    (EpisodicLifecycle.episodes leg.Chain |> List.length)
                                    (Timeline.name (EpisodicLifecycle.timeline leg.Chain)))
                            0
                        | Error es ->
                            printErrors Console.Error es
                            3
                    dumpBench "full-export"
                    code

let private runEmit (shaping: Config.Config) (catalog: Catalog) (outputDir: string) : int =
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
let private runEmitSkeletonOnly (catalog: Catalog) (outputDir: string) : int =
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

let private runDeploy (shaping: Config.Config) (catalog: Catalog) : int =
    if not (Deploy.Docker.isAvailable ()) then
        die
            4
            "projection: Docker daemon not reachable. Set DOCKER_HOST or start the daemon to run `deploy`."
    else
        let runBody () =
            printfn "projection: spinning up an ephemeral SQL Server container..."
            LogSink.recordStageStart "deploy"
            let sw = System.Diagnostics.Stopwatch.StartNew()
            let result = (Deploy.runFromCatalogWith shaping catalog).GetAwaiter().GetResult()
            LogSink.recordStageEvent "deploy" sw.ElapsedMilliseconds
                (match result with Ok (_, report) when report.Ok -> LogSink.Succeeded | _ -> LogSink.Failed)
            match result with
            | Ok (outputs, report) ->
                printfn
                    "projection: emitted %d SSDT bundle entries (JSON + distributions: typed JsonNode)"
                    (Map.count outputs.SsdtBundle)
                if report.Ok then
                    printfn
                        "projection: deploy succeeded — database `%s`, %d table(s) landed"
                        report.Database
                        report.TablesCreated
                    0
                else
                    // Per chapter 3.5 deep audit (2026-05-09): CLI
                    // error emission via per-line `Console.Error
                    // .WriteLine`. Typed list flows in; per-segment
                    // writes flow out; no concatenation. Header line
                    // composes via `Console.Error.Write` segments —
                    // each typed value (`report.Database`) emitted
                    // independently.
                    Console.Error.Write "projection: SQL Server rejected the SSDT in database `"
                    Console.Error.Write report.Database
                    Console.Error.WriteLine "`:"
                    for line in report.Errors do
                        Console.Error.Write "  "
                        Console.Error.WriteLine line
                    3
            | Error errors ->
                (
                    printErrors Console.Error errors
                    2
                )
        // --watch + a real TTY → a live deploy stage (§13). The schema deploy is
        // one aggregated batch (no per-table count to honestly report), so the
        // board shows the stage going Applying → Deploy complete, not a bar.
        let exitCode =
            if Watch.shouldWatch watchMode.Value then
                Watch.renderWatch [ "deploy" ] (Watch.resolveDwellMs ()) runBody
            else runBody ()
        dumpBench "deploy"
        exitCode

let private runCanary (sourceDdlPath: string) : int =
    if not (File.Exists sourceDdlPath) then
        die 1 (sprintf "projection: source DDL not found: %s" sourceDdlPath)
    elif not (Deploy.Docker.isAvailable ()) then
        die
            4
            "projection: Docker daemon not reachable. Set DOCKER_HOST or start the daemon to run `canary`."
    else
        let sourceDdl = File.ReadAllText sourceDdlPath
        printfn "projection: spinning up ephemeral SQL Server for the wide canary..."
        // E4 — the production canary deploys the canonical schema-then-data
        // form (DDL + StaticPopulationEmitter's InsertRow realization into the
        // fresh-empty target). Schema-only when the source carries no static
        // populations. See `Deploy.schemaWithStaticPopulation`.
        let stageTimer = System.Diagnostics.Stopwatch.StartNew()
        let task = Deploy.runWideCanary sourceDdl Deploy.schemaWithStaticPopulation
        let result = task.GetAwaiter().GetResult()
        stageTimer.Stop()
        // Tier-1 reporting — the canary stage feeds the §10 runComplete stages
        // table (the canary verb is single-stage; full-export records its own).
        LogSink.recordStage "canary" stageTimer.ElapsedMilliseconds
            (match result with
             | Ok r when PhysicalSchema.isEqual r.Diff -> LogSink.Succeeded
             | _ -> LogSink.Failed)
        let exitCode =
            match result with
            | Ok report ->
                printfn
                    "projection: source deployed %d table(s); target deployed %d table(s)"
                    report.SourceReport.TablesCreated
                    report.TargetReport.TablesCreated
                // Tier-1 reporting (§7.7) — emit the structured fidelity verdict
                // (canary.diffEmpty / canary.divergence) alongside the prose, so
                // CI can gate on it and the run ledger can record it.
                EventProjection.canaryEnvelopes report.TargetReport.TablesCreated report.Diff
                |> List.iter LogSink.emit
                if PhysicalSchema.isEqual report.Diff then
                    TtyRenderer.renderVoicedTo Console.Out "canary.diffEmpty"
                        (Map.ofList [ "tableCount", box report.TargetReport.TablesCreated ])
                    0
                else
                    TtyRenderer.renderVoicedTo Console.Error "canary.divergence"
                        (Map.ofList [ "renderedDiff", box (PhysicalSchema.renderDiff report.Diff) ])
                    5
            | Error errors ->
                (
                    printErrors Console.Error errors
                    2
                )
        dumpBench "canary"
        exitCode

/// AC-X8 — `projection canary <source.sql> --cdc-silence`. The wide canary
/// PLUS the protein P-9 assertion: after the source≈target round-trip, enable
/// CDC on the deployed target and measure an *idempotent redeploy*
/// (`MigrationRun.execute tgt tgt`, an empty differential) with the production
/// reader. Green iff the PhysicalSchema diff is empty AND the redeploy fired
/// ZERO CDC captures — the `V2_DRIVER.md` highest-leverage property surfaced by
/// the canary itself, not a harness.
let private runCanaryCdcSilence (sourceDdlPath: string) : int =
    if not (File.Exists sourceDdlPath) then
        die 1 (sprintf "projection: source DDL not found: %s" sourceDdlPath)
    elif not (Deploy.Docker.isAvailable ()) then
        die
            4
            "projection: Docker daemon not reachable. Set DOCKER_HOST or start the daemon to run `canary`."
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
        printfn "projection: spinning up ephemeral SQL Server for the CDC-silence canary..."
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
                printfn
                    "projection: source deployed %d table(s); target deployed %d table(s)"
                    report.SourceReport.TablesCreated
                    report.TargetReport.TablesCreated
                let schemaOk = PhysicalSchema.isEqual report.Diff
                if not schemaOk then
                    TtyRenderer.renderVoicedTo Console.Error "canary.divergence"
                        (Map.ofList [ "renderedDiff", box (PhysicalSchema.renderDiff report.Diff) ])
                if cdcDelta <> 0 then
                    eprintfn "The redeploy was not idempotent: %d row(s) captured where none were expected." cdcDelta
                if schemaOk && cdcDelta = 0 then
                    // §6 CDC-silence proof — the deepest fidelity finding, said plain.
                    printfn "Confirmed idempotent: zero rows captured, zero schema changes issued."
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
let private runApprove
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

let private dispositionName (d: IdentityDisposition) : string =
    match d with
    | IdentityDisposition.ReconciledByRule    -> "re-keyed by rule"
    | IdentityDisposition.AssignedBySink      -> "assigned by the target"
    | IdentityDisposition.PreservedFromSource -> "preserved from source"

let private narrateTransferReport (report: Transfer.TransferReport) : unit =
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
            (SsKey.rootOriginal k.Kind)
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
                (SsKey.rootOriginal u.Kind)
                (Name.value u.Column)
                (SsKey.rootOriginal u.Target)
    if not (List.isEmpty report.UnmatchedIdentities) then
        printfn ""
        printfn
            "%d identity(ies) unmatched — source records with no match in the target:"
            report.UnmatchedIdentities.Length
        for (k, s) in report.UnmatchedIdentities do
            printfn "  %s source '%s'" (SsKey.rootOriginal k) (SourceKey.value s)
    if not (List.isEmpty report.SkippedReferences) then
        printfn ""
        printfn
            "%d row(s) dropped — a relationship points to an unmatched record:"
            report.SkippedReferences.Length
        for (owner, r) in report.SkippedReferences do
            printfn
                "  %s.%s → %s (unmatched source '%s')"
                (SsKey.rootOriginal owner)
                (Name.value r.Column)
                (SsKey.rootOriginal r.Target)
                (SourceKey.value r.UnresolvedSource)

/// Parse an optional `--source-env` / `--sink-env` label into the
/// apparatus's `Environment`. The four named environments resolve
/// case-insensitively; anything else is a `Named` escape hatch; absence
/// keeps the default role-named label.
let private runTransfer
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
    let runBody () =
        let result =
            (Transfer.runThroughConnectionsResumable mode emission resumable allowCdc allowDrops tables connections resolveReconciliation)
                .GetAwaiter().GetResult()
        match result with
        | Ok report ->
            narrateTransferReport report
            // 6.A.1 — the drop-set is fail-loud, not exit-0. A successful
            // write that dropped FK-orphan rows or left reconciled-kind
            // sources unmatched surfaces a distinct non-zero exit unless the
            // operator declared the drops acceptable via --allow-drops.
            let dropCode = Transfer.exitCodeForReport allowDrops report
            if dropCode <> 0 then
                Console.Error.WriteLine
                    (sprintf
                        "%d row(s) would be dropped — a relationship points to an unmatched record. Pass --allow-drops to accept the loss, or resolve the records."
                        (Transfer.droppedRowCount report))
                let kindCount (label: string) (keys: SsKey seq) =
                    keys
                    |> Seq.countBy SsKey.rootOriginal
                    |> Seq.iter (fun (k, n) ->
                        Console.Error.WriteLine (sprintf "  %s %s: %d" label k n))
                kindCount "dropped in" (report.SkippedReferences |> List.map fst)
                kindCount "unmatched in" (report.UnmatchedIdentities |> List.map fst)
            dropCode
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
    // --watch + a real TTY → the live data-load board (§13); the transfer leg
    // streams the "load" stage with per-table progress. Only on a real --execute
    // (a dry-run writes no rows, so the load stage would never advance).
    let exitCode =
        if executeGated && Watch.shouldWatch watchMode.Value then
            Watch.renderWatch [ "load" ] (Watch.resolveDwellMs ()) runBody
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

let private narrateIntegrityReport (report: IntegrityReport) : unit =
    if DataIntegrityChecker.isClean report then
        printfn "Verified. The data matches across both deployments."
    else
        printfn "The data diverges between the two deployments. The differences are shown below."
        if not (List.isEmpty report.RowCountDeltas) then
            printfn ""
            printfn "Row counts (%d differ):" report.RowCountDeltas.Length
            for d in report.RowCountDeltas do
                printfn "  %-40s before=%d after=%d (change=%+d)"
                    (SsKey.rootOriginal d.Kind) d.Before d.After (d.After - d.Before)
        if not (List.isEmpty report.NullCountDeltas) then
            printfn ""
            printfn "Null counts (%d differ):" report.NullCountDeltas.Length
            for d in report.NullCountDeltas do
                printfn "  %-40s %-30s before=%d after=%d (change=%+d)"
                    (SsKey.rootOriginal d.Kind) (SsKey.rootOriginal d.Attribute)
                    d.Before d.After (d.After - d.Before)
        if not (List.isEmpty report.Warnings) then
            printfn ""
            printfn "Schema differences between the two deployments (%d):" report.Warnings.Length
            for w in report.Warnings do
                printfn "  %s  (%s)" w.Message w.Code

let private runVerifyData (beforeSpec: string) (afterSpec: string) : int =
    let collect = function Ok _ -> [] | Error es -> es
    let parsedBefore = TransferSpec.parseConnectionSpec beforeSpec
    let parsedAfter  = TransferSpec.parseConnectionSpec afterSpec
    let specErrors = collect parsedBefore @ collect parsedAfter
    if not (List.isEmpty specErrors) then
        Console.Error.WriteLine "projection verify-data: argument error:"
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
        Console.Error.WriteLine "projection verify-data: connection error:"
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
            | Ok contract -> return! DataIntegrityChecker.compare before after contract
        }
    let exitCode =
        match work.GetAwaiter().GetResult() with
        | Ok report ->
            narrateIntegrityReport report
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
let private runDrift (toPath: string) (connSpec: string) : int =
    let collect = function Ok _ -> [] | Error es -> es
    let parsedConn = TransferSpec.parseConnectionSpec connSpec
    if not (List.isEmpty (collect parsedConn)) then
        Console.Error.WriteLine "projection drift: --conn argument error:"
        printErrors Console.Error (collect parsedConn)
        dumpBench "drift"
        2
    else
    let connStrR = ConnectionResolver.resolve "Deployed" (Result.value parsedConn)
    if not (List.isEmpty (collect connStrR)) then
        Console.Error.WriteLine "projection drift: connection error:"
        printErrors Console.Error (collect connStrR)
        dumpBench "drift"
        6
    else
    let work =
        task {
            let! modelR = Compose.read toPath
            match modelR with
            | Error es ->
                Console.Error.WriteLine (sprintf "projection drift: could not read --to %s:" toPath)
                printErrors Console.Error es
                return 6
            | Ok model ->
                use cnn = new Microsoft.Data.SqlClient.SqlConnection(Result.value connStrR)
                do! cnn.OpenAsync()
                match! DriftRun.detect model cnn with
                | Error es ->
                    Console.Error.WriteLine "projection drift: could not read the deployed schema:"
                    printErrors Console.Error es
                    return 3
                | Ok diff ->
                    if PhysicalSchema.isEqual diff then
                        printfn "Verified. The deployed schema matches the model."
                        return 0
                    else
                        eprintfn "The deployed schema diverges from the model. The difference is shown below."
                        eprintfn "%s" (PhysicalSchema.renderDiff diff)
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
let private runEject (storePath: string) : int =
    match EjectRun.fromStore storePath with
    | Error msg ->
        Console.Error.WriteLine (sprintf "projection eject: %s" msg)
        2
    | Ok pkg ->
        printfn "projection eject: timeline %s — %d episode(s) preserved (append-forever), %d refactorlog reference(s)"
            (Timeline.name pkg.Timeline) (List.length pkg.Episodes) (List.length pkg.RefactorLogRefs)
        if EjectRun.isFaithful pkg then
            printfn "Verified. The reconstruction reproduces the frozen state from genesis to freeze."
            0
        else
            Console.Error.WriteLine "The package is not verified: the reconstruction does not reproduce the frozen state."
            5

/// Tier-4 reporting — `readiness`. Read the cross-run ledger
/// (`PROJECTION_LEDGER_DIR`) and report the R6 cutover gauge: how many
/// consecutive green canaries, and whether the gate is eligible. Human
/// gauge to stdout; one structured `summary.readiness` event to stderr so
/// CI can branch on it. Read-only (no ledger append for the query itself).
let private runReadiness () : int =
    match RunLedger.configuredDir () with
    | None ->
        eprintfn "projection: no run ledger configured. Set PROJECTION_LEDGER_DIR to accumulate run history."
        4
    | Some dir ->
        let records = RunLedger.read dir
        let r = RunLedger.readiness records
        let recent =
            records |> List.choose (fun e -> e.Canary) |> List.rev |> List.truncate 16 |> List.rev
        // Human channel — the themed cutover board (color on a TTY, plain piped).
        TtyRenderer.renderReadinessBoard r recent (RunLedger.ledgerPath dir)
        // Machine channel — one structured summary.readiness event (CI gates
        // on `eligible`).
        LogSink.beginRun () |> ignore
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
        0

/// Live-probe a target for the setup readback:
/// `(ref, reachable, grants)` where `grants` pairs each planned write action
/// (ALTER / INSERT / DELETE / CREATE TABLE) with its database-scope grant
/// status. Reuses the same machinery the migrate pre-flights use — resolve the
/// ref, open the connection (reachability), capture the grant evidence (the §14
/// / A.6 readback). D3 — the broader grants (INSERT / CREATE TABLE / DELETE) are
/// surfaced, not collapsed to ALTER alone, so the setup view names exactly which
/// writes a target permits. A failed resolve / open is `unreachable`, never a
/// stack trace; every grant reads false when the target is unreachable.
let private probeTarget (connRef: string) : string * bool * (Preflight.WriteAction * bool) list =
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
let private runSetup (connRef: string option) : int =
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

/// `diff <refA> <refB>` — change, rendered essence-first (INSTRUMENT slice 1,
/// the first surface of the instrument). Resolves both refs through `Ref`
/// (file / `@runId` / `json:` / `live:`) and renders the catalog change: the
/// plain verdict that leads, then the per-channel dig beneath. `--format json`
/// emits the same `View` as structure.
let private runDiff (refAText: string) (refBText: string) (asJson: bool) (depth: int) : int =
    let resolve (s: string) = (Ref.resolveCatalog (Ref.parse s)).GetAwaiter().GetResult()
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
            match Comparison.catalog.Between a b with
            | Error e ->
                Console.Error.WriteLine(sprintf "projection diff: %s" e)
                2
            | Ok d ->
                TtyRenderer.renderAnswer asJson depth (Comparison.renderCatalogChange d)
                0

/// P3 (REPORTING_HORIZON polish) — `explain <config> <ssKey>`. The drill-down
/// doorway: run the projection, then tell the full story for ONE node — every
/// transform that touched it (with the decision + rationale, rendered through
/// the SAME `EventProjection.transformKindRender` the event stream uses) and
/// every finding (with its suggested fix). "Every number is a doorway."
/// `ssKey` matches by exact root or substring, so `CustomerId` finds
/// `OSUSR_FOO.OrderHeader.CustomerId`.
let private runExplain (configPath: string) (ssKeyText: string) : int =
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
            printfn ""
            printfn "  explain  %s" ssKeyText
            printfn ""
            if List.isEmpty trail && List.isEmpty diags then
                printfn "  %s no transforms or findings matched" Theme.warn
                printfn "      %s try a fuller name, or a model that exercises this node" Theme.dot
                1
            else
                if not (List.isEmpty trail) then
                    printfn "  transforms"
                    for e in trail do
                        let tag, detail = EventProjection.transformKindRender e.TransformKind
                        match detail with
                        | Some d -> printfn "  %s %s %s %s %s %s" Theme.arrow e.PassName Theme.dot tag Theme.dot d
                        | None   -> printfn "  %s %s %s %s" Theme.arrow e.PassName Theme.dot tag
                    printfn ""
                if not (List.isEmpty diags) then
                    printfn "  findings"
                    for d in diags do
                        let g =
                            match d.Severity with
                            | DiagnosticSeverity.Error   -> Theme.bad
                            | DiagnosticSeverity.Warning -> Theme.warn
                            | _                          -> Theme.dot
                        printfn "  %s %s %s %s" g d.Code Theme.dot d.Message
                        match d.SuggestedConfig with
                        | Some c -> printfn "      %s fix: %s = %s" Theme.arrow c.Path c.Value
                        | None   -> ()
                    printfn ""
                0

/// P4 (REPORTING_HORIZON polish) — `suggest-config <config> [--apply <out>]`.
/// Run the projection, collect every actionable `SuggestedConfig` from the
/// diagnostic streams, merge by path (dedup), **rank by impact** (how many
/// nodes each edit touches), and present the to-do list highest-leverage
/// first. `--apply` writes the merged patch JSON. This is principle #5 made
/// concrete: don't just describe — recommend, ranked, and hand over the patch.
let private runSuggestConfig (configPath: string) (applyTo: string option) : int =
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
            let merged =
                (report.Diagnostics @ report.PassDiagnostics)
                |> List.choose (fun e -> e.SuggestedConfig |> Option.map (fun c -> c, e.SsKey))
                |> List.groupBy (fun (c, _) -> c.Path)
                |> List.map (fun (path, items) ->
                    let c0 = fst (List.head items)
                    let ssKeys =
                        items
                        |> List.choose (fun (_, k) -> k |> Option.map SsKey.rootOriginal)
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
let private runPolicyDiff (configAPath: string) (configBPath: string) : int =
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

/// A `MigrationError` as a plain located cause — the operator reads the finding,
/// never a raw DU dump (`THE_VOICE.md` §10). The full statement-first migrate
/// surfaces land in Waves 2–3; this Wave-0 translation only banishes the `%A`.
let private migrationErrorDetail (e: MigrationError) : string =
    match e with
    | DiffFailed _              -> "the changes could not be computed"
    | RefusedByViolations v     -> sprintf "%d removal(s) are not yet approved" (List.length v)
    | RefusedBySchemaErrors es  -> sprintf "%d change(s) cannot be expressed as a single ALTER" (List.length es)
    | EmitFailed _              -> "the changes could not be built"
    | SchemaReadFailed _        -> "the deployed schema could not be read"
    | ExecutionFailed msg       -> sprintf "the migration could not be applied — %s" msg
    | RefusedByTightening msg   -> sprintf "a column tightening would fail against existing data — %s" msg
    | VerificationFailed _      -> "the round-trip did not match the model"
    | DataTransferFailed _      -> "the data load did not complete"
    | RefusedByCdc t            -> sprintf "the schema change would run against a CDC-tracked database (%d table(s))" (List.length t)
    | StoreReadFailed msg       -> sprintf "the run history could not be read — %s" msg

/// The migrate preview as a §6/§9 minimality Surface — the smallest faithful
/// change, said plain (statement first, the per-move breakdown beneath), never
/// `norm=`. The schema change-manifest of δ: exactly the difference, and no more.
let private migratePreviewSurface (artifacts: MigrationArtifacts) : Surface.Surface =
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
let private renderUndeclaredDropGate (violations: SchemaLoss list) : unit =
    let tokens = violations |> List.map Migration.lossToken |> String.concat ", "
    let detail = sprintf "%d removal(s) await approval: %s" (List.length violations) tokens
    TtyRenderer.renderGate "projection migrate"
        (Preflight.refusalOf [ ValidationError.create "migrate.undeclaredDestructiveChange" detail ])

/// Shared dry-run renderer for a `migrate` preview outcome — the change-manifest
/// of δ, or a fail-loud refusal (undeclared losses / inexpressible change).
let private reportPreviewOutcome (header: string) (result: Result<MigrationArtifacts, MigrationError>) : int =
    let exitCode =
        match result with
        | Error (RefusedByViolations violations) ->
            renderUndeclaredDropGate violations
            9
        | Error (RefusedBySchemaErrors entries) ->
            Console.Error.WriteLine "projection migrate: these change(s) cannot be expressed as a single ALTER:"
            for e in entries do
                Console.Error.WriteLine (sprintf "    [%s] %s" e.Code e.Message)
            9
        | Error other ->
            Console.Error.WriteLine (sprintf "The migration did not complete: %s." (migrationErrorDetail other))
            2
        | Ok artifacts ->
            printfn "%s" header
            TtyRenderer.renderAnswer false View.defaultDepth (Surface.render (migratePreviewSurface artifacts))
            0
    dumpBench "migrate"
    exitCode

let private runMigratePreview (fromPath: string) (toPath: string) (declaration: LossDeclaration) : int =
    let loaded =
        task {
            let! a = Compose.read fromPath
            let! b = Compose.read toPath
            return a, b
        }
    let a, b = loaded.GetAwaiter().GetResult()
    match a, b with
    | Error errors, _ ->
        Console.Error.WriteLine (sprintf "projection migrate: could not read --from %s:" fromPath)
        printErrors Console.Error errors
        6
    | _, Error errors ->
        Console.Error.WriteLine (sprintf "projection migrate: could not read --to %s:" toPath)
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
let private runMigrateFromStore (storePath: string) (toPath: string) (declaration: LossDeclaration) (forceGenesis: bool) : int =
    let bRead = (Compose.read toPath).GetAwaiter().GetResult()
    match bRead with
    | Error errors ->
        Console.Error.WriteLine (sprintf "projection migrate: could not read --to %s:" toPath)
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
let private plannedWritesOf (stmts: Statement list) : Preflight.PlannedWrite list =
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
let private reportMigrationError (e: MigrationError) : int =
    match e with
    | RefusedByViolations violations ->
        renderUndeclaredDropGate violations
        9
    | RefusedBySchemaErrors entries ->
        Console.Error.WriteLine "projection migrate: these change(s) cannot be expressed:"
        for e in entries do Console.Error.WriteLine (sprintf "    [%s] %s" e.Code e.Message)
        9
    | RefusedByCdc tracked ->
        Console.Error.WriteLine (
            sprintf "projection migrate: schema DDL against a CDC-tracked database (%d table(s)). Pass --allow-cdc to proceed." (List.length tracked))
        9
    | RefusedByTightening msg ->
        Console.Error.WriteLine (
            sprintf "projection migrate: a column tightening (NULL → NOT NULL) would fail against existing NULL data; no DDL ran. %s" msg)
        9
    | SchemaReadFailed es ->
        Console.Error.WriteLine "The deployed schema could not be read."
        printErrors Console.Error es
        6
    | other ->
        Console.Error.WriteLine (sprintf "The migration did not complete: %s." (migrationErrorDetail other))
        2

/// The A1 connection + A2 permission pre-flights against a sink connection,
/// given the planned schema statements. Returns `Ok ()` to proceed or a printed
/// refusal + exit code. A2's grant capture is database-scope (survey-gated
/// object-scope refinement, OPEN-2 / P1).
let private migratePreflights (label: string) (cnn: Microsoft.Data.SqlClient.SqlConnection) (planned: Preflight.PlannedWrite list) : System.Threading.Tasks.Task<Result<unit, int>> =
    // Each pre-flight refusal renders through the §5 Gate surface — the
    // consequence as meaning + the one plain imperative — never a raw header +
    // dump. The exit is pinned to the migrate verb's historical code (7, the
    // connection/permission/credential class), independent of `classify`'s axis
    // code, so the displayed exit matches the returned one.
    let refuse (es: ValidationError list) : Result<unit, int> =
        TtyRenderer.renderGate "projection migrate" { Preflight.refusalOf es with ExitCode = 7 }
        Error 7
    task {
        match! Preflight.connectionPreflight cnn cnn with
        | Error es -> return refuse es
        | Ok () ->
            match! Preflight.captureGrantEvidence cnn with
            | Error es -> return refuse es
            | Ok grant ->
                match Preflight.permissionPreflight grant planned with
                | Error es -> return refuse es
                | Ok () -> return Ok ()
    }

/// G7 — the Decision↔Data tightening pre-flight, wired into the migrate verbs.
/// Derives the narrowing-to-NOT-NULL overlay from the A→B displacement and, when
/// it is NON-EMPTY, probes the live data source's null counts via
/// `Preflight.tighteningPreflight`, refusing (exit 9 / migrate.dataViolatesTightening)
/// before any write when a tightened column carries NULLs. When the overlay is
/// empty (a non-tightening migration) the probe is SKIPPED entirely — the
/// self-probing pre-flight surveys every kind, a cost a non-tightening migration
/// must not pay. `dataCnn` is the connection whose data is at risk: the in-place
/// `cnn` for MC (state A lives in the sink), the SOURCE for MX.
let private tighteningPreflight
    (sourceA: Catalog)
    (target: Catalog)
    (dataCnn: Microsoft.Data.SqlClient.SqlConnection)
    : System.Threading.Tasks.Task<Result<unit, int>> =
    task {
        let overlay = Preflight.tighteningOverlay sourceA target
        if Set.isEmpty overlay.EnforceNotNull then return Ok ()
        else
            match! Preflight.tighteningPreflight dataCnn sourceA overlay with
            | Ok () -> return Ok ()
            | Error es ->
                // §5 Data-compat gate — the existing data violates the tightening.
                TtyRenderer.renderGate "projection migrate" (Preflight.refusalOf es)
                return Error 9
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
let private runMigrateExecute (target: Catalog) (connSpec: string) (declaration: LossDeclaration) (allowCdc: bool) (storePath: string option) (envLabel: string option) : int =
    if System.Environment.GetEnvironmentVariable "PROJECTION_ALLOW_EXECUTE" <> "1" then
        TtyRenderer.renderVoicedError (ValidationError.create "gate.intent" "PROJECTION_ALLOW_EXECUTE is not set in the environment.")
        dumpBench "migrate"
        7
    else
    let work =
        task {
            match TransferSpec.parseConnectionSpec connSpec with
            | Error es ->
                Console.Error.WriteLine "projection migrate: --conn argument error:"
                printErrors Console.Error es
                return 2
            | Ok connRef ->
                let sub : Substrate =
                    { Environment = parseEnvironment "migrate-sink" None; Role = SubstrateRole.Sink; ConnectionRef = connRef }
                match! ConnectionResolver.openSubstrate sub with
                | Error es ->
                    Console.Error.WriteLine "projection migrate: could not open --conn:"
                    printErrors Console.Error es
                    return 3
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
                                | Ok () ->
                                    match storePath with
                                    | Some store ->
                                        // AC-P8 — durable provenance. After a verified
                                        // execute, persist the episode onto the timeline
                                        // so the next sprint's diff loads it as the prior
                                        // (the emission→snapshot→diff loop closes here).
                                        let env = parseEnvironment "DEV" envLabel
                                        let timeline = Timeline.create (Projection.Core.Environment.name env)
                                        match timeline with
                                        | Error es ->
                                            Console.Error.WriteLine "projection migrate: --lifecycle-store timeline name error:"
                                            printErrors Console.Error es
                                            return 2
                                        | Ok tl ->
                                            let at = System.DateTimeOffset.UtcNow
                                            let! recorded =
                                                MigrationRun.executeAndRecord
                                                    allowCdc declaration sourceA target store tl env at None cnn
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
                                        // X4 — measure CDC-silence, don't just
                                        // assert no DDL ran. `executeAndMeasureCdc`
                                        // brackets the execute with the change-
                                        // measure ‖·‖; an idempotent redeploy
                                        // surfaces zero statements AND zero captures.
                                        let! outcome = MigrationRun.executeAndMeasureCdc allowCdc declaration sourceA target cnn
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
    let runBody () = work.GetAwaiter().GetResult()
    // --watch + a real TTY → the live stage board (§13), pre-seeded with the
    // migrate leg's arc (build → apply → verify) the executor streams.
    let code =
        if Watch.shouldWatch watchMode.Value then
            Watch.renderWatch migrateStages (Watch.resolveDwellMs ()) runBody
        else runBody ()
    dumpBench "migrate"
    code

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
let private runMigrateWithData (target: Catalog) (sinkSpec: string) (sourceSpec: string) (reconcileSpecs: string list) (userMapPath: string option) (declaration: LossDeclaration) (allowCdc: bool) (storePath: string option) (envLabel: string option) : int =
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
        Console.Error.WriteLine "projection migrate: --reconcile / --user-map argument error:"
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
                Console.Error.WriteLine "projection migrate: --sink-conn argument error:"
                printErrors Console.Error es
                return 2
            | _, Error es ->
                Console.Error.WriteLine "projection migrate: --source-conn argument error:"
                printErrors Console.Error es
                return 2
            | Ok sinkRef, Ok sourceRef ->
                let sinkSub : Substrate = { Environment = parseEnvironment "migrate-sink" None; Role = SubstrateRole.Sink; ConnectionRef = sinkRef }
                let sourceSub : Substrate = { Environment = parseEnvironment "migrate-source" None; Role = SubstrateRole.Source; ConnectionRef = sourceRef }
                match! ConnectionResolver.openSubstrate sinkSub with
                | Error es ->
                    Console.Error.WriteLine "projection migrate: could not open --sink-conn:"
                    printErrors Console.Error es
                    return 3
                | Ok sink ->
                    use sink = sink
                    match! ConnectionResolver.openSubstrate sourceSub with
                    | Error es ->
                        Console.Error.WriteLine "projection migrate: could not open --source-conn:"
                        printErrors Console.Error es
                        return 3
                    | Ok dataSource ->
                        use dataSource = dataSource
                        let! readA = ReadSide.read sink
                        match readA with
                        | Error es -> return reportMigrationError (SchemaReadFailed es)
                        | Ok sinkSourceA ->
                            // Pre-flight the SOURCE (read) + SINK (write) before any mutation.
                            match! Preflight.connectionPreflight dataSource sink with
                            | Error es ->
                                // §5 connection gate; the migrate verb's connection
                                // refusal exits 7 (the credential class), pinned here.
                                TtyRenderer.renderGate "projection migrate" { Preflight.refusalOf es with ExitCode = 7 }
                                return 7
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
                                    | Ok () ->
                                      // AC-X2 — resolve the re-key map against
                                      // contract B (reuse the transfer verb's
                                      // resolver). Non-empty ⇒ executeWithData
                                      // runs the reconciling load whose AC-I5
                                      // pre-write gate composes first.
                                      match TransferSpec.resolveAllReconciliation target reconcileEntries userMapEntries with
                                      | Error es ->
                                          Console.Error.WriteLine "projection migrate: --reconcile / --user-map could not be resolved against contract B:"
                                          printErrors Console.Error es
                                          return 2
                                      | Ok reconciliation ->
                                        match storePath with
                                        | Some store ->
                                            // X5 — measure the data movement (CDC ‖·‖)
                                            // and RECORD the episode durably. A
                                            // CDC-tracked sink confounds the schema
                                            // Verified flag, so the record gates on
                                            // schema-applied + transfer-OK and carries
                                            // the measured capture count.
                                            let env = parseEnvironment "DEV" envLabel
                                            match Timeline.create (Projection.Core.Environment.name env) with
                                            | Error es ->
                                                Console.Error.WriteLine "projection migrate: --lifecycle-store timeline name error:"
                                                printErrors Console.Error es
                                                return 2
                                            | Ok tl ->
                                                let at = System.DateTimeOffset.UtcNow
                                                let! recorded =
                                                    MigrationRun.executeWithDataAndRecord declaration Transfer.Execute allowCdc
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
                                            MigrationRun.executeWithData declaration Transfer.Execute allowCdc
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
    let runBody () = work.GetAwaiter().GetResult()
    // --watch + a real TTY → the live board (§13) across the whole arc: the
    // schema leg (build → apply → verify) and the data leg's load.
    let code =
        if Watch.shouldWatch watchMode.Value then
            Watch.renderWatch migrateDataStages (Watch.resolveDwellMs ()) runBody
        else runBody ()
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
let private runProjectLivePreview (target: Catalog) (connSpec: string) (declaration: LossDeclaration) : int =
    let work =
        task {
            match TransferSpec.parseConnectionSpec connSpec with
            | Error es ->
                Console.Error.WriteLine "projection project: connection reference error:"
                printErrors Console.Error es
                return 6
            | Ok connRef ->
                let sub : Substrate =
                    { Environment = parseEnvironment "preview" None; Role = SubstrateRole.Sink; ConnectionRef = connRef }
                match! ConnectionResolver.openSubstrate sub with
                | Error es ->
                    Console.Error.WriteLine "projection project: could not open the destination:"
                    printErrors Console.Error es
                    return 3
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
let private runSyntheticLoad (model: ModelSource) (modelOssys: string option) (profileRef: string) (connSpec: string) (opts: LoadOpts) (execute: bool) : int =
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
    let result =
        (SyntheticLoadRun.run
            modelOssys modelFile profileRef connSpec opts.Emission opts.AllowCdc
            SyntheticConfig.defaultConfig SyntheticLoadRun.defaultSeed executeGated)
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
let private runCaptureProfile (connSpec: string) (outPath: string) : int =
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

let private runPlan (shaping: Config.Config) (surveyAdvisory: string list) (plan: ExecutionPlan) : int =
    for n in plan.Notes do eprintfn "Note — %s" n
    // Resolve the model to a Catalog under the live-OSSYS-primary / file-
    // fallback policy (ModelResolution), then run the Catalog-accepting face.
    //
    // THE_CONFIG_CONTROL_PLANE §6 (S3) — the SINGLE shared module-filter seam.
    // Every model-bearing flow arm (emit / deploy / preview / migrate) routes
    // the resolved catalog through `Compose.applyModuleFilter` HERE so a
    // `model.modules` scope narrows the bundle and the live/docker/migrate
    // catalogs identically (the riskiest-seam callout). An empty `model.modules`
    // is the all-permissive identity, so the default flow stays byte-identical.
    let needCatalog (modelOssys: string option) (model: ModelSource) (run: Catalog -> int) : int =
        let modelFile =
            match model with
            | ModelSource.ModelFile p | ModelSource.ConfigFile p -> Some p
            | ModelSource.Unspecified -> None
        let resolved =
            (ModelResolution.resolveCatalog modelOssys modelFile).GetAwaiter().GetResult()
            |> Result.bind (Compose.applyModuleFilter shaping)
        match resolved with
        | Ok catalog -> run catalog
        | Error es ->
            for e in es do TtyRenderer.renderVoicedError e
            6
    // THE_CONFIG_CONTROL_PLANE §6 (S3) — apply the shaping catalog overlays
    // (renames + policy tightening) to a module-filtered catalog before the
    // non-bundle destinations (preview / migrate / migrate-with-data) evolve
    // the sink schema toward it. Default shaping is the identity on the catalog.
    let withShaped (shaping: Config.Config) (catalog: Catalog) (run: Catalog -> int) : int =
        match Compose.applyShapingToCatalog shaping catalog with
        | Ok shapedCatalog -> run shapedCatalog
        | Error es ->
            for e in es do TtyRenderer.renderVoicedError e
            6
    match plan.Action with
    // project ------------------------------------------------------------
    | PlanAction.PublishBundle (c, dir, store, env) ->
        let verbosity = if verboseMode.Value then LogSink.Verbosity.Verbose else LogSink.Verbosity.Quiet
        let run () = runFullExport c (Some dir) verbosity Set.empty store env
        // --watch + a real TTY → the live stage board (§13), pre-seeded with the
        // pipeline's planned stages so the whole arc is visible from the first frame.
        if Watch.shouldWatch watchMode.Value then Watch.renderWatch pipelineStages (Watch.resolveDwellMs ()) run
        else run ()
    | PlanAction.EmitSkeleton (model, modelOssys, dir) ->
        needCatalog modelOssys model (fun cat -> withRun "projection project" (fun () -> runEmitSkeletonOnly cat dir))
    | PlanAction.EmitBundle (model, modelOssys, dir) ->
        needCatalog modelOssys model (fun cat -> withRun "projection project" (fun () -> runEmit shaping cat dir))
    | PlanAction.DeployDocker (model, modelOssys) ->
        needCatalog modelOssys model (fun cat -> withRun "projection project" (fun () -> runDeploy shaping cat))
    | PlanAction.PreviewSchema (model, modelOssys, conn, decl) ->
        needCatalog modelOssys model (fun cat -> withShaped shaping cat (fun shapedCat -> runProjectLivePreview shapedCat conn decl))
    | PlanAction.Transfer (src, sink, opts, execute) ->
        runTransfer src sink None None opts.Reconcile opts.Rekey execute opts.AllowCdc (opts.Declaration = DeclareAll) opts.Emission opts.Resumable opts.Tables surveyAdvisory
    | PlanAction.RunReverseLeg (_src, _sink, _opts, _execute) ->
        // G2 — the engine ROUTED the B→A legacy reverse leg distinctly (not as an
        // A→A peer transfer). The reverse-leg RUNNER (`Transfer.runReverseLeg` /
        // the M3.b face) needs two SsKey-ALIGNED contracts — the logical source and
        // physical sink RENDERED from the ONE authored model — which a live two-DB
        // flow cannot produce yet (the J3 residual; THE_DATA_PRODUCERS §6 LE-1).
        // Reading the two live DBs independently would NOT align the SsKeys. So we
        // surface the gap as a NAMED REFUSAL — not a crash, and never a silent
        // mis-run as a peer transfer. (When a clean contract source exists, this
        // arm runs it; until then, the honest boundary is the refusal.)
        TtyRenderer.renderVoicedError
            (ValidationError.create "cli.move.reverseLegResidual"
                "the legacy B→A reverse leg needs SsKey-aligned contracts (the one model rendered logical-source + physical-sink); a live two-DB flow cannot produce them yet — see J3 / THE_DATA_PRODUCERS §6 LE-1.")
        6
    | PlanAction.MigrateWithData (model, modelOssys, sink, src, opts) ->
        needCatalog modelOssys model (fun cat -> withShaped shaping cat (fun shapedCat -> runMigrateWithData shapedCat sink src opts.Reconcile opts.Rekey opts.Declaration opts.AllowCdc opts.Store opts.Env))
    | PlanAction.SynthesizeAndLoad (model, modelOssys, profile, conn, opts, execute) ->
        runSyntheticLoad model modelOssys profile conn opts execute
    | PlanAction.CaptureProfile (conn, out) -> runCaptureProfile conn out
    | PlanAction.PublishAndLoad (c, conn, store, env) ->
        let run () = runFullExportLoad c conn None store env
        // The load flow runs the same publish pipeline, so it streams the same
        // stage arc; --watch shows the live board (§13).
        if Watch.shouldWatch watchMode.Value then Watch.renderWatch pipelineStages (Watch.resolveDwellMs ()) run
        else run ()
    | PlanAction.Migrate (model, modelOssys, conn, opts) ->
        needCatalog modelOssys model (fun cat -> withShaped shaping cat (fun shapedCat -> runMigrateExecute shapedCat conn opts.Declaration opts.AllowCdc opts.Store opts.Env))
    // check --------------------------------------------------------------
    | PlanAction.CheckCanary (ddl, false) -> withRun "projection check" (fun () -> runCanary ddl)
    | PlanAction.CheckCanary (ddl, true)  -> withRun "projection check --cdc-silence" (fun () -> runCanaryCdcSilence ddl)
    | PlanAction.CheckDrift (m, conn)      -> runDrift m conn
    | PlanAction.CheckData (before, after) -> runVerifyData before after
    | PlanAction.CheckReady                -> runReadiness ()
    // explain ------------------------------------------------------------
    | PlanAction.ExplainDiff (a, b, asJson, depthOpt) -> runDiff a b asJson (defaultArg depthOpt View.defaultDepth)
    | PlanAction.ExplainPolicy (a, b)        -> runPolicyDiff a b
    | PlanAction.ExplainNode (c, k)          -> runExplain c k
    | PlanAction.ExplainSuggest (c, applyTo) -> runSuggestConfig c applyTo
    | PlanAction.ExplainRegistry ->
        // Self-description (NORTH_STAR "self-describing" leg) — the engine names
        // its own registered transforms (the `registered ⇔ executed` registry).
        let all = RegisteredAllTransforms.all
        let stageBindingText (s: StageBinding) =
            match s with
            | StageBinding.Adapter        -> "adapter"
            | StageBinding.Pass           -> "pass"
            | StageBinding.OrderingPolicy -> "ordering"
            | StageBinding.Emitter        -> "emitter"
            | StageBinding.Pipeline       -> "pipeline"
        printfn "projection: %d registered transform(s)" (List.length all)
        for rt in all |> List.sortBy (fun r -> stageBindingText r.StageBinding, r.Name) do
            printfn "  %-12s %s" (stageBindingText rt.StageBinding) rt.Name
        0
    | PlanAction.ExplainMigratePreview (fromP, toP, decl)   -> runMigratePreview fromP toP decl
    | PlanAction.ExplainMigrateFromStore (store, toP, decl, forceGenesis) -> runMigrateFromStore store toP decl forceGenesis
    // seal ---------------------------------------------------------------
    | PlanAction.SealEject store -> runEject store
    | PlanAction.SealApprove (version, approver, rationale, store) -> runApprove version approver rationale store
    // report -------------------------------------------------------------
    | PlanAction.ReportBundle store ->
        match ReportRun.fromStore store with
        | Ok bundle ->
            printLines Console.Out (ReportRun.render bundle)
            0
        | Error msg ->
            Console.Error.WriteLine (sprintf "projection report: %s" msg)
            6
    // refused ------------------------------------------------------------
    | PlanAction.Refused (exit, error) -> TtyRenderer.renderVoicedError error; exit

/// `projection init` — scaffold a `projection.json` so the operator starts from
/// a working surface (first-run ergonomics). Refuses to overwrite an existing
/// file (look-before-overwrite); the conn is a `env:`/`file:` reference (D9).
let private runInit () : int =
    let path = "projection.json"
    if File.Exists path then
        Console.Error.WriteLine (sprintf "projection init: %s already exists; not overwriting." path)
        1
    else
        // LINT-ALLOW: terminal operator-facing scaffold text at the CLI boundary.
        // The shape MUST match `ProjectionConfig.parse` (MovementSurface.fs):
        // `environments` (access bundle|direct|docker; grant; conn is env:/file:)
        // and `flows` (from/to). The MODEL is read LIVE from a cloud OutSystems
        // environment via `modelOssys` (the primary; `model` is the file fallback,
        // ModelResolution.chooseOrigin). A SOURCE-only env carries no grant; only a
        // SINK does — the cloud-insertion sink is `data` (DML-only, R6). The prior
        // `targets` block was removed at slice F5; the parser ignores unknown keys.
        let scaffold =
            "{\n" +
            "  \"modelOssys\": \"file:./secrets/ossys.conn\",\n" +
            "  \"environments\": {\n" +
            "    \"local\":      { \"access\": \"docker\" },\n" +
            "    \"onprem-dev\": { \"access\": \"bundle\", \"out\": \"./dist/onprem-dev\", \"grant\": \"schema+data\", \"rendition\": \"logical\" }\n" +
            "  },\n" +
            "  \"flows\": {\n" +
            "    \"try\":     { \"from\": \"model\", \"to\": \"local\" },\n" +
            "    \"publish\": { \"from\": \"model\", \"to\": \"onprem-dev\" }\n" +
            "  }\n" +
            "}\n"
        File.WriteAllText(path, scaffold)
        printfn "projection init: wrote %s." path
        printfn "  Next: put your cloud OutSystems connection string in ./secrets/ossys.conn — the"
        printfn "        model is read LIVE from it (the file's contents ARE the connection string; D9,"
        printfn "        gitignored, never committed). The engine reads the file directly — no shell"
        printfn "        export. Then `projection` lists the flows; `projection try` previews into a"
        printfn "        throwaway Docker database; `projection publish` emits the on-prem SSDT bundle."
        printfn "        For the cloud-insertion flows (golden / preview / synth into a data-only cloud"
        printfn "        sink) see examples/projection.sample.json. A live write needs both --go and"
        printfn "        PROJECTION_ALLOW_EXECUTE=1."
        0

/// Discover `projection.json` (or `PROJECTION_CONFIG`) — absent is the empty
/// config (aliasing is opt-in).
let private discoverConfig () : Result<ProjectionConfig> =
    let path =
        match System.Environment.GetEnvironmentVariable "PROJECTION_CONFIG" with
        | null | "" -> "projection.json"
        | p -> p
    ProjectionConfig.fromFile path

/// `projection survey` — the capability survey (prototype;
/// `HANDOFF_CAPABILITY_SURVEY_2026_06_09.md`). Probe every configured
/// environment in parallel and render the declared-vs-actual capability matrix:
/// is every place actually able to do what the pipeline asks of it?
let private runSurvey () : int =
    match discoverConfig () with
    | Error es ->
        for e in es do TtyRenderer.renderVoicedError e
        6
    | Ok cfg ->
        let reports = (CapabilitySurvey.survey cfg).GetAwaiter().GetResult()
        TtyRenderer.renderAnswer false View.defaultDepth (TtyRenderer.buildSurveyView reports)
        // CI gate: non-zero when a connected environment can't do what is asked.
        // The standalone verb HARD-STOPS (exit 7); the in-flow advisory (G0c)
        // reads the SAME `CapabilitySurvey.blocked` predicate but only warns.
        if reports |> List.exists CapabilitySurvey.blocked then 7 else 0

/// A flow's content origin, rendered for the menu (THE_CLI.md §4.4).
let private flowSourceText (s: FlowSource) : string =
    match s with
    | FlowSource.Env e           -> e
    | FlowSource.Model           -> "model"
    | FlowSource.Synthetic None  -> "synthetic"
    | FlowSource.Synthetic (Some p) -> sprintf "synthetic(%s)" p
    | FlowSource.NoData          -> "none"

/// `projection` with no args lists the flows as `name: from → to (spec)` —
/// the config IS the menu (THE_CLI.md §4.4). No flows configured → the help.
let private runList () : int =
    match discoverConfig () with
    | Error es ->
        Console.Error.WriteLine "projection: projection.json is invalid:"
        printErrors Console.Error es
        6
    | Ok cfg ->
        if Map.isEmpty cfg.Flows then printLines Console.Out usageLines
        else
            Console.Out.WriteLine "Flows (projection <flow> [--go] [--fresh] [--allow-drops]):"
            for KeyValue (name, f) in cfg.Flows do
                let extra =
                    [ if Option.isSome f.Rekey then yield "rekey"
                      if not (List.isEmpty f.Tables) then yield sprintf "tables: %s" (String.concat "," f.Tables) ]
                let suffix = if List.isEmpty extra then "" else sprintf "  (%s)" (String.concat "; " extra)
                Console.Out.WriteLine(sprintf "  %-16s %s → %s%s" name (flowSourceText f.From) f.To suffix)
        0

[<EntryPoint>]
let main argv =
    // Polish (REPORTING_HORIZON) — global flags, parsed + stripped before
    // verb dispatch so per-verb argv shapes are unchanged.
    //   --pretty / --json / --no-pretty : force the channel; default AUTO
    //     (a real TTY gets the Spectre panel, a pipe gets clean NDJSON — the
    //     operator never thinks about format).
    //   -v / --verbose : surface depth (the bench table, etc.).
    let has flag = Array.contains flag argv
    verboseMode := has "-v" || has "--verbose"
    watchMode := has "--watch"
    let forceJson = has "--json" || has "--no-pretty"
    let forcePretty = has "--pretty"
    // "operator wants pretty" — explicit, or auto when stderr is a real TTY
    // and NDJSON wasn't forced. `TtyRenderer.shouldRender` re-checks the TTY
    // so a forced --pretty into a pipe still won't spray ANSI.
    prettyMode := forcePretty || (not forceJson && not Console.IsErrorRedirected)
    let globalFlags = set [ "--pretty"; "--json"; "--no-pretty"; "-v"; "--verbose"; "--watch" ]
    let argv = argv |> Array.filter (fun a -> not (Set.contains a globalFlags))
    match argv with
    | [| "--help" |] | [| "-h" |] ->
        printLines Console.Out usageLines
        0
    | [||] -> runList ()
    | [| "init" |] -> runInit ()
    | [| "setup" |] -> runSetup None
    | [| "setup"; "--conn"; ref |] -> runSetup (Some ref)
    | [| "survey" |] -> runSurvey ()
    | _ ->
        match discoverConfig () with
        | Error es ->
            Console.Error.WriteLine "projection: projection.json is invalid:"
            printErrors Console.Error es
            6
        | Ok cfg ->
            match Command.parse cfg (List.ofArray argv) with
            | Error es ->
                for e in es do TtyRenderer.renderVoicedError e
                Console.Error.WriteLine ""
                printLines Console.Error usageLines
                1
            | Ok intent ->
                // Pure routing → effectful runner. The surface→engine map is
                // totality-tested (`Command.plan`); `runPlan` executes + voices.
                //
                // G0c — compute the advisory capability survey HERE (the dispatch
                // layer, where `cfg` is in scope; `discoverConfig`/`survey` live
                // below `runTransfer` in this file, so the survey is threaded IN,
                // never fetched inside the runner). Run it only for a live-Execute
                // Flow (a `--go` flow); preview / non-flow verbs carry no advisory
                // (the empty list). The survey is read-only; its findings warn but
                // never gate (R6 — V2 owns no production write path).
                let surveyAdvisory =
                    match intent with
                    | Intent.Flow (_, opts) when opts.Go ->
                        let reports = (CapabilitySurvey.survey cfg).GetAwaiter().GetResult()
                        CapabilitySurvey.advisoryLines reports
                    | _ -> []
                runPlan cfg.Shaping surveyAdvisory (Command.plan cfg intent)
