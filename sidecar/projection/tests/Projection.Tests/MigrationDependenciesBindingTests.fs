module Projection.Tests.MigrationDependenciesBindingTests

open System.IO
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Targets.Data

/// Migration-context wiring (2026-06-15) — `MigrationDependenciesBinding
/// .fromConfig` coverage. Mirrors `EmissionFoldersBindingTests`: a small
/// V1 JSON fixture loads a real catalog through `Compose.readJson`; the
/// binder is exercised across no-path / valid / unresolved / malformed /
/// missing-id / cell-coercion paths; structured errors carry the
/// `pipeline.migrationDependencies.*` code namespace.

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
          "name": "Role",
          "physicalName": "OSUSR_APPCORE_ROLE",
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
            },
            {
              "name": "Label", "physicalName": "LABEL", "originalName": null,
              "dataType": "Text", "length": 100, "precision": null, "scale": null,
              "default": null, "isMandatory": true, "isIdentifier": false, "isAutoNumber": false,
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

let private roleKind (catalog: Catalog) : Kind =
    Catalog.allKinds catalog |> List.find (fun k -> Name.value k.Name = "Role")

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
        Profiler    = { Provider = Config.ProfilerProvider.Fixture }
        Overrides   = overrides
        Emission    = {
            Ssdt = true; Dacpac = true; Sqlproj = false; Json = true; Distributions = true
            StaticSeeds = true; MigrationDependencies = true; Bootstrap = true; BootstrapAllData = false
            DecisionLog = true; Opportunities = true; Validations = true; IncludePlatformAutoIndexes = true; DeleteScope = None; RenderConstraintsElegant = true; EmitIdentityAnnotations = true; DataVerification = Projection.Core.DataVerification.Standard; Tolerance = None; DataStaging = Projection.Core.DataStagingPolicy.auto
        }
        Policy      = {
            Insertion       = "SchemaOnly"
            Tightening      = None
            TransformGroups = []
        }
        Output      = { Dir = "out/" }
    }

