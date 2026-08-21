# OrderLine → OrderItem: rename the table (a refactorlog entry keeps all 8 rows; without it every row is lost)

## Verdict
This PR renames `dbo.OrderLine` to `dbo.OrderItem`, keeping all 8 rows and the table's identity,
because a refactorlog entry records that the old and new names are the same table. Confirm the
`.refactorlog` entry travels with the build into each environment before promoting, and confirm
every caller of the old name — foreign keys, views, procedures, reports, ETL — is updated, because
the rename breaks each one.

## Intent
The developer's stated intent for this PBI: rename the `OrderLine` entity to `OrderItem` in Service
Studio and have the data follow the new name, exactly as the platform does it. No work item supplied
— attach one before merge.

## What changes
- `dbo.OrderLine` → `dbo.OrderItem`: the table name in the `CREATE TABLE` header changes, and a
  refactorlog entry records the rename so DacFx emits `sp_rename` rather than a drop-and-recreate.
- The table's own constraint names are left unchanged (`PK_OrderLine_Id`, `DF_OrderLine_Amount`), so
  DacFx performs a pure metadata rename and does not rebuild the table.

## Before promoting
- Confirm the `.refactorlog` entry for `[dbo].[OrderLine]` → `[OrderItem]` is present in the promoted
  build. Script the delta and confirm it reads `EXEC sp_rename … 'OBJECT'`, not `DROP TABLE` +
  `CREATE TABLE`. This is the single check that separates a safe rename from silent data loss.
- Confirm every caller of `dbo.OrderLine` is repointed to `dbo.OrderItem` — foreign keys pointing at
  it, views, procedures, synonyms, reports, ETL, and application code. The old name stops resolving
  the moment the rename lands.

## The data
- 8 rows in `dbo.OrderLine`, object_id `1061578820`. The rename keeps every row and keeps the
  object_id, so the table that carried the rows is the same table under the new name.
- No table has a foreign key pointing at `OrderLine`, so no child relationship breaks; the table's own
  foreign key and primary key keep their old names after the rename.

## How it ships
- One release, applied in place. With the refactorlog entry present, the delta is a single
  `EXEC sp_rename`, which is a metadata operation: the rows and the object's identity are preserved,
  and no data is read or rewritten. The data-loss gate is not engaged, because no data is moved.
- Without the refactorlog entry the change does not ship as a rename at all. DacFx sees the old table
  vanish and a new one appear, and the delta becomes `DROP TABLE dbo.OrderLine` + `CREATE TABLE
  dbo.OrderItem`. On a populated table the data-loss gate blocks that drop; under a relaxed gate it
  would drop the 8 rows outright. Ship the refactorlog entry, or do not ship the rename.

## What proving showed
Published to a throwaway copy on this branch.
- **Tried:** rename the header from `dbo.OrderLine` to `dbo.OrderItem` with no refactorlog entry,
  script the delta → it reads `DROP TABLE [dbo].[OrderLine];` then `CREATE TABLE [dbo].[OrderItem]`.
  Publish → refused. `Warning SQL72015: The table [dbo].[OrderLine] is being dropped, data loss could
  occur.` then `Error SQL72014: … Msg 50000, Level 16, State 127, Line 6 Rows were detected. The
  schema update is terminating because data loss might occur.` and `Could not deploy package.`
  `OrderLine` survived with 8 rows; `OrderItem` was never created.
- **Did:** add the refactorlog entry for `[dbo].[OrderLine]` → `[OrderItem]`, rebuild, script the
  delta → it now reads `EXECUTE sp_rename @objname = N'[dbo].[OrderLine]', @newname = N'OrderItem',
  @objtype = N'OBJECT';`. Publish → `Rename [dbo].[OrderLine] to OrderItem`, `Successfully published
  database.`
- **Realized:** `OrderLine` is gone, `OrderItem` holds all 8 rows, and its object_id is `1061578820`
  — the same value the table had before the rename, so identity was preserved, not re-minted. A
  second publish with no change was a clean no-op.
- **Realized:** renaming the table's `PK_` and `DF_` constraints in the same edit, without their own
  refactorlog entries, made DacFx rebuild the table through a shadow copy (`tmp_ms_xx_OrderItem`) —
  the 8 rows still survived, but the object_id changed to `1253579504`. Leaving the constraint names
  unchanged keeps the rename a pure metadata operation.

## After deploy — check
```sql
-- exactly one table, named OrderItem, holding the full row count, expect one row
SELECT t.name, SUM(p.rows) AS row_count, t.object_id
FROM sys.tables t
JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
WHERE t.name IN ('OrderLine', 'OrderItem')
GROUP BY t.name, t.object_id;

-- the old name no longer resolves, expect an "Invalid object name" error
SELECT TOP 1 1 FROM dbo.OrderLine;
```

## How to roll this back
The rename reverses without data loss: rename back with `EXEC sp_rename 'dbo.OrderItem', 'OrderLine',
'OBJECT'`, carried by its own refactorlog entry so the reverse is declarative, and repoint every
caller back to `OrderLine`. The caller edits are not auto-reversed. A rename that ever went through
as the drop-and-recreate has already lost the rows and cannot be rolled back from the schema — the
only way back there is a database backup.

## Not checked / still open
- Application impact — every caller of the old name breaks until repointed; the old name stops
  resolving (`Invalid object name`, proven above). That all callers were found and repointed is not
  confirmed on a copy — the app owner owns closing this before promotion.
- Constraint, index, and trigger names — the rename leaves the table's own objects under their old
  names (`PK_OrderLine_Id`, `DF_OrderLine_Amount`). Renaming them to match is cosmetic and was not done.
- Other environments — the rename was proven on a copy of Dev, where the refactorlog entry is present.
  That the entry travels into Test, UAT, and Prod is the load-bearing risk; confirm the delta reads
  `sp_rename` before each promotion.
- Reversibility — only the forward rename was exercised; the reverse rename and the caller reverts
  were not.
