namespace Projection.Pipeline

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
            Success : bool
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
        let private socketCandidates : string list =
            [
                "/var/run/docker.sock"
                sprintf
                    "%s/.docker/run/docker.sock"
                    (Environment.GetFolderPath Environment.SpecialFolder.UserProfile)
            ]

        // Why these specific values (not arbitrary):
        // - `ProbeTimeoutMs = 2000`: `docker version` against a healthy
        //   daemon returns in tens of ms; 2 s is the slack for a
        //   loaded host. Larger timeouts hide a wedged daemon
        //   (downstream canary work fails anyway). 2 s is the smallest
        //   value that doesn't false-negative under host load.
        // - `BringupBudgetMs = 30000`: dockerd cold-start measured at
        //   1-3 s in this environment; 30 s is ~10× the observed p99,
        //   covering pathological cases (TLS clock-skew retries, host
        //   memory pressure during boot).
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
        let private BringupBudgetMs : int = 30_000

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

        /// JIT bring-up. Returns true iff Docker is usable for canary
        /// work. Tests gate on this for clean skip-if-unavailable
        /// semantics that survive a mid-session daemon drop.
        let ensureRunning () : bool =
            if not (isAvailable ()) then false
            elif isResponding () then true
            else startDaemon ()

    [<Literal>]
    let private DefaultImage : string =
        "mcr.microsoft.com/mssql/server:2022-latest"

    let private uniqueDatabaseName (prefix: string) : string =
        sprintf "%s_%s" prefix ((Guid.NewGuid().ToString "N").Substring(0, 12))

    let private collectErrors (ex: exn) : string list =
        match ex with
        | :? SqlException as sql ->
            [
                for e in sql.Errors ->
                    sprintf
                        "[severity=%d error=%d line=%d] %s"
                        e.Class
                        e.Number
                        e.LineNumber
                        e.Message
            ]
        | _ -> [ ex.Message ]

    let private buildPerDbConnectionString (master: string) (dbName: string) : string =
        let b = SqlConnectionStringBuilder(master)
        b.InitialCatalog <- dbName
        b.ConnectionString

    let private createDatabase (masterConn: string) (dbName: string) : Task<unit> =
        task {
            use _ = Bench.scope "deploy.createDatabase"
            use cnn = new SqlConnection(masterConn)
            do! cnn.OpenAsync()
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sprintf "CREATE DATABASE [%s];" dbName
            let! _ = cmd.ExecuteNonQueryAsync()
            return ()
        }

    /// Sqlcmd-style `GO` batch separator. Lines whose trimmed value
    /// equals `GO` (case-insensitive) split a script into independent
    /// batches; SqlClient's `ExecuteNonQueryAsync` runs one segment
    /// at a time. Per session-34 — large fixture loads (≥ 50k
    /// INSERTs) routinely blow past sensible per-batch sizes; chunk
    /// emission + splitting keeps each round-trip bounded without
    /// changing SQL semantics. Scripts without `GO` markers run as
    /// a single segment, identical to the v1 behaviour.
    ///
    /// Per the no-regex / no-string-concatenation discipline
    /// (`DECISIONS 2026-05-09 — No-string-concatenation / no-regex
    /// discipline`), the splitter uses built-in `String.Split('\n')`
    /// + `Trim` + literal compare via
    /// `System.String.Equals(_, "GO", OrdinalIgnoreCase)` instead of
    /// `System.Text.RegularExpressions.Regex`. Lines are accumulated
    /// per-segment via `String.concat "\n"` (built-in joiner). The
    /// fold preserves segment order without mutation; segments with
    /// only whitespace are filtered.
    let private isGoLine (line: string) : bool =
        System.String.Equals(
            line.Trim(),
            "GO",
            System.StringComparison.OrdinalIgnoreCase)

    let private splitOnGo (sql: string) : string[] =
        let lines = sql.Split('\n')
        // Walk lines via fold; each GO-line closes a segment. The
        // accumulator carries `current` (lines of the in-flight
        // segment, reverse-ordered) and `segs` (closed segments,
        // reverse-ordered). At end, the final `current` is the
        // tail segment.
        let initial : string list * string list list = [], []
        let lastSegRev, segsRev =
            lines
            |> Array.fold
                (fun (current, segs) line ->
                    if isGoLine line then [], current :: segs
                    else line :: current, segs)
                initial
        let allSegsRev = lastSegRev :: segsRev
        allSegsRev
        |> List.rev
        |> List.map (fun lineListRev ->
            lineListRev |> List.rev |> String.concat "\n")
        |> List.filter (fun s -> not (System.String.IsNullOrWhiteSpace s))
        |> List.toArray

    /// Public realization of a SQL text batch. Splits on `^GO$`
    /// markers (sqlcmd-style); each segment runs in its own
    /// round-trip with no client-side timeout. Test fixtures (and
    /// arbitrary external SQL scripts) consume this directly to
    /// load source data; V2's own emit path goes through
    /// `executeStream` with the typed statement seq.
    let executeBatch (cnn: SqlConnection) (sql: string) : Task<unit> =
        task {
            use _ = Bench.scope "deploy.executeBatch"
            let segments = splitOnGo sql
            Bench.recordSample "deploy.executeBatch.segments" (int64 segments.Length)
            for segment in segments do
                use _ = Bench.scope "deploy.executeBatch.segment"
                use cmd = cnn.CreateCommand()
                cmd.CommandText <- segment
                // Per session-34 — `0` disables the client-side command
                // timeout. SQL Server handles the work; we just wait.
                cmd.CommandTimeout <- 0
                let! _ = cmd.ExecuteNonQueryAsync()
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
                | SetIdentityInsert _ ->
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

    let private warmConnectionString () : string option =
        match Environment.GetEnvironmentVariable WarmConnStringEnvVar with
        | null -> None
        | "" -> None
        | s when System.String.IsNullOrWhiteSpace s -> None
        | s -> Some s

    /// Run the body against a master connection string. Honors the
    /// `PROJECTION_MSSQL_CONN_STR` env var if set (warm container,
    /// no startup cost); otherwise spins an ephemeral container and
    /// disposes it on completion.
    ///
    /// Database isolation is preserved either way — callers create
    /// per-purpose `<prefix>_<guid>` databases via `createDatabase`,
    /// so the warm-container case still gives every canary its own
    /// fresh DB.
    let private useContainer (body: string -> Task<'a>) : Task<'a> =
        task {
            use _ = Bench.scope "deploy.useContainer"
            match warmConnectionString () with
            | Some warmConn ->
                use _ = Bench.scope "deploy.useContainer.warm"
                return! body warmConn
            | None ->
                let container =
                    MsSqlBuilder()
                        .WithImage(DefaultImage)
                        .WithCleanUp(true)
                        .Build()
                try
                    do!
                        task {
                            use _ = Bench.scope "deploy.containerStart"
                            return! container.StartAsync()
                        }
                    let masterConn = container.GetConnectionString()
                    return! body masterConn
                finally
                    use _ = Bench.scope "deploy.containerDispose"
                    container.DisposeAsync().AsTask().GetAwaiter().GetResult()
        }

    /// Deploy `sql` to a fresh per-run database in an ephemeral
    /// container; report success / failure. Each call is independent.
    let runEphemeral (sql: string) : Task<Report> =
        task {
            use _ = Bench.scope "deploy.runEphemeral"
            return!
                useContainer (fun masterConn ->
                    task {
                        let dbName = uniqueDatabaseName "Projection"
                        do! createDatabase masterConn dbName
                        let perDbConn = buildPerDbConnectionString masterConn dbName
                        try
                            use cnn = new SqlConnection(perDbConn)
                            do! cnn.OpenAsync()
                            do! executeBatch cnn sql
                            let! tables = countUserTables cnn
                            return
                                {
                                    Success = true
                                    Database = dbName
                                    TablesCreated = tables
                                    Errors = []
                                }
                        with
                        | ex ->
                            return
                                {
                                    Success = false
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
                        let dbName = uniqueDatabaseName "Projection"
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
                                | Success c -> Catalog.allKinds c |> List.length
                                | Failure _ -> 0
                            let report =
                                {
                                    Success = true
                                    Database = dbName
                                    TablesCreated = tables
                                    Errors = []
                                }
                            let catalog =
                                match readResult with
                                | Success c -> Some c
                                | Failure _ -> None
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
                                            Success = false
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
    let runWideCanaryWithLoader
        (loadSource: SqlConnection -> Task<unit>)
        (emit: Catalog -> seq<Statement>)
        : Task<Result<WideCanaryReport>> =
        task {
            use _ = Bench.scope "deploy.runWideCanary"
            return!
                useContainer (fun masterConn ->
                    task {
                        let sourceDbName = uniqueDatabaseName "Source"
                        let targetDbName = uniqueDatabaseName "Target"

                        // Phase 1: load source via caller-supplied loader,
                        // read it back.
                        do!
                            task {
                                use _ = Bench.scope "deploy.runWideCanary.sourcePhase"
                                do! createDatabase masterConn sourceDbName
                            }
                        let sourceConn = buildPerDbConnectionString masterConn sourceDbName

                        let mutable sourceCatalog : Catalog option = None
                        let mutable sourceErrors : string list = []
                        let mutable sourceTables = 0

                        try
                            use cnn = new SqlConnection(sourceConn)
                            do! cnn.OpenAsync()
                            do! loadSource cnn
                            // Phase 3: derive TablesCreated from the
                            // readside Catalog instead of running a
                            // separate countUserTables query.
                            let! readResult = ReadSide.read cnn
                            match readResult with
                            | Success c ->
                                sourceCatalog <- Some c
                                sourceTables <- Catalog.allKinds c |> List.length
                            | Failure errors ->
                                sourceErrors <-
                                    errors
                                    |> List.map (fun e -> sprintf "[%s] %s" e.Code e.Message)
                        with
                        | ex ->
                            sourceErrors <- collectErrors ex

                        match sourceCatalog with
                        | None ->
                            let aggregated =
                                sourceErrors
                                |> List.map (fun s ->
                                    ValidationError.create "wideCanary.source.failed" s)
                            return Result.failure aggregated
                        | Some src ->
                            let sourceReport =
                                {
                                    Success = true
                                    Database = sourceDbName
                                    TablesCreated = sourceTables
                                    Errors = sourceErrors
                                }

                            // Phase 2: emit V2's SSDT statement stream,
                            // execute via the bulk-aware realization,
                            // read the target back.
                            let stmts =
                                use _ = Bench.scope "deploy.runWideCanary.emit"
                                emit src
                            do!
                                task {
                                    use _ = Bench.scope "deploy.runWideCanary.targetPhase"
                                    do! createDatabase masterConn targetDbName
                                }
                            let targetConn = buildPerDbConnectionString masterConn targetDbName

                            let mutable targetCatalog : Catalog option = None
                            let mutable targetErrors : string list = []
                            let mutable targetTables = 0

                            try
                                use cnn = new SqlConnection(targetConn)
                                do! cnn.OpenAsync()
                                do! executeStream cnn stmts
                                // Phase 3: derive TablesCreated from the
                                // readside Catalog (same optimization as
                                // the source phase).
                                let! readResult = ReadSide.read cnn
                                match readResult with
                                | Success c ->
                                    targetCatalog <- Some c
                                    targetTables <- Catalog.allKinds c |> List.length
                                | Failure errors ->
                                    targetErrors <-
                                        errors
                                        |> List.map (fun e -> sprintf "[%s] %s" e.Code e.Message)
                            with
                            | ex ->
                                targetErrors <- collectErrors ex

                            match targetCatalog with
                            | None ->
                                let aggregated =
                                    targetErrors
                                    |> List.map (fun s ->
                                        ValidationError.create "wideCanary.target.failed" s)
                                return Result.failure aggregated
                            | Some tgt ->
                                let targetReport =
                                    {
                                        Success = true
                                        Database = targetDbName
                                        TablesCreated = targetTables
                                        Errors = targetErrors
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
            | Success catalog ->
                let outputs = Compose.project catalog
                let! report = runEphemeral outputs.Sql
                return Result.success (outputs, report)
            | Failure errors ->
                return Result.failure errors
        }
