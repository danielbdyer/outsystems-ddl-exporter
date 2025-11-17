# OutSystems DDL Exporter – Remaining Backlog (Release Candidate Focus)

> This backlog captures the open scope needed to finish the extraction → tightening → SMO emission pipeline described in the README while honoring the architectural guardrails and the living test plan. Completed work from earlier iterations has been collapsed into guardrail references so we can focus on the remaining shippable increments.

---

## 0. Critical: Full-Export & UAT-Users Verification (Milestone-Based)

### **MILESTONE 1: Export Artifact Verification** *(Proves exports are complete and correct)*

#### M1.1. Full-Export Verification Framework
- [ ] **Implement comprehensive export verification system** that validates artifact integrity end-to-end:
  - Add checksums and fingerprints to `FullExportRunManifest` (per-stage: extraction hash, profile fingerprint, SSDT emission hash) with row-count and entity-count metadata
  - Implement manifest-driven filesystem verification that compares declared artifact lists (`DynamicArtifacts`, `StaticSeedArtifacts`) against actual files, failing fast when files are missing or have unexpected sizes
  - Build export completeness validator that cross-references emitted entity scripts against the model's entity catalog, surfacing missing or orphaned files before SSDT import
  - Emit structured verification report (`export-validation.json`) alongside manifest with pass/fail status, missing files, checksum mismatches, and recommended remediation
  - *(Delivers: Provable export integrity without re-running pipelines)*
  - *(Guardrails §§1,6,8; Test Plan §§4,13.1,17,17.3)*

#### M1.2. Topological Insertion Order Proof & Verification
- [ ] **Implement topological proof generation and constraint verification system**:
  - Extend `EntityDependencySorter` to emit dependency proof artifact (`topological-order-proof.json`) recording every edge (source table, target table, FK constraint name), final ordering, cycle detection results, missing edge diagnostics, and alphabetical fallback metadata
  - Build post-emission FK constraint verifier that parses emitted dynamic insert scripts, extracts table references and INSERT ordering, proves every INSERT appears after all FK dependencies, and fails the build when violations are detected
  - Capture topological metadata in `FullExportRunManifest.Stages[].Artifacts.topologicalProof` with paths to proof artifact, ordering mode, node/edge counts, cycle detection status, and fallback indicators
  - *(Delivers: Provable correct insertion order with auditable proof)*
  - *(Guardrails §§5,6,7,8; Test Plan §§2.5,8.3,13.1,17)*

#### M1.3. Data Integrity Verification (Source-to-Target Parity)
- [ ] **Implement end-to-end data integrity verification proving exported data matches source exactly**:
  - **Source data capture**: Extract row counts, column checksums, and distinct value counts per table from source database (QA), record in `source-data-fingerprint.json` with table name, row count, per-column NULL counts, per-column distinct value counts (for low-cardinality columns), and aggregate checksum per table
  - **Target data validation**: After loading emitted INSERT scripts to target database (UAT staging), extract same metrics, compare against source fingerprint, prove row counts match (no data loss), prove NULL counts match per column (NULL preservation), prove non-transformed columns have identical checksums (1:1 data fidelity), and for transformed columns (user FKs), prove all values map correctly per transformation map
  - **Transformation verification**: For each user FK column, query target values, prove every value either (a) exists in UAT inventory (transformed correctly) or (b) was already in UAT inventory (no transformation needed), prove orphan set was fully transformed, verify no orphans introduced, and emit per-column transformation audit
  - **Comprehensive verification report**: Generate `data-integrity-verification.json` with source/target row count comparison per table, per-column NULL count comparison, non-transformed column checksum comparison (pass/fail), transformed column validation results (all values in UAT inventory), detected discrepancies with row-level detail (for small datasets), and overall pass/fail status
  - *(Delivers: Unfailing confidence in ETL pipeline correctness; proves full-export can replace DMM)*
  - *(Guardrails §§6,8,10; Test Plan §§13.1,18.5)*

