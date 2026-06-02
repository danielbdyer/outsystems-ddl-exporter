module Projection.Tests.ApprovalWorkflowTests

// H-086: ApprovalWorkflow — approval records for versioned policies.

open System
open Xunit
open Projection.Core

/// Slice 0 (2026-06-02): Core retired the `*Now` wrappers; tests use a fixed
/// `testTime` for determinism. Per the Episode.fs "boundary-supplied at"
/// pattern — wall-clock impurity stays at the boundary.
let private testTime : System.DateTimeOffset =
    System.DateTimeOffset(2026, 1, 1, 0, 0, 0, System.TimeSpan.Zero)

let private sampleVersion = VersionedPolicy.digestOf Policy.empty

// ---------------------------------------------------------------------------
// pending
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-086: pending creates a Pending record`` () =
    let record = ApprovalWorkflow.pending testTime sampleVersion
    Assert.Equal(Pending, record.Decision)
    Assert.Equal(sampleVersion, record.PolicyVersion)
    Assert.Equal(None, record.ApprovedBy)
    Assert.Equal(None, record.Rationale)

[<Fact>]
let ``H-086: pendingFor uses the VersionedPolicy's content Digest as the approval anchor`` () =
    let vp = VersionedPolicy.create testTime Policy.empty None
    let record = ApprovalWorkflow.pendingFor testTime vp
    Assert.Equal(vp.Digest, record.PolicyVersion)
    Assert.Equal(Pending, record.Decision)

// ---------------------------------------------------------------------------
// approve
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-086: approve transitions decision to Approved`` () =
    let at = DateTimeOffset.Parse "2026-05-22T12:00:00Z"
    let record =
        ApprovalWorkflow.pending testTime sampleVersion
        |> ApprovalWorkflow.approve "alice@example.com" (Some "LGTM") at
    Assert.Equal(Approved, record.Decision)
    Assert.Equal(Some "alice@example.com", record.ApprovedBy)
    Assert.Equal(Some "LGTM", record.Rationale)
    Assert.Equal(at, record.At)

[<Fact>]
let ``H-086: approve preserves PolicyVersion`` () =
    let at = DateTimeOffset.UtcNow
    let record =
        ApprovalWorkflow.pending testTime sampleVersion
        |> ApprovalWorkflow.approve "reviewer" None at
    Assert.Equal(sampleVersion, record.PolicyVersion)

[<Fact>]
let ``H-086: approve stamps the supplied timestamp`` () =
    let record =
        ApprovalWorkflow.pending testTime sampleVersion
        |> ApprovalWorkflow.approve "reviewer" None testTime
    Assert.Equal(testTime, record.At)

// ---------------------------------------------------------------------------
// reject
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-086: reject transitions decision to Rejected`` () =
    let at = DateTimeOffset.Parse "2026-05-22T12:00:00Z"
    let record =
        ApprovalWorkflow.pending testTime sampleVersion
        |> ApprovalWorkflow.reject "bob@example.com" (Some "needs rework") at
    Assert.Equal(Rejected, record.Decision)
    Assert.Equal(Some "bob@example.com", record.ApprovedBy)
    Assert.Equal(Some "needs rework", record.Rationale)

// ---------------------------------------------------------------------------
// isApproved / isPending
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-086: isPending true for new record`` () =
    let record = ApprovalWorkflow.pending testTime sampleVersion
    Assert.True(ApprovalWorkflow.isPending record)
    Assert.False(ApprovalWorkflow.isApproved record)

[<Fact>]
let ``H-086: isApproved true after approve`` () =
    let record =
        ApprovalWorkflow.pending testTime sampleVersion
        |> ApprovalWorkflow.approve "reviewer" None testTime
    Assert.True(ApprovalWorkflow.isApproved record)
    Assert.False(ApprovalWorkflow.isPending record)

[<Fact>]
let ``H-086: isApproved false after reject`` () =
    let record =
        ApprovalWorkflow.pending testTime sampleVersion
        |> ApprovalWorkflow.reject "reviewer" None testTime
    Assert.False(ApprovalWorkflow.isApproved record)
    Assert.False(ApprovalWorkflow.isPending record)

// ---------------------------------------------------------------------------
// Round-trip: pending → approve → reject re-applies correctly
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-086: decision can be overridden from Approved to Rejected`` () =
    let at = DateTimeOffset.Parse "2026-05-22T13:00:00Z"
    let record =
        ApprovalWorkflow.pending testTime sampleVersion
        |> ApprovalWorkflow.approve "first-reviewer" None testTime
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
        ApprovalWorkflow.pending testTime sampleVersion
        |> ApprovalWorkflow.approve "reviewer" None testTime
    let registry =
        ApprovalRegistry.empty |> ApprovalRegistry.record record
    Assert.Equal(Some record, ApprovalRegistry.tryFind sampleVersion registry)

[<Fact>]
let ``H-086 registry: isApprovedFor returns true after recording an Approved record`` () =
    let record =
        ApprovalWorkflow.pending testTime sampleVersion
        |> ApprovalWorkflow.approve "reviewer" None testTime
    let registry =
        ApprovalRegistry.empty |> ApprovalRegistry.record record
    Assert.True(ApprovalRegistry.isApprovedFor sampleVersion registry)

[<Fact>]
let ``H-086 registry: isRejectedFor returns true after recording a Rejected record`` () =
    let record =
        ApprovalWorkflow.pending testTime sampleVersion
        |> ApprovalWorkflow.reject "reviewer" None testTime
    let registry =
        ApprovalRegistry.empty |> ApprovalRegistry.record record
    Assert.True(ApprovalRegistry.isRejectedFor sampleVersion registry)
    Assert.False(ApprovalRegistry.isApprovedFor sampleVersion registry)

[<Fact>]
let ``H-086 registry: isSuppressed gates SuggestedConfig surface — true iff rejected`` () =
    let approvedRecord =
        ApprovalWorkflow.pending testTime sampleVersion
        |> ApprovalWorkflow.approve "reviewer" None testTime
    let approvedRegistry =
        ApprovalRegistry.empty |> ApprovalRegistry.record approvedRecord
    Assert.False(ApprovalRegistry.isSuppressed sampleVersion approvedRegistry)
    let rejectedRecord =
        ApprovalWorkflow.pending testTime sampleVersion
        |> ApprovalWorkflow.reject "reviewer" (Some "do not surface") testTime
    let rejectedRegistry =
        ApprovalRegistry.empty |> ApprovalRegistry.record rejectedRecord
    Assert.True(ApprovalRegistry.isSuppressed sampleVersion rejectedRegistry)

[<Fact>]
let ``H-086 registry: last-write-wins on the same PolicyVersion`` () =
    let first =
        ApprovalWorkflow.pending testTime sampleVersion
        |> ApprovalWorkflow.approve "first" None testTime
    let second =
        ApprovalWorkflow.pending testTime sampleVersion
        |> ApprovalWorkflow.reject "second" (Some "revoking") testTime
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
    let approved = ApprovalWorkflow.pending testTime v1 |> ApprovalWorkflow.approve "a" None testTime
    let rejected = ApprovalWorkflow.pending testTime v2 |> ApprovalWorkflow.reject "b" None testTime
    let registry =
        ApprovalRegistry.empty
        |> ApprovalRegistry.record approved
        |> ApprovalRegistry.record rejected
    Assert.Single(ApprovalRegistry.approvedRecords registry) |> ignore
    Assert.Single(ApprovalRegistry.rejectedRecords registry) |> ignore
