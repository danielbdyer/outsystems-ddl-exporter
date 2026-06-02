namespace Projection.Targets.SSDT

// LINT-ALLOW-FILE-MUTATION: Microsoft.SqlServer.TransactSql.ScriptDom
//   builds typed `TSqlFragment` instances by mutating `.Members` /
//   `.Definitions` collections (BCL-mandated mutable-property
//   surface). Each `build*` function constructs a fresh fragment;
//   mutation is local to the builder and the resulting fragment is
//   handed to `Sql160ScriptGenerator.GenerateScript` immutably
//   thereafter. Per audit Lens-2 Tier-2 (justified — typed-AST
//   library forces the mutation shape); same allowed-exception
//   class as `JsonEmitter`'s `JsonWriterOptions` setters.
//
// **Postdoctoral discipline.** Every builder function is a *pure*
// mapping from V2's algebraic `Statement` DU to the corresponding
// `TSqlFragment` subtype. The output is a typed AST whose grammar
// is enforced by ScriptDom; `Sql160ScriptGenerator` (with pinned
// `SqlScriptGeneratorOptions`) emits byte-deterministic T-SQL
// text. The parse-roundtrip property `parse(emit(fragment)) ≡
// fragment-shape` is verified by
// `tests/Projection.Tests/ScriptDomRoundTripTests.fs`.

open Microsoft.SqlServer.TransactSql.ScriptDom
open Projection.Core

