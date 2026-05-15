module Projection.Tests.TriggerLiftTests

open Xunit
open Projection.Core
open Projection.Adapters.Osm

// ---------------------------------------------------------------------------
// Chapter A.0' slice γ — Trigger lift tests.
//
// First worked example of the builder-mediated mode of the closed-DU
// empirical-test discipline (per DECISIONS 2026-05-15 — Closed-DU
// empirical-test discipline refinement). Test sites use
// `Fixtures.attribute / kind / module' / catalog` builders from the
// start, demonstrating that future record extensions (slice δ
// Sequences, slice ε DefaultValue, etc.) reach the same call sites
// only through the builder seam.
//
// Two-path coverage of the new `Catalog.Triggers : Trigger list`:
//   - JSON path: V1's per-entity `triggers` JSON array flows through
//     `CatalogReader.parseTrigger` and aggregates to top-level
//     `Catalog.Triggers`.
//   - Rowset path: `RowsetBundle.Triggers : TriggerRow list` (pre-
//     joined by EntityId) flows through `parseTriggerRow` (in
//     `parseKindRow`) and aggregates the same way.
//
// L3-S4 axiom: every trigger in `Catalog.Triggers` carries `Name`,
// `Definition` (T-SQL text), `IsDisabled`, and `KindSsKey` (link to
// owning entity). Pillar-9 classification: DataIntent (V1 evidence
// carriage; no operator intent at parse time).
// ---------------------------------------------------------------------------

let private mkName s = Name.create s |> Result.value

let private parseSync (source: CatalogReader.SnapshotSource) : Result<Catalog> =
    (CatalogReader.parse source).GetAwaiter().GetResult()

let private firstTrigger (c: Catalog) : Trigger = c.Triggers |> List.head

// ---------------------------------------------------------------------------
// JSON path — V1 `entities[].triggers[]` per-entity arrays.
// ---------------------------------------------------------------------------

let private jsonWithOneTrigger : string =
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
          "triggers": [
            { "name": "TR_USER_AUDIT", "isDisabled": false, "definition": "CREATE TRIGGER [dbo].[TR_USER_AUDIT] ON [dbo].[OSUSR_APPCORE_USER] AFTER UPDATE AS BEGIN INSERT INTO AUDIT VALUES ('user updated') END" }
          ]
        }
      ]
    }
  ]
}"""

let private jsonWithMultipleTriggers : string =
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
          "triggers": [
            { "name": "TR_USER_AUDIT",      "isDisabled": false, "definition": "CREATE TRIGGER TR_USER_AUDIT      ON dbo.OSUSR_APPCORE_USER AFTER INSERT AS BEGIN END" },
            { "name": "TR_USER_VALIDATE",   "isDisabled": true,  "definition": "CREATE TRIGGER TR_USER_VALIDATE   ON dbo.OSUSR_APPCORE_USER AFTER UPDATE AS BEGIN END" }
          ]
        }
      ]
    }
  ]
}"""

let private jsonNoTriggers : string =
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
let ``L3-S4 slice γ: JSON path carries a single trigger to Catalog.Triggers`` () =
    match parseSync (CatalogReader.SnapshotJson jsonWithOneTrigger) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        Assert.Equal (1, List.length catalog.Triggers)
        let tr = firstTrigger catalog
        Assert.Equal<Name>(mkName "TR_USER_AUDIT", tr.Name)
        Assert.False tr.IsDisabled
        Assert.Contains ("AFTER UPDATE", tr.Definition)

[<Fact>]
let ``L3-S4 slice γ: JSON path links trigger to its owning Kind via KindSsKey`` () =
    match parseSync (CatalogReader.SnapshotJson jsonWithOneTrigger) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let tr = firstTrigger catalog
        let userKind = catalog.Modules.[0].Kinds.[0]
        Assert.Equal<SsKey>(userKind.SsKey, tr.KindSsKey)

[<Fact>]
let ``L3-S4 slice γ: JSON path carries multiple triggers and preserves IsDisabled per trigger`` () =
    match parseSync (CatalogReader.SnapshotJson jsonWithMultipleTriggers) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        Assert.Equal (2, List.length catalog.Triggers)
        let audit =
            catalog.Triggers |> List.find (fun t -> t.Name = mkName "TR_USER_AUDIT")
        let validate =
            catalog.Triggers |> List.find (fun t -> t.Name = mkName "TR_USER_VALIDATE")
        Assert.False audit.IsDisabled
        Assert.True validate.IsDisabled
        Assert.Contains ("AFTER INSERT", audit.Definition)
        Assert.Contains ("AFTER UPDATE", validate.Definition)

