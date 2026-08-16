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
    /// `ModelFile` is the `osm_model.json` fallback; `SinkWitness` (the
    /// data-sink chapter, S7) is the witnessed-state form — the sink store's
    /// latest (or pinned) sync for a named environment, offline-true.
    type ModelOrigin =
        | LiveOssys of conn: string
        | ModelFile of path: string
        | SinkWitness of env: string * syncId: SyncOrdinal option

    /// The full selection — pure, total (the data-sink chapter, S7).
    /// `offline = true` pins away from the wire: a live-OSSYS read is
    /// forbidden, so the sink env (preferred — the witnessed estate) or the
    /// model file (a file is already offline) serves, and neither configured
    /// is the named offline refusal. Online, the standing primary/fallback
    /// order (live OSSYS > model file) gains the sink as the LAST fallback —
    /// a witnessed state serves only when nothing live or authored is
    /// configured, so no existing resolution changes shape.
    let chooseOriginWith
        (offline: bool)
        (sinkEnv: (string * SyncOrdinal option) option)
        (modelOssys: string option)
        (modelFile: string option)
        : Result<ModelOrigin> =
        if offline then
            match sinkEnv, modelFile with
            | Some (env, syncId), _ -> Result.success (SinkWitness (env, syncId))
            | None, Some path       -> Result.success (ModelFile path)
            | None, None ->
                Result.failureOf
                    (ValidationError.create
                        "model.offline.noSource"
                        "offline model resolution: name a witnessed environment (`sink:<env>`) or configure `model` (osm_model.json file) — a live OSSYS read is pinned away under --offline.")
        else
            match modelOssys, modelFile, sinkEnv with
            | Some conn, _, _          -> Result.success (LiveOssys conn)
            | None, Some path, _       -> Result.success (ModelFile path)
            | None, None, Some (e, s)  -> Result.success (SinkWitness (e, s))
            | None, None, None ->
                Result.failureOf
                    (ValidationError.create
                        "model.noSource"
                        "no model source: configure `modelOssys` (live OSSYS — primary) or `model` (osm_model.json file — fallback).")

    /// The standing two-source selection — live OSSYS wins when configured;
    /// otherwise the model file; neither configured is a named refusal (total
    /// decisions, no silent default). The full form (`chooseOriginWith`)
    /// carries the offline pin and the sink fallback.
    let chooseOrigin (modelOssys: string option) (modelFile: string option) : Result<ModelOrigin> =
        chooseOriginWith false None modelOssys modelFile

    /// Resolve a chosen origin to its `Catalog` — every arm through the one
    /// `Source` port (recon #13), so there is one port for "where a catalog
    /// comes from": `ofOssys` (live), `Compose.read` (the authored file),
    /// `ofSink` (the witnessed state).
    let resolveOrigin (origin: ModelOrigin) : Task<Result<Catalog>> =
        task {
            match origin with
            | ModelFile path -> return! Compose.read path
            | LiveOssys connSpec -> return! Source.read (Source.ofOssys connSpec)
            | SinkWitness (env, syncId) -> return! Source.read (Source.ofSink env syncId)
        }

    /// Resolve the model to a `Catalog` under the primary/fallback policy.
    /// `Some modelOssys` → live OSSYS (primary, via `LiveModelRead`); else
    /// `Some modelFile` → the `osm_model.json` file (fallback); neither → a
    /// named refusal.
    let resolveCatalog (modelOssys: string option) (modelFile: string option) : Task<Result<Catalog>> =
        task {
            match chooseOrigin modelOssys modelFile with
            | Error es -> return Result.failure es
            | Ok origin -> return! resolveOrigin origin
        }
