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
        "projection — produce the model at a destination, check it, understand it, seal it."
        "  One engine, four verbs (THE_CLI.md). Targets and the default model are named in"
        "  projection.json (or PROJECTION_CONFIG); a target's conn is an env:/file: reference (D9)."
        ""
        "USAGE:"
        "    projection project --to <dest> [options]"
        "    projection check  ( <source.sql> [--cdc-silence] | drift --model <m> --to <t>"
        "                      | data --before <t> --after <t> | ready )"
        "    projection explain ( diff <a> <b> [--format json] [--depth N] | policy <a> <b>"
        "                       | node <config> <ssKey> | suggest <config> [--apply <out>] | registry"
        "                       | migrate --to <b> ( --from <a> | --store <s> ) [--allow-drops] )"
        "    projection seal ( --store <path> | approve <version> --approver <name>"
        "                    [--rationale <text>] [--store <path>] )"
        "    projection init                 scaffold a projection.json"
        ""
        "PROJECT — the hero. Produce the model at a destination; the destination decides the form."
        "  --to <dest>   a folder (the file bundle) · docker (one-touch ephemeral DB) · a named"
        "                target or env:/file: ref (a live database; reads the prior state and"
        "                applies the minimal change). dir: forces a folder."
        "  Model source: --config <unified.json> (model + overlays) · --model <model.json> ·"
        "                projection.json \"model\". "
        "  --shape bundle|ssdt|skeleton   file-bundle composition (folder destinations)."
        "  --scope all|schema|data        which legs to emit (DDL+DML / DML-only)."
        "  --how merge|replace|fresh      the data replacement strategy."
        "  --data model|synthetic|none|<target>   data origin; <target> ingests rows (transfer)."
        "  --rekey <users.csv>            re-key identities (Reidentify) on a data load."
        "  --from auto|empty|<model>|@<target>   the baseline A (default auto)."
        "  --go                           commit a live write (preview by default). The live write"
        "                                 also needs PROJECTION_ALLOW_EXECUTE=1 (R6)."
        "  --allow-drops                  accept declared destructive loss."
        ""
        "CHECK — assert fidelity.  fidelity canary (default; --cdc-silence adds the redeploy"
        "  silence assertion) · drift (deployed vs model) · data (row/null counts) · ready"
        "  (the run-ledger readiness gauge; needs PROJECTION_LEDGER_DIR)."
        ""
        "EXPLAIN — understand before shipping.  diff (two refs) · policy (two configs) · node"
        "  (one SsKey's transforms + findings) · suggest (ranked config edits) · migrate"
        "  (the dry-run plan: two-model or snapshot⊖snapshot)."
        ""
        "SEAL — provenance.  eject (the append-forever package; default) · approve (record a"
        "  policy-version decision)."
        ""
        "Every verb persists a bench snapshot to bench/<verb>/<utc-iso>.json; -v surfaces the"
        "table. --pretty / --json force the channel (default AUTO: a TTY gets the panel, a pipe"
        "gets NDJSON). --watch shows the live stage board on a project --to <folder> --config run."
        ""
        "Exit codes:"
        "    0  succeeded"
        "    1  argv error (missing input / unknown target)"
        "    2  parse error (model JSON / spec / config-parse)"
        "    3  execution error (SQL rejected the change; connection open; unbreakable cycle)"
        "    4  Docker unavailable (project --to docker; check fidelity)"
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

/// Print one validation-error line directly via per-segment BCL
/// `Write` / `WriteLine` calls. Per chapter 3.5 deep audit
/// (2026-05-09): the prior implementation joined `"  [<code>]
/// <message>"` via `sprintf` + `String.concat "\n"`. Defensive:
/// `Console.Write` writes each typed segment independently;
/// `WriteLine` appends the line terminator. No intermediate
/// concatenated string.
let private printErrorLine (writer: TextWriter) (e: ValidationError) : unit =
    writer.Write "  ["
    writer.Write e.Code
    writer.Write "] "
    writer.WriteLine e.Message

