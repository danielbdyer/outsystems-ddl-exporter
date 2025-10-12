# OutSystems DDL Exporter – Execution Backlog (Iteration-Driven)

> Purpose: translate the README roadmap into shippable slices that prioritize DDL emission, leverage the provided fixtures/mocks, and keep remediation scripts optional.
>
> Test-plan cross-reference: keep `notes/test-plan.md` in sync—each section below calls out the checklist ranges it satisfies.

## 0. Fixture & Config Foundations
- [x] Check in the provided **edge-case `model.json`** under `tests/Fixtures/model.edge-case.json`; add loader utilities for tests.
- [x] Document a deterministic restore/build/test checklist for local + CI runs (see `notes/run-checklist.md`).
- [x] Store mock profiling outputs (`Columns`, `UniqueCandidates`, `FkReality`) as deterministic JSON/CSV fixtures and expose a fake `IDataProfiler` for unit tests.
- [x] Define default tightening configuration (`EvidenceGated`, FK toggles, emission options) in a strongly-typed options class plus JSON sample under `config/`.
- [x] Extract the Advanced SQL export into `src/AdvancedSql/outsystems_model_export.sql` and link it from the README so the query can be linted and versioned separately from prose.
- 🔗 **Checklist**: unlocks downstream coverage in §1–§5.

## 1. Domain & Contracts Hardening
- [x] Extend domain DTOs to capture all fixture fields (`isPlatformAuto`, `physical.isPresentButInactive`, external db hints, delete rule semantics).
- [x] Introduce profiling DTOs (column stats, uniqueness, FK evidence) and decision result types (per-column NULL, per-index UNIQUE, per-reference FK decision).
- [x] Add a model validator that enforces required shape, attribute uniqueness, identifier rules, reference targets, and index integrity with regression tests derived from the edge-case fixture.
- [x] Publish a boundary contract appendix that documents `IModelProvider`, `IDataProfiler`, `ITighteningPolicy`, `ISmoBuilder`, `IDdlEmitter`, and `IDmmComparator` invariants for contributors. *(Now covers DTO snippets and interface quick reference in `notes/design-contracts.md`.)*
- 🔗 **Checklist**: maps to §1.1–§1.6 (JSON validation rules).

## 2. Profiling Pipeline (Mock-First)
- [x] Implement profiler abstraction returning `ProfileSnapshot`; wire mock implementation that reads from fixture JSON/CSV for test runs.
- [x] Build translators from raw profiler rows → domain profiling DTOs with guard rails (e.g., normalized schema/table casing).
- [x] Cover scenarios: clean unique column, NULL drift, FK with orphans, Ignore delete rule, external entity pass-through. *(Regression: `tests/Osm.Pipeline.Tests/FixtureDataProfilerTests.cs`)*
- 🔗 **Checklist**: enables §2.1–§2.5, §12.1, and performance scenarios in §10.

## 3. Tightening Policy Engine
- [ ] Encode policy matrix for `Cautious`, `EvidenceGated`, `Aggressive` modes focusing on DDL outcomes (NOT NULL, UNIQUE, FK) while flagging—but not generating—remediation SQL.
  - [x] Nullability + FK decisions for PKs, single-column uniques, Protect vs. Ignore delete rules, and aggressive remediation hints based on fixtures F1–F3.
  - [x] Extend coverage to PK tightening, physical NOT NULL enforcement, mandatory + default signals, and null-budget epsilon handling. *(New tests: `PrimaryKeyTighteningTests`, `PhysicalRealityTests`, `MandatoryDefaultTests`, `NullBudgetTests`)*
  - [x] Multi-column uniqueness decisions honor composite evidence, suppress when duplicates are detected, and surface rationale counts in the SSDT manifest summary.
- [x] Implement decision logging/explanations to accompany each decision (inputs: model metadata, profiling evidence, toggle state). *(See `PolicyDecisionReporter` and `DecisionReportTests`.)*
- [x] Provide unit tests using micro-fixtures (F1, F2, F3) verifying policy outcomes under each mode and null budget setting.
- [x] Extract unique index decision evaluation into a dedicated strategy to reduce `TighteningPolicy` complexity and enable reuse in future CLI telemetry refactors.
- 🔗 **Checklist**: covers §3.1–§3.9, §11.1–§11.2, and cross-schema items in §16.

## 4. SMO Object Graph Construction
- [x] Create translators from domain + decisions → SMO `Table`, `Index`, `ForeignKey` objects (no string concatenation).
  - Completed: `SmoObjectGraphFactory` materialises detached SMO objects using naming overrides so tests can assert against real SMO metadata without SQL connectivity.
