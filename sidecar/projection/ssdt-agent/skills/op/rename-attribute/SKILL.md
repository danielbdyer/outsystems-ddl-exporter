---
name: rename-attribute
description: Use when the developer says "rename the attribute", "change FirstName to GivenName", "I renamed the field in Service Studio", "rename this column" — an existing column getting a new name. Keeps the data only with a refactorlog entry; without one SSDT drops the column and every value is lost.
---

# Rename attribute (a rename with no refactorlog entry)

> **Default (provisional — the data decides; prove before you classify).** With a refactorlog entry
> this ships as a single schema change, applied in place: a metadata `sp_rename` renames the column
> and preserves its data. A dev lead or an experienced developer should review it — every caller of
> the old name (views, procedures, ORM mappings, reports, ETL) must change to keep working. Without
> a refactorlog entry SSDT instead drops the old column and adds the new one, and every value in the
> column is lost — stop and demand the refactorlog before this ships.

## OutSystems phrasing
"rename the attribute", "change FirstName to GivenName", "I renamed the field in Service Studio".

## SSDT meaning
With a **refactorlog entry**, SSDT emits `EXEC sp_rename 'schema.Table.Old', 'New', 'COLUMN'` —
data preserved. **Without** it, SSDT sees the old column gone and a new one appear and emits `DROP
COLUMN [Old]` + `ADD [New]` — all values in that column lost. Edit the CREATE; never write `ALTER`.

## The named trap
**A rename with no refactorlog entry** (handbook 16 = §19.1), with its companion **Refactorlog
Cleanup** (§19.6). This is the identity-vs-name concern — see
`../../_index/identity-and-refactorlog/SKILL.md`; do not re-derive the refactorlog mechanics here.

## How it flips (the specifics only)
- refactorlog entry present → delta is `sp_rename ... 'COLUMN'`, data preserved and applied in
  place; every caller of the old name (views, procs, ORM mappings, reports, ETL) must change, so a
  dev lead or an experienced developer reviews it
- refactorlog entry missing → delta is `DROP COLUMN`+`ADD`, every value in the column is lost; stop
  and demand the refactorlog (see `../../_index/identity-and-refactorlog/SKILL.md`)
- CDC-enabled → the capture instance names the old column and is frozen to the table's current
  columns, so it must be recreated → added scrutiny (see `../../_index/cdc/SKILL.md`)
- external consumers must keep the old name → a backward-compatibility view holds it
  (`../compat-view/SKILL.md`) → the change stages across releases so both names coexist. **The rename
  is yours; the compat view is principal-only — route that bridge up to a principal** (see
  `../compat-view/SKILL.md`).

## Prove it
Script the delta and **read it** — it MUST be `sp_rename ... 'COLUMN'`. If you see `DROP
COLUMN`/`ADD`, the refactorlog is missing; catch it in the delta, never after deploy. Confirm the
`.refactorlog` changed when the rename was authored. For the publish loop, see
`../../prove-on-dacpac/SKILL.md`.

## The verdict (to the developer)
You renamed the attribute, and the data is safe: on a disposable copy of Dev I read the generated
delta and it came out as `sp_rename`, which renames the column in place and keeps every value. Had
that refactorlog entry been missing, SSDT would instead have dropped the old column and added a new
one, losing everything in it — so reading the delta is what makes this safe rather than a hope. The
real cost is that every caller of the old name has to move to the new one: views, procedures, ORM
mappings, reports, ETL, so a dev lead or an experienced developer should review it. One question
before it ships — does anything outside this project still read the old name? If so, a
backward-compatibility view keeps the old name alive while those consumers move; if not, the rename
is clean once the in-project callers are updated.

## The reasoning (in conversation)
The refactorlog is what carries a column's identity, not its cosmetics
(`../../_index/identity-and-refactorlog/SKILL.md`). That's why renaming the field by editing the
text alone isn't enough: to SSDT that looks like one column disappearing and a different one
appearing, and it handles that by dropping the old and adding the new — which is where the data
goes. Reading the generated delta every time is how you catch that loss in the script, where it
costs nothing to fix, instead of in production, where it costs the column.

## On the record
The fragment this contributes to the pull request (`../../author-pr/SKILL.md`).

**Review & release**
- A dev lead or an experienced developer should review this: the running application must change to
  keep working, because every caller of the old column name (views, procedures, ORM mappings,
  reports, ETL) must move to the new name.
- Ships as a single schema change, applied in place: a metadata `sp_rename` renames the column and
  preserves its data. No data is read or written.
- Added scrutiny, when the table is CDC-tracked: the change-data-capture capture instance names the
  old column and is frozen to the table's current columns, so it must be recreated (see
  `../../_index/cdc/SKILL.md`).

**Verification** — run in each environment after deployment:
```sql
-- expect 1 row, name = <New>: the column exists under the new name only, the old name is gone
SELECT c.name FROM sys.columns c
WHERE c.object_id = OBJECT_ID('dbo.<table>') AND c.name IN ('<Old>', '<New>');
```
Before each promotion the generated delta must read as `sp_rename ... 'COLUMN'`, not `DROP COLUMN`
+ `ADD` — a publish that lost the refactorlog would drop the column and lose its values.

**Rollback.** Reversible without data loss: rename the column back (`<New>` → `<Old>`) with its own
refactorlog entry — `sp_rename` preserves the data in both directions. The callers updated for the
new name must be reverted with it. Not auto-reversed.

**Not verified**
- Application impact — consumers of the old column name outside the project (reports, ETL,
  integrations not in the dacpac) break silently until they move to the new name; @app-owner and the
  consumer owners confirm the callers are updated.
- Other environments — Test, UAT, and Prod may hold external consumers still reading the old name
  where Dev does not. Read the delta and run the verification query before each promotion.
- Reversibility — only the forward rename is exercised on the disposable copy; the backout rename is
  the same metadata operation but is not separately proven.
- Capture — when the table is CDC-tracked, recreating the capture instance for the new column name is
  not exercised on the copy (see `../../_index/cdc/SKILL.md`).
