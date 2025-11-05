# Feasibility Assessment: Extending `uat-users` for Cross-Environment Remapping

## Executive Summary
- **Goal**: Finish the `uat-users` command so it can ingest a golden data export (including `dbo.[User]`) from Environment A, map the user identities of Environment B onto that snapshot, require operators to reconcile unmapped identities, and emit either (a) a users-aware export bundle or (b) a deterministic `UPDATE` script suitable for replay once non-user tables have been migrated.
- **Feasibility**: High. The existing pipeline already enumerates FK dependencies, produces reconciliation templates, and emits idempotent scripts. Extending it to drive an additional mapping and emission mode builds on established abstractions in `Osm.Pipeline.UatUsers` and can reuse current artifact staging mechanics.
- **Primary Risks**: Identity drift between snapshots, large batch update performance, and ensuring referential integrity when replacing foreign keys. These risks can be mitigated through snapshot fingerprinting, batched script generation, and guardrails that validate mapping completeness before emission.

## Current Capabilities
- `docs/verbs/uat-users.md` describes the command's artifacts: FK catalog, orphan report, mapping template, and guarded apply script for a single environment.
- `src/Osm.Pipeline/UatUsers` already models the user FK discovery, load, and artifact emission steps, producing deterministic outputs under feature-flag control.
- The pipeline design favors fixture-first execution with snapshot reuse, aligning with the new requirement to consume a fully exported dataset instead of querying live SQL when desired.

## Target Use Case
1. Operators export a full dataset (including the `dbo.[User]` table) from Environment A.
2. The `uat-users` command ingests this dataset as the **source baseline**.
3. Environment B's users (potentially with different identifiers) are loaded as the **target catalog**.
4. The command detects missing mappings, forcing human reconciliation via CSV templates.
5. Once reconciled, the command either:
   - Emits updated table data scoped to Environment B's user IDs, ready for bulk import; **or**
   - Generates a batched `UPDATE` script applying FK replacements across all affected tables after the rest of the migration has run.

## Required Enhancements
### 1. Dual-Environment Inputs
- Extend CLI options to accept a source snapshot bundle (model JSON + data exports) and a target user manifest (via SQL connection, CSV, or JSON).
- Validate fingerprints for both environments to prevent mismatched schema/data combinations.

### 2. User Mapping & Reconciliation
- Reuse `PrepareUserMapStep` to produce a baseline mapping template seeded with Environment B user identifiers.
- Add a reconciliation validator that fails the pipeline when required mappings remain blank, preventing partial emission.
- Persist reconciliation state in the snapshot file to allow iterative refinement without reprocessing the entire dataset.

### 3. Data Rewriting Engine
- Introduce a step that traverses all FK-bearing tables in the exported dataset, replacing source user IDs with the reconciled target IDs.
- Ensure lookups are buffered efficiently (e.g., dictionary keyed by source ID) to support large tables.
- Record row-level change manifests for auditing and potential rollback.

### 4. Emission Modes
- **Export Mode**: Serialize transformed tables back to CSV/JSON, mirroring the structure expected by downstream import tooling. Include a companion manifest enumerating changed rows per table.
- **Script Mode**: Generate deterministic `UPDATE` statements grouped in configurable batch sizes (e.g., 500 rows) with `BEGIN TRANSACTION` wrappers and optional retry guards. Reuse `SqlScriptEmitter` conventions to keep formatting and telemetry consistent.
- Provide feature flags so operators can select one or both modes during a run.

### 5. Integrity & Safety Checks
- Before emission, assert that every FK reference in the transformed dataset resolves to a valid Environment B user.
- Compare row counts pre- and post-transformation to guarantee no accidental duplications or deletions.
- Optionally emit dry-run reports that enumerate pending updates without touching data, supporting change review processes.

## Data Flow Proposal
```mermaid
graph TD
    A[Source Export (Env A)] -->|Load tables + users| B[Baseline Snapshot Loader]
    B --> C[User Mapping Resolver]
    D[Env B Users] --> C
    C -->|Validated map| E[FK Replacement Engine]
    E --> F1[Mode A: Transformed Export]
    E --> F2[Mode B: Batched UPDATE Script]
    C --> G[Reconciliation Template]
    E --> H[Change Manifest]
```

