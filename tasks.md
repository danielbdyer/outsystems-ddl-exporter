# OutSystems DDL Exporter – Remaining Backlog (Release Candidate Focus)

> This backlog captures the open scope needed to finish the extraction → tightening → SMO emission pipeline described in the README while honoring the architectural guardrails and the living test plan. Completed work from earlier iterations has been collapsed into guardrail references so we can focus on the remaining shippable increments.

## 0. Critical: Full-Export & UAT-Users Verification

### 0.1. Provable Export Integrity
- [ ] **Add export checksum and fingerprinting** to `FullExportRunManifest` so downstream consumers can verify artifact integrity without rerunning the entire pipeline. Include per-stage checksums (extraction hash, profile fingerprint, SSDT emission hash) alongside row-count and entity-count metadata. *(Guardrails §6; Test Plan §17)*
- [ ] **Implement manifest-driven export verification** that compares the declared artifact list (`DynamicArtifacts`, `StaticSeedArtifacts`) against the filesystem and fails fast when files are missing or have unexpected sizes. *(Guardrails §§1,6; Test Plan §§13.1,17.3)*
- [ ] **Create an export completeness validator** that cross-references the emitted entity scripts against the model's entity catalog, surfacing missing or orphaned files before SSDT import. Emit a structured report (`export-validation.json`) alongside the manifest. *(Guardrails §8; Test Plan §4)*
- [ ] **Add regression tests for manifest stability** that assert the `DynamicArtifacts`, `StaticSeedArtifacts`, and `Stages` collections remain deterministic across repeated runs with identical inputs, proving idempotence. *(Test Plan §§8.1,11.1)*

### 0.2. Provable Topological Insertion Order
- [ ] **Extend `EntityDependencySorter` to emit a dependency proof artifact** (`topological-order-proof.json`) that records every edge (source table, target table, FK constraint name), the final ordering, and cycle/fallback diagnostics. Persist this alongside dynamic insert scripts. *(Guardrails §§5,8; Test Plan §§2.5,13.1)*
- [ ] **Add topological order validation tests** that inject known FK cycles or missing edges and assert the sorter produces correct warnings (`CycleDetected`, `MissingEdgeCount`) while still emitting a valid alphabetical fallback. *(Test Plan §§4,11.3)*
- [ ] **Implement a post-emission FK constraint verifier** that parses emitted dynamic insert scripts, extracts referenced tables, and proves every INSERT statement appears after all its FK dependencies. Fail the build when violations are detected. *(Guardrails §§5,7; Test Plan §§8.3,13.1)*
- [ ] **Capture topological metadata in `FullExportRunManifest`** under `Stages[].Artifacts.topologicalProof` so CI/CD can archive the proof and operators can audit ordering decisions without re-parsing the model. *(Guardrails §6; Test Plan §17)*
- [ ] **Document topological fallback behavior** in `docs/full-export-artifact-contract.md`, explaining when alphabetical fallback applies, how to diagnose cycles, and remediation steps for operators. Include examples from the edge-case fixtures. *(Architecture Guardrails §8)*

### 0.3. QA → UAT User Mapping Verification
- [ ] **Add comprehensive user inventory integrity checks** that validate both QA and UAT CSV files contain all required columns (`Id`, `Username`, `EMail`, `Name`, `External_Id`, `Is_Active`, `Creation_Date`, `Last_Login`), report missing/malformed rows with line numbers, and fail fast before FK analysis begins. *(Guardrails §6; Test Plan §§9.1,12.1)*
- [ ] **Implement orphan discovery proof artifact** (`uat-users-orphan-discovery.json`) that records the FK catalog, distinct user IDs per column, orphan detection logic, and the final orphan set. Include row counts, query fingerprints, and timestamps for audit trails. *(Guardrails §§6,8; Test Plan §17)*
- [ ] **Create a mapping completeness validator** that proves every orphan from the QA inventory has a corresponding `TargetUserId` in the user map, every `TargetUserId` exists in the UAT inventory, and no duplicate `SourceUserId` entries exist. Emit actionable errors with line numbers and recommended fixes. *(Guardrails §6; Test Plan §§7.3,9.1)*
- [ ] **Add FK value transformation verification** that re-scans the generated SQL script, extracts all `UPDATE` statements, and proves that every `WHERE <column> IN (...)` clause references only `SourceUserId` values from the orphan set, and every `SET <column> = CASE ...` block assigns only `TargetUserId` values from the UAT inventory. *(Guardrails §§5,8; Test Plan §8.3)*
- [ ] **Extend `ValidateUserMapStep` to emit a validation report** (`uat-users-validation-report.json`) that includes pass/fail status for each validation rule (QA inventory coverage, UAT inventory coverage, duplicate detection, orphan completeness), along with detailed error contexts and counts. *(Guardrails §6; Test Plan §17)*

