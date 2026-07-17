---
name: rename-entity
description: Use when the developer says "rename the Entity", "change the table name from Customer to Client", "I renamed it in Service Studio" — an existing table getting a new name. Identity must be carried by the refactorlog; a rename with no refactorlog entry loses the table's data.
---

# Rename entity

> **Default (provisional — the data decides).** With the refactorlog entry: ships as a single
> schema change applied in place, the delta a metadata `sp_rename`, no data read or written; a dev
> lead or an experienced developer should review it, because the running application must change to
> keep working — every caller referencing the old name breaks. Without the refactorlog entry: the
> delta is `DROP TABLE` + `CREATE TABLE` and every row is lost — stop and get the refactorlog first.

## OutSystems phrasing
"rename the Entity", "change the table name from Customer to Client", "I renamed it in Service Studio".

## SSDT meaning
With a **refactorlog entry**, SSDT emits `EXEC sp_rename 'schema.Old', 'New', 'OBJECT'` — data
and `object_id` preserved. **Without** the entry SSDT sees one table vanish and a new one
appear and emits `DROP TABLE [Old]` + `CREATE TABLE [New]` — all rows lost. Never write `ALTER`.

## The named trap
A rename with no refactorlog entry (handbook 16 = §19.1), with its companion Refactorlog Cleanup
(§19.6). This is the identity-vs-name concern — see
`../../_index/identity-and-refactorlog/SKILL.md`; do not re-derive the refactorlog mechanics here.

## How it flips (the specifics only)
- refactorlog entry present → ships in place, the delta is `sp_rename`; a dev lead or an experienced
  developer reviews it, because every caller crosses a boundary the rename breaks — FKs, views,
  procs, ETL, reports all reference the name
- **refactorlog entry missing** → the delta is `DROP`+`CREATE` and every row is lost; stop and demand
  the refactorlog before anything else (see `../../_index/identity-and-refactorlog/SKILL.md`)
- table is CDC-enabled → the capture instance still names the old table → ships as a scripted, staged
  change that recreates the capture instance, with added scrutiny because the table feeds a
  change-data-capture stream (see `../../_index/cdc/SKILL.md`)
- external consumers must keep the old name → add a backward-compat view (`../compat-view/SKILL.md`),
  pushing toward a staged, multi-release coexistence

## Prove it
Run `sqlpackage /Action:Script` and **read the delta** — it MUST be `sp_rename`. If you see
`DROP TABLE`/`CREATE TABLE`, the refactorlog is missing; this is the single most important
delta-read in the whole catalog. Confirm the `.refactorlog` file changed when the rename was
authored. For the publish loop, see `../../prove-on-dacpac/SKILL.md`.

## The verdict (to the developer)
You renamed the entity. On a disposable copy of Dev, SSDT generated `sp_rename`, so the rows are
preserved and the table keeps its identity. That only works because the refactorlog entry exists —
without it SSDT would see the old table vanish and a new one appear, DROP and re-CREATE the table,
and lose every row. The rename is metadata-only, but the new name breaks every caller — foreign
keys, views, procedures, ETL, reports — so a dev lead or an experienced developer should review it
before it ships. One question: does anything outside this project still need the old name? If so,
this needs a backward-compatible view kept during the transition.

## The reasoning (in conversation)
The refactorlog carries *identity, not text* — see `../../_index/identity-and-refactorlog/SKILL.md`
for the full why. The failure this avoids: renaming by editing the CREATE alone, which reads to SSDT
as dropping one table and creating another — the most expensive silent data loss in the catalog,
because nothing errors and the rows are simply gone.

## On the record
The fragment this contributes to the pull request (`../../author-pr/SKILL.md`).

**Review & release**
- A dev lead or an experienced developer should review this: the running application must change to
  keep working — the rename breaks every caller that references the old name (foreign keys, views,
  procedures, ETL, reports).
- Ships as a single schema change, applied in place. The delta is a metadata `sp_rename`; no data is
  read or written.
- Added scrutiny, if the table is CDC-tracked: it feeds a change-data-capture stream, and the
  capture instance still names the old table, so it is recreated — ships then as a scripted, staged
  change (see `../../_index/cdc/SKILL.md`).

**Verification** — run in each environment after deployment
```sql
-- expect 1 row: the table exists under its new name
SELECT name FROM sys.tables WHERE name = 'Client';
-- expect 0 rows: the old name is gone
SELECT name FROM sys.tables WHERE name = 'Customer';
```

**Rollback**
Reversible without data loss: rename back with `EXEC sp_rename 'schema.New', 'Old', 'OBJECT'`,
carried by its own refactorlog entry so the reverse is declarative. Every caller updated to the new
name must be reverted with it; that is not auto-reversed.

**Not verified**
- Application impact. Any caller still referencing the old name — foreign keys, views, procedures,
  ETL, reports, and any consumer outside the dacpac — breaks until updated. That every caller was
  updated is not confirmed here.
- Other environments. The rename is declarative only if the promoted build carries the refactorlog
  entry; a build missing it emits `DROP`+`CREATE` and loses the rows. Confirm the `.refactorlog`
  travels with the change.
- Reversibility. Only the forward rename is exercised on the disposable copy; the reverse rename and
  the caller reverts are not.
