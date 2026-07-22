# Order: add CreatedOn / CreatedBy / UpdatedOn audit stamps (required, and their defaults backfill existing rows)

**In OutSystems** — You add three audit Attributes to the `Order` Entity — `CreatedOn`, `CreatedBy`, `UpdatedOn` — set each *Is Mandatory = Yes*, and give them default values (current date/time, current user).
**In SSDT** — three new lines are added to `Tables/dbo.Order.sql`: `[CreatedOn] DATETIME2 NOT NULL CONSTRAINT [DF_Order_CreatedOn] DEFAULT (SYSUTCDATETIME())`, `[CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT [DF_Order_CreatedBy] DEFAULT (SUSER_SNAME())`, and `[UpdatedOn] DATETIME2 NOT NULL CONSTRAINT [DF_Order_UpdatedOn] DEFAULT (SYSUTCDATETIME())`. You add required columns *with defaults*; SQL Server stamps every existing row from the defaults as the columns land.

## Summary

You add three required audit columns to `Order`, each defaulting to a value SQL Server computes
(`SYSUTCDATETIME()` for the timestamps, `SUSER_SNAME()` for the user). This is the **safe counterpart to
add-mandatory**: a required (`NOT NULL`) column with **no** default is refused on a populated table,
because SQL Server has no value for the rows already there — but the moment the column carries a
`DEFAULT`, SQL Server fills every existing row from it as the column is added, and the same populated
table **applies clean in place**. This was proven objectively against a Twin — a disposable SQL Server
database published from this estate and filled with real-shaped synthetic data — with a
**production-faithful** publish (`BlockOnPossibleDataLoss = true`, the deployment a real environment
runs). No work item was provided with the request; attach one before merge so the record is traceable.

**A distinction worth holding onto — a default describes the future, not the past.** For an
*already-existing* column, a default only governs new inserts and never rewrites existing rows. These are
*new* columns, so no existing row has a value yet, and the default is exactly what backfills them. That
is why required audit columns *with* a default apply clean, while the same columns with **no** default
would block a populated table (the add-mandatory / Optimistic-NOT-NULL trap). But there is a catch to
name: the backfill stamps every historical row with the value the function returns **at deploy time** —
so all pre-existing orders get the *deploying* login as `CreatedBy` (here `sa`) and the *deploy* moment
as `CreatedOn`, not the identity or time of whoever really created them. The columns are honestly filled;
their historical *meaning* for old rows is a data-owner decision (named under Not verified).

## Review & release

- Because these columns carry defaults, the change is additive and existing values are untouched, so any
  team member can review it — but a reviewer should confirm the backfilled meaning is acceptable (old
  rows get the deploy-time stamp, see Summary).
- It ships as a single schema change, applied in place: SSDT emits `ALTER TABLE [dbo].[Order] ADD` for
  the three columns, and existing rows are stamped from the defaults in the same step. No gate
  relaxation, no pre-deployment backfill script, no staging — the defaults *are* the backfill.
- Name every constraint explicitly (`DF_Order_CreatedOn`, `DF_Order_CreatedBy`, `DF_Order_UpdatedOn`).
  Letting SSDT auto-name them (`DF__Order__Created__<hash>`) yields names that differ per environment and
  make later diffing and dropping fragile.
- Added scrutiny: at production row counts the stamp of every existing row runs inside the `ALTER` and
  may block writes or run long — schedule a window.

## Changes

| File | Change |
|---|---|
| `Tables/dbo.Order.sql` | Adds `[CreatedOn] DATETIME2 NOT NULL CONSTRAINT [DF_Order_CreatedOn] DEFAULT (SYSUTCDATETIME())`, `[CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT [DF_Order_CreatedBy] DEFAULT (SUSER_SNAME())`, and `[UpdatedOn] DATETIME2 NOT NULL CONSTRAINT [DF_Order_UpdatedOn] DEFAULT (SYSUTCDATETIME())` |

No renames (the refactorlog is unchanged). No index, view, or procedure changes. Only the three new
audit columns and their named defaults are added; every existing column is untouched.

## Data remediation

None required — the defaults *are* the remediation. Because each column ships with a `DEFAULT`, SQL
Server backfills every existing row as the column is added, so no row is ever value-less and the
`NOT NULL` lands clean. This is precisely the fix the *add-mandatory* / Optimistic-NOT-NULL family points
to: a required column blocks a populated table until it carries a default (or a pre-deployment backfill
supplies the value). What value the historical rows *should* carry — is the deploy-time stamp meaningful
for orders created long ago? — is a data-owner decision, named under Not verified.

