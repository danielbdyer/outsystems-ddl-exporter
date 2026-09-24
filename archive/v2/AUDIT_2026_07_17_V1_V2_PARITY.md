# AUDIT_2026_07_17 — V1 full-export ↔ V2 publish parity (the switchover audit)

> **Produced 2026-07-17** against the tree at `ea21b13` (post-PR #668/#669/#670/#671).
> Audience: the manager and dev leads deciding whether V2's `publish` can replace V1's
> `full-export` for the OutSystems→SQL Server external-entity cutover. Scope: the **v2 publish
> (bundle emission)** command vs the **v1 full-export** command, on an **identical
> ossys/metamodel/OutSystems-cloud target**.
>
> This is a *mega audit*: functional parity (does the DDL + data produce the same **loaded
> database**), emission/aesthetic parity (bytes, formatting, layout), plus determinism,
> interop, and operational surface. Every headline finding is either **empirically proven**
> (both engines run against one live estate and the resulting databases diffed) or
> **personally code-verified** at `file:line`. Static-analysis-only findings are labelled as
> such. **When this document disagrees with the code or `DECISIONS.md`, they win.**

---

## 0 — How this audit was run (so you can reproduce it)

Both engines were built and run against **one shared live estate** so that every output
difference is *pipeline behaviour*, not input skew.

- **Shared estate.** Database `ProjectionEstate`/`ParityEstate` on the warm SQL Server 2022
  container (`localhost:11433`), seeded from
  `sidecar/projection/src/Projection.Adapters.OssysSql/Resources/ossys-edge-case.seed.sql`
  (7 espaces, 15 entities across 7 modules, 2 static kinds, self-referencing Category tree,
  cross-module FKs, an untrusted FK, `ON DELETE CASCADE`/`SET NULL`/`ON UPDATE CASCADE`,
  disabled + enabled triggers, a filtered unique index, a computed column, an XML column,
  a `COLLATE`-divergent column, authored `''` and `getutcdate()` defaults) + deterministic
  data rows (Country×3, Currency×3, City×2, Customer×1).
- **The extraction SQL is byte-identical on both sides** (`md5 05b49adb…`,
  `src/AdvancedSql/outsystems_metadata_rowsets.sql` ≡
  `sidecar/projection/src/Projection.Adapters.OssysSql/Resources/outsystems_metadata_rowsets.sql`),
  gated by `SESSION_CONTEXT(N'OsmSkipJsonRowsets')` which V2 sets and V1 does not — the gate
  changes no typed-rowset content. **So both engines read the same source truth.**
- **V1:** `full-export --config <cfg> --connection-string <ParityEstate> --profiler-provider
  sql --refresh-cache` (cfg: `tighteningPath=config/default-tightening.json`,
  `supplementalModels.includeUsers=false`). Exit 0.
- **V2:** `projection publish --json` with `model.ossys=file:<conn>`, `profiler.provider=live`,
  `PROJECTION_MSSQL_CONN_STR` set, target env `access:bundle grant:schema+data`. Exit 0.
- **Round-trip.** Every emitted per-table `.sql` was deployed (topologically ordered, same
  algorithm both sides) to a fresh database (`ParityV1DB`/`ParityV2DB`); the data lanes were
  run; and the two resulting databases were diffed at the `sys`-catalog level (columns, types,
  nullability, defaults, identity, collation, FKs with actions + trust, indexes with options,
  triggers, extended properties, checks, schemas, row counts).
- **Determinism.** Each engine was run twice and its full output tree byte-diffed.
- **Interop lanes.** V2 was additionally fed V1's `model.edge-case.json` fixture and V1's own
  freshly-extracted `model.extracted.json`.

The full evidence set (both output trees, the catalog diff, deploy logs, determinism diffs,
run NDJSON) was retained. The finding IDs `EF-*`/`EP-*` (empirical), `V-*` (personally
verified), and the area IDs (from a 14-dimension static comparison) are cross-referenced below.

---

## 1 — The verdict

**V2 is a net fidelity improvement over V1 and is the right long-term driver — but it is not
yet a drop-in replacement.** On the identical estate, V2's *loaded database* is closer to the
deployed OutSystems source reality than V1's on **eight** independent axes (delete/update rules,
untrusted-FK trust state, collation, XML typing, `CREATE SCHEMA`, refactorlog, temporal scale
facets, deployed `DATA_COMPRESSION`), and V2 fixes **five** latent V1 correctness bugs. But V2
carries **three blocker-class regressions** (two universal, one conditional on composite-PK-target
FKs) and a cluster of **conditional majors** that must be closed or consciously accepted before a
production cutover of a real (300-table) estate.

> **A second empirical round (2026-07-17) fanned out across eight axes the base estate didn't
> exercise — computed/PERSISTED columns, temporal tables, sequences, empty-string→NULL,
> composite-PK FK targets, deployed-NOT-NULL drift, the scalar-facet matrix, and >1000-row data
> staging — each on its own database, deployed and diffed. It is written up in §7; its findings
> are folded into the registers and scorecard below.** The headline additions: a third
> (conditional) blocker (composite-PK FK truncation), a fifth V1 bug (>1000-row destructive-sync
> data loss), the deployed-NOT-NULL loosening now proven, and **three shared gaps where *both*
> engines are wrong the same way** (temporal system-versioning, `PERSISTED`, sequences) — which
> matter for scoping but do **not** block a v1→v2 switch, since neither engine handles them.

