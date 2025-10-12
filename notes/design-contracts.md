# Boundary Design Contracts

This appendix complements `architecture-guardrails.md` by describing the executable seams that keep the pipeline composable. Each boundary lists its primary types, the contract it enforces, and the observable error/telemetry surface so new adapters or refactors can plug in with confidence.

---

## Model ingestion (`ModelIngestionService` / `ModelJsonDeserializer`)
- **Input**: File path or `Stream` containing the Advanced SQL JSON payload emitted by [`src/AdvancedSql/outsystems_model_export.sql`](../src/AdvancedSql/outsystems_model_export.sql).
- **Output**: `Result<Osm.Domain.Model.OsmModel>` with fully populated module/entity/attribute/index graphs.
- **Invariants**:
  - Modules preserve export order and logical names; entity and attribute identifiers remain case-sensitive logical names.
  - Physical names (`physicalName`, `db.catalog`, `db.schema`) are optional but, when present, are normalized to uppercase schema/table casing.
  - Rejects payloads with duplicate logical or physical identifiers, dangling references, or malformed index metadata. Validation happens eagerly so downstream policies can assume shape correctness.
- **Failures**: Return `Result.Fail` with error codes `MODEL_JSON_PARSE`, `MODEL_VALIDATION`, or `MODEL_REFERENCE_MISSING`; log the JSON pointer when available.
- **Telemetry hooks**: module/entity counts, export timestamp, and deserializer duration.

## Profiling (`FixtureDataProfiler` / future live `IDataProfiler`)
- **Input**: Connection metadata (or fixture paths), the ingested model, and toggle-driven projections describing required statistics.
- **Output**: `Result<ProfileSnapshot>` containing column null counts, duplicate probes for unique candidates, and FK orphan summaries.
- **Invariants**:
  - Column references are matched on schema + physical table + physical column names; logical names are attached for reporting only.
  - Every statistic is timestamped and carries the sampling strategy so evidence cache keys can incorporate data freshness.
  - When a probe cannot run (permissions, timeout), the snapshot records `profileMissing = true` for that element so the tightening policy can fall back conservatively.
- **Failures**: `PROFILE_EXECUTION` with inner SQL text, or `PROFILE_DESERIALIZATION` when fixture JSON is malformed.
- **Telemetry hooks**: row counts, duplicate group sizes, orphan counts, execution duration per probe category.

## Tightening policy (`TighteningPolicy` / `PolicyDecisionReporter`)
- **Input**: Logical model, `ProfileSnapshot`, and `TighteningOptions` (mode + toggles + thresholds).
- **Output**: `PolicyDecisionSet` summarizing per-column nullability decisions, per-index uniqueness actions, and FK creation flags.
- **Invariants**:
  - Decisions never mutate model state; they are pure functions of inputs.
  - Every decision carries rationale codes (`PK`, `DATA_NO_NULLS`, `DELETE_RULE_IGNORE`, etc.) so emitters and telemetry can reason about outcomes without re-evaluating predicates.
  - Modes (`Cautious`, `EvidenceGated`, `Aggressive`) and toggles (OSIDX inclusion, FK overrides, null budget) must be honored exactly once—no implicit re-computation inside emitters.
- **Failures**: Policy evaluation always succeeds; guardrails use assertions/tests instead of runtime errors. Misconfiguration (e.g., unsupported mode) should be caught during option deserialization.
- **Telemetry hooks**: counts of tightened columns, remediation requirements, suppressed FKs, unique index enforcement, and rationale histograms.

## SMO model construction (`SmoModelFactory` / `SmoBuildOptions`)
- **Input**: Domain model, `PolicyDecisionSet`, and `SmoBuildOptions` (including naming overrides and emission toggles).
- **Output**: Immutable `SmoModel` containing table, column, index, and foreign key definitions ready for SMO scripting.
- **Invariants**:
  - Logical table/column names are projected to emitted objects by default while preserving explicit physical overrides.
  - Physical attributes (schema, catalog, data types) flow through untouched; inactive attributes are pruned entirely.
  - Platform auto-indexes (OSIDX_*) are excluded unless the toggle is explicitly enabled.
  - The builder does not access the filesystem or mutate SMO servers; it only produces detached object graphs for testing.
- **Failures**: Throwing is reserved for programming errors (null arguments). Option validation should occur before invocation.
- **Telemetry hooks**: table/index/foreign-key counts and whether bare-table mode is active.

### SMO object translation (`SmoObjectGraphFactory`)
- **Input**: `SmoModel` definitions and `SmoBuildOptions`.
- **Output**: Detached `Microsoft.SqlServer.Management.Smo.Table` objects suitable for validation/scripting.
- **Invariants**:
  - Respects naming overrides for both primary tables and referenced foreign key targets.
  - Populates identity settings, nullability, index metadata (unique/primary key flags, included columns), and FK trust flags exactly as present in `SmoModel`.
  - Reuses a deterministic in-memory `Server`/`Database` so tests do not require SQL connectivity.
- **Failures**: Programming errors (`ArgumentNullException`) when definitions/options are missing. Callers should validate DTO shape before translation.
- **Telemetry hooks**: Count of translated tables and which catalogs were materialised (for future logging scenarios).

