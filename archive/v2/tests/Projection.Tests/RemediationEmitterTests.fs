module Projection.Tests.RemediationEmitterTests

open Xunit
open Projection.Core
open Projection.Targets.OperationalDiagnostics
open Projection.Tests.Fixtures

/// Chapter 5+ slice `5.13.remediation-emitter` coverage. Exercises
/// `RemediationEmitter.emit` against `sampleCatalog` decision-set
/// fixtures. The emitter projects only operator-attention findings
/// (NullabilityConflict + FK orphans + UniqueIndex duplicates); other
/// outcome variants don't surface in the output.

let private emptyNullability : NullabilityDecisionSet = { Decisions = [] }
let private emptyUniqueIndex : UniqueIndexDecisionSet  = { Decisions = [] }
let private emptyForeignKey  : ForeignKeyDecisionSet   = { Decisions = [] }

let private nullabilityConflict (attrKey: SsKey) (interventionId: string) : NullabilityDecision = {
    AttributeKey   = attrKey
    Outcome        =
        NullabilityOutcome.RequireOperatorApproval
            (MandatoryButHasNullsBeyondBudget (42L, 1000L, 0.01m))
    InterventionId = interventionId
}

let private nullabilityEnforced (attrKey: SsKey) : NullabilityDecision = {
    AttributeKey   = attrKey
    Outcome        = NullabilityOutcome.EnforceNotNull NullabilityEvidence.PrimaryKey
    InterventionId = "primary-key"
}

let private fkOrphans (refKey: SsKey) (orphanCount: int64) : ForeignKeyDecision = {
    ReferenceKey   = refKey
    Outcome        = ForeignKeyOutcome.DoNotEnforce (DataHasOrphans orphanCount)
    InterventionId = "fk-orphans"
}

let private fkEnforced (refKey: SsKey) : ForeignKeyDecision = {
    ReferenceKey   = refKey
    Outcome        =
        ForeignKeyOutcome.EnforceConstraint
            ForeignKeyEvidence.DatabaseConstraintPresent
    InterventionId = "fk-present"
}

// ----------------------------------------------------------------------
// Empty / identity path
// ----------------------------------------------------------------------

[<Fact>]
let ``5.13.remediation: empty decision sets produce no-findings header only`` () =
    let sql =
        RemediationEmitter.emit
            sampleCatalog
            emptyNullability
            emptyUniqueIndex
            emptyForeignKey
    Assert.Contains("No remediation candidates surfaced", sql)
    // No per-finding remediation block — no SELECT or UPDATE / DELETE
    // operator-actionable statements appear (the file-level intro
    // comment naming the OPTION-1/2/3 convention is preserved).
    Assert.DoesNotContain("SELECT * FROM", sql)
    Assert.DoesNotContain("UPDATE [dbo]", sql)
    Assert.DoesNotContain("DELETE FROM", sql)

// ----------------------------------------------------------------------
// Nullability conflict — produces SELECT + commented UPDATE + DELETE
// ----------------------------------------------------------------------

[<Fact>]
let ``5.13.remediation: nullability conflict emits 3 options for offending attribute`` () =
    let nullability : NullabilityDecisionSet = { Decisions = [ nullabilityConflict customerNameKey "nullability-budget" ] }
    let sql =
        RemediationEmitter.emit sampleCatalog nullability emptyUniqueIndex emptyForeignKey
    Assert.Contains("SELECT * FROM [dbo].[OSUSR_S1S_CUSTOMER] WHERE [NAME] IS NULL;", sql)
    Assert.Contains("-- UPDATE [dbo].[OSUSR_S1S_CUSTOMER] SET [NAME] = <DEFAULT> WHERE [NAME] IS NULL;", sql)
    Assert.Contains("-- DELETE FROM [dbo].[OSUSR_S1S_CUSTOMER] WHERE [NAME] IS NULL;", sql)
    Assert.Contains("Reason: Mandatory column has 42 null(s)", sql)
    Assert.Contains("intervention: nullability-budget", sql)

[<Fact>]
let ``5.13.remediation: nullability EnforceNotNull does not produce remediation`` () =
    let nullability : NullabilityDecisionSet = { Decisions = [ nullabilityEnforced customerNameKey ] }
    let sql =
        RemediationEmitter.emit sampleCatalog nullability emptyUniqueIndex emptyForeignKey
    Assert.Contains("No remediation candidates surfaced", sql)

// ----------------------------------------------------------------------
// FK orphans
// ----------------------------------------------------------------------

[<Fact>]
let ``5.13.remediation: FK orphan finding emits SELECT NOT IN + commented UPDATE + DELETE`` () =
    let fk : ForeignKeyDecisionSet = { Decisions = [ fkOrphans orderRefToCustomer 7L ] }
    let sql =
        RemediationEmitter.emit sampleCatalog emptyNullability emptyUniqueIndex fk
    Assert.Contains("SELECT * FROM [dbo].[OSUSR_S1S_ORDER] WHERE [CUSTOMER_ID] IS NOT NULL;", sql)
    Assert.Contains("Reason: Reference has 7 orphan row(s)", sql)

