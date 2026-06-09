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

    /// Π_Bootstrap emit (canonical; plan-consuming). Realizes the
    /// supplied `DataLoadPlan`. Slice ζ MVP scope: Bootstrap has no
    /// row source today (the plan's `Loads[i].Rows` are empty for
    /// every kind in the current call paths), so emission is
    /// uniformly the empty no-op script per T11 keyset coverage.
    /// Chapter 4.2 slice η lands the per-kind row source (system
    /// users + default policies) routed through `DataLoadPlan.build`
    /// — at which point this same `emitFromPlan` body materializes
    /// real content without signature churn.
    let emitFromPlan
        (catalog: Catalog)
        (_profile: Profile)
        (_plan: DataLoadPlan)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        use _ = Bench.scope "emit.bootstrap.emitFromPlan"
        ArtifactByKind.perKind catalog (fun _ -> emptyScript)

    /// Π_Bootstrap emit (composer-facing; hoisted-topo + UserRemap
    /// context). Builds the (currently empty) plan and delegates to
    /// `emitFromPlan`; `UserRemapContext` flows in by signature but
    /// substitution lands at `DataLoadPlan.build` (the canonical
    /// site). The composer's external interface preserves the
    /// existing arity for zero-churn through this slice.
    let emitWithTopo
        (topo: TopologicalOrder)
        (catalog: Catalog)
        (profile: Profile)
        (userRemap: UserRemapContext)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        use _ = Bench.scope "emit.bootstrap.emitWithTopo"
        // Slice ζ MVP: no row source yet (Map.empty). When chapter
        // 4.2 slice η lands the per-kind row source, discover the
        // user kind here and convert UserRemap → SurrogateRemap so
        // the plan-build applies the substitution.
        let _ = userRemap
        let plan = DataLoadPlan.build catalog topo Map.empty SurrogateRemapContext.empty
        emitFromPlan catalog profile plan

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
        // `NotImplementedInV2` status — Bootstrap uses the record-literal
        // form (`RegisteredTransformMetadata.emitter` helper fixes
        // Status = Active per the sibling-emitter-registry-helper
        // arc's "common case" rationale). TransformSite helper still
        // applies for the inner site.
        { Name = "bootstrapEmitter"
          Domain = Data
          StageBinding = Emitter
          Sites =
            [ TransformSite.operatorIntent "userRemapBootstrap" Insertion
                "Slice ζ MVP — Bootstrap's data-publication surface is the operator's `UserRemapContext` (target-environment user identity mapping). The emitter consumes the context but emits no rows yet (chapter 4.2 slice η populates the per-kind row source). OverlayAxis = Insertion when the future emission lands; named here so chapter 4.2's cash-out doesn't need to invent the classification." ]
          Status =
            NotImplementedInV2
                "Slice ζ MVP — Bootstrap emits the empty no-op artifact for every kind today. Chapter 4.2 slice η (UserFkReflowPass emitter integration) lands the per-kind row source; chapter 4.3 (Diagnostics emitters) lands the Profile-evidence-derived row source. The structural hook + composer dispatch land at slice ζ so chapters 4.2 + 4.3 have a fixed insertion point and the slice θ partition assertion is honest (the composer asks Bootstrap for its coverage rather than silently knowing it's empty)." }
