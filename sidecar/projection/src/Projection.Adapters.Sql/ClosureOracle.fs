namespace Projection.Adapters.Sql
// LINT-ALLOW-FILE-MUTATION: the closure BFS (worklist + fuel + accumulators) is a sealed function-local imperative traversal at the SQL seam; the closure is assembled once then returned immutably

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

    /// Resolve a logical attribute `Name` to its physical, bracket-encoded
    /// column against THIS catalog's `kind` — the cross-environment plane
    /// crossing (each side resolves physical names via its own catalog).
    let private physicalOf (kind: Kind) (col: Name) : string =
        let encode = Microsoft.SqlServer.TransactSql.ScriptDom.Identifier.EncodeIdentifier
        kind.Attributes
        |> List.tryFind (fun a -> a.Name = col)
        |> Option.map (fun a -> encode (ColumnRealization.columnNameText a.Column))
        |> Option.defaultWith (fun () ->
            failwithf "ClosureOracle.renderPredicate: kind %A has no attribute %A" kind.SsKey col)

    /// Render a typed `Predicate` to a `(whereSql, addParams)` pair against the
    /// kind — logical columns resolved to physical, values bound as PARAMETERS
    /// (no literal injection). The `Raw` arm is a verbatim escape hatch
    /// (LINT-ALLOW). `All`/empty `And` → `1 = 1`; empty `In` → `1 = 0`.
    let renderPredicate (kind: Kind) (p: Predicate) : string * (SqlCommand -> unit) =
        // Predicate values are always raw strings (the catalog's value form);
        // bound as parameters. Stored as string (non-null) to satisfy F#9
        // nullness on `AddWithValue`'s `obj` argument.
        let pars = System.Collections.Generic.List<string * string>()
        let nextName () = System.String.Concat("@p", string pars.Count)  // LINT-ALLOW: terminal SQL-text boundary; parameter placeholder token
        let rec go (p: Predicate) : string =
            match p with
            | Predicate.All -> "1 = 1"  // LINT-ALLOW: terminal SQL-text boundary; constant-true predicate
            | Predicate.Raw sql -> sql  // LINT-ALLOW: terminal SQL-text boundary; operator-supplied raw-predicate escape hatch
            | Predicate.Equals (c, v) ->
                let nm = nextName () in pars.Add(nm, v)
                System.String.Concat(physicalOf kind c, " = ", nm)  // LINT-ALLOW: terminal SQL-text boundary; column encoded, value parameterized
            | Predicate.In (c, vs) ->
                if List.isEmpty vs then "1 = 0"  // LINT-ALLOW: terminal SQL-text boundary; constant-false predicate for an empty set
                else
                    let names = vs |> List.map (fun v -> let nm = nextName () in pars.Add(nm, v); nm)
                    System.String.Concat(physicalOf kind c, " IN (", String.concat ", " names, ")")  // LINT-ALLOW: terminal SQL-text boundary; column encoded, values parameterized
            | Predicate.And ps ->
                if List.isEmpty ps then "1 = 1"  // LINT-ALLOW: terminal SQL-text boundary; constant-true predicate
                else
                    ps
                    |> List.map (fun sub -> System.String.Concat("(", go sub, ")"))
                    |> String.concat " AND "  // LINT-ALLOW: terminal SQL-text boundary; sub-predicates already bounded/parameterized
        let whereSql = go p
        let addParams (cmd: SqlCommand) =
            for (nm, v) in pars do cmd.Parameters.AddWithValue(nm, v) |> ignore
        whereSql, addParams

    /// Resolve a logical `EntityCoordinate` to a `Kind` in this catalog —
    /// matching by entity `Name` (the cross-environment bridge) OR the physical
    /// table name (so an operator can address a root by logical entity on an
    /// OSSYS source or by physical table on a raw-DB readback). Module
    /// disambiguation is deferred (the single-module / live-readback case);
    /// returns the first match.
    let resolveEntity (catalog: Catalog) (coord: EntityCoordinate) : Kind option =
        Catalog.allKinds catalog
        |> List.tryFind (fun k ->
            Name.value k.Name = coord.Entity || TableId.tableText k.Physical = coord.Entity)

    /// Fetch the ROOT rows of `kind` selected by a typed `Predicate` — the
    /// "use case" seed (`Orders WHERE Region = 'West'`). The predicate is
    /// pushed to SQL (logical → physical) and read live.
    let fetchRootsByPredicate (cnn: SqlConnection) (kind: Kind) (predicate: Predicate) : Task<Closure.FetchedRows> =
        task {
            let whereSql, addParams = renderPredicate kind predicate
            let stream =
                ReadSide.readRowsWhereStream cnn kind whereSql addParams
                |> ReadSide.materializeStream kind
            let! rows = AsyncStream.toList stream
            return ({ Kind = kind.SsKey; Rows = rows } : Closure.FetchedRows)
        }

    /// Drive the closure to its referential fixed point from a set of already-
    /// fetched root rows, under the slice's traversal `directives` (`Stop`
    /// frontiers are not expanded), reading parents live via `fetch`. Bounded
    /// by a hard hop cap (the runaway backstop). Returns the closed state; the
    /// caller derives `Closure.materialize` / `Closure.reportWith` from it.
    ///
    /// The `walkWhere` form adds a caller-supplied fetch FILTER (2026-07-10, the
    /// csv-destination program): every planner-emitted fetch passes through
    /// `keep` before it is executed, so a caller can hold the walk back from
    /// whole KINDS by SsKey — the csv export skips static reference tables
    /// this way. Termination holds because the pure planner already recorded
    /// the filtered keys as Requested (they are never re-demanded), and a
    /// filtered kind never enters the closed row set. `walk` below is the
    /// keep-everything specialization — byte-identical to its pre-filter self.
    let walkWhere (keep: Closure.RowKeyFetch -> bool) (cnn: SqlConnection) (sourceCatalog: Catalog) (directives: RelationshipDirective list) (roots: Closure.FetchedRows list) : Task<Result<Closure.ClosureState>> =
        task {
            let mutable state = Closure.empty
            let mutable pending = roots
            let mutable fuel = 100000
            let mutable running = true
            let mutable refusal : ValidationError option = None
            while running do
                // Pure planner: fold the fetched rows in, plan the next hop's
                // parent fetches (honouring `Stop` frontiers). Empty IS the
                // fixed point.
                let state', planned = Closure.stepWith directives sourceCatalog state pending
                let fetches = List.filter keep planned
                state <- state'
                if List.isEmpty fetches then
                    running <- false
                elif fuel <= 0 then
                    // The runaway backstop: a reachable operational outcome (a
                    // pathological reference graph), so it is a NAMED refusal on
                    // the report, not an unstructured `failwith`. A mutable flag
                    // (not a nested match in the loop) keeps the `task` block
                    // FS3511-safe under Release static compilation.
                    refusal <-
                        Some (ValidationError.create
                                "closure.fuelExhausted"
                                "closure walk did not reach a fixed point within the hop cap")
                    running <- false
                else
                    let mutable fetched : Closure.FetchedRows list = []
                    for fc in fetches do
                        let! fr = fetch cnn sourceCatalog fc
                        fetched <- fr :: fetched
                    pending <- fetched
                    fuel <- fuel - 1
            match refusal with
            | Some es -> return Result.failureOf es
            | None    -> return Result.success state
        }

    let walk (cnn: SqlConnection) (sourceCatalog: Catalog) (directives: RelationshipDirective list) (roots: Closure.FetchedRows list) : Task<Result<Closure.ClosureState>> =
        walkWhere (fun _ -> true) cnn sourceCatalog directives roots