### 0.4. In-Scope User Guarantee & FK Reference Integrity
- [ ] **Implement a snapshot-driven FK value audit** that captures the distinct user IDs from each catalogued column before and after the remap script runs, proving that only in-scope `SourceUserId` values were transformed and all resulting values exist in the UAT user inventory. *(Guardrails §10; Test Plan §18.5)*
- [ ] **Add a pre-flight NULL preservation check** that queries each FK column for NULL values, records the count in the snapshot, and asserts the generated SQL script includes `WHERE <column> IS NOT NULL` guards for every UPDATE statement. *(Test Plan §8.3; Guardrails §8)*
- [ ] **Create a UAT inventory allow-list verifier** that cross-references the final user map against the parsed UAT CSV, surfaces any `TargetUserId` values not present in the `Id` column, and blocks artifact emission until the map is corrected. *(Guardrails §6; Test Plan §§7.3,12.1)*
- [ ] **Extend the matching report** (`04_matching_report.csv`) to include a `ValidationStatus` column (`Valid`, `TargetNotInUAT`, `SourceNotInQA`, `OrphanMismatch`) so operators can identify invalid mappings before the SQL script is generated. *(Guardrails §6; Test Plan §17)*
- [ ] **Add end-to-end integration tests** that simulate QA→UAT promotion with edge cases: missing UAT users, duplicate source mappings, orphan exclusions, NULL preservation, and GUID vs. INT identifier mixing. Assert the pipeline fails gracefully with actionable diagnostics. *(Test Plan §§9.1,18.5)*
- [ ] **Document the user mapping verification contract** in `docs/verbs/uat-users.md`, including validation rules, proof artifacts, error messages, and a troubleshooting matrix for common operator mistakes. *(Architecture Guardrails §8)*

### 0.5. Full-Export + UAT-Users Integrated Workflow
- [ ] **Extend `FullExportRunManifest` to capture UAT-users provenance** including QA/UAT inventory paths, snapshot fingerprints, orphan counts, mapping counts, FK catalog size, and the matching strategy used. Surface this metadata in the `uatUsers.*` namespace. *(Guardrails §6; Test Plan §17)*
- [ ] **Add full-export idempotence tests** that run `full-export --enable-uat-users` twice with identical inputs and assert manifests, artifacts, and checksums remain stable (including UAT-users outputs). *(Test Plan §§8.1,11.1)*
- [ ] **Implement a combined verification step** that runs after the UAT-users pipeline completes, asserting that dynamic insert scripts, static seed scripts, and UAT remap scripts collectively reference only approved entities and users. Emit a consolidated proof report. *(Guardrails §§8,10; Test Plan §13.1)*
- [ ] **Create a load harness extension** (`tools/FullExportLoadHarness`) that can replay the UAT remap script against a staging database, capture before/after row counts per FK column, and prove the transformation was lossless (no NULLs introduced, no orphans created). *(Guardrails §§7,10; Test Plan §§10.2,18.5)*
- [ ] **Document the full-export + uat-users playbook** in `docs/full-export-artifact-contract.md`, explaining the artifact layout, proof mechanisms, verification steps, and SSDT/deployment integration for QA→UAT promotion scenarios. *(Architecture Guardrails §8)*

---

## 1. Policy & Decision Telemetry Hardening
- [ ] Finish codifying the tightening policy matrix for `Cautious`, `EvidenceGated`, and `Aggressive` modes, documenting every NOT NULL / UNIQUE / FK rule with references to profiling evidence requirements. Surface the matrix in `notes/design-contracts.md` and assert it with table-driven tests. *(Guardrails §4; Test Plan §14.1)*
- [ ] Expand decision telemetry so the CLI emits per-module rollups (tightened columns, skipped FKs with rationale) alongside the existing manifest. *(Guardrails §6; Test Plan §17)*
- [ ] Add property-based tests that prove decision monotonicity when null counts drop or orphans are eliminated. *(Test Plan §§11.1–11.2)*
- [ ] Document toggle precedence (CLI flags → environment → config) and ensure the policy engine logs when overrides change default behavior. *(Guardrails §2; Test Plan §§7.3–7.5)*

