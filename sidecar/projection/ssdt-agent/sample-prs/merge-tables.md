# Customer + CustomerAddress: merge the address satellite back into Customer (phase 1 — cardinality proven 1:1 before the copy, every row and value preserved, a 1:many merge caught before it silently drops rows)

**In OutSystems** — You fold the `CustomerAddress` Entity back into `Customer`: the two Entities become one, with `CustomerAddress`'s `PostalCode` Attribute absorbed onto `Customer`. This is two Entities becoming one — "we don't need two Entities, combine them."
**In SSDT** — the inverse of a split: **add** the absorbing column (`Customer.PostalCode`) to the survivor, **copy** the data from the absorbed table keyed by `CustomerId`, repoint every reader, then **drop** the absorbed `dbo.CustomerAddress` table. SSDT adds the nullable column and publishes clean; it will **not** copy the data, and — because `DropObjectsNotInSource = false` — it will **not** drop the absorbed table automatically either. Edit the `CREATE`; never write the `ALTER`.

## Summary

This PR proves **phase 1** of a staged merge. Like `split-table` and `extract-to-lookup`, folding two
Entities into one moves existing data behind a coexistence window and **cannot ship in one publish**. The
signature safety property of a merge — the one lesson that governs everything else — is **cardinality:
the absorbed side must be 1:1 with the survivor, and that must be proven *before* any copy runs.** A merge
silently *assumes* one absorbed row per parent; if the data is actually one-to-**many**, a naive copy
keeps one row per parent and **silently drops the rest** — and a value-hash will **not** flag it, because
it only compares the rows that survived. So the row-count proof comes first.

What phase 1 establishes objectively, on a disposable copy: the absorbed rows **equal** the distinct
parents (**1:1**, before anything is copied); the copy then fills every survivor row (0 left NULL) with
**byte-identical** values (a `SHA2_256` digest match); and — critically — a deliberately-injected
one-to-many row is **caught by the cardinality probe** (26 ≠ 25) and shown to make a naive copy silently
drop a row the value-hash never notices. The absorbed `CustomerAddress` table is deliberately retained;
its removal is a **later phase**, and under the production posture that removal is a **phantom** (the
declarative drop leaves the table in place — the real drop is a deliberate, reviewed scripted
`DROP TABLE`), which this PR also demonstrates.

Proven objectively against a **Twin** — a disposable SQL Server 2022 database published from this estate
and filled with real-shaped synthetic data — under a **production-faithful** publish
(`BlockOnPossibleDataLoss = true`, `GenerateSmartDefaults = false`, `DropObjectsNotInSource = false`). The
merge is imperative data motion, so it was run as scripted steps against the live Twin. No work item was
provided with the request; attach one before merge so the record is traceable.

## Review & release

- **A dev lead must review this**: existing data is moved into the survivor's new column and the absorbed
  table is dropped once the copy is proven complete.
- **Ships across three releases (three pull requests)**: add the absorbing column and copy from the
  absorbed table, cut the application over (repoint reads, foreign keys, and views), then drop the
  absorbed table — the two coexist while readers migrate. This PR is the add + copy phase. On an empty
  absorbed table the merge collapses to a clean drop plus an in-place column add (one release); here it
  holds 25 rows.
