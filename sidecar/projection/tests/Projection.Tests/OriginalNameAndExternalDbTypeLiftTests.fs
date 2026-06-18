module Projection.Tests.OriginalNameAndExternalDbTypeLiftTests

open Xunit
open Projection.Core
open Projection.Adapters.Osm

// ---------------------------------------------------------------------------
// Chapter 4.9 slice β — `Attribute.OriginalName` + `Attribute.ExternalDatabaseType`
// IR fidelity lift. Mirrors the chapter A.0' slice α `Description` lift
// pattern: defensive JSON read on both axes, rowset-path pickup, None
// when the source omits.
// ---------------------------------------------------------------------------

let private parseSync (source: CatalogReader.SnapshotSource) : Result<Catalog> =
    TaskSync.run (fun () -> CatalogReader.parse source)

let private firstKind (c: Catalog) : Kind =
    c.Modules
    |> List.head
    |> fun m -> m.Kinds |> List.head

let private findAttr (name: string) (k: Kind) : Attribute =
    k.Attributes
    |> List.find (fun a -> Name.value a.Name = name)

// ---------------------------------------------------------------------------
// JSON path — both fields present on one attribute, absent on another.
// ---------------------------------------------------------------------------

let private v1FixtureWithRenameAndExternal : string =
    """{
  "exportedAtUtc": "2026-05-17T00:00:00.0000000+00:00",
  "modules": [
    {
      "name": "AppCore",
      "isSystem": false,
      "isActive": true,
      "entities": [
        {
          "name": "Account",
          "physicalName": "OSUSR_APPCORE_ACCOUNT",
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
            },
            {
              "name": "EmailAddress",
              "physicalName": "EMAIL_ADDRESS",
              "originalName": "Email",
              "dataType": "Text",
              "length": 250,
              "precision": null,
              "scale": null,
              "default": null,
              "isMandatory": true,
              "isIdentifier": false,
              "isAutoNumber": false,
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
              "name": "LegacyKey",
              "physicalName": "LEGACY_KEY",
              "originalName": null,
              "dataType": "Text",
              "length": 50,
              "precision": null,
              "scale": null,
              "default": null,
              "isMandatory": false,
              "isIdentifier": false,
              "isAutoNumber": false,
              "isActive": true,
              "isReference": 0,
              "refEntityId": null,
              "refEntity_name": null,
              "refEntity_physicalName": null,
              "reference_deleteRuleCode": null,
              "reference_hasDbConstraint": 0,
              "external_dbType": "NVARCHAR(50)",
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
let ``Slice β: JSON path carries Attribute.OriginalName when source provides it`` () =
    let result = parseSync (CatalogReader.SnapshotJson v1FixtureWithRenameAndExternal)
    match result with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let kind = firstKind catalog
        let renamed = findAttr "EmailAddress" kind
        Assert.Equal(Some "Email", renamed.OriginalName)

[<Fact>]
let ``Slice β: JSON path defaults Attribute.OriginalName to None when source nulls it`` () =
    let result = parseSync (CatalogReader.SnapshotJson v1FixtureWithRenameAndExternal)
    match result with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let kind = firstKind catalog
        let id = findAttr "Id" kind
        Assert.Equal<string option>(None, id.OriginalName)

[<Fact>]
let ``Slice β: JSON path carries Attribute.ExternalDatabaseType when source provides it`` () =
    let result = parseSync (CatalogReader.SnapshotJson v1FixtureWithRenameAndExternal)
    match result with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let kind = firstKind catalog
        let legacy = findAttr "LegacyKey" kind
        Assert.Equal(Some "NVARCHAR(50)", legacy.ExternalDatabaseType)

[<Fact>]
let ``Slice β: JSON path defaults Attribute.ExternalDatabaseType to None when source nulls it`` () =
    let result = parseSync (CatalogReader.SnapshotJson v1FixtureWithRenameAndExternal)
    match result with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let kind = firstKind catalog
        let id = findAttr "Id" kind
        Assert.Equal<string option>(None, id.ExternalDatabaseType)

// ---------------------------------------------------------------------------
// Rowset path — both fields land via `AttributeRow.OriginalName` and
// `AttributeRow.ExternalDatabaseType`.
// ---------------------------------------------------------------------------

let private moduleRow : OssysRowsetTypes.ModuleRow =
    { EspaceId       = 1
      EspaceName     = "AppCore"
      IsSystemModule = false
      IsActive       = true
      EspaceKind     = None
      EspaceSsKey    = None }

let private accountKindRow : OssysRowsetTypes.KindRow =
    { EntityId          = 11
      EspaceId          = 1
      EntityName        = "Account"
      PhysicalTableName = "OSUSR_APPCORE_ACCOUNT"
      DbSchema          = "dbo"
      IsStatic          = false
      IsExternal        = false
      IsSystemEntity    = false
      IsActive          = true
      EntitySsKey       = None
      PrimaryKeySsKey   = None
      Description       = None }

let private mkAttrRow
        (attrId: int)
        (attrName: string)
        (originalName: string option)
        (externalDbType: string option)
        : OssysRowsetTypes.AttributeRow =
    {
        Collation = None
        AttrId              = attrId
        EntityId            = 11
        AttrName            = attrName
        PhysicalCol         = attrName.ToUpperInvariant()
        DataType            = "Text"
        IsMandatory         = true
        IsIdentifier        = false
        IsAutoNumber        = false
        Length              = Some 250
        Precision           = None
        Scale               = None
        AttrSsKey           = None
        IsActive            = true
        Description         = None
        OriginalName        = originalName
        ExternalDatabaseType = externalDbType
        IsComputed           = false
        ComputedDefinition   = None
        DefaultConstraintName = None
        Order                = None
    }

[<Fact>]
let ``Slice β: rowset path carries Attribute.OriginalName from AttributeRow`` () =
    let bundle : OssysRowsetTypes.RowsetBundle =
        { Modules    = [ moduleRow ]
          Kinds      = [ accountKindRow ]
          Attributes = [ mkAttrRow 111 "EmailAddress" (Some "Email") None ]
          References = []; Indexes = []; IndexColumns = []; Triggers = []; ColumnChecks = [] }
    match parseSync (CatalogReader.SnapshotRowsets bundle) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let kind = firstKind catalog
        let attr = findAttr "EmailAddress" kind
        Assert.Equal(Some "Email", attr.OriginalName)

[<Fact>]
let ``Slice β: rowset path carries Attribute.ExternalDatabaseType from AttributeRow`` () =
    let bundle : OssysRowsetTypes.RowsetBundle =
        { Modules    = [ moduleRow ]
          Kinds      = [ accountKindRow ]
          Attributes = [ mkAttrRow 112 "LegacyKey" None (Some "NVARCHAR(50)") ]
          References = []; Indexes = []; IndexColumns = []; Triggers = []; ColumnChecks = [] }
    match parseSync (CatalogReader.SnapshotRowsets bundle) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let kind = firstKind catalog
        let attr = findAttr "LegacyKey" kind
        Assert.Equal(Some "NVARCHAR(50)", attr.ExternalDatabaseType)

[<Fact>]
let ``Slice β: rowset path defaults both fields to None when source omits`` () =
    let bundle : OssysRowsetTypes.RowsetBundle =
        { Modules    = [ moduleRow ]
          Kinds      = [ accountKindRow ]
          Attributes = [ mkAttrRow 113 "Id" None None ]
          References = []; Indexes = []; IndexColumns = []; Triggers = []; ColumnChecks = [] }
    match parseSync (CatalogReader.SnapshotRowsets bundle) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let kind = firstKind catalog
        let attr = findAttr "Id" kind
        Assert.Equal<string option>(None, attr.OriginalName)
        Assert.Equal<string option>(None, attr.ExternalDatabaseType)
