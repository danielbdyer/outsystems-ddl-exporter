module Projection.Tests.TransformRegistryCompletenessTests

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Adapters.Osm
open Projection.Tests.Fixtures

// Chapter A.4.7' slice η — `SymmetricClosure.run` is private; the
// canonical surface is `.registered.Run` returning
// `Lineage<Diagnostics<Catalog>>`. This per-file shim restores the
// `Lineage<Catalog>` shape so existing assertions keep reading.
let private scRun (c: Catalog) : Lineage<Catalog> =
    SymmetricClosure.registered.Run c |> Lineage.map (fun d -> d.Value)

// Chapter A.4.7' slice η — `NormalizeStaticPopulations.run` is private; the
// canonical surface is `.registered.Run` returning
// `Lineage<Diagnostics<Catalog>>`. This per-file shim restores the
// `Lineage<Catalog>` shape so existing assertions keep reading.
let private nspRun (c: Catalog) : Lineage<Catalog> =
    NormalizeStaticPopulations.registered.Run c |> Lineage.map (fun d -> d.Value)

// Chapter A.4.7' slice η — per-pass `let run` is private; shims wrap
// `.registered.Run` and unwrap the Diagnostics layer so existing
// assertions on `lineage.Trail` and `lineage.Value` keep reading.
let private nmRun (morphism: NamingMorphism.Morphism) (catalog: Catalog) : Lineage<Catalog> =
    (NamingMorphism.registered morphism).Run catalog
    |> Lineage.map (fun d -> d.Value)

let private ciRun (c: Catalog) : Lineage<Catalog> =
    CanonicalizeIdentity.registered.Run c |> Lineage.map (fun d -> d.Value)

// ---------------------------------------------------------------------------
// Chapter A.4.7 slice θ — bidirectional property tests.
//
// The chapter's **structural exit gate**. Per V2_PRODUCTION_CUTOVER.md
// §6.4.7 task 7 + DECISIONS 2026-05-15 (late), the registry's totality
// + classification contract is enforced bidirectionally: skeleton-
// purity (no overlay leaks into the skeleton) + overlay-exercise
// (every registered overlay fires in canary) + totality coverage
// (every transformation site has a registry entry) + harvest-
// classification (every Tolerance v1↔v2 entry references a registry
// NotImplementedInV2 entry).
//
// **Scope deviation at slice θ.** The original spec (per Q12 answer)
// enumerated five bidirectional tests; the fifth — manifest digest
// round-trip — depends on slice η (CLI + manifest extension), which
// chapter A.4.7 defers-with-trigger per consumer-pressure principle.
// Slice θ ships the four tests that are reachable from the slice
// γ/δ/ε registrations + the slice ζ classification filters.
//
// **Test-side aggregation.** Slice ζ minimum ships classification
// filter helpers but defers the full Compose.run traversal refactor.
// The slice θ property tests aggregate the registry test-side
// (referencing each pass / adapter / strategy registration directly)
// — the runtime traversal seam (Compose.runWithSkeleton) lands when
// slice ζ.2 fires per consumer pressure.
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// Test-side aggregated registry (all 18 entries: 1 adapter + 12 passes
// + 5 strategies). Configurable passes are pre-applied with sensible
// defaults; the factory pattern's static metadata doesn't vary with
// config so the per-config invocation here is a witness.
// ---------------------------------------------------------------------------

let private emptyMask : VisibilityMask.Mask = { Hide = [] }
let private hideOsNativeMask : VisibilityMask.Mask =
    { Hide = [ VisibilityMask.hideOrigin Origin.Native ] }
let private identityMorphism : NamingMorphism.Morphism = NamingMorphism.identity
let private appendUnderscoreV (n: Name) : Name =
    Name.create (System.String.Concat (Name.value n, "_v")) |> Result.value
let private renameCustomerSpec : TableRename.RenameSpec =
    { Key    = TableRename.Logical (mkName "Sales", mkName "Customer")
      Target = (TableId.create "renamed" "customer_v2" |> Result.value) }

