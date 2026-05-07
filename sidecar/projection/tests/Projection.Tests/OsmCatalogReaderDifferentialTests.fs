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
