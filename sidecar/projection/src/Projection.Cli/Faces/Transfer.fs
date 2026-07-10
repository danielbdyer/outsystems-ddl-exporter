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
open Projection.Adapters.OssysSql
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
/// The live-run report, rendered through the `View` engine (2026-07-08, the
/// rendering-elevation program): the load plan becomes a responsive
/// `View.Table` (Spectre auto-sizes it to the terminal), the cycle / unmatched
/// / drop sections become status-glyphed `Disclosure` blocks, and — when the
/// flow declared a `supportingScope` — the SAME References / Dependents
/// guarantee tree the go board shows closes the report ("the invariants that
/// held"). The verdict line stays the Voice-owned `renderVoicedTo`. `scopeGroups`
/// is the shared `SupportingScope.scopeGroups` output (empty for a run with no
/// declared scope — the generic `transfer` verb).
let narrateTransferReportWithScope (report: Transfer.TransferReport) (scopeGroups: GoBoard.ScopeGroup list) : unit =
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
    // The load plan as one responsive table — the engine measures each column
    // against the terminal budget, so the wide table-name column reflows.
    let planHeaders = [ "table"; "disposition"; "ingested"; "written"; "deferred FK" ]
    let planRow (k: Transfer.KindOutcome) : (string * View.Status) list =
        let deferred = Set.count k.DeferredFkColumns
        [ nm k.Kind, View.Neutral
          dispositionName k.Disposition, View.Neutral
          string k.RowsIngested, (if k.RowsIngested > 0 then View.Ok else View.Neutral)
          string k.RowsWritten, (if k.RowsWritten > 0 then View.Ok else View.Neutral)
          string deferred, (if deferred > 0 then View.Warn else View.Neutral) ]
    // A status-glyphed disclosure for a fault/advisory section — the headline
    // carries the count + verdict, the rows reveal beneath (fully expanded at
    // the board depth).
    let section (status: View.Status) (headline: string) (rows: string list) : View.View option =
        match rows with
        | [] -> None
        | _  -> Some (View.Disclosure (headline, status, rows |> List.map View.Note))
    let blocks : View.View list =
        [ yield View.Field ("load plan", sprintf "%d table(s) touched" touched.Length, View.Neutral)
          yield View.Table (planHeaders, touched |> List.map planRow)
          if not (List.isEmpty untouched) then
              yield View.Note (sprintf "%d other modeled table(s): no rows to move, not reconciled — untouched." untouched.Length)
          match section View.Bad
                    (sprintf "%d relationship cycle(s) cannot be broken — the load cannot run as planned" report.UnbreakableCycleFks.Length)
                    (report.UnbreakableCycleFks |> List.map (fun u -> sprintf "%s.%s -> %s" (nm u.Kind) (Name.value u.Column) (nm u.Target))) with
          | Some v -> yield v | None -> ()
          match section View.Warn
                    (sprintf "%d identity(ies) unmatched — source records with no match in the target" report.UnmatchedIdentities.Length)
                    (report.UnmatchedIdentities |> List.map (fun (k, s) -> sprintf "%s source '%s'" (nm k) (SourceKey.value s))) with
          | Some v -> yield v | None -> ()
          match section View.Warn
                    (sprintf "%d source record(s) had a non-unique reconcile key — the first binding was kept" report.AmbiguousIdentities.Length)
                    (report.AmbiguousIdentities |> List.map (fun (k, s) -> sprintf "%s source '%s'" (nm k) (SourceKey.value s))) with
          | Some v -> yield v | None -> ()
          match section View.Warn
                    (sprintf "%d target record(s) shared a reconcile key with an older record — the oldest was kept (supply an override if the wrong one won)" report.AmbiguousTargetMatchKeys.Length)
                    (report.AmbiguousTargetMatchKeys |> List.map (fun (k, a) -> sprintf "%s target '%s' (displaced)" (nm k) (AssignedKey.value a))) with
          | Some v -> yield v | None -> ()
          match section View.Bad
                    (sprintf "%d row(s) dropped — a relationship points to an unmatched record" report.SkippedReferences.Length)
                    (report.SkippedReferences |> List.map (fun (owner, r) -> sprintf "%s.%s -> %s (unmatched source '%s')" (nm owner) (Name.value r.Column) (nm r.Target) (SourceKey.value r.UnresolvedSource))) with
          | Some v -> yield v | None -> ()
          // NM-53 — a resumable G10 no-op re-run replays the prior run's drop
          // count (the marker persists the count, not the exact references), so
          // the re-run is not silently clean.
          match report.ReplayedPriorDrops with
          | Some n when n > 0 ->
              yield View.Note (sprintf "already complete; prior run dropped %d row(s) — re-surfacing that verdict (exact references not replayed)." n)
          | _ -> ()
          // The guarantee tree — when the flow declared a supporting scope, the
          // invariants that governed this run (the same lens `check go` shows).
          if not (List.isEmpty scopeGroups) then
              yield GoBoardView.scopeTree "supporting scope — the invariants that held" View.Ok scopeGroups [] ]
    GoBoardView.writeView Console.Out (View.Doc blocks)

/// The live-run report for the generic `transfer` verb (no declared supporting
/// scope) — the historical one-argument entry point every caller and test still
/// holds. Delegates to the scope-aware form with an empty guarantee tree.
let narrateTransferReport (report: Transfer.TransferReport) : unit =
    narrateTransferReportWithScope report []

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