## 2. SMO & Emission Validation
- [ ] Extend `SmoModelFactoryTests` to assert NOT NULL / UNIQUE / FK propagation for the edge-case fixture so SMO output mirrors policy decisions end-to-end. *(Guardrails §5; Test Plan §4)*
- [ ] Add pre-remediation idempotence and batch limit tests for the emission path, ensuring reruns do not duplicate scripts or overrun configured thresholds. *(Test Plan §§8.1–8.2)*
- [ ] Verify FK scripting uses `WITH CHECK` and trusted states under positive evidence, and capture opt-out coverage when evidence is missing. *(Test Plan §8.3; Guardrails §8)*
- [ ] Create an SSDT import smoke test that compiles the emitted artifacts inside an SSDT project to prove downstream consumption. *(Test Plan §13.1)*
- [ ] Introduce mutation tests that shuffle index ordinals or toggle inactive columns to guarantee the SMO builder rejects invalid graphs. *(Test Plan §11.3; Guardrails §1)*

## 3. Evidence Extraction & Caching
- [ ] Implement the concrete `IAdvancedSqlExecutor` using `Microsoft.Data.SqlClient`, honoring Guardrail §10 by keeping database concerns in the Pipeline layer. *(Test Plan §18.5 prerequisite)*
- [ ] Stand up an integration test harness (containerized SQL Server or deterministic stub) that exercises the live extraction adapter and asserts parity with the fixture-generated JSON. *(Test Plan §18.5)*
- [ ] Add cache eviction heuristics so stale payloads age out when module selections shrink or toggles change, and cover the logic with unit + integration tests. *(Test Plan §18.6)*
- [ ] Surface timeout and sampling knobs for the extractor, propagate them through manifests, and validate they appear in cache telemetry. *(Test Plan §18.7; Guardrails §10)*
- [ ] Introduce evidence cache drift detection that invalidates cache entries when profiling row counts deviate beyond configured tolerances. *(Test Plan §8.4)*

## 4. Performance & Scale Readiness
- [ ] Execute wide-table profiling benchmarks to validate the mock profiler and tightening pipeline maintain acceptable throughput for tables with hundreds of columns. *(Test Plan §2.5; Guardrails §7)*
- [ ] Add large-entity-count integration tests (500 entities × 10 attributes) to prove the pipeline does not exhibit quadratic behavior. *(Test Plan §10.1; Guardrails §7)*
- [ ] Capture timing baselines for the hot policy and SMO paths on representative datasets; publish results in `notes/perf-readout.md`. *(Test Plan §10.2)*
- [ ] **Benchmark topological sorting performance** on models with 1000+ entities and deep FK chains (10+ levels), documenting worst-case timings and memory usage in `notes/perf-readout.md`. *(Test Plan §§2.5,10.2; Guardrails §7)*
- [ ] **Profile UAT-users FK discovery and transformation** on datasets with 100+ FK columns and millions of distinct user IDs, ensuring the pipeline completes within acceptable SLAs. *(Test Plan §§10.1,18.5; Guardrails §7)*

## 5. Error Handling & Observability
- [ ] Improve JSON ingestion error messages with JSONPath context so CLI failures point to the exact offending field. *(Test Plan §9.1; Guardrails §6)*
- [ ] Enhance DMM parse diagnostics to include file, line, and token context on ScriptDom failures. *(Test Plan §9.2)*
- [ ] Harden filesystem error handling so unwritable output roots produce actionable messages and leave partial artifacts in a recoverable state. *(Test Plan §9.3)*
- [ ] Emit structured telemetry artifacts (JSON log + manifest + decision report) from CI matrix runs and document retention expectations. *(Test Plan §17.3; Guardrails §6)*
- [ ] Extend `notes/design-contracts.md` with sample failure payloads and telemetry schemas for downstream integrations. *(Architecture Guardrails §8)*

## 6. Security & Operations Enablement
- [ ] Validate that the profiler and extraction adapters operate correctly under read-only SQL principals with minimal permissions. *(Test Plan §12.1)*
- [ ] Author an operator runbook covering cache warm-up, toggle strategies for incremental hardening, and incident response for failed tightening decisions. *(Backlog §9 alignment)*
- [ ] Document strategies for substituting live profilers vs. fixture mocks, including environment variable recipes for CI/CD vs. local runs. *(Backlog §9; Guardrails §3)*
- [ ] **Add UAT-users security audit documentation** covering required SQL permissions for FK discovery, inventory validation, and remap script execution. Include least-privilege principal recipes. *(Test Plan §12.1; Guardrails §10)*
- [ ] **Create incident response playbook** for failed UAT-users runs, covering common failures (missing inventory columns, orphan overflow, UAT user exhaustion) with diagnostic SQL queries and remediation steps. *(Architecture Guardrails §8)*