- [x] Ensure indexes respect `isPlatformAuto` toggle; exclude when configured. *(Unit · [tests/Osm.Smo.Tests/SmoModelFactoryTests.cs](tests/Osm.Smo.Tests/SmoModelFactoryTests.cs); Integration · [tests/Osm.Emission.Tests/SsdtEmitterTests.cs](tests/Osm.Emission.Tests/SsdtEmitterTests.cs))*
- [ ] Validate NOT NULL/UNIQUE/FK flagging via SMO assertions on the edge-case fixture baseline (`tests/Osm.Smo.Tests/SmoModelFactoryTests.cs`).
- [x] Unique index enforcement / suppression flows through SMO definitions and emitted scripts. *(Unit · P1 · [tests/Osm.Smo.Tests/SmoModelFactoryTests.cs](tests/Osm.Smo.Tests/SmoModelFactoryTests.cs), [tests/Osm.Emission.Tests/SsdtEmitterTests.cs](tests/Osm.Emission.Tests/SsdtEmitterTests.cs))*
- [x] Emitters skip inactive or physically retired columns, project logical table/column identifiers into the DDL, and regenerate PK/IX/FK names using PascalCase identifiers to avoid downstream collisions. *(Unit · P1 · [tests/Osm.Smo.Tests/SmoModelFactoryTests.cs](tests/Osm.Smo.Tests/SmoModelFactoryTests.cs); Integration · P1 · [tests/Osm.Etl.Integration.Tests/EmissionPipelineTests.cs](tests/Osm.Etl.Integration.Tests/EmissionPipelineTests.cs))*
- 🔗 **Checklist**: targets §4.1–§4.6 and feeds §15 drift handling once SMO objects are live.

- [x] Emit SSDT-ready files under `out/Modules/<Module>/{Tables,Indexes,ForeignKeys}` honoring deterministic naming conventions.
- [x] Generate `manifest.json` summarizing modules/entities and emitted objects; include policy/toggle snapshot for traceability.
- [x] Author CLI integration test that runs `build-ssdt` against fixtures using the mock profiler and asserts directory structure + key file contents.
- [x] Add optional "concatenated constraints" emission mode that produces a combined DDL file per table (Tables + FKs + Indexes) while keeping the per-artifact layout for default runs.
- 🔗 **Checklist**: executes §5.1–§5.6, §8.1–§8.3, §14.1, and §17.2.

- [x] Build DMM parser layer using ScriptDom to ingest PK-only, NOT NULL baseline scripts.
- [x] Implement diffing between SMO-emitted tables and DMM tables (PK alignment, column nullability).
- [x] Add CLI command `dmm-compare` with JSON + console output, plus tests leveraging synthetic DMM fixtures.
- [x] Expand DMM comparator coverage to report missing/extra tables or columns and primary key drift. *(Unit · P0 · [tests/Osm.Dmm.Tests/DmmComparatorTests.cs](tests/Osm.Dmm.Tests/DmmComparatorTests.cs))*
- [x] Canonicalize ScriptDom types (length/precision/scale) during comparison to eliminate cosmetic drift noise. *(Unit · P1 · [tests/Osm.Dmm.Tests/DmmComparatorTests.cs](tests/Osm.Dmm.Tests/DmmComparatorTests.cs))*
- [x] Support inline `CREATE TABLE` primary key declarations and mixed `ALTER TABLE` batches when parsing DMM scripts. *(Unit · P1 · [tests/Osm.Dmm.Tests/DmmComparatorTests.cs](tests/Osm.Dmm.Tests/DmmComparatorTests.cs))*
- 🔗 **Checklist**: unlocks §6.1–§6.7 and error diagnostics in §9.2.

- [x] Expand CLI verbs (`build-ssdt`, `dmm-compare`) with rich argument parsing, validation, and help text (document mock-profiler flag).
- [x] Surface decision summaries (counts of tightened columns, skipped FKs with reasons) and write an execution log artifact.
- [x] Update README quickstart to reference fixture-driven dry runs and configuration options.
- [x] Fail `dmm-compare` with a non-zero exit code and persisted diff artifact when parity gaps exist. *(Integration · P0 · [tests/Osm.Cli.Tests/CliIntegrationTests.cs](tests/Osm.Cli.Tests/CliIntegrationTests.cs))*
- [x] Honor CLI/environment overrides for tightening toggles, profiler selection, and cache configuration. *(Test Plan §7.3 · Guardrail §2 · [tests/Osm.Cli.Tests/CliIntegrationTests.cs](tests/Osm.Cli.Tests/CliIntegrationTests.cs))*
- [x] Support `--config` plus environment variable overrides for connection strings, profiler selection, and cache roots; document precedence and add a template `config/appsettings.example.json`. *(Test Plan §7.5 · [tests/Osm.Cli.Tests/CliIntegrationTests.cs](tests/Osm.Cli.Tests/CliIntegrationTests.cs); [tests/Osm.Cli.Tests/Configuration/CliConfigurationLoaderTests.cs](tests/Osm.Cli.Tests/Configuration/CliConfigurationLoaderTests.cs))*
- 🔗 **Checklist**: required for §7.1–§7.4, §9.3, §12.1, and §14.1.

