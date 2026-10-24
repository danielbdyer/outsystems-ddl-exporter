module Projection.Tests.ModuleFilterTests

open System.Collections.Generic
open Xunit
open Projection.Core
open Projection.Tests.IRBuilders

// ---------------------------------------------------------------------------
// Chapter B.4 slice 4 — ModuleFilter carbon-copy from V1
// (`src/Osm.Pipeline/ModelIngestion/ModuleFilter.cs`,
//  `src/Osm.Domain/Configuration/ModuleFilterOptions.cs`,
//  `src/Osm.Domain/Configuration/ModuleEntityFilterOptions.cs`).
//
// Slice 7 (`full-export` CLI) consumes this filter via `Pipeline.Config
// .ModelSection` (`Modules` / `IncludeSystemModules` /
// `IncludeInactiveModules` / per-module entity restrictions). The pillar
// 9 classification is `OperatorIntent of Selection` — every axis here
// narrows the universe of work the downstream pipeline operates against.
//
// V1 parity tests: each V1 `ModuleFilterTests` scenario has a V2 mirror
// here adapted to V2's Catalog / Module / Kind / ModalityMark vocabulary.
// V1's `IsSystemModule` per-module bit becomes V2's per-kind
// `ModalityMark.SystemOwned`; the V2 translation is "a module is treated
// as system-owned iff every kind in it carries SystemOwned" — see
// `ModuleFilter.fs` `isAllSystemModule` docstring.
// ---------------------------------------------------------------------------

let private mustOk r =
    match r with
    | Ok v -> v
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        invalidOp (sprintf "fixture: %s" codes)

let private mkSsKey label = SsKey.synthesized "OS_TEST" label |> mustOk
let private mkName s = Name.create s |> mustOk
let private mkTable s = TableId.create "dbo" s |> mustOk

let private mkAttr label =
    Attribute.create (mkSsKey (sprintf "ATTR_%s" label)) (mkName label) PrimitiveType.Integer

let private mkKind label =
    let attr = mkAttr label
    Kind.create (mkSsKey (sprintf "KIND_%s" label)) (mkName label) (mkTable label) [ attr ]

let private mkSystemKind label =
    let k = mkKind label
    { k with Modality = [ ModalityMark.SystemOwned ] }

let private mkInactiveKind label =
    let k = mkKind label
    { k with IsActive = false }

let private mkActiveModule name kinds =
    let m = mkModule (mkSsKey (sprintf "MOD_%s" name)) (mkName name) kinds
    { m with IsActive = true }

let private mkInactiveModule name kinds =
    let m = mkActiveModule name kinds
    { m with IsActive = false }

let private mkCat modules = mkCatalog modules

// ---------------------------------------------------------------------------
// `Options.empty` and `hasFilter`.
// ---------------------------------------------------------------------------

[<Fact>]
let ``ModuleFilter.empty has no filter (hasFilter returns false)`` () =
    Assert.False(ModuleFilter.hasFilter ModuleFilter.empty)

[<Fact>]
let ``ModuleFilter.apply empty preserves the catalog unchanged`` () =
    let cat = mkCat [ mkActiveModule "M1" [ mkKind "E1" ] ]
    let result = ModuleFilter.apply ModuleFilter.empty cat |> mustOk
    Assert.Equal<Module list>(cat.Modules, result.Modules)

[<Fact>]
let ``ModuleFilter.empty has IncludeSystemModules + IncludeInactiveModules true`` () =
    Assert.True(ModuleFilter.empty.IncludeSystemModules)
    Assert.True(ModuleFilter.empty.IncludeInactiveModules)
    Assert.True(Set.isEmpty ModuleFilter.empty.Modules)
    Assert.True(Map.isEmpty ModuleFilter.empty.EntityFilters)

[<Fact>]
let ``ModuleFilter.hasFilter true when at least one axis differs from empty`` () =
    let opts =
        { ModuleFilter.empty with
            Modules = Set.ofList [ "m1" ]
            ModulesOriginal = [ "M1" ] }
    Assert.True(ModuleFilter.hasFilter opts)

