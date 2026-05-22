module Projection.Tests.ApprovalWorkflowTests

// H-086: ApprovalWorkflow — approval records for versioned policies.

open System
open Xunit
open Projection.Core

let private sampleVersion = VersionedPolicy.digestOf Policy.empty

// ---------------------------------------------------------------------------
// pending
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-086: pending creates a Pending record`` () =
    let record = ApprovalWorkflow.pending sampleVersion
    Assert.Equal(Pending, record.Decision)
    Assert.Equal(sampleVersion, record.PolicyVersion)
    Assert.Equal(None, record.ApprovedBy)
    Assert.Equal(None, record.Rationale)

[<Fact>]
let ``H-086: pendingFor uses the VersionedPolicy's content Digest as the approval anchor`` () =
    let vp = VersionedPolicy.now Policy.empty None
    let record = ApprovalWorkflow.pendingFor vp
    Assert.Equal(vp.Digest, record.PolicyVersion)
    Assert.Equal(Pending, record.Decision)

// ---------------------------------------------------------------------------
// approve
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-086: approve transitions decision to Approved`` () =
    let at = DateTimeOffset.Parse "2026-05-22T12:00:00Z"
    let record =
        ApprovalWorkflow.pending sampleVersion
        |> ApprovalWorkflow.approve "alice@example.com" (Some "LGTM") at
    Assert.Equal(Approved, record.Decision)
    Assert.Equal(Some "alice@example.com", record.ApprovedBy)
    Assert.Equal(Some "LGTM", record.Rationale)
    Assert.Equal(at, record.At)

[<Fact>]
let ``H-086: approve preserves PolicyVersion`` () =
    let at = DateTimeOffset.UtcNow
    let record =
        ApprovalWorkflow.pending sampleVersion
        |> ApprovalWorkflow.approve "reviewer" None at
    Assert.Equal(sampleVersion, record.PolicyVersion)

[<Fact>]
let ``H-086: approveNow captures a recent timestamp`` () =
    let before = DateTimeOffset.UtcNow.AddSeconds -1.0
    let record =
        ApprovalWorkflow.pending sampleVersion
        |> ApprovalWorkflow.approveNow "reviewer" None
    let after = DateTimeOffset.UtcNow.AddSeconds 1.0
    Assert.True(record.At >= before)
    Assert.True(record.At <= after)

// ---------------------------------------------------------------------------
// reject
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-086: reject transitions decision to Rejected`` () =
    let at = DateTimeOffset.Parse "2026-05-22T12:00:00Z"
    let record =
        ApprovalWorkflow.pending sampleVersion
        |> ApprovalWorkflow.reject "bob@example.com" (Some "needs rework") at
    Assert.Equal(Rejected, record.Decision)
    Assert.Equal(Some "bob@example.com", record.ApprovedBy)
    Assert.Equal(Some "needs rework", record.Rationale)

// ---------------------------------------------------------------------------
// isApproved / isPending
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-086: isPending true for new record`` () =
    let record = ApprovalWorkflow.pending sampleVersion
    Assert.True(ApprovalWorkflow.isPending record)
    Assert.False(ApprovalWorkflow.isApproved record)

[<Fact>]
let ``H-086: isApproved true after approve`` () =
    let record =
        ApprovalWorkflow.pending sampleVersion
        |> ApprovalWorkflow.approveNow "reviewer" None
    Assert.True(ApprovalWorkflow.isApproved record)
    Assert.False(ApprovalWorkflow.isPending record)

[<Fact>]
let ``H-086: isApproved false after reject`` () =
    let record =
        ApprovalWorkflow.pending sampleVersion
        |> ApprovalWorkflow.rejectNow "reviewer" None
    Assert.False(ApprovalWorkflow.isApproved record)
    Assert.False(ApprovalWorkflow.isPending record)

// ---------------------------------------------------------------------------
// Round-trip: pending → approve → reject re-applies correctly
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-086: decision can be overridden from Approved to Rejected`` () =
    let at = DateTimeOffset.Parse "2026-05-22T13:00:00Z"
    let record =
        ApprovalWorkflow.pending sampleVersion
        |> ApprovalWorkflow.approveNow "first-reviewer" None
        |> ApprovalWorkflow.reject "second-reviewer" (Some "reverted") at
    Assert.Equal(Rejected, record.Decision)
    Assert.Equal(Some "second-reviewer", record.ApprovedBy)

