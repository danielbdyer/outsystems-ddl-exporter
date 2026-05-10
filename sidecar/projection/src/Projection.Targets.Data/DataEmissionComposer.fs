namespace Projection.Targets.Data

open Projection.Core
open Projection.Core.Passes

/// Π_Data composer (chapter 4.1.B slice η). The dispatch layer that
/// reads `Policy.Emission.DataComposition` and selects which data
/// emitters fire — preserving A18 amended ("emitters cannot consume
/// `Policy`") at the structural level: each emitter signature
/// literally cannot type-check with a `Policy` parameter; only this
/// composer reads `Policy`, and it routes through emitters that
/// consume the `Catalog × Profile (× boundary-supplied evidence)`
/// shape.
///
/// **Slice η + ε scope.** The composer dispatches through
/// `StaticSeedsEmitter` (slice α/β/δ) and `MigrationDependenciesEmitter`
/// (slice ε). `BootstrapEmitter` (slice ζ) ships next; the composer
/// treats it as a no-op stub today. When ζ lands, the composer adds
/// its dispatch branch without changing the signature.
///
/// **Hoisted `TopologicalOrderPass`.** Per the slice-δ improvement
/// surface (post-commit summary), the composer is the natural home
/// for the pass invocation. Each emitter previously ran the pass
/// internally; now the composer runs it once and threads the result
/// through `emitWithTopo`. The lineage trail of the pass is
/// preserved at the composer surface (returned from `composeWith
/// Lineage`); the convenience `compose` form silently discards the
/// trail (mirroring the pre-composer per-emitter behavior) for
/// callers that don't need it.
///
/// **Per-axis correctness for multi-kind cycles** (the slice-δ open
/// item). Pre-composer, slice δ's per-kind `Rendered` was deploy-
/// correct only for self-FK cycles; multi-kind cycles required
/// global Phase-1-then-Phase-2 ordering across emitters. The
/// composer is where that global ordering would land. **Slice η
/// MVP scope: the global ordering is structurally enabled (every
/// emitter outputs `Phase1Merges + Phase2Updates` separately;
/// composer can interleave) but not yet REIFIED — the per-kind
/// `Rendered` is the only consumer-visible output today.** Slice ε
/// (Migration) is the natural trigger for reifying the global
/// `Phase1 ⨄ Phase2` interleave — at that point a sibling
/// `composeRendered` (or similar) lifts the per-kind text into a
/// pipeline-level concatenation respecting the global phase
/// boundary.
[<RequireQualifiedAccess>]
module DataEmissionComposer =

    [<Literal>]
    let version : int = 1

    /// The composer's three sibling emitter outputs. `StaticSeeds`
    /// is real today; `MigrationDependencies` and `Bootstrap` are
    /// no-op stubs (per slice ordering — slices ε / ζ ship them).
    /// The shape is the natural home for the cross-emitter union
    /// the composer performs at the `union` step.
    type SiblingArtifacts =
        {
            StaticSeeds          : ArtifactByKind<DataInsertScript>
            MigrationDependencies : ArtifactByKind<DataInsertScript>
            Bootstrap            : ArtifactByKind<DataInsertScript>
        }

    /// Empty `DataInsertScript` — the no-op shape every emitter
    /// emits for kinds it doesn't own. Centralized so future
    /// emitters that ship slice ε / ζ get the same neutral form.
    let private emptyScript : DataInsertScript =
        { Phase1Merges = []; Phase2Updates = []; Rendered = "" }

    /// Build a no-op `ArtifactByKind<DataInsertScript>` keyed by
    /// every catalog kind — every kind maps to `emptyScript`. Used
    /// by slices ε / ζ stubs and by `AllExceptStatic`-skipped
    /// branches. Per T11 strict-equality keyset: every kind is
    /// keyed; no kind is silently absent.
    let private emptyArtifact (catalog: Catalog) : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        let allKinds = Catalog.allKinds catalog
        let slices =
            allKinds
            |> List.map (fun k -> k.SsKey, emptyScript)
            |> Map.ofList
        ArtifactByKind.create catalog slices

    /// Run the three sibling emitters per the policy's data-
    /// composition variant. Per pre-scope §3.4 + §3.2:
    ///   - `AllRemaining`     → Static fires; Migration fires;
    ///                          Bootstrap fires (stub today).
    ///   - `AllExceptStatic`  → Static skipped; Migration fires;
    ///                          Bootstrap fires (stub today).
    ///   - `AllData`          → Static fires; Migration skipped;
    ///                          Bootstrap fires for everything (stub
    ///                          today).
    ///
    /// `EmitData = false` short-circuits before this function — the
    /// caller emits nothing on the data axis and never invokes
    /// `compose`.
    let private dispatchSiblings
        (composition: DataComposition)
        (topo: TopologicalOrder)
        (catalog: Catalog)
        (profile: Profile)
        (migration: MigrationDependencyContext)
        : Result<SiblingArtifacts, EmitError> =
        use _ = Bench.scope "compose.data.dispatchSiblings"
        let staticSeeds =
            match composition with
            | AllRemaining
            | AllData         -> StaticSeedsEmitter.emitWithTopo topo catalog profile
            | AllExceptStatic -> emptyArtifact catalog
        let migrationDependencies =
            match composition with
            | AllRemaining
            | AllExceptStatic -> MigrationDependenciesEmitter.emitWithTopo topo catalog profile migration
            | AllData         -> emptyArtifact catalog
        let bootstrap = emptyArtifact catalog              // slice ζ pending
        match staticSeeds, migrationDependencies, bootstrap with
        | Ok s, Ok m, Ok b ->
            Ok { StaticSeeds = s; MigrationDependencies = m; Bootstrap = b }
        | Error e, _, _
        | _, Error e, _
        | _, _, Error e -> Error e

    /// Union the three sibling artifacts into one
    /// `ArtifactByKind<DataInsertScript>` keyed by every catalog
    /// kind. **Slice η scope:** since Migration and Bootstrap are
    /// no-op stubs today, the union reduces to "use Static's value
    /// if non-empty, else neutral." When ε/ζ land, this becomes the
    /// per-kind partition assertion (per pre-scope §5.3 +
    /// `EmitError.OverlappingEmitterCoverage`); slice θ ships that.
    /// For now the union is a left-biased fold favoring
    /// `StaticSeeds`.
    let private unionSiblings
        (catalog: Catalog)
        (siblings: SiblingArtifacts)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        use _ = Bench.scope "compose.data.unionSiblings"
        let staticMap = ArtifactByKind.toMap siblings.StaticSeeds
        let migrationMap = ArtifactByKind.toMap siblings.MigrationDependencies
        let bootstrapMap = ArtifactByKind.toMap siblings.Bootstrap
        let pickFirstNonEmpty (k: SsKey) : DataInsertScript =
            // Left-biased: Static → Migration → Bootstrap → empty.
            // Slice θ replaces this with overlap-detection + a
            // partition assertion (`EmitError.OverlappingEmitter
            // Coverage of (SsKey * EmitterName list)`).
            let isPopulated (s: DataInsertScript) : bool =
                not (List.isEmpty s.Phase1Merges)
            match Map.tryFind k staticMap with
            | Some s when isPopulated s -> s
            | _ ->
                match Map.tryFind k migrationMap with
                | Some s when isPopulated s -> s
                | _ ->
                    match Map.tryFind k bootstrapMap with
                    | Some s -> s
                    | None   -> emptyScript
        let allKinds = Catalog.allKinds catalog
        let slices =
            allKinds
            |> List.map (fun k -> k.SsKey, pickFirstNonEmpty k.SsKey)
            |> Map.ofList
        ArtifactByKind.create catalog slices

    /// Π_Data compose with explicit lineage propagation. Hoists the
    /// `TopologicalOrderPass` invocation; threads the resulting
    /// `Lineage<TopologicalOrder>` through to the result so pipeline
    /// callers preserve trail fidelity (writer-fidelity discipline:
    /// the topo pass produces decisions; those decisions belong in
    /// the trail). Returns `Lineage<Result<...>>` rather than
    /// `Result<Lineage<...>>` to keep the trail visible even on
    /// error.
    let composeWithLineage
        (policy: Policy)
        (catalog: Catalog)
        (profile: Profile)
        (migration: MigrationDependencyContext)
        : Lineage<Result<ArtifactByKind<DataInsertScript>, EmitError>> =
        use _ = Bench.scope "compose.data.composeWithLineage"
        let topoLineage = TopologicalOrderPass.runWith TreatAsCycle catalog
        let topo = topoLineage.Value
        let composition = policy.Emission.DataComposition
        // Match-bind explicitly: dispatchSiblings + unionSiblings
        // both return `Result<_, EmitError>` (the BCL two-parameter
        // Result), distinct from V2's `Result<'a> = Result<'a,
        // ValidationError list>` alias whose bind is in scope.
        let result =
            match dispatchSiblings composition topo catalog profile migration with
            | Ok siblings -> unionSiblings catalog siblings
            | Error e     -> Error e
        topoLineage |> Lineage.map (fun _ -> result)

    /// Π_Data compose. Convenience wrapper around `composeWith
    /// Lineage` for callers that don't need the topo lineage trail
    /// (canary tests, direct-Π verification). The trail is silently
    /// discarded — pipeline-level callers SHOULD route through
    /// `composeWithLineage` to preserve trail fidelity.
    ///
    /// Default `migration = MigrationDependencyContext.empty` —
    /// callers that don't have a migration channel configured
    /// (the dominant case at chapter 4.1.B slice ε since the
    /// ingestion adapter is deferred) get an empty Migration
    /// contribution.
    let compose
        (policy: Policy)
        (catalog: Catalog)
        (profile: Profile)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        (composeWithLineage policy catalog profile MigrationDependencyContext.empty).Value

    /// `compose` variant accepting an explicit `MigrationDependency
    /// Context`. For callers (canary tests, future pipeline
    /// integration) that supply migration rows programmatically.
    let composeWithMigration
        (policy: Policy)
        (catalog: Catalog)
        (profile: Profile)
        (migration: MigrationDependencyContext)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        (composeWithLineage policy catalog profile migration).Value
