# OrderLine: add a reference to Order over dirty data (an orphan blocks validation and leaves the foreign key untrusted; reconcile then re-validate to trusted)

**In OutSystems** — You add a reference Attribute so each `OrderLine` belongs to an `Order` — the same "draw the relationship" change as any reference — but some existing `OrderLine` records point at an `Order` that no longer exists (orphans).
**In SSDT** — a `CONSTRAINT [FK_OrderLine_Order] FOREIGN KEY ([OrderId]) REFERENCES [dbo].[Order] ([Id])` is added to `Tables/dbo.OrderLine.sql`. The publish engine validates it against **every existing child row** — and an orphan makes that validation fail.

## Summary

You add the reference from `OrderLine` to `Order`, but the data is dirty: at least one `OrderLine`
points at an `Order` that does not exist. A foreign key is a **claim about the existing data**, and SQL
Server proves it as the constraint lands. The important finding is *how* SSDT tries to add it and what a
failed attempt leaves behind: it adds the constraint **`WITH NOCHECK`** (skipping validation), then runs
a separate **`WITH CHECK CHECK`** to validate the existing rows. Over an orphan that second step fails
(`Msg 547`) and the publish is refused — but the constraint **lingers, untrusted**
(`is_not_trusted = 1`): the optimizer ignores it and the orphan is still there.

So the honest path is a scripted change in a single release: add `WITH NOCHECK`, **reconcile the
orphans** (repoint, insert the missing parent, or delete), then **`WITH CHECK CHECK`** to validate and
end **trusted** — never leave it at `WITH NOCHECK`. This was proven objectively against a Twin — a
disposable SQL Server database published from this estate and filled with real-shaped synthetic data —
with a **production-faithful** publish (`BlockOnPossibleDataLoss = true`), and the full remedy was run to
its trusted end state (see the trust ladder in `../skills/op/toggle-trust/SKILL.md` and
`../skills/_index/constraint-is-a-claim/SKILL.md`). No work item was provided with the request; attach
one before merge so the record is traceable.

## Review & release

- A dev lead must review this: existing data is modified (the orphans are reconciled) and a cross-table
  relationship is added. If the reconcile **deletes** child rows, a principal must review it — removed
  data cannot be undone.
- It does **not** ship as a single declarative change. It ships as a **scripted change in one release**:
  the foreign key is added `WITH NOCHECK`, the orphans are reconciled, then re-validated `WITH CHECK
  CHECK` to end trusted — a reconcile that cannot be expressed as a table definition. If orphans are
  still being created by the running application, it ships **across releases** so the application keeps
  working while the change is in flight.
- Do **not** ship it `WITH NOCHECK` to "get past" the block: an untrusted foreign key guards nothing the
  optimizer relies on, and the orphans remain. `is_not_trusted = 0` is the end-state proof.
- Added scrutiny: first time this operation is proven on the Twin; at production row counts the
  `WITH CHECK CHECK` re-validation scans the table and may block writes or run long — schedule a window.

## Changes

| File | Change |
|---|---|
| `Tables/dbo.OrderLine.sql` | Adds `CONSTRAINT [FK_OrderLine_Order] FOREIGN KEY ([OrderId]) REFERENCES [dbo].[Order] ([Id])` to the table definition |
| Pre/post-deployment script | Adds the FK `WITH NOCHECK`, reconciles the orphaned `OrderLine` rows, then `ALTER TABLE ... WITH CHECK CHECK CONSTRAINT [FK_OrderLine_Order]` to end trusted |

No renames (the refactorlog is unchanged). No index, view, or procedure changes.

## Data remediation

Probe first: `SELECT COUNT(*) FROM dbo.OrderLine c LEFT JOIN dbo.[Order] p ON c.OrderId = p.Id WHERE
p.Id IS NULL;`. If it returns more than 0, the declarative foreign key is blocked at the `WITH CHECK
CHECK` validation. The remedy is the reconcile: repoint each orphan to a real `Order`, insert the missing
`Order`, or delete the orphaned `OrderLine` — a data-owner decision (named under Not verified) — then
re-validate. The original child values changed by a reconcile must be recorded for a manual restore.

On the proof substrate the whole sequence was observed directly, ending trusted:
- With **1** orphan present, the production publish was **refused** at `WITH CHECK CHECK` (Msg 547) and
  the constraint was left **untrusted** (`is_not_trusted = 1`).
- `WITH CHECK CHECK` **while the orphan remained** failed again (Msg 547) and stayed untrusted — trust
  cannot be granted over violating data.
- After the orphan was **reconciled** (orphan probe back to **0**), `WITH CHECK CHECK` succeeded and the
  foreign key ended **trusted** (`is_not_trusted = 0`).

