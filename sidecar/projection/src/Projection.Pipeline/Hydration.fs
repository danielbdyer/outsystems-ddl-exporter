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
    let private emitDataOf (cfg: Config.Config) : bool =
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
    let private streamAndGraft (cnn: Microsoft.Data.SqlClient.SqlConnection) (catalog: Catalog) : Task<Catalog> =
        task {
            let topo = (TopologicalOrderPass.runWith TreatAsCycle catalog).Value
            let ownedStatic =
                Catalog.allKinds catalog
                |> List.filter isStaticKind
                |> List.map (fun k -> k.SsKey)
                |> Set.ofList
            let! rowsByKind = Ingestion.collectInOrderFor ownedStatic cnn catalog topo
            return graftStaticPopulations rowsByKind catalog
        }

    /// Hydrate the catalog for a full-export run. No data emission ⇒ identity.
    /// No live OSSYS source (the model came from `model.path`) ⇒ identity (the
    /// skip is named in `diagnostics`, never silent). `model.ossys` present ⇒
    /// open a SECOND connection (the model-read connection is use-disposed, not
    /// reusable) and stream+graft. `parseConnRef` accepts BOTH `env:` and
    /// `file:` ossys refs and hydration treats them identically — the `file:`
    /// form is not special-cased and not deprecated. The OSSYS branch mirrors
    /// `LiveModelRead.fromConnSpecWith`'s open template.
    let hydrateCatalog (cfg: Config.Config) (catalog: Catalog) : Task<Result<Catalog>> =
        task {
            if not (emitDataOf cfg) then
                return Result.success catalog
            else
                match cfg.Model.Ossys with
                | None -> return Result.success catalog
                | Some connSpec ->
                    match LiveModelRead.parseConnRef connSpec with
                    | Error es -> return Result.failure es
                    | Ok connRef ->
                        let sub : Substrate =
                            { Environment   = Environment.Named "ossys-hydration-source"
                              Role          = SubstrateRole.Source
                              ConnectionRef = connRef }
                        match! ConnectionResolver.openSubstrate sub with
                        | Error es -> return Result.failure es
                        | Ok cnn ->
                            use cnn = cnn
                            let! hydrated = streamAndGraft cnn catalog
                            return Result.success hydrated
        }

    /// The Bootstrap lane's row source (Bootstrap-always, 2026-06-14). Streams
    /// the bootstrap-eligible kinds' rows from the live OSSYS source into the
    /// `Map<SsKey, StaticRow list>` shape `DataLoadPlan.build` consumes — every
    /// data-bearing kind under `AllData`, the complement of (Static-marked ∪
    /// Migration) under `AllRemaining`/`AllExceptStatic`. (Migration is empty in
    /// the production path today; the complement excludes its kinds when it is
    /// wired.) Disjoint from the Static lane's catalog-grafted populations, so
    /// the composer's `OverlappingEmitterCoverage` partition law holds. Data-off
    /// / no live OSSYS source / no eligible kinds ⇒ the empty Map (the
    /// file-sourced skip is NAMED in `diagnostics`, never silent). Scoped via
    /// `Ingestion.collectInOrderFor` — never the mark-everything `ReadSide.read`
    /// (survival rule 8). A SECOND short-lived connection (the model-read
    /// connection is use-disposed), mirroring `hydrateCatalog`.
    let hydrateBootstrapRows (cfg: Config.Config) (catalog: Catalog) : Task<Result<Map<SsKey, StaticRow list>>> =
        task {
            if not (emitDataOf cfg) then
                return Result.success Map.empty
            else
                // A kind carries rows worth bootstrapping only if it has columns
                // to write (PK-less / attribute-less artifacts have no MERGE).
                let isDataBearing (k: Kind) = not (List.isEmpty k.Attributes)
                let composition = Config.dataCompositionOf cfg
                let eligible =
                    Catalog.allKinds catalog
                    |> List.filter (fun k ->
                        isDataBearing k
                        && (match composition with
                            | AllData -> true
                            | AllRemaining | AllExceptStatic -> not (isStaticKind k)))
                    |> List.map (fun k -> k.SsKey)
                    |> Set.ofList
                if Set.isEmpty eligible then
                    return Result.success Map.empty
                else
                    match cfg.Model.Ossys with
                    | None -> return Result.success Map.empty
                    | Some connSpec ->
                        match LiveModelRead.parseConnRef connSpec with
                        | Error es -> return Result.failure es
                        | Ok connRef ->
                            let sub : Substrate =
                                { Environment   = Environment.Named "ossys-bootstrap-source"
                                  Role          = SubstrateRole.Source
                                  ConnectionRef = connRef }
                            match! ConnectionResolver.openSubstrate sub with
                            | Error es -> return Result.failure es
                            | Ok cnn ->
                                use cnn = cnn
                                let topo = (TopologicalOrderPass.runWith TreatAsCycle catalog).Value
                                let! rows = Ingestion.collectInOrderFor eligible cnn catalog topo
                                return Result.success rows
        }

    /// Registry metadata (pillar 9). Read-only observation — DataIntent
    /// (mirrors the OSSYS `CatalogReader` / Transfer `Ingestion` adapters).
    let registeredMetadata : RegisteredTransformMetadata =
        RegisteredTransformMetadata.adapter "fullExportHydration" Data
            [ TransformSite.dataIntent "staticRowHydration"
                "Stream static-entity rows from the live OSSYS source (Ingestion.collectInOrderFor, scoped to static-marked kinds — never ReadSide.read) and graft them onto the catalog's Static populations before the data lanes render. The model.ossys connection ref may be env: or file: (both hydrate identically). Observation only — the rows are what the source holds; no operator opinion enters. A model with no live OSSYS source (read from model.path) skips with the named data.hydration.skippedNoLiveSource diagnostic."
              TransformSite.dataIntent "bootstrapRowHydration"
                "Stream the Bootstrap lane's rows from the live OSSYS source (Ingestion.collectInOrderFor, scoped per DataComposition — every data-bearing kind under AllData, the complement of Static ∪ Migration under AllRemaining/AllExceptStatic; never ReadSide.read) into the Map<SsKey, StaticRow list> the BootstrapEmitter renders. Observation only — the rows are what the source holds; no operator opinion enters. Disjoint from the Static lane (the partition law). Data-off / file-sourced ⇒ the empty Map (named skip in diagnostics)." ]
