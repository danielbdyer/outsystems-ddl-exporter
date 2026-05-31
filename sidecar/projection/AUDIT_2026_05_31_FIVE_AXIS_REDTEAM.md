# AUDIT 2026-05-31 — Five-Axis Red-Team: Substantiating the Isomorphism

> **What this is.** The exhaustive, durable record of the six-agent adversarial red-team that tested the
> NORTH_STAR five-axis basis to destruction against the operator's one-command A→B migration. It is the
> *full-fidelity* source; `NORTH_STAR.md` (the iso-ladder + T-V/T-VI + Promise 8), `EXECUTION_PLAN.md`
> (Wave 6), and `DECISIONS 2026-05-31 — Five-axis red-team` are its *projections*. Nothing here is
> compressed away into those surfaces — every finding, file:line, failure scenario, and acceptance
> criterion lives here at full precision. Read this before opening any Wave 6 slice.
>
> **Method.** Six parallel read-only `Explore` agents — one per axis (Schema, Data, Identity, Time,
> Decision) plus an integrator (Composition / dimensional basis). Each was given a *falsifiable* thesis
> and instructed to find where it is **false**, citing file:line. Findings below are the agents' reports,
> reconciled across agents (see "Reconciliation" §0.4) and de-duplicated in the master table (§7).
>
> **Citations caveat.** Line numbers are the agents' citations against the live tree on 2026-05-31; treat
> them as "this symbol, near here," not byte-exact. The *findings* are load-bearing; verify the exact line
> when you open the slice that touches it.

---

## 0. Framing

### 0.1 The falsifiable thesis (three claims under test)

> The five axes (Schema, Data, Identity, Time, Decision) each form a **faithful** round-trip (Project ⊣
> Ingest *isomorphism*, not a lossy retraction), are mutually **orthogonal** (no hidden coupling), and
> together **span** what an operator needs — so a database can move A→B with minimum-viable touches
> (RefactorLog renames + CDC-aware + sink-minted two-phase insert) in ≈one command by composing them.

**Verdict: all three falsified.** Details in §1.

### 0.2 The operator forcing case (why this matters)

The operator's stated future need: *"refresh the schema and the data and find the minimum viable touches to
move a database from point A to point B — RefactorLog for renames, CDC-aware, sink-minted two-phased insert
— nearly one command."* This is the **L3 bullseye** (NORTH_STAR Promise 8): the composition of all five axes
into one act. The red-team's job was to find where that composition breaks.

### 0.3 The isomorphism ladder (the vocabulary this audit establishes)

Each matrix cell has a **rung**, not a checkbox:

| Rung | Meaning | Test |
|---|---|---|
| **L0** | no witness | — |
| **L1** | witness present | a named round-trip test exists + is live (`matrix-status.sh` floor; **5/5 on 2026-05-31**) |
| **L2** | **faithful** | `Ingest ∘ Project = id` modulo an erasure set that is **named and closed** — a `Tolerance` entry, a structured diagnostic, or a fail-loud refusal. **A silent erasure is L1-but-not-L2.** |
| **L3** | **composed** | the axis is **orthogonal** (no hidden coupling) and participates in the green one-command `migrate A B` canary |

The red-team's central result: **the 5/5 matrix is the L1 floor; every axis sits at L1-but-not-L2 with ≥1
silent erasure; no axis is L3.** This audit inventories the L2 gaps (§2), the orthogonality breaks (§3), the
spanning gaps (§4), the composition gap (§5), and the complete acceptance criteria to climb the ladder (§6).

### 0.4 Reconciliation across agents

The **integrator (Composition) agent ran without the recent §5.x session context** and mis-stated three axes
as "unbuilt": it claimed Lifecycle has "no type in Core," Decision is "decided, not emitted," and Identity
"SsKey not persisted." **All three are wrong** and are corrected here against the axis specialists:
- **Lifecycle is built** — `Lifecycle.fs` ships `replayTo` / `evolutionChain` / snapshots (§5.3). The L2 gap
  is that `replayTo` is a *fetch*, not a `fold applyDiff` (Time §2.4), not that it is absent.
- **Decision IS emitted** — `DecisionOverlay` flows through `SsdtDdlEmitter.statementsWith`; nullability
  round-trips (the §V E3 witness shipped 2026-05-31). The L2 gap is uniqueness + FK-trust readback.
- **Identity IS persisted** — `V2.SsKey` extended property + `SsKey.deserialize` recovery (Wave 4.1). The
  L2 gap is the `Synthesized`-variant rename bound, not absence.

The integrator's **structural** findings (no orchestrator, the orthogonality couplings, the missing
dimensions, the adjunction-law analysis) are accurate and load-bearing; only its per-axis "unbuilt" claims
were stale. This audit uses the axis specialists for per-axis state and the integrator for composition.

---

## 1. The verdict

| Claim | Verdict | One-line basis |
|---|---|---|
| **ISO** — each axis round-trips faithfully | ❌ **Falsified** | The proven laws (`AdjunctionLawTests`) are one-directional (T1 determinism, diff-reflexivity, T11 surjectivity, a 2-facet schema round-trip). Faithful-iso is unproven on every axis and *false* (silent erasure) on several. The full Docker-bound adjunction test is `Skip`-ped. |
| **ORTHOGONAL** — axes are independent | ❌ **Falsified** | Pillar-9 (DataIntent ⊥ OperatorIntent) holds, but Axis ⊥ Axis does not: Decision tightening breaks the Data load; Identity rename diverges the coordinates Data matches on. |
| **SPANNING** — the 5 axes cover an A→B migration | ❌ **Falsified** | Three load-bearing dimensions live in no axis: Permissions/Security, Transactionality/Rollback, Connection pre-flight. And no `migrate` orchestrator exists. |

