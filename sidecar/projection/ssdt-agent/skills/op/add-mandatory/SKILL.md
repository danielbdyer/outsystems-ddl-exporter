---
name: add-mandatory
description: Use when the developer says "add a required attribute", "add a Status field everyone must have one", "add a field that can't be blank" — a new NOT NULL column. The Optimistic NOT NULL trap; a populated table needs a DEFAULT.
---

# Add mandatory attribute (Optimistic NOT NULL)

> **Default (provisional — the data decides).** A dev lead or an experienced developer should
> review this: adding a required attribute means the running application must change to keep
> working. With an explicit default it ships as a single schema change, applied in place — SQL
> Server fills every existing row from the default as the column is added. Populated with no
> default: the deployment is blocked. Prove it on a disposable copy before classifying.

## OutSystems phrasing
"add a required attribute", "add a Status field, everyone must have one".

## SSDT meaning
Add a `NOT NULL` column. On a populated table SQL Server must put *something* in every existing
row — so it needs a **DEFAULT**. With `NOT NULL CONSTRAINT DF_... DEFAULT(...)`, SSDT emits `ALTER
... ADD [Col] <type> NOT NULL CONSTRAINT ... DEFAULT ...` and stamps existing rows. Without a
default, the deployment is blocked on the populated table. Edit the CREATE; never write `ALTER`.

## The named trap
**Optimistic NOT NULL** (handbook 16 = §19.2) — a `NOT NULL` column with no default on a populated
table: the build succeeds (SSDT cannot see the rows), and the deployment is blocked at deploy with
"Cannot insert NULL". If `GenerateSmartDefaults=True`, SSDT **silently** backfills (e.g. an empty
string) — Permissive lets that be observed, showing what would have been stamped. This is not the
tightening class: that one blocks on row presence for an *existing* column, whereas here the block
is a genuine can't-insert-NULL on a *new* column, cured by supplying a value.

## How it flips (the specifics only)
- table empty → ships as a single schema change, applied in place; any team member can review it
  (no rows to fill, so the default is optional).
- populated + explicit DEFAULT → ships as a single schema change, applied in place — SQL Server
  fills existing rows from the default as the column is added; a dev lead or an experienced
  developer reviews it, because adding a required attribute means the running application must
  change to keep working.
- populated, **no DEFAULT** → the deployment is blocked → add a default (back to a single in-place
  schema change) or a **pre-deployment backfill** that fills the rows before the column lands; do
  **not** let `GenerateSmartDefaults` silently decide the value.
- CDC-enabled / >1M rows → **added scrutiny** (see `../../_index/cdc/SKILL.md`).

## Prove it
With a default, Strict publishes clean and the delta shows the `DEFAULT`. Drop the default and prove
the Strict refusal ("Cannot insert NULL"); then run Permissive with `GenerateSmartDefaults=True` and
snapshot the data hash to show the developer exactly what value SSDT *would* have silently stamped.
For the publish loop, see `../../prove-on-dacpac/SKILL.md`.

## The verdict (to the developer)
You asked to add a required attribute, so every row that already exists needs a value for it. With
the default you gave it, SSDT stamps every existing row and publishes clean on a disposable copy of
Dev. Without a default it would have been blocked at deploy — or, with GenerateSmartDefaults on,
silently filled with an empty string — and I proved both on the copy, so the value that would have
been invented is visible instead of a surprise. One thing worth your call: is that default the right
value for the rows that already exist, or should those be set deliberately before the column becomes
required?

## The reasoning (in conversation)
The DEFAULT is the one thing that lets SQL Server fill rows it has no way to populate on its own. The
no-default block is SSDT refusing to invent your data — which is the safe behavior. The opposite is
the dangerous one: `GenerateSmartDefaults` invents a value silently, with no signal that it did. So
the question to settle before this ships is a plain one — who supplies the value for the rows that
already exist: you, explicitly, or the engine, silently? It should never be silently.

## On the record
Fragments for the pull request (`../../author-pr/SKILL.md`), record register.

**Review & release**
- A dev lead or an experienced developer should review this: adding a required attribute means the
  running application must change to keep working. On an empty table the change is additive and any
  team member can review it.
- Ships as a single schema change, applied in place — with an explicit default, SQL Server fills
  every existing row from the default as the column is added. With no default on a populated table
  the deployment is blocked ("Cannot insert NULL"); shipping then needs an explicit default, or a
  pre-deployment backfill that fills the rows before the column lands.
- Added scrutiny, when it applies: at production row counts (> 1M) the column add may run long or
  block writes (schedule a window); a CDC-tracked table freezes its capture instance to the current
  columns and needs handling (see `../../_index/cdc/SKILL.md`).

**Verification** — run in each environment after deployment
```sql
-- expect 1 row, is_nullable = 0: the column exists and rejects NULLs
SELECT c.name AS column_name, c.is_nullable
FROM sys.columns c
WHERE c.object_id = OBJECT_ID('<table>') AND c.name = '<Col>';

-- expect 1 row when a default was supplied: the named default that stamps existing and new rows
SELECT dc.name, dc.definition
FROM sys.default_constraints dc
JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
WHERE dc.parent_object_id = OBJECT_ID('<table>') AND c.name = '<Col>';
```

**Rollback**
The column drops back out without touching the pre-existing columns. If a default was added, drop it
first: `ALTER TABLE <table> DROP CONSTRAINT DF_<Table>_<Col>;`, then
`ALTER TABLE <table> DROP COLUMN <Col>;`. Dropping the column discards the values it held (the
default-stamped or later-entered values); every other column in each row is unchanged.

**Not verified**
- Application impact — inserts that omit this column now rely on the default; with no default, an
  insert that omits it is blocked with "Cannot insert NULL". Whether application code supplies a
  meaningful value rather than leaning on the default is not confirmed here (@app-owner).
- Other environments — Test, UAT, and Prod may hold different row counts the disposable copy of Dev
  cannot see, and if the column ships without an explicit default, a profile with
  `GenerateSmartDefaults` enabled may silently stamp a value this copy did not. Run the verification
  query before promotion.
- Production scale and timing — on a large table, adding a NOT NULL column with a default may run
  long or block writes; the small copy does not show it.
- Reversibility — dropping the column back out discards that column's values; the forward publish is
  all that was proven on the copy.
