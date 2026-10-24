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
    Assert.Equal(ConstraintState.NoDbConstraint, r.ConstraintState)
    Assert.False(Reference.hasDbConstraint r)
    Assert.True(Reference.isConstraintTrusted r)
    Assert.True(Reference.isConstraintStateConsistent r)

[<Fact>]
let ``G14: a real untrusted (NOCHECK) constraint is preserved`` () =
    let r = baseRef |> Reference.withConstraintState true false
    Assert.Equal(ConstraintState.UntrustedConstraint, r.ConstraintState)
    Assert.True(Reference.hasDbConstraint r)
    Assert.False(Reference.isConstraintTrusted r)
    Assert.True(Reference.isConstraintStateConsistent r)

[<Fact>]
let ``G14: a real trusted constraint is preserved`` () =
    let r = baseRef |> Reference.withConstraintState true true
    Assert.Equal(ConstraintState.TrustedConstraint, r.ConstraintState)
    Assert.True(Reference.hasDbConstraint r && Reference.isConstraintTrusted r)
    Assert.True(Reference.isConstraintStateConsistent r)

[<Property>]
let ``G14: withConstraintState always yields a consistent reference`` (hasDb: bool) (trusted: bool) =
    baseRef |> Reference.withConstraintState hasDb trusted |> Reference.isConstraintStateConsistent

[<Property>]
let ``G14: withConstraintState is idempotent`` (hasDb: bool) (trusted: bool) =
    let once = baseRef |> Reference.withConstraintState hasDb trusted
    let twice = once |> Reference.withConstraintState (Reference.hasDbConstraint once) (Reference.isConstraintTrusted once)
    once = twice

// ---------------------------------------------------------------------------
// M4 (THE VECTOR §6 Kind II) — the `ConstraintState` DU is the promotion of the
// G14 invariant from a runtime check to a TYPE THEOREM. The illegal quadrant is
// now unrepresentable (the DU has no fourth variant); the legacy boolean pair is
// a derived projection that round-trips. Mirrors the `Archetype.grant ∘ ofGrant
// = id` precedent.
// ---------------------------------------------------------------------------

let private allStates =
    [ ConstraintState.NoDbConstraint
      ConstraintState.TrustedConstraint
      ConstraintState.UntrustedConstraint ]

[<Fact>]
let ``M4: the boolean projection matches each variant's documented (hasDb, trusted) pair`` () =
    Assert.Equal<(bool * bool) list>(
        [ (false, true); (true, true); (true, false) ],
        allStates |> List.map ConstraintState.toLegacyBooleans)

[<Fact>]
let ``M4: round-trip law — ofLegacyBooleans (toLegacyBooleans s) = s for every variant`` () =
    for s in allStates do
        let (hasDb, trusted) = ConstraintState.toLegacyBooleans s
        Assert.Equal(s, ConstraintState.ofLegacyBooleans hasDb trusted)

[<Property>]
let ``M4: ofLegacyBooleans normalizes any boolean pair into a representable state (no illegal quadrant)`` (hasDb: bool) (trusted: bool) =
    // Every boolean pair — including the legacy illegal (false, false) — maps to
    // one of the three legal variants; there is no fourth state to land in.
    List.contains (ConstraintState.ofLegacyBooleans hasDb trusted) allStates

[<Property>]
let ``M4: ofLegacyBooleans is the left inverse on legal pairs, normalizing on the illegal one`` (hasDb: bool) (trusted: bool) =
    // toLegacyBooleans ∘ ofLegacyBooleans is the identity on the THREE legal pairs
    // and maps the illegal (false, false) to the canonical (false, true).
    let s = ConstraintState.ofLegacyBooleans hasDb trusted
    let (h2, t2) = ConstraintState.toLegacyBooleans s
    if hasDb then (h2 = hasDb && t2 = trusted)        // legal pairs survive unchanged
    else (h2 = false && t2 = true)                    // (false, _) normalizes to vacuous trust
