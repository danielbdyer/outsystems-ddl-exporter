module Projection.Tests.VersionedPolicyTests

// H-085: VersionedPolicy — SHA-256 content-addressed policy snapshots.

open System
open Xunit
open Projection.Core

// ---------------------------------------------------------------------------
// versionOf determinism and content-addressing
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-085: versionOf is deterministic for the same policy`` () =
    let v1 = VersionedPolicy.versionOf Policy.empty
    let v2 = VersionedPolicy.versionOf Policy.empty
    Assert.Equal(v1, v2)

[<Fact>]
let ``H-085: versionOf produces different values for different policies`` () =
    let v1 = VersionedPolicy.versionOf Policy.empty
    let v2 = VersionedPolicy.versionOf { Policy.empty with Insertion = InsertNew }
    Assert.NotEqual<string>(v1, v2)

[<Fact>]
let ``H-085: versionOf produces a 64-character hex string`` () =
    let v = VersionedPolicy.versionOf Policy.empty
    Assert.Equal(64, v.Length)
    Assert.True(v |> Seq.forall (fun c -> "0123456789abcdef".Contains c))

[<Fact>]
let ``H-085: changing Selection produces a different version`` () =
    let p1 = Policy.empty
    let p2 = { Policy.empty with Selection = IncludeOnly (Set.singleton (SsKey.synthesized "TEST" "k" |> Result.value)) }
    Assert.NotEqual<string>(VersionedPolicy.versionOf p1, VersionedPolicy.versionOf p2)

[<Fact>]
let ``H-085: changing Emission produces a different version`` () =
    let p1 = Policy.empty
    let p2 = { Policy.empty with Emission = EmissionPolicy.dataOnly }
    Assert.NotEqual<string>(VersionedPolicy.versionOf p1, VersionedPolicy.versionOf p2)

[<Fact>]
let ``H-085: changing Insertion produces a different version`` () =
    let p1 = Policy.empty
    let p2 = { Policy.empty with Insertion = InsertNew }
    Assert.NotEqual<string>(VersionedPolicy.versionOf p1, VersionedPolicy.versionOf p2)

// ---------------------------------------------------------------------------
// create / now
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-085: create preserves the policy and changelog`` () =
    let at = DateTimeOffset.Parse "2026-05-22T00:00:00Z"
    let vp = VersionedPolicy.create at Policy.empty (Some "initial")
    Assert.Equal(Policy.empty, vp.Policy)
    Assert.Equal(Some "initial", vp.ChangeLog)
    Assert.Equal(at, vp.At)

[<Fact>]
let ``H-085: create computes the expected version`` () =
    let at = DateTimeOffset.UtcNow
    let vp = VersionedPolicy.create at Policy.empty None
    Assert.Equal(VersionedPolicy.versionOf Policy.empty, vp.Version)

[<Fact>]
let ``H-085: now produces a VersionedPolicy with non-empty version`` () =
    let vp = VersionedPolicy.now Policy.empty None
    Assert.False(System.String.IsNullOrEmpty vp.Version)

// ---------------------------------------------------------------------------
// sameContent / changed
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-085: sameContent is true for same policy at different times`` () =
    let t1 = DateTimeOffset.Parse "2026-01-01T00:00:00Z"
    let t2 = DateTimeOffset.Parse "2026-06-01T00:00:00Z"
    let vp1 = VersionedPolicy.create t1 Policy.empty None
    let vp2 = VersionedPolicy.create t2 Policy.empty None
    Assert.True(VersionedPolicy.sameContent vp1 vp2)

[<Fact>]
let ``H-085: changed is true when policy axes differ`` () =
    let vp1 = VersionedPolicy.now Policy.empty None
    let vp2 = VersionedPolicy.now { Policy.empty with Insertion = Merge } None
    Assert.True(VersionedPolicy.changed vp1 vp2)

[<Fact>]
let ``H-085: changed is false when policy is the same`` () =
    let vp1 = VersionedPolicy.now Policy.empty None
    let vp2 = VersionedPolicy.now Policy.empty (Some "log line")
    Assert.False(VersionedPolicy.changed vp1 vp2)
