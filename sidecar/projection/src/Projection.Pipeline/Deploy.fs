namespace Projection.Pipeline

// LINT-ALLOW-FILE-MUTATION: Docker JIT bring-up poll loop + bulk-grouping mutables for the typed-
//   Statement-stream realization. Per audit Lens-2 Tier-1+2; see commit-log
//   slice λ.2 for the runWideCanaryWithLoader phase-functional refactor.

open System
open System.IO
open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Testcontainers.MsSql
open Projection.Core
open Projection.Adapters.Sql
open Projection.Targets.SSDT

/// M2 / M3 (per the chapter-3.1 milestone sequence chosen at
/// session 27): deploy V2's emitted SSDT to an ephemeral SQL Server
/// (M2), and read the deployed schema back to reconstruct a Catalog
/// (M3). Together they close the canary's V2-internal round-trip:
/// `Catalog → emit → deploy → read → Catalog'`.
///
/// Per `DECISIONS 2026-05-23 — Source SQL Server with OutSystems
/// semantics is the canary's primary wide integration surface`, the
/// wide canary further exercises a source-DDL fixture (the operator's
/// reality) through the same readside, then through V2's emitter to
/// a target ephemeral container, then back through readside, and
/// asserts source ≈ target on the `PhysicalSchema` axis.
///
/// **Idempotency.** Each `runEphemeral` / `runWithReadback` /
/// `runWideCanary` call is independent — fresh container, fresh
/// database(s), full teardown. Same input produces the same outcome
/// with no state crossing between calls.
[<RequireQualifiedAccess>]
module Deploy =

    /// Outcome of executing emitted SQL against an ephemeral database.
    type Report =
        {
            Ok : bool
            Database : string
            TablesCreated : int
            Errors : string list
        }

    /// Outcome of `runWithReadback`: the deploy `Report` plus the
    /// `Catalog` reconstructed from the deployed schema (when deploy
    /// succeeded).
    type DeployedResult =
        {
            Report : Report
            Reconstructed : Catalog option
        }

    /// Outcome of `runWideCanary`: the source Catalog (read back
    /// from the deployed source DDL), the target Catalog (read back
    /// from V2's emitted SSDT after deploy), and the
    /// `PhysicalSchemaDiff` between them. Both `Report` values
    /// surface deploy failures in either half.
    type WideCanaryReport =
        {
            Source : Catalog
            Target : Catalog
            Diff : PhysicalSchemaDiff
            SourceReport : Report
            TargetReport : Report
        }

    /// Docker availability + lazy JIT bring-up. Per session-36 — the
    /// session-start hook starts dockerd at session boot, but the
    /// daemon can drop mid-session (OOM, signal, environment shift);
    /// `ensureRunning` probes responsiveness and re-starts the daemon
    /// best-effort so canary tests don't fail spuriously. `isAvailable`
    /// stays as the static yes/no for callers that just want a probe;
    /// tests that actually need Docker should call `ensureRunning`.
    [<RequireQualifiedAccess>]
    module Docker =
        // Canonical Docker Unix socket locations. Two candidates:
        //   1. system-wide socket at /var/run/docker.sock (Linux default)
        //   2. user-scoped rootless socket under ~/.docker/run/docker.sock
        // Path composition flows through `Path.Combine` (audit Top-10
        // #6: filesystem boundary uses BCL primitive, not `sprintf`).
        [<Literal>]
        let private SystemSocketPath : string = "/var/run/docker.sock"

        let private socketCandidates : string list =
            let home = Environment.GetFolderPath Environment.SpecialFolder.UserProfile
            let userSocket = Path.Combine(home, ".docker", "run", "docker.sock")
            [ SystemSocketPath; userSocket ]

        // Why these specific values (not arbitrary):
        // - `ProbeTimeoutMs = 2000`: `docker version` against a healthy
        //   daemon returns in tens of ms; 2 s is the slack for a
        //   loaded host. Larger timeouts hide a wedged daemon
        //   (downstream canary work fails anyway). 2 s is the smallest
        //   value that doesn't false-negative under host load.
        // - `BringupBudgetMs = 5000`: dockerd cold-start measured at
        //   1-3 s on hosts where bring-up is permitted; 5 s is the
        //   smallest budget that survives a sandbox-loaded p99 cold
        //   boot. Per `DECISIONS 2026-05-10 — Docker probe efficiency`,
        //   higher budgets ARE NOT a help on a host where the daemon
        //   genuinely cannot start (web sandbox without dockerd
        //   privileges) — the budget is paid once per call (×N tests
        //   absent memoization), and then the daemon still won't be
        //   up. Memoization (below) collapses the test-suite-level
        //   cost; the lower per-call ceiling makes even un-memoized
        //   first-call paths reasonable.
        // - `BringupPollMs = 200`: below the perceptual instant-response
        //   threshold (~250 ms) so a successful bring-up returns
        //   without feeling laggy; small enough to detect a fast
        //   bring-up promptly without hammering the probe.
        // The shape is a poll-until-ready loop, not a fixed wait —
        // we exit as soon as the daemon is responsive; the budget
        // is only consumed in the failure case.
        [<Literal>]
        let private ProbeTimeoutMs : int = 2_000

        [<Literal>]
        let private BringupBudgetMs : int = 5_000

        [<Literal>]
        let private BringupPollMs : int = 200

        /// Static probe: env var or socket file present. Cheap; doesn't
        /// contact the daemon. Used as the gate for "this environment
        /// could plausibly run Docker."
        let isAvailable () : bool =
            let hasEnvHost =
                let v = Environment.GetEnvironmentVariable "DOCKER_HOST"
                not (String.IsNullOrWhiteSpace v)
            let socketExists =
                socketCandidates |> List.exists File.Exists
            hasEnvHost || socketExists

        /// Active probe: shell out to `docker version` with a 2 s
        /// ceiling. Captures the daemon-not-listening case that
        /// `isAvailable` (socket-file probe only) misses.
        let private isResponding () : bool =
            try
                let psi =
                    System.Diagnostics.ProcessStartInfo(
                        FileName = "docker",
                        Arguments = "version --format \"{{.Server.Version}}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false)
                match System.Diagnostics.Process.Start psi with
                | null -> false
                | p ->
                    use _ = p
                    if p.WaitForExit ProbeTimeoutMs then p.ExitCode = 0
                    else
                        try p.Kill() with _ -> ()
                        false
            with _ -> false

        /// Best-effort daemon spawn followed by poll-until-ready. The
        /// budget is a *ceiling* — we exit as soon as the daemon
        /// answers `isResponding`, so a healthy bring-up returns
        /// within a few hundred ms. The full budget consumes only
        /// when dockerd genuinely failed to start.
        let private startDaemon () : bool =
            try
                let psi =
                    System.Diagnostics.ProcessStartInfo(
                        FileName = "sudo",
                        Arguments = "dockerd",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false)
                System.Diagnostics.Process.Start psi |> ignore
            with _ -> ()
            let sw = System.Diagnostics.Stopwatch.StartNew()
            let mutable up = isResponding ()
            while not up && sw.ElapsedMilliseconds < int64 BringupBudgetMs do
                System.Threading.Thread.Sleep BringupPollMs
                up <- isResponding ()
            up

        // Memoization for `ensureRunning`. Per `DECISIONS 2026-05-10 —
        // Docker probe efficiency`: a test suite invokes `ensureRunning`
        // per-test through `skipIfNoDocker`; absent memoization, every
        // canary test pays the full bring-up budget when Docker is
        // unavailable (~14 minutes for a 15-test suite at the prior
        // 30 s budget). Memoization collapses the suite-level cost to
        // a single probe-and-bring-up cycle; the result is cached for
        // the life of the process.
        //
        // Thread-safety: the lock guards both read and write. Re-entry
        // is impossible (the inner work is `isAvailable` /
        // `isResponding` / `startDaemon`, none of which call
        // `ensureRunning`).
        //
        // Reset semantics: `resetMemo ()` clears the cache for callers
        // that need to re-probe (e.g., a long-running CLI that wants
        // to retry after a daemon recovery). Tests do not use it; the
        // canary-test pattern is one probe per process.
        let private memoLock : obj = obj ()
        let mutable private memoResult : bool option = None

        let private probeOnce () : bool =
            // Per the chapter-3.1 sandbox finding (codified
            // 2026-05-10 — Docker probe efficiency): the dominant
            // worst case in the web sandbox is `isAvailable` true
            // (socket file present from a prior bring-up attempt)
            // but the daemon not responding AND `sudo dockerd`
            // unavailable to us. `BringupBudgetMs` is the ceiling
            // for that loop; memoization (above) keeps the cost
            // suite-wide constant.
            if not (isAvailable ()) then false
            elif isResponding () then true
            else startDaemon ()

        /// JIT bring-up. Returns true iff Docker is usable for canary
        /// work. Tests gate on this for clean skip-if-unavailable
        /// semantics that survive a mid-session daemon drop.
        ///
        /// **Memoized** per `DECISIONS 2026-05-10 — Docker probe
        /// efficiency`. The bring-up budget is paid at most once per
        /// process. Use `resetMemo ()` if you need to retry after a
        /// known recovery event (rare; tests don't).
        let ensureRunning () : bool =
            lock memoLock (fun () ->
                match memoResult with
                | Some cached -> cached
                | None ->
                    let result = probeOnce ()
                    memoResult <- Some result
                    result)

        /// Clear the memoized `ensureRunning` result. The next call
        /// re-probes from scratch. Per the discipline above: only
        /// callers that have *evidence* of daemon recovery should
        /// invoke this (e.g., a test runner with a between-suite
        /// hook that re-armed Docker).
        let resetMemo () : unit =
            lock memoLock (fun () -> memoResult <- None)

    [<Literal>]
    let private DefaultImage : string =
        "mcr.microsoft.com/mssql/server:2022-latest"

    /// **First-class non-determinism boundary.** `DatabaseNameGenerator
    /// = unit -> string` is the typed seam through which `Guid.NewGuid`
    /// (or any test-pinned counter) enters Pipeline. Per
    /// `DECISIONS 2026-05-09 — No-string-concatenation / no-regex
    /// discipline` audit Tier 2: the leak from non-determinism into
    /// the deploy boundary is reified as a parameter, not hidden in
    /// a closure. Callers that need byte-determinism on database
    /// names inject a counter-based generator; the default uses
    /// `Guid.NewGuid` for unique-per-run isolation in shared
    /// containers (the canary's de-facto requirement).
    type DatabaseNameGenerator = unit -> string

    [<RequireQualifiedAccess>]
    module DatabaseNameGenerator =

        /// Default `DatabaseNameGenerator` — uses `Guid.NewGuid`. The
        /// observable non-determinism is scoped to per-database
        /// names; T1 byte-determinism at the SQL emission layer is
        /// unaffected (V2's Π output is pure; only the deploy-host's
        /// per-database scoping is non-deterministic, and that's a
        /// Pipeline concern). Per-segment formatting goes through
        /// `String.Concat` rather than `sprintf`.
        /// Length of the GUID suffix used in ephemeral database
        /// names. 12 chars of N-format GUID is ~48 bits of entropy
        /// — sufficient for per-run uniqueness across concurrent
        /// canary processes; short enough to fit `Source_<suffix>`
        /// under SQL Server's 128-char identifier limit with
        /// generous prefix headroom.
        [<Literal>]
        let private GuidSuffixLength : int = 12

        let guidBased : DatabaseNameGenerator =
            // The `guidBased` binding IS the sanctioned `Guid.NewGuid`
            // site — the reified non-determinism boundary. Audit
            // Lens-2 Tier-1 discharged: Guid.NewGuid is now type-
            // visible through the seam, not hidden inside a private
            // function.
            fun () ->
                let suffix = Guid.NewGuid().ToString("N").Substring(0, GuidSuffixLength)  // LINT-ALLOW: reified non-determinism boundary
                suffix

    let private uniqueDatabaseName
        (gen: DatabaseNameGenerator)
        (prefix: string)
        : string =
        String.Concat(prefix, "_", gen())

    /// Format one `SqlError` row as a structured diagnostic string.
    /// Per the no-string-concatenation discipline, integer fields go
    /// through invariant-culture `Int32.ToString`; segments compose
    /// via `String.concat " "` over a typed `string list`. The
    /// canonical surface form is "[severity=X error=Y line=Z]
    /// message" — downstream consumers parse this text-form via the
    /// matching parser; both endpoints are V2-internal.
    let private formatSqlError (e: Microsoft.Data.SqlClient.SqlError) : string =
        let inv = System.Globalization.CultureInfo.InvariantCulture
        let header =
            [
                String.Concat("[severity=", e.Class.ToString(inv))
                String.Concat("error=",     e.Number.ToString(inv))
                String.Concat("line=",      e.LineNumber.ToString(inv), "]")
            ]
            |> String.concat " "
        String.Concat(header, " ", e.Message)

    let private collectErrors (ex: exn) : string list =
        match ex with
        | :? SqlException as sql ->
            [ for e in sql.Errors -> formatSqlError e ]
        | _ -> [ ex.Message ]

    /// Typed connection-string handling — chapter-3.6 cash-out of
    /// audit Top-10 #1 (centralize connection-string validation
    /// behind `SqlConnectionStringBuilder`). Wraps the BCL builder
    /// in a `Result<_>` so malformed strings surface as
    /// `ValidationError` at the validation boundary, not as an
    /// opaque `SqlException` at connect time.
    [<RequireQualifiedAccess>]
    module ConnectionString =

        let private invalidConnectionString (message: string) : ValidationError =
            ValidationError.create "deploy.connectionString.invalid" message

        /// Parse a connection string into a validated typed builder.
        /// `SqlConnectionStringBuilder` throws on malformed input;
        /// we catch and lift to `ValidationError` so the boundary
        /// surfaces structured errors.
        let parse (connStr: string) : Result<SqlConnectionStringBuilder> =
            if System.String.IsNullOrWhiteSpace connStr then
                Result.failureOf
                    (invalidConnectionString
                        "Connection string is null, empty, or whitespace.")
            else
                try
                    Result.success (SqlConnectionStringBuilder(connStr))
                with
                | :? System.ArgumentException as ex ->
                    Result.failureOf (invalidConnectionString ex.Message)
                | :? System.FormatException as ex ->
                    Result.failureOf (invalidConnectionString ex.Message)

        /// Build a per-database connection string from a master
        /// connection string. Trusts the `master` argument (callers
        /// flow through `parse` first if validation is needed); the
        /// `dbName` is set as `InitialCatalog`. Identifier escaping
        /// (handling `]` inside dbName) is `SqlConnectionStringBuilder`'s
        /// responsibility — its setter handles SQL-quoting per spec.
        let buildPerDb (master: string) (dbName: string) : string =
            let b = SqlConnectionStringBuilder(master)
            b.InitialCatalog <- dbName
            b.ConnectionString

    let private buildPerDbConnectionString (master: string) (dbName: string) : string =
        ConnectionString.buildPerDb master dbName

    /// `CREATE DATABASE [<dbName>];` text. Bracket-quoting flows
    /// through `Render.quote` (which delegates to ScriptDom's
    /// `Identifier.EncodeIdentifier`) — eliminates the manual
    /// `[` / `]` literals (audit Top-10 #10: single source of truth
    /// for SQL identifier quoting). The trailing `;` is T-SQL
    /// statement convention. Caller-supplied `dbName` flows through
    /// the canonical SQL identifier encoder, so values containing
    /// `]` (which would close the bracket prematurely) are
    /// structurally escaped at the boundary.
    let private createDatabaseSql (dbName: string) : string =
        String.Concat("CREATE DATABASE ", Render.quote dbName, ";")

    let private createDatabase (masterConn: string) (dbName: string) : Task<unit> =
        task {
            use _ = Bench.scope "deploy.createDatabase"
            use cnn = new SqlConnection(masterConn)
            do! cnn.OpenAsync()
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- createDatabaseSql dbName
            let! _ = cmd.ExecuteNonQueryAsync()
            return ()
        }

    /// Public realization of a SQL text batch. Splits via
    /// `BatchSplitter.splitWithLoudFallback` — gold-standard
    /// `TSql160Parser`-based batch detection with `splitOnGo`
    /// line-fold as a permissive fallback. ScriptDom failures
    /// emit a stderr announcement (operators see WHY the
    /// fallback fired); the run continues so canaries don't
    /// hard-fail on grammatical idiosyncrasies (chapter-3.6
    /// cash-out per the user's "implement behind an adapter
    /// or strategy with loud announcement" directive).
    ///
    /// **Perf citation (pillar 7):** ScriptDom Parse adds ~5-10ms
    /// per `executeBatch` call (vs ~1ms for line-fold). At
    /// `executeBatch`-per-deploy cardinality (one per phase),
    /// the absolute cost stays sub-100ms even on huge fixtures.
    /// Per-segment `ExecuteNonQueryAsync` round-trips remain the
    /// dominant cost.
    let executeBatch (cnn: SqlConnection) (sql: string) : Task<unit> =
        task {
            use _ = Bench.scope "deploy.executeBatch"
            let segments = BatchSplitter.splitWithLoudFallback "deploy.executeBatch" sql
            Bench.recordSample "deploy.executeBatch.segments" (int64 segments.Length)
            for segment in segments do
                use _ = Bench.scope "deploy.executeBatch.segment"
                // Slice A.4.7'-prelude.perf-sweep-4 diagnostic: per-segment
                // size sample so the bench rollup surfaces segment-size
                // distribution. The dominant `deploy.executeBatch` cost
                // (~83% of canary wall at production scale) is SQL Server
                // wait time on per-segment ExecuteNonQueryAsync round-
                // trips; surfacing P50/P95/P99 of segment size lets the
                // perf-sweep judge whether parallel-segment dispatch or
                // segment-shape reduction is the higher-leverage target.
                Bench.recordSample "deploy.executeBatch.segment.bytes" (int64 segment.Length)
                use cmd = cnn.CreateCommand()
                cmd.CommandText <- segment
                // Per session-34 — `0` disables the client-side command
                // timeout. SQL Server handles the work; we just wait.
                cmd.CommandTimeout <- 0
                let! _ = cmd.ExecuteNonQueryAsync()
                ()
        }

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
                    eprintfn
                        "deploy.detectParallelism: SQL Server DMV unavailable; falling back to Environment.ProcessorCount = %d (clamped to %d)"
                        clientCpus clamped
                    Bench.recordSample "deploy.detectParallelism.clientCpus" (int64 clientCpus)
                    Bench.recordSample "deploy.detectParallelism.resolved" (int64 clamped)
                    return clamped
                else
                    // Layer 3: static fallback (last resort).
                    eprintfn
                        "deploy.detectParallelism: both SQL Server DMV and Environment.ProcessorCount unavailable; falling back to %d"
                        FallbackParallelism
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
                    eprintfn
                        "PROJECTION_DEPLOY_PARALLELISM='%s' is not a positive integer; auto-detecting"
                        s
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

    /// **Parallel-segment realization of a SQL text batch** (slice
    /// A.4.7'-prelude.perf-sweep-5 primitive; integration deferred per
    /// preflight). Splits via `BatchSplitter` then dispatches segments
    /// across `parallelism` concurrent SqlConnections gated by a
    /// SemaphoreSlim. Each segment runs on its own freshly-opened
    /// connection; `SqlConnection`'s internal pooling (keyed on the
    /// connection string) makes the per-segment new/Open/Dispose cheap
    /// on warm pools.
    ///
    /// **CALLER CONTRACT — segment-ordering independence.** The caller
    /// MUST guarantee that all segments in `sql` are mutually
    /// independent. Violating this contract produces nondeterministic
    /// failures (FK constraints; Phase-1/Phase-2 sequencing; etc.).
    /// True for: a topological-level group of independent kinds at a
    /// single phase. FALSE for: full schema DDL (FK target → FK source);
    /// full data deploy (Phase-1 MERGEs are topologically ordered;
    /// Phase-2 UPDATEs reference Phase-1 rows).
    ///
    /// **Status — landed without integration.** The composer-side
    /// refactor to emit parallel-safe topological-level groups is
    /// deferred to a dedicated architectural slice. This primitive
    /// ships as a ready tool with the preflight contract documented;
    /// the canary continues using sequential `executeBatch` until the
    /// composer exposes safe groups.
    let executeBatchParallel
            (connectionString: string)
            (sql: string)
            (parallelism: int)
            : Task<unit> =
        task {
            use _ = Bench.scope "deploy.executeBatchParallel"
            let segments = BatchSplitter.splitWithLoudFallback "deploy.executeBatchParallel" sql
            Bench.recordSample "deploy.executeBatchParallel.segments" (int64 segments.Length)
            Bench.recordSample "deploy.executeBatchParallel.parallelism" (int64 parallelism)
            use semaphore = new System.Threading.SemaphoreSlim(parallelism, parallelism)
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
                        cmd.CommandTimeout <- 0
                        let! _ = cmd.ExecuteNonQueryAsync()
                        ()
                    finally
                        semaphore.Release() |> ignore
                }
            let tasks = segments |> Array.map runSegment
            let! _ = Task.WhenAll(tasks)
            ()
        }

    /// Default bulk batch size — per session-35 bench, 1000 was
    /// network-round-trip dominant (~12 ms per 1000-row batch on a
    /// warm container). Bumping to 5000 amortizes the round-trip
    /// across 5× the rows; per-row sizes (≈100–500 bytes) keep
    /// batches under ~2.5 MB, well within `SqlBulkCopy` and
    /// transaction-log comfort. Larger batches saturate; smaller
    /// batches pay round-trip overhead linearly.
    [<Literal>]
    let private DefaultBulkBatchSize : int = 5000

    /// Bulk-aware realization of Π's statement stream. Per session-34
    /// — folds consecutive `InsertRow`s for the same `(TableId,
    /// columnShape)` into `SqlBulkCopy` batches; non-`InsertRow`
    /// statements (DDL / IDENTITY toggles) flush via `executeBatch`
    /// so their ordering relative to the inserts is preserved.
    /// Decorative `Comment` / `Blank` statements are skipped — they
    /// belong to `Render.toText`, not to execution.
    ///
    /// **Streaming.** The `seq<Statement>` is consumed lazily; only
    /// the active bulk batch is buffered (≤ `DefaultBulkBatchSize`
    /// rows). DDL accumulates into a text buffer flushed at every
    /// transition into a non-DDL statement, so DDL ordering is
    /// preserved against inserts.
    ///
    /// **Observability.** `streamProbe` taps the upstream statement
    /// stream; per-batch bench scopes (`deploy.bulk.copyRows`,
    /// `deploy.bulk.copyRows.batchSize`) record batch-level
    /// throughput. Operators reading the bench table see the row
    /// count, batch count, total wall time per realization.
    let executeStream (cnn: SqlConnection) (statements: seq<Statement>) : Task<unit> =
        task {
            use _ = Bench.scope "deploy.executeStream"
            let buffer = ResizeArray<CellValue list>()
            let mutable currentTable : TableId option = None
            let mutable currentShape : string list option = None
            let pendingDdl = System.Text.StringBuilder()

            let flushBulk () =
                task {
                    if buffer.Count > 0 then
                        match currentTable with
                        | Some t ->
                            let rows = buffer |> List.ofSeq
                            do! Bulk.copyRows cnn t rows
                        | None -> ()
                        buffer.Clear()
                        currentTable <- None
                        currentShape <- None
                }

            let flushDdl () =
                task {
                    if pendingDdl.Length > 0 then
                        let sql = pendingDdl.ToString()
                        pendingDdl.Clear() |> ignore
                        do! executeBatch cnn sql
                }

            let appendDdl (s: Statement) =
                let sb = System.Text.StringBuilder()
                Render.toSql sb s
                pendingDdl.Append(sb.ToString()) |> ignore

            for s in Bench.streamProbe "deploy.executeStream.input" statements do
                match s with
                | Blank | Comment _ -> ()
                | CreateTable _ ->
                    do! flushBulk ()
                    appendDdl s
                | CreateIndex _ ->
                    // Chapter 4.1.A slice 3: CREATE INDEX is a DDL
                    // statement, same realization shape as CREATE TABLE
                    // (flush bulk inserts before issuing DDL; route
                    // through Render.toSql which delegates to
                    // ScriptDomGenerate per pillar 7).
                    do! flushBulk ()
                    appendDdl s
                | SetExtendedProperty _ ->
                    // Chapter 4.1.A slice 8: EXEC sys.sp_addextendedproperty
                    // is a DDL-class statement (metadata attachment, not
                    // data mutation). Same realization shape as CREATE
                    // INDEX — flush any pending bulk inserts before
                    // issuing the EXEC; route through Render.toSql which
                    // delegates to ScriptDomGenerate.
                    do! flushBulk ()
                    appendDdl s
                | SetIdentityInsert _ ->
                    do! flushBulk ()
                    appendDdl s
                | AlterTableNoCheckConstraint _ ->
                    // Slice 5.13.fk-features-emit (matrix row 59) —
                    // ALTER TABLE ... WITH NOCHECK CHECK CONSTRAINT
                    // is a DDL-class statement; same realization shape
                    // as the other DDL arms (flush bulk inserts before
                    // issuing DDL, then route through Render.toSql
                    // which delegates to ScriptDomGenerate).
                    do! flushBulk ()
                    appendDdl s
                | AlterIndexDisable _ ->
                    // Slice 5.13.index-features-emit (matrix row 55) —
                    // ALTER INDEX … DISABLE is a DDL-class statement;
                    // same realization shape as the other DDL arms.
                    do! flushBulk ()
                    appendDdl s
                | InsertRow (table, values) ->
                    let shape = values |> List.map (fun v -> v.Column)
                    let canAppend =
                        match currentTable, currentShape with
                        | Some t, Some sh -> t = table && sh = shape
                        | _ -> false
                    if not canAppend then
                        do! flushBulk ()
                        do! flushDdl ()
                        currentTable <- Some table
                        currentShape <- Some shape
                    buffer.Add values
                    if buffer.Count >= DefaultBulkBatchSize then
                        do! flushBulk ()
                        currentTable <- Some table
                        currentShape <- Some shape

            do! flushBulk ()
            do! flushDdl ()
        }

    /// `countUserTables` SQL — typed component list joined by
    /// `String.concat " "` per the no-string-concatenation discipline
    /// (`DECISIONS 2026-05-09`). Each clause is a literal at its own
    /// list element; no `+` operators. Pinned per-version constant
    /// so the wire bytes are stable across emits.
    let private countUserTablesSql : string =
        [
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES"
            "WHERE TABLE_TYPE = 'BASE TABLE'"
            "AND TABLE_SCHEMA NOT IN ('sys','INFORMATION_SCHEMA')"
        ]
        |> String.concat " "

    let private countUserTables (cnn: SqlConnection) : Task<int> =
        task {
            use _ = Bench.scope "deploy.countUserTables"
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- countUserTablesSql
            let! tablesObj = cmd.ExecuteScalarAsync()
            return Convert.ToInt32 tablesObj
        }

    /// Environment variable that, when set, points all canary
    /// deploys at a pre-warmed SQL Server container instead of
    /// spinning a fresh ephemeral one each call. Per session-29
    /// operator framing, the bench data showed containerStart
    /// dominates ~75% of every canary's wall time; reusing a single
    /// container across the session collapses that to a one-time
    /// cost paid by the SessionStart hook.
    ///
    /// The connection string must point at the container's `master`
    /// database; the deploy logic still creates per-run
    /// `Source_<guid>` / `Target_<guid>` / `Projection_<guid>`
    /// databases for isolation, so warm-container reuse preserves
    /// the run-level idempotency contract from M2.
    [<Literal>]
    let WarmConnStringEnvVar : string = "PROJECTION_MSSQL_CONN_STR"

    /// Read the warm-container connection string from the environment.
    /// Returns `None` when the env var is unset / blank; returns
    /// `Some connStr` when set AND `SqlConnectionStringBuilder` accepts
    /// it (per `ConnectionString.parse`). Malformed env vars surface
    /// at this validation boundary as a stderr warning + `None`
    /// (canary falls back to ephemeral container) rather than as an
    /// opaque `SqlException` at the first connect call.
    let private warmConnectionString () : string option =
        match Environment.GetEnvironmentVariable WarmConnStringEnvVar with
        | null -> None
        | raw when System.String.IsNullOrWhiteSpace raw -> None
        | raw ->
            match ConnectionString.parse raw with
            | Ok _ -> Some raw
            | Error errors ->
                let codes =
                    errors
                    |> List.map (fun e -> e.Code)
                    |> String.concat ", "  // LINT-ALLOW: terminal stderr-emission boundary; typed code list joined for human-readable warning
                eprintfn
                    "  WARNING: %s is set but malformed (%s); falling back to ephemeral container."
                    WarmConnStringEnvVar
                    codes
                None

    /// Run `body` with a master connection string sourced from either
    /// the warm container (`PROJECTION_MSSQL_CONN_STR` env var) or a
    /// fresh ephemeral Testcontainers SQL Server. Public so canary
    /// tests beyond the runEphemeral / runWithReadback shapes (e.g.,
    /// the chapter 4.1.B CDC-silence canary) can orchestrate multi-
    /// statement deploy + probe + verify cycles without rebuilding
    /// container plumbing per test.
    ///
    /// **Database isolation is preserved either way** — callers create
    /// per-purpose `<prefix>_<guid>` databases via `createDatabase`,
    /// so the warm-container case still gives every canary its own
    /// fresh DB.
    /// Run `body` against a freshly-spun **ephemeral** Testcontainers
    /// SQL Server that is disposed at scope exit — bypasses the warm-
    /// container shortcut even when `PROJECTION_MSSQL_CONN_STR` is
    /// set. Public for tests whose semantic footprint pollutes
    /// instance-wide state (CDC infrastructure, server-level configs,
    /// `master.sys.databases.is_cdc_enabled` flips) and would
    /// therefore contend with concurrent canary tests sharing the
    /// warm container's master.
    ///
    /// **Why CDC needs this** (chapter 4.1.B slice δ observability
    /// cash-out — audit-during-validation discipline). `sys.sp_cdc_
    /// enable_db` + `sp_cdc_enable_table` set up per-DB capture
    /// infrastructure AND flip `master.sys.databases.is_cdc_enabled`
    /// on the parent instance; concurrent CREATE / DROP DATABASE
    /// calls from sibling canaries hold `master`-database locks
    /// that serialize against the CDC scan / capture path. The
    /// resulting livelock is reproducible: running `CdcSilenceTests`
    /// in parallel with `CanaryDeployTests` / `GeneratorScaleTests`
    /// against the same warm container hangs indefinitely. The
    /// `Docker-SqlServer` xUnit collection (see `tests/Projection.
    /// Tests/TestCollections.fs`) serializes the test classes as
    /// the broad fix; this dedicated container is the structural
    /// fix — CDC infrastructure stays in its own SQL Server instance
    /// and never touches the warm container's `master`.
    ///
    /// **Cost.** ~10-20s testcontainers cold-start per call. Callers
    /// that invoke `useEphemeralContainer` per-test pay this cost
    /// per-test; per-test-class amortization belongs in xUnit
    /// `IClassFixture` / `IAsyncLifetime` machinery the caller
    /// arranges.
    /// Handle for a started ephemeral container. The `MasterConnectionString`
    /// is the open contract; `DisposeAsync` reaps the container. Per-test-class
    /// xUnit `IAsyncLifetime` fixtures construct this once and share across
    /// test methods to amortize the ~5-10s container cold-start
    /// (slice A.4.7'-prelude.test-fixture-lift, 2026-05-19).
    type EphemeralContainerHandle =
        {
            MasterConnectionString : string
            DisposeAsync : unit -> Task
        }

    /// Spin up a fresh Testcontainers SQL Server and return a handle whose
    /// `DisposeAsync` reaps it. Sibling to `useEphemeralContainer`: the
    /// scope-based form (`useEphemeralContainer`) owns the lifecycle for
    /// one body; the handle form lets a test-class fixture own the
    /// lifecycle across multiple test methods.
    let acquireEphemeralContainer () : Task<EphemeralContainerHandle> =
        task {
            use _ = Bench.scope "deploy.acquireEphemeralContainer"
            let container =
                MsSqlBuilder()
                    .WithImage(DefaultImage)
                    .WithCleanUp(true)
                    .Build()
            do!
                task {
                    use _ = Bench.scope "deploy.containerStart"
                    return! container.StartAsync()
                }
            return
                {
                    MasterConnectionString = container.GetConnectionString()
                    DisposeAsync = fun () ->
                        task {
                            use _ = Bench.scope "deploy.containerDispose"
                            do! container.DisposeAsync()
                        } :> Task
                }
        }

    let useEphemeralContainer (body: string -> Task<'a>) : Task<'a> =
        task {
            use _ = Bench.scope "deploy.useEphemeralContainer"
            let! handle = acquireEphemeralContainer ()
            try
                return! body handle.MasterConnectionString
            finally
                handle.DisposeAsync().GetAwaiter().GetResult()
        }

    let useContainer (body: string -> Task<'a>) : Task<'a> =
        task {
            use _ = Bench.scope "deploy.useContainer"
            match warmConnectionString () with
            | Some warmConn ->
                use _ = Bench.scope "deploy.useContainer.warm"
                return! body warmConn
            | None ->
                return! useEphemeralContainer body
        }

    /// Bootstrap a fresh per-run database, deploy `seedSql` to it,
    /// then invoke `body` with an open `SqlConnection`. Disposes the
    /// connection on exit; the per-run database is left for the
    /// container's cleanup. Chapter 5.0 slice γ — the OSSYS extraction
    /// canary's orchestration primitive: caller seeds the database with
    /// the synthetic OSSYS schema, then runs the rowsets-SQL extraction
    /// against the seeded source.
    ///
    /// The body's `SqlConnection` is open against the bootstrapped
    /// per-run database. The body is responsible for any exception
    /// handling; this primitive does no error wrapping.
    let withBootstrappedDatabase
            (databaseLabel: string)
            (seedSql: string)
            (body: SqlConnection -> Task<'a>)
            : Task<'a> =
        task {
            use _ = Bench.scope "deploy.withBootstrappedDatabase"
            return!
                useContainer (fun masterConn ->
                    task {
                        let dbName = uniqueDatabaseName DatabaseNameGenerator.guidBased databaseLabel
                        do! createDatabase masterConn dbName
                        let perDbConn = buildPerDbConnectionString masterConn dbName
                        use cnn = new SqlConnection(perDbConn)
                        do! cnn.OpenAsync()
                        do! executeBatch cnn seedSql
                        return! body cnn
                    })
        }

    /// Deploy `sql` to a fresh per-run database in an ephemeral
    /// container; report success / failure. Each call is independent.
    let runEphemeral (sql: string) : Task<Report> =
        task {
            use _ = Bench.scope "deploy.runEphemeral"
            return!
                useContainer (fun masterConn ->
                    task {
                        let dbName = uniqueDatabaseName DatabaseNameGenerator.guidBased "Projection"
                        do! createDatabase masterConn dbName
                        let perDbConn = buildPerDbConnectionString masterConn dbName
                        try
                            use cnn = new SqlConnection(perDbConn)
                            do! cnn.OpenAsync()
                            do! executeBatch cnn sql
                            let! tables = countUserTables cnn
                            return
                                {
                                    Ok = true
                                    Database = dbName
                                    TablesCreated = tables
                                    Errors = []
                                }
                        with
                        | ex ->
                            return
                                {
                                    Ok = false
                                    Database = dbName
                                    TablesCreated = 0
                                    Errors = collectErrors ex
                                }
                    })
        }

    /// Deploy `sql` to a fresh per-run database AND read the deployed
    /// schema back via `Projection.Adapters.Sql.ReadSide.read`.
    /// Returns the `Report` plus the reconstructed `Catalog` (None
    /// when deploy fails).
    ///
    /// This is the V2-internal closure check: any caller can compare
    /// their source Catalog against the reconstructed one via
    /// `PhysicalSchema.ofCatalog` to confirm the emitter preserved
    /// structural intent under deploy.
    let runWithReadback (sql: string) : Task<DeployedResult> =
        task {
            use _ = Bench.scope "deploy.runWithReadback"
            return!
                useContainer (fun masterConn ->
                    task {
                        let dbName = uniqueDatabaseName DatabaseNameGenerator.guidBased "Projection"
                        do! createDatabase masterConn dbName
                        let perDbConn = buildPerDbConnectionString masterConn dbName
                        try
                            use cnn = new SqlConnection(perDbConn)
                            do! cnn.OpenAsync()
                            do! executeBatch cnn sql
                            // Per session-30 Phase 3: TablesCreated is
                            // derived from the readside Catalog instead
                            // of a separate `countUserTables` query.
                            // Saves ~70ms per readback-style call.
                            let! readResult = ReadSide.read cnn
                            let tables =
                                match readResult with
                                | Ok c -> Catalog.allKinds c |> List.length
                                | Error _ -> 0
                            let report =
                                {
                                    Ok = true
                                    Database = dbName
                                    TablesCreated = tables
                                    Errors = []
                                }
                            let catalog =
                                match readResult with
                                | Ok c -> Some c
                                | Error _ -> None
                            return
                                {
                                    Report = report
                                    Reconstructed = catalog
                                }
                        with
                        | ex ->
                            return
                                {
                                    Report =
                                        {
                                            Ok = false
                                            Database = dbName
                                            TablesCreated = 0
                                            Errors = collectErrors ex
                                        }
                                    Reconstructed = None
                                }
                    })
        }

    /// Wide canary: deploy `sourceDdl` to a fresh `Source_*`
    /// database, read it back as `sourceCatalog`, run V2's emitter
    /// (`emit`) on `sourceCatalog` to produce SSDT, deploy that to a
    /// fresh `Target_*` database in the same container, read it back
    /// as `targetCatalog`, and return both Catalogs plus the
    /// `PhysicalSchemaDiff` between them.
    ///
    /// Per `DECISIONS 2026-05-23 — Source SQL Server with OutSystems
    /// semantics is the canary's primary wide integration surface`,
    /// this is the canary's structural-fidelity round-trip: the
    /// source DDL is the operator's reality; the target is V2's
    /// projection of operator intent; an empty `Diff` means V2's
    /// emitter preserved the source's structural intent on the
    /// `(schema, table, column, type, nullable, isPrimaryKey)` axis.
    ///
    /// **Single container, two databases.** ~10x faster than two
    /// containers; databases are isolated by SQL Server. The
    /// container disposes on completion; both databases go with it.
    /// Wide canary, source loader form. Per session-35 — the source
    /// half accepts an arbitrary loader function instead of a fixed
    /// SQL string, so test fixtures can choose between text-batch
    /// (default, for arbitrary external SQL) and bulk realization
    /// (`Bulk.copyRows` for tabular seed data). The string-based
    /// `runWideCanary` is a thin wrapper. Bulk-loading 500k rows
    /// drops from ~10 minutes to a handful of seconds at the cost
    /// of moving from text-INSERT to typed-row form on the source
    /// side — same observable post-state.
    /// Per-phase outcome reified as a typed record. Replaces the
    /// six `let mutable` accumulators flagged by the audit
    /// (`Codebase determinism + non-built-in audit` Lens-2 Tier-1)
    /// — phases now compose via `Task<PhaseOutcome>` returns rather
    /// than caller-side mutation. Each phase is self-contained:
    /// open connection, run work, read back, classify result.
    /// Errors aggregate into `Errors`; `Catalog = Some _` flags
    /// success.
    type private PhaseOutcome =
        {
            Catalog : Catalog option
            Errors  : string list
            Tables  : int
        }

    /// Format a `ValidationError` as a structured diagnostic string.
    /// Per the no-string-concatenation discipline, segments compose
    /// via `String.Concat` over typed components rather than
    /// `sprintf "[%s] %s"`. The canonical "[code] message" surface
    /// form is the V2-internal contract for diagnostic display.
    let private formatValidationError (e: ValidationError) : string =
        String.Concat("[", e.Code, "] ", e.Message)

    /// Phase 1 — load source via caller-supplied loader; read it
    /// back through the SQL adapter. Pure phase: no mutation
    /// observable outside the function; the only side-effect is the
    /// SQL connection's IO, which is the work being delegated. The
    /// `try ... with` wraps around `cnn.OpenAsync()` and
    /// `loadSource cnn`; failures bubble up as `Errors`.
    let private runSourcePhase
        (sourceConn: string)
        (loadSource: SqlConnection -> Task<unit>)
        : Task<PhaseOutcome> =
        task {
            try
                use cnn = new SqlConnection(sourceConn)
                do! cnn.OpenAsync()
                do! loadSource cnn
                let! readResult = ReadSide.read cnn
                return
                    match readResult with
                    | Ok c ->
                        {
                            Catalog = Some c
                            Errors = []
                            Tables = Catalog.allKinds c |> List.length
                        }
                    | Error errors ->
                        {
                            Catalog = None
                            Errors = errors |> List.map formatValidationError
                            Tables = 0
                        }
            with
            | ex ->
                return
                    {
                        Catalog = None
                        Errors = collectErrors ex
                        Tables = 0
                    }
        }

    /// Phase 2 — execute V2's emitted statement stream via the
    /// bulk-aware realization, then read the target back. Same
    /// shape as `runSourcePhase`; same purity discipline.
    let private runTargetPhase
        (targetConn: string)
        (stmts: seq<Statement>)
        : Task<PhaseOutcome> =
        task {
            try
                use cnn = new SqlConnection(targetConn)
                do! cnn.OpenAsync()
                do! executeStream cnn stmts
                let! readResult = ReadSide.read cnn
                return
                    match readResult with
                    | Ok c ->
                        {
                            Catalog = Some c
                            Errors = []
                            Tables = Catalog.allKinds c |> List.length
                        }
                    | Error errors ->
                        {
                            Catalog = None
                            Errors = errors |> List.map formatValidationError
                            Tables = 0
                        }
            with
            | ex ->
                return
                    {
                        Catalog = None
                        Errors = collectErrors ex
                        Tables = 0
                    }
        }

    let private aggregateFailure
        (code: string)
        (errors: string list)
        : Result<'a> =
        errors
        |> List.map (fun s -> ValidationError.create code s)
        |> Result.failure

    let runWideCanaryWithLoader
        (loadSource: SqlConnection -> Task<unit>)
        (emit: Catalog -> seq<Statement>)
        : Task<Result<WideCanaryReport>> =
        task {
            use _ = Bench.scope "deploy.runWideCanary"
            return!
                useContainer (fun masterConn ->
                    task {
                        let sourceDbName = uniqueDatabaseName DatabaseNameGenerator.guidBased "Source"
                        let targetDbName = uniqueDatabaseName DatabaseNameGenerator.guidBased "Target"

                        // Phase 1: load source, read it back.
                        do!
                            task {
                                use _ = Bench.scope "deploy.runWideCanary.sourcePhase"
                                do! createDatabase masterConn sourceDbName
                            }
                        let sourceConn = buildPerDbConnectionString masterConn sourceDbName
                        let! sourceOutcome = runSourcePhase sourceConn loadSource

                        match sourceOutcome.Catalog with
                        | None ->
                            return aggregateFailure "wideCanary.source.failed" sourceOutcome.Errors
                        | Some src ->
                            let sourceReport =
                                {
                                    Ok = true
                                    Database = sourceDbName
                                    TablesCreated = sourceOutcome.Tables
                                    Errors = sourceOutcome.Errors
                                }

                            // Phase 2: emit Π output, deploy, read back.
                            let stmts =
                                use _ = Bench.scope "deploy.runWideCanary.emit"
                                emit src
                            do!
                                task {
                                    use _ = Bench.scope "deploy.runWideCanary.targetPhase"
                                    do! createDatabase masterConn targetDbName
                                }
                            let targetConn = buildPerDbConnectionString masterConn targetDbName
                            let! targetOutcome = runTargetPhase targetConn stmts

                            match targetOutcome.Catalog with
                            | None ->
                                return aggregateFailure "wideCanary.target.failed" targetOutcome.Errors
                            | Some tgt ->
                                let targetReport =
                                    {
                                        Ok = true
                                        Database = targetDbName
                                        TablesCreated = targetOutcome.Tables
                                        Errors = targetOutcome.Errors
                                    }
                                let diff =
                                    use _ = Bench.scope "deploy.runWideCanary.diff"
                                    let sourceSchema = PhysicalSchema.ofCatalog src
                                    let targetSchema = PhysicalSchema.ofCatalog tgt
                                    PhysicalSchema.diff sourceSchema targetSchema
                                return
                                    Result.success
                                        {
                                            Source = src
                                            Target = tgt
                                            Diff = diff
                                            SourceReport = sourceReport
                                            TargetReport = targetReport
                                        }
                    })
        }

    /// String-form wrapper around `runWideCanaryWithLoader`.
    /// Equivalent to `runWideCanaryWithLoader (fun cnn -> executeBatch
    /// cnn sourceDdl)`; preserved for callers passing arbitrary SQL
    /// scripts (the historical canary surface).
    let runWideCanary
        (sourceDdl: string)
        (emit: Catalog -> seq<Statement>)
        : Task<Result<WideCanaryReport>> =
        runWideCanaryWithLoader (fun cnn -> executeBatch cnn sourceDdl) emit

    /// End-to-end: parse a V1 `osm_model.json` from disk, project
    /// through the three sibling Π's, and deploy the SSDT. Returns
    /// the `Report` from `runEphemeral` along with the artifact
    /// strings that fed the deploy.
    let runFromV1Json (jsonPath: string) : Task<Result<Compose.Outputs * Report>> =
        task {
            let! parsed = Compose.read jsonPath
            match parsed with
            | Ok catalog ->
                let outputs = Compose.project EmissionPolicy.empty catalog
                // Per Tier-1 #2 (Outputs.Sql → SsdtBundle): aggregate
                // the bundle's per-table SQL files into one batch
                // for the ephemeral-deploy single-string contract.
                // Production deploys iterate the bundle (one file per
                // SSDT artifact); this dogfood path keeps the legacy
                // single-batch deploy via `aggregateSsdt`.
                let! report = runEphemeral (Compose.aggregateSsdt outputs.SsdtBundle)
                return Result.success (outputs, report)
            | Error errors ->
                return Result.failure errors
        }