// ---------------------------------------------------------------------------
// Module name filtering — operator-supplied include list.
// ---------------------------------------------------------------------------

[<Fact>]
let ``ModuleFilter.apply keeps only the modules named in the include list`` () =
    let cat =
        mkCat
            [ mkActiveModule "M1" [ mkKind "E1" ]
              mkActiveModule "M2" [ mkKind "E2" ]
              mkActiveModule "M3" [ mkKind "E3" ] ]
    let opts =
        ModuleFilter.createOptions [ "M1"; "M3" ] true true [] |> mustOk
    let result = ModuleFilter.apply opts cat |> mustOk
    let names = result.Modules |> List.map (fun m -> Name.value m.Name) |> Set.ofList
    Assert.Equal<Set<string>>(Set.ofList [ "M1"; "M3" ], names)

[<Fact>]
let ``ModuleFilter.apply module-name match is case-insensitive`` () =
    let cat =
        mkCat
            [ mkActiveModule "AdminCenter" [ mkKind "E1" ]
              mkActiveModule "Sales" [ mkKind "E2" ] ]
    let opts =
        ModuleFilter.createOptions [ "admincenter" ] true true [] |> mustOk
    let result = ModuleFilter.apply opts cat |> mustOk
    Assert.Single(result.Modules) |> ignore
    Assert.Equal("AdminCenter", Name.value result.Modules.Head.Name)

[<Fact>]
let ``ModuleFilter.apply unknown module name surfaces moduleFilter.modules.missing`` () =
    let cat = mkCat [ mkActiveModule "M1" [ mkKind "E1" ] ]
    let opts =
        ModuleFilter.createOptions [ "M1"; "DoesNotExist" ] true true [] |> mustOk
    let result = ModuleFilter.apply opts cat
    match result with
    | Ok _ -> Assert.Fail("expected failure for unknown module name")
    | Error es ->
        Assert.Single(es) |> ignore
        Assert.Equal("moduleFilter.modules.missing", es.Head.Code)
        Assert.Contains("DoesNotExist", es.Head.Message)

// ---------------------------------------------------------------------------
// IncludeSystemModules — V2 translation of V1's IsSystemModule.
// ---------------------------------------------------------------------------

[<Fact>]
let ``ModuleFilter.apply drops modules where every kind is SystemOwned when IncludeSystemModules false`` () =
    let cat =
        mkCat
            [ mkActiveModule "App" [ mkKind "E1" ]
              mkActiveModule "Sys" [ mkSystemKind "S1"; mkSystemKind "S2" ] ]
    let opts =
        ModuleFilter.createOptions [] false true [] |> mustOk
    let result = ModuleFilter.apply opts cat |> mustOk
    Assert.Single(result.Modules) |> ignore
    Assert.Equal("App", Name.value result.Modules.Head.Name)

[<Fact>]
let ``ModuleFilter.apply IncludeSystemModules true preserves SystemOwned modules`` () =
    let cat =
        mkCat
            [ mkActiveModule "App" [ mkKind "E1" ]
              mkActiveModule "Sys" [ mkSystemKind "S1" ] ]
    let opts = ModuleFilter.createOptions [] true true [] |> mustOk
    // hasFilter is false here (every axis matches empty); apply returns catalog unchanged.
    Assert.False(ModuleFilter.hasFilter opts)
    let result = ModuleFilter.apply opts cat |> mustOk
    Assert.Equal(2, result.Modules.Length)

[<Fact>]
let ``ModuleFilter.apply mixed-modality module is kept even when IncludeSystemModules false`` () =
    let cat =
        mkCat
            [ mkActiveModule "Mixed" [ mkKind "App"; mkSystemKind "Sys" ] ]
    let opts = ModuleFilter.createOptions [] false true [] |> mustOk
    let result = ModuleFilter.apply opts cat |> mustOk
    // Mixed module passes (not every kind is SystemOwned); all its kinds preserved.
    Assert.Single(result.Modules) |> ignore
    Assert.Equal(2, result.Modules.Head.Kinds.Length)

