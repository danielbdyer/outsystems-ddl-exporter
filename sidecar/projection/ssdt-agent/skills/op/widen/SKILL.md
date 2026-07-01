---
name: widen
description: Use when the developer says "make the field longer", "increase Email to 256", "give Total more precision", "make it a bigger number" — enlarging length/precision. Data-preserving; the one coupling is the index-key byte limit.
---

# Widen length/type

> **Default (provisional — the data decides).** Mechanism 1 Pure Declarative, single-phase, Tier 2 — usually metadata-only, additive to callers.

## OutSystems phrasing
"make the field longer", "increase Email to 256", "give Total more precision".

## SSDT meaning
Enlarge length/precision (`NVARCHAR(100)`→`NVARCHAR(200)`, `DECIMAL(10,2)`→`DECIMAL(18,2)`,
`INT`→`BIGINT`). Every existing value still fits, so it is data-preserving. SSDT emits `ALTER
COLUMN` to the wider type. Edit the CREATE; never write `ALTER`.

## The named trap
**Index-key byte limit** — a column inside a non-clustered index key cannot push the key past
1700 bytes (900 in older versions); widening it then fails. And `NVARCHAR` widening **doubles
storage** vs `VARCHAR`, which can tip an index over that limit. This is a single-op coupling (it
recurs only here), so it stays inline — not lifted to an index skill.

## How it flips (the specifics only)
- widen a non-indexed column → **M1, single-phase, Tier 1–2**
- the column sits in an index key and the widen blows the byte limit → publish **vetoes** / build complains → drop/redesign the index → multi-step single-PR
- very large table on an old SQL version → may rebuild rather than metadata-only → **+1 Tier** at >1M rows
- CDC-enabled → type change → recreate capture instance → **+1 Tier** (see `../../_index/cdc/SKILL.md`)

## Prove it
Strict publishes clean; delta is `ALTER COLUMN` to the wider type with no veto and no rebuild of
unrelated objects. If the column is indexed, prove the index either survives or is the only thing
the delta also touches. For the publish loop, see `../../prove-on-dacpac/SKILL.md`.

## Verdict to the developer
"Widening preserves every existing value, so it publishes clean — Pure Declarative, single-phase.
The one thing I checked: this column isn't in an index whose key would blow the byte limit, and
NVARCHAR doubling didn't tip anything over. Tier 2."

## Teach it (the graduation)
Widening is data-preserving by definition — only the data could make it unsafe, and here it
can't — so the risk is *structural* (the index byte budget), not data. Widen and narrow are
mirror images (widen's risk is the byte budget, narrow's is `MAX(LEN)`). Fail mode avoided:
treating a type change as obviously-safe without looking one hop out at what the column
participates in.
