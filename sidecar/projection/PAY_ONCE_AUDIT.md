# PAY_ONCE_AUDIT — the single-receipt discipline, named and hunted

> Opened 2026-07-02 (operator directive: name the consolidation shape
> precisely and search for it intentionally). This document carries the
> named law, the audit method, and the adjudicated receipt ledger. The
> findings sections are filled from the audit workflow's verified output;
> the frame stands on its own.

## 1 — The law

**Every fact enters the system once, at its cheapest sufficient
representation; everything else derives from that single payment by pure,
threaded functions.**

This is the perf-dual of the engine's soul. The adjunction
(`Ingest ∘ Project = identity`, modulo named erasures) is a conservation
law downward: *nothing is lost in silence*. The pay-once law is the same
conservation upward: *nothing is paid twice in silence*. The change ledger
counts every displacement once; the cost ledger must count every fact
once. A second payment is an unbalanced book.

## 2 — The four planes (+ the tower)

A second payment hides on exactly one of these planes:

| Plane | The duplicated cost | Session exemplars |
|---|---|---|
| **Acquisition** | the wire/server paid twice for one substrate | P1 (profiler re-read what the data lanes held); batched nullability reflection |
| **Representation** | one piece of information re-encoded hop after hop | P3 (cell → Map → typed Map → AST literal); typed values round-tripped through `"i:"+ToString` keys |
| **Derivation** | a pure `f(x)` recomputed while `x` never changed | topo re-runs; `emittedNames` ×3 per kind; per-item linear scans |
| **Materialization** | something built eagerly that no consumer demands at that strength | the ~100MB `Fused` string guarding one boolean gate |
| **Tower** (structural) | per-element wrappers each re-wrapping every element to carry trivial state | two probe layers = 2 Tasks/row carrying a counter |

## 3 — Why the payments were invisible: the naming theorem

**A second payment is invisible until the shared value has a name.**
`Fused` hid because the interleave had no on-demand surface; the topo
re-runs hid because nothing threaded `ComposeState.TopologicalOrder`; the
carrier tax hid because `StaticRow` was "the" row and the quantum had no
consumers. Conversely, every fix in the family was *naming plus
threading*: `TargetKeySet`, `readsideIdentity`, `loadForWith`,
`renderQuanta`, `*Using` forms, `SamplingPolicy`.

So the audit predicate is not "find slow code." It is: **find the unnamed
intermediate value with two purchase receipts.** Corollaries the house
already operates: discover-once/derive-pure (EvidenceCache), carriers
reify eagerly, `Catalog.kindIndex`'s `ConditionalWeakTable` (the ONE
sanctioned cache shape: keyed by the immutable value itself, so staleness
is impossible by construction — caching is otherwise refused in favor of
threading).

## 4 — The audit method (repeatable)

1. **Sweep** — plane-specific finders over disjoint areas, information
   only: each finding must cite BOTH receipts (`file:line` where the fact
   is first paid; where it is paid again), the payment grain
   (per-cell/row/kind/publish/verb), the path class, and the NAME the
   shared value would take. Exclusion list = everything already executed
   or declined-with-reasons (PERF_OPPORTUNITIES).
2. **Adversarial verify** — two independent skeptics per finding, strict
   AND-survival, each defaulting to *refuted* under uncertainty:
   - the **value-identity skeptic**: are the receipts really for the same
     fact over the same immutable input? (This lens is what caught
     `Rendered` being a non-copy before a refactor was wasted on it.)
   - the **consumer skeptic**: does some consumer genuinely need the
     second form — a law surface (A35 carriers), a verification purpose
     (canary re-reads), a staleness boundary?
3. **Synthesize** — a completeness critic names what each plane failed to
   surface; adjudication and ranking happen here, by hand, grain × path ×
   risk, each survivor carrying its named carrier, identity gate, and
   measurement leg.

## 5 — Verified receipt ledger

*(pending — filled from the audit workflow's verified output)*

## 6 — Killed findings (the skeptics' work, kept for the record)

*(pending)*

## 7 — Coverage gaps the critic named

*(pending)*
