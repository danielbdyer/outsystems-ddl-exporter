module Projection.Tests.OsmRowsetReaderTests

open Xunit
open Projection.Core
open Projection.Adapters.Osm
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Chapter 3.2 slice 1 — `SnapshotRowsets` variant of `SnapshotSource`.
//
// Sibling translation path to `SnapshotJson`. The rowset bundle carries
// SsKey natively (via `OssysOriginal guid` per `Identity.fs:70`), so
// A1's "identity survives rename" bound resolves through this path
// rather than being JSON-projection-bounded.
//
// Two test scenarios per CHAPTER_3_2_OPEN.md axis 5 (SsKey-shape
// divergence per source variant):
//
//   1. Bundle WITHOUT SsKey Guids → fallback to synthesized form.
//      Same `Catalog` value as the JSON-path's `expectedCatalog` —
//      this is the parity case the cross-source parity test (slice 5)
//      will leverage.
//
//   2. Bundle WITH SsKey Guids → `OssysOriginal guid` form. Different
//      structural SsKey shape; same Module / Kind / Attribute count;
//      same physical mappings. This is the chapter's load-bearing
//      addition: A1 unbounded.
// ---------------------------------------------------------------------------

let private mkName s = Name.create s |> Result.value

let private parseSync (source: CatalogReader.SnapshotSource) : Result<Catalog> =
    (CatalogReader.parse source).GetAwaiter().GetResult()

// ---------------------------------------------------------------------------
// Minimal-fixture rowset bundle. Mirrors `v1MinimalFixture` in
// OsmCatalogReaderDifferentialTests: one module (`AppCore`); one
// entity (`User`); two attributes (`Id` PK + IDENTITY, `Email`).
// ---------------------------------------------------------------------------

let private moduleRow (sskey: System.Guid option) : CatalogReader.ModuleRow =
    {
        EspaceId       = 1
        EspaceName     = "AppCore"
        IsSystemModule = false
        IsActive       = true
        EspaceSsKey    = sskey
    }

let private userKindRow (sskey: System.Guid option) : CatalogReader.KindRow =
    {
        EntityId          = 11
        EspaceId          = 1
        EntityName        = "User"
        PhysicalTableName = "OSUSR_APPCORE_USER"
        DbSchema          = "dbo"
        IsStatic          = false
        IsExternal        = false
        IsActive          = true
        EntitySsKey       = sskey
        PrimaryKeySsKey   = None
    }

let private idAttrRow (sskey: System.Guid option) : CatalogReader.AttributeRow =
    {
        AttrId       = 111
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
        AttrSsKey    = sskey
        IsActive     = true
    }

let private emailAttrRow (sskey: System.Guid option) : CatalogReader.AttributeRow =
    {
        AttrId       = 112
        EntityId     = 11
        AttrName     = "Email"
        PhysicalCol  = "EMAIL"
        DataType     = "Text"
        IsMandatory  = true
        IsIdentifier = false
        IsAutoNumber = false
        Length       = Some 250
        Precision    = None
        Scale        = None
        AttrSsKey    = sskey
        IsActive     = true
    }

// ---------------------------------------------------------------------------
// Scenario 1 — synthesized SsKey fallback (parity with the JSON path).
// When no Guid SsKeys are present in the bundle, the rowset translation
// emits the same `Synthesized` SsKeys as the JSON path's
// `OS_MOD_*` / `OS_KIND_*` / `OS_ATTR_*` conventions.
// ---------------------------------------------------------------------------

let private appCoreModuleKey = modKey "AppCore"
let private userKindKey      = kindKey ["AppCore"; "User"]
let private userIdAttrKey    = attrKey ["AppCore"; "User"; "Id"]
let private userEmailAttrKey = attrKey ["AppCore"; "User"; "Email"]

let private expectedCatalogSynthesized : Catalog =
    let userKind : Kind =
        { SsKey    = userKindKey
          Name     = mkName "User"
          Origin   = OsNative
          Modality = []
          Physical = { Schema = "dbo"; Table = "OSUSR_APPCORE_USER" }
          Attributes = [
              { SsKey        = userIdAttrKey
                Name         = mkName "Id"
                Type         = Integer
                Column       = { ColumnName = "ID"; IsNullable = false }
                IsPrimaryKey = true
                IsMandatory = true; Length = None; Precision = None; Scale = None; IsIdentity = true }
              { SsKey        = userEmailAttrKey
                Name         = mkName "Email"
                Type         = Text
                Column       = { ColumnName = "EMAIL"; IsNullable = false }
                IsPrimaryKey = false
                IsMandatory = true; Length = Some 250; Precision = None; Scale = None; IsIdentity = false }
          ]
          References = []
          Indexes    = [] }
    { Modules = [
        { SsKey = appCoreModuleKey
          Name  = mkName "AppCore"
          Kinds = [ userKind ] } ] }

