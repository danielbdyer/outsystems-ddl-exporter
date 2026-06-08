namespace Projection.Pipeline

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Adapters.Sql
open Projection.Adapters.Osm
open Projection.Adapters.OssysSql

/// The V1-free **live OSSYS model read** primitive — the shared core behind
/// `ModelResolution` (flow surface) and `Compose.readConfigModel` (full-export
/// surface). Compiled first in the Pipeline project (no intra-project
/// dependencies) so both consumers can reach it regardless of compile order.
///
/// Reads the OutSystems model directly from a live OSSYS database: V2's own
/// `MetadataSnapshotRunner` (over V2's carbon-copied rowset SQL) → `RowsetBundle`
/// → `CatalogReader.parse (SnapshotRowsets …)` → `Catalog` with **native GUID
/// SsKey** (A1-stable). No V1 chain, no `osm_model.json`.
[<RequireQualifiedAccess>]
module LiveModelRead =

    /// Parse a connection reference (`env:<var>` / `file:<path>`) — the
    /// out-of-band credential pointer (D9), never the secret. Inlined here so
    /// this primitive carries no intra-Pipeline dependency.
    let parseConnRef (spec: string) : Result<ConnectionRef> =
        let trimmed = (spec: string).Trim()
        if trimmed.StartsWith "env:" then Result.success (ConnectionRef.EnvVar (trimmed.Substring 4))
        elif trimmed.StartsWith "file:" then Result.success (ConnectionRef.File (trimmed.Substring 5))
        else
            Result.failureOf
                (ValidationError.create
                    "model.ossys.connRef"
                    (sprintf "model OSSYS connection '%s' must be an out-of-band reference (env:<var> or file:<path>)." spec))

    /// Read the model from an already-open OSSYS connection: snapshot → bundle
    /// → Catalog (native SsKey).
    let fromConnection (cnn: SqlConnection) : Task<Result<Catalog>> =
        task {
            match! MetadataSnapshotRunner.runAsync cnn MetadataSnapshotRunner.defaultParameters with
            | Error es -> return Result.failure es
            | Ok snapshot ->
                let bundle = MetadataSnapshotRunner.toBundle snapshot
                return! CatalogReader.parse (CatalogReader.SnapshotRowsets bundle)
        }

    /// Read the model live from a connection reference: parse → open (Source
    /// role) → `fromConnection`.
    let fromConnSpec (connSpec: string) : Task<Result<Catalog>> =
        task {
            match parseConnRef connSpec with
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
                    return! fromConnection cnn
        }