/// Typed-AST builders mapping V2's `Statement` DU to ScriptDom's
/// `TSqlFragment` hierarchy. The mapping is total over the DU's
/// SQL-bearing variants (`CreateTable`, `InsertRow`,
/// `SetIdentityInsert`, `CreateTrigger`, `CreateSequence`, etc.);
/// non-SQL variants (`Comment`, `Blank`) are realization-layer
/// concerns — the splice into the emitted text-stream lives in
/// `ScriptDomGenerate.toText`.
///
/// Every builder is pure (V2's contract): same input → same
/// `TSqlFragment` shape, byte-for-byte. ScriptDom's
/// `Sql160ScriptGenerator` is the byte-deterministic emitter; per
/// `DECISIONS 2026-05-09 — Built-in obligation`, we delegate to
/// the canonical typed builder rather than hand-rolling SQL text.
[<RequireQualifiedAccess>]
module ScriptDomBuild =

    // -----------------------------------------------------------------------
    // Per-thread TSql160Parser cache — slice A.4.7'-prelude.perf-sweep-3
    // (`PERF_OPPORTUNITIES.md` Ranks C1-C3). `TSql160Parser` carries
    // tens-of-KB of internal state per allocation; three call sites
    // (`parseComputedExpression`, `checkConstraint`,
    // `tryParseFilterWithDiagnostics`) previously allocated a fresh
    // parser per call. At production scale (300 tables × per-table
    // CHECK constraints + computed columns + filtered indexes) this
    // compounded to thousands of parser allocations per pipeline pass.
    // ScriptDom's `TSql160Parser` is documented as **not thread-safe**;
    // `System.Threading.ThreadLocal<T>` gives one parser per thread,
    // lazily initialized on first access, reused for every subsequent
    // parse on that thread.
    let private threadLocalParser =
        new System.Threading.ThreadLocal<TSql160Parser>(
            fun () -> TSql160Parser(initialQuotedIdentifiers = false))

    // -----------------------------------------------------------------------
    // Identifier / type helpers — typed wrappers over ScriptDom's mutable
    // Identifier / TypeReference primitives.
    // -----------------------------------------------------------------------

    /// A bracketed identifier (`[name]`). ScriptDom's `Identifier` carries
    /// `Value` (raw name) and `QuoteType` (NotQuoted / SquareBracket /
    /// DoubleQuote); we pin `SquareBracket` to match SSDT convention.
    let private bracketed (name: string) : Identifier =
        let id = Identifier()
        id.Value <- name
        id.QuoteType <- QuoteType.SquareBracket
        id

    /// `[schema].[name]` schema-qualified identifier list. ScriptDom's
    /// `MultiPartIdentifier` carries an ordered `Identifiers` list; we
    /// build it from the typed `TableId`.
    let private schemaObjectName (schema: string) (table: string) : SchemaObjectName =
        let n = SchemaObjectName()
        n.Identifiers.Add(bracketed schema)
        n.Identifiers.Add(bracketed table)
        n

    /// `[schema].[name]` for a `TableId` value (or
    /// `[catalog].[schema].[name]` when the `TableId` carries an
    /// explicit catalog coordinate). Goes through `TableId.qualified`
    /// semantics (the canonical bracket-quoting) but produces the typed
    /// AST form.
    ///
    /// **Slice 4.3 — cross-DB three-part name (L3-S10 / L3-I10).** When
    /// `TableId.Catalog = Some db`, ScriptDom's `MultiPartIdentifier`
    /// carries a third leading `Identifier` so the rendered name is the
    /// three-part `[db].[schema].[table]`. Pre-slice the catalog axis
    /// silently degraded to the two-part implicit-current-database form
    /// (`schemaObjectFromTableId` dropped `.Catalog`). Additive: a
    /// `Catalog = None` `TableId` (today's universal case) emits the
    /// byte-identical two-part name.
    let private schemaObjectFromTableId (t: TableId) : SchemaObjectName =
        match t.Catalog with
        | Some catalog ->
            let n = SchemaObjectName()
            n.Identifiers.Add(bracketed catalog)
            n.Identifiers.Add(bracketed (TableId.schemaText t))
            n.Identifiers.Add(bracketed (TableId.tableText t))
            n
        | None ->
            schemaObjectName (TableId.schemaText t) (TableId.tableText t)

    /// Map V2's `PrimitiveType` to ScriptDom's `SqlDataTypeOption`.
    /// Closed-DU dispatch — adding a new variant lights up an
    /// exhaustiveness error here only.
    let private sqlDataTypeOption (t: PrimitiveType) : SqlDataTypeOption =
        match t with
        | Integer  -> SqlDataTypeOption.Int
        | Decimal  -> SqlDataTypeOption.Decimal
        | Text     -> SqlDataTypeOption.NVarChar
        | Boolean  -> SqlDataTypeOption.Bit
        | DateTime -> SqlDataTypeOption.DateTime2
        | Date     -> SqlDataTypeOption.Date
        | Time     -> SqlDataTypeOption.Time
        | Binary   -> SqlDataTypeOption.VarBinary
        | Guid     -> SqlDataTypeOption.UniqueIdentifier

    /// Build a `SqlDataTypeReference` carrying the type expression
    /// (length / precision / scale parameters per V2's defaults).
    /// `NVARCHAR(N)` / `NVARCHAR(MAX)`; `VARBINARY(N)` / `VARBINARY(MAX)`;
    /// `DECIMAL(P, S)` / `DECIMAL(P, 0)` / `DECIMAL(18, 4)`; fixed
    /// types pass through.
    ///
    /// Public surface (chapter-3.7 slice β'): `Render.columnSqlType`
    /// delegates here + through `ScriptDomGenerate.generateDataType`
    /// so the SQL DDL type expression has exactly one source of
    /// truth — the typed AST built here, rendered by ScriptDom's
    /// `Sql160ScriptGenerator`. Per pillar 7 (gold-standard library
    /// precedence), the use-case-specific library IS the emission
    /// path; previously `Render.columnSqlType` composed type strings
    /// via `String.Concat` (`sqlTypeWithLength`, `sqlDecimal`),
    /// which the chapter-3.7 audit found in violation of pillar 1
    /// (data-structure-oriented).
    let dataTypeReference
        (typ: PrimitiveType)
        (length: int option)
        (precision: int option)
        (scale: int option)
        : DataTypeReference =
        let r = SqlDataTypeReference()
        r.SqlDataTypeOption <- sqlDataTypeOption typ
        r.Name <- SchemaObjectName()
        r.Name.Identifiers.Add(bracketed (string r.SqlDataTypeOption))
        match typ with
        | Text | Binary ->
            match length with
            | Some n when n > 0 ->
                let lit = IntegerLiteral()
                lit.Value <- string n
                r.Parameters.Add(lit)
            | _ ->
                let mx = MaxLiteral()
                mx.Value <- "MAX"
                r.Parameters.Add(mx)
        | Decimal ->
            let p =
                match precision with
                | Some v when v > 0 -> v
                | _ -> 18
            let s =
                match precision, scale with
                | Some _, Some v -> v
                | Some _, None   -> 0
                | None, _        -> 4
            let pLit = IntegerLiteral()
            pLit.Value <- string p
            r.Parameters.Add(pLit)
            let sLit = IntegerLiteral()
            sLit.Value <- string s
            r.Parameters.Add(sLit)
        | _ -> ()
        r :> DataTypeReference

    /// Map a concrete `SqlStorageType` to its ScriptDom
    /// `SqlDataTypeOption`. Closed-DU dispatch — adding a storage
    /// variant lights up an exhaustiveness error here. `Xml` is absent
    /// (it has no `SqlDataTypeOption`; `dataTypeReferenceFromStorage`
    /// builds an `XmlDataTypeReference` instead).
    let private storageDataTypeOption (st: SqlStorageType) : SqlDataTypeOption =
        match st with
        | SqlStorageType.BigInt           -> SqlDataTypeOption.BigInt
        | SqlStorageType.Int              -> SqlDataTypeOption.Int
        | SqlStorageType.SmallInt         -> SqlDataTypeOption.SmallInt
        | SqlStorageType.TinyInt          -> SqlDataTypeOption.TinyInt
        | SqlStorageType.Bit              -> SqlDataTypeOption.Bit
        | SqlStorageType.Decimal _        -> SqlDataTypeOption.Decimal
        | SqlStorageType.Numeric _        -> SqlDataTypeOption.Numeric
        | SqlStorageType.Money            -> SqlDataTypeOption.Money
        | SqlStorageType.SmallMoney       -> SqlDataTypeOption.SmallMoney
        | SqlStorageType.Float            -> SqlDataTypeOption.Float
        | SqlStorageType.Real             -> SqlDataTypeOption.Real
        | SqlStorageType.NVarChar _       -> SqlDataTypeOption.NVarChar
        | SqlStorageType.VarChar _        -> SqlDataTypeOption.VarChar
        | SqlStorageType.NChar _          -> SqlDataTypeOption.NChar
        | SqlStorageType.Char _           -> SqlDataTypeOption.Char
        | SqlStorageType.NText            -> SqlDataTypeOption.NText
        | SqlStorageType.Text             -> SqlDataTypeOption.Text
        | SqlStorageType.DateTime         -> SqlDataTypeOption.DateTime
        | SqlStorageType.DateTime2 _      -> SqlDataTypeOption.DateTime2
        | SqlStorageType.DateTimeOffset _ -> SqlDataTypeOption.DateTimeOffset
        | SqlStorageType.SmallDateTime    -> SqlDataTypeOption.SmallDateTime
        | SqlStorageType.Date             -> SqlDataTypeOption.Date
        | SqlStorageType.Time _           -> SqlDataTypeOption.Time
        | SqlStorageType.VarBinary _      -> SqlDataTypeOption.VarBinary
        | SqlStorageType.Binary _         -> SqlDataTypeOption.Binary
        | SqlStorageType.Image            -> SqlDataTypeOption.Image
        | SqlStorageType.UniqueIdentifier -> SqlDataTypeOption.UniqueIdentifier
        | SqlStorageType.Xml              -> SqlDataTypeOption.None  // unreachable; Xml builds XmlDataTypeReference before this call

    /// Build a `DataTypeReference` from a concrete `SqlStorageType` —
    /// the evidence-bearing emission path. Preferred over
    /// `dataTypeReference` (the `PrimitiveType` fallback) whenever the
    /// source named the concrete type. `XML` builds an
    /// `XmlDataTypeReference`; everything else builds a
    /// `SqlDataTypeReference` carrying length / precision / scale
    /// parameters faithful to the storage type (`NVARCHAR(MAX)` vs
    /// `NVARCHAR(100)`; `DECIMAL(18,2)`; `DATETIME2(7)` vs bare
    /// `DATETIME2`).
    let dataTypeReferenceFromStorage (st: SqlStorageType) : DataTypeReference =
        match st with
        | SqlStorageType.Xml ->
            // ScriptDom models XML separately (no SqlDataTypeOption).
            XmlDataTypeReference() :> DataTypeReference
        | _ ->
            let r = SqlDataTypeReference()
            r.SqlDataTypeOption <- storageDataTypeOption st
            r.Name <- SchemaObjectName()
            r.Name.Identifiers.Add(bracketed (string r.SqlDataTypeOption))
            let addInt (n: int) =
                let lit = IntegerLiteral()
                lit.Value <- string n
                r.Parameters.Add(lit)
            let addLength (len: SqlLength) =
                match len with
                | Bounded n -> addInt n
                | Max ->
                    let mx = MaxLiteral()
                    mx.Value <- "MAX"
                    r.Parameters.Add(mx)
            let addScale (scale: int option) =
                match scale with
                | Some n -> addInt n
                | None   -> ()
            match st with
            | SqlStorageType.NVarChar len
            | SqlStorageType.VarChar len
            | SqlStorageType.VarBinary len -> addLength len
            | SqlStorageType.NChar n
            | SqlStorageType.Char n
            | SqlStorageType.Binary n -> addInt n
            | SqlStorageType.Decimal (p, s)
            | SqlStorageType.Numeric (p, s) -> addInt p; addInt s
            | SqlStorageType.DateTime2 scale
            | SqlStorageType.DateTimeOffset scale
            | SqlStorageType.Time scale -> addScale scale
            | _ -> ()
            r :> DataTypeReference

    /// Map a typed `SqlLiteral` value to a ScriptDom `ScalarExpression`
    /// (specifically a `Literal` subclass projected to its supertype
    /// for use in `RowValue.ColumnValues` + DEFAULT clauses). Per the
    /// Tier-1 #4 transition (RawTextEmitter retirement arc): the
    /// IR→typed-literal projection lives in
    /// `Projection.Core.SqlLiteral`; this is the SSDT-resident
    /// `SqlLiteral` → ScriptDom-`Literal` mapping. Used by
    /// `cellExpression` (single-row INSERT VALUES), `buildMergeStatement`
    /// (MERGE InlineDerivedTable), `buildUpdateStatement` (UPDATE SET +
    /// change-detection WHERE), and `columnDefinition` (DEFAULT
    /// constraint emission per slice 5.13.column-features-emit).
    let buildSqlLiteral (lit: SqlLiteral) : ScalarExpression =
        match lit with
        | NullLit ->
            NullLiteral() :> ScalarExpression
        | IntegerLit s ->
            let l = IntegerLiteral()
            l.Value <- s
            l :> ScalarExpression
        | DecimalLit s ->
            let l = NumericLiteral()
            l.Value <- s
            l :> ScalarExpression
        | BooleanLit b ->
            let l = IntegerLiteral()
            l.Value <- if b then "1" else "0"
            l :> ScalarExpression
        | TemporalLit raw ->
            let l = StringLiteral()
            l.Value <- raw
            l.IsNational <- false
            l :> ScalarExpression
        | GuidLit raw ->
            let l = StringLiteral()
            l.Value <- raw
            l.IsNational <- false
            l :> ScalarExpression
        | TextLit raw ->
            let l = StringLiteral()
            l.Value <- raw
            l.IsNational <- true
            l :> ScalarExpression
        | BinaryLit prefixed ->
            let l = BinaryLiteral()
            // ScriptDom's `BinaryLiteral.Value` carries the value
            // pre-prefixed; `SqlLiteral.BinaryLit` already has the
            // `0x` prefix per `RawValueCodec.withHexPrefix`. The
            // writer emits the value verbatim.
            l.Value <- prefixed
            l :> ScalarExpression

    // -----------------------------------------------------------------------
    // Column definitions inside CREATE TABLE.
    // -----------------------------------------------------------------------

    /// Build a `ColumnDefinition` for a V2 `ColumnDef`. Includes
    /// nullability constraint, IDENTITY(1,1) clause when applicable.
    /// PRIMARY KEY constraint is emitted at the table level (not per
    /// column) — see `buildPrimaryKey` below. Same convention as
    /// `Render.fs`.
    /// Parse a computed-column expression via TSql160Parser. Slice
    /// 5.3.α.column-axis-deferral-closeout (LR4). V1 source:
    /// `CreateTableStatementBuilder.cs:391-409` (ParseExpression).
    /// Parse failure falls back to a `StringLiteral` wrapping the raw
    /// text — preserves emission surface even when the parser can't
    /// produce a typed AST (real V1-source expressions parse cleanly).
    let private parseComputedExpression (expr: string) : ScalarExpression =
        use reader = new System.IO.StringReader(expr)
        let fragment, _errors = threadLocalParser.Value.ParseExpression(reader)
        match Option.ofObj fragment with
        | Some e -> e
        | None ->
            let str = StringLiteral()
            str.Value <- expr
            str :> ScalarExpression

    let private columnDefinition (c: ColumnDef) : ColumnDefinition =
        use _ = Bench.scope "emit.scriptDom.build.columnDefinition"
        let col = ColumnDefinition()
        col.ColumnIdentifier <- bracketed c.Name
        match c.Computed with
        | Some config ->
            // LR4 cash-out — computed column. V1 shape (CreateTable
            // StatementBuilder.cs:296,299-302,304-311,362-365):
            // DataType = null; no NullableConstraintDefinition;
            // no IdentityOptions; ComputedColumnExpression set on
            // the ColumnDefinition. The PERSISTED flag is V1-deferred
            // (V1 doesn't emit persistence; V2's ComputedColumnConfig
            // .IsPersisted is carriage-only until ScriptDom round-trip
            // demands it on emit).
            col.ComputedColumnExpression <- parseComputedExpression config.Expression
            if config.IsPersisted then
                col.IsPersisted <- true
        | None ->
            // Prefer the concrete `SqlStorageType` evidence (faithful
            // BIGINT / DATETIME / NVARCHAR(MAX)); fall back to the
            // `PrimitiveType` → `SqlDataTypeOption` mapping when the
            // source carried only the semantic category.
            col.DataType <-
                match c.SqlStorage with
                | Some storage -> dataTypeReferenceFromStorage storage
                | None         -> dataTypeReference c.Type c.Length c.Precision c.Scale
            // Nullability — ScriptDom NULL/NOT NULL constraint is a
            // `NullableConstraintDefinition` on `Constraints`.
            let nullCons = NullableConstraintDefinition()
            nullCons.Nullable <- c.Nullable
            col.Constraints.Add(nullCons)
            // IDENTITY(1,1) when applicable. ScriptDom carries
            // `IdentityOptions` directly on the column.
            if c.IsIdentity then
                let identity = IdentityOptions()
                let seedLit = IntegerLiteral()
                seedLit.Value <- "1"
                identity.IdentitySeed <- seedLit
                let incLit = IntegerLiteral()
                incLit.Value <- "1"
                identity.IdentityIncrement <- incLit
                col.IdentityOptions <- identity
            // DEFAULT clause (slice 5.13.column-features-emit): when the
            // column carries a typed `SqlLiteral` default, emit an
            // inline `[CONSTRAINT <name>] DEFAULT <literal>` clause.
            // ScriptDom carries this as a `DefaultConstraintDefinition`
            // on `Constraints`. The literal flows through `buildSqlLiteral`
            // — same typed-AST path the MERGE / UPDATE statements use,
            // so the rendered DEFAULT value is byte-identical to other
            // emission surfaces.
            match c.DefaultValue with
            | None -> ()
            | Some lit ->
                let defCons = DefaultConstraintDefinition()
                match c.DefaultName with
                | Some name when not (System.String.IsNullOrWhiteSpace name) ->
                    defCons.ConstraintIdentifier <- bracketed name
                | _ -> ()
                defCons.Expression <- buildSqlLiteral lit
                col.Constraints.Add(defCons)
            // LR3 cash-out — single-column PK inline. When the column
            // carries IsPrimaryKey AND the caller signals (via the
            // Statement.CreateTable shape) that this is a single-column
            // PK, attach a `UniqueConstraintDefinition { IsPrimaryKey =
            // true }` inline. The signal is the absence of a table-
            // level PrimaryKeyDef on the CreateTableStatement; see
            // `buildCreateTable` for the dispatch logic.
            // (Inline-PK constraint is added by `buildCreateTable` per
            //  the single-vs-multi-column shape; this site only carries
            //  the per-column material.)
        col

    /// Build a table-level CHECK constraint via TSql160Parser. Slice
    /// 5.13.column-features-emit (chapter A.0' slice ε emit closure).
    /// V1's `#ColumnCheckReality.Definition` carries the deployed
    /// expression text (e.g., `([Age] >= 0)`); we parse it via
    /// `TSql160Parser.ParseBooleanExpression` and embed the typed
    /// `BooleanExpression` into ScriptDom's `CheckConstraintDefinition`.
    /// Parse failure surfaces as a `Diagnostics<...>` Warning entry
    /// in a future slice; today the failure path produces a
    /// `BooleanParenthesisExpression` with the raw text as a
    /// `StringLiteral` (last-resort fallback so emission proceeds).
    let private checkConstraint (chk: ColumnCheckDef) : CheckConstraintDefinition =
        let cons = CheckConstraintDefinition()
        match chk.Name with
        | Some name when not (System.String.IsNullOrWhiteSpace name) ->
            cons.ConstraintIdentifier <- bracketed name
        | _ -> ()
        use reader = new System.IO.StringReader(chk.Definition)
        let fragment, _errors = threadLocalParser.Value.ParseBooleanExpression(reader)
        cons.CheckCondition <-
            match Option.ofObj fragment with
            | Some expr -> expr
            | None ->
                // Last-resort fallback: wrap raw text in a literal
                // (preserves the SQL surface even when the parser
                // can't produce a typed AST). Real production V1
                // expressions parse cleanly under TSql160Parser.
                let str = StringLiteral()
                str.Value <- chk.Definition
                let cmp = BooleanComparisonExpression()
                cmp.ComparisonType <- BooleanComparisonType.Equals
                cmp.FirstExpression <- str :> ScalarExpression
                cmp.SecondExpression <- str :> ScalarExpression
                cmp :> BooleanExpression
        // V1's `IsNotTrusted` reflects whether the deployed-target's
        // CHECK is currently NOCHECK'd (operator may have disabled it
        // for a bulk-load). ScriptDom doesn't model the NOCHECK state
        // inline in `CHECK constraint`; the WITH NOCHECK clause is
        // emitted at ALTER TABLE level. For inline emission we just
        // carry the constraint definition; preservation of NOCHECK
        // state is a future post-emit ALTER TABLE step (matrix row
        // 59 cash-out).
        cons

    /// Build the table-level PRIMARY KEY constraint (when present).
    /// ScriptDom carries this as a
    /// `UniqueConstraintDefinition { IsPrimaryKey = true }` in
    /// `CreateTableStatement.Definition.TableConstraints`.
    let private primaryKeyConstraint
        (pk: PrimaryKeyDef)
        : ConstraintDefinition =
        let cons = UniqueConstraintDefinition()
        cons.IsPrimaryKey <- true
        cons.ConstraintIdentifier <- bracketed pk.Name
        for col in pk.Columns do
            let order = ColumnWithSortOrder()
            let colRef = ColumnReferenceExpression()
            let mid = MultiPartIdentifier()
            mid.Identifiers.Add(bracketed col)
            colRef.MultiPartIdentifier <- mid
            order.Column <- colRef
            order.SortOrder <- SortOrder.NotSpecified
            cons.Columns.Add(order)
        cons :> ConstraintDefinition

    let private toDeleteUpdateAction (a: ReferenceActionSql) : DeleteUpdateAction =
        match a with
        | NoActionSql -> DeleteUpdateAction.NoAction
        | CascadeSql  -> DeleteUpdateAction.Cascade
        | SetNullSql  -> DeleteUpdateAction.SetNull

    /// Build a single-column foreign-key constraint per V2's
    /// `ForeignKeyDef`. Maps V2's `ReferenceActionSql` DU to
    /// ScriptDom's `DeleteUpdateAction`. Slice 5.13.fk-features-emit:
    /// emits the optional `UpdateAction` from `fk.OnUpdate`. When
    /// `None`, ScriptDom omits the ON UPDATE clause and SQL Server's
    /// server-default NO ACTION applies (preserves V1 emission shape).
    let private foreignKeyConstraint
        (fk: ForeignKeyDef)
        : ConstraintDefinition =
        let cons = ForeignKeyConstraintDefinition()
        cons.ConstraintIdentifier <- bracketed fk.Name
        cons.Columns.Add(bracketed fk.SourceColumn)
        cons.ReferenceTableName <- schemaObjectFromTableId fk.Target
        cons.ReferencedTableColumns.Add(bracketed fk.TargetColumn)
        cons.DeleteAction <- toDeleteUpdateAction fk.OnDelete
        match fk.OnUpdate with
        | Some action -> cons.UpdateAction <- toDeleteUpdateAction action
        | None        -> ()
        cons :> ConstraintDefinition

    /// Build a `CreateTableStatement` from V2's typed
    /// `(TableId, ColumnDef list, PrimaryKeyDef option, ForeignKeyDef list)`
    /// triple. Pure: same inputs → same fragment shape (verified by
    /// `tests/Projection.Tests/ScriptDomRoundTripTests.fs`).
    /// CREATE TABLE builder. Returns the typed `CreateTableStatement`
    /// paired with a Diagnostics stream. Chapter 4.9 slice ζ — the
    /// canonical signature carries `Diagnostics<_>` so future
    /// Diagnostics sources (column-default parse failures; check-
    /// constraint parse failures; per-column metadata anomalies) can
    /// surface through the same writer without re-introducing a
    /// sibling-wrapper. Today the entries list is empty by
    /// construction; callers explicitly drop via `.Value` at the call
    /// site (per the chapter 4.7 sibling-wrapper discipline + V2-no-
    /// back-compat).
    /// LR3 cash-out — V1's CreateTableStatementBuilder.cs:67-78 inlines
    /// single-column PKs at the column-constraint level instead of as
    /// a separate table-level constraint. The shape: when exactly one
    /// PK column exists, attach a `UniqueConstraintDefinition` to that
    /// column's `Constraints`; skip the table-level PK entry. Multi-
    /// column PKs continue to emit as table-level (V1 L80-98 shape).
    let private attachInlinePrimaryKey
        (colDefs: System.Collections.Generic.IList<ColumnDefinition>)
        (pk: PrimaryKeyDef)
        : unit =
        match pk.Columns with
        | [ pkColumnName ] ->
            // Find the matching ColumnDefinition by identifier value.
            let target =
                colDefs
                |> Seq.tryFind (fun cd ->
                    match Option.ofObj cd.ColumnIdentifier with
                    | Some ident ->
                        System.String.Equals(
                            ident.Value, pkColumnName,
                            System.StringComparison.OrdinalIgnoreCase)
                    | None -> false)
            match target with
            | Some cd ->
                let cons = UniqueConstraintDefinition()
                cons.IsPrimaryKey <- true
                cons.Clustered <- System.Nullable(true)
                cons.ConstraintIdentifier <- bracketed pk.Name
                cd.Constraints.Add(cons)
            | None -> ()
        | _ -> ()

    /// Map V2's `TemporalRetentionUnit` DU to ScriptDom's
    /// `TemporalRetentionPeriodUnit`. H-022 (Cluster A).
    let private temporalRetentionUnit (u: TemporalRetentionUnit) : TemporalRetentionPeriodUnit =
        match u with
        | Days   -> TemporalRetentionPeriodUnit.Days
        | Weeks  -> TemporalRetentionPeriodUnit.Weeks
        | Months -> TemporalRetentionPeriodUnit.Months
        | Years  -> TemporalRetentionPeriodUnit.Years

    let buildCreateTable
        (table: TableId)
        (columns: ColumnDef list)
        (pk: PrimaryKeyDef option)
        (fks: ForeignKeyDef list)
        (checks: ColumnCheckDef list)
        (temporal: TemporalConfig option)
        : Diagnostics<CreateTableStatement> =
        use _ = Bench.scope "emit.scriptDom.build.createTable"
        let stmt = CreateTableStatement()
        stmt.SchemaObjectName <- schemaObjectFromTableId table
        let def = TableDefinition()
        columns
        |> Bench.iterDo "emit.scriptDom.build.columns" (fun c ->
            def.ColumnDefinitions.Add(columnDefinition c))
        match pk with
        | None -> ()
        | Some p ->
            // LR3 — single-column PKs inline at the column-constraint
            // level (V1 CreateTableStatementBuilder.cs:67-78 shape);
            // multi-column PKs remain table-level (V1 L80-98 shape).
            match p.Columns with
            | [ _ ] -> attachInlinePrimaryKey def.ColumnDefinitions p
            | _     -> def.TableConstraints.Add(primaryKeyConstraint p)
        fks
        |> Bench.iterDo "emit.scriptDom.build.createTable.fk" (fun fk ->
            def.TableConstraints.Add(foreignKeyConstraint fk))
        // Slice 5.13.column-features-emit (chapter A.0' slice ε emit
        // closure): table-level CHECK constraints follow PK + FK in
        // declaration order, matching V1's CREATE TABLE shape.
        checks
        |> Bench.iterDo "emit.scriptDom.build.createTable.check" (fun chk ->
            def.TableConstraints.Add(checkConstraint chk))
        stmt.Definition <- def
        // H-022: `ModalityMark.Temporal` — emit PERIOD FOR SYSTEM_TIME
        // inside the table definition and WITH (SYSTEM_VERSIONING = ON)
        // as a table option. Both are required for SQL Server temporal
        // table creation; partial emission is not meaningful.
        match temporal with
        | None -> ()
        | Some tc ->
            match tc.PeriodStart, tc.PeriodEnd with
            | Some ps, Some pe ->
                let period = SystemTimePeriodDefinition()
                period.StartTimeColumn <- bracketed (Name.value ps)
                period.EndTimeColumn   <- bracketed (Name.value pe)
                def.SystemTimePeriod <- period
            | _ -> ()
            let sv = SystemVersioningTableOption()
            sv.OptionState <- OptionState.On
            match tc.HistorySchema, tc.HistoryTable with
            | Some hs, Some ht ->
                sv.HistoryTable <- schemaObjectName hs ht
            | _ -> ()
            match tc.Retention with
            | Infinite -> ()
            | Limited (value, unit) ->
                let ret = RetentionPeriodDefinition()
                ret.Duration <- IntegerLiteral(Value = string value)
                ret.Units <- temporalRetentionUnit unit
                sv.RetentionPeriod <- ret
            stmt.Options.Add(sv)
        Diagnostics.ofValue stmt

    // -----------------------------------------------------------------------
    // INSERT statements.
    // -----------------------------------------------------------------------

    /// Cell-value → ScalarExpression. Routes through the typed
    /// `SqlLiteral.ofRaw` projection (Core) and the SSDT-resident
    /// `buildSqlLiteral` mapping above.
    let private cellExpression (cell: CellValue) : ScalarExpression =
        SqlLiteral.ofRaw cell.Type cell.Raw |> buildSqlLiteral

    /// Build an `InsertStatement` for an `InsertRow` statement.
    /// Emits `INSERT INTO [schema].[table] ([col]…) VALUES (…)`.
    /// The column list mirrors the cell-value order to keep the
    /// emission deterministic.
    let buildInsertRow
        (table: TableId)
        (cells: CellValue list)
        : InsertStatement =
        use _ = Bench.scope "emit.scriptDom.build.insertRow"
        let stmt = InsertStatement()
        let spec = InsertSpecification()
        let target = NamedTableReference()
        let tableRef = SchemaObjectName()
        tableRef.Identifiers.Add(bracketed (TableId.schemaText table))
        tableRef.Identifiers.Add(bracketed (TableId.tableText table))
        target.SchemaObject <- tableRef
        spec.Target <- target
        // Column list — `(col1, col2, …)`.
        for cell in cells do
            let colRef = ColumnReferenceExpression()
            let mid = MultiPartIdentifier()
            mid.Identifiers.Add(bracketed cell.Column)
            colRef.MultiPartIdentifier <- mid
            spec.Columns.Add(colRef)
        // Values list — single-row literal `(VALUES (lit1, lit2, …))`.
        let valuesSrc = ValuesInsertSource()
        let row = RowValue()
        for cell in cells do
            row.ColumnValues.Add(cellExpression cell)
        valuesSrc.RowValues.Add(row)
        spec.InsertSource <- valuesSrc
        stmt.InsertSpecification <- spec
        stmt

    // -----------------------------------------------------------------------
    // MERGE statement (chapter 4.1.B; Tier-1 #1 RawTextEmitter retirement
    // arc cash-out). The typed-AST replacement for StaticSeedsEmitter's
    // hand-rolled StringBuilder MERGE construction.
    // -----------------------------------------------------------------------

    /// MERGE construction args. Decoupled from `Catalog`/`Kind`/
    /// `Attribute` so the builder is testable in isolation and reusable
    /// across MERGE-emitting consumers (StaticSeedsEmitter today; future
    /// MigrationDependenciesEmitter).
    ///
    /// Conventions:
    ///   - `target`: `[schema].[table]` of the merge target
    ///   - `allColumns`: column names in declaration order (the
    ///     INSERT column list + Source-table column list)
    ///   - `pkColumns`: ON-clause join columns (typically the PK
    ///     column subset of `allColumns`)
    ///   - `updColumns`: WHEN MATCHED UPDATE-SET columns (typically
    ///     `allColumns` minus `pkColumns`)
    ///   - `rows`: each row = one `SqlLiteral` per column in
    ///     `allColumns` order (per-cell typed via
    ///     `Projection.Core.SqlLiteral`)
    ///   - `cdcAware`: when true, emit the WHEN MATCHED AND
    ///     <change-detection-predicate> form per chapter 4.1.B slice β;
    ///     when false, the unconditional V1-shape WHEN MATCHED.
    type MergeBuildArgs =
        {
            Target     : TableId
            AllColumns : string list
            PkColumns  : string list
            UpdColumns : string list
            Rows       : SqlLiteral list list
            CdcAware   : bool
        }

    /// Build a `[Target|Source].[col]` qualified column reference.
    let private qualifiedColumnRef (alias: string) (col: string) : ColumnReferenceExpression =
        let refExpr = ColumnReferenceExpression()
        let mid = MultiPartIdentifier()
        mid.Identifiers.Add(bracketed alias)
        mid.Identifiers.Add(bracketed col)
        refExpr.MultiPartIdentifier <- mid
        refExpr

    /// Build a single `Target.[col] = Source.[col]` boolean expression
    /// for the ON-clause join condition or the change-detection
    /// equality test.
    let private columnEquality (col: string) : BooleanComparisonExpression =
        let cmp = BooleanComparisonExpression()
        cmp.ComparisonType <- BooleanComparisonType.Equals
        cmp.FirstExpression <- qualifiedColumnRef "Target" col :> ScalarExpression
        cmp.SecondExpression <- qualifiedColumnRef "Source" col :> ScalarExpression
        cmp

    /// Build a single `Target.[col] <> Source.[col]` boolean expression
    /// for the change-detection predicate's value-mismatch arm.
    let private columnInequality (col: string) : BooleanComparisonExpression =
        let cmp = BooleanComparisonExpression()
        cmp.ComparisonType <- BooleanComparisonType.NotEqualToBrackets
        cmp.FirstExpression <- qualifiedColumnRef "Target" col :> ScalarExpression
        cmp.SecondExpression <- qualifiedColumnRef "Source" col :> ScalarExpression
        cmp

    /// Build a `<expr> IS [NOT] NULL` boolean expression.
    let private isNullCheck (alias: string) (col: string) (negate: bool) : BooleanIsNullExpression =
        let n = BooleanIsNullExpression()
        n.Expression <- qualifiedColumnRef alias col :> ScalarExpression
        n.IsNot <- negate
        n

    /// Combine two boolean expressions via `AND` / `OR`.
    let private boolBinary (op: BooleanBinaryExpressionType) (left: BooleanExpression) (right: BooleanExpression) : BooleanExpression =
        let bin = BooleanBinaryExpression()
        bin.BinaryExpressionType <- op
        bin.FirstExpression <- left
        bin.SecondExpression <- right
        bin :> BooleanExpression

    /// Wrap a boolean expression in parentheses (`(<expr>)`).
    let private boolParen (inner: BooleanExpression) : BooleanExpression =
        let p = BooleanParenthesisExpression()
        p.Expression <- inner
        p :> BooleanExpression

    /// Fold a non-empty list of boolean expressions left-to-right via
    /// the given operator. `[a; b; c]` with `AND` → `a AND b AND c`.
    let private foldBool (op: BooleanBinaryExpressionType) (terms: BooleanExpression list) : BooleanExpression =
        match terms with
        | [] -> invalidOp "foldBool: empty term list"
        | first :: rest ->
            rest |> List.fold (fun acc t -> boolBinary op acc t) first

    /// Build the change-detection predicate for one non-key column:
    ///   Target.[c] <> Source.[c]
    ///   OR (Target.[c] IS NULL AND Source.[c] IS NOT NULL)
    ///   OR (Target.[c] IS NOT NULL AND Source.[c] IS NULL)
    ///
    /// Per chapter 4.1.B slice β + pre-scope §6: nullable-aware, since
    /// `NULL <> NULL` is `UNKNOWN` in SQL. The three OR-branches cover
    /// value-mismatch + null-asymmetry both ways.
    let private perColumnChangeDetection (col: string) : BooleanExpression =
        let valueDiff = columnInequality col :> BooleanExpression
        let targetNullSourceNot =
            boolBinary
                BooleanBinaryExpressionType.And
                (isNullCheck "Target" col false :> BooleanExpression)
                (isNullCheck "Source" col true :> BooleanExpression)
            |> boolParen
        let targetNotSourceNull =
            boolBinary
                BooleanBinaryExpressionType.And
                (isNullCheck "Target" col true :> BooleanExpression)
                (isNullCheck "Source" col false :> BooleanExpression)
            |> boolParen
        foldBool
            BooleanBinaryExpressionType.Or
            [ valueDiff; targetNullSourceNot; targetNotSourceNull ]

    /// The full change-detection predicate across all updatable
    /// columns: per-column predicates joined with OR. Wrapped in
    /// parentheses so the surrounding `WHEN MATCHED AND (...)` is
    /// well-grouped under ScriptDom's emission rules.
    let private changeDetectionPredicate (updColumns: string list) : BooleanExpression =
        updColumns
        |> List.map perColumnChangeDetection
        |> foldBool BooleanBinaryExpressionType.Or
        |> boolParen

    /// Build the `MergeStatement` for the supplied args. Per Tier-1 #1:
    /// the entire MERGE flows through ScriptDom's typed AST; rendering
    /// happens at `Sql160ScriptGenerator.GenerateScript` (no
    /// StringBuilder, no per-fragment LINT-ALLOWs).
    ///
    /// Chapter 4.9 slice ζ — canonical Diagnostics-bearing signature.
    /// Pattern established for future Diagnostics sources (row-literal
    /// type-coercion failures; ON-clause condition anomalies; per-cell
    /// SqlLiteral parse-back validation). Today the entries list is
    /// empty by construction.
    let private buildMergeStatementCore (args: MergeBuildArgs) : MergeStatement =
        use _ = Bench.scope "emit.scriptDom.build.merge"
        let stmt = MergeStatement()
        let spec = MergeSpecification()
        // Target [schema].[table] AS Target
        let targetRef = NamedTableReference()
        targetRef.SchemaObject <- schemaObjectFromTableId args.Target
        spec.Target <- targetRef
        spec.TableAlias <- bracketed "Target"

        // Source: USING (VALUES (...), (...)) AS Source(c1, c2, ...)
        let inline_ = InlineDerivedTable()
        args.Rows
        |> Bench.iterDo "emit.scriptDom.build.merge.row" (fun row ->
            let rv = RowValue()
            for cell in row do
                rv.ColumnValues.Add(buildSqlLiteral cell)
            inline_.RowValues.Add(rv))
        inline_.Alias <- bracketed "Source"
        for c in args.AllColumns do
            inline_.Columns.Add(bracketed c)
        spec.TableReference <- inline_ :> TableReference

        // ON-clause: Target.[pk1] = Source.[pk1] [AND ...]
        let onTerms =
            args.PkColumns
            |> List.map (fun c -> columnEquality c :> BooleanExpression)
        spec.SearchCondition <- foldBool BooleanBinaryExpressionType.And onTerms

        // WHEN MATCHED [AND <predicate>] THEN UPDATE SET
        if not (List.isEmpty args.UpdColumns) then
            let updateAction = UpdateMergeAction()
            for c in args.UpdColumns do
                let setClause = AssignmentSetClause()
                setClause.Column <- qualifiedColumnRef "Target" c
                setClause.NewValue <- qualifiedColumnRef "Source" c :> ScalarExpression
                updateAction.SetClauses.Add(setClause :> SetClause)
            let matchedClause = MergeActionClause()
            matchedClause.Condition <- MergeCondition.Matched
            if args.CdcAware then
                matchedClause.SearchCondition <- changeDetectionPredicate args.UpdColumns
            matchedClause.Action <- updateAction
            spec.ActionClauses.Add(matchedClause)

        // WHEN NOT MATCHED THEN INSERT (...) VALUES (Source.[c1], ...)
        let insertAction = InsertMergeAction()
        let insertSrc = ValuesInsertSource()
        let insertRow = RowValue()
        for c in args.AllColumns do
            insertRow.ColumnValues.Add(qualifiedColumnRef "Source" c :> ScalarExpression)
        insertSrc.RowValues.Add(insertRow)
        insertAction.Source <- insertSrc
        for c in args.AllColumns do
            let colRef = ColumnReferenceExpression()
            let mid = MultiPartIdentifier()
            mid.Identifiers.Add(bracketed c)
            colRef.MultiPartIdentifier <- mid
            insertAction.Columns.Add(colRef)
        let notMatchedClause = MergeActionClause()
        notMatchedClause.Condition <- MergeCondition.NotMatched
        notMatchedClause.Action <- insertAction
        spec.ActionClauses.Add(notMatchedClause)

        stmt.MergeSpecification <- spec
        stmt

    /// Canonical Diagnostics-bearing entry point (chapter 4.9 slice ζ).
    let buildMergeStatement (args: MergeBuildArgs) : Diagnostics<MergeStatement> =
        Diagnostics.ofValue (buildMergeStatementCore args)

    // -----------------------------------------------------------------------
    // UPDATE statement (chapter 4.1.B slice δ; two-phase insertion /
    // cycle-breaking).
    //
    // Phase-2 of the cycle-breaking pattern (`PhasedDynamicEntityInsert
    // Generator.cs:88-148` is V1's empirical reference): once Phase-1
    // MERGEs have inserted every cycle-participating row with deferred
    // FK columns NULLed, Phase-2 UPDATEs populate those FK columns now
    // that their target rows exist. Per the Tier-3 hard-requirement
    // deferral (`DECISIONS 2026-05-10 — text-builder-as-first-instinct`):
    // every new SQL-emitting consumer starts on the typed-AST library.
    // The `MergeBuildArgs` record + per-cell `SqlLiteral` projection is
    // the immediate precedent.
    // -----------------------------------------------------------------------

    /// UPDATE construction args. Decoupled from `Catalog`/`Kind`/
    /// `Attribute` (mirrors `MergeBuildArgs`'s `TableId` + name-list
    /// shape) so the builder is testable in isolation and reusable
    /// across UPDATE-emitting consumers (StaticSeedsEmitter Phase-2
    /// today; future MigrationDependenciesEmitter / BootstrapEmitter
    /// Phase-2 paths).
    ///
    /// `SetCells`: column-name → typed-literal pairs the UPDATE
    /// assigns. Order preserved in the emitted SET clause for T1
    /// byte-determinism.
    ///
    /// `WhereCells`: column-name → typed-literal pairs joined with
    /// AND in the WHERE clause (typically the row's PK columns —
    /// composite PKs supported via the cell list). Order preserved
    /// for T1 byte-determinism.
    type UpdateBuildArgs =
        {
            Target     : TableId
            SetCells   : (string * SqlLiteral) list
            WhereCells : (string * SqlLiteral) list
            /// When `true`, append a change-detection predicate to the
            /// WHERE clause (`AND (<set-col-differs> OR ...)`) so a
            /// no-op UPDATE is structurally filtered before SQL Server
            /// observes it. Symmetric to `MergeBuildArgs.CdcAware`'s
            /// effect on the `WHEN MATCHED AND (...)` predicate.
            ///
            /// Per `DECISIONS 2026-05-18 (slice 5.13.cdc-silence-cross-emitter)`:
            /// V2 must structurally guarantee CDC silence for every
            /// emission delta variant — Phase-2 UPDATE cannot lean on
            /// SQL Server's no-op-MERGE optimization (which applies
            /// to MERGE WHEN MATCHED UPDATE, not standalone UPDATE).
            /// When `CdcAware = false` and `SetCells` is non-empty,
            /// the UPDATE fires unconditionally on PK match — the
            /// pre-slice shape, preserved for non-CDC-tracked tables.
            CdcAware   : bool
        }

    /// Build a single-part `[col]` column reference (no Target/Source
    /// alias, unlike MERGE's two-part `qualifiedColumnRef`).
    let private barColumnRef (col: string) : ColumnReferenceExpression =
        let refExpr = ColumnReferenceExpression()
        let mid = MultiPartIdentifier()
        mid.Identifiers.Add(bracketed col)
        refExpr.MultiPartIdentifier <- mid
        refExpr

    /// Build `[col] = <literal>` for one WHERE-clause equality term.
    let private whereEquality (col: string) (lit: SqlLiteral) : BooleanComparisonExpression =
        let cmp = BooleanComparisonExpression()
        cmp.ComparisonType <- BooleanComparisonType.Equals
        cmp.FirstExpression <- barColumnRef col :> ScalarExpression
        cmp.SecondExpression <- buildSqlLiteral lit
        cmp

    /// Build `[col] IS [NOT] NULL` against a single-part column ref
    /// (no Target/Source alias — UPDATE statement has only the
    /// target table in scope). Sibling to the MERGE-side aliased
    /// `isNullCheck`.
    let private singlePartIsNullCheck (col: string) (negate: bool) : BooleanIsNullExpression =
        let n = BooleanIsNullExpression()
        n.Expression <- barColumnRef col :> ScalarExpression
        n.IsNot <- negate
        n

    /// Build `[col] <> <literal>` for a standalone-UPDATE change-
    /// detection term.
    let private barInequality (col: string) (lit: SqlLiteral) : BooleanComparisonExpression =
        let cmp = BooleanComparisonExpression()
        cmp.ComparisonType <- BooleanComparisonType.NotEqualToBrackets
        cmp.FirstExpression <- barColumnRef col :> ScalarExpression
        cmp.SecondExpression <- buildSqlLiteral lit
        cmp

    /// Single-column change-detection predicate suitable for a
    /// standalone UPDATE's WHERE clause:
    ///
    ///   - literal is NULL → predicate = `[col] IS NOT NULL`
    ///     (difference iff target carries a value)
    ///   - literal is non-NULL → predicate =
    ///     `[col] <> <lit> OR [col] IS NULL`
    ///     (difference iff target value differs OR is NULL)
    ///
    /// Sibling to the MERGE-side `perColumnChangeDetection`. The
    /// shape differs because UPDATE has no Source table — the new
    /// value is the typed `SqlLiteral` known at build-time, so the
    /// literal-IS-NULL branch collapses at compile-time rather than
    /// emitting a `<lit> IS NULL` SQL expression. Equivalent
    /// truth-table; tighter SQL.
    let private perColumnPhase2Difference (col: string) (lit: SqlLiteral) : BooleanExpression =
        match lit with
        | SqlLiteral.NullLit ->
            singlePartIsNullCheck col true :> BooleanExpression
        | _ ->
            let inequality = barInequality col lit :> BooleanExpression
            let isNull = singlePartIsNullCheck col false :> BooleanExpression
            boolBinary BooleanBinaryExpressionType.Or inequality isNull
            |> boolParen

    /// Full change-detection predicate across all SET cells: per-cell
    /// predicates joined with OR. Wrapped in parentheses for
    /// well-grouped emission under ScriptDom's rendering rules.
    let private phase2DifferencePredicate (setCells: (string * SqlLiteral) list) : BooleanExpression =
        setCells
        |> List.map (fun (col, lit) -> perColumnPhase2Difference col lit)
        |> foldBool BooleanBinaryExpressionType.Or
        |> boolParen

    /// Build the `UpdateStatement` for the supplied args. Per Tier-3
    /// (text-builder-as-first-instinct discipline): every node typed,
    /// every literal flowing through `SqlLiteral`, no terminal text
    /// composition until `Sql160ScriptGenerator.GenerateScript`.
    ///
    /// Chapter 4.9 slice ζ — canonical Diagnostics-bearing signature.
    /// Slice 5.13.cdc-silence-cross-emitter extension: when
    /// `args.CdcAware = true`, the WHERE clause appends a
    /// `(<any-set-cell-differs>)` term so no-op UPDATEs are
    /// structurally filtered before SQL Server observes them
    /// (eliminates the CDC leak path for Phase-2 UPDATEs on
    /// idempotent redeploy).
    let private buildUpdateStatementCore (args: UpdateBuildArgs) : UpdateStatement =
        use _ = Bench.scope "emit.scriptDom.build.update"
        let stmt = UpdateStatement()
        let spec = UpdateSpecification()
        let target = NamedTableReference()
        target.SchemaObject <- schemaObjectFromTableId args.Target
        spec.Target <- target

        // SET-clause: [col] = <literal>, [col] = <literal>, ...
        for (col, lit) in args.SetCells do
            let setClause = AssignmentSetClause()
            setClause.Column <- barColumnRef col
            setClause.NewValue <- buildSqlLiteral lit
            spec.SetClauses.Add(setClause :> SetClause)

        // WHERE-clause: [pk1] = <litpk1> [AND [pk2] = <litpk2> ...]
        //               [AND (<set-cell-differs> OR ...)]
        //
        // The change-detection term is appended when CdcAware = true
        // and SetCells is non-empty; an UPDATE with no SET cells
        // can't fire CDC (it's a no-op statement at the parser
        // boundary), so the predicate-append guard skips that
        // degenerate case.
        match args.WhereCells with
        | [] -> ()
        | cells ->
            let pkTerms =
                cells
                |> List.map (fun (col, lit) ->
                    whereEquality col lit :> BooleanExpression)
            let allTerms =
                if args.CdcAware && not (List.isEmpty args.SetCells) then
                    pkTerms @ [ phase2DifferencePredicate args.SetCells ]
                else
                    pkTerms
            let where = WhereClause()
            where.SearchCondition <- foldBool BooleanBinaryExpressionType.And allTerms
            spec.WhereClause <- where

        stmt.UpdateSpecification <- spec
        stmt

    /// Canonical Diagnostics-bearing entry point (chapter 4.9 slice ζ).
    let buildUpdateStatement (args: UpdateBuildArgs) : Diagnostics<UpdateStatement> =
        Diagnostics.ofValue (buildUpdateStatementCore args)

    // -----------------------------------------------------------------------
    // SET IDENTITY_INSERT statement.
    // -----------------------------------------------------------------------

    /// Build a `SetIdentityInsertStatement` for the typed
    /// `(TableId, enabled)` pair. Emits
    /// `SET IDENTITY_INSERT [schema].[table] {ON|OFF};`.
    /// Per ScriptDom 170.23.0 API: the `.Table` property carries
    /// the typed `SchemaObjectName` directly (not wrapped in
    /// `NamedTableReference`).
    let buildSetIdentityInsert
        (table: TableId)
        (enabled: bool)
        : SetIdentityInsertStatement =
        use _ = Bench.scope "emit.scriptDom.build.setIdentityInsert"
        let stmt = SetIdentityInsertStatement()
        stmt.Table <- schemaObjectFromTableId table
        stmt.IsOn <- enabled
        stmt

    // -----------------------------------------------------------------------
    // CREATE INDEX statement (chapter 4.1.A slice 3).
    // -----------------------------------------------------------------------

    /// Build a `CreateIndexStatement` for an `IndexDef`. Emits
    /// `CREATE [UNIQUE] [NONCLUSTERED] INDEX [name] ON [schema].[table]
    /// ([col1], [col2], ...);`. ScriptDom's `CreateIndexStatement`
    /// carries the `Unique` flag, the `Name` identifier, the
    /// `OnName` schema-object-name (the table), and a `Columns`
    /// list of `ColumnWithSortOrder` (column reference + ASC/DESC).
    /// V2 emits ASC by default per V1 convention; non-clustered is
    /// implicit (ScriptDom emits NONCLUSTERED for non-PK indexes
    /// per SQL Server defaults).
    /// Parse a raw filter-definition string into a typed
    /// `BooleanExpression`, surfacing parse failures as Diagnostic
    /// warnings via the V2 `Diagnostics<'a>` writer. Mirrors V1's
    /// `IndexScriptBuilder.ParsePredicate`
    /// (`src/Osm.Smo/PerTableEmission/IndexScriptBuilder.cs:403-419`)
    /// shape, lifted to `TSql160Parser` (SQL Server 2022 compat 160
    /// per supreme operating discipline pillar 4) + extended with
    /// the V2 Diagnostics emission path codified at chapter 4.6
    /// slice γ open Q3.
    ///
    /// **Diagnostic shape per chapter 4.6 open Q3:**
    /// - Source = `"emitter:ssdt"`
    /// - Code = `"emit.ssdt.index.filterParseFailure"`
    /// - Severity = `Warning` (does not block emission)
    /// - Message names the raw filter + parser error count
    /// - Metadata carries `raw` (the original filter string) + `errorCount`
    ///
    /// **Returns `Diagnostics<BooleanExpression option>`** — Some on
    /// successful parse (empty diagnostic entries); None with one
    /// Warning entry per failure path. Consumers compose via
    /// `Diagnostics.bind`.
    let tryParseFilterWithDiagnostics (raw: string) : Diagnostics<BooleanExpression option> =
        if System.String.IsNullOrWhiteSpace raw then
            Diagnostics.ofValue None
        else
            use reader = new System.IO.StringReader(raw)
            let fragment, errors = threadLocalParser.Value.ParseBooleanExpression(reader)
            let errorCount = if isNull errors then 0 else errors.Count
            match Option.ofObj fragment with
            | None ->
                let entry : DiagnosticEntry =
                    { Source   = "emitter:ssdt"
                      Severity = DiagnosticSeverity.Warning
                      Code     = "emit.ssdt.index.filterParseFailure"
                      Message  = sprintf "Index filter expression failed to parse; emit will omit WHERE clause (errors: %d)" errorCount
                      SsKey    = None
                      Metadata =
                        Map.ofList
                            [ "raw", raw
                              "errorCount", string errorCount ]
                      SuggestedConfig = None }
                Diagnostics.ofValueWith entry None
            | Some _ when errorCount > 0 ->
                let entry : DiagnosticEntry =
                    { Source   = "emitter:ssdt"
                      Severity = DiagnosticSeverity.Warning
                      Code     = "emit.ssdt.index.filterParseFailure"
                      Message  = sprintf "Index filter parser reported errors; emit will omit WHERE clause (errors: %d)" errorCount
                      SsKey    = None
                      Metadata =
                        Map.ofList
                            [ "raw", raw
                              "errorCount", string errorCount ]
                      SuggestedConfig = None }
                Diagnostics.ofValueWith entry None
            | Some f ->
                // Wrap in BooleanParenthesisExpression for output
                // readability (V1 IndexScriptBuilder convention).
                let wrapped =
                    match f with
                    | :? BooleanParenthesisExpression -> f
                    | _ ->
                        let parens = BooleanParenthesisExpression()
                        parens.Expression <- f
                        parens :> BooleanExpression
                Diagnostics.ofValue (Some wrapped)

    /// CREATE INDEX builder. Returns the typed `CreateIndexStatement`
    /// paired with a Diagnostics stream surfacing filter-parse failures
    /// (Source=emitter:ssdt; Code=emit.ssdt.index.filterParseFailure;
    /// Severity=Warning). When the filter parses cleanly, the entries
    /// list is empty and the resulting CREATE INDEX carries the typed
    /// FilterPredicate. When the filter fails to parse, the resulting
    /// CREATE INDEX omits the WHERE clause AND the Diagnostics carries
    /// a Warning entry consumers can surface in the manifest or
    /// per-emit log.
    ///
    /// Callers that don't surface diagnostics drop them explicitly via
    /// `.Value` at the call site (chapter 4.7 slice β cash-out of
    /// chapter 4.6 slice γ "Diagnostics-aware emitter signature"
    /// forward signal; V2-no-back-compat — no legacy silent-skip
    /// wrapper).
    let buildCreateIndex (idx: IndexDef) : Diagnostics<CreateIndexStatement> =
        use _ = Bench.scope "emit.scriptDom.build.createIndex"
        let stmt = CreateIndexStatement()
        stmt.Unique <- idx.IsUnique
        stmt.Name <- bracketed idx.Name
        stmt.OnName <- schemaObjectFromTableId idx.Table
        idx.Columns
        |> Bench.iterDo "emit.scriptDom.build.createIndex.keyColumn" (fun keyCol ->
            let col = ColumnWithSortOrder()
            let colRef = ColumnReferenceExpression()
            let mid = MultiPartIdentifier()
            mid.Identifiers.Add(bracketed keyCol.Name)
            colRef.MultiPartIdentifier <- mid
            col.Column <- colRef
            // Chapter 4.9 slice γ — per-column sort direction. V1's
            // IndexScriptBuilder convention sets SortOrder only on
            // descending columns (ascending falls through as
            // NotSpecified). Mirrors that here.
            match keyCol.Direction with
            | IndexDefColumnDirection.Descending -> col.SortOrder <- SortOrder.Descending
            | IndexDefColumnDirection.Ascending  -> col.SortOrder <- SortOrder.NotSpecified
            stmt.Columns.Add(col))
        // Chapter 4.5 slice β — INCLUDE columns for covering indexes.
        idx.IncludedColumns
        |> Bench.iterDo "emit.scriptDom.build.createIndex.includeColumn" (fun colName ->
            let colRef = ColumnReferenceExpression()
            let mid = MultiPartIdentifier()
            mid.Identifiers.Add(bracketed colName)
            colRef.MultiPartIdentifier <- mid
            stmt.IncludeColumns.Add(colRef))
        // Chapter 4.8 slice β — on-disk index options WITH (…) clause.
        // Each option's typed ScriptDom IndexOption variant is added only
        // when the field deviates from V1's IndexOnDiskMetadata.Empty
        // default (FillFactor=None, IsPadded=false, AllowRowLocks=true,
        // AllowPageLocks=true, NoRecomputeStatistics=false). SQL Server's
        // CREATE INDEX omits the WITH clause when all defaults hold.
        let intLiteral (n: int) : ScalarExpression =
            let lit = IntegerLiteral()
            lit.Value <- string n
            lit :> ScalarExpression
        let onOffOption (kind: IndexOptionKind) (isOn: bool) : IndexOption =
            let opt = IndexStateOption()
            opt.OptionKind <- kind
            opt.OptionState <-
                if isOn then OptionState.On
                else OptionState.Off
            opt :> IndexOption
        let exprOption (kind: IndexOptionKind) (expr: ScalarExpression) : IndexOption =
            let opt = IndexExpressionOption()
            opt.OptionKind <- kind
            opt.Expression <- expr
            opt :> IndexOption
        match idx.FillFactor with
        | Some n -> stmt.IndexOptions.Add(exprOption IndexOptionKind.FillFactor (intLiteral n))
        | None -> ()
        if idx.IsPadded then
            stmt.IndexOptions.Add(onOffOption IndexOptionKind.PadIndex true)
        if not idx.AllowRowLocks then
            stmt.IndexOptions.Add(onOffOption IndexOptionKind.AllowRowLocks false)
        if not idx.AllowPageLocks then
            stmt.IndexOptions.Add(onOffOption IndexOptionKind.AllowPageLocks false)
        if idx.NoRecomputeStatistics then
            stmt.IndexOptions.Add(onOffOption IndexOptionKind.StatisticsNoRecompute true)
        // Slice 5.13.index-features-emit (matrix row 55):
        // `IGNORE_DUP_KEY = ON` via IndexStateOption.
        if idx.IgnoreDuplicateKey then
            stmt.IndexOptions.Add(onOffOption IndexOptionKind.IgnoreDupKey true)
        // Slice 5.13.index-features-emit (matrix row 56):
        // `DATA_COMPRESSION = <level>` via DataCompressionOption.
        // The realization-layer enum (`IndexDataCompressionSql`) maps
        // 1:1 to ScriptDom's `DataCompressionLevel` (modulo the
        // columnstore variants V2 doesn't surface today; lift trigger:
        // a columnstore-bearing index surfaces in production).
        match idx.DataCompression with
        | None -> ()
        | Some level ->
            let opt = DataCompressionOption()
            opt.OptionKind <- IndexOptionKind.DataCompression
            // Fully qualify ScriptDom's `DataCompressionLevel` —
            // Core's `Projection.Core.DataCompressionLevel` shares
            // the same type-name (intentional parallel modeling per
            // pillar 8 ubiquitous language); at this join site the
            // F# compiler needs the namespace prefix to disambiguate.
            opt.CompressionLevel <-
                match level with
                | NoneCompressionSql ->
                    Microsoft.SqlServer.TransactSql.ScriptDom.DataCompressionLevel.None
                | RowCompressionSql ->
                    Microsoft.SqlServer.TransactSql.ScriptDom.DataCompressionLevel.Row
                | PageCompressionSql ->
                    Microsoft.SqlServer.TransactSql.ScriptDom.DataCompressionLevel.Page
            stmt.IndexOptions.Add(opt :> IndexOption)
        // Slice A.4.7'-prelude.row56-dataspace (LR7 closure): emit
        // `ON [filegroup]` or `ON [scheme]([cols])` via ScriptDom's
        // `FileGroupOrPartitionScheme`. Closed-DU dispatch on the
        // realization-layer `IndexDataSpaceSql` produces both variants
        // through the same ScriptDom shape (IsFileGroup discriminates).
        match idx.DataSpace with
        | None -> ()
        | Some (FilegroupDataSpaceSql name) ->
            let ds = FileGroupOrPartitionScheme()
            ds.Name <- IdentifierOrValueExpression()
            ds.Name.Identifier <- bracketed name
            stmt.OnFileGroupOrPartitionScheme <- ds
        | Some (PartitionSchemeDataSpaceSql (name, cols)) ->
            let ds = FileGroupOrPartitionScheme()
            ds.Name <- IdentifierOrValueExpression()
            ds.Name.Identifier <- bracketed name
            for col in cols do
                ds.PartitionSchemeColumns.Add(bracketed col)
            stmt.OnFileGroupOrPartitionScheme <- ds
        // Chapter 4.5 slice α — WHERE clause via TSql160Parser, lifted
        // through the Diagnostics writer.
        match idx.Filter with
        | None -> Diagnostics.ofValue stmt
        | Some raw ->
            tryParseFilterWithDiagnostics raw
            |> Diagnostics.map (fun predOpt ->
                predOpt |> Option.iter (fun p -> stmt.FilterPredicate <- p)
                stmt)

    // -----------------------------------------------------------------------
    // Statement-level dispatch.
    // -----------------------------------------------------------------------

    /// Map a SQL-bearing `Statement` to its `TSqlStatement` form.
    /// Build an `ExecuteStatement` for a `SetExtendedProperty`. Chapter
    /// 4.1.A slice 8: V1 form per `ExtendedPropertyScriptBuilder.cs`:
    /// `EXEC sys.sp_addextendedproperty @name=N'…', @value=N'…',
    ///  @level0type=N'SCHEMA', @level0name=N'<schema>',
    ///  @level1type=N'TABLE',  @level1name=N'<table>'
    ///  [, @level2type=N'COLUMN'|N'INDEX', @level2name=N'<col-or-idx>']`.
    /// All string parameters are national (`N''`) per V1 + SQL Server
    /// extended-property convention; the value parameter is `NULL`
    /// when `propertyValue = None`.
    /// Build an `ExecuteStatement` for an `sp_addextendedproperty`
    /// call. Chapter 4.9 slice ζ — canonical Diagnostics-bearing
    /// signature. Future Diagnostics sources (level-validation
    /// failures on rare V1 ownership forms; truncation warnings on
    /// >7500-char property values) flow through the writer without
    /// reshaping the signature.
    let private buildSetExtendedPropertyCore
            (owner: ExtendedPropertyOwner)
            (propertyName: string)
            (propertyValue: string option)
            : ExecuteStatement =
        use _ = Bench.scope "emit.scriptDom.build.setExtendedProperty"
        let nText (value: string) : ScalarExpression =
            let l = StringLiteral()
            l.Value <- value
            l.IsNational <- true
            l :> ScalarExpression

        let parameter (name: string) (value: ScalarExpression) : ExecuteParameter =
            let p = ExecuteParameter()
            let vr = VariableReference()
            vr.Name <- name
            p.Variable <- vr
            p.ParameterValue <- value
            p

        let procRef = ProcedureReference()
        procRef.Name <- schemaObjectName "sys" "sp_addextendedproperty"
        let procRefName = ProcedureReferenceName()
        procRefName.ProcedureReference <- procRef

        let exec = ExecutableProcedureReference()
        exec.ProcedureReference <- procRefName

        let valueExpr =
            match propertyValue with
            | Some v -> nText v
            | None   -> NullLiteral() :> ScalarExpression

        exec.Parameters.Add(parameter "@name"  (nText propertyName))
        exec.Parameters.Add(parameter "@value" valueExpr)
        // Chapter 4.9 slice ε — multi-level dispatch on owner. SCHEMA-
        // only emits no @level1; TABLE owners (Table / Column / Index)
        // always emit @level0=SCHEMA + @level1=TABLE; column / index
        // add @level2.
        match owner with
        | SchemaProperty schema ->
            exec.Parameters.Add(parameter "@level0type" (nText "SCHEMA"))
            exec.Parameters.Add(parameter "@level0name" (nText schema))
        | TableProperty table ->
            exec.Parameters.Add(parameter "@level0type" (nText "SCHEMA"))
            exec.Parameters.Add(parameter "@level0name" (nText (TableId.schemaText table)))
            exec.Parameters.Add(parameter "@level1type" (nText "TABLE"))
            exec.Parameters.Add(parameter "@level1name" (nText (TableId.tableText table)))
        | ColumnProperty (table, col) ->
            exec.Parameters.Add(parameter "@level0type" (nText "SCHEMA"))
            exec.Parameters.Add(parameter "@level0name" (nText (TableId.schemaText table)))
            exec.Parameters.Add(parameter "@level1type" (nText "TABLE"))
            exec.Parameters.Add(parameter "@level1name" (nText (TableId.tableText table)))
            exec.Parameters.Add(parameter "@level2type" (nText "COLUMN"))
            exec.Parameters.Add(parameter "@level2name" (nText col))
        | IndexProperty (table, idx) ->
            exec.Parameters.Add(parameter "@level0type" (nText "SCHEMA"))
            exec.Parameters.Add(parameter "@level0name" (nText (TableId.schemaText table)))
            exec.Parameters.Add(parameter "@level1type" (nText "TABLE"))
            exec.Parameters.Add(parameter "@level1name" (nText (TableId.tableText table)))
            exec.Parameters.Add(parameter "@level2type" (nText "INDEX"))
            exec.Parameters.Add(parameter "@level2name" (nText idx))

        let spec = ExecuteSpecification()
        spec.ExecutableEntity <- exec

        let stmt = ExecuteStatement()
        stmt.ExecuteSpecification <- spec
        stmt

    /// Build `ALTER TABLE <table> WITH NOCHECK CHECK CONSTRAINT
    /// <constraintName>` via ScriptDom's
    /// `AlterTableConstraintModificationStatement`. Preserves the
    /// deployed target's FK trust state when V1's
    /// `#FkReality.IsNoCheck = 1` flips `Reference.IsConstraintTrusted`
    /// to `false`. Slice 5.13.fk-features-emit (matrix row 59).
    ///
    /// Renders as the V1 emission shape:
    ///   `ALTER TABLE [Schema].[Table] WITH NOCHECK CHECK CONSTRAINT [FK_…]`
    /// — the `WITH NOCHECK` prefix (ExistingRowsCheckEnforcement)
    /// tells SQL Server to skip validation of existing rows; the
    /// trailing `CHECK CONSTRAINT` (ConstraintEnforcement) re-enables
    /// the constraint going forward.
    let buildAlterTableNoCheckConstraint
            (table: TableId)
            (constraintName: string)
            : AlterTableConstraintModificationStatement =
        use _ = Bench.scope "emit.scriptDom.build.alterTableNoCheckConstraint"
        let stmt = AlterTableConstraintModificationStatement()
        stmt.SchemaObjectName <- schemaObjectFromTableId table
        stmt.ConstraintNames.Add(bracketed constraintName)
        stmt.ExistingRowsCheckEnforcement <- ConstraintEnforcement.NoCheck
        stmt.ConstraintEnforcement <- ConstraintEnforcement.Check
        stmt

    /// Build `ALTER TABLE <table> NOCHECK CONSTRAINT <fk>` via ScriptDom's
    /// `AlterTableConstraintModificationStatement` with
    /// `ConstraintEnforcement.NoCheck` (and no `WITH` prefix). Disables the
    /// constraint — `is_disabled = 1`, `is_not_trusted = 1`. 6.A.6 — the
    /// first leg of the two-step that reproduces an enabled-untrusted FK
    /// (see `Statement.AlterTableDisableConstraint`); `buildAlterTableNoCheckConstraint`
    /// is the second leg (re-enable skipping validation).
    let buildAlterTableDisableConstraint
            (table: TableId)
            (constraintName: string)
            : AlterTableConstraintModificationStatement =
        use _ = Bench.scope "emit.scriptDom.build.alterTableDisableConstraint"
        let stmt = AlterTableConstraintModificationStatement()
        stmt.SchemaObjectName <- schemaObjectFromTableId table
        stmt.ConstraintNames.Add(bracketed constraintName)
        stmt.ConstraintEnforcement <- ConstraintEnforcement.NoCheck
        stmt

    /// Build `ALTER TABLE <table> DISABLE TRIGGER <name>` via
    /// ScriptDom's `AlterTableTriggerModificationStatement` with
    /// `TriggerEnforcement.Disable`. Preserves the deployed target's
    /// trigger-disabled state when V1's
    /// `IndexOnDiskMetadata`-equivalent `Trigger.IsDisabled = true`.
    /// Slice D.2.d (chapter D's emission-aesthetics arc).
    let buildAlterTableDisableTrigger
            (table: TableId)
            (triggerName: string)
            : AlterTableTriggerModificationStatement =
        use _ = Bench.scope "emit.scriptDom.build.alterTableDisableTrigger"
        let stmt = AlterTableTriggerModificationStatement()
        stmt.SchemaObjectName <- schemaObjectFromTableId table
        stmt.TriggerNames.Add(bracketed triggerName)
        stmt.TriggerEnforcement <- TriggerEnforcement.Disable
        stmt

    /// Build `ALTER INDEX <indexName> ON <table> DISABLE` via
    /// ScriptDom's `AlterIndexStatement` with
    /// `AlterIndexType.Disable`. Preserves a deployed target's
    /// disabled-index state when V1's
    /// `IndexOnDiskMetadata.IsDisabled = true`. Slice
    /// 5.13.index-features-emit (matrix row 55).
    ///
    /// Renders as the V1 emission shape:
    ///   `ALTER INDEX [name] ON [Schema].[Table] DISABLE`
    let buildAlterIndexDisable
            (table: TableId)
            (indexName: string)
            : AlterIndexStatement =
        use _ = Bench.scope "emit.scriptDom.build.alterIndexDisable"
        let stmt = AlterIndexStatement()
        stmt.Name <- bracketed indexName
        stmt.OnName <- schemaObjectFromTableId table
        stmt.AlterIndexType <- AlterIndexType.Disable
        stmt

    /// The `DataTypeReference` for a `ColumnDef` — concrete SqlStorage
    /// evidence when present, else the `PrimitiveType` mapping. Mirrors
    /// `columnDefinition`'s data-type branch exactly so an ALTER COLUMN's
    /// rendered type is byte-identical to the same column's CREATE TABLE
    /// declaration (SSDT consistency: the altered shape must equal the
    /// declared shape, or DacFx re-diffs it forever).
    let private columnDataType (c: ColumnDef) : DataTypeReference =
        match c.SqlStorage with
        | Some storage -> dataTypeReferenceFromStorage storage
        | None         -> dataTypeReference c.Type c.Length c.Precision c.Scale

    /// 6.A.12 — `ALTER TABLE <table> ADD <column>` via ScriptDom's
    /// `AlterTableAddTableElementStatement` carrying one
    /// `ColumnDefinition`. Reuses `columnDefinition` so the added
    /// column's full shape (type, nullability, IDENTITY, DEFAULT)
    /// matches a CREATE TABLE declaration of the same column.
    let buildAlterTableAddColumn
            (table: TableId)
            (c: ColumnDef)
            : AlterTableAddTableElementStatement =
        use _ = Bench.scope "emit.scriptDom.build.alterTableAddColumn"
        let stmt = AlterTableAddTableElementStatement()
        stmt.SchemaObjectName <- schemaObjectFromTableId table
        let def = TableDefinition()
        def.ColumnDefinitions.Add(columnDefinition c)
        stmt.Definition <- def
        stmt

    /// 6.A.12 — `ALTER TABLE <table> ALTER COLUMN <col> <type> NULL|NOT NULL`
    /// via ScriptDom's `AlterTableAlterColumnStatement`. Nullability rides
    /// the `AlterTableAlterColumnOption.Null | .NotNull` enum (verified
    /// against `Sql160ScriptGenerator`). Scope: the column SHAPE facets
    /// (type / length / precision / scale / nullability) — the
    /// `SchemaMigrationEmitter` routes DEFAULT / computed / identity /
    /// rename changes elsewhere (constraint DDL / RefactorLog), so this
    /// builder never sees a computed-column `ColumnDef`.
    let buildAlterTableAlterColumn
            (table: TableId)
            (c: ColumnDef)
            : AlterTableAlterColumnStatement =
        use _ = Bench.scope "emit.scriptDom.build.alterTableAlterColumn"
        let stmt = AlterTableAlterColumnStatement()
        stmt.SchemaObjectName <- schemaObjectFromTableId table
        stmt.ColumnIdentifier <- bracketed c.Name
        stmt.DataType <- columnDataType c
        // ALTER COLUMN always restates nullability (matches DacFx); the
        // option enum is the only place ScriptDom carries NULL/NOT NULL.
        stmt.AlterTableAlterColumnOption <-
            if c.Nullable then AlterTableAlterColumnOption.Null
            else AlterTableAlterColumnOption.NotNull
        stmt

    /// C1 emitter follow-on — `ALTER TABLE <table> ADD CONSTRAINT <fk>
    /// FOREIGN KEY …` via ScriptDom's `AlterTableAddTableElementStatement`,
    /// reusing `foreignKeyConstraint` so a standalone-added FK is byte-identical
    /// to the same FK inlined in a CREATE TABLE.
    let buildAlterTableAddForeignKey
            (table: TableId)
            (fk: ForeignKeyDef)
            : AlterTableAddTableElementStatement =
        use _ = Bench.scope "emit.scriptDom.build.alterTableAddForeignKey"
        let stmt = AlterTableAddTableElementStatement()
        stmt.SchemaObjectName <- schemaObjectFromTableId table
        let def = TableDefinition()
        def.TableConstraints.Add(foreignKeyConstraint fk)
        stmt.Definition <- def
        stmt

    /// C1 destructive follow-on — `ALTER TABLE <table> DROP COLUMN|CONSTRAINT
    /// <name>` via ScriptDom's `AlterTableDropTableElementStatement`.
    let private buildAlterTableDropElement
            (table: TableId)
            (elementType: TableElementType)
            (name: string)
            : AlterTableDropTableElementStatement =
        use _ = Bench.scope "emit.scriptDom.build.alterTableDropElement"
        let stmt = AlterTableDropTableElementStatement()
        stmt.SchemaObjectName <- schemaObjectFromTableId table
        let element = AlterTableDropTableElement()
        element.TableElementType <- elementType
        element.Name <- bracketed name
        stmt.AlterTableDropTableElements.Add(element)
        stmt

    let buildAlterTableDropColumn (table: TableId) (columnName: string) : AlterTableDropTableElementStatement =
        buildAlterTableDropElement table TableElementType.Column columnName

    let buildAlterTableDropConstraint (table: TableId) (constraintName: string) : AlterTableDropTableElementStatement =
        buildAlterTableDropElement table TableElementType.Constraint constraintName

    /// C1 destructive follow-on — `DROP INDEX <name> ON <table>` via ScriptDom's
    /// `DropIndexStatement` + `DropIndexClause`.
    let buildDropIndex (table: TableId) (indexName: string) : DropIndexStatement =
        use _ = Bench.scope "emit.scriptDom.build.dropIndex"
        let stmt = DropIndexStatement()
        let clause = DropIndexClause()
        clause.Index <- bracketed indexName
        clause.Object <- schemaObjectFromTableId table
        stmt.DropIndexClauses.Add(clause)
        stmt

    /// C1 destructive follow-on — `DROP SEQUENCE <schema>.<name>` via ScriptDom's
    /// `DropSequenceStatement`.
    let buildDropSequence (schema: string) (name: string) : DropSequenceStatement =
        use _ = Bench.scope "emit.scriptDom.build.dropSequence"
        let stmt = DropSequenceStatement()
        stmt.Objects.Add(schemaObjectName schema name)
        stmt

    /// Canonical Diagnostics-bearing entry point (chapter 4.9 slice ζ).
    let buildSetExtendedProperty
            (owner: ExtendedPropertyOwner)
            (propertyName: string)
            (propertyValue: string option)
            : Diagnostics<ExecuteStatement> =
        Diagnostics.ofValue (buildSetExtendedPropertyCore owner propertyName propertyValue)

    /// Parse a raw trigger `Definition` string into its `TSqlStatement`.
    /// Returns `None` when the definition is blank, the parser reports
    /// errors, or the result is not a `TSqlScript`. Follows the
    /// `parseComputedExpression` pattern using the full-script parser
    /// path: `TSql160Parser.Parse` → cast to `TSqlScript` → extract
    /// the first statement in the first batch. H-019 (Cluster A).
    let private tryParseTriggerBody (definition: string) : TSqlStatement option =
        if System.String.IsNullOrWhiteSpace definition then
            None
        else
            use reader = new System.IO.StringReader(definition)
            let frag, errors = threadLocalParser.Value.Parse(reader)
            let errorCount = if isNull errors then 0 else errors.Count
            if errorCount > 0 then None
            else
                match frag with
                | :? TSqlScript as s ->
                    s.Batches
                    |> Seq.tryHead
                    |> Option.bind (fun batch ->
                        batch.Statements |> Seq.tryHead)
                | _ -> None

    /// Map a sequence data type string (e.g. "bigint", "int",
    /// "decimal(18,0)") to a `SqlDataTypeReference`. Handles the six
    /// SQL Server sequence-legal numeric types; decimal/numeric with
    /// precision+scale parameters use integer literals for P and S.
    /// Falls back to `bigint` for unrecognised strings (V1 sequences
    /// are almost exclusively `bigint`). H-020 (Cluster A).
    let private sequenceDataType (rawType: string) : DataTypeReference =
        let r = SqlDataTypeReference()
        let lower = rawType.Trim().ToLowerInvariant()
        // Detect parametric decimal/numeric: "decimal(18,0)" etc.
        let parenIdx = lower.IndexOf('(')
        let baseName = if parenIdx >= 0 then lower.[..parenIdx - 1].TrimEnd() else lower
        let sqlOpt =
            match baseName with
            | "bigint"   -> SqlDataTypeOption.BigInt
            | "int"      -> SqlDataTypeOption.Int
            | "smallint" -> SqlDataTypeOption.SmallInt
            | "tinyint"  -> SqlDataTypeOption.TinyInt
            | "decimal"  -> SqlDataTypeOption.Decimal
            | "numeric"  -> SqlDataTypeOption.Numeric
            | _          -> SqlDataTypeOption.BigInt
        r.SqlDataTypeOption <- sqlOpt
        r.Name <- SchemaObjectName()
        r.Name.Identifiers.Add(bracketed (baseName.ToLowerInvariant()))
        // Parse precision and scale from "(P, S)" if present.
        if parenIdx >= 0 && (sqlOpt = SqlDataTypeOption.Decimal || sqlOpt = SqlDataTypeOption.Numeric) then
            let inner = lower.[parenIdx + 1..].TrimEnd([| ')'; ' ' |])
            let parts = inner.Split(',')
            match parts with
            | [| p; s |] ->
                let pLit = IntegerLiteral()
                pLit.Value <- p.Trim()
                r.Parameters.Add(pLit)
                let sLit = IntegerLiteral()
                sLit.Value <- s.Trim()
                r.Parameters.Add(sLit)
            | _ -> ()
        r :> DataTypeReference

    /// Build a `CreateSequenceStatement` from a V2 `Sequence` IR record.
    /// Maps `Schema`/`Name` to a bracketed `SchemaObjectName`; maps each
    /// populated IR field to the corresponding `SequenceOption`. Emits
    /// AS, START WITH, INCREMENT BY, MINVALUE, MAXVALUE, CYCLE/NO CYCLE,
    /// and CACHE/NO CACHE clauses. H-020 (Cluster A).
    let buildCreateSequence (seq: Sequence) : CreateSequenceStatement =
        let stmt = CreateSequenceStatement()
        stmt.Name <- schemaObjectName seq.Schema (Name.value seq.Name)
        // AS <type>
        let dtOpt = DataTypeSequenceOption()
        dtOpt.OptionKind <- SequenceOptionKind.As
        dtOpt.DataType <- sequenceDataType seq.DataType
        stmt.SequenceOptions.Add(dtOpt)
        // Numeric scalar options helper.
        let addDecimal (kind: SequenceOptionKind) (v: decimal) =
            let opt = ScalarExpressionSequenceOption()
            opt.OptionKind <- kind
            let lit = NumericLiteral()
            lit.Value <- v.ToString("G", System.Globalization.CultureInfo.InvariantCulture)
            opt.OptionValue <- lit
            stmt.SequenceOptions.Add(opt)
        seq.StartValue |> Option.iter (addDecimal SequenceOptionKind.Start)
        seq.Increment  |> Option.iter (addDecimal SequenceOptionKind.Increment)
        seq.Minimum    |> Option.iter (addDecimal SequenceOptionKind.MinValue)
        seq.Maximum    |> Option.iter (addDecimal SequenceOptionKind.MaxValue)
        // CYCLE / NO CYCLE — ScalarExpressionSequenceOption with NoValue flag.
        let cycleOpt = ScalarExpressionSequenceOption()
        cycleOpt.OptionKind <- SequenceOptionKind.Cycle
        cycleOpt.NoValue <- not seq.IsCycleEnabled
        stmt.SequenceOptions.Add(cycleOpt)
        // CACHE / NO CACHE.
        match seq.CacheMode with
        | Unspecified -> ()
        | NoCache ->
            let noCache = ScalarExpressionSequenceOption()
            noCache.OptionKind <- SequenceOptionKind.Cache
            noCache.NoValue <- true
            stmt.SequenceOptions.Add(noCache)
        | Cache ->
            let cacheOpt = ScalarExpressionSequenceOption()
            cacheOpt.OptionKind <- SequenceOptionKind.Cache
            cacheOpt.NoValue <- false
            seq.CacheSize |> Option.iter (fun n ->
                let lit = IntegerLiteral()
                lit.Value <- string n
                cacheOpt.OptionValue <- lit)
            stmt.SequenceOptions.Add(cacheOpt)
        stmt

    /// Returns `None` for non-SQL variants (`Comment`, `Blank`) so
    /// the realization layer can splice them through the text
    /// stream directly. Closed-DU dispatch — adding a new variant
    /// lights up an exhaustiveness error here only.
    let buildStatement (stmt: Statement) : TSqlStatement option =
        match stmt with
        | Blank -> None
        | Comment _ -> None
        | BatchSeparator -> None  // Slice D.2.c — sqlcmd directive; no ScriptDom AST equivalent
        | CreateTable (table, cols, pk, fks, checks, temporal) ->
            Some ((buildCreateTable table cols pk fks checks temporal).Value :> TSqlStatement)
        | CreateIndex idx ->
            Some ((buildCreateIndex idx).Value :> TSqlStatement)
        | InsertRow (table, cells) ->
            Some (buildInsertRow table cells :> TSqlStatement)
        | SetIdentityInsert (table, enabled) ->
            Some (buildSetIdentityInsert table enabled :> TSqlStatement)
        | SetExtendedProperty (owner, propName, propValue) ->
            Some ((buildSetExtendedProperty owner propName propValue).Value :> TSqlStatement)
        | AlterTableNoCheckConstraint (table, constraintName) ->
            Some (buildAlterTableNoCheckConstraint table constraintName :> TSqlStatement)
        | AlterTableDisableConstraint (table, constraintName) ->
            Some (buildAlterTableDisableConstraint table constraintName :> TSqlStatement)
        | AlterIndexDisable (table, indexName) ->
            Some (buildAlterIndexDisable table indexName :> TSqlStatement)
        | CreateTrigger definition ->
            tryParseTriggerBody definition
        | AlterTableDisableTrigger (table, triggerName) ->
            Some (buildAlterTableDisableTrigger table triggerName :> TSqlStatement)
        | CreateSequence seqIR ->
            Some (buildCreateSequence seqIR :> TSqlStatement)
        | AlterTableAddColumn (table, column) ->
            Some (buildAlterTableAddColumn table column :> TSqlStatement)
        | AlterTableAlterColumn (table, column) ->
            Some (buildAlterTableAlterColumn table column :> TSqlStatement)
        | AlterTableAddForeignKey (table, fk) ->
            Some (buildAlterTableAddForeignKey table fk :> TSqlStatement)
        | AlterTableDropColumn (table, columnName) ->
            Some (buildAlterTableDropColumn table columnName :> TSqlStatement)
        | AlterTableDropConstraint (table, constraintName) ->
            Some (buildAlterTableDropConstraint table constraintName :> TSqlStatement)
        | DropIndex (table, indexName) ->
            Some (buildDropIndex table indexName :> TSqlStatement)
        | DropSequence (schema, name) ->
            Some (buildDropSequence schema name :> TSqlStatement)
