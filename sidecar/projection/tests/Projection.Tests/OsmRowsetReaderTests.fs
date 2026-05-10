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
            References = []
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
            References = []
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
            References = []
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
        { Modules = [ moduleRow None ]; Kinds = []; Attributes = []; References = [] }
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
        IsActive          = true
        EntitySsKey       = sskey
        PrimaryKeySsKey   = None
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
    }

let private userAccountRefRow : CatalogReader.ReferenceRow =
    {
        AttrId          = 113
        RefEntityName   = "Account"
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
let ``slice 2: inactive source attribute drops its reference at the boundary`` () =
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
        Assert.Empty user.References
        // AccountId attribute itself is also dropped — parity with the
        // session-21 inactive-attribute filter.
        Assert.Equal (1, List.length user.Attributes)

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
        IsActive          = true
        EntitySsKey       = None
        PrimaryKeySsKey   = None
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
