module Projection.Cli.Program

open System
open System.Diagnostics
open System.IO
open Argu
open Projection.Core
open Projection.Pipeline
open Projection.Targets.SSDT
open Projection.Cli.FullExportArgs

/// Usage lines. Per chapter 3.5 deep audit (2026-05-09): the lines
/// are a typed `string list` carrying the structured help-page
/// content. Emission to the terminal is via per-line BCL
/// `TextWriter.WriteLine` rather than concatenation into a
/// multi-line string. The typed list IS the data; each line is
/// emitted independently; no intermediate concatenation.
let private usageLines : string list =
    [
        "projection — V2 sidecar end-to-end pipeline."
        ""
        "USAGE:"
        "    projection full-export --config <path> [--output <dir>] [--verbose]"
        "    projection emit --config <path>"
        "    projection emit [--skeleton-only] <input-osm-model.json> <output-dir>"
        "    projection deploy <input-osm-model.json>"
        "    projection canary <source-ddl-file>"
        ""
        "SUBCOMMANDS:"
        "    full-export   Phase B structural-exit subcommand (chapter B.4 slice 7)."
        "                  Reads a unified config, projects through V2's pass chain,"
        "                  writes SSDT + JSON + Distributions + actionable diagnostic"
        "                  artifacts, and emits a structured NDJSON event stream to"
        "                  stderr conforming to docs/logging-format.md. Wraps today's"
        "                  three live config consumers (Model.Path; Overrides"
        "                  .TableRenames; Output.Dir); other config sections parse-"
        "                  but-ignore (Chapter C wires them). Runs against SnapshotJson"
        "                  / SnapshotRowsets connectivity only — LiveOssysConnection"
        "                  is a follow-up chapter."
        ""
        "    emit    Parse V1 JSON, project through three sibling Π's,"
        "            and write SSDT / JSON / Distributions artifacts."
        "            Three argument forms:"
        "              --config <path>     read unified config JSON (V2_PRODUCTION_CUTOVER §5.1)."
        "              --skeleton-only <input> <out>  project through skeletonChainSteps"
        "                                  only (the four pure-DataIntent passes; per"
        "                                  chapter A.4.7' slice ζ)."
        "              <input> <out>       legacy positional form (kept during A.1 transition)."
        ""
        "    deploy  Parse V1 JSON, project SSDT, spin up an ephemeral"
        "            SQL Server container, deploy the SSDT, count tables,"
        "            and tear down. Run-level idempotent."
        ""
        "    canary  Run the wide canary against a SQL DDL file. Deploy"
        "            source to one ephemeral DB, read it back, run V2's"
        "            emitter on the reconstruction, deploy that to a"
        "            second DB, read back, compare on the PhysicalSchema"
        "            axis. Per DECISIONS 2026-05-23, this is V2's primary"
        "            wide integration surface."
        ""
        "All commands print a Bench table at exit and persist a JSON"
        "snapshot to bench/<command>/<utc-iso>.json under the current"
        "working directory. Per session-29 framing, this puts the perf"
        "surface in the operator's daily attention so regressions"
        "surface naturally."
        ""
        "Exit codes:"
        "    0  command succeeded"
        "    1  argv error (wrong arg count, missing input)"
        "    2  parse error (V1 JSON did not satisfy V2's adapter contract)"
        "    3  deploy error (SQL Server rejected the SSDT)"
        "    4  Docker unavailable (deploy/canary requires a running daemon)"
        "    5  canary divergence (PhysicalSchema diff non-empty)"
        "    6  config error (config file missing / unparseable / D9 violation)"
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
let private dumpBench (tag: string) : unit =
    let stats = Bench.snapshot ()
    if not (List.isEmpty stats) then
        printfn ""
        printfn "Bench (sorted by total time):"
        printfn "%s" (Bench.renderTable stats)
        let path = BenchSink.defaultPath (Directory.GetCurrentDirectory()) tag
        try
            BenchSink.persistJson path tag stats
            printfn ""
            printfn "  bench snapshot: %s" path
        with ex ->
            eprintfn "  WARNING: failed to persist bench snapshot: %s" ex.Message

// ----------------------------------------------------------------------
// `full-export` (chapter B.4 slice 7) — Phase B structural-exit
// subcommand. Per chapter B.4 mid-rescope + `DECISIONS 2026-05-19
// (slice B.4.{4-7}.rescope)`: THIN scope; wraps today's three live
// `Pipeline.Config` consumers + slice 6's actionable-diagnostics + slice
// 6.5's LogSink emission. NDJSON event stream to stderr conforms to
// `docs/logging-format.md` §3-§13 + §15.1; runSummary terminal event
// per §10.
// ----------------------------------------------------------------------

/// Map a stage's `Bench.scope` duration to a `LogSink.recordStage` +
/// `summary.stageCompleted` envelope pair. Pure boundary helper —
/// emits both ends so the runSummary's stage table is populated
/// alongside the stream's per-stage end events.
let private recordStage
    (stageName: string)
    (outcome: LogSink.Outcome)
    (durationMs: int64)
    : unit =
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

/// Emit a structured config-error envelope per the §7.1 contract
/// (`config.validationFailed`). One event per ValidationError so the
/// operator can grep + jq each independently.
let private emitConfigErrors (errors: ValidationError list) : unit =
    for e in errors do
        let payload : Map<string, objnull> =
            Map.ofList [
                "code",    box e.Code
                "reason",  box e.Message
            ]
        LogSink.emit
            { LogSink.envelope LogSink.Error LogSink.Config "config.validationFailed" payload with
                Phase = LogSink.ErrorPhase }

/// Render an emit-phase ValidationError stream as one
/// `transform.diagnostic` event per error (per §7.4 — level matches
/// severity; Error → `error`). Slice 7's THIN scope routes
/// pass-produced errors through this projection.
let private emitTransformErrors (errors: ValidationError list) : unit =
    for e in errors do
        let payload : Map<string, objnull> =
            Map.ofList [
                "code",    box e.Code
                "message", box e.Message
            ]
        LogSink.emit
            { LogSink.envelope LogSink.Error LogSink.Transform "transform.diagnostic" payload with
                Phase = LogSink.ErrorPhase }

/// Resolve the effective output directory: CLI `--output` override
/// wins over the config's `Output.Dir`. Defaults to the config value.
let private resolveOutputDir
    (cfg: Config.Config)
    (outputOverride: string option)
    : string =
    match outputOverride with
    | Some dir when not (String.IsNullOrWhiteSpace dir) -> dir
    | _ -> cfg.Output.Dir

/// Emit a config-resolved snapshot at run start per §7.1: one
/// `config.runStart` with command + configPath, then one
/// `config.connectionResolved` naming the SnapshotJson source
/// (per D9; secrets absent by construction in V2's config).
let private emitConfigSnapshot
    (cfg: Config.Config)
    (configPath: string)
    (effectiveOutput: string)
    : unit =
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
        Map.ofList [
            "kind",      box "SnapshotJson"
            "modelPath", box cfg.Model.Path
        ]
    LogSink.emit
        { LogSink.envelope LogSink.Info LogSink.Config "config.connectionResolved" connPayload with
            Phase  = LogSink.Start
            Source = Some LogSink.Configuration }

/// `projection full-export` entry. Parses config, emits config events,
/// runs the existing `Compose.runWithConfig` orchestration (which
/// reads → renames → projects → writes), times each stage via
/// `Bench.scope`, and emits `summary.stageCompleted` + the terminal
/// `summary.runComplete` event. The NDJSON stream goes to stderr per
/// §5; artifact-path narration goes to stdout for operator visibility
/// at the CLI surface (matching the pre-existing `emit` subcommand).
let private runFullExport
    (configPath: string)
    (outputOverride: string option)
    (verbose: bool)
    : int =
    LogSink.reset ()
    LogSink.setVerbose verbose
    Bench.reset ()
    let mutable outcome : LogSink.Outcome = LogSink.Succeeded
    let exitCode =
        try
            try
                match Config.fromFile configPath with
                | Error errors ->
                    emitConfigErrors errors
                    outcome <- LogSink.Failed
                    6
                | Ok cfg ->
                    let effectiveOutput = resolveOutputDir cfg outputOverride
                    let cfgForRun = { cfg with Output = { Dir = effectiveOutput } }
                    emitConfigSnapshot cfgForRun configPath effectiveOutput
                    // Stage timings — wrap the orchestration so each named
                    // stage emits a summary.stageCompleted event. We
                    // delegate to `Compose.runWithConfig` for the full
                    // composition (read + rename + project + write) and
                    // record the aggregate stage timing under `pipeline`.
                    let sw = Stopwatch.StartNew()
                    let task = Compose.runWithConfig cfgForRun
                    let result = task.GetAwaiter().GetResult()
                    sw.Stop()
                    match result with
                    | Ok paths ->
                        recordStage "pipeline" LogSink.Succeeded sw.ElapsedMilliseconds
                        printfn "projection: wrote %d artifact(s) to %s" paths.Length effectiveOutput
                        paths
                        |> List.iter (fun p ->
                            let info = FileInfo p
                            LogSink.recordArtifact {
                                Kind      = Path.GetFileName p |> nonNull
                                Path      = p
                                SizeBytes = Some info.Length
                                FileCount = None
                            }
                            printfn "  %s (%d bytes)" p info.Length)
                        0
                    | Error errors ->
                        recordStage "pipeline" LogSink.Failed sw.ElapsedMilliseconds
                        emitTransformErrors errors
                        outcome <- LogSink.Failed
                        Console.Error.WriteLine "projection: full-export failed:"
                        printErrors Console.Error errors
                        2
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
                Console.Error.WriteLine ("projection: full-export aborted: " + ex.Message)
                2
        finally
            // §10 mandatory: runSummary emits on every exit path.
            let benchStats = Bench.snapshot ()
            LogSink.runComplete outcome "projection full-export" benchStats |> ignore
            dumpBench "full-export"
    exitCode

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

/// `emit --config <path>` entry. Reads the unified config JSON, surfaces
/// structured errors (file-not-found / parse / D9) at exit code 6, then
/// delegates to `Compose.runWithConfig` which threads the parsed config
/// through read → rename → project → write. Today the wired config
/// sections drive `Model.Path`, `Overrides.TableRenames`, and `Output.Dir`;
/// other sections are validated but unused, so operators can hand-write
/// a full config without runtime surprises.
let private runEmitFromConfig (configPath: string) : int =
    match Config.fromFile configPath with
    | Error errors ->
        Console.Error.WriteLine "projection: config error:"
        printErrors Console.Error errors
        6
    | Ok config ->
        let task = Compose.runWithConfig config
        let result = task.GetAwaiter().GetResult()
        let exitCode =
            match result with
            | Ok paths ->
                printfn "projection: wrote %d artifact(s) to %s" paths.Length config.Output.Dir
                paths
                |> List.iter (fun p ->
                    let info = FileInfo p
                    printfn "  %s (%d bytes)" p info.Length)
                0
            | Error errors ->
                Console.Error.WriteLine "projection: emit failed:"
                printErrors Console.Error errors
                2
        dumpBench "emit"
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
        let task = Deploy.runWideCanary sourceDdl SsdtDdlEmitter.statements
        let result = task.GetAwaiter().GetResult()
        let exitCode =
            match result with
            | Ok report ->
                printfn
                    "projection: source deployed %d table(s); target deployed %d table(s)"
                    report.SourceReport.TablesCreated
                    report.TargetReport.TablesCreated
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

/// Dispatch `full-export` via the Argu surface (`FullExportArgs`).
/// Argument-parse failures surface as exit code 1 with a usage hint;
/// successful parses route to `runFullExport`.
let private dispatchFullExport (argv: string[]) : int =
    let parser =
        ArgumentParser.Create<FullExportArg>(
            programName = "projection full-export",
            errorHandler = ProcessExiter())
    try
        let parsed = parser.Parse(argv, raiseOnUsage = false)
        if parsed.IsUsageRequested then
            // ProcessExiter handles --help itself, but the explicit
            // check guards against future Argu shape changes.
            0
        else
            let configPath = parsed.GetResult Config
            let outputOverride = parsed.TryGetResult Output
            let verbose = parsed.Contains Verbose
            runFullExport configPath outputOverride verbose
    with
    | :? ArguParseException as ex ->
        Console.Error.WriteLine ex.Message
        1

[<EntryPoint>]
let main argv =
    match argv with
    | [| "full-export" |] ->
        Console.Error.WriteLine "projection full-export: --config <path> required"
        Console.Error.WriteLine ""
        Console.Error.WriteLine "Run `projection full-export --help` for usage."
        1
    | arr when arr.Length >= 1 && arr.[0] = "full-export" ->
        dispatchFullExport (Array.skip 1 arr)
    | [| "emit"; "--config"; configPath |] ->
        runEmitFromConfig configPath
    | [| "emit"; "--skeleton-only"; inputPath; outputDir |] ->
        runEmitSkeletonOnly inputPath outputDir
    | [| "emit"; inputPath; outputDir |] ->
        runEmit inputPath outputDir
    | [| "deploy"; inputPath |] ->
        runDeploy inputPath
    | [| "canary"; sourceDdlPath |] ->
        runCanary sourceDdlPath
    | [||]
    | [| "--help" |]
    | [| "-h" |] ->
        // Per chapter 3.5 deep audit (2026-05-09): help-page emission
        // via per-line `Console.Out.WriteLine`. Typed list flows in;
        // per-line writes flow out; no intermediate concatenated
        // multi-line string.
        printLines Console.Out usageLines
        0
    | _ ->
        Console.Error.WriteLine "projection: invalid arguments"
        Console.Error.WriteLine ""
        printLines Console.Error usageLines
        1
