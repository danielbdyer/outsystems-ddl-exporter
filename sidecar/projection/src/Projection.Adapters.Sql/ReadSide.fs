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
/// (`Origin.Native`, empty `Modality`, single `Reconstructed`
/// module) — comparison surfaces (`PhysicalSchema`) ignore the
/// V2-IR-only axes by construction.
[<RequireQualifiedAccess>]
module ReadSide =

    /// Reverse type mapping: SQL Server's INFORMATION_SCHEMA
    /// `DATA_TYPE` value → V2 `PrimitiveType`. Delegates to the
    /// type-correspondence bounded context's inverse classification
    /// (`SqlTypeCorrespondence.ofSqlDataType`), which is the single
    /// source of truth shared with `Projection.Targets.SSDT.Render
    /// .columnSqlType` (the forward direction). Chapter-3.7 slice β;
    /// audit Tier-1 #8.
    let private mapSqlType (dataType: string) : Result<PrimitiveType> =
        SqlTypeCorrespondence.ofSqlDataType dataType

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

    /// One foreign-key column reflected from `sys.foreign_keys ⋈
    /// sys.foreign_key_columns`: the referencing (source) coordinate, the
    /// referenced (target) coordinate, and whether the constraint is
    /// untrusted (`WITH NOCHECK` — `sys.foreign_keys.is_not_trusted`). The
    /// read-side's typed FK row; `buildReference` is its sole consumer.
    /// 6.A.5 — `IsNotTrusted` lifted the prior anonymous 6-string tuple to
    /// a named record so the FK-trust axis reads by name, not by position.
    type private FkRow =
        {
            SourceSchema : string
            SourceTable  : string
            SourceColumn : string
            TargetSchema : string
            TargetTable  : string
            TargetColumn : string
            IsNotTrusted : bool
        }

    /// E2 (debrief G4) — classify an FK metadata row before it becomes a
    /// `Reference`. `SCHEMA_NAME()` returns NULL when a referenced/parent
    /// schema was dropped between the metadata read and the FK probe, or
    /// when the account lacks `VIEW DEFINITION` on that schema. Such a row
    /// cannot be faithfully reconstructed; per the no-silent-drop boundary
    /// axiom it must surface a NAMED diagnostic — not a silent skip, not an
    /// opaque `GetString` cast failure that aborts the whole readback. Pure +
    /// public so the classification is unit-witnessed without a live
    /// substrate (`tests/Projection.Tests/ForeignKeyReadbackTests.fs`).
    [<RequireQualifiedAccess>]
    module ForeignKeyReadback =

        /// All coordinates resolved non-blank — the row reconstructs.
        type FkCoordinates =
            {
                SourceSchema : string
                SourceTable  : string
                SourceColumn : string
                TargetSchema : string
                TargetTable  : string
                TargetColumn : string
                IsNotTrusted : bool
            }

        type Classification =
            | Reconstructable of FkCoordinates
            | Unreadable of reason: string

        let private norm (o: string option) : string option =
            o |> Option.map (fun (s: string) -> s.Trim()) |> Option.filter (fun s -> s <> "")

        /// Classify a raw FK row. `None` models a NULL read (`SCHEMA_NAME()`
        /// or a NULL column); a blank/whitespace string is treated
        /// identically. On an unreadable coordinate the reason names the
        /// visible endpoints and which side's schema was lost, plus the two
        /// likely causes, so an operator can locate and fix the grant.
        let classify
            (sourceSchema: string option) (sourceTable: string option) (sourceColumn: string option)
            (targetSchema: string option) (targetTable: string option) (targetColumn: string option)
            (isNotTrusted: bool)
            : Classification =
            match norm sourceSchema, norm sourceTable, norm sourceColumn,
                  norm targetSchema, norm targetTable, norm targetColumn with
            | Some ss, Some st, Some sc, Some ts, Some tt, Some tc ->
                Reconstructable
                    { SourceSchema = ss; SourceTable = st; SourceColumn = sc
                      TargetSchema = ts; TargetTable = tt; TargetColumn = tc
                      IsNotTrusted = isNotTrusted }
            | nSrcSchema, _, _, nTgtSchema, _, _ ->
                let show (o: string option) = norm o |> Option.defaultValue "<unreadable>"
                let src = System.String.Concat(show sourceSchema, ".", show sourceTable, ".", show sourceColumn)
                let tgt = System.String.Concat(show targetSchema, ".", show targetTable, ".", show targetColumn)
                let which =
                    match nSrcSchema, nTgtSchema with
                    | None, None -> "both endpoints' schemas"
                    | None, _ -> "the parent schema"
                    | _, None -> "the referenced schema"
                    | _ -> "a coordinate"
                Unreadable
                    (System.String.Concat(
                        "readside.foreignKeys: cross-schema FK ", src, " -> ", tgt,
                        " skipped — ", which,
                        " unreadable (NULL SCHEMA_NAME: dropped schema, or missing VIEW DEFINITION grant on a least-privilege account)"))

    /// One key column of a reflected non-PK index (`sys.indexes ⋈
    /// sys.index_columns`), within a `(schema, table)` group: the owning
    /// index's name + uniqueness, the participating column, its sort
    /// direction, and its 1-based position in the index key. 6.A.5 —
    /// `attachIndexes` groups these by `IndexName` and rebuilds each
    /// `Index` in `KeyOrdinal` order.
    type private IndexColumnRow =
        {
            IndexName    : string
            IsUnique     : bool
            ColumnName   : string
            IsDescending : bool
            KeyOrdinal   : int
        }

    /// One reflected `sys.sequences` row: the full sequence shape recovered
    /// from the deployed catalog. `buildSequences` reconstructs a
    /// `Sequence` per row via the `Sequence.create` smart constructor.
    /// A named record so the ten axes read by name, not by tuple position.
    type private SequenceRow =
        {
            Schema       : string
            Name         : string
            DataType     : string
            StartValue   : decimal option
            Increment    : decimal option
            MinimumValue : decimal option
            MaximumValue : decimal option
            IsCycling    : bool
            IsCached     : bool
            CacheSize    : int option
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
    /// Read a nullable integer column as `int option`, coercing the
    /// common SQL integer CLR types. The body was previously duplicated
    /// verbatim in `readColumnRows` and `readSchemaCombined`.
    let private optIntOf (reader: SqlDataReader) (idx: int) : int option =
        if reader.IsDBNull idx then None
        else
            match reader.GetValue idx with
            | :? int32 as i32 -> Some (int i32)
            | :? int16 as i16 -> Some (int i16)
            | :? int64 as i64 -> Some (int i64)
            | :? byte as b -> Some (int b)
            | _ -> None

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
            let optInt = optIntOf reader
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
            // Defensive diagnostic (slice A.4.7'-prelude.defensive-
            // hardening): zero column rows from INFORMATION_SCHEMA.COLUMNS
            // is a SIGNAL not silence. Either (a) the user-schema is
            // genuinely empty (rare in production), or (b) the
            // VIEW DEFINITION permission is restricted (the user has
            // db_datareader but cannot see metadata). Surface the
            // signal so operators can diagnose; downstream emits an
            // empty SSDT bundle otherwise.
            if rows.Count = 0 then
                eprintfn
                    "readside.readColumnRows: zero rows from INFORMATION_SCHEMA.COLUMNS; verify VIEW DEFINITION permission on user schemas (Azure SQL least-privilege accounts may filter metadata)"
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

    /// Wave-1 slice 1.2 — read each column's DEFAULT-constraint definition
    /// from `sys.default_constraints`, keyed by `(schema, table, column)`.
    /// The `definition` column is SQL Server's canonical parenthesized form
    /// (e.g. `((0))`, `('foo')`, `(getdate())`); `DefaultExpr.normalize`
    /// (in PhysicalSchema) strips the redundant outer-paren wrapping SQL
    /// Server adds so the read-back value compares equal to the emitter's
    /// `SqlLiteral.toString` form. Single round-trip; small result set;
    /// mirrors `readIdentityColumns`.
    let private readDefaultConstraints (cnn: SqlConnection)
        : Task<Map<string * string * string, string>> =
        task {
            use _ = Bench.scope "readside.readDefaultConstraints"
            use cmd = cnn.CreateCommand()
            cmd.CommandText <-
                "SELECT SCHEMA_NAME(t.schema_id), t.name, c.name, dc.definition \
                 FROM sys.default_constraints dc \
                 JOIN sys.columns c \
                   ON c.object_id = dc.parent_object_id \
                  AND c.column_id = dc.parent_column_id \
                 JOIN sys.tables t ON t.object_id = dc.parent_object_id \
                 WHERE t.is_ms_shipped = 0"
            use! reader = cmd.ExecuteReaderAsync()
            let result =
                System.Collections.Generic.Dictionary<string * string * string, string>()
            let mutable hasMore = true
            while hasMore do
                let! more = reader.ReadAsync()
                if more then
                    let key = (reader.GetString 0, reader.GetString 1, reader.GetString 2)
                    result.[key] <- reader.GetString 3
                else
                    hasMore <- false
            return result |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq
        }

    /// Wave-1 slice 1.3 (L3-S7 real-SQL leg) — read computed-column
    /// definitions from `sys.computed_columns`, keyed by `(schema, table,
    /// column)`. Value is `(definition, isPersisted)`. The `definition` is
    /// SQL Server's canonical parenthesized expression (e.g. `([QTY]*(100))`);
    /// `PhysicalSchema.encodeComputed` (via `ComputedColumnConfig`) carries
    /// the paren-normalization tolerance. Single round-trip; mirrors
    /// `readDefaultConstraints`. Closes the last hollow-canary feature.
    let private readComputedColumns (cnn: SqlConnection)
        : Task<Map<string * string * string, string * bool>> =
        task {
            use _ = Bench.scope "readside.readComputedColumns"
            use cmd = cnn.CreateCommand()
            cmd.CommandText <-
                "SELECT SCHEMA_NAME(t.schema_id), t.name, c.name, \
                        cc.definition, cc.is_persisted \
                 FROM sys.computed_columns cc \
                 JOIN sys.columns c \
                   ON c.object_id = cc.object_id AND c.column_id = cc.column_id \
                 JOIN sys.tables t ON t.object_id = cc.object_id \
                 WHERE t.is_ms_shipped = 0"
            use! reader = cmd.ExecuteReaderAsync()
            let result =
                System.Collections.Generic.Dictionary<string * string * string, string * bool>()
            let mutable hasMore = true
            while hasMore do
                let! more = reader.ReadAsync()
                if more then
                    let key = (reader.GetString 0, reader.GetString 1, reader.GetString 2)
                    result.[key] <- (reader.GetString 3, reader.GetBoolean 4)
                else
                    hasMore <- false
            return result |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq
        }

    /// Drain a SQL reader into `Map<'K, 'T list>`, grouping each row under
    /// `keyOf reader` with `entryOf reader` as the value and preserving the
    /// reader's row order within each group. Collapses the
    /// `TryGetValue`/else-create-`ResizeArray` group-append loop shared by
    /// `readTriggers` / `readCheckConstraints` / `readIndexes` /
    /// `readExtendedProperties` (E4, 2026-06-04 — 4-consumer kernel).
    /// Single forward pass; callers that need a specific in-group order
    /// `ORDER BY` in SQL (see `readIndexes`).
    let private readGrouped
            (keyOf: SqlDataReader -> 'K)
            (entryOf: SqlDataReader -> 'T)
            (reader: SqlDataReader)
            : Task<Map<'K, 'T list>> =
        task {
            let acc = System.Collections.Generic.Dictionary<'K, ResizeArray<'T>>()
            let mutable hasMore = true
            while hasMore do
                let! more = reader.ReadAsync()
                if more then
                    let key = keyOf reader
                    let entry = entryOf reader
                    match acc.TryGetValue key with
                    | true, lst -> lst.Add entry
                    | false, _ ->
                        let lst = ResizeArray<_>() in lst.Add entry; acc.[key] <- lst
                else hasMore <- false
            return acc |> Seq.map (fun kv -> kv.Key, List.ofSeq kv.Value) |> Map.ofSeq
        }

    /// Wave-1 slice 1.3 — read DML triggers from `sys.triggers`, keyed by
    /// `(schema, table)`. Each value is `(triggerName, isDisabled, body)`;
    /// the body is the `OBJECT_DEFINITION` text (PhysicalSchema normalizes
    /// it). Single round-trip.
    let private readTriggers (cnn: SqlConnection)
        : Task<Map<string * string, (string * bool * string) list>> =
        task {
            use _ = Bench.scope "readside.readTriggers"
            use cmd = cnn.CreateCommand()
            cmd.CommandText <-
                "SELECT SCHEMA_NAME(t.schema_id), t.name, tr.name, \
                        tr.is_disabled, OBJECT_DEFINITION(tr.object_id) \
                 FROM sys.triggers tr \
                 JOIN sys.tables t ON t.object_id = tr.parent_id \
                 WHERE tr.is_ms_shipped = 0 AND t.is_ms_shipped = 0"
            use! reader = cmd.ExecuteReaderAsync()
            return!
                readGrouped
                    (fun r -> (r.GetString 0, r.GetString 1))
                    (fun r ->
                        let body = if r.IsDBNull 4 then "" else r.GetString 4
                        (r.GetString 2, r.GetBoolean 3, body))
                    reader
        }

    /// Wave-1 slice 1.3 — read CHECK constraints from `sys.check_constraints`,
    /// keyed by `(schema, table)`. Value is `(constraintName, definition,
    /// isNotTrusted)`. Single round-trip.
    let private readCheckConstraints (cnn: SqlConnection)
        : Task<Map<string * string, (string * string * bool) list>> =
        task {
            use _ = Bench.scope "readside.readCheckConstraints"
            use cmd = cnn.CreateCommand()
            cmd.CommandText <-
                "SELECT SCHEMA_NAME(t.schema_id), t.name, cc.name, \
                        cc.definition, cc.is_not_trusted \
                 FROM sys.check_constraints cc \
                 JOIN sys.tables t ON t.object_id = cc.parent_object_id \
                 WHERE t.is_ms_shipped = 0"
            use! reader = cmd.ExecuteReaderAsync()
            return!
                readGrouped
                    (fun r -> (r.GetString 0, r.GetString 1))
                    (fun r -> (r.GetString 2, r.GetString 3, r.GetBoolean 4))
                    reader
        }

    /// 6.A.5 — read non-PK indexes (`sys.indexes ⋈ sys.index_columns`),
    /// keyed by `(schema, table)`. Each row is `(indexName, isUnique,
    /// columnName, isDescending, keyOrdinal)`, ordered by index then key
    /// ordinal so `attachIndexes` rebuilds each index's key-column list in
    /// declaration order. PK-backing indexes (`is_primary_key = 1`) are
    /// excluded — the PK is modeled on the attributes (`IsPrimaryKey`), not
    /// as an `Index`. Heaps (`type = 0`) and INCLUDE (non-key) columns are
    /// excluded (V2 `Index.Columns` is key-only). Single round-trip,
    /// mirroring `readCheckConstraints`.
    let private readIndexes (cnn: SqlConnection)
        : Task<Map<string * string, IndexColumnRow list>> =
        task {
            use _ = Bench.scope "readside.readIndexes"
            use cmd = cnn.CreateCommand()
            cmd.CommandText <-
                "SELECT SCHEMA_NAME(t.schema_id), t.name, i.name, i.is_unique, \
                        c.name, ic.is_descending_key, ic.key_ordinal \
                 FROM sys.indexes i \
                 JOIN sys.tables t ON t.object_id = i.object_id \
                 JOIN sys.index_columns ic \
                   ON ic.object_id = i.object_id AND ic.index_id = i.index_id \
                 JOIN sys.columns c \
                   ON c.object_id = ic.object_id AND c.column_id = ic.column_id \
                 WHERE i.is_primary_key = 0 AND i.type > 0 AND i.name IS NOT NULL \
                   AND ic.is_included_column = 0 AND t.is_ms_shipped = 0 \
                 ORDER BY SCHEMA_NAME(t.schema_id), t.name, i.name, ic.key_ordinal"
            use! reader = cmd.ExecuteReaderAsync()
            return!
                readGrouped
                    (fun r -> (r.GetString 0, r.GetString 1))
                    (fun r ->
                        { IndexName    = r.GetString 2
                          IsUnique     = r.GetBoolean 3
                          ColumnName   = r.GetString 4
                          IsDescending = r.GetBoolean 5
                          KeyOrdinal   = System.Convert.ToInt32(r.GetValue 6) })
                    reader
        }

    /// Wave-1 slice 1.3 — read SEQUENCE objects from `sys.sequences`.
    /// Each row carries the full shape (type, start/increment/min/max,
    /// cycle, cache). Single round-trip; small result set.
    let private readSequences (cnn: SqlConnection) : Task<SequenceRow list> =
        task {
            use _ = Bench.scope "readside.readSequences"
            use cmd = cnn.CreateCommand()
            cmd.CommandText <-
                "SELECT SCHEMA_NAME(s.schema_id), s.name, TYPE_NAME(s.system_type_id), \
                        s.start_value, s.increment, s.minimum_value, s.maximum_value, \
                        s.is_cycling, s.is_cached, s.cache_size \
                 FROM sys.sequences s"
            use! reader = cmd.ExecuteReaderAsync()
            let optDec (i: int) : decimal option =
                if reader.IsDBNull i then None else Some (System.Convert.ToDecimal(reader.GetValue i))
            let optInt (i: int) : int option =
                if reader.IsDBNull i then None else Some (System.Convert.ToInt32(reader.GetValue i))
            let rows = ResizeArray<SequenceRow>()
            let mutable hasMore = true
            while hasMore do
                let! more = reader.ReadAsync()
                if more then
                    rows.Add(
                        { Schema       = reader.GetString 0
                          Name         = reader.GetString 1
                          DataType     = reader.GetString 2
                          StartValue   = optDec 3
                          Increment    = optDec 4
                          MinimumValue = optDec 5
                          MaximumValue = optDec 6
                          IsCycling    = reader.GetBoolean 7
                          IsCached     = reader.GetBoolean 8
                          CacheSize    = optInt 9 })
                else hasMore <- false
            return List.ofSeq rows
        }

    /// Wave-1 slice 1.3 — read NON-`V2.LogicalName` extended properties on
    /// tables + columns from `sys.extended_properties`, keyed by
    /// `(schema, table, columnOrNull)`. The `V2.LogicalName` property is
    /// excluded — it is the emitter's own round-trip scaffolding, covered by
    /// the LogicalNameBindings axis. Value is `(propName, propValue) list`.
    let private readExtendedProperties (cnn: SqlConnection)
        : Task<Map<string * string * string option, (string * string) list>> =
        task {
            use _ = Bench.scope "readside.readExtendedProperties"
            use cmd = cnn.CreateCommand()
            cmd.CommandText <-
                "SELECT SCHEMA_NAME(t.schema_id), t.name, c.name, \
                        ep.name, CAST(ep.value AS NVARCHAR(MAX)) \
                 FROM sys.extended_properties ep \
                 JOIN sys.tables t ON t.object_id = ep.major_id \
                 LEFT JOIN sys.columns c \
                   ON c.object_id = ep.major_id AND c.column_id = ep.minor_id \
                 WHERE ep.class = 1 AND t.is_ms_shipped = 0 \
                   AND ep.name <> N'V2.LogicalName' \
                   AND ep.name <> N'V2.SsKey'"
            use! reader = cmd.ExecuteReaderAsync()
            return!
                readGrouped
                    (fun r ->
                        let col = if r.IsDBNull 2 then None else Some (r.GetString 2)
                        (r.GetString 0, r.GetString 1, col))
                    (fun r ->
                        let value = if r.IsDBNull 4 then "" else r.GetString 4
                        (r.GetString 3, value))
                    reader
        }

    /// Read the set of FK relationships across every user table as typed
    /// `FkRow`s (carrying the `WITH NOCHECK` trust state). Composite FKs
    /// surface as multiple rows. Uses sys.* directly; REFERENTIAL_CONSTRAINTS
    /// / KEY_COLUMN_USAGE shape is awkward for the source/target column-pair
    /// join we need. The live readback path uses the combined `readSchemaCombined`
    /// batch; this stand-alone reader mirrors that batch's FK SELECT for
    /// out-of-band FK-metadata probing.
    let private readForeignKeys (cnn: SqlConnection) : Task<FkRow list> =
        task {
            use _ = Bench.scope "readside.readForeignKeys"
            use cmd = cnn.CreateCommand()
            cmd.CommandText <-
                "SELECT \
                    SCHEMA_NAME(t.schema_id), t.name, c.name, \
                    SCHEMA_NAME(rt.schema_id), rt.name, rc.name, \
                    fk.is_not_trusted \
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
            let rows = ResizeArray<FkRow>()
            let mutable hasMore = true
            // E2 (debrief G4): a NULL SCHEMA_NAME() (dropped schema /
            // missing VIEW DEFINITION grant) is classified Unreadable and
            // surfaces a NAMED diagnostic + skip — never a silent drop nor
            // an opaque GetString cast failure.
            let rawOpt i = if reader.IsDBNull i then None else Some (reader.GetString i)
            while hasMore do
                let! more = reader.ReadAsync()
                if more then
                    let isNotTrusted = (not (reader.IsDBNull 6)) && reader.GetBoolean 6
                    match
                        ForeignKeyReadback.classify
                            (rawOpt 0) (rawOpt 1) (rawOpt 2) (rawOpt 3) (rawOpt 4) (rawOpt 5) isNotTrusted
                        with
                    | ForeignKeyReadback.Reconstructable c ->
                        rows.Add(
                            { SourceSchema = c.SourceSchema
                              SourceTable  = c.SourceTable
                              SourceColumn = c.SourceColumn
                              TargetSchema = c.TargetSchema
                              TargetTable  = c.TargetTable
                              TargetColumn = c.TargetColumn
                              IsNotTrusted = c.IsNotTrusted })
                    | ForeignKeyReadback.Unreadable reason -> eprintfn "%s" reason
                else
                    hasMore <- false
            return List.ofSeq rows
        }

    let private buildAttribute
        (columnLogicalNames: Map<string * string * string, string>)
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
                // Slice D.1.b — hydrate Attribute.Name from the
                // `V2.LogicalName` extended property when present;
                // backward-compat fallback to the deployed column name.
                let nameSource =
                    Map.tryFind (row.Schema, row.Table, row.Column) columnLogicalNames
                    |> Option.defaultValue row.Column
                match Name.create nameSource with
                | Error errors -> Result.failure errors
                | Ok attrName ->
                    match ColumnName.create row.Column with
                    | Error errors -> Result.failure errors
                    | Ok columnName ->
                    let coord = (row.Schema, row.Table, row.Column)
                    Result.success
                        {
                            SsKey = attrKey
                            Name = attrName
                            Type = ptype
                            Column =
                                {
                                    ColumnName = columnName
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
                            // Chapter A.0' slice α — SQL Server's
                            // `INFORMATION_SCHEMA.COLUMNS` does not
                            // surface the OutSystems-defined description;
                            // descriptions live in extended properties
                            // (sys.extended_properties / fn_listextended-
                            // property). Carrying via the ReadSide path
                            // gates on chapter 4.1.A slice 8's extended-
                            // properties pickup. Slice-α scope is OSSYS-
                            // adapter pickup only.
                            Description = None
                            // Chapter A.0' slice β — ReadSide reads
                            // deployed SQL Server schema; the source
                            // has no V1 `Is_Active` axis (the column
                            // exists in the deployed table, therefore
                            // it is structurally active). `IsActive`
                            // defaults to `true` on this path; the
                            // OSSYS-adapter rowset path carries the
                            // V1-source value.
                            IsActive = true
                            // Chapter A.0' slices ε + ζ — ReadSide
                            // pickup of DEFAULT, Computed, and
                            // attribute-level ExtendedProperties
                            // gates on a future slice that queries
                            // `sys.default_constraints`,
                            // `sys.computed_columns`, and
                            // `sys.extended_properties` over the
                            // deployed schema. Empty / None defaults
                            // until that slice lands.
                            DefaultValue = None
                            DefaultName = None
                            Computed = None
                            ExtendedProperties = []
                            // Chapter 4.9 slice β — ReadSide reads the
                            // deployed schema, which carries no rename
                            // history or external-DB-type override
                            // metadata. Defaults to `None`; the OSSYS
                            // adapter (JSON / rowset paths) carries the
                            // V1-source values where present.
                            OriginalName = None
                            ExternalDatabaseType = None
                            // ReadSide reflects the deployed schema
                            // structurally; the concrete storage type
                            // is recoverable but unused here. The
                            // semantic `Type` drives the canary's
                            // PhysicalSchema comparison; `SqlStorage`
                            // stays `None` (semantic fallback) so this
                            // path's emission is unchanged.
                            SqlStorage = None
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
                // Defensive type-switch (slice
                // A.4.7'-prelude.defensive-hardening): some SQL
                // providers surface `time` columns as `DateTime`
                // rather than `TimeSpan`; the prior `:?>` cast
                // threw `InvalidCastException` on those drivers.
                let ts =
                    match v with
                    | :? System.TimeSpan as t -> t
                    | :? System.DateTime as dt -> dt.TimeOfDay
                    | other ->
                        invalidOp
                            (sprintf "ReadSide.Time: unexpected runtime type %s" (other.GetType().FullName))
                RawValueCodec.formatTime ts
            | Guid ->
                let guid =
                    match v with
                    | :? System.Guid as g -> g
                    | :? System.Data.SqlTypes.SqlGuid as sg -> sg.Value
                    | other ->
                        invalidOp
                            (sprintf "ReadSide.Guid: unexpected runtime type %s" (other.GetType().FullName))
                RawValueCodec.formatGuid guid
            | Decimal ->
                System.Convert.ToDecimal(v).ToString(inv)
            | Text ->
                match v.ToString() with
                | null -> ""
                | s -> s
            | Binary ->
                // Older SqlClient surfaces `varbinary`/`binary` as
                // `SqlBytes` / `SqlBinary` rather than `byte[]`.
                let bytes =
                    match v with
                    | :? (byte[]) as b -> b
                    | :? System.Data.SqlTypes.SqlBytes as sb -> sb.Value
                    | :? System.Data.SqlTypes.SqlBinary as sb -> sb.Value
                    | other ->
                        invalidOp
                            (sprintf "ReadSide.Binary: unexpected runtime type %s" (other.GetType().FullName))
                System.Convert.ToHexString bytes

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
            |> List.map (fun a -> encode (ColumnRealization.columnNameText a.Column))
            |> String.concat ", "  // LINT-ALLOW: terminal SQL-text-emission boundary; segments are typed (already encoded)
        let pkCol =
            kind.Attributes
            |> List.tryFind (fun a -> a.IsPrimaryKey)
            |> Option.map (fun a -> ColumnRealization.columnNameText a.Column)
            |> Option.defaultWith (fun () ->
                // Defensive against an attribute-less Kind (slice
                // A.4.7'-prelude.defensive-hardening). The A39
                // smart-constructor invariant guarantees non-empty
                // attributes for any `Kind.create`-built value;
                // this explicit match keeps the failure mode
                // legible if a future code path bypasses the
                // smart constructor.
                match kind.Attributes with
                | a :: _ -> ColumnRealization.columnNameText a.Column
                | [] ->
                    failwithf
                        "ReadSide.readRows: Kind %A has no attributes — A39 invariant violated"
                        kind.SsKey)
        let qualified =
            System.String.Join(  // LINT-ALLOW: terminal SQL-text-emission boundary; segments are typed (each via Identifier.EncodeIdentifier)
                ".",
                [| encode (TableId.schemaText kind.Physical); encode (TableId.tableText kind.Physical) |])
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
                cmd.CommandTimeout <- CommandTimeoutPolicy.resolve ()
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
                                    (TableId.schemaText kind.Physical)
                                    (TableId.tableText kind.Physical)
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
        |> AsyncStream.probe (sprintf "readside.readRowsStream.%s.%s" (TableId.schemaText kind.Physical) (TableId.tableText kind.Physical))
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
                    [| encode (TableId.schemaText kind.Physical); encode (TableId.tableText kind.Physical) |])
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

    /// Recover a kind's SsKey from the persisted `V2.SsKey` extended
    /// property when present (Wave 4.1; A1: identity survives rename), else
    /// the `READSIDE_KIND` synthesis (pre-V2-emission / non-V2 schemas, or a
    /// malformed stored value — a synthesized key is always valid, so a bad
    /// value degrades gracefully rather than failing the read). Shared by
    /// `buildKind` (a kind's own identity) and `buildReference` (an FK's
    /// **target-kind** identity) so a reconstructed FK's `TargetKind`
    /// MATCHES the reconstructed target Kind's `SsKey`. 6.A.5 — without this,
    /// a kind reconstructed from its persisted SsKey but referenced by an
    /// FK synthesizing `READSIDE_KIND` fails to resolve in
    /// `PhysicalSchema.ForeignKeys` (`Map.tryFind r.TargetKind kindByKey`
    /// misses) — the FK silently vanishes from the projection (the A42 gap).
    let private recoverKindSsKey
        (tableSsKeys: Map<string * string, string>)
        (schema: string)
        (table: string)
        : Result<SsKey> =
        match Map.tryFind (schema, table) tableSsKeys with
        | Some serialized ->
            match SsKey.deserialize serialized with
            | Ok recovered -> Result.success recovered
            | Error _ -> kindSsKey schema table
        | None -> kindSsKey schema table

    let private buildKind
        (tableLogicalNames: Map<string * string, string>)
        (columnLogicalNames: Map<string * string * string, string>)
        (tableSsKeys: Map<string * string, string>)
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
                buildAttribute columnLogicalNames row primaryKeySet identitySet)
        result {
            let! attributes = Result.aggregate attrResults
            // Wave 4.1 — recover the persisted SsKey (see `recoverKindSsKey`).
            let! kKey = recoverKindSsKey tableSsKeys schema table
            // Slice D.1.b — hydrate Kind.Name from the `V2.LogicalName`
            // extended property when present; backward-compat fallback
            // to the deployed table name.
            let nameSource =
                Map.tryFind (schema, table) tableLogicalNames
                |> Option.defaultValue table
            let! kName = Name.create nameSource
            // Slice 5 (lens-after) — TableId is typed (SchemaName /
            // TableName); construct via the smart constructor that
            // validates and wraps. Errors aggregate into the result
            // monad's first-failure.
            let! physical = TableId.create schema table
            return
                {
                    SsKey = kKey
                    Name = kName
                    Origin = Native
                    Modality = []
                    Physical = physical
                    Attributes = attributes
                    References = []
                    Indexes = []
                    // Chapter A.0' slice α — see buildAttribute rationale.
                    Description = None
                    // Chapter A.0' slice β — see buildAttribute rationale.
                    IsActive = true
                    // Chapter A.0' slices γ + ε + ζ — ReadSide does
                    // not yet pick up triggers, table-level CHECK
                    // constraints, or entity-level extended
                    // properties from the deployed schema. Empty
                    // defaults; future slices add the queries.
                    Triggers = []
                    ColumnChecks = []
                    ExtendedProperties = []
                }
        }

    /// Read all user tables + columns from a deployed database and
    /// reconstruct a V2 `Catalog`. Returns the reconstructed Catalog
    /// or aggregated validation errors.
    ///
    /// **Best-effort fields.** `Origin = Native` and `Modality = []`
    /// for every reconstructed Kind (cannot be recovered from SQL).
    /// `References` (incl. FK-trust state) are recovered via
    /// `attachReferences`, and non-PK `Indexes` (incl. uniqueness) via
    /// `attachIndexes` (6.A.5) — `buildKind` constructs both empty, the
    /// attach helpers populate them from the reflected metadata.
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
        (tableSsKeys: Map<string * string, string>)
        (fk: FkRow)
        : Result<Reference> =
        // Chapter-3.6: `result { }` CE replaces the prior 4-deep
        // nested-match chain. Same short-circuit semantics; reads
        // as the algebraic spec.
        result {
            let! srcAttrKey = attributeSsKey fk.SourceSchema fk.SourceTable fk.SourceColumn
            // 6.A.5 — recover the target kind's SsKey the same way buildKind
            // does (persisted V2.SsKey, else synthesis) so the FK resolves
            // against the reconstructed target Kind in PhysicalSchema.
            let! tgtKindKey = recoverKindSsKey tableSsKeys fk.TargetSchema fk.TargetTable
            let! refKey =
                SsKey.synthesized
                    "READSIDE_REF"
                    (sprintf "%s.%s.%s" fk.SourceSchema fk.SourceTable fk.SourceColumn)
            let! refName = Name.create (sprintf "FK_%s_%s" fk.SourceTable fk.SourceColumn)
            // Slice 5.13.fk-features-emit — smart-constructor migration.
            // ReadSide reconstructs Reference from `sys.foreign_keys`,
            // which by definition lists references backed by DB
            // constraints (HasDbConstraint = true). 6.A.5 — the FK batch
            // now also carries `sys.foreign_keys.is_not_trusted`, so a
            // deployed `WITH NOCHECK` FK reads back as
            // `IsConstraintTrusted = false` instead of silently
            // inheriting the `Reference.create` `true` default
            // (the named smart-constructor default-substitution hazard:
            // every axis that deviates from the constructor default MUST
            // be set explicitly in the `with` block). OnDelete / OnUpdate
            // referential-action axes remain defaulted until a follow-up
            // slice joins `sys.foreign_keys`'s action columns.
            // G14 — route the constraint-state pair through the guard (FKs from
            // sys.foreign_keys always have a real constraint, so this is the
            // consistent `(true, trusted)` case; uniform with the V1 producer).
            return
                Reference.create refKey refName srcAttrKey tgtKindKey
                |> Reference.withConstraintState true (not fk.IsNotTrusted)
        }

    /// Attach references to a Kind based on the FKs grouped by
    /// (schema, table) coordinates. Per session-31 Session B.
    let private attachReferences
        (tableSsKeys: Map<string * string, string>)
        (fkGroups: Map<string * string, FkRow list>)
        (k: Kind)
        : Result<Kind> =
        match fkGroups.TryFind(TableId.qualifiedParts k.Physical) with
        | None -> Result.success k
        | Some fks ->
            result {
                let! refs = fks |> List.map (buildReference tableSsKeys) |> Result.aggregate
                return { k with References = refs }
            }

    /// Wave-1 slice 1.2 — attach recovered DEFAULT-constraint values to a
    /// Kind's attributes. `defaults` maps `(schema, table, column)` to the
    /// raw `sys.default_constraints.definition`. For each attribute with a
    /// recovered default, normalize SQL Server's parenthesization
    /// (`PhysicalSchema.normalizeDefault`) and reconstruct a typed
    /// `SqlLiteral` via `SqlLiteral.ofRaw attr.Type` — the inverse of the
    /// emitter's `SqlLiteral.toString`. Because both the emitter-IR side
    /// (`PhysicalSchema.ofCatalog`) and this read-side both pass through
    /// `normalizeDefault`, the DEFAULT survives the emit → deploy → read
    /// round-trip on the `PhysicalColumn.Default` axis. Attributes without a
    /// recovered default keep `DefaultValue = None`.
    let private attachDefaults
        (defaults: Map<string * string * string, string>)
        (k: Kind)
        : Kind =
        if Map.isEmpty defaults then k
        else
            let attrs =
                k.Attributes
                |> List.map (fun a ->
                    let schemaStr, tableStr = TableId.qualifiedParts k.Physical
                    let coord = (schemaStr, tableStr, ColumnRealization.columnNameText a.Column)
                    match Map.tryFind coord defaults with
                    | Some definition ->
                        let normalized = PhysicalSchema.normalizeDefault definition
                        { a with DefaultValue = Some (SqlLiteral.ofRaw a.Type normalized) }
                    | None -> a)
            { k with Attributes = attrs }

    /// Wave-1 slice 1.3 (L3-S7) — attach recovered computed-column configs to
    /// a Kind's attributes. `computed` maps `(schema, table, column)` to the
    /// raw `sys.computed_columns.definition` + `is_persisted`. Reconstructs
    /// `Attribute.Computed` via the `ComputedColumnConfig.create` smart
    /// constructor (best-effort: a blank definition is skipped). The
    /// expression normalization (paren-stripping) is shared with the
    /// PhysicalSchema projection through `ComputedColumnConfig` → `encodeComputed`.
    let private attachComputed
        (computed: Map<string * string * string, string * bool>)
        (k: Kind)
        : Kind =
        if Map.isEmpty computed then k
        else
            let attrs =
                k.Attributes
                |> List.map (fun a ->
                    let schemaStr, tableStr = TableId.qualifiedParts k.Physical
                    let coord = (schemaStr, tableStr, ColumnRealization.columnNameText a.Column)
                    match Map.tryFind coord computed with
                    | Some (definition, isPersisted) ->
                        match ComputedColumnConfig.create definition isPersisted with
                        | Ok cc -> { a with Computed = Some cc }
                        | Error _ -> a
                    | None -> a)
            { k with Attributes = attrs }

    /// Wave-1 slice 1.3 — attach recovered triggers + CHECK constraints +
    /// extended properties to a Kind. Uses the Core smart constructors
    /// (`Trigger.create` / `ColumnCheck.create` / `ExtendedProperty.create`)
    /// so the reconstructed IR carries the same invariants as the forward
    /// path; a constructor failure (e.g. blank definition) is skipped
    /// (best-effort recovery — a malformed deployed object should not abort
    /// the whole readback). `attrEpByCol` maps a column name to its recovered
    /// extended properties. SsKeys synthesized from the deployed coordinates.
    let private attachAnnotations
        (triggers: Map<string * string, (string * bool * string) list>)
        (checks: Map<string * string, (string * string * bool) list>)
        (extProps: Map<string * string * string option, (string * string) list>)
        (k: Kind)
        : Kind =
        let schema, table = TableId.qualifiedParts k.Physical
        let recoveredTriggers =
            Map.tryFind (schema, table) triggers
            |> Option.defaultValue []
            |> List.choose (fun (name, disabled, body) ->
                match SsKey.synthesized "READSIDE_TRIGGER" (sprintf "%s.%s.%s" schema table name),
                      Name.create name with
                | Ok sk, Ok nm ->
                    match Trigger.create sk nm disabled body with
                    | Ok t -> Some t
                    | Error _ -> None
                | _ -> None)
        let recoveredChecks =
            Map.tryFind (schema, table) checks
            |> Option.defaultValue []
            |> List.choose (fun (name, definition, notTrusted) ->
                match SsKey.synthesized "READSIDE_CHECK" (sprintf "%s.%s.%s" schema table name) with
                | Ok sk ->
                    let nm = match Name.create name with | Ok n -> Some n | Error _ -> None
                    match ColumnCheck.create sk nm definition notTrusted with
                    | Ok c -> Some c
                    | Error _ -> None
                | Error _ -> None)
        let mkEps (col: string option) : ExtendedProperty list =
            Map.tryFind (schema, table, col) extProps
            |> Option.defaultValue []
            |> List.choose (fun (name, value) ->
                match ExtendedProperty.create name (Some value) with
                | Ok ep -> Some ep
                | Error _ -> None)
        let attrs =
            k.Attributes
            |> List.map (fun a ->
                { a with ExtendedProperties = a.ExtendedProperties @ mkEps (Some (ColumnRealization.columnNameText a.Column)) })
        { k with
            Triggers = k.Triggers @ recoveredTriggers
            ColumnChecks = k.ColumnChecks @ recoveredChecks
            ExtendedProperties = k.ExtendedProperties @ mkEps None
            Attributes = attrs }

    /// 6.A.5 — attach recovered non-PK indexes to a Kind. Resolves each
    /// index key column to the kind's `Attribute.SsKey` by column name (the
    /// same coordinate `attachDefaults` keys on); an index whose key columns
    /// don't all resolve is skipped (best-effort, mirroring the other attach
    /// helpers). `IsUnique` survives the round-trip so a deployed UNIQUE
    /// index reads back as `Index.IsUnique = true` instead of vanishing on
    /// the hardcoded `Indexes = []`. SsKey synthesized from the deployed
    /// coordinates (`READSIDE_IDX`).
    let private attachIndexes
        (indexes: Map<string * string, IndexColumnRow list>)
        (k: Kind)
        : Kind =
        match Map.tryFind (TableId.qualifiedParts k.Physical) indexes with
        | None -> k
        | Some rows ->
            let ssKeyByColumn =
                k.Attributes |> List.map (fun a -> ColumnRealization.columnNameText a.Column, a.SsKey) |> Map.ofList
            let recovered =
                rows
                |> List.groupBy (fun r -> r.IndexName)
                |> List.choose (fun (indexName, cols) ->
                    let keyColumns =
                        cols
                        |> List.choose (fun r ->
                            Map.tryFind r.ColumnName ssKeyByColumn
                            |> Option.map (fun sk ->
                                IndexColumn.create sk (if r.IsDescending then Descending else Ascending)))
                    // Skip an index whose key columns don't all resolve (a
                    // computed-column or partition key the attribute set
                    // doesn't carry) rather than emit a partial index.
                    if List.length keyColumns <> List.length cols then None
                    else
                        match SsKey.synthesized "READSIDE_IDX" (sprintf "%s.%s.%s" (TableId.schemaText k.Physical) (TableId.tableText k.Physical) indexName),
                              Name.create indexName with
                        | Ok sk, Ok nm ->
                            // ReadSide query at readIndexes excludes PKs (`is_primary_key = 0`),
                            // so PrimaryKey is unreachable here; project IsUnique via
                            // ofLegacyBooleans with isPK = false to reach the typed surface.
                            let isU = cols |> List.exists (fun r -> r.IsUnique)
                            Some { Index.create sk nm keyColumns with Uniqueness = IndexUniqueness.ofLegacyBooleans isU false }
                        | _ -> None)
            { k with Indexes = recovered }

    /// Wave-1 slice 1.3 — reconstruct catalog-level `Sequence` values from
    /// the `sys.sequences` rows via the `Sequence.create` smart constructor.
    let private buildSequences (rows: SequenceRow list) : Sequence list =
        rows
        |> List.choose (fun r ->
            let cacheMode =
                if not r.IsCached then NoCache
                elif Option.isSome r.CacheSize then Cache
                else Unspecified
            match SsKey.synthesized "READSIDE_SEQUENCE" (sprintf "%s.%s" r.Schema r.Name),
                  Name.create r.Name with
            | Ok sk, Ok nm ->
                match Sequence.create sk nm r.Schema r.DataType r.StartValue r.Increment r.MinimumValue r.MaximumValue r.IsCycling cacheMode r.CacheSize with
                | Ok s -> Some s
                | Error _ -> None
            | _ -> None)

    /// Combined-query variant of the five schema-readback queries
    /// (`readColumnRows` + `readPrimaryKeys` + `readIdentityColumns`
    /// + `readForeignKeys` + `readLogicalNameProperties`). Sends ONE
    /// `SqlCommand` containing five SQL batches separated by `;`,
    /// then walks the five result sets via `NextResultAsync`.
    /// **Perf-implications (pillar 7):** eliminates 4 of the 5
    /// round-trips per `read` call — per-canary-readback ~150-300ms
    /// shaved on the warm container (chapter-3.6 perf-aware close-out
    /// optimization; slice D.1.b extends the same single-batch
    /// envelope).
    ///
    /// Big-O: same as the prior sum (one query per projection); the
    /// win is round-trip reduction, not asymptotic.
    ///
    /// **Slice D.1.b — 5th batch (`V2.LogicalName` extended-property
    /// recovery).** Joins `sys.extended_properties` with `sys.tables`
    /// (table-level when `minor_id = 0`) and `sys.columns` (column-
    /// level when `minor_id > 0`) for the property named
    /// `V2.LogicalName`. Two result tuples emerge:
    ///   - `(schema, table, value)` per table-level row (column = NULL)
    ///   - `(schema, table, column, value)` per column-level row
    /// Returned as two maps so `buildKind` / `buildAttribute` can
    /// hydrate `Kind.Name` / `Attribute.Name` from the property
    /// value when present (backward-compat fallback to the deployed
    /// name when absent — pre-D.1.b deployed schemas + non-V2-
    /// emitted schemas continue to round-trip via the deployed name).
    let private readSchemaCombined (cnn: SqlConnection)
        : Task<list<ColumnRow>
              * Set<string * string * string>
              * Set<string * string * string>
              * list<FkRow>
              * Map<string * string, string>
              * Map<string * string * string, string>
              * Map<string * string, string>> =
        task {
            use _ = Bench.scope "readside.readSchemaCombined"
            use cmd = cnn.CreateCommand()
            cmd.CommandText <-
                // Five batches separated by `;`. Order matters — the
                // `NextResultAsync` walk below depends on it.
                //   1. columns          (INFORMATION_SCHEMA.COLUMNS)
                //   2. primary keys     (INFORMATION_SCHEMA.TABLE_CONSTRAINTS join)
                //   3. identity cols    (sys.columns)
                //   4. foreign keys     (sys.foreign_keys join)
                //   5. logical-name xps (sys.extended_properties; slice D.1.b)
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
                    SCHEMA_NAME(rt.schema_id), rt.name, rc.name, \
                    fk.is_not_trusted \
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
                 ORDER BY SCHEMA_NAME(t.schema_id), t.name, c.column_id; \
                 SELECT \
                    SCHEMA_NAME(t.schema_id), t.name, c.name, \
                    CAST(ep.value AS NVARCHAR(MAX)) \
                 FROM sys.extended_properties ep \
                 JOIN sys.tables t ON t.object_id = ep.major_id \
                 LEFT JOIN sys.columns c \
                   ON c.object_id = ep.major_id AND c.column_id = ep.minor_id \
                 WHERE ep.class = 1 \
                   AND ep.name = N'V2.LogicalName' \
                   AND t.is_ms_shipped = 0; \
                 SELECT \
                    SCHEMA_NAME(t.schema_id), t.name, \
                    CAST(ep.value AS NVARCHAR(MAX)) \
                 FROM sys.extended_properties ep \
                 JOIN sys.tables t ON t.object_id = ep.major_id \
                 WHERE ep.class = 1 \
                   AND ep.name = N'V2.SsKey' \
                   AND ep.minor_id = 0 \
                   AND t.is_ms_shipped = 0"
            use! reader = cmd.ExecuteReaderAsync()

            // Result set 1: column rows (matches readColumnRows shape).
            let columnRows = ResizeArray<ColumnRow>()
            let optInt = optIntOf reader
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
            let fkRows = ResizeArray<FkRow>()
            let mutable hasMore4 = true
            // E2 (debrief G4): classify each FK row's coordinates; a NULL
            // SCHEMA_NAME() yields a NAMED diagnostic + skip instead of an
            // opaque `GetString` cast failure that would abort the readback.
            let rawOpt4 i = if reader.IsDBNull i then None else Some (reader.GetString i)
            while hasMore4 do
                let! more = reader.ReadAsync()
                if more then
                    let isNotTrusted = (not (reader.IsDBNull 6)) && reader.GetBoolean 6
                    match
                        ForeignKeyReadback.classify
                            (rawOpt4 0) (rawOpt4 1) (rawOpt4 2) (rawOpt4 3) (rawOpt4 4) (rawOpt4 5) isNotTrusted
                        with
                    | ForeignKeyReadback.Reconstructable c ->
                        fkRows.Add(
                            { SourceSchema = c.SourceSchema
                              SourceTable  = c.SourceTable
                              SourceColumn = c.SourceColumn
                              TargetSchema = c.TargetSchema
                              TargetTable  = c.TargetTable
                              TargetColumn = c.TargetColumn
                              IsNotTrusted = c.IsNotTrusted })
                    | ForeignKeyReadback.Unreadable reason -> eprintfn "%s" reason
                else hasMore4 <- false

            // Result set 5: V2.LogicalName extended properties
            // (slice D.1.b). Column 2 (sys.columns.name) is NULL for
            // table-level entries (LEFT JOIN); when present, the row
            // is column-level. Two maps emerge for the consumers.
            let! _ = reader.NextResultAsync()
            let tableLogical = System.Collections.Generic.Dictionary<string * string, string>()
            let columnLogical = System.Collections.Generic.Dictionary<string * string * string, string>()
            let mutable hasMore5 = true
            while hasMore5 do
                let! more = reader.ReadAsync()
                if more then
                    let schema = reader.GetString 0
                    let table = reader.GetString 1
                    let value = reader.GetString 3
                    if reader.IsDBNull 2 then
                        tableLogical[(schema, table)] <- value
                    else
                        let column = reader.GetString 2
                        columnLogical[(schema, table, column)] <- value
                else hasMore5 <- false

            // Result set 6: V2.SsKey extended properties (Wave 4.1).
            // Table-level only (minor_id = 0); the serialized identity
            // recovered here lets buildKind deserialize the original SsKey
            // instead of synthesizing READSIDE_KIND (A1: identity survives
            // rename).
            let! _ = reader.NextResultAsync()
            let tableSsKeys = System.Collections.Generic.Dictionary<string * string, string>()
            let mutable hasMore6 = true
            while hasMore6 do
                let! more = reader.ReadAsync()
                if more then
                    tableSsKeys[(reader.GetString 0, reader.GetString 1)] <- reader.GetString 2
                else hasMore6 <- false

            return
                List.ofSeq columnRows,
                Set.ofSeq primaryKeySet,
                Set.ofSeq identitySet,
                List.ofSeq fkRows,
                tableLogical |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq,
                columnLogical |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq,
                tableSsKeys |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq
        }

    /// Wave-3 slice 3.1 — names every user table the deployed database is
    /// tracking with Change Data Capture (`sys.tables.is_tracked_by_cdc = 1`).
    /// Lives in the SQL adapter (the read-side's domain is deployed-schema
    /// metadata); the Transfer pre-flight (`Projection.Pipeline.Transfer`)
    /// consults it to refuse an `Execute` write against a CDC-tracked sink.
    /// Returns `[schema].[table]`-style names, ordered.
    let cdcTrackedTables (cnn: SqlConnection) : Task<string list> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <-
                "SELECT SCHEMA_NAME(t.schema_id) + '.' + t.name \
                 FROM sys.tables t \
                 WHERE t.is_tracked_by_cdc = 1 AND t.is_ms_shipped = 0 \
                 ORDER BY 1"
            use! reader = cmd.ExecuteReaderAsync()
            let names = ResizeArray<string>()
            let mutable more = true
            while more do
                let! has = reader.ReadAsync()
                if has then names.Add(reader.GetString 0) else more <- false
            return List.ofSeq names
        }

    /// W1-A seam T1 — the production CDC capture-count reader the
    /// "Measure" leg of the change-over-time proteins (X1/X4/X5/X8)
    /// needs (the change-measure `‖·‖`; physically the CDC capture
    /// count per `WAVE_6_ALGEBRA.md`). Sums `cdc.[<schema>_<table>_CT]`
    /// row counts across the tracked tables.
    ///
    /// `tracked` is the discovery shape `cdcTrackedTables` returns —
    /// `schema.table` names (`SCHEMA_NAME(...) + '.' + name`). The CT
    /// capture-table name is derived exactly as SQL Server names it:
    /// `cdc.<schema>_<table>_CT`. Additive only; `cdcTrackedTables` is
    /// unchanged. The no-CDC case (empty `tracked`) returns 0 by the
    /// empty fold. The caller controls scope by passing the table list,
    /// so a sum over a subset (one kind's "Measure" leg) is expressible.
    let cdcCaptureCount (cnn: SqlConnection) (tracked: string list) : Task<int> =
        task {
            let mutable total = 0
            for name in tracked do
                // `cdcTrackedTables` returns unbracketed `schema.table`;
                // split on the first '.' so a table whose name carries a
                // '.' (unusual but legal) still resolves its schema.
                let schema, table =
                    match name.IndexOf '.' with
                    | i when i >= 0 -> name.Substring(0, i), name.Substring(i + 1)
                    | _ -> "dbo", name
                let captureTable =
                    System.String.Concat("cdc.[", schema, "_", table, "_CT]")  // LINT-ALLOW: terminal SQL-text-emission boundary; CT-table name mirrors SQL Server's cdc.<schema>_<table>_CT naming
                use cmd = cnn.CreateCommand()
                cmd.CommandText <-
                    System.String.Concat("SELECT COUNT(*) FROM ", captureTable, ";")  // LINT-ALLOW: terminal SQL-text-emission boundary; captureTable is the CDC capture relation name
                let! countObj = cmd.ExecuteScalarAsync()
                total <- total + System.Convert.ToInt32 countObj
            return total
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
                let! columnRows, primaryKeySet, identitySet, fkRows, tableLogicalNames, columnLogicalNames, tableSsKeys =
                    readSchemaCombined cnn
                let kindResults =
                    columnRows
                    |> List.groupBy (fun row -> row.Schema, row.Table)
                    |> Bench.iterMap "readside.kindGroup" (fun ((schema, table), rows) ->
                        buildKind tableLogicalNames columnLogicalNames tableSsKeys schema table rows primaryKeySet identitySet)
                match Result.aggregate kindResults with
                | Error errors -> return Result.failure errors
                | Ok kinds ->
                    let fkGroups =
                        fkRows
                        |> List.groupBy (fun fk -> fk.SourceSchema, fk.SourceTable)
                        |> Map.ofList
                    let kindsWithRefsResults =
                        kinds
                        |> Bench.iterMap "readside.attachReferences" (attachReferences tableSsKeys fkGroups)
                    match Result.aggregate kindsWithRefsResults with
                    | Error errors -> return Result.failure errors
                    | Ok kindsWithRefs0 ->
                        // Wave-1 slice 1.2 — recover DEFAULT constraints
                        // (one extra round-trip) and attach them so the
                        // canary's PhysicalSchema.Default axis is no longer
                        // blind to a dropped/changed DEFAULT clause.
                        let! defaults = readDefaultConstraints cnn
                        // Wave-1 slice 1.3 (L3-S7 real-SQL leg) — recover
                        // computed-column configs from sys.computed_columns.
                        let! computed = readComputedColumns cnn
                        // Wave-1 slice 1.3 — recover the four table/catalog-
                        // scoped annotation features (triggers / checks /
                        // sequences / extended properties) so the canary's
                        // Annotations axis is no longer blind to them.
                        let! triggers = readTriggers cnn
                        let! checks = readCheckConstraints cnn
                        let! extProps = readExtendedProperties cnn
                        // 6.A.5 — recover non-PK indexes (unique + ordinary)
                        // so `Kind.Indexes` is no longer hardcoded `[]`; a
                        // deployed UNIQUE index survives the round-trip.
                        let! indexes = readIndexes cnn
                        let! sequenceRows = readSequences cnn
                        let recoveredSequences = buildSequences sequenceRows
                        let kindsWithRefs =
                            kindsWithRefs0
                            |> List.map (attachDefaults defaults)
                            |> List.map (attachComputed computed)
                            |> List.map (attachAnnotations triggers checks extProps)
                            |> List.map (attachIndexes indexes)
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
                                                        SsKey    = mKey
                                                        Name     = mName
                                                        Kinds    = kindsWithRows
                                                        // Chapter A.0' slice β —
                                                        // see buildAttribute rationale;
                                                        // the deployed-schema readback
                                                        // has no `Is_Active` axis.
                                                        IsActive = true
                                                        // Chapter A.0' slice ζ —
                                                        // see buildAttribute rationale.
                                                        ExtendedProperties = []
                                                    }
                                                ]
                                            // Wave-1 slice 1.3 — sequences
                                            // recovered from sys.sequences via
                                            // readSequences + buildSequences.
                                            Sequences = recoveredSequences
                                        }
            with
            | ex ->
                return
                    Result.failureOf (
                        ValidationError.create
                            "readside.query.failed"
                            (sprintf "INFORMATION_SCHEMA query failed: %s" ex.Message))
        }
