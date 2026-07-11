// R4-APR (FORMAL_METHODS.md §2/§4): bounded model check of the
// approval workflow + registry (Projection.Core/ApprovalWorkflow.fs;
// H-086).
//
// The F# machine: per policy digest, an ApprovalState — Pending, or
// Approved(by, rationale), or Rejected(by, rationale) — held in an
// ApprovalRegistry keyed by digest with LAST-WRITE-WINS semantics
// (later records for the same digest supersede earlier ones; a
// rejected digest can be re-approved). `isSuppressed` — the HORIZON
// Skip-equivalent gate on SuggestedConfig emission — is defined as
// "currently Rejected", nothing more.
//
// The model: verdict membership per digest as three disjoint var sets;
// the reviewer attribution as a var map populated exactly by decision
// events. The F# DU's structural guarantee — Pending carries no
// reviewer (the pre-slice-2 illegal state) — becomes a checked
// temporal invariant of the transition system.
//
// Rung-4 boundary: bounded model checking at the scopes below.

module Approval

// Reviewer attribution — populated by approve/reject, cleared by a
// pending record. (Rationale is free text; elided — it rides with the
// reviewer in the same variant and adds no transition structure.)
sig Digest {
  var decidedBy : lone Reviewer
}
sig Reviewer {}

// Verdict membership. A digest in none of the three sets has never
// been recorded (no registry entry).
var sig Pend in Digest {}
var sig Appr in Digest {}
var sig Rej  in Digest {}

// ---------------------------------------------------------------------------
// Transitions — ApprovalRegistry.record with each ApprovalWorkflow
// constructor. Last-write-wins: every event unconditionally overwrites
// the digest's entry.
// ---------------------------------------------------------------------------

pred recordPending[d: Digest] {
  Pend' = Pend + d
  Appr' = Appr - d
  Rej'  = Rej  - d
  decidedBy' = decidedBy - (d -> Reviewer)
}

pred approve[d: Digest, r: Reviewer] {
  Appr' = Appr + d
  Pend' = Pend - d
  Rej'  = Rej  - d
  decidedBy' = decidedBy ++ (d -> r)
}

pred reject[d: Digest, r: Reviewer] {
  Rej'  = Rej  + d
  Pend' = Pend - d
  Appr' = Appr - d
  decidedBy' = decidedBy ++ (d -> r)
}

pred stutter {
  Pend' = Pend and Appr' = Appr and Rej' = Rej and decidedBy' = decidedBy
}

fact traces {
  no Pend and no Appr and no Rej and no decidedBy  // empty registry
  always {
    stutter
    or (some d: Digest | recordPending[d])
    or (some d: Digest, r: Reviewer | approve[d, r])
    or (some d: Digest, r: Reviewer | reject[d, r])
  }
}

// isSuppressed — definitionally "currently Rejected" (the F# gate).
fun suppressed : set Digest { Rej }

// ---------------------------------------------------------------------------
// Laws.
// ---------------------------------------------------------------------------

// The registry entry is a function: a digest is never in two verdict
// states at once (the F# Map<digest, record> shape).
check VerdictIsExclusive {
  always (no (Pend & Appr) and no (Pend & Rej) and no (Appr & Rej))
} for 4 but 1..12 steps expect 0

// The slice-2 structural law, temporal form: a Pending digest never
// carries reviewer attribution — the pre-refactor illegal state
// { Decision = Pending; ApprovedBy = Some _ } is unreachable.
check PendingCarriesNoReviewer {
  always (no (Pend <: decidedBy))
} for 4 but 1..12 steps expect 0

// Every decided digest carries its reviewer — Approved/Rejected
// package `by` inside the variant; attribution cannot be lost while
// the decision stands.
check DecidedCarriesReviewer {
  always (all d: Appr + Rej | one d.decidedBy)
} for 4 but 1..12 steps expect 0

// No silent transitions: a digest's verdict state changes only when a
// record event for THAT digest occurs — the registry is an audit
// surface; entries never drift.
check NoSilentTransitions {
  always (all d: Digest |
    (not ((d in Pend) iff (d in Pend'))
      or not ((d in Appr) iff (d in Appr'))
      or not ((d in Rej) iff (d in Rej')))
        implies (recordPending[d] or some r: Reviewer | approve[d, r] or reject[d, r]))
} for 4 but 1..12 steps expect 0

// Suppression is exactly rejection — the gate can never suppress an
// approved or pending digest.
check SuppressionIsRejection {
  always (suppressed = Rej and no (suppressed & Appr) and no (suppressed & Pend))
} for 4 but 1..12 steps expect 0

// ---------------------------------------------------------------------------
// Sanity + the HORIZON loop (run, expect 1).
// ---------------------------------------------------------------------------

// Last-write-wins makes suppression escapable: a digest can be
// rejected (suppressed) and later re-approved (gate lifts). This is
// the operator's re-approval loop, reachable by construction.
run SuppressionIsEscapable {
  some d: Digest | eventually (d in suppressed and eventually d in Appr)
} for 3 but 1..10 steps expect 1

// A full lifecycle — pending, rejected, re-approved — is representable.
run FullReviewLoop {
  some d: Digest | eventually (d in Pend and eventually (d in Rej and eventually d in Appr))
} for 3 but 1..10 steps expect 1
