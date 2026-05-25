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

    /// Realize the plan onto an open Sink connection. Phase 1: every kind
    /// in topological order, bulk-insert with deferred FKs NULLed. Phase 2:
    /// the cycle-broken kinds, in topological order, UPDATE the deferred
    /// FKs to their source values.
    let projectOntoSink (sink: SqlConnection) (catalog: Catalog) (plan: TransferPlan) : Task<unit> =
        task {
            for load in plan.Loads do
                match Catalog.tryFindKind load.Kind catalog with
                | Some kind when not (List.isEmpty load.Rows) ->
                    do! Bulk.copyRows sink kind.Physical (toCellRows kind load.DeferredFkColumns load.Rows)
                | _ -> ()

            for load in plan.Loads do
                if not (Set.isEmpty load.DeferredFkColumns) then
                    match Catalog.tryFindKind load.Kind catalog with
                    | Some kind ->
                        let updates = load.Rows |> List.choose (phase2UpdateSql kind load.DeferredFkColumns)
                        if not (List.isEmpty updates) then
                            do! Deploy.executeBatch sink (String.concat "\n" updates)
                    | None -> ()
        }

    // -- orchestration ------------------------------------------------------

    let private reportKinds (mode: Mode) (plan: TransferPlan) : KindOutcome list =
        plan.Loads
        |> List.map (fun l ->
            { Kind              = l.Kind
              Disposition       = l.Disposition
              RowsIngested      = l.Rows.Length
              DeferredFkColumns = l.DeferredFkColumns
              RowsWritten       = (match mode with Execute -> l.Rows.Length | DryRun -> 0) })

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
        task {
            let topoLineage : Lineage<TopologicalOrder> = TopologicalOrderPass.runWith TreatAsCycle catalog
            let topo = topoLineage.Value
            let! rows = Ingestion.collectInOrder source catalog topo
            let plan = TransferPlan.build catalog topo rows
            if mode = Execute && not (TransferPlan.isSatisfiable plan) then
                return
                    Result.failureOf
                        (ValidationError.create
                            "transfer.unbreakableCycleFk"
                            (sprintf
                                "%d non-deferrable cycle FK(s) — cannot execute a clean two-phase load"
                                plan.UnbreakableCycleFks.Length))
            else
                if mode = Execute then do! projectOntoSink sink catalog plan
                return
                    Result.success
                        { Mode = mode
                          Kinds = reportKinds mode plan
                          UnbreakableCycleFks = plan.UnbreakableCycleFks }
        }

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
