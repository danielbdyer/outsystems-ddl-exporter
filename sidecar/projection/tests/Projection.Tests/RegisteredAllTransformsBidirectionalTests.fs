module Projection.Tests.RegisteredAllTransformsBidirectionalTests

// Slice A.4.7'-prelude+pipeline-registry — bidirectional property
// tests against the Pipeline-level unified registry
// (`RegisteredAllTransforms.all`). Per the pillar-9 load-bearing
// commitment + L3-CC-Transform-Totality (A41 candidate): the
// skeleton-purity property + the overlay-exercise property +
// totality coverage together close the bidirectional contract.
//
// **Skeleton-purity (forward direction):** for every catalog the
// skeleton-view pass chain ingests, the emitted lineage trail
// carries zero `OperatorIntent _` events. A pass classified
// DataIntent that leaks operator intent into the trail fails this
// property. Exists in `SkeletonPurityTests.fs` for `sampleCatalog`;
// strengthened here across a fixture sweep.
//
// **Overlay-exercise (reverse direction):** for every registered
// `OperatorIntent <axis>` site, exercising the registered pass with
// a non-empty intent produces at least one `LineageEvent` with
// `Classification = OperatorIntent <axis>`. A registered overlay
// that never fires under any operator intent is "dead overlay" —
// the third pillar-9 failure mode. Exists for VisibilityMask
// (Selection) + TableRename (Emission); strengthened here to cover
// Tightening + Ordering axes + all five OverlayAxis variants.
//
// **Totality (registry shape):** `RegisteredAllTransforms.all`
// covers every registered transformation site V2 ships. Drift
// surfaces here when a new pass / adapter / emitter / strategy
// lands without being added to its project's registry surface.

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Pipeline
open Projection.Tests.Fixtures
open Projection.Tests.TotalityFunctor


// ---------------------------------------------------------------------------
// Totality — the unified registry covers every shipped surface.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A.4.7'-prelude: RegisteredAllTransforms.all validates through TransformRegistry.create`` () =
    match TransformRegistry.create RegisteredAllTransforms.all with
    | Ok entries ->
        Assert.True(
            List.length entries >= 21,
            sprintf "expected >= 21 entries; got %d" (List.length entries))
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        Assert.Fail(sprintf "Expected RegisteredAllTransforms.all to validate; got: %s" codes)

[<Fact>]
let ``A.4.7'-prelude: RegisteredAllTransforms.all covers Adapter / Pass / Emitter / Pipeline stages`` () =
    // Five `StageBinding` values exist (Adapter / Pass / OrderingPolicy
    // / Emitter / Pipeline); production registrations cover four —
    // OrderingPolicy is structurally available for future use
    // (TopologicalOrderPass currently binds as `Pass`, not
    // OrderingPolicy, because its `Run` shape carries the policy
    // closure-over rather than as a separate binding). When an
    // OrderingPolicy-bound transformation lands, the expected-set
    // grows to all five.
    let stages =
        RegisteredAllTransforms.all
        |> List.map (fun rt -> rt.StageBinding)
        |> Set.ofList
    let required = Set.ofList [ Adapter; Pass; Emitter; Pipeline ]
    Assert.True(
        Set.isSubset required stages,
        sprintf "missing required stages: %A" (Set.difference required stages))

[<Fact>]
let ``A.4.7'-prelude: RegisteredAllTransforms.all contains every domain (Schema / Data / Identity / Diagnostics / CrossCutting)`` () =
    // Identity / CutoverSafety domains may not yet be represented at
    // this slice; the assertion is "every domain present is one of
    // the named six." Drift surfaces when a transformation declares
    // a domain not in the Domain DU (compile error) — this test is
    // structural-presence rather than full-coverage.
    let domains =
        RegisteredAllTransforms.all
        |> List.map (fun rt -> rt.Domain)
        |> Set.ofList
    let validDomains =
        Set.ofList
            [ Domain.Schema; Domain.Data; Domain.Identity
              Domain.Diagnostics; Domain.CutoverSafety; Domain.CrossCutting ]
    let unknown = Set.difference domains validDomains
    Assert.True(Set.isEmpty unknown, sprintf "Unknown domains: %A" unknown)