#### M1.4. Export Verification Test Coverage
- [ ] **Add comprehensive regression and validation tests for export verification**:
  - Manifest stability tests asserting `DynamicArtifacts`, `StaticSeedArtifacts`, and `Stages` collections remain deterministic across repeated runs with identical inputs
  - Topological order validation tests injecting known FK cycles, missing edges, and self-references, asserting correct warnings (`CycleDetected`, `MissingEdgeCount`, `AlphabeticalFallbackApplied`) while still emitting valid output
  - Checksum verification tests ensuring per-stage hashes remain stable for fixture runs and change appropriately when inputs vary
  - **Data integrity verification tests**: Load fixture data to source database, run full-export, load INSERT scripts to target, verify source-to-target parity using verification framework, assert non-transformed data matches exactly, assert transformed data maps correctly, and prove zero data loss
  - *(Delivers: Confidence in verification system correctness and data integrity validation)*
  - *(Test Plan §§4,8.1,11.1,11.3,13.1)*

---

### **MILESTONE 2: UAT-Users Transformation Guarantees** *(Proves QA→UAT mapping is safe, complete, and in-scope)*

#### M2.1. UAT-Users Verification Framework
- [ ] **Implement comprehensive UAT-users verification and proof system** covering the full pipeline from inventory validation through FK transformation:
  - **Inventory integrity checks**: Validate both QA and UAT CSV files contain all required columns (`Id`, `Username`, `EMail`, `Name`, `External_Id`, `Is_Active`, `Creation_Date`, `Last_Login`), report missing/malformed rows with line numbers, detect duplicate IDs, and fail fast before FK analysis begins
  - **Orphan discovery proof**: Emit `uat-users-orphan-discovery.json` recording the FK catalog (schema, table, column, constraint name), distinct user IDs per column with row counts, orphan detection logic (QA inventory minus UAT allowed set), final orphan set with provenance, query fingerprints, and timestamps for audit trails
  - **Mapping completeness validation**: Extend `ValidateUserMapStep` to prove every orphan has a corresponding `TargetUserId`, every `TargetUserId` exists in UAT inventory, no duplicate `SourceUserId` entries exist, and emit `uat-users-validation-report.json` with pass/fail status per rule (QA coverage, UAT coverage, duplicate detection, orphan completeness), detailed error contexts with line numbers, and recommended fixes
  - **UAT allow-list verification**: Cross-reference final user map against parsed UAT CSV, surface any `TargetUserId` values not present in the `Id` column, and block artifact emission until the map is corrected
  - **FK transformation proof**: Implement snapshot-driven FK value audit capturing distinct user IDs from each catalogued column before transformation (from live DB or snapshot), implement pre-flight NULL preservation check querying each FK column for NULL values and recording counts, extend matching report (`04_matching_report.csv`) with `ValidationStatus` column (`Valid`, `TargetNotInUAT`, `SourceNotInQA`, `OrphanMismatch`), and emit snapshot including NULL counts and distinct ID sets for before/after comparison
  - *(Delivers: Provable in-scope user guarantee with comprehensive validation)*
  - *(Guardrails §§6,8,10; Test Plan §§7.3,8.3,9.1,12.1,17,18.5)*

#### M2.2. Transformation Verification with Unified Logic
- [ ] **Implement transformation verification using unified mapping logic applied at different stages**:
  - **Core verification**: Build transformation map from user mapping CSV, validate every source exists in QA inventory, validate every target exists in UAT inventory, prove no duplicate sources, emit transformation map artifact for audit
  - **Primary (full-export integration)**: Verify pre-transformed INSERT scripts—parse emitted `DynamicData/**/*.dynamic.sql` files, extract INSERT VALUES for user FK columns, prove no orphan IDs appear in emitted data (all transformed to UAT targets or were already in UAT inventory), verify all user FK values exist in UAT inventory, compare row counts between QA source and UAT-ready output (should match; no data loss)
  - **Secondary (standalone verification)**: Generate UPDATE script as independent proof artifact—emit `02_apply_user_remap.sql` using same transformation map, parse emitted UPDATE statements, verify `WHERE ... IN (...)` clauses reference only orphan set, verify `CASE ... WHEN ... THEN` blocks assign only UAT inventory targets, assert `WHERE ... IS NOT NULL` guards present
  - **Cross-validation**: Compare transformation counts between INSERT and UPDATE artifacts, prove user ID coverage matches, verify NULL preservation in both representations
  - Emit unified verification report (`uat-users-verification.json`) with transformation map fingerprint, INSERT script validation results, UPDATE script validation results (when generated), cross-validation status, and pass/fail per verification rule
  - *(Delivers: Single transformation logic with mode-specific application; UPDATE script serves as verification artifact)*
  - *(Guardrails §§5,8; Test Plan §8.3)*
  - *(See: docs/design-uat-users-transformation.md §Unified Transformation Implementation)*

