module Projection.Cli.Faces.Transfer
// LINT-ALLOW-FILE: CLI run-face operator-facing prose + Voice payload boxing at the terminal CLI boundary; the structural surface is the typed MovementSpec / Intent / Voice catalog, BCL primitives only at this terminal text edge.

// The `transfer` face (Phase 11 Slice D) — bidirectional data-load + the reverse leg, with the disposition / load-plan narration.
// Extracted verbatim from the RunFaces wall (recon #3 — per-verb file split);
// zero behavior change. Uses the shared `Face` combinator + `nameOf` from
// `Faces.Common`.

open System
open System.Diagnostics
open System.IO
open Projection.Core
open Projection.Adapters.Sql
open Projection.Pipeline
open Projection.Targets.SSDT
open Projection.Cli
open Projection.Cli.OperatorConsole
open Projection.Cli.Faces.Common


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
// The reconciliation / integrity / load-plan reports are keyed by `SsKey`; the
// shared `nameOf` (so a real OSSYS estate doesn't render as a wall of hex) now
// lives in `Faces.Common` (opened above), shared with the extracted verify-data
// face — see its docstring.

/// The load-plan / cycle-FK / unmatched-identity narration, named by the transfer
/// report's own `Names` index (populated from the engine's contract catalog;
/// empty ⇒ `rootOriginal` fallback, byte-identical to pre-displayName behaviour).
let narrateTransferReport (report: Transfer.TransferReport) : unit =
    let nm = nameOf report.Names
    // §4 Move — lead with the finding (rows moved, in dependency order); the
    // load plan rides beneath. Dependency order is the engine's guarantee that
    // a row never lands before the rows it points to.
    //
    // 2026-07-06 (the live rehearsal): a DRY RUN's headline counts the rows
    // that WOULD move (`RowsIngested` — a preview writes nothing by
    // definition, so the written sum read "0 rows would move" over a
    // 4-row forecast — actively misleading). An Execute's headline stays the
    // written sum (the actual outcome).
    let totalWritten = report.Kinds |> List.sumBy (fun k -> k.RowsWritten)
    let headlineCount =
        match report.Mode with
        | Transfer.DryRun  -> report.Kinds |> List.sumBy (fun k -> k.RowsIngested)
        | Transfer.Execute -> totalWritten
    let verdictCode = match report.Mode with Transfer.DryRun -> "transfer.previewPlan" | Transfer.Execute -> "transfer.applied"
    // The plan narration shows the tables the run TOUCHES (rows in flight or
    // a reconcile rule); the untouched remainder collapses to one line — 13
    // all-zero rows around the 2 that matter was noise, not information.
    let touched, untouched =
        report.Kinds
        |> List.partition (fun k ->
            k.RowsIngested > 0 || k.RowsWritten > 0
            || k.Disposition = IdentityDisposition.ReconciledByRule)
    let verdictPayload : Voice.Payload = Map.ofList [ "rowCount", box headlineCount; "tableCount", box touched.Length ]
    TtyRenderer.renderVoicedTo Console.Out verdictCode verdictPayload
    printfn ""
    printfn "The load plan (%d table(s) touched):" touched.Length
    for k in touched do
        printfn "  %-40s %-22s ingested=%d written=%d deferred-fk-columns=%d"
            (nm k.Kind)
            (dispositionName k.Disposition)
            k.RowsIngested
            k.RowsWritten
            (Set.count k.DeferredFkColumns)
    if not (List.isEmpty untouched) then
        printfn "  (%d other modeled table(s): no rows to move, not reconciled — untouched.)" untouched.Length
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

/// After a successful EXECUTE, the undo artifact (`transfer-undo.sql`,
/// written by the engine's success tail into the revert dir) is the
/// deliberate-revert half of the proving loop — point the operator at it.
let private narrateUndoPointer (mode: Transfer.Mode) (revertOut: string option) : unit =
    match mode, revertOut with
    | Transfer.Execute, Some dir ->
        let path = System.IO.Path.Combine(dir, "transfer-undo.sql")
        if System.IO.File.Exists path then
            printfn ""
            printfn "Undo script written: %s" path
            printfn "  Revert this run with: PROJECTION_ALLOW_EXECUTE=1 projection revert --script %s --against <sink-environment> --go" path
    | _ -> ()

/// The ONE reconcile/user-map input parse (final-pass consolidation,
/// 2026-07-06: four near-identical copies collapsed). Parses the
/// `--reconcile` specs and the optional `--user-map` CSV, aggregating every
/// spec error. `deferMissingUserMap` preserves the peer face's gate-scoping
/// contract: a missing file parses as EMPTY there (the delegate re-parses
/// and refuses `transfer.userMap.fileMissing` byte-identically); every
/// other caller refuses the missing file here.
let private parseReconcileInputs
    (reconcileSpecs: string list)
    (userMapPath: string option)
    (deferMissingUserMap: bool)
    : Result<TransferSpec.ReconcileEntry list * TransferSpec.UserMapEntry list> =
    let parsedReconciles = reconcileSpecs |> List.map TransferSpec.parseReconcileSpec
    let parsedUserMap : Result<TransferSpec.UserMapEntry list> =
        match userMapPath with
        | None -> Result.success []
        | Some path ->
            if not (System.IO.File.Exists path) then
                if deferMissingUserMap then Result.success []
                else
                    Result.failureOf
                        (ValidationError.create "transfer.userMap.fileMissing"
                            (sprintf "user-map file '%s' not found." path))
            else TransferSpec.parseUserMapCsv (System.IO.File.ReadAllText path)
    let errors =
        (parsedReconciles |> List.collect (function Ok _ -> [] | Error es -> es))
        @ (match parsedUserMap with Ok _ -> [] | Error es -> es)
    if not (List.isEmpty errors) then Result.failure errors
    else
        Result.success
            (parsedReconciles |> List.choose (function Ok e -> Some e | _ -> None),
             (match parsedUserMap with Ok es -> es | _ -> []))

