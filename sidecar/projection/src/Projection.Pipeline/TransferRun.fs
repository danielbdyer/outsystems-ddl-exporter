namespace Projection.Pipeline

// LINT-ALLOW-FILE: transfer-run orchestration at the boundary — terminal SQL text over
//   validated TableIds, function-local run-state mutables, and `box`/`unbox` at
//   the SqlParameter boundary (BCL APIs that take `obj`). The run output is
//   immutable.

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Core.Passes
open Projection.Adapters.Sql
open Projection.Targets.SSDT

/// Which realization a reverse-leg request runs on — the "best possible
/// realization" policy made STRUCTURAL. The streaming realization
/// dominates the materialized path on every measured axis FOR THE
/// REQUESTS IT ADMITS (faster: ~35.5k vs ~27k rows/sec at the 2026-06-10
/// bench; bounded memory; journal-resumable), so it is chosen
/// AUTOMATICALLY whenever the request admits it; the materialized path
/// carries the combinations streaming does not yet support (a declared
/// table subset, the G10 resumable envelope, WipeAndLoad). An EXPLICIT
/// streaming request on an inadmissible combination refuses BY NAME —
/// never a silent downgrade; a journal on an inadmissible combination
/// likewise (the ledger belongs to the streaming realization).
///
/// **NM-31 — CLOSED (Phase 2, 2026-06-15): the realizations are now
/// drop-symmetric on the pre-write halt.** BOTH arms thread `allowDrops`
/// + a reconciliation map into the ENGINE: a reconciling run with
/// unmatched orphans takes a PRE-WRITE halt (`validateUserMap` /
/// `executeGate`, AC-I5 — the Sink stays untouched) gated on
/// `allowDrops`, whether realized streaming
/// (`runStreamingReconcilingWithRenames`) or materialized (`runCore`'s
/// reconcile leg). The streaming arm gained its reconcile leg (the named
/// follow-on the prior note reserved), so `choose` no longer presents a
/// false symmetry: a non-reconciling run carries an empty reconciliation
/// (the halt never fires; FK orphans still surface POST-write as
/// `SkippedReferences` + the exit-9 verdict via `narrateDropExit`).
[<RequireQualifiedAccess>]
type ReverseLegRealization =
    | Streaming of journalDirectory: string option
    | Materialized

[<RequireQualifiedAccess>]
module ReverseLegRealization =

    /// Pure and total over the request surface: every request lands on a
    /// realization or a NAMED refusal. The selector is deterministic from
    /// the request alone — testable without a connection.
    ///
    /// **NM-31 (closed).** `choose` selects on admissibility (table subset /
    /// resumable / emission), NOT on drop-halt capability — and since Phase 2
    /// it no longer needs to: both realizations carry the reconcile leg + the
    /// `validateUserMap` pre-write halt (the streaming arm gained it), so the
    /// arms are symmetric on the orphan halt. The selector picks the best
    /// realization the request admits; reconcile rides whichever it picks.
    let choose
        (emission: EmissionMode)
        (resumable: bool)
        (tables: string list)
        (streamingRequested: bool)
        (journalDirectory: string option)
        (sinkResidentResumeAvailable: bool)
        : Result<ReverseLegRealization> =
        let admissible =
            List.isEmpty tables && not resumable && emission = EmissionMode.Incremental
        if streamingRequested && not (List.isEmpty tables) then
            Result.failureOf
                (ValidationError.create "transfer.reverseLeg.streamingTablesUnsupported"
                    "the streaming reverse leg loads the whole estate; a declared table subset is the named follow-on. Remove --tables or drop --streaming to run materialized.")
        elif streamingRequested && resumable && sinkResidentResumeAvailable then
            // Slice C2 — the resume chooser reads the sink ARCHETYPE instead of
            // assuming the cloud (ManagedDml) shape. A `FullRights` sink CAN host
            // the G10 sink-resident progress table (CREATE TABLE permitted), so
            // `--resumable` is HONORED on the materialized envelope (the path that
            // carries sink-resident resume) rather than refused. The ManagedDml
            // refusal below stands — its data grant forbids the CREATE TABLE the
            // progress table needs (the original, archetype-correct reasoning).
            Result.success ReverseLegRealization.Materialized
        elif streamingRequested && resumable then
            Result.failureOf
                (ValidationError.create "transfer.reverseLeg.streamingResumableUnsupported"
                    "the streaming reverse leg resumes through its journal, not the G10 marker (whose progress table needs CREATE TABLE the data grant forbids). Replace --resumable with --journal <dir>.")
        elif streamingRequested && emission = EmissionMode.WipeAndLoad then
            Result.failureOf
                (ValidationError.create "transfer.reverseLeg.streamingWipeUnsupported"
                    "the streaming reverse leg is Incremental; the wipe-and-load refresh stays on the materialized path (the wipe must invalidate the journal — the named follow-on).")
        elif Option.isSome journalDirectory && not admissible then
            Result.failureOf
                (ValidationError.create "transfer.reverseLeg.journalRequiresStreaming"
                    "--journal is the streaming realization's chunk-resume ledger; the request's table subset / --resumable / wipe forces the materialized path. Remove those to stream, or drop --journal.")
        elif admissible then Result.success (ReverseLegRealization.Streaming journalDirectory)
        else Result.success ReverseLegRealization.Materialized

    /// Phase 3 — the duplicate-hazard gate (the charter's "small lever"). A
    /// streaming realization with NO journal, run for real (`executeGated`),
    /// has no idempotent envelope: any re-run re-streams and DOUBLES every
    /// sink-minted (AssignedBySink) kind. This refuses that shape BY NAME so
    /// the operator must supply `--journal <dir>` (making the run resumable +
    /// idempotent by construction). Pure and total: a DryRun (not gated), a
    /// journal-bearing stream, and the materialized arm (its own G10 envelope)
    /// all pass. Tested without a connection.
    let executeJournalGate (realization: ReverseLegRealization) (executeGated: bool) : ValidationError option =
        match realization, executeGated with
        | ReverseLegRealization.Streaming None, true ->
            Some
                (ValidationError.create "transfer.reverseLeg.streamingExecuteRequiresJournal"
                    "a streaming --execute needs --journal <dir>: without it a re-run re-streams and DOUBLES every sink-minted kind (no idempotent envelope). Pass --journal <dir> to make the load resumable + idempotent.")
        | _ -> None