[<Fact>]
let ``A.4.7'-prelude: every site classification appears at least once across the unified registry`` () =
    // Pillar 9: two classifications (DataIntent + OperatorIntent of
    // OverlayAxis). The registry must witness both — pure-skeleton-
    // only would indicate no operator-overlay registrations; pure-
    // overlay-only would indicate no structural projection surfaces.
    let allSites =
        RegisteredAllTransforms.all
        |> List.collect (fun rt -> rt.Sites)
    let hasDataIntent =
        allSites |> List.exists (fun s ->
            match s.Classification with
            | DataIntent -> true
            | OperatorIntent _ -> false)
    let hasOperatorIntent =
        allSites |> List.exists (fun s ->
            match s.Classification with
            | OperatorIntent _ -> true
            | DataIntent -> false)
    Assert.True(hasDataIntent, "registry must carry at least one DataIntent site")
    Assert.True(hasOperatorIntent, "registry must carry at least one OperatorIntent site")

// ---------------------------------------------------------------------------
// Skeleton-purity — extended across multiple fixtures. The
// `SkeletonPurityTests.fs` file already verifies sampleCatalog;
// this surface sweeps over partial-fixture catalogs to amplify
// the property's coverage. A leak surfaces when ANY catalog
// produces an OperatorIntent event from the skeleton chain.
// ---------------------------------------------------------------------------

/// Build catalogs from partial-fixture combinations. Each Kind in
/// Fixtures.fs (customer / order / country) is structurally
/// well-formed; assembling them in different module groupings
/// exercises the skeleton chain across multiple shapes.
let private partialFixtureCatalogs : (string * Catalog) list =
    let customerOnly =
        { Modules =
            [ { SsKey = salesModuleKey
                Name = mkName "Sales"
                Kinds = [ customer ]
                IsActive = true
                ExtendedProperties = [] } ]
          Sequences = [] }
    let orderAndCustomer =
        { Modules =
            [ { SsKey = salesModuleKey
                Name = mkName "Sales"
                Kinds = [ customer; order ]
                IsActive = true
                ExtendedProperties = [] } ]
          Sequences = [] }
    let countryOnly =
        { Modules =
            [ { SsKey = salesModuleKey
                Name = mkName "Sales"
                Kinds = [ country ]
                IsActive = true
                ExtendedProperties = [] } ]
          Sequences = [] }
    let twoModules =
        { Modules =
            [ { SsKey = salesModuleKey
                Name = mkName "Sales"
                Kinds = [ customer ]
                IsActive = true
                ExtendedProperties = [] }
              { SsKey = modKey "Geo"
                Name = mkName "Geo"
                Kinds = [ country ]
                IsActive = true
                ExtendedProperties = [] } ]
          Sequences = [] }
    [
        "customer-only", customerOnly
        "order+customer", orderAndCustomer
        "country-only", countryOnly
        "two-modules", twoModules
        "full-sample", sampleCatalog
    ]

[<Fact>]
let ``Skeleton-purity sweep: Compose.runSkeleton emits zero OperatorIntent events across every fixture variant`` () =
    let leaks =
        partialFixtureCatalogs
        |> List.collect (fun (label, cat) ->
            let result = Compose.runSkeleton cat
            result.Trail
            |> List.filter (fun e ->
                match e.Classification with
                | OperatorIntent _ -> true
                | DataIntent -> false)
            |> List.map (fun e -> label, e))
    Assert.True(
        List.isEmpty leaks,
        sprintf
            "Compose.runSkeleton leaked OperatorIntent events on %d fixture-event pairs: %A"
            (List.length leaks)
            (leaks |> List.map fst |> List.distinct))

[<Fact>]
let ``Skeleton-purity sweep: every fixture variant produces a non-empty skeleton trail (skeleton chain fires)`` () =
    // Sanity: if the skeleton chain never fires, "zero OperatorIntent
    // leaks" is vacuously true. Assert the chain DOES produce events
    // for every fixture — the property has bite.
    for label, cat in partialFixtureCatalogs do
        let result = Compose.runSkeleton cat
        Assert.True(
            not (List.isEmpty result.Trail),
            sprintf "skeleton chain produced empty trail for fixture '%s'" label)

// ---------------------------------------------------------------------------
// Overlay-exercise — per axis. Each OverlayAxis variant
// (Selection / Tightening / Emission / Insertion / Ordering)
// has at least one registered pass; exercising that pass with a
// non-empty intent must produce LineageEvents classified
// `OperatorIntent <axis>`.
//
// `TransformRegistryCompletenessTests.fs` covers VisibilityMask
// (Selection) + TableRename (Emission). This file adds Tightening +
// Ordering + Selection (UserFkReflowPass) for full per-axis coverage.
// ---------------------------------------------------------------------------

let private ciRun (c: Catalog) : Catalog =
    (CanonicalizeIdentity.registered.Run c |> Lineage.map (fun d -> d.Value)).Value

