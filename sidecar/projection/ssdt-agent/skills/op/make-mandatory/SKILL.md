---
name: make-mandatory
description: Use when the developer says "make Email required", "tick the Mandatory checkbox", "this attribute must be filled", "change it from optional to required" — an existing column NULL→NOT NULL. THE canonical table-has-rows tightening flip.
---

# Make mandatory (NULL → NOT NULL) — the tightening-class spine

> **Default (provisional — the data decides).** EMPTY table → Mechanism 1, single-phase, Tier 1. POPULATED table (NULLs OR zero NULLs) → NOT a clean M1 and NOT a clean M3 — it needs a conscious gate decision. Prove before you classify.

## OutSystems phrasing
"make Email required", "tick the Mandatory checkbox on this attribute".

## SSDT meaning
Change an existing column from `NULL` to `NOT NULL`. SSDT emits `ALTER TABLE ... ALTER COLUMN
[Col] <type> NOT NULL` — but on a populated table it guards that ALTER with a **data-blind
`BlockOnPossibleDataLoss` check that fires on table-has-rows, NOT column-has-NULLs**. Edit the
CREATE; never write `ALTER`.

## The named trap
This is **the tightening class** — see `../../_index/tightening-class/SKILL.md` for the
`IF EXISTS(SELECT TOP 1 1 FROM <t>) RAISERROR(...,16,127)` guard, the empty-vs-populated ladder,
and the proven **why** (SSDT computes the whole deploy script once, up front, so a same-release
backfill cannot satisfy it). **Do not re-derive the guard here.** The showcase failure this op
exists to catch: classifying from the `.sql` text or a clean NULL probe — both *look* green, yet
the engine refuses. **The old "backfill → clean NOT NULL under Strict = Mechanism 3" recipe is
BANNED — it was disproven: a pre-deploy backfill cleared every NULL and Strict STILL vetoed.**

## How it flips (the specifics only)
- **table EMPTY** → **M1 Pure Declarative, single-phase, Tier 1** (the `IF EXISTS` is false; the ALTER lands — verify genuinely empty first)
- **table POPULATED — NULLs present OR zero NULLs, does not matter** → cannot pass the prod-strict gate by backfill alone (see `../../_index/tightening-class/SKILL.md`). After proving `COUNT(*) WHERE Col IS NULL = 0` (necessary, not sufficient), choose ONE:
    - **(a) named gate-relaxation** — operationally M4/Script-Only: disable `BlockOnPossibleDataLoss` for this one targeted change, logged, with the proof packet carrying **both** the zero-NULL probe and the relaxation decision.
    - **(b) restructure as M5 Multi-Phase** so the engine never has to relax its guard (see `../../_index/multi-phase/SKILL.md`).
  Tier 2 baseline; **+1** for CDC / >1M / first-time.
- **+ CDC, no-gap** → push toward M5 Multi-Phase, +1 Tier (see `../../_index/cdc/SKILL.md`)

## Prove it (COL-03 / COL-03C — discover, don't assert)
1. Edit `NULL` → `NOT NULL`, build, Strict publish → prove the veto fires, and **read the delta**
   to SEE the `IF EXISTS(...) RAISERROR(...,16,127)` guard ABOVE the `ALTER COLUMN` (table-has-rows).
2. Author the pre-deploy backfill, re-run the NULL probe → prove `0` NULLs remain.
3. Re-run Strict → prove it **STILL vetoes** and the column **stays nullable**. This step is the showcase finding.
4. Deliver the corrected verdict: (a) named gate-relaxation after proven-zero-NULL, or (b) multi-phase — and PROVE the chosen path lands the `NOT NULL`.

The `COL-03C` twin (zero NULLs from the start) still vetoes; the `COL-03B` twin (EMPTY) publishes
clean M1. For the publish loop, see `../../prove-on-dacpac/SKILL.md`.

## Verdict to the developer
"You said make Email mandatory. On a copy SSDT vetoed it — the guard is `IF EXISTS (SELECT TOP 1
1 FROM Customer) RAISERROR(...)` *before* the ALTER: table-has-rows, not NULL-has-rows. I proved
it — backfilled every NULL (0 remain) and Strict STILL vetoed and left the column nullable. So on
your populated table this needs a conscious call: I deliberately relax BlockOnPossibleDataLoss
for this one change *after* proving zero NULLs (logged, script-only), or we stage it multi-phase.
Tier 2 (+1 if CDC). On an EMPTY table it would have been a clean one-liner, Tier 1 — the
difference is entirely the rows."

## Teach it (the graduation)
This is the sharpest lesson in trusting the oracle over the recipe — the guard is table-has-rows
(see `../../_index/tightening-class/SKILL.md`), so a clean data probe is *necessary but never
sufficient*. Fail mode avoided: trusting backfill-alone and running the banned recipe instead of
making the conscious, documented gate call.
