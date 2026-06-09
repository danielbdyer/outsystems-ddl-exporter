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
      OnlyActiveAttributes   = true
      ValidationOverrides    = { AllowMissingSchema = [] } }

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
