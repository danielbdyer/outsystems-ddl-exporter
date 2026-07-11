// R4-LC (FORMAL_METHODS.md §2/§4): bounded model check of the Lifecycle
// monotone-history machine (Projection.Core/Lifecycle.fs; L3-L2).
//
// The F# machine: a Lifecycle is a chain of CatalogSnapshots along one
// Timeline, opened at a genesis snapshot; `Lifecycle.append` accepts a
// new snapshot IFF its Version ordinal strictly exceeds the latest,
// and refuses (named: lifecycle.append.nonMonotonic) otherwise — prior
// history is never altered.
//
// The model: versions are the totally-ordered sig V; the chain state is
// the var set Chain; an append transition carries the F# guard. A
// refused append is a non-transition (the machine stutters) — the F#
// Result.failure changes nothing.
//
// What this adds over the rung-3 suite (LifecycleAppendExhaustiveTests,
// 341 concrete traces): the temporal laws are checked over EVERY
// interleaving of appends and stutters up to the step bound, for every
// ordinal universe up to the scope — including the derived theorem
// that an ordinal once skipped below the frontier is locked out
// forever, which the concrete suite never states.
//
// Rung-4 boundary, stated honestly: bounded model checking — the scopes
// below are the claim's bound. (TLA+/TLC is the named upgrade path for
// unbounded checking; TLC is not fetchable in the build environment —
// see FORMAL_METHODS.md §4.)

module Lifecycle

open util/ordering[V]

// Version ordinals. `first` is the genesis snapshot's ordinal.
sig V {}

// The snapshot chain: the set of appended version ordinals. Insertion
// order equals ordinal order by construction of the append guard, so a
// set (plus the ordering) faithfully carries the F# list.
var sig Chain in V {}

fun latest : one V { max[Chain] }

// ---------------------------------------------------------------------------
// Transitions.
// ---------------------------------------------------------------------------

// Lifecycle.genesis: the chain opens with exactly the genesis snapshot.
pred init { Chain = first }

// Lifecycle.append, accepted: the F# guard `nextOrdinal > lastOrdinal`.
pred append[v: V] {
  gt[v, latest]
  Chain' = Chain + v
}

// A refused append (non-monotone ordinal) or an idle step: no change.
pred stutter { Chain' = Chain }

fact traces {
  init
  always { stutter or (some v: V | append[v]) }
}

// ---------------------------------------------------------------------------
// Laws (check = must hold on every trace within scope; expect 0 = no
// counterexample).
// ---------------------------------------------------------------------------

// L3-L2 (i): history is append-only — the chain only ever grows.
check HistoryImmutable {
  always (Chain in Chain')
} for 6 but 1..12 steps expect 0

// L3-L2 (ii): the genesis snapshot is never lost.
check GenesisPersists {
  always (first in Chain)
} for 6 but 1..12 steps expect 0

// L3-L2 (iii): the frontier never retreats — `latest` is monotone.
check FrontierMonotone {
  always (lte[latest, latest'])
} for 6 but 1..12 steps expect 0

// Derived theorem: an ordinal below the frontier that is not in the
// chain can never enter it (the guard admits only above-frontier
// ordinals and the frontier never retreats). This is the "no silent
// reorder / no late insert" content of the named refusal.
check SkippedIsLockedOut {
  always (all v: V |
    (lt[v, latest] and v not in Chain) implies always (v not in Chain))
} for 6 but 1..12 steps expect 0

// ---------------------------------------------------------------------------
// Sanity (run, expect 1 = satisfiable): the model is not vacuously
// over-constrained — a full chain is reachable, and a sparse chain
// (holes below the frontier) is representable.
// ---------------------------------------------------------------------------

run FullChainReachable {
  eventually (Chain = V)
} for 4 but 1..8 steps expect 1

run SparseChainRepresentable {
  eventually (some v: V | lt[v, latest] and v not in Chain)
} for 4 but 1..8 steps expect 1
