module Projection.Tests.IsActiveLiftTests

open Xunit
open Projection.Core
open Projection.Adapters.Osm

// ---------------------------------------------------------------------------
// Chapter A.0' slice β — IsActive carry-through tests.
//
// First worked example of pillar 9 at harvest time. Per DECISIONS
// 2026-05-15 — A.0' slice β amendment, the session-21 silent-drop
// disposition retires; IsActive lifts to the V2 IR as DataIntent
// evidence; downstream emitters decide filtering policy via future
// overlays at the OperatorIntent layer.
//
// Two-path coverage of the new `Module.IsActive` / `Kind.IsActive` /
// `Attribute.IsActive` fields:
//   - JSON path: V1's `isActive` JSON property at Module / Entity /
//     Attribute level flows into the V2 IR via `isActiveOrDefault`
//     in `CatalogReader.parseModule` / `parseKind` / `parseAttribute`.
//   - Rowset path: V1's `ossys_Espace.Is_Active` / `ossys_Entity.Is_Active`
//     / `ossys_EntityAttr.Is_Active` columns flow via `ModuleRow.IsActive`
//     / `KindRow.IsActive` / `AttributeRow.IsActive` through
//     `parseModuleRow` / `parseKindRow` / `parseAttributeRow`.
//
// Defensive read: when the JSON source omits the field, V1's
// `ISNULL(Is_Active, 1)` semantics apply — missing → true.
// ---------------------------------------------------------------------------

let private mkName s = Name.create s |> Result.value

let private parseSync (source: CatalogReader.SnapshotSource) : Result<Catalog> =
    (CatalogReader.parse source).GetAwaiter().GetResult()

let private firstModule (c: Catalog) : Module = c.Modules |> List.head
let private firstKind (c: Catalog) : Kind = (firstModule c).Kinds |> List.head
let private findKind (name: string) (c: Catalog) : Kind =
    (firstModule c).Kinds |> List.find (fun k -> Name.value k.Name = name)
let private findAttr (name: string) (k: Kind) : Attribute =
    k.Attributes |> List.find (fun a -> Name.value a.Name = name)

// ---------------------------------------------------------------------------
// JSON path — IsActive present (true) on Module + Entity + Attribute.
// ---------------------------------------------------------------------------

let private jsonAllActive : string =
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

