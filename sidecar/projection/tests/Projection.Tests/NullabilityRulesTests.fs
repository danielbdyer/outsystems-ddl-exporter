module Projection.Tests.NullabilityRulesTests

open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Tests.Fixtures
// NullabilityRules and NullabilityOutcome are RequireQualifiedAccess;
// the types live at namespace level (open Projection.Core suffices) but
// case constructors on NullabilityOutcome need qualification.

// ---------------------------------------------------------------------------
// Helpers — small named fixtures for the per-attribute decider.
// ---------------------------------------------------------------------------

let private mkConfig
    (nullBudget: decimal)
    (allowRelax: bool)
    (overrides: TighteningOverride list) : NullabilityTighteningConfig =
    NullabilityTighteningConfig.create nullBudget allowRelax overrides
    |> Result.value

let private decideOnFixture
    (attribute: Attribute)
    (config: NullabilityTighteningConfig) : NullabilityDecision =
    NullabilityRules.evaluate "test-intervention" config attribute Profile.empty

// ---------------------------------------------------------------------------
// Operator overrides are absolute — bypass the entire signal hierarchy.
// ---------------------------------------------------------------------------

[<Fact>]
let ``override: a KeepNullable override on a PK attribute still keeps it nullable`` () =
    // Override is absolute — even structural signals (PK) are bypassed.
    let pkAttr = customer.Attributes |> List.find (fun a -> a.IsPrimaryKey)
    let cfg =
        mkConfig 0.0m false
            [ { AttributeKey = pkAttr.SsKey; Action = OverrideAction.KeepNullable } ]
    let decision = decideOnFixture pkAttr cfg
    match decision.Outcome with
    | NullabilityOutcome.KeepNullable OperatorOverride -> ()
    | other -> Assert.Fail(sprintf "Expected KeepNullable OperatorOverride, got %A" other)

[<Fact>]
let ``override: a KeepNullable override on a physically-NOT-NULL column still keeps it nullable`` () =
    // The Tenant column is physically NOT NULL on Customer.
    let tenantAttr =
        customer.Attributes
        |> List.find (fun a -> a.SsKey = customerTenantKey)
    let cfg =
        mkConfig 0.0m false
            [ { AttributeKey = tenantAttr.SsKey; Action = OverrideAction.KeepNullable } ]
    let decision = decideOnFixture tenantAttr cfg
    match decision.Outcome with
    | NullabilityOutcome.KeepNullable OperatorOverride -> ()
    | other -> Assert.Fail(sprintf "Expected KeepNullable OperatorOverride, got %A" other)

[<Fact>]
let ``override: an unrelated override does NOT bypass other signals`` () =
    let pkAttr = customer.Attributes |> List.find (fun a -> a.IsPrimaryKey)
    let unrelated = attrKey ["Unrelated"]
    let cfg =
        mkConfig 0.0m false
            [ { AttributeKey = unrelated; Action = OverrideAction.KeepNullable } ]
    let decision = decideOnFixture pkAttr cfg
    Assert.Equal(NullabilityOutcome.EnforceNotNull PrimaryKey, decision.Outcome)

// ---------------------------------------------------------------------------
// Structural signals — PK and PhysicallyNotNull fire regardless of mode /
// budget / profile.
// ---------------------------------------------------------------------------

[<Fact>]
let ``structural: a PK attribute is always EnforceNotNull(PrimaryKey)`` () =
    let pkAttr = customer.Attributes |> List.find (fun a -> a.IsPrimaryKey)
    let decision = decideOnFixture pkAttr (mkConfig 0.0m false [])
    Assert.Equal(NullabilityOutcome.EnforceNotNull PrimaryKey, decision.Outcome)

[<Fact>]
let ``structural: a non-PK physically-NOT-NULL attribute is EnforceNotNull(PhysicallyNotNull)`` () =
    let nameAttr =
        customer.Attributes
        |> List.find (fun a -> a.SsKey = customerNameKey)
    // Sanity: this attribute is physically NOT NULL in the fixture.
    Assert.False(nameAttr.Column.IsNullable)
    Assert.False(nameAttr.IsPrimaryKey)
    let decision = decideOnFixture nameAttr (mkConfig 0.0m false [])
    Assert.Equal(NullabilityOutcome.EnforceNotNull PhysicallyNotNull, decision.Outcome)

