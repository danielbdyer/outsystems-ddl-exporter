module Projection.Tests.IsActiveCarryThroughTests

open Xunit
open Projection.Core
open Projection.Adapters.Osm

// ---------------------------------------------------------------------------
// Chapter A.0' slice β — IsActive carry-through tests.
//
// Sibling to `DescriptionLiftTests` (slice α). Two-path coverage of
// the new `Module.IsActive` / `Kind.IsActive` / `Attribute.IsActive`
// fields:
//   - JSON path: V1's `isActive` property at Module / Entity / Attribute
//     levels flows into the V2 IR via `isActiveOrDefault` in
//     `CatalogReader.parseModule` / `parseKind` / `parseAttribute`.
//   - Rowset path: V1's `ossys_Espace.Is_Active` / `ossys_Entity.Is_Active`
//     / `ossys_EntityAttr.Is_Active` columns flow via `ModuleRow.IsActive` /
//     `KindRow.IsActive` / `AttributeRow.IsActive` through
//     `parseModuleRow` / `parseKindRow` / `parseAttributeRow`.
//
// Default-true semantic: when the JSON source omits the `isActive`
// property, the adapter mirrors V1's `ISNULL(Is_Active, 1)` SQL
// coercion (`outsystems_metadata_rowsets.sql:94, 116, 239`).
//
// Pillar 9 (harvest-dichotomy) — the source value is `DataIntent`
// evidence; the adapter no longer filters on it. A Selection-axis
// pass that re-applies an inactive-records drop policy is deferred-
// with-trigger per IR-grows-under-evidence. See `DECISIONS 2026-05-16
// (slice β)`.
//
// Carriage-only at slice β. Whether downstream emitters honor
// `IsActive=false` lands per-consumer (no current consumer demands
// suppression).
// ---------------------------------------------------------------------------

let private mkName s = Name.create s |> Result.value

let private parseSync (source: CatalogReader.SnapshotSource) : Result<Catalog> =
    TaskSync.run (fun () -> CatalogReader.parse source)

let private firstModule (c: Catalog) : Module = c.Modules |> List.head

let private firstKind (c: Catalog) : Kind =
    (firstModule c).Kinds |> List.head

let private findAttr (name: string) (k: Kind) : Attribute =
    k.Attributes |> List.find (fun a -> Name.value a.Name = name)

// ---------------------------------------------------------------------------
// JSON path — explicit isActive: false at three levels.
// ---------------------------------------------------------------------------

let private v1FixtureAllActive : string =
    """{
  "exportedAtUtc": "2026-05-16T00:00:00.0000000+00:00",
  "modules": [
    {
      "name": "AppCore",
      "isSystem": false,
      "isActive": true,
      "entities": [
        {
          "name": "User",
          "physicalName": "OSUSR_APPCORE_USER",
          "isStatic": false,
          "isExternal": false,
          "isActive": true,
          "db_catalog": null,
          "db_schema": "dbo",
          "attributes": [
            {
              "name": "Id",
              "physicalName": "ID",
              "originalName": null,
              "dataType": "Identifier",
              "length": null,
              "precision": null,
              "scale": null,
              "default": null,
              "isMandatory": true,
              "isIdentifier": true,
              "isAutoNumber": true,
              "isActive": true,
              "isReference": 0,
              "refEntityId": null,
              "refEntity_name": null,
              "refEntity_physicalName": null,
              "reference_deleteRuleCode": null,
              "reference_hasDbConstraint": 0,
              "external_dbType": null,
              "physical_isPresentButInactive": 0
            }
          ],
          "relationships": [],
          "indexes": [],
          "triggers": []
        }
      ]
    }
  ]
}"""

let private v1FixtureAllInactive : string =
    """{
  "exportedAtUtc": "2026-05-16T00:00:00.0000000+00:00",
  "modules": [
    {
      "name": "Legacy",
      "isSystem": false,
      "isActive": false,
      "entities": [
        {
          "name": "Retired",
          "physicalName": "OSUSR_LEGACY_RETIRED",
          "isStatic": false,
          "isExternal": false,
          "isActive": false,
          "db_catalog": null,
          "db_schema": "dbo",
          "attributes": [
            {
              "name": "Id",
              "physicalName": "ID",
              "originalName": null,
              "dataType": "Identifier",
              "length": null,
              "precision": null,
              "scale": null,
              "default": null,
              "isMandatory": true,
              "isIdentifier": true,
              "isAutoNumber": true,
              "isActive": true,
              "isReference": 0,
              "refEntityId": null,
              "refEntity_name": null,
              "refEntity_physicalName": null,
              "reference_deleteRuleCode": null,
              "reference_hasDbConstraint": 0,
              "external_dbType": null,
              "physical_isPresentButInactive": 0
            },
            {
              "name": "DeletedField",
              "physicalName": "DELETEDFIELD",
              "originalName": null,
              "dataType": "Text",
              "length": 100,
              "precision": null,
              "scale": null,
              "default": null,
              "isMandatory": false,
              "isIdentifier": false,
              "isAutoNumber": false,
              "isActive": false,
              "isReference": 0,
              "refEntityId": null,
              "refEntity_name": null,
              "refEntity_physicalName": null,
              "reference_deleteRuleCode": null,
              "reference_hasDbConstraint": 0,
              "external_dbType": null,
              "physical_isPresentButInactive": 1
            }
          ],
          "relationships": [],
          "indexes": [],
          "triggers": []
        }
      ]
    }
  ]
}"""

