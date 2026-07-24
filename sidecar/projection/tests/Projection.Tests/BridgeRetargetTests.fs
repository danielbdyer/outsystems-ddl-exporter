module Projection.Tests.BridgeRetargetTests

open Xunit
open Projection.Core

// The generic pure bridge-retarget kernel (BridgeRetarget.fs). The laws exercised:
//   * the clean profile is the identity — every one of the three verdicts is Ready;
//   * each block failure gates ONLY the verdicts it informs (Retargeting) and names
//     the offending check; each warning downgrades to ReadyWithWarnings, never Blocked;
//   * the three verdicts are INDEPENDENT — a bridge-key block never touches PayloadSync,
//     a payload-conflict warning never touches Retargeting;
//   * the check ledger is total (every BridgeCheck.all case is evaluated, names unique).

let private eval (p: BridgeRetargetProfile) = BridgeRetarget.evaluate p
let private clean = BridgeRetargetProfile.clean "rt1"

let private failed (r: BridgeReadiness) : BridgeCheck list =
    match r with
    | BridgeReadiness.Blocked cs -> cs
    | BridgeReadiness.ReadyWithWarnings cs -> cs
    | BridgeReadiness.Ready -> []

[<Fact>]
let ``clean profile is the identity — all three verdicts Ready`` () =
    let d = eval clean
    Assert.Equal(BridgeReadiness.Ready, d.Retargeting)
    Assert.Equal(BridgeReadiness.Ready, d.BridgeRows)
    Assert.Equal(BridgeReadiness.Ready, d.PayloadSync)
    // and every check passed
    Assert.All(d.Checks, fun c -> Assert.True(c.Passed, BridgeCheck.name c.Check))

[<Fact>]
let ``check ledger is total — every BridgeCheck.all case is evaluated exactly once`` () =
    let d = eval clean
    let evaluated = d.Checks |> List.map (fun c -> c.Check)
    Assert.Equal<BridgeCheck list>(BridgeCheck.all, evaluated)
    // names are stable and unique
    let names = BridgeCheck.all |> List.map BridgeCheck.name
    Assert.Equal(List.length names, names |> List.distinct |> List.length)

[<Fact>]
let ``non-unique bridge key BLOCKS retargeting and names the check — payload untouched`` () =
    let d = eval { clean with BridgeKeyDuplicateCount = 3L }
    match d.Retargeting with
    | BridgeReadiness.Blocked cs -> Assert.Contains(BridgeCheck.BridgeKeyUnique, cs)
    | other -> Assert.Fail(sprintf "expected Blocked, got %A" other)
    // verdict isolation: bridge-key uniqueness does not inform PayloadSync
    Assert.Equal(BridgeReadiness.Ready, d.PayloadSync)

[<Fact>]
let ``null bridge key BLOCKS retargeting`` () =
    let d = eval { clean with BridgeKeyNullCount = 1L }
    Assert.Contains(BridgeCheck.BridgeKeyNonNull, failed d.Retargeting)
    Assert.False(BridgeRetarget.retargetCleared d)

[<Fact>]
let ``unresolved values BLOCK the coverage invariant`` () =
    let d = eval { clean with UnresolvedThroughBridgeCount = 5L }
    Assert.Contains(BridgeCheck.AllSourceValuesResolveThroughBridge, failed d.Retargeting)
    match d.Retargeting with
    | BridgeReadiness.Blocked _ -> ()
    | other -> Assert.Fail(sprintf "expected Blocked, got %A" other)

[<Fact>]
let ``targeting the bridge primary key BLOCKS the retarget`` () =
    let d = eval { clean with TargetsBridgePrimaryKey = true }
    Assert.Contains(BridgeCheck.RetargetWouldUseBridgePrimaryKey, failed d.Retargeting)

[<Fact>]
let ``broken original parent BLOCKS the retarget`` () =
    let d = eval { clean with BrokenOriginalParentCount = 2L }
    Assert.Contains(BridgeCheck.SourceValuesStillResolveToOriginalParent, failed d.Retargeting)

[<Fact>]
let ``type mismatch and no-trusted-constraint both BLOCK`` () =
    let d = eval { clean with KeyTypesMatch = false; TrustedConstraintPossible = false }
    let blocks = failed d.Retargeting
    Assert.Contains(BridgeCheck.SourceAndBridgeKeyTypesMatch, blocks)
    Assert.Contains(BridgeCheck.TrustedConstraintPossible, blocks)

[<Fact>]
let ``existing retarget conflict BLOCKS`` () =
    let d = eval { clean with ExistingRetargetConflict = true }
    Assert.Contains(BridgeCheck.ExistingRetargetConflict, failed d.Retargeting)

