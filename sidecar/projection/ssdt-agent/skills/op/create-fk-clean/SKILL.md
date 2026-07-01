---
name: create-fk-clean
description: Use when the developer says "add a reference to Customer", "draw the relationship from Order to Customer", "Order belongs to a Customer" AND the child data is clean (every child points at a real parent) — a FOREIGN KEY that SQL Server validates against every existing child row and lands as one clean ADD CONSTRAINT.
---

# Create a foreign key, clean data (Forgotten FK Check)

> **Default (provisional — the data decides).** Mechanism 1 Pure Declarative, single-phase, Tier 2 — but PROVE zero orphans first. If orphans exist, this is not your op — route to `../create-fk-orphan/SKILL.md`.

## OutSystems phrasing
"add a reference to Customer", "draw the relationship from Order to Customer", "Order belongs to a Customer".

## SSDT meaning
`CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer]([CustomerId])`. SSDT emits `ALTER TABLE ... ADD CONSTRAINT ...` and SQL Server **validates every existing child row** against the parent. All `Order.CustomerId` present in `Customer` → lands clean.

## The named trap
**Forgotten FK Check** (handbook file 16 = §19.3): adding the FK without probing for **orphans** (child rows whose parent key does not exist) — the clean declarative FK then vetoes at deploy. Dodging with `WITH NOCHECK` leaves an untrusted constraint. This is the constraint-is-a-claim family (violation-on-a-value veto) — see `../../_index/constraint-is-a-claim/SKILL.md`; the orphan-reconcile-retrust path lives in `../create-fk-orphan/SKILL.md`.

## How it flips (the specifics only)
- no orphan rows → M1, single-phase, Tier 2 (inserts/updates now validated; new inter-table dependency).
- orphan rows present → FK validation **vetoes** → route to `../create-fk-orphan/SKILL.md` (M4/M5, Tier 3).
- parent table large → validation scans it → **+1 Tier** at >1M rows.
- CDC-enabled → no capture impact from the FK itself; coordinate if either side is mid-migration (see `../../_index/cdc/SKILL.md`).

## Prove it
Run the orphan probe FIRST: `SELECT COUNT(*) FROM child c LEFT JOIN parent p ON c.<fk> = p.<pk> WHERE p.<pk> IS NULL`. Then Strict publish: clean data → the delta is one `ADD CONSTRAINT`; orphans → Strict vetoes (show the orphan count + offending child rows) and you switch ops. See `../../prove-on-dacpac/SKILL.md` (publish loop) + `../../talk-to-local-sql/SKILL.md` (probe). Seed: Order→Customer clean rows are the positive; the seeded orphan `Order.CustomerId=999` is the flip that routes you to create-fk-orphan; OrderLine→Order gives FK-graph depth (KEY-01).

## Verdict to the developer
"Adding the reference makes SQL Server check every Order against a real Customer. I proved there are no orphans on a copy of your data, so the foreign key lands clean — Pure Declarative, single-phase, Tier 2."

## Teach it (the graduation)
An FK is the textbook *only the data decides*: the same `ADD CONSTRAINT` is clean M1 or vetoed Tier 3 on one orphan — you classify from the orphan count, never the `.sql`. See `../../_index/constraint-is-a-claim/SKILL.md`. Fail mode avoided: drawing the relationship without the orphan probe and shipping a deploy that vetoes.
