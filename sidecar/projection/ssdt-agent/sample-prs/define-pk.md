# OrderLine: define the Identifier (a primary key over unique, non-NULL keys builds the clustered index clean)

**In OutSystems** — Every Entity has an Identifier — the auto-number key that uniquely names each record. You declare it on `OrderLine` (its `Id`), the way every Entity gets an identifier when you create it.
**In SSDT** — a `CONSTRAINT [PK_OrderLine] PRIMARY KEY ([Id])` is declared on `Tables/dbo.OrderLine.sql`. On an existing populated table the publish engine **builds a clustered index** — it scans and reorders every row — and it **fails if the key has any duplicate or NULL value**.

## Summary

You define the Identifier on `OrderLine`. A primary key is a **claim of uniqueness proven at build
time**: on a table that already holds rows, adding it builds a clustered index over every row, and the
build only succeeds if the key column is **unique and non-NULL**. On a brand-new table the key is part
of the create and just applies; on a populated table it still ships as a single in-place change, but the
build runs over live data, so it earns a closer review.

This was proven objectively against a Twin — a disposable SQL Server database published from this estate
and filled with real-shaped synthetic data — with a **production-faithful** publish
(`BlockOnPossibleDataLoss = true`, the deployment a real environment runs). The key column here is `Id`,
an auto-number (IDENTITY) column: it is **already NOT NULL and unique**, so no "make it NOT NULL first"
step is needed and the clustered index built clean with every row and every value intact. No work item
was provided with the request; attach one before merge so the record is traceable.

## Review & release

- A dev lead or an experienced developer should review this: the clustered-index build runs over every
  existing row of a populated table (not a brand-new one).
- It ships as a **single schema change, applied in place** — the primary key is declared and SQL Server
  builds its clustered index. Here the key (`Id`) is already unique and non-NULL, so it publishes clean.
- **Prove the key is clean first.** If the key column held a **duplicate or NULL** value, the index
  build would be blocked and the error would name the offending keys; it would then ship as one release
  with a pre-deployment script that dedupes or assigns keys before the key can land. Not the case here.
- Added scrutiny: none at this size; at >1M rows the clustered-index build locks the table and runs
  long — schedule a window.

## Changes

| File | Change |
|---|---|
| `Tables/dbo.OrderLine.sql` | Declares `CONSTRAINT [PK_OrderLine] PRIMARY KEY ([Id])` — the table's clustered primary key on its auto-number Identifier |

No renames (the refactorlog is unchanged). No index, view, or procedure changes beyond the primary
key's own clustered index; no column values are touched.

## Data remediation

None here — the key was already clean — but the duplicate and NULL probes are the gate, so run them
first:

```sql
-- expect 0 rows: no key value repeats
SELECT Id, COUNT(*) FROM dbo.OrderLine GROUP BY Id HAVING COUNT(*) > 1;
-- expect 0: no key value is NULL
SELECT COUNT(*) FROM dbo.OrderLine WHERE Id IS NULL;
```

On the Twin both returned **0** (and `Id` is already NOT NULL), which is why the primary key built
clean. A primary-key column must be NOT NULL: here that already holds, so no widening/NOT-NULL step is
required first. If either probe returns more than 0 in another environment, the build is blocked there —
dedupe or assign keys in a pre-deployment script before the key can land, and record the original values
for a manual restore.

## Deployment evidence — objective proof, production-faithful publish, 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, mints real-shaped data,
removes the primary key to leave a heap (a table with no primary key) with unique keys, then adds the
primary key back and asserts the outcome under a **production-faithful** DacFx posture
(`BlockOnPossibleDataLoss = true`, no smart-defaults). DacFx is the same publish engine `sqlpackage`
wraps.

**Test:** `Twin.Tests.Integration.SamplePrReferenceIntegrityTests+SamplePrReferenceIntegrityTests.define-pk: adding a primary key over unique non-NULL keys builds the clustered index clean`

```
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 1 m 10 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact — the primary key builds its clustered index clean, and every row and value is intact.** With
`OrderLine` reduced to a heap (**PK exists = 0**, no clustered primary-key index) holding **25 rows**
whose `Id` had **0 duplicate keys**, **0 NULL keys**, and was already NOT NULL, the production-faithful
publish of the primary key was **accepted**; the clustered index built and no row moved value (a
row-content digest was byte-identical before and after). Verbatim from the run:

```
baseline: OrderLine rows=25, PK_OrderLine exists=1, row digest=-75979182
before (heap): PK exists=0, clustered PK index=0, key NULLs=0, duplicate keys=0, [Id] is_nullable=0 (0 = already NOT NULL, so no NOT NULL step is needed first)
production publish (BlockOnPossibleDataLoss=true) ADD CONSTRAINT [PK_OrderLine] PRIMARY KEY: APPLIED (Ok)
  after apply: PK exists=1, clustered primary-key index=1, OrderLine rows=25 (was 25, intact), row digest=-75979182 (unchanged=true)
```

The publish returned `Ok`: the primary key exists as the table's clustered index, the row count is
unchanged (**25 → 25**), and the content digest is identical (`-75979182` before and after) — the build
reorders storage but changes no value. A primary key over unique, non-NULL keys is a single clean
in-place change.

## Verification — run in each environment after deployment

```sql
-- expect 0 rows: the key is unique, no value repeats
SELECT Id, COUNT(*) FROM dbo.OrderLine GROUP BY Id HAVING COUNT(*) > 1;

-- expect 0: no key value is NULL
SELECT COUNT(*) FROM dbo.OrderLine WHERE Id IS NULL;

-- expect 1 row: the primary key exists as the clustered index
SELECT name, type_desc FROM sys.indexes
WHERE object_id = OBJECT_ID('dbo.OrderLine') AND is_primary_key = 1;
```

## Rollback

Dropping the primary key is lossless for the data — it drops the constraint and its clustered index
without changing any row value:

```sql
ALTER TABLE dbo.OrderLine DROP CONSTRAINT PK_OrderLine;
```

If a pre-deployment script had deduped or assigned keys to make the key hold (not needed here), that
remediation is not auto-reversed — the recorded originals are what a manual restore uses. Backing the
change out was not exercised.

## Not verified

- **Application impact.** Any insert path that writes a duplicate or NULL `Id` will now fail with a
  primary-key violation; the application's insert code is not confirmed here.
- **Other environments.** Test, UAT, and Prod may hold duplicate or NULL keys this copy does not — run
  the duplicate and NULL probes in each before promotion.
- **Production scale and timing.** On a large table the clustered-index build locks the table and runs
  long; the small copy cannot show the duration.
- **Reversibility.** The forward build is proven (clean, values intact); if keys ever had to be deduped
  or assigned, backing that out is not exercised.
