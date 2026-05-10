# CLAUDE.md — V2 Sidecar Navigation

This file is the first-read pointer for fresh agents. It does **not**
substitute for the canonical documents — it points at them. All
substantive disciplines, axioms, and resolved questions live in the
files this document indexes; this file's only job is to make sure
nothing load-bearing is missed.

If you are an agent opening this codebase for the first time, read
the documents in the order this file lists. If you are an agent
returning across sessions, this is the navigation surface; the
substantive surfaces are unchanged.

## Reading order for a fresh agent

`KICKOFF.md` is the fresh-agent first-message brief — read it first as a
5-minute orientation that points at the canonical surfaces in the order
below. Per `DECISIONS 2026-05-22 — CLAUDE.md reading-order update`,
`VISION.md` is item 2 (the strategic frame for the cutover); the
companion strategic surfaces (`SPINE.md`, `PLAYBOOK.md`, `STAGING.md`,
`BACKLOG.md`) are **on-demand** references — read when the relevant
work surfaces them, not as part of the canonical first-read pass.

1. **`HANDOFF.md`** — bridge letter from the most-recent-closed
   chapter. Short on purpose. Names what is load-bearing and what
   is deferred. Older chapters' handoff letters preserved at
   `HANDOFF_CHAPTER_<N>.md` (currently `HANDOFF_CHAPTER_1.md`).
2. **`VISION.md`** — strategic frame for V2: cutover as forcing
   function; sibling chorus + verification posture; acceptance
   criteria; cutover fallback ladder. Read for the *why*. Companion
   strategic surfaces (`SPINE.md` for the categorical structure;
   `PLAYBOOK.md` for technical guidance; `STAGING.md` for the Stage
   0 foundation phase; `BACKLOG.md` for the full ~375-item
   inventory) are referenced on demand.
3. **`CHAPTER_3_1_CLOSE.md`** — chapter-3.1 close synthesis (sessions
   27–36). The canary chapter. Read for the M1–M3 milestone sequence;
   the four meta-codifications (bench-driven optimization, stream-
   realization pattern, five-agent epistemic-tier audit, harmonization-
   via-parameterization); forward signals into chapter 3.2 / 3.5 /
   4.1 / 4.2. **Companion file:** `AUDIT_2026_05_DDD_HEXAGONAL_FP.md`
   carries the chapter-close five-agent DDD/Hexagonal/FP audit with
   Tier 1/2/3/4 backlog by epistemic level + leverage. The next-
   chapter agent reads both at chapter open.