// ---------------------------------------------------------------------------
// IncludeInactiveModules — drops inactive modules + inactive kinds.
// ---------------------------------------------------------------------------

[<Fact>]
let ``ModuleFilter.apply drops inactive modules when IncludeInactiveModules false`` () =
    let cat =
        mkCat
            [ mkActiveModule "Active" [ mkKind "E1" ]
              mkInactiveModule "Inactive" [ mkKind "E2" ] ]
    let opts = ModuleFilter.createOptions [] true false [] |> mustOk
    let result = ModuleFilter.apply opts cat |> mustOk
    Assert.Single(result.Modules) |> ignore
    Assert.Equal("Active", Name.value result.Modules.Head.Name)

[<Fact>]
let ``ModuleFilter.apply drops inactive kinds within active modules when IncludeInactiveModules false`` () =
    let cat =
        mkCat
            [ mkActiveModule "M1" [ mkKind "Active1"; mkInactiveKind "Inactive1"; mkKind "Active2" ] ]
    let opts = ModuleFilter.createOptions [] true false [] |> mustOk
    let result = ModuleFilter.apply opts cat |> mustOk
    Assert.Single(result.Modules) |> ignore
    let keptKinds = result.Modules.Head.Kinds |> List.map (fun k -> Name.value k.Name) |> Set.ofList
    Assert.Equal<Set<string>>(Set.ofList [ "Active1"; "Active2" ], keptKinds)

[<Fact>]
let ``ModuleFilter.apply drops module where every kind is inactive when IncludeInactiveModules false`` () =
    let cat =
        mkCat
            [ mkActiveModule "Live" [ mkKind "E1" ]
              mkActiveModule "Dead" [ mkInactiveKind "D1"; mkInactiveKind "D2" ] ]
    let opts = ModuleFilter.createOptions [] true false [] |> mustOk
    let result = ModuleFilter.apply opts cat |> mustOk
    Assert.Single(result.Modules) |> ignore
    Assert.Equal("Live", Name.value result.Modules.Head.Name)

// ---------------------------------------------------------------------------
// Filter-empties-everything failure mode.
// ---------------------------------------------------------------------------

[<Fact>]
let ``ModuleFilter.apply surfaces moduleFilter.modules.empty when filter removes every module`` () =
    let cat = mkCat [ mkActiveModule "Sys" [ mkSystemKind "S1" ] ]
    let opts = ModuleFilter.createOptions [] false true [] |> mustOk
    let result = ModuleFilter.apply opts cat
    match result with
    | Ok _ -> Assert.Fail("expected failure when filter removes everything")
    | Error es ->
        Assert.Single(es) |> ignore
        Assert.Equal("moduleFilter.modules.empty", es.Head.Code)

// ---------------------------------------------------------------------------
// Per-module entity filters.
// ---------------------------------------------------------------------------

[<Fact>]
let ``ModuleFilter.apply per-module entity filter keeps only matching kinds`` () =
    let cat =
        mkCat
            [ mkActiveModule "Sales" [ mkKind "Order"; mkKind "Customer"; mkKind "Invoice" ] ]
    let opts =
        ModuleFilter.createOptions [] true true [ "Sales", seq { "Order"; "Customer" } ] |> mustOk
    let result = ModuleFilter.apply opts cat |> mustOk
    Assert.Single(result.Modules) |> ignore
    let kept = result.Modules.Head.Kinds |> List.map (fun k -> Name.value k.Name) |> Set.ofList
    Assert.Equal<Set<string>>(Set.ofList [ "Order"; "Customer" ], kept)

