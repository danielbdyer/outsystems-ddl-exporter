module Projection.Tests.SnapshotScopeBindingTests

open Xunit
open Projection.Pipeline
open Projection.Adapters.OssysSql

// ---------------------------------------------------------------------
// Reconciliation slice 4 (DECISIONS 2026-06-13; adjudication C4) — the
// query-time scope pushdown binding. The opt-in gate mirrors
// `ModuleFilterBinding` (A7 polarity): empty `model.modules` ⇒ the
// show-me-everything `defaultParameters`; the include flags act only
// alongside a declared selection. `OnlyActiveAttributes` IS pushed:
// inactive duplicate attributes must be excluded at query time, before
// naming/FK/index/DDL logic runs — the axis is orthogonal to module
// scope, so it binds in both arms.
// ---------------------------------------------------------------------

let private modelWith (modules: Config.ModuleSelector list) (incSys: bool) (incInactive: bool) (onlyActive: bool) : Config.ModelSection =
    { Path                   = None
      Ossys                  = Some "env:TEST_CONN"
      Modules                = modules
      IncludeSystemModules   = incSys
      IncludeInactiveModules = incInactive
      OnlyActiveAttributes   = onlyActive }

let private model (modules: Config.ModuleSelector list) (incSys: bool) (incInactive: bool) : Config.ModelSection =
    modelWith modules incSys incInactive true

[<Fact>]
let ``empty model.modules yields defaultParameters plus the attribute-activity axis`` () =
    let p = SnapshotScopeBinding.fromModel (model [] true true)
    Assert.Equal({ MetadataSnapshotRunner.defaultParameters with OnlyActiveAttributes = true }, p)

[<Fact>]
let ``include flags alone (no modules) stay inert — the A7 polarity`` () =
    // Flags set, no modules: the binding must NOT push the flags down
    // (the in-memory filter treats them as inert; the pushdown must not
    // be more aggressive than the semantic seam). The attribute-activity
    // axis still binds — it is not a module-scope flag.
    let p = SnapshotScopeBinding.fromModel (model [] false false)
    Assert.Equal({ MetadataSnapshotRunner.defaultParameters with OnlyActiveAttributes = true }, p)

[<Fact>]
let ``onlyActiveAttributes = false preserves inactive attributes (operator opt-out)`` () =
    // If the operator asks to include inactive attributes, preserve them
    // and let later diagnostics explain any deploy conflicts.
    let unscoped = SnapshotScopeBinding.fromModel (modelWith [] true true false)
    Assert.Equal(MetadataSnapshotRunner.defaultParameters, unscoped)
    let scoped = SnapshotScopeBinding.fromModel (modelWith [ Config.ModuleSelector.Whole "Ops" ] false false false)
    Assert.False scoped.OnlyActiveAttributes

[<Fact>]
let ``declared modules push names (sorted, deduplicated) and the include flags`` () =
    let p =
        SnapshotScopeBinding.fromModel
            (model
                [ Config.ModuleSelector.Whole "Ops"
                  Config.ModuleSelector.Whole "AppCore"
                  Config.ModuleSelector.Whole "appcore" ]  // case-insensitive duplicate
                false false)
    Assert.Equal<string list>([ "AppCore"; "Ops" ], p.ModuleNames)
    Assert.False p.IncludeSystem
    Assert.False p.IncludeInactive
    Assert.Equal(None, p.EntityFilterJson)
    // The config key binds through (the fixture sets it true).
    Assert.True p.OnlyActiveAttributes

[<Fact>]
let ``entity narrowing serializes the documented JSON shape, sorted for determinism`` () =
    let p =
        SnapshotScopeBinding.fromModel
            (model
                [ Config.ModuleSelector.WithEntities ("ServiceCenter", [ "User"; "Organization" ])
                  Config.ModuleSelector.Whole "AppCore" ]
                true false)
    Assert.True p.IncludeSystem
    match p.EntityFilterJson with
    | Some json ->
        // The shape the rowsets script documents
        // (outsystems_metadata_rowsets.sql:28); entities sorted.
        Assert.Equal("""{"ServiceCenter":["Organization","User"]}""", json)
    | None -> failwith "expected an entity filter"

[<Fact>]
let ``a WithEntities selector with an empty entity list contributes no filter entry`` () =
    let p =
        SnapshotScopeBinding.fromModel
            (model [ Config.ModuleSelector.WithEntities ("AppCore", []) ] false false)
    Assert.Equal<string list>([ "AppCore" ], p.ModuleNames)
    Assert.Equal(None, p.EntityFilterJson)

[<Fact>]
let ``T1: the binding is deterministic`` () =
    let m =
        model
            [ Config.ModuleSelector.WithEntities ("B", [ "Z"; "A" ])
              Config.ModuleSelector.Whole "A" ]
            false true
    Assert.Equal(SnapshotScopeBinding.fromModel m, SnapshotScopeBinding.fromModel m)
