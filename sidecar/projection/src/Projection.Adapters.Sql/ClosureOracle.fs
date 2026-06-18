namespace Projection.Adapters.Sql

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core

/// Slice 1b — the adapter **oracle** that executes the pure `Closure` planner's
/// scoped fetches against a live SOURCE connection, plus the `walk` driver that
/// loops `Closure.step ∘ fetch` to the referential fixed point. This is the one
/// SQL seam the pure engine plans for (the `EvidenceCache`/`LiveProfiler`
/// "discover-once, derive-pure" shape).
///
/// **Cross-environment plane separation.** `fetch` reads the SOURCE environment
/// through the SOURCE catalog's physical names — `ReadSide.readRowsKeyedStream`
/// resolves the logical `KeyColumn` to its physical column against the source
/// `kind`. The closed row-set it returns is LOGICAL (attribute-`Name`-keyed),
/// so it bridges to ANY target whose schema is congruent by coordinate — the
/// eSpace-divergent-physical-name requirement (different physical names per
/// environment for the same logical entity).
[<RequireQualifiedAccess>]
module ClosureOracle =

    /// Max key values per `IN (…)` read; larger fetches chunk into several
    /// round-trips (the SQL Server practical IN-list / parameter ceiling). The
    /// pure planner already de-duplicates keys before they reach here.
    let private chunkSize = 900

    /// Execute one scoped fetch: read the source rows of `f.Kind` whose
    /// `f.KeyColumn` value is in `f.Keys`. Chunks the key set, concatenates the
    /// reads. A kind absent from the source catalog yields no rows (the pure
    /// `Closure.step` already excludes such fetches; total here for safety).
    let fetch (cnn: SqlConnection) (sourceCatalog: Catalog) (f: Closure.RowKeyFetch) : Task<Closure.FetchedRows> =
        task {
            match Catalog.tryFindKind f.Kind sourceCatalog with
            | None -> return { Kind = f.Kind; Rows = [] }
            | Some kind ->
                let chunks = f.Keys |> Set.toList |> List.chunkBySize chunkSize
                let mutable acc : StaticRow list = []
                for chunk in chunks do
                    let stream =
                        ReadSide.readRowsKeyedStream cnn kind f.KeyColumn chunk
                        |> ReadSide.materializeStream kind
                    let! rows = AsyncStream.toList stream
                    acc <- List.append rows acc
                return { Kind = f.Kind; Rows = acc }
        }

    /// Fetch the ROOT rows of `kind` by primary-key value — the closure's seed.
    /// (Predicate-scoped roots — `… WHERE <predicate>` — land in Slice 3/4;
    /// the foundation seeds by explicit key.)
    let fetchRootsByKey (cnn: SqlConnection) (sourceCatalog: Catalog) (kind: SsKey) (pkColumn: Name) (keys: Set<string>) : Task<Closure.FetchedRows> =
        fetch cnn sourceCatalog { Kind = kind; KeyColumn = pkColumn; Keys = keys }

    /// Drive the closure to its referential fixed point from a set of already-
    /// fetched root rows, reading parents live via `fetch`. Bounded by a hard
    /// hop cap (Slice 4 promotes this to the named `closure.fuelExhausted`
    /// refusal carried on the report). Returns the closed state; the caller
    /// derives `Closure.materialize` / `Closure.report` from it.
    let walk (cnn: SqlConnection) (sourceCatalog: Catalog) (roots: Closure.FetchedRows list) : Task<Closure.ClosureState> =
        task {
            let mutable state = Closure.empty
            let mutable pending = roots
            let mutable fuel = 100000
            let mutable running = true
            while running do
                // `Closure.step` is pure: fold the fetched rows in, plan the
                // next hop's parent fetches. An empty plan IS the fixed point.
                let state', fetches = Closure.step sourceCatalog state pending
                state <- state'
                if List.isEmpty fetches then
                    running <- false
                elif fuel <= 0 then
                    failwith "closure walk did not reach a fixed point (hop fuel exhausted)"
                else
                    let mutable fetched : Closure.FetchedRows list = []
                    for fc in fetches do
                        let! fr = fetch cnn sourceCatalog fc
                        fetched <- fr :: fetched
                    pending <- fetched
                    fuel <- fuel - 1
            return state
        }
