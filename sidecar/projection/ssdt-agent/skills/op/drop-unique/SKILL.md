---
name: drop-unique
description: Use when the developer says "codes don't have to be unique anymore", "remove the uniqueness rule", "allow duplicates on this attribute" — removing a UNIQUE constraint or unique index. Always publishes clean, but duplicates can be written from that moment on, and re-adding uniqueness later is blocked by any duplicate that appeared (Msg 1505).
---

# Drop a unique constraint or unique index

> **Default (provisional — prove before you classify).** Ships as a single schema change, applied
> in place — one `DROP INDEX` (or `DROP CONSTRAINT` for a keyed form), no data read or written,
> and the publish never blocks. A dev lead or an experienced developer reviews it: an identity
> guarantee the application and other tables may rely on stops being enforced.

> **SHIP terminal: ONE RELEASE, in place.** Proven live on this branch (database `PG_inv_x1`,
> sqlpackage 170.5.76): removing `UIX_Status_Code` from the CREATE generated the single statement
> `DROP INDEX [UIX_Status_Code] ON [dbo].[Status];` and the Strict publish returned
> `Successfully published database.` with the three seeded rows untouched.
>
> **Proven precedent:** `../../../sample-prs/drop-unique.md` — the worked instance of the
> ten-section template (`../../author-pr/SKILL.md`) for this op.

## OutSystems phrasing
"codes don't have to be unique anymore", "remove the uniqueness rule", "two customers can share
this now".

## SSDT meaning
Remove the `CREATE UNIQUE INDEX` statement (or the `CONSTRAINT ... UNIQUE`) from the table's
`.sql`. SSDT emits `DROP INDEX [UIX_...] ON <table>` (or `ALTER TABLE ... DROP CONSTRAINT`).
Data is untouched; the table stops refusing duplicate values.

## The named trap
Uniqueness is often load-bearing beyond the table: lookups by that column assume one row
(`TOP 1` semantics silently change), and an upsert or MERGE keyed on the column can start
matching several rows — `Msg 8672` on the next seed run against duplicated keys. And the door
swings shut behind the drop: re-adding uniqueness later is `../add-unique/SKILL.md` over
whatever rows accumulated — one duplicate blocks it with `Msg 1505` until the duplicates are
reconciled. The index was also a real index: reads that used it may slow.

## How it flips (the specifics only)
- **permanent removal — duplicates are now legitimate** → ships in place as a single schema
  change; a dev lead or an experienced developer reviews it. Confirm no MERGE, upsert, or seed
  keys on the column.
- **the column is a MERGE or seed key** (`../../_index/idempotent-seed/SKILL.md`) → the seed's
  `ON` clause can start matching several rows; the seed keyed on it is part of the change set,
  or the drop waits.
- **removed only to change the index's shape** → that is `../modify-index/SKILL.md`
  (DROP + CREATE in one publish), not a standalone drop.
- **re-adding later** → `../add-unique/SKILL.md` against that day's data; a duplicate written in
  the gap blocks it (`Msg 1505`).

## Prove it
Strict publishes clean; the delta is a single `DROP INDEX` (or `DROP CONSTRAINT`); nothing
blocks. Probe `sys.indexes WHERE is_unique = 1` before and after, and grep the seed for a MERGE
keyed on the column — the publish cannot see that dependency. See
`../../prove-on-dacpac/SKILL.md`.

## The verdict (to the developer)
"You asked to allow duplicates on this attribute. Removing the uniqueness rule always publishes
clean and changes no existing row. From then on the database accepts duplicates — and anything
that assumed one-row-per-value, including our own seed script if it matches on this attribute,
needs a look before this ships. If uniqueness ever needs to come back, every duplicate written
in between blocks it until reconciled. Confirm duplicates are genuinely legitimate now, and
this ships as a single in-place change."

## The reasoning (in conversation)
A unique constraint is an identity claim other code quietly builds on — lookups, upserts, seeds
keyed on the value. Dropping it never fails at deploy; it changes what "find the row for this
code" means everywhere else. The mistake to avoid is scoping this as an index change when it is
a meaning change.

## On the record

The pull request is an instance of the ten-section template in `../../author-pr/SKILL.md`; the
worked instance is `../../../sample-prs/drop-unique.md`. SHIP terminal: **ONE RELEASE, in
place.** The fragment this operation contributes:

**Review & release**
- A dev lead or an experienced developer must review this: an identity guarantee stops being
  enforced; no data is touched.
- Ships as a single schema change, applied in place — one `DROP INDEX` (or
  `ALTER TABLE ... DROP CONSTRAINT`). No data is read or written, and the publish never blocks.
- Added scrutiny: none at deploy time; reads that used the index may slow, and duplicates can
  accumulate after deploy.

**Verification** — run in each environment after deployment
```sql
-- expect 0 rows: the unique index no longer exists
SELECT name FROM sys.indexes
WHERE object_id = OBJECT_ID('<schema>.<Table>') AND name = 'UIX_<Table>_<Column>';
```

**Rollback**
Re-creating the index reverses the drop:
`CREATE UNIQUE INDEX UIX_<Table>_<Column> ON <schema>.<Table> (<Column>);`. The build validates
uniqueness over every existing row, so it lands clean only while no duplicate was written in
the gap; a duplicate blocks it with `Msg 1505` until reconciled (`../add-unique/SKILL.md`). The
drop itself loses no data.

**Not verified**
- Seed and upsert dependencies — whether any MERGE or application upsert keys on the column is
  confirmed only for the scripts the disposable copy carries.
- Read performance — the index served reads as well as uniqueness; the plan impact does not
  show on a small copy. Whoever owns query performance confirms it.
- Other environments — proven on a disposable copy of Dev only. Run the verification query in
  each environment before promotion.