**The single most important sentence in this audit:** *the two engines do not produce the same
loaded database from the same source* — and in the current tree, **each side has at least one
way to produce a database that is wrong or that fails to deploy.** This is not a "v2 is 98%
there, ship it" story; it is a "v2 is architecturally ahead and closes real v1 bugs, but has
its own new sharp edges" story. Both are true at once, exactly as you anticipated.

**Recommended posture:** keep the R6 dual-track. Gate V2-driver on closing the two universal
blockers (topological ordering of the data lanes; the authored-default expression rendering), the
conditional composite-PK-FK blocker, plus the FK-cascade and trigger items below. Everything else
is a documented, blessable divergence.

### Scorecard (this estate, deployed-database view)

| Axis | V1 | V2 | Winner |
|---|---|---|---|
| FK `ON DELETE`/`ON UPDATE` rules | **dropped** (all NO ACTION — a bug) | preserved (CASCADE/SET NULL/UPDATE CASCADE) | **V2** |
| Untrusted FK end-state | left **DISABLED** (enforcement hole) | enabled-untrusted (matches source) | **V2** |
| Per-column `COLLATE` | dropped (→ DB default) | preserved | **V2** |
| XML / deployed-storage typing | `NVARCHAR(MAX)` | `XML` (faithful) | **V2** |
| `CREATE SCHEMA` for non-dbo | not emitted (fresh build fails) | `Schemas/<s>.sql` | **V2** |
| RefactorLog in bundle | none | wired (`#671`) | **V2** |
| Index `DATA_COMPRESSION` | preserved (PAGE/ROW) | **dropped → decompresses on deploy** | **V1** |
| Same-leading-column index names | **collide → deploy fails (Msg 1913)** | deduped (`_1`/`_2`), deploys | **V2** |
| FK to a composite-PK target | full 2-leg FK (deploys) | **first leg only → Msg 1776, undeployable** | **V1** |
| Deployed NOT NULL, model nullable | preserved (`PHYSICAL_NOT_NULL`) | **loosened to NULL** | **V1** |
| Computed-column expression identifiers | rewritten to logical | **left physical → fails on CS collation** | **V1** |
| Temporal scale facet (`datetime2(3)`/`time(2)`) | **dropped → widened to scale 7** | preserved | **V2** |
| >1000-row destructive (Authoritative) sync | **67% row loss (batch-boundary delete)** | immune (stage-all-then-MERGE) | **V2** |
| Temporal tables / `PERSISTED` / sequences | dropped | dropped | neither (shared gap) |
| Authored `''` / `getutcdate()` default | faithful | **mis-rendered → insert bomb** | **V1** |
| Data-lane executable order | (its own bootstrap is broken) | **alphabetical fallback on any cycle → FK 547** | neither |
| Trigger emission | 1-batch bug → runtime bomb | physical-name target → **deploy bomb** | neither |
| Nullability tightening | evidence-gated NOT NULL | never coerces | design choice (bless) |
| Determinism (re-run byte-identity) | 10 files drift (timestamps) | 0 bytes | **V2** |
| Reads V1's model.json | yes (best-effort) | **refuses** (stricter reader) | V1 (interop) |

---

## 2 — The regression register (V2-worse), ranked

Each row: what V2 does worse, the evidence, whether it bites a real OutSystems estate, and the
fix. **B = blocker, M = major, m = minor.**

### B-1 — Data lanes fall back to alphabetical order on any FK cycle → not linearly deployable
*(empirical EF-8 + personally verified V-1; `fks-topo-order-defeated`)*

V2 **does** run `TopologicalOrderPass` on the data lanes (`BootstrapEmitter.fs:167`,
`StaticSeedsEmitter.fs:385`, `DataEmissionComposer.fs`), so the blunt claim "it never orders"
is false. **But** the pass falls back to `Mode = Alphabetical` (by `SsKey`) for the *whole
catalog* whenever any SCC is unresolved (`TopologicalOrderPass.fs:503-556`), and under the
`TreatAsCycle` self-loop policy that the bootstrap uses, self-references and non-weak cycles go
unresolved. On this modest 15-entity estate the run emitted **`structural.cycleUnresolved` six
times**, so `Data/Bootstrap.sql` rendered `Customer` (SsKey `bbbb…`) **before** its FK target
`City` (SsKey `cccc…`) → linear execution fails **`FOREIGN KEY … 547`** (proven on `ParityV2DB`).

- **Bites?** Yes, broadly. Real OutSystems estates (300 tables) are cycle-dense; the shipped
  `Data/Bootstrap.sql` — which the `.sqlproj` deliberately `None`'s as "a separate post-publish
  load step the receiving team's pipeline must add" — is not linearly executable whenever an
  unresolved cycle exists. The per-table **DDL** bundle is unaffected (SSDT/DacFx resolve refs
  at build; only linear `sqlcmd` execution of the data file breaks).
- **Fix:** the data-lane render must not inherit the whole-catalog alphabetical fallback — order
  the acyclic majority topologically and defer only the genuine cycle members (the two-phase
  NULL→UPDATE machinery already exists for exactly this). Verify whether the internal `--go`
  load leg levels correctly (it may) and, if so, make the shipped file match it.

### B-2 — V2 is not a drop-in reader of V1's model.json / extraction output
*(empirical EI-1/EI-2; `interop/B1,B2,C1,A1,C2`)*

