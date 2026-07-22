# OrderLine: move to the sales schema — a header-edit "move" without a refactorlog is a phantom move; use ALTER SCHEMA TRANSFER

**In OutSystems** — You move the `OrderLine` Entity to a different module / namespace, which lands it under a different database schema (`dbo.OrderLine` → `sales.OrderLine`).
**In SSDT** — you change the schema in the `CREATE TABLE` header. **Without a refactorlog entry** SSDT has no way to know this is the *same* table under a new address, so it treats `sales.OrderLine` as a brand-new table — and what happens next depends entirely on the deploy's drop policy.

## Summary

You move `OrderLine` to the `sales` schema. This looks like a rename and carries the **same trap** as a rename:
the two-part `schema.Table` name is only an *address*, not the table's identity, and SSDT only knows a move
happened if the **refactorlog** carries it. Edit the header alone and SSDT sees "an old table that's gone, a new
table that appeared" — two different objects. Against the Twin, under a **production-faithful** publish
(`BlockOnPossibleDataLoss = true`, `DropObjectsNotInSource = false`), the discovered outcome is worse than an
error, because it looks clean: the publish returns **`Ok`** but performs a **phantom move** — it creates
`sales.OrderLine` **empty** and leaves the populated `dbo.OrderLine` exactly where it was. **The rows do not
follow, and the deploy reports success.** This was proven objectively against a Twin — a disposable SQL Server
database published from this estate and filled with real-shaped synthetic data. No work item was provided with
the request; attach one before merge so the record is traceable.

**The correct move is a scripted `ALTER SCHEMA TRANSFER`** (or a refactorlog entry that makes SSDT emit it),
which moves the *same object* — proven below to preserve the `object_id` and all 25 rows.

## Review & release

- A dev lead or an experienced developer must review this: a header-edit move without a refactorlog **does not
  move the data**, and it does not fail loudly — it either strands the rows in the old table (the posture proven
  here) or, under a drop-enabled posture, drops the old table and loses them. Either way the intended move did
  not happen.
- It ships as a **scripted change**: `ALTER SCHEMA [sales] TRANSFER [dbo].[OrderLine]`, which moves the table
  intact (`object_id` and rows preserved) in one metadata operation the table definition cannot express — or as
  a header edit **paired with a refactorlog entry** so SSDT emits that transfer for you. The bare header edit,
  reviewed here, is **not** a valid way to ship the move.
- Every fully-qualified `schema.Table` reference must follow the move: views, stored procedures, synonyms, and
  application code that name `dbo.OrderLine` break when the table becomes `sales.OrderLine`. That reference list
  is the main release risk.
- Added scrutiny: first time this operation is proven on the Twin; a first move on this estate carries added
  scrutiny — schedule a window.

## Changes

| File | Change |
|---|---|
| `Tables/dbo.__sales-schema.sql` | **New** — `CREATE SCHEMA [sales];` (its own one-statement file) |
| `Tables/dbo.OrderLine.sql` | Re-heads the table from `[dbo].[OrderLine]` to `[sales].[OrderLine]` |
| *(required, not yet present)* `<project>.refactorlog` | Must carry the move so SSDT transfers the table instead of dropping/recreating it — **or** ship the move as an authored `ALTER SCHEMA TRANSFER` script instead of the bare header edit |

The header edit alone is what makes this a phantom move. It must be paired with the refactorlog entry or replaced
by the scripted transfer.

## Data remediation

The data does not move on its own. On the disposable copy the corrective, **lossless** move was proven directly:

```sql
-- the real move: same object, new schema. object_id and rows are preserved.
ALTER SCHEMA [sales] TRANSFER [dbo].[OrderLine];
```

If a header-edit "move" has already been published (leaving an empty `sales.OrderLine` and the populated
`dbo.OrderLine`), the remedy is to drop the empty phantom and run the transfer:

```sql
DROP TABLE [sales].[OrderLine];                       -- the empty table the phantom move created
ALTER SCHEMA [sales] TRANSFER [dbo].[OrderLine];      -- move the real, populated one
```

Under a **drop-enabled** deploy posture the same header edit would instead *drop* the populated `dbo.OrderLine`
and lose its rows outright — in that case the rows come back only from a backup. That drop-enabled path was not
exercised here (named under Not verified); the phantom-move path below is what this Twin proved.

## Deployment evidence — objective proof, production-faithful publish, 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, materializes real-shaped data,
applies the header-edit "move" under a **production-faithful** DacFx posture
(`BlockOnPossibleDataLoss = true`, `DropObjectsNotInSource = false`, no smart-defaults), and then proves the
corrective `ALTER SCHEMA TRANSFER` directly. DacFx is the same publish engine `sqlpackage` wraps.

**Test:** `Twin.Tests.Integration.SamplePrSchemaChangeTests+SamplePrSchemaChangeTests.move-schema: a header-edit move without a refactorlog is a phantom move; ALTER SCHEMA TRANSFER is the real one`

