module Projection.Tests.RegisteredDataTransformsTests

// Chapter 5.13 slice data-emission-registry — pillar 9
// harvest-discipline classification of the data-axis sibling-Π
// emitters + composer.
//
// The slice extends `Projection.Core/RegisteredTransforms.all`
// (12 passes + 5 strategies + 1 adapter) with the four data-emission
// surfaces (3 emitters + 1 composer). The registry's totality property
// (every transformation site classifies as DataIntent or
// OperatorIntent per pillar 9) extends to cover the data axis.
//
// The tests in this file:
//   1. Pin the registry's cardinality (4 entries: composer + 3 emitters)
//   2. Assert each entry validates through `TransformRegistry.create`
//      (uniqueness + non-empty rationales + substantive
//      NotImplementedInV2 if applicable)
//   3. Assert the structural-data-emission baseline (composer +
//      StaticSeeds + Migration's `deferredFkPhase2` site classify as
//      DataIntent — reachable from `composeRendered Policy.empty
//      catalog Profile.empty` with empty operator contexts)
//   4. Assert the operator-intent overlay axes (DataComposition →
//      Emission; UserRemapContext + MigrationDependencyContext →
//      Insertion) are correctly classified

open Xunit
open Projection.Core
open Projection.Targets.Data

[<Fact>]
let ``5.13.data-emission-registry: RegisteredDataTransforms.all carries exactly the four data-axis entries`` () =
    let names =
        RegisteredDataTransforms.all
        |> List.map (fun rt -> rt.Name)
        |> List.sort
    Assert.Equal<string list>(
        [ "bootstrapEmitter"; "dataEmissionComposer";
          "migrationDependenciesEmitter"; "staticSeedsEmitter" ],
        names)

[<Fact>]
let ``5.13.data-emission-registry: every data-axis entry validates through TransformRegistry.create`` () =
    // Per pillar 9: every Site.Rationale must be non-empty; every
    // Status = NotImplementedInV2 must carry substantive rationale;
    // Names must be unique. `TransformRegistry.create` aggregates
    // every violation; an empty Result means the discipline holds.
    let result = TransformRegistry.create RegisteredDataTransforms.all
    Assert.True(
        Result.isSuccess result,
        sprintf "TransformRegistry.create rejected RegisteredDataTransforms.all: %A" (Result.errors result))

[<Fact>]
let ``5.13.data-emission-registry: every data-axis entry binds StageBinding to Emitter or Pipeline`` () =
    // Data emission lives at the Emitter + Pipeline stage seams; no
    // entry should claim Adapter / Pass / OrderingPolicy.
    let stages = RegisteredDataTransforms.all |> List.map (fun rt -> rt.StageBinding)
    for stage in stages do
        match stage with
        | Emitter | Pipeline -> ()
        | other ->
            Assert.Fail(
                sprintf "data-axis entry has unexpected StageBinding %A; expected Emitter or Pipeline" other)

[<Fact>]
let ``5.13.data-emission-registry: every data-axis entry binds Domain to Data`` () =
    let domains = RegisteredDataTransforms.all |> List.map (fun rt -> rt.Domain)
    for domain in domains do
        Assert.Equal<Domain>(Data, domain)

[<Fact>]
let ``5.13.data-emission-registry: composer claims DataComposition as OperatorIntent Emission`` () =
    // Pillar 9 — the composer reads `Policy.Emission.DataComposition`
    // and dispatches; the policy reads is the canonical operator-intent
    // entry point at the data axis. OverlayAxis = Emission (what
    // physical form the data takes — which siblings fire).
    let composer = DataEmissionComposer.registeredMetadata
    let dispatch =
        composer.Sites
        |> List.find (fun s -> s.SiteName = "compositionDispatch")
    Assert.Equal<Classification>(OperatorIntent Emission, dispatch.Classification)

