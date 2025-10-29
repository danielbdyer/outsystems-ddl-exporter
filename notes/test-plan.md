# Living Test Plan – OutSystems DDL Exporter

> Derived from `readme.md`, `architecture-guardrails.md`, `notes/design-contracts.md`, and `tasks.md`. Use this checklist to drive test-first delivery across the Clean Architecture boundaries (Domain ⇄ Validation ⇄ Pipeline ⇄ SMO ⇄ CLI) while honoring feature toggles and deterministic fixtures.
>
> Conventions
> - Fixtures: prefer `tests/Fixtures/model.edge-case.json` plus micro-fixtures F1–F3. Derive new minimal fixtures per scenario when needed and keep the Advanced SQL export in `src/AdvancedSql/outsystems_model_export.sql` as the canonical shape for new fixture captures.
> - Modes: run Cautious, EvidenceGated, and Aggressive unless a scenario states otherwise.
> - Toggles: hydrate defaults from `config/default-tightening.json` so every policy test starts from the shared EvidenceGated baseline before exercising overrides.
> - Assertions: assert externally observable behavior (decisions, emitted DDL, CLI exit codes).

- [x] **1.1 Required fields and shape** — reject payloads missing modules/entities. *(Unit · P0 · [tests/Osm.Validation.Tests/ModelValidator/RequiredFieldsTests.cs](tests/Osm.Validation.Tests/ModelValidator/RequiredFieldsTests.cs))*
- [x] **1.2 Duplicate attribute names / physical names** — surface entity-level collisions. *(Unit · P0 · [tests/Osm.Validation.Tests/ModelValidator/AttributeUniquenessTests.cs](tests/Osm.Validation.Tests/ModelValidator/AttributeUniquenessTests.cs))*
- [x] **1.3 Identifier constraints** — enforce exactly one identifier with type `Identifier`. *(Unit · P0 · [tests/Osm.Validation.Tests/ModelValidator/IdentifierRulesTests.cs](tests/Osm.Validation.Tests/ModelValidator/IdentifierRulesTests.cs))*
- [x] **1.4 Reference sanity** — references require known targets and metadata. *(Unit · P0 · [tests/Osm.Validation.Tests/ModelValidator/ReferenceRulesTests.cs](tests/Osm.Validation.Tests/ModelValidator/ReferenceRulesTests.cs))*
- [x] **1.5 Index integrity** — verify index columns exist and ordinals are contiguous. *(Unit · P1 · [tests/Osm.Validation.Tests/ModelValidator/IndexRulesTests.cs](tests/Osm.Validation.Tests/ModelValidator/IndexRulesTests.cs))*
- [x] **1.6 Schema/cross-catalog coherence** — downgrade or warn on constrained cross-schema FKs by default. *(Unit · P1 · [tests/Osm.Validation.Tests/ModelValidator/ReferenceRulesTests.cs](tests/Osm.Validation.Tests/ModelValidator/ReferenceRulesTests.cs))*
- [x] **1.7 Model root invariants** — OutSystems model requires at least one module and disallows duplicate module names (case-insensitive). *(Unit · P0 · [tests/Osm.Domain.Tests/OsmModelTests.cs](tests/Osm.Domain.Tests/OsmModelTests.cs))*

