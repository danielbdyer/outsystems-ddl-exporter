# Customer: widen Email to NVARCHAR(400) (data-preserving; applies in place, every value kept)

## Verdict
This PR enlarges `dbo.Customer.Email` from `NVARCHAR(256)` to `NVARCHAR(400)`; every existing value
still fits, so nothing is read or rewritten and the deploy cannot be refused. Confirm no consumer of
`Email` assumes the old 256-character limit (a fixed buffer, a downstream column mapping).

## Intent
The developer's stated intent for this PBI: make the `Email` field longer so longer addresses fit —
"increase Email". No work item supplied — attach one before merge.

## What changes
- `dbo.Customer.Email`: `NVARCHAR(256) NULL` → `NVARCHAR(400) NULL` (length only; type and
  nullability unchanged).

## Before promoting
- A dev lead approves this: enlarging a column keeps every value and the running application
  is unaffected.
- Where `Email` sits inside a non-clustered index key, confirm the widen does not push that key past
  the ~1700-byte limit (`NVARCHAR` storage doubles). `Email` is in no index here, and `NVARCHAR(400)`
  is 800 bytes, so that limit is not approached.

## The data
- No existing data is touched. `dbo.Customer` holds 5 rows; 3 carry a non-NULL `Email`, the longest
  24 characters — well inside both the old and the new width — and 2 are NULL. Every value already
  fits `NVARCHAR(400)`.

## How it ships
- One release, applied in place. The declarative difference is a single
  `ALTER TABLE dbo.Customer ALTER COLUMN [Email] NVARCHAR(400) NULL`, which contains no data-loss
  step — a wider type cannot lose a value — so the data-loss guard never fires and no script is
  needed.

## What proving showed
Published to a throwaway copy of the database on this branch (SQL Server 2022, `sqlpackage 170.4.83.3`).
- **Tried:** record the before state — `Email` `max_length = 512` bytes (`NVARCHAR(256)`), 3 non-NULL
  values, longest 24 characters, aggregate value digest `-715616066`. Widen it, script the
  difference → `ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR (400) NULL;`. Publish with
  `BlockOnPossibleDataLoss = true` → `Successfully published database.`
- **Did:** record the after state — `Email` `max_length = 800` bytes (`NVARCHAR(400)`), still
  3 non-NULL values, longest 24, aggregate value digest `-715616066` — identical to before.
- **Realized:** the widen changes the column's declared width and reads or rewrites no value; the
  identical digest is the proof no row was touched.
- Re-publish with no change → `Successfully published database.`; the schema difference is empty.

## After deploy — check
```sql
-- the widened length landed: NVARCHAR(400) => max_length 800 bytes
SELECT c.name, t.name AS type_name, c.max_length
FROM sys.columns c
JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID('dbo.Customer') AND c.name = 'Email';
```

## How to roll this back
Narrowing back to `NVARCHAR(256)` reverses the change and is lossless only while no value longer than
256 characters has been written. The forward widen changes no data, so reversing the schema is safe
immediately after deploy, before any longer value is stored; once a longer value exists, narrowing is
refused or truncates. Backing the change out was not exercised.

## Not checked / still open
- Application impact — a wider column is additive to callers, but a client that assumed the old
  length (a fixed-size buffer, a downstream contract, an SSIS column mapping) is not exercised on the
  copy; the application owner confirms it tolerates the new length.
- Other environments — row counts and SQL Server version differ; a widen that is metadata-only here
  may rebuild where the table is larger or the server older.
- Production scale and timing — if the change rebuilds rather than altering metadata, blocking or
  duration at production row counts is not shown by the 5-row copy; schedule a window if so.
- Reversibility — only the forward widen is proven; narrowing back is lossless only before a longer
  value is stored (see rollback).
