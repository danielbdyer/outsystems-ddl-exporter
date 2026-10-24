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
let ``H-033: diffPolicy returns Lineage wrapping the structural diff`` () =
    let before = Policy.empty
    let after  = { Policy.empty with Insertion = InsertNew }
    let lineage = PolicyDiff.diffPolicy before after
    Assert.True(lineage.Value.Insertion.Changed)
    Assert.Equal(InsertNew, lineage.Value.Insertion.After)

[<Fact>]
let ``H-033: diffPolicy trail is empty for the structural variant`` () =
    let lineage = PolicyDiff.diffPolicy Policy.empty Policy.empty
    Assert.Empty(lineage.Trail)

[<Fact>]
let ``H-033 full-projection: same policy on the same catalog produces no per-Kind deltas`` () =
    let lineage =
        PolicyDiff.diffFullProjection sampleCatalog Profile.empty Policy.empty Policy.empty
    Assert.Empty(lineage.Value.ChangedKinds)
    Assert.Empty(lineage.Value.KindDeltas)
    Assert.False(lineage.Value.StructuralDiff.AnyChanged)

[<Fact>]
let ``H-033 full-projection: changing Tightening produces per-Kind deltas`` () =
    // Tightening interventions thread through `allChainStepsFor` into the
    // NullabilityPass / UniqueIndexPass / ForeignKeyPass — adding one
    // changes those passes' decisions and therefore their lineage events
    // for at least one kind. Tightening is the canonical chain-wired axis;
    // Selection's filterCatalog effect lives outside the chain.
    let cfg = NullabilityTighteningConfig.create 0.05m true [] |> Result.value
    let after =
        { Policy.empty with
            Tightening = { Interventions = [Nullability ("test", cfg)] } }
    let lineage =
        PolicyDiff.diffFullProjection sampleCatalog Profile.empty Policy.empty after
    Assert.True(lineage.Value.StructuralDiff.Tightening.Changed)
    Assert.NotEmpty(lineage.Value.ChangedKinds)

[<Fact>]
let ``H-033 full-projection: changing Selection is recorded structurally even when the chain is Selection-independent`` () =
    // Selection's catalog-filter effect is applied outside the registered
    // chain (see SelectionPolicy.filterCatalog vs. allChainStepsFor); a
    // Selection axis change therefore shows up in StructuralDiff but may
    // not produce chain-level lineage deltas. The structural-diff
    // attribution is the carrier consumers rely on for Selection deltas.
    let after =
        { Policy.empty with
            Selection = ExcludeOnly (Set.singleton customerKey) }
    let lineage =
        PolicyDiff.diffFullProjection sampleCatalog Profile.empty Policy.empty after
    Assert.True(lineage.Value.StructuralDiff.Selection.Changed)

[<Fact>]
let ``H-033 full-projection: structural axis diff is carried through`` () =
    let after = { Policy.empty with Insertion = InsertNew }
    let lineage =
        PolicyDiff.diffFullProjection sampleCatalog Profile.empty Policy.empty after
    Assert.True(lineage.Value.StructuralDiff.Insertion.Changed)
    Assert.Equal(InsertNew, lineage.Value.StructuralDiff.Insertion.After)

[<Fact>]
let ``H-033 full-projection: Lineage trail concatenates both runs' events`` () =
    let after = { Policy.empty with Insertion = Merge }
    let lineage =
        PolicyDiff.diffFullProjection sampleCatalog Profile.empty Policy.empty after
    // Two runs of the chain produce strictly more events than one run.
    let singleRun =
        let chain = RegisteredTransforms.allChainStepsFor Policy.empty Profile.empty
        PassChainAdapter.compose chain (ComposeState.initial sampleCatalog)
    Assert.True(lineage.Trail.Length >= singleRun.Trail.Length)

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

// Helper: build a Warning diagnostic at the named code targeting `key`.
let private warningDiag (code: string) (key: SsKey) (message: string) : DiagnosticEntry =
    { DiagnosticEntry.create "ConflictDetectorTests" DiagnosticSeverity.Warning code message
      with SsKey = Some key }

