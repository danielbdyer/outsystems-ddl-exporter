[<Xunit.Collection("Global-MutableState")>]
module Projection.Tests.PayOnceCombinedVerbTests

open System
open System.IO
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Targets.Data

/// PL-1 (PAY_ONCE_PLAN) — the combined-verb identity gates. The combined
/// verbs (`runWithConfigAndLoad` / `runWithConfigAndStore`) now thread the
/// publish's own `EstateAcquisition` into their second legs instead of
/// re-acquiring the estate; these facts pin the threaded forms VALUE-
/// IDENTICAL to the standalone compute-then-delegate forms (which reproduce
/// the incumbent pipeline: fresh read → hydration → chain → compose). The
/// content-bearing live-OSSYS twin (hydrated rows, wire receipts) lives in
/// `Projection.Tests.Integration/PayOnceCombinedVerbDockerTests.fs`.

/// A two-entity model: one static entity (the `Static []` marker a
/// file-sourced read yields — populations hydrate only from a live source)
/// and one plain entity, so the seed-plan projection exercises both the
/// static-marked and the bootstrap-eligible arms.
let private modelJson : string =
    """{
  "exportedAtUtc": "2026-06-03T00:00:00.0000000+00:00",
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
              "dataType": "rtIdentifier",
              "length": null, "precision": null, "scale": null, "default": null,
              "isMandatory": true, "isIdentifier": true, "isAutoNumber": true,
              "isActive": true, "isReference": 0,
              "refEntityId": null, "refEntity_name": null, "refEntity_physicalName": null,
              "reference_deleteRuleCode": null, "reference_hasDbConstraint": 0,
              "external_dbType": null, "physical_isPresentButInactive": 0
            },
            {
              "name": "Code",
              "physicalName": "CODE",
              "originalName": null,
              "dataType": "rtText",
              "length": 8, "precision": null, "scale": null, "default": null,
              "isMandatory": true, "isIdentifier": false, "isAutoNumber": false,
              "isActive": true, "isReference": 0,
              "refEntityId": null, "refEntity_name": null, "refEntity_physicalName": null,
              "reference_deleteRuleCode": null, "reference_hasDbConstraint": 0,
              "external_dbType": null, "physical_isPresentButInactive": 0
            }
          ],
          "relationships": [], "indexes": [], "triggers": []
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
              "dataType": "rtIdentifier",
              "length": null, "precision": null, "scale": null, "default": null,
              "isMandatory": true, "isIdentifier": true, "isAutoNumber": true,
              "isActive": true, "isReference": 0,
              "refEntityId": null, "refEntity_name": null, "refEntity_physicalName": null,
              "reference_deleteRuleCode": null, "reference_hasDbConstraint": 0,
              "external_dbType": null, "physical_isPresentButInactive": 0
            }
          ],
          "relationships": [], "indexes": [], "triggers": []
        }
      ]
    }
  ]
}"""

let private writeTempJson (content: string) : string =
    let path =
        Path.Combine(Path.GetTempPath(), sprintf "po1-model-%s.json" (Guid.NewGuid().ToString "N"))
    File.WriteAllText(path, content)
    path

let private writeTempConfig (extraAxes: string) (modelPath: string) (outputDir: string) : string =
    let json =
        sprintf
            """{ "model": { "path": "%s" }, "output": { "dir": "%s" }%s }"""
            (modelPath.Replace("\\", "\\\\"))
            (outputDir.Replace("\\", "\\\\"))
            extraAxes
    let path =
        Path.Combine(Path.GetTempPath(), sprintf "po1-config-%s.json" (Guid.NewGuid().ToString "N"))
    File.WriteAllText(path, json)
    path

let private tempOutputDir () : string =
    Path.Combine(Path.GetTempPath(), sprintf "po1-out-%s" (Guid.NewGuid().ToString "N"))

let private tempStorePath () : string =
    Path.Combine(Path.GetTempPath(), sprintf "po1-store-%s.json" (Guid.NewGuid().ToString "N"))

let private safeRm (dir: string) : unit =
    if Directory.Exists dir then
        try Directory.Delete(dir, recursive = true) with _ -> ()

let private safeDel (file: string) : unit =
    if File.Exists file then (try File.Delete file with _ -> ())