#### M2.3. UAT-Users Integration Test Coverage
- [ ] **Add comprehensive end-to-end integration tests for UAT-users edge cases**:
  - Simulate QA→UAT promotion scenarios: missing UAT users (map contains targets not in UAT inventory), duplicate source mappings (same `SourceUserId` mapped multiple times), orphan exclusions (discovered orphans not in QA inventory), NULL preservation (FK columns with NULLs that must remain NULL), GUID vs. INT identifier mixing (inventory with mixed ID types), inventory column omissions (missing required CSV columns), malformed CSV data (invalid timestamps, encoding issues)
  - Assert pipeline fails gracefully with actionable diagnostics (error messages include line numbers, invalid values, recommended fixes) and verify no artifacts are emitted when validation fails
  - *(Delivers: Confidence in UAT-users failure modes and error handling)*
  - *(Test Plan §§9.1,18.5)*

---

### **MILESTONE 3: Integrated Workflow & Operational Readiness** *(Production-ready full-export + uat-users)*

#### M3.1. Full-Export Manifest Extensions & Combined Verification
- [ ] **Extend `FullExportRunManifest` and implement combined verification step**:
  - Capture UAT-users provenance metadata in `uatUsers.*` namespace: QA/UAT inventory paths, snapshot fingerprints, orphan counts, mapping counts, FK catalog size, matching strategy used, validation report path, proof artifact paths, **transformation mode** (`pre-transformed-inserts` or `post-load-updates`), and `transformationApplied` boolean flag for dynamic insert stage
  - Implement combined verification step running after UAT-users pipeline completes, asserting dynamic insert scripts reference only approved entities (from model catalog), static seed scripts reference approved entities, **pre-transformed INSERT scripts contain only UAT-inventory user IDs** (when `transformationApplied: true`), and emit consolidated proof report (`full-export-verification.json`) aggregating export validation, topological proof, and UAT-users transformation verification results
  - *(Delivers: Single source of truth for full-export + uat-users correctness with transformation mode tracking)*
  - *(Guardrails §§6,8,10; Test Plan §§13.1,17)*
  - *(See: docs/design-uat-users-transformation.md for mode semantics)*

#### M3.2. Load Harness Extension for End-to-End Data Verification
- [ ] **Extend `tools/FullExportLoadHarness` to prove full-export ETL pipeline correctness end-to-end**:
  - **Source data fingerprinting**: Connect to source database (QA), extract row counts per table, calculate per-column checksums for non-transformed columns, record NULL counts per column, capture distinct value counts for low-cardinality columns, and persist source fingerprint to enable post-load comparison
  - **Target data loading and validation**: Load emitted DDL scripts (SafeScript.sql), load static seed scripts (via SSDT post-deployment), load pre-transformed dynamic INSERT scripts (`DynamicData/**/*.dynamic.sql`) to staging database, extract same metrics from target, compare against source fingerprint
  - **Data integrity verification (non-transformed columns)**: For every non-user-FK column, compare source vs. target checksums, prove 1:1 data match, detect any discrepancies with row-level detail, verify NULL counts match exactly, and emit per-column verification results
  - **Transformation verification (user FK columns)**: Query all user FK columns in target, prove every value exists in UAT inventory (no orphans introduced), compare against transformation map to verify correct mapping, prove orphan set was fully transformed, verify NULL preservation (NULL count source = NULL count target), and emit per-column transformation audit
  - **Comprehensive ETL verification report**: Generate `load-harness-full-verification.json` with source/target row count comparison per table (must match exactly), per-column checksum comparison for non-transformed data (pass/fail), per-column transformation validation for user FKs (all in UAT inventory), detected discrepancies (with row-level detail for small datasets), performance metrics (load time, index build time), and overall pass/fail status proving ETL correctness
  - **Optional dual-proof verification**: If UPDATE script was generated for cross-validation, load original QA data to separate staging, replay `02_apply_user_remap.sql`, compare UPDATE-transformed data vs. INSERT-transformed data, prove both approaches yield identical results
  - *(Delivers: Unfailing confidence in full-export ETL pipeline; proves tool can replace DMM with verified data integrity)*
  - *(Guardrails §§7,10; Test Plan §§10.2,13.1,18.5)*
  - *(See: docs/design-uat-users-transformation.md §Verification Strategy)*

