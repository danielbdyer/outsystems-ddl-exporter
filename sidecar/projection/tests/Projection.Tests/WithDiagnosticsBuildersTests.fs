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
    let result = ScriptDomBuild.buildCreateTable table cols None [] [] None
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
            DeleteScope = None
            StagedSource = None
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
            CdcAware = false
        }
    let result = ScriptDomBuild.buildUpdateStatement args
    Assert.Empty result.Entries
    Assert.NotNull result.Value

// ---------------------------------------------------------------------------
// AC-D7 + AC-G4 — the MERGE's `WHEN NOT MATCHED BY SOURCE THEN DELETE` arm is
// ABSENT unless a delete-scope is declared, and when declared it deletes only
// within the scope. The gate ("no unscoped delete") is structural: the only
// way to get a DELETE arm is to pass a `DeleteScope`.
//
// Discriminating: an always-on DELETE arm fails the `None` case (T−S rows
// would be deleted); a never-on arm fails the `Some` case.
// ---------------------------------------------------------------------------

let private renderMerge (args: ScriptDomBuild.MergeBuildArgs) : string =
    let stmt = (ScriptDomBuild.buildMergeStatement args).Value
    ScriptDomGenerate.generateOne (stmt :> Microsoft.SqlServer.TransactSql.ScriptDom.TSqlStatement)

let private mergeArgs (deleteScope: ScriptDomBuild.DeleteScope option) : ScriptDomBuild.MergeBuildArgs =
    {
        Target      = mkTable "dbo" "Widget"
        AllColumns  = [ "Id"; "Tenant"; "Name" ]
        PkColumns   = [ "Id" ]
        UpdColumns  = [ "Tenant"; "Name" ]
        Rows        =
            [ [ SqlLiteral.ofRaw Integer "1"
                SqlLiteral.ofRaw Integer "7"
                SqlLiteral.ofRaw Text    "a" ] ]
        CdcAware    = false
        DeleteScope = deleteScope
        StagedSource = None
    }

// ---------------------------------------------------------------------------
// StagedSource — the error-8623-safe form. `Some "#seed_X"` makes the MERGE (and
// the validate-before-apply guard) draw from a pre-staged `#temp` instead of an
// inline `VALUES` constructor. `None` (the default) is byte-identical.
// ---------------------------------------------------------------------------

[<Fact>]
let ``StagedSource Some: the MERGE draws from the #temp, not an inline VALUES`` () =
    let sql = renderMerge { mergeArgs None with StagedSource = Some "#seed_Widget" }
    Assert.Contains("[#seed_Widget]", sql)     // USING the staged temp table
    // the row literals now live in the #temp, not the MERGE — `N'a'` is the
    // inline Name value, present in the VALUES form, absent in the staged form.
    Assert.DoesNotContain("N'a'", sql)

[<Fact>]
let ``StagedSource None: the MERGE keeps the inline VALUES (byte-identical default)`` () =
    let sql = renderMerge (mergeArgs None)
    Assert.Contains("VALUES", sql)
    Assert.DoesNotContain("#seed", sql)

[<Fact>]
let ``StagedSource Some: the validate-before-apply guard EXCEPTs the #temp, not a VALUES`` () =
    let guard = ScriptDomBuild.buildValidateBeforeApplyGuard { mergeArgs None with StagedSource = Some "#seed_Widget" }
    let sql = ScriptDomGenerate.generateOne guard
    Assert.Contains("[#seed_Widget]", sql)
    Assert.DoesNotContain("VALUES", sql)

[<Fact>]
let ``AC-D7/AC-G4: DeleteScope=None emits NO WHEN NOT MATCHED BY SOURCE arm`` () =
    let rendered = renderMerge (mergeArgs None)
    Assert.DoesNotContain("NOT MATCHED BY SOURCE", rendered)
    Assert.DoesNotContain("DELETE", rendered)

[<Fact>]
let ``AC-D7/AC-G4: DeleteScope=None is byte-identical to the pre-scope MERGE output`` () =
    // The pre-scope MERGE shape: ON-clause + WHEN MATCHED UPDATE +
    // WHEN NOT MATCHED INSERT, with no delete arm. Pinned literally so a
    // regression that quietly added an always-on DELETE arm would break here.
    let rendered = renderMerge (mergeArgs None)
    let expected =
        "MERGE INTO [dbo].[Widget]\n"
        + " AS [Target]\n"
        + "USING (VALUES (1, 7, N'a')) AS [Source]([Id], [Tenant], [Name]) ON [Target].[Id] = [Source].[Id]\n"
        + "WHEN MATCHED THEN UPDATE \n"
        + "    SET [Target].[Tenant] = [Source].[Tenant],\n"
        + "        [Target].[Name]   = [Source].[Name]\n"
        + "WHEN NOT MATCHED THEN INSERT ([Id], [Tenant], [Name]) VALUES ([Source].[Id], [Source].[Tenant], [Source].[Name])"
    Assert.Equal(expected, rendered)

[<Fact>]
let ``AC-D7/AC-G4: a declared DeleteScope emits exactly one scoped WHEN NOT MATCHED BY SOURCE THEN DELETE arm`` () =
    let scope : ScriptDomBuild.DeleteScope =
        { Terms = [ ("Tenant", SqlLiteral.ofRaw Integer "7") ] }
    let rendered = renderMerge (mergeArgs (Some scope))
    // Exactly one DELETE arm.
    let occurrences =
        rendered.Split([| "NOT MATCHED BY SOURCE" |], System.StringSplitOptions.None).Length - 1
    Assert.Equal(1, occurrences)
    // The arm carries the scope predicate and the DELETE action.
    Assert.Contains("WHEN NOT MATCHED BY SOURCE AND [Target].[Tenant] = 7 THEN DELETE", rendered)
    // The non-delete arms remain (the scope arm is additive).
    Assert.Contains("WHEN MATCHED THEN UPDATE", rendered)
    Assert.Contains("WHEN NOT MATCHED THEN INSERT", rendered)

[<Fact>]
let ``AC-D7/AC-G4: a multi-term DeleteScope folds its terms with AND`` () =
    let scope : ScriptDomBuild.DeleteScope =
        { Terms =
            [ ("Tenant", SqlLiteral.ofRaw Integer "7")
              ("Name",   SqlLiteral.ofRaw Text    "a") ] }
    let rendered = renderMerge (mergeArgs (Some scope))
    // The generator wraps long predicates with continuation-indentation;
    // normalize whitespace to assert the folded AND content regardless of
    // line-wrapping.
    let normalized = System.Text.RegularExpressions.Regex.Replace(rendered, "\\s+", " ")
    Assert.Contains(
        "WHEN NOT MATCHED BY SOURCE AND [Target].[Tenant] = 7 AND [Target].[Name] = N'a' THEN DELETE",
        normalized)
