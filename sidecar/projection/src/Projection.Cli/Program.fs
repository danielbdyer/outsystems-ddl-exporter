module Projection.Cli.Program

open System
open System.Diagnostics
open System.IO
open Argu
open Projection.Core
open Projection.Adapters.Sql
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
        "    projection skeleton <input-osm-model.json> <output-dir>"
        "    projection deploy <input-osm-model.json>"
        "    projection canary <source-ddl-file>"
        "    projection approve <policy-version> --approver <name> [--rationale <text>]"
        "    projection transfer --source-conn <env|file:ref> --sink-conn <env|file:ref>"
        "                        [--reconcile <table>:<match-column>]... [--execute]"
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
        "    skeleton  Project through skeletonChainSteps only (the four pure-"
        "              DataIntent passes). Equivalent to `emit --skeleton-only`."
        "              Top-level verb for operator convenience (H-036)."
        ""
        "    approve   Record an approval decision for a policy version (H-086)."
        "              Prints the `ApprovalRecord` JSON to stdout. The policy"
        "              version is the hex SHA-256 digest produced by"
        "              `VersionedPolicy.digestOf`. Exit 0 on success."
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
        "    transfer  Bidirectional data-load — ingest rows from a Source"
        "              substrate and project them onto a Sink over one shared"
        "              schema (the Source's reconstructed catalog). Default"
        "              is a safe DryRun preview reporting the plan + the"
        "              skip-and-diagnose; `--execute` writes to the Sink and"
        "              is gated behind PROJECTION_ALLOW_EXECUTE=1 (R6 — V2"
        "              owns no production write path until the gate is lowered)."
        "              Per `--reconcile` entry: the named kind's rows are NOT"
        "              re-inserted (already in the Sink) and FKs targeting it"
        "              are re-pointed via the matched Sink surrogate; rows that"
        "              reference an unmatched identity are dropped + reported."
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
        "    2  parse error (V1 JSON did not satisfy V2's adapter contract; transfer spec error)"
        "    3  deploy / transfer-execution error (SQL Server rejected the SSDT; unbreakable cycle)"
        "    4  Docker unavailable (deploy/canary requires a running daemon)"
        "    5  canary divergence (PhysicalSchema diff non-empty)"
        "    6  config error (config file missing / unparseable / D9 violation; transfer connection ref)"
        "    7  R6 gate refusal (transfer --execute without PROJECTION_ALLOW_EXECUTE=1)"
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

/// `projection full-export` entry. Delegates the LogSink envelope
/// orchestration (config snapshot, stage timing, diagnostics, terminal
/// `summary.runComplete`) + composition to `FullExportRun.execute` — the
/// single implementation, shared with the test harness so the NDJSON
/// contract has no drift-prone second copy. Here the CLI narrates the
/// operator-facing console output and maps the `RunOutcome` to an exit
/// code. Per §5: NDJSON to stderr (via LogSink, inside `execute`);
/// artifact-path narration to stdout here.
let private runFullExport
    (configPath: string)
    (outputOverride: string option)
    (verbosity: LogSink.Verbosity)
    (mutedCategories: Set<LogSink.Category>)
    : int =
    let outcome = FullExportRun.execute configPath outputOverride verbosity mutedCategories
    match outcome with
    | FullExportRun.RunOutcome.Succeeded (report, effectiveOutput) ->
        printfn "projection: wrote %d artifact(s) to %s" report.Paths.Length effectiveOutput
        report.Paths
        |> List.iter (fun p ->
            let info = FileInfo p
            printfn "  %s (%d bytes)" p info.Length)
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
            | Ok report ->
                printfn "projection: wrote %d artifact(s) to %s" report.Paths.Length config.Output.Dir
                report.Paths
                |> List.iter (fun p ->
                    let info = FileInfo p
                    printfn "  %s (%d bytes)" p info.Length)
                // Chapter C slice C.2 — surface special-circumstances
                // findings to stderr (the legacy `emit --config` surface
                // pre-dates LogSink and uses Console for narration).
                for entry in report.Diagnostics do
                    let accepted =
                        if Map.containsKey "acceptedVia" entry.Metadata then " [accepted]"
                        else ""
                    Console.Error.WriteLine (
                        sprintf "  diagnostic [%s] %s: %s%s"
                            (string entry.Severity) entry.Code entry.Message accepted)
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

