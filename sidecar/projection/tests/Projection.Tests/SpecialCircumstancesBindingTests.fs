module Projection.Tests.SpecialCircumstancesBindingTests

open Xunit
open Projection.Core
open Projection.Pipeline

/// Chapter C slice C.2 — `SpecialCircumstancesBinding.fromConfig`
/// coverage. Mirrors `TighteningBindingTests`'s structure: a small
/// V1 JSON fixture loads a real catalog through `Compose.readJson`;
/// the binder is exercised across empty / valid / unresolvable / aggregated
/// paths; structured errors carry the `pipeline.specialCircumstances.*`
/// code namespace.

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
    let task () = Compose.readJson v1Json
    match TaskSync.run task with
    | Ok c -> c
    | Error errs -> failwithf "test fixture invalid: %A" errs

let private emptyOverrides : Config.OverridesSection = {
    TableRenames           = []
    MigrationDependencies  = None
    StaticData             = None
    CircularDependencies   = None
    AllowMissingPrimaryKey = []
    EmissionFolders        = []
}

let private mkConfig (overrides: Config.OverridesSection) : Config.Config =
    {
        Model       = {
            Path                   = Some "ignored.json"
            Ossys                  = None
            Modules                = []
            IncludeSystemModules   = false
            IncludeInactiveModules = false
            OnlyActiveAttributes   = true
        }
        Profile     = { Path = None }
        Profiler    = { Provider = "fixture" }
        Overrides   = overrides
        Emission    = {
            Ssdt = true; Dacpac = true; Json = true; Distributions = true
            StaticSeeds = true; MigrationDependencies = true; Bootstrap = true; BootstrapAllData = false
            DecisionLog = true; Opportunities = true; Validations = true; IncludePlatformAutoIndexes = true; DeleteScope = None; RenderConstraintsElegant = true; EmitIdentityAnnotations = true; DataVerification = Projection.Core.DataVerification.Standard
        }
        Policy      = {
            Insertion       = "SchemaOnly"
            Tightening      = None
            TransformGroups = []
        }
        Output      = { Dir = "out/" }
    }

let private hasErrorCode (code: string) (errs: ValidationError list) : bool =
    errs |> List.exists (fun e -> e.Code = code)

// ----------------------------------------------------------------------
// Empty paths
// ----------------------------------------------------------------------

[<Fact>]
let ``C.2: empty Overrides yields SpecialCircumstances.empty`` () =
    let catalog = loadCatalog ()
    let cfg = mkConfig emptyOverrides
    match SpecialCircumstancesBinding.fromConfig catalog cfg with
    | Ok sc ->
        Assert.True(Set.isEmpty sc.AllowedMissingPrimaryKeys)
        Assert.True(Set.isEmpty sc.AllowedCycles)
    | Error errs -> Assert.Fail(sprintf "expected Ok, got errors: %A" errs)

// ----------------------------------------------------------------------
// AllowMissingPrimaryKey binder
// ----------------------------------------------------------------------

[<Fact>]
let ``C.2: AllowMissingPrimaryKey resolves valid LogicalName to kind SsKey`` () =
    let catalog = loadCatalog ()
    let overrides =
        { emptyOverrides with
            AllowMissingPrimaryKey = [ { Module = "AppCore"; Entity = "User" } ] }
    let cfg = mkConfig overrides
    match SpecialCircumstancesBinding.fromConfig catalog cfg with
    | Ok sc ->
        Assert.Equal(1, Set.count sc.AllowedMissingPrimaryKeys)
        // The resolved SsKey should match the User kind's SsKey.
        let user =
            Catalog.allKinds catalog
            |> List.find (fun k -> Name.value k.Name = "User")
        Assert.Contains(user.SsKey, sc.AllowedMissingPrimaryKeys)
    | Error errs -> Assert.Fail(sprintf "expected Ok, got %A" errs)

