---
name: create-entity
description: Use when the developer says "add a new Entity", "create a new table", "I need a new CustomerPreference entity", "make a new entity for X" — a brand-new table that does not yet exist. The purest additive CREATE TABLE.
---

# Create entity

> **Default (provisional — the data decides).** Mechanism 1 Pure Declarative, single-phase, Tier 1 — additive and self-contained, but confirm the clean publish.

## OutSystems phrasing
"add a new Entity", "create a new table", "I need a new CustomerPreference entity".

## SSDT meaning
A new `.sql` file in `Modules/` holding a `CREATE TABLE` — PK, columns, FKs, defaults, all
inline. The table does not exist, so SSDT emits the `CREATE TABLE` verbatim; nothing to
transition. The canonical "describe the destination" move. Never write `ALTER`.

## The named trap
*FK to a non-existent parent* — if the new table references a table not in the project, the
**build** fails (not the deploy); add the parent first or in the same change. And a file the
project glob misses silently never deploys. Neither is a data flip — this is a **dependency**
concern, not a veto concern.

## How it flips (the specifics only)
- new standalone table → **M1, single-phase, Tier 1** (nothing depends on it, nothing to transition)
- table needs seed/lookup rows on creation → pairs with **M2 Declarative+Post-Deploy** (the guarded MERGE seed — see `../create-static-seed/SKILL.md` and `../../_index/idempotent-seed/SKILL.md`)
- table must be CDC-enabled from birth → CDC is not in the dacpac model → **M4 Script-Only rider, +1 Tier** (see `../../_index/cdc/SKILL.md`)
- FK to a parent not yet in the project → still M1, but the build gates it (dependency-order, not a data flip)

## Prove it
Strict publish must succeed clean; the generated delta must be a single `CREATE TABLE
[schema].[Name]` with **no** `DROP`/`ALTER` of any sibling. If the delta touches a table you
did not edit, stop — the change is not self-contained. For the publish loop that PROVES this,
see `../../prove-on-dacpac/SKILL.md`; for the substrate, `../../talk-to-local-sql/SKILL.md`.

## Verdict to the developer
"New entity, nothing depends on it yet — I described the table and SSDT just creates it. Pure
Declarative, single-phase, Tier 1. Proven clean on a copy of your data, and the delta is only
the CREATE."

## Teach it (the graduation)
A create is the purest *model IS the schema* — there is no existing data for SSDT to be
conservative about, so the only thing that can go wrong is a *dependency*, not a data flip. Ask
"does anything already depend on this, or does this depend on anything not yet here?" — the fail
mode avoided is over-tiering self-contained additive work.
