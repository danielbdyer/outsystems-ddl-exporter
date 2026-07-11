// R5-DIFF (FORMAL_METHODS.md §2/§4): machine-checked proofs of the
// change algebra's groupoid laws — T12–T16 / A-Lifecycle-4 / NM-45
// (Projection.Core/CatalogDiff.fs, Lifecycle.fs).
//
// THE MODEL AND ITS BINDING, stated honestly. `State` abstracts
// "a Catalog modulo the diff's captured surface" — the quotient on
// which the F# docstrings state every law. The F# realization pins
// the correspondence:
//   - `between a b`     realizes `Between(a, b)` — a delta IS its
//     endpoint pair on the captured surface (compose reads only
//     endpoints: `between (source d1) (target d2)`).
//   - `applyDiff`'s law `applyDiff d (source d) ≈ target d` (the
//     round-trip law, property-tested in ChangeAlgebraSweepTests /
//     AdjunctionLawTests) realizes `Apply`.
//   - `compose`'s partiality guard `isEmpty (between (target d1)
//     (source d2))` realizes `Adjacent`.
// What Dafny proves is the ALGEBRA — associativity, identity,
// inverse, the functor law, the fundamental-theorem fold, replay
// agreement, and NM-45's unreachability claim — for ALL states and
// all chain lengths, unconditionally. What stays property-tested is
// the correspondence between the F# functions and this model (the
// existing rung-2 suites own that pin).

// A catalog on the captured surface. `(==)` because the F# quotient
// has decidable equality (isEmpty of the between-diff).
type State(==)

// A delta is typed by its endpoints — displacements, not edits
// (WAVE_6_ALGEBRA: "state is a torsor over delta").
datatype Delta = Delta(src: State, tgt: State)

function Between(a: State, b: State): Delta { Delta(a, b) }

// norm d = 0 ⟺ isEmpty d ⟺ d is an identity displacement.
predicate IsEmpty(d: Delta) { d.src == d.tgt }

// The torsor action at the source: applying a delta where it starts
// lands at its target (the F# round-trip law).
function Apply(d: Delta, c: State): State
  requires c == d.src
{ d.tgt }

// The groupoid composition is PARTIAL: defined iff d1's target meets
// d2's source on the captured surface (F#: `compose` returns None
// otherwise — fail-loud, never silently wrong).
predicate Adjacent(d1: Delta, d2: Delta) { d1.tgt == d2.src }

function Compose(d1: Delta, d2: Delta): Delta
  requires Adjacent(d1, d2)
{ Between(d1.src, d2.tgt) }

// M12 — the groupoid inverse; total (`between` is total).
function Inverse(d: Delta): Delta { Between(d.tgt, d.src) }

// ---------------------------------------------------------------------------
// The groupoid laws (A-Lifecycle-4 / M12).
// ---------------------------------------------------------------------------

// Associativity: both groupings recompute Between(src d1, tgt d3) —
// exactly the F# docstring's justification.
lemma ComposeAssociative(d1: Delta, d2: Delta, d3: Delta)
  requires Adjacent(d1, d2) && Adjacent(d2, d3)
  ensures Adjacent(Compose(d1, d2), d3)
  ensures Adjacent(d1, Compose(d2, d3))
  ensures Compose(Compose(d1, d2), d3) == Compose(d1, Compose(d2, d3))
{}

// The empty delta at an endpoint is a two-sided identity.
lemma ComposeIdentity(d: Delta)
  ensures Adjacent(Between(d.src, d.src), d)
  ensures Compose(Between(d.src, d.src), d) == d
  ensures Adjacent(d, Between(d.tgt, d.tgt))
  ensures Compose(d, Between(d.tgt, d.tgt)) == d
{}

// The inverse laws: d ⊕ d⁻¹ is the identity at the source; d⁻¹ ⊕ d
// the identity at the target. Round-trip: applying d then d⁻¹ is a
// no-op on the captured surface.
lemma InverseLaws(d: Delta)
  ensures Adjacent(d, Inverse(d))
  ensures Compose(d, Inverse(d)) == Between(d.src, d.src)
  ensures Adjacent(Inverse(d), d)
  ensures Compose(Inverse(d), d) == Between(d.tgt, d.tgt)
  ensures Apply(Inverse(d), Apply(d, d.src)) == d.src
{}

// The functor law (compose's docstring): applying the composite
// equals applying sequentially.
lemma FunctorLaw(d1: Delta, d2: Delta)
  requires Adjacent(d1, d2)
  ensures Apply(Compose(d1, d2), d1.src) == Apply(d2, Apply(d1, d1.src))
{}

// Identity displacements are exactly the empty ones (T15's norm-zero
// characterization, endpoint form).
lemma EmptyIsIdentity(a: State)
  ensures IsEmpty(Between(a, a))
{}

// ---------------------------------------------------------------------------
// Chains — Lifecycle.evolutionChain / netDiff / reconstructLatest.
// ---------------------------------------------------------------------------