[<Fact>]
let ``5.13.remediation: FK EnforceConstraint does not produce remediation`` () =
    let fk : ForeignKeyDecisionSet = { Decisions = [ fkEnforced orderRefToCustomer ] }
    let sql =
        RemediationEmitter.emit sampleCatalog emptyNullability emptyUniqueIndex fk
    Assert.Contains("No remediation candidates surfaced", sql)

// ----------------------------------------------------------------------
// Operator-safety: UPDATE + DELETE commented out by default
// ----------------------------------------------------------------------

[<Fact>]
let ``5.13.remediation: UPDATE statement is commented-out (operator confirms before uncommenting)`` () =
    let nullability : NullabilityDecisionSet = { Decisions = [ nullabilityConflict customerNameKey "intv" ] }
    let sql =
        RemediationEmitter.emit sampleCatalog nullability emptyUniqueIndex emptyForeignKey
    // The UPDATE line starts with "-- " (commented out).
    let lines = sql.Split([| '\n' |])
    let updateLines =
        lines
        |> Array.filter (fun l -> l.Contains "UPDATE [dbo]")
    Assert.NotEmpty(updateLines)
    Assert.All(updateLines, fun l -> Assert.StartsWith("-- ", l.TrimStart()))

[<Fact>]
let ``5.13.remediation: DELETE statement is commented-out (operator confirms before uncommenting)`` () =
    let nullability : NullabilityDecisionSet = { Decisions = [ nullabilityConflict customerNameKey "intv" ] }
    let sql =
        RemediationEmitter.emit sampleCatalog nullability emptyUniqueIndex emptyForeignKey
    let lines = sql.Split([| '\n' |])
    let deleteLines =
        lines
        |> Array.filter (fun l -> l.Contains "DELETE FROM [dbo]")
    Assert.NotEmpty(deleteLines)
    Assert.All(deleteLines, fun l -> Assert.StartsWith("-- ", l.TrimStart()))

[<Fact>]
let ``5.13.remediation: SELECT statement is NOT commented-out (active by default)`` () =
    let nullability : NullabilityDecisionSet = { Decisions = [ nullabilityConflict customerNameKey "intv" ] }
    let sql =
        RemediationEmitter.emit sampleCatalog nullability emptyUniqueIndex emptyForeignKey
    let lines = sql.Split([| '\n' |])
    let activeSelectLines =
        lines
        |> Array.filter (fun l ->
            let trimmed = l.TrimStart()
            trimmed.StartsWith "SELECT * FROM [dbo]" || trimmed.StartsWith "SELECT [")
    Assert.NotEmpty(activeSelectLines)

// ----------------------------------------------------------------------
// Multi-finding aggregation
// ----------------------------------------------------------------------

[<Fact>]
let ``5.13.remediation: multiple findings across axes all surface in single artifact`` () =
    let nullability : NullabilityDecisionSet = { Decisions = [ nullabilityConflict customerNameKey "intv1" ] }
    let fk          : ForeignKeyDecisionSet  = { Decisions = [ fkOrphans orderRefToCustomer 3L ] }
    let sql =
        RemediationEmitter.emit sampleCatalog nullability emptyUniqueIndex fk
    Assert.Contains("[NAME] IS NULL", sql)
    Assert.Contains("[CUSTOMER_ID]", sql)
    // Both intervention IDs appear.
    Assert.Contains("intervention: intv1", sql)
    Assert.Contains("intervention: fk-orphans", sql)

// ----------------------------------------------------------------------
// Deterministic ordering — same inputs produce byte-identical output
// ----------------------------------------------------------------------

[<Fact>]
let ``5.13.remediation: deterministic — repeated emit yields byte-identical output`` () =
    let nullability : NullabilityDecisionSet = { Decisions = [ nullabilityConflict customerNameKey "intv1" ] }
    let fk          : ForeignKeyDecisionSet  = { Decisions = [ fkOrphans orderRefToCustomer 3L ] }
    let sql1 =
        RemediationEmitter.emit sampleCatalog nullability emptyUniqueIndex fk
    let sql2 =
        RemediationEmitter.emit sampleCatalog nullability emptyUniqueIndex fk
    Assert.Equal(sql1, sql2)

// ----------------------------------------------------------------------
// Data reality (2026-07-06) — the fidelity report's violations render as
// remediation blocks, deduplicated against the decision blocks.
// ----------------------------------------------------------------------

let private finding (entity: string) (column: string) (kind: RemediationEmitter.DataRealityKind) : RemediationEmitter.DataRealityFinding =
    { Entity = entity; Column = column; Kind = kind }

