# OrderLine: change LineNumber from INT to BIGINT (a widening retype — applies in place, every value preserved)

## Verdict
This PR changes `dbo.OrderLine.LineNumber` from `INT` to `BIGINT`; every existing value already fits
the wider type, so nothing is read or rewritten and the deploy cannot be refused. Confirm the
direction is a genuine widening — a narrowing or value-reshaping cast would be a different, staged
change.

## Intent
The developer's stated intent for this PBI: store `LineNumber` as a bigger number type — "make it a
long integer" — so the column is not capped at the `INT` range. No work item supplied — attach one
before merge.

## What changes
- `dbo.OrderLine.LineNumber`: `INT NOT NULL` → `BIGINT NOT NULL` (a widening type change).

## Before promoting
- Any team member can approve this: a widening type change keeps every value and the running
  application is unaffected.
- Confirm the direction is `INT → BIGINT`, not the reverse. A narrowing (`BIGINT → INT`, `INT →
  TINYINT`) or a value-reshaping cast (Text → Date) is refused on a populated table and is a
  different change — retype-explicit — not this one.

## The data
- No existing data is touched. `dbo.OrderLine` holds 8 rows; `LineNumber` values are small line
  positions (maximum 3). Every one already fits `BIGINT`.

## How it ships
- One release, applied in place. The declarative difference is a single
  `ALTER TABLE dbo.OrderLine ALTER COLUMN [LineNumber] BIGINT NOT NULL`, which contains no data-loss
  step — every `INT` value fits `BIGINT` — so the data-loss guard never fires and no script is
  needed.

## What proving showed
Published to a throwaway copy of the database on this branch (SQL Server 2022, `sqlpackage 170.4.83.3`).
- **Tried:** record the before state — `LineNumber` type `int`, 8 rows, `SUM(LineNumber) = 13`,
  `MAX(LineNumber) = 3`, aggregate value digest `129`. Retype it, script the difference →
  `ALTER TABLE [dbo].[OrderLine] ALTER COLUMN [LineNumber] BIGINT NOT NULL;`. Publish with
  `BlockOnPossibleDataLoss = true` → `Successfully published database.`
- **Did:** record the after state — `LineNumber` type `bigint`, 8 rows, `SUM = 13`, `MAX = 3`,
  aggregate value digest `129` — identical to before.
- **Realized:** the widening changes the column's declared type and reshapes no value; the identical
  sum, maximum, and digest are the proof no row was touched.
- Re-publish with no change → `Successfully published database.`; the schema difference is empty.

## After deploy — check
```sql
-- the column ends at the widened type, expect type_name = bigint
SELECT c.name, ty.name AS type_name
FROM sys.columns c
JOIN sys.types ty ON ty.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID('dbo.OrderLine') AND c.name = 'LineNumber';
```

## How to roll this back
Narrowing `BIGINT` back to `INT` reverses the change and is lossless only while no value larger than
the `INT` range has been written; re-narrowing is itself the lossy direction, subject to the
row-presence and overflow refusal. The forward widen changes no data, so reversing the schema is safe
immediately after deploy, before any larger value is stored. Backing the change out was not
exercised.

## Not checked / still open
- Application impact — a widened column can change how strongly-typed application code handles it (an
  Int32 mapping now backing a `BIGINT` column, an SSIS column type); application-side type handling
  is not confirmed here. The application owner owns it.
- Other environments — the type change is proven on one copy; that no other environment holds a value
  that would make the reverse (narrowing) lossy is not shown here.
- Production scale and timing — whether the `ALTER COLUMN` is metadata-only or a size-of-data rewrite
  at production row counts is not shown by the 8-row copy; on a large table schedule a window.
- Reversibility — only the forward widening is exercised; narrowing back is the lossy direction (see
  rollback).
