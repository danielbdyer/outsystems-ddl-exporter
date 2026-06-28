# Refactor Opportunities

A consolidated, deduplicated inventory of refactor opportunities discovered by a
fan-out analysis of the `src/` tree (8 parallel analyst passes, June 2026). Each
pass took a distinct lens (cross-codebase duplication, per-project collapse,
domain/Result boilerplate, naming/dead-code). Findings that surfaced from
multiple independent passes are flagged **[cross-validated]** — those carry the
highest confidence.

This document is the tracking checklist. Check items off as they land. LOC
figures are estimated net deletions; "Effort" is implementation cost, "Risk" is
behavioral-change risk.

---

## Top-line health

The codebase is architecturally healthy: clean layering (no dependency-direction
breaches between Domain → Json → Validation → SMO/DMM → Pipeline → CLI), records
throughout, a real functional `Result<T>`, and several genuinely well-factored
spots (the `NullabilitySignal` rule tree; the table-driven nullability/unique
policy matrices). The debt is concentrated and mostly mechanical. Total
defensible reduction across all findings: **~3,000–4,000 LOC**, plus a cluster of
clarity/discipline fixes.

---

## Phase 0 — Quick correctness & drift hazards (Low risk)

- [x] **0.1 🐛 Latent bug:** `ProfilingQueryExecutor.cs:98-99` calls
  `BuildForeignKeyStatusDictionary` twice with *identical arguments*.
  **Investigated:** not an observable bug — the consumer (`SqlDataProfiler.cs:323-327`)
  uses `ForeignKeyNoCheckStatuses` only as a fallback for keys absent from
  `ForeignKeyStatuses`, which never happens since both are keyed by `plan.ForeignKeys`.
  Fixed by computing once and reusing (zero behavior change) + clarifying comment.
  **[cross-validated: Duplication + Pipeline]**
- [x] **0.2 SQL identifier quoting** — centralized the `[name]`/`]]`-escaping rule in a
  new `Osm.Domain/Sql/SqlIdentifier` (`Quote`/`Qualify`). Routed all 7 sites through it:
  `SqlIdentifierFormatter` (Emission), `IdentifierFormatter` (Smo bracket branch),
  `RemediationQueryBuilder` + `TighteningOpportunitiesAnalyzer` (Validation),
  `SqlDynamicEntityDataProvider` + `StaticEntityDataProviders` + `UatUsers/SqlFormatting`
  (Pipeline). **[cross-validated: Duplication + SMO]**
- [~] **0.3 Magic-string constants** — **deferred into Phase 4.** Audit showed the
  `"manifest.json"` sites are a same-string/different-concept trap (SSDT output vs
  evidence-cache vs profile vs run manifest — distinct files that merely share a name);
  collapsing them into one const would be misleading. `"dbo"` (~24 sites) needs
  per-site "is this genuinely the default schema?" verification. Verb-name dedup
  (consts already exist as `*Verb.VerbName`) is folded into Phase 4.11, which rewrites
  the very CLI factories that re-hardcode them.
- [x] **0.4 UAT artifact filenames** — added `UatUsersArtifactNames` (directory +
  filenames + verification report) and routed all emit/read/manifest sites through it
  (`UatUsersArtifacts`, the 3 pipeline steps, `FullExportRunManifest`, `FullExportVerb`,
  `FullExportPipeline`, `UatUsersPipelineRunner`, `UatUsersVerifier`, CLI `UatUsersCommand`).
  ⚠️ **Flagged separately:** `UatUsersVerifier.cs:52` reads `03_user_fk_catalog.json`
  while the emit side writes `03_catalog.txt` — a genuine name mismatch left as a literal
  pending intent confirmation (see Open Questions). **[cross-validated: Pipeline + Duplication + CLI]**

## Phase 1 — Additive `Result<T>` combinator layer (Low risk, high leverage)

**[cross-validated: JSON + Domain + Duplication]** — the single highest-leverage
lever. `if (x.IsFailure) return Result<T>.Failure(x.Errors)` appears **266× across
83 files**. Add combinators (zero behavior change), then migrate call sites.

- [x] **1.1** Added to `Result.cs` / `ResultTaskExtensions.cs`: `Combine` (2/3/4-arity,
  error-accumulating), `Traverse` + index-aware overload, `Match`, `Tap`/`TapError`,
  `MapErrors`, and async `EnsureAsync`/`MatchAsync`/`TapAsync`. Covered by
  `ResultCombinatorTests`. (Phase 1a)