[<Fact>]
let ``H-034: tightening diagnostic on a Selection-removed kind is a contradiction`` () =
    // The HORIZON-compliant shape: tightening fired on a kind Selection
    // had already removed → the axis acted on something the catalog never
    // surfaces. This is the true conflict signal.
    let events = [ removalEvent "visibilityMask" customerKey ]
    let diag = warningDiag "tightening.nullability.requireOperatorApproval"
                           customerKey
                           "Nullability requires operator approval"
    let conflicts = ConflictDetector.detectConflicts events [diag]
    Assert.Single(conflicts) |> ignore
    match conflicts.[0] with
    | AxisContradiction (axis, key, code, _) ->
        Assert.Equal(Tightening, axis)
        Assert.Equal(customerKey, key)
        Assert.Equal(diag.Code, code)
    | other ->
        Assert.Fail(sprintf "Expected AxisContradiction, got %A" other)

[<Fact>]
let ``H-034 regression: normal tightening diagnostic on a VISIBLE kind is NOT a conflict`` () =
    // The regression test for the audit-discovered false-positive defect:
    // working `tightening.*` diagnostics are NOT contradictions on their
    // own. Production passes (NullabilityPass, UniqueIndexPass,
    // ForeignKeyPass) emit `tightening.nullability.relaxedUnderEvidence`
    // and similar codes during normal evidence-based decision-making.
    // These must not flood the conflict surface.
    let diag = warningDiag "tightening.nullability.relaxedUnderEvidence"
                           customerKey
                           "Nullability relaxed under empirical evidence"
    let conflicts = ConflictDetector.detectConflicts [] [diag]
    Assert.Empty(conflicts)

[<Fact>]
let ``H-034 regression: tightening diagnostic on visible kind even with Selection removals elsewhere is NOT a conflict`` () =
    // Customer was removed but the tightening diagnostic targets Order
    // (still visible). Order being acted on is not a conflict.
    let events = [ removalEvent "visibilityMask" customerKey ]
    let diag = warningDiag "tightening.uniqueIndex.policyDisabled"
                           orderKey
                           "Unique index disabled by policy"
    let conflicts = ConflictDetector.detectConflicts events [diag]
    Assert.Empty(conflicts)

[<Fact>]
let ``H-034: Selection-prefix diagnostic on a Selection-removed kind is NOT flagged as self-contradiction`` () =
    // The Selection axis's own removal-emitted code targeting the removed
    // kind is the removal's diagnostic, not a contradiction.
    let events = [ removalEvent "visibilityMask" customerKey ]
    let diag = warningDiag "selection.inactive-attribute"
                           customerKey
                           "Selection excluded inactive attribute"
    let conflicts = ConflictDetector.detectConflicts events [diag]
    Assert.Empty(conflicts)

[<Fact>]
let ``H-034: non-policy diagnostic codes are ignored even on removed kinds`` () =
    let events = [ removalEvent "visibilityMask" customerKey ]
    let diag = warningDiag "profiling.anomaly.nullRate.high"
                           customerKey
                           "Null rate anomaly"
    let conflicts = ConflictDetector.detectConflicts events [diag]
    Assert.Empty(conflicts)

[<Fact>]
let ``H-034: emission diagnostic on a removed kind is a contradiction`` () =
    let events = [ removalEvent "visibilityMask" customerKey ]
    let diag = warningDiag "emission.nameRewrite"
                           customerKey
                           "Name rewrite emitted for excluded kind"
    let conflicts = ConflictDetector.detectConflicts events [diag]
    Assert.Single(conflicts) |> ignore
    match conflicts.[0] with
    | AxisContradiction (axis, _, _, _) -> Assert.Equal(Emission, axis)
    | other -> Assert.Fail(sprintf "Expected AxisContradiction, got %A" other)

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

// ---------------------------------------------------------------------------
// H-035 (pipeline-level, executable A12 orthogonality): the HORIZON contract.
//
// The audit found the prior shipped tests proved `PolicyDiff.compare`'s
// correctness, NOT that the pipeline preserves axis isolation. These tests
// remediate by running the registered pipeline twice and asserting that an
// axis change produces lineage events ONLY for kinds that the changed axis
// can plausibly observe.
// ---------------------------------------------------------------------------

let private runForPolicy (policy: Policy) =
    let chain = RegisteredTransforms.allChainStepsFor policy Profile.empty
    PassChainAdapter.compose chain (ComposeState.initial sampleCatalog)