Two hard refusals, both proven:
- V2 rejects V1's `tests/Fixtures/model.edge-case.json` with `adapter.osm.indexFields`
  ("Required index fields missing on … AppCore.City / ExtBilling.BillingAccount") — V2's OSM
  reader requires index fields that V1's minimal index-JSON shape omits and that V1's own
  `Osm.Json` loader tolerates/defaults.
- V2 rejects V1's freshly live-extracted `model.extracted.json` with
  `catalog.index.danglingColumn` on `SystemUsers.User PK_User_Id` — V1 emits a PK-backing index
  referencing a column V2's `Catalog.create` invariant will not admit.

- **Bites?** Only if your workflow feeds V1 artifacts into V2 (e.g. running V2 as a verifier on
  V1's extracted model). For the live-OSSYS path both engines read the source directly and this
  never arises. But it **blocks** the "V2-augmented" mode where V1 drives and V2 checks V1's
  own model — a mode the project's own KPI ladder relies on.
- **Fix:** widen V2's OSM adapter to default the missing index fields (as V1 does) and to
  tolerate V1's PK-backing-index shape, *or* declare V1's model.json explicitly out of V2's
  input contract. Note the latent trap (`C2`): had parsing proceeded, V2 would have silently
  dropped IDENTITY (V1's `onDisk.isIdentity` isn't lifted) — so a permissive fix must also lift
  identity, or it trades a loud refusal for a silent corruption.

### M-1 — Authored default expressions mis-rendered; `getutcdate()` becomes an insert-time bomb
*(empirical EF-2 + mechanism-pinned; `types-defaults-*`)*

The OutSystems authored default `getutcdate()` (`ossys_Entity_Attr.DEFAULT_VALUE`) is emitted by
V2 as `DEFAULT CAST ('getutcdate()' AS DATETIME2(7))` — a **string literal**, not the function —
which deploys and then **fails every insert with Msg 241** (proven on `ParityV2DB`). The authored
empty-string default `''` becomes `DEFAULT N''''''` (a literal two-apostrophe string) instead of
V1's faithful `DEFAULT ('')`.

- **Mechanism (verified):** V2 lifts the raw `DEFAULT_VALUE` text through the *typed-value*
  channel (`OssysRowsetReader.fs:104-108` → `SqlLiteral.ofRaw` with the column's primitive),
  so `getutcdate()` is typed as a `DateTimeLit` and WP-17(d)'s explicit-CAST temporal render
  (`SqlLiteral.fs`, `ScriptDomBuild.fs:338-345`) wraps it as `CAST('getutcdate()' AS …)`.
  **Neither engine interprets the OutSystems expression language**; V1 sidesteps it by
  re-emitting the *deployed* `sys.default_constraints.definition` verbatim, which is why V1 is
  correct here. V2's golden corpus is catalog-direct so it never exercises this authored-value
  channel — the bug is invisible to the goldens.
- **Bites?** Yes — function-valued defaults (`getutcdate()`, `newid()`, `newsequentialid()`) are
  common on OutSystems audit columns; every such column is an insert-time failure under V2.
- **Fix:** discriminate expression-vs-value at the lift (refuse or pass through raws that fail
  `RawValueCodec` canonical parse; or consume the already-extracted `#ColumnReality.DefaultDefinition`).
  One fix closes both the `''` and `getutcdate()` cases. **This is the highest-value single fix
  in the register.**

### M-2 — `DATA_COMPRESSION` silently dropped on the live lane (now empirically proven)
*(empirical EF-15 — deployed-database proof; `idx-datacompression-live-parse-bug`, `idx-json-path-options-dropped`, `idx-platform-auto-live-flag-dead`)*

**Confirmed at the deployed-database level.** I added a PAGE-compressed and a ROW-compressed
index to the source estate and re-ran both engines. **V1 preserves compression**
(`… WITH (DATA_COMPRESSION = PAGE ON PARTITIONS (1))` / `ROW …`); **V2 emits the indexes bare**
(`CREATE INDEX … ON …([Col])`, no `WITH` clause). Deploying V2's project and reading back the
catalog proves it: every StockItem index reads `data_compression_desc = NONE`. Both engines read
the *same* extraction SQL (which does `SELECT` `data_compression`), so this is a **genuine V2
live-lane regression, not shared behaviour** — deploying the ejected V2 project silently
**decompresses** every compressed index (a real storage-size and IO-profile change post-eject).

Still lane-specific and **not** re-verified here: (a) through V2's model-JSON source lane a
disabled index is claimed to deploy **enabled** and `IGNORE_DUP_KEY`/other `WITH` options to drop;
(c) `emission.includePlatformAutoIndexes=false` claimed to be a no-op on the live lane. The index
options I *did* exercise on the live lane were faithful (filtered `UIX … WHERE … IS NOT NULL`,
`FILLFACTOR=85`, `STATISTICS_NORECOMPUTE=ON`, `ALTER INDEX … DISABLE` — byte-identical to V1,
EP-5), so the remaining claims are the JSON lane's (a) and the prune-flag's (c). **Fix:** carry
the `data_compression` facet the rowsets already return through the IR into ScriptDom index emission.

### M-3 — Nullability: V2 never tightens (and can drop a deployed NOT NULL); V1 does both
*(empirical EF-3; `types-nullability-*`)*

On this estate V1 tightened `Category.ParentCategoryId` and `StockMovement.SupplierId` to
`NOT NULL` (model + deployed say nullable; the profiler saw zero rows/zero nulls →
evidence-gated tightening); V2 keeps them nullable. Conversely (static analysis,
`types-nullability-deployed-notnull-loosened`) a column the model marks nullable but that a DBA
hand-tightened to `NOT NULL` in the deployed DB is emitted **NULL** by V2, dropping the
integrity constraint. V2's stance ("nullability is the modeling team's decision, not the
tool's" — `DECISIONS 2026-06-22`, amended 2026-07-15 to re-allow *relaxation* overrides) is a
deliberate, principled divergence — **but it is a behaviour change the operator must bless**:
mandatory-but-dirty columns emit `NOT NULL` under V2 and **fail the load** (by design), where V1
would have shipped a nullable column that silently carried the legacy NULLs. This interlocks
with FK semantics (a `NOT NULL` FK column forecloses `ON DELETE SET NULL`).

### M-4 — Platform users (`ossys_User`) not seeded by a default V2 publish
*(personally verified V-4; `data-lanes-ossys-user-supplemental-gap`)*

V1 defaults `includeUsers ?? true` (`BuildSsdtRequestAssembler.cs:213`) and ships
`config/supplemental/ossys-user.json`, so its bootstrap seeds `dbo.User` rows out of the box. V2's
adapter never marks `IsUserFk` (matches only in compiled DLLs, not source), and its user-remap
paths *remap* FK values across environments rather than *seeding* user rows — so a default V2
publish emits **no platform-user rows**. A migrated table with a mandatory `CreatedBy`/`UpdatedBy`
FK to the platform user table therefore has **dangling references** under V2 unless the user
table is separately populated. **Nuance:** V1's bulk user-seed is environment-blind; V2's design
(explicit `transfer --reconcile <UserTable>:<emailColumn>`) is *more correct* for a multi-env
cutover but is **near-inert today** (the matching-strategy config was removed). State which path
the org blesses and wire it before relying on user FKs.

### M-5 — Two `.sqlproj` build/deploy hazards
*(personally verified V-3 (G5b) + empirical G5a; `bundle-g5a/g5b`, `sqlproj-C1/C2`)*

- **G5a:** `manifest.remediation.sql` is written unconditionally at the **bundle root** and is
  **not** `Build`-Removed from the emitted `.sqlproj` (confirmed: absent from
  `ProjectionCatalog.sqlproj`). When the estate produces remediation findings, that file contains
  active `SELECT`s the SDK `Build` glob compiles as a schema object → build break. (My estate was
  clean, so the file was empty — the hazard is latent, not cosmetic.)
- **G5b:** the post-deploy `:r`-include order is **alphabetical** (`Pipeline.fs:1640-1643`,
  `… |> List.sort`), so with both data lanes present `Data/MigrationData.sql` is included
  **before** `Data/StaticSeeds.sql` — inverting the intended static-first order. If any
  operator-curated migration row FKs into a static-entity row, the linear post-deploy fails. (My
  run had no migration lane, so it was latent.)
- **Fix:** `Build`-Remove (or relocate) `manifest.remediation.sql`; order the post-deploy lanes
  static-first explicitly rather than by `List.sort`.

### M-6 — FK constraint names re-synthesized (schema-compare churn); manifest FK-count wrong
*(empirical EP-3 + EF-13; `fks-constraint-names-not-honored`, `manifests/v2-fk-count-inflated`)*

- V2 always synthesizes `FK_<Owner>_<Target>_<Col>` and never honours a deployed FK constraint
  name — the deployed name *is* read on the live path (`OssysFkRealityRow.FkName`) but dropped at
  `toBundle`. For platform-generated `OSFRK_*` names both engines converge (6/8 FK names were
  byte-identical on my estate), so this bites only estates with **hand-curated** FK names, which
  see rename churn on first schema-compare. (WP-7 is the open remainder.)
- V2's `manifest.json` `foreignKeyCount` disagrees with the emitted DDL on **7/15** tables
  because it counts *inbound* references too (City=1 for Customer→City), contradicting its own
  field doc. A consumer trusting the manifest miscounts.

### M-7 — Operator reporting: V2's default publish answers different questions than V1's
*(`manifests/v1-only-operator-questions`, `v2-summary-zero-decisions`, `options-provenance`; `console-ux/UX-02,UX-10`)*

V1's default full-export ships `policy-decisions.json`, `opportunities.json`, `validations.json`
answering "*why is this column NOT NULL / why was this FK created / what evidence backed each
decision / which toggles were in effect and from where*." V2's default publish reports tightening
decisions as all-zeros in `manifest.summary.txt` (the interventions that produce decisions aren't
run unless configured) and moves the operator surface to `fidelity.json`/`.txt` (data violations,
fired tolerances, uniqueness candidates) — a *different* set of answers, not a superset. V2 also
does not echo the effective config/options provenance into the artifacts the way V1's manifest
does. Separately, on the **success console** V2's terse stdout can hide warning-severity findings
(they roll up to disk + one Warn line) where V1 prints them inline — a CI step capturing only
stdout sees a clean run (`UX-02`). Net: V2's diagnostics are *richer and machine-parseable*
(NDJSON, exit-code API, episode ledger, `check` verbs) but **not a drop-in substitute** for the
specific decision-provenance questions V1's reports answer by default.

### Minor / conditional (abbreviated)
- **m** FK-name-honoring (WP-7 open), PK/index cross-table collision has no tripwire, junction-table
  deferral + manual cycle-ordering overrides (V1 `CircularDependencyOptions`) not carried,
  composite-PK FK first-leg-only, computed-column identifier remapping edge, decimal-precision-0
  edge, seed-row sourcing is live-connection-only on V2 (no fixture-snapshot data lane),
  bare-table/quote-strategy layout knobs V1 has and V2 lacks, coverage-denominator + extended-
  property-count reporting quirks, refusal-render defects, NDJSON purity on failure.
- **cosmetic** header banners omitted, cross-catalog FK detection, partition-scheme edge.

---

## 3 — The improvement register (V2-better) and the V1 bugs V2 fixes

These are the reasons to switch. Several are **correctness fixes**, not aesthetics.

### V2 fixes five latent V1 correctness bugs
- **V1 loses >1000 rows on a destructive static-entity sync** *(empirical EF-23)*. Under
  Authoritative (destructive) synchronization, V1 partitions a static entity into 1000-row batches
  and appends `WHEN NOT MATCHED BY SOURCE THEN DELETE` to **each** batch — so batch 2's delete arm
  nukes batch 1's rows. A 1500-row source deploys **500 rows** (67% silent loss; proven — deployed
  `COUNT=500, MIN=1001, MAX=1500`, exactly batch 2). `StaticSeedSqlBuilder.cs:95/169/261-263`. The
  default mode is non-destructive so only opt-in Authoritative configs are exposed, but the loss is
  silent when they are. V2's stage-all-then-one-MERGE design cannot exhibit it (deployed 1500).
- **V1 emits colliding index names → deploy failure** *(empirical EF-16)*. Two source indexes that
  share a leading column (e.g. `IDX_STOCKITEM_REORDER` on `(ReorderLevel, WarehouseQty)` and a
  second index on `(ReorderLevel)`) both synthesize to `IX_StockItem_ReorderLevel` — V1 has **no
  collision dedup**, so deploying its `dbo.StockItem.sql` fails **Msg 1913** ("index … already
  exists"), proven on a fresh DB. V2 deduplicates with SsKey ordinals (`…_1`/`…_2`) and deploys
  clean. Same-leading-column indexes are common on real estates. *(Note the pairing with M-2: on
  this exact estate V1 fails to deploy while V2 deploys but decompresses — neither is correct.)*
- **V1 drops every physically-backed FK's delete/update rule** *(personally verified V-2;
  empirical EF-1)*. `ForeignKeyEvidenceResolver.cs:141-143` feeds the sys-catalog action
  (`"CASCADE"`/`"SET_NULL"`) into `MapDeleteRule` (`SmoEntityEmitter.cs:177-185`), whose switch
  only recognizes the OutSystems model vocabulary (`"Cascade"`/`"Delete"`/`"SetNull"`) and falls
  through to `NoAction`. So every source-backed FK on a live extraction loses its rule (proven:
  all `NO_ACTION` in `ParityV1DB` where the source has CASCADE/SET NULL). Perverse corollary:
  *logical-only* refs (no deployed constraint) *do* get their rule via the model code. `ON UPDATE`
  is structurally unrepresentable in V1 (`SmoForeignKeyDefinition` has no field). **V2 maps and
  preserves all of it** (EF-1).
- **V1 leaves untrusted FKs DISABLED** *(empirical EF-4)*. V1's `WITH NOCHECK ADD` +
  `NOCHECK CONSTRAINT` deploys the untrusted FK as `is_disabled=1` — new rows are **not enforced
  at all** (an integrity hole). V2's `NOCHECK` + `WITH NOCHECK CHECK` two-step reproduces the
  source exactly (`is_not_trusted=1, is_disabled=0`).
- **V1's disabled-trigger emission is a batch bug** *(empirical EF-7)*. V1 lands
  `CREATE TRIGGER` and the following `ALTER TABLE … DISABLE TRIGGER` **in one batch** (no `GO`),
  so the `ALTER` becomes part of the trigger module (`sys.sql_modules` proven) and the
  source-disabled trigger deploys **enabled** → every insert fails Msg 4920. (V2's trigger story
  is *also* broken — see below — but differently.)

### V2 is more faithful to deployed reality
- **Collation preserved** (EF-5): V1 drops per-column `COLLATE` (→ DB default); V2 keeps it.
- **XML / deployed-storage typing** (EF-6): V1 emits `NVARCHAR(MAX)` for an `XML` column; V2
  emits `XML`. WP-4b restored deployed-storage precedence for ordinary scalars on the live lane.
- **`CREATE SCHEMA`** (EF-10, `#671`): V1 emits none (a fresh build of a non-dbo estate fails);
  V2 emits `Schemas/<schema>.sql`. This was a gap **shared** with V1 that V2 has now closed.
- **RefactorLog in the bundle** (`#671`): rename detection + a byte-deterministic XML renderer
  now reach the bundle + `.sqlproj` `<RefactorLog>` item, so an incremental publish ALTERs rather
  than DROP+CREATEs on rename. V1 has no refactorlog at all.
- **Idempotent bootstrap** (`data-lanes-bootstrap-upsert-vs-insert-only`): V2's bootstrap MERGE
  converges a drifted DB to the source snapshot (full upsert); V1's is insert-only (no-op on
  existing rows).