let private allRegistrations : RegisteredTransformMetadata list =
    [ CatalogReader.registeredMetadata
      RegisteredTransform.toMetadata CanonicalizeIdentity.registered
      RegisteredTransform.toMetadata NormalizeStaticPopulations.registered
      RegisteredTransform.toMetadata SymmetricClosure.registered
      RegisteredTransform.toMetadata TopologicalOrderPass.registered
      RegisteredTransform.toMetadata (NamingMorphism.registered identityMorphism)
      RegisteredTransform.toMetadata (VisibilityMask.registered emptyMask)
      RegisteredTransform.toMetadata (NullabilityPass.registered Policy.empty Profile.empty)
      RegisteredTransform.toMetadata (UniqueIndexPass.registered Policy.empty Profile.empty)
      RegisteredTransform.toMetadata (ForeignKeyPass.registered Policy.empty Profile.empty)
      RegisteredTransform.toMetadata (CategoricalUniquenessPass.registered Policy.empty Profile.empty)
      RegisteredTransform.toMetadata (TableRename.registered [])
      RegisteredTransform.toMetadata (UserFkReflowPass.registered Policy.empty Profile.empty) ]
    @ StrategyRegistrations.all

// ---------------------------------------------------------------------------
// L3-CC-Transform-Totality: Compose.runWithSkeleton emits zero
// OperatorIntent LineageEvents — **skeleton-purity property**.
//
// Slice θ scope: at this slice, `Compose.runWithSkeleton` is not yet
// the canonical execution loop (slice ζ.2 forward signal). The
// property is verified at the per-pass level: invoking each callable
// pass in `TransformRegistry.skeletonView allRegistrations`
// individually produces only `DataIntent` lineage events. The named
// failure mode (a pass classified DataIntent that actually expresses
// operator intent) leaks here.
// ---------------------------------------------------------------------------

[<Fact>]
let ``L3-CC-Transform-Totality: skeleton-view passes emit zero OperatorIntent LineageEvents`` () =
    // skeletonView yields entries whose every Site is DataIntent.
    let skeleton = TransformRegistry.skeletonView allRegistrations
    let skeletonNames = skeleton |> List.map (fun rt -> rt.Name) |> Set.ofList
    // Sanity: skeleton contains the structurally-pure passes only.
    Assert.Contains("canonicalizeIdentity", skeletonNames)
    Assert.Contains("namingMorphism", skeletonNames)
    Assert.Contains("normalizeStaticPopulations", skeletonNames)
    Assert.Contains("symmetricClosure", skeletonNames)
    Assert.Contains("ossysCatalogReader", skeletonNames)
    Assert.Contains("cycleResolution", skeletonNames)
    // Exclusion witnesses: any pass with even one OperatorIntent site
    // is excluded from the skeleton.
    Assert.DoesNotContain("topologicalOrder", skeletonNames)
    Assert.DoesNotContain("visibilityMask", skeletonNames)
    Assert.DoesNotContain("nullability", skeletonNames)
    Assert.DoesNotContain("tableRename", skeletonNames)

    // Property: invoke each callable skeleton-view pass; assert every
    // emitted LineageEvent carries Classification = DataIntent. A
    // misclassification leak (a pass marked DataIntent but emitting
    // OperatorIntent events) fails this assertion.
    let skeletonTrails =
        [ ciRun sampleCatalog
          nmRun appendUnderscoreV sampleCatalog
          nspRun sampleCatalog
          scRun sampleCatalog ]
        |> List.collect (fun lineage -> lineage.Trail)
    Assert.NotEmpty skeletonTrails
    for event in skeletonTrails do
        Assert.Equal(DataIntent, event.Classification)

// ---------------------------------------------------------------------------
// L3-CC-Transform-Totality: every registered OperatorIntent
// transformation fires in canary — **overlay-exercise property**.
//
// Slice θ scope: at this slice, the canary is a structural axis-
// coverage check + representative-pass invocation. The full runtime
// exercise (every OperatorIntent pass produces events in an
// operator-reality scenario) requires populated Policy + Profile
// fixtures for the registered-intervention passes; slice ζ.2 or
// canary-suite wiring covers the full sweep.
// ---------------------------------------------------------------------------

