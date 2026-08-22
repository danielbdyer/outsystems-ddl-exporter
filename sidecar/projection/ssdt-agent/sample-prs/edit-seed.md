# Category: add 'Refunded' to the lookup (one new seed row; the label change touches exactly one row)

## Verdict
This PR adds a `Refunded` value to the `dbo.Category` lookup by extending the post-deploy seed MERGE.
The table definition is unchanged and no existing row is disturbed. Confirm the redeploy is silent and
that a label change touches exactly one row, not the whole table, before promoting.

## Intent
The developer's stated intent for this PBI: add a `Refunded` option to the Category list. No work item
supplied — attach one before merge.

## What changes
- `Data/Seed.sql` (the post-deploy MERGE): a new `WHEN NOT MATCHED THEN INSERT` row for `Refunded`
  (explicit id 4). No table definition change.

## Before promoting
- Redeploy unchanged in each environment and confirm the seed touches 0 rows and leaves an identical
  hash — the new row is idempotent.
- If this PR also amends a label, confirm the guarded `WHEN MATCHED` updates exactly one row, not the
  table size.

## The data
- One new row (`Refunded`, id 4) added to the seeded set by its explicit id. Existing rows keep their
  identity.

## How it ships
- One release: the seed MERGE in the post-deploy script re-runs, inserting the new row (or amending the
  one changed row). The table definition is unchanged.

## What proving showed (published to a throwaway copy, this branch)
Proven on a copy this branch (`pg_seed`, sqlpackage 170.4.83.3).
- **Tried:** the seed MERGE extended with `(4, N'Refunded', 1)` — the first run inserted the new row,
  `@@ROWCOUNT = 1`; re-running it over the now-matching data touched **0 rows** (silent).
- **Did:** amending one label — `Category 1` `Hardware → Hardware Pro` — through the guarded
  `WHEN MATCHED` touched exactly **1 row** (`@@ROWCOUNT = 1`), the one that changed, not the table.
- **Realized:** the guard is what keeps a label change to one row. The same MERGE written **unguarded**
  (`WHEN MATCHED THEN UPDATE` with no column comparison) touched all 3 existing rows on a no-op run —
  broken even when the values still match. A label change must touch the one row that changed, never
  rewrite the table.

## After deploy — check (each environment)
```sql
-- the added (or amended) value is present once with its current label
SELECT Id, Code, IsActive FROM dbo.Category WHERE Code = N'Refunded';

-- redeploy unchanged: expect an identical content hash; the guarded MERGE touches 0 rows on the re-run
SELECT COUNT(*) AS rows, CHECKSUM_AGG(BINARY_CHECKSUM(Id, Code, IsActive)) AS content_hash FROM dbo.Category;
```

## How to roll this back
Revert the `VALUES` block and redeploy. A label amendment reverts through the same guarded `WHEN
MATCHED`, which sets the `Code` back — lossless. A newly added value is removed by a separate
deactivate-don't-delete step (`delete-seed-value`): the seed MERGE has no delete-unmatched-by-source
branch, so taking the row out of the `VALUES` block leaves the inserted row in place. Backing the
change out was not exercised.

## Not checked / still open
- Application impact — code that switches on the exact set of values (a screen bound to the list, logic
  that resolves a value by id) is not exercised on the copy (app owner).
- Other environments — whether Test, UAT, or Prod already hold this key with a different label, or the
  id is already taken, is unknown from the copy. Run the verification query before promotion.