### V2 is stronger operationally / structurally
- **Determinism** (EF/ED): V2 re-run is **byte-identical across the whole tree** (manifest
  included — the packet's stamp-exclusion note is stale); V1 re-run drifts **10 files**
  (timestamp headers in both bootstraps, all four decision/manifest JSONs, both remediation
  scripts). For a golden-diff change-review workflow post-switchover this is decisive.
- **Buildable project** (`bundle-sqlproj-generation`): V1's `.sqlproj` is a legacy
  msbuild-2003 skeleton that was never a working deploy path (missing targets import; a
  `PostDeploy Include Seeds\**\*.sql` glob where SSDT allows only one post-deploy script). V2's
  opt-in `Microsoft.Build.Sql/2.2.0` SDK project is the actual `dotnet build`/`sqlpackage` path.
- **Atomic output-dir replace** prevents stale-artifact drift (the V1 failure mode where removed
  entities linger). **Config** is explicit per-cwd `projection.json` vs V1's silent
  root-`pipeline.json` auto-discovery trap (`--config` is mandatory for deterministic V1 runs).
- **Fail-loud refusals** with a documented exit-code API and named diagnostic codes vs V1's
  best-effort-import-with-warnings; **DacFx dacpac** path (V1 has none); **check verbs** (canary/
  drift/data/ready/shape/go/plan + estate) and the **episode ledger** (provenance across runs).

---

## 4 — Emission / aesthetic parity

The **CREATE TABLE bodies are near-byte-identical** — V2's `renderConstraintsElegant` (a text
post-processor over ScriptDom `Sql160` output) reproduces V1's inline constraint ladder, 4-space
indent, UPPERCASE keywords, bracket-quoting, `IDENTITY (1, 1)`, column order, and `GO` framing.
Both are ASCII/UTF-8, LF-only, no BOM, trailing newline (EP-2, EP-7). File layout is identical:
`Modules/<Module>/<schema>.<Table>.sql`, and **V1 already emits logical entity names**
(`[dbo].[Customer]`, not `[OSUSR_ABC_CUSTOMER]`) — the "V2 renames, V1 didn't" framing is false;
both substitute logical names, with physical names surviving only inside DF/CK constraint names
on **both** sides.

