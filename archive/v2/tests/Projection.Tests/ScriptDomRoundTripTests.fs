module Projection.Tests.ScriptDomRoundTripTests

open System.IO
open Xunit
open Microsoft.SqlServer.TransactSql.ScriptDom
open Projection.Core
open Projection.Core.Passes
open Projection.Targets.SSDT
open Projection.Tests.Fixtures

// Chapter A.4.7' slice η — `CanonicalizeIdentity.run` is private; the
// canonical surface is `.registered.Run` returning
// `Lineage<Diagnostics<Catalog>>`. This per-file shim restores the
// `Lineage<Catalog>` shape so existing assertions keep reading.
let private ciRun (c: Catalog) : Lineage<Catalog> =
    CanonicalizeIdentity.registered.Run c |> Lineage.map (fun d -> d.Value)

// ---------------------------------------------------------------------------
// ScriptDom round-trip + determinism property tests.
//
// Per `DECISIONS 2026-05-09 — Built-in obligation`, V2 delegates
// SQL emission to Microsoft's typed `TSqlFragment` AST and the
// `Sql160ScriptGenerator` byte-deterministic emitter. Three
// load-bearing properties:
//
//   1. **Determinism** — `emit(stmt)` is byte-identical across
//      repeat invocations.
//   2. **Parse-roundtrip** — `parse(emit(stmt))` re-acquires a
//      `TSqlFragment` of the *same shape* (same statement type;
//      no parser errors). Validates that the AST we built is
//      grammatically valid T-SQL.
//   3. **Stream framing** — `ScriptDomGenerate.toText` over a
//      `seq<Statement>` is byte-deterministic; trivia (`Comment`,
//      `Blank`) splices through deterministically.
//
// Postdoctoral discipline (per the user's "clean room locked in;
// postdoctoral scrutiny" commitment): every test here pins inputs,
// asserts byte-equality, and validates parse-correctness. No
// substring matching; no string interpolation in assertions.
// ---------------------------------------------------------------------------

let private parseSql (sql: string) : TSqlFragment * ParseError list =
    let parser = TSql160Parser(initialQuotedIdentifiers = true)
    use reader = new StringReader(sql)
    let mutable errors : System.Collections.Generic.IList<ParseError> | null = null
    let fragment = parser.Parse(reader, &errors)
    let errorList =
        match errors with
        | null -> []
        | es -> [ for e in es -> e ]
    nonNull fragment, errorList

// ---------------------------------------------------------------------------
// CreateTable — typed AST emission round-trip.
// ---------------------------------------------------------------------------

let private sampleColumns : ColumnDef list =
    [
        {
            Collation = None
            Identity = None
            Name = "Id"
            Type = Integer
            SqlStorage = None
            Length = None; Precision = None; Scale = None
            Nullable = false; IsIdentity = true; IsPrimaryKey = true
            DefaultValue = None; DefaultName = None
            Computed = None
            Provenance = "Id"
        }
        {
            Collation = None
            Identity = None
            Name = "Name"
            Type = Text
            SqlStorage = None
            Length = Some 100; Precision = None; Scale = None
            Nullable = false; IsIdentity = false; IsPrimaryKey = false
            DefaultValue = None; DefaultName = None
            Computed = None
            Provenance = "Name"
        }
        {
            Collation = None
            Identity = None
            Name = "Score"
            Type = Decimal
            SqlStorage = None
            Length = None; Precision = Some 18; Scale = Some 4
            Nullable = true; IsIdentity = false; IsPrimaryKey = false
            DefaultValue = None; DefaultName = None
            Computed = None
            Provenance = "Score"
        }
    ]

let private samplePk : PrimaryKeyDef =
    { Name = "PK_dbo_Customer"; Columns = ["Id"] }

let private sampleTable : TableId =
    TableId.create "dbo" "Customer" |> Result.value

