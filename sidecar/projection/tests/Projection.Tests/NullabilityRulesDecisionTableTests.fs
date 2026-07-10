module Projection.Tests.NullabilityRulesDecisionTableTests

// R3-NULL (FORMAL_METHODS.md §2/§3): rung-3 exhaustive cover of
// `NullabilityRules.evaluate`.
//
// Where `NullabilityRulesTests.fs` witnesses the signal hierarchy at
// hand-picked points and by sampling, this suite enumerates the ENTIRE
// finite decision space and verifies at every point — proof by
// exhaustion, not falsification by sampling.
//
// **The factored space (2^5 × 4 = 128 points).**
//   override present   × 2   (a KeepNullable override keyed to the attribute, or none)
//   IsPrimaryKey       × 2
//   Column.IsNullable  × 2   (physically nullable, or physically NOT NULL)
//   IsMandatory        × 2
//   profile class      × 4   (NoProfile | ZeroNulls | WithinBudget | BeyondBudget)
//   AllowMandatoryRelaxation × 2
//
// The profile class discretizes the unbounded numeric evidence into
// the four regions the rule distinguishes, one representative
// realization per class (rowCount 100, budget 0.05: zero nulls / 4
// nulls within the 5-null allowance / 12 nulls beyond it). The
// `observed <= allowed` boundary gets its own edge points (§ boundary
// tests below): exactly-at-budget and one-past-budget.
//
// **The oracle** is an independent transcription of the documented
// signal hierarchy (NullabilityRules.fs, the "Order of evaluation"
// block). Conformance at all 128 points proves code ⇔ documented
// decision table. The precedence / non-interference laws are
// quantified over the same enumeration WITHOUT consulting the oracle,
// so an error shared by oracle and implementation cannot hide from
// them.

open Xunit
open Projection.Core
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// The factored point.
// ---------------------------------------------------------------------------

type private ProfileClass =
    | NoProfile
    | ZeroNulls
    | WithinBudget
    | BeyondBudget

type private DecisionPoint = {
    HasOverride  : bool
    IsPk         : bool
    PhysNullable : bool
    IsMandatory  : bool
    Evidence     : ProfileClass
    AllowRelax   : bool
}

let private bools = [ false; true ]
let private profileClasses = [ NoProfile; ZeroNulls; WithinBudget; BeyondBudget ]

/// The full 2^5 × 4 = 128-point enumeration.
let private allPoints : DecisionPoint list =
    [ for hasOverride in bools do
        for isPk in bools do
          for physNullable in bools do
            for isMandatory in bools do
              for evidence in profileClasses do
                for allowRelax in bools do
                  yield
                      { HasOverride  = hasOverride
                        IsPk         = isPk
                        PhysNullable = physNullable
                        IsMandatory  = isMandatory
                        Evidence     = evidence
                        AllowRelax   = allowRelax } ]

// ---------------------------------------------------------------------------
// Realization — one concrete (config, attribute, profile) per point.
// Representative values: rowCount 100, budget 0.05m ⇒ allowance 5.
// ---------------------------------------------------------------------------

let private budget = 0.05m
let private rowCount = 100L

let private nullCountOf (c: ProfileClass) : int64 =
    match c with
    | NoProfile    -> 0L // unused — no column profile is built
    | ZeroNulls    -> 0L
    | WithinBudget -> 4L
    | BeyondBudget -> 12L

let private subjectKey = attrKey [ "DecisionTable"; "Subject" ]

let private mkAttribute (p: DecisionPoint) : Attribute =
    { Attribute.create subjectKey (mkName "Subject") Text with
        Column       = ColumnRealization.create "SUBJECT" p.PhysNullable |> Result.value
        IsPrimaryKey = p.IsPk
        IsMandatory  = p.IsMandatory }

let private mkColumnProfile (nullCount: int64) : ColumnProfile =
    let probe =
        ProbeStatus.create System.DateTimeOffset.UnixEpoch rowCount Succeeded
        |> Result.value
    { AttributeKey         = subjectKey
      RowCount             = rowCount
      NullCount            = nullCount
      MaxObservedLength    = None
      NullCountProbeStatus = probe }

let private mkProfile (p: DecisionPoint) : Profile =
    match p.Evidence with
    | NoProfile -> Profile.empty
    | c -> { Profile.empty with Columns = [ mkColumnProfile (nullCountOf c) ] }

