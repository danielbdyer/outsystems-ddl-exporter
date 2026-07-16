# SSDT_HANDOFF_REVIEW_PACKET.md — the emission decision register and remediation plan of record

> **Prepared 2026-07-15, revised same day after the deep-dive round** against the tree at
> `ef706ac`. Audience: the SSDT-owning dev leads who will inherit the ejected schema + data, and
> the manager shepherding the handoff. This document now serves **two purposes**: (1) the
> decision register for review — every opinionated emission decision, its current setting, and
> where it lives; (2) **the remediation plan of record** (§10) — the fixes we have already
> decided to make, annotated inline with ⚑ markers. Nothing in §10 is implemented yet; the plan
> is the artifact.
>
> **This document restates for review convenience; when it disagrees with the code or
> `DECISIONS.md`, they win** (the repo's latest-first rule). Line references are as-of the
> commit above. Produced by profiling `Projection.Targets.*`, `Projection.Core`, the adapters,
> `DECISIONS.md`, `CONFIG_REFERENCE.md`, `THE_GOLDEN_EMISSION.md`, the golden corpus — and,
> for the revision, the V1 pipeline at repo root (`src/`, `config/`), the handbook, and the
> platform-reality evidence in `TEMPLATED_LOGIC_AND_BUSINESS_RULES.md` and the fixtures.
>
> **Two companion artifacts** carry the depth this register only summarizes: (1)
> `SCALAR_REPRESENTATION_AUDIT.md` — the per-scalar × per-hop V1/V2 carriage catalog behind rows
> C4/C11; (2) §11 below — the end-to-end full-export → deploy → load → verify operational
> runbook (how the schema + data actually reach the target database), because the emission is
> only half the deliverable.

---

## 0 — How to review with this packet

Every decision carries:

- **an ID** (`A1`, `E2`, …) — cite these in review notes;
- **a class**: **[KNOB]** (selectable in `projection.json`), **[HARD]** (hard-coded; change =
  code + golden re-record + DECISIONS entry), **[GAP]** (open debt), **[DEPLOY]** (deployment-
  side, receiving team owns);
- **a verdict line**. Rows the 2026-07-15 review already decided are marked
  **Decided: planned fix — WP-n** (see §10) or **Locked: approved as-is**; open rows keep
  ☐ Approve ☐ Modify ☐ Discuss.
- **⚑ WP-n** inline = this behavior changes under the remediation plan; the entry describes
  the *current* behavior, the WP describes the *target*.

Three ways to see any decision concretely before blessing it:

1. **The golden corpus** — `tests/Projection.Tests/Golden/master/` is the byte-pinned intended
   output of a catalog containing every emission variance; reading it top-to-bottom *is* the
   review of most rows here.
2. **`projection explain node <projection.json> "<Module>.<Entity>"`** — runs the pipeline with
   your shaping overlays and reports every transform + finding for one entity.
3. **The blessing protocol** — every §10 fix will land as a byte diff on the goldens
   (`GOLDEN_RECORD=1` + DECISIONS note). Your sign-off on the diff is the blessing.

A representative emitted table (`Golden/master/Modules/Relations/dbo.Engagement.sql`):

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

## 1 — What diverges from source, and how to read it

### 1.0 By design — expected transforms (confirm, don't litigate)

These are the product working as intended. They are listed so nobody rediscovers them as
surprises; they need a nod, not a debate.

- **Logical naming throughout (H1).** Emitted DDL uses OutSystems *logical* names —
  `[dbo].[Customer]`, never `[dbo].[OSUSR_ABC_CUSTOMER]`. This is the desired outcome of the
  exporter: the ejected estate reads as the domain model, and the swap to External Entities
  consumes it as such. The mechanics (what gets rewritten where) still have two planned
  completions — trigger bodies and literal-safe rewriting (⚑ WP-6, H2).
- **Synthesized names for generated objects (A1–A3).** Where the platform's names were
  machine noise (`OSPRK_*`, `OSFRK_*`, `OSIDX_*`), the exporter synthesizes readable,
  deterministic names in the logical vocabulary. That regime is expected. What remains under
  review is *convention details inside the regime*: the PK pattern aligns to V1's
  (⚑ WP-8, A1), and unnamed DEFAULTs/CHECKs gain synthesized names (⚑ WP-9, A4/A5).
- **Tightening is the mission, not a side effect (E2).** The product's own name for itself is
  "Extraction, **Tightening**, and SSDT Prep" (`readme.md:1`): the ejected schema is
  deliberately *the schema the estate should have had* — real constraints included — with
  every divergence from deployed reality named, never silent. The open question is not
  *whether* to materialize constraints but *under what evidence regime* (§1.1 row 1).

### 1.1 The divergences that need active blessing (risk-ranked)

| # | Divergence | Register / plan |
|---|---|---|
| 1 | **FK creation posture: the evidence regime inverted between V1 and V2.** V1's shipped default was `EvidenceGated` — "no tightening without proof"; orphaned references withhold DDL until cleaned. V2's shipping default emits **every** deployable reference as an enforced, trusted FK with no orphan check at emit time; the evidence gate is opt-in. Compounding it, the live extraction path currently hardcodes `HasDbConstraint = true` for every reference, erasing the logical-vs-backed distinction the gate needs. | E2, E3 · ⚑ WP-1 |
| 2 | **Delete-rule semantics deviate from database reality.** The platform physically creates: Protect → FK `NO ACTION`; Delete → FK `CASCADE`; **Ignore → no FK at all**. V2 emits an enforced FK for Ignore, and derives `ON DELETE` from the *model's* rule code even though the deployed action is extracted (`#FkReality.DeleteAction`) and then never consulted. No cascade-path (msg 1785) analysis exists. | E1 · ⚑ WP-1 |
| 3 | **Empty string → NULL, universally, on the data plane.** A V2 regression: V1 preserved `N''` (its only coercion was the deliberate single-space sentinel). OutSystems Text has no NULL at the language level — `''` *is* its null value, the platform default-constrains Text columns with `DEFAULT ('')`, and compiled `=''` filters change meaning under NULL. | F11 · ⚑ WP-3 |
| 4 | **Type/nullability authority is the logical model, not deployed reality.** `NOT NULL` where the DB column was NULL (by design, C6); identifiers forced `BIGINT` while the repo's own platform notes record `INT` reality (C2); `email`/`phone` emitted ANSI `VARCHAR` although the platform's own DDL is `NVARCHAR` — the code brands the VARCHAR widths "IMPOSED V1-parity inference, NOT a source-declared fact" (C3 · ⚑ WP-4); deployed type drift undiagnosed (C1). | C1–C6 |
| 5 | **Silently absent object classes.** Temporal tables, sequences (model-sourced runs), `PERSISTED` on computed columns, `ROWGUIDCOL`/`SPARSE`/`FILESTREAM` — and **clustering**: neither pipeline captures `sys.indexes.type_desc`, so a DBA-re-clustered table is silently re-clustered back to PK-clustered on deploy (a data-layout rewrite). | C10, D1 · ⚑ WP-2, WP-5 |
| 6 | **Source UNIQUE constraints are flattened to unique indexes** (object class changes; both pipelines do this — the fix is an upgrade over both). | A7 · ⚑ WP-10 |
| 7 | **Trigger bodies ship with physical table references** (broken DDL for real triggered tables), and the CHECK/filter logical rewrite is textual (string-literal corruption is an accepted limitation). | H2 · ⚑ WP-6 |
| 8 | **Platform-auto (OSIDX) index keep/prune is irreversible post-eject** (no provenance marker survives renaming) — and note V1's shipped default was *prune* (keeping unique ones), while V2's default is *keep all*. | D2 |
| 9 | **Vendor extended properties are added** (`Projection.SsKey`/`LogicalName` on every table and column) — load-bearing pre-eject, residue after; keep-or-strip is an eject decision. | H6 |
| 10 | **Inactive attributes' columns are dropped** from the model (default-on) — correct for eject, but the drops deserve explicit per-entity disposition in the ChangeManifest rather than a silent global filter. | C7 · ⚑ WP-7 |

---

## 2 — Decision register

### A. Identifier & constraint naming

**A1. PK names synthesized — convention aligns to V1** — [HARD] ⚑ **WP-8**
Current V2: `PK_<Schema>_<Table>` (`SsdtDdlEmitter.fs:213`; `PK_dbo_Customer`). V1's convention
is `PK_<LogicalTable>_<KeyCol…>` (`src/Osm.Smo/SmoIndexBuilder.cs:42-55`; test pins
`PK_Customer_Id`), with physical→logical token replacement. Platform names (`OSPRK_*`) are
machine noise and are correctly never passed through by either pipeline.
*Decided (2026-07-15): planned fix — adopt V1's convention (WP-8).*

**A2. FK names synthesized `FK_<OwnerTable>_<TargetTable>_<SourceColumn>`** — [HARD]
Expected under the synthesis regime (§1.0). The IR carries `Reference.Name` from source; honoring
a *curated* name when present is the open WP7 remainder in the reconciliation plan
(`SsdtDdlEmitter.fs:266-307`) — note V1 did honor evidence names, but mutated them (forced `FK`
prefix, appended column segment, 128-capped: `ForeignKeyNameFactory.cs`). Duplicate names are a
loud Error tripwire, not a silent dedupe.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**A3. Index names synthesized `IX_/UIX_<Kind>_<Attrs>` (+ SsKey-ordered `_1/_2/_3` collision ordinals)** — [HARD]
Expected under the synthesis regime (shipped 2026-07-01; `Projection.Core/IndexNaming.fs:34-67`;
`THE_GOLDEN_EMISSION.md:170` still says TODO — stale). Ordinals renumber if a sibling index is
added/removed; a named re-open trigger exists for an `overrides.indexNames` axis if a handful of
semantically-named indexes should survive. V1 likewise regenerated all index names.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**A4. Unnamed DEFAULTs stay anonymous → server-generated names on deploy** — [HARD] ⚑ **WP-9**
Named only when the source carried a name (`DF__*` auto-names filtered at the reader); V1
behaved identically (`CreateTableStatementBuilder.cs:324-335` — no `DF_` synthesis anywhere in
V1). Anonymous defaults deploy as `DF__<random>` per environment — perpetual schema-compare
noise. The fix synthesizes `DF_<Table>_<Column>` when the source name is absent.
*Decided (2026-07-15): planned fix — WP-9.*

**A5. Unnamed CHECKs anonymous; untrusted CHECK state not reproduced; degenerate parse fallback** — [HARD] ⚑ **WP-9**
Same anonymity story as A4 (V1 identical). Additionally `ColumnCheck.IsNotTrusted` is carried
in IR but never emitted — an untrusted source CHECK deploys **trusted** (validation runs at
deploy and can fail); only FKs get the NOCHECK two-step (B6). And a CHECK body that fails
ScriptDom parse falls back to a degenerate `'text' = 'text'` expression instead of refusing
(`ScriptDomBuild.fs:456-487`).
*Decided (2026-07-15): planned fix — WP-9 (synthesize names; reproduce trust state; refuse on
parse failure).*

**A6. The >128 identifier budget: 115-char head + `_` + 12-hex SHA-256** — [HARD] ⚑ **WP-11**
V2's algorithm (`Coordinates.fs:99-107`) is the faithful port of V1's `ForeignKeyNameFactory`
truncation (`TruncateWithHash`, same 115+12 shape). Scope differs: V1 capped **FK names only**
(PK/index names were renamed but never length-checked — a latent deploy-failure hole); V2
already caps all *generated* PK/FK/index names (a safe superset). The shared remaining hole:
**pass-through names** (authored DF_/CK_/trigger names) are uncapped in both — a >128 authored
name fails at deploy. The fix closes that hole with fit-or-refuse.
*Decided (2026-07-15): planned fix — WP-11.*

**A7. No UNIQUE constraints — uniqueness flattened to `CREATE UNIQUE INDEX`** — [HARD] ⚑ **WP-10**
Confirmed V1-parity: V1 also emitted only unique indexes (its sole `UniqueConstraintDefinition`
uses are the PK; extraction captures deployed UNIQUE constraints as index rows with kind `'UQ'`
via `is_unique_constraint = 1`, then tightening/emission treat UQ ≡ unique index). So the
flattening is inherited, not new — and the fix is a deliberate **upgrade over both pipelines**:
DB-level UNIQUE constraints re-emit as constraints (`ALTER TABLE … ADD CONSTRAINT … UNIQUE`),
preserving object class for schema compare and FK-target semantics.
*Decided (2026-07-15): planned fix — WP-10.*

### B. DDL shape & style

**B1. Constraint placement: the inline ladder** — [HARD, operator-blessed]
Single-column PK/FK/DEFAULT/CHECK inline beneath their column at +4/+8/+12; stacks on one
column; composite PK and multi-column CHECK table-level. Composite PK omits the `CLUSTERED`
keyword while single-column PK states it — deploy-equivalent, declaratively asymmetric.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**B2. `emission.renderConstraintsElegant` (default `true`)** — [KNOB]
The ladder is a text post-processor over ScriptDom output (pinned to ScriptDom 170.23.0's
shape) and also normalizes `NO ACTION` clause presence (backfills the missing one of ON
DELETE/ON UPDATE; drops both when both are NO ACTION — V1's convention). `false` bypasses it
entirely — the V1-parity bisect lever.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**B3. GO framing** — [HARD]
`GO` blank-framed both sides; per-table files GO-between-statements never trailing; `stream.sql`
keeps a terminal GO (deliberate asymmetry). LF-only; files end with newline; golden-enforced.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**B4. Style constants** — [HARD]
UPPERCASE keywords, bracket-quoted identifiers, 4-space indent, no semicolons in DDL (data
lanes use `;`), wrapped `EXECUTE [sys].[sp_addextendedproperty]`. CREATE TABLE column alignment
rides an unpinned ScriptDom default (package upgrade would re-bless goldens).
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**B5. SQL Server 2022 pin (Sql160) — generator, parser, DSP, dacpac model** — [HARD, named trigger]
Confirm the production target platform before first publish; a different version needs a
DECISIONS amendment (the trigger is named in code).
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**B6. Untrusted FK reproduction: the NOCHECK two-step** — [HARD]
`NOCHECK CONSTRAINT` then `WITH NOCHECK CHECK CONSTRAINT` (order verified against SQL Server).
Faithful to deployed trust state; note untrusted FKs in an OutSystems estate are DBA-added
anomalies by definition (the platform never creates them) — reflection-preserving them is the
right default. Imperative ALTERs live in the table's object file — fine for script deployment,
unusual for pure declarative publish.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**B7. No header comments** — [HARD, tolerance `HeaderCommentsOmitted`] — cosmetic.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

### C. Types, nullability, identity, collation

**C1. The logical model is the type authority; deployed storage never widens ordinary scalars** — [HARD]
V2 consults `#ColumnReality` only for `bt*` reference attributes. **V1 was more
reality-preferring here**: its `TypeMappingPolicy` resolves the *on-disk* rule first for
ordinary scalars, so a deployed `nvarchar` email column emitted `NVARCHAR` in V1 while V2 emits
the logical mapping. Deployed type drift is undiagnosed (only nullability/identity get
warnings). WP-4 partially restores on-disk precedence (for the email/phone family first) with
divergence diagnostics.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**C2. Identifier/reference types → `BIGINT`** — [HARD]
Both pipelines force BIGINT for `identifier`/`autonumber`/`longinteger` (V1:
`ShouldPreferRuntimeMapping` skips even deployed INT identity; V2: forced in
`OssysTranslation`). But the repo's own platform-reality note records `[ID] INT NOT NULL
IDENTITY(1,1)` with identifier columns typed `INT` (`DECISIONS.md` 2026-05-23 source-semantics
entry), and the code calls BIGINT "a default, not a law". **Verify against the deployed estate
before eject** — a blanket INT→BIGINT changes storage, index width, and external consumers.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**C3. `email` → `VARCHAR(250)`, `phone` → `VARCHAR(20)` — a deviation from platform reality** — [HARD] ⚑ **WP-4**
The platform's own DDL for Email/Phone is **NVARCHAR** (modeled `ossys_User.EMAIL` is on-disk
`nvarchar(250)`; the handbook's guidance table says "Email, Phone Number → NVARCHAR(n)";
`notes/note14.md` lists "Using VARCHAR for User Text" as an anti-pattern). V1's
`email→varchar(250)` rule existed but rarely fired on live runs because on-disk metadata won
(C1); V2 applies the logical mapping directly, so the VARCHAR actually lands. The code itself
brands the widths "IMPOSED V1-parity … NOT a source-declared fact" (`OssysTypeMapping.fs:79-85`).
Two ANSI islands in an NVARCHAR schema = collation/codepage sensitivity, implicit-conversion
risk, and possible non-Latin truncation on round-trip.
*Decided (2026-07-15): planned fix — NVARCHAR(250)/(20) + on-disk precedence (WP-4).*

**C4. `datetime` handling — the type is lane-dependent and V1 *does* cast to datetime2 in data** — [HARD] ⚑ **WP-17**
*(This row supersedes the earlier "V1, V2 and the platform all agree on legacy DATETIME" — that
was true only of V1's DDL type-mapping config. The deep-dive found the story is richer on two
axes; full trace in the companion `SCALAR_REPRESENTATION_AUDIT.md` §5.)*
- **DDL type is lane-dependent.** On the storage-evidence lane (live OSSYS) `DateTime →
  DATETIME` (legacy, `ScriptDomBuild.fs:218`). On the `PrimitiveType`-fallback lane (no storage
  evidence — the catalog-direct **goldens**, ReadSide-derived catalogs, JSON without
  `SqlStorage`) `DateTime → DATETIME2` (`ScriptDomBuild.fs:129`, `SqlStorageType.fs:128`). So the
  same logical model emits `DATETIME` from a live export and `DATETIME2` from the golden/fallback
  path — **the goldens show `DATETIME2`.** The reviewer's "does it coerce to DATETIME2?" instinct
  was right: the coercion is real, in the fallback lane.
- **V1 pivots to datetime2 in the *data*.** V1's seed literal is `CAST('…fffffff' AS
  datetime2(7))` (`src/Osm.Emission/Formatting/SqlLiteralFormatter.cs:90`; `AS date` / `AS
  time(7)` likewise). V2 emits a bare `'…fffffff'` string (`ScriptDomBuild.fs:306-310`) and
  relies on implicit conversion. V1's explicit CAST is precision-explicit and language-
  independent; V2's bare form is a `SET DATEFORMAT` boundary case against legacy `DATETIME`.
- **Round-trip is nonetheless preserved** — the staged `#temp` is typed from `a.SqlStorage`
  (`StagedMerge.fs:51`), so the 7-digit raw converts to the true column type on INSERT and the
  MERGE compares like-typed; a legacy-datetime value only ever had 1/300s precision. No silent
  *data* loss; the divergences are the DDL *type* and the literal *form*.
**Recommendation (adopted into WP-17):** keep the target type at **database reality**
(`DATETIME` on the storage lane) but (1) fix the fallback lane so a storage-evidence-less catalog
does not silently upgrade to `DATETIME2` (align it to the legacy default, or refuse datetime
without evidence — this also stops the goldens misrepresenting a live export); and (2) adopt
V1's explicit `CAST(… AS datetime2(7))` seed-literal form for language-independence. The
post-cutover `DATETIME2(3)` modernization stands as a later dev-lead-owned migration. Related
facts unchanged: `datetime2` sources emit `DATETIME2(7)`; `time → TIME(7)`; `currency →
DECIMAL(37,8)`; bare decimal clamps `(18,0)`; `text ≥ 2000` → `NVARCHAR(MAX)` (inclusive,
V1-identical).
*Decided (2026-07-15): planned fix — WP-17 (fallback-lane datetime default + explicit-CAST seed
literals), with the broader scalar-fidelity gaps it belongs to.*

**C5. Identity: `Is_AutoNumber` → `IDENTITY (1, 1)` fixed** — [HARD]
No reader consults `sys.identity_columns`; no `DBCC CHECKIDENT` emitted. Matters for fresh
builds + loads only (IDENTITY_INSERT brackets preserve values). Confirm for any external table
with a deliberate seed.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**C6. Nullability = `Is_Mandatory`; the tool never tightens or loosens** — [HARD, decided 2026-06-22]
Given (per review): the OutSystems model is authoritative; config-driven NULL→NOT NULL coercion
stays disabled; legacy NULLs surface as data violations in `fidelity.json` and the fix is
upstream in the model. Note the platform norm this rides on: `isMandatory` is logical — deployed
Text columns are typically `NULL` with `DEFAULT ('')`.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**C7. `model.onlyActiveAttributes` (default `true`) — inactive columns dropped** — [KNOB] ⚑ **WP-7**
Correct for eject (prevents duplicate columns/FKs, DacFx SQL71508), but today it is a silent
global filter. The fix makes the disposition explicit: an inactive-attribute inventory per
entity, drops declared in the terminal ChangeManifest (the named-erasure discipline), and an
opt-in preserve list for the exceptions.
*Decided (2026-07-15): planned fix — WP-7.*

**C8. Collation: per-column COLLATE only when it differed from the *source* DB default** — [HARD/GAP]
Default-collation columns inherit the *target* DB default at deploy; `.sqlproj` pins
`ModelCollation 1033, CI` only; collation drift is invisible to the round-trip proof. Pin the
target database collation as a deployment prerequisite (J-list).
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**C9. DEFAULT literals: authored channel only; deployed hand-added DEFAULTs not lifted** — [HARD]
(named trigger on record). Platform-shaped Text columns carry `DEFAULT ('')` — see F11/WP-3 for
the empty-string default fidelity question.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**C10. Partially/un-supported object classes** — [GAP] ⚑ **WP-5**
Computed columns emit but `PERSISTED` is lost (reader hardcodes false); temporal has emit
support but no adapter produces the mark (system-versioned tables emit as plain tables,
silently); sequences absent from model-sourced runs; `ROWGUIDCOL`/`SPARSE`/`ANSI_PADDING`/
`FILESTREAM` unmodeled. None carries a tolerance token — silent loss, against the repo's own
named-erasure law.
*Decided (2026-07-15): planned fix — WP-5 (capture-or-refuse, plus estate inventory first).*

**C11. Data-plane scalar carriage: the 28 concrete types collapse to 9 for transport** — [HARD/GAP] ⚑ **WP-17**
The DDL sees both the semantic `PrimitiveType` (9) and the concrete `SqlStorageType` (28); the
**data plane sees only `PrimitiveType`** — a `CellValue` carries the 9-way category + a raw
string, never the concrete type (`Bulk.fs:27,78`). So every rich type is collapsed by
`SqlStorageType.toPrimitiveType` (`SqlStorageType.fs:79-108`) before it can be a value. Four
collapses are not faithful: **`Float`/`Real`** → the `Decimal` carrier (≈15-digit truncation +
overflow above ≈7.9E28; V1 carried native `double`/`float` at G17/G9); **`DateTimeOffset`** →
`DateTime` (offset dropped, and readback `Convert.ToDateTime` on a `DateTimeOffset` throws);
**`Xml`** → `Text` (re-serialization, empty-xml erased, and a CDC-enabled kind builds `T.[c] <>
S.[c]` which `xml` cannot compile). All four share one trait — **OutSystems has no native type
that produces them; they arrive only via DBA columns or External Entities**, i.e. exactly the
cutover boundary the product serves. Plus two V1/V2 literal-form divergences: V1 wraps temporal
literals in explicit `CAST` (C4) and escapes CR/LF/TAB into `CHAR()` concatenation, where V2
embeds raw control characters in `N'…'`. **Full per-type × per-hop trace, V1 vs V2, is the
companion `SCALAR_REPRESENTATION_AUDIT.md`** (the standalone research artifact); its §7 hazards
map to WP-17 and its §8 names the unwitnessed types (`Float`/`Real`/`DateTimeOffset`/`Xml`/
`Image`/`SmallDateTime` have no round-trip fixture today).
*Decided (2026-07-15): planned fix — WP-17 (faithful-or-refuse carriage for the collapsing
concrete types; explicit-CAST temporal literals; control-char escaping; the fixture backlog).*

### D. Indexes

**D1. Clustering: PK hard-wired as the only clustered index** — [HARD/GAP] ⚑ **WP-2**
Platform reality: Service Studio cannot author clustered indexes at all — the handbook glossary
marks "Clustered index … **(Hidden)**", and platform convention is PK-clustered. So the
*platform* never creates the shapes V2 can't represent — but **DBAs do** (re-clustering a large
table on tenant/date keys is a real on-prem pattern, invisible to the platform model), and
neither V1 nor V2 captures `sys.indexes.type_desc` (V1's own `IsClustered` domain field is
aspirational, never populated). Consequence today: deploying the ejected project over a
DBA-re-clustered table silently re-clusters it back to the PK — a physical data-layout rewrite
with no diagnostic and no tolerance token.
*Decided (2026-07-15): planned fix — WP-2 (capture clustering + heaps at extraction; emit
`CLUSTERED`/`NONCLUSTERED` per reality; diagnostic in the interim).*

**D2. `emission.includePlatformAutoIndexes` (default `true` = keep)** — [KNOB]
Detection is the `OSIDX_%` name-prefix heuristic. Keep ⇒ inherit possibly-redundant auto
indexes; prune ⇒ FK columns commonly lose their only index (nothing synthesizes replacements,
D6). Two V1 divergences to weigh: V1's shipped default was **prune** (`includePlatformAutoIndexes:
false` in `config/default-tightening.json`), and V1's prune **kept unique** platform-auto
indexes (`SmoIndexBuilder.cs:74-77` drops only non-unique ones) — V2 prunes all flagged. Also:
post-renaming, no provenance marker survives into the project, so keep-vs-prune is irreversible
from the ejected artifacts alone.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**D3. Index option fidelity is forward-only** — [HARD/GAP, tolerance `IndexOptionsUnreflected`]
Options emit faithfully (FILLFACTOR/PAD_INDEX/IGNORE_DUP_KEY/uniform DATA_COMPRESSION/locks/
filegroup/DESC/INCLUDE/filtered; disabled → post-CREATE `ALTER INDEX … DISABLE`), but the
verification leg recovers none of them — post-handoff drift is invisible to the tool; SSDT
schema compare is the only guard.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**D4. `uniqueIndex` tightening: evidence-driven IX→UIX promotion (additive-only)** — [KNOB]
Never un-uniques. Gotcha: registering the intervention without the booleans defaults both
`enforceSingleColumnUnique`/`enforceMultiColumnUnique` to `true` (V1's shipped default was also
both-true). Promotions are point-in-time evidence.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**D5. `categoricalUniqueness` advisory-only** — [KNOB] — fidelity-report candidates; never DDL.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**D6. Nothing synthesizes FK-supporting indexes** — [HARD]
Emitted = source − PK-backing − pruned. New FKs from tightening may be unindexed; plan a
deliberate FK-indexing pass post-cutover.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

### E. References & foreign-key policy

**E1. Delete-rule semantics — current mapping vs database reality** — [HARD] ⚑ **WP-1**
What the platform physically creates (repo's own evidence — `TEMPLATED_LOGIC_AND_BUSINESS_RULES.md:529-534`,
fixtures, and the platform-shaped seed): **Protect → real FK, `ON DELETE NO ACTION`** (the
"cannot delete" error is ultimately DB-enforced — correcting this packet's earlier framing of
Protect as application-level-only); **Delete → real FK, `ON DELETE CASCADE`** (with the known
platform caveat that cascaded deletes bypass application logic); **Ignore → no FK at all**
(application-managed; the classic orphan source, also the shape of external/cross-DB refs).
What V2 emits today: the **model's** rule code mapped `Delete→CASCADE, Protect→NO ACTION,
Ignore→NO ACTION` — *with the FK created even for Ignore* (V1's mapping was identical, but its
evidence gate meant Ignore-refs only materialized on proven-clean data). Meanwhile the deployed
FK's actual delete action **is extracted** (`#FkReality.DeleteAction`,
`outsystems_metadata_rowsets.sql:654-660`) **and never consulted** — `ON DELETE` restates the
model while `ON UPDATE` alone is reflection-sourced (E4). No multiple-cascade-path (msg 1785)
pre-analysis exists in either pipeline.
**Recommendation (adopted into WP-1):** for physically-backed FKs, mirror `sys.foreign_keys` —
emit the *reflected* delete action (model code becomes a cross-check diagnostic when they
disagree); treat Ignore/missing-rule references with no physical FK as *expected no-FK cases*
that only the evidence-gated tightening channel may materialize; make
`treatMissingDeleteRuleAsIgnore` real (missing → Ignore semantics = no FK) or delete it; and add
cascade-path (1785) pre-analysis to the backlog as a pre-emit diagnostic — SQL Server will
reject some OutSystems-legal `Delete` topologies at deploy and nothing predicts that today.
*Decided (2026-07-15): planned fix — WP-1; 1785 analysis signaled to backlog.*
*Landed (2026-07-16, WP-1b): the reflected `#FkReality.DeleteAction` now wins the emitted
`ON DELETE` for physically-backed FKs (reader via `OssysTranslation.chooseOnDeleteAction`), with a
named `Warning` on model↔reality disagreement. Still open (WP-1c): Ignore/missing-rule references
still default-emit an FK; the evidence-gated no-FK posture is not yet mandatory.*

