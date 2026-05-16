# V2 Production Cutover Plan

**Status:** Draft 3 — 2026-05-12. Integrates the verifiability-triangle audit (`AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md`) and `PRODUCT_AXIOMS.md` into the plan-of-record. Drafts 1 / 2 / 2.1 / 2.2 commit references: `2ab3a8a` / `5da03c2` / `090edab` / mid-doc-edit. Draft 3 is the first version where workstreams are tagged with the L3 axioms they operationalize and the bucket promotions they produce; cutover criteria are restated as axiom-bucket-witnessed claims; risks are restated where possible as axiom-violation scenarios; and Campaign A / B / C (from audit Part IX) are integrated as cross-cutting workstream tags rather than parallel work.

**Companion documents:** `AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md` (the L1↔L2↔L3 coverage map; the algebra's reference manual), `PRODUCT_AXIOMS.md` (L3 product axiom catalog; the operator's promise), `AXIOMS.md` (L2 formal axioms; the algebra's interior), `VISION.md` (strategic frame), `V2_DRIVER.md` (KPI ladder), `STAGING.md`, `HANDOFF.md`, `DECISIONS.md`.

**Outstanding:** Operator's "document of key evolutions" (referenced 2026-05-11 by the product owner) — likely reshapes UAT-users scope and may add a sixth core concern. Operator decision on (a) Campaign A sequencing within Phase A and (b) axiom-naming convention (A41+ in AXIOMS.md vs `L3-Boundary-*` namespace).

---

## Table of Contents

1. [Executive summary + five Draft-3 insights](#1-executive-summary)
2. [Use cases in scope](#2-use-cases-in-scope)
3. [Current state audit](#3-current-state-audit)
4. [Locked-in design decisions](#4-locked-in-design-decisions)
5. [The composition algebra](#5-the-composition-algebra)
6. [Phase A — Soak path (workstreams)](#6-phase-a--soak-path)
7. [Phase B — Full independence (workstreams)](#7-phase-b--full-independence)
8. [Cutover criteria (axiom-witnessed)](#8-cutover-criteria-axiom-witnessed)
9. [Risk register](#9-risk-register)
10. [Open questions](#10-open-questions)
11. [Deferred / out-of-scope](#11-deferred--out-of-scope)
12. [Per-axiom delivery matrix](#12-per-axiom-delivery-matrix)
13. [Addenda (append-only)](#13-addenda-append-only)

---

## 1. Executive summary

V2 is structurally green on the schema/DDL axis (1,121 tests post-Slice-1 PhysicallyRenamed; three Π's wired through `Compose.run` + a fourth, `Compose.runWithConfig`, threading rename through `read → applyRenames → project → write`). The cutover blockers are not in the algebra's interior — they're in three concentric rings around it: boundary contracts (V1 ↔ V2 ingestion; V2 → disk emission; V2 → operator diagnostics), operational evolution (versioning, config compatibility, CI/CD migration), and compositional reasoning (pass-order commutation, Lifecycle temporal axis). The verifiability-triangle audit (`AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md`) catalogued every gap and proposed three campaigns; this Draft 3 integrates those campaigns into the Phase A / Phase B sequence as cross-cutting workstream tags.

### Five Draft-3 insights

**(1) IR fidelity (A.0') *is* the largest unnamed-axiom operationalization.** The 11 IR concepts in Phase A.0' (triggers, sequences, temporal tables, DEFAULT values, computed columns, CHECK constraints, ExtendedProperties, descriptions, IsExternal/Origin mapping, IsActive flags, Catalog coordinate) map one-to-one onto L3-S4 through L3-S10, L3-I10, and L3-CC4. Once A.0' completes, eight Tier-1 unnamed axioms promote from Bucket D to Bucket A by construction. Campaign A.2 (no-silent-drop) is A.0''s completion criterion restated as an axiom.

**(2) Only 2 cutover-blocker axioms are genuinely *new* work.** Of the four Bucket-D Tier-1 axioms surfaced by the audit:
- L3-Idempotence-OnRedeploy (CDC silence) is in flight at chapter 4.1.B.
- L3-Boundary-NoSilentDrop is A.0''s exit criterion.
- L3-Boundary-AtomicEmission is net-new — but it's a small surgical change to `Compose.write`.
- L3-Boundary-ManifestMatchesDisk is net-new — but it's a signature change in `Compose.write` + `ManifestEmitter`.

Campaign A's *incremental* cost on top of the existing Phase A plan is **3-5 days**, not the 3-5 weeks the audit estimated when Campaign A was framed in isolation.

**(3) The Catalog cross-field invariants expansion (1 → 8) is the deepest single change.** Original Slice 2 was about physical-name uniqueness — one invariant. The audit's Tier-1 catalog (Part VI.2) named seven more cross-field invariants in the same shape: `IsIdentity ⇒ ¬IsNullable`, `IsPrimaryKey index ⇒ IsUnique`, Length/Precision/Scale coherence with Type, Length bounded by SQL VARCHAR limit, PK ordering enforced, at most one Static modality per Kind, physical-name uniqueness. All eight extend `Catalog.create` in the same shape. One PR, mechanically simple, very high leverage — every Catalog instance downstream gains seven new structural guarantees on top of the existing five A39 invariants.

**(4) "R11 dissolved" was the algebra at work.** Once the audit confirmed `TopologicalOrderPass` reads SsKey only, R11 (rename ordering perturbs topo) became impossible by construction, not by mitigation. Draft 3 expects more R-dissolutions as Campaign B lands: R11 dissolved post-audit; R2 likelihood/impact dropped to Low/Low post-Q1 resolution; future structural commitments will retire R4, R7, R10, R11 categories of concern.

**(5) Cutover criteria become provable.** Before: "Phase A exit: all A.0–A.6 deliverables met" — judgment. After: "Phase A exit: every Tier-1 L3 axiom in the target set is Bucket A or B; no Tier-1 axiom remains in Bucket D" — mechanically verifiable from the audit doc's coverage map at any point in time. Cutover ladder rungs become axiom-bucket invariants rather than checklist completion.

### Sequence at the highest level

The two-phase frame holds. Phase A (V2 emits from V1-extracted JSON; soak against V1 outputs; ~6-9 weeks including Campaigns A's 3-5 incremental days + Campaigns B/C wherever they slot in). Phase B (V2 owns extraction + profiling; ~8-11 weeks). V1 sunset begins at cutover+30 + one full schema-evolution cycle per R6.

---

## 2. Use cases in scope

### 2.1 `extract-model`
Connect to live OSSYS SQL Server, run the OutSystems metadata queries, write a deterministic snapshot of modules / entities / attributes / references / indexes / triggers / sequences / extended-properties to disk. V2's `CatalogReader` parses this snapshot's existing fields; V2 must learn to *produce* it and to also carry the fields V2 currently drops at the adapter boundary (per §3.3 and Phase A.0').

**Axioms underwritten:** L3-S1 (byte-determinism), L3-I2 (OssysOriginal survives), L3-I3 (synthesis deterministic), L3-CC4 (IR fidelity for production), L3-Boundary-NoSilentDrop (post-Campaign A.2).

### 2.2 `full-export` with overrides
Chain extract → profile → emit, accepting:
- **Table-rename overrides**: rename source table to target table (both logical `Module::Entity` and physical `schema.table` forms). Shipped at Slice 1 (commit `9d578cc`); L3-I1 verified.
- **Migration-dependency overrides**: append specific rows into specific tables, with PKs auto-assigned at emit time as `MAX(observedSet.Id) + ROW_NUMBER()` baked as literals into emitted INSERT/MERGE statements.
- **Static-data overrides**: seed-row fixtures (parallel-but-distinct from migration dependencies; A.3 vs A.3').

Outputs needed: SSDT project on disk (per-table .sql + manifest), `.dacpac` binary, static-seed INSERTs, migration-INSERT scripts, decision/opportunity/validation logs.

**Axioms underwritten:** L3-S1 through L3-S10 (schema fidelity), L3-D1 through L3-D10 (data correctness), L3-I1 (rename safety), L3-X1 through L3-X10 (diagnostics), L3-C1 (canary), L3-Idempotence-OnRedeploy (CDC silence), L3-Boundary-AtomicEmission, L3-Boundary-ManifestMatchesDisk.

### 2.3 Explicitly out of scope
(See §11 for the full deferral catalog with rationale.)

- **Apply / load-harness phases** (operator runs SSDT/dacpac via external tooling; D7).
- **UAT-users transformation as a top-level feature** until the operator's evolutions document arrives.
- **V1 verbs not in operator's named two use cases** (dmm-compare, analyze, inspect, policy-explain).
- **`.sqlproj` generation**, **`SafeScript.sql` / `RemediationScript.sql` emission**, **V1-compatible `osm_model.json` re-emission**.
- **Telemetry package, evidence-cache directory.**

---

## 3. Current state audit

(Synthesized from the 2026-05-11 six-agent audit + 2026-05-12 verifiability-triangle audit. Cross-references throughout to `AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md`.)

### 3.1 V1 surface (src/)

| Capability | Location | Notes |
|---|---|---|
| CLI verbs | `src/Osm.Cli/` | 8 verbs: extract-model, full-export, build-ssdt, profile, dmm-compare, inspect, analyze, policy explain (+ uat-users behind env flag) |
| OSSYS metadata SQL | `src/AdvancedSql/outsystems_metadata_rowsets.sql` (1,184 LOC) + `outsystems_model_export.sql` (931 LOC) | Parameterized |
| Result-set processors | `src/Osm.Pipeline/SqlExtraction/` | 25 concrete processors + `MetadataSnapshotRunner.cs` (407 LOC) + `MetadataAccumulator.cs` (104 LOC); core extraction ~2,000 C# LOC |
| Profile probes | `src/Osm.Pipeline/Profile/` (~6,000 C# LOC) | 5 query builders: NullCount, NullRowSample, UniqueCandidate, ForeignKeyProbe, ForeignKeyOrphanSample. Uniform sampling via `TableSamplingPolicy`. No MAX(Id) probe today. No distribution/percentile probes. |
| Connector | `src/Osm.Pipeline/Sql/SqlConnectionFactory.cs` | Microsoft.Data.SqlClient; auth modes (Integrated, SqlPassword, ActiveDirectoryDefault, AccessToken) |
| Override binders | `src/Osm.Pipeline/Application/NamingOverridesBinder.cs`; `src/Osm.Pipeline/StaticData/StaticEntityDataProviders.cs`; `src/Osm.Pipeline/Configuration/CliConfigurationLoader.cs` | Seven scattered input mechanisms |

### 3.2 V2 surface (sidecar/projection/)

| Capability | Location | State |
|---|---|---|
| CLI | `src/Projection.Cli/Program.fs` | Hand-rolled `match argv`; subcommands `emit` / `emit --config <path>` / `deploy` / `canary`; positional + flag forms coexist |
| Pipeline composition | `src/Projection.Pipeline/Pipeline.fs` (`Compose.run` + `Compose.runWithConfig`) | `runWithConfig` threads rename through `read → applyRenames → project → write` |
| Config + boundary | `src/Projection.Pipeline/Config.fs` (D9 secret-free) + `RenameBinding.fs` (Config → Core mapper) | Shipped at A.0 / A.1 / A.4 (commits `93468a3`, `df18bbf`, `502592f`, `9d578cc`) |
| Emitters wired in CLI | SsdtDdlEmitter, JsonEmitter, DistributionsEmitter | 3 of 11 |
| Emitters built but unwired | DacpacEmitter, DockerImageEmitter, StaticSeedsEmitter, MigrationDependenciesEmitter, BootstrapEmitter, DecisionLogEmitter, OpportunitiesEmitter, ValidationsEmitter, RefactorLogEmitter | All tested; A.2 wires them under EmissionPolicy gates |
| OSSYS metadata extractor | — | **Absent.** V2's `ReadSide` reads `INFORMATION_SCHEMA` for canary verification, not OSSYS metadata. Phase B.0–B.2. |
| Profile probes | — | **Absent.** `Profile.empty` is used in CLI; type structure is rich and ready. Phase B.3. |
| Detection passes | `src/Projection.Core/Passes/NullabilityPass.fs`, `ForeignKeyPass.fs`, `UniqueIndexPass.fs` | Mature; route to Opportunities via `Code` prefix; only meaningful with real Profile data |
| TableRename | `src/Projection.Core/Passes/TableRename.fs` + `TransformKind.PhysicallyRenamed` payload | Shipped at Slice 1 (commit `9d578cc`); L3-I1 verified |

### 3.3 IR-fidelity gaps → L3-S4–S10 + L3-I9–I10 + L3-CC4 underwriting

V2's `Catalog` does not yet carry several schema concepts V1's `OsmModel` preserves. **Each is a Tier-1 L3 axiom currently in Bucket D; A.0' is the workstream that promotes them to Bucket A.**

| V1 concept | L3 axiom | Phase A.0' deliverable | Cutover impact |
|---|---|---|---|
| Trigger definitions (T-SQL text) | L3-S4 | Add `Catalog.Triggers : Trigger list` with `Definition : string`, `IsDisabled : bool` | Triggers silently disappear from target DB |
| Sequences (StartValue, Increment, Min, Max, Cycle, Cache) | L3-S5 | Add `Catalog.Sequences : Sequence list` | Applications depending on sequence-generated IDs fail |
| Temporal tables (TemporalTableMetadata) | (covered by L3-S4 family; new sub-axiom pending) | Extend `ModalityMark` with `Temporal of TemporalConfig` | History-table configuration lost; audit trail broken |
| DEFAULT values | L3-S6 | Add `Attribute.DefaultValue : SqlLiteral option` | Column defaults vanish; subsequent INSERTs miss defaulted data |
| Computed columns | L3-S7 | Add `Attribute.Computed : ComputedColumnConfig option` | Computed-column definitions lost |
| CHECK constraints | L3-S8 | Add `Kind.ColumnChecks : ColumnCheck list` | Data validation rules disappear |
| Catalog (database) coordinate | L3-S10 / L3-I10 | Extend `TableId` to `{ Catalog: string option; Schema; Table }` | Cross-DB FKs silently degrade |
| ExtendedProperties at 4 levels | L3-S9 | Add `ExtendedProperties: ExtendedProperty list` to Module / Kind / Attribute / Index | OSSYS-defined metadata, audit fields lost |
| Description fields (Entity, Attribute) | (covered by L3-S9; new sub-axiom pending) | Add `Description: string option` to Kind metadata + Attribute metadata | Operator-visible docstrings vanish |
| IsExternal / Origin mapping | (Bucket-B today; A.0' upgrades) | Adapter property test: V1 `IsExternal=true → V2 Origin = ExternalViaIntegrationStudio \| ExternalDirect` | External entities emitted as native (or vice versa) |
| IsActive flags (Module, Attribute) | (Bucket-B today; A.0' upgrades) | Add `IsActive: bool` to Module / Attribute | Inactive schema elements leak into cutover DDL |

**Campaign A.2 ("no silent V1 feature skip" / L3-Boundary-NoSilentDrop)** is A.0''s completion criterion: every concept in the table above ends with either (a) a typed Catalog field, OR (b) a `Diagnostic.Severity=Error` at the adapter boundary. No silent passthrough is the structural invariant; the axiom names it.

### 3.4 Credentials & secrets (cross-cutting; L3-C8)

**Policy (D9):** Connection strings and credentials *do not live in the unified config JSON*. They are sourced from environment variables, a separate non-checked-in connection-config file referenced via CLI flag, or connection-string keywords for tokenless auth (`Authentication=Integrated`, `Authentication=ActiveDirectoryDefault`). The config JSON is **secret-free by construction**.

**L3 axiom:** L3-C8 ("no credentials in unified config"). **Bucket:** A (parser-level enforcement at `Config.fs` + structural absence of credential field types).

**Acceptance criterion:** `Config.parse` rejects any property whose name is a credential signature (Slice A.0's `D9 guardrail`; see `ConfigTests.``D9: credential property anywhere is rejected``). Campaign C.3 expands the credential-token list (`bearer`, `authorization`, `credentials`, `oauth`, `personal+access+token`, `private+token`).

### 3.5 Observability & logging (cross-cutting; L3-X9, L3-X10)

V1 uses `Microsoft.Extensions.Logging` with structured EventIds. V2 ships its own log format (D10); operator updates downstream log-aggregation tooling (CloudWatch / ELK / Splunk queries) at cutover. The plan does not gate on log-format equivalence.

**L3 axioms:** L3-X9 ("config validation errors are structured"; Bucket A), L3-X10 ("multi-environment policy diffs are signaled"; Bucket D pending chapter 4.1.A M4), plus L3-Operational-LoggingContract (candidate; Tier 2; informally Q9 from gap-hunt).

**Implications:**
- V1's `--extract-sql-metadata-out` / `--build-sql-metadata-out` / `--profile-sql-metadata-out` diagnostic JSON outputs are not preserved as a V2 contract. If operator tooling consumes them, it migrates to consuming V2's emitted Decision/Opportunity/Validation JSONs.
- V2's logging format is defined during Phase B.4; stable structured properties documented at `sidecar/projection/docs/logging-format.md`.

### 3.6 Determinism & idempotency (cross-cutting; L3-C7, L3-S1, L3-D2, L3-Idempotence-OnRedeploy)

V2 claims byte-determinism (T1 + L3-S1). User-supplied overrides (renames, migration-dep rows, module filters) can perturb execution order if not handled canonically.

**Policy (D12):** Canonical sort applied to user-supplied collections at config-validation time, before they reach pipeline passes. Specifically:
- `overrides.migrationDependencies` rows iterated in declaration order *within each table*; tables sorted by canonical kind-key.

**`overrides.tableRenames` exception (post-Draft-2.2 audit):** Rename order is structurally moot. `TopologicalOrderPass` reads SsKeys only; `Reference.TargetKind` is a SsKey, not a TableId. Renames rewrite only `Kind.Physical`. Two `TableRename.run` invocations with the same spec set in different orders produce structurally-equal output Catalogs (property test `D12: rename spec order does not affect the rewritten catalog`). D12 retains the general guardrail for any future user-supplied collection where ordering could matter.

**L3 axioms:** L3-C7 ("canonical ordering prevents determinism regression"; Bucket A), L3-S1 ("byte-determinism"; Bucket A modulo cross-platform extension Q3/Q4), L3-D2 ("static-seed deterministic order"; Bucket A), L3-Idempotence-OnRedeploy ("CDC silence on redeploy"; Bucket D pending chapter 4.1.B).

**Out of scope deliberately:**
- Idempotency test as a Phase A.6 gate.
- Graceful concurrent-extraction failure handling (operator coordinates externally).
- Single-writer documentation as a required deliverable.

### 3.7 CI/CD migration (cross-cutting; operator-owned per D11)

V1 is invoked in operator's CI/CD workflows. V2 cutover at T+30 breaks any V1-invocation site not migrated. **Operator-owned, not a plan deliverable.** Risk R9 stays as documented known risk.

**L3 axiom:** L3-Operational-CIMigration (candidate; Tier 2; informally Q6/Q19 from gap-hunt). Not formalized in this Draft 3; promotion deferred until operator's evolutions document arrives and clarifies their CI surface.

### 3.8 Structural-commitment posture (new in Draft 3)

The audit (`AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md` Part IV) classifies every L2 axiom (A1–A40 + T1–T11) and every L3 axiom into four coverage buckets:

| Bucket | Definition | Count |
|---|---|---|
| **A** | Full coverage (L3 ✓ L2 ✓ L1 ✓ test). Type system structurally prevents violation. | 18 L2 axioms; ~28 L3 axioms |
| **B** | L3 ✓ L2 ✓ test ✓; L1 by convention only. One refactor away from regression. | 12 L2 axioms; ~16 L3 axioms |
| **C** | Weakness: untested, hidden, aspirational, deferred, subsumed, scope boundary, or partial structural. | 20 L2 axioms; 0 L3 axioms (Bucket C is L2-only) |
| **D** | Unnamed L3 axiom; no L2 backing; silent operator dependency. | 0 L2 axioms; ~30 L3 candidates |

**Three rings of weakness** (audit Part VII) identify where V2's structural commitment is weakest:
1. **Boundary contracts** (V1→V2 ingestion; V2→disk writing; V2→operator diagnostics) — atomic emission, lossless parsing of carried fields, manifest matches disk, cross-platform encoding. Campaign A's primary target.
2. **Operational evolution** (V2's promises over time) — diagnostic code stability, config schema backward-compat, decision-log replay completeness, refactor-log DacFx compat. Campaign C's primary target.
3. **Compositional reasoning** (pass-order commutation, Lifecycle temporal axis). Mostly Bucket-C aspirational; surfaces when triggers fire.

**Plan-of-record implication:** every Phase A workstream is now tagged with the L3 axioms it promotes (D → A/B) and the campaign membership (A / B / C / none).

---

## 4. Locked-in design decisions

Decisions D1–D12 with axiom-underwriting tags. Revisable, but moving any requires explicit reopening in §13 with a dated entry.

| # | Decision | Rationale | Axioms underwritten |
|---|---|---|---|
| D1 | **V2 owns OSSYS extraction** (port from V1) | Required for V1 sunset; clean cutover; F# parity | L3-C10 (V1 sunset gating) |
| D2 | **Full V2 independence before cutover**, not partial/coexistence | Operator workflow expects one tool | L3-C3 (V2 owns no write during dual-track), L3-C4 (per-pair transition) |
| D3 | **Single typed config file** replacing V1's seven scattered input mechanisms | Reduces operator cognitive load | L3-X9 (config errors structured) |
| D4 | **Phase A first as a soak**, then Phase B | Surfaces emit-side gaps early; validates emit-half before extraction-side rewrite | L3-C1 (canary asserts V1 ≈ V2) |
| D5 | **MigrationDependencies PK assignment**: pre-compute at emit time as `MAX(observedSet.Id) + ROW_NUMBER()` | Deterministic; readable diffs; no deploy-time uncertainty | L3-D3 (migration PK determinism) |
| D6 | **V2 owns profile probes** against OSSYS DB | Required for full independence (D2); detection passes need profile data | L3-D6 (User FK reflow totality), L3-C5 (canary green for ≥30+≥4) |
| D7 | **Apply phase is external** | Operator runs DacFx publish / sqlcmd; reduces V2 scope | (scope-bounding, no axiom) |
| D8 | **Tightening as detection, not intervention** | Detection passes exist; profile data is missing input; intervention-axis tuning is out of scope | L3-X1, L3-X2, L3-X6, L3-X7 (diagnostics) |
| D9 | **Connection strings + credentials live outside the unified config JSON** | Eliminates accidental-secret-in-git failure mode by construction | L3-C8 (no credentials in config) |
| D10 | **V2 ships its own log format** | Smallest V2 scope; operator updates downstream | L3-Operational-LoggingContract (candidate) |
| D11 | **CI/CD inventory and migration is operator-owned** | Operator has internal context; plan provides V2 CLI surface as migration target | L3-Operational-CIMigration (candidate; R9 known risk) |
| D12 | **Canonical sort on user-supplied collections** at config-validation time | Sufficient determinism guardrail; rename specifically moot per topo/SsKey orthogonality | L3-C7 (canonical ordering) |

---

## 5. The composition algebra

(New in Draft 3.) The principle by which Phase A and Phase B workstreams compose, and the morphisms that connect operational work to structural commitment.

### 5.1 The workstream signature

Every workstream `W` carries five coordinates:

```
W : (phase, campaign, axiom_set, dependencies, exit_gate)
```

- **phase**: temporal coordinate. `A.x` or `B.x` with an ordinal.
- **campaign**: structural-coordinate cross-cut. One of `A` (cutover-blocker unnamed axioms), `B` (structural fortification), `C` (Tier-2 + boundary VOs + Config strengthening), or `none` (purely operational; no campaign membership).
- **axiom_set**: the L3 axioms `W` operationalizes. Each axiom moves from one bucket to another as a consequence of `W` completing.
- **dependencies**: workstreams that must complete before `W` can begin.
- **exit_gate**: the verifiable claim that signals `W` is done. Restated as: every axiom in `axiom_set` has reached its target bucket; named property tests pass.

### 5.2 The bucket-promotion morphism

Each workstream produces a **bucket-promotion delta** — a typed transition on the audit's coverage map. The transition shape:

```
Δ : axiom_id × bucket_before → bucket_after
```

Phase A's exit invariant is: for every L3 axiom in the target set, `Δ(axiom)` ends at Bucket A or B; no Tier-1 axiom in Bucket D.

Phase B's exit invariant is: the full L3 catalog is covered; canary green across the 4-env × 4-artifact matrix; cutover-ladder per-pair counter reaches N=10 + operator sign-off (R6).

### 5.3 The five morphisms of concern

Every piece of work in this plan can be traced through five morphisms:

1. **Use case → L3 axiom.** The operator's needs (extract-model, full-export-with-overrides) decompose into testable L3 claims.
2. **L3 axiom → L2 axiom.** Each L3 claim is underwritten by formal axioms in `AXIOMS.md` (or is a Bucket-D candidate awaiting L2 promotion).
3. **L2 axiom → L1 commitment.** Each formal axiom is realized by smart constructors, closed DUs, or value objects in F# code.
4. **L1 commitment → test.** Each commitment is property-tested or example-tested.
5. **Test → cutover criterion.** Cutover ladder rungs flip on test-witnessed axiom-bucket invariants.

These compose: a single piece of work traverses all five morphisms. The plan-of-record makes the chain visible per workstream.

### 5.4 Campaign membership as cross-cutting tag

The audit's three campaigns (A / B / C) are not separate phases. They are **cross-cutting workstream tags** that classify the structural-commitment work within Phase A / Phase B:

- **Campaign A — cutover-blocker unnamed axioms.** Four Tier-1 Bucket-D axioms: atomic emission, no-silent-drop, redeploy-idempotence/CDC-silence, manifest-matches-disk. Three of four are *already in Phase A* (no-silent-drop = A.0''s completion criterion; CDC-silence = chapter 4.1.B in flight). Two genuinely new workstreams (atomic emission, manifest-matches-disk) slot into A.7.
- **Campaign B — structural fortification.** Catalog cross-field invariants (1 → 8), name-uniqueness invariants, equality+ordering, A28-style smart constructors. Subsumes original Slice 2 and Slice 3. Distributes across A.2 (emitter wiring), A.4 (rename plumbing), and a new A.4.5 (cross-field-invariant batch).
- **Campaign C — Tier-2 + boundary VOs + Config strengthening.** Typed Config DUs, RelativePath VO, SqlAuthMode DU, cross-platform encoding axioms, diagnostic-code stability, ColumnRealization typed lifts. Distributes across A.0 (Config schema), A.1 (CLI surface), A.5 (profile ingestion), and B.4 (orchestration + logging format).

Within Phase A (or B), workstream order is determined by dependency + concern, not by campaign membership. The Campaign tag is a *property* of the workstream, not a sequencing constraint.

### 5.5 The two parallel axes

The plan is organized along two orthogonal axes:

- **The temporal axis (Phase A → Phase B).** What the operator can do at each cutover ladder rung.
- **The structural axis (Bucket D → Bucket B → Bucket A).** What V2 commits to by construction.

A workstream's coordinate is `(phase, campaign)`. A workstream's effect is a Δ on the structural axis.

After Phase A, the temporal axis flips: V2 emits from V1 inputs; soak gates the ladder rung. After all bucket-D Tier-1 axioms reach Bucket A in Phase A, the structural axis is sufficient for V2-augmented mode.

After Phase B, the temporal axis flips again: V2 owns extraction; the canary verifies; cutover ladder per-pair counter accumulates. After the full L3 catalog reaches its target bucket, the structural axis is sufficient for V2-driver mode.

---

## 6. Phase A — Soak path

**Goal:** V2 emit produces the full artifact set from V1-extracted `osm_model.json` and V1-captured profile JSON, with config-driven overrides and an IR that carries every V1 schema concept the operator's workload uses. Validate against the operator's real workload via differential testing (functional equivalence) before Phase B begins.

**Phase A exit invariant:** for every L3 axiom in `{ L3-S1, L3-S2, L3-S4 through L3-S10, L3-D1 through L3-D6, L3-I1 through L3-I8, L3-X1, L3-X2, L3-X4, L3-X9, L3-C1, L3-C5, L3-C7, L3-C8, L3-CC1, L3-CC3, L3-CC4 }`, `Δ(axiom)` ends at Bucket A or Bucket B. Zero Tier-1 axioms remain in Bucket D. The diff against V1 outputs on operator's representative workload is functional-equivalence clean.

### 6.0 A.0 — Unified config schema

**Operational deliverable:** typed F# model in `Projection.Pipeline.Config` representing the unified config; JSON parser/validator; schema documented at `sidecar/projection/docs/config-schema.md`.

**Campaign:** C.3 (parse-time strengthening — partial; full strengthening lands as C.3 completes).

**Axioms operationalized:** L3-X9 (config errors structured), L3-C8 (no credentials in config), L3-C7 (canonical ordering).

**Bucket promotion:**
- L3-X9: D → A.
- L3-C8: D → A.
- L3-C7: foundational — D12 declared, sort applied at config-validation time once consumers exist.

**Dependencies:** none.

**Exit gate:** `ConfigTests` cover round-trip, missing required, malformed JSON, D9 credential rejection, full-schema round-trip, error aggregation. 19 ConfigTests + 4 fromFile tests passing.

**Status:** **Shipped at A.0 (commit `93468a3`) + A.1 (`df18bbf`).**

### 6.0' A.0' — IR fidelity lifts (Campaign A.2 + B prerequisite)

**Operational deliverable:** V2's `Catalog` carries every schema concept enumerated in §3.3. Each lift is a typed Catalog field (or an explicit deferral with rationale).

**Campaign:** A.2 (no-silent-drop) — A.0''s completion criterion *is* L3-Boundary-NoSilentDrop's structural enforcement. Also Campaign B (smart-constructor sweep) where the new fields gain smart constructors.

**Axioms operationalized:** L3-S4 (Trigger), L3-S5 (Sequence), L3-S6 (DEFAULT), L3-S7 (Computed), L3-S8 (CHECK), L3-S9 (ExtendedProperties + Descriptions), L3-S10 / L3-I10 (Catalog coordinate), L3-CC4 (IR fidelity for production), L3-Boundary-NoSilentDrop.

**Bucket promotion (post-A.0'):**
- L3-S4 through L3-S10: D → A.
- L3-I10: D → A.
- L3-CC4: D → A.
- L3-Boundary-NoSilentDrop: D → A (the axiom's structural enforcement is "every field listed has a typed home OR a Diagnostic.Severity=Error at the adapter").

**Dependencies:** A.0 (typed config provides allowMissingPrimaryKey / allowMissingSchema overrides for partial-coverage edge cases).

**Tasks (one slice each; each closable via sidecar's chapter-close convention):**

- **Trigger lifts**: add `Catalog.Triggers : Trigger list` with `Definition: string`, `IsDisabled: bool`, lineage to source attribute. OSSYS `CatalogReader` populates from V1's `TriggerModel`.
- **Sequence lifts**: add `Catalog.Sequences : Sequence list` with StartValue, Increment, Min, Max, IsCycleEnabled, CacheMode. Adapter populates from V1's `OsmModel.Sequences`.
- **Temporal lifts**: extend `ModalityMark` with `Temporal of TemporalConfig` carrying history-schema, history-table, period columns, retention policy.
- **DEFAULT lifts**: add `Attribute.DefaultValue : SqlLiteral option`.
- **Computed-column lifts**: add `Attribute.Computed : ComputedColumnConfig option`.
- **CHECK lifts**: add `Kind.ColumnChecks : ColumnCheck list` with name + CHECK clause text.
- **Catalog-coordinate lift**: extend `TableId` to `{ Catalog: string option; Schema; Table }`; default Catalog to None for current single-DB case.
- **ExtendedProperties lifts**: add to `Module / Kind / Attribute / Index` as `ExtendedProperties: ExtendedProperty list`.
- **Description lifts**: add `Description: string option` to Kind metadata + Attribute metadata.
- **IsExternal / Origin mapping audit**: clarify in `CatalogReader` adapter; add adapter property test ensuring V1 `IsExternal=true → V2 Origin = ExternalViaIntegrationStudio | ExternalDirect`.
- **IsActive lifts**: add `IsActive: bool` to Module / Attribute (Kind already has).
- For each lift: differential property test against a fixture catalog asserting round-trip preservation.

**Out of scope for A.0'** (deferred-with-rationale; see §11):
- `OriginalName` (prior attribute names) — renames handled at cutover, not embedded in model.
- `ExternalDatabaseType` — V2's `PrimitiveType` abstraction is intentional per AXIOMS A13.
- `IndexColumnDirection` (per-column asc/desc) — acceptable loss per 2026-05-10 vestigial-fields convention.
- `IsPlatformAuto` index flag — presentation-only.

**Exit gate:** every concept in §3.3 has either a typed Catalog field (with smart constructor) or a structured-error path at the OSSYS adapter boundary. Differential property test confirms round-trip on operator's representative workload. L3-Boundary-NoSilentDrop verified by property test `NoSilentDropTests.``every-V1-concept-either-carried-or-errored``.

**Estimated effort:** 3-4 weeks (5-7 slices, sidecar's standard chapter cadence).

### 6.1 A.1 — CLI surface upgrade

**Operational deliverable:** `Projection.Cli` accepts `--config <path>` for `emit`; connection sources via `--connection-string-env <VAR>` or `--connection-file <path>` (the latter unused in Phase A — V2 doesn't connect to OSSYS during Phase A).

**Campaign:** C.3 (CLI argv parser strengthening), partial.

**Axioms operationalized:** L3-X9 (config errors structured) extended to CLI errors; L3-Boundary-CLIDisambiguation (candidate, ambiguous CLI forms rejected).

**Bucket promotion:** L3-X9 extends to cover CLI paths.

**Dependencies:** A.0.

**Exit gate:** `projection emit --config example.json` runs end-to-end with the full Phase A emitter set; legacy positional form retained as deprecated shorthand; CLI smoke tests pass.

**Status:** **Shipped at A.1 (commit `df18bbf`).**

### 6.2 A.2 — Emitter wiring under EmissionPolicy gates

**Operational deliverable:** all built-but-hidden emitters fire from `Compose.project` when their gate is open per config (StaticSeeds, MigrationDependencies, Bootstrap, DecisionLog, Opportunities, Validations, Dacpac).

**Campaign:** B (subsumes original Slice 2's invariant work — Catalog cross-field invariants extend `Catalog.create` while emitter wiring extends `Compose.project`).

**Axioms operationalized:** L3-D7, L3-D8, L3-D9, L3-D10 (data emitters under EmissionPolicy); L3-X1, L3-X2 (DecisionLog / Opportunities / Validations).

**Bucket promotion:**
- L3-D7 / L3-D8: D → A.
- L3-D9: D → A.
- L3-D10: D → A.
- L3-X1 / L3-X2: B → A (signature-enforced once wired).

**Dependencies:** A.0', A.0 (Config gates feed EmissionPolicy).

**Tasks:**
- Refactor `Compose.project` to accept `ValidatedConfig` rather than hardcoded empties.
- Wire StaticSeeds / MigrationDependencies / Bootstrap (under `emission.staticSeeds` / `migrationDependencies` / `bootstrap`).
- Wire DecisionLog / Opportunities / Validations (under `emission.decisionLog` / `opportunities` / `validations`).
- Wire DacpacEmitter (under `emission.dacpac`).
- Honor `dynamicData.insertMode` (PerEntity vs SingleFile output layout).
- Honor `dynamicData.deferJunctionTables` and `dynamicData.staticSeedParentMode` in `DataEmissionComposer`.
- Integration tests for each gate (on, off, all-on, all-off).

**Exit gate:** with config-driven gates, V2 emit produces SSDT + dacpac + Static seeds + Migration deps + Bootstrap + DecisionLog + Opportunities + Validations from one run.

### 6.3 A.3 — StaticData and MigrationDependencies JSON loaders

**Operational deliverable:** two operator-facing JSON formats with corresponding loaders. Static-data fixtures populate static-entity seed rows; migration-dependency rows are appended with auto-assigned PKs.

**Campaign:** none directly (operational); Campaign B picks up smart constructors for the loader output types.

**Axioms operationalized:** L3-D7 (StaticData schema-validated), L3-D8 (MigrationDeps schema-validated), L3-D3 (PK assignment deterministic; per Q1 resolution V2 observes its own dataset — no V1 modification).

**Bucket promotion:**
- L3-D7 / L3-D8: D → A (via loader smart constructors).
- L3-D3: B → A (smart-constructed PK-assignment pass).

**Dependencies:** A.0, A.0', A.2.

**Tasks:**
- `Projection.Pipeline/StaticDataLoader.fs` — parse + resolve to (schema, table) tuples; validate column existence against Catalog.
- `Projection.Pipeline/MigrationDependencyLoader.fs` — parse + resolve kind keys; validate column existence.
- New pre-emit pass `MigrationDependencyPkAssignmentPass` — given the observed-set MAX (per Q1: profile-supplied if present, else static-data fixture MAX, else 0) + parsed migration document, produce fully-PK'd `MigrationDependencyContext`. PK assignment edge cases per Q13: identity-overflow detection, deterministic ordering, no-observed-max baseline.
- Wire StaticData → StaticSeedsEmitter input.
- Wire MigrationDependencyContext → MigrationDependenciesEmitter.
- Tests: malformed JSON, unknown table/kind, unknown column, type mismatches, identity overflow, deterministic PK ordering.

**Exit gate:** operator-provided JSON for both static-data and migration-dependencies produces correctly-emitted MERGE statements in `out/`.

### 6.4 A.4 — Table-rename plumbing with canonical ordering

**Operational deliverable:** config-declared table renames apply to `Catalog` before emitters run. Rename entries canonical-sorted at config-validation time (per D12).

**Campaign:** B (Catalog rewrite passes use established CatalogTraversal.mapKinds; smart-constructor discipline).

**Axioms operationalized:** L3-I1 (SsKey stable under rename), L3-I7 (Name ⊥ SsKey), L3-C7 (canonical ordering).

**Bucket promotion:**
- L3-I1: D → A (Slice 1 PhysicallyRenamed shipped at commit `9d578cc`).
- L3-I7: B → A.
- L3-C7: A → A (already; property test extended).

**Dependencies:** A.0 (rename overrides in config), A.0' (typed VOs in TableId via Campaign C.1).

**Status:** **Shipped at A.4 (commit `502592f`) + Slice 1 PhysicallyRenamed (`9d578cc`).** Rename-order-invariance property test enforces L3-C7. Lineage emits typed `PhysicallyRenamed of PhysicalRename` payload structurally.

### 6.4.5 A.4.5 — Catalog cross-field invariants batch (Campaign B core)

**Operational deliverable:** extend `Catalog.create` with the 7 additional cross-field invariants from audit Part VI.2 (the original Slice 2 was 1 of 8).

**Campaign:** B (the deepest single change in the merge).

**Axioms operationalized:** L3-Catalog-IdentityNullCoherence (new candidate), L3-Catalog-MandatoryNullCoherence (new candidate), L3-Catalog-PrimaryKeyUniqueIndexCoherence (new candidate), L3-Catalog-TypeShapeCoherence (new candidate), L3-Catalog-LengthBoundedCoherence (new candidate), L3-Catalog-PKOrderingCoherence (new candidate), L3-Catalog-SingleStaticModality (new candidate), L3-Catalog-PhysicalNameUniqueness (new candidate; was original Slice 2).

**Bucket promotion:** 8 new axioms enter at Bucket A immediately via smart-constructor enforcement.

**Dependencies:** A.0' (typed fields must exist before invariants are checked).

**Tasks:**
- Extend `Catalog.create` (in `Projection.Core/Catalog.fs`) with 8 invariant checks; aggregate errors via the existing `Result.aggregate` pattern.
- Confirm no existing test fixtures violate the new invariants (or update fixtures with rationale).
- Add error codes: `catalog.attribute.identityNullableContradiction`, `catalog.attribute.mandatoryNullableContradiction`, `catalog.index.primaryKeyNotUnique`, `catalog.attribute.typeShapeMismatch`, `catalog.attribute.lengthBoundExceeded`, `catalog.attribute.pkOrderingDiscontinuous`, `catalog.kind.multipleStaticModalities`, `catalog.kind.duplicatePhysicalName`.
- 8 dedicated property tests; round-trip property test on operator's representative workload.

**Exit gate:** 8 new invariants enforced at `Catalog.create`; existing tests green; new invariants tested.

**Estimated effort:** 1-2 weeks. Mechanically simple; concentrated; very high leverage.

### 6.4.6 A.4.6 — Name uniqueness invariants batch (Campaign B)

**Operational deliverable:** extend `Catalog.create` and `Module.create` with the 7 name-uniqueness invariants from audit Part VI.3.

**Campaign:** B.

**Axioms operationalized:** L3-Catalog-ModuleNameUniqueness, L3-Module-KindNameUniqueness, L3-Kind-AttributeNameUniqueness, L3-Kind-ReferenceNameUniqueness, L3-Kind-IndexNameUniqueness, L3-Index-ColumnUniqueness, L3-Static-RowIdentityUniqueness (all new candidates).

**Bucket promotion:** 7 new axioms enter at Bucket A.

**Dependencies:** A.4.5 (the cross-field invariants batch establishes the smart-constructor expansion pattern).

**Tasks:** as in §6.4.5 — invariant checks, error codes, property tests.

**Estimated effort:** 1 week.

### 6.4.7 A.4.7 — Transform registry: canonical strongly-typed cross-cutting surface (Campaign B core; load-bearing for laboratory-quality scale)

**Operational deliverable:** ship `Projection.Core/TransformRegistry.fs` as the canonical strongly-typed surface for every transformation site in V2 (sibling to Lineage / Diagnostics / Bench as a cross-cutting structural-evidence layer); full-sweep refactor of every existing pass module + the 25 OSSYS adapter rules + emitter strategies to expose `<PassName>.registered : RegisteredTransform<'In, 'Out>` as the primary public surface; `LineageEvent` gains `Classification : DataIntent | OperatorIntent of OverlayAxis` field; `Compose.run` refactored to traverse the registry as its execution loop; `Compose.runWithSkeleton` filters the traversal to `DataIntent`-only sites; `osm emit --skeleton-only` CLI flag; `ManifestEmitter` extension with `registry.digest` + per-artifact `applied-transforms : (SsKey × OverlayAxis option) list`; bidirectional property tests (skeleton-purity + overlay-exercise + totality coverage + Tolerance-cross-reference).

**Campaign:** B (structural fortification; co-equal load-bearing with A.4.5 / A.4.6; co-equal-Tier-1 load-bearing with CDC silence per `V2_DRIVER.md` per-axis stakes table).

**Axioms operationalized:** **L3-CC-Transform-Totality** (the load-bearing operator promise; the cross-cutting structural-evidence-layer commitment; the dichotomy enforced bidirectionally). **A41 candidate** (`AXIOMS.md` Amendments scheduled — Transform registry totality + canonical strongly-typed shape) cashes here. **Pillar 9** (`DECISIONS 2026-05-15 (late) — harvest-dichotomy classification`) gains its structural enforcement seam here.

**Bucket promotion:** L3-CC-Transform-Totality: **D → A** (registry totality + dichotomy enforcement promote from convention to smart-constructor-grade structural enforcement).

**Dependencies:**
- **A.0' (chapter A.0') must close before A.4.7 begins.** The registry enumerates against the post-IR-fidelity pass set; opening A.4.7 before A.0' closes would lock in a pass set still being extended (Triggers, Sequences, DEFAULT, Computed, CHECK constraints, ExtendedProperties IR lifts all introduce or touch passes downstream).
- **A.4.7-prelude small slice** (within or just after A.0', before A.4.7 proper): `LineageEvent` gains a `Classification : Classification` field. Existing pass drivers update via writer-fidelity discipline canonical primitives. No traversal yet; events self-classify. This is the *minimum structural commitment that lets pillar 9 manifest in code while A.0' is still in flight*.
- Soft dependency on A.4.5 / A.4.6 (the smart-constructor expansion pattern is established there); concurrent execution acceptable once A.0' closes.

**Why this is load-bearing for laboratory-quality outcomes at scale.** Three layered reasons:

1. **Pillar 9 (harvest-dichotomy classification) needs a structural enforcement seam.** Without the registry, the discipline lives at code review only. Per the meta-discipline-with-structural-test pattern (pillar 8 + four-question naming + LINT-ALLOW substantive-rationale): each meta-discipline pairs with structural tests. A.4.7 is pillar 9's structural pair.
2. **The chapter-4.x scope expansion grows policy-driven mutations monotonically.** User FK reflow, operational diagnostics, multi-environment policy/profile parameterization each add new transformation sites. Without the registry seam, each new pass is one more convention to track in code review; the named failure mode (*skeleton-overlay drift*) becomes structurally uncatchable as the codebase scales.
3. **A18 amended needs its bidirectional sibling.** A18 forbids `Policy` in emitters (Π-side commitment by structural type). A41 (`OperatorIntent` enumeration; bidirectional property tests) is the Pass-side commitment. The two siblings together carry the dichotomy as a *type-witnessed bidirectional contract*, not a one-sided discipline. Without A.4.7, the structural posture stays asymmetric.

Per `DECISIONS 2026-05-15 (late) — Pillar 9: harvest-dichotomy classification`: this re-opens the 2026-05-13 cash-out under different consumer pressure (skeleton-overlay decomposition + harvest-time classification, not pipeline composition); enumerative + canonical-function-definition, not just name-keyed; compile-time, not reflection.

**The strongly-typed canonical registry shape (per Q3 + Q7 answers — single definition site; unified type parameters across all five stage seams):**

```fsharp
// In Projection.Core/TransformRegistry.fs:

type StageBinding =
    | Adapter           // raw rowset / JSON → Catalog-fragment translations (e.g., OSSYS adapter rules)
    | Pass              // Catalog → Lineage<Diagnostics<Catalog>> (the existing 10 passes)
    | OrderingPolicy    // ordering-policy parameter sites (e.g., SelfLoopPolicy on TopologicalOrderPass)
    | Emitter           // Catalog → ArtifactByKind<'element> (sibling Π emitters)
    | Pipeline          // Compose-level transformations (e.g., TableRename.applyRenames)

type OverlayAxis =
    | Selection
    | Emission
    | Insertion
    | Tightening
    // Per Q9: OverlayAxis = existing Policy DU axes exactly, with room to expand if a fifth axis warranted
    // by real evidence. Today the four Policy axes are exactly the OverlayAxis values; Policy IS operator
    // intent reified.

type Classification =
    | DataIntent                              // preserves data intention; lands in skeleton
    | OperatorIntent of OverlayAxis           // operator-supplied; lands as registered overlay

type TransformSite = {
    SiteName : string                          // e.g., "SortKahn" inside TopologicalOrderPass
    Classification : Classification             // intra-pass classification fidelity (per Q11)
    Rationale : string                         // harvest-discipline analysis prose; cited at code review
}

type TransformStatus =
    | Active
    | NotImplementedInV2 of rationale : string  // v1 harvest classification with no v2 equivalent

type RegisteredTransform<'In, 'Out> = {
    Name : PassName
    Domain : Domain                            // schema / data / identity / diagnostics / cutover-safety / cross-cutting
    StageBinding : StageBinding
    Sites : TransformSite list                 // each Site has its own classification (per Q11)
    Run : 'In -> Lineage<Diagnostics<'Out>>    // typed transformation function itself (per Q3 — registry is canonical)
    Status : TransformStatus
}

// Type-parameter examples per Q7 (unified shape across stage seams):
//   adapter rules:    RegisteredTransform<RawRowSet, CatalogFragment>
//   passes:           RegisteredTransform<Catalog, Catalog>
//   emitters:         RegisteredTransform<Catalog, ArtifactByKind<'element>>

module TransformRegistry =
    let all : RegisteredTransform<obj, obj> list = [
        // top-level evaluation order; each pass module's `<PassName>.registered` is referenced here.
        // F# type erasure on 'In/'Out happens at the registry boundary; per-Run invocation uses the
        // pass module's typed export directly.
        unbox CanonicalizeIdentityPass.registered
        unbox NullabilityPass.registered
        unbox TopologicalOrderPass.registered
        unbox TableRenamePass.registered
        // ... ~10 passes + 25 OSSYS adapter rules + emitter strategies + ordering policy sites
    ]

    let allInStageOrder : RegisteredTransform<obj, obj> list =
        all |> List.sortBy (fun rt -> stageOrdinal rt.StageBinding)
```

**Tasks (full-sweep scope per Q6 — ~3 weeks estimated; significantly larger than the original 1.5-2 week estimate):**

1. **TransformRegistry module** (`src/Projection.Core/TransformRegistry.fs`).
   - Implement the type system above; smart constructor `TransformRegistry.create` enforces invariants (every `Name` unique within registry; every `Domain` is codified-concerns-set; every Site has non-empty Rationale; `Status = NotImplementedInV2 r` requires `r ≠ ""`).
   - The `all` list is hand-maintained; each pass module's `.registered` is referenced explicitly. F# top-level evaluation order resolves dependencies.

2. **Full-sweep refactor: every existing pass module exposes `.registered` as primary surface.**
   - **~10 pass modules** in `Projection.Core/Passes/`: `CanonicalizeIdentity`, `NamingMorphism`, `NormalizeStaticPopulations`, `SymmetricClosure`, `TopologicalOrderPass`, `VisibilityMask`, `NullabilityPass`, `UniqueIndexPass`, `ForeignKeyPass`, `CategoricalUniquenessPass`. Each pass module rewrites: `let run` becomes private; `let registered : RegisteredTransform<…> = { … ; Run = run }` is the new primary export.
   - **~25 OSSYS adapter rules** in `Projection.Adapters.Osm/CatalogReader.fs` (the 25 translation rules from chapter 2). Each transformative rule (filters, remaps, derivations — per Q7 boundary, not pass-through mappings) gets a `RegisteredTransform<RawRow, CatalogFragment>` entry. Pass-through rules (field-A-maps-to-field-B) stay as before — they're translations, not transformations.
   - **Emitter strategies** in `Projection.Targets.SSDT/*Rules.fs` (NullabilityRules, UniqueIndexRules, ForeignKeyRules, CategoricalUniquenessRules, CycleResolution): each strategy's outcome production gets a `RegisteredTransform` entry. (Note: `Composition.fanOut` is the strategy-dispatch primitive; registry classification is at the strategy-decision level, not at the fan-out level.)
   - **Pipeline-level transformations**: `TableRename.applyRenames` in `Projection.Pipeline/Pipeline.fs`. Gets a `RegisteredTransform<Catalog, Catalog>` entry with `StageBinding = Pipeline`.
   - Estimated subtask: ~1.5-2 weeks for the full sweep (each module is ~30-60 LOC of refactor; ~30 modules total).

3. **`LineageEvent.Classification` field** (`src/Projection.Core/Lineage.fs`).
   - Per Q3 (registry-authoritative; LineageEvent looks up classification): every `LineageEvent` is constructed with a `Classification` field that mirrors the `RegisteredTransform`'s classification at the firing pass. The mirror is established by writer-fidelity discipline primitives (`LineageDiagnostics.tellDiagnostics` etc.); the canonical lookup is `TransformRegistry.classificationOf : PassName -> Classification` (returns the SitesList aggregated up if all sites share a classification; returns `OperatorIntent`-aggregate otherwise).
   - This is the A.4.7-prelude small slice (per Dependencies above). Lands before the full traversal refactor.

4. **`Compose.run` traversal refactor** (`src/Projection.Pipeline/Pipeline.fs`).
   - `Compose.run : Catalog -> Profile -> Policy -> outputDir -> Result<string list, ComposeError>` iterates `TransformRegistry.allInStageOrder`; for each registered transformation, invokes `registered.Run` at the appropriate stage seam.
   - `Compose.runWithSkeleton : Catalog -> Profile -> outputDir -> Result<string list, ComposeError>` filters the traversal to `Classification = DataIntent` (Sites containing only `DataIntent` entries; mixed-classification passes contribute only their `DataIntent` Sites; `OperatorIntent` Sites are skipped).
   - `Compose.runWithConfig` continues to exist; layers on top of `Compose.run` for config-driven invocations.

5. **CLI surface** (`src/Projection.Cli/Program.fs`).
   - `osm emit --skeleton-only` invokes `Compose.runWithSkeleton`; binary toggle per Q8. Per-OverlayAxis flags (`--no-tightening`, etc.) deferred-with-trigger per the consumer-pressure principle.
   - The flag is concept-shaped (pillar 8: names what you GET); alternative-considered `--no-overrides` rejected as action-shaped.

6. **ManifestEmitter extension** (`src/Projection.Targets.SSDT/ManifestEmitter.fs` + `Projection.Targets.Json/ManifestEmitter.fs`).
   - Per Q3 / Q12: `registry : { digest : Sha256; transforms : RegisteredTransformMetadata list }` field where `RegisteredTransformMetadata` is the serializable subset of `RegisteredTransform` (drops the `Run` function).
   - Per-artifact `applied-transforms : (SsKey × OverlayAxis option) list` field naming every `LineageEvent.PassName` whose event mentions that artifact's SsKey + the `OverlayAxis` (`None` for `DataIntent` sites; `Some axis` for `OperatorIntent axis` sites).
   - Round-trip property: parsing the manifest yields a `RegisteredTransformMetadata list` byte-equal to the registry at emit time.
   - Per the text-builder-as-first-instinct discipline (Tier-3 codification): manifest extension uses `Utf8JsonWriter` / sorted-key `JsonNode` (existing precedent); zero `StringBuilder()` at the registry-field site.

7. **TransformRegistryCompletenessTests** (`tests/Projection.Tests/TransformRegistryCompletenessTests.fs`).
   - Five bidirectional property tests (per Q12 — both-directions enforcement):
   - `` ``L3-CC-Transform-Totality: Compose.runWithSkeleton emits zero OperatorIntent LineageEvents`` `` — **skeleton-purity property.** Runs `Compose.runWithSkeleton` on a representative canary catalog; asserts every `LineageEvent` produced has `Classification = DataIntent` (or, equivalently, the firing pass's `Sites` filtered to those that ran contain only `DataIntent`). Failure mode: an `OperatorIntent` leaked into the skeleton; pass bypassed the seam OR misclassified.
   - `` ``L3-CC-Transform-Totality: every registered OperatorIntent transformation fires in canary`` `` — **overlay-exercise property.** Runs operator-reality canary with `Bench.recordSample` instrumentation on each registered transformation; asserts every entry with `Classification = OperatorIntent _` fires at least once. Failure mode: dead overlay; mis-registration; canary missing a scenario.
   - `` ``L3-CC-Transform-Totality: every transformation site is in TransformRegistry`` `` — **totality coverage.** Test-time filesystem scan of `Projection.Core/Passes/*.fs` + `Projection.Adapters.Osm/CatalogReader.fs` adapter rules + `Projection.Targets.SSDT/*Rules.fs` strategies; asserts each scanned module's surface has a corresponding registry entry. Per CLAUDE.md "reflection is out of scope for Core" — filesystem scan is at test boundary, not in Core.
   - `` ``L3-CC-Transform-Totality: every Tolerance entry naming a v1 transformation references a NotImplementedInV2 registry entry`` `` — **harvest-classification coverage.** For each `Tolerance` entry tagged as a v1↔v2 transformation divergence, asserts the registry has a `Status = NotImplementedInV2 of rationale` entry whose rationale matches. Catches the triple-deliverable harvest workflow (Skip stub + Tolerance + registry NotImplementedInV2) — if any of the three is missing, the property fails.
   - `` ``L3-CC-Transform-Totality: manifest registry digest matches registry contents`` `` — round-trip property. ManifestEmitter writes `registry.digest`; parser reads; equality holds.

8. **Error codes.** `registry.passUnregistered`, `registry.registeredPassNotExercised`, `registry.digestMismatch`, `registry.duplicatePassName`, `registry.classificationMissing`, `skeleton.policyLeakDetected` (the named failure-mode error for skeleton-purity property failure), `harvest.toleranceWithoutRegistryEntry`, `harvest.notImplementedV2WithoutToleranceOrSkipStub`.

**Tier-3 hard-requirement Active deferral (preserved):** per the text-builder-as-first-instinct discipline (`DECISIONS 2026-05-10`), the `ManifestEmitter` registry-field extension MUST use `Utf8JsonWriter` / sorted-key `JsonNode` (the precedent); a fresh `StringBuilder()` at this site would be a counterfactual to the discipline.

**Exit gate:**
- `TransformRegistry.create` enforces totality at smart-constructor invocation; no parallel enumeration anywhere in `Projection.Core`.
- Every pass module exposes `<PassName>.registered : RegisteredTransform<'In, 'Out>` as its primary public surface; `let run` is private; consumers invoke `registered.Run`.
- `LineageEvent` carries `Classification`; writer-fidelity discipline primitives propagate it; manifest emits the classification per artifact.
- `Compose.run` traverses the registry as execution loop; `Compose.runWithSkeleton` filters to `DataIntent`; `osm emit --skeleton-only` invokes the latter.
- All 5 bidirectional `TransformRegistryCompletenessTests` green.
- Coverage tests fail the build on intentional-fail probes (add a stub pass without registering; add a registered pass with no canary scenario; add a `Tolerance` entry without a `NotImplementedInV2` registry entry — each must fail).
- L3-CC-Transform-Totality moves from Bucket D to Bucket A in the §12 delivery matrix.
- A41 cashes at A.4.7 close per the AXIOMS scaffolding discipline (chapter agent fills the body when A.4.7 closes).

**Estimated effort:** ~3 weeks (significantly larger than the original 1.5-2 week estimate due to full-sweep retroactive scope per Q6). Subtask breakdown: ~1.5 weeks for full-sweep pass / adapter / emitter refactor (~30 modules × ~30-60 LOC each); ~0.5 week for Compose.run traversal refactor; ~0.5 week for LineageEvent extension + writer-fidelity propagation; ~0.25 week for manifest extension; ~0.25 week for bidirectional property tests + intentional-fail probes.

**Anti-scope (NOT this workstream):**
- It is NOT a single linear `pass1 >> pass2 >> pass3` pipeline. Per-use-case driver pattern stands (per `DECISIONS 2026-05-13 — Transform registry cash-out` preserved reasoning); the registry is *enumerative + canonical-function-definition*, not *compositional substrate*.
- It is NOT reflection-based runtime dispatch. Registry contents are compile-time F# `let` bindings; the test-boundary filesystem scan is the only reflection-adjacent surface and lives at the test boundary, not in `Projection.Core`.
- It is NOT a replacement for `Composition.fanOut`. Strategies fan out *within* a pass; the registry enumerates transformation sites *across* the pipeline. Different granularities; both preserved.
- It does NOT introduce per-OverlayAxis CLI flags at A.4.7. `--skeleton-only` is binary per Q8; granular toggling deferred-with-trigger.
- It does NOT refactor `Policy.fs` to hoist Policy axes into `OverlayAxis` as a structural simplification. That refactor is deferred-with-trigger (consumer pressure when real call-sites consult both Policy axes and OverlayAxis values). For A.4.7, `OverlayAxis` is the duplicate-by-design vocabulary (per Q9: "with an opportunity to expand in case we truly find a fifth axis is warranted") — the structural equivalence is acknowledged in DECISIONS but not collapsed at the type level until evidence forces it.

### 6.5 A.5 — Profile-JSON ingestion + completeness audit

**Operational deliverable:** adapter that reads V1's `profile` verb output JSON and hydrates V2's `Profile` type. Required-vs-optional field enumeration with partial-failure semantics.

**Campaign:** B (Profile aggregate-root smart constructor lifts from convention to structural; CdcAwareness.create gains its documented contract enforcement; ForeignKeyReality.create gains empirical-probe invariants).

**Axioms operationalized:** L3-X7 (silent V1 drops documented), L3-D6 partial (User FK reflow uses profile data); plus all Profile-related smart-constructor lifts (Campaign B core).

**Bucket promotion:** several Profile-side smart-constructor types go B → A. Profile.create lands as the aggregate-root smart constructor.

**Dependencies:** A.0, A.2 (detection passes consume profile data via wired emitters).

**Tasks:**
- Audit V1's profile JSON schema. Enumerate every field. Mark each: required-for-Phase-A-emit, optional-with-fallback, advisory-only.
- New `Projection.Adapters.Osm/ProfileReader.fs` mirroring `CatalogReader.fs` in style.
- Required-field validation at parse time. Optional fields produce structured warnings, not errors.
- **Profile.create** smart constructor enforcing: no duplicate `AttributeKey` in `Columns` / `UniqueCandidates` / `CompositeUniqueCandidates`; at most one `AttributeDistribution` per attribute (Categorical XOR Numeric); CdcAwareness invariant; ForeignKeyReality bounds.
- **CdcAwareness.create** smart constructor validating `Map.forall (fun k _ -> Set.contains k enabled) instances`.
- **ForeignKeyReality.create** smart constructor enforcing `OrphanCount ≥ 0` and `HasOrphan ⇔ OrphanCount > 0`.
- Wire into `Compose.run` when config supplies `profile.path`.
- Tests: round-trip, schema-shift handling, partial-failure (probe timed out, one column missing), full-failure (file absent or unparseable).

**Exit gate:** with V1-captured profile JSON supplied via config, V2 emit produces decision logs containing orphan-FK warnings and mandatory-null warnings. Required-field contract documented; Profile / CdcAwareness / ForeignKeyReality smart constructors enforce invariants at construction.

### 6.6 A.6 — Soak (differential testing on functional equivalence)

**Operational deliverable:** reproducible test rig running V2 emit against operator's real workload and comparing outputs against V1's on **functional equivalence**, not byte-identity. Agreed differences recorded in §13.

**Campaign:** none directly (operational gating).

**Axioms operationalized:** L3-C1 (canary asserts V1 ≈ V2 modulo tolerances), L3-C5 (canary green for ≥30 + ≥4).

**Bucket promotion:**
- L3-C1: B → A (functional equivalence is the now-axiomatized form of "modulo tolerances").
- L3-C5: B → A.

**Dependencies:** A.0 through A.5 + A.7 (boundary axioms).

**Tasks:**
- Pick a representative fixture (real production model + profile, or the largest existing test fixture).
- Run V1 full-export → capture outputs.
- Run V2 emit on V1's extracted JSON + profile JSON → capture outputs.
- Build a diff harness (per Draft 2.2's spec; semantic diff over byte diff where shapes diverge intentionally).
- Triage every divergence: V2 bug, V1 bug, or agreed-different (record in §13.2).
- Fix V2 bugs; document agreed differences.

**Exit gate:** functional-equivalence diff is clean (no unexplained divergence). The 4-environment × 4-artifact canary matrix is set up; the per-pair N=10 green-run counter starts accumulating.

### 6.7 A.7 — Boundary axioms (Campaign A net-new work)

**Operational deliverable:** atomic emission + manifest-matches-disk implemented as structural commitments at the Pipeline boundary.

**Campaign:** A (cutover-blocker unnamed axioms; the two net-new items in Campaign A after subtracting A.0' and chapter 4.1.B).

**Axioms operationalized:** L3-Boundary-AtomicEmission, L3-Boundary-ManifestMatchesDisk.

**Bucket promotion:**
- L3-Boundary-AtomicEmission: D → A.
- L3-Boundary-ManifestMatchesDisk: D → A.

**Dependencies:** A.0 through A.6 (boundary axioms gate exit of Phase A; running in parallel with A.5 / A.6 is fine).

**Tasks:**

**A.7.1 — Atomic emission.**
- Promote `Compose.write` to transactional: write each file to a temp path, fsync, atomic-rename to final. On any failure, clean up temp paths and leave the output directory unchanged.
- Add property test inducing a failure midway through `Compose.write`; assert output directory is empty (or contains only files from before the failed run).
- Add L2 axiom (or boundary axiom in PRODUCT_AXIOMS.md per axiom-naming-convention decision).

**A.7.2 — Manifest matches disk.**
- `Compose.write` returns its written paths; `ManifestEmitter` consumes that list, not the in-memory `Outputs` map.
- Property test: post-write, every manifest entry exists on disk; every file on disk has a manifest entry.
- Signature change: `Compose.write : OutputDir → Outputs → Result<{ ManifestEntries: RelativePath list; PathsWritten: AbsolutePath list }>`.

**Exit gate:** induced-failure test passes (output dir untouched on midway failure); manifest-disk reconciliation property test passes.

**Estimated effort:** 3-5 days.

### 6.8 A.8 — CDC-silence canary (Campaign A; co-located with chapter 4.1.B)

**Operational deliverable:** the CDC-silence-on-idempotent-redeploy property test on a production-shape canary.

**Campaign:** A (cutover-blocker unnamed axiom — co-located here because the canary is operational gating).

**Axioms operationalized:** L3-D1 / L3-Idempotence-OnRedeploy.

**Bucket promotion:** D → A.

**Dependencies:** A.2 (data emitters wired), A.5 (profile data for CDC-tracked tables).

**Status:** **In flight at chapter 4.1.B.** Cross-references `CHAPTER_4_1_B_OPEN.md`.

### 6.9 Phase A milestones (Draft 3 revised estimates)

- **A.0 + A.1**: shipped.
- **A.0'**: 3-4 weeks (IR fidelity lifts).
- **A.2**: 1 week (emitter wiring; depends on A.0').
- **A.3**: 1.5 weeks (loaders + auto-PK + edge-case tests).
- **A.4 + A.4.5 + A.4.6**: A.4 shipped; A.4.5 + A.4.6 add 2-3 weeks (cross-field + name-uniqueness invariant batches; mechanically simple, very high leverage).
- **A.5**: 1-1.5 weeks (profile reader + Profile / CdcAwareness / ForeignKeyReality smart constructors).
- **A.6**: 1-2 weeks (soak; +50% buffer per R5).
- **A.7**: 3-5 days (boundary axioms).
- **A.8**: in flight at chapter 4.1.B.

**Phase A total: 9-12 weeks** for one focused developer; ~6-8 weeks with two developers parallelizing (A.0' alongside A.0/A.1; A.4.5/A.4.6 alongside A.5; A.7 alongside A.6).

---

## 7. Phase B — Full independence

**Goal:** V2 connects to OSSYS SQL Server, extracts metadata, captures profile, emits artifacts in a single command. V1 is unnecessary.

**Phase B exit invariant:** the full L3 axiom catalog reaches its target bucket. Canary green across the 4-env × 4-artifact matrix. Per-pair N=10 counter reaches threshold + operator sign-off. V1 sunset eligible at cutover+30 + one schema-evolution cycle (per R6).

### 7.1 B.0 — Foundation

**Operational deliverable:** SQL connection layer + verbatim copy of V1's metadata SQL scripts.

**Campaign:** C (boundary VO: SqlAuthMode closed DU per audit Part VI.6.BB).

**Axioms operationalized:** L3-Boundary-SqlAuth (candidate; replaces D9-related connection-mode docs with structural form).

**Tasks:**
- Copy `src/AdvancedSql/outsystems_metadata_rowsets.sql` (1,184 LOC) and `outsystems_model_export.sql` (931 LOC) verbatim into `sidecar/projection/sql/`.
- Port `SqlConnectionFactory` to F# in `Projection.Adapters.Sql/Connection.fs`. Auth modes: Integrated, SqlPassword, ActiveDirectoryDefault, AccessToken. Cert trust + application name. Connection-source bound from env-var or separate file per D9.
- Closed DU `SqlAuthMode = Integrated | SqlPassword of secretRef | ActiveDirectoryDefault | AccessToken of secretRef`.

**Estimated effort:** 1 week.

### 7.2 B.1 — Result-set binding (highest-risk Phase B work)

**Operational deliverable:** port 25 result-set processors from C# inheritance/factory pattern to F# DU + pattern-match dispatch. Port `MetadataAccumulator` and `ResultSetReader`.

**Campaign:** none directly (operational).

**Axioms operationalized:** L3-I2 (OssysOriginal survives extraction natively; bound resolved post-Phase-B).

**Bucket promotion:** L3-I2 gains unconditional coverage (the JSON-projection-lossiness bound retires through the rowset path).

**Risk:** highest in Phase B. The async-stream lifetime management in `MetadataSnapshotRunner.cs:407` is non-trivial. F#'s `task` workflow handles this, but `CommandBehavior.SequentialAccess` semantics must be preserved.

**Estimated effort:** 2-3 weeks.

### 7.3 B.2 — Snapshot serialization

**Operational deliverable:** F# port of `SnapshotJsonBuilder` and `SnapshotValidator`.

**Campaign:** none directly.

**Axioms operationalized:** L3-S1 cross-platform extension (functional-equivalent osm_model.json across host OSes); L3-I2 unconditional.

**Bucket promotion:** L3-S1's cross-platform Q3/Q4 axes are explicitly addressed (LF line endings; UTF-8 byte-order-mark policy; POSIX path separators in any path fields).

**Tasks:**
- Port `SnapshotJsonBuilder` to F# in `Projection.Adapters.Osm/SnapshotWriter.fs`. Direct `Utf8JsonWriter` calls.
- Port `SnapshotValidator`.
- Verify output is byte-equivalent (or semantic-equivalent with documented diff) to V1's `osm_model.json` for at least one large fixture.

**Note:** `MetadataContractOverrides` (141 LOC) handles **OSSYS-schema-flexibility for optional columns**, NOT SQL-Server-version-dependence. T-SQL queries use stable 2016+ features.

**Estimated effort:** 1 week.

### 7.4 B.3 — Profile probes

**Operational deliverable:** F# port of V1's 5 query builders + `ProfilingQueryExecutor` orchestration. Add `MaxIdentityValueQueryBuilder` for native MAX(Id) capture (resolves Q1 unconditionally for Phase B).

**Campaign:** none directly (operational).

**Axioms operationalized:** L3-D1 / L3-Idempotence-OnRedeploy (CDC silence now backed by V2-native profile probes); L3-D6 (User FK reflow full coverage).

**Bucket promotion:** L3-D1 promoted via the chapter 4.1.B canary; L3-D6 promoted via full UserFkReflow coverage at scale.

**Tasks:**
- Port the 5 query builders.
- Add `MaxIdentityValueQueryBuilder`.
- Port `ProfilingQueryExecutor` (672 C# LOC) orchestration.
- Sampling: respect `--sampling-threshold` / `--sampling-size`.
- Hydrate `Profile` directly via the smart constructor; skip JSON middleman in Phase B.
- Tests: each probe in isolation; full-population end-to-end; partial-failure semantics.

**Estimated effort:** 3-4 weeks.

### 7.5 B.4 — Orchestration + new CLI subcommands + logging-format contract

**Operational deliverable:** `projection extract`, `projection profile`, `projection full-export` subcommands; documented V2 logging format.

**Campaign:** C (logging-format contract; cross-platform encoding axioms).

**Axioms operationalized:** L3-Operational-LoggingContract (Tier-2 axiom, promotes from D to A via formal definition).

**Bucket promotion:** L3-Operational-LoggingContract D → A.

**Tasks:**
- Add `projection extract --config <path>` subcommand. Connection source resolved per D9.
- Add `projection profile --config <path>`.
- Add `projection full-export --config <path>`.
- Define V2's logging format (per D10) — structured properties, event categories. Documented at `sidecar/projection/docs/logging-format.md`. Operator updates downstream tooling.
- Port `ModuleFilter` and `MetadataContractOverrides`.

**Estimated effort:** 1-2 weeks.

### 7.6 Phase B milestones (Draft 3 revised estimates)

- **B.0**: 1 week.
- **B.1**: 2-3 weeks (highest-risk).
- **B.2**: 1 week.
- **B.3**: 3-4 weeks.
- **B.4**: 1-2 weeks.

**Phase B total: 8-11 weeks** for one focused developer; ~5-7 weeks with two developers parallelizing B.1 / B.3.

---

## 8. Cutover criteria (axiom-witnessed)

The criteria are restated from Draft 2.2 as axiom-bucket-witnessed claims. Each criterion is mechanically verifiable from the audit doc's coverage map at any point in time.

### 8.1 Phase A exit (V2-augmented eligibility)

**Structural axis (Bucket-D depletion):**
- For every L3 axiom in `{ L3-S1 through L3-S10, L3-D1 through L3-D6, L3-I1 through L3-I8, L3-X1, L3-X2, L3-X4, L3-X9, L3-C1, L3-C5, L3-C7, L3-C8, L3-CC1, L3-CC3, L3-CC4, L3-Boundary-AtomicEmission, L3-Boundary-ManifestMatchesDisk, L3-Boundary-NoSilentDrop, L3-Idempotence-OnRedeploy }`, the audit's coverage map shows Bucket A or Bucket B.
- Zero Tier-1 L3 axioms remain in Bucket D.

**Operational axis (functional equivalence):**
- Differential diff vs V1 outputs is clean (functional-equivalent on operator's representative workload).
- Config schema doc reviewed and approved by operator.
- No known V2-side defects in emit half.

**Witness:** named property tests pass; the audit doc's coverage map is refreshed at Phase A close (chapter-close ritual).

### 8.2 Phase B exit (V2-driver per-pair eligibility)

**Structural axis (full coverage):**
- The full L3 axiom catalog reaches its target bucket. Bucket-D is empty (modulo deliberately-deferred Tier-3 items, which are listed in §11 with rationale).

**Operational axis:**
- V2 `full-export` runs against OSSYS and produces functionally-equivalent `osm_model.json` to V1.
- V2 profile probes produce functionally-equivalent `Profile` data to V1 (modulo Q1's MaxIdentityValue, which V2 adds natively).
- V2 emit outputs functionally equivalent to V1 per Phase A criterion.
- V2 logging format documented; operator's downstream tooling rewrite complete.
- ≥1 full end-to-end production dry-run completed by operator.
- Cutover-day runbook written (separate document; operator-owned per D11).

### 8.3 V2-driver mode flip (per (environment, artifact type) pair)

- The pair's canary has accumulated N=10 consecutive green runs.
- Operator sign-off recorded.
- R6 governance: V1 stays warm through cutover+30 regardless.

### 8.4 V1 sunset

- T+30 days after Phase B exit + one full schema-evolution cycle on V2 emissions in all four environments + canary green throughout that cycle + operator confirmation.
- T+90 days: V1 archived; `src/` becomes read-only reference.

---

## 9. Risk register

Risks restated where possible as axiom-violation scenarios. Mitigations tied to campaign components.

| # | Risk | Current state | Axiom-violation framing | Mitigation |
|---|---|---|---|---|
| R1 | Operator's "document of key evolutions" diverges from `origin/main` | Outstanding; awaiting delivery | (not an axiom violation — operator-side scope change) | Pause Phase A.0' until operator shares the evolutions doc; revise to Draft 4 before coding starts |
| R2 | V1's profile JSON lacks `MaxIdentityValue` → Phase A.3 auto-PK observes only static-data fixture rows | Low/Low post-Q1 resolution | (Q1 resolved; not a real risk) | V2 observes own dataset; profile-supplied MAX is best-effort, static-data fixture MAX is fallback, starting-at-0 deterministic baseline |
| R3 | Async stream lifetime in `MetadataSnapshotRunner` port (B.1) hits memory/correctness issues | Medium/High | (B.1 implementation risk; not an axiom violation in V2 today) | Port with `task` workflow + explicit `IAsyncDisposable`; integration test against ≥1000-entity catalog |
| R4 | Byte-equivalence of `osm_model.json` (B.2) cannot be achieved due to JSON output ordering | Low/Medium | L3-S1 cross-platform extension (Q3/Q4) — addressed at B.2 | Fall back to functional equivalence; the cross-platform axes (LF / UTF-8 / POSIX paths) explicitly committed at B.2 |
| R5 | Differential diff (A.6) surfaces deep emit-side divergence requiring substantial V2 rework | Medium/High | (operational risk; not an axiom violation) | A.6 budget includes +50% buffer for surprises |
| R6 | Operator's workflow uses V1 capabilities not yet inventoried | Low post-Draft-2 audit | (scope risk) | Continue to verify during soak; §11 catalog comprehensive |
| R7 | OSSYS-side SQL incompatibility in production tenant | Low/High | (operational risk) | `MetadataContractOverrides` handles OSSYS-schema-flexibility; test against operator's actual OSSYS version |
| R8 | IR lifts (A.0') discover unanticipated semantic complexity in V1's domain model | Medium/High | L3-CC4 (IR fidelity for production workloads) — exit gate not reached | Slice per concept; closable independently; if one lift exceeds 1 week, escalate to design review |
| R9 | CI/CD silent break at T+30 (operator-owned per D11) | High/High | L3-Operational-CIMigration violation (axiom not yet formalized; operator-owned) | Operator inventories sites during soak; maps each to V2 config-file equivalent; cuts over before T+30 |
| R10 | Logging format divergence (V2 ships own per D10) | High/Medium | L3-Operational-LoggingContract — operator's tooling rewrite required at cutover | Operator's downstream tooling rewrite happens at cutover; V2 logging format documented at B.4 |
| ~~R11~~ | Determinism breaks under config-driven rename ordering | **Dissolved 2026-05-12 (post-A.4 audit + Slice 1).** | L3-C7 holds by construction: `TopologicalOrderPass` reads SsKey only; `Reference.TargetKind` is SsKey-keyed; rename rewrites only `Kind.Physical`. Property test `D12: rename spec order does not affect the rewritten catalog` enforces. | — |
| R12 | Profile completeness boundary unclear — partial probe failure produces incomplete profile | Medium/Medium | L3-Profile-CompletenessContract (Tier-2 candidate; A.5 addresses) | A.5 enumerates required-vs-optional fields with structured-warning semantics; partial-failure tests in A.5 + B.3 |

**Expected R-dissolutions during campaign execution:**
- R10 dissolves into "operator-owned cutover-task" once L3-Operational-LoggingContract is formalized at B.4.
- R12 dissolves once Profile.create's smart constructor lands at A.5.
- R3 dissolves once B.1's integration tests pass at ≥1000-entity scale.

The pattern of R-dissolution (R11 was the first) is itself a quality signal: as the verifiability triangle's structural commitments accumulate, the risk register shrinks because risks become impossibilities-by-construction rather than concerns-to-mitigate.

---

## 10. Open questions

Numbered for stable reference; resolved questions retained for traceability.

**Q1.** **MAX(Id) source for the auto-PK pass (A.3).** **Resolved 2026-05-11**: no V1 modification. V2 observes its own dataset. Phase A: priority (profile-supplied MAX → static-data fixture MAX → 0). Phase B: V2's own profile probe captures `MaxIdentityValue` natively. **Closed.**

**Q2.** **Argu vs hand-rolled CLI parser (A.1).** Hand-rolled retained at A.1 shipping. Re-open when subcommand × flag matrix grows large enough that Argu pays for itself. **Outstanding (revisable).**

**Q3.** **Backward-compat for V2's existing positional CLI.** Kept as deprecated shorthand with stderr warning. **Outstanding (revisable).**

**Q4.** **Profile JSON shape coupling in Phase B.3.** Default: V2-native primary, V1-compat secondary via `--compatibility-mode` flag if operator needs it. **Outstanding.**

**Q5.** **Dacpac as primary vs SSDT-folder as primary.** Both required per §2.2; emit side-by-side in `out/`. **Resolved.**

**Q6.** **Operator's "document of key evolutions."** Outstanding; triggers Draft 4.

**Q7.** **Sampling thresholds in Phase B.3.** Operator's production catalog characteristics determine defaults. **Outstanding.**

**~~Q8–Q11.~~** Resolved per D9 / D10 / D11 / D12.

**Q12.** **Rename test coverage scope (A.4).** Resolved at Slice 1: unit tests + boundary tests + property test (random rename permutations). **Closed.**

**Q13.** **MigrationDependencies PK edge cases (A.3).** Resolved at Draft 2.1: (i) structured error pointing to observed set; (ii) structured error on identity overflow; (iii) deterministic per canonical sort (D12). **Closed.**

**Q14 (new in Draft 3).** **Campaign A sequencing within Phase A.** **Resolved 2026-05-12**: option (a) — atomic emission first. Shipped at A.7.1 (commit `4e3d944`); `L3-Boundary-AtomicEmission` promoted from Bucket D to Bucket A. The structural pattern (staging-then-replace via `Compose.write` returning `Result<string list>`) is now established for A.7.2 (manifest-matches-disk) to consume. **Closed.**

**Q15 (new in Draft 3).** **Axiom-naming convention.** **Resolved 2026-05-12**: option (b) — separate `L3-Boundary-*` namespace in `PRODUCT_AXIOMS.md`. `AXIOMS.md` A41+ surface stays reserved for algebra-interior extensions (Lifecycle when it lands; A37 erasure when promoted). Boundary axioms cite L2 axioms but do not extend the L2 surface. First instance: `L3-Boundary-AtomicEmission` in `PRODUCT_AXIOMS.md` Group Boundary. **Closed.**

---

## 11. Deferred / out-of-scope

(Operator-confirmed. If any becomes needed, reopen as a separate plan.)

### 11.1 V1 verbs the operator does not use in production
- `dmm-compare`, `analyze`, `inspect`, `policy explain`.

**Note on `uat-users`:** the CLI verb form is **not** deferred. Operator's pending "document of key evolutions" (R1) is expected to expand UAT-users into a more featureful V2 workstream. Until that doc lands, UAT-users scope is held open; see §13.1.

### 11.2 V1 outputs V2 will not produce
- **`.sqlproj`** (SQL Server Database Project file).
- **`SafeScript.sql` / `RemediationScript.sql`**.
- **V1-compatible `osm_model.json` re-emitter.**
- **`evidence-cache/`**, **`telemetry-package.zip`**.
- **UAT-Users artifacts** (user-map CSVs, etc.) — held open pending §13.1.

### 11.3 V1 capabilities deferred-with-trigger
- `--apply` / `--apply-static-seed-mode` phase.
- `--run-load-harness` + variants.
- Per-Catalog Docker parameterization.
- OSSYS User-kind identification in OSSYS adapter.
- CSV adapter for ManualOverride / UserMapLoader.
- `supplementalModels` config block.

### 11.4 V2 plan capabilities deferred
- CI/CD invocation-site inventory and migration (operator-owned per D11).
- Idempotency-test gate in Phase A.6 (operator declined).
- Concurrent-extraction safety (operator coordinates externally).
- V1 logging-format compatibility (D10).

### 11.5 IR concepts deliberately not lifted in A.0'
- `OriginalName` (prior attribute names).
- `ExternalDatabaseType` (raw DBMS type string).
- Per-column `IndexColumnDirection`.
- `IsPlatformAuto` index flag.

### 11.6 L3 axioms deliberately deferred to Tier 3
- Performance promises beyond bench-driven gating.
- Multi-version property stability beyond same-input determinism.
- Semantic equivalence of generated SQL beyond DacFx model-equality (canary handles this).

---

## 12. Per-axiom delivery matrix

Each L3 axiom in `PRODUCT_AXIOMS.md` mapped to the Phase + Workstream that delivers it and the final bucket after delivery. Bucket-A axioms already covered are summarized; Bucket-D entries are highlighted with workstream pointers.

### 12.1 Schema (L3-S1 through L3-S10)

| Axiom | Current bucket | Delivered by | Final bucket |
|---|---|---|---|
| L3-S1 (byte-determinism) | A modulo Q3/Q4 | A.0 (existing) + B.2 (cross-platform) | A |
| L3-S2 (DACPAC round-trip) | A modulo A37 | A.2 + chapter 3.4 close | A |
| L3-S3 (SSDT/DACPAC agreement) | A | (existing T11 + ArtifactByKind) | A |
| L3-S4 (Trigger definitions) | D | A.0' Trigger lift | A |
| L3-S5 (Sequence definitions) | D | A.0' Sequence lift | A |
| L3-S6 (DEFAULT values) | D | A.0' DEFAULT lift | A |
| L3-S7 (Computed columns) | D | A.0' Computed lift | A |
| L3-S8 (CHECK constraints) | D | A.0' CHECK lift | A |
| L3-S9 (ExtendedProperties) | D | A.0' ExtendedProperties lift | A |
| L3-S10 (Catalog coordinates) | D | A.0' Catalog-coordinate lift | A |

### 12.2 Data (L3-D1 through L3-D10)

| Axiom | Current bucket | Delivered by | Final bucket |
|---|---|---|---|
| L3-D1 (CDC silence on redeploy) | D | A.8 / chapter 4.1.B | A |
| L3-D2 (static-seed deterministic order) | A | (existing) | A |
| L3-D3 (migration PK determinism) | B | A.3 (with PK-assignment pass) | A |
| L3-D4 (topological order FK-safe) | A | (existing) | A |
| L3-D5 (cycles via allowlist) | A | (existing SelfLoopPolicy) | A |
| L3-D6 (User FK reflow totality) | B | A.5 + B.3 | A |
| L3-D7 (StaticData schema-validated) | D | A.3 StaticData loader | A |
| L3-D8 (MigrationDeps schema-validated) | D | A.3 MigrationDeps loader | A |
| L3-D9 (Bootstrap covers remaining) | B | A.2 partition assertion | A |
| L3-D10 (emission gates) | D | A.2 emitter wiring | A |

### 12.3 Identity (L3-I1 through L3-I10)

| Axiom | Current bucket | Delivered by | Final bucket |
|---|---|---|---|
| L3-I1 (SsKey stable under rename) | A | **Shipped at Slice 1 (`9d578cc`)** | A |
| L3-I2 (OssysOriginal survives) | A modulo bound | B.1 + B.2 (unconditional) | A |
| L3-I3 (synthesis deterministic) | A | (existing T1) | A |
| L3-I4 (RefactorLog history) | B | A.4 + chapter 3.5 θ/ι | A |
| L3-I5 (V1Mapped identity) | B | B.1 (rowset path) | A |
| L3-I6 (identity stable across evolution) | B | A.4 + chapter 3.5 | A |
| L3-I7 (Name ⊥ SsKey) | A | **Verified at Slice 1** | A |
| L3-I8 (SsKey uniqueness in Catalog) | A | (existing A39) | A |
| L3-I9 (Attribute identity under column rename) | D | chapter 3.2 SnapshotRowsets | A |
| L3-I10 (cross-catalog reference identity) | D | A.0' Catalog-coordinate lift | A |

### 12.4 Diagnostics (L3-X1 through L3-X10)

| Axiom | Current bucket | Delivered by | Final bucket |
|---|---|---|---|
| L3-X1 (every decision → LineageEvent) | B | A.2 (signature-enforced + Campaign B LineageEvent.create) | A |
| L3-X2 (Opportunity routing) | B | A.2 + chapter 4.3 | A |
| L3-X3 (severity matches consequence) | D | chapter 4.3 | A or B |
| L3-X4 (lineage earliest-first) | A | (existing A24) | A |
| L3-X5 (remediation guidance) | D | chapter 4.3 | A or B |
| L3-X6 (detection traceable) | B | A.2 + chapter 4.3 | A |
| L3-X7 (silent V1 drops documented) | D | A.0' + Campaign A.2 | A |
| L3-X8 (tolerance taxonomy logged) | B | chapter 4.1.A M4 | A |
| L3-X9 (config errors structured) | A | (shipped at A.0) | A |
| L3-X10 (multi-env policy diffs) | D | chapter 4.1.A M4 | A or B |

### 12.5 Cutover safety (L3-C1 through L3-C10)

| Axiom | Current bucket | Delivered by | Final bucket |
|---|---|---|---|
| L3-C1 (canary V1≈V2) | B | A.6 (functional equivalence) + R6 governance | A |
| L3-C2 (T-30/T-15 fallback) | (governance) | governance | (governance) |
| L3-C3 (V2 doesn't write during dual-track) | B | R6 + B.1 ReadSide tests | A |
| L3-C4 (per-pair transition) | (governance) | governance | (governance) |
| L3-C5 (canary ≥30+≥4) | B | A.6 + chapter 3.4 | A |
| L3-C6 (CDC redeploy silence) | D | A.8 / chapter 4.1.B (co-located with L3-D1) | A |
| L3-C7 (canonical ordering) | A | **Verified at Slice 1** | A |
| L3-C8 (no credentials in config) | A | (shipped at A.0 D9 guardrail) | A |
| L3-C9 (dry-run completeness) | (governance) | governance | (governance) |
| L3-C10 (V1 sunset gating) | (governance) | governance | (governance) |

### 12.6 Cross-cutting + boundary

| Axiom | Current bucket | Delivered by | Final bucket |
|---|---|---|---|
| L3-CC1 (Π contract) | A | (existing T11 + ArtifactByKind) | A |
| L3-CC2 (Pass contract) | B | Campaign B (`type Pass<'in, 'out>` alias) | A |
| L3-CC3 (lineage monotonic) | A | (existing A24) | A |
| L3-CC4 (IR fidelity for production) | D | A.0' (full) | A |
| L3-CC5 (perf regression gates) | B | (existing perf-gate.sh) | B |
| L3-CC6 (domain-first naming) | B (review-enforced) | (existing) | B |
| **L3-CC-Transform-Totality** | **D** | **A.4.7 (Campaign B core; load-bearing for laboratory-quality scale)** | **A** |
| L3-Boundary-AtomicEmission | D | A.7.1 | A |
| L3-Boundary-NoSilentDrop | D | A.0' completion criterion | A |
| L3-Boundary-ManifestMatchesDisk | D | A.7.2 | A |
| L3-Idempotence-OnRedeploy | D | A.8 / chapter 4.1.B | A |

### 12.7 Campaign B new axioms (introduced via A.4.5 + A.4.6)

15 new L3 candidates enter at Bucket A immediately via Campaign B's smart-constructor expansion (audit Part VI.2 + VI.3). Full statements pending Campaign B implementation; placeholder IDs listed:
- L3-Catalog-IdentityNullCoherence
- L3-Catalog-MandatoryNullCoherence
- L3-Catalog-PrimaryKeyUniqueIndexCoherence
- L3-Catalog-TypeShapeCoherence
- L3-Catalog-LengthBoundedCoherence
- L3-Catalog-PKOrderingCoherence
- L3-Catalog-SingleStaticModality
- L3-Catalog-PhysicalNameUniqueness (was Slice 2)
- L3-Catalog-ModuleNameUniqueness
- L3-Module-KindNameUniqueness
- L3-Kind-AttributeNameUniqueness
- L3-Kind-ReferenceNameUniqueness
- L3-Kind-IndexNameUniqueness
- L3-Index-ColumnUniqueness
- L3-Static-RowIdentityUniqueness

---

## 13. Addenda (append-only)

### 13.X — V2 self-containment + carbon-copy editorial inheritance (2026-05-16 audible)

V2 has zero runtime dependency on V1's trunk — no `ProjectReference`, no V1 assembly on V2's classpath, no Bridge wrapper layer. V1's role in V2 is editorial donor: V2 reads V1's source for inspiration, carbon-copies relevant files into V2's domain-structured locations, cites V1 via a file-header comment + an `ADMIRE.md` row, and refactors freely. The carbon-copy is a one-time editorial event recorded in `BACKLOG.md` § "V1 inheritance log"; subsequent V1 evolution is not automatically tracked.

The F#/C# partition is by language idiom, not by V1/V2 lineage. Pure F# core; F# adapters wrap external libraries (`Microsoft.Data.SqlClient`, `ScriptDom`, etc.); a small, focused, museum-polish C# layer exists only where the gold-standard library is irreducibly C#-idiomatic (SMO, DacFx if pursued). New C# adapter projects (e.g., `Projection.Adapters.OssysSql`) land in `sidecar/projection/src/` per chapter as the consuming chapter opens.

Cherry-pick discipline holds by construction. R6 governance is unchanged (V1 production write path during Stage 1 V2-augmented; gated transition to Stage 2 V2-driver via N=10 canary + operator sign-off). V1 stays warm through cutover+30. V1 sunset begins administratively when V2 has run V2 emissions in every environment for one full schema-evolution cycle and the operator authorizes.

**Cross-references.** `DECISIONS 2026-05-16 (later) — V2 self-containment + carbon-copy editorial inheritance`; `BACKLOG.md` § "V1 inheritance log"; `ADMIRE.md` (per-V1-component records); `CLAUDE.md` operating-disciplines table.

### 13.1 (placeholder) Audit slot for operator's "document of key evolutions"

When operator delivers the evolutions document, append a synthesis subsection here, revise §3 / §4 / §6 as needed, and bump the plan to Draft 4. Operator preview (2026-05-11): evolution doc focuses primarily on UAT-users; likely impact includes UAT-users as Phase A feature, possible sixth core concern, and IR-fidelity extensions if new Catalog/Profile fields required.

### 13.2 (placeholder) Agreed differences between V1 and V2 outputs

During Phase A.6 soak, divergences classified as "agreed-different" recorded here.

Initial entries expected:
- `manifest.json` shape: V1's `SsdtManifest` (342-field record) vs V2's `ArtifactByKind` structure.
- `decision-log.json` / `opportunities.json` / `validations.json` shape: V1's staged structure vs V2's per-kind structure.
- V1's `osm_model.json` vs V2's `JsonEmitter` output — intentionally different files.

### 13.3 (placeholder) Per-milestone close notes

As Phase A.0 / A.0' / ... / B.4 close, append a 5-10 line close note: what shipped, what deferred, what surprised, which axioms promoted.

Confirmed close notes so far:
- **A.0** (commit `93468a3`): Config types + parser + D9 guardrail; L3-X9 / L3-C8 promoted to A; 23 ConfigTests passing.
- **A.1** (commit `df18bbf`): `emit --config` CLI bridge; L3-X9 extends to CLI paths.
- **A.4** (commit `502592f`): TableRename pass + RenameBinding + Compose.runWithConfig; L3-I1 / L3-I7 / L3-C7 promoted to A; R11 dissolved.
- **Slice 1 PhysicallyRenamed** (commit `9d578cc`): `TransformKind.PhysicallyRenamed of PhysicalRename`; rename audit trail now typed.
- **A.7.1 Atomic emission** (commit pending): `Compose.write` refactored to staging-then-replace pattern; `Compose.writeWith` testable seam with injectable `FileWriter`; `Result<string list>` return ripples cleanly through `Compose.run` and `Compose.runWithConfig`; **L3-Boundary-AtomicEmission promoted from Bucket-D candidate to Bucket-A formal** per `PRODUCT_AXIOMS.md` Group Boundary. Q15 axiom-naming convention locked in (`L3-Boundary-*` namespace; `AXIOMS.md` A41+ stays algebra-interior). 7 new property tests in `ComposeAtomicWriteTests` covering happy path, induced-failure with/without sentinel content, no staging leaks, replace-not-merge semantics. 1128 tests passing (1121 baseline + 7 new); zero regressions.

### 13.4 (placeholder) Audit log

- **2026-05-11 audit (Draft 1 → Draft 2):** Six parallel agents. Drove §3.3 IR-fidelity inventory, §3.4-§3.7 cross-cutting sections, D9-D12, R8-R12, Q8-Q13, §6.0' (A.0') new workstream, §6.3 (A.3 split), Phase B.3 estimate revision (2-3 → 3-4 weeks), §11 expanded deferral catalog.

- **2026-05-12 audit (Draft 2.2 — pre-A.4 implementation):** One Explore agent scoped Catalog/rename surface. Findings: R11 dissolved; `CatalogTraversal.mapKinds` confirmed; A39 doesn't enforce physical-name uniqueness (Campaign B); `TransformKind.Renamed` no-payload (Slice 1 added `PhysicallyRenamed`); `SchemaName`/`TableName` VOs unlifted (Campaign C.1). Slice 1 shipped post-audit.

- **2026-05-12 verifiability-triangle audit:** Two rounds of parallel agents. Produced `AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md` (1410 lines), `PRODUCT_AXIOMS.md` (constitutional L3 sibling), and three campaigns (A / B / C) superseding the prior 3-slice airtight plan. Draft 3 (this revision) integrates the campaigns into Phase A / Phase B as cross-cutting workstream tags.

### 13.5 (placeholder) R-dissolution log

Track risks that dissolve through structural commitment.

- **R11 (2026-05-12)**: rename order perturbing topo. Dissolved when audit confirmed `TopologicalOrderPass` reads SsKey only; `Reference.TargetKind` is SsKey-keyed; rename rewrites only `Kind.Physical`.

Subsequent dissolutions append here.

### 13.6 V1-soak debt lane

Tracked alongside (not inside) the Phase A workstreams. Three v1-side fixes that reduce false-positive noise during Phase A.6 soak — each one removes a class of disagreement V2 would otherwise have to either tolerate (Tolerance entry) or attribute (V1 bug, not V2 disagreement). The lane lives here rather than in V1's roadmap because the *value* of fixing them is felt during V2 soak, not during V1 standalone operation. Sequenced as v1-side PRs against the v1 trunk; each one is small and surgical.

See `V2_DRIVER.md` § "V1-soak debt lane" for the lane's full backlog format; this addendum carries the rationale connecting the three vectors to Phase A.6 (the soak workstream) and Phase B (V2 owning extraction + profiling).

| # | Vector | Origin | Why it accelerates Phase A.6 |
|---|---|---|---|
| **V1.1** | Complete `EntityFilters` wiring through `SqlModelExtractionService` + `SqlDataProfiler` + validation scope | V1 (C# trunk). `ModuleEntityFilterOptions` is wired for `SqlDynamicEntityDataProvider` (lines 807-822) but NOT for the metadata extraction or profiling paths — V1 over-fetches and over-validates. | Phase A.6 differential testing compares V1 ≈ V2 on filtered fixtures. Over-fetching on V1 side produces "extra entities V2 didn't ask for" disagreements; either tolerance entries proliferate or V1 emits things V2's filter expects to suppress. Wiring `EntityFilters` end-to-end in V1 makes both halves consume the same selection. |
| **V1.2** | Global topological sort across categories for StaticSeeds emission | V1 (C# trunk). `BuildSsdtStaticSeedStep.cs:82-86` sorts static entities only; cross-category FKs (static → regular, regular → static) violate FK constraints because the global graph isn't built. `BuildSsdtBootstrapSnapshotStep.cs:111-117` does it right (sorts all categories then filters). | Phase A.6 differential testing on workloads with cross-category FKs catches V1's broken StaticSeeds output as "disagreement" when it's actually a V1 bug. Fixing v1 to use the global-then-filter pattern means V2's emission and V1's emission converge on the same FK-correct order. |
| **V1.3** | DatabaseSnapshot dedup — eliminate the 2-3x redundant OSSYS_* query passes across `SqlModelExtractionService` (lines 61-68) + `SqlDataProfiler` (lines 81-88) + `SqlDynamicEntityDataProvider` (lines 575-677) | V1 (C# trunk). Three independent fetch paths query OSSYS_* metadata; no shared snapshot. | Phase B.0–B.2 (V2 owns extraction) consumes V1's manifest as the soak baseline; if V1 keeps three fetch paths that can diverge subtly under concurrent extraction or schema drift between calls, V2's "consume V1 manifest" contract gets more variability than necessary. Single-snapshot V1 = a stable Phase B input. Also reduces operator-side extraction time (~30-40% by elimination of redundant queries). |

**Provenance.** These three vectors originate from a v1 modernization document (`docs/architecture/entity-pipeline-unification-v2.md`) authored before v2's algebraic frame was established. The full document is a v1-refactor proposal, not a v2 plan; this lane surfaces the three highest-leverage items as v1-side debt whose payoff is felt during V2 soak.

**Status:** un-started. Each vector is a v1-side PR (estimated 0.5-1 week each). No campaign tag (these are v1 hygiene, not v2 structural commitments). Owner: v1 maintainers under v1 governance.

**Exit gate:** all three vectors landed in v1 before Phase A.6 soak begins (or, if Phase A.6 begins first, treated as tolerance entries to be retired as the v1-side PRs land).
