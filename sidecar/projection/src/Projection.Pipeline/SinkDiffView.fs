namespace Projection.Pipeline

open System.Threading.Tasks
open Projection.Core
open Projection.Adapters.Osm
open Projection.Adapters.OssysSql

/// The Catalog-grain view of a sink displacement window (the data-sink
/// chapter, S10). Parse BOTH witnessed editions through the exact pipeline
/// every sink read rides (`toBundle` → `CatalogReader.parse`) and diff at
/// the catalog grain (`CatalogDiff.between`) — a derived VIEW over the
/// journal, never a second truth: `CatalogDiff` is a projection of the
/// acquisition-grain displacement stream, and the erasure-witness
/// inequality (T19's companion law) bounds the view's norm by the
/// journal's own entries for the window —
///
///     CatalogDiff.norm (view a b)  ≤  SinkDisplacement.norm (diff a b)
///
/// (a catalog-visible change is carried by at least one rowset-row
/// displacement; rowset changes the catalog projection folds away — the
/// named erasures — cost journal entries with no view counterpart, never
/// the reverse). This is what makes `diff sink:e@a sink:e@b` (S13) honest:
/// the human-shaped diff can UNDERSTATE the journal, never invent.
[<RequireQualifiedAccess>]
module SinkDiffView =

    /// Parse one witnessed edition to its catalog — the sink read pipeline
    /// minus the store walk (`SinkRead.readCatalog` is the store-addressed
    /// sibling; this one takes the snapshot the caller already holds).
    let catalogOf (snapshot: MetadataSnapshotRunner.MetadataSnapshot) : Task<Result<Catalog>> =
        CatalogReader.parse (CatalogReader.SnapshotRowsets (MetadataSnapshotRunner.toBundle snapshot))

    /// The Catalog-grain diff between two witnessed editions.
    let catalogDiffOf
        (a: MetadataSnapshotRunner.MetadataSnapshot)
        (b: MetadataSnapshotRunner.MetadataSnapshot)
        : Task<Result<CatalogDiff>> =
        task {
            match! catalogOf a with
            | Error es -> return Result.failure es
            | Ok ca ->
                match! catalogOf b with
                | Error es -> return Result.failure es
                | Ok cb -> return Result.success (CatalogDiff.between ca cb)
        }
