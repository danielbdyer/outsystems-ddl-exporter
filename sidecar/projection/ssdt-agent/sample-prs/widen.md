# Customer: widen Email to 400 (data-preserving; applies clean in place, every value kept)

**In OutSystems** — You increase the *Length* of the `Email` Attribute on the `Customer` Entity from 250 to 400 ("make the Email field longer").
**In SSDT** — `[Email] NVARCHAR(250) NOT NULL` becomes `[Email] NVARCHAR(400) NOT NULL` in `Tables/dbo.Customer.sql`. You enlarge the column; every existing value still fits, so it is data-preserving.

## Summary

You widen `Customer.Email` from `NVARCHAR(250)` to `NVARCHAR(400)`. Widening is **data-preserving by
definition** — every value you already have still fits the bigger type — so a production publish
**applies it clean in place**, reading and rewriting no data. This was proven objectively against a
Twin — a disposable SQL Server database published from this estate and filled with real-shaped
synthetic data — with a **production-faithful** publish (`BlockOnPossibleDataLoss = true`, the
deployment a real environment runs), and every stored `Email` value was confirmed byte-identical
before and after. No work item was provided with the request; attach one before merge so the record is
traceable.

Widen and *narrow* are mirror images: widening's only risk is structural, not data — the
**index-key byte limit** (a column inside a non-clustered index key cannot push the key past ~1700
bytes, and `NVARCHAR` widening doubles storage). `Email` sits in **no index** here and `NVARCHAR(400)`
is 800 bytes, so that limit is not approached. Narrowing back is the risky direction (it can truncate);
see `narrow.md`.

## Review & release

- Any team member can review this: the change is data-preserving and the running application is
  unaffected.
- It ships as a single schema change, applied in place — SSDT emits `ALTER COLUMN [Email] NVARCHAR(400)
  NOT NULL`. No data is read or written.
- Added scrutiny, only where the condition holds (otherwise none): if the column sat inside an index
  key near the byte limit, the index would be redesigned in the same PR (a dev lead review) — it does
  not here. On a very large table on an older SQL version the change may rebuild rather than run
  metadata-only and could block writes; schedule a window if so.

## Changes

| File | Change |
|---|---|
| `Tables/dbo.Customer.sql` | Widens `[Email]` from `NVARCHAR(250)` to `NVARCHAR(400)` |

No renames (the refactorlog is unchanged). No index, view, or procedure changes. Only the length of
`[Email]` changes — its type (`NVARCHAR`), nullability (`NOT NULL`), and every other column are
untouched.

## Data remediation

None. Every existing value already fits the wider type, so there is nothing to reconcile — the widen
reads and rewrites no data. The proof confirmed this directly (below): the aggregate value digest and a
single-row probe were **identical** before and after.

## Deployment evidence — objective proof, production-faithful publish, 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, materializes
real-shaped data, applies the widen, and asserts the outcome under a **production-faithful** DacFx
posture (`BlockOnPossibleDataLoss = true`, no smart-defaults). DacFx is the same publish engine
`sqlpackage` wraps.

**Test:** `Twin.Tests.Integration.SamplePrCleanApplyTests+SamplePrCleanApplyTests.widen: enlarging a column applies clean and preserves every value`

```
Passed!  - Failed:     0, Passed:     6, Skipped:     0, Total:     6, Duration: 1 m 24 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact — the widen applies clean and preserves every value.** `Customer` held **25 rows**, `Email`
`NVARCHAR(250)` = 500 bytes. The production-faithful publish to `NVARCHAR(400)` = 800 bytes was
**accepted**; the column widened in place and every stored value was preserved — the aggregate value
digest (`795751655`), the longest length (`MAX(LEN) = 9`), and the probed row's exact value
(`text-4405`) were identical before and after. Verbatim from the run:

```
baseline: Customer rows=25, Email max_length=500 bytes (NVARCHAR 250), MAX(LEN(Email))=9, value digest=795751655
  probe (Email of MIN(Id) row) before = text-4405
production publish (BlockOnPossibleDataLoss=true) ALTER Email NVARCHAR(250)->NVARCHAR(400): APPLIED (Ok).
  after apply: Email max_length=800 bytes (NVARCHAR 400), Customer rows=25 (intact), MAX(LEN(Email))=9, value digest=795751655
  probe (Email of MIN(Id) row) after = text-4405
```

The publish returned `Ok` under the production-faithful posture — a wider type cannot lose a value, so
there is nothing for the engine to refuse. After the apply: `max_length = 800` bytes (`NVARCHAR(400)`),
the row count is unchanged (**25 → 25**), and the value digest and single-row probe are byte-identical
(`795751655` and `text-4405` on both sides), proving no value was read or rewritten.

## Verification — run in each environment after deployment

```sql
-- expect the widened length: NVARCHAR(400) => max_length 800 bytes
SELECT c.name, t.name AS type_name, c.max_length
FROM sys.columns c
JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID('dbo.Customer') AND c.name = 'Email';
```

## Rollback

Narrowing back to `NVARCHAR(250)` is lossless **only while no value longer than 250 has been written**.
The forward widen changes no data, so reversing the schema is safe immediately after deployment, before
any wider value is stored; once a value longer than 250 exists, `ALTER COLUMN` to the narrower type is
blocked or truncates (see `narrow.md`). Backing the change out was not exercised.

## Not verified

- **Application impact.** A wider column is additive to callers, but a client that assumed the old
  length (a fixed-size buffer, a downstream contract, an SSIS column mapping) is not exercised on the
  disposable copy; the application owner confirms it tolerates the new length.
- **Other environments.** Row counts and SQL Server version differ; a widen that is metadata-only here
  may rebuild where the table is larger or the server older.
- **Production scale and timing.** If the change rebuilds, blocking or duration at production row counts
  is not shown by the disposable copy.
- **Reversibility.** Only the forward widen is proven; narrowing back is lossless only before a longer
  value is stored (see Rollback).
