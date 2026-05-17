# Handoff letter — Chapter A.4.7' (prime) SHIPPED (Compose.run registry-traversal; registry load-bearing for execution; A41 amended)

To the next-chapter agent. Read this before anything else in the V2 sidecar. It is short on purpose.

The chapter-1 and chapter-2 handoff letters are preserved at `HANDOFF_CHAPTER_1.md` and `HANDOFF_CHAPTER_2.md` adjacent to this file. Read them after this one if you want the prior architects' framings.

## 2026-05-17 (chapter 4.6 close — slices α + β + γ) — Forward-signal cleanup bundle (HasDbConstraint + IsPlatformAuto + filter-parse Diagnostic)

**Branch / baseline.** Continues on `claude/review-chapter-close-Rqo0x`. **Test baseline at chapter close: 1348 / 1348 non-canary passing** (1330 prior + 18 net new across the chapter — 10 slice α + 0 slice β + 9 slice γ minus 1 chapter-4.4 always-false test retired). 0 build warnings under `TreatWarningsAsErrors=true`; lint count unchanged.

**Chapter 4.6 closes.** Read `CHAPTER_4_6_CLOSE.md` for the chapter-close synthesis. Read `CHAPTER_4_6_OPEN.md` for the strategic frame.

**What shipped (3 substantive commits + close).** All four chapter-4.4 always-false PredicateName variants now lift to real IR evaluation (slice α retires the last 2: HasLogicalForeignKeyWithoutDbConstraint + HasLogicalForeignKeyWithDbConstraint). One of four A.0' deferred concepts retires (slice β: IsPlatformAuto). Chapter 4.5 silent-skip Q3 deferral closes (slice γ: Diagnostics-aware filter-parse helper).

- **Slice α (`75687e6`):** `Reference.HasDbConstraint : bool` IR + adapter pickup (JSON path captures `reference_hasDbConstraint` int-flag; rowset path propagates from `#FkReality` HasFK column; SymmetricClosure inherits; ReadSide defaults true). Both predicate variants lift to real evaluation. 24 Reference literal sites migrated via Python mechanical-edit pass. 10 tests.
- **Slice β (`be44e74`):** `Index.IsPlatformAuto : bool` IR + adapter pickup. IR-only carriage; emitter consumption (operator-toggle) deferred. 9 Index literal sites migrated.
- **Slice γ (`f2b2640`):** `ScriptDomBuild.tryParseFilterWithDiagnostics` public helper. Returns `Diagnostics<BooleanExpression option>` with `Source=emitter:ssdt`, `Code=emit.ssdt.index.filterParseFailure`, `Severity=Warning`, Metadata carrying raw filter + parser error count. 9 tests.

**What's load-bearing going forward.**

- **All 16 V1-aligned PredicateName variants evaluate against real V2 IR** — the chapter-4.4 always-false-pending-IR category is empty. Future predicate additions follow closed-DU widening + adapter pickup + emit-time evaluation pattern.
- **`reference_hasDbConstraint` adapter primitive** — `getIntFlag` with COALESCE-to-false default. Reusable for future similar V1 int-flag fields.
- **`tryParseFilterWithDiagnostics` helper** — the Diagnostics-aware parse primitive. Future emit-time parse consumers (CHECK constraint, partial-index rewriting, expression validation in DACPAC adapter) consume this surface.

**Forward signals retained (after this chapter):**

1. **`IndexColumnDirection`** (ASC/DESC per column) — record-modification rather than additive. Trigger: emission demands per-column sort direction.
2. **`OriginalName` + `ExternalDatabaseType`** A.0' deferred concepts — untriggered.
3. **On-disk rich Index metadata** (FillFactor / IsPadded / partition / data compression) — V1 carries; V2 emission doesn't need for V2-driver correctness.
4. **`isPlatformAuto` emitter consumption** (NEW) — IR carriage shipped at slice β; operator-toggle wiring waits on a real workflow demanding platform-auto-index filtering.
5. **Diagnostics-aware emitter signature** (NEW) — slice γ ships the helper; buildCreateIndex wiring waits on a downstream consumer needing filter-parse failures in the manifest or per-emit Diagnostics stream.
6. **PreRemediation field population** — V2_DRIVER §154 RemediationEmitter chapter 5+.
7. **Module.ExtendedProperties emission** — multi-level-aware emitter refactor.
8. **Sequence emission** — V1 fixture gated.

**Recommended next-chapter shortlist.** V2-driver structural surface remains operationally complete. Pending substantial work:

1. **OSSYS catalog producer carbon-copy** — Phase 8 / chapter 5+ live-SQL slice. Highest-value V1 inheritance candidate per BACKLOG.
2. **`IndexColumnDirection` chapter** — record-modification (~80+ literal-site migration). Triggered when emission needs per-column sort direction.
3. **Module.ExtendedProperties emission** — schema-level sp_addextendedproperty (no level1 args). Needs multi-level-aware emitter refactor.
4. **CREATE SEQUENCE emitter** — V1 fixture gated; build the typed CreateSequenceStatement path.
5. **Phase 8 pragmatic close** — F# Analyzers SDK / Coordinates Stage 2 / Hex port lifts / cutover-day runbook / V1 sunset plan.

---

## 2026-05-17 (chapter 4.5 close — slices α + β) — Index IR fidelity (Filter + IncludedColumns) + chapter-4.4 predicate cash-outs

**Branch / baseline.** Continues on `claude/review-chapter-close-Rqo0x`. **Test baseline at chapter close: 1330 / 1330 non-canary passing** (1313 prior + 17 new across the chapter — 9 slice α + 8 slice β); canary tests skip when Docker unwarm. 0 skipped; 0 build warnings under `TreatWarningsAsErrors=true`; lint count unchanged.

**Chapter 4.5 closes.** Read `CHAPTER_4_5_CLOSE.md` for the chapter-close synthesis. Read `CHAPTER_4_5_OPEN.md` for the strategic frame (eight-axis; three-slice plan; four resolved-at-open questions).

**What shipped (2 substantive commits + close).** Two of the four `HasFilteredIndex / HasIncludedIndexColumns / HasLogicalForeignKey×DbConstraint` always-false PredicateName variants from chapter 4.4 retire. V2's Index IR + emission now carry V1's `IndexOnDiskMetadata.FilterDefinition` + `IndexColumnModel.IsIncluded` axes.

- **Slice α (`59c19d8`):** `Index.Filter : string option` IR + `IndexDef.Filter` realization-layer field + `ScriptDomBuild.parseFilterPredicate` (TSql160Parser.ParseBooleanExpression at emit time; BooleanParenthesisExpression wrap per V1 IndexScriptBuilder convention) + `buildCreateIndex` emits WHERE clause + adapter captures V1 JSON `filterDefinition` + `HasFilteredIndex` predicate lifts to real. 9 tests in `IndexFilterTests.fs`.
- **Slice β (`b9dc072`):** `Index.IncludedColumns : SsKey list` IR + `IndexDef.IncludedColumns` realization + `CatalogReader.parseIndex` partitions V1 `columns[]` by `isIncluded` flag (pre-slice-β the adapter dropped `isIncluded=true` per the documented ADMIRE divergence; that drop retires) + `ScriptDomBuild.buildCreateIndex` emits INCLUDE columns + `HasIncludedIndexColumns` predicate lifts. 8 tests in `IndexIncludedColumnsTests.fs`. `OsmCatalogReaderDifferentialTests` IX_USER_NAME expectation updated (EmailLower now captured).
- **Slice γ (this commit):** Chapter close ritual + `CHAPTER_4_5_CLOSE.md` + HANDOFF + BACKLOG + README.

**What's load-bearing going forward.**

- **`TSql160Parser.ParseBooleanExpression` as the canonical filter-parse primitive** at the SSDT layer. Future SQL-expression-parsing consumers (CHECK constraint emission via DACPAC adapter; partial-index rewriting; etc.) inherit the primitive.
- **Adapter-side partition-by-flag pattern** (`isIncluded` flag → key vs included columns). Future per-column-axis IR fields scale the same way.
- **2 of 4 always-false predicates retired**; PredicateCoverage manifest section gains accurate per-table flags for filtered + INCLUDE-bearing indexes.

**Forward signals retained (5):**

1. **`HasLogicalForeignKey×DbConstraint` predicate pair** — V2's `Reference` doesn't carry logical-vs-physical distinction. Trigger: future chapter flows the Tightening-pass `ForeignKeyOutcome` decision into Reference.
2. **`IndexColumnDirection`** (ASC/DESC per column) — record-modification (restructure `Columns : SsKey list` → `Columns : IndexColumn list`) rather than additive. Trigger: emission demands per-column sort direction.
3. **`Index.IsPlatformAuto`** — adapter-derivable; no consumer demand.
4. **On-disk rich metadata** (FillFactor / IsPadded / partition columns / data compression) — V1 carries; V2 emission doesn't need for V2-driver correctness.
5. **Filter-parse-failure Diagnostic emission** — currently silent-skip; trigger: real fixture surfaces a parse failure.

**Recommended next-chapter shortlist.** V2-driver structural surface remains operationally complete (Phases 1–7 + 5.5 + 5.6/4.5 closed). Pending work:

1. **`HasLogicalForeignKey×DbConstraint` chapter** — retires the last 2 always-false PredicateName variants. Requires Tightening-decision-into-Reference flow (record-extension on Reference; pass-pipeline integration). Estimated 1-2 sessions.
2. **`IndexColumnDirection` chapter** — record-modification (more invasive than chapter 4.5's record-extensions); restructures `Index.Columns : SsKey list` → `Index.Columns : IndexColumn list`. Estimated 1-2 sessions including ~80+ literal-site migrations.
3. **Module.ExtendedProperties emission** — gated on V1 confirmation.
4. **Sequence emission** — gated on V1 fixture.
5. **OSSYS catalog producer carbon-copy** — Phase 8 / chapter 5+ live-SQL slice.
6. **Phase 8 pragmatic close** — F# Analyzers SDK / Coordinates Stage 2 / Hex port lifts / cutover-day runbook / V1 sunset plan.

---

## 2026-05-17 (chapter 4.4 close — slices α + β + γ + δ) — Manifest diagnostic fields retire three of four chapter-4.4-fills deferrals

**Branch / baseline.** Continues on `claude/review-chapter-close-Rqo0x`. **Test baseline at chapter close: 1313 / 1313 non-canary passing** (1262 prior + 51 new across the chapter — 18 slice α + 14 slice β + 8 slice γ + 11 slice δ); canary tests skip when Docker unwarm. 0 skipped; 0 build warnings under `TreatWarningsAsErrors=true`; lint count 13 — unchanged from chapter A.4.7' baseline.

**Chapter 4.4 closes.** Read `CHAPTER_4_4_CLOSE.md` for the chapter-close synthesis (per-slice ledger + 8-item close ritual + V1-input-envelope walk + deferral re-affirmations). Read `CHAPTER_4_4_OPEN.md` for the strategic frame (eight axes; four-slice plan; four resolved-at-open questions).

**What shipped (4 substantive commits + close).** Three of the four `chapter 4.4 fills` deferrals codified in `ManifestEmitter.fs:32-33,176-183` are retired. `Coverage` / `PredicateCoverage` / `Unsupported` now emit typed evidence; `PreRemediation` stays empty per `V2_DRIVER.md` §154 (RemediationEmitter deferred to chapter 5+).

- **Slice α (`a6505e5`):** `CoverageBreakdown` + `CoverageSummary` + `Coverage.compute`. Mirrors V1's `Osm.Emission.CoverageBreakdown` shape + percentage-rounding contract (Math.Round AwayFromZero; total=0→100; emitted=0→0). T11 keyset coverage holds structurally — V2 emits everything so `Emitted = Total` per axis. 18 tests.
- **Slice β (`09e71ce`):** `PredicateName` closed DU (16 variants matching V1's SsdtPredicateNames verbatim) + `PredicateCoverage`. 12 variants have V2 IR evidence; 4 always-false pending V2 IR refinement (HasFilteredIndex / HasIncludedIndexColumns / HasLogicalForeignKey×DbConstraint pair). PredicateCounts emits as sorted-by-name array of `{name, count}` objects per chapter open Q2 (documented divergence from V1's JSON-dict shape). 14 tests.
- **Slice γ (`b08c7dc`):** `Unsupported.compute` renders `ToleratedDivergence.allKnown` as sorted strings. Closed-DU empirical-test discipline ensures content audit surfaces variant additions / retirements. 7 tests.
- **Slice δ (this commit):** V1 differential test (`ManifestV1DifferentialTests.fs`) cross-checks V2's emit shape against V1's reference types — PredicateName names verbatim; CoverageBreakdown rounding contract; SsdtCoverageSummary three-axis shape; SsdtPredicateCoverage two-section shape; PredicateCoverageEntry fields; Unsupported as IReadOnlyList; PreRemediation empty per §154; documented V2-only divergences (registry.digest; predicateCounts shape). 11 tests. **Chapter-close ritual discharged.**

**What's load-bearing.** After this chapter:

- **`CoverageBreakdown` smart constructor** is the typed primitive for V1-shape coverage emission; V1-rounding-contract preserved.
- **`PredicateName` closed DU** is the typed registry of V1's 16 named manifest predicates; when V2 IR grows the 4 always-false predicates' evidence, the lift surfaces via the closed-DU empirical-test discipline.
- **`ToleratedDivergence` enumeration is the Unsupported source** — when a Tolerance variant retires (chapter widens IR + emitter consumption), the manifest's Unsupported list shortens; `ManifestUnsupportedTests.fs` "current-variant content audit" surfaces the retirement.
- **Manifest's V1-compatible JSON schema** — every operator-consultable axis now carries typed evidence at the V1-shape surface (modulo documented divergences).

**Forward signals retained (4):**

1. **`PreRemediation` field population** — V2_DRIVER §154 deferred-to-chapter-5+ RemediationEmitter trigger. Empty-array structurally correct until RemediationEmitter ships.
2. **`PredicateName` 4 always-false variants** — IR refinement triggers: Index.Filter for HasFilteredIndex; key/included column split for HasIncludedIndexColumns; logical-vs-physical Reference distinction for the WithDbConstraint pair.
3. **Unsupported per-divergence rationale** — consumer-pressure trigger to widen string list → typed record list.
4. **V1↔V2 PredicateCounts JSON-shape divergence** — Tolerance candidate (or shape-flip to JSON dict with key-sorted serialization) if byte-equality with V1 demanded.

**Recommended next-chapter shortlist (superseding the chapter A.4.7' close entry's stale shortlist).** The V2-driver critical-path Phases 1–7 are all closed; chapter 4.4 retires the largest piece of named pending V2_DRIVER work. Remaining work:

1. **Deferred-with-trigger items** (consumer-pressure-driven; ~1-2 sessions each as triggers fire):
   - Module.ExtendedProperties emission — gated on V1 confirmation of module → schema convention.
   - Sequence emission — gated on V1 fixture surfacing sequences.
   - Chapter 4.3 slices δ (CLI wire-up) + ε (V1 differential).
   - Chapter 4.2 OSSYS adapter User-kind identification surface + CSV adapter for ManualOverride.
   - Chapter 3.x slices ε + ζ + per-Catalog parameterization.
   - V1↔V2 byte-equality for sp_addextendedproperty / predicateCounts shape — if downstream consumer demands.
2. **PhysicalSchema extended-property reflection** — extends canary's diff surface; orthogonal to emitter axis.
3. **Phase 8 pragmatic close** — F# Analyzers SDK / Coordinates Stage 2 typed VOs / Hex port lifts / cutover-day operator runbook / V1 sunset planning. Consumer-pressure-driven; opens at cutover-15 to cutover+30 window.

The 4 always-false `PredicateName` variants are forward-signal opportunities — when V2 IR grows the corresponding evidence (likely via a future DACPAC adapter slice or rowset extension), the manifest's PredicateCoverage tightens automatically and the close-ritual ToleratedDivergence-content-audit catches the retirement.

---

## 2026-05-17 (post-A.4.7' doc-refresh hygiene) — V2_DRIVER + BACKLOG refreshed; recommended-next-chapter shortlist below supersedes the chapter A.4.7' close entry

**Branch / baseline.** Continues on `claude/review-chapter-close-Rqo0x`. **Test baseline unchanged at 1262 / 1262 non-canary passing**; canary tests skip when Docker unwarm; 0 build warnings under `TreatWarningsAsErrors=true`; lint count 13. Operator-reality perf baseline re-recorded to absorb chapter A.4.7''s `compose.runChain` Bench scope (5 warm captures green; 202 labels × 5 runs; see `DECISIONS 2026-05-17 (post-chapter-A.4.7' hygiene) — Perf baseline re-recorded`).

**What changed in this hygiene pass.** The "Recommended next chapter" list in the chapter A.4.7' close entry below listed chapter 4.1.B (closed 2026-05-11) and chapter 4.2 (closed 2026-05-15) as if openable — the author was treating the V2_DRIVER per-axis stakes table as canonical without noticing V2_DRIVER and BACKLOG had drifted. The drift was caught in this hygiene pass and patched:

- `V2_DRIVER.md` Phase 3 / 4 / 5 / 6 / 7 rows updated to **shipped** with chapter-close-doc references and a current-state-as-of-2026-05-17 paragraph naming the operationally-complete structural surface.
- `BACKLOG.md` Phase 3 / 4 / 5 / 6 status fields refreshed; per-phase slice tables shipped-out; deferred-with-trigger lists codified per chapter close docs; sequencing graph at §VII updated to show the current state.

**Recommended next-chapter shortlist (superseding the chapter A.4.7' close entry below).** The V2-driver critical-path Phases (1–5 + 7) are all closed. The actually-pending named work is:

1. **Chapter 4.4 — Operational Diagnostics manifest fields.** The largest piece of named pending V2_DRIVER work. `ManifestEmitter.fs:32-33,181` docstring + emission paths confirm `Coverage` / `PredicateCoverage` / `PreRemediation` / `Unsupported` currently emit as `null` / defaults. Chapter fills them under per-axis property test coverage. Likely 4-6 slices.
2. **Module.ExtendedProperties emission** — deferred-with-trigger from chapter 4.1.A.8 (`DECISIONS 2026-05-17 — sp_addextendedproperty emission, Decision 4`). Gated on V1-side confirmation of module → schema convention. ~1 session if trigger fires.
3. **Sequence emission** — `CREATE SEQUENCE` emitter for `Catalog.Sequences` IR shape (chapter A.0' slice δ shipped the IR; no emitter). Deferred until V1 fixture surfaces sequences. ~1-2 sessions if trigger fires.
4. **Chapter 4.3 slice δ (CLI wire-up) + slice ε (V1 differential)** — deferred-with-trigger from chapter 4.3 close. Slice δ triggers on operator demand for one-command diagnostics emission; slice ε triggers on chapter that needs cross-version diagnostic-fidelity evidence.
5. **Chapter 4.2 OSSYS adapter User-kind identification surface + CSV adapter for ManualOverride** — deferred-with-trigger from chapter 4.2 close. Slice triggers on real platform-user-kind data flow.
6. **Chapter 3.x slices ε (modality marks → comments/extended properties) + ζ (byte-determinism via canonicalization)** — deferred-with-trigger from chapter 3.x close.
7. **Phase 8 pragmatic close** — F# Analyzers SDK; Coordinates Stage 2 typed VOs; Hex port lifts; cutover-day operator runbook; V1 sunset planning. Consumer-pressure-driven; opens at cutover-15 to cutover+30 window per fallback-ladder gates.

**The chapter A.4.7' close letter below remains accurate as a historical record of what shipped that chapter** — its 8 forward signals (item-numbered list) are all carried forward unchanged. Only the "Recommended next chapter" list at the bottom of that entry has been superseded by the shortlist above.

---

## 2026-05-17 (chapter A.4.7' close — slices α + β + γ + δ + ε + ζ + η + θ) — Compose.run registry-traversal; A41 amended (execution totality); 5/5 bidirectional property tests; `let run` private across all 12 passes

**Branch / baseline.** Continues on `claude/review-chapter-close-VnRe8`. **Test baseline at chapter close: 1262 / 1262 non-canary passing** (1226 prior + 36 new across the chapter — 6 ComposeChainAdapterTests + 5 RegisteredTransformsTests + 5 PassChainAdapterComposeTests + 3 SkeletonPurityTests + 5 RegistryDigestRoundTripTests + 12 sundry test additions across migrations); canary tests skip when Docker unwarm. 0 skipped; 0 build warnings under `TreatWarningsAsErrors=true`; lint count 13 — unchanged from main / chapter A.4.7 close baseline; zero new introduced across chapter A.4.7'.

**Chapter A.4.7' closes.** Read `CHAPTER_A_4_7_PRIME_CLOSE.md` for the chapter-close synthesis (8-slice ledger, A41 amendment cash-out, 4 meta-codifications, 8 forward signals, pillar-9 audit, verifiability-triangle audit, chapter-close ritual checklist). Read `CHAPTER_A_4_7_PRIME_OPEN.md` for the strategic frame (9 axes; 8-slice plan; resolved-at-chapter-open naming + slicing decisions).

**What shipped (8 substantive commits + close).** Chapter A.4.7' cashes chapter A.4.7's forward signal #3 ("Compose.run registry-traversal refactor") in full. The registry is now the load-bearing execution surface for V2's emit path — bypassing it is structurally impossible because the hand-coded pass sequence has retired. A41 amended (execution totality) cashes the chapter A.4.7 metadata-only commitment into metadata + execution.

- **Slice α (`d376ee0`):** ComposeState aggregate + PassChainAdapter lift constructors. 6 witness tests.
- **Slice β (`4f83325`):** `RegisteredTransforms.all` (17) + `allChainSteps` (12) populated at end of Core compile order. Naming via pillar 8: concept-shaped plural. 5 witness tests.
- **Slice γ (`b5a515a`):** `PassChainAdapter.compose` traversal kernel; A24 trail ordering. 5 tests.
- **Slice δ (`5b90fdf`):** `Compose.project` consumes registry; hand-coded sequence retires; registry load-bearing. Empirical-bet validated: zero byte-shifts.
- **Slice ε (`908e50d`):** `RegisteredTransforms.skeletonChainSteps` (4 entries) + `Compose.runSkeleton` + skeleton-purity true-execution property test. 3 tests.
- **Slice ζ (`22f26b8`):** `TransformRegistry.digest` + ManifestEmitter `registry.digest` field + `osm emit --skeleton-only` CLI + 5th bidirectional property test (registry-digest round-trip). 5 tests. **5/5 bidirectional property tests met — chapter exit gate.**
- **Slice η.1 partial (`c58fb11`):** VisibilityMask `let run` privatized as trial; Stop-hook-recovery checkpoint.
- **Slice η complete (`11f03e8`):** All 11 remaining passes privatized; ~308 call sites migrated via per-test-file shape-restoring shims. 39 files changed. Subagent-driven bulk + parent ValidationError boundary fix.
- **Slice θ (this commit):** A41 amendment body filled in `AXIOMS.md`; `CHAPTER_A_4_7_PRIME_CLOSE.md` ships; this HANDOFF.md updated. 4 meta-codifications captured (compile-order-resolved-at-assembly-point; empirical-bet method; per-test-file shape-restoring shim; Stop-hook respect via partial-commit-with-explicit-deferral).

**What's load-bearing.** After this chapter:

- **The registry is V2's load-bearing execution surface.** `Compose.project` consumes `RegisteredTransforms.allChainSteps` as its execution loop; bypassing requires bypassing the composer itself.
- **`let run` is private in all 12 pass modules.** Public callable is `<Pass>.registered.Run` only. Parallel-exposure transition affordance retired.
- **Manifest carries `registry.digest`** for downstream audit consumers.
- **CLI exposes `osm emit --skeleton-only`** as the operator-facing skeleton baseline.
- **5/5 bidirectional property tests** green at runtime.

**Forward signals retained (8):**

1. `applied-transforms : (SsKey × OverlayAxis option) list` per-artifact manifest field — deferred from slice ζ per consumer-pressure.
2. Per-OverlayAxis CLI flags — deferred from chapter A.4.7 open Q8.
3. `Policy.fs` ↔ `OverlayAxis` structural collapse — preserved deferral.
4. Emitter-as-chain-step — trigger: fourth emitter OR runtime emitter classification.
5. Adapter-as-chain-step — trigger: V2 composes multiple adapters.
6. `Compose.run` async-streaming form — trigger: chain-level streaming perf concern at 300-table scale.
7. `ComposeState.Profile` field — trigger: consumer needs runtime profile inspection.
8. `runChain` placement re-evaluation — trigger: third consumer demands per-stage variant.

**Recommended next chapter.** Operator's call between:

1. **Chapter 4.1.B** — CDC-silence-on-idempotent-redeploy property test (V2_DRIVER's highest-leverage single deliverable). Slice α: ScriptDomBuild.buildMergeStatement (Tier-3 hard-requirement deferral from chapter 4.1.A close).
2. **Chapter 4.2** — User FK reflow consumer side (chapter A.4.7 forward signal). Open at 2026-05-16 close.
3. **Chapter 4.4** — Operational Diagnostics (Coverage / PredicateCoverage / PreRemediation / Unsupported manifest fields).
4. **Module.ExtendedProperties emission** — deferred-with-trigger from chapter 4.1.A.8; gated on V1 confirmation.

## 2026-05-17 (chapter 4.1.A slice 8 reopen + ship) — sp_addextendedproperty emission; CommentMetadataUnreflected Tolerance retired

**Branch / baseline.** Continues on `claude/review-chapter-close-VnRe8`. **Test baseline at slice close: 1226 / 1226 non-canary passing** (1219 prior + 7 new `SsdtExtendedPropertyEmissionTests`); canary tests skip when Docker unwarm. 0 build warnings under `TreatWarningsAsErrors=true`; lint count 13 — unchanged from main / chapter A.0' / A.4.7 baseline.

**What shipped (`f140595`).** Chapter 4.1.A's slice 8 — deferred-with-trigger since chapter A.0' close — fires. The IR carriage from chapter A.0' (slices α + ζ) now has its emitter consumer.

- **Typed Statement variant.** `Statement.SetExtendedProperty of tableId * target * propertyName * propertyValue` with `ExtendedPropertyTarget = TableExtendedProperty | ColumnExtendedProperty of columnName | IndexExtendedProperty of indexName` (concept-shaped per pillar 8; matches SQL Server's `@level2type` taxonomy).
- **ScriptDom typed-AST emission.** `ScriptDomBuild.buildSetExtendedProperty` maps to `ExecuteStatement` wrapping `sys.sp_addextendedproperty` with national-string parameters. Per text-builder-as-first-instinct discipline (Tier-3 hard-requirement) — typed AST is the right move; no `StringBuilder()` shortcut at this site.
- **Per-kind emission order** via `SsdtDdlEmitter.extendedPropertyStatements`: table description → table ExtendedProperties → per-column descriptions → per-column ExtendedProperties → per-index ExtendedProperties. Hooked into `kindToSsdtFile` after `indexStatements`.
- **`Tolerance.CommentMetadataUnreflected` retired** per closed-DU empirical-test discipline. Variant removed; `allKnown` set goes from 5 to 4 elements; runtime test + FsCheck arbitrary updated.

**Textual deviation from V1.** ScriptDom canonicalizes `EXEC sys.sp_addextendedproperty` → `EXECUTE [sys].[sp_addextendedproperty]` + `@name = N'...'` (spaces around `=`). Semantically identical; canary's `PhysicalSchema` diff is text-blind. Forward signal: if a consumer demands byte-equality with V1's text form, swap to `String.Concat`-at-terminal-boundary OR ScriptDom's `ScriptCompatibilityOptions` — today no consumer demands it.

**Module.ExtendedProperties deferred-with-trigger.** SQL Server's `@level0type = N'SCHEMA'` semantics map module → schema only when modules align 1:1 with schemas. V2 doesn't yet formalize this; module-level emission awaits V1-side confirmation of emission convention. Triple deliverable (Skip stub + Tolerance + NotImplementedInV2 registry) does NOT fire — this is "defer until V1 confirmed," not "V2 chose not to bring forward."

**Forward signals retained:**

1. **Module.ExtendedProperties emission** — gated on V1 confirmation.
2. **PhysicalSchema extended-property reflection** — extends canary's diff surface; separate from emitter axis.
3. **V1↔V2 byte-equality for sp_addextendedproperty** — gated on consumer demand for line-by-line text diff with V1.
4. **Sequence emission** — chapter A.0' slice δ shipped `Catalog.Sequences` IR carriage; no `CREATE SEQUENCE` emitter exists. Likely chapter 4.x slice when V1 fixture surfaces sequences.
5. **The four deferred-out-of-A.0' V1 concepts** — `OriginalName`, `ExternalDatabaseType`, `IndexColumnDirection`, `IsPlatformAuto`. Each gated on its own consumer-pressure trigger.
6. **Slice η** (chapter A.4.7 forward signal) — `osm emit --skeleton-only` CLI + ManifestEmitter registry-digest + per-artifact `applied-transforms` + fifth bidirectional property test (manifest digest round-trip).
7. **Slice γ.2** (chapter A.4.7 forward signal) — make `let run` private in 12 pass modules + migrate ~80 call sites (~9 production + ~70 test).

**Recommended next chapter.** Operator's call between:

1. **Slice η — CLI + manifest extension.** Retires chapter A.4.7's last open forward signal. Adds operator-facing `osm emit --skeleton-only`; ManifestEmitter gains `registry.digest` + per-artifact `applied-transforms`; fifth bidirectional property test (manifest digest round-trip) ships. Estimated 1-2 sessions.
2. **Slice γ.2 — private `run` migration.** Structural hygiene: make `let run` private in all 12 pass modules; migrate ~80 call sites to `<Pass>.registered.Run` / `(<Pass>.registered config).Run`. Mechanical but voluminous (notably ~50+ test sites). Estimated 2-3 sessions.
3. **Module.ExtendedProperties / Sequence emission slices.** Forward-signal completion as V1-side evidence surfaces. Per-slice ~1-2 sessions.
4. **Compose.run registry-traversal refactor.** Heavier — designs pass-chaining adapter for heterogeneous output types. Chapter 4.x or 5.x scope.

## 2026-05-16 (chapter A.4.7 close — slices ζ + θ + ι) — Transform registry shipped; A41 cashed; L3-CC-Transform-Totality D → A

## 2026-05-16 (chapter A.4.7 close — slices ζ + θ + ι) — Transform registry shipped; A41 cashed; L3-CC-Transform-Totality D → A

**Branch / baseline.** Continues on `claude/review-chapter-close-VnRe8`. **Test baseline at chapter close: 1281 / 1281 passing** (1202 prior + 79 new across the chapter — 14 ClassificationCarryThroughTests + 25 TransformRegistryTests + 18 PassRegistrationsTests + 5 AdapterRegistrationsTests + 8 StrategyRegistrationsTests + 11 TransformRegistryCompletenessTests including 3 intentional-fail probes); 0 skipped; 0 build warnings under `TreatWarningsAsErrors=true`; lint count 13 — unchanged from main; zero new introduced across chapter A.4.7.

**Chapter A.4.7 closes.** Read `CHAPTER_A_4_7_CLOSE.md` for the chapter-close synthesis (per-slice ledger, L3 axiom promotion, four meta-codifications, six forward signals, pillar-9 audit, chapter-close ritual checklist). Read `CHAPTER_A_4_7_OPEN.md` for the strategic frame (9 axes; 9-slice plan; resolved-at-chapter-open Q9 expansion).

**What shipped (5 commits across the chapter).** Chapter A.4.7 ships V2's fourth cross-cutting structural-evidence concern — `TransformRegistry` (sibling to Lineage / Diagnostics / Bench). The data-intent / operator-intent dichotomy promoted from convention to type-witnessed bidirectional contract. 18 transformation sites classified (1 adapter + 12 passes + 5 strategies). 4 of 5 bidirectional property tests shipped + 3 intentional-fail probes. The fifth property — manifest digest round-trip — deferred-with-trigger to slice η (consumer-pressure deferred).

- **Slice α (`e060e70`):** `Projection.Core/Classification.fs` ships `OverlayAxis` (Selection / Emission / Insertion / Tightening / Ordering) + `Classification` (DataIntent / OperatorIntent of OverlayAxis). `LineageEvent.Classification` field added; writer-fidelity primitives propagate; 12 passes self-classify.
- **Slice β (`e6e94e0`):** `Projection.Core/TransformRegistry.fs` ships `StageBinding` / `Domain` / `TransformSite` / `TransformStatus` / `RegisteredTransform<'In, 'Out>` / `RegisteredTransformMetadata` + smart constructor. Fifth `OverlayAxis` variant `Ordering` ships per Q9-trigger-fires worked example (TopologicalOrderPass.SelfLoopPolicy is the named real-evidence trigger).
- **Slice γ (`bfec22f`):** 12 pass `.registered` exports across 6 categories (3 simple + 2 config factory + 1 multi-site + 4 intervention factories + 1 Result-wrapping + 1 UserFkReflowPass). Spec deviation codified: heterogeneous output types, factory pattern, parallel-exposure of `let run`. Slice γ.2 forward-signaled.
- **Slices δ + ε (`244533e`):** `CatalogReader.registeredMetadata` (1 adapter entry with 6 Sites for ~26 transformative rules) + `StrategyRegistrations` module (5 strategy entries — 4 Tightening + 1 DataIntent CycleResolution). Per-rule-as-Sites pattern; dedicated `StrategyRegistrations.fs` solves compile-order constraint.
- **Slices ζ + θ + ι (this commit):** `TransformRegistry.skeletonView` / `overlayView` / `overlayAxes` filter helpers. `TransformRegistryCompletenessTests.fs` ships 4 bidirectional property tests + 3 intentional-fail probes. AXIOMS A41 body cashed. `PRODUCT_AXIOMS.md` L3-CC-Transform-Totality D → A. `CHAPTER_A_4_7_CLOSE.md` + this HANDOFF entry.

**Chapter A.4.7 meta-codifications** (full detail in close doc):

1. **Per-rule-as-Sites for non-callable transformations** — when a structural commitment calls for N separate registry entries but the implementation has N rules embedded in one callable surface, ship N Sites within one registry entry. Worked at CatalogReader (6 Sites for ~26 rules) and TopologicalOrderPass (2 Sites for SortKahn + SelfLoopHandling).
2. **Compile-order-constraint-solved-via-dedicated-module** — extract registrations into a downstream module when embedding would create a circular dependency. Worked at `StrategyRegistrations.fs`.
3. **Factory pattern for configurable transformations** — `.registered <config>` returns `RegisteredTransform<...>`; static metadata + per-config Run closure. Worked at 8 of 12 passes.
4. **Parallel-exposure during structural-commitment transitions** — ship new canonical surface alongside the old as a transition affordance. Worked at slice γ; γ.2 trigger documented.

**A18 ↔ A41 sibling commitment preserved.** A18 amended (no Policy in emitters) + A41 (registry totality + bidirectional property tests) are now type-witnessed siblings carrying the data-intent / operator-intent dichotomy bidirectionally. The four meta-disciplines (pillar 8 / pillar 7 amendment / text-builder-as-first-instinct / pillar 9) are now fully realized as type-witnessed-bidirectional contracts after chapter A.4.7 close.

**V2 self-containment preserved.** Zero carbon-copy events across chapter A.4.7. `BACKLOG.md` V1 inheritance log remains empty.

**Six forward signals** (deferred-with-trigger; full list in close doc):

1. Slice γ.2 trigger — make `let run` private + migrate consumers from `<Pass>.run` to `<Pass>.registered.Run`.
2. Slice η scope — `osm emit --skeleton-only` CLI flag + ManifestEmitter registry-digest extension + per-artifact `applied-transforms` field + fifth property test (manifest digest round-trip).
3. `Compose.run` registry-traversal refactor — replace hand-coded orchestration with `TransformRegistry.allInStageOrder` traversal. Requires pass-chaining adapter for heterogeneous output types. Likely chapter 4.x or 5.x scalable-orchestration cutover-blocker concern.
4. Fifth `OverlayAxis` expansion trigger — apply Q9-trigger-fires discipline when future chapter surfaces real-evidence for an operator-intent axis not subsumed by the existing five.
5. `Policy.fs ↔ OverlayAxis` collapse refactor — lands when call-sites consult both vocabularies at one site.
6. Tolerance retirement signals — when first v1-harvest "don't bring forward" decision lands, triple deliverable fires (Skip stub + Tolerance + NotImplementedInV2 registry entry); slice θ's harvest-classification coverage test gains substantive content.

**Recommended next chapter.** Three forward paths from chapter A.0' close; one (LineageEvent.Classification) retired by chapter A.4.7 slice α; one (A.4.7 full registry refactor) retired by this chapter. Remaining:

1. **Chapter 4.1.A slice 8 (ExtendedProperties + Descriptions DDL emission)** — highest-leverage cutover-blocker progress at this point. IR carriage is complete (chapter A.0' slices α + ζ); SSDT emitter consumes IR fields + emits `sp_addextendedproperty` calls. Retires `CommentMetadataUnreflected` Tolerance variant. ~1-2 sessions.

Alternatives per `V2_DRIVER.md`: A.5 (Profile-JSON ingestion + completeness audit), A.6 (differential-testing soak), A.7 (user matching), chapter 3.x DacpacEmitter, chapter 4.1.B (data triumvirate continuation).

**Outstanding (operator-side; unchanged):**
- R1 — operator's "document of key evolutions" still pending. Hold UAT-users decisions until it lands.
- Q2 / Q3 / Q4 / Q7 unchanged.

## 2026-05-16 (chapter A.0' close — slice θ + slice ι) — IR-fidelity body fully landed; L3-Boundary-NoSilentDrop verified

**Branch / baseline.** Continues on `claude/retire-isactive-disposition-WD4Ez`. **Test baseline at chapter close: 1202 / 1202 passing** (1177 prior + 21 new `NoSilentDropTests` + 4 new TableId.Catalog tests in `IRFidelityLiftTests`); 0 skipped; 0 build warnings under `TreatWarningsAsErrors=true`; lint count unchanged from main (13 pre-existing on main; zero new introduced across chapter A.0').

**Chapter A.0' closes.** Read `CHAPTER_A_0_PRIME_CLOSE.md` for the chapter-close synthesis (per-slice ledger, L3 promotions, meta-codifications, forward signals, close ritual checklist). Ten L3 axioms advance D → A: L3-S4 (Triggers), L3-S5 (Sequences), L3-S6 (DEFAULT), L3-S7 (Computed), L3-S8 (CHECK), L3-S9 (Descriptions + IsActive + ExtendedProperties), L3-S10 / L3-I10 (Catalog coordinate), L3-CC4 (IR fidelity for production), L3-Boundary-NoSilentDrop, and IsExternal/Origin (Bucket-B → A upgrade).

**What shipped in slice θ + slice ι (this commit).**

- **Slice θ — `TableId.Catalog : string option` (L3-S10 / L3-I10).** `TableId` extended from `{ Schema; Table }` to `{ Catalog : string option; Schema; Table }`. `TableId.create` retains its `(schema, table)` signature and defaults `Catalog = None` (V1's `db_catalog: null` parity); `TableId.createWithCatalog catalog schema table` carries explicit cross-database coordinates with blank-catalog rejection per A39. OSSYS JSON adapter (`parseKind`) reads V1's `db_catalog` field; rowset path + ReadSide default to `None`. 9 record-literal sites in src touched mechanically; tests adjusted via the Python pass. Chapter's structural enforcement holds — cross-database FKs no longer silently degrade to implicit-current-database scope.
- **Slice ι — L3-Boundary-NoSilentDrop + IsExternal/Origin audit.** `NoSilentDropTests.fs` ships 21 tests in three sections:
  - **Per-concept structural witnesses (12 tests)** — for each V1 concept in `V2_PRODUCTION_CUTOVER.md` §3.3, a runtime assertion against the V2 IR's typed home.
  - **Kitchen-sink JSON fixture witness (1 test)** — six axes asserted across one Catalog: Descriptions + IsActive + Triggers + DEFAULT + ExtendedProperties + IsExternal=true → ExternalViaIntegrationStudio.
  - **IsExternal / Origin mapping audit (8 tests)** — JSON-path two-way placeholder + rowset-path three-way real + the `isExternal=true never → OsNative` invariant.

**Chapter A.0' meta-codifications** (full detail in close doc):

1. Mechanical-edits precedent for record-extension slices (`/tmp/fix_fields.py` + dedupe + indent-normaliser scripts).
2. `IRBuilders.fs` fixture-builder pattern (chapter A.0' XXXXL contribution; `Fixtures.fs` retrofitted as worked example).
3. Per-axis property test as completion criterion (`NoSilentDropTests.fs` demonstrates the trifecta: per-concept structural + kitchen-sink + invariant property).
4. Pillar-8 deviation discipline (slice γ: chapter open's "Catalog.Triggers" planning shorthand → implementation chose `Kind.Triggers` per domain analysis).

**A18 amended preserved.** No emitter consumes Policy. All new IR fields are `Catalog`-side DataIntent evidence; Π consumes `Catalog × Profile` per A18 amended.

**V2 self-containment preserved.** Zero carbon-copy events across chapter A.0' (10 slices total). `BACKLOG.md` V1 inheritance log remains empty.

**Tolerance retirement signals.** `CommentMetadataUnreflected` Tolerance variant is one structural step closer to retirement: IR carries Description + ExtendedProperties + TableId.Catalog at completion. Full retirement gates on emitter consumption (chapter 4.1.A slice 8 ExtendedProperties DDL emission + chapter 3.x DacpacEmitter cross-database FK qualification).

**Recommended next chapter.** Three independent forward-progress paths:

1. **`LineageEvent.Classification` field (A.4.7-prelude small slice)** — unblocked by chapter A.0' close. Adds the `Classification : Classification` field to `LineageEvent` so events self-classify before the full A.4.7 traversal refactor. Per `DECISIONS 2026-05-15 (late)`. ~1 session.
2. **Chapter 4.1.A slice 8 (ExtendedProperties + Descriptions DDL emission)** — emitter consumption of the IR fields chapter A.0' lifted. Retires the `CommentMetadataUnreflected` Tolerance variant. Per `Active deferrals` in `DECISIONS.md`. ~1-2 sessions.
3. **A.4.7 (Transform registry, full refactor)** — `RegisteredTransform<'In, 'Out>` + Compose.run traversal + bidirectional property tests. The chapter A.0' IR shape is the input. ~3 weeks. The substantive next-chapter target.

**Operator-side choice** (which to ship next):
- If the operator wants the cutover-blocker progress, **slice 4.1.A.8** is the highest-leverage emitter work (Tolerance retirement).
- If the operator wants structural commitments to compound, **A.4.7-prelude** is the smallest unblocking step.
- If the operator wants the load-bearing refactor, **A.4.7 full** is ~3 weeks of focused work.

**Outstanding (operator-side; unchanged):**
- R1 — operator's "document of key evolutions" still pending. Hold UAT-users decisions until it lands.
- Q2 / Q3 / Q4 / Q7 unchanged.

**Forward signals retained.**
- Rowset-path pickup for triggers / extended properties / defaults / column checks / db_catalog (gated on V1 rowset extension or DACPAC-adapter slice).
- IRBuilders retroactive sweep (volume refactor; reduces next-chapter field-addition blast radius from ~150 sites to ~1).
- `Module.create` parameter-pollution revisit-trigger.
- `ModalityMark.mapPayload` helper extraction-trigger (pending fourth pass-module touch).

## 2026-05-16 (XXXXL — slices γ + δ + ε + ζ + η) — IR-fidelity body shipped; chapter A.0' two slices from close

**Branch / baseline.** Continues on `claude/retire-isactive-disposition-WD4Ez` (post-slice-β). **Test baseline: 1177 / 1177 passing** (1155 prior + 22 new `IRFidelityLiftTests`); 0 skipped; 0 build warnings under `TreatWarningsAsErrors=true`; lint clean across 27 rules. Five of chapter A.0''s remaining seven slices shipped as one coherent XXXXL slice; slice θ (`TableId.Catalog`) and ι (chapter-close L3-Boundary-NoSilentDrop property test) remain pending.

**What shipped (one commit).** Six new value types in `Projection.Core.Catalog`: `Trigger`, `Sequence` + `SequenceCacheMode`, `ComputedColumnConfig`, `ColumnCheck`, `ExtendedProperty`, `TemporalConfig` + `TemporalRetention` + `TemporalRetentionUnit`. Nine new IR fields across `Attribute` / `Kind` / `Module` / `Index` / `Catalog`. One new `ModalityMark` variant (`Temporal of TemporalConfig`; closed-DU widening). One file extraction (`PrimitiveType.fs` split out so `SqlLiteral` can compile BEFORE `Catalog`; `Attribute.DefaultValue : SqlLiteral option` resolves cleanly). Two `*.create` signatures expanded (`Module.create` gained `extendedProperties`; `Catalog.create` gained `sequences`). OSSYS JSON adapter pickup wired for triggers + defaults + entity-level extended properties; rowset path + ReadSide default to empty/None.

**The DECISIONS amendment is the load-bearing artifact.** `DECISIONS 2026-05-16 (slices γ + δ + ε + ζ + η — XXXXL)` codifies the five-slice body. Pillar-9 classification: all DataIntent; reachable from `Project(catalog, Policy.empty, profile)` without operator opinion. A18 amended preserved (no emitter consumes Policy). V2 self-containment preserved (no carbon-copy event; `BACKLOG.md` V1 inheritance log stays empty).

**Refactor — `IRBuilders.fs` fixture-builder pattern (user-requested at XXXXL close).** Centralised `mkAttribute` / `mkKind` / `mkModule` / `mkIndex` / `mkCatalog` builders with minimum-evidence DataIntent defaults. `Fixtures.fs` retrofitted as the worked example. Next-chapter agents add new IR fields once in `IRBuilders.fs` instead of touching ~150 record-literal sites. The discipline is documented in `IRBuilders.fs` module-level docstring + the DECISIONS amendment's "Forward signals" section (item 4 — retroactive sweep at chapter close).

**Pillar-8 deviation from the chapter open's "Catalog.Triggers" planning shorthand.** Slice γ ships `Kind.Triggers : Trigger list` (kind-scoped, not Catalog-scoped). Triggers are owned by tables per SQL Server semantics and V1's JSON projects them at entity level. The chapter open document records the corrected scope; the design rationale is in the DECISIONS amendment.

**Closed-DU empirical-test discipline held (slice η).** `ModalityMark.Temporal` is the only DU-widening slice in the chapter. Match-site additions: 3 pass modules (`CanonicalizeIdentity`, `NamingMorphism`, `NormalizeStaticPopulations`) + `JsonEmitter.modalityString`. All three pass modules now have a `Temporal _ -> m` no-op match arm with a docstring naming the slice. No other ripple — the empirical test confirmed the discipline generalises to DU-widening identically to record-extension.

**Next-most-ready slices (chapter A.0' is two slices from close):**

- **Slice θ — `TableId.Catalog : string option`** — extends the `TableId` shape (currently `{ Schema; Table }`) with an optional 3-part catalog name. Touches every `TableId` literal site (potentially invasive; mechanical-edits precedent from prior slices applies but the touch-pattern differs from record extensions because `TableId` is a record-type, not an IR record like `Attribute` / `Kind`). L3-S10 / L3-I10 promotion.
- **Slice ι — IsExternal / Origin mapping audit + L3-Boundary-NoSilentDrop property test** — pure property-test slice; no IR change. Per `CHAPTER_A_0_PRIME_OPEN.md` axis 7's chapter-close ritual, slice ι formalises the chapter's completion criterion: every V1 schema concept in `V2_PRODUCTION_CUTOVER.md` §3.3 either carries to the IR (slices α–η) or routes through `Diagnostic.Severity=Error` at the adapter boundary. Property test asserts the no-silent-drop predicate.

Either ordering works. Recommendation: **slice ι first** — it's smaller, validates the lifts that already shipped, and gates chapter close. Slice θ can land at chapter close or as a subsequent slice depending on operator preference (the TableId extension's invasive blast radius makes it a clean candidate for its own chapter or a dedicated slice).

**Forward signals (named in the DECISIONS amendment).** Rowset slice for triggers / extended properties / defaults / column checks (when V1's rowset bundle extends or DACPAC adapter lands); emitter consumption for the new IR fields per-consumer demand (CommentMetadataUnreflected Tolerance retires when emitters catch up); Module.create parameter-pollution revisit-trigger; IRBuilders retroactive sweep at chapter close; ModalityMark.mapPayload helper extraction at the four-pass-module threshold.

**Outstanding (operator-side; unchanged):**
- R1 — operator's "document of key evolutions" still pending. Hold UAT-users decisions until it lands.
- Q2 / Q3 / Q4 / Q7 unchanged.

## 2026-05-16 (slice β) — Chapter A.0' slice β shipped: IsActive lifted to IR, session-21 boundary filter retired (first pillar-9 worked example)

**Branch / baseline.** New branch `claude/retire-isactive-disposition-WD4Ez` from main post-audible merge (PR #542 merged at `811f539`). Working tree clean before this commit. **Test baseline: 1155 / 1155 passing** (1146 prior + 9 new `IsActiveCarryThroughTests`); 0 skipped; 0 build warnings under `TreatWarningsAsErrors=true`; lint clean across 27 rules. The mechanical-edits precedent from slice α held (the closed-DU record-extension empirical-test discipline catches FS0764 errors at literal-construction sites only; semantic interpretation sites unaffected — 100+ literal sites across 23 test files received `IsActive = true` via a Python pass against the FS0764 worklist).

**What shipped (one commit).** Chapter A.0' slice β: `Module.IsActive`, `Kind.IsActive`, `Attribute.IsActive` lifted into `Projection.Core.Catalog`; six OSSYS-adapter boundary filters retired (JSON: `parseModule`'s entity filter, `parseKind`'s attribute filter; rowset: `parseRowsetBundle`'s module filter, `parseModuleRow`'s entity filter, `parseKindRow`'s attribute filter; plus `parseAttribute*` populator changes). The `isActiveOrDefault` helper is preserved; its role flips from "gate inclusion" to "populate the IR field." `parseModule` (JSON path) gains a module-level `isActiveOrDefault` read (the JSON path did not previously filter at module level per Subagent #3's O2 finding; the read lands for parity with the rowset path's `ModuleRow.IsActive`). `Module.create` gains an `isActive: bool` parameter (the disjointness invariant on Kinds is field-orthogonal; no smart-constructor logic changes). `Projection.Adapters.Sql.ReadSide` defaults the new field to `true` on all three levels — the deployed-schema readback has no V1 `Is_Active` axis, so structural existence = active.

**The DECISIONS amendment is the load-bearing artifact.** `DECISIONS 2026-05-16 (slice β) — Retire OSSYS-adapter IsActive boundary filter; lift IsActive to IR (supersedes session-21)` supersedes the session-21 silent-drop disposition (codified under `DECISIONS 2026-05-15 — OSSYS adapter translation rules`, rule 18). The amendment cites:
- **Pillar 9 (harvest-dichotomy classification)** — slice β is the first worked example at slice level. Session 21's filter-at-the-adapter mis-placed an `OperatorIntent of Selection` at the adapter boundary, which is restricted to `DataIntent` carriage. The source value is `DataIntent` evidence; any filter is `OperatorIntent`, deferred-with-trigger.
- **The 2026-05-16 (later) audible** — V2 self-containment posture: this slice carries no carbon-copy event, no V1 `ProjectReference`, no `ADMIRE.md` row, no `BACKLOG.md` V1-inheritance-log row. The lift is an internal IR refinement.
- **Named failure mode (pillar 9 — skeleton-overlay drift, silent-inclusion-at-harvest sub-mode):** the session-21 rationale "IR grows under evidence — no consumer demands the records' presence" rationalized a *silent drop* of operator intent. The honest disposition (carry as evidence + defer the filter pass; or refuse the operation + document) was not taken at the time.

**Why Kind.IsActive is in scope (not just Module + Attribute as §6.0' / §3.3 of `V2_PRODUCTION_CUTOVER.md` originally scoped).** The chapter-A.0' open's axis 4 amendment names Kind explicitly. Without `Kind.IsActive`, the rowset-path `parseKindRow` and JSON-path `parseKind` filters would have remained as residual silent drops, leaving an asymmetry against the chapter's L3-Boundary-NoSilentDrop completion criterion (slice ι property test). The HANDOFF 2026-05-15 entry flagged this and recommended inclusion; slice β honors it.

**Witnesses.** 9 new tests in `tests/Projection.Tests/IsActiveCarryThroughTests.fs` cover JSON-path + rowset-path × Module / Kind / Attribute × explicit-true / explicit-false / default-true (JSON only), plus cross-source parity. Three rowset-path tests in `OsmRowsetReaderTests.fs` rewrite the prior `SnapshotRowsets: inactive ... drop at the boundary` tests as `SnapshotRowsets: inactive ... carry through with IsActive=false (slice β)`. The slice-2 reference-chain test rewrites as `slice 2 / slice β: inactive source attribute carries through with its reference`. The JSON-path `differential: V1 mixed-active fixture filters inactive records at the boundary` test rewrites as `slice β: V1 mixed-active fixture carries IsActive through at all three levels` (expected catalog now contains all five V1 records — three active, two inactive — with `IsActive` populated from source).

**Pillar 9 bookkeeping — DataIntent vs OperatorIntent for this slice.**
- **DataIntent (skeleton-reachable, no operator opinion):** the new `IsActive` field on `Module` / `Kind` / `Attribute`. Reachable from `Project(catalog, Policy.empty, profile)` without operator opinion. Lands in the skeleton.
- **OperatorIntent of Selection (deferred-with-trigger):** any Selection-axis pass that re-applies an inactive-records drop policy. No current consumer demands it. When a consumer surfaces, the pass lands per chapter-4.x slice scope.

**Next-most-ready slice: A.0' slice γ — `Catalog.Triggers : Trigger list` + `Trigger` value type + adapter pickup.** Per `CHAPTER_A_0_PRIME_OPEN.md` slice plan (L3-S4 promotion: D → A). Dependencies satisfied (β just shipped). Medium risk — new top-level `Catalog` field. Or pivot to slice ε (DefaultValue + Computed + ColumnChecks; L3-S6 / L3-S7 / L3-S8 promotions) if the operator prefers an Attribute / Kind body expansion before a top-level `Catalog` addition. Both inherit the mechanical-edits precedent (Python-pass against FS0764 worklist; `IsActive = true` shape for slice β maps to `Triggers = []` shape for slice γ, etc.).

**Alternative starts (per the 2026-05-15 HANDOFF entry below).** Slice ι (IsExternal / Origin mapping audit; small property-test-only slice; pairs with the chapter-close L3-Boundary-NoSilentDrop scaffolding) is also unblocked. Useful chapter-mid hygiene.

**The A.4.7-prelude small slice** (`LineageEvent.Classification` field) per the 2026-05-15 (late) entry below remains deferred-with-trigger: lands during or just after A.0' close. Slice β does not need it — the pillar-9 classification of slice β is documented in the DECISIONS amendment's text, not in a `LineageEvent.Classification` field.

**Outstanding (operator-side; unchanged from the 2026-05-15 entry below):**
- R1 — operator's "document of key evolutions" still pending. Hold UAT-users decisions until it lands.
- Q2 / Q3 / Q4 / Q7 unchanged.

## 2026-05-16 (audible) — Bridge wave retired; V2 self-containment + carbon-copy editorial inheritance codified

**Branch / baseline.** Continues on `claude/csharp-fsharp-projection-seams-y8MhJ`. Surgical audible commit: removes the Bridge wave artifacts; codifies the replacement framing. Test baseline holds (no F# behavior change; no perf-gate impact).

**What happened.** The same-day Bridge wave codification introduced two C# Bridge projects, a `[BridgeMethod]` audit attribute, a four-state inheritance gradient, a wall analyzer, a 2,300-line manifesto, and a V2-for-V1 surface. The operator directed an audible on three grounds: (1) V1 should disappear from memory into V2 — not be a structural element of V2's runtime via ProjectReferences and Bridge wrapper layers; (2) the F#/C# partition should be by language idiom, not by V1/V2 lineage; (3) the user's goal is *tethering and inspiration*, not delegation-to-V1 — V1 is editorial donor, not Bridge backend.

**The new operative philosophy.** V2 is **fully self-contained**. No `ProjectReference` to V1's trunk csprojs. No V1 assembly on V2's classpath at any point. When V2 wants a V1 capability, V2 reads V1's source for inspiration, decides what's worth keeping, and **carbon-copies** the relevant V1 source files into V2's domain-structured locations — existing F# adapter projects for algebraic capabilities, or new **museum-polish** C# adapter projects for capabilities whose gold-standard library is irreducibly C#-idiomatic (SMO, DacFx). The carbon-copy may land verbatim (rename to V2 vocabulary in a follow-up commit) or refactored at copy-time (lands already V2-shaped) — pragmatic per file. Each carbon-copied file carries a one-time file-header citation comment naming the V1 source; the corresponding `ADMIRE.md` entry carries the editorial trail.

**The F#/C# partition reverts to its original philosophical boundary.** F# for the pure algebraic core (`Projection.Core`); F# for adapters wrapping external libraries at the boundary (`Projection.Adapters.Osm`, `Projection.Adapters.Sql`, `Projection.Targets.*`, `Projection.Pipeline`); a small, museum-polish C# layer for capabilities whose gold-standard library is irreducibly C#-idiomatic. The C# layer is named for what it adapts (e.g., `Projection.Adapters.OssysSql`), domain partition not language partition.

**Cherry-pick safety holds by construction.** V2 has no V1 references; every commit is cherry-pickable into a V1-only trunk by definition. The earlier framing ("the boundary is data, not typed cross-references") is restored as the load-bearing form.

**V1 from V2's perspective is frozen in time** at the V1 head V2 saw when it inherited. V1's actual ongoing evolution (V1 has switched to a different remote and its head is ahead of what V2 sees per the operator) is V1's concern alone; V2 aims for parity with the V1 version V2 carbon-copied from. Subsequent V1 evolution is not automatically tracked.

**What was removed this commit:**

- `Projection.Bridge.Core` C# project — deleted entirely (the csproj, the audit primitives, the wire records, the worked-example capability).
- `Projection.Bridge.Runtime` C# project — deleted.
- `Projection.Bridge.Tests` test project — deleted.
- `CSHARP_FSHARP_MANIFESTO.md` — deleted (the philosophy lives in CLAUDE.md + README.md sections now; no separate manifesto).
- `CHAPTER_0_5_OPEN.md` — deleted (the Bridge bring-up does not exist).
- `Projection.sln` entries for the three Bridge projects — removed.
- `AXIOMS.md` Bridge clause on A41 candidate + A42 candidate — removed.
- `CLAUDE.md` Bridge inheritance operating-discipline row + load-bearing commitments — removed.
- `README.md` "V2 inherits from V1" Bridge framing prose — removed.
- `V2_DRIVER.md` Phase 0.5 Bridge wave addition — removed.
- `V2_PRODUCTION_CUTOVER.md` §13.X Bridge wave addendum — replaced with V2 self-containment addendum.

**What was added this commit:**

- `CLAUDE.md` operating-disciplines row: "F#/C# language-role partition + V1 as editorial donor". Codifies V2 self-containment; the carbon-copy editorial discipline; the file-header-citation audit trail; cherry-pick safety holds by construction.
- `CLAUDE.md` load-bearing commitments: V2 self-containment commitment with the no-ProjectReference, no-V1-assembly, carbon-copy-into-domain-structure clauses.
- `README.md` section: "V2 is self-contained; V1 is editorial donor". The prose framing of the new philosophy.
- `DECISIONS 2026-05-16 (later) — V2 self-containment + carbon-copy editorial inheritance (Bridge wave audible)`. Codifying entry; the earlier same-day Bridge wave entry is preserved as historical record of the rejected direction with a SUPERSEDED marker pointing at the audible.
- `ADMIRE.md` format amendment — entries with placement "carbon-copied" record the V1 source path, V2 location, date inherited, refactor status, and citation comment. Replaces the Bridge-gradient pair amendment.
- `ADMIRE.md` per-entry updates — the four affected entries (NullabilityEvaluator, UniqueIndexDecisionOrchestrator, ForeignKeyEvaluator, OSSYS catalog producer) now carry audible-update paragraphs naming the new posture (no Bridge nomenclature).
- `V2_DRIVER.md` chapter sequencing — V1 inheritance posture paragraph at the top (no Phase 0.5; the phases below cite V1 inheritance opportunities per chapter, but no Bridge prerequisite exists).
- `V2_PRODUCTION_CUTOVER.md` §13.X — V2 self-containment addendum.
- `BACKLOG.md` — rewritten under the new philosophy. Integrated structure preserved (phase-by-phase, cross-cutting work, V1 inheritance log, risk register, sequencing graph). Bridge-specific content excised. The V1 inheritance log replaces the master Bridge-methods table; the V2-for-V1 section is removed.

**Two preserved ADMIRE re-classification corrections (independent of Bridge nomenclature):**

- `NullabilityEvaluator` and `ForeignKeyEvaluator`: PURE PASS in F#. V1 evaluators are mode-bound policy front-to-back; V2's strategy rules cover the rule space directly. No carbon-copy candidate.
- `UniqueIndexEvidenceAggregator`: carbon-copy candidate (the only one of the three V1 evaluators). Lands at the chapter that consumes the lifted evidence.
- `EntityDependencySorter`: harmonized via existing `TopologicalOrderPass.SelfLoopPolicy` (A40). No carbon-copy candidate.
- OSSYS catalog producer: highest-value carbon-copy candidate. Lands in a dedicated C# adapter project (`Projection.Adapters.OssysSql`, museum-polish) when the chapter consuming it opens.

**What is load-bearing in this session.**

- V2 self-containment is now operating-discipline tier. Every agent confirms intent against it before adopting any new pattern that touches the V1↔V2 relationship.
- V2 has zero V1 ProjectReferences. The build is V1-independent.
- Carbon-copy events land in `ADMIRE.md` + a one-time file-header citation comment + a row in `BACKLOG.md` § "V1 inheritance log".
- The C# layer in V2 is constrained to capabilities whose gold-standard library is irreducibly C#-idiomatic, with museum-polish quality. New C# adapter projects are named for the capability they adapt, not for V1.

**Continue on the in-flight A.0' chapter slice β next.** A.0' was paused under the Bridge wave; the audible removes the Bridge prerequisite. A.0' slice β (the IsActive disposition retirement) resumes as the next-most-ready slice. The pillar-9 worked-example framing for IsActive disposition is preserved (per the 2026-05-15 late entry below); slice β's DECISIONS amendment cites pillar 9 + the V2 self-containment commitment.

**Outstanding (operator-side):**
- R1 — operator's "document of key evolutions" still pending. Hold UAT-users decisions until it lands.
- Q2 / Q3 / Q4 / Q7 unchanged from the 2026-05-12 final handoff below; revisable during touching slices.

## 2026-05-15 (late, second pass) — Pillar 9 + DataIntent/OperatorIntent reification + canonical-strongly-typed registry shape

**Branch / baseline.** Continues on `claude/research-v2-direction-zKg9g`. Documentation-only commit (second pass of the same documentation surface as the entry below); no code changes; test baseline unchanged (1146/1146).

**Twelve-question principal-PO design session sharpened the framing significantly.** The first 2026-05-15 entry (below) re-opened the transform registry under skeleton-overlay separation pressure but under-developed three load-bearing aspects. The late-2026-05-15 codification fills them in:

1. **Pillar 9 — harvest-dichotomy classification — codified at supreme-operating-discipline level.** The dichotomy is a meta-discipline operative AT HARVEST TIME (when an agent reads v1 or any source for what to bring forward) — every transformation site is classified `DataIntent` (preserves data intention; lands in skeleton) or `OperatorIntent of OverlayAxis` (operator-supplied intent; lands as registered overlay). The classification is the *outcome of agent harvest analysis*, not a property the code wears. The harvest workflow is named (4 steps); the harvest-gap triple-deliverable is named (Skip stub + Tolerance entry + `NotImplementedInV2` registry entry); the named failure mode is *skeleton-overlay drift* (three sub-modes, each caught by a property test). Sibling to pillar 8 (domain-first naming), the LINT-ALLOW substantive-rationale discipline, and the text-builder-as-first-instinct discipline — four meta-disciplines, each applied at consideration time, each enforced bidirectionally by structural tests. See `DECISIONS 2026-05-15 (late) — Pillar 9: harvest-dichotomy classification`.

2. **Policy IS operator intent, reified.** `OverlayAxis = Policy DU axes exactly` (Selection / Emission / Insertion / Tightening). `OperatorIntent (Overlay Tightening)` reads as "operator intent expressed via the Tightening axis." Ubiquitous-language consistency: Policy axes and OverlayAxis values are the same thing, structurally. Existing `Projection.Core.Policy.fs` becomes a use-site of `OverlayAxis`. Reserved for expansion if a fifth axis warranted; today four exactly.

3. **The registry is canonical for both metadata AND the transformation-function definition itself; no parallel enumeration.** Each pass module's primary public surface becomes `<PassName>.registered : RegisteredTransform<'In, 'Out>`; the `let run` function becomes private; consumers invoke `registered.Run`. Five stage seams (`Adapter | Pass | OrderingPolicy | Emitter | Pipeline`). Single-definition-site discipline. `Compose.run` traverses the registry as its execution loop. The full-sweep retroactive scope (every pass + 25 OSSYS adapter rules + emitter strategies) bumps A.4.7 from ~1.5-2 weeks to **~3 weeks** estimated.

4. **The registry is the FOURTH cross-cutting concern**, sibling to Lineage / Diagnostics / Bench — together they form V2's structural-evidence layer. Each plugs into every stage that has its kind of activity; each is enforced structurally; each has its own writer/observer primitive. The cross-cutting framing is now visible in `V2_DRIVER.md` per-axis stakes table and `PRODUCT_AXIOMS.md` L3-CC-Transform-Totality.

5. **Three-step rollout (per Q4):** discipline + 9th pillar + L3 axiom land NOW (this session). `LineageEvent.Classification` field lands as a small slice during or just after A.0' (A.4.7-prelude). Full structural surface (registry refactor + Compose.run traversal + bidirectional property tests + CLI flag + manifest extension) lands at A.4.7 proper post-A.0' close.

**What shipped (this session, documentation-only, second pass):**

- **`DECISIONS.md`** — new entry `2026-05-15 (late) — Pillar 9: harvest-dichotomy classification (DataIntent vs OperatorIntent); registry as cross-cutting concern; canonical strongly-typed registry shape`. ~250 lines codifying pillar 9, the 4-step harvest workflow, the strongly-typed canonical registry shape (`RegisteredTransform<'In, 'Out>`), the 5-stage `StageBinding` DU, the `Sites : TransformSite list` for intra-pass classification fidelity, the bidirectional property tests, the four-cross-cutting-concerns frame, and the three-step rollout.
- **`PRODUCT_AXIOMS.md`** — L3-CC-Transform-Totality restated with DataIntent/OperatorIntent vocabulary; bidirectional property tests; 5-stage cross-cutting framing; harvest discipline cross-reference.
- **`AXIOMS.md`** — A41 candidate placeholder strengthened with the full type-system shape (`Classification`, `TransformSite`, `RegisteredTransform<'In, 'Out>`, `OverlayAxis = Policy axes`, `TransformStatus`); canonical-registry decision documented.
- **`V2_PRODUCTION_CUTOVER.md`** — §6.4.7 substantially rewritten: full-sweep retroactive scope (~3 weeks); strongly-typed canonical registry shape with type definitions; 5-stage seam handling per Q7 unified type parameters; `Compose.run` traversal refactor per Q5; binary `--skeleton-only` CLI per Q8; harvest-workflow triple deliverable per Q10; intra-pass classification fidelity via `Sites` list per Q11; bidirectional property tests per Q12 (5 named tests covering skeleton-purity + overlay-exercise + totality coverage + harvest-classification cross-reference + manifest round-trip).
- **`V2_DRIVER.md`** — per-axis stakes row reframed with cross-cutting sibling framing (Lineage / Diagnostics / Bench / TransformRegistry as four-concern structural-evidence layer); pillar 9 reference; A.4.7 effort estimate updated to ~3 weeks.
- **`CLAUDE.md`** — operating-disciplines row updated for pillar 9 elevation; load-bearing commitments list updated with DataIntent/OperatorIntent vocabulary + canonical-registry shape + 5-stage seams.
- **`docs/architecture/entity-pipeline-unification-v2.md`** — header banner strengthened to invoke pillar 9 as the operative classification any agent reading the doc must apply.

**Immediate operative consequences:**

- **In-flight A.0' slice β (IsActive disposition retirement) is the FIRST WORKED EXAMPLE of pillar 9 at harvest time.** Per the harvest analysis: session-21's silent-drop of inactive records filtered on operator-meaningful "active/inactive" status — that's `OperatorIntent` (no current `OverlayAxis` fit; one of the candidate axis-expansion triggers). Slice β retires that mis-placement and lifts `IsActive` to the IR as `DataIntent` evidence. The slice-β DECISIONS amendment (originally just "supersede session-21") now should ALSO cite pillar 9 + classify the original disposition + the corrected disposition.
- **Every chapter-close ritual gains a pillar-9 check.** Per the chapter-close ritual discipline, every close adds a one-paragraph audit step. Future chapter closes now include: "Which transformations did this chapter introduce or modify? What's each transformation's classification? Does the registry / Tolerance / Skip-stub triple deliverable hold for any v1-harvest gaps?"
- **Pillar 9 is operative for the v1-soak debt vectors too.** When V1.1 (EntityFilters wiring), V1.2 (global topo for StaticSeeds), V1.3 (DatabaseSnapshot dedup) land as v1-side PRs, the harvest discipline applies during V2's Phase A.6 differential testing: any v1 transformation that becomes visible during soak gets classified before tolerance decisions are made.

**Continue on the in-flight A.0' chapter slice β next.** The new framing is *additive* to slice β; it doesn't redirect. Slice β remains the next-most-ready work. Pillar 9 is operative for the slice's harvest analysis; the slice-β DECISIONS amendment supersedes session-21 AND cites pillar 9 for the classification rationale.

**A.4.7 is the next-chapter target after A.0' close.** ~3 weeks; full-sweep retroactive refactor; canonical strongly-typed registry; bidirectional property tests. The A.4.7-prelude small slice (`LineageEvent.Classification` field) lands during or just after A.0' to let events self-classify before the full traversal refactor.

## 2026-05-15 — Transform registry re-opened as L3-CC-Transform-Totality (A.4.7 specced; chapter A.0' continues)

**Branch / baseline.** Continues on `claude/research-v2-direction-zKg9g`. Documentation-only commit; no code changes; test baseline unchanged (1146/1146).

**The principal-PO surfaced an axiomatic finding during a v1-doc review:** the skeleton/overlay separation is the structural seam V2 needs to stay clinical/laboratory-quality as it scales. *Factual/objective/skeletal* output = `Project(catalog, Policy.empty, profile)` (the deterministic baseline reachable without operator opinion). *Opinionated/override/subjective* overlay = the ordered, named, registered set of `Pass` invocations that compose the baseline into the full output. Both halves must be enumerable, recoverable, audit-traceable. A18 amended is the Π-side commitment; the transform registry is the *Pass-side* commitment. The two siblings together carry the decomposition as type-witnessed contract, not discipline.

**What shipped (this session, documentation-only):**

- **`PRODUCT_AXIOMS.md`** — new **L3-CC-Transform-Totality** axiom in Group CC. Tier 1 (cutover blocker); Bucket D pending A.4.7. Co-equal load-bearing with CDC silence per the new V2_DRIVER per-axis stakes row.
- **`AXIOMS.md`** — new **A41 candidate (Transform registry totality)** placeholder in Amendments scheduled; body fills at A.4.7 close.
- **`DECISIONS.md`** — new entry `2026-05-15 — Transform registry re-opened: skeleton-overlay separation as L3-CC-Transform-Totality`. Re-opens the 2026-05-13 cash-out under different consumer pressure (skeleton-overlay separation, not pipeline composition); preserves the prior reasoning while naming the different shape the registry takes under the new pressure. Active deferrals index updated to disambiguate the strategy-registry-mechanism (still deferred under its original framing) from the transform-registry (re-opened under the new framing).
- **`V2_PRODUCTION_CUTOVER.md`** — new workstream **§6.4.7 A.4.7** (Campaign B core; load-bearing for laboratory-quality scale); §12.6 delivery matrix row added; new §13.6 V1-soak debt lane addendum carrying the three v1-side cleanup vectors (EntityFilters wiring; global topo for StaticSeeds; DatabaseSnapshot dedup).
- **`V2_DRIVER.md`** — per-axis stakes table gains a "Skeleton/overlay separation" row at verification depth = Highest; new "V1-soak debt lane" section in the backlog (V1.1 / V1.2 / V1.3 v1-side PRs).
- **`CLAUDE.md`** — operating-disciplines table gets a row pointing at the DECISIONS entry; load-bearing commitments list gains L3-CC-Transform-Totality.
- **`docs/architecture/entity-pipeline-unification-v2.md`** — header banner added clarifying this is a v1-refactor doc (not v2 plan); names the three vectors promoted to V2_DRIVER's v1-soak debt lane.

**Why this is load-bearing and not just a backlog item.** Per the new V2_DRIVER framing: the chapter-4.x scope expansion grows the number of policy-driven mutations monotonically (User FK reflow; operational diagnostics; multi-environment policy/profile parameterization). Without the registry seam, each new pass is one more convention to track in code review. The four-question naming analysis (pillar 8) catches naming drift; the LINT-ALLOW substantive-rationale discipline catches string-composition drift; **the transform registry catches skeleton/overlay drift.** Three sibling disciplines, each preventing a class of failure that scales linearly with codebase growth. CDC silence is the highest-leverage *property test*; transform registry is the highest-leverage *structural-enforcement seam*. Co-equal load-bearing.

**What this re-opening does NOT do** (per the DECISIONS entry's preserved-reasoning protocol):
- It does NOT introduce a single linear `pass1 >> pass2 >> pass3` pipeline. The per-use-case driver pattern stands; the registry is **enumerative**, not **compositional**.
- It does NOT add reflection or name-keyed runtime dispatch. Compile-time `module TransformRegistry` referencing each `Pass` module by name. CLAUDE.md's "reflection is out of scope for Core" holds.
- It does NOT replace `Composition.fanOut`. Strategies fan out *within* a pass; the registry enumerates *passes themselves*. Different granularities, both preserved.
- It does NOT introduce per-pass policy axes the operator can toggle individually. `--skeleton-only` is binary (baseline vs. baseline+all-overlays). Granular toggling deferred-with-trigger.

**Sequencing.** A.4.7 depends on **A.0' close** (chapter A.0' is still in flight; slice α shipped, slice β next per the section below). The registry enumerates against the post-IR-fidelity pass set; opening A.4.7 before A.0' closes would lock in a pass set A.0' is still extending. Concurrent with A.4.5 / A.4.6 acceptable once A.0' closes.

**Continue on the in-flight A.0' chapter slice β next.** The new A.4.7 spec is the *next-chapter* target, not a redirect. The framing below (slice β: `Module.IsActive` + `Attribute.IsActive` + retire boundary filter, with DECISIONS amendment superseding session-21) is still the next slice to land. Operator-side check before slice β: alignment on retiring the inactive-records filter (carries semantic shift; DECISIONS amendment required).

**The three v1-soak debt vectors are independently shippable** by v1 maintainers; they accelerate Phase A.6 soak by removing false-positive disagreement classes (V1 over-fetching; V1's broken StaticSeeds FK order; V1's triple-fetch variability). Each is small, surgical, reversible. Not load-bearing for V2-driver KPI directly; the KPI tracks V2-axis property tests. See `V2_PRODUCTION_CUTOVER.md` §13.6 for the per-vector rationale.

## 2026-05-15 — Chapter A.0' open + slice α shipped

**Branch / baseline.** Branch: `claude/review-handoff-docs-CF2v5`. PR #538 (chapter pre-A.0') merged at `8733d0c`; PR #539 follow-up merged. Chapter A.0' opened at commit `3c75d00`. **Test baseline: 1146 / 1146 passing** (1128 prior + 11 canary tests now visible with Docker running + 7 new `DescriptionLiftTests`). Zero regressions; `TreatWarningsAsErrors=true` clean; lint clean.

**What shipped this session.** The operator picked A.0' (IR fidelity lifts) over A.7.2 (ManifestMatchesDisk). The chapter opens the 7-9-slice arc that promotes L3-S4 through L3-S10 + L3-I10 + L3-CC4 + L3-Boundary-NoSilentDrop from Bucket D → Bucket A:

- `CHAPTER_A_0_PRIME_OPEN.md` — strategic-frame axes (8 numbered), slice plan (α–ι), out-of-scope, success criteria. Use this as the chapter's reading-order item.
- **Slice α — `Kind.Description` + `Attribute.Description`** (commit `3c75d00`). Purely additive. OSSYS adapter populates from JSON `description` field (defensive read via `getOptionalString`) and from extended `KindRow.Description` / `AttributeRow.Description` (rowset DTOs extended too). `Projection.Adapters.Sql.ReadSide` sets `None` — extended-property pickup gates on chapter 4.1.A slice 8. ~170 record-literal sites across 23 test files received `Description = None` per the closed-DU record-extension empirical-test discipline (chapter 3.2 close generalization). 7 new tests in `DescriptionLiftTests.fs` cover JSON-path + rowset-path roundtrip and `None`-default cases.

**The next-most-ready slice: A.0' slice β — `Module.IsActive` + `Attribute.IsActive` (carry-through; retire boundary filter).** Dependencies satisfied (α just shipped). Scope: extend `Module` and `Attribute` with `IsActive : bool` fields; retire the session-21 inactive-records filter at `parseModule` / `parseKind` / `parseModuleRow` / `parseKindRow`; carry the flag through to the IR; downstream emitters decide. **DECISIONS amendment required** superseding session-21's silent-drop disposition. Consider adding `Kind.IsActive` too — §3.3's omission of Kind is likely an oversight (entity-level `Is_Active` exists in V1 OSSYS); without it the entity-level filter stays at the adapter and creates an asymmetry with the L3-Boundary-NoSilentDrop completion criterion. Read `CHAPTER_A_0_PRIME_OPEN.md` axis 4 + the §6.0' / §3.3 spec before deciding.

**Alternative starts** if slice β is too disruptive (the IsActive semantic shift may want operator alignment first):

- **Slice ε — `Attribute.DefaultValue : SqlLiteral option`** (additive; matches α's pattern). V1 JSON has `"default": null` already; needs a small JSON-to-SqlLiteral parser at the adapter boundary. No prior decisions to supersede. ~130 attribute literal sites need `DefaultValue = None` added (same blast radius as α; see `/tmp/fix_records.py` precedent if helpful).
- **Slice ι — IsExternal / Origin mapping audit** (pure property test; no IR change). Lift the existing `parseOrigin` discipline into a property test asserting V1 `IsExternal=true → V2 Origin ∈ {ExternalViaIntegrationStudio; ExternalDirect}`. Small slice; useful chapter-mid hygiene. Pairs with the chapter-close L3-Boundary-NoSilentDrop property test scaffolding.

**Outstanding (operator-side; same as post-A.7.1):**
- R1 — operator's "document of key evolutions" still pending. Hold UAT-users decisions until it lands.
- Q2 / Q3 / Q4 / Q7 unchanged; revisable during touching slices.

**Mechanical-edits precedent** for record-extension slices: the slice-α experience produced a reusable workflow. Step 1: extend the IR record + the adapter DTOs (`Catalog.fs` + `CatalogReader.fs`). Step 2: build, capture FS0764 worklist. Step 3: sed-pass for inline-close-brace literals (`s/(IsIdentity = (true|false))(\s*)\}/\1; Description = None\3}/g` analog); python-pass for multi-line records (brace-counter walking from the opening `{` to find the matching `}`). Step 4: property test in a new `<SliceName>LiftTests.fs` file added to `Projection.Tests.fsproj`. Step 5: build + test + commit. Slice β / ε / γ / δ inherit. The closed-DU record-extension empirical-test discipline holds across all of them (chapter 3.2 close codified this).

**Load-bearing methodology unchanged from the 2026-05-12 final handoff above.** L1↔L2↔L3 verifiability triangle is the lens for structural work; campaigns are cross-cutting tags; per-PR L3 review for PRs touching boundary code or CLI surface.

**Per-axiom delivery matrix updates** at chapter-A.0' close: cash `L3-S9` descriptions sub-axiom (advances toward full L3-S9; full lands at slice ζ ExtendedProperties). Forward-signal Tolerance retirement: `CommentMetadataUnreflected` is one step closer (the IR now carries descriptions; emitter consumption is chapter 4.1.A slice 8 territory).

## 2026-05-12 — Final handoff (post-A.7.1; PR #538 active)

**PR / branch / baseline.** Pull request: [#538](https://github.com/danielbdyer/outsystems-ddl-exporter/pull/538). Branch: `claude/audit-v1-v2-sidecar-7Ifij`. Subsequent commits to this branch update the PR. **Test baseline: 1128 / 1128 passing** (1121 + 7 new atomic-write property tests; zero regressions; canary excluded from this baseline as usual). All slices below ship clean under `TreatWarningsAsErrors=true`.

**Reading order for an agent picking this up.** Read in this order, top-down:

1. **`AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md` Part I (framing) + Part IX (campaigns)** — the methodology and the implementation plan. ~150 lines total.
2. **`V2_PRODUCTION_CUTOVER.md`** Draft 3 — the canonical plan-of-record with axiom-tagged workstreams. Skim §1 (five Draft-3 insights), §5 (composition algebra), §12 (per-axiom delivery matrix). ~994 lines.
3. **`PRODUCT_AXIOMS.md`** — the L3 axiom catalog; constitutional sibling to `AXIOMS.md`. Reference material.
4. The relevant audit-doc Part IV / Part VI section for the slice you're picking up.

Then the relevant slice-specific files. Do not start coding without reading at least #1 + the §12 row for the axioms your slice operationalizes.

**Two operator decisions locked in this session:**
- **Q14 — Campaign A sequencing:** atomic emission first. **Done at A.7.1 (commit `4e3d944`).**
- **Q15 — Axiom-naming convention:** `L3-Boundary-*` namespace in `PRODUCT_AXIOMS.md`; `AXIOMS.md` A41+ stays reserved for algebra-interior extensions. **Codified in `PRODUCT_AXIOMS.md` Group Boundary.**

**What shipped this session, in commit order:**

| Commit | Slice | Axioms promoted (D → A or B → A) |
|---|---|---|
| `2ab3a8a` → `143a885` | Cutover plan Drafts 1 → 3 (5 commits) | (planning; no code) |
| `491fbb5` | Verifiability-triangle audit doc | (audit; no code) |
| `72ff8a3` | `PRODUCT_AXIOMS.md` sibling + 6 cross-refs | (doc system; no code) |
| `93468a3` | A.0 Config + D9 guardrail | L3-X9, L3-C8 |
| `df18bbf` | A.1 `emit --config` bridge | L3-X9 (CLI) |
| `502592f` | A.4 TableRename + RenameBinding + Compose.runWithConfig | L3-I1, L3-I7, L3-C7 (**R11 dissolved**) |
| `9d578cc` | Slice 1 PhysicallyRenamed variant | (L3-I1 audit-trail typed) |
| `4e3d944` | A.7.1 atomic emission | **L3-Boundary-AtomicEmission (first formalized boundary axiom)** |

**The next-most-ready slice: A.7.2 — `L3-Boundary-ManifestMatchesDisk`.** Dependencies satisfied (A.7.1 just shipped). Scope: change `ManifestEmitter` to consume the path list returned by `Compose.write` rather than the in-memory `Outputs`; add a property test that every manifest entry exists on disk and every file on disk has a manifest entry post-write. Small surgical change; estimated under 1 day. See `V2_PRODUCTION_CUTOVER.md` §6.7 (workstream A.7.2 spec) + `AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md` Part IX Campaign A.

**Alternative starts** if A.7.2 is too small or you prefer parallel work:
- **A.4.5 — Catalog cross-field invariants batch (Campaign B).** Mechanically simple, very high leverage. Extends `Catalog.create` with 7 cross-field invariants (IsIdentity⇒¬IsNullable, IsPrimaryKey⇒IsUnique, Type/Length/Precision/Scale coherence, Length≤8000, PK ordering, single Static modality, physical-name uniqueness). One PR, ~2 weeks, promotes 8 new L3-Catalog-* axioms to Bucket A. See `V2_PRODUCTION_CUTOVER.md` §6.4.5 + audit Part VI.2.
- **A.0' — IR fidelity workstream (Campaign A.2 prerequisite).** Largest single body of work (~3-4 weeks, 5-7 slices). Promotes 8 Tier-1 unnamed axioms (L3-S4 through L3-S10 + L3-I10 + L3-CC4) when complete. Read `V2_PRODUCTION_CUTOVER.md` §3.3 (gap table) + §6.0' (workstream spec).

**Outstanding (unblocked but operator-side):**
- **R1 — Operator's "document of key evolutions"** (focuses on UAT-users; reshapes scope). Until it lands, hold UAT-users decisions; don't pre-scope speculatively. See `V2_PRODUCTION_CUTOVER.md` §13.1.
- Q2 (Argu vs hand-rolled CLI parser), Q3 (legacy positional backward-compat), Q4 (Profile JSON shape coupling), Q7 (sampling thresholds): all revisable during the slices that touch them; don't escalate.

**Load-bearing methodology** (per `DECISIONS 2026-05-12 — Verifiability-triangle audit methodology` + `CLAUDE.md` operating-disciplines row):

- The L1↔L2↔L3 verifiability triangle is the lens for all subsequent structural work. Every workstream carries an axiom-promotion delta (`Δ : axiom_id × bucket_before → bucket_after`); cutover criteria are axiom-bucket-witnessed; campaigns are cross-cutting tags, not parallel phases.
- Per-PR L3 review for PRs touching boundary code or adding config/CLI surface: "which L3 axioms does this touch; are they Bucket A or below; does this strengthen or weaken the structural commitment?"
- Chapter-close L3 step: every chapter close adds a one-paragraph audit check naming axioms touched and new Bucket-D gaps introduced.
- Annual re-audit refresh.

**Deferred-with-trigger** (unchanged):
- `LiveOssysConnection` (chapter 3.2 forward signal) remains reserved.
- Lifecycle temporal axis named in A6-amended but not operationalized (placeholder Group Lifecycle in `PRODUCT_AXIOMS.md`).
- 4 IR concepts deliberately NOT lifted in A.0' (OriginalName / ExternalDatabaseType / per-column IndexColumnDirection / IsPlatformAuto) per `V2_PRODUCTION_CUTOVER.md` §11.5.

**One thing to internalize before coding:** *campaign tags are cross-cutting, not sequential.* A.7.1 was Campaign A. A.4.5 is Campaign B. The next slice you pick up will likely carry a campaign tag too. The campaign isn't a phase to "finish before moving on" — it's a structural-commitment class that the slice belongs to. Read the §12 delivery matrix entry for whatever axioms your slice operationalizes; that tells you the campaign membership.

## 2026-05-12 — V2 cutover plan + verifiability-triangle audit landed (preserved; some items now resolved in the section above)

**Branch:** `claude/audit-v1-v2-sidecar-7Ifij`. **Status:** session-driven, not chapter-driven; pivot from chapter-5 work into a product-readiness audit + structural-commitments campaign plan. Test baseline holds (1121 tests passing post-Slice-1 PhysicallyRenamed).

What landed this session, in order:

- **`V2_PRODUCTION_CUTOVER.md`** (Draft 2.2) — the cutover plan: phase ladder, IR-fidelity workstream (A.0'), config schema, locked-in decisions D1–D12, risks R1–R12, deferral catalog. Currently the canonical plan; campaigns from the audit below operationalize as Phase A workstreams.

- **Slice 1: `PhysicallyRenamed` variant** (commit `9d578cc`) — first "airtight-by-design" slice. `TransformKind` extended with typed `PhysicallyRenamed of PhysicalRename` carrying `{ Before; After }` TableIds. `TableRename` emits the new variant; no-op renames suppressed. 1121 tests green.

- **`AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md`** (1410 lines) — the integrator's view of V2's structural posture across three levels: L1 commitments / L2 axioms (`AXIOMS.md`) / L3 product axioms. Coverage map (Bucket A/B/C/D), 9-tier illegal-states catalog from a 4-agent bottom-up scan, gap-hunt 30 candidate axioms, three proposed campaigns (A: 4 cutover-blocker unnamed axioms; B: structural fortification subsuming the prior slice-2/slice-3 work; C: Tier-2 + boundary VOs + Config strengthening). **Read Part I (framing) + Part IX (campaigns) at minimum.**

- **`PRODUCT_AXIOMS.md`** — the canonical L3 sibling to `AXIOMS.md`. 56 L3 product axioms grouped by core concern (schema/data/identity/diagnostics/cutover-safety + cross-cutting) plus four Tier-1 unnamed boundary candidates pending Campaign A.

- **Cross-reference updates** across `CLAUDE.md` (reading order item 3.5 + operating-disciplines row), `AXIOMS.md` (header pointer), `V2_PRODUCTION_CUTOVER.md` (companion-docs line + §11.4 audit-log entry), `DECISIONS.md` (verifiability-triangle methodology entry), `README.md` (brief pointer).

**Outstanding before next slice begins:**
1. Operator decision on Campaign A ordering (atomic emission vs CDC silence first).
2. Operator decision on axiom-naming convention (extend A41+ in `AXIOMS.md` vs separate `L3-Boundary-*` namespace).
3. Operator's "document of key evolutions" (R1) — will likely reshape UAT-users scope and possibly add a sixth core concern.

**Load-bearing:** the L1↔L2↔L3 verifiability triangle is the lens for all subsequent structural work. Per the new operating discipline in `CLAUDE.md`, every chapter close adds a one-paragraph audit check (which L3 axioms touched; new Bucket-D gaps introduced). Per-PR L3 review for PRs touching boundary code or adding config/CLI surface.

**Deferred-with-trigger:** `LiveOssysConnection` (chapter 3.2 forward signal) remains reserved; Lifecycle temporal axis named in A6-amended but not operationalized (placeholder Group Lifecycle in `PRODUCT_AXIOMS.md`).

## Chapter 5 open + slices ν + θ (added 2026-05-11; FSharp.Analyzers.SDK + Coordinates Stage 2 VOs)

**Branch:** `claude/chapter-4-ddd-improvements-XVCAM`. **Test baseline:** 1072 non-canary tests passing (+12 across slices ν + θ); 0 skipped; 0 build warnings under `TreatWarningsAsErrors=true`. **Lint:** clean across 27 rules (one LINT-ALLOW added on the analyzer's diagnostic message at a terminal text-emission boundary; rationale per `DECISIONS 2026-05-10 — LINT-ALLOW substantive-rationale discipline`).

Chapter 5 (Phase 8 pragmatic close per V2_DRIVER §252) opens as the formal chapter name for the consumer-pressure-driven hygiene + governance queue. **Open-ended**: slices land as separate commits; no single-chapter close fires until the queue empties or stabilizes per V1-sunset milestones.

### Slices ν + θ (chapter open)

| # | Slice | What |
|---|---|---|
| ν | F# Analyzers SDK custom analyzer | New `Projection.Analyzers` project (net8.0; `FSharp.Analyzers.SDK` 0.30.0 pinned for F#-9-SDK compat); one analyzer `Projection001NoUnsafeTimeInCore` (untyped-AST walk; detects `DateTime.Now`/`UtcNow`/`Today`/`Guid.NewGuid`/`Random.Shared` calls under `src/Projection.Core/`); `.config/dotnet-tools.json` registers `fsharp-analyzers`; `scripts/run-analyzers.sh` is the opt-in runner. End-to-end verified: runner walks all 28 Core files, reports zero violations (Core is clean by discipline). |
| θ | Coordinates Stage 2 typed VOs | `SchemaName` / `TableName` / `ColumnName` single-case-DU smart constructors land in `Coordinates.fs`. Reject null / empty / whitespace; reject >128 chars (SQL Server identifier limit). **Record-field migration deferred-with-trigger** (Stage 1 docstring's "real bug" trigger preserved; typed surface is opt-in for new code; existing `string`-field readers compile unchanged). 12 acceptance / rejection / boundary tests. |

### Outstanding queue (post-chapter-5-open)

**Within chapter 5 (deferred-with-trigger; consumer-pressure-driven):**

- **PhysicalRealization / Column.ColumnName record-field migration to Stage 2 typed VOs** — `Coordinates.fs:19-23` Stage 1 trigger preserved.
- **Additional FSharp.Analyzers.SDK analyzers** — false-negative on the grep rules drives new analyzer adoption.
- **CI integration for the analyzers runner** — earns its place when the analyzer set grows beyond one rule.
- **Hex port lifts** (`IArtifactSink`, `IDeployHost`) — under genuine consumer demand.
- **Cutover-day operator runbook** — joint deliverable with solution architect.
- **V1 sunset planning** — after cutover+30 + one full schema-evolution cycle.

**Deferred-with-trigger from chapter 3.x close:**

- Slice ε — Modality marks → comments / extended properties.
- Slice ζ — Byte-determinism cash-out via post-hoc Origin.xml canonicalization.
- Per-Catalog parameterization of DockerImageEmitter Dockerfile + entrypoint.
- Chapter 4.4 RemediationEmitter (V2_DRIVER §147 free-corollary).

**Quietly-deferred queue** — preserved at the chapter 3.x close prologue below.

---

## Chapter 3.x close (added 2026-05-11; DacpacEmitter dev-tooling + DockerImageEmitter; V2-driver KPI Phase 6 substantively shipped under reframe)

**Branch:** `claude/chapter-4-ddd-improvements-XVCAM`. **Test baseline:** 1060 non-canary tests passing (+48 net since chapter 4.3 close; +13 across chapter 3.x); 0 skipped; 0 build warnings under `TreatWarningsAsErrors=true`. **Lint:** clean across 27 rules (zero new LINT-ALLOWs in chapter 3.x).

Chapter 3.x closes the **dev-tooling DACPAC artifact path** end-to-end: V2 Catalog → typed-AST stream → DacFx model → `.dacpac` bytes → Docker image → registry → `docker pull` + `docker run`. The operator's one-command stand-up requirement is structurally green; production deploy stays untouched on the SSDT-style file path. The Tier-3 `text-builder-as-first-instinct` Active deferral is cashed out — DacFx (`Microsoft.SqlServer.DacFx` v162.x) is in the codebase and active inside `Projection.Targets.SSDT`. **AXIOMS T1 binary-emitter amendment cashed** at chapter close: text emitters preserve byte-equality; binary emitters preserve content-equality via DacFx model round-trip; the unifying predicate `t1ByteEqualOrModelEquivalent` chooses per emitter kind.

### Slice arc α + β + γ + δ_dock (this chapter)

| # | Commit | Slice | What |
|---|---|---|---|
| 1 | `090f2d7` | α | DacpacEmitter v0 + chapter open + `Microsoft.SqlServer.DacFx` NuGet + 4 tests; Tier-3 hard-requirement deferral cashed out; DacFx integration deferral cashed out |
| 2 | `5985b40` | β + γ + δ_dock | FK round-trip; Indexes round-trip; **DockerImageEmitter** producing a typed `DockerImageContext { Dockerfile; DacpacBytes; EntrypointScript; Readme }` for one-command dev stand-up |
| 3 | (this commit) | close | CHAPTER_3_X_CLOSE.md (8-item ritual); AXIOMS T1 binary-emitter amendment cashed; three slices deferred-with-trigger (ε modality marks; ζ byte-determinism; per-Catalog parameterization) |

### Outstanding queue (post-chapter-3.x close → Chapter 5)

**Chapter 5 (Phase 8 pragmatic close) opens next.** Consumer-pressure-driven items per V2_DRIVER §252:

- **Slice ν — F# Analyzers SDK custom analyzer** (originally scoped at chapter 3.7). Complements 27 grep lint rules with AST detection.
- **Slice θ — Coordinates Stage 2 typed VOs** (`SchemaName` / `TableName` / `ColumnName`; originally scoped at chapter 3.7). DDD VO win when adapter ripple is acceptable.
- **Hex port lifts** (`IArtifactSink`, `IDeployHost`) — under genuine consumer demand.
- **Cutover-day operator runbook** — joint deliverable with solution architect.
- **V1 sunset planning** — after cutover+30 + one full schema-evolution cycle.

**Deferred-with-trigger at chapter 3.x close:**

- Slice ε — Modality marks → comments / extended properties (trigger: downstream consumer demands structured access to modality marks from the .dacpac model).
- Slice ζ — Byte-determinism cash-out via post-hoc Origin.xml canonicalization (trigger: snapshot consumer demands byte-stable dacpac artifacts).
- Per-Catalog parameterization of Dockerfile / entrypoint (trigger: second consumer with conflicting defaults).

**Quietly-deferred queue (no current consumer; surface at next chapter audit):**

- OSSYS adapter User-kind identification surface (chapter 4.2 close-deferred).
- CSV adapter for `ManualOverride` (UserMapLoader) (chapter 4.2 close-deferred).
- `Attribute.Default` field + DEFAULT constraint emission (chapter 4.1.A close-deferred).
- `Kind.Description` + `Attribute.Description` fields + extended-properties emission (chapter 4.1.A close-deferred).
- Statement DU MERGE/UPDATE promotion (chapter 4.1.B close-deferred; third-consumer trigger).
- Sort-vs-data deferral predicate distinction (chapter 4.1.B close codified discipline).
- Chapter 4.4 RemediationEmitter — V2_DRIVER §147 free-corollary table: "deferred under V2-driver KPI; revisit at chapter 5+ if remediation is operator-needed."
- Chapter 4.3 slices δ (CLI wire-up) + ε (V1 differential test).

---

## Chapter 3.x open + slices α + β + γ + δ_dock (preserved for reference)

**Branch:** `claude/chapter-4-ddd-improvements-XVCAM`. **Test baseline:** 1060 non-canary tests passing (+4 slice α; +2 slice β; +1 slice γ; +6 slice δ_dock = +13 in chapter 3.x; net +48 since chapter 4.3 close baseline); 0 skipped; 0 build warnings under `TreatWarningsAsErrors=true`. **Lint:** clean across 27 rules (DacFx adoption + Docker context emission both pillar-7 right moves; zero new LINT-ALLOWs in the chapter).

Chapter 3.x opens the **DacpacEmitter dev-tooling chapter** — reframing the pre-scope's deploy-path-conditional V2-driver KPI critical-path framing to a dev-tooling sibling-Π emitter per operator directive ("stand up a local copy of the database in no time flat — almost a one-click deploy strategy for my development team"). Production deploy path stays SSDT-style file deploy via `SsdtDdlEmitter.emitSlices`; DacpacEmitter ships the `.dacpac` artifact format the dev team consumes via `sqlpackage`, Visual Studio, or `DacServices.Deploy` to a local SQL Server.

**Slice δ_dock reframes pre-scope slice δ** (CLI `dac deploy` verb) → **DockerImageEmitter** per the operator's follow-up directive: "create a custom Docker package that stands itself up with the loaded SQL server inside of it ... single command up and my team doesn't have to have the repository to pull the data fresh each time." The emitter produces a Docker build context (Dockerfile + dacpac + entrypoint + README) that CI/CD builds into a registry-published image. Dev consumption is `docker pull` + `docker run` — no source checkout required.

**Three Active deferrals retired at chapter open + slice α:**

1. **DacFx integration in `Projection.Targets.SSDT.DacpacEmitter`** (Active deferrals row 214): cashed out — chapter ships under dev-tooling framing.
2. **`Microsoft.SqlServer.Dac` (DacFx) adoption Tier-3 hard-requirement** (Active deferrals row 223): cashed out — `Microsoft.SqlServer.DacFx` v162.x NuGet adopted in `Projection.Targets.SSDT.fsproj`. Pure F# wrapper (no C# subproject; pre-scope §6.2 bias yielded under empirical pressure — DacFx's V2-relevant surface is small, all `IDisposable`-aware calls F# handles via `use`).
3. **T1 amendment for binary emitters** — content-equality via DacFx round-trip (`Catalog → emit → DacPackage.Load → TSqlModel.GetObjects` enumeration matches across invocations), NOT byte-equality. DacFx embeds wall-clock timestamps in Origin.xml; the algebraic claim holds at the DacFx model level.

### Slice arc α + β + γ + δ_dock (this chapter to date)

| # | Slice | What |
|---|---|---|
| α | DacpacEmitter v0 + chapter open + `Microsoft.SqlServer.DacFx` NuGet + 4 tests (non-empty bytes; DacFx round-trip yields one Table per Kind; T1 content-determinism; T11 commutativity vs SsdtDdlEmitter on physical (Schema, Table) pair) |
| β | FK round-trip test — `sampleCatalog`'s Order→Customer FK ingests via DacFx + re-enumerates through `ForeignKeyConstraint.TypeClass` |
| γ | Indexes round-trip — `indexedCatalog` fixture (single-column unique + composite non-unique + single-column non-unique) ingests via DacFx + re-enumerates through `Index.TypeClass`; `Index.Unique` property preserved across the round-trip |
| δ_dock | **DockerImageEmitter** (reframes pre-scope slice δ per operator directive): emits a Docker build context `{ Dockerfile; DacpacBytes; EntrypointScript; Readme }` that CI builds into a self-contained `mcr.microsoft.com/mssql/server:2022-latest`-based image. Image bakes in the dacpac + installs `sqlpackage` at build; entrypoint starts SQL Server, polls until ready, publishes the dacpac. Dev team `docker pull` + `docker run` with no source checkout — "single command up." 6 tests (Dockerfile shape; entrypoint shape; README shape; embedded dacpac round-trips through DacFx; T1 byte-determinism on the static-template fields) |

**A18 amended preserved structurally** — both `DacpacEmitter.emit` and `DockerImageEmitter.emit` take `Catalog -> Result<...>` (Catalog only; no Policy parameter; Profile widening lands when a slice forces it). **T11 keyset coverage** holds across siblings (SsdtDdlEmitter directory bundle and DacpacEmitter model agree on the per-Kind (Schema, Table) set; DockerImageEmitter wraps the dacpac unchanged). **Pillar 7** holds end-to-end (Statement generation via SsdtDdlEmitter typed-AST stream; per-statement script via `ScriptDomGenerate.generateOne`; `.dacpac` serialization via DacFx `DacPackageExtensions.BuildPackage`; SQL Server image via Microsoft's canonical `mcr.microsoft.com/mssql/server`; DACPAC deploy via Microsoft's canonical `sqlpackage`).

### Outstanding queue (post-chapter-3.x slice δ_dock)

**Within chapter 3.x:**

- **Slice ε** — Modality marks → comments / extended properties.
- **Slice ζ** — Byte-determinism cash-out (post-hoc canonicalization). **Deferred-with-trigger** at chapter open: surface only when a snapshot consumer demands byte-stable dacpac artifacts.
- **Per-Catalog parameterization of the Dockerfile / entrypoint** — slice δ_dock ships pinned constants (database name = `ProjectionCatalog`; base image = `mcr.microsoft.com/mssql/server:2022-latest`). Per-Catalog overrides land when an operator workflow demands them (IR-grows-under-evidence).

**Now-unblocked (per V2-driver KPI sequencing + DacpacEmitter dev-tooling reframe):**

- **Chapter 4.4 RemediationEmitter** — schema-level partial-state recovery; composes over `CatalogDiff` + DacpacEmitter's typed model output. Inherits the dev-tooling framing per the chapter 4.3 close `2026-05-11 — Chapter 4.3 close + slices δ + ε deferred-with-trigger` entry.

---

## Chapter 4.3 close (added 2026-05-11; Operational Diagnostics V2 structural arc shipped; V2-driver KPI Phase 5 closed)

**Branch:** `claude/chapter-4-ddd-improvements-XVCAM`. **Test baseline:** 1012 non-canary tests passing + ~16 Docker-dependent canary tests; 0 skipped; 0 build warnings under `TreatWarningsAsErrors=true`. **Lint:** clean across 27 rules.

Chapter 4.3 closes the **operational-diagnostics axis** — the operator-facing surface of V2's diagnostic pipeline. Three sibling-Π emitters under `Projection.Targets.OperationalDiagnostics` route the existing `Diagnostics<'a>` writer's entries into three operator-vocabulary artifacts via a Code-prefix routing table. The work is **projection over substrate, not new algebra** — no new IR, no new pass shape, no parallel writer.

**The chapter-2 "three-channel Diagnostics split" Active deferral was retired at chapter 4.3 open** with the **refuse the split** decision: the three V1 artifacts ARE the three channels (decision-log = audit; opportunities = operator; validations = developer); routing happens at emit time via the Code-prefix table, not via a structural split of `Diagnostics<'a>`.

### Slice arc α + β + γ (this chapter)

| # | Commit | Slice | What |
|---|---|---|---|
| 1 | `bf3770b` | α | DecisionLogEmitter v0 + chapter-2 three-channel-split deferral retired + new `Projection.Targets.OperationalDiagnostics` project |
| 2 | `abe0040` | β + γ | `Routing` primitive + `OpportunitiesEmitter` + `ValidationsEmitter` + chapter-signature **Routing partition property** + R4 multi-environment promotion property test (independent forward-progress per V2_DRIVER.md) |

**A18 amended preserved structurally** — every emitter's signature is `Catalog × DiagnosticEntry list`; never Policy. **T11 keyset coverage** holds across all three siblings (every catalog kind keyed; empty `entries: []` when no diagnostics match). **Pillar 1** holds end-to-end (JsonNode typed seam at the Π port; strings emerge only at terminal `Utf8JsonWriter`).

### Outstanding queue (post-chapter-4.3)

**V2-driver KPI critical-path under V2_DRIVER.md sequencing — closed front-to-back for the unconditional path:**

- ✅ Chapter 4.1.A (production SSDT DDL emitter)
- ✅ Chapter 4.1.B (CDC-aware data triumvirate; KPI's highest-leverage chapter)
- ✅ Chapter 4.2 (User FK reflow; A32 cashed out)
- ✅ Chapter 4.3 (Operational Diagnostics V2; three-channel deferral retired)
- ✅ R4 multi-environment promotion property test (independent forward-progress)

**Remaining critical-path (deploy-path-conditional):**

- **Chapter 3.x DacpacEmitter** — DacFx adoption mandatory per Tier-3 codification. **Conditional on the cutover team's deploy-path choice**: SSDT-style file deploy (already covered by `SsdtDdlEmitter`) vs DACPAC + SqlPackage deploy (requires this chapter). Pre-scope: `CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md`. Active deferral entry at top of `DECISIONS.md`.
- **Chapter 4.4 RemediationEmitter** — schema-level partial-state recovery; composes over `CatalogDiff` + `DacpacEmitter`. **Sequenced after chapter 3.x DacpacEmitter** (inherits the deploy-path conditionality). Pre-scope: `CHAPTER_4_PRESCOPE_DIAGNOSTICS_AND_REMEDIATION.md` Part 2.

**Deferred-with-trigger at chapter 4.3 close (per the close-ritual discipline):**

- **Chapter 4.3 slice δ — CLI wire-up in `Projection.Pipeline`** — operator-UX integration; trigger: real cutover-day operator workflow consuming the three artifacts.
- **Chapter 4.3 slice ε — V1 differential test** — V1 envelope walk; trigger: V1's `OpportunityLogWriter` + `PolicyDecisionLogWriter` + `ValidationReport` writers stabilize as canonical reference shape.

**Independent forward-progress alternatives (no chapter open required):**

- (None substantive — R4 shipped this session; the cutover-ladder structural commitment is structurally encoded.)

**Quietly deferred (no current consumer; reframe at next chapter audit):**

- OSSYS adapter User-kind identification surface (chapter 4.2 close-deferred).
- CSV adapter for `ManualOverride` (UserMapLoader) (chapter 4.2 close-deferred).
- `Attribute.Default` field + DEFAULT constraint emission (chapter 4.1.A close-deferred; rowset-adapter trigger).
- `Kind.Description` + `Attribute.Description` fields + extended-properties emission (chapter 4.1.A close-deferred; rowset-adapter trigger).
- Statement DU MERGE/UPDATE promotion (chapter 4.1.B close-deferred; third-consumer trigger).
- Sort-vs-data deferral predicate distinction (chapter 4.1.B close codified discipline).
- Chapter-3.7 audit-cleanup slice queue (γ traverseCatalog / ζ attach-adapters / η Result-CE adoption / θ Coordinates Stage 2 / ι writer-monad codification / κ Lineage.tell perf audit / λ SsKey.rootOriginal V1 prefix / μ Restrict→NoActionSql Diagnostics / ν F# Analyzers SDK / ξ-π port lifts).

---

## Chapter 4.2 close (added 2026-05-11; User FK reflow shipped end-to-end; V2-driver KPI Phase 4 closed)

**Branch:** `claude/chapter-4-ddd-improvements-XVCAM`. **Test baseline:** 963 non-canary tests passing + ~16 Docker-dependent canary tests; 0 skipped; 0 build warnings under `TreatWarningsAsErrors=true`. **Lint:** clean across 27 rules.

Chapter 4.2 closes the **User FK reflow axis** — the V2-driver KPI's per-axis correctness depth for user-identity reflow across the four-environment cutover. Slice arc α → η shipped end-to-end on the branch:

| # | Commit | Slice | What |
|---|---|---|---|
| 1 | `17930c2` | α | UserMatchingStrategy DU + identity types (UserId/SourceUserId/TargetUserId/Email) + Policy 5th axis |
| 2 | `4678a76` | β + γ | UserPopulation in Profile + UserRemap.fs (UserRemapContext + RemapDiagnostic + smart constructor) |
| 3 | `d2a091d` | δ | UserFkReflowPass.discover (ByEmail real; others deferred-stub) |
| 4 | `a0e9807` | ε | Full strategy DU coverage (BySsKey / ManualOverride / FallbackToSystemUser; recursive composition; lazy indexes) |
| 5 | `693eb13` | ζ | Reference.IsUserFk : bool IR refinement (23 sites updated; closed-DU empirical-test held) |
| 6 | `08a75cf` | η | UserRemapContext wiring into MigrationDependenciesEmitter + multi-environment commutativity property |

**A32 cashed out at chapter 4.2 close** (per AXIOMS.md A32 cash-out body). The pass-produces-emitter-consumable-value pattern is now a wired template — `UserFkReflowPass.discover : ... -> Lineage<Diagnostics<UserRemapContext>>` produces; `MigrationDependenciesEmitter.emitWithUserRemap` consumes; the multi-environment commutativity property test specializes T4.

**Two new Active deferrals codified at this close:**

- **OSSYS adapter User-kind identification surface** — OSSYS adapter currently sets `IsUserFk = false` for every Reference; trigger: real OSSYS-source-V2-target reflow workflow with User-FK columns. Slice η emitter integration is structurally complete; gap is at adapter boundary only.
- **CSV adapter for `ManualOverride` (UserMapLoader)** — ManualOverride works via programmatic construction today; trigger: real operator workflow demands file-format pickup path. Mirrors chapter 4.1.B slice ε NDJSON-adapter deferral.

### Outstanding queue (post-chapter-4.2)

**Critical-path under V2-driver KPI** (per `V2_DRIVER.md`):

- **Chapter 4.3 — three-channel Diagnostics split** (DecisionLogEmitter / OpportunitiesEmitter / ValidationsEmitter). Pre-scope: `CHAPTER_4_PRESCOPE_DIAGNOSTICS_AND_REMEDIATION.md`. The substrate is already shipped (`Diagnostics<'a>` writer); this chapter is projection, not new algebra. **Natural next move** per V2_DRIVER.md sequencing.
- **Chapter 4.1.A slices 6 / 7 / 8** — cross-module FKs / identity + defaults / extended properties. Now-unblocked per chapter 3.2 SnapshotRowsets. Pre-scope: `CHAPTER_4_PRESCOPE_SSDT_DDL_EMITTER.md` §8.

**Hard-requirement Active deferrals (read at chapter open):**

- **Chapter 3.x DacpacEmitter** — MUST adopt `Microsoft.SqlServer.Dac` (DacFx). **Conditional on whether the cutover deploy path requires DACPAC** (product question).

**Independent forward-progress:**

- **R4 multi-environment promotion property test** — uses M4 Tolerance taxonomy `Set<ToleratedDivergence>`; ~150 LOC; chapter 4.2's multi-environment commutativity property is the worked precedent.

**Quietly deferred (no current consumer; reframe at next chapter audit):**

- **OSSYS adapter User-kind identification surface** (chapter 4.2 close-deferred; see DECISIONS entry).
- **CSV adapter for `ManualOverride` (UserMapLoader)** (chapter 4.2 close-deferred; see DECISIONS entry).
- **V1↔V2 differential test for UserFkReflowPass** (pre-scope §9; deferred pending V1 fixture canonicalization).
- **`SourceTag` value-object refactor of SsKey** (chapter 4.2 close-deferred per pre-scope's "what this chapter does NOT do" list).

---

## Chapter 4.1.B close (added 2026-05-11; CDC-aware data triumvirate fully closed end-to-end; V2-driver KPI Phase 3 highest-stakes deliverable shipped)

**Branch:** `claude/chapter-4-ddd-improvements-XVCAM`. **Test baseline:** 893 passing non-canary tests + ~16 Docker-dependent canary tests, 0 skipped, 0 build warnings under `TreatWarningsAsErrors=true`. **Lint:** clean across 27 rules. **Canary suite hang fix:** shipped (Docker-SqlServer xUnit collection + dedicated CdcSilence container).

Chapter 4.1.B closes the **CDC-aware data triumvirate** — the V2-driver KPI's highest-leverage chapter per `V2_DRIVER.md` per-axis correctness stakes table. Slice arc α → κ shipped end-to-end across two close arcs:

- **Slices α/β/γ** shipped at the joint chapter-4.1.A close arc (`CHAPTER_4_1_A_CLOSE.md`). Slice γ — CDC-silence canary GREEN under real SQL Server 2022 CDC — was the chapter signature deliverable.
- **Slices δ → κ** shipped this session arc on branch `claude/chapter-4-ddd-improvements-XVCAM`. See `CHAPTER_4_1_B_CLOSE.md` for the full slice-by-slice synthesis.

**The eight-item chapter-close ritual was operated** at this close (per `CHAPTER_4_1_B_CLOSE.md`); two new deferrals codified at the Active deferrals index (Statement DU MERGE/UPDATE promotion; sort-vs-data deferral predicate distinction).

### Slice arc δ → κ (this session)

| # | Commit | Slice | What |
|---|---|---|---|
| 1 | `23c9d76` | δ + topo v4 | Two-phase insertion / cycle-breaking + `TopologicalOrderPass` v3→v4 self-loop SCC detection + `Kind.tryFindAttribute` lift |
| 2 | `fafa8fd` | (canary fix) | `Docker-SqlServer` xUnit collection + dedicated CdcSilence ephemeral container — closes a canary-suite-hang bug |
| 3 | `44c4871` | η | DataEmissionComposer + EmissionPolicy.DataComposition DU + `StaticSeedsEmitter.emitWithTopo` (hoisted-topo) |
| 4 | `0aa3761` | ε | MigrationDependenciesEmitter (typed AST per Tier-3 hard-requirement Active deferral cash-out) |
| 5 | `9544006` | ζ + θ | BootstrapEmitter (structural stub) + `EmitError.OverlappingEmitterCoverage` + composer partition assertion |
| 6 | `340eb15` | ι + κ | `composeRendered` global Phase-1-then-Phase-2 ordering + `RenderedPhase1`/`RenderedPhase2` split + typed `DataInsertRow.Values : Map<Name, SqlLiteral>` (pillar 1 lift) |

**A18 amended holds structurally** for all three sibling-Π emitters (Static / Migration / Bootstrap) — none can type-check with a Policy parameter; only `DataEmissionComposer` reads `Policy.Emission.DataComposition`. **T11 keyset coverage** holds across all three siblings. **Pillar 1** strengthened at the row level (typed `SqlLiteral` flows through `DataInsertRow.Values`; raw strings emerge only at the absolute terminal `Sql160ScriptGenerator` boundary). **Pillar 7 Tier-3 hard-requirement Active deferrals** for chapter 4.1.B all cashed out.

### Outstanding queue (post-chapter-4.1.B)

**Critical-path under V2-driver KPI** (per `V2_DRIVER.md`):

- **Chapter 4.2 — `UserFkReflowPass` + `UserMatchingStrategy` + `SourceTag` refactor of SsKey.** Pre-scope: `CHAPTER_4_PRESCOPE_USERFK_REFLOW.md`. Plugs into `UserRemapContext` shape that slice ζ established + composer's `composeRenderedFull` pipeline-integration entry. **Natural next move.** Inherits chapter 3.2's `OssysOriginal` operational reachability for cross-version `V1Mapped` UUIDv5 derivation.
- **Chapter 4.3 — three-channel Diagnostics split** (DecisionLogEmitter / OpportunitiesEmitter / ValidationsEmitter). Pre-scope: `CHAPTER_4_PRESCOPE_DIAGNOSTICS_AND_REMEDIATION.md`. Activates Diagnostics writer's deferred channel-routing under real consumer pressure.
- **Chapter 4.1.A slices 6/7/8** (cross-module FKs / identity + defaults / extended properties). **Unblocked by chapter 3.2** (SnapshotRowsets) — IR widening surfaces via the rowset path's SsKey carriage + EspaceKind / IsSystemEntity activation.

**Hard-requirement Active deferrals (read at chapter open per Tier-3 codification):**

- **Chapter 3.x DacpacEmitter** — MUST adopt `Microsoft.SqlServer.Dac` (DacFx). Pre-scope at `CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md`. Active deferral entry at top of `DECISIONS.md`. **Conditional on whether the cutover deploy path requires DACPAC** (product question).

**Two new deferrals codified at chapter 4.1.B close** (read at chapter open):

- **Statement DU MERGE/UPDATE promotion** — third MERGE/UPDATE consumer triggers the cross-target lift (DacpacEmitter Phase-2 path / Faker / Profile-attached rows in chapter 4.3 are candidates).
- **Sort-vs-data deferral predicate distinction** — sibling-but-distinct cycle-question discipline; future emitter agents choose the predicate that fits their semantic question explicitly.

**Independent forward-progress** (no chapter open required):

- **R4 multi-environment promotion property test** — uses M4 Tolerance taxonomy `Set<ToleratedDivergence>`; ~150 LOC; concrete next slice.

**Quietly deferred** (no current consumer; reframe at next chapter audit):

- **Migration adapter (NDJSON / CSV pickup directory)** — chapter 4.1.B slice ε; deferred until real ingestion path consumer surfaces.
- **Bootstrap row sources** (system users / default policies / profile-attached rows) — chapter 4.1.B slice ζ; deferred until chapters 4.2/4.3 supply consumers.
- **Tolerance slice β** (quotient operator on PhysicalSchemaDiff). Slice α variants are about axes PhysicalSchemaDiff doesn't compare; reopen if a new variant lands that requires diff-filtering.
- **Outstanding chapter-3.7 audit-cleanup slice queue** (γ traverseCatalog / ζ attach-adapters / η Result-CE adoption / θ Coordinates Stage 2 / ι writer-monad codification / κ Lineage.tell perf audit / λ SsKey.rootOriginal V1 prefix / μ Restrict→NoActionSql Diagnostics / ν F# Analyzers SDK / ξ-π port lifts) — see chapter-3.7 prologue below for triggers.

---

## Chapter 3.2 close (added 2026-05-10; substantive close + JSON-projection-lossiness class structurally resolved)

**Branch:** `claude/review-ddl-exporter-zB3LF`. **Test baseline:** 882 passing non-canary tests + ~16 Docker-dependent canary tests, 0 skipped, 0 build warnings under `TreatWarningsAsErrors=true`. **Lint:** clean across 27 rules. **Perf-gate:** clean (adapter-only changes; no canary-affecting work).

Chapter 3.2 closes the **JSON-projection-lossiness class** structurally by adding the `SnapshotRowsets` variant of `SnapshotSource` end-to-end. Five substantive slices + post-close bug-fix arc:

- **Slice 1** (`6dab9cd`) — `SnapshotRowsets` DU variant + `RowsetBundle` carrier + `ModuleRow` / `KindRow` / `AttributeRow` records + `parseRowsetBundle` minimum. SsKey at all three levels.
- **Slice 2** (`0354727`) — Reference rowsets (`#RefResolved` ⊕ `#FkReality`). FK SsKey carriage; rule 16 same-module assumption tested under rowset path.
- **Slice 3** (`d5d1812`) — `EspaceKind` activation; `parseOriginFromRowset` three-way real refines rule 17 from JSON-path placeholder.
- **Slice 4** (`6eae21f`) — `IsSystemEntity` activation; new `ModalityMark.SystemOwned` variant. Third lossiness-class member resolved.
- **Slice 5** (`a74b904`) — Cross-source parity tests (JSON ↔ Rowset). Total-equality (no-Guids) + shape-equality (Guid-carrying). Closes the chapter.
- **Post-close bug fix** (`0336795`) — `propagateOrFallback` codification at two-consumer threshold; seven build-failure sites refactored uniformly across both translation paths. Underlying error codes (e.g., `adapter.osm.unmappedDeleteRule`) survive the build-level wrap instead of being swallowed under generic umbrellas.

**A1's JSON-projection-lossiness bound — operationally resolved.** Chapter 3.2 makes A1's `OssysOriginal` variant operationally reachable at the OSSYS-adapter boundary for the first time. AXIOMS.md A1 footer + four-variant amendment updated. See `CHAPTER_3_2_CLOSE.md` for the full substantive synthesis.

**`SnapshotRowsets` Active deferral cashed out** at `DECISIONS 2026-05-10 — Chapter 3.2 close`. One silent-trigger fire scanned + cashed; 16 other active deferrals untriggered.

### Outstanding queue (post-chapter-3.2)

**Critical-path under V2-driver KPI** (per `V2_DRIVER.md`):
- **Chapter 4.1.B slice δ** (two-phase insertion / cycle-breaking). CDC-silence-on-idempotent-redeploy property test is the V2-driver KPI's highest-leverage single deliverable.
- **Chapter 4.1.B slices ε/ζ** (MigrationDependencies + Bootstrap). `ScriptDomBuild.buildMergeStatement` adoption mandatory per Active deferrals row.
- **Chapter 3.x DacpacEmitter**. DacFx adoption mandatory per Active deferrals row.
- **Chapter 4.2 User FK reflow**. Inherits chapter 3.2's `OssysOriginal` operational reachability; cross-version `V1Mapped` UUIDv5 derivation lands here.

**Highest-priority deferred slice (cross-cutting):**
- **Cross-module FK IR refinement** (Active deferrals row). Trigger: fixture exercising cross-module FK. Chapter 3.2 fixtures were all same-module; rowset path is structurally ready for the extension.

**Independent forward-progress** (carried from prior outstanding queue; not chapter-3.2-affected):
- R4 multi-environment promotion property test (uses M4 Tolerance taxonomy).
- Chapter 3.7 audit-cleanup slice queue (γ / ζ / η / θ / ι / κ / λ / μ / ν / ξ / ο / π) — `ξ` (`ICatalogReader` port lift) can now use chapter 3.2's `SnapshotRowsets` as second-source-of-truth precedent.

**Chapter close ritual joint pass** still beneficial across 3.1 / 3.5 / 3.6 / 3.7 / 4.1.A / 4.1.B-in-flight if the next chapter wants to discharge documentation drift before opening new substantive work. Chapter 3.2's own close ritual executed (see `CHAPTER_3_2_CLOSE.md` "Chapter-close ritual execution" section).

---

## Chapter 4.1.A close arc + 4.1.B in-flight prologue (added 2026-05-10; substantive close + V2-driver KPI Phase 2 + Phase 3 highest-stakes deliverable shipped)

## Chapter 4.1.A close arc + 4.1.B in-flight prologue (added 2026-05-10; substantive close + V2-driver KPI Phase 2 + Phase 3 highest-stakes deliverable shipped)

**Branch:** `claude/review-ddl-exporter-ilV0k`. **Test baseline:** 840 passing non-canary tests + ~16 Docker-dependent canary tests (skip-if-no-Docker), 0 skipped, 0 build warnings under `TreatWarningsAsErrors=true`. **Lint:** clean across 27 rules. **Perf-gate:** clean.

This prologue covers a **bundled close arc**: chapter 4.1.A (V2-driver KPI Phase 2; SSDT DDL emitter; in-flight surface closed), chapter 4.1.B (V2-driver KPI Phase 3; CDC-aware data triumvirate; opened + slices α/β/γ shipped — γ is the V2_DRIVER.md highest-stakes deliverable), the **RawTextEmitter retirement arc** (the chapter-3-era one-big-string + raw-INSERT pre-cursor fully retired; -520 LOC), and the **Tier-1/2/3 typed-AST transitions** (six retired LINT-ALLOWs at the StaticSeedsEmitter MERGE site + four canonical typed surfaces shipped + new failure mode codified). Substantive deliverables shipped (load-bearing):

### Chapter 4.1.A — production SSDT DDL emitter (V2-driver KPI Phase 2)

In-flight surface closed; slices 6/7/8 gated on chapter 3.2 SnapshotRowsets.

- **`SsdtDdlEmitter.emitSlices : Emitter<SsdtFile>`** (`Projection.Targets.SSDT/SsdtDdlEmitter.fs`) — per-kind `.sql` files via ScriptDom typed AST + `Sql160ScriptGenerator`. Slices 1+2+3+4+5 cover CREATE TABLE + composite PKs + non-PK indexes + intra-module FKs. RelativePath via cross-platform-deterministic forward slashes (`Modules/<Module>/<Schema>.<Table>.sql`).
- **`SsdtDdlEmitter.statements : Catalog -> seq<Statement>`** — typed-stream surface for canary tests + `Render.toText` consumers. Topologically ordered via `TopologicalOrderPass.runWith SkipSelfEdges` (FK targets emit before referencers). Same algorithm RawTextEmitter used.
- **`ManifestEmitter.emit`** (slice 9) — `manifest.json` per V1 SsdtManifest schema; `Utf8JsonWriter` gold-standard library.
- **`SsdtBundle.compose`** (slice 10) — composition of `(ArtifactByKind<SsdtFile>, Manifest)` into `Map<RelativePath, string>`. F# core never touches the file system; downstream hosts (Pipeline / CLI) consume the map.
- **Slices 6 (cross-module FKs), 7 (identity + defaults), 8 (extended properties) gated** on chapter 3.2 SnapshotRowsets surfacing IR widening.

### Chapter 4.1.B — CDC-aware data triumvirate (V2-driver KPI Phase 3)

Opened with strategic-frame eight-axis discipline; slices α/β/γ shipped; δ-θ pending.

- **Chapter open** (`CHAPTER_4_1_B_OPEN.md`) — strategic-frame axes named per `DECISIONS 2026-05-15`. CDC-silence-on-idempotent-redeploy property test is the highest-leverage single deliverable per `V2_DRIVER.md` per-axis correctness stakes table.
- **Slice α — `StaticSeedsEmitter v0`** (`fd38908`). New `Projection.Targets.Data` project (sibling to Targets.SSDT / Json / Distributions). `DataInsertScript` + `DataInsertRow` typed value foundation. V1-shape MERGE per `StaticSeedSqlBuilder.cs:211-260`. T11 sibling-Π keyset coverage; T1 byte-determinism; A18 amended (Catalog × Profile, never Policy).
- **Slice β — `Profile.CdcAwareness` field + change-detection MERGE predicate** (`2d8210e`). The load-bearing semantic addition. Per-kind dispatch on `CdcAwareness.isEnabled`: CDC-enabled kinds emit the change-detection predicate (`Target.[c] <> Source.[c] OR (Target.[c] IS NULL AND Source.[c] IS NOT NULL) OR (Target.[c] IS NOT NULL AND Source.[c] IS NULL)` per non-key column, all OR-joined); CDC-disabled kinds keep V1's predicate-free WHEN MATCHED. CdcAwareness lives on Profile (A34 alignment), not Policy.
- **Slice γ — CDC-silence canary GREEN** (`cdcd953`). Operationally proves under real SQL Server 2022 CDC that V2's redeploy pipeline does not fire spurious CDC capture entries on identical-content redeploys. Two `[<Fact>]` tests in `CdcSilenceTests.fs` (skip-if-no-Docker gated): positive (post == baseline; 0 new CDC entries) + sensitivity (changed-content redeploy DOES fire CDC; proves the canary mechanism is real). `sys.sp_cdc_scan` Agent-less synchronous capture; `cdc.<schema>_<table>_CT` row count assertion. Empirical finding: SQL Server 2022's MERGE→CDC pipeline doesn't capture no-op UPDATEs even from V1-shape unconditional WHEN MATCHED — V2's predicate is defense-in-depth (correct under any SQL Server version), not the load-bearing fix in 2022 specifically.
- **Slice δ (two-phase insertion / cycle-breaking), ε (MigrationDependenciesEmitter), ζ (BootstrapEmitter), η (DataEmissionComposer + EmissionPolicy.DataComposition DU), θ (partition assertion) pending.** Slices ε/ζ have a **hard-requirement Active deferral** (Tier-3 codification): MUST adopt `ScriptDomBuild.buildMergeStatement` from slice α precedent.

### M4 Tolerance taxonomy slice α — typed equivalence-class definition

`af7b96c`. The R6 split-brain governance + cutover fallback ladder + R4 multi-environment promotion test all depend on this typed surface.

- **`Projection.Core.ToleratedDivergence`** — closed DU enumerating five empirically-grounded divergences (HeaderCommentsOmitted / PostDeployForeignKeysSplit / IndexesUnreflected / StaticPopulationsUnreflected / CommentMetadataUnreflected). Each variant has concrete canary or emitter evidence today.
- **`Tolerance = Set<ToleratedDivergence>`** — value object with smart-constructor encapsulation (`strict` / `permissive` / `withDivergence` / `tolerates` / `divergences` / `isStrict` / `ofSet`). `Set` encoding (over a flat-bool record) per pillar 1 + pillar 8: the `Tolerance` IS the equivalence-class definition; membership says "this divergence is accepted."
- **Closed-DU expansion empirical-test discipline applied**: `coverage` function + `allKnown` cardinality test catch incomplete extensions at compile time + runtime.
- **Slice β** (quotient operator on PhysicalSchemaDiff) **reframed as no-op-until-consumer-pressure**: the slice α variants are all about axes that PhysicalSchemaDiff doesn't compare anyway. Reopen if a new variant lands that requires diff-filtering.
- **R4 multi-environment promotion property test** — uses the `Set<ToleratedDivergence>` encoding; pending; concrete next slice.

### RawTextEmitter retirement arc — chapter-3-era pre-cursor fully retired

`e4936d5` + `d91067a` + `197b9e7`. Net: -520 LOC.

- **Slice 1** — `SsdtDdlEmitter.statements : Catalog -> seq<Statement>` typed-stream surface (the missing piece that unblocked migration).
- **Slice 2** — Migrate all 9 call sites: Pipeline.fs, Cli/Program.fs (runWideCanary), CanaryRoundTripTests (×4), GeneratorScaleTests (×2), ScriptDomRoundTripTests, JsonEmitterTests (×2), RichProfilingEndToEndTests, SiblingEmitterContractTests (×2), SsdtDdlEmitterTests (×2). Topological order preserved via `TopologicalOrderPass.runWith SkipSelfEdges`. Re-baseline of substring assertions that depended on RawTextEmitter's `Provenance` trailing-comment SsKey roots (V2-IR-internal; SsdtDdlEmitter doesn't emit them).
- **Slice 3** — Delete `RawTextEmitter.fs` + `RawTextEmitterTests.fs`. Pillar 8 win: action-shaped name retires; concept-shaped `SsdtDdlEmitter` (chapter 4.1.A) + `StaticSeedsEmitter` (chapter 4.1.B) remain.

### Tier-1 typed-AST transitions — pillar-1 / pillar-7 alignment across the Outputs seam

Four transitions shipped this session (chapter 4.1.A close arc).

- **#4 — `Projection.Core.SqlLiteral` typed expression module** (`08ca554`). The IR→SQL-literal projection lives in Core; closed DU with eight variants (NullLit / IntegerLit / DecimalLit / BooleanLit / TextLit / TemporalLit / GuidLit / BinaryLit) one per PrimitiveType + NULL sentinel. `ofRaw` + `toString` + `formatRaw` convenience. Both consumers (SSDT.Render + Data.StaticSeedsEmitter) flow through the typed middle layer.
- **#1 — MERGE → ScriptDom MergeStatement typed AST** (`bface9a`). `ScriptDomBuild.buildMergeStatement` (~150 LOC of typed-AST construction with `MergeBuildArgs` record + per-column predicate builders); `StaticSeedsEmitter.renderMerge` retired the StringBuilder construction. **6 LINT-ALLOWs retired** at the MERGE site (chapter 4.1.B slice α/β shipped with them; Tier-1 #1 is the cash-out). The change-detection predicate is now a typed `BooleanBinaryExpression(Or)` of `BooleanComparisonExpression(NotEqualToBrackets)` + `BooleanIsNullExpression` AST wrapped in `BooleanParenthesisExpression`.
- **#2 — `Compose.Outputs.Sql : string` → `SsdtBundle : Map<RelativePath, string>`** (`705e31d`). Production-shape per-table file map. Pipeline.write iterates the bundle; Deploy.runEphemeral consumes `Compose.aggregateSsdt` (the `\nGO\n`-joined per-.sql convenience). Chapter-3 single-blob retires.
- **#3 — `Compose.Outputs.Json + .Distributions : string` → `JsonNode`** (`22ecc59`). Typed at the Outputs seam; consumers query the typed tree. Chapter 3.7 slice ε's per-kind typed JsonNode lifted to the Outputs seam. Pillar 1 holds end-to-end across Pipeline composition.

### Tier-3 codification — text-builder-as-first-instinct discipline (third named failure mode)

`23d9d5d`. Substantive DECISIONS entry (~120 lines) + Active deferrals index entries + AGENTS.md + CLAUDE.md operating-disciplines table.

- **Named failure mode**: **text-builder-as-first-instinct** — the agent reaches for StringBuilder as the default for new emitters, then attaches LINT-ALLOWs once the lint surfaces. Each LINT-ALLOW is individually defensible per the substantive-rationale discipline; the AGGREGATE is the bug. Six LINT-ALLOWs at one MERGE site means the typed-AST migration was skipped at construction time. Sibling failure mode to **performance-of-compliance** (chapter 3.7 slice β'' — the LINT-ALLOW shaped like an audit trail without substance) and **domain-blind naming** (chapter 3.7 slice β''' — the name shaped like a placeholder for an absent domain concept).
- **4-step protocol**: (1) articulate the typed-AST library BEFORE the first StringBuilder; (2) cross-check the precedent emitters; (3) first draft uses the typed AST; (4) LINT-ALLOWs at terminal text boundaries only.
- **Two hard-requirement Active deferrals** added to the index for chapter open:
  - **Microsoft.SqlServer.Dac (DacFx) adoption in `Projection.Targets.SSDT.DacpacEmitter`** — chapter 3.x. Hard requirement: the .dacpac ZIP+XML format MUST flow through DacFx.
  - **MigrationDependenciesEmitter + BootstrapEmitter typed-AST adoption from slice α** — chapter 4.1.B slices ε/ζ. Hard requirement: `ScriptDomBuild.buildMergeStatement` precedent. The chapter-close ritual scans the Active deferrals table; future agents at chapter open MUST read these entries.

### Docker probe + verify-before-diagnose discipline (fourth named failure mode)

`6ec4a64` + `b56f558`.

- **`Deploy.Docker.ensureRunning` memoized**; `BringupBudgetMs` lowered 30s→5s. Worst-case suite cost when Docker is down: collapsed `N×30s` (~7.5 min for N=15 canary tests) → 5s (one probe, cached).
- **PreToolUse hook** `.claude/hooks/docker-probe.sh` auto-fires before infra-relevant Bash commands (matches `dotnet test` / `docker *` / `*canary*` / `*Canary*` / `*Testcontainers*` / `*sqlcmd*` / `*mssql*`); reports current Docker state + last session-start hook line via `additionalContext`. NEVER blocks; only informs.
- **Named failure mode**: **infrastructure-blame jumping** — the agent jumps to "X infrastructure is unavailable" without running the cheap verification probe. Codified in AGENTS.md (root) "Verify-before-diagnose for infrastructure" subsection.

### Outstanding queue (post-this-session)

**Highest-leverage:** chapter close ritual for 3.6 + 3.7 + 4.1.A + 4.1.B-in-flight + RawTextEmitter retirement + Tier 1/2/3 (eight items per CLAUDE.md operating-disciplines table; catches cross-cutting drift).

**Independent forward-progress:**
- R4 multi-environment promotion property test (uses M4 Tolerance taxonomy; ~150 LOC; no new chapter open).
- Chapter 3.2 SnapshotRowsets pre-scope review (unblocks 4.1.A slices 6/7/8 + 4.1.B downstream).
- Chapter 4.1.B slice δ (two-phase insertion / cycle-breaking).

**Hard-requirement Active deferrals (read at chapter open):**
- Chapter 3.x DacpacEmitter — DacFx adoption mandatory.
- Chapter 4.1.B slices ε/ζ (MigrationDeps + Bootstrap) — `ScriptDomBuild.buildMergeStatement` adoption mandatory.

**Chapter close ritual deferred for all of:** 3.6, 3.7, 4.1.A, 4.1.B-in-flight, RawTextEmitter retirement, Tier 1/2/3 transitions. Joint pass is the natural next move.

---

## Chapter 3.7 prologue (added 2026-05-10; in flight, audit-cleanup hygiene)

**Branch:** `claude/review-ddl-exporter-ilV0k`. **Test baseline:** 790 passing, 0 skipped, 0 build warnings under `TreatWarningsAsErrors=true`. **Lint:** clean across 27 rules (Rule 27 added this chapter; see below). **Perf-gate:** clean.

Chapter 3.7 is a **B&W audit-cleanup hygiene chapter** picking up Tier-1 / Tier-2 / Tier-3 audit findings still open at chapter-3.6 close. Substantive deliverables shipped (load-bearing):

- **Slice α — `Lineage.Trail [<CustomEquality>]` (A26 cash-out)**. Audit Tier-2 #12. `Lineage<'a>` projects equality through `Value` only; trails are metadata not in equality. `Lineage.byValue` / `Lineage.byValueAndTrail` helpers expose the explicit projections. Monad-laws property tests + operator tests strengthened to `byValueAndTrail`. Two new property/Fact tests cash out A26 directly. Pass / PassWithDiagnostics aliases inherit the `'output : equality` constraint. +30 LOC.
- **Slice β — `Projection.Core.SqlTypeCorrespondence` bounded context (Tier-1 #8 cash-out)**. The forward / inverse PrimitiveType ↔ SQL DDL vocabulary pair previously split across `Projection.Targets.SSDT.Render.columnSqlType` (forward) and `Projection.Adapters.Sql.ReadSide.mapSqlType` (inverse) is consolidated into one closed-DU dispatch surface in Core. Round-trip property + 25 InlineData theory + Fact + property test sweep the recognized SQL Server alias vocabulary. ReadSide.mapSqlType becomes a 1-line alias.
- **Slice β' — `Render.columnSqlType` through ScriptDom typed AST (pillar 7 cash-out)**. Slice-β shipped with four `String.Concat` LINT-ALLOWs in Render that named the boundary without naming the considered alternative — *performance-of-compliance* (the named failure mode; see below). Slice-β' lifted `ScriptDomBuild.dataTypeReference` from `private` to public, added `ScriptDomGenerate.generateDataType : DataTypeReference -> string`, made `Render.columnSqlType` delegate. Output byte-identical (790 tests still green); four LINT-ALLOWs retired; two private helpers retired (`sqlTypeWithLength`, `sqlDecimal`); one unused import retired (`open System.Globalization`). Per-column generator instantiation surfaces via bench label `scriptDom.generateDataType` (perf-gate clean).
- **Slice β'' — LINT-ALLOW substantive-rationale discipline codification**. `DECISIONS 2026-05-10` codifies the four-question analysis as the structural prerequisite for any per-line `LINT-ALLOW` marker on a string-composition / built-in-substitute site. Names the failure mode **performance-of-compliance** (a marker shaped like an audit trail without the substance). Updates: pillar 7 amendment in DECISIONS.md supreme operating discipline section; new operating-disciplines table row in CLAUDE.md; expanded LINT-ALLOW guidance in root AGENTS.md; new sub-bullet in KICKOFF.md supreme-discipline section; new decision tree "When you reach for a string-composition primitive" in PLAYBOOK.md; lint Rule 27 added (per-line concat-aversion LINT-ALLOW inventory + soft floor).
- **Slice ε — Json + Distributions Π typed per-kind JsonNode (audit Tier-1 #7; pillar 1 cash-out)**. `JsonEmitter.emitSlices : Emitter<JsonNode>` (was `Emitter<string>`); `DistributionsEmitter.emitSlices : EmitterWithProfile<JsonNode>` (was `EmitterWithProfile<string>`). Internal serialization path is BCL-typed end-to-end (`Utf8JsonWriter` → `MemoryStream` → `byte[]` → `JsonNode.Parse(ReadOnlySpan<byte>)`); no managed `string` materializes at the per-kind seam. The doc composer's prior `JsonNode.Parse(kindText)` re-parse retires; typed `JsonNode` writes through the indented document writer via `node.WriteTo(writer)`. Added 4 new contract tests in `SiblingEmitterContractTests.fs` (renamed from `T11TypeTheoremTests.fs` — see slice β''' below). T11 fully structural at BOTH axes (keyset + per-kind value type). 794 passing tests.
- **Slice β''' — Domain-first naming discipline codification (pillar 8)**. `DECISIONS 2026-05-10 — Domain-first naming and ubiquitous-language consistency` codifies the four-question domain-naming analysis as the structural prerequisite for any named type / function / file / module / test in V2. Names the failure mode **domain-blind naming** (a name shaped like a placeholder for the absent domain concept). No lint enforcement (heuristic syntactic checks misfire on legitimate uses; the discipline-document path catches what the heuristic can't). Updates: pillar 8 added to DECISIONS.md supreme operating discipline; new top-row in CLAUDE.md operating-disciplines table; pillar 8 added to root AGENTS.md supreme-discipline summary; new pillar 8 paragraph in KICKOFF.md supreme-discipline section; new decision tree "When you reach for a name" in PLAYBOOK.md (with worked-precedents table + worked-anti-patterns table). Worked rename: `T11TypeTheoremTests.fs` → `SiblingEmitterContractTests.fs` (concept-shaped name names what the file IS, not which theorem ID it cites).
- **Docker hook canary-readiness fix**. `session-start.sh` now writes a comprehensive subsystem-status line at end-of-hook (`session-start <READY|DEGRADED|FAIL> <utc> | dotnet=<v> | docker=<state> | image=<state> | warm=<state>`) so agents reading `$HOME/.claude-projection-hook-status` see the FULL canary-readiness picture, not just dotnet. The dotnet-only intermediate status line retired (a fresh agent reading "session-start OK" without seeing the comprehensive verdict mistook the dotnet OK for full canary readiness). Stable subsystem vocabulary (running / cached / ready / missing / failed / not-ready / skipped) makes the file greppable. AGENTS.md "Pre-flight & Alignment" documents the new format + recovery path (re-run `bash .claude/hooks/session-start.sh`; idempotent).
- **V2-driver as destination KPI codification (principal-PO sidebar)**. `V2_DRIVER.md` (new standalone canonical surface) codifies V2-driver as the project's north star, supersedes the implicit "V2-augmented as floor; V2-driver as aspirational" framing in `DECISIONS 2026-05-22 — R6`, AND absorbs the operative backlog (supersedes `BACKLOG.md` which is now a forwarding pointer). The KPI in one sentence: V2 reaches V2-driver mode for the cutover by being provably correct on every axis V2 owns (schema, data, identity, diagnostics, and any future sibling), with provable correctness defined as structural-type-level enforcement plus per-axis property tests. **CDC silence on idempotent redeploy (chapter 4.1.B) is the highest-leverage single deliverable.** Chapters 4.1.B / 4.2 / 4.3 / 3.x / 3.2 are critical-path under V2-driver KPI, not optional. Updates: new top-row in CLAUDE.md operating-disciplines table; new V2-driver paragraph in KICKOFF.md supreme-discipline section; KICKOFF.md strategic-surfaces table now points at `V2_DRIVER.md` (was `BACKLOG.md`); CLAUDE.md reading-order section + VISION.md companion-strategic-surfaces both updated to reference `V2_DRIVER.md`; new DECISIONS entry `2026-05-10 — V2-driver as destination KPI`. The `V2_DRIVER.md` "Executive backlog summary" table is the chapter-by-chapter sequencing under this KPI; per-chapter operational detail continues to live in `CHAPTER_*_PRESCOPE_*.md`.

**Chapter-3.7 slice queue** (in user-preferred order; ε now shipped per the chapter-4.1.A close arc):
- ✅ **α** — `Lineage.Trail [<CustomEquality>]` (A26 cash-out). Shipped at `1f8a617`.
- ✅ **β** — `Projection.Core.SqlTypeCorrespondence` bounded context (Tier-1 #8 cash-out). Shipped.
- ✅ **β'** — `Render.columnSqlType` through ScriptDom typed AST. Shipped.
- ✅ **β''** — LINT-ALLOW substantive-rationale discipline codification. Shipped.
- ✅ **ε** — Json + Distributions Π typed per-kind `JsonNode` (audit Tier-1 #7; T11 fully structural; pillar 1). **Shipped** at chapter-4.1.A close arc (per HANDOFF prologue line 105). T11 is now structural at both axes (keyset + per-kind value type).
- ✅ **β'''** — Domain-first naming discipline codification (pillar 8). Shipped.

**Outstanding chapter-3.7 slice queue:**
- **γ** — `traverseCatalog` natural-transformation primitive (audit Tier-3 #23; FP composition).
- **ζ** — Three `attach` adapters take string JSON → SnapshotSource-shaped (audit Tier-1 #6).
- **η** — `result {}` CE adoption at `ReadSide.fs:540-690` (audit Tier-3 #24).
- **θ** — Coordinates Stage 2 typed `SchemaName` / `TableName` / `ColumnName` VOs (audit Tier-3 #20a).
- **ι** — Lineage / Diagnostics writer-monad codification refresh (audit Tier-2 #18 + #19).
- **κ** — `Lineage.tell` `m.Trail @ [event]` O(N²) audit (perf-class question).
- **λ** — `SsKey.rootOriginal` V1 prefix in emitter output (audit Tier-1 #11; needs DECISIONS amendment first).
- **μ** — `Restrict→NoActionSql` Diagnostics scaffolding (audit Tier-1 #10 + Tier-2 #15).
- **ν** — F# Analyzers SDK custom analyzer (KICKOFF deferral #1; complements 27 grep rules with AST detection). **Note:** consolidates the historical chapter-3.6 slice χ "F# Analyzers SDK" deferral — same item, single queue location going forward.
- **ξ / ο / π** — Port lifts (`ICatalogReader` / `IArtifactSink` / `IDeployHost`); ξ stayed deferred at chapter 3.2 close per the variant-vs-source distinction (`CHAPTER_3_2_OPEN.md` axis 6); will fire when a true second catalog source (DACPAC / OData / in-memory) materializes — likely chapter 3.x DacpacEmitter open.

**Chapter close ritual: substantively discharged at chapter-4.1.A close arc + chapter-3 cross-cutting close (2026-05-10);** see `CHAPTER_4_1_A_CLOSE.md` (joint coverage of 3.6/3.7/4.1.A/4.1.B-α/β/γ) + `CHAPTER_3_2_CLOSE.md` (chapter 3.2 specific) + `CHAPTER_3_5_CLOSE.md` (chapter 3.5 retroactive close).

---

## Chapter 3.6 prologue (added 2026-05-09; substantive close, ritual deferred)

**Branch:** `claude/review-ddl-exporter-EH1lh`. **Test baseline:** 757 passing, 0 skipped, 0 build warnings under `TreatWarningsAsErrors=true`. **Lint:** clean across 26 rules. **Perf-gate:** clean against `bench/baseline-canary.json` baseline.

Chapter 3.6 closed five of the six chapter-3.5-close-deferred items (KICKOFF.md table) plus a comprehensive brittleness audit + library-API audit + Result migration. **Substantive deliverables shipped (load-bearing):**
- **Pillar 6 codified** (no V2-internal back-compat paths). Cashed out `SsKey.original` parser-shim, `SsKey.derived` aliasing forwarder, `LEGACY` source marker, CLI bare-positional-args back-compat. OSSYS adapter flows through `Module.create` / `Catalog.create`.
- **Pillar 7 codified** (gold-standard library precedence + perf-clause). Every refactor SHALL cite perf implications; every hot-path function has `Bench.scope`; every loop flows through `Bench` iterators; every counter via `Bench.recordSample`.
- **`LineageEvent.Removed of RemovalReason` + `Annotated of AnnotationDetail`**: typed-payload widening across 5 producer pass drivers + SymmetricClosure.
- **`SsKey.Synthesized of basisParts: string list`**: typed segments through the DU; `String.concat "_"` survives only at terminal `rootOriginal`.
- **`RawValueCodec`**: V2's canonical raw-value format contract; consolidates Render + Bulk + ReadSide.
- **`ConnectionString.parse`**: typed `SqlConnectionStringBuilder` validation.
- **`BatchSplitter` strategy**: `TSql160Parser` gold-standard with line-fold loud-fallback for `Deploy.executeBatch`.
- **`EmissionPolicy.create`**: A39 invariant.
- **`CatalogTraversal.mapKinds` primitive**: extracted from VisibilityMask + NormalizeStaticPopulations.
- **`BenchSink` port**: `Bench.persistJson` extracted from Core (audit Tier-1 #1).
- **Statistical perf-gate** (`scripts/perf-gate.sh`) + **pre-commit hook** + **Stop hook** (`hookSpecificOutput.additionalContext`): per-label `μ + Kσ` outlier detection across rolling history (N=20); soft-skip on missing Docker/dotnet.
- **`Result<'a>` aliased to `Microsoft.FSharp.Core.Result<'a, ValidationError list>`**: custom DU + `result {}` builder retired. **FsToolkit.ErrorHandling 4.18.0** adopted; `result {}` / `taskResult {}` / `validation {}` CEs now native. `DiagnosticSeverity` qualified.
- **ScriptDom expansion** at boundary sites (`createDatabaseSql`, `readRowsStream`, `readRows.SELECT COUNT`): single source of truth for SQL identifier quoting via `Identifier.EncodeIdentifier`.
- **Bench coverage at every pass entry** (10 passes; iterator-logging-as-first-class-outcome discipline).

**Sole 3.6-deferral remaining:**
- **Slice χ — F# Analyzers SDK custom analyzer** (KICKOFF deferral #1; standalone). **Consolidated with chapter-3.7 slice ν** at the chapter-3 cross-cutting close (2026-05-10) — same item, single queue location going forward (chapter-3.7 slice ν is the canonical reference). Re-open trigger: false-negative surfaces in CI.

**3.6 chapter-close ritual** discharged jointly at chapter-4.1.A close arc + chapter-3 cross-cutting close (2026-05-10). See `CHAPTER_4_1_A_CLOSE.md` "Chapter close ritual — eight items walked".

**Forward signals into 3.7+ / 4.x** (recorded for future agents at `DECISIONS.md` 2026-05-09 chapter-3.6 audit-findings entry): DacFx adoption (chapter 3.x DacpacEmitter — primitives named: `TSqlModel`, `DacPackage`, `DacServices.GenerateDeployScript`, `SchemaComparison`, `DacDeployOptions`), SqlBatch (when SqlClient ≥ 5.5 + canary bottleneck), `SqlConnection.RetryLogicProvider` (canary CI flake), `AsyncSeq` (when streaming readside needs `bufferByCountAndTime`), `JsonObject` typed per-kind (when 2nd `ArtifactByKind<string>` consumer fires), Argu CLI (when CLI grows beyond 3 commands), Verify.XUnit (DacpacEmitter golden-rotation pressure), `Microsoft.Extensions.Logging` (CI structured-logs consumer demand), `Utf8JsonReader` (bench surfaces JSON parse time at scale), incremental `validation {}` adoption at `CatalogReader.parseAttribute / parseKind` for ~80 LoC reduction + better error aggregation, incremental `taskResult {}` adoption at `Deploy.runWideCanaryWithLoader` for ~40 LoC reduction.

**Pillar 7 perf-clause practice**: agents SHALL cite perf implications in every commit message and SHALL identify the perf class before committing (zero / O(1) / O(N) / O(N log N) / O(N²) — with the scaling axis).

---

## Where you are

You have inherited three closed chapters (1, 2, 3.1) of the V2 sidecar. The most recent close — chapter 3.1, the canary chapter — accumulated through sessions 27 → 36. Chapter 3.1's substantive synthesis is at `CHAPTER_3_1_CLOSE.md`; the chapter's epistemic capstone is `AUDIT_2026_05_DDD_HEXAGONAL_FP.md` (the five-agent DDD/Hexagonal/FP audit).

The codebase builds; **all 713 tests pass (697 non-canary + 16 canary)**; the canary's round-trip property holds across the 300-table forcing-function fixture and at 500k rows on the bulk path (27s warm).

You are not starting from scratch. You are continuing a multi-chapter arc whose accumulated judgment is partly in the canonical documents (`AXIOMS.md`, `DECISIONS.md`, `ADMIRE.md`) and partly in `CHAPTER_1_CLOSE.md` / `CHAPTER_2_CLOSE.md` / `CHAPTER_3_1_CLOSE.md` / `AUDIT_2026_05_DDD_HEXAGONAL_FP.md` next to this letter.

## What to read, in order

1. **`CLAUDE.md`** — the navigation surface. Indexes the canonical documents and lists the operating disciplines. Start here.
2. **`HANDOFF.md`** (this file) — the bridge between what chapters 1+2+3.1 know and what you need to know.
3. **`CHAPTER_3_1_CLOSE.md`** — chapter-3.1 close synthesis. Sections of immediate relevance:
   - The chapter-3.1 arc summary (M1–M3 milestone sequence; forcing-function scaling; data plane + at-scale + audit).
   - The four meta-codifications (bench-driven optimization, stream-realization pattern, five-agent epistemic-tier audit, harmonization-via-parameterization).
   - The forward signals into chapter 3.2 / 3.5 / 4.1 / 4.2.
4. **`AUDIT_2026_05_DDD_HEXAGONAL_FP.md`** — the chapter-3.1 close audit. Tier 1 / 2 / 3 / 4 backlog organized by epistemic level (B&W vs SUBJ) and leverage (H/M/L). ~30 findings; 10 acted on at session 36; ~20 routed to named sub-chapters.
5. **`CHAPTER_2_CLOSE.md`** — chapter-2 close synthesis. Read for OSSYS adapter context (25 translation rules, three-class typology, V1-input-envelope walk).
6. **`CHAPTER_1_CLOSE.md`** — historical context. Some priorities resolved by chapter 2 / 3.1; disciplines and load-bearing commitments persist.
7. **`AXIOMS.md`** — the algebra. A1–A40 with V2 amendments appended. **Note:** A35 / A36 cashed at session 34; A37–A40 candidates added at chapter 3.1 close (Coordinates VO, aggregate constructors, harmonization-via-parameterization, writer-fidelity).
8. **`DECISIONS.md`** — chronological operating discipline. Long. Read the most recent ten entries first; chapter-3.1-close entries cluster at the bottom.
9. **`ADMIRE.md`** — V1↔V2 bridge. `EntityDependencySorter` advanced to (advanced — consumed via `TopologicalOrderPass.runWith`); `EntitySeedDeterminizer` (advanced — `StaticRow.Values` raw IR contract closes the loop).
10. **The code.** `Projection.sln`. Strategies in `src/Projection.Core/Strategies/`; passes in `src/Projection.Core/Passes/`; sibling Π emitters in `src/Projection.Targets.{SSDT,Json,Distributions}/`; F# adapters in `src/Projection.Adapters.{Sql,Osm}/`; the canary in `src/Projection.Pipeline/`. Chapter 3.1's substantive deliverables: `Statement.fs` / `Render.fs` / `RawTextEmitter.fs` (typed Π output); `Bulk.fs` / `Deploy.fs` (bulk realization); `AsyncStream.fs` / `ReadSide.fs:readRowsStream` (streaming readside); `Coordinates.fs` (`TableId` value object); `PhysicalSchema.fs` (four-axis fidelity surface).

## What's load-bearing

These commitments are not negotiable without explicit DECISIONS entries amending them. If you find yourself wanting to break one, write the amendment first.

- **F#-pure-core / no-I/O-in-Core.** `Projection.Core` has zero I/O. Audited clean (`CHAPTER_1_CLOSE.md §1.1`); confirmed across chapters 2, 3.1. Chapter 3.1 audit Agent 2 #1 flagged `Bench.persistJson` as an outstanding violation; ⏸ deferred (rolls forward as `BenchSink` port extraction).
- **A18 amended.** Π consumes whichever subset of `Catalog × Profile` it needs, but never `Policy`. Catalog and Profile are *evidence*; Policy is *intent*. If you reach for Policy from inside an emitter, you are in the wrong layer — the work belongs in a pass.
- **A35 (chapter-3.1 contribution).** Π's output is a deterministic *statement stream*, not a string. Realization layers (`Render.toText`, `Deploy.executeStream`) consume the stream and choose their emission form. Bulk-vs-incremental deploy is realization-layer policy invisible to Π.
- **A36 (chapter-3.1 contribution).** Bulk-vs-incremental is realization-layer policy. The algebra (A18, T1, T11) holds at the stream level; how a realization deploys is its own concern.
- **Strategy-layer codification (`DECISIONS 2026-05-11`).** Pure functions of IR fields; typed function-type seam (`StrategyEvaluator<'context, 'config, 'decision>`); structured rationale DUs; lineage events on actual decisions; module name advertises domain (`<Domain>Rules` suffix); total decisions with named skips.
- **`Composition.fanOut` for registered-intervention pass drivers.** All pass drivers delegate to it.
- **Closed-DU expansion empirical-test discipline.** Adding a DU variant should produce F# exhaustiveness errors only at match sites; no caller reshaping outside the variant's module.
- **Decimal as default for continuous statistical evidence.** T1 byte-determinism requires it.
- **Sibling-Π commutativity (T11).** Every Π's output should mention every catalog kind by SsKey root. **Note:** T11 is currently *aspirational* not structural — three Π's return `string`. Chapter 3.5's Π port realization makes T11 structural via the typed `ArtifactByKind<'element>` surface.
- **Pass return-type codification.** Passes return `Lineage<'output>` for decisions only; `Lineage<Diagnostics<'output>>` when producing decisions plus observer-relevant findings. Chapter 3.1's writer-fidelity codification adds: pass drivers MUST use `LineageDiagnostics.tellDiagnostics` / `Lineage.ofValueAndEvents` (the canonical primitives); manual record-building is forbidden.
- **Three-class typology for V1↔V2 translation findings (chapter-2 contribution).** Lossiness / boundary-discipline / alternative-IR-surface. Chapter 3.2 is a translation chapter — operate the typology.
- **Five-agent epistemic-tier audit protocol (chapter-3.1 contribution).** Multi-agent parallel audit at chapter close; convergence map as primary synthesis surface. Tier 1/2/3/4 backlog organizes findings by epistemic level + leverage.
- **Harmonization-via-parameterization (chapter-3.1 contribution).** When two implementations diverge on a single axis, parameterize the algorithm on that axis. Worked example: `SelfLoopPolicy` in `TopologicalOrderPass`.

## What's deferred but might fire under your work

The Active deferrals index at the top of `DECISIONS.md` is the canonical surface; chapter-mid-audits and chapter-close ritual scan it. If your work surfaces the cash-out trigger, log a DECISIONS entry — don't quietly resolve the deferral.

**Chapter-3.5-likely fires:**
- **Π port realization** — three emitters return `string`; `Emitter<'element>` declared but unrealized. Chapter 3.5 RefactorLog is the natural new consumer that earns the typed structured output. Cross-cutting blocker for T11 structural-type encoding.
- **`SsKey.rootOriginal` V1 prefix in emitter output** — needs a DECISIONS amendment first to supersede the chapter-3 pre-scope §3 commitment that the source-prefix form is the stable identifier.
- **`Restrict → NoActionSql` collapse explicit** — paired with a Diagnostics-emission scaffolding for `CatalogReader`. Chapter 3.5 RefactorLog needs this for round-trip-fidelity diff.

**Chapter-3.2-likely fires:**
- **`SnapshotRowsets` variant of `SnapshotSource`** — closes the JSON-projection-lossiness class. Pre-scope at `CHAPTER_3_PRESCOPE_SNAPSHOT_ROWSETS.md`.
- **`ICatalogReader` port** — Position B trigger has fired (two consumers: `Osm.CatalogReader.parse` + `Sql.ReadSide.read`). Chapter 3.2 lifts the surface.
- **Three `attach` adapters take `string` JSON** — should mirror `SnapshotSource` shape. Hidden ports.
- **Silent V1 drops without Diagnostics** — chapter 3.2 adds the Diagnostics-emission scaffolding to `CatalogReader`.

**Chapter-4.1-likely fires:**
- **Type-correspondence module** — owns the 5 inverse functions (`mapSqlType` ↔ `columnSqlType`; `formatRawValue` ↔ `formatSqlLiteral`; `parseRaw` + `clrType`). T1 byte-determinism currently rests on conventional inversion across 3 projects. Chapter 4.1's data triumvirate (StaticSeedsEmitter / MigrationDependenciesEmitter / BootstrapEmitter) is where this lift earns its place.
- **`Bulk` lives in `Pipeline`** but is structurally `Adapters.Sql` concern. Move at chapter 4.1 open.
- **`IDeployHost` port** — wraps Testcontainers + warm-conn + executeStream. Chapter 4.1's data emitters need this seam to swap between live SQL and ephemeral container.
- **Streaming digest cash-out** — `RowDigester` / `PhysicalRowDigest` shipped at chapter 3.1 as scaffolding; chapter 4.1's data triumvirate uses them at scale.

**Chapter-4.2-likely fires:**
- **Identity DU refactor** — `OssysOriginal` / `V1Mapped` parameterized on `SourceTag` value object. The `V1Mapped` variant is reserved for cross-source identity threading; today unreachable from production. Chapter 4.2's User FK reflow makes it reachable.

**Cross-cutting cleanup (any chapter):**
- **`Bench.persistJson` writes from Core** — `BenchSink` port; Bench out of Core.
- **`IArtifactSink` port** — `Compose.write` + `Bench.persistJson` reach `File.WriteAllText` directly.
- **`Lineage.Trail` `[<CustomEquality>]`** — A26 documented but not enforced (default `=` compares Trail).
- **Typed `SchemaName` / `TableName` / `ColumnName` VOs** — Stage 2 of Coordinates.
- **`traverseCatalog` natural-transformation primitive** — 4 consumers hand-rolling mutable `ResizeArray<LineageEvent>` traversals.
- **`result { ... }` computation expression** — `ReadSide.fs:540–690` chains 4–5 deep, beyond the codebase's "bearable three steps" mark.

**Lower-priority (watch for accidental fires):**
- **Composition primitives `fallback`, `accumulate`, `wrap`, `lift`** — zero current consumers each.
- **Strategy registry mechanism** — N=5 strategies; threshold N≥4–6 plus a real consumer demanding name-keyed lookup.
- **Three-channel Diagnostics split** — single channel sufficient at all chapter-2/3.1 consumers.
- **Faker emitter** — gates on third evidence type.

## What you should not do

The accumulated judgment from sessions 1–36 includes specific don'ts:

- **Don't strip "dead code" without checking docstrings.** `ForeignKeyRules.isIgnoreRule` always returns false; `ForeignKeyKeepReason.CrossCatalogBlocked` is reserved-unreachable; `Origin.ExternalDirect` is unreachable from OSSYS today (chapter 4 SnapshotRowsets / DACPAC may reach it). All intentional.
- **Don't delete the OSSYS adapter's deferred `SnapshotSource` variants.** `SnapshotRowsets` and `LiveOssysConnection` are reserved DU variants with explicit re-open triggers. They appear unused until chapter 3.2+; do not delete.
- **Don't treat `RawTextEmitter` as an SSDT replacement.** It is a debug/diff-oracle synthetic-milestone form. DacpacEmitter (chapter 3.x) is the additive sibling, not a replacement.
- **Don't extract speculative composition primitives.** Two-consumer threshold; refined by anticipation-vs-speculation (`DECISIONS 2026-05-13`); refined again by `opportunityEntry` shape-distinction analysis (`DECISIONS 2026-05-14`).
- **Don't reach for Policy from a Π.** A18 amended forbids it.
- **Don't bypass the writer's API.** `LineageDiagnostics.tellDiagnostics` / `Lineage.ofValueAndEvents` are the canonical pass-driver primitives. Manual record-building is forbidden.
- **Don't cash out Active deferrals silently.** The DacFx trigger fired silently across sessions 18–22 and was caught only at session-23 chapter-mid-audit. The lesson is structural: **the index exists so it doesn't recur**.
- **Don't open new substantive slices without classifying the finding into the three-class typology first.** Trace-before-fixture; classify; resolution shape follows.
- **Don't build ports without consumer pressure.** The session-36 audit identified ports declared in Core (`Emitter`, `Compare`, `Render`) that are unrealized. The fix isn't to add more declarations — it's either to *realize* with a real consumer (chapter 3.5 Π port) or to *retire* the declaration (the `Adapter` alias retired at session 36 with no consumer). Closed-DU expansion empirical-test discipline applies to ports too.
- **Don't overwrite this file.** When chapter 3.2 / 3.5 / 4.x closes, this letter becomes `HANDOFF_CHAPTER_3_1.md` and you write the new outgoing letter as `HANDOFF.md`. Append-only documentation discipline.

## Disposition

The dispositions chapter 3.1 inherited from chapters 1–2 hold; chapter 3.1 added these:

- **Bench-driven optimization protocol.** Three-candidate / 2-refuted / 1-confirmed shape; refuted swaps documented with bench data so the same swap doesn't recur.
- **Stream-realization pattern.** Π's typed output stream + realization layers as sibling consumers. Same algebra; multiple realizations.
- **Five-agent epistemic-tier audit at chapter close.** Multi-agent parallel; convergence-map as primary surface; Tier 1/2/3/4 backlog discipline.
- **Harmonization-via-parameterization.** Single-axis-divergent implementations earn one parameterized algorithm.
- **Writer-fidelity discipline.** `LineageDiagnostics.tellDiagnostics` / `Lineage.ofValueAndEvents` canonical; manual record-building forbidden.
- **Audits roll forward as named sub-chapters.** Chapter 3.1's audit (Tier-1 / Tier-2 / Tier-3) didn't dump 30 findings into the next chapter — it routed each finding to the natural sub-chapter (3.2 / 3.5 / 4.1 / 4.2) with explicit pre-scope alignment. The discipline: audit findings are routed, not piled.

## Where to start

Per `CHAPTER_3_1_CLOSE.md`, the chapter-3.2 / 3.5 / 4.1 priorities split based on what unlocks T-30 first (per `DECISIONS 2026-05-22 — T-30 / T-15 cutover fallback ladder gates`):

1. **Read this letter, CLAUDE.md, CHAPTER_3_1_CLOSE.md, AUDIT_2026_05_DDD_HEXAGONAL_FP.md, and the recent DECISIONS entries** — orient. ~45 minutes.

2. **Decide chapter sequencing.** The four plausible next chapters:
   - **Chapter 3.2 — `SnapshotRowsets` adapter.** Closes the JSON-projection-lossiness class. Smaller scope. Pre-scope at `CHAPTER_3_PRESCOPE_SNAPSHOT_ROWSETS.md`. Lifts `ICatalogReader` port (Position B trigger fired).
   - **Chapter 3.5 — Π port realization + RefactorLog / CatalogDiff.** Largest leverage. Realizes the declared `Emitter<'element>` shape; unblocks T11 structural-type encoding. Pre-scope at `CHAPTER_3_PRESCOPE_REFACTORLOG_AND_CATALOG_DIFF.md`. Pairs naturally with the audit-deferred Π-port-realization.
   - **Chapter 3.x — DacpacEmitter.** Re-deferred at chapter-2 close. Inherits chapter-3.5's structured-output pattern. Pre-scope at `CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md`. Eight risks/open-questions still open.
   - **Chapter 4.1 — Data triumvirate.** Pre-scope at `CHAPTER_4_PRESCOPE_DATA_TRIUMVIRATE.md`. Inherits `Bulk` / `RowDigester` / `AsyncStream` from chapter 3.1.

   The pre-scope documents are still current. Subagent #4's (DacpacEmitter) and subagent #5's (SnapshotRowsets) recommendations from chapter-2 close hold; the chapter-3.1 audit sharpens them with the Π-port-realization framing.

3. **Open the chapter you choose** with a chapter-open document naming the strategic-frame axes (`DECISIONS 2026-05-15` shape; the OSSYS chapter is the worked example). Multi-session chapters earn this discipline at chapter open.

After the chapter-open scoping, the substantive work begins. Operate the chapter-mid-audit at every 3–5 substantive sessions; operate the chapter-close ritual at chapter close (eight items, including the V1-input-envelope walk for V1↔V2 translation chapters and the new five-agent audit for architectural-frame chapters).

## Closing

You inherit a codebase whose architectural disciplines hold under audit (the chapter-3.1 audit was the strongest one yet — five agents, ~30 findings, codified as `AUDIT_2026_05_DDD_HEXAGONAL_FP.md`), whose codification is at multiple stability marks, whose audit disciplines have proven generative (each audit produces new disciplines), and whose canonical documents are honest about what's been done and what hasn't.

Chapter 3.1's distinctive intellectual artifact: **the canary as a load-bearing forcing function**. The 300-table fixture is the verification surface for the V1↔V2 round-trip property. Chapter 3.1's distinctive operational artifact: **typed Π output as a stream, with realization plurality**. Chapter 3.1's load-bearing structural innovation: **harmonization-via-parameterization** — single-axis-divergent implementations earn one parameterized algorithm.

The chapter you open is yours to shape. The disciplines above are not constraints; they are the load-bearing structure that lets the chapter ahead support more weight than the one behind. Hold the spine.

— The session 27–36 architect.
