# OutSystems DDL Exporter – Execution Backlog (Iteration-Driven)

> Purpose: translate the README roadmap into shippable slices that prioritize DDL emission, leverage the provided fixtures/mocks, and keep remediation scripts optional.
>
> Test-plan cross-reference: keep `notes/test-plan.md` in sync—each section below calls out the checklist ranges it satisfies.

## 0. Fixture & Config Foundations
- [x] Check in the provided **edge-case `model.json`** under `tests/Fixtures/model.edge-case.json`; add loader utilities for tests.
- [x] Document a deterministic restore/build/test checklist for local + CI runs (see `notes/run-checklist.md`).
- [x] Store mock profiling outputs (`Columns`, `UniqueCandidates`, `FkReality`) as deterministic JSON/CSV fixtures and expose a fake `IDataProfiler` for unit tests.
- [x] Define default tightening configuration (`EvidenceGated`, FK toggles, emission options) in a strongly-typed options class plus JSON sample under `config/`.
- 🔗 **Checklist**: unlocks downstream coverage in §1–§5.

## 1. Domain & Contracts Hardening
- [x] Extend domain DTOs to capture all fixture fields (`isPlatformAuto`, `physical.isPresentButInactive`, external db hints, delete rule semantics).
- [x] Introduce profiling DTOs (column stats, uniqueness, FK evidence) and decision result types (per-column NULL, per-index UNIQUE, per-reference FK decision).
- [x] Add a model validator that enforces required shape, attribute uniqueness, identifier rules, reference targets, and index integrity with regression tests derived from the edge-case fixture.
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
- 🔗 **Checklist**: covers §3.1–§3.9, §11.1–§11.2, and cross-schema items in §16.

## 4. SMO Object Graph Construction
- [ ] Create translators from domain + decisions → SMO `Table`, `Index`, `ForeignKey` objects (no string concatenation).
  - Progress: offline `SmoTableDefinition`/`SmoColumnDefinition` records now capture the policy outcomes for tests without requiring a live SMO server; real SMO object wiring remains open.
- [ ] Ensure indexes respect `isPlatformAuto` toggle; exclude when configured.
  - Covered today via offline definitions and unit tests; revalidate once full SMO objects are emitted.
- [ ] Validate NOT NULL/UNIQUE/FK flagging via SMO assertions on the edge-case fixture baseline (`tests/Osm.Smo.Tests/SmoModelFactoryTests.cs`).
- 🔗 **Checklist**: targets §4.1–§4.6 and feeds §15 drift handling once SMO objects are live.

- [x] Emit SSDT-ready files under `out/Modules/<Module>/{Tables,Indexes,ForeignKeys}` honoring deterministic naming conventions.
- [x] Generate `manifest.json` summarizing modules/entities and emitted objects; include policy/toggle snapshot for traceability.
- [x] Author CLI integration test that runs `build-ssdt` against fixtures using the mock profiler and asserts directory structure + key file contents.
- [x] Add optional "concatenated constraints" emission mode that produces a combined DDL file per table (Tables + FKs + Indexes) while keeping the per-artifact layout for default runs.
- 🔗 **Checklist**: executes §5.1–§5.6, §8.1–§8.3, §14.1, and §17.2.

- [x] Build DMM parser layer using ScriptDom to ingest PK-only, NOT NULL baseline scripts.
- [x] Implement diffing between SMO-emitted tables and DMM tables (PK alignment, column nullability).
- [x] Add CLI command `dmm-compare` with JSON + console output, plus tests leveraging synthetic DMM fixtures.
- 🔗 **Checklist**: unlocks §6.1–§6.7 and error diagnostics in §9.2.

- [x] Expand CLI verbs (`build-ssdt`, `dmm-compare`) with rich argument parsing, validation, and help text (document mock-profiler flag).
- [x] Surface decision summaries (counts of tightened columns, skipped FKs with reasons) and write an execution log artifact.
- [x] Update README quickstart to reference fixture-driven dry runs and configuration options.
- 🔗 **Checklist**: required for §7.1–§7.4, §9.3, §12.1, and §14.1.

## 8. Quality Gates & CI Enablement
- [ ] Establish unit + integration test projects for Validation, SMO, DMM, and Pipeline layers (reuse fixtures and mocks).
- [ ] Add `dotnet format`/style checks and ensure analyzers are enabled (`TreatWarningsAsErrors` in critical projects).
- [ ] Configure CI pipeline template documenting commands to run (tests, format, CLI smoke against fixtures).
- 🔗 **Checklist**: underpins automated execution of §§1–17.

## 9. Operations & Extensibility Follow-ups
- [ ] Document how to substitute live profilers vs. mock fixtures; outline toggle strategy for incremental hardening.
- [ ] Sketch roadmap items: delta-only emission, multi-environment manifests, optional remediation-pack generation.
- [ ] Capture open questions (e.g., handling very large modules, performance tuning) for backlog grooming.
- 🔗 **Checklist**: keeps future work aligned with §§10–§17 and observability guardrails.

## 10. SQL Extraction & Evidence Cache Kickoff
- [ ] Build a configurable SQL Server extraction adapter that hydrates the OutSystems metadata JSON without coupling the domain layer to ADO.NET primitives; respect Clean Architecture boundaries by routing through the Pipeline layer.
- [ ] Persist extraction payloads (model JSON, profiling pivots, DMM exports) into a cache directory with deterministic keys derived from module selection, toggle states, and connection metadata so repeated runs can reuse evidence.
- [ ] Add CLI flags (`--connection`, `--module-filter`, `--refresh-cache`) and typed options that govern both live extraction and cache behavior.
- [ ] Design cache manifests that record hash digests, timestamps, and provenance for each payload, enabling future ETL stages to verify freshness before emitting SSDT artifacts.
- 🔗 **Checklist**: unlocks §7.4, §9.1, §14 matrix coverage, and new caching verification scenarios in §17.
