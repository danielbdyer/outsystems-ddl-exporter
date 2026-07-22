# OrderLine: rename the Entity to OrderItem — a header-edit rename without a refactorlog is a phantom; the real rename is EXEC sp_rename

**In OutSystems** — You rename the `OrderLine` Entity to `OrderItem`. In Service Studio the table keeps its data and the platform rewires every reference for you.
**In SSDT** — you change the table name in the `CREATE TABLE` header (`dbo.OrderLine` → `dbo.OrderItem`) in `Tables/dbo.OrderLine.sql`. A `schema.Table` name is an **address, not identity**: **without a refactorlog entry** SSDT has no way to know this is the *same* table under a new name, so it treats `dbo.OrderItem` as a brand-new table — and what happens next depends entirely on the deploy's drop policy.

## Summary

You rename `OrderLine` to `OrderItem`. This looks like a rename but carries the **same trap** as a schema move: the two-part table name is only an *address*, and SSDT only knows a rename happened if the **refactorlog** carries it. Edit the header alone and SSDT sees "an old table that's gone, a new table that appeared" — two different objects. Against the Twin — a disposable SQL Server database published from this estate and filled with real-shaped synthetic data — under a **production-faithful** publish (`BlockOnPossibleDataLoss = true`, `DropObjectsNotInSource = false`), the discovered outcome is worse than an error, because it looks clean: the publish returns **`Ok`** but performs a **phantom rename** — it creates `dbo.OrderItem` **empty** and leaves the populated `dbo.OrderLine` exactly where it was. **The rows do not follow, and the deploy reports success.** No work item was provided with the request; attach one before merge so the record is traceable.

**The correct rename is a scripted `EXEC sp_rename 'dbo.OrderLine', 'OrderItem'`** (or a refactorlog entry that makes SSDT emit it), which renames the *same object* — proven below to preserve the `object_id` and all 25 rows.

## Review & release

