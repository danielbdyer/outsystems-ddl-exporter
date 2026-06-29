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
    let args : MergeBuildArgs =
        {
            Target = table
            AllColumns = [ "Id" ]
            PkColumns = [ "Id" ]
            UpdColumns = []
            Rows = [ [ pkLit ] ]
            CdcAware = false
            DeleteScope = None
            RowSource = MergeRowSource.InlineValues
        }
    let result = ScriptDomBuild.buildMergeStatement args
    Assert.Empty result.Entries
    Assert.NotNull result.Value

[<Fact>]
let ``Slice ζ: buildUpdateStatement returns Diagnostics with empty entries today`` () =
    let table = mkTable "dbo" "Widget"
    let pkLit  : SqlLiteral = SqlLiteral.ofRaw Integer "1"
    let setLit : SqlLiteral = SqlLiteral.ofRaw Text    "renamed"
    let args : UpdateBuildArgs =
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

let private renderMerge (args: MergeBuildArgs) : string =
    let stmt = (ScriptDomBuild.buildMergeStatement args).Value
    ScriptDomGenerate.generateOne (stmt :> Microsoft.SqlServer.TransactSql.ScriptDom.TSqlStatement)

let private mergeArgs (deleteScope: DeleteScope option) : MergeBuildArgs =
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
        RowSource   = MergeRowSource.InlineValues
    }

// ---------------------------------------------------------------------------
// MergeRowSource — the error-8623-safe form. `Staged "#seed_X"` makes the MERGE
// (and the validate-before-apply guard) draw from a pre-staged `#temp` instead
// of an inline `VALUES` constructor. `InlineValues` (the default) is
// byte-identical.
// ---------------------------------------------------------------------------

[<Fact>]
let ``MergeRowSource Staged: the MERGE draws from the #temp, not an inline VALUES`` () =
    let sql = renderMerge { mergeArgs None with RowSource = MergeRowSource.Staged "#seed_Widget" }
    Assert.Contains("[#seed_Widget]", sql)     // USING the staged temp table
    // the row literals now live in the #temp, not the MERGE — `N'a'` is the
    // inline Name value, present in the VALUES form, absent in the staged form.
    Assert.DoesNotContain("N'a'", sql)

[<Fact>]
let ``MergeRowSource InlineValues: the MERGE keeps the inline VALUES (byte-identical default)`` () =
    let sql = renderMerge (mergeArgs None)
    Assert.Contains("VALUES", sql)
    Assert.DoesNotContain("#seed", sql)

[<Fact>]
let ``MergeRowSource Staged: the validate-before-apply guard EXCEPTs the #temp, not a VALUES`` () =
    let guard = ScriptDomBuild.buildValidateBeforeApplyGuard { mergeArgs None with RowSource = MergeRowSource.Staged "#seed_Widget" }
    let sql = ScriptDomGenerate.generateOne guard
    Assert.Contains("[#seed_Widget]", sql)
    Assert.DoesNotContain("VALUES", sql)

// ---------------------------------------------------------------------------
// Statement-DU promotion — the typed `Statement` stream now models the data
// lane's MERGE/UPDATE. `ScriptDomBuild.buildStatement` dispatches `Statement
// .Merge` / `Statement.Update` to the same builders the emitters call directly,
// so the DU path is byte-faithful (the prerequisite for the emitters to emit
// typed `Statement` values instead of rendering MERGE/UPDATE text per-site).
// ---------------------------------------------------------------------------

let private renderViaStatement (s: Statement) : string =
    match ScriptDomBuild.buildStatement s with
    | Some fragment -> ScriptDomGenerate.generateOne fragment
    | None -> failwith "buildStatement returned None for a MERGE/UPDATE data statement"

[<Fact>]
let ``Statement.Merge renders identically to buildMergeStatement (DU dispatch is faithful)`` () =
    let args = mergeArgs None
    Assert.Equal(renderMerge args, renderViaStatement (Statement.Merge args))

