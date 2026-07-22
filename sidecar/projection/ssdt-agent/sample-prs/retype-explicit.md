# OrderLine: change Quantity's type (a lossy narrowing is blocked; a widening retype lands clean)

**In OutSystems** — You change the *Data Type* of the `Quantity` Attribute on `OrderLine` to a smaller numeric type that not every value fits (the kind of "store it as a smaller number" change where some rows are out of range).
**In SSDT** — `[Quantity] INT` becomes `[Quantity] TINYINT` in `Tables/dbo.OrderLine.sql`. You change the column's type; the publish engine decides whether the data survives the cast.

## Summary

You change `Quantity` from `INT` to `TINYINT`. This is a **value-reshaping, lossy** type change:
`TINYINT` holds `0–255`, and the data holds larger values, so some rows cannot be represented in the
new type. On a **populated** table a production publish **refuses it**, and the direction of the change
is what decides everything — a *narrowing* (lossy) retype is blocked, while a *widening* (lossless)
retype of the same column lands clean in place. This was proven objectively against a Twin — a
disposable SQL Server database published from this estate and filled with real-shaped synthetic data —
with a **production-faithful** publish (`BlockOnPossibleDataLoss = true`, the deployment a real
environment runs).

A safe/widening retype (e.g. `INT → BIGINT`) is the separate *retype-implicit* change and needs none of
this ceremony. A lossy retype cannot be a single clean `ALTER COLUMN`: it stages across releases — add
a new column of the target type, convert the values that convert, decide the fate of those that do not,
then swap the new column in (see `../skills/_index/multi-phase/SKILL.md`). No work item was provided
with the request; attach one before merge.

## Review & release

- A dev lead must review this: existing data is reshaped, and rows that do not fit the new type must be
  decided on before anything ships. If non-convertible rows are dropped rather than reconciled, a
  principal must review it — data is removed and cannot be undone.
- A **widening/lossless** retype ships as a single schema change, applied in place — no data is at
  risk. A **narrowing/lossy** retype does **not**: the production publish is blocked while the table
  holds rows, and it ships across multiple releases (add-new → convert → swap), each its own PR, so the
  running application keeps working while the change is in flight.
- Added scrutiny: at production row counts the convert-and-swap may block writes or run long — schedule
  a window.

## Changes

| File | Change |
|---|---|
| `Tables/dbo.OrderLine.sql` (lossy case) | Changes `[Quantity]` from `INT` to `TINYINT` |
| `Tables/dbo.OrderLine.sql` (safe contrast) | Changes `[Quantity]` from `INT` to `BIGINT` (widening — the *retype-implicit* direction) |

No renames (the refactorlog is unchanged). No index, view, or procedure changes. Only the type of
`[Quantity]` changes.

## Data remediation

Probe the data first: `SELECT COUNT(*) FROM dbo.OrderLine WHERE Quantity > 255;` (and `MAX(Quantity)`)
counts the rows that will not fit the target type. On the proof substrate **19 of 25** rows exceed 255
(plus a seeded value of 1000). Those rows are the real question: shorten/clamp them deliberately, or
keep them and stage the change so the new type coexists with the old while the application migrates. No
clamp value is prescribed here; it is a data-owner decision (named under Not verified).

## Deployment evidence — objective proof, production-faithful publish, 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, seeds the blocking
condition in real-shaped data, and asserts the outcome under a **production-faithful** DacFx posture
(`BlockOnPossibleDataLoss = true`, no smart-defaults). DacFx is the same publish engine `sqlpackage`
wraps.

**Test:** `Twin.Tests.Integration.SamplePrTighteningTests+SamplePrTighteningTests.retype-explicit: a lossy narrowing type change blocks a populated column, a widening retype applies`

```
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 1 m 51 s - Twin.Tests.Integration.dll (net9.0)
```