Genuine aesthetic deltas (all cosmetic, all deploy-equivalent):
- Extended-property style: V1 `EXEC sys.sp_addextendedproperty @name=N'…',` + trailing `;` vs V2
  `EXECUTE [sys].[sp_addextendedproperty] @name = N'…'` (bracketed, spaced, no `;`).
- V2 adds `Projection.LogicalName` + `Projection.SsKey` extended properties per table *and* per
  column (70+70 on this estate) — load-bearing for rename round-trips pre-eject, inert vendor
  residue after (decide keep-or-strip at eject).
- Composite PK: V1 `PRIMARY KEY CLUSTERED (…)` vs V2 `PRIMARY KEY (…)` (deploy-equivalent).
- Static-seed file: V1 per-module `Seeds/RefData/StaticEntities.seed.sql` with banner comments +
  `SET NOCOUNT ON` + per-entity headers; V2 a single bare `Data/StaticSeeds.sql`. Same rows, same
  MERGE upsert semantics, same `IDENTITY_INSERT` bracketing, same `N'…'` literals (EP-6).
- Three constraint/index **name** deltas out of dozens: `FK_Category_Parent_*`→`FK_Category_Category_*`,
  `FK_SalesOrderLine_SalesOrder_SalesOrderId`→`FK_OrderLine_SalesOrder_OrderId`, and V2's extra
  `IX_JobRun_CreatedOn` (the kept platform-auto index). PK names are byte-identical everywhere.

