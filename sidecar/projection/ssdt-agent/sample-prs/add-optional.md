# Customer: add an optional MiddleName (a nullable column — the safest change in the catalog, applies clean)

**In OutSystems** — You add a new Attribute `MiddleName` (Text) to the `Customer` Entity and leave *Is Mandatory = No*.
**In SSDT** — a new line `[MiddleName] NVARCHAR(100) NULL` is added to the `Customer` table in `Tables/dbo.Customer.sql`. You add the column to the table definition; existing rows simply take `NULL`.

## Summary

You add an optional `MiddleName` to `Customer` — the safest change there is. In OutSystems the
attribute appears and Service Studio handles the rest; here the same intent is a new **nullable**
column, and a production publish **applies it in place on a populated table** — `NULL` is always a
valid value for the rows already there, so nothing can conflict with it and the deployment is never
blocked. This was proven objectively against a Twin — a disposable SQL Server database published from
this estate and filled with real-shaped synthetic data — with a **production-faithful** publish
(`BlockOnPossibleDataLoss = true`, the deployment a real environment runs). No work item was provided
with the request; attach one before merge so the record is traceable.

This is the easy sibling of *add-mandatory*: the difference is the one flag. An optional add can never
be blocked because `NULL` satisfies every existing row; a *required* add on a populated table is
refused until those rows have a value (see `add-mandatory.md`).

## Review & release

- Any team member can review this: the change is additive and the running application is unaffected.
- It ships as a single schema change, applied in place — SSDT emits `ALTER TABLE ... ADD [MiddleName]
  NVARCHAR(100) NULL` and existing rows take `NULL`. No data is read or written.
- Added scrutiny: none — an optional column is additive and every existing row takes `NULL`.

## Changes

| File | Change |
|---|---|
| `Tables/dbo.Customer.sql` | Adds `[MiddleName] NVARCHAR(100) NULL` (nullable) to the table definition |

No renames (the refactorlog is unchanged). No index, view, or procedure changes. Only the new
`MiddleName` column is added; every existing column is untouched.

## Data remediation

None. The column is nullable, so there is nothing to fill for the existing rows — they take `NULL` as
the column is added, which is a valid value. No backfill, no gate relaxation, no staging.

## Deployment evidence — objective proof, production-faithful publish, 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, materializes
real-shaped data, applies the additive edit, and asserts the outcome under a **production-faithful**
DacFx posture (`BlockOnPossibleDataLoss = true`, no smart-defaults). DacFx is the same publish engine
`sqlpackage` wraps.

**Test:** `Twin.Tests.Integration.SamplePrCleanApplyTests+SamplePrCleanApplyTests.add-optional: a new nullable column applies clean on a populated table`

```
Passed!  - Failed:     0, Passed:     6, Skipped:     0, Total:     6, Duration: 1 m 24 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact — a populated table applies clean.** `Customer` held **25 rows** and had no `MiddleName`
column. The production-faithful publish of the nullable column was **accepted**, and the column landed
`is_nullable = 1` with all 25 rows intact. Verbatim from the run:

```
baseline: Customer rows=25, MiddleName column exists=0
production publish (BlockOnPossibleDataLoss=true) ADD [MiddleName] NVARCHAR(100) NULL: APPLIED (Ok).
  after apply: MiddleName column exists=1, is_nullable=1, Customer rows=25 (intact)
```

The publish returned `Ok` under the production-faithful posture — no row-presence guard, no block —
because `NULL` is a valid value for every existing row. The column exists (`is_nullable = 1`) and the
row count is unchanged (**25 → 25**).

## Verification — run in each environment after deployment

```sql
-- expect 1 row, is_nullable = 1: the optional column landed and accepts NULL
SELECT c.name, c.is_nullable
FROM sys.columns c
WHERE c.object_id = OBJECT_ID('dbo.Customer') AND c.name = 'MiddleName';
```

## Rollback

Remove the column from the `CREATE TABLE` and republish; SSDT emits
`ALTER TABLE dbo.Customer DROP COLUMN MiddleName;`. Lossless only while the column is unwritten — every
row holds `NULL` at deploy, so dropping it immediately after deployment loses nothing; once the
application writes values into it, dropping the column discards them. Backing the change out was not
exercised.

## Not verified

- **Application impact.** A nullable add does not change existing application behaviour, but any code
  intended to populate the new column is not exercised by the disposable copy — the application owner
  confirms it before promotion.
- **Production scale and timing.** The add is metadata-only on modern SQL Server with
  `IgnoreColumnOrder = True` (the profile keeps it True, so column position is a non-issue); that it
  stays metadata-only at production row counts and on the target's edition is not confirmed by the
  small copy.
- **Reversibility.** The forward add is proven; once values are written into the column, dropping it is
  lossy (see Rollback).
