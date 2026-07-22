# Customer: add an index on Email (additive and applies clean; the build cost lives in the row count, not the .sql)

**In OutSystems** — You add an Index on the `Email` Attribute of the `Customer` Entity (or ask to "make Email searchable" / "the customer list screen is slow on Email").
**In SSDT** — a `CREATE NONCLUSTERED INDEX [IX_Customer_Email] ON [dbo].[Customer] ([Email])` object is added to the estate. You add the index; the publish engine builds it over every existing row at deploy time.

## Summary

You add a non-unique index on `Customer.Email`. This is **additive** — nothing is lost, and a
production publish **applies it clean in place** on a populated table. The one thing to separate from
what the engine *does* is what it *costs*: `CREATE INDEX` builds the index over every existing row, and
on a large table that build takes a **write-blocking lock whose duration scales with the row count**.
That cost lives in the data, not in the `.sql`. The change was proven objectively against a Twin — a
disposable SQL Server database published from this estate and filled with real-shaped synthetic data —
with a **production-faithful** publish (`BlockOnPossibleDataLoss = true`, the deployment a real
environment runs). No work item was provided with the request; attach one before merge so the record is
traceable.

The disposable copy is small, so its build was instant — but **the observed build time is not the
production build time**; row count is the predictor. This is a *non-unique* index: unlike a *unique*
index (see `add-unique.md`), it cannot be refused by the data — duplicate `Email` values are fine.

## Review & release

- Any team member can review this: the change is additive and the running application is unaffected.
- It ships as a single declarative schema change: SSDT emits `CREATE INDEX` and builds the index over
  every existing row. The index rides its own estate file (`Tables/dbo.Customer.IX_Email.sql`) — one
  statement per file, so the `Customer` table definition is untouched.
- Added scrutiny, when the target table is large: at production row counts the build takes a
  write-blocking lock and may block writes or run long — schedule a maintenance window, or use
  `WITH (ONLINE = ON)` where the target edition is Enterprise/Developer (it fails on Standard). Whether
  a dev lead should review it turns on the row count, not the `.sql`.

## Changes

| File | Change |
|---|---|
| `Tables/dbo.Customer.IX_Email.sql` | Adds `CREATE NONCLUSTERED INDEX [IX_Customer_Email] ON [dbo].[Customer] ([Email])` |

No renames (the refactorlog is unchanged). No table, view, or procedure changes — the index is a new,
separate object; `dbo.Customer`'s definition is untouched.

## Data remediation

None. An index holds no source data — it is a derived structure built from the rows already present.
There is nothing to fill, reconcile, or backfill; the only consideration is the build's lock duration
at scale (see Review & release), which is an operational window, not a data fix.

## Deployment evidence — objective proof, production-faithful publish, 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, materializes
real-shaped data, adds the index as its own estate file, and asserts the outcome under a
**production-faithful** DacFx posture (`BlockOnPossibleDataLoss = true`, no smart-defaults). DacFx is
the same publish engine `sqlpackage` wraps.

**Test:** `Twin.Tests.Integration.SamplePrCleanApplyTests+SamplePrCleanApplyTests.add-index: a non-unique index builds clean over a populated table`

```
Passed!  - Failed:     0, Passed:     6, Skipped:     0, Total:     6, Duration: 1 m 24 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact — the index builds clean over a populated table.** `Customer` held **25 rows** and had no
`IX_Customer_Email` index. The production-faithful publish of the `CREATE NONCLUSTERED INDEX` was
**accepted**; the index landed enabled and non-unique with all 25 rows intact. Verbatim from the run:

```
baseline: Customer rows=25, IX_Customer_Email exists=0
production publish (BlockOnPossibleDataLoss=true) CREATE NONCLUSTERED INDEX [IX_Customer_Email]: APPLIED (Ok).
  after apply: IX_Customer_Email exists=1, type_desc=NONCLUSTERED, is_unique=0 (non-unique), is_disabled=0, Customer rows=25 (intact)
```

The publish returned `Ok` under the production-faithful posture — a `CREATE INDEX` is a clean additive
change and there is no data condition a non-unique index can violate. After the apply: the index exists
as `NONCLUSTERED`, `is_unique = 0` (non-unique), `is_disabled = 0` (enabled and usable), and the row
count is unchanged (**25 → 25**).

## Verification — run in each environment after deployment

```sql
-- expect 1 row, is_disabled = 0: the index landed and is enabled
SELECT name, type_desc, is_unique, is_disabled
FROM sys.indexes
WHERE object_id = OBJECT_ID('dbo.Customer') AND name = 'IX_Customer_Email';
```

## Rollback

`DROP INDEX IX_Customer_Email ON dbo.Customer;` — lossless: the index holds no source data, only a
derived structure. Re-adding it incurs the same write-blocking build cost. Backing the change out was
not exercised.

## Not verified

- **Production build time and lock duration.** The disposable copy is small, so its build was
  effectively instant; the production build time and how long the write-blocking lock lasts are
  governed by the production row count, which the copy does not exercise.
- **Target edition.** `ONLINE = ON` (a non-blocking build) requires Enterprise/Developer; on Standard
  the build blocks writes for its duration. The target's edition is not confirmed here.
- **Whether the index is worth its write cost.** The index speeds reads on `Email` but adds a small
  cost to every insert/update that maintains it; whether that trade fits the workload is a design
  decision the copy cannot make.