// Same shape as v1FixtureAllActive but omits the `isActive` property
// at all three levels. Per V1's `ISNULL(Is_Active, 1)` semantic, the
// adapter coerces absent to `true`.
let private v1FixtureNoIsActiveProperties : string =
    """{
  "exportedAtUtc": "2026-05-16T00:00:00.0000000+00:00",
  "modules": [
    {
      "name": "AppCore",
      "isSystem": false,
      "entities": [
        {
          "name": "User",
          "physicalName": "OSUSR_APPCORE_USER",
          "isStatic": false,
          "isExternal": false,
          "db_catalog": null,
          "db_schema": "dbo",
          "attributes": [
            {
              "name": "Id",
              "physicalName": "ID",
              "originalName": null,
              "dataType": "Identifier",
              "length": null,
              "precision": null,
              "scale": null,
              "default": null,
              "isMandatory": true,
              "isIdentifier": true,
              "isAutoNumber": true,
              "isReference": 0,
              "refEntityId": null,
              "refEntity_name": null,
              "refEntity_physicalName": null,
              "reference_deleteRuleCode": null,
              "reference_hasDbConstraint": 0,
              "external_dbType": null,
              "physical_isPresentButInactive": 0
            }
          ],
          "relationships": [],
          "indexes": [],
          "triggers": []
        }
      ]
    }
  ]
}"""

[<Fact>]
let ``L3-S9 IsActive: JSON path carries Module.IsActive=true when source provides it`` () =
    match parseSync (CatalogReader.SnapshotJson v1FixtureAllActive) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        Assert.True((firstModule catalog).IsActive)

[<Fact>]
let ``L3-S9 IsActive: JSON path carries Kind.IsActive=true when source provides it`` () =
    match parseSync (CatalogReader.SnapshotJson v1FixtureAllActive) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        Assert.True((firstKind catalog).IsActive)

[<Fact>]
let ``L3-S9 IsActive: JSON path carries Attribute.IsActive=true when source provides it`` () =
    match parseSync (CatalogReader.SnapshotJson v1FixtureAllActive) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let idAttr = findAttr "Id" (firstKind catalog)
        Assert.True(idAttr.IsActive)

[<Fact>]
let ``L3-S9 IsActive: JSON path carries inactive records into the IR (slice β retires the session-21 filter)`` () =
    // The carriage-through assertion: explicit `isActive: false` at
    // all three levels survives into the IR with the value preserved.
    // Sibling to the OsmCatalogReaderDifferentialTests mixed-active
    // test, which exercises both true and false at the entity /
    // attribute levels in the same module.
    match parseSync (CatalogReader.SnapshotJson v1FixtureAllInactive) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let m = firstModule catalog
        Assert.False(m.IsActive)
        let kind = m.Kinds |> List.head
        Assert.False(kind.IsActive)
        let deletedField = findAttr "DeletedField" kind
        Assert.False(deletedField.IsActive)

[<Fact>]
let ``L3-S9 IsActive: JSON path defaults to true when source omits the property at all levels`` () =
    // V1's SQL `ISNULL(Is_Active, 1)` coercion is mirrored at the JSON
    // adapter via `isActiveOrDefault`: missing `isActive` → true.
    match parseSync (CatalogReader.SnapshotJson v1FixtureNoIsActiveProperties) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let m = firstModule catalog
        Assert.True(m.IsActive)
        let kind = m.Kinds |> List.head
        Assert.True(kind.IsActive)
        let idAttr = findAttr "Id" kind
        Assert.True(idAttr.IsActive)

// ---------------------------------------------------------------------------
// Rowset path — IsActive on DTO rows carried into the IR.
// ---------------------------------------------------------------------------

let private moduleRowWith (isActive: bool) (espaceId: int) (espaceName: string)
        : OssysRowsetTypes.ModuleRow =
    {
        EspaceId       = espaceId
        EspaceName     = espaceName
        IsSystemModule = false
        IsActive       = isActive
        EspaceKind     = Some "eSpace"
        EspaceSsKey    = None
    }

