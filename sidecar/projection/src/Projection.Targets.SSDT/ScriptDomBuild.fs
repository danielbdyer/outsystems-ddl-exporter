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
/// `SetIdentityInsert`); non-SQL variants (`Comment`, `Blank`)
/// are realization-layer concerns — the splice into the emitted
/// text-stream lives in `ScriptDomGenerate.toText`.
///
/// Every builder is pure (V2's contract): same input → same
/// `TSqlFragment` shape, byte-for-byte. ScriptDom's
/// `Sql160ScriptGenerator` is the byte-deterministic emitter; per
/// `DECISIONS 2026-05-09 — Built-in obligation`, we delegate to
/// the canonical typed builder rather than hand-rolling SQL text.
[<RequireQualifiedAccess>]
module ScriptDomBuild =

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

    /// `[schema].[name]` for a `TableId` value. Goes through
    /// `TableId.qualified` semantics (the canonical bracket-quoting)
    /// but produces the typed AST form.
    let private schemaObjectFromTableId (t: TableId) : SchemaObjectName =
        schemaObjectName t.Schema t.Table

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
    /// Mirrors `Render.columnSqlType` semantics: `NVARCHAR(N)` /
    /// `NVARCHAR(MAX)`; `VARBINARY(N)` / `VARBINARY(MAX)`;
    /// `DECIMAL(P, S)` / `DECIMAL(P, 0)` / `DECIMAL(18, 4)`; fixed
    /// types pass through.
    let private dataTypeReference
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

    // -----------------------------------------------------------------------
    // Column definitions inside CREATE TABLE.
    // -----------------------------------------------------------------------

    /// Build a `ColumnDefinition` for a V2 `ColumnDef`. Includes
    /// nullability constraint, IDENTITY(1,1) clause when applicable.
    /// PRIMARY KEY constraint is emitted at the table level (not per
    /// column) — see `buildPrimaryKey` below. Same convention as
    /// `Render.fs`.
    let private columnDefinition (c: ColumnDef) : ColumnDefinition =
        let col = ColumnDefinition()
        col.ColumnIdentifier <- bracketed c.Name
        col.DataType <- dataTypeReference c.Type c.Length c.Precision c.Scale
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
        col

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

    /// Build a single-column foreign-key constraint per V2's
    /// `ForeignKeyDef`. Maps V2's `ReferenceActionSql` DU to
    /// ScriptDom's `DeleteUpdateAction`.
    let private foreignKeyConstraint
        (fk: ForeignKeyDef)
        : ConstraintDefinition =
        let cons = ForeignKeyConstraintDefinition()
        cons.ConstraintIdentifier <- bracketed fk.Name
        cons.Columns.Add(bracketed fk.SourceColumn)
        cons.ReferenceTableName <- schemaObjectFromTableId fk.Target
        cons.ReferencedTableColumns.Add(bracketed fk.TargetColumn)
        cons.DeleteAction <-
            match fk.OnDelete with
            | NoActionSql -> DeleteUpdateAction.NoAction
            | CascadeSql  -> DeleteUpdateAction.Cascade
            | SetNullSql  -> DeleteUpdateAction.SetNull
        cons :> ConstraintDefinition

    /// Build a `CreateTableStatement` from V2's typed
    /// `(TableId, ColumnDef list, PrimaryKeyDef option, ForeignKeyDef list)`
    /// triple. Pure: same inputs → same fragment shape (verified by
    /// `tests/Projection.Tests/ScriptDomRoundTripTests.fs`).
    let buildCreateTable
        (table: TableId)
        (columns: ColumnDef list)
        (pk: PrimaryKeyDef option)
        (fks: ForeignKeyDef list)
        : CreateTableStatement =
        let stmt = CreateTableStatement()
        stmt.SchemaObjectName <- schemaObjectFromTableId table
        let def = TableDefinition()
        for c in columns do
            def.ColumnDefinitions.Add(columnDefinition c)
        match pk with
        | None -> ()
        | Some p ->
            def.TableConstraints.Add(primaryKeyConstraint p)
        for fk in fks do
            def.TableConstraints.Add(foreignKeyConstraint fk)
        stmt.Definition <- def
        stmt

    // -----------------------------------------------------------------------
    // INSERT statements.
    // -----------------------------------------------------------------------

    /// Build the V-typed `Literal` expression for one cell. `""` is
    /// NULL; numeric / text / binary / guid / boolean dispatch per
    /// `Render.formatSqlLiteral` semantics. Returned as
    /// `ScalarExpression` for use in `RowValue` lists.
    let private cellExpression (cell: CellValue) : ScalarExpression =
        if System.String.IsNullOrEmpty cell.Raw then
            NullLiteral() :> ScalarExpression
        else
            match cell.Type with
            | Integer ->
                let lit = IntegerLiteral()
                lit.Value <- cell.Raw
                lit :> ScalarExpression
            | Decimal ->
                let lit = NumericLiteral()
                lit.Value <- cell.Raw
                lit :> ScalarExpression
            | Boolean ->
                let lit = IntegerLiteral()
                lit.Value <-
                    match cell.Raw.ToLowerInvariant() with
                    | "true" | "1" -> "1"
                    | _ -> "0"
                lit :> ScalarExpression
            | DateTime | Date | Time | Guid ->
                // Quoted string literal; ScriptDom emits with single quotes.
                let lit = StringLiteral()
                lit.Value <- cell.Raw
                lit.IsNational <- false
                lit :> ScalarExpression
            | Text ->
                // National string (`N'…'`); ScriptDom doubles single
                // quotes inside `Value` automatically.
                let lit = StringLiteral()
                lit.Value <- cell.Raw
                lit.IsNational <- true
                lit :> ScalarExpression
            | Binary ->
                // Hex literal `0x…`. ScriptDom carries
                // `BinaryLiteral.Value` already prefixed; we strip an
                // existing `0x` prefix so the writer adds it once.
                let lit = BinaryLiteral()
                let trimmed =
                    if cell.Raw.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase)
                    then cell.Raw.Substring 2
                    else cell.Raw
                lit.Value <- System.String.Concat("0x", trimmed)
                lit :> ScalarExpression

    /// Build an `InsertStatement` for an `InsertRow` statement.
    /// Emits `INSERT INTO [schema].[table] ([col]…) VALUES (…)`.
    /// The column list mirrors the cell-value order to keep the
    /// emission deterministic.
    let buildInsertRow
        (table: TableId)
        (cells: CellValue list)
        : InsertStatement =
        let stmt = InsertStatement()
        let spec = InsertSpecification()
        let target = NamedTableReference()
        let tableRef = SchemaObjectName()
        tableRef.Identifiers.Add(bracketed table.Schema)
        tableRef.Identifiers.Add(bracketed table.Table)
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
        let stmt = SetIdentityInsertStatement()
        stmt.Table <- schemaObjectFromTableId table
        stmt.IsOn <- enabled
        stmt

    // -----------------------------------------------------------------------
    // Statement-level dispatch.
    // -----------------------------------------------------------------------

    /// Map a SQL-bearing `Statement` to its `TSqlStatement` form.
    /// Returns `None` for non-SQL variants (`Comment`, `Blank`) so
    /// the realization layer can splice them through the text
    /// stream directly. Closed-DU dispatch — adding a new variant
    /// lights up an exhaustiveness error here only.
    let buildStatement (stmt: Statement) : TSqlStatement option =
        match stmt with
        | Blank -> None
        | Comment _ -> None
        | CreateTable (table, cols, pk, fks) ->
            Some (buildCreateTable table cols pk fks :> TSqlStatement)
        | InsertRow (table, cells) ->
            Some (buildInsertRow table cells :> TSqlStatement)
        | SetIdentityInsert (table, enabled) ->
            Some (buildSetIdentityInsert table enabled :> TSqlStatement)
