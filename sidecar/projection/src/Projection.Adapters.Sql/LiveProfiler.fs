namespace Projection.Adapters.Sql

// LINT-ALLOW-FILE: live SQL-profiling adapter at the boundary — terminal SQL-text emission
//   (String.Concat/Join/concat over typed encode-quoted segments), operator-
//   facing diagnostic prose, function-local mutables, and a mutable result
//   accumulator; the SQL probes emit terminal text at the DB boundary.

open System
open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core

/// Operator-supplied profile-capture options. Surfaces:
///   - `MaxRowsPerKind` — optional sampling cap. `None` = full-scan
///     (default); `Some N` = `SELECT TOP (N)` capped at extraction time.
///     Sampling is **deterministic** when the kind has a single-column PK
///     (the cache stream uses `ORDER BY <pk>` for repeatable extraction).
///   - `EnvironmentTag` — operator-supplied label for the environment being
///     profiled (dev / qa / uat / prod), used by the multi-env orchestrator
///     to label profiles before merge.
///
/// Per `DECISIONS 2026-05-18 (slice 5.4.δ.profiling)` — sampling policy is
/// operator intent: it lives in the orchestrator/options (this adapter
/// surface), not in the Profile IR. The cache substrate it fills is pure
/// Core (`EvidenceCache`); these capture options are the adapter's own,
/// so they stay here at the boundary where the SQL runs.
type SqlProfilerOptions = {
    MaxRowsPerKind  : int option
    EnvironmentTag  : string option
    /// Bounded parallelism for the per-kind discovery when the caller uses
    /// the connection-factory form (`captureEvidenceCacheConcurrent`) — how
    /// many kinds may run their 3-query discovery concurrently, each on its
    /// own pooled connection (`profiler.maxConcurrency`). Acquisition-only
    /// concurrency: the cache is keyed and every derived Profile axis is
    /// pure, so results never depend on completion order. The single-open-
    /// connection entry points (`captureEvidenceCacheWith` / `attach`)
    /// remain strictly serial regardless of this value.
    MaxConcurrency  : int
}

[<RequireQualifiedAccess>]
module SqlProfilerOptions =

    /// Default options: full-scan (no sampling cap), no environment tag,
    /// discovery concurrency 4 (low and explicit — the factory form only).
    let defaults : SqlProfilerOptions = {
        MaxRowsPerKind  = None
        EnvironmentTag  = None
        MaxConcurrency  = 4
    }

