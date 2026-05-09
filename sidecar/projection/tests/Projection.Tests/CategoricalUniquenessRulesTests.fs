module Projection.Tests.CategoricalUniquenessRulesTests

open System
open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Helpers — synthesize attributes and Categorical evidence.
// ---------------------------------------------------------------------------

let private mkConfig (minDistinct: int64) : CategoricalUniquenessConfig =
    CategoricalUniquenessConfig.create minDistinct |> Result.value

let private mkProbe (sample: int64) (outcome: ProbeOutcome) : ProbeStatus =
    ProbeStatus.create DateTimeOffset.UnixEpoch sample outcome
    |> Result.value

let private mkCat
    (attrKey: SsKey)
    (frequencies: (string * int64) list)
    (distinctCount: int64)
    (isTruncated: bool)
    (probeOutcome: ProbeOutcome) : AttributeDistribution =
    let probe = mkProbe (max 1L distinctCount) probeOutcome
    let cat =
        CategoricalDistribution.create
            attrKey frequencies distinctCount isTruncated probe
        |> Result.value
    AttributeDistribution.Categorical cat

let private decideOnFixture
    (attribute: Attribute)
    (config: CategoricalUniquenessConfig)
    (profile: Profile) : CategoricalUniquenessDecision =
    CategoricalUniquenessRules.evaluate
        "test-intervention" config attribute profile

// Use the existing customer.Id attribute as a stable fixture target.
let private targetAttr =
    customer.Attributes |> List.find (fun a -> a.SsKey = customerIdAttrKey)

// ---------------------------------------------------------------------------
// Signal hierarchy — six branches, six tests.
// ---------------------------------------------------------------------------

[<Fact>]
let ``no Categorical evidence registered ⇒ DoNotSuggest(NoCategoricalEvidence)`` () =
    let cfg = mkConfig 2L
    let decision = decideOnFixture targetAttr cfg Profile.empty
    Assert.Equal(
        CategoricalUniquenessOutcome.DoNotSuggest
            CategoricalUniquenessKeepReason.NoCategoricalEvidence,
        decision.Outcome)

[<Fact>]
let ``unreliable probe outcome ⇒ DoNotSuggest(EvidenceMissing)`` () =
    let cfg = mkConfig 2L
    let evidence =
        mkCat targetAttr.SsKey [ "A", 1L; "B", 1L ] 2L false FallbackTimeout
    let profile = { Profile.empty with Distributions = [ evidence ] }
    let decision = decideOnFixture targetAttr cfg profile
    Assert.Equal(
        CategoricalUniquenessOutcome.DoNotSuggest
            CategoricalUniquenessKeepReason.EvidenceMissing,
        decision.Outcome)

[<Fact>]
let ``truncated vocabulary ⇒ DoNotSuggest(VocabularyTruncated)`` () =
    let cfg = mkConfig 2L
    // Truncated: distinctCount > Frequencies.Length permitted by the
    // smart constructor when IsTruncated = true.
    let evidence =
        mkCat targetAttr.SsKey [ "A", 1L; "B", 1L ] 100L true Succeeded
    let profile = { Profile.empty with Distributions = [ evidence ] }
    let decision = decideOnFixture targetAttr cfg profile
    Assert.Equal(
        CategoricalUniquenessOutcome.DoNotSuggest
            CategoricalUniquenessKeepReason.VocabularyTruncated,
        decision.Outcome)

[<Fact>]
let ``distinctCount below floor ⇒ DoNotSuggest(DistinctCountBelowThreshold)`` () =
    let cfg = mkConfig 5L  // floor of 5
    // Only 3 distinct values; below the floor.
    let evidence =
        mkCat targetAttr.SsKey [ "A", 1L; "B", 1L; "C", 1L ] 3L false Succeeded
    let profile = { Profile.empty with Distributions = [ evidence ] }
    let decision = decideOnFixture targetAttr cfg profile
    Assert.Equal(
        CategoricalUniquenessOutcome.DoNotSuggest
            (CategoricalUniquenessKeepReason.DistinctCountBelowThreshold (3L, 5L)),
        decision.Outcome)

[<Fact>]
let ``duplicates observed ⇒ DoNotSuggest(DuplicatesObserved)`` () =
    let cfg = mkConfig 2L
    // distinctCount = 3, totalObservations = 5 (one value repeats).
    let evidence =
        mkCat targetAttr.SsKey [ "A", 3L; "B", 1L; "C", 1L ] 3L false Succeeded
    let profile = { Profile.empty with Distributions = [ evidence ] }
    let decision = decideOnFixture targetAttr cfg profile
    Assert.Equal(
        CategoricalUniquenessOutcome.DoNotSuggest
            (CategoricalUniquenessKeepReason.DuplicatesObserved (3L, 5L)),
        decision.Outcome)