/// 6.A.1 — the drop-set is fail-loud, not exit-0. A successful write that
/// dropped FK-orphan rows or left reconciled-kind sources unmatched surfaces
/// a distinct non-zero exit unless the operator declared the drops
/// acceptable via --allow-drops; the dropped/unmatched kinds are narrated.
let private narrateDropExit (allowDrops: bool) (report: Transfer.TransferReport) : int =
    let dropCode = Transfer.exitCodeForReport allowDrops report
    if dropCode <> 0 then
        TtyRenderer.renderVoicedTo Console.Error "transfer.rowsDropped"
            (Map.ofList [ "droppedCount", box (Transfer.droppedRowCount report) ] : Voice.Payload)
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
    (sinkResidentResume: bool)
    : int =
    // 2026-07-06 (the parity sweep): the FORWARD leg's --resumable rides the
    // same G10 sink-resident progress table the reverse leg's does — and had
    // the same unguarded raw CREATE TABLE crash on a managed sink. The same
    // named refusal, before any connection opens.
    if resumable && not sinkResidentResume then
        TtyRenderer.renderVoicedError
            (ValidationError.create "transfer.reverseLeg.resumableSinkUnsupported"
                "--resumable keeps its G10 marker in a sink-resident progress table, which needs CREATE TABLE the sink's data grant forbids (archetype managed-dml/undeclared). Drop --resumable (a wipe-and-load re-run is idempotent by construction), or declare the sink archetype full-rights if it truly can host the table.")
        dumpBench "transfer"
        2
    else
    let collect = function Ok _ -> [] | Error es -> es
    let parsedSource    = TransferSpec.parseConnectionSpec sourceSpec
    let parsedSink      = TransferSpec.parseConnectionSpec sinkSpec
    let parsedInputs    = parseReconcileInputs reconcileSpecs userMapPath false
    let specErrors =
        collect parsedSource
        @ collect parsedSink
        @ (match parsedInputs with Ok _ -> [] | Error es -> es)
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
    let entries, userMapEntries = Result.value parsedInputs
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
            narrateUndoPointer mode revertOut
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
    // per-pair flip — the run's own exit stands. Since 2026-07-02 the advisory
    // lines render VOICED in the dispatch prologue (`runPlan`), before any Live
    // region — never raw stderr from inside a face.
    // --pretty + a real TTY → the live data-load board (§13); the transfer leg
    // streams the "load" stage with per-table progress. Only on a real --execute
    // (a dry-run writes no rows, so the load stage would never advance).
    Face.staged "transfer" executeGated Spines.transfer runBody

// ---------------------------------------------------------------------------
// J3 closed — the `legacy` B→A reverse-leg face (THE_DATA_PRODUCERS §6 LE-1).
// The two SsKey-aligned contracts arrive RENDERED from the one authored model
// (`CatalogRendition`, produced at the dispatch arm); the face owns the
// operator gates — the execute env-gate, and a NAMED refusal for
// reconcile/rekey (the reconcile + rename combination is the documented
// follow-on; refusing is the honest boundary, never a silent straight-load) —
// and drives `Transfer.runReverseLegThroughConnections` through the apparatus.
// ---------------------------------------------------------------------------

/// The shared two-contract face body: the reverse leg (`legacy`, logical→
/// physical renditions of ONE authored model) and the peer leg (A→A, two
/// OSSYS-read per-environment contracts) drive the SAME engine path — two
/// SsKey-aligned contracts, reads with source names, writes with sink names.
/// `faceLabel` owns the operator-facing error prefix (THE_VOICE: the verb
/// presents in its own words).
let runContractPairTransfer
    (faceLabel: string)
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
    : int =
    let collect = function Ok _ -> [] | Error es -> es
    let parsedSource = TransferSpec.parseConnectionSpec sourceSpec
    let parsedSink   = TransferSpec.parseConnectionSpec sinkSpec
    // Phase 2 (the charter): reconcile on the reverse leg is no longer
    // refused — the User family re-keys by business key on the up-leg. The
    // specs parse exactly as the forward face's; the named refusal that stood
    // here is lifted (DECISIONS 2026-06-15 — reconcile ∘ reverse leg).
    let parsedInputs = parseReconcileInputs reconcileSpecs userMapPath false
    let specErrors =
        collect parsedSource @ collect parsedSink
        @ (match parsedInputs with Ok _ -> [] | Error es -> es)
    if not (List.isEmpty specErrors) then
        Console.Error.WriteLine (faceLabel + ": argument error:")
        printErrors Console.Error specErrors
        dumpBench "transfer"
        2
    else

    // Resolve the reconcile / user-map specs against the PHYSICAL sink
    // contract (the rendition the reverse leg writes into; `findKindByTable`
    // matches physical names, consistent with the forward face's live-read
    // contract). A bad spec refuses by name before any connection opens.
    let entries, userMapEntries = Result.value parsedInputs
    let reconcile      = not (List.isEmpty entries) || not (List.isEmpty userMapEntries)
    match TransferSpec.resolveAllReconciliation physicalSinkContract entries userMapEntries with
    | Error es ->
        Console.Error.WriteLine (faceLabel + ": reconcile resolution error:")
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
        Console.Error.WriteLine (faceLabel + ": apparatus invariant violation:")
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
            narrateUndoPointer mode revertOut
            narrateDropExit allowDrops report
        | Error errors ->
            printErrors Console.Error errors
            (Preflight.refusalOf errors).ExitCode
    // G0c — the advisory capability survey renders in the dispatch prologue
    // (voiced, pre-Live) since 2026-07-02; same posture as the peer transfer.
    Face.staged "transfer" executeGated Spines.transfer runBody

