module Projection.Tests.ForeignKeyRulesTests

open System
open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Helpers — small named constructors for the per-reference decider.
// ForeignKeyRules and ForeignKeyOutcome are RequireQualifiedAccess.
// ---------------------------------------------------------------------------

let private ssKey (s: string) : SsKey = testKey s
let private name  (s: string) : Name  = Name.create s   |> Result.value

let private mkConfig
    (enableCreation: bool)
    (allowCrossSchema: bool)
    (allowNoCheckCreation: bool) : ForeignKeyTighteningConfig =
    ForeignKeyTighteningConfig.create
        enableCreation allowCrossSchema true false allowNoCheckCreation

let private mkProbe (rowCount: int64) (outcome: ProbeOutcome) : ProbeStatus =
    ProbeStatus.create DateTimeOffset.UnixEpoch rowCount outcome
    |> Result.value

let private mkReality
    (referenceKey: SsKey)
    (hasOrphan: bool)
    (orphanCount: int64)
    (outcome: ProbeOutcome) : ForeignKeyReality =
    { ReferenceKey = referenceKey
      HasOrphan    = hasOrphan
      OrphanCount  = orphanCount
      IsNoCheck    = false
      ProbeStatus  = mkProbe 100L outcome }

let private decide
    (config: ForeignKeyTighteningConfig)
    (catalog: Catalog)
    (sourceKind: Kind)
    (reference: Reference)
    (profile: Profile) : ForeignKeyDecision =
    ForeignKeyRules.evaluate "test-intervention" config sourceKind reference catalog profile

// The Order kind in the fixture has a single Reference (Order → Customer).
let private orderRef : Reference =
    order.References |> List.head

// ---------------------------------------------------------------------------
// PolicyDisabled gate — EnableCreation = false.
// ---------------------------------------------------------------------------

[<Fact>]
let ``policyDisabled: EnableCreation=false short-circuits with PolicyDisabled`` () =
    let cfg = mkConfig false true false
    let decision = decide cfg sampleCatalog order orderRef Profile.empty
    Assert.Equal(
        ForeignKeyOutcome.DoNotEnforce ForeignKeyKeepReason.PolicyDisabled,
        decision.Outcome)

[<Fact>]
let ``policyDisabled: profile evidence is not consulted when EnableCreation=false`` () =
    // Profile shows clean reality (no orphans, succeeded); the gate
    // still wins.
    let cfg = mkConfig false true false
    let profile =
        { Profile.empty with
            ForeignKeys = [ mkReality orderRef.SsKey false 0L Succeeded ] }
    let decision = decide cfg sampleCatalog order orderRef profile
    Assert.Equal(
        ForeignKeyOutcome.DoNotEnforce ForeignKeyKeepReason.PolicyDisabled,
        decision.Outcome)

// ---------------------------------------------------------------------------
// MissingTarget — the reference's target kind is absent from the catalog.
// V2 surfaces explicitly; V1 silently skips.
// ---------------------------------------------------------------------------

[<Fact>]
let ``missingTarget: target kind absent from catalog ⇒ DoNotEnforce(MissingTarget)`` () =
    // Construct a catalog where the Order kind references a missing
    // target.
    let danglingTargetKey = ssKey "OS_KIND_NoSuchKind"
    let danglingRef : Reference =
        { SsKey           = ssKey "OS_REF_Order_Dangling"
          Name            = name "Dangling"
          SourceAttribute = orderCustomerFkKey
          TargetKind      = danglingTargetKey
          OnDelete        = NoAction
          IsUserFk        = false; HasDbConstraint = false }
    let cfg = mkConfig true true true
    let decision = decide cfg sampleCatalog order danglingRef Profile.empty
    Assert.Equal(
        ForeignKeyOutcome.DoNotEnforce MissingTarget,
        decision.Outcome)

// ---------------------------------------------------------------------------
// DatabaseConstraintPresent — V1's HasDatabaseConstraint=true maps to
// V2's TrustedConstraint probe outcome (the probe was skipped because
// the DB constraint was trusted).
// ---------------------------------------------------------------------------

[<Fact>]
let ``trusted constraint: TrustedConstraint probe outcome ⇒ EnforceConstraint(DatabaseConstraintPresent)`` () =
    let cfg = mkConfig true true false
    let profile =
        { Profile.empty with
            ForeignKeys = [ mkReality orderRef.SsKey false 0L TrustedConstraint ] }
    let decision = decide cfg sampleCatalog order orderRef profile
    Assert.Equal(
        ForeignKeyOutcome.EnforceConstraint DatabaseConstraintPresent,
        decision.Outcome)

