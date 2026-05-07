module Projection.Tests.OsmCatalogReaderDifferentialTests

open Xunit
open Projection.Core
open Projection.Adapters.Osm

// ---------------------------------------------------------------------------
// Differential test for the OSSYS catalog adapter (session 18 — first
// substantive commit of the OSSYS implementation chapter).
//
// The contract: V2's `Projection.Adapters.Osm.CatalogReader.parse`
// consumes V1's `osm_model.json` shape (per `SnapshotJsonBuilder` in
// the V1 trunk pipeline) and produces an equivalent V2 `Catalog`.
// The V1 JSON shape is the formal V1↔V2 contract; this test embeds a
// minimal fixture as a string constant (hermetic — no file-system
// dependency, no path resolution). The embedded copy is the V2
// contract; if V1's JSON shape changes, this test fails until the
// V2 expectation is updated, surfacing the divergence as a contract
// conversation rather than a silent drift.
//
// V1 source (the JSON shape this fixture mirrors):
//   /home/user/outsystems-ddl-exporter/src/Osm.Pipeline/SqlExtraction/SnapshotJsonBuilder.cs
//
// **Minimal slice for session 18 commit 2.** One module, one entity,
// two attributes (Id PK + Name). No references, no indexes, no
// static populations. Subsequent sessions in the OSSYS arc extend
// the slice as fixtures surface translation rules under empirical
// pressure.
// ---------------------------------------------------------------------------

let private v1MinimalFixture : string =
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

// ---------------------------------------------------------------------------
// Hand-built V2 expected Catalog. This is the contract the parser
// must satisfy. The naming convention for synthesized SsKey values
// follows the existing V2 fixture convention (`OS_MOD_<ModuleName>`,
// `OS_KIND_<ModuleName>_<EntityName>`, `OS_ATTR_<ModuleName>_<EntityName>_<AttrName>`).
// The synthesis is necessary because V1's JSON does NOT carry SSKey
// values — `SnapshotJsonBuilder` writes only names/physical-names;
// the rowsets have SSKeys but the assembled JSON discards them. The
// session 18 commit 4 captures this as a translation rule with
// rationale and re-open trigger.
// ---------------------------------------------------------------------------

let private mkKey s = SsKey.original s |> Result.value
let private mkName s = Name.create s |> Result.value

let private appCoreModuleKey = mkKey "OS_MOD_AppCore"
let private userKindKey      = mkKey "OS_KIND_AppCore_User"
let private userIdAttrKey    = mkKey "OS_ATTR_AppCore_User_Id"
let private userEmailAttrKey = mkKey "OS_ATTR_AppCore_User_Email"

let private expectedCatalog : Catalog =
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
                IsMandatory  = true }
              { SsKey        = userEmailAttrKey
                Name         = mkName "Email"
                Type         = Text
                Column       = { ColumnName = "EMAIL"; IsNullable = false }
                IsPrimaryKey = false
                IsMandatory  = true }
          ]
          References = []
          Indexes    = [] }
    { Modules = [
        { SsKey = appCoreModuleKey
          Name  = mkName "AppCore"
          Kinds = [ userKind ] } ] }

// ---------------------------------------------------------------------------
// Parser invocation — async at the boundary, sync-await for tests.
// ---------------------------------------------------------------------------

let private parseSync (source: CatalogReader.SnapshotSource) : Result<Catalog> =
    (CatalogReader.parse source).GetAwaiter().GetResult()

// ---------------------------------------------------------------------------
// Differential test — minimal slice.
// ---------------------------------------------------------------------------

[<Fact>]
let ``differential: V1 minimal-fixture JSON parses into the expected V2 Catalog`` () =
    let result = parseSync (CatalogReader.SnapshotJson v1MinimalFixture)
    match result with
    | Failure errors ->
        Assert.Fail(
            sprintf
                "Expected Result.Success; got Result.Failure with %d error(s): %A"
                errors.Length
                errors)
    | Success actual ->
        Assert.Equal<Catalog>(expectedCatalog, actual)

// ---------------------------------------------------------------------------
// Source-variant parity — SnapshotJson and SnapshotFile produce the
// same Catalog. Reserved as a Skip for now: the file-path variant
// requires touching the file system, which the chapter-open scoping
// has not yet decided whether to exercise in unit tests vs.
// integration tests. Activation lands when the file-path
// implementation does.
// ---------------------------------------------------------------------------

