namespace Projection.Core

open Projection.Core.Passes

/// Materialized collection of V2's Core-resident registered
/// transformations. Per chapter A.4.7' slice β
/// (`CHAPTER_A_4_7_PRIME_OPEN.md`): this is the substantive
/// populated form that consumers traverse.
///
/// **Single definition site — the registry drives the run
/// (`DECISIONS 2026-06-04`).** The Core pass chain is defined ONCE, as
/// `chainSteps : ChainStep list`. Each `ChainStep` pairs a pass's
/// metadata (the pillar-9 classification surface) with how it plugs
/// into the run (`Build` — the lift strategy + `ComposeState` writeback
/// + `Policy` / `Profile` threading). The metadata registry (`all`) and
/// the execution chain (`allChainSteps` / `allChainStepsFor`) are both
/// **projections of `chainSteps`** — they can no longer drift, and
/// `transform.registered` describes exactly what executes. (The prior
/// design hand-maintained three parallel lists in the same order; they
/// had already drifted on `TableRename`'s position.)
///
/// **Compile-order rationale.** `TransformRegistry.fs` compiles
/// before any pass module (the registry types must be in scope
/// when pass modules declare their `.registered` exports). The
/// populated `chainSteps` / derivations therefore cannot live in
/// `TransformRegistry.fs`; this file lives at the end of the Core
/// compile order, where every pass module's `.registered` is in
/// scope.
///
/// **Project-boundary note.** The OSSYS adapter's
/// `CatalogReader.registeredMetadata` lives in
/// `Projection.Adapters.Osm` — outside Core. The full production
/// registry (per A41 totality) assembles at the Pipeline / consumer
/// level by prepending the adapter + emitter metadata to
/// `RegisteredTransforms.all`.
///
/// **Skeleton-friendly defaults.** Config-taking pass factories
/// (`VisibilityMask.registered mask`, `NamingMorphism.registered
/// morphism`, `TableRename.registered specs`, plus the `Policy ×
/// Profile` decision-set passes) take empty / identity defaults for
/// the metadata projection (metadata is config-invariant — Sites /
/// Name / Domain / Status); the `Build` closure captures the caller's
/// real config at run time.
///
/// **Slice D.1.a exception — logical-name emission ships Enabled.**
/// `LogicalTableEmission.registered Enabled` + `LogicalColumnEmission.registered Enabled`
/// are the production defaults (V2 emits logical names — `Customer`
/// not `OSUSR_ABC_CUSTOMER` — out of the box). Both carry
/// `OperatorIntent Emission` (the operator chose either way; default-on
/// IS the operator's intent).
[<RequireQualifiedAccess>]
module RegisteredTransforms =

    let private emptyMask : VisibilityMask.Mask = { Hide = [] }
    let private identityMorphism : NamingMorphism.Morphism = NamingMorphism.identity

    // ------------------------------------------------------------------
    // ChainStep builders — one per pass *shape* (how it reads from /
    // writes back to ComposeState). The metadata projection and the
    // execution lift both flow from the same pass `.registered` factory,
    // so a pass is defined ONCE.
    // ------------------------------------------------------------------

    /// Config-invariant catalog-rewriting pass (output replaces
    /// `state.Catalog`).
    let private catalogStep (rt: RegisteredTransform<Catalog, Catalog>) : ChainStep =
        { Metadata = RegisteredTransform.toMetadata rt
          Build    = fun _ _ -> PassChainAdapter.liftCatalogPass rt }

    /// Config-invariant decision pass reading `state.Catalog`.
    let private decisionStep
        (rt: RegisteredTransform<Catalog, 'D>)
        (writeBack: 'D -> ComposeState -> ComposeState)
        : ChainStep =
        { Metadata = RegisteredTransform.toMetadata rt
          Build    = fun _ _ -> PassChainAdapter.liftDecisionPass rt writeBack }

    /// Config-invariant decision pass reading the pre-computed topology.
    let private topologyStep
        (rt: RegisteredTransform<TopologicalOrder, 'D>)
        (writeBack: 'D -> ComposeState -> ComposeState)
        : ChainStep =
        { Metadata = RegisteredTransform.toMetadata rt
          Build    = fun _ _ -> PassChainAdapter.liftTopologyPass rt writeBack }

    /// Profile-aware decision pass — metadata projects from the
    /// `Profile.empty` factory (config-invariant); `Build` threads the
    /// caller's profile.
    let private profileDecisionStep
        (registeredFor: Profile -> RegisteredTransform<Catalog, 'D>)
        (writeBack: 'D -> ComposeState -> ComposeState)
        : ChainStep =
        { Metadata = RegisteredTransform.toMetadata (registeredFor Profile.empty)
          Build    = fun _ profile -> PassChainAdapter.liftDecisionPass (registeredFor profile) writeBack }

    /// Policy + Profile-aware decision pass (the four tightening passes +
    /// `UserFkReflowPass`) — metadata projects from the empty-config
    /// factory; `Build` threads the caller's policy + profile.
    let private tighteningStep
        (registeredFor: Policy -> Profile -> RegisteredTransform<Catalog, 'D>)
        (writeBack: 'D -> ComposeState -> ComposeState)
        : ChainStep =
        { Metadata = RegisteredTransform.toMetadata (registeredFor Policy.empty Profile.empty)
          Build    = fun policy profile -> PassChainAdapter.liftDecisionPass (registeredFor policy profile) writeBack }

    /// **The single source of truth for the Core pass chain**, in
    /// EXECUTION order (canonical). Both the metadata registry (`all`)
    /// and the execution chain project from this list. Order: 5 catalog-
    /// rewriting passes → 2 default-on logical-name emission passes
    /// (BEFORE `TableRename` so operator pins dominate) → `TableRename`
    /// → `TopologicalOrderPass` → 2 graph-analytics passes (after
    /// topology is populated) → `ProfileAnomalyPass` → `SchemaComplexityPass`
    /// → `QueryHintPass` → 4 tightening decision passes → `UserFkReflowPass`.
    /// The chain, parameterized by the operator physical-rename pins (S6.3) the
    /// `LogicalTableEmission` step must skip so a physical-form `tableRenames`
    /// override survives into the emitted physical table. `Set.empty` is the
    /// canonical chain (`chainSteps`) — byte-identical to the pre-S6.3 behavior.
    let chainStepsWithPins (logicalEmissionPins: Set<SsKey>) : ChainStep list =
        [ catalogStep CanonicalizeIdentity.registered
          catalogStep (VisibilityMask.registered emptyMask)
          catalogStep (NamingMorphism.registered identityMorphism)
          catalogStep NormalizeStaticPopulations.registered
          catalogStep SymmetricClosure.registered
          catalogStep (LogicalTableEmission.registeredWithPins logicalEmissionPins LogicalTableEmission.Enabled)
          catalogStep (LogicalColumnEmission.registered LogicalColumnEmission.Enabled)
          catalogStep (TableRename.registered [])
          decisionStep TopologicalOrderPass.registered ComposeState.withTopologicalOrder
          topologyStep CentralityPass.registered ComposeState.withCentralityRanking
          topologyStep BoundedContextPass.registered ComposeState.withBoundedContexts
          profileDecisionStep ProfileAnomalyPass.registered ComposeState.withProfileAnomalies
          // SchemaComplexityPass reads BOTH Catalog and the pre-computed
          // topology from ComposeState at apply-time (not baked at
          // registration), so it uses the catalog-topology lift directly.
          { Metadata = RegisteredTransform.toMetadata (SchemaComplexityPass.registered None)
            Build    =
                fun _ _ ->
                    PassChainAdapter.liftCatalogTopologyPass
                        SchemaComplexityPass.name
                        SchemaComplexityPass.run
                        ComposeState.withSchemaComplexity }
          profileDecisionStep QueryHintPass.registered ComposeState.withQueryHints
          tighteningStep NullabilityPass.registered ComposeState.withNullabilityDecisions
          tighteningStep UniqueIndexPass.registered ComposeState.withUniqueIndexDecisions
          tighteningStep ForeignKeyPass.registered ComposeState.withForeignKeyDecisions
          tighteningStep CategoricalUniquenessPass.registered ComposeState.withCategoricalUniquenessDecisions
          tighteningStep UserFkReflowPass.registered ComposeState.withUserRemap ]

    /// The canonical chain — no physical-rename pins (byte-identical default).
    let chainSteps : ChainStep list = chainStepsWithPins Set.empty

    /// The full Core metadata registry — every chain step's metadata
    /// (projected from `chainSteps`) plus the strategy registrations.
    /// `transform.registered`, the manifest emitter, and the A41 totality
    /// property tests read this; it cannot drift from what runs.
    let all : RegisteredTransformMetadata list =
        (chainSteps |> List.map ChainStep.metadata) @ StrategyRegistrations.all

    /// The execution chain threaded with a caller-supplied `Policy` +
    /// `Profile` through every step's `Build`. Catalog-rewriting +
    /// config-invariant steps ignore both; the profile / tightening
    /// steps capture them.
    let allChainStepsFor (policy: Policy) (profile: Profile) : PassChainAdapter list =
        chainSteps |> List.map (ChainStep.build policy profile)

    /// The execution chain with operator physical-rename pins (S6.3) — the
    /// `LogicalTableEmission` step skips the pinned kinds so a physical-form
    /// `tableRenames` override survives into the emitted physical table.
    /// `Set.empty` is `allChainStepsFor` (byte-identical).
    let allChainStepsForWithPins
        (logicalEmissionPins: Set<SsKey>)
        (policy: Policy)
        (profile: Profile)
        : PassChainAdapter list =
        chainStepsWithPins logicalEmissionPins |> List.map (ChainStep.build policy profile)

    /// The execution chain with skeleton-friendly empty defaults —
    /// byte-identical to the prior hand-written `allChainSteps`
    /// (`allChainStepsFor Policy.empty Profile.empty`).
    let allChainSteps : PassChainAdapter list =
        allChainStepsFor Policy.empty Profile.empty

    /// `allChainSteps` filtered to entries whose metadata is in
    /// `TransformRegistry.skeletonView` (every Site classifies as
    /// `DataIntent`). Consumed by `Compose.runSkeleton` to produce the
    /// baseline reachable from `Project(catalog, Policy.empty, profile)`
    /// without operator opinion. Join key is `Name`.
    let skeletonChainSteps : PassChainAdapter list =
        let skeletonPassNames =
            TransformRegistry.skeletonView all
            |> List.filter (fun rt -> rt.StageBinding = Pass)
            |> List.map (fun rt -> rt.Name)
            |> Set.ofList
        allChainSteps
        |> List.filter (fun adapter -> Set.contains adapter.Name skeletonPassNames)