[<Fact>]
let ``C.2: AllowMissingPrimaryKey unresolved LogicalName surfaces structured error`` () =
    let catalog = loadCatalog ()
    let overrides =
        { emptyOverrides with
            AllowMissingPrimaryKey = [ { Module = "AppCore"; Entity = "NoSuchKind" } ] }
    let cfg = mkConfig overrides
    match SpecialCircumstancesBinding.fromConfig catalog cfg with
    | Ok _ -> Assert.Fail("expected Error for unresolved LogicalName")
    | Error errs ->
        Assert.True(
            hasErrorCode "pipeline.specialCircumstances.allowMissingPk.unresolved" errs,
            sprintf "expected pipeline.specialCircumstances.allowMissingPk.unresolved; got %A" errs)

[<Fact>]
let ``C.2: AllowMissingPrimaryKey aggregates errors across multiple unresolved entries`` () =
    let catalog = loadCatalog ()
    let overrides =
        { emptyOverrides with
            AllowMissingPrimaryKey =
                [ { Module = "Ghost"; Entity = "One" }
                  { Module = "Ghost"; Entity = "Two" } ] }
    let cfg = mkConfig overrides
    match SpecialCircumstancesBinding.fromConfig catalog cfg with
    | Ok _ -> Assert.Fail("expected Error")
    | Error errs ->
        let unresolved =
            errs
            |> List.filter (fun e -> e.Code = "pipeline.specialCircumstances.allowMissingPk.unresolved")
        Assert.Equal(2, unresolved.Length)

// ----------------------------------------------------------------------
// AllowedCycles binder
// ----------------------------------------------------------------------

[<Fact>]
let ``C.2: AllowedCycles resolves logical { module, entity } entries to SsKey set`` () =
    let catalog = loadCatalog ()
    let cycle : Config.CircularDependencyCycle = {
        Order = [
            { Module = "AppCore"; Entity = "User";         Position = 1 }
            { Module = "AppCore"; Entity = "Organization"; Position = 2 }
        ]
    }
    let overrides =
        { emptyOverrides with
            CircularDependencies = Some { AllowedCycles = [ cycle ]; StrictMode = false } }
    let cfg = mkConfig overrides
    match SpecialCircumstancesBinding.fromConfig catalog cfg with
    | Ok sc ->
        Assert.Equal(1, Set.count sc.AllowedCycles)
        // The resolved cycle should carry both kinds' SsKeys.
        let resolvedCycle = sc.AllowedCycles |> Set.toList |> List.head
        Assert.Equal(2, Set.count resolvedCycle)
    | Error errs -> Assert.Fail(sprintf "expected Ok, got %A" errs)

[<Fact>]
let ``C.2: AllowedCycles unresolved logical ref surfaces structured error`` () =
    let catalog = loadCatalog ()
    let cycle : Config.CircularDependencyCycle = {
        Order = [
            { Module = "Ghost"; Entity = "Nope"; Position = 1 }
        ]
    }
    let overrides =
        { emptyOverrides with
            CircularDependencies = Some { AllowedCycles = [ cycle ]; StrictMode = false } }
    let cfg = mkConfig overrides
    match SpecialCircumstancesBinding.fromConfig catalog cfg with
    | Ok _ -> Assert.Fail("expected Error")
    | Error errs ->
        Assert.True(
            hasErrorCode "pipeline.specialCircumstances.allowedCycle.unresolved" errs,
            sprintf "expected pipeline.specialCircumstances.allowedCycle.unresolved; got %A" errs)

// ----------------------------------------------------------------------
// Aggregated errors across axes
// ----------------------------------------------------------------------

[<Fact>]
let ``C.2: errors aggregate across both allowMissingPk and allowedCycles axes`` () =
    let catalog = loadCatalog ()
    let cycle : Config.CircularDependencyCycle = {
        Order = [ { Module = "Ghost"; Entity = "Nope2"; Position = 1 } ]
    }
    let overrides =
        { emptyOverrides with
            AllowMissingPrimaryKey = [ { Module = "Ghost"; Entity = "Kind" } ]
            CircularDependencies   = Some { AllowedCycles = [ cycle ]; StrictMode = false } }
    let cfg = mkConfig overrides
    match SpecialCircumstancesBinding.fromConfig catalog cfg with
    | Ok _ -> Assert.Fail("expected Error")
    | Error errs ->
        Assert.True(hasErrorCode "pipeline.specialCircumstances.allowMissingPk.unresolved" errs)
        Assert.True(hasErrorCode "pipeline.specialCircumstances.allowedCycle.unresolved" errs)
