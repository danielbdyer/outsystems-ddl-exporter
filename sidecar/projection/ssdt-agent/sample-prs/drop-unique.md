# Status.Code: allow duplicate codes (drop UIX_Status_Code)

## Verdict
This change removes the unique index `UIX_Status_Code` from `dbo.Status`, so two status rows
may carry the same Code. It ships as a single in-place schema change and never blocks. Confirm
duplicates are genuinely legitimate, and confirm nothing matches rows by Code alone — the
post-deployment seed and any upsert keyed on Code assume one row per value today. No work item
supplied — attach one before merge.

## Intent
The developer's stated intent: status codes are being reorganized and a transitional period
needs two rows sharing a code; the uniqueness rule blocks that. No work item supplied — attach
one before merge.

## What changes
- `dbo.Status`: the statement `CREATE UNIQUE INDEX [UIX_Status_Code] ON dbo.Status (Code);` is
  removed from the table's definition file. The table's columns and primary key are unchanged.

## Before promoting
- Confirm duplicates are legitimate with the product owner — this retires an identity
  guarantee, not just an index.
- Check every lookup, upsert, or MERGE keyed on `Status.Code`. The seed on the copy matches by
  `Id`, not Code, so it is unaffected; application code and other environments' scripts are not
  covered here. A MERGE keyed on a duplicated value fails with `Msg 8672` at its next run.

## The data
- `dbo.Status` holds 3 rows with distinct codes (`Pending`, `Shipped`, `Cancelled`), so nothing
  violates today and nothing is validated by the drop. The change is about what may be written
  after it lands.

## How it ships
- Ships as a single schema change, applied in place. No data is read or written. The generated
  script is one statement:
  `DROP INDEX [UIX_Status_Code] ON [dbo].[Status];`

## What proving showed
Published to a throwaway copy on this branch (sqlpackage 170.5.76).
- **Tried:** remove the unique index from the CREATE, build, Strict publish → published. The
  delta contains the single `DROP INDEX` and nothing else; the 3 rows are untouched.
- **Realized:** a drop has nothing to validate, so the publish cannot fail — and cannot warn.
  The guarantee's consumers (lookups, upserts, the index's own read performance) are outside
  the deployment engine's sight.

## After deploy — check
```sql
-- expect 0 rows: the unique index no longer exists
SELECT name FROM sys.indexes
WHERE object_id = OBJECT_ID('dbo.Status') AND name = 'UIX_Status_Code';
```

## How to roll this back
Re-creating the index reverses the change:
`CREATE UNIQUE INDEX UIX_Status_Code ON dbo.Status (Code);`
The build validates uniqueness over every existing row, so it lands clean only while no
duplicate Code was written in the gap; a duplicate blocks it with `Msg 1505` naming the
duplicated value, until the duplicates are reconciled. The drop itself loses no data.

## Not checked / still open
- Duplicates during the gap. Nothing refuses a duplicate Code after this lands; if uniqueness
  is ever wanted back, every duplicate written in between must be reconciled first.
- Read performance. The index served reads as well as uniqueness; the effect of losing it does
  not show on a 3-row copy.
- Other environments. QA, UAT, and Prod were not published here; run the check query in each
  before promotion.
