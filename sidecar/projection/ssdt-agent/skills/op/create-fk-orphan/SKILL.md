---
name: create-fk-orphan
description: Use when the developer says "add a reference to Customer" but the data is dirty — some child rows point at parents that do not exist (orphans). The NOCHECK → reconcile → WITH CHECK CHECK script path that ends with a TRUSTED foreign key.
---

# Create a foreign key with orphans (NOCHECK → reconcile → WITH CHECK CHECK)

> **Default (provisional — the data decides).** Ships as a scripted change in a single release — the orphans force a reconcile before the constraint can be honest, and that reconcile cannot be expressed as a table definition. A dev lead must review this: existing data is modified and a cross-table relationship is added. Prove before you classify. If the reconcile must stage across releases, it ships across releases instead, so the running application keeps working while the change is in flight.

## OutSystems phrasing
Same as create-fk-clean ("add a reference to Customer", "Order belongs to a Customer"), but some child rows point at parents that do not exist.

## SSDT meaning
The clean declarative FK would be blocked. The script path: add the constraint `WITH NOCHECK` (constraint exists but **untrusted**) → reconcile the orphans (delete them, repoint them, or insert the missing parents) → `ALTER TABLE ... WITH CHECK CHECK CONSTRAINT [FK_...]` to validate and **restore trust** so the optimizer honors it.

## The named trap
Stopping at `WITH NOCHECK` — the constraint is present but **untrusted** (`is_not_trusted = 1`), protecting nothing and ignored by the optimizer. The `WITH CHECK CHECK` re-validation is mandatory. This is the NOCHECK→reconcile→re-trust ladder owned by the constraint-is-a-claim family — see `../../_index/constraint-is-a-claim/SKILL.md`; do not re-derive the trust mechanics here.

## How it flips (the specifics only)
- orphans reconcilable in one release → ships as a scripted change in a single release; a dev lead reviews it, because existing data is modified.
- reconcile must wait on an app change (orphans still being created) → ships across releases so the running application keeps working while the change is in flight; a coexistence concern (see `../../_index/multi-phase/SKILL.md`).
- orphan reconcile **deletes** child rows → data is removed and cannot be undone; a principal must review it.
- CDC-enabled, or >1M rows → added scrutiny: the change-data-capture stream needs handling, or at >1M rows the `WITH CHECK CHECK` re-validation scans the table and may block writes or run long (see `../../_index/cdc/SKILL.md`).

## Prove it
First prove that the clean FK is blocked, and by how much — the orphan count via `LEFT JOIN ... WHERE p.<pk> IS NULL`. Then prove the full script on a disposable copy of Dev: `NOCHECK` adds the constraint untrusted (`SELECT is_not_trusted FROM sys.foreign_keys WHERE name='FK_...'` → 1), the reconcile clears the orphans, and `WITH CHECK CHECK` flips it back to trusted (`is_not_trusted = 0`) without being blocked. That trusted re-validation is the proof. See `../../prove-on-dacpac/SKILL.md` + `../../talk-to-local-sql/SKILL.md`. Seed: the `Order.CustomerId=999` orphan drives the whole sequence.

## The verdict (to the developer)
You asked to add a reference from Order to Customer. On a disposable copy of Dev, SSDT refused it: 8 Orders point at Customers that don't exist, and it won't validate the constraint while those orphans are there. So this release it's a scripted change — add the constraint without checking, fix the 8 orphans, then re-validate so the foreign key ends trusted. I ran that whole sequence on the copy; it ends with a trusted key. A dev lead should review it, because it changes existing data. The call that's yours: how should the 8 orphans be fixed — repoint them to a real Customer, add the missing Customers, or delete the orphaned Orders?

## The reasoning (in conversation)
"The constraint exists" is not "the constraint is trusted" — after any `NOCHECK` shortcut, prove `is_not_trusted = 0` or you haven't finished. See `../../_index/constraint-is-a-claim/SKILL.md`. The failure this avoids: shipping a silent untrusted foreign key that guards nothing.

## On the record
The fragment this op contributes to the pull request (`../../author-pr/SKILL.md`).

**Review & release**
- A dev lead must review this: existing data is modified (the orphans are reconciled) and a cross-table relationship is added. If the reconcile deletes child rows, a principal must review this: data is removed and the removal cannot be undone.
- Ships as a scripted change in a single release — the foreign key is added `WITH NOCHECK`, the orphans are reconciled, then re-validated `WITH CHECK CHECK` to end trusted; this reconcile cannot be expressed as a table definition. If orphans are still being created by the running application, it ships across releases instead.
- Added scrutiny: none for a small table; at >1M rows the `WITH CHECK CHECK` re-validation scans the table and may block writes or run long (schedule a window); a CDC-tracked table freezes its capture instance to the current columns and needs handling (see `../../_index/cdc/SKILL.md`).

**Verification — run in each environment after deployment**
```sql
-- expect 0 rows: every child points at a parent that exists
SELECT c.<fk> FROM child c LEFT JOIN parent p ON c.<fk> = p.<pk> WHERE p.<pk> IS NULL;

-- expect one row, is_not_trusted = 0: the foreign key is validated and honored by the optimizer
SELECT name, is_not_trusted FROM sys.foreign_keys WHERE name = 'FK_<child>_<parent>';
```

**Rollback**
The foreign key drops without data loss: `ALTER TABLE <child> DROP CONSTRAINT FK_<child>_<parent>;`. The orphan reconcile is not auto-reversed — the original child values (the seeded `Order.CustomerId=999` and any others) are recorded in the remediation for a manual restore.

**Not verified**
- Application impact: once the foreign key is trusted, an insert or update that points a child at a parent that does not exist is rejected with error 547; application-side validation is not confirmed here.
- Other environments: the orphan count was proven on a disposable copy of Dev only; Test, UAT, and Prod may hold a different number of orphans this copy cannot see — run the verification query before promotion.
- Production scale: the `WITH CHECK CHECK` re-validation and the reconcile are exercised at seed scale only; blocking and duration at production row counts are not shown by the small copy.
- Reversibility: only the forward path is proven; backing the reconcile out is not exercised, and the recorded originals are what a manual restore would use.
