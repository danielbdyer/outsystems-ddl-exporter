---
name: split-table
description: Use when the developer says "split Customer into Customer and CustomerAddress", "pull the address fields out into their own entity", "this entity is doing too much, break it up" — one entity becoming two, with data moving. SSDT destination = a multi-phase additive-CREATE / copy / cutover / drop-old-columns program.
---

# Split one entity into two (one-PR / Naked-Rename-per-column trap)

> **Default (provisional — the data decides).** Mechanism 5 Multi-Phase, multi-PR, Tier 3 — but PROVE the source is empty first (it may collapse to M1).

## OutSystems phrasing
"split Customer into Customer and CustomerAddress", "pull the address fields out into their own entity", "break this entity up".

## SSDT meaning
CREATE the new table, copy the moving columns' data keyed by the source PK, add the FK, then (much later) drop the old columns from the source CREATE. SSDT will CREATE the new table and ADD the FK declaratively but will **never move the data** — the copy is a post-deploy script, and the column drop is a `BlockOnPossibleDataLoss` veto until the copy is proven. Handbook file 14 (=§17.6). Never write ALTER.

## The named trap
Treating it as one PR — dropping the old columns in the same release that creates the new table breaks any app code still reading them. The related trap is *Naked Rename* applied per-column when the developer "renames" columns into the new table instead of copying — see `../../_index/identity-and-refactorlog/SKILL.md`. The coexistence + subtractive-licensing WHY is `../../_index/multi-phase/SKILL.md`; do not re-derive either.

## How it flips (the specifics only)
- new entity, **source table empty** (greenfield split) → collapses to **M1**, single-phase: just CREATE both tables. Prove the source is empty first.
- source populated, app reads old columns → **M5 Multi-Phase, multi-PR** (Phase 1 additive CREATE+FK+copy+dual-write, Tier 2 · Phase 2 repoint reads · Phase 3 drop old columns, the `BlockOnPossibleDataLoss` veto moment, Tier 3).
- **+ CDC on the source** → +1 Tier; the final column drop needs a capture-instance refresh — see `../../_index/cdc/SKILL.md` and `../recreate-capture-instance/SKILL.md`.
- **+ >1M rows / first-time** → +1 Tier; the copy is a long-running batched operation.

## Prove it
Phase 1 — Strict publishes the additive CREATE+FK clean; the post-deploy copy runs; hash the moving columns source-vs-new-table and prove **equal**. Phase 3 — edit the source CREATE to drop the moved columns; Strict MUST veto on data loss, and only the proven-equal Phase-1 hash licenses the drop (see `../../_index/multi-phase/SKILL.md` for the totality proof). See `prove-on-dacpac` / `talk-to-local-sql` for the loop. On the sample, split `Customer` into `Customer` + `CustomerAddress` (STR-01; the seeded 1:1 CustomerAddress row is the copy target).

## Verdict to the developer
"Splitting Customer into two entities moves data, so it can't be one release. I proved the additive half publishes clean and copies all N rows (hashes match); the column-drop half SSDT vetoes until the copy is proven — three PRs, in order."

## Teach it (the graduation)
Any change that *relocates data between shapes* is additive-then-cutover-then-subtractive, and the proof that licenses the subtractive phase is a BEFORE/AFTER hash showing the new home holds every value; the fail mode avoided is a one-PR split that breaks live reads or loses data. Full WHY: `../../_index/multi-phase/SKILL.md`.