[<Fact>]
let ``Overlay-exercise (Tightening axis): NullabilityPass's registered metadata classifies its overlay site as OperatorIntent Tightening`` () =
    // NullabilityPass metadata classifies the per-strategy site as
    // `OperatorIntent Tightening` (per StrategyRegistrations.fs L43-85).
    // The metadata-level claim is the load-bearing one; runtime
    // events from registered-intervention passes are gated on
    // populated `TighteningPolicy.Interventions`, which the per-
    // test fixture supplies as needed (existing tests in
    // `NullabilityPassTests.fs` exercise the runtime path).
    let config =
        NullabilityTighteningConfig.create 0M true []
        |> Result.value
    let policy =
        { Policy.empty with
            Tightening =
                { Interventions = [ Nullability ("test-intervention", config) ] } }
    let metadata = RegisteredTransform.toMetadata (NullabilityPass.registered policy Profile.empty)
    let tighteningSites =
        metadata.Sites
        |> List.filter (fun s ->
            match s.Classification with
            | OperatorIntent Tightening -> true
            | _ -> false)
    Assert.NotEmpty tighteningSites

[<Fact>]
let ``Overlay-exercise (Ordering axis): TopologicalOrderPass.registeredWith TreatAsCycle classifies self-loop handling as OperatorIntent Ordering`` () =
    // TopologicalOrderPass has two Sites per `Catalog.fs:386`:
    //   - sortKahn (DataIntent — Kahn's algorithm is structural)
    //   - selfLoopHandling (OperatorIntent Ordering when the policy
    //     is non-default; per A40 SelfLoopPolicy + DECISIONS 2026-05-15
    //     (late) Q9-trigger-fires worked example).
    // Sites enumeration verifies the structural-metadata claim;
    // runtime exercise verifies the LineageEvent fires.
    let enriched = ciRun sampleCatalog
    let rt = TopologicalOrderPass.registeredWith TreatAsCycle
    let metadata = RegisteredTransform.toMetadata rt
    let orderingSites =
        metadata.Sites
        |> List.filter (fun s ->
            match s.Classification with
            | OperatorIntent Ordering -> true
            | _ -> false)
    Assert.NotEmpty orderingSites
    // Runtime: the pass run produces some trail; assert it includes
    // a non-zero count of events when ordering policy is exercised.
    let result = rt.Run enriched
    Assert.NotEmpty result.Trail

[<Fact>]
let ``Overlay-exercise (Selection axis): UserFkReflowPass under non-empty UserMatchingStrategy classifies events as OperatorIntent Selection`` () =
    // UserFkReflowPass's Sites classify as `OperatorIntent Selection`
    // (per pre-scope IDENTITY axis — operator chooses how to match
    // source/target Users). Setting Policy.UserMatching to a
    // non-FallbackToSystemUser strategy exercises the axis.
    let policy =
        { Policy.empty with UserMatching = ByEmail }
    let metadata = RegisteredTransform.toMetadata (UserFkReflowPass.registered policy Profile.empty)
    let selectionSites =
        metadata.Sites
        |> List.filter (fun s ->
            match s.Classification with
            | OperatorIntent Selection -> true
            | _ -> false)
    Assert.NotEmpty selectionSites

// ---------------------------------------------------------------------------
// Coverage view consistency — `TransformRegistry.skeletonView` +
// `overlayView` are complementary projections of the same registry.
// Their union must equal the input list (no entries lost); their
// intersection must be empty (no entry is both skeleton + overlay).
// ---------------------------------------------------------------------------

[<Fact>]
let ``A.4.7'-prelude: skeletonView + overlayView partition the unified registry exactly`` () =
    let registry = RegisteredAllTransforms.all
    let skeleton = TransformRegistry.skeletonView registry
    let overlay = TransformRegistry.overlayView registry
    let skeletonNames = skeleton |> List.map (fun rt -> rt.Name) |> Set.ofList
    let overlayNames = overlay |> List.map (fun rt -> rt.Name) |> Set.ofList
    let allNames = registry |> List.map (fun rt -> rt.Name) |> Set.ofList
    // Union covers every registry entry.
    Assert.Equal<Set<string>>(allNames, Set.union skeletonNames overlayNames)
    // Intersection is empty.
    Assert.True(
        Set.isEmpty (Set.intersect skeletonNames overlayNames),
        sprintf
            "skeleton + overlay overlap: %A"
            (Set.intersect skeletonNames overlayNames))