let private printErrors (writer: TextWriter) (errors: ValidationError list) : unit =
    for e in errors do printErrorLine writer e

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
        printfn "projection: wrote %d artifact(s) to %s" report.Paths.Length effectiveOutput
        report.Paths
        |> List.iter (fun p ->
            let info = FileInfo p
            printfn "  %s (%d bytes)" p info.Length)
        storeLeg
        |> Option.iter (fun leg ->
            printfn "projection: lifecycle bundle — recorded episode (%d on timeline %s); accumulated refactorlog %d entr(ies)"
                (EpisodicLifecycle.episodes leg.Chain |> List.length)
                (Timeline.name (EpisodicLifecycle.timeline leg.Chain))
                (List.length leg.AccumulatedRefactorLog))
    | FullExportRun.RunOutcome.ConfigInvalid _ ->
        // config.validationFailed envelopes already emitted by `execute`.
        ()
    | FullExportRun.RunOutcome.RunFailed errors ->
        Console.Error.WriteLine "projection: full-export failed:"
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
                            printfn "projection: full-export published %d artifact(s); loaded the seed (%d CDC capture(s) measured)"
                                report.Paths.Length cdcDelta
                            legOpt
                            |> Option.iter (fun leg ->
                                printfn "  episode recorded (%d on timeline %s)"
                                    (EpisodicLifecycle.episodes leg.Chain |> List.length)
                                    (Timeline.name (EpisodicLifecycle.timeline leg.Chain)))
                            0
                        | Error es ->
                            Console.Error.WriteLine "projection full-export --load: failed:"
                            printErrors Console.Error es
                            3
                    dumpBench "full-export"
                    code