[<Fact>]
let ``trusted constraint: TrustedConstraint short-circuits regardless of cross-schema gate`` () =
    // Even with cross-schema disallowed, TrustedConstraint wins —
    // the DB already enforces the FK, V2 records what's there.
    let cfg = mkConfig true false false
    let profile =
        { Profile.empty with
            ForeignKeys = [ mkReality orderRef.SsKey false 0L TrustedConstraint ] }
    let decision = decide cfg sampleCatalog order orderRef profile
    Assert.Equal(
        ForeignKeyOutcome.EnforceConstraint DatabaseConstraintPresent,
        decision.Outcome)

// ---------------------------------------------------------------------------
// EnforceConstraint — clean profile, eligible.
// ---------------------------------------------------------------------------

[<Fact>]
let ``clean profile: no orphans + cross-schema allowed ⇒ EnforceConstraint(NoEvidenceObstacle)`` () =
    let cfg = mkConfig true true false
    let profile =
        { Profile.empty with
            ForeignKeys = [ mkReality orderRef.SsKey false 0L Succeeded ] }
    let decision = decide cfg sampleCatalog order orderRef profile
    Assert.Equal(
        ForeignKeyOutcome.EnforceConstraint (NoEvidenceObstacle 100L),
        decision.Outcome)

// ---------------------------------------------------------------------------
// DataHasOrphans — orphans observed, AllowNoCheckCreation=false.
// ---------------------------------------------------------------------------

[<Fact>]
let ``orphans observed + AllowNoCheckCreation=false ⇒ DoNotEnforce(DataHasOrphans)`` () =
    let cfg = mkConfig true true false
    let profile =
        { Profile.empty with
            ForeignKeys = [ mkReality orderRef.SsKey true 7L Succeeded ] }
    let decision = decide cfg sampleCatalog order orderRef profile
    Assert.Equal(
        ForeignKeyOutcome.DoNotEnforce (DataHasOrphans 7L),
        decision.Outcome)

// ---------------------------------------------------------------------------
// ScriptWithNoCheck — orphans + AllowNoCheckCreation=true (V1 Cautious).
// ---------------------------------------------------------------------------

[<Fact>]
let ``orphans observed + AllowNoCheckCreation=true ⇒ EnforceConstraint(ScriptWithNoCheck)`` () =
    let cfg = mkConfig true true true
    let profile =
        { Profile.empty with
            ForeignKeys = [ mkReality orderRef.SsKey true 12L Succeeded ] }
    let decision = decide cfg sampleCatalog order orderRef profile
    Assert.Equal(
        ForeignKeyOutcome.EnforceConstraint (ScriptWithNoCheck 12L),
        decision.Outcome)

// ---------------------------------------------------------------------------
// CrossSchemaBlocked — cross-schema FK + AllowCrossSchema=false.
// ---------------------------------------------------------------------------

[<Fact>]
let ``cross-schema: source and target in different schemas + AllowCrossSchema=false ⇒ DoNotEnforce(CrossSchemaBlocked)`` () =
    // Synthesize a catalog where Order's target is in a different schema.
    let altCustomer =
        { customer with
            Physical = { Schema = "alt"; Table = customer.Physical.Table; Catalog = None } }
    let altModule =
        { salesModule with Kinds = [ altCustomer; order; country ] }
    let altCatalog : Catalog = { Modules = [ altModule ]; Sequences = [] }
    let cfg = mkConfig true false false
    let profile =
        { Profile.empty with
            ForeignKeys = [ mkReality orderRef.SsKey false 0L Succeeded ] }
    let decision = decide cfg altCatalog order orderRef profile
    Assert.Equal(
        ForeignKeyOutcome.DoNotEnforce CrossSchemaBlocked,
        decision.Outcome)

[<Fact>]
let ``cross-schema: same schema is not blocked even when AllowCrossSchema=false`` () =
    // Order and Customer share schema "dbo" in the fixture.
    let cfg = mkConfig true false false
    let profile =
        { Profile.empty with
            ForeignKeys = [ mkReality orderRef.SsKey false 0L Succeeded ] }
    let decision = decide cfg sampleCatalog order orderRef profile
    Assert.Equal(
        ForeignKeyOutcome.EnforceConstraint (NoEvidenceObstacle 100L),
        decision.Outcome)

[<Fact>]
let ``cross-schema: comparison is case-insensitive`` () =
    // V1's SchemaEquals uses OrdinalIgnoreCase; V2 mirrors.
    let altCustomer =
        { customer with
            Physical = { Schema = "DBO"; Table = customer.Physical.Table; Catalog = None } }
    let altModule =
        { salesModule with Kinds = [ altCustomer; order; country ] }
    let altCatalog : Catalog = { Modules = [ altModule ]; Sequences = [] }
    let cfg = mkConfig true false false
    let profile =
        { Profile.empty with
            ForeignKeys = [ mkReality orderRef.SsKey false 0L Succeeded ] }
    let decision = decide cfg altCatalog order orderRef profile
    Assert.Equal(
        ForeignKeyOutcome.EnforceConstraint (NoEvidenceObstacle 100L),
        decision.Outcome)

