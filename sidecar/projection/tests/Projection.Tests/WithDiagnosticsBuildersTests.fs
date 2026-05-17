module Projection.Tests.WithDiagnosticsBuildersTests

open Xunit
open Projection.Core
open Projection.Targets.SSDT

// ---------------------------------------------------------------------------
// Chapter 4.9 slice ζ — Diagnostics-bearing canonical signatures for the
// four ScriptDom builders that previously returned bare AST nodes
// (`buildCreateTable`, `buildSetExtendedProperty`, `buildMergeStatement`,
// `buildUpdateStatement`). The pattern is the chapter 4.7 slice β cash-out
// of "Diagnostics-aware emitter signature": single Diagnostics-bearing
// canonical surface; callers without per-builder Diagnostics consumers
// drop entries explicitly via `.Value` at the call site (sibling-wrapper
// discipline; V2-no-back-compat).
//
// Today every builder returns `Diagnostics.ofValue stmt` (empty entries
// list) because no Diagnostics source exists yet. Future per-builder
// Diagnostics sources (column-default parse failures, level-validation
// rare-form failures, row-literal type-coercion warnings, etc.) flow
// through the writer without re-shaping the signature.
// ---------------------------------------------------------------------------

let private mkTable (schema: string) (table: string) : TableId =
    TableId.create schema table |> Result.value

[<Fact>]
let ``Slice ζ: buildCreateTable returns Diagnostics with empty entries today`` () =
    let table = mkTable "dbo" "Widget"
    let cols : ColumnDef list = []
    let result = ScriptDomBuild.buildCreateTable table cols None []
    Assert.Empty result.Entries
    Assert.NotNull result.Value

[<Fact>]
let ``Slice ζ: buildSetExtendedProperty returns Diagnostics with empty entries today`` () =
    let table = mkTable "dbo" "Widget"
    let result =
        ScriptDomBuild.buildSetExtendedProperty
            (TableProperty table) "MS_Description" (Some "A widget")
    Assert.Empty result.Entries
    Assert.NotNull result.Value

[<Fact>]
let ``Slice ζ: buildSetExtendedProperty on SchemaProperty returns Diagnostics with empty entries today`` () =
    let result =
        ScriptDomBuild.buildSetExtendedProperty
            (SchemaProperty "billing") "Owner" (Some "Team-Billing")
    Assert.Empty result.Entries
    Assert.NotNull result.Value

[<Fact>]
let ``Slice ζ: buildMergeStatement returns Diagnostics with empty entries today`` () =
    let table = mkTable "dbo" "Widget"
    let pkLit : SqlLiteral =
        SqlLiteral.ofRaw Integer "1"
    let args : ScriptDomBuild.MergeBuildArgs =
        {
            Target = table
            AllColumns = [ "Id" ]
            PkColumns = [ "Id" ]
            UpdColumns = []
            Rows = [ [ pkLit ] ]
            CdcAware = false
        }
    let result = ScriptDomBuild.buildMergeStatement args
    Assert.Empty result.Entries
    Assert.NotNull result.Value

[<Fact>]
let ``Slice ζ: buildUpdateStatement returns Diagnostics with empty entries today`` () =
    let table = mkTable "dbo" "Widget"
    let pkLit  : SqlLiteral = SqlLiteral.ofRaw Integer "1"
    let setLit : SqlLiteral = SqlLiteral.ofRaw Text    "renamed"
    let args : ScriptDomBuild.UpdateBuildArgs =
        {
            Target = table
            SetCells = [ ("Name", setLit) ]
            WhereCells = [ ("Id", pkLit) ]
        }
    let result = ScriptDomBuild.buildUpdateStatement args
    Assert.Empty result.Entries
    Assert.NotNull result.Value