**One shared functional trap on the trigger axis** (EF-7): V2 keeps the trigger's **physical**
name and `ON`-target unrewritten (`ON [dbo].[OSUSR_XYZ_JOBRUN]`), so `dbo.JobRun.sql` and
`dbo.StockMovement.sql` **fail at deploy** (Msg 8197) and their post-trigger batches (including
the extended properties) are skipped. V2 does correctly rewrite CHECK bodies and index filter
predicates, and pairs its `DISABLE TRIGGER` with the right (physical) name and only for
source-disabled triggers — but the unrewritten trigger body is broken DDL for any real triggered
table. So: **v1 trigger = runtime bomb (fails silently at deploy, bombs on insert); v2 trigger =
deploy bomb (fails loud, partial table).** Neither ships correct trigger DDL; a real estate with
triggers needs hand-authoring on either side.

---

## 5 — Interop, determinism, and the "already fixed on main" corrections

Several divergences described in the `SSDT_HANDOFF_REVIEW_PACKET.md` (as of `ef706ac`) are
**already remediated** on this tree (`ea21b13`) and should not count against V2:

| Was flagged | Status on `ea21b13` | Evidence |
|---|---|---|
| A1 `PK_<Schema>_<Table>` shape | **Fixed** — V1-shape `PK_<Entity>_Id`, byte-identical to V1 | EP-3 (empirical) + WP-8 |
| C1 deployed-type drift never wins | **Fixed** — deployed-storage precedence restored | WP-4b + EF-6 |
| FK HasDbConstraint hardcoded true | **Fixed** — real on live path | WP-1a + V-verified parity |
| G3 refactorlog never reaches bundle | **Fixed** — wired + DacFx-proven | `#671` + EF (emitted) |
| G6 no `CREATE SCHEMA` | **Fixed** — `Schemas/<s>.sql` | `#671` + EF-10 (deploys clean) |

**Interop** (V2 refusing V1 inputs) is B-2 above. **Determinism** strongly favours V2 (§3).

V2's own `V1_PARITY_MATRIX.md` self-assessment (append-only, status-history-inflated) tallies
roughly 115 PARITY / 40 V2-EXTENSION / 57 DIVERGENCE / 95 NOT-MAPPED / 29 V1-SUNSET / 5
V1-BUG-CORRECTED across ~120 capability rows — i.e. V2 itself records a large **NOT-MAPPED**
surface (niche V1 paths not carried). Most NOT-MAPPED rows are low-value or deliberate sunsets,
but the count is the honest headline: *V1 has a big long tail V2 does not replicate*, and the
switchover decision must be scoped to the estate's actually-used features, not V1's full surface.

