# Core Technical Flows and Epistemic Guardrails

> **Purpose.** This document is a cartography of the OutSystems DDL Exporter value stream, tracing how the system converts raw OutSystems metadata into deployable SSDT artifacts. It attends to epistemological commitments at each layer: how knowledge is captured, validated, tightened, and preserved as evidence. Protecting these flows protects the plumbing that makes the exporter trustworthy.

## 1. Purpose & Epistemic Framing

1. The exporter is an epistemic machine: its job is to *know* the schema, to *justify* confidence in tightening decisions, and to *prove* downstream consumers can trust the emitted artifacts. Each stage upgrades the certainty of the knowledge base.
2. The README narrates a five-step value stream (capture, profile, decide, emit, reconcile). This reference expands those steps into nine interlocking flows that must remain intact during refactors.
3. Guardrails from `architecture-guardrails.md` define structural constraints. We cite them explicitly so this document remains a living checklist for safeguarding the epistemic pipeline.

## 2. Flow 1 — Model Extraction

**Scope.** Source metadata is harvested from OutSystems Advanced SQL exports or fixtures, yielding canonical JSON describing entities, attributes, and relationships.

- **Primary orchestrator.** `SqlModelExtractionService` (Pipeline layer) wraps the extraction `IAdvancedSqlExecutor` adapter. Guardrail §10 keeps database I/O confined to Pipeline abstractions.
- **Supporting components.** `ExtractModelPipeline`, `AdvancedSqlExportParser`, fixture loaders under `tests/Fixtures`.
- **Epistemic contract.** Extraction produces *raw facts* about the model without interpreting or tightening. Evidence tags (module, timestamp, source) annotate the payload for later provenance tracking.
- **Failure surface.** Connectivity, authentication, or malformed exports surface as `ValidationError` instances. Errors must be preserved verbatim—never squashed—to prevent epistemic gaps.
- **Guardrails.**
  - §1 (Layer boundaries): domain types (`Osm.Domain.Model`) only materialize after extraction.
  - §3 (Configuration): extraction respects CLI/config toggles for module selection and caching.

**Invariants to protect.** Keep extraction pure (no policy decisions), deterministic (fixtures produce stable JSON), and fully logged.

## 3. Flow 2 — Model Ingestion & Filtering

**Scope.** JSON payloads are hydrated into the domain model (`Osm.Domain.Model`) and filtered by module selection rules.

- **Primary service.** `ModelIngestionService` converts JSON into strongly typed aggregates, invoking validation hooks to accumulate warnings/errors.
- **Telemetry contract.** Each ingestion run records module inclusion/exclusion, schema hash, and provenance (live SQL vs. fixture). These fields must remain stable to avoid cache poisoning.
- **Filtering rules.** Module filters prioritize explicit CLI arguments, then configuration, then defaults—documented precedence protects reproducibility (see backlog item §1).
- **Epistemic framing.** Ingestion is the first *interpretive* step: raw facts become a *candidate world model*. We must maintain transparent validation messages for any data we cannot faithfully hydrate.
- **Guardrails.**
  - §2 (Toggle precedence) ensures deterministic reconciliation of conflicting inputs.
  - §6 (Telemetry) mandates structured logging of ingestion decisions.

**Invariants to protect.** Domain DTOs are immutable snapshots; validation warnings must be preserved for later policy reviews.

## 4. Flow 3 — Profiling as Epistemic Evidence

**Scope.** Profilers sample live databases or fixtures to gather statistical evidence (null counts, value distributions, orphan checks) required by tightening policies.

- **Profiler adapters.** `IFixtureProfiler`, `ISqlProfiler`, and orchestrators under `Osm.Pipeline.Profiling`.
- **Bootstrap sequencing.** Profiling is triggered before any policy decision. The pipeline ensures evidence precedes tightening—no decision without data.
- **Evidence schema.** Profiles include entity/table identifiers, column metrics (nullable counts, max lengths), FK orphan counts, sample sizes, and timestamps.
- **Epistemic contract.** Evidence is tagged with sampling assumptions (full scan vs. sampled). Policies must read these assumptions before upgrading certainty.
- **Guardrails.**
  - §7 (Performance) encourages scalable profiling but never at the expense of completeness without recording the compromise.
  - §8 (Observability) requires logs and manifests that explain profiler configuration.

**Invariants to protect.** Evidence must be reproducible, timestamped, and tied to the exact extraction snapshot to prevent stale decisions.

## 5. Flow 4 — Evidence Caching & Provenance

**Scope.** The `EvidenceCacheCoordinator` mediates reuse of prior extraction and profiling outputs.

- **Cache keys.** Derived from module selections, toggles, profiler configuration, and schema fingerprints. Any new config dimension must join the key to avoid reusing incompatible evidence.
- **Confidence metadata.** Cache entries embed provenance (who captured, when, from which environment) and detection heuristics for drift.
- **Epistemic rationale.** Caching preserves prior knowledge but only when the epistemic state matches current assumptions. Drift detection invalidates knowledge when reality may have changed.
- **Guardrails.**
  - §3 (Configuration) to respect toggles.
  - §6 (Telemetry) to emit cache hit/miss diagnostics.

**Invariants to protect.** Never silently accept stale cache entries; log explicit justification when reuse occurs.

## 6. Flow 5 — Policy Synthesis & Tightening Decisions

**Scope.** `TighteningPolicy` aggregates extraction facts and profiling evidence to decide which constraints (NOT NULL, UNIQUE, FK, CHECK) can be tightened safely.

