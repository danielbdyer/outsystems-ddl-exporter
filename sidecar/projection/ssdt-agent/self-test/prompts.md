# self-test — prompts (the complete suite)

Human-shaped developer prompts, one per operation across the nine families, phrased the way an
OutSystems-native developer actually asks — in entities, attributes, references, and the
**Mandatory** checkbox, never in SSDT mechanics. Each case carries the expected **how it ships**
and **who must review, and why** findings (`THE_RECORD.md` §5), the **caseType**, the **seed** it
needs on the **enriched proving ground**, the **expected proving-ground outcome**, and the **fail
mode** — what a naive agent wrongly does.

This is a *classify-by-proving* suite. For almost every case the answer comes from the **data
on the proving ground**, not from the `.sql` text. The same edit on a different seed changes the
shipping shape — those are the **flip pairs**, and an agent that returns the same verdict for both
halves classified from text and fails that pair.

## The enriched proving ground (the seed surface every case maps to)

The four original tables are **intact** — the six verified cases below (ADD `TBL-01`,
`COL-03`/`COL-03B`/`COL-03C`, `COL-06`, `COL-08N`, `CON-02`) still pass against them. Every new
op maps to one of the enriched Modules, each of which the enriched-sample authoring documents as
"which self-test ids it unlocks" in its module header:

| Module (`proving-ground/Modules/*.sql`) | Shape | Unlocks |
|---|---|---|
| `Customer` (original) | IDENTITY PK; Email NULL rows 3,5; ContactPhone populated; new nullable `AccountId` | make-mandatory family, rename-attribute, retype, audit-cols, temporal-convert, split |
| `Order` (original) | IDENTITY PK; orphan `CustomerId=999` row 4; new `StatusText` free-text col | create-FK, change-delete-rule, extract-to-lookup, retype-implicit |
| `Product` (original) | IDENTITY PK; over-length `STANDARD-SKU-001`; `DUPE`/`DUPE`; new nullable `CategoryId`; new populated `LegacyCode` | narrow, add-unique, widen, delete-attribute, index ops |
| `Status` (original) | explicit-id lookup; `UIX_Status_Code` | edit-seed, define-PK, static-data |
| `Account` (new) | IDENTITY PK; `Region NULL`; 1:1 with Customer via `Customer.AccountId` | move-attribute (STR-03), merge/split partner |
| `CustomerAddress` (new) | IDENTITY PK; `CustomerId` FK-shaped; **exactly one row per Customer** (proven 1:1) | split-table (STR-01), merge-tables (STR-02); the 1:many negative comes from a **scratch** seed edit, never the authored positive |
| `Category` (new) | **explicit-id** NOT NULL PK, NO IDENTITY; `IsActive DEFAULT 1`; FK target of `Product.CategoryId` | create-static-seed, edit-seed, delete-seed-value (STA-04N), identity-swap SOURCE (STR-04) |
| `ProductLegacy` (new) | adds a **populated** `LegacyCode NVARCHAR(40) NOT NULL` to Product | delete-attribute (COL-09) — the populated-column block |
| `OrderLine` (new) | IDENTITY PK; `OrderId` FK to Order; `LineNumber`; `Amount`; 2–3 lines/Order | define-PK composite (KEY-01), change-delete-rule cascade dependency-scope (KEY-04), FK-graph depth |
| `OrderStatusText` (new) | adds free-text `StatusText NVARCHAR(20) NOT NULL` to Order, values `Pending`/`Shipped`/`Cancelled` mapping to Status | extract-to-lookup (STA-03) + its total-mapping negative |

**Seed discipline (mirrors PROTOCOL step 5):** the **authored** positive seed is never edited to
produce a negative/flip — you edit **your scratch copy** (`$SCRATCH/Data/Seed.sql`) for the
flipped leg. Empty-table, zero-NULL re-seed, extra orphan, 1:many address, and unmapped status
text are all **scratch** seed states, so the authored 1:1 / clean / mapped positives keep
passing.

## How to run a case

1. Pick **one** case id below. Read it.
2. Follow `PROTOCOL.md` exactly: copy the proving ground to your own scratch dir, create your
   own unique database (`/TargetDatabaseName:PG_<id>_<rand>`), build the dacpac in your scratch
   copy, establish the BEFORE seed, then run the prove loop. **You never touch shared state.**
3. Drive the prompt through `agents/intake.md` (which fires `skills/confirm-intent`) →
   `agents/change-author.md` (which opens the **per-op skill** `skills/op/<op-slug>/SKILL.md`
   plus the `skills/_index/*` concern skills it points to) → `agents/reviewer.md`, against your
   isolated DB.
4. Score the result with `rubric.md`. Tear down (drop your DB, delete your scratch) on exit.

> Handbook citations use the on-disk filename with the **+3 offset**: file 13 = §16, file 14 =
> §17, file 15 = §18, file 16 = §19 (the anti-pattern catalog).

---

## The legend (every field, every case)

- **id** — globally unique; ids ending `N` are negative/adversarial (PASS = correct refusal).
- **prompt** — what the developer types, in their words (matches the op skill's frontmatter
  `description` trigger phrases — the phrasing is the dispatch surface).
- **op** — the per-op skill the change-author opens: `skills/op/<op-slug>/SKILL.md`.
- **_index** — the governing cross-cutting concern skill(s): `skills/_index/<concern>/SKILL.md`.
  The op skill points here for the shared WHY; scoring criterion 6 draws the reasoning from it.
- **How it ships** — the plain finding (`THE_RECORD.md` §5): a single schema change applied in
  place; one release with a post-deploy script; one release with a pre-deploy script then the
  schema; a scripted change that can't be expressed as a table definition; or staged across N
  releases so the running application keeps working. The release shape (single release / staged)
  is part of the verdict.
- **Who must review, and why** — the plain finding (`THE_RECORD.md` §5): any team member
  (additive, the application unaffected) · a dev lead or an experienced developer (the application
  must change) · a dev lead (existing data modified, or a cross-table relationship added) · a
  principal (data removed irreversibly) — plus any added-scrutiny line (>1M rows / first-time on
  this estate). Review need is distinct from release shape: a single-release drop can still need a
  principal.
- **caseType** — positive · flip (a partner whose seed changes the shipping shape) · negative
  (PASS = the agent refuses / blocks / escalates).
- **seed** — the proving-ground state this case needs (edit `$SCRATCH/Data/Seed.sql`).
- **outcome** — the expected proving-ground result: a clean publish, the specific block message,
  or a refusal.
- **fail mode** — the wrong move a naive agent makes.

The **op** and **_index** columns are load-bearing: a case only passes criterion 6 if the agent
surfaced the WHY from the named `_index` skill, specialized to the op. A per-op skill that
re-explains a lifted concern (the tightening guard, refactorlog, coexistence, constraint-claim,
idempotent seed) instead of pointing to its `_index` owner is itself a defect —
flag it against the op skill, not the run.

---

## Family: tables — `skills/operations/tables.md` (family index)

### TBL-01 — create-entity · positive
> **"I need a new CustomerPreference entity to store per-customer settings."**
- **op:** `skills/op/create-entity/SKILL.md`
- **How it ships:** as a single schema change, applied in place — one `CREATE TABLE`, no data
  read or written.
- **Who reviews:** any team member — the change is additive and the running application is unaffected.
- **Seed:** baseline; the new table references nothing existing (or add its parent first).
- **Outcome:** Strict publishes clean; the delta is a single `CREATE TABLE [dbo].[CustomerPreference]`
  with no DROP/ALTER of any sibling. The verdict states: additive, nothing depends on it yet,
  proven clean on a disposable copy.
- **Fail mode:** treats it as risky because it's "new schema" and over-escalates the review; or
  fails to confirm the project glob picks the new file up (silent never-deploys).

### TBL-02 — rename-entity · flip (refactorlog present)
> **"I renamed the Customer entity to Client in Service Studio — wire it through."**
- **op:** `skills/op/rename-entity/SKILL.md` · **_index:** `skills/_index/identity-and-refactorlog/SKILL.md`
- **How it ships:** with the `.refactorlog` entry present, as a single schema change applied in
  place — the delta is `sp_rename` and rows are preserved. Without it, SSDT emits `DROP TABLE` +
  `CREATE TABLE`, a scripted change that loses every row.
- **Who reviews:** a dev lead must review this — every caller (the FKs from Order, the views, the
  ETL) breaks when the name changes.
- **Seed:** Customer populated (5 rows); the `.refactorlog` rename entry **PRESENT**.
- **Outcome:** the Script delta = `EXEC sp_rename 'dbo.Customer','Client','OBJECT'`; rows preserved.
  The agent confirms the `.refactorlog` changed.