[<Fact(Skip = "SnapshotFile variant pending. The chapter-open scoping (DECISIONS 2026-05-15 — OSSYS adapter parse signature) names file-path as a SnapshotSource variant; activation deferred until the implementation chapter decides whether file-path tests live in this unit-test project or in a separate integration-test surface.")>]
let ``differential: SnapshotJson and SnapshotFile produce identical Catalog`` () =
    ()

// ---------------------------------------------------------------------------
// Reference-bearing fixture (session 19 — second slice in the OSSYS arc).
//
// Adds a second entity (Account) and a reference attribute on User
// (AccountId pointing at Account.Id with deleteRuleCode "Protect").
// The fixture surfaces:
//
//   - The V1 nullable deleteRuleCode → V2 closed OnDelete translation
//     rule (the deferred question session 17's ADMIRE entry named).
//   - Reference SsKey synthesis (a fourth synthesis rule on top of
//     the three module/kind/attribute rules from session 18).
//   - The relationships[] array's role in V1's JSON shape
//     (aggregation of attribute-level reference fields; the V2
//     adapter walks attributes[isReference=1] directly).
//   - Cross-attribute reference resolution (TargetKind requires
//     synthesizing the SsKey of a target entity that lives elsewhere
//     in the catalog).
//
// Same hermetic-fixture discipline as the minimal slice: embedded as
// a string constant; hand-built V2 expected output; parse + assert.
// ---------------------------------------------------------------------------

let private v1ReferenceFixture : string =
    """{
  "exportedAtUtc": "2026-05-16T00:00:00.0000000+00:00",
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
            }
          ],
          "relationships": [],
          "indexes": [],
          "triggers": []
        },
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
            },
            {
              "name": "AccountId",
              "physicalName": "ACCOUNTID",
              "originalName": null,
              "dataType": "Identifier",
              "length": null,
              "precision": null,
              "scale": null,
              "default": null,
              "isMandatory": true,
              "isIdentifier": false,
              "isAutoNumber": false,
              "isActive": true,
              "isReference": 1,
              "refEntityId": 1,
              "refEntity_name": "Account",
              "refEntity_physicalName": "OSUSR_APPCORE_ACCOUNT",
              "reference_deleteRuleCode": "Protect",
              "reference_hasDbConstraint": 1,
              "external_dbType": null,
              "physical_isPresentButInactive": 0
            }
          ],
          "relationships": [
            {
              "viaAttributeName": "AccountId",
              "toEntity_name": "Account",
              "toEntity_physicalName": "OSUSR_APPCORE_ACCOUNT",
              "hasDbConstraint": 1
            }
          ],
          "indexes": [],
          "triggers": []
        }
      ]
    }
  ]
}"""

// Reference-bearing expected catalog. Account first (alphabetical
// in the JSON; preserves order); User has a Reference pointing at
// Account via AccountId, OnDelete = NoAction (V1 "Protect" maps
// to NoAction per V1's SmoEntityEmitter convention).
let private accountKindKey  = mkKey "OS_KIND_AppCore_Account"
let private accountIdAttrKey = mkKey "OS_ATTR_AppCore_Account_Id"
let private userAccountIdAttrKey = mkKey "OS_ATTR_AppCore_User_AccountId"
let private userAccountReferenceKey = mkKey "OS_REF_AppCore_User_AccountId"

let private expectedReferenceCatalog : Catalog =
    let accountKind : Kind =
        { SsKey    = accountKindKey
          Name     = mkName "Account"
          Origin   = OsNative
          Modality = []
          Physical = { Schema = "dbo"; Table = "OSUSR_APPCORE_ACCOUNT" }
          Attributes = [
              { SsKey        = accountIdAttrKey
                Name         = mkName "Id"
                Type         = Integer
                Column       = { ColumnName = "ID"; IsNullable = false }
                IsPrimaryKey = true
                IsMandatory  = true }
          ]
          References = []
          Indexes    = [] }
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
                IsMandatory  = true }
              { SsKey        = userAccountIdAttrKey
                Name         = mkName "AccountId"
                Type         = Integer
                Column       = { ColumnName = "ACCOUNTID"; IsNullable = false }
                IsPrimaryKey = false
                IsMandatory  = true }
          ]
          References = [
              { SsKey           = userAccountReferenceKey
                Name            = mkName "AccountId"
                SourceAttribute = userAccountIdAttrKey
                TargetKind      = accountKindKey
                OnDelete        = NoAction }
          ]
          Indexes    = [] }
    { Modules = [
        { SsKey = appCoreModuleKey
          Name  = mkName "AppCore"
          Kinds = [ accountKind; userKind ] } ] }

