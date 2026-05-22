module Projection.Tests.PolicyDiffTests

// H-033: PolicyDiff — structural five-axis policy comparison.
// H-034: ConflictDetector — structural conflict detection from lineage + diagnostics.
// H-035: Policy regression tests — axis isolation property tests.

open Xunit
open FsCheck.Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// H-033: PolicyDiff.compare
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-033: compare equal policies produces no changes`` () =
    let diff = PolicyDiff.compare Policy.empty Policy.empty
    Assert.False(diff.AnyChanged)
    Assert.False(diff.Selection.Changed)
    Assert.False(diff.Emission.Changed)
    Assert.False(diff.Insertion.Changed)
    Assert.False(diff.Tightening.Changed)
    Assert.False(diff.UserMatching.Changed)

[<Fact>]
let ``H-033: compare detects Selection change`` () =
    let before = Policy.empty
    let after  = { Policy.empty with Selection = IncludeOnly (Set.singleton customerKey) }
    let diff   = PolicyDiff.compare before after
    Assert.True(diff.Selection.Changed)
    Assert.False(diff.Emission.Changed)
    Assert.False(diff.Insertion.Changed)
    Assert.True(diff.AnyChanged)

[<Fact>]
let ``H-033: compare detects Emission change`` () =
    let before = Policy.empty
    let after  = { Policy.empty with Emission = EmissionPolicy.dataOnly }
    let diff   = PolicyDiff.compare before after
    Assert.True(diff.Emission.Changed)
    Assert.False(diff.Selection.Changed)
    Assert.True(diff.AnyChanged)

[<Fact>]
let ``H-033: compare detects Insertion change`` () =
    let before = Policy.empty
    let after  = { Policy.empty with Insertion = InsertNew }
    let diff   = PolicyDiff.compare before after
    Assert.True(diff.Insertion.Changed)
    Assert.False(diff.Selection.Changed)
    Assert.True(diff.AnyChanged)

[<Fact>]
let ``H-033: compare detects Tightening change`` () =
    let cfg    = NullabilityTighteningConfig.create 0.1m false [] |> Result.value
    let before = Policy.empty
    let after  = { Policy.empty with Tightening = { Interventions = [Nullability ("n1", cfg)] } }
    let diff   = PolicyDiff.compare before after
    Assert.True(diff.Tightening.Changed)
    Assert.False(diff.Selection.Changed)
    Assert.True(diff.AnyChanged)

[<Fact>]
let ``H-033: compare detects UserMatching change`` () =
    let before = Policy.empty
    let after  = { Policy.empty with UserMatching = BySsKey }
    let diff   = PolicyDiff.compare before after
    Assert.True(diff.UserMatching.Changed)
    Assert.False(diff.Selection.Changed)
    Assert.True(diff.AnyChanged)

[<Fact>]
let ``H-033: compare captures before and after values`` () =
    let before = Policy.empty
    let after  = { Policy.empty with Insertion = Merge }
    let diff   = PolicyDiff.compare before after
    Assert.Equal(SchemaOnly, diff.Insertion.Before)
    Assert.Equal(Merge, diff.Insertion.After)

[<Fact>]
let ``H-033: diffPolicy returns Lineage wrapping the diff`` () =
    let before = Policy.empty
    let after  = { Policy.empty with Insertion = InsertNew }
    let emptyCatalog : Catalog = { Modules = []; Sequences = [] }
    let lineage = PolicyDiff.diffPolicy emptyCatalog Profile.empty before after
    Assert.True(lineage.Value.Insertion.Changed)
    Assert.Equal(InsertNew, lineage.Value.Insertion.After)

[<Fact>]
let ``H-033: diffPolicy trail is empty for structural diff`` () =
    // The structural variant of diffPolicy has an empty trail; the
    // per-Kind trail lands in the full-projection variant (future).
    let emptyCatalog : Catalog = { Modules = []; Sequences = [] }
    let lineage = PolicyDiff.diffPolicy emptyCatalog Profile.empty Policy.empty Policy.empty
    Assert.Empty(lineage.Trail)

