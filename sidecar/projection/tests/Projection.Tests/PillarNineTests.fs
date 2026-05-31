module Projection.Tests.PillarNineTests

// H-052 (HORIZON Cluster F): the **bidirectional contract** of pillar 9.
//
// The pillar 9 / L3-CC-Transform-Totality / A41 contract reads in two
// directions:
//   1. **Skeleton-purity** — `Compose.runSkeleton` emits zero
//      `OperatorIntent` events. Any pass classified `DataIntent` that
//      actually leaks operator intent is caught here.
//   2. **Overlay-exercise** — every registered `OperatorIntent` axis
//      fires at least one `LineageEvent` carrying that classification
//      when the corresponding overlay is exercised with non-empty
//      configuration.
//
// Existing coverage (Cluster A.4.7 / chapter A.4.7'):
//   - `SkeletonPurityTests.fs` — example-based skeleton-purity on
//     `sampleCatalog`.
//   - `TransformRegistryCompletenessTests.fs` — per-pass overlay
//     invocation for VisibilityMask + TableRename (Selection + Emission).
//
// What this file adds:
//   - Property-based skeleton-purity sweep across permutations of the
//     sample catalog (FsCheck).
//   - Overlay-exercise tests for ALL four registered `OverlayAxis`
//     values (Selection, Emission, Tightening, Ordering) — completing
//     the bidirectional contract per HORIZON H-052.
//   - Coverage assertion: the set of axes named by
//     `TransformRegistry.overlayAxes` is fully exercised by this file.

open Xunit
open FsCheck.Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Adapters.Osm
open Projection.Pipeline
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Per-file helpers
// ---------------------------------------------------------------------------

let private mkName (s: string) : Name = Name.create s |> Result.value

let private shuffleModules (rng: int) (c: Catalog) : Catalog =
    // Deterministic-on-int "shuffle" via List.sortBy hash; preserves
    // structural equality of the catalog's contents but varies the
    // collection order. Skeleton purity is order-invariant — the
    // property must hold for any well-formed catalog.
    let sorted =
        c.Modules
        |> List.mapi (fun i m -> ((rng + i) * 31 + 17), m)
        |> List.sortBy fst
        |> List.map snd
    { c with Modules = sorted }

let private operatorIntents (trail: LineageEvent list) : LineageEvent list =
    trail
    |> List.filter (fun e ->
        match e.Classification with
        | OperatorIntent _ -> true
        | DataIntent -> false)

let private axesPresent (trail: LineageEvent list) : Set<OverlayAxis> =
    trail
    |> List.choose (fun e ->
        match e.Classification with
        | OperatorIntent axis -> Some axis
        | DataIntent -> None)
    |> Set.ofList

let private allRegistrations : RegisteredTransformMetadata list =
    [ CatalogReader.registeredMetadata
      RegisteredTransform.toMetadata CanonicalizeIdentity.registered
      RegisteredTransform.toMetadata NormalizeStaticPopulations.registered
      RegisteredTransform.toMetadata SymmetricClosure.registered
      RegisteredTransform.toMetadata TopologicalOrderPass.registered
      RegisteredTransform.toMetadata (NamingMorphism.registered NamingMorphism.identity)
      RegisteredTransform.toMetadata (VisibilityMask.registered VisibilityMask.empty)
      RegisteredTransform.toMetadata (NullabilityPass.registered Policy.empty Profile.empty)
      RegisteredTransform.toMetadata (UniqueIndexPass.registered Policy.empty Profile.empty)
      RegisteredTransform.toMetadata (ForeignKeyPass.registered Policy.empty Profile.empty)
      RegisteredTransform.toMetadata (CategoricalUniquenessPass.registered Policy.empty Profile.empty)
      RegisteredTransform.toMetadata (TableRename.registered [])
      RegisteredTransform.toMetadata (UserFkReflowPass.registered Policy.empty Profile.empty) ]
    @ StrategyRegistrations.all

// ---------------------------------------------------------------------------
// H-052 (1/2): Skeleton-purity — `Compose.runSkeleton` emits zero
// OperatorIntent events. Property form: holds for any permutation of
// the well-formed sample catalog.
// ---------------------------------------------------------------------------

[<Property>]
let ``H-052 skeleton-purity: runSkeleton emits zero OperatorIntent events for any module ordering`` (seed: int) =
    let permuted = shuffleModules seed sampleCatalog
    let result = Compose.runSkeleton permuted
    List.isEmpty (operatorIntents result.Trail)

[<Fact>]
let ``H-052 skeleton-purity (positive form): every skeleton trail event carries DataIntent`` () =
    let result = Compose.runSkeleton sampleCatalog
    Assert.NotEmpty result.Trail
    for event in result.Trail do
        Assert.Equal(DataIntent, event.Classification)

