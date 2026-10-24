module Projection.Tests.EstateScopeGovernanceTests

// The cross-environment comparison faces (`check environments` / `check estate`
// / `check shape`) must read the OSSYS estate SCOPED to `model.modules` — not
// the whole estate. Reading the whole estate drags in clone / deleted / test /
// system eSpaces; a cloned module carries the SAME entity SS_Keys as its
// original, so the whole-estate read fails to construct (`catalog.kinds.
// duplicateKey`). Scoping excludes the clones and the (stable-across-
// environments) SS_Key matching is then correct.
//
// Two guards here:
//   1. GOVERNANCE (source-scan) — no face calls the unscoped `Source.ofOssys`
//      directly; the sanctioned path is `ScopedRead` (or `Source.ofOssysWith`
//      with a bound scope). This is the "restrict against this happening again"
//      rail so a future face cannot silently re-introduce the whole-estate read.
//   2. SCOPE (pure) — `ScopedRead.applyScope` narrows a catalog to the declared
//      `model.modules`, and an empty selection is the byte-identical identity.

open System.IO
open System.Text.RegularExpressions
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Tests.Fixtures
open Projection.Tests.IRBuilders

// -- shared root walk (the AxiomTests idiom) ---------------------------------

let private projectionRoot : string =
    let sentinel = Path.Combine("tests", "Projection.Tests", "EstateScopeGovernanceTests.fs")
    let rec findUp (dir: DirectoryInfo option) : string option =
        match dir with
        | None -> None
        | Some d ->
            if File.Exists(Path.Combine(d.FullName, sentinel)) then Some d.FullName
            else findUp (Option.ofObj d.Parent)
    let start =
        System.Reflection.Assembly.GetExecutingAssembly().Location
        |> Path.GetDirectoryName
        |> Option.ofObj
        |> Option.map (fun d -> DirectoryInfo d)
    match findUp start with
    | Some root -> root
    | None ->
        failwith "projection root not found above the test assembly — expected tests/Projection.Tests/EstateScopeGovernanceTests.fs"

// -- 1. governance: no unscoped OSSYS read in a comparison face --------------

[<Fact>]
let ``governance: no face reads the whole OSSYS estate (Source.ofOssys) — the scoped seam is the only path`` () =
    let facesDir = Path.Combine(projectionRoot, "src", "Projection.Cli", "Faces")
    Assert.True(Directory.Exists facesDir, sprintf "expected the faces directory at %s" facesDir)
    // The unscoped constructor `Source.ofOssys` (NOT the scope-bearing
    // `Source.ofOssysWith`) reads every eSpace in the estate. Comment lines are
    // stripped first so a doc mention of the name is never a false positive; a
    // genuine call is never on a `//`-prefixed line.
    let forbidden = Regex(@"Source\.ofOssys(?!With)")
    let offenders =
        Directory.EnumerateFiles(facesDir, "*.fs", SearchOption.AllDirectories)
        |> Seq.choose (fun file ->
            let source =
                File.ReadAllText file
                |> fun raw -> raw.Split('\n')
                |> Array.map (fun line -> if line.TrimStart().StartsWith "//" then "" else line)
                |> String.concat "\n"
            if forbidden.IsMatch source then Some (Path.GetFileName file |> Option.ofObj |> Option.defaultValue file) else None)
        |> Seq.toList
    Assert.True(
        List.isEmpty offenders,
        sprintf
            "these comparison faces read the WHOLE OSSYS estate via Source.ofOssys instead of scoping to model.modules through ScopedRead: %s"
            (String.concat ", " offenders))

// -- 2. scope: applyScope narrows to model.modules; empty is identity --------

let private twoModuleCatalog : Catalog =
    // `customer` and `country` carry no cross-references, so two single-kind
    // modules form a referentially-clean catalog.
    mkCatalog
        [ mkModule (modKey "InScope")  (mkName "InScope")  [ customer ]
          mkModule (modKey "OutScope") (mkName "OutScope") [ country ] ]

let private modelScopedTo (modules: Config.ModuleSelector list) : Config.ModelSection =
    { Path                   = None
      Ossys                  = None
      Modules                = modules
      IncludeSystemModules   = true
      IncludeInactiveModules = true
      OnlyActiveAttributes   = true }

[<Fact>]
let ``scope: applyScope keeps only the declared model.modules (out-of-scope clone/system eSpace dropped)`` () =
    let model = modelScopedTo [ Config.ModuleSelector.Whole "InScope" ]
    let scoped = ScopedRead.applyScope model twoModuleCatalog |> Result.value
    let moduleNames = scoped.Modules |> List.map (fun m -> Name.value m.Name)
    Assert.Equal<string list>([ "InScope" ], moduleNames)
    let kindNames =
        Catalog.allKinds scoped |> List.map (fun k -> Name.value k.Name) |> List.sort
    Assert.Equal<string list>([ "Customer" ], kindNames)

[<Fact>]
let ``scope: an empty model.modules is the show-everything identity (byte-identical default)`` () =
    let model = modelScopedTo []
    let scoped = ScopedRead.applyScope model twoModuleCatalog |> Result.value
    Assert.Equal(2, List.length scoped.Modules)
    Assert.Equal<string list>(
        twoModuleCatalog.Modules |> List.map (fun m -> Name.value m.Name),
        scoped.Modules |> List.map (fun m -> Name.value m.Name))

[<Fact>]
let ``scope: a model.modules naming a module absent from the estate refuses by name (fail-loud, not silent-empty)`` () =
    let model = modelScopedTo [ Config.ModuleSelector.Whole "NoSuchModule" ]
    match ScopedRead.applyScope model twoModuleCatalog with
    | Ok _ -> Assert.Fail "expected a moduleFilter.modules.missing refusal for an unknown module"
    | Error errs ->
        Assert.Contains(errs, fun (e: ValidationError) -> e.Code = "moduleFilter.modules.missing")
