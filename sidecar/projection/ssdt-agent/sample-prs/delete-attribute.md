# OrderLine: delete the Sku attribute (a populated column is blocked by the data-loss guard; the scripted DROP COLUMN is the irreversible step)

**In OutSystems** — You delete the `OrderLine.Sku` Attribute — "remove the field, we don't use it".
**In SSDT** — you remove the `[Sku]` column from `Tables/dbo.OrderLine.sql`. SSDT emits `ALTER TABLE [dbo].[OrderLine] DROP COLUMN [Sku]`. On a column that holds data, `BlockOnPossibleDataLoss = true` refuses the publish; the values are irrecoverable without a backup. Edit the `CREATE`; never write the `ALTER` by hand.

## Summary

You delete the `Sku` column from `OrderLine`. Unlike dropping an index or a foreign key (which take no rows with them and publish clean — see `drop-index.md`, `drop-fk.md`), **a column carries data**, and this one is populated on every row. This was proven objectively against a Twin — a disposable SQL Server database published from this estate and filled with real-shaped synthetic data — under a **production-faithful** publish (`BlockOnPossibleDataLoss = true`, `DropObjectsNotInSource = false`, no smart-defaults), and the discovered outcome is a **block**, not a phantom.

A column is not an "object not in source" in the sense the `DropObjectsNotInSource` switch protects — that switch guards whole tables, not columns inside a table that is still present. So the column drop **proceeds** as an `ALTER TABLE DROP COLUMN`, and `BlockOnPossibleDataLoss = true` then **refuses it** with the same row-presence guard that governs a NOT-NULL tightening (`make-mandatory.md`): the deploy terminates because the table holds rows whose values would be lost. The block is the safety net working. The refusal **rolls back transactionally** — the column and every value are left exactly as they were.

Because the values cannot be recovered once dropped, the destructive step — a scripted `ALTER TABLE ... DROP COLUMN` — is what review and the deprecation plan govern. **How simply it ships and how dangerous it is are two different things:** a one-line `DROP COLUMN` can still be the most dangerous change in front of you, because once the values are gone they are gone. No work item was provided with the request; attach one before merge so the record is traceable.

## Review & release

- A **principal** must review this: data is removed and the removal cannot be undone. (An empty, provably-unused column loses no data, but a drop is structurally irreversible — a dev lead reviews at minimum.)
- It does **not** ship as a single in-place edit while the column holds data — the production publish is blocked (see the evidence). It ships across releases as the **4-phase deprecation** (soft-deprecate → stop writes → verify nothing reads it → drop) so the running application keeps working while the change is in flight. On an empty, provably-unused column with no dependents, the same edit ships as one clean declarative change.
- Added scrutiny, when the table is large: at production row counts the drop may block writes or run long — schedule a window.

## Changes

| File | Change |
|---|---|
| `Tables/dbo.OrderLine.sql` | Removes the `[Sku] NVARCHAR(64) NOT NULL` column from the table definition |

No rename (the refactorlog is unchanged). No index, view, or procedure changes in this estate. Only `[Sku]` is removed — every other column, the primary key, and the `FK_OrderLine_Order` reference are untouched.

## Data remediation

Before the final drop phase, prove the column is truly dead: `sys.dm_sql_referencing_entities` must return nothing for `Sku` — no view, procedure, computed column, or index still referencing it — and the application must have genuinely stopped writing it (a `sys.dm_sql_referencing_entities` check sees SQL objects, not application code). Only then does the drop ship, and only as a deliberate, principal-reviewed act. There is **no backfill** that makes a populated drop safe — the row-presence guard fires regardless of the values; the safe path is the staged deprecation, not a gate relaxation over live data.

## Deployment evidence — objective proof, production-faithful publish, 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, materializes real-shaped data with `Sku` populated on every row, attempts the drop under the **production-faithful** posture, and then proves the scripted `DROP COLUMN` is the irreversible destructive step. DacFx is the same publish engine `sqlpackage` wraps.