---

## 6 — Switchover readiness checklist

**Close before V2-driver on a real estate (correctness blockers):**
1. **B-1** Data-lane topological order — don't inherit the whole-catalog alphabetical fallback;
   order the acyclic majority, defer only true cycle members. *(the shipped `Data/*.sql` must be
   linearly deployable)*
2. **M-1** Authored default expressions — discriminate expression-vs-value at the OSSYS lift so
   `getutcdate()`/`newid()`/`''` render faithfully. *(highest-value single fix)*
3. **B-3 (EF-17)** Composite-PK FK — carry the referenced-column list in the `Reference` IR so a
   multi-leg FK emits all legs, or refuse loudly. *(silent → undeployable today; inventory
   composite-PK-target FKs first)*
4. **M-8 (EF-19)** Rewrite computed-column **expression** identifiers to logical names (V1 does;
   V2 doesn't → fails on case-sensitive collation).
5. **EF-7** Trigger emission — rewrite trigger `ON`-target + body to logical names (or refuse
   loudly with a named diagnostic). Shared with V1; both are broken.
6. **M-5** The two `.sqlproj` hazards — `Build`-Remove `manifest.remediation.sql`; order the
   post-deploy lanes static-first.

**Bless as deliberate divergences (write the DECISIONS note + operator sign-off):**
7. **M-3** Nullability stance — V2 refuses coercion (mandatory-but-dirty fails the load by design)
   *and* loosens a deployed `NOT NULL` on a model-nullable column (EF-18). Default posture is also
   inverted: V1 tightens by default (opt-out), V2 is opt-in.
8. **M-4** Platform-user seeding — decide bulk-seed (V1) vs `transfer --reconcile` (V2) and wire it.
9. **M-6** FK-name honouring (WP-7) — accept the one-time schema-compare churn or close WP-7.
10. Platform-auto index default (V2 keeps, V1 prunes) and the extended-property vendor residue.

**Hand-author regardless of engine (shared gaps — neither emits them):**
11. **Temporal / system-versioning, `PERSISTED` computed columns, sequences** (EF-20/21/22) — both
    engines drop these. If the estate uses them, they need hand-authored SSDT objects either way.

**Confirm on a targeted fixture before trusting/dismissing (lane-specific, not exercised here):**
12. **M-2** disabled-index/`IGNORE_DUP_KEY` on the **JSON** lane (the live-lane `DATA_COMPRESSION`
    drop is now proven, EF-15). Deploy a disabled-index estate through the JSON lane and diff.
13. **B-2 / C2** If you need V2 to read V1 model.json, widen the OSM adapter *and* lift IDENTITY
    (else the loud refusal becomes a silent IDENTITY drop).

**Estate pre-audit adds:** composite-PK tables that are FK targets (B-3), computed columns
(expression case + `PERSISTED`), temporal tables, sequences, static entities >1000 rows synced
destructively (EF-23), non-default temporal scales (EF-24), and the target database collation
(case-sensitive ⇒ M-8 is a blocker).

**Already good — lean on these:** delete/update-rule fidelity, untrusted-FK trust, collation, XML
typing, `CREATE SCHEMA`, refactorlog, byte-determinism, the buildable SDK project, fail-loud
refusals + exit-code API, and the episode/fidelity diagnostics.

**Estate pre-audit (cheap sys-catalog sweeps that de-risk the verdicts):** function-valued
DEFAULTs, triggers, composite-PK targets of FKs, hand-curated (non-`OSFRK_`) FK names,
compressed/disabled indexes, mandatory-but-physically-nullable columns, DBA-tightened NOT NULLs,
tables with mandatory `CreatedBy`/`UpdatedBy` FKs, and self-referencing/cyclic entity clusters.

---

## 7 — Second-round empirical findings (deployed-database-proven delta, 2026-07-17)

A fan-out across **eight axes the base estate never exercised**, each provisioned on its own
database, run through both engines, deployed, and diffed at the `sys`-catalog level. Every row
below is proven by a deployed value or a verbatim `Msg`. IDs are `EF-17…EF-26` in the evidence
ledger.

### New regressions (V2-worse)
- **B-3 (conditional blocker) — composite-PK FK truncated to one leg → undeployable** *(EF-17)*.
  Source `LineComment` has a physical 2-leg FK to `OrderLine`'s composite PK `(OrderId, LineNo)`.
  V1 emits both legs and deploys; V2's `Reference` IR is a single `sourceAttribute → targetKind`
  with no referenced-column list, so it drops the `LineNo` leg and points the FK at `OrderLine`'s
  *first* PK column — which is not a standalone key → **`Msg 1776` "no primary or candidate keys …
  match"**, table not created. V2 publish exits 0 with **no truncation diagnostic** (violating its
  own *downgrades-never-silent* law). Conditional on the estate having an FK that targets a
  composite-PK table; **inventory for these before cutover.** *(Sibling, EF-17b: a purely logical
  single-column ref to a composite-PK target produces an invalid single-leg FK on **both** engines
  — a shared defect, since V1's evidence-less fallback is V2's only path.)*
