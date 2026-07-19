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
/// `BootstrapEmitter` (slice ζ, activated at WP6 step 2 — DECISIONS
/// 2026-06-13 — once the pipeline's hydration step grafts its
/// per-kind row source; see `BootstrapEmitter.registeredMetadata`'s
/// "Status = Active"). Slice θ adds the partition assertion: every
/// kind's populated coverage comes from at most one emitter under a
/// given `DataComposition`; overlap surfaces as
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
/// item, reified at slice ι). Pre-composer, slice δ's per-kind
/// `Rendered` was deploy-correct only for self-FK cycles; multi-kind
/// cycles required global Phase-1-then-Phase-2 ordering across
/// emitters. **Slice η enabled it structurally** (every emitter
/// outputs `Phase1Merges + Phase2Updates` separately); **slice ι
/// reifies it** — `composeRendered` / `composeRenderedFull` (below,
/// ~line 351) concatenate Phase-1 across all kinds in topological
/// order, then Phase-2 across all kinds, into one globally-ordered
/// GO-batched string. The per-kind `Rendered` stays available for
/// callers that only need self-FK-correct output; `composeRendered`
/// is the multi-kind-cycle-correct surface.
[<RequireQualifiedAccess>]
module DataEmissionComposer =

    [<Literal>]
    let version : int = 1

    /// The composer's three sibling emitter outputs. All three are
    /// real emitters today (`MigrationDependenciesEmitter` since
    /// slice ε; `BootstrapEmitter` since WP6 step 2 — see the module
    /// doc above); each renders empty per kind only when its own
    /// context/plan is empty, not because the emitter is a stub.
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
        ArtifactByKind.perKind catalog (fun _ -> emptyScript)

    /// The Bootstrap lane's row source, in one of two currencies.
    /// `Rows` is the established shape: the hydration step's drained
    /// `Map<SsKey, StaticRow list>`, rendered HERE via
    /// `BootstrapEmitter.emitWithTopo` (plan-build + MERGE render at
    /// compose time). `Prerendered` is the pipelined shape: each
    /// eligible kind's `DataInsertScript` was rendered AT DRAIN TIME
    /// (as the kind's rows landed, before the reader moved on), so the
    /// composer only assembles — the per-kind render cost has already
    /// been paid, overlapped with acquisition I/O. The two arms MUST
    /// produce identical artifacts for the same underlying rows: the
    /// drain-time renderer is the same `StaticSeedsEmitter.renderLoad`
    /// over the same `DataLoadPlan.loadFor` core `emitWithTopo`'s
    /// batch build folds, under the same delete-scope-suppressed
    /// options (pinned by the pipelined-bootstrap equivalence test).
    /// Prerendered scripts keep their `Phase1Merges` rows populated so
    /// the `unionSiblings` partition assertion sees real coverage.
    [<RequireQualifiedAccess>]
    type BootstrapLane =
        | Rows of Map<SsKey, StaticRow list>
        | Prerendered of Map<SsKey, DataInsertScript>

    /// Run the three sibling emitters per the policy's data-
    /// composition variant. Per pre-scope §3.4 + §3.2:
    ///   - `AllRemaining`     → Static fires; Migration fires;
    ///                          Bootstrap fires.
    ///   - `AllExceptStatic`  → Static skipped; Migration fires;
    ///                          Bootstrap fires.
    ///   - `AllData`          → Static fires; Migration skipped;
    ///                          Bootstrap fires for everything.
    ///
    /// `EmitData = false` short-circuits before this function — the
    /// caller emits nothing on the data axis and never invokes
    /// `compose`.
    let private dispatchSiblings
        (composition: DataComposition)
        (opts: DataEmitOptions)
        (topo: TopologicalOrder)
        (catalog: Catalog)
        (profile: Profile)
        (migration: MigrationDependencyContext)
        (bootstrapLane: BootstrapLane)
        (userRemap: UserRemapContext)
        : Result<SiblingArtifacts, EmitError> =
        use _ = Bench.scope "compose.data.dispatchSiblings"
        // The operator's optional emission axes (delete scope / drift guard /
        // staging) ride to every lane in ONE `DataEmitOptions` — the composer
        // is the single site that lifts them from `EmissionPolicy`. Bootstrap
        // suppresses the delete arm internally (the additive upsert lane); the
        // migration lane consumes verification + delete-scope and ignores
        // staging (no staged path there yet).
        let staticSeeds =
            use _ = Bench.scope "compose.data.dispatchSiblings.staticSeeds"
            match composition with
            // `AllData` means Bootstrap covers EVERYTHING (static included), so
            // Static is skipped — else both lanes claim the static kinds and the
            // partition law (`unionSiblings`) trips once Bootstrap is populated
            // (the slice-ζ differentiation the prior MVP test anticipated).
            | AllRemaining    -> StaticSeedsEmitter.emitWithTopo opts topo catalog profile
            | AllExceptStatic
            | AllData         -> emptyArtifact catalog
        let migrationDependencies =
            use _ = Bench.scope "compose.data.dispatchSiblings.migrationDeps"
            match composition with
            | AllRemaining
            | AllExceptStatic -> MigrationDependenciesEmitter.emitWithTopo opts topo catalog profile migration userRemap
            | AllData         -> emptyArtifact catalog
        let bootstrap =
            use _ = Bench.scope "compose.data.dispatchSiblings.bootstrap"
            match bootstrapLane with
            | BootstrapLane.Rows bootstrapRows ->
                BootstrapEmitter.emitWithTopo opts topo catalog profile bootstrapRows userRemap
            | BootstrapLane.Prerendered scripts ->
                // Drain-time-rendered scripts: assemble only. Kinds absent
                // from the map were not bootstrap-eligible — they take the
                // same `emptyScript` the batch build's empty-rows loads
                // render to (T11 keyset preserved either way).
                ArtifactByKind.perKind catalog (fun k ->
                    Map.tryFind k.SsKey scripts |> Option.defaultValue emptyScript)
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
        // AC-D7 — the operator's convergent-delete scope (OperatorIntent of
        // Emission). The composer resolves it OFF `Policy` here and threads
        // the plain value; the emitters never see `Policy` (A18 amended).
        let opts = DataEmitOptions.ofEmissionPolicy policy.Emission
        // Match-bind explicitly: dispatchSiblings + unionSiblings
        // both return `Result<_, EmitError>` (the BCL two-parameter
        // Result), distinct from V2's `Result<'a> = Result<'a,
        // ValidationError list>` alias whose bind is in scope.
        let result =
            match dispatchSiblings composition opts topo catalog profile migration (BootstrapLane.Rows Map.empty) userRemap with
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
    /// Render one `ArtifactByKind` as a globally-ordered GO-batched T-SQL
    /// string: ALL Phase-1 MERGEs across all kinds (in topological order)
    /// then ALL Phase-2 UPDATEs (same order) — the slice-ι global-phase
    /// shape. Walking `topo.Order` honors FK precedence for Phase-1; Phase-2
    /// targets exist by then (the order is for diagnostic determinism). The
    /// single render site for both the fused union and each per-lane sibling
    /// (WP6 step 3) — they differ only in WHICH artifact is walked, never in
    /// HOW, so the per-lane outputs are byte-faithful slices of the same
    /// per-kind strings, re-rendered nowhere.
    /// Lift the operator's `EmissionPolicy.RenderDataElegant` bool to the data
    /// formatter's typed `Mode` (Core's bool → the target's typed mode; the
    /// emitter never reads `Policy`, A18 amended — the composer is the lift site,
    /// mirroring `ConstraintFormatter` at the SSDT seam).
    let private dataFormatMode (policy: Policy) : DataSeedFormatter.Mode =
        if policy.Emission.RenderDataElegant then DataSeedFormatter.Enabled
        else DataSeedFormatter.Disabled

    let private renderArtifactInTopoOrder
        (mode: DataSeedFormatter.Mode)
        (laneTitle: string)
        (catalog: Catalog)
        (topo: TopologicalOrder)
        (artifact: ArtifactByKind<DataInsertScript>)
        : string =
        let map = ArtifactByKind.toMap artifact
        // SsKey → (module, entity) logical names for the V1-style per-block
        // headers (`renderDataElegant`). Absent kinds fall back to empty strings;
        // the compact `Disabled` path never reads them.
        let moduleEntityOf =
            catalog.Modules
            |> List.collect (fun m -> m.Kinds |> List.map (fun k -> k.SsKey, (Name.value m.Name, Name.value k.Name)))
            |> Map.ofList
        // Per-kind blocks in topological order — same `choose` (kinds in the
        // artifact map, topo order) the prior phase1/phase2 walk used, so the
        // `Disabled` reproduction stays byte-identical.
        let blocks =
            topo.Order
            |> List.choose (fun k ->
                Map.tryFind k map
                |> Option.map (fun s ->
                    let modName, entName =
                        Map.tryFind k moduleEntityOf |> Option.defaultValue ("", "")
                    { DataSeedFormatter.Module   = modName
                      DataSeedFormatter.Entity   = entName
                      DataSeedFormatter.RowCount = List.length s.Phase1Merges
                      DataSeedFormatter.Phase1   = s.RenderedPhase1
                      DataSeedFormatter.Phase2   = s.RenderedPhase2 }))
        // `Disabled` is the prior global Phase-1-then-Phase-2 concatenation
        // byte-for-byte; `Enabled` adds V1's banner / NOCOUNT / per-entity headers
        // + one-row-per-line VALUES. The published-file boundary only — the
        // parallel-deploy path (`composeRenderedLeveled*`) stays compact.
        DataSeedFormatter.renderLane mode laneTitle blocks

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
        // AC-D7 — the operator's convergent-delete scope (OperatorIntent of
        // Emission). The composer resolves it OFF `Policy` here and threads
        // the plain value; the emitters never see `Policy` (A18 amended).
        let opts = DataEmitOptions.ofEmissionPolicy policy.Emission
        match dispatchSiblings composition opts topo catalog profile migration (BootstrapLane.Rows Map.empty) userRemap with
        | Error e -> Error e
        | Ok siblings ->
            match unionSiblings catalog siblings with
            | Error e   -> Error e
            // The fused, on-demand single-artifact render is a DEPLOY shape, not a
            // reviewed file — it stays COMPACT (`Disabled`) so it remains a faithful
            // partition of `composeRenderedLeveled` (also compact). `renderDataElegant`
            // formats the published per-lane bundle files (`composeRenderedBundle*`),
            // never this deploy surface.
            | Ok artifact -> Ok (renderArtifactInTopoOrder DataSeedFormatter.Disabled "Data Seeds" catalog topo artifact)

    /// The three per-lane renderings from ONE dispatch (WP6 step 3):
    /// `Data/StaticSeeds.sql` / `Data/MigrationData.sql` /
    /// `Data/Bootstrap.sql`, each sibling walked global-phase in
    /// topological order. A lane with no owned rows renders the empty
    /// string. The FUSED cross-lane seed is deliberately NOT a bundle
    /// field: its one production consumer was an is-anything-there gate,
    /// yet materializing it re-concatenated every per-kind string into a
    /// second whole-estate copy (~the sum of the lanes) on every publish.
    /// Callers that need the fused text (the single-artifact deploy shape)
    /// take `composeRenderedFull` — the same union render, produced only
    /// when asked for. The partition law (`OverlappingEmitterCoverage`)
    /// still holds on the bundle path: `unionSiblings` runs for its
    /// assertion; only the union's RENDER is skipped.
    type RenderedDataBundle =
        {
            StaticSeeds   : string
            MigrationData : string
            Bootstrap     : string
        }

    [<RequireQualifiedAccess>]
    module RenderedDataBundle =
        /// The non-empty per-lane renderings, keyed by their `Data/*.sql`
        /// relative path. Empty lanes are dropped. Used by the pipeline to
        /// decide the per-lane file set (it emits these only when ≥2 are
        /// present — see `nonEmptyLaneCount`).
        let perLaneFiles (b: RenderedDataBundle) : Map<string, string> =
            [ "Data/StaticSeeds.sql",   b.StaticSeeds
              "Data/MigrationData.sql", b.MigrationData
              "Data/Bootstrap.sql",     b.Bootstrap ]
            |> List.filter (fun (_, sql) -> not (System.String.IsNullOrWhiteSpace sql))
            |> Map.ofList

        /// How many lanes carry content. The fused seed equals a single
        /// non-empty lane, so the per-lane split adds information only at ≥2.
        let nonEmptyLaneCount (b: RenderedDataBundle) : int =
            perLaneFiles b |> Map.count

    /// Π_Data compose-rendered with the per-lane split (WP6 step 3).
    /// One dispatch → the fused seed + the three lane renderings. The
    /// fused arm is byte-identical to `composeRenderedFull`.
    let composeRenderedBundleFull
        (policy: Policy)
        (catalog: Catalog)
        (profile: Profile)
        (migration: MigrationDependencyContext)
        (userRemap: UserRemapContext)
        : Result<RenderedDataBundle, EmitError> =
        use _ = Bench.scope "compose.data.composeRenderedBundle"
        let topoLineage = TopologicalOrderPass.runWith TreatAsCycle catalog
        let topo = topoLineage.Value
        let composition = policy.Emission.DataComposition
        let opts = DataEmitOptions.ofEmissionPolicy policy.Emission
        match dispatchSiblings composition opts topo catalog profile migration (BootstrapLane.Rows Map.empty) userRemap with
        | Error e -> Error e
        | Ok siblings ->
            match unionSiblings catalog siblings with
            | Error e   -> Error e
            | Ok _union ->
                // `_union` exists for `unionSiblings`' partition-law
                // assertion only — rendering it would re-concatenate the
                // whole estate's per-kind text a second time (the fused
                // surface is `composeRenderedFull`, on demand).
                Ok { StaticSeeds   = renderArtifactInTopoOrder (dataFormatMode policy) "Static Seeds" catalog topo siblings.StaticSeeds
                     MigrationData = renderArtifactInTopoOrder (dataFormatMode policy) "Migration Data" catalog topo siblings.MigrationDependencies
                     Bootstrap     = renderArtifactInTopoOrder (dataFormatMode policy) "Bootstrap" catalog topo siblings.Bootstrap }

    /// Convenience over `composeRenderedBundleFull` — empty contexts.
    let composeRenderedBundle
        (policy: Policy)
        (catalog: Catalog)
        (profile: Profile)
        : Result<RenderedDataBundle, EmitError> =
        composeRenderedBundleFull
            policy catalog profile
            MigrationDependencyContext.empty
            UserRemapContext.empty

    /// Π_Data compose-rendered bundle WITH the hydrated Bootstrap row source
    /// (WP6 step 3, completed 2026-06-14). The pipeline's hydration step
    /// streams the bootstrap lane's rows (`Map<SsKey, StaticRow list>`) from
    /// the live source — every data-bearing kind under `AllData`, the
    /// complement of (Static ∪ Migration) under `AllRemaining` — and threads
    /// them here so `Data/Bootstrap.sql` carries content. Byte-identical to
    /// `composeRenderedBundleFull` when `bootstrapRows = Map.empty` (the
    /// non-hydrated path). The non-Docker data-lane golden supplies an
    /// in-memory `bootstrapRows`; the Docker golden supplies the live-hydrated
    /// one — the same entry, the same render, differing only in the row source.
    /// `composeRenderedBundleWithBootstrap` with a CALLER-SUPPLIED
    /// topological order — the publish pipeline threads the chain's
    /// stored `ComposeState.TopologicalOrder` (the chain's
    /// `TopologicalOrderPass` already ran Kahn/Tarjan over the SAME
    /// post-chain catalog this composer receives) instead of re-running
    /// it here. Contract: `topo` MUST derive from `catalog`; the
    /// topo-less sibling below computes it and stays the safe entry for
    /// callers without a chain state.
    /// The lane-general core: the Bootstrap row source arrives as a
    /// `BootstrapLane` — either the drained rows (rendered here) or the
    /// drain-time-prerendered per-kind scripts (assembled here). The
    /// rows-taking sibling below delegates with `BootstrapLane.Rows`;
    /// the pipelined publish arm calls this directly with
    /// `BootstrapLane.Prerendered`.
    let composeRenderedBundleWithBootstrapLaneUsing
        (topo: TopologicalOrder)
        (policy: Policy)
        (catalog: Catalog)
        (profile: Profile)
        (migration: MigrationDependencyContext)
        (bootstrapLane: BootstrapLane)
        (userRemap: UserRemapContext)
        : Result<RenderedDataBundle, EmitError> =
        use _ = Bench.scope "compose.data.composeRenderedBundleWithBootstrap"
        let composition = policy.Emission.DataComposition
        let opts = DataEmitOptions.ofEmissionPolicy policy.Emission
        match dispatchSiblings composition opts topo catalog profile migration bootstrapLane userRemap with
        | Error e -> Error e
        | Ok siblings ->
            match unionSiblings catalog siblings with
            | Error e -> Error e
            | Ok _union ->
                // `_union` exists for `unionSiblings`' partition-law
                // assertion only — rendering it would re-concatenate the
                // whole estate's per-kind text a second time (the fused
                // surface is `composeRenderedFull`, on demand).
                Ok { StaticSeeds   = renderArtifactInTopoOrder (dataFormatMode policy) "Static Seeds" catalog topo siblings.StaticSeeds
                     MigrationData = renderArtifactInTopoOrder (dataFormatMode policy) "Migration Data" catalog topo siblings.MigrationDependencies
                     Bootstrap     = renderArtifactInTopoOrder (dataFormatMode policy) "Bootstrap" catalog topo siblings.Bootstrap }

    let composeRenderedBundleWithBootstrapUsing
        (topo: TopologicalOrder)
        (policy: Policy)
        (catalog: Catalog)
        (profile: Profile)
        (migration: MigrationDependencyContext)
        (bootstrapRows: Map<SsKey, StaticRow list>)
        (userRemap: UserRemapContext)
        : Result<RenderedDataBundle, EmitError> =
        composeRenderedBundleWithBootstrapLaneUsing
            topo policy catalog profile migration (BootstrapLane.Rows bootstrapRows) userRemap

    let composeRenderedBundleWithBootstrap
        (policy: Policy)
        (catalog: Catalog)
        (profile: Profile)
        (migration: MigrationDependencyContext)
        (bootstrapRows: Map<SsKey, StaticRow list>)
        (userRemap: UserRemapContext)
        : Result<RenderedDataBundle, EmitError> =
        let topo = (TopologicalOrderPass.runWith TreatAsCycle catalog).Value
        composeRenderedBundleWithBootstrapUsing topo policy catalog profile migration bootstrapRows userRemap

    /// Topo-less sibling of `composeRenderedBundleWithBootstrapLaneUsing`
    /// (computes the order here) — the safe entry for callers without a
    /// chain state, mirroring the rows-taking pair above.
    let composeRenderedBundleWithBootstrapLane
        (policy: Policy)
        (catalog: Catalog)
        (profile: Profile)
        (migration: MigrationDependencyContext)
        (bootstrapLane: BootstrapLane)
        (userRemap: UserRemapContext)
        : Result<RenderedDataBundle, EmitError> =
        let topo = (TopologicalOrderPass.runWith TreatAsCycle catalog).Value
        composeRenderedBundleWithBootstrapLaneUsing topo policy catalog profile migration bootstrapLane userRemap

    /// Per-level rendered scripts for parallel-safe deployment. Each
    /// `ParallelSafe<string>` group carries one kind's rendered SQL per
    /// member, at one topological level; the lists are level-ordered
    /// (level 0 first).
    ///
    /// **Realization contract (card P1 — the half the type now
    /// carries):** WITHIN-group independence is the token itself —
    /// `ParallelSafe` is minted only by `TopologicalOrder.levels` and
    /// rides here through per-member `choose` (kind → that kind's
    /// rendered script), so `Deploy.executeBatchParallel` can demand it.
    /// What stays the caller's duty, stated: deploy `Phase1Levels` in
    /// order, then `Phase2Levels` in order — LEVELS are sequential;
    /// only members within one level are concurrent. Empty levels are
    /// dropped (a level whose kinds have empty `RenderedPhase1` /
    /// `RenderedPhase2` doesn't appear). Slice
    /// A.4.7'-prelude.perf-sweep-6 (composer-levels) cash-out.
    type LeveledDeploymentText = {
        Phase1Levels : ParallelSafe<string> list
        Phase2Levels : ParallelSafe<string> list
    }

    /// Companion surface for the leveled plan (card P2 — the load leg's
    /// "nothing to deploy" arm needs both, mirroring the fused form's
    /// `IsNullOrWhiteSpace` gate). `composeRenderedLeveled` drops empty
    /// levels, so `isEmpty` ⇔ the catalog projected no seed statements.
    [<RequireQualifiedAccess>]
    module LeveledDeploymentText =
        let empty : LeveledDeploymentText =
            { Phase1Levels = []; Phase2Levels = [] }
        let isEmpty (plan: LeveledDeploymentText) : bool =
            List.isEmpty plan.Phase1Levels && List.isEmpty plan.Phase2Levels

    /// Level-aware sibling of `composeRenderedFull` — returns the
    /// rendered text grouped by topological level so callers can
    /// dispatch each level's segments in parallel via
    /// `Deploy.executeBatchParallel`. Same `dispatchSiblings` +
    /// `unionSiblings` partition pipeline; the only differences from
    /// `composeRenderedFull` are (a) per-level grouping via
    /// `TopologicalOrder.levels`, (b) structured return type, (c)
    /// empty-level dropping. Carries the hydrated Bootstrap row source
    /// (Bootstrap-always, 2026-06-14) — the store-leg counterpart of
    /// `composeRenderedBundleWithBootstrap`, so the deployed leveled seed
    /// plan carries the same Bootstrap rows the published bundle does (the
    /// parity duty). Byte-identical to `composeRenderedLeveled` when the
    /// Bootstrap lane is empty.
    /// The lane-general leveled core with a CALLER-SUPPLIED topological
    /// order — the leveled twin of `composeRenderedBundleWithBootstrapLane
    /// Using` (PL-1): the publish pipeline threads the chain's stored
    /// `ComposeState.TopologicalOrder` (Kahn/Tarjan already ran over the
    /// SAME post-chain catalog) and its Bootstrap lane (drained rows or
    /// drain-time-prerendered scripts) instead of re-deriving both here.
    /// Contract: `topo` MUST derive from `catalog`; the topo-less siblings
    /// below compute it and stay the safe entries.
    let composeRenderedLeveledWithBootstrapLaneUsing
        (topo: TopologicalOrder)
        (policy: Policy)
        (catalog: Catalog)
        (profile: Profile)
        (migration: MigrationDependencyContext)
        (bootstrapLane: BootstrapLane)
        (userRemap: UserRemapContext)
        : Result<LeveledDeploymentText, EmitError> =
        use _ = Bench.scope "compose.data.composeRenderedLeveled"
        let composition = policy.Emission.DataComposition
        // AC-D7 — the operator's convergent-delete scope (OperatorIntent of
        // Emission). The composer resolves it OFF `Policy` here and threads
        // the plain value; the emitters never see `Policy` (A18 amended).
        let opts = DataEmitOptions.ofEmissionPolicy policy.Emission
        match dispatchSiblings composition opts topo catalog profile migration bootstrapLane userRemap with
        | Error e -> Error e
        | Ok siblings ->
            match unionSiblings catalog siblings with
            | Error e -> Error e
            | Ok artifact ->
                let map = ArtifactByKind.toMap artifact
                let levels = TopologicalOrder.levels topo
                // Card P1 — the token rides the rendering: per-member
                // `choose` (kind → that kind's rendered script) preserves
                // the group structure `levels` proved, so the cross-kind
                // concatenation (and its LINT-ALLOW) is retired — the
                // members stay distinct and `executeBatchParallel` splits
                // per member.
                let scriptsForLevel
                    (selector: DataInsertScript -> string)
                    (level: ParallelSafe<SsKey>)
                    : ParallelSafe<string> =
                    level
                    |> ParallelSafe.choose (fun k ->
                        Map.tryFind k map
                        |> Option.map selector
                        |> Option.filter (fun s -> s.Length > 0))
                let nonEmpty = List.filter (ParallelSafe.isEmpty >> not)
                let phase1 =
                    levels
                    |> List.map (scriptsForLevel (fun s -> s.RenderedPhase1))
                    |> nonEmpty
                let phase2 =
                    levels
                    |> List.map (scriptsForLevel (fun s -> s.RenderedPhase2))
                    |> nonEmpty
                Bench.recordSample "compose.data.composeRenderedLeveled.phase1Levels" (int64 phase1.Length)
                Bench.recordSample "compose.data.composeRenderedLeveled.phase2Levels" (int64 phase2.Length)
                Ok { Phase1Levels = phase1; Phase2Levels = phase2 }

    /// Rows-taking leveled sibling with a caller-supplied order — mirrors
    /// `composeRenderedBundleWithBootstrapUsing`.
    let composeRenderedLeveledWithBootstrapUsing
        (topo: TopologicalOrder)
        (policy: Policy)
        (catalog: Catalog)
        (profile: Profile)
        (migration: MigrationDependencyContext)
        (bootstrapRows: Map<SsKey, StaticRow list>)
        (userRemap: UserRemapContext)
        : Result<LeveledDeploymentText, EmitError> =
        composeRenderedLeveledWithBootstrapLaneUsing
            topo policy catalog profile migration (BootstrapLane.Rows bootstrapRows) userRemap

    let composeRenderedLeveledWithBootstrap
        (policy: Policy)
        (catalog: Catalog)
        (profile: Profile)
        (migration: MigrationDependencyContext)
        (bootstrapRows: Map<SsKey, StaticRow list>)
        (userRemap: UserRemapContext)
        : Result<LeveledDeploymentText, EmitError> =
        let topo = (TopologicalOrderPass.runWith TreatAsCycle catalog).Value
        composeRenderedLeveledWithBootstrapUsing topo policy catalog profile migration bootstrapRows userRemap

    /// Level-aware sibling of `composeRenderedFull` — no Bootstrap row source
    /// (`composeRenderedLeveledWithBootstrap` with `Map.empty`; byte-identical
    /// to the pre-Bootstrap-always behaviour). The established call shape for
    /// the canary / perf callers that do not hydrate a Bootstrap lane.
    let composeRenderedLeveled
        (policy: Policy)
        (catalog: Catalog)
        (profile: Profile)
        (migration: MigrationDependencyContext)
        (userRemap: UserRemapContext)
        : Result<LeveledDeploymentText, EmitError> =
        composeRenderedLeveledWithBootstrap policy catalog profile migration Map.empty userRemap

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
        // Pipeline-bound (composer-stage); record literal form per the
        // sibling-emitter-registry-helper arc's "metadata helpers cover
        // Emitter + Adapter only; Pipeline / Pass / OrderingPolicy
        // flow through their typed shells or stay literal." TransformSite
        // helpers still apply for the inner sites.
        { Name = "dataEmissionComposer"
          Domain = Data
          StageBinding = Pipeline
          Sites =
            [ TransformSite.operatorIntent "compositionDispatch" Emission
                "Reads `Policy.Emission.DataComposition` (closed DU `AllRemaining \| AllExceptStatic \| AllData`) and dispatches which sibling emitters fire. This is the canonical site where operator intent enters the data-emission pipeline — the composer is the only data-axis surface that consumes `Policy`. OverlayAxis = Emission (chooses what physical form a kind takes in emitted output)."
              TransformSite.operatorIntent "migrationContextThreading" Insertion
                "Threads the operator-supplied `MigrationDependencyContext` to `MigrationDependenciesEmitter`. The context's row inventory is operator-published evidence (pre-scope §2.2); the composer is the routing layer. OverlayAxis = Insertion."
              TransformSite.operatorIntent "userRemapContextThreading" Insertion
                "Threads the operator-supplied `UserRemapContext` to `MigrationDependenciesEmitter` + `BootstrapEmitter`. The remap mapping is operator-supplied (chapter 4.2 slice γ); the composer is the routing layer. OverlayAxis = Insertion."
              TransformSite.dataIntent "globalPhaseOrdering"
                "Slice ι cash-out — concatenate ALL Phase-1 MERGEs (across all kinds + all emitters, in topological order) before ANY Phase-2 UPDATE. The ordering is structural (deploy-correctness for multi-kind FK cycles); no operator opinion enters. DataIntent — the topology is the source of truth."
              TransformSite.dataIntent "partitionAssertion"
                "Slice θ cash-out — every kind's populated coverage comes from at most one sibling emitter under a given `DataComposition`; overlap surfaces as `EmitError.OverlappingEmitterCoverage`. The partition check is structural fidelity (not configurable); fires deterministically on first overlap in catalog order." ]
          Status = Active }
