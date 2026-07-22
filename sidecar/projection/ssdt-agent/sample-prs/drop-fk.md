# OrderLine: drop the reference to Order (no rows are lost; what you remove is the guarantee against orphans)

**In OutSystems** — You remove the reference from `OrderLine` to `Order` — "we don't need the link anymore", "unhook these entities".
**In SSDT** — you delete the `CONSTRAINT [FK_OrderLine_Order] FOREIGN KEY ...` line from `Tables/dbo.OrderLine.sql`. SSDT emits `ALTER TABLE ... DROP CONSTRAINT [FK_OrderLine_Order]`. No row is touched; the table simply stops validating the reference.

## Summary

You drop the foreign key from `OrderLine` to `Order`. Dropping a constraint **never loses data** — it reads and writes no rows — so this publishes clean every time. This was proven objectively against a Twin — a disposable SQL Server database published from this estate and filled with real-shaped synthetic data — under a **production-faithful** publish (`BlockOnPossibleDataLoss = true`, `DropObjectsNotInSource = false`, no smart-defaults).

The removal really does take effect declaratively. As with dropping an index (`drop-index.md`), a **foreign key is a sub-object DacFx governs with its own option** — `DropConstraintsNotInSource`, which defaults to **true** independently of the off `DropObjectsNotInSource` master switch. So the constraint is dropped by the declarative removal, proven below — unlike deleting a whole *Entity*, which that off switch turns into a phantom (`delete-entity.md`). Dropping a constraint is safe to let through because it touches no rows.

The real risk is not data loss — it is what the reference was **guaranteeing.** With the foreign key gone, nothing stops an `OrderLine` from being written that points at an `Order` that does not exist. The Twin proved this directly: an orphan child that was **blocked** (SQL error 547) while the reference stood was **accepted** once it was dropped. A trusted foreign key is also a hint the query optimizer uses, so some query plans may change, occasionally for the worse. Neither consequence shows up in the publish, which is why this is worth a second set of eyes. No work item was provided with the request; attach one before merge so the record is traceable.

## Review & release

- A dev lead or an experienced developer should review this: dropping the constraint **weakens referential integrity** (an orphan can now be written) and can **shift query plans**; no data is touched.
- It ships as a single schema change, applied in place — a single `ALTER TABLE [dbo].[OrderLine] DROP CONSTRAINT [FK_OrderLine_Order]`. No data is read or written, and the publish never blocks.
- One thing to confirm: is this a **permanent** removal, or a **temporary** step to unblock another change (a type change, a table drop)? If it is temporary, it belongs inside that larger migration rather than shipping on its own — and re-adding the constraint later re-runs orphan validation, which can be blocked by any orphan written while it was absent.

## Changes

| File | Change |
|---|---|
| `Tables/dbo.OrderLine.sql` | Removes the `CONSTRAINT [FK_OrderLine_Order] FOREIGN KEY ([OrderId]) REFERENCES [dbo].[Order] ([Id])` line from the table definition |

No column changes: `OrderLine`'s columns, its primary key, and the `OrderId` column itself are all untouched — only the foreign-key constraint that validated `OrderId` is removed.

## Deployment evidence — objective proof, production-faithful publish, 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, materializes real-shaped data with the foreign key present, removes it under the **production-faithful** posture, and then proves both the losslessness and the integrity consequence directly. DacFx is the same publish engine `sqlpackage` wraps.

**Test:** `Twin.Tests.Integration.SamplePrRemovalTests+SamplePrRemovalTests.drop-fk: removing a foreign key publishes clean and loses no rows, but an orphan can now be written`

```
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 1 m 19 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact 1 — the declarative removal drops the FK, keeps every row, and opens the door to orphans.** `OrderLine` held 25 rows. With the reference present, an attempt to insert an `OrderLine` pointing at a non-existent `Order` was **blocked** (SQL error `547`). Removing the reference and publishing returned **`Ok`**: the FK is gone (`sys.foreign_keys`), all 25 rows intact — and the identical orphan insert now **succeeds** (error number `0`). Verbatim from the run:

```
baseline: OrderLine rows=25, FK_OrderLine_Order exists=1; inserting an orphan child returned SQL error number=547 (547 = FK conflict = BLOCKED)
production publish (BlockOnPossibleDataLoss=true, DropObjectsNotInSource=false) remove FK_OrderLine_Order from source: APPLIED (Ok)
  DISCOVERED: FK exists after=0 (0 = DROPPED declaratively by the granular DropConstraintsNotInSource default; 1 = phantom survival), OrderLine rows=25 (was 25, intact)
  the real consequence: inserting an orphan child now returns SQL error number=0 (0 = ALLOWED — nothing prevents an OrderLine pointing at a missing Order; 547 = still blocked)
```

Reading the facts: `FK exists after=0` — the constraint was **actually dropped**, even under `DropObjectsNotInSource = false`, because `DropConstraintsNotInSource` (default **true**) governs it. `OrderLine rows=25` — no row lost. The `547 → 0` shift on the orphan probe is the point of the whole change: **the data survived, the guarantee did not.** (The orphan insert was run inside a rolled-back transaction, so the table is left exactly as it was.)

**Fact 2 — the scripted `ALTER TABLE ... DROP CONSTRAINT` is the identical lossless form.** Re-adding the FK and dropping it by hand behaves the same: constraint gone via `sys.foreign_keys`, rows intact. Verbatim:

```
scripted ALTER TABLE [dbo].[OrderLine] DROP CONSTRAINT [FK_OrderLine_Order]: FK exists before=1 -> after=0 (gone via sys.foreign_keys), OrderLine rows=25 (intact)
```

## Verification — run in each environment after deployment

```sql
-- expect 0 rows: the foreign key no longer exists
SELECT name FROM sys.foreign_keys WHERE name = 'FK_OrderLine_Order';

-- integrity is now the application's responsibility: this returns any OrderLine
-- rows that point at a missing Order — expect 0, but nothing in the database
-- prevents them from appearing once the constraint is gone.
SELECT ol.Id FROM dbo.OrderLine ol
LEFT JOIN dbo.[Order] o ON o.Id = ol.OrderId
WHERE o.Id IS NULL;
```

## Rollback

Re-create the constraint: `ALTER TABLE [dbo].[OrderLine] ADD CONSTRAINT [FK_OrderLine_Order] FOREIGN KEY ([OrderId]) REFERENCES [dbo].[Order] ([Id]);`. This **re-runs orphan validation** — it lands clean only if no orphan rows were written while the constraint was absent; otherwise it is blocked until those rows are reconciled (see `create-fk-orphan`). The original drop loses no data, so nothing else needs restoring. Only the forward drop was exercised here.

## Not verified

- **Query-plan impact.** Dropping a trusted foreign key removes a hint the optimizer used; a plan change or regression will not show on a disposable copy, whose statistics and data volume differ from production. Whoever owns query performance confirms this.
- **Application impact.** Nothing enforces the reference after the drop; whether application code relies on the database rejecting an `OrderLine` that points at a missing `Order` is not confirmed here (@app-owner).
- **Other environments.** The drop was proven on a disposable copy of Dev only; whether Test, UAT, and Prod already carry orphan rows (which would block a rollback) is not shown here. Run the verification query before promotion.