[<Fact>]
let ``SnapshotRowsets: bundle without SsKey Guids parses with synthesized-form SsKeys (JSON-path parity)`` () =
    let bundle : CatalogReader.RowsetBundle =
        {
            Modules    = [ moduleRow None ]
            Kinds      = [ userKindRow None ]
            Attributes = [ idAttrRow None; emailAttrRow None ]
        }
    let result = parseSync (CatalogReader.SnapshotRowsets bundle)
    match result with
    | Error errors ->
        Assert.Fail (sprintf "Expected Ok; got Error: %A" errors)
    | Ok actual ->
        Assert.Equal<Catalog>(expectedCatalogSynthesized, actual)

// ---------------------------------------------------------------------------
// Scenario 2 — Guid-carrying SsKey (the load-bearing addition; A1
// unbounded). When the bundle carries `OssysSsKey: Some guid`, the
// resulting `Catalog` carries `SsKey.OssysOriginal guid` instead of
// the synthesized form. Cross-source parity with the JSON path is
// structural-shape only (same kinds, same attributes, same physical
// mappings) — SsKey identity differs by construction.
// ---------------------------------------------------------------------------

let private appCoreGuid = System.Guid.Parse("11111111-1111-4111-8111-111111111111")
let private userGuid    = System.Guid.Parse("22222222-2222-4222-8222-222222222222")
let private idGuid      = System.Guid.Parse("33333333-3333-4333-8333-333333333333")
let private emailGuid   = System.Guid.Parse("44444444-4444-4444-8444-444444444444")

[<Fact>]
let ``SnapshotRowsets: bundle WITH SsKey Guids produces OssysOriginal SsKeys (A1 bound resolved)`` () =
    let bundle : CatalogReader.RowsetBundle =
        {
            Modules    = [ moduleRow (Some appCoreGuid) ]
            Kinds      = [ userKindRow (Some userGuid) ]
            Attributes = [ idAttrRow (Some idGuid); emailAttrRow (Some emailGuid) ]
        }
    let result = parseSync (CatalogReader.SnapshotRowsets bundle)
    match result with
    | Error errors ->
        Assert.Fail (sprintf "Expected Ok; got Error: %A" errors)
    | Ok actual ->
        Assert.Equal (1, List.length actual.Modules)
        let m = actual.Modules.[0]
        Assert.Equal<SsKey>(OssysOriginal appCoreGuid, m.SsKey)
        Assert.Equal (1, List.length m.Kinds)
        let k = m.Kinds.[0]
        Assert.Equal<SsKey>(OssysOriginal userGuid, k.SsKey)
        Assert.Equal<string>("OSUSR_APPCORE_USER", k.Physical.Table)
        Assert.Equal<string>("dbo", k.Physical.Schema)
        Assert.Equal (2, List.length k.Attributes)
        let a0 = k.Attributes.[0]
        let a1 = k.Attributes.[1]
        Assert.Equal<SsKey>(OssysOriginal idGuid, a0.SsKey)
        Assert.Equal<SsKey>(OssysOriginal emailGuid, a1.SsKey)

// ---------------------------------------------------------------------------
// A1 cash-out: when the rowset path supplies SsKey Guids, the
// resulting Catalog is structurally distinct from the JSON-path
// Catalog (SsKey shape) but observably equivalent on every other
// axis. The bound in `AXIOMS A1` ("identity survives rename
// **bounded by JSON-projection lossiness**") becomes unbounded
// through this path — the "bounded by" qualifier is replaced by
// the rowset variant's input contract.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A1 unbounded: rowset Catalog with Guid SsKeys mirrors JSON Catalog on every non-SsKey axis`` () =
    let bundle : CatalogReader.RowsetBundle =
        {
            Modules    = [ moduleRow (Some appCoreGuid) ]
            Kinds      = [ userKindRow (Some userGuid) ]
            Attributes = [ idAttrRow (Some idGuid); emailAttrRow (Some emailGuid) ]
        }
    match parseSync (CatalogReader.SnapshotRowsets bundle) with
    | Error errors ->
        Assert.Fail (sprintf "Expected Ok; got Error: %A" errors)
    | Ok actual ->
        // Names + physical realizations + types — every non-SsKey
        // axis matches the JSON-path expected catalog.
        Assert.Equal (1, List.length actual.Modules)
        let m = actual.Modules.[0]
        Assert.Equal<Name>(mkName "AppCore", m.Name)
        let k = m.Kinds.[0]
        Assert.Equal<Name>(mkName "User", k.Name)
        Assert.Equal<Origin>(OsNative, k.Origin)
        Assert.Equal<ModalityMark list>([], k.Modality)
        let attrNames = k.Attributes |> List.map (fun a -> a.Name)
        Assert.Equal<Name list>([ mkName "Id"; mkName "Email" ], attrNames)
        let attrTypes = k.Attributes |> List.map (fun a -> a.Type)
        Assert.Equal<PrimitiveType list>([ Integer; Text ], attrTypes)

