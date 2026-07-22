# Order: promote free-text Channel into a Channel lookup + FK (phase 1 — lookup seeded, mapping proven total, FK trusted)

**In OutSystems** — You turn the free-text `Order.Channel` Attribute (values like `Web`, `Store`) into a reference to a new `Channel` **Static Entity**, so the values become a governed lookup instead of typed-in text — the way you stop typos by replacing a text field with a chosen-from-a-list reference.
**In SSDT** — a multi-step transform: **create** the `dbo.Channel` lookup (explicit-ID PK + `Code`), **seed** it with the distinct existing values via an idempotent MERGE, **add** an `Order.ChannelId` foreign-key column, and **backfill** it by joining text → lookup key. The old `Channel` text column and the new `ChannelId` FK **coexist** while readers migrate.

## Summary

This PR proves **phase 1** of a staged extraction. Like `split-table`, moving existing data into a new
shape behind a new relationship **cannot ship in one publish** — the old free-text column and the new
lookup have to coexist while every reader and writer migrates to the FK, so it stages across several
releases. What phase 1 establishes objectively, on a disposable copy, is the load-bearing safety
property: **the mapping is total.** Every distinct source value has a seeded lookup row (zero unmapped),
the backfill leaves no `Order` without a key (zero NULL `ChannelId`), every key resolves to a real
lookup row (zero orphans), and the foreign key lands **trusted**. The seed itself is idempotent — a
re-run touches zero rows with an identical content-hash. Proving totality **before** the old column is
ever dropped is what stops a value from silently becoming NULL.

**The old `Channel` text column is deliberately retained here.** Dropping it is a **later phase** (its
own PR), gated on the application having stopped reading and writing the text column — exactly the
coexistence discipline `split-table` follows.

This was proven objectively against a **Twin** — a disposable SQL Server 2022 database published from
this estate and filled with real-shaped synthetic data. Because the extraction is imperative data motion
(create, seed, add FK, backfill), it was run as scripted steps against the live Twin, the same way a
real migration script runs. No work item was provided with the request; attach one before merge so the
record is traceable.

## Review & release

- **A dev lead must review this**: existing data is moved into a new shape and a cross-table relationship
  (the lookup foreign key) is added.
- **Ships across releases (multiple PRs)**: create the lookup, seed it, add the FK column, backfill, then
  — in a *later* PR, only after the app stops using the text column — drop the old free-text column. This
  PR is the create + seed + FK + backfill phase.
- **The mapping must be proven total before the drop phase**, so nothing silently becomes NULL. If any
  environment holds a `Channel` value that was never seeded, that value has no lookup row and the drop
  would lose it — reconcile first. If a value is genuinely lost, a principal must review, because the
  removal cannot be undone.
- Added scrutiny: none for a small, clean source; at >1M rows the backfill scans the table and may block
  writes or run long — schedule a window.

## Changes

| Object | Change |
|---|---|
| `dbo.Channel` (new lookup) | `CREATE TABLE [dbo].[Channel] ([Id] INT NOT NULL, [Code] NVARCHAR(20) NOT NULL, CONSTRAINT [PK_Channel] PRIMARY KEY ([Id]))` |
| `Data/StaticSeeds.sql` | Adds the guarded MERGE seeding `Channel` with the distinct existing values `(1, Web)`, `(2, Store)` — explicit IDs |
| `dbo.Order` | Adds `[ChannelId] INT NULL` and `CONSTRAINT [FK_Order_Channel] FOREIGN KEY ([ChannelId]) REFERENCES [dbo].[Channel] ([Id])`; backfills `ChannelId` from the text join |
| `dbo.Order.Channel` | **Retained this phase** — the free-text column stays while readers migrate; its drop is a later PR |

No renames (the refactorlog is unchanged).

## Data motion

The lookup is seeded with the **distinct existing values** of `Order.Channel` under explicit IDs, then
`ChannelId` is backfilled by joining `Order.Channel = Channel.Code`. The totality gate is run **before**
the FK column is even added: `SELECT DISTINCT Channel FROM dbo.[Order] WHERE Channel NOT IN (SELECT Code
FROM dbo.Channel)` must return zero rows. On the copy it did; the backfill then left zero NULL keys and
zero orphans, so the FK validated over real data and landed trusted.

## Deployment evidence — objective proof, live Twin (SQL Server 2022), 2026-07-22

The proof is a green integration test that materializes real-shaped `Order` data on a live Twin,
establishes the free-text column's known distinct values, then runs the extraction's phase-1 steps and
asserts the totality, backfill, trust, and idempotence properties by consuming the data directly. The
content-hash is an order-sensitive `SHA2_256` over the lookup's `FOR XML RAW` projection.

