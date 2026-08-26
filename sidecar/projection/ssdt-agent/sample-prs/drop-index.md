# Customer: drop IX_Customer_Region (proposed unused — usage evidence, not the publish, is the proof)

## Verdict
This PR drops the index `IX_Customer_Region`. Dropping an index loses no data and reverses by
re-creating it, but "unused" is an assumption — confirm from usage statistics in each environment that
nothing seeks it over a representative window before promoting. A dropped index that backed a hot query
is a silent slowdown with nothing to warn anyone.

## Intent
The developer's stated intent for this PBI: remove an index believed to be unused, to save its write
and storage cost. No work item supplied — attach one before merge.

## What changes
- `dbo.Customer`: drop the nonclustered index `IX_Customer_Region`.

## Before promoting
- From a prod-shaped source, run the usage query (below) over a representative window and confirm zero
  `user_seeks` / `user_scans` / `user_lookups`. If any environment shows usage, stop — the index is
  load-bearing there.

## The data
- No data is touched. An index is a derived structure, not stored source data.

## How it ships
- One release, applied in place. SSDT emits a single `DROP INDEX`; no row is read or written, and the
  publish never blocks.

## What proving showed
Published to a throwaway copy on this branch.
- **Tried / Did:** publish the drop → the delta is a clean `DROP INDEX IX_Customer_Region ON
  dbo.Customer;` and the publish is green.
- **Realized:** a copy carries no production query load, so the clean publish is **not** the proof.
  The proof is usage evidence — `sys.dm_db_index_usage_stats` showing zero seeks/scans/lookups over a
  representative window in each real environment.

## After deploy — check
```sql
-- BEFORE, from a prod-shaped source: expect zero user_seeks/user_scans/user_lookups over a
-- representative window — the index is unused
SELECT user_seeks, user_scans, user_lookups
FROM sys.dm_db_index_usage_stats
WHERE object_id = OBJECT_ID('dbo.Customer');

-- AFTER: expect 0 rows — the index is gone
SELECT name FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Customer') AND name = 'IX_Customer_Region';
```

## How to roll this back
Re-create the index (revert the `.sql` edit and republish); SSDT emits `CREATE INDEX`. Lossless — an
index holds no source data — but re-creating it runs a write-blocking build whose duration scales with
row count. Backing the change out was not exercised.

## Not checked / still open
- Application impact — a copy carries no production load, so whether any query depends on this index and
  would slow down once it is gone is not shown by the publish. Usage evidence from a prod-shaped source
  settles it (app owner).
- Other environments — usage patterns differ by environment; zero seeks in one window does not prove
  zero in QA, UAT, or Prod. Run the usage query in each before promotion.
- Reversibility — re-creating the index restores the structure, but the rebuild time and the
  write-blocking lock at production row counts are not measured on the small copy.