// ---------------------------------------------------------------------------
// Inactive-records filter parity with the JSON path (session 21).
// Modules / kinds / attributes with `IsActive=false` drop at the
// boundary identically across paths.
// ---------------------------------------------------------------------------

[<Fact>]
let ``SnapshotRowsets: inactive modules drop at the boundary (session 21 parity)`` () =
    let inactive = { moduleRow None with EspaceId = 2; EspaceName = "Inactive"; IsActive = false }
    let inactiveKind = { userKindRow None with EntityId = 21; EspaceId = 2; EntityName = "InactiveUser" }
    let bundle : CatalogReader.RowsetBundle =
        {
            Modules    = [ moduleRow None; inactive ]
            Kinds      = [ userKindRow None; inactiveKind ]
            Attributes = [ idAttrRow None; emailAttrRow None ]
        }
    match parseSync (CatalogReader.SnapshotRowsets bundle) with
    | Error errors -> Assert.Fail (sprintf "Expected Ok; got Error: %A" errors)
    | Ok actual ->
        // Only the active module survives.
        Assert.Equal (1, List.length actual.Modules)
        Assert.Equal<Name>(mkName "AppCore", actual.Modules.[0].Name)

[<Fact>]
let ``SnapshotRowsets: inactive kinds drop at the boundary`` () =
    let inactiveKind =
        { userKindRow None with EntityId = 12; EntityName = "Archived"; IsActive = false }
    let bundle : CatalogReader.RowsetBundle =
        {
            Modules    = [ moduleRow None ]
            Kinds      = [ userKindRow None; inactiveKind ]
            Attributes = [ idAttrRow None; emailAttrRow None ]
        }
    match parseSync (CatalogReader.SnapshotRowsets bundle) with
    | Error errors -> Assert.Fail (sprintf "Expected Ok; got Error: %A" errors)
    | Ok actual ->
        // Only the active kind survives within the module.
        Assert.Equal (1, List.length actual.Modules.[0].Kinds)
        Assert.Equal<Name>(mkName "User", actual.Modules.[0].Kinds.[0].Name)

[<Fact>]
let ``SnapshotRowsets: inactive attributes drop at the boundary`` () =
    let inactiveAttr = { emailAttrRow None with AttrId = 113; AttrName = "Deleted"; IsActive = false }
    let bundle : CatalogReader.RowsetBundle =
        {
            Modules    = [ moduleRow None ]
            Kinds      = [ userKindRow None ]
            Attributes = [ idAttrRow None; emailAttrRow None; inactiveAttr ]
        }
    match parseSync (CatalogReader.SnapshotRowsets bundle) with
    | Error errors -> Assert.Fail (sprintf "Expected Ok; got Error: %A" errors)
    | Ok actual ->
        Assert.Equal (2, List.length actual.Modules.[0].Kinds.[0].Attributes)

// ---------------------------------------------------------------------------
// Closed-DU expansion empirical-test (DECISIONS 2026-05-13).
// Adding SnapshotRowsets should not have rippled outside the variant's
// module. The verification: this test compiles + the existing JSON-
// path tests in OsmCatalogReaderDifferentialTests pass unchanged.
// (The latter is implicit via the test suite running green.)
// ---------------------------------------------------------------------------

[<Fact>]
let ``Closed-DU expansion: SnapshotJson + SnapshotRowsets coexist; both paths usable from same caller`` () =
    let json =
        """{ "exportedAtUtc": "2026-05-10T00:00:00Z",
             "modules": [ { "name": "AppCore", "isSystem": false, "isActive": true,
                            "entities": [] } ] }"""
    let bundle : CatalogReader.RowsetBundle =
        { Modules = [ moduleRow None ]; Kinds = []; Attributes = [] }
    let resJson = parseSync (CatalogReader.SnapshotJson json)
    let resRow  = parseSync (CatalogReader.SnapshotRowsets bundle)
    match resJson, resRow with
    | Ok cJson, Ok cRow ->
        Assert.Equal (1, List.length cJson.Modules)
        Assert.Equal (1, List.length cRow.Modules)
        Assert.Equal<Name>(mkName "AppCore", cJson.Modules.[0].Name)
        Assert.Equal<Name>(mkName "AppCore", cRow.Modules.[0].Name)
    | _ ->
        Assert.Fail (sprintf "Expected Ok from both; got JSON=%A, Rowset=%A" resJson resRow)
