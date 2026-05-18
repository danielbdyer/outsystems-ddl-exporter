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
        { Phase1Merges   = []
          Phase2Updates  = []
          RenderedPhase1 = ""
          RenderedPhase2 = ""
          Rendered       = "" }

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
            | AllExceptStatic -> MigrationDependenciesEmitter.emitWithTopo topo catalog profile migration userRemap
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
    /// Full-arity compose accepting both Migration and UserRemap
    /// contexts explicitly. The pipeline-integration entry point
    /// chapter 4.2 (UserFkReflowPass) wires up. Callers without a
    /// UserRemapContext pass `UserRemapContext.empty` explicitly
    /// (chapter 4.7 cleanup: the prior `composeWithMigration` middle-
    /// tier wrapper retired as overdifferentiated — three sibling
    /// surfaces defaulting different axis subsets is the anti-pattern;
    /// the explicit default makes the caller's choice visible).
    let composeFull
        (policy: Policy)
        (catalog: Catalog)
        (profile: Profile)
        (migration: MigrationDependencyContext)
        (userRemap: UserRemapContext)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        (composeWithLineage policy catalog profile migration userRemap).Value

    /// Π_Data compose-rendered (chapter 4.1.B slice ι; multi-kind
    /// cycle global-phase reification). Produces a single
    /// globally-ordered GO-batched T-SQL string where ALL Phase-1
    /// MERGEs across ALL kinds (in topological order) precede ANY
    /// Phase-2 UPDATE — the structural cash-out of the slice-δ
    /// improvement surface item #2.
    ///
    /// **Why per-kind `Rendered` is insufficient for multi-kind
    /// cycles.** A 2-cycle (kind A ↔ kind B with nullable FKs) has
    /// per-kind `Rendered = MERGE_A; UPDATE_A` (Phase-1 + Phase-2
    /// concatenated kind-locally). Deploying the per-kind output
    /// in isolation fails: `UPDATE_A`'s WHERE references B's row
    /// that doesn't exist yet. The cycle-correct deploy order is
    /// `MERGE_A; MERGE_B; UPDATE_A; UPDATE_B` — ALL Phase-1
    /// across both kinds, THEN ALL Phase-2.
    ///
    /// **How `composeRendered` reifies it.** Slice ι's
    /// `RenderedPhase1` / `RenderedPhase2` split at the per-kind
    /// level lets the composer concatenate Phase-1 across all
    /// kinds (in topological order from the hoisted topo pass)
    /// then Phase-2 across all kinds (same order). Self-FK kinds
    /// continue to deploy correctly under per-kind `Rendered`
    /// (the kind-local Phase-1 + Phase-2 IS the deploy order for
    /// a 1-node SCC); multi-kind cycles require this global view.
    ///
    /// Full-arity form. The `composeRendered` convenience defaults
    /// both contexts to empty.
    let composeRenderedFull
        (policy: Policy)
        (catalog: Catalog)
        (profile: Profile)
        (migration: MigrationDependencyContext)
        (userRemap: UserRemapContext)
        : Result<string, EmitError> =
        use _ = Bench.scope "compose.data.composeRendered"
        // Hoist the topo pass once; both the composer and the
        // global-phase rendering consume the same ordering.
        let topoLineage = TopologicalOrderPass.runWith TreatAsCycle catalog
        let topo = topoLineage.Value
        let composition = policy.Emission.DataComposition
        match dispatchSiblings composition topo catalog profile migration userRemap with
        | Error e -> Error e
        | Ok siblings ->
            match unionSiblings catalog siblings with
            | Error e   -> Error e
            | Ok artifact ->
                let map = ArtifactByKind.toMap artifact
                // Walk kinds in topological order so Phase-1 MERGEs
                // honor FK precedence at the global level
                // (cycle-broken kinds appear in topo.Order via
                // `applyResolver`'s reduced-graph re-Kahn). Same
                // order applies to Phase-2 UPDATEs (target rows
                // exist by then; ordering is for diagnostic
                // determinism rather than correctness).
                let phase1Texts =
                    topo.Order
                    |> List.choose (fun k ->
                        Map.tryFind k map
                        |> Option.map (fun s -> s.RenderedPhase1))
                let phase2Texts =
                    topo.Order
                    |> List.choose (fun k ->
                        Map.tryFind k map
                        |> Option.map (fun s -> s.RenderedPhase2))
                let allText =
                    Seq.append phase1Texts phase2Texts
                    |> System.String.Concat  // LINT-ALLOW: terminal global Phase-1-then-Phase-2 concatenation across all kinds in topological order (chapter 4.1.B slice ι); each segment is the per-kind ScriptDom-rendered RenderedPhase1 / RenderedPhase2 string already terminated by `;\nGO\n`; BCL `String.Concat(IEnumerable<string>)` is the right primitive at this terminal-text boundary; preserves the global cycle-correct deploy order that per-kind Rendered cannot express
                Ok allText

    /// Π_Data compose-rendered (canary-test convenience). Defaults
    /// migration + userRemap to empty.
    let composeRendered
        (policy: Policy)
        (catalog: Catalog)
        (profile: Profile)
        : Result<string, EmitError> =
        composeRenderedFull
            policy catalog profile
            MigrationDependencyContext.empty
            UserRemapContext.empty

    /// Harvest-discipline classification per pillar 9 (chapter 5.13
    /// slice data-emission-registry). Five sites covering the
    /// composer's surfaces; classifies which inputs are
    /// data-intention vs operator-intention so the skeleton-purity
    /// property test asserts `composeRendered policy catalog
    /// Profile.empty` with `policy = Policy.empty + empty contexts`
    /// is reachable as the DataIntent baseline.
    ///
    /// **Stage binding = Pipeline.** The composer orchestrates three
    /// sibling-Π emitters; it is the data-axis dispatch layer, not
    /// itself an emitter producing artifacts directly. Per
    /// `TransformRegistry.fs` StageBinding docstring: "Pipeline —
    /// `Compose`-level transformations."
    let registeredMetadata : RegisteredTransformMetadata =
        { Name = "dataEmissionComposer"
          Domain = Data
          StageBinding = Pipeline
          Sites =
            [ { SiteName = "compositionDispatch"
                Classification = OperatorIntent Emission
                Rationale = "Reads `Policy.Emission.DataComposition` (closed DU `AllRemaining \| AllExceptStatic \| AllData`) and dispatches which sibling emitters fire. This is the canonical site where operator intent enters the data-emission pipeline — the composer is the only data-axis surface that consumes `Policy`. OverlayAxis = Emission (chooses what physical form a kind takes in emitted output)." }
              { SiteName = "migrationContextThreading"
                Classification = OperatorIntent Insertion
                Rationale = "Threads the operator-supplied `MigrationDependencyContext` to `MigrationDependenciesEmitter`. The context's row inventory is operator-published evidence (pre-scope §2.2); the composer is the routing layer. OverlayAxis = Insertion." }
              { SiteName = "userRemapContextThreading"
                Classification = OperatorIntent Insertion
                Rationale = "Threads the operator-supplied `UserRemapContext` to `MigrationDependenciesEmitter` + `BootstrapEmitter`. The remap mapping is operator-supplied (chapter 4.2 slice γ); the composer is the routing layer. OverlayAxis = Insertion." }
              { SiteName = "globalPhaseOrdering"
                Classification = DataIntent
                Rationale = "Slice ι cash-out — concatenate ALL Phase-1 MERGEs (across all kinds + all emitters, in topological order) before ANY Phase-2 UPDATE. The ordering is structural (deploy-correctness for multi-kind FK cycles); no operator opinion enters. DataIntent — the topology is the source of truth." }
              { SiteName = "partitionAssertion"
                Classification = DataIntent
                Rationale = "Slice θ cash-out — every kind's populated coverage comes from at most one sibling emitter under a given `DataComposition`; overlap surfaces as `EmitError.OverlappingEmitterCoverage`. The partition check is structural fidelity (not configurable); fires deterministically on first overlap in catalog order." } ]
          Status = Active }
