module Projection.Tests.EmissionFoldersBindingTests

open Xunit
open Projection.Core
open Projection.Pipeline

/// Chapter C slice C.3 — `EmissionFoldersBinding.fromConfig` coverage.
/// Mirrors `SpecialCircumstancesBindingTests`'s structure: a small V1
/// JSON fixture loads a real catalog through `Compose.readJson`; the
/// binder is exercised across empty / valid / unresolved / invalid-
/// folder / aggregated-error paths; structured errors carry the
/// `pipeline.emissionFolders.*` code namespace.

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
        Profiler    = { Provider = Config.ProfilerProvider.Fixture; MaxConcurrency = 4 }
        Overrides   = overrides
        Emission    = {
            Ssdt = true; Dacpac = true; Sqlproj = false; Json = true; Distributions = true
            StaticSeeds = true; MigrationDependencies = true; Bootstrap = true; BootstrapAllData = false
            DecisionLog = true; Opportunities = true; Validations = true; IncludePlatformAutoIndexes = true; DeleteScope = None; Signoff = []; RenderConstraintsElegant = true; RenderDataElegant = true; EmitIdentityAnnotations = true; DataVerification = Projection.Core.DataVerification.Standard; Tolerance = None; DataStaging = Projection.Core.DataStagingPolicy.auto; DataReadConcurrency = 4; PipelinedBootstrap = true
        }
        Policy      = {
            Insertion       = "SchemaOnly"
            Tightening      = None
            TransformGroups = []
        }
        Output      = { Dir = "out/" }
    }

let private mkEntry (modName: string) (entity: string) (folder: string) : Config.EmissionFolderEntry =
    { Ref = { Module = modName; Entity = entity }; Folder = folder }

let private hasErrorCode (code: string) (errs: ValidationError list) : bool =
    errs |> List.exists (fun e -> e.Code = code)

// ----------------------------------------------------------------------
// Empty path
// ----------------------------------------------------------------------

[<Fact>]
let ``C.3: empty EmissionFolders yields EmissionFolders.empty`` () =
    let catalog = loadCatalog ()
    let cfg = mkConfig emptyOverrides
    match EmissionFoldersBinding.fromConfig catalog cfg with
    | Ok folders ->
        Assert.True(EmissionFolders.isEmpty folders)
        Assert.True(Map.isEmpty folders.ByKind)
    | Error errs -> Assert.Fail(sprintf "expected Ok, got %A" errs)

// ----------------------------------------------------------------------
// Logical-ref resolution
// ----------------------------------------------------------------------

[<Fact>]
let ``C.3: resolves valid LogicalName to kind SsKey`` () =
    let catalog = loadCatalog ()
    let overrides =
        { emptyOverrides with
            EmissionFolders = [ mkEntry "AppCore" "User" "Static/Reference" ] }
    let cfg = mkConfig overrides
    match EmissionFoldersBinding.fromConfig catalog cfg with
    | Ok folders ->
        Assert.Equal(1, Map.count folders.ByKind)
        let user =
            Catalog.allKinds catalog
            |> List.find (fun k -> Name.value k.Name = "User")
        Assert.Equal(Some "Static/Reference", Map.tryFind user.SsKey folders.ByKind)
    | Error errs -> Assert.Fail(sprintf "expected Ok, got %A" errs)

[<Fact>]
let ``C.3: unresolved LogicalName surfaces structured error`` () =
    let catalog = loadCatalog ()
    let overrides =
        { emptyOverrides with
            EmissionFolders = [ mkEntry "AppCore" "Phantom" "Static" ] }
    let cfg = mkConfig overrides
    match EmissionFoldersBinding.fromConfig catalog cfg with
    | Ok _ -> Assert.Fail("expected Error for unresolved LogicalName")
    | Error errs ->
        Assert.True(
            hasErrorCode "pipeline.emissionFolders.unresolved" errs,
            sprintf "expected pipeline.emissionFolders.unresolved; got %A" errs)

// ----------------------------------------------------------------------
// Folder-validation rejections
// ----------------------------------------------------------------------

