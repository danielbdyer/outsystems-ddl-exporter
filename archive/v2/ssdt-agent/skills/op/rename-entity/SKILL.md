---
name: rename-entity
description: Use when the developer says "rename the Entity", "change the table name from Customer to Client", "I renamed it in Service Studio" — an existing table getting a new name. Identity must be carried by the refactorlog; a rename with no refactorlog entry loses the table's data.
---

# Rename entity

> **Default (provisional — prove before you classify).** With the refactorlog entry: ships as a single
> schema change applied in place, the delta a metadata `sp_rename`, no data read or written; a dev
> lead approves it, weighing that the running application must change to
> keep working — every caller referencing the old name breaks. Without the refactorlog entry the
> rename does not happen, in one of two ways decided by the deploy's drop posture: under the
> production posture (`DropObjectsNotInSource=False`) the publish returns Ok and performs a
> **phantom rename** — the new table created empty, the populated original stranded; under the
> drop-enabled diagnostic posture the delta is `DROP TABLE` + `CREATE TABLE` and every row is
> lost. Stop and get the refactorlog first.

> **The pull request.** `../../author-pr/SKILL.md` is the ten-section template every change fills;
> the worked instance for this op is `../../../sample-prs/rename-entity.md` — a complete PR proven
> live on this branch. **Ships as ONE RELEASE, applied in place** — the delta is a single
> `EXEC sp_rename … 'OBJECT'` that keeps every row and the object_id — **only when the refactorlog
> entry travels with the build.** Without it the delta is `DROP TABLE` + `CREATE TABLE` and the rows
> are lost (proven live, 2026-08-21: rename kept all 8 rows and object_id `1061578820`).

## OutSystems phrasing
"rename the Entity", "change the table name from Customer to Client", "I renamed it in Service Studio".

## SSDT meaning
With a **refactorlog entry**, SSDT emits `EXEC sp_rename 'schema.Old', 'New', 'OBJECT'` — data
and `object_id` preserved. **Without** the entry SSDT sees one table vanish and a new one appear,
and what happens next depends entirely on the deploy's drop posture. Under the **production
posture** (`DropObjectsNotInSource=False` — proven on the Twin, DacFx 162.5.57): the publish
returns **Ok** and performs a **phantom rename** — `[New]` is created **empty** and the populated
`[Old]` is stranded exactly where it was; the rows do not follow and nothing errors. Under the
**diagnostic posture** (`DropObjectsNotInSource=True`, the disposable copy): the delta is
`DROP TABLE [Old]` + `CREATE TABLE [New]` — all rows lost. Either way the intended rename did not
happen. Never write `ALTER`.

## The named trap
A rename with no refactorlog entry (handbook 16 = §19.1), with its companion Refactorlog Cleanup
(§19.5). This is the identity-vs-name concern — see
`../../_index/identity-and-refactorlog/SKILL.md`; do not re-derive the refactorlog mechanics here.

## How it flips (the specifics only)
- refactorlog entry present → ships in place, the delta is `sp_rename`; a dev lead approves it, because every caller crosses a boundary the rename breaks — FKs, views,
  procs, ETL, reports all reference the name
- **refactorlog entry missing** → the rename does not happen: a phantom under the production
  posture (new table empty, original stranded, publish green — proven:
  `../../../sample-prs/rename-entity.md`), a data-losing `DROP`+`CREATE` under the diagnostic
  posture; stop and demand the refactorlog before anything else (see
  `../../_index/identity-and-refactorlog/SKILL.md`)
- external consumers must keep the old name → the rename stages across releases so those consumers
  can migrate during a transition window

## Prove it
Run `sqlpackage /Action:Script` and **read the delta** — it MUST be `sp_rename`. If you see
`DROP TABLE`/`CREATE TABLE`, the refactorlog is missing; this is the single most important
delta-read in the whole catalog. Confirm the `.refactorlog` file changed when the rename was
authored. For the publish loop, see `../../prove-on-dacpac/SKILL.md`.

## The verdict (to the developer)
You renamed the entity. On a disposable copy of Dev, SSDT generated `sp_rename`, so the rows are
preserved and the table keeps its identity. That only works because the refactorlog entry exists —
without it SSDT sees the old table vanish and a new one appear, and either quietly creates the new
table empty while stranding your data under the old name (a green deploy that didn't do what you
asked — that's what a real production posture does), or drops and re-creates the table and loses
every row. The rename is metadata-only, but the new name breaks every caller — foreign
keys, views, procedures, ETL, reports — so a dev lead approves this
before it ships. One question: does anything outside this project still need the old name? If so,
the rename must stage across releases so those consumers can migrate before the old name goes away.

## The reasoning (in conversation)
The refactorlog carries *identity, not text* — see `../../_index/identity-and-refactorlog/SKILL.md`
for the full why. The failure this avoids: renaming by editing the CREATE alone, which reads to SSDT
as dropping one table and creating another — the most expensive silent data loss in the catalog,
because nothing errors and the rows are simply gone.

## On the record
The fragment this contributes to the pull request (`../../author-pr/SKILL.md` is the template; the
worked instance is `../../../sample-prs/rename-entity.md`).

**Review & release**
- A dev lead approves this, weighing that the running application must change to keep working — the rename breaks every caller that references the old name (foreign keys, views,
  procedures, ETL, reports).
- Ships as a single schema change, applied in place. The delta is a metadata `sp_rename`; no data is
  read or written.

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
