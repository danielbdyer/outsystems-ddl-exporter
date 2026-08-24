# Customer: rename ContactPhone to MobileNumber (a refactorlog entry keeps the phone numbers; without one the column is dropped and every value is lost)

## Verdict
This PR renames `dbo.Customer.ContactPhone` to `MobileNumber` and carries a refactorlog entry that
makes the deploy `sp_rename` the column, keeping all five phone numbers. Confirm the generated
difference reads as `sp_rename`, not `DROP COLUMN` + `ADD`, before each promotion, and that every
caller of the old name moves to the new one.

## Intent
The developer's stated intent for this PBI: rename the `ContactPhone` attribute to `MobileNumber` —
"I renamed the field in Service Studio" — keeping the numbers already stored. No work item supplied —
attach one before merge.

## What changes
- `dbo.Customer.ContactPhone` → `dbo.Customer.MobileNumber` (rename; type `NVARCHAR(40) NULL`
  unchanged).
- `SampleCatalog.refactorlog`: one `Rename Refactor` entry mapping `[dbo].[Customer].[ContactPhone]`
  to new name `[MobileNumber]`. This entry is what makes the deploy a rename instead of a drop.

## Before promoting
- A dev lead or an experienced developer should approve this: the running application must change,
  because every caller of the old column name — views, procedures, ORM mappings, reports, ETL — must
  move to `MobileNumber` to keep working.
- Before each promotion, script the difference and confirm it reads
  `EXEC sp_rename ... 'COLUMN'`, not `DROP COLUMN [ContactPhone]` + `ADD [MobileNumber]`. A difference
  that drops and re-adds means the refactorlog entry did not travel with the change, and the deploy
  would lose every phone number.

## The data
- `dbo.Customer` holds 5 rows; every one carries a `ContactPhone` value (for example
  `+1-206-555-0101`). None is NULL. These five numbers are exactly what the rename must preserve and
  a drop-and-re-add would lose.

## How it ships
- One release, applied in place — with the refactorlog entry present. The generated difference is a
  single `EXEC sp_rename '[dbo].[Customer].[ContactPhone]', 'MobileNumber', 'COLUMN'`, a metadata
  operation that renames the column while preserving its data and its `object_id`. It contains no
  data-loss step, so the data-loss guard (`BlockOnPossibleDataLoss = true`) does not fire.
- Without the refactorlog entry the same edit generates `DROP COLUMN [ContactPhone]` +
  `ADD [MobileNumber]` instead — a data-loss step. On this pipeline that difference is refused on a
  populated table, and if the gate were ever relaxed the drop would delete every value. The
  refactorlog entry, not the edited column name, is what carries the data.

## What proving showed
Published to a throwaway copy of the database on this branch (SQL Server 2022, `sqlpackage 170.4.83.3`).
The 5 phone numbers give an aggregate value digest of `1312825711` before the change.
- **Tried:** rename by editing the column name only, with no refactorlog entry; script the
  difference → two statements, `ALTER TABLE [dbo].[Customer] DROP COLUMN [ContactPhone];` and
  `ALTER TABLE [dbo].[Customer] ADD [MobileNumber] NVARCHAR (40) NULL;`. Publish with
  `BlockOnPossibleDataLoss = true` → refused:
  `Warning SQL72015: The column [dbo].[Customer].[ContactPhone] is being dropped, data loss could
  occur.` then
  `Error SQL72014: ... Msg 50000, Level 16, State 127 ... Rows were detected. The schema update is
  terminating because data loss might occur.` and `Could not deploy package.`
- **Did:** to see what the refusal was protecting, relax the gate for that one publish
  (a diagnostic on the copy, never a shipping path) → the drop committed:
  `ContactPhone` no longer exists and `MobileNumber` holds NULL in all 5 rows. Every phone number was
  lost. (The post-deploy seed then failed because it still named the dropped column, but the drop had
  already committed — the loss did not wait for a clean deploy.)
- **Did:** on a fresh copy of the same data, add the `Rename Refactor` entry mapping
  `[dbo].[Customer].[ContactPhone]` to `[MobileNumber]`; script the difference → one statement,
  `EXECUTE sp_rename @objname = N'[dbo].[Customer].[ContactPhone]', @newname = N'MobileNumber',
  @objtype = N'COLUMN';`. Publish with `BlockOnPossibleDataLoss = true` →
  `Successfully published database.`
- **Realized:** the refactorlog entry, not the column name in the `CREATE`, decides whether the data
  survives. With it, the difference is `sp_rename` and every value is kept; without it, the difference
  drops the column and the values are gone. After the rename: `ContactPhone` is gone, `MobileNumber`
  exists, all 5 rows are populated, and the aggregate value digest is `1312825711` — identical to the
  original, so the numbers survived intact.
- Re-publish with no change → `Successfully published database.`; the schema difference is empty.

## After deploy — check
```sql
-- the column exists under the new name only, expect 1 row, name = MobileNumber
SELECT c.name
FROM sys.columns c
WHERE c.object_id = OBJECT_ID('dbo.Customer') AND c.name IN ('ContactPhone', 'MobileNumber');

-- the values survived, expect 5 rows carrying a non-NULL number
SELECT COUNT(*) AS PopulatedRows
FROM dbo.Customer
WHERE MobileNumber IS NOT NULL;
```
Before each promotion, the scripted difference must read `sp_rename ... 'COLUMN'` — a difference that
reads `DROP COLUMN` + `ADD` would drop the column and lose its values.

## How to roll this back
Reversible without data loss: rename `MobileNumber` back to `ContactPhone` with its own refactorlog
entry — `sp_rename` preserves the data in both directions. The callers updated for the new name must
be reverted with it. Never delete the refactorlog entry to "undo" the rename: a fresh-environment
deploy replays the whole refactorlog, and a missing entry re-becomes `DROP COLUMN` + `ADD` on that
environment. Backing the change out was not exercised.

## Not checked / still open
- Application impact — consumers of the old column name outside the project (reports, ETL,
  integrations not in the dacpac) break silently until they move to `MobileNumber`. The application
  owner and the consumer owners confirm the callers are updated.
- Other environments — Test, UAT, and Prod may hold external consumers still reading `ContactPhone`
  where the copy does not. Read the difference and run the verification queries before each promotion.
- A backward-compatibility bridge — if a consumer cannot move in step, a computed column carrying the
  old name can keep it resolving while that consumer catches up. That was not built or exercised here.
- Reversibility — only the forward rename is exercised on the copy; the backout rename is the same
  metadata operation but is not separately proven.