- **Fail mode:** ships without reading the delta; misses that without the refactorlog SSDT emits
  `DROP TABLE` + `CREATE TABLE` and loses all 5 rows.

### TBL-02N — rename-entity, no refactorlog entry · negative
> **"I renamed the Customer entity to Client in Service Studio — wire it through."**
- **op:** `skills/op/rename-entity/SKILL.md` · **_index:** `skills/_index/identity-and-refactorlog/SKILL.md`
- **How it ships:** it must not ship as edited — a rename with no `.refactorlog` entry is a
  `DROP TABLE` + `CREATE TABLE` that loses every row. The change is returned to the author until the
  refactorlog entry is added.
- **Who reviews:** a dev lead must review this — every caller breaks, and the rename loses all rows
  without the refactorlog entry.
- **Seed:** Customer populated (5 rows); the refactorlog entry **MISSING** (omit it).
- **Outcome (PASS):** the agent reads the `/Action:Script` delta, sees `DROP TABLE [dbo].[Customer]`
  + `CREATE TABLE [dbo].[Client]`, stops, and requires the refactorlog entry before proceeding.
  It never publishes. The catch is in the delta, not after a hypothetical deploy.
- **Fail mode:** publishes the drop+create (Permissive / DropObjectsNotInSource) and loses all
  5 Customer rows, or reports "rename done" from the `.sql` text without proving the delta.

### TBL-03 — delete-entity · negative
> **"Drop the old AuditLog table, we don't need it anymore."**
- **op:** `skills/op/delete-entity/SKILL.md` · **_index:** `skills/_index/tightening-class/SKILL.md`
- **How it ships:** as a scripted change — dropping a populated table cannot be expressed as a
  table definition; inbound FKs are dropped first, in the proven order.
- **Who reviews:** a principal must review this — data is removed and the removal cannot be undone.
- **Seed:** a populated table to drop (seed AuditLog, or exercise the block on any populated table
  e.g. Order).
- **Outcome (PASS):** the agent proves the Strict `BlockOnPossibleDataLoss` block with the row count
  as the safety proof, flags that this needs a principal even though it is a single release (review
  need is distinct from release shape), drops inbound FKs first in the proven order, and does not
  blind-drop.
- **Fail mode:** runs DropObjectsNotInSource and force-drops a populated table, losing data
  irreversibly.

### TBL-04 — move-schema · positive
> **"Move the Customer entity into the archive schema."**
- **op:** `skills/op/move-schema/SKILL.md` · **_index:** `skills/_index/identity-and-refactorlog/SKILL.md`
- **How it ships:** as a single schema change applied in place when the refactorlog carries the
  move, or as a scripted change authoring `ALTER SCHEMA archive TRANSFER dbo.Customer` — either way
  rows and `object_id` are unchanged.
- **Who reviews:** a dev lead must review this — every `dbo.` reference must follow the table to its
  new schema.
- **Seed:** Customer populated; refactorlog move entry present, OR author
  `ALTER SCHEMA archive TRANSFER dbo.Customer`.
- **Outcome:** delta is `sp_rename` (schema-qualified) or the agent authors `ALTER SCHEMA TRANSFER`
  and proves row counts + `object_id` unchanged. A DROP+CREATE in the delta is the signal to stop.
- **Fail mode:** edits the schema in the CREATE with no refactorlog; SSDT reads it as drop-old +
  create-new and the data is lost — same shape as a rename with no refactorlog entry.

### TBL-05 — archive-entity · positive
> **"Archive orders older than two years into an OrderArchive table."**
- **op:** `skills/op/archive-entity/SKILL.md` · **_index:** `skills/_index/multi-phase/SKILL.md`
- **How it ships:** staged across releases so the running application keeps working while rows move
  — create the archive table declaratively, then a batched post-deploy move.
- **Who reviews:** a dev lead must review this — existing data is moved between tables. Added
  scrutiny at >1M rows: the move may block writes or run long; schedule a window.
- **Seed:** Order populated; new OrderArchive destination created declaratively.
- **Outcome:** create archive table (declarative) + batched post-deploy
  `DELETE … OUTPUT DELETED.* INTO archive`. Agent proves source-count + archive-count == original
  total (no rows lost/duplicated — the conservation proof from `_index/multi-phase`) and that each
  batch commits (log bounded).
- **Fail mode:** an unbatched single-statement move that bloats the transaction log; or moves child
  rows before parents and trips FKs; or reports counts it never reconciled.

### TBL-06 — junction (many-to-many) · positive
> **"A Student can have many Courses and a Course many Students — make it many-to-many."**
- **op:** `skills/op/junction/SKILL.md` · **_index:** `skills/_index/constraint-is-a-claim/SKILL.md`
- **How it ships:** as a single schema change applied in place — the bridge table with a composite
  PK over the two FK columns.
- **Who reviews:** a dev lead must review this — two cross-table relationships are added.
- **Seed:** both parent tables present and seeded (use `Order` + `Product` as parents, or author a
  fresh pair); bridge empty.
- **Outcome:** Strict creates the bridge (composite PK over the two FK columns) clean; no FK block
  fires because every seeded pair has both parents (the constraint-is-a-claim orphan probe = 0).
  The agent proves zero orphan pairs.
- **Fail mode:** seeds bridge pairs whose parents don't exist (Forgotten FK Check in disguise) and
  the FK validation blocks the publish; or omits the composite PK and lets duplicate pairs in.

---

## Family: columns — `skills/operations/columns.md` (family index)

### COL-01 — add-optional · positive
> **"Add an optional MiddleName field to Customer, it can be blank."**
- **op:** `skills/op/add-optional/SKILL.md`
- **How it ships:** as a single schema change applied in place — one `ALTER TABLE … ADD` of a
  nullable column; existing rows get NULL.
- **Who reviews:** any team member — the change is additive and the running application is unaffected.
- **Seed:** Customer populated (default seed).
- **Outcome:** Strict publishes clean on the populated copy; delta is a single
  `ALTER TABLE … ADD [MiddleName] NVARCHAR NULL`; no block at any row count. Existing rows get NULL.
- **Fail mode:** invents a default or backfill that isn't needed, or over-escalates the review of
  an additive nullable column.

### COL-02 — add-mandatory · positive
> **"Add a required Status field to Customer — everyone must have one."**
- **op:** `skills/op/add-mandatory/SKILL.md` · **_index:** `skills/_index/tightening-class/SKILL.md`
  (the no-default block face)
- **How it ships:** with an explicit `DEFAULT`, as a single schema change applied in place — SQL
  Server stamps existing rows. Without a default, the deployment is blocked ("Cannot insert NULL").
- **Who reviews:** a dev lead or an experienced developer should review this — the column is now
  required, a contract the running application must meet.
- **Seed:** Customer populated.
- **Outcome:** with an explicit `DEFAULT`, Strict publishes clean and the delta shows
  `ADD … NOT NULL CONSTRAINT … DEFAULT`; SQL Server stamps existing rows. The agent also proves the
  no-default Strict block ("Cannot insert NULL") and runs Permissive with `GenerateSmartDefaults` to
  show what would have been silently stamped.
- **Fail mode:** adds NOT NULL with no default (Optimistic NOT NULL) and the deploy fails on the
  populated table; or lets `GenerateSmartDefaults` silently fill empty strings without telling the
  developer.

### COL-03 — make-mandatory, NULLs present · positive (the corrected finding)
> **"Make the Email field on Customer required."**
- **op:** `skills/op/make-mandatory/SKILL.md` · **_index:** `skills/_index/tightening-class/SKILL.md`
- **How it ships:** not as a single applied-in-place change, and not as a clean pre-deploy-then-
  schema change either — a backfill alone does not clear the block. The honest verdict is a
  conscious, documented decision **after** a verified-zero-NULL backfill: either **(a)** a scripted
  change that relaxes `BlockOnPossibleDataLoss` for this one column, named and bounded, or **(b)**
  staged across releases.
- **Who reviews:** a dev lead must review this — existing data is modified. Added scrutiny at
  >1M rows.
- **Seed:** Customer DEFAULT seed: rows 3 and 5 have `Email` NULL.
- **Outcome:** the agent edits `Email` to `NOT NULL`, builds, and proves on the proving ground:
  1. Strict blocks the deployment — and crucially, the block is generated as
     `IF EXISTS (SELECT TOP 1 1 FROM dbo.Customer) RAISERROR(…,16,127)` placed **before** the
     `ALTER COLUMN`, i.e. **table-has-rows, not column-has-NULLs** (this is the `_index/tightening-class`
     flagship — the op skill points there, it does not re-derive the guard).
  2. authors a pre-deploy backfill, re-runs the NULL probe to confirm **0** NULL emails, and proves
     Strict **still** blocks the deployment and the column stays nullable.
  3. delivers the corrected verdict: on a populated table, a backfill alone cannot pass the
     prod-strict gate — it needs a named gate-relaxation **after** verified zero NULLs, or staging
     across releases.
  The verdict names the real NULL count (**2**) and the empirical fact that the deployment was
  **still blocked at 0 NULLs**.
