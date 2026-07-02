namespace Projection.Pipeline

open System.Threading.Tasks
open Projection.Core
open Projection.Core.Passes
open Projection.Adapters.Sql

/// WP6 step 4 (DECISIONS 2026-06-13) — the read-only hydration step for the
/// full-export data lanes. The forward OSSYS model read marks static entities
/// `Static []` (the marker present, populations empty — `OssysRowsetReader`);
/// this step streams those rows from the live source and grafts them onto the
/// catalog BEFORE the transform chain, so the static-seed lane emits real rows
/// instead of nothing. Read-only, adapter-side; streams via `Ingestion`
/// scoped to the static-marked kinds — NEVER `ReadSide.read`, which marks
/// every data-bearing table `Static` (survival rule 8).
///
/// The async entry (`hydrateCatalog`) lives here, in the async pipeline
/// caller — never inside the sync `runWithConfigCore` (FS3511). The named
/// skip for a file-sourced model is `diagnostics` (pure, config-derived,
/// surfaced by `runWithConfigCore`).
[<RequireQualifiedAccess>]
module Hydration =

    /// EmitData per `Compose.buildPolicyFromConfig` — any data-lane flag turns
    /// it on. Replicated here (config-shaped, not policy) so hydration can
    /// gate without building the full `Policy`.
    let internal emitDataOf (cfg: Config.Config) : bool =
        cfg.Emission.StaticSeeds
        || cfg.Emission.MigrationDependencies
        || cfg.Emission.Bootstrap

    /// A kind carries a `Static` modality marker (the static-entity class the
    /// OSSYS adapter flags). Distinct from "has rows": the marker is present
    /// with empty populations on the forward read.
    let private isStaticKind (k: Kind) : bool =
        k.Modality |> List.exists (function Static _ -> true | _ -> false)

    /// Graft hydrated rows onto the catalog's static-marked kinds, by `SsKey`
    /// (rename-invariant, A1), preserving every other modality mark and the
    /// kind order. Pure. Grafting PRE-chain lets `NormalizeStaticPopulations`
    /// sort the rows deterministically downstream. A kind without a `Static`
    /// marker, or one absent from `rowsByKind`, is untouched.
    let graftStaticPopulations
        (rowsByKind: Map<SsKey, StaticRow list>)
        (catalog: Catalog)
        : Catalog =
        catalog
        |> Catalog.mapKinds (fun k ->
            match Map.tryFind k.SsKey rowsByKind with
            | Some rows when isStaticKind k ->
                { k with
                    Modality =
                        k.Modality |> List.map (function Static _ -> Static rows | m -> m) }
            | _ -> k)

    /// The named diagnostics for the hydration step, derived from config
    /// (pure; surfaced by `runWithConfigCore`). Data on + NO live OSSYS source
    /// (the model is read from `model.path`, the osm_model.json fallback, not
    /// `model.ossys`) ⇒ a NAMED skip — never silent emptiness: the static
    /// lanes can only emit catalog-resident rows because there is no live
    /// connection to hydrate from. NB this keys on the PRESENCE of
    /// `model.ossys`, not its ref FORM — an `env:` and a `file:` ossys ref
    /// both hydrate (the `file:` form is the operator's predominant one). A
    /// live OSSYS source, or data-off, ⇒ no diagnostic.
    let diagnostics (cfg: Config.Config) : DiagnosticEntry list =
        if not (emitDataOf cfg) then []
        else
            match cfg.Model.Ossys with
            | Some _ -> []
            | None ->
                match cfg.Model.Path with
                | Some _ ->
                    [ DiagnosticEntry.create
                        "data:hydration"
                        DiagnosticSeverity.Warning
                        "data.hydration.skippedNoLiveSource"
                        "data emission is on but the model has no live OSSYS source: it is read from model.path (the osm_model.json fallback), not model.ossys. Static-entity rows can only be hydrated from a live connection, so the data lanes emit only catalog-resident populations. Set model.ossys (an env: or file: connection ref) to hydrate." ]
                | None ->
                    // NM-48: the BOTH-ABSENT case (Ossys = None ∧ Path = None,
                    // data on). The model has no source at all — a degenerate
                    // config the upstream model-read refuses (`modelNoSource`),
                    // so this branch is UNREACHABLE in the normal pipeline. The
                    // "named skip, never silence" law does not get to rely on a
                    // distant guard: rather than fall to a silent `[]`, name the
                    // skip explicitly so an out-of-pipeline caller of this pure
                    // function (or a future path that bypasses the guard) sees a
                    // diagnostic, not nothing.
                    [ DiagnosticEntry.create
                        "data:hydration"
                        DiagnosticSeverity.Warning
                        "data.hydration.skippedNoModelSource"
                        "data emission is on but the model declares no source at all (neither model.path nor model.ossys). The model read refuses this configuration upstream (pipeline.config.modelNoSource); if it is reached here, no rows can be hydrated and the data lanes emit nothing. Set model.ossys (to hydrate) or model.path (catalog-resident populations only)." ]

    /// Stream the static-marked kinds' rows from an open OSSYS connection and
    /// graft them. Scoped to the static kinds via `Ingestion.collectInOrderFor`
    /// (never the mark-everything `ReadSide.read`; survival rule 8).
    let private streamAndGraft (topo: TopologicalOrder) (cnn: Microsoft.Data.SqlClient.SqlConnection) (catalog: Catalog) : Task<Catalog> =
        task {
            let ownedStatic =
                Catalog.allKinds catalog
                |> List.filter isStaticKind
                |> List.map (fun k -> k.SsKey)
                |> Set.ofList
            let! rowsByKind = Ingestion.collectInOrderFor ownedStatic cnn catalog topo
            return graftStaticPopulations rowsByKind catalog
        }

    /// The bounded-concurrent static-graft arm, hoisted to module level so the
    /// caller's `task { }` stays statically compilable in Release (FS3511 —
    /// deeply nested match/match! inside one state machine is the named
    /// failure shape; cf. `runWithConfigCore`).
    let private hydrateStaticConcurrent
        (concurrency: int)
        (connSpec: string)
        (topo: TopologicalOrder)
        (catalog: Catalog)
        : Task<Result<Catalog>> =
        task {
            let ownedStatic =
                Catalog.allKinds catalog
                |> List.filter isStaticKind
                |> List.map (fun k -> k.SsKey)
                |> Set.ofList
            // Resolve the spec ONCE; per-kind opens are pure pooled opens on
            // the same connection string.
            match ConnectionSpec.openerFor "ossys-hydration-source" connSpec with
            | Error es -> return Result.failure es
            | Ok openConnection ->
                match! Ingestion.collectInOrderForConcurrent concurrency openConnection ownedStatic catalog topo with
                | Error es -> return Result.failure es
                | Ok rowsByKind -> return Result.success (graftStaticPopulations rowsByKind catalog)
        }

    /// The bounded-concurrent Bootstrap arm — same FS3511 hoist as
    /// `hydrateStaticConcurrent`.
    let private collectBootstrapConcurrent
        (concurrency: int)
        (connSpec: string)
        (eligible: Set<SsKey>)
        (catalog: Catalog)
        (topo: TopologicalOrder)
        : Task<Result<Map<SsKey, StaticRow list>>> =
        task {
            // Resolve the spec ONCE; per-kind opens are pure pooled opens on
            // the same connection string.
            match ConnectionSpec.openerFor "ossys-bootstrap-source" connSpec with
            | Error es -> return Result.failure es
            | Ok openConnection ->
                return! Ingestion.collectInOrderForConcurrent concurrency openConnection eligible catalog topo
        }

    /// Hydrate the catalog for a full-export run. No data emission ⇒ identity.
    /// No live OSSYS source (the model came from `model.path`) ⇒ identity (the
    /// skip is named in `diagnostics`, never silent). `model.ossys` present ⇒
    /// open a SECOND connection (the model-read connection is use-disposed, not
    /// reusable) and stream+graft, through the one `ConnectionSpec.openSpec`
    /// opener (every spec form; `env:` / `file:` remain the recommended
    /// out-of-band ossys ref, neither special-cased nor deprecated). Mirrors
    /// `LiveModelRead.fromConnSpecWith`'s open template (the same one opener).
    /// `hydrateCatalog` with a CALLER-SUPPLIED topological order over
    /// `catalog` — the publish pipeline computes ONE order for both
    /// hydration arms (static graft + bootstrap rows run over the same
    /// pre-chain catalog; grafting changes `Modality` only, never the FK
    /// edges the order derives from). The topo-less sibling below
    /// computes it and stays the safe entry for standalone callers.
    let hydrateCatalogUsing (topo: TopologicalOrder) (cfg: Config.Config) (catalog: Catalog) : Task<Result<Catalog>> =
        task {
            if not (emitDataOf cfg) then
                return Result.success catalog
            else
                match cfg.Model.Ossys with
                | None -> return Result.success catalog
                | Some connSpec ->
                    // `emission.dataReadConcurrency` — bounded parallel row
                    // hydration, each kind on its own short-lived connection
                    // through the ONE `ConnectionSpec.openSpec` opener.
                    // `1` is the strictly serial single-connection path.
                    let concurrency = max 1 cfg.Emission.DataReadConcurrency
                    if concurrency = 1 then
                        match! ConnectionSpec.openSpec SubstrateRole.Source "ossys-hydration-source" connSpec with
                        | Error es -> return Result.failure es
                        | Ok cnn ->
                            use cnn = cnn
                            let! hydrated = streamAndGraft topo cnn catalog
                            return Result.success hydrated
                    else
                        return! hydrateStaticConcurrent concurrency connSpec topo catalog
        }

    let hydrateCatalog (cfg: Config.Config) (catalog: Catalog) : Task<Result<Catalog>> =
        // Compute the order only past the same gates the Using form
        // no-ops on — a data-off / file-sourced publish pays nothing.
        if not (emitDataOf cfg) || cfg.Model.Ossys.IsNone then
            Task.FromResult (Result.success catalog)
        else
            hydrateCatalogUsing (TopologicalOrderPass.runWith TreatAsCycle catalog).Value cfg catalog

    /// The Bootstrap lane's row source (Bootstrap-always, 2026-06-14). Streams
    /// the bootstrap-eligible kinds' rows from the live OSSYS source into the
    /// `Map<SsKey, StaticRow list>` shape `DataLoadPlan.build` consumes — every
    /// data-bearing kind under `AllData`, the complement of (Static-marked ∪
    /// Migration) under `AllRemaining`/`AllExceptStatic`. Disjoint from the
    /// Static lane's catalog-grafted populations AND the operator-curated
    /// Migration lane, so the composer's `OverlappingEmitterCoverage` partition
    /// law holds. Data-off / no live OSSYS source / no eligible kinds ⇒ the
    /// empty Map (the file-sourced skip is NAMED in `diagnostics`, never
    /// silent). Scoped via `Ingestion.collectInOrderFor` — never the
    /// mark-everything `ReadSide.read` (survival rule 8). A SECOND short-lived
    /// connection (the model-read connection is use-disposed), mirroring
    /// `hydrateCatalog`.
    ///
    /// `migrationKinds` (migration-context wiring, 2026-06-15) is the set of
    /// kind `SsKey`s the operator-curated Migration lane populates
    /// (`MigrationDependenciesBinding.kindKeysOf`). Under `AllRemaining`/
    /// `AllExceptStatic` those kinds are excluded from the Bootstrap complement
    /// so the three lanes stay disjoint; under `AllData` the Migration lane is
    /// skipped (`dispatchSiblings`) so the exclusion does not apply. `Set.empty`
    /// (no migration file) is byte-identical to the prior Static-only exclusion.
    /// The caller-supplied-topo sibling of `hydrateBootstrapRowsExcluding`
    /// (see `hydrateCatalogUsing` — one order serves both hydration arms).
    /// The Bootstrap lane's eligibility set — the ONE definition both the
    /// row-collecting arm below and the pipelined drain-time-rendering arm
    /// (`collectBootstrapRenderedUsing`'s Pipeline caller) read, so the two
    /// execution schedules cannot disagree about which kinds the lane owns.
    /// A kind carries rows worth bootstrapping only if it has columns to
    /// write (PK-less / attribute-less artifacts have no MERGE); under
    /// `AllRemaining`/`AllExceptStatic` the Static-marked and operator-
    /// curated Migration kinds are excluded (the partition law); under
    /// `AllData` Bootstrap owns everything data-bearing. Data-off ⇒ empty.
    let bootstrapEligible
        (migrationKinds: Set<SsKey>)
        (cfg: Config.Config)
        (catalog: Catalog)
        : Set<SsKey> =
        if not (emitDataOf cfg) then Set.empty
        else
            let isDataBearing (k: Kind) = not (List.isEmpty k.Attributes)
            let composition = Config.dataCompositionOf cfg
            Catalog.allKinds catalog
            |> List.filter (fun k ->
                isDataBearing k
                && (match composition with
                    | AllData -> true
                    | AllRemaining | AllExceptStatic ->
                        not (isStaticKind k) && not (Set.contains k.SsKey migrationKinds)))
            |> List.map (fun k -> k.SsKey)
            |> Set.ofList

    /// The Static-marked kind keyset — the pipelined publish arm's evidence
    /// partition reads it (static kinds never enter evidence derivation,
    /// mirroring `LiveProfiler`'s own non-static filter).
    let staticKindKeys (catalog: Catalog) : Set<SsKey> =
        Catalog.allKinds catalog
        |> List.filter isStaticKind
        |> List.map (fun k -> k.SsKey)
        |> Set.ofList

    let hydrateBootstrapRowsExcludingUsing
        (topo: TopologicalOrder)
        (migrationKinds: Set<SsKey>)
        (cfg: Config.Config)
        (catalog: Catalog)
        : Task<Result<Map<SsKey, StaticRow list>>> =
        task {
            if not (emitDataOf cfg) then
                return Result.success Map.empty
            else
                let eligible = bootstrapEligible migrationKinds cfg catalog
                if Set.isEmpty eligible then
                    return Result.success Map.empty
                else
                    match cfg.Model.Ossys with
                    | None -> return Result.success Map.empty
                    | Some connSpec ->
                        // `emission.dataReadConcurrency` — the Bootstrap lane
                        // is the all-data row source, so the bounded parallel
                        // drain is where the wall-clock win lives. `1` keeps
                        // the strictly serial single-connection path.
                        let concurrency = max 1 cfg.Emission.DataReadConcurrency
                        if concurrency = 1 then
                            match! ConnectionSpec.openSpec SubstrateRole.Source "ossys-bootstrap-source" connSpec with
                            | Error es -> return Result.failure es
                            | Ok cnn ->
                                use cnn = cnn
                                let! rows = Ingestion.collectInOrderFor eligible cnn catalog topo
                                return Result.success rows
                        else
                            return! collectBootstrapConcurrent concurrency connSpec eligible catalog topo
        }

    let hydrateBootstrapRowsExcluding
        (migrationKinds: Set<SsKey>)
        (cfg: Config.Config)
        (catalog: Catalog)
        : Task<Result<Map<SsKey, StaticRow list>>> =
        if not (emitDataOf cfg) || cfg.Model.Ossys.IsNone then
            Task.FromResult (Result.success Map.empty)
        else
            hydrateBootstrapRowsExcludingUsing
                (TopologicalOrderPass.runWith TreatAsCycle catalog).Value
                migrationKinds cfg catalog

    /// No-migration-lane Bootstrap hydration (`hydrateBootstrapRowsExcluding`
    /// with `Set.empty`) — byte-identical to the pre-migration-wiring behaviour.
    /// The established call shape for callers without an operator-curated
    /// Migration lane (canary / golden tests); the config-driven publish path
    /// routes through `…Excluding` with the resolved migration kinds.
    let hydrateBootstrapRows (cfg: Config.Config) (catalog: Catalog) : Task<Result<Map<SsKey, StaticRow list>>> =
        hydrateBootstrapRowsExcluding Set.empty cfg catalog

    /// One kind's drain-time projection for the PIPELINED Bootstrap arm:
    /// rows land → the kind's MERGE script renders immediately (against
    /// its RENDER-CATALOG target kind — post-prefix-chain physical form)
    /// and, when the kind is in the evidence partition, its evidence
    /// cache derives from the same rows — then the rows drop. Hoisted to
    /// module level (FS3511) and kept as the ONE projection body so the
    /// per-kind cost the drain gate bounds is named in one place.
    let private renderKindAtDrain
        (targetKinds: Map<SsKey, Kind>)
        (cycleMembers: Set<SsKey>)
        (opts: DataEmitOptions)
        (cdc: CdcAwareness)
        (nullability: Map<string, Map<string, bool>>)
        (evidenceKinds: Set<SsKey>)
        (srcKind: Kind)
        (rows: StaticRow list)
        : Projection.Targets.Data.DataInsertScript * CachedKind option =
        // Render against the TARGET kind (the post-prefix-chain catalog's
        // physical form — same kind `emitWithTopo`'s batch plan resolves);
        // a kind the chain dropped falls back to its source form (it then
        // cannot be in the render catalog's keyset and the composer never
        // reads its script).
        let tgt =
            Map.tryFind srcKind.SsKey targetKinds
            |> Option.defaultValue srcKind
        // The SAME per-kind plan core `DataLoadPlan.buildWith` folds
        // (structural policy; the publish path's user remap is empty —
        // `UserRemapContext.toSurrogate` of the empty mapping IS
        // `SurrogateRemapContext.empty`), so the drain-time script equals
        // the batch-built one by construction.
        let load, _skipped =
            DataLoadPlan.loadFor cycleMembers SurrogateRemapContext.empty tgt rows
        let script =
            Projection.Targets.Data.StaticSeedsEmitter.renderLoad opts cdc tgt load
        // Evidence derives from the SOURCE kind over the same landed rows
        // (the profile describes the source; physical names must match the
        // live nullability reflection). Sampled / static kinds are outside
        // the partition — their evidence stays with the live discovery.
        let evidence =
            if Set.contains srcKind.SsKey evidenceKinds then
                EvidenceCache.cachedKindOfRows
                    (LiveProfiler.nullabilityFor nullability srcKind)
                    srcKind
                    rows
            else None
        script, evidence

    /// The PIPELINED Bootstrap collect (P2 production wiring): drain the
    /// eligible kinds' rows from the live OSSYS source and, ON THE DRAIN
    /// WORKER as each kind's rows land (inside the concurrency gate, after
    /// the connection is pooled back — `Ingestion
    /// .collectInOrderForConcurrentWith`), render that kind's Bootstrap
    /// MERGE script and derive its evidence cache, dropping the rows. The
    /// per-kind render + evidence cost OVERLAPS the remaining kinds' wire
    /// time instead of serializing after the whole-estate drain, and live
    /// row memory is capped at `concurrency` kinds rather than the estate.
    ///
    /// Identity contract (pinned by the pipelined-bootstrap equivalence
    /// test): the returned scripts equal what `BootstrapEmitter
    /// .emitWithTopo` renders at compose time from the same rows — same
    /// `DataLoadPlan.loadFor` core, same `StaticSeedsEmitter.renderLoad`,
    /// same delete-scope-suppressed `opts` (the CALLER passes the
    /// bootstrap-lane posture: `DataEmitOptions.withDeleteScope None`) —
    /// and the returned evidence equals `captureEvidenceCacheDerived`'s
    /// per-kind derivation. `cycleMembers` MUST be the RENDER catalog's
    /// (`TopologicalOrder.cycleMembers` of the post-prefix-chain topo);
    /// `sourceTopo` only schedules the drain over the source catalog.
    let collectBootstrapRenderedUsing
        (concurrency: int)
        (connSpec: string)
        (eligible: Set<SsKey>)
        (sourceCatalog: Catalog)
        (targetKinds: Map<SsKey, Kind>)
        (cycleMembers: Set<SsKey>)
        (opts: DataEmitOptions)
        (cdc: CdcAwareness)
        (nullability: Map<string, Map<string, bool>>)
        (evidenceKinds: Set<SsKey>)
        (sourceTopo: TopologicalOrder)
        : Task<Result<Map<SsKey, Projection.Targets.Data.DataInsertScript * CachedKind option>>> =
        task {
            if Set.isEmpty eligible then
                return Result.success Map.empty
            else
                // Resolve the spec ONCE; per-kind opens are pure pooled opens
                // on the same connection string (mirrors the row-collecting
                // Bootstrap arm).
                match ConnectionSpec.openerFor "ossys-bootstrap-source" connSpec with
                | Error es -> return Result.failure es
                | Ok openConnection ->
                    return!
                        Ingestion.collectInOrderForConcurrentWith
                            (renderKindAtDrain targetKinds cycleMembers opts cdc nullability evidenceKinds)
                            (max 1 concurrency)
                            openConnection
                            eligible
                            sourceCatalog
                            sourceTopo
        }

    /// Registry metadata (pillar 9). Read-only observation — DataIntent
    /// (mirrors the OSSYS `CatalogReader` / Transfer `Ingestion` adapters).
    /// The `staticRowHydration` site covers `graftStaticPopulations` (the
    /// pre-chain Catalog→Catalog graft). F13 (audit 2026-06-17): this metadata
    /// existed but was never assembled into `RegisteredAllTransforms.all` — it
    /// is now wired in, so the graft is visible to the unified totality view.
    let registeredMetadata : RegisteredTransformMetadata =
        RegisteredTransformMetadata.adapter "fullExportHydration" Data
            [ TransformSite.dataIntent "staticRowHydration"
                "Stream static-entity rows from the live OSSYS source (Ingestion.collectInOrderFor, scoped to static-marked kinds — never ReadSide.read) and graft them onto the catalog's Static populations before the data lanes render. The model.ossys connection ref may be env: or file: (both hydrate identically). Observation only — the rows are what the source holds; no operator opinion enters. A model with no live OSSYS source (read from model.path) skips with the named data.hydration.skippedNoLiveSource diagnostic."
              TransformSite.dataIntent "bootstrapRowHydration"
                "Stream the Bootstrap lane's rows from the live OSSYS source (Ingestion.collectInOrderFor, scoped per DataComposition — every data-bearing kind under AllData, the complement of Static ∪ Migration under AllRemaining/AllExceptStatic; never ReadSide.read) into the Map<SsKey, StaticRow list> the BootstrapEmitter renders. Observation only — the rows are what the source holds; no operator opinion enters. Disjoint from the Static lane (the partition law). Data-off / file-sourced ⇒ the empty Map (named skip in diagnostics)." ]
