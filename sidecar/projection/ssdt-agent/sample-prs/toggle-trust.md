# OrderLine: trust the Order reference (WITH CHECK CHECK turns an untrusted foreign key trusted once the data is clean)

**In OutSystems** — You have a reference (`OrderLine → Order`) that was turned on without checking the existing data — so it exists but the database doesn't trust it. Now that the data is clean, you want it fully trusted and enforced. "Trust the constraint now that the data is clean", "turn the FK back on."
**In SSDT** — this is **operational, not declarative**: `ALTER TABLE [dbo].[OrderLine] WITH CHECK CHECK CONSTRAINT [FK_OrderLine_Order]` re-validates the existing rows and marks the constraint trusted. It toggles the runtime trust state of an existing constraint — **not a shape SSDT converges to** — so it lives in a pre/post-deployment script or a runbook, not in a table definition.

## Summary

A foreign key added `WITH NOCHECK` **exists but is untrusted** (`is_not_trusted = 1`): the optimizer
ignores it, and the existing rows were never validated — so it protects nothing you can rely on.
Toggling it to trusted is an **operational step**, not a declarative change: `WITH CHECK CHECK`
re-validates every existing row and, if they all conform, marks the constraint trusted. The proof that
matters is the **ending trust state** (`is_not_trusted = 0`), not that the command ran.

The catch is that trust cannot be granted over dirty data: while any row violates the relationship,
`WITH CHECK CHECK` fails (`Msg 547`) and the constraint stays untrusted — so the data must be clean
first. Both faces were proven objectively against a Twin — a disposable SQL Server database published
from this estate and filled with real-shaped synthetic data. This is the same trust ladder as the
FK-with-orphans remedy (`../skills/op/create-fk-orphan/SKILL.md` and
`../skills/_index/constraint-is-a-claim/SKILL.md`). No work item was provided with the request; attach
one before merge so the record is traceable.

## Review & release

- The review need is **inherited from the change this step serves**. Where it re-trusts a foreign key
  after reconciling data (the FK-with-orphans remedy), a dev lead must review it, because existing data
  was modified.
- It ships as a **scripted change** — a constraint's trust state cannot be expressed as a table
  definition; it lives in a pre/post-deployment script or a runbook. Always flag it as
  operational / script-only.
- **Trust requires clean data first.** Prove the relationship holds (orphan probe = 0) before running
  `WITH CHECK CHECK`; over a violating row it fails and leaves the constraint untrusted. The end-state to
  insist on is `is_not_trusted = 0`.
- Added scrutiny: none for a small table; at >1M rows the `WITH CHECK CHECK` re-validation scans the
  table and may block writes or run long — schedule a window.

## Changes

| File | Change |
|---|---|
| Pre/post-deployment script (not a table definition) | `ALTER TABLE [dbo].[OrderLine] WITH CHECK CHECK CONSTRAINT [FK_OrderLine_Order]` — re-validate the existing rows and mark the foreign key trusted |

No table-definition change, no renames (the refactorlog is unchanged). This operation does not alter the
described schema shape — it changes the runtime trust state of an existing constraint.

## Data remediation

The relationship must hold before trust can be granted. Probe first:
`SELECT COUNT(*) FROM dbo.OrderLine c LEFT JOIN dbo.[Order] p ON c.OrderId = p.Id WHERE p.Id IS NULL;`.
If it returns 0, `WITH CHECK CHECK` validates and the constraint ends trusted. If it returns more than 0,
the re-validation fails (`Msg 547`) and the constraint stays untrusted — reconcile the orphans first
(that reconcile is the FK-with-orphans remedy; record originals for a manual restore), then re-run.

## Deployment evidence — objective proof, production-faithful publish, 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, establishes an untrusted
foreign key over clean data, toggles it trusted, and also proves the guard (a violating row keeps it
untrusted) — asserting the trust state before and after each step by reading `sys.foreign_keys` directly.
DacFx is the same publish engine `sqlpackage` wraps.

**Test:** `Twin.Tests.Integration.SamplePrReferenceIntegrityTests+SamplePrReferenceIntegrityTests.toggle-trust: WITH CHECK CHECK makes an untrusted FK trusted over clean data; a violating row keeps it untrusted`

```
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 1 m 10 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact 1 (primary) — over clean data, `WITH CHECK CHECK` flips untrusted → trusted.** The foreign key
existed but was **untrusted** (`is_not_trusted = 1`, added `WITH NOCHECK`) even though the data was clean
(orphan probe **0**). The `WITH CHECK CHECK` re-validation succeeded and marked it trusted. Verbatim from
the run:

```
before: FK_OrderLine_Order exists=1, orphan probe=0 (clean), is_not_trusted=1 (1 = present but ignored by the optimizer, guards nothing)
toggle-trust (ALTER TABLE ... WITH CHECK CHECK over clean data): OK -> is_not_trusted 1 -> 0
```

`is_not_trusted` moved **1 → 0** — the constraint ends trusted, validated against every existing row and
honoured by the optimizer. That transition is the whole operation.

**Fact 2 (the guard) — over a violating row, `WITH CHECK CHECK` fails and stays untrusted.** Returned to
`NOCHECK` and given a violating row (an orphan), the re-validation failed (`Msg 547`) and the constraint
stayed untrusted; only after the orphan was reconciled did it flip trusted again. Verbatim from the run:

```
guard: NOCHECK again (is_not_trusted=1), seed a violating row (orphan probe=1), then WITH CHECK CHECK: Msg 547, Line 1: The ALTER TABLE statement conflicted with the FOREIGN KEY constraint "FK_OrderLine_Order". The conflict occurred in database "twin", table "dbo.Order", column 'Id'. -> is_not_trusted=1 (still 1)
recover: reconcile the orphan, WITH CHECK CHECK again: OK -> is_not_trusted=0 (0 = trusted)
```

This is why the order is fixed: **clean the data, then trust**. Trust cannot be granted while a row
violates the relationship, and a constraint left at `NOCHECK` guards nothing.

## Verification — run in each environment after deployment

```sql
-- expect is_not_trusted = 0: the foreign key ends trusted, not merely present
SELECT name, is_not_trusted FROM sys.foreign_keys WHERE name = 'FK_OrderLine_Order';

-- expect 0 rows: the relationship holds (a precondition for trust)
SELECT c.OrderId FROM dbo.OrderLine c
LEFT JOIN dbo.[Order] p ON c.OrderId = p.Id WHERE p.Id IS NULL;
```

## Rollback

Re-running `ALTER TABLE dbo.OrderLine NOCHECK CONSTRAINT FK_OrderLine_Order;` stops enforcement again,
but a constraint returned to `NOCHECK` ends **untrusted** (`is_not_trusted = 1`) and guards nothing.
Backing out a re-trust removes protection rather than restoring a safe prior state; the untrusted state
is not a safe resting place. Backing the change out was not exercised.

## Not verified

- **Other environments.** The ending trust state is proven on a disposable copy only. A `WITH CHECK
  CHECK` that meets a violating row in another environment leaves the constraint untrusted
  (`is_not_trusted = 1`) — re-probe `is_not_trusted` after the script runs in each environment before
  relying on it.
- **The reconcile decision.** When a violating row exists, how it is fixed is a data-owner decision, not
  made here.
- **Production scale and timing.** On a large table the `WITH CHECK CHECK` re-validation may block writes
  or run long; the small copy cannot show the duration.
- **Reversibility.** Only the forward move to trusted is exercised; returning to `NOCHECK` restores the
  untrusted, unenforced state, which is not a safe resting state.