## 2. Profiler (Catalog & Data Reality) (`Osm.Pipeline` / `Osm.Domain` Profiling)
- [x] **2.1 Null counts per table** — per-table pivot yields deterministic `NullCount`. *(Unit · P0 · [tests/Osm.Json.Tests/ProfileSnapshotDeserializerTests.cs](tests/Osm.Json.Tests/ProfileSnapshotDeserializerTests.cs), [tests/Osm.Pipeline.Tests/FixtureDataProfilerTests.cs](tests/Osm.Pipeline.Tests/FixtureDataProfilerTests.cs))*
- [x] **2.2 Unique duplicate detection** — detect duplicates ignoring NULLs. *(Unit · P0 · [tests/Osm.Json.Tests/ProfileSnapshotDeserializerTests.cs](tests/Osm.Json.Tests/ProfileSnapshotDeserializerTests.cs), [tests/Osm.Pipeline.Tests/FixtureDataProfilerTests.cs](tests/Osm.Pipeline.Tests/FixtureDataProfilerTests.cs))*
- [x] **2.3 FK orphan detection (no DB FK)** — identify orphans when constraints absent. *(Unit · P0 · [tests/Osm.Json.Tests/ProfileSnapshotDeserializerTests.cs](tests/Osm.Json.Tests/ProfileSnapshotDeserializerTests.cs), [tests/Osm.Pipeline.Tests/FixtureDataProfilerTests.cs](tests/Osm.Pipeline.Tests/FixtureDataProfilerTests.cs))*
- [x] **2.4 Physical metadata snapshot** — capture computed/default/non-null flags. *(Unit · P1 · [tests/Osm.Pipeline.Tests/FixtureDataProfilerTests.cs](tests/Osm.Pipeline.Tests/FixtureDataProfilerTests.cs))*
- [x] **2.5 Performance: many columns** — confirm scaling for wide tables (512 columns) while keeping SQL sampling capped at 50k rows per probe. *(Perf · P2 · [tests/Osm.Pipeline.Tests/Performance/SqlDataProfilerPerformanceTests.cs](tests/Osm.Pipeline.Tests/Performance/SqlDataProfilerPerformanceTests.cs))*

## 3. Tightening Policy (Decisions) (`Osm.Validation` or dedicated policy project)
- [x] **3.1 PK → NOT NULL** — PKs always tighten. *(Unit · P0 · [tests/Osm.Validation.Tests/Policy/PrimaryKeyTighteningTests.cs](tests/Osm.Validation.Tests/Policy/PrimaryKeyTighteningTests.cs))*
- [x] **3.2 Physical NOT NULL respected** — physical metadata enforces tightening. *(Unit · P0 · [tests/Osm.Validation.Tests/Policy/PhysicalRealityTests.cs](tests/Osm.Validation.Tests/Policy/PhysicalRealityTests.cs))*
- [x] **3.3 EvidenceGated: Unique clean ⇒ NOT NULL** — unique + clean data tightens. *(Unit · P0 · [tests/Osm.Validation.Tests/Policy/TighteningPolicyTests.cs](tests/Osm.Validation.Tests/Policy/TighteningPolicyTests.cs))*
- [x] **3.4 EvidenceGated: Mandatory + Default + No NULLs ⇒ NOT NULL**. *(Unit · P1 · [tests/Osm.Validation.Tests/Policy/MandatoryDefaultTests.cs](tests/Osm.Validation.Tests/Policy/MandatoryDefaultTests.cs))*
- [x] **3.5 EvidenceGated: FK enforced + No NULLs ⇒ NOT NULL**. *(Unit · P1 · [tests/Osm.Validation.Tests/Policy/TighteningPolicyTests.cs](tests/Osm.Validation.Tests/Policy/TighteningPolicyTests.cs))*
- [x] **3.6 Ignore + Orphans ⇒ no FK, no NOT NULL**. *(Unit · P0 · [tests/Osm.Validation.Tests/Policy/TighteningPolicyTests.cs](tests/Osm.Validation.Tests/Policy/TighteningPolicyTests.cs))*
- [x] **3.7 Aggressive remediation generation** — tighten with pre-script requirement. *(Unit · P1 · [tests/Osm.Validation.Tests/Policy/TighteningPolicyTests.cs](tests/Osm.Validation.Tests/Policy/TighteningPolicyTests.cs))*
- [x] **3.8 NullBudget epsilon behavior** — treat tiny null rates within tolerance. *(Unit · P2 · [tests/Osm.Validation.Tests/Policy/NullBudgetTests.cs](tests/Osm.Validation.Tests/Policy/NullBudgetTests.cs))*
- [x] **3.9 Multi-column unique** — enforce composite uniqueness when clean. *(Unit · P3 · [tests/Osm.Validation.Tests/Policy/TighteningPolicyTests.cs](tests/Osm.Validation.Tests/Policy/TighteningPolicyTests.cs))*
- [x] **3.10 Unique index strategy remediation** — cover physical enforcement with duplicates and aggressive remediation when evidence is absent. *(Unit · P2 · [tests/Osm.Validation.Tests/Policy/UniqueIndexDecisionStrategyTests.cs](tests/Osm.Validation.Tests/Policy/UniqueIndexDecisionStrategyTests.cs))*

