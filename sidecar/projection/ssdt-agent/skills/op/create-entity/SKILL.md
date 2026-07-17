---
name: create-entity
description: Use when the developer says "add a new Entity", "create a new table", "I need a new CustomerPreference entity", "make a new entity for X" — a brand-new table that does not yet exist. The purest additive CREATE TABLE.
---

# Create entity

> **Default (provisional — the data decides).** Any team member can review this: the change is
> additive and self-contained — a brand-new table nothing yet depends on, and the running
> application is unaffected. Ships as a single schema change, applied in place: the `CREATE TABLE`
> is emitted verbatim, and no existing data is read or written. Prove the clean publish on a
> disposable copy before classifying.

## OutSystems phrasing
"add a new Entity", "create a new table", "I need a new CustomerPreference entity".

## SSDT meaning
A new `.sql` file in `Modules/` holding a `CREATE TABLE` — PK, columns, FKs, defaults, all
inline. The table does not exist, so SSDT emits the `CREATE TABLE` verbatim; nothing to
transition. The canonical "describe the destination" move. Never write `ALTER`.

## The named trap
*A foreign key to a non-existent parent.* If the new table references a table not in the project,
the **build** fails — not the deploy; add the parent first, or in the same change. And a file the
project glob misses silently never deploys. Neither is driven by the data: both are dependency
concerns caught at build time, not the deployment being blocked by the rows in the table.

## How it flips (the specifics only)
- new standalone table → ships as a single schema change, applied in place; any team member can
  review it (nothing depends on it, nothing to transition — additive, the running application is
  unaffected).
- table needs seed/lookup rows on creation → ships as one release: the CREATE, then a
  post-deployment script that seeds the rows after the table lands (the guarded MERGE seed — see
  `../create-static-seed/SKILL.md` and `../../_index/idempotent-seed/SKILL.md`).
- table must be CDC-enabled from birth → CDC is not in the dacpac model, so it ships as a scripted
  change: enabling capture cannot be expressed as a table definition. Added scrutiny: the table
  feeds a change-data-capture stream and needs handling from birth (see `../../_index/cdc/SKILL.md`).
- FK to a parent not yet in the project → still the same single schema change, but the build gates
  it on dependency order (the parent must exist first) — a dependency concern, not the data driving
  a different disposition.

## Prove it
Strict publish must succeed clean, and the generated delta must be a single `CREATE TABLE
[schema].[Name]` with **no** `DROP`/`ALTER` of any sibling. If the delta touches a table not
edited for this change, stop — the change is not self-contained. For the publish loop that proves
this, see `../../prove-on-dacpac/SKILL.md`; for the substrate, `../../talk-to-local-sql/SKILL.md`.

## The verdict (to the developer)
You asked to add a new entity — a brand-new table nothing depends on yet. On a disposable copy of
your data, SSDT just runs the `CREATE TABLE` and the table appears; there's no existing data for it
to be conservative about, so nothing can conflict and nothing can be lost. It published clean, and
the only thing in the delta is the CREATE — it's ready to ship. The one thing worth checking is
dependencies: does anything this table needs (say a parent table for a foreign key) already exist in
the project, and does anything already depend on it?

## The reasoning (in conversation)
A create is the purest case of the model being the schema — there's no existing data for SSDT to be
conservative about, so the only thing that can go wrong is a dependency, not anything the data does:
a foreign key to a parent that isn't in the project yet, or a `.sql` file the project glob misses.
That's why the question to ask is "does anything already depend on this, or does this depend on
anything not yet here?" The mistake to avoid is treating self-contained additive work as riskier than
it is — asking for heavier review than a brand-new table nothing depends on actually needs.

## On the record
Fragments for the pull request (`../../author-pr/SKILL.md`), record register.

**Review & release**
- Any team member can review this: the change is additive and the running application is unaffected —
  a brand-new table nothing yet depends on.
- Ships as a single schema change, applied in place: SSDT emits `CREATE TABLE [schema].[Name]`
  verbatim. No existing data is read or written. If the table needs seed rows on creation, it ships
  as one release instead: the CREATE, then a post-deployment MERGE seed that runs after it lands
  (see `../create-static-seed/SKILL.md`).
- Added scrutiny: none for a standalone table. If it must be CDC-enabled from birth, capture cannot
  be expressed as a table definition, so it ships as a scripted change and the table feeds a
  change-data-capture stream that needs handling (see `../../_index/cdc/SKILL.md`).

**Verification** — run in each environment after deployment
```sql
-- expect 1 row: the new table exists
SELECT name, type_desc
FROM sys.tables
WHERE object_id = OBJECT_ID('dbo.<table>');
```

**Rollback**
Remove the `CREATE TABLE` file from the project and republish; SSDT emits `DROP TABLE
[schema].[Name];`. Lossless only while the table is unwritten — it is created empty; once the
application writes rows into it, dropping the table discards them, and any post-deployment seed rows
go with it.

**Not verified**
- Application impact — a new table nothing yet reads or writes does not change existing behaviour,
  but any application code intended to read or write it is not exercised by the disposable copy
  (@app-owner).
- Dependencies — every referenced foreign-key parent must exist in the project, and the new `.sql`
  file must be included by the project glob; the clean Strict publish confirms this only for the
  parents present in this dacpac, not for other environments' project structure.
- Reversibility — the forward create is proven; once rows are written into the table, dropping it is
  lossy (see Rollback).
