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
    /// (pure; surfaced by `runWithConfigCore`). Data on + a file-sourced model
    /// (no `model.ossys`) ⇒ a NAMED skip — never silent emptiness: the static
    /// lanes can only emit catalog-resident rows because there is no live
    /// source to hydrate from. OSSYS-sourced or data-off ⇒ no diagnostic.
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
                        "data.hydration.skippedFileSourced"
                        "data emission is on but the model is file-sourced (model.path); static-entity rows cannot be hydrated from a live source, so the data lanes emit only catalog-resident populations. Set model.ossys to hydrate from the live source." ]
                | None -> []

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
    /// File-sourced (no `model.ossys`) ⇒ identity (the skip is named in
    /// `diagnostics`, never silent). OSSYS-sourced ⇒ open a SECOND connection
    /// (the model-read connection is use-disposed, not reusable) and
    /// stream+graft. The OSSYS branch mirrors `LiveModelRead.fromConnSpecWith`'s
    /// open template.
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

    /// Registry metadata (pillar 9). Read-only observation — DataIntent
    /// (mirrors the OSSYS `CatalogReader` / Transfer `Ingestion` adapters).
    let registeredMetadata : RegisteredTransformMetadata =
        RegisteredTransformMetadata.adapter "fullExportHydration" Data
            [ TransformSite.dataIntent "staticRowHydration"
                "Stream static-entity rows from the live OSSYS source (Ingestion.collectInOrderFor, scoped to static-marked kinds — never ReadSide.read) and graft them onto the catalog's Static populations before the data lanes render. Observation only — the rows are what the source holds; no operator opinion enters. A file-sourced model skips with the named data.hydration.skippedFileSourced diagnostic." ]