## 8. Quality Gates & CI Enablement
- [ ] Establish unit + integration test projects for Validation, SMO, DMM, and Pipeline layers (reuse fixtures and mocks).
  - Progress: added `Osm.Etl.Integration.Tests` to replay the fixture-driven emission pipeline against golden SSDT snapshots and extended CLI integration coverage to diff emitted directories against the curated fixtures (default + rename).
- [ ] Add `dotnet format`/style checks and ensure analyzers are enabled (`TreatWarningsAsErrors` in critical projects).
- [ ] Configure CI pipeline template documenting commands to run (tests, format, CLI smoke against fixtures).
- [ ] Author a GitHub Actions pipeline (Windows + Linux matrix) that runs restore/build/test and publishes CLI fixture artifacts; wire in CI badges once green. *(Test Plan §17.3)*
- [ ] Enable CodeQL scanning and Dependabot (NuGet + GitHub Actions) to keep SMO/ScriptDom dependencies current.
- [ ] Add OSS hygiene files: `LICENSE`, `CONTRIBUTING.md`, `CODEOWNERS`, and link them from the README onboarding section.
- 🔗 **Checklist**: underpins automated execution of §§1–17.

## 9. Operations & Extensibility Follow-ups
- [ ] Document how to substitute live profilers vs. mock fixtures; outline toggle strategy for incremental hardening.
- [ ] Sketch roadmap items: delta-only emission, multi-environment manifests, optional remediation-pack generation.
- [ ] Capture open questions (e.g., handling very large modules, performance tuning) for backlog grooming.
- [ ] Expand `notes/design-contracts.md` with example failure payloads and telemetry guidance so new adapters integrate cleanly.
- 🔗 **Checklist**: keeps future work aligned with §§10–§17 and observability guardrails.

## 10. SQL Extraction & Evidence Cache Kickoff
- [ ] Build a configurable SQL Server extraction adapter that hydrates the OutSystems metadata JSON without coupling the domain layer to ADO.NET primitives; respect Clean Architecture boundaries by routing through the Pipeline layer. *(Progress: fixture-backed `SqlModelExtractionService` + CLI `extract-model` command with manifest-driven executor; live SQL adapter still pending.)*
  - [ ] Implement a concrete `IAdvancedSqlExecutor` that shells `Microsoft.Data.SqlClient` while honoring read-only guardrails. *(Test Plan §12.1 · Guardrail §10)*
  - [ ] Add integration smoke tests that replay a containerized SQL Server or deterministic stub to prove the live adapter wiring. *(Test Plan §18 follow-up)*
- [x] Persist extraction payloads (model JSON, profiling pivots, DMM exports) into a cache directory with deterministic keys derived from module selection, toggle states, and connection metadata so repeated runs can reuse evidence. *(EvidenceCacheService + CLI cache integration)*
- [x] Add module filter options to CLI/config so module selections propagate into emission and cache metadata. *(Integration · P1 · [tests/Osm.Cli.Tests/CliIntegrationTests.cs](tests/Osm.Cli.Tests/CliIntegrationTests.cs); Unit · P1 · [tests/Osm.Pipeline.Tests/ModuleFilterTests.cs](tests/Osm.Pipeline.Tests/ModuleFilterTests.cs))*
- [ ] Add CLI `--connection` flags and typed options that govern live extraction against SQL Server while honoring cache behavior. *(Cache root / refresh flags shipped; connection wiring still pending.)*
- [x] Design cache manifests that record hash digests, timestamps, and provenance for each payload, enabling future ETL stages to verify freshness before emitting SSDT artifacts.
- [ ] Introduce cache eviction/expiry heuristics so obsolete payloads age out once source toggles or modules disappear. *(Test Plan §18 follow-up)*
- [ ] Surface timeout/sampling knobs on the live SQL adapter and mirror them in the cache manifest for auditability. *(Test Plan §18.7)*
- 🔗 **Checklist**: unlocks §7.4, §9.1, §14 matrix coverage, and new caching verification scenarios in §17.
