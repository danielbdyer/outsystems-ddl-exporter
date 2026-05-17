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
    IRBuilders.mkAttribute k (Name.create name |> Result.value) Integer

let private mkKindAt (schema: string) (table: string) : Kind =
    let physical = TableId.create schema table |> Result.value
    mkKind (testKey table) (Name.create table |> Result.value) physical [ mkAttr "Id" ]

// ---------------------------------------------------------------------------
// ScriptDom level — buildSetExtendedProperty dispatches on owner.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Slice ε: SchemaProperty emits level0=SCHEMA only`` () =
    let stmt = ScriptDomBuild.buildSetExtendedProperty
                    (SchemaProperty "billing")
                    "MS_Description"
                    (Some "Billing module schema annotation")
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
    let totalSchemaEmits =
        bodies |> List.sumBy (countOccurrences "@level0type = N'SCHEMA'")
    // Each kind has 1 emit (the SCHEMA-level module statement is on
    // the first kind of each schema only, but every kind has its own
    // @level0=SCHEMA from its TABLE-level statements... wait, neither
    // kind has a Description here, so the only @level0=SCHEMA emits
    // are the two module-level ones).
    Assert.Equal(2, totalSchemaEmits)

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
    // No @level0 emission at all (kind has no Description and no
    // ExtendedProperties; module has no ExtendedProperties).
    Assert.DoesNotContain("sp_addextendedproperty", body)