[<Fact>]
let ``structural: a physically-nullable non-PK attribute without overrides yields KeepNullable(NoTighteningSignal)`` () =
    // Synthesize a nullable, non-PK attribute.
    let nullable : Attribute =
        { SsKey        = attrKey ["Test"; "NullableNonPk"]
          Name         = Name.create "Optional" |> Result.value
          Type         = Text
          Column       = { ColumnName = "OPTIONAL"; IsNullable = true }
          IsPrimaryKey = false; IsMandatory = false; Length = None; Precision = None; Scale = None; IsIdentity = false; Description = None; IsActive = true; DefaultValue = None; Computed = None; ExtendedProperties = [] }
    let decision = decideOnFixture nullable (mkConfig 0.0m false [])
    Assert.Equal(NullabilityOutcome.KeepNullable NoTighteningSignal, decision.Outcome)

// ---------------------------------------------------------------------------
// Decision metadata — every decision carries its attribute SsKey and the
// intervention id that produced it.
// ---------------------------------------------------------------------------

[<Fact>]
let ``decision: AttributeKey is the attribute being decided`` () =
    let pkAttr = customer.Attributes |> List.find (fun a -> a.IsPrimaryKey)
    let decision = decideOnFixture pkAttr (mkConfig 0.0m false [])
    Assert.Equal(pkAttr.SsKey, decision.AttributeKey)

[<Fact>]
let ``decision: InterventionId is the id passed to evaluate`` () =
    let pkAttr = customer.Attributes |> List.find (fun a -> a.IsPrimaryKey)
    let decision =
        NullabilityRules.evaluate
            "named-intervention-2026-05-09"
            (mkConfig 0.0m false [])
            pkAttr
            Profile.empty
    Assert.Equal("named-intervention-2026-05-09", decision.InterventionId)

// ---------------------------------------------------------------------------
// enforces / requiresApproval helpers.
// ---------------------------------------------------------------------------

[<Fact>]
let ``enforces: true for EnforceNotNull, false for KeepNullable`` () =
    let pkAttr = customer.Attributes |> List.find (fun a -> a.IsPrimaryKey)
    let nullable : Attribute =
        { SsKey        = attrKey ["T"]
          Name         = Name.create "T" |> Result.value
          Type         = Text
          Column       = { ColumnName = "T"; IsNullable = true }
          IsPrimaryKey = false; IsMandatory = false; Length = None; Precision = None; Scale = None; IsIdentity = false; Description = None; IsActive = true; DefaultValue = None; Computed = None; ExtendedProperties = [] }
    let cfg = mkConfig 0.0m false []
    Assert.True (NullabilityRules.enforces (decideOnFixture pkAttr cfg))
    Assert.False(NullabilityRules.enforces (decideOnFixture nullable cfg))

[<Fact>]
let ``requiresApproval: false for the rules covered by the synthetic fixture`` () =
    // V2's IR does not yet carry IsMandatory on Attribute; the
    // approval branch is unreachable on the synthetic fixture. When
    // IsMandatory lands, this test extends with the conflict scenario.
    let pkAttr = customer.Attributes |> List.find (fun a -> a.IsPrimaryKey)
    let cfg = mkConfig 0.0m false []
    Assert.False(NullabilityRules.requiresApproval (decideOnFixture pkAttr cfg))

// ---------------------------------------------------------------------------
// Determinism — pure function; same inputs → same decision.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: evaluate is deterministic`` () =
    let pkAttr = customer.Attributes |> List.find (fun a -> a.IsPrimaryKey)
    let cfg = mkConfig 0.0m false []
    let d1 = decideOnFixture pkAttr cfg
    let d2 = decideOnFixture pkAttr cfg
    Assert.Equal<NullabilityDecision>(d1, d2)

[<Property>]
let ``property: evaluate is reflexive on equal inputs`` (id1: NonEmptyString) =
    if System.String.IsNullOrWhiteSpace id1.Get then true
    else
        let pkAttr = customer.Attributes |> List.find (fun a -> a.IsPrimaryKey)
        let cfg = mkConfig 0.0m false []
        let d1 = NullabilityRules.evaluate id1.Get cfg pkAttr Profile.empty
        let d2 = NullabilityRules.evaluate id1.Get cfg pkAttr Profile.empty
        d1 = d2

// ---------------------------------------------------------------------------
// emptyDecisionSet — V2's strict default value.
// ---------------------------------------------------------------------------

[<Fact>]
let ``emptyDecisionSet contains zero decisions`` () =
    Assert.Empty(NullabilityRules.emptyDecisionSet.Decisions)

