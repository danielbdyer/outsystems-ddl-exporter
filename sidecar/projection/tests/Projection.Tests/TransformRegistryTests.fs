module Projection.Tests.TransformRegistryTests

open Xunit
open Projection.Core

// ---------------------------------------------------------------------------
// Chapter A.4.7 slice β — TransformRegistry type-system + smart-constructor
// witnesses.
//
// Slice β ships:
//   - StageBinding / Domain DUs (the registry's stage-seam and
//     domain-concern vocabularies).
//   - TransformSite record (intra-pass classification fidelity per
//     pillar 9; `DECISIONS 2026-05-15 (late)` Q11 answer).
//   - TransformStatus DU (Active | NotImplementedInV2 of rationale;
//     harvest-workflow triple-deliverable enforcement).
//   - RegisteredTransform<'In, 'Out> record + RegisteredTransformMetadata
//     type-erased projection (single definition site; no parallel
//     enumeration per Q3).
//   - TransformRegistry.create smart constructor enforcing totality
//     invariants (unique Name; non-empty Site.Rationale; non-empty
//     NotImplementedInV2 rationale).
//
// Registry's empty `all` list ships at slice β as a structural
// placeholder; slice γ + δ + ε populate as pass modules / adapter
// rules / emitter strategies expose `.registered`.
//
// Bidirectional property tests (skeleton-purity + overlay-exercise +
// totality coverage + harvest-classification cross-reference + manifest
// round-trip) land at slice θ after the population slices have shipped.
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// Type-system witnesses — the new types compile + variants are usable.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A.4.7 slice β: StageBinding carries five stage seams`` () =
    let stages : StageBinding list = [ Adapter; Pass; OrderingPolicy; Emitter; Pipeline ]
    Assert.Equal(5, stages.Length)

[<Fact>]
let ``A.4.7 slice β: Domain carries six codified concerns`` () =
    let domains : Domain list =
        [ Schema; Data; Identity; Diagnostics; CutoverSafety; CrossCutting ]
    Assert.Equal(6, domains.Length)

[<Fact>]
let ``A.4.7 slice β: TransformStatus carries Active and NotImplementedInV2`` () =
    let statuses : TransformStatus list =
        [ Active; NotImplementedInV2 "v1 had X; v2 chose Y per DECISIONS YYYY-MM-DD" ]
    // Pattern-match exhaustiveness witness; F# warns (and
    // TreatWarningsAsErrors errors) if a TransformStatus variant is
    // added without a match arm here.
    let isActive s =
        match s with
        | Active -> true
        | NotImplementedInV2 _ -> false
    Assert.True(isActive statuses.[0])
    Assert.False(isActive statuses.[1])

[<Fact>]
let ``A.4.7 slice β: TransformRegistry.stageOrdinal orders Adapter < OrderingPolicy < Pass < Emitter < Pipeline`` () =
    let ordinals = [
        TransformRegistry.stageOrdinal Adapter
        TransformRegistry.stageOrdinal OrderingPolicy
        TransformRegistry.stageOrdinal Pass
        TransformRegistry.stageOrdinal Emitter
        TransformRegistry.stageOrdinal Pipeline
    ]
    Assert.Equal<int list>([0; 1; 2; 3; 4], ordinals)

// ---------------------------------------------------------------------------
// Helper — minimum-evidence metadata entries for smart-constructor tests.
// ---------------------------------------------------------------------------

let private validSite : TransformSite =
    { SiteName = "site1"
      Classification = DataIntent
      Rationale = "harvest analysis: no operator opinion enters this site" }

let private mkEntry (name: string) (status: TransformStatus) : RegisteredTransformMetadata =
    { Name = name
      Domain = Schema
      StageBinding = Pass
      Sites = [ validSite ]
      Status = status }

let private failureCodes (errs: ValidationError list) : string list =
    errs |> List.map (fun e -> e.Code)

