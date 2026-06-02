module Projection.Tests.SchemaMigrationEmitterTests

open Xunit
open Projection.Core
open Projection.Targets.SSDT
open Projection.Tests.Fixtures

// 6.A.12 — the implied emission differential. `SchemaMigrationEmitter.emit`
// turns a `CatalogDiff` into minimum-viable-touch DDL: ALTER TABLE … ADD /
// ALTER COLUMN, not a full CREATE. Renames are the RefactorLog channel
// (tested in RefactorLogEmitterTests); destructive / unsupported changes are
// refused fail-loud. Clean-room: `between` (comparison) ≠ `emit` (emission).

type private FsResult<'a, 'b> = Microsoft.FSharp.Core.Result<'a, 'b>

let private mustOk (r: FsResult<'a, EmitError>) : 'a =
    match r with
    | FsResult.Ok v -> v
    | FsResult.Error e -> Assert.Fail(sprintf "%A" e); Unchecked.defaultof<'a>

let private nm (s: string) : Name = Name.create s |> Result.value

let private catalogWithCustomer (customer': Kind) : Catalog =
    let m = { salesModule with Kinds = [ customer'; order; country ] }
    Catalog.create [ m ] [] |> Result.value

let private withCustomerName (f: Attribute -> Attribute) : Catalog =
    let c' =
        { customer with
            Attributes =
                customer.Attributes
                |> List.map (fun a -> if a.SsKey = customerNameKey then f a else a) }
    catalogWithCustomer c'

/// Emit the migration of `sampleCatalog → target`.
let private migrationOf (target: Catalog) : Statement list * DiagnosticEntry list =
    let diff = CatalogDiff.between sampleCatalog target |> mustOk
    let m = SchemaMigrationEmitter.emit diff
    m.Value, m.Entries

let private isCreateTable = function Statement.CreateTable _ -> true | _ -> false
let private alterColumns (stmts: Statement list) =
    stmts |> List.choose (function Statement.AlterTableAlterColumn (_, c) -> Some c | _ -> None)
let private addColumns (stmts: Statement list) =
    stmts |> List.choose (function Statement.AlterTableAddColumn (_, c) -> Some c | _ -> None)

[<Fact>]
let ``migration: a column type change emits an ALTER, not a CREATE`` () =
    let target = withCustomerName (fun a -> { a with Type = Integer })
    let stmts, _ = migrationOf target
    // The minimum-viable-touch is an ALTER COLUMN — never a full CREATE.
    Assert.False(stmts |> List.exists isCreateTable)
    let alters = alterColumns stmts
    Assert.Equal(1, List.length alters)
    Assert.Equal("NAME", (List.head alters).Name)

[<Fact>]
let ``migration: ALTER COLUMN renders as ALTER TABLE ... ALTER COLUMN via ScriptDom`` () =
    let target = withCustomerName (fun a -> { a with Type = Integer })
    let stmts, _ = migrationOf target
    let sb = System.Text.StringBuilder()
    stmts |> List.iter (Render.toSql sb)
    let sql = sb.ToString()
    Assert.Contains("ALTER TABLE", sql)
    Assert.Contains("ALTER COLUMN", sql)
    Assert.DoesNotContain("CREATE TABLE", sql)

[<Fact>]
let ``migration: a new attribute emits ADD COLUMN`` () =
    let newKey = attrKey ["Customer"; "Loyalty"]
    let c' =
        { customer with
            Attributes =
                customer.Attributes
                @ [ { Attribute.create newKey (nm "Loyalty") Integer with
                        Column = ColumnRealization.create ("LOYALTY") (true) |> Result.value } ] }
    let stmts, _ = migrationOf (catalogWithCustomer c')
    let adds = addColumns stmts
    Assert.Equal(1, List.length adds)
    Assert.Equal("LOYALTY", (List.head adds).Name)
    Assert.False(stmts |> List.exists isCreateTable)

[<Fact>]
let ``migration: a dropped column is refused fail-loud (no DROP emitted)`` () =
    let c' =
        { customer with
            Attributes = customer.Attributes |> List.filter (fun a -> a.SsKey <> customerTenantKey) }
    let stmts, entries = migrationOf (catalogWithCustomer c')
    // Nothing emitted for a pure drop — refused, not silently dropped.
    Assert.Empty(stmts)
    Assert.Contains(entries, fun e ->
        e.Code = "migration.destructiveColumnDrop" && e.Severity = DiagnosticSeverity.Error)

[<Fact>]
let ``migration: a DEFAULT-constraint facet change is refused fail-loud`` () =
    // Customer.Name gains a DEFAULT — a DefaultValue facet ALTER COLUMN can't
    // express in one statement; refused (no partial/misleading ALTER).
    let lit = SqlLiteral.ofRaw Text "unknown"
    let target = withCustomerName (fun a -> { a with DefaultValue = Some lit })
    let stmts, entries = migrationOf target
    Assert.Empty(alterColumns stmts)
    Assert.Contains(entries, fun e ->
        e.Code = "migration.unsupportedFacetChange" && e.Severity = DiagnosticSeverity.Error)

[<Fact>]
let ``migration: an identity facet change is refused fail-loud`` () =
    let target = withCustomerName (fun a -> { a with IsIdentity = true })
    let _, entries = migrationOf target
    Assert.Contains(entries, fun e ->
        e.Code = "migration.unsupportedFacetChange" && e.Severity = DiagnosticSeverity.Error)

[<Fact>]
let ``migration: NULL to NOT NULL tightening emits the ALTER with a narrowing Warning`` () =
    // Build source (nullable) → target (not null) directly; the fixture
    // columns are all NOT NULL, so construct the nullable source explicitly.
    let mk (nullable: bool) =
        let c' =
            { customer with
                Attributes =
                    customer.Attributes
                    |> List.map (fun a ->
                        if a.SsKey = customerNameKey then { a with Column = { a.Column with IsNullable = nullable } } else a) }
        catalogWithCustomer c'
    let diff = CatalogDiff.between (mk true) (mk false) |> mustOk
    let m = SchemaMigrationEmitter.emit diff
    Assert.True(m.Value |> List.exists (function Statement.AlterTableAlterColumn _ -> true | _ -> false))
    Assert.Contains(m.Entries, fun e ->
        e.Code = "migration.narrowingColumn" && e.Severity = DiagnosticSeverity.Warning)

[<Fact>]
let ``migration: an identical diff emits no statements and no diagnostics`` () =
    let diff = CatalogDiff.between sampleCatalog sampleCatalog |> mustOk
    let m = SchemaMigrationEmitter.emit diff
    Assert.Empty(m.Value)
    Assert.Empty(m.Entries)

[<Fact>]
let ``migration: a rename alone emits no ALTER (renames are the RefactorLog channel)`` () =
    // Customer.Name's logical name changes (Name → FullName); the column
    // SHAPE is unchanged, so the migration emitter emits nothing — the rename
    // rides the RefactorLog channel, disjoint from the ALTER channel.
    let target = withCustomerName (fun a -> { a with Name = nm "FullName" })
    let stmts, entries = migrationOf target
    Assert.Empty(alterColumns stmts)
    Assert.Empty(addColumns stmts)
    Assert.Empty(entries)
