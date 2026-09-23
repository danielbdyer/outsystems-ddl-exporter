module Projection.Tests.SuggestConfigGatingTests

open Xunit
open Projection.Core
open Projection.Targets.OperationalDiagnostics

// Wave-3 slice 3.2 — SuggestConfig approval gating. `emitWith` consults the R6
// ApprovalRegistry: a REJECTED policy digest suppresses that policy's
// suggested-config hints (V2 stops nudging toward edits derived from a policy
// version the operator turned down). Approved / pending / unknown digests do
// not suppress. `emit` (the empty-registry wrapper) is byte-identical to the
// pre-gating output. The gating is policy-LEVEL (a SuggestedConfig carries no
// policy-version identity, so per-suggestion suppression is not modeled).

/// Slice 0 (2026-06-02): Core retired the `*Now` wrappers; tests use a fixed
/// `testTime` for determinism. Per the Episode.fs "boundary-supplied at"
/// pattern — wall-clock impurity stays at the boundary.
let private testTime : System.DateTimeOffset =
    System.DateTimeOffset(2026, 1, 1, 0, 0, 0, System.TimeSpan.Zero)

let private suggestingDiagnostic () : DiagnosticEntry =
    let sc =
        match SuggestedConfig.create "tightening.nullBudget" "0.05" with
        | Ok s -> s | Error e -> failwithf "%A" e
    { DiagnosticEntry.create "test" DiagnosticSeverity.Info "test.suggest" "hint"
      with SuggestedConfig = Some sc }

let private editCount (node: System.Text.Json.Nodes.JsonNode) : int =
    match node.["suggestedEdits"] with
    | null -> -1
    | edits -> edits.AsArray().Count

let private rejectedRegistry (digest: string) : ApprovalRegistry =
    ApprovalRegistry.empty
    |> ApprovalRegistry.record (ApprovalWorkflow.pending testTime digest |> ApprovalWorkflow.reject "alice" (Some "no") testTime)

let private approvedRegistry (digest: string) : ApprovalRegistry =
    ApprovalRegistry.empty
    |> ApprovalRegistry.record (ApprovalWorkflow.pending testTime digest |> ApprovalWorkflow.approve "alice" (Some "ok") testTime)

[<Fact>]
let ``3.2: empty registry shows suggestions (no gating)`` () =
    let node = SuggestConfigEmitter.emitWith ApprovalRegistry.empty "digestX" [ suggestingDiagnostic () ]
    Assert.Equal(1, editCount node)

[<Fact>]
let ``3.2: a rejected policy digest suppresses its suggested-config hints`` () =
    let node = SuggestConfigEmitter.emitWith (rejectedRegistry "digestX") "digestX" [ suggestingDiagnostic () ]
    Assert.Equal(0, editCount node)

[<Fact>]
let ``3.2: an approved policy digest does NOT suppress (only rejection suppresses)`` () =
    let node = SuggestConfigEmitter.emitWith (approvedRegistry "digestX") "digestX" [ suggestingDiagnostic () ]
    Assert.Equal(1, editCount node)

[<Fact>]
let ``3.2: a rejection of a DIFFERENT digest does not suppress this policy`` () =
    let node = SuggestConfigEmitter.emitWith (rejectedRegistry "otherDigest") "digestX" [ suggestingDiagnostic () ]
    Assert.Equal(1, editCount node)

[<Fact>]
let ``3.2: emit equals emitWith empty (byte-identical default wrapper)`` () =
    let diags = [ suggestingDiagnostic () ]
    let viaWrapper = (SuggestConfigEmitter.emit diags).ToJsonString()
    let viaEmpty = (SuggestConfigEmitter.emitWith ApprovalRegistry.empty "" diags).ToJsonString()
    Assert.Equal(viaWrapper, viaEmpty)

// ---------------------------------------------------------------------------
// align-II.3 (a4-7) — per-proposal suppression: rejecting one nudge while
// its sibling surfaces. The proposal's identity derives from (Path, Value).
// ---------------------------------------------------------------------------

let private secondSuggestingDiagnostic () : DiagnosticEntry =
    let sc =
        match SuggestedConfig.create "tightening.allowMandatoryRelaxation" "true" with
        | Ok s -> s | Error e -> failwithf "%A" e
    { DiagnosticEntry.create "test" DiagnosticSeverity.Info "test.suggest2" "hint2"
      with SuggestedConfig = Some sc }

[<Fact>]
let ``align-II.3: SuggestedConfig.proposalKey is deterministic and distinct per (Path, Value)`` () =
    let a = match SuggestedConfig.create "p.one" "1" with Ok s -> s | Error e -> failwithf "%A" e
    let a2 = match SuggestedConfig.createWithNote "p.one" "1" "different note" with Ok s -> s | Error e -> failwithf "%A" e
    let b = match SuggestedConfig.create "p.one" "2" with Ok s -> s | Error e -> failwithf "%A" e
    Assert.Equal(SuggestedConfig.proposalKey a, SuggestedConfig.proposalKey a2)   // note is not identity
    Assert.NotEqual<string>(SuggestedConfig.proposalKey a, SuggestedConfig.proposalKey b)

[<Fact>]
let ``align-II.3: a rejected proposal is suppressed while its sibling surfaces (per-proposal grain)`` () =
    let digest = "digestY"
    let rejectedKey =
        match SuggestedConfig.create "tightening.nullBudget" "0.05" with
        | Ok s -> SuggestedConfig.proposalKey s | Error e -> failwithf "%A" e
    let registry =
        ApprovalRegistry.empty
        |> ApprovalRegistry.recordProposal
            rejectedKey
            (ApprovalWorkflow.pending testTime digest |> ApprovalWorkflow.reject "alice" (Some "not this one") testTime)
    let node =
        SuggestConfigEmitter.emitWith registry digest
            [ suggestingDiagnostic (); secondSuggestingDiagnostic () ]
    Assert.Equal(1, editCount node)
    // Whole-policy rejection still subsumes every proposal.
    Assert.True(ApprovalRegistry.isProposalSuppressed digest rejectedKey registry)
    Assert.False(ApprovalRegistry.isProposalSuppressed digest "another-key" registry)

[<Fact>]
let ``align-II.3: whole-policy rejection subsumes every proposal (the coarse grain still holds)`` () =
    let digest = "digestZ"
    let registry = rejectedRegistry digest
    let anyKey =
        match SuggestedConfig.create "any.path" "1" with
        | Ok s -> SuggestedConfig.proposalKey s | Error e -> failwithf "%A" e
    Assert.True(ApprovalRegistry.isProposalSuppressed digest anyKey registry)
    let node = SuggestConfigEmitter.emitWith registry digest [ suggestingDiagnostic () ]
    Assert.Equal(0, editCount node)