let private runEmit (inputPath: string) (outputDir: string) : int =
    if not (File.Exists inputPath) then
        die 1 (sprintf "projection: input file not found: %s" inputPath)
    else
        let task = Compose.run inputPath outputDir
        let result = task.GetAwaiter().GetResult()
        let exitCode =
            match result with
            | Ok paths ->
                printfn "projection: wrote %d artifact(s) to %s" paths.Length outputDir
                paths
                |> List.iter (fun p ->
                    let info = FileInfo p
                    printfn "  %s (%d bytes)" p info.Length)
                0
            | Error errors ->
                (
                    Console.Error.WriteLine "projection: parse failed:"
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
let private runEmitSkeletonOnly (inputPath: string) (outputDir: string) : int =
    if not (File.Exists inputPath) then
        die 1 (sprintf "projection: input file not found: %s" inputPath)
    else
        let task = Compose.runSkeletonOnly inputPath outputDir
        let result = task.GetAwaiter().GetResult()
        let exitCode =
            match result with
            | Ok paths ->
                printfn
                    "projection: wrote %d skeleton-only artifact(s) to %s"
                    paths.Length
                    outputDir
                paths
                |> List.iter (fun p ->
                    let info = FileInfo p
                    printfn "  %s (%d bytes)" p info.Length)
                0
            | Error errors ->
                (
                    Console.Error.WriteLine "projection: parse failed:"
                    printErrors Console.Error errors
                    2
                )
        dumpBench "emit-skeleton-only"
        exitCode

let private runDeploy (inputPath: string) : int =
    if not (File.Exists inputPath) then
        die 1 (sprintf "projection: input file not found: %s" inputPath)
    elif not (Deploy.Docker.isAvailable ()) then
        die
            4
            "projection: Docker daemon not reachable. Set DOCKER_HOST or start the daemon to run `deploy`."
    else
        printfn "projection: spinning up an ephemeral SQL Server container..."
        let task = Deploy.runFromV1Json inputPath
        let result = task.GetAwaiter().GetResult()
        let exitCode =
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
                    Console.Error.WriteLine "projection: parse failed:"
                    printErrors Console.Error errors
                    2
                )
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
                    printfn "projection: canary green — PhysicalSchema diff empty"
                    0
                else
                    eprintfn ""
                    eprintfn
                        "projection: canary RED — PhysicalSchema diff non-empty:\n%s"
                        (PhysicalSchema.renderDiff report.Diff)
                    5
            | Error errors ->
                (
                    Console.Error.WriteLine "projection: canary failed:"
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
                    eprintfn
                        "projection: canary RED — PhysicalSchema diff non-empty:\n%s"
                        (PhysicalSchema.renderDiff report.Diff)
                if cdcDelta <> 0 then
                    eprintfn
                        "projection: canary RED — idempotent redeploy fired %d CDC capture(s) (expected 0)"
                        cdcDelta
                if schemaOk && cdcDelta = 0 then
                    printfn "projection: canary green — PhysicalSchema diff empty AND idempotent redeploy CDC-silent (0 captures measured)"
                    0
                else 5
            | Error errors ->
                Console.Error.WriteLine "projection: canary failed:"
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
    printfn "projection: approved policy version %s" policyVersion
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
            | Error e -> Error (sprintf "%A" e)
            | Ok registry ->
                match ApprovalStore.save path (ApprovalRegistry.record record registry) with
                | Ok () -> printfn "  store     : %s (recorded)" path; Ok ()
                | Error e -> Error (sprintf "%A" e)
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
    | IdentityDisposition.ReconciledByRule    -> "ReconciledByRule"
    | IdentityDisposition.AssignedBySink      -> "AssignedBySink"
    | IdentityDisposition.PreservedFromSource -> "PreservedFromSource"

let private narrateTransferReport (report: Transfer.TransferReport) : unit =
    let modeName =
        match report.Mode with
        | Transfer.DryRun  -> "DryRun (preview only — no Sink writes)"
        | Transfer.Execute -> "Execute (Sink wrote)"
    printfn "projection transfer: mode = %s" modeName
    printfn ""
    printfn "Plan (%d kind(s)):" report.Kinds.Length
    for k in report.Kinds do
        printfn "  %-40s %-22s ingested=%d written=%d deferredFkCols=%d"
            (SsKey.rootOriginal k.Kind)
            (dispositionName k.Disposition)
            k.RowsIngested
            k.RowsWritten
            (Set.count k.DeferredFkColumns)
    if not (List.isEmpty report.UnbreakableCycleFks) then
        printfn ""
        printfn
            "Unbreakable cycle FKs (%d) — plan is unsatisfiable for Execute:"
            report.UnbreakableCycleFks.Length
        for u in report.UnbreakableCycleFks do
            printfn
                "  %s.%s -> %s"
                (SsKey.rootOriginal u.Kind)
                (Name.value u.Column)
                (SsKey.rootOriginal u.Target)
    if not (List.isEmpty report.UnmatchedIdentities) then
        printfn ""
        printfn
            "Unmatched identities (%d) — reconciled-kind sources with no Sink match:"
            report.UnmatchedIdentities.Length
        for (k, s) in report.UnmatchedIdentities do
            printfn "  %s source '%s'" (SsKey.rootOriginal k) (SourceKey.value s)
    if not (List.isEmpty report.SkippedReferences) then
        printfn ""
        printfn
            "Skipped references (%d) — rows dropped (FK targets an unmatched identity):"
            report.SkippedReferences.Length
        for (owner, r) in report.SkippedReferences do
            printfn
                "  %s.%s -> %s (unresolved source '%s')"
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
        Console.Error.WriteLine
            "projection transfer: --execute requires PROJECTION_ALLOW_EXECUTE=1 in the environment (R6 gate). Refusing."
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
    let result =
        (Transfer.runThroughConnections mode allowCdc allowDrops connections resolveReconciliation)
            .GetAwaiter().GetResult()
    let exitCode =
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
                        "projection transfer: %d row(s) dropped (transfer.droppedReferences) — refusing exit 0. Pass --allow-drops to accept the loss."
                        (Transfer.droppedRowCount report))
                let kindCount (label: string) (keys: SsKey seq) =
                    keys
                    |> Seq.countBy SsKey.rootOriginal
                    |> Seq.iter (fun (k, n) ->
                        Console.Error.WriteLine (sprintf "  %s %s: %d" label k n))
                kindCount "SkippedReferences" (report.SkippedReferences |> List.map fst)
                kindCount "UnmatchedIdentities" (report.UnmatchedIdentities |> List.map fst)
            dropCode
        | Error errors ->
            Console.Error.WriteLine "projection transfer: failed:"
            printErrors Console.Error errors
            let anyCode (prefix: string) =
                errors |> List.exists (fun (e: ValidationError) -> e.Code.StartsWith prefix)
            // G1 — a dead/unreachable endpoint refuses with exit 6 (connection).
            // G2 — an insufficient sink grant (or a failed grant probe) refuses
            // with exit 7 (permission), mirroring the migrate verb's mapping.
            if anyCode "transfer.connection" then 6
            elif anyCode "transfer.insufficientGrant" || anyCode "transfer.grantProbeFailed" then 7
            elif anyCode "transfer.reconcile." || anyCode "transfer.userMap." then 2
            // AC-I5 — a pre-write validate-user-map halt maps to the SAME exit
            // as a post-write drop (9), so an orphan returns the same code
            // whether refused before the write or reported after it; only the
            // timing (and the untouched Sink) changes.
            elif anyCode "transfer.unmappedIdentities" then Transfer.DroppedReferencesExit
            else 3
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
        printfn "projection verify-data: clean — no row-count or null-count divergence."
    else
        if not (List.isEmpty report.RowCountDeltas) then
            printfn "Row-count divergences (%d):" report.RowCountDeltas.Length
            for d in report.RowCountDeltas do
                printfn "  %-40s before=%d after=%d (delta=%+d)"
                    (SsKey.rootOriginal d.Kind) d.Before d.After (d.After - d.Before)
        if not (List.isEmpty report.NullCountDeltas) then
            printfn ""
            printfn "Null-count divergences (%d):" report.NullCountDeltas.Length
            for d in report.NullCountDeltas do
                printfn "  %-40s %-30s before=%d after=%d (delta=%+d)"
                    (SsKey.rootOriginal d.Kind) (SsKey.rootOriginal d.Attribute)
                    d.Before d.After (d.After - d.Before)
        if not (List.isEmpty report.Warnings) then
            printfn ""
            printfn "Warnings (%d) — schema drift between the two deployments:" report.Warnings.Length
            for w in report.Warnings do
                printfn "  %s: %s" w.Code w.Message

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
            Console.Error.WriteLine "projection verify-data: failed:"
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
                        printfn "projection drift: no drift — the deployed schema matches the model"
                        return 0
                    else
                        eprintfn "projection drift: DRIFT DETECTED — the deployed schema differs from the model:\n%s"
                            (PhysicalSchema.renderDiff diff)
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
            printfn "projection eject: package self-verified — FTC reconstruction from genesis reproduces the frozen state"
            0
        else
            Console.Error.WriteLine "projection eject: package FAILED self-verification — the reconstruction does not reproduce the frozen state."
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
        Console.Error.WriteLine "projection explain: config invalid:"
        printErrors Console.Error errs
        2
    | Ok config ->
        match (Compose.runWithConfig config).GetAwaiter().GetResult() with
        | Error errs ->
            Console.Error.WriteLine "projection explain: run failed:"
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
                printfn "      %s try a fuller SsKey, or a model/profile that exercises this node" Theme.dot
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
        Console.Error.WriteLine "projection suggest-config: config invalid:"
        printErrors Console.Error errs
        2
    | Ok config ->
        let task = Compose.runWithConfig config
        match task.GetAwaiter().GetResult() with
        | Error errs ->
            Console.Error.WriteLine "projection suggest-config: run failed:"
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
                    printfn "  %s wrote merged patch (%d edits) to %s" Theme.ok (List.length merged) out
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
        Console.Error.WriteLine "projection policy-diff: config error:"
        printErrors Console.Error errors
        6
    | Ok cfgA, Ok cfgB ->
        let result = (PolicyDiff.diffConfigs cfgA cfgB).GetAwaiter().GetResult()
        let exitCode =
            match result with
            | Error errors ->
                Console.Error.WriteLine "projection policy-diff: failed:"
                printErrors Console.Error errors
                2
            | Ok diff ->
                let s = diff.StructuralDiff
                printfn "projection policy-diff: %s"
                    (if s.AnyChanged then "policies differ" else "policies identical")
                let axis (name: string) (changed: bool) =
                    printfn "  %-13s %s" name (if changed then "changed" else "same")
                axis "selection"    s.Selection.Changed
                axis "emission"     s.Emission.Changed
                axis "insertion"    s.Insertion.Changed
                axis "tightening"   s.Tightening.Changed
                axis "userMatching" s.UserMatching.Changed
                printfn "  changed kinds: %d" (List.length diff.ChangedKinds)
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

