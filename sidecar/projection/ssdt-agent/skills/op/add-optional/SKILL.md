---
name: add-optional
description: Use when the developer says "add an optional attribute", "add a MiddleName field it can be blank", "add a field that doesn't have to be filled" — a new nullable column. The safest change in the catalog.
---

# Add optional attribute

> **Default (provisional — the data decides).** Any team member can review this: the change is
> additive and the running application is unaffected. Ships as a single schema change, applied in
> place — existing rows just get NULL, no data is read or written. Prove it on a disposable copy
> before classifying.

## OutSystems phrasing
"add an optional attribute", "add a MiddleName field, it can be blank".

## SSDT meaning
Add a `NULL` column inside the `CREATE TABLE`. SSDT emits `ALTER TABLE ... ADD [Col] <type>
NULL`. Existing rows get `NULL`; a metadata-only operation on modern SQL Server. Never author
the `ALTER` — edit the CREATE.

## The named trap
None material at the data layer. The one edge: if `IgnoreColumnOrder=False` a mid-table insert
can trigger a rebuild — the publish profiles keep `IgnoreColumnOrder=True`, so position is a
non-issue.

## How it flips (the specifics only)
- any table state (empty or populated) → ships as a single schema change, applied in place; any team
  member can review it — the change is additive and the running application is unaffected (NULL is
  always a valid existing-row value).

## Prove it
Strict publish succeeds clean; the delta is a single `ALTER TABLE ... ADD ... NULL`; the deployment
is never blocked, regardless of row count. That clean run on a populated copy is the whole proof. For
the publish loop, see `../../prove-on-dacpac/SKILL.md`.

## The verdict (to the developer)
You asked to add an optional attribute — the safest change in the catalog. On a disposable copy of
your populated data, SSDT just runs `ALTER TABLE ... ADD ... NULL`, and existing rows simply get
NULL, so nothing already in the table can conflict with it. It published clean and nothing was lost.
There's nothing to decide here; it's ready to ship.

## The reasoning (in conversation)
An optional add never gets blocked, because NULL is always a valid value for the rows already in the
table. What decides whether a change like this flips is whether the existing rows can already satisfy
the new rule — which is exactly why making a column *mandatory* is the harder sibling: the rows
already there may not satisfy it. The mistake to avoid is fearing additive columns and putting the
risk in the wrong place — an optional add is genuinely additive, and no existing row can conflict
with it.

## On the record
Fragments for the pull request (`../../author-pr/SKILL.md`), record register.

**Review & release**
- Any team member can review this: the change is additive and the running application is unaffected.
- Ships as a single schema change, applied in place. No data is read or written.
- Added scrutiny: none — an optional column is additive and every existing row takes NULL.

**Verification** — run in each environment after deployment
```sql
-- expect 1 row, is_nullable = 1: the optional column landed and accepts NULL
SELECT name, is_nullable
FROM sys.columns
WHERE object_id = OBJECT_ID('dbo.<table>') AND name = '<column>';
```

**Rollback**
Remove the column from the `CREATE TABLE` and republish; SSDT emits
`ALTER TABLE <table> DROP COLUMN <column>;`. Lossless only while the column is unwritten — every row
holds NULL at deploy; once the application writes values into it, dropping the column discards them.

**Not verified**
- Application impact — a nullable add does not change existing application behaviour, but any code
  intended to populate the new column is not exercised by the disposable copy (@app-owner).
- Production scale and timing — the add is metadata-only on modern SQL Server with
  `IgnoreColumnOrder=True`; that it stays metadata-only at production row counts and on the target's
  edition and version is not confirmed by the small copy.
- Reversibility — the forward add is proven; once values are written into the column, dropping it is
  lossy (see Rollback).