// ---------------------------------------------------------------------------
// Outcome-shape round-trip — DUs can be constructed and pattern-matched.
// ---------------------------------------------------------------------------

[<Fact>]
let ``outcome: NullabilityEvidence variants round-trip`` () =
    Assert.Equal<NullabilityEvidence>(PrimaryKey, PrimaryKey)
    Assert.Equal<NullabilityEvidence>(PhysicallyNotNull, PhysicallyNotNull)
    Assert.Equal<NullabilityEvidence>(LogicalMandatoryNoProfile, LogicalMandatoryNoProfile)
    Assert.Equal<NullabilityEvidence>(LogicalMandatoryNoNulls 100L, LogicalMandatoryNoNulls 100L)
    Assert.Equal<NullabilityEvidence>(
        LogicalMandatoryWithinBudget (4L, 100L, 0.05m),
        LogicalMandatoryWithinBudget (4L, 100L, 0.05m))

[<Fact>]
let ``outcome: KeepNullableReason variants round-trip`` () =
    Assert.Equal<KeepNullableReason>(OperatorOverride, OperatorOverride)
    Assert.Equal<KeepNullableReason>(NoTighteningSignal, NoTighteningSignal)
    Assert.Equal<KeepNullableReason>(
        RelaxedUnderEvidence (12L, 100L, 0.05m),
        RelaxedUnderEvidence (12L, 100L, 0.05m))

[<Fact>]
let ``outcome: NullabilityConflict variants round-trip`` () =
    Assert.Equal<NullabilityConflict>(
        MandatoryButHasNullsBeyondBudget (12L, 100L, 0.05m),
        MandatoryButHasNullsBeyondBudget (12L, 100L, 0.05m))

[<Fact>]
let ``outcome: NullabilityOutcome variants round-trip`` () =
    Assert.Equal<NullabilityOutcome>(
        NullabilityOutcome.EnforceNotNull PrimaryKey,
        NullabilityOutcome.EnforceNotNull PrimaryKey)
    Assert.Equal<NullabilityOutcome>(
        NullabilityOutcome.KeepNullable OperatorOverride,
        NullabilityOutcome.KeepNullable OperatorOverride)
    Assert.Equal<NullabilityOutcome>(
        NullabilityOutcome.RequireOperatorApproval (MandatoryButHasNullsBeyondBudget (12L, 100L, 0.05m)),
        NullabilityOutcome.RequireOperatorApproval (MandatoryButHasNullsBeyondBudget (12L, 100L, 0.05m)))

// ---------------------------------------------------------------------------
// IsMandatory branches (activated 2026-05-10) — V1 mandatory-driven
// signal hierarchy. The structural commitment is that mandatory
// signals fire only when the attribute's IsMandatory flag is true;
// physically nullable, non-PK, non-mandatory attributes still go to
// KeepNullable(NoTighteningSignal).
// ---------------------------------------------------------------------------

let private mkMandatoryAttr (key: string) (isNullable: bool) : Attribute =
    { SsKey        = testKey key
      Name         = Name.create "M" |> Result.value
      Type         = Text
      Column       = { ColumnName = "M"; IsNullable = isNullable }
      IsPrimaryKey = false
      IsMandatory = true; Length = None; Precision = None; Scale = None; IsIdentity = false; Description = None; IsActive = true; DefaultValue = None; Computed = None; ExtendedProperties = [] }

let private mkColProfile (attrKey: SsKey) (rowCount: int64) (nullCount: int64) : ColumnProfile =
    let probe =
        ProbeStatus.create System.DateTimeOffset.UnixEpoch rowCount Succeeded
        |> Result.value
    { AttributeKey         = attrKey
      RowCount             = rowCount
      NullCount            = nullCount
      NullCountProbeStatus = probe }

[<Fact>]
let ``mandatory: profile absent ⇒ EnforceNotNull(LogicalMandatoryNoProfile)`` () =
    let attr = mkMandatoryAttr "OS_ATTR_M_NoProfile" true
    let cfg = mkConfig 0.0m false []
    let decision = NullabilityRules.evaluate "test" cfg attr Profile.empty
    Assert.Equal(
        NullabilityOutcome.EnforceNotNull LogicalMandatoryNoProfile,
        decision.Outcome)

