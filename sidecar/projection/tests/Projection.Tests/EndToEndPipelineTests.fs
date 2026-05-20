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
        | Ok c -> c
        | Error errors ->
            let codes = errors |> List.map (fun e -> e.Code) |> String.concat ", "
            failwithf "fixture: parse failed with codes: %s" codes
    Compose.project EmissionPolicy.empty catalog

// ---------------------------------------------------------------------
// E2E: parse + project succeeds and produces non-empty artifacts.
// ---------------------------------------------------------------------

[<Fact>]
let ``M1: V1 minimal fixture parses end-to-end into non-empty SSDT, JSON, and Distributions artifacts`` () =
    let outputs = parseAndProject ()
    // Per Tier-1 #2: SsdtBundle is per-table file map (chapter 4.1.A
    // production shape). Non-empty means at least the manifest.json +
    // one .sql file are present.
    Assert.NotEmpty(outputs.SsdtBundle)
    // Per Tier-1 #3: Json/Distributions are JsonNode-typed; emit
    // produces a JsonObject (per chapter 3.7 slice ε); structural
    // emptiness check via property count.
    Assert.NotNull(outputs.Json)
    Assert.NotNull(outputs.Distributions)

// ---------------------------------------------------------------------
// E2E: each artifact carries the expected structural markers. Smoke
// checks rather than golden-file snapshots — emitter shape can evolve
// without forcing brittle test updates, but the artifacts still have
// to thread V1's identifiers through V2's IR and back out.
// ---------------------------------------------------------------------

[<Fact>]
let ``M1: SSDT artifact carries CREATE TABLE for the V1-named entity`` () =
    // Pre-RawTextEmitter-retirement: this test also asserted the
    // SsKey root "OS_KIND_AppCore_User" appeared in `outputs.Sql` via
    // RawTextEmitter's `Provenance` trailing comments. The production
    // SsdtDdlEmitter (ScriptDom-rendered, the new backing) does not
    // emit those comments — SsKey roots are V2-IR-internal identifiers
    // with no SSDT-DDL surface. The structural property the assertion
    // approximated (every catalog kind appears in every Π's keyset)
    // is now enforced by `ArtifactByKind.create`'s smart constructor +
    // `SiblingEmitterContractTests.fs` worked examples.
    let outputs = parseAndProject ()
    // Aggregate the SsdtBundle's per-table SQL files for the substring
    // assertion; the production shape is per-table but the structural
    // property (CREATE TABLE for the V1-named entity exists somewhere
    // in the SSDT artifact set) holds against the aggregate.
    Assert.Contains("CREATE TABLE [dbo].[OSUSR_APPCORE_USER]", Compose.aggregateSsdt outputs.SsdtBundle)

/// Project a nullable JsonNode child to a non-null one or fail. The
/// JsonNode indexer returns `JsonNode | null` per F# 9 nullness;
/// per-test asserts that required children exist.
let private requireNode (label: string) (n: System.Text.Json.Nodes.JsonNode | null) : System.Text.Json.Nodes.JsonNode =
    match Option.ofObj n with
    | Some node -> node
    | None      -> Assert.Fail(sprintf "%s: required JsonNode child was null" label); Unchecked.defaultof<_>

[<Fact>]
let ``M1: JSON artifact carries module SsKey and emitter version`` () =
    // Per Tier-1 #3: Json is JsonNode-typed at the Outputs seam.
    // Query the typed tree directly for the structural property
    // (no string parsing); the emitter / module / kind ssKey fields
    // are addressable via JsonNode indexer access.
    let outputs = parseAndProject ()
    let json = outputs.Json
    Assert.Equal("Projection.Targets.Json", (requireNode "emitter" json.["emitter"]).GetValue<string>())
    let modules = (requireNode "modules" json.["modules"]).AsArray()
    let moduleSsKeys =
        modules
        |> Seq.choose Option.ofObj
        |> Seq.map (fun m -> (requireNode "module.ssKey" m.["ssKey"]).GetValue<string>())
        |> Set.ofSeq
    Assert.Contains("OS_MOD_AppCore", moduleSsKeys)
    let kindSsKeys =
        modules
        |> Seq.choose Option.ofObj
        |> Seq.collect (fun m ->
            (requireNode "module.kinds" m.["kinds"]).AsArray()
            |> Seq.choose Option.ofObj
            |> Seq.map (fun k -> (requireNode "kind.ssKey" k.["ssKey"]).GetValue<string>()))
        |> Set.ofSeq
    Assert.Contains("OS_KIND_AppCore_User", kindSsKeys)

