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
    /// `at` is the initiation timestamp — boundary-supplied per the
    /// `Episode.fs` canonical pattern; Core holds no clock.
    let pending (at: DateTimeOffset) (policyVersion: string) : ApprovalRecord =
        { PolicyVersion = policyVersion
          Decision      = Pending
          ApprovedBy    = None
          At            = at
          Rationale     = None }

    /// Create a `Pending` approval record for a `VersionedPolicy`. Uses
    /// the content `Digest` (stable identity) as the approval anchor; the
    /// `SemVer` is human-facing and may bump across rationale changes
    /// that do not move the digest.
    let pendingFor (at: DateTimeOffset) (vp: VersionedPolicy) : ApprovalRecord =
        pending at vp.Digest

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

    // Slice 0 (2026-06-02): `approveNow` / `rejectNow` retired. They captured
    // `DateTimeOffset.UtcNow` inside Core (analyzer gap pre-Slice-0). `pending`
    // now takes `at` as a boundary-supplied parameter; CLI and Pipeline supply
    // `DateTimeOffset.UtcNow` at the call site; tests pass a per-file
    // `testTime` constant for determinism. Per the `Episode.fs` canonical
    // shape — Core holds no clock.

    /// True iff the record carries an `Approved` decision.
    let isApproved (record: ApprovalRecord) : bool =
        record.Decision = Approved

    /// True iff the record carries a `Pending` decision (no review yet).
    let isPending (record: ApprovalRecord) : bool =
        record.Decision = Pending


/// Indexed store of approval records, keyed by `PolicyVersion` (the
/// `VersionedPolicy.Digest` value).
///
/// **Loop closure (H-086 audit remediation).** The HORIZON spec required
/// "rejected suggestions are preserved as `Skip`-equivalent entries in
/// the policy: the diagnostic fires but `SuggestedConfig` is suppressed
/// for this key." The full closed loop needs three pieces:
///   1. A persistent store of approval records (this type — in-memory
///      for v0, suitable for piping to/from JSON for v1).
///   2. A query API the SuggestedConfig emitter consults before
///      surfacing a hint (`isSuppressed digest registry`).
///   3. A policy-file writer that re-emits the operator policy with the
///      approved/rejected decisions baked in.
///
/// This module ships (1) + (2). Piece (3) is a downstream policy-file
/// persistence concern (Config.fs writer; not part of the algebraic core).
type ApprovalRegistry = {
    /// Approval records indexed by `VersionedPolicy.Digest`. One entry
    /// per policy version observed; later decisions for the same digest
    /// supersede earlier ones (last-write-wins).
    ByDigest : Map<string, ApprovalRecord>
}

[<RequireQualifiedAccess>]
module ApprovalRegistry =

    /// An empty registry (no approval records recorded).
    let empty : ApprovalRegistry = { ByDigest = Map.empty }

    /// Insert or update an approval record. Keyed by the record's
    /// `PolicyVersion` (the VersionedPolicy digest). Last write wins.
    let record (rec_: ApprovalRecord) (registry: ApprovalRegistry) : ApprovalRegistry =
        { ByDigest = Map.add rec_.PolicyVersion rec_ registry.ByDigest }

    /// Look up the approval record (if any) for the given policy digest.
    let tryFind (policyDigest: string) (registry: ApprovalRegistry) : ApprovalRecord option =
        Map.tryFind policyDigest registry.ByDigest

    /// True iff the registry has a record marking the given policy digest
    /// as `Approved`. The SuggestedConfig surface consults this to gate
    /// whether to surface hints for already-approved policies.
    let isApprovedFor (policyDigest: string) (registry: ApprovalRegistry) : bool =
        match tryFind policyDigest registry with
        | Some r -> ApprovalWorkflow.isApproved r
        | None   -> false

    /// True iff the registry has a record marking the given policy digest
    /// as `Rejected`. The SuggestedConfig surface consults this to gate
    /// whether to *suppress* hints (the HORIZON "Skip-equivalent" behavior
    /// for rejected suggestions).
    let isRejectedFor (policyDigest: string) (registry: ApprovalRegistry) : bool =
        match tryFind policyDigest registry with
        | Some r -> r.Decision = Rejected
        | None   -> false

    /// **The HORIZON `Skip`-equivalent gate.** True when downstream
    /// SuggestedConfig emission should be suppressed for the given
    /// policy digest — currently equivalent to "the operator rejected
    /// this version." Operators can re-approve by recording a new
    /// `Approved` for the same digest (last-write-wins).
    let isSuppressed (policyDigest: string) (registry: ApprovalRegistry) : bool =
        isRejectedFor policyDigest registry

    /// All `Approved` records in the registry, sorted by their decision
    /// timestamp ascending (chronological order). For audit / display.
    let approvedRecords (registry: ApprovalRegistry) : ApprovalRecord list =
        registry.ByDigest
        |> Map.values
        |> Seq.filter ApprovalWorkflow.isApproved
        |> Seq.sortBy (fun r -> r.At)
        |> Seq.toList

    /// All `Rejected` records in the registry, sorted by their decision
    /// timestamp ascending.
    let rejectedRecords (registry: ApprovalRegistry) : ApprovalRecord list =
        registry.ByDigest
        |> Map.values
        |> Seq.filter (fun r -> r.Decision = Rejected)
        |> Seq.sortBy (fun r -> r.At)
        |> Seq.toList
