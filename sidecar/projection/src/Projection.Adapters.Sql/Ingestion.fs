namespace Projection.Adapters.Sql

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core

/// The reader leg of the Transfer adjunction — `Projection`'s named peer.
/// Lifts a Source substrate's rows back into `StaticRow`s, per kind, in
/// FK-safe (topological) order: the order a two-phase load consumes them.
/// A thin composition over `ReadSide.readRowsStream` in the Transfer
/// vocabulary. Slice B of the Transfer epic — see `PRESCOPE_TRANSFER.md`
/// §9 seam 2, §10.
[<RequireQualifiedAccess>]
module Ingestion =

    /// Stream one kind's rows from the Source connection (the row-reader
    /// leg, named in Transfer vocabulary).
    let streamKind (cnn: SqlConnection) (kind: Kind) : AsyncStream<StaticRow> =
        ReadSide.readRowsStream cnn kind

    /// Per-kind row streams in topological order. Kinds in the order but
    /// absent from the catalog are skipped. The streams are lazy — nothing
    /// is read until a consumer pulls.
    let streamsInOrder
        (cnn: SqlConnection)
        (catalog: Catalog)
        (topo: TopologicalOrder)
        : (SsKey * AsyncStream<StaticRow>) list =
        topo.Order
        |> List.choose (fun key ->
            Catalog.tryFindKind key catalog
            |> Option.map (fun k -> key, streamKind cnn k))

    /// Materialize every kind's rows into the `Map<SsKey, StaticRow list>`
    /// that the pure `TransferPlan.build` consumes. Reads each kind in
    /// topological order, one open reader at a time (Source-friendly). For
    /// preview / canary scale; a streaming realization (Slice C) consumes
    /// `streamsInOrder` directly without materializing.
    let collectInOrder
        (cnn: SqlConnection)
        (catalog: Catalog)
        (topo: TopologicalOrder)
        : Task<Map<SsKey, StaticRow list>> =
        task {
            let mutable acc = Map.empty
            for (key, stream) in streamsInOrder cnn catalog topo do
                let! rows = AsyncStream.toList stream
                acc <- Map.add key rows acc
            return acc
        }
