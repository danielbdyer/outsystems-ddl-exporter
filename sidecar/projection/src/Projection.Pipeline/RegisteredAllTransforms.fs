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
    /// The Transfer epic's registered surfaces (bidirectional data load) — the
    /// reader leg (`Ingestion` adapter), the pure two-phase plan (`DataLoadPlan`),
    /// the Projection-onto-Sink realization (`Transfer`), and the
    /// `ReconciledByRule` matching ruleset (`Reconciliation` — the epic's first
    /// OperatorIntent site, Selection axis). Named as ONE bound source (rather than
    /// inline in `all`) so the bidirectional test can pin the unified registry
    /// against it — the "a transfer surface dropped from the `@`-assembly" guard,
    /// the analog of `Compose.emitSteps` / `EmissionSeam.executedNames`.
    ///
    /// NB: unlike the emit / read / seam partitions — each a fold over ONE
    /// execution definition, so their metadata projects from the same list they
    /// execute — the four transfer surfaces execute at HETEROGENEOUS sites (row
    /// read / plan build / sink realize / reconcile match), with no single fold to
    /// project from. So they are registered-as-metadata (like `DacpacEmitter` /
    /// `CatalogReader`), and the test pins the registry contains exactly this
    /// declared set; binding each to its runtime call would need an XL restructure
    /// of transfer execution into one step list (tracked, not done here).
    let transferEpic : RegisteredTransformMetadata list =
        [ Ingestion.registeredMetadata
          DataLoadPlan.registeredMetadata
          Transfer.registeredMetadata
          Reconciliation.registeredMetadata ]

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
            // F13 (audit 2026-06-17) — the static-row hydration adapter
            // (`fullExportHydration`) was already authored but never wired into
            // this totality view — registered-in-isolation; the wiring closes it.
            Hydration.registeredMetadata ]
        // The row-plane DATA CORRECTION SEAM's registered overrides, projected
        // from the SAME `DataCorrectionSeam.overrides` the Pipeline executes
        // (`DataCorrectionSeam.apply` at the extract seam). Formerly the bare
        // `ApprovedDataCorrections.registeredMetadata` was spliced here while its
        // execution was hand-wired — now both flow through the one seam, so
        // `registered ⇔ executed` holds for the correction seam by construction
        // (the E5 discipline extended to the row plane; its own bidirectional test).
        @ DataCorrectionSeam.metadata
        // F2 + F3 (audit 2026-06-17) — the post-chain EMISSION SEAM's registered
        // rewrites, projected from the SAME `EmissionSeam.rewrites` the Pipeline
        // executes (`EmissionSeam.apply`). Splicing the seam's metadata here, and
        // pairing it with the bidirectional E5 test, makes the emit seam a BOUND
        // source of the totality proof (closing the F2 counterexample's class).
        @ EmissionSeam.metadata
        @ RegisteredDataTransforms.all
        @ RegisteredTransforms.all
        // Transfer epic — the one named bound source `transferEpic` (above), so
        // the bidirectional test pins the assembly against it.
        @ transferEpic
