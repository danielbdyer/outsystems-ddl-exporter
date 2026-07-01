---
name: temporal-new
description: Use when the developer says "I want full history on this new entity", "keep every version of every row (new table)", "system-versioned table from the start", "show me what this record looked like last Tuesday — for a brand-new entity" — temporal versioning on a NEW entity. SSDT destination = a declarative system-versioned CREATE (SYSTEM_VERSIONING = ON + history table + period columns).
---

# Add temporal versioning — new entity (temporal-vs-CDC conflation trap)

> **Default (provisional — the data decides).** Mechanism 1 Pure Declarative, single-phase, Tier 2 — SSDT builds a new system-versioned table cleanly. (Converting an EXISTING populated table is a different op — route to `../temporal-convert/SKILL.md`.)

## OutSystems phrasing
"I want full history on this new entity", "keep every version of every row (new table)", "point-in-time history from birth".

## SSDT meaning
`SYSTEM_VERSIONING = ON` with a paired history table and two `GENERATED ALWAYS AS ROW START/END` `datetime2` period columns. SQL Server maintains the history table on every write. Unlike CDC, **temporal IS expressible declaratively for a new table** — SSDT can publish the system-versioned CREATE. Never write ALTER.

## The named trap
**Conflating temporal with CDC.** They are different mechanisms — different licensing (temporal is all editions; CDC is Enterprise/Standard) and different shapes (temporal gives point-in-time row history; CDC gives a change feed). Picking the wrong one is a design error the agent must catch at **intake**, before building. If the developer needs a *change feed* (old→new values for an ETL), route to `../enable-cdc/SKILL.md`. See `../../_index/cdc/SKILL.md` for the CDC weight this avoids.

## How it flips (the specifics only)
- **new table** (this op) → **M1**, single-phase, Tier 2.
- existing empty table → **M1**, single-phase.
- existing **populated** table → NOT this op; route to `../temporal-convert/SKILL.md` (multi-phase).
- **+ CDC already on the table** → +1 Tier and CDC sequencing (you are stacking two history mechanisms — confirm the developer wants both) — see `../../_index/cdc/SKILL.md`.
- **+ >1M rows / first-time** → +1 Tier.

## Prove it
Preview the Strict delta and confirm it publishes the system-versioned CREATE clean — the history table and period columns appear, no veto. See `prove-on-dacpac`. On the sample, temporal-new (AUD-01) is a scratch-authored brand-new system-versioned table (greenfield — no authored seed needed).

## Verdict to the developer
"You want history — there are two kinds. Temporal gives you point-in-time row history with no licensing cost, and for a new entity it publishes clean in one release. (CDC is the other kind — a change feed — and it's a much heavier commitment; tell me which you actually need.)"

## Teach it (the graduation)
When a developer's word covers two mechanisms with different bills, disambiguate at intake — "track changes" and "keep history" are exactly such words; the fail mode avoided is building CDC's heavy machinery when point-in-time editions were all that was wanted. Full WHY (name your intent): `../../_index/cdc/SKILL.md`.
