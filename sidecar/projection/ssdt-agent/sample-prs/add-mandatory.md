# OrderLine: add a required Amount (a populated table is blocked until the new column has a default)

**In OutSystems** — You add a new Attribute `Amount` (Decimal) to the `OrderLine` Entity and set *Is Mandatory = Yes*, with no default value.
**In SSDT** — a new line `[Amount] DECIMAL(18,2) NOT NULL` is added to the `OrderLine` table in `Tables/dbo.OrderLine.sql`. You add the column to the table definition; the publish engine decides whether the table's existing rows let a *required, value-less* column land.

## Summary

You add a required `Amount` to `OrderLine`. In OutSystems, Service Studio would quietly pick a
platform default for the rows that already exist; here the same intent is a new `NOT NULL` column
with **no default**, and on a **populated** table a production publish **refuses it** — SQL Server has
no value to put in the column for the 25 rows already there, so the deployment is blocked. This was
proven objectively against a Twin — a disposable SQL Server database published from this estate and
filled with real-shaped synthetic data — with a **production-faithful** publish
(`BlockOnPossibleDataLoss = true`, the deployment a real environment runs).

This is **not** the make-mandatory case (tightening an *existing* column). Here the column is *new*
and empty for every existing row, and the fix is the one thing SQL Server cannot invent on its own: a
**value** for the rows that already exist — supplied as a `DEFAULT` (the separate *add-default* change)
or a pre-deployment backfill. No work item was provided with the request; attach one before merge so
the record is traceable.

## Review & release

- A dev lead or an experienced developer should review this: adding a required attribute means the
  running application must change to keep working — any insert that omits `Amount` now fails.
- It does not ship as a single in-place edit while `dbo.OrderLine` holds rows. It ships one of two
  ways: **(a)** give the column an explicit `DEFAULT` — then it ships as a single schema change,
  applied in place, and SQL Server stamps every existing row from the default as the column is added
  (that is the *add-default* change); or **(b)** a pre-deployment backfill that fills the rows before
  the column lands. On an **empty** table the same edit ships as one clean declarative change.
- Do **not** reach for `GenerateSmartDefaults` to get past the block — it silently invents a value
  (e.g. `0`) for every existing row with no signal that it did. The value for existing rows is a
  data-owner decision, made explicitly.
- Added scrutiny: first time this operation is proven on the Twin. At production row counts the column
  add (once it carries a default) may run long or block writes — schedule a window.

## Changes

| File | Change |
|---|---|
| `Tables/dbo.OrderLine.sql` | Adds `[Amount] DECIMAL(18,2) NOT NULL` (no default) to the table definition |

No renames (the refactorlog is unchanged). No index, view, or procedure changes. Only the new `Amount`
column is added; every existing column is untouched.

## Data remediation

The block is not cleared by editing the table — it is cleared by supplying a **value** for the rows
that already exist. Two honest paths, both a data-owner decision (named under Not verified):

- **Add a default** — change the column to `NOT NULL CONSTRAINT DF_OrderLine_Amount DEFAULT (<value>)`.
  SQL Server fills every existing row from the default as the column is added, and the publish lands
  in place. This is the *add-default* change.
- **Pre-deployment backfill** — a script that inserts the intended value into a nullable staging of
  the column before it is tightened, so no row is value-less when the `NOT NULL` lands.

On the proof substrate the two facts were observed directly:
- On the **populated** table (25 rows), the value-less `NOT NULL` add was **refused**.
- On the table **emptied to 0 rows**, the identical edit **applied** clean (the column landed
  `NOT NULL`).

## Deployment evidence — objective proof, production-faithful publish, 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, seeds the condition
in real-shaped data, and asserts the outcome under a **production-faithful** DacFx posture
(`BlockOnPossibleDataLoss = true`, no smart-defaults). DacFx is the same publish engine `sqlpackage`
wraps.

**Test:** `Twin.Tests.Integration.SamplePrTighteningTests+SamplePrTighteningTests.add-mandatory: a new NOT NULL column with no default blocks a populated table, applies when empty`

```
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 1 m 51 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact 1 (primary) — a populated table is REFUSED.** OrderLine held **25 rows** and had no `Amount`
column. The production-faithful publish of the value-less `NOT NULL` column was refused. The warning
names the cause (no default for the existing rows); the block itself is the data-loss guard. Verbatim
from the run:

```
Could not deploy package.
Warning SQL72015: The column [dbo].[OrderLine].[Amount] on table [dbo].[OrderLine] must be added, but the column has no default value and does not allow NULL values. If the table contains data, the ALTER script will not work. To avoid this issue you must either: add a default value to the column, mark it as allowing NULL values, or enable the generation of smart-defaults as a deployment option.
Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 50000, Level 16, State 127, Line 6 Rows were detected. The schema update is terminating because data loss might occur.
Error SQL72045: Script execution error.  The executed script:
IF EXISTS (SELECT TOP 1 1
           FROM   [dbo].[OrderLine])
    RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127)
        WITH NOWAIT;
```

After the refusal: `OrderLine` rows = **25** (intact), the `Amount` column was **not added** — the
block left the schema and data exactly as they were.

**Fact 2 (contrast) — the same edit on an EMPTY table applies clean.** With OrderLine emptied to 0
rows, the identical value-less `NOT NULL` add published successfully under the same production-faithful
posture:

```
CONTRAST empty-table production publish APPLIED: Amount column exists=1, is_nullable=0 (mandatory), rows=0
```

`is_nullable = 0` — the column is now mandatory. With no rows present, there is nothing to fill, so
nothing blocks.

## Verification — run in each environment after deployment

```sql
-- AFTER deployment — expect 1 row, is_nullable = 0: the column exists and rejects NULLs
SELECT c.name AS column_name, c.is_nullable
FROM sys.columns c
WHERE c.object_id = OBJECT_ID('dbo.OrderLine') AND c.name = 'Amount';

-- expect 1 row when a default was supplied: the named default that stamps existing and new rows
SELECT dc.name, dc.definition
FROM sys.default_constraints dc
JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
WHERE dc.parent_object_id = OBJECT_ID('dbo.OrderLine') AND c.name = 'Amount';
```

## Rollback

The column drops back out without touching the pre-existing columns. If a default was added, drop it
first:

```sql
ALTER TABLE dbo.OrderLine DROP CONSTRAINT DF_OrderLine_Amount;
ALTER TABLE dbo.OrderLine DROP COLUMN Amount;
```

Dropping the column discards the values it held (the default-stamped or later-entered values); every
other column in each row is unchanged. Backing the change out was not exercised.

## Not verified

- **Application impact.** Any insert that omits `Amount` now fails once the column is mandatory. With
  a default, inserts that omit it rely on that default; whether application code supplies a meaningful
  value rather than leaning on the default is not confirmed here — the application owner owns closing
  this before promotion.
- **The value for existing rows.** No default or backfill value is chosen here; a data owner must
  supply it. If the column ever ships without an explicit default, a profile with
  `GenerateSmartDefaults` enabled may silently stamp a value this copy did not.
- **Other environments.** Test, UAT, and Prod hold their own row counts the disposable copy cannot
  see; the block fires in *every* populated environment. Plan the default (or backfill) in each before
  promotion.
- **Production scale and timing.** On a large table, adding a `NOT NULL` column with a default may run
  long or block writes; the small copy cannot show that. Schedule a window.
- **Reversibility.** The forward publish is proven (both the block and the empty-table apply); backing
  the change out was not exercised.
