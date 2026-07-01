---
name: extract-to-lookup
description: Use when the developer says "turn this text Status column into a proper Status entity", "these string values should be a lookup so we stop typos", "promote the free-text StatusText into a real reference entity" — replacing a free-text column with a Static Entity + FK. SSDT destination = a multi-phase create-lookup / seed / add-FK / backfill / drop-old-column program.
---

# Extract free-text column to a lookup entity (Forgotten-FK-Check / lost-unmapped-values trap)

> **Default (provisional — the data decides).** Mechanism 5 Multi-Phase, multi-PR, Tier 3 — but PROVE the mapping is total before the drop.

## OutSystems phrasing
"turn this text Status column into a proper Status entity", "these string values should be a lookup so we stop typos".

## SSDT meaning
A multi-step transform: **create** the lookup table, **seed** it with the distinct existing values, **add** a new FK column to the source, **backfill** by joining text → lookup key, then **drop** the old free-text column. Data is read, reshaped, and moved; old and new representations coexist during the transition. Never write ALTER.

## The named trap
Doing it in one publish and silently losing the unmapped values — any source text with no seeded lookup row becomes NULL or vetoes the FK. This is the **Forgotten-FK-Check** face (handbook 16 = §19.3) *and* a coexistence move. The staging/coexistence WHY is `../../_index/multi-phase/SKILL.md`; the seed leg's guarded MERGE + explicit IDs are `../../_index/idempotent-seed/SKILL.md`. Do not re-derive either here.

## How it flips (the specifics only)
- clean distinct values, all mappable → M5, multi-PR, Tier 3.
- **unmapped / dirty source values present** → still M5, but a pre-backfill reconcile is required so nothing maps to NULL; +1 Tier toward 4 if values are lost.
- **+ CDC-tracked** / **+ >1M rows** → +1 Tier — see `../../_index/cdc/SKILL.md`.

## Prove it
Prove the mapping is **total** BEFORE dropping the old column:
`SELECT DISTINCT <oldcol> FROM <t> WHERE <oldcol> NOT IN (SELECT Code FROM <lookup>)` must return **zero rows**. Each phase must publish Strict-clean; the drop phase Strict-vetoes until the backfill hash proves the new shape holds every value (see `../../_index/multi-phase/SKILL.md` for the totality proof). See `prove-on-dacpac` / `talk-to-local-sql` for the loop. On the sample, promote `dbo.Order.StatusText` ('Pending'/'Shipped'/'Cancelled') into a Status FK (STA-03); a scratch seed with an unmapped value fires the total-mapping negative.

## Verdict to the developer
"Promoting that text column to a Status lookup moves existing data into a new shape across a new FK, so it's multi-phase, multi-PR — Tier 3. Before we drop the old column I proved every value maps to a lookup row (zero unmapped), so nothing silently becomes NULL."

## Teach it (the graduation)
When data *moves into a new shape* rather than the schema merely *gaining* shape, the app can't switch in one step — stage it and prove the mapping is total before the drop; the fail mode avoided is the one-publish version silently NULL-ing unmapped values. Full WHY: `../../_index/multi-phase/SKILL.md`.
