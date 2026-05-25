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
/// over one shared `Catalog` (the schema contract). Slice C of the
/// Transfer epic: the `PreservedFromSource` path (source surrogate keys
/// written directly via `SqlBulkCopy`'s `KeepIdentity`), two-phase
/// (phase 1 bulk-insert with cycle-deferred FK columns NULLed; phase 2
/// UPDATE those columns in topological order). `AssignedBySink` (sink
/// mints keys + capture/remap) is Slice E. See `PRESCOPE_TRANSFER.md`
/// §9 seams 3–4, §10.
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
            /// Source rows dropped because an FK targeted a reconciled
            /// identity that has no Sink home — paired with the owning
            /// kind. Empty for a non-reconciling Transfer.
            SkippedReferences   : (SsKey * UnresolvedReference) list
        }

    // -- Projection-onto-Sink realization -----------------------------------

    /// Project a kind's ingested rows into `SqlBulkCopy` cell rows.
    /// Deferred FK columns are emitted as the empty raw — `KeepNulls`
    /// maps that to SQL NULL — so phase 1 satisfies a cycle; phase 2
    /// re-points them. All other columns carry the source value (the
    /// PK included → `KeepIdentity` preserves it: `PreservedFromSource`).
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
    /// source values, keyed by the kind's primary key. `None` when the
    /// kind has no PK or no deferred columns. Terminal SQL boundary —
    /// values via `SqlLiteral`, identifiers via `Render.quote` (mirrors
    /// the forward `StaticSeedsEmitter.renderUpdate`).
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

    /// A non-reconciled kind's load with its FK values already re-pointed
    /// through the reconciliation remap (`Reconciliation.remapRowFks`) and
    /// its unresolvable rows dropped. Reconciled kinds are excluded — their
    /// rows already exist in the Sink, so their phase-1 insert is skipped.
    type private PreparedLoad =
        {
            Load     : TransferKindLoad
            Kind     : Kind
            Remapped : RemappedRows
        }

    /// Re-point every non-reconciled load's FK values that target a
    /// reconciled kind through the remap, dropping rows whose referenced
    /// identity has no Sink home (skip-and-diagnose). Pure; consumed by
    /// both `DryRun` (preview) and `Execute` (the write). When the
    /// reconciliation set is empty this is the identity over the loads.
    let private prepare
        (catalog: Catalog)
        (reconciledKinds: Set<SsKey>)
        (remap: SurrogateRemapContext)
        (plan: TransferPlan)
        : PreparedLoad list =
        plan.Loads
        |> List.choose (fun load ->
            if load.Disposition = IdentityDisposition.ReconciledByRule then None
            else
                Catalog.tryFindKind load.Kind catalog
                |> Option.map (fun kind ->
                    let fkTargets = Reconciliation.reconciledFkColumns reconciledKinds kind
                    { Load     = load
                      Kind     = kind
                      Remapped = Reconciliation.remapRowFks fkTargets remap load.Rows }))

    /// Realize the prepared loads onto an open Sink connection. Phase 1:
    /// every prepared kind in topological order, bulk-insert with deferred
    /// FKs NULLed. Phase 2: the cycle-broken kinds, in topological order,
    /// UPDATE the deferred FKs to their (already remapped) source values.
    let private writePrepared (sink: SqlConnection) (prepared: PreparedLoad list) : Task<unit> =
        task {
            for p in prepared do
                if not (List.isEmpty p.Remapped.Rows) then
                    do! Bulk.copyRows sink p.Kind.Physical (toCellRows p.Kind p.Load.DeferredFkColumns p.Remapped.Rows)

            for p in prepared do
                if not (Set.isEmpty p.Load.DeferredFkColumns) then
                    let updates = p.Remapped.Rows |> List.choose (phase2UpdateSql p.Kind p.Load.DeferredFkColumns)
                    if not (List.isEmpty updates) then
                        do! Deploy.executeBatch sink (String.concat "\n" updates)
        }

    // -- reconciliation orchestration ---------------------------------------

    /// Reconcile each operator-chosen kind's Source surrogates to the
    /// pre-existing Sink identities. Reads the Sink rows for each reconciled
    /// kind (the Sink is not write-only) and folds the per-kind results into
    /// one remap + the combined unmatched list. A read-only step — safe in
    /// `DryRun`. Re-captures through `SurrogateRemapContext.capture` so the
    /// merged context carries the construction-time invariant.
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

    let private reportKinds (mode: Mode) (plan: TransferPlan) (prepared: PreparedLoad list) : KindOutcome list =
        let writtenByKind =
            match mode with
            | DryRun  -> Map.empty
            | Execute -> prepared |> List.map (fun p -> p.Load.Kind, p.Remapped.Rows.Length) |> Map.ofList
        plan.Loads
        |> List.map (fun l ->
            { Kind              = l.Kind
              Disposition       = l.Disposition
              RowsIngested      = l.Rows.Length
              DeferredFkColumns = l.DeferredFkColumns
              RowsWritten       = Map.tryFind l.Kind writtenByKind |> Option.defaultValue 0 })

    let private runCore
        (mode: Mode)
        (source: SqlConnection)
        (sink: SqlConnection)
        (catalog: Catalog)
        (reconciliation: Map<SsKey, ReconciliationStrategy>)
        : Task<Result<TransferReport>> =
        task {
            let topoLineage : Lineage<TopologicalOrder> = TopologicalOrderPass.runWith TreatAsCycle catalog
            let topo = topoLineage.Value
            let! rows = Ingestion.collectInOrder source catalog topo
            let! reconciled = reconcileAgainstSink sink catalog reconciliation rows
            let reconciledKinds = reconciliation |> Map.toSeq |> Seq.map fst |> Set.ofSeq
            let plan =
                TransferPlan.build catalog topo rows
                |> TransferPlan.reclassifyReconciled reconciledKinds
            if mode = Execute && not (TransferPlan.isSatisfiable plan) then
                return
                    Result.failureOf
                        (ValidationError.create
                            "transfer.unbreakableCycleFk"
                            (sprintf
                                "%d non-deferrable cycle FK(s) — cannot execute a clean two-phase load"
                                plan.UnbreakableCycleFks.Length))
            else
                let prepared = prepare catalog reconciledKinds reconciled.Remap plan
                if mode = Execute then do! writePrepared sink prepared
                return
                    Result.success
                        { Mode                = mode
                          Kinds               = reportKinds mode plan prepared
                          UnbreakableCycleFks = plan.UnbreakableCycleFks
                          UnmatchedIdentities = reconciled.Unmatched
                          SkippedReferences   =
                            prepared
                            |> List.collect (fun p ->
                                p.Remapped.Skipped |> List.map (fun s -> p.Load.Kind, s)) }
        }

    /// Run a Transfer over one shared `Catalog` (the schema contract):
    /// ingest rows from the Source, build the identity-aware two-phase
    /// plan, and — when `Execute` — project them onto the Sink. `DryRun`
    /// reports the plan without writing. `Execute` against an unsatisfiable
    /// plan (a non-deferrable cycle FK) fails loudly rather than attempting
    /// a doomed load. Both connections are caller-supplied and open.
    let run
        (mode: Mode)
        (source: SqlConnection)
        (sink: SqlConnection)
        (catalog: Catalog)
        : Task<Result<TransferReport>> =
        runCore mode source sink catalog Map.empty

    /// Run a *reconciling* Transfer — the operator's headline case
    /// (Dev→UAT User re-key). `reconciliation` names, per kind, how its
    /// Source surrogates reconcile to the *pre-existing* Sink identities
    /// (`ReconciledByRule`): those kinds skip their phase-1 insert, and
    /// every FK pointing at them is re-pointed through the matched remap.
    /// References to identities with no Sink home are dropped and reported
    /// in `SkippedReferences`.
    let runReconciling
        (mode: Mode)
        (source: SqlConnection)
        (sink: SqlConnection)
        (catalog: Catalog)
        (reconciliation: Map<SsKey, ReconciliationStrategy>)
        : Task<Result<TransferReport>> =
        runCore mode source sink catalog reconciliation

    /// Registry metadata (pillar 9). The Projection-onto-Sink realization
    /// classifies entirely as `DataIntent`: how a plan deploys is
    /// realization-layer policy (A36), and `PreservedFromSource` writes the
    /// Source keys directly. The `Execute` gate is an R6 safety control, not
    /// a transformation axis. `AssignedBySink` (Slice E) will add an
    /// OUTPUT-capture site.
    let registeredMetadata : RegisteredTransformMetadata =
        RegisteredTransformMetadata.emitter "transferProjection" Data
            [ TransformSite.dataIntent "phase1BulkInsert"
                "Phase 1: bulk-insert each kind's rows (deferred FK columns NULLed), preserving the Source surrogate keys via SqlBulkCopy KeepIdentity. Realization of the plan (A36); DataIntent."
              TransformSite.dataIntent "phase2FkRepoint"
                "Phase 2: UPDATE the cycle-deferred FK columns to their Source values, keyed by PK, in topological order. Deterministic from the plan; no operator opinion." ]
