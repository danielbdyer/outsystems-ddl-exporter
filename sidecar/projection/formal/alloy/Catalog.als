// R4-CAT (FORMAL_METHODS.md §2/§4): relational model of the Catalog
// IR's structural invariants — the executable form of the
// verifiability-triangle audit's Part V §5.2 (what `Catalog.create`
// enforces today) and Part VI (the catalog of illegal states still
// representable).
//
// The spec has three layers:
//   1. FACTS — the invariants the production smart constructor
//      enforces now. Every instance Alloy exhibits below satisfies
//      them, so every witness is a value `Catalog.create` would BUILD.
//   2. WITNESSES (run, expect 1) — the audit's Part VI Tier-1 illegal
//      states, each shown SATISFIABLE under today's facts: a
//      machine-checked proof that the gap is real, not an audit
//      opinion. If a future `Catalog.create` hardening closes one,
//      its witness goes UNSAT and the expectation here flips — the
//      spec is the live ledger of which quadrants remain open.
//   3. FORTIFIED (check, expect 0) — under the audit's proposed
//      Campaign B.4 invariants, each illegal state is UNREPRESENTABLE.
//      This is the campaign's precise, checkable specification.
//
// Rung-4 boundary: bounded — all claims hold within the scopes below.

module Catalog

// ---------------------------------------------------------------------------
// Signatures — the IR skeleton (Catalog.fs), at invariant altitude.
// Boolean flags are subset sigs; physical coordinates are atoms.
// ---------------------------------------------------------------------------

sig SsKey {}
sig SchemaName {}
sig TableName {}

sig Kind {
  key    : one SsKey,
  schema : one SchemaName,   // Physical realization (TableId)
  table  : one TableName,
  attrs  : set Attribute,
  refs   : set Ref,
  idxs   : set Idx
}

sig Attribute { akey : one SsKey }
sig MandatoryAttr, NullableCol, IdentityAttr, PkAttr in Attribute {}

sig Ref {
  rkey   : one SsKey,
  src    : one Attribute,
  target : one SsKey        // references target Kinds by identity (A4)
}

sig Idx {
  ikey : one SsKey,
  cols : set Attribute
}
sig UniqueIdx, PkIdx in Idx {}

// ---------------------------------------------------------------------------
// Layer 1 — FACTS: what Catalog.create enforces today (audit Part V
// §5.2, invariants 1–5) plus ownership shape.
// ---------------------------------------------------------------------------

fact TodaysInvariants {
  // Every attribute / reference / index belongs to exactly one kind.
  all a: Attribute | one attrs.a
  all r: Ref       | one refs.r
  all i: Idx       | one idxs.i

  // (1)(2) Kind SsKeys are disjoint across the catalog.
  all disj k1, k2: Kind | k1.key != k2.key

  // (3) Every reference's source attribute is among its kind's attributes.
  all k: Kind | k.refs.src in k.attrs

  // (4) Every reference's target kind exists in the catalog.
  all r: Ref | some k: Kind | k.key = r.target

  // (5) Every index's columns are among its kind's attributes.
  all k: Kind | k.idxs.cols in k.attrs
}

// ---------------------------------------------------------------------------
// Derived theorems under today's facts (check, expect 0) — properties
// the enforced invariants already buy.
// ---------------------------------------------------------------------------

// FK-graph well-formedness: every reference resolves to EXACTLY ONE
// kind (existence is fact 4; uniqueness follows from key disjointness).
check RefsResolveUniquely {
  all r: Ref | one key.(r.target)
} for 6 expect 0

// Reference sources never dangle outside the owning kind — a rename
// or attribute move cannot orphan an FK source silently.
check RefSourcesAreOwned {
  all k: Kind, r: k.refs | r.src in k.attrs
} for 6 expect 0

// ---------------------------------------------------------------------------
// Layer 2 — WITNESSES: the Part VI Tier-1 illegal states, each
// representable TODAY (run, expect 1 = Alloy exhibits a catalog that
// Catalog.create would happily build).
// ---------------------------------------------------------------------------

// Part VI 1.A — IDENTITY column that is nullable (SQL forbids
// IDENTITY NULL; the IR does not).
run IllegalIdentityNullable {
  some (IdentityAttr & NullableCol)
} for 4 expect 1

// Part VI 1.B — logically mandatory column that is physically
// nullable (the semantic contradiction the nullability pass must
// then adjudicate).
run IllegalMandatoryNullable {
  some (MandatoryAttr & NullableCol)
} for 4 expect 1

// Part VI 1.C — a primary-key index that is not unique (PK is unique
// by SQL definition; the flags can disagree).
run IllegalPkIndexNotUnique {
  some (PkIdx - UniqueIdx)
} for 4 expect 1

// Part VI 1.H — two kinds sharing one physical (schema, table)
// coordinate: emission would write one file over the other.
run IllegalDuplicatePhysicalName {
  some disj k1, k2: Kind | k1.schema = k2.schema and k1.table = k2.table
} for 4 expect 1

// ---------------------------------------------------------------------------
// Layer 3 — FORTIFIED: Campaign B.4's proposed invariants. Under
// them, every Layer-2 illegal state is unrepresentable (check,
// expect 0). This predicate is the campaign's specification: when
// Catalog.create adopts these checks, move each clause into
// TodaysInvariants and flip the matching Layer-2 witness to expect 0.
// ---------------------------------------------------------------------------

pred Fortified {
  no (IdentityAttr & NullableCol)                  // 1.A
  no (MandatoryAttr & NullableCol)                 // 1.B
  PkIdx in UniqueIdx                               // 1.C
  all disj k1, k2: Kind |                          // 1.H
    (k1.schema != k2.schema or k1.table != k2.table)
}

check FortifiedClosesIdentityQuadrant {
  Fortified implies no (IdentityAttr & NullableCol)
} for 6 expect 0

check FortifiedClosesMandatoryQuadrant {
  Fortified implies no (MandatoryAttr & NullableCol)
} for 6 expect 0

check FortifiedClosesPkUniqueQuadrant {
  Fortified implies PkIdx in UniqueIdx
} for 6 expect 0

check FortifiedClosesPhysicalCollision {
  Fortified implies
    (all disj k1, k2: Kind | k1.schema != k2.schema or k1.table != k2.table)
} for 6 expect 0

// Fortification is satisfiable — it does not over-constrain the IR
// into vacuity (a well-formed multi-kind catalog still exists).
run FortifiedIsInhabited {
  Fortified and #Kind >= 2 and some Ref and some (PkIdx & UniqueIdx)
} for 4 expect 1