[<Fact>]
let ``ModuleFilter.apply entity filter is case-insensitive`` () =
    let cat =
        mkCat
            [ mkActiveModule "M1" [ mkKind "FooBar"; mkKind "Baz" ] ]
    let opts =
        ModuleFilter.createOptions [] true true [ "M1", seq { "foobar" } ] |> mustOk
    let result = ModuleFilter.apply opts cat |> mustOk
    Assert.Single(result.Modules) |> ignore
    Assert.Single(result.Modules.Head.Kinds) |> ignore
    Assert.Equal("FooBar", Name.value result.Modules.Head.Kinds.Head.Name)

[<Fact>]
let ``ModuleFilter.apply unknown entity name surfaces moduleFilter.entities.missing`` () =
    let cat = mkCat [ mkActiveModule "M1" [ mkKind "E1" ] ]
    let opts =
        ModuleFilter.createOptions [] true true [ "M1", seq { "E1"; "NotThere" } ] |> mustOk
    let result = ModuleFilter.apply opts cat
    match result with
    | Ok _ -> Assert.Fail("expected failure for unknown entity")
    | Error es ->
        Assert.Single(es) |> ignore
        Assert.Equal("moduleFilter.entities.missing", es.Head.Code)
        Assert.Contains("NotThere", es.Head.Message)

[<Fact>]
let ``ModuleFilter.apply modules outside entity-filter map pass through with all kinds`` () =
    let cat =
        mkCat
            [ mkActiveModule "Filtered" [ mkKind "K1"; mkKind "K2" ]
              mkActiveModule "Untouched" [ mkKind "X1"; mkKind "X2" ] ]
    let opts =
        ModuleFilter.createOptions [] true true [ "Filtered", seq { "K1" } ] |> mustOk
    let result = ModuleFilter.apply opts cat |> mustOk
    Assert.Equal(2, result.Modules.Length)
    let untouched = result.Modules |> List.find (fun m -> Name.value m.Name = "Untouched")
    Assert.Equal(2, untouched.Kinds.Length)

// ---------------------------------------------------------------------------
// `createOptions` — operator-input validation.
// ---------------------------------------------------------------------------

[<Fact>]
let ``ModuleFilter.createOptions rejects null module name`` () =
    let mods : string seq =
        seq { "Valid"; Unchecked.defaultof<string> }
    let result = ModuleFilter.createOptions mods true true []
    match result with
    | Ok _ -> Assert.Fail("expected validation failure")
    | Error es ->
        Assert.Contains(es, fun e -> e.Code = "moduleFilter.modules.null")

[<Fact>]
let ``ModuleFilter.createOptions rejects whitespace module name`` () =
    let result = ModuleFilter.createOptions [ "Valid"; "   " ] true true []
    match result with
    | Ok _ -> Assert.Fail("expected validation failure")
    | Error es ->
        Assert.Contains(es, fun e -> e.Code = "moduleFilter.modules.empty")

[<Fact>]
let ``ModuleFilter.createOptions dedupes case-insensitively`` () =
    let opts =
        ModuleFilter.createOptions [ "M1"; "m1"; "M2" ] true true [] |> mustOk
    Assert.Equal(2, Set.count opts.Modules)
    Assert.Equal(2, List.length opts.ModulesOriginal)

[<Fact>]
let ``ModuleFilter.createOptions trims surrounding whitespace`` () =
    let opts =
        ModuleFilter.createOptions [ "  M1  " ] true true [] |> mustOk
    Assert.Equal<Set<string>>(Set.ofList [ "m1" ], opts.Modules)
    Assert.Equal<string list>([ "M1" ], opts.ModulesOriginal)

[<Fact>]
let ``ModuleFilter.createOptions accumulates errors across module + entity-filter axes`` () =
    let entityErrors =
        [ "M1", seq { "  " }   // entity filter with whitespace entry
          "  ", seq { "E1" } ] // entity-filter key with whitespace
    let result =
        ModuleFilter.createOptions [ "" ] true true entityErrors
    match result with
    | Ok _ -> Assert.Fail("expected accumulated failures")
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> Set.ofList
        Assert.Contains("moduleFilter.modules.empty", codes)
        Assert.Contains("moduleFilter.entities.module.empty", codes)
        Assert.Contains("moduleFilter.entities.empty", codes)