// ---------------------------------------------------------------------------
// Smart-constructor tests — totality invariants enforced.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A.4.7 slice β: TransformRegistry.create accepts empty list (vacuous totality)`` () =
    match TransformRegistry.create [] with
    | Ok entries -> Assert.Empty entries
    | Error es ->
        let codes = failureCodes es |> String.concat ", "
        Assert.Fail(sprintf "Expected empty list to validate; got errors: %s" codes)

[<Fact>]
let ``A.4.7 slice β: TransformRegistry.create accepts a single well-formed Active entry`` () =
    let entry = mkEntry "canonicalizeIdentity" Active
    match TransformRegistry.create [ entry ] with
    | Ok entries -> Assert.Equal(1, List.length entries)
    | Error es ->
        let codes = failureCodes es |> String.concat ", "
        Assert.Fail(sprintf "Expected well-formed entry to validate; got errors: %s" codes)

[<Fact>]
let ``A.4.7 slice β: TransformRegistry.create accepts a well-formed NotImplementedInV2 entry`` () =
    let entry = mkEntry "legacyV1Filter" (NotImplementedInV2 "filter superseded by IsActive carry-through per DECISIONS 2026-05-16 (slice β)")
    match TransformRegistry.create [ entry ] with
    | Ok entries -> Assert.Equal(1, List.length entries)
    | Error es ->
        let codes = failureCodes es |> String.concat ", "
        Assert.Fail(sprintf "Expected well-formed NotImplementedInV2 entry; got errors: %s" codes)

[<Fact>]
let ``A.4.7 slice β: TransformRegistry.create rejects duplicate Names`` () =
    let entry1 = mkEntry "shared" Active
    let entry2 = mkEntry "shared" Active
    match TransformRegistry.create [ entry1; entry2 ] with
    | Ok _ -> Assert.Fail "Expected duplicate-Name rejection."
    | Error es ->
        let codes = failureCodes es
        Assert.Contains("registry.duplicatePassName", codes)

[<Fact>]
let ``A.4.7 slice β: TransformRegistry.create rejects empty Name`` () =
    let entry = mkEntry "" Active
    match TransformRegistry.create [ entry ] with
    | Ok _ -> Assert.Fail "Expected empty-Name rejection."
    | Error es ->
        let codes = failureCodes es
        Assert.Contains("registry.nameEmpty", codes)

[<Fact>]
let ``A.4.7 slice β: TransformRegistry.create rejects empty Site.Rationale`` () =
    let entry =
        { mkEntry "passWithEmptyRationale" Active with
            Sites = [ { SiteName = "site"; Classification = DataIntent; Rationale = "" } ] }
    match TransformRegistry.create [ entry ] with
    | Ok _ -> Assert.Fail "Expected empty-Rationale rejection."
    | Error es ->
        let codes = failureCodes es
        Assert.Contains("registry.siteRationaleEmpty", codes)

[<Fact>]
let ``A.4.7 slice β: TransformRegistry.create rejects NotImplementedInV2 with empty rationale`` () =
    let entry = mkEntry "uncodifiedSkip" (NotImplementedInV2 "")
    match TransformRegistry.create [ entry ] with
    | Ok _ -> Assert.Fail "Expected empty-NotImplementedInV2-rationale rejection."
    | Error es ->
        let codes = failureCodes es
        Assert.Contains("registry.notImplementedRationaleEmpty", codes)

[<Fact>]
let ``A.4.7 slice β: TransformRegistry.create aggregates errors across multiple entries`` () =
    let bad1 = mkEntry "" Active
    let bad2 = mkEntry "uncodified" (NotImplementedInV2 "")
    let good = mkEntry "ok" Active
    match TransformRegistry.create [ bad1; bad2; good ] with
    | Ok _ -> Assert.Fail "Expected error aggregation."
    | Error es ->
        let codes = failureCodes es
        // Both errors surface; aggregation rather than first-failure.
        Assert.Contains("registry.nameEmpty", codes)
        Assert.Contains("registry.notImplementedRationaleEmpty", codes)

// ---------------------------------------------------------------------------
// Registry shape — empty `all` ships at slice β; stage filtering compiles.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A.4.7 slice β: TransformRegistry.all ships empty (populated by slices γ-ε)`` () =
    Assert.Empty TransformRegistry.all

