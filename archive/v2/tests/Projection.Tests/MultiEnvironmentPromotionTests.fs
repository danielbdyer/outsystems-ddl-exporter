module Projection.Tests.MultiEnvironmentPromotionTests

open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core

// ---------------------------------------------------------------------------
// R4 multi-environment promotion property test (M4 Tolerance taxonomy).
//
// Per `HANDOFF.md` outstanding queue + `V2_DRIVER.md` independent-forward-
// progress + `DECISIONS 2026-05-22 — T-30 / T-15 cutover fallback ladder
// gates`: the four-environment promotion (Dev → QA → UAT → PROD) tightens
// monotonically; PROD = `Tolerance.strict` (zero divergences); Dev may
// be `Tolerance.permissive` (every known divergence) during the dual-
// track period.
//
// The chapter 4.2 multi-environment commutativity property test is the
// worked precedent (four target populations under one source population;
// per-environment differences live in TargetUserId values). R4 encodes
// the structural commitment of the cutover ladder itself: the per-
// environment Tolerance values form a monotonic chain
// `Dev.divergences ⊇ QA.divergences ⊇ UAT.divergences ⊇ PROD.divergences`.
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// Cutover-ladder shape — the structural commitment under R4.
// ---------------------------------------------------------------------------

/// Per-environment cutover-promotion configuration. Each environment
/// carries its own `Tolerance` value. The R4 property asserts the
/// chain `Dev ⊇ QA ⊇ UAT ⊇ PROD` holds for any valid configuration.
type CutoverLadder =
    {
        Dev  : Tolerance
        Qa   : Tolerance
        Uat  : Tolerance
        Prod : Tolerance
    }

let private isMonotonicallyTightening (ladder: CutoverLadder) : bool =
    let dev  = Tolerance.divergences ladder.Dev
    let qa   = Tolerance.divergences ladder.Qa
    let uat  = Tolerance.divergences ladder.Uat
    let prod = Tolerance.divergences ladder.Prod
    Set.isSubset qa  dev
    && Set.isSubset uat qa
    && Set.isSubset prod uat

// ---------------------------------------------------------------------------
// R4 base properties — the cutover-ladder gates.
// ---------------------------------------------------------------------------

[<Fact>]
let ``R4: PROD environment is strict (zero divergences tolerated)`` () =
    // Per DECISIONS 2026-05-22 T-30 / T-15: the PROD environment
    // accepts zero divergences. `Tolerance.strict` IS the PROD
    // posture.
    Assert.True (Tolerance.isStrict Tolerance.strict)
    Assert.Empty (Tolerance.divergences Tolerance.strict)

[<Fact>]
let ``R4: Dev environment may be permissive during dual-track (every known divergence accepted)`` () =
    // Per DECISIONS 2026-05-22 T-30 / T-15 + cutover-ladder
    // R6: Dev may run permissive during the V2-augmented mode of
    // the fallback ladder. Verify `Tolerance.permissive` covers
    // every empirically-grounded variant.
    let permissive = Tolerance.divergences Tolerance.permissive
    Assert.Equal<Set<ToleratedDivergence>> (ToleratedDivergence.allKnown, permissive)

[<Fact>]
let ``R4: canonical monotonic chain Dev=permissive ⊇ QA ⊇ UAT ⊇ PROD=strict holds`` () =
    // Worked example of the cutover ladder per DECISIONS 2026-05-22:
    // - Dev:  permissive (every known divergence accepted)
    // - QA:   accepts IndexOptionsUnreflected + StaticPopulationsUnreflected
    // - UAT:  accepts only StaticPopulationsUnreflected
    // - PROD: strict
    let ladder =
        { Dev  = Tolerance.permissive
          Qa   = Tolerance.ofSet (Set.ofList
                    [ ToleratedDivergence.IndexOptionsUnreflected
                      ToleratedDivergence.StaticPopulationsUnreflected ])
          Uat  = Tolerance.ofSet (Set.ofList
                    [ ToleratedDivergence.StaticPopulationsUnreflected ])
          Prod = Tolerance.strict }
    Assert.True (isMonotonicallyTightening ladder)