[<Fact>]
let ``C.3: empty folder string surfaces structured error`` () =
    let catalog = loadCatalog ()
    let overrides =
        { emptyOverrides with
            EmissionFolders = [ mkEntry "AppCore" "User" "" ] }
    let cfg = mkConfig overrides
    match EmissionFoldersBinding.fromConfig catalog cfg with
    | Ok _ -> Assert.Fail("expected Error for empty folder")
    | Error errs ->
        Assert.True(hasErrorCode "pipeline.emissionFolders.invalidFolder.empty" errs)

[<Fact>]
let ``C.3: absolute folder starting with slash rejected`` () =
    let catalog = loadCatalog ()
    let overrides =
        { emptyOverrides with
            EmissionFolders = [ mkEntry "AppCore" "User" "/abs/path" ] }
    let cfg = mkConfig overrides
    match EmissionFoldersBinding.fromConfig catalog cfg with
    | Ok _ -> Assert.Fail("expected Error for absolute folder")
    | Error errs ->
        Assert.True(hasErrorCode "pipeline.emissionFolders.invalidFolder.absolute" errs)

[<Fact>]
let ``C.3: Windows drive-letter prefix rejected as absolute`` () =
    let catalog = loadCatalog ()
    let overrides =
        { emptyOverrides with
            EmissionFolders = [ mkEntry "AppCore" "User" "C:/some/path" ] }
    let cfg = mkConfig overrides
    match EmissionFoldersBinding.fromConfig catalog cfg with
    | Ok _ -> Assert.Fail("expected Error for drive-letter prefix")
    | Error errs ->
        Assert.True(hasErrorCode "pipeline.emissionFolders.invalidFolder.absolute" errs)

[<Fact>]
let ``C.3: backslash in folder rejected (force forward-slash convention)`` () =
    let catalog = loadCatalog ()
    let overrides =
        { emptyOverrides with
            EmissionFolders = [ mkEntry "AppCore" "User" "Static\\Reference" ] }
    let cfg = mkConfig overrides
    match EmissionFoldersBinding.fromConfig catalog cfg with
    | Ok _ -> Assert.Fail("expected Error for backslash")
    | Error errs ->
        Assert.True(hasErrorCode "pipeline.emissionFolders.invalidFolder.backslash" errs)

[<Fact>]
let ``C.3: parent-traversal (..) segment rejected`` () =
    let catalog = loadCatalog ()
    let overrides =
        { emptyOverrides with
            EmissionFolders = [ mkEntry "AppCore" "User" "Static/../escape" ] }
    let cfg = mkConfig overrides
    match EmissionFoldersBinding.fromConfig catalog cfg with
    | Ok _ -> Assert.Fail("expected Error for parent traversal")
    | Error errs ->
        Assert.True(hasErrorCode "pipeline.emissionFolders.invalidFolder.parentTraversal" errs)

[<Fact>]
let ``C.3: empty segment from trailing slash rejected`` () =
    let catalog = loadCatalog ()
    let overrides =
        { emptyOverrides with
            EmissionFolders = [ mkEntry "AppCore" "User" "Static/Reference/" ] }
    let cfg = mkConfig overrides
    match EmissionFoldersBinding.fromConfig catalog cfg with
    | Ok _ -> Assert.Fail("expected Error for trailing slash")
    | Error errs ->
        Assert.True(hasErrorCode "pipeline.emissionFolders.invalidFolder.emptySegment" errs)

[<Fact>]
let ``C.3: empty segment from double slash rejected`` () =
    let catalog = loadCatalog ()
    let overrides =
        { emptyOverrides with
            EmissionFolders = [ mkEntry "AppCore" "User" "Static//Reference" ] }
    let cfg = mkConfig overrides
    match EmissionFoldersBinding.fromConfig catalog cfg with
    | Ok _ -> Assert.Fail("expected Error for double slash")
    | Error errs ->
        Assert.True(hasErrorCode "pipeline.emissionFolders.invalidFolder.emptySegment" errs)