/// H-036: `projection skeleton <input> <output>` top-level verb. Identical
/// semantics to `emit --skeleton-only`; the top-level verb is the ergonomic
/// form for operator automation (H-036, Cluster C policy intelligence).
let private runSkeleton (inputPath: string) (outputDir: string) : int =
    runEmitSkeletonOnly inputPath outputDir

/// H-086: `projection approve <policyVersion> --approver <name> [--rationale <text>]`.
/// Creates an `ApprovalRecord` for the given policy version, prints it as a
/// structured summary to stdout, and exits 0. The policy version string is
/// the hex SHA-256 digest from `VersionedPolicy.digestOf` (or any opaque
/// version identifier the operator tracks). This verb does not persist the
/// record — piping stdout to a JSON store is the operator's responsibility.
let private runApprove
    (policyVersion: string)
    (approver: string)
    (rationale: string option)
    : int =
    let record =
        ApprovalWorkflow.pending policyVersion
        |> ApprovalWorkflow.approveNow approver rationale
    printfn "projection: approved policy version %s" policyVersion
    printfn "  approver  : %s" approver
    printfn "  decision  : Approved"
    printfn "  at        : %s" (record.At.ToString "o")
    match rationale with
    | Some r -> printfn "  rationale : %s" r
    | None   -> ()
    dumpBench "approve"
    0

/// Dispatch `full-export` via the Argu surface (`FullExportArgs`).
/// Argument-parse failures surface as exit code 1 with a usage hint;
/// successful parses route to `runFullExport`.
let private parseCategoryName (raw: string) : Result<LogSink.Category, string> =
    match raw.ToLowerInvariant() with
    | "config"    -> Ok LogSink.Config
    | "extract"   -> Ok LogSink.Extract
    | "profile"   -> Ok LogSink.Profile
    | "transform" -> Ok LogSink.Transform
    | "emit"      -> Ok LogSink.Emit
    | "deploy"    -> Ok LogSink.Deploy
    | "canary"    -> Ok LogSink.Canary
    | "summary"   -> Ok LogSink.Summary
    | other       ->
        Error (
            sprintf
                "projection: --mute-category '%s' is not a recognized category. Known: config | extract | profile | transform | emit | deploy | canary | summary."
                other)

let private dispatchFullExport (argv: string[]) : int =
    argv |> VerbArgs.parse<FullExportArg> "projection full-export" (fun parsed ->
            let configPath = parsed.GetResult Config
            let outputOverride = parsed.TryGetResult Output
            let verbose = parsed.Contains Verbose
            let debug = parsed.Contains Debug
            // Chapter C slice C.6 — verbosity DU resolution. --debug
            // takes precedence over --verbose (the union maximum).
            let verbosity =
                if debug then LogSink.Verbosity.Debug
                elif verbose then LogSink.Verbosity.Verbose
                else LogSink.Verbosity.Quiet
            // Resolve --mute-category arguments to a Set<Category>.
            // Aggregates per-argument errors so the operator sees
            // every malformed name in one pass.
            let muteResults =
                parsed.GetResults MuteCategory
                |> List.map parseCategoryName
            let muteErrors =
                muteResults |> List.choose (function Error e -> Some e | Ok _ -> None)
            if not (List.isEmpty muteErrors) then
                for err in muteErrors do Console.Error.WriteLine err
                1
            else
                let mutedCategories =
                    muteResults
                    |> List.choose (function Ok c -> Some c | Error _ -> None)
                    |> Set.ofList
                runFullExport configPath outputOverride verbosity mutedCategories)

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