/// Shared dry-run renderer for a `migrate` preview outcome — the change-manifest
/// of δ, or a fail-loud refusal (undeclared losses / inexpressible change).
let private reportPreviewOutcome (header: string) (result: Result<MigrationArtifacts, MigrationError>) : int =
    let exitCode =
        match result with
        | Error (RefusedByViolations violations) ->
            Console.Error.WriteLine (
                sprintf "projection migrate: REFUSED — %d undeclared destructive change(s). Re-run with --allow-drops (accept all) or --declare-drop <token> for each:" (List.length violations))
            for v in violations do
                Console.Error.WriteLine (sprintf "    %s" (Migration.lossToken v))
            9
        | Error (RefusedBySchemaErrors entries) ->
            Console.Error.WriteLine "projection migrate: REFUSED — change(s) the engine cannot express as a single ALTER:"
            for e in entries do
                Console.Error.WriteLine (sprintf "    [%s] %s" e.Code e.Message)
            9
        | Error other ->
            Console.Error.WriteLine (sprintf "projection migrate: failed: %A" other)
            2
        | Ok artifacts ->
            let p = artifacts.Plan.Preview
            printfn "%s" header
            if Migration.isIdempotent artifacts.Plan then
                printfn "  idempotent — nothing to do (zero minimum-viable touches)"
            else
                printfn "  minimum-viable touches (norm): %d" p.Norm
                printfn "    renamed kinds:        %d" p.Channels.RenamedKinds
                printfn "    added kinds:          %d" p.Channels.AddedKinds
                printfn "    removed kinds:        %d" p.Channels.RemovedKinds
                printfn "    reshaped attributes:  %d" p.Channels.ChangedAttributes
                printfn "    renamed attributes:   %d" p.Channels.RenamedAttributes
                printfn "    added attributes:     %d" p.Channels.AddedAttributes
                printfn "    removed attributes:   %d" p.Channels.RemovedAttributes
                for (_, fromN, toN) in p.RenamedKinds do
                    printfn "    rename: %s -> %s" (Name.value fromN) (Name.value toN)
                printfn "  emitted: %d ALTER/ADD statement(s), %d refactorlog rename(s)"
                    (List.length artifacts.SchemaStatements) (List.length artifacts.RefactorLog)
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
let private runMigrateFromStore (storePath: string) (toPath: string) (declaration: LossDeclaration) : int =
    let bRead = (Compose.read toPath).GetAwaiter().GetResult()
    match bRead with
    | Error errors ->
        Console.Error.WriteLine (sprintf "projection migrate: could not read --to %s:" toPath)
        printErrors Console.Error errors
        6
    | Ok target ->
        reportPreviewOutcome
            (sprintf "projection migrate (store:%s) -> %s  (dry-run, snapshot⊖snapshot)" storePath toPath)
            (MigrationRun.previewFromStore storePath declaration target)

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
        Console.Error.WriteLine (
            sprintf "projection migrate: REFUSED — %d undeclared destructive change(s). Re-run with --allow-drops (accept all) or --declare-drop <token> for each:" (List.length violations))
        for v in violations do Console.Error.WriteLine (sprintf "    %s" (Migration.lossToken v))
        9
    | RefusedBySchemaErrors entries ->
        Console.Error.WriteLine "projection migrate: REFUSED — change(s) the engine cannot express:"
        for e in entries do Console.Error.WriteLine (sprintf "    [%s] %s" e.Code e.Message)
        9
    | RefusedByCdc tracked ->
        Console.Error.WriteLine (
            sprintf "projection migrate: REFUSED — schema DDL against a CDC-tracked DB (%d table(s)); pass --allow-cdc to proceed." (List.length tracked))
        9
    | RefusedByTightening msg ->
        Console.Error.WriteLine (
            sprintf "projection migrate: REFUSED — a column tightening (NULL → NOT NULL) would fail against existing NULL data; no DDL ran. %s" msg)
        9
    | SchemaReadFailed es ->
        Console.Error.WriteLine "projection migrate: reading the deployed schema failed:"
        printErrors Console.Error es
        6
    | other ->
        Console.Error.WriteLine (sprintf "projection migrate: failed: %A" other)
        2

