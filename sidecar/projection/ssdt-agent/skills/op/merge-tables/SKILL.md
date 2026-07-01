---
name: merge-tables
description: Use when the developer says "merge CustomerAddress back into Customer", "we don't need two entities, combine them", "fold the lookup into its parent", "collapse these two entities into one" — two entities becoming one, with data moving. SSDT destination = a multi-phase add-absorbing-columns / copy / repoint / drop-absorbed-table program with a cardinality proof.
---

# Merge two entities into one (collision / silent-1:many-drop trap) — recipe AUTHORED HERE

> **AUTHORED-HERE NOTICE.** Handbook file 14 lists §17.7 "merge-entities" in its index but the template body is empty. The multi-phase recipe below is authored here to fill the gap and is the working contract; fold it back into the handbook when file 14 is completed.

> **Default (provisional — the data decides).** Mechanism 5 Multi-Phase, multi-PR, Tier 3 — but PROVE cardinality (1:1) BEFORE copying anything.

## OutSystems phrasing
"merge CustomerAddress back into Customer", "we don't need two entities, combine them", "fold the lookup into its parent".

## SSDT meaning
The inverse of a split. ADD the absorbing columns to the surviving table's CREATE, copy data from the entity being absorbed, repoint every FK and view that referenced the absorbed entity, then drop the absorbed table. SSDT ADDs the columns and (if nullable) publishes clean; it will **not** copy the data and **will veto** the final `DROP TABLE` under `BlockOnPossibleDataLoss`. Never write ALTER.

## The named trap
**Collision and cardinality.** A merge silently assumes the absorbed entity is **1:1 or 1:0..1** with the survivor. If it is actually 1:many, a naive copy keeps one row per parent and **silently drops the rest** — and a value-hash will NOT flag it (the hash only compares surviving rows). Treat any merge as suspected 1:many until proven otherwise. Companion trap: a *SELECT \* View* (see `../create-view/SKILL.md`) over the absorbed entity left unenumerated through the merge. The coexistence + subtractive-licensing WHY is `../../_index/multi-phase/SKILL.md`; do not re-derive it.

## How it flips (the specifics only)
- absorbed entity **empty** → drop it as a clean subtractive change (**M3**, single-PR) and add the survivor's columns as **M1**. No data to move.
- absorbed populated, **proven 1:1** with survivor → **M5 Multi-Phase, multi-PR** (three phases below). Tier 3.
- absorbed populated, **1:many** (collision) → STILL M5 but the merge is *semantically wrong as stated*: STOP and tell the developer the cardinality — a design decision, not a mechanism flip.
- **+ CDC on either table** → +1 Tier and capture-instance management on both — see `../../_index/cdc/SKILL.md`.
- **+ >1M rows / first-time** → +1 Tier.

**The three phases (each its own PR):**
- **Phase 1 (additive, M1/2):** ADD the absorbing columns (nullable) to the survivor CREATE, post-deploy-copy from the absorbed entity, dual-write new rows. Prove cardinality here: count absorbed rows vs. distinct parents — equal = 1:1, safe; unequal = 1:many, STOP. Strict publishes clean. Tier 2.
- **Phase 2 (cutover, M2):** repoint app reads, FKs, and views from the absorbed entity to the survivor's new columns. Leave a backward-compat view named for the absorbed entity if any external consumer still references it (`../compat-view/SKILL.md`).
- **Phase 3 (subtractive, M3):** drop the absorbed table. Strict MUST veto under `BlockOnPossibleDataLoss` until Phase-1 hashes prove every value is now in the survivor.

## Prove it
Phase 1 — the **row-count cardinality check** (absorbed rows == distinct parent keys) BEFORE anything else; then hash the absorbed columns vs. the new survivor columns and prove equal. Phase 3 — Strict vetoes the `DROP TABLE` until the hashes match. See `prove-on-dacpac` / `talk-to-local-sql`. On the sample, merge `CustomerAddress` (seeded 1:1) into `Customer` (STR-02); a scratch seed adding a 2nd CustomerAddress row for one Customer fires the 1:many refusal (STR-02N).

## Verdict to the developer
"Before merging CustomerAddress into Customer I proved on a copy of your data that it's 1:1 (row counts match) — if it had been one-to-many the merge would have silently dropped rows. The data-copy publishes clean; the old-table drop SSDT vetoes until the copy is proven. Three PRs."

## Teach it (the graduation)
A row-count proof is a different proof from a value-hash, and you need the count *first* — "combine these" hides the unstated question *how many of the absorbed side per parent*, and only the data answers it; the fail mode avoided is a naive copy silently discarding 1:many rows. Full WHY (coexistence): `../../_index/multi-phase/SKILL.md`.
