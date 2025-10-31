# Domain Meta-Model Roadmap

> The target state in `docs/domain-target-state.md` enumerates dozens of records, value objects, and policies. This roadmap condenses that surface area into a single meta-model so every use case speaks the same language. Each pillar below identifies the canonical aggregates, the data they own, and the transformations that move a run from ingestion through release. Subsequent stages describe how we solidify each pillar while preserving the architectural guardrails and backlog alignment.

## Canonical Meta-Model Pillars

### 1. Baseline Schema Graph — _"What is the platform shipping today?"_
- **Aggregates**: `OsmModel`, `ModuleModel`, `EntityModel`, `AttributeModel`, `RelationshipModel`, `IndexModel`, `TriggerModel`, `SequenceModel`, `AttributeOnDiskMetadata`, `RelationshipActualConstraint`.
- **Data contract**: immutable snapshot of OutSystems metadata normalized into domain aggregates plus the `EntityAttributeIndex` identity catalog.
- **Transformations**: JSON DTO → deterministic mappers → aggregate constructors that enforce naming coherence and persist on-disk realities.
- **Secondary interactions**: feeds lookup services (`EntityAttributeIndex`, duplicate detectors) and seeds profiling scope.

### 2. Evidence Ledger — _"What realities have we measured?"_
- **Aggregates**: `ColumnProfile`, `ForeignKeyReality`, `CompositeUniqueCandidateProfile`, `ProfilingSignalEvaluation`, evidence cache manifests.
- **Data contract**: measured null counts, duplicate signals, orphan checks, probe statuses, and cache provenance that correspond one-to-one with baseline attributes and relationships.
- **Transformations**: profiling scheduler → `IAdvancedSqlExecutor` → evidence materialization → cache persistence with drift detection.
- **Secondary interactions**: annotates baseline aggregates, drives decision monotonicity, and exposes telemetry for probe health.

### 3. Decision Ledger — _"What policies now hold?"_
- **Aggregates**: `ColumnIdentity`, `SignalEvaluation`, `NullabilityPolicyDefinition`, `UniquenessPolicyDefinition`, `ForeignKeyPolicyDefinition`, tightening diagnostics, toggle precedence model.
- **Data contract**: rationale trees and policy outcomes for every attribute/index/relationship under each tightening mode, recorded as a stable decision log keyed by `EntityAttributeIndex`.
- **Transformations**: evidence ledger + mode configuration → policy matrix evaluation → decision persistence → telemetry emission (JSON bundles + CLI summaries).
- **Secondary interactions**: surfaces operator insights, drives emission contracts, and enforces guardrail-backed invariants (monotonicity, reproducibility).

### 4. Artifact Fabric — _"What will we emit?"_
- **Aggregates**: `EntityEmissionSnapshot`, `TableArtifactSnapshot`, `TableColumnSnapshot`, `TableIndexSnapshot`, `TableForeignKeySnapshot`, `TableTriggerSnapshot`, SMO graph factories, emission manifests.
- **Data contract**: materialized schema artifacts (SMO + filesystem) that mirror decision ledger outcomes, enriched with profiling and emission metadata.
- **Transformations**: decision ledger → SMO model factory → snapshot comparison → deterministic script writer → SSDT import validation.
- **Secondary interactions**: provides golden outputs for regression tests and release packaging, while feeding telemetry on generated artifacts.

### 5. Run Envelope — _"How do we operate and release it?"_
- **Aggregates**: pipeline orchestration (`Osm.Pipeline`), run configuration (`TighteningOptions`, CLI flag resolution), telemetry DTOs, CI/release packaging assets.
- **Data contract**: configuration lineage, execution manifests, telemetry bundles, operator runbooks, and OSS hygiene artifacts.
- **Transformations**: toggle resolution (CLI → environment → config) → pipeline execution plan → telemetry capture → CI/release automation.
- **Secondary interactions**: enforces guardrails, keeps cache + evidence lifecycles consistent, and exposes the system for observability and compliance.

## Use Case Alignment With the Meta-Model