#### M3.3. Full-Export Idempotence Tests
- [ ] **Add full-export workflow idempotence and determinism tests**:
  - Execute `full-export --enable-uat-users` twice with identical inputs (same model, profile, inventories, user map), assert manifests are byte-identical (all checksums, metadata, artifact lists stable), assert all emitted artifacts are identical (**DDL scripts, pre-transformed INSERT scripts, proof artifacts**), verify INSERT scripts contain same transformed user FK values across runs, assert verification reports show same pass/fail results, and cover both scenarios: with and without UAT-users enabled
  - *(Delivers: Confidence in reproducible pre-transformed data generation)*
  - *(Test Plan §§8.1,11.1)*

#### M3.4. Verification Contract Documentation
- [ ] **Document verification contracts, proof mechanisms, and recommended workflow**:
  - Update `docs/full-export-artifact-contract.md`: Add sections explaining verification framework (checksums, manifest validation, completeness checking), topological proof artifact schema and interpretation, combined verification report format, **pre-transformed INSERT contract when UAT-users is enabled** (transformation mode metadata, transformationApplied flag semantics, UAT-ready data guarantee), recommended deployment workflow (full-export with pre-transformed INSERTs as primary), optional verification workflow (generate UPDATE script for cross-validation), and SSDT/deployment integration consuming verification artifacts
  - Update `docs/verbs/uat-users.md`: **Lead with recommended approach** (full-export integration with pre-transformed INSERTs), explain standalone mode as verification/legacy migration tool, add decision tree showing when to use each approach, document unified transformation logic with mode-specific application, add section detailing verification rules (inventory checks, orphan discovery, mapping validation, FK transformation proof), proof artifact schemas (`uat-users-orphan-discovery.json`, `uat-users-validation-report.json`, `uat-users-verification.json`), validation failure error messages with examples and fixes, and troubleshooting matrix for common operator mistakes
  - Create operator incident response playbook (`docs/incident-response-uat-users.md`): Cover common failure scenarios (missing inventory columns, orphan overflow, UAT user exhaustion, transformation verification failures in INSERT scripts), provide diagnostic SQL queries to investigate issues (query INSERT scripts for orphan IDs, verify UAT inventory coverage, check NULL preservation), include remediation steps and decision trees, emphasize recommended workflow (full-export integration), and add examples from integration test fixtures
  - Update `docs/full-export-artifact-contract.md` topological section: Explain alphabetical fallback behavior, how to diagnose cycles using proof artifact, remediation steps for operators (breaking cycles, adding missing relationships), and include examples from edge-case fixtures showing cycle detection and fallback
  - **Reference `docs/design-uat-users-transformation.md`** from all documentation to provide architectural context, decision tree, unified transformation logic explanation, and comparison table showing why pre-transformed INSERTs are superior
  - *(Delivers: Operator self-service with clear guidance on recommended approach; UPDATE script positioned as verification tool)*
  - *(Architecture Guardrails §8)*
  - *(See: docs/design-uat-users-transformation.md for transformation architecture and decision tree)*

---

### **MILESTONE 4: Performance & Security Validation** *(Optional for initial production; recommend for scale)*