- [x] **1.2** Added `Result<T>.Failure(string code, string message)`. (Phase 1a)
- [~] **1.3** Config `Create` migrations: `NullabilityOverrideRule.Create` and
  `ModuleValidationOverrideDefinition.Create` collapsed via `Result.Combine`.
  **Intentionally not migrated:** `NamingOverrideRule.Create` (conditional/optional-field
  validation + cross-field checks — not a `Combine` shape; mechanical collapse would risk
  behavior change) and `EntityOverrideDefinition.Create` (stateful accumulation loop).
  `EntityDocumentMapper.cs:55-176` and `TighteningOptionsDeserializer` ladders are
  **enabled** by the new API but left for a focused follow-up (bespoke per-field path
  decoration; high churn, lower mechanical confidence).
- [x] **1.4** Added `DocumentMapperContext.MapArray<TDoc,TModel>` (lenient skeleton:
  empty-on-null, skip null elements, indexed path, short-circuit). Migrated the lenient
  mappers: Trigger, Sequence, Relationship, ExtendedProperty. (Index ×2 and the
  required-collection Attribute mapper deferred — Index has nested sub-arrays and Attribute
  uses fail-on-null semantics that need a variant.)
- [x] **1.5** Added `ResolveCoordinate` + `ResolveForeignKeyCoordinate` in
  `ProfileSnapshotDeserializer`; migrated `MapColumn`, `MapUniqueCandidate`, and the
  6-block `MapForeignKey` prologue. (Composite-unique left as-is — 2-part, column-less.)

## Phase 2 — Dead code removal (Low–Med risk)

- [ ] **2.1** Entire legacy "opportunity per evaluator" subsystem (~350-450 LOC):
  `OpportunityBuilder`, evaluator `Analyze(…, ColumnAnalysisBuilder)` overloads,
  `ColumnAnalysis.Opportunities`, legacy `ReportSummary`/`OpportunityMetrics` —
  fully constructed, never read (superseded by `TighteningOpportunitiesAnalyzer`).
  Verify analyze-command summary numbers are unaffected.
- [ ] **2.2** FK declarative matrix (`TighteningPolicyMatrix.ForeignKeys`) referenced
  only by tests while production hand-rolls the same logic in `ForeignKeyEvaluator`.
  Reconcile to one source of truth (prefer making the evaluator consume the matrix).
- [ ] **2.3** `ProfileSnapshotDebugFormatter.cs` (~99 LOC, tests-only) — remove or
  relocate to test support.
- [ ] **2.4** Local `Result<T>` reinvented in `PolicyCommandFactory.cs:1046` — delete,
  use the domain `Result<T>`.
- [ ] **2.5** Two unused summary builders in `PolicyDecisionSummaryFormatter.cs:346-392`
  (`BuildNullContradictionSummary`, `BuildOrphanSummary`). ~47 LOC.
- [ ] **2.6** ⚠️ **TRAP — do NOT delete:** `ScripterExtensions.Dispose` looks dead to
  grep (zero textual refs) but is load-bearing via extension-method binding in
  `SmoContext.cs:95`. Make the call explicit instead of removing it.

## Phase 3 — BuildSsdt state-record collapse (Med risk, biggest LOC win)

**[cross-validated: Pipeline + Duplication]** — ~1,400-1,600 LOC, the largest
single structural sink.

- [ ] **3.1** `BuildSsdtPipelineStates.cs` is a 12-record linear inheritance chain;
  each record re-declares the entire cumulative field list (`TelemetryPackaged`
  re-lists ~30 properties / 29-arg base call). Collapse to one `BuildSsdtState`
  record with invariants + per-stage output sub-records.
- [ ] **3.2** Each `BuildSsdt*Step` then rebuilds full state with 15-25-arg
  constructor calls, often twice (skip + success path). Convert to `state with { … }`
  updates. (Tied to 3.1.)

## Phase 4 — Discipline fixes & remaining collapses (Med risk; golden-file diffs)

### Discipline / guardrail compliance
- [ ] **4.1** No-string-concat violation: `CreateTableStatementBuilder.cs:262-282`
  hand-builds `$"ALTER TABLE … WITH NOCHECK ADD CONSTRAINT …"` (standing `// TODO`).
  Route through ScriptDom `AlterTableAddTableElementStatement` +
  `ForeignKeyConstraintDefinition`.
- [ ] **4.2** Text-surgery formatters re-parse already-rendered DDL by splitting on
  `"FOREIGN KEY"`/`"DEFAULT"`/`"CONSTRAINT"` strings
  (`CreateTableFormatter.cs`, `ConstraintFormatter.cs`). Drive formatting from the AST
  via `SqlScriptGeneratorOptions`, or at minimum collapse the shared line-walk.
- [ ] **4.3** `ExtendedPropertyScriptBuilder.cs:82-136` builds `sp_addextendedproperty`
  via 3 near-identical raw templates — parameterize or emit via AST.
- [ ] **4.4** Physical SQL leaking into the pure decision layer:
  `TighteningOpportunitiesAnalyzer.cs:340-723` emits `ALTER TABLE`/`CREATE UNIQUE INDEX`
  T-SQL — relocate statement building to emission.

