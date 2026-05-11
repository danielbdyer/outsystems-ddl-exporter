# V2 Production Cutover Plan

**Status:** Draft 1 — initial plan established 2026-05-11 via collaborative audit with the product owner. Stable basis for iteration; further audits append below as sections under "Addenda". This document supersedes ad-hoc cutover speculation in `HANDOFF.md` and the various `CHAPTER_*_OPEN.md` files; those remain authoritative for their slice scope, this document is authoritative for *production readiness as a deliverable*.

**Companion documents:** `V2_DRIVER.md` (KPI / phase ladder), `VISION.md` (architectural north star), `STAGING.md` (chapter staging), `HANDOFF.md` (current-session pointer), `DECISIONS.md` (dated decision log).

---

## 1. Executive Summary

V2 is structurally green on the schema/DDL axis (1,072 tests passing, three Π's wired through `Compose.run`) but functionally constrained: its CLI accepts only positional args and threads `Policy.empty` / `Profile.empty` / `UserRemapContext.empty` into emission. Eleven emitters are built and tested, three fire from the CLI. The OSSYS metadata extractor — V1's ~2,115 LOC of T-SQL plus ~6,565 LOC of C# pipeline — does not exist in V2 at all; V2 today consumes V1's published `osm_model.json` as its left-hand boundary.

The operator's two production use cases — `extract-model` and `full-export` with table-rename and migration-dependency overrides — require V2 to (a) own OSSYS extraction and profiling end-to-end, (b) accept a unified config file that drives renames, migration-dependency rows, module selection, and policy axes, and (c) light up the data emitters and diagnostic emitters currently asleep behind the CLI's hardcoded defaults.

Sequence: **Phase A** ships V2's emit half (config + override surface + emitter wiring + migration-dependency JSON loader + auto-PK pass + profile-JSON ingestion) running against V1-produced extraction and profile JSON. This is the *soak path* — V2 emit + V1 extract/profile in parallel, with differential testing against V1's full output set. **Phase B** ports OSSYS extraction and profiling into V2 (`projection extract`, `projection profile`, `projection full-export` subcommands), enabling V2 full independence and V1 sunset.

---

## 2. Use Cases In Scope

### 2.1 `extract-model`
Connect to live OSSYS SQL Server, run the OutSystems metadata queries, write a deterministic snapshot of modules/entities/attributes/references/indexes/triggers to disk. V2's `CatalogReader` already parses this snapshot; V2 must learn to *produce* it.

### 2.2 `full-export` with overrides
Chain extract → profile → emit, accepting:
- **Table-rename overrides**: rename source table to target table (both logical `Module::Entity` and physical `schema.table` forms).
- **Migration-dependency overrides**: append specific rows into specific tables, with PKs auto-assigned at emit time as `MAX(SourceOSSYS.Id) + ROW_NUMBER()` baked as literals into the emitted INSERT/MERGE statements.

Outputs needed: SSDT project on disk (per-table .sql + manifest), .dacpac binary, migration-INSERT scripts for the dependency rows, decision/opportunity/validation logs.

### 2.3 Explicitly out of scope
- **Apply / load-harness phases.** The operator runs the emitted artifacts against the real target DB via external tooling (DacFx publish or sqlcmd). V2 does not need an `--apply` surface. The `deploy` and `canary` subcommands stay as dev tooling.
- **UAT-users transformation as a top-level v2 feature.** The User-FK reflow exists internally and fires when `UserRemapContext` is supplied; CSV / inventory ingestion for that context is deferred-with-trigger.

---

## 3. Current State Audit (synthesized from 2026-05-11 audit subagents)

### 3.1 V1 surface (src/)

| Capability | Location | Notes |
|---|---|---|
| CLI verbs | `src/Osm.Cli/` | 8 verbs: extract-model, full-export, build-ssdt, profile, dmm-compare, inspect, analyze, policy explain (+ uat-users behind env flag) |
| OSSYS metadata SQL | `src/AdvancedSql/outsystems_metadata_rowsets.sql` (1,184 LOC) + `outsystems_model_export.sql` (931 LOC) | Parameterized: `@ModuleNamesCsv`, `@IncludeSystem`, `@IncludeInactive`, `@OnlyActiveAttributes`, `@EntityFilterJson` |
| Extraction orchestration | `src/Osm.Pipeline/SqlExtraction/` | `MetadataSnapshotRunner.cs` (407 LOC) + 25 result-set processors (988 LOC total) + `MetadataAccumulator.cs` (104 LOC) |
| Snapshot writer | `src/Osm.Pipeline/SqlExtraction/SnapshotJsonBuilder.cs` (288 LOC) | Writes `osm_model.json` via `Utf8JsonWriter` |
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
| User-FK reflow | `src/Projection.Targets.Data/MigrationDependenciesEmitter.fs:253-330` + `UserFkReflowPass` | Built; consumes `UserRemapContext`; programmatic construction only |

### 3.3 The four-axis gap

1. **OSSYS extraction**: ~6,565 C# LOC + 2,115 SQL LOC. SQL copies verbatim. C# port estimated ~4,155 F# LOC.
2. **Profile probes**: V1's `profile` verb captures NULL counts, FK orphan counts, unique-index dup counts, MAX(Id) per identity column. Must be ported to V2.
3. **CLI / config surface**: V2 needs a unified typed config file (single JSON) replacing V1's seven scattered input mechanisms. CLI must accept `--config <path>` plus a small set of common overrides.
4. **Emitter wiring + override plumbing**: data emitters, dacpac emitter, diagnostic emitters need to be threaded through `Compose.project` under config-driven `EmissionPolicy` gates. Table-rename overrides need a pre-emit Catalog rewrite pass. Migration-dependency JSON needs a loader + an auto-PK pass.

---

## 4. Locked-in Design Decisions

These are decisions made during the 2026-05-11 audit collaboration. Each is revisable, but moving them requires explicit reopening in §9 with a new dated entry.

| # | Decision | Rationale |
|---|---|---|
| D1 | **V2 owns OSSYS extraction** (port from V1), not subprocess-shell-out, not permanent V1 dependency. | Required for V1 sunset; clean cutover; F# parity. |
| D2 | **Full V2 independence before cutover**, not partial / coexistence. | Operator workflow expects one tool. Coexistence is a fallback, not a delivery target. |
| D3 | **Single typed config file** (one JSON document) for all overrides, replacing V1's seven scattered input mechanisms. CLI accepts `--config <path>` + a small set of common flag overrides. | Reduces operator cognitive load; one schema to learn; supports future evolution. |
| D4 | **Phase A first as a soak**, then Phase B. V2 emit runs against V1-extracted JSON during Phase B port for differential testing. | Surfaces emit-side gaps early; validates emit-half against real workloads before extraction-side rewrite lands; reduces risk of finding emit bugs late in cutover. |
| D5 | **MigrationDependencies PK assignment**: pre-compute at emit time as `MAX(SourceOSSYS.Id) + ROW_NUMBER()`; bake literal IDs into emitted SQL. | Deterministic; readable diffs; no deploy-time uncertainty; aligns with V1's literal-ID INSERT convention. *Caveat: requires MAX(Id) per table to be captured by extraction or profile — see §9 Q1.* |
| D6 | **V2 owns profile probes** against OSSYS DB, not "V1 profiles, V2 ingests profile JSON." | Required for full independence (D2); detection passes (orphaned FKs, mandatory-null) need profile data to fire; V1's profile verb is portable. |
| D7 | **Apply phase is external.** V2 emits artifacts; operator runs them via DacFx publish / sqlcmd. No `--apply` flag, no in-V2 deploy-to-real-target. | Operator confirmed external tooling owns the apply step; reduces V2 scope; existing `deploy` subcommand stays as ephemeral dev tooling. |
| D8 | **Tightening as detection, not intervention.** Operator does not configure tightening rules in production; V2's role is to *catch* SQL Server semantic breakers (orphaned FKs, IsMandatory=true + nulls, unique-index dups) and emit them as Opportunities for operator review. | Detection passes already exist; profile data is the missing input; intervention-axis tuning is out of scope. |

---

## 5. Phase A — Soak Path (V1 extracts/profiles, V2 emits)

**Goal:** V2 emit produces the full artifact set from V1-extracted `osm_model.json` and V1-captured profile JSON, with config-driven overrides. Validate against operator's real workload via differential testing before Phase B begins.

### 5.1 A.0 — Unified config schema

**Deliverable:** A typed F# discriminated-union model in `Projection.Pipeline` representing the unified config, plus a JSON parser/validator. Documented schema with concrete examples.

**Schema sketch** (preliminary; finalize during A.0 work):

```json
{
  "model": {
    "path": "extracted/osm_model.json",
    "modules": ["AppCore", { "name": "ServiceCenter", "entities": ["User"] }],
    "includeSystemModules": false,
    "includeInactiveModules": false,
    "onlyActiveAttributes": true
  },
  "profile": {
    "path": "extracted/profile.json"
  },
  "overrides": {
    "tableRenames": [
      { "from": { "module": "OldModule", "entity": "OldEntity" }, "to": { "schema": "dbo", "table": "NEW_TABLE" } },
      { "from": { "schema": "dbo", "table": "OSUSR_X_Y" }, "to": { "schema": "dbo", "table": "RENAMED" } }
    ],
    "migrationDependencies": {
      "path": "overrides/migration-rows.json"
    }
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

**Tasks:**
- Define F# record types in `Projection.Pipeline/Config.fs`
- Implement `Config.parse : JsonNode → Result<Config, ConfigError>` with structured errors (path + reason)
- Implement `Config.validate : Config → Result<ValidatedConfig, ValidationError>` (file existence checks, schema consistency)
- Write the schema reference doc at `sidecar/projection/docs/config-schema.md` with each section explained
- Property tests: round-trip parse → serialize → parse; structured-error coverage

**Exit:** Operator can read the schema doc, hand-write a config file, parse it, get a typed `ValidatedConfig` back or a structured error.

### 5.2 A.1 — CLI surface upgrade

**Deliverable:** `Projection.Cli` accepts `--config <path>` for `emit`; new `projection emit --config <path>` is the canonical entry. Legacy positional-arg form may stay as deprecated shorthand or be removed.

**Tasks:**
- Replace hand-rolled `match argv` parser with `Argu` (F#-idiomatic) or extend the hand-rolled approach if Argu's overhead is judged unacceptable.
- Wire `--config` to `Config.parse → Config.validate → Compose.runWithConfig`.
- Update help output. New exit codes: `6` for config-parse-error, `7` for config-validation-error.
- Backward-compat: legacy positional form maps to a `Config` with mostly-empty overrides + EmissionPolicy.empty (today's behavior).

**Exit:** `projection emit --config example.json` runs end-to-end with today's emitter set.

### 5.3 A.2 — Emitter wiring under EmissionPolicy gates

**Deliverable:** All built-but-hidden emitters fire from `Compose.project` when their corresponding `EmissionPolicy` gate is open.

**Tasks:**
- Refactor `Compose.project` in `src/Projection.Pipeline/Pipeline.fs:181` to accept the validated config (not hardcoded empties).
- Add gates:
  - `EmissionPolicy.EmitData = true` → DataEmissionComposer wired (Static + MigrationDeps + Bootstrap)
  - `EmissionPolicy.EmitDiagnostics = true` → DecisionLog + Opportunities + Validations wired
  - Config `emission.dacpac = true` → DacpacEmitter wired into the SSDT bundle path
- Verify `DataComposition` mode flows through (AllRemaining / AllExceptStatic / AllData) per config.
- Update integration tests to exercise each gate.

**Exit:** With config-driven gates, V2 emit can produce: SSDT + dacpac + Static seeds + Migration deps + Bootstrap + DecisionLog + Opportunities + Validations. All from one run.

### 5.4 A.3 — MigrationDependencies JSON loader + auto-PK pass

**Deliverable:** Operator-facing JSON file format + loader + pre-emit pass that assigns PKs.

**JSON shape** (preliminary):
```json
{
  "tables": [
    {
      "kindKey": { "module": "AppCore", "entity": "Country" },
      "rows": [
        { "Code": "US", "Label": "United States" },
        { "Code": "CA", "Label": "Canada" }
      ]
    }
  ]
}
```

Rows omit the PK column. Loader looks up MAX(Id) per kind, assigns Id = MAX+1, MAX+2, ... in declaration order. The assigned PKs are baked into the resulting `MigrationDependencyContext` `Values` map.

**Tasks:**
- `Projection.Pipeline/MigrationDependencyLoader.fs` — parse JSON, resolve kind keys against `Catalog`, validate column names against attribute set, surface structured errors.
- New pre-emit pass `MigrationDependencyPkAssignmentPass` in `Projection.Core/Passes/` — given `Profile.MaxIdentityValues` (new field, see §9 Q1) and a parsed migration document, produce a fully-PK'd `MigrationDependencyContext`.
- Wire into `DataEmissionComposer.composeFull` via the new pass.
- Tests:
  - Loader: malformed JSON, unknown kind, unknown attribute, type mismatches (string vs int column)
  - PK assignment: deterministic ordering, MAX-collision diagnostic
  - End-to-end: JSON in → MERGE SQL out with literal IDs

**Exit:** Operator-provided JSON config produces correctly-PK'd MERGE statements emitted to `out/` as part of the standard emit flow.

### 5.5 A.4 — Table-rename plumbing

**Deliverable:** Config-declared table renames apply to `Catalog` before emitters run.

**Tasks:**
- New pre-emit pass `TableRenamePass` in `Projection.Core/Passes/` — takes a rename map + `Catalog`, returns a renamed `Catalog`. Validates: each rename source exists; no collision in targets; lineage trail records the rewrite.
- Support both logical (`Module::Entity`) and physical (`schema.table`) source forms, matching V1's `NamingOverridesBinder` behavior at `src/Osm.Pipeline/Application/NamingOverridesBinder.cs:32-119`.
- Apply renames *before* topological-order pass so dependency edges follow renamed names.
- Tests: round-trip rename, collision detection, missing-source error, both source forms.

**Exit:** A config with `tableRenames` produces SSDT DDL referencing the renamed names everywhere (table defs, FK references, indexes, triggers, manifest).

### 5.6 A.5 — Profile-JSON ingestion

**Deliverable:** Adapter that reads V1's `profile` verb output JSON and hydrates V2's `Profile` type. With profile data threaded, the existing detection passes (Nullability, ForeignKey, UniqueIndex) light up.

**Tasks:**
- Inspect V1's profile JSON shape (`src/Osm.Pipeline/Profile/` and the `profile` verb output).
- New `Projection.Adapters.Osm/ProfileReader.fs` mirroring `CatalogReader.fs` in style.
- Validate that V1's profile JSON contains all fields V2's `Profile` type expects (`Columns`, `UniqueCandidates`, `ForeignKeys`, optionally `Distributions`, `CdcAwareness`). Identify any field-level gaps.
- Wire into `Compose.run` when config supplies `profile.path`.
- Tests: round-trip parse, schema-shift handling, missing-field diagnostics.

**Exit:** With V1-captured profile JSON supplied via config, V2 emit produces decision logs containing orphan-FK warnings and mandatory-null warnings.

### 5.7 A.6 — Soak: differential testing against V1's outputs

**Deliverable:** A reproducible test rig that runs V2 emit against operator's real workload (or a representative production-sized fixture) and compares outputs byte-for-byte (or semantic-diff) against V1's outputs.

**Tasks:**
- Pick a representative fixture (real production model + profile, or the largest existing test fixture).
- Run V1 full-export → capture outputs.
- Run V2 emit on V1's extracted JSON + profile JSON → capture outputs.
- Build a diff harness:
  - SSDT .sql files: byte diff, with allowlist for whitespace / formatting differences (if any are agreed)
  - Manifest JSON: semantic diff (key-order-agnostic)
  - .dacpac: deserialize and compare schema model (DacFx provides this)
  - Migration-INSERT SQL: byte diff
  - Decision logs: semantic diff (entries may be in different order; compare set-equivalence)
- Triage every divergence: bug in V2, bug in V1 (acknowledged), or agreed-different (record in this doc §11).
- Fix V2 bugs; document agreed differences.

**Exit:** Differential diff is clean (no unexplained divergence) on operator's representative workload. V2 emit is functionally equivalent to V1 build/SSDT/diagnostic emission, modulo the recorded agreed differences.

### 5.8 Phase A milestones (estimated, single-developer focus)

- **A.0 + A.1**: 1-1.5 weeks (config schema + CLI plumbing)
- **A.2**: 1 week (emitter wiring; the emitters exist, this is plumbing)
- **A.3**: 1-1.5 weeks (loader + auto-PK pass + tests)
- **A.4**: 0.5-1 week (rename pass + tests)
- **A.5**: 1 week (profile reader; depends on V1's profile JSON stability)
- **A.6**: 1-2 weeks (soak; duration depends on divergence triage workload)

**Phase A total: 6-9 weeks** for one focused developer.

---

## 6. Phase B — Full Independence (V2 owns extraction + profiling)

**Goal:** V2 connects to OSSYS SQL Server, extracts metadata, captures profile, and emits artifacts in a single command. V1 is unnecessary.

### 6.1 B.0 — Foundation

**Tasks:**
- Copy `src/AdvancedSql/outsystems_metadata_rowsets.sql` (1,184 LOC) and `outsystems_model_export.sql` (931 LOC) verbatim into `sidecar/projection/sql/`.
- Port `SqlConnectionFactory` to F# in `Projection.Adapters.Sql/Connection.fs` (~50 F# LOC). Auth modes: Integrated, SqlPassword, ActiveDirectoryDefault, AccessToken. Cert trust + application name.
- Port `SqlConnectionOptions`, `DbCommandExecutor`, `IAdvancedSqlExecutor` interfaces as F# records/functions (no interfaces; pattern-match dispatch where polymorphism is needed).

**Exit:** V2 can open a connection to OSSYS SQL Server and execute the metadata SQL script, returning a `SqlDataReader`-equivalent F# stream.

### 6.2 B.1 — Result-set binding

**Tasks:**
- Port 25 result-set processors. C# uses inheritance + factory; F# uses a single DU `MetadataResultSet` with one case per result + pattern-match dispatch.
- Port `MetadataAccumulator` to an F# record of lists (`ResizeArray<T>` during accumulation; immutable `IReadOnlyList<T>` at finalization).
- Port `ResultSetReader` and `ResultSetDescriptorFactory`. Total ~1,500 C# LOC → ~900 F# LOC.

**Risk:** highest in Phase B. The async-stream lifetime management in `MetadataSnapshotRunner.cs:407` is non-trivial. F#'s `task` workflow handles this, but the `CommandBehavior.SequentialAccess` semantics must be preserved exactly to avoid memory exhaustion on large catalogs.

**Exit:** V2 runs `outsystems_metadata_rowsets.sql` against OSSYS and produces a populated `OutsystemsMetadataSnapshot` F# record.

### 6.3 B.2 — Snapshot serialization

**Tasks:**
- Port `SnapshotJsonBuilder` to F# in `Projection.Adapters.Osm/SnapshotWriter.fs` (~300 F# LOC). Direct `Utf8JsonWriter` calls, no reflection.
- Port `SnapshotValidator`. Validates accumulator completeness (no orphan attribute references, no missing parent modules, etc.).
- Verify output is byte-equivalent to V1's `osm_model.json` for at least one large fixture. This is critical: V2's `CatalogReader` already parses this format; any field-order or escaping divergence breaks the chain.

**Exit:** V2 produces a byte-equivalent `osm_model.json` from a live OSSYS DB. V2's existing `CatalogReader` consumes it without modification.

### 6.4 B.3 — Profile probes

**Tasks:**
- Inventory V1's `profile` verb probes: per-column NULL count, FK orphan count, unique-index dup count, MAX(Id) per identity column, optionally distribution probes (categorical / numeric percentiles).
- Port the probe runners. Each probe is a parameterized SQL query; results hydrate a `Profile` record directly (skipping the V1 JSON middleman).
- Sampling: respect `--sampling-threshold` / `--sampling-size` for large tables.
- Add `MaxIdentityValue` field to `ColumnProfile` (this is needed by Phase A's auto-PK pass; can be backfilled in V1 sooner and used by V2 in Phase A.3, then re-implemented native in Phase B.3).
- Tests: each probe in isolation against a fixture DB; end-to-end profile generation against the canary's ephemeral SQL Server.

**Exit:** V2 connects to OSSYS, runs the probe set, returns a populated `Profile` record. Detection passes fire with real data.

### 6.5 B.4 — Orchestration + new CLI subcommands

**Tasks:**
- Add `projection extract --config <path>` subcommand. Reads `sql.connectionString` from config; runs metadata extraction; writes `osm_model.json` to configured output path.
- Add `projection profile --config <path>` subcommand. Reads connection; runs probes; writes profile JSON (matching V1's shape for soak-compatibility) or hydrates `Profile` in-memory for direct consumption.
- Add `projection full-export --config <path>` subcommand. Chains extract → profile → emit. All three steps drive off the same unified config from §5.1.
- Port `ModuleFilter` (defensive post-extraction filter) and `MetadataContractOverrides` (version-dependent SQL field handling).
- New exit codes: `8` for SQL connection failure, `9` for SQL-script execution error, `10` for profile probe failure.

**Exit:** Operator runs `projection full-export --config production.json` against OSSYS. V2 produces the full artifact set without V1.

### 6.6 Phase B milestones (estimated)

- **B.0**: 1 week (foundation)
- **B.1**: 2-3 weeks (result-set binding; highest-risk phase)
- **B.2**: 1 week (serialization + validator)
- **B.3**: 2-3 weeks (profile probes)
- **B.4**: 1-2 weeks (orchestration + CLI)

**Phase B total: 7-10 weeks** for one focused developer. Parallel two-developer split: ~4-6 weeks elapsed.

---

## 7. Cutover Criteria

### 7.1 Phase A exit
- All A.0–A.6 deliverables met.
- Differential diff vs V1 outputs is clean on operator's representative workload.
- Config schema doc reviewed and approved by operator.
- No known V2-side defects in emit half.

### 7.2 Phase B exit (= full cutover gate)
- V2 `full-export` runs against OSSYS SQL Server and produces byte-equivalent `osm_model.json` to V1's `extract-model` output.
- V2 profile probes produce the same `Profile` data as V1's `profile` verb (modulo agreed differences).
- V2 emit outputs are functionally equivalent to V1's outputs (per Phase A criterion).
- ≥1 full end-to-end production dry-run completed by operator.
- Cutover-day runbook written (separate document).

### 7.3 V1 sunset
- T+30 days after Phase B exit: V1 enters maintenance mode (no new features, only critical bug fixes if V2 cannot serve).
- T+90 days: V1 archived. `src/` becomes read-only reference.

---

## 8. Risk Register

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| R1 | Operator's "document of key evolutions" diverges meaningfully from current `origin/main` — plan may need revision. | High (operator-flagged) | Medium | Pause Phase A.0 until operator shares the evolutions doc; revise this plan as Draft 2 before coding starts. |
| R2 | V1's profile JSON schema lacks `MaxIdentityValue` per column → Phase A.3 auto-PK pass blocked until V1 is amended or Phase B.3 lands. | Medium | Medium | Audit V1 profile JSON first thing in A.5; if missing, either (a) amend V1's profile verb to emit MAX(Id) as a small additional probe, or (b) defer A.3 to Phase B. Decision: A.5 audit feeds A.3 sequencing. |
| R3 | Async stream lifetime in `MetadataSnapshotRunner` port (B.1) hits non-obvious memory or correctness issues. | Medium | High | Port with `task` workflow + explicit `IAsyncDisposable` handling; integration test against a fixture catalog ≥1000 entities to validate streaming behavior under load. |
| R4 | Byte-equivalence of `osm_model.json` (B.2) cannot be achieved due to System.Text.Json output ordering differences between C# and F# usage. | Low | High | Use the exact same `JsonWriterOptions`; for any unavoidable diff (e.g., property order), fall back to semantic equivalence + update `CatalogReader` if needed. |
| R5 | Differential diff (A.6) surfaces a deep emit-side divergence that requires substantial V2 rework. | Medium | High | Phase A.6 is intentionally placed before Phase B starts; surfacing divergence early is the point. Budget +50% time for A.6 to absorb surprises. |
| R6 | Operator's workflow uses V1 capabilities not yet inventoried (e.g., uat-users, dmm-compare, analyze). | Medium | Medium | Confirm with operator that the two named use cases (`extract-model`, `full-export`) are exhaustive for cutover; if not, scope additional verbs. |
| R7 | OSSYS-side SQL incompatibility surfaces in production tenant (different SQL Server version than dev/test). | Low | High | `MetadataContractOverrides` (port in B.4) handles version-dependent fields; test against each target SQL Server version operator runs. |

---

## 9. Open Questions

Numbered for stable reference; revisions go in §11 addenda.

**Q1.** **MAX(Id) source for the auto-PK pass (A.3).** D5 commits to "MAX(SourceOSSYS.Id) at emit time, literal IDs in SQL." This implies V2 needs OSSYS access during emit, which contradicts Phase A's "V1 extracts, V2 emits" separation. Options:
  - (a) Extend V1's profile verb to emit `MaxIdentityValue` per identity column; V2 reads from profile JSON. Smallest scope; keeps Phase A self-contained.
  - (b) V2 opens a side-channel SQL connection during emit to query MAX. Adds OSSYS dependency to Phase A.
  - (c) Defer A.3 to Phase B, where V2 has OSSYS access natively. Phase A emits Migration deps with operator-supplied PKs (today's `MigrationDependenciesEmitter` behavior); auto-PK lands with extraction.

  Recommendation: (a). Cleanest. Requires a small V1 patch but avoids breaking Phase A's separation.

**Q2.** **Argu vs hand-rolled CLI parser (A.1).** Hand-rolled stays simple if argv grows linearly; Argu pays off when subcommand × flag matrix gets large. Phase B adds 3 subcommands × ~5 common flags. Argu likely earns its keep but adds a dependency. Decide during A.1.

**Q3.** **Backward-compat for V2's existing positional CLI.** Drop it or keep it as deprecated shorthand? Current usage in tests / dev tooling matters here.

**Q4.** **Profile JSON shape coupling.** Should V2's Phase B.3 profile output match V1's JSON shape byte-equivalent (for symmetric soak), or define a V2-native shape and emit a V1-compat shape as a separate output? Recommendation: V2-native primary, V1-compat secondary, with a "compatibility-mode" config flag.

**Q5.** **Dacpac as primary vs SSDT-folder as primary.** Operator implied both are needed; clarify whether one is the canonical artifact and the other is supplementary, or whether they're peers in the output bundle.

**Q6.** **Operator's "document of key evolutions"** (referenced 2026-05-11 by the product owner). Outstanding. Plan revision required after delivery.

**Q7.** **Sampling and large-catalog behavior in Phase B.** V1's `--sampling-threshold` / `--sampling-size` apply primarily to profile probes. Confirm whether operator's production catalogs need sampling and what thresholds make sense.

---

## 10. Deferred / Out-of-Scope

These are *deliberately* not in scope for cutover. If operator decides they need any of these later, reopen as a separate plan.

- **`--apply` phase** (D7).
- **`--load-harness` phase** (wait stats, locks, fragmentation profiling).
- **`uat-users` transformation as a CLI feature** (`UserRemapContext` exists internally; CSV ingestion deferred-with-trigger per `HANDOFF.md:75`).
- **`dmm-compare` verb** — V1 capability not in operator's named use cases.
- **`analyze` verb** — same.
- **`policy explain` verb** — same.
- **Per-Catalog Docker parameterization** — deferred-with-trigger per `HANDOFF.md:70`.
- **OSSYS User-kind identification** in OSSYS adapter — deferred-with-trigger per `HANDOFF.md:74`.

---

## 11. Addenda (append-only)

Subsequent audits, decisions, and plan revisions append below. Each entry dated and tagged.

### 11.1 (placeholder) Audit slot for operator's "document of key evolutions"

When operator delivers the evolutions document, append a synthesis subsection here, revise §3 (Current State Audit) as needed, and bump the plan to Draft 2 in the header.

### 11.2 (placeholder) Agreed differences between V1 and V2 outputs

During Phase A.6 soak, any divergences classified as "agreed-different" (rather than "V2 bug") get recorded here with rationale.

### 11.3 (placeholder) Per-milestone close notes

As Phase A.0, A.1, …, B.4 close, append a 5-10 line close note: what shipped, what deferred, what surprised.