// evolutionChain: the per-edge deltas of a snapshot chain
// [between s0 s1, between s1 s2, ...].
function ChainDeltas(states: seq<State>): seq<Delta>
  requires |states| >= 1
  ensures |ChainDeltas(states)| == |states| - 1
  ensures forall i :: 0 <= i < |states| - 1 ==>
    ChainDeltas(states)[i] == Between(states[i], states[i + 1])
{
  if |states| == 1 then []
  else [Between(states[0], states[1])] + ChainDeltas(states[1..])
}

// A delta sequence is chained from a state when each edge departs
// exactly where its predecessor lands — the well-formedness of an
// evolution chain, in the induction-friendly recursive form.
ghost predicate ChainedFrom(c: State, ds: seq<Delta>)
  decreases |ds|
{
  |ds| == 0 || (ds[0].src == c && ChainedFrom(ds[0].tgt, ds[1..]))
}

// NM-45, part 1: a snapshot chain's per-edge deltas are chained by
// construction.
lemma ChainDeltasAreChained(states: seq<State>)
  requires |states| >= 1
  ensures ChainedFrom(states[0], ChainDeltas(states))
  decreases |states|
{
  if |states| > 1 {
    ChainDeltasAreChained(states[1..]);
    assert ChainDeltas(states)[1..] == ChainDeltas(states[1..]);
  }
}

// NM-45, part 2: on a chained sequence, every consecutive edge pair
// is adjacent — `CatalogDiff.compose` returning None inside
// `Lifecycle.netDiff` is structurally unreachable. Proven, not
// asserted.
lemma NonComposableIsUnreachable(states: seq<State>)
  requires |states| >= 1
  ensures forall i :: 0 <= i < |states| - 2 ==>
    Adjacent(ChainDeltas(states)[i], ChainDeltas(states)[i + 1])
{}

// reconstructLatest's fold (6.A.11 / H-007): replay `applyDiff` along
// a chained delta sequence.
ghost function Replay(c: State, ds: seq<Delta>): State
  requires ChainedFrom(c, ds)
  decreases |ds|
{
  if |ds| == 0 then c else Replay(Apply(ds[0], c), ds[1..])
}

// netDiff's fold: the groupoid ⊕ along the chain, seeded at the
// first edge.
ghost function FoldCompose(acc: Delta, ds: seq<Delta>): Delta
  requires ChainedFrom(acc.tgt, ds)
  decreases |ds|
{
  if |ds| == 0 then acc
  else FoldCompose(Compose(acc, ds[0]), ds[1..])
}

// The core induction: the fold always spans from the accumulator's
// source to wherever replay lands.
lemma FoldComposeSpans(acc: Delta, ds: seq<Delta>)
  requires ChainedFrom(acc.tgt, ds)
  ensures FoldCompose(acc, ds) == Between(acc.src, Replay(acc.tgt, ds))
  decreases |ds|
{
  if |ds| != 0 {
    FoldComposeSpans(Compose(acc, ds[0]), ds[1..]);
  }
}

// Replay along a snapshot chain's own deltas lands on the stored
// latest snapshot — reconstructLatest agrees with the fetch, for
// every chain.
lemma ReplayAgreement(states: seq<State>)
  requires |states| >= 1
  ensures ChainedFrom(states[0], ChainDeltas(states))
  ensures Replay(states[0], ChainDeltas(states)) == states[|states| - 1]
  decreases |states|
{
  ChainDeltasAreChained(states);
  if |states| > 1 {
    ReplayAgreement(states[1..]);
    assert ChainDeltas(states)[1..] == ChainDeltas(states[1..]);
  }
}

// T13 / 6.H.3 — the fundamental theorem: the folded net displacement
// IS `between genesis latest`, for every chain length. (The F#
// docstring's "both groupings recompute between genesis latest",
// proven for all chains rather than witnessed on samples.)
lemma FundamentalTheorem(states: seq<State>)
  requires |states| >= 2
  ensures ChainedFrom(states[0], ChainDeltas(states))
  ensures FoldCompose(ChainDeltas(states)[0], ChainDeltas(states)[1..])
       == Between(states[0], states[|states| - 1])
{
  ReplayAgreement(states);
  var edges := ChainDeltas(states);
  FoldComposeSpans(edges[0], edges[1..]);
  ReplayAgreement(states[1..]);
  assert edges[1..] == ChainDeltas(states[1..]);
}

// The torsor round-trip (T12, chain form): the net displacement
// applied at genesis derives exactly what replay derives — the
// stored latest.
lemma NetDiffAndReplayAgree(states: seq<State>)
  requires |states| >= 2
  ensures ChainedFrom(states[0], ChainDeltas(states))
  ensures FoldCompose(ChainDeltas(states)[0], ChainDeltas(states)[1..])
       == Between(states[0], states[|states| - 1])
  ensures Apply(FoldCompose(ChainDeltas(states)[0], ChainDeltas(states)[1..]), states[0])
       == Replay(states[0], ChainDeltas(states))
{
  FundamentalTheorem(states);
  ReplayAgreement(states);
}
