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

> **Status:** 2.6 (the `ScripterExtensions` trap) is done. The remaining items below were
> assessed during this work and several turned out to be riskier than the headline analysis
> implied — they touch **public contract surface** (manifest/fingerprint/config schemas),
> **golden DDL output**, or **serializer round-trips**. Those are flagged inline and left as
> **focused follow-up PRs** (each needs its own golden-diff / round-trip verification) rather
> than being bundled into this sweep. This is deliberate: the value of Phase 4 is correctness
> + clarity, and rushing contract/golden changes would undercut both.

- [x] **2.6** (carried over) `ScripterExtensions.Dispose` extension renamed to a plain
  `DisconnectAndDispose(Scripter?)` static and invoked by name from `SmoContext.Dispose`,
  making the previously-invisible (and grep-"dead"-looking) coupling explicit. Smo green.
- [~] **4.14 Deferred — contract-entangled, NOT a safe shim delete.** `EmitBareTableOnly`
  is `[Obsolete]` but woven into the `SsdtManifest` field schema, the `EmissionFingerprintCalculator`
  (cache fingerprints), `CacheMetadataBuilder`, and — critically — **backward-compatible JSON
  config *input*** (`TighteningOptionsDeserializer` still accepts the `emitBareTableOnly` key).
  Removing it changes those contracts; it's a product decision, not a mechanical cleanup.

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
- [ ] **4.5** `EntityEmissionSnapshot` ×2 (`Model/Emission/` vs `Model/Artifacts/`). Follow-up
  (rename ripples through Smo/Emission/Pipeline consumers — its own PR).
- [x] **4.6** `OpportunitiesReport` ×2 — **resolved by Phase 2.1**: the legacy `Tightening/`
  copy was deleted, leaving only the canonical `Tightening/Opportunities/` one.
- [ ] **4.7** `CacheMetadataBuilder` ×2 (`Application/` vs `Evidence/`) — follow-up; needs a
  semantic-overlap check before merging (the two assemble different metadata shapes).
- [x] **4.8** `ITighteningAnalyzer` ×2 — renamed the internal decision-setting interface to
  `IColumnDecisionAnalyzer` (concept: analyzes a column → writes its tightening decision),
  leaving the public opportunity analyzer's `ITighteningAnalyzer` unambiguous. Build + Validation green.

### Remaining structural collapses
- [x] **4.9 Done.** Unified the duplicate ProfileSnapshot DTO family (serializer vs
  deserializer) into one shared internal `ProfileSnapshotDocuments.cs`. The `Ref` read-alias
  is preserved via `JsonIgnore(WhenWritingNull)` so serialized output is byte-identical.
  Json (89) + profile serialization/round-trip Pipeline tests (44) green.
- [x] **4.10 (partial)** Deduplicated `CreateSqlMetadataOptions` — four byte-identical private
  copies (`ModelLoader`, `FullExportPipeline`, `FullExportApplicationService`,
  `BuildSsdtApplicationService`) → one `ResolvedSqlOptions.ToModelIngestionMetadata()`.
  The `ProfilingQueryExecutor`/`TableMetadataLoader` reader-loop scaffold remains a follow-up
  (Med risk — per-call command config + error codes must be preserved exactly).
- [ ] **4.11** 5 command factories bypass `PipelineCommandFactory` (Analyze, Inspect,
  VerifyData, Policy, UatUsers) — re-hand-roll scope/error/exit-code plumbing.
  `SplitTableIdentifier` copy-pasted verbatim (78 LOC) between two UAT files.
- [x] **4.12 (partial)** Added `NullSafeComparer<T>` base in Osm.Smo and refactored the 4
  comparers (`SmoTableBuilder`, `SmoRenameLens`, `SmoColumnBuilder`, `SmoTriggerBuilder`) to
  drop the identical null-handling preamble, keeping only their key chains. Smo green (100).
  (The 9 single-string value objects + cross-aggregate duplicate-detection helpers remain a
  follow-up — the value-object collapse really wants a source generator.)
- [ ] **4.13** Single-impl interface cluster (~150-300 LOC): `IPathCanonicalizer`,
  several `Json/Deserialization` validators/factories with no test double — inline.
- (4.14 moved up — see the status block at the top of Phase 4.)
- [ ] **4.15** `SupplementalLoader` pure forwarding wrapper — inline into
  `PipelineBootstrapper`, delete (~40 LOC).
- [ ] **4.16** Options/resolver/binder sprawl: collapse the SQL options quartet
  (`SqlConnectionOptions`/`SqlProfilerOptions`/`SqlProfilerLimits`/`SqlSamplingOptions`)
  and the module-filter concept (modeled 3+ ways). ~200-400 LOC. (Largest of phase 4.)
- [ ] **4.17** Domain-blind naming migration: `*ApplicationService` → verb-concept,
  `*ResultSetProcessor` → `*RowReader`/`*Mapping`, `UserMatchingConfigurationHelper`,
  `LegacyPolicyAdapter`. Incremental, LOC-neutral, clarity-positive.

---

## Outcome of this pass

**Done & verified (committed):** Phase 0 (0.1, 0.2, 0.4), Phase 1 (full additive `Result`
API + the high-confidence migrations), Phase 2 (the entire legacy opportunity subsystem +
2.3/2.4/2.5/2.6), Phase 3 (the full BuildSsdt state-chain collapse — the single biggest win),
and Phase 4 naming de-collisions (4.6 resolved, 4.8 renamed). Each phase built clean and
passed its scoped suites; net ≈ −690 LOC despite adding new API + tests.

**Deferred to focused follow-up PRs (with rationale recorded inline above):** the items that
touch **public contract surface** (4.14 manifest/fingerprint/config; potentially 4.5/4.7 renames),
**golden DDL output** (4.1–4.4 — the no-string-concat DDL rewrites and AST-driven formatters),
or **serializer round-trips** (4.9 DTO dedup), plus the broad **options/resolver/binder sprawl**
(4.16) and **CLI command-factory** restructure (4.11). These are individually valuable but each
needs its own golden-diff / round-trip / contract verification, which is why they were not
bundled into this sweep. 2.2 (FK matrix) is likewise deferred — it is a test-validated spec,
not dead code.

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
