module Projection.Tests.CompositionTests

open Xunit
open Projection.Core
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Direct tests for Composition.fanOut, the canonical strategy-driver
// primitive landed in session 11 commit 4 per DECISIONS 2026-05-13.
//
// The four pass drivers (NullabilityPass, UniqueIndexPass,
// ForeignKeyPass, CategoricalUniquenessPass) all delegate to fanOut;
// their existing pass-tests indirectly exercise it. These tests
// exercise the primitive directly via a synthetic minimal strategy
// to validate the contract independent of any specific strategy's
// quirks.
// ---------------------------------------------------------------------------

// A synthetic per-attribute strategy: every attribute gets a
// "decision" that's just its SsKey + the intervention id. No real
// rules logic; we test the primitive's structural commitments.

type private SyntheticDecision = {
    AttributeKey   : SsKey
    InterventionId : string
}

type private SyntheticDecisionSet = {
    Decisions : SyntheticDecision list
}

type private SyntheticConfig = {
    Tag : string
}

let private syntheticEmptyDecisionSet : SyntheticDecisionSet =
    { Decisions = [] }

// Synthetic intervention filter — pulls Nullability interventions out
// of the registry but reinterprets the config as a synthetic tag for
// test purposes. (We don't add a synthetic TighteningIntervention
// variant — that would be over-extension; the test reuses the
// existing Nullability variant as a carrier.)
let private syntheticInterventionFilter
    (policy: TighteningPolicy)
    : (string * SyntheticConfig) list =
    policy.Interventions
    |> List.choose (fun intervention ->
        match intervention with
        | Nullability (id, _) -> Some (id, { Tag = id })
        | _                   -> None)

let private syntheticSortedAttributes (catalog: Catalog) : (Kind * Attribute) list =
    catalog.Modules
    |> List.collect (fun m -> m.Kinds)
    |> List.sortBy (fun k -> k.SsKey)
    |> List.collect (fun k ->
        k.Attributes
        |> List.sortBy (fun a -> a.SsKey)
        |> List.map (fun a -> k, a))

let private syntheticEvaluate
    (id: string)
    (_cfg: SyntheticConfig)
    ((_kind, attr): Kind * Attribute)
    (_profile: Profile)
    : SyntheticDecision =
    { AttributeKey = attr.SsKey; InterventionId = id }

let private syntheticBuildEvent (decision: SyntheticDecision) : LineageEvent =
    { PassName      = "synthetic"
      PassVersion   = 1
      SsKey         = decision.AttributeKey
      TransformKind = Annotated decision.InterventionId }

let private syntheticConfig : Composition.FanOutConfig<Kind * Attribute, SyntheticConfig, SyntheticDecision, SyntheticDecisionSet> = {
    InterventionFilter = syntheticInterventionFilter
    SortedContexts     = syntheticSortedAttributes
    Evaluate           = syntheticEvaluate
    EmptyDecisionSet   = syntheticEmptyDecisionSet
    WrapDecisions      = fun decisions -> { Decisions = decisions }
    BuildEvent         = syntheticBuildEvent
}

let private nullabilityCfg : NullabilityTighteningConfig =
    NullabilityTighteningConfig.create 0.0m false [] |> Result.value

// ---------------------------------------------------------------------------
// Observable identity on empty policy.
// ---------------------------------------------------------------------------

[<Fact>]
let ``fanOut: empty policy yields the configured empty decision set with no events`` () =
    let lineage = Composition.fanOut syntheticConfig sampleCatalog Policy.empty Profile.empty
    Assert.Equal<SyntheticDecisionSet>(syntheticEmptyDecisionSet, lineage.Value)
    Assert.Empty(lineage.Trail)

[<Fact>]
let ``fanOut: policy with interventions of the wrong variant yields empty (filter is sole gatekeeper)`` () =
    // Register a UniqueIndex intervention; the synthetic filter reads
    // Nullability only, so it returns empty.
    let policy =
        { Policy.empty with
            Tightening =
                { Interventions =
                    [ UniqueIndex ("u-1", UniqueIndexTighteningConfig.create true true) ] } }
    let lineage = Composition.fanOut syntheticConfig sampleCatalog policy Profile.empty
    Assert.Empty(lineage.Value.Decisions)
    Assert.Empty(lineage.Trail)

// ---------------------------------------------------------------------------
// One-intervention fan-out.
// ---------------------------------------------------------------------------

[<Fact>]
let ``fanOut: one intervention emits one decision per context`` () =
    let policy =
        { Policy.empty with
            Tightening =
                { Interventions = [ Nullability ("alpha", nullabilityCfg) ] } }
    let lineage = Composition.fanOut syntheticConfig sampleCatalog policy Profile.empty
    let totalAttributes =
        Catalog.allKinds sampleCatalog |> List.sumBy (fun k -> k.Attributes.Length)
    Assert.Equal(totalAttributes, lineage.Value.Decisions.Length)
    Assert.Equal(totalAttributes, lineage.Trail.Length)

