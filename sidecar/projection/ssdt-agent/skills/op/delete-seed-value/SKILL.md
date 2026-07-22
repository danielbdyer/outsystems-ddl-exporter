---
name: delete-seed-value
description: Use when the developer says "delete the Cancelled status value", "retire this lookup value", "remove the option nobody picks anymore", "drop this order type from the list" — retiring a row from an OutSystems Static Entity. SSDT destination = set IsActive = 0 in the seed MERGE, NOT a hard DELETE.
---

# Retire a static lookup value (hard-DELETE-orphans trap — deactivate, don't delete)

> **Default (provisional — the data decides).** Deactivate via `IsActive = 0`, carried by the seed
> MERGE in the post-deployment script and shipped as one release. A dev lead or an experienced
> developer should review it while the value is still referenced; any team member can review the
> retirement of a value nothing points at. Prove the reference before classifying: a hard DELETE is
> refused when fact rows or the application point at the value.

## OutSystems phrasing
"delete the Cancelled status value", "retire this lookup value", "remove this order type from the list".

## SSDT meaning
Set `IsActive = 0` on the seed row (the guarded `WHEN MATCHED` fires for that one row), preserving the row's identity and history. A hard `DELETE` of a seed row is the wrong move when anything references it. Never write ALTER.

## The named trap
**Deleting a lookup value the app still references** — a hard DELETE orphans every fact row pointing at it and breaks the app's `StatusId = 3` constant. The discipline is **deactivate, don't delete** — owned by `../../_index/idempotent-seed/SKILL.md`; do not re-derive it here.

## How it flips (the specifics only)
- value unreferenced, no fact rows point at it → a clean subtractive seed edit is defensible, but
  default to `IsActive = 0` anyway. Any team member can review it: nothing points at the value.
- **value the app / fact rows reference** → `IsActive = 0`, shipped as one release, reviewed by a dev
  lead or an experienced developer. A hard DELETE that orphans those fact rows removes data
  irreversibly and would need a principal — it is usually wrong; refuse the DELETE and propose
  deactivation.

## Prove it
Prove the reference exists before choosing: `SELECT COUNT(*) FROM <factTable> WHERE <fk> = <valueId>` — nonzero means DELETE orphans, deactivate instead. After the `IsActive = 0` edit, redeploy unchanged and assert **0 rows affected** + identical hash. See `prove-on-dacpac` / `talk-to-local-sql`. On the sample, `dbo.Category` is referenced by `dbo.Product.CategoryId`, so a hard DELETE of a Category value fires the orphan negative (STA-04N).

## The verdict (to the developer)
You asked to retire that value. Instead of deleting the row, I set `IsActive = 0` on it, so the N rows
that still point at it keep their referential integrity — nothing is orphaned. Deleting it outright
would have broken those fact rows and the app's hard-coded StatusId constant. It's gone from the
active list, but the row and its history stay put.

## The reasoning (in conversation)
Retiring a lookup value is a referential decision, not a plain data edit: other rows and the
application code point at it by id. Deactivating with `IsActive = 0` keeps that identity, so every
reference stays valid; a hard DELETE would silently orphan the fact rows that still point at the id.
That is why the safe move preserves the row and just marks it inactive. The full why is in
`../../_index/idempotent-seed/SKILL.md`.

## On the record
The fragment this op contributes to the pull request (`../../author-pr/SKILL.md`).

**Review & release**
- A dev lead or an experienced developer should review this: the value leaves the set the running
  application offers, so someone should confirm no active flow depends on it. Existing references are
  preserved — no fact row is orphaned. (A value nothing references can be reviewed by any team member.)
- Ships as one release: the seed MERGE in the post-deployment script re-runs and sets `IsActive = 0`
  for the retired row. The table definition is unchanged.
- Added scrutiny: none — the guarded `WHEN MATCHED` sets `IsActive = 0` on only the one retired row.

**Verification** — run in each environment after deployment
```sql
-- expect IsActive = 0: the value is retired in place, not deleted
SELECT IsActive FROM <lookupTable> WHERE <id> = <valueId>;

-- expect 0 rows: every fact row that referenced the value still resolves to a live lookup row
SELECT f.<fk> FROM <factTable> f
LEFT JOIN <lookupTable> l ON l.<id> = f.<fk>
WHERE f.<fk> = <valueId> AND l.<id> IS NULL;
```

**Rollback**
Reversible without data loss: set `IsActive = 1` for the row in the seed MERGE and redeploy. The row
was never deleted, so its identity and every reference stay intact. A hard DELETE would not be
auto-reversible — the row and the fact-row references it anchored could not be restored from the
deploy, which is the failure this retirement avoids.

**Not verified**
- Application impact. Any screen or logic that filters on `IsActive = 1` stops offering the value;
  code that still resolves it by id keeps working. Which paths the running application exercises is
  not confirmed on the disposable copy — @app-owner.
- Other environments. Test, UAT, and Prod may hold additional fact rows referencing the value, or
  carry it under a different id. The disposable copy of Dev cannot show this; run the verification
  query before promotion.
- Reversibility of downstream state. Flipping `IsActive` back to 1 restores the row, but any consumer
  that cached the active set is not refreshed by this change and is not exercised here.
