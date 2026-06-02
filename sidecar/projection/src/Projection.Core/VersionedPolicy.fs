namespace Projection.Core

open System
open System.Security.Cryptography
open System.Text

/// Semantic version triple for policy snapshots (H-085).
///
/// The component layout follows SemVer 2.0.0 conventions:
///   - `Major` — bumps when a previous policy axis is removed or restricted
///     (an established surface is taken away or narrowed). Example: turning
///     a `TighteningPolicy.Interventions` list from N entries down to N-1.
///   - `Minor` — bumps when a new axis surface is added (something that
///     was previously open is now constrained, OR a new intervention
///     was added). Example: adding an `IncludeOnly` restriction to a
///     previously `IncludeAll` policy is a *restriction* in selection
///     semantics but an *addition* of operator intent; per HORIZON it is
///     a minor bump because the change adds an axis.
///   - `Patch` — bumps when only the rationale / cosmetic content
///     changes; the policy's structural shape is unchanged.
///
/// Composers `next*` produce the next version from a previous version
/// according to the three bump rules (SemVer 2.0.0: a major bump resets
/// minor and patch to 0; a minor bump resets patch to 0).
type SemVer = {
    Major : int
    Minor : int
    Patch : int
}


/// Classification of the structural delta between two `Policy` values
/// (H-085). Encodes the SemVer bump kind without committing to a
/// specific previous version.
type SemVerBump =
    /// No structural change — policies are content-equal. No bump
    /// required (current version stands).
    | NoBump
    /// Rationale or non-structural change only (preserved for future
    /// use; the current `Policy` shape has no rationale fields, so this
    /// branch fires for content-equal policies whose serialized
    /// representations nonetheless differ — e.g., across runtime
    /// versions). Reset patch+1.
    | PatchBump
    /// New axis surface added (a previously-default axis took on a
    /// non-default value, or a Tightening intervention was added).
    /// Minor+1; reset patch to 0.
    | MinorBump
    /// Existing axis surface removed or restricted (a non-default axis
    /// returned to its default, or an intervention was removed, or an
    /// existing axis value was narrowed). Major+1; reset minor and
    /// patch to 0.
    | MajorBump


/// A `Policy` stamped with a content-derived version identifier (H-085).
///
/// Carries both a stable content digest and a `SemVer` version. The digest
/// (hex SHA-256) is the snapshot identity — equal digest ⇒ structurally
/// equal policy. The SemVer captures the relationship to a *previous*
/// snapshot; on construction without a predecessor the version defaults
/// to `1.0.0` (the first version in a chain). `evolve` produces the next
/// `VersionedPolicy` from a predecessor + new policy + changelog, applying
/// the correct bump.
type VersionedPolicy = {
    /// Hex SHA-256 of the canonical policy representation. Stable for
    /// structurally-equal policies within a runtime version.
    Digest    : string
    /// SemVer version. `1.0.0` for the first snapshot in a chain;
    /// computed from the predecessor's version + the bump on `evolve`.
    Version   : SemVer
    /// Snapshot timestamp.
    At        : DateTimeOffset
    /// The policy value.
    Policy    : Policy
    /// Optional human-readable description of what changed relative to
    /// the prior version.
    ChangeLog : string option
}


/// SemVer projection helpers (H-085).
[<RequireQualifiedAccess>]
module SemVer =

    /// The genesis version `1.0.0` — the first snapshot in a chain.
    let genesis : SemVer = { Major = 1; Minor = 0; Patch = 0 }

    /// Apply a `SemVerBump` to a `SemVer`. Major resets minor and patch
    /// to 0; minor resets patch to 0; patch increments only patch;
    /// `NoBump` returns the input unchanged.
    let applyBump (bump: SemVerBump) (v: SemVer) : SemVer =
        match bump with
        | NoBump    -> v
        | PatchBump -> { v with Patch = v.Patch + 1 }
        | MinorBump -> { v with Minor = v.Minor + 1; Patch = 0 }
        | MajorBump -> { Major = v.Major + 1; Minor = 0; Patch = 0 }

    /// Compact textual form `"M.m.p"`. Round-trips via `tryParse` (TBD).
    let toString (v: SemVer) : string =
        sprintf "%d.%d.%d" v.Major v.Minor v.Patch


