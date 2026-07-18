module Projection.Tests.DescriptionLiftTests

open Xunit
open Projection.Core
open Projection.Adapters.Osm

// ---------------------------------------------------------------------------
// Chapter A.0' slice α — Description lift tests.
//
// Two-path coverage of the new `Kind.Description` and
// `Attribute.Description` fields:
//   - JSON path: V1's `description` JSON property at Entity / Attribute
//     level flows into the V2 IR via `getOptionalString` in
//     `CatalogReader.parseKind` / `parseAttribute`.
//   - Rowset path: V1's `ossys_Entity.Description` /
//     `ossys_EntityAttr.Description` columns flow via
//     `KindRow.Description` / `AttributeRow.Description` through
//     `parseKindRow` / `parseAttributeRow`.
//
// Defensive read: when the source omits the field (JSON without the
// property; rowset DTO with `Description = None`), the IR carries
// `Description = None`. Per the L3-Boundary-NoSilentDrop completion
// criterion (chapter A.0' completion), the absence path is verified
// alongside the present path.
//
// Carriage-only at slice α. Emission of descriptions (extended
// properties DDL; .dacpac metadata) lands when chapter 4.1.A slice 8
// catches up; the `CommentMetadataUnreflected` Tolerance variant in
// `Projection.Core.Tolerance` retires at that point.
// ---------------------------------------------------------------------------

let private mkName s = Name.create s |> Result.value

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
// JSON path — descriptions present on Entity + Attribute.
// ---------------------------------------------------------------------------

let private v1FixtureWithDescriptions : string =
    """{
  "exportedAtUtc": "2026-05-15T00:00:00.0000000+00:00",
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
          "description": "Platform user; carries cross-application identity.",
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
              "description": "Surrogate primary key.",
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
              "name": "Email",
              "physicalName": "EMAIL",
              "originalName": null,
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

// Same shape as `v1FixtureWithDescriptions` but without any
// `description` properties — the defensive-read path. The adapter's
// `getOptionalString` returns `Ok None` for absent fields; the IR
// receives `Description = None`.
let private v1FixtureWithoutDescriptions : string =
    """{
  "exportedAtUtc": "2026-05-15T00:00:00.0000000+00:00",
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

[<Fact>]
let ``L3-S9: JSON path carries Kind.Description when source provides it`` () =
    let result = parseSync (CatalogReader.SnapshotJson v1FixtureWithDescriptions)
    match result with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let kind = firstKind catalog
        Assert.Equal(
            Some "Platform user; carries cross-application identity.",
            kind.Description)

[<Fact>]
let ``L3-S9: JSON path carries Attribute.Description when source provides it`` () =
    let result = parseSync (CatalogReader.SnapshotJson v1FixtureWithDescriptions)
    match result with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let kind = firstKind catalog
        let idAttr = findAttr "Id" kind
        Assert.Equal(Some "Surrogate primary key.", idAttr.Description)

[<Fact>]
let ``L3-S9: JSON path defaults Description to None when source omits it`` () =
    // Defensive read — `getOptionalString` returns Ok None when the
    // property is absent. Both Kind and any-attribute lacking
    // `description` end with `Description = None`.
    let result = parseSync (CatalogReader.SnapshotJson v1FixtureWithoutDescriptions)
    match result with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let kind = firstKind catalog
        Assert.Equal<string option>(None, kind.Description)
        let idAttr = findAttr "Id" kind
        Assert.Equal<string option>(None, idAttr.Description)

[<Fact>]
let ``L3-S9: JSON path Email attribute without description stays None on a mixed fixture`` () =
    // The mixed fixture has a description on Id but NOT on Email.
    // Defensive parsing means Email's Description is None while Id's
    // is Some.
    let result = parseSync (CatalogReader.SnapshotJson v1FixtureWithDescriptions)
    match result with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let kind = firstKind catalog
        let emailAttr = findAttr "Email" kind
        Assert.Equal<string option>(None, emailAttr.Description)

// ---------------------------------------------------------------------------
// Rowset path — descriptions on DTO rows.
// ---------------------------------------------------------------------------

let private moduleRow : OssysRowsetTypes.ModuleRow =
    {
        EspaceId       = 1
        EspaceName     = "AppCore"
        IsSystemModule = false
        IsActive       = true
        EspaceKind     = Some "eSpace"
        EspaceSsKey    = None
    }

let private kindRowWith (description: string option) : OssysRowsetTypes.KindRow =
    {
        EntityId          = 11
        EspaceId          = 1
        EntityName        = "User"
        PhysicalTableName = "OSUSR_APPCORE_USER"
        DbSchema          = "dbo"
        IsStatic          = false
        IsExternal        = false
        IsSystemEntity    = false
        IsActive          = true
        EntitySsKey       = None
        PrimaryKeySsKey   = None
        Description       = description
    }

let private idAttrRowWith (description: string option) : OssysRowsetTypes.AttributeRow =
    {
        Collation = None
        AttrId       = 111
        EntityId     = 11
        AttrName     = "Id"
        PhysicalCol  = "ID"
        DataType     = "Identifier"
        DefaultValue = None
        IsMandatory  = true
        IsIdentifier = true
        IsAutoNumber = true
        Length       = None
        Precision    = None
        Scale        = None
        AttrSsKey    = None
        IsActive     = true
        Description  = description
        OriginalName = None
        ExternalDatabaseType = None
        IsComputed = false
        ComputedDefinition = None
        DefaultConstraintName = None
        DeployedStorage = None; DeployedIsNullable = None; IsPersisted = false
        Order = None
    }

[<Fact>]
let ``L3-S9: rowset path carries Kind.Description from KindRow`` () =
    let bundle : OssysRowsetTypes.RowsetBundle =
        { Modules    = [ moduleRow ]
          Kinds      = [ kindRowWith (Some "Carried through the rowset path.") ]
          Attributes = [ idAttrRowWith None ]
          References = []; Indexes = []; IndexColumns = []; Triggers = []; ColumnChecks = []; Sequences = []; Temporal = [] }
    let result = parseSync (CatalogReader.SnapshotRowsets bundle)
    match result with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let kind = firstKind catalog
        Assert.Equal(Some "Carried through the rowset path.", kind.Description)

[<Fact>]
let ``L3-S9: rowset path carries Attribute.Description from AttributeRow`` () =
    let bundle : OssysRowsetTypes.RowsetBundle =
        { Modules    = [ moduleRow ]
          Kinds      = [ kindRowWith None ]
          Attributes = [ idAttrRowWith (Some "Surrogate primary key (rowset).") ]
          References = []; Indexes = []; IndexColumns = []; Triggers = []; ColumnChecks = []; Sequences = []; Temporal = [] }
    let result = parseSync (CatalogReader.SnapshotRowsets bundle)
    match result with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let kind = firstKind catalog
        let idAttr = findAttr "Id" kind
        Assert.Equal(Some "Surrogate primary key (rowset).", idAttr.Description)

[<Fact>]
let ``L3-S9: rowset path None Description survives roundtrip`` () =
    let bundle : OssysRowsetTypes.RowsetBundle =
        { Modules    = [ moduleRow ]
          Kinds      = [ kindRowWith None ]
          Attributes = [ idAttrRowWith None ]
          References = []; Indexes = []; IndexColumns = []; Triggers = []; ColumnChecks = []; Sequences = []; Temporal = [] }
    let result = parseSync (CatalogReader.SnapshotRowsets bundle)
    match result with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let kind = firstKind catalog
        Assert.Equal<string option>(None, kind.Description)
        let idAttr = findAttr "Id" kind
        Assert.Equal<string option>(None, idAttr.Description)