/// After a FAILED execute (the `Error` path), point the operator at the
/// compensation artifact the engine's failure tail writes (`transfer-revert.sql`
/// — the DELETE-by-captured-key script for whatever partial rows landed before
/// the fault). The success tail has `narrateUndoPointer`; a crash previously left
/// the operator with a classified error and NO pointer to the undo, so they had
/// to already know the runbook. Printed on stderr (the error channel the failure
/// itself uses); silent when no artifact was written (a pre-write refusal that
/// touched nothing) — a pointer to a file that does not exist would mislead.
let private narrateCompensationPointer (mode: Transfer.Mode) (revertOut: string option) : unit =
    match mode, revertOut with
    | Transfer.Execute, Some dir ->
        let path = System.IO.Path.Combine(dir, "transfer-revert.sql")
        if System.IO.File.Exists path then
            eprintfn ""
            eprintfn "A partial write may have landed before the failure. Compensation script written: %s" path
            eprintfn "  Undo the partial rows with: PROJECTION_ALLOW_EXECUTE=1 projection revert --script %s --against <sink-environment> --go" path
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
let private narrateDropExit (allowDrops: bool) (signoffs: WriteSignoff.WriteApproval list) (report: Transfer.TransferReport) : int =
    // T1.6 — the drop-set is accepted by --allow-drops OR the flow's durable
    // `Drops` signoff; the exit code reads the SAME acknowledgement the engine's
    // pre-write gate does, so a config-greenlit run does not then exit-9.
    let dropCode = Transfer.exitCodeForReport (Transfer.dropsAcknowledged allowDrops signoffs) report
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
            narrateDropExit allowDrops [] report
        | Error errors ->
            printErrors Console.Error errors
            narrateCompensationPointer mode revertOut
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
    // 2026-07-08 — the flow's `reconcileIgnore` audit columns (attribute
    // names the reconciled-kind matched-pair diff skips).
    (reconcileIgnore: string list)
    // 2026-07-08 — the typed supporting scope; resolved here (against the sink
    // contract) into the seed / acknowledged-exclusion sets the write path
    // consumes. The reference/anchor entries already rode in via the desugared
    // `reconcileSpecs`/`tables`; this carries only the WRITE-plane markings.
    (supportingScope: SupportingScope.SupportingScopeEntry list)
    // 2026-07-08 — the flow's write-signoff greenlights; threaded onto the
    // engine's `WriteOptions.Signoffs` so a destructive Execute refuses BY
    // NAME (`transfer.writeSignoff.ungreenlit`) until the mode is greenlit.
    (signoff: WriteSignoff.WriteApproval list)
    // 2026-07-09 (T0.3) — the flow's `foreignRefs`: acknowledged out-of-contract
    // references, threaded to `WriteOptions.ForeignRefsAcknowledged`.
    (foreignRefs: string list)
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
    let reconcileIgnoreSet : Set<Name> =
        reconcileIgnore
        |> List.choose (fun n -> match Name.create n with Ok v -> Some v | Error _ -> None)
        |> Set.ofList
    let resolvedScope =
        match SupportingScope.resolve physicalSinkContract supportingScope with
        | Ok r -> r
        | Error _ -> SupportingScope.empty
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
                (Transfer.runStreamingReverseLegThroughConnections mode allowCdc allowDrops journal connections logicalSourceContract physicalSinkContract reconciliation reconcileIgnoreSet resolvedScope.StaticLookupKinds signoff revertAuto revertOut)
                    .GetAwaiter().GetResult()
            | ReverseLegRealization.Materialized ->
                (Transfer.runReverseLegThroughConnectionsWith sinkCapability.IdentityPolicy mode emission resumable allowCdc allowDrops tables connections logicalSourceContract physicalSinkContract reconciliation reconcileIgnoreSet resolvedScope.SeedKinds resolvedScope.AcknowledgedExclusions signoff resolvedScope.StaticLookupKinds (Set.ofList foreignRefs) revertAuto revertOut)
                    .GetAwaiter().GetResult()
        match result with
        | Ok report ->
            // The guarantee tree for a declared-scope flow — the same References /
            // Dependents hierarchy `check go` shows, now closing the run report
            // (the invariants that held). Empty when no `supportingScope` was
            // declared, so the tree only appears where it means something.
            let scopeGroups =
                if List.isEmpty supportingScope then []
                else
                    let payloadSet =
                        match Transfer.resolveLoadSet physicalSinkContract tables with
                        | Ok (Some s) -> s
                        | _ -> Set.empty
                    let baseReconciled = reconciliation |> Map.toSeq |> Seq.map fst |> Set.ofSeq
                    SupportingScope.scopeGroups physicalSinkContract payloadSet baseReconciled supportingScope
            narrateTransferReportWithScope report scopeGroups
            narrateUndoPointer mode revertOut
            narrateDropExit allowDrops signoff report
        | Error errors ->
            printErrors Console.Error errors
            narrateCompensationPointer mode revertOut
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
    (reconcileIgnore: string list)
    (supportingScope: SupportingScope.SupportingScopeEntry list)
    (signoff: WriteSignoff.WriteApproval list)
    // 2026-07-09 (T0.3) — the flow's `foreignRefs`: acknowledged out-of-contract
    // references, threaded to `WriteOptions.ForeignRefsAcknowledged`.
    (foreignRefs: string list)
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
    // Desugar supporting scope onto the terse string inputs (see runPeerTransfer).
    let scopeDesugar = SupportingScope.desugarToStrings supportingScope
    runContractPairTransfer "projection move (reverse leg)"
        sourceSpec sinkSpec logicalSourceContract physicalSinkContract
        (reconcileSpecs @ scopeDesugar.ExtraReconcile) reconcileIgnore supportingScope signoff foreignRefs userMapPath executeRequested allowCdc allowDrops
        emission resumable streaming journalDirectory (tables @ scopeDesugar.ExtraTables)
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
    // The snapshot scope BOTH contract reads run under — the dispatcher
    // binds it from the projection.json `model` section
    // (`SnapshotScopeBinding.fromModel`), so the transfer reads the same
    // modeled estate as full-export/publish (2026-07-07).
    (contractScope: MetadataSnapshotRunner.SnapshotParameters)
    (sourceSpec: string)
    (sinkSpec: string)
    (reconcileSpecs: string list)
    (reconcileIgnore: string list)
    (supportingScope: SupportingScope.SupportingScopeEntry list)
    (signoff: WriteSignoff.WriteApproval list)
    // 2026-07-09 (T0.3) — the flow's `foreignRefs`: acknowledged out-of-contract
    // references, threaded to `WriteOptions.ForeignRefsAcknowledged`.
    (foreignRefs: string list)
    // 2026-07-09 — the peer contracts' IDENTITY BASIS + the cloned-module
    // source→sink module map. `ByName` runs `NameAlignment.align` over the
    // acquired source contract before the SsKey-keyed gates (see below).
    (alignment: AlignmentMode)
    (alignMap: Map<string, string>)
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
    // Desugar the supporting-scope vocabulary onto the terse string inputs the
    // gates + engine already consume (2026-07-08): reference-family entries
    // become reconcile specs, owned-child / reference-seed become subset
    // tables. The typed model (seed / blocked sets) rides the write path.
    let scopeDesugar = SupportingScope.desugarToStrings supportingScope
    let reconcileSpecs = reconcileSpecs @ scopeDesugar.ExtraReconcile
    let tables = tables @ scopeDesugar.ExtraTables
    // Acquire the two SsKey-aligned contracts (the one I/O seam this face
    // adds). An unreadable OSSYS metamodel refuses on the schema-read axis
    // (exit 6) before any gate or connection-opening work.
    // Each side reads under its own snapshot scope: for `by-name` the sink's
    // clone modules carry the mapped names, so the sink scope remaps the model's
    // module names through `alignMap` (else the sink read mis-scopes and
    // A4-duplicates a referenced entity — `catalog.kinds.duplicateKey`).
    let sinkScope = PeerTransfer.sinkScopeFor alignment alignMap contractScope
    match (PeerTransfer.acquireContractsWith contractScope sinkScope sourceSpec sinkSpec).GetAwaiter().GetResult() with
    | Error errors ->
        // The gate surface owns the copy (§5): `source.ossys.*` classifies
        // onto the schema-read axis, so the operator gets the statement +
        // the next move, never the flat GenericStop wall.
        TtyRenderer.renderGate "projection transfer (peer)" (Preflight.refusalOf errors)
        dumpBench "transfer"
        (Preflight.refusalOf errors).ExitCode
    | Ok (acquiredSource, sinkContract) ->

    // Cloned-module alignment (2026-07-09). `by-sskey` (default) is identity —
    // the two contracts already align on native GUIDs. `by-name` rewrites the
    // SOURCE contract's SsKeys to the sink's by name (within `alignMap`) so the
    // downstream SsKey-keyed gates + engine run UNCHANGED; a mismatch refuses
    // BY NAME (`alignment.*`) here, before any gate or connection work. Rebind
    // `sourceContract` to the aligned catalog — every consumer below sees it.
    match NameAlignment.alignForMode alignment alignMap tables acquiredSource sinkContract with
    | Error errors ->
        TtyRenderer.renderGate "projection transfer (peer)" (Preflight.refusalOf errors)
        dumpBench "transfer"
        (Preflight.refusalOf errors).ExitCode
    | Ok sourceContract ->

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
        printfn "The go board (open decisions + before→after forecast + red/green verdict): projection check go <flow>  [--sql writes the planned T-SQL] [--impact writes the denormalized before/after data artifact]"

    // The shared contract-pair body: realization selector, execute/journal
    // gates, apparatus, engine run, narration — identical to the reverse leg,
    // with the peer pair as the contracts and the peer label on the prose.
    runContractPairTransfer "projection transfer (peer)"
        sourceSpec sinkSpec sourceContract sinkContract
        reconcileSpecs reconcileIgnore supportingScope signoff foreignRefs userMapPath executeRequested allowCdc allowDrops
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
    // The provenance the artifact writer stamps (the wrong-sink guard):
    // `-- projection:<kind> server=<s> database=<db> generated=<t>`. Parsed
    // purely by `TransferRevert.parseProvenance`; the fail-closed verdict is
    // `TransferRevert.guardVerdict` (checked below, once the sink resolves).
    let provenance = TransferRevert.parseProvenance allLines
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
            // THE WRONG-SINK GUARD (2026-07-06; fail-CLOSED 2026-07-09): the
            // artifact deletes BY KEY in whatever server/database --against
            // resolves to. `guardVerdict` refuses a server- OR database-mismatch
            // AND a header-less script (which can no longer be verified) — only
            // --force proceeds past it. The single most destructive standalone
            // verb defaults to refuse, not proceed.
            match TransferRevert.guardVerdict force provenance sink.DataSource sink.Database with
            | Some refusal ->
                TtyRenderer.renderVoicedError refusal
                dumpBench "revert"
                7
            | None ->
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

let private probeCount (cnn: Microsoft.Data.SqlClient.SqlConnection) (sql: string) : int64 =
    use cmd = cnn.CreateCommand()
    cmd.CommandText <- sql
    System.Convert.ToInt64 (cmd.ExecuteScalar())