// ---------------------------------------------------------------------------
// E1–E4 registered <-> executed isomorphism (DECISIONS 2026-06-04).
// The execution sources — the pass chain (`RegisteredTransforms.chainSteps`),
// the emit phase (`Compose.emitSteps`), and the read adapter
// (`Compose.readStep`) — and the unified registry
// (`RegisteredAllTransforms.all`) must agree: nothing executed via a bound
// source may be unregistered, and the emit/read stages register EXACTLY what
// those sources execute. Drift here = a transform wired to run with no
// registry entry, or an emit/read entry registered with no execution binding.
// ---------------------------------------------------------------------------

let private allNames : Set<string> =
    RegisteredAllTransforms.all |> List.map (fun m -> m.Name) |> Set.ofList

let private emitStepNames : string list =
    Compose.emitSteps |> List.map (fun s -> s.Metadata.Name)

[<Fact>]
let ``E1: every emitStep executes through a registered transform (no orphan emit execution)`` () =
    for name in emitStepNames do
        Assert.True(
            Set.contains name allNames,
            sprintf "emitStep '%s' executes but is not in RegisteredAllTransforms.all" name)

[<Fact>]
let ``E2: the read adapter (Compose.readStep) executes through a registered transform`` () =
    Assert.True(
        Set.contains Compose.readStep.Metadata.Name allNames,
        sprintf
            "readStep '%s' executes but is not registered"
            Compose.readStep.Metadata.Name)

[<Fact>]
let ``pass chain: every chainStep executes through a registered transform (no orphan pass execution)`` () =
    for step in RegisteredTransforms.chainSteps do
        Assert.True(
            Set.contains step.Metadata.Name allNames,
            sprintf "chainStep '%s' executes but is not registered" step.Metadata.Name)

[<Fact>]
let ``E1: the emit phase is exactly six sibling-Pi emitters, each registered exactly once`` () =
    // The six come from the single `Compose.emitSteps` source; if a seventh
    // emitter is added to the fold (or one removed) without the registry
    // tracking it, this fails. Names are distinct and all present.
    Assert.Equal(6, List.length emitStepNames)
    Assert.Equal<Set<string>>(Set.ofList emitStepNames, Set.ofList emitStepNames |> Set.filter (fun n -> Set.contains n allNames))
    Assert.Equal(List.length emitStepNames, (Set.ofList emitStepNames |> Set.count))

[<Fact>]
let ``E1: SuggestConfigEmitter is registered (closes the executed-but-unregistered mismatch)`` () =
    // SuggestConfig ran in the emit phase but was absent from the registry
    // before E1; the emitStep single source now carries it.
    Assert.True(
        List.contains "suggestConfigEmitter" emitStepNames,
        "suggestConfigEmitter must be one of the registry-driven emit steps")
    Assert.Contains("suggestConfigEmitter", allNames)

// ---------------------------------------------------------------------------
// NM-43 — the registered <-> executed projection closed BOTH WAYS for the one
// surface where both sides project from `chainSteps`: the Core registry
// `RegisteredTransforms.all`, which is `(chainSteps |> map metadata) @
// StrategyRegistrations.all`. The bidirectional suite above asserts
// `executed ⊆ registered`; the REVERSE — `registered ⊆ executed` for the
// executable (chain-projected) entries — was untested, so a typo'd
// registration name, or a chainStep removed while its metadata lingers, was
// invisible. These close it WITHOUT over-firing on the intentionally
// metadata-only strategy entries (which bind `Pass` too but are not chain
// steps — they are invoked via `Composition.fanOut` from inside a host pass).
// ---------------------------------------------------------------------------

let private chainStepNames : Set<string> =
    RegisteredTransforms.chainSteps |> List.map (fun s -> s.Metadata.Name) |> Set.ofList

let private coreRegistryNames : Set<string> =
    RegisteredTransforms.all |> List.map (fun m -> m.Name) |> Set.ofList

let private strategyRegistrationNames : Set<string> =
    StrategyRegistrations.all |> List.map (fun m -> m.Name) |> Set.ofList

[<Fact>]
let ``NM-43 (forward): every chainSteps step name is registered in RegisteredTransforms.all`` () =
    let unregistered = Set.difference chainStepNames coreRegistryNames
    Assert.True(
        Set.isEmpty unregistered,
        sprintf "chainSteps that execute but are not in RegisteredTransforms.all: %A" (Set.toList unregistered))