### 1.1 Consolidated per-axis iso-ladder

| Axis | L1 witness | L2 verdict (faithful?) | L3 (composed?) |
|---|---|---|---|
| **Schema** | ✅ `PhysicalSchema diff` | ◑ **retraction** — 6 facets erased silently; `CatalogDiff` kind-level (attribute changes invisible) | ⬚ no `diff→ALTER` |
| **Data** | ✅ `data canary` | ◑ **partial map** — exit-0 on dropped rows; cyclic AssignedBySink silently wrong; empty↔NULL conflated | ⬚ not transactional |
| **Identity** | ✅ `reload preserves SsKey` | ◑ faithful for `OssysOriginal`; `Synthesized`+rename loses identity silently | ⬚ RefactorLog/Transfer unreconciled |
| **Time** | ✅ `replayTo genesis` | ◑ **trivial** — fetch, not `fold applyDiff`; round-trip law unproven | ⬚ no minimal-touch emit |
| **Decision** | ✅ `reproduces the DecisionOverlay` | ◑ iso on **1/3** sub-axes (nullability); uniqueness + FK-trust unread | ⬚ no Decision↔Data pre-flight |

### 1.2 The convergent finding (surfaced independently by Schema, Data-adjacent, and Time)

**`CatalogDiff` is kind-level only** (`CatalogDiff.fs:26-32`, explicitly deferred) — structurally blind to
attribute-level changes (column type, nullability, default, computed). Because "minimum viable touches" *is*
a precise diff, this is the **single deepest gap** for the operator's use case: a column going
`TEXT → NVARCHAR(256)` produces **no diff signal** → no ALTER → silent full-redeploy with possible type
coercion on existing data. Closed by **Wave 6.A.10** (the critical-path keystone).

### 1.3 The one genuinely-solid result