// ---------------------------------------------------------------------------
// `ModuleEntityFilter.create` — entity-filter smart constructor.
// ---------------------------------------------------------------------------

[<Fact>]
let ``ModuleEntityFilter.create rejects empty sequence`` () =
    let result = ModuleEntityFilter.create []
    match result with
    | Ok _ -> Assert.Fail("expected failure on empty sequence")
    | Error es ->
        Assert.Contains(es, fun e -> e.Code = "moduleFilter.entities.empty")

[<Fact>]
let ``ModuleEntityFilter.create rejects null entity name`` () =
    let names : string seq =
        seq { "Valid"; Unchecked.defaultof<string> }
    let result = ModuleEntityFilter.create names
    match result with
    | Ok _ -> Assert.Fail("expected failure on null entry")
    | Error es ->
        Assert.Contains(es, fun e -> e.Code = "moduleFilter.entities.nullEntry")

[<Fact>]
let ``ModuleEntityFilter.create dedupes case-insensitively + preserves original case`` () =
    let f = ModuleEntityFilter.create [ "Foo"; "foo"; "Bar" ] |> mustOk
    Assert.Equal(2, Set.count f.NormalizedNames)
    Assert.Equal<string list>([ "Foo"; "Bar" ], f.OriginalNames)

[<Fact>]
let ``ModuleEntityFilter.matches is case-insensitive against Kind.Name`` () =
    let f = ModuleEntityFilter.create [ "FooBar" ] |> mustOk
    let k1 = mkKind "FooBar"
    let k2 = mkKind "Other"
    Assert.True(ModuleEntityFilter.matches k1 f)
    Assert.False(ModuleEntityFilter.matches k2 f)

[<Fact>]
let ``ModuleEntityFilter.missingNames returns names with no kind match`` () =
    let f = ModuleEntityFilter.create [ "E1"; "Missing1"; "Missing2" ] |> mustOk
    let kinds = [ mkKind "E1" ]
    let missing = ModuleEntityFilter.missingNames kinds f |> Set.ofList
    Assert.Equal<Set<string>>(Set.ofList [ "Missing1"; "Missing2" ], missing)

// ---------------------------------------------------------------------------
// Algebraic / structural properties.
// ---------------------------------------------------------------------------

[<Fact>]
let ``ModuleFilter.apply is idempotent — apply opts (apply opts c) == apply opts c`` () =
    let cat =
        mkCat
            [ mkActiveModule "M1" [ mkKind "E1"; mkInactiveKind "E2" ]
              mkActiveModule "M2" [ mkKind "E3" ]
              mkInactiveModule "M3" [ mkKind "E4" ] ]
    let opts = ModuleFilter.createOptions [ "M1"; "M2" ] true false [] |> mustOk
    let once = ModuleFilter.apply opts cat |> mustOk
    let twice = ModuleFilter.apply opts once |> mustOk
    Assert.Equal<Module list>(once.Modules, twice.Modules)

[<Fact>]
let ``ModuleFilter.apply result modules are a subset of input modules`` () =
    let cat =
        mkCat
            [ mkActiveModule "A" [ mkKind "K1" ]
              mkActiveModule "B" [ mkKind "K2" ]
              mkActiveModule "C" [ mkKind "K3" ] ]
    let opts = ModuleFilter.createOptions [ "A"; "C" ] true true [] |> mustOk
    let result = ModuleFilter.apply opts cat |> mustOk
    let inputNames = cat.Modules |> List.map (fun m -> Name.value m.Name) |> Set.ofList
    let resultNames = result.Modules |> List.map (fun m -> Name.value m.Name) |> Set.ofList
    Assert.True(Set.isSubset resultNames inputNames)
