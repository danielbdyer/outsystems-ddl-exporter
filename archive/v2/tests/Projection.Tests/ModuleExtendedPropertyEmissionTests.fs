module Projection.Tests.ModuleExtendedPropertyEmissionTests

open Xunit
open Projection.Core
open Projection.Targets.SSDT
open Projection.Tests.Fixtures
open Projection.Tests.IRBuilders

// ---------------------------------------------------------------------------
// Chapter 4.9 slice ε — Module.ExtendedProperties emission (SCHEMA-level
// `sp_addextendedproperty`). Multi-level `buildSetExtendedProperty`:
// the `ExtendedPropertyOwner` DU now admits a SchemaProperty variant
// that emits only `@level0type=N'SCHEMA', @level0name=N'<schema>'`
// (no @level1, no @level2). Per-module emission per distinct schema in
// the module's kinds; deterministically gated to the first kind of
// each schema so the statement emits exactly once per (module, schema).
// ---------------------------------------------------------------------------

let private mkAttr (name: string) : Attribute =
    let k = testKey name
    Attribute.create k (Name.create name |> Result.value) Integer

let private mkKindAt (schema: string) (table: string) : Kind =
    let physical = TableId.create schema table |> Result.value
    Kind.create (testKey table) (Name.create table |> Result.value) physical [ mkAttr "Id" ]

// ---------------------------------------------------------------------------
// ScriptDom level — buildSetExtendedProperty dispatches on owner.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Slice ε: SchemaProperty emits level0=SCHEMA only`` () =
    let stmt =
        (ScriptDomBuild.buildSetExtendedProperty
            (SchemaProperty "billing")
            "MS_Description"
            (Some "Billing module schema annotation")).Value
    Assert.NotNull stmt
    let sql = ScriptDomGenerate.toText (seq { Statement.SetExtendedProperty (SchemaProperty "billing", "MS_Description", Some "Billing module schema annotation") })
    Assert.Contains("@level0type = N'SCHEMA'", sql)
    Assert.Contains("@level0name = N'billing'", sql)
    Assert.DoesNotContain("@level1type", sql)
    Assert.DoesNotContain("@level2type", sql)

[<Fact>]
let ``Slice ε: TableProperty preserves prior level0+level1 shape`` () =
    let table = TableId.create "dbo" "T_Widget" |> Result.value
    let sql = ScriptDomGenerate.toText (seq { Statement.SetExtendedProperty (TableProperty table, "MS_Description", Some "Widget table") })
    Assert.Contains("@level0type = N'SCHEMA'", sql)
    Assert.Contains("@level0name = N'dbo'", sql)
    Assert.Contains("@level1type = N'TABLE'", sql)
    Assert.Contains("@level1name = N'T_Widget'", sql)
    Assert.DoesNotContain("@level2type", sql)

[<Fact>]
let ``Slice ε: ColumnProperty + IndexProperty preserve prior level2 shape`` () =
    let table = TableId.create "dbo" "T_Widget" |> Result.value
    let colSql = ScriptDomGenerate.toText (seq { Statement.SetExtendedProperty (ColumnProperty (table, "Code"), "MS_Description", Some "Widget code") })
    Assert.Contains("@level2type = N'COLUMN'", colSql)
    Assert.Contains("@level2name = N'Code'", colSql)
    let idxSql = ScriptDomGenerate.toText (seq { Statement.SetExtendedProperty (IndexProperty (table, "UQ_T_Widget_Code"), "MS_Description", Some "Code index") })
    Assert.Contains("@level2type = N'INDEX'", idxSql)
    Assert.Contains("@level2name = N'UQ_T_Widget_Code'", idxSql)

// ---------------------------------------------------------------------------
// Emitter level — SsdtDdlEmitter.emitSlices emits Module.ExtendedProperties
// per (module, schema) at the first kind of each schema only.
// ---------------------------------------------------------------------------

let private moduleEpA : ExtendedProperty =
    { Name = "MS_Description"; Value = Some "Module-level annotation A" }

let private moduleEpB : ExtendedProperty =
    { Name = "Owner"; Value = Some "Team-Billing" }

