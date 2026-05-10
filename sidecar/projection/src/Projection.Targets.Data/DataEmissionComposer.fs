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
/// **Slice η + ε + ζ + θ scope.** The composer dispatches through
/// the three sibling-Π data emitters: `StaticSeedsEmitter` (slice
/// α/β/δ), `MigrationDependenciesEmitter` (slice ε), and
/// `BootstrapEmitter` (slice ζ; structural stub pending chapters
/// 4.2 / 4.3 row-source consumers). Slice θ adds the partition
/// assertion: every kind's populated coverage comes from at most one
/// emitter under a given `DataComposition`; overlap surfaces as
/// `EmitError.OverlappingEmitterCoverage` rather than the prior
/// left-biased silent precedence.
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
        (userRemap: UserRemapContext)
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
        let bootstrap =
            BootstrapEmitter.emitWithTopo topo catalog profile userRemap
        match staticSeeds, migrationDependencies, bootstrap with
        | Ok s, Ok m, Ok b ->
            Ok { StaticSeeds = s; MigrationDependencies = m; Bootstrap = b }
        | Error e, _, _
        | _, Error e, _
        | _, _, Error e -> Error e

    /// Slice θ partition assertion (chapter 4.1.B). Per pre-scope
    /// §5.3: every kind's populated coverage must come from at most
    /// one sibling emitter under a given `DataComposition`. Two
    /// emitters both claiming the same kind under the same
    /// composition is a configuration-level mismatch (e.g., a kind
    /// appearing both in `Modality.Static` AND in the migration
    /// team's pickup channel under `AllRemaining`); the operator
    /// needs the diagnostic to surface, not silent left-biased
    /// precedence. Returns `EmitError.OverlappingEmitterCoverage
    /// (SsKey, [emitter names])` on the first overlap encountered;
    /// returns the union artifact on partition success.
    let private unionSiblings
        (catalog: Catalog)
        (siblings: SiblingArtifacts)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        use _ = Bench.scope "compose.data.unionSiblings"
        let staticMap = ArtifactByKind.toMap siblings.StaticSeeds
        let migrationMap = ArtifactByKind.toMap siblings.MigrationDependencies
        let bootstrapMap = ArtifactByKind.toMap siblings.Bootstrap
        let isPopulated (s: DataInsertScript) : bool =
            not (List.isEmpty s.Phase1Merges)
        // Per-kind coverage assertion. Each sibling either populated
        // the kind (script with non-empty `Phase1Merges`) or didn't.
        // The partition holds when at most one sibling populated it.
        let coverageOf (k: SsKey) : (string * DataInsertScript) list =
            [ "StaticSeeds",          Map.tryFind k staticMap
              "MigrationDependencies", Map.tryFind k migrationMap
              "Bootstrap",            Map.tryFind k bootstrapMap ]
            |> List.choose (fun (name, opt) ->
                match opt with
                | Some s when isPopulated s -> Some (name, s)
                | _                         -> None)
        let allKinds = Catalog.allKinds catalog
        // Walk kinds in catalog order; first overlap wins (deterministic
        // diagnostic: same input → same kind reported).
        let resolveKind (k: SsKey) : Result<SsKey * DataInsertScript, EmitError> =
            match coverageOf k with
            | []          -> Ok (k, emptyScript)
            | [ (_, s) ]  -> Ok (k, s)
            | overlaps    ->
                let names = overlaps |> List.map fst
                Error (OverlappingEmitterCoverage (k, names))
        let folder
            (acc: Result<(SsKey * DataInsertScript) list, EmitError>)
            (k: Kind)
            : Result<(SsKey * DataInsertScript) list, EmitError> =
            match acc with
            | Error _ as err -> err
            | Ok rows ->
                match resolveKind k.SsKey with
                | Ok row    -> Ok (row :: rows)
                | Error err -> Error err
        match List.fold folder (Ok []) allKinds with
        | Error err -> Error err
        | Ok rows ->
            let slices = rows |> List.rev |> Map.ofList
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
        (userRemap: UserRemapContext)
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
            match dispatchSiblings composition topo catalog profile migration userRemap with
            | Ok siblings -> unionSiblings catalog siblings
            | Error e     -> Error e
        topoLineage |> Lineage.map (fun _ -> result)

    /// Π_Data compose. Convenience wrapper around `composeWith
    /// Lineage` for callers that don't need the topo lineage trail
    /// (canary tests, direct-Π verification). The trail is silently
    /// discarded — pipeline-level callers SHOULD route through
    /// `composeWithLineage` to preserve trail fidelity.
    ///
    /// Defaults `migration = MigrationDependencyContext.empty` and
    /// `userRemap = UserRemapContext.empty` for callers in the
    /// dominant slice η + ε + ζ MVP shape (no migration channel
    /// configured; chapter 4.2 ships the populated remap).
    let compose
        (policy: Policy)
        (catalog: Catalog)
        (profile: Profile)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        (composeWithLineage
            policy catalog profile
            MigrationDependencyContext.empty
            UserRemapContext.empty).Value

    /// `compose` variant accepting an explicit `MigrationDependency
    /// Context`. For callers (canary tests, future pipeline
    /// integration) that supply migration rows programmatically.
    /// `userRemap` defaults to `UserRemapContext.empty`.
    let composeWithMigration
        (policy: Policy)
        (catalog: Catalog)
        (profile: Profile)
        (migration: MigrationDependencyContext)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        (composeWithLineage
            policy catalog profile migration UserRemapContext.empty).Value

    /// Full-arity compose accepting both Migration and UserRemap
    /// contexts explicitly. The pipeline-integration entry point
    /// chapter 4.2 (UserFkReflowPass) wires up.
    let composeFull
        (policy: Policy)
        (catalog: Catalog)
        (profile: Profile)
        (migration: MigrationDependencyContext)
        (userRemap: UserRemapContext)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        (composeWithLineage policy catalog profile migration userRemap).Value
