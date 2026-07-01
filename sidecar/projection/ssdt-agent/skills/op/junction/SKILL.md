---
name: junction
description: Use when the developer says "make this a many-to-many", "a Student can have many Courses and a Course many Students", "add a bridge entity", "add a join/link table" — an M:N bridge table with a composite PK over two FKs.
---

# Junction (M:N bridge table)

> **Default (provisional — the data decides).** Mechanism 1 Pure Declarative, single-phase, Tier 2 — additive, but it introduces two inter-table dependencies.

## OutSystems phrasing
"make this a many-to-many", "a Student can have many Courses and a Course many Students", "add a bridge entity".

## SSDT meaning
A new `CREATE TABLE` whose PK is the **composite of two FK columns**, each FK referencing one
parent's PK. It is a `create-entity` with two inbound dependencies and one composite key. Never
write `ALTER`.

## The named trap
Declaring the FKs when the parent rows the bridge will reference do not yet exist (orphans on
seed) — the **Forgotten FK Check** in disguise. That is the constraint-is-a-claim concern — see
`../../_index/constraint-is-a-claim/SKILL.md`. Forgetting the composite PK lets duplicate pairs
in. Do not re-derive the orphan/claim mechanics here.

## How it flips (the specifics only)
- brand-new empty bridge, both parents present → **M1**, Tier 2
- bridge seeded with pairs referencing missing parents → the FK validation **vetoes** → flips to the orphan path (`../create-fk-orphan/SKILL.md`, `../../_index/constraint-is-a-claim/SKILL.md`), M4/M5
- either parent table is large → FK validation scans both → **+1 Tier** at >1M rows

## Prove it
Strict publish creates the bridge clean and **no FK veto fires** — proving every seeded pair has
both parents present. Author one orphan pair and watch the veto to demonstrate the failure mode
the composite-PK + two-FK shape guards against. Probe: `LEFT JOIN` each FK column to its parent
looking for NULL parents. For the publish loop, see `../../prove-on-dacpac/SKILL.md`.

## Verdict to the developer
"A many-to-many is a bridge table: a composite primary key over two foreign keys. I described it
and SSDT creates it clean — Pure Declarative, single-phase. Tier 2 because it now ties two
entities together. I checked there are no orphan pairs, so the FK validation passes."

## Teach it (the graduation)
The composite-PK-over-two-FKs shape *is* the guarantee (PK stops duplicate pairs, the two FKs
stop orphan pairs) — the same orphan-check discipline as every FK (see
`../../_index/constraint-is-a-claim/SKILL.md`). Fail mode avoided: treating "many-to-many" as
just two columns and seeding orphan pairs.
