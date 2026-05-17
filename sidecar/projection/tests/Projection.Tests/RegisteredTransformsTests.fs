module Projection.Tests.RegisteredTransformsTests

open Xunit
open Projection.Core

// ---------------------------------------------------------------------------
// Chapter A.4.7' slice β — RegisteredTransforms.all + allChainSteps
// witnesses.
//
// Slice β ships:
//   - `RegisteredTransforms.all : RegisteredTransformMetadata list`
//     populated with the 17 Core-resident entries (12 pass + 5
//     strategy).
//   - `RegisteredTransforms.allChainSteps : PassChainAdapter list`
//     populated with the 12 pass entries lifted through the slice-α
//     `PassChainAdapter` adapter (6 Catalog-chainable +
//     6 decision-set).
//
// The OSSYS adapter's `CatalogReader.registeredMetadata` is project-
// layered out of `Projection.Core`; the 18-entry production registry
// is assembled at the Pipeline / consumer level by prepending the
// adapter to `RegisteredTransforms.all`. The chapter-A.4.7 slice θ
// completeness tests (`TransformRegistryCompletenessTests`) continue
// to operate on the test-side 18-entry aggregation; slice γ's
// `runChain` consumer will adopt `RegisteredTransforms.all` directly.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A.4.7' slice β: RegisteredTransforms.all carries 17 Core-resident entries (12 pass + 5 strategy)`` () =
    Assert.Equal(17, List.length RegisteredTransforms.all)

[<Fact>]
let ``A.4.7' slice β: RegisteredTransforms.allChainSteps carries 12 PassChainAdapter entries`` () =
    Assert.Equal(12, List.length RegisteredTransforms.allChainSteps)

[<Fact>]
let ``A.4.7' slice β: every PassChainAdapter Name matches a Pass-stage entry in RegisteredTransforms.all`` () =
    let metadataNames =
        RegisteredTransforms.all
        |> List.filter (fun rt -> rt.StageBinding = Pass)
        |> List.map (fun rt -> rt.Name)
        |> Set.ofList
    let chainStepNames =
        RegisteredTransforms.allChainSteps
        |> List.map (fun step -> step.Name)
        |> Set.ofList
    Assert.True(
        Set.isSubset chainStepNames metadataNames,
        sprintf
            "chainSteps Names %A must be a subset of Pass-stage metadata Names %A"
            chainStepNames
            metadataNames)

[<Fact>]
let ``A41: RegisteredTransforms.all validates through TransformRegistry.create (uniqueness + rationale + status invariants)`` () =
    match TransformRegistry.create RegisteredTransforms.all with
    | Ok entries -> Assert.Equal(17, List.length entries)
    | Error es -> failwithf "expected RegisteredTransforms.all to validate; got %A" es

[<Fact>]
let ``A.4.7' slice β: PassChainAdapter Names are unique within allChainSteps`` () =
    let names = RegisteredTransforms.allChainSteps |> List.map (fun step -> step.Name)
    let unique = names |> Set.ofList
    Assert.Equal(List.length names, Set.count unique)
