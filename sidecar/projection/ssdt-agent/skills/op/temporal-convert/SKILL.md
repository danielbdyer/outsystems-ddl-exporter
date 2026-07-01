---
name: temporal-convert
description: Use when the developer says "add full history to our existing populated entity", "turn on system versioning for Customer which already has data", "make this live table temporal", "start keeping history on an entity that's already in production" — converting an EXISTING populated table to temporal. SSDT destination = a multi-phase add-period-columns / backfill / enable-versioning program.
---

# Convert an existing populated table to temporal (backfilled-ROW-START trap)

> **Default (provisional — the data decides).** Mechanism 5 Multi-Phase, multi-PR, Tier 3 when the table is populated — but PROVE the period-column backfill produces sane start times. (A NEW entity is `../temporal-new/SKILL.md`, single-phase.)

## OutSystems phrasing
"add full history to our existing populated entity", "turn on system versioning for Customer which already has data", "make this live table temporal".

## SSDT meaning
Add the (hidden) `GENERATED ALWAYS AS ROW START/END` period columns with backfilled start times, create the paired history table, then turn `SYSTEM_VERSIONING = ON` against live data. Staged because you cannot swap the shape under a running app atomically. Never write ALTER.

## The named trap
Adding the period columns to a populated table needs **sensible historical defaults for `ROW START`**, or every existing row claims to have begun at conversion time. Also conflating temporal with CDC (see `../temporal-new/SKILL.md`). The coexistence WHY is `../../_index/multi-phase/SKILL.md`; do not re-derive it.

## How it flips (the specifics only)
- existing **populated** table (this op) → **M5 Multi-Phase, multi-PR**: add period columns with backfilled start times, create the history table, enable versioning. Tier 3.
- existing **empty** table → collapses to **M1**, single-phase.
- **+ CDC already on the table** → +1 Tier and CDC sequencing (stacking two history mechanisms — confirm intent) — see `../../_index/cdc/SKILL.md`.
- **+ >1M rows / first-time** → +1 Tier.

## Prove it
Prove the period-column backfill produces sane `ROW START` values and that enabling versioning does not veto on the populated table; hash the data before/after to prove the rows themselves are untouched. See `prove-on-dacpac` / `talk-to-local-sql`. On the sample, convert the existing populated `Customer` in a scratch copy (AUD-02).

## Verdict to the developer
"Converting your existing populated entity to keep history is staged, because the period columns have to be backfilled first — otherwise every existing row would falsely claim it began at conversion time. I proved the backfill produces sane start times and that the rows themselves are untouched."

## Teach it (the graduation)
Converting an *existing populated* table is multi-phase for the coexistence reason — old and new must coexist while the period columns are added and backfilled; the fail mode avoided is every historical row falsely dated to conversion time. Full WHY: `../../_index/multi-phase/SKILL.md`.
