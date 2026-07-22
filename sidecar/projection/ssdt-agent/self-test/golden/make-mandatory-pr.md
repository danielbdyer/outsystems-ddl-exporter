# Customer: make Email required (two rows backfilled)

## Summary
dbo.Customer.Email is tightened from `NVARCHAR(256) NULL` to `NOT NULL`, so a customer row can no
longer be saved without an email address. The business reason, in the requester's words: "no
customer should be missing an email." Two existing rows hold NULL Email and are backfilled in the
same release. No work item was provided with the request — attach one before merge so the record
is traceable.

## Review & release
- A dev lead must review this: existing data is modified — two dbo.Customer rows are backfilled,
  and an existing column is tightened to NOT NULL while the table holds rows.
- Ships as a scripted change: the data-loss guard (BlockOnPossibleDataLoss) is relaxed for this
  one publish, after the zero-NULL count is proven — a publish-time decision that cannot be
  expressed as a table definition.
- Added scrutiny: none.

## Changes
| File | Change |
|---|---|
| Modules/Customer.sql | Tightens Email from `NVARCHAR(256) NULL` to `NOT NULL` in the table definition |
| Script.PreDeployment.sql | Enables the idempotent backfill: rows where Email IS NULL are stamped `unknown+<Id>@example.invalid` before the schema change lands |
| Data/Seed.sql | Customer rows 3 and 5 now seed the backfilled Email values; the seed previously declared NULL for both, which fails after the tightening (Msg 515) |

No renames (the refactorlog is unchanged). No index changes; no view or procedure definitions
change.

## Data remediation
Two of the five dbo.Customer rows violate the new rule: Customer 3 (Initech) and Customer 5
(Stark Industries) hold NULL Email (counted on the disposable copy, 2026-07-16).
- Decision: backfill each with a distinct placeholder, `unknown+<Id>@example.invalid`. This value
  is an assumed answer taken from the project's pre-deployment worked example; no business owner
  has confirmed it. The assumption is named under Not verified and must be settled before
  promotion.
- Rows affected: 2. Original values recorded for audit: Customer 3, Email NULL →
  `unknown+3@example.invalid`; Customer 5, Email NULL → `unknown+5@example.invalid`.
- The post-deployment seed declared NULL Email for the same two rows. Left as it was, the first
  deploy after the tightening fails in the seed — Msg 515, "Cannot insert the value NULL into
  column 'Email' … UPDATE fails." — observed on the disposable copy. The seed rows now carry the
  backfilled values, so the remediation is durable at source and a redeploy captures zero rows.

## Deployment evidence — disposable copy of Dev, 2026-07-16, sqlpackage 170.4.83.3
- The generated deploy script guards the tightening on row presence, not on blank values.
  Verbatim from the generated delta, placed above the ALTER:
  ```sql
  IF EXISTS (select top 1 1 from [dbo].[Customer])
      RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT
  ...
  ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR (256) NOT NULL;
  ```
  The script never inspects the Email column.
- The publish under the strict profile is blocked while dbo.Customer holds rows (5 rows, 2 NULL
  Emails):
  `Error SQL72014: … Msg 50000, Level 16, State 127 — Rows were detected. The schema update is
  terminating because data loss might occur.`
- The backfill alone does not clear the block. With the pre-deployment backfill active, the NULL
  count reached 0, the strict publish was blocked again with the same error, and the column stayed
  nullable (is_nullable = 1). Zero blanks is necessary, not sufficient: the guard fires on row
  presence.
- A relaxed publish attempted before the seed fix failed in the post-deployment seed — Msg 515,
  "Cannot insert the value NULL into column 'Email' … UPDATE fails." The schema change had already
  landed when the seed failed: the publish is not atomic across the schema transaction and the
  post-deployment script. The Data/Seed.sql change in this set removes that failure.
- From the original state, one publish with `/p:BlockOnPossibleDataLoss=False` lands the complete
  change set: the backfill stamps both rows, the ALTER lands, the seed captures nothing. End
  state: is_nullable = 0, zero NULL Emails, 5 rows.
- An INSERT with NULL Email now fails:
  `Msg 515, Level 16, State 2 — Cannot insert the value NULL into column 'Email', table
  '…dbo.Customer'; column does not allow nulls. INSERT fails.` The row count is unchanged (5).
- A second publish of the same build issued no object changes, and the Customer content digest is
  identical before and after (5 rows, digest 0xFFFFFFFF5E8E9706): the database is unchanged.

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
  backfill stamps all of them with placeholders. Run the NULL probe in each environment before
  promotion, and note the relaxed-gate publish applies there too — the guard fires on row presence
  in every populated environment.
- Production scale and timing. The ALTER COLUMN may block writes or run long at production row
  counts; the small copy cannot show that. Schedule a window.
- Reversibility. The forward publish is proven; backing the change out is not exercised here.