- **Decision matrix.** Modes (`Cautious`, `EvidenceGated`, `Aggressive`) define evidence thresholds. Future work codifies this matrix in `notes/design-contracts.md`.
- **Input contracts.** Each decision requires explicit evidence artifacts (e.g., `NullAnalysis`, `OrphanReport`). Lack of evidence yields “pre-remediation required” decisions.
- **Telemetry.** Policies log not only the decision but the *rationale chain* (evidence references, thresholds, overrides). This is the epistemic heartbeat.
- **Guardrails.**
  - §4 (Policy determinism) demands deterministic outcomes for identical inputs.
  - §6 (Telemetry) ensures decisions are auditable.
  - §8 (Safety) forbids tightening without positive evidence.

**Invariants to protect.** Decisions must be pure functions of inputs; toggles only affect them through explicit configuration flows.

## 7. Flow 6 — SMO Modeling & SSDT Emission

**Scope.** The emission pipeline transforms tightening decisions into SQL Server Management Objects (SMO) graphs and serializes them into SSDT-compatible artifacts.

- **SMO builders.** `SmoObjectGraphFactory`, `SsdtEmitter`, and related mappers translate domain decisions into SMO tables, columns, indexes, and constraints.
- **Artifact outputs.** SSDT `.sql` files, manifest logs, and optional BCP/batch scripts for static data.
- **Traceability.** Each emitted artifact is annotated with fingerprints (schema version, evidence hash) to tie emitted DDL back to policy decisions.
- **Testing posture.** `Osm.Smo.Tests` and `Osm.Emission.Tests` assert parity between decisions and generated SMO graphs.
- **Guardrails.**
  - §5 (SMO namespace hygiene) prevents collisions (see prior fix for `System.Index`).
  - §8 (Safety) ensures scripts represent trustworthy tightening.

**Invariants to protect.** Emission must be idempotent (reruns do not duplicate scripts) and strictly derived from decisions.

## 8. Flow 7 — Static Data Seeds

**Scope.** When static entities require deterministic seed data, the pipeline emits data scripts alongside schema changes.

- **Execution path.** Branch in emission pipeline conditioned on policy outcomes and configuration toggles.
- **Determinism.** Static data must be emitted in canonical ordering with explicit primary keys to avoid non-deterministic diffs.
- **Epistemic justification.** Seed scripts are evidence-bearing artifacts—they prove that static datasets align with constraints.
- **Guardrails.**
  - §7 (Performance) to batch inserts efficiently.
  - §8 (Safety) to ensure seeds do not contradict tightening decisions.

**Invariants to protect.** Seeds must encode provenance (source module, extraction timestamp) and avoid destructive operations.

## 9. Flow 8 — DMM Parity Lens

**Scope.** The exporter compares emitted SSDT artifacts with Database Model Management (DMM) scripts to ensure parity.

- **Comparator pipeline.** `DmmComparisonService` and supporting parsers analyze differences, generating audit logs.
- **Branching logic.** Handles both SSDT projects and raw DMM scripts, unifying diffs into a common report.
- **Epistemic framing.** Parity reports are *claims about reality*. They document how well our knowledge aligns with downstream expectations.
- **Guardrails.**
  - §6 (Telemetry) for diff artifacts.
  - §8 (Safety) to prevent silent divergence.

**Invariants to protect.** Diff artifacts must include enough context (object name, script path, evidence references) for remediation.

## 10. Flow 9 — CLI & Dependency Graph Orchestration

**Scope.** The CLI orchestrates pipelines, wires dependencies, and exposes toggles.

- **Primary components.** `AddPipeline` module registration, `Osm.Cli` entry point, and dependency injection container.
- **Epistemic glue.** CLI logs must link command invocations to evidence manifests, establishing a chain of custody for every run.
- **Guardrails.**
  - §1 (Layer boundaries) ensures CLI only orchestrates, never reimplements domain logic.
  - §2 (Toggle precedence) to ensure CLI flags override config deterministically.

**Invariants to protect.** Dependency wiring must remain deterministic; new services enter via DI with explicit lifetimes.

## 11. Cross-Cutting Guardrails & Philosophical Commitments

1. **Telemetry as memory.** Every stage leaves a manifest trail. Losing telemetry is equivalent to losing knowledge; treat logs as first-class artifacts.
2. **Deterministic pipelines.** Guardrail §1–§4 collectively ensure the pipeline remains deterministic. Reordering steps risks epistemic regressions.
3. **Evidence-first governance.** Policies only act on evidence captured upstream. This is our epistemic constitution: *no tightening without proof.*
4. **Reproducibility.** Fixtures, cache keys, and manifests make the pipeline reproducible. Without them, claims about schema safety degrade to conjecture.
5. **Human-in-the-loop awareness.** The exporter emits human-readable reports (manifests, diffs). Philosophically, the system augments human stewards, providing transparent reasoning they can audit.

## 12. Operational Checkpoints

- **Before coding.** Run `notes/run-checklist.md` to ensure environment parity; failures (e.g., current `SqlModelExtractionService` build errors) must be documented.
- **Before refactors.** Verify each flow’s invariants via unit/integration tests; add regression coverage when touching guarded surfaces.
- **Before release.** Review telemetry to confirm guardrails remain intact—no tightening decisions without evidence, no emission without traceability.

## 13. Living Document Expectations

- Update this reference when new flows emerge (e.g., delta emission) or when guardrails evolve.
- Link new backlog items to the flow they protect so roadmap discussions remain grounded in epistemic impact.
- Treat deviations from these flows as architectural incidents; escalate via architecture reviews before shipping.

> **Bottom line.** The OutSystems DDL Exporter succeeds when it faithfully captures knowledge, justifies tightening, and emits artifacts with traceable provenance. These flows are the epistemic plumbing—protect them as the minimal, irreplaceable backbone of the system.