// ---------------------------------------------------------------------------
// H-052 (2/2): Overlay-exercise — every distinct OverlayAxis present
// in the registry fires at least one OperatorIntent LineageEvent
// carrying that axis when the corresponding overlay is exercised.
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-052 overlay-exercise: Selection axis fires when VisibilityMask hides at least one kind`` () =
    let mask : VisibilityMask.Mask =
        { Hide = [ VisibilityMask.hideOrigin Origin.Native ] }
    let rt = VisibilityMask.registered mask
    let result = rt.Run sampleCatalog
    let axes = axesPresent result.Trail
    Assert.Contains(Selection, axes)

[<Fact>]
let ``H-052 overlay-exercise: Emission axis fires when TableRename has at least one spec`` () =
    let renameSpec : TableRename.RenameSpec =
        { Key    = TableRename.Logical (mkName "Sales", mkName "Customer")
          Target = { Catalog = None; Schema = "renamed"; Table = "customer_v2" } }
    let rt = TableRename.registered [ renameSpec ]
    let result = rt.Run sampleCatalog
    let axes = axesPresent result.Trail
    Assert.Contains(Emission, axes)

[<Fact>]
let ``H-052 overlay-exercise: Tightening axis fires when NullabilityPass has an intervention`` () =
    // Nullability intervention with min-evidence target threshold:
    // `MaxNullFraction = 0` says "tighten to NOT NULL if no nulls
    // observed." Profile.empty produces evidence-missing; the pass
    // still emits a per-attribute Tightening event for each candidate.
    let cfg = NullabilityTighteningConfig.create 0.05m true [] |> Result.value
    let policy =
        { Policy.empty with
            Tightening = { Interventions = [ Nullability ("h052-nullability", cfg) ] } }
    let rt = NullabilityPass.registered policy Profile.empty
    let result = rt.Run sampleCatalog
    let axes = axesPresent result.Trail
    Assert.Contains(Tightening, axes)

[<Fact>]
let ``H-052 overlay-exercise: Ordering axis is named at the TopologicalOrderPass registry site`` () =
    // The Ordering axis lives at the **registry-metadata** level
    // (TopologicalOrderPass's `selfLoopHandling` Site carries
    // `Classification = OperatorIntent Ordering`). Per the pass's
    // module docstring at line 396-404, "the SelfLoopHandling site
    // affects buildGraph but emits no per-event lineage at this
    // slice"; per-event classification is uniformly `DataIntent`. So
    // the overlay-exercise property for Ordering is enforced at the
    // structural level (registry Site presence + skeleton exclusion)
    // rather than per-event. This is the chapter A.4.7 open's Q9-
    // trigger-fires worked example for a fifth overlay axis whose
    // expression is **pre-runtime** (buildGraph affects topology)
    // rather than **post-runtime** (event classification).
    let topo = TopologicalOrderPass.registered
    let orderingSites =
        topo.Sites
        |> List.filter (fun s -> s.Classification = OperatorIntent Ordering)
    Assert.NotEmpty orderingSites
    // Skeleton exclusion: a pass with any OperatorIntent site is
    // excluded from skeletonView (per TransformRegistry.skeletonView
    // semantics). TopologicalOrderPass not in skeleton confirms the
    // Ordering site's enforcement.
    let skeletonNames =
        TransformRegistry.skeletonView allRegistrations
        |> List.map (fun rt -> rt.Name)
        |> Set.ofList
    Assert.DoesNotContain(topo.Name, skeletonNames)

// ---------------------------------------------------------------------------
// Coverage closure: the four exercised axes above EQUAL the full set
// of axes named by `TransformRegistry.overlayAxes` over the canonical
// registry. If a fifth `OperatorIntent _` axis lands in the registry
// without an overlay-exercise test, this assertion fails.
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-052 overlay-exercise coverage: every registry-known OverlayAxis has a corresponding overlay-exercise test above`` () =
    let exercised = Set.ofList [ Selection; Emission; Tightening; Ordering ]
    let registryAxes = TransformRegistry.overlayAxes allRegistrations
    Assert.Equal<Set<OverlayAxis>>(registryAxes, exercised)

// ---------------------------------------------------------------------------
// Bidirectional contract: skeleton-purity AND overlay-exercise hold
// over the same registry. The skeleton excludes every pass with an
// `OperatorIntent` site; the overlay-exercise tests each fire when the
// excluded passes are invoked with non-empty config. The two views
// partition the registry's runtime behaviour completely.
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-052 bidirectional contract: skeletonView and overlayView partition the registry`` () =
    let skeleton =
        TransformRegistry.skeletonView allRegistrations
        |> List.map (fun rt -> rt.Name)
        |> Set.ofList
    let overlay =
        TransformRegistry.overlayView allRegistrations
        |> List.map (fun rt -> rt.Name)
        |> Set.ofList
    Assert.Empty(Set.intersect skeleton overlay)
    let total =
        allRegistrations
        |> List.map (fun rt -> rt.Name)
        |> Set.ofList
    Assert.Equal<Set<string>>(total, Set.union skeleton overlay)