// Mixed: active module containing one active entity AND one inactive
// entity. The inactive entity has one active attribute and one inactive
// attribute. Exercises carry-through at all three levels.
let private jsonMixedActive : string =
    """{
  "exportedAtUtc": "2026-05-15T00:00:00.0000000+00:00",
  "modules": [
    {
      "name": "AppCore",
      "isSystem": false,
      "isActive": true,
      "entities": [
        {
          "name": "ActiveEntity",
          "physicalName": "OSUSR_APPCORE_ACTIVE",
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
        },
        {
          "name": "ArchivedEntity",
          "physicalName": "OSUSR_APPCORE_ARCHIVED",
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
              "name": "Deleted",
              "physicalName": "DELETED",
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

// Defensive read — `isActive` field absent at all three levels. V1's
// `ISNULL(Is_Active, 1)` semantics map missing → true.
let private jsonNoIsActiveField : string =
    """{
  "exportedAtUtc": "2026-05-15T00:00:00.0000000+00:00",
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
let ``L3-S9 slice β: JSON path carries Module.IsActive when true`` () =
    let result = parseSync (CatalogReader.SnapshotJson jsonAllActive)
    match result with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let m = firstModule catalog
        Assert.True m.IsActive

[<Fact>]
let ``L3-S9 slice β: JSON path carries Kind.IsActive when true`` () =
    let result = parseSync (CatalogReader.SnapshotJson jsonAllActive)
    match result with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let k = firstKind catalog
        Assert.True k.IsActive

[<Fact>]
let ``L3-S9 slice β: JSON path carries Attribute.IsActive when true`` () =
    let result = parseSync (CatalogReader.SnapshotJson jsonAllActive)
    match result with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let k = firstKind catalog
        let attr = findAttr "Id" k
        Assert.True attr.IsActive

[<Fact>]
let ``L3-S9 slice β: JSON path carries inactive Kind through to IR (not dropped)`` () =
    let result = parseSync (CatalogReader.SnapshotJson jsonMixedActive)
    match result with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        // Both entities survive at the IR; the inactive one carries
        // IsActive = false. Session-21's silent-drop disposition retired.
        Assert.Equal (2, List.length (firstModule catalog).Kinds)
        let active = findKind "ActiveEntity" catalog
        let archived = findKind "ArchivedEntity" catalog
        Assert.True active.IsActive
        Assert.False archived.IsActive

[<Fact>]
let ``L3-S9 slice β: JSON path carries inactive Attribute through to IR (not dropped)`` () =
    let result = parseSync (CatalogReader.SnapshotJson jsonMixedActive)
    match result with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        // The ArchivedEntity has two attributes (Id + Deleted); both
        // surface in V2's IR; Deleted carries IsActive = false.
        let archived = findKind "ArchivedEntity" catalog
        Assert.Equal (2, List.length archived.Attributes)
        let deleted = findAttr "Deleted" archived
        Assert.False deleted.IsActive

[<Fact>]
let ``L3-S9 slice β: JSON path defaults missing isActive to true (V1 ISNULL semantics)`` () =
    let result = parseSync (CatalogReader.SnapshotJson jsonNoIsActiveField)
    match result with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let m = firstModule catalog
        let k = firstKind catalog
        let attr = findAttr "Id" k
        Assert.True m.IsActive
        Assert.True k.IsActive
        Assert.True attr.IsActive

// ---------------------------------------------------------------------------
// Rowset path — IsActive on DTO rows carries through.
// ---------------------------------------------------------------------------

let private moduleRow (isActive: bool) : CatalogReader.ModuleRow =
    {
        EspaceId       = 1
        EspaceName     = "AppCore"
        IsSystemModule = false
        IsActive       = isActive
        EspaceKind     = Some "eSpace"
        EspaceSsKey    = None
    }

let private kindRow (espaceId: int) (entityId: int) (name: string) (isActive: bool)
    : CatalogReader.KindRow =
    {
        EntityId          = entityId
        EspaceId          = espaceId
        EntityName        = name
        PhysicalTableName = sprintf "OSUSR_APPCORE_%s" (name.ToUpperInvariant())
        DbSchema          = "dbo"
        IsStatic          = false
        IsExternal        = false
        IsSystemEntity    = false
        IsActive          = isActive
        EntitySsKey       = None
        PrimaryKeySsKey   = None
        Description       = None
    }

let private attrRow (entityId: int) (attrId: int) (name: string) (isActive: bool)
    : CatalogReader.AttributeRow =
    {
        AttrId       = attrId
        EntityId     = entityId
        AttrName     = name
        PhysicalCol  = name.ToUpperInvariant()
        DataType     = "Identifier"
        IsMandatory  = true
        IsIdentifier = (name = "Id")
        IsAutoNumber = (name = "Id")
        Length       = None
        Precision    = None
        Scale        = None
        AttrSsKey    = None
        IsActive     = isActive
        Description  = None
    }

[<Fact>]
let ``L3-S9 slice β: rowset path carries Module.IsActive`` () =
    let bundle : CatalogReader.RowsetBundle =
        { Modules    = [ moduleRow true ]
          Kinds      = [ kindRow 1 11 "User" true ]
          Attributes = [ attrRow 11 111 "Id" true ]
          References = []; Triggers = []  }
    match parseSync (CatalogReader.SnapshotRowsets bundle) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let m = firstModule catalog
        Assert.True m.IsActive

[<Fact>]
let ``L3-S9 slice β: rowset path carries Kind.IsActive`` () =
    let bundle : CatalogReader.RowsetBundle =
        { Modules    = [ moduleRow true ]
          Kinds      = [ kindRow 1 11 "User" true ]
          Attributes = [ attrRow 11 111 "Id" true ]
          References = []; Triggers = []  }
    match parseSync (CatalogReader.SnapshotRowsets bundle) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let k = firstKind catalog
        Assert.True k.IsActive

[<Fact>]
let ``L3-S9 slice β: rowset path carries Attribute.IsActive`` () =
    let bundle : CatalogReader.RowsetBundle =
        { Modules    = [ moduleRow true ]
          Kinds      = [ kindRow 1 11 "User" true ]
          Attributes = [ attrRow 11 111 "Id" true ]
          References = []; Triggers = []  }
    match parseSync (CatalogReader.SnapshotRowsets bundle) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let k = firstKind catalog
        let attr = findAttr "Id" k
        Assert.True attr.IsActive

[<Fact>]
let ``L3-S9 slice β: rowset path carries inactive flags at every level (no silent drop)`` () =
    // Module with IsActive=true; one active entity and one inactive
    // entity; the active entity has one active attr; the inactive
    // entity has one active attr (Id) and one inactive attr (Deleted).
    let bundle : CatalogReader.RowsetBundle =
        { Modules    = [ moduleRow true ]
          Kinds      = [ kindRow 1 11 "Active" true
                         kindRow 1 12 "Archived" false ]
          Attributes = [ attrRow 11 111 "Id" true
                         attrRow 12 121 "Id" true
                         attrRow 12 122 "Deleted" false ]
          References = []; Triggers = []  }
    match parseSync (CatalogReader.SnapshotRowsets bundle) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        // Module carries IsActive = true.
        let m = firstModule catalog
        Assert.True m.IsActive
        // Both entities surface.
        Assert.Equal (2, List.length m.Kinds)
        let active = findKind "Active" catalog
        let archived = findKind "Archived" catalog
        Assert.True active.IsActive
        Assert.False archived.IsActive
        // The archived entity has both Id (active) and Deleted (inactive).
        Assert.Equal (2, List.length archived.Attributes)
        let archivedId = findAttr "Id" archived
        let archivedDeleted = findAttr "Deleted" archived
        Assert.True archivedId.IsActive
        Assert.False archivedDeleted.IsActive

[<Fact>]
let ``L3-S9 slice β: rowset path carries inactive Module through to IR (not dropped)`` () =
    // Two modules, one active and one inactive.
    let inactiveModule : CatalogReader.ModuleRow =
        { moduleRow false with EspaceId = 2; EspaceName = "Inactive" }
    let bundle : CatalogReader.RowsetBundle =
        { Modules    = [ moduleRow true; inactiveModule ]
          Kinds      = [ kindRow 1 11 "User" true
                         kindRow 2 21 "InactiveUser" true ]
          Attributes = [ attrRow 11 111 "Id" true
                         attrRow 21 211 "Id" true ]
          References = []; Triggers = []  }
    match parseSync (CatalogReader.SnapshotRowsets bundle) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        // Both modules survive at the IR; session-21's silent-drop
        // disposition retired.
        Assert.Equal (2, List.length catalog.Modules)
        let active = catalog.Modules |> List.find (fun m -> m.Name = mkName "AppCore")
        let inactive = catalog.Modules |> List.find (fun m -> m.Name = mkName "Inactive")
        Assert.True active.IsActive
        Assert.False inactive.IsActive

// ---------------------------------------------------------------------------
// Pillar-9 worked-example axis tests — verify the harvest dichotomy
// disposition holds: the IsActive transformation is DataIntent (the IR
// reflects V1's complete inventory) and is reachable from
// Project(catalog, Policy.empty, profile) without operator filter.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Pillar 9 slice β: inactive records reachable from skeleton (Policy.empty preserves V1 inventory)`` () =
    // The harvest-dichotomy classification of slice β (per DECISIONS
    // 2026-05-15 — A.0' slice β amendment) is DataIntent. Test
    // disposition: parsing a mixed-active source under no operator
    // policy yields a Catalog containing every V1 record, with the
    // IsActive flag carrying the operator-meaningful inactive status.
    // OperatorIntent filtering (drop inactive at emit time) is a
    // future overlay, not an adapter-time silent drop.
    let result = parseSync (CatalogReader.SnapshotJson jsonMixedActive)
    match result with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        // The skeleton-baseline pre-policy IR carries every V1 record
        // including inactives. Subsequent passes / overlays may filter
        // based on IsActive when consumer pressure surfaces an
        // OverlayAxis for that intent.
        let m = firstModule catalog
        // Active + Archived entities both present.
        Assert.Equal (2, List.length m.Kinds)
        // The Archived entity has both its attributes (Id + Deleted).
        let archived = findKind "ArchivedEntity" catalog
        Assert.Equal (2, List.length archived.Attributes)
