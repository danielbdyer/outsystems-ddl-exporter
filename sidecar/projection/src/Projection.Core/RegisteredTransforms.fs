namespace Projection.Core

open Projection.Core.Passes

/// Materialized collection of V2's Core-resident registered
/// transformations. Per chapter A.4.7' slice β
/// (`CHAPTER_A_4_7_PRIME_OPEN.md`): this is the substantive
/// populated form that consumers traverse.
///
/// **Compile-order rationale.** `TransformRegistry.fs` compiles
/// before any pass module (the registry types must be in scope
/// when pass modules declare their `.registered` exports). The
/// populated `all` / `allChainSteps` therefore cannot live in
/// `TransformRegistry.fs`; this file lives at the end of the Core
/// compile order, where every pass module's `.registered` is in
/// scope.
///
/// **Project-boundary note.** The OSSYS adapter's
/// `CatalogReader.registeredMetadata` lives in
/// `Projection.Adapters.Osm` — outside Core. The full 18-entry
/// production registry (per A41 totality) assembles at the
/// Pipeline / consumer level by prepending the adapter metadata to
/// `RegisteredTransforms.all`.
///
/// **Skeleton-friendly defaults.** Config-taking pass factories
/// (`VisibilityMask.registered mask`, `NamingMorphism.registered
/// morphism`, `TableRename.registered specs`, plus the four
/// `Policy × Profile` decision-set passes) consume empty / identity
/// defaults here. Metadata is invariant of config (Sites / Name /
/// Domain / Status); the Apply closure captures the config, so
/// non-default-config consumers (Compose.project at slice δ) call
/// the factories directly. Slice γ / δ may add a factory variant
/// of `allChainSteps` if two consumers demand it.
///
/// **Slice D.1.a exception — logical-name emission ships Enabled.**
/// `LogicalTableEmission.registered Enabled` + `LogicalColumnEmission.registered Enabled`
/// are the production defaults (V2 emits logical names — `Customer`
/// not `OSUSR_ABC_CUSTOMER` — out of the box). Operators that want
/// physical-name emission for diagnostic / V1-parity reasons wire
/// `Disabled` mode explicitly. Both modes carry `OperatorIntent Emission`
/// (the operator chose either way; default-on IS the operator's intent).
[<RequireQualifiedAccess>]
module RegisteredTransforms =

    let private emptyMask : VisibilityMask.Mask = { Hide = [] }
    let private identityMorphism : NamingMorphism.Morphism = NamingMorphism.identity

    /// 24 Core-resident `RegisteredTransformMetadata` entries — 19
    /// pass + 5 strategy. Validates through `TransformRegistry.create`
    /// (uniqueness of Name; non-empty Site.Rationale; substantive
    /// `NotImplementedInV2` rationale where applicable) per A41.
    /// Cluster D adds 5 graph-analytics passes (H-071 through H-076,
    /// H-073 through H-075 share a single pass count with H-073/H-076).
    /// Slice D.1.a adds `LogicalTableEmission` + `LogicalColumnEmission`
    /// (default-on emission-axis logical-name substitution passes).
    let all : RegisteredTransformMetadata list =
        [ RegisteredTransform.toMetadata CanonicalizeIdentity.registered
          RegisteredTransform.toMetadata (VisibilityMask.registered emptyMask)
          RegisteredTransform.toMetadata (NamingMorphism.registered identityMorphism)
          RegisteredTransform.toMetadata NormalizeStaticPopulations.registered
          RegisteredTransform.toMetadata SymmetricClosure.registered
          RegisteredTransform.toMetadata (TableRename.registered [])
          RegisteredTransform.toMetadata (LogicalTableEmission.registered LogicalTableEmission.Enabled)
          RegisteredTransform.toMetadata (LogicalColumnEmission.registered LogicalColumnEmission.Enabled)
          RegisteredTransform.toMetadata TopologicalOrderPass.registered
          RegisteredTransform.toMetadata CentralityPass.registered
          RegisteredTransform.toMetadata BoundedContextPass.registered
          RegisteredTransform.toMetadata (ProfileAnomalyPass.registered Profile.empty)
          RegisteredTransform.toMetadata (SchemaComplexityPass.registered None)
          RegisteredTransform.toMetadata (QueryHintPass.registered Profile.empty)
          RegisteredTransform.toMetadata (NullabilityPass.registered Policy.empty Profile.empty)
          RegisteredTransform.toMetadata (UniqueIndexPass.registered Policy.empty Profile.empty)
          RegisteredTransform.toMetadata (ForeignKeyPass.registered Policy.empty Profile.empty)
          RegisteredTransform.toMetadata (CategoricalUniquenessPass.registered Policy.empty Profile.empty)
          RegisteredTransform.toMetadata (UserFkReflowPass.registered Policy.empty Profile.empty) ]
        @ StrategyRegistrations.all

    /// 19 `PassChainAdapter` entries — the typed execution surface
    /// for slice γ's `runChain` kernel. Order: 6 catalog-rewriting
    /// passes (CanonicalizeIdentity / VisibilityMask / NamingMorphism /
    /// NormalizeStaticPopulations / SymmetricClosure / TableRename),
    /// then 2 default-on logical-name emission-axis substitutions
    /// (LogicalTableEmission / LogicalColumnEmission; slice D.1.a),
    /// then the topological-order pass, then 5 Cluster D graph-
    /// analytics passes (H-071 through H-076), then 6 decision-set
    /// passes. Each analytics and decision-set pass writes back via
    /// the matching `ComposeState.with*` setter.
    ///
    /// Cluster D analytics passes run after TopologicalOrderPass so
    /// CentralityPass and BoundedContextPass have topology available
    /// in ComposeState. The H-073 / H-076 profile-driven passes use
    /// `Profile.empty` as default; supply a non-empty profile via
    /// `allChainStepsFor` for LiveProfiler-enabled paths.
    let allChainSteps : PassChainAdapter list =
        [ PassChainAdapter.liftCatalogPass CanonicalizeIdentity.registered
          PassChainAdapter.liftCatalogPass (VisibilityMask.registered emptyMask)
          PassChainAdapter.liftCatalogPass (NamingMorphism.registered identityMorphism)
          PassChainAdapter.liftCatalogPass NormalizeStaticPopulations.registered
          PassChainAdapter.liftCatalogPass SymmetricClosure.registered
          // Slice D.1.a — logical-name emission as default. Both run
          // BEFORE TableRename so operator-supplied physical pinnings
          // dominate (TableRename writes to Kind.Physical last; the
          // logical substitution runs first and TableRename overrides
          // when an operator pins). Both classified
          // OperatorIntent Emission.
          PassChainAdapter.liftCatalogPass (LogicalTableEmission.registered LogicalTableEmission.Enabled)
          PassChainAdapter.liftCatalogPass (LogicalColumnEmission.registered LogicalColumnEmission.Enabled)
          PassChainAdapter.liftCatalogPass (TableRename.registered [])
          PassChainAdapter.liftDecisionPass
            TopologicalOrderPass.registered
            ComposeState.withTopologicalOrder
          // Cluster D graph-analytics passes — run after topology is
          // populated in ComposeState.
          PassChainAdapter.liftTopologyPass
            CentralityPass.registered
            ComposeState.withCentralityRanking
          PassChainAdapter.liftTopologyPass
            BoundedContextPass.registered
            ComposeState.withBoundedContexts
          PassChainAdapter.liftDecisionPass
            (ProfileAnomalyPass.registered Profile.empty)
            ComposeState.withProfileAnomalies
          PassChainAdapter.liftDecisionPass
            (SchemaComplexityPass.registered None)
            ComposeState.withSchemaComplexity
          PassChainAdapter.liftDecisionPass
            (QueryHintPass.registered Profile.empty)
            ComposeState.withQueryHints
          PassChainAdapter.liftDecisionPass
            (NullabilityPass.registered Policy.empty Profile.empty)
            ComposeState.withNullabilityDecisions
          PassChainAdapter.liftDecisionPass
            (UniqueIndexPass.registered Policy.empty Profile.empty)
            ComposeState.withUniqueIndexDecisions
          PassChainAdapter.liftDecisionPass
            (ForeignKeyPass.registered Policy.empty Profile.empty)
            ComposeState.withForeignKeyDecisions
          PassChainAdapter.liftDecisionPass
            (CategoricalUniquenessPass.registered Policy.empty Profile.empty)
            ComposeState.withCategoricalUniquenessDecisions
          PassChainAdapter.liftDecisionPass
            (UserFkReflowPass.registered Policy.empty Profile.empty)
            ComposeState.withUserRemap ]

    /// Chapter C slice C.1 — factory variant of `allChainSteps` that
    /// threads a caller-supplied `Policy` + `Profile` through the
    /// four decision-set passes (NullabilityPass / UniqueIndexPass /
    /// ForeignKeyPass / CategoricalUniquenessPass) instead of baking
    /// `Policy.empty` + `Profile.empty` at module init.
    ///
    /// Used by `Compose.projectWith` (Pipeline.fs) — the slice-C.1
    /// cash-out routes operator-supplied tightening interventions
    /// through the existing pass chain by registering them per-call.
    /// `allChainSteps` (the static version above) stays for the
    /// skeleton-only / no-policy paths.
    ///
    /// Catalog-rewriting passes (entries 0-6 in the chain) are
    /// policy-invariant; they reuse the same closures as the static
    /// version. Only the 6 decision-set passes (TopologicalOrderPass
    /// + 4 tightening passes + UserFkReflowPass) re-construct with the
    /// caller's policy.
    let allChainStepsFor (policy: Policy) (profile: Profile) : PassChainAdapter list =
        [ PassChainAdapter.liftCatalogPass CanonicalizeIdentity.registered
          PassChainAdapter.liftCatalogPass (VisibilityMask.registered emptyMask)
          PassChainAdapter.liftCatalogPass (NamingMorphism.registered identityMorphism)
          PassChainAdapter.liftCatalogPass NormalizeStaticPopulations.registered
          PassChainAdapter.liftCatalogPass SymmetricClosure.registered
          // Slice D.1.a — logical-name emission as default; same wiring
          // as allChainSteps (the policy/profile axis doesn't gate the
          // logical-emission default). Runs BEFORE TableRename so
          // operator pins dominate.
          PassChainAdapter.liftCatalogPass (LogicalTableEmission.registered LogicalTableEmission.Enabled)
          PassChainAdapter.liftCatalogPass (LogicalColumnEmission.registered LogicalColumnEmission.Enabled)
          PassChainAdapter.liftCatalogPass (TableRename.registered [])
          PassChainAdapter.liftDecisionPass
            TopologicalOrderPass.registered
            ComposeState.withTopologicalOrder
          // Cluster D analytics passes — profile-aware variants use the
          // caller-supplied profile rather than Profile.empty.
          PassChainAdapter.liftTopologyPass
            CentralityPass.registered
            ComposeState.withCentralityRanking
          PassChainAdapter.liftTopologyPass
            BoundedContextPass.registered
            ComposeState.withBoundedContexts
          PassChainAdapter.liftDecisionPass
            (ProfileAnomalyPass.registered profile)
            ComposeState.withProfileAnomalies
          PassChainAdapter.liftDecisionPass
            (SchemaComplexityPass.registered None)
            ComposeState.withSchemaComplexity
          PassChainAdapter.liftDecisionPass
            (QueryHintPass.registered profile)
            ComposeState.withQueryHints
          PassChainAdapter.liftDecisionPass
            (NullabilityPass.registered policy profile)
            ComposeState.withNullabilityDecisions
          PassChainAdapter.liftDecisionPass
            (UniqueIndexPass.registered policy profile)
            ComposeState.withUniqueIndexDecisions
          PassChainAdapter.liftDecisionPass
            (ForeignKeyPass.registered policy profile)
            ComposeState.withForeignKeyDecisions
          PassChainAdapter.liftDecisionPass
            (CategoricalUniquenessPass.registered policy profile)
            ComposeState.withCategoricalUniquenessDecisions
          PassChainAdapter.liftDecisionPass
            (UserFkReflowPass.registered policy profile)
            ComposeState.withUserRemap ]

    /// Chapter A.4.7' slice ε — `allChainSteps` filtered to entries
    /// whose corresponding metadata is in `TransformRegistry.skeletonView`
    /// (every Site classifies as `DataIntent`). Consumed by
    /// `Compose.runSkeleton` (Pipeline.fs) to produce the baseline
    /// reachable from `Project(catalog, Policy.empty, profile)`
    /// without operator opinion.
    ///
    /// Join key is Name: each `PassChainAdapter.Name` mirrors the
    /// wrapped `RegisteredTransform.Name`. The filter excludes passes
    /// whose Sites contain at least one `OperatorIntent _` —
    /// `TopologicalOrderPass` (selfLoopHandling = OperatorIntent
    /// Ordering); the four Tightening-axis decision-set passes
    /// (NullabilityPass / UniqueIndexPass / ForeignKeyPass /
    /// CategoricalUniquenessPass — each Sites carries an
    /// OperatorIntent Tightening); `VisibilityMask` (hideOrigin /
    /// hideKeys / hideModality = OperatorIntent Selection);
    /// `NamingMorphism` (presentation-name rewrite = OperatorIntent
    /// Emission); `TableRename` (physical-name rewrite =
    /// OperatorIntent Emission); `UserFkReflowPass` (UserRemap =
    /// OperatorIntent Insertion).
    let skeletonChainSteps : PassChainAdapter list =
        let skeletonPassNames =
            TransformRegistry.skeletonView all
            |> List.filter (fun rt -> rt.StageBinding = Pass)
            |> List.map (fun rt -> rt.Name)
            |> Set.ofList
        allChainSteps
        |> List.filter (fun adapter -> Set.contains adapter.Name skeletonPassNames)
