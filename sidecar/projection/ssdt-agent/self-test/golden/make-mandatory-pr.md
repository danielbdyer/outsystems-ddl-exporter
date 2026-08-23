# Customer: make Email required (two releases; two rows backfilled)

## Summary
dbo.Customer.Email is tightened from `NVARCHAR(256) NULL` to `NOT NULL`, so a customer row can no
longer be saved without an email address. The business reason, in the requester's words: "no
customer should be missing an email." Two existing rows hold NULL Email and are backfilled in
Release 1. No work item was provided with the request — attach one before merge so the record is
traceable.

## Review & release
- A dev lead must review this: existing data is modified — two dbo.Customer rows are backfilled,
  and an existing column is tightened to NOT NULL while the table holds rows.
- Ships as **two releases**, because this pipeline (Azure DevOps → Octopus) cannot relax the
  data-loss guard. **Release 1** — a pre-deployment script backfills the NULLs and runs the
  `ALTER … NOT NULL` while the model still declares `NULL`, so DacFx generates no data-loss step and
  the row-presence guard never fires. **Release 2** — the model declares `NOT NULL`; the database is
  already tightened, so DacFx generates nothing. The gate is never relaxed.
- Added scrutiny: none.

## Changes
| File | Change |
|---|---|
| Modules/Customer.sql | **Release 2** declares `Email NOT NULL`; in Release 1 it still declares `NULL` (so DacFx emits no guarded ALTER) |
| Script.PreDeployment.sql | **Release 1 only:** backfills rows where Email IS NULL, then runs `ALTER TABLE dbo.Customer ALTER COLUMN Email NVARCHAR(256) NOT NULL`. Idempotent — guarded on `Email IS NULL` and on `is_nullable = 1` |
| Data/Seed.sql | Rows 3 and 5 now seed the backfilled Email values; the seed previously declared NULL for both, which fails after the tightening (Msg 515). Ships in Release 1 |

No renames (the refactorlog is unchanged). No index changes; no view or procedure definitions
change.

## Data remediation
Two of the five dbo.Customer rows violate the new rule: Customer 3 (Initech) and Customer 5
(Stark Industries) hold NULL Email (counted on the disposable copy, 2026-08-22).
- Decision: backfill each with a distinct placeholder, `unknown+<Id>@example.invalid`. This value
  is an assumed answer taken from the project's pre-deployment worked example; no business owner
  has confirmed it. The assumption is named under Not verified and must be settled before promotion.
- Rows affected: 2. Original values recorded for audit: Customer 3, Email NULL →
  `unknown+3@example.invalid`; Customer 5, Email NULL → `unknown+5@example.invalid`.
- The post-deployment seed declared NULL Email for the same two rows. Left as it was, the first
  deploy after the tightening fails in the seed — Msg 515 — so the seed rows now carry the
  backfilled values and the remediation is durable at source; a redeploy captures zero rows.

## Deployment evidence — disposable copy of Dev, 2026-08-22, sqlpackage 170.4.83.3
- The generated deploy script guards the tightening on row presence, not on blank values. Verbatim
  from the generated delta, placed above the ALTER:
  ```sql
  IF EXISTS (select top 1 1 from [dbo].[Customer])
      RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
  ...
  ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR (256) NOT NULL;
  ```
  The script never inspects the Email column.
- **The naive single release is blocked.** Model tightened to `NOT NULL`, no pre-deploy, published
  under the strict profile against the populated copy (5 rows, 2 NULL Emails, DB `pg_mm_naive`):
  `Error SQL72014: … Msg 50000, Level 16, State 127 — Rows were detected. The schema update is
  terminating because data loss might occur.` A same-release backfill cannot clear it: DacFx computes
  the guard once, up front, from the pre-publish state — which is why Release 1 keeps the model lagging.
- **Release 1 lands (DB `pg_mm`).** With the model still `NULL` and the pre-deploy running the
  backfill then the `ALTER`, the publish succeeded. End state: `is_nullable = 0`, 5 rows, 0 NULL
  Emails, Customer 3/5 = `unknown+3/5@example.invalid`, content digest
  `CHECKSUM_AGG(BINARY_CHECKSUM(Id, Name, Email)) = 1818783869`.
- **The seed must ride with the change.** Publishing the model change without the Seed.sql fix failed
  in the post-deployment seed — `Msg 515, Level 16, State 2 — Cannot insert the value NULL into column
  'Email' … UPDATE fails.` — the schema had already tightened. The Data/Seed.sql change in this set
  removes that failure.
- **Release 2 is a clean no-op (DB `pg_mm`).** With the database already `NOT NULL`, publishing the
  model at `NOT NULL` (no pre-deploy) issued no object change; the content digest was unchanged
  (`1818783869`), and a second publish of the same build changed nothing either.
- An INSERT with NULL Email now fails: `Msg 515 — Cannot insert the value NULL into column 'Email'
  … INSERT fails.` The row count is unchanged (5).

## Verification — run in each environment after deployment
```sql
-- expect 0: no row holds a NULL in the tightened column
SELECT COUNT(*) AS null_rows FROM dbo.Customer WHERE Email IS NULL;

-- expect is_nullable = 0: the column landed NOT NULL
SELECT is_nullable FROM sys.columns
WHERE object_id = OBJECT_ID('dbo.Customer') AND name = 'Email';
```

## Rollback
Re-widening the column is lossless:
`ALTER TABLE dbo.Customer ALTER COLUMN Email NVARCHAR(256) NULL;`
The backfill is not auto-reversed: a full backout also restores Customer 3 and Customer 5 to NULL
Email (originals recorded above) and reverts the seed rows, or the next deploy re-stamps the
placeholders. Backing the change out was not exercised on the disposable copy.

## Not verified
- Application impact. Any code path that saves a Customer without an Email, or writes NULL to it,
  now fails with error 515. Application-side validation is not confirmed here — the application
  owner owns closing this before promotion.
- The backfill value. `unknown+<Id>@example.invalid` is an assumed placeholder, not a confirmed
  business answer. A data owner must accept the placeholder or supply real addresses; the
  placeholder is visible anywhere Email is displayed or mailed.
- Other environments. Test, UAT, and Prod may hold NULL Emails this copy cannot see; on deploy the
  Release-1 backfill stamps all of them with placeholders. Run the NULL probe in each environment
  before promotion, and land Release 1 (the column is already NOT NULL) before sending Release 2 up.
- Production scale and timing. The ALTER COLUMN may block writes or run long at production row
  counts; the small copy cannot show that. Schedule a window.
- Reversibility. The forward two-release is proven; backing the change out is not exercised here.
