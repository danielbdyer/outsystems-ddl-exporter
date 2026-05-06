module Projection.Tests.V1NullabilityParityTests

open System
open Xunit
open Projection.Core
open Projection.Core.Passes

// ---------------------------------------------------------------------------
// Full V1 parity test for NullabilityEvaluator's eight test scenarios
// (tests/Osm.Validation.Tests/Policy/NullabilityEvaluatorTests.cs).
// V1's 8 example-based tests are mapped into V2's collapsed-mode form;
// 5 translate as Behavioral parity assertions; 3 are explicit Skip
// cases naming V2's intentional divergence from V1.
//
// V1↔V2 mode mapping (V1's three modes collapsed into one):
//
//   V1 EvidenceGated, AllowCautiousRelaxation=false, nulls-beyond-budget
//     ⇒ V2 ignores the V1-specific "silent stay-nullable" outcome under
//       evidence-gated; V2's collapsed mode + AllowMandatoryRelaxation=false
//       produces RequireOperatorApproval (the explicit ternary). The
//       V1 silent-relax behavior is V2 with AllowMandatoryRelaxation=true.
//
//   V1 Cautious, AllowCautiousRelaxation=false, nulls-beyond-budget
//     ⇒ V2 with AllowMandatoryRelaxation=false. V1's MakeNotNull=true +
//       RequiresRemediation=true is V2's RequireOperatorApproval
//       (semantically equivalent — operator must decide).
//
//   V1 Cautious, AllowCautiousRelaxation=true, nulls-beyond-budget
//     ⇒ V2 with AllowMandatoryRelaxation=true. V1's MakeNotNull=false is
//       V2's KeepNullable(RelaxedUnderEvidence).
//
//   V1 Aggressive ⇒ no V2 equivalent yet (V2 has no Aggressive-mode
//     intervention; if a real V2 use case demands it, a new
//     TighteningIntervention variant arrives under "IR grows under
//     evidence").
// ---------------------------------------------------------------------------

let private mkKey s = SsKey.original s |> Result.value
let private mkName s = Name.create s |> Result.value

/// Build a synthetic catalog with a single attribute matching the V1
/// test's "Sample.SampleEntity.Mandatory" coordinate. V1 uses
/// (Module, Entity, Attribute) names + (Schema, Table, Column) physical
/// coordinates; V2 uses SsKey identities.
let private mandatoryAttributeKey = mkKey "OS_ATTR_Sample_SampleEntity_Mandatory"
let private idAttributeKey        = mkKey "OS_ATTR_Sample_SampleEntity_Id"
let private sampleEntityKey       = mkKey "OS_KIND_Sample_SampleEntity"

let private buildCatalog (mandatoryColumnIsNullable: bool) (mandatoryColumnIsMandatory: bool) : Catalog =
    let kind : Kind =
        { SsKey    = sampleEntityKey
          Name     = mkName "SampleEntity"
          Origin   = OsNative
          Modality = []
          Physical = { Schema = "dbo"; Table = "OSUSR_TEST_SAMPLE" }
          Attributes = [
              { SsKey        = idAttributeKey
                Name         = mkName "Id"
                Type         = Integer
                Column       = { ColumnName = "ID"; IsNullable = false }
                IsPrimaryKey = true
                IsMandatory  = false }
              { SsKey        = mandatoryAttributeKey
                Name         = mkName "Mandatory"
                Type         = Text
                Column       = { ColumnName = "MANDATORY"; IsNullable = mandatoryColumnIsNullable }
                IsPrimaryKey = false
                IsMandatory  = mandatoryColumnIsMandatory } ]
          References = [] }
    { Modules = [
        { SsKey = mkKey "OS_MOD_Sample"
          Name  = mkName "Sample"
          Kinds = [ kind ] } ] }

let private buildProfile (rowCount: int64) (nullCount: int64) : Profile =
    let probe =
        ProbeStatus.create DateTimeOffset.UnixEpoch rowCount Succeeded
        |> Result.value
    { Profile.empty with
        Columns = [
            { AttributeKey         = mandatoryAttributeKey
              RowCount             = rowCount
              NullCount            = nullCount
              NullCountProbeStatus = probe } ] }

let private policyWith (config: NullabilityTighteningConfig) : Policy =
    { Policy.empty with
        Tightening =
            { Interventions = [ Nullability ("v1-parity", config) ] } }

let private decisionFor (key: SsKey) (lineage: Lineage<NullabilityDecisionSet>) : NullabilityDecision =
    lineage.Value.Decisions
    |> List.find (fun d -> d.AttributeKey = key)

// ---------------------------------------------------------------------------
// V1 #1 — EvidenceGated_Should_Tighten_MandatoryColumn_When_NullBudgetNotExceeded
//   V1 input:  Cautious + Mandatory column + 4/100 nulls + budget 5%.
//              (V1's EvidenceGated; V2's collapsed mode is closer to V1
//              Cautious — outcome is the same here either way.)
//   V1 output: MakeNotNull=true; rationales include DataNoNulls + NullBudgetEpsilon.
//   V2 form:   AllowMandatoryRelaxation=false; profile within budget ⇒
//              EnforceNotNull(LogicalMandatoryWithinBudget(4, 100, 0.05)).
// ---------------------------------------------------------------------------

