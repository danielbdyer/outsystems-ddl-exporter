module Projection.Tests.DiagnosticsEndToEndTests

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// End-to-end milestone — UniqueIndex with its opportunity stream
// flowing through Lineage<Diagnostics<_>> end-to-end.
//
// Demonstrates the dual writer's contract on a realistic catalog +
// policy + profile triple:
//   - decisions are produced and Annotated lineage events fire;
//   - DiagnosticEntry values are produced for every DoNotEnforce
//     decision (the V2-shaped equivalent of V1's
//     OpportunityBuilder.TryCreate);
//   - both trails are deterministic across repeated runs (T1
//     byte-determinism holds for the dual writer);
//   - the diagnostic stream and the decision stream agree by SsKey
//     (per-decision-equivalent emission).
// ---------------------------------------------------------------------------

let private ssKey (s: string) : SsKey = SsKey.original s |> Result.value
let private name  (s: string) : Name  = Name.create s   |> Result.value

let private mkIndex
    (key: string)
    (columns: SsKey list)
    (isUnique: bool)
    : Index =
    { SsKey        = ssKey key
      Name         = name "IX"
      Columns      = columns
      IsUnique     = isUnique
      IsPrimaryKey = false }

/// Catalog with one already-unique single-column index (Customer.Name)
/// plus three non-unique single-column indexes that will produce
/// DoNotEnforce decisions under a "single-column toggle off, composite
/// on" policy gate (the canonical PolicyDisabled scenario from
/// V1's UniqueIndexDecisionStrategyTests).
let private endToEndCatalog : Catalog =
    let customerSingleUnique =
        mkIndex "OS_IDX_Customer_Name_U" [ customerNameKey ] true
    let orderSingle =
        mkIndex "OS_IDX_Order_CustomerId" [ orderCustomerFkKey ] false
    let countrySingle =
        mkIndex "OS_IDX_Country_Code" [ countryCodeKey ] false
    let customer' = { customer with Indexes = [ customerSingleUnique ] }
    let order'    = { order    with Indexes = [ orderSingle ] }
    let country'  = { country  with Indexes = [ countrySingle ] }
    let salesModule' =
        { salesModule with Kinds = [ customer'; order'; country' ] }
    { Modules = [ salesModule' ] }

let private singleOffCompositeOnPolicy : Policy =
    let cfg = UniqueIndexTighteningConfig.create false true
    { Policy.empty with
        Tightening = { Interventions = [ UniqueIndex ("v1-style", cfg) ] } }

// ---------------------------------------------------------------------------
// Both trails populated end-to-end.
// ---------------------------------------------------------------------------

[<Fact>]
let ``end-to-end: lineage trail and diagnostic stream populate together`` () =
    let result = UniqueIndexPass.run endToEndCatalog singleOffCompositeOnPolicy Profile.empty

    // Lineage trail: one Annotated event per decision.
    let decisions = (UniqueIndexPass.decisionsOf result).Decisions
    Assert.Equal(decisions.Length, result.Trail.Length)
    Assert.All(result.Trail, fun e ->
        Assert.Equal(UniqueIndexPass.version, e.PassVersion)
        Assert.Equal("uniqueIndex", e.PassName)
        match e.TransformKind with
        | Annotated _ -> ()
        | other -> Assert.Fail(sprintf "Expected Annotated, got %A" other))

    // Diagnostic stream: one Warning entry per DoNotEnforce decision.
    let doNotEnforceCount =
        decisions
        |> List.filter (fun d ->
            match d.Outcome with
            | UniqueIndexOutcome.DoNotEnforce _ -> true
            | _ -> false)
        |> List.length
    let entries = LineageDiagnostics.entries result
    Assert.Equal(doNotEnforceCount, entries.Length)
    Assert.All(entries, fun e ->
        Assert.Equal(Warning, e.Severity)
        Assert.Equal("uniqueIndex", e.Source))

[<Fact>]
let ``end-to-end: diagnostic SsKeys align with the decisions that produced them`` () =
    let result = UniqueIndexPass.run endToEndCatalog singleOffCompositeOnPolicy Profile.empty

    let doNotEnforceKeys =
        (UniqueIndexPass.decisionsOf result).Decisions
        |> List.choose (fun d ->
            match d.Outcome with
            | UniqueIndexOutcome.DoNotEnforce _ -> Some d.IndexKey
            | _ -> None)

    let entryKeys =
        LineageDiagnostics.entries result
        |> List.choose (fun e -> e.SsKey)

    Assert.Equal<SsKey list>(doNotEnforceKeys, entryKeys)

// ---------------------------------------------------------------------------
// T1 byte-determinism extends to the dual writer.
// Same triple ⇒ identical decisions, identical lineage trail,
// identical diagnostic stream.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: byte-determinism holds for the dual writer (decisions + lineage + diagnostics)`` () =
    let r1 = UniqueIndexPass.run endToEndCatalog singleOffCompositeOnPolicy Profile.empty
    let r2 = UniqueIndexPass.run endToEndCatalog singleOffCompositeOnPolicy Profile.empty

    // The decision set, the lineage trail, and the diagnostic stream
    // each independently hold byte-determinism. Asserting them
    // separately makes a regression's source visible.
    Assert.Equal<UniqueIndexDecisionSet>(
        UniqueIndexPass.decisionsOf r1,
        UniqueIndexPass.decisionsOf r2)
    Assert.Equal<LineageEvent list>(r1.Trail, r2.Trail)
    Assert.Equal<DiagnosticEntry list>(
        LineageDiagnostics.entries r1,
        LineageDiagnostics.entries r2)

// ---------------------------------------------------------------------------
// Observable identity on empty policy — the structural commitment
// extends to the dual writer.
// ---------------------------------------------------------------------------

[<Fact>]
let ``end-to-end: empty policy yields empty decisions, empty trail, empty diagnostics`` () =
    let result = UniqueIndexPass.run endToEndCatalog Policy.empty Profile.empty

    Assert.Empty((UniqueIndexPass.decisionsOf result).Decisions)
    Assert.Empty(result.Trail)
    Assert.Empty(LineageDiagnostics.entries result)

// ---------------------------------------------------------------------------
// LineageDiagnostics.diagnostics gives both halves together — useful
// for downstream consumers (e.g., the eventual JSON manifest emitter)
// that want value + entries without the lineage trail.
// ---------------------------------------------------------------------------

[<Fact>]
let ``end-to-end: LineageDiagnostics.diagnostics returns the inner Diagnostics<_>`` () =
    let result = UniqueIndexPass.run endToEndCatalog singleOffCompositeOnPolicy Profile.empty
    let diag : Diagnostics<UniqueIndexDecisionSet> = LineageDiagnostics.diagnostics result

    Assert.Equal<UniqueIndexDecisionSet>(
        UniqueIndexPass.decisionsOf result,
        diag.Value)
    Assert.Equal<DiagnosticEntry list>(
        LineageDiagnostics.entries result,
        diag.Entries)

// ---------------------------------------------------------------------------
// Nullability + UniqueIndex opportunity streams flowing together.
// Session 15: the codification's second real test. Two passes, each
// producing its own Lineage<Diagnostics<_>>, run against a shared
// catalog + policy. Each pass's opportunity stream is independent;
// their diagnostic entries do not interleave (each pass returns its
// own dual writer; the orchestrator composes them at the call site,
// not via shared state).
// ---------------------------------------------------------------------------

let private nullabilityCatalog : Catalog =
    let mandatoryAttributeKey = ssKey "OS_ATTR_DiagEnd_Sample_Mandatory"
    let idAttributeKey        = ssKey "OS_ATTR_DiagEnd_Sample_Id"
    let sampleKey             = ssKey "OS_KIND_DiagEnd_Sample"
    let sample : Kind =
        { SsKey    = sampleKey
          Name     = name "Sample"
          Origin   = OsNative
          Modality = []
          Physical = { Schema = "dbo"; Table = "OSUSR_DIAG_END_SAMPLE" }
          Attributes = [
              { SsKey        = idAttributeKey
                Name         = name "Id"
                Type         = Integer
                Column       = { ColumnName = "ID"; IsNullable = false }
                IsPrimaryKey = true
                IsMandatory  = false }
              { SsKey        = mandatoryAttributeKey
                Name         = name "Mandatory"
                Type         = Text
                Column       = { ColumnName = "MANDATORY"; IsNullable = true }
                IsPrimaryKey = false
                IsMandatory  = true } ]
          References = []; Indexes = [] }
    { Modules = [
        { SsKey = ssKey "OS_MOD_DiagEnd"
          Name  = name "DiagEnd"
          Kinds = [ sample ] } ] }

let private nullabilityProfileWithNullsBeyondBudget : Profile =
    let mandatoryAttributeKey = ssKey "OS_ATTR_DiagEnd_Sample_Mandatory"
    let probe =
        ProbeStatus.create System.DateTimeOffset.UnixEpoch 100L Succeeded
        |> Result.value
    { Profile.empty with
        Columns = [
            { AttributeKey         = mandatoryAttributeKey
              RowCount             = 100L
              NullCount            = 12L
              NullCountProbeStatus = probe } ] }

let private nullabilityPolicyForOpportunity : Policy =
    let cfg = NullabilityTighteningConfig.create 0.05m false [] |> Result.value
    { Policy.empty with
        Tightening = { Interventions = [ Nullability ("v1-parity", cfg) ] } }

[<Fact>]
let ``end-to-end: Nullability opportunity stream emits one Warning entry on RequireOperatorApproval`` () =
    let result =
        NullabilityPass.run
            nullabilityCatalog
            nullabilityPolicyForOpportunity
            nullabilityProfileWithNullsBeyondBudget

    // Decision side: mandatory column with nulls beyond budget +
    // relaxation forbidden ⇒ RequireOperatorApproval.
    let mandatoryAttributeKey = ssKey "OS_ATTR_DiagEnd_Sample_Mandatory"
    let decisions = (NullabilityPass.decisionsOf result).Decisions
    let mandatoryDecision =
        decisions |> List.find (fun d -> d.AttributeKey = mandatoryAttributeKey)
    match mandatoryDecision.Outcome with
    | NullabilityOutcome.RequireOperatorApproval _ -> ()
    | other -> Assert.Fail(sprintf "Expected RequireOperatorApproval, got %A" other)

    // Diagnostic side: exactly one Warning entry referencing the
    // mandatory column. The PK column produces an EnforceNotNull
    // outcome → no diagnostic.
    let entries = LineageDiagnostics.entries result
    Assert.Single(entries) |> ignore
    let entry = entries.[0]
    Assert.Equal(Warning, entry.Severity)
    Assert.Equal("nullability", entry.Source)
    Assert.Equal(Some mandatoryAttributeKey, entry.SsKey)

[<Fact>]
let ``end-to-end: Nullability and UniqueIndex opportunity streams remain independent under shared catalog`` () =
    // Two passes, each producing its own Lineage<Diagnostics<_>>.
    // Combined catalog adapts: nullability fixtures (Sample +
    // Mandatory column) plus uniqueness fixtures (Customer/Order/
    // Country with indexes). Each pass filters its own intervention
    // variant per the closed-DU dispatch discipline; their
    // diagnostic streams do not cross-pollinate.
    let combinedCatalog : Catalog =
        { Modules = nullabilityCatalog.Modules @ endToEndCatalog.Modules }
    let nullabilityCfg = NullabilityTighteningConfig.create 0.05m false [] |> Result.value
    let uniqueCfg      = UniqueIndexTighteningConfig.create false true
    let combinedPolicy : Policy =
        { Policy.empty with
            Tightening =
                { Interventions =
                    [ Nullability ("null-1", nullabilityCfg)
                      UniqueIndex  ("uniq-1", uniqueCfg) ] } }

    let nullResult = NullabilityPass.run combinedCatalog combinedPolicy nullabilityProfileWithNullsBeyondBudget
    let uniqResult = UniqueIndexPass.run combinedCatalog combinedPolicy Profile.empty

    let nullEntries = LineageDiagnostics.entries nullResult
    let uniqEntries = LineageDiagnostics.entries uniqResult

    // Each pass's diagnostic Source identifies it; entries do not
    // interleave because each pass returns its own dual writer.
    Assert.All(nullEntries, fun e -> Assert.Equal("nullability",  e.Source))
    Assert.All(uniqEntries, fun e -> Assert.Equal("uniqueIndex",  e.Source))

    // Each pass tags its decisions only with its own intervention id.
    let nullInterventionIds =
        (NullabilityPass.decisionsOf nullResult).Decisions
        |> List.map (fun d -> d.InterventionId)
        |> Set.ofList
    let uniqInterventionIds =
        (UniqueIndexPass.decisionsOf uniqResult).Decisions
        |> List.map (fun d -> d.InterventionId)
        |> Set.ofList
    Assert.Equal<Set<string>>(Set.singleton "null-1", nullInterventionIds)
    Assert.Equal<Set<string>>(Set.singleton "uniq-1", uniqInterventionIds)

[<Fact>]
let ``T1: byte-determinism holds for NullabilityPass under the dual writer`` () =
    let r1 =
        NullabilityPass.run
            nullabilityCatalog
            nullabilityPolicyForOpportunity
            nullabilityProfileWithNullsBeyondBudget
    let r2 =
        NullabilityPass.run
            nullabilityCatalog
            nullabilityPolicyForOpportunity
            nullabilityProfileWithNullsBeyondBudget

    Assert.Equal<NullabilityDecisionSet>(
        NullabilityPass.decisionsOf r1,
        NullabilityPass.decisionsOf r2)
    Assert.Equal<LineageEvent list>(r1.Trail, r2.Trail)
    Assert.Equal<DiagnosticEntry list>(
        LineageDiagnostics.entries r1,
        LineageDiagnostics.entries r2)
