# Milestone Gap Review & Execution Blueprint

## Major Gaps Remaining

1. **Model Validation Baseline (Completed / Follow-ups Pending)**
   The new `ModelValidator` enforces required shape, attribute uniqueness, identifier semantics, reference targets, and index integrity with regression coverage tied to the edge-case fixture. The remaining follow-up is to surface the validator inside the ingestion pipeline and extend it with schema/cross-catalog rules so §1.6 warnings and aggregated diagnostics are emitted pre-policy.【F:tasks.md†L14-L18】【F:notes/test-plan.md†L12-L16】

2. **Profiler Pipeline (Mock-First Implementation)**  
   We have fixtures and DTOs, yet no concrete profiler abstraction reading those fixtures or translating raw rows into the immutable profiling types. This is prerequisite work for any tightening decisions because it provides the factual signals (null counts, uniqueness, orphan detection).【F:tasks.md†L20-L24】

3. **Tightening Policy Matrix & Audit Trail**  
   All policy modes are still unimplemented. We need a functional-style decision engine that consumes model metadata plus profiling evidence and emits declarative decisions alongside rationale stacks, honoring feature flags for FK creation, platform indexes, and null budgets.【F:tasks.md†L26-L30】【F:architecture-guardrails.md†L16-L18】

4. **SMO Translators & Golden DDL**  
   No SMO layer exists yet to materialize tables, indexes, and foreign keys based on the policy output. Implementing this translator is mandatory before we can emit SSDT artifacts or concatenated constraint scripts, and the guardrails forbid ad-hoc SQL concatenation.【F:tasks.md†L32-L36】【F:architecture-guardrails.md†L20-L22】

5. **Per-Table SSDT Emission & Concatenated Mode**  
   Without the emission orchestrator we cannot satisfy the pipeline's primary goal of generating ready-to-import DDL. The backlog also calls for a concatenated-constraint option to support consumers that prefer a single apply script.【F:tasks.md†L38-L43】【F:architecture-guardrails.md†L36-L38】

6. **DMM Parity Comparator & CLI Workflows**  
   ScriptDom-based diffing plus CLI verbs (`build-ssdt`, `dmm-compare`) remain stubs. These deliverables ensure we can validate emitted DDL against the existing DMM lens and expose deterministic automation for CI/CD.【F:tasks.md†L45-L55】【F:architecture-guardrails.md†L24-L27】

7. **Quality Gates, CI Harness, and Operational Docs**
   Analyzer enforcement, formatting, and runbook updates are not yet wired. Bringing these online keeps the solution pipeline-ready and aligns with the guardrail emphasis on observability and deterministic execution.【F:tasks.md†L57-L67】【F:architecture-guardrails.md†L24-L35】

8. **SQL Extraction & Evidence Cache Kickoff**
   The backlog now calls for a configurable SQL Server adapter that can pull metadata directly from an OutSystems database while persisting cacheable payloads keyed by module selection and toggle state. No infrastructure or CLI wiring exists yet, so we cannot hydrate fresh JSON/profiling evidence without manual exports.【F:tasks.md†L69-L80】【F:architecture-guardrails.md†L39-L43】

## Execution Blueprint

- **Sequence the Work According to Dependency Flow**
  Follow the layered guardrail (Domain → Profiling → Validation → SMO/DMM → Pipeline → CLI) to avoid leaking lower-level details upward. Implement JSON schema validation first, then the mock-first profiler, policy engine, SMO translators, emission, DMM comparator, and finally CLI enhancements.【F:architecture-guardrails.md†L3-L18】

- **Lean on Fixture-First, Test-Driven Slices**  
  Each milestone should start with the provided fixtures (`tests/Fixtures/model.edge-case.json`, profiler snapshots) to define expected behavior and to build approval tests for emission outputs. Add micro-fixtures (F1–F3) to cover targeted policy behaviors and property tests where outlined in the test plan.【F:architecture-guardrails.md†L12-L18】

- **Keep Feature Flags Centralized and Observable**  
  Extend the tightening configuration as new behaviors appear, and surface decision rationales plus toggle states in manifests and CLI summaries to maintain traceability. This ensures new constraints remain opt-in until backed by telemetry.【F:architecture-guardrails.md†L8-L11】【F:architecture-guardrails.md†L24-L27】

- **Produce Deterministic Artifacts for Every Layer**
  Persist golden SMO scripts and DMM comparison fixtures so refactors are validated through diffs rather than ad-hoc testing. Ensure emission paths are idempotent and ready for CI consumption alongside mock profiler data. As live extraction comes online, extend the same determinism to cache manifests (timestamps, hashes) so downstream steps can prove freshness without re-querying SQL Server on every run.【F:architecture-guardrails.md†L12-L18】【F:architecture-guardrails.md†L39-L43】

- **Document Outcomes as You Land Milestones**  
  Update `tasks.md`, the test plan, and guardrail notes whenever a slice completes to keep the roadmap synchronized with reality and to advertise new coverage to reviewers.【F:tasks.md†L3-L67】【F:architecture-guardrails.md†L24-L35】

By executing the backlog in this order, we stay aligned with the clean architecture boundaries, honor feature-flag discipline, and produce deterministic, test-driven outputs that move the exporter toward SSDT-ready parity.
