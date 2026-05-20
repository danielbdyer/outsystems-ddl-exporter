module Projection.Tests.EmissionFoldersOverlayTests

open Xunit
open Projection.Core
open Projection.Pipeline

/// Chapter C slice C.3 — the post-emit, pre-compose rewrite step
/// (`applyEmissionFolderOverrides` in Pipeline.fs) coverage. Exercises
/// the overlay through the public `Compose.projectWithState` surface:
/// asserts the resulting `Outputs.SsdtBundle` carries the rewritten
/// per-kind paths while preserving the SQL bodies + the manifest.json
/// entry + non-overridden kinds' default paths.

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
        },
        {
          "name": "Organization",
          "physicalName": "OSUSR_APPCORE_ORG",
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

let private kindByName (catalog: Catalog) (name: string) : Kind =
    Catalog.allKinds catalog
    |> List.find (fun k -> Name.value k.Name = name)

let private projectFor (folders: EmissionFolders) (catalog: Catalog) : Compose.Outputs =
    Compose.projectWithState Policy.empty Profile.empty EmissionPolicy.empty folders catalog
    |> fst

let private sqlPaths (outputs: Compose.Outputs) : Set<string> =
    outputs.SsdtBundle
    |> Map.toSeq
    |> Seq.map fst
    |> Seq.filter (fun p -> p.EndsWith ".sql")
    |> Set.ofSeq

// ----------------------------------------------------------------------
// Empty / no-op identity
// ----------------------------------------------------------------------

[<Fact>]
let ``C.3: empty EmissionFolders preserves V1 default Modules/<Module>/ layout`` () =
    let catalog = loadCatalog ()
    let outputs = projectFor EmissionFolders.empty catalog
    let paths = sqlPaths outputs
    Assert.Contains("Modules/AppCore/dbo.OSUSR_APPCORE_USER.sql", paths)
    Assert.Contains("Modules/AppCore/dbo.OSUSR_APPCORE_ORG.sql", paths)

[<Fact>]
let ``C.3: empty EmissionFolders yields byte-identical outputs to no-override projection`` () =
    let catalog = loadCatalog ()
    let outputsNoOverride =
        Compose.projectWithState
            Policy.empty
            Profile.empty
            EmissionPolicy.empty
            EmissionFolders.empty
            catalog
        |> fst
    let outputsViaProject = Compose.project EmissionPolicy.empty catalog
    Assert.Equal<Map<string, string>>(outputsNoOverride.SsdtBundle, outputsViaProject.SsdtBundle)

// ----------------------------------------------------------------------
// Override applied
// ----------------------------------------------------------------------

[<Fact>]
let ``C.3: single override rewrites the targeted kind's path; basename preserved`` () =
    let catalog = loadCatalog ()
    let user = kindByName catalog "User"
    let folders : EmissionFolders = {
        ByKind = Map.ofList [ user.SsKey, "Static/Reference" ]
    }
    let outputs = projectFor folders catalog
    let paths = sqlPaths outputs
    Assert.Contains("Static/Reference/dbo.OSUSR_APPCORE_USER.sql", paths)
    Assert.DoesNotContain("Modules/AppCore/dbo.OSUSR_APPCORE_USER.sql", paths)

[<Fact>]
let ``C.3: override leaves non-overridden kinds' paths unchanged`` () =
    let catalog = loadCatalog ()
    let user = kindByName catalog "User"
    let folders : EmissionFolders = {
        ByKind = Map.ofList [ user.SsKey, "Static/Reference" ]
    }
    let outputs = projectFor folders catalog
    let paths = sqlPaths outputs
    // Only User overridden; Organization stays in default location.
    Assert.Contains("Modules/AppCore/dbo.OSUSR_APPCORE_ORG.sql", paths)

[<Fact>]
let ``C.3: multi-segment folder concatenates correctly`` () =
    let catalog = loadCatalog ()
    let user = kindByName catalog "User"
    let folders : EmissionFolders = {
        ByKind = Map.ofList [ user.SsKey, "Static/Reference/Lookup" ]
    }
    let outputs = projectFor folders catalog
    let paths = sqlPaths outputs
    Assert.Contains("Static/Reference/Lookup/dbo.OSUSR_APPCORE_USER.sql", paths)

[<Fact>]
let ``C.3: SQL body content unchanged by folder override (only path is rewritten)`` () =
    let catalog = loadCatalog ()
    let user = kindByName catalog "User"
    let baseline = projectFor EmissionFolders.empty catalog
    let folders : EmissionFolders = {
        ByKind = Map.ofList [ user.SsKey, "Static" ]
    }
    let overridden = projectFor folders catalog
    let baselineUserBody =
        Map.find "Modules/AppCore/dbo.OSUSR_APPCORE_USER.sql" baseline.SsdtBundle
    let overriddenUserBody =
        Map.find "Static/dbo.OSUSR_APPCORE_USER.sql" overridden.SsdtBundle
    Assert.Equal(baselineUserBody, overriddenUserBody)

[<Fact>]
let ``C.3: override preserves manifest.json at the bundle root`` () =
    let catalog = loadCatalog ()
    let user = kindByName catalog "User"
    let folders : EmissionFolders = {
        ByKind = Map.ofList [ user.SsKey, "Static" ]
    }
    let outputs = projectFor folders catalog
    Assert.True(Map.containsKey "manifest.json" outputs.SsdtBundle)

// ----------------------------------------------------------------------
// Bundle keyset cardinality preserved
// ----------------------------------------------------------------------

[<Fact>]
let ``C.3: bundle keyset cardinality preserved under override (no .sql lost)`` () =
    let catalog = loadCatalog ()
    let user = kindByName catalog "User"
    let baseline = projectFor EmissionFolders.empty catalog
    let folders : EmissionFolders = {
        ByKind = Map.ofList [ user.SsKey, "Static" ]
    }
    let overridden = projectFor folders catalog
    Assert.Equal(baseline.SsdtBundle.Count, overridden.SsdtBundle.Count)

// ----------------------------------------------------------------------
// Multiple overrides
// ----------------------------------------------------------------------

[<Fact>]
let ``C.3: multiple overrides apply independently per SsKey`` () =
    let catalog = loadCatalog ()
    let user = kindByName catalog "User"
    let org  = kindByName catalog "Organization"
    let folders : EmissionFolders = {
        ByKind = Map.ofList [
            user.SsKey, "Static/Reference"
            org.SsKey,  "Static/Tenant"
        ]
    }
    let outputs = projectFor folders catalog
    let paths = sqlPaths outputs
    Assert.Contains("Static/Reference/dbo.OSUSR_APPCORE_USER.sql", paths)
    Assert.Contains("Static/Tenant/dbo.OSUSR_APPCORE_ORG.sql", paths)
