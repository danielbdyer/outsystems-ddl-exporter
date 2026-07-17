---
name: move-schema
description: Use when the developer says "move the entity to the archive schema", "put this table under a different namespace/module", "change its schema" — a schema change on an existing table, carrying the same refactorlog trap as a rename: with the refactorlog entry SSDT moves the data, without one it drops and recreates.
---

# Move schema (between schemas)

> **Default (provisional — the data decides; prove before you classify).** Ships as a single schema
> change applied in place when the refactorlog carries the move, or as a scripted change
> (`ALTER SCHEMA TRANSFER`) when `object_id` preservation is preferred or the table is large — either
> way the data, `object_id`, and row counts are preserved. Without a refactorlog entry SSDT reads it
> as drop-and-create and the rows are lost. A dev lead or an experienced developer should review this:
> every fully-qualified `schema.Table` reference must follow the move, so the running application must
> change to keep working.

## OutSystems phrasing
"move the entity to the archive schema", "put this table under a different namespace/module".

## SSDT meaning
Change the schema in the `CREATE TABLE` header. With a **refactorlog** entry SSDT treats it as a
move and preserves the data; the cleaner, `object_id`-preserving path is a script:
`ALTER SCHEMA target TRANSFER source.Table`. Without a refactorlog entry, SSDT reads it as **drop
the old table and create the new** — the rows are lost, the same shape as a rename with no
refactorlog entry. Never write `ALTER COLUMN` here.

## The named trap
Same family as a rename with no refactorlog entry — no refactorlog → `DROP`+`CREATE`, and every
fully-qualified `dbo.X` reference breaks. This is the identity-vs-name concern — see
`../../_index/identity-and-refactorlog/SKILL.md`, which names the two-part `schema.Table` name as
just an address. Do not re-derive it here.

## How it flips (the specifics only)
- refactorlog present, table empty or small → ships as a single schema change applied in place; the
  refactorlog carries the move, so the data is preserved and `object_id` is unchanged.
- prefer `object_id` preservation / large table → ships as a scripted change (`ALTER SCHEMA
  TRANSFER`) — one operation, data untouched, `object_id` preserved.
- refactorlog MISSING → SSDT reads it as drop-and-create and the rows are lost — STOP, same remedy
  as a rename with no refactorlog entry (see `../../_index/identity-and-refactorlog/SKILL.md`).
- CDC-enabled → the capture instance references the old two-part `schema.Table` name → added
  scrutiny: it is frozen to that name and must be recreated (see `../../_index/cdc/SKILL.md`).

## Prove it
Script the delta. A move must be `sp_rename` (schema-qualified) or your authored `ALTER SCHEMA
TRANSFER` that leaves row counts and `object_id` intact — prove both. A `DROP`+`CREATE` in the
delta is the data-loss signal: it means SSDT is dropping the old table and recreating the new one,
and the rows go with it. For the publish loop, see `../../prove-on-dacpac/SKILL.md`.

## The verdict (to the developer)
You asked to move the entity to another schema. That's the same trap as a rename: with the
refactorlog SSDT keeps the data, without it SSDT drops the table and recreates it, and the rows go
with it. On a disposable copy of Dev I proved the move with `ALTER SCHEMA TRANSFER` — the row counts
and `object_id` came through unchanged, so the table moved rather than being dropped and rebuilt.
The one thing to line up is that every fully-qualified `schema.Table` reference has to follow the
move, so the running application has to change with it — that's why a dev lead or an experienced
developer should have eyes on the reference list.

## The reasoning (in conversation)
Identity is separate from name — the two-part `schema.Table` name is just an address, not the table
itself (see `../../_index/identity-and-refactorlog/SKILL.md`). Once you see that, a schema move stops
looking like a special operation: it's the rename trap again, and the same proof settles it — did the
row counts and `object_id` come through, or did SSDT drop and recreate. The failure this avoids is
relearning that proof from scratch for every operation instead of recognizing the one pattern
underneath them all.

## On the record
The fragment this op contributes to the pull request (`../../author-pr/SKILL.md`).

**Review & release**
- A dev lead or an experienced developer should review this: the running application must change to
  keep working — every fully-qualified `schema.Table` reference must follow the move.
- Ships as a single schema change applied in place when the refactorlog carries the move (the data,
  `object_id`, and row counts are preserved), or as a scripted change (`ALTER SCHEMA TRANSFER`) when
  `object_id` preservation is preferred or the table is large — the transfer cannot be expressed as a
  table definition.
- Added scrutiny: none for a small table with the refactorlog present; a CDC-tracked table has its
  capture instance frozen to the old two-part `schema.Table` name and must be recreated (see
  `../../_index/cdc/SKILL.md`); a first-time move on this estate or a large table carries added
  scrutiny — schedule a window.

**Verification** — run in each environment after deployment
```sql
-- expect one row, under the target schema only: the table moved intact, it was not dropped and
-- recreated (a drop+create would lose the rows and mint a new object_id)
SELECT SCHEMA_NAME(t.schema_id) AS schema_name, t.name, SUM(p.rows) AS row_count
FROM sys.tables t
JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
WHERE t.name = '<Table>'
GROUP BY SCHEMA_NAME(t.schema_id), t.name;
```
On the disposable copy, the before-and-after `object_id` and row count must match — that is the
proof the delta moved the table rather than dropping and recreating it.

**Rollback**
The move reverses losslessly as a schema operation: transfer the table back with `ALTER SCHEMA
<source> TRANSFER <target>.<Table>`, which preserves the rows and `object_id`, and repoint every
`schema.Table` reference back to the source name. What is not auto-reversed is the reference edits —
the same fully-qualified references that were updated to the new schema must be updated back. A move
that ever went through as a drop-and-create (the refactorlog-missing failure) has already lost the
rows and cannot be rolled back from the schema alone — restore from a backup.

**Not verified**
- Application impact: every fully-qualified `schema.Table` reference — in views, procedures,
  synonyms, and application code — breaks when the schema changes; that all of them were found and
  repointed is not confirmed here — the app owner confirms it.
- Other environments: the move was proven on a disposable copy of Dev, where the refactorlog carries
  it; that the refactorlog entry is present and the reference set is the same in Test, UAT, and Prod
  is not confirmed here — confirm the refactorlog carries the move, and run the verification query,
  before each promotion.
- Production scale / timing: `ALTER SCHEMA TRANSFER` is a metadata operation, but any dependent
  rebuild and the CDC capture-instance recreation are exercised at seed scale only; blocking and
  duration at production row counts are not shown here.
- Reversibility: only the forward move is proven; transferring the table back and repointing every
  reference is not exercised here.