## Effort & Dependencies
- **Engineering Effort**: ~2–3 iterations (assuming 1-week sprints) covering CLI extensions, pipeline steps, integration tests with fixtures, and documentation updates.
- **Testing**: Requires fixture datasets representing both environments, golden outputs for export/script modes, and integration tests exercising reconciliation failure paths.
- **Dependencies**: Existing snapshot infrastructure, SQL emission utilities, and CLI telemetry should be leveraged; no new third-party libraries anticipated.

## Risks & Mitigations
| Risk | Impact | Mitigation |
| --- | --- | --- |
| Divergent schemas between environments | Mapping failures | Validate schema fingerprints before processing; abort with actionable error |
| Large update batches causing locks/timeouts | Deployment disruption | Provide configurable batch sizes and optional `WAITFOR DELAY` throttling |
| Partial reconciliations leading to orphaned data | Data inconsistency | Hard fail when unresolved user IDs remain; emit summary reports |
| Misaligned identity types (GUID vs. INT) | Conversion errors | Normalize identifiers during mapping; enforce consistent type metadata |

## Open Questions & Answers
1. **Emission strategy**: The command will always emit full-table replacements so reconciled data can be reviewed holistically. An optional *operator sandbox* mode will cap the output scope (single table, sample rows) to speed debugging or iterative dry runs before generating the authoritative bundle.
2. **Verification & auditability**: The pipeline will derive a verifiable logic chain by comparing row counts pre/post transformation, validating every FK edge, and producing a signed summary manifest that records mapping completeness, checksum digests per table, and the 1:1 source↔target user correlations.
3. **Rollback expectations**: No bespoke rollback scripts are required—the operator contract relies on standard backup/restore procedures if a deployment must be undone.

## Recommended Next Steps
1. Finalize CLI specification (inputs, flags, telemetry additions) with stakeholders.
2. Produce representative dual-environment fixtures to drive TDD.
3. Implement the mapping validator and FK replacement engine with unit coverage.
4. Add emission-mode toggles, golden artifact tests, and documentation updates (`docs/verbs/uat-users.md`).
5. Pilot the enhanced command against a staging pair of environments to validate performance and operator ergonomics.

## Implementation Task List
- [ ] Finalize CLI specification, capturing required inputs, validation rules, and telemetry fields.
- [ ] Build dual-environment fixture assets (schema + data + user catalogs) to drive repeatable tests.
- [ ] Implement mapping validator & FK replacement engine with accompanying unit coverage.
- [ ] Wire emission-mode toggles and author golden artifact tests.
- [ ] Execute a staging pilot and collect operator feedback.

## Task Execution Progress
### Task 1: CLI Specification Draft
- Established core flags and inputs:

  | Option | Description | Required |
  | --- | --- | --- |
  | `--source-export <path>` | Points to the Environment A export bundle containing table data and `dbo.[User]`. | Yes |
  | `--target-users <path\|connection>` | Accepts either a CSV/JSON manifest or connection string to enumerate Environment B users. | Yes |
  | `--sandbox-limit <table[:rows]>` | Enables the operator sandbox mode for scoped outputs during debugging. | No |
  | `--emit-export` / `--emit-script` | Feature toggles controlling which emission modes run. | At least one |
  | `--fingerprint <path>` | Optional override to supply pre-computed schema fingerprints for offline verification. | No |
- Documented telemetry additions: emission mode selection, sandbox scope, total reconciled mappings, checksum per table.

### Task 2: Fixture Definition Blueprint
- Identified fixture structure: paired directories `fixtures/env-a` and `fixtures/env-b` with aligned schema metadata plus CSV payloads for FK-bearing tables.
- Defined user catalog format: normalized columns (`UserKey`, `UserName`, `Email`, `IsActive`) to support deterministic reconciliation regardless of identifier type.
- Planned verification harness: JSON manifest enumerating table hashes to simplify parity assertions across modes.

### Task 3: Mapping Validator & FK Replacement Design Notes
- Validator contract: require coverage of every source user ID discovered in FK traversal; surface missing entries as structured diagnostics consumable by the CLI.
- Replacement engine sketch: stream tables row-by-row, swap user IDs via dictionary lookups, and record change manifests capturing original and substituted IDs for auditing.
- Proposed unit coverage: fixture-driven tests for 1:1 mappings, detection of unmapped users, and assurance that non-user columns remain untouched during transformation.

