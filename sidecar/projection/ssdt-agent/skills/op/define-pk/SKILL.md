---
name: define-pk
description: Use when the developer says "set the primary key", "the Identifier", "make this the unique key for the entity", "use a composite key on OrderId + LineNumber" — declaring a PRIMARY KEY inline in the CREATE (new table) or onto a populated table (a clustered-index build over every row that vetoes on duplicate/NULL keys).
---

# Define the primary key (the Identifier)

> **Default (provisional — the data decides).** New table → Mechanism 1 Pure Declarative, single-phase, Tier 1. Existing populated table → Mechanism 1, single-phase, Tier 2 (the clustered-index build touches every row) — but PROVE the key is unique and non-NULL first.

## OutSystems phrasing
"the Identifier", "set the primary key", "make this the unique key for the entity", "use a composite key on OrderId + LineNumber".

## SSDT meaning
`CONSTRAINT [PK_Table] PRIMARY KEY CLUSTERED (...)` inline in the `CREATE`. On a new table it is part of the create. On an existing populated table, adding the PK **builds a clustered index** (scans and reorders every row) and **fails if the key column has duplicate or NULL values**.

## The named trap
A PK is a *claim of uniqueness proven at build time* — adding it to a populated table with duplicate or NULL key values vetoes on the index build. This is the constraint-is-a-claim family (populated-veto face) — see `../../_index/constraint-is-a-claim/SKILL.md`; do not re-derive the claim mechanics here. Separate trap: confusing an IDENTITY surrogate with a natural key — see `../identity-swap/SKILL.md`.

## How it flips (the specifics only)
- new table → M1, Tier 1 (PK inline in the create).
- existing table, key column already unique & non-NULL → M1, Tier 2 (the index build touches every row).
- existing table with duplicate/NULL key values → index build **vetoes** → M3 Pre-Deploy (dedupe/assign keys first) or M5 Multi-Phase — the veto is on the actual duplicate/NULL values, see `../../_index/constraint-is-a-claim/SKILL.md`.
- \>1M rows → **+1 Tier** (blocking clustered-index build).
- CDC-enabled → **+1** (see `../../_index/cdc/SKILL.md`).

## Prove it
Run the op-specific probes FIRST: `SELECT <keycols>, COUNT(*) FROM <table> GROUP BY <keycols> HAVING COUNT(*) > 1` (duplicate probe) and a NULL count on each key column. Then Strict publish: dups → the index build vetoes (show the offending keys); clean → the delta is the PK inline in the create (new) or a clean clustered-index build (existing). Author the dedupe/assign-keys pre-deploy, re-run Strict clean. See `../../prove-on-dacpac/SKILL.md` for the publish loop and `../../talk-to-local-sql/SKILL.md` for running the probes. Seed: OrderLine (KEY-01) proves the composite `OrderId + LineNumber`.

## Verdict to the developer
"Defining the Identifier on a new entity is free — it's part of the create. On your existing table it builds a clustered index over every row; I proved the key is unique with no NULLs, so it publishes clean. Tier 2 because the build touches the whole table."

## Teach it (the graduation)
A key constraint is a claim about existing rows — you prove the claim (duplicate + NULL probe) before you classify, per `../../_index/constraint-is-a-claim/SKILL.md`. Fail mode avoided: assuming the key is clean and shipping a deploy that vetoes on dirty keys.
