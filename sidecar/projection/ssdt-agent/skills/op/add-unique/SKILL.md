---
name: add-unique
description: Use when the developer says "this attribute should be unique", "no two customers can share an email", "stop duplicate codes" — adding a UNIQUE constraint or unique index. Uniqueness is built over all existing rows at deploy; duplicates block it (Msg 1505); a unique index permits only one NULL, so several NULLs on a nullable column block it too (use a filtered index).
---

# Add a unique constraint

> **Default (provisional — prove before you classify).** One release, applied in place — the unique
> index builds over the existing rows; no data is modified. A dev lead or an experienced developer
> reviews it: the running application is now rejected when it would create a duplicate. Prove no
> duplicates — and no second NULL on a nullable column — before classifying; either blocks the build.

> **SHIP terminal: ONE RELEASE, build-or-block.** A unique index is enforced the moment it builds —
> there is no trusted/untrusted state. Proven live on this branch (DBs `db_uq`, `db_uqf`): a **plain**
> unique index on `Customer.Email` (two customers have no email) → **BLOCK `Msg 1505`** ("duplicate key
> value is (<NULL>)") — a unique index permits only one NULL; a **filtered** index
> (`WHERE Email IS NOT NULL`) → `Successfully published database.`, `is_unique = 1`, `has_filter = 1`.
> A duplicate value blocks the same way (`Msg 1505`, the value named). `FINDINGS_AND_CHANGES.md` F10.
>
> **Proven precedent:** `../../../sample-prs/add-unique.md` — the worked instance of the ten-section
> pull-request template (`../../author-pr/SKILL.md`) for this op, carrying the live messages.

## OutSystems phrasing
"this attribute should be unique", "no two customers can share an email", "stop duplicate codes".

## SSDT meaning
`CREATE UNIQUE INDEX [UIX_<Table>_<Col>] ON <Table> (<Col>)` — the v2 emitter renders uniqueness as a
unique index after the table, not an inline constraint. SSDT builds the index over **every existing
row** at deploy; the build is where uniqueness is proven.

## The named trap
**Duplicate values block the build** — the deploy fails (`Msg 1505`) the instant two rows share a
value, and the message names the duplicate. Second trap: **a unique index allows exactly ONE NULL**, so
a nullable column with two or more NULLs blocks the build the same way (the "duplicate key value" is
`<NULL>`). The fix for legitimately-repeated blanks is a **filtered unique index**:
`CREATE UNIQUE INDEX … WHERE <Col> IS NOT NULL`. This is the constraint-is-a-claim family — a value
blocks the build, not row presence; see `../../_index/constraint-is-a-claim/SKILL.md`.

## How it flips (the specifics only)
- no duplicates, and at most one NULL → one release, in place; the index builds and is enforced. A dev
  lead or an experienced developer reviews it, because the application is now rejected on a duplicate.
- duplicates present → a pre-deploy de-dupe clears them first, then the index builds; without it the
  publish blocks (`Msg 1505`). A dev lead reviews it, because existing data is modified.
- nullable column with more than one NULL → a **filtered** unique index (`WHERE <Col> IS NOT NULL`)
  enforces uniqueness among the filled values and allows any number of blanks; stays one release. If
  every row must instead have a unique value, make the column required first (make-mandatory), then a
  plain unique index — a larger change.
- >1M rows → added scrutiny: the build (and any de-dupe) may block writes or run long — schedule a window.

## Prove it
Run the duplicate probe FIRST: `SELECT <Col>, COUNT(*) FROM <table> GROUP BY <Col> HAVING COUNT(*) > 1`
(and a NULL count for a nullable column). Then publish: clean (≤1 NULL, no duplicate) → the index
builds; a duplicate or a second NULL → the publish blocks with `Msg 1505` naming the duplicate value.
Author the de-dupe or switch to a filtered index, re-publish clean. See `../../prove-on-dacpac/SKILL.md`
+ `../../talk-to-local-sql/SKILL.md`. Seed: `Customer.Email` has two NULLs (a plain index blocks; a
filtered index builds); Status's `UIX_Status_Code` is a clean single-column positive.

## The verdict (to the developer)
You asked to make Email unique. Email is optional, and on a copy of Dev a plain unique rule was refused
— two customers have no email yet, and a unique index allows only one blank (`Msg 1505`). A filtered
unique index enforces uniqueness among the customers who have an email and allows any number without
one; it built clean on the copy. If instead two customers shared the same filled email, that blocks the
build the same way until the duplicate is reconciled. The call that's yours: may a customer have no
email (a filtered index), or must every customer have a unique email (make it required first, then a
plain unique index)?

## The reasoning (in conversation)
Run the duplicate probe (`GROUP BY … HAVING COUNT(*) > 1`) and a NULL count before anything else — the
existing rows determine how this ships; the SQL statement never can. The failure this avoids is "just
add the rule" over data that already holds duplicates: the build blocks. On a nullable column, a unique
index permits exactly one NULL — a second blank blocks it the same way, and a filtered unique index is
the fix when repeated blanks are legitimate. See `../../_index/constraint-is-a-claim/SKILL.md`.

## On the record
The pull request is an instance of the ten-section template in `../../author-pr/SKILL.md`; the worked
instance for this op — with the live messages — is `../../../sample-prs/add-unique.md`. SHIP terminal:
**ONE RELEASE, build-or-block.** The fragment this operation contributes:

**Review & release**
- A dev lead or an experienced developer reviews this: the application is now rejected when it would
  create a duplicate (or, on a plain index, a second blank). When a pre-deploy de-dupe removes
  duplicate rows first, a dev lead reviews it: existing data is modified.
- Ships as one release, applied in place — the unique index builds over the existing rows. No data is
  modified unless a de-dupe is needed.
- Added scrutiny, when it applies: at production row counts the build and any de-dupe may block writes
  or run long (schedule a window).

**Verification** — run in each environment after deployment
```sql
-- expect 0 rows: no value is shared across rows, so uniqueness holds
SELECT <Col>, COUNT(*) FROM <table> WHERE <Col> IS NOT NULL GROUP BY <Col> HAVING COUNT(*) > 1;

-- expect one row, is_unique = 1: the unique index exists and is enforced
SELECT name, is_unique, has_filter FROM sys.indexes
WHERE object_id = OBJECT_ID('<table>') AND name = 'UIX_<Table>_<Col>';
```

**Rollback**
The unique index drops without data loss: `DROP INDEX [UIX_<Table>_<Col>] ON <table>;` (the same for a
filtered unique index). A pre-deploy de-dupe is not auto-reversed; the rows it removed or merged are
recorded in the pre-deploy step's output.

**Not verified**
- Application impact — any insert or update that would create a duplicate now fails ("duplicate key was
  found"); on a plain index a second NULL fails the same way. Application-side handling is not confirmed
  here (app owner).
- Other environments — QA, UAT, and Prod may hold duplicates the copy cannot see. Run the duplicate
  probe before promotion.
- Production scale and timing — on a large table the build and any de-dupe may block writes or run
  long; the small copy does not show it.