**Test:** `Twin.Tests.Integration.SamplePrRemovalTests+SamplePrRemovalTests.delete-attribute: removing a populated column is BLOCKED by the data-loss guard; the scripted DROP COLUMN is the irreversible step`

```
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 1 m 19 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact 1 (primary) — the populated column drop is REFUSED, and the block rolls back.** `OrderLine` held 25 rows with `Sku` populated on all 25 (zero NULLs). The production-faithful publish of the column removal was **refused**; the column survived (`exists after=1`) and all 25 rows were left intact. Verbatim from the run — the DacFx warning that named the column, and the row-presence guard that terminated the deploy:

```
baseline: OrderLine rows=25, [Sku] column exists=1, rows with Sku NOT NULL=25 (fully populated)
production publish (BlockOnPossibleDataLoss=true, DropObjectsNotInSource=false) remove [Sku] from source: REFUSED: Could not deploy package.
Warning SQL72015: The column [dbo].[OrderLine].[Sku] is being dropped, data loss could occur.
Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 50000, Level 16, State 127, Line 6 Rows were detected. The schema update is terminating because data loss might occur.
Error SQL72045: Script execution error.  The executed script:
IF EXISTS (SELECT TOP 1 1
           FROM   [dbo].[OrderLine])
    RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127)
        WITH NOWAIT;
```

```
  DISCOVERED: [Sku] column exists after=1 (1 = survived the block; 0 = dropped), OrderLine rows=25 (was 25)
```

Reading the facts: `SQL72015` names exactly what would be lost — `[dbo].[OrderLine].[Sku]` is being dropped — and the guard (`IF EXISTS (SELECT TOP 1 1 FROM [dbo].[OrderLine]) RAISERROR(...)`) sits above the `DROP COLUMN` and fires on **row presence**, not on the column's content. After the refusal the column still exists (`exists after=1`) and all 25 rows remain — the block is transactional.

**Fact 2 — the scripted `DROP COLUMN` is the irreversible destructive step SSDT refused.** Run by hand, it removes the column and its values; the rows themselves remain, but the `Sku` values are gone for good. Verbatim:

```
scripted ALTER TABLE [dbo].[OrderLine] DROP COLUMN [Sku]: [Sku] exists after=0 (gone), OrderLine rows=25 (rows remain; the Sku values are irrecoverably lost)
```

The 25 `OrderLine` rows are still there — this is not a table drop — but the `Sku` value each carried is unrecoverable without a backup. That irreversibility, not the one-line simplicity, is why a principal signs it off.

## Verification — run in each environment after deployment

```sql
-- BEFORE the final drop: expect 0 rows — nothing in the database still references the column.
SELECT referencing_schema_name, referencing_entity_name
FROM sys.dm_sql_referencing_entities('dbo.OrderLine', 'OBJECT')
WHERE referenced_minor_id = (SELECT column_id FROM sys.columns WHERE object_id = OBJECT_ID('dbo.OrderLine') AND name = 'Sku');

-- AFTER deployment: expect 0 rows — the column no longer exists on the table.
SELECT c.name FROM sys.columns c
WHERE c.object_id = OBJECT_ID('dbo.OrderLine') AND c.name = 'Sku';
```

## Rollback

Re-adding the column definition restores the *structure* (`ALTER TABLE dbo.OrderLine ADD Sku NVARCHAR(64) NULL;`), but a populated column's **values are gone once dropped** and are recoverable only from a backup taken before the drop. An empty, provably-unused column re-adds losslessly. The drop is not auto-reversed, and only the forward drop was exercised here.

## Not verified

- **Application impact.** Whether application code outside the database still writes or reads `Sku`. `sys.dm_sql_referencing_entities` sees SQL objects, not application code; @app-owner confirms the app has stopped.
- **Other environments.** Test, UAT, and Prod may still have live readers where Dev does not. Run the referencing check and the verification query before each promotion.
- **Reversibility.** Only the forward drop is exercised on the disposable copy; the dropped values are not recoverable from the schema change, and the pre-drop backup is the sole restore path.
- **Production scale / timing.** At large row counts the drop may block writes or run long; the small disposable copy cannot show this.
