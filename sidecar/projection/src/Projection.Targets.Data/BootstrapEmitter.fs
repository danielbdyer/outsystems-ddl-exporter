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

    /// Discover the user kind's `SsKey` from the catalog by scanning for
    /// any reference flagged `IsUserFk` — its `TargetKind` names the
    /// platform user kind. Mirrors `MigrationDependenciesEmitter
    /// .tryDiscoverUserKind` (the two data-publication emitters that
    /// thread a `UserRemapContext` share the discovery shape; kept local
    /// per the deliberate per-emitter parallelism). `None` when no such
    /// reference exists (the dominant case until the OSSYS adapter's
    /// IsUserFk detection lands).
    let private tryDiscoverUserKind (catalog: Catalog) : SsKey option =
        Catalog.allKinds catalog
        |> List.tryPick (fun k ->
            k.References
            |> List.tryPick (fun r -> if r.IsUserFk then Some r.TargetKind else None))

    /// Π_Bootstrap emit (canonical; plan-consuming). WP6 step 2
    /// (DECISIONS 2026-06-13) — Bootstrap's renderer IS the static-seeds
    /// MERGE renderer: both emitters realize the same algebra over the
    /// same `DataLoadPlan` (A40), so this delegates to
    /// `StaticSeedsEmitter.emitFromPlanWith`. Bootstrap is the
    /// remaining-kinds upsert lane (system users, default policies, and
    /// kinds not owned by StaticSeeds or MigrationDependencies); no
    /// delete scope on this lane. An empty plan (the case until the WP6
    /// hydration step grafts the per-kind row source) renders empty per
    /// kind, byte-identical to the prior stub; a populated plan renders
    /// its MERGEs (IDENTITY_INSERT-bracketed for `AssignedBySink` kinds,
    /// via the shared renderer).
    let emitFromPlan
        (opts: DataEmitOptions)
        (catalog: Catalog)
        (profile: Profile)
        (plan: DataLoadPlan)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        use _ = Bench.scope "emit.bootstrap.emitFromPlan"
        // No delete scope on the additive bootstrap lane (suppress it regardless
        // of `opts.DeleteScope`); the rest of the operator's posture rides along.
        StaticSeedsEmitter.emitFromPlan (DataEmitOptions.withDeleteScope None opts) catalog profile plan

    /// Convert the operator's `UserRemapContext` to a `SurrogateRemapContext`
    /// keyed under the catalog-discovered user kind (the same route
    /// `MigrationDependenciesEmitter.buildPlan` takes). No user kind / a failed
    /// conversion ⇒ the empty remap (the dominant case until OSSYS IsUserFk
    /// detection lands).
    let private resolveRemap (catalog: Catalog) (userRemap: UserRemapContext) : SurrogateRemapContext =
        match tryDiscoverUserKind catalog with
        | Some userKindKey ->
            match UserRemapContext.toSurrogate userKindKey userRemap with
            | Ok r    -> r
            | Error _ -> SurrogateRemapContext.empty
        | None -> SurrogateRemapContext.empty

    /// Π_Bootstrap emit (composer-facing; hoisted-topo + UserRemap + the
    /// hydrated row source + NM-73 verification). Converts the `UserRemap
    /// Context`, builds the plan over `bootstrapRows`, and delegates to the
    /// shared static-seeds renderer (A40 — same algebra over the same
    /// `DataLoadPlan`). The row source is what the pipeline's hydration step
    /// streamed for the bootstrap lane: every data-bearing kind under
    /// `AllData`, the complement of (Static-populated ∪ Migration-context)
    /// under `AllRemaining`; empty until hydrated (byte-stable empty per kind).
    /// `verification` threads the operator's NM-73 drift-guard posture; the
    /// guard is enabled on this lane (operator decision 2026-06-14). Bootstrap
    /// carries **no delete scope** (`None`) — it is the additive upsert lane.
    /// **Partition law:** the hydrated row source MUST be the complement of
    /// (Static-populated ∪ Migration-context) kinds whenever those lanes also
    /// fire (i.e. under `AllRemaining`/`AllExceptStatic`) — feeding Bootstrap a
    /// kind another active lane also populates trips the composer's
    /// `OverlappingEmitterCoverage` assertion (a production `invalidOp`). Under
    /// `AllData` the sibling lanes are dispatched empty, so Bootstrap covers
    /// every kind without overlap.
    /// Π_Bootstrap emit (composer-facing; hoisted topo + UserRemap + the
    /// hydrated row source). Converts the `UserRemapContext`, builds the plan
    /// over `bootstrapRows`, and delegates to the shared static-seeds renderer
    /// under the operator's `DataEmitOptions` (A40 — same algebra, same
    /// `DataLoadPlan`). The bootstrap lane is the FIRST-DEPLOY full-snapshot
    /// lane (V1's `AllEntitiesIncludingStatic`) — the lane MOST likely to carry
    /// a kind past the 8623 wall — so `opts.Staging` MUST reach it too. The
    /// drift-guard posture rides this lane (operator decision 2026-06-14).
    /// Bootstrap carries **no delete scope** — it is the additive upsert lane —
    /// so the delete arm is suppressed regardless of `opts.DeleteScope`.
    /// **Partition law:** the hydrated row source MUST be the complement of
    /// (Static-populated ∪ Migration-context) kinds whenever those lanes also
    /// fire, or the composer's `OverlappingEmitterCoverage` assertion fires.
    let emitWithTopo
        (opts: DataEmitOptions)
        (topo: TopologicalOrder)
        (catalog: Catalog)
        (profile: Profile)
        (bootstrapRows: Map<SsKey, StaticRow list>)
        (userRemap: UserRemapContext)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        use _ = Bench.scope "emit.bootstrap.emitWithTopo"
        let remap = resolveRemap catalog userRemap
        let plan = DataLoadPlan.build catalog topo bootstrapRows remap
        StaticSeedsEmitter.emitFromPlan (DataEmitOptions.withDeleteScope None opts) catalog profile plan

    /// Π_Bootstrap emit (standalone). Convenience for callers that don't go
    /// through the `DataEmissionComposer`; empty row source (byte-identical to
    /// the slice-ζ stub shape). Pass `DataEmitOptions.defaults` for the default
    /// posture.
    let emit
        (opts: DataEmitOptions)
        (catalog: Catalog)
        (profile: Profile)
        (userRemap: UserRemapContext)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        use _ = Bench.scope "emit.bootstrap.emit"
        let topo = (TopologicalOrderPass.runWith TreatAsCycle catalog).Value
        emitWithTopo opts topo catalog profile Map.empty userRemap

    /// Harvest-discipline classification per pillar 9 (chapter 5.13
    /// slice data-emission-registry).
    ///
    /// **Status = Active (WP6 step 2, DECISIONS 2026-06-13).** Bootstrap
    /// is implemented: `emitFromPlan` delegates to the static-seeds MERGE
    /// renderer (A40 — same algebra over the same `DataLoadPlan`), so it
    /// renders whatever plan it is handed (an empty plan renders empty per
    /// kind — `MigrationDependenciesEmitter` is likewise Active and emits
    /// nothing without a context). The DataIntent `bootstrapRowsProjection`
    /// site is the plan-rendering surface; the OperatorIntent
    /// `userRemapBootstrap` site is the `UserRemapContext` threading
    /// (`emitWithTopo` converts it via `UserRemapContext.toSurrogate`). The
    /// per-kind row source is grafted by the WP6 hydration step.
    let registeredMetadata : RegisteredTransformMetadata =
        RegisteredTransformMetadata.emitter "bootstrapEmitter" Data
            [ TransformSite.dataIntent "bootstrapRowsProjection"
                "Render MERGE statements for the supplied DataLoadPlan's loads — the remaining-kinds lane (system users, default policies, and any kind not owned by StaticSeeds or MigrationDependencies). Delegates to StaticSeedsEmitter.emitFromPlanWith; pure projection of the post-substitution plan (identity substitution landed once at DataLoadPlan.build). DataIntent. The per-kind row source is grafted by the WP6 hydration step; an empty plan renders empty per kind (T11 keyset preserved). The populated coverage MUST be the complement of (Static ∪ Migration) kinds, or the composer's OverlappingEmitterCoverage partition assertion fires."
              TransformSite.operatorIntent "userRemapBootstrap" Insertion
                "Bootstrap's data-publication surface threads the operator's `UserRemapContext` (target-environment user identity mapping) into `DataLoadPlan.build` via `UserRemapContext.toSurrogate`, keyed under the catalog-discovered user kind — the same conversion `MigrationDependenciesEmitter.buildPlan` performs. OverlayAxis = Insertion." ]