**E2. Materializing logical-only references as FKs — the mission, and the regime question** — [KNOB via tightening] ⚑ **WP-1**
Answering the review question "are references *meant* to be logical-only in this estate?":
logical-only (`HasDbConstraint=false`) is a real, expected class, not an anomaly — it is
exactly what the platform produces for Ignore-rule references, references touching External
Entities (the platform never issues DDL against an external database; its references there are
pure metadata), out-of-scope targets, and DBA-touched tables. And **materializing them into
real FKs is the product's declared end state** — "Extraction, Tightening, and SSDT Prep"
(`readme.md:1`), "evidence-gated NOT NULL, UNIQUE, and FK creation" (`readme.md:8`): the
ejected schema is *the schema the estate should have had*, with every strengthening named.
The genuine decision is the **regime**: V1 shipped `EvidenceGated` + `enableCreation: true` —
creation only on proven-clean data, orphans withhold DDL ("no tightening without proof",
`docs/core-flows.md:136`) — while V2's shipping default emits every deployable reference as an
enforced trusted FK with the evidence gate as opt-in. Source-backed FKs always re-emit in both
(correct). **Recommendation (adopted into WP-1): make the V1 regime the mandatory eject
posture** — `foreignKey` intervention + live profiler always on for production emissions, so
every created FK is an evidence-named decision and every withheld one a named refusal.
*Decided (2026-07-15): planned fix — WP-1 (posture + the E3 gate defaults review).*