- **Cardinality (1:1) must be proven before the copy in every environment.** If any environment holds a
  one-to-many parent this copy did not, the merge is *semantically wrong as stated* there — STOP and treat
  it as a design decision (which of the parent's rows wins?), not a matter of how it ships.
- Added scrutiny: none for a small, clean, 1:1 merge; at >1M rows the copy scans the table and may block
  writes or run long — schedule a window.

## Changes

| Object | Change |
|---|---|
| `dbo.Customer` | Adds the absorbing column `[PostalCode] NVARCHAR(20) NULL` (nullable over a populated table → publishes clean) |
| copy (post-deploy) | `UPDATE c SET c.PostalCode = a.PostalCode FROM dbo.Customer c JOIN dbo.CustomerAddress a ON a.CustomerId = c.Id` — after the cardinality proof |
| `dbo.CustomerAddress` | **Retained this phase** — the absorbed table stays while readers migrate; its removal is a later PR, and under the production posture it is a phantom until a deliberate scripted `DROP TABLE` (see the evidence) |

No renames (the refactorlog is unchanged).

## Data motion

The absorbing column is added nullable, then `Customer.PostalCode` is backfilled from
`CustomerAddress.PostalCode` joined on `CustomerId`. Two independent proofs gate the phase: the
**cardinality** proof (absorbed rows == distinct parents) runs **first, before any copy**, and the
**value** proof (a before/after content-hash of the moving column, absorbed vs. survivor) confirms every
value arrived. A merge needs both, and the count has to come first — the value-hash cannot see a row a
one-to-many copy dropped.

## Deployment evidence — objective proof, production-faithful publish, live Twin (SQL Server 2022), 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, creates and seeds a 1:1
`CustomerAddress` satellite, and asserts the cardinality, copy-fidelity, one-to-many-catch, and
phantom-drop properties by consuming the data directly. DacFx is the same publish engine `sqlpackage`
wraps.

**Test:** `Twin.Tests.Integration.SamplePrStructuralTests+SamplePrStructuralTests.merge-tables (phase 1): cardinality is proven 1:1 before the copy, the copy preserves every row and value, a 1:many merge is caught before it silently drops rows, and the absorbed-table drop is the later phase`

```
Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3, Duration: 1 m 2 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact 1 — cardinality is proven 1:1 BEFORE the copy, then the copy preserves every row and value.** The
absorbed `CustomerAddress` held 25 rows across 25 distinct parents (**equal → 1:1**), with **zero** parents
carrying more than one child. Only then was `Customer.PostalCode` added and backfilled: **0** survivor rows
were left NULL, and the absorbed-vs-survivor digest matched **byte-for-byte**. Verbatim from the run:

```
baseline: Customer rows=25, CustomerAddress exists=0 (absent)
setup: production publish CREATE TABLE [dbo].[CustomerAddress] (1:1 satellite): APPLIED (Ok); seeded 25 addresses (one per Customer)
phase 1 CARDINALITY (before any copy): absorbed rows=25, distinct parents=25, parents with >1 child=0 (equal + zero = 1:1, the copy is safe)
phase 1 additive+copy: production publish ADD [dbo].[Customer].[PostalCode] (nullable): APPLIED (Ok); PostalCode column exists=1; copied 25 rows from the absorbed table
  fidelity: Customers with NULL PostalCode after the copy=0 (0 = every survivor row filled); absorbed digest=2D48598647B43217F8359DF5C83137AF2E1EB3071CC910EEBE177AE54A5A320B, survivor digest=2D48598647B43217F8359DF5C83137AF2E1EB3071CC910EEBE177AE54A5A320B (match=true)
```

**Fact 2 (the load-bearing lesson) — a 1:many merge is caught before it silently drops a row.** Injecting a
**second** address for one customer made the absorbed side one-to-many. The cardinality probe caught it
immediately (26 absorbed rows vs. 25 distinct parents; one parent with >1 child). The naive copy then
touched only **25** rows — one per parent — so **one** absorbed value never reached the survivor, and a
value-hash over the 25 survivors would still have looked complete. This is exactly why the row-count comes
first. Verbatim:

```
the 1:many counterexample: a 2nd address is added for one Customer -> absorbed rows=26, distinct parents=25, parents with >1 child=1 (UNEQUAL -> the cardinality probe CATCHES it; STOP)
  the silent loss it prevents: the naive copy touched 25 rows (one per parent), NOT the 26 absorbed rows -> 1 absorbed value(s) never reached the survivor, and a value-hash over the 25 survivors would still look complete
```

**Fact 3 — the absorbed-table drop is the guarded later phase (a phantom under the production posture).**
Because `DropObjectsNotInSource = false` (the default `sqlpackage` ships), removing `CustomerAddress` from
the project is a **phantom**: the production publish returns `Ok` and the table survives untouched — the
declarative "drop" does nothing. The real Phase-3 removal is therefore a **deliberate, reviewed scripted
`DROP TABLE`**, which discards the table and its rows irreversibly. Verbatim:

```
phase 3 subtractive (the later phase): removing CustomerAddress from the project under the production publish: APPLIED (Ok) -> CustomerAddress exists=1 rows=26 (a PHANTOM — the declarative drop leaves it in place)
  the deliberate Phase-3 act: scripted DROP TABLE [dbo].[CustomerAddress] -> exists=0 (gone; the absorbed rows go with it — irreversible without a backup)
```

Every absorbed row maps 1:1 into the survivor, every value is byte-identical, a one-to-many merge is
caught before it can lose a row, and the absorbed table's removal is a deliberate act — the safe
foundation the later cutover and drop phases depend on.

## Verification — run in each environment after deployment

```sql
-- expect absorbed_rows = distinct_parents: the absorbed side is 1:1 with the survivor, so the copy
-- carries every row (unequal = 1:many; STOP, the merge is unsafe as stated)
SELECT
  (SELECT COUNT(*)                    FROM dbo.CustomerAddress) AS absorbed_rows,
  (SELECT COUNT(DISTINCT CustomerId)  FROM dbo.CustomerAddress) AS distinct_parents;

-- expect equal hashes: PostalCode now holds the same content on the survivor as on the absorbed table
-- (run after the copy, before the Phase-3 drop)
SELECT
  CONVERT(CHAR(64), HASHBYTES('SHA2_256',
    CAST((SELECT CustomerId AS Cid, PostalCode FROM dbo.CustomerAddress ORDER BY CustomerId FOR XML RAW) AS VARBINARY(MAX))), 2) AS absorbed_hash,
  CONVERT(CHAR(64), HASHBYTES('SHA2_256',
    CAST((SELECT Id AS Cid, PostalCode FROM dbo.Customer WHERE PostalCode IS NOT NULL ORDER BY Id FOR XML RAW) AS VARBINARY(MAX))), 2) AS survivor_hash;
```

## Rollback

Before the Phase-3 drop, backing out is lossless: `ALTER TABLE dbo.Customer DROP CONSTRAINT ...; ALTER
TABLE dbo.Customer DROP COLUMN PostalCode;` and repoint reads back to `CustomerAddress`, which still holds
its data. The Phase-3 drop is **not** auto-reversible — once `CustomerAddress` is dropped it is gone, and
recovery means recreating it and copying back from `Customer.PostalCode` (whose values were proven equal to
the absorbed originals before the drop). Keep the absorbed table recoverable — a backup — until the drop is
confirmed durable. Backing the change out was not exercised.

## Not verified

- **The later phases.** Only phase 1 (add column + copy + cardinality proof) is proven here, plus the
  *demonstration* that the absorbed-table removal is a phantom. The application cutover and the final,
  deliberate `DROP TABLE dbo.CustomerAddress` are separate PRs and are **not** exercised by this proof.
- **Application impact.** The running application must dual-write into `Customer.PostalCode` during phase 1
  and read the survivor after cutover; that every reader and writer has been repointed off
  `CustomerAddress` is confirmed by the app owner, not here.
- **Other environments.** Cardinality (1:1) was proven on a disposable copy of Dev only; Test, UAT, and
  Prod may hold a one-to-many parent this copy does not — run the cardinality query before the copy in
  each environment.
- **External consumers.** An outside reference may still read `CustomerAddress` by name; the known ones are
  repointed during cutover, unknown ones are not covered.
- **Production scale / timing.** The copy and drop are exercised at seed scale only; blocking and duration
  at >1M rows are not shown by the small copy.
- **Reversibility.** Only the forward merge is proven; recreating the dropped absorbed table and copying
  back is not exercised here.
