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
[<RequireQualifiedAccess>]
type ReverseLegRealization =
    | Streaming of journalDirectory: string option
    | Materialized

[<RequireQualifiedAccess>]
module ReverseLegRealization =

    /// Pure and total over the request surface: every request lands on a
    /// realization or a NAMED refusal. The selector is deterministic from
    /// the request alone — testable without a connection.
    let choose
        (emission: EmissionMode)
        (resumable: bool)
        (tables: string list)
        (streamingRequested: bool)
        (journalDirectory: string option)
        : Result<ReverseLegRealization> =
        let admissible =
            List.isEmpty tables && not resumable && emission = EmissionMode.Incremental
        if streamingRequested && not (List.isEmpty tables) then
            Result.failureOf
                (ValidationError.create "transfer.reverseLeg.streamingTablesUnsupported"
                    "the streaming reverse leg loads the whole estate; a declared table subset is the named follow-on. Remove --tables or drop --streaming to run materialized.")
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
            /// Source rows dropped at plan-build because a targeted FK
            /// had no matched assigned counterpart — paired with the
            /// owning kind. Empty for a non-reconciling Transfer.
            SkippedReferences   : (SsKey * UnresolvedReference) list
            /// Every capture-ladder rung descent the write took (a sink
            /// capability refusal — e.g. triggers reject OUTPUT-without-
            /// INTO — degraded the lane, named per kind). Empty when every
            /// kind ran its preferred rung.
            CaptureLaneDescents : LaneDescent list
        }

    // -- Projection-onto-Sink realization -----------------------------------

    /// Project a kind's already-post-substitution rows into `SqlBulkCopy`
    /// cell rows. Deferred FK columns are emitted as the empty raw —
    /// `KeepNulls` maps that to SQL NULL — so Phase 1 satisfies a cycle;
    /// Phase 2 re-points them.
    let private toCellsOver (attrs: Attribute list) (deferred: Set<Name>) (rows: StaticRow list) : CellValue list list =
        rows
        |> List.map (fun row ->
            attrs
            |> List.map (fun a ->
                let raw =
                    if Set.contains a.Name deferred then ""
                    else Map.tryFind a.Name row.Values |> Option.defaultValue ""
                { Column = ColumnRealization.columnNameText a.Column; Type = a.Type; Raw = raw }))

    let private toCellRows (kind: Kind) (deferred: Set<Name>) (rows: StaticRow list) : CellValue list list =
        toCellsOver kind.Attributes deferred rows

    /// The minted-bulk-lane projection: every attribute EXCEPT the IDENTITY
    /// PK (the Sink mints it; `Bulk.copyRowsSinkMinted` carries no
    /// `KeepIdentity`).
    let private toCellRowsExcludingIdentity (kind: Kind) (deferred: Set<Name>) (rows: StaticRow list) : CellValue list list =
        toCellsOver
            (kind.Attributes |> List.filter (fun a -> not (a.IsPrimaryKey && a.IsIdentity)))
            deferred rows

    /// Q3 — the cell projections at the quantum grain (A40 siblings of
    /// `toCellsOver`; the streaming realization's lanes consume these):
    /// per-column getters are STAGED against the stream's (renamed) basis
    /// once per kind, then applied per row. Deferred FK columns emit the
    /// empty raw (SQL NULL under KeepNulls), exactly as the Map-carried
    /// projection does.
    let private quantumCellsOver (basis: RowBasis) (attrs: Attribute list) (deferred: Set<Name>) (rows: RowQuantum list) : CellValue list list =
        let cols =
            attrs
            |> List.map (fun a ->
                let get =
                    if Set.contains a.Name deferred then (fun _ -> "")
                    else RowQuantum.cellGetter basis a.Name
                ColumnRealization.columnNameText a.Column, a.Type, get)
        rows
        |> List.map (fun q ->
            cols |> List.map (fun (col, ty, get) -> { Column = col; Type = ty; Raw = get q }))

    let private quantumCellRows (basis: RowBasis) (kind: Kind) (deferred: Set<Name>) (rows: RowQuantum list) : CellValue list list =
        quantumCellsOver basis kind.Attributes deferred rows

    /// The minted-bulk-lane projection at the quantum grain — every
    /// attribute EXCEPT the IDENTITY PK (the Sink mints it).
    let private quantumCellRowsExcludingIdentity (basis: RowBasis) (kind: Kind) (deferred: Set<Name>) (rows: RowQuantum list) : CellValue list list =
        quantumCellsOver basis
            (kind.Attributes |> List.filter (fun a -> not (a.IsPrimaryKey && a.IsIdentity)))
            deferred rows

    /// Phase-2 UPDATE for one row: set the deferred FK columns to their
    /// (already remapped, plan-side) values, keyed by the kind's primary
    /// key. `None` when the kind has no PK or no deferred columns.
    let private phase2UpdateSql (kind: Kind) (deferred: Set<Name>) (row: StaticRow) : string option =
        let pkAttrs = kind.Attributes |> List.filter (fun a -> a.IsPrimaryKey)
        let deferredAttrs = kind.Attributes |> List.filter (fun a -> Set.contains a.Name deferred)
        if List.isEmpty pkAttrs || List.isEmpty deferredAttrs then None
        else
            let lit (a: Attribute) =
                Map.tryFind a.Name row.Values
                |> Option.defaultValue ""
                |> SqlLiteral.ofRaw a.Type
                |> SqlLiteral.toString
            let clause (a: Attribute) = sprintf "%s = %s" (Render.quote (ColumnRealization.columnNameText a.Column)) (lit a)
            Some (
                sprintf "UPDATE %s SET %s WHERE %s;"
                    (Render.tableQualified kind.Physical)
                    (deferredAttrs |> List.map clause |> String.concat ", ")
                    (pkAttrs |> List.map clause |> String.concat " AND "))

    /// `phase2UpdateSql` at the quantum grain (Q3): the per-attribute
    /// literal getters are staged against the stream's basis once per
    /// kind; the returned closure renders one row's UPDATE. A kind with
    /// no PK or no deferred columns resolves to a constant `None` closure
    /// once, never per row.
    let private phase2UpdateSqlQuantum (basis: RowBasis) (kind: Kind) (deferred: Set<Name>) : (RowQuantum -> string option) =
        let pkAttrs = kind.Attributes |> List.filter (fun a -> a.IsPrimaryKey)
        let deferredAttrs = kind.Attributes |> List.filter (fun a -> Set.contains a.Name deferred)
        if List.isEmpty pkAttrs || List.isEmpty deferredAttrs then (fun _ -> None)
        else
            let clauseOf (a: Attribute) : RowQuantum -> string =
                let get = RowQuantum.cellGetter basis a.Name
                fun q ->
                    sprintf "%s = %s"
                        (Render.quote (ColumnRealization.columnNameText a.Column))
                        (get q |> SqlLiteral.ofRaw a.Type |> SqlLiteral.toString)
            let setClauses = deferredAttrs |> List.map clauseOf
            let whereClauses = pkAttrs |> List.map clauseOf
            fun q ->
                Some (
                    sprintf "UPDATE %s SET %s WHERE %s;"
                        (Render.tableQualified kind.Physical)
                        (setClauses |> List.map (fun render -> render q) |> String.concat ", ")
                        (whereClauses |> List.map (fun render -> render q) |> String.concat " AND "))

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
            | [] -> return descents
            | chunk :: rest ->
                // Single-value bind then destructure — a tuple `let!` is not
                // statically compilable under Release (FS3511).
                let! outcome =
                    SurrogateCapture.captureChunkDescending sink kind kindKey
                        (fun (a: Attribute) -> StaticRow.valueOrEmpty a.Name)
                        identityAttr deferred lane chunk
                let pairs, succeededLane, newDescents = outcome
                pairs |> List.iter (fun (srcVal, assignedVal) -> PackedSurrogateRemap.capture kindKey srcVal assignedVal remap)
                return! captureChunks sink kind identityAttr deferred kindKey remap succeededLane (descents @ newDescents) rest
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
    let private writePlan (sink: SqlConnection) (catalog: Catalog) (plan: DataLoadPlan) : Task<(SsKey * UnresolvedReference) list * LaneDescent list> =
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
            // (Dictionary<int64,int64> per kind — the 288M-row estate's
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
                                            (toCellRowsExcludingIdentity kind load.DeferredFkColumns remapped.Rows)
                                | Some idAttr ->
                                    let! descents =
                                        captureChunks sink kind idAttr load.DeferredFkColumns load.Kind remap
                                            CaptureLane.StagedMergeOutput []
                                            (remapped.Rows |> List.chunkBySize CaptureChunkSize)
                                    laneDescents.Value <- laneDescents.Value @ descents
                                | None ->
                                    // ofKind only returns AssignedBySink for an IDENTITY PK, so this is
                                    // unreachable; fall back to the bulk path rather than drop the rows.
                                    do! Bulk.copyRows sink kind.Physical (toCellRows kind load.DeferredFkColumns remapped.Rows)
                            | _ ->
                                do! Bulk.copyRows sink kind.Physical (toCellRows kind load.DeferredFkColumns remapped.Rows)
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
                            let updates = rowsForUpdate |> List.choose (phase2UpdateSql kind load.DeferredFkColumns)
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

    /// FK-ordered wipe: DELETE every target table CHILD-FIRST (reverse
    /// topological order) so a foreign-key constraint never blocks the clear.
    /// (`TRUNCATE` is refused by SQL Server on an FK-referenced table regardless
    /// of order, so the child-first DELETE is the FK-safe realization of the
    /// wipe — same end state, the `2·|rows|` CDC cost `EmissionMode` documents.)
    /// The kinds the wipe will DELETE, child-first — the pure core of
    /// `wipeFkOrdered`. The wipe never touches two classes of kind (PE-1 /
    /// P-REKEY — golden user-exclusion holds under *any* strategy, not just
    /// Incremental):
    /// (1) a **`ReconciledByRule`** kind — its sink rows are the sink's OWN
    /// (matched by business key); deleting them would destroy the sink's
    /// inventory (e.g. its users) and the zeroed plan would not re-insert them;
    /// (2) a kind outside `loadSet` (the declared golden subset) — untouched,
    /// not refreshed. `loadSet = None` wipes every non-reconciled loaded kind.
    let wipeTargets (plan: DataLoadPlan) (topo: TopologicalOrder) (loadSet: Set<SsKey> option) : SsKey list =
        let loaded =
            plan.Loads
            |> List.filter (fun l -> l.Disposition <> IdentityDisposition.ReconciledByRule)
            |> List.map (fun l -> l.Kind)
            |> Set.ofList
        let inScope =
            match loadSet with
            | Some ls -> Set.intersect loaded ls
            | None    -> loaded
        List.rev topo.Order |> List.filter (fun k -> Set.contains k inScope)

    let private wipeFkOrdered (sink: SqlConnection) (catalog: Catalog) (plan: DataLoadPlan) (topo: TopologicalOrder) (loadSet: Set<SsKey> option) : Task<unit> =
        task {
            for k in wipeTargets plan topo loadSet do
                match Catalog.tryFindKind k catalog with
                | None      -> ()
                | Some kind ->
                    do! Deploy.executeBatch sink
                            (System.String.Concat("DELETE FROM ", Render.tableQualified kind.Physical, ";"))  // LINT-ALLOW: terminal SQL-text boundary; table name is a validated TableId via Render.tableQualified
        }

    /// The durable phase-marker table — records which transfers completed, so a
    /// re-run of an already-finished transfer is a no-op (idempotent).
    ///
    /// L4 — G10 on the ledger contract (R3 / RI-3): this is the DEGENERATE
    /// single-quantum instance, retired as a separate ledger mechanism. One
    /// entry ("the whole run"), fingerprint = the plan signature
    /// (`planMarker`, recomputed from the live plan on every run — the
    /// grain's ResumeAdmit, with equality realized as the SQL set-membership
    /// `isMarked` answers), WriteAdmit positional at `markComplete` (after
    /// `writePlan`, the same control-flow witness as the journal's append).
    /// It exercises NOTHING of the contract's replay machinery, honestly: a
    /// single full-state quantum has no partial sums to rebuild — the sink's
    /// rows ARE the state, and the admitted re-run's no-op IS the resume.
    /// The streaming realization's chunk-grain journal (`CaptureJournal`) is
    /// the non-degenerate sibling; the two stay distinct REALIZATIONS of one
    /// contract, not two mechanisms.
    let private progressTableSql : string =
        "IF OBJECT_ID('dbo.__projection_transfer_progress') IS NULL \
           CREATE TABLE dbo.__projection_transfer_progress \
             ( Marker NVARCHAR(450) NOT NULL PRIMARY KEY, \
               CompletedAt DATETIME2 NOT NULL CONSTRAINT DF___ptp_at DEFAULT SYSUTCDATETIME() );"

    /// A deterministic signature of a plan — the sorted set of target tables it
    /// loads. Two re-runs of the same transfer share it; a different transfer
    /// (different tables) does not.
    let private planMarker (catalog: Catalog) (plan: DataLoadPlan) : string =
        plan.Loads
        |> List.choose (fun l -> Catalog.tryFindKind l.Kind catalog)
        |> List.map (fun k -> Render.tableQualified k.Physical)
        |> List.sort
        |> String.concat "|"

    let private isMarked (sink: SqlConnection) (marker: string) : Task<bool> =
        task {
            use cmd = sink.CreateCommand()
            cmd.CommandText <- "SELECT COUNT(*) FROM dbo.__projection_transfer_progress WHERE Marker = @m;"
            cmd.Parameters.AddWithValue("@m", marker) |> ignore
            let! c = cmd.ExecuteScalarAsync()
            return System.Convert.ToInt32 c > 0
        }

    let private markComplete (sink: SqlConnection) (marker: string) : Task<unit> =
        task {
            use cmd = sink.CreateCommand()
            cmd.CommandText <- "INSERT INTO dbo.__projection_transfer_progress (Marker) VALUES (@m);"
            cmd.Parameters.AddWithValue("@m", marker) |> ignore
            let! _ = cmd.ExecuteNonQueryAsync()
            return ()
        }

    /// **G10 — the resumable/idempotent envelope around `writePlan`.** A
    /// completed transfer (its marker present) is a NO-OP on re-run. Otherwise
    /// the plan's tables are cleared FK-first — so a partial prior attempt
    /// leaves NO duplicates — and reloaded via the unchanged `writePlan`, then
    /// the completion marker is written. A mid-load failure leaves the marker
    /// UNSET, so re-running the same command resumes to a complete,
    /// duplicate-free state. Phase-tracked + idempotent (the resolved fork),
    /// not a single all-or-nothing transaction envelope.
    let private writePlanResumable (sink: SqlConnection) (catalog: Catalog) (plan: DataLoadPlan) (topo: TopologicalOrder) (loadSet: Set<SsKey> option) : Task<(SsKey * UnresolvedReference) list * LaneDescent list> =
        task {
            do! Deploy.executeBatch sink progressTableSql
            let marker = planMarker catalog plan
            let! already = isMarked sink marker
            if already then return [], []
            else
                do! wipeFkOrdered sink catalog plan topo loadSet
                let! outcome = writePlan sink catalog plan
                do! markComplete sink marker
                return outcome
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
                    | [] -> ()
            return { Remap = remap; Unmatched = unmatched; Ambiguous = ambiguous }
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
          LoadSet : Set<SsKey> option }

    [<RequireQualifiedAccess>]
    module WriteOptions =
        let def : WriteOptions = { Emission = EmissionMode.Incremental; Resumable = false; LoadSet = None }
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
            // UAT-preview is exactly the surprise R6 guards against.
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
            let! rawRows = Ingestion.collectInOrder source ingestCatalog topo
            let repointed =
                match ingestion with
                | Some (_, renameMap) when not (Map.isEmpty renameMap) ->
                    rawRows |> Map.map (fun _ rs -> RenameProjection.repointRows renameMap rs)
                | _ -> rawRows
            let reconciledKinds = reconciliation |> Map.toSeq |> Seq.map fst |> Set.ofSeq
            // Item 5 — the declared table subset (golden data). Restrict the
            // load to the named kinds; the catalog stays whole (FK context),
            // so non-listed sink tables are simply never written (untouched).
            // Reconciled kinds are KEPT even when not listed: `reconcileAgainstSink`
            // needs their SOURCE rows to build the business-key (email) remap, and
            // the plan zeroes them via `reclassifyReconciled` — so they remain
            // never-inserted while the user-FK re-key still resolves (golden:
            // exclude the User family from the copied set, re-key their FKs).
            let rows =
                match writeOpts.LoadSet with
                | Some loadSet ->
                    repointed |> Map.filter (fun k _ -> Set.contains k loadSet || Set.contains k reconciledKinds)
                | None -> repointed
            let! reconciled = reconcileAgainstSink sink catalog reconciliation rows
            // The plan-build is the ONE OperatorIntent Insertion site —
            // substitution is applied here, once. After this, every Row
            // is in target identity space.
            let plan =
                DataLoadPlan.build catalog topo rows reconciled.Remap
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
                let! writeSkips, laneDescents =
                    task {
                        if mode <> Execute then return [], []
                        else
                            match writeOpts.Emission, writeOpts.Resumable with
                            | EmissionMode.WipeAndLoad, _ ->
                                // D10 — operator-selected full refresh: FK-ordered
                                // wipe of the plan's tables, then the standard load.
                                // Restricted to the LoadSet so an excluded family
                                // (golden user-exclusion) is untouched, not wiped.
                                do! wipeFkOrdered sink catalog plan topo writeOpts.LoadSet
                                return! writePlan sink catalog plan
                            | EmissionMode.Incremental, true ->
                                // G10 — resumable/idempotent envelope.
                                return! writePlanResumable sink catalog plan topo writeOpts.LoadSet
                            | EmissionMode.Incremental, false ->
                                return! writePlan sink catalog plan
                    }
                return
                    Result.success
                        { Mode                = mode
                          Kinds               = reportKinds mode plan
                          UnbreakableCycleFks = plan.UnbreakableCycleFks
                          UnmatchedIdentities = reconciled.Unmatched
                          AmbiguousIdentities = reconciled.Ambiguous
                          // Plan-build drops (reconcile misses) + write-time
                          // drops (AssignedBySink FK misses) both surface here.
                          SkippedReferences   = plan.SkippedReferences @ writeSkips
                          CaptureLaneDescents = laneDescents }
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
                let rows = SyntheticData.generate catalog profile config seed
                let plan = DataLoadPlan.build catalog topo rows SurrogateRemapContext.empty
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
                                    do! wipeFkOrdered sink catalog plan topo None
                                    return! writePlan sink catalog plan
                                | EmissionMode.Incremental ->
                                    return! writePlan sink catalog plan
                        }
                    return
                        Result.success
                            { Mode                = mode
                              Kinds               = reportKinds mode plan
                              UnbreakableCycleFks = plan.UnbreakableCycleFks
                              UnmatchedIdentities = []
                              AmbiguousIdentities = []
                              SkippedReferences   = plan.SkippedReferences @ writeSkips
                              CaptureLaneDescents = laneDescents }
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
    let runWithRenames
        (mode: Mode)
        (allowCdc: bool)
        (source: SqlConnection)
        (sink: SqlConnection)
        (sourceContract: Catalog)
        (sinkContract: Catalog)
        : Task<Result<TransferReport>> =
        task {
            match CatalogDiff.between sourceContract sinkContract with
            | Error e ->
                return Result.failureOf (ValidationError.create "transfer.renameDiffFailed" (sprintf "%A" e))
            | Ok diff ->
                let renameMap =
                    RenameProjection.renames diff |> RenameProjection.renameMap
                return! runCore mode allowCdc false source sink sinkContract Map.empty (Some (sourceContract, renameMap)) WriteOptions.def
        }

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
        : Task<Result<Map<SsKey, StreamedKind> * (SsKey * UnresolvedReference) list * LaneDescent list>> =
        let assignedBySinkKinds =
            plan.Loads
            |> List.choose (fun l -> if l.Disposition = IdentityDisposition.AssignedBySink then Some l.Kind else None)
            |> Set.ofList
        let fkTargetKinds =
            Catalog.allKinds catalog
            |> List.collect (fun k -> k.References |> List.map (fun r -> r.TargetKind))
            |> Set.ofList
        let remap = PackedSurrogateRemap.create ()
        let journalIndex = journal |> Option.map CaptureJournal.load

        // Q3: the re-point at the quantum grain — FK ordinals resolved
        // against the stream's renamed basis once per call (cheap; the
        // chunk behind it is 50k rows), cells re-pointed copy-on-write.
        let repoint (basis: RowBasis) (excluding: Set<Name>) (kind: Kind) (rows: RowQuantum list) : RemappedQuanta =
            let fkTargets =
                SurrogateRemap.fkColumnsTargeting assignedBySinkKinds kind
                |> Map.filter (fun col _ -> not (Set.contains col excluding))
            if Map.isEmpty fkTargets then { Rows = rows; Skipped = [] }
            else
                SurrogateRemap.remapQuantumFksWith
                    (PackedSurrogateRemap.tryFind remap)
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
                        match index.TryGetValue((SsKey.rootOriginal load.Kind, chunkIx)) with
                        | true, record -> Some record
                        | false, _ -> None)
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
                                            (quantumCellRowsExcludingIdentity basis kind load.DeferredFkColumns remapped.Rows)
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
                                    do! Bulk.copyRows sink kind.Physical (quantumCellRows basis kind load.DeferredFkColumns remapped.Rows)
                                    return [], lane, []
                            | _ ->
                                do! Bulk.copyRows sink kind.Physical (quantumCellRows basis kind load.DeferredFkColumns remapped.Rows)
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
                    return Result.success ({ Ingested = ingested; Written = written }, skips, descents)
                else
                    let nextPending = Projection.Adapters.Sql.AsyncStream.nextBatch CaptureChunkSize stream
                    match! writeChunk kind load basis pkOf lane chunkIx chunkRaw with
                    | Error es -> return Result.failure es
                    | Ok (written', newDescents, newSkips, lane') ->
                        return! loadKindChunks kind load basis pkOf stream nextPending (chunkIx + 1) lane'
                                    (ingested + List.length chunkRaw) (written + written')
                                    (skips @ newSkips) (descents @ newDescents)
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
                if List.isEmpty chunkRaw then return skips
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
                    return! phase2Chunks kind load basis idAttr renderUpdate stream (skips @ newSkips)
            }

        let rec phase2 (loads: DataLoadKind list) (skips: (SsKey * UnresolvedReference) list)
            : Task<(SsKey * UnresolvedReference) list> =
            task {
                match loads with
                | [] -> return skips
                | load :: rest ->
                    if Set.isEmpty load.DeferredFkColumns then return! phase2 rest skips
                    else
                        match Catalog.tryFindKind load.Kind catalog, Catalog.tryFindKind load.Kind ingestCatalog with
                        | Some kind, Some ingestKind ->
                            let idAttr = kind.Attributes |> List.tryFind (fun a -> a.IsPrimaryKey && a.IsIdentity)
                            let basis = RowBasis.rename renameMap (Kind.rowBasis ingestKind)
                            let renderUpdate = phase2UpdateSqlQuantum basis kind load.DeferredFkColumns
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

    /// **The streaming realization** — bounded memory for the estate-scale
    /// straight load (non-reconciling; Incremental semantics — the wipe and
    /// reconcile envelopes stay on the materialized path). The optional
    /// journal directory makes the run CHUNK-RESUMABLE: a journaled chunk
    /// whose source fingerprint matches is skipped and its captured pairs
    /// rebuild the remap; a fingerprint mismatch refuses by name
    /// (`transfer.resume.sourceDrift`); a COMPLETED run re-runs as a full
    /// skip — the streaming path's idempotent re-run (closing the G3
    /// duplicate hazard whenever a journal is supplied). The resumed run's
    /// report counts the resumed run's work; the journaled run reported its
    /// own drops (exit-9 semantics are per-run). `DryRun` reports the plan
    /// structure with zero counts — nothing is ingested.
    let runStreamingWithRenames
        (mode: Mode)
        (allowCdc: bool)
        (source: SqlConnection)
        (sink: SqlConnection)
        (sourceContract: Catalog)
        (sinkContract: Catalog)
        (journalDirectory: string option)
        : Task<Result<TransferReport>> =
        task {
            match CatalogDiff.between sourceContract sinkContract with
            | Error e ->
                return Result.failureOf (ValidationError.create "transfer.renameDiffFailed" (sprintf "%A" e))
            | Ok diff ->
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
                    // Structure only — order, dispositions, deferred columns;
                    // the rows STREAM at write time (the whole point).
                    let plan = DataLoadPlan.build sinkContract topo Map.empty SurrogateRemapContext.empty
                    let preWrite = if mode = Execute then executeGate sinkContract plan else None
                    match preWrite with
                    | Some refusal -> return Result.failureOf refusal
                    | None ->
                        if mode <> Execute then
                            return
                                Result.success
                                    { Mode                = mode
                                      Kinds               = reportKinds mode plan
                                      UnbreakableCycleFks = plan.UnbreakableCycleFks
                                      UnmatchedIdentities = []
                                      AmbiguousIdentities = []
                                      SkippedReferences   = plan.SkippedReferences
                                      CaptureLaneDescents = [] }
                        else
                            let journal =
                                journalDirectory
                                |> Option.map (fun dir -> CaptureJournal.create dir (planMarker sinkContract plan))
                            match! writePlanStreaming source sink sourceContract renameMap sinkContract plan journal with
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
                                          UnmatchedIdentities = []
                                          AmbiguousIdentities = []
                                          SkippedReferences   = skips
                                          CaptureLaneDescents = descents }
        }

    /// The streaming reverse leg through the `TransferConnections`
    /// apparatus (D9) — the bounded-memory sibling of
    /// `runReverseLegThroughConnections` for the estate-scale B→A load.
    /// Both contracts arrive RENDERED from the one authored model
    /// (`CatalogRendition`); the optional journal directory makes the run
    /// chunk-resumable. Straight load, Incremental semantics — the
    /// reconcile / WipeAndLoad / table-subset combinations stay on the
    /// materialized path (the CLI face refuses them by name).
    let runStreamingReverseLegThroughConnections
        (mode: Mode)
        (allowCdc: bool)
        (journalDirectory: string option)
        (connections: TransferConnections)
        (logicalSourceContract: Catalog)
        (physicalSinkContract: Catalog)
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
                    return! runStreamingWithRenames mode allowCdc source sink logicalSourceContract physicalSinkContract journalDirectory
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
    let runReconcilingWithRenames
        (mode: Mode)
        (allowCdc: bool)
        (source: SqlConnection)
        (sink: SqlConnection)
        (sourceContract: Catalog)
        (sinkContract: Catalog)
        (reconciliation: Map<SsKey, ReconciliationStrategy>)
        : Task<Result<TransferReport>> =
        task {
            match CatalogDiff.between sourceContract sinkContract with
            | Error e ->
                return Result.failureOf (ValidationError.create "transfer.renameDiffFailed" (sprintf "%A" e))
            | Ok diff ->
                let renameMap =
                    RenameProjection.renames diff |> RenameProjection.renameMap
                return! runCore mode allowCdc false source sink sinkContract reconciliation (Some (sourceContract, renameMap)) WriteOptions.def
        }

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
                    match! ReadSide.read source with
                    | Error es -> return Result.failure es
                    | Ok contract ->
                        match resolveLoadSet contract tables with
                        | Error es -> return Result.failure es
                        | Ok loadSet ->
                            match resolveReconciliation contract with
                            | Error es -> return Result.failure es
                            | Ok reconciliation ->
                                return! runCore mode allowCdc allowDrops source sink contract reconciliation None { WriteOptions.ofEmission emission with Resumable = resumable; LoadSet = loadSet }
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
        runThroughConnectionsResumable mode emission false allowCdc allowDrops tables connections resolveReconciliation

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
    /// the same emission/resumable envelope the peer transfer honors. Straight
    /// load — the reconcile + rename combination is the named follow-on.
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
        : Task<Result<TransferReport>> =
        task {
            match CatalogDiff.between logicalSourceContract physicalSinkContract with
            | Error e ->
                return Result.failureOf (ValidationError.create "transfer.renameDiffFailed" (sprintf "%A" e))
            | Ok diff ->
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
                            return! runCore mode allowCdc allowDrops source sink physicalSinkContract Map.empty (Some (logicalSourceContract, renameMap)) { WriteOptions.ofEmission emission with Resumable = resumable; LoadSet = loadSet }
        }

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

    /// Whether a completed run lost any rows (the drop-set is non-empty).
    let hasDrops (report: TransferReport) : bool =
        not (List.isEmpty report.SkippedReferences)
        || not (List.isEmpty report.UnmatchedIdentities)
        || not (List.isEmpty report.AmbiguousIdentities)   // NM-51

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
