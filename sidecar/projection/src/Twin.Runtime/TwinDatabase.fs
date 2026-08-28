namespace Twin.Runtime
// LINT-ALLOW-FILE: the Twin's ADO.NET database driver — connection open / exec /
//   reader loops against SQL Server. The mutable locals carry the imperative
//   read/exec state; box/unbox crosses the `SqlParameter.Value` / reader-ordinal
//   boundary where the ADO.NET API is `obj`-typed by contract (no typed
//   alternative exists at the driver seam — considered a typed wrapper, rejected
//   as re-boxing at the same boundary); terminal SQL text is composed at the
//   command boundary. All boundary-confined; nothing escapes the module.

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Pipeline
open Twin.Core

/// THE TWIN — the twin database's own state schema (Twin.Runtime).
///
/// The `[twin]` schema is the tool's only write outside the estate's own
/// objects (law 5): one single-row state table holding the fingerprints,
/// so the twin describes what it holds and `status`/`up` never consult
/// hidden local state. A schema publish with drop-not-in-source removes
/// the state schema (the estate does not define it); `ensureState` re-lays
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

    /// Lay the `[twin]` state schema (its single-row `__state` table) if absent.
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

    /// Read the stored state; `emptyState` when the state schema is absent
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

    /// Total rows held by the estate's tables (the `[twin]` state schema
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

    /// Post-mint constraint re-validation — the trust gate. The bulk load
    /// path does not enforce CHECK constraints, so a mint whose generated
    /// data violates one would otherwise land green with the constraint
    /// silently untrusted (`is_not_trusted = 1`) — a local copy that stops
    /// matching what an upper environment enforces. This pass re-validates
    /// every user check and foreign key (`WITH CHECK CHECK CONSTRAINT`),
    /// so a mint either ends with every constraint TRUSTED or refuses by
    /// name. The schema-derived floor cannot read a predicate; a scenario,
    /// correction, or evidence entry owns the data-side remedy.
    let revalidateConstraints (twinCnn: SqlConnection) : Task<Result<int>> =
        task {
            try
                let candidates = System.Collections.Generic.List<string * string * string>()
                use listCmd = twinCnn.CreateCommand()
                listCmd.CommandText <-
                    """
SELECT s.name, t.name, c.name
FROM sys.check_constraints c
JOIN sys.tables t ON t.object_id = c.parent_object_id
JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE s.name <> N'twin' AND t.is_ms_shipped = 0
UNION ALL
SELECT s.name, t.name, fk.name
FROM sys.foreign_keys fk
JOIN sys.tables t ON t.object_id = fk.parent_object_id
JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE s.name <> N'twin' AND t.is_ms_shipped = 0;
"""
                let! reader = listCmd.ExecuteReaderAsync()
                use r = reader
                let mutable go = true
                while go do
                    let! hasRow = r.ReadAsync()
                    if hasRow then candidates.Add(r.GetString 0, r.GetString 1, r.GetString 2)
                    else go <- false
                r.Close()

                let bracket (n: string) = System.String.Concat("[", n.Replace("]", "]]"), "]")  // LINT-ALLOW: identifier quoting at the command boundary
                let failures = System.Collections.Generic.List<string * string>()
                for (schemaName, tableName, constraintName) in candidates do
                    try
                        use cmd = twinCnn.CreateCommand()
                        cmd.CommandText <-
                            System.String.Concat(  // LINT-ALLOW: terminal DDL at the command boundary; identifiers bracket-escaped above
                                "ALTER TABLE ", bracket schemaName, ".", bracket tableName,
                                " WITH CHECK CHECK CONSTRAINT ", bracket constraintName, ";")
                        let! _ = cmd.ExecuteNonQueryAsync()
                        ()
                    with ex ->
                        failures.Add(
                            System.String.Concat(bracket schemaName, ".", bracket tableName, ".", bracket constraintName),  // LINT-ALLOW: refusal display path
                            ex.Message)

                if failures.Count = 0 then return Result.success candidates.Count
                else
                    let firstName, firstDetail = failures.[0]
                    return
                        Result.failureOf
                            (ValidationError.createWithMetadata
                                "twin.mint.constraintViolation"
                                "The minted data violates a declared constraint; the twin refuses rather than hold it untrusted."
                                (Map.ofList
                                    [ "constraint", Some firstName
                                      "failing", Some (string failures.Count)
                                      "detail", Some firstDetail ]))
            with ex ->
                return Result.failureOf (sqlFailure "revalidate constraints" ex.Message)
        }