- **M-3 confirmed — deployed NOT NULL loosened to NULL** *(EF-18)*. A column the model marks
  `Is_Mandatory=0` but physically `ALTER`ed to `NOT NULL`: V1 preserves `NOT NULL`
  (`policy-decisions.json` rationale `PHYSICAL_NOT_NULL`, deployed `is_nullable=0`); V2 emits `NULL`
  (deployed `is_nullable=1`), silently dropping the constraint. Root cause: V2's catalog
  `Column.IsNullable` reads the model's `Is_Mandatory`, not the physical schema — so its own rule
  named `PhysicallyNotNull` is inert. On an incremental apply V2 would drive the column back to
  `NULL`. This is the second half of audit M-3, now empirically proven.
- **M-8 (new) — computed-column expressions keep physical identifiers** *(EF-19)*. Both engines
  rename table columns to logical mixed-case, but only V1 rewrites the **computed-expression**
  identifiers. V2 emits `[TotalValue] AS ([WAREHOUSEQTY] * [AVGCOST])` (physical uppercase). Latent
  on the default case-insensitive collation; on a **case-sensitive** database collation V2's table
  is a hard deploy failure (**`Msg 207` "Invalid column name WAREHOUSEQTY"**, table not created)
  where V1 deploys clean. Blocker *if the target collation is case-sensitive.*

### New improvements (V2-better) — beyond the fifth V1 bug (EF-23, in §3)
- **Temporal scale facets preserved** *(EF-24)*. V1 drops the scale on `datetime2`/`time`/
  `datetimeoffset` at the DDL layer → deployed columns widened to scale 7 (`datetime2(3)` source →
  V1 deployed scale 7; V2 deployed scale 3). Latent at the OutSystems platform default of 7 (bare
  == 7); bites any non-default temporal scale (storage + schema-compare drift). V1's extraction was
  *correct* (`onDisk.scale=3` in its own model.json) — the loss is purely at emission.

### Shared gaps — **both engines wrong the same way** (do NOT block a v1→v2 switch)
These matter for *scoping* the ejected estate (the receiving team must hand-author them regardless
of engine), not for the switchover decision — neither engine is a regression relative to the other.
- **Temporal / system-versioning** *(EF-20)*: a `SYSTEM_VERSIONED` source table (`temporal_type=2`
  + history table + `PERIOD`) is emitted by **both** as a plain table (`temporal_type=0`), silently
  dropping the `GENERATED ALWAYS` period columns, `SYSTEM_VERSIONING`, and the history table.
- **`PERSISTED` computed columns** *(EF-21)*: source `is_persisted=1` → **both** emit
  non-persisted. Shared root cause: `outsystems_metadata_rowsets.sql` (byte-identical both sides)
  never selects `sys.computed_columns.is_persisted`.
- **Sequences** *(EF-22)*: `CREATE SEQUENCE` objects are absent from **both** model-sourced runs
  (V1 model.json has no `sequences`; V2 `catalog.snapshot.json` `sequences=[]`). Confirms packet C10.

### Confirmed parity (both correct)
- **Empty-string / single-space data plane** *(EF-25)*: `''` handling, the OutSystems single-space
  sentinel, NULL preservation, and NOT-NULL-carrying-`''` load behaviour are behaviour-identical.
- **Scalar type map** *(EF-26)*: `identifier`/`autonumber`/reference→`BIGINT`, `currency`→
  `DECIMAL(37,8)`, legacy `datetime`→`DATETIME`, `int`/`bigint`/`bit`/`decimal` — all identical,
  deployed facets match. (`XML` typing + `NVARCHAR` collation remain V2-better per EF-5/EF-6.)

---

## 8 — Bottom line

V2 is the better engine and the correct destination: it is more faithful to deployed reality on
**eight** axes, fixes **five** real V1 bugs (including a silent >1000-row destructive-sync data
loss), and is byte-deterministic and operationally far ahead. It is **not** a silent drop-in:
**three** blocker-class regressions — data-lane ordering and authored-default rendering
(universal), and composite-PK FK truncation (conditional) — can produce a database that fails to
deploy or fails at insert, and a cluster of conditional majors change behaviour in ways an
operator must consciously accept. And **three axes are shared gaps** (temporal, `PERSISTED`,
sequences) that *neither* engine handles — real work for the receiving team, but not a reason to
prefer one engine over the other.

The shape of the decision is unchanged by the second round, only sharpened: close the blockers
(§6 items 1–6), bless the deliberate divergences (7–10), hand-author the shared gaps (11), confirm
the JSON-lane items (12–13), and V2 clears the bar for environment-by-environment cutover with V1
held warm as the fallback — exactly the R6 ladder the project already commits to. The through-line
holds: **the two engines do not produce the same loaded database, and on the current tree each has
at least one way to produce one that is wrong or won't deploy** — V2's are fewer, more often
loud, and closing; V1's are silent and structural.

---

### Appendix — evidence index
Empirical output trees, the `sys`-catalog diff, deploy logs, determinism diffs, and run NDJSON
were produced against `ParityEstate`/`ParityV1DB`/`ParityV2DB` on `localhost:11433`. Finding
provenance: `EF-*`/`EP-*`/`EA-*`/`ED-*`/`EI-*`/`EO-*` = empirically proven this session;
`V-*` = personally code-verified at `file:line`; area IDs (`fks-*`, `types-*`, `bundle-*`, …) =
a 14-dimension static comparison, cross-checked against the goldens and the current tree, with
lane-specific claims flagged where not empirically exercised. Reproduction recipes for both
engines (fixture lane and live lane) are in the run scripts retained with this audit.