[<Fact>]
let ``orphaned bridge rows only WARN — bridge-rows verdict, retarget stays clear`` () =
    let d = eval { clean with OrphanedBridgeRowCount = 4L }
    match d.BridgeRows with
    | BridgeReadiness.ReadyWithWarnings cs -> Assert.Contains(BridgeCheck.BridgeRowsOrphanedFromSource, cs)
    | other -> Assert.Fail(sprintf "expected ReadyWithWarnings, got %A" other)
    // a warning never blocks retargeting
    Assert.Equal(BridgeReadiness.Ready, d.Retargeting)
    Assert.True(BridgeRetarget.retargetCleared d)

[<Fact>]
let ``payload conflicts only WARN on the payload-sync verdict — nothing else moves`` () =
    let d = eval { clean with PayloadConflictCount = 1L }
    match d.PayloadSync with
    | BridgeReadiness.ReadyWithWarnings cs -> Assert.Contains(BridgeCheck.BridgePayloadConflicts, cs)
    | other -> Assert.Fail(sprintf "expected ReadyWithWarnings, got %A" other)
    Assert.Equal(BridgeReadiness.Ready, d.Retargeting)
    Assert.Equal(BridgeReadiness.Ready, d.BridgeRows)

[<Fact>]
let ``missing identity evidence WARNS the bridge-rows verdict only`` () =
    let d = eval { clean with IdentityEvidence = BridgeIdentityEvidence.Missing }
    Assert.Contains(BridgeCheck.IdentityEvidenceMissing, failed d.BridgeRows)
    Assert.Equal(BridgeReadiness.Ready, d.Retargeting)

[<Fact>]
let ``ambiguous identity evidence WARNS the bridge-rows verdict`` () =
    let d = eval { clean with IdentityEvidence = BridgeIdentityEvidence.Ambiguous }
    Assert.Contains(BridgeCheck.IdentityEvidenceAmbiguous, failed d.BridgeRows)

[<Fact>]
let ``untrusted existing constraint WARNS retargeting but does not block it`` () =
    let d = eval { clean with ExistingConstraintTrusted = Some false }
    match d.Retargeting with
    | BridgeReadiness.ReadyWithWarnings cs -> Assert.Contains(BridgeCheck.ExistingConstraintUntrusted, cs)
    | other -> Assert.Fail(sprintf "expected ReadyWithWarnings, got %A" other)
    Assert.True(BridgeRetarget.retargetCleared d)

[<Fact>]
let ``a trusted (or absent) existing constraint fires no untrusted warning`` () =
    Assert.Equal(BridgeReadiness.Ready, (eval { clean with ExistingConstraintTrusted = Some true }).Retargeting)
    Assert.Equal(BridgeReadiness.Ready, (eval { clean with ExistingConstraintTrusted = None }).Retargeting)

[<Fact>]
let ``non-audit references WARN retargeting without blocking`` () =
    let d = eval { clean with OnlyAuditReferences = false }
    match d.Retargeting with
    | BridgeReadiness.ReadyWithWarnings cs -> Assert.Contains(BridgeCheck.NonAuditReferencesIncluded, cs)
    | other -> Assert.Fail(sprintf "expected ReadyWithWarnings, got %A" other)

[<Fact>]
let ``a block and a warning on the same verdict yields Blocked (block dominates)`` () =
    // BridgeKeyUnique (block) + ExistingConstraintUntrusted (warn) both inform Retargeting.
    let d = eval { clean with BridgeKeyDuplicateCount = 1L; ExistingConstraintTrusted = Some false }
    match d.Retargeting with
    | BridgeReadiness.Blocked cs ->
        Assert.Contains(BridgeCheck.BridgeKeyUnique, cs)
        // the warning is not promoted into the block list
        Assert.DoesNotContain(BridgeCheck.ExistingConstraintUntrusted, cs)
    | other -> Assert.Fail(sprintf "expected Blocked, got %A" other)

[<Fact>]
let ``retargetCleared is false exactly when Retargeting is Blocked`` () =
    Assert.True(BridgeRetarget.retargetCleared (eval clean))
    Assert.True(BridgeRetarget.retargetCleared (eval { clean with OrphanedBridgeRowCount = 1L }))
    Assert.False(BridgeRetarget.retargetCleared (eval { clean with BridgeKeyPresent = false }))

[<Fact>]
let ``absent bridge key blocks both retargeting and bridge-rows (shared check)`` () =
    let d = eval { clean with BridgeKeyPresent = false }
    Assert.Contains(BridgeCheck.BridgeKeyExists, failed d.Retargeting)
    Assert.Contains(BridgeCheck.BridgeKeyExists, failed d.BridgeRows)
