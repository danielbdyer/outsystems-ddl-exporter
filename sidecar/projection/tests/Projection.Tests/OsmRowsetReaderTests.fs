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
        // V1 normal-module marker; observed in
        // tests/Fixtures/sql/model.edge-case.seed.sql:97-99.
        EspaceKind     = Some "eSpace"
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
        IsSystemEntity    = false
        IsActive          = true
        EntitySsKey       = sskey
        PrimaryKeySsKey   = None
        Description       = None
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
        Description  = None
        OriginalName = None
        ExternalDatabaseType = None
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
        Description  = None
        OriginalName = None
        ExternalDatabaseType = None
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
          Physical = { Schema = "dbo"; Table = "OSUSR_APPCORE_USER"; Catalog = None }
          Attributes = [
              { IRBuilders.mkAttribute userIdAttrKey (mkName "Id") Integer with Column = { ColumnName = "ID"; IsNullable = false }; IsPrimaryKey = true; IsMandatory = true; IsIdentity = true }
              { IRBuilders.mkAttribute userEmailAttrKey (mkName "Email") Text with Column = { ColumnName = "EMAIL"; IsNullable = false }; IsMandatory = true; Length = Some 250 }
          ]
          References = []
          Indexes    = []
          Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }
    { Modules = [
        { SsKey = appCoreModuleKey
          Name  = mkName "AppCore"
          Kinds = [ userKind ]; IsActive = true; ExtendedProperties = [] } ]; Sequences = [] }

