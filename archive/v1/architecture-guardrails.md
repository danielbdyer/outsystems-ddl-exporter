# Architectural Guardrails for the OutSystems DDL Exporter

## 1. Preserve Clean, Layered Boundaries
- Treat the solution layout in the README as the canonical dependency graph: Domain → Json/Profiling DTOs → Validation → SMO/DMM → Pipeline → CLI. Keep references one-directional to avoid leaking infrastructure concerns into the domain core. 【F:readme.md†L82-L99】
- Enforce DTO immutability and constructor validation so that downstream orchestrators cannot introduce partially formed aggregates. Lean on functional result types (`Result<T>`) for failures instead of exceptions to keep policies predictable during batch processing. 【F:src/Osm.Domain/Abstractions/Result.cs†L1-L129】
- Back every domain invariant with unit tests that hammer on edge cases (duplicate logical names, case-insensitive column collisions, invalid ordinals) so JSON ingestion and higher layers inherit a hardened contract surface. 【F:tests/Osm.Domain.Tests/EntityModelTests.cs†L1-L120】【F:tests/Osm.Domain.Tests/IndexModelTests.cs†L1-L47】

## 2. Feature Flag Everything That Touches DDL
- Centralize toggles (policy mode, FK creation, platform auto-index inclusion, concatenated-constraint emission) in a configuration object that can be supplied via CLI, environment variables, or JSON files. This mirrors the README guidance on keeping execution deterministic across environments. 【F:readme.md†L38-L63】【F:readme.md†L330-L378】
- Every new hardening behavior should have a guard: start as opt-in and graduate to default-on only after fixtures and telemetry demonstrate safety.

## 3. Deterministic, Fixture-First Pipelines
- Use the provided synthetic `model.json` and profiling snapshots as the truth source for unit and integration tests; ensure CLI verbs accept a `--profile-mock-folder` toggle for deterministic CI runs with no SQL connectivity. 【F:readme.md†L38-L60】【F:tasks.md†L5-L23】
- Persist golden outputs for SMO emission (per-table files and concatenated constraint variants) so refactors surface as diff noise instead of runtime surprises.

## 4. Separation Between Logical Decisions and Physical Emission
- Keep the tightening policy engine pure: it should operate on logical model + profiling evidence and output declarative decisions (per-column nullability, per-index uniqueness, per-reference FK creation). Physical SMO translation consumes those decisions without re-running eligibility logic, enabling headless testing. 【F:readme.md†L253-L318】【F:tasks.md†L24-L47】
- Record policy explanations alongside decisions for auditability; emit them in CLI summaries and manifests to support governance reviews.

## 5. SMO and ScriptDom as the Sole DDL Authorities
- Maintain the "no string concatenation" principle by routing all SQL generation through SMO and ScriptDom, as reinforced in the README. Any helper that manipulates raw SQL must exist purely for assertions or comparisons, never for emission. 【F:readme.md†L8-L12】【F:readme.md†L288-L318】
- Provide adapters that translate feature-flag combinations into SMO scripting options so both per-artifact and concatenated outputs originate from the same object graph.

## 6. Pipelineability and Observability
- Design orchestrators to return structured telemetry (counts of tightened columns, skipped foreign keys, remediation prerequisites). Surface that telemetry both in console output and as JSON artifacts for downstream pipelines. 【F:readme.md†L38-L86】【F:src/Osm.Pipeline/Orchestration/FullExportPipeline.cs†L89-L211】
- Ensure all file emissions (Tables, Indexes, ForeignKeys, concatenated constraints) are idempotent and overwrite-safe, enabling repeated dry runs in CI without manual cleanup.
- Keep the `full-export` pipeline’s execution log authoritative: it should stitch together extraction, profiling, emission, and schema-apply metadata so the manifest/telemetry bundle lists every artifact (model JSON, profile manifest, safe/remediation scripts, telemetry zips) introduced by new features. Any schema or telemetry changes must be reflected in `notes/design-contracts.md` before shipping so consuming teams stay in sync. 【F:src/Osm.Pipeline/Orchestration/FullExportPipeline.cs†L89-L211】【F:notes/design-contracts.md†L1-L74】
- Surface auxiliary stages (e.g., `uat-users`) through the manifest and metadata so downstream automation can locate remediation bundles without parsing console output. Record the artifact roots and preview/apply/catalog files in documentation whenever new stage types appear. 【F:src/Osm.Pipeline/Runtime/FullExportRunManifest.cs†L12-L214】【F:docs/verbs/uat-users.md†L33-L86】

## 7. Scalability to 200+ Entities
- Favor streaming/iterator patterns when reading large models and writing DDL to prevent high memory pressure when dealing with 100+ entities and 100+ static entities. 【F:readme.md†L6-L12】
- Parallelize profiling and emission steps behind feature flags once deterministic baselines are established; expose batch-size knobs to tune performance without code changes.

## 8. Guardrails for Future Extensibility
- Document assumptions (e.g., Ignore delete rules never emit DB FKs unless overrides are set) directly in the policy matrix and manifest outputs so that future contributors understand the default contract.
- Keep remediation scripting optional but pluggable: policies may emit "pre-remediation required" hints that another module can translate into SQL, preserving the DDL pipeline's cleanliness while leaving room for future automation.

## 9. Concatenated Constraint Mode Strategy
- When the "concatenated constraints" flag is enabled, emit a sibling file next to the standard per-artifact files (e.g., `Tables/dbo.OSUSR_ABC_CUSTOMER.full.sql`) that appends indexes, foreign keys, and check constraints in a deterministic order. This satisfies downstream consumers that prefer a single apply script without abandoning modular SSDT defaults. 【F:tasks.md†L48-L51】
- Validate both emission modes in tests to ensure they script from the same SMO objects, preventing drift between per-file and combined outputs.

## 10. SQL Extraction & Evidence Caching Guardrails
- Treat the OutSystems SQL Server connection as an infrastructure concern that lives behind an adapter in the Pipeline layer; surface configuration exclusively through typed options so the domain and policy layers remain persistence-agnostic. 【F:tasks.md†L69-L80】
- Cache initial extraction payloads (model JSON, profiling pivots, DMM exports) using deterministic cache keys composed of module selections, toggle states, and connection metadata so repeat runs can short-circuit remote calls while preserving auditability. Persist cache manifests alongside emitted artifacts. 【F:tasks.md†L69-L80】
- Always offer a `--refresh` or equivalent override that forces re-querying the source database, and record timestamps plus hash digests of cached payloads so downstream ETL stages can prove freshness before emitting SSDT packages. 【F:notes/test-plan.md†L107-L110】

---
These guardrails should keep the codebase modular, feature flaggable, and ready for pipeline automation while supporting aggressive iteration toward the SSDT-first future described in the README.
