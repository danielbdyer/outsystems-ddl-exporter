namespace Projection.Core

/// One kind's contribution to a Transfer load, in topological position:
/// the rows ingested from the Source, the identity disposition (who owns
/// the surrogate key — `IdentityDisposition.ofKind`), and the FK columns
/// that must be deferred to phase 2 of the two-phase nulls-then-FKs load.
type TransferKindLoad =
    {
        Kind              : SsKey
        Disposition       : IdentityDisposition
        DeferredFkColumns : Set<Name>
        Rows              : StaticRow list
    }

/// A non-nullable FK whose target is in the same dependency cycle: it
/// cannot be deferred (phase 1 cannot NULL it), so a clean two-phase load
/// cannot satisfy it. Surfaced as an operator-facing diagnostic rather
/// than silently producing an unsatisfiable plan (total decisions, named
/// skips).
type UnbreakableCycleFk =
    {
        Kind   : SsKey
        Column : Name
        Target : SsKey
    }

/// The pure, identity-aware two-phase load plan for a Transfer: per kind,
/// in FK-safe (topological) order, the rows to load, the identity
/// disposition, and the deferred FK columns. Direction-neutral — the
/// `Ingestion` adapter supplies the rows; a Projection realization
/// consumes the plan (renders SQL / executes / bulk-copies). This is the
/// data-level analog of the schema-level plan the forward emitters build;
/// it adds the identity dimension the forward `DataInsertScript` lacks.
/// Slice B of the Transfer epic — see `PRESCOPE_TRANSFER.md` §9 seam 1, §10.
type TransferPlan =
    {
        /// Per-kind loads in FK-safe topological order.
        Loads               : TransferKindLoad list
        /// Non-nullable cycle FKs that block a clean two-phase load.
        UnbreakableCycleFks : UnbreakableCycleFk list
    }

[<RequireQualifiedAccess>]
module TransferPlan =

    /// Build the plan from the schema contract (the in-memory `Catalog`),
    /// a precomputed `TopologicalOrder`, and the ingested rows per kind.
    /// Pure and DB-free: classify each kind's `IdentityDisposition`, order
    /// by `topo.Order`, select deferred FK columns via
    /// `TopologicalOrder.deferredFkColumns`, attach the ingested rows
    /// (empty when a kind was not ingested), and surface non-deferrable
    /// cycle FKs as diagnostics.
    let build
        (catalog: Catalog)
        (topo: TopologicalOrder)
        (rows: Map<SsKey, StaticRow list>)
        : TransferPlan =
        let members = TopologicalOrder.cycleMembers topo

        let loads =
            topo.Order
            |> List.choose (fun key ->
                Catalog.tryFindKind key catalog
                |> Option.map (fun k ->
                    { Kind              = key
                      Disposition       = IdentityDisposition.ofKind k
                      DeferredFkColumns = TopologicalOrder.deferredFkColumns members k
                      Rows              = Map.tryFind key rows |> Option.defaultValue [] }))

        let unbreakable =
            topo.Order
            |> List.collect (fun key ->
                match Catalog.tryFindKind key catalog with
                | None -> []
                | Some k when not (Set.contains key members) -> []
                | Some k ->
                    k.References
                    |> List.choose (fun r ->
                        if Set.contains r.TargetKind members then
                            Kind.tryFindAttribute r.SourceAttribute k
                            |> Option.bind (fun a ->
                                if a.Column.IsNullable then None
                                else Some { Kind = key; Column = a.Name; Target = r.TargetKind })
                        else None))

        { Loads = loads; UnbreakableCycleFks = unbreakable }

    /// The kinds whose phase-1 insert must NULL one or more FK columns and
    /// be re-pointed in phase 2 (the cycle-broken kinds). Convenience for
    /// a realization that wants the phase-2 work list.
    let deferredLoads (plan: TransferPlan) : TransferKindLoad list =
        plan.Loads |> List.filter (fun l -> not (Set.isEmpty l.DeferredFkColumns))

    /// True iff every cycle FK is deferrable — i.e. the plan is a clean
    /// two-phase load with no unsatisfiable FK.
    let isSatisfiable (plan: TransferPlan) : bool =
        List.isEmpty plan.UnbreakableCycleFks

    /// Registry metadata (pillar 9). The pure plan builder classifies
    /// entirely as `DataIntent`: disposition is derived from `IsIdentity`,
    /// deferral from the cycle graph, ordering from the supplied topology —
    /// no operator opinion. The per-kind `--disposition` override (Slice D)
    /// and the `ReconciledByRule` ruleset (Slice C′) land as separate
    /// `OperatorIntent` sites when they ship.
    let registeredMetadata : RegisteredTransformMetadata =
        { Name         = "transferPlan"
          Domain       = Data
          StageBinding = Pipeline
          Sites =
            [ TransformSite.dataIntent "identityDisposition"
                "Classify each kind's IdentityDisposition (AssignedBySink vs PreservedFromSource) via ofKind, derived from the PK's IsIdentity flag. No operator opinion enters."
              TransformSite.dataIntent "deferredFkSelection"
                "Select cycle-deferred FK columns per kind via TopologicalOrder.deferredFkColumns (in-cycle + same-cycle target + nullable). Structural cycle-break; reachable without operator opinion."
              TransformSite.dataIntent "rowAttachment"
                "Attach the ingested rows to each kind in topological order. Pure data carriage; no operator filtering or insertion." ]
          Status = Active }
