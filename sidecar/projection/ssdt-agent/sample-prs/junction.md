# OrderTag: make Order and Tag many-to-many (a new bridge Entity with two references and a composite identifier)

**In OutSystems** — You make `Order` and `Tag` a many-to-many: an Order can carry many Tags and a Tag can be on many Orders. In OutSystems that's a junction (bridge) Entity — a small Entity whose identifier is the two references together, one pointing at each side.
**In SSDT** — a new file `Tables/dbo.OrderTag.sql` holds a single `CREATE TABLE [dbo].[OrderTag]` whose **composite primary key spans its two foreign-key columns** (`OrderId → Order`, `TagId → Tag`), plus a small new `Tables/dbo.Tag.sql` parent. The bridge does not exist yet, so SSDT emits the `CREATE TABLE` verbatim — there is nothing to transition.

## Summary

You add an `OrderTag` bridge so `Order` and `Tag` form a many-to-many. The shape carries the whole
guarantee: the **composite primary key over the two foreign keys** is what makes it a real
many-to-many — the key spanning both columns stops the same pair being recorded twice, and the two
foreign keys stop a pair from pointing at an `Order` or a `Tag` that doesn't exist. It is a
`create-entity` with two inbound relationships and one composite key; no existing data is read or
written.

This was proven objectively against a Twin — a disposable SQL Server database published from this estate
and filled with real-shaped synthetic data — with a **production-faithful** publish
(`BlockOnPossibleDataLoss = true`, the deployment a real environment runs). Both parents were present,
the bridge was created empty, and both foreign keys landed **trusted**; seeded valid pairs kept both
orphan legs at zero and a duplicate pair was rejected by the composite key. The one thing to decide is
whether you seed any initial pairs — if so, every pair needs a real row on **both** sides, or the create
is blocked (route to `../skills/op/create-fk-orphan/SKILL.md`). No work item was provided with the
request; attach one before merge so the record is traceable.

## Review & release

- A dev lead must review this: it adds **two cross-table relationships**. The change itself is additive —
  two brand-new tables nothing yet depends on — so the running application is otherwise unaffected.
- It ships as a **single schema change, applied in place** — one `CREATE TABLE` for the `Tag` parent and
  one for the `OrderTag` bridge (composite primary key over its two foreign-key columns). No existing
  data is read or written.
- **If the bridge ships with seed pairs**, prove both sides carry no orphan pairs first. A pair pointing
  at a missing `Order` or `Tag` blocks the create; it then becomes a reconcile path
  (`create-fk-orphan`). Created empty, as here, there is nothing to reconcile.
- Added scrutiny: none for small parents; at >1M rows in either parent the foreign-key validation scans
  both and may block writes or run long — schedule a window.

## Changes

| File | Change |
|---|---|
| `Tables/dbo.Tag.sql` | Adds `CREATE TABLE [dbo].[Tag]` — `[Id]` IDENTITY primary key, `[Name]` |
| `Tables/dbo.OrderTag.sql` | Adds `CREATE TABLE [dbo].[OrderTag]` — composite `CONSTRAINT [PK_OrderTag] PRIMARY KEY ([OrderId], [TagId])`, `FK_OrderTag_Order` to `dbo.Order`, `FK_OrderTag_Tag` to `dbo.Tag` |

No renames (the refactorlog is unchanged). No existing table is touched — the only objects added are the
two new tables and their constraints.

## Data remediation

None. The bridge is created **empty**; there is no existing data to fill or reconcile, and both foreign
keys validate over an empty child, so they land **trusted** with nothing to fix. If seed pairs are added
later, probe both legs before deploy — every pair must have a real parent on both sides:

```sql
SELECT b.OrderId FROM dbo.OrderTag b LEFT JOIN dbo.[Order] p ON p.Id = b.OrderId WHERE p.Id IS NULL;
SELECT b.TagId   FROM dbo.OrderTag b LEFT JOIN dbo.Tag t   ON t.Id = b.TagId   WHERE t.Id IS NULL;
```

## Deployment evidence — objective proof, production-faithful publish, 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, materializes
real-shaped data in the existing tables, adds the `Tag` parent and the `OrderTag` bridge as new estate
files, and asserts the outcome under a **production-faithful** DacFx posture
(`BlockOnPossibleDataLoss = true`, no smart-defaults). DacFx is the same publish engine `sqlpackage`
wraps.

