namespace Projection.Pipeline

open Projection.Core

/// Axis-level comparison between two `Policy` values (H-033).
///
/// Each `PolicyAxisDiff<'a>` carries the before and after value for one
/// axis, plus a `Changed` flag. The aggregate `PolicyDiff` record collects
/// all five axes and provides `AnyChanged` for quick triage.
type PolicyAxisDiff<'a> = {
    Before  : 'a
    After   : 'a
    Changed : bool
}

/// Five-axis structural diff between two policies (H-033).
type PolicyDiff = {
    Selection    : PolicyAxisDiff<SelectionPolicy>
    Emission     : PolicyAxisDiff<EmissionPolicy>
    Insertion    : PolicyAxisDiff<InsertionPolicy>
    Tightening   : PolicyAxisDiff<TighteningPolicy>
    UserMatching : PolicyAxisDiff<UserMatchingStrategy>
    /// True iff at least one axis changed between `before` and `after`.
    AnyChanged   : bool
}


/// Policy diff construction and the `diffPolicy` lineage-bearing comparator (H-033).
[<RequireQualifiedAccess>]
module PolicyDiff =

    let private axisOf (before: 'a) (after: 'a) : PolicyAxisDiff<'a> =
        { Before = before; After = after; Changed = before <> after }

    /// Compute the five-axis structural diff between two policies. Pure;
    /// no side effects.
    let compare (before: Policy) (after: Policy) : PolicyDiff =
        let selection    = axisOf before.Selection    after.Selection
        let emission     = axisOf before.Emission     after.Emission
        let insertion    = axisOf before.Insertion    after.Insertion
        let tightening   = axisOf before.Tightening   after.Tightening
        let userMatching = axisOf before.UserMatching after.UserMatching
        { Selection    = selection
          Emission     = emission
          Insertion    = insertion
          Tightening   = tightening
          UserMatching = userMatching
          AnyChanged   =
              selection.Changed
           || emission.Changed
           || insertion.Changed
           || tightening.Changed
           || userMatching.Changed }

    /// Compute the five-axis policy diff and return it in a lineage carrier
    /// (H-033). The `catalog` and `profile` parameters are reserved for a
    /// future "full-projection diff" that runs both policies through the
    /// pass chain and diffs the resulting `ComposeState` trees via
    /// `LineageTree.bifurcate`; in V1 the structural diff of the Policy
    /// records is the primary deliverable.
    ///
    /// The returned `Lineage<PolicyDiff>` has an empty trail — the diff
    /// operates at the policy-aggregate level, not at the per-Kind level.
    /// Per-Kind attribution appears in the full-projection variant once
    /// H-033's "run both policies" milestone ships.
    let diffPolicy
        (_catalog: Catalog)
        (_profile: Profile)
        (policyBefore: Policy)
        (policyAfter: Policy)
        : Lineage<PolicyDiff> =
        Lineage.ofValue (compare policyBefore policyAfter)
