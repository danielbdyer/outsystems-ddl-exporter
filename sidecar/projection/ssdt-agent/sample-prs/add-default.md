# Customer: add a required IsActive with a default (the default backfills existing rows, so a populated table applies clean)

**In OutSystems** — You add a new Attribute `IsActive` (Boolean) to the `Customer` Entity, set *Is Mandatory = Yes*, and give it a default value of `True`.
**In SSDT** — a new line `[IsActive] BIT NOT NULL CONSTRAINT [DF_Customer_IsActive] DEFAULT ((1))` is added to the `Customer` table in `Tables/dbo.Customer.sql`. You add a required column *with a default*; SQL Server stamps every existing row from the default as the column lands.

## Summary

You add a required `IsActive` to `Customer`, defaulting to on. This is the **safe counterpart to
add-mandatory**: a required (`NOT NULL`) column with **no** default is refused on a populated table,
because SQL Server has no value for the rows already there — but the moment the column carries a
`DEFAULT`, SQL Server fills every existing row from it as the column is added, and the same populated
table **applies clean in place**. This was proven objectively against a Twin — a disposable SQL Server
database published from this estate and filled with real-shaped synthetic data — with a
**production-faithful** publish (`BlockOnPossibleDataLoss = true`, the deployment a real environment
runs). No work item was provided with the request; attach one before merge so the record is traceable.

**A precise distinction worth holding onto.** A default on an *already-existing* column is a rule for
*future* writes only — it never rewrites rows that already hold a value. This change is different:
`IsActive` is a *new* column, so no existing row has a value yet, and the default is what fills them.
Both facts are true and consistent — a default never overwrites an existing value, but a new required
column's default is exactly what backfills the rows that have none. That backfill is why this is the
honest, no-surprises way to add a required attribute to a populated table.

## Review & release

- Any team member can review this: the change is additive, existing values are untouched, and the
  running application keeps working (inserts that omit `IsActive` now receive the default).
- It ships as a single schema change, applied in place — SSDT emits `ALTER TABLE ... ADD [IsActive]
  BIT NOT NULL CONSTRAINT [DF_Customer_IsActive] DEFAULT ((1))`, and existing rows are stamped from
  the default in the same step. No gate relaxation, no staging.
- Name the constraint explicitly (`DF_Customer_IsActive`). Letting SSDT auto-name it
  (`DF__Customer__IsActi__<hash>`) yields a name that differs per environment and makes later diffing
  and dropping fragile.
- Added scrutiny: none. Adding a required column *with a default* is additive and touches no existing
  value beyond stamping the new column.

## Changes

| File | Change |
|---|---|
| `Tables/dbo.Customer.sql` | Adds `[IsActive] BIT NOT NULL CONSTRAINT [DF_Customer_IsActive] DEFAULT ((1))` to the table definition |

No renames (the refactorlog is unchanged). No index, view, or procedure changes. Only the new
`IsActive` column and its named default are added; every existing column is untouched.

## Data remediation

None required — the default *is* the remediation. Because the column ships with `DEFAULT ((1))`, SQL
Server backfills every existing row to `1` as the column is added, so no row is ever value-less and the
`NOT NULL` lands clean. This is precisely the fix the *add-mandatory* change points to: a required
column blocks a populated table until it carries a default (or a pre-deployment backfill supplies the
value). The default value itself (`1` = active) is a data-owner decision, named under Not verified.

## Deployment evidence — objective proof, production-faithful publish, 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, materializes
real-shaped data, applies the additive edit, and asserts the outcome under a **production-faithful**
DacFx posture (`BlockOnPossibleDataLoss = true`, no smart-defaults). DacFx is the same publish engine
`sqlpackage` wraps.

**Test:** `Twin.Tests.Integration.SamplePrCleanApplyTests+SamplePrCleanApplyTests.add-default: a NOT NULL column with a default applies clean and backfills existing rows`

```
Passed!  - Failed:     0, Passed:     6, Skipped:     0, Total:     6, Duration: 1 m 24 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact — a populated table applies clean, and every existing row is backfilled.** `Customer` held
**25 rows** and had no `IsActive` column. The production-faithful publish of the required column *with
a default* was **accepted**; the named default constraint landed, and all 25 existing rows carry the
default value. Verbatim from the run:

```
baseline: Customer rows=25, IsActive column exists=0
production publish (BlockOnPossibleDataLoss=true) ADD [IsActive] BIT NOT NULL DEFAULT ((1)): APPLIED (Ok) on the populated table.
  after apply: IsActive exists=1, is_nullable=0 (mandatory), DF_Customer_IsActive exists=1, definition=((1))
  backfill: Customer rows=25 (intact), rows with IsActive=1 = 25, rows NOT carrying the default = 0
```

The publish returned `Ok` under the production-faithful posture — the row-presence guard that blocks a
value-less required add (see `add-mandatory.md`) never fires, because the default gives SQL Server a
value for every existing row. After the apply: the column is `NOT NULL` (`is_nullable = 0`), the named
default `DF_Customer_IsActive` exists with definition `((1))`, the row count is unchanged
(**25 → 25**), and **all 25** rows carry `IsActive = 1` with **0** rows escaping the default.

## Verification — run in each environment after deployment

```sql
-- expect 1 row, is_nullable = 0: the required column exists and rejects NULLs
SELECT c.name AS column_name, c.is_nullable
FROM sys.columns c
WHERE c.object_id = OBJECT_ID('dbo.Customer') AND c.name = 'IsActive';

-- expect 1 row: the named default that stamped existing rows and stamps new ones
SELECT dc.name, dc.definition
FROM sys.default_constraints dc
JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
WHERE dc.name = 'DF_Customer_IsActive';

-- expect 0: every existing row was backfilled from the default
SELECT COUNT(*) AS rows_missing_default FROM dbo.Customer WHERE IsActive <> 1;
```

## Rollback

Drop the default first, then the column:

```sql
ALTER TABLE dbo.Customer DROP CONSTRAINT DF_Customer_IsActive;
ALTER TABLE dbo.Customer DROP COLUMN IsActive;
```

Dropping the column discards the values it held (the default-stamped and later-entered values); every
other column in each row is unchanged. Backing the change out was not exercised.

## Not verified

- **Application impact.** Inserts that omit `IsActive` now receive the default (`1`) instead of failing;
  whether application code supplies a meaningful value rather than leaning on the default is not
  confirmed here — the application owner owns closing this before promotion.
- **The default value.** `1` (active) is used here as a reasonable default; the correct default for the
  business is a data-owner decision. Every existing row was stamped with it, so choosing it changes
  historical rows' meaning, not just future inserts.
- **Other environments.** An existing unnamed default on this column in Test/UAT/Prod (there should be
  none, since the column is new) would collide; the disposable copy of Dev cannot see other
  environments. Run the verification query before promotion.
- **Production scale and timing.** On a large table, adding a `NOT NULL` column with a default may run
  long or block writes while every row is stamped; the small copy cannot show that. Schedule a window.
