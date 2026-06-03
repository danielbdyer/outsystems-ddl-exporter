module Projection.Tests.ReferenceConstraintStateTests

open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core

// ---------------------------------------------------------------------------
// G14 — the Reference constraint-state guard. Trust is only meaningful when a
// DB constraint exists: `¬IsConstraintTrusted ⟹ HasDbConstraint`. The illegal
// quadrant `(HasDbConstraint=false ∧ IsConstraintTrusted=false)` — an untrusted
// (WITH NOCHECK) reference with no constraint to NOCHECK — is normalized away by
// `Reference.withConstraintState`, the sanctioned setter every external-evidence
// producer (V1 rowset, ReadSide) routes through. These tests pin the guard +
// prove the illegal quadrant is unreachable in practice.
// ---------------------------------------------------------------------------

let private baseRef : Reference =
    let key s = SsKey.synthesized "OS_G14" s |> Result.value
    Reference.create (key "ref") (Name.create "FK_X" |> Result.value) (key "src") (key "tgt")

[<Fact>]
let ``G14: Reference.create default is constraint-state consistent`` () =
    Assert.True(Reference.isConstraintStateConsistent baseRef)

[<Fact>]
let ``G14: the illegal quadrant (no constraint, untrusted) normalizes to vacuous-trust`` () =
    // The discriminating case: untrusted WITHOUT a DB constraint is illegal; the
    // guard canonicalizes it to (no constraint, trusted) rather than leaving a
    // NOCHECK marker on a non-existent constraint.
    let r = baseRef |> Reference.withConstraintState false false
    Assert.False(r.HasDbConstraint)
    Assert.True(r.IsConstraintTrusted)
    Assert.True(Reference.isConstraintStateConsistent r)

[<Fact>]
let ``G14: a real untrusted (NOCHECK) constraint is preserved`` () =
    let r = baseRef |> Reference.withConstraintState true false
    Assert.True(r.HasDbConstraint)
    Assert.False(r.IsConstraintTrusted)
    Assert.True(Reference.isConstraintStateConsistent r)

[<Fact>]
let ``G14: a real trusted constraint is preserved`` () =
    let r = baseRef |> Reference.withConstraintState true true
    Assert.True(r.HasDbConstraint && r.IsConstraintTrusted)
    Assert.True(Reference.isConstraintStateConsistent r)

[<Property>]
let ``G14: withConstraintState always yields a consistent reference`` (hasDb: bool) (trusted: bool) =
    baseRef |> Reference.withConstraintState hasDb trusted |> Reference.isConstraintStateConsistent

[<Property>]
let ``G14: withConstraintState is idempotent`` (hasDb: bool) (trusted: bool) =
    let once = baseRef |> Reference.withConstraintState hasDb trusted
    let twice = once |> Reference.withConstraintState once.HasDbConstraint once.IsConstraintTrusted
    once = twice
