# Product: make the Code index unique (a duplicate Code blocks the rebuild until it is reconciled)

## Verdict
This PR changes `IX_Product_Code` from non-unique to unique. SSDT rebuilds the index (`DROP` +
`CREATE`) over every row; if two products share a Code the build is refused (`Msg 1505`) until the
duplicates are reconciled. Confirm no two products share a Code in each environment before promoting.

## Intent
The developer's stated intent for this PBI: enforce that no two products share a Code, so a duplicate
Code becomes impossible. No work item supplied — attach one before merge.

## What changes
- `dbo.Product`: change `IX_Product_Code` to a unique index. SSDT does not ALTER an index in place — it
  emits `DROP INDEX` then `CREATE UNIQUE INDEX`.

## Before promoting
- Run the duplicate query (below) in each environment and confirm it returns 0 rows. If any two
  products share a Code, reconcile them in a pre-deploy first (merge or correct) — a data-owner decision.

## The data
- The Product Codes are distinct (a duplicate would block the build). No row is modified; an index is a
  derived structure.

## How it ships
- One release, applied in place. The change is a full rebuild (`DROP INDEX` + `CREATE UNIQUE INDEX`)
  over every row, which takes a write-blocking lock scaled to row count. Clean data → the unique index
  builds; a duplicate → the build is refused (`Msg 1505`).

## What proving showed
Published to a throwaway copy on this branch.
- **Tried:** over Code values that share a duplicate, publish the uniqueness change → refused.
  `Msg 1505`: "the CREATE UNIQUE INDEX statement terminated because a duplicate key was found … The
  duplicate key value is (…)" — the message names the collision.
- **Did:** over distinct Codes, the `DROP INDEX` + `CREATE UNIQUE INDEX` rebuild lands clean;
  `is_unique = 1`.
- **Realized:** it is a value block (the duplicate is named), not a row-presence block — reconcile the
  duplicate and the rebuild lands.

## After deploy — check
```sql
-- no two products share a Code, expect 0 rows
SELECT Code, COUNT(*) FROM dbo.Product GROUP BY Code HAVING COUNT(*) > 1;

-- the index is now unique, expect one row with is_unique = 1
SELECT name, is_unique FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Product') AND name = 'IX_Product_Code';
```

## How to roll this back
Revert to the non-unique index (revert the `.sql` edit and republish); SSDT does `DROP INDEX` +
`CREATE INDEX` to restore the prior shape. Lossless — an index holds no source data — but the rebuild
runs again. A pre-deploy de-dupe is not auto-reversed; the rows it merged are in its output. Backing
the change out was not exercised.

## Not checked / still open
- Application impact — once the index is unique, any insert or update that would create a duplicate
  Code fails ("duplicate key was found"); application-side handling is not confirmed here (app owner).
- Other environments — Test, UAT, and Prod may hold duplicate Codes the copy cannot see. Run the
  duplicate query before promotion.
- Production scale and timing — on a large table the `DROP` + `CREATE` rebuild and any de-dupe may
  block writes or run long; the small copy does not show it.
