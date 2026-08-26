---
name: add-check
description: Use when the developer says "Total must be positive", "Status has to be one of these values", "age can't be negative" — any business rule enforced at the data layer via a CHECK constraint. SSDT re-validates every existing row at deploy; a violating row blocks it (Msg 547); over clean data the check trusts itself.
---

# Add a check constraint

> **Default (provisional — prove before you classify).** One release, applied in place — the check
> re-validates every existing row and, over clean data, trusts itself (`is_not_trusted = 0`); no data
> is modified. A dev lead or an experienced developer reviews it: the running application must produce
> conforming data or its writes are rejected. Prove zero violations on a copy first; a violating row
> blocks the deploy until it is reconciled.

> **SHIP terminal: ONE RELEASE, trusts itself.** Proven live on this branch (DBs `db_chk`, `db_chkv`):
> adding `CK_Order_Total CHECK (Total > 0)`, the generated script is `ALTER TABLE [dbo].[Order] WITH
> NOCHECK ADD CONSTRAINT [CK_Order_Total] CHECK (Total > 0);` then `ALTER TABLE [dbo].[Order] WITH CHECK
> CHECK CONSTRAINT [CK_Order_Total];` — the **same two statements as a foreign key** (F9/F10). Clean
> data → `Successfully published database.`, `is_not_trusted = 0`. One violating row (`Total = -5`) →
> **BLOCK `Msg 547`** ("conflicted with the CHECK constraint … column 'Total'"). `FINDINGS_AND_CHANGES.md` F10.
>
> **Proven precedent:** `../../../sample-prs/add-check.md` — the worked instance of the ten-section
> pull-request template (`../../author-pr/SKILL.md`) for this op, carrying the live messages.

## OutSystems phrasing
"Total must be positive", "Status has to be one of these values", "age can't be negative" — any
business rule the developer wants enforced at the data layer.

## SSDT meaning
`CONSTRAINT CK_<Table>_<Col> CHECK (<predicate>)` added to the table's CREATE. On publish, the
generated script adds the constraint `WITH NOCHECK` and then re-validates it `WITH CHECK CHECK`,
checking **every existing row**. Over clean data it ends trusted; a violating row blocks the deploy
with `Msg 547`. Edit the CREATE; never write `ALTER`.

## The named trap
A check is create-fk's twin: a single violating row blocks the deploy at the `WITH CHECK CHECK`
re-validation (`Msg 547`). Reaching for `WITH NOCHECK` by hand to get past the block is the
anti-pattern — it leaves the check **untrusted** (`is_not_trusted = 1`), so the optimizer ignores it
and bad rows stay. The block is the constraint working; the fix is to reconcile the data, not to dodge
the check. See `../../_index/constraint-is-a-claim/SKILL.md`.

## How it flips (the specifics only)
- every existing row satisfies the predicate → one release, in place; the check re-validates and trusts
  itself. A dev lead or an experienced developer reviews it, because the running application must now
  produce conforming data.
- violating rows present → a pre-deploy step brings them into compliance first, then the same
  declarative add re-validates and trusts itself; without the reconcile the publish blocks (`Msg 547`).
  A dev lead reviews it, because existing data is modified.
- violating rows that cannot be fixed in-place (legitimate legacy data) → stage across releases
  (quarantine / grandfather — see `../../_index/multi-phase/SKILL.md`), or accept an untrusted check
  only as a named, logged, explicit exception.
- >1M rows / first-time on this estate → added scrutiny: at production row counts the re-validation may
  run long or block writes (schedule a window); a first-time operation warrants an extra reviewer.

## Prove it
Run the violation probe FIRST: `SELECT COUNT(*) FROM <table> WHERE NOT (<predicate>)`. Then publish:
clean → the check lands trusted; any violating row → the publish blocks with `Msg 547` naming the
constraint and column. Author the pre-deploy reconcile, re-publish clean, and confirm
`SELECT is_not_trusted FROM sys.check_constraints WHERE name='CK_…'` returns 0. See
`../../prove-on-dacpac/SKILL.md` + `../../talk-to-local-sql/SKILL.md`.

## The verdict (to the developer)
You asked to enforce Total > 0. On a copy of Dev, every order already satisfies it, so the check adds
and re-validates in one publish and ends trusted — one release, nothing to reconcile. If any order had
a Total of 0 or less, the publish would be refused (`Msg 547`) until those rows were reconciled in a
pre-deploy step first. Going forward, a write that sets a Total to 0 or less is rejected.

## The reasoning (in conversation)
Run the violation probe (`WHERE NOT (<predicate>)`) before anything else: the same check text lands in
one release when every row conforms, and blocks with `Msg 547` the moment one row does not — so you
read what will happen from the violation count, never from the `.sql`. The declarative add re-validates
itself (`WITH NOCHECK ADD` then `WITH CHECK CHECK`), so a clean check ends trusted on its own; an
untrusted check comes only from a hand-written `WITH NOCHECK` dodge. See
`../../_index/constraint-is-a-claim/SKILL.md`.

## On the record
The pull request is an instance of the ten-section template in `../../author-pr/SKILL.md`; the worked
instance for this op — with the live messages — is `../../../sample-prs/add-check.md`. SHIP terminal:
**ONE RELEASE, trusts itself.** The fragment this operation contributes:

**Review & release**
- A dev lead or an experienced developer reviews this: the running application must produce conforming
  data, or its writes are rejected with error 547. When a pre-deploy step reconciles violating rows
  first, a dev lead reviews it: existing data is modified.
- Ships as one release, applied in place — the check re-validates every existing row in the publish and
  ends trusted. No data is modified unless a reconcile is needed.
- Added scrutiny, when it applies: at production row counts the re-validation may block writes or run
  long (schedule a window); a first-time operation on this estate.

**Verification** — run in each environment after deployment
```sql
-- expect 0 rows: no row violates the predicate
SELECT COUNT(*) FROM <table> WHERE NOT (<predicate>);

-- expect is_not_trusted = 0: the check is trusted, so the optimizer honors it
SELECT is_not_trusted FROM sys.check_constraints WHERE name = 'CK_<Table>_<Col>';
```

**Rollback**
The constraint drops without data loss: `ALTER TABLE <table> DROP CONSTRAINT CK_<Table>_<Col>;`. This
is also the cleanup for an untrusted check left by a blocked attempt. A pre-deploy reconcile is not
auto-reversed; the original values are recorded in the pre-deploy step's output.

**Not verified**
- Application impact — any code path that writes a value violating the predicate now fails with error
  547 ("conflicted with the CHECK constraint"); application-side validation is not confirmed here (app owner).
- Other environments — QA, UAT, and Prod may hold violating rows the copy cannot see. Run the
  violation probe before promotion.
- Production scale and timing — on a large table the re-validation may block writes or run long; the
  small copy does not show it.