[<Fact>]
let ``mandatory: profile shows zero nulls ⇒ EnforceNotNull(LogicalMandatoryNoNulls)`` () =
    let attr = mkMandatoryAttr "OS_ATTR_M_NoNulls" true
    let profile = { Profile.empty with Columns = [ mkColProfile attr.SsKey 100L 0L ] }
    let cfg = mkConfig 0.0m false []
    let decision = NullabilityRules.evaluate "test" cfg attr profile
    Assert.Equal(
        NullabilityOutcome.EnforceNotNull (LogicalMandatoryNoNulls 100L),
        decision.Outcome)

[<Fact>]
let ``mandatory: profile shows nulls within budget ⇒ EnforceNotNull(LogicalMandatoryWithinBudget)`` () =
    let attr = mkMandatoryAttr "OS_ATTR_M_WithinBudget" true
    // 4 nulls / 100 rows = 4%; budget 5% — within budget.
    let profile = { Profile.empty with Columns = [ mkColProfile attr.SsKey 100L 4L ] }
    let cfg = mkConfig 0.05m false []
    let decision = NullabilityRules.evaluate "test" cfg attr profile
    Assert.Equal(
        NullabilityOutcome.EnforceNotNull (LogicalMandatoryWithinBudget (4L, 100L, 0.05m)),
        decision.Outcome)

[<Fact>]
let ``mandatory: nulls beyond budget + relaxation forbidden ⇒ RequireOperatorApproval`` () =
    let attr = mkMandatoryAttr "OS_ATTR_M_BeyondBudget" true
    // 12 nulls / 100 rows = 12%; budget 5% — beyond budget.
    let profile = { Profile.empty with Columns = [ mkColProfile attr.SsKey 100L 12L ] }
    // AllowMandatoryRelaxation = false (Cautious-equivalent default).
    let cfg = mkConfig 0.05m false []
    let decision = NullabilityRules.evaluate "test" cfg attr profile
    Assert.Equal(
        NullabilityOutcome.RequireOperatorApproval
            (MandatoryButHasNullsBeyondBudget (12L, 100L, 0.05m)),
        decision.Outcome)

[<Fact>]
let ``mandatory: nulls beyond budget + relaxation allowed ⇒ KeepNullable(RelaxedUnderEvidence)`` () =
    let attr = mkMandatoryAttr "OS_ATTR_M_Relaxed" true
    let profile = { Profile.empty with Columns = [ mkColProfile attr.SsKey 100L 12L ] }
    // AllowMandatoryRelaxation = true.
    let cfg = mkConfig 0.05m true []
    let decision = NullabilityRules.evaluate "test" cfg attr profile
    Assert.Equal(
        NullabilityOutcome.KeepNullable
            (RelaxedUnderEvidence (12L, 100L, 0.05m)),
        decision.Outcome)

[<Fact>]
let ``mandatory: PK takes precedence over IsMandatory`` () =
    // A PK column is structurally NOT NULL regardless of IsMandatory.
    let attr =
        { mkMandatoryAttr "OS_ATTR_M_AsPk" false with
            IsPrimaryKey = true }
    let cfg = mkConfig 0.0m false []
    let decision = NullabilityRules.evaluate "test" cfg attr Profile.empty
    Assert.Equal(NullabilityOutcome.EnforceNotNull PrimaryKey, decision.Outcome)

[<Fact>]
let ``mandatory: PhysicallyNotNull takes precedence over IsMandatory`` () =
    let attr = mkMandatoryAttr "OS_ATTR_M_PhysNotNull" false
    let cfg = mkConfig 0.0m false []
    let decision = NullabilityRules.evaluate "test" cfg attr Profile.empty
    Assert.Equal(NullabilityOutcome.EnforceNotNull PhysicallyNotNull, decision.Outcome)

[<Fact>]
let ``mandatory: override takes precedence over IsMandatory`` () =
    let attr = mkMandatoryAttr "OS_ATTR_M_Override" true
    let cfg =
        mkConfig 0.0m false
            [ { AttributeKey = attr.SsKey; Action = OverrideAction.KeepNullable } ]
    let decision = NullabilityRules.evaluate "test" cfg attr Profile.empty
    Assert.Equal(NullabilityOutcome.KeepNullable OperatorOverride, decision.Outcome)

[<Fact>]
let ``mandatory: requiresApproval helper fires for the conflict outcome`` () =
    let attr = mkMandatoryAttr "OS_ATTR_M_Conflict" true
    let profile = { Profile.empty with Columns = [ mkColProfile attr.SsKey 100L 12L ] }
    let cfg = mkConfig 0.05m false []
    let decision = NullabilityRules.evaluate "test" cfg attr profile
    Assert.True(NullabilityRules.requiresApproval decision)