- A dev lead or an experienced developer must review this: a header-edit rename without a refactorlog **does not move the data**, and it does not fail loudly — it either strands the rows in the old table (the posture proven here) or, under a drop-enabled posture, drops the old table and loses them. Either way the intended rename did not happen.
- It ships as a **scripted change**: `EXEC sp_rename 'dbo.OrderLine', 'OrderItem'`, which renames the table intact (`object_id` and rows preserved) in one metadata operation the table definition cannot express — or as a header edit **paired with a refactorlog entry** so SSDT emits that `sp_rename` for you. The bare header edit, reviewed here, is **not** a valid way to ship the rename.
- Every reference to the old name must follow the rename: foreign keys, views, stored procedures, synonyms, reports, ETL, and application code that name `dbo.OrderLine` break when the table becomes `dbo.OrderItem`. That reference list is the main release risk — proven directly below (the old name stops resolving, and the table's own foreign key keeps its stale old name).
- Added scrutiny: first time this operation is proven on the Twin; a first entity rename on this estate carries added scrutiny — schedule a window and read the delta first.

## Changes

| File | Change |
|---|---|
| `Tables/dbo.OrderLine.sql` | Re-heads the table from `[dbo].[OrderLine]` to `[dbo].[OrderItem]` (its constraints carried to matching new names, `PK_OrderItem` / `FK_OrderItem_Order`) |
| *(required, not yet present)* `<project>.refactorlog` | Must carry the rename so SSDT emits `sp_rename` instead of creating a new table and stranding the old one — **or** ship the rename as an authored `EXEC sp_rename` script instead of the bare header edit |

The header edit alone is what makes this a phantom rename. It must be paired with the refactorlog entry or replaced by the scripted `sp_rename`.

## Data remediation

The data does not move on its own. On the disposable copy the corrective, **lossless** rename was proven directly:

```sql
-- the real rename: same object, new name. object_id and rows are preserved.
EXEC sp_rename 'dbo.OrderLine', 'OrderItem';
```

If a header-edit "rename" has already been published (leaving an empty `dbo.OrderItem` and the populated `dbo.OrderLine`), the remedy is to drop the empty phantom and run the rename:

```sql
DROP TABLE [dbo].[OrderItem];             -- the empty table the phantom rename created
EXEC sp_rename 'dbo.OrderLine', 'OrderItem';   -- rename the real, populated one
```

Under a **drop-enabled** deploy posture (`DropObjectsNotInSource = true`, data loss unblocked) the same header edit would instead *drop* the populated `dbo.OrderLine` and lose its rows outright — in that case the rows come back only from a backup. That drop-enabled path was not exercised here (named under Not verified); the phantom-rename path below is what this Twin proved.

## Deployment evidence — objective proof, production-faithful publish, 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, materializes real-shaped data, applies the header-edit rename under a **production-faithful** DacFx posture (`BlockOnPossibleDataLoss = true`, `DropObjectsNotInSource = false`, no smart-defaults), and then proves the corrective `EXEC sp_rename` directly. DacFx is the same publish engine `sqlpackage` wraps.

**Test:** `Twin.Tests.Integration.SamplePrRenameTests+SamplePrRenameTests.rename-entity: a bare table rename (no refactorlog) is a phantom (new empty table, old survives); EXEC sp_rename preserves object_id and all rows`

```
Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2, Duration: 49 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact 1 (the trap) — the publish reports `Ok`, but the rows do not move.** `dbo.OrderLine` held **25 rows**. The header-edit rename (no refactorlog) applied under the production-faithful posture, and the result is a phantom: a new **empty** `dbo.OrderItem` and the original `dbo.OrderLine` left in place with all its rows. Verbatim from the run:

```
baseline: dbo.OrderLine exists=1, rows=25, object_id=965578478; dbo.OrderItem exists=0
production publish (BlockOnPossibleDataLoss=true, DropObjectsNotInSource=false) bare rename dbo.OrderLine -> dbo.OrderItem, NO refactorlog: APPLIED (Ok)
  DISCOVERED phantom rename: dbo.OrderItem created=1 with rows=0 (EMPTY - the rows did NOT follow)
  the source table was NOT dropped: dbo.OrderLine still exists=1 with rows=25, object_id=965578478 (unchanged=true) - the populated original is stranded
```

Reading the facts: the publish returned **`Ok`** — no error, no warning. It created a `dbo.OrderItem` table, but that table has **0 rows**. Because `DropObjectsNotInSource = false` (the production posture protects objects the source no longer names), `dbo.OrderLine` was **not dropped** — it still holds all **25 rows**, with its `object_id` (`965578478`) unchanged. You now have two tables: the real one, still at the old name, and an empty one at the new name. A green deploy that quietly did not do what was asked.

**Fact 2 (the real rename) — `sp_rename` renames the same object intact.** On the same copy, dropping the empty phantom and renaming the populated original preserves the `object_id` and every row. Verbatim:

```
the CORRECT rename (EXEC sp_rename 'dbo.OrderLine', 'OrderItem'): dbo.OrderLine exists=0 (gone), dbo.OrderItem exists=1 rows=25 object_id=965578478 (OrderLine's original was 965578478 -> preserved=true)
```

`dbo.OrderLine` is now gone, `dbo.OrderItem` holds the **25 rows**, and its `object_id` is **`965578478`** — the *same* object the table always was, now under a new name. Identical `object_id` before and after is the proof this was a **rename**, not a drop-and-recreate.

**Fact 3 (the reference fallout) — the old name breaks, and the constraints do not follow.** A rename is metadata-cheap but every reference to the old name is now stale. Proven directly:

```
  reference fallout: querying the old name returned: Msg 208, Line 1: Invalid object name 'dbo.OrderLine'.
  reference fallout: the renamed table's FK still carries its stale old name='FK_OrderLine_Order' (a table rename does not rename the constraints that reference it)
```

Any view, procedure, report, or application query that still names `dbo.OrderLine` now fails with `Msg 208 — Invalid object name`. And the table's own foreign key kept its old name, `FK_OrderLine_Order`, even though the table is now `OrderItem` — a table rename does not rename the constraints, indexes, or triggers attached to it, so their names drift from the entity. (`OrderLine` is a leaf — nothing has a foreign key *pointing at* it — so no child relationship broke here; had it been a parent, every inbound foreign key would also need repointing.)

## Verification — run in each environment after deployment

```sql
-- expect exactly one row, named OrderItem, with the full row count.
-- Two rows (OrderLine + OrderItem) means the phantom rename happened; an OrderItem row with 0 rows means the data was left behind.
SELECT t.name, SUM(p.rows) AS row_count, t.object_id
FROM sys.tables t
JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
WHERE t.name IN ('OrderLine', 'OrderItem')
GROUP BY t.name, t.object_id;
```

On a disposable copy, capture `object_id` and the row count **before** and **after**: both must match for the same (now `OrderItem`) table. A changed `object_id`, an empty `OrderItem`, or a surviving `OrderLine` all mean the rename did not really happen. Before each promotion the generated delta must read as `EXEC sp_rename`, not `DROP TABLE` + `CREATE TABLE`.

## Rollback

The rename reverses losslessly as a metadata operation: `EXEC sp_rename 'dbo.OrderItem', 'OrderLine'` renames the table back with its rows and `object_id`, and every reference must be repointed back to `OrderLine`. The reference edits are **not** auto-reversed. A rename that ever went through as the drop-enabled data-loss variant has already lost the rows and cannot be rolled back from the schema alone — restore from a backup. Only the forward rename (phantom + corrective `sp_rename`) was exercised here.

## Not verified

- **Application impact.** Every reference to the old name — foreign keys pointing at it, views, procedures, synonyms, reports, ETL, and application code — breaks when the table becomes `dbo.OrderItem`; the old name stops resolving (`Msg 208`, proven above). That all such references were found and repointed is not confirmed here. The application owner owns closing this before promotion.
- **Constraint / index / trigger names.** The rename leaves the table's own objects under their old names (`FK_OrderLine_Order`, `PK_OrderLine`, proven above for the FK). Renaming them to match is cosmetic but recommended; it was not done here.
- **The drop-enabled data-loss variant.** Under a deploy posture with `DropObjectsNotInSource = true` (and data loss unblocked), the same header edit would drop `dbo.OrderLine` and lose its rows rather than strand them. That path was not run here — only the production-faithful phantom-rename outcome was — but it is the documented worse case; confirm your target's drop policy.
- **The refactorlog path.** Shipping the rename as a header edit **plus** a refactorlog entry (so SSDT emits the `sp_rename` itself) is the declarative alternative to the scripted `sp_rename`. This proof used the scripted rename directly; that a refactorlog entry is present and correct in each environment is not confirmed here — confirm it before each promotion.
- **Other environments.** Test, UAT, and Prod may hold `OrderLine` at different volumes or already carry an `OrderItem`; the disposable copy cannot see them. Run the verification query before promotion.
- **Production scale and timing.** `sp_rename` is a metadata operation, but any dependent rebuild is exercised at seed scale only; blocking and duration at production row counts are not shown here.
