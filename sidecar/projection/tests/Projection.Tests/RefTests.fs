[<Xunit.Collection("Global-MutableState")>]
module Projection.Tests.RefTests

open System
open System.IO
open Xunit
open Projection.Core
open Projection.Pipeline

/// The keystone connector — the revision algebra. Every ref resolves to the
/// SAME operand type, so verbs become `verb <ref>`. The decisive test: a
/// @runId resolves to a Catalog (the Run <-> Ref connection that wires the
/// two preconditions together).

let private minimalModel = """{
  "exportedAtUtc": "2026-05-20T00:00:00.0000000+00:00",
  "modules": [ { "name": "AppCore", "isSystem": false, "isActive": true,
    "entities": [ { "name": "User", "physicalName": "OSUSR_APPCORE_USER",
      "isStatic": false, "isExternal": false, "isActive": true, "db_catalog": null, "db_schema": "dbo",
      "attributes": [ { "name": "Id", "physicalName": "ID", "originalName": null, "dataType": "rtIdentifier",
        "length": null, "precision": null, "scale": null, "default": null, "isMandatory": true,
        "isIdentifier": true, "isAutoNumber": true, "isActive": true, "isReference": 0, "refEntityId": null,
        "refEntity_name": null, "refEntity_physicalName": null, "reference_deleteRuleCode": null,
        "reference_hasDbConstraint": 0, "external_dbType": null, "physical_isPresentButInactive": 0 } ],
      "relationships": [], "indexes": [], "triggers": [] } ] } ] }"""

[<Fact>]
let ``Ref: parse reads the revision syntax`` () =
    Assert.Equal(Ref.RunArtifact "01ABC", Ref.parse "@01ABC")
    Assert.Equal(Ref.Live "uat", Ref.parse "live:uat")
    Assert.Equal(Ref.Json "{}", Ref.parse "json:{}")
    Assert.Equal(Ref.File "model.json", Ref.parse "model.json")

[<Fact>]
let ``Ref: a json ref resolves to a catalog`` () =
    match TaskSync.run (fun () -> Ref.resolveCatalog (Ref.Json minimalModel)) with
    | Ok c    -> Assert.NotEmpty(Catalog.allKinds c)
    | Error e -> Assert.Fail(sprintf "%A" e)

[<Fact>]
let ``Ref: a runId ref resolves to the run's catalog (the Run-Ref connection)`` () =
    let dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
    let prior = Environment.GetEnvironmentVariable "PROJECTION_RUNS_DIR"
    try
        Environment.SetEnvironmentVariable("PROJECTION_RUNS_DIR", dir)
        let run : Run.Run =
            { RunId = "01HUB"; Ts = "t"; Command = "x"; InputDigest = "d"; Outcome = "succeeded"
              Canary = None; Registered = 0; Applied = 0; Declined = 0; Events = []
              Artifacts = Map.ofList [ "model.json", minimalModel ] }
        Run.save dir run
        match TaskSync.run (fun () -> Ref.resolveCatalog (Ref.parse "@01HUB")) with
        | Ok c    -> Assert.NotEmpty(Catalog.allKinds c)
        | Error e -> Assert.Fail(sprintf "%A" e)
    finally
        Environment.SetEnvironmentVariable("PROJECTION_RUNS_DIR", prior)
        try Directory.Delete(dir, true) with _ -> ()

[<Fact>]
let ``Ref: a live ref fails loud (capability exists; adapter pending)`` () =
    match TaskSync.run (fun () -> Ref.resolveCatalog (Ref.Live "uat")) with
    | Ok _    -> Assert.Fail "live should be unavailable, not silently wrong"
    | Error _ -> ()
