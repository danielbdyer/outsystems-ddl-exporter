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

1. **`HANDOFF.md`** — bridge letter from the most-recent-closed
   chapter. Short on purpose. Names what is load-bearing and what
   is deferred. Older chapters' handoff letters preserved at
   `HANDOFF_CHAPTER_<N>.md` (currently `HANDOFF_CHAPTER_1.md`).
2. **`CHAPTER_2_CLOSE.md`** — chapter-2 close synthesis (sessions
   13–25). Read for the OSSYS adapter chapter's accumulated
   state (25 translation rules), the three-class typology, the
   meta-codifications (chapter-mid-audit; trace-before-fixture;
   V1-envelope-walk), and the chapter-3 forward signals.
   **Companion files at the projection root:**
   `CHAPTER_2_AUDIT_3_OSSYS_COMPLETENESS.md` (subagent #3's
   full audit report); `CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md`
   (subagent #4's chapter-open input); and
   `CHAPTER_3_PRESCOPE_SNAPSHOT_ROWSETS.md` (subagent #5's
   chapter-open input). The chapter-3 agent reads the pre-scope
   documents at chapter-open.
3. **`CHAPTER_1_CLOSE.md`** — chapter-1 close synthesis (sessions
   1–12). Read for historical context. Some priorities listed
   there have been resolved by chapter 2 (Diagnostics writer
   cashed out; OSSYS adapter shipped); the disciplines and
   load-bearing commitments persist.
4. **`AXIOMS.md`** — the formal system. A1–A34 / T1–T11 with
   amended originals. A18's amendment at the bottom is the
   load-bearing form for sibling Π's; the original A18 carries a
   forwarding pointer. A1's bound on identity-survives-rename
   through the JSON path is documented at the bottom (added
   session 23).
5. **`DECISIONS.md`** — append-only resolved-questions log. Read the
   most recent ten entries first; older entries remain in force
   unless explicitly superseded. Two indexes at the top:
   *Active deferrals* (catches silent-trigger fires across chapters)
   and *Operating disciplines* (cross-cutting practices, pointing
   at the substantive entries).
6. **`ADMIRE.md`** — V1↔V2 bridge. One entry per V1 component
   admired and placed in V2. Three modes: V1-migration / V2-growth
   / hybrid (`DECISIONS 2026-05-13` — admire spectrum). Multi-
   session chapters use `extracting (in flight, N slices)` while
   in flight (session 23 amendment).
7. **`README.md`** — surface-level orientation; updated at chapter
   closes. Not the source of truth for any specific question.
8. **The code.** `Projection.sln`. Strategies in
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
  every catalog kind by SsKey root.

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
