module Projection.Tests.UniqueIndexRulesTests

open System
open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Tests.Fixtures
// UniqueIndexRules and UniqueIndexOutcome are RequireQualifiedAccess; the
// types live at namespace level (open Projection.Core suffices) but case
// constructors on UniqueIndexOutcome need qualification.

// ---------------------------------------------------------------------------
// Helpers — small named constructors for the per-index decider.
// ---------------------------------------------------------------------------

let private ssKey (s: string) : SsKey = testKey s
let private name  (s: string) : Name  = Name.create s   |> Result.value

let private mkConfig (single: bool) (composite: bool) : UniqueIndexTighteningConfig =
    UniqueIndexTighteningConfig.create single composite

let private mkIndex
    (key: string)
    (columns: SsKey list)
    (isUnique: bool)
    : Index =
    { IRBuilders.mkIndex (ssKey key) (name "IX") columns with IsUnique = isUnique }

let private mkProbe (rowCount: int64) : ProbeStatus =
    ProbeStatus.create DateTimeOffset.UnixEpoch rowCount Succeeded
    |> Result.value

let private mkUnreliableProbe (rowCount: int64) : ProbeStatus =
    ProbeStatus.create DateTimeOffset.UnixEpoch rowCount FallbackTimeout
    |> Result.value

let private mkSingleCandidate (attrKey: SsKey) (hasDup: bool) (rowCount: int64) : UniqueCandidateProfile =
    { AttributeKey = attrKey
      HasDuplicate = hasDup
      ProbeStatus  = mkProbe rowCount }

let private mkSingleCandidateUnreliable (attrKey: SsKey) : UniqueCandidateProfile =
    { AttributeKey = attrKey
      HasDuplicate = false
      ProbeStatus  = mkUnreliableProbe 100L }

let private mkCompositeCandidate
    (kindKey: SsKey)
    (attrKeys: SsKey list)
    (hasDup: bool)
    : CompositeUniqueCandidateProfile =
    { KindKey       = kindKey
      AttributeKeys = attrKeys
      HasDuplicate  = hasDup
      ProbeStatus   = mkProbe 100L }

let private decide
    (config: UniqueIndexTighteningConfig)
    (kind: Kind)
    (index: Index)
    (profile: Profile) : UniqueIndexDecision =
    UniqueIndexRules.evaluate "test-intervention" config kind index profile

// ---------------------------------------------------------------------------
// AlreadyUnique short-circuit — the catalog declares the index unique.
// Trusted regardless of profile or policy gates.
// ---------------------------------------------------------------------------

[<Fact>]
let ``alreadyUnique: catalog-declared unique short-circuits with AlreadyUnique`` () =
    // Single-column index, declared unique, no profile evidence at all.
    let index = mkIndex "OS_IDX_Customer_Email_Unique" [ customerNameKey ] true
    let cfg = mkConfig false false  // both toggles off — irrelevant
    let decision = decide cfg customer index Profile.empty
    Assert.Equal(
        UniqueIndexOutcome.EnforceUnique AlreadyUnique,
        decision.Outcome)

[<Fact>]
let ``alreadyUnique: composite catalog-declared unique short-circuits regardless of toggles`` () =
    let index =
        mkIndex
            "OS_IDX_Customer_NameTenant"
            [ customerNameKey; customerTenantKey ]
            true
    // EnforceMultiColumnUnique = false; AlreadyUnique still fires.
    let cfg = mkConfig false false
    let decision = decide cfg customer index Profile.empty
    Assert.Equal(
        UniqueIndexOutcome.EnforceUnique AlreadyUnique,
        decision.Outcome)

[<Fact>]
let ``alreadyUnique: profile evidence is not consulted when the catalog declares unique`` () =
    // Profile shows duplicates (which would normally block enforcement) —
    // but the catalog says the index is unique, so we trust the source.
    let index = mkIndex "OS_IDX_X" [ customerNameKey ] true
    let cfg = mkConfig true false
    let profile =
        { Profile.empty with
            UniqueCandidates = [ mkSingleCandidate customerNameKey true 100L ] }
    let decision = decide cfg customer index profile
    Assert.Equal(
        UniqueIndexOutcome.EnforceUnique AlreadyUnique,
        decision.Outcome)

