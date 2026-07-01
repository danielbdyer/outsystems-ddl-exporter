---
name: retype-implicit
description: Use when the developer says "change to a bigger number", "INT to BIGINT", "make this VARCHAR into NVARCHAR", "store it as a wider type" — a widening/implicit type change where every value already fits. Lossless.
---

# Retype implicit (widening conversion)

> **Default (provisional — the data decides).** Mechanism 1 Pure Declarative, single-phase, Tier 2 — every value already fits the bigger type, so the engine has nothing to refuse.

## OutSystems phrasing
"change to a bigger number", "INT to BIGINT", "make this VARCHAR into NVARCHAR".

## SSDT meaning
Change the column's data type in the widening direction (`INT`→`BIGINT`,
`VARCHAR(n)`→`NVARCHAR(n)`, `DECIMAL(10,2)`→`DECIMAL(18,2)`). These are lossless — SSDT emits a
single `ALTER COLUMN`. Edit the CREATE; never write `ALTER`.

## The named trap
None material for a true widening — every value converts. The one edge: `VARCHAR`→`NVARCHAR`
**doubles storage**, which can tip an indexed column over the index-key byte limit (see
`../widen/SKILL.md`). If the "retype" is actually value-reshaping (Text→Date, DATETIME→DATE),
it is NOT this op — route to `../retype-explicit/SKILL.md`.

## How it flips (the specifics only)
- implicit/widening, all values convert → **M1, single-phase, Tier 2**
- VARCHAR→NVARCHAR on an indexed column → storage doubles → the index-key byte-limit edge (see `../widen/SKILL.md`)
- direction is actually narrowing/reshaping → **wrong op** → `../retype-explicit/SKILL.md`
- CDC-enabled / >1M rows → **+1 Tier** (see `../../_index/cdc/SKILL.md`)

## Prove it
Strict publishes clean; the delta is one `ALTER COLUMN` to the wider type with no veto and no
rebuild of unrelated objects. If you ever see the delta do more than the single ALTER, stop and
check whether the conversion is actually value-reshaping. For the publish loop, see
`../../prove-on-dacpac/SKILL.md`.

## Verdict to the developer
"Going INT→BIGINT is lossless — SSDT just widens it, Pure Declarative, single-phase. Every
existing value already fits the bigger type, so there's nothing for the engine to refuse. Tier 2."

## Teach it (the graduation)
Direction is everything: a widening retype is free because every value already fits, so only the
*explicit/narrowing* sibling needs per-row proof. Fail mode avoided: classifying "retype" as one
thing — always ask "is this widening (free) or value-reshaping (prove each row)?" before
promising anything.