/// The reverse-leg face under its own name — the pre-2026-07-06 signature,
/// byte-identical behavior; the body is the shared `runContractPairTransfer`.
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
    : int =
    runContractPairTransfer "projection move (reverse leg)"
        sourceSpec sinkSpec logicalSourceContract physicalSinkContract
        reconcileSpecs userMapPath executeRequested allowCdc allowDrops
        emission resumable streaming journalDirectory tables
        revertPolicy revertDir sinkCapability

// ---------------------------------------------------------------------------
// The peer (A→A) face — the QA→UAT partial transfer with differing physical
// names (the 2026-07-06 partial-transfer readiness program). Two deployed
// cells of ONE model: each side's contract is read from its OWN OSSYS
// metamodel (`PeerTransfer.acquireContracts` — native GUID SsKeys, the
// espace-invariance law), so the pair aligns by identity without an authored
// model in the loop. Two pre-write gates ride the pair before the shared
// contract-pair body runs:
//   - the SHAPE gate — SS_KEY-keyed schema compatibility over the kinds this
//     run touches (`transfer.peer.shapeDivergence`, exit 5; advisories
//     surface, never silent), and
//   - the SUBSET-FK gate — FK edges escaping a declared `tables` subset each
//     get a proposed strategy (reconcile against rows the sink already holds;
//     widen the subset; --allow-drops); a live Execute with un-strategized
//     escapes refuses by name (`transfer.peer.subsetFkEscapes`, exit 9), a
//     preview narrates the proposals instead.
// ---------------------------------------------------------------------------

let runPeerTransfer
    (sourceSpec: string)
    (sinkSpec: string)
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
    : int =
    // Acquire the two SsKey-aligned contracts (the one I/O seam this face
    // adds). An unreadable OSSYS metamodel refuses on the schema-read axis
    // (exit 6) before any gate or connection-opening work.
    match (PeerTransfer.acquireContracts sourceSpec sinkSpec).GetAwaiter().GetResult() with
    | Error errors ->
        // The gate surface owns the copy (§5): `source.ossys.*` classifies
        // onto the schema-read axis, so the operator gets the statement +
        // the next move, never the flat GenericStop wall.
        TtyRenderer.renderGate "projection transfer (peer)" (Preflight.refusalOf errors)
        dumpBench "transfer"
        (Preflight.refusalOf errors).ExitCode
    | Ok (sourceContract, sinkContract) ->

    // Resolve the gate inputs: the declared subset (against the SOURCE
    // contract — the same resolver the engine uses) and the reconciled kind
    // set (against the SINK contract — the same resolution the shared body
    // performs). 2026-07-06 (adversarial MEDIUM #7): a reconcile spec that
    // fails to parse/resolve REFUSES HERE, before the gates — the prior
    // swallow-to-empty let the subset-FK gate blame the operator's
    // (correctly-written but typo'd) strategy as a missing one, refusing
    // exit 9 "escapes" where the true problem was the bad spec (exit 2).
    let reconciledResolution : Result<Set<SsKey>> =
        // deferMissingUserMap: the delegate refuses fileMissing byte-identically.
        parseReconcileInputs reconcileSpecs userMapPath true
        |> Result.bind (fun (entries, userMapEntries) ->
            TransferSpec.resolveAllReconciliation sinkContract entries userMapEntries
            |> Result.map (fun reconciliation -> reconciliation |> Map.toSeq |> Seq.map fst |> Set.ofSeq))
    match reconciledResolution with
    | Error errors ->
        Console.Error.WriteLine "projection transfer (peer): reconcile resolution error:"
        printErrors Console.Error errors
        dumpBench "transfer"
        2
    | Ok reconciledKeys ->
    match Transfer.resolveLoadSet sourceContract tables with
    | Error errors ->
        // The subset itself failed to resolve — same refusal the engine
        // would give; surface it now, before any connection opens.
        Console.Error.WriteLine "projection transfer (peer): table subset error:"
        printErrors Console.Error errors
        dumpBench "transfer"
        (Preflight.refusalOf errors).ExitCode
    | Ok loadSet ->

    // Gate 1 — SS_KEY-keyed shape compatibility over the kinds this run
    // touches (the subset + its reconciled kinds; the whole estate when no
    // subset is declared). Blocking divergence refuses by name (exit 5);
    // advisories print to stderr and the run proceeds.
    let gateScope = loadSet |> Option.map (Set.union reconciledKeys)
    match PeerTransfer.shapeGate gateScope sourceContract sinkContract with
    | Error errors ->
        // §5 gate surface — `transfer.peer.shapeDivergence` carries its own
        // axis (ShapeDivergence, exit 5) and copy; never the GenericStop wall.
        TtyRenderer.renderGate "projection transfer (peer)" (Preflight.refusalOf errors)
        dumpBench "transfer"
        (Preflight.refusalOf errors).ExitCode
    | Ok advisories ->
    // Advisories land on STDOUT with the rest of the preview (the answer
    // channel): `projection golden > preview.txt` must capture the safety
    // information the operator reviews before authorizing a live write —
    // stderr-only advisories silently vanish from a redirected preview.
    if not (List.isEmpty advisories) then
        printfn "%d shape advisory(ies) — real divergence that does not block a data load:" advisories.Length
        advisories |> List.iter (fun line -> printfn "  %s" line)

    // Gate 2 — FK edges escaping the declared subset. A preview narrates the
    // per-edge strategy proposals; a live Execute with un-strategized escapes
    // refuses by name unless the operator declared the drop-set acceptable.
    let escapes =
        match loadSet with
        | Some s -> PeerTransfer.escapingFks sourceContract s reconciledKeys
        | None -> []
    if not (List.isEmpty escapes) then
        printfn "%d relationship(s) escape the declared table subset:" escapes.Length
        PeerTransfer.narrateEscapes escapes |> List.iter (fun line -> printfn "  %s" line)
    match PeerTransfer.subsetFkGate executeRequested escapes with
    | Error errors ->
        // §5 gate surface — `transfer.peer.subsetFkEscapes` (SubsetFkEscape,
        // exit 9) with the reconcile/widen/allow-drops next move.
        TtyRenderer.renderGate "projection transfer (peer)" (Preflight.refusalOf errors)
        dumpBench "transfer"
        (Preflight.refusalOf errors).ExitCode
    | Ok () ->

    // The decision board is one verb away — point every preview at it (the
    // board re-derives the same gates PLUS the live probes and the row
    // forecast, with the red/green verdict).
    if not executeRequested then
        printfn ""
        printfn "The go board (open decisions + row forecast + red/green verdict): projection check go <flow>"

    // The shared contract-pair body: realization selector, execute/journal
    // gates, apparatus, engine run, narration — identical to the reverse leg,
    // with the peer pair as the contracts and the peer label on the prose.
    runContractPairTransfer "projection transfer (peer)"
        sourceSpec sinkSpec sourceContract sinkContract
        reconcileSpecs userMapPath executeRequested allowCdc allowDrops
        emission resumable streaming journalDirectory tables
        revertPolicy revertDir sinkCapability