// ---------------------------------------------------------------------------
// EvidenceMissing — probe not reliable / missing entirely.
// ---------------------------------------------------------------------------

[<Fact>]
let ``evidence missing: probe outcome FallbackTimeout ⇒ DoNotEnforce(EvidenceMissing)`` () =
    let cfg = mkConfig true true false
    let profile =
        { Profile.empty with
            ForeignKeys = [ mkReality orderRef.SsKey false 0L FallbackTimeout ] }
    let decision = decide cfg sampleCatalog order orderRef profile
    Assert.Equal(
        ForeignKeyOutcome.DoNotEnforce ForeignKeyKeepReason.EvidenceMissing,
        decision.Outcome)

[<Fact>]
let ``evidence missing: no FK reality at all ⇒ DoNotEnforce(EvidenceMissing)`` () =
    let cfg = mkConfig true true false
    let decision = decide cfg sampleCatalog order orderRef Profile.empty
    Assert.Equal(
        ForeignKeyOutcome.DoNotEnforce ForeignKeyKeepReason.EvidenceMissing,
        decision.Outcome)

[<Fact>]
let ``evidence missing: Cancelled probe outcome ⇒ DoNotEnforce(EvidenceMissing)`` () =
    let cfg = mkConfig true true true
    let profile =
        { Profile.empty with
            ForeignKeys = [ mkReality orderRef.SsKey false 0L Cancelled ] }
    let decision = decide cfg sampleCatalog order orderRef profile
    Assert.Equal(
        ForeignKeyOutcome.DoNotEnforce ForeignKeyKeepReason.EvidenceMissing,
        decision.Outcome)

[<Fact>]
let ``evidence missing: AmbiguousMapping probe outcome ⇒ DoNotEnforce(EvidenceMissing)`` () =
    let cfg = mkConfig true true true
    let profile =
        { Profile.empty with
            ForeignKeys = [ mkReality orderRef.SsKey false 0L AmbiguousMapping ] }
    let decision = decide cfg sampleCatalog order orderRef profile
    Assert.Equal(
        ForeignKeyOutcome.DoNotEnforce ForeignKeyKeepReason.EvidenceMissing,
        decision.Outcome)

// ---------------------------------------------------------------------------
// Decision metadata — every decision carries its reference SsKey and the
// intervention id that produced it.
// ---------------------------------------------------------------------------

[<Fact>]
let ``decision: ReferenceKey is the reference being decided`` () =
    let cfg = mkConfig false true false
    let decision = decide cfg sampleCatalog order orderRef Profile.empty
    Assert.Equal(orderRef.SsKey, decision.ReferenceKey)

[<Fact>]
let ``decision: InterventionId is the id passed to evaluate`` () =
    let cfg = mkConfig false true false
    let decision =
        ForeignKeyRules.evaluate
            "named-intervention-2026-05-11"
            cfg
            order
            orderRef
            sampleCatalog
            Profile.empty
    Assert.Equal("named-intervention-2026-05-11", decision.InterventionId)

// ---------------------------------------------------------------------------
// enforces / scriptsWithNoCheck helpers.
// ---------------------------------------------------------------------------

[<Fact>]
let ``enforces: true for EnforceConstraint, false for DoNotEnforce`` () =
    let blockedCfg = mkConfig false true false
    let allowedCfg = mkConfig true true false
    let cleanProfile =
        { Profile.empty with
            ForeignKeys = [ mkReality orderRef.SsKey false 0L Succeeded ] }
    let blocked = decide blockedCfg sampleCatalog order orderRef Profile.empty
    let allowed = decide allowedCfg sampleCatalog order orderRef cleanProfile
    Assert.False(ForeignKeyRules.enforces blocked)
    Assert.True (ForeignKeyRules.enforces allowed)

[<Fact>]
let ``scriptsWithNoCheck: true only for the ScriptWithNoCheck evidence variant`` () =
    let cfg = mkConfig true true true
    let orphanProfile =
        { Profile.empty with
            ForeignKeys = [ mkReality orderRef.SsKey true 5L Succeeded ] }
    let cleanProfile =
        { Profile.empty with
            ForeignKeys = [ mkReality orderRef.SsKey false 0L Succeeded ] }
    let withNoCheck = decide cfg sampleCatalog order orderRef orphanProfile
    let normal      = decide cfg sampleCatalog order orderRef cleanProfile
    Assert.True (ForeignKeyRules.scriptsWithNoCheck withNoCheck)
    Assert.False(ForeignKeyRules.scriptsWithNoCheck normal)