// ---------------------------------------------------------------------------
// H-034: ConflictDetector.detectConflicts
// ---------------------------------------------------------------------------

let private removalEvent (passName: string) (key: SsKey) : LineageEvent =
    { PassName       = passName
      PassVersion    = 1
      SsKey          = key
      TransformKind  = Removed ExplicitKeyList
      Classification = OperatorIntent Selection }

let private tighteningEvent (passName: string) (key: SsKey) : LineageEvent =
    { PassName       = passName
      PassVersion    = 1
      SsKey          = key
      TransformKind  = Touched
      Classification = OperatorIntent Tightening }

let private dataEvent (passName: string) (key: SsKey) : LineageEvent =
    { PassName       = passName
      PassVersion    = 1
      SsKey          = key
      TransformKind  = Touched
      Classification = DataIntent }

[<Fact>]
let ``H-034: empty events and diagnostics produces no conflicts`` () =
    let conflicts = ConflictDetector.detectConflicts [] []
    Assert.Empty(conflicts)

[<Fact>]
let ``H-034: OperatorIntent pass on removed kind produces UnreachableTransform`` () =
    let events =
        [ removalEvent   "visibilityMask"  customerKey
          tighteningEvent "nullabilityPass" customerKey ]
    let conflicts = ConflictDetector.detectConflicts events []
    Assert.Single(conflicts) |> ignore
    match conflicts.[0] with
    | UnreachableTransform (passName, ssKey) ->
        Assert.Equal("nullabilityPass", passName)
        Assert.Equal(customerKey, ssKey)
    | other ->
        Assert.Fail(sprintf "Expected UnreachableTransform, got %A" other)

[<Fact>]
let ``H-034: DataIntent pass on removed kind is not a conflict`` () =
    let events =
        [ removalEvent "visibilityMask" customerKey
          dataEvent    "canonicalize"   customerKey ]
    let conflicts = ConflictDetector.detectConflicts events []
    Assert.Empty(conflicts)

[<Fact>]
let ``H-034: removal event itself is not flagged as UnreachableTransform`` () =
    let events = [ removalEvent "visibilityMask" customerKey ]
    let conflicts = ConflictDetector.detectConflicts events []
    Assert.Empty(conflicts)

[<Fact>]
let ``H-034: AxisContradiction from selection-prefixed diagnostic code`` () =
    let diag : DiagnosticEntry =
        { Code     = "selection.excludedKindHasTighteningIntervention"
          Message  = "Kind excluded by selection but tightening intervention registered"
          Severity = DiagnosticSeverity.Warning
          SsKey    = Some customerKey
          Source   = "ConflictDetector"
          SuggestedConfig = None
          Metadata = Map.empty }
    let conflicts = ConflictDetector.detectConflicts [] [diag]
    Assert.Single(conflicts) |> ignore
    match conflicts.[0] with
    | AxisContradiction (axis, code, _) ->
        Assert.Equal(Selection, axis)
        Assert.Equal(diag.Code, code)
    | other ->
        Assert.Fail(sprintf "Expected AxisContradiction, got %A" other)

[<Fact>]
let ``H-034: non-policy diagnostic codes are ignored`` () =
    let diag : DiagnosticEntry =
        { Code     = "nullability.noEvidence"
          Message  = "No evidence for nullability tightening"
          Severity = DiagnosticSeverity.Info
          SsKey    = None
          Source   = "NullabilityPass"
          SuggestedConfig = None
          Metadata = Map.empty }
    let conflicts = ConflictDetector.detectConflicts [] [diag]
    Assert.Empty(conflicts)

[<Fact>]
let ``H-034: multiple conflicts are all returned`` () =
    let events =
        [ removalEvent   "visibilityMask"  customerKey
          tighteningEvent "nullabilityPass" customerKey
          tighteningEvent "fkPass"          customerKey ]
    let conflicts = ConflictDetector.detectConflicts events []
    Assert.Equal(2, conflicts.Length)

