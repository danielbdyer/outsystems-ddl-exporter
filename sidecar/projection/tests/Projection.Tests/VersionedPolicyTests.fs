module Projection.Tests.VersionedPolicyTests

// H-085: VersionedPolicy — SHA-256 content-addressed snapshots plus
// SemVer (major/minor/patch) classification of the structural delta
// between successive policies.

open System
open Xunit
open Projection.Core
open Projection.Tests.Fixtures

/// Slice 0 (2026-06-02): Core retired the `*Now` wrappers; tests use a fixed
/// `testTime` for determinism. Per the Episode.fs "boundary-supplied at"
/// pattern — wall-clock impurity stays at the boundary.
let private testTime : System.DateTimeOffset =
    System.DateTimeOffset(2026, 1, 1, 0, 0, 0, System.TimeSpan.Zero)

// ---------------------------------------------------------------------------
// digestOf — content addressing
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-085: digestOf is deterministic for the same policy`` () =
    let v1 = VersionedPolicy.digestOf Policy.empty
    let v2 = VersionedPolicy.digestOf Policy.empty
    Assert.Equal(v1, v2)

[<Fact>]
let ``H-085: digestOf produces different values for different policies`` () =
    let v1 = VersionedPolicy.digestOf Policy.empty
    let v2 = VersionedPolicy.digestOf { Policy.empty with Insertion = InsertNew }
    Assert.NotEqual<string>(v1, v2)

[<Fact>]
let ``H-085: digestOf produces a 64-character hex string`` () =
    let v = VersionedPolicy.digestOf Policy.empty
    Assert.Equal(64, v.Length)
    Assert.True(v |> Seq.forall (fun c -> "0123456789abcdef".Contains c))

[<Fact>]
let ``H-085: changing Selection produces a different digest`` () =
    let p1 = Policy.empty
    let p2 = { Policy.empty with Selection = IncludeOnly (Set.singleton customerKey) }
    Assert.NotEqual<string>(VersionedPolicy.digestOf p1, VersionedPolicy.digestOf p2)

[<Fact>]
let ``H-085: changing Emission produces a different digest`` () =
    let p1 = Policy.empty
    let p2 = { Policy.empty with Emission = EmissionPolicy.dataOnly }
    Assert.NotEqual<string>(VersionedPolicy.digestOf p1, VersionedPolicy.digestOf p2)

[<Fact>]
let ``H-085: changing Insertion produces a different digest`` () =
    let p1 = Policy.empty
    let p2 = { Policy.empty with Insertion = InsertNew }
    Assert.NotEqual<string>(VersionedPolicy.digestOf p1, VersionedPolicy.digestOf p2)

// ---------------------------------------------------------------------------
// SemVer.applyBump — the version-bump algebra
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-085 SemVer: NoBump leaves the version unchanged`` () =
    let v = { Major = 2; Minor = 3; Patch = 4 }
    Assert.Equal(v, SemVer.applyBump NoBump v)

[<Fact>]
let ``H-085 SemVer: PatchBump increments only the patch`` () =
    Assert.Equal({ Major = 2; Minor = 3; Patch = 5 },
                 SemVer.applyBump PatchBump { Major = 2; Minor = 3; Patch = 4 })

[<Fact>]
let ``H-085 SemVer: MinorBump increments minor and resets patch`` () =
    Assert.Equal({ Major = 2; Minor = 4; Patch = 0 },
                 SemVer.applyBump MinorBump { Major = 2; Minor = 3; Patch = 4 })

[<Fact>]
let ``H-085 SemVer: MajorBump increments major and resets minor and patch`` () =
    Assert.Equal({ Major = 3; Minor = 0; Patch = 0 },
                 SemVer.applyBump MajorBump { Major = 2; Minor = 3; Patch = 4 })

[<Fact>]
let ``H-085 SemVer: toString formats as M.m.p`` () =
    Assert.Equal("2.3.4", SemVer.toString { Major = 2; Minor = 3; Patch = 4 })

// ---------------------------------------------------------------------------
// bumpKind — structural-delta classification (the HORIZON contract)
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-085 bump: identical policies produce NoBump`` () =
    Assert.Equal(NoBump, VersionedPolicy.bumpKind Policy.empty Policy.empty)

[<Fact>]
let ``H-085 bump: adding a Tightening intervention is a MinorBump`` () =
    let cfg = NullabilityTighteningConfig.create 0.1m false [] |> Result.value
    let after = { Policy.empty with Tightening = { Interventions = [Nullability ("n1", cfg)] } }
    Assert.Equal(MinorBump, VersionedPolicy.bumpKind Policy.empty after)

