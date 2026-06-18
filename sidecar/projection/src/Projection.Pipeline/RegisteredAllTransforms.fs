namespace Projection.Pipeline

open Projection.Core
open Projection.Adapters.Osm
open Projection.Targets.SSDT
open Projection.Targets.Json
open Projection.Targets.Distributions
open Projection.Targets.Data
open Projection.Targets.OperationalDiagnostics
open Projection.Adapters.Sql

/// Pipeline-level unified registry assembly. Concatenates every
/// `RegisteredTransformMetadata` surface V2 ships:
///   - `RegisteredTransforms.all` (Core: passes + ordering policies)
///   - `CatalogReader.registeredMetadata` (OSSYS adapter)
///   - Sibling-Π emitter metadata (SSDT / Json / Distributions /
///     StaticPopulation)
///   - `RegisteredDataTransforms.all` (Data-axis composer + emitters)
///
/// Per the cherry-pick boundary discipline (`DECISIONS 2026-05-15 (late)
/// — Pillar 9`): each project owns its own registry surface; this
/// module is the *call-site assembly* that downstream consumers
/// (CLI / canary / property tests) reach for when they need the
/// totality view. The unified surface lives in Pipeline because
/// Pipeline is the first project in the dependency graph that
/// references every emitter target.
///
/// Per A41 candidate (registry totality + bidirectional property
/// tests): this surface IS the registry the skeleton-purity +
/// overlay-exercise property tests iterate over. Pillar 9 named
/// failure mode `skeleton-overlay drift` — three sub-modes —
/// surfaces here when the iteration finds (a) a DataIntent-marked
/// site whose pass leaks OperatorIntent events; (b) an OperatorIntent
/// site whose pass never fires; (c) a transformation site missing
/// from the registry.
[<RequireQualifiedAccess>]
module RegisteredAllTransforms =

    /// Every registered transformation V2 ships, regardless of stage
    /// binding (Adapter / Pass / OrderingPolicy / Emitter / Pipeline).
    /// Iteration order: Core passes + ordering policies (12) →
    /// OSSYS adapter (1) → SSDT emitter (1) → Json emitter (1) →
    /// Distributions emitter (1) → Data-axis surfaces (4: composer +
    /// 3 emitters) → StaticPopulation emitter (1) → operator-UX
    /// projections → **Transfer epic (3: ingestion adapter + plan +
    /// Projection-onto-Sink)**. The bidirectional property tests (stage /
    /// domain coverage, validate-through-create, both classifications
    /// present) project from this single source — no hardcoded count.
    ///
    /// **Skeleton-view consumers** use `TransformRegistry.skeletonView`
    /// to filter to DataIntent-only entries; **overlay-exercise
    /// consumers** use `TransformRegistry.overlayView` for the
    /// complementary set. Both views project from this single source.
    /// F2 (audit 2026-06-17) — registry visibility for the post-chain
    /// emit-seam index pruning. `EmissionPolicy.filterPlatformAutoIndexes`
    /// (Core) prunes `IsPlatformAuto` indexes when
    /// `IncludePlatformAutoIndexes = false`; it executes at the emit seam
    /// (`Pipeline.fs` main-emit + dacpac) like the other conditional emitters
    /// (DacpacEmitter, ConstraintFormatter — "registered-as-metadata, executed
    /// at their own sites"). It is an `OperatorIntent Emission` mutation (the
    /// toggle is operator policy), so it registers here. The metadata cannot
    /// co-locate with the Core function (Policy.fs compiles before
    /// TransformRegistry.fs), so it lives at the Pipeline assembly point. The
    /// fuller structural lift — routing it through the registered chain seam so
    /// execution↔registration is bound, not just both-present — is audit F3.
    let private filterPlatformAutoIndexesMetadata : RegisteredTransformMetadata =
        RegisteredTransformMetadata.emitter "filterPlatformAutoIndexes" Schema
            [ TransformSite.operatorIntent "platformAutoIndexPruning" Emission
                "Prune indexes marked IsPlatformAuto=true from the emitted catalog when Policy.Emission.IncludePlatformAutoIndexes=false (chapter 4.8 slice γ; V1-parity default keeps them). Applied at the emit seam (post-chain), executed at its own site like DacpacEmitter. OperatorIntent Emission: the IncludePlatformAutoIndexes toggle is operator-supplied emission policy, not source evidence." ]

    let all : RegisteredTransformMetadata list =
        // E1 (`DECISIONS 2026-06-04`) — the full-export emit phase's six
        // sibling-Π emitters (SSDT / Json / Distributions / Remediation /
        // Summary / SuggestConfig) project their metadata from the SAME
        // `Compose.emitSteps` that drives their execution, so
        // `registered ⇔ executed` holds for the emit stage by construction.
        // (SuggestConfig was previously executed-but-unregistered — E1 closes
        // that mismatch.)
        (Compose.emitSteps |> List.map (fun step -> step.Metadata))
        // E2 (`DECISIONS 2026-06-04`) — the read adapter projects its metadata
        // from the SAME `Compose.readStep` that `Compose.read` / `readJson`
        // dispatch through, so `registered ⇔ executed` holds for the read
        // stage. Still registered-as-metadata, executed at their own sites
        // (the E4 follow-up): the conditional render-mode / dacpac /
        // data-bundle emitters. `ConstraintFormatter` is `OperatorIntent
        // Emission` (Slice D.3.b — the rendered-text-boundary overlay sibling
        // to the SSDT emitter); the others classify DataIntent.
        @ [ Compose.readStep.Metadata
            ConstraintFormatter.registeredMetadata
            DacpacEmitter.registeredMetadata
            StaticPopulationEmitter.registeredMetadata
            // F2 / F13 (audit 2026-06-17) — two Catalog→Catalog mutators that
            // run at their own boundary sites. F2: the emit-seam index prune
            // (fresh metadata). F13: the static-row hydration adapter
            // (`fullExportHydration`) was already authored but never wired into
            // this totality view — registered-in-isolation; the wiring closes it.
            filterPlatformAutoIndexesMetadata
            Hydration.registeredMetadata ]
        @ RegisteredDataTransforms.all
        @ RegisteredTransforms.all
        // Transfer epic (bidirectional data load) — the reader leg
        // (Ingestion adapter), the pure two-phase plan, and the
        // Projection-onto-Sink realization. All DataIntent today; the
        // operator `--disposition` / `ReconciledByRule` overlays (Slices
        // C′/D) will add OperatorIntent sites in place.
        @ [ Ingestion.registeredMetadata
            DataLoadPlan.registeredMetadata
            Transfer.registeredMetadata
            // Slice C′ — the ReconciledByRule matching ruleset is the
            // Transfer epic's first OperatorIntent site (Selection axis,
            // mirroring the forward UserFkReflowPass).
            Reconciliation.registeredMetadata ]