/// The Transfer orchestrator — `Compose`'s data-direction sibling. Binds
/// the two legs of the adjunction across two substrates: `Ingestion`
/// (Source → rows) then a Projection-onto-Sink realization (rows → Sink),
/// over one shared `Catalog` (the schema contract).
///
/// **Algebraic shape (chapter A.0' first-principles convergence).** A
/// Transfer is one realization of the fundamental data-load relationship
/// `(Plan, Realization)`: ingest produces raw rows in source identity
/// space; `DataLoadPlan.build` applies the operator-supplied
/// `SurrogateRemapContext` and produces post-substitution rows
/// (`OperatorIntent Insertion`, registered ONCE in `DataLoadPlan`); this
/// orchestrator then *just realizes the plan* — Phase 1 bulk-insert,
/// Phase 2 deferred-FK UPDATEs. Realization is `DataIntent` end-to-end;
/// the remap is invisible here.
[<RequireQualifiedAccess>]
module Transfer =

    /// Whether a run writes to the Sink (`Execute`) or only ingests +
    /// plans + reports (`DryRun`, the safe default for a preview).
    type Mode =
        | DryRun
        | Execute

    /// Per-kind outcome surfaced to the operator.
    type KindOutcome =
        {
            Kind              : SsKey
            Disposition       : IdentityDisposition
            RowsIngested      : int
            DeferredFkColumns : Set<Name>
            RowsWritten       : int
        }

    type TransferReport =
        {
            Mode                : Mode
            Kinds               : KindOutcome list
            UnbreakableCycleFks : UnbreakableCycleFk list
            /// Reconciled-kind Source surrogates with no matched Sink
            /// identity (the per-identity skip-and-diagnose from
            /// `reconcileKind`). Empty for a non-reconciling Transfer.
            UnmatchedIdentities : (SsKey * SourceKey) list
            /// NM-51 — reconciled Source surrogates whose PK column value was
            /// NOT unique among the rows (the second binding refused, first
            /// kept). A data-fidelity hazard surfaced, not silently dropped.
            /// Empty for a non-reconciling Transfer or a unique reconcile key.
            AmbiguousIdentities : (SsKey * SourceKey) list
            /// NM-58 — Sink surrogates displaced by the duplicate-key tiebreaker:
            /// more than one Sink row shared a (non-blank) reconcile match key,
            /// so the oldest (lowest-PK) won and these lost. Surfaced so the
            /// operator can override the specific keys (the production user
            /// directory's "duplicate email groups"). Empty for a unique key or
            /// a non-reconciling Transfer.
            AmbiguousTargetMatchKeys : (SsKey * AssignedKey) list
            /// Source rows dropped at plan-build because a targeted FK
            /// had no matched assigned counterpart — paired with the
            /// owning kind. Empty for a non-reconciling Transfer.
            SkippedReferences   : (SsKey * UnresolvedReference) list
            /// Every capture-ladder rung descent the write took (a sink
            /// capability refusal — e.g. triggers reject OUTPUT-without-
            /// INTO — degraded the lane, named per kind). Empty when every
            /// kind ran its preferred rung.
            CaptureLaneDescents : LaneDescent list
            /// NM-53 — on a G10 resumable NO-OP re-run of a transfer that already
            /// completed, this carries the DROP COUNT the FIRST run recorded
            /// (`Some n`); `None` on a fresh run (the run's own
            /// `SkippedReferences` are the live drops). It exists so the no-op
            /// path REPLAYS a prior exit-9 (FK-orphan) verdict — `hasDrops`
            /// consults it — rather than reporting a misleading clean exit-0. It
            /// is a REPLAYED count, marked as such (the exact prior
            /// `UnresolvedReference`s are not re-listed, only the verdict-bearing
            /// count is persisted in the progress marker).
            ReplayedPriorDrops  : int option
            /// NM-21 — the named `synthetic.fk.unsatisfiable` events σ raised:
            /// a non-nullable FK column forced to NULL because its synthetic
            /// parent pool was empty. Surfaced here so a DryRun preview (which
            /// never reaches the load-time failure) reports the unsatisfiable
            /// structure rather than letting σ's NULL pass silently. Empty for
            /// an ingested Transfer (only the σ path can raise it).
            SyntheticUnsatisfiableFks : SyntheticDiagnostic list
            /// Display-name index (`SsKey -> Name`) for the kinds/columns this
            /// report names — built from the contract catalog at construction, so a
            /// consumer (the CLI narration) reads tables by `Name`, not the GUID
            /// `rootOriginal`. Empty ⇒ the consumer falls back to `rootOriginal`
            /// (byte-identical to pre-displayName behaviour). It is terminal DISPLAY
            /// metadata only — no engine logic reads it (A4: identity stays the SsKey).
            Names : Map<SsKey, string>
        }

    // -- Projection-onto-Sink realization -----------------------------------


    /// The chunk size every capture rung consumes (the staged rungs amortize
    /// one MERGE per chunk; the bench rationale and the rung mechanics live
    /// in `SurrogateCapture`).
    [<Literal>]
    let private CaptureChunkSize = 50_000

    /// Stage-and-capture every chunk of one kind's rows through the capture
    /// LADDER (`SurrogateCapture.captureChunkDescending`), folding each
    /// chunk's `(source → assigned)` pairs into the PACKED remap. The lane
    /// is STICKY per kind: a rung the sink refused once is not re-attempted
    /// on the kind's later chunks. Module-level tail-recursive task
    /// continuation (FS3511 posture). Returns every descent taken — each a
    /// named outcome for the report.
    let rec private captureChunks
        (sink: SqlConnection)
        (kind: Kind)
        (identityAttr: Attribute)
        (deferred: Set<Name>)
        (kindKey: SsKey)
        (remap: PackedSurrogateRemap)
        (lane: CaptureLane)
        (descents: LaneDescent list)
        (chunks: StaticRow list list)
        : Task<LaneDescent list> =
        task {
            match chunks with
            // `descents` accumulates REVERSED (each chunk's `List.rev newDescents`
            // prepended in O(|newDescents|)) and is reversed once here — so the
            // per-chunk fold is O(n), not the O(chunks²) a right-append `@` makes
            // on an orphan/descent-heavy load.
            | [] -> return List.rev descents
            | chunk :: rest ->
                // Single-value bind then destructure — a tuple `let!` is not
                // statically compilable under Release (FS3511).
                let! outcome =
                    SurrogateCapture.captureChunkDescending sink kind kindKey
                        (fun (a: Attribute) -> StaticRow.valueOrEmpty a.Name)
                        identityAttr deferred lane chunk
                let pairs, succeededLane, newDescents = outcome
                pairs |> List.iter (fun (srcVal, assignedVal) -> PackedSurrogateRemap.capture kindKey srcVal assignedVal remap)
                return! captureChunks sink kind identityAttr deferred kindKey remap succeededLane (List.rev newDescents @ descents) rest
        }

    /// Realize the plan onto an open Sink connection, returning any
    /// write-time skip-and-diagnose references (FK values targeting an
    /// `AssignedBySink` kind whose Source surrogate had no captured
    /// assignment). Phase 1 runs in topological order so each
    /// `AssignedBySink` kind's per-row OUTPUT captures feed the FK re-point
    /// of every later referencer; Phase 2 re-points the cycle-deferred FKs
    /// against the completed remap. `PreservedFromSource` /
    /// `ReconciledByRule` loads are byte-identical to the pre-§5.2 path —
    /// the re-point is a no-op when no `AssignedBySink` kind is in scope.

    let private writePlan (sink: SqlConnection) (catalog: Catalog) (plan: DataLoadPlan) (autoRevert: bool) (revertArtifactDir: string option) : Task<(SsKey * UnresolvedReference) list * LaneDescent list> =
        task {
            let assignedBySinkKinds =
                plan.Loads
                |> List.choose (fun l ->
                    if l.Disposition = IdentityDisposition.AssignedBySink then Some l.Kind else None)
                |> Set.ofList
            // The kinds some FK targets — only THEIR minted surrogates have a
            // consumer (a later referencer's re-point). An AssignedBySink kind
            // outside this set needs no capture at all and rides the minted
            // bulk lane (SqlBulkCopy without KeepIdentity — the fast lane).
            let fkTargetKinds =
                Catalog.allKinds catalog
                |> List.collect (fun k -> k.References |> List.map (fun r -> r.TargetKind))
                |> Set.ofList
            // The Source→Sink-minted surrogate map, accumulated as
            // AssignedBySink kinds insert; the PACKED realization-layer store
            // (Dictionary<int64,int64> per kind — the hundreds-of-millions-row estate's
            // FK-target tables would not fit the string-keyed immutable Map).
            // Threaded through the topological Phase-1 loop so referencers
            // re-point against captures made by their (earlier-ordered)
            // targets.
            let remap = PackedSurrogateRemap.create ()
            // ref cells (not `let mutable`): the load body is the staged CE's
            // stage thunk (card S4c), and F# closures cannot capture mutable
            // locals.
            let writeSkips : (SsKey * UnresolvedReference) list ref = ref []
            let laneDescents : LaneDescent list ref = ref []

            // Phase 1 excludes the deferred (cycle) FK columns from the re-point:
            // they are inserted as NULL, and their targets' captures are not
            // complete until the whole kind has loaded — resolving them here
            // would wrongly drop rows. Phase 2 re-points them against the
            // COMPLETED remap (`excluding = Set.empty`).
            let repoint (excluding: Set<Name>) (kind: Kind) (rows: StaticRow list) : RemappedRows =
                let fkTargets =
                    SurrogateRemap.fkColumnsTargeting assignedBySinkKinds kind
                    |> Map.filter (fun col _ -> not (Set.contains col excluding))
                if Map.isEmpty fkTargets then { Rows = rows; Skipped = [] }
                else SurrogateRemap.remapRowFksWith (PackedSurrogateRemap.tryFind remap) fkTargets rows

            // Live "load" stage (§13) — the data-transfer leg streams per-table
            // progress so `--watch` shows "Loading the data · N of M · ~Xs
            // remaining". Rides the existing NDJSON channel; plain machine events
            // when no one is watching.
            let loadTotal = List.length plan.Loads
            let loadSw = System.Diagnostics.Stopwatch.StartNew()
            let loaded = ref 0
            // Card S4c — the load bracket is the `staged { }` CE's
            // (`Spines.transfer`): an exception mid-load now CLOSES the stage
            // `aborted` on the wire (the board line goes Halted) instead of
            // leaving an open `.started`; the per-table progress events are
            // unchanged.
            let loadBody () : Task<Result<unit, ValidationError list>> =
              task {
                for load in plan.Loads do
                    if not (List.isEmpty load.Rows) then
                        match Catalog.tryFindKind load.Kind catalog with
                        | None      -> ()
                        | Some kind ->
                            let remapped = repoint load.DeferredFkColumns kind load.Rows
                            writeSkips.Value <- writeSkips.Value @ (remapped.Skipped |> List.map (fun u -> load.Kind, u))
                            match load.Disposition with
                            | IdentityDisposition.AssignedBySink ->
                                match kind.Attributes |> List.tryFind (fun a -> a.IsPrimaryKey && a.IsIdentity) with
                                | Some _ when not (Set.contains load.Kind fkTargetKinds) ->
                                    // No FK anywhere targets this kind, so its minted
                                    // surrogates have no consumer: skip capture and
                                    // bulk-insert with the identity column excluded —
                                    // the Sink mints, nothing needs the mapping. (A
                                    // cycle member is always FK-targeted by its cycle
                                    // predecessor, so this lane never carries
                                    // deferred columns.)
                                    do! Bulk.copyRowsSinkMinted sink kind.Physical
                                            (TransferCellShaping.toCellRowsExcludingIdentity kind load.DeferredFkColumns remapped.Rows)
                                | Some idAttr ->
                                    let! descents =
                                        captureChunks sink kind idAttr load.DeferredFkColumns load.Kind remap
                                            CaptureLane.StagedMergeOutput []
                                            (remapped.Rows |> List.chunkBySize CaptureChunkSize)
                                    laneDescents.Value <- laneDescents.Value @ descents
                                | None ->
                                    // ofKind only returns AssignedBySink for an IDENTITY PK, so this is
                                    // unreachable; fall back to the bulk path rather than drop the rows.
                                    do! Bulk.copyRows sink kind.Physical (TransferCellShaping.toCellRows kind load.DeferredFkColumns remapped.Rows)
                            | _ ->
                                do! Bulk.copyRows sink kind.Physical (TransferCellShaping.toCellRows kind load.DeferredFkColumns remapped.Rows)
                    loaded.Value <- loaded.Value + 1
                    LogSink.recordStageProgress "load" loaded.Value loadTotal loadSw.ElapsedMilliseconds

                // Phase 2 — re-point the cycle-deferred FK columns against the
                // COMPLETED remap. For an `AssignedBySink` kind the WHERE keys on
                // the ASSIGNED PK (the sink replaced the source PK at insert; the
                // captured remap supplies the translation — the 6.A.2 lift,
                // operator-authorized 2026-06-10). A deferred FK value with no
                // captured target is a NAMED phase-2 erasure: the row stands (it
                // was inserted in phase 1 with the column NULL) but the reference
                // is lost — surfaced in `SkippedReferences`, never silent. A row
                // whose own PK has no capture was dropped (and named) in phase 1;
                // it has no sink row to update, so it is passed over here.
                for load in plan.Loads do
                    if not (Set.isEmpty load.DeferredFkColumns) && not (List.isEmpty load.Rows) then
                        match Catalog.tryFindKind load.Kind catalog with
                        | None      -> ()
                        | Some kind ->
                            let remapped2 = repoint Set.empty kind load.Rows
                            writeSkips.Value <-
                                writeSkips.Value
                                @ (remapped2.Skipped
                                   |> List.filter (fun u -> Set.contains u.Column load.DeferredFkColumns)
                                   |> List.map (fun u -> load.Kind, u))
                            let rowsForUpdate =
                                match load.Disposition, kind.Attributes |> List.tryFind (fun a -> a.IsPrimaryKey && a.IsIdentity) with
                                | IdentityDisposition.AssignedBySink, Some idAttr ->
                                    remapped2.Rows
                                    |> List.choose (fun row ->
                                        match Map.tryFind idAttr.Name row.Values with
                                        | Some srcVal when srcVal <> "" ->
                                            PackedSurrogateRemap.tryFind remap load.Kind srcVal
                                            |> Option.map (fun assigned ->
                                                { row with Values = Map.add idAttr.Name assigned row.Values })
                                        | _ -> None)
                                | _ -> remapped2.Rows
                            let updates = rowsForUpdate |> List.choose (TransferCellShaping.phase2UpdateSql kind load.DeferredFkColumns)
                            if not (List.isEmpty updates) then
                                do! Deploy.executeBatch sink (String.concat "\n" updates)

                return Ok ()
              }
            let! verdict =
                staged Spines.transfer {
                    do! Staged.stage Stages.load loadBody
                    return ()
                }
            match verdict.Disposition with
            | RunCompleted () -> return writeSkips.Value, laneDescents.Value
            | RunStopped _ ->
                // The body never returns Error; total match, named.
                return invalidOp "writePlan: the load body cannot stop"
            | RunAborted (_, Some ex) ->
                // Build A — the data-leg compensating-undo / revert-script. The
                // `remap` holds the keys the sink minted so far; revert by captured
                // key (child-first), executing it (autoRevert) or writing it as an
                // artifact, then re-raise the ORIGINAL failure (the load DID fail;
                // pre-existing rows are untouched). With both levers off this is
                // byte-identical to the prior bare re-raise.
                do! TransferRevert.runRevert sink autoRevert revertArtifactDir (TransferRevert.buildRevertScript catalog plan remap)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw()
                return Unchecked.defaultof<_>
            | RunAborted (refusal, None) -> return failwith refusal
        }

    // -- D10 / G10 (Wave 3) — the wipe-and-load + resumable write envelopes ---
    //
    // Both wrap the unchanged `writePlan` (the hardened two-phase realization);
    // neither rewrites it. D10 is the operator-selected full refresh; G10 is the
    // crash-safe resumable/idempotent load (phase-tracked, NOT a single
    // all-or-nothing transaction envelope).


    /// **G10 — the resumable/idempotent envelope around `writePlan`.** A
    /// completed transfer (its marker present) is a NO-OP on re-run. Otherwise
    /// the plan's tables are cleared FK-first — so a partial prior attempt
    /// leaves NO duplicates — and reloaded via the unchanged `writePlan`, then
    /// the completion marker is written. A mid-load failure leaves the marker
    /// UNSET, so re-running the same command resumes to a complete,
    /// duplicate-free state. Phase-tracked + idempotent (the resolved fork),
    /// not a single all-or-nothing transaction envelope.
    ///
    /// NM-53 — the third return component is the REPLAYED prior-drop count:
    /// `None` on a fresh run (the write's own `writeSkips` ARE the drops); on a
    /// no-op re-run of a completed transfer, `Some n` re-surfaces the drop count
    /// the first run recorded, so `hasDrops` / exit-9 REPLAYS rather than reading
    /// a misleading clean exit-0. The marker records the count (not the full
    /// drop-set — re-listing the exact `UnresolvedReference`s would need a codec
    /// that ripples too far; the count replays the VERDICT, and the report marks
    /// it as a replay, not as freshly-observed drops).
    let private writePlanResumable (sink: SqlConnection) (catalog: Catalog) (plan: DataLoadPlan) (topo: TopologicalOrder) (loadSet: Set<SsKey> option) (autoRevert: bool) (revertArtifactDir: string option) : Task<(SsKey * UnresolvedReference) list * LaneDescent list * int option> =
        task {
            do! Deploy.executeBatch sink TransferResume.progressTableSql
            let marker = TransferResume.planMarker catalog plan
            let! prior = TransferResume.markedDropCount sink marker
            match prior with
            | Some priorDrops ->
                // Completed already: no-op the write, but REPLAY the prior drop
                // verdict so a re-run does not silently report a clean exit-0.
                return [], [], Some priorDrops
            | None ->
                do! TransferResume.wipeFkOrdered sink catalog plan topo loadSet
                let! (writeSkips, laneDescents) = writePlan sink catalog plan autoRevert revertArtifactDir
                // The drop count = plan-build drops + this write's drops; the same
                // sum `SkippedReferences` (and thus `hasDrops`) sees this run.
                let dropCount = plan.SkippedReferences.Length + writeSkips.Length
                do! TransferResume.markComplete sink marker dropCount
                return writeSkips, laneDescents, None
        }

    // -- 6.A.3: surrogate-capture refusal (fail-loud, not silent) -------------
    //
    // The `AssignedBySink` shape the capture path cannot honor, detected from
    // the built plan + the schema contract and refused at Execute time rather
    // than silently mis-keyed — *total decisions, named skips*. A pure
    // decision (no connection) so the data canary and the fast-pool unit test
    // witness the SAME refusal (the 6.A.1 pattern). The former 6.A.2 refusal
    // (cyclic AssignedBySink) was LIFTED 2026-06-10, operator-authorized:
    // Phase 2 now re-points the deferred FK through the completed remap AND
    // keys its WHERE on the assigned PK, so the shape loads correctly.

    /// 6.A.3 — `AssignedBySink` kinds whose primary key spans more than one
    /// column. `insertCaptureRow` captures a single `IsPrimaryKey && IsIdentity`
    /// column and `SourceKey`/`AssignedKey` are single-string, so a composite
    /// surrogate is silently truncated to one leg. Refuse. (Representing a
    /// composite surrogate as a tuple key is the named follow-on.)
    let compositeAssignedBySinkKinds (catalog: Catalog) (plan: DataLoadPlan) : SsKey list =
        plan.Loads
        |> List.choose (fun l ->
            if l.Disposition <> IdentityDisposition.AssignedBySink then None
            else
                Catalog.tryFindKind l.Kind catalog
                |> Option.bind (fun k ->
                    let pkCount = k.Attributes |> List.filter (fun a -> a.IsPrimaryKey) |> List.length
                    if pkCount > 1 then Some l.Kind else None))

    /// The first Execute-time refusal a built plan triggers, or `None` when
    /// the plan is cleanly executable. Folds the existing unsatisfiable-cycle
    /// check together with the 6.A.3 surrogate-capture refusal so the
    /// orchestrator has one pre-write gate and the order of precedence is
    /// explicit (structural unsatisfiability first, then the capture shape).
    let executeGate (catalog: Catalog) (plan: DataLoadPlan) : ValidationError option =
        if not (DataLoadPlan.isSatisfiable plan) then
            Some (ValidationError.create
                    "transfer.unbreakableCycleFk"
                    (sprintf
                        "%d non-deferrable cycle FK(s) — cannot execute a clean two-phase load"
                        plan.UnbreakableCycleFks.Length))
        else
            match compositeAssignedBySinkKinds catalog plan with
            | k :: _ ->
                Some (ValidationError.create
                        "transfer.compositeSurrogateUnsupported"
                        (sprintf
                            "Kind %s is AssignedBySink with a multi-column primary key; surrogate capture is single-column and would truncate the composite key. Refusing rather than half-capture it."
                            (SsKey.rootOriginal k)))
            | [] -> None

    /// AC-I5 — pre-write validate-user-map. A reconciling Transfer whose
    /// user-map leaves Source identities unmatched would, post-write, surface
    /// them via `exitCodeForReport` (exit 9) — *after* the rows landed. This
    /// refuses at Execute time, before any write, so an unmapped orphan is a
    /// pre-write halt (the Sink stays untouched), not a post-write exit. The
    /// gate reads the SAME `reconciled.Unmatched` set the post-write exit reads
    /// (6.A.1), so the two cannot disagree. `allowDrops` (the operator's
    /// `--allow-drops`) downgrades to the existing post-write reported-drop path
    /// — a non-reconciling run has an empty `Unmatched`, so this never fires for
    /// `run`/`runWithRenames`.
    let validateUserMap (allowDrops: bool) (reconciled: ReconciledIdentity) : ValidationError option =
        if allowDrops || List.isEmpty reconciled.Unmatched then None
        else
            let kinds =
                reconciled.Unmatched
                |> List.map (fun (k, _) -> SsKey.rootOriginal k)
                |> List.distinct
                |> List.truncate 3
                |> String.concat ", "
            Some (ValidationError.create
                    "transfer.unmappedIdentities"
                    (sprintf
                        "%d Source identit(ies) have no Sink match in the user-map (kind(s): %s); refusing --execute before any write. Remediate the user-map or pass --allow-drops to accept the loss."
                        reconciled.Unmatched.Length kinds))

    // -- G1 / G2: connection + permission pre-flight (T-VI spanning) ---------
    //
    // The transfer write path opened both endpoints and ran straight into the
    // load with no liveness/credential or grant check (only the in-pipeline CDC
    // gate). A dead/unreachable endpoint surfaced as a mid-load failure; a
    // write-denied sink transferred zero rows and exited clean. These two gates
    // refuse BEFORE any write — G1 (both endpoints live + credentialed,
    // `transfer.connectionUnavailable`) and G2 (the sink grant covers the
    // planned INSERTs, `transfer.insufficientGrant`).

    /// The writes a straight load performs at the sink: one INSERT per kind
    /// (the FK-repoint Phase 2 is an UPDATE on the same tables INSERT covers, so
    /// INSERT is the grant the gate requires). Deterministic — catalog order.
    let private plannedTransferWrites (catalog: Catalog) : Preflight.PlannedWrite list =
        Catalog.allKinds catalog
        |> List.map (fun k ->
            { Preflight.Schema = TableId.schemaText k.Physical
              Preflight.Table  = TableId.tableText k.Physical
              Preflight.Action = Preflight.Insert })
        |> List.distinct

    /// G1 + G2 for an Execute transfer: probe both endpoints (connection
    /// liveness/credential) and the sink grant against the planned INSERTs,
    /// refusing before any write. Re-codes the migrate-named refusals under the
    /// `transfer.*` namespace so the CLI can map them to the connection/
    /// permission exit codes. A grant-probe failure is itself a refusal (a sink
    /// we cannot survey is a sink we will not write to blind).
    let spanningPreflight
        (source: SqlConnection)
        (sink: SqlConnection)
        (catalog: Catalog)
        : Task<Result<unit>> =
        task {
            match! Preflight.connectionPreflight source sink with
            | Error es ->
                return
                    Result.failure
                        (es |> List.map (fun e -> ValidationError.create "transfer.connectionUnavailable" e.Message))
            | Ok () ->
                match! Preflight.captureGrantEvidence sink with
                | Error es ->
                    return
                        Result.failure
                            (es |> List.map (fun e -> ValidationError.create "transfer.grantProbeFailed" e.Message))
                | Ok grant ->
                    match Preflight.permissionPreflight grant (plannedTransferWrites catalog) with
                    | Ok () -> return Ok ()
                    | Error es ->
                        return
                            Result.failure
                                (es |> List.map (fun e -> ValidationError.create "transfer.insufficientGrant" e.Message))
        }

    // -- reconciliation orchestration ---------------------------------------

    /// Reconcile each operator-chosen kind's Source surrogates to the
    /// pre-existing Sink identities. Reads the Sink rows for each
    /// reconciled kind (the Sink is not write-only) and folds the
    /// per-kind results into one remap + the combined unmatched list.
    /// A read-only step — safe in `DryRun`. Re-captures through
    /// `SurrogateRemapContext.capture` so the merged context carries
    /// the construction-time invariant.
    let private reconcileAgainstSink
        (sink: SqlConnection)
        (catalog: Catalog)
        (reconciliation: Map<SsKey, ReconciliationStrategy>)
        (sourceRows: Map<SsKey, StaticRow list>)
        : Task<ReconciledIdentity> =
        task {
            let mutable remap = SurrogateRemapContext.empty
            let mutable unmatched : (SsKey * SourceKey) list = []
            let mutable ambiguous : (SsKey * SourceKey) list = []
            let mutable ambiguousTargets : (SsKey * AssignedKey) list = []
            for KeyValue (kind, strategy) in reconciliation do
                match Catalog.tryFindKind kind catalog with
                | None -> ()
                | Some k ->
                    match Kind.primaryKey k with
                    | pk :: _ ->
                        let srcRows = Map.tryFind kind sourceRows |> Option.defaultValue []
                        let! sinkRows = AsyncStream.toList (Ingestion.streamKindRows sink k)
                        let result = Reconciliation.reconcileKind kind pk.Name strategy srcRows sinkRows
                        for KeyValue (rk, inner) in result.Remap.Assignments do
                            for KeyValue (src, assigned) in inner do
                                match SurrogateRemapContext.capture rk src assigned remap with
                                | Ok r    -> remap <- r
                                // NM-51/NM-52 — record the cross-kind merge
                                // conflict instead of discarding the named error.
                                | Error _ -> ambiguous <- (rk, src) :: ambiguous
                        unmatched <- unmatched @ result.Unmatched
                        ambiguous <- ambiguous @ result.Ambiguous
                        ambiguousTargets <- ambiguousTargets @ result.AmbiguousTargetKeys
                    | [] -> ()
            return { Remap = remap; Unmatched = unmatched; Ambiguous = ambiguous; AmbiguousTargetKeys = ambiguousTargets }
        }

    // -- orchestration ------------------------------------------------------

    let private reportKinds (mode: Mode) (plan: DataLoadPlan) : KindOutcome list =
        plan.Loads
        |> List.map (fun l ->
            // RowsIngested reflects the source-side count; for reconciled
            // kinds the plan zeroed Rows so we'd lose that — but the
            // reconciled-kind set's source count IS the rows that became
            // the remap, not rows that get inserted. The operator-facing
            // distinction: `Rows.Length` is what would be written; for
            // ReconciledByRule that's 0 by design.
            { Kind              = l.Kind
              Disposition       = l.Disposition
              RowsIngested      = l.Rows.Length
              DeferredFkColumns = l.DeferredFkColumns
              RowsWritten       = (match mode with Execute -> l.Rows.Length | DryRun -> 0) })

    /// The write-seam policy (Wave 3): the D10 `EmissionMode` (incremental MERGE
    /// vs operator-selected wipe-and-load) and the G10 resumability flag. The
    /// default is incremental + non-resumable — byte-identical to the pre-Wave-3
    /// write path, so every existing caller is unaffected.
    type WriteOptions =
        { Emission : EmissionMode
          Resumable : bool
          /// The declared load-set (item 5 — golden-data table subset). `None`
          /// loads every kind; `Some s` loads only kinds in `s`, leaving the
          /// rest of the sink untouched (the catalog stays whole for FK
          /// context — only the per-kind row load is restricted).
          LoadSet : Set<SsKey> option
          /// Slice C1 — the identity-disposition policy `DataLoadPlan.buildWith`
          /// applies. `Structural` (the `def` default) is byte-identical; a
          /// `FullRights` sink threads `PreferPreservedKeys` so IDENTITY-PK kinds
          /// preserve their source keys (no capture/remap).
          IdentityPolicy : IdentityPolicy
          /// Option C (operator-authorized 2026-06-15) — re-trust the sink's FKs
          /// after the bulk load. `SqlBulkCopy` (no `CHECK_CONSTRAINTS`) leaves
          /// them `is_not_trusted = 1`; `true` (the `def` default) re-validates
          /// each untrusted FK on a loaded table with `WITH CHECK CHECK
          /// CONSTRAINT`, restoring trust so the sink rounds-trips faithfully —
          /// the post-load scan is one semi-join per FK, guaranteed to succeed
          /// after a faithful transfer (a failure is a LOUD integrity signal, not
          /// silent corruption). `false` keeps raw load throughput at the reverse
          /// leg's hundreds-of-millions-of-rows scale and leaves the FKs
          /// untrusted — the NAMED `ToleratedDivergence.FkTrustNotRestoredOnBulkLoad`.
          /// Covers the MATERIALIZED write path (`writePlan`); the streaming
          /// reverse-leg path is a named Wave-2 follow-on.
          RetrustForeignKeys : bool
          /// Build A — the data-leg compensating-undo. On a mid-load failure the
          /// captured (sink-minted) keys give the precise `DELETE`-by-captured-key
          /// revert. `true` → the engine EXECUTES that revert automatically (the
          /// opt-in `--auto-revert`); `false` (the `def` default) → it does NOT
          /// auto-delete — instead, when `RevertArtifactDir` is set, it writes the
          /// precise revert script to an artifact for the operator to review/run.
          /// Either way the original failure still propagates (the load failed) and
          /// pre-existing rows are never touched (only minted keys are targeted).
          AutoRevert : bool
          /// Where the revert script is written when `AutoRevert = false` (and, as
          /// a record, when it is `true`). `None` (the `def` default) writes no
          /// artifact — byte-identical to the pre-Build-A path. `Some dir` → the
          /// child-first `DELETE`-by-captured-key script lands at
          /// `<dir>/transfer-revert.sql` on a failed load.
          RevertArtifactDir : string option }

    [<RequireQualifiedAccess>]
    module WriteOptions =
        let def : WriteOptions = { Emission = EmissionMode.Incremental; Resumable = false; LoadSet = None; IdentityPolicy = IdentityPolicy.Structural; RetrustForeignKeys = true; AutoRevert = false; RevertArtifactDir = None }
        let resumable : WriteOptions = { def with Resumable = true }
        let ofEmission (mode: EmissionMode) : WriteOptions = { def with Emission = mode }

    let private runCore
        (mode: Mode)
        (allowCdc: bool)
        (allowDrops: bool)
        (source: SqlConnection)
        (sink: SqlConnection)
        (catalog: Catalog)
        (reconciliation: Map<SsKey, ReconciliationStrategy>)
        (ingestion: (Catalog * Map<Name, Name>) option)
        (writeOpts: WriteOptions)
        : Task<Result<TransferReport>> =
        task {
            // Wave-3 slice 3.1 — CDC pre-flight gate. Only an Execute run that
            // writes to the sink is at risk; DryRun and `allowCdc = true` skip
            // the check. The refusal is fail-loud (a structured error), never a
            // silent proceed — writing against a CDC-tracked sink during a
            // Managed-environment preview is exactly the surprise R6 guards against.
            // G0 (AC-G0) — the pre-plan Execute gates compose through ONE
            // mandatory `Preflight.all`, in precedence order (CDC first, then the
            // spanning connection/grant/permission gate G1/G2), short-circuiting on
            // the first refusal. The ENTRY POINT is collapsed; the per-axis exit
            // codes are NOT — each refusal keeps its `transfer.*` code, classified
            // by `Preflight.classify` at the CLI seam. The post-plan structural
            // gates (executeGate → validateUserMap) stay in their precedence-ordered
            // `preWrite` block below: they need the built plan + reconcile, so they
            // cannot join this pre-plan list, but they refuse through the same
            // `ValidationError` / `Preflight.refusalOf` seam. Only an Execute run
            // mutates the sink; DryRun previews without writing, so both gates skip.
            let cdcGate : Task<Result<unit>> =
                task {
                    if mode = Execute && not allowCdc then
                        // NM-54 — an unverifiable CDC state is UNSAFE: a probe
                        // failure (transient SqlException / VIEW DEFINITION denial)
                        // REFUSES the write through the same named-refusal seam,
                        // never proceeds and never crashes.
                        match! ReadSide.cdcTrackedTables sink with
                        | Error es -> return Result.failure es
                        | Ok tracked ->
                            if List.isEmpty tracked then return Ok ()
                            else
                                return
                                    Result.failureOf
                                        (ValidationError.create
                                            "transfer.cdcTrackedSink"
                                            (sprintf
                                                "Sink has %d CDC-tracked table(s) (e.g. %s); refusing --execute. Pass --allow-cdc to override."
                                                (List.length tracked)
                                                (tracked |> List.truncate 3 |> String.concat ", ")))
                    else return Ok ()
                }
            let spanningGate : Task<Result<unit>> =
                task {
                    if mode = Execute then return! spanningPreflight source sink catalog
                    else return Ok ()
                }
            match! Preflight.all [ cdcGate; spanningGate ] with
            | Error es -> return Result.failure es
            | Ok () ->
            let topoLineage : Lineage<TopologicalOrder> = TopologicalOrderPass.runWith TreatAsCycle catalog
            let topo = topoLineage.Value
            // 6.B.2 — RefactorLog-aware ingestion. With a rename context,
            // ingest with the SOURCE contract (old physical columns) and
            // re-point each row's values onto the sink's names (by SsKey,
            // A1-stable) before plan/write, which use `catalog` (the sink
            // contract B). With no rename context, source and sink share the
            // schema and ingestion uses `catalog` directly (byte-identical).
            let ingestCatalog = match ingestion with Some (c, _) -> c | None -> catalog
            let reconciledKinds = reconciliation |> Map.toSeq |> Seq.map fst |> Set.ofSeq
            // Item 5 — the declared table subset (golden data). Restrict the
            // load to the named kinds; the catalog stays whole (FK context),
            // so non-listed sink tables are simply never written (untouched).
            // Reconciled kinds are KEPT even when not listed: `reconcileAgainstSink`
            // needs their SOURCE rows to build the business-key (email) remap, and
            // the plan zeroes them via `reclassifyReconciled` — so they remain
            // never-inserted while the user-FK re-key still resolves (golden:
            // exclude the User family from the copied set, re-key their FKs).
            // PL-10 (S08) — the subset scopes the INGEST itself
            // (`collectInOrderFor` — the existing scoped collector), so a
            // '--tables'-restricted transfer never streams the non-listed
            // kinds' rows from the source only to discard them.
            let ingestScope =
                match writeOpts.LoadSet with
                | Some loadSet -> Set.union loadSet reconciledKinds
                | None -> Set.ofList topo.Order
            let! rawRows = Ingestion.collectInOrderFor ingestScope source ingestCatalog topo
            let rows =
                match ingestion with
                | Some (_, renameMap) when not (Map.isEmpty renameMap) ->
                    rawRows |> Map.map (fun _ rs -> RenameProjection.repointRows renameMap rs)
                | _ -> rawRows
            let! reconciled = reconcileAgainstSink sink catalog reconciliation rows
            // The plan-build is the ONE OperatorIntent Insertion site —
            // substitution is applied here, once. After this, every Row
            // is in target identity space.
            let plan =
                DataLoadPlan.buildWith writeOpts.IdentityPolicy catalog topo rows reconciled.Remap
                |> DataLoadPlan.reclassifyReconciled reconciledKinds
            // Pre-write gate, precedence-ordered: structural unsatisfiability /
            // surrogate-capture shapes first (executeGate), then the
            // validate-user-map orphan halt (AC-I5). Both fire only at Execute,
            // before any write.
            let preWrite =
                if mode = Execute then
                    match executeGate catalog plan with
                    | Some refusal -> Some refusal
                    | None         -> validateUserMap allowDrops reconciled
                else None
            match preWrite with
            | Some refusal -> return Result.failureOf refusal
            | None ->
                // Option C — snapshot the AS-DEPLOYED FK trust BEFORE the load, so
                // the post-load restore re-validates only the FKs the bulk load
                // strips (a NoCheckFk decision — untrusted as-deployed — is absent
                // from the snapshot and so preserved). Materialized path only; the
                // streaming reverse-leg is a named Wave-2 follow-on.
                let! preTrustedFks =
                    task {
                        if mode = Execute && writeOpts.RetrustForeignKeys
                        then return! TransferFkTrust.trustedFksOnLoadedTables sink catalog plan
                        else return []
                    }
                let! writeSkips, laneDescents, replayedPriorDrops =
                    task {
                        if mode <> Execute then return [], [], None
                        else
                            match writeOpts.Emission, writeOpts.Resumable with
                            | EmissionMode.WipeAndLoad, _ ->
                                // D10 — operator-selected full refresh: FK-ordered
                                // wipe of the plan's tables, then the standard load.
                                // Restricted to the LoadSet so an excluded family
                                // (golden user-exclusion) is untouched, not wiped.
                                do! TransferResume.wipeFkOrdered sink catalog plan topo writeOpts.LoadSet
                                let! (skips, descents) = writePlan sink catalog plan writeOpts.AutoRevert writeOpts.RevertArtifactDir
                                return skips, descents, None
                            | EmissionMode.Incremental, true ->
                                // G10 — resumable/idempotent envelope. NM-53 — the
                                // third component REPLAYS a prior no-op run's drop
                                // count so exit-9 is not silently lost on re-run.
                                return! writePlanResumable sink catalog plan topo writeOpts.LoadSet writeOpts.AutoRevert writeOpts.RevertArtifactDir
                            | EmissionMode.Incremental, false ->
                                let! (skips, descents) = writePlan sink catalog plan writeOpts.AutoRevert writeOpts.RevertArtifactDir
                                return skips, descents, None
                    }
                // Option C — restore the trust the bulk load stripped (exactly the
                // pre-load snapshot, so NoCheckFk decisions are preserved). A
                // re-validation that fails is a loud integrity signal, not silent.
                if mode = Execute && writeOpts.RetrustForeignKeys then
                    do! TransferFkTrust.restoreFkTrust sink preTrustedFks
                return
                    Result.success
                        { Mode                = mode
                          Kinds               = reportKinds mode plan
                          UnbreakableCycleFks = plan.UnbreakableCycleFks
                          UnmatchedIdentities = reconciled.Unmatched
                          AmbiguousIdentities = reconciled.Ambiguous
                          AmbiguousTargetMatchKeys = reconciled.AmbiguousTargetKeys
                          // Plan-build drops (reconcile misses) + write-time
                          // drops (AssignedBySink FK misses) both surface here.
                          SkippedReferences   = plan.SkippedReferences @ writeSkips
                          CaptureLaneDescents = laneDescents
                          ReplayedPriorDrops  = replayedPriorDrops
                          // NM-21 — only the σ path can raise these; ingested
                          // Transfer never draws against a synthetic pool.
                          SyntheticUnsatisfiableFks = []
                          Names = Catalog.nameIndex catalog }
        }

    /// Run a Transfer over one shared `Catalog` (the schema contract):
    /// ingest rows from the Source, build the identity-aware two-phase
    /// plan, and — when `Execute` — project them onto the Sink. `DryRun`
    /// reports the plan without writing. `Execute` against an
    /// unsatisfiable plan (a non-deferrable cycle FK) fails loudly
    /// rather than attempting a doomed load. Both connections are
    /// caller-supplied and open.
    let run
        (mode: Mode)
        (allowCdc: bool)
        (source: SqlConnection)
        (sink: SqlConnection)
        (catalog: Catalog)
        : Task<Result<TransferReport>> =
        // Non-reconciling: `Unmatched` is always empty, so the validate-user-map
        // gate never fires — `allowDrops = false` is the safe, inert default.
        runCore mode allowCdc false source sink catalog Map.empty None WriteOptions.def

    /// **G10 — a resumable/idempotent Transfer.** Same as `run`, but the write
    /// seam is phase-tracked: a mid-load failure is recoverable by re-running
    /// the same command — the plan's tables are cleared FK-first then reloaded,
    /// and a completion marker makes a finished transfer a no-op. No duplicate
    /// rows on re-run; resumes to complete, duplicate-free state.
    ///
    /// NM-40: TEST-ONLY SEAM — exercises the resumable engine without the
    /// connection apparatus. Production routes through
    /// `runThroughConnectionsResumable`; the only callers are canary tests.
    let runResumable
        (mode: Mode)
        (allowCdc: bool)
        (source: SqlConnection)
        (sink: SqlConnection)
        (catalog: Catalog)
        : Task<Result<TransferReport>> =
        runCore mode allowCdc false source sink catalog Map.empty None WriteOptions.resumable

    /// **D10 — a Transfer under an explicit `EmissionMode`.** `Incremental` is
    /// exactly `run`; `WipeAndLoad` FK-ordered-clears the plan's tables before
    /// the load — the operator-selected full refresh (the `2·|rows|` CDC cost
    /// `EmissionMode` documents). Incremental stays the default everywhere else.
    ///
    /// NM-40: TEST-ONLY SEAM — exercises the emission-mode branch without the
    /// connection apparatus. Production routes through the
    /// `*ThroughConnections*` family; the only callers are canary tests.
    let runWithEmissionMode
        (mode: Mode)
        (emission: EmissionMode)
        (allowCdc: bool)
        (source: SqlConnection)
        (sink: SqlConnection)
        (catalog: Catalog)
        : Task<Result<TransferReport>> =
        runCore mode allowCdc false source sink catalog Map.empty None (WriteOptions.ofEmission emission)

    /// **A Transfer under explicit `WriteOptions`.** The general test seam that
    /// exposes every write-seam lever — including option C's `RetrustForeignKeys`
    /// (default on; pass `{ WriteOptions.def with RetrustForeignKeys = false }` to
    /// keep the bulk load's untrusted FKs, the named `FkTrustNotRestoredOnBulkLoad`
    /// opt-out). Non-reconciling.
    ///
    /// NM-40: TEST-ONLY SEAM — exercises the WriteOptions branch without the
    /// connection apparatus. Production routes through the `*ThroughConnections*`
    /// family; the only callers are canary tests.
    let runWithOptions
        (mode: Mode)
        (writeOpts: WriteOptions)
        (allowCdc: bool)
        (source: SqlConnection)
        (sink: SqlConnection)
        (catalog: Catalog)
        : Task<Result<TransferReport>> =
        runCore mode allowCdc false source sink catalog Map.empty None writeOpts

    /// **Synthetic load (THE_SYNTHETIC_DATA_DESIGN §8, slice S2).** The
    /// synthetic source has **no source DB**: rows are *generated* by the pure
    /// Core `σ` (`SyntheticData.generate`) rather than ingested, then the
    /// transfer's write seam realizes them. So this reuses `DataLoadPlan.build`
    /// → `writePlan` / `wipeFkOrdered` exactly as `runCore` does, but replaces
    /// the `Ingestion.collectInOrder` step with generation — it does **not** go
    /// through `runCore` / `runThroughConnections` (no `source` endpoint, no
    /// spanning two-endpoint preflight). The remap is empty (the generated rows
    /// are already in target identity space). `DryRun` plans + reports without
    /// writing; `Execute` runs the sink CDC gate, the sink grant preflight, and
    /// the load.
    let runSynthetic
        (mode: Mode)
        (emission: EmissionMode)
        (allowCdc: bool)
        (sink: SqlConnection)
        (catalog: Catalog)
        (profile: Profile)
        (config: SyntheticConfig)
        (seed: uint64)
        // F0c-I/O — the boundary realization injected as a pure `rows → rows`
        // transform (the Faker PII pass over the generated tokens). Passed as a
        // closure so Core σ stays pure AND `TransferRun` stays Faker-agnostic
        // (`FakerRealization` compiles AFTER this file). `id` for callers that
        // bless no correction → byte-identical (the π∘σ≈id canary's contract).
        (realize: Map<SsKey, StaticRow list> -> Map<SsKey, StaticRow list>)
        : Task<Result<TransferReport>> =
        task {
            // CDC pre-flight (Execute only) — mirror runCore's sink gate.
            let! cdcGate =
                task {
                    if mode = Execute && not allowCdc then
                        // NM-54 — unverifiable CDC state is UNSAFE: refuse on probe failure.
                        match! ReadSide.cdcTrackedTables sink with
                        | Error es -> return Result.failure es
                        | Ok tracked ->
                            if List.isEmpty tracked then return Ok ()
                            else
                                return
                                    Result.failureOf
                                        (ValidationError.create
                                            "synthetic.cdcTrackedSink"
                                            (sprintf
                                                "Sink has %d CDC-tracked table(s) (e.g. %s); refusing --execute. Pass --allow-cdc to override."
                                                (List.length tracked)
                                                (tracked |> List.truncate 3 |> String.concat ", ")))
                    else return Ok ()
                }
            match cdcGate with
            | Error e -> return Result.failure e
            | Ok () ->
            // Sink-only grant preflight (Execute only). No source endpoint, so
            // the spanning two-connection check does not apply — only the sink
            // must carry the planned INSERT grant.
            let! grantGate =
                task {
                    if mode = Execute then
                        match! Preflight.captureGrantEvidence sink with
                        | Error es ->
                            return
                                Result.failure
                                    (es |> List.map (fun e -> ValidationError.create "synthetic.grantProbeFailed" e.Message))
                        | Ok grant ->
                            match Preflight.permissionPreflight grant (plannedTransferWrites catalog) with
                            | Ok () -> return Ok ()
                            | Error es ->
                                return
                                    Result.failure
                                        (es |> List.map (fun e -> ValidationError.create "synthetic.insufficientGrant" e.Message))
                    else return Ok ()
                }
            match grantGate with
            | Error es -> return Result.failure es
            | Ok () ->
                let topo = (TopologicalOrderPass.runWith TreatAsCycle catalog).Value
                // σ — pure generation in place of ingestion. Rows are already in
                // target identity space, so the remap is empty (identity).
                let rows, syntheticDiags = SyntheticData.generateWithDiagnostics catalog profile config seed
                // F0c-I/O — realize the boundary corrections (Faker over the PII
                // tokens σ emitted) BEFORE planning the load. Identity when the
                // correction is empty (byte-identical to the pre-F0c load).
                let realizedRows = realize rows
                let plan = DataLoadPlan.build catalog topo realizedRows SurrogateRemapContext.empty
                let preWrite = if mode = Execute then executeGate catalog plan else None
                match preWrite with
                | Some refusal -> return Result.failureOf refusal
                | None ->
                    let! writeSkips, laneDescents =
                        task {
                            if mode <> Execute then return [], []
                            else
                                match emission with
                                | EmissionMode.WipeAndLoad ->
                                    // σ generation has no declared subset — wipe all.
                                    do! TransferResume.wipeFkOrdered sink catalog plan topo None
                                    return! writePlan sink catalog plan false None
                                | EmissionMode.Incremental ->
                                    return! writePlan sink catalog plan false None
                        }
                    return
                        Result.success
                            { Mode                = mode
                              Kinds               = reportKinds mode plan
                              UnbreakableCycleFks = plan.UnbreakableCycleFks
                              UnmatchedIdentities = []
                              AmbiguousIdentities = []
                              AmbiguousTargetMatchKeys = []
                              SkippedReferences   = plan.SkippedReferences @ writeSkips
                              CaptureLaneDescents = laneDescents
                              // Synthetic load has no resumable G10 envelope.
                              ReplayedPriorDrops  = None
                              // NM-21 — σ's named unsatisfiable-FK lineage.
                              SyntheticUnsatisfiableFks = syntheticDiags
                              Names = Catalog.nameIndex catalog }
        }

    /// **Live golden apply** (data-portability — the Execute vehicle of
    /// `slice-apply`). The sibling of `runSynthetic`: instead of generating
    /// rows from σ, it takes a pre-built `Map<SsKey, StaticRow list>` (a golden
    /// dataset already mapped onto the TARGET catalog) and runs the SAME Execute
    /// path — the sink CDC gate, the grant preflight, `executeGate`, then
    /// `writePlan` (the capture-and-hoist write: for an AssignedBySink kind it
    /// INSERTs without the PK, captures the sink-minted key, and repoints child
    /// FKs — exactly the managed-cloud path, no IDENTITY_INSERT). `DryRun` plans
    /// + reports without writing; the orphan / cycle diagnostics ride the report
    /// (`SkippedReferences` = post-load orphans, the verification the spec's DoD
    /// names). This is the ADDITIVE (Incremental MERGE) form; the authoritative
    /// scoped-delete RESET stays the emitted T-SQL artifact (`SliceApplyRun.emit`),
    /// which carries the bounded `WHEN NOT MATCHED BY SOURCE … DELETE` arm.
    let runGoldenApply
        (mode: Mode)
        (allowCdc: bool)
        (sink: SqlConnection)
        (catalog: Catalog)
        (rows: Map<SsKey, StaticRow list>)
        : Task<Result<TransferReport>> =
        task {
            let! cdcGate =
                task {
                    if mode = Execute && not allowCdc then
                        match! ReadSide.cdcTrackedTables sink with
                        | Error es -> return Result.failure es
                        | Ok tracked ->
                            if List.isEmpty tracked then return Ok ()
                            else
                                return
                                    Result.failureOf
                                        (ValidationError.create
                                            "slice.apply.cdcTrackedSink"
                                            (sprintf
                                                "Sink has %d CDC-tracked table(s) (e.g. %s); refusing --go. Pass --allow-cdc to override."
                                                (List.length tracked)
                                                (tracked |> List.truncate 3 |> String.concat ", ")))
                    else return Ok ()
                }
            match cdcGate with
            | Error e -> return Result.failure e
            | Ok () ->
            let! grantGate =
                task {
                    if mode = Execute then
                        match! Preflight.captureGrantEvidence sink with
                        | Error es ->
                            return
                                Result.failure
                                    (es |> List.map (fun e -> ValidationError.create "slice.apply.grantProbeFailed" e.Message))
                        | Ok grant ->
                            match Preflight.permissionPreflight grant (plannedTransferWrites catalog) with
                            | Ok () -> return Ok ()
                            | Error es ->
                                return
                                    Result.failure
                                        (es |> List.map (fun e -> ValidationError.create "slice.apply.insufficientGrant" e.Message))
                    else return Ok ()
                }
            match grantGate with
            | Error es -> return Result.failure es
            | Ok () ->
                let topo = (TopologicalOrderPass.runWith TreatAsCycle catalog).Value
                let plan = DataLoadPlan.build catalog topo rows SurrogateRemapContext.empty
                let preWrite = if mode = Execute then executeGate catalog plan else None
                match preWrite with
                | Some refusal -> return Result.failureOf refusal
                | None ->
                    let! writeSkips, laneDescents =
                        task {
                            if mode <> Execute then return [], []
                            else return! writePlan sink catalog plan false None
                        }
                    return
                        Result.success
                            { Mode                = mode
                              Kinds               = reportKinds mode plan
                              UnbreakableCycleFks = plan.UnbreakableCycleFks
                              UnmatchedIdentities = []
                              AmbiguousIdentities = []
                              AmbiguousTargetMatchKeys = []
                              SkippedReferences   = plan.SkippedReferences @ writeSkips
                              CaptureLaneDescents = laneDescents
                              ReplayedPriorDrops  = None
                              SyntheticUnsatisfiableFks = []
                              Names = Catalog.nameIndex catalog }
        }

    /// 6.B.2 — RefactorLog-aware Transfer. The source is at schema A
    /// (`sourceContract`); the sink is at schema B (`sinkContract`). A rename
    /// (table or column) means the two contracts differ on physical
    /// coordinates while the SsKeys are stable (A1). This ingests with the
    /// source contract, re-points every row's values onto the sink's names via
    /// the A→B `CatalogDiff` attribute renames (identity-matched, never
    /// ordinal), and writes against the sink contract. A no-rename pair (A = B
    /// modulo renames) is byte-identical to `run`. Straight load (no
    /// reconciliation); the reconcile + rename combination is the follow-on.
    /// Slice C1 — the policy-bearing `runWithRenames`: a `FullRights` sink threads
    /// `IdentityPolicy.PreferPreservedKeys` so the populate preserves source keys
    /// (no capture/remap). `runWithRenames` fixes `Structural` (byte-identical).
    /// PL-1 (S13) — the rename-aware transfer over a PRECOMPUTED A→B
    /// displacement: migrate-with-data's schema leg already holds
    /// `artifacts.Plan.Diff` over the identical (sinkSource, target) pair,
    /// so the rename map derives from the threaded value instead of a
    /// second whole-catalog `CatalogDiff.between`. Contract: `diff` MUST be
    /// `CatalogDiff.between sourceContract sinkContract`; the diff-less
    /// sibling below computes it and stays the safe entry.
    let runWithRenamesUsing
        (diff: CatalogDiff)
        (identityPolicy: IdentityPolicy)
        (mode: Mode)
        (allowCdc: bool)
        (source: SqlConnection)
        (sink: SqlConnection)
        (sourceContract: Catalog)
        (sinkContract: Catalog)
        : Task<Result<TransferReport>> =
        let renameMap =
            RenameProjection.renames diff |> RenameProjection.renameMap
        runCore mode allowCdc false source sink sinkContract Map.empty (Some (sourceContract, renameMap)) { WriteOptions.def with IdentityPolicy = identityPolicy }

    let runWithRenamesWith
        (identityPolicy: IdentityPolicy)
        (mode: Mode)
        (allowCdc: bool)
        (source: SqlConnection)
        (sink: SqlConnection)
        (sourceContract: Catalog)
        (sinkContract: Catalog)
        : Task<Result<TransferReport>> =
        runWithRenamesUsing
            (CatalogDiff.between sourceContract sinkContract)
            identityPolicy mode allowCdc source sink sourceContract sinkContract

    let runWithRenames
        (mode: Mode)
        (allowCdc: bool)
        (source: SqlConnection)
        (sink: SqlConnection)
        (sourceContract: Catalog)
        (sinkContract: Catalog)
        : Task<Result<TransferReport>> =
        runWithRenamesWith IdentityPolicy.Structural mode allowCdc source sink sourceContract sinkContract

    /// M3.b — the `legacy` B→A reverse leg's engine face (`THE_DATA_PRODUCERS`
    /// LE-1). The source is at the LOGICAL rendition (B, on-prem); the sink is at
    /// the PHYSICAL rendition (A, the OSUSR cloud) of the ONE authored `SsKey`
    /// model. Because both contracts are RENDERED from one model, their SsKeys
    /// align by construction — exactly the precondition `runWithRenames`'s A1-
    /// stable, never-ordinal repoint needs; the two renditions differ only on
    /// physical coordinates (names), which the A→B `CatalogDiff` rename map
    /// re-points. This is a THIN wrapper that names the reverse leg and delegates
    /// to the LE-2-proven `runWithRenames` GIVEN THE TWO CONTRACTS — it does not
    /// produce them. `CatalogRendition.logical` / `.physical` produce them from
    /// the one authored model (J3 closed; the classifier is
    /// `Command.reverseLegOf`). A no-rename pair collapses to a straight load.
    ///
    /// NM-40: TEST-ONLY SEAM — names + delegates the reverse leg without the
    /// connection apparatus. Production routes through
    /// `runReverseLegThroughConnections` / the streaming variant; the only
    /// callers are canary tests.
    let runReverseLeg
        (mode: Mode)
        (allowCdc: bool)
        (source: SqlConnection)
        (sink: SqlConnection)
        (logicalSourceContract: Catalog)
        (physicalSinkContract: Catalog)
        : Task<Result<TransferReport>> =
        runWithRenames mode allowCdc source sink logicalSourceContract physicalSinkContract

    // -- The streaming realization (bounded memory + chunk resume) -----------

    /// The named refusal for a resume whose source changed under the journal.
    /// L2: the typed `LedgerDrift` (the contract's ResumeAdmit disagreement)
    /// maps onto the SAME named refusal — code and message bytes unchanged;
    /// the recorded/recomputed fingerprints ride as metadata (candor gained,
    /// nothing moved).
    let private sourceDriftRefusal (kind: SsKey) (drift: LedgerDrift<string * string * int>) : ValidationError =
        let render (firstPk: string, lastPk: string, rawCount: int) =
            sprintf "firstPk=%s lastPk=%s rawCount=%d" firstPk lastPk rawCount
        ValidationError.createWithMetadata
            "transfer.resume.sourceDrift"
            (sprintf
                "Kind %s chunk %d does not match its journaled fingerprint; the source changed since the journaled run. Refusing to resume over drifted data — clear the journal (or run WipeAndLoad) to reload from scratch."
                (SsKey.rootOriginal kind) drift.Position)
            (Map.ofList
                [ "recordedFingerprint", Some (render drift.Recorded)
                  "recomputedFingerprint", Some (render drift.Recomputed) ])

    /// Per-kind phase-1 streaming totals (rows pulled from the source;
    /// rows that reached the sink — journal-skipped chunks count what the
    /// journaled run wrote).
    type private StreamedKind =
        { Ingested : int
          Written  : int }

    /// Phase 4 — the movement DryRun row-count estimate. The streaming
    /// realization ingests nothing on DryRun (the rows STREAM at write
    /// time), so its preview reported zero rows-would-move; the operator
    /// could not see "N rows would move" before committing. This is the
    /// cheap exact count (`COUNT_BIG(*)` aggregate — one round-trip per
    /// kind, no row scan) the streaming DryRun reports as `RowsIngested`,
    /// the materialized DryRun's row count without the materialization. The
    /// SQL is terminal text over a validated `TableId` (the file's
    /// LINT-ALLOW boundary). `int` of the aggregate is faithful for the
    /// preview surface (`KindOutcome.RowsIngested : int`).
    let private countKindRows (cnn: SqlConnection) (kind: Kind) : Task<int> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <-
                sprintf "SELECT COUNT_BIG(*) FROM [%s].[%s];"
                    (TableId.schemaText kind.Physical) (TableId.tableText kind.Physical)
            let! scalar = cmd.ExecuteScalarAsync()
            return int (unbox<int64> scalar)
        }

    /// The bounded-memory realization of the straight-load plan: per kind,
    /// the source streams in `CaptureChunkSize` chunks through
    /// rename-repoint → FK-repoint (deferred excluded) → the capture
    /// ladder / bulk lanes; only the packed remap and the chunk in flight
    /// are resident. Phase 2 re-STREAMS the deferred kinds against the
    /// completed remap (idempotent UPDATEs — safe to repeat on resume).
    /// With a journal: a journaled chunk whose source fingerprint
    /// (first/last PK + raw count — deterministic because `ReadSide`
    /// orders by PK) matches is skipped and its pairs rebuild the remap; a
    /// mismatch is the named `transfer.resume.sourceDrift` refusal.
    let private writePlanStreaming
        (source: SqlConnection)
        (sink: SqlConnection)
        (ingestCatalog: Catalog)
        (renameMap: Map<Name, Name>)
        (catalog: Catalog)
        (plan: DataLoadPlan)
        (journal: CaptureJournal option)
        // Phase 2 (reconcile ∘ streaming) — the pre-built reconcile remap
        // (Source→pre-existing-Sink surrogate, by the operator ruleset) and
        // the kinds it reconciles. A reconciled kind is `ReconciledByRule`
        // in `plan.Loads` (the sink already owns its rows): its phase-1
        // insert is skipped, and every FK targeting it re-points through
        // `reconcileRemap`. Empty / `Set.empty` is the straight-load path
        // (byte-identical to the pre-reconcile streaming realization).
        (reconcileRemap: SurrogateRemapContext)
        (reconciledKinds: Set<SsKey>)
        : Task<Result<Map<SsKey, StreamedKind> * (SsKey * UnresolvedReference) list * LaneDescent list>> =
        let assignedBySinkKinds =
            plan.Loads
            |> List.choose (fun l -> if l.Disposition = IdentityDisposition.AssignedBySink then Some l.Kind else None)
            |> Set.ofList
        // The FK-target set the re-point resolves against: AssignedBySink
        // kinds (captured during the stream into the packed remap) UNION the
        // reconciled kinds (matched before the stream into `reconcileRemap`).
        // The two are disjoint by kind, so the combined lookup is unambiguous.
        let remapTargetKinds = Set.union assignedBySinkKinds reconciledKinds
        let fkTargetKinds =
            Catalog.allKinds catalog
            |> List.collect (fun k -> k.References |> List.map (fun r -> r.TargetKind))
            |> Set.ofList
        let remap = PackedSurrogateRemap.create ()
        // The memory-lean resume index (byte offsets, not the full record set):
        // each chunk's pairs are read on demand, so a hundreds-of-millions-row
        // resume does not hold the whole journal resident beside the live remap.
        let journalIndex = journal |> Option.map CaptureJournal.openResumeIndex

        // The combined surrogate lookup: a target's assigned key is in the
        // packed remap (AssignedBySink, stream-captured) OR the reconcile
        // remap (ReconciledByRule, pre-matched). A kind lives in exactly one,
        // so the fall-through never double-resolves.
        let lookupAssigned (kindKey: SsKey) (sourceRaw: string) : string option =
            match PackedSurrogateRemap.tryFind remap kindKey sourceRaw with
            | Some assigned -> Some assigned
            | None ->
                SurrogateRemapContext.tryFindAssigned kindKey (SourceKey.ofString sourceRaw) reconcileRemap
                |> Option.map AssignedKey.value

        // Q3: the re-point at the quantum grain — FK ordinals resolved
        // against the stream's renamed basis once per call (cheap; the
        // chunk behind it is 50k rows), cells re-pointed copy-on-write.
        let repoint (basis: RowBasis) (excluding: Set<Name>) (kind: Kind) (rows: RowQuantum list) : RemappedQuanta =
            let fkTargets =
                SurrogateRemap.fkColumnsTargeting remapTargetKinds kind
                |> Map.filter (fun col _ -> not (Set.contains col excluding))
            if Map.isEmpty fkTargets then { Rows = rows; Skipped = [] }
            else
                SurrogateRemap.remapQuantumFksWith
                    lookupAssigned
                    (SurrogateRemap.fkOrdinalsTargeting basis fkTargets)
                    rows

        // One chunk of one kind — a journal skip or a lane write, each
        // ending in a journal append so the chunk is durably done. The
        // chunk's quanta are positional against `basis` — the stream's
        // RENAMED header (Q3: the rename happened once at the basis,
        // never per row); `pkOf` indexes the sink PK's cell through it.
        // The journal fingerprint (first/last PK + raw count) is the same
        // bytes the Map-carried path produced — journals resume across
        // the carrier change.
        let writeChunk (kind: Kind) (load: DataLoadKind) (basis: RowBasis) (pkOf: RowQuantum -> string) (lane: CaptureLane) (chunkIx: int) (chunkRaw: RowQuantum list)
            : Task<Result<int * LaneDescent list * (SsKey * UnresolvedReference) list * CaptureLane>> =
            task {
                let firstPk = chunkRaw |> List.tryHead |> Option.map pkOf |> Option.defaultValue ""
                let lastPk = chunkRaw |> List.tryLast |> Option.map pkOf |> Option.defaultValue ""
                let rawCount = List.length chunkRaw
                let journaled =
                    journalIndex
                    |> Option.bind (fun index ->
                        CaptureJournal.tryFindRecord index (SsKey.rootOriginal load.Kind) chunkIx)
                match journaled with
                | Some record ->
                    // L2 — the journal grain's ResumeAdmit (R3 / RI-3): the
                    // live source slice recomputes the fingerprint; admission
                    // replays the journaled pairs into the shared remap
                    // through the instance's spec (the effectful fold,
                    // adapted at the instance); disagreement is the SAME
                    // named drift refusal as before, never a silent re-run.
                    match Ledger.resumeAdmit (firstPk, lastPk, rawCount) (CaptureJournal.toEntry record) with
                    | Ok admitted ->
                        Ledger.replay (CaptureJournal.spec load.Kind remap) [ admitted ] |> ignore
                        return Result.success ((Verified.value admitted).WrittenCount, [], [], lane)
                    | Error drift ->
                        return Result.failureOf (sourceDriftRefusal load.Kind drift)
                | None ->
                    let remapped = repoint basis load.DeferredFkColumns kind chunkRaw
                    let skips = remapped.Skipped |> List.map (fun u -> load.Kind, u)
                    let! laneOutcome =
                        task {
                            match load.Disposition with
                            | IdentityDisposition.AssignedBySink ->
                                match kind.Attributes |> List.tryFind (fun a -> a.IsPrimaryKey && a.IsIdentity) with
                                | Some _ when not (Set.contains load.Kind fkTargetKinds) ->
                                    do! Bulk.copyRowsSinkMinted sink kind.Physical
                                            (TransferCellShaping.quantumCellRowsExcludingIdentity basis kind load.DeferredFkColumns remapped.Rows)
                                    return [], lane, []
                                | Some idAttr ->
                                    let! outcome =
                                        SurrogateCapture.captureChunkDescending sink kind load.Kind
                                            (fun (a: Attribute) -> RowQuantum.cellGetter basis a.Name)
                                            idAttr load.DeferredFkColumns lane remapped.Rows
                                    let pairs, succeeded, descents = outcome
                                    pairs |> List.iter (fun (src, assigned) -> PackedSurrogateRemap.capture load.Kind src assigned remap)
                                    return pairs, succeeded, descents
                                | None ->
                                    do! Bulk.copyRows sink kind.Physical (TransferCellShaping.quantumCellRows basis kind load.DeferredFkColumns remapped.Rows)
                                    return [], lane, []
                            | _ ->
                                do! Bulk.copyRows sink kind.Physical (TransferCellShaping.quantumCellRows basis kind load.DeferredFkColumns remapped.Rows)
                                return [], lane, []
                        }
                    let pairs, succeededLane, descents = laneOutcome
                    journal
                    |> Option.iter (fun j ->
                        CaptureJournal.append j
                            { Kind = SsKey.rootOriginal load.Kind
                              ChunkIx = chunkIx
                              FirstPk = firstPk
                              LastPk = lastPk
                              RawCount = rawCount
                              WrittenCount = List.length remapped.Rows
                              Pairs = pairs |> List.map (fun (src, assigned) -> [| src; assigned |]) |> List.toArray })
                    return Result.success (List.length remapped.Rows, descents, skips, succeededLane)
            }

        // One-chunk ingest PREFETCH: chunk N+1 starts pulling from the
        // SOURCE while chunk N writes to the SINK (different connections —
        // true overlap; the task CE is hot, so the pull progresses during
        // the write await). On a refusal/crash the in-flight prefetch is
        // abandoned (its read completes or faults unobserved — harmless,
        // the source connection is read-only).
        let rec loadKindChunks (kind: Kind) (load: DataLoadKind) (basis: RowBasis) (pkOf: RowQuantum -> string) (stream: Projection.Adapters.Sql.AsyncStream<RowQuantum>)
                               (pending: Task<RowQuantum list>)
                               (chunkIx: int) (lane: CaptureLane)
                               (ingested: int) (written: int)
                               (skips: (SsKey * UnresolvedReference) list) (descents: LaneDescent list)
            : Task<Result<StreamedKind * (SsKey * UnresolvedReference) list * LaneDescent list>> =
            task {
                let! chunkRaw = pending
                if List.isEmpty chunkRaw then
                    // `skips` / `descents` accumulate REVERSED (each chunk prepended
                    // in O(|new|)); reversed once here so the per-chunk fold is O(n),
                    // not the O(chunks²) a right-append `@` makes on a skip-heavy load.
                    return Result.success ({ Ingested = ingested; Written = written }, List.rev skips, List.rev descents)
                else
                    let nextPending = Projection.Adapters.Sql.AsyncStream.nextBatch CaptureChunkSize stream
                    match! writeChunk kind load basis pkOf lane chunkIx chunkRaw with
                    | Error es -> return Result.failure es
                    | Ok (written', newDescents, newSkips, lane') ->
                        return! loadKindChunks kind load basis pkOf stream nextPending (chunkIx + 1) lane'
                                    (ingested + List.length chunkRaw) (written + written')
                                    (List.rev newSkips @ skips) (List.rev newDescents @ descents)
            }

        let loadTotal = List.length plan.Loads
        let loadSw = System.Diagnostics.Stopwatch.StartNew()

        let rec phase1 (loads: DataLoadKind list) (loaded: int)
                       (totals: Map<SsKey, StreamedKind>)
                       (skips: (SsKey * UnresolvedReference) list)
                       (descents: LaneDescent list)
            : Task<Result<Map<SsKey, StreamedKind> * (SsKey * UnresolvedReference) list * LaneDescent list>> =
            task {
                match loads with
                | [] -> return Result.success (totals, skips, descents)
                | load :: rest when load.Disposition = IdentityDisposition.ReconciledByRule ->
                    // Reconciled kinds skip the phase-1 insert — the sink
                    // already owns their rows; only the FK re-point through
                    // `reconcileRemap` (above) touches them. Counted as
                    // loaded so the stage progress denominator stays whole.
                    LogSink.recordStageProgress "load" (loaded + 1) loadTotal loadSw.ElapsedMilliseconds
                    return! phase1 rest (loaded + 1) totals skips descents
                | load :: rest ->
                    match Catalog.tryFindKind load.Kind catalog, Catalog.tryFindKind load.Kind ingestCatalog with
                    | Some kind, Some ingestKind ->
                        let pkName =
                            kind.Attributes
                            |> List.tryFind (fun a -> a.IsPrimaryKey)
                            |> Option.defaultValue (List.head kind.Attributes)
                            |> fun a -> a.Name
                        // Q3: the rename is a HEADER operation — the source
                        // basis re-keys to sink names once per kind; the
                        // quanta are untouched (the streaming path's
                        // per-row rename walk is deleted, not ported).
                        let basis = RowBasis.rename renameMap (Kind.rowBasis ingestKind)
                        let pkOf = RowQuantum.cellGetter basis pkName
                        let stream = Ingestion.streamKind source ingestKind
                        let firstChunk = Projection.Adapters.Sql.AsyncStream.nextBatch CaptureChunkSize stream
                        match! loadKindChunks kind load basis pkOf stream firstChunk 0 CaptureLane.StagedMergeOutput 0 0 [] [] with
                        | Error es -> return Result.failure es
                        | Ok (streamed, kindSkips, kindDescents) ->
                            LogSink.recordStageProgress "load" (loaded + 1) loadTotal loadSw.ElapsedMilliseconds
                            return! phase1 rest (loaded + 1) (Map.add load.Kind streamed totals) (skips @ kindSkips) (descents @ kindDescents)
                    | _ ->
                        return! phase1 rest (loaded + 1) totals skips descents
            }

        // Phase 2 — re-stream the deferred kinds against the COMPLETED
        // remap; the same semantics as the materialized path (the 6.A.2
        // lift: WHERE keyed on the ASSIGNED PK; an unresolved deferred
        // value is a NAMED erasure). UPDATEs are idempotent, so a resumed
        // run repeating them is harmless.
        let rec phase2Chunks (kind: Kind) (load: DataLoadKind) (basis: RowBasis) (idAttr: Attribute option) (renderUpdate: RowQuantum -> string option)
                             (stream: Projection.Adapters.Sql.AsyncStream<RowQuantum>)
                             (skips: (SsKey * UnresolvedReference) list)
            : Task<(SsKey * UnresolvedReference) list> =
            task {
                let! chunkRaw = Projection.Adapters.Sql.AsyncStream.nextBatch CaptureChunkSize stream
                // `skips` accumulates REVERSED (each chunk prepended in O(|new|)),
                // reversed once at the terminal — O(n), not the O(chunks²) of `@`.
                if List.isEmpty chunkRaw then return List.rev skips
                else
                    let remapped2 = repoint basis Set.empty kind chunkRaw
                    let newSkips =
                        remapped2.Skipped
                        |> List.filter (fun u -> Set.contains u.Column load.DeferredFkColumns)
                        |> List.map (fun u -> load.Kind, u)
                    let rowsForUpdate =
                        match load.Disposition, idAttr with
                        | IdentityDisposition.AssignedBySink, Some idAttr ->
                            // The PK cell re-keys to the ASSIGNED surrogate
                            // (copy-on-write) so the UPDATE's WHERE hits the
                            // sink row. A PK column absent from the basis
                            // can carry no remappable source key — every
                            // row drops, exactly as the Map-carried path's
                            // absent-key lookup dropped them.
                            match RowBasis.tryOrdinal idAttr.Name basis with
                            | None -> []
                            | Some idIx ->
                                remapped2.Rows
                                |> List.choose (fun q ->
                                    match q.Cells.[idIx] with
                                    | "" -> None
                                    | srcVal ->
                                        PackedSurrogateRemap.tryFind remap load.Kind srcVal
                                        |> Option.map (fun assigned ->
                                            let cells = Array.copy q.Cells
                                            cells.[idIx] <- assigned
                                            { Cells = cells }))
                        | _ -> remapped2.Rows
                    let updates = rowsForUpdate |> List.choose renderUpdate
                    if not (List.isEmpty updates) then
                        do! Deploy.executeBatch sink (String.concat "\n" updates)
                    return! phase2Chunks kind load basis idAttr renderUpdate stream (List.rev newSkips @ skips)
            }

        let rec phase2 (loads: DataLoadKind list) (skips: (SsKey * UnresolvedReference) list)
            : Task<(SsKey * UnresolvedReference) list> =
            task {
                match loads with
                | [] -> return skips
                | load :: rest ->
                    // Reconciled kinds are never inserted, so they have no
                    // phase-2 deferred-FK re-stream either.
                    if load.Disposition = IdentityDisposition.ReconciledByRule || Set.isEmpty load.DeferredFkColumns then return! phase2 rest skips
                    else
                        match Catalog.tryFindKind load.Kind catalog, Catalog.tryFindKind load.Kind ingestCatalog with
                        | Some kind, Some ingestKind ->
                            let idAttr = kind.Attributes |> List.tryFind (fun a -> a.IsPrimaryKey && a.IsIdentity)
                            let basis = RowBasis.rename renameMap (Kind.rowBasis ingestKind)
                            let renderUpdate = TransferCellShaping.phase2UpdateSqlQuantum basis kind load.DeferredFkColumns
                            let stream = Ingestion.streamKind source ingestKind
                            let! kindSkips = phase2Chunks kind load basis idAttr renderUpdate stream []
                            return! phase2 rest (skips @ kindSkips)
                        | _ -> return! phase2 rest skips
            }

        task {
            // Card S4c — the load bracket is the `staged { }` CE's
            // (`Spines.transfer`): a phase-1 refusal (e.g. the resume
            // source-drift refusal) now CLOSES the stage `failed` on the wire —
            // the pre-spine code returned early, leaving the board hanging on
            // an open `.started` — and an exception closes it `aborted`.
            let! verdict =
                staged Spines.transfer {
                    let! outcome =
                        Staged.stage Stages.load (fun () ->
                            task {
                                match! phase1 plan.Loads 0 Map.empty [] [] with
                                | Error es -> return Error es
                                | Ok (totals, skips, descents) ->
                                    let! phase2Skips = phase2 plan.Loads []
                                    return Ok (totals, skips @ phase2Skips, descents)
                            })
                    return outcome
                }
            match verdict.Disposition with
            | RunCompleted value -> return Result.success value
            | RunStopped es -> return Result.failure es
            | RunAborted (_, Some ex) ->
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw()
                return Unchecked.defaultof<_>
            | RunAborted (refusal, None) -> return failwith refusal
        }

    /// **The streaming realization, reconcile-capable** — bounded memory for
    /// the estate-scale load. The optional journal directory makes the run
    /// CHUNK-RESUMABLE: a journaled chunk whose source fingerprint matches is
    /// skipped and its captured pairs rebuild the remap; a fingerprint
    /// mismatch refuses by name (`transfer.resume.sourceDrift`); a COMPLETED
    /// run re-runs as a full skip — the streaming path's idempotent re-run
    /// (closing the G3 duplicate hazard whenever a journal is supplied). The
    /// resumed run's report counts the resumed run's work; the journaled run
    /// reported its own drops (exit-9 semantics are per-run). `DryRun` reports
    /// the plan structure (and the reconcile outcome — Unmatched/Ambiguous,
    /// which `reconcileAgainstSink` reads read-only) with zero write counts.
    ///
    /// **Phase 2 (the charter) — reconcile ∘ streaming + the validate-user-map
    /// pre-write halt (closing NM-31 / N4 on the streaming arm).** When
    /// `reconciliation` names kinds (the User family re-keyed by email), the
    /// runner reconciles them against the sink BEFORE the stream (read-only):
    /// each named kind's source rows are matched to the pre-existing sink rows
    /// by the operator ruleset, producing the Source→Sink remap the stream
    /// consults for every FK targeting a reconciled kind. The reconciled kinds
    /// are `ReconciledByRule` (never re-imported — the sink owns them). The
    /// `validateUserMap` gate then HALTS before any write if an orphan source
    /// identity is unmatched (unless `--allow-drops`), so an unmapped user is a
    /// pre-write refusal (`transfer.unmappedIdentities`), not a post-write
    /// drop — the streaming counterpart of the materialized AC-I5 halt. An
    /// empty `reconciliation` is the straight-load path (`allowDrops` inert,
    /// the gate never fires), byte-identical to the pre-reconcile realization.
    let runStreamingReconcilingWithRenames
        (mode: Mode)
        (allowCdc: bool)
        (allowDrops: bool)
        (source: SqlConnection)
        (sink: SqlConnection)
        (sourceContract: Catalog)
        (sinkContract: Catalog)
        (reconciliation: Map<SsKey, ReconciliationStrategy>)
        (journalDirectory: string option)
        // D — the streaming arm of the data-leg compensating-undo (M23 on the
        // estate-scale path). On a mid-stream crash the partial sink-minted rows
        // are reverted (autoRevert) or scripted (revertDir), reconstructing the
        // remap from the off-box journal. Both inert (false / None) → byte-identical
        // to the pre-D streaming realization.
        (autoRevert: bool)
        (revertDir: string option)
        : Task<Result<TransferReport>> =
        task {
            let diff = CatalogDiff.between sourceContract sinkContract
            let renameMap = RenameProjection.renames diff |> RenameProjection.renameMap
            let cdcGate : Task<Result<unit>> =
                task {
                    if mode = Execute && not allowCdc then
                        // NM-54 — unverifiable CDC state is UNSAFE: refuse on probe failure.
                        match! ReadSide.cdcTrackedTables sink with
                        | Error es -> return Result.failure es
                        | Ok tracked ->
                            if List.isEmpty tracked then return Ok ()
                            else
                                return
                                    Result.failureOf
                                        (ValidationError.create
                                            "transfer.cdcTrackedSink"
                                            (sprintf
                                                "Sink has %d CDC-tracked table(s) (e.g. %s); refusing --execute. Pass --allow-cdc to override."
                                                (List.length tracked)
                                                (tracked |> List.truncate 3 |> String.concat ", ")))
                    else return Ok ()
                }
            let spanningGate : Task<Result<unit>> =
                task {
                    if mode = Execute then return! spanningPreflight source sink sinkContract
                    else return Ok ()
                }
            match! Preflight.all [ cdcGate; spanningGate ] with
            | Error es -> return Result.failure es
            | Ok () ->
                let topo = (TopologicalOrderPass.runWith TreatAsCycle sinkContract).Value
                let reconciledKinds = reconciliation |> Map.toSeq |> Seq.map fst |> Set.ofSeq
                // Reconcile the named kinds against the sink (read-only,
                // safe in DryRun): ingest each reconciled kind's SOURCE
                // rows (re-pointed to the sink's names — the only kinds
                // materialized; the estate-scale bulk streams), and match
                // them to the pre-existing SINK rows by the operator
                // ruleset. The reverse leg's User population is small,
                // so this is bounded; the FK-target bulk never materializes.
                let! reconciledSourceRows =
                    task {
                        let mutable acc : Map<SsKey, StaticRow list> = Map.empty
                        for KeyValue (kind, _) in reconciliation do
                            match Catalog.tryFindKind kind sourceContract with
                            | Some ingestKind ->
                                let! rows = AsyncStream.toList (Ingestion.streamKindRows source ingestKind)
                                acc <- Map.add kind (RenameProjection.repointRows renameMap rows) acc
                            | None -> ()
                        return acc
                    }
                let! reconciled = reconcileAgainstSink sink sinkContract reconciliation reconciledSourceRows
                // Structure only — order, dispositions, deferred columns;
                // the rows STREAM at write time (the whole point).
                // `reclassifyReconciled` marks the reconciled kinds skip-
                // insert so the stream never re-imports a sink-owned row.
                let plan =
                    DataLoadPlan.build sinkContract topo Map.empty SurrogateRemapContext.empty
                    |> DataLoadPlan.reclassifyReconciled reconciledKinds
                // Pre-write gates (Execute only), precedence-ordered:
                // structural unsatisfiability first, then the validate-
                // user-map orphan halt (AC-I5 / NM-31 — now ON streaming).
                let preWrite =
                    if mode = Execute then
                        match executeGate sinkContract plan with
                        | Some refusal -> Some refusal
                        | None         -> validateUserMap allowDrops reconciled
                    else None
                match preWrite with
                | Some refusal -> return Result.failureOf refusal
                | None ->
                    if mode <> Execute then
                        // Phase 4 — the movement DryRun preview. Estimate
                        // each kind's rows-would-move with a cheap exact
                        // COUNT (no ingestion): a reconciled kind moves
                        // nothing (ReconciledByRule — the sink owns it), so
                        // it previews 0; every other kind previews its
                        // source count. RowsWritten stays 0 (a preview), and
                        // the reconcile outcome (Unmatched / Ambiguous) rides
                        // the same report — the rekey-map preview.
                        let! previewKinds =
                            task {
                                let mutable acc : KindOutcome list = []
                                for l in plan.Loads do
                                    let! estimate =
                                        task {
                                            if l.Disposition = IdentityDisposition.ReconciledByRule then return 0
                                            else
                                                match Catalog.tryFindKind l.Kind sourceContract with
                                                | Some srcKind -> return! countKindRows source srcKind
                                                | None -> return 0
                                        }
                                    acc <-
                                        { Kind              = l.Kind
                                          Disposition       = l.Disposition
                                          RowsIngested      = estimate
                                          DeferredFkColumns = l.DeferredFkColumns
                                          RowsWritten       = 0 } :: acc
                                return List.rev acc
                            }
                        return
                            Result.success
                                { Mode                = mode
                                  Kinds               = previewKinds
                                  UnbreakableCycleFks = plan.UnbreakableCycleFks
                                  UnmatchedIdentities = reconciled.Unmatched
                                  AmbiguousIdentities = reconciled.Ambiguous
                                  AmbiguousTargetMatchKeys = reconciled.AmbiguousTargetKeys
                                  SkippedReferences   = plan.SkippedReferences
                                  CaptureLaneDescents = []
                                  // Streaming DryRun: no G10 resumable replay.
                                  ReplayedPriorDrops  = None
                                  // NM-21 — streaming Transfer ingests source rows, not σ.
                                  SyntheticUnsatisfiableFks = []
                                  Names = Catalog.nameIndex sourceContract }
                    else
                        let journal =
                            journalDirectory
                            |> Option.map (fun dir -> CaptureJournal.create dir (TransferResume.planMarker sinkContract plan))
                        // Phase 3 — the address-drift guard: if THIS run's
                        // journal file is absent but the directory holds a
                        // prior run's journal under a different plan marker,
                        // resuming would silently orphan it and DOUBLE
                        // committed work. Refuse by name instead.
                        let addressDrift =
                            journal
                            |> Option.map CaptureJournal.siblingJournalsUnderDrift
                            |> Option.defaultValue []
                        if not (List.isEmpty addressDrift) then
                            return Result.failureOf
                                (ValidationError.create "transfer.resume.journalAddressDrift"
                                    (sprintf
                                        "the journal directory holds %d journal(s) under a DIFFERENT plan marker (e.g. %s) but none for this run — the plan changed since the journaled run, so resuming would silently re-stream and DOUBLE committed work. Clear the journal directory to reload from scratch, or restore the prior plan to resume."
                                        (List.length addressDrift)
                                        (addressDrift |> List.truncate 1 |> List.map (fun f -> System.IO.Path.GetFileName f |> nonNull) |> String.concat ", ")))
                        else
                        // D — the streaming data-leg compensating-undo. A mid-stream
                        // chunk crash RE-RAISES out of `writePlanStreaming` (its
                        // `RunAborted (_, Some ex)` branch re-throws — unlike the
                        // materialized arm, whose own failure point is the same
                        // exception branch but is INSIDE `writePlan`). The streaming
                        // failure manifests as a thrown exception, NOT a returned
                        // `Result.failure` — so the compensation hangs off a `with`,
                        // not the `Error` match arm. Replay the off-box journal into a
                        // remap and run the SAME M23 revert the materialized arm runs,
                        // then re-raise the ORIGINAL crash. A NAMED `Error es` (the
                        // resume source-drift refusal) returns below WITHOUT reverting:
                        // that run only skipped/replayed journaled chunks and wrote
                        // nothing new, so deleting by captured key would destroy
                        // PRIOR-run committed rows — the one thing the undo must never do.
                        let! streamed =
                            task {
                                try
                                    let! r = writePlanStreaming source sink sourceContract renameMap sinkContract plan journal reconciled.Remap reconciledKinds
                                    return r
                                with ex ->
                                    do! TransferRevert.runRevertFromJournal sink sinkContract plan journal autoRevert revertDir
                                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw()
                                    return Unchecked.defaultof<_>
                            }
                        match streamed with
                        | Error es -> return Result.failure es
                        | Ok (totals, skips, descents) ->
                            let kinds =
                                plan.Loads
                                |> List.map (fun l ->
                                    let t = Map.tryFind l.Kind totals |> Option.defaultValue { Ingested = 0; Written = 0 }
                                    { Kind              = l.Kind
                                      Disposition       = l.Disposition
                                      RowsIngested      = t.Ingested
                                      DeferredFkColumns = l.DeferredFkColumns
                                      RowsWritten       = t.Written })
                            return
                                Result.success
                                    { Mode                = mode
                                      Kinds               = kinds
                                      UnbreakableCycleFks = plan.UnbreakableCycleFks
                                      UnmatchedIdentities = reconciled.Unmatched
                                      AmbiguousIdentities = reconciled.Ambiguous
                                      AmbiguousTargetMatchKeys = reconciled.AmbiguousTargetKeys
                                      SkippedReferences   = skips
                                      CaptureLaneDescents = descents
                                      // Streaming-journal resume is per-run by
                                      // design (the journaled run reported its
                                      // own drops); no G10 marker replay here.
                                      ReplayedPriorDrops  = None
                                      // NM-21 — streaming Transfer ingests source rows, not σ.
                                      SyntheticUnsatisfiableFks = []
                                      Names = Catalog.nameIndex sourceContract }
        }

    /// The straight-load streaming realization — the non-reconciling default
    /// (sibling-wrapper discipline: supplies the empty reconciliation +
    /// inert `allowDrops` the caller did not name). FK orphans surface
    /// POST-write as `SkippedReferences`; with no reconcile leg the validate-
    /// user-map gate never fires. Byte-identical to the pre-Phase-2 runner.
    let runStreamingWithRenames
        (mode: Mode)
        (allowCdc: bool)
        (source: SqlConnection)
        (sink: SqlConnection)
        (sourceContract: Catalog)
        (sinkContract: Catalog)
        (journalDirectory: string option)
        : Task<Result<TransferReport>> =
        // The straight load supplies the inert revert levers (`false None`) the
        // caller did not name — D's compensating-undo is reachable only through the
        // reconciling / reverse-leg faces that carry the per-environment policy.
        runStreamingReconcilingWithRenames mode allowCdc false source sink sourceContract sinkContract Map.empty journalDirectory false None

    /// The streaming reverse leg through the `TransferConnections`
    /// apparatus (D9) — the bounded-memory sibling of
    /// `runReverseLegThroughConnections` for the estate-scale B→A load.
    /// Both contracts arrive RENDERED from the one authored model
    /// (`CatalogRendition`); the optional journal directory makes the run
    /// chunk-resumable. Incremental semantics — the WipeAndLoad / table-
    /// subset combinations stay on the materialized path (the CLI face
    /// refuses them by name). **Phase 2: reconcile-capable** — a non-empty
    /// `reconciliation` re-keys the named kinds (the User family by email)
    /// with the `validateUserMap` pre-write halt (`allowDrops` gates it);
    /// empty is the straight load.
    let runStreamingReverseLegThroughConnections
        (mode: Mode)
        (allowCdc: bool)
        (allowDrops: bool)
        (journalDirectory: string option)
        (connections: TransferConnections)
        (logicalSourceContract: Catalog)
        (physicalSinkContract: Catalog)
        (reconciliation: Map<SsKey, ReconciliationStrategy>)
        // D — the per-environment revert policy, collapsed at the RunFaces face
        // (`RevertPolicy.toEngine`) to (autoRevert, dir); previously only the
        // materialized reverse-leg face consumed them.
        (autoRevert: bool)
        (revertDir: string option)
        : Task<Result<TransferReport>> =
        task {
            match! ConnectionResolver.openSubstrate connections.Source with
            | Error es -> return Result.failure es
            | Ok source ->
                use source = source
                match! ConnectionResolver.openSubstrate connections.Sink with
                | Error es -> return Result.failure es
                | Ok sink ->
                    use sink = sink
                    return! runStreamingReconcilingWithRenames mode allowCdc allowDrops source sink logicalSourceContract physicalSinkContract reconciliation journalDirectory autoRevert revertDir
        }

    /// Run a *reconciling* Transfer — the operator's headline case
    /// (Dev→UAT User re-key). `reconciliation` names, per kind, how its
    /// Source surrogates reconcile to the *pre-existing* Sink identities
    /// (`ReconciledByRule`): those kinds skip their phase-1 insert, and
    /// every FK pointing at them is re-pointed through the matched remap.
    /// References to identities with no Sink home are dropped at
    /// plan-build and reported in `SkippedReferences`.
    let runReconciling
        (mode: Mode)
        (allowCdc: bool)
        (allowDrops: bool)
        (source: SqlConnection)
        (sink: SqlConnection)
        (catalog: Catalog)
        (reconciliation: Map<SsKey, ReconciliationStrategy>)
        : Task<Result<TransferReport>> =
        runCore mode allowCdc allowDrops source sink catalog reconciliation None WriteOptions.def

    /// AC-I7 — the composed Transfer: a sprint that carries BOTH a column
    /// rename (the source is at schema A, the sink at schema B) AND a
    /// Dev→UAT re-key (`reconciliation`). This threads both legs through the
    /// SINGLE `runCore` path: it derives the A→B rename map from
    /// `CatalogDiff.between sourceContract sinkContract` (as `runWithRenames`
    /// does) and passes the `reconciliation` map (as `runReconciling` does),
    /// so `runCore` re-points each ingested row's values onto the sink's
    /// names by SsKey (A1-stable, never ordinal), THEN reconciles the
    /// re-pointed rows against the sink and re-keys every FK through the
    /// matched remap — in that order, in one run. The two prior entrypoints
    /// are the degenerate corners: `runWithRenames` is this with
    /// `reconciliation = Map.empty`; `runReconciling` is this with no rename
    /// context (A = B). A no-rename pair AND empty reconciliation collapses
    /// to `run`. This composition is what the `runWithRenames`/`runReconciling`
    /// site named "the follow-on."
    /// Slice C1 — the policy-bearing `runReconcilingWithRenames`. A `FullRights`
    /// sink threads `PreferPreservedKeys`; `runReconcilingWithRenames` fixes
    /// `Structural` (byte-identical).
    /// PL-1 (S13) — the reconciling sibling over a PRECOMPUTED displacement
    /// (see `runWithRenamesUsing`; same contract on `diff`).
    let runReconcilingWithRenamesUsing
        (diff: CatalogDiff)
        (identityPolicy: IdentityPolicy)
        (mode: Mode)
        (allowCdc: bool)
        (source: SqlConnection)
        (sink: SqlConnection)
        (sourceContract: Catalog)
        (sinkContract: Catalog)
        (reconciliation: Map<SsKey, ReconciliationStrategy>)
        : Task<Result<TransferReport>> =
        let renameMap =
            RenameProjection.renames diff |> RenameProjection.renameMap
        runCore mode allowCdc false source sink sinkContract reconciliation (Some (sourceContract, renameMap)) { WriteOptions.def with IdentityPolicy = identityPolicy }

    let runReconcilingWithRenamesWith
        (identityPolicy: IdentityPolicy)
        (mode: Mode)
        (allowCdc: bool)
        (source: SqlConnection)
        (sink: SqlConnection)
        (sourceContract: Catalog)
        (sinkContract: Catalog)
        (reconciliation: Map<SsKey, ReconciliationStrategy>)
        : Task<Result<TransferReport>> =
        runReconcilingWithRenamesUsing
            (CatalogDiff.between sourceContract sinkContract)
            identityPolicy mode allowCdc source sink sourceContract sinkContract reconciliation

    let runReconcilingWithRenames
        (mode: Mode)
        (allowCdc: bool)
        (source: SqlConnection)
        (sink: SqlConnection)
        (sourceContract: Catalog)
        (sinkContract: Catalog)
        (reconciliation: Map<SsKey, ReconciliationStrategy>)
        : Task<Result<TransferReport>> =
        runReconcilingWithRenamesWith IdentityPolicy.Structural mode allowCdc source sink sourceContract sinkContract reconciliation

    /// Slice 4.2 — drive a Transfer through the `TransferConnections`
    /// apparatus instead of caller-opened connections. Opens both
    /// substrates via `ConnectionResolver.openSubstrate` (D9: credentials
    /// resolved out of band at the apparatus boundary), reconstructs the
    /// schema contract from the Source (`ReadSide.read`), resolves the
    /// reconciliation against that contract, and runs. The apparatus
    /// carries `ProfiledForIdentity` (Source always; Sink too when
    /// reconciling — the Sink is read, not write-only): the Source open +
    /// the Sink read happen here, one connection per substrate (no
    /// per-table probes; the reconcile reads the Sink via the existing
    /// `reconcileAgainstSink` path).
    ///
    /// `resolveReconciliation` is a function of the reconstructed contract
    /// so the contract is read exactly once (the Source open is not
    /// duplicated to resolve reconciliation specs).
    /// Resolve the declared table subset (logical entity names) to a load-set
    /// of `SsKey`s against the source contract — refusing any name the schema
    /// does not carry (total decisions, named skips). Empty list → `None` (all).
    let private resolveLoadSet (contract: Catalog) (tables: string list) : Result<Set<SsKey> option> =
        if List.isEmpty tables then Result.success None
        else
            let byName =
                Catalog.allKinds contract
                |> List.map (fun k -> (Name.value k.Name).ToLowerInvariant(), k.SsKey)
                |> Map.ofList
            let resolved = tables |> List.map (fun t -> t, Map.tryFind (t.ToLowerInvariant()) byName)
            match resolved |> List.choose (fun (t, o) -> if Option.isNone o then Some t else None) with
            | [] -> Result.success (Some (resolved |> List.choose snd |> Set.ofList))
            | missing ->
                Result.failureOf
                    (ValidationError.create "transfer.tablesUnknown"
                        (sprintf "table subset names not found in the source schema: %s" (String.concat ", " missing)))

    /// The full apparatus-driven entry: the emission mode, the G10 resumable
    /// flag, and the declared table subset, threaded onto the `WriteOptions` the
    /// write seam consumes. `resumable = true` routes the incremental load
    /// through `writePlanResumable` (the idempotent upsert envelope); it is inert
    /// under `WipeAndLoad` (the wipe leg owns the refresh).
    let runThroughConnectionsResumable
        (mode: Mode)
        (emission: EmissionMode)
        (resumable: bool)
        (allowCdc: bool)
        (allowDrops: bool)
        (tables: string list)
        (connections: TransferConnections)
        (resolveReconciliation: Catalog -> Result<Map<SsKey, ReconciliationStrategy>>)
        (autoRevert: bool)
        (revertDir: string option)
        : Task<Result<TransferReport>> =
        task {
            match! ConnectionResolver.openSubstrate connections.Source with
            | Error es -> return Result.failure es
            | Ok source ->
                use source = source
                match! ConnectionResolver.openSubstrate connections.Sink with
                | Error es -> return Result.failure es
                | Ok sink ->
                    use sink = sink
                    // PL-7 (S01): the contract read is schema-only — `read`
                    // materialized ≤100k rows/table into `Modality.Static`
                    // that nothing on the transfer path consumes (the load's
                    // rows stream via `Ingestion.collectInOrderFor`), so the
                    // per-table drain was pure wire waste.
                    match! ReadSide.readSchema source with
                    | Error es -> return Result.failure es
                    | Ok contract ->
                        match resolveLoadSet contract tables with
                        | Error es -> return Result.failure es
                        | Ok loadSet ->
                            match resolveReconciliation contract with
                            | Error es -> return Result.failure es
                            | Ok reconciliation ->
                                return! runCore mode allowCdc allowDrops source sink contract reconciliation None { WriteOptions.ofEmission emission with Resumable = resumable; LoadSet = loadSet; AutoRevert = autoRevert; RevertArtifactDir = revertDir }
        }

    /// The non-resumable form (every existing caller's behavior — byte-identical
    /// to the pre-resumable write path).
    let runThroughConnectionsWithEmission
        (mode: Mode)
        (emission: EmissionMode)
        (allowCdc: bool)
        (allowDrops: bool)
        (tables: string list)
        (connections: TransferConnections)
        (resolveReconciliation: Catalog -> Result<Map<SsKey, ReconciliationStrategy>>)
        : Task<Result<TransferReport>> =
        runThroughConnectionsResumable mode emission false allowCdc allowDrops tables connections resolveReconciliation false None

    /// The incremental-MERGE default (the existing callers' behavior); the
    /// `--how` CLI surface selects `WipeAndLoad` via the `WithEmission` form.
    /// (Sibling-wrapper discipline: the wrapper supplies the default the caller
    /// did not name.)
    let runThroughConnections
        (mode: Mode)
        (allowCdc: bool)
        (allowDrops: bool)
        (connections: TransferConnections)
        (resolveReconciliation: Catalog -> Result<Map<SsKey, ReconciliationStrategy>>)
        : Task<Result<TransferReport>> =
        runThroughConnectionsWithEmission mode EmissionMode.Incremental allowCdc allowDrops [] connections resolveReconciliation

    /// J3 closed — drive the B→A reverse leg through the `TransferConnections`
    /// apparatus (D9: credentials resolved out of band at the apparatus
    /// boundary). Unlike `runThroughConnectionsResumable`, the contract is NOT
    /// read from the live Source: `ReadSide` synthesizes attribute SsKeys from
    /// physical coordinates, so two independent live reads would never align by
    /// identity. Both contracts arrive RENDERED from the ONE authored model
    /// (`CatalogRendition.logical` / `.physical`) — SsKey-aligned by
    /// construction, the precondition the identity-matched repoint needs. The
    /// declared table subset resolves against the source contract by logical
    /// entity name (`Name` is rendition-invariant); the write options thread
    /// the same emission/resumable envelope the peer transfer honors.
    /// **Phase 2: reconcile-capable** — a non-empty `reconciliation` re-keys
    /// the named kinds (the User family by business key) through `runCore`'s
    /// reconcile leg (the same AC-I5 pre-write halt the forward path uses);
    /// empty is the straight load. The streaming sibling
    /// (`runStreamingReverseLegThroughConnections`) is preferred for the
    /// estate-scale combinations; this materialized arm carries the table-
    /// subset / WipeAndLoad / G10-resumable cases the selector routes here.
    let runReverseLegThroughConnectionsWith
        (identityPolicy: IdentityPolicy)
        (mode: Mode)
        (emission: EmissionMode)
        (resumable: bool)
        (allowCdc: bool)
        (allowDrops: bool)
        (tables: string list)
        (connections: TransferConnections)
        (logicalSourceContract: Catalog)
        (physicalSinkContract: Catalog)
        (reconciliation: Map<SsKey, ReconciliationStrategy>)
        (autoRevert: bool)
        (revertDir: string option)
        : Task<Result<TransferReport>> =
        task {
            let diff = CatalogDiff.between logicalSourceContract physicalSinkContract
            let renameMap =
                RenameProjection.renames diff |> RenameProjection.renameMap
            match resolveLoadSet logicalSourceContract tables with
            | Error es -> return Result.failure es
            | Ok loadSet ->
                match! ConnectionResolver.openSubstrate connections.Source with
                | Error es -> return Result.failure es
                | Ok source ->
                    use source = source
                    match! ConnectionResolver.openSubstrate connections.Sink with
                    | Error es -> return Result.failure es
                    | Ok sink ->
                        use sink = sink
                        return! runCore mode allowCdc allowDrops source sink physicalSinkContract reconciliation (Some (logicalSourceContract, renameMap)) { WriteOptions.ofEmission emission with Resumable = resumable; LoadSet = loadSet; IdentityPolicy = identityPolicy; AutoRevert = autoRevert; RevertArtifactDir = revertDir }
        }

    /// The structural-policy reverse leg (byte-identical; the ManagedDml cloud
    /// sink). A `FullRights` reverse-leg sink threads `PreferPreservedKeys` via
    /// `runReverseLegThroughConnectionsWith`.
    let runReverseLegThroughConnections
        (mode: Mode)
        (emission: EmissionMode)
        (resumable: bool)
        (allowCdc: bool)
        (allowDrops: bool)
        (tables: string list)
        (connections: TransferConnections)
        (logicalSourceContract: Catalog)
        (physicalSinkContract: Catalog)
        (reconciliation: Map<SsKey, ReconciliationStrategy>)
        : Task<Result<TransferReport>> =
        runReverseLegThroughConnectionsWith IdentityPolicy.Structural mode emission resumable allowCdc allowDrops tables connections logicalSourceContract physicalSinkContract reconciliation false None

    // -- 6.A.1: the drop-set is fail-loud, not exit-0 -----------------------
    //
    // The red-team's CRITICAL Data #1: a successful Execute that dropped
    // FK-orphan rows (`SkippedReferences`) or left reconciled-kind source
    // surrogates unmatched (`UnmatchedIdentities`) silently exited 0 — a
    // refresh script saw "complete" while rows vanished. That violates
    // *total decisions, named skips*: an erasure must surface. These pure
    // functions name the drop-set and the exit-code policy so the CLI and
    // the data canary witness the *same* decision.

    /// The exit code a completed Transfer maps to when its drop-set is
    /// non-empty and the operator has not declared the drops acceptable.
    /// Distinct from the connection (6) / reconcile (2) / apparatus (3)
    /// failure codes so a refresh script can branch on "rows were dropped."
    [<Literal>]
    let DroppedReferencesExit = 9

    /// The rows a run dropped: FK-orphan referencers skipped at plan-build
    /// or Phase-2 (`SkippedReferences`) plus reconciled-kind Source
    /// surrogates with no Sink match (`UnmatchedIdentities`). Both are data
    /// the Sink will not carry; both must be surfaced, never silently 0.
    let droppedRowCount (report: TransferReport) : int =
        report.SkippedReferences.Length
        + report.UnmatchedIdentities.Length
        + report.AmbiguousIdentities.Length   // NM-51
        + (report.ReplayedPriorDrops |> Option.defaultValue 0)   // NM-53

    /// Whether a completed run lost any rows (the drop-set is non-empty).
    ///
    /// NM-53 — a G10 resumable NO-OP re-run of a transfer that previously dropped
    /// FK-orphans (exit 9) carries the prior count in `ReplayedPriorDrops`; it
    /// counts as drops so the exit-9 verdict REPLAYS rather than reading a
    /// misleading clean exit-0 on the re-run.
    let hasDrops (report: TransferReport) : bool =
        not (List.isEmpty report.SkippedReferences)
        || not (List.isEmpty report.UnmatchedIdentities)
        || not (List.isEmpty report.AmbiguousIdentities)   // NM-51
        || (report.ReplayedPriorDrops |> Option.exists (fun n -> n > 0))   // NM-53

    /// The exit-code policy for a *completed* (Ok) Transfer. A clean run is
    /// 0; a run that dropped rows is `DroppedReferencesExit` (fail-loud)
    /// unless `allowDrops` (the operator's `--allow-drops`, mirroring
    /// `--allow-cdc`) declares the loss acceptable. 6.A.1 — the silent
    /// exit-0 erasure becomes a named refusal.
    let exitCodeForReport (allowDrops: bool) (report: TransferReport) : int =
        if (not allowDrops) && hasDrops report then DroppedReferencesExit else 0

    /// Registry metadata (pillar 9). Bulk/UPDATE realization of a
    /// pre-substituted plan is `DataIntent` (the operator-supplied remap
    /// landed at `DataLoadPlan.build`); the §5.2 `AssignedBySink` capture
    /// site is `OperatorIntent Insertion` because the Sink-minted remap is
    /// discovered *during* the write, not supplied to the plan.
    let registeredMetadata : RegisteredTransformMetadata =
        RegisteredTransformMetadata.emitter "transferProjection" Data
            [ TransformSite.dataIntent "phase1BulkInsert"
                "Phase 1: bulk-insert each plan load's rows (deferred FK columns NULLed). Rows are already post-substitution (`DataLoadPlan.build` is the OperatorIntent Insertion site). Realization of the plan (A36); DataIntent."
              TransformSite.dataIntent "phase2FkRepoint"
                "Phase 2: UPDATE the cycle-deferred FK columns to their plan-side values, keyed by PK, in topological order. Deterministic from the plan; no operator opinion."
              TransformSite.operatorIntent "assignedKeyCapture" Insertion
                "§5.2 Slice E (set-based form, 2026-06-10): for `AssignedBySink` kinds (IDENTITY PK), insert per-batch with `MERGE … OUTPUT S.[__SRC_KEY], INSERTED.<pk>` (omitting the identity column so the Sink mints the surrogate) and capture each Source→assigned surrogate into a `SurrogateRemapContext`; every later referencer's FK targeting the kind is re-pointed via `tryFindAssigned`, skip-and-diagnose on miss. Unlike `DataLoadPlan.build`'s substitution (operator-supplied remap, known pre-build), this remap is discovered *during* the write — the assigned identity does not exist until the Sink mints it — so the site is OperatorIntent Insertion at the realization layer." ]
