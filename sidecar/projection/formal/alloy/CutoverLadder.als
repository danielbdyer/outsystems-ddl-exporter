// R4-CUT (FORMAL_METHODS.md §2/§4): bounded model check of the cutover
// fallback ladder — R6 split-brain governance (DECISIONS 2026-05-22),
// the T-30 / T-15 gates (DECISIONS 2026-05-22), and the V2-driver KPI
// framing (V2_DRIVER.md). Before this spec the ladder's verifiability
// was, in the audit's own words, "governance, not code."
//
// The machine, transcribed:
//   - Per (environment × artifact-type) PAIR, a rung: V1Only <
//     Augmented (V1 drives, V2 verifies) < Driver (V2 emits the
//     production bytes).
//   - Per-pair promotion Augmented → Driver requires N CONSECUTIVE
//     green canary runs plus operator sign-off (R6). A red canary
//     resets the consecutive counter to zero.
//   - Phases advance PreT30 → PreT15 → Window → Survival → Sunset.
//     The T-30 gate's negative branch caps every pair at Augmented;
//     the T-15 gate's negative branch (canary flake / tolerance churn)
//     retreats every pair to V1Only for the cutover window.
//   - V1 stays warm as the fallback rung through cutover+30 (the
//     Survival phase); archiving V1 is only possible in Sunset.
//   - Falling back down the ladder is always available while V1 is
//     warm; nothing ever forces a promotion.
//
// Model constant: RequiredGreens = 3 stands in for production N=10.
// The LAW under check is the structure — consecutiveness, gating,
// single-route promotion — not the magnitude, which is operator
// policy. (Scoped integers keep the bounded check tractable.)
//
// Rung-4 boundary: bounded model checking at the scopes below.

module CutoverLadder

// ---------------------------------------------------------------------------
// Static structure.
// ---------------------------------------------------------------------------

abstract sig Mode {}
one sig V1Only, Augmented, Driver extends Mode {}

// Rungs strictly below a mode — the fallback codomain.
fun lowerRungs[m: Mode] : set Mode {
  (m = Driver) implies (V1Only + Augmented)
  else (m = Augmented) implies V1Only
  else none
}

abstract sig Phase {}
one sig PreT30, PreT15, Window, Survival, Sunset extends Phase {}

// One (environment × artifact-type) pair per atom (R6: the transition
// is per-pair, never flag-day global).
sig Pair {
  var mode   : one Mode,
  var greens : one Int   // consecutive green canary runs, capped at RequiredGreens
}

var sig SignedOff in Pair {}   // operator sign-off, per pair

one sig V1Track {}
var sig Warm    in V1Track {}  // V1 kept warm (fallback available)
var sig Retreat in V1Track {}  // the T-15 negative branch fired

var one sig phase in Phase {}

fun RequiredGreens : Int { 3 }

// ---------------------------------------------------------------------------
// Frame helpers.
// ---------------------------------------------------------------------------

