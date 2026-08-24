# OrderLine: drop the table (all 8 rows are removed, and the schema alone cannot restore them)

## Verdict
This PR removes the whole `dbo.OrderLine` table and the 8 rows in it, in one release, through an
explicit scripted `DROP TABLE`. Confirm in each environment that nothing still reads `OrderLine` —
no report, export, or scheduled job — before promoting, because the drop cannot be undone from the
schema; the only way back is a database backup taken beforehand.

## Intent
The developer's stated intent for this PBI: remove the `OrderLine` entity from the model because it
is no longer used, and remove the underlying table with it. No work item supplied — attach one
before merge.

## What changes
- `dbo.OrderLine`: dropped. The `Modules/OrderLine.sql` `CREATE TABLE` is removed from the project,
  and a pre-deploy step runs `DROP TABLE dbo.OrderLine`.
- The `OrderLine` rows in the seed are removed with it, so the post-deploy seed no longer writes to
  the dropped table.

## Before promoting
- Run the reference query (below) in each environment and confirm 0 rows point at `OrderLine` — no
  foreign key, view, or procedure depends on it. `OrderLine` is a leaf here, so nothing does; a
  table with inbound foreign keys needs those dropped first, in the same script, before the table.
- Confirm the 8 rows are genuinely disposable — check with the report and export owners that none of
  them read `OrderLine`, because the drop removes the rows for good.
- Confirm Release 1 landed (the table is gone) in each environment before promoting to the next.

## The data
- 8 rows in `dbo.OrderLine`, object_id `1061578820`. Every row is removed by the drop.
- No table has a foreign key pointing at `OrderLine`, so no inbound relationship blocks the drop.

## How it ships
- One release, through a scripted drop. The production pipeline publishes with
  `DropObjectsNotInSource = false`, so removing `OrderLine.sql` alone does nothing — the table and
  its rows survive and the publish still reports success. The real removal is an explicit,
  idempotent pre-deploy `DROP TABLE dbo.OrderLine`, which is plain T-SQL the data-loss gate does not
  govern. The `.sql` is removed in the same release, so DacFx neither generates its own drop (which
  the gate would block) nor re-creates the table.
- The two-release pattern that ships a narrow or a populated `NOT NULL` does not transfer to a drop.
  Leaving the model holding `OrderLine` while a pre-deploy drops it makes the next publish re-create
  the table empty (proven below). The model must catch up — the `.sql` removed — in the same release
  as the scripted drop.
- The pre-deploy `DROP TABLE` is guarded by `IF OBJECT_ID('dbo.OrderLine','U') IS NOT NULL`, so it is
  safe to re-run and safe if a later step in the deploy fails and leaves the drop already done.
- Letting DacFx generate the drop instead (a publish with `DropObjectsNotInSource = true`) is blocked
  on a populated table and this pipeline cannot relax that block. That path is a diagnostic, not the
  shipping path.

## What proving showed
Published to a throwaway copy on this branch.
- **Tried:** remove `OrderLine.sql`, publish with the data-loss gate on and `DropObjectsNotInSource =
  true` (so DacFx generates the drop) → refused. `Warning SQL72015: The table [dbo].[OrderLine] is
  being dropped, data loss could occur.` then `Error SQL72014: … Msg 50000, Level 16, State 127, Line
  6 Rows were detected. The schema update is terminating because data loss might occur.` and `Could
  not deploy package.` The generated script guards the drop with `IF EXISTS (select top 1 1 from
  [dbo].[OrderLine]) RAISERROR (…) … DROP TABLE [dbo].[OrderLine];`. `OrderLine` survived with 8 rows.
  This is the same table-has-rows guard that blocks a narrow and a populated `NOT NULL`.
- **Did:** publish the file removal under the production posture (`DropObjectsNotInSource = false`) →
  `Successfully published database.` But `OrderLine` survived with 8 rows and object_id `1061578820`
  unchanged — removing the file removed nothing.
- **Realized:** a pre-deploy `DROP TABLE` while the model still declares `OrderLine` drops the table
  on the first publish, then the next publish re-creates it: `Creating Table [dbo].[OrderLine]…`, 0
  rows, a new object_id `1253579504`. The model has to stop declaring the table in the same release.
- **Did:** remove the `.sql` and run an idempotent pre-deploy `DROP TABLE dbo.OrderLine` under the
  production posture, over a table repopulated to 8 rows → `Pre-deploy: dropping dbo.OrderLine
  (scripted, idempotent).` then `Successfully published database.` The table is gone. A second publish
  with no change → `Successfully published database.`, the table still gone — a clean no-op.

## After deploy — check
```sql
-- before the drop: nothing still points at the table, expect 0 rows
SELECT referencing_schema_name, referencing_entity_name
FROM sys.dm_sql_referencing_entities('dbo.OrderLine', 'OBJECT');

-- after the drop: the table no longer exists, expect NULL
SELECT OBJECT_ID('dbo.OrderLine', 'U') AS table_object_id;
```

## How to roll this back
The table definition is restored from source control (the `CREATE TABLE` and its seed rows), but the
8 rows are not restored by re-creating the table — the drop is the irreversible act. Recovering the
data needs a database backup taken before the drop; that backup is not part of this change and is
arranged separately. The row count in the block message (8) records how many rows the drop removes.

## Not checked / still open
- Whether the 8 rows are truly disposable is the developer's call and is not settled on a copy. If
  any is needed, stop and reconsider before the drop lands — it cannot be reversed afterward.
- Application impact — any query, view, procedure, report, export, or job that names `OrderLine`
  fails once it is gone. `sys.dm_sql_referencing_entities` finds in-database references only, not
  application code or external consumers; the app owner confirms nothing outside the database reads it.
- Other environments — the 8-row count and the empty reference list were measured on a copy of Dev;
  Test, UAT, and Prod may hold different counts or references. Run the pre-drop checks before each
  promotion.
- Production scale and timing — at large row counts the drop may block writes or run long; the small
  copy does not show duration or locking at that scale.
