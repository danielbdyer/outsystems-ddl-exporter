module Projection.Tests.SourceTests

open Xunit
open Projection.Core
open Projection.Pipeline

/// Frontier base — the capability-typed input port. Discriminating predicate:
/// a capability is the presence of its function, so asking a static model to
/// profile is structurally impossible (the function isn't there).

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
let ``Source: a snapshot cannot profile; withProfile grants the capability in the type`` () =
    let snap = Source.ofJson minimalModel
    Assert.False(Source.canProfile snap)
    Assert.True(Option.isNone (Source.profile snap))         // structurally absent
    Assert.False(Source.has Source.Profile snap)
    let live = snap |> Source.withProfile (fun _ -> task { return Result.success Profile.empty })
    Assert.True(Source.canProfile live)
    Assert.True(Option.isSome (Source.profile live))         // structurally present
    Assert.True(Source.has Source.Profile live)

[<Fact>]
let ``Source: ofJson genuinely resolves a catalog through the port`` () =
    match TaskSync.run (fun () -> Source.read (Source.ofJson minimalModel)) with
    | Ok catalog -> Assert.NotEmpty(Catalog.allKinds catalog)
    | Error errs -> Assert.Fail(sprintf "expected a catalog; got %A" errs)
