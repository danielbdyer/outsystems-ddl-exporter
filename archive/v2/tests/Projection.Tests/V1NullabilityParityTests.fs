module Projection.Tests.V1NullabilityParityTests

open System
open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Tests.Fixtures

// Chapter A.4.7' slice η — `NullabilityPass.run` is private; the
// canonical surface is `.registered.Run`. Shape-compatible — both
// return `Lineage<Diagnostics<NullabilityDecisionSet>>`; this shim is
// a pure rename so existing assertions keep reading.
let private nullRun (catalog: Catalog) (policy: Policy) (profile: Profile) : Lineage<Diagnostics<NullabilityDecisionSet>> =
    (NullabilityPass.registered policy profile).Run catalog

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

let private mkKey s = testKey s

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
          Origin   = Native
          Modality = []
          Physical = (TableId.create "dbo" "OSUSR_TEST_SAMPLE" |> Result.value)
          Attributes = [
              { Attribute.create idAttributeKey (mkName "Id") Integer with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true }
              { Attribute.create mandatoryAttributeKey (mkName "Mandatory") Text with Column = ColumnRealization.create ("MANDATORY") (mandatoryColumnIsNullable) |> Result.value; IsMandatory = mandatoryColumnIsMandatory } ]
          References = []; Indexes = []; Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }
    { Modules = [
        { SsKey = mkKey "OS_MOD_Sample"
          Name  = mkName "Sample"
          Kinds = [ kind ]; IsActive = true; ExtendedProperties = [] } ]; Sequences = [] }

let private buildProfile (rowCount: int64) (nullCount: int64) : Profile =
    let probe =
        ProbeStatus.create DateTimeOffset.UnixEpoch rowCount Succeeded
        |> Result.value
    { Profile.empty with
        Columns = [
            { AttributeKey         = mandatoryAttributeKey
              RowCount             = rowCount
              NullCount            = nullCount
              MaxObservedLength = None
              NullCountProbeStatus = probe } ] }

let private policyWith (config: NullabilityTighteningConfig) : Policy =
    { Policy.empty with
        Tightening =
            { Interventions = [ Nullability ("v1-parity", config) ] } }

let private decisionFor (key: SsKey) (lineage: Lineage<Diagnostics<NullabilityDecisionSet>>) : NullabilityDecision =
    (LineageDiagnostics.payload lineage).Decisions
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
    let lineage = nullRun catalog (policyWith cfg) profile
    let decision = decisionFor mandatoryAttributeKey lineage
    Assert.Equal(
        NullabilityOutcome.EnforceNotNull
            (LogicalMandatoryWithinBudget (4L, 100L, 0.05m)),
        decision.Outcome)

[<Fact>]
let ``never silently tighten: a within-budget tightening emits a Warning diagnostic (operator 2026-07-19)`` () =
    // Same scenario as V1 #1 — a mandatory column tightened over 4/100 nulls,
    // kept under the 5% budget. It USED to tighten silently (EnforceNotNull ->
    // None); now the tightening is surfaced (never silently tighten).
    let catalog = buildCatalog true true
    let profile = buildProfile 100L 4L
    let cfg = NullabilityTighteningConfig.create 0.05m false [] |> Result.value
    let lineage = nullRun catalog (policyWith cfg) profile
    let tighten =
        LineageDiagnostics.entries lineage
        |> Seq.filter (fun e -> e.Code = "tightening.nullability.tightenedWithinBudget")
        |> Seq.toList
    Assert.Single(tighten) |> ignore
    let entry = tighten.[0]
    Assert.Equal(DiagnosticSeverity.Warning, entry.Severity)
    Assert.Equal(Some mandatoryAttributeKey, entry.SsKey)
    Assert.Contains("4/100", entry.Message)

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
    let lineage = nullRun catalog (policyWith cfg) profile
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
    let lineage = nullRun catalog (policyWith cfg) profile
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
    let lineage = nullRun catalog (policyWith cfg) profile
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

// V1 #5 (Aggressive-mode unique-signal-with-remediation) Skip
// stub retired per the user's chapter-3.5 directive (2026-05-09:
// "we don't need them"). V2 collapsed Aggressive mode per
// `DECISIONS 2026-05-09`; if a future intervention variant or
// config field re-introduces the concept, a fresh test lands
// structurally with the implementation.