[<Fact>]
let ``T1: ScriptDom CreateTable emit is byte-identical across repeat invocations`` () =
    let fragment =
        (ScriptDomBuild.buildCreateTable
            sampleTable sampleColumns (Some samplePk) [] [] None).Value
    let runs =
        [ for _ in 1 .. 25 -> ScriptDomGenerate.generateOne fragment ]
    let head = List.head runs
    Assert.All(runs, fun s -> Assert.Equal(head, s))

[<Fact>]
let ``Parse-roundtrip: ScriptDom CreateTable emit re-parses cleanly`` () =
    let fragment =
        (ScriptDomBuild.buildCreateTable
            sampleTable sampleColumns (Some samplePk) [] [] None).Value
    let emitted = ScriptDomGenerate.generateOne fragment
    let reparsed, errors = parseSql emitted
    Assert.Empty(errors)
    Assert.NotNull(reparsed)
    // Re-parsed AST is a TSqlScript whose first statement is a
    // CreateTableStatement. Confirms the emitted text is
    // grammatically valid T-SQL on Sql160 grammar.
    let script = reparsed :?> TSqlScript
    Assert.Single(script.Batches) |> ignore
    let batch = script.Batches.[0]
    Assert.Single(batch.Statements) |> ignore
    Assert.IsType<CreateTableStatement>(batch.Statements.[0])

[<Fact>]
let ``ScriptDom CreateTable emit names the schema and table`` () =
    let fragment =
        (ScriptDomBuild.buildCreateTable
            sampleTable sampleColumns (Some samplePk) [] [] None).Value
    let emitted = ScriptDomGenerate.generateOne fragment
    // Re-parse and inspect the typed AST — no substring search per
    // the no-string-concatenation discipline.
    let reparsed, _ = parseSql emitted
    let script = reparsed :?> TSqlScript
    let stmt = script.Batches.[0].Statements.[0] :?> CreateTableStatement
    let identifiers =
        stmt.SchemaObjectName.Identifiers
        |> Seq.map (fun i -> i.Value)
        |> List.ofSeq
    Assert.Equal<string list>(["dbo"; "Customer"], identifiers)

// ---------------------------------------------------------------------------
// F1 (audit 2026-06-17) — collation faithfulness. A source-declared collation
// rides through `ColumnDef.Collation` to a `COLLATE <name>` clause; absence
// emits nothing (byte-identical to pre-F1). Asserted via the parsed AST's
// `ColumnDefinition.Collation` identifier (no substring search, per the
// no-string-concatenation discipline).
// ---------------------------------------------------------------------------

let private columnNamed (name: string) (collation: string option) : ColumnDef =
    { Collation = collation
      Identity = None
      Name = name; Type = Text; SqlStorage = None
      Length = Some 100; Precision = None; Scale = None
      Nullable = false; IsIdentity = false; IsPrimaryKey = false
      DefaultValue = None; DefaultName = None
      Computed = None; Provenance = name }

let private firstColumnOf (cols: ColumnDef list) : ColumnDefinition =
    let fragment = (ScriptDomBuild.buildCreateTable sampleTable cols None [] [] None).Value
    let emitted = ScriptDomGenerate.generateOne fragment
    let reparsed, errors = parseSql emitted
    Assert.Empty(errors)
    let script = reparsed :?> TSqlScript
    let stmt = script.Batches.[0].Statements.[0] :?> CreateTableStatement
    stmt.Definition.ColumnDefinitions.[0]

[<Fact>]
let ``F1: a column carrying a Collation emits a COLLATE clause naming that collation`` () =
    let col = firstColumnOf [ columnNamed "Label" (Some "SQL_Latin1_General_CP1_CI_AS") ]
    Assert.NotNull(col.Collation)
    Assert.Equal<string>("SQL_Latin1_General_CP1_CI_AS", (nonNull col.Collation).Value)

[<Fact>]
let ``F1: a column with no Collation emits no COLLATE clause (byte-identical to pre-F1)`` () =
    let col = firstColumnOf [ columnNamed "Label" None ]
    Assert.Null(col.Collation)

