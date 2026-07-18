# CUTOVER BOARD POPULATION PLAN — the single green-to-cut-over monitor

> **Written 2026-07-18** as the build-plan companion to `AUDIT_2026_07_17_V1_V2_PARITY.md`.
> The audit is the *flip side* of this document: the audit found every way v2's **emission** can be
> wrong; this plan makes the **estate board** (`check estate` / `EstateFinding` / `Readiness`) the
> single surface where every way a **cutover** can go red is a finding carrying enough evidence to
> act on — *clear the data*, *NOCHECK it for now*, *hand-author the object*, or *it's an engine fix*.
> When this plan disagrees with the code or `DECISIONS.md`, they win.

---

## 0 — What "cut over" means here, and the scope this plan owns

"Cut over" is **not** merely "v2 replaces v1." It is: **execute an all-OutSystems-sourced External
Entities SSDT setup that (a) honors SQL Server / SSDT export norms, (b) preserves source model
intent, and (c) preserves source data** — with *no surprises*. The board is how that becomes
monitorable rather than hoped-for.

**The operator's actual move** (ontology `THE_USE_CASE_ONTOLOGY.md` proteins **P-1 / P-2**): a
per-environment load — Dev-cloud→Dev on-prem, QA-cloud→QA on-prem, UAT-cloud→UAT on-prem — each
carrying *its own* environment's data against a synchronized schema. The cross-environment user
re-key (P-3) is deferred to backlog. The chain is
`Snapshot → Diff → Gate → Publish → Insert (CDC-aware MERGE, FK-topological) → Measure → Verify → Record`.

**The operator's artifact set** (locked, decision Q15): `Modules/<Module>/<Schema>.<Table>.sql`
(tables) + `Data/StaticSeeds.sql` + `Data/MigrationData.sql` + `Data/Bootstrap.sql` (per
environment). The operator copies the `Modules` directory into the deploy repo and PRs it. **Out of
scope and therefore off the board:** the `.sqlproj`, publish profiles, the engine-emitted
refactorlog, and the dacpac — so the audit's `.sqlproj` G5 hazards, publish-profile gap,
refactorlog-in-bundle, and dacpac-path findings are **moot for this cutover** and are not board
concerns.

