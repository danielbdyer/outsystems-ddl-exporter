# Directory & Concern Map

> Quick reference for the OutSystems DDL Exporter repo layout so we can jump directly to the right project without exploratory `ls` passes. Layer names mirror the guardrails hierarchy (Domain → JSON/Profiling → Validation → SMO/DMM → Pipeline → CLI).

## High-Level Layer Table

| Concern | Primary paths | Highlights | Related tests |
| --- | --- | --- | --- |
| Domain contracts | `src/Osm.Domain/Model`, `src/Osm.Domain/Configuration` | Immutable aggregates (`EntityModel`, `AttributeModel`, `IndexModel`) plus `TighteningOptions` defaults that feed toggles. | `tests/Osm.Domain.Tests` validates invariants and ordinal collision handling. |
| JSON ingestion & profiling DTOs | `src/Osm.Json` | `ModelJsonLoader` + DTO mappers keep fixture inputs deterministic. | `tests/Osm.Json.Tests` ensures model/profiling JSON compatibility. |
| Validation & tightening policy | `src/Osm.Validation/Tightening`, `src/Osm.Validation/Policies` | Toggle snapshots, policy matrices, telemetry shaping. `TighteningToggleSnapshot` centralizes key names. | `tests/Osm.Validation.Tests` cover policy edge cases. |
| SMO / DMM generation | `src/Osm.Smo`, `src/Osm.Dmm` | `SmoModelFactory`, `ConstraintNameNormalizer`, and ScriptDom adapters for emission. | `tests/Osm.Smo.Tests`, `tests/Osm.Dmm.Tests`. |
| Pipeline orchestration | `src/Osm.Pipeline/Orchestration`, `src/Osm.Pipeline/Runtime` | Pipeline bootstrap, extraction (`ExtractModelPipeline`), evidence cache coordination, telemetry writers, manifest definitions. | `tests/Osm.Pipeline.Tests` + integration suites under `tests/Osm.Pipeline.Integration.Tests`. |
| CLI + operator tools | `src/Osm.Cli`, `src/Osm.LoadHarness`, `tools/*` | Entry point verbs, harnesses for SSDT validation and full-export load testing. | `tests/Osm.Cli.Tests`, load harness integration tests. |

## Auxiliary Roots

- `tests/Fixtures` – synthetic `model.json` + profiling payloads for deterministic tests and CLI smokes.
- `docs/verbs` – CLI verb design references; helpful when wiring new command-line flags.
- `notes/*` – living documents (`test-plan.md`, `perf-readout.md`) that should be cross-linked from PRs.
- `schema/*` – SSDT seed artifacts referenced by the emission and load harness pipelines.

## Navigation Clues

- Every folder under `src` is a standalone project referenced by `OutSystemsModelToSql.sln`. Use the project name as the namespace root when searching.
- Pipeline orchestration steps follow the `BuildSsdt*Step` and `*PipelineRequest`/`Result` naming pattern inside `src/Osm.Pipeline/Orchestration`.
- CLI verb wiring lives under `src/Osm.Cli/Commands`; options and defaults usually resolve into `TighteningOptions` before being shipped to the pipeline layer.
- Tests mirror the project names. If you need to add a regression test for a given project, open the matching folder under `tests/`.

Keep this map nearby whenever you need to describe where code lives in status updates or PR summaries.
