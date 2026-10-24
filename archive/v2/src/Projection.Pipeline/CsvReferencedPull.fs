namespace Projection.Pipeline

open Projection.Core

/// THE REFERENCED PULL (2026-07-10, the csv-destination program): the pure
/// vocabulary that turns a declared export subset into the seeds and the
/// filter of a referential-closure walk over the SOURCE — so a CSV export
/// can optionally carry the rows its foreign keys point at, transitively
/// closed, while STATIC reference tables stay out (their content is
/// environment-identical by declaration; exporting them adds bytes, not
/// information).
///
/// The seeding trick: the subset's own ingested tables ARE the walk's roots.
/// `Closure.stepWith` derives the next fetches from every populated foreign
/// key of the rows it holds — for the subset's rows those are exactly the
/// escaping edges' distinct key values, while in-subset references resolve
/// against the already-closed rows (per-key dedup) and fetch nothing. No
/// separate "distinct escaping keys" computation exists to drift from the
/// closure's own.
///
/// The static test reads MODALITY MEMBERSHIP (`ModalityMark.Static _`),
/// never `Kind.staticPopulations <> []`: the OSSYS metamodel readers mark a
/// static entity `Static []` (flag only, no populations), and a ReadSide
/// readback marks EVERYTHING static (survival rule 8) — so the contract this
/// module is given must come from the OSSYS metamodel, and the test must not
/// lean on populations being present.
[<RequireQualifiedAccess>]
module CsvReferencedPull =

    /// The kinds the contract declares static.
    let staticKinds (contract: Catalog) : Set<SsKey> =
        Catalog.allKinds contract
        |> List.filter (fun k ->
            k.Modality |> List.exists (function ModalityMark.Static _ -> true | _ -> false))
        |> List.map (fun k -> k.SsKey)
        |> Set.ofList

    /// The walk's fetch filter: a fetch aimed at a static kind is dropped —
    /// the planner has already recorded its keys as requested, so the demand
    /// never recurs and the kind never enters the closed set.
    let keepFetch (statics: Set<SsKey>) (f: Closure.RowKeyFetch) : bool =
        not (Set.contains f.Kind statics)

    /// The subset's ingested tables as the walk's roots.
    let rootsOf (ingested: Map<SsKey, StaticRow list>) : Closure.FetchedRows list =
        ingested
        |> Map.toList
        |> List.map (fun (k, rows) -> ({ Kind = k; Rows = rows } : Closure.FetchedRows))

    /// The pulled tables: everything the closure closed over MINUS the
    /// declared subset (statics never entered — the filter held them out).
    let pulledRows (loadSet: Set<SsKey>) (closed: Map<SsKey, StaticRow list>) : Map<SsKey, StaticRow list> =
        closed |> Map.filter (fun k _ -> not (Set.contains k loadSet))