// ---------------------------------------------------------------------------
// H-086 audit remediation: ApprovalRegistry — indexed store of approval
// records (the loop-closure surface).
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-086 registry: empty registry has no records`` () =
    Assert.Equal<Map<string, ApprovalRecord>>(Map.empty, ApprovalRegistry.empty.ByDigest)
    Assert.False(ApprovalRegistry.isApprovedFor sampleVersion ApprovalRegistry.empty)
    Assert.False(ApprovalRegistry.isRejectedFor sampleVersion ApprovalRegistry.empty)

[<Fact>]
let ``H-086 registry: record inserts an approval record keyed by PolicyVersion`` () =
    let record =
        ApprovalWorkflow.pending sampleVersion
        |> ApprovalWorkflow.approveNow "reviewer" None
    let registry =
        ApprovalRegistry.empty |> ApprovalRegistry.record record
    Assert.Equal(Some record, ApprovalRegistry.tryFind sampleVersion registry)

[<Fact>]
let ``H-086 registry: isApprovedFor returns true after recording an Approved record`` () =
    let record =
        ApprovalWorkflow.pending sampleVersion
        |> ApprovalWorkflow.approveNow "reviewer" None
    let registry =
        ApprovalRegistry.empty |> ApprovalRegistry.record record
    Assert.True(ApprovalRegistry.isApprovedFor sampleVersion registry)

[<Fact>]
let ``H-086 registry: isRejectedFor returns true after recording a Rejected record`` () =
    let record =
        ApprovalWorkflow.pending sampleVersion
        |> ApprovalWorkflow.rejectNow "reviewer" None
    let registry =
        ApprovalRegistry.empty |> ApprovalRegistry.record record
    Assert.True(ApprovalRegistry.isRejectedFor sampleVersion registry)
    Assert.False(ApprovalRegistry.isApprovedFor sampleVersion registry)

[<Fact>]
let ``H-086 registry: isSuppressed gates SuggestedConfig surface — true iff rejected`` () =
    let approvedRecord =
        ApprovalWorkflow.pending sampleVersion
        |> ApprovalWorkflow.approveNow "reviewer" None
    let approvedRegistry =
        ApprovalRegistry.empty |> ApprovalRegistry.record approvedRecord
    Assert.False(ApprovalRegistry.isSuppressed sampleVersion approvedRegistry)
    let rejectedRecord =
        ApprovalWorkflow.pending sampleVersion
        |> ApprovalWorkflow.rejectNow "reviewer" (Some "do not surface")
    let rejectedRegistry =
        ApprovalRegistry.empty |> ApprovalRegistry.record rejectedRecord
    Assert.True(ApprovalRegistry.isSuppressed sampleVersion rejectedRegistry)

[<Fact>]
let ``H-086 registry: last-write-wins on the same PolicyVersion`` () =
    let first =
        ApprovalWorkflow.pending sampleVersion
        |> ApprovalWorkflow.approveNow "first" None
    let second =
        ApprovalWorkflow.pending sampleVersion
        |> ApprovalWorkflow.rejectNow "second" (Some "revoking")
    let registry =
        ApprovalRegistry.empty
        |> ApprovalRegistry.record first
        |> ApprovalRegistry.record second
    Assert.True(ApprovalRegistry.isRejectedFor sampleVersion registry)
    Assert.False(ApprovalRegistry.isApprovedFor sampleVersion registry)

[<Fact>]
let ``H-086 registry: approvedRecords and rejectedRecords project correctly`` () =
    let v1 = VersionedPolicy.digestOf Policy.empty
    let v2 = VersionedPolicy.digestOf { Policy.empty with Insertion = InsertNew }
    let approved = ApprovalWorkflow.pending v1 |> ApprovalWorkflow.approveNow "a" None
    let rejected = ApprovalWorkflow.pending v2 |> ApprovalWorkflow.rejectNow "b" None
    let registry =
        ApprovalRegistry.empty
        |> ApprovalRegistry.record approved
        |> ApprovalRegistry.record rejected
    Assert.Single(ApprovalRegistry.approvedRecords registry) |> ignore
    Assert.Single(ApprovalRegistry.rejectedRecords registry) |> ignore
