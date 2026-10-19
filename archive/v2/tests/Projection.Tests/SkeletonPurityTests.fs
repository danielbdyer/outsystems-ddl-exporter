module Projection.Tests.SkeletonPurityTests

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Chapter A.4.7' slice ε — Skeleton-purity true-execution property test.
//
// Promotes the skeleton-purity property from filter-shape-only (chapter
// A.4.7 slice θ — asserts every site in `skeletonView all` carries
// `Classification = DataIntent`) to true-execution (this slice — runs
// `Compose.runSkeleton` and asserts the *emitted* LineageEvent trail
// carries zero `OperatorIntent _` events).
//
// L3-CC-Transform-Totality: A41's bidirectional contract holds at
// runtime, not just at metadata. A pass that classifies its Sites as
// DataIntent but emits a LineageEvent with `Classification =
// OperatorIntent _` leaks operator intent into the skeleton; the
// property test catches the divergence.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A.4.7' slice ε: skeletonChainSteps contains the ten pure-DataIntent passes`` () =
    // `namingMorphism` lands in the skeleton because its Sites
    // classify as DataIntent — the act of carrying a logical→
    // physical name correspondence is data-intention; an operator-
    // supplied non-identity Morphism is captured in the closed-over
    // config at factory time, not in the Sites classification.
    // Symmetric with `canonicalizeIdentity` (identity rewrite as
    // structural canonicalization) and `symmetricClosure` /
    // `normalizeStaticPopulations` (algorithm-internal closures).
    // Cluster D adds five graph-analytics passes (H-071 through
    // H-076): `centrality`, `boundedContext`, `profileAnomaly`,
    // `schemaComplexity`, and `queryHint`. NM-36 adds a sixth,
    // `cascadeShockZones`. All are purely evidence-driven with no
    // operator opinion (DataIntent Sites).
    let names =
        RegisteredTransforms.skeletonChainSteps
        |> List.map (fun adapter -> adapter.Name)
        |> Set.ofList
    let expected =
        Set.ofList
            [ "canonicalizeIdentity"
              "namingMorphism"
              "normalizeStaticPopulations"
              "symmetricClosure"
              "centrality"
              "boundedContext"
              "profileAnomaly"
              "schemaComplexity"
              "queryHint"
              "cascadeShockZones" ]
    Assert.Equal<Set<string>>(expected, names)

[<Fact>]
let ``L3-CC-Transform-Totality (true-execution): Compose.runSkeleton emits zero OperatorIntent LineageEvents`` () =
    let result = Compose.runSkeleton sampleCatalog
    let operatorIntents =
        result.Trail
        |> List.filter (fun event ->
            match event.Classification with
            | OperatorIntent _ -> true
            | DataIntent -> false)
    Assert.True(
        List.isEmpty operatorIntents,
        sprintf
            "Compose.runSkeleton must emit zero OperatorIntent events; observed %d: %A"
            (List.length operatorIntents)
            operatorIntents)

[<Fact>]
let ``A.4.7' slice ε: Compose.runSkeleton trail entries all carry DataIntent classification (positive form)`` () =
    let result = Compose.runSkeleton sampleCatalog
    let allDataIntent =
        result.Trail
        |> List.forall (fun event ->
            match event.Classification with
            | DataIntent -> true
            | OperatorIntent _ -> false)
    Assert.True(allDataIntent, "every LineageEvent in skeleton run must carry DataIntent")
