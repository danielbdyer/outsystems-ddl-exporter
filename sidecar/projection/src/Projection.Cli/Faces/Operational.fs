module Projection.Cli.Faces.Operational
// LINT-ALLOW-FILE: CLI run-face operator-facing prose + Voice payload boxing at the terminal CLI boundary; the structural surface is the typed MovementSpec / Intent / Voice catalog, BCL primitives only at this terminal text edge.

// The read-only operational verbs: verify-data / drift / eject / readiness /
// setup — extracted from the RunFaces wall (recon #3, the per-verb file split).
// Depends only on Pipeline run modules + the shared CLI helpers + `Faces.Common`
// (the `nameOf` spine the integrity narration shares with the transfer faces).
// Verbatim relocation — zero behavior change.

open System
open System.IO
open Projection.Core
open Projection.Adapters.Sql
open Projection.Pipeline
open Projection.Targets.SSDT
open Projection.Cli
open Projection.Cli.OperatorConsole
open Projection.Cli.Faces.Common

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