[<Fact>]
let ``H-085 bump: removing a Tightening intervention is a MajorBump`` () =
    let cfg = NullabilityTighteningConfig.create 0.1m false [] |> Result.value
    let before = { Policy.empty with Tightening = { Interventions = [Nullability ("n1", cfg)] } }
    Assert.Equal(MajorBump, VersionedPolicy.bumpKind before Policy.empty)

[<Fact>]
let ``H-085 bump: IncludeAll to IncludeOnly is a MajorBump (restriction)`` () =
    let after =
        { Policy.empty with Selection = IncludeOnly (Set.singleton customerKey) }
    Assert.Equal(MajorBump, VersionedPolicy.bumpKind Policy.empty after)

[<Fact>]
let ``H-085 bump: IncludeOnly to IncludeAll is a MinorBump (widening)`` () =
    let before =
        { Policy.empty with Selection = IncludeOnly (Set.singleton customerKey) }
    Assert.Equal(MinorBump, VersionedPolicy.bumpKind before Policy.empty)

[<Fact>]
let ``H-085 bump: narrowing an IncludeOnly set is a MajorBump`` () =
    let before =
        { Policy.empty with Selection = IncludeOnly (Set.ofList [customerKey; orderKey]) }
    let after =
        { Policy.empty with Selection = IncludeOnly (Set.singleton customerKey) }
    Assert.Equal(MajorBump, VersionedPolicy.bumpKind before after)

[<Fact>]
let ``H-085 bump: adding Insertion non-default is a MinorBump`` () =
    let after = { Policy.empty with Insertion = InsertNew }
    Assert.Equal(MinorBump, VersionedPolicy.bumpKind Policy.empty after)

[<Fact>]
let ``H-085 bump: returning Insertion to default is a MajorBump`` () =
    let before = { Policy.empty with Insertion = InsertNew }
    Assert.Equal(MajorBump, VersionedPolicy.bumpKind before Policy.empty)

// ---------------------------------------------------------------------------
// create / now / evolve
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-085: create produces a genesis snapshot at 1.0.0`` () =
    let at = DateTimeOffset.Parse "2026-05-22T00:00:00Z"
    let vp = VersionedPolicy.create at Policy.empty (Some "initial")
    Assert.Equal(Policy.empty, vp.Policy)
    Assert.Equal(Some "initial", vp.ChangeLog)
    Assert.Equal(at, vp.At)
    Assert.Equal(SemVer.genesis, vp.Version)

[<Fact>]
let ``H-085: create computes the expected digest`` () =
    let at = DateTimeOffset.UtcNow
    let vp = VersionedPolicy.create at Policy.empty None
    Assert.Equal(VersionedPolicy.digestOf Policy.empty, vp.Digest)

[<Fact>]
let ``H-085: create produces a VersionedPolicy at the genesis version`` () =
    let vp = VersionedPolicy.create testTime Policy.empty None
    Assert.Equal(SemVer.genesis, vp.Version)

[<Fact>]
let ``H-085 evolve: adding a Tightening intervention bumps minor`` () =
    let cfg = NullabilityTighteningConfig.create 0.1m false [] |> Result.value
    let predecessor = VersionedPolicy.create testTime Policy.empty None
    let after = { Policy.empty with Tightening = { Interventions = [Nullability ("n1", cfg)] } }
    let next = VersionedPolicy.evolve predecessor testTime after (Some "added n1")
    Assert.Equal({ Major = 1; Minor = 1; Patch = 0 }, next.Version)

[<Fact>]
let ``H-085 evolve: removing a Tightening intervention bumps major`` () =
    let cfg = NullabilityTighteningConfig.create 0.1m false [] |> Result.value
    let withIntervention =
        { Policy.empty with Tightening = { Interventions = [Nullability ("n1", cfg)] } }
    let predecessor = VersionedPolicy.create testTime withIntervention None
    let next = VersionedPolicy.evolve predecessor testTime Policy.empty (Some "removed n1")
    Assert.Equal({ Major = 2; Minor = 0; Patch = 0 }, next.Version)

[<Fact>]
let ``H-085 evolve: structurally identical policies bump nothing`` () =
    let predecessor = VersionedPolicy.create testTime Policy.empty None
    let next = VersionedPolicy.evolve predecessor testTime Policy.empty None
    Assert.Equal(predecessor.Version, next.Version)

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
    let vp1 = VersionedPolicy.create testTime Policy.empty None
    let vp2 = VersionedPolicy.create testTime { Policy.empty with Insertion = Merge } None
    Assert.True(VersionedPolicy.changed vp1 vp2)

[<Fact>]
let ``H-085: changed is false when policy is the same`` () =
    let vp1 = VersionedPolicy.create testTime Policy.empty None
    let vp2 = VersionedPolicy.create testTime Policy.empty (Some "log line")
    Assert.False(VersionedPolicy.changed vp1 vp2)