## Deployment evidence — objective proof, production-faithful publish, 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, seeds an orphan in
real-shaped data, attempts the add under a **production-faithful** DacFx posture
(`BlockOnPossibleDataLoss = true`, no smart-defaults), then runs the reconcile-and-retrust remedy to its
trusted end state. DacFx is the same publish engine `sqlpackage` wraps.

**Test:** `Twin.Tests.Integration.SamplePrReferenceIntegrityTests+SamplePrReferenceIntegrityTests.create-fk-orphan: an orphan blocks the FK at WITH CHECK CHECK and leaves it untrusted; reconcile then re-validate to trusted`

```
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 1 m 10 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact 1 (the block) — an orphan REFUSES the foreign key at validation, and leaves it untrusted.** One
`OrderLine` pointed at a non-existent `Order` (orphan probe **1**). The production-faithful publish was
refused; the failing statement is the `WITH CHECK CHECK` re-validation, not the `ADD`. Verbatim from the
run:

```
before: orphan probe (OrderLine->Order)=1 (a child points at an Order that does not exist)
production publish ADD CONSTRAINT [FK_OrderLine_Order] over an orphan: REFUSED at the WITH CHECK CHECK re-validation (Msg 547).
  after block: FK exists=1, is_not_trusted=1 (1 = added WITH NOCHECK but never validated -> untrusted; -1 = rolled back / absent)
  strict detail:
Could not deploy package.
Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 547, Level 16, State 0, Line 1 The ALTER TABLE statement conflicted with the FOREIGN KEY constraint "FK_OrderLine_Order". The conflict occurred in database "twin", table "dbo.Order", column 'Id'.
Error SQL72045: Script execution error.  The executed script:
ALTER TABLE [dbo].[OrderLine] WITH CHECK CHECK CONSTRAINT [FK_OrderLine_Order];
```

The constraint was added `WITH NOCHECK` (so it exists) and the `WITH CHECK CHECK` validation then failed
on the orphan — leaving the foreign key present but **untrusted** (`is_not_trusted = 1`). The deployment
did not achieve a trusted, enforced relationship.

**Fact 2 (the remedy) — reconcile, then re-validate to trusted.** With the constraint present `WITH
NOCHECK` (untrusted), `WITH CHECK CHECK` **while the orphan remained** failed again (Msg 547) and stayed
untrusted; only after the orphan was reconciled did the re-validation succeed and flip it trusted.
Verbatim from the run:

```
remedy step 1 (constraint present WITH NOCHECK): is_not_trusted=1
remedy step 2 (WITH CHECK CHECK while the orphan remains): Msg 547, Line 1: The ALTER TABLE statement conflicted with the FOREIGN KEY constraint "FK_OrderLine_Order". The conflict occurred in database "twin", table "dbo.Order", column 'Id'. -> is_not_trusted=1 (still untrusted)
remedy step 3 (reconcile -> orphan probe=0 -> WITH CHECK CHECK): OK -> is_not_trusted=0 (0 = trusted)
```

`is_not_trusted = 0` — the foreign key is validated against every row and honoured by the optimizer. The
sequence is the whole point: **NOCHECK → reconcile → WITH CHECK CHECK**, and trust is only granted once
the orphan probe reads 0.

## Verification — run in each environment after deployment

```sql
-- expect 0 rows: every child points at a parent that exists
SELECT c.OrderId FROM dbo.OrderLine c
LEFT JOIN dbo.[Order] p ON c.OrderId = p.Id WHERE p.Id IS NULL;

-- expect one row, is_not_trusted = 0: the foreign key is validated and honoured by the optimizer
SELECT name, is_not_trusted FROM sys.foreign_keys WHERE name = 'FK_OrderLine_Order';
```

## Rollback

The foreign key drops without data loss:

```sql
ALTER TABLE dbo.OrderLine DROP CONSTRAINT FK_OrderLine_Order;
```

This is also the cleanup for an **untrusted** constraint left by a blocked attempt. The orphan reconcile
is **not** auto-reversed — the original child values recorded under Data remediation are what a manual
restore uses. Backing the change out was not exercised.

## Not verified

- **Application impact.** Once the foreign key is trusted, any insert or update that points an
  `OrderLine` at a missing `Order` is rejected with error 547; application-side validation is not
  confirmed here — the application owner owns it.
- **The reconcile decision.** How the orphans are fixed — repoint, insert the missing parent, or delete —
  is a data-owner decision, not made here. If it deletes rows, that is irreversible.
- **Other environments.** The orphan count was proven on a disposable copy only; Test, UAT, and Prod may
  hold a different number of orphans this copy cannot see — run the orphan probe in each before
  promotion.
- **Production scale and timing.** The `WITH CHECK CHECK` re-validation and the reconcile are exercised
  at seed scale only; blocking and duration at production row counts are not shown by the small copy.
- **Reversibility.** Only the forward path is proven (block, then reconcile-to-trusted); backing the
  reconcile out is not exercised, and the recorded originals are what a manual restore would use.
