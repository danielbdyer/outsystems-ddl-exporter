# V2 — Master Backlog: V1 Parity + Stated Intent

**Date:** 2026-05-08
**Method:** Eight parallel subagent surveys (V1 extraction+domain; V1 profile+tightening; V1 SSDT/SMO emission; V1 data emission+UAT+migration; V1 pipeline+CLI; V1 DMM+AdvancedSql+test infra; V2 intent gaps + ADMIRE + AXIOMS amendments outstanding). Each subagent walked its area exhaustively against `/home/user/outsystems-ddl-exporter/src/` (V1 trunk) and `sidecar/projection/` (V2 sidecar).

**Scope:** This document inventories every V1 capability V2 must reach parity with, every V2-intent commitment per `VISION.md` revision 2, and every cross-cutting governance/documentation gap. Items are tagged by **V2 status**, **recommended chapter**, and **disposition** (parity / divergence-named / widening / defer-with-trigger / cut / won't-carry-forward).

**~375 items.** Cumulative, not chapter-specific. Use the cross-cutting indexes at the end to filter by chapter or status.

---

## Contents

- [Executive summary](#executive-summary)
- [Reading guide](#reading-guide)
- [Section 1: Extraction + Domain (V1 input surface)](#section-1--extraction--domain-v1-input-surface)
- [Section 2: Profile + Tightening passes](#section-2--profile--tightening-passes)
- [Section 3: SSDT/SMO schema emission](#section-3--ssdtsmo-schema-emission)
- [Section 4: Data emission + UAT + Migration](#section-4--data-emission--uat--migration)
- [Section 5: Pipeline orchestration + CLI](#section-5--pipeline-orchestration--cli)
- [Section 6: Auxiliary tooling (DMM + AdvancedSql + test infra)](#section-6--auxiliary-tooling-dmm--advancedsql--test-infra)
- [Section 7: V2 intent gaps + ADMIRE + AXIOMS amendments outstanding](#section-7--v2-intent-gaps--admire--axioms-amendments-outstanding)
- [Cross-cutting indexes](#cross-cutting-indexes)

---

## Executive summary

### Counts by area

| Area | Items | Shipped | In-flight / pre-scoped | Missing / deferred | Won't-carry-forward |
|---|---|---|---|---|---|
| 1. Extraction + Domain | 34 | 19 | 8 | 5 | 2 |
| 2. Profile + Tightening | 70 | 48 | 18 | 4 | 3 |
| 3. SSDT/SMO emission | 76 | 0 (RawText only) | ~58 | ~10 | 8 |
| 4. Data emission + UAT | 60 | 0 | ~50 | ~6 | ~4 |
| 5. Pipeline + CLI | 50 | 1 (JSON canary) | ~38 | ~7 | 4 |
| 6. DMM + AdvancedSql + test | 30 | 4 | 16 | 5 | 5 |
| 7. V2 intent + governance | 55 | 0 | ~25 | ~25 | 0 (governance is "pending", not "won't") |
| **Total** | **~375** | **~72** | **~213** | **~62** | **~26** |

### Counts by chapter ownership

| Chapter | Item count | Notes |
|---|---|---|
| **Chapter 3.1** (read-side adapter + comparator + Projection.Pipeline shell) | ~30 | The immediate-value vehicle; V1 dogfood |
| **Chapter 3.2** (SnapshotRowsets adapter variant) | ~25 | Resolves JSON-projection lossiness; A1 unblock |
| **Chapter 3.3** (DacpacEmitter + DacFx wrapper) | ~25 | Fast-iteration emission surface |
| **Chapter 3.4** (Canary as property-test surface) | ~12 | Tier-1/2/3 properties, FsCheck generators |
| **Chapter 3.5** (RefactorLogEmitter + CatalogDiff) | ~10 | Identity threading, refactor-log XML |
| **Chapter 3-cross-cutting** (ArtifactByKind refactor + SsKey DU + four-variant CatalogDiff) | ~8 | Type-system theorem foundation |
| **Chapter 4.1.A** (SSDT DDL emitter + Manifest) | ~50 | Production SSDT shape, V1-parity layer |
| **Chapter 4.1.B** (CDC-aware data triumvirate) | ~45 | StaticSeeds/MigrationDeps/Bootstrap, EmissionPolicy DU, CdcAwareness |
| **Chapter 4.2** (User FK reflow as Policy) | ~12 | UserMatchingStrategy + UserRemapContext + 4 V1-pipeline-step distillation |
| **Chapter 4.3** (Operational diagnostics V2) | ~15 | DecisionLogEmitter / OpportunitiesEmitter / ValidationsEmitter + Code-prefix routing |
| **Chapter 4.4** (RemediationEmitter) | ~6 | Composition over CatalogDiff + DacpacEmitter; subtractive gate |
| **Chapter 5+** (DMM comparator, LoadHarness, full CLI surface) | ~30 | Post-cutover validation infrastructure |
| **Governance / cross-cutting** (DECISIONS entries, ADMIRE updates, AXIOMS amendments, CLAUDE/HANDOFF docs) | ~50 | Not chapter-owned; ad-hoc or chapter-close ritual |
| **Won't-carry-forward (cut)** | ~26 | V1-specific; documented as intentional non-coverage |

### Top-of-mind risks surfaced

1. **`Projection.Pipeline` C# orchestrator is named across five chapter pre-scopes (3.1, 3.4, 3.5, 4.1, 4.3-4.4) but has no single-owner design contract.** Risk: scope creep, duplicate responsibilities, unclear what it does at each chapter close. **Mitigation:** governance entry naming Projection.Pipeline's surface per chapter (item 7.32–7.37).
2. **AXIOMS.md amendments and DECISIONS.md governance entries pending across chapters** (items 7.1–7.20). Risk: codification debt accumulates; chapter-close ritual must enforce.
3. **V1 has substantial capability V2 chapter-3-4 plans don't fully cover** — particularly DMM comparator (item 6.1–6.3), LoadHarness (item 6.13–6.16), and ~30 V1-only tightening rationales (items 2.18, 2.41–2.43). Most defer-with-trigger to chapter 5+ but some surface during chapter 3 differentials.
4. **CDC-aware MERGE is V2's load-bearing addition** — V1 has no CDC awareness (item 4.19 — V2 growth). The change-detection predicate is the cutover-blocking property (chapter 4.1.B slice 6).
5. **No cutover-fallback decision framework operationalized** (items 7.11, 7.53, 7.54). The three-tier ladder is in VISION; T-30/T-15 gates are not in DECISIONS.md; V1 warm-start is a hard rule but not codified.

---

## Reading guide

This backlog is **cumulative reference**, not a sprint plan. Each chapter pre-scope (`CHAPTER_3_PRESCOPE_*.md`, `CHAPTER_4_PRESCOPE_*.md`) is the operational plan for that chapter; this backlog cross-references which items each chapter delivers.

**When to consult:**
- **Chapter open**: cross-reference items the pre-scope claims against this backlog's "by chapter" index. Identify any items the pre-scope omits.
- **Chapter mid-audit** (per chapter-mid-audit discipline): scan items in flight; surface any that have silently shifted disposition.
- **Chapter close** (chapter-close ritual): confirm shipped items are marked, ADMIRE.md entries land, AXIOMS.md amendments commit, deferred items remain triggered.
- **Pre-cutover (T-30/T-15 gates)**: verify governance items 7.11, 7.53, 7.54 are operationalized; verify acceptance criteria 7.6–7.10 have tracking surfaces.

**Status legend:**
- `shipped` — V2 has it; tests prove it.
- `in-flight` — code in progress; not yet at chapter close.
- `pre-scoped (chapter X.Y)` — the chapter pre-scope names it; not yet implemented.
- `deferred` — named in V2 plan but no chapter owns it; trigger required to re-open.
- `missing` — not in any plan; gap to surface.
- `won't-carry-forward (rationale)` — V2 deliberately does not match V1; cut documented.

**Disposition legend:**
- `parity` — V2 must match V1's behavior.
- `divergence-named` — V2 differs from V1; the divergence is documented (typically as a Tolerance entry in the comparator).
- `widening` — V2 adds beyond V1; new V2 capability.
- `defer-with-trigger` — V2 will eventually carry; named trigger condition.
- `cut` — V2 does not pursue; documented cut.

---

## Section 1 — Extraction + Domain (V1 input surface)

V1 lives at `src/Osm.Pipeline/SqlExtraction/`, `src/Osm.Domain/`, `src/Osm.Pipeline/SnapshotLoader/`, `tests/Fixtures/profiling/`. V2 reads via `Projection.Adapters.Osm.CatalogReader` and writes via `Projection.Targets.Json.JsonEmitter`. Detailed survey of 34 items.

### 1.1 SQL extraction & rowsets (V1 adapter source)

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 1.1 | Module metadata extraction (rowset #1: `#E`) | `outsystems_metadata_rowsets.sql:956–975` | shipped | SsKey synthesis from name only; `EspaceKind`, `EspaceSsKey` dropped through JSON path | 3.2 (SnapshotRowsets) | parity (A1 bound) |
| 1.2 | Entity metadata extraction (rowset #2: `#Ent`) | `outsystems_metadata_rowsets.sql:976–1007` | shipped | `EntitySsKey`, `PrimaryKeySSKey`, `IsSystemEntity` lost in JSON | 3.2 | parity (A1 bound) |
| 1.3 | Attribute metadata extraction (rowset #3: `#Attr`) | `outsystems_metadata_rowsets.sql:1008–1044` | shipped | `AttrSSKey` lost in JSON; `OriginalName`, `LegacyType` only in rowsets | 3.2 | parity |
| 1.4 | Reference extraction (rowset #4: `#RefResolved`) | `outsystems_metadata_rowsets.sql:1045–1053` | shipped (rule 16) | Same-module assumption; cross-module FK handling deferred | 3.2 (cross-module FK slice) | parity-pending |
| 1.5 | Physical table structure (rowset #5: `#PhysTbls`) | `outsystems_metadata_rowsets.sql:1054–1060` | shipped | `ObjectId` not persisted (needed for join keys) | 3.2 | parity |
| 1.6 | **Column physical reality (rowset #6: `#ColumnReality`)** — `IsIdentity`, `IsComputed`, `ComputedDefinition`, `DefaultConstraintName`, `DefaultDefinition`, `CollationName` | `outsystems_metadata_rowsets.sql:1061–1080` | **missing** | V2 IR doesn't model identity / computed / defaults / collation | 3.3+ (IR widening) | parity (deferred) |
| 1.7 | Column check constraints (rowset #7: `#ColumnCheckRow`) | `outsystems_metadata_rowsets.sql:1081–1090` | missing | Check constraints not modeled | 4+ (deferred) | defer-with-trigger |
| 1.8 | Aggregated column-check JSON (rowset #8: `#AttrCheckJson`) | `outsystems_metadata_rowsets.sql:1091–1095` | missing | Pre-aggregated; alternative input surface | 4+ | defer-with-trigger |
| 1.9 | Physical column presence (rowset #9: `#PhysColsPresent`) | `outsystems_metadata_rowsets.sql:1096–1102` | partial | V2 assumes all logical attrs exist physically | 3.2+ | parity-pending |
| 1.10 | Index metadata (rowsets #10 + #11: `#AllIdx`, `#IdxColsMapped`) | `outsystems_metadata_rowsets.sql:1103–1160` | shipped (session 22) | `IsDisabled`, `FilterDefinition`, `DataSpaceName`, `IsPadded`, `FillFactor`, partition/compression metadata not carried | 3.3+ | widening-candidate |
| 1.11 | FK metadata (rowsets #12–17) | `outsystems_metadata_rowsets.sql:1161–1221` | shipped | **`IsNoCheck` flag on FK not modeled** | 3.3 | parity |
| 1.12 | Trigger metadata (rowset #18: `#Triggers`) | `outsystems_metadata_rowsets.sql:1222–1233` | won't-carry | V2 scope: schema only; no trigger logic | — | won't-carry-forward |
| 1.13 | Aggregated JSON rowsets (#19–23) | `outsystems_metadata_rowsets.sql:1234–1341` | shipped (JSON path) | `FOR JSON PATH` aggregations collapse per-column structure; lossiness source | 3.2 | parity (rowsets resolves) |

### 1.2 V1 Domain model (Osm.Domain) types

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 1.14 | Module / Espace type — V1 also carries `EspaceId`, `EspaceKind`, `IsSystem`, `IsActive` | `ModuleModel.cs` | shipped | V2 IR needs `IsSystem` if lossiness fires | 3.2+ | parity |
| 1.15 | Entity / Kind type — V1 also carries `IsExternalEntity`, `DataKind`, `Description`, `Temporal` (versioning), `ExtendedProperties` | `EntityModel.cs` | shipped | `IsSystemEntity`, `Description`, `ExtendedProperties` deferred | 3.2+ | parity-candidate |
| 1.16 | **Attribute / Column type** — V1 also carries `Length`, `Precision`, `Scale`, `IsAutoNumber`, **`IsIdentity`**, **`IsComputed`**, **`ComputedDefinition`**, **`DefaultValue`**, **`SqlType`**, **`Collation`** (via `AttributeOnDiskMetadata`) | `AttributeModel.cs` + `AttributeOnDiskMetadata.cs` | partial | Physical-metadata gap | 3.3+ | parity |
| 1.17 | **RelationshipModel / Reference type** — V1 carries `RelId`, `Name`, `SourceAttrId`, `TargetEntityId`, `DeleteAction`, `UpdateAction`, **`IsNoCheck`** (constraint-trust), `RefEntityName` (cross-module) | `RelationshipModel.cs` | shipped | `IsNoCheck` not in V2 | 3.3 | parity |
| 1.18 | **IndexModel / Index type** — V1 carries `IsDisabled`, `FilterDefinition`, `DataSpace`, `Compression`, `FillFactor` | `IndexModel.cs` | shipped (basic) | Advanced index properties deferred | 3.3+ | widening-candidate |
| 1.19 | EntityMetadata (description / extended properties / temporal markers) | `EntityMetadata.cs`, `AttributeMetadata.cs` | partial | `Description` not modeled | 4+ (deferred) | missing |
| 1.20 | Catalog / OutSystemsInternalModel aggregate (modules + static internals + versioning + tenant markers) | `OutSystemsInternalModel.cs` + `OsmModel.cs` | shipped (core) | Internal-entity handling via `Origin.OsNative` + static-modality | 3.2+ | parity |

### 1.3 JSON snapshot schema (osm_model.json)

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 1.21 | Module JSON shape — fields: `name`, `isSystem`, `isActive`, `entities[]`; **dropped: `espaceKind`, `espaceSSKey`** | `SnapshotJsonBuilder.cs:114–126` | shipped | Lossiness source | 3.2 | parity |
| 1.22 | Entity JSON shape — fields: `name`, `physicalName`, `isStatic`, `isExternal`, `isActive`, `db_catalog`, `db_schema`, `meta`; **dropped: `isSystemEntity`, `entitySSKey`, `primaryKeySSKey`** | `SnapshotJsonBuilder.cs:194–209` | shipped | Three lossiness members | 3.2 | parity |
| 1.23 | Attribute JSON shape — **dropped: `attrSSKey`** (V1 rowset carries it; JSON aggregates) | SQL line 745–809 | shipped | SsKey resolution critical for A1 | 3.2 | parity |
| 1.24 | Index JSON shape — fields: name, isUnique, isIdentity, columns | SQL line 879–909 | shipped (rule 22) | Per-index metadata richer in rowsets | 3.2+ | parity |
| 1.25 | Relationship JSON shape — **missing from rowsets: `isNoCheck` flag** | SQL line 849–878 | shipped (rules 14–16) | V1 physical FK row carries flag; JSON variant truncates | 3.3 | parity |

### 1.4 Model loading & identity

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 1.26 | V1 SsKey (Guid) persistence in rowsets | Rowsets 1, 2, 3, 6 | missing (JSON path) | A1 bound: JSON path loses native Guid; V2 synthesizes | 3.2 | parity (resolved by SnapshotRowsets) |
| 1.27 | Identity thread across schema versions (refactor log XML; UUIDv5 mapping) | (V1 has no equivalent) | pre-scoped (3.5) | RefactorLogEmitter + CatalogDiff land it | 3.5 | parity (V2-growth via 3.5) |
| 1.28 | Static / OutSystemsInternalModel round-trip (hardcoded Users entity) | `OutSystemsInternalModel.cs` | shipped (origin-blind) | V2 routes via `Origin.OsNative` + static-modality | — | parity (V2 divergence-named) |
| 1.29 | Profile shape (data evidence) | `ProfileSnapshot.cs` | shipped (JSON) | V1 collects statistical distributions; V2 consumes structured | — | parity |

### 1.5 Infrastructure & verbs

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 1.30 | SQL execution via `MetadataSnapshotRunner` + `SqlClientOutsystemsMetadataReader` | `MetadataSnapshotRunner.cs`, `SqlClientOutsystemsMetadataReader.cs` | missing (snapshot-only today) | V2 reads JSON output only; SnapshotRowsets will execute rowsets; LiveOssysConnection deferred | 3.2 + 3.2+ | parity-pending |
| 1.31 | JSON deserialization (`ModelJsonDeserializer.cs`, ~1500 LOC) | `ModelJsonDeserializer.cs` | shipped (F# port) | `CatalogReader.parseDocument` | — | parity |
| 1.32 | Snapshot validation | `SnapshotValidator.cs` | partial | V2 validation gates implicit in parsing; full V1 validator deferred | 3+ | parity |
| 1.33 | Model loader / round-trip | `ModelLoader.cs` (V1 orchestration) | shipped (simplified) | `Projection.Pipeline` canary TBD (3.1) | 3.1 | parity-pending |
| 1.34 | DACPAC variant input (if any) | not visible in V1 | missing | V1 uses SMO (live DB) + JSON snapshot, not DACPAC as input; V2 emits DACPAC, not consumes | — | won't-carry-forward |

---

## Section 2 — Profile + Tightening passes

V2's chapter-2-close ships ~80% of this surface; most items are `shipped` (parity). Detailed survey of 70 items.

### 2.1 Profile shape & evidence collection

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 2.1 | ColumnProfile — row count, null count, nullable, default, samples | `ColumnProfile.cs:6–70` | shipped | V2 keyed by `SsKey`; V1 fields like `IsComputed`/`IsPrimaryKey` deferred to Catalog | — | divergence-named (cut from Profile) |
| 2.2 | UniqueCandidateProfile (single-column uniqueness, duplicates flag) | `UniqueCandidateProfile.cs` | shipped | V2 adds `ProbeStatus` for auditability | — | parity + widening |
| 2.3 | CompositeUniqueCandidateProfile (multi-column with participating columns) | `CompositeUniqueCandidateProfile.cs` | shipped | V2 uses `SsKey list` vs V1 physical coordinates | — | widening |
| 2.4 | ForeignKeyReality (orphan count, samples, NO CHECK flag) | `ForeignKeyReality.cs` | shipped | V2 adds `ProbeStatus` | — | widening |
| 2.5 | ProbeOutcome enum (Succeeded / Timeout / Cancelled / TrustedConstraint / AmbiguousMapping) | implicit in V1's `ProfilingProbeStatus` | shipped | V2 clarifies V1's conflation of probe-execution vs observed-reality | — | parity (clearer) |
| 2.6 | Null sample rows (annotated) | `NullRowSample.cs` | deferred | V2 routes to operational diagnostics, not IR | post-3 | defer-with-trigger |
| 2.7 | Orphan sample rows (annotated) | `ForeignKeyOrphanSample.cs` | deferred | V2 routes to diagnostics | post-3 | defer-with-trigger |
| 2.8 | **CategoricalDistribution** — per-value frequency with truncation contract | (missing in V1) | shipped | V2-growth (session 9) | — | V2-growth (new) |
| 2.9 | **NumericDistribution** — percentiles (P25/P50/P75/P95/P99) with monotonicity | (missing in V1) | shipped | V2-growth (session 10) | — | V2-growth (new) |
| 2.10 | **AttributeDistribution union** — closed DU (Categorical / Numeric; extensible) | (missing in V1) | shipped | V2-only | — | V2-growth (new) |

### 2.2 Decision-record shapes & flags

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 2.11 | NullabilityDecision — `MakeNotNull` + `RequiresRemediation` flags + rationales | `NullabilityDecision.cs:6–27` | shipped (renamed) | V2 uses `NullabilityOutcome` DU lifting conflicts to type variants | — | divergence-named |
| 2.12 | **NullabilityOutcome variants** — `EnforceNotNull(evidence)` / `KeepNullable(reason)` / `RequireOperatorApproval(conflict)` | (V2-growth) | shipped | V1 binary flags conflate three states | — | V2-growth |
| 2.13 | NullabilityEvidence DU (PrimaryKey / PhysicallyNotNull / LogicalMandatoryNoProfile / NoNulls / WithinBudget) | implicit in V1 | shipped | Stringly-typed in V1; structured in V2 | — | parity + widening |
| 2.14 | KeepNullableReason DU (OperatorOverride / NoTighteningSignal / RelaxedUnderEvidence) | implicit in V1 | shipped | V2-growth | — | V2-growth |
| 2.15 | NullabilityConflict DU (`MandatoryButHasNullsBeyondBudget` with evidence) | implicit in V1 | shipped | V2 lifts to explicit DU variant | — | V2-growth |
| 2.16 | UniqueIndexDecision — `EnforceUnique` + `RequiresRemediation` | `UniqueIndexDecision.cs:5–20` | shipped (renamed) | V2 `UniqueIndexOutcome` DU | — | divergence-named |
| 2.17 | ForeignKeyDecision — `CreateConstraint` + `ScriptWithNoCheck` + rationales | `ForeignKeyDecision.cs:5–24` | shipped (renamed) | V2 `ForeignKeyOutcome` DU separates NoCheck into justified variant | — | divergence-named |
| 2.18 | **TighteningRationales constants** — 22 stringly-typed enum constants | `TighteningRationales.cs:3–31` | shipped (absorbed into evidence DUs) | V2 eliminates stringly-typed rationales — divergence-named (positive) | — | divergence-named |

### 2.3 Opportunity & validation reports

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 2.19 | OpportunityType enum (Nullability/UniqueIndex/ForeignKey) | `Opportunity.cs:10–15` | shipped | V2 routes by Code prefix in emitter | 4.3 | parity (alternative surface) |
| 2.20 | OpportunityDisposition enum (Unknown/ReadyToApply/NeedsRemediation) | `Opportunity.cs:17–22` | pre-scoped (4.3) | Severity + Code prefix in V2 | 4.3 | parity |
| 2.21 | OpportunityCategory enum (Contradiction/Recommendation/Validation) | `Opportunity.cs:24–51` | pre-scoped (4.3) | Code-prefix routing | 4.3 | parity |
| 2.22 | ChangeRisk record (Level, Label, Description) with Low/Moderate/High/Unknown | `ChangeRisk.cs:5–33` | pre-scoped (4.3) | V2 carries Risk in Diagnostics metadata | 4.3 | parity (metadata) |
| 2.23 | RiskLevel enum (Unknown=0, Low=1, Moderate=2, High=3) | `RiskLevel.cs:1–9` | pre-scoped (4.3) | V2 metadata | 4.3 | parity |
| 2.24 | Opportunity rich record (Type, Title, Summary, Risk, Disposition, Category, Evidence, Column, Index, etc.) | `Opportunity.cs:86–102` | pre-scoped (4.3) | Routes via Diagnostics; OpportunitiesEmitter implements differential | 4.3 | parity |
| 2.25 | OpportunityEvidenceSummary (RequiresRemediation, EvidenceAvailable, DataClean?, HasDuplicates?, HasOrphans?) | `Opportunity.cs:53–58` | pre-scoped (4.3) | Synthesizes at emit time | 4.3 | parity (computed) |
| 2.26 | OpportunityColumn detailed metadata | `Opportunity.cs:60–84` | pre-scoped (4.3) | Enriches from Catalog + Profile + decisions at emit time | 4.3 | parity (computed) |
| 2.27 | ValidationFinding (subset of Opportunity minus Risk/Disposition/Category/Statements/EvidenceSummary) | `ValidationFinding.cs:8–19` | pre-scoped (4.3) | Routes via Code prefix | 4.3 | parity |
| 2.28 | OpportunitiesReport (counts: Disposition/Category/Type/Risk + GeneratedAtUtc) | `OpportunitiesReport.cs:8–23` | pre-scoped (4.3) | V2 computes counts at emit time per Code prefix | 4.3 | parity |

### 2.4 Decision & policy reporting

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 2.29 | PolicyDecisionSet (three decision dicts + diagnostics + identities + toggle snapshot) | `PolicyDecisionSet.cs:7–44` | shipped | V1 carries per-decision toggle snapshot; V2 separates concerns | — | parity |
| 2.30 | PolicyDecisionReport (rationale counts, module rollups, toggle precedence) | `PolicyDecisionReporter.cs:188–221` | pre-scoped (4.3) | DecisionLogEmitter / decision-log.json | 4.3 | parity |
| 2.31 | ColumnDecisionReport (Column, MakeNotNull, RequiresRemediation, Rationales) | `PolicyDecisionReporter.cs:294–302` | pre-scoped (4.3) | Per-SsKey JSON record | 4.3 | parity |
| 2.32 | UniqueIndexDecisionReport (Index, EnforceUnique, RequiresRemediation, Rationales) | `PolicyDecisionReporter.cs:318–326` | pre-scoped (4.3) | Per-SsKey | 4.3 | parity |
| 2.33 | ForeignKeyDecisionReport (Column, CreateConstraint, ScriptWithNoCheck, Rationales) | `PolicyDecisionReporter.cs:304–316` | pre-scoped (4.3) | Per-SsKey | 4.3 | parity |
| 2.34 | ModuleDecisionRollup (per-module counts + rationale dicts) | `PolicyDecisionReporter.cs:223–234` | pre-scoped (4.3) | Module rollups in decision-log | 4.3 | parity (computed) |
| 2.35 | Toggle precedence (`ToggleExportValue` (value + source) + `ToExportDictionary()`) | `TighteningToggleSnapshot.cs:37–126` | shipped | V1 serializes precedence; V2 threads as policy context | — | parity |

### 2.5 Tightening pass engines & rule logic

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 2.36 | NullabilityEvaluator (override → PK → physical → logical mandatory + budget) | `NullabilityEvaluator.cs` | shipped | `NullabilityPass.fs` + `NullabilityRules.fs` algebraically equivalent | — | parity |
| 2.37 | UniqueIndexEvidenceAggregator | `UniqueIndexEvidenceAggregator.cs` | shipped | `UniqueIndexPass.fs` separates outcome + remediation via DU | — | parity + widening |
| 2.38 | UniqueIndexDecisionOrchestrator | `UniqueIndexDecisionOrchestrator.cs` | shipped | `UniqueIndexPass.fs` reframes via DU | — | parity |
| 2.39 | ForeignKeyEvaluator | `ForeignKeyEvaluator.cs` | shipped | `ForeignKeyRules.fs` uses outcome DU | — | parity + widening |
| 2.40 | **CategoricalUniquenessRules** (infers single-column uniqueness from distribution) | (missing in V1) | shipped | V2-growth | — | V2-growth (new) |
| 2.41 | Signal evaluation hierarchy (override > structural > evidence-based) | implicit in V1 evaluators | shipped | `*Rules.fs` makes hierarchy explicit | — | parity |
| 2.42 | NullBudget policy (decimal 0.0–1.0 or count threshold) | `TighteningPolicy.cs` + evaluators | shipped | V2 carries as config | — | parity |
| 2.43 | Override mechanism (operator-directed exclusions per (module, entity, attribute) or (module, index)) | `LegacyPolicyAdapter.cs` | shipped | V1 maps legacy YAML overrides; V2's overrides are policy input | — | parity |

### 2.6 Diagnostic & validation infrastructure

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 2.44 | TighteningDiagnostic record (Code, Message, Severity, LogicalName, Canonical*, Candidates, ResolvedByOverride) | `TighteningDiagnostic.cs:13–22` | shipped | V2's Diagnostics writer subsumes | — | parity |
| 2.45 | TighteningDiagnosticSeverity enum (Info, Warning) | `TighteningDiagnostic.cs:5–9` | shipped | Diagnostics entries carry Severity | — | parity |
| 2.46 | ValidationReport (Validations, TypeCounts, GeneratedAtUtc) | `Validations/ValidationReport.cs:7–15` | pre-scoped (4.3) | ValidationsEmitter | 4.3 | parity |
| 2.47 | TighteningDiagnostic.CreateMandatoryNullConflict factory (mandatory-column-has-nulls, samples, remediation query) | `TighteningDiagnostic.cs:24–47` | pre-scoped (4.3) | V2 routes via Diagnostics entries | 4.3 | parity |

### 2.7 Cycle & topological resolution

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 2.48 | Tarjan SCC algorithm (FK cycle resolution) | `EntityDependencySorter.cs` | shipped | `CycleResolution.fs` adapted | — | parity |
| 2.49 | TopologicalOrderPass (mandatory-edge-only) | implicit in V1 | shipped | Lifted to `TopologicalOrderPass.fs` | — | parity (lifted to pass) |
| 2.50 | FK cycle detection + remediation diagnostics | `EntityDependencySorter.cs` | shipped | V2 detects; handling is policy | — | parity |

### 2.8 Pre-remediation & remediation infrastructure

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 2.51 | PreRemediationManifestEntry (Module, Table, TableFile, Hash) | `SsdtManifest.cs:33–37` | pre-scoped (4.4) | RemediationEmitter manifest | 4.4 | parity |
| 2.52 | **RemediationQueryBuilder** (T-SQL backfill/cleanup queries) | `RemediationQueryBuilder.cs` | cut | V2's operator surface is JSON-only; SQL rendering deferred | — | won't-carry-forward |
| 2.53 | RemediationGeneratePreScripts toggle (policy) | `TighteningToggleKeys.cs:26` | shipped | Threaded via policy; no emitter-side script generation | — | parity |
| 2.54 | RemediationSentinel options (numeric/text/date) | `TighteningToggleKeys.cs:28–30` + `RemediationOptions` | deferred | Schema-only through chapter 4.4 | post-4 | defer-with-trigger |

### 2.9 Profile serialization & deserialization

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 2.55 | ProfileSnapshot JSON serialization (Columns, UniqueCandidates, CompositeUniqueCandidates, ForeignKeys) | `ProfileSnapshotSerializer.cs` | shipped | V2 deserializes and validates against Profile IR | — | parity |
| 2.56 | ProfileSnapshot JSON deserialization | `ProfileSnapshotDeserializer.cs` | shipped | OSSYS adapter traces V1 profile | — | parity |
| 2.57 | ProfilingProbeStatus serialization | `ProfileSnapshotSerializer.cs` | shipped | V2 maps to `ProbeStatus`; clarifies V1's outcome conflation | — | parity |

### 2.10 Policy & configuration

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 2.58 | TighteningOptions (Policy, ForeignKeys, Uniqueness, Remediation, Mocking sub-options) | `TighteningOptions.cs` | shipped | V1 monolithic; V2 per-pass granular configs | — | parity |
| 2.59 | TighteningToggleSnapshot.Create (baseline comparison + source resolver) | `TighteningToggleSnapshot.cs:64–101` | shipped | V2 threads policy context | — | parity |
| 2.60 | ToggleSource enum (Default/Configuration/Environment/CommandLine) | `TighteningToggleSnapshot.cs:7–12` | shipped (renamed) | V2's `Origin` DU generalized | — | parity (generalized) |
| 2.61 | Per-environment policy fan-out (multi-env profiling) | `CaptureProfileApplicationService.cs` | pre-scoped (future) | R4 algebraic shape exists in axioms; concrete impl deferred | post-4 | pre-scoped |

### 2.11 Multi-environment support & consensus

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 2.62 | MultiEnvironmentProfileReport | `MultiEnvironmentProfileReport.cs` | pre-scoped (future) | V1 has multi-env profiler; V2 deferred | post-4 | pre-scoped |
| 2.63 | MultiEnvironmentConstraintConsensus | `MultiEnvironmentConstraintConsensus.cs` | pre-scoped (future) | Trigger: real demand | post-4 | pre-scoped |
| 2.64 | ForeignKeyMappingResolver (cross-env FK references) | `ForeignKeyMappingResolver.cs` | pre-scoped (3+) | Cross-module FK in 4.2 per HANDOFF | 4.2 | pre-scoped |

### 2.12 Utility & indexing infrastructure

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 2.65 | EntityAttributeIndex (lookup by (schema, table, column)) | `EntityAttributeIndex.cs` | shipped | Catalog provides SsKey lookup | — | parity (alternative indexing) |
| 2.66 | ForeignKeyTargetIndex (lookup FK references by target kind + column) | `ForeignKeyTargetIndex.cs` | shipped | Catalog.Reference navigation | — | parity |
| 2.67 | **SsdtPredicateCoverage** (tracks which SSDT predicates are exercised per table) | `SsdtPredicateCoverage.cs:26–48` | cut | V2 is target-agnostic | — | won't-carry-forward |

### 2.13 Summary statistics & rollup

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 2.68 | ReportSummary (per-opportunity-type counts, aggregate stats) | `ReportSummary.cs` | pre-scoped (4.3) | OpportunitiesEmitter computes | 4.3 | parity (computed) |
| 2.69 | Per-module rationale aggregation | `PolicyDecisionReporter.cs:79–95` | pre-scoped (4.3) | DecisionLogEmitter | 4.3 | parity (computed) |
| 2.70 | OpportunityMetrics (risk/disposition/category breakdown) | `OpportunityMetrics.cs` | pre-scoped (4.3) | OpportunitiesEmitter computes | 4.3 | parity (computed) |

---

## Section 3 — SSDT/SMO schema emission

V1's emission is the largest area; V2's chapter 4.1.A pre-scope already names ~10 slices. Detailed survey of 76 items.

### 3.1 Schema & module structure (1–4)

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 3.1 | Module directory grouping (`Modules/<Module>/...`) | `TableEmissionPlanner.cs:232` | shipped | V2 inherits | — | parity |
| 3.2 | Module name sanitization (`My.Module` → `My_Module`) | `SmoNormalization`, `SsdtManifest.cs:25–29` | pre-scoped (4.1.A §3) | V2 sanitizes on output | 4.1.A slice 1 | parity |
| 3.3 | `CREATE SCHEMA IF NOT EXISTS` (V2 new convention; not in V1) | — (V1 implicit per-table) | missing | V2 new convention; tolerance entry | 4.1.A slice 1 | divergence-named |
| 3.4 | Per-table file naming `<Schema>.<Table>.sql` | `PerTableWriter.cs:187` | pre-scoped (4.1.A §3) | Emitter produces relative paths | 4.1.A slice 1 | parity |

### 3.2 CREATE TABLE statement (5–13)

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 3.5 | Schema-qualified CREATE TABLE | `CreateTableStatementBuilder.cs:101–105` | pre-scoped (4.1.A §3) | `[Schema].[Table]` rendering | 4.1.A slice 1 | parity |
| 3.6 | One-column-per-line opinionated formatting | `CreateTableFormatter.cs:17–100` | pre-scoped | Formatter matches V1 layout | 4.1.A slice 2 | parity |
| 3.7 | All 9 PrimitiveType variants | `defaultSqlType` in `RawTextEmitter.fs:31–41` | pre-scoped | Shared `SqlTypeMap.fs` | 4.1.A slice 2 | parity |
| 3.8 | `IDENTITY(seed, increment)` on identity columns | `SmoColumnBuilder.cs:28–30` | pre-scoped (4.1.A §8 slice 7) | `Attribute.IsIdentity` IR field needed; gated on 3.2 | 4.1.A slice 7 | parity-deferred |
| 3.9 | DEFAULT constraint syntax `CONSTRAINT [DF_<Table>_<Col>] DEFAULT (expr)` | `SmoColumnBuilder.cs:34–35, 43` | pre-scoped (4.1.A §8 slice 7) | `Attribute.Default` IR needed | 4.1.A slice 7 | parity-deferred |
| 3.10 | Default constraint naming + length-cap at 128 | `ConstraintNameNormalizer.cs:10–64` | pre-scoped (4.1.A slice 7) | Same normalizer rules | 4.1.A slice 7 | parity |
| 3.11 | CHECK constraints (static discriminators, user-supplied) | `SmoColumnBuilder.cs:45–72` | missing | IR has no CheckConstraint; `IgnoreCheckConstraints` tolerance | 4+ | divergence-documented |
| 3.12 | Computed columns (`COLUMNPROPERTY ... 'IsComputed'`) | `SmoColumnBuilder.cs:31–32` | missing | IR has no `IsComputed`; `IgnoreComputedColumns` tolerance | 4+ | divergence-documented |
| 3.13 | Collation clause `COLLATE <collation>` | `SmoColumnBuilder.cs:41` | missing | IR has no collation field; `IgnoreCollation` tolerance | 4+ | divergence-documented |

### 3.3 Primary key constraints (14–16)

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 3.14 | Single-column PK inline | `CreateTableStatementBuilder.cs:67–77` | pre-scoped (4.1.A §3) | Emitter inlines when 1 attribute | 4.1.A slice 1 | parity |
| 3.15 | Composite PK as table constraint | `CreateTableStatementBuilder.cs:80–97` | pre-scoped (4.1.A slice 4) | Emitter outputs separate constraint | 4.1.A slice 4 | parity |
| 3.16 | PK naming convention `PK_<PhysicalTable>` (length-cap) | `ConstraintNameNormalizer.cs:10–64` | pre-scoped (4.1.A §3) | Inherited from V1 | 4.1.A slice 1 | parity |

### 3.4 Indexes (17–24)

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 3.17 | Non-clustered unique index | `IndexScriptBuilder.cs:56–89` | pre-scoped (4.1.A slice 3) | Emitter filters PK-marked | 4.1.A slice 3 | parity |
| 3.18 | Non-unique index | same | pre-scoped (4.1.A slice 3) | `IsUnique` flag determines keyword | 4.1.A slice 3 | parity |
| 3.19 | Index naming `UIX_<Table>_<cols>` / `IX_<Table>_<cols>` | `IndexNameGenerator.cs:30–35` | pre-scoped (4.1.A slice 3) | Extracted helper | 4.1.A slice 3 | parity |
| 3.20 | Index name normalization & length-capping | `ConstraintNameNormalizer.cs:10–64` | pre-scoped | Same rules | 4.1.A slice 3 | parity |
| 3.21 | Disabled index `ALTER INDEX [...] DISABLE` | `PerTableWriter.cs:134–139` | pre-scoped (4.1.A slice 3) | IR needs `Index.IsDisabled` | 4.1.A slice 3 | parity-deferred |
| 3.22 | Index with INCLUDE (non-key columns) | `IndexScriptBuilder.cs:67–70` | missing (cut) | V2 convention omits | — | won't-carry-forward |
| 3.23 | Filtered index (`WHERE <predicate>`) | `IndexScriptBuilder.cs` | missing | IR has no filter expression; `IgnoreFilteredIndexes` tolerance | 4+ | divergence-documented |
| 3.24 | Index ordering (alphabetically by name, case-insensitive) | `PerTableWriter.cs:160–163` | pre-scoped (4.1.A §6) | Sort by `Index.SsKey`; canonicalization aligns | 4.1.A slice 3 | parity |

### 3.5 Foreign keys (25–31)

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 3.25 | Intra-module FK inline | `CreateTableStatementBuilder.cs:108–170` | pre-scoped (4.1.A slice 5) | Emitter inlines same-module FKs | 4.1.A slice 5 | parity |
| 3.26 | Cross-module FK inline (V1 parity; may diverge post-3.2) | `CreateTableStatementBuilder.cs:158–163` | pre-scoped (4.1.A slice 6) | Emitter resolves via `Catalog.tryFindKind`; tolerance entry | 4.1.A slice 6 | parity-divergence |
| 3.27 | Deferred NOCHECK FK as separate ALTER TABLE (`WITH NOCHECK`) | `PerTableWriter.cs:110–114` | missing | IR has no `IsNoCheck`; `IgnoreNoCheckClause` tolerance | 4+ | divergence-documented |
| 3.28 | FK naming `FK_<Owner>_<Ref>_<col>` (length-cap with hash suffix) | `ForeignKeyNameFactory.cs:17–60` | pre-scoped (4.1.A slice 5) | Extracted helper | 4.1.A slice 5 | parity |
| 3.29 | FK naming normalization | `ForeignKeyNameFactory.cs` references | pre-scoped (4.1.A slice 5) | Inherited rules | 4.1.A slice 5 | parity |
| 3.30 | FK ON DELETE action mapping | `RawTextEmitter.fs:43–48` | pre-scoped (4.1.A §3) | Action mapping inherited | 4.1.A slice 1 | parity |
| 3.31 | Cross-module FK post-deploy routing (optional) | `PerTableWriter.cs:110–114` | pre-scoped (4.1.A slice 10) | Tolerance flag `PostDeployForeignKeys = false` initially | 4.1.A slice 10 | defer-with-trigger |

### 3.6 Extended properties (32–37)

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 3.32 | Table description (`MS_Description` via `sp_addextendedproperty`) | `ExtendedPropertyScriptBuilder.cs:82–95` | pre-scoped (4.1.A slice 8) | `Kind.Description` IR needed | 4.1.A slice 8 | parity-deferred |
| 3.33 | Column description (level2 = COLUMN) | `ExtendedPropertyScriptBuilder.cs:98–114` | pre-scoped (4.1.A slice 8) | `Attribute.Description` IR needed | 4.1.A slice 8 | parity-deferred |
| 3.34 | Index description (level2 = INDEX or CONSTRAINT) | `ExtendedPropertyScriptBuilder.cs:117–135` | pre-scoped (4.1.A slice 8) | `Index.Description` IR needed | 4.1.A slice 8 | parity-deferred |
| 3.35 | Extended property escape rule (single-quote → double-single-quote) | `ExtendedPropertyScriptBuilder.cs:138–140` | pre-scoped (4.1.A slice 8) | Shared escape helper | 4.1.A slice 8 | parity |
| 3.36 | Extended property ordering (tables, columns in-order, indexes in-order) | `ExtendedPropertyScriptBuilder.cs:47–79` | pre-scoped (4.1.A §6) | Emitter groups and sorts | 4.1.A slice 8 | parity |
| 3.37 | Tolerance `IgnoreExtendedProperties = true` (pre-slice-8) | `SsdtManifest.cs` | documented | Deferred until SnapshotRowsets surfaces descriptions | 4.1.A slice 8 | parity-gated |

### 3.7 Triggers (38–40)

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 3.38 | Trigger emission (CREATE TRIGGER) | `SmoTriggerBuilder.cs:1–50`, `PerTableWriter.cs:290–325` | missing | V2 out-of-scope; `IgnoreTriggers = true` tolerance | — | won't-carry-forward |
| 3.39 | Disabled trigger (`ALTER TABLE [...] DISABLE TRIGGER`) | `PerTableWriter.cs:313–319` | missing | Out-of-scope | — | won't-carry-forward |
| 3.40 | Trigger name rewriting (rename annotation) | `SmoRenameLens.cs`, `PerTableWriter.cs:327–354` | missing | Out-of-scope | — | won't-carry-forward |

### 3.8 Per-table headers (41–44)

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 3.41 | Optional header comment block (`/* Source: ... Profile: ... Decisions: ... Fingerprint: ... */`) | `PerTableWriter.cs:187–267` | pre-scoped (4.1.A slice 1) | EmissionPolicyPass fills fields | 4.1.A slice 1 | parity-deferred |
| 3.42 | Header fields (Source, Profile, Decisions, Fingerprint) | `PerTableWriter.cs:206–227` | pre-scoped (4.1.A slice 1) | V2 populates from post-pass catalog | 4.1.A slice 1 | parity-deferred |
| 3.43 | Header `AdditionalItems` | `PerTableWriter.cs:218–227` | pre-scoped (4.1.A slice 1) | V2 composes from EmissionContext | 4.1.A slice 1 | parity-deferred |
| 3.44 | Header ordering (alphabetical by label) | `PerTableWriter.cs:249–253` | pre-scoped (4.1.A slice 1) | Emitter sorts | 4.1.A slice 1 | parity |

### 3.9 Statement ordering & formatting (45–48)

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 3.45 | Per-file statement order (CREATE → NOCHECK FKs → indexes → DISABLE → ext props → triggers → newline) | `PerTableWriter.cs:87–185` | pre-scoped (4.1.A §4) | V2 same order; triggers omitted | 4.1.A slice 1 | parity |
| 3.46 | Statement batch joiner (GO separator) | `StatementBatchFormatter.cs` | pre-scoped (4.1.A slice 1) | Joins with GO + newline | 4.1.A slice 1 | parity |
| 3.47 | Trailing newline per file | `TablePlanWriter.cs:83–87` | pre-scoped (4.1.A slice 1) | C# host appends; LF normalized | 4.1.A slice 1 | parity-divergence |
| 3.48 | **Newline normalization** (V1 OS-dependent CRLF/LF; V2 LF unconditional) | `PerTableWriter.cs:176`, `TablePlanWriter.cs:83` | pre-scoped (4.1.A §6) | Tolerance `NewlineNormalization = true` | 4.1.A slice 1 | divergence-named |

### 3.10 Manifest schema (49–58)

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 3.49 | Manifest top-level (`SsdtManifest`: Tables, Options, Emission, PreRemediation, Coverage, PredicateCoverage, Unsupported) | `SsdtManifest.cs:6–14` | pre-scoped (4.1.A §9 slice 9) | ManifestEmitter | 4.1.A slice 9 | parity |
| 3.50 | `TableManifestEntry` (Module, Schema, Table, TableFile, Indexes, ForeignKeys, IncludesExtendedProperties) | `SsdtManifest.cs:16–23` | pre-scoped (4.1.A slice 9) | Per-table entry | 4.1.A slice 9 | parity |
| 3.51 | Manifest `Options` (IncludePlatformAutoIndexes, EmitBareTableOnly, SanitizeModuleNames, ModuleParallelism) | `SsdtManifest.cs:25–29` | pre-scoped (4.1.A slice 9) | Pass through from emission context | 4.1.A slice 9 | parity |
| 3.52 | Manifest `Emission` (Algorithm, Hash) | `SsdtManifest.cs:31` | pre-scoped (4.1.A §6) | SsKey-rooted hash; differs from V1 | 4.1.A slice 9 | divergence-named |
| 3.53 | Manifest `PreRemediation` (Module, Table, TableFile, Hash) | `SsdtManifest.cs:33–37` | pre-scoped (4.1.A slice 9) | Empty in 4.1.A; chapter 4.4 fills | 4.1.A slice 9 | parity-deferred |
| 3.54 | Manifest `PolicySummary` (decision counts and rationales) | `SsdtManifest.cs:39–52` | missing (empty in 4.1.A) | Lives in chapter 4.4 | 4.1.A slice 9 | defer-with-trigger |
| 3.55 | Manifest `Coverage` (Tables, Columns, Constraints with Emitted/Total/Percentage) | `SsdtManifest.cs:54–66` | pre-scoped (4.1.A slice 9) | Computed from catalog cardinality | 4.1.A slice 9 | parity |
| 3.56 | Manifest `PredicateCoverage` (fine-grained predicate satisfaction) | `SsdtManifest.cs` | pre-scoped (4.1.A slice 9) | Empty in 4.1.A | 4.1.A slice 9 | defer-with-trigger |
| 3.57 | Manifest `Unsupported` (un-emittable elements) | `SsdtManifest.cs:14` | pre-scoped (4.1.A slice 9) | Collects divergences | 4.1.A slice 9 | parity |
| 3.58 | Manifest JSON determinism (UTF-8, sorted keys, no timestamp) | `Utf8JsonWriter` pattern | pre-scoped (4.1.A §6) | `Utf8JsonWriter` | 4.1.A slice 9 | parity |

### 3.11 Refactor.log integration (59–61)

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 3.59 | Refactor.log XML at `<outDir>/<projectName>.refactorlog` | (V1 has no .refactorlog) | pre-scoped (4.1.A slice 10, 3.5 owns content) | Composition layer emits | 3.5 + 4.1.A slice 10 | V2-growth |
| 3.60 | Refactor.log XML schema for `<Renamed>` entries | SSDT spec | pre-scoped (3.5) | RefactorLogEmitter; T11 validates | 3.5 | parity (V2 sole emitter) |
| 3.61 | Refactor.log emitted only if diff carries renames | implicit V1 behavior | pre-scoped (4.1.A §5) | Composition emits when CatalogDiff non-empty | 4.1.A slice 10 | parity |

### 3.12 Directory tree & file composition (62–67)

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 3.62 | `<outDir>/Modules/<Module>/<Schema>.<Table>.sql` hierarchy | `TableEmissionPlanner.cs:230–250` | pre-scoped (4.1.A §3) | Emitter relative paths | 4.1.A slice 1 | parity |
| 3.63 | `<outDir>/manifest.json` at project root | `SsdtEmitter.cs:94–122` | pre-scoped (4.1.A slice 9) | Composition writes | 4.1.A slice 9 | parity |
| 3.64 | `<outDir>/Scripts/PostDeploy/` for post-deploy scripts | V1 convention | missing (empty in 4.1.A) | Cross-module FKs / seed data are 4.1.B+ | 4.1.B+ | deferred-to-4.1B |
| 3.65 | Post-deploy script file naming convention | V1 convention | missing | Deferred to 4.1.B | 4.1.B | deferred |
| 3.66 | Directory creation + parallelism (`ModuleParallelism`) | `SsdtEmitter.cs:81–88` | pre-scoped (4.1.A slice 1) | C# host handles | 4.1.A slice 1 | parity |
| 3.67 | UTF-8 BOM-less encoding (`UTF8Encoding(false)`) | `SsdtEmitter.cs:18` | pre-scoped (4.1.A §6) | C# host LF | 4.1.A slice 1 | parity |

### 3.13 Naming, sanitization, diagnostics, type mapping, EmissionPolicy (68–76)

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 3.68 | Module name sanitization pipeline | `ModuleNameSanitizer.cs` | pre-scoped (4.1.A slice 1) | Host-side | 4.1.A slice 1 | parity |
| 3.69 | Schema name handling (implicit per-table; no CREATE SCHEMA in 4.1.A) | `CreateTableStatementBuilder.cs:101–105` | pre-scoped (4.1.A §3) | Future schema consolidation | 4.1.A slice 1 | parity |
| 3.70 | Identifier quoting (`[<identifier>]`) | throughout | pre-scoped (4.1.A slice 1) | Same | 4.1.A slice 1 | parity |
| 3.71 | Emitted element tracking (`PerTableWriteResult` with IndexNames, ForeignKeyNames) | `PerTableWriteResult` record | pre-scoped (4.1.A slice 1) | Result with metadata for manifest builder | 4.1.A slice 1 | parity |
| 3.72 | Extended property tracking (`IncludesExtendedProperties` flag) | `PerTableWriteResult.IncludesExtendedProperties` | pre-scoped (4.1.A slice 8) | Emitter sets flag | 4.1.A slice 8 | parity |
| 3.73 | Type correspondence policy (synthetic defaults) | `RawTextEmitter.fs:31–41` | pre-scoped (4.1.A §3) | Shared `SqlTypeMap.fs` with DACPAC | 4.1.A slice 2 | parity |
| 3.74 | Bare-table-only mode (`SmoBuildOptions.EmitBareTableOnly`) | `SmoBuildOptions` | pre-scoped (4.1.A slice 1) | Emitter checks flag | 4.1.A slice 1 | parity |
| 3.75 | EmissionPolicy interpretation (AllRemaining/AllExceptStatic/AllData) | `Policy.fs` (4.1.B sibling) | pre-scoped (4.1.A §7) | EmissionPolicyPass | 4.1.A slice 1 | parity-deferred |
| 3.76 | Profile independence (emission unaffected by missing Profile fields) | A18 amended seam | axiom A34 | Emitter consumes only Catalog | 4.1.A slice 1 | parity |

---

## Section 4 — Data emission + UAT + Migration

V1's Osm.Emission/Seeds/, PhasedDynamicEntityInsertGenerator, and UAT-Users pipeline. V2's chapter 4.1.B + 4.2 pre-scopes cover most. Detailed survey of 60 items.

### 4.1 Static seeds & dynamic inserts (V1's Seeds/ + PhasedDynamicEntityInsertGenerator)

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 4.1 | Static seed MERGE basic shape | `StaticSeedSqlBuilder.cs:211–273` | pre-scoped (4.1.B slice 3) | V1 unconditional UPDATE; V2 adds CDC-aware predicate (slice 6) | 4.1.B slice 3 | parity + widening |
| 4.2 | Identity-insert handling (`SET IDENTITY_INSERT ... ON/OFF`) | `StaticSeedSqlBuilder.cs:140–145, 202–206` | pre-scoped (4.1.B slice 3) | V2 inherits | 4.1.B slice 3 | parity |
| 4.3 | DELETE branch (Authoritative mode: `WHEN NOT MATCHED BY SOURCE THEN DELETE`) | `StaticSeedSqlBuilder.cs:261–268` | pre-scoped (4.1.B slice 3) | CDC-safety automatic on idempotent redeploy | 4.1.B slice 3 | parity |
| 4.4 | Synchronization modes (NonDestructive / Authoritative / ValidateThenApply) | `TighteningOptions.cs:360–365` | pre-scoped (4.1.B slice 1) | V2 dispatcher routes via DataComposition | 4.1.B slice 1 | widening |
| 4.5 | Drift validation (ValidateThenApply: EXCEPT-based pre-check) | `StaticSeedSqlBuilder.cs:102–138` | pre-scoped (4.1.B slice 3) | V2 inherits shape | 4.1.B slice 3 | parity |
| 4.6 | Batching (default 1000 rows + PRINT progress markers) | `StaticSeedSqlBuilder.cs:14, 95–100, 167–187` | pre-scoped (4.1.B slice 3) | V2 inherits configurable batches | 4.1.B slice 3 | parity |
| 4.7 | Column escaping / identifier formatting | `StaticSeedSqlBuilder.cs:304–305, 66–72` | pre-scoped (4.1.B slice 3) | V2 inherits (renamed module) | 4.1.B slice 3 | parity |
| 4.8 | SQL literal escaping (strings, dates, binary; `N'...'` prefix, CHAR(10/13/9), `0x...`, `CAST(... AS datetime2(7))`) | `SqlLiteralFormatter.cs:1–94` | shipped | V2 carries identical rules | — | parity |
| 4.9 | NULL handling in escape rules + three-valued logic guards | `SqlLiteralFormatter.cs:9–41` | shipped | V2 inherits | — | parity |
| 4.10 | Dynamic INSERT (single-phase, `INSERT INTO ... WITH (TABLOCK)`) | `DynamicEntityInsertGenerator.cs:1–788` | shipped (or deferred to 4.1.B slice 8) | When no cycles | 4.1.B slice 8 | parity |
| 4.11 | Phased insert (two-phase) | `PhasedDynamicEntityInsertGenerator.cs:1–498` | pre-scoped (4.1.B slice 5) | Phase 1 (NULL deferred FKs) + phase 2 (UPDATE) | 4.1.B slice 5 | parity |
| 4.12 | Deferred FK column identification (cycle-aware) | `PhasedDynamicEntityInsertGenerator.cs:150–192` | pre-scoped (4.1.B slice 5) | V2 carries `DeferredFkSet` on `DataInsertRow` | 4.1.B slice 5 | parity |
| 4.13 | Self-referencing FK ordering (within-table topological sort on PK values) | `DynamicEntityInsertGenerator.cs:308–561` | shipped | V2 inherits | — | parity |
| 4.14 | SCC cycle detection | `PhasedDynamicEntityInsertGenerator.cs:325–354`, `EntityDependencySorter` | pre-scoped (4.1.B slice 4) | `TopologicalOrderPass.Cycles` | 4.1.B slice 4 | parity |
| 4.15 | Entity dependency sorting (FK-aware) | `EntityDependencySorter.cs` | shipped | `TopologicalOrderPass.fs` | — | parity |
| 4.16 | PK discovery for MERGE ON clause | `PhasedDynamicEntityInsertGenerator.cs:356–366` | pre-scoped (4.1.B slice 3) | `IsPrimaryKey` flag (session 25) | 4.1.B slice 3 | parity |
| 4.17 | CTE for source projection (SourceRows CTE) | `PhasedDynamicEntityInsertGenerator.cs:285–291, 406–445` | pre-scoped (4.1.B slice 5) | `DataInsertRow` + renderer | 4.1.B slice 5 | parity |
| 4.18 | NULL-masking CTE for phase 1 (`PhaseOneSource` with `CASE WHEN 1=0`) | `PhasedDynamicEntityInsertGenerator.cs:413–442` | pre-scoped (4.1.B slice 5) | V2 carries idiom | 4.1.B slice 5 | parity |
| 4.19 | **CDC-aware change-detection predicate** | NEW in V2 | missing in V1 | `(Target.[Col] <> Source.[Col] OR null-state-different OR ...)` per-column composition | 4.1.B slice 6 | V2-growth |
| 4.20 | **Per-kind CDC dispatch** (`Profile.CdcAwareness`-driven) | NEW in V2 | missing in V1 | One-per-environment flexibility | 4.1.B slice 6 | V2-growth |
| 4.21 | **CDC discovery adapter** | NEW in V2 | missing in V1 | Reads `cdc.change_tables` via SQL | 4.1.B slice 2 | NEW |
| 4.22 | StaticSeedSqlBuilder MERGE template | `StaticSeedSqlBuilder.cs:221–273` | shipped (template) | V2 generates identical T-SQL shape | — | parity |
| 4.23 | VALUES clause formatting | `StaticSeedSqlBuilder.cs:307–337` | shipped | V2 renders from structured rows | — | parity |
| 4.24 | UpdateNullableFks (phase 2 UPDATE with JOIN) | `PhasedDynamicEntityInsertGenerator.cs:255–303` | pre-scoped (4.1.B slice 5) | Phase 2 render | 4.1.B slice 5 | parity |
| 4.25 | Header comment / metadata (Module / Entity / Schema) | `StaticSeedSqlBuilder.cs:56–64` | shipped | V2 carries forward | — | parity |
| 4.26 | GO batch separator | `StaticSeedSqlBuilder.cs:84, 136, 206, 241, 243, 271, 272` | shipped | Renderer | — | parity |
| 4.27 | Dynamic dataset container (`DynamicEntityDataset`) | `DynamicEntityInsertGenerator.cs:18–39` | shipped | `DataInsertScript.fs` | — | parity |
| 4.28 | DynamicEntityInsertArtifact (per-table) | `DynamicEntityInsertGenerator.cs:58–198` | shipped | V2 artifact writer pattern | — | parity |
| 4.29 | Insert generator options (batch size) | `DynamicEntityInsertGenerator.cs:41–56` | shipped | Render-time parameter | — | parity |

### 4.2 UAT-Users pipeline (V1's Osm.Pipeline.UatUsers/)

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 4.30 | UAT-Users pipeline structure (7-step orchestration) | `UatUsersPipeline.cs:1–76` | pre-scoped (4.2 slices 1–7) | `UserFkReflowPass.fs` + emitter integration | 4.2 | parity + abstraction |
| 4.31 | UserMatchingEngine (core logic: email/attribute/regex/fallback) | `UserMatchingEngine.cs:19–79` | pre-scoped (4.2 slices 4–5) | DU with pure algorithm | 4.2 | parity (reframed as DU) |
| 4.32 | UserMatchingOptions (strategy configuration enum) | `UserMatchingOptions.cs:1–19` | pre-scoped (4.2 slice 1) | `UserMatchingStrategy` policy axis | 4.2 slice 1 | parity (reframed) |
| 4.33 | UserMapLoader (CSV parsing) | `UserMapLoader.cs:1–80` | pre-scoped (4.2 slice 5) | Boundary adapter | 4.2 slice 5 | parity |
| 4.34 | DiscoverUserFkCatalogStep | `Steps/DiscoverUserFkCatalogStep.cs` | pre-scoped (4.2 slices 6–7) | `Reference.IsUserFk` flag in IR | 4.2 slice 6 | parity (moved to IR) |
| 4.35 | LoadQaUserInventoryStep | `Steps/LoadQaUserInventoryStep.cs` | pre-scoped (4.2 slice 2) | `Profile.SourceUsers` | 4.2 slice 2 | parity (moved to Profile) |
| 4.36 | LoadUatUserInventoryStep | `Steps/LoadUatUserInventoryStep.cs` | pre-scoped (4.2 slice 2) | `Profile.TargetUsers` | 4.2 slice 2 | parity (moved to Profile) |
| 4.37 | AnalyzeForeignKeyValuesStep | `Steps/AnalyzeForeignKeyValuesStep.cs` | pre-scoped (4.2 slice 4) | `UserFkReflowPass.discover` | 4.2 slice 4 | parity (made pure) |
| 4.38 | ApplyMatchingStrategyStep | `Steps/ApplyMatchingStrategyStep.cs` | pre-scoped (4.2 slices 4–5) | DU pattern match | 4.2 slices 4–5 | parity |
| 4.39 | ValidateUserMapStep | `Steps/ValidateUserMapStep.cs` | pre-scoped (4.2 slice 6) | `UserRemapContext.isFullyMapped` | 4.2 slice 6 | parity |
| 4.40 | EmitArtifactsStep (artifact emission with remapped values) | `Steps/EmitArtifactsStep.cs` | pre-scoped (4.2 slice 7) | Distributed to MigrationDeps + Bootstrap emitters | 4.2 slice 7 | parity |
| 4.41 | UserMatchingResult shape | `UserMatchingResult.cs` | pre-scoped (4.2 slices 3, 6) | `UserRemapContext` | 4.2 slices 3,6 | parity (restructured) |
| 4.42 | Fallback strategy (Ignore / SingleTarget / RoundRobin) | `UserMatchingEngine.cs:33–67` | pre-scoped (4.2 slice 5) | V2 collapses to `FallbackToSystemUser` | 4.2 slice 5 | divergence (focused) |
| 4.43 | **Lineage / diagnostics on user-match decisions** | NEW in V2 | absent in V1 | `Lineage<Diagnostics<UserRemapContext>>` | 4.2 slice 4 | V2-growth |

### 4.3 Static-entity & cross-cutting infrastructure

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 4.44 | Static entity seed definition builder | `StaticEntitySeedScriptGenerator.cs:96–198` | pre-scoped (4.1.B slice 3) | Walks model entities, filters, builds columns | 4.1.B slice 3 | parity |
| 4.45 | Static entity row normalization (`EntitySeedDeterminizer`) | `Seeds/EntitySeedDeterminizer.cs` | pre-scoped (4.1.B slice 3) | V2 carries to data emit phase | 4.1.B slice 3 | parity |
| 4.46 | PK enforcement in MERGE predicate | `StaticSeedSqlBuilder.cs:147–162, 229–233` | pre-scoped (4.1.B slice 3) | PK-only or fallback | 4.1.B slice 3 | parity |
| 4.47 | Column list projection (Target.Col / Source.Col) | `StaticSeedSqlBuilder.cs:71–72` | pre-scoped (4.1.B slice 3) | V2 carries | 4.1.B slice 3 | parity |
| 4.48 | ModuleValidationOverrides (allowMissingPrimaryKey per module) | `StaticSeedSqlBuilder.cs:40, 152–158` | deferred | V2 defers until use case | post-4 | defer-with-trigger |
| 4.49 | StaticSeedForeignKeyPreflight | `Seeds/StaticSeedForeignKeyPreflight.cs` | deferred | Validation pass; deferred | post-4 | defer-with-trigger |
| 4.50 | Row deduplication (by PK) | `DynamicEntityInsertGenerator.cs:282–306` | shipped | Hash-set-based duplicate removal | — | parity |
| 4.51 | **Migration data intake (schema / format)** | (V1 has no equivalent) | missing | NDJSON pickup directory; new V2 capability | 4.1.B slice 7 | NEW |
| 4.52 | Profiling data (row counts / null counts) | V1 Profiling/ | shipped | `Profile.Distributions` | — | parity |
| 4.53 | AdvancedSql/ area documentation | `/AdvancedSql/` directory | deferred | Inventory needed | post-3 | defer-with-trigger |
| 4.54 | Data emission configuration flags (`EmitData`, `IncludeStatic`, etc.) | `CliConfiguration.cs` + `SqlSectionReader.cs` | pre-scoped (4.1.B slice 1) | `Policy.Emission.DataComposition` | 4.1.B slice 1 | parity |
| 4.55 | Constraint-disable toggle for cyclic data (`ALTER TABLE NOCHECK CONSTRAINT ALL`) | `DynamicEntityInsertGenerator.cs:100–105, 249–251` | pre-scoped (4.1.B slice 5) | Phase-1 wrapping | 4.1.B slice 5 | parity |
| 4.56 | Row-order preservation for self-refs | `DynamicEntityInsertGenerator.cs:269–270, 308–561` | shipped | Topological sort within table | — | parity |
| 4.57 | Data emission split (per-table vs master file via `GroupByModule` / `EmitMasterFile`) | `StaticSeedOptions` flags | pre-scoped (4.1.B slice 1) | `EmissionPolicy` | 4.1.B slice 1 | parity (master-file deferred) |
| 4.58 | **Topological-order preservation across emitters** | NEW in V2 | missing in V1 | `DataEmissionComposer` interleaves under one topo order | 4.1.B slice 4 | V2-growth |
| 4.59 | **Partition overlap detection (emitter coverage)** | NEW in V2 | missing in V1 | `EmitError.OverlappingEmitterCoverage` | 4.1.B slice 1 | NEW |
| 4.60 | **DataInsertScript structured artifact** | NEW in V2 | missing in V1 | `Phase1Merges / Phase2Updates / Rendered` | 4.1.B slice 1 | V2-growth |

---

## Section 5 — Pipeline orchestration + CLI

V1 has substantial CLI (`src/Osm.Cli/`) and orchestration (`src/Osm.Pipeline/Orchestration/`); V2 has neither shipped. Detailed survey of 50 items.

### 5.1 V1 verb-level inventory

| # | V1 verb | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 5.1 | `extract-model` | `ExtractModelVerb.cs` | pre-scoped (3.2+) | `JsonEmitter` sibling exists; CLI verb ships as command factory | 3.2+ | parity |
| 5.2 | `build-ssdt` | `BuildSsdtVerb.cs` | pre-scoped (3.1, 3.3) | RawTextEmitter shipped; DacpacEmitter chapter 3.3 | 3.3 | parity |
| 5.3 | `profile` | `ProfileVerb.cs` | pre-scoped (3.2+) | V1 captures `ProfileSnapshot` | 3.2+ | widening (V2 richer) |
| 5.4 | `full-export` (chains extract → profile → build) | `FullExportVerb.cs` | pre-scoped (3.1–3.3+) | V2 composes over shipped/deferred primitives | 3.3 | parity |
| 5.5 | `analyze` | `AnalyzeVerb.cs` | pre-scoped (4.1+) | V2 strategy-layer + passes replace V1 analyzer | 4.1 | parity |
| 5.6 | `dmm-compare` | `DmmCompareVerb.cs` | pre-scoped (3.5+) | V2 comparator may reuse for cross-source parity | 3.5 | defer-with-trigger |
| 5.7 | `verify-data` | `VerifyDataCommandFactory.cs` | pre-scoped (3.1) | Read-side adapter provides oracle | 3.1 | parity |
| 5.8 | `inspect` | `InspectCommandFactory.cs` | missing | New V2 CLI artifact | 4.3 | parity |
| 5.9 | `policy` (internal diagnostic) | `PolicyCommandFactory.cs` | missing | V1-specific tooling | — | won't-carry-forward |
| 5.10 | `uat-users` (conditional on `OSM_ENABLE_REMAP_USERS`) | `UatUsersCommandFactory.cs:48–52` | missing | V2 scope TBD | 4.2+ | defer-with-trigger |

### 5.2 CLI options & configuration

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 5.11 | `--config` (global) | `CliGlobalOptions.cs:9` | pre-scoped (3.1) | V2 canary loads pipeline config | 3.1 | parity |
| 5.12 | `--model` | `VerbOptionRegistry.cs:24` | pre-scoped (3.1, 4.1) | `CatalogReader.parse` | 3.1 | parity |
| 5.13 | `--profile` | `VerbOptionRegistry.cs:25` | pre-scoped (3.2+) | Boundary adapter consumes | 3.2+ | parity |
| 5.14 | `--out` (per-verb flavors) | `VerbOptionRegistry.cs:44–52` | pre-scoped (3.1+) | V2 verbs write to operator-configured paths | 3.1 | parity |
| 5.15 | `--snapshot` (V1-legacy alias) | factories | pre-scoped | V2 must expose alias | 3.1 | parity |
| 5.16 | `--only-active-attributes` / `--include-inactive-attributes` | `VerbOptionRegistry.cs:54–55` | missing | Routing through Policy axis | 3.2 | parity |
| 5.17 | `--static-data` | `VerbOptionRegistry.cs:27` | pre-scoped (4.2+) | Optional fixture input | 4.2 | parity |
| 5.18 | `--mock-advanced-sql` (test-harness) | `VerbOptionRegistry.cs:56` | pre-scoped (test-harness) | Test-only | 3.1 | won't-carry-forward-to-CLI |
| 5.19 | `--rename-table` (`source=override`) | `VerbOptionRegistry.cs:28` | pre-scoped (4.2) | Physical-table override | 4.2 | parity |
| 5.20 | `--circular-deps-config` | `VerbOptionRegistry.cs:29` | pre-scoped (4.2) | Advanced tuning | 4.2 | parity |
| 5.21 | `--max-degree-of-parallelism` (global) | `CliGlobalOptions.cs:10` | missing | F# core synchronous; tunable knob unnecessary | 3.1 | won't-carry-forward |
| 5.22 | `--dynamic-insert-mode` | `VerbOptionRegistry.cs:30–33` | pre-scoped (3.3) | SSDT emitter owns choice | 3.3 | parity |
| 5.23 | `--defer-junction-tables` | `VerbOptionRegistry.cs:34–39` | pre-scoped (4.2) | Topological-order pass config | 4.2 | parity |
| 5.24 | `--static-seed-parent-mode` | `VerbOptionRegistry.cs:40–42` | pre-scoped (4.1+) | Policy axis | 4.1 | parity |
| 5.25 | `--profiler-provider` (`fixture` or `sql`) | `VerbOptionRegistry.cs:26` | pre-scoped (3.2) | Profile source selection | 3.2 | parity |
| 5.26 | `--dmm` (path to baseline DMM script) | `VerbOptionRegistry.cs:59` | pre-scoped (3.5) | DMM comparison verb | 3.5 | defer-with-trigger |
| 5.27 | `--manifest` (verify-data) | `VerifyDataCommandFactory.cs:30` | pre-scoped (3.1) | Standard artifact naming | 3.1 | parity |
| 5.28 | `--source-connection` / `--target-connection` (verify-data) | `VerifyDataCommandFactory.cs:34–38` | pre-scoped (3.1) | Read-side adapter parameters | 3.1 | parity |
| 5.29 | `--report-out` (verify-data) | `VerifyDataCommandFactory.cs:33` | pre-scoped (3.1) | Standard artifact naming | 3.1 | parity |
| 5.30 | `--open-report` (Spectre.Console extension) | `OpenReportVerbExtension.cs` | pre-scoped (3.4) | F# core has no I/O; C# canary may offer | 3.4 | divergence-named |

### 5.3 Pipeline orchestration & step ordering

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 5.31 | BuildSsdtPipeline (11-step ordered sequence) | `BuildSsdtPipeline.cs:57–95` | pre-scoped (3.1–3.3+) | Functional composition replaces imperative chain | 3.3 | parity |
| 5.32 | ProfileCaptureResult (profile pipeline) | `CaptureProfilePipeline.cs` | pre-scoped (3.2) | Step ordering must match | 3.2 | parity |
| 5.33 | FullExportPipeline (chain orchestrator) | `FullExportApplicationService.cs` (implied) | pre-scoped (3.1–3.3+) | V2 CLI composes sub-pipelines | 3.3 | parity |
| 5.34 | Step-result envelope (success/failure, exit-code semantics) | `BuildSsdtPipelineResult.cs:23–40` | pre-scoped (3.1) | `Lineage<'output>` + errors → exit codes | 3.1 | parity |
| 5.35 | Progress reporting / Spectre.Console | `SpectreConsoleProgressService.cs` | pre-scoped (3.4) | F# core no progress; C# canary may wire | 3.4 | divergence-named |
| 5.36 | Cancellation token threading | `BuildSsdtPipeline.HandleAsync(..., CT)` | pre-scoped (3.1) | V2 sync core; cancellation at boundary | 3.1 | divergence-named |
| 5.37 | Emission-step caching / evidence cache | `BuildSsdtEvidenceCacheStep.cs` | pre-scoped (3.3+) | Catalog immutable; caching strategy TBD | 3.3 | defer-with-trigger |
| 5.38 | Bootstrap snapshot step (final-state capture) | `BuildSsdtBootstrapSnapshotStep.cs` | pre-scoped (3.3+) | V2 equivalent TBD | 3.3 | parity |
| 5.39 | Telemetry packaging step | `BuildSsdtTelemetryPackagingStep.cs` | pre-scoped (3.4+) | V2 observability TBD | 3.4 | parity |
| 5.40 | Pipeline execution log (structured timeline) | `PipelineExecutionLog.cs`, `PipelineLogMetadataBuilder.cs` | pre-scoped (3.1) | V2 Lineage carries decisions; CLI exposure TBD | 3.1 | parity |

### 5.4 Output formatting & artifacts

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 5.41 | Decision-log JSON (`policy-decisions.json`) | `PolicyDecisionLogWriter.cs:36–88` | pre-scoped (4.3) | V2 single `decision-log.json` | 4.3 | parity |
| 5.42 | Opportunities JSON | `OpportunityLogWriter.cs:76–82` | pre-scoped (4.3) | OpportunitiesEmitter | 4.3 | parity |
| 5.43 | Validations JSON | `OpportunityLogWriter.cs` via `ValidationFinding.FromOpportunity` | pre-scoped (4.3) | ValidationsEmitter | 4.3 | parity |
| 5.44 | SSDT artifact emission (sql/ tree, .sqlproj) | `BuildSsdtEmissionStep.cs`, `BuildSsdtSqlProjectStep.cs` | shipped (RawText) + pre-scoped (Dacpac) | DacpacEmitter swaps in 3.3 | 3.3 | parity |
| 5.45 | Manifest.json (artifact inventory) | `BuildSsdtPipelineResult.cs:104–130` | pre-scoped (3.1) | C# CliManifestWriter | 3.1 | parity |
| 5.46 | Profile artifacts (snapshot + statistics JSON) | `CaptureProfilePipeline.cs` | pre-scoped (3.2) | V2 richer Profile + Distributions | 3.2 | widening |
| 5.47 | Tightening analysis outputs | `AnalyzeVerb.cs` | pre-scoped (4.1+) | Three-artifact routing | 4.1 | parity |
| 5.48 | Data-integrity verification report (JSON) | `DataIntegrityVerificationReport.cs` | pre-scoped (3.1) | `CatalogEquivalence` Diff | 3.1 | parity |
| 5.49 | SQL validation report | `BuildSsdtSqlValidationStep.cs` | pre-scoped (3.3) | DacFx validation | 3.3 | parity |
| 5.50 | Text/table/markdown output formatters (Spectre tables, progress bars) | `CommandConsole.cs` | pre-scoped (3.4) | C# host owns rendering | 3.4 | divergence-named |

---

## Section 6 — Auxiliary tooling (DMM + AdvancedSql + test infra)

V1's `Osm.Dmm`, `/AdvancedSql/`, `tests/Osm.TestSupport/`, `Osm.LoadHarness`. Detailed survey of 30 items.

### 6.1 DMM (Data Model Mapping)

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 6.1 | DMM Comparator logic (compares Catalog against SSDT + SMO via lens) | `src/Osm.Dmm/DmmComparator.cs` (26KB) | missing | Validates T11 cross-source; V2 must implement post-pipeline | 5+ | defer-with-trigger |
| 6.2 | DMM multi-lens architecture (`IDmmLens`: ScriptDom / SMO / SsdtProject) | `IDmmLens.cs` + 3 implementations | extracting | V2 inherits pattern; lenses land when DacpacEmitter+ReadSide ship | 5 | divergence-named |
| 6.3 | DMM comparison results model (`DmmTable`, `DmmColumn`, `DmmIndex`, `DmmForeignKey`, `DmmComparisonResult`, `DmmDifference`) | `DmmModels.cs` | pre-scoped (3-4) | Diagnostics writer can absorb findings | 4 | defer-with-trigger |

### 6.2 AdvancedSql/ tree (T-SQL extraction templates)

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 6.4 | `outsystems_metadata_rowsets.sql` (44KB; T-SQL metadata extraction; parameterized) | `/AdvancedSql/` | shipped (adapted as F#) | V2's CatalogReader rewrites; lossy on `SnapshotSource` variants | 3.2 | parity |
| 6.5 | `outsystems_model_export.sql` (39KB; procedural two-phase JSON build) | `/AdvancedSql/` | shipped (adapted) | JSON schema identical; parameterization preserved | 2 (closed) | parity |

### 6.3 Test support

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 6.6 | `SqlServerFixture` (177 lines; testcontainers boot + seed + procedure exec) | `tests/Osm.TestSupport/SqlServerFixture.cs` | pre-scoped (3) | V2 testcontainers F# wrapper | 3.1 | parity |
| 6.7 | `DockerAvailability` (63 lines; skip logic) | `tests/Osm.TestSupport/DockerAvailability.cs` | in-flight (3) | xUnit-based skip attribute | 3.1 | parity |
| 6.8 | `EmissionOutput` (210 lines; SSDT artifact bundle capture) | `tests/Osm.TestSupport/EmissionOutput.cs` | pre-scoped (3-4) | Golden-file harness | 4 | parity |
| 6.9 | `ProfileFixtures` (270 lines; mock data builders) | `tests/Osm.TestSupport/ProfileFixtures.cs` | in-flight (2) | Profile mock builders | 2 (closed) | parity |
| 6.10 | `ModelFixtures` (23 lines; lightweight builders) | `tests/Osm.TestSupport/ModelFixtures.cs` | in-flight (2) | `Fixtures.fs` | 2 (closed) | parity |
| 6.11 | `DirectorySnapshot` (220 lines; recursive file tree capture) | `tests/Osm.TestSupport/DirectorySnapshot.cs` | pre-scoped (3-4) | Golden-file diffing | 4 | parity |
| 6.12 | `SqlServerFactAttribute` (34 lines; xUnit skip marker) | `tests/Osm.TestSupport/SqlServerFactAttribute.cs` | in-flight (3) | F# xUnit attributes | 3.1 | parity |

### 6.4 Load harness

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 6.13 | `LoadHarnessRunner` (270 lines; SQL replay + perf metrics: wait stats, locks, fragmentation) | `src/Osm.LoadHarness/LoadHarnessRunner.cs` | deferred | V2 perf-testing module post-pipeline | 5+ | defer-with-trigger |
| 6.14 | `LoadHarnessOptions` (52 lines) | `src/Osm.LoadHarness/LoadHarnessOptions.cs` | deferred | Perf testing config | 5+ | defer-with-trigger |
| 6.15 | `LoadHarnessReport` (38 lines; structured telemetry) | `src/Osm.LoadHarness/LoadHarnessReport.cs` | deferred | Perf report model | 5+ | defer-with-trigger |
| 6.16 | LoadHarness CLI wrapper | `tools/FullExportLoadHarness/Program.cs` | deferred | V2 perf CLI verb | 5+ | defer-with-trigger |

### 6.5 Configuration

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 6.17 | **`config/default-tightening.json`** (50 lines; central policy toggles) | `config/default-tightening.json` | shipped (in V1) — **MISSING IN V2** | V2 must replicate; reference via pipeline.json or CLI | 3 | divergence-named (CRITICAL gap) |
| 6.18 | `config/type-mapping.default.json` (140 lines; OS→SQL Server type rules) | `config/type-mapping.default.json` | shipped (consumed) | V2 may inline or reference | 2-3 | parity |
| 6.19 | `config/appsettings.example.json` (34 lines) | `config/appsettings.example.json` | shipped (template) | V2 CLI analogous shape | 3-4 | parity |

### 6.6 Schema & docs

| # | V1 capability | V1 location | V2 status | Gap | Chapter | Disposition |
|---|---|---|---|---|---|---|
| 6.20 | `schema/cir-v1.json` (14KB; circular-dependency test fixture) | `schema/cir-v1.json` | in-flight (2-3) | `tests/Fixtures/cir-v1.json` | 2 (closed) | parity |
| 6.21 | `tools/schema-validator/` directory | `tools/schema-validator/` | pre-scoped | Likely lint/validation CLI; scope unclear | 4+ | defer-with-trigger |
| 6.22 | Handbook documentation (16 files, ~300KB) | `handbook/` | won't-carry-forward (docs-only) | V2 owns separate docs (`CLAUDE.md`, etc.) | — | won't-carry-forward |
| 6.23 | SSDT playbook (16 files, ~200KB) | `ssdt-playbook/` | won't-carry-forward (docs-only) | V2 may cross-reference | — | won't-carry-forward |
| 6.24 | `tasks.md` (47 lines; V1 dev tracking) | root | won't-carry-forward (transient) | V2 uses git+DECISIONS | — | won't-carry-forward |
| 6.25 | `architecture-guardrails.md` (V1 layering principles) | root | won't-carry-forward | V2 codifies in AXIOMS+DECISIONS | — | won't-carry-forward |
| 6.26 | `EDITORIAL-RECOMMENDATIONS.md` (V1 style guide) | root | won't-carry-forward | V2 owns `CLAUDE.md` style section | — | won't-carry-forward |
| 6.27 | `test-circular-deps.json` (test fixture) | root | in-flight | Reference data | 1 (closed) | parity |
| 6.28 | `test-baseline-summary.md`, `test-baseline-before-m1.2-m1.3.log` | root | won't-carry-forward (transient) | V1 dev artifacts; V2 baseline lives in tests | — | won't-carry-forward |
| 6.29 | `global.json` SDK pinning (sdk 9.0.305) | root + sidecar | shipped (same) | V1+V2 pinned identically | — | parity |
| 6.30 | DMM integration points (test suite) | `src/Osm.Dmm.Tests/` | pre-scoped | Validation pass in pipeline | 5 | defer-with-trigger |

---

## Section 7 — V2 intent gaps + ADMIRE + AXIOMS amendments outstanding

V2-side commitments not in chapter pre-scopes — governance, documentation, infrastructure, amendments, tracking. Detailed survey of 55 items.

### 7.1 AXIOMS.md amendments outstanding

| # | V2-intent commitment | Source | Current status | Owner | Disposition |
|---|---|---|---|---|---|
| 7.1 | T1 amendment for binary emitters (DACPAC) | VISION ac. crit. 2; CHAPTER_3_PRESCOPE_DACPAC §risk6 | pending | 3.3 close | governance |
| 7.2 | `EmitError` as primitive notion | VISION; VISION_REVIEW Appendix H | silent | 3 close | governance |
| 7.3 | Four-variant SsKey DU (A1 amendment) | VISION + Appendix H §H.5 | pending | ArtifactByKind refactor | tracked |
| 7.4 | T11 sibling-Π commutativity type theorem | Appendix H §H.4 + VISION ac. crit. 4 | pending | post-refactor | tracked |
| 7.5 | CatalogDiff as integral projection artifact | Appendix H §H.6; CHAPTER_3.5 | pending | 3.5 close | tracked |

### 7.2 Acceptance-criteria tracking infrastructure

| # | V2-intent commitment | Source | Current status | Owner | Disposition |
|---|---|---|---|---|---|
| 7.6 | Acceptance criterion #1 bookkeeping (canary-hit counter) | VISION ac. crit. 1 | pending (named) | governance | governance |
| 7.7 | Acceptance criterion #2 predicate library (idempotentRedeploy) | VISION ac. crit. 2 | pre-scoped (3.4) | 3.4 | tracked |
| 7.8 | Acceptance criterion #3 renameSurvives predicate (OssysOriginal SsKeys) | VISION ac. crit. 3 | pending (gated on 3.2) | 3.4 (depends on 3.2) | tracked |
| 7.9 | Acceptance criterion #4 retire substring T11 tests | VISION ac. crit. 4 | pending (tests named) | 3 close | governance |
| 7.10 | Acceptance criterion #5 V1 sunset gate + per-environment criteria | VISION ac. crit. 5 | pending (criterion unspecified) | governance | governance |

### 7.3 Cutover fallback ladder governance

| # | V2-intent commitment | Source | Current status | Owner | Disposition |
|---|---|---|---|---|---|
| 7.11 | Cutover fallback ladder governance rule R6 (split-brain elimination) | VISION; VISION_REVIEW Appendix B §B.2 | pending (in VISION; not in DECISIONS) | governance (pre-cutover) | governance |
| 7.53 | V1-only/V2-augmented/V2-driver decision framework (T-30, T-15 gates) | VISION fallback ladder | pending (criteria not in DECISIONS) | governance (pre-cutover) | governance |
| 7.54 | V1 warm-start guarantee | VISION ("V1 stays warm through cutover+30") | pending (rule named, not operationalized) | governance / 4 close | governance |
| 7.55 | Per-environment canary signal requirement | VISION ac. crit. 2 + 5 | pending | 3.4 → 4 | governance |

### 7.4 CatalogEquivalence Tolerance entries (each becomes a DECISIONS entry)

| # | V2-intent commitment | Source | Current status | Owner | Disposition |
|---|---|---|---|---|---|
| 7.12 | CatalogEquivalence comparator + Tolerance flags | CHAPTER_3.1 §2 | pending (pre-scoped) | 3.1 close | tracked |
| 7.13 | V2 newline-normalization divergence from V1 | comparator Tolerance | silent | 3 close | governance |
| 7.14 | V2 fingerprint algorithm divergence from V1 | implicit | silent | 3 close | governance |
| 7.15 | DECISIONS — post-deploy FK split toggle | VISION ch4 plan | silent | 4.1 close | governance |
| 7.16 | DECISIONS — EmissionPolicy DU placement | CHAPTER_4.1.B §2 | pending | 4.1.B close | tracked |
| 7.17 | DECISIONS — CdcAwareness on Profile, not Policy | CHAPTER_4.1.B; VISION ch4.1 | pending | 4.1.B close | governance |
| 7.18 | DECISIONS — SubtractiveRequiresConfirmation gate | CHAPTER_4.4 | silent | 4.4 close | governance |
| 7.19 | DECISIONS — UUIDv5 namespace commitment | CHAPTER_3.5 | pending | 3.5 close | governance |
| 7.20 | DECISIONS — strict-equal smart constructor discipline | implicit | silent | 3 close | governance |
| 7.21 | DECISIONS — read-side adapter promoted to chapter 3.1 | VISION ch3 plan | pending | 3 open | governance |
| 7.22 | DECISIONS — refusal of three-channel Diagnostics split (4.3) | CHAPTER_4.3 §1.4 | pending | 4.3 close | governance |
| 7.43 | Tolerance entry per CatalogEquivalence flag (each a DECISIONS entry) | CHAPTER_3.1 §2 | silent (per-tolerance entries not pre-written) | 3 close | governance |

### 7.5 ADMIRE.md state

| # | V2-intent commitment | Source | Current status | Owner | Disposition |
|---|---|---|---|---|---|
| 7.23 | ADMIRE chapter-2 entries currency check | ADMIRE entries | pending (status may have drifted) | 3 open | verify-existing |
| 7.24 | ADMIRE missing V1 components census | cross-reference with V1 trunk inventory | silent (incomplete) | governance | governance |
| 7.25 | ADMIRE entries for chapter-3 extractions (read-side, Dacpac, RefactorLog, canary, ArtifactByKind) | VISION ch3 plan | pending (chapter not open) | 3 close | tracked |
| 7.26 | ADMIRE template captures green-canary status | VISION ac. crit. 5 | pending (template not updated) | 3.4 close | governance |
| 7.51 | Stricter contract-testing for ADMIRE entries (each `extracted` cites a test) | ADMIRE; CLAUDE | pending (discipline in place; application incomplete) | 3 close | verify-existing |

### 7.6 CLAUDE.md state

| # | V2-intent commitment | Source | Current status | Owner | Disposition |
|---|---|---|---|---|---|
| 7.27 | CLAUDE operating-disciplines table currency | CLAUDE §"Operating disciplines" | pending | 3 close | verify-existing |
| 7.28 | CLAUDE F# feature surface table updates | CLAUDE §"F# feature surface" | pending | 3 + 4 close | verify-existing |
| 7.29 | CLAUDE load-bearing commitments alignment with HANDOFF | CLAUDE §"Load-bearing commitments" | pending (sync required) | 3 close | verify-existing |
| 7.30 | CLAUDE — add VISION.md to reading order | CLAUDE §"Reading order" | pending (VISION not listed) | 3 open | governance |

### 7.7 HANDOFF.md state

| # | V2-intent commitment | Source | Current status | Owner | Disposition |
|---|---|---|---|---|---|
| 7.31 | HANDOFF accumulation across chapters (forward signals from 8 pre-scopes) | HANDOFF §"Where to start" | pending (HANDOFF is ch2-to-3 bridge) | 3 close (write `HANDOFF_CHAPTER_3.md`) | tracked |

### 7.8 Projection.Pipeline orchestrator coordination

| # | V2-intent commitment | Source | Current status | Owner | Disposition |
|---|---|---|---|---|---|
| 7.32 | Projection.Pipeline scope at 3.1 close | CHAPTER_3.1 §2 | pending (pre-scoped) | 3.1 close | tracked |
| 7.33 | Projection.Pipeline growth in chapter 3.4 | CHAPTER_3.4 | pending | 3.4 close | tracked |
| 7.34 | Projection.Pipeline growth in chapter 3.5 | VISION ch3 plan | pending | 3.5 close | tracked |
| 7.35 | Projection.Pipeline growth in chapter 4.1 | CHAPTER_4.1.A + 4.1.B | pending | 4.1 close | tracked |
| 7.36 | Projection.Pipeline growth in chapter 4.1.B (promoted-lane integration) | CHAPTER_4.1.B | pending | 4.1.B close | tracked |
| 7.37 | Projection.Pipeline growth in chapter 4.3–4.4 | CHAPTER_4.3+4.4 | pending | 4.3-4.4 close | tracked |

### 7.9 Projection.Tests.Canary project

| # | V2-intent commitment | Source | Current status | Owner | Disposition |
|---|---|---|---|---|---|
| 7.38 | Projection.Tests.Canary project boundary + tier ownership | CHAPTER_3.4 §1 | pending | 3.4 close | tracked |
| 7.39 | tier-2/tier-3 properties added in later chapters | VISION + CHAPTER_3.4 | pending (cross-chapter) | 3.4 close (write growth plan) | governance |

### 7.10 Cross-cutting + risk register + post-3 documentation

| # | V2-intent commitment | Source | Current status | Owner | Disposition |
|---|---|---|---|---|---|
| 7.40 | Cutover risk register coverage (CDC, partial-state, split-brain, drift) | VISION_REVIEW Appendix B | pending (named, not gap-mapped) | 3 close | governance |
| 7.41 | V2 documentation updates post-chapter-3 close (README, CLAUDE, HANDOFF) | HANDOFF; README; CLAUDE | pending | 3 close | tracked |
| 7.42 | Acceptance criterion #1 canary-hit counter (location specified) | VISION ac. crit. 1 | pending (no surface) | governance | governance |

### 7.11 AXIOMS amendments verification

| # | V2-intent commitment | Source | Current status | Owner | Disposition |
|---|---|---|---|---|---|
| 7.44 | T1 amended (binary) | VISION_REVIEW Appendix H; chapter 3.3 | pending | 3.3 close | tracked |
| 7.45 | T11 (sibling-Π) | Appendix H §H.4 | pending | 3 close | tracked |
| 7.46 | A32 already in AXIOMS | AXIOMS A32 | done | — | verify-existing |
| 7.47 | A34 already in AXIOMS | AXIOMS A34 | done | — | verify-existing |
| 7.48 | T1 amended (triple determinism) | AXIOMS T1 amended | done | — | verify-existing |
| 7.49 | A1 amendment four-variant SsKey | Appendix H §H.5 | pending | 3 (refactor) | tracked |
| 7.50 | CatalogDiff structural commitment (axiom A35? amendment to A33?) | CHAPTER_3.5 | pending | 3.5 close | tracked |

### 7.12 Drift-detection job

| # | V2-intent commitment | Source | Current status | Owner | Disposition |
|---|---|---|---|---|---|
| 7.52 | Drift-detection job scheduling and surface | VISION ch3 plan ("read-side at four real DBs on schedule") | silent (no impl plan) | post-3 work | defer-with-trigger |

---

## Cross-cutting indexes

### Index by chapter (which items each chapter delivers)

**Chapter 3.1 (read-side adapter + comparator + Projection.Pipeline shell):** 1.30, 1.33, 5.1–5.10 (verb registry), 5.11–5.18 (CLI options), 5.27–5.29, 5.31, 5.34, 5.40, 5.45, 5.48, 6.6, 6.7, 6.12, 7.12, 7.21, 7.30, 7.32, 7.43

**Chapter 3.2 (SnapshotRowsets):** 1.1–1.5, 1.9, 1.13, 1.21–1.23, 1.26, 1.30, 5.3, 5.13, 5.16, 5.25, 5.46, 6.4

**Chapter 3.3 (DacpacEmitter):** 1.6, 1.10, 5.2, 5.4, 5.22, 5.31, 5.44, 5.49, 7.1, 7.44

**Chapter 3.4 (Canary property surface):** 5.30, 5.35, 5.39, 5.50, 7.7, 7.8, 7.26, 7.33, 7.38, 7.39

**Chapter 3.5 (RefactorLogEmitter + CatalogDiff):** 1.27, 3.59–3.61, 5.6, 5.26, 6.1, 7.5, 7.19, 7.34, 7.50

**Chapter 3-cross-cutting (ArtifactByKind refactor):** 7.3, 7.4, 7.49

**Chapter 4.1.A (SSDT DDL emitter + Manifest):** 3.1–3.76 (most), 5.44, 7.35

**Chapter 4.1.B (CDC-aware data triumvirate):** 4.1–4.29, 4.44–4.60, 5.24, 7.16, 7.17, 7.36

**Chapter 4.2 (User FK reflow):** 1.17 (`IsNoCheck`-related), 4.30–4.43, 5.10, 5.17, 5.19, 5.20, 5.23, 5.24, 6.30, 7.55

**Chapter 4.3 (Operational Diagnostics V2):** 2.19–2.28, 2.30–2.34, 2.46–2.47, 2.68–2.70, 5.5, 5.41–5.43, 5.47, 7.22, 7.37

**Chapter 4.4 (RemediationEmitter):** 2.51, 3.53, 7.18

**Post-chapter-4 / Chapter 5+ (DMM, LoadHarness, full CLI):** 6.1–6.3, 6.13–6.16, 6.21, 7.52

**Governance / cross-cutting (DECISIONS, AXIOMS, ADMIRE, CLAUDE):** 7.1–7.55 (most)

### Index by V2 status

**Shipped (parity confirmed):** ~72 items across sections 1–6 (V1-trunk capabilities V2 already mirrors at chapter-2 close).

**Pre-scoped (chapter pre-scope owns):** ~213 items across sections 3, 4, 5; minority in sections 1, 2, 6.

**Missing (gap to surface or close):** ~62 items, notably:
- 1.6 (Column physical reality: identity/computed/default/collation)
- 1.7–1.8 (check constraints)
- 1.11 (FK `IsNoCheck`)
- 3.11–3.13 (CHECK / computed / collation)
- 3.21–3.23 (disabled / filtered / included indexes)
- 3.27 (FK NOCHECK clause)
- 3.32–3.34 (extended properties — gated on 3.2)
- 4.51 (Migration data intake schema — V2-growth)
- 6.17 (`config/default-tightening.json` — CRITICAL gap)
- 7.x governance items (AXIOMS amendments, DECISIONS entries)

**Won't-carry-forward:** ~26 items, notably:
- 1.12 (rowset #18: triggers)
- 1.34 (DACPAC variant input)
- 2.52 (RemediationQueryBuilder SQL generation)
- 2.67 (SsdtPredicateCoverage)
- 3.22 (INCLUDE columns on indexes)
- 3.38–3.40 (triggers)
- 5.9 (V1 `policy` verb)
- 5.18 (`--mock-advanced-sql` to operator CLI)
- 5.21 (`--max-degree-of-parallelism`)
- 6.22–6.26, 6.28 (V1 docs and transient artifacts)

**Deferred (defer-with-trigger):** ~30 items, gated on real consumer demand.

### Index by disposition

**Parity (V2 must match V1):** ~200 items.
**Divergence-named (V2 deliberately differs; documented):** ~25 items including 3.3 (CREATE SCHEMA), 3.27 (NOCHECK), 3.48 (newline), 3.52 (fingerprint), 6.17 (default-tightening.json placement).
**Widening (V2-growth):** ~30 items — categorical/numeric distributions, evidence DUs, conflict DUs, structured rationales, CDC awareness, RefactorLogEmitter, ArtifactByKind, RemediationEmitter, triangulation comparator, `IsUserFk`.
**Defer-with-trigger:** ~30 items.
**Cut / won't-carry-forward:** ~26 items.

### Index by V1 file (for V1 maintainers cross-referencing)

(Most-referenced V1 files; full citations in tables above.)

- `src/Osm.Pipeline/SqlExtraction/SnapshotJsonBuilder.cs` — items 1.21–1.25
- `src/AdvancedSql/outsystems_metadata_rowsets.sql` — items 1.1–1.13
- `src/Osm.Domain/EntityModel.cs`, `AttributeModel.cs`, `RelationshipModel.cs`, `IndexModel.cs` — items 1.14–1.20
- `src/Osm.Validation/Tightening/Opportunities/Opportunity.cs` — items 2.19–2.26
- `src/Osm.Validation/Tightening/Decisions/PolicyDecisionReporter.cs` — items 2.30–2.34
- `src/Osm.Smo/PerTableWriter.cs` — items 3.41–3.48
- `src/Osm.Smo/PerTableEmission/CreateTableStatementBuilder.cs` — items 3.5–3.16
- `src/Osm.Smo/IndexNameGenerator.cs`, `ForeignKeyNameFactory.cs`, `ConstraintNameNormalizer.cs` — items 3.10, 3.16, 3.19, 3.20, 3.28, 3.29
- `src/Osm.Emission/SsdtManifest.cs` — items 3.49–3.58
- `src/Osm.Emission/SsdtEmitter.cs` — items 3.62–3.67
- `src/Osm.Emission/Seeds/StaticSeedSqlBuilder.cs` — items 4.1–4.9, 4.22–4.26, 4.46
- `src/Osm.Emission/PhasedDynamicEntityInsertGenerator.cs` — items 4.11–4.18, 4.24
- `src/Osm.Pipeline/UatUsers/*.cs` — items 4.30–4.43
- `src/Osm.Cli/*.cs` — items 5.1–5.30
- `src/Osm.Pipeline/Orchestration/*.cs` — items 5.31–5.40, 5.41–5.43
- `src/Osm.Dmm/*.cs` — items 6.1–6.3
- `tests/Osm.TestSupport/*.cs` — items 6.6–6.12
- `src/Osm.LoadHarness/*.cs` — items 6.13–6.15

---

## Closing notes

This backlog is **comprehensive but not infallible**. The surveys' subagents walked V1 exhaustively but ~5–10 items per area may have been missed under the time budget. Verify against trunk during chapter-open ritual and during chapter-mid-audit.

**Recommended next steps:**

1. **Pre-chapter-3 governance burst** (item 7.11, 7.21, 7.30, 7.53): write four DECISIONS entries before chapter 3 opens.
2. **Verify ADMIRE currency** (item 7.23): scan ADMIRE.md and update status strings for chapter-2 entries.
3. **Verify AXIOMS currency** (items 7.46–7.48): confirm A32, A34, T1 amended are current.
4. **Cross-reference each chapter pre-scope against this backlog**: identify any items the pre-scope omits.

The backlog is cumulative and grows as chapters close. ADMIRE.md absorbs items as they reach `extracted (green canary)` status; DECISIONS.md absorbs governance entries; AXIOMS.md absorbs amendments. By end of chapter 4.4 + governance burst, ~290 of 375 items should be closed; remaining ~85 are post-cutover trajectory (DMM, LoadHarness, drift-detection job, multi-environment consensus).