[<Fact>]
let ``5.13.data-emission-registry: composer's globalPhaseOrdering site classifies DataIntent`` () =
    // Slice ι cash-out — global Phase-1-then-Phase-2 ordering across
    // emitters is structural deploy-correctness (not operator-supplied).
    // This claim is load-bearing for matrix row 160's reclassification
    // from "NOT YET REIFIED" to "🟢 PARITY".
    let composer = DataEmissionComposer.registeredMetadata
    let ordering =
        composer.Sites
        |> List.find (fun s -> s.SiteName = "globalPhaseOrdering")
    Assert.Equal<Classification>(DataIntent, ordering.Classification)

[<Fact>]
let ``5.13.data-emission-registry: composer's partitionAssertion site classifies DataIntent`` () =
    // Slice θ cash-out — overlap detection is structural fidelity,
    // not configurable; fires on first overlap in deterministic
    // catalog order.
    let composer = DataEmissionComposer.registeredMetadata
    let partition =
        composer.Sites
        |> List.find (fun s -> s.SiteName = "partitionAssertion")
    Assert.Equal<Classification>(DataIntent, partition.Classification)

[<Fact>]
let ``5.13.data-emission-registry: StaticSeedsEmitter is pure DataIntent (substitution moved to DataLoadPlan)`` () =
    // Post-convergence: the operator-supplied SurrogateRemapContext is
    // applied at `DataLoadPlan.build` (the canonical OperatorIntent
    // Insertion altitude); the static-seeds emitter consumes the plan's
    // post-substitution rows and classifies entirely DataIntent.
    let emitter = StaticSeedsEmitter.registeredMetadata
    let bySite = emitter.Sites |> List.map (fun s -> s.SiteName, s.Classification) |> Map.ofList
    Assert.Equal<Classification>(DataIntent, bySite.["staticRowsProjection"])
    Assert.Equal<Classification>(DataIntent, bySite.["cdcAwareChangeDetection"])
    Assert.Equal<Classification>(DataIntent, bySite.["deferredFkPhase2"])
    Assert.False(Map.containsKey "staticRowSurrogateRemap" bySite,
                 "staticRowSurrogateRemap retired at the data-load convergence; substitution lives at DataLoadPlan.identitySubstitution")

[<Fact>]
let ``5.13.data-emission-registry: MigrationDependenciesEmitter keeps row-emission OperatorIntent but retires the remap site`` () =
    // Post-convergence: the `userRemapRewrite` site retires (substitution
    // landed at `DataLoadPlan.identitySubstitution`). `migrationRowEmission`
    // stays OperatorIntent Insertion because it classifies the *inclusion
    // of operator-supplied rows*, not their value-rewriting — distinct
    // operator-intent surfaces, distinct sites.
    let emitter = MigrationDependenciesEmitter.registeredMetadata
    let bySite = emitter.Sites |> List.map (fun s -> s.SiteName, s.Classification) |> Map.ofList
    Assert.Equal<Classification>(OperatorIntent Insertion, bySite.["migrationRowEmission"])
    Assert.Equal<Classification>(DataIntent, bySite.["deferredFkPhase2"])
    Assert.False(Map.containsKey "userRemapRewrite" bySite,
                 "userRemapRewrite retired at the data-load convergence; substitution lives at DataLoadPlan.identitySubstitution")

[<Fact>]
let ``5.13.data-emission-registry: BootstrapEmitter is NotImplementedInV2 at slice ζ MVP`` () =
    // Slice ζ MVP — Bootstrap is the empty-no-op stub today; the
    // registry entry transitions to Active when chapter 4.2 slice η
    // populates the per-kind row source. The rationale must be
    // substantive (pillar 9 harvest-discipline; TransformRegistry
    // rejects empty rationale).
    let emitter = BootstrapEmitter.registeredMetadata
    match emitter.Status with
    | NotImplementedInV2 rationale ->
        Assert.False(
            System.String.IsNullOrWhiteSpace rationale,
            "BootstrapEmitter NotImplementedInV2 rationale must be substantive per pillar 9")
        Assert.Contains("Chapter 4.2 slice η", rationale)
    | Active ->
        Assert.Fail("BootstrapEmitter expected to be NotImplementedInV2 at slice ζ MVP")

