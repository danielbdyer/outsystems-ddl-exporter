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
    let all : RegisteredTransformMetadata list =
        [ CatalogReader.registeredMetadata
          SsdtDdlEmitter.registeredMetadata
          // Slice D.3.b — `ConstraintFormatter.registeredMetadata`
          // is the realization-layer overlay sibling to the SSDT
          // emitter. Classified `OperatorIntent Emission` per pillar
          // 9; pairs with `LogicalTableEmission` / `LogicalColumnEmission`
          // (catalog-level) on the same Emission axis but operates at
          // the rendered-text boundary (Mode parameter at `Render.toText`
          // call site; default-on production wiring).
          ConstraintFormatter.registeredMetadata
          DacpacEmitter.registeredMetadata
          JsonEmitter.registeredMetadata
          DistributionsEmitter.registeredMetadata
          StaticPopulationEmitter.registeredMetadata
          // Chapter 5+ slices 5.13.remediation-emitter +
          // 5.13.summary-formatter — operator-UX projections under
          // Projection.Targets.OperationalDiagnostics; both classify
          // as DataIntent (the projections derive from DecisionSet
          // outcomes carrying observed evidence; no operator opinion
          // enters the projection).
          RemediationEmitter.registeredMetadata
          SummaryFormatter.registeredMetadata ]
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