[<Fact>]
let ``SnapshotRowsets: bundle without SsKey Guids parses with synthesized-form SsKeys (JSON-path parity)`` () =
    let bundle : CatalogReader.RowsetBundle =
        {
            Modules    = [ moduleRow None ]
            Kinds      = [ userKindRow None ]
            Attributes = [ idAttrRow None; emailAttrRow None ]
            References = []
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
            References = []
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
            References = []
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
// IsActive carries through; the boundary filter retires (slice β).
//
// Chapter A.0' slice β retires the session-21 inactive-records filter.
// Modules / kinds / attributes with `IsActive=false` now survive into
// the V2 IR carrying their lifecycle flag; downstream emitters decide
// whether to suppress them. The earlier "drop at the boundary" tests
// are reworked as carry-through tests. Pillar-9 harvest analysis: the
// filter was `OperatorIntent`, mis-placed at the adapter; the lift
// reclassifies the source value as `DataIntent` evidence.
// ---------------------------------------------------------------------------

[<Fact>]
let ``SnapshotRowsets: inactive modules carry through with IsActive=false (slice β)`` () =
    let inactive = { moduleRow None with EspaceId = 2; EspaceName = "Inactive"; IsActive = false }
    let inactiveKind = { userKindRow None with EntityId = 21; EspaceId = 2; EntityName = "InactiveUser" }
    let bundle : CatalogReader.RowsetBundle =
        {
            Modules    = [ moduleRow None; inactive ]
            Kinds      = [ userKindRow None; inactiveKind ]
            Attributes = [ idAttrRow None; emailAttrRow None ]
            References = []
        }
    match parseSync (CatalogReader.SnapshotRowsets bundle) with
    | Error errors -> Assert.Fail (sprintf "Expected Ok; got Error: %A" errors)
    | Ok actual ->
        Assert.Equal (2, List.length actual.Modules)
        let byName = actual.Modules |> List.map (fun m -> Name.value m.Name, m.IsActive)
        Assert.Contains(("AppCore", true), byName)
        Assert.Contains(("Inactive", false), byName)

[<Fact>]
let ``SnapshotRowsets: inactive kinds carry through with IsActive=false (slice β)`` () =
    let inactiveKind =
        { userKindRow None with EntityId = 12; EntityName = "Archived"; IsActive = false }
    let bundle : CatalogReader.RowsetBundle =
        {
            Modules    = [ moduleRow None ]
            Kinds      = [ userKindRow None; inactiveKind ]
            Attributes = [ idAttrRow None; emailAttrRow None ]
            References = []
        }
    match parseSync (CatalogReader.SnapshotRowsets bundle) with
    | Error errors -> Assert.Fail (sprintf "Expected Ok; got Error: %A" errors)
    | Ok actual ->
        let kinds = actual.Modules.[0].Kinds
        Assert.Equal (2, List.length kinds)
        let byName = kinds |> List.map (fun k -> Name.value k.Name, k.IsActive)
        Assert.Contains(("User", true), byName)
        Assert.Contains(("Archived", false), byName)

[<Fact>]
let ``SnapshotRowsets: inactive attributes carry through with IsActive=false (slice β)`` () =
    let inactiveAttr = { emailAttrRow None with AttrId = 113; AttrName = "Deleted"; IsActive = false }
    let bundle : CatalogReader.RowsetBundle =
        {
            Modules    = [ moduleRow None ]
            Kinds      = [ userKindRow None ]
            Attributes = [ idAttrRow None; emailAttrRow None; inactiveAttr ]
            References = []
        }
    match parseSync (CatalogReader.SnapshotRowsets bundle) with
    | Error errors -> Assert.Fail (sprintf "Expected Ok; got Error: %A" errors)
    | Ok actual ->
        let attrs = actual.Modules.[0].Kinds.[0].Attributes
        Assert.Equal (3, List.length attrs)
        let byName = attrs |> List.map (fun a -> Name.value a.Name, a.IsActive)
        Assert.Contains(("Id", true), byName)
        Assert.Contains(("Email", true), byName)
        Assert.Contains(("Deleted", false), byName)

// ---------------------------------------------------------------------------
// Closed-DU expansion empirical-test (DECISIONS 2026-05-13).
// Adding SnapshotRowsets should not have rippled outside the variant's
// module. The verification: this test compiles + the existing JSON-
// path tests in OsmCatalogReaderDifferentialTests pass unchanged.
// (The latter is implicit via the test suite running green.)
// ---------------------------------------------------------------------------

[<Fact>]
let ``Closed-DU expansion: SnapshotJson + SnapshotRowsets coexist; both paths usable from same caller`` () =
    // Both paths now construct a module with one Kind to satisfy the
    // LR1 per-module non-empty Kind invariant (matrix row 42; slice
    // 5.13.module-non-empty-invariant). The test's intent
    // (cross-path-from-same-caller) holds; the fixture grows minimally
    // to remain Module.create-valid.
    let json =
        """{ "exportedAtUtc": "2026-05-10T00:00:00Z",
             "modules": [ { "name": "AppCore", "isSystem": false, "isActive": true,
                            "entities": [
                              { "name": "User", "physicalName": "OSUSR_APPCORE_USER",
                                "db_schema": "dbo", "isStatic": false, "isExternal": false,
                                "isSystem": false, "isActive": true,
                                "attributes": [
                                  { "name": "Id", "physicalName": "ID",
                                    "dataType": "Integer", "isMandatory": true,
                                    "isIdentifier": true, "isAutoNumber": true,
                                    "isActive": true, "isReference": false } ] } ] } ] }"""
    let bundle : CatalogReader.RowsetBundle =
        {
            Modules    = [ moduleRow None ]
            Kinds      = [ userKindRow None ]
            Attributes = [ idAttrRow None ]
            References = []
        }
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


// ===========================================================================
// Chapter 3.2 slice 2 — reference rowsets (V1 #RefResolved + #FkReality).
//
// Fixture mirrors session-19's v1ReferenceFixture: AppCore module;
// Account entity (Id PK); User entity (Id PK + AccountId FK to Account);
// OnDelete = NoAction (V1 "Protect" → NoAction per parseDeleteRule).
//
// Tests cover:
//   - Reference appears in target Kind, with synthesized OS_REF SsKey.
//   - Same-module assumption (rule 16): TargetKind resolves within the
//     source attribute's module.
//   - OnDelete mapping parity with parseDeleteRule.
//   - Inactive source attribute drops the reference (chained filter).
//   - Reference SsKey is always synthesized (V1 rowsets don't carry a
//     reference-level Guid; only attribute / kind / module Guids).
//   - Cross-source parity: rowset path with no Guids reproduces the
//     JSON path's expectedReferenceCatalog structurally.
// ===========================================================================

let private accountKindRow (sskey: System.Guid option) : CatalogReader.KindRow =
    {
        EntityId          = 21
        EspaceId          = 1
        EntityName        = "Account"
        PhysicalTableName = "OSUSR_APPCORE_ACCOUNT"
        DbSchema          = "dbo"
        IsStatic          = false
        IsExternal        = false
        IsSystemEntity    = false
        IsActive          = true
        EntitySsKey       = sskey
        PrimaryKeySsKey   = None
        Description       = None
    }

let private accountIdRow (sskey: System.Guid option) : CatalogReader.AttributeRow =
    {
        AttrId       = 211
        EntityId     = 21
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
        Description  = None
        OriginalName = None
        ExternalDatabaseType = None
    }

/// User has Id (PK + IDENTITY) and AccountId (FK to Account); the
/// AccountId attribute is the source of the reference.
let private userKindRowForRef : CatalogReader.KindRow = userKindRow None

let private userIdRow : CatalogReader.AttributeRow = idAttrRow None

let private userAccountIdRow : CatalogReader.AttributeRow =
    {
        AttrId       = 113
        EntityId     = 11
        AttrName     = "AccountId"
        PhysicalCol  = "ACCOUNTID"
        DataType     = "Identifier"
        IsMandatory  = true
        IsIdentifier = false
        IsAutoNumber = false
        Length       = None
        Precision    = None
        Scale        = None
        AttrSsKey    = None
        IsActive     = true
        Description  = None
        OriginalName = None
        ExternalDatabaseType = None
    }

let private userAccountRefRow : CatalogReader.ReferenceRow =
    {
        AttrId          = 113
        RefEntityName   = "Account"
        RefEntityId     = None
        DeleteRuleCode  = Some "Protect"
        HasDbConstraint = true
    }

let private accountKindKey            = kindKey ["AppCore"; "Account"]
let private accountIdAttrKey          = attrKey ["AppCore"; "Account"; "Id"]
let private userAccountIdAttrKey      = attrKey ["AppCore"; "User"; "AccountId"]
let private userAccountReferenceKey   = refKey  ["AppCore"; "User"; "AccountId"]

let private referenceBundle : CatalogReader.RowsetBundle =
    {
        Modules    = [ moduleRow None ]
        Kinds      = [ accountKindRow None; userKindRowForRef ]
        Attributes = [ accountIdRow None; userIdRow; userAccountIdRow ]
        References = [ userAccountRefRow ]
    }

[<Fact>]
let ``slice 2: reference rowset surfaces a Reference on the source kind`` () =
    match parseSync (CatalogReader.SnapshotRowsets referenceBundle) with
    | Error errors ->
        Assert.Fail (sprintf "Expected Ok; got Error: %A" errors)
    | Ok actual ->
        let m = actual.Modules.[0]
        let user = m.Kinds |> List.find (fun k -> k.Name = mkName "User")
        Assert.Equal (1, List.length user.References)
        let r = user.References.[0]
        Assert.Equal<Name>(mkName "AccountId", r.Name)
        Assert.Equal<SsKey>(userAccountIdAttrKey, r.SourceAttribute)
        Assert.Equal<SsKey>(accountKindKey, r.TargetKind)
        Assert.Equal<ReferenceAction>(NoAction, r.OnDelete)
        Assert.Equal<SsKey>(userAccountReferenceKey, r.SsKey)

[<Fact>]
let ``slice 2: rule 16 same-module assumption — TargetKind synthesized within source module`` () =
    // The fixture's AccountId reference carries `RefEntityName = "Account"`
    // (no module prefix). The translation must scope the target-kind
    // SsKey synthesis to the source attribute's module (`AppCore`),
    // producing OS_KIND_AppCore_Account — not OS_KIND_<other>_Account.
    match parseSync (CatalogReader.SnapshotRowsets referenceBundle) with
    | Error errors -> Assert.Fail (sprintf "Expected Ok; got Error: %A" errors)
    | Ok actual ->
        let user = actual.Modules.[0].Kinds |> List.find (fun k -> k.Name = mkName "User")
        let r = user.References.[0]
        // Constructed via kindKey ["AppCore"; "Account"] — same-module
        Assert.Equal<SsKey>(kindKey ["AppCore"; "Account"], r.TargetKind)

[<Fact>]
let ``slice 2: OnDelete mapping — Protect → NoAction; Delete → Cascade; SetNull → SetNull`` () =
    let mkRefBundle (code: string option) : CatalogReader.RowsetBundle =
        { referenceBundle with
            References = [ { userAccountRefRow with DeleteRuleCode = code } ] }
    let parseAndPickRule (b: CatalogReader.RowsetBundle) : ReferenceAction =
        match parseSync (CatalogReader.SnapshotRowsets b) with
        | Error es -> Assert.Fail (sprintf "%A" es); NoAction
        | Ok c     ->
            let user = c.Modules.[0].Kinds |> List.find (fun k -> k.Name = mkName "User")
            user.References.[0].OnDelete
    Assert.Equal<ReferenceAction>(NoAction, parseAndPickRule (mkRefBundle (Some "Protect")))
    Assert.Equal<ReferenceAction>(Cascade,  parseAndPickRule (mkRefBundle (Some "Delete")))
    Assert.Equal<ReferenceAction>(SetNull,  parseAndPickRule (mkRefBundle (Some "SetNull")))
    Assert.Equal<ReferenceAction>(NoAction, parseAndPickRule (mkRefBundle None))
    Assert.Equal<ReferenceAction>(NoAction, parseAndPickRule (mkRefBundle (Some "Ignore")))

[<Fact>]
let ``slice 2: unmapped DeleteRuleCode surfaces as a translation error`` () =
    let bundle =
        { referenceBundle with
            References =
                [ { userAccountRefRow with DeleteRuleCode = Some "TotallyMadeUpRule" } ] }
    match parseSync (CatalogReader.SnapshotRowsets bundle) with
    | Ok _ ->
        Assert.Fail "Expected Error for unmapped delete-rule code; got Ok"
    | Error errors ->
        let codes = errors |> List.map (fun e -> e.Code)
        Assert.Contains("adapter.osm.unmappedDeleteRule", codes)

[<Fact>]
let ``slice 2 / slice β: inactive source attribute carries through with its reference`` () =
    // Chapter A.0' slice β — the session-21 filter retires. The
    // inactive `AccountId` attribute now survives into the IR with
    // `IsActive=false`, and its reference is carried alongside.
    // Downstream emitters decide whether to suppress emission.
    let bundle =
        { referenceBundle with
            Attributes =
                [ accountIdRow None
                  userIdRow
                  { userAccountIdRow with IsActive = false } ] }
    match parseSync (CatalogReader.SnapshotRowsets bundle) with
    | Error errors -> Assert.Fail (sprintf "Expected Ok; got Error: %A" errors)
    | Ok actual ->
        let user = actual.Modules.[0].Kinds |> List.find (fun k -> k.Name = mkName "User")
        Assert.Equal (2, List.length user.Attributes)
        let accountIdAttr =
            user.Attributes |> List.find (fun a -> Name.value a.Name = "AccountId")
        Assert.False accountIdAttr.IsActive
        Assert.Equal (1, List.length user.References)

[<Fact>]
let ``slice 2: Reference SsKey is always synthesized (rowsets carry no per-reference Guid)`` () =
    // Even when the source attribute carries an OssysOriginal Guid,
    // the Reference SsKey is synthesized via OS_REF_<mod>_<entity>_<attr>.
    // V1's #RefResolved rowset does not carry a per-reference Guid;
    // the Reference is a derived entity in V2's algebra.
    let guidUserAccountId =
        System.Guid.Parse("55555555-5555-4555-8555-555555555555")
    let bundle : CatalogReader.RowsetBundle =
        {
            Modules    = [ moduleRow None ]
            Kinds      = [ accountKindRow None; userKindRowForRef ]
            Attributes = [
                accountIdRow None
                userIdRow
                { userAccountIdRow with AttrSsKey = Some guidUserAccountId }
            ]
            References = [ userAccountRefRow ]
        }
    match parseSync (CatalogReader.SnapshotRowsets bundle) with
    | Error errors -> Assert.Fail (sprintf "Expected Ok; got Error: %A" errors)
    | Ok actual ->
        let user = actual.Modules.[0].Kinds |> List.find (fun k -> k.Name = mkName "User")
        let acctIdAttr = user.Attributes |> List.find (fun a -> a.Name = mkName "AccountId")
        // Source attribute has the Guid SsKey
        Assert.Equal<SsKey>(OssysOriginal guidUserAccountId, acctIdAttr.SsKey)
        // Reference itself is OS_REF synthesized
        let r = user.References.[0]
        Assert.Equal<SsKey>(userAccountReferenceKey, r.SsKey)
        // Reference's SourceAttribute points at the actual attribute
        // SsKey (the OssysOriginal Guid).
        Assert.Equal<SsKey>(OssysOriginal guidUserAccountId, r.SourceAttribute)

[<Fact>]
let ``slice 2: cross-source parity — rowset path mirrors JSON path's expectedReferenceCatalog`` () =
    // The reference fixture without Guids must round-trip into the
    // same Catalog the JSON path's v1ReferenceFixture produces (modulo
    // synthesized SsKeys, which both paths share when no Guids are
    // present). This is the structural-shape parity assertion the
    // chapter-3.2 slice-5 cross-source test will generalize.
    match parseSync (CatalogReader.SnapshotRowsets referenceBundle) with
    | Error errors -> Assert.Fail (sprintf "Expected Ok; got Error: %A" errors)
    | Ok actual ->
        let m = actual.Modules.[0]
        Assert.Equal (2, List.length m.Kinds)
        let account = m.Kinds |> List.find (fun k -> k.Name = mkName "Account")
        let user    = m.Kinds |> List.find (fun k -> k.Name = mkName "User")
        Assert.Equal<SsKey>(accountKindKey,        account.SsKey)
        Assert.Equal<SsKey>(accountIdAttrKey,      account.Attributes.[0].SsKey)
        Assert.Equal<SsKey>(userKindKey,           user.SsKey)
        Assert.Equal<SsKey>(userAccountIdAttrKey,  user.Attributes.[1].SsKey)
        // The Reference itself: structural identity matches the JSON
        // path exactly (synthesized SsKey, name, target, OnDelete).
        let r = user.References.[0]
        Assert.Equal<SsKey>(userAccountReferenceKey, r.SsKey)
        Assert.Equal<SsKey>(accountKindKey,          r.TargetKind)
        Assert.Equal<SsKey>(userAccountIdAttrKey,    r.SourceAttribute)
        Assert.Equal<ReferenceAction>(NoAction,      r.OnDelete)


// ===========================================================================
// Chapter 3.2 slice 3 — EspaceKind activation (Origin three-way real).
//
// Mirrors session-20's v1ExternalFixture: external entity, this time
// with EspaceKind carried on the ModuleRow. The translation refines
// from the JSON-path placeholder (collapsing to ExternalViaIntegrationStudio
// for all external entities) to the three-way real driven by the
// EspaceKind marker.
//
// Tests cover:
//   - EspaceKind = "Extension" + isExternal=true → ExternalViaIntegrationStudio
//   - EspaceKind ≠ "Extension" (e.g., "eSpace") + isExternal=true → ExternalDirect
//   - EspaceKind = None + isExternal=true → ExternalDirect
//   - isExternal=false (any EspaceKind) → OsNative
//   - Case-insensitive matching on the marker
//   - Refinement vs JSON path: same fixture under both paths emits
//     a divergent Origin when EspaceKind ≠ "Extension" — this is
//     the empirical evidence that the rowset path refines rule 17.
// ===========================================================================

let private externalModuleRow (espaceKind: string option) : CatalogReader.ModuleRow =
    {
        EspaceId       = 2
        EspaceName     = "ExtBilling"
        IsSystemModule = false
        IsActive       = true
        EspaceKind     = espaceKind
        EspaceSsKey    = None
    }

let private billingAccountKindRow : CatalogReader.KindRow =
    {
        EntityId          = 31
        EspaceId          = 2
        EntityName        = "BillingAccount"
        PhysicalTableName = "BILLING_ACCOUNT"
        DbSchema          = "billing"
        IsStatic          = false
        IsExternal        = true
        IsSystemEntity    = false
        IsActive          = true
        EntitySsKey       = None
        PrimaryKeySsKey   = None
        Description       = None
    }

let private billingAccountIdRow : CatalogReader.AttributeRow =
    {
        AttrId       = 311
        EntityId     = 31
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
        Description  = None
        OriginalName = None
        ExternalDatabaseType = Some "int"
    }

let private externalBundle (espaceKind: string option) : CatalogReader.RowsetBundle =
    {
        Modules    = [ externalModuleRow espaceKind ]
        Kinds      = [ billingAccountKindRow ]
        Attributes = [ billingAccountIdRow ]
        References = []
    }

let private originOf (bundle: CatalogReader.RowsetBundle) : Origin =
    match parseSync (CatalogReader.SnapshotRowsets bundle) with
    | Error es -> Assert.Fail (sprintf "%A" es); OsNative
    | Ok c     ->
        let kind = c.Modules.[0].Kinds.[0]
        kind.Origin

[<Fact>]
let ``slice 3: EspaceKind="Extension" + isExternal=true → ExternalViaIntegrationStudio`` () =
    Assert.Equal<Origin>(ExternalViaIntegrationStudio, originOf (externalBundle (Some "Extension")))

[<Fact>]
let ``slice 3: EspaceKind="eSpace" + isExternal=true → ExternalDirect`` () =
    // Normal-module marker on an isExternal=true entity. V1's data
    // model permits this combination (a Direct external entity in a
    // normal eSpace, bypassing the IS step). The rowset path's
    // three-way real surfaces it as ExternalDirect — the JSON path
    // collapses it to ExternalViaIntegrationStudio (the load-bearing
    // empirical-evidence refinement).
    Assert.Equal<Origin>(ExternalDirect, originOf (externalBundle (Some "eSpace")))

[<Fact>]
let ``slice 3: EspaceKind=None + isExternal=true → ExternalDirect`` () =
    // Absent marker witnesses absent IS step. Same disposition as
    // the eSpace case — neither carries an IS signal.
    Assert.Equal<Origin>(ExternalDirect, originOf (externalBundle None))

[<Fact>]
let ``slice 3: isExternal=false → OsNative regardless of EspaceKind`` () =
    // The OsNative branch is independent of EspaceKind. The
    // moduleRow helper sets EspaceKind = Some "eSpace"; the slice-1
    // fixture's User entity has isExternal = false; Origin must
    // remain OsNative. Belt-and-suspenders check on the matrix.
    let bundle : CatalogReader.RowsetBundle =
        {
            Modules    = [ { externalModuleRow (Some "Extension") with EspaceName = "AppCore" } ]
            Kinds      = [ { billingAccountKindRow with EntityName = "User"
                                                        PhysicalTableName = "OSUSR_APPCORE_USER"
                                                        DbSchema = "dbo"
                                                        IsExternal = false } ]
            Attributes = [ billingAccountIdRow ]
            References = []
        }
    Assert.Equal<Origin>(OsNative, originOf bundle)

[<Fact>]
let ``slice 3: EspaceKind marker matches case-insensitively`` () =
    // V1's column is nvarchar(50); historical samples have varied
    // in capitalization across V1 versions. Both "Extension" and
    // "EXTENSION" should resolve to ExternalViaIntegrationStudio.
    Assert.Equal<Origin>(ExternalViaIntegrationStudio, originOf (externalBundle (Some "Extension")))
    Assert.Equal<Origin>(ExternalViaIntegrationStudio, originOf (externalBundle (Some "EXTENSION")))
    Assert.Equal<Origin>(ExternalViaIntegrationStudio, originOf (externalBundle (Some "extension")))

[<Fact>]
let ``slice 3: refinement evidence — rowset path diverges from JSON path on EspaceKind="eSpace"+isExternal=true`` () =
    // Empirical evidence that the rowset path refines rule 17.
    // Same conceptual fixture (an external entity with no IS marker)
    // resolves to ExternalDirect under the rowset path but to
    // ExternalViaIntegrationStudio under the JSON path. The
    // divergence is by design — it's the difference the chapter
    // closes.
    let rowsetOrigin = originOf (externalBundle (Some "eSpace"))
    // JSON-path equivalent: same shape, no EspaceKind.
    let jsonFixture = """{
      "exportedAtUtc": "2026-05-17T00:00:00Z",
      "modules": [
        { "name": "ExtBilling", "isSystem": false, "isActive": true,
          "entities": [
            { "name": "BillingAccount", "physicalName": "BILLING_ACCOUNT",
              "isStatic": false, "isExternal": true, "isActive": true,
              "db_catalog": null, "db_schema": "billing",
              "attributes": [
                { "name": "Id", "physicalName": "ID", "originalName": null,
                  "dataType": "Identifier", "length": null, "precision": null,
                  "scale": null, "default": null, "isMandatory": true,
                  "isIdentifier": true, "isAutoNumber": true, "isActive": true,
                  "isReference": 0, "refEntityId": null, "refEntity_name": null,
                  "refEntity_physicalName": null, "reference_deleteRuleCode": null,
                  "reference_hasDbConstraint": 0, "external_dbType": "int",
                  "physical_isPresentButInactive": 0 }
              ],
              "relationships": [], "indexes": [], "triggers": []
            }
          ]
        }
      ]
    }"""
    let jsonOrigin =
        match parseSync (CatalogReader.SnapshotJson jsonFixture) with
        | Error es -> Assert.Fail (sprintf "%A" es); OsNative
        | Ok c     -> c.Modules.[0].Kinds.[0].Origin
    Assert.Equal<Origin>(ExternalDirect,                 rowsetOrigin)
    Assert.Equal<Origin>(ExternalViaIntegrationStudio,   jsonOrigin)
    Assert.NotEqual<Origin>(jsonOrigin, rowsetOrigin)


// ===========================================================================
// Chapter 3.2 slice 4 — IsSystemEntity activation (ModalityMark.SystemOwned).
//
// New system-entity fixture (no V1↔V2 differential precedent; chapter
// 3.2 slice 4 surfaces the carriage path under empirical pressure).
// V1's `ossys_Entity.Is_System` lifts into V2's `ModalityMark.SystemOwned`
// payload-free mark — chosen over flat `Kind.IsSystem: bool`, an
// `Origin` axis split, or a new `Stewardship` DU per the IR-refinement
// decision recorded in CatalogReader.fs KindRow docstring.
//
// Tests cover:
//   - IsSystemEntity=true lifts into Kind.Modality with SystemOwned mark
//   - IsSystemEntity=false omits the SystemOwned mark (parity with V1 default)
//   - SystemOwned coexists with Static (composite-modality case)
//   - SystemOwned coexists with isExternal+EspaceKind (orthogonality)
//   - Catalog with mixed system/non-system entities (filter-by-Modality
//     pattern future consumers will use)
// ===========================================================================

/// Sibling-Π helper: does a Kind carry the SystemOwned mark?
let private hasSystemOwnedMark (k: Kind) : bool =
    k.Modality |> List.contains SystemOwned

let private systemKindRow : CatalogReader.KindRow =
    {
        EntityId          = 41
        EspaceId          = 1
        EntityName        = "SystemAudit"
        PhysicalTableName = "OSSYS_AUDIT"
        DbSchema          = "ossys"
        IsStatic          = false
        IsExternal        = false
        IsSystemEntity    = true
        IsActive          = true
        EntitySsKey       = None
        PrimaryKeySsKey   = None
        Description       = None
    }

let private systemAuditIdRow : CatalogReader.AttributeRow =
    {
        AttrId       = 411
        EntityId     = 41
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
        Description  = None
        OriginalName = None
        ExternalDatabaseType = None
    }

let private systemBundle : CatalogReader.RowsetBundle =
    {
        Modules    = [ moduleRow None ]
        Kinds      = [ systemKindRow ]
        Attributes = [ systemAuditIdRow ]
        References = []
    }

[<Fact>]
let ``slice 4: IsSystemEntity=true → Kind.Modality contains SystemOwned`` () =
    match parseSync (CatalogReader.SnapshotRowsets systemBundle) with
    | Error es -> Assert.Fail (sprintf "Expected Ok; got Error: %A" es)
    | Ok actual ->
        let k = actual.Modules.[0].Kinds.[0]
        Assert.True (hasSystemOwnedMark k, "Expected Modality to contain SystemOwned")
        Assert.Equal<ModalityMark list>([ SystemOwned ], k.Modality)

[<Fact>]
let ``slice 4: IsSystemEntity=false → Modality omits SystemOwned (matches V1 default)`` () =
    // The slice-1 minimal-fixture bundle has IsSystemEntity=false on
    // the User kind (per the moduleRow helper default). Its Modality
    // must NOT contain SystemOwned.
    let bundle : CatalogReader.RowsetBundle =
        {
            Modules    = [ moduleRow None ]
            Kinds      = [ userKindRow None ]
            Attributes = [ idAttrRow None ]
            References = []
        }
    match parseSync (CatalogReader.SnapshotRowsets bundle) with
    | Error es -> Assert.Fail (sprintf "Expected Ok; got Error: %A" es)
    | Ok actual ->
        let k = actual.Modules.[0].Kinds.[0]
        Assert.False (hasSystemOwnedMark k, "User kind should not have SystemOwned")
        Assert.Equal<ModalityMark list>([], k.Modality)

[<Fact>]
let ``slice 4: SystemOwned coexists with Static (composite-modality case)`` () =
    // A static + system entity (rare but possible — V1 system enums
    // exist; they're both static-populated and platform-owned).
    // ModalityMark list shape carries both marks; declaration order
    // is Static-first, SystemOwned-second per parseKindRow's list
    // construction order.
    let staticSystemKindRow : CatalogReader.KindRow =
        { systemKindRow with IsStatic = true; EntityName = "SystemEnum" }
    let bundle : CatalogReader.RowsetBundle =
        {
            Modules    = [ moduleRow None ]
            Kinds      = [ staticSystemKindRow ]
            Attributes = [ systemAuditIdRow ]
            References = []
        }
    match parseSync (CatalogReader.SnapshotRowsets bundle) with
    | Error es -> Assert.Fail (sprintf "Expected Ok; got Error: %A" es)
    | Ok actual ->
        let k = actual.Modules.[0].Kinds.[0]
        Assert.Equal<ModalityMark list>([ Static []; SystemOwned ], k.Modality)

[<Fact>]
let ``slice 4: SystemOwned is orthogonal to Origin axis`` () =
    // System-owned + external is rare but representable. The IR
    // refinement choice (SystemOwned in Modality, NOT in Origin)
    // means Origin and SystemOwned compose freely. This test asserts
    // the orthogonality empirically — a system entity that is also
    // marked isExternal carries BOTH Origin=ExternalViaIntegrationStudio
    // AND Modality=[SystemOwned], without conflict.
    let externalSystemKind : CatalogReader.KindRow =
        { systemKindRow with IsExternal = true }
    let bundle : CatalogReader.RowsetBundle =
        {
            Modules    = [ { externalModuleRow (Some "Extension") with EspaceId = 1 } ]
            Kinds      = [ externalSystemKind ]
            Attributes = [ systemAuditIdRow ]
            References = []
        }
    match parseSync (CatalogReader.SnapshotRowsets bundle) with
    | Error es -> Assert.Fail (sprintf "Expected Ok; got Error: %A" es)
    | Ok actual ->
        let k = actual.Modules.[0].Kinds.[0]
        Assert.Equal<Origin>(ExternalViaIntegrationStudio, k.Origin)
        Assert.True (hasSystemOwnedMark k, "Expected SystemOwned mark on external+system kind")

[<Fact>]
let ``slice 4: mixed catalog — system and non-system kinds coexist`` () =
    // Future-consumer pattern: a Pass that filters out system entities
    // walks `kinds |> List.filter (not << hasSystemOwnedMark)`. This
    // test exercises that shape directly — mixed catalog with two
    // kinds (User non-system; SystemAudit system); the filter
    // discriminates exactly the two.
    let bundle : CatalogReader.RowsetBundle =
        {
            Modules    = [ moduleRow None ]
            Kinds      = [ userKindRow None; systemKindRow ]
            Attributes = [ idAttrRow None; systemAuditIdRow ]
            References = []
        }
    match parseSync (CatalogReader.SnapshotRowsets bundle) with
    | Error es -> Assert.Fail (sprintf "Expected Ok; got Error: %A" es)
    | Ok actual ->
        let kinds = actual.Modules.[0].Kinds
        Assert.Equal (2, List.length kinds)
        let systemKinds    = kinds |> List.filter hasSystemOwnedMark
        let userKinds      = kinds |> List.filter (not << hasSystemOwnedMark)
        Assert.Equal (1, List.length systemKinds)
        Assert.Equal (1, List.length userKinds)
        Assert.Equal<Name>(mkName "SystemAudit", systemKinds.[0].Name)
        Assert.Equal<Name>(mkName "User",        userKinds.[0].Name)


// ===========================================================================
// Chapter 3.2 slice 5 — cross-source parity tests (closes chapter 3.2).
//
// Validates the load-bearing claim of the chapter: SnapshotJson and
// SnapshotRowsets produce structurally-equivalent Catalogs for the
// same conceptual fixture, modulo the documented SsKey-shape
// divergence (CHAPTER_3_2_OPEN.md axis 5).
//
// Two granularities:
//
//   (1) **Total parity** — when the rowset bundle carries no Guids,
//       both paths emit `Synthesized` SsKeys via the same OS_MOD_*
//       / OS_KIND_* / OS_ATTR_* / OS_REF_* conventions. The two
//       Catalogs are byte-identical; `Assert.Equal<Catalog>` works.
//
//   (2) **Shape parity** — when the rowset bundle carries Guids, its
//       SsKeys are `OssysOriginal` while the JSON path's stay
//       `Synthesized`. The shape projection (`catalogShape`) strips
//       SsKey identity and compares Names + structural fields only.
//
// Coverage axes (parity tests run across all three slice-1/2/3
// fixture classes that round-trip cleanly between both paths):
//
//   - Minimal (slice 1): one module, one kind, two attributes.
//   - Reference (slice 2): two kinds, one Reference, same-module FK.
//   - External (slice 3): external entity with EspaceKind="Extension"
//     so Origin aligns (ExternalViaIntegrationStudio both sides).
//
// **Out of parity scope** (documented):
//   - EspaceKind="eSpace" + isExternal=true — rowset emits ExternalDirect,
//     JSON emits ExternalViaIntegrationStudio (slice 3's refinement
//     evidence test asserts this divergence directly).
//   - IsSystemEntity=true — rowset emits Modality=[SystemOwned]; JSON
//     drops the bit (no corresponding JSON field). Tested via slice 4.
//
// These divergences are CHAPTER 3.2's load-bearing additions; the
// parity tests assert what doesn't diverge.
// ===========================================================================

// --- Structural-shape projection helpers ----------------------------------

type private AttributeShape =
    {
        Name         : Name
        Type         : PrimitiveType
        Column       : ColumnRealization
        IsPrimaryKey : bool
        IsMandatory  : bool
        Length       : int option
        Precision    : int option
        Scale        : int option
        IsIdentity   : bool
    }

type private ReferenceShape =
    {
        Name                : Name
        SourceAttributeName : Name option
        TargetKindName      : Name option
        OnDelete            : ReferenceAction
    }

type private KindShape =
    {
        Name       : Name
        Origin     : Origin
        Modality   : ModalityMark list
        Physical   : PhysicalRealization
        Attributes : AttributeShape list
        References : ReferenceShape list
    }

type private ModuleShape =
    {
        Name  : Name
        Kinds : KindShape list
    }

type private CatalogShape = { Modules : ModuleShape list }

let private attributeShape (a: Attribute) : AttributeShape =
    {
        Name         = a.Name
        Type         = a.Type
        Column       = a.Column
        IsPrimaryKey = a.IsPrimaryKey
        IsMandatory  = a.IsMandatory
        Length       = a.Length
        Precision    = a.Precision
        Scale        = a.Scale
        IsIdentity   = a.IsIdentity
    }

let private referenceShape (nameBySsKey: Map<SsKey, Name>) (r: Reference) : ReferenceShape =
    {
        Name                = r.Name
        SourceAttributeName = Map.tryFind r.SourceAttribute nameBySsKey
        TargetKindName      = Map.tryFind r.TargetKind nameBySsKey
        OnDelete            = r.OnDelete
    }

let private kindShape (nameBySsKey: Map<SsKey, Name>) (k: Kind) : KindShape =
    {
        Name       = k.Name
        Origin     = k.Origin
        Modality   = k.Modality
        Physical   = k.Physical
        Attributes = k.Attributes |> List.map attributeShape
        References = k.References |> List.map (referenceShape nameBySsKey)
    }

let private moduleShape (nameBySsKey: Map<SsKey, Name>) (m: Module) : ModuleShape =
    { Name = m.Name; Kinds = m.Kinds |> List.map (kindShape nameBySsKey) }

let private catalogShape (c: Catalog) : CatalogShape =
    // Build a name lookup across all attribute and kind SsKeys; references
    // resolve their SourceAttribute/TargetKind to names, which gives us
    // SsKey-shape-independent comparison.
    let nameBySsKey : Map<SsKey, Name> =
        c.Modules
        |> List.collect (fun m ->
            m.Kinds
            |> List.collect (fun k ->
                (k.SsKey, k.Name)
                :: (k.Attributes |> List.map (fun a -> a.SsKey, a.Name))))
        |> Map.ofList
    { Modules = c.Modules |> List.map (moduleShape nameBySsKey) }

// --- Parity test fixtures (small JSON strings + matching bundles) ---------

let private minimalJsonFixture =
    """{
  "exportedAtUtc": "2026-05-10T00:00:00Z",
  "modules": [
    { "name": "AppCore", "isSystem": false, "isActive": true,
      "entities": [
        { "name": "User", "physicalName": "OSUSR_APPCORE_USER",
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
              "physical_isPresentButInactive": 0 },
            { "name": "Email", "physicalName": "EMAIL", "originalName": null,
              "dataType": "Text", "length": 250, "precision": null,
              "scale": null, "default": null, "isMandatory": true,
              "isIdentifier": false, "isAutoNumber": false, "isActive": true,
              "isReference": 0, "refEntityId": null, "refEntity_name": null,
              "refEntity_physicalName": null, "reference_deleteRuleCode": null,
              "reference_hasDbConstraint": 0, "external_dbType": null,
              "physical_isPresentButInactive": 0 }
          ],
          "relationships": [], "indexes": [], "triggers": []
        }
      ]
    }
  ]
}"""

let private minimalRowsetBundleNoGuids : CatalogReader.RowsetBundle =
    {
        Modules    = [ moduleRow None ]
        Kinds      = [ userKindRow None ]
        Attributes = [ idAttrRow None; emailAttrRow None ]
        References = []
    }

let private referenceJsonFixture =
    """{
  "exportedAtUtc": "2026-05-16T00:00:00Z",
  "modules": [
    { "name": "AppCore", "isSystem": false, "isActive": true,
      "entities": [
        { "name": "Account", "physicalName": "OSUSR_APPCORE_ACCOUNT",
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
          ], "relationships": [], "indexes": [], "triggers": [] },
        { "name": "User", "physicalName": "OSUSR_APPCORE_USER",
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
              "physical_isPresentButInactive": 0 },
            { "name": "AccountId", "physicalName": "ACCOUNTID", "originalName": null,
              "dataType": "Identifier", "length": null, "precision": null,
              "scale": null, "default": null, "isMandatory": true,
              "isIdentifier": false, "isAutoNumber": false, "isActive": true,
              "isReference": 1, "refEntityId": 1, "refEntity_name": "Account",
              "refEntity_physicalName": "OSUSR_APPCORE_ACCOUNT",
              "reference_deleteRuleCode": "Protect",
              "reference_hasDbConstraint": 1, "external_dbType": null,
              "physical_isPresentButInactive": 0 }
          ], "relationships": [], "indexes": [], "triggers": [] }
      ]
    }
  ]
}"""

let private externalParityJsonFixture =
    """{
  "exportedAtUtc": "2026-05-17T00:00:00Z",
  "modules": [
    { "name": "ExtBilling", "isSystem": false, "isActive": true,
      "entities": [
        { "name": "BillingAccount", "physicalName": "BILLING_ACCOUNT",
          "isStatic": false, "isExternal": true, "isActive": true,
          "db_catalog": null, "db_schema": "billing",
          "attributes": [
            { "name": "Id", "physicalName": "ID", "originalName": null,
              "dataType": "Identifier", "length": null, "precision": null,
              "scale": null, "default": null, "isMandatory": true,
              "isIdentifier": true, "isAutoNumber": true, "isActive": true,
              "isReference": 0, "refEntityId": null, "refEntity_name": null,
              "refEntity_physicalName": null, "reference_deleteRuleCode": null,
              "reference_hasDbConstraint": 0, "external_dbType": "int",
              "physical_isPresentButInactive": 0 }
          ], "relationships": [], "indexes": [], "triggers": [] }
      ]
    }
  ]
}"""

// External bundle aligned to JSON's Origin: EspaceKind="Extension"
// produces ExternalViaIntegrationStudio under the rowset path,
// matching the JSON path's two-way collapse for isExternal=true.
let private externalParityRowsetBundle : CatalogReader.RowsetBundle =
    externalBundle (Some "Extension")

// --- Parity assertion helper ----------------------------------------------

let private assertCatalogsTotallyEqual (jsonSource: string) (bundle: CatalogReader.RowsetBundle) =
    let jsonResult   = parseSync (CatalogReader.SnapshotJson jsonSource)
    let rowsetResult = parseSync (CatalogReader.SnapshotRowsets bundle)
    match jsonResult, rowsetResult with
    | Ok jsonCatalog, Ok rowsetCatalog ->
        Assert.Equal<Catalog>(jsonCatalog, rowsetCatalog)
    | _ ->
        Assert.Fail (
            sprintf "Expected Ok from both paths; JSON=%A, Rowset=%A"
                jsonResult rowsetResult)

let private assertCatalogsShapeEqual (jsonSource: string) (bundle: CatalogReader.RowsetBundle) =
    let jsonResult   = parseSync (CatalogReader.SnapshotJson jsonSource)
    let rowsetResult = parseSync (CatalogReader.SnapshotRowsets bundle)
    match jsonResult, rowsetResult with
    | Ok jsonCatalog, Ok rowsetCatalog ->
        Assert.Equal<CatalogShape>(
            catalogShape jsonCatalog,
            catalogShape rowsetCatalog)
    | _ ->
        Assert.Fail (
            sprintf "Expected Ok from both paths; JSON=%A, Rowset=%A"
                jsonResult rowsetResult)

// --- Parity tests ---------------------------------------------------------

[<Fact>]
let ``slice 5 parity: minimal fixture — JSON ≡ Rowset (no Guids; total equality)`` () =
    assertCatalogsTotallyEqual minimalJsonFixture minimalRowsetBundleNoGuids

[<Fact>]
let ``slice 5 parity: reference-bearing fixture — JSON ≡ Rowset (no Guids; total equality)`` () =
    let bundle : CatalogReader.RowsetBundle =
        {
            Modules    = [ moduleRow None ]
            Kinds      = [ accountKindRow None; userKindRowForRef ]
            Attributes = [ accountIdRow None; userIdRow; userAccountIdRow ]
            References = [ userAccountRefRow ]
        }
    assertCatalogsTotallyEqual referenceJsonFixture bundle

[<Fact>]
let ``slice 5 parity: external fixture aligned at Extension — JSON ≡ Rowset (no Guids; total equality)`` () =
    assertCatalogsTotallyEqual externalParityJsonFixture externalParityRowsetBundle

[<Fact>]
let ``slice 5 parity: minimal fixture WITH Guids — shape parity holds modulo SsKey divergence`` () =
    let bundle : CatalogReader.RowsetBundle =
        {
            Modules    = [ moduleRow (Some appCoreGuid) ]
            Kinds      = [ userKindRow (Some userGuid) ]
            Attributes = [ idAttrRow (Some idGuid); emailAttrRow (Some emailGuid) ]
            References = []
        }
    // SsKey identity diverges (rowset = OssysOriginal Guid; JSON =
    // Synthesized); structural shape (names, types, columns,
    // physical, Origin, Modality, references) must match.
    assertCatalogsShapeEqual minimalJsonFixture bundle

[<Fact>]
let ``slice 5 parity: SsKey divergence axis — Guids change identity but not shape`` () =
    // Direct evidence that the SsKey divergence is the ONLY axis on
    // which the two parses differ for a Guid-carrying bundle. Total
    // equality FAILS (Catalog SsKeys differ); shape equality PASSES.
    let bundle : CatalogReader.RowsetBundle =
        {
            Modules    = [ moduleRow (Some appCoreGuid) ]
            Kinds      = [ userKindRow (Some userGuid) ]
            Attributes = [ idAttrRow (Some idGuid); emailAttrRow (Some emailGuid) ]
            References = []
        }
    let jsonCat   =
        match parseSync (CatalogReader.SnapshotJson minimalJsonFixture) with
        | Ok c -> c | Error es -> failwithf "%A" es
    let rowsetCat =
        match parseSync (CatalogReader.SnapshotRowsets bundle) with
        | Ok c -> c | Error es -> failwithf "%A" es
    // Catalogs are NOT byte-identical (SsKey divergence proven).
    Assert.NotEqual<Catalog>(jsonCat, rowsetCat)
    // But their shapes ARE identical (the load-bearing claim).
    Assert.Equal<CatalogShape>(catalogShape jsonCat, catalogShape rowsetCat)
    // And the rowset path's identity is OssysOriginal (the chapter's
    // load-bearing addition that makes A1's rename-survival
    // unbounded).
    let rowsetUserSsKey = rowsetCat.Modules.[0].Kinds.[0].SsKey
    Assert.Equal<SsKey>(OssysOriginal userGuid, rowsetUserSsKey)