[<Fact>]
let ``Slice ε: emits Module.ExtendedProperties per distinct schema in the module`` () =
    // Two kinds in different schemas; module-level extended properties
    // emit once per schema.
    let kind1 = mkKindAt "dbo" "T1"
    let kind2 = mkKindAt "billing" "T2"
    let m =
        { mkModule (testKey "M") (Name.create "M" |> Result.value) [ kind1; kind2 ] with
            ExtendedProperties = [ moduleEpA ] }
    let catalog = mkCatalog [ m ]
    let bundle =
        match SsdtDdlEmitter.emitSlices catalog with
        | Ok files -> files
        | Error err -> failwithf "emitSlices error: %A" err
    let bodies =
        bundle
        |> ArtifactByKind.toMap
        |> Map.toList
        |> List.map (fun (_, f) -> f.Body)
    // The module's extended property emits once per distinct schema:
    // total occurrences across all bundle bodies should be 2 (one per
    // schema dbo + billing).
    let countOccurrences (needle: string) (text: string) : int =
        let mutable n = 0
        let mutable i = 0
        while i >= 0 do
            i <- text.IndexOf(needle, i + 1)
            if i >= 0 then n <- n + 1
        n
    // Slice D.1.b — every table also emits SCHEMA-level segments
    // via its V2.LogicalName extended property (table-level properties
    // are SCHEMA → TABLE), so counting raw @level0=SCHEMA hits no
    // longer isolates the module-property contribution. The
    // distinguishing signal: how many times the module-property's
    // distinctive value ("Module-level annotation A") appears across
    // all bundle bodies — once per distinct schema the module spans.
    let totalModuleAnnotationEmits =
        bodies |> List.sumBy (countOccurrences "Module-level annotation A")
    Assert.Equal(2, totalModuleAnnotationEmits)

[<Fact>]
let ``Slice ε: emits Module.ExtendedProperties exactly once per (module, schema)`` () =
    // Two kinds in the same schema → module-level extended properties
    // emit on the alphabetically first kind only.
    let kindA = mkKindAt "dbo" "AAA_First"
    let kindB = mkKindAt "dbo" "ZZZ_Last"
    let m =
        { mkModule (testKey "M") (Name.create "M" |> Result.value) [ kindA; kindB ] with
            ExtendedProperties = [ moduleEpA; moduleEpB ] }
    let catalog = mkCatalog [ m ]
    let bundle =
        match SsdtDdlEmitter.emitSlices catalog with
        | Ok files -> files
        | Error err -> failwithf "emitSlices error: %A" err
    let countOccurrences (needle: string) (text: string) : int =
        let mutable n = 0
        let mutable i = 0
        while i >= 0 do
            i <- text.IndexOf(needle, i + 1)
            if i >= 0 then n <- n + 1
        n
    let totalModuleAnnotationEmits =
        bundle
        |> ArtifactByKind.toMap
        |> Map.toList
        |> List.sumBy (fun (_, f) -> countOccurrences "Module-level annotation A" f.Body)
    Assert.Equal(1, totalModuleAnnotationEmits)
    // Owner property also emits exactly once.
    let totalOwnerEmits =
        bundle
        |> ArtifactByKind.toMap
        |> Map.toList
        |> List.sumBy (fun (_, f) -> countOccurrences "Team-Billing" f.Body)
    Assert.Equal(1, totalOwnerEmits)

[<Fact>]
let ``Slice ε: Module with empty ExtendedProperties emits no SCHEMA-only statements`` () =
    let kind1 = mkKindAt "dbo" "T1"
    let m = mkModule (testKey "M") (Name.create "M" |> Result.value) [ kind1 ]
    let catalog = mkCatalog [ m ]
    let bundle =
        match SsdtDdlEmitter.emitSlices catalog with
        | Ok files -> files
        | Error err -> failwithf "emitSlices error: %A" err
    let body =
        bundle
        |> ArtifactByKind.toMap
        |> Map.toList
        |> List.head
        |> fun (_, f) -> f.Body
    // Slice D.1.b — V2.LogicalName extended properties emit
    // unconditionally per kind / attribute; sp_addextendedproperty
    // calls always appear in the body. The narrower-by-D.1.b assertion:
    // no SCHEMA-only (table-omitted) statements appear, i.e., every
    // sp_addextendedproperty call has a `@level1type = N'TABLE'`
    // segment in the same statement. Walk the body and confirm.
    let mutable cursor = 0
    let mutable allHaveLevel1 = true
    while cursor >= 0 do
        cursor <- body.IndexOf("sp_addextendedproperty", cursor + 1, System.StringComparison.Ordinal)
        if cursor >= 0 then
            let stmtEnd =
                let next = body.IndexOf("EXECUTE", cursor + 1, System.StringComparison.Ordinal)
                if next < 0 then body.Length else next
            let stmt = body.Substring(cursor, stmtEnd - cursor)
            if not (stmt.Contains("@level1type = N'TABLE'")) then
                allHaveLevel1 <- false
    Assert.True(
        allHaveLevel1,
        "every sp_addextendedproperty call should target a TABLE (SCHEMA-only forms require module-level ExtendedProperties, which this fixture omits)")
