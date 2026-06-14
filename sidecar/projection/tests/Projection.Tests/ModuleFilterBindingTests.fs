module Projection.Tests.ModuleFilterBindingTests

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Tests.IRBuilders

/// A7 — `ModuleFilterBinding.fromConfig` coverage. Verifies the binder maps the
/// `Config.ModelSection` module-selection axis (`model.modules` + the system /
/// inactive include flags) onto the typed `ModuleFilterOptions` that
/// `ModuleFilter.apply` consumes, and that an empty `model.modules` is the
/// all-permissive identity (byte-identical full-export default).

let private mustOk r =
    match r with
    | Ok v -> v
    | Error es ->
        let codes = es |> List.map (fun (e: ValidationError) -> e.Code) |> String.concat ", "
        invalidOp (sprintf "fixture: %s" codes)

let private mkSsKey label = SsKey.synthesized "OS_TEST" label |> mustOk
let private mkName s = Name.create s |> mustOk
let private mkTable s = TableId.create "dbo" s |> mustOk
let private mkAttr label = Attribute.create (mkSsKey (sprintf "ATTR_%s" label)) (mkName label) PrimitiveType.Integer
let private mkKind label = Kind.create (mkSsKey (sprintf "KIND_%s" label)) (mkName label) (mkTable label) [ mkAttr label ]
let private mkActiveModule name kinds =
    let m = mkModule (mkSsKey (sprintf "MOD_%s" name)) (mkName name) kinds
    { m with IsActive = true }

/// A `ModelSection` carrying a given module selection; the include flags follow
/// the config defaults (`includeSystemModules` / `includeInactiveModules` =
/// false, `onlyActiveAttributes` = true) so the binding's opt-in gate is the
/// behavior under test.
let private modelWith (modules: Config.ModuleSelector list) : Config.ModelSection =
    { Path                   = Some "model.json"
      Ossys                  = None
      Modules                = modules
      IncludeSystemModules   = false
      IncludeInactiveModules = false
      OnlyActiveAttributes   = true }

[<Fact>]
let ``A7: an empty model.modules binds to ModuleFilter.empty (identity, byte-identical default)`` () =
    let opts = ModuleFilterBinding.fromConfig (modelWith []) |> mustOk
    Assert.False(ModuleFilter.hasFilter opts)
    Assert.Equal<ModuleFilterOptions>(ModuleFilter.empty, opts)

[<Fact>]
let ``A7: an empty model.modules applies as the identity on any catalog`` () =
    // Even with a system + inactive module present, the no-selection config
    // leaves the catalog untouched (the opt-in gate preserves today's behavior).
    let sysKind = { mkKind "S1" with Modality = [ ModalityMark.SystemOwned ] }
    let cat =
        mkCatalog
            [ mkActiveModule "App" [ mkKind "E1" ]
              { mkActiveModule "Sys" [ sysKind ] with IsActive = false } ]
    let opts = ModuleFilterBinding.fromConfig (modelWith []) |> mustOk
    let result = ModuleFilter.apply opts cat |> mustOk
    Assert.Equal<Module list>(cat.Modules, result.Modules)

[<Fact>]
let ``A7: a Whole selector binds the module name with no entity filter`` () =
    let opts =
        ModuleFilterBinding.fromConfig (modelWith [ Config.ModuleSelector.Whole "App" ])
        |> mustOk
    Assert.Equal<Set<string>>(Set.ofList [ "app" ], opts.Modules)
    Assert.True(Map.isEmpty opts.EntityFilters)

[<Fact>]
let ``A7: a WithEntities selector binds the module name + a per-module entity filter`` () =
    let opts =
        ModuleFilterBinding.fromConfig
            (modelWith [ Config.ModuleSelector.WithEntities ("App", [ "E1"; "E2" ]) ])
        |> mustOk
    Assert.Equal<Set<string>>(Set.ofList [ "app" ], opts.Modules)
    Assert.True(opts.EntityFilters.ContainsKey "app")
    Assert.Equal<Set<string>>(Set.ofList [ "e1"; "e2" ], opts.EntityFilters.["app"].NormalizedNames)

[<Fact>]
let ``A7: a module selection narrows the catalog to the named modules (apply through the binding)`` () =
    let cat =
        mkCatalog
            [ mkActiveModule "App" [ mkKind "E1" ]
              mkActiveModule "Reporting" [ mkKind "E2" ] ]
    let opts = ModuleFilterBinding.fromConfig (modelWith [ Config.ModuleSelector.Whole "App" ]) |> mustOk
    let result = ModuleFilter.apply opts cat |> mustOk
    let names = result.Modules |> List.map (fun m -> Name.value m.Name) |> Set.ofList
    Assert.Equal<Set<string>>(Set.ofList [ "App" ], names)

