# AuditEvent: create a system-versioned table (point-in-time history from birth; one release)

## Verdict
This PR creates a new system-versioned (temporal) table `dbo.AuditEvent` with its paired history table.
No existing data is touched. Confirm the developer wants point-in-time row history — not a row-level
change feed — and that a history-retention policy is intended, before promoting.

## Intent
The developer's stated intent for this PBI: keep every prior version of every `AuditEvent` row, so a
past state of a record can be read at a point in time. No work item supplied — attach one before merge.

## What changes
- `dbo.AuditEvent`: a new system-versioned `CREATE TABLE` (`SYSTEM_VERSIONING = ON`) with a paired
  history table and two `GENERATED ALWAYS AS ROW START/END` `datetime2` period columns.

## Before promoting
- Confirm this is point-in-time history (temporal), not a row-level change feed for a downstream
  consumer — a different mechanism handled outside this change. Settle it at intake.
- Confirm a history-retention policy is intended: the paired history table grows with every update and
  has no cleanup unless one is set.

## The data
- None — the entity is new. Nothing to backfill.

## How it ships
- One release, applied in place. Temporal versioning is expressible declaratively for a new table, so
  SSDT publishes the system-versioned CREATE — the table, its history table, and the period columns —
  clean. No existing data is read or written.

## What proving showed
Published to a throwaway copy on this branch.
- **Tried / Did:** publish → the system-versioned CREATE lands clean; the history table and the two
  period columns appear, nothing blocked. `Successfully published database.`
- **Realized:** `temporal_type_desc = SYSTEM_VERSIONED_TEMPORAL_TABLE`, the history table is named, and
  the two period columns are `GENERATED ALWAYS AS ROW START` / `ROW END`.

## After deploy — check
```sql
-- expect one row, temporal_type_desc = SYSTEM_VERSIONED_TEMPORAL_TABLE, with a paired history table
SELECT t.name, t.temporal_type_desc, h.name AS history_table
FROM sys.tables t LEFT JOIN sys.tables h ON h.object_id = t.history_table_id
WHERE t.object_id = OBJECT_ID('dbo.AuditEvent');

-- expect 2 rows: the period columns, GENERATED ALWAYS AS ROW START and ROW END
SELECT c.name, c.generated_always_type_desc FROM sys.columns c
WHERE c.object_id = OBJECT_ID('dbo.AuditEvent') AND c.generated_always_type <> 0;
```

## How to roll this back
Remove the system-versioned CREATE from the project and republish. A system-versioned table cannot be
dropped directly: the delta sets `SYSTEM_VERSIONING = OFF` first (unlinking the history table), then
drops the main and history tables. Lossless only while both are unwritten — once the application writes
rows, dropping the pair discards the current rows and their accumulated history. Backing out was not exercised.

## Not checked / still open
- Application impact — a new entity nothing yet reads or writes does not change existing behaviour, but
  the code that will query history (`FOR SYSTEM_TIME`) is new and is not exercised by the copy (app owner).
- Design intent — the copy proves the table publishes clean; it cannot confirm that point-in-time
  history, and not a change feed, is what the use case needs. That is a design confirmation owed at intake.
- History growth and retention — the history table grows with every update and has no cleanup unless a
  retention policy is set; whether one is configured, and the versioning write overhead at production
  volumes, is not shown by the small copy.