## Deployment evidence — objective proof, production-faithful publish, 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, materializes
real-shaped data, applies the additive edit, and asserts the outcome under a **production-faithful**
DacFx posture (`BlockOnPossibleDataLoss = true`, no smart-defaults). DacFx is the same publish engine
`sqlpackage` wraps.

**Test:** `Twin.Tests.Integration.SamplePrCleanApply2Tests+SamplePrCleanApply2Tests.audit-columns: NOT NULL audit stamps with function defaults apply clean and backfill existing rows`

```
Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4, Duration: 1 m 16 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact — a populated table applies clean, and every existing row is backfilled by the function defaults.**
`Order` held **25 rows** and had no audit columns. The production-faithful publish of the three required
columns *with function defaults* was **accepted**; all three named defaults landed, and all 25 existing
rows carry a value with **zero NULLs**. Verbatim from the run:

```
baseline: Order rows=25, CreatedOn column exists=0
production publish (BlockOnPossibleDataLoss=true) ADD [CreatedOn]/[CreatedBy]/[UpdatedOn] NOT NULL with function defaults: APPLIED (Ok)
  after apply: CreatedOn exists=1 (is_nullable=0), CreatedBy exists=1 (is_nullable=0), UpdatedOn exists=1
  named defaults: DF_Order_CreatedOn=1 def=(sysutcdatetime()), DF_Order_CreatedBy=1 def=(suser_sname()), DF_Order_UpdatedOn=1
  backfill: Order rows=25 (intact), CreatedOn NULLs=0, CreatedBy NULLs=0, UpdatedOn NULLs=0, sample CreatedBy stamp=sa
```

The publish returned `Ok` under the production-faithful posture — the row-presence guard that blocks a
value-less required add (see `make-mandatory.md`) never fires, because the defaults give SQL Server a
value for every existing row. After the apply: all three columns exist `NOT NULL` (`is_nullable = 0`),
the named defaults exist with definitions `(sysutcdatetime())` and `(suser_sname())`, the row count is
unchanged (**25 → 25**), and **every** row was backfilled (`0` NULLs in each column). The sample stamp
`CreatedBy = sa` is the login that ran the deploy — the honest evidence of the deploy-time-stamp caveat
in the Summary.

## Verification — run in each environment after deployment

```sql
-- expect 0: no existing row is missing an audit value
SELECT COUNT(*) AS rows_missing_audit
FROM dbo.[Order]
WHERE CreatedOn IS NULL OR CreatedBy IS NULL OR UpdatedOn IS NULL;

-- expect 3 rows: the named defaults that stamped existing rows and stamp new ones
SELECT dc.name, c.name AS column_name, dc.definition
FROM sys.default_constraints dc
JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
WHERE dc.name IN ('DF_Order_CreatedOn', 'DF_Order_CreatedBy', 'DF_Order_UpdatedOn');
```

## Rollback

Drop the defaults, then the columns:

```sql
ALTER TABLE dbo.[Order] DROP CONSTRAINT DF_Order_CreatedOn, DF_Order_CreatedBy, DF_Order_UpdatedOn;
ALTER TABLE dbo.[Order] DROP COLUMN CreatedOn, CreatedBy, UpdatedOn;
```

This returns the table to its prior shape without touching pre-existing data — the columns held only
audit values introduced by this change (including the deploy-time stamps the defaults backfilled).
Backing the change out was not exercised.

## Not verified

- **Application impact — who stamps these going forward.** A `DEFAULT` fills a column only when an insert
  omits it and never on update, so nothing here maintains `UpdatedOn` on edit, and `CreatedBy` reflects
  the database login, not the application user, unless the application (or a trigger) writes these
  columns explicitly. Whether the app stamps them is not confirmed here — the application owner owns it.
- **The meaning of the backfilled values.** Every pre-existing row was stamped with the deploy-time
  `SYSUTCDATETIME()` and the deploying login (`sa` on the copy), not the real authoring time or user.
  Whether that is acceptable for historical orders — or whether a truer backfill is needed — is a
  data-owner decision.
- **Other environments.** Test / UAT / Prod hold rows this copy does not; the same defaults will stamp
  them at deploy time with that environment's deploy login and moment. Run the verification query after
  deployment in each.
- **Production scale and timing.** On a large table, stamping every row inside the `ALTER` may run long
  or block writes; the small copy cannot show that. Schedule a window.
