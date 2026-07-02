namespace Projection.Pipeline

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Adapters.Sql
open Projection.Adapters.Osm
open Projection.Adapters.OssysSql

/// The V1-free **live OSSYS model read** primitive — the shared core behind
/// `ModelResolution` (flow surface) and `Compose.readConfigModel` (full-export
/// surface). The connection-spec decode + open now flow through the one
/// `ConnectionSpec.openSpec` opener (recon #13 — compiled before this), so this
/// module carries no bespoke connection-ref decode of its own.
///
/// Reads the OutSystems model directly from a live OSSYS database: V2's own
/// `MetadataSnapshotRunner` (over V2's carbon-copied rowset SQL) → `RowsetBundle`
/// → `CatalogReader.parse (SnapshotRowsets …)` → `Catalog` with **native GUID
/// SsKey** (A1-stable). No V1 chain, no `osm_model.json`.
[<RequireQualifiedAccess>]
module LiveModelRead =

    /// Read the model from an already-open OSSYS connection under the
    /// supplied scope parameters: snapshot → bundle → Catalog (native
    /// SsKey). Reconciliation slice 4 (DECISIONS 2026-06-13) — the
    /// scope-bearing face; `SnapshotScopeBinding.fromModel` derives the
    /// parameters from the operator's `model.modules` declaration, and
    /// `ModuleFilter.apply` remains the semantic seam downstream.
    let fromConnectionWith
        (parameters: MetadataSnapshotRunner.SnapshotParameters)
        (cnn: SqlConnection)
        : Task<Result<Catalog>> =
        task {
            match! MetadataSnapshotRunner.runAsync cnn parameters with
            | Error es -> return Result.failure es
            | Ok snapshot ->
                // F9 (audit 2026-06-17) — surface, never silently discard, every
                // logical-vs-deployed `#ColumnReality` divergence the snapshot
                // carries (the adapter keeps the LOGICAL value; the operator is
                // told so they can confirm which source is authoritative). The
                // carried value is unchanged — diagnostic only, no auto-resolve.
                for d in MetadataSnapshotRunner.columnRealityDivergences snapshot do
                    // LINT-ALLOW: operator-facing boundary warning (channel 2),
                    // sibling to the tightening-relax acknowledgment.
                    eprintfn "%s: %s" d.Code d.Message
                // PK identity carries twice in OSSYS (attribute flag +
                // entity key); the reader recovers from a missing flag via
                // the entity key but never resolves a contradiction — that
                // is named here.
                for d in MetadataSnapshotRunner.primaryKeyDivergences snapshot do
                    // LINT-ALLOW: operator-facing boundary warning (channel 2).
                    eprintfn "%s: %s" d.Code d.Message
                let bundle = MetadataSnapshotRunner.toBundle snapshot
                // Slice 4 — under a pushed scope, prune reference rows
                // whose target entity the server-side narrowing excluded
                // (the cross-scope edges). `ModuleFilter.apply` applies
                // the SAME semantic in memory (its step 5), which is the
                // pushdown ≡ filter equivalence law. Rows whose
                // `RefEntityId` is unknown (`None`) are kept — a truly
                // dangling one still fails loudly at `Catalog.create`,
                // preserving the corrupt-source posture for full reads.
                let scoped =
                    if List.isEmpty parameters.ModuleNames then bundle
                    else
                        let kindIds =
                            bundle.Kinds
                            |> List.map (fun k -> k.EntityId)
                            |> Set.ofList
                        { bundle with
                            References =
                                bundle.References
                                |> List.filter (fun r ->
                                    match r.RefEntityId with
                                    | Some id -> Set.contains id kindIds
                                    | None    -> true) }
                return! CatalogReader.parse (CatalogReader.SnapshotRowsets scoped)
        }

    /// Read the model from an already-open OSSYS connection: snapshot → bundle
    /// → Catalog (native SsKey). The show-me-everything stance
    /// (`defaultParameters`) — the canary/baseline face.
    let fromConnection (cnn: SqlConnection) : Task<Result<Catalog>> =
        fromConnectionWith MetadataSnapshotRunner.defaultParameters cnn

    /// Read the model live from a connection spec under the supplied scope
    /// parameters: open (Source role, through the one `ConnectionSpec.openSpec`)
    /// → `fromConnectionWith`. Accepts every spec form uniformly (`env:` /
    /// `file:` / `live:` / bare — recon #13, D9 amended 2026-06-28); `env:` /
    /// `file:` remain the recommended out-of-band form.
    let fromConnSpecWith
        (parameters: MetadataSnapshotRunner.SnapshotParameters)
        (connSpec: string)
        : Task<Result<Catalog>> =
        task {
            match! ConnectionSpec.openSpec SubstrateRole.Source "ossys-model-source" connSpec with
            | Error es -> return Result.failure es
            | Ok cnn ->
                use cnn = cnn
                return! fromConnectionWith parameters cnn
        }

    /// Read the model live from a connection reference: parse → open (Source
    /// role) → `fromConnection`.
    let fromConnSpec (connSpec: string) : Task<Result<Catalog>> =
        fromConnSpecWith MetadataSnapshotRunner.defaultParameters connSpec
