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
| **3 — exhaustive** | in-language enumeration of a *finite* input space; the property holds at **every** point | violation is *impossible within the enumerated space* — proof by exhaustion | **Phase 1, shipped** (§3) |
| **4 — model-checked** | external checker over a bounded universe (Alloy 6: relational + full temporal LTL) | violation is *impossible within the bound*, incl. temporal/liveness claims tests cannot state | **Phase 2, shipped** (`formal/alloy/`, §4) |
| **5 — proven** | SMT-backed prover (Dafny + Z3) on the algebraic core | violation is *impossible, unconditionally* — machine-checked theorem | **Phase 3 beachhead, shipped** (`formal/dafny/`, §4) |

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
| Lifecycle temporal laws over every interleaving: history append-only, genesis persists, frontier monotone, skipped-ordinal lockout | **4** (bounded) | 4 | `formal/alloy/Lifecycle.als` — 4 checks + 2 sanity runs |
| Cutover ladder safety (R6, T-30/T-15, N-consecutive; formerly "governance, not code"): Driver only by evidence+sign-off, V1 warm before Sunset, archive Sunset-only, T-15 retreat holds, counter honesty, fallback always enabled — plus nothing-forces-promotion | **4** (bounded; model N=3 stands for policy N=10) | 4 | `formal/alloy/CutoverLadder.als` — 6 checks + 4 reachability runs |
| `ApprovalState` machine + registry: verdict exclusivity, Pending-carries-no-reviewer (the pre-slice-2 illegal state, temporal form), no silent transitions, suppression ⇔ rejection, re-approval escapability | **4** (bounded) | 4 | `formal/alloy/Approval.als` — 5 checks + 2 runs |
| Part VI illegal-states catalog: the four Tier-1 quadrants (1.A/1.B/1.C/1.H) machine-confirmed representable today; Campaign B.4's `Fortified` invariants proven to close each, without vacuity | **4** (bounded) | 4 | `formal/alloy/Catalog.als` — 2 theorems + 4 witnesses + 4 fortified checks + 1 run |
| A44 `expressible ⇔ reachable` as state-space coverage | 2 (canary enumeration) | **4** | deferred — Phase 2.1; trigger: next config-control-plane slice |
| Identity conservation (A5 + the torsor's conserved charge): `SsKey` multiset changes only by named transitions | 2 | **4** | deferred — Phase 2.1; trigger: next pass-pipeline structural change |
| `CatalogDiff` groupoid laws (T13/A-Lifecycle-4/M12: compose associativity, identities, inverses, functor law), the fundamental theorem (netDiff = between genesis latest, all chain lengths), replay agreement (reconstructLatest), NM-45 non-composable-unreachable | **5** | 5 | `formal/dafny/CatalogDiffAlgebra.dfy` — 25 verified obligations |
| Writer-monad laws for `Lineage` (A24): monad triple, chronological-trail laws, value-plane untouched, Kleisli category laws | **5** | 5 | `formal/dafny/LineageWriter.dfy` — 8 verified obligations |
| T1 determinism kernel | 2 | **5** | deferred — a prover model would not yet bind to the emission pipeline; trigger: emitter-core extraction |

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

## 4 — Rungs 4–5, as shipped (2026-07-11)

- **Phase 2 (model checking) — `formal/alloy/`, four specs.** Tool decision, recorded
  honestly: TLA⁺/TLC was the plan's named checker for the temporal machines, but the TLC
  jar is unreachable from the build environment (GitHub release assets are session-scoped;
  the non-GitHub mirrors reset; not on Maven Central). **Alloy 6** (Maven Central,
  sha256-pinned, headless `exec` CLI) carries full temporal LTL — `var` state,
  `always`/`eventually`/`after`, bounded lasso traces — so the temporal machines shipped
  as Alloy specs with the same guarantee class: exhaustive within the stated bound,
  liveness included. TLC remains the named upgrade path for unbounded checking; trigger:
  network access to a TLC distribution, or a state space the bounded check cannot cover.
  Every command carries an explicit `expect`; a mismatch in EITHER direction fails the
  runner (a law breaking, a model going vacuous, or an illegal-state witness silently
  closing all fail alike).
- **Phase 3 (prover beachhead) — `formal/dafny/`, two modules.** Dafny 4.11 + Z3 (F★'s
  named alternative; chosen because Dafny installs as a dotnet tool inside the repo's
  existing toolchain). The binding contract is stated in each module header: Dafny proves
  the ALGEBRA for all inputs unconditionally; the correspondence between the F# functions
  and the verified model is pinned by the existing rung-2 property suites
  (`ChangeAlgebraSweepTests`, `AdjunctionLawTests`, `LineageTests`, `DiagnosticsTests`).
  A separate lane — never on the main `dotnet build` path.
- **The runner + gate.** `scripts/model-check.sh` (sibling to `test.sh`/`perf-gate.sh`):
  fetches pinned tools on demand into `formal/.tools` (never committed), runs every Alloy
  spec against its expectations and every Dafny module to zero errors, and enforces the
  **anti-drift citation gate** — every `formal/` path this file cites must exist on the
  tree. CI runs it via `.github/workflows/formal-projection.yml`. The deeper M16
  integration (per-axiom citations from `AxiomTests.fs` into formal artifacts) is the
  named follow-on; trigger: the next `AXIOMS.md` amendment that lands with a formal
  witness.

## 5 — Maintenance

- A rung claim without a live witness is a first-class defect — same rule as `AxiomTests.fs`
  citations; fix in the commit that discovers it.
- New rung-3 suites follow §3's four-step pattern and register a §2 row in the same commit.
- Demotions (a suite deleted, a bound narrowed) amend the ledger in the same commit, with a
  `DECISIONS.md` entry naming why.
