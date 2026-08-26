namespace Twin.Runtime

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Pipeline
open Twin.Core

/// THE TWIN — the trunk model, read back (Twin.Runtime).
///
/// The crossover merge and the drift report both bind against THE TRUNK —
/// the estate's repository definitions at the current head — never against
/// the running twin, whose materialization can lag the head. This module
/// owns that acquisition, following `Check.run`'s throwaway pattern:
/// resolve the estate files, build the dacpac, publish it to a throwaway
/// database on whatever SQL Server `Deploy.acquireContainer` yields (the
/// warm container, `PROJECTION_MSSQL_CONN_STR`, or an ephemeral one —
/// no Docker is required when the environment variable names a reachable
/// server), read the schema back as a catalog, and drop the database in
/// a `finally`, always.
[<RequireQualifiedAccess>]
module TrunkModel =

    let private openCnn (connStr: string) : Task<SqlConnection> =
        task {
            let cnn = new SqlConnection(connStr)
            do! cnn.OpenAsync()
            return cnn
        }

    let private dropThrowaway (masterConnStr: string) (dbName: string) : unit =
        try
            use master = new SqlConnection(masterConnStr)
            master.Open()
            use cmd = master.CreateCommand()
            cmd.CommandText <-
                System.String.Concat(
                    "IF DB_ID(N'", dbName, "') IS NOT NULL BEGIN ALTER DATABASE ",
                    Projection.Targets.SSDT.Render.quote dbName,
                    " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ",
                    Projection.Targets.SSDT.Render.quote dbName, "; END")  // LINT-ALLOW: terminal throwaway-cleanup SQL; the generated name passes through the SSDT renderer's quoting
            cmd.ExecuteNonQuery() |> ignore
        with _ -> ()

    /// Publish the estate's current definitions to a throwaway database
    /// and read the schema back as a catalog — the trunk's shape, exactly
    /// as the twin's own ReadSide would see it.
    let readback (root: string) (config: TwinConfig) : Task<Result<Catalog>> =
        task {
            match EstateFiles.resolve root config.Estate with
            | Error es -> return Result.failure es
            | Ok estate ->
                match EstateModel.buildDacpac estate with
                | Error es -> return Result.failure es
                | Ok dacpac ->
                    let! handle = Deploy.acquireContainer ()
                    try
                        let dbName = System.String.Concat("TwinTrunk_", System.Guid.NewGuid().ToString("N").Substring(0, 12))  // LINT-ALLOW: terminal throwaway database name
                        let builder = SqlConnectionStringBuilder handle.MasterConnectionString
                        let! published = EstateModel.publishTo builder.ConnectionString dbName dacpac
                        match published with
                        | Error es -> return Result.failure es
                        | Ok () ->
                            try
                                builder.InitialCatalog <- dbName
                                use! cnn = openCnn builder.ConnectionString
                                let! readBack = Readback.readSchema cnn
                                return readBack
                            finally
                                dropThrowaway handle.MasterConnectionString dbName
                    finally
                        (handle.DisposeAsync()).GetAwaiter().GetResult()
        }