## 4. SMO Object Builder (`Osm.Smo`)
- [x] **4.1 Column nullability applied** — offline `SmoColumnDefinition` records reflect tightening decisions. *(Unit · P0 · [tests/Osm.Smo.Tests/SmoModelFactoryTests.cs](tests/Osm.Smo.Tests/SmoModelFactoryTests.cs))*
- [x] **4.2 PK creation** — offline index definitions preserve clustered PK column order. *(Unit · P0 · [tests/Osm.Smo.Tests/SmoModelFactoryTests.cs](tests/Osm.Smo.Tests/SmoModelFactoryTests.cs))*
- [x] **4.3 Unique index enforcement / suppression** — toggle unique vs. disabled. *(Unit · P1 · [tests/Osm.Smo.Tests/SmoModelFactoryTests.cs](tests/Osm.Smo.Tests/SmoModelFactoryTests.cs), [tests/Osm.Emission.Tests/SsdtEmitterTests.cs](tests/Osm.Emission.Tests/SsdtEmitterTests.cs))*
- [x] **4.4 FK creation only when allowed** — offline FK definitions obey decision flags. *(Unit · P0 · [tests/Osm.Smo.Tests/SmoModelFactoryTests.cs](tests/Osm.Smo.Tests/SmoModelFactoryTests.cs))*
- [x] **4.5 External schema handling** — offline table definitions retain non-dbo schemas. *(Unit · P1 · [tests/Osm.Smo.Tests/SmoModelFactoryTests.cs](tests/Osm.Smo.Tests/SmoModelFactoryTests.cs))*
- [x] **4.6 External db type passthrough** — offline columns honor external type metadata. *(Unit · P2 · [tests/Osm.Smo.Tests/SmoModelFactoryTests.cs](tests/Osm.Smo.Tests/SmoModelFactoryTests.cs))*
- [x] **4.7 Logical naming projection** — SMO definitions surface logical table/column identifiers for downstream scripting. *(Unit · P0 · [tests/Osm.Smo.Tests/SmoModelFactoryTests.cs](tests/Osm.Smo.Tests/SmoModelFactoryTests.cs))*