// ---------------------------------------------------------------------------
// F10 (audit 2026-06-17) — IDENTITY seed/increment is IR-driven, not a (1,1)
// hardcode. A reflected non-default seed rides through `ColumnDef.Identity` to
// `IDENTITY(seed, increment)`; absence emits the OS-native `IDENTITY(1, 1)`
// byte-identically. Asserted on the re-parsed AST's `IdentityOptions`.
// ---------------------------------------------------------------------------

let private identityColumn (identity: (int64 * int64) option) : ColumnDef =
    { Collation = None
      Identity = identity
      Name = "Id"; Type = Integer; SqlStorage = None
      Length = None; Precision = None; Scale = None
      Nullable = false; IsIdentity = true; IsPrimaryKey = false
      DefaultValue = None; DefaultName = None
      Computed = None; Provenance = "Id" }

let private identitySeedIncrement (col: ColumnDefinition) : string * string =
    let opts = nonNull col.IdentityOptions
    (opts.IdentitySeed :?> IntegerLiteral).Value, (opts.IdentityIncrement :?> IntegerLiteral).Value

[<Fact>]
let ``F10: an identity column carrying a non-default seed emits IDENTITY(seed, increment)`` () =
    let col = firstColumnOf [ identityColumn (Some (1000L, 5L)) ]
    Assert.NotNull(col.IdentityOptions)
    Assert.Equal<string * string>(("1000", "5"), identitySeedIncrement col)

[<Fact>]
let ``F10: an identity column with no seed emits the OS-native IDENTITY(1, 1) (byte-identical)`` () =
    let col = firstColumnOf [ identityColumn None ]
    Assert.NotNull(col.IdentityOptions)
    Assert.Equal<string * string>(("1", "1"), identitySeedIncrement col)

[<Fact>]
let ``ScriptDom CreateTable emit carries every column with its data type`` () =
    let fragment =
        (ScriptDomBuild.buildCreateTable
            sampleTable sampleColumns (Some samplePk) [] [] None).Value
    let emitted = ScriptDomGenerate.generateOne fragment
    let reparsed, _ = parseSql emitted
    let script = reparsed :?> TSqlScript
    let stmt = script.Batches.[0].Statements.[0] :?> CreateTableStatement
    let columnNames =
        stmt.Definition.ColumnDefinitions
        |> Seq.map (fun c -> c.ColumnIdentifier.Value)
        |> List.ofSeq
    Assert.Equal<string list>(["Id"; "Name"; "Score"], columnNames)
    // Confirm the typed data-type is present per V2 mapping.
    let dataTypes =
        stmt.Definition.ColumnDefinitions
        |> Seq.map (fun c ->
            match c.DataType with
            | :? SqlDataTypeReference as r -> r.SqlDataTypeOption
            | _ -> SqlDataTypeOption.None)
        |> List.ofSeq
    Assert.Equal<SqlDataTypeOption list>(
        [
            SqlDataTypeOption.Int
            SqlDataTypeOption.NVarChar
            SqlDataTypeOption.Decimal
        ],
        dataTypes)

[<Fact>]
let ``ScriptDom CreateTable carries the primary-key constraint`` () =
    let fragment =
        (ScriptDomBuild.buildCreateTable
            sampleTable sampleColumns (Some samplePk) [] [] None).Value
    let emitted = ScriptDomGenerate.generateOne fragment
    let reparsed, _ = parseSql emitted
    let script = reparsed :?> TSqlScript
    let stmt = script.Batches.[0].Statements.[0] :?> CreateTableStatement
    // Slice 5.3.α.column-axis-deferral-closeout (LR3): single-column PKs
    // inline at the column-constraint level (V1 CreateTableStatementBuilder
    // .cs:67-78 shape); multi-column PKs remain table-level. The sample
    // PK is single-column ([ "Id" ]) so the constraint appears on the
    // matching ColumnDefinition's Constraints list.
    let inlinePks =
        stmt.Definition.ColumnDefinitions
        |> Seq.collect (fun cd ->
            cd.Constraints
            |> Seq.choose (function
                | :? UniqueConstraintDefinition as u when u.IsPrimaryKey -> Some u
                | _ -> None))
        |> List.ofSeq
    let tableLevelPks =
        stmt.Definition.TableConstraints
        |> Seq.choose (function
            | :? UniqueConstraintDefinition as u when u.IsPrimaryKey -> Some u
            | _ -> None)
        |> List.ofSeq
    let pks = inlinePks @ tableLevelPks
    Assert.Single(pks) |> ignore
    let pk = List.head pks
    Assert.Equal("PK_dbo_Customer", pk.ConstraintIdentifier.Value)

