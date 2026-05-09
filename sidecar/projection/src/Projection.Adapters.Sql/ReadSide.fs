namespace Projection.Adapters.Sql

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core

/// M3 (per the chapter-3.1 milestone sequence chosen at session 27):
/// the read-side adapter. Reads a deployed SQL Server schema via
/// `INFORMATION_SCHEMA` queries and reconstructs a V2 `Catalog`.
///
/// Per the canary's wide-integration framing
/// (`DECISIONS 2026-05-23 — Source SQL Server with OutSystems
/// semantics is the canary's primary wide integration surface`),
/// this adapter is the single piece both halves of the canary's
/// round-trip rest on:
///
///   - **Source readback.** Deploy an OutSystems-shaped source
///     schema; read it back into a `Catalog`. The source DDL is
///     the operator's reality.
///   - **Target readback.** Deploy V2's emitted SSDT to a separate
///     ephemeral container; read it back into a `Catalog`. The
///     emitted artifacts are V2's projection of operator intent.
///
/// A round-trip property test then asserts source ≈ target modulo
/// named tolerances (M4 formalizes the Tolerance taxonomy; for M3
/// the comparison is structural over the `(schema, table, column,
/// type, nullable)` axis via `PhysicalSchema`).
///
/// **Reconstruction is best-effort, not lossless.** SQL Server's
/// INFORMATION_SCHEMA carries the structural skeleton (tables,
/// columns, types, nullability, PK identities); it does **not**
/// carry V2-IR-only metadata (Origin, Modality, Module names,
/// static populations, comment-style audit metadata). The
/// reconstructed Catalog assigns reasonable defaults
/// (`Origin.OsNative`, empty `Modality`, single `Reconstructed`
/// module) — comparison surfaces (`PhysicalSchema`) ignore the
/// V2-IR-only axes by construction.
[<RequireQualifiedAccess>]
module ReadSide =

    /// Reverse type mapping: SQL Server's INFORMATION_SCHEMA
    /// `DATA_TYPE` value → V2 `PrimitiveType`. Mirrors
    /// `Projection.Targets.SSDT.RawTextEmitter.defaultSqlType`'s
    /// forward mapping inverted across the supported PrimitiveType
    /// vocabulary.
    ///
    /// Returns `Failure` on unknown SQL types — surfaces an
    /// emitter-IR mismatch that the canary's blocking semantic
    /// catches. M4's Tolerance taxonomy can name accepted-but-
    /// unmapped types as a tolerance flag.
    let private mapSqlType (dataType: string) : Result<PrimitiveType> =
        match dataType.ToUpperInvariant() with
        | "INT" | "BIGINT" | "SMALLINT" | "TINYINT" ->
            Result.success Integer
        | "DECIMAL" | "NUMERIC" | "MONEY" | "SMALLMONEY" ->
            Result.success Decimal
        | "NVARCHAR" | "VARCHAR" | "CHAR" | "NCHAR" | "TEXT" | "NTEXT" ->
            Result.success Text
        | "BIT" ->
            Result.success Boolean
        | "DATETIME" | "DATETIME2" | "SMALLDATETIME" | "DATETIMEOFFSET" ->
            Result.success DateTime
        | "DATE" ->
            Result.success Date
        | "TIME" ->
            Result.success Time
        | "VARBINARY" | "BINARY" | "IMAGE" ->
            Result.success Binary
        | "UNIQUEIDENTIFIER" ->
            Result.success Guid
        | unknown ->
            Result.failureOf (
                ValidationError.create
                    "readside.column.unknownType"
                    (sprintf
                        "INFORMATION_SCHEMA.DATA_TYPE = '%s' has no V2 PrimitiveType mapping. \
                         Either extend ReadSide.mapSqlType or add a Tolerance flag (M4)."
                        unknown))

    /// Synthesis convention for reconstructed Module SsKeys. Each
    /// readback produces a single Module named "Reconstructed"
    /// because SQL Server's catalog has no concept of OutSystems
    /// modules — the module structure is V2-IR-only metadata.
    let private reconstructedModuleName : string = "Reconstructed"

    let private moduleSsKey () : Result<SsKey> =
        SsKey.synthesized "READSIDE_MOD" reconstructedModuleName

    let private kindSsKey (schema: string) (table: string) : Result<SsKey> =
        SsKey.synthesized "READSIDE_KIND" (sprintf "%s.%s" schema table)

    let private attributeSsKey
        (schema: string) (table: string) (column: string) : Result<SsKey> =
        SsKey.synthesized
            "READSIDE_ATTR"
            (sprintf "%s.%s.%s" schema table column)

    /// Read the basic per-column metadata for every user table in
    /// the database. Excludes `sys` and `INFORMATION_SCHEMA` rows.
    /// Single round-trip; group client-side by `(schema, table)`.
    let private readColumnRows (cnn: SqlConnection)
        : Task<list<string * string * string * string * bool>> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <-
                "SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, DATA_TYPE, IS_NULLABLE \
                 FROM INFORMATION_SCHEMA.COLUMNS \
                 WHERE TABLE_SCHEMA NOT IN ('sys','INFORMATION_SCHEMA') \
                 ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION"
            use! reader = cmd.ExecuteReaderAsync()
            let rows = ResizeArray<string * string * string * string * bool>()
            let mutable hasMore = true
            while hasMore do
                let! more = reader.ReadAsync()
                if more then
                    let schema = reader.GetString 0
                    let table = reader.GetString 1
                    let column = reader.GetString 2
                    let dataType = reader.GetString 3
                    let nullable = (reader.GetString 4) = "YES"
                    rows.Add(schema, table, column, dataType, nullable)
                else
                    hasMore <- false
            return List.ofSeq rows
        }

    /// Read the set of `(schema, table, column)` tuples that are
    /// part of a PRIMARY KEY constraint. Used to set
    /// `Attribute.IsPrimaryKey`.
    let private readPrimaryKeys (cnn: SqlConnection)
        : Task<Set<string * string * string>> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <-
                "SELECT kcu.TABLE_SCHEMA, kcu.TABLE_NAME, kcu.COLUMN_NAME \
                 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc \
                 JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu \
                   ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME \
                  AND tc.TABLE_SCHEMA = kcu.TABLE_SCHEMA \
                 WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY' \
                   AND tc.TABLE_SCHEMA NOT IN ('sys','INFORMATION_SCHEMA')"
            use! reader = cmd.ExecuteReaderAsync()
            let result = System.Collections.Generic.HashSet<string * string * string>()
            let mutable hasMore = true
            while hasMore do
                let! more = reader.ReadAsync()
                if more then
                    let schema = reader.GetString 0
                    let table = reader.GetString 1
                    let column = reader.GetString 2
                    result.Add(schema, table, column) |> ignore
                else
                    hasMore <- false
            return Set.ofSeq result
        }

    let private buildAttribute
        (schema: string)
        (table: string)
        (column: string)
        (dataType: string)
        (nullable: bool)
        (primaryKeySet: Set<string * string * string>)
        : Result<Attribute> =
        let result = mapSqlType dataType
        match result with
        | Failure errors -> Result.failure errors
        | Success ptype ->
            match attributeSsKey schema table column with
            | Failure errors -> Result.failure errors
            | Success attrKey ->
                match Name.create column with
                | Failure errors -> Result.failure errors
                | Success attrName ->
                    Result.success
                        {
                            SsKey = attrKey
                            Name = attrName
                            Type = ptype
                            Column = { ColumnName = column; IsNullable = nullable }
                            IsPrimaryKey = primaryKeySet.Contains(schema, table, column)
                            IsMandatory = not nullable
                        }

    let private buildKind
        (schema: string)
        (table: string)
        (columnRows: list<string * string * string * string * bool>)
        (primaryKeySet: Set<string * string * string>)
        : Result<Kind> =
        // collect all attributes for this (schema, table)
        let attrResults =
            columnRows
            |> List.map (fun (_, _, col, dt, nl) ->
                buildAttribute schema table col dt nl primaryKeySet)
        let aggregated =
            attrResults
            |> List.fold
                (fun acc r ->
                    match acc, r with
                    | Failure es, Failure es' -> Result.failure (es @ es')
                    | Failure _, _ -> acc
                    | _, Failure es -> Result.failure es
                    | Success xs, Success x -> Result.success (xs @ [ x ]))
                (Result.success [])
        match aggregated with
        | Failure errors -> Result.failure errors
        | Success attributes ->
            match kindSsKey schema table with
            | Failure errors -> Result.failure errors
            | Success kKey ->
                match Name.create table with
                | Failure errors -> Result.failure errors
                | Success kName ->
                    Result.success
                        {
                            SsKey = kKey
                            Name = kName
                            Origin = OsNative
                            Modality = []
                            Physical = { Schema = schema; Table = table }
                            Attributes = attributes
                            References = []
                            Indexes = []
                        }

    /// Read all user tables + columns from a deployed database and
    /// reconstruct a V2 `Catalog`. Returns the reconstructed Catalog
    /// or aggregated validation errors.
    ///
    /// **Best-effort fields.** `Origin = OsNative` and `Modality = []`
    /// for every reconstructed Kind (cannot be recovered from SQL).
    /// `References = []` and `Indexes = []` for M3 MVP — FK and index
    /// reconstruction defers to a follow-up slice.
    ///
    /// **Round-trip comparison.** The reconstructed Catalog uses the
    /// `READSIDE_*` synthesis source for SsKeys. Comparing to a
    /// Catalog produced by a different adapter (OSSYS / SnapshotJson)
    /// requires the `PhysicalSchema` projection (which compares by
    /// `(schema, table, column, type, nullable)` and is invariant
    /// under the SsKey-source difference).
    let read (cnn: SqlConnection) : Task<Result<Catalog>> =
        task {
            try
                let! columnRows = readColumnRows cnn
                let! primaryKeySet = readPrimaryKeys cnn
                let kindResults =
                    columnRows
                    |> List.groupBy (fun (s, t, _, _, _) -> s, t)
                    |> List.map (fun ((schema, table), rows) ->
                        buildKind schema table rows primaryKeySet)
                let kindsAggregated =
                    kindResults
                    |> List.fold
                        (fun acc r ->
                            match acc, r with
                            | Failure es, Failure es' -> Result.failure (es @ es')
                            | Failure _, _ -> acc
                            | _, Failure es -> Result.failure es
                            | Success xs, Success x -> Result.success (xs @ [ x ]))
                        (Result.success [])
                match kindsAggregated with
                | Failure errors -> return Result.failure errors
                | Success kinds ->
                    match moduleSsKey () with
                    | Failure errors -> return Result.failure errors
                    | Success mKey ->
                        match Name.create reconstructedModuleName with
                        | Failure errors -> return Result.failure errors
                        | Success mName ->
                            return
                                Result.success
                                    {
                                        Modules =
                                            [
                                                {
                                                    SsKey = mKey
                                                    Name = mName
                                                    Kinds = kinds
                                                }
                                            ]
                                    }
            with
            | ex ->
                return
                    Result.failureOf (
                        ValidationError.create
                            "readside.query.failed"
                            (sprintf "INFORMATION_SCHEMA query failed: %s" ex.Message))
        }
