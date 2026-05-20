module Projection.Tests.SummaryFormatterTests

open Xunit
open Projection.Core
open Projection.Targets.OperationalDiagnostics
open Projection.Tests.Fixtures

/// Chapter 5+ slice `5.13.summary-formatter` coverage. The formatter
/// classifies each decision into one of 6 buckets (PrimaryKey /
/// Physical / Mandatory / ForeignKey / Unique / Remediation) and
/// emits an operator-readable rollup.

let private emptyNullability : NullabilityDecisionSet = { Decisions = [] }
let private emptyUniqueIndex : UniqueIndexDecisionSet  = { Decisions = [] }
let private emptyForeignKey  : ForeignKeyDecisionSet   = { Decisions = [] }

let private nullabilityDecision (key: SsKey) (outcome: NullabilityOutcome) : NullabilityDecision = {
    AttributeKey   = key
    Outcome        = outcome
    InterventionId = "intv"
}

let private fkDecision (key: SsKey) (outcome: ForeignKeyOutcome) : ForeignKeyDecision = {
    ReferenceKey   = key
    Outcome        = outcome
    InterventionId = "intv"
}

let private uiDecision (key: SsKey) (outcome: UniqueIndexOutcome) : UniqueIndexDecision = {
    IndexKey       = key
    Outcome        = outcome
    InterventionId = "intv"
}

let private joined (lines: string list) : string = String.concat "\n" lines

// ----------------------------------------------------------------------
// Bucket classification — Nullability
// ----------------------------------------------------------------------

[<Fact>]
let ``5.13.summary: NullabilityOutcome.EnforceNotNull PrimaryKey classifies as PrimaryKey bucket`` () =
    let nullability : NullabilityDecisionSet = {
        Decisions = [
            nullabilityDecision customerIdAttrKey
                (NullabilityOutcome.EnforceNotNull NullabilityEvidence.PrimaryKey)
        ]
    }
    let text =
        SummaryFormatter.format nullability emptyUniqueIndex emptyForeignKey |> joined
    Assert.Contains("[PrimaryKey  ]    1 decision(s)", text)
    Assert.Contains("[Physical    ]    0 decision(s)", text)

[<Fact>]
let ``5.13.summary: PhysicallyNotNull classifies as Physical bucket`` () =
    let nullability : NullabilityDecisionSet = {
        Decisions = [
            nullabilityDecision customerIdAttrKey
                (NullabilityOutcome.EnforceNotNull NullabilityEvidence.PhysicallyNotNull)
        ]
    }
    let text =
        SummaryFormatter.format nullability emptyUniqueIndex emptyForeignKey |> joined
    Assert.Contains("[Physical    ]    1 decision(s)", text)

[<Fact>]
let ``5.13.summary: LogicalMandatory variants classify as Mandatory bucket`` () =
    let nullability : NullabilityDecisionSet = {
        Decisions = [
            nullabilityDecision customerIdAttrKey
                (NullabilityOutcome.EnforceNotNull NullabilityEvidence.LogicalMandatoryNoProfile)
            nullabilityDecision customerNameKey
                (NullabilityOutcome.EnforceNotNull (NullabilityEvidence.LogicalMandatoryNoNulls 1000L))
            nullabilityDecision customerTenantKey
                (NullabilityOutcome.EnforceNotNull
                    (NullabilityEvidence.LogicalMandatoryWithinBudget (1L, 1000L, 0.01m)))
        ]
    }
    let text =
        SummaryFormatter.format nullability emptyUniqueIndex emptyForeignKey |> joined
    Assert.Contains("[Mandatory   ]    3 decision(s)", text)

[<Fact>]
let ``5.13.summary: RequireOperatorApproval classifies as Remediation bucket`` () =
    let nullability : NullabilityDecisionSet = {
        Decisions = [
            nullabilityDecision customerNameKey
                (NullabilityOutcome.RequireOperatorApproval
                    (MandatoryButHasNullsBeyondBudget (5L, 100L, 0.01m)))
        ]
    }
    let text =
        SummaryFormatter.format nullability emptyUniqueIndex emptyForeignKey |> joined
    Assert.Contains("[Remediation ]    1 decision(s)", text)