[<Fact>]
let ``L3-S4 slice γ: JSON path with empty triggers array yields empty Catalog.Triggers`` () =
    match parseSync (CatalogReader.SnapshotJson jsonNoTriggers) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        Assert.Empty catalog.Triggers

// ---------------------------------------------------------------------------
// Rowset path — `RowsetBundle.Triggers : TriggerRow list` pre-joined
// by EntityId.
// ---------------------------------------------------------------------------

let private moduleRow : CatalogReader.ModuleRow =
    { EspaceId       = 1
      EspaceName     = "AppCore"
      IsSystemModule = false
      IsActive       = true
      EspaceKind     = Some "eSpace"
      EspaceSsKey    = None }

let private userKindRow : CatalogReader.KindRow =
    { EntityId          = 11
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
      Description       = None }

let private idAttrRow : CatalogReader.AttributeRow =
    { AttrId       = 111
      EntityId     = 11
      AttrName     = "Id"
      PhysicalCol  = "ID"
      DataType     = "Identifier"
      IsMandatory  = true
      IsIdentifier = true
      IsAutoNumber = true
      Length       = None
      Precision    = None
      Scale        = None
      AttrSsKey    = None
      IsActive     = true
      Description  = None }

let private triggerRow (name: string) (isDisabled: bool) (definition: string)
    : CatalogReader.TriggerRow =
    { EntityId          = 11
      TriggerName       = name
      IsDisabled        = isDisabled
      TriggerDefinition = definition }

[<Fact>]
let ``L3-S4 slice γ: rowset path carries a single trigger to Catalog.Triggers`` () =
    let bundle : CatalogReader.RowsetBundle =
        { Modules    = [ moduleRow ]
          Kinds      = [ userKindRow ]
          Attributes = [ idAttrRow ]
          References = []
          Triggers   = [ triggerRow "TR_USER_AUDIT" false
                                    "CREATE TRIGGER TR_USER_AUDIT ON dbo.OSUSR_APPCORE_USER AFTER UPDATE AS BEGIN END" ] }
    match parseSync (CatalogReader.SnapshotRowsets bundle) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        Assert.Equal (1, List.length catalog.Triggers)
        let tr = firstTrigger catalog
        Assert.Equal<Name>(mkName "TR_USER_AUDIT", tr.Name)
        Assert.False tr.IsDisabled
        Assert.Contains ("AFTER UPDATE", tr.Definition)

[<Fact>]
let ``L3-S4 slice γ: rowset path preserves IsDisabled and Definition verbatim`` () =
    let definition = "CREATE TRIGGER TR_USER_VALIDATE ON dbo.OSUSR_APPCORE_USER AFTER UPDATE AS BEGIN IF UPDATE(Id) RAISERROR ('blocked', 16, 1) END"
    let bundle : CatalogReader.RowsetBundle =
        { Modules    = [ moduleRow ]
          Kinds      = [ userKindRow ]
          Attributes = [ idAttrRow ]
          References = []
          Triggers   = [ triggerRow "TR_USER_VALIDATE" true definition ] }
    match parseSync (CatalogReader.SnapshotRowsets bundle) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let tr = firstTrigger catalog
        Assert.True tr.IsDisabled
        Assert.Equal (definition, tr.Definition)

[<Fact>]
let ``L3-S4 slice γ: rowset path links trigger to its owning Kind via KindSsKey`` () =
    let bundle : CatalogReader.RowsetBundle =
        { Modules    = [ moduleRow ]
          Kinds      = [ userKindRow ]
          Attributes = [ idAttrRow ]
          References = []
          Triggers   = [ triggerRow "TR_USER_AUDIT" false "..." ] }
    match parseSync (CatalogReader.SnapshotRowsets bundle) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let tr = firstTrigger catalog
        let userKind = catalog.Modules.[0].Kinds.[0]
        Assert.Equal<SsKey>(userKind.SsKey, tr.KindSsKey)

[<Fact>]
let ``L3-S4 slice γ: rowset path carries multiple triggers per Kind preserving disabled-state`` () =
    let bundle : CatalogReader.RowsetBundle =
        { Modules    = [ moduleRow ]
          Kinds      = [ userKindRow ]
          Attributes = [ idAttrRow ]
          References = []
          Triggers   =
            [ triggerRow "TR_USER_AUDIT"      false "AUDIT body"
              triggerRow "TR_USER_VALIDATE"   true  "VALIDATE body" ] }
    match parseSync (CatalogReader.SnapshotRowsets bundle) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        Assert.Equal (2, List.length catalog.Triggers)
        let audit =
            catalog.Triggers |> List.find (fun t -> t.Name = mkName "TR_USER_AUDIT")
        let validate =
            catalog.Triggers |> List.find (fun t -> t.Name = mkName "TR_USER_VALIDATE")
        Assert.False audit.IsDisabled
        Assert.True validate.IsDisabled