// ---------------------------------------------------------------------------
// InsertRow — typed AST emission.
// ---------------------------------------------------------------------------

let private sampleCells : CellValue list =
    [
        { Column = "Id"; Type = Integer; Raw = Some "42" }
        { Column = "Name"; Type = Text; Raw = Some "Acme Corp" }
        { Column = "Score"; Type = Decimal; Raw = Some "3.14" }
    ]

[<Fact>]
let ``T1: ScriptDom InsertRow emit is byte-identical across repeat invocations`` () =
    let fragment = ScriptDomBuild.buildInsertRow sampleTable sampleCells
    let runs =
        [ for _ in 1 .. 25 -> ScriptDomGenerate.generateOne fragment ]
    let head = List.head runs
    Assert.All(runs, fun s -> Assert.Equal(head, s))

[<Fact>]
let ``Parse-roundtrip: ScriptDom InsertRow re-parses to InsertStatement`` () =
    let fragment = ScriptDomBuild.buildInsertRow sampleTable sampleCells
    let emitted = ScriptDomGenerate.generateOne fragment
    let reparsed, errors = parseSql emitted
    Assert.Empty(errors)
    let script = reparsed :?> TSqlScript
    Assert.IsType<InsertStatement>(script.Batches.[0].Statements.[0])

[<Fact>]
let ``ScriptDom InsertRow with NULL cell preserves NULL through round-trip`` () =
    let cellsWithNull =
        [
            { Column = "Id"; Type = Integer; Raw = Some "1" }
            { Column = "Name"; Type = Text; Raw = None }   // explicit NULL (WP-3: out-of-band)
        ]
    let fragment = ScriptDomBuild.buildInsertRow sampleTable cellsWithNull
    let emitted = ScriptDomGenerate.generateOne fragment
    let reparsed, errors = parseSql emitted
    Assert.Empty(errors)
    let script = reparsed :?> TSqlScript
    let stmt = script.Batches.[0].Statements.[0] :?> InsertStatement
    let valuesSrc = stmt.InsertSpecification.InsertSource :?> ValuesInsertSource
    let row = valuesSrc.RowValues.[0]
    Assert.IsType<NullLiteral>(row.ColumnValues.[1])

[<Fact>]
let ``ScriptDom InsertRow with an empty-string Text cell renders N'' — a value, not NULL (F11)`` () =
    let cellsWithEmpty =
        [
            { Column = "Id"; Type = Integer; Raw = Some "1" }
            { Column = "Name"; Type = Text; Raw = Some "" }   // genuine empty string
        ]
    let fragment = ScriptDomBuild.buildInsertRow sampleTable cellsWithEmpty
    let emitted = ScriptDomGenerate.generateOne fragment
    let reparsed, errors = parseSql emitted
    Assert.Empty(errors)
    let script = reparsed :?> TSqlScript
    let stmt = script.Batches.[0].Statements.[0] :?> InsertStatement
    let valuesSrc = stmt.InsertSpecification.InsertSource :?> ValuesInsertSource
    let row = valuesSrc.RowValues.[0]
    let lit = Assert.IsType<StringLiteral>(row.ColumnValues.[1])
    Assert.True(lit.IsNational, "empty-string Text renders as the national N'' literal")
    Assert.Equal("", lit.Value)

