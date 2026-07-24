module Projection.Tests.BridgeRetargetEvidenceBindingTests

open System.IO
open Xunit
open Projection.Core
open Projection.Pipeline

/// The file-based bridge-retarget EVIDENCE SUPPLEMENT (`BridgeRetargetBinding`
/// `.loadEvidence` + `.applyEvidence`). The evidence file supplies the DATA half of
/// a retarget's readiness profile — the facts a catalog inspection cannot know —
/// so a configured retarget can CLEAR (its fail-closed `unproven` data facts are
/// overridden). The laws exercised:
///   * loadEvidence: no path ⇒ empty map (every retarget stays blocked, byte-
///     identical); a valid file parses each id's facts; OMITTED counts default
///     FAIL-CLOSED (the blocking counts to 1, so a partial entry keeps the retarget
///     blocked); malformed / unreadable / duplicate-id / bad-count / bad-identity
///     files are NAMED refusals (`pipeline.config.bridgeRetargetEvidence.*`).
///   * applyEvidence: all-clear evidence over a structurally-sound unproven profile
///     CLEARS the retarget; the SAME profile without evidence stays BLOCKED (the
///     fail-closed boundary); a residual data block still blocks; a warning-only
///     fact clears with the warning surfaced.

// ---------------------------------------------------------------------------
// applyEvidence — the DATA-fact override at the exact seam the binder applies it.
// ---------------------------------------------------------------------------

/// A structurally-sound retarget whose DATA facts are still fail-closed — exactly
/// what the binder assembles from the catalog before the evidence supplement.
let private structural : BridgeRetargetProfile =
    { BridgeRetargetProfile.unproven "rt1" with
        BridgeKeyPresent          = true
        TargetsBridgePrimaryKey   = false
        KeyTypesMatch             = true
        ExistingConstraintTrusted = Some true }

let private allClear : BridgeRetargetEvidence =
    { UnresolvedThroughBridge = 0L
      BrokenOriginalParent    = 0L
      OrphanedBridgeRows      = 0L
      PayloadConflicts        = 0L
      BridgeKeyDuplicates     = 0L
      BridgeKeyNulls          = 0L
      IdentityEvidence        = BridgeIdentityEvidence.Present }

let private cleared (p: BridgeRetargetProfile) : bool =
    BridgeRetarget.retargetCleared (BridgeRetarget.evaluate p)

[<Fact>]
let ``without evidence a structurally-sound retarget stays BLOCKED (fail-closed)`` () =
    // The binder fills the catalog facts but leaves the DATA facts unproven, so the
    // retarget never clears on the strength of the declaration alone.
    Assert.False(cleared structural)

[<Fact>]
let ``all-clear evidence CLEARS the same retarget`` () =
    let proven = BridgeRetargetBinding.applyEvidence allClear structural
    Assert.True(cleared proven)
    // and a trusted constraint is DERIVED as achievable (full coverage, no nulls).
    Assert.True(proven.TrustedConstraintPossible)

[<Fact>]
let ``a residual data block in the evidence keeps the retarget BLOCKED`` () =
    // Evidence that is clean except for unresolved coverage still fails the
    // success-invariant coverage check — the retarget does not land.
    let partial = { allClear with UnresolvedThroughBridge = 3L }
    let proven = BridgeRetargetBinding.applyEvidence partial structural
    Assert.False(cleared proven)
    // and the derived trusted-constraint fact is false (coverage is incomplete).
    Assert.False(proven.TrustedConstraintPossible)

[<Fact>]
let ``null bridge keys in the evidence keep the retarget BLOCKED and deny a trusted constraint`` () =
    let proven = BridgeRetargetBinding.applyEvidence { allClear with BridgeKeyNulls = 2L } structural
    Assert.False(cleared proven)
    Assert.False(proven.TrustedConstraintPossible)