[<Fact>]
let ``L3-CC-Transform-Totality: every distinct OverlayAxis used has at least one registered overlay`` () =
    let axesPresent = TransformRegistry.overlayAxes allRegistrations
    // Selection (VisibilityMask + UserFkReflowPass); Tightening (4
    // intervention passes + 4 strategies); Emission (TableRename);
    // Ordering (TopologicalOrderPass.selfLoopHandling — Q9-trigger-
    // fires worked example). Insertion has no registered consumer at
    // chapter A.4.7 close — forward signal: chapter 4.x adds an
    // Insertion-axis pass and this assertion gains a fourth
    // expected axis.
    let expected = Set.ofList [ Selection; Tightening; Emission; Ordering ]
    Assert.Equal<Set<OverlayAxis>>(expected, axesPresent)

[<Fact>]
let ``L3-CC-Transform-Totality: VisibilityMask overlay produces OperatorIntent Selection events when exercised`` () =
    let rt = VisibilityMask.registered hideOsNativeMask
    let result = rt.Run sampleCatalog
    Assert.NotEmpty result.Trail
    for event in result.Trail do
        Assert.Equal(OperatorIntent Selection, event.Classification)

[<Fact>]
let ``L3-CC-Transform-Totality: TableRename overlay produces OperatorIntent Emission events when exercised`` () =
    let rt = TableRename.registered [ renameCustomerSpec ]
    let result = rt.Run sampleCatalog
    Assert.NotEmpty result.Trail
    for event in result.Trail do
        Assert.Equal(OperatorIntent Emission, event.Classification)

// ---------------------------------------------------------------------------
// L3-CC-Transform-Totality: every transformation site has a
// registry entry — **totality coverage property**.
//
// Slice θ scope: enumerate the expected registered surfaces (12
// passes + 1 adapter + 5 strategies = 18 entries); assert each is
// present in the aggregated registry. The filesystem-scan variant
// (open Passes/ + Strategies/ + Adapters/Osm directories at test
// boundary) is the full strength of the property but requires
// directory inspection at runtime; slice θ ships the enumerated
// form. Drift detection: when a new pass / strategy / adapter
// lands without a registration, slice ζ.2's aggregation update
// surfaces the gap.
// ---------------------------------------------------------------------------

[<Fact>]
let ``L3-CC-Transform-Totality: aggregated registry covers all 12 passes + 1 adapter + 5 strategies = 18 entries`` () =
    Assert.Equal(18, List.length allRegistrations)

[<Fact>]
let ``L3-CC-Transform-Totality: every expected pass / adapter / strategy name is present in the registry`` () =
    let names = allRegistrations |> List.map (fun rt -> rt.Name) |> Set.ofList
    let expected =
        Set.ofList [
            // Adapter
            "ossysCatalogReader"
            // Passes
            "canonicalizeIdentity"
            "normalizeStaticPopulations"
            "symmetricClosure"
            "topologicalOrder"
            "namingMorphism"
            "visibilityMask"
            "nullability"
            "uniqueIndex"
            "foreignKey"
            "categoricalUniqueness"
            "tableRename"
            "userFkReflow"
            // Strategies
            "nullabilityRules"
            "uniqueIndexRules"
            "foreignKeyRules"
            "categoricalUniquenessRules"
            "cycleResolution"
        ]
    Assert.Equal<Set<string>>(expected, names)

[<Fact>]
let ``L3-CC-Transform-Totality: aggregated registry validates through TransformRegistry.create (uniqueness + rationale + status invariants)`` () =
    match TransformRegistry.create allRegistrations with
    | Ok entries -> Assert.Equal(18, List.length entries)
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        Assert.Fail(sprintf "Expected aggregated registry to validate; got errors: %s" codes)

