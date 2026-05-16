module Projection.Tests.IRFidelityLiftTests

open Xunit
open Projection.Core
open Projection.Adapters.Osm
open Projection.Tests.IRBuilders

// ---------------------------------------------------------------------------
// Chapter A.0' slices γ + δ + ε + ζ + η — IR fidelity lift tests.
//
// One file covering the five slices that ship together:
//   γ — `Kind.Triggers : Trigger list` + V1 JSON `entity.triggers[]` pickup
//   δ — `Catalog.Sequences : Sequence list` + Sequence value type
//   ε — `Attribute.DefaultValue : SqlLiteral option` + V1 JSON `default`
//       pickup + `Attribute.Computed : ComputedColumnConfig option` +
//       `Kind.ColumnChecks : ColumnCheck list`
//   ζ — `ExtendedProperties` at four levels (Module / Kind / Attribute /
//       Index) + V1 JSON `entity.extendedProperties[]` pickup
//   η — `ModalityMark.Temporal of TemporalConfig` (closed-DU expansion)
//
// All five share the chapter axis 1 framing: structural commitment, not
// feature. The IR carries the evidence; emission lands per-consumer.
// Pillar 9 classification: all DataIntent (source-schema evidence,
// reachable from `Project(catalog, Policy.empty, profile)` without
// operator opinion).
//
// **Test-fixture pattern.** Tests use `IRBuilders.mkAttribute` /
// `mkKind` / etc. so that future slices that add IR fields only update
// the builder, not these tests. See `IRBuilders.fs` for the discipline.
// ---------------------------------------------------------------------------

let private mkName' s = Name.create s |> Result.value
let private mkSsKey (s: string) : SsKey =
    SsKey.synthesized "TEST" s |> Result.value
let private parseSync (source: CatalogReader.SnapshotSource) : Result<Catalog> =
    (CatalogReader.parse source).GetAwaiter().GetResult()
let private firstKind (c: Catalog) : Kind =
    c.Modules |> List.head |> fun m -> m.Kinds |> List.head

// ---------------------------------------------------------------------------
// Slice γ — Trigger value type + Kind.Triggers + adapter pickup.
// ---------------------------------------------------------------------------

[<Fact>]
let ``L3-S4 triggers: Trigger.create rejects blank definition`` () =
    let key  = mkSsKey "OS_TRG_M_K_T"
    let name = mkName' "TR_OnInsert"
    match Trigger.create key name false "" with
    | Ok _ -> Assert.Fail("Expected Error for blank definition")
    | Error errors ->
        let codes = errors |> List.map (fun e -> e.Code)
        Assert.Contains("trigger.definition.empty", codes)

[<Fact>]
let ``L3-S4 triggers: Trigger.create accepts non-blank definition; carries IsDisabled`` () =
    let key  = mkSsKey "OS_TRG_M_K_T"
    let name = mkName' "TR_OnInsert"
    match Trigger.create key name true "PRINT 'hello'" with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok t ->
        Assert.True(t.IsDisabled)
        Assert.Equal("PRINT 'hello'", t.Definition)