## 5. Per-Table DDL Emitter (`Osm.Emission`)
- [x] **5.1 File organization by module** — verify folder tree and naming. *(Integration · P0 · [tests/Osm.Emission.Tests/SsdtEmitterTests.cs](tests/Osm.Emission.Tests/SsdtEmitterTests.cs))*
- [x] **5.2 Table scripts contain PK only** — indexes/FKs emitted separately. *(Integration · P0 · [tests/Osm.Emission.Tests/SsdtEmitterTests.cs](tests/Osm.Emission.Tests/SsdtEmitterTests.cs))*
- [x] **5.3 Index script filters PK** — avoid PK duplication. *(Integration · P0 · [tests/Osm.Emission.Tests/SsdtEmitterTests.cs](tests/Osm.Emission.Tests/SsdtEmitterTests.cs))*
- [x] **5.4 Pre-remediation concatenation per table** — single file per table with GO separators. *(Integration · P1 · [tests/Osm.Emission.Tests/SsdtEmitterTests.cs](tests/Osm.Emission.Tests/SsdtEmitterTests.cs))*
- [x] **5.5 Manifest generation** — manifest lists all outputs + toggle snapshot. *(Integration · P1 · [tests/Osm.Emission.Tests/SsdtEmitterTests.cs](tests/Osm.Emission.Tests/SsdtEmitterTests.cs))*
- [x] **5.6 Table naming overrides** — config/CLI renames cascade to scripts and manifests. *(Integration · P0 · [tests/Osm.Emission.Tests/SsdtEmitterTests.cs](tests/Osm.Emission.Tests/SsdtEmitterTests.cs), [tests/Osm.Cli.Tests/CliIntegrationTests.cs](tests/Osm.Cli.Tests/CliIntegrationTests.cs), [tests/Osm.Etl.Integration.Tests/EmissionPipelineTests.cs](tests/Osm.Etl.Integration.Tests/EmissionPipelineTests.cs))*
- [x] **5.7 Golden fixture parity** — end-to-end pipeline emission matches the curated SSDT snapshots for default and rename scenarios. *(Integration · P0 · [tests/Osm.Etl.Integration.Tests/EmissionPipelineTests.cs](tests/Osm.Etl.Integration.Tests/EmissionPipelineTests.cs))*
- [x] **5.8 Logical casing in emitted DDL** — default emission rewrites tables/columns to logical names while preserving override support. *(Integration · P0 · [tests/Osm.Emission.Tests/SsdtEmitterTests.cs](tests/Osm.Emission.Tests/SsdtEmitterTests.cs), [tests/Osm.Etl.Integration.Tests/EmissionPipelineTests.cs](tests/Osm.Etl.Integration.Tests/EmissionPipelineTests.cs))*
- [x] **5.9 Toggle: include/exclude platform auto-indexes** — OSIDX_* obey toggle. *(Unit · P1 · [tests/Osm.Smo.Tests/SmoModelFactoryTests.cs](tests/Osm.Smo.Tests/SmoModelFactoryTests.cs); Integration · P1 · [tests/Osm.Emission.Tests/SsdtEmitterTests.cs](tests/Osm.Emission.Tests/SsdtEmitterTests.cs))*

## 6. DMM Parity Comparator (`Osm.Dmm`)
- [x] **6.1 Perfect parity passes** — identical DDL yields no diffs. *(Unit · P0 · [tests/Osm.Dmm.Tests/DmmComparatorTests.cs](tests/Osm.Dmm.Tests/DmmComparatorTests.cs))*
- [x] **6.2 Column order sensitivity** — detect mismatched order when strict. *(Unit · P1 · [tests/Osm.Dmm.Tests/DmmComparatorTests.cs](tests/Osm.Dmm.Tests/DmmComparatorTests.cs))*
- [x] **6.3 Type canonicalization** — ignore stylistic differences. *(Unit · P1 · [tests/Osm.Dmm.Tests/DmmComparatorTests.cs](tests/Osm.Dmm.Tests/DmmComparatorTests.cs))*
- [x] **6.4 Missing or extra table/column** — report presence diffs. *(Unit · P0 · [tests/Osm.Dmm.Tests/DmmComparatorTests.cs](tests/Osm.Dmm.Tests/DmmComparatorTests.cs))*
- [x] **6.5 PK differences** — highlight PK mismatches. *(Unit · P0 · [tests/Osm.Dmm.Tests/DmmComparatorTests.cs](tests/Osm.Dmm.Tests/DmmComparatorTests.cs))*
- [x] **6.6 NOT NULL enforcement** — flag nullable vs. required columns. *(Unit · P0 · [tests/Osm.Dmm.Tests/DmmComparatorTests.cs](tests/Osm.Dmm.Tests/DmmComparatorTests.cs))*
- [x] **6.7 Parser robustness** — handle inline vs. ALTER PK styles. *(Unit · P1 · [tests/Osm.Dmm.Tests/DmmComparatorTests.cs](tests/Osm.Dmm.Tests/DmmComparatorTests.cs))*