let private kindRowWith (isActive: bool) (entityId: int) (espaceId: int) (entityName: string)
        : OssysRowsetTypes.KindRow =
    {
        EntityId          = entityId
        EspaceId          = espaceId
        EntityName        = entityName
        PhysicalTableName = sprintf "OSUSR_%s" (entityName.ToUpperInvariant())
        DbSchema          = "dbo"
        IsStatic          = false
        IsExternal        = false
        IsSystemEntity    = false
        IsActive          = isActive
        EntitySsKey       = None
        PrimaryKeySsKey   = None
        Description       = None
    }

let private attrRowWith (isActive: bool) (attrId: int) (entityId: int) (attrName: string)
        : OssysRowsetTypes.AttributeRow =
    {
        AttrId       = attrId
        EntityId     = entityId
        AttrName     = attrName
        PhysicalCol  = attrName.ToUpperInvariant()
        DataType     = "Identifier"
        IsMandatory  = true
        IsIdentifier = true
        IsAutoNumber = true
        Length       = None
        Precision    = None
        Scale        = None
        AttrSsKey    = None
        IsActive     = isActive
        Description  = None
        OriginalName = None
        ExternalDatabaseType = None
        IsComputed = false
        ComputedDefinition = None
        DefaultConstraintName = None
    }

[<Fact>]
let ``L3-S9 IsActive: rowset path carries inactive Module into the IR`` () =
    let bundle : OssysRowsetTypes.RowsetBundle =
        { Modules    = [ moduleRowWith false 1 "Legacy" ]
          Kinds      = [ kindRowWith true 11 1 "Retired" ]
          Attributes = [ attrRowWith true 111 11 "Id" ]
          References = []; Indexes = []; IndexColumns = []; Triggers = []; ColumnChecks = [] }
    match parseSync (CatalogReader.SnapshotRowsets bundle) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        Assert.False((firstModule catalog).IsActive)

[<Fact>]
let ``L3-S9 IsActive: rowset path carries inactive Kind into the IR`` () =
    let bundle : OssysRowsetTypes.RowsetBundle =
        { Modules    = [ moduleRowWith true 1 "AppCore" ]
          Kinds      = [ kindRowWith false 11 1 "Retired" ]
          Attributes = [ attrRowWith true 111 11 "Id" ]
          References = []; Indexes = []; IndexColumns = []; Triggers = []; ColumnChecks = [] }
    match parseSync (CatalogReader.SnapshotRowsets bundle) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        Assert.False((firstKind catalog).IsActive)

[<Fact>]
let ``L3-S9 IsActive: rowset path carries inactive Attribute into the IR`` () =
    let bundle : OssysRowsetTypes.RowsetBundle =
        { Modules    = [ moduleRowWith true 1 "AppCore" ]
          Kinds      = [ kindRowWith true 11 1 "User" ]
          Attributes = [ attrRowWith true 111 11 "Id"
                         attrRowWith false 112 11 "DeletedField" ]
          References = []; Indexes = []; IndexColumns = []; Triggers = []; ColumnChecks = [] }
    match parseSync (CatalogReader.SnapshotRowsets bundle) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let kind = firstKind catalog
        Assert.Equal(2, List.length kind.Attributes)
        let deleted = findAttr "DeletedField" kind
        Assert.False deleted.IsActive

[<Fact>]
let ``L3-S9 IsActive: cross-source parity — same V1 values produce identical IR on both paths`` () =
    // Sibling to chapter 3.2's cross-source parity property:
    // structurally-equivalent rowset and JSON inputs produce
    // structurally-equivalent catalogs. For slice β the load-bearing
    // axis is `IsActive` carriage at all three levels.
    let bundle : OssysRowsetTypes.RowsetBundle =
        { Modules    = [ moduleRowWith true 1 "AppCore" ]
          Kinds      = [ kindRowWith true 11 1 "User" ]
          Attributes = [ attrRowWith true 111 11 "Id" ]
          References = []; Indexes = []; IndexColumns = []; Triggers = []; ColumnChecks = [] }
    let resJson = parseSync (CatalogReader.SnapshotJson v1FixtureAllActive)
    let resRow  = parseSync (CatalogReader.SnapshotRowsets bundle)
    match resJson, resRow with
    | Ok cJson, Ok cRow ->
        let mJson = firstModule cJson
        let mRow  = firstModule cRow
        Assert.Equal(mJson.IsActive, mRow.IsActive)
        Assert.Equal((mJson.Kinds.[0]).IsActive, (mRow.Kinds.[0]).IsActive)
        let attrJson = findAttr "Id" mJson.Kinds.[0]
        let attrRow  = findAttr "Id" mRow.Kinds.[0]
        Assert.Equal(attrJson.IsActive, attrRow.IsActive)
    | _ ->
        Assert.Fail(sprintf "Expected Ok from both; got JSON=%A, Rowset=%A" resJson resRow)