/// Construction and inspection for `VersionedPolicy` (H-085).
[<RequireQualifiedAccess>]
module VersionedPolicy =

    /// Compute the hex SHA-256 content digest for a given policy. The
    /// canonical representation is the F# structural printer; the digest
    /// is deterministic for the same runtime version. Consumers comparing
    /// digests across runtime upgrades should treat inequality as
    /// "possibly changed" rather than "definitely changed" — the digest
    /// is a snapshot ID, not a cross-version stability guarantee.
    let digestOf (policy: Policy) : string =
        use _ = Bench.scope "ir.policy.digestOf"
        let repr = sprintf "%A" policy
        let bytes = Encoding.UTF8.GetBytes repr
        use sha = SHA256.Create()
        let hash = sha.ComputeHash bytes
        hash |> Array.map (fun b -> b.ToString "x2") |> String.concat ""

    // -----------------------------------------------------------------
    // Bump classification (H-085 SemVer semantics)
    // -----------------------------------------------------------------

    /// True iff the selection went from a wider to a narrower set of kinds.
    /// `IncludeAll` is the widest; `IncludeOnly s` narrows to `s`;
    /// `ExcludeOnly s` narrows by removing `s`.
    let private selectionRestricted
        (before: SelectionPolicy)
        (after: SelectionPolicy)
        : bool =
        match before, after with
        | IncludeAll, IncludeOnly _ -> true
        | IncludeAll, ExcludeOnly s -> not (Set.isEmpty s)
        | IncludeOnly a, IncludeOnly b ->
            Set.isProperSubset b a
        | ExcludeOnly a, ExcludeOnly b ->
            Set.isProperSubset a b
        | _ -> false

    /// True iff the selection went from a narrower to a wider set of kinds.
    let private selectionWidened
        (before: SelectionPolicy)
        (after: SelectionPolicy)
        : bool =
        selectionRestricted after before

    /// Classify the structural delta between two policies as a SemVer
    /// bump. The HORIZON contract is:
    ///   - **Major**: removal or restriction of an existing axis
    ///     (e.g., `IncludeAll` → `IncludeOnly` narrows selection;
    ///     `Tightening.Interventions` shrinks; a non-default axis
    ///     returns to its default).
    ///   - **Minor**: addition of an axis surface (a default value
    ///     takes on a non-default value, or a Tightening intervention
    ///     is added).
    ///   - **Patch**: structural equality but representational
    ///     difference (e.g., the F# structural printer disagrees but
    ///     `before = after`). Reserved for cross-runtime stability.
    ///   - **NoBump**: structural equality AND identical digest.
    let bumpKind (before: Policy) (after: Policy) : SemVerBump =
        if before = after then NoBump
        else
            let selRestrict = selectionRestricted before.Selection after.Selection
            let selWiden    = selectionWidened    before.Selection after.Selection
            let beforeIds =
                before.Tightening.Interventions
                |> List.map TighteningIntervention.id |> Set.ofList
            let afterIds  =
                after.Tightening.Interventions
                |> List.map TighteningIntervention.id |> Set.ofList
            let interventionsRemoved = not (Set.isSubset beforeIds afterIds)
            let interventionsAdded   = not (Set.isSubset afterIds beforeIds)
            let isMajor =
                selRestrict
                || interventionsRemoved
                || (before.Emission     <> after.Emission
                    && after.Emission     = Policy.empty.Emission)
                || (before.Insertion    <> after.Insertion
                    && after.Insertion    = Policy.empty.Insertion)
                || (before.UserMatching <> after.UserMatching
                    && after.UserMatching = Policy.empty.UserMatching)
            let isMinor =
                selWiden
                || interventionsAdded
                || (before.Emission     <> after.Emission
                    && before.Emission    = Policy.empty.Emission)
                || (before.Insertion    <> after.Insertion
                    && before.Insertion   = Policy.empty.Insertion)
                || (before.UserMatching <> after.UserMatching
                    && before.UserMatching = Policy.empty.UserMatching)
            let isStructural =
                before.Selection <> after.Selection
                || before.Emission <> after.Emission
                || before.Insertion <> after.Insertion
                || before.UserMatching <> after.UserMatching
                || beforeIds <> afterIds
            if isMajor then MajorBump
            elif isMinor then MinorBump
            elif isStructural then
                // Non-default → non-default change on an axis: the axis
                // surface is changing rather than appearing/disappearing.
                // Counts as Minor (a new operator intent appeared).
                MinorBump
            else
                // Same set of intervention IDs but different content
                // (e.g., reordering, rationale-only change). Patch bump
                // preserves the chain without claiming structural change.
                PatchBump

    // -----------------------------------------------------------------
    // Construction
    // -----------------------------------------------------------------

    /// Construct a genesis `VersionedPolicy` (`1.0.0`) at the given
    /// timestamp. Used for the first snapshot in a version chain.
    let create
        (at: DateTimeOffset)
        (policy: Policy)
        (changeLog: string option)
        : VersionedPolicy =
        { Digest    = digestOf policy
          Version   = SemVer.genesis
          At        = at
          Policy    = policy
          ChangeLog = changeLog }

    /// Evolve a `VersionedPolicy` to a new policy, computing the next
    /// SemVer from the predecessor's version and the structural delta.
    /// `at` records the new snapshot's timestamp.
    let evolve
        (predecessor: VersionedPolicy)
        (at: DateTimeOffset)
        (newPolicy: Policy)
        (changeLog: string option)
        : VersionedPolicy =
        let bump = bumpKind predecessor.Policy newPolicy
        let nextVersion = SemVer.applyBump bump predecessor.Version
        { Digest    = digestOf newPolicy
          Version   = nextVersion
          At        = at
          Policy    = newPolicy
          ChangeLog = changeLog }

    // Slice 0 (2026-06-02): `VersionedPolicy.now` and `evolveNow` retired.
    // They captured `DateTimeOffset.UtcNow` inside Core (analyzer gap pre-Slice-0).
    // The principled shape mirrors `Episode.fs` and `ApprovalRecord.At`:
    // Core takes `at` as a boundary-supplied parameter; CLI and Pipeline
    // supply `DateTimeOffset.UtcNow` at the call site. Tests use a
    // per-file fixed `testTime` constant for determinism.

    /// True iff two versioned policies share the same content digest
    /// (structurally equal policies, regardless of version / timestamp).
    let sameContent (a: VersionedPolicy) (b: VersionedPolicy) : bool =
        a.Digest = b.Digest

    /// True iff `after`'s digest differs from `before`'s — i.e. the
    /// policy changed between the two snapshots.
    let changed (before: VersionedPolicy) (after: VersionedPolicy) : bool =
        not (sameContent before after)
