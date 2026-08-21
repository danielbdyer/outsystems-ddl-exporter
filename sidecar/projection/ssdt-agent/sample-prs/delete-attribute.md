# Product.LegacyCode: drop the column (its values are removed for good, across two releases)

## Verdict
This change drops `dbo.Product.LegacyCode` and the value it holds on all 5 products; the values
cannot be recovered from the schema. Confirm nothing still reads the column and that its values are
not needed, in each environment, before promoting. This pipeline cannot relax the data-loss guard,
so the drop ships as two releases. No work item supplied — attach one before merge.

## Intent
The developer's stated intent for this PBI: remove the `LegacyCode` attribute that is no longer
used — "drop this field, it is not needed anymore". No work item supplied — attach one before merge.

## What changes
- `dbo.Product`: drop the `LegacyCode NVARCHAR(40) NOT NULL` column and its default constraint
  `DF_Product_LegacyCode`.
- `Data/Seed.sql`: the Product seed stops writing `LegacyCode` (its column-list, match, and insert
  references are removed), so the post-deploy seed does not reference a column that no longer exists.

## Before promoting
- Run the referencing query (below) in each environment and confirm nothing — no view, procedure,
  computed column, or index — still references `LegacyCode`. This query sees SQL objects, not
  application code.
- Check with the application owner that no application code still reads or writes `LegacyCode`. The
  column's values are gone once Release 1 lands and cannot be recovered from the schema; a principal
  signs this off because the loss cannot be undone.
- Confirm Release 1 has landed in an environment — the column is already gone there — before sending
  Release 2 up to it.

## How it ships
- Two releases, because this pipeline (Azure DevOps → Octopus) always publishes with the data-loss
  guard `BlockOnPossibleDataLoss` on and cannot turn it off for one deploy. A declarative drop of a
  populated column is refused by the same row-presence guard that blocks a narrowing.
- **Release 1** — a pre-deploy script drops the default constraint, then the column
  (`ALTER TABLE dbo.Product DROP CONSTRAINT DF_Product_LegacyCode;` then
  `ALTER TABLE dbo.Product DROP COLUMN LegacyCode;`). The model still declares `LegacyCode`, so
  DacFx generates no drop step and the guard never fires. The script is idempotent: it drops each
  object only if it still exists, so it is safe to re-run and safe if a later step fails. The
  corrected seed ships here — a post-deploy seed that still writes `LegacyCode` fails once the column
  is gone (`Msg 207`).
- Publish Release 1 once. Re-publishing Release 1 **re-adds** the column: with the model still
  declaring `LegacyCode` against a database that no longer has it, DacFx generates
  `ADD [LegacyCode] … DEFAULT (N'LEGACY') NOT NULL` and every row is stamped with the placeholder
  `LEGACY` — the original values do not come back. Send Release 2 up promptly; if Release 1 is
  re-published before Release 2, Release 2 then blocks on the re-added populated column.
- **Release 2** — the model drops `LegacyCode` and carries no pre-deploy. The database no longer has
  the column, so DacFx generates nothing. This closes the gap between the model and the database.

## The data
- 5 products. `LegacyCode` is populated on all 5.
- The guard blocks on row presence, not on the column's content: the drop is refused while the
  table holds any row.

## What proving showed
Published to a throwaway copy on this branch.
- **Tried:** remove `LegacyCode` from the table and the seed, publish under the guard → refused.
  Warning SQL72015: "The column [dbo].[Product].[LegacyCode] is being dropped, data loss could
  occur." `Msg 50000`: "Rows were detected. The schema update is terminating because data loss might
  occur." The column survived.
- **Did:** drop the column in a pre-deploy step with the model still declaring it, but leave the
  seed writing `LegacyCode` → the deploy failed with `Msg 207`: "Invalid column name 'LegacyCode'."
  The pre-deploy's drop had already committed — the column was gone even though the deploy failed.
- **Did:** remove `LegacyCode` from the seed; publish Release 1 once → published, the column is
  gone. Publish Release 2 (model drops the column, no pre-deploy) → published, nothing changed;
  re-publish → published, nothing changed. All 5 product rows remain — this removes a column, not
  rows.
- **Realized:** re-publishing Release 1 re-adds the column with its default (`LEGACY` on every row),
  so Release 1 is a single publish and Release 2 must follow at once. The seed that writes the
  column must stop in the same change set, and the dropped values cannot be recovered afterward.

## After deploy — check
```sql
-- nothing references the column before the final drop, expect 0 rows
SELECT referencing_schema_name, referencing_entity_name
FROM sys.dm_sql_referencing_entities('dbo.Product', 'OBJECT')
WHERE referenced_minor_id = (
  SELECT column_id FROM sys.columns
  WHERE object_id = OBJECT_ID('dbo.Product') AND name = 'LegacyCode');

-- the column no longer exists, expect 0 rows
SELECT c.name FROM sys.columns c
WHERE c.object_id = OBJECT_ID('dbo.Product') AND c.name = 'LegacyCode';
```

## How to roll this back
Re-adding the column restores the structure but not the data:
`ALTER TABLE dbo.Product ADD LegacyCode NVARCHAR(40) NULL;` gives back an empty column. The
per-row `LegacyCode` values are gone once Release 1 lands and are recoverable only from a backup
taken before Release 1. Backing the change out was not exercised.

## Not checked / still open
- Application impact — whether application code outside the database still reads or writes
  `LegacyCode`. `sys.dm_sql_referencing_entities` sees SQL objects, not application code; the app
  owner confirms the app has stopped before Release 1.
- The values themselves — no backup of the `LegacyCode` values is taken by this change; if any are
  needed later, capture them before Release 1.
- Other environments — Test, UAT, and Prod may still have live readers where the copy does not. Run
  the referencing query and land Release 1 in each before Release 2.
- Production scale and timing — the drop cost at production row counts is not shown by the small
  copy. Schedule a window.
- Reversibility — only the forward drop is exercised; the dropped values are not recoverable from
  the schema, and a pre-Release-1 backup is the sole restore path.