**E3. The `foreignKey` intervention gate — defaults and surprises** — [KNOB] ⚑ **WP-1**
Gate order: source-backed always emits → `enableCreation` → evidence (no profile ⇒
`EvidenceMissing` ⇒ dropped — so intervention-on-without-profile is *more* conservative than
off) → orphans (`allowNoCheckCreation` decides dropped-vs-NOCHECK'd) → cross-schema check.
Binder defaults when registered: `enableCreation=true, allowCrossSchema=true,
allowCrossCatalog=false, allowNoCheckCreation=false`. Note **V1's shipped `allowCrossSchema`
was `false`** (V2 flipped to true) — revisit under WP-1. **Critical defect found during this
review: the live-snapshot path hardcodes `HasDbConstraint = true` for every reference**
(`MetadataSnapshotRunner.fs:1326`, contradicting its own doc comment; the JSON path correctly
defaults absent→false) — on live extractions every reference presents as source-backed, which
bypasses the entire gate. WP-1 fixes this first; the regime decision is moot until it lands.
*Decided (2026-07-15): planned fix — WP-1.*

**E4. ON UPDATE preserved from source reflection** — [HARD]
The platform never authors ON UPDATE actions (immutable autonumber PKs make update-cascade
meaningless); a non-default action in the estate is DBA-added. V1 dropped the fact; V2
round-trips it. Preserving deployed reality here is the right default — flag any observed
`ON UPDATE CASCADE` for the leads as a curiosity to review, not a normalization target.
*Verdict:* ☐ Approve (recommended) ☐ Modify ☐ Discuss

**E5. Inverse exclusion + FK name-collision tripwire** — [HARD]
Derived inverse references never become FKs (pure-target kinds emit zero FKs); collisions are
loud Errors.
*Locked (2026-07-15): approved as-is.*

**E6. Placebo knobs** — [KNOB, inert] ⚑ **WP-1**
`treatMissingDeleteRuleAsIgnore` (unreachable), `allowCrossCatalog` (IR lacks catalogs on
references), `circularDependencies.strictMode` (parsed, zero consumers). Under WP-1 each is
made real or removed — a fail-closed config surface must not carry decorative switches.
*Decided (2026-07-15): planned fix — WP-1.*

**E7. Composite-PK FK targets: first-leg-only emission** — [GAP, tolerance `CompositePkFkUnreflected`] ⚑ **WP-12**
Single-column `Reference` IR; an FK to a composite-PK target emits only its first leg — invalid
SQL unless that column has its own unique index. Guard is "OutSystems never does this"
(operator-confirmed for native estates), not a refusal — but external/DBA tables can.
*Decided (2026-07-15): planned fix — WP-12 (refuse loudly at emit until multi-leg lands; estate
audit as precondition).*

**E8. Schema cycles: automatic weak-edge resolution; alphabetical fallback breaks linear streams** — [HARD] ⚑ **WP-13**
Nullable+NoAction/SetNull edges defer to data phase-2; non-deferrable cycles are a named
refusal. `allowedCycles` is annotate-only (does not change ordering); `strictMode` is dead
(E6/WP-1). With an unresolved cycle the flat `stream.sql` falls back to alphabetical order and
contains forward FK references — fine for DacFx publish, broken for linear sqlcmd execution.
*Decided (2026-07-15): planned fix — WP-13.*

**E9. `UserReflow`: opt-in, data-plane, currently near-inert** — [KNOB/GAP] ⚑ **WP-14**
Remaps `CreatedBy`/`UpdatedBy` *values* cross-environment; never retargets FK DDL. Today the
OSSYS adapter never sets `IsUserFk` and the matching-strategy config was removed — enabling the
group in `full-export` is close to a no-op; the real path is `transfer --reconcile`. Unmatched
users = row skipped (diagnosed).
*Decided (2026-07-15): planned fix — WP-14 (wire it or formally retire it in favor of the
reconcile path; no half-alive transform groups at eject).*

**E10. FK trust after bulk loads** — [DEPLOY/GAP] ⚑ **WP-15**
Default materialized-transfer path re-trusts post-load; **streaming and synthetic legs do not
yet wire re-trust** (named follow-on) — they always exhibit `FkTrustNotRestoredOnBulkLoad`
today. Until WP-15 lands, big loads need the `is_not_trusted` sweep + `WITH CHECK CHECK
CONSTRAINT` in the runbook.
*Decided (2026-07-15): planned fix — WP-15.*

### F. Data lanes (static seeds / migration dependencies / bootstrap)

**F1. Three disjoint lanes; bootstrap = complement; `bootstrapAllData` flips to full snapshot** — [KNOB]
Disjointness asserted; fused `Data/seed.sql` retired (per-lane files are the artifacts).
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**F2. Seed shape: one idempotent MERGE per kind; CDC-aware predicates on CDC-enabled kinds** — [HARD]
Deterministic row order; CDC-silence on idempotent redeploy is canaried. No `HOLDLOCK` —
single-writer deploy windows assumed.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**F3. IDENTITY kinds: `SET IDENTITY_INSERT` bracket in ONE GO batch** — [HARD]
Requires ownership/ALTER rights in the post-deploy execution context.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**F4. FK cycles in data: phase-1 NULL insert, phase-2 UPDATE re-point; NOT-NULL cycles refuse** — [HARD]
Mid-state visible if a deploy fails between phases; rerun converges.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**F5. `deleteScope`: opt-in convergent DELETE arm, sign-off-gated** — [KNOB]
`WHEN NOT MATCHED BY SOURCE AND <terms> THEN DELETE`; refused unless `emission.signoff` carries
`delete-scope`; terms name the **logical** column (two docs still say physical — stale, §8).
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**F6. `dataVerification`: `standard` vs `validateBeforeApply` (symmetric-EXCEPT guard)** — [KNOB]
Finding: the inline form emits the guard as its own GO batch (docstring says same batch) —
under `sqlcmd` without `-b` a tripped guard may not stop the following MERGE; SSDT publish and
the repo's executor fail loud; the staged form is airtight.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**F7. `dataStaging`: auto / 1000 / 100k thresholds; XACT_ABORT atomic staged batches** — [KNOB]
Rights: tempdb create + `BEGIN TRAN` (`inline` = locked-down escape hatch, ~30k ceiling).
Inline sub-threshold MERGEs are not in explicit transactions; recovery model = idempotent
rerun. Do not hand-wrap GO-separated batches in an outer transaction.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**F8. Choreography: post-deploy carries StaticSeeds + MigrationData; Bootstrap is a separate post-publish step** — [HARD]
`Microsoft.Build.Sql` inlines `:r` at build time. The receiving pipeline must add the bootstrap
step explicitly. (Decision currently recorded only in a code docstring — §8.)
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**F9. `migrationDependencies` file: logical-keyed rows; `""` = NULL; kind-level bootstrap exclusion** — [KNOB]
Naming a kind removes the whole kind from Bootstrap — the operator owns completeness.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**F10. Determinism & read concurrency (`dataReadConcurrency` 4, acquisition-only)** — [KNOB]
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**F11. Empty string → NULL, universally — a V2 regression against both V1 and the platform** — [HARD, tolerance `EmptyTextNormalizedToNull`] ⚑ **WP-3**
The platform: OutSystems Text has no NULL at the language level — `''` *is* the null value; the
runtime writes `''`, never NULL; the platform column shape for optional Text is `NULL` with
`DEFAULT ('')` (fixtures: `[FIRSTNAME] NVARCHAR(100) NULL … DEFAULT ('')`). V1: preserved `N''`
— its only coercion was the deliberate OutSystems single-space sentinel (`" "` → NULL, nullable
columns only, `StaticEntitySeedScriptGenerator.cs:236-244`). V2: the empty raw string is the
IR's *universal* NULL sentinel for every type — so `''` is erased on transfer-write, three
distinct meanings collapse (`EXECUTION_PLAN.md:960-966`), compiled `=''` filters change results
under NULL semantics, tightened NOT-NULL loads fail on values that were never NULL, and
`DEFAULT ('')` fidelity wobbles (golden currently renders `DEFAULT N''`; two docs say `DEFAULT
NULL` — reconcile at blessing).
*Decided (2026-07-15): planned fix — WP-3 (preserve `''` end-to-end; distinct empty-vs-NULL
representation; deliberate single-space-sentinel handling; retire the tolerance).*

### G. Bundle, `.sqlproj`, dacpac, refactorlog

**G1. Bundle layout `Modules/<Module>/<Schema>.<Table>.sql`; atomic replace; `emissionFolders` redirects** — [HARD + KNOB]
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**G2. `emission.sqlproj` (default `false`): minimal SDK project** — [KNOB]
SDK `Microsoft.Build.Sql/2.2.0`, DSP Sql160, `ModelCollation 1033, CI`, lane Build-Removes —
and nothing else (no `nuget.config` though restore requires one, no SqlCmd vars, publish
profiles, warnings-as-errors, code analysis, `DacVersion`, PreDeploy, RefactorLog item). For
the eject handoff `sqlproj: true` is the obvious setting; hardening is the J-list.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**G3. The refactorlog never reaches the bundle — highest-stakes wiring gap** — [GAP]
Rename detection, stable-UUIDv5 accumulation, and the XML renderer all exist and are tested —
but no production path writes `<project>.refactorlog` or the `.sqlproj` item. Incremental
publish of the ejected project DROP+CREATEs on any rename. The eject contract (P-7) promises
the complete accumulated refactorlog in the terminal package — close before eject.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**G4. `emission.dacpac` (default `false`): dev-tooling, schema-only, content-equality determinism** — [KNOB]
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**G5. Two wired-path hazards** — [GAP → fix before `sqlproj:true` handoff]
(a) `manifest.remediation.sql` written unconditionally at bundle root, not Build-Removed — with
findings it would compile as a schema object and break the build; (b) wired post-deploy `:r`
order is alphabetical (Migration before Static) against the emitter's documented deploy order.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**G6. No `CREATE SCHEMA` objects emitted** — [GAP]
Non-dbo estates don't build/publish until schema objects are hand-authored.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**G7. Ancillary emitters** — [info]
`SchemaMigrationEmitter` = ALTER preview/verification lens (never feeds dacpac);
`DockerImageEmitter` dormant (ship-or-cut at eject); `PhysicalSchemaReader` = in-process
round-trip backstop.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

### H. Renames, scope, metadata annotations

**H1. Logical-name substitution — by design, default-on** — [HARD, expected]
The desired outcome (§1.0). Residual sharp edges, kept visible: no config off-switch
(recompile-only fallback to physical), and blank/>128 logical names silently keep the physical
name with no diagnostic (worth a cheap tripwire when convenient).
*Verdict:* ☐ Approve (expected) ☐ Discuss edge handling

**H2. What the substitution rewrites — and what it doesn't** — [HARD/GAP] ⚑ **WP-6**
CHECK bodies and index filters are rewritten via bracket-token string replace (string-literal
corruption is an accepted limitation); **trigger bodies are not rewritten** — the golden ships
a trigger targeting the physical table (broken DDL for real triggered tables). Column renames
are not operator-configurable (no `columnRenames` axis).
*Decided (2026-07-15): planned fix — WP-6 (ScriptDom-based, literal-safe rewrites incl. trigger
bodies; refusal parity with the dacpac path for unparseable bodies).*

**H3. `tableRenames`: dual-form, fail-closed; physical form pins** — [KNOB]
Renames feed the refactorlog channel (see G3) and SsKeys are preserved.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**H4. Schema pass-through; modules ≠ schemas** — [HARD]
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**H5. Scope gates and the A7-polarity trap** — [KNOB]
Empty `model.modules` ⇒ include-flags inert (named note) and system modules ride along. Decide
explicitly which platform (OSSYS-backed) entities belong in the ejected estate; express it as a
non-empty modules list.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**H6. Extended properties: MS_Description + `Projection.SsKey`/`LogicalName` on every table and column** — [KNOB]
Load-bearing for rename round-trips pre-eject; inert vendor residue after. Decide keep-or-strip
at eject (stripping forfeits future diff/migrate tooling; keeping needs a schema-compare
exclusion). Audit for legacy `V2.*`-named properties on pre-rename deployments.
*Verdict:* ☐ Approve ☐ Modify ☐ Discuss

**H7. Cross-module table-name collision tripwire** — [GAP → ✅ LANDED] ⚑ **WP-16**
Two same-named entities in different modules would emit duplicate CREATE TABLEs surfacing only
at DacFx; same-module duplicates would silently last-win. FK names have a tripwire; table names
now do too.
*Landed (2026-07-16, WP-16): `SsdtDdlEmitter.tableNameCollisionDiagnostics` raises one Error
(`emit.ssdt.table.nameCollision`) per participating kind, mirroring the FK-name tripwire; wired
into the publish diagnostics chain. DECISIONS 2026-07-16.*

---

## 3 — The accepted-divergence vocabulary (tolerance tokens)

`emission.tolerance` parses fail-closed; absent ⇒ permissive (all 10), `[]` ⇒ strict; env
ladder Dev ⊇ QA ⊇ UAT ⊇ PROD=strict is property-tested; the manifest publishes the vocabulary
in-band; `fidelity.txt` reports what fired per run.

| Token | Meaning | Dev-lead action |
|---|---|---|
| `HeaderCommentsOmitted` | no V1 header banners | none |
| `PostDeployForeignKeysSplit` | FK *file placement* is not contract (all inline today) | compare deployed FK sets, not layout |
| `IndexOptionsUnreflected` | **OpenGap** — canary blind to filter/INCLUDE/options drift | own via schema compare |
| `StaticPopulationsUnreflected` | seed content not on the schema surface | data-leg canary when it matters |
| `EmptyTextNormalizedToNull` | `''` → NULL on transfer-write — **slated for retirement under WP-3** | audit `''`-vs-NULL semantics meanwhile |
| `CompositePkFkUnreflected` | **OpenGap** — first-leg-only composite-PK FKs — **WP-12 upgrades to refusal** | inventory composite-PK targets |
| `CharAnsiPaddingTolerated` | char(n) padding equality; CDC-invisible | none |
| `DecimalScaleTolerated` | `1.0` vs `1.00` same stored value | none |
| `FkTrustNotRestoredOnBulkLoad` | names the re-trust opt-out; streaming/synthetic legs always exhibit it — **WP-15 wires them** | post-load `is_not_trusted` sweep until then |
| `TriggerBodyUnparsedDropped` | **OpenGap** — unparseable trigger body omitted from text artifacts with an in-band marker; dacpac refuses — **WP-6 aligns text path to refusal** | `grep -r "TriggerBodyUnparsedDropped" <bundle>` |

Unnamed erasures (no token — the §10 estate-audit list): temporal, sequences (model-sourced),
`PERSISTED`, ROWGUIDCOL/SPARSE/FILESTREAM, non-PK clustering (WP-2/WP-5 close these),
deployed hand-added DEFAULTs (C9, named trigger).

---

## 4 — What is proven vs. assumed

**Proven, byte-level:** golden corpus (re-record only via `GOLDEN_RECORD=1` + DECISIONS note);
LF-only/trailing-newline/framed-GO invariants; SsKey-sorted determinism; T1 bit-identical text.
**Proven, behavioral:** CDC-silence on idempotent redeploy; NOCHECK/unique decisions survive
emit→deploy→read-back; bundle-deploy ≡ dacpac-publish (PhysicalSchema equality); eject
self-verifies by replay.
**Excluded/assumed:** dacpac bytes (content-equality only); `manifest.json` bytes; the OpenGap
tolerances; emit-path ACCEPTED DIVERGENCES is structural, not a round-trip proof; publish
rollback never proven.

---

## 5 — The eject bill of materials

Terminal package (P-7): frozen SSDT bundle + complete accumulated refactorlog (G3 wiring gap
must close first) + full episode chain (`LifecycleStore`) + terminal ChangeManifest (eject-time
drops declared — WP-7's inactive-attribute dispositions land here) + operator-owned provenance
declaration. `projection eject --store` self-verifies by reconstruction. Until then, R6
dual-track: per-pair cutover gates on N=10 consecutive green canaries + operator sign-off.

---

## 6 — Proposed eject-emission baseline config (strawman to bless)

Updated for the review decisions. Every line is still a lead-flippable choice; ⚑ marks lines
whose semantics change under §10.

```jsonc
{
  "model": {
    "env": "prod",
    "modules": [ /* EXPLICIT list — settle the platform-entity question (H5) */ ],
    "includeSystemModules": false,
    "includeInactiveModules": false,
    "onlyActiveAttributes": true         // C7 ⚑ WP-7: dispositions become explicit in the ChangeManifest
  },
  "overrides": {
    "tableRenames": [ /* curated; refactorlog channel — G3 must be wired */ ],
    "allowMissingPrimaryKey": [ /* audited heap list */ ]
  },
  "emission": {
    "ssdt": true,
    "sqlproj": true,                     // hand the leads a buildable project (harden per J-list)
    "dacpac": false,
    "includePlatformAutoIndexes": true,  // D2: keep; note V1 shipped prune-but-keep-unique — decide once, it's irreversible
    "identityAnnotations": true,         // H6: keep until eject; strip-or-keep decided at freeze
    "renderConstraintsElegant": true,
    "staticSeeds": true, "migrationDependencies": true, "bootstrap": true,
    "bootstrapAllData": false,
    "dataVerification": "validateBeforeApply",  // F6: drift guard on managed environments — per-env choice
    "dataStaging": { "mode": "auto" },
    "tolerance": []                      // PROD strict; name exceptions deliberately per env ladder
  },
  "policy": {
    "insertion": "SchemaOnly",
    "tightening": {
      "interventions": [
        { "kind": "foreignKey", "id": "eject-fks",   // E2/E3 ⚑ WP-1: the V1 regime, mandatory
          "enableCreation": true,
          "allowCrossSchema": false,     // V1's shipped default; V2's binder default (true) is the deviation
          "allowNoCheckCreation": false } // V1-strict: orphans withhold DDL → remediate data pre-eject.
                                          // Alternative: true ⇒ orphaned FKs become named NOCHECK decisions.
      ]
    }
  },
  "profiler": { "provider": "live" }     // evidence, so every FK/unique decision is named — mandatory posture
}
```

The four decisions this config forces: the `model.modules` scope list (H5); the FK regime —
V1-strict vs NOCHECK-pragmatic (E2/E3, WP-1); `dataVerification` per environment (F6); the
per-environment tolerance ladder (§3).

---

## 7 — Deployment-side ownership ([DEPLOY] — the receiving team's list)

Recommended in-repo (`ssdt-playbook/Foundations/SSDT-Deployment-Safety.md`, standards in
`ssdt-playbook/Reference/SSDT-Standards.md`): `BlockOnPossibleDataLoss=True` always;
`DropObjectsNotInSource=False` UAT+Prod; `IgnoreColumnOrder=True`; `GenerateSmartDefaults`
Dev-only; `AllowIncompatiblePlatform=False`; `TreatTSqlWarningsAsErrors` Test+. Empirics: the
NULL→NOT NULL guard fires on *table-has-rows*, not *column-has-NULLs* — plan explicit pre-deploy
migrations for mandatory-column tightening.

Unaddressed anywhere in the repo — pin before first publish: pre-compare noise family
(`IgnoreWhitespace`/`IgnoreKeywordCasing`/`IgnoreAnsiNulls`/`IgnoreSemicolonBetweenStatements` —
emitted DDL has no semicolons); `DoNotDropObjectTypes`/`ExcludeObjectTypes` (protect extended
properties if keeping H6, hand-added schemas from G6); `ScriptDatabaseOptions`,
`VerifyDeployment`, `CommandTimeout`, contributors; committed per-env `.publish.xml` +
`.refactorlog` project item (G3); environment prerequisites the tool does not check (server/DB
collation vs `ModelCollation 1033, CI`; compat level vs the Sql160 pin; editions; `CREATE
SCHEMA`; cross-env user reconciliation); post-publish Bootstrap step (F8); post-load FK trust
sweep (E10 until WP-15); single-writer deploy windows (F2); rollback strategy (never proven);
and adopting golden-diff-as-change-review going forward.

---

## 8 — Gaps and stale docs (with plan pointers)

**Functional gaps, ranked** (⚑ = in the §10 plan):
1. Refactorlog not wired into bundle/.sqlproj (G3) — close before eject.
2. **Live-path `HasDbConstraint = true` hardcode** (`MetadataSnapshotRunner.fs:1326`) — erases
   the logical-vs-backed distinction; blocks the FK evidence regime. ⚑ WP-1 (first).
3. Trigger bodies keep physical refs + unparsed-body drop marker. ⚑ WP-6.
4. Empty-string → NULL universal erasure (V2 regression). ⚑ WP-3.
5. Delete-rule reality: Ignore→FK-created (⚑ WP-1c, open); reflected `DeleteAction` unused
   (**✅ WP-1b landed** — reader prefers it + named divergence diagnostic); no 1785 analysis (⚑ WP-1e).
6. Clustering not captured — silent re-clustering of DBA-modified tables. ⚑ WP-2.
7. `manifest.remediation.sql` build hazard + alphabetical lane order (G5).
8. No `CREATE SCHEMA` emission (G6).
9. Unnamed erasures: temporal/sequences/PERSISTED/ROWGUIDCOL/SPARSE/FILESTREAM. ⚑ WP-5.
10. email/phone VARCHAR vs platform NVARCHAR. ⚑ WP-4.
11. UNIQUE-constraint flattening. ⚑ WP-10. · Unnamed DF/CK anonymity. ⚑ WP-9. · Pass-through
    >128 names. ⚑ WP-11. · Composite-PK FK legs. ⚑ WP-12. · Cycle-fallback stream order. ⚑ WP-13.
    · UserReflow half-wiring. ⚑ WP-14. · Streaming re-trust. ⚑ WP-15. · Table-name collision
    tripwire. ⚑ WP-16. · Inactive-attribute disposition. ⚑ WP-7. · PK convention. ⚑ WP-8.
12. **Data-plane scalar collapse** (C11, `SCALAR_REPRESENTATION_AUDIT.md`) — `Float`/`Real`
    precision+overflow, `DateTimeOffset` offset-dropped-and-readback-throws, `Xml`
    re-serialize + CDC `<>` compile error, temporal bare-literal vs V1's CAST, and the
    fallback-lane `DateTime → DATETIME2` upgrade. ⚑ WP-17 (bites on DBA/External columns —
    size via the §9 estate inventory).

**Doc drift found while profiling** (trust code + DECISIONS over these): `THE_GOLDEN_EMISSION.md:170`
(index synthesis shipped 2026-07-01) and `:129` (empty-text DEFAULT); `DeleteScopePolicy`/
`CONFIG_REFERENCE` "physical columns" (terms resolve logical); `Policy.fs` validate-guard "one
GO batch" docstring; retired `Data/seed.sql` references; the 2026-06-24 bootstrap decision and
the sqlproj feature lack DECISIONS entries; `MetadataSnapshotRunner.fs:946-947` doc comment
contradicts its own `:1326` behavior.

---

## 9 — Suggested review sessions (2 × 90 min)

**Session 1 — the schema contract.** Golden corpus read-through (30 min) → §1.0 confirmations →
§1.1 rows 1–5 (the FK regime decision is the headline) → open A/B/C/D verdicts.
**Session 2 — data + project + plan.** `Data/StaticSeeds.sql` walkthrough → F/G registers (G3
refactorlog decision) → §6 strawman line-by-line → §7 ownership assignments → **§10 work-plan
walkthrough: confirm scope and sequencing of WP-1 … WP-16**.

Pre-session estate audits that de-risk verdicts: sys-catalog sweeps for triggers,
computed/persisted columns, temporal tables, sequences, composite PKs targeted by FKs, non-PK
clustered indexes and heaps, deployed identifier widths (INT vs BIGINT), email/phone column
types and non-Latin data, `''`-meaningful text columns, >128-char names, cross-module duplicate
entity names, FKs with non-default ON UPDATE or untrusted state, and the
`HasDbConstraint=false` population (per delete rule) — that last one sizes the entire WP-1
decision.

---

## 10 — The remediation work plan (plan of record — nothing here is implemented yet)

Decided at the 2026-07-15 review. Each WP lands with its golden re-record + `DECISIONS.md`
entry per the blessing protocol; tolerance retirements delete their `@ladder` tags in the same
commit. Sequencing note: WP-1(a) — the `HasDbConstraint` fix — precedes everything that reasons
about the logical-vs-backed split.

### Group I — restore database-reality fidelity

**WP-1 · FK reality & regime (E1, E2, E3, E6)** — the headline package.
(a) **✅ LANDED (WP-1a, DECISIONS 2026-07-16).** Fix the live-path `HasDbConstraint = true`
hardcode so the logical-vs-backed distinction survives live extraction (JSON-path parity:
absent → false).
(b) **✅ LANDED (WP-1b, DECISIONS 2026-07-16).** For physically-backed FKs, emit the **reflected**
delete action (`#FkReality.DeleteAction` — was extracted, consumed nowhere); the rowset reader now
prefers it over the model rule via `OssysTranslation.chooseOnDeleteAction`, and
`MetadataSnapshotRunner.deleteRuleDivergences` raises a named `Warning`
(`adapter.ossys.fkReality.deleteActionDivergence`) when model rule code and deployed action
disagree. (No golden change — the corpus is catalog-direct, no `#FkReality` rows.)
(c) References with no physical FK (Ignore, missing rule, external-entity, out-of-scope) are
*expected no-FK cases*: never default-emit; materialization happens only through the
evidence-gated `foreignKey` intervention — restoring V1's `EvidenceGated` posture as the
mandatory eject regime (intervention + live profiler always on).
(d) Make `treatMissingDeleteRuleAsIgnore` real (missing → Ignore semantics) or remove it;
implement-or-remove `allowCrossCatalog`; delete dead `strictMode`; revisit `allowCrossSchema`
default (V1 shipped `false`; V2's binder default `true`).
(e) **Backlog signal (analysis worth doing):** cascade-path pre-analysis — walk the emitted FK
graph for multiple-cascade-path shapes (SQL Server msg 1785) and self-referencing cascade
limits, and report them as pre-emit diagnostics instead of deploy-time failures.
*Done means:* live-vs-JSON extraction parity test on `HasDbConstraint`; goldens show reflected
delete actions; an Ignore-rule fixture emits no FK without an intervention; placebo knobs gone
or functional; 1785 analyzer ticketed separately.

**WP-2 · Clustering fidelity (D1).** Extract `sys.indexes.type_desc` (+ heap detection) per
table; carry a clustered flag in the Index IR (and PK clustered/nonclustered); emit
`CLUSTERED`/`NONCLUSTERED` per deployed reality. Until it lands: a per-table diagnostic when
extraction detects a non-PK clustered index or nonclustered PK (today that fact never leaves
the source DB). Platform context: Service Studio cannot author these — every occurrence is DBA
intent, exactly what must not be silently reverted by a deploy.
*Done means:* fixture with a re-clustered table round-trips; the silent-normalization erasure
gets a token or dies.

**WP-3 · Empty-string preservation (F11).** Distinct empty-vs-NULL representation in the
transfer IR (kill the universal `""`-as-NULL sentinel); preserve `N''` end-to-end on write
(V1 parity); handle the OutSystems single-space sentinel as a *deliberate, documented* rule
(V1: `" "` → NULL on nullable columns only) rather than an accident; reconcile the
empty-string DEFAULT rendering (`DEFAULT ('')` platform shape vs golden `DEFAULT N''` vs stale
`DEFAULT NULL` docs); retire `EmptyTextNormalizedToNull`.
*Done means:* round-trip canary distinguishes `''`/NULL; tolerance token gone; goldens
re-blessed.

**WP-4 · Unicode fidelity for email/phone (C3, C1).** Map `email` → `NVARCHAR(250)`, `phone` →
`NVARCHAR(20)` (platform-native; handbook-aligned); prefer on-disk column reality over the
logical mapping for ordinary scalars (V1's `onDisk` precedence pattern) with a named divergence
diagnostic when they disagree.
*Done means:* goldens show NVARCHAR; a deployed-NVARCHAR fixture emits NVARCHAR even with a
VARCHAR logical rule; divergence diagnostic tested.

**WP-5 · Silent object classes (C10).** Capture `is_persisted` (emit `PERSISTED`); produce
temporal marks in the adapters (emission already supports them); extract sequences on the
OSSYS path; capture-or-refuse ROWGUIDCOL/SPARSE/FILESTREAM (named refusal if unmodeled).
Precondition: the §9 estate inventory says which of these actually exist — scope the WP to
reality.
*Done means:* each class either round-trips or refuses loudly with a named code; no silent
loss remains.

**WP-6 · Rewrite fidelity (H2).** Replace bracket-token string substitution with
ScriptDom-based identifier rewriting (literal-safe) for CHECK bodies and index filters; extend
the rewrite to **trigger bodies** (tables and columns); align the text-artifact path with the
dacpac path on unparseable trigger bodies (refuse, don't marker-drop) — retiring
`TriggerBodyUnparsedDropped`.
*Done means:* the golden trigger targets `[dbo].[ScalarGallery]`; a string literal containing a
physical name survives; both artifact paths refuse identically.

**WP-7 · Inactive-attribute disposition (C7).** Emit an inactive-attribute inventory
(per entity: name, type, last-active metadata); drops declared in the terminal ChangeManifest
as named erasures; opt-in preserve list for exceptions. The global filter stays the default —
it just stops being silent.

**WP-17 · Data-plane scalar fidelity (C4, C11)** — scoped by `SCALAR_REPRESENTATION_AUDIT.md`.
The data plane transports the 9-way `PrimitiveType`, so the 28 concrete `SqlStorageType`s
collapse for carriage (`SqlStorageType.fs:79-108`); four collapses are not faithful and the
temporal literal form diverges from V1. **Design constraint (from the V1-dynamic-path trace,
audit §9):** V1 preserved `float`/`real`/`datetimeoffset`/`money`/`xml` fidelity because it
carried native CLR objects into rendered literals — but V1 had **no CDC-awareness at all**
(insert-if-absent MERGE). V2's raw-string codec is what buys CDC-silence + the single provable
round-trip. So the fix is **not** to revert to object-carriage — it must **add faithful carriers
(or named refusals) for the collapsing types while keeping the raw-string / CDC-silent design.**
Scope:
(a) **`Float`/`Real`** — give the data plane a faithful carrier (a `Float` primitive/raw form at
G17/G9, or refuse `float`/`real` in a data lane with a named code) instead of silently routing
through `Decimal` (truncation + overflow).
(b) **`DateTimeOffset`** — carry the offset (a raw form with `K`, V1's `datetimeoffset(7)` shape)
or refuse; fix the `ReadSide` arm that throws on a boxed `DateTimeOffset` (`ReadSide.fs:628-629`).
(c) **`Xml`** — decide faithful text carriage vs refusal; guard the CDC change-detect predicate
so an `xml` column (no `<>` operator) cannot emit an uncompilable `T.[c] <> S.[c]`.
(d) **Temporal literals** — adopt V1's explicit `CAST(… AS datetime2(7))` / `AS date` / `AS
time(7)` seed-literal form (language-independent, precision-explicit), replacing the bare quoted
string; and fix the fallback DDL lane so `DateTime` without storage evidence defaults to legacy
`DATETIME` rather than `DATETIME2` (aligns the goldens with a live export).
(e) **Text control characters** — escape CR/LF/TAB (V1's `CHAR()` concatenation) rather than
embedding raw control bytes in `N'…'`.
(f) **Fixture backlog** — a round-trip witness for every concrete type that has none today
(`Float`/`Real`/`DateTimeOffset`/`Xml`/`Image`/`SmallDateTime`/`Money`), so the audit's §4
verdicts become test-proven, not code-derived.
*Done means:* the four unfaithful collapses each round-trip or refuse with a named code; temporal
goldens carry the CAST form and a legacy-`DATETIME` fallback; the scalar-audit witness table has
no UNWITNESSED rows. Note the audience caveat: (a)–(c) bite only on DBA/External-Entity columns,
so the §9 estate inventory scopes how much of WP-17 is load-bearing for *this* estate.

### Group II — naming & constraint-object fidelity

**WP-8 · PK naming convention (A1).** Adopt V1's `PK_<LogicalTable>_<KeyCol…>` (e.g.
`PK_Customer_Id`), replacing `PK_<Schema>_<Table>`. One golden re-record; schema embedded in
names disappears.

**WP-9 · DEFAULT/CHECK constraint completeness (A4, A5).** Synthesize deterministic
`DF_<Table>_<Column>` / `CK_<Table>_<Column…>` names when the source name is absent (both
budget-fitted); reproduce untrusted CHECK state (the CHECK analogue of the FK NOCHECK
two-step); replace the degenerate `'text' = 'text'` CHECK parse fallback with a named refusal.
*Done means:* zero anonymous constraints in the goldens; an untrusted CHECK fixture round-trips
its trust bit; parse failure refuses with the object named.

**WP-10 · UNIQUE constraints as constraints (A7).** Extraction already distinguishes kind
`'UQ'`; carry it through the IR and emit `ALTER TABLE … ADD CONSTRAINT … UNIQUE` for DB-level
UNIQUE constraints (unique *indexes* stay indexes). A deliberate upgrade over both pipelines —
V1 flattened too.
*Done means:* a UQ fixture emits a constraint; schema compare against source shows no
object-class change.

**WP-11 · Identifier budget closure (A6).** Fit-or-refuse for pass-through names (authored
DF_/CK_/trigger/index names >128 — today they'd fail at deploy); when FK evidence-name honoring
lands (WP7 remainder), route those through the same shaping + budget discipline V1's
`ForeignKeyNameFactory` applied.

### Group III — machinery completion

**WP-12 · Composite-PK FK targets (E7).** Upgrade the tolerance to a loud emit-time refusal
naming the reference (an invalid first-leg FK must be impossible to ship); multi-leg reference
modeling only if the estate audit finds real occurrences.

**WP-13 · Cycle-fallback ordering (E8).** Under unresolved cycles, keep the stream
dependency-ordered (or flip to the sanctioned post-deploy FK split) instead of the alphabetical
fallback that produces forward references; `allowedCycles` stays annotate-only but says so in
its config docs.

**WP-14 · UserReflow disposition (E9).** Either wire it end-to-end (OSSYS adapter `IsUserFk`
resolution + matching-strategy config restored) or formally retire the transform group and
bless `transfer --reconcile` as the only user-remap path. No half-alive transform groups at
eject.

**WP-15 · Streaming FK re-trust (E10).** Wire the post-load `WITH CHECK CHECK CONSTRAINT`
sweep into the streaming and synthetic realizations (materialized-path parity); until then the
runbook owns it.

**WP-16 · Table-name collision tripwire (H7).** **✅ LANDED (DECISIONS 2026-07-16).** Error (not
last-wins) when two kinds resolve to the same emitted `(schema, table)`; mirror of the existing
FK-name tripwire (`SsdtDdlEmitter.tableNameCollisionDiagnostics` → `emit.ssdt.table.nameCollision`,
one Error per participating kind; wired into the publish diagnostics chain).

---

## 11 — The operational runbook: source estate → target DB with schema + data

The emission is half the deliverable; the leads also inherit the **procedure** to stand the
logically-named schema up and load it. `projection <args>` = `dotnet run --project
src/Projection.Cli --`. A = **automated** by the pipeline once invoked; M = **manual**
operator / receiving-team action. (Traced from `MovementSurface.fs`, `Program.fs`,
`Faces/Export.fs`, `Pipeline.fs`, `Deploy.fs`, `PostDeployEmitter.fs`, `GETTING_STARTED.md`,
`THE_CLI.md`, `V2_PRODUCTION_CUTOVER.md`, `PARTIAL_TRANSFER_RUNBOOK.md`.)

**The blessed shape (D7, `V2_PRODUCTION_CUTOVER.md:206`): the apply phase is external.** The
pipeline *emits*; DacFx / sqlpackage *applies*. The internal executors (`Deploy.executeStream`,
`executeLeveledSeed`) drive the canary, docker, and `--load` legs — not a production schema
create against a customer DB.

| # | Step | Command / tool (A/M) | Consumes → produces | Rights | Gate |
|---|---|---|---|---|---|
| 0 | Toolchain + config | M: `dotnet build`; author `projection.json` (§6) + `secrets/*.conn` | → config | — | `projection check canary fixtures/…` exit 0 |
| 1 | Estate readiness | M runs / A judges: `projection check shape` | live OSSYS → verdict | SELECT on `ossys_*` | exit 0 (5 divergence, 6 unreadable) |
| 2 | **Emission** | M runs / A produces: `projection publish --go` (flow→`PublishBundle`) or `full-export <cfg> --lifecycle-store <p>` | live model + hydrated rows → bundle (`Modules/**.sql`, `Data/{StaticSeeds,MigrationData,Bootstrap}.sql`, `.sqlproj`+`Script.PostDeployment.sql` if `sqlproj:true`, `manifest.json`, `fidelity.*`, `catalog.snapshot.json`) | source SELECT | artifact count; read `fidelity.txt`; `diff` vs prior |
| 3 | Hand-off | M: deliver bundle to Octopus/CI | → CI workspace | — | — |
| 4 | Target prep | M (receiving team): DB exists, collation = `1033 CI`, compat = SQL2022, logins, `CREATE SCHEMA` for non-dbo, `nuget.config` | → deployable target | dbcreator/DDL | — |
| 5 | **Schema + seeds/migration** | M invokes / DacFx executes: `dotnet build ProjectionCatalog.sqlproj` → `sqlpackage /Action:Publish` (profile per §7). Post-deploy (**StaticSeeds + MigrationData only**, inlined at build) runs inside the publish | sqlproj + bundle → schema (logical names) + static/migration rows | DDL; post-deploy needs IDENTITY_INSERT (ownership/ALTER) + `#temp`+TRAN | publish OK; SSDT compare; `check drift` |
| 6a | **Bulk data — script path** | **M: operator runs `Data/Bootstrap.sql` via `sqlcmd -b`** post-publish — *no verb executes this file* | Bootstrap.sql → remaining rows | as step 5 post-deploy | idempotent rerun; `check data` |
| 6b | **Bulk data — pipeline path (alt.)** | M invokes / A executes: live `schema+data` sink + `PROJECTION_ALLOW_EXECUTE=1 projection <flow> --go` → `PublishAndLoad` → `executeLeveledSeed` (Phase-1 levels → Phase-2 levels; parallel within level; CDC-measured) | lanes loaded live; episode recorded | DML + IDENTITY_INSERT/`#temp`; two-key gate | `load.completed`; re-run → 0 (CDC-silent) |
| 6c | Data-only / estate-scale streaming (variant) | M: `check go <flow>` → `… --go` (+ `streaming:true` + `--journal <dir>`) | live rows → sink; `transfer-undo.sql` | DML + `#temp` (no ALTER on the peer path) | board GREEN; report |
| 7 | Post-load hardening | A on materialized transfer (auto re-trust); **M for streaming/synthetic legs + raw bulk**: `is_not_trusted` sweep + `WITH CHECK CHECK CONSTRAINT` (until WP-15); **M**: enable CDC for the SSIS consumer; keep `Projection.*` EPs + schema-compare exclusion | → trusted-FK, CDC-tracked DB | ALTER (trust); db_owner for `sp_cdc_enable_db` | `is_not_trusted = 0` except reproduced NOCHECK FKs (B6) |
| 8 | Verification | M: `check drift`, `check data --before --after`, redeploy CDC-silence (0 changes), `diff` | → proofs | read-only | drift ∅; counts equal; CDC = 0 |
| 9 | Record + steady state | M: `seal <flow>` → `report <flow>`; re-run per release (minimal `B⊖A`); cutover per pair after N=10 green canaries + sign-off (R6) | episodes → change bundle | — | ledger streak; ladder |

**Say-it-loudly gaps in the procedure** (these are why the runbook is manual where it is):

1. **Nothing executes `Data/Bootstrap.sql`** — deliberate (operator decision 2026-06-24, recorded
   only in `PostDeployEmitter.fs:14-17`, no `DECISIONS.md` entry); the post-deploy carries only
   Static + Migration, and the `.sqlproj` `Build Remove`s Bootstrap. The receiving pipeline must
   add the step (or use the `--load` path, whose leveled plan carries the Bootstrap rows).
2. **CDC enablement has no verb and no runbook section** — only the load-premise comment
   (`Pipeline.fs:2920-2924`); who runs `sp_cdc_enable_db/table` on the target is undocumented
   convention.
3. **Streaming / synthetic legs never re-trust FKs** (E10 / WP-15) — the `WITH CHECK CHECK`
   sweep is the operator's until WP-15.
4. **No statistics or `DBCC CHECKIDENT` step anywhere** — receiving DBA hygiene (C5 confirms no
   reseed is emitted; IDENTITY_INSERT preserves values).
5. **Refactorlog not in the bundle** (G3) and **rollback never proven** (§4) — the two eject-time
   must-close items the procedure otherwise assumes.
6. Bundle prerequisites the emission does not supply: `nuget.config` (G2), `CREATE SCHEMA` (G6),
   and the publish profile itself (§7).

The full hop-by-hop trace (both `--load` and transfer variants, per-path rights, and the
verification verb matrix) lives in the procedure research; this table is the operator-facing
distillation.

---

*Register short-links: golden corpus `tests/Projection.Tests/Golden/`; config schema
`CONFIG_REFERENCE.md`; decision ledger `DECISIONS.md` (latest-first; Active deferrals index at
top); tolerance vocabulary `src/Projection.Core/Tolerance.fs`; blessing protocol
`THE_GOLDEN_EMISSION.md` §2; platform-reality evidence `TEMPLATED_LOGIC_AND_BUSINESS_RULES.md`
§delete-rules, `handbook/03-The-Translation-Layer.md`, `ossys-edge-case.seed.sql`; V1 ground
truth `config/type-mapping.default.json`, `src/Osm.Smo/`; scalar carriage
`SCALAR_REPRESENTATION_AUDIT.md` (companion) + `src/Projection.Core/{PrimitiveType,SqlLiteral,
SqlStorageType,RawValueCodec}.fs`, `src/Projection.Pipeline/Bulk.fs`.*
