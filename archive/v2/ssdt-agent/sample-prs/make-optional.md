# CustomerAddress: make Line1 optional (a loosening — applies in place; the risk is downstream, not at deploy)

## Verdict
This PR lets `dbo.CustomerAddress.Line1` hold NULL where it was required; every stored value is
kept and the deploy cannot be refused. Confirm no report, query, or job that reads `Line1` breaks on
a NULL before a row is written blank.

## Intent
The developer's stated intent for this PBI: make the address `Line1` attribute optional — "let it be
blank now", so a CustomerAddress can be saved before the street line is known. No work item supplied —
attach one before merge.

## What changes
- `dbo.CustomerAddress.Line1`: `NVARCHAR(120) NOT NULL` → `NVARCHAR(120) NULL` (nullability only;
  type and width unchanged).

## Before promoting
- A dev lead approves this with the lightest look when nothing downstream assumes `Line1` is
  always populated. If a report, an ETL/SSIS job, or application code does assume it, the approval
  weighs that the consumer must change to tolerate a NULL. This changes what the lead weighs, not
  how it ships.
- Check with the owners of any consumer that reads `Line1` and confirm a NULL is safe there.

## The data
- No existing data is touched. `dbo.CustomerAddress` holds 5 rows and every one already carries a
  `Line1` value; the loosening changes the rule, not the values.

## How it ships
- One release, applied in place. The declarative difference is a single
  `ALTER TABLE dbo.CustomerAddress ALTER COLUMN [Line1] NVARCHAR(120) NULL`, which contains no
  data-loss step. A loosening removes a rule; no existing row can violate "allows NULL", so the
  data-loss guard never fires and no script is needed.

## What proving showed
Published to a throwaway copy of the database on this branch (SQL Server 2022, `sqlpackage 170.4.83.3`).
- **Tried:** with `Line1` at `is_nullable = 0` and all 5 rows populated, loosen it, script the
  difference → `ALTER TABLE [dbo].[CustomerAddress] ALTER COLUMN [Line1] NVARCHAR (120) NULL;`.
  Publish with `BlockOnPossibleDataLoss = true` → `Successfully published database.`
- **Did:** query the end state → `Line1` is `is_nullable = 1`; `dbo.CustomerAddress` still holds
  5 rows and all 5 keep their original non-NULL value.
- **Realized:** a loosening cannot be blocked at deploy — the danger lives where the deploy cannot
  see it, in the consumers that assumed `Line1` was always filled.
- Re-publish with no change → `Successfully published database.`; the schema difference is empty.

## After deploy — check
```sql
-- the column now permits NULL, expect is_nullable = 1
SELECT c.name, c.is_nullable
FROM sys.columns c
WHERE c.object_id = OBJECT_ID('dbo.CustomerAddress') AND c.name = 'Line1';
```

## How to roll this back
The loosening writes no data, so there is nothing to restore on the data side. Reversing the schema
means tightening `Line1` back to `NOT NULL` — a separate make-mandatory change, not an automatic
undo of this one: it is refused while the table holds rows, and is not lossless once any NULL `Line1`
has been written. Backing the change out was not exercised.

## Not checked / still open
- Application and consumer impact — any report, query, ETL job, or code path that assumed `Line1`
  is never NULL will meet one once a row is written blank; which consumers depend on it is not
  confirmed by the copy. The application owner owns closing this before promotion.
- Other environments — whether QA, UAT, or Prod hold consumers that break on a NULL `Line1` is not
  known from one copy.
- Reversibility — only the forward loosening is proven; re-tightening is a make-mandatory change with
  its own row-presence guard and is not exercised here.