**Definition of green (this plan's north star).** The board is green — ready to cut over an
environment — when, for that environment:
1. `Readiness.isReady` holds (schema matches the agreed shape; **zero** data dealbreakers), **and**
2. **zero** Emission-plane *dealbreaker* findings remain (the emitted `Modules/*.sql` + data lanes
   deploy clean and preserve intent + data), **and**
3. every orphan / null / overflow finding is either **cleared** (REPAIR lane retired) or
   **NOCHECK'd with a reopen probe** (RELAX lane, the re-tighten trigger recorded).

Red is any dealbreaker still open. Amber (WATCH) is advisory and never blocks.

---

## 1 — The board today (what already exists — do not rebuild it)

The estate chapter (PR #668) already shipped a real, typed board. Reuse it.

- **Lanes** (`EstateLane`, board order): **DECIDE** (rulings only the operator can make) →
  **REPAIR** (mechanical fixes — *clear the data, then enforce*) → **RELAX** (the interim posture —
  *leave it unenforced / NOCHECK for now*, each carrying a **reopen probe**: one runnable `SELECT`
  whose zero result retires the relaxation) → **WATCH** (capped advisories; no lever).
- **Planes** (`EstatePlane`): **Schema / Data / Identity / Operational / Emission**. The Emission
  plane is labeled in-code as "the #669 audit dimension — whether the schema this estate would
  publish faithfully models database reality, and whether it would deploy at all." *That is exactly
  this plan's home.*
- **Evidence** (`EstateFinding.Evidence : (string * int64) list`): per-finding labeled counts, per
  environment. RELAX findings additionally carry `ReopenProbe : string`.
- **The readiness gate** (`Readiness.fs`, `projection check shape`): profiles each environment's data
  against the agreed schema; any **data dealbreaker** (NULLs into NOT NULL, dups into UNIQUE,
  **orphaned FKs**, width/type overflow) → `Verdict.Paused`. `Readiness.isReady` requires **every**
  environment `Ready`.
- **Finding kinds that already exist and already populate** (`EstateFinding.fs` + `Estate.fs`):
  - Data: `DataNotNull` / `DataNotNullPastBand`, `DataOrphans` / `DataOrphansPastBand`, `DataUnique`,
    `DataOverflow`, `DataAsymmetry`, `DataUniquenessCandidate`, `DataHeadroom`, `DataDateSentinel`,
    `DataCollationCollision`.
  - Schema: `SchemaPresence`, `SchemaLag`, `SchemaRename`, `SchemaAttributes`, `SchemaReferences`,
    `SchemaIndexes`, `SchemaTrigger`, `SchemaCheck`, `SchemaModality`, `SchemaActivity`, `SchemaTrust`.
  - Emission (raised in `Estate.fs`): `EmissionCompositePkFk`, `EmissionDuplicateName`,
    `EmissionLongName`, `EmissionLossyScalar`, `EmissionNoPrimaryKey`, `EmissionNonDefaultOnUpdate`.
  - Identity: `IdentitySynthesized`. Operational: `OperationalCdc`. Posture: `PostureActive`,
    `PostureRetirable`.

**The two-line takeaway:** the *data* plane already models the operator's "remove the data or NOCHECK
the row" workflow exactly (`DataOrphans` REPAIR ↔ `DataOrphansPastBand` RELAX). The *Emission* plane
is real but **under-covered** — it sees ~6 failure modes, and the audit found ~10 more. Populating
the board is mostly (i) adding Emission finding kinds + detection, (ii) *joining detection to emission*
so a detected problem is also a loud refusal rather than silent broken DDL, and (iii) wiring the
data-plane population for the newly-enforced logical-only FKs.

---

## 2 — The locked decisions this board must reflect

Every emission decision from the parity review, and its board consequence.

| # | Decision (locked) | Board / emission consequence |
|---|---|---|
| 1 | **Nullability tightening OFF** (model faithfulness paramount) | The engine never coerces model-nullable → NOT NULL from data evidence. No board finding needed for *not* tightening; a mandatory-but-dirty column instead surfaces as `DataNotNull`. |
| 2 | **Preserve deployed NOT NULL** on a model-nullable column | Engine fix (consult physical `is_nullable`); until shipped, `EmissionDeployedNotNullLoosened` (NEW) surfaces the silent constraint drop. Principle: **deployed-schema > model > data-evidence**. |
| 3 | **Logical-only FKs enforced, evidence-gated** | Orphans for each enforced logical-only ref must raise `DataOrphans` (REPAIR: clear) / `DataOrphansPastBand` (RELAX: NOCHECK + reopen probe). **Never a silent deploy-time `Msg 547`.** |
| 4 | **Preserve `''` distinctly** (do not coerce to NULL) | Engine fix on the data plane; a NOT-NULL text column carrying `''` remains a real value, not a null violation. |
| — | **Authored `DEFAULT ''` renders correctly** (not `N''''''`) | Engine fix (M-1); until shipped, `EmissionAuthoredDefault` (NEW) flags the mis-render. |
| 6 | **Synthesize FK names; reset inconsistent authored names** | No board finding (intended reset). |
| 7 | **Keep platform-auto (`OSIDX`) indexes** | No board finding (kept by design). |
| 8 | **`Ignore` delete-rule → no FK** (configurable; model-intent faithful) | No board finding (faithful); the config toggle is the lever. |
| 9 | **Triggers: rewrite to logical; refuse loudly + surface if it can't round-trip** | `EmissionTriggerUnrewritten` (NEW), Emission plane, DECIDE/REPAIR. |
| 10 | **CDC-silence predicate default-ON** | Monitor via `Measure` (CDC count = |delta|; 0 on redeploy) → `OperationalCdc` / a churn advisory (WATCH). Not a gate. |
| 11 | **Keep `MS_Description`; `identityAnnotations: false`** (drop `SsKey`/`LogicalName` for cutover) | Config setting, no finding. `MS_Description` is ungated and survives. |
| 12 | **temporal / PERSISTED / sequences: FAIL LOUDLY if present** | `EmissionTemporalDropped` / `EmissionPersistedDropped` / `EmissionSequenceDropped` (NEW), Emission plane, **DECIDE dealbreakers**. The estate sweep (§5) decides whether they fire. |
| 13 | **SQL Server 2022 (Sql160)** | No finding; a one-time platform assertion. |
| 14 | **OutSystems scalar-type intent wins** (37,8 / DATETIME / NVARCHAR text · VARCHAR email+phone / BIGINT) | Platform mapping confirmed against the OutSystems 11 Database Data Types reference (DECISIONS 2026-07-18); norm docs corrected. `EmissionLossyScalar` continues to flag genuine downgrades. |
| lock | **DF/CK constraint names → logical proper-case** | Engine fix; no finding (a name-quality correction). |

---

## 3 — The finding map (the core): every cutover-relevant finding → its board home

Grouped by the operator's artifact surfaces. **NEW** = a finding kind to add; **exists** = already on
the board (may need population wiring). `DB` = **dealbreaker** (blocks green); `RL` = relax-able
(NOCHECK/leave with reopen probe); `HA` = hand-author (shared gap, both engines).

### 3.A — Table DDL emission (`Modules/*.sql`)

| Audit ref | Failure mode if unaddressed | Board kind | Plane · Lane | Evidence to carry | Detection seam | Class |
|---|---|---|---|---|---|---|
| EF-2 / M-1 | `getutcdate()` default → deploy OK, insert `Msg 241`; `''` → `N''''''` | `EmissionAuthoredDefault` **NEW** | Emission · DECIDE | table.column, raw `DEFAULT_VALUE`, rendered SQL | OSSYS default-lift (`OssysRowsetReader` / `SqlLiteral.ofRaw`) | DB |
| EF-19 / M-8 | Computed-col expr keeps physical identifiers → `Msg 207` on CS collation | `EmissionComputedExprIdentifiers` **NEW** | Emission · REPAIR | table.column, expression text, target collation | computed-col identifier rewrite pass | DB (on CS collation) |
| EF-7 | Trigger `ON`-target/body unrewritten → deploy `Msg 8197` | `EmissionTriggerUnrewritten` **NEW** | Emission · DECIDE | table, trigger name, unresolved physical refs | trigger-rewrite round-trip check (decision 9) | DB |
| EF-15 / M-2 | `DATA_COMPRESSION` dropped → silent decompress | `EmissionIndexOptionDropped` **NEW** | Emission · WATCH→REPAIR | table, index, source `data_compression_desc` | rowset index-option carriage | RL (storage/IO drift) |
| EF-17 / B-3 | Composite-PK FK first-leg-only → deploy `Msg 1776` | `EmissionCompositePkFk` **exists** — *join to refusal* | Emission · DECIDE | owner, target, PK legs vs emitted legs | `Estate.fs:954` (detects) → emitter must refuse, not truncate | DB |
| EF-18 / M-3 | Deployed `NOT NULL` loosened to `NULL` | `EmissionDeployedNotNullLoosened` **NEW** | Emission · DECIDE | table.column, deployed `is_nullable=0`, model `Is_Mandatory=0` | nullability resolution (decision 2 fix) | DB |
| EF-16 | Two same-leading-column indexes → `Msg 1913` | `EmissionDuplicateName` **exists** (ordinal dedup already emitted) | Emission · — | — | already handled by SsKey ordinals | ✓ fixed |
| A6 | Generated name > 128 chars | `EmissionLongName` **exists** | Emission · WATCH | name, hashed form | `Coordinates` budget | ✓ |
| H7 | Cross-module duplicate logical table name | `EmissionDuplicateName` **exists** | Emission · DECIDE | the colliding logical name, both modules | catalog logical-name uniqueness | DB |
| C-scalars | Genuine scalar downgrade (not the intended OutSystems mapping) | `EmissionLossyScalar` **exists** | Emission · WATCH | source vs emitted type | scalar map | RL |
| D1 | PK-less kind emitted as heap | `EmissionNoPrimaryKey` **exists** | Emission · DECIDE | kind, `allowMissingPrimaryKey` status | catalog | RL (allow-list) |

### 3.B — Data-lane emission (`Data/StaticSeeds.sql`, `MigrationData.sql`, `Bootstrap.sql`)

| Audit ref | Failure mode | Board kind | Plane · Lane | Evidence | Detection seam | Class |
|---|---|---|---|---|---|---|
| EF-8 / B-1 | Alphabetical fallback on any unresolved cycle → bootstrap `Msg 547` on linear run | `EmissionDataLaneOrder` **NEW** | Emission · REPAIR | the cycle members, the mis-ordered pair | `TopologicalOrderPass` Mode=Alphabetical on the data lanes | DB |
| F2 / EF-25 | `''` coerced to NULL (pre-decision-4) | (engine fix) | — | — | data-plane literal codec | ✓ fixed by decision 4 |
| EF-23 | (v1-only) >1000-row Authoritative row loss | n/a — v2 immune | — | — | — | ✓ (v2 not affected) |

### 3.C — Estate-readiness data findings (the operator's clear-or-NOCHECK surface)

These already exist and already populate; the plan **wires the newly-enforced logical-only FKs into
them** (decision 3) and confirms the reopen-probe discipline.

| Condition | Board kind (exists) | Lane | Operator action |
|---|---|---|---|
| Orphan rows on an enforced (incl. logical-only) FK, clearable | `DataOrphans` | REPAIR | clear the orphan rows, then enforce |
| Orphan rows too many to clear before cutover | `DataOrphansPastBand` | RELAX | NOCHECK the FK; reopen probe = the orphan-count `SELECT` |
| Model-mandatory column with NULLs in the data | `DataNotNull` | REPAIR | backfill, then the column deploys NOT NULL |
| … too many NULLs to backfill pre-cutover | `DataNotNullPastBand` | RELAX | leave nullable; reopen probe = the null-count `SELECT` |
| Values exceeding the emitted length | `DataOverflow` | REPAIR | clean/rewiden |
| Duplicate values under a promoted UNIQUE | `DataUnique` | REPAIR | dedupe |

### 3.D — Shared-gap dealbreakers (decision 12 — fail loud if present)

| Object class | Board kind | Plane · Lane | Evidence | Class |
|---|---|---|---|---|
| System-versioned temporal table | `EmissionTemporalDropped` **NEW** | Emission · DECIDE | table, period cols, history table | **DB** |
| `PERSISTED` computed column | `EmissionPersistedDropped` **NEW** | Emission · DECIDE | table.column | **DB** |
| Sequence object / sequence-backed default | `EmissionSequenceDropped` **NEW** | Emission · DECIDE | sequence name, consuming column | **DB** |

Rationale (operator, decision 12): *"if they're there, they deserve to be included; not bringing
them over is a dealbreaker."* Absent ⇒ these never fire and it stays a backlog item. Present ⇒ red
until the object is hand-authored into the deploy repo or engine support lands.

---

## 4 — What to build: new kinds + population wiring

**New Emission finding kinds** (each: add to the `EstateFindingKind` closed DU — the totality check
forces `laneOf`/`planeOf`/text; add its detector in `Estate.fs`'s `emissionFindingsFor`):
`EmissionAuthoredDefault`, `EmissionComputedExprIdentifiers`, `EmissionTriggerUnrewritten`,
`EmissionIndexOptionDropped`, `EmissionDeployedNotNullLoosened`, `EmissionDataLaneOrder`,
`EmissionTemporalDropped`, `EmissionPersistedDropped`, `EmissionSequenceDropped`.

**Join detection to emission (the `downgrades-never-silent` law).** Today `EmissionCompositePkFk` is
*detected* by `check estate` but `publish` still silently truncates. Generalize the pattern: every
Emission **dealbreaker** must also make the emitter **refuse loudly** (named diagnostic), so a red
board and a refused publish are the same fact. No dealbreaker may be emit-time silent.

**Population wiring for existing kinds:**
- `DataOrphans` / `DataOrphansPastBand` must receive the orphan evidence for **logical-only** refs
  now that decision 3 enforces them — verify today's producer covers logical-only refs, not only
  physically-backed ones (the one seam to extend).
- Confirm every RELAX finding carries a **reopen probe** (`SELECT` → zero retires it), including the
  NOCHECK'd logical-only FKs, so a relaxation can never be silently forgotten.

**Engine fixes that retire NEW kinds** (once shipped, the finding stops firing — the healthiest
outcome): M-1 authored defaults, M-8 computed-expr identifiers, decision-2 deployed-NOT-NULL
preservation, decision-4 `''` preservation, B-1 data-lane ordering, decision-9 trigger rewrite,
DF/CK logical names. The board carries them until then.

---

## 5 — The estate sweep (run per environment before trusting green)

Decision 12 needs facts: *are* temporal/PERSISTED/sequences/etc. present in the real estate? These
read-only `sys`-catalog probes answer it per environment (point them at each OSSYS source DB). Each
non-empty result is a board finding; each empty result is a proven negative.

```sql
-- Temporal / system-versioning (→ EmissionTemporalDropped, dealbreaker)
SELECT s.name, t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
WHERE t.temporal_type <> 0;
-- PERSISTED computed columns (→ EmissionPersistedDropped, dealbreaker)
SELECT OBJECT_NAME(object_id), name FROM sys.computed_columns WHERE is_persisted = 1;
-- Sequences (→ EmissionSequenceDropped, dealbreaker)
SELECT name FROM sys.sequences;
-- Computed columns at all (→ EmissionComputedExprIdentifiers if any + CS collation)
SELECT OBJECT_NAME(object_id), name, definition FROM sys.computed_columns;
-- Composite-PK tables that are FK targets (→ EmissionCompositePkFk)
SELECT OBJECT_NAME(fk.referenced_object_id) AS target, COUNT(*) legs
FROM sys.foreign_keys fk JOIN sys.foreign_key_columns c ON c.constraint_object_id=fk.object_id
GROUP BY fk.object_id, fk.referenced_object_id HAVING COUNT(*) > 1;
-- Deployed NOT NULL where the OutSystems model marks the attribute nullable (→ EmissionDeployedNotNullLoosened)
--   (join sys.columns.is_nullable=0 against ossys_Entity_Attr.Is_Mandatory=0)
-- Triggers (→ EmissionTriggerUnrewritten)
SELECT name, OBJECT_NAME(parent_id) FROM sys.triggers WHERE parent_id <> 0;
-- Uniform DATA_COMPRESSION indexes (→ EmissionIndexOptionDropped)
SELECT OBJECT_NAME(i.object_id), i.name, p.data_compression_desc
FROM sys.indexes i JOIN sys.partitions p ON p.object_id=i.object_id AND p.index_id=i.index_id
WHERE p.data_compression_desc <> 'NONE';
-- Target database collation (→ decides whether EmissionComputedExprIdentifiers is a blocker)
SELECT DATABASEPROPERTYEX(DB_NAME(),'Collation');
-- Function-valued DEFAULT constraints (→ EmissionAuthoredDefault exposure; the authored
--   channel is classified, the reflected channel stays un-lifted — matrix row 53)
SELECT OBJECT_NAME(parent_object_id), COL_NAME(parent_object_id, parent_column_id), definition
FROM sys.default_constraints WHERE definition LIKE '%(%' AND definition LIKE '%)%'
  AND definition NOT LIKE '(''%''%' AND definition NOT LIKE '((%';
-- Hand-curated FK names (→ decision 6: synthesized names reset these; inventory first)
SELECT name, OBJECT_NAME(parent_object_id) FROM sys.foreign_keys
WHERE name NOT LIKE 'OSFRK%' AND name NOT LIKE 'FK__%';
-- Mandatory CreatedBy/UpdatedBy user references (→ P-3 re-key exposure, deferred lane)
SELECT OBJECT_NAME(fk.parent_object_id), c.name
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
JOIN sys.columns c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
WHERE c.name IN ('CREATEDBY','UPDATEDBY','CREATED_BY','UPDATED_BY') AND c.is_nullable = 0;
-- Self-referencing and cyclic entity clusters (→ EmissionDataLaneOrder; the load defers
--   enforcement inside a true cycle)
SELECT OBJECT_NAME(parent_object_id) AS child, OBJECT_NAME(referenced_object_id) AS parent
FROM sys.foreign_keys WHERE parent_object_id = referenced_object_id
   OR referenced_object_id IN
      (SELECT parent_object_id FROM sys.foreign_keys f2
       WHERE f2.referenced_object_id = sys.foreign_keys.parent_object_id);
-- Static entities over 1,000 rows (→ the v1 Authoritative-sync loss class, EF-23; v2 is
--   immune, but the row volume scopes the seed lane)
SELECT e.NAME, e.PHYSICAL_TABLE_NAME FROM ossys_Entity e
WHERE e.IS_STATIC = 1 AND e.IS_ACTIVE = 1;  -- join row counts per table when running
-- Non-default temporal scales (→ EF-24: v2 preserves them; v1 widened to 7 — inventory
--   confirms the exposure either way)
SELECT OBJECT_NAME(object_id), name, scale FROM sys.columns
WHERE system_type_id IN (42, 43, 41) AND scale <> 7;  -- datetime2, datetimeoffset, time
-- Text attributes declared over 2,000 (→ the verbatim-length ruling, DECISIONS 2026-07-18:
--   2,001–4,000 now emit bounded where the platform deployed nvarchar(max) — narrowing a
--   deployed MAX column rebuilds it, so prove no row exceeds the declared length first)
SELECT e.NAME, a.NAME, a.LENGTH FROM ossys_Entity_Attr a
JOIN ossys_Entity e ON e.ID = a.ENTITY_ID
WHERE a.TYPE = 'rtText' AND a.LENGTH > 2000 AND e.IS_ACTIVE = 1;
-- ... and for each hit, the overflow probe (zero rows = the narrowing applies without loss):
--   SELECT COUNT(*) FROM <physical_table> WHERE LEN(<physical_column>) > <declared_length>;
```

The audit's `reproduce.sh` and the empirical harness already stand these up on the warm container —
point the sweep at your real Dev/QA/UAT OSSYS sources when ready.

---

## 6 — Build sequence (highest-leverage first)

1. **Wire the dealbreaker detections** (§5 → `Estate.fs`): temporal / PERSISTED / sequences /
   composite-PK-FK-join / deployed-NOT-NULL / trigger-unrewritten / data-lane-order. This makes the
   board *see* every red before any engine fix — the fastest path to "no surprises."
2. **Join detection to emitter refusal** so no dealbreaker is emit-time silent (generalize the
   `EmissionCompositePkFk` pattern).
3. **Wire `DataOrphans` for logical-only FKs** (decision 3) + confirm reopen probes.
4. **Ship the engine fixes** that retire the NEW kinds (M-1, M-8, B-1, decision-2, decision-4,
   decision-9, DF/CK names) — each retires its finding.
5. **Encode the green definition** (§0) as the estate verdict: `Readiness.isReady ∧ no Emission
   dealbreaker ∧ every orphan/null cleared-or-relaxed`.

---

## 7 — What this buys the operator

One surface, `projection check estate`, where **red = a concrete, evidenced reason the cutover would
fail**, each on the right lane: *rule on it* (DECIDE), *clear the data* (REPAIR), *NOCHECK it with a
recorded re-tighten trigger* (RELAX), or *note it* (WATCH). Green means every axis — parity,
emission-correctness, and OutSystems-faithful SQL norms — is satisfied for that environment, and the
`Modules/*.sql` + data lanes will deploy clean and preserve intent + data. No silent losses; every
divergence named; the books balanced before you cut over.
