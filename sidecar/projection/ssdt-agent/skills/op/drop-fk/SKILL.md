---
name: drop-fk
description: Use when the developer says "remove the reference", "we don't need the link to Customer anymore", "unhook these entities" — dropping a FOREIGN KEY. Always publishes clean (no data loss), but weakens integrity and can shift query plans.
---

# Drop a foreign key

> **Default (provisional — the data decides; prove before you classify).** Ships as a single schema change, applied in place — a single `ALTER TABLE ... DROP CONSTRAINT`, no data read or written, and the publish never blocks. A dev lead or an experienced developer should review this: dropping the constraint weakens referential integrity and can shift (regress) query plans.

## OutSystems phrasing
"remove the reference", "we don't need the link to Customer anymore", "unhook these entities".

## SSDT meaning
Remove the `CONSTRAINT` from the table definition. SSDT emits `ALTER TABLE ... DROP CONSTRAINT [FK_...]`. Data is untouched; the table just stops validating the reference.

## The named trap
Dropping a **trusted** FK removes a hint the **query optimizer** relied on — plans can change and regress. None material to the publish (dropping never loses rows). Edge: when the drop is only to unblock another change (a type change, a table drop), document *why* — re-adding the constraint later re-runs the orphan validation (see `../create-fk-orphan/SKILL.md`).

## How it flips (the specifics only)
- permanent removal → ships in place as a single schema change; a dev lead or experienced developer should review it (integrity weakens, plans can shift).
- dropped as a temporary step inside a larger migration (type change, table drop) → a sub-step of a multi-step / multi-PR plan, not standalone (see `../../_index/multi-phase/SKILL.md`).
- re-adding it later → that re-add re-runs create-fk orphan validation → can flip then.

## Prove it
Strict publishes clean; the delta is a single `DROP CONSTRAINT`; nothing blocks it (dropping never loses rows). The proof is mostly confirming the delta touches *only* the constraint and nothing rebuilds. Flag the optimizer-plan and integrity-loss consequences to the developer, since the publish itself cannot fail. See `../../prove-on-dacpac/SKILL.md` + `../../talk-to-local-sql/SKILL.md`.

## The verdict (to the developer)
"You asked to remove the reference. Dropping it always publishes clean — removing a constraint never loses data, so there's nothing to remediate first. Two things are worth knowing before it ships: the database stops enforcing that Orders point at real Customers, so nothing prevents an orphan being written afterward; and any query plan that trusted the foreign key may change, occasionally for the worse. Neither shows up in the publish itself, which is why this is worth a second set of eyes from a lead or an experienced developer. One thing to confirm: is this a permanent removal, or a temporary step to unblock another change like a type change or a table drop? If it's temporary, it belongs inside that larger migration rather than shipping on its own."

## The reasoning (in conversation)
A publish that never blocks is not the same as a change with no consequence. A trusted foreign key is information the optimizer *uses* to shape query plans, not just a guard against orphans — so dropping one has effects the publish can't show you. The mistake to avoid is reading "unhook these entities" as zero-risk just because nothing fails at deploy time.

## On the record
The fragment this op contributes to the pull request (`../../author-pr/SKILL.md`).

**Review & release**
- A dev lead or an experienced developer should review this: dropping the constraint weakens referential integrity and can shift query plans; no data is touched.
- Ships as a single schema change, applied in place — a single `ALTER TABLE ... DROP CONSTRAINT`. No data is read or written, and the publish never blocks.
- Added scrutiny: none. The drop reads and writes no data, so row count is not a factor.

**Verification** — run in each environment after deployment
```sql
-- expect 0 rows: the foreign key no longer exists
SELECT name FROM sys.foreign_keys WHERE name = 'FK_<child>_<parent>_<column>';
```

**Rollback**
Re-creating the constraint reverses the drop: `ALTER TABLE <child> ADD CONSTRAINT FK_<child>_<parent>_<column> FOREIGN KEY (<fk>) REFERENCES <parent> (<pk>);`. This re-runs the orphan validation of `../create-fk-orphan/SKILL.md`, so it lands clean only if no orphan rows were written while the constraint was absent; otherwise it is blocked until those rows are reconciled. The original drop loses no data, so nothing else needs restoring.

**Not verified**
- Query-plan impact — dropping a trusted foreign key removes a hint the optimizer used; a plan change or regression will not show on a disposable copy, whose statistics and data volume differ from production. Whoever owns query performance confirms this.
- Application impact — nothing enforces the reference after the drop; whether application code relies on the database rejecting an order that points at a missing customer is not confirmed here.
- Other environments — the drop was proven on a disposable copy of Dev only; whether Test, UAT, and Prod behave identically is not shown here. Run the verification query before promotion.
