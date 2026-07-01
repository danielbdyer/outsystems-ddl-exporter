---
name: drop-fk
description: Use when the developer says "remove the reference", "we don't need the link to Customer anymore", "unhook these entities" — dropping a FOREIGN KEY. Always publishes clean (no data loss), but weakens integrity and can shift query plans.
---

# Drop a foreign key

> **Default (provisional — the data decides).** Mechanism 1 Pure Declarative, single-phase, Tier 2 — dropping never vetoes, but it weakens integrity and can shift (regress) query plans.

## OutSystems phrasing
"remove the reference", "we don't need the link to Customer anymore", "unhook these entities".

## SSDT meaning
Remove the `CONSTRAINT` from the table definition. SSDT emits `ALTER TABLE ... DROP CONSTRAINT [FK_...]`. Data is untouched; the table just stops validating the reference.

## The named trap
Dropping a **trusted** FK removes a hint the **query optimizer** relied on — plans can change and regress. None material to the publish (dropping never loses rows). Edge: if you're dropping the FK only to unblock another change (a type change, a table drop), document *why* — re-adding it later re-runs the orphan validation (see `../create-fk-orphan/SKILL.md`).

## How it flips (the specifics only)
- permanent removal → M1, single-phase, Tier 2.
- dropped as a temporary step inside a larger migration (type change, table drop) → a sub-step of a multi-step / multi-PR plan, not standalone (see `../../_index/multi-phase/SKILL.md`).
- re-adding it later → that re-add re-runs create-fk orphan validation → can flip then.
- CDC-enabled → no direct capture impact; the optimizer-plan risk is the real concern.

## Prove it
Strict publishes clean; the delta is a single `DROP CONSTRAINT`; no veto (dropping never loses rows). The proof is mostly confirming the delta touches *only* the constraint and nothing rebuilds. Flag the optimizer-plan and integrity-loss consequences to the developer since the publish itself can't fail. See `../../prove-on-dacpac/SKILL.md` + `../../talk-to-local-sql/SKILL.md`.

## Verdict to the developer
"Dropping the reference always publishes clean — removing a constraint never loses data. Pure Declarative, single-phase. Two things to know: the database stops enforcing that Orders point at real Customers, and query plans that trusted the foreign key may change. Tier 2 for that reason."

## Teach it (the graduation)
The absence of a data veto is not the absence of consequence — a trusted FK is information the optimizer *uses*, not just a guard, so dropping one has effects the publish can't show. Fail mode avoided: treating "unhook these entities" as zero-risk.