## 7. CLI / Pipeline Orchestration (`Osm.Cli` + `Osm.Pipeline`)
- [x] **7.1 `build-ssdt` end-to-end** — full artifact emission with mock profiler. *(Integration · P0 · [tests/Osm.Cli.Tests/CliIntegrationTests.cs](tests/Osm.Cli.Tests/CliIntegrationTests.cs))*
- [x] **7.2 `dmm-compare` gate failure** — exit non-zero + diff artifact. *(Integration · P0 · [tests/Osm.Cli.Tests/CliIntegrationTests.cs](tests/Osm.Cli.Tests/CliIntegrationTests.cs))*
- [x] **7.3 Toggle overrides via flags/env** — CLI overrides configuration (table rename override covered via `--rename-table`; environment fallback validated by `BuildSsdt_AllowsEnvironmentOverrides`). *(Integration · P1 · [tests/Osm.Cli.Tests/CliIntegrationTests.cs](tests/Osm.Cli.Tests/CliIntegrationTests.cs))*
- [x] **7.4 Mock profiler folder** — deterministic run without SQL Server. *(Integration · P0 · [tests/Osm.Cli.Tests/CliIntegrationTests.cs](tests/Osm.Cli.Tests/CliIntegrationTests.cs))*
- [x] **7.5 Config & environment overrides** — `--config` binding plus env-var fallbacks hydrate CLI defaults and evidence cache behavior. *(Integration · P1 · [tests/Osm.Cli.Tests/CliIntegrationTests.cs](tests/Osm.Cli.Tests/CliIntegrationTests.cs), [tests/Osm.Cli.Tests/Configuration/CliConfigurationLoaderTests.cs](tests/Osm.Cli.Tests/Configuration/CliConfigurationLoaderTests.cs))*
- [x] **7.6 Module filter enforcement** — module selection flags/config limit emission scope and merge with cache metadata. *(Integration · P1 · [tests/Osm.Cli.Tests/CliIntegrationTests.cs](tests/Osm.Cli.Tests/CliIntegrationTests.cs); Unit · P1 · [tests/Osm.Pipeline.Tests/ModuleFilterTests.cs](tests/Osm.Pipeline.Tests/ModuleFilterTests.cs), [tests/Osm.Domain.Tests/ModuleFilterOptionsTests.cs](tests/Osm.Domain.Tests/ModuleFilterOptionsTests.cs))*
- [x] **7.7 CLI fixture parity** — `build-ssdt` default run and rename overrides reproduce the curated emission snapshots byte-for-byte. *(Integration · P0 · [tests/Osm.Cli.Tests/CliIntegrationTests.cs](tests/Osm.Cli.Tests/CliIntegrationTests.cs))*

## 8. Safety, Idempotence, and Data-Fix Semantics
- [ ] **8.1 Pre-script idempotence** — reruns without side effects. *(Integration · P1 · [tests/Osm.Smo.Tests/Emitter/PreScriptIdempotenceTests.cs](tests/Osm.Smo.Tests/Emitter/PreScriptIdempotenceTests.cs))*
- [ ] **8.2 Batch limits honored** — enforce backfill thresholds. *(Unit & Integration · P2 · [tests/Osm.Smo.Tests/Emitter/BatchLimitTests.cs](tests/Osm.Smo.Tests/Emitter/BatchLimitTests.cs))*
- [ ] **8.3 WITH CHECK for FK trust** — constraints created trusted. *(Integration · P2 · [tests/Osm.Smo.Tests/Emitter/FkTrustTests.cs](tests/Osm.Smo.Tests/Emitter/FkTrustTests.cs))*
- [ ] **8.4 Evidence cache drift detection** — cache invalidates when row counts or module selections change beyond thresholds. *(Integration · P1 · pending)*

## 9. Error Handling & Diagnostics
- [ ] **9.1 Helpful JSON parse errors** — include JSON path context. *(Unit · P1 · [tests/Osm.Json.Tests/ErrorReporting/JsonParseErrorTests.cs](tests/Osm.Json.Tests/ErrorReporting/JsonParseErrorTests.cs))*
- [ ] **9.2 ScriptDom parse failures** — report file + line positions. *(Unit · P1 · [tests/Osm.Dmm.Tests/ErrorReporting/ScriptDomErrorTests.cs](tests/Osm.Dmm.Tests/ErrorReporting/ScriptDomErrorTests.cs))*
- [ ] **9.3 Filesystem permissions** — graceful failure on unwritable output. *(Integration · P2 · [tests/Osm.Cli.Tests/FilesystemPermissionTests.cs](tests/Osm.Cli.Tests/FilesystemPermissionTests.cs))*