// ---------------------------------------------------------------------------
// PolicyDisabled gates — the toggle for the index's column-count category
// is off; the algebra reports the gate without consulting domain reasoning.
// ---------------------------------------------------------------------------

[<Fact>]
let ``policyDisabled: single-column index with EnforceSingleColumnUnique=false yields DoNotEnforce(PolicyDisabled)`` () =
    let index = mkIndex "OS_IDX_Single" [ customerNameKey ] false
    let cfg = mkConfig false true   // composite on, single off
    let decision = decide cfg customer index Profile.empty
    Assert.Equal(
        UniqueIndexOutcome.DoNotEnforce PolicyDisabled,
        decision.Outcome)

[<Fact>]
let ``policyDisabled: composite index with EnforceMultiColumnUnique=false yields DoNotEnforce(PolicyDisabled)`` () =
    let index =
        mkIndex "OS_IDX_Composite" [ customerNameKey; customerTenantKey ] false
    let cfg = mkConfig true false   // single on, composite off
    let decision = decide cfg customer index Profile.empty
    Assert.Equal(
        UniqueIndexOutcome.DoNotEnforce PolicyDisabled,
        decision.Outcome)

[<Fact>]
let ``policyDisabled: profile evidence is not consulted when the gate is off`` () =
    // Even with strong evidence (probe succeeded, no duplicates), the
    // gate-off result wins — the algebra reports the gate.
    let index = mkIndex "OS_IDX_X" [ customerNameKey ] false
    let cfg = mkConfig false false
    let profile =
        { Profile.empty with
            UniqueCandidates = [ mkSingleCandidate customerNameKey false 1000L ] }
    let decision = decide cfg customer index profile
    Assert.Equal(
        UniqueIndexOutcome.DoNotEnforce PolicyDisabled,
        decision.Outcome)

// ---------------------------------------------------------------------------
// Profile-driven decisions, single-column path.
// ---------------------------------------------------------------------------

[<Fact>]
let ``single-column: probe succeeded + no duplicates ⇒ EnforceUnique(SingleColumnNoDuplicates)`` () =
    let index = mkIndex "OS_IDX_Single_Clean" [ customerNameKey ] false
    let cfg = mkConfig true false
    let profile =
        { Profile.empty with
            UniqueCandidates = [ mkSingleCandidate customerNameKey false 250L ] }
    let decision = decide cfg customer index profile
    Assert.Equal(
        UniqueIndexOutcome.EnforceUnique (SingleColumnNoDuplicates 250L),
        decision.Outcome)

[<Fact>]
let ``single-column: probe succeeded + duplicates present ⇒ DoNotEnforce(DataHasDuplicates)`` () =
    let index = mkIndex "OS_IDX_Single_Dirty" [ customerNameKey ] false
    let cfg = mkConfig true false
    let profile =
        { Profile.empty with
            UniqueCandidates = [ mkSingleCandidate customerNameKey true 100L ] }
    let decision = decide cfg customer index profile
    Assert.Equal(
        UniqueIndexOutcome.DoNotEnforce DataHasDuplicates,
        decision.Outcome)

[<Fact>]
let ``single-column: probe unreliable ⇒ DoNotEnforce(NoCandidateProfiled)`` () =
    // V2's collapsed-mode default: missing/unreliable evidence does not
    // tighten. The "no reliable candidate" branch produces
    // NoCandidateProfiled (the rules module collapses the "no probe" and
    // "unreliable probe" cases into the same keep reason — there's no
    // observable difference at the decision level).
    let index = mkIndex "OS_IDX_Single_Unreliable" [ customerNameKey ] false
    let cfg = mkConfig true false
    let profile =
        { Profile.empty with
            UniqueCandidates = [ mkSingleCandidateUnreliable customerNameKey ] }
    let decision = decide cfg customer index profile
    Assert.Equal(
        UniqueIndexOutcome.DoNotEnforce NoCandidateProfiled,
        decision.Outcome)

[<Fact>]
let ``single-column: no candidate profiled ⇒ DoNotEnforce(NoCandidateProfiled)`` () =
    let index = mkIndex "OS_IDX_Single_Missing" [ customerNameKey ] false
    let cfg = mkConfig true false
    let decision = decide cfg customer index Profile.empty
    Assert.Equal(
        UniqueIndexOutcome.DoNotEnforce NoCandidateProfiled,
        decision.Outcome)

