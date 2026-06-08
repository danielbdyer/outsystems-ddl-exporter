namespace Projection.Pipeline

open System.Threading.Tasks
open Projection.Core

/// Where the model (schema B) is read from, and the **primary/fallback**
/// policy between the two sources (V1_INPUT_DEPRECATION.md §3).
///
/// **Live OSSYS is primary; the `osm_model.json` file is the optional
/// fallback.** When a live OSSYS connection is configured (`modelOssys`), the
/// model is read directly from the OutSystems metadata database via
/// `LiveModelRead` — V1-free, native GUID SsKey. The authored `osm_model.json`
/// file remains a configured fallback for cutover safety — not retired.
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

    /// Resolve the model to a `Catalog` under the primary/fallback policy.
    /// `Some modelOssys` → live OSSYS (primary, via `LiveModelRead`); else
    /// `Some modelFile` → the `osm_model.json` file (fallback); neither → a
    /// named refusal.
    let resolveCatalog (modelOssys: string option) (modelFile: string option) : Task<Result<Catalog>> =
        task {
            match chooseOrigin modelOssys modelFile with
            | Error es -> return Result.failure es
            | Ok (ModelFile path) -> return! Compose.read path
            | Ok (LiveOssys connSpec) -> return! LiveModelRead.fromConnSpec connSpec
        }