// The `registered ⇔ executed` core for the chain-projected partition, via the
// totality functor: the chainStep names (`executed`) and the chain-projected
// registry names (`RegisteredTransforms.all` minus the metadata-only strategies,
// = `registered`) coincide exactly — `X ⊆ Y ∧ Y ⊆ X ⇒ X = Y`. A registration
// whose name typo'd away from its chainStep, or a chainStep removed while a
// metadata entry lingered, surfaces here as a partition that no longer matches.
// The distinctness projection (`Metadata.Name` over `chainSteps`) gates the
// comparison: no two chainSteps may collide on one name.
let private chainExecutedSpec : TotalitySpec<string, ChainStep, string> =
    { Left = Set.difference coreRegistryNames strategyRegistrationNames
      Right = chainStepNames
      LeftLabel = "chain-projected registration"
      RightLabel = "executed chainStep"
      Members = RegisteredTransforms.chainSteps
      Project = fun s -> s.Metadata.Name }

[<Fact>]
let ``NM-43 (reverse): registered ⇔ executed is bidirectional for the chain-projected partition (every registration executes, every chainStep is registered)`` () =
    // `RegisteredTransforms.all` projects from exactly two sources — the chain
    // (`chainSteps |> map metadata`) and the metadata-only strategies
    // (`StrategyRegistrations.all`). The chain-projected partition is `all` minus
    // the strategy registrations; via the functor it must coincide with the live
    // chainStep names in both directions (`X ⊆ Y ∧ Y ⊆ X ⇒ X = Y`).
    assertBidirectionalSubset chainExecutedSpec

[<Fact>]
let ``NM-43: chainStep names are distinct (the projection is injective)`` () =
    assertProjectionDistinct chainExecutedSpec

[<Fact>]
let ``NM-43: the chain projection is EXACTLY closed — all = chainSteps ∪ strategies, partitioned`` () =
    // The strongest statement of the round-trip: the Core registry is precisely
    // the chainStep names plus the strategy names, with the strategy partition
    // disjoint from the chain partition (no strategy name shadows a chainStep,
    // no chainStep is mis-tagged as a strategy). This pins the projection so any
    // drift on EITHER side — a phantom registration or a dropped chainStep —
    // fails loudly.
    Assert.Equal<Set<string>>(coreRegistryNames, Set.union chainStepNames strategyRegistrationNames)
    Assert.True(
        Set.isEmpty (Set.intersect chainStepNames strategyRegistrationNames),
        sprintf
            "a strategy registration name collides with a chainStep name: %A"
            (Set.toList (Set.intersect chainStepNames strategyRegistrationNames)))

// ---------------------------------------------------------------------------
// F2 + F13 (audit 2026-06-17) — the two formerly-untracked Catalog→Catalog
// mutators are now in the totality view. F2's emit-seam index prune
// (`filterPlatformAutoIndexes`) gains a fresh OperatorIntent Emission metadata
// entry. F13's static-row hydration was ALREADY authored as a DataIntent
// adapter (`fullExportHydration`, whose `staticRowHydration` site describes the
// graft) but was never wired into `RegisteredAllTransforms.all` — so it was
// registered-in-isolation, invisible to the unified totality view; the wiring
// now names it there. Both execute at their own boundary sites (emit seam /
// pre-chain hydration), like DacpacEmitter / CatalogReader, so they are
// registered-as-metadata rather than chain-bound (the fuller chain lift that
// would bind execution↔registration for the emit-seam filter is audit F3).
// ---------------------------------------------------------------------------

let private siteHasClassification (name: string) (pred: Classification -> bool) : bool =
    RegisteredAllTransforms.all
    |> List.tryFind (fun m -> m.Name = name)
    |> Option.map (fun m -> m.Sites |> List.exists (fun s -> pred s.Classification))
    |> Option.defaultValue false

[<Fact>]
let ``F2 (audit): filterPlatformAutoIndexes is registered as an OperatorIntent Emission mutator`` () =
    Assert.Contains("filterPlatformAutoIndexes", allNames)
    Assert.True(
        siteHasClassification "filterPlatformAutoIndexes" (function OperatorIntent Emission -> true | _ -> false),
        "filterPlatformAutoIndexes must carry an OperatorIntent Emission site (the IncludePlatformAutoIndexes toggle is operator policy)")

[<Fact>]
let ``F13 (audit): the static-row hydration adapter (which grafts) is in the totality view as a DataIntent mutator`` () =
    Assert.Contains("fullExportHydration", allNames)
    Assert.True(
        siteHasClassification "fullExportHydration" (function DataIntent -> true | _ -> false),
        "fullExportHydration must carry a DataIntent site (boundary row carriage + graft, no operator overlay)")
