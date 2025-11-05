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

## Open Questions
- Should the command support **incremental** updates (only rows changed since last run) or always emit full-table replacements?
- What audit artifacts are required for compliance (e.g., signed manifests, hash digests)?
- Do operators need rollback scripts, or is restoring from backup sufficient?

## Recommended Next Steps
1. Finalize CLI specification (inputs, flags, telemetry additions) with stakeholders.
2. Produce representative dual-environment fixtures to drive TDD.
3. Implement the mapping validator and FK replacement engine with unit coverage.
4. Add emission-mode toggles, golden artifact tests, and documentation updates (`docs/verbs/uat-users.md`).
5. Pilot the enhanced command against a staging pair of environments to validate performance and operator ergonomics.

