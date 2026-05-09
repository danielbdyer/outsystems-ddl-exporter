module Projection.Tests.OsmCatalogReaderDifferentialTests

open Xunit
open Projection.Core
open Projection.Adapters.Osm
open Projection.Tests.Fixtures

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

let private mkName s = Name.create s |> Result.value

let private appCoreModuleKey = modKey "AppCore"
let private userKindKey      = kindKey ["AppCore"; "User"]
let private userIdAttrKey    = attrKey ["AppCore"; "User"; "Id"]
let private userEmailAttrKey = attrKey ["AppCore"; "User"; "Email"]

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

// SnapshotFile differential test stub retired per the user's
// chapter-3.5 directive (2026-05-09: "we don't need them"). The
// SnapshotFile variant's activation lands with the file-path
// implementation chapter; differential coverage will be authored
// fresh against the live implementation rather than reserved as
// a long-lived Skip stub.

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
let private accountKindKey  = kindKey ["AppCore"; "Account"]
let private accountIdAttrKey = attrKey ["AppCore"; "Account"; "Id"]
let private userAccountIdAttrKey = attrKey ["AppCore"; "User"; "AccountId"]
let private userAccountReferenceKey = refKey ["AppCore"; "User"; "AccountId"]

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
                IsMandatory = true; Length = None; Precision = None; Scale = None; IsIdentity = true }
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
                IsMandatory = true; Length = None; Precision = None; Scale = None; IsIdentity = true }
              { SsKey        = userAccountIdAttrKey
                Name         = mkName "AccountId"
                Type         = Integer
                Column       = { ColumnName = "ACCOUNTID"; IsNullable = false }
                IsPrimaryKey = false
                IsMandatory = true; Length = None; Precision = None; Scale = None; IsIdentity = false }
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

let private extBillingModuleKey = modKey "ExtBilling"
let private billingAccountKindKey = kindKey ["ExtBilling"; "BillingAccount"]
let private billingAccountIdAttrKey = attrKey ["ExtBilling"; "BillingAccount"; "Id"]

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
                IsMandatory = true; Length = None; Precision = None; Scale = None; IsIdentity = true }
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

// ---------------------------------------------------------------------------
// Mixed-active fixture (session 21 — fourth slice in the OSSYS arc).
//
// Surfaces the deferred inactive-records boundary choice from the
// session 17 ADMIRE chapter scope. Three prior slices passed
// without exercising this question; the empirical pressure for it
// arrives only when a fixture forces it.
//
// Trace performed before writing this fixture:
//   - V1 SQL carries IsActive flags through to JSON at three levels:
//     `module.isActive` (line 924), entity-level `isActive` (line
//     931), attribute-level `isActive` (line 759).
//   - V1 SQL also has SQL-layer pre-filtering parameters
//     (`@IncludeInactive` line 127; `@OnlyActiveAttributes` line
//     254) that reduce the rowsets before JSON aggregation.
//   - **The flags ARE visible to V2 through the JSON path** —
//     unlike SsKey (session 18) or EspaceKind (session 20), this
//     is NOT a member of the JSON-projection-lossiness class. The
//     boundary choice is genuine; V2 has the information.
//
// Boundary disposition for this slice (filter at adapter):
//   - V2 IR has no per-record IsActive axis today; carry-through
//     would require IR refinement (substantive — adding an IsActive
//     axis to Kind / Attribute, or a Modality.Inactive variant).
//   - "IR grows under evidence" — no current consumer demands the
//     inactive records' presence in V2's IR.
//   - A18 amended (Π consumes Catalog × Profile, never Policy)
//     argues for carry-through in principle (filtering is
//     operator intent), but the smallest honest-now implementation
//     is filter-at-adapter with the bound documented.
//
// Filter rule:
//   - `entity.isActive: false` → entity dropped from the V2 Catalog
//   - `attribute.isActive: false` → attribute dropped from its Kind's
//     Attributes list (entity remains)
//   - `module.isActive: false` → not exercised by this fixture;
//     defers until a fixture forces the question
//
// Auditability: Diagnostics-attached audit of dropped records is a
// future extension when the adapter's return shape grows from
// `Task<Result<Catalog>>` to a Diagnostics-bearing variant. Today's
// drop is silent. The DECISIONS amendment captures this.
// ---------------------------------------------------------------------------

