---
name: modify-default
description: Use when the developer says "change the default value", "new orders should default to Shipped now, not Pending", "stop defaulting this attribute" — changing or removing a DEFAULT constraint. SSDT does a DROP-then-ADD; it ships in place as a single schema change and never backfills existing rows.
---

# Modify or remove a default

> **Default (provisional — the data decides).** Any team member can review this: no existing row
> values change — a default governs only future inserts. Ships as a single schema change, applied
> in place: SSDT does a DROP-then-ADD for a change, or a plain DROP to remove it. Prove it on a
> disposable copy before classifying.

## OutSystems phrasing
"change the default value", "new orders should default to Shipped now, not Pending", "stop
defaulting this attribute".

## SSDT meaning
**Modify** = SSDT `DROP`s then re-`ADD`s the named constraint (a brief no-default window inside the
deploy transaction). **Remove** = `DROP CONSTRAINT`. Neither touches existing row values — a default
only ever governed future inserts.

## The named trap
Same as add-default: the **unnamed default** makes the DROP-then-ADD fragile across environments —
insist the constraint is named `DF_<Table>_<Col>`. And a modified default still does not retro-change
existing rows that were written under the old default; if the developer wants old rows re-stamped,
that is a separate backfill (see `../add-default/SKILL.md` and
`../../_index/idempotent-seed/SKILL.md`).

## How it flips (the specifics only)
- modify / remove a default → ships as a single schema change, applied in place; any team member
  can review it, in any data state — no existing row values change.
- the developer also wants existing rows re-stamped to the new value → a separate op. It ships as
  one release: the schema change, then a post-deployment script that runs an idempotent UPDATE after
  it lands (see `../../_index/idempotent-seed/SKILL.md`).
- CDC-enabled table → CDC does not track constraints (handbook file 15 = §18.5), so a modified or
  removed default on a CDC-tracked table needs no added scrutiny on its own.

## Prove it
Build + Strict `sqlpackage /Action:Script`; for a *modify*, confirm SSDT emits DROP-then-ADD of
`DF_…` and the Strict publish is clean with **no UPDATE of existing rows**. For a *remove*, confirm a
clean `DROP CONSTRAINT DF_…` with no row touch. See `../../prove-on-dacpac/SKILL.md` +
`../../talk-to-local-sql/SKILL.md`.

## The verdict (to the developer)
You asked to change the default. This is a schema-only change: SSDT drops the old default constraint
and adds the new one, and on a disposable copy of your data no existing rows were touched. Old rows
keep the value they were written with — a default only governs future inserts. If you want those old
rows re-stamped to the new value, that's a separate backfill I can prove the same way. Do you want
the existing rows re-stamped, or just new inserts from here on?

## The reasoning (in conversation)
A default is a rule about future inserts. Changing or dropping it never reaches back to yesterday's
rows — those were written under the old rule and keep their values. If you need the existing rows to
carry the new value, that's a separate, proven backfill. The surprise this avoids: expecting a
changed default to rewrite history. It doesn't — it only ever governs what gets written next.

## On the record
Fragments for the pull request (`../../author-pr/SKILL.md`), record register.

**Review & release**
- Any team member can review this: no existing data is touched — a default governs only future
  inserts.
- Ships as a single schema change, applied in place: SSDT does a DROP-then-ADD for a modify, or a
  plain DROP for a remove. No table rebuild, no row updates.
- Added scrutiny: none. CDC does not track constraints, so a modified or removed default on a
  CDC-tracked table adds no scrutiny on its own (handbook file 15 = §18.5).

**Verification** — run in each environment after deployment
```sql
-- modify: expect 1 row — DF_<Table>_<Col> exists carrying the new default definition
-- remove: expect 0 rows — the default constraint is gone
SELECT dc.name, c.name AS column_name, dc.definition
FROM sys.default_constraints dc
JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
WHERE dc.name = 'DF_<Table>_<Col>';
```

**Rollback**
Lossless: no existing rows change either way. Backing out a modify re-creates `DF_<Table>_<Col>` with
its previous definition; backing out a remove re-adds the dropped constraint. Record the prior
definition so the restore is exact.

**Not verified**
- Application impact — inserts that omit this column now receive the new default, or fail if the
  default was removed and the column is NOT NULL with no value supplied; whether any code relies on
  the old behaviour is not confirmed here (@app-owner).
- Other environments — a default created unnamed (`DF__Table__Col__<hash>`) or by an ad-hoc script
  may differ per environment; the disposable copy of Dev cannot see it. Run the verification query
  before promotion.
- Retro re-stamp — existing rows are deliberately left as written; if the new value must apply to old
  rows, that is a separate, proven backfill and is not part of this change.
