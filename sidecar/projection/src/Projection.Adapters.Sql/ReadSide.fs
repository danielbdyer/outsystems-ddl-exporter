namespace Projection.Adapters.Sql

// LINT-ALLOW-FILE-MUTATION: SQL streaming reader lifetime —
//   cmdOpt / readerOpt / rowIdx / disposed and per-batch hasMore
//   mutables encapsulated behind the AsyncStream<StaticRow> pull
//   abstraction. BCL's SqlDataReader is itself a mutable cursor;
//   the lifetime state machine is reified per audit Lens-2 Tier-2.

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open FsToolkit.ErrorHandling

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
    /// Returns `Error` on unknown SQL types — surfaces an
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
    [<Literal>]
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
        | Error errors -> Result.failure errors
        | Ok ptype ->
            match attributeSsKey row.Schema row.Table row.Column with
            | Error errors -> Result.failure errors
            | Ok attrKey ->
                match Name.create row.Column with
                | Error errors -> Result.failure errors
                | Ok attrName ->
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

    /// Format a SQL Server scalar value as the canonical raw
    /// invariant-culture string that V2's IR stores in
    /// `StaticRow.Values`. Per session-33 — the canary's row-data
    /// round-trip relies on this formatter producing the same
    /// string for source and target reads, so the row hashes match.
    /// The IR contract is: raw values, no SQL quoting; the emitter
    /// (Projection.Targets.SSDT.RawTextEmitter) is responsible for
    /// SQL literal formatting at INSERT-emission time.
    ///
    /// This convention aligns with `Projection.Adapters.Sql.Static`
    /// (the V1 JSON adapter), which already produces invariant-culture
    /// strings via `JsonElement.GetRawText`. Both producers feed the
    /// same emitter formatter, keeping the IR canonical.
    ///
    /// `null` / `DBNull` is encoded as the empty string — the same
    /// sentinel `Static.fs` uses for `JsonValueKind.Null`. The
    /// emitter renders empty for nullable columns as `NULL`; columns
    /// not present in the row's Values map are also `NULL` by
    /// omission.
    let private formatRawValue (typ: PrimitiveType) (value: obj | null) : string =
        // Format rules flow through `RawValueCodec` so the V2 raw-
        // form contract is single-sourced across emit / parse /
        // readback.
        let isNullish =
            match value with
            | null -> true
            | v -> v :? System.DBNull
        if isNullish then ""
        else
            let v = nonNull value
            let inv = System.Globalization.CultureInfo.InvariantCulture
            match typ with
            | Integer ->
                System.Convert.ToInt64(v).ToString(inv)
            | Boolean ->
                RawValueCodec.formatBoolean (System.Convert.ToBoolean v)
            | DateTime ->
                RawValueCodec.formatDateTime (System.Convert.ToDateTime v)
            | Date ->
                RawValueCodec.formatDate (System.Convert.ToDateTime v)
            | Time ->
                RawValueCodec.formatTime (v :?> System.TimeSpan)
            | Guid ->
                RawValueCodec.formatGuid (v :?> System.Guid)
            | Decimal ->
                System.Convert.ToDecimal(v).ToString(inv)
            | Text ->
                match v.ToString() with
                | null -> ""
                | s -> s
            | Binary ->
                System.Convert.ToHexString (v :?> byte[])

    /// Stream a table's rows as an `AsyncStream<StaticRow>` — pull-
    /// based, bench-instrumented, no row materialization. Per
    /// session-34, the streaming readside is the canonical row
    /// source; `readRows` is a buffered wrapper retained for the
    /// existing per-row PhysicalSchema axis where small-table
    /// granularity is wanted.
    ///
    /// The reader's lifetime tracks the stream: the underlying
    /// `SqlCommand` and `SqlDataReader` open on first pull and
    /// dispose on EOF or exception. Callers must drain to `None`
    /// (or accept that abandoned streams clean up at GC).
    let readRowsStream (cnn: SqlConnection) (kind: Kind) : AsyncStream<StaticRow> =
        // Bracket-quoting flows through ScriptDom's
        // `Identifier.EncodeIdentifier` (canonical, vendor-supplied
        // SQL-identifier encoder). Eliminates the prior `sprintf
        // "[%s]"` hand-rolled bracket-quoting at three call sites
        // (column names, schema, table) — audit Top-10 #10: single
        // source of truth for SQL identifier quoting.
        let encode = Microsoft.SqlServer.TransactSql.ScriptDom.Identifier.EncodeIdentifier
        let columns =
            kind.Attributes
            |> List.map (fun a -> encode a.Column.ColumnName)
            |> String.concat ", "  // LINT-ALLOW: terminal SQL-text-emission boundary; segments are typed (already encoded)
        let pkCol =
            kind.Attributes
            |> List.tryFind (fun a -> a.IsPrimaryKey)
            |> Option.map (fun a -> a.Column.ColumnName)
            |> Option.defaultValue (
                kind.Attributes |> List.head |> fun a -> a.Column.ColumnName)
        let qualified =
            System.String.Join(  // LINT-ALLOW: terminal SQL-text-emission boundary; segments are typed (each via Identifier.EncodeIdentifier)
                ".",
                [| encode kind.Physical.Schema; encode kind.Physical.Table |])
        let cmdText =
            System.String.Concat(  // LINT-ALLOW: terminal SQL-text-emission boundary; columns/qualified/encode results are typed safe segments
                "SELECT ", columns,
                " FROM ", qualified,
                " ORDER BY ", encode pkCol)
        let mutable cmdOpt : SqlCommand option = None
        let mutable readerOpt : SqlDataReader option = None
        let mutable rowIdx = 0
        let mutable disposed = false
        let dispose () =
            if not disposed then
                disposed <- true
                match readerOpt with
                | Some r -> r.Dispose()
                | None -> ()
                match cmdOpt with
                | Some c -> c.Dispose()
                | None -> ()
                readerOpt <- None
                cmdOpt <- None
        let openReader () =
            task {
                use _ = Bench.scope "readside.readRowsStream.open"
                let cmd = cnn.CreateCommand()
                cmd.CommandText <- cmdText
                cmd.CommandTimeout <- 0
                cmdOpt <- Some cmd
                let! reader = cmd.ExecuteReaderAsync()
                readerOpt <- Some reader
            }
        let pull () : Task<StaticRow option> =
            task {
                if disposed then return None
                else
                    try
                        if Option.isNone readerOpt then do! openReader ()
                        let r = Option.get readerOpt
                        let! more = r.ReadAsync()
                        if not more then
                            dispose ()
                            return None
                        else
                            let values =
                                kind.Attributes
                                |> List.mapi (fun i a ->
                                    let raw : obj | null =
                                        if r.IsDBNull i then null
                                        else r.GetValue i
                                    a.Name, formatRawValue a.Type raw)
                                |> Map.ofList
                            let basis =
                                sprintf
                                    "%s.%s.%d"
                                    kind.Physical.Schema
                                    kind.Physical.Table
                                    rowIdx
                            rowIdx <- rowIdx + 1
                            match SsKey.synthesized "READSIDE_ROW" basis with
                            | Ok rowKey ->
                                return Some { Identifier = rowKey; Values = values }
                            | Error _ ->
                                // SsKey.synthesized only fails on blank input;
                                // basis is non-blank by construction.
                                dispose ()
                                return None
                    with ex ->
                        dispose ()
                        return raise ex
            }
        pull
        |> AsyncStream.probe (sprintf "readside.readRowsStream.%s.%s" kind.Physical.Schema kind.Physical.Table)
        |> AsyncStream.probe "readside.readRowsStream.all"

    /// Buffered wrapper: probe COUNT(*), and if ≤ `maxRows`, drain
    /// `readRowsStream` into a list. Above threshold, return `None`
    /// without opening the row reader. Per session-34, this is the
    /// existing-shape API for the per-row PhysicalSchema axis;
    /// large-table digesting goes through `readRowsStream` directly.
    let private readRows
        (cnn: SqlConnection)
        (kind: Kind)
        (maxRows: int)
        : Task<StaticRow list option> =
        task {
            use _ = Bench.scope "readside.readRows"
            // Bracket-quoting flows through ScriptDom's
            // `Identifier.EncodeIdentifier` to match `readRowsStream`
            // (single source of truth for SQL identifier encoding;
            // audit Section 4 consistency fix).
            let encode = Microsoft.SqlServer.TransactSql.ScriptDom.Identifier.EncodeIdentifier
            let qualified =
                System.String.Join(  // LINT-ALLOW: terminal SQL-text-emission boundary; segments are typed (each via Identifier.EncodeIdentifier)
                    ".",
                    [| encode kind.Physical.Schema; encode kind.Physical.Table |])
            use countCmd = cnn.CreateCommand()
            countCmd.CommandText <-
                System.String.Concat("SELECT COUNT(*) FROM ", qualified)  // LINT-ALLOW: terminal SQL-text-emission boundary; qualified is pre-encoded
            let! countObj = countCmd.ExecuteScalarAsync()
            let count = System.Convert.ToInt32 countObj
            if count > maxRows then
                return None
            elif count = 0 then
                return Some []
            else
                let stream = readRowsStream cnn kind
                let! rows = AsyncStream.toList stream
                return Some rows
        }

    let private buildKind
        (schema: string)
        (table: string)
        (columnRows: list<ColumnRow>)
        (primaryKeySet: Set<string * string * string>)
        (identitySet: Set<string * string * string>)
        : Result<Kind> =
        use _ = Bench.scope "readside.buildKind"
        // Chapter-3.6 adoption-trigger cash-out: `result { }` CE
        // replaces the prior 4-deep nested-match chain. Reads as
        // the algebraic spec; short-circuits on first failure.
        let attrResults =
            columnRows
            |> Bench.iterMap "readside.buildAttribute" (fun row ->
                buildAttribute row primaryKeySet identitySet)
        result {
            let! attributes = Result.aggregate attrResults
            let! kKey = kindSsKey schema table
            let! kName = Name.create table
            return
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
        // Chapter-3.6: `result { }` CE replaces the prior 4-deep
        // nested-match chain. Same short-circuit semantics; reads
        // as the algebraic spec.
        result {
            let! srcAttrKey = attributeSsKey srcSchema srcTable srcColumn
            let! tgtKindKey = kindSsKey tgtSchema tgtTable
            let! refKey =
                SsKey.synthesized
                    "READSIDE_REF"
                    (sprintf "%s.%s.%s" srcSchema srcTable srcColumn)
            let! refName = Name.create (sprintf "FK_%s_%s" srcTable srcColumn)
            return
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
            result {
                let! refs = fks |> List.map buildReference |> Result.aggregate
                return { k with References = refs }
            }

    /// Combined-query variant of the four schema-readback queries
    /// (`readColumnRows` + `readPrimaryKeys` + `readIdentityColumns`
    /// + `readForeignKeys`). Sends ONE `SqlCommand` containing four
    /// SQL batches separated by `;`, then walks the four result sets
    /// via `NextResultAsync`. **Perf-implications (pillar 7):**
    /// eliminates 3 of the 4 round-trips per `read` call —
    /// per-canary-readback ~150-300ms shaved on the warm container
    /// (chapter-3.6 perf-aware close-out optimization). The
    /// individual single-query helpers (`readColumnRows`,
    /// `readPrimaryKeys`, `readIdentityColumns`,
    /// `readForeignKeys`) are preserved so tests can exercise each
    /// projection independently.
    ///
    /// Big-O: same as the prior sum (one query per projection); the
    /// win is round-trip reduction, not asymptotic.
    let private readSchemaCombined (cnn: SqlConnection)
        : Task<list<ColumnRow> * Set<string * string * string> * Set<string * string * string> * list<string * string * string * string * string * string>> =
        task {
            use _ = Bench.scope "readside.readSchemaCombined"
            use cmd = cnn.CreateCommand()
            cmd.CommandText <-
                // Four batches separated by `;`. Order matters — the
                // `NextResultAsync` walk below depends on it.
                //   1. columns       (INFORMATION_SCHEMA.COLUMNS)
                //   2. primary keys  (INFORMATION_SCHEMA.TABLE_CONSTRAINTS join)
                //   3. identity cols (sys.columns)
                //   4. foreign keys  (sys.foreign_keys join)
                "SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, DATA_TYPE, IS_NULLABLE, \
                        CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE \
                 FROM INFORMATION_SCHEMA.COLUMNS \
                 WHERE TABLE_SCHEMA NOT IN ('sys','INFORMATION_SCHEMA') \
                 ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION; \
                 SELECT kcu.TABLE_SCHEMA, kcu.TABLE_NAME, kcu.COLUMN_NAME \
                 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc \
                 JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu \
                   ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME \
                  AND tc.TABLE_SCHEMA = kcu.TABLE_SCHEMA \
                 WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY' \
                   AND tc.TABLE_SCHEMA NOT IN ('sys','INFORMATION_SCHEMA'); \
                 SELECT SCHEMA_NAME(t.schema_id), t.name, c.name \
                 FROM sys.columns c \
                 JOIN sys.tables t ON t.object_id = c.object_id \
                 WHERE c.is_identity = 1 \
                   AND t.is_ms_shipped = 0; \
                 SELECT \
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

            // Result set 1: column rows (matches readColumnRows shape).
            let columnRows = ResizeArray<ColumnRow>()
            let optInt (idx: int) : int option =
                if reader.IsDBNull idx then None
                else
                    let raw = reader.GetValue idx
                    match raw with
                    | :? int32 as i32 -> Some (int i32)
                    | :? int16 as i16 -> Some (int i16)
                    | :? int64 as i64 -> Some (int i64)
                    | :? byte as b -> Some (int b)
                    | _ -> None
            let mutable hasMore1 = true
            while hasMore1 do
                let! more = reader.ReadAsync()
                if more then
                    let lengthRaw = optInt 5
                    let length =
                        match lengthRaw with
                        | Some -1 -> None  // SQL Server's MAX marker
                        | other -> other
                    columnRows.Add(
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
                else hasMore1 <- false

            // Result set 2: primary-key triples.
            let! _ = reader.NextResultAsync()
            let primaryKeySet = System.Collections.Generic.HashSet<string * string * string>()
            let mutable hasMore2 = true
            while hasMore2 do
                let! more = reader.ReadAsync()
                if more then
                    primaryKeySet.Add(
                        reader.GetString 0,
                        reader.GetString 1,
                        reader.GetString 2) |> ignore
                else hasMore2 <- false

            // Result set 3: identity-column triples.
            let! _ = reader.NextResultAsync()
            let identitySet = System.Collections.Generic.HashSet<string * string * string>()
            let mutable hasMore3 = true
            while hasMore3 do
                let! more = reader.ReadAsync()
                if more then
                    identitySet.Add(
                        reader.GetString 0,
                        reader.GetString 1,
                        reader.GetString 2) |> ignore
                else hasMore3 <- false

            // Result set 4: foreign-key tuples.
            let! _ = reader.NextResultAsync()
            let fkRows = ResizeArray<string * string * string * string * string * string>()
            let mutable hasMore4 = true
            while hasMore4 do
                let! more = reader.ReadAsync()
                if more then
                    fkRows.Add(
                        reader.GetString 0,
                        reader.GetString 1,
                        reader.GetString 2,
                        reader.GetString 3,
                        reader.GetString 4,
                        reader.GetString 5)
                else hasMore4 <- false

            return List.ofSeq columnRows, Set.ofSeq primaryKeySet, Set.ofSeq identitySet, List.ofSeq fkRows
        }

    let read (cnn: SqlConnection) : Task<Result<Catalog>> =
        task {
            use _ = Bench.scope "readside.read"
            try
                // Single round-trip via `readSchemaCombined` (chapter
                // 3.6 quick-win optimization): 4 result sets returned
                // by one `SqlCommand` instead of 4 separate
                // `ExecuteReaderAsync` calls. ~150-300ms saved per
                // canary-readback on the warm container.
                let! columnRows, primaryKeySet, identitySet, fkRows = readSchemaCombined cnn
                let kindResults =
                    columnRows
                    |> List.groupBy (fun row -> row.Schema, row.Table)
                    |> Bench.iterMap "readside.kindGroup" (fun ((schema, table), rows) ->
                        buildKind schema table rows primaryKeySet identitySet)
                match Result.aggregate kindResults with
                | Error errors -> return Result.failure errors
                | Ok kinds ->
                    let fkGroups =
                        fkRows
                        |> List.groupBy (fun (s, t, _, _, _, _) -> s, t)
                        |> Map.ofList
                    let kindsWithRefsResults =
                        kinds
                        |> Bench.iterMap "readside.attachReferences" (attachReferences fkGroups)
                    match Result.aggregate kindsWithRefsResults with
                    | Error errors -> return Result.failure errors
                    | Ok kindsWithRefs ->
                        // Per session-34 — threshold lifted to 100k.
                        // Below that, rows materialize into V2 IR
                        // (`Modality.Static`) for the per-row PhysicalSchema
                        // axis; above, the round-trip falls back to
                        // schema-only (no rows in IR). Streaming
                        // realizations (`readRowsStream`) bypass the
                        // threshold and feed digesters directly without
                        // IR materialization — chapter-4.1 territory.
                        let maxRows = 100_000
                        let kindsWithRows = ResizeArray<Kind>(List.length kindsWithRefs)
                        for k in kindsWithRefs do
                            let! rowsOpt = readRows cnn k maxRows
                            let kindWithRows =
                                match rowsOpt with
                                | Some rows when not (List.isEmpty rows) ->
                                    { k with Modality = [ Static rows ] }
                                | _ -> k
                            kindsWithRows.Add kindWithRows
                        let kindsWithRows = List.ofSeq kindsWithRows
                        match moduleSsKey () with
                        | Error errors -> return Result.failure errors
                        | Ok mKey ->
                            match Name.create reconstructedModuleName with
                            | Error errors -> return Result.failure errors
                            | Ok mName ->
                                return
                                    Result.success
                                        {
                                            Modules =
                                                [
                                                    {
                                                        SsKey = mKey
                                                        Name = mName
                                                        Kinds = kindsWithRows
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