// ---------------------------------------------------------------------------
// Determinism — pure function; same inputs → same decision.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: evaluate is deterministic`` () =
    let cfg = mkConfig true true true
    let profile =
        { Profile.empty with
            ForeignKeys = [ mkReality orderRef.SsKey true 5L Succeeded ] }
    let d1 = decide cfg sampleCatalog order orderRef profile
    let d2 = decide cfg sampleCatalog order orderRef profile
    Assert.Equal<ForeignKeyDecision>(d1, d2)

[<Property>]
let ``property: evaluate is reflexive on equal inputs`` (id: NonEmptyString) =
    if String.IsNullOrWhiteSpace id.Get then true
    else
        let cfg = mkConfig true true false
        let d1 = ForeignKeyRules.evaluate id.Get cfg order orderRef sampleCatalog Profile.empty
        let d2 = ForeignKeyRules.evaluate id.Get cfg order orderRef sampleCatalog Profile.empty
        d1 = d2

// ---------------------------------------------------------------------------
// emptyDecisionSet — V2's strict default value.
// ---------------------------------------------------------------------------

[<Fact>]
let ``emptyDecisionSet contains zero decisions`` () =
    Assert.Empty(ForeignKeyRules.emptyDecisionSet.Decisions)

// ---------------------------------------------------------------------------
// Outcome-shape round-trip — DUs can be constructed and pattern-matched.
// ---------------------------------------------------------------------------

[<Fact>]
let ``outcome: ForeignKeyEvidence variants round-trip`` () =
    Assert.Equal<ForeignKeyEvidence>(DatabaseConstraintPresent, DatabaseConstraintPresent)
    Assert.Equal<ForeignKeyEvidence>(NoEvidenceObstacle 100L, NoEvidenceObstacle 100L)
    Assert.Equal<ForeignKeyEvidence>(ScriptWithNoCheck 5L, ScriptWithNoCheck 5L)

[<Fact>]
let ``outcome: ForeignKeyKeepReason variants round-trip`` () =
    Assert.Equal<ForeignKeyKeepReason>(ForeignKeyKeepReason.PolicyDisabled, ForeignKeyKeepReason.PolicyDisabled)
    Assert.Equal<ForeignKeyKeepReason>(DataHasOrphans 12L, DataHasOrphans 12L)
    Assert.Equal<ForeignKeyKeepReason>(CrossSchemaBlocked, CrossSchemaBlocked)
    Assert.Equal<ForeignKeyKeepReason>(CrossCatalogBlocked, CrossCatalogBlocked)
    Assert.Equal<ForeignKeyKeepReason>(DeleteRuleIgnored, DeleteRuleIgnored)
    Assert.Equal<ForeignKeyKeepReason>(ForeignKeyKeepReason.EvidenceMissing, ForeignKeyKeepReason.EvidenceMissing)
    Assert.Equal<ForeignKeyKeepReason>(MissingTarget, MissingTarget)

[<Fact>]
let ``outcome: ForeignKeyOutcome variants round-trip`` () =
    Assert.Equal<ForeignKeyOutcome>(
        ForeignKeyOutcome.EnforceConstraint DatabaseConstraintPresent,
        ForeignKeyOutcome.EnforceConstraint DatabaseConstraintPresent)
    Assert.Equal<ForeignKeyOutcome>(
        ForeignKeyOutcome.DoNotEnforce ForeignKeyKeepReason.PolicyDisabled,
        ForeignKeyOutcome.DoNotEnforce ForeignKeyKeepReason.PolicyDisabled)

// ---------------------------------------------------------------------------
// IR-refinement caveat — CrossCatalogBlocked is currently unreachable.
// The DU variant is reserved (empirical pattern verifies that the
// existing branches do not produce it from any reachable input shape).
// ---------------------------------------------------------------------------

[<Fact>]
let ``CrossCatalogBlocked is currently unreachable from any V2 IR shape`` () =
    // No matter what config + profile + catalog we throw at evaluate
    // (using the synthetic fixture's IR shape), the rule never
    // produces CrossCatalogBlocked because V2's IR has no catalog
    // (database) field. When the IR refinement lands, this test
    // documents the moment the rule becomes reachable.
    let cfg = ForeignKeyTighteningConfig.create true false false true true
    // Probe in every outcome state.
    let outcomes = [ Succeeded; FallbackTimeout; Cancelled; TrustedConstraint; AmbiguousMapping ]
    for outcome in outcomes do
        let profile =
            { Profile.empty with
                ForeignKeys = [ mkReality orderRef.SsKey false 0L outcome ] }
        let decision = decide cfg sampleCatalog order orderRef profile
        match decision.Outcome with
        | ForeignKeyOutcome.DoNotEnforce CrossCatalogBlocked ->
            Assert.Fail "CrossCatalogBlocked is not yet reachable; the IR refinement is pending"
        | _ -> ()
