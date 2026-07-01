---
name: recreate-capture-instance
description: Use when the developer says "I added a column but CDC isn't picking it up", "the ETL feed is missing the new field", "why doesn't the change feed see my new attribute", "the warehouse stopped getting the new column" — a schema change on a CDC-tracked table. SSDT destination = a dual-instance (v1/v2) capture-instance migration script, multi-phase when no-gap is required.
---

# Recreate the CDC capture instance (CDC Surprise realized — silent-missing-column trap)

> **Default (provisional — the data decides).** Mechanism 5 Multi-Phase, multi-PR whenever no-gap is required (dual-instance v1/v2), Tier 3 (+1 → Tier 4 because a mistake silently loses change records).

## OutSystems phrasing
"I added a column to Customer but CDC isn't picking it up", "the ETL feed is missing the new field", "why doesn't the change feed see my new attribute".

## SSDT meaning
A CDC capture instance is **frozen at the table shape it was created for** — adding/altering a column does not retrofit it. The safe procedure is the **dual-instance pattern**: create a *second* capture instance (`Customer_v2`) for the new shape with `sp_cdc_enable_table @capture_instance = 'Customer_v2'`, let consumers drain the old instance (`Customer_v1`), cut them over to v2, then drop v1. Handbook file 14 (=§17.9). Never write ALTER; this is a script.

## The named trap
**CDC Surprise realized** — the developer's "trivial" add-column change is now a multi-phase capture-instance migration, and if they skip it the new column's changes are **silently absent from the feed** (no error, just missing data downstream). Also the *Refactorlog Cleanup* family: a schema change the refactorlog handles for the dacpac still needs the capture instance rebuilt independently. The standing-tax WHY is `../../_index/cdc/SKILL.md`; the coexistence WHY is `../../_index/multi-phase/SKILL.md`. Do not re-derive either.

## How it flips (the specifics only)
- schema change on a CDC table, **no-gap NOT required** (consumers tolerate a brief gap) → drop and recreate the single capture instance: **M4 Script-Only**, single-PR, Tier 3.
- schema change on a CDC table, **no-gap required** → **dual-instance v1/v2, M5 Multi-Phase, multi-PR**. Tier 3, **+1 → Tier 4** (a mistake silently loses change records).
- **+ >1M rows** → +1 Tier; both capture instances are populated during the overlap.

## Prove it
Prove the gap exists — add a column to a CDC-enabled table on the isolated DB, then show the existing capture instance does **not** surface the new column (`sys.sp_cdc_get_captured_columns` lacks it). Then prove the dual-instance fix: `Customer_v2` surfaces the new column while `Customer_v1` is still drainable. Isolation is mandatory (see `../../_index/cdc/SKILL.md`, `talk-to-local-sql`). On the sample, add a column to `dbo.CdcCandidate` after capture (AUD-05).

## Verdict to the developer
"Your new column isn't trivial because CDC is on this entity — the change feed is frozen to the old shape and silently won't include the new field. I proved that on a copy. The fix runs two capture instances side by side so the ETL never misses a change during the switch — that's why it's staged across releases, not a one-line add."

## Teach it (the graduation)
With CDC the dangerous outcome isn't a loud veto, it's a quiet gap — so the proof is showing the existing instance does NOT surface the new column, then showing the dual instance does; the fail mode avoided is the warehouse silently missing a column for a month. Full WHY: `../../_index/cdc/SKILL.md`.