[<Fact>]
let ``5.13.summary: KeepNullable RelaxedUnderEvidence classifies as Remediation bucket`` () =
    let nullability : NullabilityDecisionSet = {
        Decisions = [
            nullabilityDecision customerNameKey
                (NullabilityOutcome.KeepNullable (RelaxedUnderEvidence (5L, 100L, 0.01m)))
        ]
    }
    let text =
        SummaryFormatter.format nullability emptyUniqueIndex emptyForeignKey |> joined
    Assert.Contains("[Remediation ]    1 decision(s)", text)

[<Fact>]
let ``5.13.summary: KeepNullable for non-Remediation reasons does not classify`` () =
    let nullability : NullabilityDecisionSet = {
        Decisions = [
            nullabilityDecision customerNameKey
                (NullabilityOutcome.KeepNullable NoTighteningSignal)
            nullabilityDecision customerTenantKey
                (NullabilityOutcome.KeepNullable OperatorOverride)
        ]
    }
    let text =
        SummaryFormatter.format nullability emptyUniqueIndex emptyForeignKey |> joined
    // All buckets show 0 — these decisions don't map to any of the 6.
    Assert.Contains("[Remediation ]    0 decision(s)", text)
    Assert.Contains("[Mandatory   ]    0 decision(s)", text)

// ----------------------------------------------------------------------
// Bucket classification — ForeignKey + UniqueIndex
// ----------------------------------------------------------------------

[<Fact>]
let ``5.13.summary: ForeignKeyOutcome.EnforceConstraint classifies as ForeignKey bucket`` () =
    let fk : ForeignKeyDecisionSet = {
        Decisions = [
            fkDecision orderRefToCustomer
                (ForeignKeyOutcome.EnforceConstraint
                    ForeignKeyEvidence.DatabaseConstraintPresent)
        ]
    }
    let text =
        SummaryFormatter.format emptyNullability emptyUniqueIndex fk |> joined
    Assert.Contains("[ForeignKey  ]    1 decision(s)", text)

[<Fact>]
let ``5.13.summary: ForeignKeyOutcome.DoNotEnforce DataHasOrphans classifies as Remediation`` () =
    let fk : ForeignKeyDecisionSet = {
        Decisions = [
            fkDecision orderRefToCustomer
                (ForeignKeyOutcome.DoNotEnforce (DataHasOrphans 3L))
        ]
    }
    let text =
        SummaryFormatter.format emptyNullability emptyUniqueIndex fk |> joined
    Assert.Contains("[Remediation ]    1 decision(s)", text)

[<Fact>]
let ``5.13.summary: UniqueIndexOutcome.EnforceUnique classifies as Unique bucket`` () =
    let ui : UniqueIndexDecisionSet = {
        Decisions = [
            uiDecision customerIdAttrKey
                (UniqueIndexOutcome.EnforceUnique UniqueIndexEvidence.AlreadyUnique)
        ]
    }
    let text =
        SummaryFormatter.format emptyNullability ui emptyForeignKey |> joined
    Assert.Contains("[Unique      ]    1 decision(s)", text)

[<Fact>]
let ``5.13.summary: UniqueIndexOutcome.DoNotEnforce DataHasDuplicates classifies as Remediation`` () =
    let ui : UniqueIndexDecisionSet = {
        Decisions = [
            uiDecision customerIdAttrKey
                (UniqueIndexOutcome.DoNotEnforce DataHasDuplicates)
        ]
    }
    let text =
        SummaryFormatter.format emptyNullability ui emptyForeignKey |> joined
    Assert.Contains("[Remediation ]    1 decision(s)", text)

// ----------------------------------------------------------------------
// Multi-axis rollup + ordering invariant
// ----------------------------------------------------------------------