// ---------------------------------------------------------------------------
// H-035: Policy regression property tests — axis isolation.
//
// The core claim: changing policy axis X does NOT affect what the other
// axes observe. These are the executable form of the A12 orthogonality
// axiom under the PolicyDiff lens.
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-035: changing only Selection does not change Emission in the diff`` () =
    let before = Policy.empty
    let after  = { Policy.empty with Selection = IncludeOnly (Set.singleton customerKey) }
    let diff   = PolicyDiff.compare before after
    Assert.True(diff.Selection.Changed)
    Assert.False(diff.Emission.Changed)
    Assert.False(diff.Insertion.Changed)
    Assert.False(diff.Tightening.Changed)
    Assert.False(diff.UserMatching.Changed)

[<Fact>]
let ``H-035: changing only Insertion does not change Selection in the diff`` () =
    let before = Policy.empty
    let after  = { Policy.empty with Insertion = TruncateAndInsert }
    let diff   = PolicyDiff.compare before after
    Assert.True(diff.Insertion.Changed)
    Assert.False(diff.Selection.Changed)
    Assert.False(diff.Emission.Changed)
    Assert.False(diff.Tightening.Changed)
    Assert.False(diff.UserMatching.Changed)

[<Fact>]
let ``H-035: changing only Emission does not change Tightening in the diff`` () =
    let before = Policy.empty
    let after  = { Policy.empty with Emission = EmissionPolicy.combined }
    let diff   = PolicyDiff.compare before after
    Assert.True(diff.Emission.Changed)
    Assert.False(diff.Tightening.Changed)
    Assert.False(diff.Selection.Changed)
    Assert.False(diff.Insertion.Changed)
    Assert.False(diff.UserMatching.Changed)

[<Fact>]
let ``H-035: changing only Tightening does not change Selection`` () =
    let cfg    = NullabilityTighteningConfig.create 0.05m true [] |> Result.value
    let before = Policy.empty
    let after  = { Policy.empty with Tightening = { Interventions = [Nullability ("n", cfg)] } }
    let diff   = PolicyDiff.compare before after
    Assert.True(diff.Tightening.Changed)
    Assert.False(diff.Selection.Changed)
    Assert.False(diff.Emission.Changed)
    Assert.False(diff.Insertion.Changed)
    Assert.False(diff.UserMatching.Changed)

[<Fact>]
let ``H-035: changing only UserMatching does not change any other axis`` () =
    let before = Policy.empty
    let after  = { Policy.empty with UserMatching = BySsKey }
    let diff   = PolicyDiff.compare before after
    Assert.True(diff.UserMatching.Changed)
    Assert.False(diff.Selection.Changed)
    Assert.False(diff.Emission.Changed)
    Assert.False(diff.Insertion.Changed)
    Assert.False(diff.Tightening.Changed)

/// Axis isolation in the PolicyExpr DSL: overriding one axis leaves all
/// other axes at Policy.empty values (the Override semantics guarantee).
[<Fact>]
let ``H-035: PolicyExpr Override Selection does not touch other axes`` () =
    let p = { Policy.empty with Selection = IncludeOnly (Set.singleton customerKey)
                                Insertion = InsertNew
                                Emission  = EmissionPolicy.combined }
    let expr = PolicyExpr.Override (Selection, PolicyExpr.ofPolicy p)
    let result = PolicyExpr.eval expr
    Assert.Equal(p.Selection, result.Selection)
    Assert.Equal(Policy.empty.Insertion, result.Insertion)
    Assert.Equal(Policy.empty.Emission, result.Emission)

[<Property>]
let ``H-035: PolicyDiff AnyChanged iff at least one axis Changed`` (insertNew: bool) =
    let policy = if insertNew then { Policy.empty with Insertion = InsertNew } else Policy.empty
    let diff = PolicyDiff.compare Policy.empty policy
    diff.AnyChanged = (diff.Selection.Changed || diff.Emission.Changed
                    || diff.Insertion.Changed || diff.Tightening.Changed
                    || diff.UserMatching.Changed)