## 10. Performance & Scale
- [x] **10.1 Many entities** — ensure no quadratic behavior (500×10) and enforce `MaxConcurrentTableProfiles <= 8` throughout execution. *(Perf · P2 · [tests/Osm.Pipeline.Tests/Performance/SqlDataProfilerPerformanceTests.cs](tests/Osm.Pipeline.Tests/Performance/SqlDataProfilerPerformanceTests.cs))*
- [ ] **10.2 Hot path profiling on large table** — document runtime for large datasets. *(Perf · P3 · [tests/Osm.Pipeline.Tests/Performance/LargeTableProfilingTests.cs](tests/Osm.Pipeline.Tests/Performance/LargeTableProfilingTests.cs))*

## 11. Property-Based & Mutation-Resilience
- [ ] **11.1 Decision monotonicity (null counts)** — property tests keep tightening monotonic. *(Property · P2 · [tests/Osm.Validation.Tests/Policy/DecisionMonotonicityPropertyTests.cs](tests/Osm.Validation.Tests/Policy/DecisionMonotonicityPropertyTests.cs))*
- [ ] **11.2 FK decision monotonicity** — orphans cleared never disable constraints. *(Property · P2 · [tests/Osm.Validation.Tests/Policy/FkDecisionPropertyTests.cs](tests/Osm.Validation.Tests/Policy/FkDecisionPropertyTests.cs))*
- [ ] **11.3 Mutation: index ordinal shuffles** — validator rejects non-contiguous ordinals under mutation. *(Mutation · P3 · [tests/Osm.Validation.Tests/ModelValidator/IndexMutationTests.cs](tests/Osm.Validation.Tests/ModelValidator/IndexMutationTests.cs))*

## 12. Security / Least Privilege
- [ ] **12.1 Profiler honors read-only principal** — no DDL attempted. *(Integration · P2 · [tests/Osm.Pipeline.Tests/Security/ReadOnlyPrincipalTests.cs](tests/Osm.Pipeline.Tests/Security/ReadOnlyPrincipalTests.cs))*

## 13. SSDT Consumption Dry-Run
- [ ] **13.1 SSDT import build check** — emitted artifacts compile inside SSDT project. *(Integration · P2 · [tests/Osm.Cli.Tests/SsdtImportTests.cs](tests/Osm.Cli.Tests/SsdtImportTests.cs))*

## 14. Toggle Matrix Spot Checks
- [ ] **14.1 Policy/toggle matrix** — iterate representative combinations and assert deltas (Aggressive vs EvidenceGated, OSIDX toggle, FK enablement, null budget). *(Integration · P1 · [tests/Osm.Cli.Tests/ToggleMatrixTests.cs](tests/Osm.Cli.Tests/ToggleMatrixTests.cs))*

## 15. Inactive-But-Physical Columns
- [x] **15.1 Active-only emission** — suppress inactive or physically retired attributes from SSDT output while recording their policy decisions for audit. *(Unit · P1 · [tests/Osm.Smo.Tests/SmoModelFactoryTests.cs](tests/Osm.Smo.Tests/SmoModelFactoryTests.cs); Integration · P1 · [tests/Osm.Emission.Tests/SsdtEmitterTests.cs](tests/Osm.Emission.Tests/SsdtEmitterTests.cs), [tests/Osm.Etl.Integration.Tests/EmissionPipelineTests.cs](tests/Osm.Etl.Integration.Tests/EmissionPipelineTests.cs))*

