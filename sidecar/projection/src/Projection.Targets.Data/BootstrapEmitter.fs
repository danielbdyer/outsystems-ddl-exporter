namespace Projection.Targets.Data

open Projection.Core
open Projection.Core.Passes

// ---------------------------------------------------------------------------
// UserRemapContext — placeholder shape for chapter 4.2's UserFkReflowPass
// (per pre-scope §2.3). The Bootstrap emitter consumes it at row-emission
// time to rewrite `CreatedBy` / `UpdatedBy` columns environment-by-
// environment.
//
// Per pre-scope §2.3: "Until chapter 4.2 lands, the BootstrapEmitter slice
// ships with `UserRemapContext = Map.empty` as a pass-through (no
// rewrites)." This minimal shape lets the composer flow `Catalog × Profile
// × UserRemapContext` through to the emitter without a Bootstrap-specific
// hole; chapter 4.2 supplies the real `Map<SsKey, Map<int64, int64>>` that
// drives per-environment User identity remapping.
//
// `SourceUserId` and `TargetUserId` are intentionally `int64` aliases for
// the slice ζ MVP — chapter 4.2's `UserMatchingStrategy` may refine them
// to typed value objects (per the chapter open's strategic frame) when
// the matching strategies surface real type-safety pressure.
// ---------------------------------------------------------------------------

/// Per-kind User-FK remap. Keyed by the kind's SsKey; inner map keyed
/// by source-environment User id → target-environment User id. Empty
/// outer map = no remap (the slice ζ MVP default; chapter 4.2 ships
/// the populated form).
type UserRemapContext = Map<SsKey, Map<int64, int64>>

[<RequireQualifiedAccess>]
module UserRemapContext =

    /// The empty remap context — every kind unaltered. Per pre-scope
    /// §2.3: this is the slice ζ MVP default (chapter 4.2 lands the
    /// real per-environment remap).
    let empty : UserRemapContext = Map.empty

    /// Look up the remap for a given kind. Returns `None` if no
    /// per-kind entry exists; `Some Map.empty` for an explicitly
    /// empty entry (different from absence per chapter 4.2's
    /// distinction-pending semantics).
    let tryFindKindRemap (kindKey: SsKey) (ctx: UserRemapContext) : Map<int64, int64> option =
        Map.tryFind kindKey ctx

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
    let private emptyScript : DataInsertScript =
        { Phase1Merges = []; Phase2Updates = []; Rendered = "" }

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
