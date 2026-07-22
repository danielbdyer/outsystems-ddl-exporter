# Customer / IX_Customer_Email: rebuild the index — operational maintenance, not a schema change (the deploy carries no delta)

**In OutSystems** — You ask to *rebuild / reorganize* an index because it is fragmented, or you want the deploy to "rebuild the index" as part of shipping.
**In SSDT** — `ALTER INDEX ... REBUILD` is a maintenance operation against the data's **physical storage**, not a change to the index's *definition*. There is no declarative destination for "rebuild me" — the dacpac has nothing to converge to, so a publish issues no statement for it.

## Summary

You want an index rebuilt. That is genuine maintenance, but it is **not a schema change** — the index's
definition is identical before and after; only its physical storage is defragmented. Because SSDT converges the
*described shape* and a rebuild changes only *storage*, there is nothing for the deploy to do: proven against the
Twin, a publish of an estate already at its converged shape returns **`NothingToApply`**, and it **still** returns
`NothingToApply` after the index has been physically rebuilt out-of-band. This was proven objectively against a
Twin — a disposable SQL Server database published from this estate and filled with real-shaped synthetic data.
The honest outcome of this request is a **routing decision**, not a PR that changes files: send the rebuild to a
scheduled maintenance job keyed to measured fragmentation. No work item was provided with the request; if a
maintenance job or plan does not exist, that is what to open.

**Do not try to express maintenance declaratively.** Putting `ALTER INDEX ... REBUILD` in a post-deploy script
makes it re-run on **every** publish — a blocking operation that fires whether or not the index is fragmented,
the opposite of an idempotent deploy. The rebuild belongs in a job that runs when fragmentation crosses a
threshold, not in the schema project.

## Review & release

- This is **not** reviewed as a schema change and does not ship in the dacpac: a rebuild or reorganize alters
  only the index's physical storage, not its definition. Route it to a scheduled maintenance job keyed to
  measured fragmentation.
- Nothing in `Tables/*.sql` changes for a rebuild. If this request arrived alongside a real schema change, keep
  the rebuild **out** of that PR and note it as an out-of-band operational step.
- Added scrutiny, when a rebuild does ride alongside a real change as an out-of-band step on a large table: a
  `REBUILD` takes a lock (online vs offline, Enterprise-gated) with a real duration at production row counts —
  schedule a window with the DBA.

## Changes

| File | Change |
|---|---|
| *(none)* | A rebuild has no declarative representation — no estate file changes, and a publish emits no `ALTER INDEX` statement. |

This PR's contribution is a **boundary finding**: the request is kept out of the schema project and routed to the
maintenance job. There is nothing to merge into the dacpac.

## Data remediation

None — a rebuild reads and re-derives the index's own pages; it never changes a source `Customer` value. Proven
below: the `Email` content digest is byte-identical before and after the rebuild.

## Deployment evidence — objective proof, production-faithful publish, 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin **with the index already at its
converged shape**, shows the publish has nothing to apply, performs the physical rebuild out-of-band, and shows
the publish *still* has nothing to apply — demonstrating the rebuild never enters the declarative model. The
convergence check is the Twin's own `Runs.up`; the strict production posture is the same one the other sample
PRs deploy under. DacFx is the same publish engine `sqlpackage` wraps.

**Test:** `Twin.Tests.Integration.SamplePrSchemaChangeTests+SamplePrSchemaChangeTests.rebuild-index: ALTER INDEX REBUILD is operational maintenance with no declarative delta (NothingToApply)`

```
Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4, Duration: 1 m 22 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact — the deploy has no delta before OR after the rebuild; the definition and data are unchanged.** With
`IX_Customer_Email` already converged, a re-publish reported `NothingToApply`; the index was then physically
rebuilt with a direct `ALTER INDEX ... REBUILD`; and a subsequent re-publish *still* reported `NothingToApply`.
Verbatim from the run:

```
baseline: index converged; second `up` (no change) -> NothingToApply (tables=5, rows=78, scenario=default)
  before rebuild: IX_Customer_Email exists=1, key columns=1, type_desc=NONCLUSTERED, Customer rows=25, Email digest=795751655, avg_fragmentation_in_percent=0
operational: ALTER INDEX [IX_Customer_Email] ON [dbo].[Customer] REBUILD executed out-of-band (a direct DB maintenance command, not a publish).
  after rebuild: index exists=1, key columns=1, type_desc=NONCLUSTERED (definition unchanged), Customer rows=25, Email digest=795751655 (data unchanged), avg_fragmentation_in_percent=0
  third `up` AFTER the physical rebuild -> NothingToApply (tables=5, rows=78, scenario=default)
```

Reading the facts:
- **The publish has nothing to do.** With the estate at its converged shape, `Runs.up` returned
  **`NothingToApply`** — a "rebuild me" request adds no publishable delta. (The `tables=5` / `rows=78` are the
  proof estate: the four base tables plus the index's own one-statement definition file, holding `Status` 3 +
  `Customer` 25 + `Order` 25 + `OrderLine` 25 = 78 rows.)
- **The rebuild is an out-of-band command, not a deploy.** `ALTER INDEX ... REBUILD` was run directly against
  the database — the maintenance action itself.
- **It changed nothing declarative and nothing in the data.** After the rebuild the index is unchanged
  (`NONCLUSTERED`, still **1 key column**), `Customer` still holds **25 rows**, and the `Email` content digest is
  **byte-identical** (`795751655` → `795751655`). Only physical storage was touched (fragmentation was already
  `0` on this small table).
- **The deploy still has nothing to do.** A third `Runs.up` — *after* the physical rebuild — again returned
  **`NothingToApply`**. The rebuild left no trace the dacpac can see; it is entirely outside the declarative
  model.

## Verification — run in each environment after deployment

```sql
-- The schema deploy carries no delta for a rebuild: a publish issues no ALTER INDEX statement.
-- The operational trigger the maintenance job keys on is fragmentation, measured directly:
SELECT i.name, ps.avg_fragmentation_in_percent, ps.page_count
FROM sys.dm_db_index_physical_stats(DB_ID(), OBJECT_ID('dbo.Customer'), NULL, NULL, 'LIMITED') ps
JOIN sys.indexes i ON i.object_id = ps.object_id AND i.index_id = ps.index_id
WHERE i.name = 'IX_Customer_Email';
```

## Rollback

Nothing to roll back in the schema — the dacpac is unchanged. A rebuild or reorganize changes only physical
storage, not the index definition, so it leaves no schema state to reverse.

## Not verified

- **Production scale and timing.** The lock a `REBUILD` takes (online vs offline, Enterprise-gated) and how long
  it runs at production row counts are operational and not shown by any dacpac publish; on the seed-scale copy
  fragmentation was already `0`, so no reorganization was needed. The DBA / maintenance plan owns scheduling the
  window.
- **The maintenance itself.** Whether fragmentation has actually crossed the threshold that warrants a rebuild is
  measured per environment by the job, out of band from this PR.
- **The anti-pattern's harm at scale.** That a post-deploy `ALTER INDEX ... REBUILD` re-runs on *every* publish
  is argued from the deploy model, not timed here; the point stands regardless of duration — it is recurring,
  blocking, and unconditional.