let private mkConfig (p: DecisionPoint) : NullabilityTighteningConfig =
    let overrides =
        if p.HasOverride then
            [ { AttributeKey = subjectKey; Action = OverrideAction.KeepNullable } ]
        else []
    NullabilityTighteningConfig.create budget p.AllowRelax overrides
    |> Result.value

let private decide (p: DecisionPoint) : NullabilityOutcome =
    (NullabilityRules.evaluate "r3-null" (mkConfig p) (mkAttribute p) (mkProfile p)).Outcome

// ---------------------------------------------------------------------------
// The oracle — the documented signal hierarchy, transcribed
// independently as data. One arm per line of the docstring's
// "Order of evaluation" block.
// ---------------------------------------------------------------------------

let private oracle (p: DecisionPoint) : NullabilityOutcome =
    if p.HasOverride then
        NullabilityOutcome.KeepNullable OperatorOverride
    elif p.IsPk then
        NullabilityOutcome.EnforceNotNull NullabilityEvidence.PrimaryKey
    elif not p.PhysNullable then
        NullabilityOutcome.EnforceNotNull PhysicallyNotNull
    elif p.IsMandatory then
        match p.Evidence with
        | NoProfile    -> NullabilityOutcome.EnforceNotNull LogicalMandatoryNoProfile
        | ZeroNulls    -> NullabilityOutcome.EnforceNotNull (LogicalMandatoryNoNulls rowCount)
        | WithinBudget ->
            NullabilityOutcome.EnforceNotNull
                (LogicalMandatoryWithinBudget (nullCountOf WithinBudget, rowCount, budget))
        | BeyondBudget ->
            if p.AllowRelax then
                NullabilityOutcome.KeepNullable
                    (RelaxedUnderEvidence (nullCountOf BeyondBudget, rowCount, budget))
            else
                NullabilityOutcome.RequireOperatorApproval
                    (MandatoryButHasNullsBeyondBudget (nullCountOf BeyondBudget, rowCount, budget))
    else
        NullabilityOutcome.KeepNullable NoTighteningSignal

// ---------------------------------------------------------------------------
// R3-NULL Law 0 — the enumeration is the space it claims to be.
// ---------------------------------------------------------------------------

[<Fact>]
let ``R3-NULL cardinality: the enumeration covers exactly the 2^5 * 4 = 128-point space`` () =
    Assert.Equal(128, List.length allPoints)
    Assert.Equal(128, allPoints |> List.distinct |> List.length)

// ---------------------------------------------------------------------------
// R3-NULL Law 1 — conformance: at EVERY point, evaluate agrees with
// the documented decision table.
// ---------------------------------------------------------------------------

[<Fact>]
let ``R3-NULL conformance: evaluate matches the documented signal hierarchy at every point of the space`` () =
    let disagreements =
        allPoints
        |> List.choose (fun p ->
            let actual = decide p
            let expected = oracle p
            if actual = expected then None else Some (p, expected, actual))
    Assert.True(
        List.isEmpty disagreements,
        sprintf "%d/128 points disagree with the documented decision table; first: %A"
            (List.length disagreements) (List.tryHead disagreements))

// ---------------------------------------------------------------------------
// R3-NULL Law 2 — precedence, quantified without the oracle.
// ---------------------------------------------------------------------------

[<Fact>]
let ``R3-NULL precedence: the operator override is absolute at every point where it is present`` () =
    allPoints
    |> List.filter (fun p -> p.HasOverride)
    |> List.iter (fun p ->
        Assert.Equal(NullabilityOutcome.KeepNullable OperatorOverride, decide p))

[<Fact>]
let ``R3-NULL precedence: PK decides at every override-free point regardless of the four downstream factors`` () =
    allPoints
    |> List.filter (fun p -> not p.HasOverride && p.IsPk)
    |> List.iter (fun p ->
        Assert.Equal(NullabilityOutcome.EnforceNotNull NullabilityEvidence.PrimaryKey, decide p))

[<Fact>]
let ``R3-NULL precedence: physical NOT NULL decides at every override-free non-PK point`` () =
    allPoints
    |> List.filter (fun p -> not p.HasOverride && not p.IsPk && not p.PhysNullable)
    |> List.iter (fun p ->
        Assert.Equal(NullabilityOutcome.EnforceNotNull PhysicallyNotNull, decide p))