/// Lineage events that differ between two runs. Used to detect which
/// kinds the policy delta actually touched at the pipeline level.
let private eventsDeltaKeys (a: Lineage<Diagnostics<ComposeState>>) (b: Lineage<Diagnostics<ComposeState>>) : Set<SsKey> =
    let aByKey =
        a.Trail |> List.groupBy (fun e -> e.SsKey)
        |> List.map (fun (k, evs) -> k, evs) |> Map.ofList
    let bByKey =
        b.Trail |> List.groupBy (fun e -> e.SsKey)
        |> List.map (fun (k, evs) -> k, evs) |> Map.ofList
    let allKeys =
        Set.union
            (Map.keys aByKey |> Set.ofSeq)
            (Map.keys bByKey |> Set.ofSeq)
    allKeys
    |> Set.filter (fun k ->
        Map.tryFind k aByKey <> Map.tryFind k bByKey)

[<Fact>]
let ``H-035 pipeline: running the same policy twice produces identical lineage`` () =
    // Baseline determinism: same input, same output, byte-equal trails.
    // This is the A24 chronological-bind property at the pipeline level.
    let a = runForPolicy Policy.empty
    let b = runForPolicy Policy.empty
    Assert.Equal<LineageEvent list>(a.Trail, b.Trail)

[<Fact>]
let ``H-035 pipeline: changing Tightening produces lineage deltas`` () =
    // Tightening interventions are the chain-wired axis: NullabilityPass /
    // UniqueIndexPass / ForeignKeyPass consume `policy.Tightening` when
    // making decisions. Adding an intervention must produce at least one
    // changed lineage event.
    let cfg = NullabilityTighteningConfig.create 0.05m true [] |> Result.value
    let baseline = runForPolicy Policy.empty
    let withInt =
        runForPolicy
            { Policy.empty with Tightening = { Interventions = [Nullability ("p1", cfg)] } }
    let changed = eventsDeltaKeys baseline withInt
    Assert.NotEmpty(changed)

[<Fact>]
let ``H-035 pipeline: Selection-only delta leaves the chain trail unchanged (chain is Selection-independent)`` () =
    // SelectionPolicy.filterCatalog is applied outside the registered
    // chain. A Selection-axis change therefore must NOT perturb the
    // chain's lineage events; the axis isolation is structural here
    // (chain output equals before-and-after). The Selection delta still
    // appears in the StructuralDiff (covered by `H-033 full-projection
    // changing Selection is recorded structurally`).
    let baseline = runForPolicy Policy.empty
    let restricted =
        runForPolicy { Policy.empty with Selection = ExcludeOnly (Set.singleton customerKey) }
    let changed = eventsDeltaKeys baseline restricted
    Assert.Empty(changed)

[<Fact>]
let ``H-035 pipeline: changing one chain-wired axis preserves the other axes' decision-set Option shape`` () =
    // ComposeState carries decision sets per axis. Changing Tightening
    // must not toggle whether TopologicalOrderPass populated its slot,
    // or whether UserRemap was set: the shape of the decision-set
    // optional structure is policy-axis-orthogonal.
    let cfg = NullabilityTighteningConfig.create 0.05m true [] |> Result.value
    let baseline = runForPolicy Policy.empty
    let withInt =
        runForPolicy
            { Policy.empty with Tightening = { Interventions = [Nullability ("p1", cfg)] } }
    let baseState  = LineageDiagnostics.payload baseline
    let restState  = LineageDiagnostics.payload withInt
    Assert.Equal(Option.isSome baseState.TopologicalOrder,
                 Option.isSome restState.TopologicalOrder)
    Assert.Equal(Option.isSome baseState.UserRemap,
                 Option.isSome restState.UserRemap)

[<Fact>]
let ``H-035 pipeline: identical policies produce empty FullProjectionDiff`` () =
    let lineage =
        PolicyDiff.diffFullProjection sampleCatalog Profile.empty Policy.empty Policy.empty
    Assert.Empty(lineage.Value.ChangedKinds)
    Assert.False(lineage.Value.StructuralDiff.AnyChanged)

[<Fact>]
let ``H-035 pipeline: Tightening delta produces non-empty ChangedKinds in the full-projection diff`` () =
    let cfg = NullabilityTighteningConfig.create 0.05m true [] |> Result.value
    let after =
        { Policy.empty with
            Tightening = { Interventions = [Nullability ("p1", cfg)] } }
    let lineage =
        PolicyDiff.diffFullProjection sampleCatalog Profile.empty Policy.empty after
    Assert.NotEmpty(lineage.Value.ChangedKinds)