## 7. Quality Gates & Release Packaging
- [ ] Introduce `dotnet format` and Roslyn analyzer enforcement with `TreatWarningsAsErrors` for critical projects. *(Backlog §8; Guardrails §1)*
- [ ] Configure a GitHub Actions matrix (Linux + Windows) that runs the run-checklist commands, publishes CLI artifacts, and uploads telemetry bundles. *(Backlog §8; Test Plan §17.3)*
- [ ] Enable CodeQL and Dependabot (NuGet + GitHub Actions) to keep dependencies current and secure. *(Backlog §8)*
- [ ] Add OSS hygiene files (`LICENSE`, `CONTRIBUTING.md`, `CODEOWNERS`) and link them from the README onboarding section. *(Backlog §8)*
- [ ] Package the CLI as a global tool or self-contained distribution with versioned release notes once the backlog above is green. *(Release readiness)*

## 8. Forward-Looking Enhancements
- [ ] Design delta-only emission (schema diff) capability that reuses the SMO graph but limits output to tightened artifacts, documenting guardrails for drift detection. *(Roadmap continuation)*
- [ ] Explore multi-environment manifest comparisons to support dev/test/prod promotion workflows. *(Backlog §9)*
- [ ] Scope optional remediation-pack generation that translates policy "pre-remediation required" hints into executable SQL bundles behind a feature flag. *(Guardrails §8 roadmap)*
- [ ] Capture open performance and scalability questions (e.g., 200+ entity modules, streaming IO) for future grooming sessions in `notes/backlog-ideas.md`.
- [ ] **Investigate incremental UAT-users processing** that can detect inventory deltas (new/removed users) and emit minimal UPDATE scripts instead of full-catalog rescans, reducing runtime for large FK catalogs. *(Roadmap continuation)*
- [ ] **Design a UAT-users rollback generator** that emits inverse remap scripts to restore original user IDs if post-deployment validation fails, preserving audit trails and enabling safe retries. *(Guardrails §8 roadmap)*

---

## Completed Work (Archived)

The following foundational work has been completed and is enforced by the test suite and architecture guardrails:

- ✅ Unblocked the .NET 9 build by resolving the `System.Index` vs. `Microsoft.SqlServer.Management.Smo.Index` ambiguity in `SmoObjectGraphFactory`. *(Guardrails §5; Test Plan smoke prereq)*
- ✅ Established the export artifact contract separating dynamic full-export payloads from static seed sets with regression tests validating directory structures. *(Guardrails §§3,8; Test Plan §§7.1,13.1)*
- ✅ Implemented SQL INSERT script generator with deduplication, deterministic ordering, and batching hints. *(Guardrails §§5,7; Test Plan §§2.5,8.3,13.1)*
- ✅ Extended extraction service to surface profiling telemetry for full-export with structured logging. *(Guardrails §§6,10; Test Plan §§10.2,17)*
- ✅ Created application-side load harness for replaying generated scripts with performance metrics. *(Guardrails §§7,8; Test Plan §§10.2,13.1)*
- ✅ Normalized UAT-users pipeline to carry strongly typed identifiers (GUID, INT, text) across loaders, contexts, snapshots, and SQL emission. *(Feasibility backlog completion)*
- ✅ Implemented comprehensive user inventory validation with fail-fast error handling. *(docs/verbs/uat-users.md acceptance checklist)*
- ✅ Created matching engine with multiple strategies (case-insensitive-email, exact-attribute, regex) and fallback modes. *(docs/verbs/uat-users.md §§Matching Strategies & Fallbacks)*
- ✅ Integrated UAT-users into full-export workflow with manifest metadata and artifact tracking. *(docs/full-export-artifact-contract.md; FullExportRunManifest)*

---

## Priority Legend

Tasks in **Section 0 (Critical)** are the highest priority for ensuring provable correctness of full-export and uat-users workflows. These must be completed before production deployment to UAT environments.

**Sections 1–8** represent the broader release candidate backlog, organized by functional area. Work within each section should be prioritized based on:
1. Blockers for production deployment (e.g., security validation, SSDT import tests)
2. Operator experience and error handling improvements
3. Performance and scale readiness
4. Forward-looking enhancements

---

*Last Updated: 2025-11-17 (post-full-export and uat-users implementation)*
