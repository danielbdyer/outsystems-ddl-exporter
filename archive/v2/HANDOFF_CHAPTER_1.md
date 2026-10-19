# Handoff letter

To the next-chapter agent. Read this before anything else in the V2 sidecar. It is short on purpose.

## Where you are

You have inherited eleven sessions of build-and-validate work plus one session of chapter-close audit (session 12). The V2 sidecar lives at `sidecar/projection/`. It builds; 585 of 588 tests pass; 3 are intentional Skip-with-rationale stubs. The architecture is honest and the disciplines hold.

You are not starting from scratch. You are continuing a multi-chapter arc whose accumulated judgment is partly in the canonical documents (`AXIOMS.md`, `DECISIONS.md`, `ADMIRE.md`) and partly in `CHAPTER_1_CLOSE.md` next to this letter.

## What to read, in order

1. **`README.md`** — but know it is materially stale. The first three priorities in CHAPTER_1_CLOSE.md exist to fix this. If you read README.md uncritically you will get the project structure wrong.
2. **`HANDOFF.md`** (this file) — the bridge between what the prior chapter knew and what you need to know.
3. **`CHAPTER_1_CLOSE.md`** — the chapter-close audit synthesis. Sections of immediate relevance:
   - §1 (Confirmations) — the disciplines you can trust.
   - §2 (Drift) — what to fix early; many are docs, not code.
   - §4 (Recommended priorities) — your next-chapter ranking.
   - §5 (Accumulated judgment) — what the prior agent was uncertain about, what the codification's untested seams are, what you should *not* do.
4. **`AXIOMS.md`** — the algebra. A1–A34 / T1–T11 with V2 amendments appended. Read top-to-bottom once; reference thereafter. **Note:** A18 has a critical amendment at the bottom of the file (line 507+) that the original A18 (line 163) does not point to. Read both.
5. **`DECISIONS.md`** — chronological operating discipline. Long. Read the most recent ten entries first; they capture the strategy-layer codification (DECISIONS 2026-05-11), the rich-profiling cash-outs (2026-05-12, 2026-05-13), and the chapter-close routing (final entries). Older entries are still in force unless explicitly superseded.
6. **`ADMIRE.md`** — V1 components and their V2 placements. **Five entries have stale status strings**; CHAPTER_1_CLOSE.md §2.1 lists them. Don't assume the status string is current.
7. **The code.** Project structure: see `Projection.sln`. Strategy modules in `src/Projection.Core/Strategies/`; pass drivers in `src/Projection.Core/Passes/`; sibling Π emitters in `src/Projection.Targets.{SSDT,Json,Distributions}/`; F# adapters in `src/Projection.Adapters.Sql/`.

## What's load-bearing

These commitments are not negotiable without explicit DECISIONS entries amending them. If you find yourself wanting to break one, write the amendment first.

