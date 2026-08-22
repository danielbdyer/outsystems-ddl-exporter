# Product.Code: shorten to 10 characters (1 over-length value cut, then the column narrowed across two releases)

## Verdict
This change shortens `dbo.Product.Code` from 50 to 10 characters and cuts the 1 value that is
longer than 10 down to fit. This pipeline cannot relax the data-loss guard, so it ships as two
releases. Confirm the cut value is acceptable in each environment before promoting, and land
Release 1 before Release 2. No work item supplied — attach one before merge.

## Intent
The developer's stated intent for this PBI: shorten the product code to a 10-character maximum —
"reduce the length of Code". No work item supplied — attach one before merge.

## What changes
- `dbo.Product.Code`: `NVARCHAR(50) NOT NULL` → `NVARCHAR(10) NOT NULL`.
- `Data/Seed.sql`: Product 3's code, `STANDARD-SKU-001` (16 characters), is shortened to
  `STANDARD-S` (10 characters), so the post-deploy seed stops writing a value that no longer fits.

## Before promoting
- Run the over-length query (below) in each environment. The set differs per environment; the copy
  held 1: Product 3, `STANDARD-SKU-001`, 16 characters. Every over-length row must be reconciled
  before Release 1 goes up there.
- Confirm the cut value with the data owner — `STANDARD-SKU-001` shortened to `STANDARD-S` loses the
  last 6 characters. If the full code is real data that must be kept, stop and widen a different way
  instead of cutting it.
- Confirm Release 1 has landed in an environment — the column is already 10 there — before sending
  Release 2 up to it.
- Check with the application owner that no code path writes a code longer than 10; after the
  narrowing those writes are rejected.

## The data
- 5 products. 1 code is longer than 10 characters: Product 3, `STANDARD-SKU-001`, 16 characters.
  The longest code is 16.
- The guard blocks on row presence, not on the lengths: a table where every code already fits 10 is
  still refused while it holds any row.

## How it ships
- Two releases, because this pipeline (Azure DevOps → Octopus) always publishes with the data-loss
  guard `BlockOnPossibleDataLoss` on and cannot turn it off for one deploy.
- **Release 1** — a pre-deploy script shortens the over-length codes (`LEFT(Code, 10)`) and runs
  `ALTER TABLE dbo.Product ALTER COLUMN Code NVARCHAR(10) NOT NULL`. The model still declares
  `NVARCHAR(50)`, so DacFx generates no narrowing step and the guard never fires. The script is
  idempotent: it shortens only rows still longer than 10 and narrows only while the column is still
  wider, so it is safe to re-run and safe if a later step fails. The reconciled seed ships here — a
  post-deploy seed that still writes the 16-character code fails once the column is 10 (`Msg 2628`).
- Publish Release 1 once. Re-publishing Release 1 widens the column back to 50: with the model still
  declaring `NVARCHAR(50)` against a database that is already `NVARCHAR(10)`, DacFx generates
  `ALTER COLUMN Code NVARCHAR(50)`. Send Release 2 up promptly.
- **Release 2** — the model declares `NVARCHAR(10)` and carries no pre-deploy. The database is
  already 10, so DacFx generates nothing. This closes the gap between the model and the database.

## What proving showed
Published to a throwaway copy on this branch.
- **Tried:** narrow the column to `NVARCHAR(10)`, publish under the guard → refused. `Msg 50000`:
  "Rows were detected. The schema update is terminating because data loss might occur." The
  generated script guards the narrowing with `IF EXISTS (SELECT TOP 1 1 FROM [dbo].[Product])
  RAISERROR(…)` above the `ALTER COLUMN`; Warning SQL72015 named the 50→10 change and the possible
  data loss. The column stayed at 50 characters (`max_length = 100` bytes).
- **Did:** shorten the over-length code in a pre-deploy step and run the ALTER, model still
  declaring `NVARCHAR(50)` → the post-deploy seed still wrote the 16-character
  `STANDARD-SKU-001`, so the deploy failed with `Msg 2628`: "String or binary data would be
  truncated in table … column 'Code'. Truncated value: 'STANDARD-S'." The pre-deploy's reconcile
  and narrowing had already committed — the column was 10 and the row read `STANDARD-S`.
- **Did:** reconcile the seed to `STANDARD-S`; publish Release 1 once → published, the column is 10
  (`max_length = 20` bytes). Publish Release 2 (model `NVARCHAR(10)`, no pre-deploy) → published,
  nothing changed; re-publish → published, nothing changed.
- **Realized:** shortening the values is necessary but not enough — the guard fires on row presence,
  so the narrowing cannot ride the same release as the model. The pre-deploy side effect survives a
  failed deploy, so it must be idempotent, and the seed feeding the column must stop writing
  over-length values in the same change set.

## After deploy — check
```sql
-- no code is longer than 10, expect 0 rows
SELECT Id, LEN(Code) AS len FROM dbo.Product WHERE LEN(Code) > 10;

-- the column is 10 characters, expect max_length = 20 (bytes; NVARCHAR is 2 bytes per character)
SELECT max_length FROM sys.columns
WHERE object_id = OBJECT_ID('dbo.Product') AND name = 'Code';
```

## How to roll this back
Re-widening is lossless: `ALTER TABLE dbo.Product ALTER COLUMN Code NVARCHAR(50) NOT NULL` restores
the 50-character column with no data loss. The characters cut from Product 3 are not restored by
this: the Release 1 reconcile was destructive, so the original `STANDARD-SKU-001` is recoverable only
from a backup taken before Release 1, or from a durable record the reconcile script was written to keep
— not from the deploy log. Backing the change out was not exercised.

## Not checked / still open
- The cut value is the data owner's call. Shortening `STANDARD-SKU-001` to `STANDARD-S` drops the
  last 6 characters; whether that is acceptable, or whether the full code must be kept, is not
  settled here.
- Application impact — any code path that writes a code longer than 10 is rejected after the
  narrowing. That every such path respects the new limit is not confirmed (app owner).
- Other environments — Test, UAT, and Prod may hold more over-length codes than the 1 on the copy;
  the guard blocks in every populated environment. Run the over-length query and land Release 1 in
  each before Release 2.
- Production scale and timing — the `ALTER COLUMN` rewrite cost at production row counts is not
  shown by the small copy. Schedule a window.
- Reversibility — the forward change and its lossless re-widening are the limit of what the copy
  proved; a cut value cannot be recovered from the schema.
