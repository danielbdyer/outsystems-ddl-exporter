---
name: add-default
description: Use when the developer says "give this attribute a default value", "new rows should default to Active", "everything new should start as Pending" — adding a named DEFAULT constraint. Ships in place as a single schema change; it fills only NEW rows and never backfills existing ones.
---

# Add a default

> **Default (provisional — the data decides).** Any team member can review this: the change is
> additive and the running application is unaffected. Ships as a single schema change, applied in
> place — adding a default never touches existing row values. Prove it on a disposable copy before
> classifying.

## OutSystems phrasing
"give this attribute a default value", "new rows should default to Active", "everything new should
start as Pending".

## SSDT meaning
A named default constraint on the column — `CONSTRAINT DF_<Table>_<Col> DEFAULT (<value>) FOR <Col>`
(or inline). SSDT emits `ADD CONSTRAINT`. It affects **future inserts only** — it does NOT backfill
existing rows.

## The named trap
The **unnamed default**: letting SSDT auto-name the constraint (`DF__Table__Col__<hash>`) yields a
name that differs per environment, and diffing and refactoring become fragile — always name it
`DF_<Table>_<Col>`. Second surprise: the default does not fill existing NULLs. It touches only new
rows; backfilling the existing rows is a separate op — `../backfill-rows/SKILL.md` (and if the
column is also becoming required, `../make-mandatory/SKILL.md` for the NOT-NULL path).

## How it flips (the specifics only)
- adding a default → ships as a single schema change, applied in place; any team member can review
  it, in any data state.
- the developer also wants existing rows backfilled → a separate op, `../backfill-rows/SKILL.md`. It
  ships as one release: the schema change, then a post-deployment guarded UPDATE that runs after it
  lands (the discipline is `../../_index/idempotent-seed/SKILL.md`), and a dev lead reviews the
  backfill because existing data is modified. If the column is also becoming NOT NULL, follow
  `../make-mandatory/SKILL.md` for the tightening. The default itself still ships in place and stays
  reviewable by any team member.
- CDC-enabled table → CDC does not track constraints (handbook file 15 = §18.5), so a default on a
  CDC-tracked table needs no added scrutiny on its own.

## Prove it
Build + Strict `sqlpackage /Action:Script`; confirm the delta is a clean
`ALTER TABLE … ADD CONSTRAINT DF_…` with **no UPDATE of existing rows** — that absence *is* the
proof the default does not backfill. See `../../prove-on-dacpac/SKILL.md` +
`../../talk-to-local-sql/SKILL.md`.

## The verdict (to the developer)
You asked to give this column a default. On a disposable copy of your data, SSDT just adds the
constraint and touches no existing rows. One thing worth flagging: the default only fills new rows
going forward — any existing blanks stay blank. If you want those filled in too, that's a separate
backfill step I can prove the same way. Do you want the existing rows backfilled, or just new ones
from here on?

## The reasoning (in conversation)
There are two different things hiding in one request here. A default is a rule about future writes —
it costs nothing and changes no data you already have. Filling in the rows that are already there is
a change to existing values, and that's a separate, proven step. They sound like one ask and they're
two. Keeping them apart is what avoids the common surprise: the column still shows blanks after
deploy, because the default was only ever going to touch new rows.

## On the record
Fragments for the pull request (`../../author-pr/SKILL.md`), record register.

**Review & release**
- Any team member can review this: the change is additive and the running application is unaffected.
- Ships as a single schema change, applied in place. No data is read or written.
- Added scrutiny: none. A default is not CDC-tracked (CDC does not track constraints — handbook file
  15 = §18.5).

**Verification** — run in each environment after deployment
```sql
-- expect 1 row: the named default constraint exists on the column
SELECT dc.name, c.name AS column_name, dc.definition
FROM sys.default_constraints dc
JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
WHERE dc.name = 'DF_<Table>_<Col>';
```

**Rollback**
Lossless: `ALTER TABLE <table> DROP CONSTRAINT DF_<Table>_<Col>;`. No existing rows were written, so
nothing is restored.

**Not verified**
- Application impact — inserts that omit this column now receive the default value instead of NULL;
  whether any code relies on that distinction is not confirmed here (@app-owner).
- Other environments — an existing unnamed default (`DF__Table__Col__<hash>`) on this column in
  Test/UAT/Prod must be dropped before this one lands; the disposable copy of Dev cannot see it. Run
  the verification query before promotion.
