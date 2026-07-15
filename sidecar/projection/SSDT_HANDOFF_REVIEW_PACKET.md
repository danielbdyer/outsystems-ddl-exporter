# SSDT_HANDOFF_REVIEW_PACKET.md — the emission decision register, for dev-lead review

> **Prepared 2026-07-15** against the tree at `ef706ac`. Audience: the SSDT-owning dev leads who
> will inherit the ejected schema + data, and the manager shepherding the handoff. Purpose: every
> opinionated decision the F# sidecar (`sidecar/projection`) makes when it emits the SSDT bundle,
> the data lanes, and the build artifacts — stated precisely, with its current setting, where it
> lives, and what a reviewer might change. **This document restates for review convenience; when
> it disagrees with the code or `DECISIONS.md`, they win** (the repo's latest-first rule). Line
> references are as-of the commit above and will drift — the file names won't.
>
> Produced by profiling `Projection.Targets.SSDT`, `Projection.Targets.Data`, `Projection.Core`,
> the adapters, `DECISIONS.md`, `CONFIG_REFERENCE.md`, `THE_GOLDEN_EMISSION.md`, and the golden
> corpus (`tests/Projection.Tests/Golden/`). Where current docs lag the code, the drift is called
> out inline and collected in §8.

---

## 0 — How to review with this packet

Every decision below carries:

- **an ID** (`A1`, `E2`, …) — cite these in review notes;
- **a class**:
  - **[KNOB]** — selectable in `projection.json` today (knob + default given);
  - **[HARD]** — hard-coded emission behavior; changing it = code change + golden re-record + `DECISIONS.md` entry;
  - **[GAP]** — not emitted / open debt you inherit;
  - **[DEPLOY]** — outside emission; a deployment-time or SSDT-project setting the receiving team owns;
- **a verdict line** — mark one of **Approve / Modify / Discuss** per row in the register.

Three ways to see any decision concretely before blessing it:

1. **The golden corpus** — `tests/Projection.Tests/Golden/master/` is the byte-pinned intended
   output of a catalog containing every emission variance. Reading it top-to-bottom **is** the
   review of most rows in this packet. (`pruned-platform-auto/` shows the one all-or-nothing flag
   that can't coexist with the master.)
2. **`projection explain node <projection.json> "<Module>.<Entity>"`** — runs the pipeline with
   your shaping overlays and reports every transform + finding for one entity.
3. **The blessing protocol** — any change you request lands as a byte diff on the goldens
   (`GOLDEN_RECORD=1` + a `DECISIONS.md` note). An unexplained golden diff is a defect; your
   sign-off on a diff is the blessing (`THE_GOLDEN_EMISSION.md` §2).

A representative emitted table, so the register below has a concrete referent
(`Golden/master/Modules/Relations/dbo.Engagement.sql`):

```sql
CREATE TABLE [dbo].[Engagement] (
    [Id]            INT            IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_dbo_Engagement]
            PRIMARY KEY CLUSTERED,
    [AltCustomerId] INT            NULL
        DEFAULT 0
        CONSTRAINT [FK_Engagement_Customer_AltCustomerId]
            FOREIGN KEY ([AltCustomerId]) REFERENCES [dbo].[Customer] ([Id])
                ON DELETE SET NULL
                ON UPDATE NO ACTION,
    ...
)

GO

ALTER TABLE [dbo].[Engagement] NOCHECK CONSTRAINT [FK_Engagement_User_UpdatedBy]

GO

ALTER TABLE [dbo].[Engagement] WITH NOCHECK CHECK CONSTRAINT [FK_Engagement_User_UpdatedBy]
```

---

## 1 — Spotlight: the transforms that deliberately diverge from source reality

These are the rows the leads must actively bless or veto — ranked by blast radius. Each links to
its register entry.

| # | Divergence | Register |
|---|---|---|
| 1 | **Logical renaming of every table and column** — DDL emits OutSystems *logical* names (`[dbo].[Customer]`), not physical (`[dbo].[OSUSR_ABC_CUSTOMER]`). Default-on, **no config off-switch**. CHECK bodies and index filters are rewritten textually; **trigger bodies are NOT rewritten** and ship referencing physical tables. | H1, H2 |
| 2 | **Foreign keys are created that never existed in the source DB.** OutSystems logical-only references emit as enforced, trusted FKs by default — including `ON DELETE CASCADE` ones — with **no orphan check at emit time** unless a `foreignKey` tightening intervention + live profile is configured. | E2, E3 |
| 3 | **Every generated constraint and index name is synthesized**: `PK_<Schema>_<Table>`, `FK_<Owner>_<Target>_<SourceColumn>`, `IX_/UIX_<Kind>_<Attrs>` (+ `_1/_2/_3` ordinals on same-key collisions). Source-authored FK and index names are discarded (FK-name honoring is the open WP7 remainder; `overrides.indexNames` is a named re-open trigger, not yet built). | A1–A3 |
| 4 | **Delete-rule semantics are flattened**: OutSystems `Protect` (an app-level blocker) and `Ignore` (no enforcement) both become `ON DELETE NO ACTION` **with the FK created**; `Delete` becomes `ON DELETE CASCADE` verbatim with no multiple-cascade-path (msg 1785) analysis. | E1 |
| 5 | **Nullability and types come from the logical model, not the deployed DB.** Mandatory-but-physically-nullable columns emit `NOT NULL`; deployed type drift is not diagnosed (except nullability/identity warnings); identifiers emit `BIGINT`; `email`/`phone` emit ANSI `VARCHAR` in an otherwise-NVARCHAR schema; OutSystems `datetime` emits legacy `DATETIME`. | C1–C6 |
| 6 | **Uniqueness is always a `UNIQUE INDEX`** — source UNIQUE *constraints* are silently flattened to unique indexes (object class changes); profile evidence can promote `IX → UIX` (additive-only). | A7, D4 |
| 7 | **Empty string → NULL is a named, universal erasure** on the data plane (any type). Estates that distinguish `''` from `NULL` lose the distinction on eject. | F11 |
| 8 | **Silently absent object classes**: temporal/system-versioning, sequences (from model-sourced runs), `PERSISTED` on computed columns, `ROWGUIDCOL`/`SPARSE`/`FILESTREAM`, non-PK clustered indexes, inactive attributes' columns. Some have no tolerance token — they violate the repo's own named-erasure law if present in the estate. | C7, C10, D1 |
| 9 | **Platform-auto (OSIDX) indexes are kept by default** — but after name synthesis their provenance is gone from the ejected project (no extended property marks them), so keep-vs-prune is **irreversible post-eject**. Pruning instead leaves FK columns unindexed (nothing synthesizes FK-supporting indexes). | D2, D6 |
| 10 | **Vendor metadata is added**: two `Projection.SsKey`/`Projection.LogicalName` extended properties per table *and* per column, plus `MS_Description` from OutSystems descriptions. Load-bearing for rename round-trips pre-eject; inert residue after. | H6 |

---

## 2 — Decision register

### A. Identifier & constraint naming

**A1. PK names are always synthesized `PK_<Schema>_<Table>`** — [HARD]
Source PK names are never consulted (`SsdtDdlEmitter.fs:213`; e.g. `PK_dbo_Customer`,
`PK_audit_ChangeLog`). Schema is embedded with an underscore. Renaming later = drop/recreate of
the clustered index on deploy.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**A2. FK names are always synthesized `FK_<OwnerTable>_<TargetTable>_<SourceColumn>`** — [HARD]
The IR carries `Reference.Name` from the source but the emitter never reads it — honoring it is
the open WP7 remainder (`SsdtDdlEmitter.fs:266-307`). Estates with curated FK names will see
rename churn in schema compare on first publish. Duplicate names across a schema are a loud
Error tripwire (`emit.ssdt.foreignKey.nameCollision`), not a silent dedupe.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**A3. Index names are synthesized from the logical vocabulary** — [HARD] *(shipped 2026-07-01)*
`IX_<Kind>_<Attrs>` / `UIX_…` when unique (name and uniqueness always agree); PK-backing index
takes the PK name; same-key collisions get a deterministic 1-based ordinal in SsKey order
(`IX_ScalarGallery_Code_1/_2/_3`) — inserting an index can renumber siblings. Authored names are
discarded (`UIX_ScalarGallery_Amount_Tuned` → `UIX_ScalarGallery_Amount`;
`Projection.Core/IndexNaming.fs:34-67`). A named re-open trigger exists for an
`overrides.indexNames` axis if authored names must survive. **Note:** `THE_GOLDEN_EMISSION.md:170`
still says this is TODO — stale; DECISIONS 2026-07-01 + re-recorded goldens are current.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**A4. DEFAULT constraint names: pass-through only; unnamed defaults stay anonymous** — [HARD]
A `CONSTRAINT [DF_…]` is emitted only when the source carried a name (SQL Server auto-names
`DF__*` are filtered out at the reader); otherwise the DEFAULT deploys anonymous and **the server
generates a per-environment name** — perpetual schema-compare noise across environments. There is
no synthesis fallback knob. (`ScriptDomBuild.fs:424-433`; golden `dbo.ScalarGallery.sql:11,26`
shows both shapes.) SSDT teams typically want every default named — this is a prime Modify
candidate (synthesize `DF_<Table>_<Column>` when the source name is absent).
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**A5. CHECK constraint names: pass-through only; anonymous allowed; untrusted state not reproduced** — [HARD]
Same server-auto-name drift as A4 for unnamed CHECKs. Additionally `ColumnCheck.IsNotTrusted` is
carried in the IR but never emitted — an untrusted source CHECK deploys **trusted** (validation
runs against existing data at deploy and can fail); only FKs get the NOCHECK two-step (B6). A
CHECK body that fails ScriptDom parse falls back to a degenerate `'text' = 'text'` expression
instead of refusing (`ScriptDomBuild.fs:456-487`).
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**A6. The >128-char identifier budget: 115-char head + `_` + 12-hex SHA-256** — [HARD]
Applies to *generated* PK/FK/index names only (`Coordinates.fs:99-107`; visible in the golden:
`FK_…_P_b94286649f49`, exactly 128 chars). Pass-through DF_/CK_/trigger names get no cap — a
>128 authored name would fail at deploy.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**A7. No UNIQUE constraints are ever emitted — uniqueness is realized as `CREATE UNIQUE INDEX`** — [HARD]
Source UNIQUE *constraints* are flattened to unique indexes (`sys.key_constraints` →
`sys.indexes`; the constraint identity is lost). Schema compare against the legacy DB will show
the object-class change.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

### B. DDL shape & style

**B1. Constraint placement: the inline ladder** — [HARD, operator-blessed]
Single-column PK/FK/DEFAULT/CHECK render inline beneath their column at +4/+8/+12 indentation;
several constraints on one column stack into one laddered statement; composite PK and
multi-column CHECK go table-level. Composite PK omits the `CLUSTERED` keyword while single-column
PK states it — deploy-equivalent, declaratively asymmetric. CHECK inlining is decided by a
bracketed-token heuristic (exactly one referenced column).
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**B2. `emission.renderConstraintsElegant` (default `true`)** — [KNOB]
The ladder is a **text post-processor over ScriptDom output** (`ConstraintFormatter.fs`), pinned
to ScriptDom 170.23.0's output shape. It also performs semantic-adjacent normalization: when only
one of ON DELETE / ON UPDATE is present the other is backfilled as explicit `NO ACTION`, and when
both are NO ACTION both are dropped. `false` bypasses everything (compact one-line constraints,
raw explicit `ON DELETE NO ACTION`) — all-or-nothing, intended as the V1-parity bisect lever.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**B3. GO framing** — [HARD]
`GO` framed by blank lines on both sides; per-table files put GO *between* statements (never
trailing); `stream.sql` keeps a terminal GO (deliberate asymmetry). Files end with newline; no
CRLF anywhere; all golden-enforced as negative invariants.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**B4. Style constants** — [HARD]
UPPERCASE keywords, bracket-quoted identifiers everywhere, 4-space indent, **no semicolons in
DDL** (data lanes do use `;`), `EXECUTE [sys].[sp_addextendedproperty]` wrapped form. Column
alignment inside CREATE TABLE rides an *unpinned* ScriptDom default (a package upgrade would
re-bless every golden).
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**B5. SQL Server 2022 pin (Sql160) — generator, parser, `.sqlproj` DSP, dacpac model** — [HARD, named trigger]
Uniform pin with a standing note to raise a DECISIONS amendment if production confirms a
different target version. **Confirm your target platform before first publish.**
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**B6. Untrusted FK reproduction: the NOCHECK two-step** — [HARD]
`ALTER TABLE … NOCHECK CONSTRAINT fk` then `ALTER TABLE … WITH NOCHECK CHECK CONSTRAINT fk`
(order verified against SQL Server; the second alone is a no-op for `is_not_trusted`). These
imperative ALTERs live in the *table's* object file — fine for script/sqlcmd deployment, unusual
for a declarative dacpac publish; decide whether untrusted-ness should be preserved at all after
eject vs. re-validating once.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**B7. No header comments** — [HARD, tolerance `HeaderCommentsOmitted`]
V1's `/* Source: … */` banners are dropped. Cosmetic.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

### C. Types, nullability, identity, collation

**C1. The OutSystems logical model is the type authority** — [HARD]
Deployed storage evidence is carried and wins for *facets* when present, but ordinary logical
types are **never widened by deployed reality** (only `bt*` reference attributes consult it).
A physically-widened/retyped column emits per the logical model **with no per-column type
diagnostic** (only nullability/identity divergences get warnings).
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**C2. Identifier/reference types → `BIGINT`; `integer` → `INT`** — [HARD]
`identifier`/`autonumber`/entity-reference/`longinteger` all force `BIGINT` (even overriding an
external dbType). Verify against the deployed OSUSR reality (OutSystems runtime ids are commonly
32-bit `INT`) — a blanket BIGINT changes storage, index width, and any external consumers.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**C3. Text realization** — [HARD]
`text(n)` → `NVARCHAR(n)`; **length ≥ 2000 promotes to `NVARCHAR(MAX)`** (V1 threshold);
`email` → `VARCHAR(250)` and `phone` → `VARCHAR(20)` — two ANSI islands in an NVARCHAR schema
(collation/codepage sensitivity, implicit-conversion risk in joins).
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**C4. Temporal & numeric realization** — [HARD]
`datetime` → legacy `DATETIME` (3.33 ms precision; `rtDateTime2` is the path to `DATETIME2`);
`datetime2` → `DATETIME2(7)`; `time` → `TIME(7)`; `currency` → `DECIMAL(37,8)`; `decimal` with
missing precision/scale clamps to `(18,0)`.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**C5. Identity: `Is_AutoNumber` → `IDENTITY (1, 1)`, always seed 1 / increment 1** — [HARD]
No reader ever populates a non-default seed (`sys.identity_columns` is not consulted); no
`DBCC CHECKIDENT` is emitted. Matters only for fresh builds + loads (which bracket with
IDENTITY_INSERT anyway), but confirm for any external table with a deliberate seed.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**C6. Nullability = `Is_Mandatory`; the tool never tightens or loosens** — [HARD, decided 2026-06-22]
Config-driven NULL→NOT NULL coercion was deliberately disabled (an accepted no-op): "a
nullability intervention is the team's modeling decision, not the tool's." Consequence:
mandatory-but-physically-nullable estates emit `NOT NULL` where the deployed column was NULL —
legacy NULLs surface as **data violations in `fidelity.json`** and will fail the load, by design.
The fix is upstream in the OutSystems model, not a knob.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**C7. `model.onlyActiveAttributes` (default `true`)** — [KNOB]
Inactive attributes never enter the IR (prevents duplicate columns/FKs and DacFx SQL71508).
OutSystems keeps dropped-attribute columns physically present, so schema compare against the old
DB shows them as to-drop — intended, but the leads should know the drops are coming.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**C8. Collation: emitted per column only when it differed from the *source* DB default** — [HARD/GAP]
Default-collation columns emit no `COLLATE` and inherit whatever the *target* database default
is at deploy; the `.sqlproj` pins `ModelCollation = 1033, CI` only. Collation drift is invisible
to the round-trip proof (no collation field in the comparator). **Pin the target database
collation as a deployment prerequisite (J2).**
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**C9. DEFAULT literals: authored channel only** — [HARD]
Only the OutSystems-authored default flows; a deployed hand-added DEFAULT constraint (e.g.
`getutcdate()`) is **not lifted** (named trigger on record). Empty raw string is the IR's
universal NULL sentinel (see F11).
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**C10. Object classes with partial or no support** — [GAP]
Computed columns emit but `PERSISTED` is lost (reader can't see `is_persisted`); temporal
system-versioning has emit support but **no adapter ever produces the mark** — such tables emit
as plain tables silently; sequences are silently absent from model-sourced runs;
`ROWGUIDCOL`/`SPARSE`/`ANSI_PADDING`/`FILESTREAM` are unmodeled. None of these carries a
tolerance token. **Inventory the estate for each class before eject** (a simple sys-catalog
sweep) — if present, they need hand-authored objects in the SSDT project.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

### D. Indexes

**D1. The PK is the only possible clustered index** — [HARD]
The IR has no clustered flag; a source clustered non-PK index or nonclustered PK is silently
normalized (no diagnostic, no tolerance token — the one index-area erasure without a name).
PK-less kinds emit as true heaps and must be allow-listed (`overrides.allowMissingPrimaryKey`).
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**D2. `emission.includePlatformAutoIndexes` (default `true` = keep)** — [KNOB]
Platform-auto detection is the `OSIDX_%` name-prefix heuristic on the live path. Keep ⇒ inherit
possibly-redundant OutSystems auto indexes, and (post-A3 renaming) **no provenance marker
survives into the project** — the decision is irreversible from the ejected artifacts alone.
Prune ⇒ FK columns commonly lose their only index and nothing synthesizes replacements (D6).
Recommendation to discuss: keep, and have the leads run their own index rationalization after
cutover with production query stats in hand.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**D3. Index option fidelity is forward-only** — [HARD/GAP, tolerance `IndexOptionsUnreflected`]
FILLFACTOR / PAD_INDEX / IGNORE_DUP_KEY / uniform DATA_COMPRESSION / lock flags / filegroup /
DESC / INCLUDE / filtered predicates all emit (WITH options only when deviating from defaults;
disabled indexes emit `ALTER INDEX … DISABLE` after the CREATEs). But the verification leg
recovers none of them — **post-handoff drift in index options is invisible to the tool; SSDT
schema compare becomes the only guard.**
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**D4. `uniqueIndex` tightening: evidence-driven IX→UIX promotion (additive-only)** — [KNOB]
Never un-uniques a source-unique index. Gotcha: registering the intervention while omitting the
booleans defaults **both** `enforceSingleColumnUnique`/`enforceMultiColumnUnique` to `true`.
Promotion is point-in-time evidence — data can regress after cutover and fail on first duplicate.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**D5. `categoricalUniqueness` is advisory-only** — [KNOB]
Surfaces `UniquenessCandidates` in the fidelity report; never touches DDL.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**D6. Nothing synthesizes FK-supporting indexes** — [HARD]
Emitted indexes = source indexes − PK-backing − pruned platform-auto. New FKs created by E2 may
have no supporting index at all. Plan a deliberate FK-indexing pass post-cutover.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

### E. References & foreign-key policy

**E1. Delete-rule mapping** — [HARD]
`Delete → CASCADE`; `Protect → NO ACTION`; `Ignore → NO ACTION` (FK still created); missing →
NO ACTION; unknown code = hard adapter error (`OssysTranslation.fs:377-395`). Two semantic
shifts to bless: OutSystems `Protect` is an *application-level* blocker that becomes a DB-level
constraint only because the tool creates the FK; `Ignore` (“app tolerates orphans”) becomes an
*enforced* FK. `Delete → CASCADE` is emitted verbatim with **no multiple-cascade-path (msg 1785)
analysis** — some OutSystems-legal shapes will be rejected by SQL Server at deploy.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**E2. Logical-only references emit as real, enforced, trusted FKs by default** — [KNOB via tightening]
The headline transform. With **no** `foreignKey` intervention configured, every deployable
reference — including OutSystems logical-only ones (`HasDbConstraint=false`) — emits as an
inline FK, with no orphan check at emit time; orphans surface as deploy-time failures. The
golden pins this deliberately (logical-only Cascade/SetNull FKs on `Engagement`).
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**E3. The `foreignKey` intervention gate order — and its inversion surprise** — [KNOB]
Gate order: source-backed FKs always emit (regardless of orphans); `enableCreation:false` drops
all logical-only FKs; **with the intervention ON but no live profile, logical-only FKs become
`EvidenceMissing` and are dropped** — turning the knob on without profiling is *more*
conservative than leaving it off. With a profile: orphans + `allowNoCheckCreation:false` ⇒
dropped (named decision); orphans + `true` ⇒ emitted then NOCHECK'd; clean ⇒ emitted. Every
drop/non-introduction is a named diagnostic (`decision.fkDropped` / `decision.fkNotIntroduced`).
**Recommended review posture: production emission runs with the intervention registered AND the
live profiler on, so every FK decision is evidence-named rather than implicit** (see §6).
Binder defaults when registered: `enableCreation=true, allowCrossSchema=true,
allowCrossCatalog=false, treatMissingDeleteRuleAsIgnore=false, allowNoCheckCreation=false`.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**E4. ON UPDATE is preserved from source reflection** — [HARD]
V1 dropped it; V2 carries it (`ON UPDATE CASCADE` visible in the golden). Confirm the estate
wants deployed `ON UPDATE CASCADE` on user FKs preserved rather than normalized.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**E5. Inverse exclusion + collision tripwire** — [HARD]
Derived inverse references never become FKs (pure-target kinds like `User` emit zero FKs); FK
name collisions are loud Errors, not silent dedupe. Bug-fix-class guarantee; nothing to bless.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**E6. Placebo knobs, named** — [KNOB, inert]
`treatMissingDeleteRuleAsIgnore` (unreachable — the IR can't represent "missing"),
`allowCrossCatalog` (IR doesn't model catalogs on references), and
`overrides.circularDependencies.strictMode` (parsed, zero consumers). Documented so nobody
builds confidence on them.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**E7. Composite-PK FK targets: first-leg-only** — [GAP, tolerance `CompositePkFkUnreflected`]
`Reference` is single-column; an FK to a composite-PK target emits only its first leg — which is
*invalid SQL* unless that column has its own unique index. The guard is "OutSystems never does
this" (operator-confirmed), not a refusal. **Inventory composite-PK targets before eject.**
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**E8. Schema cycles: automatic weak-edge resolution; config is annotate-only** — [HARD]
Nullable + NoAction/SetNull edges defer (data phase-2); non-deferrable cycles are a named
refusal telling you which column to make nullable. `overrides.circularDependencies.allowedCycles`
only *acknowledges* (silences the diagnostic) — it does not change ordering. With an unresolved
cycle the flat `stream.sql` falls back to alphabetical order and contains forward FK references
— fine for DacFx publish, broken for linear sqlcmd execution.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**E9. `UserReflow` transform group: opt-in, data-plane, currently near-inert** — [KNOB/GAP]
Remaps `CreatedBy`/`UpdatedBy` *values* across environments (by email match by default); does
not retarget FK DDL. Today the OSSYS adapter never marks `IsUserFk` and the matching-strategy
config was removed, so enabling the group in `full-export` is close to a no-op — the real path
is `transfer --reconcile <UserTable>:<emailColumn>`. Unmatched users = row skipped (diagnosed).
**State which path the org blesses, and require full match coverage before a real reflow.**
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**E10. FK trust after bulk loads** — [DEPLOY]
Default transfer path re-trusts (`WITH CHECK CHECK CONSTRAINT`) after `SqlBulkCopy`; FKs
untrusted *by decision* stay untrusted deliberately. The **streaming and synthetic legs do not
yet wire re-trust** (named follow-on) — big loads need a `sys.foreign_keys.is_not_trusted = 1`
sweep + re-check in the runbook (tolerance token `FkTrustNotRestoredOnBulkLoad` names the
opt-out).
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

### F. Data lanes (static seeds / migration dependencies / bootstrap)

**F1. Three disjoint lanes; bootstrap is the complement** — [KNOB]
`emission.staticSeeds` / `migrationDependencies` / `bootstrap` all default `true`; disjointness
is asserted (a kind claimed twice is a run error). `bootstrapAllData:true` flips to the full
first-deploy snapshot (only Bootstrap fires). The fused `Data/seed.sql` is retired — per-lane
files are the artifacts (stale references survive in a few docs; §8).
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**F2. Seed shape: one idempotent MERGE per kind, `USING (VALUES …)`** — [HARD]
Deterministic row order (PK-first sort); CDC-enabled kinds get a null-asymmetric
change-detection predicate on WHEN MATCHED so idempotent redeploys are CDC-silent (a proven,
canaried property — V1 had the leak). **No `HOLDLOCK`** — single-writer deploy windows are
assumed. Literals: `N'…'` texts, bare `0x…` binary, invariant date formats.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**F3. IDENTITY kinds: `SET IDENTITY_INSERT ON/OFF` bracketing the MERGE in ONE GO batch** — [HARD]
Session-scoped by design (the deploy executor opens a connection per batch). Requires
ownership/ALTER rights on the table in the post-deploy execution context.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**F4. FK cycles in data: phase-1 NULL insert, phase-2 UPDATE re-point** — [HARD]
All phase-1 MERGEs (topological order), then all phase-2 UPDATEs. A deploy failure between
phases leaves visible NULL FKs; rerun converges. NOT-NULL cycles are a named refusal.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**F5. `emission.deleteScope`: opt-in convergent DELETE arm, gated by sign-off** — [KNOB]
Adds `WHEN NOT MATCHED BY SOURCE AND <term predicate> THEN DELETE` per kind that carries every
term column. Since 2026-07-09 it is **refused unless `emission.signoff` carries `delete-scope`**
(the destructive-write greenlight family: `replace/fresh/drops/cdc/identity-insert/delete-scope`).
One global term set per run; terms name the **logical** column (two docs still say physical —
stale, §8). Bootstrap never deletes.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**F6. `emission.dataVerification`: `"standard"` (default) vs `"validateBeforeApply"`** — [KNOB]
The opt-in prepends a symmetric-EXCEPT drift guard (`THROW 50000` before the MERGE) per kind.
Finding for review: in the *inline* form the guard is its own GO batch (doc says same batch) —
under plain `sqlcmd` without `-b` a THROW in batch N does not stop batch N+1. SSDT publish and
the repo's executor fail loud; the staged form is airtight (guard inside the XACT_ABORT TRY).
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**F7. `emission.dataStaging`: `auto` / threshold 1000 / indexThreshold 100000** — [KNOB]
Above 1000 rows a kind stages through `#temp` inside one `SET XACT_ABORT ON` TRY/TRAN batch
(dodges the ~25-30k inline-MERGE plan-complexity wall, error 8623); above 100k the `#temp` gets
a clustered PK index (measured 33-37% faster). Rights: tempdb create + `BEGIN TRAN`; `"inline"`
is the locked-down escape hatch accepting the ceiling. Inline sub-threshold MERGEs are NOT in an
explicit transaction — recovery model is idempotent rerun. **Do not hand-wrap GO-separated
batches in an outer transaction** (IDENTITY_INSERT/#temp are per-connection).
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**F8. Deployment choreography: bootstrap is NOT in the post-deploy** — [HARD, operator decision 2026-06-24]
`Script.PostDeployment.sql` `:r`-includes StaticSeeds + MigrationData only; Bootstrap ships in
the project folder as a `None` item and is a **separate post-publish load step the receiving
team's pipeline must add explicitly**. `Microsoft.Build.Sql` inlines `:r` at build time — editing
`Data/*.sql` after build changes nothing. (This decision currently lives only in a code
docstring, not `DECISIONS.md` — §8.)
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**F9. `overrides.migrationDependencies`: operator-curated rows** — [KNOB]
Logical-keyed JSON; `""` (and JSON null) = NULL; naming a kind excludes the **whole kind** from
Bootstrap — the operator owns completeness for named kinds.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**F10. Determinism & read concurrency** — [KNOB]
Rendered plans are deterministic and dependency-ordered; `emission.dataReadConcurrency` (default
4) parallelizes only row *reads* (measured; inverts past ~4).
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**F11. Empty string → NULL, universally** — [HARD, tolerance `EmptyTextNormalizedToNull`]
The empty raw string is the IR's universal NULL sentinel for every type (including stored `N''`
and zero-length binary). A NOT-NULL Text column with an empty source value fails the load
loudly. **Audit any application semantics that distinguish `''` from `NULL` before trusting
transferred data.** (DDL corner: the golden currently renders an empty-string DEFAULT as
`DEFAULT N''` while two docs still say `DEFAULT NULL` — reconcile at blessing time; §8.)
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

### G. Bundle, `.sqlproj`, dacpac, refactorlog

**G1. Bundle layout: `Modules/<Module>/<Schema>.<Table>.sql`** — [HARD + KNOB]
Folder-per-module (not SSDT's conventional `Schema\Tables\…`); `overrides.emissionFolders`
redirects per entity (basename preserved). Output directory is atomically **replaced** on each
run. Also emitted: `manifest.json`, per-lane `Data/*.sql`, `fidelity.json/.txt`,
`catalog.snapshot.json`, remediation/summary/suggest artifacts.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**G2. `emission.sqlproj` (default `false`): a deliberately minimal SDK project** — [KNOB]
Emits `ProjectionCatalog.sqlproj` (SDK `Microsoft.Build.Sql/2.2.0` pinned; DSP Sql160;
`ModelCollation 1033, CI`; Build-Remove of post-deploy + data lanes; schema files via the SDK
glob). **Not set:** `nuget.config` (required to restore — the repo's own build tests write their
own), SqlCmd variables, publish profiles, `TreatTSqlWarningsAsErrors`/`SuppressTSqlWarnings`,
code analysis, `DacVersion`, `DefaultSchema`, PreDeploy, RefactorLog item. The hand-authored
proving-ground project shows the richer form the leads will likely want. No DECISIONS entry
records the sqlproj feature (§8). **For the eject handoff, `sqlproj: true` is the obvious
setting — the leads receive a buildable project, then harden it (J4).**
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**G3. The refactorlog never reaches the bundle — highest-stakes gap** — [GAP]
Rename detection, cumulative accumulation (stable UUIDv5 operation keys, prior-wins dedupe), and
a byte-deterministic XML renderer all exist and are law-tested — but **no production path writes
`<project>.refactorlog` into the bundle or references it from the emitted `.sqlproj`** (the XML
renderer has zero production callers; entries live in the episode store). Consequence: an
incremental publish of the ejected project DROP+CREATEs on any rename — the "Silent Catastrophe"
the machinery was built to prevent. The eject contract (`THE_USE_CASE_ONTOLOGY` P-7) promises
"the complete accumulated refactorlog" in the terminal package. **Close this before eject, or
the leads must export the log and wire the `<RefactorLog>` item by hand.**
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**G4. `emission.dacpac` (default `false`): dev-tooling, schema-only, content-equality determinism** — [KNOB]
Built in-process via DacFx (`Sql160`), refuses the package if any object would be dropped
(NM-24). No post-deploy embedded (DacFx limitation) — the `.sqlproj` build is the production
package path. Constant `Version=1.0.0.0`; model collation left at DacFx default (not mirrored).
Don't build binary-diff pipelines on it (Origin.xml wall-clock).
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**G5. Two wired-path hazards found during profiling** — [GAP → fix before `sqlproj:true` handoff]
(a) `manifest.remediation.sql` is written unconditionally at bundle root and is **not**
Build-Removed — with findings present it contains active SELECTs the SDK glob would compile as a
schema object (build break). (b) The wired post-deploy `:r` order is **alphabetical**
(`MigrationData` before `StaticSeeds`), while the emitter's doc/tests pin StaticSeeds-first — an
FK-ordering risk if migration rows reference static rows.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**G6. No `CREATE SCHEMA` objects are emitted** — [GAP]
Cross-schema kinds exist (golden `audit.ChangeLog`), but the bundle contains no schema objects —
a fresh SSDT build/publish of a non-dbo estate fails until the leads hand-author
`Security/<schema>.sql`.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**G7. Ancillary emitters** — [info]
`SchemaMigrationEmitter` = imperative ALTER preview/verification lens (never feeds the dacpac;
additive-safe; drops/narrowings refuse without `--allow-drops`). `DockerImageEmitter` (self
standing-up SQL 2022 container with the dacpac baked in) is **dormant** — no knob or CLI verb;
decide ship-or-cut at eject. `PhysicalSchemaReader` is the in-process round-trip backstop.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

### H. Renames, scope, metadata annotations

**H1. Logical-name substitution is default-on with no config off-switch** — [HARD]
"Substitution, not rename": emitted DDL uses logical names; physical→logical fallback is silent
for blank/>128 logical names (one physically-named table in an otherwise logical estate, no
diagnostic). Reverting to physical emission is a recompile, not a knob.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**H2. What the substitution rewrites — and what it doesn't** — [HARD/GAP]
CHECK bodies and index filter predicates are rewritten via bracket-token string replace (a
string literal containing a physical name would also be rewritten — accepted limitation; audit
the real estate). **Trigger bodies are not rewritten** — the golden ships a trigger targeting
the physical table name, i.e. broken DDL for any real triggered table. Column renames are not
operator-configurable at all (no `columnRenames` axis).
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**H3. `overrides.tableRenames`: dual-form, fail-closed; physical form pins** — [KNOB]
Logical `{module,entity}` XOR physical `{schema,table}` source; all misses/ambiguities/dup
targets are named refusals; a physical-form rename pins the kind out of logical substitution.
Renames feed the refactorlog channel (but see G3) and SsKeys are preserved so FKs keep resolving.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**H4. Schema is pass-through; modules ≠ schemas** — [HARD]
Everything lands in the source schema (effectively `dbo`); the only lever is a rename's target
schema. Module-level metadata lands as SCHEMA-level extended properties.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**H5. Scope gates and the A7 polarity trap** — [KNOB]
`model.modules` is the opt-in gate: with an **empty** module list, `includeSystemModules:false`
etc. are inert (a named Info note fires) and an unscoped run **carries the OutSystems system
modules** into the export. `onlyActiveAttributes` alone applies unconditionally. Cross-scope FK
pruning keeps filtered catalogs closed. **Decide explicitly which platform (OSSYS-backed)
entities belong in the ejected estate, and express it as a non-empty `model.modules` list.**
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**H6. Extended properties: descriptions + vendor identity annotations** — [KNOB]
`MS_Description` at table+column from OutSystems designer descriptions. Plus two
`Projection.SsKey`/`Projection.LogicalName` properties per table *and* per column
(`emission.identityAnnotations`, default `true`; `false` is a named downgrade that degrades
rename round-trips to drop+add). Pre-eject they are load-bearing; post-eject they are inert
vendor residue — **decide keep-or-strip at eject** (stripping forfeits future diff/migrate
tooling; keeping means schema-compare noise unless excluded). Legacy `V2.*`-named properties may
survive on environments deployed pre-rename — audit.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**H7. No cross-module table-name collision tripwire** — [GAP]
Two same-named entities in different modules would emit duplicate `CREATE TABLE`s surfacing only
at DacFx build; same-module duplicates would silently last-win in the bundle map. (FK names have
a tripwire; table names don't.) Cheap pre-eject audit: group logical names, assert uniqueness.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

---

## 3 — The accepted-divergence vocabulary (what the tool tolerates, by name)

`emission.tolerance` parses **fail-closed** (unknown token = refused run). Absent ⇒ permissive
(all 10 accepted, every firing reported); `[]` ⇒ strict. The environment ladder
Dev ⊇ QA ⊇ UAT ⊇ PROD=strict is property-tested. The manifest publishes the vocabulary in-band
(`unsupported`); each run's `fidelity.txt` prints the ACCEPTED DIVERGENCES that actually fired.

| Token | Meaning (one line) | Dev-lead action |
|---|---|---|
| `HeaderCommentsOmitted` | No V1 header banners | none |
| `PostDeployForeignKeysSplit` | FK *file placement* is not contract (today all inline; split is the sanctioned fallback if deploy-order failures surface) | compare deployed FK sets, not layout |
| `IndexOptionsUnreflected` | **OpenGap** — canary blind to filter/INCLUDE/options drift | own it via schema compare |
| `StaticPopulationsUnreflected` | seed content not on the schema-compare surface | run the data-leg canary when it matters |
| `EmptyTextNormalizedToNull` | `''` → NULL universally on transfer-write | audit `''`-vs-NULL semantics |
| `CompositePkFkUnreflected` | **OpenGap** — composite-PK FK targets, first-leg-only | inventory composite-PK targets |
| `CharAnsiPaddingTolerated` | char(n) trailing-blank equality; CDC-invisible | none (SQL Server property) |
| `DecimalScaleTolerated` | `1.0` vs `1.00` same stored value | none |
| `FkTrustNotRestoredOnBulkLoad` | names the re-trust opt-out; streaming/synthetic legs always exhibit it today | post-load `is_not_trusted` sweep |
| `TriggerBodyUnparsedDropped` | **OpenGap** — unparseable trigger body omitted from text artifacts **with an in-band marker comment**; dacpac path refuses instead | `grep -r "TriggerBodyUnparsedDropped" <bundle>` before accepting |

Also know the *unnamed* erasures (no token, silent): temporal tables, sequences (model-sourced),
`PERSISTED`, ROWGUIDCOL/SPARSE/FILESTREAM, non-PK clustering, deployed hand-added DEFAULTs (C9,
C10, D1). These are the packet's §8 audit items, not tolerance rows.

---

## 4 — What is proven vs. what is assumed

**Proven, byte-level:** the golden corpus (every text artifact byte-pinned; re-record only via
`GOLDEN_RECORD=1` + DECISIONS note); LF-only, trailing-newline, framed-GO negative invariants;
SsKey-sorted deterministic ordering; T1 bit-identical text output.
**Proven, behavioral:** CDC-silence on idempotent redeploy (canaried); NOCHECK/unique decisions
survive emit→deploy→read-back (falsifiability-armed); bundle-deploy ≡ dacpac-publish
(PhysicalSchema equality, Docker-witnessed); eject self-verifies (replay from genesis must
reproduce the frozen schema — exit 5 otherwise).
**Excluded/assumed:** dacpac bytes (content-equality only — DacFx wall-clock); `manifest.json`
bytes (wall-clock stamp; possibly stale exclusion, §8); the four OpenGap tolerances; emit-path
ACCEPTED DIVERGENCES is a structural report, **not** a round-trip proof (the canary is the
separate `check` verb); publish **rollback** was never proven (forward-only proving ground).

---

## 5 — The eject bill of materials

Per the P-7 contract, the terminal package is: **(a)** the frozen SSDT bundle; **(b)** the
complete accumulated refactorlog (see G3 — wiring gap); **(c)** the full episode chain
(`LifecycleStore`) so any prior state stays reconstructable; **(d)** the terminal ChangeManifest
with any eject-time drops declared; **(e)** the operator-owned provenance declaration.
`projection eject --store <path>` (alias `seal`) self-verifies by reconstruction. Until then the
R6 dual-track holds: V2 emits-but-doesn't-ship; per-pair cutover gates on N=10 consecutive green
canaries + operator sign-off; V1 warm through cutover+30.

Per-run artifacts the leads will see: the bundle + `manifest.json` (with the divergence
vocabulary in-band), per-lane `Data/*.sql`, `fidelity.json/.txt` (data violations + fired
divergences + uniqueness candidates), `catalog.snapshot.json`, and (opt-in) the
`.sqlproj`/post-deploy/dacpac.

---

## 6 — Proposed eject-emission baseline config (strawman to bless)

The defaults are byte-conservative for the dual-track, not necessarily right for the final eject.
Proposed shaping for the handoff emission — every line here is a decision the leads can flip:

```jsonc
{
  "model": {
    "env": "prod",                       // canonical source, named once
    "modules": [ /* EXPLICIT list — decide the platform-entity question (H5) */ ],
    "includeSystemModules": false,
    "includeInactiveModules": false,
    "onlyActiveAttributes": true         // C7: inactive columns are dropped — confirmed?
  },
  "overrides": {
    "tableRenames": [ /* curated; each lands in the refactorlog channel (G3!) */ ],
    "allowMissingPrimaryKey": [ /* audited heap list, not a dumping ground */ ]
  },
  "emission": {
    "ssdt": true,
    "sqlproj": true,                     // G2: hand the leads a buildable project
    "dacpac": false,                     // G4: dev-tooling; the .sqlproj build is the package path
    "includePlatformAutoIndexes": true,  // D2: keep; rationalize post-cutover with prod stats
    "identityAnnotations": true,         // H6: keep until eject; decide strip-or-keep at freeze
    "renderConstraintsElegant": true,
    "staticSeeds": true, "migrationDependencies": true, "bootstrap": true,
    "bootstrapAllData": false,           // F1: complement-only unless first-deploy snapshot wanted
    "dataVerification": "validateBeforeApply",  // F6: drift guard on managed environments — discuss
    "dataStaging": { "mode": "auto" },   // F7: portable default
    "tolerance": [ /* PROD: [] (strict) per the env ladder; name exceptions deliberately */ ]
  },
  "policy": {
    "insertion": "SchemaOnly",
    "tightening": {
      "interventions": [
        { "kind": "foreignKey", "id": "eject-fks",
          "enableCreation": true,        // E2/E3: THE decision — FKs from logical refs, evidence-gated
          "allowCrossSchema": true,
          "allowNoCheckCreation": true } // orphaned FKs become named NOCHECK decisions, not deploy failures
      ]
    }
  },
  "profiler": { "provider": "live" }     // E3: evidence, so FK/unique decisions are named, never implicit
}
```

Open questions this config forces (the four to settle in the review session): the
`model.modules` scope list (H5); `enableCreation` + NOCHECK posture (E2/E3/E1 cascade review);
`dataVerification` on/off per environment (F6); the PROD tolerance set (§3).

---

## 7 — Deployment-side ownership (outside emission — [DEPLOY], all yours after handoff)

Already recommended in-repo (`ssdt-playbook/Foundations/SSDT-Deployment-Safety.md`, standards in
`ssdt-playbook/Reference/SSDT-Standards.md`):

| Publish property | Recommended |
|---|---|
| `BlockOnPossibleDataLoss` | `True` always, every environment — non-negotiable |
| `DropObjectsNotInSource` | `False` UAT+Prod; `True` Dev/Test |
| `IgnoreColumnOrder` | `True` everywhere |
| `GenerateSmartDefaults` | `False` Test+ (`True` Dev only) |
| `AllowIncompatiblePlatform` | `False` |
| `TreatTSqlWarningsAsErrors` | `False` Dev / `True` Test+ |

Known empirics worth keeping: the NULL→NOT NULL data-loss guard fires on *table-has-rows*, not
*column-has-NULLs* — backfilling doesn't appease it; plan explicit pre-deploy migrations for
mandatory-column tightening. `BlockOnPossibleDataLoss` is a global binary, not per-object.

**Unaddressed anywhere in the repo — the leads should pin these before first publish:**
1. Pre-compare noise family: `IgnoreWhitespace`, `IgnoreKeywordCasing`, `IgnoreAnsiNulls`,
   `IgnoreSemicolonBetweenStatements` (relevant: emitted DDL has no semicolons, B4).
2. `DoNotDropObjectTypes` / `ExcludeObjectTypes` (protect: extended properties if keeping H6,
   users/permissions, any hand-added objects like `CREATE SCHEMA` from G6).
3. `ScriptDatabaseOptions`, `VerifyDeployment`, `CommandTimeout` values; deployment contributors.
4. Committed per-environment `.publish.xml` profiles + the `.refactorlog` project item (G3) —
   prescribed by the standards doc, absent from the emitted project.
5. Environment prerequisites the tool does NOT check: server/database collation vs
   `ModelCollation 1033, CI` (C8), compatibility level (emission pins Sql160 — B5), edition,
   `CREATE SCHEMA` for non-dbo schemas (G6), cross-environment user-id reconciliation (E9).
6. SSDT project hardening backlog: `nuget.config`, `DacVersion` stamping, code analysis /
   `TreatTSqlWarningsAsErrors`, SqlCmd variables, PreDeploy hook (G2).
7. Deployment pipeline: post-publish Bootstrap step (F8), post-load FK trust sweep (E10),
   single-writer deploy windows (F2), rollback strategy (never proven — §4).
8. Going forward, adopt the repo's own change-review discipline: **schema changes land as golden
   diffs**; treat any diff under the SSDT project as a contract change and review it as one.

---

## 8 — Inherited worklist: gaps and stale docs (fix-before-eject candidates)

**Functional gaps, ranked:**
1. Refactorlog not wired into the bundle/.sqlproj (G3) — rename = DROP+CREATE post-eject.
2. Trigger bodies keep physical references (H2) — broken DDL for real triggered tables; plus the
   unparseable-body drop marker (§3) to grep for.
3. `manifest.remediation.sql` build-glob hazard + alphabetical post-deploy lane order (G5).
4. No `CREATE SCHEMA` emission (G6).
5. Unnamed erasures inventory: temporal / sequences / PERSISTED / ROWGUIDCOL / SPARSE /
   FILESTREAM / non-PK clustering / hand-added DEFAULTs (C9-C10, D1) — run the estate audit.
6. No cross-module table-name collision tripwire (H7).
7. Composite-PK FK first-leg emission (E7) — audit for zero occurrences.
8. Unnamed-DEFAULT/CHECK server-name drift (A4/A5) — decide on a synthesis fallback.
9. Cascade-path (msg 1785) pre-analysis absent (E1).
10. Streaming-leg FK re-trust unwired (E10).

**Doc drift found while profiling (trust code + DECISIONS over these):**
`THE_GOLDEN_EMISSION.md:170` (index-name synthesis "TODO" — shipped 2026-07-01) and `:129`
(empty-text DEFAULT "renders NULL" — golden renders `N''`); `DeleteScopePolicy`/`CONFIG_REFERENCE`
"terms name PHYSICAL columns" (they resolve logical, post-chain); `Policy.fs` validate-guard
"one GO batch" docstring (inline form is its own batch); retired `Data/seed.sql` references in
`Pipeline.fs`/`DataEmissionComposer.fs`/golden tree doc; the 2026-06-24 bootstrap-out-of-post-deploy
and the sqlproj feature itself have no `DECISIONS.md` entries.

---

## 9 — Suggested review session plan (2 × 90 min)

**Session 1 — the schema contract.** Read `Golden/master/Modules/` top-to-bottom (30 min), then
walk: naming (A1-A7) → spotlight rows 1-4 (H1/H2, E1-E3) → types C1-C6 → indexes D1-D3. Output:
verdicts on every [HARD] row + the E2/E3 FK posture.
**Session 2 — data + project + deployment.** `Data/StaticSeeds.sql` walkthrough → F-register →
G-register (sqlproj/dacpac/refactorlog — G3 decision) → §6 strawman config line-by-line → §7
deployment ownership + §8 worklist assignment. Output: the blessed `projection.json`, the
publish-profile matrix, and owners for each §8 item.

Between sessions, cheap estate audits that de-risk verdicts: sys-catalog sweeps for triggers,
computed/persisted columns, temporal tables, sequences, composite PKs targeted by FKs,
non-`OSIDX_` platform indexes, `''`-meaningful text columns, >128-char names, cross-module
duplicate entity names.

---

*Register short-links: golden corpus `tests/Projection.Tests/Golden/`; config schema
`CONFIG_REFERENCE.md`; decision ledger `DECISIONS.md` (latest-first; Active deferrals index at
top); tolerance vocabulary `src/Projection.Core/Tolerance.fs`; blessing protocol
`THE_GOLDEN_EMISSION.md` §2.*
