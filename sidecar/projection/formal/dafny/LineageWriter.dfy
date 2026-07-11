// R5-LIN (FORMAL_METHODS.md §2/§4): machine-checked proofs of the
// lineage writer monad's laws — A24 (chronological composition) and
// the monad-law triple the rung-2 suites (DiagnosticsTests /
// LineageTests, ~34 properties) witness by sampling.
//
// THE MODEL AND ITS BINDING. `Lineage<T>` is the writer monad over an
// event trail: a computed value plus the chronological sequence of
// LineageEvents that produced it (Projection.Core/Lineage.fs). The F#
// realization: `Lineage.retn` (empty trail), `Lineage.bind` (trail is
// `m.Trail @ newEvents` — earliest-first concatenation, A24). Dafny
// proves the laws for ALL values, functions, and trails; the
// correspondence of the F# combinators to this model is pinned by the
// existing rung-2 law suites. The same laws lift to the stacked
// `LineageDiagnostics` writer (A24's chronological bind extends to
// the stack — CLAUDE.md §5), whose trail component composes
// identically.

// A lineage event — opaque here; the algebra never inspects payloads.
type Event(==)

datatype Lineage<T> = L(value: T, trail: seq<Event>)

// Lineage.retn — a pure value carries no history.
ghost function Return<T>(x: T): Lineage<T> { L(x, []) }

// Lineage.bind — run the continuation on the value; the composite
// trail is EARLIEST-FIRST: the input's history, then the
// continuation's (A24).
ghost function Bind<T, U>(m: Lineage<T>, f: T -> Lineage<U>): Lineage<U> {
  var r := f(m.value);
  L(r.value, m.trail + r.trail)
}

// ---------------------------------------------------------------------------
// The monad-law triple (the executable witnesses' names in
// LineageTests cite these directly).
// ---------------------------------------------------------------------------

// Left identity: binding a pure value is just the continuation.
lemma LeftIdentity<T, U>(x: T, f: T -> Lineage<U>)
  ensures Bind(Return(x), f) == f(x)
{
  assert [] + f(x).trail == f(x).trail;
}

// Right identity: binding into `retn` changes nothing — no phantom
// events are ever minted.
lemma RightIdentity<T>(m: Lineage<T>)
  ensures Bind(m, x => Return(x)) == m
{
  assert m.trail + [] == m.trail;
}

// Associativity: regrouping a pipeline never changes the value OR the
// audit trail (seq concatenation is associative — the reason A24's
// discipline is coherent at all).
lemma Associativity<T, U, V>(m: Lineage<T>, f: T -> Lineage<U>, g: U -> Lineage<V>)
  ensures Bind(Bind(m, f), g) == Bind(m, x => Bind(f(x), g))
{
  var fm := f(m.value);
  var gm := g(fm.value);
  assert (m.trail + fm.trail) + gm.trail == m.trail + (fm.trail + gm.trail);
}

// ---------------------------------------------------------------------------
// A24 — the chronological-trail laws, stated on their own (these are
// the writer-specific content over and above the monad triple).
// ---------------------------------------------------------------------------

// The composite trail is exactly earliest-first concatenation: every
// event of the earlier computation precedes every event of the later
// one; none are reordered, lost, or duplicated.
lemma ChronologicalTrail<T, U>(m: Lineage<T>, f: T -> Lineage<U>)
  ensures Bind(m, f).trail == m.trail + f(m.value).trail
  ensures |Bind(m, f).trail| == |m.trail| + |f(m.value).trail|
  ensures forall i :: 0 <= i < |m.trail| ==> Bind(m, f).trail[i] == m.trail[i]
  ensures forall j :: 0 <= j < |f(m.value).trail| ==>
    Bind(m, f).trail[|m.trail| + j] == f(m.value).trail[j]
{}

// A26's companion: bind never alters the computed value's dependence
// on the continuation — the trail layers separately from the value
// plane (the probe-is-identity-on-the-value-plane discipline).
lemma ValuePlaneUntouched<T, U>(m: Lineage<T>, f: T -> Lineage<U>)
  ensures Bind(m, f).value == f(m.value).value
{}

// ---------------------------------------------------------------------------
// Kleisli composition (the Pass<'a,'b> pipeline form: passes compose
// as arrows; the laws above make the pipeline a category).
// ---------------------------------------------------------------------------

ghost function Kleisli<T, U, V>(f: T -> Lineage<U>, g: U -> Lineage<V>): T -> Lineage<V> {
  x => Bind(f(x), g)
}

lemma KleisliAssociative<T, U, V, W>(
    f: T -> Lineage<U>, g: U -> Lineage<V>, h: V -> Lineage<W>, x: T)
  ensures Kleisli(Kleisli(f, g), h)(x) == Kleisli(f, Kleisli(g, h))(x)
{
  Associativity(f(x), g, h);
}

lemma KleisliIdentities<T, U>(f: T -> Lineage<U>, x: T)
  ensures Kleisli(y => Return(y), f)(x) == f(x)
  ensures Kleisli(f, y => Return(y))(x) == f(x)
{
  LeftIdentity(x, f);
  RightIdentity(f(x));
}
