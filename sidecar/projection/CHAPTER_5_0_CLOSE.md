# Chapter 5.0 close — OSSYS catalog producer carbon-copy (Phase 8; offline-first)

**Sessions:** chapter 5.0 opened + slices α + β + γ + δ + ε + close shipped in one session arc (2026-05-17). Branch `claude/review-chapter-close-Rqo0x`. Slice commits: open + α `26fc5dd` → β `8cc40f8` → γ+δ+ε `5ab15c5` → η (this commit).

V2 now stands on its own against an offline OSSYS source. The cutover-window pivot is structurally enabled: V2 can acquire its `Catalog` from a SQL Server hosting an OSSYS database without any V1 runtime dependency.

---

## What this close enables

- **V2's catalog acquisition path is V1-independent.** The SQL contract (`outsystems_metadata_rowsets.sql`) lives in V2's source tree as an embedded resource. V2's F# runner (`MetadataSnapshotRunner`) takes a `SqlConnection` + parameters, executes the script, walks the result sets, and produces a `RowsetBundle` that V2's existing `CatalogReader.SnapshotRowsets` consumes.
- **Offline canary in place.** The chapter validates end-to-end against a synthetic OSSYS source (carbon-copied from V1's `model.edge-case.seed.sql`) running in the warm Docker SQL Server. Five canary tests assert structural invariants on the extracted Catalog. The canary is included in the regression flow as the principal-PO directed: "I wouldn't mind also finding a way to mock up the query itself so it can be included in the canary flow."
- **The live-DB pivot is one configuration step away.** The runner is connection-agnostic. To extract from a real OSSYS database: `use cnn = new SqlConnection(productionConnString); cnn.OpenAsync(); MetadataSnapshotRunner.runAsync cnn parameters`. No code change required.

---

## What shipped (slice arc α / β / γ / δ / ε)

### Slice α — Carbon-copy + `Projection.Adapters.OssysSql` F# project (`26fc5dd`)

- New F# project `Projection.Adapters.OssysSql` (sibling to `Projection.Adapters.Sql` + `Projection.Adapters.Osm`).
- V1's `outsystems_metadata_rowsets.sql` (1184 LOC; 22 result sets) carbon-copied verbatim as embedded resource. Byte-identical to V1 source (md5 verified at copy time).
- `MetadataExtractionSql.read()` accessor; 4 tests + 1 gated parity test.
- ADMIRE.md transitions the entry to "carbon-copy in flight"; 2026-05-17 event recorded.

### Slice β — OSSYS bootstrap fixture (canary mockup donor; `8cc40f8`)

- V1's `model.edge-case.seed.sql` (232 LOC) carbon-copied verbatim as `Resources/ossys-edge-case.seed.sql`.
- Synthetic OSSYS source with deterministic edge-case data: 3 modules (AppCore / Ops / SystemUsers); 5 entities including system, external cross-schema, partitioned-with-trigger, FK-referenced; 16 attributes covering FK references, identifiers, defaults, deactivation, external column types; corresponding physical tables.
- `MetadataExtractionSql.readEdgeCaseSeed()` accessor; 4 tests.

### Slice γ — F# `MetadataSnapshotRunner` (`5ab15c5`)

- `MetadataSnapshotRunner.runAsync` executes the carbon-copied SQL against an open `SqlConnection` with the V1-shaped 5-parameter contract (`ModuleNames` / `IncludeSystem` / `IncludeInactive` / `OnlyActiveAttributes` / `EntityFilterJson`).
- Walks all 22 result sets via `DbDataReader.NextResultAsync`; parses the first 5 (Modules / Entities / Attributes / References / PhysicalTables) into typed F# records mirroring V1's `Outsystems*Row` DTOs; skips the remaining 17 (consumed by V2 in future slices as evidence demands).
- Result: typed `MetadataSnapshot` carrier.

### Slice δ — `toBundle` composer (`5ab15c5`)

- `MetadataSnapshotRunner.toBundle` composes the typed snapshot into V2's existing `CatalogReader.RowsetBundle` via JOIN logic:
  - Each entity row joins to its `PhysicalTable` for the `DbSchema` value (defaulting to `"dbo"` when absent).
  - Each attribute row's `DeleteRule` propagates to the joined reference.
  - References carry their `RefEntityId` through for cross-key-shape FK target resolution (see "Pre-existing V2 fix" below).
- The downstream `CatalogReader.parse (SnapshotRowsets bundle)` produces a V2 `Catalog`.

### Slice ε — OSSYS extraction canary (`5ab15c5`)

- `OssysExtractionCanaryTests` (5 tests; ~8s total against Docker):
  - 3 modules extracted (AppCore / Ops / SystemUsers).
  - AppCore has 3 entities (Customer / City / BillingAccount).
  - BillingAccount lands in `billing` schema (cross-schema external entity).
  - Customer has 6 attributes including the `CityId` FK reference.
  - Extraction is deterministic across repeated runs.
- New Deploy primitive `withBootstrappedDatabase`: creates per-run database, applies seed SQL, invokes body with open connection. Reusable for any "bootstrap and act" canary shape.

---

## Pre-existing V2 fix surfaced by the canary

The chapter surfaced (and fixed) a latent bug in V2's `parseReferenceRowFor`: when the target entity carried its own `EntitySsKey` (V1 GUID-based identity), the prior code constructed the FK target SsKey via `kindSsKey moduleName refRow.RefEntityName` (always synthesized). This produced a different SsKey shape than the target kind's own key (`SsKey.ossysOriginal g`), breaking the `danglingTarget` invariant.

**Fix:** Extended `ReferenceRow` with `RefEntityId : int option`. `parseRowsetBundle` now builds a global `Map<int, SsKey>` from EntityRows joined to ModuleRows; `parseReferenceRowFor` resolves the target via this map (preserving GUID-based identity); falls back to the synthesized shape when the target ID is absent. Default `RefEntityId = None` preserves the prior behavior for all existing test fixtures.

**Additional fixes triggered:**
- `parsePrimitiveType` extended to cover Boolean / DateTime / Date / Time / Integer / LongInteger / Decimal / Currency / BinaryData / Email / PhoneNumber (was previously only `Identifier` + `Text`).
- V1's seed fixture diverges in V2's copy: removed self-managed `OutsystemsIntegration` DB creation + `FG_Customers` filegroup + partition function/scheme + `REBUILD PARTITION ... WITH DATA_COMPRESSION` + `IGNORE_DUP_KEY = ON` on filtered index (modern SQL Server rejects). Divergences documented inline at each site.

---

## Eight-item chapter-close ritual

### 1. Active deferrals scan

| Deferral | Status |
|---|---|
| **OSSYS catalog producer carbon-copy** (Phase 8 top entry) | ✅ **Initial carbon-copy shipped** (slices α + β). Remaining 17 result sets uncomposed but accessible — future slices lift as IR coverage demands. |
| **Live SqlClient wiring** | ✅ **Structurally complete** — the runner is connection-agnostic; production wiring is supplying a real connection string. No code change required. |
| **OSSYS schema bootstrap for canary** | ✅ **Shipped** at slice β + ε. |
| **`parsePrimitiveType` data-type coverage** | ⚠️ **Partial** — chapter extended to ~12 common types; other V1 OutSystems types (e.g., `Currency` variants, business-specific types) may need addition under evidence. **Trigger: parity audit identifies untested type.** |
| **Cross-key-shape FK resolution** (latent bug pre-chapter) | ✅ **Fixed** at slice δ. |
| **17 uncomposed V1 rowsets** | Untriggered — V2's `RowsetBundle` shape currently consumes 4 rowsets; the runner walks but skips the other 17. Each consumer-driven lift adds an `RowsetBundle` field + a runner mapping. |
| **V1 inheritance opportunities (other than OSSYS)** | Phase 8 V1 inheritance log still names DACPAC + SnapshotRowsets as the principal opportunities; this chapter retires the OSSYS one. |

### 2. Contract-vs-implementation walk

Chapter open §1 named the contract: "V2 stands on its own against an offline OSSYS source; everything offline-doable ships before the live-DB pivot." Five slices shipped substantive deliverables; the 6th planned slice (live SqlClient wiring) reduces to a no-op because the runner is connection-agnostic.

### 3. CLAUDE.md staleness check

Operating-disciplines table current. **New discipline emerging this chapter (codified at chapter close):** parity-audit cadence (see handoff). The discipline lands as a row in the operating-disciplines table at chapter 5.1 open.

### 4. README.md staleness check

Test baseline 1441 → **1454 non-canary** (+13 from chapter 5.0 — α 4 + β 4 + ε 5; slice γ + δ structurally absorbed in ε's end-to-end coverage). Plus 1 Skip (gated parity).

### 5. HANDOFF.md scope

New chapter-5.0 close prologue added at this commit. Names: load-bearing (`Projection.Adapters.OssysSql`; `MetadataSnapshotRunner`; cross-key-shape FK resolution fix; OSSYS canary in regression flow), retained forward signals (17 uncomposed rowsets; live-DB integration), and the **parity-audit framing for chapter 5.1+**.

### 6. Fresh-eye walk (cross-document drift)

- `BACKLOG.md` — Phase 8 row updated (OSSYS catalog producer "shipped" status); new **Parity Audit Wave** section added under Cross-Cutting Infrastructure with the discipline framing + initial slice queue.
- `ADMIRE.md` — OSSYS catalog producer entry transitioned: "extracting (in flight)" → "carbon-copy in flight"; 3 carbon-copy events recorded (2026-05-17 α SQL file; 2026-05-17 β seed fixture; 2026-05-17 γ runner stub).
- `V2_DRIVER.md` — chapter 5.0 not previously listed at the chapter-grain level; folds under the Phase 8 entry.

### 7. V1-input-envelope walk

V1 references walked + carbon-copied:
- `src/AdvancedSql/outsystems_metadata_rowsets.sql` (1184 LOC) — verbatim copy.
- `tests/Fixtures/sql/model.edge-case.seed.sql` (232 LOC) — copy with documented divergences (DB management, IGNORE_DUP_KEY on filtered index, partition function/scheme/REBUILD).
- `src/Osm.Pipeline/SqlExtraction/MetadataSnapshotRunner.cs` (~400 LOC) — F# rewrite at copy-time per chapter open Q1; column-ordering contracts inherited from V1 processors (`ModulesResultSetProcessor`, `EntitiesResultSetProcessor`, `AttributesResultSetProcessor`, `ReferencesResultSetProcessor`, `PhysicalTablesResultSetProcessor`).
- `src/Osm.Pipeline/SqlExtraction/IOutsystemsMetadataReader.cs` (DTOs for 22 rowsets) — first 5 lifted as F# records in `MetadataSnapshotRunner`; remaining 17 documented as future-coverage targets.

### 8. AXIOMS.md amendment cash-out

No new amendments. Chapter operates within:
- **A18 amended** — `MetadataSnapshotRunner` is an adapter; it doesn't consume Policy.
- **A39** — `CatalogReader.create` smart constructor still validates the produced Catalog (the FK fix preserves the invariant).
- **T1** — bootstrap + extraction is deterministic across runs (canary test asserts this).

---

## Test count

- **1454 non-canary tests passing** (was 1441 at chapter 4.9 close; +13 net across this chapter).
- **+1 gated parity Skip** (V1 byte-identity check; runs in V1-trunk-present environments).
- **+5 OSSYS extraction canary tests** in the Docker-gated set.
- **Lint clean** across 27 rules.
- **Build clean** under `TreatWarningsAsErrors=true` everywhere.

---

## What's load-bearing going forward

- **`Projection.Adapters.OssysSql`** — V2's live-SQL extraction adapter. Sibling to `Projection.Adapters.Sql` (deployed-schema reads) and `Projection.Adapters.Osm` (V1 JSON / rowset bundle reads).
- **The SQL contract IS the truth (carbon-copy verbatim).** V2 doesn't reinvent the SQL; V2 carries V1's. The parity test gates byte-equality in V1-trunk-present environments.
- **The runner walks all 22 result sets even when consuming 4.** Future lift slices add field-level parsing without changing the orchestration shape.
- **The `withBootstrappedDatabase` Deploy primitive** is reusable for any bootstrap-and-act canary shape (V2 may grow more synthetic-source canaries: DACPAC bootstrap, V1-trunk JSON snapshot bootstrap, etc.).
- **The cross-key-shape FK resolution fix.** When rowset bundles carry GUID-based EntitySsKeys, references now resolve correctly. Pre-existing latent bug, fixed.

---

## What's deferred (with explicit triggers)

### 17 uncomposed V1 rowsets

V2's `RowsetBundle` currently surfaces 4 rowsets. V1 emits 22. The 17 uncomposed: ColumnReality (sys.columns reflection), ColumnChecks (CHECK constraints), PhysicalColumnsPresent, Indexes (sys.indexes — separate from V2's IR `Indexes` field; V1's reflects on-disk), IndexColumns, ForeignKeys (sys.foreign_keys reflection), ForeignKeyColumns, ForeignKeyAttributeMap, AttributeHasFk, ForeignKeyColumnsJson, ForeignKeyAttributeJson, Triggers, AttributeJson, RelationshipJson, IndexJson, TriggerJson, ModuleJson.

**Each maps to a V2 IR field or potential consumer.** Each lift adds: (a) `RowsetBundle` field; (b) typed F# record in `MetadataSnapshotRunner`; (c) result-set parser in `runAsync`; (d) `toBundle` JOIN logic. Trigger per lift: consumer demand from a downstream V2 path or parity audit identifying drift.

### Live `SqlClient` wiring

Structurally complete — see "What this close enables." The remaining work is the production-ops piece: connection-string discipline (where stored, how rotated), connection-pool hygiene, retry-on-transient-error semantics, and the integration test against a real OSSYS instance. Deferred as a per-environment ops concern.

### V1 source-files NOT YET carbon-copied (per the parity audit)

The remaining V1 surface in `Osm.Pipeline.SqlExtraction.*` (50+ C# files); `Osm.Domain.Model.*` (the V1 aggregate-root model); `Osm.Json.*` (V1's JSON deserialization layer); `Osm.Smo.*` (V1's SMO emission layer). These are the targets of the **parity-audit wave** opening at chapter 5.1.

---

## Closing

Chapter 5.0 is the **cutover-window pivot**. V2's catalog acquisition is now V1-independent at the structural level. The canary mockup gates regression coverage on every commit.

Per V2_DRIVER's per-axis correctness stakes, this chapter is **the unblocking move for Schema-axis V2-driver mode** — V2 can now produce its own Catalog from the production source. The remaining work to reach V2-driver mode shifts from "build the missing capability" to "verify parity with V1's existing capability."

The next chapter (5.1) opens with that exact framing: a **parity-audit wave**. The principal-PO direction at chapter 5.0 close:

> "I'd like to start heavily auditing the v1 codebase, step by step, to ensure there is maximal parity — there's a ton of code paths in V1 and I want to make sure the representational coverage-state of the parity is expressible in a formal way in the next agent's discipline. Makes sense? I'm sure we'll find plenty, and so I'd prefer to go in depth on a given small slice, one slice at a time."

This frames chapter 5.1+ as the **V1 Parity Audit Wave** — small slices, one V1 capability at a time, with formal coverage-state tracking via a new `V1_PARITY_MATRIX.md` discipline document. The handoff details the discipline in full.

— Chapter 5.0 closed (2026-05-17).
