# OrderLine: delete the Entity (removing it from the model does NOT drop the table — it is a phantom removal; the scripted DROP TABLE is the real, destructive one)

**In OutSystems** — You delete the `OrderLine` Entity — "drop the table, we don't need it".
**In SSDT** — you remove `Tables/dbo.OrderLine.sql` from the estate. What happens on deploy depends entirely on the drop policy. Under the **production default** (`DropObjectsNotInSource = false`) the table is **not** dropped by its mere absence; a real removal needs an explicit, scripted `DROP TABLE`.

## Summary

You delete the whole `OrderLine` Entity. This carries the **same trap as the schema move** (`move-schema.md`): removing an object from the model looks like it removes the object, but under a production-faithful publish it may do nothing at all. This was proven objectively against a Twin — a disposable SQL Server database published from this estate and filled with real-shaped synthetic data — under a **production-faithful** publish (`BlockOnPossibleDataLoss = true`, `DropObjectsNotInSource = false`, no smart-defaults). The discovered outcome is worse than an error because it looks clean: the publish returns **`Ok`** and performs a **phantom removal** — `dbo.OrderLine` and all **25 rows** are left exactly where they were, with the same `object_id`. **Deleting the file did not delete the table, and the deploy reported success.**

This is the point of `DropObjectsNotInSource = false`, the default `sqlpackage` ships: a deploy never drops a whole object a developer merely stopped mentioning. (It is the *opposite* of dropping an index or a foreign key, which DacFx governs with separate default-on options and which really are removed declaratively — see `drop-index.md`, `drop-fk.md`. A whole *table* is a top-level object, so the off master switch protects it.) To actually remove the table you must **script the drop explicitly** — and that scripted `DROP TABLE` is the destructive, irreversible act that review governs, because it takes the 25 rows with it.

**The size of the change tells you nothing about its risk.** One `DROP TABLE` in one release can be the gravest thing you ship: once it lands the data is gone for good and there is no undo. No work item was provided with the request; attach one before merge so the record is traceable.

## Review & release

- A **principal** must review this: data is removed and the removal cannot be undone. If the table is provably scratch (no business data ever), the review need is lower.
- It ships as a **scripted change** in a single release — in production the drop is an explicit pre-deployment `DROP TABLE`, **not** the mere absence of the `.sql` file (which, as proven below, does nothing under the production posture), sequenced after any inbound foreign keys are dropped. `OrderLine` is a leaf here — nothing references it — so no inbound-FK ordering is required; a table with inbound references would need those dropped first.
- Added scrutiny: first time a whole-Entity drop is proven on this estate; at >1M rows the drop may block writes or run long — schedule a window.

## Changes

| File | Change |
|---|---|
| `Tables/dbo.OrderLine.sql` | **Removed** from the estate |
| *(required, not the file deletion)* a scripted `DROP TABLE [dbo].[OrderLine]` | The file deletion alone does **not** drop the table under `DropObjectsNotInSource = false`; the real removal is an explicit, reviewed `DROP TABLE` (sequenced after any inbound FKs) |

Deleting the file is what makes this a phantom. It must be replaced by (or paired with) the scripted `DROP TABLE` to actually remove the table.

## Data remediation

The table does not drop on its own. On the disposable copy the destructive removal was proven directly:

```sql
-- the real removal: an explicit, scripted drop. The rows go with it.
DROP TABLE [dbo].[OrderLine];
```

If a whole-Entity "delete" has already been published (leaving `dbo.OrderLine` and its rows exactly in place, with a green deploy), nothing was lost — but nothing was removed either; the phantom is corrected by the scripted `DROP TABLE` above, once the data is confirmed safe to lose. Under a **drop-enabled** posture (`DropObjectsNotInSource = true`) the same file deletion would instead drop the populated table on deploy — recoverable only from a backup taken beforehand. That path was not exercised here (named under Not verified); the production-posture phantom is what this Twin proved.