/// Write `content` to a fresh temp file, run `f` against its path, then
/// delete the file. The migration file is config-relative-to-cwd (the
/// `model.path` precedent), so an absolute temp path is faithful.
let private withTempFile (content: string) (f: string -> 'a) : 'a =
    let path = Path.Combine(Path.GetTempPath(), sprintf "migdeps_%s.json" (System.Guid.NewGuid().ToString("N")))
    File.WriteAllText(path, content)
    try f path
    finally (try File.Delete path with _ -> ())

let private cfgWithPath (path: string) : Config.Config =
    mkConfig { emptyOverrides with MigrationDependencies = Some { Path = path } }

let private hasErrorCode (code: string) (errs: ValidationError list) : bool =
    errs |> List.exists (fun e -> e.Code = code)

// ----------------------------------------------------------------------
// No path — the empty context (byte-identical to the prior threading)
// ----------------------------------------------------------------------

[<Fact>]
let ``no migrationDependencies path yields the empty context`` () =
    let catalog = loadCatalog ()
    let cfg = mkConfig emptyOverrides
    match MigrationDependenciesBinding.fromConfig catalog cfg with
    | Ok ctx ->
        Assert.Empty ctx.Rows
        Assert.True(Set.isEmpty (MigrationDependenciesBinding.kindKeysOf ctx))
    | Error errs -> Assert.Fail(sprintf "expected Ok, got %A" errs)

// ----------------------------------------------------------------------
// Valid file — logical resolution + row synthesis
// ----------------------------------------------------------------------

[<Fact>]
let ``valid file resolves logical Module.Entity to the kind SsKey with synthesized row identity`` () =
    let catalog = loadCatalog ()
    let role = roleKind catalog
    let json =
        """{ "kinds": [ { "module": "AppCore", "entity": "Role",
              "rows": [ { "id": "Admin",   "values": { "Id": "1", "Label": "Administrator" } },
                        { "id": "Auditor", "values": { "Id": "2", "Label": "Auditor" } } ] } ] }"""
    let ctx =
        withTempFile json (fun path ->
            match MigrationDependenciesBinding.fromConfig catalog (cfgWithPath path) with
            | Ok c -> c
            | Error errs -> failwithf "expected Ok, got %A" errs)
    Assert.Equal(2, List.length ctx.Rows)
    // Every row owns the resolved Role kind key.
    Assert.True(ctx.Rows |> List.forall (fun r -> r.KindKey = role.SsKey))
    // The complement-exclusion set is exactly { Role }.
    Assert.Equal<Set<SsKey>>(Set.ofList [ role.SsKey ], MigrationDependenciesBinding.kindKeysOf ctx)
    // Values resolve by logical column name.
    let admin = ctx.Rows |> List.find (fun r -> Map.tryFind (Name.create "Label" |> Result.value) r.Values = Some "Administrator")
    Assert.Equal(Some "1", Map.tryFind (Name.create "Id" |> Result.value) admin.Values)
    // Row identities are distinct (synthesized from id).
    Assert.Equal(2, ctx.Rows |> List.map (fun r -> r.Identifier) |> List.distinct |> List.length)

// ----------------------------------------------------------------------
// Cell coercion — number / bool / null
// ----------------------------------------------------------------------

[<Fact>]
let ``scalar cells coerce: number and bool render raw, null is the empty-string NULL convention`` () =
    let catalog = loadCatalog ()
    let json =
        """{ "kinds": [ { "module": "AppCore", "entity": "Role",
              "rows": [ { "id": "R", "values": { "Id": 7, "Label": null } } ] } ] }"""
    let ctx =
        withTempFile json (fun path ->
            match MigrationDependenciesBinding.fromConfig catalog (cfgWithPath path) with
            | Ok c -> c
            | Error errs -> failwithf "expected Ok, got %A" errs)
    let row = List.exactlyOne ctx.Rows
    Assert.Equal(Some "7", Map.tryFind (Name.create "Id" |> Result.value) row.Values)
    Assert.Equal(Some "", Map.tryFind (Name.create "Label" |> Result.value) row.Values)

// ----------------------------------------------------------------------
// Fail-loud paths
// ----------------------------------------------------------------------

[<Fact>]
let ``unresolved logical kind fails loud`` () =
    let catalog = loadCatalog ()
    let json = """{ "kinds": [ { "module": "AppCore", "entity": "Ghost", "rows": [] } ] }"""
    let result =
        withTempFile json (fun path -> MigrationDependenciesBinding.fromConfig catalog (cfgWithPath path))
    match result with
    | Ok _ -> Assert.Fail "expected an unresolved-kind error"
    | Error errs -> Assert.True(hasErrorCode "pipeline.migrationDependencies.unresolvedKind" errs)

[<Fact>]
let ``malformed JSON fails loud`` () =
    let catalog = loadCatalog ()
    let result =
        withTempFile "{ not json" (fun path -> MigrationDependenciesBinding.fromConfig catalog (cfgWithPath path))
    match result with
    | Ok _ -> Assert.Fail "expected a malformed-JSON error"
    | Error errs -> Assert.True(hasErrorCode "pipeline.migrationDependencies.malformedJson" errs)

[<Fact>]
let ``a row without an id fails loud`` () =
    let catalog = loadCatalog ()
    let json = """{ "kinds": [ { "module": "AppCore", "entity": "Role", "rows": [ { "values": { "Id": "1" } } ] } ] }"""
    let result =
        withTempFile json (fun path -> MigrationDependenciesBinding.fromConfig catalog (cfgWithPath path))
    match result with
    | Ok _ -> Assert.Fail "expected a row-missing-id error"
    | Error errs -> Assert.True(hasErrorCode "pipeline.migrationDependencies.rowMissingId" errs)

[<Fact>]
let ``a missing file fails loud`` () =
    let catalog = loadCatalog ()
    let path = Path.Combine(Path.GetTempPath(), sprintf "absent_%s.json" (System.Guid.NewGuid().ToString("N")))
    match MigrationDependenciesBinding.fromConfig catalog (cfgWithPath path) with
    | Ok _ -> Assert.Fail "expected a read-failed error"
    | Error errs -> Assert.True(hasErrorCode "pipeline.migrationDependencies.readFailed" errs)