```
Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4, Duration: 1 m 22 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact 1 (the trap) — the publish reports `Ok`, but the rows do not move.** `dbo.OrderLine` held **25 rows**.
The header-edit move (no refactorlog) applied under the production-faithful posture, and the result is a phantom:
a new **empty** `sales.OrderLine` and the original `dbo.OrderLine` left in place with all its rows. Verbatim from
the run:

```
baseline: dbo.OrderLine exists=1, rows=25, object_id=965578478; sales schema exists=0, sales.OrderLine exists=0
production publish (BlockOnPossibleDataLoss=true, DropObjectsNotInSource=false) header-edit move dbo.OrderLine -> sales.OrderLine, NO refactorlog: APPLIED (Ok)
  DISCOVERED phantom move: sales schema created=1, sales.OrderLine exists=1 with rows=0 (EMPTY - rows did NOT follow)
  the source table was NOT dropped: dbo.OrderLine still exists=1 with rows=25, object_id=965578478 (unchanged=true) - the populated original is stranded
```

Reading the facts: the publish returned **`Ok`** — no error, no warning. It created the `sales` schema and a
`sales.OrderLine` table, but that table has **0 rows**. Because `DropObjectsNotInSource = false` (the production
posture protects objects the source no longer names), `dbo.OrderLine` was **not dropped** — it still holds all
**25 rows**, with its `object_id` (`965578478`) unchanged. You now have two tables: the real one, still at the
old address, and an empty one at the new address. A green deploy that quietly did not do what was asked.

**Fact 2 (the real move) — `ALTER SCHEMA TRANSFER` moves the same object intact.** On the same copy, dropping the
empty phantom and transferring the populated original preserves the `object_id` and every row. Verbatim:

```
the REAL move (ALTER SCHEMA [sales] TRANSFER [dbo].[OrderLine]): dbo.OrderLine exists=0 (gone), sales.OrderLine exists=1 rows=25 object_id=965578478 (dbo's original was 965578478 -> preserved=true)
```

`dbo.OrderLine` is now gone, `sales.OrderLine` holds the **25 rows**, and its `object_id` is **`965578478`** —
the *same* object the table always was, now under a new schema. Identical `object_id` before and after is the
proof this was a **move**, not a drop-and-recreate.

## Verification — run in each environment after deployment

```sql
-- expect exactly one row, under the target schema only, with the full row count.
-- Two rows (dbo + sales) means the phantom move happened; a sales row with 0 rows means the data was left behind.
SELECT SCHEMA_NAME(t.schema_id) AS schema_name, t.name, SUM(p.rows) AS row_count, t.object_id
FROM sys.tables t
JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
WHERE t.name = 'OrderLine'
GROUP BY SCHEMA_NAME(t.schema_id), t.name, t.object_id;
```

On a disposable copy, capture `object_id` and the row count **before** and **after**: both must match for the
same (now `sales`) table. A changed `object_id`, an empty `sales` table, or a surviving `dbo` copy all mean the
move did not really happen.

## Rollback

The move reverses losslessly as a schema operation: `ALTER SCHEMA [dbo] TRANSFER [sales].[OrderLine]` moves the
table back with its rows and `object_id`, and every `schema.Table` reference must be repointed back to `dbo`. The
reference edits are **not** auto-reversed. A move that ever went through as the drop-enabled data-loss variant has
already lost the rows and cannot be rolled back from the schema alone — restore from a backup. Only the forward
move (phantom + corrective transfer) was exercised here.

## Not verified

- **Application impact.** Every fully-qualified `dbo.OrderLine` reference — in views, procedures, synonyms, and
  application code — breaks when the table becomes `sales.OrderLine`; that all of them were found and repointed
  is not confirmed here. The application owner owns closing this before promotion.
- **The drop-enabled data-loss variant.** Under a deploy posture with `DropObjectsNotInSource = true` (and data
  loss unblocked), the same header edit would drop `dbo.OrderLine` and lose its rows rather than strand them.
  That path was not run here — only the production-faithful phantom-move outcome was — but it is the documented
  worse case; confirm your target's drop policy.
- **The refactorlog path.** Shipping the move as a header edit **plus** a refactorlog entry (so SSDT emits the
  transfer itself) is the declarative alternative to the scripted `ALTER SCHEMA TRANSFER`. This proof used the
  scripted transfer directly; that a refactorlog entry is present and correct in each environment is not
  confirmed here — confirm it before each promotion.
- **Other environments.** Test, UAT, and Prod may hold `OrderLine` at different volumes or already carry a
  `sales` schema; the disposable copy cannot see them. Run the verification query before promotion.
- **Production scale and timing.** `ALTER SCHEMA TRANSFER` is a metadata operation, but any dependent rebuild is
  exercised at seed scale only; blocking and duration at production row counts are not shown here.