let private runTransfer
    (sourceSpec: string)
    (sinkSpec: string)
    (reconcileSpecs: string list)
    (executeRequested: bool)
    : int =
    let collect = function Ok _ -> [] | Error es -> es
    let parsedSource    = TransferSpec.parseConnectionSpec sourceSpec
    let parsedSink      = TransferSpec.parseConnectionSpec sinkSpec
    let parsedReconciles = reconcileSpecs |> List.map TransferSpec.parseReconcileSpec
    let specErrors =
        collect parsedSource
        @ collect parsedSink
        @ (parsedReconciles |> List.collect collect)
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

    let sourceRef = Result.value parsedSource
    let sinkRef   = Result.value parsedSink
    let entries   = parsedReconciles |> List.map Result.value
    let srcStrR   = ConnectionResolver.resolve "Source" sourceRef
    let sinkStrR  = ConnectionResolver.resolve "Sink"   sinkRef
    let connErrors = collect srcStrR @ collect sinkStrR
    if not (List.isEmpty connErrors) then
        Console.Error.WriteLine "projection transfer: connection error:"
        printErrors Console.Error connErrors
        dumpBench "transfer"
        6
    else

    // Bind the apparatus for vocabulary + role validation (D9 + Substrate roles
    // are operative even before live profiling lands).
    let sourceSub : Substrate =
        { Environment   = Projection.Core.Environment.Named "Source"
          Role          = SubstrateRole.Source
          ConnectionRef = sourceRef }
    let sinkSub : Substrate =
        { Environment   = Projection.Core.Environment.Named "Sink"
          Role          = SubstrateRole.Sink
          ConnectionRef = sinkRef }
    match TransferConnections.create sourceSub sinkSub (not (List.isEmpty entries)) with
    | Error es ->
        Console.Error.WriteLine "projection transfer: apparatus invariant violation:"
        printErrors Console.Error es
        dumpBench "transfer"
        3
    | Ok _ ->

    let mode = if executeGated then Transfer.Execute else Transfer.DryRun
    let work =
        task {
            use source = new Microsoft.Data.SqlClient.SqlConnection(Result.value srcStrR)
            use sink   = new Microsoft.Data.SqlClient.SqlConnection(Result.value sinkStrR)
            do! source.OpenAsync()
            do! sink.OpenAsync()
            let! contractR = ReadSide.read source
            match contractR with
            | Error es -> return Result.failure es
            | Ok contract ->
                match TransferSpec.resolveReconciliation contract entries with
                | Error es -> return Result.failure es
                | Ok reconciliation ->
                    return! Transfer.runReconciling mode source sink contract reconciliation
        }
    let result = work.GetAwaiter().GetResult()
    let exitCode =
        match result with
        | Ok report ->
            narrateTransferReport report
            0
        | Error errors ->
            Console.Error.WriteLine "projection transfer: failed:"
            printErrors Console.Error errors
            if errors |> List.exists (fun (e: ValidationError) -> e.Code.StartsWith "transfer.reconcile.")
            then 2
            else 3
    dumpBench "transfer"
    exitCode

let private dispatchTransfer (argv: string[]) : int =
    argv |> VerbArgs.parse<TransferArgs.TransferArg> "projection transfer" (fun parsed ->
        let sourceSpec    = parsed.GetResult TransferArgs.Source_Conn
        let sinkSpec      = parsed.GetResult TransferArgs.Sink_Conn
        let reconcileList = parsed.GetResults TransferArgs.Reconcile
        let execute       = parsed.Contains TransferArgs.Execute
        runTransfer sourceSpec sinkSpec reconcileList execute)

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
    | [| "skeleton"; inputPath; outputDir |] ->
        runSkeleton inputPath outputDir
    | [| "approve"; policyVersion; "--approver"; approver |] ->
        runApprove policyVersion approver None
    | [| "approve"; policyVersion; "--approver"; approver; "--rationale"; rationale |] ->
        runApprove policyVersion approver (Some rationale)
    | [| "deploy"; inputPath |] ->
        runDeploy inputPath
    | [| "canary"; sourceDdlPath |] ->
        runCanary sourceDdlPath
    | [| "transfer" |] ->
        Console.Error.WriteLine "projection transfer: --source-conn and --sink-conn required"
        Console.Error.WriteLine ""
        Console.Error.WriteLine "Run `projection transfer --help` for usage."
        1
    | arr when arr.Length >= 1 && arr.[0] = "transfer" ->
        dispatchTransfer (Array.skip 1 arr)
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