**Test:** `Twin.Tests.Integration.SamplePrSeedTests+SamplePrSeedTests.extract-to-lookup (phase 1): the lookup is seeded idempotently, every distinct source value maps 1:1, and the FK is trusted`

```
Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4, Duration: 59 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact 1 — the lookup is seeded and the mapping is total.** `Order` held 25 rows across two distinct
free-text `Channel` values. The `Channel` lookup seeded the two distinct values, and **zero** distinct
source values had no lookup row — the mapping is total before anything is backfilled or dropped. Verbatim
from the run:

```
baseline: Order rows=25, free-text Channel distinct=2 (Web=13, Store=12)
step 1-2: created dbo.Channel, seeded via guarded MERGE -> rows affected=2, lookup rows=2, content-hash=2012B10408B3409643DACC5567CA45C7B943F4325FFA0E92B707B6C12A17505D
step 3 (totality, pre-backfill): distinct source values with NO lookup row = 0 (0 = the mapping is total; nothing becomes NULL)
```

**Fact 2 — the backfill is complete and the FK is trusted; the old column is retained.** After adding
`ChannelId` + the FK and backfilling all 25 rows, **zero** rows had a NULL key, **zero** keys were
orphans, and the FK landed with `is_not_trusted = 0` (validated and honoured by the optimizer). The old
`Channel` text column is still present — its drop is a later phase. Verbatim:

```
step 4-5: added Order.ChannelId + FK_Order_Channel, backfilled 25 rows -> ChannelId IS NULL=0, orphan ChannelId=0, FK is_not_trusted=0 (0 = trusted)
  old free-text column dbo.Order.Channel still present=1 (RETAINED - the drop is a later phase)
```

**Fact 3 (the seed's signature proof) — the lookup seed is idempotent.** The identical seed MERGE, re-run
against the already-seeded lookup, touched **0 rows** and left a **byte-identical content-hash**:

```
SECOND run of the identical lookup MERGE: rows affected=0 (0 = idempotent), content-hash=2012B10408B3409643DACC5567CA45C7B943F4325FFA0E92B707B6C12A17505D (identical=true)
```

Every distinct source value maps 1:1 into the lookup (0 unmapped, 0 orphan), the FK is trusted, and the
seed is silent on re-run — the safe foundation the later column-drop phase depends on.

## Verification — run in each environment after deployment

```sql
-- expect 0 rows: every source value maps to a seeded lookup row (the mapping is total)
SELECT DISTINCT Channel FROM dbo.[Order] WHERE Channel NOT IN (SELECT Code FROM dbo.Channel);

-- expect 0 rows: the backfill left no order without a lookup key
SELECT * FROM dbo.[Order] WHERE ChannelId IS NULL;

-- expect one row, is_not_trusted = 0: the foreign key landed trusted
SELECT name, is_not_trusted FROM sys.foreign_keys WHERE name = 'FK_Order_Channel';
```

## Rollback

This phase is additive to the source table (it adds a column and an FK; the text column stays), so it
backs out without data loss: `ALTER TABLE dbo.[Order] DROP CONSTRAINT FK_Order_Channel; ALTER TABLE
dbo.[Order] DROP COLUMN ChannelId; DROP TABLE dbo.Channel;` — the original `Channel` text is untouched
throughout. The *later* column-drop phase is the one that needs the total-mapping proof to be reversible
(the text is reconstructable from the FK → `Code` join). Backing the change out was not exercised.

## Not verified

- **The later phases.** Only phase 1 (create + seed + FK + backfill) is proven here. The application
  cutover (readers/writers moving to the FK) and the final free-text-column **drop** are separate PRs and
  are **not** exercised by this proof.
- **Application impact.** Application code that reads or writes `Order.Channel` directly, rather than
  through the new FK, keeps working *this* phase (the column is retained) but breaks once it is dropped
  later; that every reader and writer has moved is confirmed by the app owner, not here.
- **Other environments.** The distinct source values were enumerated on a disposable copy of Dev only;
  Test, UAT, and Prod may hold `Channel` values never seeded into the lookup — run the total-mapping query
  in each before promotion, and before the later drop phase.
- **Production scale / timing.** The seed and backfill are exercised at seed scale only; blocking and
  duration at >1M rows are not shown by the small copy.
- **Reversibility.** Only the forward phase-1 transform is proven; re-adding and re-backfilling a dropped
  column from the lookup join is a concern of the later phase and is not exercised here.
