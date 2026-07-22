# OrderLine: make Sku optional (a loosening — never refused, applies clean; the risk is downstream)

**In OutSystems** — You set *Is Mandatory = No* on the `OrderLine.Sku` Attribute ("let Sku be blank now").
**In SSDT** — `[Sku] NVARCHAR(64) NOT NULL` becomes `[Sku] NVARCHAR(64) NULL` in `Tables/dbo.OrderLine.sql`. You loosen the column; no existing row can violate "allows NULL", so the publish never refuses it.

## Summary

You make `OrderLine.Sku` optional — a pure **loosening** of an existing column. No existing row can
violate the new "allows NULL" rule, so a production publish **applies it clean in place** and can never
refuse it. This was proven objectively against a Twin — a disposable SQL Server database published from
this estate and filled with real-shaped synthetic data — with a **production-faithful** publish
(`BlockOnPossibleDataLoss = true`, the deployment a real environment runs). No work item was provided
with the request; attach one before merge so the record is traceable.

**A clean publish is not the same as no risk.** This change touches no data and is never blocked at the
deploy — but the danger lives where the engine cannot see it: **downstream**. Any report, query, or
code path that assumed `Sku` is always filled will now meet a `NULL` and can break on one. That is why
the question to settle before this ships is not "will it deploy?" (it will) but "who *relies* on `Sku`
being non-NULL?" This is the mirror image of *make-mandatory* — tightening is guarded at the deploy;
loosening is safe at the deploy and shifts the risk to the consumers.

## Review & release

- It ships as a single schema change, applied in place — SSDT emits `ALTER COLUMN [Sku] NVARCHAR(64)
  NULL`. No data is read or written, and a loosening is never refused.
- Any team member can review this when nothing downstream assumes `Sku` is always populated. If a
  downstream consumer (a report, an ETL/SSIS job, application code) does assume it, a dev lead or an
  experienced developer should review instead — the running application must change to tolerate a
  `NULL`. This changes *who* reviews, not *how* it ships.
- Added scrutiny: none at the deploy layer — the loosening cannot fail. The scrutiny is on the
  consumers, named above.

## Changes

| File | Change |
|---|---|
| `Tables/dbo.OrderLine.sql` | Loosens `[Sku]` from `NVARCHAR(64) NOT NULL` to `NVARCHAR(64) NULL` |

No renames (the refactorlog is unchanged). No index, view, or procedure changes. Only the nullability
of `[Sku]` changes — its width (64), type (`NVARCHAR`), and every other column are untouched.

## Data remediation

None at the deploy. The loosening writes no data — existing `Sku` values are untouched and simply
remain non-NULL. The only "remediation" is downstream and is a review task, not a data fix: confirm the
consumers that read `Sku` tolerate a `NULL` before any row is written blank (named under Not verified).

## Deployment evidence — objective proof, production-faithful publish, 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, materializes
real-shaped data, applies the loosening, and asserts the outcome under a **production-faithful** DacFx
posture (`BlockOnPossibleDataLoss = true`, no smart-defaults). DacFx is the same publish engine
`sqlpackage` wraps.

**Test:** `Twin.Tests.Integration.SamplePrCleanApplyTests+SamplePrCleanApplyTests.make-optional: relaxing NOT NULL to NULL applies clean on a populated table`

```
Passed!  - Failed:     0, Passed:     6, Skipped:     0, Total:     6, Duration: 1 m 24 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact — the loosening applies clean on a populated table.** `OrderLine` held **25 rows**, `Sku`
`NOT NULL` (`is_nullable = 0`). The production-faithful publish of the loosening was **accepted**; the
column now permits `NULL`, and every existing value was kept. Verbatim from the run:

```
baseline: OrderLine rows=25, Sku is_nullable=0 (NOT NULL), non-NULL Sku rows=25
production publish (BlockOnPossibleDataLoss=true) ALTER Sku NVARCHAR(64) NOT NULL -> NULL: APPLIED (Ok).
  after apply: Sku is_nullable=1 (nullable), OrderLine rows=25 (intact), non-NULL Sku rows=25 (values kept)
```

The publish returned `Ok` under the production-faithful posture — a loosening cannot be refused,
because no existing row can violate "allows NULL". After the apply: `is_nullable = 1`, the row count is
unchanged (**25 → 25**), and all **25** rows still hold their original non-NULL `Sku` value (the
loosening changed the rule, not the data).

## Verification — run in each environment after deployment

```sql
-- expect is_nullable = 1: the column now permits NULL
SELECT c.name, c.is_nullable
FROM sys.columns c
WHERE c.object_id = OBJECT_ID('dbo.OrderLine') AND c.name = 'Sku';
```

## Rollback

The loosening writes no data, so there is nothing to restore on the data side. Reversing the schema is
a re-tightening to `NOT NULL` — a separate *make-mandatory* change, not an automatic reversal of this
one: it is guarded while the table holds rows and is not lossless once any `NULL` `Sku` has been written
(see `make-mandatory.md`). Backing the change out was not exercised.

## Not verified

- **Application / consumer impact.** Any report, query, ETL job, or code path that assumed `Sku` is
  never NULL will now meet one once a row is written blank; which consumers depend on it is not
  confirmed by the publish. The application owner owns closing this before promotion.
- **Other environments.** Whether Test, UAT, or Prod already hold rows this change will let go blank, or
  whether downstream jobs there tolerate a `NULL`, is not known from a disposable copy.
- **Reversibility.** Only the forward loosening is proven. Re-tightening is a make-mandatory change with
  its own row-presence guard and is not exercised here.