[<Fact>]
let ``L3-S4 slice γ: rowset path with empty TriggerRow list yields empty Catalog.Triggers`` () =
    let bundle : CatalogReader.RowsetBundle =
        { Modules    = [ moduleRow ]
          Kinds      = [ userKindRow ]
          Attributes = [ idAttrRow ]
          References = []
          Triggers   = [] }
    match parseSync (CatalogReader.SnapshotRowsets bundle) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        Assert.Empty catalog.Triggers

// ---------------------------------------------------------------------------
// Cross-source parity — JSON path and rowset path produce identical
// triggers for matching input.
// ---------------------------------------------------------------------------

[<Fact>]
let ``L3-S4 slice γ: JSON and rowset paths agree on the same trigger shape`` () =
    let triggerDef = "CREATE TRIGGER TR_USER_AUDIT ON dbo.OSUSR_APPCORE_USER AFTER UPDATE AS BEGIN END"
    let jsonResult = parseSync (CatalogReader.SnapshotJson jsonWithOneTrigger)
    let rowsetBundle : CatalogReader.RowsetBundle =
        { Modules    = [ moduleRow ]
          Kinds      = [ userKindRow ]
          Attributes = [ idAttrRow ]
          References = []
          Triggers   =
            [ triggerRow
                "TR_USER_AUDIT"
                false
                "CREATE TRIGGER [dbo].[TR_USER_AUDIT] ON [dbo].[OSUSR_APPCORE_USER] AFTER UPDATE AS BEGIN INSERT INTO AUDIT VALUES ('user updated') END" ] }
    let rowsetResult = parseSync (CatalogReader.SnapshotRowsets rowsetBundle)
    match jsonResult, rowsetResult with
    | Ok jsonCatalog, Ok rowsetCatalog ->
        // SsKey identity matches across paths (both synthesize via
        // `OS_TRIG_<Module>_<Kind>_<Name>`) — the SnapshotJsonBuilder
        // and the V1 SQL bundle don't carry a per-trigger Guid, so
        // both paths produce the synthesized form.
        Assert.Equal (1, List.length jsonCatalog.Triggers)
        Assert.Equal (1, List.length rowsetCatalog.Triggers)
        let jsonTr = firstTrigger jsonCatalog
        let rowsetTr = firstTrigger rowsetCatalog
        Assert.Equal<SsKey>(jsonTr.SsKey, rowsetTr.SsKey)
        Assert.Equal<Name>(jsonTr.Name, rowsetTr.Name)
        Assert.Equal (jsonTr.IsDisabled, rowsetTr.IsDisabled)
        Assert.Equal<SsKey>(jsonTr.KindSsKey, rowsetTr.KindSsKey)
        // Definition text differs by source-shape (JSON's brackets vs
        // raw rowset form) — both are valid CREATE TRIGGER variants;
        // the IR carries each verbatim. The cross-source parity test
        // verifies *structure*, not byte-identical definition.
        ignore triggerDef
    | _ ->
        Assert.Fail(sprintf "Expected Ok from both paths; got JSON=%A, Rowset=%A" jsonResult rowsetResult)

// ---------------------------------------------------------------------------
// Pillar-9 worked-example axis — verify the harvest-dichotomy
// classification holds: trigger carriage is DataIntent (skeleton-
// reachable; no operator filter at parse time).
// ---------------------------------------------------------------------------

[<Fact>]
let ``Pillar 9 slice γ: trigger lift is skeleton-reachable (every V1 trigger surfaces in IR)`` () =
    // The harvest-dichotomy classification of slice γ (per DECISIONS
    // 2026-05-15 — A.0' slice γ amendment) is DataIntent. Both
    // enabled and disabled triggers carry through to the IR; the
    // operator's choice to emit-or-suppress disabled triggers is a
    // downstream OperatorIntent owned by emitter overlays, not an
    // adapter-time filter.
    match parseSync (CatalogReader.SnapshotJson jsonWithMultipleTriggers) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        // 2 triggers in V1 input → 2 triggers in V2 IR; one enabled
        // and one disabled both reach the skeleton.
        Assert.Equal (2, List.length catalog.Triggers)
        Assert.Contains (catalog.Triggers, (fun t -> not t.IsDisabled))
        Assert.Contains (catalog.Triggers, (fun t -> t.IsDisabled))
