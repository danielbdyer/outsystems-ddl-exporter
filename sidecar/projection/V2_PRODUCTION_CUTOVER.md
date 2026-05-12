# V2 Production Cutover Plan

**Status:** Draft 2.1 — 2026-05-11. Revised after 6-agent adversarial audit (5 read-only Explore agents + 1 cross-cutting risk audit, run in parallel). Draft 1 commit reference: `2ab3a8a`; Draft 2 commit reference: `5da03c2`. Draft 2 adds the IR-fidelity workstream (Phase A precondition), four new locked-in decisions (D9–D12), four new cross-cutting plan sections (§3.4–§3.7), five new risks (R8–R12), explicit deferral catalog (§10), and a substantially revised unified-config schema sketch (§5.1). Draft 2.1 corrects Q1 framing (no V1 modification; V2 observes its own dataset) and repositions UAT-users from "deferred verb" to "pending evolution doc; expected as Phase A feature."

**Companion documents:** `V2_DRIVER.md` (KPI / phase ladder), `VISION.md` (architectural north star), `STAGING.md` (chapter staging), `HANDOFF.md` (current-session pointer), `DECISIONS.md` (dated decision log).

**Outstanding:** Operator's "document of key evolutions" (referenced 2026-05-11 by the product owner). Draft 3 will absorb it.

---

## 1. Executive Summary

