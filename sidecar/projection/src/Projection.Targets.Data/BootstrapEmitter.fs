namespace Projection.Targets.Data

open Projection.Core
open Projection.Core.Passes

// ---------------------------------------------------------------------------
// UserRemapContext (chapter 4.2 slice γ refinement of chapter 4.1.B
// slice ζ placeholder).
//
// At chapter 4.1.B slice ζ this file defined a placeholder `Map<SsKey,
// Map<int64, int64>>` shape for `UserRemapContext`; chapter 4.2 slice γ
// refines it to a typed record (`{ Mapping; Unmatched; Diagnostics }`)
// living in `Projection.Core/UserRemap.fs`. This emitter now imports
// the Core type; the slice ζ MVP behavior (every kind a no-op artifact;
// `UserRemapContext.empty` pass-through) is preserved.
//
// The discovery pass that POPULATES `UserRemapContext` lands at
// chapter 4.2 slice δ (`UserFkReflowPass.discover`); the emitter
// integration that CONSUMES the populated context at row-emission
// time lands at chapter 4.2 slice η.
// ---------------------------------------------------------------------------

/// Π_Bootstrap — chapter 4.1.B slice ζ emitter. Per pre-scope §2.3:
/// "Bootstrap emits inserts for system users, default policies, and
/// any remaining-by-policy kinds whose data is not in StaticSeeds or
/// MigrationDependencies."
///
/// **Slice ζ MVP scope.** The emitter ships structurally — type
/// signature, composer integration, T11 keyset coverage — but emits
/// no rows today. The data sources Bootstrap needs (system-user
/// fixtures, default-policy snapshots, profile-attached row data)
/// land at chapters 4.2 + 4.3 when those consumers materialize. Per
/// IR-grows-under-evidence, the structural hook lands now (so the
/// composer's dispatch tree completes); the actual content fills in
/// as consumer-driven evidence surfaces.
///
/// **Why ship the stub now.** The slice η composer dispatches
/// through three sibling positions; pre-ζ the third position was a
/// `emptyArtifact` no-op directly inside the composer. Lifting the
/// stub into a named emitter module (a) gives chapters 4.2 / 4.3 a
/// fixed insertion point, (b) makes the slice θ partition assertion
/// honest (the composer asks Bootstrap for its coverage rather than
/// silently knowing it's empty), and (c) preserves T11 + A18 amended
/// at the structural level for the third sibling.
///
/// **A18 amended.** Signature carries `Catalog × Profile ×
/// UserRemapContext`; never `Policy`. The composer
/// (`DataEmissionComposer`) reads `Policy.Emission.DataComposition`
/// and chooses whether this emitter fires.
[<RequireQualifiedAccess>]
module BootstrapEmitter =

    [<Literal>]
    let version : int = 1

    /// Empty-script projection — the slice ζ MVP shape per kind.
    /// Carries the slice ι split (`RenderedPhase1` + `RenderedPhase2`)
    /// at empty defaults; per-kind `Rendered` is the empty
    /// concatenation.
    let private emptyScript : DataInsertScript =
        { Phase1Merges   = []
          Phase2Updates  = []
          RenderedPhase1 = ""
          RenderedPhase2 = ""
          Rendered       = "" }

    /// Π_Bootstrap emit (composer-facing; hoisted-topo + UserRemap
    /// context). Slice ζ MVP returns the empty no-op artifact for
    /// every kind. Future chapters fill in:
    ///   - Chapter 4.2 (UserFkReflowPass): populate
    ///     `UserRemapContext` so Bootstrap can rewrite User FKs in
    ///     the rows it emits.
    ///   - Chapter 4.3 (Diagnostics emitters): if Bootstrap gains
    ///     a per-kind row source from Profile evidence, surface it
    ///     here.
    let emitWithTopo
        (_topo: TopologicalOrder)
        (catalog: Catalog)
        (_profile: Profile)
        (_userRemap: UserRemapContext)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        use _ = Bench.scope "emit.bootstrap.emitWithTopo"
        let allKinds = Catalog.allKinds catalog
        let slices =
            allKinds
            |> List.map (fun k -> k.SsKey, emptyScript)
            |> Map.ofList
        ArtifactByKind.create catalog slices

    /// Π_Bootstrap emit (standalone). Convenience for callers that
    /// don't go through the `DataEmissionComposer`. Slice ζ MVP
    /// shape: returns the empty no-op artifact for every kind.
    let emit
        (catalog: Catalog)
        (profile: Profile)
        (userRemap: UserRemapContext)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        use _ = Bench.scope "emit.bootstrap.emit"
        let topo = (TopologicalOrderPass.runWith TreatAsCycle catalog).Value
        emitWithTopo topo catalog profile userRemap

    /// Harvest-discipline classification per pillar 9 (chapter 5.13
    /// slice data-emission-registry).
    ///
    /// **Status = NotImplementedInV2 at slice-ζ-MVP scope.** The
    /// emitter ships structurally (T11 keyset coverage; composer-
    /// dispatch hook; A18-amended signature) but emits no rows today.
    /// The rationale captures the deferral substantively per the
    /// `TransformRegistry.create` non-empty-rationale invariant.
    /// Future chapter 4.2 slice η wires the populated `UserRemapContext`
    /// into actual row emission; the registry entry transitions to
    /// `Active` with the Sites widening (per the closed-DU expansion
    /// empirical-test discipline).
    let registeredMetadata : RegisteredTransformMetadata =
        { Name = "bootstrapEmitter"
          Domain = Data
          StageBinding = Emitter
          Sites =
            [ { SiteName = "userRemapBootstrap"
                Classification = OperatorIntent Insertion
                Rationale = "Slice ζ MVP — Bootstrap's data-publication surface is the operator's `UserRemapContext` (target-environment user identity mapping). The emitter consumes the context but emits no rows yet (chapter 4.2 slice η populates the per-kind row source). OverlayAxis = Insertion when the future emission lands; named here so chapter 4.2's cash-out doesn't need to invent the classification." } ]
          Status =
            NotImplementedInV2
                "Slice ζ MVP — Bootstrap emits the empty no-op artifact for every kind today. Chapter 4.2 slice η (UserFkReflowPass emitter integration) lands the per-kind row source; chapter 4.3 (Diagnostics emitters) lands the Profile-evidence-derived row source. The structural hook + composer dispatch land at slice ζ so chapters 4.2 + 4.3 have a fixed insertion point and the slice θ partition assertion is honest (the composer asks Bootstrap for its coverage rather than silently knowing it's empty)." }
