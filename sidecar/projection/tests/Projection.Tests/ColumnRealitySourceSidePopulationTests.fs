module Projection.Tests.ColumnRealitySourceSidePopulationTests

// Slice A.4.7'-prelude.row53-source-side — source-side population
// of `Attribute.Computed` + `Attribute.DefaultName` from V1's
// `#ColumnReality` rowset via `MetadataSnapshotRunner.toBundle`.
//
// Carriage-only fields shipped at slice 5.3.α.column-axis-deferral-
// closeout (2026-05-18); this slice closes the V1-source → V2-IR
// wiring so round-trip parity becomes real (V1's deployed
// `DF_<table>_<col>` constraint identifier and computed-column
// expressions actually populate the V2 IR).
//
// Per pillar 9: pure DataIntent — V1 evidence → V2 IR projection
// within CatalogReader's existing `rowsetAggregateParsing` Site.
// No new TransformRegistry Sites; the existing Site's Rationale
// amended to cover the new fields.

open Xunit
open Projection.Core
open Projection.Adapters.Osm

// ---------------------------------------------------------------------------
// Fixture helpers — mirrors the OsmRowsetReaderTests pattern.
// ---------------------------------------------------------------------------

let private mustOk r =
    match r with
    | Ok v -> v
    | Error es -> invalidOp (sprintf "fixture: %A" es)

let private moduleRow : CatalogReader.ModuleRow =
    { EspaceId       = 1
      EspaceName     = "AppCore"
      IsSystemModule = false
      IsActive       = true
      EspaceKind     = None
      EspaceSsKey    = None }

let private kindRow : CatalogReader.KindRow =
    { EntityId          = 11
      EspaceId          = 1
      EntityName        = "Customer"
      PhysicalTableName = "OSUSR_M_CUSTOMER"
      DbSchema          = "dbo"
      IsStatic          = false
      IsExternal        = false
      IsSystemEntity    = false
      IsActive          = true
      EntitySsKey       = None
      PrimaryKeySsKey   = None
      Description       = None }

let private attrRow
    (attrId: int)
    (name: string)
    (dataType: string)
    (isComputed: bool)
    (computedDef: string option)
    (defaultConstraintName: string option)
    : CatalogReader.AttributeRow =
    { AttrId               = attrId
      EntityId             = 11
      AttrName             = name
      PhysicalCol          = name.ToUpperInvariant()
      DataType             = dataType
      IsMandatory          = false
      IsIdentifier         = name = "Id"
      IsAutoNumber         = false
      Length               = None
      Precision            = None
      Scale                = None
      AttrSsKey            = None
      IsActive             = true
      Description          = None
      OriginalName         = None
      ExternalDatabaseType = None
      IsComputed           = isComputed
      ComputedDefinition   = computedDef
      DefaultConstraintName = defaultConstraintName }

let private buildBundle (attrs: CatalogReader.AttributeRow list) : CatalogReader.RowsetBundle =
    { CatalogReader.RowsetBundle.empty with
        Modules    = [ moduleRow ]
        Kinds      = [ kindRow ]
        Attributes = attrs }

let private parseToCatalog (bundle: CatalogReader.RowsetBundle) : Catalog =
    let task () = CatalogReader.parse (CatalogReader.SnapshotRowsets bundle)
    let result = TaskSync.run task
    mustOk result

let private findAttribute (cat: Catalog) (attrName: string) : Attribute =
    cat.Modules
    |> List.collect (fun m -> m.Kinds)
    |> List.collect (fun k -> k.Attributes)
    |> List.find (fun a -> Name.value a.Name = attrName)

// ---------------------------------------------------------------------------
// LR4 source-side: V1 #ColumnReality.IsComputed + ComputedDefinition
// → V2 Attribute.Computed
// ---------------------------------------------------------------------------