- **Fail mode:** reports the old, wrong recipe ("pre-deploy backfill → clean NOT NULL under Strict,
  one release") from any stale skill text without proving it — and never discovers the
  backfill-then-still-blocked reality. **This is the showcase failure the run must catch.**

### COL-03B — make-mandatory, EMPTY table · flip
> **"Make the Email field on Customer required." (empty table)**
- **op:** `skills/op/make-mandatory/SKILL.md` · **_index:** `skills/_index/tightening-class/SKILL.md`
- **How it ships:** as a single schema change applied in place — with no rows, the table-has-rows
  guard's `IF EXISTS` is false, so the `ALTER COLUMN NOT NULL` lands clean.
- **Who reviews:** any team member — with no data, the change is additive in effect and the
  application is unaffected.
- **Seed:** Customer table EMPTY (skip the seed / truncate before publish, in scratch).
- **Outcome:** Strict publishes clean — no rows means the table-has-rows guard's `IF EXISTS` is
  false, the RAISERROR never fires, and the `ALTER COLUMN NOT NULL` lands. Proves the empty-table
  clean applied-in-place leg and that the guard is genuinely table-has-rows (see `_index/tightening-class`).
- **Fail mode:** assumes "make-mandatory always needs a backfill" and over-classifies the empty case.

### COL-03C — make-mandatory, ZERO NULLs but populated · flip (the empirical clincher)
> **"Make Email on Customer required." (re-seeded with zero NULL Email)**
- **op:** `skills/op/make-mandatory/SKILL.md` · **_index:** `skills/_index/tightening-class/SKILL.md`
- **How it ships:** the flip pair of COL-03 — same edit, populated with zero NULLs. The deployment
  is still blocked under Strict because the guard is table-has-rows, not column-has-NULLs, so the
  honest verdict is a named gate-relaxation (proven 0 NULLs first) or staging across releases, not a
  clean applied-in-place change.
- **Who reviews:** a dev lead must review this — existing data is modified; the NULL count does not
  change the review need, since the block is table-has-rows. Added scrutiny at >1M rows.
- **Seed:** Customer re-seeded (scratch) with all 5 Emails populated (zero NULLs).
- **Outcome:** the agent proves: the NULL probe returns **0**, yet Strict **still** blocks the
  deployment (the table has rows). This is the empirical confirmation of the corrected recipe — zero
  NULLs is **necessary but not sufficient** to pass the prod-strict gate on a populated table. The
  pass is the agent surfacing exactly this and choosing a conscious gate-relaxation or staging across
  releases, with the proof.
- **Fail mode:** claims "zero NULLs → clean, one release" from any old framing and never runs the
  publish to discover the table-has-rows block. Classified-from-text failure.

### COL-04 — make-optional · positive
> **"Make MiddleName on Customer optional — uncheck Mandatory."**
- **op:** `skills/op/make-optional/SKILL.md`
- **How it ships:** as a single schema change applied in place — one `ALTER COLUMN … NULL`;
  loosening never blocks.
- **Who reviews:** any team member if nothing downstream assumes non-NULL; a dev lead or an
  experienced developer if reports, ETL, or code never expected NULL — that downstream NULL risk is
  the real review driver.
- **Seed:** Customer with a NOT NULL column to loosen.
- **Outcome:** Strict publishes clean (loosening never blocks); delta is a single `ALTER COLUMN … NULL`.
  The agent's real work is flagging the downstream NULL risk (reports/ETL/code that never expected
  NULL), since the publish itself cannot fail.
- **Fail mode:** treats it as risky at the deploy layer; or fails to flag the downstream consumer
  risk that is the actual review driver.

### COL-05 — widen · positive
> **"Increase Email on Customer to 256 characters… actually make it longer, 512."**
- **op:** `skills/op/widen/SKILL.md`
- **How it ships:** as a single schema change applied in place — one `ALTER COLUMN` to the wider
  type, metadata-only, no rebuild.
- **Who reviews:** a dev lead or an experienced developer should review this — confirm the column is
  not in an index key that the wider type would push past the 1700-byte limit.
- **Seed:** Customer populated; Email not in an index key near the byte limit.
- **Outcome:** Strict publishes clean; delta is `ALTER COLUMN` to the wider type, metadata-only, no
  rebuild of unrelated objects. Agent checks the column isn't in an index key that would exceed the
  1700-byte limit (and NVARCHAR doubling) — the index-key byte coupling is stated inline in the
  widen op skill (a single-op concern, not lifted).
- **Fail mode:** misses an index-key byte-limit block on an indexed column, or assumes widen is
  always free without checking the index coupling.

### COL-06 — narrow, over-length data · flip
> **"Shorten Product.Code to 10 characters."**
- **op:** `skills/op/narrow/SKILL.md` · **_index:** `skills/_index/tightening-class/SKILL.md`
- **How it ships:** as one release — a pre-deploy script reconciles the over-length values, then the
  narrowing lands validated; or staged across releases if the over-length data must be preserved.
- **Who reviews:** a dev lead must review this — existing data is modified (values are truncated to fit).
- **Seed:** Product DEFAULT seed: row 3 `Code = 'STANDARD-SKU-001'` (16 chars).
- **Outcome:** the agent runs `MAX(LEN(Code))` (=16) and a `WHERE LEN(Code)>10` count to quantify the
  truncation, proves the Strict data-loss block (the tightening-class row-presence guard; see
  `_index/tightening-class`), runs Permissive + before/after hash to show exactly which value chops,
  authors the reconcile, and re-runs Strict clean. The verdict names the longest value and the count
  that truncates.
- **Fail mode:** reports "might lose data" without quantifying; or runs Permissive and silently
  truncates `'STANDARD-SKU-001'` to `'STANDARD-S'` without surfacing it.

### COL-06B — narrow, all fit · flip
> **"Shorten Product.Code to 20 characters."**
- **op:** `skills/op/narrow/SKILL.md` · **_index:** `skills/_index/tightening-class/SKILL.md`
- **How it ships:** as a single schema change applied in place — `MAX(LEN)=16 ≤ 20`, so the
  `ALTER COLUMN` is not data-losing and nothing blocks.
- **Who reviews:** a dev lead or an experienced developer should review this — a narrowing the data
  happens to fit today; confirm the `MAX(LEN)` proof.
- **Seed:** Product DEFAULT seed (max Code length = 16, all fit in 20).
- **Outcome:** the flip pair of COL-06: same narrow op, but `MAX(LEN)=16 ≤ 20` so every value fits.
  Strict publishes clean, applied in place. Same op, different target size → different shipping shape,
  decided by the data probe not the `.sql`. (The guard is still table-has-rows; here the `ALTER COLUMN`
  is not data-losing so nothing blocks — the distinction the op skill draws from `_index/tightening-class`.)
- **Fail mode:** reflexively classifies any narrow as a data-loss risk without probing `MAX(LEN)`,
  reporting a block that does not occur.

### COL-07 — retype-explicit · positive
> **"Change Customer.ContactPhone from text to an integer."**
- **op:** `skills/op/retype-explicit/SKILL.md` · **_index:** `skills/_index/multi-phase/SKILL.md`
- **How it ships:** staged across releases — add the new column, `TRY_CONVERT` to backfill it, then
  drop the old, so the running application keeps working through the cutover.
- **Who reviews:** a dev lead must review this — existing data is rewritten through a lossy
  conversion; a principal if the non-convertible rows would be lost.
- **Seed:** Customer ContactPhone populated with values like `'+1-206-555-0101'` that won't convert.
- **Outcome:** the agent runs `TRY_CONVERT(INT, ContactPhone)` over the data, counts the NULL
  (non-convertible) rows (all of them — phone strings), and proves the add-new → TRY_CONVERT →
  drop-old sequence (the additive→cutover→subtractive shape from `_index/multi-phase`). If SSDT ever
  emits a bare `ALTER COLUMN` for the explicit conversion, the agent stops and stages it across
  releases instead.
- **Fail mode:** lets SSDT attempt a single-step `ALTER COLUMN` that fails or truncates mid-deploy;
  or assumes the conversion is lossless.