// ---------------------------------------------------------------------------
// `projection revert` (2026-07-06, the proving-loop program) — the DELIBERATE
// UNDO. A successful transfer writes `transfer-undo.sql` (the precise
// DELETE-by-captured-key script, children first, pre-existing rows never
// touched); a failed run writes `transfer-revert.sql`. This verb previews
// (default) or executes either artifact against a configured environment —
// so a small declared subset can be transferred, verified, and reverted as
// one proving loop. A live run needs PROJECTION_ALLOW_EXECUTE=1 + --go and
// runs in ONE transaction (all deletes land or none do).
// ---------------------------------------------------------------------------

let runRevertScript (scriptPath: string) (envLabel: string) (connSpec: string) (goRequested: bool) (force: bool) : int =
    if not (System.IO.File.Exists scriptPath) then
        TtyRenderer.renderVoicedError
            (ValidationError.create "revert.scriptMissing"
                (sprintf "revert script '%s' not found. A successful transfer writes transfer-undo.sql (a failed one transfer-revert.sql) into its --revert-dir (default: the working directory); point --script at it." scriptPath))
        2
    else
    let allLines =
        System.IO.File.ReadAllLines scriptPath
        |> Array.map (fun l -> l.Trim())
    // The provenance header the artifact writer stamps (the wrong-sink
    // guard): `-- projection:<kind> server=<s> database=<db> generated=<t>`.
    let stampedDatabase =
        allLines
        |> Array.tryPick (fun l ->
            if l.StartsWith "-- projection:" then
                l.Split(' ')
                |> Array.tryPick (fun tok ->
                    if tok.StartsWith "database=" then Some (tok.Substring 9) else None)
            else None)
    let statements =
        allLines
        |> Array.filter (fun l ->
            l <> ""
            && not (l.StartsWith "--")
            && not (l.Equals("GO", System.StringComparison.OrdinalIgnoreCase)))
        |> Array.toList
    if List.isEmpty statements then
        printfn "revert: '%s' carries no statements — nothing to undo." scriptPath
        0
    else
    // The per-table summary: each statement is one chunked
    // `DELETE FROM <table> WHERE <pk> IN (k, k, …);` — table from the text,
    // key count from the IN list's commas (display only; execution runs the
    // statements verbatim).
    let tableOf (stmt: string) : string =
        let afterFrom = stmt.IndexOf "DELETE FROM "
        if afterFrom < 0 then "(statement)"
        else
            let rest = stmt.Substring(afterFrom + 12)
            match rest.IndexOf " WHERE " with
            | -1 -> rest.Trim()
            | i -> rest.Substring(0, i).Trim()
    let keyCountOf (stmt: string) : int =
        match stmt.IndexOf " IN (" with
        | -1 -> 0
        | i ->
            let inner = stmt.Substring(i + 5)
            match inner.LastIndexOf ')' with
            | -1 -> 0
            | j -> (inner.Substring(0, j).Split(',')).Length
    let summary =
        statements
        |> List.groupBy tableOf
        |> List.map (fun (t, ss) -> t, ss |> List.sumBy keyCountOf)
    printfn "Revert '%s' against %s — %d statement(s) over %d table(s):" scriptPath envLabel statements.Length summary.Length
    for (t, keys) in summary do
        printfn "  %-60s %d row(s) to delete" t keys
    let executeGated =
        goRequested && System.Environment.GetEnvironmentVariable "PROJECTION_ALLOW_EXECUTE" = "1"
    if goRequested && not executeGated then
        TtyRenderer.renderVoicedError (ValidationError.create "gate.intent" "PROJECTION_ALLOW_EXECUTE is not set in the environment.")
        dumpBench "revert"
        7
    elif not goRequested then
        printfn ""
        printfn "Preview only — no rows deleted. Execute with: PROJECTION_ALLOW_EXECUTE=1 projection revert --script %s --against %s --go" scriptPath envLabel
        0
    else
        match (ConnectionSpec.openSpec SubstrateRole.Sink "revert-sink" connSpec).GetAwaiter().GetResult() with
        | Error errors ->
            printErrors Console.Error errors
            dumpBench "revert"
            6
        | Ok sink ->
            use sink = sink
            // THE WRONG-SINK GUARD (2026-07-06, the final-pass critique's
            // top finding): the artifact deletes BY KEY in whatever database
            // --against resolves to. The header stamped at capture time
            // names the database the keys belong to; a mismatch refuses by
            // name (--force is the deliberate override — e.g. a restored
            // copy under a new name). A header-less artifact (pre-stamp)
            // proceeds with a printed note, never a refusal.
            match stampedDatabase with
            | Some stamped when not (System.String.Equals(stamped, sink.Database, System.StringComparison.OrdinalIgnoreCase)) && not force ->
                TtyRenderer.renderVoicedError
                    (ValidationError.create "revert.sinkMismatch"
                        (sprintf "this undo was captured against database '%s', but --against resolves to '%s'. Re-point --against at the environment the transfer wrote, or pass --force if the database was deliberately renamed/restored." stamped sink.Database))
                dumpBench "revert"
                7
            | _ ->
            if Option.isNone stampedDatabase then
                printfn "  (note: the script carries no provenance header — cannot verify it matches this sink.)"
            // ONE transaction: the undo lands whole or not at all (the
            // child-first order means FKs never block; a mid-script failure
            // rolls the earlier deletes back rather than leaving a
            // half-undone sink).
            use tx = sink.BeginTransaction()
            try
                let mutable total = 0
                let perTable = System.Collections.Generic.Dictionary<string, int>()
                for stmt in statements do
                    use cmd = sink.CreateCommand()
                    cmd.Transaction <- tx
                    cmd.CommandText <- stmt
                    let affected = cmd.ExecuteNonQuery()
                    total <- total + affected
                    let t = tableOf stmt
                    perTable.[t] <- (match perTable.TryGetValue t with | true, n -> n | _ -> 0) + affected
                tx.Commit()
                printfn ""
                if total = 0 then
                    printfn "Nothing to revert — the captured rows are already absent (a prior revert, or the sink was cleared)."
                else
                    printfn "Reverted — %d row(s) deleted:" total
                    for KeyValue (t, n) in perTable do
                        printfn "  %-60s %d row(s)" t n
                dumpBench "revert"
                0
            with ex ->
                (try tx.Rollback() with _ -> ())
                TtyRenderer.renderVoicedError
                    (ValidationError.create "revert.failed"
                        (sprintf "revert failed and ROLLED BACK (no rows deleted): %s" ex.Message))
                dumpBench "revert"
                3