[<Fact>]
let ``differential: V1 reference-bearing fixture parses into a Catalog with the expected V2 Reference`` () =
    let result = parseSync (CatalogReader.SnapshotJson v1ReferenceFixture)
    match result with
    | Failure errors ->
        Assert.Fail(
            sprintf
                "Expected Result.Success; got Result.Failure with %d error(s): %A"
                errors.Length
                errors)
    | Success actual ->
        Assert.Equal<Catalog>(expectedReferenceCatalog, actual)

// ---------------------------------------------------------------------------
// External-entity fixture (session 20 — third slice in the OSSYS arc).
//
// Surfaces the deferred Origin three-way collapse rule from the
// session 17 ADMIRE chapter scope. Two prior slices passed without
// exercising this question; the empirical pressure for it arrives
// only when a fixture forces it.
//
// Trace performed before writing this fixture:
//   - V1's IS-vs-Direct distinction is encoded in `EspaceKind`
//     (string column) at the rowset level (#E, espaceKind column
//     in `outsystems_metadata_rowsets.sql:96`).
//   - `SnapshotJsonBuilder` does NOT write `EspaceKind` to
//     osm_model.json. Through the JSON-snapshot path, V2 sees only
//     module-level `isSystem`/`isActive` and entity-level
//     `isExternal`/`isActive`/`isStatic`.
//   - **Through the JSON path, V2 cannot distinguish IS-vs-Direct.**
//     The bound on the Origin three-way collapse is documented and
//     resolves through the same input-path expansion as the SsKey
//     bound (SnapshotRowsets variant per `DECISIONS 2026-05-15 —
//     OSSYS adapter translation rules`, session-20 amendment).
//
// Placeholder rule under the JSON path (this slice):
//   - `isExternal: false` → OsNative
//   - `isExternal: true`  → ExternalViaIntegrationStudio
//
// The placeholder picks ExternalViaIntegrationStudio because IS
// extensions ARE the standard V1 mechanism for external entities;
// most isExternal=true cases are IS-imported. Convention-bearing
// example: V1's edge-case fixture has an "ExtBilling" module
// (the "Ext" prefix is conventional for IS-extension modules).
//
// Note: this PLACEHOLDER REPLACES the session-18 placeholder
// (`ExternalDirect`). The session-18 minimal fixture had only
// `isExternal: false` entities and never exercised the
// `isExternal: true` branch; the speculative ExternalDirect choice
// was made without empirical pressure. This slice provides the
// pressure; the rule changes under it. Captured in commit 3's
// DECISIONS amendment.
// ---------------------------------------------------------------------------

let private v1ExternalFixture : string =
    """{
  "exportedAtUtc": "2026-05-17T00:00:00.0000000+00:00",
  "modules": [
    {
      "name": "ExtBilling",
      "isSystem": false,
      "isActive": true,
      "entities": [
        {
          "name": "BillingAccount",
          "physicalName": "BILLING_ACCOUNT",
          "isStatic": false,
          "isExternal": true,
          "isActive": true,
          "db_catalog": null,
          "db_schema": "billing",
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
              "external_dbType": "int",
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

let private extBillingModuleKey = mkKey "OS_MOD_ExtBilling"
let private billingAccountKindKey = mkKey "OS_KIND_ExtBilling_BillingAccount"
let private billingAccountIdAttrKey = mkKey "OS_ATTR_ExtBilling_BillingAccount_Id"

let private expectedExternalCatalog : Catalog =
    let billingAccount : Kind =
        { SsKey    = billingAccountKindKey
          Name     = mkName "BillingAccount"
          Origin   = ExternalViaIntegrationStudio
          Modality = []
          Physical = { Schema = "billing"; Table = "BILLING_ACCOUNT" }
          Attributes = [
              { SsKey        = billingAccountIdAttrKey
                Name         = mkName "Id"
                Type         = Integer
                Column       = { ColumnName = "ID"; IsNullable = false }
                IsPrimaryKey = true
                IsMandatory  = true }
          ]
          References = []
          Indexes    = [] }
    { Modules = [
        { SsKey = extBillingModuleKey
          Name  = mkName "ExtBilling"
          Kinds = [ billingAccount ] } ] }

[<Fact>]
let ``differential: V1 external-entity fixture parses with Origin = ExternalViaIntegrationStudio`` () =
    let result = parseSync (CatalogReader.SnapshotJson v1ExternalFixture)
    match result with
    | Failure errors ->
        Assert.Fail(
            sprintf
                "Expected Result.Success; got Result.Failure with %d error(s): %A"
                errors.Length
                errors)
    | Success actual ->
        Assert.Equal<Catalog>(expectedExternalCatalog, actual)