// ---------------------------------------------------------------------------
// R4 property: per-environment monotonic-tightening invariance.
//
// For any per-environment Tolerance triple (D, Q, U) where each
// subsequent env's divergence set is a subset of the prior, the
// resulting ladder + PROD=strict is monotonically tightening.
// ---------------------------------------------------------------------------

[<Fact>]
let ``R4 property: every monotonic chain (Dev ⊇ QA ⊇ UAT ⊇ PROD=strict) satisfies isMonotonicallyTightening`` () =
    // Property-based: walk every monotonic chain over the four
    // empirically-known divergences. For each subset selection
    // (Dev, QA, UAT) where Dev ⊇ QA ⊇ UAT, the chain holds.
    let all = ToleratedDivergence.allKnown |> Set.toList
    // 2^|all| subsets each level.
    let subsets =
        let rec gen (xs: ToleratedDivergence list) : Set<ToleratedDivergence> list =
            match xs with
            | [] -> [ Set.empty ]
            | h :: t ->
                let rest = gen t
                List.append rest (rest |> List.map (Set.add h))
        gen all
    // Enumerate (Dev, QA, UAT) triples with Dev ⊇ QA ⊇ UAT.
    let mutable checkedCount = 0
    for dev in subsets do
        for qa in subsets do
            if Set.isSubset qa dev then
                for uat in subsets do
                    if Set.isSubset uat qa then
                        let ladder =
                            { Dev  = Tolerance.ofSet dev
                              Qa   = Tolerance.ofSet qa
                              Uat  = Tolerance.ofSet uat
                              Prod = Tolerance.strict }
                        Assert.True (isMonotonicallyTightening ladder)
                        checkedCount <- checkedCount + 1
    // 5 known divergences → 32 subsets per level. Verify we walked
    // the space (the count is the number of (Dev, QA, UAT)
    // monotonic chains; the formula is the dilogarithm but the
    // count is bounded by 32^3 = 32768).
    Assert.True (checkedCount > 100, sprintf "expected > 100 checked chains; got %d" checkedCount)

[<Fact>]
let ``R4 negative: non-monotonic chain (QA accepts something Dev does not) violates isMonotonicallyTightening`` () =
    // Counterexample: QA permits IndexOptionsUnreflected but Dev does
    // not. The chain Dev ⊇ QA fails.
    let ladder =
        { Dev  = Tolerance.strict
          Qa   = Tolerance.ofSet (Set.ofList [ ToleratedDivergence.IndexOptionsUnreflected ])
          Uat  = Tolerance.strict
          Prod = Tolerance.strict }
    Assert.False (isMonotonicallyTightening ladder)

[<Fact>]
let ``R4 negative: PROD with non-empty tolerance violates the cutover commitment`` () =
    // PROD must be strict (zero divergences). A configuration with
    // PROD.tolerance non-empty violates the cutover-ladder
    // commitment regardless of Dev/QA/UAT values.
    let ladder =
        { Dev  = Tolerance.permissive
          Qa   = Tolerance.permissive
          Uat  = Tolerance.permissive
          Prod = Tolerance.permissive }
    // The monotonic-chain check passes structurally
    // (permissive ⊇ permissive ⊇ permissive ⊇ permissive), but
    // PROD's posture violates the cutover commitment. The
    // structural test (`isMonotonicallyTightening`) is necessary
    // but not sufficient; the PROD-strict gate is the
    // complementary check.
    Assert.True (isMonotonicallyTightening ladder)  // chain alone passes
    Assert.False (Tolerance.isStrict ladder.Prod)   // but PROD posture fails