4. **`CHAPTER_2_CLOSE.md`** — chapter-2 close synthesis (sessions
   13–25). Read for the OSSYS adapter chapter's accumulated
   state (25 translation rules), the three-class typology, the
   meta-codifications (chapter-mid-audit; trace-before-fixture;
   V1-envelope-walk), and the chapter-3 forward signals.
   **Companion files at the projection root:**
   `CHAPTER_2_AUDIT_3_OSSYS_COMPLETENESS.md` (subagent #3's
   full audit report); `CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md`
   (subagent #4's chapter-open input); and
   `CHAPTER_3_PRESCOPE_SNAPSHOT_ROWSETS.md` (subagent #5's
   chapter-open input).
5. **`CHAPTER_1_CLOSE.md`** — chapter-1 close synthesis (sessions
   1–12). Read for historical context. Some priorities listed
   there have been resolved by chapters 2 / 3.1; the disciplines
   and load-bearing commitments persist.
5. **`AXIOMS.md`** — the formal system. A1–A34 / T1–T11 with
   amended originals. A18's amendment near the bottom is the
   load-bearing form for sibling Π's; the original A18 carries a
   forwarding pointer. A1's bound on identity-survives-rename
   through the JSON path is documented at the bottom (added
   session 23). The "Amendments scheduled (chapter close)"
   section at the very bottom carries the placeholder list for
   pending amendments (T1 binary-normal-form; T11 structural-type
   encoding; T11 diff-typed inputs; A1 four-variant SsKey; A35
   candidate; A36 candidate; A32 cash-out) — chapter agents fill
   the bodies at chapter close per `DECISIONS 2026-05-22 — Stage
   0 foundation phase`.
6. **`DECISIONS.md`** — append-only resolved-questions log. Read the
   most recent ten entries first; older entries remain in force
   unless explicitly superseded. Two indexes at the top:
   *Active deferrals* (catches silent-trigger fires across chapters)
   and *Operating disciplines* (cross-cutting practices, pointing
   at the substantive entries). The Stage 0 governance burst
   (2026-05-22) cluster at the bottom carries the five
   pre-chapter-3 entries: Stage 0 commitment, R6 split-brain rule,
   chapter 3 sequencing, CLAUDE.md reading-order, T-30 / T-15
   fallback ladder gates.
7. **`ADMIRE.md`** — V1↔V2 bridge. One entry per V1 component
   admired and placed in V2. Three modes: V1-migration / V2-growth
   / hybrid (`DECISIONS 2026-05-13` — admire spectrum). Multi-
   session chapters use `extracting (in flight, N slices)` while
   in flight (session 23 amendment).
8. **`README.md`** — surface-level orientation; updated at chapter
   closes. Not the source of truth for any specific question.
9. **The code.** `Projection.sln`. Strategies in
   `src/Projection.Core/Strategies/`; passes in
   `src/Projection.Core/Passes/`; sibling Π emitters in
   `src/Projection.Targets.{SSDT,Json,Distributions}/`; F# adapters
   in `src/Projection.Adapters.{Sql,Osm}/`. The OSSYS adapter at
   `src/Projection.Adapters.Osm/CatalogReader.fs` is chapter 2's
   substantive deliverable.

## Operating disciplines — the cross-cutting practices

These disciplines cut across substantive work. Each links to its
codifying DECISIONS entry; if you find yourself working against one
of them, write the amendment first.

| Discipline | Where to find the rationale |
|---|---|
| **Domain-first naming and ubiquitous-language consistency (pillar 8; chapter 3.7 sidebar)** — every named type / function / file / module / test in V2 MUST embody the four-question domain-naming analysis BEFORE the name is committed: (1) what domain concept does this represent (articulate it in cutover-business terms); (2) does V2 already name this concept somewhere (use the same name; ubiquitous-language consistency across Core / Adapters / Targets / Pipeline / CLI); (3) is the proposed name concept-shaped (what it IS) or action-shaped (what it DOES); (4) generic-suffix smell test — Helper / Util / Manager / Service / Handler / Processor / Wrapper / Builder / Factory / Provider stop the agent. If #4 fires, find the concept (rename) or restructure (the concept is being squashed). The named failure mode is **domain-blind naming**: when a name answers "what does this DO" rather than "what does this REPRESENT in the domain." Fails to put the domain concept in the type system; the agent feels productive (a name exists; the code compiles) without doing the domain-modeling work. **No lint enforcement** — heuristic syntactic checks misfire on legitimate uses (e.g., `LineageBuffer` is concept-shaped despite the "Buffer" suffix). The discipline-document path catches what the heuristic can't. See `PLAYBOOK.md` decision tree "When you reach for a name" for the executable form. Worked precedents (concept-shaped, ubiquitous): `Catalog` / `Module` / `Kind` / `Reference` / `SsKey` / `RemovalReason` / `AnnotationDetail` / `Coordinates.TableId` / `RawValueCodec` / `SqlTypeCorrespondence` / `BatchSplitter` / `DatabaseNameGenerator` / `EmissionPolicy` / `LineageBuffer`. Worked rename: `T11TypeTheoremTests.fs` → `SiblingEmitterContractTests.fs` (chapter 3.7 slice ε; concept-shaped name names what the file IS, not which theorem ID it cites). | `DECISIONS 2026-05-10` — Domain-first naming and ubiquitous-language consistency (pillar 8) |
| **LINT-ALLOW substantive-rationale discipline** (chapter 3.7 sidebar; pillar 7 amendment) — every per-line `LINT-ALLOW` marker on a string-composition / built-in-substitute site MUST embody the four-question analysis BEFORE the marker is committed: (1) what is the use-case-specific library; (2) is it already in the codebase; (3) what is the cost of using it (visibility lift + perf class + dep weight); (4) is there a structural reason it doesn't apply. If #4 is "no," there is no shortcut — there is the work (lift visibility, add helper, refactor call site). The named failure mode is **performance-of-compliance**: a marker shaped like an audit trail without the substance. The lint passes, the vocabulary fits, the tests are green — and the structural commitment is unmet. The discipline document does the catching the heuristic can't. See `PLAYBOOK.md` decision tree "When you reach for a string-composition primitive" for the executable form. Lint Rule 27 maintains an inventory + soft floor; substance lives in the discipline. | `DECISIONS 2026-05-10` — LINT-ALLOW substantive-rationale discipline (worked counterfactual: slice-β `Render.columnSqlType` shortcut → slice-β' ScriptDom delegation; cost was 87 LOC) |
| **Audit during validation** — when something second-order surfaces during the work, act on it before shipping. Five paydowns across sessions 4, 5, 7, 8, 11; three more during session 14. | `DECISIONS 2026-05-09 — Audits surface things not on the agenda` (line 764) |
| **IR grows under evidence, not speculation** — types, fields, DU variants, and helpers land when a consumer demands them. Two-consumer threshold for helper extraction. | `DECISIONS 2026-05-07` — IR grows under evidence, not speculation |
| **Total decisions, named skips** — strategies return decisions for every input; "no decision" is a named `KeepReason` variant rather than silence. | `DECISIONS 2026-05-11 — Strategy-layer codification: empirical verdict after the fourth instance` (line 1557; refinement 3) |
| **Closed-DU expansion empirical-test discipline** — when adding a variant, F# exhaustiveness errors should light up only at match sites; if callers outside the variant's module need reshaping, the seam is wrong. | `DECISIONS 2026-05-13` — Closed-DU expansion: empirical confirmation |
| **Two-consumer threshold for emergent primitives** — extract a helper / primitive at the second consumer, not the first. Codified for `fanOut`; deferred for `fallback` / `accumulate` / `wrap` / `lift`. | `DECISIONS 2026-05-13` — Emergent primitives earn their place through multi-consumer demand |
| **Decimal as default for continuous statistical evidence** — T1 byte-determinism requires it; `float`/`double` arithmetic varies by host. | `DECISIONS 2026-05-13` — Decimal is the default for continuous statistical evidence |
| **Discrete-rationale DUs absorb continuous evidence by adding variants at meaningful inflection points** — don't reach for `confidence: decimal` on a coarser variant; add the variant that names the band. | `DECISIONS 2026-05-13` — Discrete-rationale DUs absorb continuous evidence |
| **Pass return-type codification** — passes return `Lineage<'output>` when they produce only decisions; `Lineage<Diagnostics<'output>>` when they produce decisions plus observer-relevant findings. The shape names the production. | `DECISIONS 2026-05-13` — Pass return-type codification (session 14) |
| **Named accessors for stacked types whose nested access loses self-description** — `lineage.Value.Value` is a smell when readers must count projections to know which writer they're on. Provide module-level accessors. | `DECISIONS 2026-05-13` — Named accessors for stacked types (session 14) |
| **Contract-vs-implementation cross-reference in audits** — any audit walking contract-vs-test must also walk contract-vs-implementation. The "no test, no implementation" finding is a feature gap, not a test gap. | `DECISIONS 2026-05-13` — Audit discipline refinement (session 14) |
| **Active deferrals re-checked at chapter close** — silent-trigger fires get caught by table-scan, not by chronological re-read. The transform-registry deferral fired without cash-out for ~7 sessions; the index exists so it doesn't recur. | `DECISIONS 2026-05-13 — Transform registry cash-out + Active deferrals index` (codifying entry; session 13) — index lives at the top of `DECISIONS.md` |
| **Document the false starts** — preserve the wrong rule alongside the right one. Future agents recognize the temptation when it recurs; documentation captures the discipline's discovery, not just its outcome. | `DECISIONS 2026-05-13 — Pass return-type codification (session 14)` and `DECISIONS 2026-05-13 — Named accessors for stacked types (session 14)` — both carry preserved-false-start prose embodying the discipline |
| **Anticipation vs. speculation in abstraction extraction** — refines the two-consumer threshold with three positions (A/B/C) and an empirical test for "shape visible enough." Position B (structural alignment when the shape is concrete) earns its place; Position A (full extraction) requires both shape visibility and concrete second consumer; Position C (defer fully) is the default. | `DECISIONS 2026-05-13` — Anticipation vs. speculation in abstraction extraction (session 14) |
| **Admire entries fall on a spectrum (V1-migration / V2-growth / hybrid)** — every ADMIRE entry's template choice (what V1 gives us / what V2 adds) is governed by the entry's mode. Three modes named; chapter-2 added the `extracting (in flight, N slices)` status for multi-session chapters in flight (session 23 amendment). | `DECISIONS 2026-05-13 — Admire entries fall on a spectrum (V1-migration / V2-growth / hybrid)` (line 1862; session 23 amendment for in-flight status) |
| **Writer codification stability mark via heterogeneous-third-test protocol** — the dual-writer pattern (Lineage + Diagnostics) reached codification stability when its third real test (FK with maximum heterogeneity) held without API expansion. Four core predictions confirmed (return-type signature, named-accessor surface, opportunityEntry shape, no API expansion). Mirrors the strategy-layer codification stability mark. | `DECISIONS 2026-05-14 — Writer codification reaches its stability mark (heterogeneous third test held)` (line 3929; session 16) |
| **`opportunityEntry` extraction-defer at N=3-of-distinct-shapes** — refines the two-consumer threshold with shape-distinction analysis: surface count of consumers is not enough; if three consumers share two distinct shapes, the third is not a third consumer for extraction purposes. The three opportunityEntry functions across UniqueIndex / Nullability / ForeignKey passes share two shapes (UniqueIndex + ForeignKey are similar; Nullability is structurally different), so extraction defers despite N=3. Mirrors anticipation-vs-speculation as a refinement on the two-consumer threshold. | `DECISIONS 2026-05-14 — opportunityEntry stays inlined: N=3 of two distinct shapes, not N=3 of one` (line 4039; session 16) |
| **Chapter-close ritual** — eight load-bearing items every chapter close must execute (Active deferrals scan; contract-vs-implementation walk; CLAUDE.md / README.md staleness checks; HANDOFF + CHAPTER_N_CLOSE.md scope; fresh-eye walk; operating-disciplines table currency; **V1-input-envelope walk** for V1↔V2 translation chapters — added at session-25 chapter-2-close per the subagent #3 finding that chapters grow won't-carry-forward lists under fixture pressure rather than V1-input pressure). Recurring audits codify into rituals; ad-hoc investigations don't compound. | `DECISIONS 2026-05-14` — Chapter-close ritual (session 15; session 25 amendment for V1-envelope walk) |
| **Strategic-frame axis-naming at chapter open** — multi-session chapters (especially V1↔V2 translation chapters and architectural-arc chapters like `Projection.Pipeline`) name the chapter's load-bearing axes at chapter open, before substantive slices begin. The OSSYS chapter named eight axes at session 17; the framework-extension amendment (session 23) confirms the pattern for multi-session chapters generally. Future chapters (`Projection.Pipeline` canary; `SnapshotRowsets` implementation) inherit. | `DECISIONS 2026-05-15 — Strategic frame for the OSSYS implementation chapter` (session 17) plus the session 23 framework-extension amendment at `DECISIONS 2026-05-13` (admire spectrum) |
| **Chapter-mid-audit** — multi-session chapters dispatch a cross-document consistency audit subagent at intervals during the chapter (typically every 3–5 substantive sessions). Surfaces mid-flight propagation drift before it compounds at chapter close. Findings categorized CRITICAL / MINOR / OPEN; CRITICAL fix in next hygiene work; MINOR rolls to chapter close via CHAPTER_N_CLOSE scaffold; OPEN warrants discussion. **Active deferrals scan is a required dimension** on every dispatch (session 24 amendment): pointer drift and trigger-fire drift are different cost classes; only explicit framing catches the latter. Pairs with the chapter-close ritual. | `DECISIONS 2026-05-19` — Chapter-mid-audit as a routine practice (session 23; session 24 amendment) |
| **Trace-before-fixture** — when writing a new slice in a V1↔V2 translation chapter, trace V1's actual handling first (SQL extraction + JSON projection). Classify the finding into one of three classes (see "Three-class typology" below) before writing the failing test. The classification informs the resolution shape. Slice-level admire-mode; pairs with chapter-level admire from chapter open. | `DECISIONS 2026-05-19` — Trace-before-fixture pattern at slice level (session 23; codified at N=3) |
| **Three-class typology for V1↔V2 translation findings** — JSON-projection-lossiness (V2 can't see X; resolved by input-path expansion); V2-boundary-discipline (V2 sees X but has no axis; resolved by filter / carry-through / IR-refinement); alternative-IR-surface (V2 sees X; primary IR has no axis; parallel V2 surface is the natural home — route there, possibly making V1 input redundant). Each class has different composability and coupling characteristics. The trace-before-fixture pattern operates the classification. | `DECISIONS 2026-05-21` — Chapter 2 close: alternative-IR-surface class (session 25; completes the typology at N=2 per class) |
| **DECISIONS is for resolved questions, not session narrative** — substantive entries (disciplines, refinements, cash-outs, codifications) stay; session-narrative content (commit lists, test baselines, forward signals, rent-paying checks, recaps) lives in commit messages, PR descriptions, HANDOFF.md, CHAPTER_1_CLOSE.md, or the conversation. The substance test: would this entry still be useful in six months? Append-only protects against revisionism; prune-when-wrong protects against narrative drift. | `DECISIONS 2026-05-14` — DECISIONS is for resolved questions (session 15) |
| **Stage 0 foundation phase ships as one coherent unit before chapter 3.1 opens** — the twelve foundation items (S0.A–S0.L per `STAGING.md`) are codified in F# types (Tier 2), structural commitment (Tier 3), and primitive support (Tier 4) before any chapter-3 slice opens. Tier 1 is documentation hygiene + governance burst (S0.F AXIOMS scaffolding; S0.G five DECISIONS entries; S0.J currency checks; S0.L cross-references). The Stage 0 commitment is the structural answer to "should we just start chapter 3.1 and refactor as we go?" Per SPINE inference I6, the contract precedes its instances. | `DECISIONS 2026-05-22` — Stage 0 foundation phase ships as one coherent unit |
| **R6 split-brain governance during dual-track** — V2 emits-but-doesn't-ship while V1 owns the production write path; the canary asserts V1 ≈ V2 modulo named tolerances; disagreement blocks the PR; per-environment-per-artifact-type V2-driver transition is gated on N=10 consecutive green canary runs plus operator sign-off. The four-environment cutover stays per-pair; the gate flips when its evidence supports the flip. The Tolerance taxonomy (S0.E) is the governance surface — every divergence either matches a named tolerance or fails the canary. | `DECISIONS 2026-05-22` — R6: Split-brain governance rule for the dual-track cutover window |
| **T-30 / T-15 cutover fallback ladder gates** — V2-driver mode requires four conditions met by T-30: (a) chapter 3 closed with green canary on full 300-table Catalog; (b) chapter 4.1 (data triumvirate) shipping; (c) chapter 4.2 (User FK reflow) shipping; (d) ≥1 full UAT dry-run. T-30 yellow → V2-augmented (V1 drives, V2 verifies). T-15 unstable (canary CI flake >10%; tolerance churn) → V1-only retreat. Hard rule: V1 stays warm through cutover+30 regardless. The gates determine the ladder rung; R6 governs per-pair progression along the rung. | `DECISIONS 2026-05-22` — T-30 / T-15 cutover fallback ladder gates |
| **AXIOMS amendments scaffolded at chapter open; bodies filled at chapter close** — the "Amendments scheduled (chapter close)" section at the bottom of `AXIOMS.md` is the placeholder list for pending amendments. Chapter 3.1 close cashed A35 (Π's output is a deterministic statement stream), A36 (bulk-vs-incremental is realization-layer policy), A39 (aggregate-root smart-constructor invariants), A40 (harmonization-via-parameterization). Renumbered the prior A35/A36 candidates (Π-erased axes; CatalogDiff exhaustiveness) to A37/A38. Chapter agents fill the body when the chapter that earns the amendment closes. The scaffolding is a structural forcing function: chapter close cannot complete without resolving its placeholders. | `DECISIONS 2026-05-22` — Stage 0 foundation phase (S0.F scaffolding); chapter-3.1 close (sessions 27–36) for A35/A36/A39/A40 |
| **Bench-driven optimization protocol** — performance optimizations at hot paths require three-candidate / 2-refuted / 1-confirmed shape with bench data. Refuted swaps are documented with bench data so the same swap doesn't recur. The bench surface is how V2 earns its perf claims, the same way the canary's PhysicalSchema diff earns its fidelity claims. | `DECISIONS 2026-05-24 — Bench surface caught two wrong-direction canary optimizations` |
| **Iterator-logging is a first-class outcome over time** (codified 2026-05-09 chapter 3.6 sidebar) — every loop / iteration / lazy-stream pull emits a `Bench` sample so per-iteration distribution surfaces in the rollup table, not just per-call totals. The primitives: `Bench.scope` (RAII synchronous timing), `Bench.iterDo` / `Bench.iteriDo` / `Bench.iterMap` (per-element samples on `seq` / `list`), `Bench.streamProbe` (lazy-sequence throughput probe — records `<label>` total ms + `<label>.elements` count on enumeration completion), `Bench.streamTransit` (per-element backpressure samples), `Bench.recordSample` (external counter surfacing). All accumulators thread-safe, lock-protected. Default to `iterDo` / `iterMap` over a bare `for x in xs do`; default to `streamProbe` over a bare `seq` consumer. Stats roll up at every level (nested scopes compose); the rollup table sorts by `TotalMs` descending so expensive operations surface at the top. Operators reading the bench output should see Count + Mean + P50/P95/P99 per label. Adopting the iterator-logging primitives is structurally equivalent to TDD — the perf surface earns its place by being visible in every operator interaction. | Bench primitives at `src/Projection.Core/Bench.fs:103-233`; persist boundary at `src/Projection.Pipeline/BenchSink.fs`; canonical CLI consumer at `src/Projection.Cli/Program.fs:dumpBench` |
| **Canary as load-bearing forcing function** (codified 2026-05-23 + chapter-3.1 close + 2026-05-09 operator-reality amendment) — the canary's PhysicalSchema round-trip diff against an OutSystems-shaped source DDL is V2's primary wide integration surface. Tiers: schema-only canary (`fixtures/canary-gate.sql`, ~1.5s warm) runs on **SessionEnd hook** for the operator-confidence smoke; generator-scale canaries (`Generator bulk: 1k/10k/100k rows/table` in `GeneratorScaleTests`) exercise the bulk realization path; **operator-reality canary** (`Operator-reality canary: 50k rows × 300 tables, variegated` — `GenerateSpec.operatorReality`, ~10-12s warm) is the production-shape baseline; realistic 300-table canary gated behind `PROJECTION_RUN_REALISTIC_CANARY` env var (too slow for unit tests). The canary deploys source to one ephemeral DB, reads back via `ReadSide`, runs V2's emitter on the reconstruction, deploys to a second DB, reads back, asserts source ≈ target on `PhysicalSchema`. Empty diff = structural fidelity holds. **Per-commit + per-Stop-hook gate is operator-reality** (`scripts/perf-gate.sh` invokes `dotnet test --filter "FullyQualifiedName~Operator-reality"` with `PROJECTION_BENCH_DIR=$ROOT`); **per-session smoke is canary-gate.sql** (schema only, ~1.5s); **nightly is bulk100k + realistic 300-table** (full forcing function). Operator decision (2026-05-09): schema-only canary-gate.sql is **inappropriate** for the production-use-case perf baseline; the gate must exercise the production envelope (300 tables, 50k rows, variegated FK density) so feature additions can't silently regress under operator-reality conditions. Statistical perf regression gating: per-label `μ + Kσ` (default K=3.0) against rolling `bench/history-canary.jsonl` (max 20 runs); warm-up phase falls back to flat `BENCH_TOLERANCE` (default 1.5×) until N=5 history accumulates. Stop-hook timeout in `.claude/settings.json` is 60s (canary ~12s + statistical analysis ~1s + buffer); do not drop below 30s. | `DECISIONS 2026-05-23 — Source SQL Server with OutSystems semantics`; `DECISIONS 2026-05-09 — Operator-reality canary as the production-baseline perf gate`; `CHAPTER_3_1_CLOSE.md` (canary milestone arc); `.claude/hooks/session-end.sh` (smoke); `scripts/perf-gate.sh` (operator-reality statistical gate; chapter-3.6 cash-out) |
| **Stream-realization pattern (chapter-3.1 contribution)** — Π's canonical output is a typed deterministic stream (`seq<Statement>` for SSDT). Realization layers (`Render.toText`, `Deploy.executeStream`) consume the stream and choose their emission form. The algebra (A18 / T1 / T11) holds at the stream level; bulk-vs-incremental deploy is realization-layer policy invisible to Π (A35 / A36). | `DECISIONS 2026-05-28 — Session 34 / A35 cash-out` and `Session 34 / A36 cash-out` |
| **Writer-fidelity discipline (chapter-3.1 contribution)** — pass drivers MUST use `LineageDiagnostics.tellDiagnostics` / `Lineage.ofValueAndEvents` (the canonical primitives); manual record-building is forbidden. The dual-writer's algebraic surface is activated across pass drivers; future pass drivers inherit the discipline. | `DECISIONS 2026-05-30 — Session 36 / Writer-fidelity codification` |
| **Harmonization-via-parameterization (chapter-3.1 contribution)** — when two implementations of an algorithm diverge on a single semantic axis, parameterize the algorithm on that axis, produce both projections from one implementation, and let consumers choose. Worked example: `SelfLoopPolicy` in `TopologicalOrderPass` (chapter-3.1 collapsed `RawTextEmitter.emissionOrder`'s duplicate Kahn into the pass). Codified as A40. | `DECISIONS 2026-05-30 — Session 36 / Topological-sort harmonization via SelfLoopPolicy` |
| **Five-agent epistemic-tier audit at chapter close (chapter-3.1 contribution)** — multi-agent parallel audit dispatched at chapter close covering tightly orthogonal concerns (UL / Hex / VO / FP / ACL). Each agent classifies findings B&W vs SUBJ + H/M/L; convergence-map is the synthesis primary surface; Tier 1/2/3/4 backlog organizes findings by epistemic level + leverage. Audits are routed (named items in named chapters with named pre-scopes), not piled. Worked example: `AUDIT_2026_05_DDD_HEXAGONAL_FP.md`. | `DECISIONS 2026-05-30 — Session 36 / Five-agent DDD/Hexagonal/FP audit protocol` |

## Load-bearing commitments — do not break without writing the amendment first

These are not negotiable without an explicit DECISIONS entry that
names the prior commitment and supersedes it. If you find yourself
wanting to break one, write the amendment first.

- **F#-pure-core / no-I/O-in-Core.** `Projection.Core` has zero I/O.
  Audited clean (`CHAPTER_1_CLOSE.md §1.1`).
- **A18 amended.** Π consumes whichever subset of `Catalog × Profile`
  it needs, but never `Policy`. Catalog and Profile are *evidence*;
  Policy is *intent*. If you reach for Policy from inside an emitter,
  you are in the wrong layer — the work belongs in a pass.
- **Strategy-layer codification (`DECISIONS 2026-05-11`).** Pure
  functions of IR fields; typed function-type seam
  (`StrategyEvaluator<'context, 'config, 'decision>`); structured
  rationale DUs covering the decision space exhaustively; lineage
  events on actual decisions; module name advertises domain
  (`<Domain>Rules` suffix); total decisions with named skips.
- **`Composition.fanOut` for registered-intervention pass drivers.**
  All registered-intervention pass drivers delegate to it.
- **Decimal as default for continuous statistical evidence.**
- **Sibling-Π commutativity (T11).** Every Π's output should mention
  every catalog kind by SsKey root. **Note:** T11 is currently
  *aspirational* not structural — three Π's return `string`. Chapter
  3.5's Π port realization makes T11 structural via the typed
  `ArtifactByKind<'element>` surface.
- **A35 (chapter-3.1).** Π's canonical output is a typed
  deterministic *statement stream* (`seq<Statement>` for SSDT).
  Realization layers consume the stream and choose their emission
  form; the algebra holds at the stream level.
- **A36 (chapter-3.1).** Bulk-vs-incremental is realization-layer
  policy. How a realization deploys (`SqlBulkCopy`, per-row INSERT,
  file write, network protocol) is invisible to Π.
- **A39 (chapter-3.1).** Aggregate-root smart-constructor
  invariants. `Catalog.create` / `Module.create` / `ColumnProfile.create`
  enforce referential-integrity / empirical-probe invariants in one
  pass; consumers that flow through `create` trust the value.
- **A40 (chapter-3.1).** Harmonization-via-parameterization.
  Single-axis-divergent implementations earn one parameterized
  algorithm; same algorithm, multiple projections.
- **Writer-fidelity (chapter-3.1).** `LineageDiagnostics.tellDiagnostics`
  and `Lineage.ofValueAndEvents` are the canonical pass-driver
  primitives. Manual record-building is forbidden.
- **V2 owns no production write path during dual-track (R6).** Per
  `DECISIONS 2026-05-22 — R6`, V2 emits-but-doesn't-ship while V1
  owns the production write path. The canary asserts V1 ≈ V2 modulo
  named tolerances; disagreement blocks the PR. This eliminates
  split-brain by construction. Per-environment-per-artifact-type
  V2-driver transition is gated on N=10 consecutive green canary
  runs plus operator sign-off; the four-environment cutover stays
  per-pair, never global.
- **V1 stays warm through cutover+30.** Per `DECISIONS 2026-05-22 —
  T-30 / T-15 cutover fallback ladder gates`, V1's emission path
  is preserved as a fallback for thirty days post-cutover regardless
  of which mode the cutover entered. V1 sunset deferred to chapter
  5+ when all four environments have run V2 emissions for one full
  schema-evolution cycle.
- **Stage 0 ships before chapter 3.1 opens.** Per `DECISIONS
  2026-05-22 — Stage 0 foundation phase ships as one coherent unit`,
  the twelve foundation items per `STAGING.md` ship as one unit
  before any chapter-3 slice. Tier 1 (S0.F / S0.G / S0.J / S0.L)
  is the documentation-only governance burst; Tier 2 (S0.A) is the
  type-primitives keystone; Tier 3 (S0.B) is the structural-
  commitment refactor; Tier 4 (S0.C–S0.K) is primitive support
  modules in parallel. The chapter-1 baseline (631 passing tests)
  holds at every Stage 0 step.

## Programming style — the center target

The codebase has a coherent style. These are the gravitational
patterns; new code lands inside them by default. Each guideline
points at the canonical rationale rather than restating it. Where
the canonical surface is the code itself, the pattern is named.

### Posture

- **The type system is the contract.** Smart constructors return
  `Result<'a>` for every value type that carries an invariant; closed
  DUs make exhaustiveness compiler-checked; identity (`SsKey`,
  `Name`) is a distinct type the compiler refuses to confuse with a
  string. The first place to encode a constraint is the type system,
  not a runtime check. (`AXIOMS.md` operational principle —
  structural-commitment-via-construction-validation.)
- **Determinism is constructed, not validated.** Sort by `SsKey`
  before scanning. Use `decimal` for continuous statistical evidence
  (never `float`/`double`). No `DateTime.Now`, `Random`, or I/O in
  Core — the boundary supplies clock values; passes consume them.
  T1 byte-determinism holds because every choice supports it.
- **Defaults are minimal.** No comments unless the WHY is
  non-obvious. No abstractions unless a second consumer forces
  extraction. No fields, variants, or helpers ahead of evidence. IR
  grows under demand, not speculation. Premature anything is the
  failure mode.
- **Make divergences visible.** When V2 deliberately differs from
  V1, the difference surfaces as a `Skip` test stub at the test-file
  level, not as ADMIRE prose. When a strategy makes "no decision,"
  the named keep-reason variant says so structurally; silence is
  forbidden. Total decisions, named skips.
- **Audit during the work.** When something second-order surfaces,
  act on it before shipping. The codification absorbs refinements
  during validation, not afterward. Five paydowns across sessions
  4–11; three more during session 14. (`DECISIONS 2026-05-09` —
  Audits surface things not on the agenda.)

### Types

- **Records for products; closed DUs for sums.** F# records carry
  PascalCase fields; closed DUs widen only when evidence forces a
  new variant.
- **Smart constructors return `Result<'a>`.** Every value type whose
  invariants the type system can't express directly carries a
  `create` that returns `Result<'a>` and rejects malformed inputs.
  Downstream consumers pattern-match without re-validating; the
  invariant rides on every value. Worked examples:
  `CategoricalDistribution.create`, `NumericDistribution.create`,
  `SsKey.original`, `Name.create`.
- **`[<RequireQualifiedAccess>]` when case names may collide.**
  Outcome and KeepReason DUs across strategies share generic case
  names (`PolicyDisabled`, `EvidenceMissing`); F# resolves
  ambiguity by picking one, which produces silent miscompilation.
  Add the attribute when names are likely to recur. Worked
  examples: `NullabilityOutcome`, `UniqueIndexOutcome`,
  `ForeignKeyOutcome`.
- **`option` for absence; never null.** `Nullable=enable` plus
  `TreatWarningsAsErrors=true` is the project setting; null escapes
  fail compilation.
- **Identity is a type, not a string.** `SsKey` is a single-case DU
  (`Original of string | Derived of original × reason`); core code
  never holds a string in a place where identity belongs. Names
  (`Name`) are presentation-only.
- **Generic algebraic names in the core; domain-prescriptive names
  at the boundary.** `Kind`, `Module`, `Catalog`, `Reference` —
  not `Entity`, `Application`, `Model`, `FK`. The trunk's
  domain-prescriptive vocabulary lives in adapter translation.

### Functions

- **Pure functions, top to bottom.** Pipe operator `|>` is the
  default; reads as "do this, then this, then this." Mutable state
  only function-local for performance-sensitive algorithms (Tarjan
  SCC, ResizeArray accumulators) — never module-level.
- **Explicit type annotations on public surfaces.** Inferred types
  on private helpers. The canonical pass shape is
  `Catalog -> Policy -> Profile -> Lineage<'output>` (or
  `Lineage<Diagnostics<'output>>` when the pass produces both
  decisions and observer-relevant findings; see pass return-type
  codification).
- **Composition over open-coding.** Use the existing primitives
  (`Composition.fanOut`, `Lineage.bind`, `Diagnostics.tellMany`,
  `LineageDiagnostics.bind`). Don't reinvent. Don't extract a new
  primitive until a second consumer needs it.
- **Result composition for boundary code.** Adapters return
  `Result<'a>`; consumers compose with `Result.bind`. Exceptions
  only for true invariant violations the type system couldn't
  prevent.
- **Named accessors for stacked types whose nested access loses
  self-description.** `lineage.Value.Value` is a smell when readers
  must count projections; `LineageDiagnostics.payload`,
  `LineageDiagnostics.entries`, and domain shortcuts like
  `UniqueIndexPass.decisionsOf` are the discipline.

### Documentation in code

- **Default to no comments.** Well-named identifiers state WHAT.
  Comments belong only where WHY is non-obvious — a subtle
  invariant, a hidden constraint, a workaround that surprises.
- **Cite the canonical surface.** Comments and docstrings reference
  the axiom (`// A24: trail is f ++ g, earliest-first`) or
  decision (`// per DECISIONS 2026-05-09 — observable identity on
  empty policy`) that justifies the shape. Cross-references
  compound; they keep the canonical docs reachable from the code.
- **Don't restate what the code does.** "Returns the deep payload"
  on `payload` is appropriate; "increments the counter by one" on
  `incrementByOne` is not.
- **No multi-paragraph docstrings; no multi-line comment blocks.**
  Triple-slash F# docstrings on public types and modules are short
  paragraphs that name the algebraic role and the canonical
  reference. Detail belongs in DECISIONS.

### Tests

- **Test names cite the axiom or theorem they enforce.** F#
  backtick-quoted identifiers carry the law:
  `` ``A4: kinds with same SsKey are structurally equal`` ``,
  `` ``T1: Project is deterministic`` ``,
  `` ``A24: trail is chronological under bind`` ``. Failing tests
  point directly at the law they claim to satisfy.
- **`Skip = "..."` for deliberate V2 divergences from V1.** The
  rationale lives in the Skip string. The test appears in test
  discovery so the divergence is structurally visible. Reserve
  contract names via Skip stubs *before* implementation lands; flip
  Skip to `[<Fact>]` when the gating dependency arrives.
- **`Skip` rationale either names the reachability gap (a feature
  not yet built) or the deliberate divergence (V2 chose differently).**
  Don't conflate. A reserved-but-unbuilt contract is different
  from a deliberately-omitted V1 contract.
- **Property tests for combinatorial spaces; example tests for
  specific contracts.** FsCheck.Xunit covers permutation
  invariance, idempotence, deterministic-output-under-shuffling.
  xUnit covers worked examples that name a specific behavior.
- **Per-file test helpers at the top.** `let private mkKey`,
  `let private entry`, etc. — small named constructors for the
  file's fixtures. Avoids boilerplate in each test; keeps the
  test's intent visible.
- **Don't re-validate smart-constructor invariants.** The
  `Result<'a>` from a `create` is unwrapped via `Result.value` in
  test fixtures; the production code trusts the value. Tests for
  the constructor itself test rejection; tests for downstream
  consumers don't.

### Naming

- **Types: generic algebraic names.** `Kind`, `Module`, `Catalog`,
  `Reference`, `Profile`. The codebase serves OutSystems today and
  must accommodate DACPAC, OData, etc.
- **Modules: `<Domain>Rules` for registered-intervention
  strategies.** `NullabilityRules`, `UniqueIndexRules`,
  `ForeignKeyRules`, `CategoricalUniquenessRules`. Other suffixes
  admissible when the call pattern differs (e.g.,
  `CycleResolution` is a structural strategy, not a registered
  intervention).
- **Pass modules under `Passes/` named after the pass.**
  `NullabilityPass`, `UniqueIndexPass`, etc. Pass version is a
  `[<Literal>]` constant inside the module.
- **Source / Code conventions for diagnostics.** `Source` is
  `<PassName>` or `adapter:<adapter-name>` or
  `emitter:<emitter-name>`. `Code` is dot-separated with a
  routing top-prefix (`tightening.*`, `profiling.*`, `adapter.*`).

### Cross-cutting commitments (carried from the operating disciplines table)

- Every transformation runs inside `Lineage<_>` (A25). Every
  pass-produced decision emits one lineage event. Lineage trail is
  earliest-first under bind (A24).
- Profile is independent of Catalog and Policy (A34); no
  back-references. Passes that don't consume Profile produce
  identical output for `Profile.empty` and any populated profile.
- Π consumes whichever subset of `Catalog × Profile` it needs but
  never `Policy` (A18 amended). If an emitter wants what feels
  like Policy, the work is enrichment (a pass) producing
  emitter-consumable values.
- Pass return shape names what the pass produces:
  `Lineage<'output>` for decisions only, `Lineage<Diagnostics<'output>>`
  when decisions plus observer-relevant findings.

## F# feature surface — alignment, conscious omissions, candidates

The codebase uses a deliberate slice of F#'s feature surface. Most
of what's idiomatic F# is either already aligned with V2's posture
or consciously deferred for principled reasons. This section names
each major feature, where it sits, and the trigger that would
re-open the question. The general meta-rule:

  **V2 Core is purity-first; anything that introduces effect, time,
  concurrency, or runtime metaprogramming is consciously deferred
  from Core. Adapters at the boundary may use what Core forbids,
  when the adapter's role demands it.**

### Already used (aligned and load-bearing)

| Feature | Where it appears | Why it's used |
|---|---|---|
| **Closed discriminated unions** | Every IR type (`SsKey`, `Origin`, `TighteningIntervention`, every outcome / keep-reason DU) | The type system is the contract; closed DUs make exhaustiveness compiler-checked. The closed-DU empirical-test discipline (`DECISIONS 2026-05-13`) is itself load-bearing. |
| **Smart constructors returning `Result<'a>`** | `SsKey.original`, `Name.create`, `CategoricalDistribution.create`, `NumericDistribution.create`, `NullabilityTighteningConfig.create`, etc. | Structural-commitment-via-construction-validation principle (`AXIOMS.md` operational principle). Every value carries its own truth. |
| **Records with structural equality** | All IR types | Equality is by content; T1 byte-determinism rests on structural comparison being honest. |
| **Functor + monad operators** (`>>=`, `<!>`) | `Result`, `Lineage`. `Diagnostics` and `LineageDiagnostics` use named functions (`bind`, `map`) at present. | Idiomatic F# for chained computation; reads like the algebraic spec. |
| **Pipe operator `\|>`** | Everywhere | The default composition idiom; reads top-to-bottom. |
| **`[<RequireQualifiedAccess>]`** | Modules whose case names risk collision (`NullabilityOutcome`, `UniqueIndexOutcome`, `ForeignKeyOutcome`, `Lineage`, `Diagnostics`, `LineageDiagnostics`, `Composition`, `Catalog`, `Profile`, `TopologicalOrder`, etc.) | Required when generic case names (`PolicyDisabled`, `EvidenceMissing`) recur across DUs; F# resolves ambiguity by picking one, which produces silent miscompilation. |
| **`let inline` for operators** | `>>=`, `<!>` on `Result` and `Lineage` | Removes the function-call overhead on hot-path operators; enables F# to specialize on the closure shape. |
| **List / sequence comprehensions with `yield`** | `Composition.fanOut`, `TopologicalOrderPass`, list-of-conditional-keys patterns in tests | Idiomatic for building lists with conditional inclusion; clearer than `List.collect`. |
| **FsCheck.Xunit property tests** | Permutation invariance, idempotence, structural-commitment validation | Sweeps combinatorial spaces example-based tests can't reach. |
| **Backtick-quoted test names** | Every test | Tests are prose: `` ``A24: trail is chronological under bind`` ``. |
| **Typed statement-stream Π output** (`seq<Statement>`) | `Projection.Targets.SSDT.RawTextEmitter.statements`; `Render.toText` and `Deploy.executeStream` are realizations. | A35 cash-out (chapter-3.1). Π's canonical form is a typed deterministic stream; realizations are sibling consumers. |
| **`AsyncStream<'a> = unit -> Task<'a option>`** | `Projection.Adapters.Sql.AsyncStream` (pull-based streaming primitive); `ReadSide.readRowsStream`. | Async-side streaming (Core stays sync). Combinators: `map`, `mapAsync`, `iter`, `fold`, `bufferUpTo`, `probe`, `batchesOf`. Bench observability via `AsyncStream.probe`. |
| **`Bench.streamProbe` / `AsyncStream.probe`** | `Render.toText`, `Deploy.executeStream`, `RawTextEmitter.emit`, `ReadSide.readRowsStream`. | First-class stream observability. Records `<label>` (total ms) and `<label>.elements` (count) on enumeration completion. |
| **`Array.Parallel.map` for CPU-bound parallelism** | `PhysicalSchema.toPhysicalRows` (per-row SHA256). | Independent per-row work; deterministic output ordering preserved (Set-membership downstream). |
| **`SHA256.HashData` (allocation-free)** | `PhysicalSchema.hashStaticRowBytes`; `RowDigester.hashRowBytes`. | Replaces `SHA256.Create() + ComputeHash` to drop instance allocations on the per-row hashing hot path. |
| **`SqlBulkCopy` realization** (`Bulk.copyRows` + `Deploy.executeStream`) | `Projection.Pipeline.Bulk` + `Deploy.executeStream` folds consecutive `InsertRow` runs. | A36 realization. Bulk-vs-incremental is realization-layer policy; same algebra. |

### Aligned but underused (candidates whose trigger has not fired)

| Feature | Where it could fit | Trigger to adopt |
|---|---|---|
| **Function composition `>>` / `<<`** | Helpers like `decisionsOf` (currently `LineageDiagnostics.payload >> ...` pattern available); some `let f x = g x \|> h \|> i` chains could be `let f = g >> h >> i`. | When a private helper is plumbing-only (no parameter name carries documentation value). Don't rewrite existing `\|>` chains on principle; adopt where point-free reads as well or better than parameter-named. |
| **Computation expressions / DSLs** (`lineage { ... }`, `diagnostics { ... }`, `lineageDiagnostics { ... }`) | The three writers are monads; they could expose builder syntax. Today they expose `bind` / `map` / `tell` directly. | When consumer chains grow long enough that the operator-style noise outweighs the explicit operations. Today the longest chain is `\|> Lineage.bind ... \|> Lineage.bind ...` at three steps; that's bearable. Adoption costs one `Builder` type per writer; benefit is idiomatic F# at consumer sites. Surface when consumer feedback shows the chains are noisy. |
| **Active patterns** (`(\|Foo\|_\|)`) | Multi-step matches like `opportunityEntry` (match on `Outcome`, then nested match on `KeepReason`); same shape repeated in future passes that emit per-decision diagnostics. | When the same nested-match pattern appears in three or more places (the codebase's two-consumer threshold for primitives, plus one for a recognizable DSL). Would absorb the inner DU traversal into a named pattern: `(\|EnforceUnique\|DoNotEnforce\|)`. Don't pre-extract; surface when the pattern recurs. |
| **Units of measure** (`[<Measure>] type ms`, `[<Measure>] type pct`) | `NumericDistribution`'s percentile fields are `decimal`; nothing prevents passing a count where a percentile is expected. Could be `decimal<pct>`, `int64<rows>`, etc. | When a numeric-mix-up bug surfaces in real fixture data, OR when a strategy starts mixing percentile and count values in the same expression and the type system would help. Today's smart constructors enforce monotonicity; units of measure would add a complementary axis (dimensionality). |
| **Pattern-matching on records with shape literals** (`{ Foo = Bar }`) | Test fixtures and pattern-matching consumers. Today consumers usually destructure via `record.Field`. | When destructuring the same set of fields recurs across consumers; record-shape patterns make the consumer's intent visible. |
| **`[<NoComparison>]` / `[<NoEquality>]`** | Types where structural equality is misleading (none today; every IR type's structural equality is correct). | When a type carries cached state or order-sensitive payload that should not participate in equality. Surface when an IR refinement breaks the invariant "structural equality = semantic equality." |

### Consciously deferred (re-open triggers explicit)

| Feature | Why deferred | Trigger to re-open |
|---|---|---|
| **Reflection** (`typeof<>`, `GetType()`, attribute scanning) | The strategy registry mechanism (deferred at session 8) is reflection's natural home — find every type implementing `IStrategy` at startup. V2's strategy-layer codification dispatches via `FanOutConfig` directly; no name-keyed lookup is needed. | When a real consumer demands name-keyed strategy dispatch (e.g., a CLI surface that takes a strategy id from operator input). Pairs with the "Strategy registry mechanism" entry in the Active deferrals index. |
| **Object expressions** (`{ new IInterface with ... }`) | The codebase has very few interface boundaries. Polymorphism is via DU pattern matching, not interface dispatch. | When V2 grows interface-based polymorphism (e.g., `IDiagnosticSink` for streaming consumers in adapters; `ICatalogReader` for multiple sources). Object expressions are the right tool; they should land when the abstraction lands. |
| **Type providers** (`JsonProvider`, etc.) | Could provide compile-time access to the `osm_model.json` schema for the OSSYS adapter. Hand-written DTOs are simpler at first; the type-provider story has tooling fragility (CI integration; F# tooling versions). | When the OSSYS adapter ships and JSON-shape evolution becomes a maintenance burden. The OSSYS ADMIRE stub (session 14 commit 8) starts with hand-written DTOs; promotion to a type provider is a later optimization, not a session-15 default. |
| **DU member methods** (DUs carrying their own operation methods) | V2 convention is "types are data; modules carry operations." Coupling them is rejected — modules can be `[<RequireQualifiedAccess>]`'d, replaced, augmented; member methods can't. | Never on principle. The conscious omission is a stylistic load-bearing commitment. |
| **Anonymous records** (`{\| Foo = 1; Bar = 2 \|}`) | Throwaway intermediate values are rare in V2; named records make the intent visible. | When a test / boundary needs to construct a typed value that's truly one-off and doesn't merit its own type definition. Selective adoption; don't introduce as a pattern. |
| **`[<Struct>]` records / DUs** | Memory layout is not a bottleneck; immutability + GC is fine for the IR's scale. | When profiling shows allocation pressure on a hot pass. Premature `[<Struct>]` adoption can slow code by introducing copies; defer until evidence forces it. |

### Out of scope for Core (available in adapters when their role demands)

V2 Core's pure-core / no-I/O / no-time / no-mutation discipline
forbids these from `Projection.Core` regardless of how
idiomatic they are in F#. They may appear in adapters at the
boundary or in downstream consumer surfaces (CLI, streaming
diagnostic consumers, future host shells) when the adapter's role
demands it.

| Feature | Why out of scope for Core | Where it would land |
|---|---|---|
| **`Async<'a>` / `Task<'a>`** | Core is synchronous by design. T1 byte-determinism requires deterministic execution; async introduces scheduler nondeterminism. Strategies are synchronous (DECISIONS 2026-05-13 — Pass return-type codification names this as a stability-mark caveat). | Adapters that hit DB / file system. `Projection.Adapters.Osm.CatalogReader.parse` shipped with the `Task<Result<Catalog>>` shape (session 18; first substantive OSSYS adapter slice); the synchronous core consumes the result, not the Task. |
| **`MailboxProcessor` / actor modeling** | Core has no concurrent state and no message-passing. Mutable state inside Core is strictly function-local for performance-sensitive algorithms. | Adapters that need concurrent state — connection pooling for the OSSYS catalog adapter; streaming Diagnostics consumers in a future host shell that fans entries out to multiple sinks. Never in `Projection.Core`. |
| **FRP / `IObservable<'a>` / Reactive Extensions** | Core has no event streams. Lineage and Diagnostics are writers, not observables — entries accumulate in the value-carrier, they don't propagate by subscription. | A future Diagnostics consumer that streams to operator dashboards lives outside Core (downstream of the writer). The writer's contract is "produce entries"; the consumer's contract is "react to entries." Different responsibilities, different surfaces. |
| **`System.Reflection` for attribute scanning** | The closed-DU + typed-seam codification means dispatch is type-checked at compile time, not discovered at runtime. A reflection-based registry would replace compile-time guarantees with runtime ones. | If a future host shell needs plugin discovery (load strategy DLLs from a directory at startup), reflection lives in the host. The Core's strategy modules continue to be statically linked. |

### How to read this section

This taxonomy is descriptive of session-14's state, not prescriptive
of session-15. Each "underused" candidate has a trigger that
should be respected — don't adopt computation expressions because
they're cool; adopt them when consumer chains have grown long
enough that the operator-style chains are unreadable. Each
"consciously deferred" entry has a re-open trigger; if the
trigger fires, the deferral converts to a DECISIONS entry that
either adopts the feature or re-defers with explicit rationale
(same protocol as the Active deferrals index).

The meta-rule above (purity-first; adapters at the boundary may
use what Core forbids) is the gravitational sort: when in doubt
about a feature, ask whether it introduces effect, time,
concurrency, or runtime metaprogramming. If yes, it lives in an
adapter, not in Core. If no, the question becomes "does the
feature pay its weight at the call sites I have today?" — the
two-consumer threshold and the smell-test apply.

## What this file is not

- It is not a substitute for the canonical docs.
- It is not where new disciplines land. Substantive entries continue
  to land in `DECISIONS.md`; this file's "Operating disciplines"
  table updates to point at the new entry.
- It is not where load-bearing commitments are debated. The list
  above mirrors `HANDOFF.md`'s "What's load-bearing" section; if a
  commitment is removed there, this file updates to match.

## Maintenance

This file's currency is checked at every chapter close per the
**chapter-close ritual** (see Operating disciplines table).
Specifically: the Operating disciplines table must point at
current DECISIONS entries; the F# feature surface must reflect
what the codebase uses; the programming-style center target must
describe patterns visible in the code; the load-bearing
commitments must mirror `HANDOFF.md`. If any has drifted, the fix
lands during the close — not in the next chapter.

CLAUDE.md is at higher drift risk than the other canonical
surfaces because it indexes them. Session-15 codification of the
chapter-close ritual exists to make that risk structural rather
than aspirational.

## Closing

The codebase has earned its current shape because the disciplines
above were operated. The disciplines are not constraints; they are
the load-bearing structure that lets each chapter ahead support
more weight than the one behind. Hold the spine.
