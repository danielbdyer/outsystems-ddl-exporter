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
let ``A.4.7' slice ε (align-I.6): skeletonChainSteps contains the seven precondition-satisfiable pure-DataIntent passes`` () =
    // `namingMorphism` lands in the skeleton because its Sites
    // classify as DataIntent — the act of carrying a logical→
    // physical name correspondence is data-intention; an operator-
    // supplied non-identity Morphism is captured in the closed-over
    // config at factory time, not in the Sites classification.
    // Symmetric with `canonicalizeIdentity` (identity rewrite as
    // structural canonicalization) and `symmetricClosure` /
    // `normalizeStaticPopulations` (algorithm-internal closures).
    // `profileAnomaly` + `queryHint` are evidence-driven analytics
    // whose ProfileEvidence requirement the skeleton SUPPLIES (the
    // profile is DataIntent's own free variable — `runSkeletonWith`).
    // `physicalClaims` (S13) annotates witnessed adjudications; chain
    // default is identity, so the baseline is byte-unchanged.
    //
    // align-I.6 (a3-F2): the four topology dependents — `centrality`,
    // `boundedContext`, `schemaComplexity`, `cascadeShockZones` — are
    // OUT, excluded by the product-precondition cascade WITH their
    // producer (`topologicalOrder`, excluded for its OperatorIntent
    // Ordering site). They previously stayed and silently computed
    // over `TopologicalOrder.empty` — zero-edge analytics presented
    // as analysis. Eleven → seven, each exclusion named below.
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
              "profileAnomaly"
              "queryHint"
              "physicalClaims" ]
    Assert.Equal<Set<string>>(expected, names)

[<Fact>]
let ``align-I.6 (A52): the skeleton assembly names exactly the four topology-dependent exclusions`` () =
    // The exclusion cascade is the honest voice of the narrowed
    // assembly: each dropped step is named with the product whose
    // producer the skeleton lacks. Absent-with-a-name, never
    // present-and-degenerate.
    let exclusions = snd RegisteredTransforms.skeletonAssembly
    let byName =
        exclusions
        |> List.map (fun e -> e.StepName, e.Missing)
        |> Set.ofList
    let expected =
        Set.ofList
            [ "centrality",        ChainProduct.Topology
              "boundedContext",    ChainProduct.Topology
              "schemaComplexity",  ChainProduct.Topology
              "cascadeShockZones", ChainProduct.Topology ]
    Assert.Equal<Set<string * ChainProduct>>(expected, byName)

[<Fact>]
let ``align-I.6 (A52): runSkeleton voices each exclusion as a skeleton.stepExcluded diagnostic`` () =
    let result = Compose.runSkeleton sampleCatalog
    let voiced =
        result.Value.Entries
        |> List.filter (fun e -> e.Code = "skeleton.stepExcluded")
        |> List.choose (fun e -> Map.tryFind "step" e.Metadata)
        |> Set.ofList
    Assert.Equal<Set<string>>(
        Set.ofList [ "centrality"; "boundedContext"; "schemaComplexity"; "cascadeShockZones" ],
        voiced)

[<Fact>]
let ``align-I.6 (A52): the full chain asserts satisfiable — zero exclusions with ProfileEvidence supplied`` () =
    // The canonical chain's assembly is assert-only: a mis-wired chain
    // (a Requires no earlier Produces nor the supplied set satisfies)
    // fails loud at assembly. Building the full chain proves zero
    // exclusions; the topology dependents' requirement is met by the
    // in-chain producer.
    let adapters = RegisteredTransforms.allChainStepsFor Policy.empty Profile.empty
    Assert.Equal(List.length RegisteredTransforms.chainSteps, List.length adapters)

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
let ``align-I.6: runSkeletonWith a non-empty profile still emits zero OperatorIntent events (the profile is DataIntent's free variable)`` () =
    // DataIntent's definition — "reachable from Project(catalog,
    // Policy.empty, profile)" — is parameterized over profile; the
    // skeleton with evidence is still operator-free. Purity must hold
    // at every profile point, not only the empty one.
    let profile =
        { Profile.empty with
            SourceUsers =
                UserPopulation.create
                    [ UserAttributes.create
                        (SourceUserId.ofInt 1)
                        (SsKey.synthesizedComposite "OS_TEST" [ "skeleton"; "u1" ] |> Result.value)
                        None ] }
    let result = Compose.runSkeletonWith profile sampleCatalog
    let operatorIntents =
        result.Trail
        |> List.filter (fun event ->
            match event.Classification with
            | OperatorIntent _ -> true
            | DataIntent -> false)
    Assert.True(
        List.isEmpty operatorIntents,
        sprintf "runSkeletonWith must emit zero OperatorIntent events for ANY profile; observed %d" (List.length operatorIntents))

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
