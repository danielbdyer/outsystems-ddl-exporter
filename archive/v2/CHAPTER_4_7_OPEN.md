# Chapter 4.7 open — Refactor bundle (preparatory for the 10 forward-signal items)

**Sessions:** opens with this document. **Posture:** preparatory refactoring chapter; reduces per-item touch cost for the remaining 10 forward-signal items. **Predecessors:** chapter 4.6 (forward-signal cleanup bundle); the chapter 4.6 close shortlist named ~10 pending items whose touch profile concentrates in (a) IR record-literal sites and (b) emitter signature-flow points.

---

## Why this chapter

The chapter 4.4–4.6 arc shipped four chapter-4.4-PredicateName-always-false retirements + three manifest field cash-outs + several IR record extensions. Each IR field addition required a Python-pass migration of 15–25 test files × 5–10 literal sites = ~75–250 touches. The cost is mechanical but not free — Python pass authoring + verification per chapter accumulates.

Three refactors collapse this cost across the remaining 10 items:

1. **IRBuilders retroactive sweep** (highest leverage; items 1–4 in the chapter-4.6 close shortlist) — migrate ~150–200 direct IR record literals → `IRBuilders.mkX … with <only-non-default-fields>` syntax. Adds the missing `mkReference` helper. Drops per-future-IR-field cost from ~50+ literal sites to ~2 (IRBuilders default + optional setter).
2. **Emitter-side `Diagnostics<…>` signature lift** (medium leverage; items 5, 6, 8) — promote `buildCreateIndex` + siblings to `WithDiagnostics` variants. Future emit-time consumers (CHECK constraint parse validation; Module.ExtendedProperties multi-level emission; etc.) consume the Diagnostics-bearing surface without per-consumer wiring.
3. **Small consolidations** (small leverage; chapter-wide) — `IRBuilders.testKey` to retire ~10 file-local `mkKey` / `ssKey` helpers; `getOptionalIntFlag default` adapter primitive to retire the `match getIntFlag with Ok v -> v | Error _ -> false` pattern.

After chapter 4.7 ships, the remaining 10 items each pay a fraction of their original cost.

---

## Strategic frame — eight axes named at chapter open

1. **DDD — refactors preserve type identity; no IR shape changes.** `Reference` / `Index` / etc. records keep the same shape. The refactor migrates *consumer* code (test literals, emitter signatures, adapter int-flag readers) without touching the IR contract.

2. **FP — pure refactors via mechanical migration.** The IRBuilders sweep is a syntactic transformation; runtime semantics preserved by construction. The Diagnostics signature lift threads pure `Diagnostics<'a>` through emitter functions; the existing silent-skip path stays available as `buildXxx.Value`.

3. **Hardcore — no string-concatenation paths touched.** All three refactors operate at type-system level.

4. **Streaming — no new bench scopes; existing scopes preserved.**

5. **Hexagonal — adapter primitives consolidation (refactor 3) tightens the adapter's V1-int-flag parsing surface.**

6. **Built-in obligation — IRBuilders is V2-native; no new BCL adoption.**

7. **Aggregate-root + smart constructor — IRBuilders helpers are minimum-evidence builders; tests opt in to specific field overrides via `with`-syntax. The smart-constructor invariants (A39) remain at `Catalog.create` / `Module.create` / etc. (separate from the test-fixture builders).**