let runCheckGo
    // Same contract-read scope as `runPeerTransfer` — the go board must
    // forecast with the contracts the live run will actually read.
    (contractScope: MetadataSnapshotRunner.SnapshotParameters)
    // The board coordinates ride the reified `CheckGoArgs` record (2026-07-10,
    // the manifest program) — the tuple had reached three adjacent bools.
    (args: CheckGoArgs) : int =
    let flowName, fromLabel, toLabel = args.Flow, args.FromLabel, args.ToLabel
    let asJson, emitSql, emitImpact = args.AsJson, args.EmitSql, args.EmitImpact
    let planned = args.Planned
    let finish (items: GoBoard.Item list) : int =
        let board : GoBoard.Board = { Flow = flowName; From = fromLabel; To = toLabel; Items = items }
        // The machine lens is the stable `toJsonString` CI contract (untouched);
        // the human lens routes through `GoBoardView` — the Spectre-backed `View`
        // engine (2026-07-08, the rendering-elevation program): the forecast reflows
        // to the terminal width, the supporting-scope axis reveals as a fully-expanded
        // References / Dependents tree with the per-claim guarantee named.
        if asJson then printfn "%s" (GoBoard.toJsonString board)
        else GoBoardView.write Console.Out board
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
        // For `by-name` the sink reads its CLONE modules — the sink scope remaps
        // the model's module names through `alignMap` (board/engine parity with
        // `runPeerTransfer`); `by-sskey` reads both sides under the same scope.
        let sinkScope = PeerTransfer.sinkScopeFor opts.Alignment opts.AlignMap contractScope
        match (PeerTransfer.acquireContractsWith contractScope sinkScope sourceSpec sinkSpec).GetAwaiter().GetResult() with
        | Error errors ->
            items.Add (GoBoard.itemWith "contracts"
                (GoBoard.Status.Red
                    ("a metamodel could not be read — the run has no contract to align on.",
                     "check the connection reference and the principal's SELECT on the ossys_* tables; then re-run."))
                (errors |> List.map (fun e -> sprintf "%s: %s" e.Code e.Message)))
            finish (List.ofSeq items)
        | Ok (acquiredSource, sinkContract) ->
        // Supporting scope (2026-07-08, the business-intent program): the
        // typed vocabulary desugars onto the EFFECTIVE subset the downstream
        // axes judge — the payload is `opts.Tables` alone; owned-child /
        // reference-seed expand the written set, the reference family expands
        // the reconcile set. The dedicated axis below verifies each declared
        // intent against the graph. Computed HERE (before alignment) because the
        // effective subset is also name-alignment's strict scope.
        let scopeDesugar = SupportingScope.desugarToStrings opts.SupportingScope
        let effectiveTables = opts.Tables @ scopeDesugar.ExtraTables
        let effectiveReconcile = opts.Reconcile @ scopeDesugar.ExtraReconcile
        // Cloned-module alignment (2026-07-09) — the board aligns at the SAME
        // point the engine does (two-traversal parity): `by-name` rewrites the
        // source contract's SsKeys to the sink's by name (strict over the
        // transferred `effectiveTables`) before the SsKey-keyed axes below; a
        // mismatch reds the board `alignment.*` with its detail.
        match NameAlignment.alignForMode opts.Alignment opts.AlignMap effectiveTables acquiredSource sinkContract with
        | Error errors ->
            items.Add (GoBoard.itemWith "contracts"
                (GoBoard.Status.Red
                    ("the two estates do not align by name for the declared cloned-module map.",
                     "fix the flow's `alignMap` (source module -> sink module) or the entities' names/shape; then re-run."))
                (errors |> List.map (fun e -> sprintf "%s: %s" e.Code e.Message)))
            finish (List.ofSeq items)
        | Ok sourceContract ->
        items.Add
            (GoBoard.item "contracts"
                (match opts.Alignment with
                 | AlignmentMode.BySsKey -> GoBoard.Status.Green "both metamodels read; identities align by SS_KEY."
                 | AlignmentMode.ByName  -> GoBoard.Status.Green "both metamodels read; identities aligned BY NAME (cloned modules) — see the identity-basis line below."))
        // -- identity basis (2026-07-10): the peer leg re-mints surrogate keys,
        // and by-name alignment is name-derived. Both are consequences the
        // operator lives with on EVERY row of a large subset, so they are
        // ACKNOWLEDGED on their own advisory line — not left as a parenthetical
        // on a green pass ("no silent erasure", applied to the key plane). An
        // advisory: it never blocks (the re-mint IS the managed-cloud path), but
        // it is un-missable.
        let identityBasisNote =
            let mint =
                "the target mints a fresh surrogate key for every identity-column row, because the managed-cloud grant forbids identity insertion. Inbound foreign keys re-point to the new keys automatically; the source's own key values are not preserved, so a reference to a source key held outside the transferred set — an export, a report snapshot, another system — will not match the loaded rows."
            match opts.Alignment with
            | AlignmentMode.BySsKey -> mint
            | AlignmentMode.ByName  -> mint + " Identity correspondence here is derived by name across the cloned modules, a basis weaker than the native OutSystems identifier, entered only by explicit opt-in."
        items.Add (GoBoard.item "identity basis" (GoBoard.Status.Advisory identityBasisNote))
        // -- the declared subset -------------------------------------------
        match Transfer.resolveLoadSet sourceContract effectiveTables with
        | Error errors ->
            items.Add (GoBoard.itemWith "tables"
                (GoBoard.Status.Red ("the declared table subset does not resolve.", "fix the flow's `tables` list (use Module.Entity for ambiguous names); then re-run."))
                (errors |> List.map (fun e -> e.Message)))
            finish (List.ofSeq items)
        | Ok loadSet ->
        items.Add (GoBoard.item "tables"
            (GoBoard.Status.Green
                (match loadSet with
                 | Some s ->
                     match scopeDesugar.ExtraTables with
                     | [] -> sprintf "%d table(s) declared; all resolve." (Set.count s)
                     | extra -> sprintf "%d payload table(s) + %d brought in by supporting scope; all resolve." (List.length opts.Tables) (List.length extra)
                 | None -> "no subset declared — the whole modeled estate transfers.")))
        // -- reconcile strategy resolution ----------------------------------
        let reconcileResolution =
            parseReconcileInputs effectiveReconcile opts.Rekey false
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
        // -- supporting scope: the declared business intent, verified --------
        (if not (List.isEmpty opts.SupportingScope) then
            let payloadSet =
                match Transfer.resolveLoadSet sourceContract opts.Tables with
                | Ok (Some s) -> s
                | _ -> Set.empty
            let baseReconciled =
                match parseReconcileInputs opts.Reconcile opts.Rekey false with
                | Ok (entries, userMapEntries) ->
                    match TransferSpec.resolveAllReconciliation sinkContract entries userMapEntries with
                    | Ok m -> m |> Map.toSeq |> Seq.map fst |> Set.ofSeq
                    | Error _ -> Set.empty
                | Error _ -> Set.empty
            // Verify each entry against the SINK graph (the physical side the
            // live run writes); the payload is the declared `tables`.
            let verdicts = SupportingScope.verify sinkContract payloadSet baseReconciled opts.SupportingScope
            let contradictions =
                verdicts |> List.choose (fun (e, v) ->
                    match v with
                    | SupportingScope.ScopeClaimVerdict.Contradicted (reason, remedy) -> Some (e, reason, remedy)
                    | _ -> None)
            let resolvedScope =
                match SupportingScope.resolve sinkContract opts.SupportingScope with
                | Ok r -> r
                | Error _ -> SupportingScope.empty
            let unaccounted = SupportingScope.unaccountedEscapes sinkContract payloadSet baseReconciled resolvedScope
            let relName (e: SupportingScope.SupportingScopeEntry) =
                match e.Relationship with
                | SupportingScope.SupportingRelationship.ExistingReference _ -> "existing-reference"
                | SupportingScope.SupportingRelationship.ReferenceSeed -> "reference-seed"
                | SupportingScope.SupportingRelationship.SharedAnchor _ -> "shared-anchor"
                | SupportingScope.SupportingRelationship.StaticLookup _ -> "static-lookup"
                | SupportingScope.SupportingRelationship.OwnedChild _ -> "owned-child"
                | SupportingScope.SupportingRelationship.BlockedDependent _ -> "blocked-dependent"
            let confirmedLines =
                verdicts |> List.choose (fun (e, v) ->
                    match v with
                    | SupportingScope.ScopeClaimVerdict.Confirmed note -> Some (sprintf "%s %s — %s (%s)" (relName e) e.Table note e.Reason)
                    | _ -> None)
            // The hierarchical lens (2026-07-08, the rendering-elevation program):
            // the References / Dependents claim tree — each claim with its family,
            // normalized JOIN edges, authored reason, and the GUARANTEE a Confirmed
            // claim earns. `SupportingScope.scopeGroups` is the SHARED pure builder
            // the live-run report projects too, so the tree cannot drift between the
            // readiness surface and the run itself.
            let groups = SupportingScope.scopeGroups sinkContract payloadSet baseReconciled opts.SupportingScope
            let unaccountedStrings =
                unaccounted |> List.map (fun (k, r, target) ->
                    sprintf "%s.%s -> %s escapes the payload and no supporting-scope entry classifies it" (Name.value k.Name) (Name.value r.Name) (Name.value target.Name))
            match contradictions, unaccounted with
            | [], [] ->
                items.Add (GoBoard.scopeItem "supporting scope"
                    (GoBoard.Status.Green (sprintf "%d supporting table(s) declared with intent; every one is borne out by a relationship, and no escaping reference is unaccounted." (List.length opts.SupportingScope)))
                    groups
                    []
                    confirmedLines)
            | cs, un ->
                let detail =
                    [ for (e, reason, remedy) in cs do
                        yield sprintf "%s %s — %s" (relName e) e.Table reason
                        yield sprintf "  -> %s" remedy
                      yield! unaccountedStrings ]
                items.Add (GoBoard.scopeItem "supporting scope"
                    (GoBoard.Status.Red (sprintf "%d declared intent(s) the graph contradicts, %d escaping reference(s) unaccounted." (List.length cs) (List.length un), "correct the entr(ies) named below, or classify the unaccounted reference(s); then re-run."))
                    groups
                    unaccountedStrings
                    detail))
        // -- schema shape ----------------------------------------------------
        let gateScope = loadSet |> Option.map (Set.union reconciledKeys)
        let shape = PeerTransfer.shapeVerdict gateScope sourceContract sinkContract
        // The verdict names what MATCHED as well as what drifted
        // (2026-07-08): "indexes differ" alone leaves the operator guessing
        // how deep the agreement runs — the proven tiers close that gap.
        let provenClause =
            match shape.Proven with
            | [] -> ""
            | tiers -> sprintf " Matched: %s." (String.concat ", " tiers)
        if not (List.isEmpty shape.Blocking) then
            items.Add (GoBoard.itemWith "shape"
                (GoBoard.Status.Red (sprintf "%d blocking schema divergence(s) over the transferred set.%s" shape.Blocking.Length provenClause, "align the models (deploy the same version to both environments) or narrow the subset; then re-run."))
                shape.Blocking)
        elif not (List.isEmpty shape.Advisory) then
            items.Add (GoBoard.itemWith "shape"
                (GoBoard.Status.Advisory (sprintf "one insertable shape over the transferred set — %d advisory difference(s), none blocks a data load.%s" shape.Advisory.Length provenClause))
                shape.Advisory)
        else
            items.Add (GoBoard.item "shape" (GoBoard.Status.Green (sprintf "the two models are one shape over the transferred set.%s" provenClause)))
        // -- escaping relationships (the OPEN-DECISION axis) ----------------
        let escapes =
            match loadSet with
            | Some s -> PeerTransfer.escapingFks sourceContract s reconciledKeys
            | None -> []
        if not (List.isEmpty escapes) then
            // Live reconcile EVIDENCE (2026-07-07): each proposed reconcile
            // column, probed against the actual pair — sink uniqueness + a
            // sampled source→sink value match — so the operator pastes a
            // PROVEN rule, not a guess. A pair that will not open degrades
            // to the static (shape-derived) proposals, named.
            let evidenceLines =
                match (ConnectionSpec.openSpec SubstrateRole.Source "check-go-evidence-source" sourceSpec).GetAwaiter().GetResult() with
                | Error _ -> [ "evidence: the source connection would not open — the proposals above are shape-derived only." ]
                | Ok source ->
                    use source = source
                    match (ConnectionSpec.openSpec SubstrateRole.Sink "check-go-evidence-sink" sinkSpec).GetAwaiter().GetResult() with
                    | Error _ -> [ "evidence: the sink connection would not open — the proposals above are shape-derived only." ]
                    | Ok sink ->
                        use sink = sink
                        let probeLines =
                            PeerTransfer.probeReconcileEvidence source sink sourceContract sinkContract escapes
                            |> PeerTransfer.narrateEvidence
                            |> List.map (sprintf "evidence: %s")
                        // THE EXACT CONSEQUENCES (2026-07-10, the manifest
                        // program, slice 2): the row substrate read ONCE into
                        // the EvidenceCache from these same connections, and
                        // every answer's delta computed over the FULL rowsets
                        // through the same Core match the run uses — exact
                        // counts, never the TOP-200 sample (which remains the
                        // separate `evidence:` strength heuristic above).
                        // THE_VOICE: each consequence is ONE complete sentence,
                        // readable aloud — the condition, the counted outcome,
                        // and the fact that qualifies it. No shorthand, no
                        // mixed tense, no internal vocabulary.
                        let consequenceLines =
                            try
                                let cache = (EvidenceCache.fill source sink sourceContract sinkContract escapes).GetAwaiter().GetResult()
                                let loadSetSet = match loadSet with Some s -> s | None -> Set.empty
                                EvidenceCache.componentsOf sourceContract loadSetSet escapes
                                |> List.collect (fun componentEdges ->
                                    let per = EvidenceCache.perAnswerDeltas cache sourceContract loadSetSet reconciledKeys componentEdges Map.empty
                                    componentEdges
                                    |> List.map (fun e -> e.Target)
                                    |> List.distinct
                                    |> List.collect (fun target ->
                                        let label =
                                            componentEdges
                                            |> List.tryFind (fun e -> e.Target = target)
                                            |> Option.map (fun e -> sprintf "%s.%s" (Name.value e.TargetModule) (Name.value e.TargetName))
                                            |> Option.defaultValue (SsKey.rootOriginal target)
                                        match per |> Map.tryFind target with
                                        | None -> []
                                        | Some answers ->
                                            EvidenceCache.candidateAnswers componentEdges target
                                            |> List.choose (fun a -> answers |> Map.tryFind a |> Option.map (fun ev -> a, ev))
                                            |> List.map (fun (a, ev) ->
                                                let d = ev.Delta
                                                let uniqueness (col: Name) =
                                                    match ev.SinkUnique with
                                                    | Some true -> sprintf " Each %s value names exactly one target row." (Name.value col)
                                                    | Some false -> sprintf " The %s value repeats on the target, so the oldest row is kept and later duplicates are displaced." (Name.value col)
                                                    | None -> ""
                                                let dropped (col: Name) =
                                                    match d.RowsDropped with
                                                    | 0 -> "none drop"
                                                    | n -> sprintf "%d drop because the %s row each points at has no %s match in the target" n label (Name.value col)
                                                match a with
                                                | EvidenceCache.Answer.Reconcile col ->
                                                    sprintf "consequence: if %s is reconciled by %s, %d row(s) that point at it re-key onto the %s rows the target already holds, and %s.%s"
                                                        label (Name.value col) d.RowsRekeyed label (dropped col) (uniqueness col)
                                                | EvidenceCache.Answer.StaticLookup col ->
                                                    sprintf "consequence: if %s is declared identical in both environments and matched by %s, the same %d row(s) re-key and %s; a live run refuses if any %s row differs between the environments, is missing, or is extra.%s"
                                                        label (Name.value col) d.RowsRekeyed (dropped col) label (uniqueness col)
                                                | EvidenceCache.Answer.Pin _ ->
                                                    sprintf "consequence: if every reference to %s is re-keyed onto one chosen %s row in the target, all %d row(s) that point at it re-key and none drop; the row must be chosen, and must exist in the target."
                                                        label label d.RowsRekeyed
                                                | EvidenceCache.Answer.Widen ->
                                                    let spawned =
                                                        match d.SpawnedKeys with
                                                        | [] ->
                                                            sprintf "%s points at no table outside the transfer, so nothing further needs deciding" label
                                                        | ks ->
                                                            let names =
                                                                ks
                                                                |> List.map (fun k ->
                                                                    Catalog.tryFindKind k sourceContract
                                                                    |> Option.map (fun kd -> Name.value kd.Name)
                                                                    |> Option.defaultValue (SsKey.rootOriginal k))
                                                                |> String.concat ", "
                                                            sprintf "%s itself points at %d table(s) outside the transfer (%s), and each of those will then need this same decision" label ks.Length names
                                                    sprintf "consequence: if %s is added to the transfer, its %d row(s) transfer too — and %s."
                                                        label d.RowsEnteringScope spawned)))
                            with ex ->
                                [ sprintf "consequence: the rows could not be read, so exact per-answer counts are unavailable; the evidence above stands. Cause: %s" ex.Message ]
                        probeLines @ consequenceLines
            items.Add (GoBoard.itemWith "relationships"
                (GoBoard.Status.Red (sprintf "%d OUTBOUND reference(s) escape the transferred set — each row would carry a foreign key to a table not being transferred; each needs a decision." escapes.Length, "add the proposed reconcile entr(ies) to the flow, or widen `tables`; then re-run."))
                (PeerTransfer.narrateEscapes escapes @ evidenceLines))
        else
            items.Add (GoBoard.item "relationships" (GoBoard.Status.Green "every OUTBOUND reference from the transferred set lands inside it or on a reconciled table — no row will carry a dangling foreign key. (A replace-wipe's INBOUND-reference safety is the separate `re-run` axis.)"))
        // -- foreign refs (2026-07-10): the flow's `foreignRefs` suppresses the
        // out-of-contract-escape refusal (T0.3) by declaring the target rows
        // environment-stable — a claim the engine records but does not verify
        // (the target kind is absent from the acquired contract, so the board has
        // nothing to probe it against). Surface each suppressed ref as UNVERIFIED:
        // the run loads the source key unchanged, and a wrong declaration
        // silently cross-wires the FK across environments. Only present when the
        // flow actually declares foreignRefs.
        (if not (List.isEmpty opts.ForeignRefs) then
            items.Add
                (GoBoard.itemWith "foreign refs"
                    (GoBoard.Status.Advisory
                        (sprintf "%d out-of-contract reference(s) are declared environment-stable in `foreignRefs`; their targets lie outside the acquired contract, so the run loads the source key unchanged and their alignment across the two environments is unverified. Confirm each target holds the same identity in both environments before authorizing the run." (List.length opts.ForeignRefs)))
                    (opts.ForeignRefs |> List.map (sprintf "declared stable, alignment unverified: %s"))
                 |> GoBoard.asUnverified))
        // -- load order (the EFFECTIVE transfer graph, 2026-07-07) -----------
        // The same `TransferScope` the engine's Execute gates build:
        // declared tables plus reconciled kinds as isolated nodes, FK
        // edges binding only between written kinds — an unrelated estate
        // cycle no longer reds a partial transfer's board (and no longer
        // refuses its live run).
        let scope = TransferScope.create sinkContract loadSet reconciledKeys
        let topo = TransferScope.topology Projection.Core.TreatAsCycle scope sinkContract
        (match Transfer.orderedLoadGate topo with
         | Some refusal ->
             items.Add (GoBoard.itemWith "load order"
                 (GoBoard.Status.Red ("the load order of the transferred set degraded to the alphabetical fallback — a live run refuses.", "make the named cycle's FK columns nullable (they then defer automatically), or transfer without the affected kinds."))
                 [ refusal.Message ])
         | None ->
             items.Add (GoBoard.item "load order" (GoBoard.Status.Green "parents before children over the transferred set, proven (topological).")))
        // -- THE DRY RUN (real reads, zero writes): the row/identity forecast -
        // The dry run forecasts EXACTLY what Execute writes (two-traversal parity):
        // it goes through the SAME `runReverseLegThroughConnectionsWith` entry with
        // the SAME resolved supporting-scope the live run threads — seed kinds,
        // acknowledged exclusions, static lookups (`SupportingScope.resolve`, the
        // canonical resolver the live face at `runContractPairTransfer` uses) plus
        // the flow's `foreignRefs` ack and the sink's identity policy. Bound before
        // the dry run; the forecast axis below reuses `staticLookupKeys`.
        let boardScope =
            match SupportingScope.resolve sinkContract opts.SupportingScope with
            | Ok r -> r
            | Error _ -> SupportingScope.empty
        let staticLookupKeys : Set<SsKey> = boardScope.StaticLookupKinds
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
                    let ignoreSet =
                        opts.ReconcileIgnore
                        |> List.choose (fun n -> match Name.create n with Ok v -> Some v | Error _ -> None)
                        |> Set.ofList
                    (Transfer.runReverseLegThroughConnectionsWith
                        opts.SinkCapability.IdentityPolicy Transfer.DryRun opts.Emission false true false
                        effectiveTables connections sourceContract sinkContract reconciliation ignoreSet
                        boardScope.SeedKinds boardScope.AcknowledgedExclusions [] boardScope.StaticLookupKinds
                        (Set.ofList opts.ForeignRefs) false None)
                        .GetAwaiter().GetResult()
            match dryRun () with
            | Error errors ->
                items.Add (GoBoard.itemWith "forecast"
                    (GoBoard.Status.Red ("the dry run refused.", "resolve the named refusal; then re-run."))
                    (errors |> List.map (fun e -> sprintf "%s: %s" e.Code e.Message)))
            | Ok report ->
                let nm = nameOf report.Names
                // The BEFORE → AFTER table (2026-07-07, the go-board
                // forecast program; 2026-07-08, the board-clarity pass):
                // live sink counts beside the plan's adds / matches /
                // deletes — the data change STATED, not gestured at. A
                // short-lived sink connection probes `before` and samples
                // the rows a wipe would delete; a count that will not probe
                // renders `?`, never a silent 0.
                let physicalIn (c: Catalog) (key: SsKey) : string =
                    match Catalog.tryFindKind key c with
                    | Some kd -> sprintf "%s.%s" (TableId.schemaText kd.Physical) (TableId.tableText kd.Physical)
                    | None -> nm key
                let rowText (row: StaticRow) : string =
                    row.Values
                    |> Map.toList
                    |> List.map (fun (c, v) -> sprintf "%s=%s" (Name.value c) (if v = "" then "(blank)" else v))
                    |> String.concat ", "
                let wipeSet =
                    match report.Plan with
                    | Some plan when opts.Emission = EmissionMode.WipeAndLoad ->
                        TransferResume.wipeTargets plan topo loadSet |> Set.ofList
                    | _ -> Set.empty
                // One connection pass: per kind, the live count — and for a
                // kind the wipe will clear, the first rows it would delete.
                let sinkProbes : Map<SsKey, int64 option * string list> =
                    let keys = report.Kinds |> List.map (fun k -> k.Kind)
                    match (ConnectionSpec.openSpec SubstrateRole.Sink "check-go-forecast" sinkSpec).GetAwaiter().GetResult() with
                    | Error _ -> keys |> List.map (fun k -> k, (None, [])) |> Map.ofList
                    | Ok cnn ->
                        use cnn = cnn
                        let sampleRows (schema: string) (table: string) : string list =
                            try
                                use cmd = cnn.CreateCommand()
                                cmd.CommandText <- sprintf "SELECT TOP (5) * FROM [%s].[%s];" schema table
                                use r = cmd.ExecuteReader()
                                let cols = [ for i in 0 .. r.FieldCount - 1 -> i, r.GetName i ]
                                let rec go acc =
                                    if r.Read () then
                                        let line =
                                            cols
                                            |> List.map (fun (i, c) -> sprintf "%s=%s" c (if r.IsDBNull i then "NULL" else string (r.GetValue i)))
                                            |> String.concat ", "
                                        go (line :: acc)
                                    else List.rev acc
                                go []
                            with _ -> []
                        keys
                        |> List.map (fun key ->
                            match Catalog.tryFindKind key sinkContract with
                            | None -> key, (None, [])
                            | Some k ->
                                let schema, table = TableId.schemaText k.Physical, TableId.tableText k.Physical
                                let count =
                                    try Some (probeCount cnn (sprintf "SELECT COUNT_BIG(*) FROM [%s].[%s];" schema table))
                                    with _ -> None
                                let wipeSample =
                                    if Set.contains key wipeSet && count |> Option.exists (fun n -> n > 0L)
                                    then sampleRows schema table
                                    else []
                                key, (count, wipeSample))
                        |> Map.ofList
                let before (key: SsKey) : int64 option =
                    sinkProbes |> Map.tryFind key |> Option.bind fst
                let dropsByKind = report.SkippedReferences |> List.countBy fst |> Map.ofList
                let isDeclared (key: SsKey) =
                    match loadSet with None -> true | Some s -> Set.contains key s
                // The PAYLOAD (opts.Tables alone) vs the supporting additions:
                // a supporting kind carries its DECLARED intent in the note,
                // upgrading the ownership-blind "brought along by K.col -> T".
                let payloadKeys : Set<SsKey> =
                    match Transfer.resolveLoadSet sourceContract opts.Tables with
                    | Ok (Some s) -> s
                    | _ -> (match loadSet with Some s -> s | None -> Set.empty)
                let declaredIntent : Map<SsKey, string> =
                    opts.SupportingScope
                    |> List.choose (fun e ->
                        SupportingScope.tryResolveTable sinkContract e.Table
                        |> Option.map (fun k ->
                            let note =
                                match e.Relationship with
                                | SupportingScope.SupportingRelationship.ExistingReference _ -> sprintf "existing reference — matched to the target's own rows (%s)" e.Reason
                                | SupportingScope.SupportingRelationship.StaticLookup _      -> sprintf "static lookup — matched, held identical (%s)" e.Reason
                                | SupportingScope.SupportingRelationship.ReferenceSeed        -> sprintf "seeded reference — inserted where the target lacks it (%s)" e.Reason
                                | SupportingScope.SupportingRelationship.SharedAnchor _       -> sprintf "shared anchor — every reference re-points to one row (%s)" e.Reason
                                | SupportingScope.SupportingRelationship.OwnedChild ofParent  -> sprintf "owned child of %s (%s)" ofParent e.Reason
                                | SupportingScope.SupportingRelationship.BlockedDependent _   -> ""
                            k.SsKey, note))
                    |> Map.ofList
                // static-lookup kinds are held to ZERO divergence: a drifted
                // lookup is a real integrity fault, not an advisory.
                // A kind OUTSIDE the declared subset rides along because a
                // declared table's relationship needs it — name the pulling
                // edge(s) in Table.Column -> Target form.
                let broughtAlongBy (target: SsKey) : string list =
                    match loadSet with
                    | None -> []
                    | Some declared ->
                        Catalog.allKinds sinkContract
                        |> List.filter (fun k -> Set.contains k.SsKey declared)
                        |> List.collect (fun k ->
                            k.References
                            |> List.filter (fun r -> r.TargetKind = target)
                            |> List.map (fun r ->
                                let col =
                                    k.Attributes
                                    |> List.tryFind (fun a -> a.SsKey = r.SourceAttribute)
                                    |> Option.map (fun a -> Name.value a.Name)
                                    |> Option.defaultValue (Name.value r.Name)
                                sprintf "%s.%s -> %s" (Name.value k.Name) col (nm target)))
                        |> List.distinct
                let lineFor (k: Transfer.KindOutcome) : GoBoard.ForecastLine option =
                    let beforeN = before k.Kind
                    let reconciled = k.Disposition = IdentityDisposition.ReconciledByRule
                    let wiped = Set.contains k.Kind wipeSet
                    let deletes = if wiped then (beforeN |> Option.defaultValue 0L) else 0L
                    let drops = dropsByKind |> Map.tryFind k.Kind |> Option.defaultValue 0
                    let drift = report.ReconcileDivergences |> List.filter (fun d -> d.Kind = k.Kind) |> List.length
                    let note =
                        [ match Map.tryFind k.Kind declaredIntent with
                          | Some intent when intent <> "" -> yield intent
                          | _ ->
                              if not (Set.contains k.Kind payloadKeys) then
                                  match broughtAlongBy k.Kind with
                                  | []    -> yield "brought along (reconciled)"
                                  | edges -> yield sprintf "brought along by %s" (String.concat ", " edges)
                          if reconciled then
                              yield
                                  (if drift = 0 then "matched to existing target rows, no insert"
                                   else sprintf "matched to existing target rows, no insert; %d column(s) differ on matched rows (see match drift)" drift)
                          if not (Set.isEmpty k.DeferredFkColumns) then yield sprintf "%d FK column(s) re-point in phase 2" (Set.count k.DeferredFkColumns)
                          if drops > 0 then
                              yield
                                  (if wiped then sprintf "%d row(s) drop (unmatched reference) — wiped but NOT re-inserted" drops
                                   else sprintf "%d row(s) drop (unmatched reference)" drops)
                          if wiped && Option.isNone beforeN then yield "wiped first (count unprobed)" ]
                        |> String.concat "; "
                    let line : GoBoard.ForecastLine =
                        { Source  = physicalIn sourceContract k.Kind
                          Table   = physicalIn sinkContract k.Kind
                          Before  = beforeN
                          Adds    = int64 k.RowsIngested
                          Matches = (if reconciled then Some (int64 k.RowsMatched) else None)
                          Deletes = deletes
                          Note    = note }
                    if line.Adds > 0L || line.Deletes > 0L || reconciled || (wiped && Option.isNone beforeN)
                    then Some line else None
                // Declared tables first, then the brought-along kinds —
                // the operator reads their own list before the closure's.
                let declaredKinds, broughtKinds = report.Kinds |> List.partition (fun k -> isDeclared k.Kind)
                let forecastLines = (declaredKinds |> List.choose lineFor) @ (broughtKinds |> List.choose lineFor)
                // The rows a strategy-replace wipe would DELETE, in full — the
                // operator sees the actual records, not a count. (A dropped row
                // under replace shows in `-del` as part of the wipe; the drop
                // means it is NOT re-inserted afterwards — the `+add` column is
                // already net of drops.)
                let wipePreviews =
                    [ for k in report.Kinds do
                          match sinkProbes |> Map.tryFind k.Kind with
                          | Some (Some n, (_ :: _ as sample)) when n > 0L ->
                              yield ""
                              yield sprintf "wipe preview — %s (first %d of %d row(s) the wipe deletes):" (physicalIn sinkContract k.Kind) sample.Length n
                              for line in sample do yield sprintf "  %s" line
                          | _ -> () ]
                // `detail` is the plain/JSON twin (the aligned strings + previews),
                // kept byte-identical; the typed `forecastLines`/`previews` ride in
                // `Body` for the responsive-table lens (2026-07-08).
                let forecastDetail = GoBoard.forecastTable forecastLines @ wipePreviews
                // When a sink `before` count would not probe it renders `?`, and
                // that table's after-count is projected from the plan, not read
                // from the sink (2026-07-10). The dry run is sound, but the sink
                // state under it is unread, so the axis is an ADVISORY marked
                // unverified — the verdict names the count, and a green is never
                // read as "every fact proven" over a sink the board could not read.
                let unprobed = forecastLines |> List.filter (fun l -> Option.isNone l.Before) |> List.length
                let baseHeadline =
                    sprintf "dry run complete — %d row(s) into %d declared table(s)%s; before → after below."
                        (report.Kinds |> List.sumBy (fun k -> k.RowsIngested))
                        (declaredKinds |> List.filter (fun k -> k.RowsIngested > 0) |> List.length)
                        (match broughtKinds |> List.choose lineFor |> List.length with
                         | 0 -> ""
                         | n -> sprintf ", %d table(s) brought along by relationships" n)
                let forecastStatus =
                    if unprobed > 0
                    then GoBoard.Status.Advisory (baseHeadline + sprintf " The current row count could not be read for %d table(s), shown as `?`; their after-count is projected from the plan, not measured. Re-run the board when the sink is reachable to measure it." unprobed)
                    else GoBoard.Status.Green baseHeadline
                let forecastItm =
                    GoBoard.forecastItem "forecast"
                        forecastStatus
                        forecastLines
                        (wipePreviews |> List.filter (fun s -> s <> ""))
                        forecastDetail
                items.Add (if unprobed > 0 then GoBoard.asUnverified forecastItm else forecastItm)
                // THE IMPACT ARTIFACT (2026-07-09, `--impact`): the operator's
                // "show me EXACTLY what happens to the data". The dry run already
                // holds the AFTER rows (the plan) and the sink holds the BEFORE
                // rows; `TransferImpact` segments the transfer graph, denormalizes
                // each component into nested documents, and classifies every row
                // (add / delete / change / unchanged). Written as a self-contained
                // HTML artifact + a JSON twin — never inline on the board.
                if emitImpact then
                    match report.Plan with
                    | None ->
                        items.Add (GoBoard.item "impact"
                            (GoBoard.Status.Advisory "--impact: this dry run carried no materialized plan — no artifact written."))
                    | Some plan ->
                        // BEFORE — the sink's current rows for the transferred scope,
                        // capped (an --impact review is over the golden subset, not the
                        // estate); a table over the cap is noted, never silently cut.
                        let impactCap = 10000
                        let mutable truncated : string list = []
                        let readBefore (k: Kind) : StaticRow list =
                            match (ConnectionSpec.openSpec SubstrateRole.Sink "check-go-impact" sinkSpec).GetAwaiter().GetResult() with
                            | Error _ -> []
                            | Ok cnn ->
                                use cnn = cnn
                                try
                                    use cmd = cnn.CreateCommand()
                                    cmd.CommandText <- sprintf "SELECT TOP (%d) * FROM [%s].[%s];" (impactCap + 1) (TableId.schemaText k.Physical) (TableId.tableText k.Physical)
                                    use r = cmd.ExecuteReader()
                                    let ord = [ for i in 0 .. r.FieldCount - 1 -> r.GetName i, i ] |> Map.ofList
                                    let acc = System.Collections.Generic.List<StaticRow>()
                                    while r.Read () do
                                        let values =
                                            k.Attributes
                                            |> List.choose (fun a ->
                                                match Map.tryFind (ColumnRealization.columnNameText a.Column) ord with
                                                | Some i -> Some (a.Name, (if r.IsDBNull i then "" else string (r.GetValue i)))
                                                | None -> None)
                                            |> Map.ofList
                                        acc.Add { Identifier = k.SsKey; Values = values }
                                    if acc.Count > impactCap then truncated <- Name.value k.Name :: truncated
                                    acc |> Seq.truncate impactCap |> List.ofSeq
                                with _ -> []
                        let scopeKinds = Catalog.allKinds sinkContract |> List.filter (fun k -> Set.contains k.SsKey scope.Nodes)
                        let before = scopeKinds |> List.map (fun k -> k.SsKey, readBefore k) |> Map.ofList
                        let after = plan.Loads |> List.map (fun l -> l.Kind, l.Rows) |> Map.ofList
                        // Business keys: the reconciled/static-lookup kinds matched by
                        // a column (MatchByColumn) — the key that lets before↔after
                        // resolve to change/unchanged rather than delete-all + add-all.
                        let rec bkOf (s: ReconciliationStrategy) : Name option =
                            match s with
                            | ReconciliationStrategy.MatchByColumn c -> Some c
                            | ReconciliationStrategy.MatchByColumns (c :: _) -> Some c
                            | ReconciliationStrategy.FallbackToAssigned (_, primary) -> bkOf primary
                            | _ -> None
                        let businessKeys = reconciliation |> Map.toList |> List.choose (fun (k, s) -> bkOf s |> Option.map (fun c -> k, c)) |> Map.ofList
                        // The relational role per kind — the summary matrix + the
                        // relational-intent / 1:1-confirmation surfaces. The variety,
                        // the operator's reason, and the guarantee come from
                        // supportingScope; the static-lookup 1:1 verdict from the SAME
                        // `report.StaticLookupDivergences` the board reds on (a
                        // static-lookup kind ABSENT from that list is verified identical).
                        let staticLookupVerdict (k: SsKey) : string option =
                            match report.StaticLookupDivergences |> List.tryFind (fun d -> d.Kind = k) with
                            | Some d ->
                                let parts =
                                    [ if not (List.isEmpty d.ColumnDrifts)    then yield sprintf "%d col drift" (List.length d.ColumnDrifts)
                                      if not (List.isEmpty d.ExtraOnTarget)   then yield sprintf "+%d extra" d.ExtraOnTarget.Length
                                      if not (List.isEmpty d.MissingOnTarget) then yield sprintf "%d missing" d.MissingOnTarget.Length ]
                                Some (sprintf "drift: %s" (String.concat " · " parts))
                            | None ->
                                let n = before |> Map.tryFind k |> Option.map List.length |> Option.defaultValue 0
                                Some (sprintf "1:1 identical (%d/%d)" n n)
                        let keyLabel (rel: SupportingScope.SupportingRelationship) : string option =
                            match rel with
                            | SupportingScope.SupportingRelationship.StaticLookup key
                            | SupportingScope.SupportingRelationship.ExistingReference key -> Some key
                            | SupportingScope.SupportingRelationship.SharedAnchor (_, Some key) -> Some key
                            | SupportingScope.SupportingRelationship.OwnedChild p
                            | SupportingScope.SupportingRelationship.BlockedDependent p -> Some (sprintf "of %s" p)
                            | _ -> None
                        let roles =
                            opts.SupportingScope
                            |> List.choose (fun e ->
                                SupportingScope.tryResolveTable sinkContract e.Table
                                |> Option.map (fun k ->
                                    let verdict =
                                        match e.Relationship with
                                        | SupportingScope.SupportingRelationship.StaticLookup _ -> staticLookupVerdict k.SsKey
                                        | SupportingScope.SupportingRelationship.ExistingReference _ ->
                                            let n = before |> Map.tryFind k.SsKey |> Option.map List.length |> Option.defaultValue 0
                                            Some (sprintf "%d matched" n)
                                        | _ -> None
                                    k.SsKey,
                                    ({ Variety = SupportingScope.relationshipLabel e.Relationship
                                       Reason = e.Reason
                                       Guarantee = SupportingScope.guaranteeOf e.Relationship (SupportingScope.ScopeClaimVerdict.Confirmed "")
                                       Key = keyLabel e.Relationship
                                       Verdict = verdict } : TransferImpact.RelationalRole)))
                            |> Map.ofList
                        let inputs : TransferImpact.Inputs =
                            { Catalog = sinkContract
                              Scope = scope.Nodes
                              Reconciled = scope.Reconciled
                              Wiped = wipeSet
                              BusinessKeys = businessKeys
                              Before = before
                              After = after
                              Ignore = opts.ReconcileIgnore |> List.choose (fun n -> match Name.create n with Ok v -> Some v | Error _ -> None) |> Set.ofList
                              Roles = roles }
                        let strategyLabel = match opts.Emission with EmissionMode.WipeAndLoad -> "replace (wipe & load)" | _ -> "merge (upsert)"
                        let impact = TransferImpact.build flowName strategyLabel inputs
                        // THE TRIAGE (2026-07-10, the manifest program, slice 1):
                        // classify each relational unit from the signals already
                        // in hand — no new probe, no re-segmentation — so the
                        // artifact folds the proven-settled to one line each and
                        // foregrounds the open/coupled units. Fails toward
                        // foregrounding (any signal ⇒ Open*).
                        let escapeKinds = escapes |> List.map (fun e -> e.Kind) |> Set.ofList
                        let redVerdictKinds =
                            roles
                            |> Map.toList
                            |> List.choose (fun (k, r) ->
                                match r.Verdict with
                                | Some v when TransferTriage.isDriftVerdict v -> Some k
                                | _ -> None)
                            |> Set.ofList
                        let divergenceKinds = report.StaticLookupDivergences |> List.map (fun d -> d.Kind) |> Set.ofList
                        let destructiveKinds =
                            impact.Summary
                            |> List.choose (fun r -> if r.Context.Added > 0 || r.Context.Deleted > 0 then Some r.Kind else None)
                            |> Set.ofList
                            |> Set.union wipeSet
                        let units = TransferTriage.unitsOf escapeKinds redVerdictKinds divergenceKinds destructiveKinds staticLookupKeys impact.Segments
                        System.IO.Directory.CreateDirectory "go-board" |> ignore
                        let htmlPath = System.IO.Path.Combine ("go-board", sprintf "%s.impact.html" flowName)
                        let jsonPath = System.IO.Path.Combine ("go-board", sprintf "%s.impact.json" flowName)
                        System.IO.File.WriteAllText (htmlPath, TransferImpactView.toHtmlTriaged sinkContract units impact)
                        System.IO.File.WriteAllText (jsonPath, TransferImpactView.toJsonTriaged sinkContract units impact)
                        let truncNote = if List.isEmpty truncated then "" else sprintf " (capped at %d row(s): %s)" impactCap (String.concat ", " truncated)
                        let openCount = units |> List.filter (fun u -> not (TransferTriage.isSettled u.Triage)) |> List.length
                        items.Add (GoBoard.item "impact"
                            (GoBoard.Status.Advisory (sprintf "--impact: written to %s (+ .json twin) — %d unit(s), of which %d are open and %d settled; +%d added, -%d deleted, ~%d changed, %d unchanged%s." htmlPath (List.length units) openCount (List.length units - openCount) impact.Totals.Added impact.Totals.Deleted impact.Totals.Changed impact.Totals.Unchanged truncNote)))
                // MATCH DRIFT (2026-07-08): reconcile matches IDENTITY and
                // never rewrites data — target values are KEPT — so matched
                // pairs whose columns differ are surfaced, with the
                // reconcileIgnore move named for expected audit drift.
                // The match-drift axis speaks ONLY to NON-static-lookup reconcile
                // drift (advisory — the sink value is KEPT). Static-lookup kinds are
                // owned by the dedicated airtight axis below (the strict, bidirectional,
                // set-complete identity), so they are excluded here to avoid a
                // double report on the same table.
                if not (Map.isEmpty reconciliation) then
                    let nonLookupDivs = report.ReconcileDivergences |> List.filter (fun d -> not (Set.contains d.Kind staticLookupKeys))
                    match nonLookupDivs with
                    | [] ->
                        items.Add (GoBoard.item "match drift"
                            (GoBoard.Status.Green
                                (match opts.ReconcileIgnore with
                                 | [] -> "matched source/target rows carry identical values in every compared column."
                                 | ignored -> sprintf "matched source/target rows carry identical values (ignored audit fields: %s)." (String.concat ", " ignored))))
                    | ds ->
                        let detailLines =
                            [ for d in ds do
                                let matched =
                                    report.Kinds
                                    |> List.tryFind (fun k -> k.Kind = d.Kind)
                                    |> Option.map (fun k -> k.RowsMatched)
                                    |> Option.defaultValue 0
                                yield sprintf "%s.%s differs on %d of %d matched row(s):" (nm d.Kind) (Name.value d.Column) d.DifferingPairs matched
                                for (ak, srcV, sinkV) in d.Samples do
                                    yield sprintf "  target key %s: source '%s' vs target '%s'" (AssignedKey.value ak) srcV sinkV
                                yield sprintf "  -> expected audit drift? add \"%s\" to the flow's reconcileIgnore. Genuine content divergence needs a data decision — the transfer will not resolve it." (Name.value d.Column) ]
                        items.Add (GoBoard.itemWith "match drift"
                            (GoBoard.Status.Advisory (sprintf "%d column(s) differ between matched source/target rows — target values are KEPT (reconcile matches identity; it never rewrites data)." ds.Length))
                            detailLines)
                // THE STATIC-LOOKUP IDENTITY (2026-07-09, the guarantee-hardening
                // program): a `static-lookup` entry asserts the two environments hold
                // the IDENTICAL dataset for the table. The airtight verdict reads the
                // engine's `report.StaticLookupDivergences` (the SAME computation the
                // live Execute refuses on — the two-traversal): any value drift, an
                // extra sink row, or a missing row is a hard RED. Honors the flow's
                // reconcileIgnore (env-specific audit fields are not part of "identical").
                if not (Set.isEmpty staticLookupKeys) then
                    match report.StaticLookupDivergences with
                    | [] ->
                        items.Add (GoBoard.item "static lookup"
                            (GoBoard.Status.Green (sprintf "%d static-lookup table(s) hold the identical dataset across the environments — matched by business key, every non-key column agrees, no extra or missing rows." (Set.count staticLookupKeys))))
                    | divs ->
                        let detailLines =
                            [ for d in divs do
                                yield sprintf "%s:" (nm d.Kind)
                                for cd in d.ColumnDrifts do
                                    yield sprintf "  column %s differs on %d matched row(s):" (Name.value cd.Column) cd.DifferingPairs
                                    for (ak, srcV, sinkV) in cd.Samples do
                                        yield sprintf "    key %s: source '%s' vs target '%s'" (AssignedKey.value ak) srcV sinkV
                                if not (List.isEmpty d.ExtraOnTarget) then
                                    yield sprintf "  %d row(s) on the target the source does not hold (extra): %s" d.ExtraOnTarget.Length (d.ExtraOnTarget |> List.truncate 5 |> String.concat ", ")
                                if not (List.isEmpty d.MissingOnTarget) then
                                    yield sprintf "  %d row(s) the source holds but the target lacks (missing): %s" d.MissingOnTarget.Length (d.MissingOnTarget |> List.truncate 5 |> String.concat ", ") ]
                        items.Add (GoBoard.itemWith "static lookup"
                            (GoBoard.Status.Red (sprintf "%d static-lookup table(s) are NOT identical across the environments — a lookup declared identical has diverged (value drift / extra / missing rows)." divs.Length, "reconcile the reference data so the environments match, or reclassify the table as existing-reference (matched, not asserted-identical); then re-run."))
                            detailLines)
                // THE PLANNED SQL (`--sql`, 2026-07-07): the dry run's plan
                // rendered as the text realization's T-SQL and written
                // beside the board — the exact DML shape, readable before
                // anyone authorizes the run.
                if emitSql then
                    match report.Plan with
                    | None ->
                        items.Add (GoBoard.item "planned sql"
                            (GoBoard.Status.Advisory "--sql: this dry run carried no materialized plan — no artifact written."))
                    | Some plan ->
                        match Transfer.plannedSqlPreview opts.Emission loadSet sinkContract topo plan with
                        | Error errors ->
                            items.Add (GoBoard.itemWith "planned sql"
                                (GoBoard.Status.Advisory "--sql: the plan did not render to T-SQL — the live run is unaffected.")
                                (errors |> List.map (fun e -> e.Message)))
                        | Ok sql ->
                            let path = System.IO.Path.Combine ("go-board", sprintf "%s.planned.sql" flowName)
                            System.IO.Directory.CreateDirectory "go-board" |> ignore
                            System.IO.File.WriteAllText (path, sql)
                            items.Add (GoBoard.item "planned sql"
                                (GoBoard.Status.Advisory (sprintf "written to %s — the wipe (if any), phase-1 inserts, then phase-2 FK re-points; AssignedBySink keys mint at run time." path)))
                if not (List.isEmpty report.UnbreakableCycleFks) then
                    items.Add (GoBoard.itemWith "cycles"
                        (GoBoard.Status.Red (sprintf "%d relationship cycle(s) cannot be broken — the load cannot run as planned." report.UnbreakableCycleFks.Length, "make the cycle's FK columns nullable, or exclude the affected kinds."))
                        (report.UnbreakableCycleFks |> List.map (fun u -> sprintf "%s.%s -> %s" (nm u.Kind) (Name.value u.Column) (nm u.Target))))
                if not (List.isEmpty report.UnmatchedIdentities) then
                    // Full rows (2026-07-08): the operator reads the actual
                    // unmatched records, not just their surrogates.
                    // `UnmatchedRows` and `UnmatchedIdentities` are built in
                    // one loop and sorted by the SAME surrogate, so they're
                    // positionally aligned — the rows carry the detail.
                    let detail =
                        if not (List.isEmpty report.UnmatchedRows)
                        then report.UnmatchedRows |> List.map (fun (k, row) -> sprintf "%s: %s" (nm k) (rowText row))
                        else report.UnmatchedIdentities |> List.map (fun (k, s) -> sprintf "%s source '%s'" (nm k) (SourceKey.value s))
                    items.Add (GoBoard.itemWith "identities"
                        (GoBoard.Status.Red (sprintf "%d source identit(ies) have no target match — a live run halts before any write." report.UnmatchedIdentities.Length, "remediate the user-map / reconcile data, or accept the loss with --allow-drops at run time."))
                        detail)
                elif not (Map.isEmpty reconciliation) then
                    items.Add (GoBoard.item "identities" (GoBoard.Status.Green "every reconciled source identity matches a target row."))
                if not (List.isEmpty report.SkippedReferences) then
                    // Full rows (2026-07-08): each dropped record in full,
                    // plus the reference that failed it. `DroppedRows`
                    // carries the plan-build drops with their rows; a
                    // write-time skip (no row carried) falls back to the
                    // coordinate line.
                    let detail =
                        if not (List.isEmpty report.DroppedRows) then
                            [ for (owner, uref, row) in report.DroppedRows |> List.truncate 5 do
                                yield sprintf "%s: %s" (nm owner) (rowText row)
                                yield sprintf "  -> %s = '%s' matches no %s row in the target" (Name.value uref.Column) (SourceKey.value uref.UnresolvedSource) (nm uref.Target) ]
                        else
                            report.SkippedReferences |> List.truncate 5
                            |> List.map (fun (owner, r) -> sprintf "%s.%s -> %s (source '%s')" (nm owner) (Name.value r.Column) (nm r.Target) (SourceKey.value r.UnresolvedSource))
                    items.Add (GoBoard.itemWith "drops"
                        (GoBoard.Status.Red (sprintf "%d row(s) would drop — a relationship points at an unmatched record (shown first %d in full)." report.SkippedReferences.Length (min 5 report.SkippedReferences.Length), "fix the referenced data, or accept with --allow-drops at run time."))
                        detail)
                // The reconcile-key ambiguity that also exits 9 (2026-07-10): the
                // board previously rendered only cycles / identities / drops, so a
                // dry run carrying an ambiguous reconcile key went GREEN while the
                // same report drove the live `--go` to exit 9. These read the SAME
                // report fields the engine's `hasDrops` counts, so board and engine
                // agree on the exit-9 verdict. `AmbiguousTargetMatchKeys` (a
                // duplicate key on the SINK — a duplicate-email user directory is
                // the real case) is reachable from data; `AmbiguousIdentities` (a
                // duplicate source SURROGATE) is the defensive mirror the run report
                // also carries. `ReplayedPriorDrops` is intentionally NOT an axis
                // here: it is a resumable-run-only cause, and the board's dry run is
                // non-resumable by construction, so the board does not forecast it.
                if not (List.isEmpty report.AmbiguousIdentities) then
                    items.Add (GoBoard.itemWith "ambiguous source keys"
                        (GoBoard.Status.Red
                            (sprintf "%d source record(s) share a reconcile key — only the first binding is kept, and the rest lose their identity and drop from the load." report.AmbiguousIdentities.Length,
                             "make the source reconcile column unique per row, or approve the loss with --allow-drops at run time."))
                        (report.AmbiguousIdentities |> List.truncate 5 |> List.map (fun (k, s) -> sprintf "%s source '%s'" (nm k) (SourceKey.value s))))
                if not (List.isEmpty report.AmbiguousTargetMatchKeys) then
                    items.Add (GoBoard.itemWith "ambiguous target keys"
                        (GoBoard.Status.Red
                            (sprintf "%d target record(s) share a reconcile key with an older row — the oldest is kept and every reference re-keys onto it, displacing the rest." report.AmbiguousTargetMatchKeys.Length,
                             "pin the intended winner with a ManualOverride (`Module.Entity:Column:=<key>`), or approve the loss with --allow-drops at run time."))
                        (report.AmbiguousTargetMatchKeys |> List.truncate 5 |> List.map (fun (k, a) -> sprintf "%s target '%s' (displaced)" (nm k) (AssignedKey.value a))))
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
            // The PLANNED-WRITE grant evaluation (2026-07-07): the same
            // planned writes the engine's G2 gate enforces, evaluated per
            // transferred table against object-scope EFFECTIVE permissions
            // (database OR object grants cover; a managed-cloud principal
            // carrying object-scope DML with no database-scope grant is a
            // [ GO ]). Reconciled parents additionally need SELECT (the
            // match-by-key reads them).
            let plannedWrites = Transfer.plannedTransferWrites scope opts.Emission sinkContract
            let readTables =
                Catalog.allKinds sinkContract
                |> List.filter (fun k -> Set.contains k.SsKey scope.Nodes)
                |> List.map (fun k -> TableId.schemaText k.Physical, TableId.tableText k.Physical)
                |> List.distinct
            let probeTables =
                (plannedWrites |> List.map (fun w -> w.Schema, w.Table)) @ readTables |> List.distinct
            (match (Preflight.captureGrantEvidenceFor probeTables sink).GetAwaiter().GetResult() with
             | Error errors ->
                 items.Add (GoBoard.itemWith "grant" (GoBoard.Status.Red ("the grant probe failed.", "check the sink connection/principal; then re-run.")) (errors |> List.map (fun e -> e.Message)))
             | Ok evidence ->
                 let writeViolations =
                     Preflight.permissionViolations plannedWrites evidence
                     |> List.map (fun v -> sprintf "%s on %s" (Preflight.permissionName v.Action) v.Object)
                 let readViolations =
                     readTables
                     |> List.filter (fun (schema, table) -> not (Preflight.coversPermissionOn schema table "SELECT" evidence))
                     |> List.map (fun (schema, table) -> sprintf "SELECT on %s.%s" schema table)
                 match writeViolations @ readViolations with
                 | [] ->
                     items.Add (GoBoard.item "grant"
                         (GoBoard.Status.Green
                             (sprintf
                                 "the sink principal covers the planned writes (%d table(s), database or object scope) and SELECT on the touched set — table-level DENYs would show here."
                                 (plannedWrites |> List.map (fun w -> w.Schema, w.Table) |> List.distinct |> List.length))))
                 | violations ->
                     items.Add (GoBoard.itemWith "grant"
                         (GoBoard.Status.Red ("the sink principal does not cover the planned writes.", "grant the missing permission(s) — database scope or per-table object scope both satisfy the gate; then re-run."))
                         (violations |> List.truncate 10)))
            // Pinned owners (2026-07-06, the single-owner program): every
            // `Table:=<key>` / `Table:Column:=<key>` rule names a sink row
            // that must EXIST — probe each against the live sink so the
            // missing-owner refusal is an early red line, not an execute-time
            // halt.
            let pinnedOwners =
                let rec keysOf (st: ReconciliationStrategy) =
                    match st with
                    | ReconciliationStrategy.FallbackToAssigned (k, primary) -> k :: keysOf primary
                    | _ -> []
                reconciliation
                |> Map.toList
                |> List.collect (fun (kindKey, st) -> keysOf st |> List.map (fun k -> kindKey, k))
            if not (List.isEmpty pinnedOwners) then
                let renderKey (k: string) =
                    match System.Int64.TryParse k with
                    | true, _ -> k
                    | _ -> System.String.Concat("N'", k.Replace("'", "''"), "'")
                let missing =
                    pinnedOwners
                    |> List.choose (fun (kindKey, AssignedKey key) ->
                        match Catalog.tryFindKind kindKey sinkContract with
                        | None -> None
                        | Some k ->
                            match k.Attributes |> List.tryFind (fun a -> a.IsPrimaryKey) with
                            | None -> None
                            | Some pk ->
                                let n = probeCount sink (sprintf "SELECT COUNT_BIG(*) FROM [%s].[%s] WHERE [%s] = %s;" (TableId.schemaText k.Physical) (TableId.tableText k.Physical) (ColumnRealization.columnNameText pk.Column) (renderKey key))
                                if n = 0 then Some (sprintf "%s := '%s' — no such row in the sink" (Name.value k.Name) key) else None)
                if List.isEmpty missing then
                    items.Add (GoBoard.item "pinned owners" (GoBoard.Status.Green (sprintf "%d pinned owner(s) exist in the sink." pinnedOwners.Length)))
                else
                    items.Add (GoBoard.itemWith "pinned owners"
                        (GoBoard.Status.Red ("a pinned owner names a sink row that does not exist — every re-keyed reference would dangle.", "create the row in the sink, or fix the pinned key; then re-run."))
                        missing)
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
                 // The wipe-blocker probe (INBOUND references, distinct from
                 // the outbound `relationships` axis): a sink table OUTSIDE
                 // the transferred subset that holds rows pointing INTO a
                 // subset table would have those rows orphaned when the wipe
                 // deletes the parent — SQL Server refuses the DELETE (FK
                 // 547). This is a wipe-only, sink-state concern; the
                 // `relationships` GO above is about the subset's OWN
                 // outbound FKs, a different direction.
                 let subsetSet = subsetKinds |> List.map (fun k -> k.SsKey) |> Set.ofList
                 let kindName (key: SsKey) =
                     match Catalog.tryFindKind key sinkContract with
                     | Some k -> Name.value k.Name
                     | None -> SsKey.rootOriginal key
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
                                 if n > 0L then Some (sprintf "%s has %d row(s) whose %s points at %s (in the transferred set) — the wipe of %s would orphan them" (Name.value child.Name) n (Name.value a.Name) (kindName r.TargetKind) (kindName r.TargetKind)) else None)))
                 if List.isEmpty blockers then
                     items.Add (GoBoard.item "re-run" (GoBoard.Status.Green "strategy replace: the transferred set wipes child-first and reloads — idempotent; no other sink table references it, so nothing blocks the wipe."))
                 else
                     items.Add (GoBoard.itemWith "re-run"
                         (GoBoard.Status.Red ("a sink table OUTSIDE the transfer references a table INSIDE it — the replace-wipe would fail (FK 547) because deleting the referenced rows orphans the outside rows.", "add the referencing table(s) to the flow's `tables`, or clear their referencing rows on the sink first; then re-run."))
                         blockers)
             | _ ->
                 items.Add (GoBoard.item "re-run" (GoBoard.Status.Green "the transferred set is empty on the sink — first load; both strategies behave identically.")))
        // THE WRITE-SIGNOFF GREENLIGHT (2026-07-08): a destructive WIPE (strategy
        // replace/fresh → WipeAndLoad) is an OPEN DECISION until the flow declares
        // the mode greenlit in `signoff`; a declared table scope must COVER the
        // tables the wipe deletes (verified against the SAME forecast the board
        // shows, so board and engine cannot drift). The engine mirrors this refusal
        // (`transfer.writeSignoff.ungreenlit`) at the live-write seam.
        (match opts.Emission with
         | EmissionMode.WipeAndLoad ->
             // The wiped set is the WRITE kinds (the DELETE targets), the same
             // `scope.WriteKinds` the engine's live gate reads — NOT `Nodes`
             // (which folds in reconciled kinds that are matched, never wiped).
             // A pure authorization check over the plan, independent of sink
             // state; board and engine share `TransferScope.create` so the
             // "covered" verdict cannot drift.
             let wipedTables =
                 Catalog.allKinds sinkContract
                 |> List.filter (fun k -> Set.contains k.SsKey scope.WriteKinds)
                 |> List.map (fun k -> Name.value k.Name)
             let approved = WriteSignoff.approvedModes opts.Signoff
             // A WipeAndLoad is either `replace` or `fresh`; accept whichever the
             // flow declared (prefer `replace`, the config-`strategy` common case).
             let mode =
                 if Set.contains WriteSignoff.WriteMode.Fresh approved && not (Set.contains WriteSignoff.WriteMode.Replace approved)
                 then WriteSignoff.WriteMode.Fresh else WriteSignoff.WriteMode.Replace
             let status =
                 match WriteSignoff.verify flowName opts.Signoff mode wipedTables with
                 | WriteSignoff.Confirmed note -> GoBoard.Status.Green (sprintf "the %s wipe is greenlit — %s" (WriteSignoff.modeLabel mode) note)
                 | WriteSignoff.Missing (reason, remedy)       -> GoBoard.Status.Red (reason, remedy)
                 | WriteSignoff.ScopeMismatch (reason, remedy) -> GoBoard.Status.Red (reason, remedy)
             items.Add (GoBoard.itemWith "signoff" status (wipedTables |> List.map (sprintf "wipes %s")))
         | EmissionMode.Incremental ->
             // Upsert-only — no destructive wipe, so no wipe greenlight is required.
             // Identity-insert (the FullRights reverse-leg path, not this env→env
             // board) is gated at the ENGINE over the plan-derived
             // `identityInsertTables` (T1.5, `transfer.writeSignoff.ungreenlit`);
             // the go board is env→env-scoped (AssignedBySink), so it never drives
             // an identity-insert flow — the reverse-leg probe sheet is its surface.
             items.Add (GoBoard.item "signoff" (GoBoard.Status.Green "no destructive wipe — merge/incremental is upsert-only; no write-mode greenlight required.")))
        // The guided-plan pointer (2026-07-08): the go board VERDICTS readiness;
        // `check plan` walks the strategy options + the WHY of each. Advisory, so it
        // never affects the verdict — just names the companion surface.
        items.Add (GoBoard.item "strategy options"
            (GoBoard.Status.Advisory
                (sprintf "weigh the write strategy (merge = upsert-only / replace = wipe-and-load / fresh) and every other transfer decision with `projection check plan %s`." flowName)))
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
// THE TRANSFER PLAN (2026-07-08, the guided-wizard program) — the DECLARATIVE
// counterpart to the go board. `check go` answers "is this flow executable NOW?";
// `check plan` answers "what path do I want, and WHY?" — it walks each transfer
// decision axis with its alternatives, the tradeoff each carries, and the exact
// config edit. On a real terminal it additionally offers to pick the write
// strategy and PERSISTS the choice to projection.json (the A44 move — an
// interactive choice becomes a hand-reachable config edit); piped / CI is a
// one-shot declarative report on stdout, never a prompt (headless-total).
// ---------------------------------------------------------------------------

