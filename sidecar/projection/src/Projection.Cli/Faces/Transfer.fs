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
    let totalWritten = report.Kinds |> List.sumBy (fun k -> k.RowsWritten)
    let verdictCode = match report.Mode with Transfer.DryRun -> "transfer.previewPlan" | Transfer.Execute -> "transfer.applied"
    let verdictPayload : Voice.Payload = Map.ofList [ "rowCount", box totalWritten; "tableCount", box report.Kinds.Length ]
    TtyRenderer.renderVoicedTo Console.Out verdictCode verdictPayload
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
        Console.Error.WriteLine (faceLabel + ": argument error:")
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
        Console.Error.WriteLine "projection transfer (peer): contract acquisition error:"
        printErrors Console.Error errors
        dumpBench "transfer"
        (Preflight.refusalOf errors).ExitCode
    | Ok (sourceContract, sinkContract) ->

    // Resolve the gate inputs: the declared subset (against the SOURCE
    // contract — the same resolver the engine uses) and the reconciled kind
    // set (against the SINK contract — the same resolution the shared body
    // performs). A spec that fails to parse/resolve here is NOT refused here:
    // the shared body reproduces the refusal byte-identically, so the gates
    // simply step aside (empty reconciled set / no subset) and delegate.
    let reconciledKeys : Set<SsKey> =
        let parsedReconciles = reconcileSpecs |> List.map TransferSpec.parseReconcileSpec
        let parsedUserMap =
            match userMapPath with
            | None -> Result.success []
            | Some path ->
                if not (System.IO.File.Exists path) then Result.success []
                else TransferSpec.parseUserMapCsv (System.IO.File.ReadAllText path)
        let entries = parsedReconciles |> List.choose (function Ok e -> Some e | Error _ -> None)
        match parsedUserMap with
        | Error _ -> Set.empty
        | Ok userMapEntries ->
            match TransferSpec.resolveAllReconciliation sinkContract entries userMapEntries with
            | Ok reconciliation -> reconciliation |> Map.toSeq |> Seq.map fst |> Set.ofSeq
            | Error _ -> Set.empty
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
        errors |> List.iter TtyRenderer.renderVoicedError
        dumpBench "transfer"
        (Preflight.refusalOf errors).ExitCode
    | Ok advisories ->
    if not (List.isEmpty advisories) then
        Console.Error.WriteLine (sprintf "projection transfer (peer): %d shape advisory(ies) — real divergence that does not block a data load:" advisories.Length)
        advisories |> List.iter (fun line -> Console.Error.WriteLine ("  " + line))

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
    match PeerTransfer.subsetFkGate executeRequested allowDrops escapes with
    | Error errors ->
        errors |> List.iter TtyRenderer.renderVoicedError
        dumpBench "transfer"
        (Preflight.refusalOf errors).ExitCode
    | Ok () ->

    // The shared contract-pair body: realization selector, execute/journal
    // gates, apparatus, engine run, narration — identical to the reverse leg,
    // with the peer pair as the contracts and the peer label on the prose.
    runContractPairTransfer "projection transfer (peer)"
        sourceSpec sinkSpec sourceContract sinkContract
        reconcileSpecs userMapPath executeRequested allowCdc allowDrops
        emission resumable streaming journalDirectory tables
        revertPolicy revertDir sinkCapability

// ---------------------------------------------------------------------------