[<Fact>]
let ``single-column: degenerate empty-columns index ⇒ DoNotEnforce(NoCandidateProfiled)`` () =
    // An index with zero columns is degenerate but representable. The
    // rules module classifies it as single-column (since not composite)
    // and reports NoCandidateProfiled — no probe could exist.
    let index = mkIndex "OS_IDX_Empty" [] false
    let cfg = mkConfig true true
    let decision = decide cfg customer index Profile.empty
    Assert.Equal(
        UniqueIndexOutcome.DoNotEnforce NoCandidateProfiled,
        decision.Outcome)

// ---------------------------------------------------------------------------
// Profile-driven decisions, composite path.
// ---------------------------------------------------------------------------

[<Fact>]
let ``composite: probe succeeded + no duplicates ⇒ EnforceUnique(CompositeNoDuplicates)`` () =
    let index =
        mkIndex
            "OS_IDX_Composite_Clean"
            [ customerNameKey; customerTenantKey ]
            false
    let cfg = mkConfig false true
    let profile =
        { Profile.empty with
            CompositeUniqueCandidates =
                [ mkCompositeCandidate customerKey [ customerNameKey; customerTenantKey ] false ] }
    let decision = decide cfg customer index profile
    Assert.Equal(
        UniqueIndexOutcome.EnforceUnique CompositeNoDuplicates,
        decision.Outcome)

[<Fact>]
let ``composite: probe succeeded + duplicates ⇒ DoNotEnforce(DataHasDuplicates)`` () =
    let index =
        mkIndex
            "OS_IDX_Composite_Dirty"
            [ customerNameKey; customerTenantKey ]
            false
    let cfg = mkConfig false true
    let profile =
        { Profile.empty with
            CompositeUniqueCandidates =
                [ mkCompositeCandidate customerKey [ customerNameKey; customerTenantKey ] true ] }
    let decision = decide cfg customer index profile
    Assert.Equal(
        UniqueIndexOutcome.DoNotEnforce DataHasDuplicates,
        decision.Outcome)

[<Fact>]
let ``composite: candidate is matched by attribute set, not by column order`` () =
    // Index lists [name; tenant]; profile reports [tenant; name]. Same
    // attribute set, same kind — the candidate must match.
    let index =
        mkIndex
            "OS_IDX_Composite_OrderInverted"
            [ customerNameKey; customerTenantKey ]
            false
    let cfg = mkConfig false true
    let profile =
        { Profile.empty with
            CompositeUniqueCandidates =
                [ mkCompositeCandidate customerKey [ customerTenantKey; customerNameKey ] false ] }
    let decision = decide cfg customer index profile
    Assert.Equal(
        UniqueIndexOutcome.EnforceUnique CompositeNoDuplicates,
        decision.Outcome)

[<Fact>]
let ``composite: candidate keyed to a different kind is not matched`` () =
    let index =
        mkIndex
            "OS_IDX_Composite_WrongKind"
            [ customerNameKey; customerTenantKey ]
            false
    let cfg = mkConfig false true
    // Profile has the right attribute set, but keyed to Order, not Customer.
    let profile =
        { Profile.empty with
            CompositeUniqueCandidates =
                [ mkCompositeCandidate orderKey [ customerNameKey; customerTenantKey ] false ] }
    let decision = decide cfg customer index profile
    Assert.Equal(
        UniqueIndexOutcome.DoNotEnforce NoCandidateProfiled,
        decision.Outcome)

[<Fact>]
let ``composite: no candidate ⇒ DoNotEnforce(NoCandidateProfiled)`` () =
    let index =
        mkIndex
            "OS_IDX_Composite_Missing"
            [ customerNameKey; customerTenantKey ]
            false
    let cfg = mkConfig false true
    let decision = decide cfg customer index Profile.empty
    Assert.Equal(
        UniqueIndexOutcome.DoNotEnforce NoCandidateProfiled,
        decision.Outcome)

// ---------------------------------------------------------------------------
// Decision metadata — every decision carries its index SsKey and the
// intervention id that produced it.
// ---------------------------------------------------------------------------

[<Fact>]
let ``decision: IndexKey is the index being decided`` () =
    let index = mkIndex "OS_IDX_For_Key_Test" [ customerNameKey ] true
    let decision = decide (mkConfig true true) customer index Profile.empty
    Assert.Equal(index.SsKey, decision.IndexKey)