[<Fact>]
let ``A7: a WithEntities selection keeps only the named entities in that module`` () =
    let cat = mkCatalog [ mkActiveModule "App" [ mkKind "E1"; mkKind "E2"; mkKind "E3" ] ]
    let opts =
        ModuleFilterBinding.fromConfig
            (modelWith [ Config.ModuleSelector.WithEntities ("App", [ "E1" ]) ])
        |> mustOk
    let result = ModuleFilter.apply opts cat |> mustOk
    let kindNames =
        result.Modules
        |> List.collect (fun m -> m.Kinds |> List.map (fun k -> Name.value k.Name))
        |> Set.ofList
    Assert.Equal<Set<string>>(Set.ofList [ "E1" ], kindNames)

[<Fact>]
let ``A7: naming a module absent from the catalog is a fail-loud moduleFilter.modules.missing`` () =
    let cat = mkCatalog [ mkActiveModule "App" [ mkKind "E1" ] ]
    let opts = ModuleFilterBinding.fromConfig (modelWith [ Config.ModuleSelector.Whole "Nope" ]) |> mustOk
    match ModuleFilter.apply opts cat with
    | Ok _ -> Assert.Fail "expected a missing-module refusal"
    | Error es -> Assert.Contains("moduleFilter.modules.missing", es |> List.map (fun e -> e.Code))

// -- A7 (no-silent-drop): the inert include flags carry a NAMED note ----------

[<Fact>]
let ``A7: include flags without a modules list carry a named inert note (never silence)`` () =
    let model = { modelWith [] with IncludeSystemModules = true }
    match ModuleFilterBinding.inertFlagNote model with
    | Some note ->
        Assert.Contains("inert", note)
        Assert.Contains("model.modules", note)
    | None -> Assert.Fail "expected the inert-flag note"

[<Fact>]
let ``A7: the inert note is absent when modules are named or the flags are unset`` () =
    // Flags set WITH a selection — the filter is live, no note.
    let withSelection =
        { modelWith [ Config.ModuleSelector.Whole "AppCore" ] with IncludeInactiveModules = true }
    Assert.Equal(None, ModuleFilterBinding.inertFlagNote withSelection)
    // No flags, no selection — the default config, no note (byte-identical).
    Assert.Equal(None, ModuleFilterBinding.inertFlagNote (modelWith []))

[<Fact>]
let ``A7: the full-export diagnostic stream surfaces moduleFilter.flagsInert`` () =
    // The structured-channel witness: a run whose config sets the flags with
    // no modules list carries the Info diagnostic on RunReport.Diagnostics.
    let modelJson = """{ "exportedAtUtc": "2026-06-10T00:00:00.0000000+00:00", "modules": [ { "name": "Solo", "isSystem": false, "isActive": true, "entities": [ { "name": "Thing", "physicalName": "OSUSR_A7_THING", "isStatic": false, "isExternal": false, "isActive": true, "db_catalog": null, "db_schema": "dbo", "attributes": [ { "name": "Id", "physicalName": "ID", "originalName": null, "dataType": "rtIdentifier", "length": null, "precision": null, "scale": null, "default": null, "isMandatory": true, "isIdentifier": true, "isAutoNumber": true, "isActive": true, "isReference": 0, "refEntityId": null, "refEntity_name": null, "refEntity_physicalName": null, "reference_deleteRuleCode": null, "reference_hasDbConstraint": 0, "external_dbType": null, "physical_isPresentButInactive": 0 } ], "relationships": [], "indexes": [], "triggers": [] } ] } ] }"""
    let tmp suffix = System.IO.Path.Combine(System.IO.Path.GetTempPath(), sprintf "projection-a7note-%s-%s" (System.Guid.NewGuid().ToString "N") suffix)
    let modelPath = tmp "model.json"
    let outDir = tmp "out"
    System.IO.File.WriteAllText(modelPath, modelJson)
    try
        let cfgJson =
            sprintf
                """{ "model": { "path": "%s", "includeSystemModules": true }, "output": { "dir": "%s" } }"""
                (modelPath.Replace("\\", "\\\\")) (outDir.Replace("\\", "\\\\"))
        let cfg = Config.parse cfgJson |> mustOk
        let report = (Compose.runWithConfig cfg).GetAwaiter().GetResult() |> mustOk
        Assert.Contains(report.Diagnostics, fun (d: DiagnosticEntry) -> d.Code = "moduleFilter.flagsInert")
    finally
        try System.IO.File.Delete modelPath with _ -> ()
        try if System.IO.Directory.Exists outDir then System.IO.Directory.Delete(outDir, true) with _ -> ()