[<Fact>]
let ``warning-only evidence CLEARS the retarget with the warning surfaced`` () =
    // Orphaned bridge rows + missing identity are warnings, never blocks.
    let warnEvidence = { allClear with OrphanedBridgeRows = 4L; IdentityEvidence = BridgeIdentityEvidence.Missing }
    let proven = BridgeRetargetBinding.applyEvidence warnEvidence structural
    let decision = BridgeRetarget.evaluate proven
    Assert.True(BridgeRetarget.retargetCleared decision)
    match decision.BridgeRows with
    | BridgeReadiness.ReadyWithWarnings cs ->
        Assert.Contains(BridgeCheck.BridgeRowsOrphanedFromSource, cs)
        Assert.Contains(BridgeCheck.IdentityEvidenceMissing, cs)
    | other -> Assert.Fail(sprintf "expected ReadyWithWarnings on BridgeRows, got %A" other)

[<Fact>]
let ``applyEvidence leaves the STRUCTURAL facts untouched (only the data half moves)`` () =
    let proven = BridgeRetargetBinding.applyEvidence allClear structural
    Assert.True(proven.BridgeKeyPresent)
    Assert.False(proven.TargetsBridgePrimaryKey)
    Assert.True(proven.KeyTypesMatch)
    Assert.Equal(Some true, proven.ExistingConstraintTrusted)
    // the id is preserved through the override
    Assert.Equal("rt1", proven.RetargetId)

// ---------------------------------------------------------------------------
// loadEvidence — the file boundary (fail-closed defaults + named refusals).
// ---------------------------------------------------------------------------