Baseline probe: `OrderLine` held **25 rows**, `Quantity` type `int`, `MAX(Quantity) = 1000`, and **19**
rows exceed `TINYINT`'s range of 255.

**Fact 1 (primary) — the lossy narrowing is REFUSED on the populated table.** The production-faithful
`INT → TINYINT` publish was refused by the same data-blind row-presence guard the make-mandatory and
narrow changes hit. Verbatim from the run:

```
Could not deploy package.
Warning SQL72015: The type for column Quantity in table [dbo].[OrderLine] is currently  INT NOT NULL but is being changed to  TINYINT NOT NULL. Data loss could occur and deployment may fail if the column contains data that is incompatible with type  TINYINT NOT NULL.
Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 50000, Level 16, State 127, Line 6 Rows were detected. The schema update is terminating because data loss might occur.
Error SQL72045: Script execution error.  The executed script:
IF EXISTS (SELECT TOP 1 1
           FROM   [dbo].[OrderLine])
    RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127)
        WITH NOWAIT;
```

After the refusal: `OrderLine` rows = **25** (intact), `Quantity` type `int` (unchanged).

**Fact 2 (contrast) — a widening retype applies clean, on the same populated table.** Changing
`Quantity` from `INT` to `BIGINT` (widening — no value can be lost) published successfully under the
same production-faithful posture:

```
CONTRAST production publish (widening INT->BIGINT): APPLIED on the populated table, Quantity type=bigint, rows=25 (intact)
```

`Quantity` is now `bigint`, all 25 rows intact. The block is about the **direction** of the change (a
possible data loss), not about changing the type at all — a widening retype needs no empty table, no
gate relaxation, and no staging.

**Fact 3 (secondary) — the Twin's own relaxed publish blocks on the overflow itself.** The Twin's
`Runs.up` publishes with `BlockOnPossibleDataLoss = false`, which suppresses the row-presence guard.
Under it, the `ALTER COLUMN` actually runs and SQL Server's execution-time cast refuses it instead:

```
Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 220, Level 16, State 2, Line 1 Arithmetic overflow error for data type tinyint, value = 1000.
Error SQL72045: Script execution error.  The executed script:
ALTER TABLE [dbo].[OrderLine] ALTER COLUMN [Quantity] TINYINT NOT NULL;
```

`Quantity` stayed `int` (rolled back). This is the weaker of the two blocks: it fires on the first
out-of-range value, whereas the *production* guard (Fact 1) blocks any populated table.

## Verification — run in each environment after deployment

```sql
-- BEFORE the lossy retype — expect 0 rows: every value fits the target type
-- (a returned row would overflow / truncate on convert — reconcile it before the cutover)
SELECT Id, Quantity FROM dbo.OrderLine WHERE Quantity > 255;

-- AFTER deployment — confirm the column's type
SELECT ty.name AS type_name
FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID('dbo.OrderLine') AND c.name = 'Quantity';
```

## Rollback

A widening retype re-narrows only if every value still fits — and re-narrowing is itself a lossy retype
subject to the same block. For the lossy direction, once values are reshaped into the target type the
originals live only in whatever was preserved before the cutover (a backup, or the coexisting old
column). Not auto-reversed; backing the change out was not exercised.

## Not verified

- **Application impact.** Every read and write path still using the old type breaks once the column is
  swapped; that every caller has moved to the new type is not confirmed here — the application owner
  owns it.
- **The out-of-range rows.** Whether the 19 over-range rows are clamped, corrected, or dropped is a
  data-owner decision, not made here; dropping them removes data that cannot be recovered.
- **Other environments.** Test, UAT, and Prod may hold more out-of-range values than this copy; run the
  probe in each before the convert phase.
- **Production scale and timing.** The convert-and-swap is exercised at seed scale only; blocking and
  duration at large row counts are not shown by the disposable copy.
- **Reversibility.** Only the forward changes are exercised (the lossy block, the widening apply);
  restoring the original type and values after a lossy cutover is not proven.