[<Fact>]
let ``decision: InterventionId is the id passed to evaluate`` () =
    let index = mkIndex "OS_IDX_For_Id_Test" [ customerNameKey ] true
    let decision =
        UniqueIndexRules.evaluate
            "named-intervention-2026-05-10"
            (mkConfig true true)
            customer
            index
            Profile.empty
    Assert.Equal("named-intervention-2026-05-10", decision.InterventionId)

// ---------------------------------------------------------------------------
// enforces helper.
// ---------------------------------------------------------------------------

[<Fact>]
let ``enforces: true for EnforceUnique, false for DoNotEnforce`` () =
    let enforcedIndex = mkIndex "OS_IDX_E" [ customerNameKey ] true
    let blockedIndex  = mkIndex "OS_IDX_B" [ customerNameKey ] false
    let cfg = mkConfig false false  // single off → blocked
    let enforcedDecision = decide cfg customer enforcedIndex Profile.empty
    let blockedDecision  = decide cfg customer blockedIndex  Profile.empty
    Assert.True (UniqueIndexRules.enforces enforcedDecision)
    Assert.False(UniqueIndexRules.enforces blockedDecision)

// ---------------------------------------------------------------------------
// Determinism — pure function; same inputs → same decision.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: evaluate is deterministic`` () =
    let index = mkIndex "OS_IDX_Det" [ customerNameKey ] false
    let cfg = mkConfig true true
    let profile =
        { Profile.empty with
            UniqueCandidates = [ mkSingleCandidate customerNameKey false 100L ] }
    let d1 = decide cfg customer index profile
    let d2 = decide cfg customer index profile
    Assert.Equal<UniqueIndexDecision>(d1, d2)

[<Property>]
let ``property: evaluate is reflexive on equal inputs`` (id: NonEmptyString) =
    if String.IsNullOrWhiteSpace id.Get then true
    else
        let index = mkIndex "OS_IDX_Refl" [ customerNameKey ] true
        let cfg = mkConfig true true
        let d1 = UniqueIndexRules.evaluate id.Get cfg customer index Profile.empty
        let d2 = UniqueIndexRules.evaluate id.Get cfg customer index Profile.empty
        d1 = d2

// ---------------------------------------------------------------------------
// emptyDecisionSet — V2's strict default value.
// ---------------------------------------------------------------------------

[<Fact>]
let ``emptyDecisionSet contains zero decisions`` () =
    Assert.Empty(UniqueIndexRules.emptyDecisionSet.Decisions)

// ---------------------------------------------------------------------------
// Outcome-shape round-trip — DUs can be constructed and pattern-matched.
// ---------------------------------------------------------------------------

[<Fact>]
let ``outcome: UniqueIndexEvidence variants round-trip`` () =
    Assert.Equal<UniqueIndexEvidence>(AlreadyUnique, AlreadyUnique)
    Assert.Equal<UniqueIndexEvidence>(
        SingleColumnNoDuplicates 100L,
        SingleColumnNoDuplicates 100L)
    Assert.Equal<UniqueIndexEvidence>(CompositeNoDuplicates, CompositeNoDuplicates)

[<Fact>]
let ``outcome: UniqueIndexKeepReason variants round-trip`` () =
    Assert.Equal<UniqueIndexKeepReason>(PolicyDisabled, PolicyDisabled)
    Assert.Equal<UniqueIndexKeepReason>(DataHasDuplicates, DataHasDuplicates)
    Assert.Equal<UniqueIndexKeepReason>(EvidenceMissing, EvidenceMissing)
    Assert.Equal<UniqueIndexKeepReason>(NoCandidateProfiled, NoCandidateProfiled)

[<Fact>]
let ``outcome: UniqueIndexOutcome variants round-trip`` () =
    Assert.Equal<UniqueIndexOutcome>(
        UniqueIndexOutcome.EnforceUnique AlreadyUnique,
        UniqueIndexOutcome.EnforceUnique AlreadyUnique)
    Assert.Equal<UniqueIndexOutcome>(
        UniqueIndexOutcome.DoNotEnforce PolicyDisabled,
        UniqueIndexOutcome.DoNotEnforce PolicyDisabled)