## 16. Cross-Schema / Cross-Catalog Edge Cases
- [ ] **16.1 Cross-schema Protect** — obey `AllowCrossSchema` toggle. *(Unit · P2 · [tests/Osm.Validation.Tests/Policy/CrossSchemaToggleTests.cs](tests/Osm.Validation.Tests/Policy/CrossSchemaToggleTests.cs))*
- [ ] **16.2 Cross-catalog suppressed** — default suppression rationale. *(Unit · P2 · [tests/Osm.Validation.Tests/Policy/CrossCatalogSuppressionTests.cs](tests/Osm.Validation.Tests/Policy/CrossCatalogSuppressionTests.cs))*

## 17. Logging / Observability
- [x] **17.1 Decision rationale transparency** — log rationale stack per tightened artifact. *(Unit · P2 · [tests/Osm.Validation.Tests/Policy/DecisionReportTests.cs](tests/Osm.Validation.Tests/Policy/DecisionReportTests.cs))*
- [x] **17.2 Artifact inventory** — manifest counts align with filesystem outputs. *(Integration · P1 · [tests/Osm.Emission.Tests/SsdtEmitterTests.cs](tests/Osm.Emission.Tests/SsdtEmitterTests.cs), [tests/Osm.Cli.Tests/CliIntegrationTests.cs](tests/Osm.Cli.Tests/CliIntegrationTests.cs))*
- [ ] **17.3 CI telemetry artifact** — GitHub Actions uploads CLI logs, manifests, and decision summaries for every matrix leg. *(CI · P1 · pending)*

## 18. Evidence Extraction & Caching
- [x] **18.1 Configurable SQL extraction** — CLI connects via typed options and emits sanitized model JSON for selected modules. *(Integration · P0 · [tests/Osm.Cli.Tests/Extraction/ConfigurableConnectionTests.cs](tests/Osm.Cli.Tests/Extraction/ConfigurableConnectionTests.cs); Unit · P1 · [tests/Osm.Pipeline.Tests/SqlModelExtractionServiceTests.cs](tests/Osm.Pipeline.Tests/SqlModelExtractionServiceTests.cs), [tests/Osm.Pipeline.Tests/FixtureAdvancedSqlExecutorTests.cs](tests/Osm.Pipeline.Tests/FixtureAdvancedSqlExecutorTests.cs))*
- [x] **18.2 Cache key determinism** — identical module/toggle selections reuse cached payloads; differing inputs create new entries. *(Unit · P1 · [tests/Osm.Pipeline.Tests/EvidenceCacheServiceTests.cs](tests/Osm.Pipeline.Tests/EvidenceCacheServiceTests.cs))*
- [x] **18.3 Refresh override** — `--refresh-cache` bypasses cached payloads and records new timestamp/hash metadata. *(Integration · P1 · [tests/Osm.Cli.Tests/CliIntegrationTests.cs](tests/Osm.Cli.Tests/CliIntegrationTests.cs))*
- [x] **18.4 Cache manifest integrity** — manifest enumerates payload provenance, SHA hashes, and timestamps for auditing. *(Unit · P1 · [tests/Osm.Pipeline.Tests/EvidenceCacheServiceTests.cs](tests/Osm.Pipeline.Tests/EvidenceCacheServiceTests.cs))*
- [ ] **18.5 Live SQL adapter smoke** — run the concrete `IAdvancedSqlExecutor` against a containerized stub DB and assert extracted JSON matches fixtures. *(Integration · P1 · pending)*
- [ ] **18.6 Cache eviction on module/toggle removal** — stale payloads are purged or rehydrated when module selections shrink or toggles change. *(Unit/Integration · P2 · pending)*
- [ ] **18.7 Live extractor timeout controls** — verify configurable command timeout and sampling knobs surface in telemetry. *(Integration · P2 · pending)*

---

### Tracking Notes
- Update this file when new scenarios emerge or when coverage shifts between Unit/Integration/Perf classifications.
- Cross-link implemented tests back to architecture guardrails and backlog items to keep stakeholders aligned on progress.
- Reference `notes/design-contracts.md` during test design to confirm invariants and failure modes are asserted at the right boundary.