let private withTempFile (content: string) (f: string -> 'a) : 'a =
    let path = Path.Combine(Path.GetTempPath(), sprintf "bridgeev_%s.json" (System.Guid.NewGuid().ToString("N")))
    File.WriteAllText(path, content)
    try f path
    finally (try File.Delete path with _ -> ())

let private hasErrorCode (code: string) (errs: ValidationError list) : bool =
    errs |> List.exists (fun e -> e.Code = code)

let private loadText (content: string) : Result<Map<string, BridgeRetargetEvidence>> =
    withTempFile content (fun path -> BridgeRetargetBinding.loadEvidence (Some { Path = path }))

[<Fact>]
let ``no evidence path yields the empty map (byte-identical: every retarget blocked)`` () =
    match BridgeRetargetBinding.loadEvidence None with
    | Ok m -> Assert.True(Map.isEmpty m)
    | Error errs -> Assert.Fail(sprintf "expected Ok, got %A" errs)

[<Fact>]
let ``a valid file parses each id's data facts`` () =
    let json =
        """{ "retargets": [
              { "id": "user-createdby",
                "unresolvedThroughBridge": 0, "brokenOriginalParent": 0,
                "orphanedBridgeRows": 2, "payloadConflicts": 1,
                "bridgeKeyDuplicates": 0, "bridgeKeyNulls": 0,
                "identityEvidence": "present" } ] }"""
    match loadText json with
    | Ok m ->
        let ev = Map.find "user-createdby" m
        Assert.Equal(0L, ev.UnresolvedThroughBridge)
        Assert.Equal(0L, ev.BrokenOriginalParent)
        Assert.Equal(2L, ev.OrphanedBridgeRows)
        Assert.Equal(1L, ev.PayloadConflicts)
        Assert.Equal(0L, ev.BridgeKeyDuplicates)
        Assert.Equal(0L, ev.BridgeKeyNulls)
        Assert.Equal(BridgeIdentityEvidence.Present, ev.IdentityEvidence)
    | Error errs -> Assert.Fail(sprintf "expected Ok, got %A" errs)

[<Fact>]
let ``omitted counts default FAIL-CLOSED — a partial entry keeps the retarget blocked`` () =
    // Only the id and one clean count supplied; the blocking counts default to 1L,
    // the warning counts to 0L, identity to Missing.
    let json = """{ "retargets": [ { "id": "partial", "unresolvedThroughBridge": 0 } ] }"""
    match loadText json with
    | Ok m ->
        let ev = Map.find "partial" m
        Assert.Equal(0L, ev.UnresolvedThroughBridge)   // supplied
        Assert.Equal(1L, ev.BrokenOriginalParent)      // fail-closed default
        Assert.Equal(1L, ev.BridgeKeyDuplicates)       // fail-closed default
        Assert.Equal(1L, ev.BridgeKeyNulls)            // fail-closed default
        Assert.Equal(0L, ev.OrphanedBridgeRows)        // warning default
        Assert.Equal(0L, ev.PayloadConflicts)          // warning default
        Assert.Equal(BridgeIdentityEvidence.Missing, ev.IdentityEvidence)
        // Applying this partial evidence leaves the retarget BLOCKED.
        Assert.False(cleared (BridgeRetargetBinding.applyEvidence ev structural))
    | Error errs -> Assert.Fail(sprintf "expected Ok, got %A" errs)

[<Fact>]
let ``a file with no retargets key is the empty map`` () =
    match loadText "{ }" with
    | Ok m -> Assert.True(Map.isEmpty m)
    | Error errs -> Assert.Fail(sprintf "expected Ok, got %A" errs)

[<Fact>]
let ``malformed JSON fails loud`` () =
    match loadText "{ not json" with
    | Ok _ -> Assert.Fail "expected a malformed-JSON error"
    | Error errs -> Assert.True(hasErrorCode "pipeline.config.bridgeRetargetEvidence.malformedJson" errs)

[<Fact>]
let ``a missing file fails loud`` () =
    let path = Path.Combine(Path.GetTempPath(), sprintf "absent_%s.json" (System.Guid.NewGuid().ToString("N")))
    match BridgeRetargetBinding.loadEvidence (Some { Path = path }) with
    | Ok _ -> Assert.Fail "expected a read-failed error"
    | Error errs -> Assert.True(hasErrorCode "pipeline.config.bridgeRetargetEvidence.readFailed" errs)

[<Fact>]
let ``a duplicate id fails loud`` () =
    let json = """{ "retargets": [ { "id": "dup" }, { "id": "dup" } ] }"""
    match loadText json with
    | Ok _ -> Assert.Fail "expected a duplicate-id error"
    | Error errs -> Assert.True(hasErrorCode "pipeline.config.bridgeRetargetEvidence.duplicateId" errs)

[<Fact>]
let ``an entry without an id fails loud`` () =
    let json = """{ "retargets": [ { "unresolvedThroughBridge": 0 } ] }"""
    match loadText json with
    | Ok _ -> Assert.Fail "expected an entry-missing-id error"
    | Error errs -> Assert.True(hasErrorCode "pipeline.config.bridgeRetargetEvidence.entryMissingId" errs)

[<Fact>]
let ``an unrecognized identity value fails loud`` () =
    let json = """{ "retargets": [ { "id": "x", "identityEvidence": "maybe" } ] }"""
    match loadText json with
    | Ok _ -> Assert.Fail "expected an identity-unrecognized error"
    | Error errs -> Assert.True(hasErrorCode "pipeline.config.bridgeRetargetEvidence.identityUnrecognized" errs)

[<Fact>]
let ``a negative count fails loud`` () =
    let json = """{ "retargets": [ { "id": "x", "bridgeKeyNulls": -1 } ] }"""
    match loadText json with
    | Ok _ -> Assert.Fail "expected a negative-count error"
    | Error errs -> Assert.True(hasErrorCode "pipeline.config.bridgeRetargetEvidence.negativeCount" errs)

[<Fact>]
let ``a non-number count fails loud`` () =
    let json = """{ "retargets": [ { "id": "x", "bridgeKeyNulls": "lots" } ] }"""
    match loadText json with
    | Ok _ -> Assert.Fail "expected a count-not-number error"
    | Error errs -> Assert.True(hasErrorCode "pipeline.config.bridgeRetargetEvidence.countNotNumber" errs)

[<Fact>]
let ``identity is case-insensitive`` () =
    let json = """{ "retargets": [ { "id": "x", "identityEvidence": "AMBIGUOUS" } ] }"""
    match loadText json with
    | Ok m -> Assert.Equal(BridgeIdentityEvidence.Ambiguous, (Map.find "x" m).IdentityEvidence)
    | Error errs -> Assert.Fail(sprintf "expected Ok, got %A" errs)
