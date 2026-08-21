# Customer.Email: require a value (2 empty emails filled, then the column tightened across two releases)

## Verdict
This change makes `dbo.Customer.Email` reject NULL, so every customer must have an email; it fills
the 2 customers that have none today and tightens the column. This pipeline cannot relax the
data-loss guard, so it ships as two releases. Confirm the fill value with the data owner and land
Release 1 in each environment before Release 2. No work item supplied — attach one before merge.

## Intent
The developer's stated intent for this PBI: make an email address mandatory on every customer, so
a customer can no longer be saved without one — "this attribute must be filled". No work item
supplied — attach one before merge.

## What changes
- `dbo.Customer.Email`: `NVARCHAR(256) NULL` → `NVARCHAR(256) NOT NULL`.
- `Data/Seed.sql`: the 2 rows that carried a NULL email — Customer 3 (Initech) and Customer 5
  (Stark Industries) — now carry a real address, so the post-deploy seed stops writing NULL into
  the tightened column.

## Before promoting
- Run the NULL-email query (below) in each environment. The count differs per environment; the
  copy held 2. Every NULL must be filled before Release 1 goes up there.
- Confirm the fill value with the data owner. The copy used the placeholder
  `unknown+<Id>@example.invalid`; a real address, or a decision to collect the missing emails
  first, is a data-owner decision.
- Confirm Release 1 has landed in an environment — the column already rejects NULL there — before
  sending Release 2 up to it.
- Check with the application owner that no code path saves a customer without an email or writes
  NULL to it; after Release 1 both fail with `Msg 515`.

## How it ships
- Two releases, because this pipeline (Azure DevOps → Octopus) always publishes with the data-loss
  guard `BlockOnPossibleDataLoss` on and cannot turn it off for one deploy.
- **Release 1** — a pre-deploy script fills the NULL emails and runs
  `ALTER TABLE dbo.Customer ALTER COLUMN Email NVARCHAR(256) NOT NULL`. The model still declares
  `Email NULL`, so DacFx generates no tightening step and the guard never fires. The script is
  idempotent: it fills only rows still NULL and alters only while the column is still nullable, so
  it is safe to re-run and safe if a later step fails. The corrected seed ships here — a post-deploy
  seed that still writes a NULL email fails once the column is `NOT NULL` (`Msg 515`).
- Publish Release 1 once. Re-publishing Release 1 reverts the column to NULL: with the model still
  declaring `NULL` against a database that is already `NOT NULL`, DacFx generates
  `ALTER COLUMN Email NVARCHAR(256) NULL`. Send Release 2 up promptly.
- **Release 2** — the model declares `Email NOT NULL` and carries no pre-deploy. The database is
  already `NOT NULL`, so DacFx generates nothing. This closes the gap between the model and the
  database.

## The data
- 5 customers. 2 have no email: Customer 3 (Initech) and Customer 5 (Stark Industries). The other 3
  have one.
- The guard blocks on row presence, not on the NULLs: a table with 0 NULL emails is still refused
  while it holds any row.

## What proving showed
Published to a throwaway copy on this branch.
- **Tried:** edit the column to `NOT NULL`, publish under the guard → refused. `Msg 50000`: "Rows
  were detected. The schema update is terminating because data loss might occur." The generated
  script guards the tightening with `IF EXISTS (SELECT TOP 1 1 FROM [dbo].[Customer]) RAISERROR(…)`
  above the `ALTER COLUMN`; Warning SQL72016 named the column. The column stayed nullable.
- **Did:** fill the 2 NULL emails in a pre-deploy step and run the ALTER, model still declaring
  `NULL` → the post-deploy seed still wrote NULL into the now-`NOT NULL` column, so the deploy
  failed with `Msg 515`: "Cannot insert the value NULL into column 'Email' … UPDATE fails." The
  pre-deploy's tightening had already committed — the column was `NOT NULL` with 0 NULLs even though
  the deploy failed.
- **Did:** correct the seed so the 2 rows carry a real address; publish Release 1 once → published,
  the column is `NOT NULL`. Publish Release 2 (model `NOT NULL`, no pre-deploy) → published, nothing
  changed; re-publish → published, nothing changed.
- **Realized:** filling the NULLs is necessary but not enough — the guard fires on row presence, so
  the tightening cannot ride the same release as the model. The pre-deploy side effect survives a
  failed deploy, so it must be idempotent, and the seed feeding the column must stop writing NULL in
  the same change set.

## After deploy — check
```sql
-- no customer holds a NULL email, expect 0
SELECT COUNT(*) AS null_emails FROM dbo.Customer WHERE Email IS NULL;

-- the column rejects NULL, expect is_nullable = 0
SELECT is_nullable FROM sys.columns
WHERE object_id = OBJECT_ID('dbo.Customer') AND name = 'Email';
```

## How to roll this back
Re-widening is lossless: `ALTER TABLE dbo.Customer ALTER COLUMN Email NVARCHAR(256) NULL` restores
the nullable column with no data loss. The values written into the 2 previously-empty rows are not
set back to NULL by this; their pre-Release-1 originals (both NULL) live in the Release 1 pre-deploy
output for a manual restore. Backing the change out was not exercised.

## Not checked / still open
- The fill value is the data owner's call. The copy used the placeholder
  `unknown+<Id>@example.invalid` — a real address, or collecting the missing emails first, is not
  settled here.
- Application impact — any code path that saves a customer without an email, or writes NULL to it,
  fails after Release 1 with `Msg 515`. That every such path supplies a value is not confirmed
  (app owner).
- Other environments — Test, UAT, and Prod may hold more NULL emails than the 2 on the copy; the
  guard blocks in every populated environment. Run the NULL query and land Release 1 in each before
  Release 2.
- Production scale and timing — the `ALTER COLUMN` rewrite cost at production row counts is not
  shown by the small copy. Schedule a window.
- Reversibility — the forward change and its lossless re-widening are the limit of what the copy
  proved; restoring the original NULLs is not exercised.
