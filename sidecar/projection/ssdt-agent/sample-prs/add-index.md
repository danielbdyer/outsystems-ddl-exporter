# Order.StatusId: add the index its foreign key needs (SQL Server does not index the child side)

## Verdict
This PR adds a nonclustered index on `Order.StatusId`, the column its foreign key to Status points
through. SQL Server indexes the parent side of a foreign key, not the child, so the `Order → Status`
join scans `Order` until this lands. No data changes; the build takes a brief write-blocking lock.
Confirm the target edition and a maintenance window before promoting if `Order` is large.

## Intent
The developer's stated intent for this PBI: speed the `Order → Status` join and the Status-side delete
check by indexing the foreign-key column, which SQL Server left unindexed. No work item supplied —
attach one before merge.

## What changes
- `dbo.[Order]`: add a nonclustered index `IX_Order_StatusId` on `(StatusId)`.

## Before promoting
- An index build takes a write-blocking lock whose duration scales with row count. If `Order` is large
  in an environment, schedule a window, or use `WITH (ONLINE = ON)` where the edition is
  Enterprise/Developer (it fails on Standard). Confirm the target edition.

## The data
- 4 orders. No data changes; an index is a derived structure built from the rows already present.

## How it ships
- One release, applied in place. SSDT emits `CREATE NONCLUSTERED INDEX` and builds it over every
  existing row — a real build, not a metadata flip. No row is read or written.

## What proving showed
Published to a throwaway copy on this branch.
- **Realized (the reason):** with `FK_Order_Status` present and no explicit index, `sys.indexes` for
  `dbo.[Order]` shows only `PK_Order_Id` on `Id` — **nothing on `StatusId`**. SQL Server does not index
  the child side of a foreign key.
- **Did:** add the index, publish → `Successfully published database.` `IX_Order_StatusId` lands
  `NONCLUSTERED`, `is_unique = 0`, `is_disabled = 0`; the row count is unchanged.

## After deploy — check
```sql
-- expect one row, is_disabled = 0: the index landed and is enabled
SELECT name, type_desc, is_unique, is_disabled
FROM sys.indexes
WHERE object_id = OBJECT_ID('dbo.[Order]') AND name = 'IX_Order_StatusId';
```

## How to roll this back
`DROP INDEX IX_Order_StatusId ON dbo.[Order];` — lossless: the index holds no source data, only a
derived structure. Re-adding it runs the same write-blocking build. Backing the change out was not exercised.

## Not checked / still open
- Production build time and lock duration — the copy is small, so its build was instant; the production
  build time and how long the write-blocking lock lasts are governed by the production row count, which
  the copy does not exercise.
- Target edition — `WITH (ONLINE = ON)` requires Enterprise/Developer; on Standard the build blocks
  writes for its duration. The target's edition is not confirmed here.
- Worth its write cost — every index adds a small cost to each insert and update; for a foreign-key
  column the join it serves exists by construction, so this one is near-certainly worth it, but on a
  write-heavy table confirm the trade fits the workload.
