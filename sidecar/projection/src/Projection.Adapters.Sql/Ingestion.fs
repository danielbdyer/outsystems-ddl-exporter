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
    /// leg, named in Transfer vocabulary). Quanta are positional against
    /// `Kind.rowBasis kind` (Q2 — the in-flight carrier).
    let streamKind (cnn: SqlConnection) (kind: Kind) : AsyncStream<RowQuantum> =
        ReadSide.readRowsStream cnn kind

    /// Stream one kind's rows rebuilt at the IR grain (`StaticRow`, Map +
    /// `READSIDE_ROW` identity minted per row) — the materialized-scale
    /// boundary for consumers that hold whole row sets (reconcile reads,
    /// preview/canary collection). The streaming realization consumes
    /// `streamKind` directly and never pays this conversion.
    let streamKindRows (cnn: SqlConnection) (kind: Kind) : AsyncStream<StaticRow> =
        streamKind cnn kind |> ReadSide.materializeStream kind

    // `streamsInOrder` (per-kind streams in topological order) was deleted
    // here (Q3, 2026-06-12): its single consumer was `collectInOrder`, which
    // now converts at the IR-grain boundary directly, and the streaming
    // realization streams per kind via `streamKind` inside its own
    // chunk loop — zero consumers remained (the dead-algebra precedent,
    // DECISIONS 2026-06-04). Re-introduce per the two-consumer threshold.

    /// Materialize every kind's rows into the `Map<SsKey, StaticRow list>`
    /// that the pure `TransferPlan.build` consumes — the materialized
    /// path's SINGLE conversion point back to the IR grain (Q2: Map +
    /// Identifier minted here, via `streamKindRows`). Reads each kind in
    /// topological order, one open reader at a time (Source-friendly). For
    /// preview / canary scale; the streaming realization consumes
    /// `streamsInOrder` directly without materializing.
    let collectInOrder
        (cnn: SqlConnection)
        (catalog: Catalog)
        (topo: TopologicalOrder)
        : Task<Map<SsKey, StaticRow list>> =
        // Tail-recursive task continuation rather than `for … do let! …` over a
        // mutable accumulator: the loop-with-`let!` shape is not statically
        // compilable under Release optimization (FS3511 → the dynamic, slower
        // state machine), so it is restructured into the statically-compilable
        // recursive form (the codebase's standing posture on FS3511; cf.
        // `Pipeline.runWithConfigCore` / `Preflight`).
        let rec loop
            (acc: Map<SsKey, StaticRow list>)
            (remaining: (SsKey * AsyncStream<StaticRow>) list)
            : Task<Map<SsKey, StaticRow list>> =
            task {
                match remaining with
                | [] -> return acc
                | (key, stream) :: rest ->
                    let! rows = AsyncStream.toList stream
                    return! loop (Map.add key rows acc) rest
            }
        let rowStreams =
            topo.Order
            |> List.choose (fun key ->
                Catalog.tryFindKind key catalog
                |> Option.map (fun k -> key, streamKindRows cnn k))
        loop Map.empty rowStreams

    /// Registry metadata (pillar 9). The ingestion adapter leg classifies
    /// entirely as `DataIntent` — lifting a substrate's rows is observation,
    /// not operator opinion (mirrors the OSSYS `CatalogReader` adapter).
    let registeredMetadata : RegisteredTransformMetadata =
        RegisteredTransformMetadata.adapter "transferIngestion" Data
            [ TransformSite.dataIntent "rowStreamRead"
                "Lift each kind's rows from the Source substrate via ReadSide.readRowsStream, mapping columns positionally onto attribute Names. Observation only — the rows are what the Source holds; no operator opinion enters."
              TransformSite.dataIntent "topologicalStreamOrder"
                "Stream kinds in the precomputed TopologicalOrder (FK-safe, dependency-first) so a two-phase load consumes them in order. The order derives from the catalog's FK graph; no operator-supplied ordering at this site." ]