// ---------------------------------------------------------------------------
// L3-CC-Transform-Totality: every Tolerance entry naming a v1
// transformation references a NotImplementedInV2 registry entry —
// **harvest-classification coverage property**.
//
// Slice θ scope: at chapter A.4.7 close, no Tolerance entry currently
// has a paired NotImplementedInV2 registry entry — the triple
// deliverable (Skip stub + Tolerance + NotImplementedInV2) hasn't
// fired because no chapter so far has harvested a v1 transformation
// decision to "don't bring forward." The property test ships as a
// reachability witness: when the first NotImplementedInV2 entry
// lands, this test gains substantive content. Forward signal: paired
// Tolerance retirement at chapter 4.x.
// ---------------------------------------------------------------------------

[<Fact>]
let ``L3-CC-Transform-Totality: harvest-classification coverage — zero NotImplementedInV2 entries at chapter A.4.7 close (forward signal)`` () =
    // The property text under DECISIONS 2026-05-15 (late) — every
    // v1 transformation that V2 chose not to bring forward ships as
    // a triple deliverable (Skip stub + Tolerance entry +
    // NotImplementedInV2 registry entry). At chapter A.4.7 close,
    // every registered transformation has Status = Active; no v1↔v2
    // harvest gap has surfaced yet. The test enumerates the registry
    // and witnesses the empty NotImplementedInV2 set.
    let notImplementedInV2 =
        allRegistrations
        |> List.filter (fun rt ->
            match rt.Status with
            | NotImplementedInV2 _ -> true
            | Active -> false)
    Assert.Empty notImplementedInV2

// ---------------------------------------------------------------------------
// Intentional-fail probes — each probe shows the corresponding
// property test catches its named failure mode when triggered.
// ---------------------------------------------------------------------------

[<Fact>]
let ``L3-CC-Transform-Totality intentional-fail probe: misclassified DataIntent leak fails skeletonView purity`` () =
    // Counterfactual: a pass classified DataIntent that actually
    // expresses operator intent. The Sites list would contain an
    // OperatorIntent site; skeletonView excludes it. The probe
    // demonstrates the filter catches the misclassification.
    let counterfactual : RegisteredTransformMetadata =
        { Name = "counterfactual"
          Domain = Schema
          StageBinding = Pass
          Sites =
            [ { SiteName = "claimedDataIntent"
                Classification = DataIntent
                Rationale = "claimed pure" }
              { SiteName = "actualOperatorIntent"
                Classification = OperatorIntent Selection
                Rationale = "actual filter that operator supplied" } ]
          Status = Active }
    let skeleton = TransformRegistry.skeletonView [ counterfactual ]
    Assert.Empty skeleton  // counterfactual excluded — the leak surfaces structurally

[<Fact>]
let ``L3-CC-Transform-Totality intentional-fail probe: empty-Rationale entry rejected by TransformRegistry.create`` () =
    // Counterfactual: a pass registered with empty Rationale (pillar
    // 9's harvest-discipline analysis skipped). The smart constructor
    // catches the violation at create-time.
    let counterfactual : RegisteredTransformMetadata =
        { Name = "counterfactualNoRationale"
          Domain = Schema
          StageBinding = Pass
          Sites =
            [ { SiteName = "site"
                Classification = DataIntent
                Rationale = "" } ]
          Status = Active }
    match TransformRegistry.create [ counterfactual ] with
    | Ok _ -> Assert.Fail "Expected empty-Rationale rejection."
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code)
        Assert.Contains("registry.siteRationaleEmpty", codes)

[<Fact>]
let ``L3-CC-Transform-Totality intentional-fail probe: NotImplementedInV2 with empty rationale rejected`` () =
    // Counterfactual: a v1-harvest decision skipped without
    // substantive rationale. The smart constructor catches the
    // missing triple-deliverable on the registry side.
    let counterfactual : RegisteredTransformMetadata =
        { Name = "counterfactualNoSkipRationale"
          Domain = Schema
          StageBinding = Pass
          Sites =
            [ { SiteName = "site"
                Classification = DataIntent
                Rationale = "harvest analysis present" } ]
          Status = NotImplementedInV2 "" }
    match TransformRegistry.create [ counterfactual ] with
    | Ok _ -> Assert.Fail "Expected empty-NotImplementedInV2-rationale rejection."
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code)
        Assert.Contains("registry.notImplementedRationaleEmpty", codes)
