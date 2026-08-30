# AuditLog: rebuild the fragmented index — routed to maintenance, not shipped as a schema change

## Verdict
This is index maintenance (defragmentation), not a schema change: it does not belong in the SQL project
or this PR. Route it to a scheduled maintenance job keyed to measured fragmentation. Putting
`ALTER INDEX … REBUILD` in a post-deploy script re-runs it on every publish, taking a blocking lock
each time.

## Intent
The developer's stated intent for this PBI: defragment a fragmented index to restore query performance.
No work item supplied — attach one before merge (against the maintenance plan, not the schema project).

## What changes
- Nothing in the SQL project. The index definition is identical before and after; only its physical
  storage is defragmented, which is an operational act (`ALTER INDEX … REBUILD` / `REORGANIZE`).

## Before promoting
- This is not a schema change, so there is nothing to promote through the dacpac. Confirm a maintenance
  plan exists for this database to key the rebuild to (or route it to the DBA to set one up).

## The data
- No data changes. A rebuild rewrites the index's physical layout; the rows and the index definition
  are untouched.

## How it ships
- It does not ship in the dacpac. A rebuild or reorganize belongs in a scheduled maintenance job that
  runs when fragmentation crosses a threshold — not a deploy step.

## What proving showed
- **Realized:** the dacpac carries **no delta** for this — a publish issues no `ALTER INDEX` statement,
  so there is nothing to prove on a copy. The proof is the refusal: the request is kept out of the
  schema project and routed to the maintenance job.
- **Did (the harm, if forced):** an `ALTER INDEX … REBUILD` placed in a post-deploy script re-runs on
  every single publish — a recurring blocking operation, the opposite of idempotent intent.

## After deploy — check
```sql
-- the maintenance job keys on fragmentation, measured directly (not on any schema delta):
SELECT avg_fragmentation_in_percent
FROM sys.dm_db_index_physical_stats(DB_ID(), OBJECT_ID('dbo.AuditLog'), NULL, NULL, 'LIMITED')
WHERE index_id > 0;
```

## How to roll this back
Nothing to roll back in the schema — the dacpac is unchanged. A rebuild or reorganize changes only
physical storage, not the index definition, so it leaves no schema state to reverse.

## Not checked / still open
- Production lock and duration — the lock a `REBUILD` takes (online vs offline, Enterprise-gated) and
  how long it runs at production row counts are operational; the DBA/maintenance plan owns scheduling
  the window.
- The maintenance itself — whether fragmentation has actually crossed the threshold that warrants a
  rebuild is measured per environment by the job, out of band from this PR.
