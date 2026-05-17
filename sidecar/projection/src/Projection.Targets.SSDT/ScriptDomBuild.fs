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
    /// Map a typed `SqlLiteral` value to a ScriptDom `ScalarExpression`
    /// (specifically a `Literal` subclass projected to its supertype
    /// for use in `RowValue.ColumnValues`). Per the Tier-1 #4 transition
    /// (RawTextEmitter retirement arc): the IR→typed-literal projection
    /// lives in `Projection.Core.SqlLiteral`; this is the SSDT-resident
    /// `SqlLiteral` → ScriptDom-`Literal` mapping. Used by both
    /// `cellExpression` (single-row `InsertStatement` VALUES) and
    /// `buildMergeStatement` (multi-row VALUES inside MERGE's
    /// InlineDerivedTable).
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
    let buildMergeStatement (args: MergeBuildArgs) : MergeStatement =
        let stmt = MergeStatement()
        let spec = MergeSpecification()
        // Target [schema].[table] AS Target
        let targetRef = NamedTableReference()
        targetRef.SchemaObject <- schemaObjectFromTableId args.Target
        spec.Target <- targetRef
        spec.TableAlias <- bracketed "Target"

        // Source: USING (VALUES (...), (...)) AS Source(c1, c2, ...)
        let inline_ = InlineDerivedTable()
        for row in args.Rows do
            let rv = RowValue()
            for cell in row do
                rv.ColumnValues.Add(buildSqlLiteral cell)
            inline_.RowValues.Add(rv)
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

    /// Build the `UpdateStatement` for the supplied args. Per Tier-3
    /// (text-builder-as-first-instinct discipline): every node typed,
    /// every literal flowing through `SqlLiteral`, no terminal text
    /// composition until `Sql160ScriptGenerator.GenerateScript`.
    let buildUpdateStatement (args: UpdateBuildArgs) : UpdateStatement =
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

        // WHERE-clause: [pk1] = <litpk1> [AND [pk2] = <litpk2> ...].
        // Empty WhereCells list emits no WHERE clause (full-table
        // UPDATE — caller's responsibility to avoid that shape).
        match args.WhereCells with
        | [] -> ()
        | cells ->
            let terms =
                cells
                |> List.map (fun (col, lit) ->
                    whereEquality col lit :> BooleanExpression)
            let where = WhereClause()
            where.SearchCondition <- foldBool BooleanBinaryExpressionType.And terms
            spec.WhereClause <- where

        stmt.UpdateSpecification <- spec
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
    /// `BooleanExpression`. Mirrors V1's
    /// `IndexScriptBuilder.ParsePredicate`
    /// (`src/Osm.Smo/PerTableEmission/IndexScriptBuilder.cs:403-419`)
    /// shape, lifted to `TSql160Parser` for SQL Server 2022 compat
    /// 160 alignment (V1 uses TSql150Parser; V2 targets 160 per
    /// the supreme operating discipline pillar 4 + Sql160ScriptGenerator
    /// precedent). Parse failures surface as `None` — chapter 4.5
    /// open Q3: parse failures emit a Diagnostic warning + skip
    /// the filter; the Diagnostic-emission pathway is deferred to a
    /// later slice when actual parse failures surface.
    let private parseFilterPredicate (raw: string) : BooleanExpression option =
        if System.String.IsNullOrWhiteSpace raw then None
        else
            let parser = TSql160Parser(initialQuotedIdentifiers = false)
            use reader = new System.IO.StringReader(raw)
            // F#'s tuple-return binding for the C# `out` parameter
            // shape: `BooleanExpression ParseBooleanExpression(TextReader, out IList<ParseError>)`.
            let fragment, errors = parser.ParseBooleanExpression(reader)
            match Option.ofObj fragment with
            | None -> None
            | Some _ when not (isNull errors) && errors.Count > 0 -> None
            | Some f ->
                // Wrap in BooleanParenthesisExpression for output
                // readability (V1 IndexScriptBuilder convention;
                // pillar 8 ubiquitous-language alignment).
                match f with
                | :? BooleanParenthesisExpression -> Some f
                | _ ->
                    let parens = BooleanParenthesisExpression()
                    parens.Expression <- f
                    Some (parens :> BooleanExpression)

    let buildCreateIndex (idx: IndexDef) : CreateIndexStatement =
        let stmt = CreateIndexStatement()
        stmt.Unique <- idx.IsUnique
        stmt.Name <- bracketed idx.Name
        stmt.OnName <- schemaObjectFromTableId idx.Table
        for colName in idx.Columns do
            let col = ColumnWithSortOrder()
            let colRef = ColumnReferenceExpression()
            let mid = MultiPartIdentifier()
            mid.Identifiers.Add(bracketed colName)
            colRef.MultiPartIdentifier <- mid
            col.Column <- colRef
            stmt.Columns.Add(col)
        // Chapter 4.5 slice α — emit `WHERE <expr>` for filtered
        // indexes. Filter expression parsed via TSql160Parser at
        // emit time per chapter open Q1; silent-skip on parse
        // failure per Q3.
        match idx.Filter with
        | None -> ()
        | Some raw ->
            match parseFilterPredicate raw with
            | Some predicate -> stmt.FilterPredicate <- predicate
            | None -> ()  // silent-skip; Diagnostic emission deferred
        stmt

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
    let buildSetExtendedProperty
            (table: TableId)
            (target: ExtendedPropertyTarget)
            (propertyName: string)
            (propertyValue: string option)
            : ExecuteStatement =
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

        exec.Parameters.Add(parameter "@name"       (nText propertyName))
        exec.Parameters.Add(parameter "@value"      valueExpr)
        exec.Parameters.Add(parameter "@level0type" (nText "SCHEMA"))
        exec.Parameters.Add(parameter "@level0name" (nText table.Schema))
        exec.Parameters.Add(parameter "@level1type" (nText "TABLE"))
        exec.Parameters.Add(parameter "@level1name" (nText table.Table))
        match target with
        | TableExtendedProperty -> ()
        | ColumnExtendedProperty col ->
            exec.Parameters.Add(parameter "@level2type" (nText "COLUMN"))
            exec.Parameters.Add(parameter "@level2name" (nText col))
        | IndexExtendedProperty idx ->
            exec.Parameters.Add(parameter "@level2type" (nText "INDEX"))
            exec.Parameters.Add(parameter "@level2name" (nText idx))

        let spec = ExecuteSpecification()
        spec.ExecutableEntity <- exec

        let stmt = ExecuteStatement()
        stmt.ExecuteSpecification <- spec
        stmt

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
        | CreateIndex idx ->
            Some (buildCreateIndex idx :> TSqlStatement)
        | InsertRow (table, cells) ->
            Some (buildInsertRow table cells :> TSqlStatement)
        | SetIdentityInsert (table, enabled) ->
            Some (buildSetIdentityInsert table enabled :> TSqlStatement)
        | SetExtendedProperty (table, target, propName, propValue) ->
            Some (buildSetExtendedProperty table target propName propValue :> TSqlStatement)
