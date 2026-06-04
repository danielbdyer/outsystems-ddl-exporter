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
            /// Source rows dropped at plan-build because a targeted FK
            /// had no matched assigned counterpart — paired with the
            /// owning kind. Empty for a non-reconciling Transfer.
            SkippedReferences   : (SsKey * UnresolvedReference) list
        }

    // -- Projection-onto-Sink realization -----------------------------------

    /// Project a kind's already-post-substitution rows into `SqlBulkCopy`
    /// cell rows. Deferred FK columns are emitted as the empty raw —
    /// `KeepNulls` maps that to SQL NULL — so Phase 1 satisfies a cycle;
    /// Phase 2 re-points them.
    let private toCellRows (kind: Kind) (deferred: Set<Name>) (rows: StaticRow list) : CellValue list list =
        rows
        |> List.map (fun row ->
            kind.Attributes
            |> List.map (fun a ->
                let raw =
                    if Set.contains a.Name deferred then ""
                    else Map.tryFind a.Name row.Values |> Option.defaultValue ""
                { Column = ColumnRealization.columnNameText a.Column; Type = a.Type; Raw = raw }))

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

    /// Per-row INSERT for an `AssignedBySink` kind: omit the IDENTITY PK
    /// column (let the Sink mint it) and capture the assigned surrogate via
    /// `OUTPUT inserted.<pk>`. Returns the assigned key as its raw string,
    /// or `None` if the insert produced no output row. `SqlBulkCopy` cannot
    /// return assigned identities, so this is the per-row path §5.2 requires;
    /// the value-literal rendering mirrors `phase2UpdateSql` (the existing
    /// transfer text boundary).
    let private insertCaptureRow
        (sink: SqlConnection)
        (kind: Kind)
        (identityColumn: string)
        (row: StaticRow)
        : Task<string option> =
        task {
            let insertCols = kind.Attributes |> List.filter (fun a -> not (a.IsPrimaryKey && a.IsIdentity))
            let lit (a: Attribute) =
                Map.tryFind a.Name row.Values
                |> Option.defaultValue ""
                |> SqlLiteral.ofRaw a.Type
                |> SqlLiteral.toString
            let sql =
                if List.isEmpty insertCols then
                    sprintf "INSERT INTO %s OUTPUT inserted.%s DEFAULT VALUES;"
                        (Render.tableQualified kind.Physical) (Render.quote identityColumn)
                else
                    sprintf "INSERT INTO %s (%s) OUTPUT inserted.%s VALUES (%s);"
                        (Render.tableQualified kind.Physical)
                        (insertCols |> List.map (fun a -> Render.quote (ColumnRealization.columnNameText a.Column)) |> String.concat ", ")
                        (Render.quote identityColumn)
                        (insertCols |> List.map lit |> String.concat ", ")
            use cmd = sink.CreateCommand()
            cmd.CommandText <- sql
            let! scalar = cmd.ExecuteScalarAsync()
            return
                if isNull scalar || scalar = box System.DBNull.Value then None
                else Some (string scalar)
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
    let private writePlan (sink: SqlConnection) (catalog: Catalog) (plan: DataLoadPlan) : Task<(SsKey * UnresolvedReference) list> =
        task {
            let assignedBySinkKinds =
                plan.Loads
                |> List.choose (fun l ->
                    if l.Disposition = IdentityDisposition.AssignedBySink then Some l.Kind else None)
                |> Set.ofList
            // The Source→Sink-minted surrogate map, accumulated as
            // AssignedBySink kinds insert; threaded through the topological
            // Phase-1 loop so referencers re-point against captures made by
            // their (earlier-ordered) targets.
            let mutable remap = SurrogateRemapContext.empty
            let mutable writeSkips : (SsKey * UnresolvedReference) list = []

            let repoint (kind: Kind) (rows: StaticRow list) : RemappedRows =
                let fkTargets = SurrogateRemap.fkColumnsTargeting assignedBySinkKinds kind
                if Map.isEmpty fkTargets then { Rows = rows; Skipped = [] }
                else SurrogateRemap.remapRowFks fkTargets remap rows

            for load in plan.Loads do
                if not (List.isEmpty load.Rows) then
                    match Catalog.tryFindKind load.Kind catalog with
                    | None      -> ()
                    | Some kind ->
                        let remapped = repoint kind load.Rows
                        writeSkips <- writeSkips @ (remapped.Skipped |> List.map (fun u -> load.Kind, u))
                        match load.Disposition with
                        | IdentityDisposition.AssignedBySink ->
                            match kind.Attributes |> List.tryFind (fun a -> a.IsPrimaryKey && a.IsIdentity) with
                            | Some idAttr ->
                                for row in remapped.Rows do
                                    let! assigned = insertCaptureRow sink kind (ColumnRealization.columnNameText idAttr.Column) row
                                    match Map.tryFind idAttr.Name row.Values, assigned with
                                    | Some srcVal, Some assignedVal when srcVal <> "" ->
                                        match SurrogateRemapContext.capture load.Kind (SourceKey.ofString srcVal) (AssignedKey.ofString assignedVal) remap with
                                        | Ok r    -> remap <- r
                                        | Error _ -> ()
                                    | _ -> ()
                            | None ->
                                // ofKind only returns AssignedBySink for an IDENTITY PK, so this is
                                // unreachable; fall back to the bulk path rather than drop the rows.
                                do! Bulk.copyRows sink kind.Physical (toCellRows kind load.DeferredFkColumns remapped.Rows)
                        | _ ->
                            do! Bulk.copyRows sink kind.Physical (toCellRows kind load.DeferredFkColumns remapped.Rows)

            for load in plan.Loads do
                if not (Set.isEmpty load.DeferredFkColumns) && not (List.isEmpty load.Rows) then
                    match Catalog.tryFindKind load.Kind catalog with
                    | None      -> ()
                    | Some kind ->
                        let rows2 = (repoint kind load.Rows).Rows
                        let updates = rows2 |> List.choose (phase2UpdateSql kind load.DeferredFkColumns)
                        if not (List.isEmpty updates) then
                            do! Deploy.executeBatch sink (String.concat "\n" updates)

            return writeSkips
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
    let private wipeFkOrdered (sink: SqlConnection) (catalog: Catalog) (plan: DataLoadPlan) (topo: TopologicalOrder) : Task<unit> =
        task {
            let loaded = plan.Loads |> List.map (fun l -> l.Kind) |> Set.ofList
            let childFirst = List.rev topo.Order |> List.filter (fun k -> Set.contains k loaded)
            for k in childFirst do
                match Catalog.tryFindKind k catalog with
                | None      -> ()
                | Some kind ->
                    do! Deploy.executeBatch sink
                            (System.String.Concat("DELETE FROM ", Render.tableQualified kind.Physical, ";"))  // LINT-ALLOW: terminal SQL-text boundary; table name is a validated TableId via Render.tableQualified
        }

    /// The durable phase-marker table — records which transfers completed, so a
    /// re-run of an already-finished transfer is a no-op (idempotent).
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
    let private writePlanResumable (sink: SqlConnection) (catalog: Catalog) (plan: DataLoadPlan) (topo: TopologicalOrder) : Task<(SsKey * UnresolvedReference) list> =
        task {
            do! Deploy.executeBatch sink progressTableSql
            let marker = planMarker catalog plan
            let! already = isMarked sink marker
            if already then return []
            else
                do! wipeFkOrdered sink catalog plan topo
                let! skips = writePlan sink catalog plan
                do! markComplete sink marker
                return skips
        }

    // -- 6.A.2 / 6.A.3: surrogate-capture refusals (fail-loud, not silent) ---
    //
    // Two `AssignedBySink` shapes the per-row capture path cannot honor. Each
    // is detected from the built plan + the schema contract and refused at
    // Execute time rather than silently mis-keyed — *total decisions, named
    // skips*. Both are pure decisions (no connection) so the data canary and
    // the fast-pool unit test witness the SAME refusal (the 6.A.1 pattern).

    /// 6.A.2 — `AssignedBySink` kinds that carry cycle-deferred FK columns.
    /// `phase2UpdateSql` keys the Phase-2 re-point on the *source* PK value;
    /// for an `AssignedBySink` kind the Sink minted a fresh surrogate, so the
    /// source PK no longer exists and the UPDATE matches zero rows — the
    /// deferred FK is left silently wrong. Refuse rather than emit a no-op
    /// UPDATE. (The correct fix — re-point Phase-2 via the captured remap AND
    /// key the WHERE on the assigned PK — is the named follow-on.)
    let cyclicAssignedBySinkKinds (plan: DataLoadPlan) : SsKey list =
        plan.Loads
        |> List.choose (fun l ->
            if l.Disposition = IdentityDisposition.AssignedBySink
               && not (Set.isEmpty l.DeferredFkColumns)
            then Some l.Kind
            else None)

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
    /// check together with the 6.A.2 / 6.A.3 surrogate-capture refusals so the
    /// orchestrator has one pre-write gate and the order of precedence is
    /// explicit (structural unsatisfiability first, then the capture shapes).
    let executeGate (catalog: Catalog) (plan: DataLoadPlan) : ValidationError option =
        if not (DataLoadPlan.isSatisfiable plan) then
            Some (ValidationError.create
                    "transfer.unbreakableCycleFk"
                    (sprintf
                        "%d non-deferrable cycle FK(s) — cannot execute a clean two-phase load"
                        plan.UnbreakableCycleFks.Length))
        else
            match cyclicAssignedBySinkKinds plan with
            | k :: _ ->
                Some (ValidationError.create
                        "transfer.cyclicAssignedBySink"
                        (sprintf
                            "Kind %s is AssignedBySink with cycle-deferred FK column(s); the Phase-2 re-point keys on the source PK the Sink replaced, so it would match zero rows. Refusing rather than emit a no-op UPDATE."
                            (SsKey.rootOriginal k)))
            | [] ->
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
            for KeyValue (kind, strategy) in reconciliation do
                match Catalog.tryFindKind kind catalog with
                | None -> ()
                | Some k ->
                    match Kind.primaryKey k with
                    | pk :: _ ->
                        let srcRows = Map.tryFind kind sourceRows |> Option.defaultValue []
                        let! sinkRows = AsyncStream.toList (Ingestion.streamKind sink k)
                        let result = Reconciliation.reconcileKind kind pk.Name strategy srcRows sinkRows
                        for KeyValue (rk, inner) in result.Remap.Assignments do
                            for KeyValue (src, assigned) in inner do
                                match SurrogateRemapContext.capture rk src assigned remap with
                                | Ok r    -> remap <- r
                                | Error _ -> ()
                        unmatched <- unmatched @ result.Unmatched
                    | [] -> ()
            return { Remap = remap; Unmatched = unmatched }
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
        { Emission : EmissionMode; Resumable : bool }

    [<RequireQualifiedAccess>]
    module WriteOptions =
        let def : WriteOptions = { Emission = EmissionMode.Incremental; Resumable = false }
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
            let! cdcGate =
                task {
                    if mode = Execute && not allowCdc then
                        let! tracked = ReadSide.cdcTrackedTables sink
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
            match cdcGate with
            | Error e -> return Result.failure e
            | Ok () ->
            // G1 / G2 — both endpoints live + credentialed, and the sink grant
            // covers the planned INSERTs, BEFORE any write. Only an Execute run
            // mutates the sink; DryRun previews without writing, so it skips the
            // gate (no blind grant probe on a preview).
            let! spanningGate =
                task {
                    if mode = Execute then return! spanningPreflight source sink catalog
                    else return Ok ()
                }
            match spanningGate with
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
            let rows =
                match ingestion with
                | Some (_, renameMap) when not (Map.isEmpty renameMap) ->
                    rawRows |> Map.map (fun _ rs -> RenameProjection.repointRows renameMap rs)
                | _ -> rawRows
            let! reconciled = reconcileAgainstSink sink catalog reconciliation rows
            let reconciledKinds = reconciliation |> Map.toSeq |> Seq.map fst |> Set.ofSeq
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
                let! writeSkips =
                    task {
                        if mode <> Execute then return []
                        else
                            match writeOpts.Emission, writeOpts.Resumable with
                            | EmissionMode.WipeAndLoad, _ ->
                                // D10 — operator-selected full refresh: FK-ordered
                                // wipe of the plan's tables, then the standard load.
                                do! wipeFkOrdered sink catalog plan topo
                                return! writePlan sink catalog plan
                            | EmissionMode.Incremental, true ->
                                // G10 — resumable/idempotent envelope.
                                return! writePlanResumable sink catalog plan topo
                            | EmissionMode.Incremental, false ->
                                return! writePlan sink catalog plan
                    }
                return
                    Result.success
                        { Mode                = mode
                          Kinds               = reportKinds mode plan
                          UnbreakableCycleFks = plan.UnbreakableCycleFks
                          UnmatchedIdentities = reconciled.Unmatched
                          // Plan-build drops (reconcile misses) + write-time
                          // drops (AssignedBySink FK misses) both surface here.
                          SkippedReferences   = plan.SkippedReferences @ writeSkips }
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
    let runWithEmissionMode
        (mode: Mode)
        (emission: EmissionMode)
        (allowCdc: bool)
        (source: SqlConnection)
        (sink: SqlConnection)
        (catalog: Catalog)
        : Task<Result<TransferReport>> =
        runCore mode allowCdc false source sink catalog Map.empty None (WriteOptions.ofEmission emission)

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
    let runThroughConnections
        (mode: Mode)
        (allowCdc: bool)
        (allowDrops: bool)
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
                        match resolveReconciliation contract with
                        | Error es -> return Result.failure es
                        | Ok reconciliation ->
                            return! runCore mode allowCdc allowDrops source sink contract reconciliation None WriteOptions.def
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
        report.SkippedReferences.Length + report.UnmatchedIdentities.Length

    /// Whether a completed run lost any rows (the drop-set is non-empty).
    let hasDrops (report: TransferReport) : bool =
        not (List.isEmpty report.SkippedReferences)
        || not (List.isEmpty report.UnmatchedIdentities)

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
                "§5.2 Slice E: for `AssignedBySink` kinds (IDENTITY PK), insert per-row with `OUTPUT inserted.<pk>` (omitting the identity column so the Sink mints the surrogate) and capture each Source→assigned surrogate into a `SurrogateRemapContext`; every later referencer's FK targeting the kind is re-pointed via `tryFindAssigned`, skip-and-diagnose on miss. Unlike `DataLoadPlan.build`'s substitution (operator-supplied remap, known pre-build), this remap is discovered *during* the write — the assigned identity does not exist until the Sink mints it — so the site is OperatorIntent Insertion at the realization layer." ]
