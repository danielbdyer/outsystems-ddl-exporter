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

- [x] **2.1** Removed the legacy "opportunity per evaluator" subsystem. **Verified first**
  (read-only trace) that the `Analyze(…, ColumnAnalysisBuilder)` overloads +
  `ITighteningAnalyzer` + `ColumnDecisionAggregator` are **live** (they produce the
  nullability/FK decisions the canonical pipeline depends on) — so those were collapsed to
  decision-only, NOT deleted. Deleted: `OpportunitiesReport`, `ReportSummary`,
  `OpportunityMetrics`, `OpportunityBuilder`, `PolicyAnalysisResult`, `ColumnAnalysis`.
  Slimmed: `ColumnAnalysisBuilder` (dropped opportunity members + `Build`),
  `TighteningPolicy` (`Analyze`→folded into `Decide`, report dropped),
  `UniqueIndexDecisionOrchestrator` (dropped `OpportunityBuilder`), both evaluators
  (`Analyze` collapsed). Removed 3 now-obsolete tests + `OpportunityBuilderTests`.
  All analyze/policy/orchestration suites green; CLI opportunity numbers come from the
  canonical `TighteningOpportunitiesAnalyzer` and are unaffected.
- [~] **2.2 Deferred (not a safe deletion).** `TighteningPolicyMatrix.ForeignKeys` has no
  production reader, but it is an **executable spec** validated by `TighteningPolicyMatrixTests`
  — deleting it removes documented coverage of the intended FK policy. The alternative
  (wire `ForeignKeyEvaluator` to consume the matrix) is a behavior-affecting refactor with
  the subtlest interactions in the policy engine (`ScriptWithNoCheck`, cross-db overrides);
  it deserves its own focused, separately-verified change rather than being bundled here.
- [x] **2.3** Deleted `ProfileSnapshotDebugFormatter.cs` (production-dead; tests-only) and
  its test; repointed `DotNetCli` assembly anchor to the public `SpectreProgressRunner`.
- [x] **2.4** Deleted the local `Result<T>` in `PolicyCommandFactory`; `BuildSeverityFilter`
  now returns the domain `Result<T>` (`Failure(code, message)`).
- [x] **2.5** Deleted the two unused summary builders in `PolicyDecisionSummaryFormatter`
  (`BuildNullContradictionSummary`, `BuildOrphanSummary`).
- [ ] **2.6** ⚠️ **TRAP — do NOT delete:** `ScripterExtensions.Dispose` looks dead to
  grep (zero textual refs) but is load-bearing via extension-method binding in
  `SmoContext.cs:95`. Make the call explicit instead of removing it. (Deferred to Phase 4.)

## Phase 3 — BuildSsdt state-record collapse (Med risk, biggest LOC win)

**[cross-validated: Pipeline + Duplication]** — ~1,400-1,600 LOC, the largest
single structural sink.

- [x] **3.1** Replaced the 13-record inheritance chain in `BuildSsdtPipelineStates.cs`
  with a single `BuildSsdtState` record (223 → 50 lines). Required `Request`/`Log`;
  later-stage fields default to `null!`/empty (set by the time downstream steps read them,
  exactly as the step order guaranteed before).
- [x] **3.2** Rewrote all 11 `BuildSsdt*Step` files + `BuildSsdtPipeline` to take/return
  `BuildSsdtState` and use `state with { … }` (setting only each step's new fields) instead
  of the 15-25-arg passthrough constructors. Updated 2 test files. Full solution builds;
  all 46 BuildSsdt tests pass (verified together and in isolation). Behavior-preserving.

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
- `Osm.Cli.Tests.FilesystemPermissionTests.BuildSsdt_fails_when_output_directory_is_read_only`
  fails on a clean tree — the test expects a write failure on a read-only directory, but
  the container runs as root (root bypasses read-only permissions). Environment artifact,
  not a code regression.
- `Osm.Pipeline.Tests` `BuildSsdtPipelineTests.ExecuteAsync_emits_manifest_seed_and_cache`
  and `SqlModelExtractionServiceTests.ExtractAsync_ToFile_ShouldPersistLargeSnapshotWithoutRetainingBuffers`
  intermittently fail under full-suite parallelism (heavy shared-I/O + a large-memory
  snapshot test contending for resources). Both pass reliably in isolation and the full
  BuildSsdt class (46 tests) passes together — flakiness, not a regression.

## Verification notes

- Full `dotnet test` runs against a warm SQL container and exceeds CI time limits;
  run scoped per-project test suites for the projects each change touches.
- Golden-file/snapshot tests guard SMO emission output — phase 4 DDL changes must
  diff clean (or the goldens be intentionally regenerated and reviewed).