[<Fact>]
let ``A.4.7 slice β: TransformRegistry.inStage filters by stage seam`` () =
    let entries = [
        { mkEntry "adapterRule" Active with StageBinding = Adapter }
        { mkEntry "passEntry" Active with StageBinding = Pass }
        { mkEntry "emitterEntry" Active with StageBinding = Emitter }
    ]
    let passes = TransformRegistry.inStage Pass entries
    Assert.Equal(1, List.length passes)
    Assert.Equal("passEntry", passes.[0].Name)

[<Fact>]
let ``A.4.7 slice β: TransformRegistry.allInStageOrder sorts by stage ordinal`` () =
    let entries = [
        { mkEntry "z_pipeline" Active with StageBinding = Pipeline }
        { mkEntry "a_emitter" Active with StageBinding = Emitter }
        { mkEntry "p_pass" Active with StageBinding = Pass }
        { mkEntry "o_ordering" Active with StageBinding = OrderingPolicy }
        { mkEntry "x_adapter" Active with StageBinding = Adapter }
    ]
    let sorted = TransformRegistry.allInStageOrder entries
    let names = sorted |> List.map (fun e -> e.Name)
    Assert.Equal<string list>(
        [ "x_adapter"; "o_ordering"; "p_pass"; "a_emitter"; "z_pipeline" ],
        names)

// ---------------------------------------------------------------------------
// Type-erased projection — RegisteredTransform.toMetadata drops Run.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A.4.7 slice β: RegisteredTransform.toMetadata projects fields and drops Run`` () =
    // Build a typed RegisteredTransform<int, int> with a trivial Run.
    // The Run field is dropped by toMetadata; all other fields project
    // through identically.
    let identityRun (input: int) : Lineage<Diagnostics<int>> =
        Lineage.ofValue (Diagnostics.ofValue input)
    let typed : RegisteredTransform<int, int> =
        { Name = "identityShim"
          Domain = CrossCutting
          StageBinding = Pipeline
          Sites =
            [ { SiteName = "shim"
                Classification = DataIntent
                Rationale = "test-only identity transform for toMetadata witness" } ]
          Run = identityRun
          Status = Active }
    let metadata = RegisteredTransform.toMetadata typed
    Assert.Equal("identityShim", metadata.Name)
    Assert.Equal(CrossCutting, metadata.Domain)
    Assert.Equal(Pipeline, metadata.StageBinding)
    Assert.Equal(1, List.length metadata.Sites)
    Assert.Equal("shim", metadata.Sites.[0].SiteName)
    Assert.Equal(DataIntent, metadata.Sites.[0].Classification)
    Assert.Equal(Active, metadata.Status)

// ---------------------------------------------------------------------------
// OverlayAxis.Ordering consumption — first registry use of the fifth
// OverlayAxis variant per chapter A.4.7 open Q9-trigger-fires worked
// example. The actual TopologicalOrderPass.SelfLoopHandling site lands
// at slice ε; this test witnesses the type-system reachability.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A.4.7 slice β: TransformSite carries Classification = OperatorIntent Ordering (Q9-trigger-fires worked example reachable)`` () =
    let entry =
        { mkEntry "topologicalOrder" Active with
            StageBinding = OrderingPolicy
            Sites =
              [ { SiteName = "SortKahn"
                  Classification = DataIntent
                  Rationale = "Kahn ordering depends only on graph topology" }
                { SiteName = "SelfLoopHandling"
                  Classification = OperatorIntent Ordering
                  Rationale = "SelfLoopPolicy is operator-supplied; Q9-trigger-fires fifth OverlayAxis worked example" } ] }
    match TransformRegistry.create [ entry ] with
    | Ok entries ->
        let sites = entries.[0].Sites
        Assert.Equal(2, List.length sites)
        Assert.Equal(DataIntent, sites.[0].Classification)
        Assert.Equal(OperatorIntent Ordering, sites.[1].Classification)
    | Error es ->
        let codes = failureCodes es |> String.concat ", "
        Assert.Fail(sprintf "Expected multi-site entry to validate; got errors: %s" codes)