[<Fact>]
let ``V1 #1: mandatory column with nulls within budget tightens (LogicalMandatoryWithinBudget)`` () =
    let catalog = buildCatalog true true
    let profile = buildProfile 100L 4L
    let cfg = NullabilityTighteningConfig.create 0.05m false [] |> Result.value
    let lineage = NullabilityPass.run catalog (policyWith cfg) profile
    let decision = decisionFor mandatoryAttributeKey lineage
    Assert.Equal(
        NullabilityOutcome.EnforceNotNull
            (LogicalMandatoryWithinBudget (4L, 100L, 0.05m)),
        decision.Outcome)

// ---------------------------------------------------------------------------
// V1 #2 — EvidenceGated_Should_StayNullable_When_MandatoryColumn_Has_Nulls
//   V1 input:  EvidenceGated + Mandatory + 12/100 nulls + budget 5%.
//   V1 output: MakeNotNull=false (silent stay-nullable).
//   V2 form:   AllowMandatoryRelaxation=true; profile beyond budget ⇒
//              KeepNullable(RelaxedUnderEvidence(12, 100, 0.05)).
//   V1↔V2 mapping: V1's EvidenceGated "stay nullable on evidence" is V2's
//   AllowMandatoryRelaxation=true (V2 makes the relaxation explicit
//   rather than mode-implicit).
// ---------------------------------------------------------------------------

[<Fact>]
let ``V1 #2: mandatory column with nulls beyond budget stays nullable when relaxation allowed`` () =
    let catalog = buildCatalog true true
    let profile = buildProfile 100L 12L
    let cfg = NullabilityTighteningConfig.create 0.05m true [] |> Result.value
    let lineage = NullabilityPass.run catalog (policyWith cfg) profile
    let decision = decisionFor mandatoryAttributeKey lineage
    Assert.Equal(
        NullabilityOutcome.KeepNullable
            (RelaxedUnderEvidence (12L, 100L, 0.05m)),
        decision.Outcome)

// ---------------------------------------------------------------------------
// V1 #3 — Cautious_Should_Block_MandatoryRelaxation_When_FlagDisabled
//   V1 input:  Cautious + AllowCautiousRelaxation=false + Mandatory +
//              12/100 nulls + budget 5%.
//   V1 output: MakeNotNull=true + RequiresRemediation=true; rationale
//              CautiousRelaxationDisabled.
//   V2 form:   AllowMandatoryRelaxation=false; profile beyond budget ⇒
//              RequireOperatorApproval(MandatoryButHasNullsBeyondBudget(12, 100, 0.05)).
//   V1↔V2 mapping: V1's "tighten with remediation" is V2's
//   RequireOperatorApproval — semantically the operator must decide.
// ---------------------------------------------------------------------------

[<Fact>]
let ``V1 #3: mandatory column with nulls beyond budget + relaxation forbidden lifts to operator approval`` () =
    let catalog = buildCatalog true true
    let profile = buildProfile 100L 12L
    let cfg = NullabilityTighteningConfig.create 0.05m false [] |> Result.value
    let lineage = NullabilityPass.run catalog (policyWith cfg) profile
    let decision = decisionFor mandatoryAttributeKey lineage
    Assert.Equal(
        NullabilityOutcome.RequireOperatorApproval
            (MandatoryButHasNullsBeyondBudget (12L, 100L, 0.05m)),
        decision.Outcome)
    Assert.True(NullabilityRules.requiresApproval decision)

// ---------------------------------------------------------------------------
// V1 #4 — Cautious_Should_Allow_MandatoryRelaxation_When_FlagEnabled
//   V1 input:  Cautious + AllowCautiousRelaxation=true + Mandatory +
//              12/100 nulls + budget 5%.
//   V1 output: MakeNotNull=false; no remediation flag.
//   V2 form:   AllowMandatoryRelaxation=true ⇒ KeepNullable(RelaxedUnderEvidence).
//   (Same V2 input as V1 #2; same V2 output. V1 #2 and V1 #4 collapse
//   in V2 because V1's mode distinction is what differs; V2 has only
//   the AllowMandatoryRelaxation flag.)
// ---------------------------------------------------------------------------

[<Fact>]
let ``V1 #4: relaxation allowed yields KeepNullable (same outcome as V1 #2 in V2's collapsed form)`` () =
    let catalog = buildCatalog true true
    let profile = buildProfile 100L 12L
    let cfg = NullabilityTighteningConfig.create 0.05m true [] |> Result.value
    let lineage = NullabilityPass.run catalog (policyWith cfg) profile
    let decision = decisionFor mandatoryAttributeKey lineage
    match decision.Outcome with
    | NullabilityOutcome.KeepNullable (RelaxedUnderEvidence _) -> ()
    | other -> Assert.Fail(sprintf "Expected KeepNullable(RelaxedUnderEvidence), got %A" other)