[<Fact>]
let ``every value distinct + above floor + complete vocabulary ⇒ SuggestUnique(EveryValueDistinct)`` () =
    let cfg = mkConfig 2L
    // distinctCount = 4, totalObservations = 4 — every value distinct.
    let evidence =
        mkCat targetAttr.SsKey
            [ "A", 1L; "B", 1L; "C", 1L; "D", 1L ] 4L false Succeeded
    let profile = { Profile.empty with Distributions = [ evidence ] }
    let decision = decideOnFixture targetAttr cfg profile
    Assert.Equal(
        CategoricalUniquenessOutcome.SuggestUnique
            (EveryValueDistinct (4L, 4L)),
        decision.Outcome)

// ---------------------------------------------------------------------------
// Signal-hierarchy ordering — earlier signals trump later ones.
// ---------------------------------------------------------------------------

[<Fact>]
let ``no evidence trumps every later signal (vacuously)`` () =
    // Already covered above; this is the explicit ordering test
    // for completeness — without evidence, no other signal can fire.
    let cfg = mkConfig 1000L  // would otherwise fail at threshold
    let decision = decideOnFixture targetAttr cfg Profile.empty
    match decision.Outcome with
    | CategoricalUniquenessOutcome.DoNotSuggest
        CategoricalUniquenessKeepReason.NoCategoricalEvidence -> ()
    | other -> Assert.Fail(sprintf "Expected NoCategoricalEvidence, got %A" other)