V2 is structurally green on the schema/DDL axis (1,072 tests passing, three Π's wired through `Compose.run`) but functionally constrained in three independent dimensions: (a) its CLI accepts only positional args and threads `Policy.empty` / `Profile.empty` / `UserRemapContext.empty` into emission; (b) **its IR (`Catalog`) cannot yet represent multiple V1 schema concepts** — trigger definitions, sequences, temporal tables, DEFAULT values, computed columns, CHECK constraints, the catalog coordinate of `TableId`, extended properties at four levels, and entity/attribute descriptions; (c) it has no OSSYS metadata extractor or profile probe surface.

The operator's two production use cases — `extract-model` and `full-export` with table-rename and migration-dependency overrides — require V2 to (a) own OSSYS extraction and profiling end-to-end, (b) accept a unified config that drives renames, migration-dependency rows, module selection, and policy axes (with credentials sourced externally), and (c) light up the data emitters and diagnostic emitters currently asleep behind the CLI's hardcoded defaults — and crucially (d) **lift V1's schema-semantic concepts into V2's IR as a Phase A precondition** so that wired emitters can render complete DDL.

Sequence: **Phase A** ships V2's emit half (config + override surface + IR lifts + emitter wiring + static-data and migration-dependency JSON loaders + auto-PK pass + canonical rename order + profile-JSON ingestion) running against V1-produced extraction and profile JSON. This is the *soak path* — V2 emit + V1 extract/profile in parallel, gated on **functional equivalence** (set-equivalence + semantic diff) rather than byte-identity. **Phase B** ports OSSYS extraction and profiling into V2 (`projection extract`, `projection profile`, `projection full-export` subcommands), enabling V2 full independence and V1 sunset.

---

## 2. Use Cases In Scope

### 2.1 `extract-model`
Connect to live OSSYS SQL Server, run the OutSystems metadata queries, write a deterministic snapshot of modules/entities/attributes/references/indexes/triggers/sequences/extended-properties to disk. V2's `CatalogReader` already parses this snapshot's existing fields; V2 must learn to *produce* it and to also carry the fields V2 currently drops at the adapter boundary (see §3.3).

### 2.2 `full-export` with overrides
Chain extract → profile → emit, accepting:
- **Table-rename overrides**: rename source table to target table (both logical `Module::Entity` and physical `schema.table` forms).
- **Migration-dependency overrides**: append specific rows into specific tables, with PKs auto-assigned at emit time as `MAX(SourceOSSYS.Id) + ROW_NUMBER()` baked as literals into the emitted INSERT/MERGE statements.
- **Static-data overrides**: seed-row fixtures (parallel-but-distinct from migration dependencies; see §5.4 vs §5.5).

Outputs needed: SSDT project on disk (per-table .sql + manifest), `.dacpac` binary, static-seed INSERTs, migration-INSERT scripts, decision/opportunity/validation logs.

### 2.3 Explicitly out of scope (see §10 for the full deferral catalog with rationale)
- **Apply / load-harness phases.**
- **UAT-users transformation as a top-level feature.**
- **V1 verbs not in operator's named two use cases.** (dmm-compare, analyze, inspect, policy-explain — operator confirmed no production usage.)
- **`.sqlproj` generation** — operator handles via external SSDT tooling.
- **`SafeScript.sql` / `RemediationScript.sql` emission.**
- **V1-compatible `osm_model.json` re-emission** — V2's `JsonEmitter` emits V2 IR.
- **Telemetry package, evidence-cache directory.**

---

## 3. Current State Audit (synthesized 2026-05-11)

### 3.1 V1 surface (src/)

| Capability | Location | Notes |
|---|---|---|
| CLI verbs | `src/Osm.Cli/` | 8 verbs: extract-model, full-export, build-ssdt, profile, dmm-compare, inspect, analyze, policy explain (+ uat-users behind env flag) |
| OSSYS metadata SQL | `src/AdvancedSql/outsystems_metadata_rowsets.sql` (1,184 LOC) + `outsystems_model_export.sql` (931 LOC) | Parameterized: `@ModuleNamesCsv`, `@IncludeSystem`, `@IncludeInactive`, `@OnlyActiveAttributes`, `@EntityFilterJson` |
| Result-set processors | `src/Osm.Pipeline/SqlExtraction/` | 25 concrete processors; `MetadataSnapshotRunner.cs` (407 LOC) async-stream orchestrator; `MetadataAccumulator.cs` (104 LOC); core extraction ~2,000 C# LOC; full pipeline w/ wiring & error handling ~6,565 LOC |
| Snapshot writer | `src/Osm.Pipeline/SqlExtraction/SnapshotJsonBuilder.cs` (288 LOC) | Writes `osm_model.json` via `Utf8JsonWriter` |
| Profile probes | `src/Osm.Pipeline/Profile/` (~6,000 C# LOC) | **5 query builders**: NullCount, NullRowSample (10-row context), UniqueCandidate, ForeignKeyProbe (orphan counts), ForeignKeyOrphanSample (10-row context). Sampling uniform across probes via `TableSamplingPolicy`. **No MAX(Id) probe** (gap; see Q1). **No distribution/percentile probes.** |
| `MetadataContractOverrides` | `src/Osm.Pipeline/SqlExtraction/` (141 LOC) | **Not** SQL-Server-version-dependent (the audit's prior framing was wrong); handles OSSYS-schema flexibility for optional metadata columns (e.g., `AttributeJson` vs `AttributesJson`). T-SQL itself uses stable 2016+ features. |
| Connector | `src/Osm.Pipeline/Sql/SqlConnectionFactory.cs` (53 LOC) | Microsoft.Data.SqlClient; auth modes, cert trust, access token, app name |
| Module filtering | `src/Osm.Pipeline/ModelIngestion/ModuleFilter.cs` (140 LOC) | Both SQL-side (preemptive) and C#-side (defensive) |
| Override binders | `src/Osm.Pipeline/Application/NamingOverridesBinder.cs:32-119`; `src/Osm.Pipeline/StaticData/StaticEntityDataProviders.cs:11-119`; `src/Osm.Pipeline/Configuration/CliConfigurationLoader.cs:49-171` | Seven scattered input mechanisms (rename-table flag, static-data fixture, --config JSON, circular-deps JSON, dynamic-insert-mode/static-seed-parent-mode/defer-junction-tables flags, UAT user CSV, module filter flags) |

### 3.2 V2 surface (sidecar/projection/)

| Capability | Location | State |
|---|---|---|
| CLI | `src/Projection.Cli/Program.fs:207-230` | Hand-rolled `match argv`; three subcommands `emit`/`deploy`/`canary`; positional args only; **no flags, no config file** |
| Pipeline composition | `src/Projection.Pipeline/Pipeline.fs:176-234` (`Compose.run`) | Threads `Policy.empty` / `Profile.empty` / no-UserRemap to emitters; **hardcoded defaults; no CLI surface for overrides** |
| Emitters wired in CLI | SsdtDdlEmitter, JsonEmitter, DistributionsEmitter | 3 of 11 |
| Emitters built but unwired | DacpacEmitter, DockerImageEmitter, StaticSeedsEmitter, MigrationDependenciesEmitter, BootstrapEmitter, DecisionLogEmitter, OpportunitiesEmitter, ValidationsEmitter, RefactorLogEmitter | All tested; need `Compose.project` wiring + EmissionPolicy gates |
| OSSYS metadata extractor | — | **Absent.** V2's `ReadSide` reads `INFORMATION_SCHEMA` for canary verification, not OSSYS metadata |
| Profile probes | — | **Absent.** `Profile.empty` is used in CLI; type structure (`src/Projection.Core/Profile.fs:538-547`) is rich and ready, but no adapter populates it |
| Detection passes | `src/Projection.Core/Passes/NullabilityPass.fs:194`, `ForeignKeyPass.fs:240`, `UniqueIndexPass.fs:171` | Mature; route to Opportunities via `Code` prefix; **only meaningful with real Profile data** |
| MigrationDependenciesEmitter | `src/Projection.Targets.Data/MigrationDependenciesEmitter.fs:32-56` | Built; emits MERGE; consumes `MigrationDependencyContext`; **no JSON loader, no auto-PK assignment, no per-table file routing** |
| StaticSeedsEmitter | `src/Projection.Targets.Data/StaticSeedsEmitter.fs` | Built; emits MERGE for static-entity rows; **no JSON loader for V1-style static-data fixture** |
| User-FK reflow | `src/Projection.Targets.Data/MigrationDependenciesEmitter.fs:253-330` + `UserFkReflowPass` | Built; consumes `UserRemapContext`; programmatic construction only |

### 3.3 IR-fidelity gap (new in Draft 2)

V2's `Catalog` does not yet carry several schema concepts that V1's `OsmModel` preserves. These are not Phase B concerns — they are correctness bugs that surface as soon as V2 emit is run against a real workload. Each row below is a Phase A.0' deliverable (see §5.2).

| V1 concept | V1 carrier | V2 today | Cutover impact |
|---|---|---|---|
| **Trigger definitions** | `TriggerModel.Definition` (T-SQL text) | `Kind.Modality` may acknowledge trigger existence; Definition text dropped at adapter | Cutover emits incomplete schema; triggers silently disappear from target DB |
| **Sequences** | `OsmModel.Sequences: SequenceModel[]` (StartValue, Increment, Min, Max, Cycle, Cache) | No `Catalog.Sequences` field at all | Apps depending on sequence-generated IDs fail at cutover |
| **Temporal tables** | `EntityModel.Metadata.Temporal: TemporalTableMetadata` | No `Temporal` variant in `ModalityMark` | History-table configuration lost; audit trail broken |
| **DEFAULT values** | `AttributeModel.DefaultValue` | No `DefaultValue` on `Attribute` | Column defaults vanish; subsequent INSERTs miss defaulted data |
| **Computed columns** | `AttributeOnDiskMetadata.isComputed` + `computedDefinition` | No `IsComputed` / `ComputedDefinition` on `Attribute` | Computed-column definitions lost; DDL emission incomplete |
| **CHECK constraints** | `AttributeOnDiskCheckConstraint` (CHECK text) | No `ColumnChecks` carrier on `Kind` | Data validation rules disappear |
| **Catalog (database) coordinate** | `EntityModel.Catalog: string?` | `TableId` is `(Schema, Table)` only | Cross-database FKs silently degrade to same-catalog |
| **ExtendedProperties (4 levels)** | `Module / Entity / Attribute / Index .ExtendedProperties[]` | No carrier at any level | OSSYS-defined metadata, audit fields, custom tags all lost |
| **Description fields** | `Entity / Attribute .Metadata.Description` | No carrier | Operator-visible docstrings vanish; SQL Server extended-property descriptions cannot be emitted |
| **IsExternal / Origin mapping** | `EntityModel.IsExternal: bool` | `Kind.Origin` exists with three cases but mapping from V1's `IsExternal` is unclear in OSSYS adapter | External entities may be emitted as native or vice versa |
| **IsActive flags** | `Module / Entity / Attribute .IsActive` | Module/Attribute IsActive lost; Entity active partially preserved | Inactive schema elements may leak into cutover DDL |

### 3.4 Credentials & Secrets (cross-cutting; new in Draft 2)

V1 supports several auth modes (`SqlAuthenticationMethod` enum: Integrated, SqlPassword, ActiveDirectoryDefault, ActiveDirectoryMSI, AccessToken) per `src/Osm.Cli/Commands/Binders/SqlOptionBinder.cs:14-46`. V2 must preserve all of them at cutover.

**Policy (locked-in per D9):** Connection strings and credentials **do not live in the unified config JSON.** They are sourced from:
- Environment variables (e.g., `OSM_CONNECTION_STRING`, `OSM_ACCESS_TOKEN`).
- A separate non-checked-in connection-config file (e.g., `connection.local.json`) referenced via CLI flag.
- Connection-string keywords for tokenless auth (`Authentication=Integrated`, `Authentication=ActiveDirectoryDefault`).

The unified config JSON is **secret-free by construction.** Config parser does not have a `sql.connectionString` field. If a future evolution needs to bind connection info into a Compose pipeline, it does so by reading the env var or separate file at parser time, not by accepting a JSON property.

**Acceptance criterion** (Phase A.0 exit): config parser has no path to accept a plaintext password or access token. Static analyzer rule (`Projection.Analyzers`) flags any code that reads a `connectionString` property from `Config`.

### 3.5 Observability & Logging (cross-cutting; new in Draft 2)

V1 uses `Microsoft.Extensions.Logging` with structured EventIds and source identifiers. V2 does not have an equivalent contract wired today.

**Policy (locked-in per D10):** V2 ships **its own log format**, not a V1-compat one. The operator updates downstream log-aggregation tooling (CloudWatch Insights / ELK / Splunk queries) as part of the cutover. The plan does not gate on log-format equivalence.

**Implications:**
- V1's `--extract-sql-metadata-out` / `--build-sql-metadata-out` / `--profile-sql-metadata-out` diagnostic JSON outputs are not preserved as a V2 contract. If operator tooling consumes them, it migrates to consuming V2's emitted Decision/Opportunity/Validation JSONs instead.
- V2's logging format will be defined during Phase B.4. Stable structured properties will be documented; the operator's downstream tooling rewrite happens at cutover.

### 3.6 Determinism & Idempotency under user config (cross-cutting; new in Draft 2)

V2 claims byte-determinism (AXIOMS T1). User-supplied overrides (renames, migration-dep rows, module filters) can perturb execution order if not handled canonically.

**Policy (locked-in per D12):** Canonical sort applied to user-supplied collections at config-validation time, before they reach pipeline passes. Specifically:
- `overrides.tableRenames` sorted by canonical source-key form before being threaded into `TableRenamePass` (§5.6).
- `overrides.migrationDependencies` rows iterated in declaration order *within each table*; tables themselves sorted by canonical kind-key.

**Out of scope** (deliberately, per operator decision):
- Idempotency test in Phase A.6 soak (not a gate).
- Graceful concurrent-extraction failure handling in V2 (operator coordinates externally).
- Single-writer documentation as a required deliverable.

### 3.7 CI/CD migration (cross-cutting; new in Draft 2)

V1 is likely invoked in operator's CI/CD workflows, scripts, and IDE integrations. V2 cutover at T+30 (per §7.3) breaks any V1-invocation site that hasn't been migrated.

**Policy (locked-in per D11):** CI/CD inventory and migration is **operator-owned, not a plan deliverable.** The plan documents this as a known risk (R9) and a cutover-day responsibility. The plan does not insert a Phase A.7 audit step.

**Implications:**
- Operator-owned: enumerate V1 invocation sites; map each to a V2 config-file equivalent; cut over before T+30.
- The plan provides the V2 CLI surface specification (§5.3, §6.5) and the unified config schema (§5.1) as the artifacts the operator needs to do the mapping. No additional automation.

---

## 4. Locked-in Design Decisions

These are decisions made during the 2026-05-11 audit collaboration. Each is revisable, but moving any requires explicit reopening in §11 with a new dated entry.

| # | Decision | Rationale |
|---|---|---|
| D1 | **V2 owns OSSYS extraction** (port from V1), not subprocess-shell-out, not permanent V1 dependency. | Required for V1 sunset; clean cutover; F# parity. |
| D2 | **Full V2 independence before cutover**, not partial / coexistence. | Operator workflow expects one tool. |
| D3 | **Single typed config file** for all overrides, replacing V1's seven scattered input mechanisms. CLI accepts `--config <path>` + a small set of common flag overrides. | Reduces operator cognitive load; one schema to learn. |
| D4 | **Phase A first as a soak**, then Phase B. V2 emit runs against V1-extracted JSON during Phase B port for differential testing. | Surfaces emit-side gaps early; validates emit-half against real workloads before extraction-side rewrite lands. |
| D5 | **MigrationDependencies PK assignment**: pre-compute at emit time as `MAX(observedSet.Id) + ROW_NUMBER()`; bake literal IDs into emitted SQL. The "observed set" is whatever records V2 has in hand at PK-assignment time — static-data fixture rows, profile-supplied counts/max, or (Phase B) probe-captured MAX. V1 is not modified. | Deterministic; readable diffs; no deploy-time uncertainty; no V1 dependency. See §9 Q1 for per-phase resolution. |
| D6 | **V2 owns profile probes** against OSSYS DB. | Required for full independence (D2); detection passes need profile data; V1's probe set is portable. |
| D7 | **Apply phase is external.** V2 emits artifacts; operator runs them via DacFx publish / sqlcmd. | Reduces V2 scope; ephemeral `deploy` subcommand stays as dev tooling. |
| D8 | **Tightening as detection, not intervention.** Operator does not configure tightening rules in production; V2's role is to *catch* SQL Server semantic breakers (orphaned FKs, IsMandatory=true + nulls, unique-index dups) and emit them as Opportunities for operator review. | Detection passes already exist; profile data is the missing input; intervention-axis tuning is out of scope. |
| **D9** | **Connection strings + credentials live outside the unified config JSON.** Sourced from env vars or a separate non-checked-in file. Config parser has no path to accept plaintext passwords. | Eliminates accidental-secret-in-git failure mode by construction. See §3.4. |
| **D10** | **V2 ships its own log format.** Operator updates downstream log-aggregation tooling at cutover. V1's `Microsoft.Extensions.Logging` EventId contract is not preserved. | Smallest V2 scope; operator already plans downstream tooling rework. See §3.5. |
| **D11** | **CI/CD inventory and migration is operator-owned.** Plan does not gate on CI cutover. R9 stays as a documented known risk. | Operator already has internal context for invocation sites; plan provides V2 CLI surface as the migration target. See §3.7. |
| **D12** | **Canonical sort on user-supplied collections** (renames, migration-dep rows) at config-validation time. No idempotency test gate; no concurrent-extraction handling. | Sufficient determinism guardrail; avoids over-engineering. See §3.6. |

---

## 5. Phase A — Soak Path (V1 extracts/profiles, V2 emits)

**Goal:** V2 emit produces the full artifact set from V1-extracted `osm_model.json` and V1-captured profile JSON, with config-driven overrides and an IR that carries every V1 schema concept the operator's workload uses. Validate against operator's real workload via differential testing (functional equivalence) before Phase B begins.

### 5.1 A.0 — Unified config schema

**Deliverable:** A typed F# model in `Projection.Pipeline` representing the unified config, plus a JSON parser/validator. Schema documented at `sidecar/projection/docs/config-schema.md`. Note: connection-string and credential fields **deliberately absent** per D9.

**Schema sketch** (Draft 2 revision):

```json
{
  "model": {
    "path": "extracted/osm_model.json",
    "modules": ["AppCore", { "name": "ServiceCenter", "entities": ["User"] }],
    "includeSystemModules": false,
    "includeInactiveModules": false,
    "onlyActiveAttributes": true,
    "validationOverrides": {
      "allowMissingPrimaryKey": ["Module1::Entity1", "Module2::*"],
      "allowMissingSchema": ["Module3::*"]
    }
  },
  "profile": {
    "path": "extracted/profile.json"
  },
  "cache": {
    "root": ".artifacts/cache",
    "refresh": false,
    "ttlSeconds": 7200
  },
  "profiler": {
    "provider": "fixture",
    "mockFolder": null
  },
  "typeMapping": {
    "path": "config/type-mapping.default.json",
    "default": null,
    "overrides": {}
  },
  "overrides": {
    "tableRenames": [
      { "from": { "module": "OldModule", "entity": "OldEntity" }, "to": { "schema": "dbo", "table": "NEW_TABLE" } },
      { "from": { "schema": "dbo", "table": "OSUSR_X_Y" }, "to": { "schema": "dbo", "table": "RENAMED" } }
    ],
    "migrationDependencies": { "path": "overrides/migration-rows.json" },
    "staticData": { "path": "overrides/static-entities.json" },
    "circularDependencies": {
      "allowedCycles": [
        { "tableOrdering": [
          { "tableName": "OSUSR_ORGANIZATION", "position": 100 },
          { "tableName": "OSUSR_USER", "position": 200 }
        ] }
      ],
      "strictMode": false
    }
  },
  "dynamicData": {
    "insertMode": "PerEntity",
    "staticSeedParentMode": "Include",
    "deferJunctionTables": false
  },
  "emission": {
    "ssdt": true,
    "dacpac": true,
    "json": true,
    "distributions": true,
    "staticSeeds": true,
    "migrationDependencies": true,
    "bootstrap": true,
    "decisionLog": true,
    "opportunities": true,
    "validations": true
  },
  "policy": {
    "selection": "IncludeAll",
    "insertion": "SchemaOnly",
    "userMatching": { "strategy": "ByEmail", "fallback": "NoFallback" }
  },
  "output": {
    "dir": "out/"
  }
}
```

**Notes on schema:**
- **No `sql` block.** Connection sources are outside (D9). CLI accepts `--connection-string-env <VAR>` or `--connection-file <path>`.
- **No `tightening` block.** D8 fixes V2 as detection-only; intervention axis stays at engine defaults.
- **Emission block is 10 toggles** (one per emitter). Granularity preserved deliberately during A.0; collapse to semantic groups (`schema` / `data` / `diagnostics`) deferred until operator UX feedback at A.6.
- **`supplementalModels`** intentionally absent — deferred-with-trigger per `HANDOFF.md:74`; document in §10.

**Tasks:**
- Define F# record types in `Projection.Pipeline/Config.fs`.
- `Config.parse : JsonNode → Result<Config, ConfigError>` with structured errors (path + reason).
- `Config.validate : Config → Result<ValidatedConfig, ValidationError>` with file-existence + cross-field consistency checks.
- Static analyzer (`Projection.Analyzers`) rule: forbid `connectionString` properties anywhere in `Config` type tree.
- Schema reference doc at `sidecar/projection/docs/config-schema.md`.
- Property tests: round-trip; structured-error coverage.

**Exit:** Operator can read the schema doc, hand-write a config file, parse it, get a typed `ValidatedConfig` back or a structured error. Static analyzer confirms zero credential paths.

### 5.2 A.0' — IR fidelity lifts (Phase A precondition)

**Deliverable:** V2's `Catalog` carries every schema concept enumerated in §3.3 (or explicitly opts out per documented rationale). This work runs in parallel with A.0; both gate A.1.

**Tasks** (one slice each, each closable via sidecar's chapter-close convention):
- **Trigger lifts**: add `Catalog.Triggers : Trigger list` (or per-Kind list) with `Definition: string`, `IsDisabled: bool`, lineage to source attribute. Adapter `CatalogReader` populates from V1's `TriggerModel`.
- **Sequence lifts**: add `Catalog.Sequences : Sequence list` with StartValue, Increment, Min, Max, IsCycleEnabled, CacheMode. Adapter populates from V1's `OsmModel.Sequences`.
- **Temporal lifts**: extend `ModalityMark` with `Temporal of TemporalConfig` carrying history-schema, history-table, period columns, retention policy.
- **DEFAULT lifts**: add `Attribute.DefaultValue : SqlLiteral option`.
- **Computed-column lifts**: add `Attribute.Computed : ComputedColumnConfig option` with definition text and persistedness.
- **CHECK lifts**: add `Kind.ColumnChecks : ColumnCheck list` with name + CHECK clause text.
- **Catalog-coordinate lift**: extend `TableId` to `{ Catalog: string option; Schema; Table }`; default Catalog to None for current single-DB case.
- **ExtendedProperties lifts**: add to `Module / Kind / Attribute / Index` as `ExtendedProperties: ExtendedProperty list`.
- **Description lifts**: add `Description: string option` to Entity (Kind metadata) and Attribute metadata.
- **IsExternal / Origin mapping audit**: clarify in CatalogReader adapter; add adapter property test ensuring V1 `IsExternal=true → V2 Origin = ExternalViaIntegrationStudio | ExternalDirect`.
- **IsActive lifts**: add `IsActive: bool` to Module / Kind / Attribute (with sensible default `true`).
- For each lift: differential property test against a fixture catalog asserting round-trip preservation.

**Tasks NOT in scope for A.0'** (deferred-with-rationale; surface in §10):
- `OriginalName` (prior attribute names) — renames handled at cutover, not embedded in model.
- `ExternalDatabaseType` — V2's `PrimitiveType` abstraction is intentional per AXIOMS A13.
- `IndexColumnDirection` per-column include vs. key direction — acceptable loss per 2026-05-10 vestigial-fields convention.
- `IsPlatformAuto` index flag — presentation-only.

**Exit:** V2 IR can round-trip every V1 schema concept the operator's workload uses. Adapter property tests cover the lift surface. Emit-side correctness assumption (Phase A.6 diff is meaningful) is now well-founded.

**Estimated effort:** 3-4 weeks (5-7 slices, sidecar's standard chapter cadence).

### 5.3 A.1 — CLI surface upgrade

**Deliverable:** `Projection.Cli` accepts `--config <path>` for `emit`; connection sources via `--connection-string-env <VAR>` or `--connection-file <path>` (the latter unused in Phase A — V2 doesn't connect to OSSYS during Phase A).

**Tasks:**
- Replace hand-rolled `match argv` parser with Argu or extended hand-rolled approach (decide during work; see Q2).
- Wire `--config` to `Config.parse → Config.validate → Compose.runWithConfig`.
- New exit codes: `6` config-parse-error, `7` config-validation-error, `8` connection-source-error, `9` SQL-execution-error (B), `10` profile-probe-failure (B).
- Backward-compat: legacy positional form retained as deprecated shorthand; emits a deprecation warning to stderr.

**Exit:** `projection emit --config example.json` runs end-to-end with the full Phase A emitter set.

### 5.4 A.2 — Emitter wiring under EmissionPolicy gates

**Deliverable:** All built-but-hidden emitters fire from `Compose.project` when their gate is open per config.

**Tasks:**
- Refactor `Compose.project` to accept `ValidatedConfig` rather than hardcoded empties.
- Wire StaticSeeds / MigrationDependencies / Bootstrap (under `emission.staticSeeds` / `migrationDependencies` / `bootstrap`).
- Wire DecisionLog / Opportunities / Validations (under `emission.decisionLog` / `opportunities` / `validations`).
- Wire DacpacEmitter (under `emission.dacpac`).
- Honor `dynamicData.insertMode` (PerEntity vs SingleFile output layout).
- Honor `dynamicData.deferJunctionTables` and `dynamicData.staticSeedParentMode` in DataEmissionComposer.
- Integration tests for each gate (on, off, all-on, all-off).

**Exit:** With config-driven gates, V2 emit can produce: SSDT + dacpac + Static seeds + Migration deps + Bootstrap + DecisionLog + Opportunities + Validations. All from one run.

### 5.5 A.3 — StaticData and MigrationDependencies JSON loaders

**Deliverable:** Two operator-facing JSON formats with corresponding loaders. Static-data fixtures populate static-entity seed rows; migration-dependency rows are appended with auto-assigned PKs.

**StaticData JSON shape** (V1-compat for soak-friendliness):
```json
{
  "tables": [
    { "schema": "dbo", "table": "OSUSR_DEF_CITY", "rows": [
      { "ID": 1, "NAME": "Lisbon", "ISACTIVE": true },
      { "ID": 2, "NAME": "Porto", "ISACTIVE": true }
    ] }
  ]
}
```

**MigrationDependencies JSON shape** (rows omit PK column):
```json
{
  "tables": [
    { "kindKey": { "module": "AppCore", "entity": "Country" }, "rows": [
      { "Code": "US", "Label": "United States" },
      { "Code": "CA", "Label": "Canada" }
    ] }
  ]
}
```

**Tasks:**
- `Projection.Pipeline/StaticDataLoader.fs` — parse + resolve to (schema, table) tuples; validate column existence against Catalog.
- `Projection.Pipeline/MigrationDependencyLoader.fs` — parse + resolve kind keys; validate column existence.
- New pre-emit pass `MigrationDependencyPkAssignmentPass` — given the observed-set MAX (per Q1: profile-supplied if present, else static-data-fixture MAX, else 0) + parsed migration document, produce fully-PK'd `MigrationDependencyContext`. PK assignment edge cases per Q13: identity-overflow detection, deterministic ordering, no-observed-max baseline.
- Wire StaticData → StaticSeedsEmitter input.
- Wire MigrationDependencyContext → MigrationDependenciesEmitter.
- Tests: malformed JSON, unknown table/kind, unknown column, type mismatches, identity overflow, deterministic PK ordering.

**Exit:** Operator-provided JSON for both static-data and migration-dependencies produces correctly-emitted MERGE statements in `out/`.

### 5.6 A.4 — Table-rename plumbing with canonical ordering

**Deliverable:** Config-declared table renames apply to `Catalog` before emitters run. Rename entries canonical-sorted at config-validation time (per D12).

**Tasks:**
- Pre-emit pass `TableRenamePass` in `Projection.Core/Passes/` — takes a rename map + `Catalog`, returns a renamed `Catalog`. Validates: each rename source exists; no collision in targets; SCC membership unchanged (per R11).
- Canonical sort applied to `overrides.tableRenames` list at `Config.validate` time before reaching the pass — guarantees re-running with reordered config produces identical output.
- Support both logical (`Module::Entity`) and physical (`schema.table`) source forms.
- Apply renames *before* topological-order pass.
- Tests: round-trip rename, collision detection, missing-source error, both source forms, **rename-order-invariance property** (random permutations of rename list produce identical output).

**Exit:** A config with `tableRenames` produces SSDT DDL referencing the renamed names everywhere (table defs, FK references, indexes, triggers, manifest). Property test confirms canonical-order invariance.

### 5.7 A.5 — Profile-JSON ingestion + completeness audit

**Deliverable:** Adapter that reads V1's `profile` verb output JSON and hydrates V2's `Profile` type. Required-vs-optional field enumeration with partial-failure semantics (per R12).

**Tasks:**
- Audit V1's profile JSON schema. Enumerate every field. Mark each: required-for-Phase-A-emit, optional-with-fallback, advisory-only.
- New `Projection.Adapters.Osm/ProfileReader.fs` mirroring `CatalogReader.fs` in style.
- Required-field validation at parse time. Optional fields produce structured warnings, not errors.
- Special case: V1 today does not capture `MaxIdentityValue`. Per Q1, this is acceptable: V2's A.3 pass falls back to static-data MAX or 0. ProfileReader treats `MaxIdentityValue` as an optional field; absence produces no warning. Phase B.3 adds the field natively.
- Wire into `Compose.run` when config supplies `profile.path`.
- Tests: round-trip, schema-shift handling, partial-failure (probe timed out, one column missing), full-failure (file absent or unparseable).

**Exit:** With V1-captured profile JSON supplied via config, V2 emit produces decision logs containing orphan-FK warnings and mandatory-null warnings. Required-field contract is documented.

### 5.8 A.6 — Soak: differential testing on functional equivalence (per D-shape decision)

**Deliverable:** A reproducible test rig that runs V2 emit against operator's real workload and compares outputs against V1's outputs on **functional equivalence**, not byte-identity (per operator decision). Agreed differences recorded in §11.2.

**Tasks:**
- Pick a representative fixture (real production model + profile, or the largest existing test fixture).
- Run V1 full-export → capture outputs.
- Run V2 emit on V1's extracted JSON + profile JSON → capture outputs.
- Build a diff harness:
  - SSDT .sql files: byte diff with allowlist for whitespace/formatting; semantic diff (parse + compare AST) on failure.
  - Manifest JSON: semantic diff (key-order-agnostic, structure-aware). V1 `SsdtManifest` shape vs V2 `ArtifactByKind` shape will differ; record the structural divergence as agreed-different.
  - .dacpac: deserialize and compare schema model via DacFx.
  - Static-seed + Migration SQL: byte diff after canonical whitespace.
  - Decision logs / Opportunities / Validations: semantic set-equivalence (V1 staged structure vs V2 per-kind structure; record divergence).
- Triage every divergence: V2 bug, V1 bug (acknowledged), or agreed-different (record in §11.2).
- Fix V2 bugs; document agreed differences.

**Exit:** Functional-equivalence diff is clean (no unexplained divergence; only entries in §11.2). V2 emit is functionally equivalent to V1 build/SSDT/diagnostic emission.

### 5.9 Phase A milestones (Draft 2 revised estimates)

- **A.0 + A.1**: 1-1.5 weeks (config schema + CLI plumbing)
- **A.0'**: **3-4 weeks** (IR fidelity lifts; new in Draft 2 — runs in parallel with A.0/A.1)
- **A.2**: 1 week (emitter wiring; depends on A.0' for emitter inputs)
- **A.3**: 1.5 weeks (two loaders + auto-PK pass + edge-case tests)
- **A.4**: 0.5-1 week (rename pass + canonical-sort + tests)
- **A.5**: 1-1.5 weeks (profile reader + completeness audit)
- **A.6**: 1-2 weeks (soak; +50% buffer per R5)

**Phase A total: 9-12 weeks** for one focused developer. Parallelization (A.0' alongside A.0/A.1, A.5 alongside A.4) trims to **7-9 weeks elapsed**.

---

## 6. Phase B — Full Independence (V2 owns extraction + profiling)

**Goal:** V2 connects to OSSYS SQL Server, extracts metadata, captures profile, and emits artifacts in a single command. V1 is unnecessary.

### 6.1 B.0 — Foundation

**Tasks:**
- Copy `src/AdvancedSql/outsystems_metadata_rowsets.sql` (1,184 LOC) and `outsystems_model_export.sql` (931 LOC) verbatim into `sidecar/projection/sql/`.
- Port `SqlConnectionFactory` to F# in `Projection.Adapters.Sql/Connection.fs` (~50 F# LOC). Auth modes: Integrated, SqlPassword, ActiveDirectoryDefault, AccessToken. Cert trust + application name. Connection-source bound from env-var or separate file per D9.
- Port `SqlConnectionOptions`, `DbCommandExecutor`, `IAdvancedSqlExecutor` interfaces as F# records/functions.

**Exit:** V2 can open a connection to OSSYS SQL Server and execute the metadata SQL script, returning a streaming reader.

### 6.2 B.1 — Result-set binding

**Tasks:**
- Port 25 result-set processors. C# uses inheritance + factory; F# uses a single DU `MetadataResultSet` with one case per result + pattern-match dispatch.
- Port `MetadataAccumulator` to an F# record of lists.
- Port `ResultSetReader` and `ResultSetDescriptorFactory`. Total ~1,500 C# LOC → ~900 F# LOC.

**Risk:** highest in Phase B. The async-stream lifetime management in `MetadataSnapshotRunner.cs:407` is non-trivial. F#'s `task` workflow handles this, but `CommandBehavior.SequentialAccess` semantics must be preserved exactly to avoid memory exhaustion on large catalogs (>1000 entities).

**Exit:** V2 runs `outsystems_metadata_rowsets.sql` against OSSYS and produces a populated `OutsystemsMetadataSnapshot` F# record.

### 6.3 B.2 — Snapshot serialization

**Tasks:**
- Port `SnapshotJsonBuilder` to F# in `Projection.Adapters.Osm/SnapshotWriter.fs` (~300 F# LOC). Direct `Utf8JsonWriter` calls, no reflection.
- Port `SnapshotValidator`.
- Verify output is byte-equivalent (or semantic-equivalent with a documented diff) to V1's `osm_model.json` for at least one large fixture.

**Note:** `MetadataContractOverrides` (141 LOC) handles **OSSYS-schema-flexibility for optional columns**, NOT SQL-Server-version-dependence (Draft 1 mis-framed this). T-SQL queries themselves use stable 2016+ features.

**Exit:** V2 produces a functionally-equivalent `osm_model.json` from a live OSSYS DB.

### 6.4 B.3 — Profile probes (Draft 2 revised estimate)

**Tasks:**
- Port V1's **5 query builders** (Draft 1 said 4; actual count is 5): `NullCountQueryBuilder`, `NullRowSampleQueryBuilder`, `UniqueCandidateQueryBuilder`, `ForeignKeyProbeQueryBuilder`, `ForeignKeyOrphanSampleQueryBuilder`.
- Add `MaxIdentityValueQueryBuilder` (new in V2; not present in V1; needed for Phase A.3 auto-PK; resolves Q1).
- Port `ProfilingQueryExecutor` (672 C# LOC) orchestration.
- Sampling: respect `--sampling-threshold` / `--sampling-size` semantics (uniform across probes per V1).
- Hydrate `Profile` directly; skip JSON middleman in Phase B path.
- Tests: each probe in isolation; full-population end-to-end; partial-failure (one probe timed out, profile still emits with structured warning per R12).

**Exit:** V2 connects to OSSYS, runs the probe set, returns a populated `Profile` record. Detection passes fire with real data.

**Draft 2 estimate:** **3-4 weeks** (was 2-3 in Draft 1). Profiling surface (~6,000 C# LOC) is larger than extraction surface (~2,000 C# LOC core).

### 6.5 B.4 — Orchestration + new CLI subcommands

**Tasks:**
- Add `projection extract --config <path>` subcommand. Connection source resolved per D9 (env var or `--connection-file`).
- Add `projection profile --config <path>` subcommand.
- Add `projection full-export --config <path>` subcommand. Chains extract → profile → emit.
- Define V2's logging format (per D10) — structured properties, event categories. Documented in `sidecar/projection/docs/logging-format.md`. Operator updates downstream tooling.
- Port `ModuleFilter` (defensive post-extraction filter) and `MetadataContractOverrides`.

**Exit:** Operator runs `projection full-export --config production.json` against OSSYS. V2 produces the full artifact set without V1.

### 6.6 Phase B milestones (Draft 2 revised estimates)

- **B.0**: 1 week (foundation)
- **B.1**: 2-3 weeks (result-set binding; highest-risk phase)
- **B.2**: 1 week (serialization + validator)
- **B.3**: **3-4 weeks** (profile probes; revised up from 2-3)
- **B.4**: 1-2 weeks (orchestration + CLI + logging format)

**Phase B total: 8-11 weeks** for one focused developer.

---

## 7. Cutover Criteria

### 7.1 Phase A exit
- All A.0–A.6 deliverables met, including A.0' IR fidelity lifts.
- Functional-equivalence diff vs V1 outputs is clean (only agreed differences in §11.2) on operator's representative workload.
- Config schema doc reviewed and approved by operator.
- Canonical-rename-order property test passing.
- Static analyzer confirms no credential paths in `Config` type tree.
- No known V2-side defects in emit half.

### 7.2 Phase B exit (= full cutover gate)
- V2 `full-export` runs against OSSYS SQL Server and produces functionally-equivalent `osm_model.json` to V1's `extract-model` output.
- V2 profile probes produce functionally-equivalent `Profile` data to V1's `profile` verb output (modulo Q1's MaxIdentityValue, which V2 adds).
- V2 emit outputs functionally-equivalent artifacts (per Phase A criterion).
- V2 logging format documented; operator has updated downstream tooling.
- ≥1 full end-to-end production dry-run completed by operator.
- Cutover-day runbook written (separate document; operator-owned per D11).

### 7.3 V1 sunset
- T+30 days after Phase B exit: V1 enters maintenance mode.
- T+90 days: V1 archived. `src/` becomes read-only reference.

---

## 8. Risk Register

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| R1 | Operator's "document of key evolutions" diverges from current `origin/main`. | High | Medium | Pause Phase A.0 until operator shares the evolutions doc; revise to Draft 3 before coding starts. |
| R2 | V1's profile JSON lacks `MaxIdentityValue` → Phase A.3 auto-PK observes only static-data fixture rows (or starts at 0). | Low | Low | Closed by Q1 resolution: V2 observes its own set; profile-supplied MAX is best-effort, static-data fixture MAX is fallback, starting-at-0 is deterministic baseline. No V1 amendment. |
| R3 | Async stream lifetime in `MetadataSnapshotRunner` port (B.1) hits memory/correctness issues. | Medium | High | Port with `task` workflow + explicit `IAsyncDisposable` handling; integration test against ≥1000-entity catalog. |
| R4 | Byte-equivalence of `osm_model.json` (B.2) cannot be achieved due to JSON output ordering. | Low | Medium | Fall back to functional equivalence per A.6 pattern. |
| R5 | Differential diff (A.6) surfaces deep emit-side divergence requiring substantial V2 rework. | Medium | High | A.6 budget includes +50% buffer for surprises. |
| R6 | Operator's workflow uses V1 capabilities not yet inventoried. | Low (post-Draft-2 audit, operator confirmed two use cases exhaustive) | Medium | Continue to verify during soak; §10 catalog is comprehensive. |
| R7 | OSSYS-side SQL incompatibility in production tenant. | Low | High | `MetadataContractOverrides` handles OSSYS-schema-flexibility; test against operator's actual OSSYS version. |
| **R8** | **IR lifts (A.0') discover unanticipated semantic complexity in V1's domain model.** | Medium | High | Slice per concept; each closable independently per sidecar chapter cadence; if one lift exceeds 1 week, escalate to design review before continuing. |
| **R9** | **CI/CD silent break at T+30** (operator-owned per D11). | High | High (if operator's pipelines depend on V1) | Operator inventories invocation sites during soak; maps each to V2 config-file equivalent; cuts over before T+30. Plan provides V2 CLI spec as migration target; no automation. |
| **R10** | **Logging format divergence** (V2 ships own format per D10). | High | Medium | Operator's downstream tooling rewrite happens at cutover; V2 logging format documented at Phase B.4. |
| **R11** | **Determinism breaks under config-driven rename ordering.** | Low (mitigated by D12) | High (when triggered) | Canonical rename sort at config-validation time (A.4); rename-order-invariance property test. |
| **R12** | **Profile completeness boundary unclear** — partial probe failure produces incomplete profile. | Medium | Medium | A.5 enumerates required-vs-optional fields with structured-warning semantics; partial-failure tests in A.5 + B.3. |

---

## 9. Open Questions

Numbered for stable reference; resolved questions retained for traceability.

**Q1.** **MAX(Id) source for the auto-PK pass (A.3).** **Resolved 2026-05-11**: no V1 modification. V2 observes the highest record in its own set at PK-assignment time. Per phase:
  - **Phase A**: V2's PK-assignment pass observes (in priority order) (i) MAX(Id) for the kind in the V1-supplied profile JSON, if that field is present; (ii) MAX(Id) across static-data fixture rows for the same kind in this run; (iii) starting value 0 if neither is available (PKs begin at 1). The profile-side hit is best-effort — Draft 2.1 does not depend on V1's profile carrying MAX(Id), but if it does, V2 uses it; if not, fallback (ii)/(iii) applies. Operator-supplied explicit PKs in the migration-deps JSON, if present, override auto-assignment for that row.
  - **Phase B**: V2's own profile probe captures `MaxIdentityValue` per identity column natively. PK-assignment uses that authoritatively.
  Implication: A.3 ships in Phase A without a hard dependency on profile field availability. Edge case "no observed max" is documented as deterministic behavior, not an error. **Closed.**

**Q2.** **Argu vs hand-rolled CLI parser (A.1).** Decide during A.1 work. **Outstanding.**

**Q3.** **Backward-compat for V2's existing positional CLI.** Draft 2 default: keep as deprecated shorthand with stderr warning. **Outstanding (revisable during A.1).**

**Q4.** **Profile JSON shape coupling in Phase B.3.** Draft 2: V2-native primary, V1-compat secondary via `--compatibility-mode` flag if operator needs it. **Outstanding (revisable during B.3).**

**Q5.** **Dacpac as primary vs SSDT-folder as primary.** Both required per §2.2. Draft 2: emit both side-by-side in `out/`. **Resolved.**

**Q6.** **Operator's "document of key evolutions."** Outstanding; triggers Draft 3.

**Q7.** **Sampling thresholds in Phase B.3.** Operator's production catalog characteristics determine `--sampling-threshold` and `--sampling-size` defaults. **Outstanding.**

**~~Q8.~~ Credentials handling.** **Resolved per D9** (connection string outside config).

**~~Q9.~~ Logging format contract.** **Resolved per D10** (V2 own format, operator updates downstream).

**~~Q10.~~ Idempotency test for cutover.** **Resolved (declined)** — operator did not require an idempotency-test gate.

**~~Q11.~~ Concurrent extraction safety.** **Resolved (operator-owned coordination per D11).**

**Q12.** **Rename test coverage scope (A.4).** Options: (a) unit tests covering collision/missing-source/topo-preservation only; (b) unit + integration soak test with renames enabled; (c) unit + property test (random rename permutations). Draft 2 default: (c). **Outstanding (revisable during A.4).**

**Q13.** **MigrationDependencies PK edge cases (A.3).** What happens when: (i) MAX(Id) unknown (profile missing field), (ii) MAX(Id) at identity max value (e.g., `2^31 - 1`), (iii) rows reordered between config versions. Each must produce a structured error or deterministic behavior. Draft 2 commits to: (i) structured error pointing to Q1, (ii) structured error with operator-facing message, (iii) deterministic per canonical sort (D12). **Outstanding (revisable during A.3).**

---

## 10. Deferred / Out-of-Scope (operator-confirmed)

These are deliberately not in scope. Operator confirmed during 2026-05-11 audit. If any becomes needed, reopen as a separate plan.

### 10.1 V1 verbs the operator does not use in production
- `dmm-compare` — DMM baseline comparison.
- `analyze` — standalone tightening analyzer.
- `inspect` — model-JSON inspection.
- `policy explain` — policy-decision introspection.

**Note on `uat-users`:** the CLI verb form is **not** deferred. Operator's pending "document of key evolutions" (R1) is expected to expand UAT-users into a more featureful V2 workstream. Until that doc lands, UAT-users scope is held open; see §11.1.

### 10.2 V1 outputs V2 will not produce
- **`.sqlproj`** (SQL Server Database Project file). Operator handles via external SSDT tooling.
- **`SafeScript.sql` / `RemediationScript.sql`**. V2 emits diagnostic JSON only; operator generates remediation SQL externally if needed.
- **V1-compatible `osm_model.json` re-emitter.** V2's `JsonEmitter` emits V2 IR; Phase A.6 diff handles V1's osm_model.json by reading it as the input (Phase A) or producing V2's snapshot (Phase B).
- **`evidence-cache/` directory and manifest.**
- **`telemetry-package.zip`.**
- **UAT-Users artifacts** (user-map CSVs, template, preview, apply script, catalog). Held open pending §11.1.

### 10.3 V1 capabilities deferred-with-trigger
- **`--apply` / `--apply-static-seed-mode` phase.** Operator runs SSDT/dacpac externally via DacFx publish or sqlcmd.
- **`--run-load-harness` + `--load-harness-connection-string` + `--load-harness-report-out`**. Performance probing phase.
- **Per-Catalog Docker parameterization** (deferred-with-trigger per `HANDOFF.md:70`).
- **OSSYS User-kind identification in OSSYS adapter** (deferred-with-trigger per `HANDOFF.md:74`).
- **CSV adapter for ManualOverride / UserMapLoader** (deferred-with-trigger per `HANDOFF.md:75`).
- **`supplementalModels` config block.** Operator does not currently use; deferred-with-trigger.

### 10.4 V2 plan capabilities deferred
- **CI/CD invocation-site inventory and migration.** Operator-owned per D11 (not a plan deliverable).
- **Idempotency-test gate in Phase A.6.** Operator declined.
- **Concurrent-extraction safety** (single-writer assumption, retry handling). Operator coordinates externally.
- **V1 logging-format compatibility.** V2 ships own format per D10.

### 10.5 IR concepts deliberately not lifted in A.0'
- **`OriginalName`** (prior attribute names). Renames are operator-applied at cutover, not embedded in model.
- **`ExternalDatabaseType`** (raw DBMS type string). V2's `PrimitiveType` abstraction is intentional per AXIOMS A13.
- **Per-column `IndexColumnDirection`** (asc/desc, key vs. include). Acceptable loss per 2026-05-10 vestigial-fields convention.
- **`IsPlatformAuto`** index flag (OutSystems-synthesized vs. user-defined). Presentation-only.

---

## 11. Addenda (append-only)

Subsequent audits, decisions, and plan revisions append below. Each entry dated and tagged.

### 11.1 (placeholder) Audit slot for operator's "document of key evolutions"

When operator delivers the evolutions document, append a synthesis subsection here, revise §3 (Current State Audit) as needed, and bump the plan to Draft 3 in the header.

**Operator preview (2026-05-11):** evolution doc will focus primarily on a more effective and full-featured UAT-users command. Expected impact:
- UAT-users moves from "deferred verb form" to a first-class Phase A feature workstream.
- Likely additions to §5 (new Phase A workstream slot for UAT-users), §6 (Phase B may need UAT-related extraction extensions), §10.2 (UAT-Users artifacts no longer deferred), and §10.3 (CSV adapter / UserMapLoader deferred-with-trigger fires).
- §3.3 IR fidelity inventory may grow if the UAT-users evolution requires new Catalog/Profile fields.
Hold §5 reorg and §10 cleanup until evolution doc arrives; do not pre-scope speculatively.

### 11.2 (placeholder) Agreed differences between V1 and V2 outputs

During Phase A.6 soak, any divergences classified as "agreed-different" (rather than "V2 bug") get recorded here with rationale.

Initial entries expected (from cross-cutting audit):
- `manifest.json` shape: V1's `SsdtManifest` (342-field record) vs V2's `ArtifactByKind` structure.
- `decision-log.json` / `opportunities.json` / `validations.json` shape: V1's staged structure (`Stages[].DecisionLog`) vs V2's per-kind structure.
- V1's `osm_model.json` (input to Phase A) vs V2's `JsonEmitter` output (V2 IR shape) — these are intentionally different files, not a diff target.

### 11.3 (placeholder) Per-milestone close notes

As Phase A.0, A.0', A.1, …, B.4 close, append a 5-10 line close note: what shipped, what deferred, what surprised.

### 11.4 (placeholder) Audit log

Track adversarial audits and their findings.

- **2026-05-11 audit (Draft 1 → Draft 2):** Six parallel agents (Explore × 5 + cross-cutting × 1). Findings drove §3.3 IR-fidelity inventory, §3.4–§3.7 cross-cutting sections, D9–D12 decisions, R8–R12 risks, Q8–Q13 questions, §5.2 (A.0') new workstream, §5.5 (A.3 split into static-data + migration-deps loaders), §6.4 (Phase B.3 estimate revision 2-3 → 3-4 weeks; probe count 4 → 5), §10 expanded deferral catalog.
