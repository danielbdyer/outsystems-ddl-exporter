namespace Projection.Pipeline

open System
open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Adapters.Sql
open Projection.Targets.SSDT

/// Deploy-dispatch parallelism: auto-detection (SQL Server DMV probe →
/// client CPU count → static fallback), the per-connection-string cache,
/// the Max-Pool-Size cap, and the `ParallelSafe`-gated parallel batch
/// realization primitive. Extracted from `Deploy.fs` (R2/B7 decomposition)
/// as the concept-named home for deploy-time parallelism policy; re-exposed
/// under `Deploy.detectParallelism` / `Deploy.resolveParallelism` /
/// `Deploy.executeBatchParallel` via thin forwards so every existing
/// `Deploy.*` call site (production code, canaries,
/// `ExecuteBatchParallelTests.fs`) is untouched.
[<RequireQualifiedAccess>]
module DeployParallelism =

    /// Cache of resolved parallelism per connection string. Per slice
    /// A.4.7'-prelude.perf-sweep-7.auto-scale: a single DMV round-trip
    /// at first use surfaces the SQL Server's CPU count; subsequent
    /// calls hit the cache. `ConcurrentDictionary` is the standard
    /// concurrent cache; reference-equal connection strings (the
    /// common case — same SqlConnection.ConnectionString reused) hit
    /// the same entry.
    let private parallelismCache =
        System.Collections.Concurrent.ConcurrentDictionary<string, int>()

    /// Static fallback parallelism — used when both the SQL Server DMV
    /// probe AND the client-CPU heuristic are unavailable. Conservative
    /// default validated by the `ExecuteBatchParallelTests` microbench
    /// (1.21-1.75× speedup at parallelism=4 on the warm container).
    [<Literal>]
    let private FallbackParallelism : int = 4

    /// Maximum parallelism cap — beyond this, lock contention on MERGE-
    /// shaped workloads dominates the speedup. Empirical per the
    /// SQL Server best-practices literature for bulk-write workloads.
    [<Literal>]
    let private MaxParallelism : int = 16

    /// Minimum parallelism floor — below this, the parallel-dispatch
    /// overhead exceeds the speedup.
    [<Literal>]
    let private MinParallelism : int = 2

    /// Attempt to read SQL Server's CPU count via
    /// `sys.dm_os_sys_info.cpu_count`. Returns `Some n` on success,
    /// `None` on **any** failure mode:
    ///   - Connection failure (network, timeout)
    ///   - Permission denied (`VIEW SERVER STATE` required; restricted
    ///     in managed-instance / Azure SQL Database / least-privileged
    ///     production accounts)
    ///   - DMV missing or column renamed (very old SQL Server versions)
    ///   - Scalar query returns null / DBNull (defensive against
    ///     unexpected result shapes)
    ///   - Cast failure (defensive against non-integer return types)
    /// Never throws — exceptions are caught and converted to `None`.
    let private tryProbeServerCpus (connectionString: string) : Task<int option> =
        task {
            try
                use cnn = new SqlConnection(connectionString)
                do! cnn.OpenAsync()
                use cmd = cnn.CreateCommand()
                cmd.CommandText <- "SELECT cpu_count FROM sys.dm_os_sys_info"
                cmd.CommandTimeout <- 5
                let! raw = cmd.ExecuteScalarAsync()
                match raw with
                | null -> return None
                | :? System.DBNull -> return None
                | other ->
                    try
                        let n = System.Convert.ToInt32 other
                        if n > 0 then return Some n
                        else return None
                    with _ -> return None
            with _ -> return None
        }

    /// Layered probe for the deploy-dispatch parallelism. Tries layers
    /// in order; first success wins:
    ///   1. **SQL Server DMV** (`sys.dm_os_sys_info.cpu_count`) —
    ///      authoritative when accessible; matches the actual
    ///      server-side concurrency capacity. Capped via
    ///      `clamp(MinParallelism, MaxParallelism, serverCpus)`.
    ///   2. **Client CPU count** (`Environment.ProcessorCount`) —
    ///      defensive fallback for environments where the DMV is
    ///      restricted (managed instances; least-privileged accounts;
    ///      cross-server-version compatibility). Capped at `min 8`
    ///      because the client is the dispatcher (not the workload);
    ///      large client parallelism without a matched server
    ///      doesn't translate to throughput.
    ///   3. **Static fallback** (`FallbackParallelism`, currently 4) —
    ///      last-resort safe default. Reached only when (a) the DMV
    ///      probe failed AND (b) `Environment.ProcessorCount`
    ///      returned ≤ 0 (extraordinarily unlikely; defensive against
    ///      bizarre runtime conditions).
    ///
    /// Each layer logs the chosen path to stderr so operators can see
    /// WHY a given parallelism was selected — important for debugging
    /// unexpected deploy throughput in restricted environments.
    let detectParallelism (connectionString: string) : Task<int> =
        task {
            use _ = Bench.scope "deploy.detectParallelism"
            // Layer 1: SQL Server DMV
            let! serverProbe = tryProbeServerCpus connectionString
            match serverProbe with
            | Some serverCpus ->
                let clamped = max MinParallelism (min MaxParallelism serverCpus)
                Bench.recordSample "deploy.detectParallelism.serverCpus" (int64 serverCpus)
                Bench.recordSample "deploy.detectParallelism.resolved" (int64 clamped)
                return clamped
            | None ->
                // Layer 2: client CPU count — defensive fallback when
                // DMV is unreachable (Azure SQL Database; restricted
                // accounts; cross-version compatibility).
                let clientCpus = System.Environment.ProcessorCount
                if clientCpus > 0 then
                    // Smaller ceiling on the client-side fallback: the
                    // client is just the parallelism dispatcher, not
                    // the SQL workload — too much client parallelism
                    // without matched server capacity wastes
                    // connection-pool slots without throughput gain.
                    let clamped = max MinParallelism (min 8 clientCpus)
                    // Coded envelope, not raw stderr (2026-07-02) — a raw eprintfn
                    // tears the live board's region; channel 1 carries the advisory.
                    LogSink.emit
                        (LogSink.envelope LogSink.Info LogSink.Deploy "deploy.parallelism.dmvUnavailable"
                            (Map.ofList [ "clientCpus", box clientCpus; "resolved", box clamped ]))
                    Bench.recordSample "deploy.detectParallelism.clientCpus" (int64 clientCpus)
                    Bench.recordSample "deploy.detectParallelism.resolved" (int64 clamped)
                    return clamped
                else
                    // Layer 3: static fallback (last resort).
                    LogSink.emit
                        (LogSink.envelope LogSink.Info LogSink.Deploy "deploy.parallelism.staticFallback"
                            (Map.ofList [ "resolved", box FallbackParallelism ]))
                    Bench.recordSample "deploy.detectParallelism.resolved" (int64 FallbackParallelism)
                    return FallbackParallelism
        }

    /// Resolve the parallelism for data-deploy dispatch. Priority order:
    ///   1. `PROJECTION_DEPLOY_PARALLELISM` env var (operator override;
    ///      escape hatch for tuning or for environments where DMV
    ///      access is restricted)
    ///   2. Auto-detect via `detectParallelism` (DMV probe; cached
    ///      per-connection-string for the session)
    ///   3. Fallback to 4 (matches the prior hardcoded value)
    ///
    /// Slice A.4.7'-prelude.perf-sweep-7.auto-scale — environment-
    /// adaptive realization policy per A36 (bulk-vs-incremental is
    /// realization-layer policy; parallelism choice is the same
    /// category).
    let resolveParallelism (connectionString: string) : Task<int> =
        task {
            use _ = Bench.scope "deploy.resolveParallelism"
            match System.Environment.GetEnvironmentVariable "PROJECTION_DEPLOY_PARALLELISM" with
            | s when not (System.String.IsNullOrWhiteSpace s) ->
                match System.Int32.TryParse s with
                | true, n when n > 0 ->
                    Bench.recordSample "deploy.resolveParallelism.envOverride" (int64 n)
                    return n
                | _ ->
                    LogSink.emit
                        (LogSink.envelope LogSink.Warn LogSink.Deploy "deploy.parallelism.envOverrideMalformed"
                            (Map.ofList [ "value", box s ]))
                    match parallelismCache.TryGetValue connectionString with
                    | true, cached -> return cached
                    | false, _ ->
                        let! detected = detectParallelism connectionString
                        parallelismCache.TryAdd(connectionString, detected) |> ignore
                        return detected
            | _ ->
                match parallelismCache.TryGetValue connectionString with
                | true, cached ->
                    Bench.recordSample "deploy.resolveParallelism.cached" (int64 cached)
                    return cached
                | false, _ ->
                    let! detected = detectParallelism connectionString
                    parallelismCache.TryAdd(connectionString, detected) |> ignore
                    return detected
        }

    /// **Parallel realization of one concurrent-safe group** (slice
    /// A.4.7'-prelude.perf-sweep-5 primitive; card P1 re-signs it to
    /// DEMAND the proof). Each member is batch-split via `BatchSplitter`
    /// and the segments dispatch across `parallelism` concurrent
    /// SqlConnections gated by a SemaphoreSlim. Each segment runs on its
    /// own freshly-opened connection; `SqlConnection`'s internal pooling
    /// (keyed on the connection string) makes the per-segment
    /// new/Open/Dispose cheap on warm pools.
    ///
    /// **The independence contract is the argument's TYPE** (card P1 /
    /// R5): `ParallelSafe<string>` cannot exist unless
    /// `TopologicalOrder.levels` proved the group's members mutually
    /// independent — the comment-borne MUST this docstring used to
    /// carry is structural now. (Its 2026-05-era "the canary continues
    /// using sequential executeBatch until the composer exposes safe
    /// groups" status note was stale — the comprehensive canary has
    /// deployed leveled data through this primitive since perf-sweep-6 —
    /// and is retired with the re-signing, RI-5.)
    /// Cap the requested parallelism against the connection string's
    /// `Max Pool Size` (slice A.4.7'-prelude.defensive-hardening).
    /// Defensive against Azure SQL Database tier connection caps
    /// (Basic/S0 limit concurrent connections to 30; least-privilege
    /// accounts often run with smaller pool sizes); without the cap,
    /// `cnn.OpenAsync()` throws `SqlException` error 10928 ("Resource
    /// ID limit reached") for some segments, leaving partial DDL.
    /// Halves the pool budget to leave headroom for adjacent
    /// in-flight connections (e.g., the canary's source-DB read
    /// happens concurrently with the target-DB deploy).
    let private capParallelismToPool (connectionString: string) (requested: int) : int =
        try
            let builder = SqlConnectionStringBuilder connectionString
            let maxPool = builder.MaxPoolSize
            // SqlConnectionStringBuilder.MaxPoolSize defaults to 100.
            // Halve to leave headroom for concurrent connections.
            let safeCeil = max 1 (maxPool / 2)
            if requested > safeCeil then
                LogSink.emit
                    (LogSink.envelope LogSink.Info LogSink.Deploy "deploy.parallelism.poolCapped"
                        (Map.ofList [ "requested", box requested; "maxPoolSize", box maxPool; "capped", box safeCeil ]))
                safeCeil
            else
                requested
        with _ ->
            // Pathologically bad connection string falls through to
            // the requested value; SqlConnection itself will surface
            // the syntactic issue at Open time.
            requested

    let executeBatchParallel
            (connectionString: string)
            (level: ParallelSafe<string>)
            (parallelism: int)
            : Task<unit> =
        task {
            use _ = Bench.scope "deploy.executeBatchParallel"
            // Per-member split then flatten ≡ split-of-concatenation (every
            // member is GO-terminated), so the segment bytes and the bench
            // label series are unchanged by the re-signing.
            let segments =
                ParallelSafe.members level
                |> List.toArray
                |> Array.collect (BatchSplitter.splitWithLoudFallback "deploy.executeBatchParallel")
            let cappedParallelism = capParallelismToPool connectionString parallelism
            Bench.recordSample "deploy.executeBatchParallel.segments" (int64 segments.Length)
            Bench.recordSample "deploy.executeBatchParallel.parallelism" (int64 cappedParallelism)
            use semaphore = new System.Threading.SemaphoreSlim(cappedParallelism, cappedParallelism)
            let runSegment (segment: string) : Task<unit> =
                task {
                    do! semaphore.WaitAsync()
                    try
                        use _ = Bench.scope "deploy.executeBatchParallel.segment"
                        Bench.recordSample "deploy.executeBatchParallel.segment.bytes" (int64 segment.Length)
                        use cnn = new SqlConnection(connectionString)
                        do! cnn.OpenAsync()
                        use cmd = cnn.CreateCommand()
                        cmd.CommandText <- segment
                        cmd.CommandTimeout <- CommandTimeoutPolicy.resolve ()
                        let! _ = cmd.ExecuteNonQueryAsync()
                        ()
                    finally
                        semaphore.Release() |> ignore
                }
            let tasks = segments |> Array.map runSegment
            let! _ = Task.WhenAll(tasks)
            ()
        }