## Deployment evidence — objective proof, production-faithful publish, 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, materializes real-shaped data, removes the whole `OrderLine` table from source under the **production-faithful** posture (`BlockOnPossibleDataLoss = true`, `DropObjectsNotInSource = false`, no smart-defaults), and then proves the scripted `DROP TABLE` is the destructive step. DacFx is the same publish engine `sqlpackage` wraps.

**Test:** `Twin.Tests.Integration.SamplePrRemovalTests+SamplePrRemovalTests.delete-entity: removing a whole table from source is a phantom removal under DropObjectsNotInSource=false; the scripted DROP TABLE is the real, destructive one`

```
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 1 m 19 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact 1 (the trap) — the publish reports `Ok`, but the table is not removed.** `dbo.OrderLine` held **25 rows**. The whole-table removal (delete the `.sql`) applied under the production posture, and the result is a phantom: the table and every row survive, `object_id` unchanged. Verbatim from the run:

```
baseline: dbo.OrderLine exists=1, rows=25, object_id=965578478
production publish (BlockOnPossibleDataLoss=true, DropObjectsNotInSource=false) remove whole dbo.OrderLine from source: APPLIED (Ok)
  DISCOVERED phantom removal: dbo.OrderLine exists after=1 (1 = SURVIVED), rows=25 (was 25), object_id=965578478 (unchanged=true) - the table was NOT dropped
```

Reading the facts: the publish returned **`Ok`** — no error, no warning. But `dbo.OrderLine exists after=1`, `rows=25`, and `object_id=965578478` is unchanged. Because `DropObjectsNotInSource = false` protects objects the source no longer names, the table was **not dropped**. A green deploy that quietly did not do what was asked.

**Fact 2 (the real removal) — the scripted `DROP TABLE` is the destructive step.** On the same copy, dropping the table by hand removes it and its rows. Verbatim:

```
scripted DROP TABLE [dbo].[OrderLine] (rows just before=25): table exists after=0 (gone), object_id=-1 (-1 = no such object) - the rows are gone with it (irreversible without a backup)
```

`dbo.OrderLine` is now gone (`object_id=-1`, no such object), and the 25 rows are gone with it — irreversible without a backup. That is the destructive act a principal reviews; the file deletion alone was not it.

## Verification — run in each environment after deployment

```sql
-- BEFORE the drop: expect 0 rows — nothing still points at the table.
SELECT referencing_schema_name, referencing_entity_name
FROM sys.dm_sql_referencing_entities('[dbo].[OrderLine]', 'OBJECT');

-- AFTER the scripted drop: expect NULL — the table no longer exists. A non-NULL
-- result after a file-deletion "delete" means the phantom happened: the table
-- is still there and nothing was removed.
SELECT OBJECT_ID('[dbo].[OrderLine]', 'U') AS table_object_id;
```

## Rollback

A dropped table is **not** losslessly reversible. The table definition is recreated from source control (the `.sql` / `CREATE TABLE`), but the rows are gone — the scripted `DROP TABLE` is the irreversible act. Recovering the data depends on a backup taken before the drop; that backup is not part of this change and must be arranged deliberately. The phantom path (file deletion under the production posture) is trivially "reversible" only because it removed nothing. Only the forward removal was exercised here.

## Not verified

- **Application impact.** Any query, view, procedure, report, export, or job that reads `OrderLine` fails once it is actually dropped. Whether anything in the running application still references it is not confirmed here (@app-owner); `sys.dm_sql_referencing_entities` finds in-database references only, not application code or external consumers.
- **The drop-enabled variant.** Under `DropObjectsNotInSource = true` the same file deletion would drop the populated table on deploy rather than phantom it. That path was not run here — only the production-posture phantom — but it is the documented worse case; confirm your target's drop policy.
- **Other environments.** Test, UAT, and Prod may hold rows or references this copy cannot see. Run the pre-drop checks before promotion.
- **Production scale and timing.** At >1M rows the drop may block writes or run long; the small copy does not show duration or blocking at scale.
- **Reversibility.** Only the forward drop is proven, and it is irreversible for the data: no rollback restores the rows without a separate backup taken beforehand.