/// The A1 connection + A2 permission pre-flights against a sink connection,
/// given the planned schema statements. Returns `Ok ()` to proceed or a printed
/// refusal + exit code. A2's grant capture is database-scope (survey-gated
/// object-scope refinement, OPEN-2 / P1).
let private migratePreflights (label: string) (cnn: Microsoft.Data.SqlClient.SqlConnection) (planned: Preflight.PlannedWrite list) : System.Threading.Tasks.Task<Result<unit, int>> =
    task {
        match! Preflight.connectionPreflight cnn cnn with
        | Error es ->
            Console.Error.WriteLine (sprintf "projection migrate: %s connection pre-flight refused:" label)
            printErrors Console.Error es
            return Error 7
        | Ok () ->
            match! Preflight.captureGrantEvidence cnn with
            | Error es ->
                // A grant-probe failure is non-fatal to correctness but we surface it.
                Console.Error.WriteLine "projection migrate: permission probe failed (sys.fn_my_permissions):"
                printErrors Console.Error es
                return Error 7
            | Ok grant ->
                match Preflight.permissionPreflight grant planned with
                | Error es ->
                    Console.Error.WriteLine "projection migrate: permission pre-flight refused:"
                    printErrors Console.Error es
                    return Error 7
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
                Console.Error.WriteLine "projection migrate: tightening pre-flight refused (NOT-NULL on NULL-bearing data):"
                printErrors Console.Error es
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
let private runMigrateExecute (toPath: string) (connSpec: string) (declaration: LossDeclaration) (allowCdc: bool) (storePath: string option) (envLabel: string option) : int =
    if System.Environment.GetEnvironmentVariable "PROJECTION_ALLOW_EXECUTE" <> "1" then
        Console.Error.WriteLine
            "projection migrate: --execute requires PROJECTION_ALLOW_EXECUTE=1 in the environment (R6 gate). Refusing."
        dumpBench "migrate"
        7
    else
    let work =
        task {
            let! bRead = Compose.read toPath
            match bRead, TransferSpec.parseConnectionSpec connSpec with
            | Error es, _ ->
                Console.Error.WriteLine (sprintf "projection migrate: could not read --to %s:" toPath)
                printErrors Console.Error es
                return 6
            | _, Error es ->
                Console.Error.WriteLine "projection migrate: --conn argument error:"
                printErrors Console.Error es
                return 2
            | Ok target, Ok connRef ->
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
                                                printfn "projection migrate: executed and VERIFIED — B' reproduces B (%d statement(s)); episode recorded to %s (%d episode(s) on timeline %s)"
                                                    (List.length o.Artifacts.SchemaStatements) store
                                                    (EpisodicLifecycle.episodes chain |> List.length) (Timeline.name tl)
                                                return 0
                                            | Ok (_, None) ->
                                                Console.Error.WriteLine "projection migrate: executed but verification FAILED — B' does not reproduce B. No episode recorded."
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
                                            printfn "projection migrate: executed and VERIFIED — B' reproduces B (%d statement(s); %d CDC capture(s) measured)"
                                                (List.length o.Artifacts.SchemaStatements) cdcDelta
                                            eprintfn "projection migrate: note — no --lifecycle-store supplied; no episode persisted (the next diff has no prior to load)."
                                            return 0
                                        | Ok _ ->
                                            Console.Error.WriteLine "projection migrate: executed but verification FAILED — B' does not reproduce B."
                                            return 9
                                        | Error e -> return reportMigrationError e
        }
    let code = work.GetAwaiter().GetResult()
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
let private runMigrateWithData (toPath: string) (sinkSpec: string) (sourceSpec: string) (reconcileSpecs: string list) (userMapPath: string option) (declaration: LossDeclaration) (allowCdc: bool) (storePath: string option) (envLabel: string option) : int =
    if System.Environment.GetEnvironmentVariable "PROJECTION_ALLOW_EXECUTE" <> "1" then
        Console.Error.WriteLine
            "projection migrate: --execute requires PROJECTION_ALLOW_EXECUTE=1 in the environment (R6 gate). Refusing."
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
            let! bRead = Compose.read toPath
            match bRead, TransferSpec.parseConnectionSpec sinkSpec, TransferSpec.parseConnectionSpec sourceSpec with
            | Error es, _, _ ->
                Console.Error.WriteLine (sprintf "projection migrate: could not read --to %s:" toPath)
                printErrors Console.Error es
                return 6
            | _, Error es, _ ->
                Console.Error.WriteLine "projection migrate: --sink-conn argument error:"
                printErrors Console.Error es
                return 2
            | _, _, Error es ->
                Console.Error.WriteLine "projection migrate: --source-conn argument error:"
                printErrors Console.Error es
                return 2
            | Ok target, Ok sinkRef, Ok sourceRef ->
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
                                Console.Error.WriteLine "projection migrate: connection pre-flight refused:"
                                printErrors Console.Error es
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
                                                    printfn "projection migrate: schema executed + data loaded — %d kind(s) transferred; episode recorded to %s (%d CDC capture(s) measured; %d episode(s) on timeline %s)"
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
                                            printfn "projection migrate: schema VERIFIED + data loaded — %d kind(s) transferred"
                                                (List.length o.Transfer.Kinds)
                                            return 0
                                        | Ok _ ->
                                            Console.Error.WriteLine "projection migrate: schema executed but verification FAILED — data leg skipped."
                                            return 9
                                        | Error e -> return reportMigrationError e
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

/// Resolve the source of B to a concrete model path. A `ConfigFile` yields the
/// config's `Model.Path`; a bare `ModelFile` is itself; `Unspecified` refuses.
let private modelPathOf (model: ModelSource) : Result<string> =
    match model with
    | ModelSource.ModelFile m -> Result.success m
    | ModelSource.ConfigFile c ->
        match Config.fromFile c with
        | Ok cfg    -> Result.success cfg.Model.Path
        | Error es  -> Result.failure es
    | ModelSource.Unspecified ->
        Result.failureOf
            (ValidationError.create "cli.project.modelMissing"
                "no model — pass --model <model.json>, --config <config.json>, or set \"model\" in projection.json.")

/// project --to <live> with no --go — preview the minimal change against the
/// deployed state A, never writing (THE_CLI.md §5). Reads A live, previews
/// B ⊖ A, renders the plan via the shared migrate renderer.
let private runProjectLivePreview (toPath: string) (connSpec: string) (declaration: LossDeclaration) : int =
    let work =
        task {
            let! bRead = Compose.read toPath
            match bRead, TransferSpec.parseConnectionSpec connSpec with
            | Error es, _ ->
                Console.Error.WriteLine (sprintf "projection project: could not read the model %s:" toPath)
                printErrors Console.Error es
                return 6
            | _, Error es ->
                Console.Error.WriteLine "projection project: connection reference error:"
                printErrors Console.Error es
                return 6
            | Ok target, Ok connRef ->
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
let private runPlan (plan: ExecutionPlan) : int =
    for n in plan.Notes do eprintfn "projection project: note — %s" n
    let needModel (model: ModelSource) (run: string -> int) : int =
        match modelPathOf model with
        | Ok m     -> run m
        | Error es ->
            for e in es do TtyRenderer.renderVoicedError e
            6
    match plan.Action with
    // project ------------------------------------------------------------
    | PlanAction.PublishBundle (c, dir, store, env) ->
        let verbosity = if verboseMode.Value then LogSink.Verbosity.Verbose else LogSink.Verbosity.Quiet
        let run () = runFullExport c (Some dir) verbosity Set.empty store env
        // --watch + a real TTY → the live stage board (§13).
        if Watch.shouldWatch watchMode.Value then Watch.renderWatch (Watch.resolveDwellMs ()) run
        else run ()
    | PlanAction.EmitSkeleton (m, dir) -> withRun "projection project" (fun () -> runEmitSkeletonOnly m dir)
    | PlanAction.EmitBundle (m, dir)   -> withRun "projection project" (fun () -> runEmit m dir)
    | PlanAction.DeployDocker model    -> needModel model (fun m -> withRun "projection project" (fun () -> runDeploy m))
    | PlanAction.PreviewSchema (model, conn, decl) -> needModel model (fun m -> runProjectLivePreview m conn decl)
    | PlanAction.Transfer (src, sink, opts, execute) ->
        runTransfer src sink None None opts.Reconcile opts.Rekey execute opts.AllowCdc (opts.Declaration = DeclareAll)
    | PlanAction.MigrateWithData (model, sink, src, opts) ->
        needModel model (fun m -> runMigrateWithData m sink src opts.Reconcile opts.Rekey opts.Declaration opts.AllowCdc opts.Store opts.Env)
    | PlanAction.PublishAndLoad (c, conn, store, env) -> runFullExportLoad c conn None store env
    | PlanAction.Migrate (model, conn, opts) -> needModel model (fun m -> runMigrateExecute m conn opts.Declaration opts.AllowCdc opts.Store opts.Env)
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
        printfn "projection: %d registered transform(s)" (List.length all)
        for rt in all |> List.sortBy (fun r -> sprintf "%A" r.StageBinding, r.Name) do
            printfn "  %-12s %s" (sprintf "%A" rt.StageBinding) rt.Name
        0
    | PlanAction.ExplainMigratePreview (fromP, toP, decl)   -> runMigratePreview fromP toP decl
    | PlanAction.ExplainMigrateFromStore (store, toP, decl) -> runMigrateFromStore store toP decl
    // seal ---------------------------------------------------------------
    | PlanAction.SealEject store -> runEject store
    | PlanAction.SealApprove (version, approver, rationale, store) -> runApprove version approver rationale store
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
        let scaffold =
            "{\n" +
            "  \"model\": \"model.json\",\n" +
            "  \"targets\": {\n" +
            "    \"dev\":     { \"conn\": \"env:DEV_CONN\", \"store\": \"lifecycle/dev.json\" },\n" +
            "    \"qa\":      { \"conn\": \"env:QA_CONN\",  \"store\": \"lifecycle/qa.json\" },\n" +
            "    \"uat\":     { \"conn\": \"env:UAT_CONN\", \"store\": \"lifecycle/uat.json\" },\n" +
            "    \"publish\": { \"dir\": \"./publish\" }\n" +
            "  },\n" +
            "  \"defaults\": { \"how\": \"merge\", \"data\": \"model\" }\n" +
            "}\n"
        File.WriteAllText(path, scaffold)
        printfn "projection init: wrote %s — name the model and targets (a conn is an env:/file: reference, never a literal string)." path
        0

/// Discover `projection.json` (or `PROJECTION_CONFIG`) — absent is the empty
/// config (aliasing is opt-in).
let private discoverConfig () : Result<TargetConfig> =
    let path =
        match System.Environment.GetEnvironmentVariable "PROJECTION_CONFIG" with
        | null | "" -> "projection.json"
        | p -> p
    TargetConfig.fromFile path

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
    | [||] | [| "--help" |] | [| "-h" |] ->
        printLines Console.Out usageLines
        0
    | [| "init" |] -> runInit ()
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
                runPlan (Command.plan cfg intent)