## SSDT emission (`SsdtEmitter` / `SsdtManifest`)
- **Input**: `SmoModel`, emission directory, `SmoBuildOptions`, and `PolicyDecisionReport`.
- **Output**: Single-file SSDT artifacts per table (CREATE TABLE + inline defaults/FKs/indexes + extended properties), `manifest.json`, and `policy-decisions.json`.
- **Invariants**:
  - Emission is idempotent: running twice with identical inputs overwrites existing files without side effects.
  - File names are derived from logical identifiers unless a naming override is present.
  - Manifest snapshots include toggle states (`IncludePlatformAutoIndexes`, `EmitBareTableOnly`) plus policy telemetry to support PR automation.
- **Failures**: Raise `IOException` derivatives only when the target path is unreachable; callers should surface user-friendly messages in the CLI.
- **Telemetry hooks**: counts of written files per module, output directory, and elapsed emission time.

## DMM comparator (`DmmComparator`)
- **Input**: Intended model (from SMO/emitter) and parsed DMM SQL (`ScriptDom`).
- **Output**: `DmmComparisonResult` enumerating parity successes and structured drift (missing tables, column nullability, PK mismatch, column order differences).
- **Invariants**:
  - Comparison normalizes identifiers case-insensitively but preserves schema boundaries.
  - Column comparisons operate on logical names; physical names are included in messages for operator clarity.
  - Future enhancements will canonicalize data types and handle inline PK syntax; keep tests ready for both variations.
- **Failures**: `DMM_PARSE_ERROR` surfaces ScriptDom diagnostics (file + line/column). Comparison itself is tolerant—missing artifacts are reported, not treated as hard failures.
- **Telemetry hooks**: counts of drift categories plus summarized text suitable for PR comments.

## Evidence cache & SQL extraction (`EvidenceCacheService`, `SqlModelExtractionService`)
- **Input**: Typed command options (module list, cache root, refresh flag, connection metadata) and fixture/live SQL executors.
- **Output**: Cached payload manifests, hydrated model JSON, and extraction logs describing provenance.
- **Invariants**:
  - Cache keys combine module filters, toggle states, and connection fingerprint; cached payloads must not be reused when any dimension changes.
  - Every cache entry includes SHA256 digests, row counts, and timestamps; refresh operations always append a new manifest entry.
  - Live SQL executors respect timeout and read-only toggles once implemented; fixtures simulate identical telemetry so CLI smoke tests remain deterministic.
- **Failures**: `CACHE_IO_ERROR`, `CACHE_SIGNATURE_MISMATCH`, or `EXTRACTION_NOT_SUPPORTED` (until the live adapter ships).
- **Telemetry hooks**: cache hits vs. misses, extraction durations, and reason strings when refresh overrides are invoked.

---

Keep this document synchronized with the code by updating it whenever an interface gains a new field, failure code, or toggle. Tests in `notes/test-plan.md` reference these contracts to ensure guardrails stay enforceable.

---

## Interface quick reference

| Boundary | Inputs | Outputs | Failure codes | Notes |
| --- | --- | --- | --- | --- |
| `IModelProvider` (pipeline ingestion) | Model path, module filter, supplemental entity hints | `Result<OsmModel>` | `pipeline.buildSsdt.model.missing`, `model.validation.*` | Consumers receive a fully validated domain graph; supplemental material must already satisfy identifier rules. |
| `IDataProfiler` (`FixtureDataProfiler`, future SQL) | `OsmModel`, profiling options (`SqlProfilerOptions` or fixture manifest) | `Result<ProfileSnapshot>` | `pipeline.buildSsdt.profile.path.missing`, `profile.capture.*` | Snapshot rows are keyed by schema/table/column physical identifiers and carry timestamps + sampling metadata. |
| `ITighteningPolicy` | `OsmModel`, `ProfileSnapshot`, `TighteningOptions` | `PolicyDecisionSet` | _none (pure function)_ | All consumers must treat results as immutable decisions and respect rationale codes for telemetry. |
| `ISmoBuilder` (`SmoModelFactory` + `SmoObjectGraphFactory`) | Domain model, policy decisions, `SmoBuildOptions` | `SmoModel` definitions and detached SMO `Table` objects | _programming errors only_ | Definitions remain the canonical interchange; detached tables are for validation/scripting that requires SMO types. |
| `IDdlEmitter` (`SsdtEmitter`) | `SmoModel`, emission directory, `SmoBuildOptions`, decision summaries | `SsdtManifest`, file system artifacts | `pipeline.buildSsdt.output.missing`, IO errors | Emits per-table artifacts plus concatenated optional outputs without mutating the model. |
| `IDmmComparator` (`DmmComparator`) | Emitted SMO projections, parsed DMM ScriptDom tree | `DmmComparisonResult` | `dmm.parse.*` | Comparison normalises types and identifier casing while preserving schema separation for drift reporting. |

### DTO snippet glossary
- **`ProfileSnapshot.ColumnProfile`** – `{ schema: string, table: string, column: string, nullFraction: decimal, rowCount: long }` (nullable values require the tightening policy to honour the configured null budget).
- **`PolicyDecision.ColumnDecision`** – `{ coordinate: ColumnCoordinate, makeNotNull: bool, rationale: string[] }` (coordinates use logical names when available; `makeNotNull=false` does _not_ imply allow nulls unless the source model already permits it).
- **`SmoForeignKeyDefinition`** – `{ name: string, column: string, referencedSchema: string, referencedTable: string, referencedLogicalTable: string, deleteAction: ForeignKeyAction, isNoCheck: bool }` (referenced table name is rewritten via naming overrides during SMO translation).
