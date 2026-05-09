namespace Projection.Pipeline

open System
open System.IO
open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Testcontainers.MsSql
open Projection.Core

/// M2 (per the chapter-3.1 milestone sequence chosen at session 27):
/// deploy V2's emitted SSDT to an ephemeral SQL Server and report what
/// landed. Per `CHAPTER_3_PRESCOPE_READSIDE_ADAPTER.md` and the chapter
/// 3 sequencing decision (`DECISIONS 2026-05-22`), this is the gateway
/// to the canary loop — M3 adds the read-side adapter that
/// reconstructs a Catalog from the deployed schema, M4 adds the
/// Tolerance taxonomy + comparator, M5 wires the closing loop.
///
/// **Idempotency.** Each `runEphemeral` call is independent: a fresh
/// container, a fresh database, then full teardown. Re-running the
/// same input produces the same outcome with no state crossing
/// between runs. The operator can invoke `projection deploy` any
/// number of times with no concern about prior state. Idempotency
/// at the SSDT level (re-applying the same script to the same DB)
/// is a separate concern that gates on chapter 3.3 (DACPAC) and
/// the `idempotentRedeploy` canary predicate per `VISION.md` §"Verification posture";
/// raw .sql DROP+CREATE semantics are out of scope for M2.
[<RequireQualifiedAccess>]
module Deploy =

    /// Outcome of executing emitted SQL against an ephemeral database.
    /// `TablesCreated` counts user tables in `INFORMATION_SCHEMA.TABLES`
    /// after deploy — the smoke signal that the SSDT structurally
    /// landed. `Errors` carries SqlException-derived diagnostics on
    /// failure; otherwise empty.
    type Report =
        {
            Success : bool
            Database : string
            TablesCreated : int
            Errors : string list
        }

    /// Cheap Docker-availability probe for soft-skip in tests.
    /// Returns true iff `DOCKER_HOST` is set OR a known Docker socket
    /// path exists. Does NOT start any container or contact the
    /// daemon — pure file-system / env-var inspection.
    [<RequireQualifiedAccess>]
    module Docker =
        let private socketCandidates : string list =
            [
                "/var/run/docker.sock"
                sprintf
                    "%s/.docker/run/docker.sock"
                    (Environment.GetFolderPath Environment.SpecialFolder.UserProfile)
            ]

        let isAvailable () : bool =
            let hasEnvHost =
                let v = Environment.GetEnvironmentVariable "DOCKER_HOST"
                not (String.IsNullOrWhiteSpace v)
            let socketExists =
                socketCandidates |> List.exists File.Exists
            hasEnvHost || socketExists

    [<Literal>]
    let private DefaultImage : string =
        "mcr.microsoft.com/mssql/server:2022-latest"

    let private uniqueDatabaseName () : string =
        sprintf "Projection_%s" ((Guid.NewGuid().ToString "N").Substring(0, 12))

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

    let private buildPerRunConnectionString (master: string) (dbName: string) : string =
        let b = SqlConnectionStringBuilder(master)
        b.InitialCatalog <- dbName
        b.ConnectionString

    /// Spin up an ephemeral SQL Server container, create a fresh
    /// database, execute the emitted SSDT, count user tables, and
    /// dispose the container. Returns a `Report` with success,
    /// database name, table count, and (on failure) SqlException
    /// diagnostics.
    ///
    /// **Idempotent at the run level.** Each invocation is fully
    /// independent — fresh container, fresh database, full teardown.
    /// No state survives between calls.
    let runEphemeral (sql: string) : Task<Report> =
        task {
            let container =
                MsSqlBuilder()
                    .WithImage(DefaultImage)
                    .WithCleanUp(true)
                    .Build()
            try
                do! container.StartAsync()
                let masterConn = container.GetConnectionString()
                let dbName = uniqueDatabaseName ()

                // Phase 1: create the per-run database in master.
                do!
                    task {
                        use cnn = new SqlConnection(masterConn)
                        do! cnn.OpenAsync()
                        use cmd = cnn.CreateCommand()
                        cmd.CommandText <- sprintf "CREATE DATABASE [%s];" dbName
                        let! _ = cmd.ExecuteNonQueryAsync()
                        return ()
                    }

                // Phase 2: switch context to the per-run DB; execute
                // the SSDT; observe table count.
                let perRunConn = buildPerRunConnectionString masterConn dbName

                try
                    use cnn = new SqlConnection(perRunConn)
                    do! cnn.OpenAsync()

                    use deployCmd = cnn.CreateCommand()
                    deployCmd.CommandText <- sql
                    deployCmd.CommandTimeout <- 60
                    let! _ = deployCmd.ExecuteNonQueryAsync()

                    use countCmd = cnn.CreateCommand()
                    countCmd.CommandText <-
                        "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES "
                        + "WHERE TABLE_TYPE = 'BASE TABLE' "
                        + "AND TABLE_SCHEMA NOT IN ('sys','INFORMATION_SCHEMA')"
                    let! tablesObj = countCmd.ExecuteScalarAsync()

                    return
                        {
                            Success = true
                            Database = dbName
                            TablesCreated = Convert.ToInt32 tablesObj
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
            finally
                // Synchronous wait on async dispose: F# task CE's
                // finally cannot host `do!`. The cost is a brief
                // thread block at process exit; acceptable for the
                // operator-side iteration loop.
                container.DisposeAsync().AsTask().GetAwaiter().GetResult()
        }

    /// End-to-end: parse a V1 `osm_model.json` from disk, project
    /// through the three sibling Π's, and deploy the SSDT. Returns
    /// the `Report` from `runEphemeral` along with the artifact
    /// strings that fed the deploy. Surfaces parse failures as the
    /// `Failure` case of the outer Result.
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