The **CDC-silence-on-idempotent-redeploy** property (CLAUDE.md's "highest-leverage single deliverable") is
*genuinely shipped and witnessed* at the **data** level: `CdcSilenceTests` (line ~235-267) asserts an
idempotent redeploy adds **zero** CDC capture rows via the `WHEN MATCHED AND <col-diff>` change-detection
predicate; `CdcSilenceCrossEmitterTests` proves the composer threads it across StaticSeeds + the Phase-2
deferred-FK UPDATE. The transfer CDC pre-flight gate (`transfer.cdcTrackedSink`, `TransferRun.fs:262-278`)
is real. **The data leg of "CDC-aware" is a solid foundation.** The gap is schema-level CDC-silence (§2.5).

---

## 2. Axis-by-axis findings (the exhaustive per-agent detail)

### 2.1 Schema — emit∘read is a retraction, not an isomorphism

**What `PhysicalSchema.diff` actually compares** (`PhysicalSchema.fs:38-49`): `Columns` (schema, table,
column, type, nullable, PK, length, precision, scale, identity-flag, default, computed), `ForeignKeys`
(src→tgt coordinate tuples), `Rows` (per-row SHA256, small tables), `RowDigests` (aggregate hash + count,
large tables), `LogicalNameBindings` (V2.LogicalName), `Annotations` (triggers, checks, sequences,
extended properties). **Explicitly OUT** (lines 44-46): non-PK indexes (line 45), comment/Description
metadata, Module structure / Origin / Modality marks, and **SsKey identity** (the diff is deliberately
identity-blind so two adapters can be compared without false negatives).

**The 6-feature hollow table** (the Wave-1 un-hollowing surface):

| Feature | In IR? | Emitted? | Read back? | Round-trips? | Cite |
|---|---|---|---|---|---|
| Triggers | ✅ `Kind.Triggers` (Catalog.fs:843) | ✅ `emitTriggers` (SsdtDdlEmitter.fs:456-470) | ✅ `readTriggers` (ReadSide.fs:311-337) | ✅ (body normalized; see seam) | Annotations axis |
| CHECK constraints | ✅ `Kind.ColumnChecks` (Catalog.fs:852) | ✅ `emitTableChecks` (SsdtDdlEmitter.fs:300-307) | ✅ `readCheckConstraints` (ReadSide.fs:342-367) | ⚠️ definition yes; **NOCHECK trust flag NOT emitted** | Catalog.fs:122-127 carries `isNotTrusted` |
| Sequences | ✅ `Catalog.Sequences` (Catalog.fs:899) | ✅ `emitSequences` (SsdtDdlEmitter.fs:471-479) | ✅ `readSequences` (ReadSide.fs:372-398) | ✅ full shape | Annotations axis |
| DEFAULT constraints | ✅ `Attribute.DefaultValue` (Catalog.fs:481) | ✅ `columnDefineClause` (SsdtDdlEmitter.fs:130-150) | ✅ `readDefaultConstraints` (ReadSide.fs:246-271) | ✅ (slice 1.2 test) | `PhysicalColumn.Default` |
| Computed columns | ✅ `Attribute.Computed` (Catalog.fs:498) | ✅ (SsdtDdlEmitter.fs:130-158) | ✅ `readComputedColumns` (ReadSide.fs:280-305) | ✅ (slice 1.3 test) | `PhysicalColumn.Computed` |
| Extended properties (user) | ✅ `Kind/Attribute.ExtendedProperties` (Catalog.fs:858/503) | ❌ **emitter drops** (Render/SsdtDdlEmitter ~line 914 "emission lands when a consumer demands it") | ◑ `readExtendedProperties` (ReadSide.fs:405-436) excludes V2.LogicalName, reads others | ❌ **emit∘read ≠ id** | the retraction proof |

**The retraction proof (counterexample).** Deploy source A with a custom extended property
(`MS_Description="My table"`) → `ReadSide` populates IR → V2 emitter **does not emit** it (SsdtDdlEmitter
~914) → deployed B has zero extended properties → `read(emit(read(A))) ≠ read(A)` on the ext-props axis.
**`emit∘read` is not identity.** Severity MEDIUM-HIGH (governance/lineage metadata vanishes).

**Determinism (T1): clean.** Row hashing (`PhysicalSchema.fs:523-541`) sorts by column name before hashing
(`Array.sortBy`), values pre-stringified (no float/datetime), `SHA256.HashData` deterministic, UTF-8 stable.
`normalizeDefault` (361-378) strips matched outer parens idempotently; `encodeComputed` appends
`|persisted`. `PhysicalSchema.ofCatalog` is byte-deterministic. ✅

**CatalogDiff coverage (the A→B blind spot).** `CatalogDiff` (`CatalogDiff.fs:18-162`) is **kind-level
only**: `Renamed` / `Added` / `Removed` / `Unchanged` over `Catalog.allKinds` SsKeys. Attribute-level
changes (type, nullability, default, computed, check, trigger, sequence, ext-prop) are **invisible**
(lines 26-32, "attribute-level renames defer to a follow-on slice"). PhysicalSchema *is* attribute-aware;
CatalogDiff is *not* — they cover different facets. The operator computing a migration delta via CatalogDiff
sees only kind-level changes; column-level evolution is silent.

**Schema severity-ranked findings:**
1. **CRITICAL — user extended properties: no round-trip** (emitter drops; `emit∘read ≠ id`). No mitigation in shipped code.
2. **CRITICAL — CatalogDiff attribute-level blind** (A→B migration blind spot). Deferred.
3. **HIGH — CHECK NOCHECK trust flag not emitted** (Catalog.fs:122-127 carries `isNotTrusted`; emitter at 300-307 doesn't thread it). A NOCHECK'd source CHECK re-emits as TRUSTED → deploy/validation can fail on violating data.
4. **HIGH — FK constraint trust state not recovered** (ReadSide builds `Reference` with default `IsConstraintTrusted=true`; a deployed `WITH NOCHECK` FK reads back trusted).
5. **HIGH — trigger body + external refs** (normalized for comparison; semantics may diverge if the trigger references external procs/tables resolved differently after deploy).
6. **MEDIUM — identity seed/increment** (emitter hardcodes `IDENTITY(1,1)`; source `IDENTITY(100,5)` is lost; ReadSide recovers only the bool flag).

### 2.2 Data — a partial map with a silent drop-set

**The drop-set (CRITICAL — exit code does not reflect data loss).** FK re-point misses are skip-and-diagnose:
`SurrogateRemap.remapRowFks` (`SurrogateRemap.fs:202-230`) accumulates a miss into `Error { Column; Target;
UnresolvedSource }`; `DataLoadPlan.build` (`DataLoadPlan.fs:82-133`) pairs each dropped row with its kind into
`skipped`; `TransferRun.writePlan` (`TransferRun.fs:141-197`) merges plan-build + Phase-2 misses into
`writeSkips`. The report carries them (`TransferReport.SkippedReferences`). **But the CLI exits 0 on a
successful write regardless** — `Program.fs runTransfer` returns `0` on `Ok report` even when
`SkippedReferences` is non-empty; the skip table prints to stdout (and can scroll past on a 300-table run). A
one-command A→B refresh script sees "complete / exit 0" while rows silently vanish. **Closed by Wave 6.A.1.**

**Cyclic AssignedBySink (HIGH — silently wrong data).** `TransferRun.writePlan` Phase-1 (lines 168-182)
captures `(sourcePK → assignedPK)` via `insertCaptureRow`; on a duplicate `capture` returns `Error` which is
**silently swallowed** (`| Error _ -> ()`). Phase-2 (lines 186-194) re-keys deferred FKs via `phase2UpdateSql`,
whose WHERE clause keys on the kind's PK using the **plan-side (source) values**. For a self-referential
IDENTITY kind: Phase-1 inserts with sink-minted PKs (1,2,3); Phase-2's `WHERE id = 280` (source value) matches
**nothing** in the sink (which has 1,2,3) → silent no-op → the self-FK stays unresolved/stale. Characterization:
(a) silently wrong data — YES; (b) fail loud — NO; (c) skip — the row is written but its FK is wrong. **Closed
by Wave 6.A.2.**

**Composite identity (MEDIUM — single-column bottleneck).** `insertCaptureRow` (`TransferRun.fs:101-130`)
captures exactly one `IsPrimaryKey && IsIdentity` column via `OUTPUT inserted.<col>` (a scalar). `SourceKey` /
`AssignedKey` are `… of string` (single value). A composite PK with one+ IDENTITY columns has only the **first**
captured; the remap is incomplete; a composite FK referencing it gets one leg re-pointed, the other stale.
**Closed by Wave 6.A.3.**

**Empty-string ↔ NULL conflation (MEDIUM).** `toCellRows` (`TransferRun.fs:64-72`) maps deferred FK columns
AND missing columns to `""`; `Bulk.parseRaw` (`Bulk.fs:45-67`) maps `"" → DBNull`. Three meanings collapse onto
`""`: (1) deferred FK signal, (2) missing-column default, (3) genuine empty-string data. A source Text column
with a real empty string round-trips as NULL; the canary's per-row hash drift then *fails* (so it's caught for
small tables, but the semantic is wrong). **Closed by Wave 6.A.4.**

**Determinism + ordering (MEDIUM).** The canary asserts set-level row equality: per-row SHA256 for small tables
(catches content drift), aggregate count+hash digest for large tables. It does **not** detect row permutation
(commutative sum), and the large-table Digest could in principle pass on a coincidental count+hash collision
after a drop. For small tables, every row's content is verified.

**The 5 A→B breakpoints (the realistic failure walk):**
1. **Silent drop on unmatched reconciliation** (high likelihood) — a `ReconciledByRule` source surrogate with no sink counterpart drops every referencing row; exit 0.
2. **Composite FK / composite identity mismatch** (medium) — Phase-2 WHERE fails to match; FK stale.
3. **Empty-string↔NULL** (medium) — canary fails; manual review.
4. **Unbreakable cycle FK** (low; the **only** true fail-loud path) — `DataLoadPlan.isSatisfiable=false` → `transfer.unbreakableCycleFk`, exit non-zero.
5. **AssignedBySink duplicate capture** (low) — second capture silently ignored; later referencers of that key drop.

**Data severity table:**

| Rank | Finding | Severity | Failure mode |
|---|---|---|---|
| 1 | Exit code doesn't reflect data loss | CRITICAL | rows dropped, exit 0, script continues |
| 2 | Unmatched reconciliation rows silent | CRITICAL | FK-orphan rows vanish |
| 3 | Composite identity re-point incomplete | HIGH | stale FK on multi-col surrogate |
| 4 | Empty-string ↔ NULL conflation | HIGH | "no name" becomes "unknown" |
| 5 | Cyclic AssignedBySink unresolved | MEDIUM | manager/tree hierarchies malformed |
| 6 | Row permutation not detected | MEDIUM | order-dependent logic breaks |
| 7 | Unbreakable cycle is fail-loud | LOW (good) | controlled refusal |

### 2.3 Identity — faithful only on a sub-domain

**The 4-variant codec round-trips faithfully** (`Identity.fs:157-207`): `serialize`/`deserialize` cover
`OssysOriginal` (`O`), `Synthesized` (`S`, with basis-parts count + parts), `DerivedFrom` (`D`, recursive),
`V1Mapped` (`V`). `SsKeyTests.fs:235-262` confirm all four round-trip (incl. `DerivedFrom` nested in
`Synthesized`). ✅ **But the NORTH_STAR Identity witness exercises `OssysOriginal` ONLY** — no test exercises
`Synthesized`/`DerivedFrom` round-tripping *under rename*.

**The two-identity-system seam (CRITICAL).** Mechanism A = `SsKey` (logical identity; persisted in `V2.SsKey`
ext-prop; rename-stable per A1; lookup via `Catalog.tryFindKind` by SsKey). Mechanism B = physical table name
(`Kind.Physical : TableId`; when `V2.SsKey` is absent, ReadSide synthesizes `kindSsKey schema table`). **These
are not synchronized under rename.** Failure case (first-import + rename in one A→B op):
1. `ReadSide.read A` → `Kind.SsKey = Synthesized("READSIDE_KIND","dbo.OLD")` (no V2.SsKey on a non-V2 source).
2. `CatalogDiff.between` detects the name change; RefactorLog emits `sp_rename OLD→NEW` (rename works).
3. `Transfer` does `Ingestion.streamKind sink k` (`TransferRun.fs:224`) with the **source** Kind `k`.
4. Sink B has `dbo.NEW` → ReadSide synthesizes `Synthesized("READSIDE_KIND","dbo.NEW")` — **a different SsKey**.
5. Lookup fails; ingestion returns empty / wrong kind → **data lost or mis-mapped**.
Per the A1 amendment (`Identity.fs:3-10`): "A1 remains **bounded** for [the Synthesized] variant — a
source-side rename produces a different `SsKey`." The implementation is correct *per the axiom*; the **witness
is insufficient** and the **operator scenario (non-V2 source) is exactly the bounded case.**

**RefactorLog vs Transfer are mutually-exclusive strategies (HIGH).** RefactorLog (`RefactorLogEmitter.fs:5-11`)
produces the `.refactorlog` XML for DacFx's incremental planner — a **same-DB** `sp_rename`, data preserved
in-place. Transfer (`Transfer.fs:1-21`) is a **cross-DB** data move. **No code in Transfer consumes or generates
RefactorLog** — they are orthogonal consumers of `CatalogDiff`. The operator's "migrate + rename in one op" has
no defined composition: rename is either lost (Transfer path) or meaningless (RefactorLog applied to
already-moved data). **Closed by Wave 6.B.2 (wire) + 6.A.7 (Synthesized bound).**

**Identity severity-ranked:**
1. **CRITICAL — SsKey mismatch on rename under Transfer** (Synthesized keys; first-import). Fix options: re-sync SsKeys from sink on read; apply RefactorLog renames to source before ingest; or persist V2.SsKey on first import.
2. **HIGH — RefactorLog/Transfer composition undefined.**
3. **MEDIUM — witness insufficient** (tests `OssysOriginal` only; no `Synthesized`-under-rename test).

### 2.4 Time — a snapshot store, not an evolution algebra

**`replayTo` is a pure lookup (trivial).** `Lifecycle.fs:127-137`: `replayTo version` does
`data.Snapshots |> List.tryFind (fun s -> Version.ordinal s.Version = target)` and returns the **stored**
`Catalog`. The docstring (122-126) is honest: "the diff-replay reconstruction form (`fold (applyDiff) C₀`)
lands when `CatalogDiff` gains an `apply` peer to `between` (H-007)." **H-007 is deferred; `applyDiff` does not
exist.** The Time "iso" is store-and-fetch, not an evolution algebra.

**Diffs do not compose; no round-trip law.** `evolutionChain` (`Lifecycle.fs:110-120`) folds
`CatalogDiff.between` over snapshot pairs. But `between : Catalog → Catalog → CatalogDiff` does **not** take a
prior diff; there is **no `applyDiff : Catalog → CatalogDiff → Catalog`** (grep: zero matches); there is **no
test** of `applyDiff (between A B) A = B`. The diffs are *descriptions*, not *executable transformations*.
**Closed by Wave 6.A.11.**

**Minimum viable touches: NOT supported.** `SsdtDdlEmitter.emitSlices` (`SsdtDdlEmitter.fs:798-824`) walks
`allKinds` → `kindToSsdtFile` per kind, emitting **full CREATE TABLE** (line 70: "Rendered SQL body — CREATE
TABLE statement"). **No diff input; no ALTER path.** `grep ALTER` finds only NOCHECK/index/FK contexts, not a
delta-emit primitive. `RefactorLogEmitter` handles **renames only** (DROP+ADD → `sp_rename`), not type/
nullability/default ALTERs. `MigrationDependenciesEmitter.emitFromPlan` (345-369) emits **data** MERGE/UPDATE
scripts, orthogonal to schema DDL. **The engine redeploys whole tables and relies on DacFx for idempotence —
*tool*-level, not *engine*-level.** The operator's "minimal SQL to move A→B" cannot be answered: it can't compute
the delta (CatalogDiff is kind-level) and can't emit minimal ALTERs. **Closed by Wave 6.A.10 + 6.A.12.**

**CDC-silence is witnessed for DATA, silent on SCHEMA.** `CdcSilenceTests.fs:235-246` asserts
`Assert.Equal(baseline, post)` — zero new CDC rows on idempotent static-seed redeploy (the `WHEN MATCHED AND
<col-diff>` predicate). `CdcSilenceCrossEmitterTests` proves the cross-emitter thread. **But this is rows-within-
a-table**, not schema-version idempotence. A full CREATE-TABLE redeploy of an unchanged schema fires DDL change
events regardless; schema-level silence is by *deployment-tool convention*, not engine design. **Closed by
Wave 6.A.13.**

**Attribute-level blindness (confirmed from the Time view).** Same as Schema §2.1: `CatalogDiff` kind-level
(`CatalogDiff.fs:25-32`; prescope `CHAPTER_3_PRESCOPE_REFACTORLOG_AND_CATALOG_DIFF.md §2.1` acknowledges +
defers). A version where `Email TEXT → Email NVARCHAR(256) DEFAULT …` snapshots both faithfully but
`between` reports `Unchanged` → no diff entry → no ALTER → full redeploy with silent type coercion risk.

**Time severity tiers:**
- **TIER 1 (blocks A→B):** (1) no ALTER delta emission — CRITICAL; (2) attribute-level changes invisible — HIGH; (3) `applyDiff`/H-007 unshipped — MEDIUM (blocks the verifiable evolution algebra).
- **TIER 2 (real wins, already shipped):** (4) data-level CDC silence — mitigated; (5) refactor-log renames — mitigated (kind-level); (6) replayability for audit — nice-to-have.

### 2.5 Decision — iso on 1 of 3 sub-axes

**The three tightening sub-axes:**

| Decision | Emitted via | Read back? | Round-trips? | Status |
|---|---|---|---|---|
| **EnforceNotNull** | `Nullable = a.Column.IsNullable && not enforceNotNull` (SsdtDdlEmitter.fs:129) | ✅ `IS_NULLABLE` via INFORMATION_SCHEMA (ReadSide ~514-522) | ✅ | **witnessed** (the §V E3 nullability witness) |
| **EnforceUnique** | `idx.IsUnique \|\| Set.contains idx.SsKey overlay.EnforceUnique` (SsdtDdlEmitter.fs:416) | ❌ `Indexes = []` hardcoded "for M3 MVP" (ReadSide.fs:877) | ❌ | **BROKEN — unwitnessed** |
| **DropFk / NoCheckFk** | DropFk suppresses inline FK (line 298); NoCheckFk emits `ALTER … NOCHECK` (line 344) | ◑ FK existence read (ReadSide.fs:443-488) but **`IsConstraintTrusted` defaults true** (Reference.create ~933-937) | ❌ | **PARTIAL — trust state lost** |

**Additive-only invariant — true for nullability, semantic for FK.** Nullability (line 129) is genuinely
additive (`a.Column.IsNullable && not enforce` never loosens). DropFk (line 298 filters the reference out) is
structurally a **removal**, applied **silently** at emission — no warning, no audit, no gate. Dropping an FK the
source enforced is a safety change that should surface. **Closed by Wave 6.A.9.** Uniqueness additive-only is
**untested** (no readback).

**CDC-awareness — two halves.** (1) Transfer pre-flight refusal: IMPLEMENTED (`TransferRun.fs:262-278`, refuses
Execute against CDC-tracked sinks via `ReadSide.cdcTrackedTables`, `--allow-cdc` overrides). (2) Schema-side
CDC-silence: IMPLEMENTED + WITNESSED at the data level (`CdcSilenceTests` + `CdcSilenceCrossEmitterTests`).
**Gaps:** the CDC pre-flight guards **transfer only**, not the schema-emit (`Compose`) path — an
`osm emit --execute` against a CDC-tracked schema has no pre-flight refusal (structurally safe via the
predicate, but no operator-facing gate). And the **schema-version** idempotence (don't emit an ALTER on an
unchanged schema) does not exist (Time §2.4). CdcAwareness lives in `Profile.CdcAwareness` (Profile.fs ~677-710:
`enabled: Set<SsKey>` + `instances: Map<SsKey,string>`).

**Where decisions come from (incomplete for A→B).** Decisions are produced by the pass chain (NullabilityPass
consults `ColumnProfile.nullPercentage`; UniqueIndexPass consults composite-uniqueness probes) →
`DecisionOverlay.ofComposeState`. For a real A→B migration the operator must supply (a) a `Policy` with
`Tightening.Interventions`, and (b) a `Profile` if nullability/uniqueness decisions are desired — **but the
A→B/transfer path defaults to `Profile.empty`** (`Pipeline.fs:379-380`); `projectWith` exists but isn't called
from `TransferRun` or the main emit path. **The "decisions from live source evidence" narrative is aspirational
end-to-end.** A `LiveProfiler → Profile → decisions` wiring for A→B is needed. **Closed by Wave 6.B.1 (the
pre-flight consumes the probe).**

**Decision severity tiers:**
- **TIER 1 (round-trip credibility):** uniqueness unwitnessed (`Indexes=[]`); FK-trust silently lost. **Closed by 6.A.5 + 6.A.8.**
- **TIER 2 (CDC operational):** transfer gate exists, schema-emit gate missing; Profile integration incomplete for A→B.
- **TIER 3 (visibility):** DropFk has no audit trail (6.A.9); CDC-aware MERGE not surfaced in the manifest.

---

## 3. Orthogonality (T-V) — the couplings

| # | Coupling | Severity | Detail |
|---|---|---|---|
| O1 | **Decision (tightening) → Data (transfer)** | HIGH | A `Decision` NOT-NULL/UNIQUE on a column whose source rows violate it makes the two-phase insert **fail mid-load**. `Transfer.fs` has zero schema-data compatibility checks; the operator must understand the overlay before running. The canary uses pre-generated *compatible* fixtures, so this is **untested**. **Closed by 6.B.1.** |
| O2 | **Identity (rename) → Schema → Data** | HIGH | A rename changes physical coordinates; the Sink schema (read back) has new names, the Source (read back) has old names. SsKey matching works (A1), but Transfer must then project **old physical columns → new physical columns** with **no RefactorLog consumed** (`Transfer.fs` has zero rename references; `applyRenames` is a pre-project pass at `Pipeline.fs:647-669`). Fragile fallback = match by type+ordinal+nullable → silent column mis-map. **Closed by 6.B.2.** |
| O3 | **Time (Lifecycle) → Schema** | MEDIUM | Lifecycle is built but **disconnected** from minimal-emission: RefactorLog is computed at project-time; Transfer can't consume it; multi-version replay (A→B→C) can't be built until `applyDiff` lands. Orthogonal-but-unintegrated, so it doesn't *break* others; it doesn't *compose* either. (Integrator's "unbuilt" framing corrected per §0.4.) |
| O4 | **Profile (evidence) → Data (transfer)** | MEDIUM | `SurrogateRemap`/reconciliation is keyed on **column presence/absence** (implicit structural coupling), not an explicit pre-computed directive. If Decision adds a synthetic identity column, Transfer must *infer* reconciliation from schema structure. Implicit, not explicit-semantic. |

---

## 4. Spanning (T-VI) — the missing dimensions

| # | Missing dimension | Severity | Detail + close |
|---|---|---|---|
| S1 | **Permissions / Security** | CRITICAL | Zero coverage. `Transfer.fs` has no grant axis; `Ingestion` doesn't check source SELECT; `Deploy` doesn't check CREATE/ownership; `Profile` has no security evidence; `Config` (D9) forbids credential plaintext but carries no permission *model*. `transfer --sink-conn PROD` can read DEV (SELECT) but **silently write zero rows** to PROD (INSERT denied) → "rows written = 0", no structured error. A production A→B cannot proceed without verifying the security boundary. **Closed by 6.C.1.** |
| S2 | **Transactionality / Rollback / Recovery** | CRITICAL | A→B is **not atomic**. `Deploy.executeStream` runs DDL in one GO-batch (ACID per-batch), but `TransferRun` Execute writes via SqlBulkCopy with **no transaction boundary, no commit/rollback, no checkpoint**. A mid-transfer failure leaves the Sink **half-populated** (N rows in, M pending, unknown). Retry **duplicates** (no upsert). No rollback DDL. The operator cannot safely retry. **Closed by 6.C.2.** |
| S3 | **Connection / Config / Secrets pre-flight** | HIGH | `TransferArgs` parses `--source-conn`/`--sink-conn`; `ConnectionResolver` (D9) resolves env/file. But **no connection pooling, retry, timeout, encryption validation, no round-trip liveness test** before the transfer starts. A misconfigured env var or network blip mid-transfer silently corrupts the Sink. **Closed by 6.C.1.** |
| S4 | **Cross-database FK ordering** | MEDIUM | `CatalogDiff`/`SsKey` are `schema.table`-scoped (single DB); `Transfer` has no cross-DB FK concept; `ReadSide.read` reconstructs one DB. Multi-DB A→B (DACPAC) requires manual ordering. Single-DB (OutSystems common case) unaffected. **Deferred — 6.C.3 (rides Wave 4.3).** |

---

## 5. The composition + the adjunction laws

**No single A→B orchestrator exists.** The CLI (`Program.fs`) offers `full-export`, `emit`, `deploy`, `canary`,
`transfer`, `verify-data`, `policy-diff`, `approve` — **separate verbs**. The operator's A→B requires the
five-stage order (i) diff → (ii) rename → (iii) deploy → (iv) transfer → (v) verify, manually sequenced via a
Bash/ADO pipeline. **The rename↔transfer seam:** renames are applied at **project time** (`Pipeline.fs:647-669
applyRenames`, before emitters), and **Transfer never sees them** (`Transfer.fs` / `Program.fs runTransfer` have
zero RefactorLog references). "Nearly one command" is today **five commands with a seam in the middle.** **Closed
by Wave 6.D.1.**

**The adjunction laws — proven as an *adjunction*, not an *equivalence*.** `AdjunctionLawTests.fs` (H-050) proves:
- ✅ **T1 emitter determinism** (line ~75: same Catalog → byte-identical statements; permutation-invariant ~81).
- ✅ **CatalogDiff reflexivity** (`between c c` = all Unchanged, ~97; permutation-invariant ~103).
- ✅ **T11 surjectivity** (every kind → a CreateTable, ~127; ~137).
- ✅ **In-process round-trip on TWO facets** (`ofCatalog = ofStatementStream` on Columns + ForeignKeys; `PhysicalSchema.diff` empty, ~160-198).
- ✅ **DecisionOverlay byte-identity** (`statementsWith DecisionOverlay.empty = statements`, ~228-247).
- ✗ **Full Docker-bound adjunction: `Skip`-ped** (~211-219; "in-process covers Columns+ForeignKeys; Docker-bound adds CHECK re-parsing, default re-rendering, computed types; requires coverage-guided fixtures or N≥20 containers").

**So the adjunction holds one-directionally on a 2-facet sub-domain; the faithful-iso (equivalence) is unproven
on the other facets and false (silent erasure) on several.** An adjunction with a non-faithful right adjoint is
still an adjunction — but **not an equivalence**, and the docs (pre-this-audit) read the witness-presence as if
it were equivalence. That conflation is the meta-finding this audit corrects (NORTH_STAR L1 vs L2).

**Is the basis advantaged in dimensional space?**
- **Where the 5-axis framing HELPS (real leverage):** semantic clarity (naming an axis makes "what's missing"
  legible — the matrix is a visual forcing function); compositional structure (nine capabilities as corollaries
  of one law — genuine intellectual economy); test organization (pillar-9 skeleton/overlay).
- **Where it is ASPIRATIONAL (seams, not orthogonality):** the inter-axis seams are **real coupling** (§3), not
  dimensional independence — pillar-9 (DataIntent ⊥ OperatorIntent) is true, but Axis ⊥ Axis is not; the
  round-trip fidelity promised is partial on every axis (§2); the missing dimensions (§4) are **foundational
  constraints every axis depends on**, not optional extras.

**Conclusion:** the basis is a *good map* (advantaged for reasoning + forcing) but **not yet a faithful,
orthogonal, spanning basis** — it portrays witnesses as isomorphisms. The Wave 6 climb is what makes the map
the territory.

---

## 6. The complete acceptance-criteria catalog (every witness, every rung)

Each row is a buildable slice (full spec in `EXECUTION_PLAN.md` Wave 6). The **witness** is the named test the
matrix generator can see; **rung** is the iso-ladder level it raises; **T** is the totality it closes.

| Slice | Witness test (acceptance) | Raises | Closes | Dep | Size |
|---|---|---|---|---|---|
| **6.A.1** | `data canary: transfer with an unmatched FK exits non-zero (drop is fail-loud, not exit-0)` | Data L2 | T-I | — | S |
| **6.A.2** | `data canary: cyclic AssignedBySink is refused, not silently mis-keyed` | Data L2 | T-I | — | S→M |
| **6.A.3** | `data canary: composite-IDENTITY AssignedBySink is refused, not half-captured` | Data L2 | T-I | — | S |
| **6.A.4** | `data canary: empty-string Text round-trips faithfully (or names the tolerance)` | Data L2 | T-I | — | M |
| **6.A.5** | `schema round-trip: a UNIQUE index + a NOCHECK FK survive emit/deploy/ReadSide` | Schema+Decision L2 | T-I | — | M |
| **6.A.6** | schema-canary erasure set fully enumerated in `Tolerance`+`AxiomTests` (no silent facet) | Schema L2 | T-I | — | M |
| **6.A.7** | `A1: a Synthesized-key rename is surfaced, not silently re-keyed` | Identity L2 | T-I | — | M |
| **6.A.8** | `decision adjunction: read-back reproduces EnforceUnique and DropFk` (1/3 → 3/3) | Decision L2 | T-I | 6.A.5 | M |
| **6.A.9** | `every DropFk decision surfaces a Warning diagnostic` | Decision L2 | T-I | — | S |
| **6.A.10** | `CatalogDiff: a column type change surfaces as an attribute-level Changed entry` | Schema+Time L2 | T-I | — | M |
| **6.A.11** | `Time: applyDiff (between A B) A = B (evolution round-trip law)` | Time L2 | T-I/T-III | 6.A.10 | M |
| **6.A.12** | `migration: a column type change emits an ALTER, not a CREATE` | Time L3-precursor | T-I | 6.A.10 | L |
| **6.A.13** | `CDC-silence: redeploying an unchanged schema emits zero DDL (engine-level)` | Time L2 | T-I | 6.A.12 | M |
| **6.B.1** | `migrate pre-flight: EnforceNotNull on a NULL-bearing column refuses before writing` | orthogonality | T-V | — | M |
| **6.B.2** | `transfer: a renamed column is re-pointed by the rename map, not matched by ordinal` | orthogonality | T-V | 6.A.10 | M |
| **6.C.1** | `migrate pre-flight: a write-denied sink refuses before transferring, not silently zero rows` | spanning | T-VI | — | M |
| **6.C.2** | `transfer: an injected mid-load failure leaves the target unchanged (atomic) or resumable` | spanning | T-VI | — | L |
| **6.C.3** | (deferred — cross-DB FK; trigger: a real multi-DB source) | spanning | T-VI | 4.3 | — |
| **6.D.1** | `migrate A B: one command moves A→B with minimum viable touches; B reproduces A modulo the declared changes (atomic-or-resumable, fail-loud on violation)` | **L3 bullseye** | T-V/T-VI | 6.A.10, 6.A.12, 6.B.*, 6.C.1/.2 | M* |
| **6.E.1** | `NORTH_STAR.matrix.generated.md` shows a per-axis L1/L2/L3 rung; a new silent erasure drops a cell L2→L1 | meta | T-IV | — | M |

**Critical path to Promise 8:** `6.A.10 → 6.A.12 → {6.B.1, 6.B.2, 6.C.1, 6.C.2} → 6.D.1`. The per-axis L2
faithfulness slices (6.A.*) land in parallel as confidence-builders and matrix-rung raisers; **6.A.1 and 6.A.9
are the immediate quick wins**; **6.A.10 (attribute-level diff) is the structural keystone** on which the entire
A→B story rests.

---

## 7. Master severity-ranked findings (de-duplicated across all six agents)

| # | Finding | Axis(es) | Severity | Slice that closes it |
|---|---|---|---|---|
| 1 | `transfer` exits 0 while dropping rows | Data | CRITICAL | 6.A.1 |
| 2 | `CatalogDiff` kind-level → attribute changes invisible; no `diff→ALTER` | Schema, Time | CRITICAL | 6.A.10, 6.A.12 |
| 3 | No `migrate` orchestrator; rename never reaches Transfer | Composition, Identity | CRITICAL | 6.D.1, 6.B.2 |
| 4 | Transfer not transactional/resumable (half-populated target) | Spanning | CRITICAL | 6.C.2 |
| 5 | No permissions/connection pre-flight (write-denied sink → silent 0 rows) | Spanning | CRITICAL | 6.C.1 |
| 6 | SsKey mismatch on `Synthesized`-key rename under Transfer | Identity | CRITICAL | 6.A.7, 6.B.2 |
| 7 | Decision↔Data coupling (tightening breaks the data load) | Orthogonality | HIGH | 6.B.1 |
| 8 | Uniqueness decision unwitnessed (`Indexes=[]`) | Decision, Schema | HIGH | 6.A.5, 6.A.8 |
| 9 | FK-trust state silently lost on readback | Decision, Schema | HIGH | 6.A.5 |
| 10 | User extended properties: no round-trip (emitter drops) | Schema | HIGH | 6.A.6 |
| 11 | CHECK NOCHECK trust flag not emitted | Schema | HIGH | 6.A.6 |
| 12 | Cyclic AssignedBySink silently writes wrong FKs | Data | HIGH→MED | 6.A.2 |
| 13 | Empty-string ↔ NULL conflation | Data | HIGH→MED | 6.A.4 |
| 14 | Composite IDENTITY captured one leg | Data | MEDIUM | 6.A.3 |
| 15 | `replayTo` is a fetch; `applyDiff`/round-trip-law unshipped | Time | MEDIUM | 6.A.11 |
| 16 | Schema CDC-silence missing (engine-level vs DacFx) | Time, Decision | MEDIUM | 6.A.13 |
| 17 | DropFk applied with no audit trail | Decision | MEDIUM | 6.A.9 |
| 18 | Identity seed/increment hardcoded `(1,1)` | Schema | MEDIUM | 6.A.6 |
| 19 | Trigger body external-ref semantics may diverge | Schema | MEDIUM | 6.A.6 (tolerance) |
| 20 | Identity witness tests `OssysOriginal` only | Identity | MEDIUM | 6.A.7 |
| 21 | Cross-DB FK ordering unsupported | Spanning | MEDIUM | 6.C.3 (deferred) |
| 22 | CDC-aware MERGE not surfaced in the manifest | Decision | LOW | 6.A.9-adjacent |
| 23 | Row permutation not detected by the data canary | Data | LOW | (accept; document) |

---

## 8. How to use this audit

- **Open a Wave 6 slice?** Read its §2/§3/§4 finding here first (the *why*), then the §6 acceptance row (the
  *witness*), then `EXECUTION_PLAN.md` Wave 6 (the *spec*). The three are one chain.
- **Close a finding?** The slice's acceptance witness is the bar; on green, the matrix generator (6.E.1) raises
  the cell's rung. A finding is not "done" until its witness exists in the tree *and* its silent erasure is a
  named tolerance / diagnostic / refusal.
- **Re-audit cadence:** this is the L2/L3 baseline. When the master table (§7) is all-closed and the `migrate`
  canary (6.D.1) is green, the isomorphism is *substantiated* — the matrix is L3, NORTH_STAR Promise 8 holds,
  and a fresh red-team should re-run against the then-current claims.

— Recorded for the receiving agent. (Five-axis red-team; the full record. Its projections are NORTH_STAR §1/§3/§4/§5, EXECUTION_PLAN Wave 6, and DECISIONS 2026-05-31.)