let private mustOk (r: Result<'a>) : 'a =
    match r with
    | Ok v -> v
    | Error es -> Assert.Fail(sprintf "%A" es); Unchecked.defaultof<'a>

let private mustStoreOk (r: Microsoft.FSharp.Core.Result<'a, LifecycleStoreError>) : 'a =
    match r with
    | Microsoft.FSharp.Core.Result.Ok v -> v
    | Microsoft.FSharp.Core.Result.Error e -> Assert.Fail(sprintf "%A" e); Unchecked.defaultof<'a>

let private tl : Timeline = Timeline.create "appcore" |> mustOk

// ===========================================================================
// The load-leg gate: the threaded seed plan equals the standalone one.
// ===========================================================================

[<Fact>]
let ``PL-1: projectSeedPlanUsing over the publish's acquisition equals the standalone emittedSeedPlan (value identity)`` () =
    let modelPath = writeTempJson modelJson
    let out = tempOutputDir ()
    let cfgPath = writeTempConfig """, "emission": { "staticSeeds": true } """ modelPath out
    try
        let cfg = Config.fromFile cfgPath |> mustOk
        let _report, acquired =
            (Compose.runWithConfigAcquiring cfg).GetAwaiter().GetResult() |> mustOk
        let threadedCatalog, threadedPlan = Compose.projectSeedPlanUsing cfg acquired |> mustOk
        let standaloneCatalog, standalonePlan =
            (Compose.emittedSeedPlan cfg).GetAwaiter().GetResult() |> mustOk
        // The load leg's episode plane and the deployed plan must be the SAME
        // VALUES whether computed fresh from cfg (the incumbent's shape) or
        // threaded from the publish's acquisition (the PL-1 shape).
        Assert.Equal<Catalog>(standaloneCatalog, threadedCatalog)
        Assert.Equal<DataEmissionComposer.LeveledDeploymentText>(standalonePlan, threadedPlan)
    finally
        safeRm out; safeDel cfgPath; safeDel modelPath

[<Fact>]
let ``PL-1: the data-off seed plan is empty and value-identical both ways`` () =
    let modelPath = writeTempJson modelJson
    let out = tempOutputDir ()
    let cfgPath = writeTempConfig "" modelPath out
    try
        let cfg = Config.fromFile cfgPath |> mustOk
        let _report, acquired =
            (Compose.runWithConfigAcquiring cfg).GetAwaiter().GetResult() |> mustOk
        let threadedCatalog, threadedPlan = Compose.projectSeedPlanUsing cfg acquired |> mustOk
        let standaloneCatalog, standalonePlan =
            (Compose.emittedSeedPlan cfg).GetAwaiter().GetResult() |> mustOk
        Assert.Equal<Catalog>(standaloneCatalog, threadedCatalog)
        Assert.True(DataEmissionComposer.LeveledDeploymentText.isEmpty threadedPlan)
        Assert.Equal<DataEmissionComposer.LeveledDeploymentText>(standalonePlan, threadedPlan)
    finally
        safeRm out; safeDel cfgPath; safeDel modelPath

// ===========================================================================
// The store-leg gate: the recorded episode's schema plane equals the
// freshly-read emitted schema.
// ===========================================================================

[<Fact>]
let ``PL-1: runWithConfigAndStore records the SAME schema plane a fresh emittedSchema read yields`` () =
    let modelPath = writeTempJson modelJson
    let out = tempOutputDir ()
    let cfgPath = writeTempConfig "" modelPath out
    let store = tempStorePath ()
    try
        let cfg = Config.fromFile cfgPath |> mustOk
        let at = DateTimeOffset(2026, 7, 2, 9, 0, 0, TimeSpan.Zero)
        let _report, legOpt =
            (Compose.runWithConfigAndStore cfg (Some store) tl Environment.Dev at)
                .GetAwaiter().GetResult() |> mustOk
        Assert.True(legOpt.IsSome, "store path supplied but no store leg returned")
        // The store leg derived its emitted plane from the publish's OWN read
        // catalog (`applyRenames` over `EstateAcquisition.ReadCatalog`); a
        // fresh `emittedSchema` read of the same unchanged model must agree.
        let recorded =
            LifecycleStore.load store |> mustStoreOk
            |> EpisodicLifecycle.latest |> Episode.schema
        let fresh = (Compose.emittedSchema cfg).GetAwaiter().GetResult() |> mustOk
        Assert.Equal<Catalog>(fresh, recorded)
    finally
        safeRm out; safeDel cfgPath; safeDel modelPath; safeDel store