[<Fact>]
let ``fanOut: every decision carries the intervention id supplied to evaluate`` () =
    let policy =
        { Policy.empty with
            Tightening =
                { Interventions = [ Nullability ("alpha", nullabilityCfg) ] } }
    let lineage = Composition.fanOut syntheticConfig sampleCatalog policy Profile.empty
    Assert.All(lineage.Value.Decisions, fun d ->
        Assert.Equal("alpha", d.InterventionId))

// ---------------------------------------------------------------------------
// Multi-intervention fan-out.
// ---------------------------------------------------------------------------

[<Fact>]
let ``fanOut: N interventions x M contexts produces N*M decisions`` () =
    let policy =
        { Policy.empty with
            Tightening =
                { Interventions =
                    [ Nullability ("alpha", nullabilityCfg)
                      Nullability ("beta",  nullabilityCfg)
                      Nullability ("gamma", nullabilityCfg) ] } }
    let lineage = Composition.fanOut syntheticConfig sampleCatalog policy Profile.empty
    let totalAttributes =
        Catalog.allKinds sampleCatalog |> List.sumBy (fun k -> k.Attributes.Length)
    Assert.Equal(totalAttributes * 3, lineage.Value.Decisions.Length)
    Assert.Equal(totalAttributes * 3, lineage.Trail.Length)

// ---------------------------------------------------------------------------
// Determinism & iteration order.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: fanOut is deterministic on the triple`` () =
    let policy =
        { Policy.empty with
            Tightening =
                { Interventions = [ Nullability ("alpha", nullabilityCfg) ] } }
    let r1 = Composition.fanOut syntheticConfig sampleCatalog policy Profile.empty
    let r2 = Composition.fanOut syntheticConfig sampleCatalog policy Profile.empty
    Assert.Equal<SyntheticDecisionSet>(r1.Value, r2.Value)
    Assert.Equal<LineageEvent list>(r1.Trail, r2.Trail)

[<Fact>]
let ``fanOut: events emitted via BuildEvent appear once per decision in trail`` () =
    let policy =
        { Policy.empty with
            Tightening =
                { Interventions = [ Nullability ("alpha", nullabilityCfg) ] } }
    let lineage = Composition.fanOut syntheticConfig sampleCatalog policy Profile.empty
    Assert.Equal(lineage.Value.Decisions.Length, lineage.Trail.Length)
    // Each event references its decision's SsKey.
    let attributeKeys =
        Catalog.allKinds sampleCatalog
        |> List.collect (fun k -> k.Attributes |> List.map (fun a -> a.SsKey))
        |> Set.ofList
    Assert.All(lineage.Trail, fun e ->
        Assert.Contains(e.SsKey, attributeKeys))

// ---------------------------------------------------------------------------
// EmptyDecisionSet bypasses catalog and profile.
// ---------------------------------------------------------------------------

[<Fact>]
let ``fanOut: empty-policy path does not consult the catalog or profile`` () =
    // Synthetic config that would error if the catalog were enumerated;
    // verifies the empty-policy path short-circuits before any
    // catalog access.
    let exploding : Composition.FanOutConfig<Kind * Attribute, SyntheticConfig, SyntheticDecision, SyntheticDecisionSet> =
        { syntheticConfig with
            SortedContexts = fun _ -> failwith "should not be called" }
    let lineage = Composition.fanOut exploding sampleCatalog Policy.empty Profile.empty
    Assert.Equal<SyntheticDecisionSet>(syntheticEmptyDecisionSet, lineage.Value)

// ---------------------------------------------------------------------------
// StrategyEvaluator alias — codified at session 11 commit 5.
// ---------------------------------------------------------------------------

[<Fact>]
let ``StrategyEvaluator alias names the canonical four-input shape`` () =
    // Type-level test: the alias's signature must accept any function
    // matching string -> 'config -> 'context -> Profile -> 'decision.
    // The synthetic evaluate from above conforms; assignment compiles.
    let _ : StrategyEvaluator<Kind * Attribute, SyntheticConfig, SyntheticDecision> =
        syntheticEvaluate
    Assert.True(true)

[<Fact>]
let ``StrategyEvaluator alias is the type of FanOutConfig.Evaluate`` () =
    // Type-level test: the field must accept any function conforming
    // to StrategyEvaluator. Assignment compiles iff the alias and the
    // field share the same shape.
    let evaluatorAsAlias : StrategyEvaluator<Kind * Attribute, SyntheticConfig, SyntheticDecision> =
        syntheticEvaluate
    let _ : Composition.FanOutConfig<Kind * Attribute, SyntheticConfig, SyntheticDecision, SyntheticDecisionSet> =
        { syntheticConfig with Evaluate = evaluatorAsAlias }
    Assert.True(true)
