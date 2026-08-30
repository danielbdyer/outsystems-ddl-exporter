# Category: retire a lookup value (deactivate with IsActive = 0; a hard DELETE silently orphans the referencing rows)

## Verdict
This PR retires a `dbo.Category` value by setting `IsActive = 0` in the seed MERGE, not by deleting the
row. Products that still point at it keep their referential integrity — nothing is orphaned. A hard
`DELETE` is the wrong move: there is no foreign key declared on `Product.CategoryId`, so a delete of a
referenced value is **not refused** — it succeeds and silently orphans the rows that pointed at it.
Confirm no active flow still needs the value before promoting.

## Intent
The developer's stated intent for this PBI: retire a Category value so it no longer appears in the
active list, without breaking the rows that already reference it. No work item supplied — attach one
before merge.

## What changes
- `Data/Seed.sql` (the post-deploy MERGE): set `IsActive = 0` on the retired Category row (the guarded
  `WHEN MATCHED` fires for that one row). The table definition is unchanged; the row is not deleted.

## Before promoting
- Run the reference query (below) in each environment. A nonzero count means rows still point at the
  value — deactivate, do not delete. Confirm no active screen or flow still offers it.

## The data
- One Category row is marked inactive (proven on `Category` id 1, `Hardware`, referenced by
  `Product` 1 and 4). The rows that reference it are unchanged and still resolve.

## How it ships
- One release: the seed MERGE re-runs and sets `IsActive = 0` for the retired row. The row and its
  history stay in place, so every reference to its id stays valid.

## What proving showed (published to a throwaway copy, this branch)
Proven on a copy this branch (`pg_move`, sqlpackage 170.4.83.3).
- **Tried (the wrong move):** a hard `DELETE FROM dbo.Category WHERE Id = 1` — a value `Product`
  references — **succeeded** (`@@ROWCOUNT = 1`), it was **not refused**. `Product` 1 and 4 (which carry
  `CategoryId = 1`) were left **orphaned**: they now point at a `Category` id with no row. There is no
  foreign key declared on `Product.CategoryId`, so nothing blocked the delete — the integrity break is
  silent.
- **Did:** set `IsActive = 0` instead → the value leaves the active list, the row and its references
  stay intact, and the redeploy is silent — deactivate, don't delete.
- **Realized:** removing the value from the seed's `VALUES` block does **not** delete the row either —
  the MERGE has no delete-by-absence branch, so the seed is additive. Retiring a value is therefore a
  deliberate `IsActive = 0`, never a delete. (Were a foreign key declared, the delete would instead
  block `Msg 547`; here none is, so it orphans — the worse outcome, and the reason to deactivate.)

## After deploy — check (each environment)
```sql
-- the value is retired in place, not deleted: expect IsActive = 0 (e.g. Category id 1)
SELECT IsActive FROM dbo.Category WHERE Id = 1;

-- every row that referenced the value still resolves to a live lookup row: expect 0 rows
SELECT p.CategoryId FROM dbo.Product p
LEFT JOIN dbo.Category c ON c.Id = p.CategoryId
WHERE p.CategoryId = 1 AND c.Id IS NULL;
```

## How to roll this back
Reversible without data loss: set `IsActive = 1` for the row in the seed MERGE and redeploy. The row was
never deleted, so its identity and every reference stay intact. A hard DELETE would not be
auto-reversible — the row, and the referencing rows it anchored, could not be restored from the deploy,
which is the failure this retirement avoids. Backing the change out was not exercised.

## Not checked / still open
- Application impact — a screen or logic that filters on `IsActive = 1` stops offering the value; code
  that still resolves it by id keeps working. Which paths the running application exercises is not
  confirmed on the copy (app owner).
- Other environments — QA, UAT, and Prod may hold additional rows referencing the value, or carry it
  under a different id. Run the reference query before promotion.
- Cached consumers — a consumer that cached the active set is not refreshed by this change and is not
  exercised here.
