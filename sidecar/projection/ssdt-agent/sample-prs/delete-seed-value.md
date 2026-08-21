# Category: retire a lookup value (deactivate with IsActive = 0, not a hard DELETE, so no fact row is orphaned)

## Verdict
This PR retires a `dbo.Category` value by setting `IsActive = 0` in the seed MERGE, not by deleting the
row. Products that still point at it keep their referential integrity — nothing is orphaned. Confirm no
active flow still needs the value before promoting; a hard DELETE of a referenced value is refused.

## Intent
The developer's stated intent for this PBI: retire a Category value so it no longer appears in the
active list, without breaking the rows that already reference it. No work item supplied — attach one
before merge.

## What changes
- `Data/Seed.sql` (the post-deploy MERGE): set `IsActive = 0` on the retired Category row (the guarded
  `WHEN MATCHED` fires for that one row). The table definition is unchanged; the row is not deleted.

## Before promoting
- Run the reference query (below) in each environment. A nonzero count means Products still point at the
  value — deactivate, do not delete. Confirm no active screen or flow still offers it.

## How it ships
- One release: the seed MERGE re-runs and sets `IsActive = 0` for the retired row. The row and its
  history stay in place, so every reference to its id stays valid.

## The data
- One Category row is marked inactive. The rows that reference it (for example `Product.CategoryId`) are
  unchanged and still resolve.

## What proving showed
Published to a throwaway copy on this branch.
- **Tried (the wrong move):** a hard `DELETE` of a Category value that `Product` references → it orphans
  every Product row pointing at the id and breaks the application's constant. Refused as the approach.
- **Did:** set `IsActive = 0` instead → the value leaves the active list, the row and its references stay
  intact, and a redeploy is silent (0 rows, identical hash). `skills/_index/idempotent-seed`
  (deactivate-don't-delete).

## After deploy — check
```sql
-- the value is retired in place, not deleted: expect IsActive = 0
SELECT IsActive FROM dbo.Category WHERE Id = <valueId>;

-- every fact row that referenced the value still resolves to a live lookup row: expect 0 rows
SELECT p.CategoryId FROM dbo.Product p
LEFT JOIN dbo.Category c ON c.Id = p.CategoryId
WHERE p.CategoryId = <valueId> AND c.Id IS NULL;
```

## How to roll this back
Reversible without data loss: set `IsActive = 1` for the row in the seed MERGE and redeploy. The row was
never deleted, so its identity and every reference stay intact. A hard DELETE would not be
auto-reversible — the row and the fact-row references it anchored could not be restored from the deploy,
which is the failure this retirement avoids.

## Not checked / still open
- Application impact — a screen or logic that filters on `IsActive = 1` stops offering the value; code
  that still resolves it by id keeps working. Which paths the running application exercises is not
  confirmed on the copy (app owner).
- Other environments — Test, UAT, and Prod may hold additional fact rows referencing the value, or carry
  it under a different id. Run the reference query before promotion.
- Cached consumers — a consumer that cached the active set is not refreshed by this change and is not
  exercised here.
