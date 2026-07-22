# OrderLine: add a reference to Order (a foreign key over clean child data applies in place and lands trusted)

**In OutSystems** — You add a reference Attribute so each `OrderLine` belongs to an `Order` — drawing the relationship from the child Entity to its parent, the way "an Order belongs to a Customer" ties two Entities together.
**In SSDT** — a `CONSTRAINT [FK_OrderLine_Order] FOREIGN KEY ([OrderId]) REFERENCES [dbo].[Order] ([Id])` is added to `Tables/dbo.OrderLine.sql`. The publish engine validates that relationship against **every existing child row** as it lands — and the data decides whether it can.

## Summary

You add the reference from `OrderLine` to `Order`. A foreign key is a **claim about the existing
data**: SQL Server checks every child row against a real parent when the constraint is added. If every
`OrderLine` already points at an `Order` that exists — no **orphans** — it lands clean, in a single
change, and **trusted** (the optimizer honours it). If even one orphan exists, that same declarative
foreign key is blocked at deploy, and the change becomes a reconcile-then-retrust script
(`../skills/op/create-fk-orphan/SKILL.md`). So you read what will happen from the **orphan probe**,
never from the `.sql`.

This was proven objectively against a Twin — a disposable SQL Server database published from this estate
and filled with real-shaped synthetic data — with a **production-faithful** publish
(`BlockOnPossibleDataLoss = true`, the deployment a real environment runs). On the copy every child
already had a parent, so the foreign key applied in place and landed trusted with nothing to reconcile.
No work item was provided with the request; attach one before merge so the record is traceable.

## Review & release

- A dev lead should review this: a cross-table relationship is added, and the running application's
  behaviour changes — a write that points an `OrderLine` at a non-existent `Order` is now rejected.
- It ships as a **single schema change, applied in place** — one `ADD CONSTRAINT` that validates every
  existing child row against the parent. No existing data is read for change or written.
- **Prove zero orphans first.** If the orphan probe returns more than 0 in any environment, this
  operation does not apply there — it becomes `create-fk-orphan` (add `WITH NOCHECK`, reconcile the
  orphans, then `WITH CHECK CHECK` to end trusted).
- Added scrutiny: none for a small parent; at >1M parent rows the validation scan may block writes or
  run long — schedule a window.

## Changes

| File | Change |
|---|---|
| `Tables/dbo.OrderLine.sql` | Adds `CONSTRAINT [FK_OrderLine_Order] FOREIGN KEY ([OrderId]) REFERENCES [dbo].[Order] ([Id])` to the table definition |

No renames (the refactorlog is unchanged). No index, view, or procedure changes; only the new foreign
key is added — no column values are touched.

## Data remediation

None here — but the orphan probe is the gate, so run it first:
`SELECT COUNT(*) FROM dbo.OrderLine c LEFT JOIN dbo.[Order] p ON c.OrderId = p.Id WHERE p.Id IS NULL;`.
On the Twin it returned **0** (the child data was minted under the relationship, so every `OrderLine`
points at a real `Order`), which is why the foreign key applied clean and trusted with nothing to fix.
If that probe returns more than 0 in Test, UAT, or Prod, the deploy will be blocked there and the change
routes to `create-fk-orphan` — reconcile the orphans before the constraint can be honest.

## Deployment evidence — objective proof, production-faithful publish, 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, mints real-shaped data
under the relationship, removes the foreign key to establish a faithful before-state, then adds it back
and asserts the outcome under a **production-faithful** DacFx posture (`BlockOnPossibleDataLoss = true`,
no smart-defaults). DacFx is the same publish engine `sqlpackage` wraps.

**Test:** `Twin.Tests.Integration.SamplePrReferenceIntegrityTests+SamplePrReferenceIntegrityTests.create-fk-clean: an FK over clean child data applies in place and lands trusted`

```
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 1 m 10 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact — the foreign key applies in place and lands trusted; a bad write is now rejected.** `OrderLine`
held **25 rows**, every one pointing at a real `Order` (orphan probe **0**). With no foreign key
declared, a child pointing at a missing `Order` was **allowed** (SQL error 0); after the
production-faithful publish added the constraint, the identical bad write was **rejected** (Msg 547).
Verbatim from the run:

```
baseline (minted WITH the relationship): OrderLine rows=25, FK_OrderLine_Order exists=1, orphan probe (OrderLine->Order)=0
before (no FK declared): FK exists=0, orphan probe=0, inserting a child pointing at a missing Order returned SQL error=0 (0 = ALLOWED, nothing guards it yet)
production publish (BlockOnPossibleDataLoss=true) ADD CONSTRAINT [FK_OrderLine_Order]: APPLIED (Ok)
  after apply: FK exists=1, is_not_trusted=0 (0 = validated and honored by the optimizer), orphan probe=0, OrderLine rows=25 (was 25, intact)
  going forward: inserting a child pointing at a missing Order now returns SQL error=547 (547 = rejected by the foreign key)
```

`is_not_trusted = 0` — the foreign key exists, is validated against every existing row, and the
optimizer honours it. The publish returned `Ok` under the production-faithful posture, no row was
touched (**25 → 25**), and the referential guarantee is now enforced going forward (the orphan write
that was allowed before now returns Msg 547).

## Verification — run in each environment after deployment

```sql
-- expect 0 rows: every child points at a real parent
SELECT c.OrderId FROM dbo.OrderLine c
LEFT JOIN dbo.[Order] p ON c.OrderId = p.Id WHERE p.Id IS NULL;

-- expect one row, is_not_trusted = 0: the foreign key landed trusted
SELECT name, is_not_trusted FROM sys.foreign_keys WHERE name = 'FK_OrderLine_Order';
```

## Rollback

Lossless — the foreign key drops without touching any row:

```sql
ALTER TABLE dbo.OrderLine DROP CONSTRAINT FK_OrderLine_Order;
```

No data was modified, so nothing else is reversed. Backing the change out was not exercised.

## Not verified

- **Application impact.** Any insert or update that points an `OrderLine` at an `Order` that does not
  exist is now rejected with error 547; application-side validation is not confirmed here — the
  application owner owns it.
- **Other environments.** The orphan probe was proven on a disposable copy only; Test, UAT, and Prod may
  hold orphans this copy cannot see. Run the orphan probe in each before promotion — if it returns more
  than 0, the deploy is blocked there and the change becomes `create-fk-orphan`.
- **Production scale and timing.** On a large parent the validation scan's duration and locking are not
  shown by the small copy; schedule a window.
- **Reversibility.** The forward add is proven (clean apply, trusted); backing it out was not exercised.