[<Fact>]
let ``5.13.data-emission-registry: skeletonView excludes every entry carrying an OperatorIntent site`` () =
    // The pillar 9 bidirectional property: an entry whose Sites
    // contain ANY OperatorIntent classification excludes from the
    // skeleton view. Post-convergence: the composer
    // (compositionDispatch is OperatorIntent Emission),
    // MigrationDependenciesEmitter (migrationRowEmission stays
    // OperatorIntent Insertion), and Bootstrap (userRemapBootstrap
    // OperatorIntent Insertion deferred) drop out.
    // StaticSeedsEmitter is BACK in skeleton — the
    // `staticRowSurrogateRemap` site retired at the data-load
    // convergence, so all its sites are DataIntent.
    let skeleton = TransformRegistry.skeletonView RegisteredDataTransforms.all
    let skeletonNames = skeleton |> List.map (fun rt -> rt.Name)
    Assert.DoesNotContain("dataEmissionComposer", skeletonNames)
    Assert.DoesNotContain("migrationDependenciesEmitter", skeletonNames)
    Assert.DoesNotContain("bootstrapEmitter", skeletonNames)
    Assert.Contains("staticSeedsEmitter", skeletonNames)

[<Fact>]
let ``5.13.data-emission-registry: overlayView includes every entry carrying an OperatorIntent site`` () =
    let overlay = TransformRegistry.overlayView RegisteredDataTransforms.all
    let overlayNames = overlay |> List.map (fun rt -> rt.Name)
    Assert.Contains("dataEmissionComposer", overlayNames)
    Assert.Contains("migrationDependenciesEmitter", overlayNames)
    Assert.Contains("bootstrapEmitter", overlayNames)
    // StaticSeedsEmitter retired its `staticRowSurrogateRemap` site at
    // the data-load convergence (substitution now at
    // `DataLoadPlan.identitySubstitution`) — drops out of overlay.
    Assert.DoesNotContain("staticSeedsEmitter", overlayNames)

[<Fact>]
let ``5.13.data-emission-registry: overlayAxes returns Emission + Insertion`` () =
    // The composer claims Emission (DataComposition dispatch) +
    // Insertion (context threading); Migration + Bootstrap claim
    // Insertion. No Selection / Tightening / Ordering axes — data
    // emission doesn't filter (Selection lives at VisibilityMask)
    // or strengthen (Tightening lives at the policy passes) or
    // order topology (Ordering lives at TopologicalOrderPass).
    let axes = TransformRegistry.overlayAxes RegisteredDataTransforms.all
    Assert.True(Set.contains Emission axes,   sprintf "expected Emission in %A"   axes)
    Assert.True(Set.contains Insertion axes,  sprintf "expected Insertion in %A"  axes)
    Assert.False(Set.contains Selection axes, sprintf "expected NOT Selection in %A"   axes)
    Assert.False(Set.contains Tightening axes,sprintf "expected NOT Tightening in %A"  axes)
    Assert.False(Set.contains Ordering axes,  sprintf "expected NOT Ordering in %A"    axes)

[<Fact>]
let ``5.13.data-emission-registry: data-emission entries integrate with Core's registry without name collision`` () =
    // The full V2 registry composes Core's `all` (passes + strategies)
    // with adapter metadata (CatalogReader) + data-emission metadata
    // (this slice). Validate the union via TransformRegistry.create
    // — name uniqueness across the combined surface is the
    // load-bearing invariant (consumers index by Name).
    let combined =
        RegisteredTransforms.all
        @ RegisteredDataTransforms.all
    let result = TransformRegistry.create combined
    Assert.True(
        Result.isSuccess result,
        sprintf "Core + Data registry composition failed: %A" (Result.errors result))
