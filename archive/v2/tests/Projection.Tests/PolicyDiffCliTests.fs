module Projection.Tests.PolicyDiffCliTests

open System
open System.IO
open Xunit
open Projection.Core
open Projection.Pipeline

// §5.6 — the `policy-diff` verb's orchestrator (`PolicyDiff.diffConfigs`):
// load the shared Catalog from config-a's Model.Path, bind a Policy from each
// config via Compose.buildPolicyFromConfig, and diff. These tests exercise the
// config→policy→diff glue (the new surface); the pure diff itself is covered by
// PolicyDiffTests. Pure pool — no Docker (Profile.empty; the model is a temp file).

let private v1MinimalFixture : string =
    """{
  "exportedAtUtc": "2026-05-20T00:00:00.0000000+00:00",
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
              "dataType": "rtIdentifier",
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

let private writeTempJson (content: string) : string =
    let path =
        Path.Combine(
            Path.GetTempPath(),
            sprintf "projection-policydiff-%s.json" (Guid.NewGuid().ToString("N")))
    File.WriteAllText(path, content)
    path

let private modelFile = writeTempJson v1MinimalFixture

/// A config that targets the shared model and sets one Insertion-axis value.
let private configWith (insertion: string) : Config.Config =
    let json =
        sprintf
            """{ "model": { "path": "%s" }, "output": { "dir": "%s" }, "policy": { "insertion": "%s" } }"""
            (modelFile.Replace("\\", "\\\\"))
            ((Path.GetTempPath()).Replace("\\", "\\\\"))
            insertion
    Config.parse json |> Result.value

let private diff (a: Config.Config) (b: Config.Config) : FullProjectionDiff =
    TaskSync.run (fun () -> PolicyDiff.diffConfigs a b) |> Result.value

[<Fact>]
let ``policy-diff: differing insertion axis reports AnyChanged with Insertion changed`` () =
    let d = diff (configWith "SchemaOnly") (configWith "Merge")
    Assert.True(d.StructuralDiff.AnyChanged)
    Assert.True(d.StructuralDiff.Insertion.Changed)
    // Axes we did not vary stay equal — the diff is precise, not blanket.
    Assert.False(d.StructuralDiff.Tightening.Changed)
    Assert.False(d.StructuralDiff.Selection.Changed)

[<Fact>]
let ``policy-diff: identical configs report no change on any axis`` () =
    let d = diff (configWith "SchemaOnly") (configWith "SchemaOnly")
    Assert.False(d.StructuralDiff.AnyChanged)
    Assert.Empty(d.ChangedKinds)
