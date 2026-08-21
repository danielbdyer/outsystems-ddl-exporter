# Customer: convert to temporal (across releases; period columns backfilled with sane ROW START, rows untouched)

## Verdict
This PR converts the existing populated `Customer` table to system-versioned (temporal). It stages
across releases: add the period columns with backfilled start times, create the history table, then
enable versioning. It hinges on sane historical `ROW START` values — left to default, every existing row
falsely claims to have begun at conversion time. Confirm the backfill start time in each environment
before promoting.

## Intent
The developer's stated intent for this PBI: start keeping point-in-time history on the existing
`Customer` table. No work item supplied — attach one before merge.

## What changes
- `dbo.Customer`: add the (hidden) `GENERATED ALWAYS AS ROW START/END` period columns with backfilled
  start values, create the paired history table, then turn `SYSTEM_VERSIONING = ON` — staged across
  releases.

## Before promoting
- Confirm the `ROW START` backfill value: the conversion date, or a real historical date if the business
  tracks when each row began. Left to default, the history is dated to conversion time.
- Confirm this is point-in-time history (temporal), not a row-level change feed.

## How it ships
- Across several releases so the running application keeps working while the period columns are added,
  backfilled, and system versioning is turned on. Existing data is modified (the backfill). A new entity
  is the single-release op (`temporal-new`).

## The data
- Existing `Customer` rows get backfilled period columns; their other column values are untouched.

## What proving showed
Published to a throwaway copy on this branch.
- **Tried / Did:** the period-column backfill produces sane `ROW START` values, and enabling versioning
  is not blocked on the populated table.
- **Realized:** the before/after content hash of the existing rows matches — the rows themselves are
  untouched by the conversion. The failure this avoids is every historical row falsely dated to
  conversion time, which quietly corrupts the very history versioning was turned on to keep.

## After deploy — check
```sql
-- expect one row, temporal_type_desc = SYSTEM_VERSIONED_TEMPORAL_TABLE, with a paired history table
SELECT t.name, t.temporal_type_desc, h.name AS history_table
FROM sys.tables t LEFT JOIN sys.tables h ON h.object_id = t.history_table_id
WHERE t.name = 'Customer';

-- expect 0 rows: every existing row's ROW START predates the conversion (not stamped to conversion time)
SELECT Id, ValidFrom FROM dbo.Customer WHERE ValidFrom >= '<conversion timestamp>';
```

## How to roll this back
Turn `SYSTEM_VERSIONING = OFF`, then drop the period columns and the history table — the mirror of the
conversion. The existing rows' pre-existing values are unchanged by the conversion (the before/after
hash matches), so backing out the schema is lossless for them; any history accumulated after go-live is
lost when the history table is dropped. Backing the change out was not exercised.

## Not checked / still open
- Application impact — how the running application behaves against a system-versioned table: explicit
  column-list writes, `SELECT *`, and any attempt to write the hidden period columns are not confirmed
  here (app owner).
- Other environments — whether Test/UAT/Prod row counts change the backfill outcome or the timing is not
  shown by this copy.
- Production scale — enabling versioning and backfilling against a large table may block writes or run
  long; the small copy does not exercise it.
