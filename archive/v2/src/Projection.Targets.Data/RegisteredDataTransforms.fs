namespace Projection.Targets.Data

open Projection.Core

/// Aggregated `RegisteredTransformMetadata` collection for V2's data-
/// emission axis. Per pillar 9 (`DECISIONS 2026-05-15 (late)`): every
/// transformation site advertises its harvest-discipline classification
/// in one canonical surface; this module is the data-axis equivalent of
/// `RegisteredTransforms.all` (Core-resident schema/diagnostics axes).
///
/// **Chapter 5.13 slice data-emission-registry.** The user-emphasized
/// discipline — "separate overrides of business logic into the
/// transform registry, separating it from policy implementation and
/// pure core vanilla exporting" — manifests here as a typed enumeration
/// of every data-axis transformation site, with operator-intent overlays
/// (DataComposition policy dispatch; MigrationDependencyContext rows;
/// UserRemapContext mapping) explicitly classified `OperatorIntent` and
/// pure-core vanilla projections (per-kind MERGE construction; cycle-
/// resolution; global Phase-1-then-Phase-2 ordering) classified
/// `DataIntent`.
///
/// **Project-boundary note.** Mirrors the pattern set by
/// `CatalogReader.registeredMetadata` (in `Projection.Adapters.Osm`)
/// + `RegisteredTransforms.all` (in `Projection.Core`): each project
/// owns its registry surface; the consumer (CLI / canary / manifest
/// emitter) assembles the full registry by concatenating the
/// project-owned lists at the call site.
///
/// **Why metadata-only registration (no `RegisteredTransform<'In,
/// 'Out>` typed shell).** Emitter signatures take heterogeneous inputs
/// (`Catalog × Profile × MigrationDependencyContext × UserRemapContext`)
/// and produce `Result<ArtifactByKind<DataInsertScript>, EmitError>`
/// envelopes — neither fits the `'In -> Lineage<Diagnostics<'Out>>`
/// pass shape cleanly. The adapter precedent
/// (`CatalogReader.registeredMetadata`) established metadata-only
/// registration as the right surface for boundary translations; the
/// data-emission axis inherits that pattern.
[<RequireQualifiedAccess>]
module RegisteredDataTransforms =

    /// The four data-axis registry entries: three sibling emitters
    /// + the dispatching composer. Order chosen for readability
    /// (composer first — the orchestration root — then the three
    /// siblings in chapter 4.1.B slice order: α/β/δ static seeds, ε
    /// migration, ζ bootstrap).
    let all : RegisteredTransformMetadata list =
        [ DataEmissionComposer.registeredMetadata
          StaticSeedsEmitter.registeredMetadata
          MigrationDependenciesEmitter.registeredMetadata
          BootstrapEmitter.registeredMetadata ]
