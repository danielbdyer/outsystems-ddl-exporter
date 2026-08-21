# Category: add 'Refunded' to the lookup (one new seed row; the label change touches exactly one row)

## Verdict
This PR adds a `Refunded` value to the `dbo.Category` lookup by extending the post-deploy seed MERGE.
The table definition is unchanged and no existing row is disturbed. Confirm the redeploy is silent and
that a label change touches exactly one row, not the whole table, before promoting.

## Intent
The developer's stated intent for this PBI: add a `Refunded` option to the Category list. No work item
supplied — attach one before merge.

## What changes
- `Data/Seed.sql` (the post-deploy MERGE): a new `WHEN NOT MATCHED THEN INSERT` row for `Refunded`. No
  table definition change.

## Before promoting
- Redeploy unchanged in each environment and confirm 0 rows affected and an identical hash — the new
  row is idempotent.
- If this PR also amends a label, confirm the guarded `WHEN MATCHED` updates exactly one row, not the
  table size.

## How it ships
- One release: the seed MERGE in the post-deploy script re-runs, inserting the new row (or amending the
  one changed row). The table definition is unchanged.

## The data
- One new row (`Refunded`) added to the seeded set by its explicit id. Existing rows keep their identity.

## What proving showed
Published to a throwaway copy on this branch.
- **Tried / Did:** publish → the seed MERGE inserts the new `Refunded` row. Redeploy unchanged → 0 rows
  affected, identical hash: the added row is idempotent.
- **Realized:** a label change must touch the one row that changed, not rewrite the table — the guarded
  `WHEN MATCHED` compares each column before updating, so its update rowcount is 1, never the table
  size. An unguarded `WHEN MATCHED` rewrites every row on every deploy and is broken even when the
  values still match (`skills/_index/idempotent-seed`).

## After deploy — check
```sql
-- the added (or amended) value is present once with its current label
SELECT Id, Code, IsActive FROM dbo.Category WHERE Code = N'Refunded';

-- redeploy unchanged: expect 0 rows affected by the seed MERGE and an identical content hash
SELECT COUNT(*) AS rows, CHECKSUM_AGG(BINARY_CHECKSUM(Id, Code, IsActive)) AS content_hash FROM dbo.Category;
```

## How to roll this back
Revert the `VALUES` block and redeploy. A label amendment reverts through the same guarded `WHEN
MATCHED`, which sets the `Code` back — lossless. A newly added value is removed by a separate
deactivate-don't-delete step (`delete-seed-value`): the seed MERGE has no delete-unmatched-by-source
branch, so taking the row out of the `VALUES` block leaves the inserted row in place.

## Not checked / still open
- Application impact — code that switches on the exact set of values (a screen bound to the list, logic
  that resolves a value by id) is not exercised on the copy (app owner).
- Other environments — whether Test, UAT, or Prod already hold this key with a different label, or the
  id is already taken, is unknown from the copy. Run the verification query before promotion.
