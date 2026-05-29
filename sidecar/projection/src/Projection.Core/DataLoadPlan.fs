namespace Projection.Core

/// One kind's contribution to a data load, in topological position:
/// the rows already-in-target-identity-space (the operator-supplied
/// `SurrogateRemapContext` has been applied at plan-build), the
/// `IdentityDisposition` that selects realization semantics
/// (`PreservedFromSource` writes the row, `AssignedBySink` lets the
/// target mint the surrogate, `ReconciledByRule` skips the write
/// entirely), and the FK columns that must be deferred to Phase 2 of
/// the two-phase nulls-then-FKs load.
type DataLoadKind =
    {
        Kind              : SsKey
        Disposition       : IdentityDisposition
        DeferredFkColumns : Set<Name>
        Rows              : StaticRow list
    }

/// A non-nullable FK whose target is in the same dependency cycle: it
/// cannot be deferred (Phase 1 cannot NULL it), so a clean two-phase
/// load cannot satisfy it. Surfaced as an operator-facing diagnostic
/// rather than silently producing an unsatisfiable plan (total
/// decisions, named skips).
type UnbreakableCycleFk =
    {
        Kind   : SsKey
        Column : Name
        Target : SsKey
    }

/// The pure, identity-aware two-phase load plan that every data-load
/// realization consumes. Direction-neutral by construction (A35/A36):
/// the plan is a deterministic stream of per-kind `DataLoadKind` values;
/// realizations choose how to deploy (SQL-text emission, `SqlBulkCopy`
/// execution, in-memory snapshot, …) — bulk-vs-incremental is
/// realization-layer policy.
///
/// **The fundamental algebraic relationship** (chapter-A.0' axiom-
/// candidate; codified 2026-05-29): a data load is `(Plan, Realization)`
/// where the Plan carries POST-substitution rows and the Realization is
/// a direction-specific functor `Plan → Effect`. Identity substitution
/// — applying an operator-supplied `SurrogateRemapContext` to FK values
/// — belongs to **plan-construction**, not realization. Realizations
/// (the four current consumers: `StaticSeedsEmitter`,
/// `MigrationDependenciesEmitter`, `BootstrapEmitter`, `Transfer
/// .runReconciling`) read `plan.Loads[i].Rows` as already-in-target;
/// they do not see the remap. The one `OperatorIntent Insertion` site
/// in the entire data-load family lives at `DataLoadPlan.build`.
type DataLoadPlan =
    {
        /// Per-kind loads in FK-safe topological order.
        Loads               : DataLoadKind list
        /// Non-nullable cycle FKs that block a clean two-phase load.
        UnbreakableCycleFks : UnbreakableCycleFk list
        /// Source rows dropped at plan-build because a targeted FK had
        /// no matched assigned counterpart in the supplied remap —
        /// paired with the owning kind. Empty when remap is empty or
        /// no rows referenced unmatched identities.
        SkippedReferences   : (SsKey * UnresolvedReference) list
    }

