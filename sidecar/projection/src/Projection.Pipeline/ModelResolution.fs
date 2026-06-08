namespace Projection.Pipeline

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Adapters.Sql
open Projection.Adapters.Osm
open Projection.Adapters.OssysSql

/// Where the model (schema B) is read from, and the **primary/fallback**
/// policy between the two sources (V1_INPUT_DEPRECATION.md §3).
///
/// **Live OSSYS is primary; the `osm_model.json` file is the optional
/// fallback.** When a live OSSYS connection is configured (`modelOssys`), the
/// model is read directly from the OutSystems metadata database — V2's own
/// `MetadataSnapshotRunner` over V2's carbon-copied rowset SQL → `RowsetBundle`
/// → `CatalogReader.parse (SnapshotRowsets …)` → `Catalog` with **native
/// SsKey** (no V1 chain, no `osm_model.json`, and A1-stable identity that the
/// JSON path only name-synthesizes). The authored `osm_model.json` file
/// remains a configured fallback for cutover safety — not retired.
[<RequireQualifiedAccess>]
module ModelResolution =

    /// The resolved source of the model. `LiveOssys` is the V1-free primary;
    /// `ModelFile` is the `osm_model.json` fallback.
    type ModelOrigin =
        | LiveOssys of conn: string
        | ModelFile of path: string

    /// The primary/fallback selection — pure. Live OSSYS wins when configured;
    /// otherwise the model file; neither configured is a named refusal (total
    /// decisions, no silent default).
    let chooseOrigin (modelOssys: string option) (modelFile: string option) : Result<ModelOrigin> =
        match modelOssys, modelFile with
        | Some conn, _    -> Result.success (LiveOssys conn)
        | None, Some path -> Result.success (ModelFile path)
        | None, None ->
            Result.failureOf
                (ValidationError.create
                    "model.noSource"
                    "no model source: configure `modelOssys` (live OSSYS — primary) or `model` (osm_model.json file — fallback).")

    /// Read the model live from an already-open OSSYS connection — V1-free:
    /// metadata snapshot → rowset bundle → `Catalog` (native SsKey). The
    /// connection-taking core so the read is testable against a bootstrapped
    /// OSSYS database without a connection-spec string.
    let resolveFromConnection (cnn: SqlConnection) : Task<Result<Catalog>> =
        task {
            match! MetadataSnapshotRunner.runAsync cnn MetadataSnapshotRunner.defaultParameters with
            | Error es -> return Result.failure es
            | Ok snapshot ->
                let bundle = MetadataSnapshotRunner.toBundle snapshot
                return! CatalogReader.parse (CatalogReader.SnapshotRowsets bundle)
        }

    /// Resolve the model to a `Catalog` under the primary/fallback policy.
    /// `Some modelOssys` → live OSSYS (primary); else `Some modelFile` → the
    /// `osm_model.json` file (fallback); neither → a named refusal.
    let resolveCatalog (modelOssys: string option) (modelFile: string option) : Task<Result<Catalog>> =
        task {
            match chooseOrigin modelOssys modelFile with
            | Error es -> return Result.failure es
            | Ok (ModelFile path) -> return! Compose.read path
            | Ok (LiveOssys connSpec) ->
                match TransferSpec.parseConnectionSpec connSpec with
                | Error es -> return Result.failure es
                | Ok connRef ->
                    let sub : Substrate =
                        { Environment   = Environment.Named "ossys-model-source"
                          Role          = SubstrateRole.Source
                          ConnectionRef = connRef }
                    match! ConnectionResolver.openSubstrate sub with
                    | Error es -> return Result.failure es
                    | Ok cnn ->
                        use cnn = cnn
                        return! resolveFromConnection cnn
        }