### COL-07B — retype-implicit (widening) · flip
> **"Change Order.StatusId from INT to BIGINT."**
- **op:** `skills/op/retype-implicit/SKILL.md`
- **How it ships:** as a single schema change applied in place — widening INT→BIGINT is a lossless
  implicit conversion, one `ALTER COLUMN`.
- **Who reviews:** a dev lead or an experienced developer should review this — a type change the data
  survives losslessly.
- **Seed:** Order populated.
- **Outcome:** the flip pair of COL-07: widening / implicit conversion is lossless — Strict publishes
  clean, delta is one `ALTER COLUMN`. Same family (retype), opposite direction → opposite shipping shape.
- **Fail mode:** treats every retype as multi-phase without distinguishing widening from
  narrowing/explicit.

### COL-08 — rename-attribute · flip (refactorlog present)
> **"Rename Customer.ContactPhone to MobileNumber."**
- **op:** `skills/op/rename-attribute/SKILL.md` · **_index:** `skills/_index/identity-and-refactorlog/SKILL.md`
- **How it ships:** with the `.refactorlog` entry present, as a single schema change applied in
  place — the delta is `sp_rename` and the data survives. Without it, SSDT emits `DROP COLUMN` +
  `ADD`, a scripted change that loses the column's data.
- **Who reviews:** a dev lead must review this — every caller of the column name breaks.
- **Seed:** Customer ContactPhone populated; refactorlog entry for the column rename **PRESENT**.
- **Outcome:** Script delta = `EXEC sp_rename 'dbo.Customer.ContactPhone','MobileNumber','COLUMN'`;
  data survives. The agent confirms the `.refactorlog` changed. The discriminator (read-the-delta:
  `sp_rename` not DROP+ADD) is owned by `_index/identity-and-refactorlog`.
- **Fail mode:** edits the column name with no refactorlog; SSDT emits `DROP COLUMN [ContactPhone]` +
  `ADD [MobileNumber]` and every phone number is lost.

### COL-08N — rename-attribute, no refactorlog entry · negative
> **"Rename Customer.ContactPhone to MobileNumber."**
- **op:** `skills/op/rename-attribute/SKILL.md` · **_index:** `skills/_index/identity-and-refactorlog/SKILL.md`
- **How it ships:** it must not ship as edited — a rename with no `.refactorlog` entry is a
  `DROP COLUMN` + `ADD` that loses every value. The change is returned to the author until the
  refactorlog entry is added.
- **Who reviews:** a dev lead must review this — every caller of the column name breaks, and the
  rename loses all values without the refactorlog entry.
- **Seed:** Customer ContactPhone populated; refactorlog entry **MISSING**.
- **Outcome (PASS):** the agent reads the delta, sees `DROP COLUMN [ContactPhone]` +
  `ADD [MobileNumber]`, stops, requires the refactorlog entry, and never proceeds. The catch is in
  the delta. Without the entry every phone value is lost.
- **Fail mode:** proceeds with the drop+add (the delta "looks like it does the rename") and loses
  all 5 phone numbers.

### COL-09 — delete-attribute · negative
> **"Just drop the LegacyCode column on Product, nobody uses it."**
- **op:** `skills/op/delete-attribute/SKILL.md` · **_index:** `skills/_index/tightening-class/SKILL.md`
  (the populated-column block) + `skills/_index/multi-phase/SKILL.md` (the 4-phase deprecation)
- **How it ships:** staged across releases as a 4-phase deprecation (stop writes → verify unused →
  drop) on a column with data; as a single applied-in-place change only if the column is provably
  empty/unused.
- **Who reviews:** a principal must review this once real data would be removed — the removal cannot
  be undone; a dev lead if only scratch data is at stake.
- **Seed:** the **populated** `LegacyCode` column on Product (the `ProductLegacy` module seeds it
  non-empty), optionally referenced by a view.
- **Outcome (PASS):** the agent proves the Strict `BlockOnPossibleDataLoss` block on the populated
  column (the drop-column face of `_index/tightening-class`: table-has-rows, data-blind), runs
  `sys.dm_sql_referencing_entities` to prove nothing references it, and proposes the 4-phase
  deprecation (stop writes → verify unused → drop) rather than a blind drop. Surfaces that "nobody
  uses it" is an assumption until proven.
- **Fail mode:** blind-drops the column (Permissive), losing its values and breaking any
  view/proc that referenced it.

---

## Family: keys-and-refs — `skills/operations/keys-and-refs.md` (family index)

### KEY-01 — define-PK · positive
> **"Set the primary key on OrderLine to OrderId + LineNumber."**
- **op:** `skills/op/define-pk/SKILL.md` · **_index:** `skills/_index/constraint-is-a-claim/SKILL.md`
- **How it ships:** as a single schema change applied in place — the composite PK inline on a new
  table, or the clustered-index build over the rows of an existing populated table.
- **Who reviews:** any team member for a new table; a dev lead or an experienced developer for an
  existing populated table, where the index build touches every row and the uniqueness claim is
  proven against real data.
- **Seed:** `OrderLine` populated with 2–3 lines/Order; the (OrderId, LineNumber) pair unique &
  non-NULL. For the existing-populated leg, the composite claim is proven against real rows.
- **Outcome:** new table: the `CREATE` lands with the composite PK inline. Existing populated: the
  agent runs the duplicate probe (`GROUP BY OrderId,LineNumber HAVING COUNT(*)>1`) and NULL probe
  first (the constraint-is-a-claim probe reflex), proves uniqueness, and Strict publishes clean (the
  index build touches every row).
- **Fail mode:** adds a PK to an existing table without probing for duplicate/NULL key values, and
  the clustered-index build blocks at deploy.

### KEY-02 — create-fk-clean · positive
> **"Add a reference from Order to Customer — Order belongs to a Customer."**
- **op:** `skills/op/create-fk-clean/SKILL.md` · **_index:** `skills/_index/constraint-is-a-claim/SKILL.md`
- **How it ships:** as a single schema change applied in place — one `ADD CONSTRAINT` once zero
  orphans are proven.
