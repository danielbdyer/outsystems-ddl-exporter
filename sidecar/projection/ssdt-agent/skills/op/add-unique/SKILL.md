---
name: add-unique
description: Use when the developer says "this attribute should be unique", "no two customers can share an email", "stop duplicate codes" — adding a UNIQUE constraint or unique index. Uniqueness is built over all existing rows at deploy; duplicates block the deployment; UNIQUE permits only one NULL, so several NULLs on a nullable column block it too.
---

# Add a unique constraint

> **Default (provisional — the data decides).** A dev lead or an experienced developer should
> review this: adding a uniqueness rule means the running application must change to keep working.
> Ships as a single schema change, applied in place, when the data already satisfies uniqueness.
> Prove no duplicates — and no multi-NULL on a nullable column — before classifying.

## OutSystems phrasing
"this attribute should be unique", "no two customers can share an email", "stop duplicate codes".

## SSDT meaning
`CONSTRAINT UQ_<Table>_<Col> UNIQUE (<Col>)` (or a unique index). SSDT builds the uniqueness
enforcement over **all existing rows** at deploy.

## The named trap
**Duplicate values block the build** — the deploy fails the instant two rows share a value. Second
trap: **UNIQUE treats NULL as a value and allows exactly ONE NULL row**; a nullable column with
several NULLs blocks the deploy too — the fix for legitimate multi-NULL is a **filtered unique
index**: `CREATE UNIQUE INDEX … WHERE <Col> IS NOT NULL`. Both belong to the constraint-is-a-claim
family, where a claimed value is validated against every existing row — see
`../../_index/constraint-is-a-claim/SKILL.md` (incl. the UNIQUE-one-NULL + filtered-index remedy);
do not re-derive the claim mechanics here.

## How it flips (the specifics only)
- no duplicates, no multi-NULL problem → ships as a single schema change, applied in place; a dev
  lead or an experienced developer reviews it because the running application must change to keep
  working (prove it).
- duplicates PRESENT → **flip to a pre-deployment de-dupe plus the declarative change, in one PR**:
  the pre-deploy de-dupe clears them BEFORE the unique build; a dev lead must review this because
  existing data is modified — see `../../_index/constraint-is-a-claim/SKILL.md`.
- nullable column with >1 NULL row → a filtered unique index, OR resolve the NULLs first; stays one
  PR if the filtered index suffices.
- \+ >1M rows → **added scrutiny**: at production row counts the uniqueness build and any de-dupe
  may block writes or run long (schedule a window — build + dedupe cost).
- \+ CDC-enabled → **added scrutiny**: CDC does not track the constraint, but coordinate with the
  team's rule for change-data-capture tables (see `../../_index/cdc/SKILL.md`).

## Prove it
Run the duplicate probe FIRST: `SELECT <Col>, COUNT(*) FROM <table> GROUP BY <Col> HAVING COUNT(*)
> 1` (and a NULL count for nullable columns). Then build + Strict publish: clean → uniqueness holds;
a build failure ("duplicate key was found") means the deployment is blocked. Author the pre-deploy
de-dupe (or the filtered index), re-run Strict clean. See `../../prove-on-dacpac/SKILL.md` +
`../../talk-to-local-sql/SKILL.md`. Seed: Status's `UX_Status_Code` is the clean positive; Product's
`DUPE` rows drive the flip to a blocked deploy.

## The verdict (to the developer)
You asked to make Email unique. On a disposable copy of Dev, SSDT refused it: 4 rows share an email,
and the uniqueness is built over every existing row when the constraint is added. The way through is
one release with a pre-deployment de-dupe that resolves those 4 before the unique constraint builds —
then it builds clean on the copy. If several blank emails are legitimate rather than duplicates, the
right tool instead is a filtered unique index that ignores NULLs. Are those four duplicate emails
safe to merge or delete, or is a repeated blank email legitimate here?

## The reasoning (in conversation)
Run the duplicate probe (`GROUP BY … HAVING COUNT(*) > 1`) and a NULL count before anything else —
the data decides how this ships, the SQL statement never can. The failure this avoids is "just add
the rule" over data that already holds duplicates: the deploy blocks. On a nullable column, remember
UNIQUE permits exactly one NULL — several NULLs block it the same way, and a filtered unique index is
the fix when repeated blanks are legitimate. See `../../_index/constraint-is-a-claim/SKILL.md`.

## On the record
Fragments for the pull request (`../../author-pr/SKILL.md`), record register.

**Review & release**
- A dev lead or an experienced developer should review this: the running application must change to
  keep working — any insert that would create a duplicate now fails. When a pre-deployment de-dupe
  removes duplicate rows first, a dev lead must review this: existing data is modified.
- Ships as a single schema change, applied in place; the uniqueness is built over the existing rows
  as it lands. With remediation, it ships as one release: a pre-deployment de-dupe clears the
  duplicates, then the unique constraint lands validated.
- Added scrutiny, when it applies: at production row counts the uniqueness build and any de-dupe may
  block writes or run long (schedule a window); a CDC-tracked table — CDC does not track the
  constraint, but the team's added-scrutiny rule applies (see `../../_index/cdc/SKILL.md`).

**Verification** — run in each environment after deployment
```sql
-- expect 0 rows: no value is shared across rows, so uniqueness holds
SELECT <Col>, COUNT(*) FROM <table> GROUP BY <Col> HAVING COUNT(*) > 1;

-- expect 1 row, is_unique = 1: the unique constraint or index exists and is enforced
SELECT name, is_unique FROM sys.indexes
WHERE object_id = OBJECT_ID('<table>') AND name = 'UQ_<Table>_<Col>';
```

**Rollback**
The unique constraint drops without data loss:
`ALTER TABLE <table> DROP CONSTRAINT UQ_<Table>_<Col>;` (or `DROP INDEX UQ_<Table>_<Col> ON
<table>;` for a unique or filtered index). A pre-deployment de-dupe is not auto-reversed; the rows it
removed or merged, recorded under Data remediation, are what a manual restore uses.

**Not verified**
- Application impact — any insert or update that would create a duplicate value now fails on the
  unique key ("duplicate key was found"); on a nullable column a second NULL fails the same way.
  Application-side handling is not confirmed here (@app-owner).
- Other environments — Test, UAT, and Prod may hold duplicates the disposable copy of Dev cannot
  see. Run the verification query before promotion.
- Production scale and timing — on a large table the uniqueness build and any de-dupe may block
  writes or run long; the small copy does not show it.
- Reversibility — dropping the constraint or index is lossless, but a pre-deployment de-dupe is not
  exercised in reverse here; the recorded originals are what a manual restore would use.
