namespace Projection.Pipeline

open Projection.Core
open Projection.Adapters.Osm
open Projection.Targets.SSDT
open Projection.Targets.Json
open Projection.Targets.Distributions
open Projection.Targets.Data
open Projection.Targets.OperationalDiagnostics

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
    /// 3 emitters) → StaticPopulation emitter (1). Total: 21
    /// registrations as of slice A.4.7' overlay-exercise (this slice
    /// builds the surface and ships the FsCheck-driven bidirectional
    /// property tests against it).
    ///
    /// **Skeleton-view consumers** use `TransformRegistry.skeletonView`
    /// to filter to DataIntent-only entries; **overlay-exercise
    /// consumers** use `TransformRegistry.overlayView` for the
    /// complementary set. Both views project from this single source.
    let all : RegisteredTransformMetadata list =
        [ CatalogReader.registeredMetadata
          SsdtDdlEmitter.registeredMetadata
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
