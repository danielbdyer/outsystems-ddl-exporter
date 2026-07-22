namespace Twin.Tests.Integration

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Twin.Core
open Twin.Runtime

/// THE SAMPLE-PR PROOF SUPPORT — shared by every Twin-proven sample-PR test.
///
/// `SamplePrPublish.strict` republishes the estate CURRENTLY ON DISK to the
/// twin database with the PRODUCTION-FAITHFUL DacFx posture
/// (`EstateModel.publishStrict`: BlockOnPossibleDataLoss = true, no smart-
/// defaults, the twin's bookkeeping left alone). This is the deployment a real
/// environment runs — the one the Twin's own `Runs.up` deliberately relaxes.
///
/// The "tightening blocked by data" archetype (make-mandatory, narrow-over-
/// length, add-unique-dupes) proves the PRODUCTION block with it:
///   1. `Runs.up` materializes the BEFORE estate + real-shaped data (relaxed
///      publish is fine for setup — it just lands the schema and mints rows).
///   2. `fixture.Rewrite` applies the tightening to the on-disk estate.
///   3. `SamplePrPublish.strict fixture.Root fixture.Config` attempts the
///      production-faithful publish and returns the outcome:
///        - `Error [ ValidationError ]` when REFUSED — Code
///          "twin.publish.failed", the DacFx guard/refusal text in
///          Metadata["detail"]. The block is the proof.
///        - `Ok ()` when it APPLIES (e.g. the empty-table contrast).
/// The strict publish does NOT touch the twin's `__state`, so a subsequent
/// `Runs.up`/`Runs.seed` still reads coherent fingerprints — but note it
/// changes the live schema without updating `__state`, so run the relaxed
/// `Runs.up` facts BEFORE the strict facts in a shared session.
[<RequireQualifiedAccess>]
module SamplePrPublish =

    /// Build the on-disk estate's dacpac and strict-publish it to the twin.
    let strict (root: string) (config: TwinConfig) : Task<Result<unit>> =
        task {
            match EstateFiles.resolve root config.Estate with
            | Error es -> return Result.failure es
            | Ok estate ->
                match TwinContainer.resolvePassword config.Container.PasswordRef with
                | Error es -> return Result.failure es
                | Ok password ->
                    match EstateModel.buildDacpac estate with
                    | Error es -> return Result.failure es
                    | Ok dacpac ->
                        let masterConn = TwinContainer.masterConnectionString config.Container password
                        return! EstateModel.publishStrict masterConn dacpac
        }

/// Thin live-SQL probes shared by every sample-PR test — a scalar reader
/// (`int64`), a scalar string reader, a non-query executor, and the
/// DacFx-refusal detail extractor. Factored out of the make-mandatory
/// exemplar so every archetype consumes data the same way.
[<RequireQualifiedAccess>]
module SamplePrSql =

    let scalar (connStr: string) (sql: string) : Task<int64> =
        task {
            use cnn = new SqlConnection(connStr)
            do! cnn.OpenAsync()
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sql
            let! v = cmd.ExecuteScalarAsync()
            return System.Convert.ToInt64 v
        }

    let scalarString (connStr: string) (sql: string) : Task<string> =
        task {
            use cnn = new SqlConnection(connStr)
            do! cnn.OpenAsync()
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sql
            let! v = cmd.ExecuteScalarAsync()
            match v with
            | null -> return ""
            | :? System.DBNull -> return ""
            | _ -> return string v
        }

    let exec (connStr: string) (sql: string) : Task<int> =
        task {
            use cnn = new SqlConnection(connStr)
            do! cnn.OpenAsync()
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sql
            let! n = cmd.ExecuteNonQueryAsync()
            return n
        }

    /// The DacFx deploy report for a refused publish — parked in the
    /// ValidationError's "detail" metadata by both publish paths.
    let detail (es: ValidationError list) : string =
        es
        |> List.map (fun e -> e.Metadata |> Map.tryFind "detail" |> Option.flatten |> Option.defaultValue "")
        |> String.concat "\n"