#### M4.1. Performance Benchmarking
- [ ] **Benchmark verification systems at scale**:
  - Topological sorting: Test models with 1000+ entities and deep FK chains (10+ levels), document worst-case timings and memory usage in `notes/perf-readout.md`, and validate proof artifact generation overhead is acceptable
  - UAT-users FK discovery and transformation: Test datasets with 100+ FK columns and millions of distinct user IDs, measure orphan discovery time, mapping validation time, SQL generation time, and ensure pipeline completes within acceptable SLAs (document in `notes/perf-readout.md`)
  - *(Delivers: Confidence in production scalability)*
  - *(Test Plan §§2.5,10.1,10.2,18.5; Guardrails §7)*

#### M4.2. Security & Permissions Validation
- [ ] **Document and validate security requirements for UAT-users pipeline**:
  - Create security audit documentation (`docs/security-uat-users.md`) covering required SQL Server permissions for FK discovery (metadata access, data profiling), inventory validation (no DB access required), remap script execution (UPDATE permissions on catalogued tables), and provide least-privilege principal recipes for each operation
  - Validate UAT-users pipeline operates correctly under read-only principals during discovery/validation phases and document privilege escalation requirements for apply phase
  - *(Delivers: Production security compliance)*
  - *(Test Plan §12.1; Guardrails §10)*

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

## Milestone-Based Delivery Strategy

**Section 0 (Critical)** is now organized into **4 milestones** for iterative delivery:

### **Milestone 1: Export Artifact Verification** ✦ FOUNDATIONAL ✦
Delivers provable export integrity, correct topological ordering, and end-to-end data parity.
- **4 tasks** (M1.1–M1.4)
- **Outcome**: Full-export generates verifiable proof artifacts; operators can validate exports without re-running pipelines; **source-to-target data integrity verification proves 1:1 data match and correct transformations**
- **Blockers removed**: Can deploy to UAT with confidence in export correctness; **can replace DMM with verified ETL pipeline**

### **Milestone 2: UAT-Users Transformation Guarantees** ✦ CORE SAFETY ✦
Delivers provable in-scope user mapping and lossless FK transformation.
- **3 tasks** (M2.1–M2.3)
- **Outcome**: UAT-users pipeline proves all transformations are safe, complete, and operate only on in-scope data
- **Blockers removed**: Can execute QA→UAT promotion with proof of data integrity

### **Milestone 3: Integrated Workflow & Operational Readiness** ✦ PRODUCTION-READY ✦
Delivers end-to-end verification and operator documentation.
- **4 tasks** (M3.1–M3.4)
- **Outcome**: Full-export + uat-users workflow is production-ready with comprehensive verification, lossless transformation proof, and operator self-service documentation
- **Blockers removed**: Can release to production UAT deployments

### **Milestone 4: Performance & Security Validation** ✦ SCALE & COMPLIANCE ✦
Validates system performance at scale and security compliance.
- **2 tasks** (M4.1–M4.2)
- **Outcome**: System validated for production scale and security requirements
- **Recommendation**: Complete before high-volume or multi-tenant deployments

### **Task Count Reduction**
- **Before**: 29 granular tasks across 5 subsections
- **After**: 13 milestone-based tasks (4 + 3 + 4 + 2) with combined outcomes
- **Benefit**: Each task delivers a working, verifiable system component vs. partial implementations

### **Implementation Preference**
Tasks are designed to **implement-as-provable** rather than "build basic then iterate":
- M1.1 delivers full verification framework (not basic checksums, then later add validation)
- M2.1 delivers complete UAT-users verification (not basic checks, then later add proofs)
- Combined tasks reduce integration overhead and deliver verifiable outcomes faster

### **Dependencies**
- **M1** has no dependencies (foundational)
- **M2** can start in parallel with M1 (independent concerns)
- **M3** depends on M1 and M2 completion (integrates their outputs)
- **M4** depends on M3 (validates production-ready system)

**Sections 1–8** represent the broader release candidate backlog, organized by functional area. Work within each section should be prioritized based on:
1. Blockers for production deployment (e.g., security validation, SSDT import tests)
2. Operator experience and error handling improvements
3. Performance and scale readiness
4. Forward-looking enhancements

---

*Last Updated: 2025-11-17 (reorganized for milestone-based delivery)*
