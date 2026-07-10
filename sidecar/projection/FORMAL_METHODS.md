# FORMAL_METHODS.md — the verification ladder

> **Status: program adopted 2026-07-10** (operator approval; the "provably so" directive —
> formalize the sidecar's invariants beyond sampled witnesses, up the full ladder). Companion
> to `AXIOMS.md` (the laws), `AxiomTests.fs` (the executable witnesses), and
> `AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md` (the L0–L3 coverage triage). This document
> owns one question: **by what *means* is each law discharged, and how strong is that means?**
> It restates no axiom and no bucket — it points.

---

## 1 — The ladder

Five rungs, ordered by the strength of the guarantee. Rungs 1–2 the codebase already
operates; this program adds 3–5 where (and only where) the triage says they pay.

| Rung | Means | Guarantee shape | Standing in this codebase |
|---|---|---|---|
| **1 — types** | smart constructors, closed DUs, private ctors, identity-as-type | violation is *unconstructable* | the L1 inventory (audit Part V); Bucket A's backbone |
| **2 — sampled** | FsCheck properties (~160 across the suite) | violation is *improbable* — falsification by ~100 random points | the `[<Property>]` corpus; `AxiomTests.fs` |
| **3 — exhaustive** | in-language enumeration of a *finite* input space; the property holds at **every** point | violation is *impossible within the enumerated space* — proof by exhaustion | **this program, Phase 1** (shipped; §3) |
| **4 — model-checked** | external checker (TLA⁺/TLC for temporal, Alloy for relational) over a bounded universe | violation is *impossible within the bound*, incl. temporal/liveness claims tests cannot state | Phase 2 (planned; §4) |
| **5 — proven** | SMT-backed prover (F★) on the algebraic core | violation is *impossible, unconditionally* — machine-checked theorem | Phase 3 (beachhead only; §4) |

The load-bearing distinction between rungs 2 and 3+: **a property test falsifies; an
enumeration verifies.** `AXIOMS.md`'s T1–T16 are named *theorems*; a sampled witness leaves
them theorems-by-convention. Where the input space is genuinely finite — closed DUs ×
booleans × a discretized evidence class — sampling it is leaving proof on the table.

**Scope discipline (what this program does NOT do).** Rungs 3–5 target Bucket **B/C/D**
entries only (convention-enforced, weak, or unnamed). Re-proving Bucket A — laws the type
system already makes unforgeable — is negative value and does not land. The triage source of
truth stays the verifiability-triangle audit; this file tracks *rung*, not *bucket*.

## 2 — The rung ledger

One row per law this program has touched or targets. `Rung` is the **achieved** level;
`Target` the committed one. Citations follow the `AxiomTests.fs` discipline: the backticked
test name is the witness; a ledger row without a live witness is a defect.

| Law | Rung | Target | Witness |
|---|---|---|---|
| Nullability signal hierarchy is total, deterministic, and conforms to its documented decision table (A39-adjacent; DECISIONS 2026-05-09/10) | **3** | 3 | `NullabilityRulesDecisionTableTests.fs` — `R3-NULL` suite (128-point space) |
| FK gate ladder is total, deterministic, conforms to its documented precedence; reserved variants (`CrossCatalogBlocked`, `DeleteRuleIgnored`) unreachable; V1-parity toggles outcome-inert (DECISIONS 2026-06-12) | **3** | 3 | `ForeignKeyRulesDecisionTableTests.fs` — `R3-FK` suite (1024-point space) |
| A12 policy-axis orthogonality under operation traces (H-098) | **3** (depth ≤ 6, representative payloads) | 4 | `PolicyStateMachineTests.fs` — `R3-POL` bounded-exhaustive section (55,987 traces) |
| Lifecycle monotone-append (L3-L2): accepts exactly the strictly-increasing extensions; every rejection is the named refusal | **3** (ordinal universe ≤ 4, depth ≤ 4) | 4 | `LifecycleAppendExhaustiveTests.fs` — `R3-LC` suite (341 traces) |
| Cutover ladder safety/liveness (R6, T-30/T-15, N=10; today "governance, not code") | 0 (prose) | **4** | Phase 2 — `formal/tla/CutoverLadder.tla` |
| `ApprovalState` transition machine; registry last-write-wins + suppression | 1 | **4** | Phase 2 — `formal/tla/Approval.tla` |
| Part VI illegal-states catalog (PK⇒unique, identity⇒not-null, physical-name uniqueness, FK-graph validity) | 0–1 (mixed) | **4** | Phase 2 — `formal/alloy/Catalog.als` |
| A44 `expressible ⇔ reachable` as state-space coverage | 2 (canary enumeration) | **4** | Phase 2 — reachability frame over the movement space |
| Identity conservation (A5 + the torsor's conserved charge): `SsKey` multiset changes only by named transitions | 2 | **4** | Phase 2 — place-invariant in the TLA⁺ pass model |
| `CatalogDiff` groupoid laws (T12–T16: compose associativity/identity, FTC replay) | 2 | **5** | Phase 3 — `formal/fstar/CatalogDiff.fst` |
| Writer-monad laws for `Lineage`/`Diagnostics` (A24) | 2 | **5** | Phase 3 — `formal/fstar/Lineage.fst` |
| T1 determinism kernel | 2 | **5** | Phase 3 — where tractable; scope gated |

## 3 — Rung 3, the operating pattern (Phase 1, shipped)

The four suites share one shape — **enumerate, oracle, and quantify**:

1. **Factor the input space** into its finite coordinates (closed DUs × booleans ×
   a *discretized evidence class* — e.g. `NoProfile | ZeroNulls | WithinBudget |
   BeyondBudget` stands in for the unbounded numeric profile, one representative
   realization per class).
2. **Assert the cardinality** — the enumeration's length equals the product of the factor
   sizes. A silently-shrunken space is the failure mode of hand-rolled exhaustion; the
   cardinality check makes it loud.
3. **Transcribe the documented contract as an independent oracle** — the decision table in
   the rule module's own docstring, written a second time as data. Conformance at every
   point proves *code ⇔ documented spec*, which is the claim that matters (a bug shared by
   oracle and implementation is caught by the independent per-law quantifications, which do
   not consult the oracle).
4. **Quantify the named laws over the whole space** — precedence ("override is absolute"),
   non-interference ("profile is consulted only through the mandatory branch"),
   unreachability ("reserved DU variants never occur"), inertness ("V1-parity toggles never
   change an outcome").

Boundaries stated honestly: rung 3 proves the property **over the enumerated space**. The
discretization is part of the claim — a law about `WithinBudget` holds for the class's
representative, and the class boundaries themselves (the `observed <= allowed` edge) get
their own explicit edge points. Trace suites are exhaustive over the **op alphabet** at
representative payloads, to the stated depth — the same abstraction the H-098 Machine
variant samples, now swept completely.

## 4 — Rungs 4–5, the forward program

- **Phase 2 (model checking).** `formal/tla/` (Lifecycle, Approval, CutoverLadder;
  identity-conservation as a place-invariant) and `formal/alloy/` (the Part VI structural
  catalog). A `scripts/model-check.sh` sibling to `test.sh`/`perf-gate.sh`; CI-gated.
  Apalache if TLC's explicit state space blows up.
- **Phase 3 (prover beachhead).** `formal/fstar/` on the 3–4 algebraic crown jewels only —
  a separate lane, never on the main `dotnet build` path.
- **The anti-drift contract** (extends the M16 citation gate): every external artifact is
  cited from `AxiomTests.fs` by law id; CI asserts the cited artifact exists, its last
  check ran green, and the ledger's `Rung` matches the artifact class. A law claiming
  `model-checked` with no green run **fails the gate**. Until Phase 2 lands, the gate's
  surface is this file's §2 table — kept honest by review, exactly as far as prose can be.

## 5 — Maintenance

- A rung claim without a live witness is a first-class defect — same rule as `AxiomTests.fs`
  citations; fix in the commit that discovers it.
- New rung-3 suites follow §3's four-step pattern and register a §2 row in the same commit.
- Demotions (a suite deleted, a bound narrowed) amend the ledger in the same commit, with a
  `DECISIONS.md` entry naming why.
