namespace Projection.Pipeline

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
                { Column = a.Column.ColumnName; Type = a.Type; Raw = raw }))

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
            let clause (a: Attribute) = sprintf "%s = %s" (Render.quote a.Column.ColumnName) (lit a)
            Some (
                sprintf "UPDATE %s SET %s WHERE %s;"
                    (Render.tableQualified kind.Physical)
                    (deferredAttrs |> List.map clause |> String.concat ", ")
                    (pkAttrs |> List.map clause |> String.concat " AND "))

    /// Realize the plan onto an open Sink connection. Phase 1: every
    /// non-reconciled load (`Rows` non-empty) in topological order,
    /// bulk-insert with deferred FKs NULLed. Phase 2: the cycle-broken
    /// loads, in topological order, UPDATE the deferred FKs to their
    /// (plan-side, already-remapped) values. Loads whose disposition is
    /// `ReconciledByRule` carry empty `Rows` by plan-build; they are
    /// naturally skipped.
    let private writePlan (sink: SqlConnection) (catalog: Catalog) (plan: DataLoadPlan) : Task<unit> =
        task {
            for load in plan.Loads do
                if not (List.isEmpty load.Rows) then
                    match Catalog.tryFindKind load.Kind catalog with
                    | None      -> ()
                    | Some kind ->
                        do! Bulk.copyRows sink kind.Physical (toCellRows kind load.DeferredFkColumns load.Rows)

            for load in plan.Loads do
                if not (Set.isEmpty load.DeferredFkColumns) && not (List.isEmpty load.Rows) then
                    match Catalog.tryFindKind load.Kind catalog with
                    | None      -> ()
                    | Some kind ->
                        let updates = load.Rows |> List.choose (phase2UpdateSql kind load.DeferredFkColumns)
                        if not (List.isEmpty updates) then
                            do! Deploy.executeBatch sink (String.concat "\n" updates)
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

    let private runCore
        (mode: Mode)
        (allowCdc: bool)
        (source: SqlConnection)
        (sink: SqlConnection)
        (catalog: Catalog)
        (reconciliation: Map<SsKey, ReconciliationStrategy>)
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
            let topoLineage : Lineage<TopologicalOrder> = TopologicalOrderPass.runWith TreatAsCycle catalog
            let topo = topoLineage.Value
            let! rows = Ingestion.collectInOrder source catalog topo
            let! reconciled = reconcileAgainstSink sink catalog reconciliation rows
            let reconciledKinds = reconciliation |> Map.toSeq |> Seq.map fst |> Set.ofSeq
            // The plan-build is the ONE OperatorIntent Insertion site —
            // substitution is applied here, once. After this, every Row
            // is in target identity space.
            let plan =
                DataLoadPlan.build catalog topo rows reconciled.Remap
                |> DataLoadPlan.reclassifyReconciled reconciledKinds
            if mode = Execute && not (DataLoadPlan.isSatisfiable plan) then
                return
                    Result.failureOf
                        (ValidationError.create
                            "transfer.unbreakableCycleFk"
                            (sprintf
                                "%d non-deferrable cycle FK(s) — cannot execute a clean two-phase load"
                                plan.UnbreakableCycleFks.Length))
            else
                if mode = Execute then do! writePlan sink catalog plan
                return
                    Result.success
                        { Mode                = mode
                          Kinds               = reportKinds mode plan
                          UnbreakableCycleFks = plan.UnbreakableCycleFks
                          UnmatchedIdentities = reconciled.Unmatched
                          SkippedReferences   = plan.SkippedReferences }
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
        runCore mode allowCdc source sink catalog Map.empty

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
        (source: SqlConnection)
        (sink: SqlConnection)
        (catalog: Catalog)
        (reconciliation: Map<SsKey, ReconciliationStrategy>)
        : Task<Result<TransferReport>> =
        runCore mode allowCdc source sink catalog reconciliation

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
                            return! runCore mode allowCdc source sink contract reconciliation
        }

    /// Registry metadata (pillar 9). The Transfer realization classifies
    /// entirely as `DataIntent`: the operator's identity substitution
    /// landed at `DataLoadPlan.build` (the canonical `OperatorIntent
    /// Insertion` site); this orchestrator just realizes the plan onto
    /// the Sink. How a plan deploys is realization-layer policy (A36).
    /// `AssignedBySink` (Slice E) will add an OUTPUT-capture site.
    let registeredMetadata : RegisteredTransformMetadata =
        RegisteredTransformMetadata.emitter "transferProjection" Data
            [ TransformSite.dataIntent "phase1BulkInsert"
                "Phase 1: bulk-insert each plan load's rows (deferred FK columns NULLed). Rows are already post-substitution (`DataLoadPlan.build` is the OperatorIntent Insertion site). Realization of the plan (A36); DataIntent."
              TransformSite.dataIntent "phase2FkRepoint"
                "Phase 2: UPDATE the cycle-deferred FK columns to their plan-side values, keyed by PK, in topological order. Deterministic from the plan; no operator opinion." ]