// ---------------------------------------------------------------------------
// SetIdentityInsert.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: ScriptDom SetIdentityInsert emit is byte-identical across repeat invocations`` () =
    let fragment = ScriptDomBuild.buildSetIdentityInsert sampleTable true
    let runs =
        [ for _ in 1 .. 25 -> ScriptDomGenerate.generateOne fragment ]
    let head = List.head runs
    Assert.All(runs, fun s -> Assert.Equal(head, s))

[<Fact>]
let ``Parse-roundtrip: ScriptDom SetIdentityInsert ON re-parses cleanly`` () =
    let fragment = ScriptDomBuild.buildSetIdentityInsert sampleTable true
    let emitted = ScriptDomGenerate.generateOne fragment
    let reparsed, errors = parseSql emitted
    Assert.Empty(errors)
    let script = reparsed :?> TSqlScript
    let stmt = script.Batches.[0].Statements.[0] :?> SetIdentityInsertStatement
    Assert.True(stmt.IsOn)

[<Fact>]
let ``Parse-roundtrip: ScriptDom SetIdentityInsert OFF round-trips to IsOn=false`` () =
    let fragment = ScriptDomBuild.buildSetIdentityInsert sampleTable false
    let emitted = ScriptDomGenerate.generateOne fragment
    let reparsed, errors = parseSql emitted
    Assert.Empty(errors)
    let script = reparsed :?> TSqlScript
    let stmt = script.Batches.[0].Statements.[0] :?> SetIdentityInsertStatement
    Assert.False(stmt.IsOn)

// ---------------------------------------------------------------------------
// Stream-framing: toText over seq<Statement>.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: ScriptDomGenerate.toText is byte-identical across repeat invocations`` () =
    let stmts =
        [
            Comment "Customer table"
            Blank
            CreateTable (sampleTable, sampleColumns, Some samplePk, [], [], None)
            Blank
            SetIdentityInsert (sampleTable, true)
            InsertRow (sampleTable, sampleCells)
            SetIdentityInsert (sampleTable, false)
        ]
    let runs =
        [ for _ in 1 .. 10 -> ScriptDomGenerate.toText stmts ]
    let head = List.head runs
    Assert.All(runs, fun s -> Assert.Equal(head, s))

[<Fact>]
let ``ScriptDomGenerate.toText splices comments through with -- prefix`` () =
    let stmts =
        [
            Comment "header line"
            CreateTable (sampleTable, sampleColumns, Some samplePk, [], [], None)
        ]
    let emitted = ScriptDomGenerate.toText stmts
    // Split on \n and filter the comment line; assert it carries the
    // expected `-- ` prefix and the text. No substring search; line-
    // exact equality.
    let lines = emitted.Split('\n')
    let commentLine = lines.[0]
    Assert.Equal("-- header line", commentLine)

[<Fact>]
let ``ScriptDomGenerate.toText emits all SQL statements through ScriptDom`` () =
    let stmts =
        [
            CreateTable (sampleTable, sampleColumns, Some samplePk, [], [], None)
            InsertRow (sampleTable, sampleCells)
            SetIdentityInsert (sampleTable, true)
        ]
    let emitted = ScriptDomGenerate.toText stmts
    let reparsed, errors = parseSql emitted
    Assert.Empty(errors)
    let script = reparsed :?> TSqlScript
    // Single batch, three statements, in order.
    let stmtTypes =
        script.Batches
        |> Seq.collect (fun b -> b.Statements)
        |> Seq.map (fun s -> s.GetType().Name)
        |> List.ofSeq
    Assert.Equal<string list>(
        ["CreateTableStatement"; "InsertStatement"; "SetIdentityInsertStatement"],
        stmtTypes)

// ---------------------------------------------------------------------------
// Catalog-end-to-end — the worked-example proof that the pipeline
// is grammatically valid for the realistic fixture.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Parse-roundtrip: full sampleCatalog emits parseable SQL`` () =
    let enriched = (ciRun sampleCatalog).Value
    let stmts = SsdtDdlEmitter.statements enriched
    let emitted = ScriptDomGenerate.toText stmts
    let reparsed, errors = parseSql emitted
    Assert.Empty(errors)
    Assert.NotNull(reparsed)