/// Live-SQL adapter that captures `Profile.AttributeRealities` by
/// probing a deployed SQL Server instance. The **live-probe** path —
/// runs deterministic SQL queries against the deployed target and
/// populates per-attribute reality evidence directly. This is the
/// canonical V2 profiling path: V2 reads + profiles the live database
/// itself, with no V1-produced JSON input (the prior `ProfileSnapshot`
/// / `ProfileStatistics` V1-JSON importers were retired 2026-06-08).
///
/// **Per matrix row 49 + V2_DRIVER per-axis stakes (DATA-axis
/// cutover-blocker).** V1's `LiveProfiler.cs` (chapter 5.4.δ
/// profiling territory) populates V1's `AttributeReality.cs` via
/// runtime probes; V2 mirrors at this adapter. Sibling-Π consumers
/// (tightening passes: `NullabilityRules`, `UniqueIndexRules`,
/// `ForeignKeyRules`) read `Profile.AttributeRealities` to refine
/// their decisions.
///
/// **Per pillar 9: all sites carry DataIntent.** The adapter
/// observes deployed reality; no operator policy enters at probe
/// time. Sampling policy (when V2 adopts non-full-table probing)
/// lives in `Pipeline.Config` per matrix row 90's prior decision
/// — `DECISIONS 2026-05-18 (slice 5.4.δ.profiling) — Sampling policy
/// is operator intent; lives in the orchestrator, not in Profile
/// IR`.
///
/// **Per A18 amended + A34.** Profile is independent of Catalog and
/// Policy; the LiveProfiler reads Catalog (to identify which
/// attributes to probe) but emits Profile evidence only — no
/// catalog mutation, no policy consumption. The Catalog ↔ deployed
/// schema alignment is the caller's contract (the canary's
/// PhysicalSchema diff gates this elsewhere).
///
/// **Architecture (post-recon-#5 — capture-here, derive-in-core).** This adapter is
/// now purely the `Ingest` boundary: the `attach` entry point reads a deployed SQL
/// Server via three queries per non-static kind — (1) aggregate query (exact RowCount
/// + per-attribute exact NullCount); (2) row-stream query (per-row values, with
/// optional sampling cap / deterministic ORDER BY); (3) nullability reflection via
/// `INFORMATION_SCHEMA.COLUMNS` — and folds them into the pure `EvidenceCache`
/// substrate (Core). From there `Projection.Core.ProfileDerivation` derives EVERY
/// Profile axis in pure F# with no further SQL — `AttributeRealities`, `Columns`,
/// `ForeignKeys` (HasOrphan + OrphanCount + composite-PK resolution), `UniqueCandidates`
/// + `CompositeUniqueCandidates`, numeric + categorical distributions, FK cardinalities
/// + selectivities + multi-FK joint distributions, plus the FK orphan-sample
/// `DiagnosticEntry` side-channel (operational samples land in Diagnostics, not a
/// Profile axis). `attach` simply captures the cache, then delegates to
/// `ProfileDerivation.attachFromCache`. The derivation suite is therefore pure-pool
/// testable against a hand-built cache, with no database.
///
/// **Retired surfaces (slice B.4.2, 2026-05-19).** The pre-cache
/// `capture*` public functions and their per-attribute /
/// per-Reference SQL probes (~720 LOC) retired with chapter B.4.2;
/// the EvidenceCache substrate fully subsumes their evidence at
/// 6000 → 900 SQL round-trips at 300-table production scale.
[<RequireQualifiedAccess>]
module LiveProfiler =

    // recon #8 — the one Core quoter (`SqlIdentifier.quote`, byte-verified
    // against ScriptDom's `Identifier.EncodeIdentifier`).
    let private encode = SqlIdentifier.quote

    /// Drain a reader's single result set, applying `onRow` per row. The
    /// profiler-local twin of `ReadSide.drainRows`: same shape, same
    /// per-row-effect contract, but `LiveProfiler` compiles before
    /// `ReadSide` (`Projection.Adapters.Sql.fsproj`), so the kernel cannot
    /// be shared — it is named once here instead of hand-rolled at each
    /// drain (CONSTELLATION_BACKLOG plane N4; the cross-file kernel is the
    /// §6 refusal, its wake a third drain consumer outside `ReadSide`).
    let private drainReader
        (reader: SqlDataReader)
        (onRow: SqlDataReader -> unit)
        : Task<unit> =
        task {
            let mutable hasMore = true
            while hasMore do
                let! more = reader.ReadAsync()
                if more then onRow reader
                else hasMore <- false
        }

    /// The CATALOG-WIDE nullability reflection — one round trip replacing
    /// N per-kind `INFORMATION_SCHEMA.COLUMNS` queries (the batched form;
    /// at ~300 kinds this removes ~1/3 of profile-stage round trips). The
    /// per-kind lookup key is upper-invariant `SCHEMA.TABLE`: the per-kind
    /// query matched (schema, table) under the SERVER's collation
    /// (typically case-insensitive), so the client-side re-match must be
    /// case-insensitive too — an ordinal miss would silently default a
    /// column to NOT NULL, a false tightening signal.
    let private batchedNullabilityReflectionSql : string =
        "SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, IS_NULLABLE \
         FROM INFORMATION_SCHEMA.COLUMNS \
         WHERE TABLE_SCHEMA NOT IN ('sys', 'INFORMATION_SCHEMA')"

    let private tableKeyOf (kind: Kind) : string =
        System.String.Concat(TableId.schemaText kind.Physical, ".", TableId.tableText kind.Physical).ToUpperInvariant()  // LINT-ALLOW: case-insensitive lookup-key composition for the batched reflection map; the key IS a string primitive at the ADO.NET boundary

    let private reflectNullabilityAll
        (cnn: SqlConnection)
        : Task<Map<string, Map<string, bool>>> =
        task {
            use _ = Bench.scope "profile.live.reflectNullability.batched"
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- batchedNullabilityReflectionSql
            cmd.CommandTimeout <- CommandTimeoutPolicy.resolve ()
            use! reader = cmd.ExecuteReaderAsync()
            let mutable acc : Map<string, Map<string, bool>> = Map.empty
            do!
                drainReader reader (fun reader ->
                    let key =
                        System.String.Concat(reader.GetString(0), ".", reader.GetString(1)).ToUpperInvariant()  // LINT-ALLOW: case-insensitive lookup-key composition (see tableKeyOf)
                    let colName = reader.GetString(2)
                    let isNullable =
                        System.String.Equals(reader.GetString(3), "YES", System.StringComparison.OrdinalIgnoreCase)
                    let perTable =
                        Map.tryFind key acc |> Option.defaultValue Map.empty
                    acc <- Map.add key (Map.add colName isNullable perTable) acc)
            return acc
        }

    let private nullabilitySliceOf
        (batched: Map<string, Map<string, bool>>)
        (kind: Kind)
        : Map<string, bool> =
        Map.tryFind (tableKeyOf kind) batched |> Option.defaultValue Map.empty

    let private isStaticKind (k: Kind) : bool =
        k.Modality
        |> List.exists (function
            | Static _ -> true
            | _ -> false)




    /// Project `AttributeReality.HasDuplicates` evidence into
    /// `UniqueCandidateProfile` records. The two IR axes carry
    /// semantically identical single-column-duplicate witnesses;
    /// `UniqueIndexRules.evaluate` reads `UniqueCandidates`, not
    /// `AttributeRealities`, so the projection fills the axis the
    /// rule actually consults. PK attributes project as
    /// `HasDuplicate = false` (`AttributeReality.create` default for
    /// PKs since `ProfileDerivation.deriveAttributeRealities` skips them — PK by
    /// construction is NOT NULL and unique).
    ///

    // -----------------------------------------------------------------
    // Slice B.3.6 — EvidenceCache discovery + pure-F# derivations
    // -----------------------------------------------------------------

    /// Aggregate-query SQL for a kind: one row returning exact
    /// RowCount + per-attribute exact NullCount. Per-kind
    /// `COUNT_BIG`-based projection — one round-trip per
    /// non-static table yielding row count + per-attribute null
    /// count (V1's `NullCountQueryBuilder` shape).
    let private cacheAggregateSql (kind: Kind) : string =
        let table =
            System.String.Join(  // LINT-ALLOW: terminal SQL-text-emission boundary; typed segments
                ".",
                [| encode (TableId.schemaText kind.Physical); encode (TableId.tableText kind.Physical) |])
        let perColumnNullCount (idx: int) (attr: Attribute) : string =
            let col = encode (ColumnRealization.columnNameText attr.Column)
            System.String.Concat(  // LINT-ALLOW: terminal SQL-text boundary; segments are typed (encode-quoted column name + integer column index); BCL String.Concat IS the use-case-specific primitive
                "COUNT_BIG(CASE WHEN ", col, " IS NULL THEN 1 END) AS [c", string idx, "]")
        let selectList =
            kind.Attributes
            |> List.mapi perColumnNullCount
            |> String.concat ", "  // LINT-ALLOW: terminal SQL select-list comma joiner over typed per-column segments; String.concat IS the BCL primitive at this terminal-text boundary
        System.String.Concat(  // LINT-ALLOW: terminal SQL-text-emission boundary; typed segments
            "SELECT COUNT_BIG(*) AS [c_rows], ", selectList, " FROM ", table)

    /// Row-streaming SQL for a kind: project every catalog attribute
    /// in declaration order (positional alignment with
    /// `kind.Attributes`). Slice 7 adds optional `TOP (@N)` cap +
    /// deterministic `ORDER BY <pk>` when a single-column PK
    /// exists. Without a PK, sample ordering is engine-defined; the
    /// adapter still applies the TOP cap but operators should treat
    /// the sampled rows as nondeterministic (a future
    /// `OrderingPolicy` operator-intent variant can stabilize via
    /// `ORDER BY <attr0>` or operator-supplied keys).
    let private cacheRowStreamSql
        (maxRows: int option)
        (kind: Kind)
        : string =
        let table =
            System.String.Join(  // LINT-ALLOW: terminal SQL-text-emission boundary; typed segments
                ".",
                [| encode (TableId.schemaText kind.Physical); encode (TableId.tableText kind.Physical) |])
        let columnList =
            kind.Attributes
            |> List.map (fun a -> encode (ColumnRealization.columnNameText a.Column))
            |> String.concat ", "  // LINT-ALLOW: positional comma joiner over typed encode segments
        let topClause =
            match maxRows with
            | Some n when n > 0 -> System.String.Concat("TOP (", string n, ") ")
            | _                 -> ""
        let orderClause =
            match maxRows, Kind.primaryKey kind with
            | Some _, [ pk ] ->
                // Deterministic sampling: ORDER BY single-column PK
                // (A1 — deterministic sampling under repeated probes).
                System.String.Concat(" ORDER BY ", encode (ColumnRealization.columnNameText pk.Column))
            | _ -> ""
        System.String.Concat(  // LINT-ALLOW: terminal SQL-text-emission boundary; typed segments
            "SELECT ", topClause, columnList, " FROM ", table, orderClause)

    /// Discovery for one kind: 3 queries (aggregate + reflection +
    /// row-stream); composes into a `CachedKind`. Slice 7 accepts
    /// `maxRows` to opt into TOP-N sampling on the row-stream query.
    let private discoverKind
        (cnn: SqlConnection)
        (maxRows: int option)
        (nullabilityMap: Map<string, bool>)
        (kind: Kind)
        : Task<CachedKind option> =
        task {
            use _ = Bench.scope "profile.live.discoverKind"
            if List.isEmpty kind.Attributes then return None
            else
                // 1. Aggregate: exact RowCount + per-attribute NullCount.
                //    Own scope so aggregate / reflection / row-stream are
                //    individually attributable within `discoverKind`.
                let! (rowCount, nullCounts) = task {
                    use _ = Bench.scope "profile.live.aggregate"
                    use cmd = cnn.CreateCommand()
                    cmd.CommandText <- cacheAggregateSql kind
                    cmd.CommandTimeout <- CommandTimeoutPolicy.resolve ()
                    use! reader = cmd.ExecuteReaderAsync()
                    let! advanced = reader.ReadAsync()
                    if not advanced then
                        return (0L, Map.empty)
                    else
                        let rc =
                            if reader.IsDBNull(0) then 0L else reader.GetInt64(0)
                        let counts =
                            kind.Attributes
                            |> List.mapi (fun idx attr ->
                                let nc =
                                    if reader.IsDBNull(1 + idx) then 0L
                                    else reader.GetInt64(1 + idx)
                                attr.SsKey, nc)
                            |> Map.ofList
                        return (rc, counts)
                }
                // 2. Reflection: per-column IsNullableInDatabase — the
                //    caller's slice of the ONE batched INFORMATION_SCHEMA
                //    query (previously a per-kind round trip here).
                // 3. Row-stream: SELECT * → per-row CachedValue array.
                //    Column-oriented final shape (transpose at end).
                let perColumnValues =
                    Array.init (List.length kind.Attributes) (fun _ ->
                        System.Collections.Generic.List<CachedValue>())
                use cmd = cnn.CreateCommand()
                cmd.CommandText <- cacheRowStreamSql maxRows kind
                cmd.CommandTimeout <- CommandTimeoutPolicy.resolve ()
                use! reader = cmd.ExecuteReaderAsync()
                do!
                    drainReader reader (fun reader ->
                        for idx = 0 to (List.length kind.Attributes - 1) do
                            let cellValue =
                                CachedValue.ofReaderValue (reader.GetValue idx)
                            perColumnValues.[idx].Add cellValue)
                let columns =
                    kind.Attributes
                    |> List.mapi (fun idx attr ->
                        let isNullable =
                            Map.tryFind (ColumnRealization.columnNameText attr.Column) nullabilityMap
                            |> Option.defaultValue false
                        { AttributeKey         = attr.SsKey
                          IsNullableInDatabase = isNullable
                          Values               = perColumnValues.[idx].ToArray() })
                let columnsByKey =
                    columns
                    |> List.map (fun c -> c.AttributeKey, c)
                    |> Map.ofList
                return
                    Some
                        { KindKey      = kind.SsKey
                          RowCount     = rowCount
                          NullCounts   = nullCounts
                          Columns      = columns
                          ColumnsByKey = columnsByKey }
        }

    /// Capture a complete EvidenceCache for the catalog. Walks every
    /// non-static kind, runs the 3-query discovery, composes into a
    /// keyed cache.
    ///
    /// Slice B.3.6 architectural pivot: replaces the accreted
    /// per-attribute / per-Reference / per-Index SQL probes from
    /// slices 1-5 with ONE bulk-extraction phase per kind. All
    /// downstream Profile-axis derivations run in pure F# from the
    /// cache (see `ProfileDerivation.derive*`). Net round-trip count per
    /// catalog drops from ~6000 (at 300 tables × 10 attrs) to ~900
    /// (3 per kind regardless of attribute count).
    /// Sampling-aware overload. `attach` uses `defaults` (full-scan);
    /// operator-driven CLIs supply explicit `SqlProfilerOptions` for
    /// large catalogs. Slice 7 cash-out.
    let captureEvidenceCacheWith
        (options: SqlProfilerOptions)
        (cnn: SqlConnection)
        (catalog: Catalog)
        : Task<Result<EvidenceCache>> =
        task {
            use _ = Bench.scope "profile.live.captureEvidenceCache"
            try
                let nonStaticKinds =
                    catalog.Modules
                    |> List.collect (fun m -> m.Kinds)
                    |> List.filter (fun k -> not (isStaticKind k))
                // ONE batched reflection round trip for the whole catalog.
                let! batchedNullability = reflectNullabilityAll cnn
                let mutable acc : Map<SsKey, CachedKind> = Map.empty
                for kind in nonStaticKinds do
                    let! result = discoverKind cnn options.MaxRowsPerKind (nullabilitySliceOf batchedNullability kind) kind
                    match result with
                    | Some cached -> acc <- Map.add cached.KindKey cached acc
                    | None        -> ()
                return Result.success { Kinds = acc }
            with
            | ex ->
                return
                    Result.failureOf
                        (ValidationError.create
                            "profile.live.captureEvidenceCacheFailed"
                            (System.String.Concat("LiveProfiler.captureEvidenceCache failed: ", ex.Message)))
        }

    /// Default-options convenience: full-scan. Public-surface entry
    /// point for callers that don't need to control sampling.
    let captureEvidenceCache
        (cnn: SqlConnection)
        (catalog: Catalog)
        : Task<Result<EvidenceCache>> =
        captureEvidenceCacheWith SqlProfilerOptions.defaults cnn catalog

    /// Discovery for one kind on its OWN connection, gated by the shared
    /// semaphore. Hoisted to module level (no local closures inside a
    /// Release `task { }` — FS3511). The gate bounds in-flight discoveries;
    /// the connection is short-lived (SqlClient pooling reuses physical
    /// connections underneath).
    let private discoverKindGated
        (gate: System.Threading.SemaphoreSlim)
        (openConnection: unit -> Task<Result<SqlConnection>>)
        (maxRows: int option)
        (nullabilityMap: Map<string, bool>)
        (kind: Kind)
        : Task<Result<CachedKind option>> =
        task {
            // Phase labels: gate wait and connection open are OUTSIDE the
            // drain stopwatch (a 0 sample means uncontended/pooled).
            let swGate = System.Diagnostics.Stopwatch.StartNew()
            do! gate.WaitAsync()
            swGate.Stop()
            Bench.recordSample "profile.live.discoverKind.gateWait" swGate.ElapsedMilliseconds
            try
                let swOpen = System.Diagnostics.Stopwatch.StartNew()
                match! openConnection () with
                | Error es -> return Result.failure es
                | Ok cnn ->
                    swOpen.Stop()
                    Bench.recordSample "profile.live.discoverKind.connectionOpen" swOpen.ElapsedMilliseconds
                    use cnn = cnn
                    let sw = System.Diagnostics.Stopwatch.StartNew()
                    let! cached = discoverKind cnn maxRows nullabilityMap kind
                    sw.Stop()
                    Bench.recordSample "profile.live.discoverKind.drain" sw.ElapsedMilliseconds
                    return Result.success cached
            finally
                gate.Release() |> ignore
        }

    /// Bounded-parallel sibling of `captureEvidenceCacheWith` — the
    /// operator's `profiler.maxConcurrency` knob. Each non-static kind runs
    /// its 3-query discovery on its own short-lived connection, at most
    /// `options.MaxConcurrency` in flight. **Acquisition-only concurrency**:
    /// the cache is a keyed `Map` and every downstream Profile axis derives
    /// purely from it (`ProfileDerivation`), so the result never depends on
    /// completion order. Any per-kind open or probe failure fails the whole
    /// capture loudly.
    let captureEvidenceCacheConcurrent
        (options: SqlProfilerOptions)
        (openConnection: unit -> Task<Result<SqlConnection>>)
        (catalog: Catalog)
        : Task<Result<EvidenceCache>> =
        task {
            use _ = Bench.scope "profile.live.captureEvidenceCache.concurrent"
            try
                let nonStaticKinds =
                    catalog.Modules
                    |> List.collect (fun m -> m.Kinds)
                    |> List.filter (fun k -> not (isStaticKind k))
                let capped = max 1 options.MaxConcurrency
                Bench.recordSample "profile.live.captureEvidenceCache.concurrency" (int64 capped)
                // ONE batched reflection round trip up front (its own
                // short-lived connection), sliced per kind below — under
                // the concurrent form this also removes a per-kind query
                // from every gated worker.
                let! batchedNullabilityR = task {
                    match! openConnection () with
                    | Error es -> return Error es
                    | Ok cnn ->
                        use cnn = cnn
                        let! batched = reflectNullabilityAll cnn
                        return Ok batched
                }
                match batchedNullabilityR with
                | Error es -> return Result.failure es
                | Ok batchedNullability ->
                use gate = new System.Threading.SemaphoreSlim(capped, capped)
                let discoveries =
                    nonStaticKinds
                    |> List.map (fun kind ->
                        discoverKindGated gate openConnection options.MaxRowsPerKind (nullabilitySliceOf batchedNullability kind) kind)
                let! results = Task.WhenAll(Array.ofList discoveries)
                return
                    results
                    |> Array.toList
                    |> Result.aggregate
                    |> Result.map (fun cachedKinds ->
                        let kinds =
                            cachedKinds
                            |> List.choose id
                            |> List.map (fun c -> c.KindKey, c)
                            |> Map.ofList
                        { Kinds = kinds })
            with
            | ex ->
                return
                    Result.failureOf
                        (ValidationError.create
                            "profile.live.captureEvidenceCacheFailed"
                            (System.String.Concat("LiveProfiler.captureEvidenceCacheConcurrent failed: ", ex.Message)))
        }

    /// **Single-scan capture** — derive each hydrated kind's evidence from
    /// the rows the data lanes ALREADY pulled
    /// (`EvidenceCache.cachedKindOfRows`); the ONE global nullability
    /// reflection is the only SQL a fully-hydrated publish pays here.
    /// Per-kind budget: was 2 queries + a full row stream; becomes ZERO
    /// (the stream was the data lanes'). Kinds OUTSIDE the hydrated set
    /// (lane composition excludes them; attribute-less) keep the live
    /// gated discovery — the fallback is counted
    /// (`profile.live.derived.kinds` / `.fallback`), never silent.
    /// Sampling (`MaxRowsPerKind = Some _`) disables derivation entirely:
    /// the live aggregate is exact even under a sampled stream, and a
    /// derived cache from full rows would not be the sampled shape the
    /// operator asked for.
    let captureEvidenceCacheDerived
        (options: SqlProfilerOptions)
        (openConnection: unit -> Task<Result<SqlConnection>>)
        (hydratedRows: Map<SsKey, StaticRow list>)
        (catalog: Catalog)
        : Task<Result<EvidenceCache>> =
        task {
            use _ = Bench.scope "profile.live.captureEvidenceCache.derived"
            try
                let nonStaticKinds =
                    catalog.Modules
                    |> List.collect (fun m -> m.Kinds)
                    |> List.filter (fun k -> not (isStaticKind k))
                let! batchedNullabilityR = task {
                    match! openConnection () with
                    | Error es -> return Error es
                    | Ok cnn ->
                        use cnn = cnn
                        let! batched = reflectNullabilityAll cnn
                        return Ok batched
                }
                match batchedNullabilityR with
                | Error es -> return Result.failure es
                | Ok batchedNullability ->
                let derivable, live =
                    if options.MaxRowsPerKind.IsSome then [], nonStaticKinds
                    else
                        nonStaticKinds
                        |> List.partition (fun k -> Map.containsKey k.SsKey hydratedRows)
                let derivedKinds =
                    derivable
                    |> List.choose (fun k ->
                        EvidenceCache.cachedKindOfRows
                            (nullabilitySliceOf batchedNullability k)
                            k
                            (Map.find k.SsKey hydratedRows))
                Bench.recordSample "profile.live.derived.kinds" (int64 (List.length derivedKinds))
                Bench.recordSample "profile.live.derived.fallback" (int64 (List.length live))
                let derivedMap =
                    derivedKinds |> List.map (fun c -> c.KindKey, c) |> Map.ofList
                if List.isEmpty live then
                    return Result.success { Kinds = derivedMap }
                else
                    let capped = max 1 options.MaxConcurrency
                    use gate = new System.Threading.SemaphoreSlim(capped, capped)
                    let discoveries =
                        live
                        |> List.map (fun kind ->
                            discoverKindGated gate openConnection options.MaxRowsPerKind (nullabilitySliceOf batchedNullability kind) kind)
                    let! results = Task.WhenAll(Array.ofList discoveries)
                    return
                        results
                        |> Array.toList
                        |> Result.aggregate
                        |> Result.map (fun cachedKinds ->
                            let kinds =
                                cachedKinds
                                |> List.choose id
                                |> List.fold (fun acc c -> Map.add c.KindKey c acc) derivedMap
                            { Kinds = kinds })
            with
            | ex ->
                return
                    Result.failureOf
                        (ValidationError.create
                            "profile.live.captureEvidenceCacheFailed"
                            (System.String.Concat("LiveProfiler.captureEvidenceCacheDerived failed: ", ex.Message)))
        }

    /// Single-scan sibling of `attach`: derive the cache from hydrated
    /// rows (live fallback for uncovered kinds), then compose every
    /// Profile axis purely via `attachFromCache`.
    let attachDerived
        (options: SqlProfilerOptions)
        (openConnection: unit -> Task<Result<SqlConnection>>)
        (hydratedRows: Map<SsKey, StaticRow list>)
        (catalog: Catalog)
        (profile: Profile)
        : Task<Result<Profile>> =
        task {
            let! cacheResult = captureEvidenceCacheDerived options openConnection hydratedRows catalog
            match cacheResult with
            | Error errors -> return Result.failure errors
            | Ok cache    -> return Result.success (ProfileDerivation.attachFromCache cache catalog profile)
        }

    /// Connection-factory sibling of `attach`: capture the EvidenceCache
    /// with bounded per-kind parallelism, then compose every derived
    /// Profile axis purely via `attachFromCache` — same evidence, same
    /// derivations, different acquisition schedule.
    let attachConcurrent
        (options: SqlProfilerOptions)
        (openConnection: unit -> Task<Result<SqlConnection>>)
        (catalog: Catalog)
        (profile: Profile)
        : Task<Result<Profile>> =
        task {
            let! cacheResult = captureEvidenceCacheConcurrent options openConnection catalog
            match cacheResult with
            | Error errors -> return Result.failure errors
            | Ok cache    -> return Result.success (ProfileDerivation.attachFromCache cache catalog profile)
        }



    /// Attach realities into an existing Profile. Composable, not
    /// authoritative — callers may chain sibling adapters that fill axes
    /// LiveProfiler does not (per the rich-profiling agenda).
    /// Captures the `EvidenceCache` substrate once (three queries per
    /// non-static kind) and composes every derived Profile axis into
    /// the input via `attachFromCache`. Pre-populated axes are
    /// overwritten with cache-derived values (the live-probe path is
    /// authoritative for axes it covers); sibling axes filled by
    /// other adapters are preserved untouched.
    let attach
        (cnn: SqlConnection)
        (catalog: Catalog)
        (profile: Profile)
        : Task<Result<Profile>> =
        task {
            // Cache-driven (chapter B.3 slice 6b + B.4.2 retirement):
            // EvidenceCache once (3 queries per non-static kind);
            // every Profile axis derives from cache in pure F# via
            // `attachFromCache`. Total SQL round-trips:
            // 3 per non-static kind.
            let! cacheResult = captureEvidenceCache cnn catalog
            match cacheResult with
            | Error errors -> return Result.failure errors
            | Ok cache    -> return Result.success (ProfileDerivation.attachFromCache cache catalog profile)
        }