[<Fact>]
let ``Statement.Update renders identically to buildUpdateStatement (DU dispatch is faithful)`` () =
    let args : UpdateBuildArgs =
        { Target     = mkTable "dbo" "Widget"
          SetCells   = [ "Name", SqlLiteral.ofRaw Text "a" ]
          WhereCells = [ "Id", SqlLiteral.ofRaw Integer "1" ]
          CdcAware   = false }
    let direct =
        ScriptDomGenerate.generateOne
            ((ScriptDomBuild.buildUpdateStatement args).Value :> Microsoft.SqlServer.TransactSql.ScriptDom.TSqlStatement)
    Assert.Equal(direct, renderViaStatement (Statement.Update args))

// ---------------------------------------------------------------------------
// Staged-source primitives — `buildCreateTempTable` + `buildInsertBatches`
// (the rows that feed the `MERGE … USING #temp`).
// ---------------------------------------------------------------------------

let private renderStmt (stmt: #Microsoft.SqlServer.TransactSql.ScriptDom.TSqlStatement) : string =
    ScriptDomGenerate.generateOne (stmt :> Microsoft.SqlServer.TransactSql.ScriptDom.TSqlStatement)

let private mkCol (name: string) (storage: SqlStorageType) : ColumnDef =
    { Name = name; Type = Integer; SqlStorage = Some storage
      Length = None; Precision = None; Scale = None
      Nullable = true; IsIdentity = false; IsPrimaryKey = false
      DefaultValue = None; DefaultName = None; Computed = None
      Collation = None; Identity = None; Provenance = "" }

[<Fact>]
let ``buildCreateTempTable: a nullable, constraint-free staging heap with target column types`` () =
    let cols = [ mkCol "Id" SqlStorageType.Int; mkCol "Name" (SqlStorageType.NVarChar (SqlLength.Bounded 50)) ]
    let sql = renderStmt (ScriptDomBuild.buildCreateTempTable "#seed_X" cols)
    Assert.Contains("CREATE TABLE [#seed_X]", sql)
    Assert.Contains("[Id]", sql)
    Assert.Contains("INT", sql)
    Assert.Contains("[Name]", sql)
    Assert.Contains("NVARCHAR", sql)
    Assert.DoesNotContain("NOT NULL", sql)        // staging heap — every column nullable
    Assert.DoesNotContain("IDENTITY", sql)        // no identity on the staging table
    Assert.DoesNotContain("PRIMARY KEY", sql)     // no constraints

[<Fact>]
let ``buildInsertBatches: chunks at 1000 rows and targets the #temp`` () =
    let rows = [ for i in 1 .. 2500 -> [ SqlLiteral.ofRaw Integer (string i) ] ]
    let stmts = ScriptDomBuild.buildInsertBatches "#seed_X" [ "Id" ] rows
    Assert.Equal(3, List.length stmts)            // 1000 + 1000 + 500
    let sql = renderStmt (List.head stmts)
    Assert.Contains("INSERT", sql)               // ScriptDom omits the optional INTO
    Assert.Contains("[#seed_X]", sql)
    Assert.Contains("VALUES", sql)

[<Fact>]
let ``buildInsertBatches: empty rows yields no statements`` () =
    Assert.Empty(ScriptDomBuild.buildInsertBatches "#seed_X" [ "Id" ] [])

[<Fact>]
let ``buildUpdateFromTemp: set-based UPDATE FROM the #temp JOIN (CDC adds a WHERE)`` () =
    let sql = ScriptDomGenerate.generateOne (ScriptDomBuild.buildUpdateFromTemp (mkTable "dbo" "Org") "#fk_Org" [ "ParentId" ] [ "Id" ] true)
    Assert.Contains("UPDATE", sql)
    Assert.Contains("[#fk_Org]", sql)        // the staged source
    Assert.Contains("[Target]", sql)
    Assert.Contains("[src]", sql)
    Assert.Contains("WHERE", sql)            // cdcAware → change-detect predicate

[<Fact>]
let ``buildUpdateFromTemp: no CDC means no WHERE (unconditional re-point)`` () =
    let sql = ScriptDomGenerate.generateOne (ScriptDomBuild.buildUpdateFromTemp (mkTable "dbo" "Org") "#fk_Org" [ "ParentId" ] [ "Id" ] false)
    Assert.Contains("UPDATE", sql)
    Assert.DoesNotContain("WHERE", sql)

// -- Tier-2.1: surrogate-capture builders (reverse-leg AssignedBySink) --------
// These were the last raw-`sprintf` SQL on the highest-blast-radius path; now
// typed end to end. The deploy proof is the reverse-leg Docker canary; these
// render-witnesses pin the SQL SHAPE fast (the substring class the plan warns is
// necessary-but-insufficient — paired with the deploy E2E).

[<Fact>]
let ``buildCaptureStaging: SELECT TOP 0 ISNULL-clones the identity AS __SRC_KEY + passthrough INTO the #temp`` () =
    let sql = renderStmt (ScriptDomBuild.buildCaptureStaging "#__projection_capture" (mkTable "dbo" "Org") "Id" [ "Name"; "ParentId" ])
    Assert.Contains("SELECT TOP 0", sql)
    Assert.Contains("ISNULL([Id], [Id]) AS [__SRC_KEY]", sql)   // ISNULL strips IDENTITY (a CASE wrapper would propagate it)
    Assert.Contains("[Name]", sql)
    Assert.Contains("[#__projection_capture]", sql)
    Assert.Contains("[dbo].[Org]", sql)

[<Fact>]
let ``buildKeymapStaging: two ISNULL-clones (__SRC_KEY, __ASSIGNED) INTO the keymap #temp`` () =
    let sql = renderStmt (ScriptDomBuild.buildKeymapStaging "#__projection_keymap" (mkTable "dbo" "Org") "Id")
    Assert.Contains("AS [__SRC_KEY]", sql)
    Assert.Contains("AS [__ASSIGNED]", sql)
    Assert.Contains("[#__projection_keymap]", sql)

[<Fact>]
let ``buildCaptureMerge: ON 1 = 0, INSERT from [S], OUTPUT to the caller (no INTO)`` () =
    let sql = renderStmt (ScriptDomBuild.buildCaptureMerge (mkTable "dbo" "Org") "#__projection_capture" [ "Name"; "ParentId" ] "Id" None)
    Assert.Contains("MERGE INTO [dbo].[Org]", sql)
    Assert.Contains("ON 1 = 0", sql)
    Assert.Contains("WHEN NOT MATCHED THEN INSERT", sql)
    Assert.Contains("VALUES ([S].[Name], [S].[ParentId])", sql)
    Assert.Contains("OUTPUT [S].[__SRC_KEY], [INSERTED].[Id]", sql)
    Assert.DoesNotContain("INTO [#", sql)   // OUTPUT to the caller, NOT INTO a keymap

[<Fact>]
let ``buildCaptureMerge: OUTPUT … INTO the keymap (the trigger-proof rung)`` () =
    let sql = renderStmt (ScriptDomBuild.buildCaptureMerge (mkTable "dbo" "Org") "#__projection_capture" [ "Name" ] "Id" (Some "#__projection_keymap"))
    Assert.Contains("OUTPUT [S].[__SRC_KEY], [INSERTED].[Id] INTO [#__projection_keymap] ([__SRC_KEY], [__ASSIGNED])", sql)

[<Fact>]
let ``buildCaptureMerge: no insertable columns ⇒ INSERT DEFAULT VALUES`` () =
    let sql = renderStmt (ScriptDomBuild.buildCaptureMerge (mkTable "dbo" "Org") "#__projection_capture" [] "Id" None)
    Assert.Contains("INSERT DEFAULT VALUES", sql)
    Assert.Contains("OUTPUT", sql)

[<Fact>]
let ``buildScopeIdentitySelect: SELECT CAST(SCOPE_IDENTITY() AS BIGINT)`` () =
    let sql = renderStmt (ScriptDomBuild.buildScopeIdentitySelect ())
    Assert.Contains("SCOPE_IDENTITY()", sql)
    Assert.Contains("BIGINT", sql)

[<Fact>]
let ``buildInsertDefaultValues: INSERT … DEFAULT VALUES for the identity-only kind`` () =
    let sql = renderStmt (ScriptDomBuild.buildInsertDefaultValues (mkTable "dbo" "Org"))
    Assert.Contains("[dbo].[Org]", sql)
    Assert.Contains("DEFAULT VALUES", sql)

[<Fact>]
let ``buildSelectColumnsFromTemp: SELECT the named columns FROM the #temp (keymap readback)`` () =
    let sql = renderStmt (ScriptDomBuild.buildSelectColumnsFromTemp [ "__SRC_KEY"; "__ASSIGNED" ] "#__projection_keymap")
    Assert.Contains("[__SRC_KEY]", sql)
    Assert.Contains("[__ASSIGNED]", sql)
    Assert.Contains("[#__projection_keymap]", sql)

// -- Tier-2.2: keymap-spill builders (at-scale reverse-leg) -------------------

[<Fact>]
let ``buildKeymapSpillTable: idempotent CREATE of the NVARCHAR(450) keymap with composite PK`` () =
    let sql = renderStmt (ScriptDomBuild.buildKeymapSpillTable "#projection_keymap_spill" "PK_keymap_spill")
    Assert.Contains("IF OBJECT_ID('tempdb..#projection_keymap_spill') IS NULL", sql)
    Assert.Contains("CREATE TABLE [#projection_keymap_spill]", sql)
    Assert.Contains("[KindKey]", sql)
    Assert.Contains("NVARCHAR (450) NOT NULL", sql)
    Assert.Contains("CONSTRAINT [PK_keymap_spill] PRIMARY KEY ([KindKey], [SourceKey])", sql)

[<Fact>]
let ``buildKeymapRepoint: set-based UPDATE…JOIN re-point with the bound kind param and CONVERT`` () =
    let sql = renderStmt (ScriptDomBuild.buildKeymapRepoint (mkTable "dbo" "Customer") "ManagerId" "#projection_keymap_spill" "@kind")
    Assert.Contains("UPDATE", sql)
    Assert.Contains("[s].[ManagerId] = [k].[AssignedKey]", sql)
    Assert.Contains("[dbo].[Customer] AS [s]", sql)
    Assert.Contains("INNER JOIN", sql)
    Assert.Contains("[k].[KindKey] = @kind", sql)
    Assert.Contains("CONVERT (NVARCHAR (450), [s].[ManagerId])", sql)

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
    let scope : DeleteScope =
        DeleteScope.create [ ("Tenant", SqlLiteral.ofRaw Integer "7") ] |> Option.get
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
    let scope : DeleteScope =
        DeleteScope.create
            [ ("Tenant", SqlLiteral.ofRaw Integer "7")
              ("Name",   SqlLiteral.ofRaw Text    "a") ]
        |> Option.get
    let rendered = renderMerge (mergeArgs (Some scope))
    // The generator wraps long predicates with continuation-indentation;
    // normalize whitespace to assert the folded AND content regardless of
    // line-wrapping.
    let normalized = System.Text.RegularExpressions.Regex.Replace(rendered, "\\s+", " ")
    Assert.Contains(
        "WHEN NOT MATCHED BY SOURCE AND [Target].[Tenant] = 7 AND [Target].[Name] = N'a' THEN DELETE",
        normalized)
