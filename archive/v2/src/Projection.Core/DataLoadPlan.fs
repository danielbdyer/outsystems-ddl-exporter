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
        /// The FULL rows behind `SkippedReferences`, in the same order
        /// (2026-07-08, the board-clarity program): each dropped row with
        /// the reference that failed it, so a preview can show the actual
        /// record being lost, not just its failed FK coordinate. Row
        /// values are references into the already-materialized ingest —
        /// no copy.
        DroppedRows         : (SsKey * UnresolvedReference * StaticRow) list
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
    ///
    /// Slice C1 — the disposition default is selected by an `IdentityPolicy`:
    /// `buildWith` carries it; `build` fixes it to `Structural` (byte-identical).
    /// A `PreferPreservedKeys` policy (a `FullRights` sink permitting
    /// IDENTITY_INSERT) writes the source key directly for IDENTITY PKs too,
    /// so the whole capture/remap/FK-repoint machinery is skipped downstream
    /// (it already branches on `PreservedFromSource`). `reclassifyReconciled`
    /// still composes on top.
    /// The remap's target-kind keyset — precomputed once per build (or once
    /// per acquisition, for the per-kind entry below) so the per-kind
    /// substitution gate is a set lookup, not a Map walk.
    let private remapTargetsOf (remap: SurrogateRemapContext) : Set<SsKey> =
        remap.Assignments |> Map.toSeq |> Seq.map fst |> Set.ofSeq

    let private substituteWith
        (remapTargets: Set<SsKey>)
        (remap: SurrogateRemapContext)
        (k: Kind)
        (raw: StaticRow list)
        : RemappedRows =
        if Set.isEmpty remapTargets || List.isEmpty raw then { Rows = raw; Skipped = [] }
        else
            let fkTargets = SurrogateRemap.fkColumnsTargeting remapTargets k
            if Map.isEmpty fkTargets then { Rows = raw; Skipped = [] }
            else SurrogateRemap.remapRowFks fkTargets remap raw

    let private loadForCore
        (policy: IdentityPolicy)
        (deferralScopes: TopologicalOrder.DeferralScope list)
        (remapTargets: Set<SsKey>)
        (remap: SurrogateRemapContext)
        (k: Kind)
        (raw: StaticRow list)
        : DataLoadKind * (SsKey * UnresolvedReference) list =
        let remapped = substituteWith remapTargets remap k raw
        let load =
            { Kind              = k.SsKey
              Disposition       = IdentityDisposition.byPolicy policy k
              DeferredFkColumns = TopologicalOrder.deferredFkColumns deferralScopes k
              Rows              = remapped.Rows }
        load, (remapped.Skipped |> List.map (fun u -> k.SsKey, u))

    /// Build ONE kind's load (+ its skipped-reference diagnostics) — the
    /// per-kind unit `buildWith` folds over the topological order, exposed
    /// so an acquisition-overlapped realization can construct a kind's load
    /// the moment its rows land (the plan factors per kind: no load field
    /// depends on another kind's rows — order, cycle membership, and the
    /// remap are all fixed before acquisition). Equivalence with the batch
    /// build is BY CONSTRUCTION: `buildWith` folds the same core this
    /// function wraps. `deferralScopes` is `TopologicalOrder.deferralScopes
    /// topo`, hoisted by the caller (once per plan, not once per kind).
    let loadForWith
        (policy: IdentityPolicy)
        (deferralScopes: TopologicalOrder.DeferralScope list)
        (remap: SurrogateRemapContext)
        (k: Kind)
        (raw: StaticRow list)
        : DataLoadKind * (SsKey * UnresolvedReference) list =
        loadForCore policy deferralScopes (remapTargetsOf remap) remap k raw

    /// The structural-policy per-kind build — `loadForWith` under
    /// `IdentityPolicy.Structural`, mirroring the `build`/`buildWith` pair.
    let loadFor
        (deferralScopes: TopologicalOrder.DeferralScope list)
        (remap: SurrogateRemapContext)
        (k: Kind)
        (raw: StaticRow list)
        : DataLoadKind * (SsKey * UnresolvedReference) list =
        loadForWith IdentityPolicy.Structural deferralScopes remap k raw

    let buildWith
        (policy: IdentityPolicy)
        (catalog: Catalog)
        (topo: TopologicalOrder)
        (rawRowsByKind: Map<SsKey, StaticRow list>)
        (remap: SurrogateRemapContext)
        : DataLoadPlan =
        let scopes = TopologicalOrder.deferralScopes topo
        let remapTargets = remapTargetsOf remap

        let loadAndSkipped =
            topo.Order
            |> List.choose (fun key ->
                Catalog.tryFindKind key catalog
                |> Option.map (fun k ->
                    let raw = Map.tryFind key rawRowsByKind |> Option.defaultValue []
                    loadForCore policy scopes remapTargets remap k raw))

        let loads   = loadAndSkipped |> List.map fst
        let skipped = loadAndSkipped |> List.collect snd

        // Unsatisfiability judges UNRESOLVED cycles only (2026-07-07, the
        // resolver-completeness program): a strong edge inside a RESOLVED
        // SCC is satisfied by the proven order (the resolver breaks weak
        // edges only), so flagging it here refused loads the order
        // handles. `members` (all cycle participants) still drives the
        // DEFERRAL above — a resolved SCC's broken weak edges genuinely
        // defer to phase 2.
        // v7 slice 3 — the unsatisfiability judges PER COMPONENT: a
        // non-nullable FK is unbreakable only when source and target sit
        // in the SAME unresolved component. The prior flat-union check
        // flagged an FK between two DISTINCT unresolved cycles — a false
        // refusal the re-run order satisfies (the condensation orders
        // the two components).
        let unresolvedScopes = TopologicalOrder.unresolvedCycleScopes topo
        let unbreakable =
            topo.Order
            |> List.collect (fun key ->
                match Catalog.tryFindKind key catalog with
                | None -> []
                | Some k ->
                    let owningScopes = unresolvedScopes |> List.filter (Set.contains key)
                    if List.isEmpty owningScopes then []
                    else
                        k.References
                        |> List.choose (fun r ->
                            if owningScopes |> List.exists (Set.contains r.TargetKind) then
                                Kind.tryFindAttribute r.SourceAttribute k
                                |> Option.bind (fun a ->
                                    if a.Column.IsNullable then None
                                    else Some { Kind = key; Column = a.Name; Target = r.TargetKind })
                            else None))

        // The dropped rows, recovered by identifier difference per kind
        // (2026-07-08): `remapRowFksWith` is order-preserving and drops a
        // row exactly when it appends a skip, so the raw rows whose
        // Identifier vanished from the kept set align one-to-one, in
        // order, with that kind's skip diagnostics. (`Seq.zip` truncates
        // defensively if a duplicate identifier ever breaks the
        // alignment precondition — a unique PK is the ingest contract.)
        let droppedRows =
            loadAndSkipped
            |> List.collect (fun (load, skips) ->
                if List.isEmpty skips then []
                else
                    let keptIds = load.Rows |> List.map (fun r -> r.Identifier) |> Set.ofList
                    let raw = Map.tryFind load.Kind rawRowsByKind |> Option.defaultValue []
                    let dropped = raw |> List.filter (fun r -> not (Set.contains r.Identifier keptIds))
                    Seq.zip skips dropped
                    |> Seq.map (fun ((kindKey, uref), row) -> kindKey, uref, row)
                    |> List.ofSeq)

        { Loads               = loads
          UnbreakableCycleFks = unbreakable
          SkippedReferences   = skipped
          DroppedRows         = droppedRows }

    /// The structural-policy build — `ofKind` from the PK shape, byte-identical
    /// to every path before Slice C. The emit-side realizations and the
    /// ManagedDml/undeclared transfer paths call this; only a `FullRights` sink
    /// reaches `buildWith IdentityPolicy.PreferPreservedKeys`.
    let build
        (catalog: Catalog)
        (topo: TopologicalOrder)
        (rawRowsByKind: Map<SsKey, StaticRow list>)
        (remap: SurrogateRemapContext)
        : DataLoadPlan =
        buildWith IdentityPolicy.Structural catalog topo rawRowsByKind remap

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
                "Select cycle-deferred FK columns per kind via `TopologicalOrder.deferredFkColumns` (same-COMPONENT target; a resolved component defers its broken edges only, an unresolved one every nullable intra-component edge — v7 slices 3+5). Structural cycle-break."
              TransformSite.dataIntent "unbreakableCycleDiagnostics"
                "Surface non-nullable same-COMPONENT FKs (unresolved components only — v7 slice 3) as `UnbreakableCycleFk` so realizations refuse to execute an unsatisfiable plan. Structural; total decisions, named skips."
              TransformSite.operatorIntent "identitySubstitution" Insertion
                "Apply the operator-supplied `SurrogateRemapContext` to FK values in raw rows, producing post-substitution rows in target identity space. Every FK column whose target is in the remap is re-pointed (Source surrogate → assigned-side surrogate); rows whose targeted FK has no matched assigned counterpart are dropped (skip-and-diagnose). This is the canonical OperatorIntent Insertion site for the entire data-load family — realizations (StaticSeedsEmitter, MigrationDependenciesEmitter, BootstrapEmitter, Transfer.runReconciling) consume the post-substitution plan and classify entirely DataIntent. Empty remap → identity over rows (skeleton-purity preserved)." ]
          Status = Active }
