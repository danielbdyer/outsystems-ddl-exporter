---
name: add-check
description: Use when the developer says "Total must be positive", "Status has to be one of these values", "age can't be negative" — any business rule enforced at the data layer via a CHECK constraint. SSDT validates every existing row at deploy; violating rows block the deployment; NOCHECK skips validation but leaves the constraint untrusted.
---

# Add a check constraint

> **Default (provisional — the data decides).** A dev lead or an experienced developer should
> review this: adding the check means the running application must change to keep working. Ships as
> a single schema change, applied in place, when every existing row already satisfies the predicate.
> Prove zero violations on a disposable copy before classifying.

## OutSystems phrasing
"Total must be positive", "Status has to be one of these values", "age can't be negative" — any
business rule the developer wants enforced at the data layer.

## SSDT meaning
`CONSTRAINT CK_<Table>_<Col> CHECK (<predicate>)`. SSDT adds the constraint **WITH CHECK** by
default, validating **every existing row** at deploy.

## The named trap
**Existing rows that violate the predicate block the deployment.** The escape hatch — **WITH
NOCHECK** — is its own trap: it skips validation but leaves the constraint **untrusted**
(`is_not_trusted = 1`), so the optimizer ignores it and bad rows can already be present. Both belong
to the constraint-is-a-claim family — a violating value blocks the deploy, and the trust ladder
governs NOCHECK — see `../../_index/constraint-is-a-claim/SKILL.md`; do not re-derive the claim or
NOCHECK mechanics here.

## How it flips (the specifics only)
- every existing row satisfies the predicate → ships as a single schema change, applied in place; a
  dev lead or an experienced developer reviews it because the running application must change to keep
  working (prove it).
- violating rows PRESENT → **flip to a pre-deployment fix-up plus the declarative change, in one
  PR**: the pre-deploy fix-up brings violators into compliance BEFORE the WITH CHECK validation; a
  dev lead must review this because existing data is modified — see
  `../../_index/constraint-is-a-claim/SKILL.md`.
- violating rows that CANNOT be fixed in-place (legitimate legacy data) → consider staging across
  releases (quarantine / grandfather — see `../../_index/multi-phase/SKILL.md`), or accept WITH
  NOCHECK *only* as a named, documented, explicitly-untrusted decision.
- \+ >1M rows / first-time on this estate → **added scrutiny**: at production row counts the WITH
  CHECK validation may run long or block writes (schedule a window), and a first-time operation
  warrants an extra reviewer.
- \+ CDC-enabled → **added scrutiny**: coordinate with the team's rule for change-data-capture
  tables (see `../../_index/cdc/SKILL.md`).

## Prove it
Run the violation probe FIRST: `SELECT COUNT(*) FROM <table> WHERE NOT (<predicate>)`. Then build +
Strict publish (adds WITH CHECK): clean → zero violations; a build failure ("conflicted with the
CHECK constraint") means the deployment is blocked. Author the pre-deploy fix-up, re-run Strict
clean. If anyone proposes WITH NOCHECK, prove the cost:
`SELECT is_not_trusted FROM sys.check_constraints WHERE name='CK_…'` returns 1. See
`../../prove-on-dacpac/SKILL.md` + `../../talk-to-local-sql/SKILL.md`.

## The verdict (to the developer)
You asked to enforce Total > 0. On a disposable copy of Dev, SSDT refused it: 7 existing rows have a
Total of 0 or less, and the check validates every existing row when it is added. The way through is
a pre-deployment script that brings those 7 rows into compliance before the check validates — then
it lands clean and stays trusted. Skipping validation with NOCHECK would let it land, but the check
would be untrusted and the optimizer would ignore it, so I wouldn't take that route. What should
those 7 rows become — is there a correct Total for them, or should they be handled another way?

## The reasoning (in conversation)
Run the violation probe (`WHERE NOT (<predicate>)`) before anything else, and treat NOCHECK as a
debt, not a shortcut: if you ever reach for it, prove the untrusted state and get the constraint
back to trusted. The failure this avoids is "just add the rule" over legacy data that already
violates it — the deploy blocks, or NOCHECK silences it and you quietly ship a constraint the
optimizer will not use. See `../../_index/constraint-is-a-claim/SKILL.md`.

## On the record
Fragments for the pull request (`../../author-pr/SKILL.md`), record register.

**Review & release**
- A dev lead or an experienced developer should review this: the running application must change to
  keep working. When a pre-deployment script fixes violating rows first, a dev lead must review this:
  existing data is modified.
- Ships as a single schema change, applied in place; the check validates the existing rows as it
  lands. With remediation, it ships as one release: a pre-deployment script brings the violating rows
  into compliance, then the check lands validated and trusted.
- Added scrutiny, when it applies: at production row counts the WITH CHECK validation may block
  writes or run long (schedule a window); a first-time operation on this estate; a CDC-tracked table
  (see `../../_index/cdc/SKILL.md`).

**Verification** — run in each environment after deployment
```sql
-- expect 0 rows: no row violates the predicate
SELECT COUNT(*) FROM <table> WHERE NOT (<predicate>);

-- expect is_not_trusted = 0: the check is trusted, so the optimizer honors it
SELECT is_not_trusted FROM sys.check_constraints WHERE name = 'CK_<Table>_<Col>';
```

**Rollback**
The constraint drops without data loss: `ALTER TABLE <table> DROP CONSTRAINT CK_<Table>_<Col>;`. A
pre-deployment fix-up UPDATE is not auto-reversed; the original values recorded under Data
remediation are what a manual restore uses.

**Not verified**
- Application impact — any code path that writes a value violating the predicate now fails on the
  check constraint conflict ("conflicted with the CHECK constraint"); application-side validation is
  not confirmed here (@app-owner).
- Other environments — Test, UAT, and Prod may hold violating rows the disposable copy of Dev cannot
  see. Run the verification query before promotion.
- Production scale and timing — on a large table the WITH CHECK validation may block writes or run
  long; the small copy does not show it.
- Reversibility — if a pre-deployment script remediated rows, backing that out is not exercised here;
  the recorded originals are what a manual restore would use.