[<Fact>]
let ``A.4.7'-prelude.row53-source-side: rowset path populates Attribute.Computed from V1 ColumnReality when IsComputed = true`` () =
    let idAttr = attrRow 100 "Id" "Identifier" false None None
    let computedAttr = attrRow 101 "Doubled" "Integer" true (Some "([BASE] * 2)") None
    let cat = parseToCatalog (buildBundle [ idAttr; computedAttr ])
    let computed = findAttribute cat "Doubled"
    match computed.Computed with
    | Some config ->
        Assert.Equal("([BASE] * 2)", config.Expression)
        // V1's #ColumnReality doesn't surface sys.computed_columns
        // .is_persisted; default to false.
        Assert.False(config.IsPersisted)
    | None ->
        Assert.Fail("expected Attribute.Computed = Some when IsComputed = true")

[<Fact>]
let ``A.4.7'-prelude.row53-source-side: rowset path leaves Attribute.Computed = None when IsComputed = false`` () =
    let idAttr = attrRow 100 "Id" "Identifier" false None None
    let regularAttr = attrRow 101 "Name" "Text" false None None
    let cat = parseToCatalog (buildBundle [ idAttr; regularAttr ])
    let regular = findAttribute cat "Name"
    Assert.Equal(None, regular.Computed)

[<Fact>]
let ``A.4.7'-prelude.row53-source-side: IsComputed = true with empty ComputedDefinition produces Attribute.Computed = None (ComputedColumnConfig.create rejects blank)`` () =
    let idAttr = attrRow 100 "Id" "Identifier" false None None
    let blankComputed = attrRow 101 "Empty" "Integer" true (Some "") None
    let cat = parseToCatalog (buildBundle [ idAttr; blankComputed ])
    let blank = findAttribute cat "Empty"
    // ComputedColumnConfig.create rejects empty expressions per
    // Catalog.fs:160-167; the parser falls back to None rather than
    // failing the whole AttributeRow.
    Assert.Equal(None, blank.Computed)

// ---------------------------------------------------------------------------
// Row 53 source-side: V1 #ColumnReality.DefaultConstraintName
// → V2 Attribute.DefaultName
// ---------------------------------------------------------------------------

[<Fact>]
let ``A.4.7'-prelude.row53-source-side: rowset path populates Attribute.DefaultName from V1 ColumnReality`` () =
    let idAttr = attrRow 100 "Id" "Identifier" false None None
    let namedDefaultAttr =
        attrRow 101 "CreatedAt" "DateTime" false None (Some "DF_Customer_CreatedAt")
    let cat = parseToCatalog (buildBundle [ idAttr; namedDefaultAttr ])
    let attr = findAttribute cat "CreatedAt"
    match attr.DefaultName with
    | Some name -> Assert.Equal("DF_Customer_CreatedAt", Name.value name)
    | None      -> Assert.Fail("expected DefaultName = Some when V1 carries DefaultConstraintName")

[<Fact>]
let ``A.4.7'-prelude.row53-source-side: rowset path leaves Attribute.DefaultName = None when V1 carries no DefaultConstraintName`` () =
    let idAttr = attrRow 100 "Id" "Identifier" false None None
    let plainAttr = attrRow 101 "Name" "Text" false None None
    let cat = parseToCatalog (buildBundle [ idAttr; plainAttr ])
    let attr = findAttribute cat "Name"
    Assert.Equal(None, attr.DefaultName)

// ---------------------------------------------------------------------------
// Per pillar 9: the rowset adapter's existing `rowsetAggregateParsing`
// Site already covers these translations; no new TransformRegistry
// Site is needed. Verify the existing Site classifies as DataIntent
// (V1 evidence projection) and its Rationale references the new
// fields.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A.4.7'-prelude.row53-source-side: rowsetAggregateParsing Site classifies as DataIntent and Rationale names ColumnReality`` () =
    let site =
        CatalogReader.registeredMetadata.Sites
        |> List.find (fun s -> s.SiteName = "rowsetAggregateParsing")
    Assert.Equal(DataIntent, site.Classification)
    // Per the Rationale amendment in this slice, the prose names the
    // ColumnReality join + the new fields.
    Assert.Contains("ColumnReality", site.Rationale)
    Assert.Contains("IsComputed", site.Rationale)
    Assert.Contains("DefaultConstraintName", site.Rationale)