pred framePairs  { mode' = mode and greens' = greens }
pred frameGov    { SignedOff' = SignedOff and Warm' = Warm and Retreat' = Retreat }
pred framePhase  { phase' = phase }

// ---------------------------------------------------------------------------
// Transitions.
// ---------------------------------------------------------------------------

// A green canary run for one pair: the consecutive counter advances
// (capped — past the threshold more greens change nothing).
pred green[p: Pair] {
  greens' = greens ++ (p -> ((p.greens = RequiredGreens) implies RequiredGreens else plus[p.greens, 1]))
  mode' = mode
  frameGov and framePhase
}

// A red canary run: the CONSECUTIVE counter resets to zero (the N=10
// requirement is consecutive greens, not cumulative).
pred red[p: Pair] {
  greens' = greens ++ (p -> 0)
  mode' = mode
  frameGov and framePhase
}

// Operator sign-off for a pair.
pred signOff[p: Pair] {
  SignedOff' = SignedOff + p
  Warm' = Warm and Retreat' = Retreat
  framePairs and framePhase
}

// R6 promotion — the ONLY route to Driver: from Augmented, with the
// full consecutive-green quota AND operator sign-off. Blocked during
// the cutover window if the T-15 retreat fired.
pred promote[p: Pair] {
  p.mode = Augmented
  p.greens = RequiredGreens
  p in SignedOff
  (phase = Window) implies no Retreat
  mode' = mode ++ (p -> Driver)
  greens' = greens
  frameGov and framePhase
}

// Fallback — always available while V1 is warm: drop to any strictly
// lower rung. Nothing ever forces a promotion; this is the operator's
// escape hatch at every moment of the ladder.
pred fallback[p: Pair] {
  some Warm
  some m: lowerRungs[p.mode] {
    mode' = mode ++ (p -> m)
  }
  greens' = greens
  frameGov and framePhase
}

// T-30 gate. Positive branch: proceed unchanged. Negative branch (any
// of the four structural conditions unmet): every pair is capped at
// Augmented — V2-driver is off the table for the cutover.
pred t30Pass {
  phase = PreT30 and phase' = PreT15
  framePairs and frameGov
}
pred t30Fail {
  phase = PreT30 and phase' = PreT15
  mode' = { p: Pair, m: Mode | m = ((p.mode = Driver) implies Augmented else p.mode) }
  greens' = greens
  frameGov
}

// T-15 gate. Negative branch (canary flake >10% / uncontrolled
// tolerance churn): retreat to V1-only for the window.
pred t15Pass {
  phase = PreT15 and phase' = Window
  framePairs and frameGov
}
pred t15Fail {
  phase = PreT15 and phase' = Window
  mode' = Pair -> V1Only
  greens' = greens
  SignedOff' = SignedOff and Warm' = Warm
  Retreat' = V1Track
}

// The cutover itself, then the survival window (cutover+30, V1 warm
// regardless), then sunset.
pred cutover     { phase = Window   and phase' = Survival and framePairs and frameGov }
pred survivalEnd { phase = Survival and phase' = Sunset   and framePairs and frameGov }

// V1 archive — only in Sunset (after cutover+30 and the full
// schema-evolution cycle, both abstracted into the Sunset phase).
pred archive {
  phase = Sunset
  Warm' = none
  SignedOff' = SignedOff and Retreat' = Retreat
  framePairs and framePhase
}

pred stutter { framePairs and frameGov and framePhase }

fact traces {
  // Init: dual-track (every pair Augmented), no evidence, no sign-off,
  // V1 warm, no retreat, T-30 gate ahead.
  phase = PreT30
  mode = Pair -> Augmented
  greens = Pair -> 0
  no SignedOff
  Warm = V1Track
  no Retreat

  always {
    stutter
    or (some p: Pair | green[p] or red[p] or signOff[p] or promote[p] or fallback[p])
    or t30Pass or t30Fail or t15Pass or t15Fail
    or cutover or survivalEnd or archive
  }
}

// ---------------------------------------------------------------------------
// Safety laws (expect 0).
// ---------------------------------------------------------------------------

// THE R6 law: the only way a pair becomes Driver is with the full
// consecutive-green quota and operator sign-off in hand — no schedule
// pressure, no gate, no other event can mint a Driver.
check DriverOnlyByEvidence {
  always (all p: Pair |
    (p.mode != Driver and p.mode' = Driver)
      implies (p.greens = RequiredGreens and p in SignedOff))
} for 5 Int, exactly 2 Pair, 1..14 steps expect 0

// The fallback-ladder law: V1 stays warm in every phase before Sunset
// — there is no reachable pre-Sunset state with the safety net gone.
check V1WarmBeforeSunset {
  always (phase != Sunset implies Warm = V1Track)
} for 5 Int, exactly 2 Pair, 1..14 steps expect 0

// Archive is irreversible and Sunset-only: a cold V1 implies the
// ladder has fully landed.
check ColdV1OnlyInSunset {
  always (no Warm implies phase = Sunset)
} for 5 Int, exactly 2 Pair, 1..14 steps expect 0

// The T-15 retreat holds through the window: once the retreat fires,
// every pair stays V1Only for the entire cutover window — no
// re-promotion under flake.
check RetreatHoldsThroughWindow {
  always ((phase = Window and some Retreat) implies (all p: Pair | p.mode = V1Only))
} for 5 Int, exactly 2 Pair, 1..14 steps expect 0

// The consecutive counter is honest: it grows only via a green canary
// event for that pair (never by gate, sign-off, promotion, or another
// pair's events).
check GreensGrowOnlyByGreen {
  always (all p: Pair | gt[p.greens', p.greens] implies green[p])
} for 5 Int, exactly 2 Pair, 1..14 steps expect 0

// Fallback is enabled at every moment a pair is Driver before Sunset:
// the guard needs only V1-warm, which V1WarmBeforeSunset guarantees.
check FallbackEnabledForDrivers {
  always ((phase != Sunset) implies
    (all p: Pair | p.mode = Driver implies (some Warm and some lowerRungs[p.mode])))
} for 5 Int, exactly 2 Pair, 1..14 steps expect 0

// ---------------------------------------------------------------------------
// Reachability sanity (expect 1) — the ladder is neither vacuous nor
// coercive.
// ---------------------------------------------------------------------------

// The destination is reachable: both pairs promoted on evidence, the
// phases walked, V1 archived in Sunset. (The V2-driver KPI's happy
// path exists in the model.)
run DestinationReachable {
  eventually (phase = Sunset and no Warm and (all p: Pair | p.mode = Driver))
} for 5 Int, exactly 2 Pair, 1..24 steps expect 1

// Nothing forces a promotion: a complete trace in which no pair ever
// reaches Driver is valid (DECISIONS failure-mode-1 — "V2-driver
// pursued past readiness because the schedule says now" — has no
// counterpart transition in the machine).
run EvidenceNeverForcesPromotion {
  always (all p: Pair | p.mode != Driver)
} for 5 Int, exactly 2 Pair, 1..14 steps expect 1

// The retreat path is representable (T-15 negative branch reaches the
// window in the V1Only rung).
run RetreatPathRepresentable {
  eventually (phase = Window and some Retreat)
} for 5 Int, exactly 2 Pair, 1..14 steps expect 1

// A red canary genuinely costs progress: a pair can reach the quota,
// lose it to a red, and have to re-earn it before promoting.
run RedResetsProgress {
  some p: Pair |
    eventually (p.greens = RequiredGreens and
      eventually (p.greens = 0 and p.mode = Augmented and
        eventually p.mode = Driver))
} for 5 Int, exactly 2 Pair, 1..24 steps expect 1
