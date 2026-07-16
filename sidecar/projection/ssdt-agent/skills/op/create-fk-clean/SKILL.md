---
name: create-fk-clean
description: Use when the developer says "add a reference to Customer", "draw the relationship from Order to Customer", "Order belongs to a Customer" AND the child data is clean (every child points at a real parent) — a FOREIGN KEY that SQL Server validates against every existing child row and lands as one clean ADD CONSTRAINT.
---

# Create a foreign key, clean data (Forgotten FK Check)

> **Default (provisional — prove before you classify).** Ships as a single schema change, applied in place — one `ADD CONSTRAINT` that validates every existing child row against the parent; no data is modified. A dev lead must review this: a cross-table relationship is added. Prove zero orphans first. If orphans exist, this op does not apply — route to `../create-fk-orphan/SKILL.md`.

## OutSystems phrasing
"add a reference to Customer", "draw the relationship from Order to Customer", "Order belongs to a Customer".

## SSDT meaning
`CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer]([CustomerId])`. SSDT emits `ALTER TABLE ... ADD CONSTRAINT ...` and SQL Server **validates every existing child row** against the parent. All `Order.CustomerId` present in `Customer` → lands clean.

## The named trap
**Forgotten FK Check** (handbook file 16 = §19.3): adding the FK without probing for **orphans** (child rows whose parent key does not exist) — the clean declarative FK is then blocked at deploy when SQL Server validates the child rows. Dodging with `WITH NOCHECK` leaves an untrusted constraint (`is_not_trusted = 1`). This is the constraint-is-a-claim family — the deploy is blocked by a violating value, not by row presence — see `../../_index/constraint-is-a-claim/SKILL.md`; the orphan-reconcile-retrust path lives in `../create-fk-orphan/SKILL.md`.

## How it flips (the specifics only)
- no orphan rows → lands as one `ADD CONSTRAINT`, applied in place; inserts and updates are now validated against the parent and a new inter-table dependency exists. A dev lead reviews it: a cross-table relationship is added.
- orphan rows present → SQL Server's validation blocks the deploy → this becomes a reconcile-then-retrust change; route to `../create-fk-orphan/SKILL.md`.
- parent table large → validation scans the parent → added scrutiny at >1M rows: the scan may block writes or run long, so schedule a window.
- CDC-enabled → the FK itself has no capture impact; coordinate if either side is mid-migration (see `../../_index/cdc/SKILL.md`).

## Prove it
Run the orphan probe FIRST: `SELECT COUNT(*) FROM child c LEFT JOIN parent p ON c.<fk> = p.<pk> WHERE p.<pk> IS NULL`. Then Strict publish: clean data → the delta is one `ADD CONSTRAINT`; orphans → the Strict publish is blocked and reports the orphan count and the offending child rows, and the op changes. See `../../prove-on-dacpac/SKILL.md` (publish loop) + `../../talk-to-local-sql/SKILL.md` (probe). Seed: the clean Order→Customer rows are the positive; the seeded orphan `Order.CustomerId=999` flips it to a blocked deploy and routes to create-fk-orphan; OrderLine→Order gives FK-graph depth (KEY-01).

## The verdict (to the developer)
"You asked to add the reference from Order to Customer. On a disposable copy of your data, every Order already points at a real Customer — no orphans — so SQL Server validates them all and the foreign key lands clean, in a single change with nothing to reconcile first. One thing changes going forward: an insert or update that points an Order at a Customer that doesn't exist will now be rejected."

## The reasoning (in conversation)
A foreign key is the clearest case of the data deciding, not the script: the exact same `ADD CONSTRAINT` text lands as one clean change when every child has a parent, and is blocked at deploy the moment a single orphan exists. So you read what will happen from the orphan count, never from the `.sql` — which is why the probe runs before anything is classified. See `../../_index/constraint-is-a-claim/SKILL.md`. The failure this avoids: drawing the relationship without the orphan probe and shipping a deploy that is then blocked.

## On the record
The fragment this op contributes to the pull request (`../../author-pr/SKILL.md`).

**Review & release**
- A dev lead must review this: a cross-table relationship is added.
- Ships as a single schema change, applied in place — one `ADD CONSTRAINT` that validates every existing child row against the parent; no data is modified.
- Added scrutiny: none for a small parent; at >1M parent rows the validation scan may block writes or run long — schedule a window.

**Verification — run in each environment after deployment**
```sql
-- expect 0 rows: every child points at a real parent
SELECT c.<fk> FROM child c LEFT JOIN parent p ON c.<fk> = p.<pk> WHERE p.<pk> IS NULL;

-- expect one row, is_not_trusted = 0: the foreign key landed trusted
SELECT name, is_not_trusted FROM sys.foreign_keys WHERE name = 'FK_<child>_<parent>';
```

**Rollback**
Lossless: `ALTER TABLE <child> DROP CONSTRAINT FK_<child>_<parent>;`. No data was modified, so nothing else is reversed.

**Not verified**
- Application impact: any insert or update that points a child at a parent that does not exist will now be rejected with error 547; application-side validation is not confirmed here.
- Other environments: the orphan probe was proven on a disposable copy of Dev only; Test, UAT, and Prod may hold orphans this copy cannot see — run the verification query before promotion.
- Production scale: on a large parent the validation scan's duration and locking are not shown by the small copy.