**Test:** `Twin.Tests.Integration.SamplePrReferenceIntegrityTests+SamplePrReferenceIntegrityTests.junction: a new bridge with two FKs and a composite PK applies clean, both legs trusted`

```
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 1 m 10 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact — the bridge is created clean, both references land trusted, and the composite key holds.** The
parent `Order` held **25 rows** and neither `Tag` nor `OrderTag` existed. The production-faithful publish
created both tables; both foreign keys landed **trusted** (`is_not_trusted = 0`), the composite primary
key exists over **2 key columns** (the pair), and the bridge was **empty** (the strict publish creates
schema and does not mint). Seeded valid pairs kept both orphan legs at zero, and a duplicate pair was
rejected by the composite key. Verbatim from the run:

```
baseline: Order rows=25, dbo.Tag exists=0, dbo.OrderTag exists=0
production publish (BlockOnPossibleDataLoss=true) CREATE TABLE dbo.Tag + dbo.OrderTag (composite PK over two FKs): APPLIED (Ok)
  after apply: Tag exists=1, OrderTag exists=1, FK_OrderTag_Order exists=1 is_not_trusted=0, FK_OrderTag_Tag exists=1 is_not_trusted=0
  composite PK present=1, PK key columns=2 (2 = the pair), OrderTag rows=0 (created empty; strict publish does not mint), orphan legs: Order=0 Tag=0
seeded valid pairs: OrderTag rows=5, orphan legs after seeding: Order=0 Tag=0
  duplicate pair rejected by the composite PK: Msg 2627, Line 1: Violation of PRIMARY KEY constraint 'PK_OrderTag'. Cannot insert duplicate key in object 'dbo.OrderTag'. The duplicate key value is (1, 1).
The statement has been terminated.
```

Both `is_not_trusted = 0` — each reference is validated and honoured by the optimizer. The composite
primary key spans both columns (`PK key columns = 2`), so the same `(OrderId, TagId)` pair cannot be
recorded twice — proven directly: a duplicate insert was rejected with **Msg 2627**. Five seeded pairs
each pointed at a real `Order` and a real `Tag` (both orphan legs **0**).

## Verification — run in each environment after deployment

```sql
-- expect 0 rows from each: every bridge pair points at a real parent on both sides
SELECT b.OrderId FROM dbo.OrderTag b LEFT JOIN dbo.[Order] p ON p.Id = b.OrderId WHERE p.Id IS NULL;
SELECT b.TagId   FROM dbo.OrderTag b LEFT JOIN dbo.Tag t   ON t.Id = b.TagId   WHERE t.Id IS NULL;

-- expect two rows, is_not_trusted = 0: both references landed trusted
SELECT name, is_not_trusted FROM sys.foreign_keys WHERE name IN ('FK_OrderTag_Order','FK_OrderTag_Tag');

-- expect 2: the composite primary key spans both foreign-key columns
SELECT COUNT(*) FROM sys.index_columns ic
JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
WHERE i.object_id = OBJECT_ID('dbo.OrderTag') AND i.is_primary_key = 1;
```

## Rollback

Remove `Tables/dbo.OrderTag.sql` (and `Tables/dbo.Tag.sql` if it is not otherwise used) from the project
and republish; SSDT emits `DROP TABLE [dbo].[OrderTag];` (then `dbo.Tag`). Lossless **only while the
bridge is unwritten** — it is created empty, so until the application writes pairs into it, dropping it
discards nothing. Once pairs are written, dropping the table discards them. Backing the change out was
not exercised.

## Not verified

- **Application impact.** A brand-new bridge nothing yet reads or writes does not change existing
  behaviour; any application code that writes pairs is not exercised here. Once live, a pair pointing at
  a missing parent is rejected (error 547) and a duplicate pair is rejected by the composite key (error
  2627) — the application owner owns confirming the write paths.
- **Other environments.** If the bridge ships with seed pairs, Test, UAT, and Prod may hold parent rows
  this copy cannot see — run the orphan-leg probes before promotion.
- **Production scale and timing.** At >1M rows in either parent the foreign-key validation's duration
  and locking are not shown by the small copy.
- **Reversibility.** The forward create is proven (empty, both legs trusted, composite key enforced);
  once pairs are written, dropping the bridge is lossy (see Rollback).
