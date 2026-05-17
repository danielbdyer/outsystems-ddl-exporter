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
[<RequireQualifiedAccess>]
module RegisteredTransforms =

    let private emptyMask : VisibilityMask.Mask = { Hide = [] }
    let private identityMorphism : NamingMorphism.Morphism = NamingMorphism.identity

    /// 17 Core-resident `RegisteredTransformMetadata` entries — 12
    /// pass + 5 strategy. Validates through `TransformRegistry.create`
    /// (uniqueness of Name; non-empty Site.Rationale; substantive
    /// `NotImplementedInV2` rationale where applicable) per A41.
    let all : RegisteredTransformMetadata list =
        [ RegisteredTransform.toMetadata CanonicalizeIdentity.registered
          RegisteredTransform.toMetadata (VisibilityMask.registered emptyMask)
          RegisteredTransform.toMetadata (NamingMorphism.registered identityMorphism)
          RegisteredTransform.toMetadata NormalizeStaticPopulations.registered
          RegisteredTransform.toMetadata SymmetricClosure.registered
          RegisteredTransform.toMetadata (TableRename.registered [])
          RegisteredTransform.toMetadata TopologicalOrderPass.registered
          RegisteredTransform.toMetadata (NullabilityPass.registered Policy.empty Profile.empty)
          RegisteredTransform.toMetadata (UniqueIndexPass.registered Policy.empty Profile.empty)
          RegisteredTransform.toMetadata (ForeignKeyPass.registered Policy.empty Profile.empty)
          RegisteredTransform.toMetadata (CategoricalUniquenessPass.registered Policy.empty Profile.empty)
          RegisteredTransform.toMetadata (UserFkReflowPass.registered Policy.empty Profile.empty) ]
        @ StrategyRegistrations.all

    /// 12 `PassChainAdapter` entries — the typed execution surface
    /// for slice γ's `runChain` kernel. Order matches `all`'s pass
    /// segment: 6 Catalog-chainable passes first, then 6 decision-
    /// set-producing passes. Each decision-set pass writes back via
    /// the matching `ComposeState.with*` setter.
    let allChainSteps : PassChainAdapter list =
        [ PassChainAdapter.liftCatalogPass CanonicalizeIdentity.registered
          PassChainAdapter.liftCatalogPass (VisibilityMask.registered emptyMask)
          PassChainAdapter.liftCatalogPass (NamingMorphism.registered identityMorphism)
          PassChainAdapter.liftCatalogPass NormalizeStaticPopulations.registered
          PassChainAdapter.liftCatalogPass SymmetricClosure.registered
          PassChainAdapter.liftCatalogPass (TableRename.registered [])
          PassChainAdapter.liftDecisionPass
            TopologicalOrderPass.registered
            ComposeState.withTopologicalOrder
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
