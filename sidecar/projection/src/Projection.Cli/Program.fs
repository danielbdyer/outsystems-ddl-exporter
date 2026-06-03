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
        "    projection policy-diff <config-a> <config-b>"
        "    projection migrate --from <model-a.json> --to <model-b.json> [--allow-drops]"
        "    projection approve <policy-version> --approver <name> [--rationale <text>] [--store <path>]"
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
        "    8  verify-data divergence (row-count / null-count diff non-empty)"
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

/// Parse an optional `--source-env` / `--sink-env` label into the
/// apparatus's `Environment`. The four named environments resolve
/// case-insensitively; anything else is a `Named` escape hatch; absence
/// keeps the default role-named label.
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

let private dispatchTransfer (argv: string[]) : int =
    argv |> VerbArgs.parse<TransferArgs.TransferArg> "projection transfer" (fun parsed ->
        let sourceSpec    = parsed.GetResult TransferArgs.Source_Conn
        let sinkSpec      = parsed.GetResult TransferArgs.Sink_Conn
        let sourceEnv     = parsed.TryGetResult TransferArgs.Source_Env
        let sinkEnv       = parsed.TryGetResult TransferArgs.Sink_Env
        let reconcileList = parsed.GetResults TransferArgs.Reconcile
        let userMap       = parsed.TryGetResult TransferArgs.User_Map
        let execute       = parsed.Contains TransferArgs.Execute
        let allowCdc      = parsed.Contains TransferArgs.Allow_Cdc
        let allowDrops    = parsed.Contains TransferArgs.Allow_Drops
        runTransfer sourceSpec sinkSpec sourceEnv sinkEnv reconcileList userMap execute allowCdc allowDrops)

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