[<Fact>]
let ``data reality: nulls in a NOT NULL column emit the 3 options with the observed count`` () =
    let sql =
        RemediationEmitter.emitWith sampleCatalog emptyNullability emptyUniqueIndex emptyForeignKey
            [ finding "Customer" "Name" (RemediationEmitter.NullsInNotNullColumn 5L) ]
    Assert.Contains("data reality: nulls in a NOT NULL column", sql)
    Assert.Contains("5 null value(s) observed", sql)
    Assert.Contains("SELECT * FROM [dbo].[OSUSR_S1S_CUSTOMER] WHERE [NAME] IS NULL;", sql)
    Assert.Contains("-- UPDATE [dbo].[OSUSR_S1S_CUSTOMER] SET [NAME] = <DEFAULT> WHERE [NAME] IS NULL;", sql)
    Assert.Contains("-- DELETE FROM [dbo].[OSUSR_S1S_CUSTOMER] WHERE [NAME] IS NULL;", sql)
    Assert.DoesNotContain("No remediation candidates surfaced", sql)

[<Fact>]
let ``data reality: an orphaned reference resolves the target table + PK to concrete SQL`` () =
    let sql =
        RemediationEmitter.emitWith sampleCatalog emptyNullability emptyUniqueIndex emptyForeignKey
            [ finding "Order" "CustomerId" (RemediationEmitter.OrphanedReference 7L) ]
    Assert.Contains("data reality: relationship values without a matching record", sql)
    Assert.Contains("7 source row(s) reference a target record that does not exist", sql)
    // The target resolves through the catalog — the concrete Customer PK, no placeholders.
    Assert.Contains("NOT IN (SELECT [ID] FROM [dbo].[OSUSR_S1S_CUSTOMER])", sql)
    Assert.DoesNotContain("<TargetTable>", sql)

[<Fact>]
let ``data reality: a length overflow emits the LEN locate + commented truncation`` () =
    let sql =
        RemediationEmitter.emitWith sampleCatalog emptyNullability emptyUniqueIndex emptyForeignKey
            [ finding "Customer" "Name" (RemediationEmitter.ValueOverflow ("300", "200")) ]
    Assert.Contains("data reality: values past the declared length", sql)
    Assert.Contains("SELECT * FROM [dbo].[OSUSR_S1S_CUSTOMER] WHERE LEN([NAME]) > 200;", sql)
    Assert.Contains("-- UPDATE [dbo].[OSUSR_S1S_CUSTOMER] SET [NAME] = LEFT([NAME], 200) WHERE LEN([NAME]) > 200;", sql)

[<Fact>]
let ``data reality: duplicates in a unique column emit the GROUP BY locate`` () =
    let sql =
        RemediationEmitter.emitWith sampleCatalog emptyNullability emptyUniqueIndex emptyForeignKey
            [ finding "Customer" "Name" RemediationEmitter.DuplicatesInUniqueColumn ]
    Assert.Contains("data reality: duplicates in a unique column", sql)
    Assert.Contains("SELECT [NAME], COUNT(*) AS RowCount FROM [dbo].[OSUSR_S1S_CUSTOMER] GROUP BY [NAME] HAVING COUNT(*) > 1;", sql)

[<Fact>]
let ``data reality: a finding covered by a decision block is deduplicated, never stated twice`` () =
    let fk : ForeignKeyDecisionSet = { Decisions = [ fkOrphans orderRefToCustomer 7L ] }
    let sql =
        RemediationEmitter.emitWith sampleCatalog emptyNullability emptyUniqueIndex fk
            [ finding "Order" "CustomerId" (RemediationEmitter.OrphanedReference 7L) ]
    // The decision block renders (with its intervention id); the fidelity
    // sibling on the same (entity, column, axis) is suppressed.
    Assert.Contains("intervention: fk-orphans", sql)
    Assert.DoesNotContain("data reality: relationship values without a matching record", sql)

[<Fact>]
let ``data reality: an unresolvable entity renders nothing and never throws`` () =
    let sql =
        RemediationEmitter.emitWith sampleCatalog emptyNullability emptyUniqueIndex emptyForeignKey
            [ finding "NoSuchEntity" "NoSuchColumn" (RemediationEmitter.NullsInNotNullColumn 1L) ]
    Assert.Contains("No remediation candidates surfaced", sql)

[<Fact>]
let ``data reality: destructive statements ship commented-out (the operator-safety contract holds)`` () =
    let sql =
        RemediationEmitter.emitWith sampleCatalog emptyNullability emptyUniqueIndex emptyForeignKey
            [ finding "Customer" "Name" (RemediationEmitter.NullsInNotNullColumn 5L)
              finding "Customer" "Name" (RemediationEmitter.ValueOverflow ("300", "200"))
              finding "Order" "CustomerId" (RemediationEmitter.OrphanedReference 7L) ]
    let lines = sql.Split([| '\n' |])
    let destructive =
        lines |> Array.filter (fun l -> l.Contains "UPDATE [dbo]" || l.Contains "DELETE FROM [dbo]")
    Assert.NotEmpty(destructive)
    Assert.All(destructive, fun l -> Assert.StartsWith("-- ", l.TrimStart()))
