---
name: add-default
description: Use when the developer says "give this attribute a default value", "new rows should default to Active", "everything new should start as Pending" — adding a named DEFAULT constraint. Ships in place as a single schema change; it fills only NEW rows and never backfills existing ones.
---

# Add a default

> **Default (provisional — prove before you classify).** A dev lead approves this: the change is
> additive and the running application is unaffected. Ships as a single schema change, applied in
> place — adding a default never touches existing row values. Prove it on a disposable copy before
> classifying.

> **SHIP terminal: ONE RELEASE, in place.** Adding a default touches no existing row — proven this
> branch (F10): after adding a default, an existing `NULL` stayed `NULL` and an existing value was
> unchanged; only a fresh insert that omitted the column received the default. On a new `NOT NULL`
> column the default instead stamps every existing row as the column lands (the add-mandatory remedy) —
> the two shapes wear one word, and the record names which shipped. `FINDINGS_AND_CHANGES.md` F10.
>
> **Proven precedent:** `../../../sample-prs/add-default.md` — the worked instance of the ten-section
> pull-request template (`../../author-pr/SKILL.md`) for this op.

## OutSystems phrasing
"give this attribute a default value", "new rows should default to Active", "everything new should
start as Pending".

## SSDT meaning
A named default constraint on the column — `CONSTRAINT DF_<Table>_<Col> DEFAULT (<value>) FOR <Col>`
(or inline). Two shapes share this vocabulary, and they behave **oppositely** on existing rows:

- **On an EXISTING column** (this file's primary): SSDT emits `ADD CONSTRAINT`. It affects
  **future inserts only** — it does NOT backfill existing rows.
- **Riding a NEW `NOT NULL` column** (the `add-mandatory` remedy): the
  `ADD [Col] ... NOT NULL CONSTRAINT ... DEFAULT` **backfills every existing row from the default
  as the column lands** — that stamp is exactly why a populated table applies clean (proven:
  `../../../sample-prs/add-default.md`, DacFx 162.5.57).

## The named trap
The **unnamed default**: letting SSDT auto-name the constraint (`DF__Table__Col__<hash>`) yields a
name that differs per environment, and diffing and refactoring become fragile — always name it
`DF_<Table>_<Col>`. Second surprise: on an **existing** column the default does not fill existing
NULLs — it touches only new rows; backfilling the existing rows is a separate op (see
`../make-mandatory/SKILL.md` for the NOT-NULL-with-backfill path). The mirror surprise: on a
**new** NOT NULL column the engine does the opposite and stamps every existing row (the second
shape above) — the two shapes are different operations wearing one word, and the record must name
which one shipped.

## How it flips (the specifics only)
- adding a default to an existing column → ships as a single schema change, applied in place; a dev lead approves this, in any data state — no existing row is touched.
- the default rides a NEW mandatory column (the `add-mandatory` remedy) → ships as a single schema
  change, applied in place, **and the default stamps every existing row as the column lands**
  (proven: `../../../sample-prs/add-default.md`). The stamped values are data the record names; a dev lead approves it, because the application must now supply or
  accept that value.
- the developer also wants existing rows backfilled → a separate op. It ships as one release: the
  schema change, then a post-deployment script that runs an idempotent UPDATE after it lands (see
  `../../_index/idempotent-seed/SKILL.md`). If the column is also becoming NOT NULL, follow
  `../make-mandatory/SKILL.md` instead. The default itself still ships in place and keeps the
  lightest look.

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
The pull request is an instance of the ten-section template in `../../author-pr/SKILL.md`; the worked
instance for this op is `../../../sample-prs/add-default.md`. SHIP terminal: **ONE RELEASE, in place.**
The fragment this operation contributes:

**Review & release**
- A dev lead approves this: the change is additive and the running application is unaffected — the lightest look on this estate.
- Ships as a single schema change, applied in place. No data is read or written.
- Added scrutiny: none. Adding a default is additive and touches no existing rows.

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
  whether any code relies on that distinction is not confirmed here (app owner).
- Other environments — an existing unnamed default (`DF__Table__Col__<hash>`) on this column in
  QA/UAT/Prod must be dropped before this one lands; the disposable copy of Dev cannot see it. Run
  the verification query before promotion.
