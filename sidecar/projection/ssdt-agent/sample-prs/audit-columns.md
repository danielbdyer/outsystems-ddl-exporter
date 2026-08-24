# Customer: add CreatedBy/CreatedOn/ModifiedBy/ModifiedOn (NOT NULL with defaults that stamp every existing row as the columns land)

## Verdict
This PR adds four audit columns to `dbo.Customer`, each `NOT NULL` with a default, so the five
existing rows are stamped as the columns land and the add applies in one clean step. Confirm the
stamped values for existing rows are acceptable: those rows record the deploy time and the deploying
login, not the real time and author of each row's creation.

## Intent
The developer's stated intent for this PBI: add audit columns that stamp who created or last changed
each Customer and when — "basic audit fields", `CreatedBy` / `CreatedOn` / `ModifiedBy` /
`ModifiedOn` — and have them filled for every row. No work item supplied — attach one before merge.

## What changes
- `dbo.Customer.CreatedBy`: add `NVARCHAR(256) NOT NULL DEFAULT (SUSER_SNAME())`.
- `dbo.Customer.CreatedOn`: add `DATETIME2(3) NOT NULL DEFAULT (SYSUTCDATETIME())`.
- `dbo.Customer.ModifiedBy`: add `NVARCHAR(256) NOT NULL DEFAULT (SUSER_SNAME())`.
- `dbo.Customer.ModifiedOn`: add `DATETIME2(3) NOT NULL DEFAULT (SYSUTCDATETIME())`.

## Before promoting
- A dev lead or an experienced developer should approve this: the existing rows receive stamped
  values, and the running application must keep the four columns filled on every insert and update
  going forward.
- Confirm the application (or a trigger) writes these columns on new inserts and on updates. A `NOT
  NULL` column with only the add-time default rejects the next insert that does not supply a value.

## The data
- `dbo.Customer` holds 5 rows and none had audit columns before this change. Each default fills all
  5 rows as its column lands: `CreatedBy` and `ModifiedBy` take the deploying login, `CreatedOn` and
  `ModifiedOn` take the deploy time.

## How it ships
- One release, applied in place. The declarative difference adds the four columns with their default
  constraints in one `ALTER TABLE dbo.Customer ADD ...`. Each default stamps every existing row as
  the column lands, so no row is left without a value and the difference contains no data-loss step —
  the data-loss guard (`BlockOnPossibleDataLoss = true`) does not fire.
- The lighter alternative, if the columns need not be mandatory, is to add them `NULL`: existing rows
  stay NULL and the add is purely additive. `NOT NULL` with no default is the shape to avoid on a
  populated table — it is refused because the existing rows have no value, and would need a pre-deploy
  backfill first.

## What proving showed
Published to a throwaway copy of the database on this branch (SQL Server 2022, `sqlpackage 170.4.83.3`).
- **Tried:** add the four columns `NOT NULL` with the defaults above; script the difference → one
  statement,
  `ALTER TABLE [dbo].[Customer] ADD [CreatedBy] NVARCHAR (256) CONSTRAINT [DF_Customer_CreatedBy]
  DEFAULT (SUSER_SNAME()) NOT NULL, [CreatedOn] DATETIME2 (3) CONSTRAINT [DF_Customer_CreatedOn]
  DEFAULT (SYSUTCDATETIME()) NOT NULL, [ModifiedBy] ... , [ModifiedOn] ... ;`. Publish with
  `BlockOnPossibleDataLoss = true` → `Successfully published database.`
- **Did:** query the end state → `dbo.Customer` holds 5 rows and all 5 carry a value in every audit
  column (for example `CreatedBy = sa`, `CreatedOn = 2026-08-21T10:42:38.753`). No row was left NULL.
- **Realized:** a fresh column's block is cured by supplying the value — the default stamps the rows
  already there as the column lands, so the `NOT NULL` add stays a single clean step. (The same four
  columns added as `NULL` also published clean on a separate copy, leaving all 5 existing rows NULL —
  the additive baseline.)
- Re-publish with no change → `Successfully published database.`; the schema difference is empty and
  the defaults do not re-stamp.

## After deploy — check
```sql
-- no existing row is missing an audit value, expect 0
SELECT COUNT(*) AS RowsMissingAudit
FROM dbo.Customer
WHERE CreatedBy IS NULL OR CreatedOn IS NULL OR ModifiedBy IS NULL OR ModifiedOn IS NULL;
```

## How to roll this back
Drop the four columns:
`ALTER TABLE dbo.Customer DROP COLUMN CreatedBy, CreatedOn, ModifiedBy, ModifiedOn;`. This returns the
table to its prior shape without touching pre-existing data — the columns held only audit values this
change introduced, including the ones the defaults stamped. Backing the change out was not exercised.

## Not checked / still open
- The stamped values for existing rows are a proxy — `CreatedOn` records the deploy time, not each
  row's real creation time, and `CreatedBy` records the deploying login, not the real author. Whether
  that proxy is acceptable, or the rows need a truer historical value, is the developer's call.
- Application impact — whether the application or a trigger stamps these columns on future inserts and
  updates is not confirmed here; a `NOT NULL` column with no app-side write rejects the next insert
  that omits it. The application owner owns it.
- Other environments — QA, UAT, and Prod hold rows the copy does not, which the defaults also stamp
  at their own deploy time and login. Run the verification query in each environment after deploy.
- Production scale and timing — on the 5-row copy the add is immediate; at large row counts stamping
  every existing row as the columns land can run long. Schedule a window if the table is large.
