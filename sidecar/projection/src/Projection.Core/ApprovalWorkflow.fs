namespace Projection.Core

open System

/// The outcome of a policy approval review (H-086).
type ApprovalDecision =
    /// The reviewer accepted this policy version for use in production.
    | Approved
    /// The reviewer explicitly rejected this policy version.
    | Rejected
    /// No decision has been recorded yet.
    | Pending


/// A stamped record of a reviewer's decision on a versioned policy (H-086).
///
/// `PolicyVersion` links this record to the `VersionedPolicy.Version` digest
/// of the policy under review. The link is by opaque string so `ApprovalRecord`
/// can be stored and compared without carrying the full `Policy` value.
///
/// `ApprovedBy` is `None` for `Pending` records (no reviewer yet). For
/// `Approved` / `Rejected` records it carries the reviewer's identifier
/// (email, username, or any string the operator treats as an identity anchor).
type ApprovalRecord = {
    PolicyVersion : string
    Decision      : ApprovalDecision
    ApprovedBy    : string option
    At            : DateTimeOffset
    Rationale     : string option
}


/// Construction and state transitions for `ApprovalRecord` (H-086).
[<RequireQualifiedAccess>]
module ApprovalWorkflow =

    /// Create a `Pending` approval record for the given policy version.
    /// Records the current UTC time as the initiation timestamp.
    let pending (policyVersion: string) : ApprovalRecord =
        { PolicyVersion = policyVersion
          Decision      = Pending
          ApprovedBy    = None
          At            = DateTimeOffset.UtcNow
          Rationale     = None }

    /// Create a `Pending` approval record for a `VersionedPolicy`. Convenience
    /// wrapper that extracts the version string automatically.
    let pendingFor (vp: VersionedPolicy) : ApprovalRecord =
        pending vp.Version

    /// Transition an approval record to `Approved`. Updates `ApprovedBy`,
    /// `Rationale`, and `At` (the decision timestamp). The original
    /// `PolicyVersion` is preserved.
    let approve
        (approver: string)
        (rationale: string option)
        (at: DateTimeOffset)
        (record: ApprovalRecord)
        : ApprovalRecord =
        { record with
            Decision   = Approved
            ApprovedBy = Some approver
            At         = at
            Rationale  = rationale }

    /// `approve` variant that captures `DateTimeOffset.UtcNow` as the
    /// decision timestamp. Convenience for interactive / CLI use.
    let approveNow
        (approver: string)
        (rationale: string option)
        (record: ApprovalRecord)
        : ApprovalRecord =
        approve approver rationale DateTimeOffset.UtcNow record

    /// Transition an approval record to `Rejected`. Updates `ApprovedBy`,
    /// `Rationale`, and `At` (the rejection timestamp).
    let reject
        (approver: string)
        (rationale: string option)
        (at: DateTimeOffset)
        (record: ApprovalRecord)
        : ApprovalRecord =
        { record with
            Decision   = Rejected
            ApprovedBy = Some approver
            At         = at
            Rationale  = rationale }

    /// `reject` variant that captures `DateTimeOffset.UtcNow`.
    let rejectNow
        (approver: string)
        (rationale: string option)
        (record: ApprovalRecord)
        : ApprovalRecord =
        reject approver rationale DateTimeOffset.UtcNow record

    /// True iff the record carries an `Approved` decision.
    let isApproved (record: ApprovalRecord) : bool =
        record.Decision = Approved

    /// True iff the record carries a `Pending` decision (no review yet).
    let isPending (record: ApprovalRecord) : bool =
        record.Decision = Pending