// ---------------------------------------------------------------------------
// THE GO BOARD (2026-07-06, the preview-engine program) — `projection check
// go <flow>`: the ONE surface that forecasts a live run and flags every OPEN
// DECISION, red until each is resolved, green when the flow is genuinely
// executable. The runner: acquire the two contracts, run every pure gate the
// live run would hit, run the ENGINE DRY RUN (real reads, zero writes — the
// row-count forecast, the unmatched-identity forecast, the drop forecast),
// probe the sink (CDC posture, grant evidence, re-run hazards), and render
// the checklist with a total red/green verdict. Exit 0 green / 5 red — the
// board is CI-able.
// ---------------------------------------------------------------------------

let private probeCount (cnn: Microsoft.Data.SqlClient.SqlConnection) (sql: string) : int =
    use cmd = cnn.CreateCommand()
    cmd.CommandText <- sql
    System.Convert.ToInt32 (cmd.ExecuteScalar())

let runCheckGo (flowName: string) (fromLabel: string) (toLabel: string) (asJson: bool) (planned: PlanAction) : int =
    let finish (items: GoBoard.Item list) : int =
        let board : GoBoard.Board = { Flow = flowName; From = fromLabel; To = toLabel; Items = items }
        if asJson then printfn "%s" (GoBoard.toJsonString board)
        else GoBoard.render board |> List.iter (printfn "%s")
        GoBoard.exitCode board
    match planned with
    | PlanAction.Refused (_, e) ->
        finish [ GoBoard.item "routing" (GoBoard.Status.Red (sprintf "the flow does not plan: %s" e.Message, "fix projection.json (the flow/environment definition) and re-run.")) ]
    | PlanAction.Transfer (_, _, _, _) ->
        finish
            [ GoBoard.item "routing"
                (GoBoard.Status.Red
                    ("this env->env data flow rides the NAME-BLIND transfer (renditions unset) — it assumes matching physical table names on both sides.",
                     "set \"rendition\": \"physical\" on BOTH environments in projection.json to run the SsKey-aligned peer leg (shape + relationship gates), then re-run.")) ]
    | PlanAction.TransferPeer (sourceSpec, sinkSpec, opts, _) ->
        let items = ResizeArray<GoBoard.Item>()
        items.Add (GoBoard.item "routing" (GoBoard.Status.Green "the SsKey-aligned peer leg (both renditions physical)."))
        // -- contracts (the two OSSYS metamodel reads) ---------------------
        match (PeerTransfer.acquireContracts sourceSpec sinkSpec).GetAwaiter().GetResult() with
        | Error errors ->
            items.Add (GoBoard.itemWith "contracts"
                (GoBoard.Status.Red
                    ("a metamodel could not be read — the run has no contract to align on.",
                     "check the connection reference and the principal's SELECT on the ossys_* tables; then re-run."))
                (errors |> List.map (fun e -> sprintf "%s: %s" e.Code e.Message)))
            finish (List.ofSeq items)
        | Ok (sourceContract, sinkContract) ->
        items.Add (GoBoard.item "contracts" (GoBoard.Status.Green "both metamodels read; identities align by SS_KEY."))
        // -- the declared subset -------------------------------------------
        match Transfer.resolveLoadSet sourceContract opts.Tables with
        | Error errors ->
            items.Add (GoBoard.itemWith "tables"
                (GoBoard.Status.Red ("the declared table subset does not resolve.", "fix the flow's `tables` list (use Module.Entity for ambiguous names); then re-run."))
                (errors |> List.map (fun e -> e.Message)))
            finish (List.ofSeq items)
        | Ok loadSet ->
        items.Add (GoBoard.item "tables"
            (GoBoard.Status.Green
                (match loadSet with
                 | Some s -> sprintf "%d table(s) declared; all resolve." (Set.count s)
                 | None -> "no subset declared — the whole modeled estate transfers.")))
        // -- reconcile strategy resolution ----------------------------------
        let reconcileResolution =
            parseReconcileInputs opts.Reconcile opts.Rekey false
            |> Result.bind (fun (entries, userMapEntries) ->
                TransferSpec.resolveAllReconciliation sinkContract entries userMapEntries)
        match reconcileResolution with
        | Error errors ->
            items.Add (GoBoard.itemWith "reconcile"
                (GoBoard.Status.Red ("a reconcile/user-map entry does not resolve against the sink.", "fix the entry (Module.Entity:Column is the espace-safe form) or the user-map file; then re-run."))
                (errors |> List.map (fun e -> e.Message)))
            finish (List.ofSeq items)
        | Ok reconciliation ->
        let reconciledKeys = reconciliation |> Map.toSeq |> Seq.map fst |> Set.ofSeq
        items.Add (GoBoard.item "reconcile"
            (GoBoard.Status.Green
                (match Map.count reconciliation with
                 | 0 -> "no reconcile rules declared yet."
                 | n -> sprintf "%d reconcile rule(s) resolve against the sink." n)))
        // -- schema shape ----------------------------------------------------
        let gateScope = loadSet |> Option.map (Set.union reconciledKeys)
        let shape = PeerTransfer.shapeVerdict gateScope sourceContract sinkContract
        if not (List.isEmpty shape.Blocking) then
            items.Add (GoBoard.itemWith "shape"
                (GoBoard.Status.Red (sprintf "%d blocking schema divergence(s) over the transferred set." shape.Blocking.Length, "align the models (deploy the same version to both environments) or narrow the subset; then re-run."))
                shape.Blocking)
        elif not (List.isEmpty shape.Advisory) then
            items.Add (GoBoard.itemWith "shape"
                (GoBoard.Status.Advisory (sprintf "the two models are one shape over the transferred set, with %d advisory difference(s) that do not block a data load." shape.Advisory.Length))
                shape.Advisory)
        else
            items.Add (GoBoard.item "shape" (GoBoard.Status.Green "the two models are one shape over the transferred set."))
        // -- escaping relationships (the OPEN-DECISION axis) ----------------
        let escapes =
            match loadSet with
            | Some s -> PeerTransfer.escapingFks sourceContract s reconciledKeys
            | None -> []
        if not (List.isEmpty escapes) then
            items.Add (GoBoard.itemWith "relationships"
                (GoBoard.Status.Red (sprintf "%d relationship(s) escape the subset — each needs a decision." escapes.Length, "add the proposed reconcile entr(ies) to the flow, or widen `tables`; then re-run."))
                (PeerTransfer.narrateEscapes escapes))
        else
            items.Add (GoBoard.item "relationships" (GoBoard.Status.Green "every relationship the subset touches is inside it or reconciled."))
        // -- load order (the whole-estate topology) --------------------------
        let topo = (Projection.Core.Passes.TopologicalOrderPass.runWith Projection.Core.TreatAsCycle sinkContract).Value
        (match Transfer.orderedLoadGate topo with
         | Some refusal ->
             items.Add (GoBoard.itemWith "load order"
                 (GoBoard.Status.Red ("the load order degraded to the alphabetical fallback — a live run refuses.", "make the named cycle's FK columns nullable (they then defer automatically), or transfer without the affected kinds."))
                 [ refusal.Message ])
         | None ->
             items.Add (GoBoard.item "load order" (GoBoard.Status.Green "parents before children, proven (topological).")))
        // -- THE DRY RUN (real reads, zero writes): the row/identity forecast -
        let shapeClean = List.isEmpty shape.Blocking
        if not shapeClean then
            items.Add (GoBoard.item "forecast" (GoBoard.Status.Red ("not run — the shape divergence above would make the read/write plan unreliable.", "resolve the shape line, then re-run for the row forecast.")))
        else
            let dryRun () =
                // `ConnectionRef.Raw` (DECISIONS 2026-07-06): the resolved
                // spec is already in memory — no temp-file round trip.
                let refOf (spec: string) =
                    match TransferSpec.parseConnectionSpec spec with
                    | Ok r -> r
                    | Error _ -> ConnectionRef.Raw spec
                let srcSub : Substrate = { Environment = parseEnvironment "Source" (Some fromLabel); Role = SubstrateRole.Source; ConnectionRef = refOf sourceSpec }
                let sinkSub : Substrate = { Environment = parseEnvironment "Sink" (Some toLabel); Role = SubstrateRole.Sink; ConnectionRef = refOf sinkSpec }
                match TransferConnections.create srcSub sinkSub (not (Map.isEmpty reconciliation)) with
                | Error es -> Error es
                | Ok connections ->
                    (Transfer.runReverseLegThroughConnections
                        Transfer.DryRun opts.Emission false true false
                        opts.Tables connections sourceContract sinkContract reconciliation)
                        .GetAwaiter().GetResult()
            match dryRun () with
            | Error errors ->
                items.Add (GoBoard.itemWith "forecast"
                    (GoBoard.Status.Red ("the dry run refused.", "resolve the named refusal; then re-run."))
                    (errors |> List.map (fun e -> sprintf "%s: %s" e.Code e.Message)))
            | Ok report ->
                let nm = nameOf report.Names
                let forecastLines =
                    report.Kinds
                    |> List.filter (fun k -> k.RowsIngested > 0 || k.Disposition = IdentityDisposition.ReconciledByRule)
                    |> List.map (fun k -> sprintf "%s: %d row(s) will transfer (%s)" (nm k.Kind) k.RowsIngested (dispositionName k.Disposition))
                items.Add (GoBoard.itemWith "forecast"
                    (GoBoard.Status.Green (sprintf "dry run complete — %d row(s) across %d table(s) would transfer." (report.Kinds |> List.sumBy (fun k -> k.RowsIngested)) (report.Kinds |> List.filter (fun k -> k.RowsIngested > 0) |> List.length)))
                    forecastLines)
                if not (List.isEmpty report.UnbreakableCycleFks) then
                    items.Add (GoBoard.itemWith "cycles"
                        (GoBoard.Status.Red (sprintf "%d relationship cycle(s) cannot be broken — the load cannot run as planned." report.UnbreakableCycleFks.Length, "make the cycle's FK columns nullable, or exclude the affected kinds."))
                        (report.UnbreakableCycleFks |> List.map (fun u -> sprintf "%s.%s -> %s" (nm u.Kind) (Name.value u.Column) (nm u.Target))))
                if not (List.isEmpty report.UnmatchedIdentities) then
                    items.Add (GoBoard.itemWith "identities"
                        (GoBoard.Status.Red (sprintf "%d source identit(ies) have no sink match — a live run halts before any write." report.UnmatchedIdentities.Length, "remediate the user-map / reconcile data, or accept the loss with --allow-drops at run time."))
                        (report.UnmatchedIdentities |> List.map (fun (k, s) -> sprintf "%s source '%s'" (nm k) (SourceKey.value s))))
                elif not (Map.isEmpty reconciliation) then
                    items.Add (GoBoard.item "identities" (GoBoard.Status.Green "every reconciled source identity matches a sink row."))
                if not (List.isEmpty report.SkippedReferences) then
                    items.Add (GoBoard.itemWith "drops"
                        (GoBoard.Status.Red (sprintf "%d row(s) would drop — a relationship points at an unmatched record." report.SkippedReferences.Length, "fix the referenced data, or accept with --allow-drops at run time."))
                        (report.SkippedReferences |> List.truncate 10 |> List.map (fun (owner, r) -> sprintf "%s.%s -> %s (source '%s')" (nm owner) (Name.value r.Column) (nm r.Target) (SourceKey.value r.UnresolvedSource))))
        // -- sink probes: CDC posture, grant evidence, re-run semantics ------
        match (ConnectionSpec.openSpec SubstrateRole.Sink "check-go-sink" sinkSpec).GetAwaiter().GetResult() with
        | Error errors ->
            items.Add (GoBoard.itemWith "sink probes"
                (GoBoard.Status.Red ("the sink connection could not open for the CDC/grant/re-run probes.", "check the connection reference; then re-run."))
                (errors |> List.map (fun e -> e.Message)))
        | Ok sink ->
            use sink = sink
            (match (ReadSide.cdcTrackedTables sink).GetAwaiter().GetResult() with
             | Error errors ->
                 items.Add (GoBoard.itemWith "cdc" (GoBoard.Status.Red ("the CDC probe failed — a live run refuses rather than proceed blind.", "grant the probe's reads or resolve the error; then re-run.")) (errors |> List.map (fun e -> e.Message)))
             | Ok [] ->
                 items.Add (GoBoard.item "cdc" (GoBoard.Status.Green "the sink tracks no tables with CDC."))
             | Ok tracked ->
                 items.Add (GoBoard.itemWith "cdc"
                     (GoBoard.Status.Red (sprintf "the sink is CDC-tracked (%d table(s)) — a live run refuses without consent." tracked.Length, "run --go with --allow-cdc (the capture will see the load), or disable capture on the sink."))
                     (tracked |> List.truncate 5)))
            (match (Preflight.captureGrantEvidence sink).GetAwaiter().GetResult() with
             | Error errors ->
                 items.Add (GoBoard.itemWith "grant" (GoBoard.Status.Red ("the grant probe failed.", "check the sink connection/principal; then re-run.")) (errors |> List.map (fun e -> e.Message)))
             | Ok evidence ->
                 let missing =
                     [ "SELECT"; "INSERT"; "UPDATE"; "DELETE" ]
                     |> List.filter (fun p -> not (Preflight.coversPermissionAtDatabaseScope p evidence))
                 if List.isEmpty missing then
                     items.Add (GoBoard.itemWith "grant"
                         (GoBoard.Status.Green "the sink principal carries database-scope SELECT/INSERT/UPDATE/DELETE.")
                         [ "standing note: a TABLE-level DENY is invisible to this probe and would surface mid-load (the pinned G1 gap)." ])
                 else
                     items.Add (GoBoard.item "grant"
                         (GoBoard.Status.Red (sprintf "the sink principal lacks database-scope %s." (String.concat ", " missing), "grant the missing permission(s) to the sink principal; then re-run."))))
            // Re-run semantics over the ACTUAL sink state.
            let subsetKinds =
                match loadSet with
                | Some s -> Catalog.allKinds sinkContract |> List.filter (fun k -> Set.contains k.SsKey s)
                | None -> Catalog.allKinds sinkContract |> List.filter (fun k -> not (Set.contains k.SsKey reconciledKeys))
            let populated =
                subsetKinds
                |> List.choose (fun k ->
                    let n = probeCount sink (sprintf "SELECT COUNT_BIG(*) FROM [%s].[%s];" (TableId.schemaText k.Physical) (TableId.tableText k.Physical))
                    if n > 0 then Some (sprintf "%s: %d row(s)" (Name.value k.Name) n) else None)
            (match opts.Emission, populated with
             | EmissionMode.Incremental, (_ :: _) ->
                 items.Add (GoBoard.itemWith "re-run"
                     (GoBoard.Status.Red ("the sink already holds rows in the transferred set and the strategy is merge/incremental — a re-run would DUPLICATE sink-minted rows.", "set \"strategy\": \"replace\" on the flow (wipe-the-subset-then-load, idempotent), or clear the subset first."))
                     populated)
             | EmissionMode.WipeAndLoad, _ ->
                 // The wipe-blocker probe: out-of-subset sink children holding
                 // rows that reference an in-subset parent block the child-first
                 // DELETE with a raw 547 (the pinned wipe hazard).
                 let subsetSet = subsetKinds |> List.map (fun k -> k.SsKey) |> Set.ofList
                 let blockers =
                     Catalog.allKinds sinkContract
                     |> List.filter (fun k -> not (Set.contains k.SsKey subsetSet))
                     |> List.collect (fun child ->
                         child.References
                         |> List.filter (fun r -> Set.contains r.TargetKind subsetSet)
                         |> List.choose (fun r ->
                             child.Attributes
                             |> List.tryFind (fun a -> a.SsKey = r.SourceAttribute)
                             |> Option.bind (fun a ->
                                 let n = probeCount sink (sprintf "SELECT COUNT_BIG(*) FROM [%s].[%s] WHERE [%s] IS NOT NULL;" (TableId.schemaText child.Physical) (TableId.tableText child.Physical) (ColumnRealization.columnNameText a.Column))
                                 if n > 0 then Some (sprintf "%s.%s -> in-subset parent (%d referencing row(s))" (Name.value child.Name) (Name.value a.Name) n) else None)))
                 if List.isEmpty blockers then
                     items.Add (GoBoard.item "re-run" (GoBoard.Status.Green "strategy replace: the subset wipes child-first and reloads — idempotent; no out-of-subset rows block the wipe."))
                 else
                     items.Add (GoBoard.itemWith "re-run"
                         (GoBoard.Status.Red ("out-of-subset sink rows reference the tables the wipe would delete — the wipe would fail mid-way (FK 547).", "widen `tables` to include the referencing table(s), or clear those rows first."))
                         blockers)
             | _ ->
                 items.Add (GoBoard.item "re-run" (GoBoard.Status.Green "the transferred set is empty on the sink — first load; both strategies behave identically.")))
        // -- the run-time gates (never red here: they are per-run intent) ----
        let envGate = System.Environment.GetEnvironmentVariable "PROJECTION_ALLOW_EXECUTE" = "1"
        items.Add (GoBoard.item "execute gates"
            (GoBoard.Status.Advisory
                (if envGate then "PROJECTION_ALLOW_EXECUTE is set; the live run still needs the per-run intent flag --go."
                 else "two gates at run time: PROJECTION_ALLOW_EXECUTE=1 (environment authorization) + --go (per-run intent).")))
        finish (List.ofSeq items)
    | _ ->
        Console.Error.WriteLine (sprintf "projection check go: flow '%s' is not a live data-transfer flow (the go board covers env->env data flows)." flowName)
        2

// ---------------------------------------------------------------------------