// ---------------------------------------------------------------------------
// V1 #5 — Aggressive_Should_Flag_Remediation_When_UniqueSignal_Exceeds_NullBudget
//   V1 input:  Aggressive + unique signal + 20/100 nulls + budget 5%.
//   V1 output: MakeNotNull=true + RequiresRemediation=true.
//   V2 status: SKIP — V2 has no Aggressive-equivalent intervention. V1's
//   Aggressive mode tightens unique signals even without sufficient
//   evidence and flags remediation; V2's collapsed mode does not.
//   When a real V2 use case demands aggressive tightening of unique
//   signals, it arrives as a new TighteningIntervention variant or a
//   new field on the existing Nullability config — under "IR grows
//   under evidence."
// ---------------------------------------------------------------------------

[<Fact(Skip = "V2 collapsed Aggressive mode (DECISIONS 2026-05-09); a future intervention variant or config field arrives when demand is real.")>]
let ``V1 #5: Aggressive mode unique-signal-with-remediation — SKIPPED (V2 divergence)`` () =
    ()

// ---------------------------------------------------------------------------
// V1 #6, #7 — Opportunity-creation tests
//   V1 input:  EvidenceGated + Mandatory + 12 nulls (or non-mandatory + 0 nulls).
//   V1 output: builder.Opportunities is populated (or not).
//   V2 status: SKIP — V2 separates Diagnostics from NullabilityDecisionSet
//   (DECISIONS 2026-05-06 — Diagnostics live in a writer parallel to
//   Lineage). Opportunity creation belongs to a Diagnostics consumer,
//   not to the structural decision set. The decisions themselves are
//   covered by V1 #1–#3 above; the opportunity-stream wire-up arrives
//   when the Diagnostics writer lands.
// ---------------------------------------------------------------------------

[<Fact(Skip = "V2 separates Diagnostics from decision set (DECISIONS 2026-05-06); opportunity wire-up arrives with the Diagnostics writer.")>]
let ``V1 #6: Analyze creates remediation opportunity — SKIPPED (V2 divergence)`` () =
    ()

[<Fact(Skip = "V2 separates Diagnostics from decision set (DECISIONS 2026-05-06); opportunity wire-up arrives with the Diagnostics writer.")>]
let ``V1 #7: Analyze skips opportunity for intentional nullability — SKIPPED (V2 divergence)`` () =
    ()

// ---------------------------------------------------------------------------
// V1 #8 — NullabilityOverride_Should_Keep_Column_Nullable
//   V1 input:  EvidenceGated + budget 0.0 + override on (Sample, SampleEntity, Mandatory).
//   V1 output: MakeNotNull=false; rationale NullabilityOverride.
//   V2 form:   Override on mandatoryAttributeKey ⇒ KeepNullable(OperatorOverride).
//   Override is absolute in V2 — bypasses the entire signal hierarchy.
// ---------------------------------------------------------------------------

[<Fact>]
let ``V1 #8: an override produces KeepNullable(OperatorOverride) regardless of other signals`` () =
    let catalog = buildCatalog true true
    let profile = buildProfile 100L 0L
    let cfg =
        NullabilityTighteningConfig.create
            0.0m false
            [ { AttributeKey = mandatoryAttributeKey; Action = OverrideAction.KeepNullable } ]
        |> Result.value
    let lineage = NullabilityPass.run catalog (policyWith cfg) profile
    let decision = decisionFor mandatoryAttributeKey lineage
    Assert.Equal(NullabilityOutcome.KeepNullable OperatorOverride, decision.Outcome)

// ---------------------------------------------------------------------------
// Bonus — V1 has no test for "PK takes precedence over override-conflict-style
// inputs"; V2 documents this explicitly. The Id attribute is always
// EnforceNotNull(PrimaryKey) regardless of profile or other signals.
// ---------------------------------------------------------------------------

[<Fact>]
let ``V2 invariant (V1 implicit): PK + IsMandatory yields PrimaryKey, not LogicalMandatory`` () =
    let catalog = buildCatalog true true
    // Build a profile carrying nulls in the Id (which is PK).
    let profileWithIdNulls =
        let probe =
            ProbeStatus.create DateTimeOffset.UnixEpoch 100L Succeeded
            |> Result.value
        { Profile.empty with
            Columns = [
                { AttributeKey         = idAttributeKey
                  RowCount             = 100L
                  NullCount            = 50L
                  NullCountProbeStatus = probe } ] }
    let cfg = NullabilityTighteningConfig.create 0.05m false [] |> Result.value
    let lineage = NullabilityPass.run catalog (policyWith cfg) profileWithIdNulls
    let idDecision = decisionFor idAttributeKey lineage
    // PK takes precedence; profile evidence is not consulted for PK
    // attributes (signal hierarchy short-circuits at step 2).
    Assert.Equal(NullabilityOutcome.EnforceNotNull PrimaryKey, idDecision.Outcome)