/// The canonical baseline `CREATE`s (copied verbatim from `TwinEstateFixture`)
/// plus the per-fact reset primitive. Each sample-PR fact rewrites the tables
/// it touches back to these before applying its own edit, then drops the twin
/// database so the next converge republishes a pristine schema — the strict
/// publish alters the LIVE schema without updating `__state`, so a plain
/// re-mint cannot revert a prior fact's applied edit; a database drop can.
[<RequireQualifiedAccess>]
module SamplePrBaseline =

    let status =
        """CREATE TABLE [dbo].[Status] (
    [Id]   INT           NOT NULL,
    [Name] NVARCHAR(50)  NOT NULL,
    CONSTRAINT [PK_Status] PRIMARY KEY ([Id])
);
"""

    let customer =
        """CREATE TABLE [dbo].[Customer] (
    [Id]        INT            IDENTITY(1,1) NOT NULL,
    [Name]      NVARCHAR(100)  NOT NULL,
    [Email]     NVARCHAR(250)  NOT NULL,
    [StatusId]  INT            NOT NULL,
    [CreatedOn] DATETIME2      NOT NULL,
    CONSTRAINT [PK_Customer] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Customer_Status] FOREIGN KEY ([StatusId]) REFERENCES [dbo].[Status] ([Id])
);
"""

    let order =
        """CREATE TABLE [dbo].[Order] (
    [Id]         INT           IDENTITY(1,1) NOT NULL,
    [CustomerId] INT           NOT NULL,
    [StatusId]   INT           NOT NULL,
    [Channel]    NVARCHAR(20)  NOT NULL,
    [Total]      DECIMAL(18,2) NOT NULL,
    [PlacedOn]   DATETIME2     NOT NULL,
    CONSTRAINT [PK_Order] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id]),
    CONSTRAINT [FK_Order_Status] FOREIGN KEY ([StatusId]) REFERENCES [dbo].[Status] ([Id])
);
"""

    let orderLine =
        """CREATE TABLE [dbo].[OrderLine] (
    [Id]       INT           IDENTITY(1,1) NOT NULL,
    [OrderId]  INT           NOT NULL,
    [Sku]      NVARCHAR(64)  NOT NULL,
    [Quantity] INT           NOT NULL,
    [Note]     NVARCHAR(200) NULL,
    CONSTRAINT [PK_OrderLine] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_OrderLine_Order] FOREIGN KEY ([OrderId]) REFERENCES [dbo].[Order] ([Id])
);
"""

    /// Every table file at its canonical baseline — rewrite all of them so a
    /// fact never inherits another fact's edit on disk.
    let files : (string * string) list =
        [ "Tables/dbo.Status.sql", status
          "Tables/dbo.Customer.sql", customer
          "Tables/dbo.Order.sql", order
          "Tables/dbo.OrderLine.sql", orderLine ]

    /// Drop the twin database (clearing pooled connections first) so the next
    /// `Runs.up` rebuilds a pristine schema. Ensures the container is running
    /// so the very first fact — which starts before any converge — succeeds.
    let dropTwinDatabase (config: TwinConfig) : Task<unit> =
        task {
            match TwinContainer.resolvePassword config.Container.PasswordRef with
            | Error _ -> return ()
            | Ok password ->
                match! TwinContainer.ensureRunning config.Container password with
                | Error _ -> return ()
                | Ok () ->
                    SqlConnection.ClearAllPools()
                    use cnn = new SqlConnection(TwinContainer.masterConnectionString config.Container password)
                    do! cnn.OpenAsync()
                    use cmd = cnn.CreateCommand()
                    cmd.CommandText <-
                        "IF DB_ID('twin') IS NOT NULL BEGIN ALTER DATABASE [twin] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [twin]; END;"
                    let! _ = cmd.ExecuteNonQueryAsync()
                    return ()
        }

