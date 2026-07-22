# OrderLine: widen Quantity to a bigger number type (a widening retype — applies clean, every value preserved)

**In OutSystems** — You change the *Data Type* of the `Quantity` Attribute on `OrderLine` to a bigger number (Integer → Long Integer).
**In SSDT** — `[Quantity] INT` becomes `[Quantity] BIGINT` in `Tables/dbo.OrderLine.sql`. You change the column's type in the *widening* direction; every existing value already fits, so it is lossless.

## Summary

You change `OrderLine.Quantity` from `INT` to `BIGINT` — a **widening** type change. Direction is
everything: a widening retype is free because every existing value already fits the bigger type, so a
production publish **applies it clean in place** with nothing to refuse and no data to move. This was
proven objectively against a Twin — a disposable SQL Server database published from this estate and
filled with real-shaped synthetic data — with a **production-faithful** publish
(`BlockOnPossibleDataLoss = true`, the deployment a real environment runs), and every stored `Quantity`
value was confirmed identical before and after. No work item was provided with the request; attach one
before merge so the record is traceable.

The one thing to confirm is the **direction**. A true widening (`INT → BIGINT`, `VARCHAR → NVARCHAR`,
`DECIMAL(10,2) → DECIMAL(18,2)`) is this clean one-liner. A *narrowing or value-reshaping* cast
(`TINYINT` where values overflow, Text → Date) is a different, staged job that the production publish
**refuses** on a populated table — that is the *retype-explicit* change (see `retype-explicit.md`),
not this one.

## Review & release

- Any team member can review this: the change is data-preserving — every value already fits the wider
  type — and the running application is unaffected.
- It ships as a single schema change, applied in place — SSDT emits `ALTER COLUMN [Quantity] BIGINT
  NOT NULL`. No data is read or written.
- Added scrutiny, only when the table is large (otherwise none): at production row counts the
  `ALTER COLUMN` rewrite may block writes or run long — schedule a window.

## Changes

| File | Change |
|---|---|
| `Tables/dbo.OrderLine.sql` | Widens `[Quantity]` from `INT` to `BIGINT` (a widening/implicit retype) |

No renames (the refactorlog is unchanged). No index, view, or procedure changes. Only the type of
`[Quantity]` changes — its nullability (`NOT NULL`) and every other column are untouched.

## Data remediation

None. A widening cast loses no value — every `INT` already fits `BIGINT` — so there is nothing to
reconcile before the change lands. The proof confirmed this directly (below): the aggregate sum, the
maximum, and a single-row probe were **identical** before and after.

## Deployment evidence — objective proof, production-faithful publish, 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, materializes
real-shaped data, applies the widening retype, and asserts the outcome under a **production-faithful**
DacFx posture (`BlockOnPossibleDataLoss = true`, no smart-defaults). DacFx is the same publish engine
`sqlpackage` wraps.

**Test:** `Twin.Tests.Integration.SamplePrCleanApplyTests+SamplePrCleanApplyTests.retype-implicit: a widening type change applies clean and preserves every value`

```
Passed!  - Failed:     0, Passed:     6, Skipped:     0, Total:     6, Duration: 1 m 24 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact — the widening retype applies clean and preserves every value.** `OrderLine` held **25 rows**,
`Quantity` type `int`. The production-faithful publish to `bigint` was **accepted**; the column widened
in place and every stored value was preserved — the aggregate `SUM(Quantity) = 13003`, the
`MAX(Quantity) = 983`, and the probed row's exact value (`447`) were identical before and after.
Verbatim from the run:

```
baseline: OrderLine rows=25, Quantity type=int, SUM(Quantity)=13003, MAX(Quantity)=983
  probe (Quantity of MIN(Id) row) before = 447
production publish (BlockOnPossibleDataLoss=true) ALTER Quantity INT -> BIGINT: APPLIED (Ok).
  after apply: Quantity type=bigint, OrderLine rows=25 (intact), SUM(Quantity)=13003, MAX(Quantity)=983
  probe (Quantity of MIN(Id) row) after = 447
```

The publish returned `Ok` under the production-faithful posture — a wider numeric type cannot lose a
value, so there is nothing for the engine to refuse (contrast the lossy `INT → TINYINT` narrowing in
`retype-explicit.md`, which the same posture blocks). After the apply: the column type is `bigint`, the
row count is unchanged (**25 → 25**), and the sum, maximum, and single-row probe are identical
(`13003`, `983`, `447` on both sides), proving no value was reshaped.

## Verification — run in each environment after deployment

```sql
-- expect type_name = bigint: the column ends at the widened type
SELECT c.name, ty.name AS type_name
FROM sys.columns c
JOIN sys.types ty ON ty.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID('dbo.OrderLine') AND c.name = 'Quantity';
```

## Rollback

Narrowing `BIGINT` back to `INT` reverses the change and is lossless **only while no value larger than
`INT`'s range has been written**; re-narrowing is itself a lossy retype subject to the row-presence /
overflow block (see `retype-explicit.md`). The forward widen changes no data, so reversing the schema
is safe immediately after deployment, before any larger value is stored. Backing the change out was not
exercised.

## Not verified

- **Application impact.** A widened column can change how strongly-typed application code handles it
  (an Int32 mapping now backing a `BIGINT` column, an SSIS column type); application-side type handling
  is not confirmed here — the application owner owns it.
- **Direction confirmation at scale.** This is proven a genuine widening on the disposable copy's data;
  that no other environment holds a value that would make the reverse (narrowing) lossy is not shown
  here.
- **Production scale and timing.** Whether the `ALTER COLUMN` is metadata-only or a size-of-data rewrite
  at production row counts is not shown by the small copy; on a large table schedule a window.
- **Reversibility.** Only the forward widening is exercised; narrowing back is the lossy direction (see
  Rollback).
