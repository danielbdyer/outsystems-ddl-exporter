module Projection.Tests.TransformGroupsFilterTests

open Xunit
open Projection.Core
open Projection.Pipeline

/// Chapter C slice C.4 — the chain-filter step (`filterChainByGroups`
/// in Pipeline.fs) coverage. Exercises the filter through the public
/// `Compose.projectWithState` surface: asserts that operator-disabled
/// groups exclude the matching passes (no decisions produced for those
/// passes) while V1-parity defaults preserve the full chain.

let private v1Json : string =
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
              "name": "Id", "physicalName": "ID", "originalName": null,
              "dataType": "Identifier", "length": null, "precision": null, "scale": null,
              "default": null, "isMandatory": true, "isIdentifier": true, "isAutoNumber": true,
              "isActive": true, "isReference": 0, "refEntityId": null,
              "refEntity_name": null, "refEntity_physicalName": null,
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

let private loadCatalog () : Catalog =
    let task = Compose.readJson v1Json
    match task.GetAwaiter().GetResult() with
    | Ok c -> c
    | Error errs -> failwithf "test fixture invalid: %A" errs

let private projectFor (groups: TransformGroups) (catalog: Catalog) : ComposeState =
    Compose.projectWithState
        Policy.empty
        Profile.empty
        EmissionPolicy.empty
        EmissionFolders.empty
        groups
        catalog
    |> snd

// ----------------------------------------------------------------------
// Empty / identity
// ----------------------------------------------------------------------

[<Fact>]
let ``C.4: empty TransformGroups preserves V1-parity full-chain execution`` () =
    let catalog = loadCatalog ()
    let state = projectFor TransformGroups.empty catalog
    // V1-parity: TopologicalOrder is computed (TopologicalOrderPass ran);
    // tightening decisions surfaces (the four tightening passes ran).
    Assert.True(state.TopologicalOrder.IsSome)
    Assert.True(state.NullabilityDecisions.IsSome)
    Assert.True(state.UniqueIndexDecisions.IsSome)
    Assert.True(state.ForeignKeyDecisions.IsSome)
    Assert.True(state.CategoricalUniquenessDecisions.IsSome)

[<Fact>]
let ``C.4: every group enabled preserves V1-parity full-chain execution`` () =
    let catalog = loadCatalog ()
    let groups : TransformGroups = {
        ByGroup = Map.ofList [
            TransformGroup.Tightening, true
            TransformGroup.UserReflow, true
        ]
    }
    let state = projectFor groups catalog
    Assert.True(state.NullabilityDecisions.IsSome)
    Assert.True(state.UniqueIndexDecisions.IsSome)
    Assert.True(state.ForeignKeyDecisions.IsSome)
    Assert.True(state.CategoricalUniquenessDecisions.IsSome)

// ----------------------------------------------------------------------
// Disabling Tightening group excludes the four tightening passes
// ----------------------------------------------------------------------

[<Fact>]
let ``C.4: disabling Tightening group excludes the four tightening passes`` () =
    let catalog = loadCatalog ()
    let groups : TransformGroups = {
        ByGroup = Map.ofList [ TransformGroup.Tightening, false ]
    }
    let state = projectFor groups catalog
    // Tightening passes excluded → no decisions emitted for any of them.
    Assert.True(state.NullabilityDecisions.IsNone)
    Assert.True(state.UniqueIndexDecisions.IsNone)
    Assert.True(state.ForeignKeyDecisions.IsNone)
    Assert.True(state.CategoricalUniquenessDecisions.IsNone)

[<Fact>]
let ``C.4: disabling Tightening preserves non-tightening passes`` () =
    let catalog = loadCatalog ()
    let groups : TransformGroups = {
        ByGroup = Map.ofList [ TransformGroup.Tightening, false ]
    }
    let state = projectFor groups catalog
    // Non-tightening passes still run.
    Assert.True(state.TopologicalOrder.IsSome)

// ----------------------------------------------------------------------
// Disabling UserReflow group excludes UserFkReflowPass
// ----------------------------------------------------------------------

[<Fact>]
let ``C.4: disabling UserReflow group excludes UserFkReflowPass`` () =
    let catalog = loadCatalog ()
    let groups : TransformGroups = {
        ByGroup = Map.ofList [ TransformGroup.UserReflow, false ]
    }
    let state = projectFor groups catalog
    Assert.True(state.UserRemap.IsNone)

[<Fact>]
let ``C.4: disabling UserReflow preserves Tightening passes`` () =
    let catalog = loadCatalog ()
    let groups : TransformGroups = {
        ByGroup = Map.ofList [ TransformGroup.UserReflow, false ]
    }
    let state = projectFor groups catalog
    Assert.True(state.NullabilityDecisions.IsSome)
    Assert.True(state.UniqueIndexDecisions.IsSome)

// ----------------------------------------------------------------------
// Disabling both groups
// ----------------------------------------------------------------------

[<Fact>]
let ``C.4: disabling both groups excludes both passes' decisions`` () =
    let catalog = loadCatalog ()
    let groups : TransformGroups = {
        ByGroup = Map.ofList [
            TransformGroup.Tightening, false
            TransformGroup.UserReflow, false
        ]
    }
    let state = projectFor groups catalog
    Assert.True(state.NullabilityDecisions.IsNone)
    Assert.True(state.UniqueIndexDecisions.IsNone)
    Assert.True(state.ForeignKeyDecisions.IsNone)
    Assert.True(state.CategoricalUniquenessDecisions.IsNone)
    Assert.True(state.UserRemap.IsNone)
    // Non-tagged passes (topological order, catalog rewrites) still run.
    Assert.True(state.TopologicalOrder.IsSome)