// ---------------------------------------------------------------------------
// R3-NULL Law 3 — non-interference: profile evidence is consulted
// ONLY through the mandatory branch. At every point where a
// structural signal (override / PK / physical NOT NULL) or the
// no-signal fallthrough decides, varying the profile class never
// changes the outcome.
// ---------------------------------------------------------------------------

[<Fact>]
let ``R3-NULL non-interference: outcome is profile-invariant wherever the mandatory branch is not reached`` () =
    allPoints
    |> List.filter (fun p ->
        p.HasOverride || p.IsPk || not p.PhysNullable || not p.IsMandatory)
    |> List.groupBy (fun p -> (p.HasOverride, p.IsPk, p.PhysNullable, p.IsMandatory, p.AllowRelax))
    |> List.iter (fun (_, group) ->
        let outcomes = group |> List.map decide |> List.distinct
        Assert.Equal(1, List.length outcomes))

[<Fact>]
let ``R3-NULL non-interference: AllowMandatoryRelaxation matters only at the beyond-budget conflict point`` () =
    allPoints
    |> List.groupBy (fun p -> { p with AllowRelax = false })
    |> List.iter (fun (representative, group) ->
        let outcomes = group |> List.map decide |> List.distinct
        let isConflictPoint =
            not representative.HasOverride
            && not representative.IsPk
            && representative.PhysNullable
            && representative.IsMandatory
            && representative.Evidence = BeyondBudget
        if isConflictPoint then Assert.Equal(2, List.length outcomes)
        else Assert.Equal(1, List.length outcomes))

// ---------------------------------------------------------------------------
// R3-NULL Law 4 — the third state is exactly characterized: operator
// approval arises at precisely the (no-override, non-PK, physically
// nullable, mandatory, beyond-budget, relaxation-forbidden) point and
// nowhere else.
// ---------------------------------------------------------------------------

[<Fact>]
let ``R3-NULL characterization: RequireOperatorApproval occurs at exactly the documented conflict point`` () =
    let approvalPoints =
        allPoints
        |> List.filter (fun p ->
            match decide p with
            | NullabilityOutcome.RequireOperatorApproval _ -> true
            | _ -> false)
    let expected =
        allPoints
        |> List.filter (fun p ->
            not p.HasOverride && not p.IsPk && p.PhysNullable
            && p.IsMandatory && p.Evidence = BeyondBudget && not p.AllowRelax)
    Assert.Equal<DecisionPoint list>(expected, approvalPoints)

// ---------------------------------------------------------------------------
// R3-NULL Law 5 — totality + determinism over the whole space: every
// point yields exactly one outcome, and yields it twice identically.
// ---------------------------------------------------------------------------

[<Fact>]
let ``R3-NULL determinism: evaluate is stable at every point of the space`` () =
    allPoints
    |> List.iter (fun p -> Assert.Equal(decide p, decide p))

// ---------------------------------------------------------------------------
// Boundary points — the budget comparison's edge, outside the class
// discretization: `observed <= allowed` is inclusive, so
// exactly-at-allowance tightens and one-past-allowance conflicts.
// (rowCount 100 × budget 0.05 ⇒ allowance exactly 5.)
// ---------------------------------------------------------------------------

let private decideAtNullCount (nullCount: int64) (allowRelax: bool) : NullabilityOutcome =
    let attr =
        { Attribute.create subjectKey (mkName "Subject") Text with
            Column      = ColumnRealization.create "SUBJECT" true |> Result.value
            IsMandatory = true }
    let profile = { Profile.empty with Columns = [ mkColumnProfile nullCount ] }
    let cfg = NullabilityTighteningConfig.create budget allowRelax [] |> Result.value
    (NullabilityRules.evaluate "r3-null-edge" cfg attr profile).Outcome

[<Fact>]
let ``R3-NULL boundary: observed nulls exactly at the allowance tighten (the comparison is inclusive)`` () =
    Assert.Equal(
        NullabilityOutcome.EnforceNotNull (LogicalMandatoryWithinBudget (5L, rowCount, budget)),
        decideAtNullCount 5L false)

[<Fact>]
let ``R3-NULL boundary: observed nulls one past the allowance conflict`` () =
    Assert.Equal(
        NullabilityOutcome.RequireOperatorApproval
            (MandatoryButHasNullsBeyondBudget (6L, rowCount, budget)),
        decideAtNullCount 6L false)
