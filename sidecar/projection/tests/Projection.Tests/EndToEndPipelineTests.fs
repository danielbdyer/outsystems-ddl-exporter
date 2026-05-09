module Projection.Tests.EndToEndPipelineTests

open System.IO
open Xunit
open Projection.Core
open Projection.Pipeline

/// M1 (per the chapter-3.1 milestone sequence chosen at session 27):
/// the dogfood-frame end-to-end test. Exercises
/// `Projection.Adapters.Osm.CatalogReader → three sibling Π's` against
/// the minimal V1 fixture, asserts the artifacts are non-empty,
/// carry expected structural markers, and pass T1 byte-determinism.
///
/// Rationale per `DECISIONS 2026-05-22 — Chapter 3 sequencing`: the
/// dogfood frame ships immediately because the JsonEmitter +
/// CatalogReader pair already exists; this test wraps them in a
/// regression surface that catches drift on every `dotnet test`.
///
/// Subsequent milestones (M2 testcontainers SQL Server deploy, M3
/// read-side adapter + Catalog round-trip, M4 Tolerance taxonomy +
/// comparator, M5 full canary integration) extend this surface.

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

let private parseAndProject () : Compose.Outputs =
    let task = Compose.readJson v1MinimalFixture
    let parsed = task.GetAwaiter().GetResult()
    let catalog =
        match parsed with
        | Success c -> c
        | Failure errors ->
            let codes = errors |> List.map (fun e -> e.Code) |> String.concat ", "
            failwithf "fixture: parse failed with codes: %s" codes
    Compose.project catalog

// ---------------------------------------------------------------------
// E2E: parse + project succeeds and produces non-empty artifacts.
// ---------------------------------------------------------------------

[<Fact>]
let ``M1: V1 minimal fixture parses end-to-end into non-empty SSDT, JSON, and Distributions artifacts`` () =
    let outputs = parseAndProject ()
    Assert.NotEmpty(outputs.Sql)
    Assert.NotEmpty(outputs.Json)
    Assert.NotEmpty(outputs.Distributions)

// ---------------------------------------------------------------------
// E2E: each artifact carries the expected structural markers. Smoke
// checks rather than golden-file snapshots — emitter shape can evolve
// without forcing brittle test updates, but the artifacts still have
// to thread V1's identifiers through V2's IR and back out.
// ---------------------------------------------------------------------

[<Fact>]
let ``M1: SSDT artifact carries CREATE TABLE for the V1-named entity`` () =
    let outputs = parseAndProject ()
    Assert.Contains("CREATE TABLE [dbo].[OSUSR_APPCORE_USER]", outputs.Sql)
    Assert.Contains("OS_KIND_AppCore_User", outputs.Sql)

[<Fact>]
let ``M1: JSON artifact carries module SsKey and emitter version`` () =
    let outputs = parseAndProject ()
    Assert.Contains("\"emitter\": \"Projection.Targets.Json\"", outputs.Json)
    Assert.Contains("\"ssKey\": \"OS_MOD_AppCore\"", outputs.Json)
    Assert.Contains("\"ssKey\": \"OS_KIND_AppCore_User\"", outputs.Json)

[<Fact>]
let ``M1: Distributions artifact carries the per-attribute structure on Profile.empty`` () =
    let outputs = parseAndProject ()
    Assert.Contains("\"emitter\": \"Projection.Targets.Distributions\"", outputs.Distributions)
    Assert.Contains("\"distribution\": null", outputs.Distributions)

// ---------------------------------------------------------------------
// T1: byte-determinism. Re-running the projection on the same Catalog
// produces byte-identical artifacts. Per AXIOMS.md T1 (amended:
// determinism extends to the (catalog, policy, profile) triple).
// ---------------------------------------------------------------------

[<Fact>]
let ``T1: Compose.project is byte-deterministic on a fixed Catalog`` () =
    let outputs1 = parseAndProject ()
    let outputs2 = parseAndProject ()
    Assert.Equal(outputs1.Sql, outputs2.Sql)
    Assert.Equal(outputs1.Json, outputs2.Json)
    Assert.Equal(outputs1.Distributions, outputs2.Distributions)

// ---------------------------------------------------------------------
// E2E: writethrough — Compose.write lands the same content on disk
// that Compose.project produced in memory. Captures the seam between
// the in-memory artifact surface and the file-system surface that the
// CLI exposes.
// ---------------------------------------------------------------------

[<Fact>]
let ``M1: Compose.write writes the same bytes Compose.project produced`` () =
    let outputs = parseAndProject ()
    let outputDir =
        Path.Combine(Path.GetTempPath(), sprintf "projection-tests-%s" (System.Guid.NewGuid().ToString "N"))
    try
        let paths = Compose.write outputDir outputs
        Assert.Equal(3, List.length paths)
        let sqlOnDisk = File.ReadAllText(Path.Combine(outputDir, Compose.ArtifactPath.sql))
        let jsonOnDisk = File.ReadAllText(Path.Combine(outputDir, Compose.ArtifactPath.json))
        let distOnDisk = File.ReadAllText(Path.Combine(outputDir, Compose.ArtifactPath.distributions))
        Assert.Equal(outputs.Sql, sqlOnDisk)
        Assert.Equal(outputs.Json, jsonOnDisk)
        Assert.Equal(outputs.Distributions, distOnDisk)
    finally
        if Directory.Exists outputDir then
            Directory.Delete(outputDir, recursive = true)

// ---------------------------------------------------------------------
// E2E: parse failure surfaces as Failure with adapter-side validation
// errors, not silent success. Confirms the unhappy-path contract.
// ---------------------------------------------------------------------

[<Fact>]
let ``M1: malformed V1 JSON surfaces as Failure with adapter validation errors`` () =
    let task = Compose.readJson "{ this is not valid JSON"
    let parsed = task.GetAwaiter().GetResult()
    match parsed with
    | Failure errors ->
        Assert.NotEmpty(errors)
        let codes = errors |> List.map (fun e -> e.Code)
        Assert.Contains("adapter.osm.jsonInvalid", codes)
    | Success _ ->
        Assert.Fail "expected Failure on malformed JSON, got Success"