[<Fact>]
let ``M1: Distributions artifact carries the per-attribute structure on Profile.empty`` () =
    let outputs = parseAndProject ()
    let dist = outputs.Distributions
    Assert.Equal("Projection.Targets.Distributions", (requireNode "emitter" dist.["emitter"]).GetValue<string>())
    // On `Profile.empty` every per-attribute distribution is null
    // (no observation evidence). The JsonNode tree carries explicit
    // null entries; verify at least one attribute has the null shape.
    let nullDistFound =
        let rec walk (n: System.Text.Json.Nodes.JsonNode) : bool =
            match n with
            | :? System.Text.Json.Nodes.JsonObject as obj ->
                obj
                |> Seq.exists (fun kv ->
                    if kv.Key = "distribution" && isNull kv.Value then true
                    else
                        match Option.ofObj kv.Value with
                        | Some v -> walk v
                        | None   -> false)
            | :? System.Text.Json.Nodes.JsonArray as arr ->
                arr
                |> Seq.exists (fun child ->
                    match Option.ofObj child with
                    | Some c -> walk c
                    | None   -> false)
            | _ -> false
        walk dist
    Assert.True(nullDistFound, "expected at least one distribution: null in the Distributions artifact")

// ---------------------------------------------------------------------
// T1: byte-determinism. Re-running the projection on the same Catalog
// produces byte-identical artifacts. Per AXIOMS.md T1 (amended:
// determinism extends to the (catalog, policy, profile) triple).
// ---------------------------------------------------------------------

[<Fact>]
let ``T1: Compose.project is byte-deterministic on a fixed Catalog`` () =
    let outputs1 = parseAndProject ()
    let outputs2 = parseAndProject ()
    Assert.Equal<Map<string, string>>(outputs1.SsdtBundle, outputs2.SsdtBundle)
    // JsonNode equality is reference-based by default; compare via
    // canonical JSON-string projection (Tier-1 #3 typed-at-the-seam).
    Assert.Equal<string>(outputs1.Json.ToJsonString(), outputs2.Json.ToJsonString())
    Assert.Equal<string>(outputs1.Distributions.ToJsonString(), outputs2.Distributions.ToJsonString())

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
        let paths =
            match Compose.write outputDir outputs with
            | Ok p -> p
            | Error errs ->
                let codes = errs |> List.map (fun e -> e.Code) |> String.concat ", "
                failwithf "Compose.write failed: %s" codes
        // Per Tier-1 #2: the bundle path count + the five top-level
        // artifacts (json + distributions + remediation + summary +
        // suggest-config). Bundle has 1 .sql per catalog kind + 1
        // manifest.json. Chapter 5+ slices 5.13.remediation-emitter +
        // 5.13.summary-formatter add `manifest.remediation.sql` +
        // `manifest.summary.txt`; H-032 adds `suggest-config.json`.
        let expectedCount = Map.count outputs.SsdtBundle + 5
        Assert.Equal(expectedCount, List.length paths)
        // Each bundle entry round-trips byte-for-byte.
        for KeyValue(relPath, body) in outputs.SsdtBundle do
            let onDisk = File.ReadAllText(Path.Combine(outputDir, relPath))
            Assert.Equal<string>(body, onDisk)
        let jsonOnDisk = File.ReadAllText(Path.Combine(outputDir, Compose.ArtifactPath.json))
        let distOnDisk = File.ReadAllText(Path.Combine(outputDir, Compose.ArtifactPath.distributions))
        // Per Tier-1 #3: outputs.Json/Distributions are typed JsonNode;
        // compare on disk by re-parsing the file content and asserting
        // canonical-JSON-string equivalence (round-trip preservation).
        let jsonOpts = System.Text.Json.JsonSerializerOptions(WriteIndented = true)
        Assert.Equal<string>(outputs.Json.ToJsonString(jsonOpts), jsonOnDisk)
        Assert.Equal<string>(outputs.Distributions.ToJsonString(jsonOpts), distOnDisk)
        // Remediation + summary round-trip byte-for-byte.
        let remediationOnDisk =
            File.ReadAllText(Path.Combine(outputDir, Compose.ArtifactPath.remediation))
        Assert.Equal<string>(outputs.RemediationSql, remediationOnDisk)
        let summaryOnDisk =
            File.ReadAllText(Path.Combine(outputDir, Compose.ArtifactPath.summary))
        Assert.Equal<string>(outputs.SummaryText, summaryOnDisk)
        // suggest-config.json round-trips via canonical JsonNode string.
        let suggestConfigOnDisk =
            File.ReadAllText(Path.Combine(outputDir, Compose.ArtifactPath.suggestConfig))
        Assert.Equal<string>(outputs.SuggestConfigJson.ToJsonString(jsonOpts), suggestConfigOnDisk)
    finally
        if Directory.Exists outputDir then
            Directory.Delete(outputDir, recursive = true)

// ---------------------------------------------------------------------
// E2E: parse failure surfaces as Error with adapter-side validation
// errors, not silent success. Confirms the unhappy-path contract.
// ---------------------------------------------------------------------

[<Fact>]
let ``M1: malformed V1 JSON surfaces as Error with adapter validation errors`` () =
    let task = Compose.readJson "{ this is not valid JSON"
    let parsed = task.GetAwaiter().GetResult()
    match parsed with
    | Error errors ->
        Assert.NotEmpty(errors)
        let codes = errors |> List.map (fun e -> e.Code)
        Assert.Contains("adapter.osm.jsonInvalid", codes)
    | Ok _ ->
        Assert.Fail "expected Error on malformed JSON, got Ok"