[<Fact>]
let ``C.3: platform-reserved char in segment rejected`` () =
    let catalog = loadCatalog ()
    let overrides =
        { emptyOverrides with
            EmissionFolders = [ mkEntry "AppCore" "User" "Static/Refer?ence" ] }
    let cfg = mkConfig overrides
    match EmissionFoldersBinding.fromConfig catalog cfg with
    | Ok _ -> Assert.Fail("expected Error for reserved char")
    | Error errs ->
        Assert.True(hasErrorCode "pipeline.emissionFolders.invalidFolder.invalidChar" errs)

// ----------------------------------------------------------------------
// Multi-segment forward-slash paths accepted
// ----------------------------------------------------------------------

[<Fact>]
let ``C.3: multi-segment forward-slash folder accepted`` () =
    let catalog = loadCatalog ()
    let overrides =
        { emptyOverrides with
            EmissionFolders = [ mkEntry "AppCore" "User" "Static/Reference/Lookup" ] }
    let cfg = mkConfig overrides
    match EmissionFoldersBinding.fromConfig catalog cfg with
    | Ok folders ->
        Assert.Equal(1, Map.count folders.ByKind)
        let user =
            Catalog.allKinds catalog
            |> List.find (fun k -> Name.value k.Name = "User")
        Assert.Equal(Some "Static/Reference/Lookup", Map.tryFind user.SsKey folders.ByKind)
    | Error errs -> Assert.Fail(sprintf "expected Ok, got %A" errs)

// ----------------------------------------------------------------------
// Duplicate refs — last-wins per Map.ofList semantics
// ----------------------------------------------------------------------

[<Fact>]
let ``C.3: duplicate ref takes last entry (Map.ofList semantics)`` () =
    let catalog = loadCatalog ()
    let overrides =
        { emptyOverrides with
            EmissionFolders =
                [ mkEntry "AppCore" "User" "First"
                  mkEntry "AppCore" "User" "Last" ] }
    let cfg = mkConfig overrides
    match EmissionFoldersBinding.fromConfig catalog cfg with
    | Ok folders ->
        Assert.Equal(1, Map.count folders.ByKind)
        let user =
            Catalog.allKinds catalog
            |> List.find (fun k -> Name.value k.Name = "User")
        Assert.Equal(Some "Last", Map.tryFind user.SsKey folders.ByKind)
    | Error errs -> Assert.Fail(sprintf "expected Ok, got %A" errs)

// ----------------------------------------------------------------------
// Error aggregation across entries
// ----------------------------------------------------------------------

[<Fact>]
let ``C.3: errors aggregate across multiple malformed entries`` () =
    let catalog = loadCatalog ()
    let overrides =
        { emptyOverrides with
            EmissionFolders =
                [ mkEntry "Ghost"   "Kind" "Static"
                  mkEntry "AppCore" "User" "/absolute"
                  mkEntry "AppCore" "Organization" "Bad\\Path" ] }
    let cfg = mkConfig overrides
    match EmissionFoldersBinding.fromConfig catalog cfg with
    | Ok _ -> Assert.Fail("expected Error")
    | Error errs ->
        Assert.True(hasErrorCode "pipeline.emissionFolders.unresolved" errs)
        Assert.True(hasErrorCode "pipeline.emissionFolders.invalidFolder.absolute" errs)
        Assert.True(hasErrorCode "pipeline.emissionFolders.invalidFolder.backslash" errs)

[<Fact>]
let ``C.3: same entry surfacing both unresolved-ref AND invalid-folder errors yields both`` () =
    let catalog = loadCatalog ()
    let overrides =
        { emptyOverrides with
            EmissionFolders = [ mkEntry "Ghost" "Kind" "/absolute" ] }
    let cfg = mkConfig overrides
    match EmissionFoldersBinding.fromConfig catalog cfg with
    | Ok _ -> Assert.Fail("expected Error")
    | Error errs ->
        Assert.True(hasErrorCode "pipeline.emissionFolders.unresolved" errs)
        Assert.True(hasErrorCode "pipeline.emissionFolders.invalidFolder.absolute" errs)