let runTransferPlan (flow: string) (plan: TransferPlan.Plan) (asJson: bool) : int =
    if asJson then
        printfn "%s" (TransferPlan.toJsonString plan)
        0
    elif not (Intervene.isInteractive ()) then
        // Piped / CI — the declarative answer on stdout (pipeable / --query-able),
        // no prompt. The config edits are named for hand-editing (the menu).
        TransferPlanView.write Console.Out plan
        0
    else
        // A real terminal — render on channel 2 (stderr, where the prompt lives) so
        // the plan precedes the prompt in reading order, then offer the write-strategy
        // pick (the most consequential axis, the go board's `re-run` decision) and
        // persist it. The label is caller-resolved copy (the Intervene no-copy rule).
        TransferPlanView.write Console.Error plan
        let wordOf (code: string) = code.Substring(code.LastIndexOf('.') + 1)
        match plan.Decisions |> List.tryFind (fun d -> d.Axis = "write strategy") with
        | Some d ->
            let choices : Intervene.Choice<string> list =
                d.Options |> List.map (fun o -> { Code = o.Code; Label = o.Label; Value = wordOf o.Code })
            let currentWord =
                d.Options |> List.tryFind (fun o -> o.Chosen) |> Option.map (fun o -> wordOf o.Code) |> Option.defaultValue "merge"
            let fallback =
                choices |> List.tryFind (fun c -> c.Value = currentWord) |> Option.defaultValue (List.head choices)
            match Intervene.chooseOrDefault "Pick the write strategy — the choice is written to projection.json:" choices fallback with
            | Intervene.Chosen w when w <> currentWord ->
                let path = RelaxationStore.configPath ()
                match RelaxationStore.setFlowString path flow "strategy" w with
                | Ok () ->
                    eprintfn ""
                    eprintfn "Wrote \"strategy\": \"%s\" to %s for flow '%s'." w path flow
                    TransferPlanView.write Console.Error (TransferPlan.reselectStrategy w plan)
                    // The greenlight companion (2026-07-09): a destructive wipe
                    // (`replace`/`fresh` → WipeAndLoad) is REFUSED by the go board
                    // and the engine until the flow declares the mode in `signoff`.
                    // Having just flipped the flow destructive, offer to write the
                    // matching greenlight in the same breath (the A44 move) so the
                    // operator is not left staring at a fresh RED. `merge` needs none.
                    match (match w with
                           | "replace" -> Some WriteSignoff.WriteMode.Replace
                           | "fresh"   -> Some WriteSignoff.WriteMode.Fresh
                           | _         -> None) with
                    | Some mode ->
                        let yes : Intervene.Choice<bool> = { Code = "signoff.write"; Label = "Yes — write the greenlight to projection.json now."; Value = true }
                        let no  : Intervene.Choice<bool> = { Code = "signoff.skip";  Label = "No — the signoff will be declared by hand."; Value = false }
                        let title = sprintf "\"%s\" is a destructive wipe. %s Write the greenlight now?" w (WriteSignoff.impactOf mode)
                        match Intervene.chooseOrDefault title [ yes; no ] no with
                        | Intervene.Chosen true ->
                            let approval = { WriteSignoff.greenlit mode with AcknowledgedImpact = Some (WriteSignoff.impactOf mode) }
                            match RelaxationStore.setFlowSignoff path flow [ approval ] with
                            | Ok () -> eprintfn "Wrote the %s greenlight to %s for flow '%s' — the go board's `signoff` axis now greens." w path flow
                            | Error e -> eprintfn "Could not write the signoff to %s: %s" path e
                        | _ -> ()
                    | None -> ()
                | Error e -> eprintfn "Could not update %s: %s" path e
            | _ -> ()
        | None -> ()
        0

// ---------------------------------------------------------------------------
