# Customer.Segment: add a required attribute with a default (existing rows stamped, ships in one release)

## Verdict
This change adds a required `Segment` to `dbo.Customer` with a default of `Standard`, which stamps
the 5 customers that already exist and ships in one release. Confirm `Standard` is the right value
for the existing rows, or set them deliberately before merge. No work item supplied — attach one
before merge.

## Intent
The developer's stated intent for this PBI: add a `Segment` attribute that every customer must have
— "add a required attribute, everyone must have one". No work item supplied — attach one before
merge.

## What changes
- `dbo.Customer`: add `Segment NVARCHAR(20) NOT NULL CONSTRAINT DF_Customer_Segment DEFAULT (N'Standard')`.

## Before promoting
- Confirm `Standard` is the correct value for the customers that already exist. A default stamps
  every existing row with the same value; if the existing customers need different segments, set
  them deliberately in a follow-up rather than leaving the default in place.
- Check with the application owner that new inserts supply a real `Segment` rather than leaning on
  the default, and that no insert path breaks now that the column exists and is required.

## The data
- 5 customers, none of which has a `Segment` today (the column is new). The default `Standard`
  stamps all 5 as the column is added.
- No existing column is touched.

## How it ships
- One release. With the default in the column definition, DacFx generates a single
  `ALTER TABLE [dbo].[Customer] ADD [Segment] NVARCHAR (20) CONSTRAINT [DF_Customer_Segment]
  DEFAULT (N'Standard') NOT NULL;`. SQL Server fills every existing row from the default as the
  column is added; no data-loss step is generated, so the guard never fires.
- With `IgnoreColumnOrder` on (the estate's publish posture) DacFx ignores where the attribute sits
  in the model — proven on a copy: placing `Segment` in the middle of the `Customer` column list still
  generated a plain `ALTER TABLE … ADD` at the end, not a rebuild. (Only with `IgnoreColumnOrder`
  **off** would a mid-list insert force DacFx to copy every row into a shadow table to place the column
  physically.)
- Without a default on this populated table, the publish is refused: SQL Server has no value for the
  existing rows. A default, or a value for every existing row, is required — do not enable
  `GenerateSmartDefaults`, which invents a value silently.

## What proving showed
Published to a throwaway copy on this branch.
- **Tried:** add `Segment NVARCHAR(20) NOT NULL` with no default, publish under the guard →
  refused. Warning SQL72015: "The column [dbo].[Customer].[Segment] … must be added, but the column
  has no default value and does not allow NULL values." `Msg 50000`: "Rows were detected. The schema
  update is terminating because data loss might occur." The column was not added.
- **Did:** give the column `DEFAULT (N'Standard')` and publish → published. The generated script is
  a single `ALTER TABLE [dbo].[Customer] ADD [Segment] NVARCHAR (20) CONSTRAINT
  [DF_Customer_Segment] DEFAULT (N'Standard') NOT NULL;` with no row-presence guard. All 5 customers
  read `Segment = 'Standard'`; the column is `NOT NULL`. Re-publish → published, nothing changed.
- **Realized:** the block is not the same as tightening an existing column — it is a plain
  can't-insert-NULL on a new column with no value for the rows already there. Supplying the value
  clears it, and with a default the whole change is one clean release. The value that would have
  been invented is only visible because the default makes it explicit.

## After deploy — check
```sql
-- the column exists and rejects NULL, expect is_nullable = 0
SELECT is_nullable FROM sys.columns
WHERE object_id = OBJECT_ID('dbo.Customer') AND name = 'Segment';

-- the named default that stamps existing and new rows, expect 1 row
SELECT dc.name, dc.definition
FROM sys.default_constraints dc
JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
WHERE dc.parent_object_id = OBJECT_ID('dbo.Customer') AND c.name = 'Segment';
```

## How to roll this back
Drop the default, then the column:
`ALTER TABLE dbo.Customer DROP CONSTRAINT DF_Customer_Segment;` then
`ALTER TABLE dbo.Customer DROP COLUMN Segment;`. Dropping the column discards the values it held
(the stamped `Standard` and any later-entered values); every other column in each row is unchanged.
Backing the change out was not exercised.

## Not checked / still open
- The value for existing rows — `Standard` is a blanket default. Whether each existing customer's
  real segment should be set instead is not settled here (data owner).
- Application impact — inserts that omit `Segment` now rely on the default; whether application code
  supplies a meaningful value is not confirmed (app owner). If the column ever ships without an
  explicit default, a profile with `GenerateSmartDefaults` on may stamp a value this copy did not.
- Fallback with no default — if new rows must always supply `Segment` and no default is acceptable,
  the existing rows still need a value: add the column nullable, backfill it in a pre-deploy, then
  tighten to `NOT NULL` with the model lagging (Release 1) and let the model catch up (Release 2) —
  the two-release pattern in `make-mandatory.md`. Not exercised here.
- Other environments — QA, UAT, and Prod hold their own row counts the copy cannot see; the block
  fires in every populated environment without a default. Confirm the default lands in each.
- Production scale and timing — adding the column with a default at production row counts, or a
  table rebuild if the column is not appended at the end, may run long or block writes. Schedule a
  window.
