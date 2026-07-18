namespace Twin.Runtime

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Pipeline
open Twin.Core

/// THE TWIN — the twin database's own furniture (Twin.Runtime).
///
/// The `[twin]` schema is the tool's only write outside the estate's own
/// objects (law 5): one single-row state table holding the fingerprints,
/// so the twin describes what it holds and `status`/`up` never consult
/// hidden local state. A schema publish with drop-not-in-source removes
/// the furniture (the estate does not define it); `ensureState` re-lays
/// it afterward — cheap, idempotent, and honest about ownership.
[<RequireQualifiedAccess>]
module TwinDatabase =

    [<Literal>]
    let private StateSchema = "twin"

    let private sqlFailure (action: string) (detail: string) : ValidationError =
        ValidationError.createWithMetadata
            "twin.database.sqlFailed"
            "A statement against the twin database did not succeed."
            (Map.ofList [ "action", Some action; "detail", Some detail ])

    /// The stored materialization record.
    type StoredState = {
        SchemaFingerprint : string option
        DataFingerprint   : string option
        Scenario          : string option
        Seed              : uint64 option
        MintedRows        : int64 option
    }

    let emptyState : StoredState =
        { SchemaFingerprint = None; DataFingerprint = None; Scenario = None; Seed = None; MintedRows = None }

    /// Does the twin database exist on the container?
    let databaseExists (masterCnn: SqlConnection) : Task<bool> =
        task {
            use cmd = masterCnn.CreateCommand()
            cmd.CommandText <- "SELECT DB_ID(@name);"
            cmd.Parameters.AddWithValue("@name", TwinContainer.TwinDatabaseName) |> ignore
            let! result = cmd.ExecuteScalarAsync()
            return not (isNull result) && result <> box System.DBNull.Value
        }

    /// Lay the `[twin]` furniture (schema + state table) if absent.
    let ensureState (twinCnn: SqlConnection) : Task<Result<unit>> =
        task {
            try
                do! Deploy.executeBatch twinCnn
                        """
IF SCHEMA_ID(N'twin') IS NULL EXEC (N'CREATE SCHEMA [twin] AUTHORIZATION [dbo];');
IF OBJECT_ID(N'[twin].[__state]') IS NULL
    CREATE TABLE [twin].[__state] (
        [Lock]              INT            NOT NULL CONSTRAINT [PK_twin_state] PRIMARY KEY
                                           CONSTRAINT [CK_twin_state_single] CHECK ([Lock] = 1),
        [SchemaFingerprint] NVARCHAR(128)  NULL,
        [DataFingerprint]   NVARCHAR(128)  NULL,
        [Scenario]          NVARCHAR(128)  NULL,
        [Seed]              BIGINT         NULL,
        [MintedRows]        BIGINT         NULL
    );
IF NOT EXISTS (SELECT 1 FROM [twin].[__state])
    INSERT INTO [twin].[__state] ([Lock]) VALUES (1);
"""
                return Result.success ()
            with ex ->
                return Result.failureOf (sqlFailure "ensureState" ex.Message)
        }

    /// Read the stored state; `emptyState` when the furniture is absent
    /// (a twin that has never materialized).
    let readState (twinCnn: SqlConnection) : Task<StoredState> =
        task {
            try
                use cmd = twinCnn.CreateCommand()
                cmd.CommandText <-
                    "SELECT [SchemaFingerprint], [DataFingerprint], [Scenario], [Seed], [MintedRows] FROM [twin].[__state];"
                use! reader = cmd.ExecuteReaderAsync()
                let! has = reader.ReadAsync()
                if not has then return emptyState
                else
                    let strOf i = if reader.IsDBNull i then None else Some (reader.GetString i)
                    let intOf i = if reader.IsDBNull i then None else Some (reader.GetInt64 i)
                    return
                        { SchemaFingerprint = strOf 0
                          DataFingerprint   = strOf 1
                          Scenario          = strOf 2
                          Seed              = intOf 3 |> Option.map uint64
                          MintedRows        = intOf 4 }
            with _ ->
                return emptyState
        }

    /// Record the schema materialization (data fingerprint cleared — a
    /// new schema always re-mints before the twin is current again).
    let writeSchemaState (twinCnn: SqlConnection) (schemaFp: Fingerprint) : Task<Result<unit>> =
        task {
            try
                use cmd = twinCnn.CreateCommand()
                cmd.CommandText <-
                    "UPDATE [twin].[__state] SET [SchemaFingerprint] = @fp, [DataFingerprint] = NULL, [Scenario] = NULL, [Seed] = NULL, [MintedRows] = NULL;"
                cmd.Parameters.AddWithValue("@fp", Fingerprint.value schemaFp) |> ignore
                let! _ = cmd.ExecuteNonQueryAsync()
                return Result.success ()
            with ex ->
                return Result.failureOf (sqlFailure "writeSchemaState" ex.Message)
        }

    /// Record the mint.
    let writeDataState
        (twinCnn: SqlConnection)
        (dataFp: Fingerprint)
        (scenario: string)
        (seed: uint64)
        (mintedRows: int64)
        : Task<Result<unit>> =
        task {
            try
                use cmd = twinCnn.CreateCommand()
                cmd.CommandText <-
                    "UPDATE [twin].[__state] SET [DataFingerprint] = @fp, [Scenario] = @scenario, [Seed] = @seed, [MintedRows] = @rows;"
                cmd.Parameters.AddWithValue("@fp", Fingerprint.value dataFp) |> ignore
                cmd.Parameters.AddWithValue("@scenario", scenario) |> ignore
                cmd.Parameters.AddWithValue("@seed", int64 seed) |> ignore
                cmd.Parameters.AddWithValue("@rows", mintedRows) |> ignore
                let! _ = cmd.ExecuteNonQueryAsync()
                return Result.success ()
            with ex ->
                return Result.failureOf (sqlFailure "writeDataState" ex.Message)
        }

    /// Execute the estate's static-data lanes, in definition order.
    /// The lanes are the repo's own SQL (MERGE seeds, reference data) —
    /// executed verbatim through the kernel's batch splitter.
    let applyStaticLanes (twinCnn: SqlConnection) (estate: EstateDefinition) : Task<Result<int>> =
        task {
            let mutable failed : ValidationError option = None
            let mutable applied = 0
            for lane in EstateDefinition.staticData estate do
                if failed.IsNone then
                    try
                        do! Deploy.executeBatch twinCnn lane.Content
                        applied <- applied + 1
                    with ex ->
                        failed <-
                            Some (ValidationError.createWithMetadata
                                    "twin.staticData.failed"
                                    "A static-data lane did not apply."
                                    (Map.ofList [ "path", Some lane.RelativePath; "detail", Some ex.Message ]))
            match failed with
            | Some e -> return Result.failureOf e
            | None -> return Result.success applied
        }

    /// Total rows held by the estate's tables (the `[twin]` furniture
    /// and system objects excluded) — the status report's headline count.
    let totalRows (twinCnn: SqlConnection) : Task<int64> =
        task {
            try
                use cmd = twinCnn.CreateCommand()
                cmd.CommandText <-
                    """
SELECT COALESCE(SUM(p.[rows]), 0)
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id = t.schema_id
JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
WHERE s.name <> N'twin' AND t.is_ms_shipped = 0;
"""
                let! result = cmd.ExecuteScalarAsync()
                return System.Convert.ToInt64 result
            with _ ->
                return 0L
        }
