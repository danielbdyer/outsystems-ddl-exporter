namespace Projection.Core

open System
open System.Security.Cryptography
open System.Text

/// A `Policy` stamped with a content-derived version identifier (H-085).
///
/// **Version.** A hex-encoded SHA-256 digest of the policy's canonical
/// string representation (`sprintf "%A"`). The digest is stable for the
/// same policy value within a runtime version. Two identical policies
/// produce the same Version; any axis change produces a different Version.
/// This enables operators to track policy drift without comparing full
/// records — equality of `Version` strings implies structural equality.
///
/// **At.** The `DateTimeOffset` at which the snapshot was recorded.
/// Constructed via `VersionedPolicy.create` (caller supplies timestamp)
/// or `VersionedPolicy.now` (captures `DateTimeOffset.UtcNow`).
///
/// **ChangeLog.** Optional human-readable description of what changed
/// relative to the prior version. The `None` case is valid (automated
/// snapshots need no prose); the `Some` case is for operator-authored
/// approval records (H-086).
type VersionedPolicy = {
    Version   : string
    At        : DateTimeOffset
    Policy    : Policy
    ChangeLog : string option
}


/// Construction and inspection for `VersionedPolicy` (H-085).
[<RequireQualifiedAccess>]
module VersionedPolicy =

    /// Compute the hex SHA-256 version string for a given policy. The
    /// representation is the F# structural printer (`sprintf "%A"`) which
    /// is deterministic for the same runtime version. Consumers comparing
    /// versions across runtime upgrades should treat version inequality as
    /// "possibly changed" rather than "definitely changed" — the digest is
    /// a snapshot ID, not a cross-version stability guarantee.
    let versionOf (policy: Policy) : string =
        use _ = Bench.scope "ir.policy.versionOf"
        let repr = sprintf "%A" policy
        let bytes = Encoding.UTF8.GetBytes repr
        use sha = SHA256.Create()
        let hash = sha.ComputeHash bytes
        hash |> Array.map (fun b -> b.ToString "x2") |> String.concat ""

    /// Construct a `VersionedPolicy` at a given timestamp.
    let create (at: DateTimeOffset) (policy: Policy) (changeLog: string option) : VersionedPolicy =
        { Version   = versionOf policy
          At        = at
          Policy    = policy
          ChangeLog = changeLog }

    /// Construct a `VersionedPolicy` timestamped at `DateTimeOffset.UtcNow`.
    let now (policy: Policy) (changeLog: string option) : VersionedPolicy =
        create DateTimeOffset.UtcNow policy changeLog

    /// True iff two versioned policies represent the same Policy content
    /// (regardless of timestamp or changelog).
    let sameContent (a: VersionedPolicy) (b: VersionedPolicy) : bool =
        a.Version = b.Version

    /// True iff `after` carries a different version than `before` — i.e.
    /// the policy changed between the two snapshots.
    let changed (before: VersionedPolicy) (after: VersionedPolicy) : bool =
        not (sameContent before after)