[<RequireQualifiedAccess>]
module DataLoadPlan =

    /// Build the plan from the schema contract (the in-memory `Catalog`),
    /// a precomputed `TopologicalOrder`, the raw per-kind row source
    /// (in *source* identity space), and an operator-supplied
    /// `SurrogateRemapContext`. The build APPLIES the substitution —
    /// every FK column whose target is in the remap is re-pointed from
    /// the Source surrogate to the assigned-side surrogate, and rows
    /// whose targeted FK has no matched assigned counterpart are
    /// dropped (skip-and-diagnose at the operator's supply discipline).
    /// This is **the** OperatorIntent Insertion site for the entire
    /// data-load family; realizations are DataIntent.
    ///
    /// `IdentityDisposition` is structural (`ofKind` reads
    /// `IsIdentity`) by default; flow-specific overrides — e.g.,
    /// Transfer's "this kind is reconciled, skip its insert" —
    /// compose on top via `reclassifyReconciled` (Transfer-side
    /// concern; emit-side realizations don't need to call it).
    let build
        (catalog: Catalog)
        (topo: TopologicalOrder)
        (rawRowsByKind: Map<SsKey, StaticRow list>)
        (remap: SurrogateRemapContext)
        : DataLoadPlan =
        let members = TopologicalOrder.cycleMembers topo
        let remapTargets =
            remap.Assignments |> Map.toSeq |> Seq.map fst |> Set.ofSeq
        let substitute (k: Kind) (raw: StaticRow list) : RemappedRows =
            if Set.isEmpty remapTargets || List.isEmpty raw then { Rows = raw; Skipped = [] }
            else
                let fkTargets = SurrogateRemap.fkColumnsTargeting remapTargets k
                if Map.isEmpty fkTargets then { Rows = raw; Skipped = [] }
                else SurrogateRemap.remapRowFks fkTargets remap raw

        let loadAndSkipped =
            topo.Order
            |> List.choose (fun key ->
                Catalog.tryFindKind key catalog
                |> Option.map (fun k ->
                    let raw = Map.tryFind key rawRowsByKind |> Option.defaultValue []
                    let remapped = substitute k raw
                    let load =
                        { Kind              = key
                          Disposition       = IdentityDisposition.ofKind k
                          DeferredFkColumns = TopologicalOrder.deferredFkColumns members k
                          Rows              = remapped.Rows }
                    let owned = remapped.Skipped |> List.map (fun u -> key, u)
                    load, owned))

        let loads   = loadAndSkipped |> List.map fst
        let skipped = loadAndSkipped |> List.collect snd

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

        { Loads               = loads
          UnbreakableCycleFks = unbreakable
          SkippedReferences   = skipped }

    /// Override the disposition of the reconciled kinds to
    /// `ReconciledByRule` and zero their `Rows` (the rows already
    /// exist in the target; realizations skip the write). A
    /// Transfer-specific post-processing step — the operator's
    /// reconciliation choice — sitting separately from the universal
    /// remap-substitution at `build`. `ofKind` never returns
    /// `ReconciledByRule`; it is operator-chosen, so this is its only
    /// production path on the plan.
    let reclassifyReconciled (reconciledKinds: Set<SsKey>) (plan: DataLoadPlan) : DataLoadPlan =
        if Set.isEmpty reconciledKinds then plan
        else
            { plan with
                Loads =
                    plan.Loads
                    |> List.map (fun l ->
                        if Set.contains l.Kind reconciledKinds
                        then { l with Disposition = IdentityDisposition.ReconciledByRule; Rows = [] }
                        else l) }

    /// The kinds whose Phase-1 insert must NULL one or more FK columns
    /// and be re-pointed in Phase 2 (the cycle-broken kinds).
    /// Convenience for a realization that wants the Phase-2 work list.
    let deferredLoads (plan: DataLoadPlan) : DataLoadKind list =
        plan.Loads |> List.filter (fun l -> not (Set.isEmpty l.DeferredFkColumns))

    /// True iff every cycle FK is deferrable — i.e. the plan is a clean
    /// two-phase load with no unsatisfiable FK.
    let isSatisfiable (plan: DataLoadPlan) : bool =
        List.isEmpty plan.UnbreakableCycleFks

    /// Registry metadata (pillar 9). `DataLoadPlan.build` is **the one**
    /// `OperatorIntent Insertion` site in the entire data-load family —
    /// the operator-supplied `SurrogateRemapContext` is applied here,
    /// once, to produce post-substitution rows. Every downstream
    /// realization (`StaticSeedsEmitter`, `MigrationDependenciesEmitter`,
    /// `BootstrapEmitter`, `Transfer.runReconciling`) reads the plan as
    /// already-in-target-identity-space and classifies entirely as
    /// `DataIntent` (their sites describe how to deploy the plan, not
    /// what's in it). This consolidates the prior per-emitter
    /// `OperatorIntent Insertion` sites (`userRemapRewrite`,
    /// `staticRowSurrogateRemap`) into the single canonical altitude.
    let registeredMetadata : RegisteredTransformMetadata =
        { Name         = "dataLoadPlan"
          Domain       = Data
          StageBinding = Pipeline
          Sites =
            [ TransformSite.dataIntent "kindOrdering"
                "Order loads by the precomputed `TopologicalOrder` (FK-safe). Structural; no operator opinion enters."
              TransformSite.dataIntent "dispositionClassification"
                "Classify each kind's `IdentityDisposition` via `IdentityDisposition.ofKind` (PreservedFromSource for business PKs; AssignedBySink for IDENTITY PKs). Pure over Catalog evidence."
              TransformSite.dataIntent "deferredFkSelection"
                "Select cycle-deferred FK columns per kind via `TopologicalOrder.deferredFkColumns` (in-cycle + same-cycle target + nullable). Structural cycle-break."
              TransformSite.dataIntent "unbreakableCycleDiagnostics"
                "Surface non-nullable same-cycle FKs as `UnbreakableCycleFk` so realizations refuse to execute an unsatisfiable plan. Structural; total decisions, named skips."
              TransformSite.operatorIntent "identitySubstitution" Insertion
                "Apply the operator-supplied `SurrogateRemapContext` to FK values in raw rows, producing post-substitution rows in target identity space. Every FK column whose target is in the remap is re-pointed (Source surrogate → assigned-side surrogate); rows whose targeted FK has no matched assigned counterpart are dropped (skip-and-diagnose). This is the canonical OperatorIntent Insertion site for the entire data-load family — realizations (StaticSeedsEmitter, MigrationDependenciesEmitter, BootstrapEmitter, Transfer.runReconciling) consume the post-substitution plan and classify entirely DataIntent. Empty remap → identity over rows (skeleton-purity preserved)." ]
          Status = Active }