[<Fact>]
let ``unreliable probe trumps truncation (probe is unreliable, can't even use the truncation flag)`` () =
    let cfg = mkConfig 2L
    // Truncated AND probe unreliable — EvidenceMissing wins.
    let evidence =
        mkCat targetAttr.SsKey [ "A", 1L; "B", 1L ] 100L true Cancelled
    let profile = { Profile.empty with Distributions = [ evidence ] }
    let decision = decideOnFixture targetAttr cfg profile
    Assert.Equal(
        CategoricalUniquenessOutcome.DoNotSuggest
            CategoricalUniquenessKeepReason.EvidenceMissing,
        decision.Outcome)

[<Fact>]
let ``truncation trumps below-threshold (truncation precludes any inference)`` () =
    let cfg = mkConfig 100L  // floor of 100
    // Truncated AND below threshold — VocabularyTruncated wins
    // because truncation comes first in the hierarchy.
    let evidence =
        mkCat targetAttr.SsKey [ "A", 1L; "B", 1L ] 5L true Succeeded
    let profile = { Profile.empty with Distributions = [ evidence ] }
    let decision = decideOnFixture targetAttr cfg profile
    Assert.Equal(
        CategoricalUniquenessOutcome.DoNotSuggest
            CategoricalUniquenessKeepReason.VocabularyTruncated,
        decision.Outcome)

[<Fact>]
let ``below-threshold trumps duplicates (vocabulary too small to merit duplicates check)`` () =
    let cfg = mkConfig 10L  // floor of 10
    // distinctCount = 3, totalObservations = 5 (duplicates present),
    // distinctCount < floor — below-threshold wins because it comes
    // first in the hierarchy.
    let evidence =
        mkCat targetAttr.SsKey [ "A", 3L; "B", 1L; "C", 1L ] 3L false Succeeded
    let profile = { Profile.empty with Distributions = [ evidence ] }
    let decision = decideOnFixture targetAttr cfg profile
    Assert.Equal(
        CategoricalUniquenessOutcome.DoNotSuggest
            (CategoricalUniquenessKeepReason.DistinctCountBelowThreshold (3L, 10L)),
        decision.Outcome)

// ---------------------------------------------------------------------------
// Decision metadata.
// ---------------------------------------------------------------------------

[<Fact>]
let ``decision: AttributeKey is the attribute being decided`` () =
    let cfg = mkConfig 2L
    let decision = decideOnFixture targetAttr cfg Profile.empty
    Assert.Equal(targetAttr.SsKey, decision.AttributeKey)

[<Fact>]
let ``decision: InterventionId is the id passed to evaluate`` () =
    let cfg = mkConfig 2L
    let decision =
        CategoricalUniquenessRules.evaluate
            "named-intervention-2026-05-13" cfg targetAttr Profile.empty
    Assert.Equal("named-intervention-2026-05-13", decision.InterventionId)

// ---------------------------------------------------------------------------
// suggestsUnique helper.
// ---------------------------------------------------------------------------

[<Fact>]
let ``suggestsUnique: true for SuggestUnique, false for DoNotSuggest`` () =
    let cfg = mkConfig 2L
    let suggested =
        let evidence =
            mkCat targetAttr.SsKey
                [ "A", 1L; "B", 1L; "C", 1L; "D", 1L ] 4L false Succeeded
        decideOnFixture targetAttr cfg
            { Profile.empty with Distributions = [ evidence ] }
    let withheld = decideOnFixture targetAttr cfg Profile.empty
    Assert.True (CategoricalUniquenessRules.suggestsUnique suggested)
    Assert.False(CategoricalUniquenessRules.suggestsUnique withheld)

// ---------------------------------------------------------------------------
// Determinism / reflexivity — pure function commitment.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: evaluate is deterministic`` () =
    let cfg = mkConfig 2L
    let evidence =
        mkCat targetAttr.SsKey [ "A", 1L; "B", 1L ] 2L false Succeeded
    let profile = { Profile.empty with Distributions = [ evidence ] }
    let d1 = decideOnFixture targetAttr cfg profile
    let d2 = decideOnFixture targetAttr cfg profile
    Assert.Equal<CategoricalUniquenessDecision>(d1, d2)

[<Property>]
let ``property: evaluate is reflexive on equal inputs`` (id: NonEmptyString) =
    if String.IsNullOrWhiteSpace id.Get then true
    else
        let cfg = mkConfig 2L
        let d1 = CategoricalUniquenessRules.evaluate id.Get cfg targetAttr Profile.empty
        let d2 = CategoricalUniquenessRules.evaluate id.Get cfg targetAttr Profile.empty
        d1 = d2

// ---------------------------------------------------------------------------
// emptyDecisionSet — V2's strict default.
// ---------------------------------------------------------------------------

[<Fact>]
let ``emptyDecisionSet contains zero decisions`` () =
    Assert.Empty(CategoricalUniquenessRules.emptyDecisionSet.Decisions)

// ---------------------------------------------------------------------------
// Outcome-shape round-trip.
// ---------------------------------------------------------------------------

[<Fact>]
let ``outcome: CategoricalUniquenessEvidence variants round-trip`` () =
    Assert.Equal<CategoricalUniquenessEvidence>(
        EveryValueDistinct (3L, 3L),
        EveryValueDistinct (3L, 3L))

[<Fact>]
let ``outcome: CategoricalUniquenessKeepReason variants round-trip`` () =
    Assert.Equal<CategoricalUniquenessKeepReason>(
        CategoricalUniquenessKeepReason.NoCategoricalEvidence,
        CategoricalUniquenessKeepReason.NoCategoricalEvidence)
    Assert.Equal<CategoricalUniquenessKeepReason>(
        CategoricalUniquenessKeepReason.EvidenceMissing,
        CategoricalUniquenessKeepReason.EvidenceMissing)
    Assert.Equal<CategoricalUniquenessKeepReason>(
        CategoricalUniquenessKeepReason.VocabularyTruncated,
        CategoricalUniquenessKeepReason.VocabularyTruncated)
    Assert.Equal<CategoricalUniquenessKeepReason>(
        CategoricalUniquenessKeepReason.DistinctCountBelowThreshold (3L, 5L),
        CategoricalUniquenessKeepReason.DistinctCountBelowThreshold (3L, 5L))
    Assert.Equal<CategoricalUniquenessKeepReason>(
        CategoricalUniquenessKeepReason.DuplicatesObserved (3L, 5L),
        CategoricalUniquenessKeepReason.DuplicatesObserved (3L, 5L))

[<Fact>]
let ``outcome: CategoricalUniquenessOutcome variants round-trip`` () =
    Assert.Equal<CategoricalUniquenessOutcome>(
        CategoricalUniquenessOutcome.SuggestUnique (EveryValueDistinct (3L, 3L)),
        CategoricalUniquenessOutcome.SuggestUnique (EveryValueDistinct (3L, 3L)))
    Assert.Equal<CategoricalUniquenessOutcome>(
        CategoricalUniquenessOutcome.DoNotSuggest
            CategoricalUniquenessKeepReason.NoCategoricalEvidence,
        CategoricalUniquenessOutcome.DoNotSuggest
            CategoricalUniquenessKeepReason.NoCategoricalEvidence)

// ---------------------------------------------------------------------------
// Config validation surface.
// ---------------------------------------------------------------------------

[<Fact>]
let ``CategoricalUniquenessConfig.create: rejects negative floor`` () =
    let result = CategoricalUniquenessConfig.create -1L
    match result with
    | Ok _ -> Assert.Fail "Expected failure on negative floor"
    | Error errs ->
        Assert.Contains(errs, fun e ->
            e.Code = "categoricalUniquenessConfig.minDistinctCountForUniqueness.negative")

[<Fact>]
let ``CategoricalUniquenessConfig.create: accepts zero (no floor)`` () =
    let result = CategoricalUniquenessConfig.create 0L
    match result with
    | Ok cfg -> Assert.Equal(0L, cfg.MinDistinctCountForUniqueness)
    | Error errs -> Assert.Fail(sprintf "Expected success, got %A" errs)
