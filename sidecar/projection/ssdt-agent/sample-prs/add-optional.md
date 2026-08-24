# Customer: add an optional MiddleName column (nullable — applies in place, no existing row touched)

## Verdict
This PR adds a nullable `MiddleName` column to `dbo.Customer`; the five existing rows take NULL and
no stored value is read or rewritten. Confirm the application code that will fill `MiddleName` is
ready, or accept that the column stays NULL until it is.

## Intent
The developer's stated intent for this PBI: add an optional attribute to Customer that can be left
blank — "a field that doesn't have to be filled". No work item supplied — attach one before merge.

## What changes
- `dbo.Customer.MiddleName`: add `NVARCHAR(100) NULL` (a new nullable column).

## Before promoting
- Any team member can approve this: the column is additive and the running application keeps working
  unchanged — every existing row takes NULL, which the new column already permits.
- Run the verification query below in each environment after deploy and confirm `is_nullable = 1`.

## The data
- No existing data is touched. `dbo.Customer` holds 5 rows; each takes NULL in the new column as it
  lands. NULL is a valid value for every existing row, so nothing can conflict with the add.

## How it ships
- One release, applied in place. The declarative difference is a single
  `ALTER TABLE dbo.Customer ADD [MiddleName] NVARCHAR(100) NULL`, which contains no data-loss step,
  so the data-loss guard (`BlockOnPossibleDataLoss = true`) never fires and no pre-deploy or
  post-deploy script is needed.

## What proving showed
Published to a throwaway copy of the database on this branch (SQL Server 2022, `sqlpackage 170.4.83.3`).
- **Tried:** add the column, script the difference → a single statement,
  `ALTER TABLE [dbo].[Customer] ADD [MiddleName] NVARCHAR (100) NULL;`. Publish with
  `BlockOnPossibleDataLoss = true` → `Successfully published database.`
- **Did:** query the end state → `MiddleName` is `is_nullable = 1`, `nvarchar`, `max_length = 200`
  bytes (`NVARCHAR(100)`); `dbo.Customer` still holds 5 rows and 0 of them carry a `MiddleName`
  value (all NULL).
- **Realized:** the add cannot be blocked, because NULL satisfies every row already in the table.
- Re-publish with no change → `Successfully published database.`; the schema difference is empty
  and the idempotent seed captures no rows.

## After deploy — check
```sql
-- the optional column landed and permits NULL, expect 1 row with is_nullable = 1
SELECT c.name, c.is_nullable
FROM sys.columns c
WHERE c.object_id = OBJECT_ID('dbo.Customer') AND c.name = 'MiddleName';
```

## How to roll this back
Remove the column from the `CREATE TABLE` and republish; the difference becomes
`ALTER TABLE dbo.Customer DROP COLUMN MiddleName;`. This loses nothing while the column is unwritten —
every row holds NULL at deploy. Once the application writes values into `MiddleName`, dropping the
column discards them. Backing the change out was not exercised.

## Not checked / still open
- Application impact — a nullable add does not change existing application behaviour, but any code
  meant to populate `MiddleName` is not exercised by the copy. The application owner confirms it.
- Other environments — the add is proven on one copy; run the verification query in Test, UAT, and
  Prod after deploy.
- Production scale and timing — the add is metadata-only on modern SQL Server with
  `IgnoreColumnOrder = True`; that it stays metadata-only at production row counts and on the
  target's edition is not confirmed by the 5-row copy.
- Reversibility — the forward add is proven; once a value is written into `MiddleName`, dropping it
  is lossy (see rollback).