### Naming collisions (same name, different concept)
- [ ] **4.5** `EntityEmissionSnapshot` ×2 (`Model/Emission/` vs `Model/Artifacts/`).
- [ ] **4.6** `OpportunitiesReport` ×2 (`Tightening/` vs `Tightening/Opportunities/`).
- [ ] **4.7** `CacheMetadataBuilder` ×2 (`Application/` vs `Evidence/`) — likely merge.
- [ ] **4.8** `ITighteningAnalyzer` ×2 — consolidate or rename one.

### Remaining structural collapses
- [ ] **4.9** Duplicate ProfileSnapshot DTO family defined twice (serializer vs
  deserializer, ~170 LOC) — share one internal DTO file. **[cross-validated]**
- [ ] **4.10** Profiling SQL command/reader scaffold repeated ~6×
  (`ProfilingQueryExecutor`, `TableMetadataLoader`) — `ReadDictionaryAsync` helper +
  `ApplyCommandTimeout`/`ComputeSha256`/`CreateSqlMetadataOptions` dedup. ~90-130 LOC.
  **[cross-validated]**
- [ ] **4.11** 5 command factories bypass `PipelineCommandFactory` (Analyze, Inspect,
  VerifyData, Policy, UatUsers) — re-hand-roll scope/error/exit-code plumbing.
  `SplitTableIdentifier` copy-pasted verbatim (78 LOC) between two UAT files.
- [ ] **4.12** 9 single-string value objects + 4 `IComparer` null-preambles +
  duplicate-detection helpers across aggregates — template/share.
- [ ] **4.13** Single-impl interface cluster (~150-300 LOC): `IPathCanonicalizer`,
  several `Json/Deserialization` validators/factories with no test double — inline.
- [ ] **4.14** `[Obsolete] EmitBareTableOnly` shim never retired, now load-bearing at
  10+ sites — replace with `EmitTableMode == TableEmissionMode.BareOnly`, delete shim.
- [ ] **4.15** `SupplementalLoader` pure forwarding wrapper — inline into
  `PipelineBootstrapper`, delete (~40 LOC).
- [ ] **4.16** Options/resolver/binder sprawl: collapse the SQL options quartet
  (`SqlConnectionOptions`/`SqlProfilerOptions`/`SqlProfilerLimits`/`SqlSamplingOptions`)
  and the module-filter concept (modeled 3+ ways). ~200-400 LOC. (Largest of phase 4.)
- [ ] **4.17** Domain-blind naming migration: `*ApplicationService` → verb-concept,
  `*ResultSetProcessor` → `*RowReader`/`*Mapping`, `UserMatchingConfigurationHelper`,
  `LegacyPolicyAdapter`. Incremental, LOC-neutral, clarity-positive.

---

## Recommended sequencing

| Phase | Risk | Payoff |
|---|---|---|
| 0. Quick correctness | Low | Bug + drift hazards gone |
| 1. Additive `Result` API | Low | ~300-450 LOC + unlocks later phases |
| 2. Dead-code removal | Low-Med | ~500-700 LOC |
| 3. BuildSsdt state collapse | Med | ~1,400 LOC |
| 4. Discipline + remaining | Med | clarity + guardrail compliance + ~600 LOC |

Phases 0–1 are safe, high-ROI, and independent. Phase 3 is the largest LOC win
but touches the orchestration spine (well covered by tests, so verify with the
pipeline suite). Phase 4's golden-file-sensitive items (4.1–4.4) need
golden-output diffing before/after.

## Open questions (need product/intent confirmation)

- **UAT catalog filename mismatch:** `UatUsersVerifier.cs:52` reads
  `03_user_fk_catalog.json`, but `DiscoverUserFkCatalogStep` emits `03_catalog.txt`.
  Either the verifier is looking for a file that is never produced (dead/ineffective
  check) or there is a second catalog artifact that should exist. Left as a literal
  until the intended contract is confirmed; do not "fix" by guessing.

## Pre-existing test failures (not introduced by this work)

- `Osm.Emission.Tests.PhasedDynamicEntityInsertGeneratorTests.Generate_MultiNodeCycle_WithMultipleNullableEdges`
  and `…NullsOnlyNullableEdges` fail on a clean tree (verified by stash + clean rebuild).
  Cycle/edge phase-counting in `PhasedDynamicEntityInsertGenerator`; unrelated to the
  refactor. Tracked here so phase test runs aren't mistaken for regressions.

## Verification notes

- Full `dotnet test` runs against a warm SQL container and exceeds CI time limits;
  run scoped per-project test suites for the projects each change touches.
- Golden-file/snapshot tests guard SMO emission output — phase 4 DDL changes must
  diff clean (or the goldens be intentionally regenerated and reviewed).
