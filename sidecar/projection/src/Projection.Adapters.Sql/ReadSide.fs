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

    /// Per-column metadata row read from INFORMATION_SCHEMA.COLUMNS.
    /// Carries every axis V2's IR cares about (post-session-32):
    /// type name, nullability, length, precision, scale.
    type private ColumnRow =
        {
            Schema : string
            Table : string
            Column : string
            DataType : string
            Nullable : bool
            /// CHARACTER_MAXIMUM_LENGTH; -1 maps to None (MAX).
            Length : int option
            /// NUMERIC_PRECISION; only meaningful for decimal types.
            Precision : int option
            /// NUMERIC_SCALE; only meaningful for decimal types.
            Scale : int option
        }

    /// Read the basic per-column metadata for every user table in
    /// the database. Excludes `sys` and `INFORMATION_SCHEMA` rows.
    /// Single round-trip; group client-side by `(schema, table)`.
    ///
    /// **Bench note (session-30 Phase 3 lesson).** A direct sys.*
    /// catalog-table join was tried and reverted — empirically
    /// ~2x slower than INFORMATION_SCHEMA.COLUMNS at canary scale.
    /// The bench surface (commits fb12761 + 64eec02) was the
    /// signal: per-query timings made the wrong-direction
    /// optimization visible immediately. Documented here so future
    /// agents don't re-attempt the same swap without measuring.
    ///
    /// Per session-32 — extended to read CHARACTER_MAXIMUM_LENGTH,
    /// NUMERIC_PRECISION, NUMERIC_SCALE so the V2 IR carries
    /// byte-faithful type declarations through the round-trip.
    let private readColumnRows (cnn: SqlConnection)
        : Task<list<ColumnRow>> =
        task {
            use _ = Bench.scope "readside.readColumnRows"
            use cmd = cnn.CreateCommand()
            cmd.CommandText <-
                "SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, DATA_TYPE, IS_NULLABLE, \
                        CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE \
                 FROM INFORMATION_SCHEMA.COLUMNS \
                 WHERE TABLE_SCHEMA NOT IN ('sys','INFORMATION_SCHEMA') \
                 ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION"
            use! reader = cmd.ExecuteReaderAsync()
            let rows = ResizeArray<ColumnRow>()
            let optInt (idx: int) : int option =
                if reader.IsDBNull idx then
                    None
                else
                    let raw = reader.GetValue idx
                    match raw with
                    | :? int32 as i32 -> Some (int i32)
                    | :? int16 as i16 -> Some (int i16)
                    | :? int64 as i64 -> Some (int i64)
                    | :? byte as b -> Some (int b)
                    | _ -> None
            let mutable hasMore = true
            while hasMore do
                let! more = reader.ReadAsync()
                if more then
                    let lengthRaw = optInt 5
                    let length =
                        match lengthRaw with
                        | Some -1 -> None  // SQL Server's MAX marker
                        | other -> other
                    rows.Add(
                        {
                            Schema = reader.GetString 0
                            Table = reader.GetString 1
                            Column = reader.GetString 2
                            DataType = reader.GetString 3
                            Nullable = (reader.GetString 4) = "YES"
                            Length = length
                            Precision = optInt 6
                            Scale = optInt 7
                        })
                else
                    hasMore <- false
            return List.ofSeq rows
        }

    /// Read the set of `(schema, table, column)` identifying IDENTITY
    /// columns. Single round-trip; small result set (typically one
    /// row per table). Per session-32.
    let private readIdentityColumns (cnn: SqlConnection)
        : Task<Set<string * string * string>> =
        task {
            use _ = Bench.scope "readside.readIdentityColumns"
            use cmd = cnn.CreateCommand()
            cmd.CommandText <-
                "SELECT SCHEMA_NAME(t.schema_id), t.name, c.name \
                 FROM sys.columns c \
                 JOIN sys.tables t ON t.object_id = c.object_id \
                 WHERE c.is_identity = 1 \
                   AND t.is_ms_shipped = 0"
            use! reader = cmd.ExecuteReaderAsync()
            let result = System.Collections.Generic.HashSet<string * string * string>()
            let mutable hasMore = true
            while hasMore do
                let! more = reader.ReadAsync()
                if more then
                    result.Add(
                        reader.GetString 0,
                        reader.GetString 1,
                        reader.GetString 2) |> ignore
                else
                    hasMore <- false
            return Set.ofSeq result
        }

    /// Read the set of `(schema, table, column)` tuples that are
    /// part of a PRIMARY KEY constraint. Used to set
    /// `Attribute.IsPrimaryKey`.
    ///
    /// **Bench note (session-30 Phase 3 lesson).** A sys.indexes +
    /// sys.index_columns join was tried — slower than the
    /// INFORMATION_SCHEMA path at canary scale (~25%). The
    /// optimizer handles the INFORMATION_SCHEMA view's
    /// CONSTRAINT_TYPE filter more efficiently than the
    /// `is_primary_key = 1` predicate against all indexes. Reverted
    /// per bench data. See companion docstring on `readColumnRows`.
    let private readPrimaryKeys (cnn: SqlConnection)
        : Task<Set<string * string * string>> =
        task {
            use _ = Bench.scope "readside.readPrimaryKeys"
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

    /// Read the set of FK relationships across every user table.
    /// Returns rows of `(srcSchema, srcTable, srcCol, tgtSchema,
    /// tgtTable, tgtCol)`. Composite FKs surface as multiple rows
    /// with the same constraint object_id but different column
    /// pairs — `read` aggregates them per-table for `Kind.References`.
    ///
    /// Per session-31 Session B — adds FK round-trip to the
    /// canary's structural fidelity surface. Without this query,
    /// FK CONSTRAINTs (the actual referential constraints) would
    /// silently be dropped through the round-trip while the
    /// underlying columns survive.
    ///
    /// Uses sys.* directly for FK metadata since INFORMATION_SCHEMA's
    /// REFERENTIAL_CONSTRAINTS / KEY_COLUMN_USAGE shape is awkward
    /// for the source/target column-pair join we need.
    let private readForeignKeys (cnn: SqlConnection)
        : Task<list<string * string * string * string * string * string>> =
        task {
            use _ = Bench.scope "readside.readForeignKeys"
            use cmd = cnn.CreateCommand()
            cmd.CommandText <-
                "SELECT \
                    SCHEMA_NAME(t.schema_id), t.name, c.name, \
                    SCHEMA_NAME(rt.schema_id), rt.name, rc.name \
                 FROM sys.foreign_keys fk \
                 JOIN sys.foreign_key_columns fkc \
                   ON fkc.constraint_object_id = fk.object_id \
                 JOIN sys.tables t ON t.object_id = fk.parent_object_id \
                 JOIN sys.columns c \
                   ON c.object_id = t.object_id AND c.column_id = fkc.parent_column_id \
                 JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id \
                 JOIN sys.columns rc \
                   ON rc.object_id = rt.object_id AND rc.column_id = fkc.referenced_column_id \
                 WHERE t.is_ms_shipped = 0 \
                 ORDER BY SCHEMA_NAME(t.schema_id), t.name, c.column_id"
            use! reader = cmd.ExecuteReaderAsync()
            let rows = ResizeArray<string * string * string * string * string * string>()
            let mutable hasMore = true
            while hasMore do
                let! more = reader.ReadAsync()
                if more then
                    rows.Add(
                        reader.GetString 0,
                        reader.GetString 1,
                        reader.GetString 2,
                        reader.GetString 3,
                        reader.GetString 4,
                        reader.GetString 5)
                else
                    hasMore <- false
            return List.ofSeq rows
        }

    let private buildAttribute
        (row: ColumnRow)
        (primaryKeySet: Set<string * string * string>)
        (identitySet: Set<string * string * string>)
        : Result<Attribute> =
        let result = mapSqlType row.DataType
        match result with
        | Failure errors -> Result.failure errors
        | Success ptype ->
            match attributeSsKey row.Schema row.Table row.Column with
            | Failure errors -> Result.failure errors
            | Success attrKey ->
                match Name.create row.Column with
                | Failure errors -> Result.failure errors
                | Success attrName ->
                    let coord = (row.Schema, row.Table, row.Column)
                    Result.success
                        {
                            SsKey = attrKey
                            Name = attrName
                            Type = ptype
                            Column =
                                {
                                    ColumnName = row.Column
                                    IsNullable = row.Nullable
                                }
                            IsPrimaryKey = primaryKeySet.Contains coord
                            IsMandatory = not row.Nullable
                            // Length applies only to text / binary
                            // types; for non-applicable types
                            // INFORMATION_SCHEMA returns NULL, which
                            // we already mapped to None.
                            Length = row.Length
                            // Precision / Scale apply only to
                            // decimal types; same treatment.
                            Precision =
                                match ptype with
                                | Decimal -> row.Precision
                                | _ -> None
                            Scale =
                                match ptype with
                                | Decimal -> row.Scale
                                | _ -> None
                            IsIdentity = identitySet.Contains coord
                        }

    let private buildKind
        (schema: string)
        (table: string)
        (columnRows: list<ColumnRow>)
        (primaryKeySet: Set<string * string * string>)
        (identitySet: Set<string * string * string>)
        : Result<Kind> =
        use _ = Bench.scope "readside.buildKind"
        // collect all attributes for this (schema, table)
        let attrResults =
            columnRows
            |> Bench.iterMap "readside.buildAttribute" (fun row ->
                buildAttribute row primaryKeySet identitySet)
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
    /// Build a Reference value from one FK row tuple. The
    /// `SourceAttribute` and `TargetKind` SsKeys mirror the
    /// `kindSsKey` / `attributeSsKey` synthesis convention so the
    /// reconstructed Catalog's References resolve internally.
    let private buildReference
        (srcSchema: string, srcTable: string, srcColumn: string,
         tgtSchema: string, tgtTable: string, _tgtColumn: string)
        : Result<Reference> =
        match attributeSsKey srcSchema srcTable srcColumn with
        | Failure errors -> Result.failure errors
        | Success srcAttrKey ->
            match kindSsKey tgtSchema tgtTable with
            | Failure errors -> Result.failure errors
            | Success tgtKindKey ->
                match
                    SsKey.synthesized
                        "READSIDE_REF"
                        (sprintf "%s.%s.%s" srcSchema srcTable srcColumn)
                with
                | Failure errors -> Result.failure errors
                | Success refKey ->
                    match Name.create (sprintf "FK_%s_%s" srcTable srcColumn) with
                    | Failure errors -> Result.failure errors
                    | Success refName ->
                        Result.success
                            {
                                SsKey = refKey
                                Name = refName
                                SourceAttribute = srcAttrKey
                                TargetKind = tgtKindKey
                                // Delete-rule recovery requires
                                // joining sys.foreign_keys.delete_referential_action_desc;
                                // defer to a follow-up slice.
                                // NoAction is the SQL Server default
                                // and matches the OutSystems-shape
                                // fixtures we currently target.
                                OnDelete = NoAction
                            }

    /// Attach references to a Kind based on the FKs grouped by
    /// (schema, table) coordinates. Per session-31 Session B.
    let private attachReferences
        (fkGroups: Map<string * string, list<string * string * string * string * string * string>>)
        (k: Kind)
        : Result<Kind> =
        match fkGroups.TryFind(k.Physical.Schema, k.Physical.Table) with
        | None -> Result.success k
        | Some fks ->
            let refResults = fks |> List.map buildReference
            let aggregated =
                refResults
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
            | Success refs -> Result.success { k with References = refs }

    let read (cnn: SqlConnection) : Task<Result<Catalog>> =
        task {
            use _ = Bench.scope "readside.read"
            try
                let! columnRows = readColumnRows cnn
                let! primaryKeySet = readPrimaryKeys cnn
                let! identitySet = readIdentityColumns cnn
                let! fkRows = readForeignKeys cnn
                let kindResults =
                    columnRows
                    |> List.groupBy (fun row -> row.Schema, row.Table)
                    |> Bench.iterMap "readside.kindGroup" (fun ((schema, table), rows) ->
                        buildKind schema table rows primaryKeySet identitySet)
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
                    let fkGroups =
                        fkRows
                        |> List.groupBy (fun (s, t, _, _, _, _) -> s, t)
                        |> Map.ofList
                    let kindsWithRefsResults =
                        kinds
                        |> Bench.iterMap "readside.attachReferences" (attachReferences fkGroups)
                    let kindsWithRefsAggregated =
                        kindsWithRefsResults
                        |> List.fold
                            (fun acc r ->
                                match acc, r with
                                | Failure es, Failure es' -> Result.failure (es @ es')
                                | Failure _, _ -> acc
                                | _, Failure es -> Result.failure es
                                | Success xs, Success x -> Result.success (xs @ [ x ]))
                            (Result.success [])
                    match kindsWithRefsAggregated with
                    | Failure errors -> return Result.failure errors
                    | Success kindsWithRefs ->
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
                                                        Kinds = kindsWithRefs
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
