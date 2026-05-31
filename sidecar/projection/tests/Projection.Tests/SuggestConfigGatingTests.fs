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
    |> ApprovalRegistry.record (ApprovalWorkflow.pending digest |> ApprovalWorkflow.rejectNow "alice" (Some "no"))

let private approvedRegistry (digest: string) : ApprovalRegistry =
    ApprovalRegistry.empty
    |> ApprovalRegistry.record (ApprovalWorkflow.pending digest |> ApprovalWorkflow.approveNow "alice" (Some "ok"))

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