// ---------------------------------------------------------------------------
// V1 #6, #7 — Opportunity-creation tests (activated session 15)
//   V1 #6 input:  EvidenceGated + Mandatory + 12/100 nulls + budget 5%
//                 + AllowMandatoryRelaxation=false.
//   V1 #6 output: builder.Opportunities is populated (one
//                 remediation opportunity for the mandatory column).
//   V2 #6 form:   RequireOperatorApproval(MandatoryButHasNullsBeyondBudget)
//                 produces a DiagnosticSeverity.Warning DiagnosticEntry on the
//                 NullabilityPass.run output's diagnostic stream.
//                 V1's opportunity record's structural payload
//                 maps to the entry's Source / Severity / Code /
//                 Message / SsKey / Metadata.
//
//   V1 #7 input:  Non-mandatory column + 0 nulls.
//   V1 #7 output: builder.Opportunities is empty (intentional
//                 nullability — no opportunity).
//   V2 #7 form:   KeepNullable(NoTighteningSignal) produces no
//                 DiagnosticEntry. The diagnostic stream is empty
//                 with respect to that attribute.
//
//   Activation per DECISIONS 2026-05-10 (Skip-to-Behavioral
//   activation pattern) and DECISIONS 2026-05-13 (pass return-type
//   codification). The Diagnostics writer landed at session 14
//   commit 3; UniqueIndexPass activated as first consumer at
//   session 14 commit 5; NullabilityPass activated as second
//   consumer at session 15 commits 2-3.
// ---------------------------------------------------------------------------

[<Fact>]
let ``V1 #6: Analyze creates remediation opportunity — Diagnostics stream emits one DiagnosticSeverity.Warning entry`` () =
    let catalog = buildCatalog true true            // mandatory column
    let profile = buildProfile 100L 12L             // 12 nulls in 100 rows
    let cfg = NullabilityTighteningConfig.create 0.05m false [] |> Result.value
    let lineage = nullRun catalog (policyWith cfg) profile

    // Decision side: mandatory column with nulls beyond budget +
    // relaxation forbidden ⇒ RequireOperatorApproval (V1 #3 already
    // covers the decision; this test asserts the diagnostic
    // emission V1 #6 was the V1 representative for).
    let decision = decisionFor mandatoryAttributeKey lineage
    match decision.Outcome with
    | NullabilityOutcome.RequireOperatorApproval _ -> ()
    | other -> Assert.Fail(sprintf "Expected RequireOperatorApproval, got %A" other)

    // Diagnostic side: exactly one DiagnosticSeverity.Warning entry, tagged with the
    // mandatory column's SsKey and a tightening.nullability.* code.
    let entries = LineageDiagnostics.entries lineage
    Assert.Single(entries) |> ignore
    let entry = entries.[0]
    Assert.Equal(DiagnosticSeverity.Warning, entry.Severity)
    Assert.Equal("nullability", entry.Source)
    Assert.Equal("tightening.nullability.requireOperatorApproval", entry.Code)
    Assert.Equal(Some mandatoryAttributeKey, entry.SsKey)
    Assert.True(entry.Metadata.ContainsKey "interventionId")
    Assert.Equal("v1-parity", entry.Metadata.["interventionId"])
    Assert.False(System.String.IsNullOrWhiteSpace entry.Message)

[<Fact>]
let ``V1 #7: Analyze skips opportunity for intentional nullability — Diagnostics stream emits no entry for that attribute`` () =
    // Non-mandatory column + nullable + no nulls. V1's
    // OpportunityBuilder produces no opportunity for intentional
    // nullability; V2's mapping: KeepNullable(NoTighteningSignal)
    // produces no DiagnosticEntry.
    let catalog = buildCatalog true false           // non-mandatory column
    let profile = buildProfile 100L 0L              // zero nulls
    let cfg = NullabilityTighteningConfig.create 0.05m false [] |> Result.value
    let lineage = nullRun catalog (policyWith cfg) profile

    let decision = decisionFor mandatoryAttributeKey lineage
    match decision.Outcome with
    | NullabilityOutcome.KeepNullable NoTighteningSignal -> ()
    | other -> Assert.Fail(sprintf "Expected KeepNullable(NoTighteningSignal), got %A" other)

    // The mandatory-named-but-actually-non-mandatory column is the
    // intentional-nullability case; no diagnostic entry references
    // it. The PK column also produces no diagnostic (EnforceNotNull
    // outcome → None per the opportunityEntry mapping).
    let entries = LineageDiagnostics.entries lineage
    Assert.All(entries, fun e ->
        Assert.NotEqual(Some mandatoryAttributeKey, e.SsKey))

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
    let lineage = nullRun catalog (policyWith cfg) profile
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
                  MaxObservedLength = None
                  NullCountProbeStatus = probe } ] }
    let cfg = NullabilityTighteningConfig.create 0.05m false [] |> Result.value
    let lineage = nullRun catalog (policyWith cfg) profileWithIdNulls
    let idDecision = decisionFor idAttributeKey lineage
    // PK takes precedence; profile evidence is not consulted for PK
    // attributes (signal hierarchy short-circuits at step 2).
    Assert.Equal(NullabilityOutcome.EnforceNotNull NullabilityEvidence.PrimaryKey, idDecision.Outcome)