8. **Test-fidelity — every test passes pre + post refactor.** The mechanical migration is structurally type-safe (F# field-omission is allowed under `with`-syntax); semantic equivalence is asserted by the existing test suite executing post-migration.

---

## Slice arc

| # | Slice | Goal | LOC budget |
|---|---|---|---|
| α | Refactor 3 — small consolidations: `IRBuilders.testKey` (replaces ~10 file-local `mkKey`/`ssKey` helpers) + `getOptionalIntFlag default` adapter primitive | Eliminates duplicate test helpers; tightens adapter int-flag pattern | ~40 src + ~80 test-file touches |
| β | Refactor 2 — Diagnostics-aware emitter signatures: `buildCreateIndexWithDiagnostics`, `buildSetExtendedPropertyWithDiagnostics`, sibling lift for ScriptDom build functions; existing silent-skip variants preserved as `.Value` callers | Future emit-time parse-failure flows have a typed surface; closes chapter 4.6 slice γ "Diagnostics-aware emitter signature" forward signal | ~120 src + ~80 test |
| γ | Refactor 1 — IRBuilders retroactive sweep: add `mkReference`; Python-pass mechanical migration of ~150 direct IR record literals to `{ IRBuilders.mkX … with <fields> }` | Future IR record-extensions touch ~2 sites instead of ~50+ | ~80 src + ~600 test-file touches (Python pass) |
| δ | Chapter-close eight-item ritual + V1 differential consolidation | Refactor bundle closed; future-touch math demonstrated on a sample new IR field | ~80 test + close ritual |

**Total: ~320 LOC src + ~760+ test-file touches.** Estimated 3-4 slices at session cadence.

---

## What this chapter does **not** do

- **No new IR fields.** The bundle is preparatory; new IR fields land in subsequent chapters.
- **No new emitters.** The Diagnostics signature lift adds sibling `WithDiagnostics` functions; existing silent-skip variants kept for legacy callers.
- **No semantic test changes.** The IRBuilders sweep is a syntactic transformation; every test passes pre + post.
- **No emitter-call-site rewiring beyond what slice β requires.** Slice β preserves backward-compatible call sites via `(buildXxxWithDiagnostics …).Value`.

---

## Open questions resolved at chapter open

**Q1 — IRBuilders sweep granularity.** Migrate every literal site, or only the ones missing critical defaults? Decision: every site. Half-measure leaves blast radius asymmetric (some new-IR-field touches dropped to ~2; others stay at full count); the chapter's promise is uniformly low cost going forward.

**Q2 — Refactor 1 verification.** Each test file's literal migration is mechanical, but field-default identification needs care. Decision: Python pass with explicit default tables per IR type; verify by full-baseline test run post-pass. If any test fails, investigate per-literal divergence.

**Q3 — Slice β backward compatibility.** Existing emitter callers (`Render.toText` / `ScriptDomGenerate.toText` / `DacpacEmitter.emit`) expect raw `TSqlStatement`. Decision: keep raw variants as `let buildXxx idx = (buildXxxWithDiagnostics idx).Value`; consumers opt into Diagnostics via the explicit-suffix variant.

**Q4 — `IRBuilders.testKey` test-file API.** Today `Fixtures.fs` has `testKey`; tests outside Fixtures.fs re-implement via `SsKey.synthesized "test" …` or `ssKey x = SsKey.original x |> Result.value`. Decision: lift `testKey` to a top-level helper exported from `IRBuilders` (or `Fixtures` if API parity matters). Migrate consumers in slice α.

---

## AXIOMS amendment scan at chapter open

No new axiom candidate. Chapter operates within existing axioms — refactors preserve A18 amended (no Policy in emitters); T1 (byte-determinism unchanged through builder surface); A39 (smart-constructor invariants on aggregate roots untouched; IRBuilders are test-fixture builders, not invariant-bearing); A40 (no new parameterization).

---

## Closing

Chapter 4.7 is **preparatory refactoring** — three orthogonal cash-outs reducing per-item touch cost for the 10 remaining forward-signal items. The largest leverage is the IRBuilders retroactive sweep (slice γ); the chapter's signature deliverable is the post-refactor touch-count math demonstrated at chapter close on a sample new IR field.

Per V2_DRIVER's per-axis correctness stakes, this is **cross-cutting infrastructure work** (Lower per-axis stakes; high cross-chapter leverage). The chapter's slice scope is correspondingly contained but mechanically substantial (~150 literal-site migrations via Python pass + ~5 emitter signature lifts + small consolidations).

Slice α opens.