[<Fact>]
let ``5.13.summary: bucket order is PrimaryKey -> Physical -> Mandatory -> ForeignKey -> Unique -> Remediation`` () =
    let nullability : NullabilityDecisionSet = {
        Decisions = [
            nullabilityDecision customerIdAttrKey
                (NullabilityOutcome.EnforceNotNull NullabilityEvidence.PrimaryKey)
            nullabilityDecision customerNameKey
                (NullabilityOutcome.EnforceNotNull NullabilityEvidence.PhysicallyNotNull)
            nullabilityDecision customerTenantKey
                (NullabilityOutcome.EnforceNotNull NullabilityEvidence.LogicalMandatoryNoProfile)
        ]
    }
    let fk : ForeignKeyDecisionSet = {
        Decisions = [
            fkDecision orderRefToCustomer
                (ForeignKeyOutcome.EnforceConstraint
                    ForeignKeyEvidence.DatabaseConstraintPresent)
        ]
    }
    let text = SummaryFormatter.format nullability emptyUniqueIndex fk |> joined
    let pkIdx   = text.IndexOf("[PrimaryKey")
    let physIdx = text.IndexOf("[Physical")
    let mandIdx = text.IndexOf("[Mandatory")
    let fkIdx   = text.IndexOf("[ForeignKey")
    let uniqIdx = text.IndexOf("[Unique")
    let remIdx  = text.IndexOf("[Remediation")
    Assert.True(pkIdx < physIdx)
    Assert.True(physIdx < mandIdx)
    Assert.True(mandIdx < fkIdx)
    Assert.True(fkIdx < uniqIdx)
    Assert.True(uniqIdx < remIdx)

[<Fact>]
let ``5.13.summary: totals line aggregates structural-tightenings + remediation counts`` () =
    let nullability : NullabilityDecisionSet = {
        Decisions = [
            nullabilityDecision customerIdAttrKey
                (NullabilityOutcome.EnforceNotNull NullabilityEvidence.PrimaryKey)
            nullabilityDecision customerNameKey
                (NullabilityOutcome.RequireOperatorApproval
                    (MandatoryButHasNullsBeyondBudget (5L, 100L, 0.01m)))
        ]
    }
    let text = SummaryFormatter.format nullability emptyUniqueIndex emptyForeignKey |> joined
    Assert.Contains("Totals: 1 structural tightening(s); 1 remediation finding(s).", text)

// ----------------------------------------------------------------------
// Sample SsKey carriage (first 3 entries per bucket)
// ----------------------------------------------------------------------

[<Fact>]
let ``5.13.summary: sample lines show first 3 SsKeys per bucket`` () =
    let nullability : NullabilityDecisionSet = {
        Decisions = [
            nullabilityDecision customerIdAttrKey
                (NullabilityOutcome.EnforceNotNull NullabilityEvidence.PrimaryKey)
            nullabilityDecision customerNameKey
                (NullabilityOutcome.EnforceNotNull NullabilityEvidence.PrimaryKey)
            nullabilityDecision customerTenantKey
                (NullabilityOutcome.EnforceNotNull NullabilityEvidence.PrimaryKey)
            nullabilityDecision orderIdAttrKey
                (NullabilityOutcome.EnforceNotNull NullabilityEvidence.PrimaryKey)
        ]
    }
    let lines = SummaryFormatter.format nullability emptyUniqueIndex emptyForeignKey
    let sampleLines = lines |> List.filter (fun l -> l.Contains "sample:")
    // 3 samples surface (first 3 of 4 decisions).
    Assert.Equal(3, sampleLines.Length)

// ----------------------------------------------------------------------
// Deterministic — same inputs produce byte-identical output
// ----------------------------------------------------------------------

[<Fact>]
let ``5.13.summary: deterministic — repeated format yields byte-identical output`` () =
    let nullability : NullabilityDecisionSet = {
        Decisions = [
            nullabilityDecision customerIdAttrKey
                (NullabilityOutcome.EnforceNotNull NullabilityEvidence.PrimaryKey)
        ]
    }
    let t1 = SummaryFormatter.formatText nullability emptyUniqueIndex emptyForeignKey
    let t2 = SummaryFormatter.formatText nullability emptyUniqueIndex emptyForeignKey
    Assert.Equal(t1, t2)