- **Who reviews:** a dev lead must review this — a cross-table relationship is added.
- **Seed:** Order seed with NO orphan (repoint row 4's `CustomerId=999` to a real customer, in scratch).
- **Outcome:** the agent runs the orphan probe (`LEFT JOIN Customer WHERE Customer.Id IS NULL`) = 0
  (the claim proven true), Strict publishes clean, delta is one `ADD CONSTRAINT`.
- **Fail mode:** classifies clean from the `.sql` without proving zero orphans; or over-escalates the
  review of a clean FK.

### KEY-03 — create-fk-orphan · flip
> **"Add a foreign key from Order.CustomerId to Customer."**
- **op:** `skills/op/create-fk-orphan/SKILL.md` · **_index:** `skills/_index/constraint-is-a-claim/SKILL.md`
- **How it ships:** as a scripted change — `NOCHECK` to add it untrusted, reconcile the orphan, then
  `WITH CHECK CHECK` to re-validate; staged across releases if phased. The trust ladder cannot be
  expressed as a table definition.
- **Who reviews:** a dev lead must review this — existing data is modified to satisfy the new
  cross-table relationship.
- **Seed:** Order DEFAULT seed: row 4 has `CustomerId=999` (orphan, no parent).
- **Outcome:** the agent proves the clean-FK Strict block with the orphan count (**1**: Order 4), then
  proves the full script: `NOCHECK` adds it untrusted (`is_not_trusted=1`), reconcile clears the
  orphan, `WITH CHECK CHECK` flips to trusted (`is_not_trusted=0`) with no block — the trust ladder
  and the `is_not_trusted=0` end-state proof owned by `_index/constraint-is-a-claim`. Orphan present
  vs absent is what turns a clean applied-in-place FK into a scripted, data-modifying change.
- **Fail mode:** stops at `WITH NOCHECK` leaving an untrusted constraint the optimizer ignores; or
  never discovers the orphan and ships a clean FK that blocks at deploy.

### KEY-03N — create-fk-orphan, bare NOCHECK shortcut · negative
> **"Just add the FK from Order to Customer, ignore the orphan, NOCHECK it."**
- **op:** `skills/op/create-fk-orphan/SKILL.md` · **_index:** `skills/_index/constraint-is-a-claim/SKILL.md`
- **How it ships:** not as a bare `WITH NOCHECK` — that leaves the constraint untrusted
  (`is_not_trusted=1`) and protecting nothing. It ships as a scripted change: reconcile the orphan,
  then `WITH CHECK CHECK` to end trusted.
- **Who reviews:** a dev lead must review this — existing data is reconciled to satisfy the relationship.
- **Seed:** Order DEFAULT seed with orphan row 4.
- **Outcome (PASS):** the agent refuses to leave the constraint at NOCHECK (untrusted), proves
  `is_not_trusted=1` is the cost (optimizer ignores it, orphan still present), and insists on
  reconcile-then-`WITH CHECK CHECK` to end trusted. Dedupe/repoint the orphan first.
- **Fail mode:** does the developer's literal ask — adds `WITH NOCHECK` and stops — shipping a
  permanently untrusted constraint that protects nothing.

### KEY-04 — change-delete-rule · positive
> **"Change the Delete Rule on Order→Customer to Delete (cascade)."**
- **op:** `skills/op/change-delete-rule/SKILL.md`
- **How it ships:** as a single schema change applied in place — the delta is `DROP CONSTRAINT` +
  `ADD CONSTRAINT … ON DELETE CASCADE`, not a rebuild.
- **Who reviews:** a dev lead must review this — a single delete now silently removes rows across
  multiple tables.
- **Seed:** Order→Customer FK present; the multi-level chain (Order→OrderLine, from the `OrderLine`
  module) to show the CASCADE dependency scope visibly.
- **Outcome:** the delta is `DROP CONSTRAINT` + `ADD CONSTRAINT … ON DELETE CASCADE` (not a rebuild).
  For CASCADE, the agent proves the dependency scope: delete one Customer and record which child rows
  are removed across the whole Order→OrderLine chain.
- **Fail mode:** ships CASCADE without mapping the cascade graph; a delete that previously failed now
  silently removes child rows across tables.

### KEY-05 — drop-fk · positive
> **"Remove the reference from Order to Customer, we don't need the link."**
- **op:** `skills/op/drop-fk/SKILL.md`
- **How it ships:** as a single schema change applied in place — one `DROP CONSTRAINT`; nothing
  blocks, no rows are lost.
- **Who reviews:** a dev lead or an experienced developer should review this — integrity is no longer
  enforced and query plans that trusted the FK may regress.
- **Seed:** Order→Customer FK present and trusted.
- **Outcome:** Strict publishes clean; delta is one `DROP CONSTRAINT`; no block (dropping loses no
  rows). The agent flags the two real consequences: integrity no longer enforced, and query plans
  that trusted the FK may regress.
- **Fail mode:** treats it as zero-risk and omits the optimizer-plan / integrity-loss warning.

---

## Family: indexes — `skills/operations/indexes.md` (family index)

### IDX-01 — add-index · positive
> **"Add an index on Customer.Email, the list screen is slow."**
- **op:** `skills/op/add-index/SKILL.md`
- **How it ships:** as a single schema change applied in place — a clean `CREATE INDEX`, no drop, no
  table rebuild.
- **Who reviews:** any team member on a small table; a dev lead or an experienced developer if it is
  large or write-heavy. Added scrutiny at >1M rows: the non-ONLINE build locks writes for the
  duration; schedule a window.
- **Seed:** Customer populated.
- **Outcome:** Strict publishes clean; delta is a clean `CREATE INDEX`, no drop, no table rebuild.
  The agent flags that on the real (large) table the non-ONLINE build locks writes for the duration —
  schedule a window, or ONLINE (Enterprise only). The ONLINE-is-Enterprise coupling is stated inline
  in the add-index op skill (a single-op concern, not lifted). The proving ground is small, so
  observed build time ≠ prod build time; row count is the predictor.
- **Fail mode:** reports "ships in place, done" without naming the write-blocking build cost on the
  real prod table.

### IDX-02 — modify-index → unique · flip
> **"Make the index on Product.Code unique so we stop getting duplicates."**
- **op:** `skills/op/modify-index/SKILL.md` · **_index:** `skills/_index/constraint-is-a-claim/SKILL.md`
- **How it ships:** as one release — a pre-deploy dedupe clears the duplicates, then the UNIQUE index
  builds validated.
- **Who reviews:** a dev lead must review this — existing data is changed (duplicate rows merged) to
  let the uniqueness claim hold.
- **Seed:** Product DEFAULT seed: rows 4 & 5 share `Code='DUPE'`.
- **Outcome:** the agent runs the duplicate probe (`GROUP BY Code HAVING COUNT(*)>1`) predicting the
  block (the constraint-is-a-claim reflex — a UNIQUE index is a claim proven at apply time), then the
  build under Strict fails ("duplicate key was found"). Authors a pre-deploy dedupe, and re-runs
  Strict clean.
- **Fail mode:** emits the UNIQUE index from the `.sql` and the deploy fails at build on the `'DUPE'`
  rows; or dedupes silently without surfacing which rows merged.

### IDX-02B — modify-index (covering column) · flip
> **"Add a covering column to the index on Order.CustomerId."**
- **op:** `skills/op/modify-index/SKILL.md`
- **How it ships:** as a single schema change applied in place — SSDT emits a DROP+CREATE rebuild;
  no uniqueness is added, so nothing blocks.
- **Who reviews:** any team member for the include-list change itself; a dev lead or an experienced
  developer if the table is large, where the rebuild blocks writes.
- **Seed:** Order populated; index exists; no uniqueness added.
- **Outcome:** the flip pair of IDX-02: a key/include change with no uniqueness added is invisible to
  the app — SSDT emits a DROP+CREATE rebuild, Strict clean. Same modify-index op, but no UNIQUE →
  no claim → no duplicate block; size, not uniqueness, is what raises the review need.
- **Fail mode:** over-escalates the review of a benign include-list change, or fails to note the
  rebuild blocks writes like add-index.

### IDX-03 — drop-index · positive
> **"Drop the index on Product.Name, I don't think anything uses it."**
- **op:** `skills/op/drop-index/SKILL.md`
- **How it ships:** as a single schema change applied in place — a clean `DROP INDEX`, no data lost,
  reversible.
- **Who reviews:** any team member if usage stats prove it unused; a dev lead or an experienced
  developer if it may back a hot query.
- **Seed:** Product with a droppable index; a usage-stats source.
- **Outcome:** delta is a clean `DROP INDEX`, no data lost, reversible. The real proof is usage
  evidence: `sys.dm_db_index_usage_stats` showing zero seeks/scans/lookups over a representative
  window — "not used" is an assumption until proven. No production load on the proving ground.
- **Fail mode:** drops an index backing a hot query on the developer's say-so, causing a silent
  performance regression.

### IDX-04N — rebuild-index · negative
> **"The index on Order is fragmented — make SSDT rebuild it on every deploy."**
- **op:** `skills/op/rebuild-index/SKILL.md` (OPERATIONAL — refuse-and-route)
- **How it ships:** it does not ship in the dacpac — "rebuild me on every deploy" has no declarative
  destination. It belongs in a SQL Agent / Ola Hallengren maintenance job keyed to measured
  fragmentation.
- **Who reviews:** an operational, out-of-band change — routed to the DBA who owns the maintenance
  plan, not a schema reviewer.
- **Seed:** none — there is no delta to produce.
- **Outcome (PASS):** the agent refuses to put `ALTER INDEX REBUILD` in the dacpac, explains there is
  no declarative destination for "rebuild me" (handbook file 15 = §18), and routes it to a SQL Agent
  / Ola Hallengren maintenance plan keyed to measured fragmentation. If pushed, proves the harm: a
  post-deploy REBUILD re-runs on every publish (anti-idempotent).
- **Fail mode:** puts `ALTER INDEX REBUILD` in a post-deploy script, making a blocking rebuild
  re-run on every single deploy disguised as a schema step.

---

## Family: constraints — `skills/operations/constraints.md` (family index)

### CON-01 — add-default · positive
> **"Give Customer.Status a default of Active for new rows."**
- **op:** `skills/op/add-default/SKILL.md`
- **How it ships:** as a single schema change applied in place — a clean `ADD CONSTRAINT … DEFAULT`;
  no existing rows are read or written.
- **Who reviews:** any team member — the change is additive and only affects new rows.
- **Seed:** Customer populated; a column to default.
- **Outcome:** delta is a clean `ALTER TABLE … ADD CONSTRAINT DF_Customer_Status DEFAULT` with no
  UPDATE of existing rows — proving the default does not backfill. The agent names the constraint
  explicitly (not auto-named `DF__…__hash`) and notes the default only fills new rows.
- **Fail mode:** lets SSDT auto-name the constraint (env-fragile), or implies existing NULLs get
  backfilled by the default (they don't).

### CON-01B — modify-default · positive
> **"Change the default on Category.IsActive — new rows should default to 0 now."**
- **op:** `skills/op/modify-default/SKILL.md`
- **How it ships:** as a single schema change applied in place — a named `DROP CONSTRAINT` +
  `ADD CONSTRAINT … DEFAULT 0`, not a rebuild and not a backfill.
- **Who reviews:** any team member — existing rows are untouched; only new rows pick up the new default.
- **Seed:** `Category` populated (default seed; `IsActive DEFAULT 1`).
- **Outcome:** the delta is `DROP CONSTRAINT DF_Category_IsActive` + `ADD CONSTRAINT … DEFAULT 0` —
  a named DROP-then-ADD, not a rebuild, not a backfill of existing rows. The agent proves existing
  `IsActive` values are untouched and only new rows pick up the new default.
- **Fail mode:** implies changing the default rewrites existing rows, or lets SSDT auto-name the
  replacement constraint (env-fragile drift on the next deploy).

### CON-02 — add-unique · flip
> **"Make Customer.Email unique — no two customers can share an email."**
- **op:** `skills/op/add-unique/SKILL.md` · **_index:** `skills/_index/constraint-is-a-claim/SKILL.md`
- **How it ships:** as a single schema change applied in place if the data is already unique; if there
  are duplicates, as one release with a pre-deploy dedupe first, then the unique constraint validated.
- **Who reviews:** a dev lead or an experienced developer should review this — a new uniqueness
  contract; a dev lead must review it if a dedupe modifies existing data.
- **Seed:** Customer seed with duplicate emails (or use the Product.Code `'DUPE'` rows for the
  unique-on-Code variant).
- **Outcome:** the agent runs the duplicate probe first + a NULL count (UNIQUE allows exactly one
  NULL — the filtered-index remedy is owned by `_index/constraint-is-a-claim`). Duplicates → the
  build under Strict is blocked → pre-deploy dedupe, re-run clean. For legitimate multi-NULL, proposes
  a FILTERED unique index (`WHERE col IS NOT NULL`). The verdict names the count sharing a value.
- **Fail mode:** emits UNIQUE and the build fails on duplicates; or misses that several NULL rows
  also fail the build and reaches for the wrong fix.

### CON-02N — add-unique, force through · negative
> **"Add a unique constraint on Product.Code — and just push it through, the dupes are old data."**
- **op:** `skills/op/add-unique/SKILL.md` · **_index:** `skills/_index/constraint-is-a-claim/SKILL.md`
- **How it ships:** not forced through — the duplicates block the build, and that block is the safety
  proof. It ships as one release: a pre-deploy dedupe first, then the unique constraint.
- **Who reviews:** a dev lead must review this — existing data is deduplicated to let the constraint hold.
- **Seed:** Product DEFAULT seed: rows 4 & 5 share `Code='DUPE'`.
- **Outcome (PASS):** the agent proves the duplicate-key block under Strict, refuses to NOCHECK/force
  it, and requires the pre-deploy dedupe before the unique build. The block is the safety proof, not a
  failure to bypass.
- **Fail mode:** reaches for a way to skip validation and ships a unique constraint that either fails
  the build or is forced through, corrupting the uniqueness guarantee.

### CON-03 — add-check · flip
> **"Total on Order must always be positive."**
- **op:** `skills/op/add-check/SKILL.md` · **_index:** `skills/_index/constraint-is-a-claim/SKILL.md`
- **How it ships:** as a single schema change applied in place if every row already satisfies the
  predicate; if any violate, as one release with a pre-deploy fix-up first, then the check validated
  and left trusted.
- **Who reviews:** a dev lead or an experienced developer should review this — a new data contract;
  a dev lead must review it if a fix-up modifies existing rows.
- **Seed:** Order seed with a non-positive Total (add a row with `Total ≤ 0` in scratch), or
  all-positive for the clean leg.
- **Outcome:** the agent runs the violation probe (`SELECT COUNT(*) WHERE NOT (Total>0)`) first (the
  constraint-is-a-claim `WHERE NOT(pred)` probe). Violators → Strict adds `WITH CHECK` and the
  deployment is blocked ("conflicted with the CHECK constraint") → pre-deploy fix-up, re-run clean,
  stays trusted. If anyone proposes `WITH NOCHECK`, the agent proves the `is_not_trusted=1` cost.
- **Fail mode:** emits the CHECK and the deploy fails on violating rows; or uses `WITH NOCHECK`
  leaving an untrusted constraint the optimizer ignores.

### CON-04N — toggle-trust · negative
> **"Trust the FK now, the data's clean — just flip it on."**
- **op:** `skills/op/toggle-trust/SKILL.md` (OPERATIONAL — refuse-and-route) ·
  **_index:** `skills/_index/constraint-is-a-claim/SKILL.md` (the trust ladder + is_not_trusted=0 proof)
- **How it ships:** as a scripted change, not a declarative edit — SSDT has no destination for trust
  state. It wires as a script step: disable → clean the data → `WITH CHECK CHECK` to re-trust.
- **Who reviews:** whoever the surrounding change requires — e.g. a dev lead for an FK with orphans,
  since existing data is reconciled.
- **Seed:** a constraint currently NOCHECK/untrusted.
- **Outcome (PASS):** the agent recognizes enable/disable trust is not declarative (handbook file 15
  = §18), wires it as a script step (disable → clean data → `WITH CHECK CHECK` re-trust), and proves
  the end state is `is_not_trusted=0` (the constraint-is-a-claim end-state proof). A left-untrusted
  constraint silently stops protecting.
- **Fail mode:** tries to express trust state as a CREATE edit (SSDT has no destination for it), or
  flips trust without re-validating, leaving `is_not_trusted=1`.

---

## Family: static-data — `skills/operations/static-data.md` (family index)

### STA-01 — create-static-seed · positive
> **"Create a Category lookup with three fixed values."**
- **op:** `skills/op/create-static-seed/SKILL.md` · **_index:** `skills/_index/idempotent-seed/SKILL.md`
- **How it ships:** as one release — the `CREATE TABLE`, then a post-deployment script that seeds the
  lookup with an idempotent guarded MERGE.
- **Who reviews:** any team member — the change is additive and seeds fixed reference data.
- **Seed:** the `Category` lookup table; explicit IDs (no IDENTITY).
- **Outcome:** `CREATE TABLE` (declarative) + idempotent guarded MERGE in post-deploy with explicit
  ids. The agent proves idempotency (the `_index/idempotent-seed` silence-is-the-proof rule): deploy
  twice, the second deploy reports 0 rows affected + identical data-hash. Explicit ids so the app's
  constants mean the same row in every environment.
- **Fail mode:** writes a bare INSERT (duplicate-keys on the second deploy) or uses IDENTITY for
  lookup keys (ids drift between environments, breaking app constants).

### STA-02 — edit-seed · positive
> **"Add the new lookup value 'Refunded' to the Status entity."**
- **op:** `skills/op/edit-seed/SKILL.md` · **_index:** `skills/_index/idempotent-seed/SKILL.md`
- **How it ships:** as one release — the schema, then a post-deployment script that extends the
  guarded MERGE.
- **Who reviews:** any team member — the change adds one reference value.
- **Seed:** Status DEFAULT seed (3 rows); add `(4,'Refunded',1)`.
- **Outcome:** the agent extends the guarded MERGE, proves idempotency: re-publish with the value
  already present captures 0 rows (no-op MERGE), identical hash. Uses a guarded `WHEN MATCHED`
  (value-differs, null-safe), not unconditional. The second silent publish is the proof.
- **Fail mode:** writes an unconditional `WHEN MATCHED` that rewrites every row on every deploy,
  churning the whole table on each no-op redeploy.

### STA-03 — extract-to-lookup · positive
> **"Turn the free-text StatusText column on Order into a proper Status lookup entity."**
- **op:** `skills/op/extract-to-lookup/SKILL.md` · **_index:** `skills/_index/multi-phase/SKILL.md`
  (+ `skills/_index/idempotent-seed/SKILL.md` for the seed leg)
- **How it ships:** staged across three releases so the running application keeps working — additive
  (lookup + nullable FK column), backfill, then the subtractive drop. Added scrutiny if values would
  be lost, or >1M rows.
- **Who reviews:** a dev lead must review this — existing data is remapped and a cross-table
  relationship is added.
- **Seed:** Order with the `StatusText` free-text column (the `OrderStatusText` module: values
  `Pending`/`Shipped`/`Cancelled`).
- **Outcome:** three separate releases — **R1 additive** (create/reuse lookup + seed distinct values +
  add a *nullable* FK column); **R2 backfill** (`UPDATE Order SET StatusId = join`, then the
  **total-mapping proof** that must pass before R3 — the conservation proof from `_index/multi-phase`:
  `COUNT(*) WHERE StatusId IS NULL = 0` and `SELECT DISTINCT StatusText NOT IN (lookup) = 0 rows`);
  **R3 subtractive** (drop the old text column). Each phase is Strict-clean; the drop never runs until
  the totality proof is green.
- **Fail mode:** does it in one publish; unmapped text values silently become NULL or block the FK
  (Forgotten FK Check as orphan text).

### STA-03N — extract-to-lookup, unmapped value · negative
> **"Turn StatusText into the Status lookup — just wire the FK."**
- **op:** `skills/op/extract-to-lookup/SKILL.md` · **_index:** `skills/_index/multi-phase/SKILL.md`
- **How it ships:** it does not proceed to the drop — R2's total-mapping proof fails, so the FK
  constraint and the subtractive drop are held until every text value maps. The unmapped value is
  surfaced as a design decision.
- **Who reviews:** a dev lead must review this — the unmapped value is a design decision (add the
  lookup row, or deactivate the value).
- **Seed:** scratch Order seed with an **unmapped** `StatusText` value (e.g. `'Backordered'` with no
  Status row).
- **Outcome (PASS):** R2's total-mapping proof returns rows where `StatusText NOT IN (lookup)`; the
  agent stops before R3, surfaces the unmapped value as a design decision (add the lookup row or
  deactivate the value), and never lets the subtractive drop run against an incomplete mapping.
- **Fail mode:** ships the FK / drops the text column with an unmapped value present — the row either
  blocks the FK or is silently NULLed, losing the status.

### STA-04N — delete-seed-value · negative
> **"Delete the 'Cancelled' status value, we're retiring it."**
- **op:** `skills/op/delete-seed-value/SKILL.md` · **_index:** `skills/_index/idempotent-seed/SKILL.md`
  (deactivate-don't-delete)
- **How it ships:** not as a hard `DELETE` — that orphans the fact rows referencing the value and
  breaks the app's constant. It ships as a deactivation (`IsActive=0`), preserving referential
  integrity and history.
- **Who reviews:** any team member for the deactivation; a dev lead or a principal if a hard DELETE
  that orphans fact rows is ever pursued.
- **Seed:** Status row 3 'Cancelled' referenced by Order rows (`StatusId=3`); OR the `Category`
  variant where a `Category` row is referenced by `Product.CategoryId`.
- **Outcome (PASS):** the agent proves fact rows still reference the value, refuses the hard DELETE
  (would orphan fact rows / break app constants — the deactivate-don't-delete rule from
  `_index/idempotent-seed`), and proposes deactivate (`IsActive=0`), preserving referential integrity
  and history.
- **Fail mode:** removes the seed row; the MERGE's `WHEN NOT MATCHED BY SOURCE THEN DELETE` orphans
  every fact row pointing at it and breaks the app's constant.

---

## Family: structural — `skills/operations/structural.md` (family index)

### STR-01 — split-table · positive
> **"Split Customer into Customer and CustomerAddress, pull the address fields out."**
- **op:** `skills/op/split-table/SKILL.md` · **_index:** `skills/_index/multi-phase/SKILL.md`
- **How it ships:** staged across three releases so the running application keeps working — additive
  create + copy + dual-write, then repoint reads, then the subtractive drop.
- **Who reviews:** a dev lead or an experienced developer should review the additive phases; a dev
  lead must review the final drop — existing data is removed once the old columns go.
- **Seed:** Customer populated; `CustomerAddress` module present with **exactly one row per Customer**
  (the authored 1:1 positive).
- **Outcome:** three PRs: Phase 1 additive (`CREATE CustomerAddress` + FK + post-deploy copy +
  dual-write) Strict clean, hash moving-columns source-vs-new prove equal; Phase 2 repoint reads;
  Phase 3 subtractive drop old columns — Strict must block on `BlockOnPossibleDataLoss` until the
  Phase-1 hashes proved equal (the licensing-gate-on-the-drop rule from `_index/multi-phase`).
- **Fail mode:** does it in one PR — drops the old columns the same release it creates the new table,
  breaking app reads and risking data loss if the copy had a bug.

### STR-02 — merge-tables · positive
> **"Merge CustomerAddress back into Customer, we don't need two entities."**
- **op:** `skills/op/merge-tables/SKILL.md` · **_index:** `skills/_index/multi-phase/SKILL.md`
- **How it ships:** staged across releases so the running application keeps working — prove
  cardinality, copy, then drop the absorbed table. Added scrutiny if >1M rows, or first-time.
- **Who reviews:** a dev lead must review this — existing data is absorbed across tables and one table
  is removed.
- **Seed:** `CustomerAddress` populated, **proven 1:1** with Customer (the authored positive).
- **Outcome:** the agent proves cardinality first (absorbed rows == distinct parent keys = 1:1 — the
  cardinality conservation proof from `_index/multi-phase`) before anything else; 1:many would
  silently drop rows. Then hash absorbed columns vs the survivor's new columns prove equal; Phase 3
  drops the absorbed table — Strict blocks under `BlockOnPossibleDataLoss` until the hashes match.
- **Fail mode:** assumes 1:1 without the row-count check; on actual 1:many the naive copy keeps one
  row per parent and silently drops the rest (hash alone won't flag it).

### STR-02N — merge-tables, hidden 1:many · negative
> **"Merge CustomerAddress into Customer — it's a simple combine."**
- **op:** `skills/op/merge-tables/SKILL.md` · **_index:** `skills/_index/multi-phase/SKILL.md`
- **How it ships:** it does not proceed — the cardinality check detects 1:many, so the merge is
  stopped and the 1:many surfaced as a design decision before any copy runs.
- **Who reviews:** a dev lead must review this — the merge is semantically wrong as stated; it is a
  design decision, not a mechanical change.
- **Seed:** **scratch** `CustomerAddress` seed with MULTIPLE rows for one Customer (deliberate 1:many;
  the authored positive stays 1:1 — the negative is a scratch edit only).
- **Outcome (PASS):** the agent's cardinality check (absorbed rows ≠ distinct parents) detects
  1:many, stops, and tells the developer the merge is semantically wrong as stated — a design
  decision, not a shipping-shape flip — before any copy runs.
- **Fail mode:** runs the merge copy, keeping one address per customer, silently discarding the rest,
  and the hash check passes because it only compares the surviving rows.

### STR-03 — move-attribute · positive
> **"Move the Region attribute from Customer to Account, it's on the wrong entity."**
- **op:** `skills/op/move-attribute/SKILL.md` · **_index:** `skills/_index/multi-phase/SKILL.md`
  + `skills/_index/identity-and-refactorlog/SKILL.md` (a cross-table move has NO refactorlog identity —
  it is copy-then-drop, never a rename)
- **How it ships:** staged across releases when the source is populated — copy-then-drop, never a
  rename; a cross-table move has no refactorlog identity.
- **Who reviews:** a dev lead must review this — existing data moves between tables and the source
  column is dropped.
- **Seed:** Customer.Region populated; 1:1 relationship to `Account` via `Customer.AccountId`.
- **Outcome:** the agent proves the join is 1:1 (count check) so values aren't ambiguous, copies them
  (hash source-vs-destination prove equal), and Strict blocks the source-column drop until they are
  equal. It explicitly names that this is copy-then-drop, not a rename — a cross-table move has no
  refactorlog identity mapping (`_index/identity-and-refactorlog`).
- **Fail mode:** treats "move" as a rename and lets SSDT DROP+CREATE; or copies across a non-1:1
  relationship where the value is ambiguous.

### STR-04 — identity-swap · positive
> **"Turn on Auto Number for the Category entity's Id."**
- **op:** `skills/op/identity-swap/SKILL.md` · **_index:** `skills/_index/multi-phase/SKILL.md`
- **How it ships:** staged across releases on a populated table with FKs — a shadow-table rebuild with
  `SET IDENTITY_INSERT`, FKs dropped and recreated around it. Added scrutiny if first-time, or >1M rows.
- **Who reviews:** a dev lead must review this — every key is re-minted and every FK must still
  resolve after the rebuild.
- **Seed:** `Category` — the explicit-id (non-IDENTITY) PK table populated, with `Product.CategoryId`
  as an incoming FK. (The shadow-table-rebuild reasoning is owned inline by the identity-swap op
  skill — it is essentially one op, below the lift bar; retype-explicit cross-references it.)
- **Outcome:** the agent previews the Strict delta and confirms it is a shadow-table rebuild with
  `SET IDENTITY_INSERT` (not a no-op), proves every Id is unchanged after (the reseed preserved them)
  and every FK still resolves (zero orphans). Sequenced across PRs because the FKs drop and recreate
  around the rebuild.
- **Fail mode:** treats adding `IDENTITY(1,1)` as a trivial one-line edit; SSDT's silent full table
  rebuild re-mints keys without IDENTITY_INSERT and every FK now points at the wrong rows — the most
  dangerous one-line edit in the catalog.

---

## Family: audit — `skills/operations/audit.md` (family index)

### AUD-01 — temporal-new · positive
> **"I want full history on a new entity — every version of every row."**
- **op:** `skills/op/temporal-new/SKILL.md`
- **How it ships:** as a single schema change applied in place — the system-versioned `CREATE` for a
  new table, with its history table and period columns.
- **Who reviews:** a dev lead or an experienced developer should review this — a versioned object,
  built cleanly.
- **Seed:** new entity, temporal from birth (`SYSTEM_VERSIONING=ON` + history table + period columns),
  scratch-authored (greenfield — no authored seed table needed).
- **Outcome:** the agent confirms the developer wants point-in-time row history (every version of
  every row), then previews the Strict delta publishing the system-versioned `CREATE` clean for the
  new table.
- **Fail mode:** picks the wrong history feature for the developer's actual need, or takes on an
  unnecessary licensing/added-scrutiny tax for a clean greenfield versioned table.

### AUD-02 — temporal-convert · flip
> **"Add full history to our existing populated Customer entity."**
- **op:** `skills/op/temporal-convert/SKILL.md` · **_index:** `skills/_index/multi-phase/SKILL.md`
- **How it ships:** staged across releases — add the period columns with backfilled ROW START times,
  create the history table, then enable versioning. Added scrutiny if >1M rows, or first-time.
- **Who reviews:** a dev lead must review this — an existing populated table is converted and its rows
  are stamped with historical start times.
- **Seed:** Customer POPULATED (convert the existing populated table in a scratch copy).
- **Outcome:** the flip of AUD-01: converting an existing populated table is staged — add the (hidden)
  period columns with backfilled sane ROW START times, create the history table, then enable
  versioning (the additive→cutover shape from `_index/multi-phase`). The agent proves the backfill
  produces sane start times and that enabling versioning does not block; hash before/after proves the
  rows are untouched.
- **Fail mode:** tries to convert in one publish, or adds period columns without backfilling ROW
  START so every existing row claims to have begun at conversion time.

### AUD-03 — audit-columns · positive
> **"Add CreatedBy / CreatedOn / ModifiedBy / ModifiedOn audit fields to Customer."**
- **op:** `skills/op/audit-columns/SKILL.md` · **_index:** `skills/_index/tightening-class/SKILL.md`
  (the NOT-NULL-on-populated face)
- **How it ships:** as a single schema change applied in place if the columns are nullable; as one
  release with a pre-deploy backfill first if they are NOT NULL on a populated table.
- **Who reviews:** any team member for nullable audit columns; a dev lead or an experienced developer
  if a NOT NULL backfill stamps existing rows.
- **Seed:** Customer populated.
- **Outcome:** nullable audit columns add in a single applied-in-place release. If the developer wants
  them NOT NULL, Strict blocks on existing rows with no value (the tightening-class row-presence
  guard); the pre-deploy backfill that stamps them clears it, shipping as one release. Permissive shows
  what `GenerateSmartDefaults` would silently stamp.
- **Fail mode:** adds NOT NULL audit columns with no backfill (Optimistic NOT NULL) and the deploy is
  blocked; or lets `GenerateSmartDefaults` silently stamp values.

---

## Cross-family traps (the obvious call is wrong)

### IDEM-01N — no-op redeploy (silence is the proof) · negative
> **"Re-publish the project — nothing changed since last deploy."**
- **op:** `skills/op/edit-seed/SKILL.md` (the data-plane no-op leg) · **_index:** `skills/_index/idempotent-seed/SKILL.md`
- **How it ships:** as a single schema change applied in place with zero delta — a clean re-publish.
- **Who reviews:** any team member — nothing changes; the silence is the proof of idempotency.
- **Seed:** a DB already at the project's current state (publish once, then again unchanged).
- **Outcome (PASS):** a clean publish with zero delta; the guarded seed MERGE captures 0 rows, with an
  identical content-hash on the second deploy. Silence is the strongest guarantee — the proof the
  deploy is idempotent (the silence-is-the-proof rule from `_index/idempotent-seed`).
- **Fail mode:** an unconditional `WHEN MATCHED` MERGE rewrites every seed row on the no-op redeploy,
  churning the whole table; or reports spurious schema drift on an unchanged tree.

---

## The flip-pair index (same op, opposite shipping shape — decided only by data)

These pairs are the heart of *classify-by-proving*. The `.sql` edit is identical (or the same op);
only the **seed** differs. An agent that returns the same verdict for both halves classified from
text and fails the pair.

| op | clean leg → how it ships | flipped leg → how it ships | the data that flips it | governing _index |
|---|---|---|---|---|
| make-mandatory | COL-03B empty → **applied in place** | COL-03 / COL-03C populated → **scripted (gate-relaxation) or staged across releases** | table-has-rows, not column-has-NULLs | tightening-class |
| narrow | COL-06B fits → **applied in place** | COL-06 over-length → **pre-deploy reconcile + schema, or staged** | `MAX(LEN)` vs target | tightening-class |
| retype | COL-07B widen → **applied in place** | COL-07 explicit → **staged across releases** | lossless vs lossy conversion | multi-phase |
| create-FK | KEY-02 clean → **applied in place** | KEY-03 orphan → **scripted (NOCHECK→reconcile→WITH CHECK CHECK), or staged** | orphan count | constraint-is-a-claim |
| add-unique | CON-02 unique data → **applied in place** | CON-02 / IDX-02 dupes → **pre-deploy dedupe + schema** | duplicate count | constraint-is-a-claim |
| add-check | CON-03 all satisfy → **applied in place** | CON-03 violators → **pre-deploy fix-up + schema** | violation count | constraint-is-a-claim |
| temporal | AUD-01 new → **applied in place** | AUD-02 populated → **staged across releases** | populated vs new | multi-phase |
| modify-index | IDX-02B include → **applied in place** | IDX-02 →unique dupes → **pre-deploy dedupe + schema** | uniqueness added + dupes | constraint-is-a-claim |
| extract-to-lookup | STA-03 all mapped → **staged across releases (proceeds)** | STA-03N unmapped → **stop — design decision** | total-mapping proof | multi-phase |
| merge-tables | STR-02 1:1 → **staged across releases (proceeds)** | STR-02N 1:many → **stop — design decision** | cardinality (absorbed==parents) | multi-phase |

The make-mandatory family (COL-03 / COL-03B / COL-03C) carries the **corrected finding** and is the
single hardest gate — see `rubric.md`. The block is **table-has-rows, not column-has-NULLs**; a
backfill that clears every NULL still does not clear the gate on a populated table. An agent that
reports a clean applied-in-place or pre-deploy change on a populated table without empirically
discovering the backfill-then-still-blocked reality has classified from text and the whole run fails.

## Coverage map (every per-op skill has at least one case)

The ~41 per-op skills each have at least one prompt above. The mapping:

- **tables:** create-entity=TBL-01 · rename-entity=TBL-02/02N · delete-entity=TBL-03 ·
  move-schema=TBL-04 · archive-entity=TBL-05 · junction=TBL-06
- **columns:** add-optional=COL-01 · add-mandatory=COL-02 · make-mandatory=COL-03/03B/03C ·
  make-optional=COL-04 · widen=COL-05 · narrow=COL-06/06B · retype-implicit=COL-07B ·
  retype-explicit=COL-07 · rename-attribute=COL-08/08N · delete-attribute=COL-09
- **keys-and-refs:** define-pk=KEY-01 · create-fk-clean=KEY-02 · create-fk-orphan=KEY-03/03N ·
  change-delete-rule=KEY-04 · drop-fk=KEY-05
- **indexes:** add-index=IDX-01 · modify-index=IDX-02/02B · drop-index=IDX-03 · rebuild-index=IDX-04N
- **constraints:** add-default=CON-01 · modify-default=CON-01B · add-unique=CON-02/02N ·
  add-check=CON-03 · toggle-trust=CON-04N
- **static-data:** create-static-seed=STA-01 · edit-seed=STA-02 (+IDEM-01N) · extract-to-lookup=STA-03/03N ·
  delete-seed-value=STA-04N
- **structural:** split-table=STR-01 · merge-tables=STR-02/02N · move-attribute=STR-03 · identity-swap=STR-04
- **audit:** temporal-new=AUD-01 · temporal-convert=AUD-02 · audit-columns=AUD-03

Every negative id (`…N`) is a refusal/block/escalation case; every id with a `B`/`C` suffix or listed
in the flip-pair index is a data-decided flip whose partner must yield a **different** shipping shape.