- **F#-pure-core / no-I/O-in-Core.** `Projection.Core` has zero I/O. Adapters at the boundary do I/O. Audited clean (CHAPTER_1_CLOSE.md §1.1).
- **A18 amended.** Π consumes whichever subset of `Catalog × Profile` it needs, but never `Policy`. Catalog and Profile are *evidence*; Policy is *intent*. If you reach for Policy from inside an emitter, you are in the wrong layer — the work belongs in a pass.
- **Strategy-layer codification (DECISIONS 2026-05-11).** Pure functions of IR fields; typed function-type seam (`StrategyEvaluator<'context, 'config, 'decision>`); structured rationale DUs covering the decision space exhaustively; lineage events on actual decisions; module name advertises domain (`<Domain>Rules` suffix); total decisions with named skips.
- **`Composition.fanOut` for registered-intervention pass drivers.** All four current pass drivers delegate to it. Adding a fifth registered-intervention strategy means one more `FanOutConfig` construction, not a new from-scratch driver.
- **Closed-DU expansion empirical-test discipline (DECISIONS 2026-05-13).** When you add a variant to a closed DU, the seam is positioned correctly if F# exhaustiveness errors light up only at match sites and no callers outside the variant's module need reshaping. If they do, the seam is wrong and you're being told that.
- **Structural-commitment-via-construction-validation (AXIOMS.md operational principle).** Smart constructors enforce invariants the type system can't express. Every `create` returning `Result<'a>` should justify what it enforces.
- **Decimal as the default for continuous statistical evidence (DECISIONS 2026-05-13).** Use `decimal`, not `float`/`double`, for percentiles, range bounds, and continuous evidence values. T1 byte-determinism requires it.
- **Discrete-rationale DUs absorb continuous evidence by adding variants at meaningful inflection points (DECISIONS 2026-05-13).** Don't reach for `confidence: decimal` on a coarser variant. Add the variant that names the band.
- **Sibling-Π commutativity (T11).** Every Π's output should mention every catalog kind by SsKey root. Tests already exercise this across SSDT, JSON, and Distributions. New Π targets inherit the commitment.

## What's deferred but might fire under your work

These deferrals are explicit. If your work surfaces the cash-out trigger, log a DECISIONS entry — don't quietly resolve the deferral.

- **Composition primitives `fallback`, `accumulate`, `wrap`, `lift`** — codified-but-deferred at session 11 (DECISIONS 2026-05-13). Each has zero current consumers; the threshold is two. If a use case arrives, log it and extract.
- **Strategy registry mechanism** — deferred at session 8; no consumers yet.
- **Transform registry** — DECISIONS 2026-05-06 deferred at N≥4 passes; we are at N=10. **The trigger fired without cash-out.** Per CHAPTER_1_CLOSE.md §4 priority 5, log either (a) build the registry now, or (b) explain why the original framing was overtaken by the per-use-case driver pattern V2 evolved into. Do not ignore.
- **Diagnostics writer** — DECISIONS 2026-05-06 reserved the slot; no implementation. Multiple downstream artifacts (`decision-log.json`, `opportunities.json`, `validations.json`, `dmm-diff.json`, the opportunity-stream half of UniqueIndex, the operator-approval handoff for FK/Nullability) gate on it. Probably your highest-impact next-chapter milestone if the doc-hygiene work is done.
- **`RequireQualifiedAccess` retrofit** on `UniqueIndexKeepReason` and `ForeignKeyKeepReason` — DECISIONS 2026-05-11 refinement 1 says retrofit when next substantively modified. If you touch either file substantively, do the retrofit.
- **`CycleResolution.ResolutionStep.Reason` migration to structured DU** — same trigger as above.
- **Cross-catalog FK detection IR refinement** (`Catalog : string option` field) — deferred until a real fixture surfaces it. Reserved DU variant `ForeignKeyKeepReason.CrossCatalogBlocked` exists but is unreachable today. Do not delete the variant.

## What you should not do

The accumulated judgment from sessions 1–11 includes some specific don'ts:

- **Don't strip "dead code" without checking docstrings.** `ForeignKeyRules.isIgnoreRule` always returns false; `ForeignKeyKeepReason.CrossCatalogBlocked` is reserved-unreachable. Both intentional for V1 parity.
- **Don't delete the `ProfileSnapshot.fs` `Ref`/`Reference` fallback parser.** V1 emits `Reference`; the docstring shows `Ref` (typo); the parser handles both. The fallback is correct.
- **Don't treat `RawTextEmitter` as an SSDT replacement.** It is a debug/diff-oracle synthetic-milestone form. Real CREATE TABLE / DacFx work belongs in a future `Projection.Targets.SSDT.DacpacEmitter` as a third sibling Π, not a rewrite.
- **Don't extract speculative composition primitives.** Two-consumer threshold (DECISIONS 2026-05-13). If you find yourself naming an abstraction with one current consumer, stop.
- **Don't refactor `ForeignKeyRules.evaluate` to take a unified `'context`.** The closure-based adaptation in `ForeignKeyPass.run` is the documented pattern; it honors "uniform signature shape but variable arity context."
- **Don't pull Faker forward.** The session-11 reflection is explicit: Faker waits until at least three evidence types exist. We have two (Categorical, Numeric). Either add a third evidence type first, or proceed with Faker on two and explicitly accept the limitations.
- **Don't reach for Policy from a Π.** A18 amended forbids it. If your emitter wants what feels like Policy, the work is enrichment (a pass) producing emitter-consumable values, not Π directly.

## Disposition

The prior chapter operated under a few dispositions worth inheriting:

- **Audit during validation.** When a finding surfaces second-order, act on it before shipping. The five paydowns across sessions 4, 5, 7, 8, 11 all came from this disposition. The codification's stability mark (DECISIONS 2026-05-13) is itself the same discipline applied at the chapter boundary.
- **IR grows under evidence, not speculation.** New types, fields, and DU variants land when a consumer demands them. Two-consumer threshold for helper extraction; same threshold for composition primitives; analogous threshold for evidence types in the rich-profiling agenda.
- **Total decisions, named skips.** Strategies return decisions for every input; "no decision" is named as a `KeepReason` variant rather than represented as silence. V2 audit chains gain reasons V1 lacked because V1 silently skipped.
- **Cherry-pick discipline.** Every commit atomic and reversibly mergeable. The V2 sidecar references no V1 trunk source files; the boundary is data, not typed cross-references.
- **Documentation is the bridge.** When you finish meaningful work, ask: would a fresh agent reading only the docs understand what changed and why? If not, the docs are incomplete.

## Where to start

Per CHAPTER_1_CLOSE.md §4, the recommended order is:

1. **README.md absorption** — ~1 hour. Highest-leverage doc work; misroutes everything that follows if left stale.
2. **ADMIRE status sweep** — ~30 minutes. Five entries.
3. **Skip-stub completion** — ~1 hour. Three test files.
4. **Two missing TopologicalOrderPass tests** — moderate.
5. **Transform registry cash-out** — ~30 minutes thinking + DECISIONS entry.

After doc hygiene, the substantive next moves are likely either the **Diagnostics writer** (gates a large class of V1 outputs) or a **third evidence type + Faker emitter** (rich-profiling vector continuation). The session-11 reflection gives empirical context for both.

## Closing

You inherit a codebase whose architectural disciplines hold under audit, whose codification is at its stability mark, and whose documentation is mostly honest about what's been done. The drift is mostly cosmetic. The hard work — the strategy-layer codification, the rich-profiling extension, the deferred-decisions cash-out — is behind you and behind the codebase.

The chapter you open is yours to shape. The disciplines above are not constraints; they are the load-bearing structure that lets the chapter ahead support more weight than the one behind. Hold the spine.

— The session 1–12 architect.