| Run Stage | Meta-Model Pivot | Aggregate Owners | Transformational Primitives | Observability Hook |
|-----------|------------------|------------------|-----------------------------|--------------------|
| **Ingestion** | Baseline Schema Graph | `Osm.Domain.Model` | Deterministic DTO mappers, duplicate detection, `EntityAttributeIndex` population | Schema registry telemetry, ingestion manifest |
| **Profiling** | Evidence Ledger | `Osm.Domain.Profiling`, cache adapters | Probe scheduling, SQL execution, cache lifecycle, drift detection | Probe status stream, evidence cache manifest |
| **Policy Evaluation** | Decision Ledger | `Osm.Validation.Tightening` | Policy matrix resolution, signal evaluation trees, toggle precedence | Decision bundles, CLI summaries, rationale traces |
| **Emission** | Artifact Fabric | `Osm.Domain.Model.Artifacts`, `Osm.Domain.Model.Emission`, `Osm.Smo` | SMO graph synthesis, snapshot diffing, script emission, SSDT import | Golden artifacts, emission manifests, SMO parity tests |
| **Operations & Release** | Run Envelope | `Osm.Pipeline`, CI assets | Run orchestration, telemetry bundling, packaging, quality gates | CI logs, telemetry bundles, release notes |

This table is the shared lens for all stakeholders: every feature is scoped by identifying which pillar it affects, which aggregates own the change, and which primitive must be extended without violating guardrails.

## Transformational Primitives & Invariants

- **Normalization primitives**: immutable aggregate constructors, identity catalogs, logical/physical naming rules, serialization determinism tests.
- **Evidence primitives**: probe definitions, timeout/sampling knobs, cache eviction heuristics, fixture harness for deterministic baselines.
- **Decision primitives**: policy catalogs, monotonicity assertions, toggle precedence rules, diagnostic serialization for telemetry.
- **Emission primitives**: SMO projection, snapshot comparison, idempotent file writers, SSDT verification, regression goldens.
- **Operational primitives**: pipeline orchestration, telemetry schema definitions, CI matrix templates, analyzer/format enforcement, release packaging scripts.

Each primitive carries explicit invariants (monotonicity, reproducibility, guardrail conformance) that must be proven with automated coverage before the pillar is considered complete.

## Roadmap Sequenced by Pillar Maturity

1. **Stabilize the Baseline Schema Graph**
   - Unblock `EntityAttributeIndex` accessibility and enforce uniform `LangVersion`/nullable settings so every aggregate compiles under .NET 9 (`notes/run-checklist.md`, README updates).
   - Reconcile `tasks.md` with this meta-model lens so the backlog mirrors the canonical aggregate set.
   - Backfill ingestion unit tests validating naming coherence, on-disk metadata fidelity, and DTO ↔ aggregate round-trips.

2. **Complete the Evidence Ledger**
   - Implement `IAdvancedSqlExecutor`, probe scheduler, and cache manifests under Guardrail §10.
   - Stand up fixture-backed and live integration suites that hydrate profiling aggregates and capture cache provenance/telemetry.
   - Introduce drift detection + timeout/sampling overrides, wiring them through CLI/environment/config sources.

3. **Publish the Decision Ledger**
   - Materialize the tightening policy matrix for `Cautious`, `EvidenceGated`, and `Aggressive` modes with table-driven tests.
   - Guarantee monotonicity and toggle precedence via property-based and precedence tests; emit rationale trees as structured telemetry bundles.
   - Document override lineage and diagnostic formats in `notes/design-contracts.md` to align operators and auditors.

4. **Weave the Artifact Fabric**
   - Expand SMO factories so `EntityEmissionSnapshot` / `TableArtifactSnapshot` express every decision (NULL/UNIQUE/FK/index/trigger metadata).
   - Persist golden SMO outputs for edge-case fixtures and assert parity via regression suites (Guardrail §5).
   - Execute SSDT import smokes and idempotence checks to validate downstream consumption.

5. **Fortify the Run Envelope**
   - Instrument pipeline runs with module-level telemetry rollups, cache/evidence lifecycle insights, and packaging metadata.
   - Enforce quality gates (`dotnet format`, analyzers, CodeQL, Dependabot) and publish OSS hygiene + operator runbooks.
   - Package the CLI (global tool or self-contained) with reproducible builds, release notes, and telemetry retention policies.

Each stage yields a demonstrable increment: a new invariant locked in, a telemetry artifact captured, or a guardrail satisfied. Progress should always be evidenced via the run checklist (restore → build → test) plus the relevant telemetry or documentation updates.

---
**Progress Tracking Guidance**

- Anchor backlog entries in `tasks.md` to the meta-model pillar they advance so cross-team coordination stays precise.
- Capture build/test outputs and telemetry bundles for each completed bullet to preserve auditability.
- Use this document as the canonical reference when scoping new work: identify the pillar, enumerate the aggregates/primitives involved, then link acceptance to guardrails and automated coverage.