let private v1FixtureWithTriggers : string =
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
            { "name": "Id", "physicalName": "ID", "originalName": null,
              "dataType": "Identifier", "length": null, "precision": null,
              "scale": null, "default": null, "isMandatory": true,
              "isIdentifier": true, "isAutoNumber": true, "isActive": true,
              "isReference": 0, "refEntityId": null, "refEntity_name": null,
              "refEntity_physicalName": null, "reference_deleteRuleCode": null,
              "reference_hasDbConstraint": 0, "external_dbType": null,
              "physical_isPresentButInactive": 0 }
          ],
          "relationships": [], "indexes": [],
          "triggers": [
            { "name": "TR_User_Audit", "isDisabled": false,
              "definition": "CREATE TRIGGER TR_User_Audit ON OSUSR_APPCORE_USER AFTER INSERT AS PRINT 'audit'" },
            { "name": "TR_User_Validation", "isDisabled": true,
              "definition": "CREATE TRIGGER TR_User_Validation ON OSUSR_APPCORE_USER FOR DELETE AS RAISERROR('blocked', 16, 1)" }
          ]
        }
      ]
    }
  ]
}"""

[<Fact>]
let ``L3-S4 triggers: JSON path lifts entity.triggers[] into Kind.Triggers`` () =
    match parseSync (CatalogReader.SnapshotJson v1FixtureWithTriggers) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let kind = firstKind catalog
        Assert.Equal(2, List.length kind.Triggers)
        let names = kind.Triggers |> List.map (fun t -> Name.value t.Name)
        Assert.Contains("TR_User_Audit", names)
        Assert.Contains("TR_User_Validation", names)
        let validation =
            kind.Triggers |> List.find (fun t -> Name.value t.Name = "TR_User_Validation")
        Assert.True(validation.IsDisabled)

// ---------------------------------------------------------------------------
// Slice δ — Sequence value type + Catalog.Sequences.
// ---------------------------------------------------------------------------

[<Fact>]
let ``L3-S5 sequences: Sequence.create rejects blank schema`` () =
    let key  = mkSsKey "OS_SEQ_dbo_S1"
    let name = mkName' "S1"
    match Sequence.create key name "" "BIGINT" None None None None false Unspecified None with
    | Ok _ -> Assert.Fail("Expected Error for blank schema")
    | Error errors ->
        Assert.Contains("sequence.schema.empty", errors |> List.map (fun e -> e.Code))

[<Fact>]
let ``L3-S5 sequences: Sequence.create rejects blank dataType`` () =
    let key  = mkSsKey "OS_SEQ_dbo_S1"
    let name = mkName' "S1"
    match Sequence.create key name "dbo" "" None None None None false Unspecified None with
    | Ok _ -> Assert.Fail("Expected Error for blank dataType")
    | Error errors ->
        Assert.Contains("sequence.dataType.empty", errors |> List.map (fun e -> e.Code))

[<Fact>]
let ``L3-S5 sequences: Sequence.create carries cycle + cache flags`` () =
    let key  = mkSsKey "OS_SEQ_dbo_OrderNo"
    let name = mkName' "OrderNo"
    match
        Sequence.create key name "dbo" "BIGINT"
            (Some 1m) (Some 1m) (Some 1m) (Some 9999999m) true Cache (Some 100)
    with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok s ->
        Assert.True(s.IsCycleEnabled)
        Assert.Equal(Cache, s.CacheMode)
        Assert.Equal(Some 100, s.CacheSize)
        Assert.Equal(Some 9999999m, s.Maximum)

[<Fact>]
let ``L3-S5 sequences: Catalog.create accepts Sequences list with disjoint SsKeys`` () =
    let key1 = mkSsKey "OS_SEQ_dbo_S1"
    let key2 = mkSsKey "OS_SEQ_dbo_S2"
    let s1 =
        Sequence.create key1 (mkName' "S1") "dbo" "BIGINT" None None None None false Unspecified None
        |> Result.value
    let s2 =
        Sequence.create key2 (mkName' "S2") "dbo" "INT" None None None None false NoCache None
        |> Result.value
    match Catalog.create [] [ s1; s2 ] with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok c -> Assert.Equal(2, List.length c.Sequences)

[<Fact>]
let ``L3-S5 sequences: Catalog.create rejects duplicate Sequence SsKeys (A4)`` () =
    let dup = mkSsKey "OS_SEQ_dbo_S1"
    let s1 =
        Sequence.create dup (mkName' "S1") "dbo" "BIGINT" None None None None false Unspecified None
        |> Result.value
    let s2 =
        Sequence.create dup (mkName' "S2") "dbo" "INT" None None None None false NoCache None
        |> Result.value
    match Catalog.create [] [ s1; s2 ] with
    | Ok _ -> Assert.Fail("Expected Error on duplicate Sequence SsKey")
    | Error errors ->
        Assert.Contains(
            "catalog.sequences.duplicateKey",
            errors |> List.map (fun e -> e.Code))

// ---------------------------------------------------------------------------
// Slice ε — DefaultValue + Computed + ColumnChecks.
// ---------------------------------------------------------------------------

let private v1FixtureWithDefault : string =
    """{
  "exportedAtUtc": "2026-05-16T00:00:00.0000000+00:00",
  "modules": [ { "name": "AppCore", "isSystem": false, "isActive": true,
    "entities": [ { "name": "Country", "physicalName": "OSUSR_APPCORE_COUNTRY",
      "isStatic": true, "isExternal": false, "isActive": true,
      "db_catalog": null, "db_schema": "dbo",
      "attributes": [
        { "name": "Id", "physicalName": "ID", "originalName": null,
          "dataType": "Identifier", "length": null, "precision": null,
          "scale": null, "default": null, "isMandatory": true,
          "isIdentifier": true, "isAutoNumber": true, "isActive": true,
          "isReference": 0, "refEntityId": null, "refEntity_name": null,
          "refEntity_physicalName": null, "reference_deleteRuleCode": null,
          "reference_hasDbConstraint": 0, "external_dbType": null,
          "physical_isPresentButInactive": 0 },
        { "name": "Code", "physicalName": "CODE", "originalName": null,
          "dataType": "Text", "length": 10, "precision": null,
          "scale": null, "default": "UNKNOWN", "isMandatory": true,
          "isIdentifier": false, "isAutoNumber": false, "isActive": true,
          "isReference": 0, "refEntityId": null, "refEntity_name": null,
          "refEntity_physicalName": null, "reference_deleteRuleCode": null,
          "reference_hasDbConstraint": 0, "external_dbType": null,
          "physical_isPresentButInactive": 0 }
      ], "relationships": [], "indexes": [], "triggers": [] } ] } ] }"""

[<Fact>]
let ``L3-S6 default: JSON path lifts attribute.default into Attribute.DefaultValue`` () =
    match parseSync (CatalogReader.SnapshotJson v1FixtureWithDefault) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let kind = firstKind catalog
        let codeAttr = kind.Attributes |> List.find (fun a -> Name.value a.Name = "Code")
        match codeAttr.DefaultValue with
        | Some (TextLit raw) -> Assert.Equal("UNKNOWN", raw)
        | other -> Assert.Fail(sprintf "Expected TextLit 'UNKNOWN'; got %A" other)

[<Fact>]
let ``L3-S6 default: absent attribute.default leaves DefaultValue = None`` () =
    match parseSync (CatalogReader.SnapshotJson v1FixtureWithDefault) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let kind = firstKind catalog
        let idAttr = kind.Attributes |> List.find (fun a -> Name.value a.Name = "Id")
        Assert.Equal<SqlLiteral option>(None, idAttr.DefaultValue)

[<Fact>]
let ``L3-S7 computed: ComputedColumnConfig.create rejects blank expression`` () =
    match ComputedColumnConfig.create "" false with
    | Ok _ -> Assert.Fail("Expected Error for blank expression")
    | Error errors ->
        Assert.Contains(
            "computedColumn.expression.empty",
            errors |> List.map (fun e -> e.Code))

[<Fact>]
let ``L3-S7 computed: ComputedColumnConfig.create accepts non-blank expression`` () =
    match ComputedColumnConfig.create "FirstName + ' ' + LastName" true with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok cfg ->
        Assert.True(cfg.IsPersisted)
        Assert.Equal("FirstName + ' ' + LastName", cfg.Expression)

[<Fact>]
let ``L3-S8 columnCheck: ColumnCheck.create rejects blank definition`` () =
    let key = mkSsKey "OS_CHK_M_K_C"
    match ColumnCheck.create key None "" false with
    | Ok _ -> Assert.Fail("Expected Error for blank definition")
    | Error errors ->
        Assert.Contains(
            "columnCheck.definition.empty",
            errors |> List.map (fun e -> e.Code))

[<Fact>]
let ``L3-S8 columnCheck: ColumnCheck.create accepts optional name`` () =
    let key = mkSsKey "OS_CHK_M_K_C"
    match ColumnCheck.create key None "Amount > 0" false with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok check ->
        Assert.Equal<Name option>(None, check.Name)
        Assert.Equal("Amount > 0", check.Definition)

// ---------------------------------------------------------------------------
// Slice ζ — ExtendedProperties at four levels + JSON adapter pickup.
// ---------------------------------------------------------------------------

[<Fact>]
let ``L3-S9 extendedProperties: ExtendedProperty.create rejects blank name`` () =
    match ExtendedProperty.create "" (Some "v") with
    | Ok _ -> Assert.Fail("Expected Error for blank name")
    | Error errors ->
        Assert.Contains(
            "extendedProperty.name.empty",
            errors |> List.map (fun e -> e.Code))

[<Fact>]
let ``L3-S9 extendedProperties: ExtendedProperty.create normalises empty string to None`` () =
    match ExtendedProperty.create "MS_Description" (Some "") with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok ep -> Assert.Equal<string option>(None, ep.Value)

let private v1FixtureWithExtendedProperties : string =
    """{
  "exportedAtUtc": "2026-05-16T00:00:00.0000000+00:00",
  "modules": [ { "name": "AppCore", "isSystem": false, "isActive": true,
    "entities": [ { "name": "User", "physicalName": "OSUSR_APPCORE_USER",
      "isStatic": false, "isExternal": false, "isActive": true,
      "db_catalog": null, "db_schema": "dbo",
      "attributes": [
        { "name": "Id", "physicalName": "ID", "originalName": null,
          "dataType": "Identifier", "length": null, "precision": null,
          "scale": null, "default": null, "isMandatory": true,
          "isIdentifier": true, "isAutoNumber": true, "isActive": true,
          "isReference": 0, "refEntityId": null, "refEntity_name": null,
          "refEntity_physicalName": null, "reference_deleteRuleCode": null,
          "reference_hasDbConstraint": 0, "external_dbType": null,
          "physical_isPresentButInactive": 0 }
      ], "relationships": [], "indexes": [], "triggers": [],
      "extendedProperties": [
        { "name": "MS_Description", "value": "Platform user table" },
        { "name": "App_Owner", "value": null }
      ] } ] } ] }"""

[<Fact>]
let ``L3-S9 extendedProperties: JSON path lifts entity.extendedProperties[] into Kind.ExtendedProperties`` () =
    match parseSync (CatalogReader.SnapshotJson v1FixtureWithExtendedProperties) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let kind = firstKind catalog
        Assert.Equal(2, List.length kind.ExtendedProperties)
        let byName =
            kind.ExtendedProperties |> List.map (fun ep -> ep.Name, ep.Value)
        Assert.Contains(("MS_Description", Some "Platform user table"), byName)
        Assert.Contains(("App_Owner", None), byName)

// ---------------------------------------------------------------------------
// Slice η — ModalityMark.Temporal closed-DU widening.
// ---------------------------------------------------------------------------

[<Fact>]
let ``L3-S4 temporal: ModalityMark.Temporal carries the temporal config`` () =
    let cfg : TemporalConfig =
        { HistorySchema = Some "history"
          HistoryTable  = Some "OSUSR_X_USER_History"
          PeriodStart   = Some (mkName' "ValidFrom")
          PeriodEnd     = Some (mkName' "ValidTo")
          Retention     = Limited (90, Days) }
    let mark = Temporal cfg
    match mark with
    | Temporal extracted ->
        Assert.Equal(Some "history", extracted.HistorySchema)
        match extracted.Retention with
        | Limited (90, Days) -> ()
        | other -> Assert.Fail(sprintf "Expected Limited(90, Days); got %A" other)
    | _ -> Assert.Fail("Expected Temporal variant")

[<Fact>]
let ``L3-S4 temporal: Infinite retention round-trips through ModalityMark`` () =
    let cfg : TemporalConfig =
        { HistorySchema = None
          HistoryTable  = None
          PeriodStart   = None
          PeriodEnd     = None
          Retention     = Infinite }
    let kind =
        mkKind
            (mkSsKey "OS_KIND_M_K")
            (mkName' "K")
            { Schema = "dbo"; Table = "T"; Catalog = None }
            []
    let kindWithTemporal = { kind with Modality = [ Temporal cfg ] }
    let extracted =
        kindWithTemporal.Modality
        |> List.tryPick (function Temporal t -> Some t | _ -> None)
    Assert.Equal<TemporalConfig option>(Some cfg, extracted)

// ---------------------------------------------------------------------------
// IRBuilders smoke test — the fixture-builder pattern produces valid IR.
// ---------------------------------------------------------------------------

[<Fact>]
let ``IRBuilders: mkAttribute defaults all new fields to DataIntent zero-evidence`` () =
    let attr =
        mkAttribute (mkSsKey "OS_ATTR_M_K_A") (mkName' "A") Integer
    Assert.True(attr.IsActive)
    Assert.Equal<SqlLiteral option>(None, attr.DefaultValue)
    Assert.Equal<ComputedColumnConfig option>(None, attr.Computed)
    Assert.Empty(attr.ExtendedProperties)

[<Fact>]
let ``IRBuilders: mkKind defaults Triggers / ColumnChecks / ExtendedProperties to []`` () =
    let kind =
        mkKind
            (mkSsKey "OS_KIND_M_K")
            (mkName' "K")
            { Schema = "dbo"; Table = "T"; Catalog = None }
            []
    Assert.Empty(kind.Triggers)
    Assert.Empty(kind.ColumnChecks)
    Assert.Empty(kind.ExtendedProperties)

[<Fact>]
let ``IRBuilders: mkCatalog defaults Sequences to []`` () =
    let c = mkCatalog []
    Assert.Empty(c.Sequences)

// ---------------------------------------------------------------------------
// Slice θ — TableId.Catalog extension (L3-S10 / L3-I10).
// ---------------------------------------------------------------------------

[<Fact>]
let ``L3-S10 catalog coordinate: TableId.create defaults Catalog to None`` () =
    match TableId.create "dbo" "T" with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok tid ->
        Assert.Equal<string option>(None, tid.Catalog)
        Assert.Equal("dbo", tid.Schema)
        Assert.Equal("T", tid.Table)

[<Fact>]
let ``L3-S10 catalog coordinate: TableId.createWithCatalog carries explicit catalog`` () =
    match TableId.createWithCatalog "AppDb" "dbo" "Orders" with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok tid -> Assert.Equal<string option>(Some "AppDb", tid.Catalog)

[<Fact>]
let ``L3-S10 catalog coordinate: TableId.createWithCatalog rejects blank catalog`` () =
    match TableId.createWithCatalog "" "dbo" "Orders" with
    | Ok _ -> Assert.Fail("Expected Error for blank catalog")
    | Error errors ->
        Assert.Contains(
            "tableId.catalog.empty",
            errors |> List.map (fun e -> e.Code))

let private v1FixtureWithDbCatalog : string =
    """{
  "exportedAtUtc": "2026-05-16T00:00:00.0000000+00:00",
  "modules": [
    { "name": "M", "isSystem": false, "isActive": true,
      "entities": [
        { "name": "E", "physicalName": "OSUSR_M_E",
          "isStatic": false, "isExternal": false, "isActive": true,
          "db_catalog": "AnalyticsDb", "db_schema": "dbo",
          "attributes": [
            { "name": "Id", "physicalName": "ID", "originalName": null,
              "dataType": "Identifier", "length": null, "precision": null,
              "scale": null, "default": null,
              "isMandatory": true, "isIdentifier": true, "isAutoNumber": true,
              "isActive": true, "isReference": 0, "refEntityId": null,
              "refEntity_name": null, "refEntity_physicalName": null,
              "reference_deleteRuleCode": null, "reference_hasDbConstraint": 0,
              "external_dbType": null, "physical_isPresentButInactive": 0 }
          ],
          "relationships": [], "indexes": [], "triggers": [] }
      ] }
  ]
}"""

[<Fact>]
let ``L3-S10 catalog coordinate: JSON path lifts db_catalog into TableId.Catalog`` () =
    match parseSync (CatalogReader.SnapshotJson v1FixtureWithDbCatalog) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        Assert.Equal<string option>(Some "AnalyticsDb", (firstKind catalog).Physical.Catalog)
