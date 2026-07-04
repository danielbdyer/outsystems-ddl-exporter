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

    /// Docker availability + lazy JIT bring-up. The implementation
    /// lives in the concept-named `DockerDaemon` module (container-
    /// daemon lifecycle); this nested `Deploy.Docker` facade forwards
    /// to it so every existing `Deploy.Docker.*` call site
    /// (`isAvailable`, `ensureRunning`, `resetMemo`) is untouched.
    /// Per session-36 — the session-start hook starts dockerd at
    /// session boot, but the daemon can drop mid-session (OOM, signal,
    /// environment shift); `ensureRunning` probes responsiveness and
    /// re-starts the daemon best-effort so canary tests don't fail
    /// spuriously.
    [<RequireQualifiedAccess>]
    module Docker =
        /// Static probe: env var or socket file present. Cheap; doesn't
        /// contact the daemon. Forwards to `DockerDaemon.isAvailable`.
        let isAvailable () : bool = DockerDaemon.isAvailable ()

        /// JIT bring-up (memoized). Forwards to
        /// `DockerDaemon.ensureRunning`.
        let ensureRunning () : bool = DockerDaemon.ensureRunning ()

        /// Clear the memoized `ensureRunning` result. Forwards to
        /// `DockerDaemon.resetMemo`.
        let resetMemo () : unit = DockerDaemon.resetMemo ()

    [<Literal>]
    let private DefaultImage : string =
        "mcr.microsoft.com/mssql/server:2022-latest"

    /// **First-class non-determinism boundary.** `DatabaseNameGenerator
    /// = unit -> string` is the typed seam through which `Guid.NewGuid`
    /// (or any test-pinned counter) enters Pipeline. The type + the
    /// default generator live in the concept-named
    /// `DatabaseNameGenerator` module; re-exposed here as
    /// `Deploy.DatabaseNameGenerator` (the type abbreviation plus a
    /// nested facade) so existing references are untouched.
    type DatabaseNameGenerator = Projection.Pipeline.DatabaseNameGenerator

    [<RequireQualifiedAccess>]
    module DatabaseNameGenerator =
        /// Default `DatabaseNameGenerator` — uses `Guid.NewGuid`.
        /// Forwards to `DatabaseNameGenerator.guidBased`.
        let guidBased : DatabaseNameGenerator =
            Projection.Pipeline.DatabaseNameGenerator.guidBased

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
    /// behind `SqlConnectionStringBuilder`). The implementation lives
    /// in the concept-named `DeployConnectionString` module; this
    /// nested `Deploy.ConnectionString` facade forwards to it so every
    /// existing `Deploy.ConnectionString.*` call site (`parse`,
    /// `buildPerDb`) is untouched.
    [<RequireQualifiedAccess>]
    module ConnectionString =
        /// Parse a connection string into a validated typed builder.
        /// Forwards to `DeployConnectionString.parse`.
        let parse (connStr: string) : Result<SqlConnectionStringBuilder> =
            DeployConnectionString.parse connStr

        /// Build a per-database connection string from a master
        /// connection string. Forwards to
        /// `DeployConnectionString.buildPerDb`.
        let buildPerDb (master: string) (dbName: string) : string =
            DeployConnectionString.buildPerDb master dbName

    let private buildPerDbConnectionString (master: string) (dbName: string) : string =
        ConnectionString.buildPerDb master dbName

    /// `CREATE DATABASE [<dbName>];` — Tier-3.4: rendered from a typed
    /// `ScriptDomBuild.buildCreateDatabase` (`CreateDatabaseStatement`), replacing
    /// the prior `String.Concat`. `dbName` flows through the typed
    /// `Identifier` (square-bracket quoting, `]` doubled), so the canonical
    /// identifier-encoding guarantee holds structurally rather than by a hand
    /// `Render.quote` call.
    let private createDatabaseSql (dbName: string) : string =
        ScriptDomGenerate.generateOne (ScriptDomBuild.buildCreateDatabase dbName)

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
                cmd.CommandTimeout <- CommandTimeoutPolicy.resolve ()
                let! _ = cmd.ExecuteNonQueryAsync()
                ()
        }

    /// PL-6 (S14) — realization of PRE-SPLIT batch segments: the caller
    /// holds the batch structure it assembled from typed statements
    /// moments earlier (a joined Phase-2 UPDATE list, one wipe DELETE,
    /// one rename), so the `TSql160Parser` re-parse `executeBatch` pays
    /// to REDISCOVER those boundaries is passed by construction. Each
    /// caller passes exactly the segment list the splitter would have
    /// produced from its GO-free text (for the no-`GO` case: the
    /// singleton), so the per-segment command texts and round-trip
    /// count are unchanged. Semantics mirror the splitter's output
    /// (per-segment `Trim`, whitespace-only segments dropped).
    /// `executeBatch` remains the entry for text of unknown provenance
    /// (operator-supplied scripts may carry `GO`).
    let executeSegments (cnn: SqlConnection) (segments: string list) : Task<unit> =
        task {
            use _ = Bench.scope "deploy.executeSegments"
            let cleaned =
                segments
                |> List.map (fun s -> s.Trim())
                |> List.filter (fun s -> s <> "")
            Bench.recordSample "deploy.executeSegments.segments" (int64 (List.length cleaned))
            for segment in cleaned do
                use _ = Bench.scope "deploy.executeSegments.segment"
                Bench.recordSample "deploy.executeSegments.segment.bytes" (int64 segment.Length)
                use cmd = cnn.CreateCommand()
                cmd.CommandText <- segment
                cmd.CommandTimeout <- CommandTimeoutPolicy.resolve ()
                let! _ = cmd.ExecuteNonQueryAsync()
                ()
        }

    /// X4 / X8 / X5 — the change-measure ‖·‖ (`WAVE_6_ALGEBRA.md`),
    /// physically the CDC capture count, as a deploy-time measurement
    /// primitive: force a synchronous capture pass, then sum the production
    /// reader (`ReadSide.cdcCaptureCount`) over every table the deployed DB
    /// tracks. The scan makes the measurement immediate — Agent-free
    /// containers never run the capture job, so without it the CT tables stay
    /// empty. When the capture job IS running (a real CDC deployment with SQL
    /// Agent) the manual scan is refused; that refusal is benign (captures
    /// land automatically) and swallowed, so the count is still the truth.
    /// No CDC tracking ⇒ 0 by the empty fold. Callers bracket an action with
    /// this (baseline → act → post) to surface the captures the action fired.
    ///
    /// NM-54 — this is a MEASURE: if the CDC axis can't be read (the tracked
    /// probe or the capture-count probe refuses), the count CANNOT be taken, and
    /// fabricating 0 would silently understate the change-norm. Both probes now
    /// return a NAMED `Result`; this surfaces the Error rather than swallowing it.
    let cdcCaptureTotal (cnn: SqlConnection) : Task<Result<int>> =
        task {
            match! ReadSide.cdcTrackedTables cnn with
            | Error es -> return Result.failure es
            | Ok tracked ->
                if List.isEmpty tracked then return Result.success 0
                else
                    try do! executeBatch cnn "EXEC sys.sp_cdc_scan;"
                    with _ -> ()  // capture job already running ⇒ captures land automatically
                    return! ReadSide.cdcCaptureCount cnn tracked
        }

    /// Deploy-dispatch parallelism auto-detection, the per-connection-
    /// string cache, the Max-Pool-Size cap, and the `ParallelSafe`-gated
    /// parallel batch realization primitive now live in the concept-
    /// named `DeployParallelism` module (R2/B7 decomposition); these
    /// thin forwards keep every existing `Deploy.detectParallelism` /
    /// `Deploy.resolveParallelism` / `Deploy.executeBatchParallel` call
    /// site (production code, canaries, `ExecuteBatchParallelTests.fs`)
    /// untouched.
    let detectParallelism : string -> Task<int> =
        DeployParallelism.detectParallelism

    let resolveParallelism : string -> Task<int> =
        DeployParallelism.resolveParallelism

    let executeBatchParallel : string -> ParallelSafe<string> -> int -> Task<unit> =
        DeployParallelism.executeBatchParallel

    /// **Production leveled seed dispatch (card P2)** — the ONE owner of
    /// the leveled deploy order, so faces cannot misorder it: Phase-1
    /// levels in level order, THEN Phase-2 levels in level order; levels
    /// are sequential (level k+1 may depend on level k); only WITHIN a
    /// level does `executeBatchParallel` dispatch concurrently, licensed
    /// by the `ParallelSafe` token each level carries. Parallelism rides
    /// the existing resolution stack (`resolveParallelism`: the
    /// `PROJECTION_DEPLOY_PARALLELISM` operator override, else the DMV
    /// probe cached per connection string, else 4) and the Max-Pool-Size
    /// cap inside `executeBatchParallel`.
    ///
    /// The connection STRING (not an open connection) is the argument
    /// because every segment opens its own pooled connection — the seam
    /// the P2 card named. Error semantics: a failing segment surfaces
    /// from its level's `Task.WhenAll` and no later level starts; the
    /// seed is an idempotent MERGE, so a re-run after a partial failure
    /// converges (and is CDC-silent on the already-landed rows).
    let executeLeveledSeed
            (connectionString: string)
            (plan: Projection.Targets.Data.DataEmissionComposer.LeveledDeploymentText)
            : Task<unit> =
        task {
            use _ = Bench.scope "deploy.executeLeveledSeed"
            let! parallelism = resolveParallelism connectionString
            for level in plan.Phase1Levels do
                do! executeBatchParallel connectionString level parallelism
            for level in plan.Phase2Levels do
                do! executeBatchParallel connectionString level parallelism
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
    /// `executeStream` with an explicit bulk batch size — the sibling
    /// wrapper supplies `DefaultBulkBatchSize` (the F# default-argument
    /// idiom). The knob exists for the perf harness's batch sweep
    /// (PERF_HARNESS §4 slice 3 / CONSTELLATION_BACKLOG H4); production
    /// callers use `executeStream`.
    let executeStreamWith (batchSize: int) (cnn: SqlConnection) (statements: seq<Statement>) : Task<unit> =
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
                // PL-6 (S33): `Render.toSql` writes into any StringBuilder —
                // the pending-DDL buffer IS the carrier (the prior throwaway
                // local builder paid each statement's text two more times:
                // ToString + Append).
                Render.toSql pendingDdl s

            for s in Bench.streamProbe "deploy.executeStream.input" statements do
                match s with
                | Blank | Comment _ -> ()
                | BatchSeparator ->
                    // Slice D.2.c — `BatchSeparator` materialises as
                    // `GO` in the rendered text; the streaming deploy
                    // path treats it the same as Blank (no DDL effect),
                    // because the per-statement DDL flushes already
                    // segment-by-segment via `appendDdl` / `flushBulk`.
                    ()
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
                | Statement.Merge _ | Statement.Update _ ->
                    // The MERGE/UPDATE data-population variants. The SSDT-DDL
                    // deploy stream does not currently carry these (the data
                    // lane deploys via its own rendered path); they are
                    // executable SQL, so route them through the same
                    // flush-then-execute shape as the other non-bulk statements.
                    do! flushBulk ()
                    appendDdl s
                | AlterTableNoCheckConstraint _ | AlterTableDisableConstraint _ ->
                    // Slice 5.13.fk-features-emit (matrix row 59) +
                    // 6.A.6 — ALTER TABLE … NOCHECK CONSTRAINT (disable)
                    // and ALTER TABLE … WITH NOCHECK CHECK CONSTRAINT
                    // (re-enable skipping validation) are DDL-class
                    // statements; same realization shape as the other DDL
                    // arms (flush bulk inserts before issuing DDL, then
                    // route through Render.toSql which delegates to
                    // ScriptDomGenerate).
                    do! flushBulk ()
                    appendDdl s
                | AlterIndexDisable _ ->
                    // Slice 5.13.index-features-emit (matrix row 55) —
                    // ALTER INDEX … DISABLE is a DDL-class statement;
                    // same realization shape as the other DDL arms.
                    do! flushBulk ()
                    appendDdl s
                | CreateTrigger _ ->
                    // H-019: CREATE TRIGGER is DDL; flush any pending
                    // bulk inserts before issuing. Render.toSql delegates
                    // to ScriptDomBuild.tryParseTriggerBody; if the
                    // definition fails to parse, the statement produces
                    // no SQL and the flush is a no-op.
                    do! flushBulk ()
                    appendDdl s
                | AlterTableDisableTrigger _ ->
                    // Slice D.2.d — ALTER TABLE ... DISABLE TRIGGER is
                    // DDL; same realization shape as the CreateTrigger
                    // sibling above. Flush bulk inserts; route through
                    // Render.toSql which delegates to ScriptDomBuild's
                    // AlterTableTriggerModificationStatement builder.
                    do! flushBulk ()
                    appendDdl s
                | CreateSequence _ ->
                    // H-020: CREATE SEQUENCE is DDL; sequences precede
                    // tables in the statement stream so they must flush
                    // any stale bulk state before executing.
                    do! flushBulk ()
                    appendDdl s
                | AlterTableAddColumn _ | AlterTableAlterColumn _ | AlterTableAddForeignKey _
                | AlterTableDropColumn _ | AlterTableDropConstraint _ | DropIndex _ | DropSequence _ ->
                    // 6.A.12 + C1: minimum-viable-touch migration DDL (ALTER
                    // TABLE ADD / ALTER / DROP COLUMN; ADD / DROP CONSTRAINT;
                    // DROP INDEX / SEQUENCE) — same realization shape as the
                    // other DDL arms — flush pending bulk inserts, then route
                    // through Render.toSql.
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
                    if buffer.Count >= batchSize then
                        do! flushBulk ()
                        currentTable <- Some table
                        currentShape <- Some shape

            do! flushBulk ()
            do! flushDdl ()
        }

    let executeStream (cnn: SqlConnection) (statements: seq<Statement>) : Task<unit> =
        executeStreamWith DefaultBulkBatchSize cnn statements

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
            let! raw = cmd.ExecuteScalarAsync()
            // Defensive DBNull/null handling (slice
            // A.4.7'-prelude.defensive-hardening): mirror the
            // `tryProbeServerCpus` template — `Convert.ToInt32 null`
            // returns 0 silently, masking a connection failure as
            // "no tables in DB." Pattern-match on the boxed result.
            match raw with
            | null -> return 0
            | :? DBNull -> return 0
            | other -> return Convert.ToInt32 other
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
                    |> String.concat ", "  // LINT-ALLOW: typed code list joined for the envelope payload
                LogSink.emit
                    (LogSink.envelope LogSink.Warn LogSink.Deploy "deploy.warmConnMalformed"
                        (Map.ofList [ "envVar", box WarmConnStringEnvVar; "codes", box codes ]))
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

    /// Warm-honoring handle acquisition — the per-test-class fixture's
    /// container source. When `PROJECTION_MSSQL_CONN_STR` is set (the
    /// dev-loop warm container or any reachable SQL Server), returns a
    /// handle wrapping that master connection with a **no-op
    /// `DisposeAsync`** (the warm container outlives the test process —
    /// per-test isolation still holds because callers create
    /// `<prefix>_<guid>` databases on it); otherwise falls back to
    /// `acquireEphemeralContainer ()` (a fresh Testcontainers SQL
    /// Server reaped on dispose). Handle-shaped sibling to
    /// `useContainer` — same warm-or-ephemeral choice, for
    /// `IClassFixture` lifetimes. **CDC / instance-wide-state fixtures
    /// must call `acquireEphemeralContainer ()` directly** to stay
    /// isolated from the shared warm instance.
    let acquireContainer () : Task<EphemeralContainerHandle> =
        match warmConnectionString () with
        | Some warmConn ->
            task {
                return
                    { MasterConnectionString = warmConn
                      DisposeAsync = fun () -> Task.CompletedTask }
            }
        | None -> acquireEphemeralContainer ()

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
    /// connection AND reaps the per-run database on exit (best-effort).
    /// Chapter 5.0 slice γ — the OSSYS extraction canary's orchestration
    /// primitive: caller seeds the database with the synthetic OSSYS
    /// schema, then runs the rowsets-SQL extraction against the seeded
    /// source.
    ///
    /// **Why the reap (2026-06-12).** The original "left for the
    /// container's cleanup" was correct only for true ephemeral
    /// containers. Under the warm dev-loop container the per-run
    /// databases ACCUMULATED for the instance's lifetime — 209 counted
    /// after one session's runs — and that accumulation is the
    /// degradation behind the warm container's
    /// `insufficient system memory in resource pool 'default'` failures
    /// (CLAUDE.md §4 rule 2's third signature, root-caused). The drop is
    /// cheap (~50 ms) because the per-DB pool is evicted first — an idle
    /// pooled session makes SINGLE_USER WITH ROLLBACK IMMEDIATE cost
    /// ~3 s (measured) while an evicted one costs ~50 ms.
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
                        try
                            use cnn = new SqlConnection(perDbConn)
                            do! cnn.OpenAsync()
                            do! executeBatch cnn seedSql
                            return! body cnn
                        finally
                            try
                                let cnnEvict = new SqlConnection(perDbConn)
                                SqlConnection.ClearPool cnnEvict
                                cnnEvict.Dispose()
                                use cnnDrop = new SqlConnection(masterConn)
                                cnnDrop.OpenAsync().GetAwaiter().GetResult()
                                executeBatch cnnDrop
                                    // LINT-ALLOW: terminal scratch-container teardown SQL.
                                    //   (1) Why string, not typed AST? `SET SINGLE_USER WITH ROLLBACK
                                    //       IMMEDIATE` has no clean typed node in ScriptDom 161
                                    //       (`DatabaseOptionKind` omits the access-mode option). (Tier-3.4
                                    //       typed the paired `CREATE DATABASE`; this teardown stays here.)
                                    //   (2) Injection-safe? Yes — the only interpolation is `dbName` via
                                    //       `Render.quote` (`Identifier.EncodeIdentifier`, `]` doubled); the
                                    //       rest is fixed keywords.
                                    //   (3) Terminal boundary? Yes — best-effort cleanup of a throwaway
                                    //       per-test container DB, never the production emit→deploy path.
                                    //   (4) Validated? By execution against the scratch master; a failure is
                                    //       swallowed (`with _ -> ()`) — the container is discarded anyway.
                                    (System.String.Concat(
                                        "ALTER DATABASE ", Render.quote dbName,
                                        " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; ",
                                        "DROP DATABASE ", Render.quote dbName, ";"))
                                |> fun t -> t.GetAwaiter().GetResult()
                            with _ -> ()
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

    /// E4 (`DECISIONS 2026-06-04`) — the canonical schema-then-data deploy
    /// form: `SsdtDdlEmitter.statements` (DDL) followed by
    /// `StaticPopulationEmitter.statements` (the typed `InsertRow` realization
    /// of `Modality.Static` populations). This is the production wiring of
    /// `StaticPopulationEmitter` per its module header — the wide canary's
    /// target is a fresh-empty database, so the InsertRow realization (no
    /// idempotency / no CDC) is the right data lane. Identity to the prior
    /// schema-only canary when the source carries no static populations (the
    /// `InsertRow` stream is then empty). Closes the registered-but-unexecuted
    /// mismatch the E1–E4 isomorphism surfaced.
    let schemaWithStaticPopulation (catalog: Catalog) : seq<Statement> =
        Seq.append
            (SsdtDdlEmitter.statements catalog)
            (Projection.Targets.Data.StaticPopulationEmitter.statements catalog)

    /// **X8 — the canary's CDC-silence leg.** The wide canary proves the
    /// PhysicalSchema round-trip (source ≈ target); X8's criterion adds the
    /// SECOND assertion the protein P-9 canary demands — that an *idempotent
    /// redeploy* of the deployed target fires ZERO CDC captures (the
    /// `V2_DRIVER.md` highest-leverage property). After the target is deployed
    /// and read back, this enables CDC on it (`enableCdc`), brackets a
    /// caller-supplied idempotent `redeploy` with the change-measure ‖·‖
    /// (`cdcCaptureTotal`, the PRODUCTION reader), and returns the report
    /// alongside the measured capture delta. `redeploy` is parameterized (as
    /// `loadSource` is) so the migrate machinery — which compiles *after*
    /// Deploy — is threaded in by the caller: an idempotent
    /// `MigrationRun.execute tgt tgt` measures 0; a data-churning redeploy
    /// measures > 0 (the discriminator that proves the meter is live).
    let runWideCanaryWithCdcSilence
        (loadSource: SqlConnection -> Task<unit>)
        (emit: Catalog -> seq<Statement>)
        (enableCdc: SqlConnection -> Catalog -> Task<unit>)
        (redeploy: SqlConnection -> Catalog -> Task<unit>)
        : Task<Result<WideCanaryReport * int>> =
        task {
            use _ = Bench.scope "deploy.runWideCanaryCdcSilence"
            return!
                useContainer (fun masterConn ->
                    task {
                        let sourceDbName = uniqueDatabaseName DatabaseNameGenerator.guidBased "Source"
                        let targetDbName = uniqueDatabaseName DatabaseNameGenerator.guidBased "Target"
                        do! createDatabase masterConn sourceDbName
                        let sourceConn = buildPerDbConnectionString masterConn sourceDbName
                        let! sourceOutcome = runSourcePhase sourceConn loadSource
                        match sourceOutcome.Catalog with
                        | None ->
                            return aggregateFailure "wideCanary.source.failed" sourceOutcome.Errors
                        | Some src ->
                            let sourceReport =
                                { Ok = true; Database = sourceDbName; TablesCreated = sourceOutcome.Tables; Errors = sourceOutcome.Errors }
                            let stmts = emit src
                            do! createDatabase masterConn targetDbName
                            let targetConn = buildPerDbConnectionString masterConn targetDbName
                            let! targetOutcome = runTargetPhase targetConn stmts
                            match targetOutcome.Catalog with
                            | None ->
                                return aggregateFailure "wideCanary.target.failed" targetOutcome.Errors
                            | Some tgt ->
                                let targetReport =
                                    { Ok = true; Database = targetDbName; TablesCreated = targetOutcome.Tables; Errors = targetOutcome.Errors }
                                let diff =
                                    PhysicalSchema.diff (PhysicalSchema.ofCatalog src) (PhysicalSchema.ofCatalog tgt)
                                let report =
                                    { Source = src; Target = tgt; Diff = diff; SourceReport = sourceReport; TargetReport = targetReport }
                                // CDC-silence phase on the deployed target: enable
                                // CDC, then bracket the idempotent redeploy with the
                                // change-measure ‖·‖. delta == 0 ⇒ CDC-silent.
                                use cdcConn = new SqlConnection(targetConn)
                                do! cdcConn.OpenAsync()
                                do! enableCdc cdcConn tgt
                                // NM-54 — the measure surfaces its probe Error
                                // rather than fabricating a count.
                                match! cdcCaptureTotal cdcConn with
                                | Error es -> return Result.failure es
                                | Ok baseline ->
                                    do! redeploy cdcConn tgt
                                    match! cdcCaptureTotal cdcConn with
                                    | Error es -> return Result.failure es
                                    | Ok post ->
                                        return Result.success (report, post - baseline)
                    })
        }

    /// End-to-end: parse a V1 `osm_model.json` from disk, project
    /// through the three sibling Π's, and deploy the SSDT. Returns
    /// the `Report` from `runEphemeral` along with the artifact
    /// strings that fed the deploy.
    /// One-touch ephemeral deploy from an already-resolved `Catalog` — the
    /// model-source-agnostic core (model read live from OSSYS or from file).
    let runFromCatalog (catalog: Catalog) : Task<Result<Compose.Outputs * Report>> =
        task {
            let outputs = Compose.project EmissionPolicy.empty catalog
            let! report = runEphemeral (Compose.aggregateSsdt outputs.SsdtBundle)
            return Result.success (outputs, report)
        }

    /// THE_CONFIG_CONTROL_PLANE §6 (S3) — the overlay-aware sibling of
    /// `runFromCatalog`: project the caller-supplied catalog through the
    /// unified config's shaping (`Compose.projectWithConfig` — renames /
    /// policy / emission toggles / folders / groups) before deploying the
    /// SSDT to the ephemeral container. The flow `DeployDocker` arm routes
    /// here so `projection <flow→docker>` honors the shaping. The module
    /// filter is applied upstream at the shared `Program.needCatalog` seam.
    /// `Config.defaultConfig` shaping is byte-identical to `runFromCatalog`.
    let runFromCatalogWith (shaping: Config.Config) (catalog: Catalog) : Task<Result<Compose.Outputs * Report>> =
        task {
            match Compose.projectWithConfig shaping catalog with
            | Error errors -> return Result.failure errors
            | Ok outputs ->
                let! report = runEphemeral (Compose.aggregateSsdt outputs.SsdtBundle)
                return Result.success (outputs, report)
        }

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
