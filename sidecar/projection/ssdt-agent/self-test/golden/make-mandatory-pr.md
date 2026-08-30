# Customer — make Email required (two releases)

## Verdict
This change makes `dbo.Customer.Email` reject NULL, so a customer can no longer be saved without
an email; it fills the 2 existing customers that have none. On a populated table this pipeline
cannot relax the data-loss guard, so it ships as two releases. Confirm the fill value with the
data owner, and land Release 1 in each environment before Release 2.

## Intent
Make the Email attribute on Customer required — in the requester's words, "no customer should be
missing an email." No work item was supplied with the request; attach one before merge so the
record is traceable.

## What changes
- `dbo.Customer.Email`: `NVARCHAR(256) NULL` → `NVARCHAR(256) NOT NULL`.
- Release 1 is a pre-deployment script that backfills the rows holding NULL and runs the
  `ALTER … NOT NULL` while the table definition still declares `NULL`; the seed rows for the same
  two customers carry the backfilled values in the same release.
- Release 2 declares `Email NOT NULL` in the table definition. The database is already tightened,
  so nothing is generated.

No rename (the refactorlog is unchanged). No index, view, or procedure changes.

## Before promoting
- A dev lead must review this: existing data is modified — two `dbo.Customer` rows are backfilled,
  and an existing column is tightened while the table holds rows.
- Confirm the backfill value with the customer-data owner before Release 1. `unknown+<Id>@example.invalid`
  is an assumed placeholder, not a confirmed business answer (see Not checked).
- Run the NULL-email query (below) in each environment. The count differs per environment; the
  disposable copy held 2. Every NULL must be filled before Release 1 tightens the column there.
- Land Release 1 in an environment — the column already rejects NULL there — before sending
  Release 2 up to it. Do not re-publish Release 1 on its own, and hold other Dev publishes during
  the window: while the table definition lags, an intervening publish carries the old shape and
  reverts the tightening.
- Added scrutiny: first time on this estate — the operations ledger holds no prior make-mandatory
  (during the cutover, that is every operation).

## The data
Two of the five `dbo.Customer` rows violate the new rule: Customer 3 (Initech) and Customer 5
(Stark Industries) hold NULL Email. Counted on a disposable copy of Dev, 2026-08-24: 5 rows,
2 NULL.
- Backfill: each row gets a distinct placeholder — Customer 3 → `unknown+3@example.invalid`,
  Customer 5 → `unknown+5@example.invalid`. Original values recorded for audit: both were NULL.

## How it ships
Ships across two releases so the running application keeps working while the change is in flight.
Release 1 is a pre-deployment script — backfill, then `ALTER COLUMN Email NVARCHAR(256) NOT NULL` —
with the table definition still declaring `NULL`, plus the corrected seed. Release 2 is the table
definition declaring `NOT NULL`. This pipeline (Azure DevOps → Octopus) always publishes with the
data-loss guard on and cannot relax it for one deploy, so the tightening is done behind a lagging
definition rather than as a single declarative change.

## What proving showed
Published to a disposable copy of Dev on this branch, 2026-08-24, sqlpackage 170.4.83.3.

- **Tried** the naive single release — the definition tightened to `NOT NULL`, no pre-deploy —
  against the populated copy (`pg_mm_naive`, 5 rows, 2 NULL). **Did:** the deployment refused it —
  `Msg 50000, Level 16, State 127 — Rows were detected. The schema update is terminating because
  data loss might occur.` The generated guard sits above the ALTER and reads row presence, not
  values:
  ```sql
  IF EXISTS (select top 1 1 from [dbo].[Customer])
      RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
  ...
  ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR (256) NOT NULL;
  ```
  **Realized:** a same-release backfill cannot clear the block — the guard is computed once, up
  front, from the pre-publish state — and the column was left nullable.
- **Tried** Release 1 — the definition still `NULL`, the pre-deploy backfill and ALTER, the
  corrected seed — on `pg_mm`. **Did:** the publish succeeded; end state `is_nullable = 0`, 5 rows,
  0 NULL, Customer 3/5 = `unknown+3/5@example.invalid`, content digest
  `CHECKSUM_AGG(BINARY_CHECKSUM(Id, Name, Email)) = 1818783869`. **Realized:** the tightening lands
  because the deploy was planned from a state where the definition and the database agreed (`NULL`),
  so the main script never touches Email — the pre-deploy does.
- **Tried** Release 1 without the seed fix. **Did:** the post-deployment seed failed —
  `Msg 515, Level 16, State 2 — Cannot insert the value NULL into column 'Email' … UPDATE fails.`
  **Realized:** the seed must carry the backfilled values, or the first deploy after the tightening
  collides with it. The corrected seed rows ship in Release 1.
- **Tried** Release 2 — the definition `NOT NULL`, no pre-deploy — on the already-tightened `pg_mm`.
  **Did:** the generated delta contained no ALTER on Email; the publish issued no object change;
  the content digest was unchanged (`1818783869`). **Realized:** with the definition and the
  database both `NOT NULL`, Release 2 is a clean no-op.
- An INSERT with NULL Email now fails: `Msg 515 — Cannot insert the value NULL into column 'Email'`.
  The row count is unchanged (5).

## After deploy — check
```sql
-- expect 0: no row holds a NULL in the tightened column
SELECT COUNT(*) AS null_rows FROM dbo.Customer WHERE Email IS NULL;

-- expect is_nullable = 0: the column landed NOT NULL
SELECT is_nullable FROM sys.columns
WHERE object_id = OBJECT_ID('dbo.Customer') AND name = 'Email';
```

## How to roll this back
Re-widening the column is lossless:
`ALTER TABLE dbo.Customer ALTER COLUMN Email NVARCHAR(256) NULL;`
The backfill is not auto-reversed: a full backout also restores Customer 3 and Customer 5 to NULL
(originals recorded above) and reverts the seed rows, or the next deploy re-stamps the
placeholders. Backing the change out was not exercised on the disposable copy.

## Not checked / still open
- The backfill value. `unknown+<Id>@example.invalid` is an assumed placeholder, not a confirmed
  business answer. A data owner must accept it or supply real addresses; the placeholder is visible
  anywhere Email is displayed or sent.
- Application impact. Any code path that saves a Customer without an Email, or writes NULL to it,
  now fails with error 515. Application-side validation is not confirmed here; the application owner
  owns closing this before promotion.
- Other environments. QA, UAT, and Prod may hold more NULL Emails than the 2 on the disposable
  copy; the Release-1 backfill stamps whatever it finds. Run the NULL query and land Release 1 in
  each before Release 2.
- Production scale and timing. The ALTER COLUMN may block writes or run long at production row
  counts; the small copy cannot show that. Schedule a window.
- Reversibility. The forward two-release is proven; backing the change out is not exercised here.
