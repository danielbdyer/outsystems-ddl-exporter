# Customer: broaden the Email index to cover StatusId and include Name (a clean DROP+CREATE rebuild)

**In OutSystems** — You change the index on `Customer` so it covers more than `Email` — you want it to serve lookups that also filter on `StatusId` and read `Name` without touching the table.
**In SSDT** — the index definition in `Tables/dbo.Customer.IX_Email.sql` changes from `([Email])` to `([Email], [StatusId]) INCLUDE ([Name])`. SSDT does **not** alter an index in place — it emits `DROP INDEX` + `CREATE INDEX`, a full rebuild over all rows.

## Summary

You edit an existing index to add a key column and an included column. In OutSystems you adjust the index in
the Entity editor; here the same intent is an edited index definition, and because this change adds **no**
uniqueness, the publish is **clean** — an index holds no source data, only a derived structure, so rebuilding it
over a populated table applies in place and loses nothing. This was proven objectively against a Twin — a
disposable SQL Server database published from this estate and filled with real-shaped synthetic data — with a
**production-faithful** publish (`BlockOnPossibleDataLoss = true`, the deployment a real environment runs). No
work item was provided with the request; attach one before merge so the record is traceable.

**The one case where an index change is not free.** Making a non-unique index **unique** is a claim over the
data: the unique index is built over every existing row, so it fails the instant two rows share the key value.
That is a *different* change from this one — this PR adds a key column and an INCLUDE, no `UNIQUE` — so it is
never blocked by the data. If you later make an index unique, probe for duplicates first
(`GROUP BY <keys> HAVING COUNT(*) > 1`) and expect a pre-deployment de-dupe (see `add-unique.md`).

## Review & release

- Any team member can review a key-or-include change: it is a structural rebuild and the running application is
  unaffected — every query that used the old index still works, now with a wider one available.
- It ships as a single declarative schema change, applied in place: SSDT emits `DROP INDEX` + `CREATE INDEX`, a
  full rebuild over all rows. No gate relaxation, no staging.
- Added scrutiny: none for this seed-scale table. At production row counts the `DROP INDEX` + `CREATE INDEX`
  rebuild blocks writes to `Customer` while it runs — on a large table push it to an experienced developer with
  a named window.

## Changes

| File | Change |
|---|---|
| `Tables/dbo.Customer.IX_Email.sql` | Changes `IX_Customer_Email` from `([Email])` to `([Email], [StatusId]) INCLUDE ([Name])` |

The index rides its **own one-statement file** (a `CREATE INDEX` appended to the `Customer` table's `CREATE`
fails the model build with `SQL71006: Only one statement is allowed per batch`). No renames (the refactorlog is
unchanged). No table, view, or procedure changes — only the index's shape.

## Data remediation

None required — the change adds no constraint over the data. An index holds no source rows, so the rebuild
neither reads nor rewrites any `Customer` value; it only re-derives the index structure.

## Deployment evidence — objective proof, production-faithful publish, 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, materializes real-shaped data
**with the index already present**, edits the index definition, and asserts the outcome under a
**production-faithful** DacFx posture (`BlockOnPossibleDataLoss = true`, no smart-defaults). DacFx is the same
publish engine `sqlpackage` wraps.

**Test:** `Twin.Tests.Integration.SamplePrSchemaChangeTests+SamplePrSchemaChangeTests.modify-index: an existing index gains a key column and an INCLUDE via a clean DROP+CREATE`

```
Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4, Duration: 1 m 22 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact — the rebuild applies clean and the index takes its new shape; rows intact.** `Customer` held **25 rows**
and carried the single-column index `IX_Customer_Email ([Email])`. The production-faithful publish of the
broadened definition was **accepted**, and `sys.index_columns` shows the new shape. Verbatim from the run:

```
baseline: Customer rows=25, IX_Customer_Email exists=1, key columns=1 (Email), included columns=0, index_id=3
production publish (BlockOnPossibleDataLoss=true) modify IX_Customer_Email (Email) -> (Email, StatusId) INCLUDE (Name) [SSDT DROP+CREATE]: APPLIED (Ok)
  after apply: index exists=1, key columns=2, 2nd key column=StatusId, included columns=1, included column=Name, type_desc=NONCLUSTERED, index_id=3 (was 3), Customer rows=25 (intact)
```

Reading the facts:
- **Before.** One index existed with **1 key column** (`Email`) and **0 included columns**.
- **The publish applied clean (`Ok`)** under `BlockOnPossibleDataLoss = true` — SSDT's `DROP INDEX` +
  `CREATE INDEX` rebuild is not a data-loss operation, so it is not blocked on a populated table.
- **After.** The index now has **2 key columns** — `Email` plus `StatusId` as the second key — and **1 included
  column**, `Name`, still `NONCLUSTERED`. `Customer` held **25 rows** before and after: the rebuild re-derived
  the index without touching a single source value.

## Verification — run in each environment after deployment

```sql
-- expect the new shape: 2 key columns (Email, StatusId) then Name as an included column
SELECT c.name AS column_name, ic.key_ordinal, ic.is_included_column
FROM sys.indexes i
JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE i.name = 'IX_Customer_Email' AND i.object_id = OBJECT_ID('dbo.Customer')
ORDER BY ic.is_included_column, ic.key_ordinal;

-- expect 1 row, is_unique = 0: the index exists and is still non-unique
SELECT name, is_unique FROM sys.indexes
WHERE object_id = OBJECT_ID('dbo.Customer') AND name = 'IX_Customer_Email';
```

## Rollback

Revert the `.sql` edit and republish; SSDT emits `DROP INDEX` + `CREATE INDEX` to restore the prior single-column
shape. The change is lossless — an index holds no source data — but re-creating it runs the same write-blocking
rebuild. Backing the change out was not exercised here.

## Not verified

- **Application impact.** None expected — widening an index only adds coverage; existing queries are unaffected
  and no insert/update path changes. (This would differ if the change added `UNIQUE`, which it does not.) Not
  confirmed against the running application here.
- **Query benefit.** Whether the broadened index actually improves the intended lookups is a plan/statistics
  question measured per environment, not shown by this publish.
- **Other environments.** Test, UAT, and Prod may carry a differently-named or differently-shaped index on these
  columns; the disposable copy of Dev cannot see them. Run the verification query before promotion.
- **Production scale and timing.** On a large `Customer` the `DROP INDEX` + `CREATE INDEX` rebuild may block
  writes or run long; the small copy does not show it. Schedule a window.
