module Projection.Tests.ScriptDomRoundTripTests

open System.IO
open Xunit
open Microsoft.SqlServer.TransactSql.ScriptDom
open Projection.Core
open Projection.Core.Passes
open Projection.Targets.SSDT
open Projection.Tests.Fixtures

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
            Name = "Id"
            Type = Integer
            Length = None; Precision = None; Scale = None
            Nullable = false; IsIdentity = true; IsPrimaryKey = true
            Provenance = "Id"
        }
        {
            Name = "Name"
            Type = Text
            Length = Some 100; Precision = None; Scale = None
            Nullable = false; IsIdentity = false; IsPrimaryKey = false
            Provenance = "Name"
        }
        {
            Name = "Score"
            Type = Decimal
            Length = None; Precision = Some 18; Scale = Some 4
            Nullable = true; IsIdentity = false; IsPrimaryKey = false
            Provenance = "Score"
        }
    ]

let private samplePk : PrimaryKeyDef =
    { Name = "PK_dbo_Customer"; Columns = ["Id"] }

let private sampleTable : TableId =
    { Schema = "dbo"; Table = "Customer" }

[<Fact>]
let ``T1: ScriptDom CreateTable emit is byte-identical across repeat invocations`` () =
    let fragment =
        ScriptDomBuild.buildCreateTable
            sampleTable sampleColumns (Some samplePk) []
    let runs =
        [ for _ in 1 .. 25 -> ScriptDomGenerate.generateOne fragment ]
    let head = List.head runs
    Assert.All(runs, fun s -> Assert.Equal(head, s))

[<Fact>]
let ``Parse-roundtrip: ScriptDom CreateTable emit re-parses cleanly`` () =
    let fragment =
        ScriptDomBuild.buildCreateTable
            sampleTable sampleColumns (Some samplePk) []
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
        ScriptDomBuild.buildCreateTable
            sampleTable sampleColumns (Some samplePk) []
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

[<Fact>]
let ``ScriptDom CreateTable emit carries every column with its data type`` () =
    let fragment =
        ScriptDomBuild.buildCreateTable
            sampleTable sampleColumns (Some samplePk) []
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
        ScriptDomBuild.buildCreateTable
            sampleTable sampleColumns (Some samplePk) []
    let emitted = ScriptDomGenerate.generateOne fragment
    let reparsed, _ = parseSql emitted
    let script = reparsed :?> TSqlScript
    let stmt = script.Batches.[0].Statements.[0] :?> CreateTableStatement
    let pks =
        stmt.Definition.TableConstraints
        |> Seq.choose (function
            | :? UniqueConstraintDefinition as u when u.IsPrimaryKey -> Some u
            | _ -> None)
        |> List.ofSeq
    Assert.Single(pks) |> ignore
    let pk = List.head pks
    Assert.Equal("PK_dbo_Customer", pk.ConstraintIdentifier.Value)

// ---------------------------------------------------------------------------
// InsertRow — typed AST emission.
// ---------------------------------------------------------------------------

let private sampleCells : CellValue list =
    [
        { Column = "Id"; Type = Integer; Raw = "42" }
        { Column = "Name"; Type = Text; Raw = "Acme Corp" }
        { Column = "Score"; Type = Decimal; Raw = "3.14" }
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
            { Column = "Id"; Type = Integer; Raw = "1" }
            { Column = "Name"; Type = Text; Raw = "" }   // NULL
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
            CreateTable (sampleTable, sampleColumns, Some samplePk, [])
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
            CreateTable (sampleTable, sampleColumns, Some samplePk, [])
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
            CreateTable (sampleTable, sampleColumns, Some samplePk, [])
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
    let enriched = (CanonicalizeIdentity.run sampleCatalog).Value
    let stmts = SsdtDdlEmitter.statements enriched
    let emitted = ScriptDomGenerate.toText stmts
    let reparsed, errors = parseSql emitted
    Assert.Empty(errors)
    Assert.NotNull(reparsed)