let private dispatchVerifyData (argv: string[]) : int =
    argv |> VerbArgs.parse<VerifyDataArgs.VerifyDataArg> "projection verify-data" (fun parsed ->
        let beforeSpec = parsed.GetResult VerifyDataArgs.Before_Conn
        let afterSpec  = parsed.GetResult VerifyDataArgs.After_Conn
        runVerifyData beforeSpec afterSpec)

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
/// Parse the declared-loss gate's input from the raw argv: `--allow-drops`
/// accepts everything (`DeclareAll`); each `--declare-drop <token>` (repeatable)
/// names one accepted removal (`DeclareThese`); absent both, the safe default
/// `DeclareNone` refuses every destructive removal. The tokens are the
/// source-free `Migration.lossToken` form the refusal prints.
let private parseLossDeclaration (arr: string[]) : LossDeclaration =
    if Array.contains "--allow-drops" arr then DeclareAll
    else
        let tokens =
            arr
            |> Array.indexed
            |> Array.choose (fun (i, a) ->
                if a = "--declare-drop" && i + 1 < arr.Length then Some arr.[i + 1] else None)
            |> Array.toList
        match tokens with
        | [] -> DeclareNone
        | _  -> DeclareThese (Set.ofList tokens)

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
                                        let! outcome = MigrationRun.execute allowCdc declaration sourceA target cnn
                                        match outcome with
                                        | Ok o when o.Verified ->
                                            printfn "projection migrate: executed and VERIFIED — B' reproduces B (%d statement(s))"
                                                (List.length o.Artifacts.SchemaStatements)
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
/// --execute [--allow-drops] [--allow-cdc]` — the cross-substrate composition:
/// evolve the SINK's schema A→B in place, then transfer rows from the SOURCE
/// substrate into the migrated sink over contract B (straight load — the
/// `--reconcile` / `--user-map` re-key forms are the follow-on). Schema is
/// fail-loud + minimum-viable; the data leg runs only if the schema verified.
let private runMigrateWithData (toPath: string) (sinkSpec: string) (sourceSpec: string) (declaration: LossDeclaration) (allowCdc: bool) : int =
    if System.Environment.GetEnvironmentVariable "PROJECTION_ALLOW_EXECUTE" <> "1" then
        Console.Error.WriteLine
            "projection migrate: --execute requires PROJECTION_ALLOW_EXECUTE=1 in the environment (R6 gate). Refusing."
        dumpBench "migrate"
        7
    else
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
                                        let! outcome =
                                            MigrationRun.executeWithData declaration Transfer.Execute allowCdc
                                                sinkSourceA target Map.empty dataSource sink
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
        runApprove policyVersion approver None None
    | [| "approve"; policyVersion; "--approver"; approver; "--rationale"; rationale |] ->
        runApprove policyVersion approver (Some rationale) None
    | [| "approve"; policyVersion; "--approver"; approver; "--store"; store |] ->
        runApprove policyVersion approver None (Some store)
    | [| "approve"; policyVersion; "--approver"; approver; "--rationale"; rationale; "--store"; store |] ->
        runApprove policyVersion approver (Some rationale) (Some store)
    | [| "deploy"; inputPath |] ->
        runDeploy inputPath
    | [| "canary"; sourceDdlPath |] ->
        runCanary sourceDdlPath
    | [| "policy-diff"; configAPath; configBPath |] ->
        runPolicyDiff configAPath configBPath
    | arr when arr.Length >= 1 && arr.[0] = "migrate" && not (Array.contains "--execute" arr) ->
        let valueOf (flag: string) =
            arr
            |> Array.tryFindIndex ((=) flag)
            |> Option.bind (fun i -> if i + 1 < arr.Length then Some arr.[i + 1] else None)
        let declaration = parseLossDeclaration arr
        match valueOf "--to", valueOf "--from", valueOf "--store" with
        | Some toPath, Some fromPath, _ -> runMigratePreview fromPath toPath declaration
        | Some toPath, None, Some storePath -> runMigrateFromStore storePath toPath declaration
        | _ ->
            Console.Error.WriteLine
                "projection migrate (dry-run): needs --to <modelB.json> with either --from <modelA.json> (two-model) or --store <lifecycle.json> (snapshot⊖snapshot)."
            2
    | arr when arr.Length >= 1 && arr.[0] = "migrate" && Array.contains "--execute" arr ->
        // B1 — live execution. In-place (`--conn`) or cross-substrate
        // (`--sink-conn` + `--source-conn`, the data-load form). Flag-order-
        // independent parse.
        let valueOf (flag: string) =
            arr
            |> Array.tryFindIndex ((=) flag)
            |> Option.bind (fun i -> if i + 1 < arr.Length then Some arr.[i + 1] else None)
        let declaration = parseLossDeclaration arr
        let allowCdc = Array.contains "--allow-cdc" arr
        match valueOf "--to", valueOf "--source-conn", valueOf "--sink-conn", valueOf "--conn" with
        | Some toPath, Some sourceSpec, Some sinkSpec, _ ->
            runMigrateWithData toPath sinkSpec sourceSpec declaration allowCdc
        | Some toPath, None, None, Some connSpec ->
            // AC-P8 — optional durable provenance. `--lifecycle-store` (alias
            // `--store`) persists the episode after a verified execute; absent,
            // behavior is unchanged (a one-line note tells the operator nothing
            // was persisted). `--env` stamps the timeline / episode environment.
            let storePath =
                match valueOf "--lifecycle-store" with
                | Some _ as s -> s
                | None -> valueOf "--store"
            runMigrateExecute toPath connSpec declaration allowCdc storePath (valueOf "--env")
        | _ ->
            Console.Error.WriteLine
                "projection migrate --execute: in-place needs --to <modelB.json> --conn <ref>; cross-substrate needs --to --sink-conn --source-conn."
            2
    | [| "transfer" |] ->
        Console.Error.WriteLine "projection transfer: --source-conn and --sink-conn required"
        Console.Error.WriteLine ""
        Console.Error.WriteLine "Run `projection transfer --help` for usage."
        1
    | arr when arr.Length >= 1 && arr.[0] = "transfer" ->
        dispatchTransfer (Array.skip 1 arr)
    | [| "verify-data" |] ->
        Console.Error.WriteLine "projection verify-data: --before-conn and --after-conn required"
        Console.Error.WriteLine ""
        Console.Error.WriteLine "Run `projection verify-data --help` for usage."
        1
    | arr when arr.Length >= 1 && arr.[0] = "verify-data" ->
        dispatchVerifyData (Array.skip 1 arr)
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