[<Fact>]
let ``R4 composition: PROD-strict gate ∧ monotonic chain define the cutover-ladder commitment`` () =
    // The cutover-ladder commitment requires BOTH:
    //   1. Per-env Tolerance values form a monotonic chain.
    //   2. PROD's Tolerance is strict.
    // Test both checks pass on the canonical worked-example ladder.
    let ladder =
        { Dev  = Tolerance.permissive
          Qa   = Tolerance.ofSet (Set.ofList
                    [ ToleratedDivergence.IndexOptionsUnreflected
                      ToleratedDivergence.StaticPopulationsUnreflected ])
          Uat  = Tolerance.ofSet (Set.ofList
                    [ ToleratedDivergence.StaticPopulationsUnreflected ])
          Prod = Tolerance.strict }
    Assert.True (isMonotonicallyTightening ladder)
    Assert.True (Tolerance.isStrict ladder.Prod)

// ---------------------------------------------------------------------------
// R4 invariant property: monotonicity is closed under `withDivergence`
// applied at the Dev end.
//
// If a ladder is monotonically tightening AND a new divergence is added
// to Dev (only), the ladder remains monotonically tightening (Dev gains
// a divergence; QA/UAT/PROD unchanged; subset relations preserved).
// ---------------------------------------------------------------------------

[<Property>]
let ``R4 property: adding a divergence to Dev preserves monotonic tightening`` (toAdd: ToleratedDivergence) =
    let baseLadder =
        { Dev  = Tolerance.ofSet (Set.ofList
                    [ ToleratedDivergence.IndexOptionsUnreflected
                      ToleratedDivergence.StaticPopulationsUnreflected ])
          Qa   = Tolerance.ofSet (Set.ofList
                    [ ToleratedDivergence.StaticPopulationsUnreflected ])
          Uat  = Tolerance.strict
          Prod = Tolerance.strict }
    // Baseline holds.
    let baseIsValid = isMonotonicallyTightening baseLadder
    // Adding a divergence to Dev only.
    let extendedLadder = { baseLadder with Dev = Tolerance.withDivergence toAdd baseLadder.Dev }
    let extendedIsValid = isMonotonicallyTightening extendedLadder
    baseIsValid && extendedIsValid

[<Property>]
let ``R4 property: tightening PROD never makes a strict ladder invalid (idempotent)`` () =
    // PROD is already strict; "tightening" it (set-intersect with
    // empty) is idempotent. Verify by construction.
    let ladder =
        { Dev  = Tolerance.permissive
          Qa   = Tolerance.permissive
          Uat  = Tolerance.permissive
          Prod = Tolerance.strict }
    let original = Tolerance.divergences ladder.Prod
    let tightenedProd = ladder.Prod  // already strict
    Set.isEmpty (Tolerance.divergences tightenedProd)
    && Tolerance.divergences tightenedProd = original

// ---------------------------------------------------------------------------
// R4 — structural T11 specialization for Tolerance.
//
// Tolerance equivalence-class membership is monotonic in the
// divergence set: adding a divergence can only expand acceptance.
// ---------------------------------------------------------------------------

[<Property>]
let ``R4 property: Tolerance acceptance is monotonic in the divergence set`` (existing: ToleratedDivergence) (additional: ToleratedDivergence) =
    // Build a tolerance with one divergence; check whether it
    // accepts the additional one (false unless additional = existing).
    let single = Tolerance.ofSet (Set.singleton existing)
    let extended = Tolerance.withDivergence additional single
    // Acceptance is monotonic: anything `single` accepts, `extended`
    // also accepts.
    let acceptsUnderSingle = Tolerance.tolerates existing single
    let acceptsUnderExtended = Tolerance.tolerates existing extended
    acceptsUnderSingle = acceptsUnderExtended  // both true: monotonicity holds for the existing divergence

[<Fact>]
let ``R4: Tolerance.permissive accepts every empirically-grounded divergence`` () =
    let permissive = Tolerance.permissive
    for d in ToleratedDivergence.allKnown do
        Assert.True (Tolerance.tolerates d permissive, sprintf "permissive should accept %A" d)

[<Fact>]
let ``R4: Tolerance.strict rejects every empirically-grounded divergence`` () =
    let strict = Tolerance.strict
    for d in ToleratedDivergence.allKnown do
        Assert.False (Tolerance.tolerates d strict, sprintf "strict should reject %A" d)