let private v1MixedActiveFixture : string =
    """{
  "exportedAtUtc": "2026-05-18T00:00:00.0000000+00:00",
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
            },
            {
              "name": "DeprecatedField",
              "physicalName": "DEPRECATEDFIELD",
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
              "physical_isPresentButInactive": 1
            }
          ],
          "relationships": [],
          "indexes": [],
          "triggers": []
        },
        {
          "name": "RetiredEntity",
          "physicalName": "OSUSR_APPCORE_RETIRED",
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

let private activeEntityKindKey = kindKey ["AppCore"; "ActiveEntity"]
let private activeEntityIdAttrKey = attrKey ["AppCore"; "ActiveEntity"; "Id"]

let private expectedMixedActiveCatalog : Catalog =
    let activeEntity : Kind =
        { SsKey    = activeEntityKindKey
          Name     = mkName "ActiveEntity"
          Origin   = OsNative
          Modality = []
          Physical = { Schema = "dbo"; Table = "OSUSR_APPCORE_ACTIVE" }
          Attributes = [
              { SsKey        = activeEntityIdAttrKey
                Name         = mkName "Id"
                Type         = Integer
                Column       = { ColumnName = "ID"; IsNullable = false }
                IsPrimaryKey = true
                IsMandatory = true; Length = None; Precision = None; Scale = None; IsIdentity = true }
              // DeprecatedField (isActive: false) is dropped at the boundary.
          ]
          References = []
          Indexes    = [] }
    // RetiredEntity (isActive: false) is dropped at the boundary.
    { Modules = [
        { SsKey = appCoreModuleKey
          Name  = mkName "AppCore"
          Kinds = [ activeEntity ] } ] }

[<Fact>]
let ``differential: V1 mixed-active fixture filters inactive records at the boundary`` () =
    let result = parseSync (CatalogReader.SnapshotJson v1MixedActiveFixture)
    match result with
    | Failure errors ->
        Assert.Fail(
            sprintf
                "Expected Result.Success; got Result.Failure with %d error(s): %A"
                errors.Length
                errors)
    | Success actual ->
        Assert.Equal<Catalog>(expectedMixedActiveCatalog, actual)

// ---------------------------------------------------------------------------
// Index-bearing fixture (session 22 — fifth slice in the OSSYS arc).
//
// Trace performed before writing the fixture (admire-mode at slice
// level; same discipline as sessions 20-21):
//   - V1 carries the indexes[] array through to JSON; rich shape
//     including name, isPrimary, kind, isUnique, plus per-column
//     attribute, physicalColumn, ordinal, isIncluded, direction.
//     Plus storage/performance fields (isDisabled, isPadded,
//     fill_factor, etc.) and structural fields (filterDefinition,
//     dataSpace, partitionColumns, dataCompression).
//   - V2's Index shape is narrow: SsKey, Name, Columns (SsKey list),
//     IsUnique, IsPrimaryKey. Five fields.
//   - Most V1 fields don't fit V2's IR. Per the OSSYS ADMIRE entry
//     (section "What V2 will explicitly NOT carry forward"),
//     V1 included-columns (isIncluded=true) are dropped at the
//     boundary; V2's Columns carries only key columns.
//
// Classification: V2-boundary-discipline class (per session 22's
// two-classes amendment). V1 has the info; V2's IR scope is what's
// being chosen. The translation rules are V2's own architectural
// choices about scope, not input-path-bound questions.
//
// Translation rules the fixture forces:
//   1. Index SsKey synthesis:
//      OS_IDX_<modName>_<entName>_<indexName>
//   2. V1 `isUnique` → V2 Index.IsUnique (direct)
//   3. V1 `isPrimary` → V2 Index.IsPrimaryKey (direct)
//   4. V1 columns[].attribute → V2 SsKey via lookup
//      (OS_ATTR_<modName>_<entName>_<attribute>)
//   5. V1 columns[].isIncluded=true → DROP from V2's Columns list
//      (key columns only; documented in the OSSYS ADMIRE entry)
//   6. V1 columns[].ordinal → V2 preserves order via sort-by-ordinal
//      in the boundary
//
// Won't-carry-forward (extending the list):
//   - kind (V1 string; redundant with IsPrimaryKey + IsUnique)
//   - isPlatformAuto (V1 OSIDX_-prefixed marker; V2 has no axis)
//   - isDisabled, isPadded, fill_factor, ignoreDupKey,
//     allowRowLocks, allowPageLocks, noRecompute
//     (storage/performance attributes)
//   - filterDefinition (filtered indexes; V2 has no filter axis)
//   - dataSpace, partitionColumns, dataCompression (storage)
//   - columns[].direction (asc/desc; V2 has no per-column direction
//     axis today; could surface as an IR refinement under "IR
//     grows under evidence" if a future emitter needs it)
//   - columns[].physicalColumn (V2 derives physical from the
//     attribute's ColumnRealization; redundant)
//
// Fixture: one entity (User) with three indexes:
//   - PK_USER (isPrimary=true, isUnique=true, single column Id)
//   - UX_USER_EMAIL (isPrimary=false, isUnique=true, single column Email)
//   - IX_USER_NAME (isPrimary=false, isUnique=false, composite on
//     LastName + FirstName + an INCLUDE column EmailLower that V2
//     drops)
// ---------------------------------------------------------------------------

let private v1IndexFixture : string =
    """{
  "exportedAtUtc": "2026-05-19T00:00:00.0000000+00:00",
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
              "length": null, "precision": null, "scale": null, "default": null,
              "isMandatory": true, "isIdentifier": true, "isAutoNumber": true, "isActive": true,
              "isReference": 0,
              "refEntityId": null, "refEntity_name": null, "refEntity_physicalName": null,
              "reference_deleteRuleCode": null, "reference_hasDbConstraint": 0,
              "external_dbType": null, "physical_isPresentButInactive": 0
            },
            {
              "name": "Email",
              "physicalName": "EMAIL",
              "originalName": null,
              "dataType": "Text",
              "length": 250, "precision": null, "scale": null, "default": null,
              "isMandatory": true, "isIdentifier": false, "isAutoNumber": false, "isActive": true,
              "isReference": 0,
              "refEntityId": null, "refEntity_name": null, "refEntity_physicalName": null,
              "reference_deleteRuleCode": null, "reference_hasDbConstraint": 0,
              "external_dbType": null, "physical_isPresentButInactive": 0
            },
            {
              "name": "LastName",
              "physicalName": "LASTNAME",
              "originalName": null,
              "dataType": "Text",
              "length": 100, "precision": null, "scale": null, "default": null,
              "isMandatory": true, "isIdentifier": false, "isAutoNumber": false, "isActive": true,
              "isReference": 0,
              "refEntityId": null, "refEntity_name": null, "refEntity_physicalName": null,
              "reference_deleteRuleCode": null, "reference_hasDbConstraint": 0,
              "external_dbType": null, "physical_isPresentButInactive": 0
            },
            {
              "name": "FirstName",
              "physicalName": "FIRSTNAME",
              "originalName": null,
              "dataType": "Text",
              "length": 100, "precision": null, "scale": null, "default": null,
              "isMandatory": true, "isIdentifier": false, "isAutoNumber": false, "isActive": true,
              "isReference": 0,
              "refEntityId": null, "refEntity_name": null, "refEntity_physicalName": null,
              "reference_deleteRuleCode": null, "reference_hasDbConstraint": 0,
              "external_dbType": null, "physical_isPresentButInactive": 0
            },
            {
              "name": "EmailLower",
              "physicalName": "EMAILLOWER",
              "originalName": null,
              "dataType": "Text",
              "length": 250, "precision": null, "scale": null, "default": null,
              "isMandatory": false, "isIdentifier": false, "isAutoNumber": false, "isActive": true,
              "isReference": 0,
              "refEntityId": null, "refEntity_name": null, "refEntity_physicalName": null,
              "reference_deleteRuleCode": null, "reference_hasDbConstraint": 0,
              "external_dbType": null, "physical_isPresentButInactive": 0
            }
          ],
          "relationships": [],
          "indexes": [
            {
              "name": "PK_USER",
              "isPrimary": true,
              "kind": "PrimaryKey",
              "isUnique": true,
              "isPlatformAuto": 0,
              "columns": [
                { "attribute": "Id", "physicalColumn": "ID", "ordinal": 1, "isIncluded": false, "direction": "ASC" }
              ]
            },
            {
              "name": "UX_USER_EMAIL",
              "isPrimary": false,
              "kind": "Index",
              "isUnique": true,
              "isPlatformAuto": 0,
              "columns": [
                { "attribute": "Email", "physicalColumn": "EMAIL", "ordinal": 1, "isIncluded": false, "direction": "ASC" }
              ]
            },
            {
              "name": "IX_USER_NAME",
              "isPrimary": false,
              "kind": "Index",
              "isUnique": false,
              "isPlatformAuto": 0,
              "columns": [
                { "attribute": "LastName",   "physicalColumn": "LASTNAME",   "ordinal": 1, "isIncluded": false, "direction": "ASC" },
                { "attribute": "FirstName",  "physicalColumn": "FIRSTNAME",  "ordinal": 2, "isIncluded": false, "direction": "ASC" },
                { "attribute": "EmailLower", "physicalColumn": "EMAILLOWER", "ordinal": 3, "isIncluded": true,  "direction": "ASC" }
              ]
            }
          ],
          "triggers": []
        }
      ]
    }
  ]
}"""

let private userIndexLastNameAttrKey  = attrKey ["AppCore"; "User"; "LastName"]
let private userIndexFirstNameAttrKey = attrKey ["AppCore"; "User"; "FirstName"]
let private userIndexEmailLowerAttrKey = attrKey ["AppCore"; "User"; "EmailLower"]

let private pkUserIndexKey      = idxKey ["AppCore"; "User"; "PK_USER"]
let private uxUserEmailIndexKey = idxKey ["AppCore"; "User"; "UX_USER_EMAIL"]
let private ixUserNameIndexKey  = idxKey ["AppCore"; "User"; "IX_USER_NAME"]

let private expectedIndexCatalog : Catalog =
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
              { SsKey        = userIndexLastNameAttrKey
                Name         = mkName "LastName"
                Type         = Text
                Column       = { ColumnName = "LASTNAME"; IsNullable = false }
                IsPrimaryKey = false
                IsMandatory = true; Length = Some 100; Precision = None; Scale = None; IsIdentity = false }
              { SsKey        = userIndexFirstNameAttrKey
                Name         = mkName "FirstName"
                Type         = Text
                Column       = { ColumnName = "FIRSTNAME"; IsNullable = false }
                IsPrimaryKey = false
                IsMandatory = true; Length = Some 100; Precision = None; Scale = None; IsIdentity = false }
              { SsKey        = userIndexEmailLowerAttrKey
                Name         = mkName "EmailLower"
                Type         = Text
                Column       = { ColumnName = "EMAILLOWER"; IsNullable = true }
                IsPrimaryKey = false
                IsMandatory = false; Length = Some 250; Precision = None; Scale = None; IsIdentity = false }
          ]
          References = []
          Indexes = [
              { SsKey        = pkUserIndexKey
                Name         = mkName "PK_USER"
                Columns      = [ userIdAttrKey ]
                IsUnique     = true
                IsPrimaryKey = true }
              { SsKey        = uxUserEmailIndexKey
                Name         = mkName "UX_USER_EMAIL"
                Columns      = [ userEmailAttrKey ]
                IsUnique     = true
                IsPrimaryKey = false }
              { SsKey        = ixUserNameIndexKey
                Name         = mkName "IX_USER_NAME"
                Columns      = [ userIndexLastNameAttrKey; userIndexFirstNameAttrKey ]
                // EmailLower (isIncluded: true) is DROPPED at the boundary;
                // V2's Columns carries only key columns per the OSSYS
                // ADMIRE entry's "what V2 will explicitly NOT carry
                // forward" section.
                IsUnique     = false
                IsPrimaryKey = false }
          ] }
    { Modules = [
        { SsKey = appCoreModuleKey
          Name  = mkName "AppCore"
          Kinds = [ userKind ] } ] }

[<Fact>]
let ``differential: V1 index-bearing fixture parses with PK + unique + non-unique-with-include indexes`` () =
    let result = parseSync (CatalogReader.SnapshotJson v1IndexFixture)
    match result with
    | Failure errors ->
        Assert.Fail(
            sprintf
                "Expected Result.Success; got Result.Failure with %d error(s): %A"
                errors.Length
                errors)
    | Success actual ->
        Assert.Equal<Catalog>(expectedIndexCatalog, actual)

// ---------------------------------------------------------------------------
// Static-entity fixture (session 24 — sixth slice in the OSSYS arc; last
// substantive slice in chapter 2).
//
// Trace-before-fixture finding (V2-boundary-discipline class). V1's SQL
// extraction at `src/AdvancedSql/outsystems_metadata_rowsets.sql:929`
// emits `isStatic = (DataKind = 'staticEntity')`. V1's JSON projection
// at `src/Osm.Pipeline/SqlExtraction/SnapshotJsonBuilder.cs:207` writes
// it as `"isStatic": <bool>` on the entity. V2's adapter already maps
// `isStatic: true` → `Modality = [Static []]` at `CatalogReader.fs:578`;
// the implementation has shipped since session 18 but no fixture has
// covered it (the five prior fixtures all carried `isStatic: false`).
// This slice closes that gap.
//
// **Empty population is intentional.** The OSSYS adapter's
// responsibility ends at the modality flag. Static-entity *population*
// data (the rows of the lookup table) flows through a separate V1
// extraction pipeline (`static-entities.*.json`) and arrives at V2 via
// `Projection.Adapters.Sql/Static.attachStaticPopulations`, which
// composes onto a Catalog that already carries `Static []` markers.
// The split mirrors V1's own extraction split — model JSON carries
// the schema-shape fact; static-data JSON carries the population. The
// OSSYS adapter must produce `[Static []]` (empty population) so the
// downstream Static adapter has the marker to fill against.
//
// Fixture: one entity (Country) — a typical lookup table — with two
// attributes (Id PK + Code). `isStatic: true`. No references; no
// indexes. The minimal shape that exercises the modality flag.
// ---------------------------------------------------------------------------

let private v1StaticEntityFixture : string =
    """{
  "exportedAtUtc": "2026-05-20T00:00:00.0000000+00:00",
  "modules": [
    {
      "name": "AppCore",
      "isSystem": false,
      "isActive": true,
      "entities": [
        {
          "name": "Country",
          "physicalName": "OSUSR_APPCORE_COUNTRY",
          "isStatic": true,
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
              "length": null, "precision": null, "scale": null, "default": null,
              "isMandatory": true, "isIdentifier": true, "isAutoNumber": true, "isActive": true,
              "isReference": 0,
              "refEntityId": null, "refEntity_name": null, "refEntity_physicalName": null,
              "reference_deleteRuleCode": null, "reference_hasDbConstraint": 0,
              "external_dbType": null, "physical_isPresentButInactive": 0
            },
            {
              "name": "Code",
              "physicalName": "CODE",
              "originalName": null,
              "dataType": "Text",
              "length": 8, "precision": null, "scale": null, "default": null,
              "isMandatory": true, "isIdentifier": false, "isAutoNumber": false, "isActive": true,
              "isReference": 0,
              "refEntityId": null, "refEntity_name": null, "refEntity_physicalName": null,
              "reference_deleteRuleCode": null, "reference_hasDbConstraint": 0,
              "external_dbType": null, "physical_isPresentButInactive": 0
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

let private countryKindKey     = kindKey ["AppCore"; "Country"]
let private countryIdAttrKey   = attrKey ["AppCore"; "Country"; "Id"]
let private countryCodeAttrKey = attrKey ["AppCore"; "Country"; "Code"]

let private expectedStaticEntityCatalog : Catalog =
    let countryKind : Kind =
        { SsKey    = countryKindKey
          Name     = mkName "Country"
          Origin   = OsNative
          // The static-entity translation rule (session 24): V1
          // `isStatic: true` → V2 `Modality = [Static []]`. Empty
          // population is intentional — the OSSYS adapter's
          // responsibility ends at the modality flag; populations
          // flow through `Projection.Adapters.Sql/Static.fs`
          // separately.
          Modality = [ Static [] ]
          Physical = { Schema = "dbo"; Table = "OSUSR_APPCORE_COUNTRY" }
          Attributes = [
              { SsKey        = countryIdAttrKey
                Name         = mkName "Id"
                Type         = Integer
                Column       = { ColumnName = "ID"; IsNullable = false }
                IsPrimaryKey = true
                IsMandatory = true; Length = None; Precision = None; Scale = None; IsIdentity = true }
              { SsKey        = countryCodeAttrKey
                Name         = mkName "Code"
                Type         = Text
                Column       = { ColumnName = "CODE"; IsNullable = false }
                IsPrimaryKey = false
                IsMandatory = true; Length = Some 8; Precision = None; Scale = None; IsIdentity = false }
          ]
          References = []
          Indexes    = [] }
    { Modules = [
        { SsKey = appCoreModuleKey
          Name  = mkName "AppCore"
          Kinds = [ countryKind ] } ] }

[<Fact>]
let ``differential: V1 static-entity fixture parses with Modality = [Static []]`` () =
    let result = parseSync (CatalogReader.SnapshotJson v1StaticEntityFixture)
    match result with
    | Failure errors ->
        Assert.Fail(
            sprintf
                "Expected Result.Success; got Result.Failure with %d error(s): %A"
                errors.Length
                errors)
    | Success actual ->
        Assert.Equal<Catalog>(expectedStaticEntityCatalog, actual)
