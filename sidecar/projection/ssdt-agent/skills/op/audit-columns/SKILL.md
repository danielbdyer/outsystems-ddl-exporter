---
name: audit-columns
description: Use when the developer says "add CreatedBy/CreatedOn/ModifiedBy/ModifiedOn", "stamp who changed it and when", "basic audit fields", "add created/modified tracking columns" — ordinary audit stamp columns. SSDT destination = declarative nullable columns (or Mechanism 3 pre-deploy backfill if NOT NULL on a populated table).
---

# Add manual audit columns (Optimistic-NOT-NULL trap)

> **Default (provisional — the data decides).** Nullable audit columns: Mechanism 1 Pure Declarative, single-phase, Tier 1. NOT NULL on a populated table: Mechanism 3 Pre-Deploy + Declarative, single-PR, Tier 2 — PROVE the backfill clears the veto.

## OutSystems phrasing
"add CreatedBy / CreatedOn / ModifiedBy / ModifiedOn", "stamp who changed it and when", "basic audit fields".

## SSDT meaning
Ordinary nullable columns (often with `DEFAULT SYSUTCDATETIME()` / `DEFAULT SUSER_SNAME()`) plus app-side or trigger-side stamping. SSDT ADDs them declaratively. Never write ALTER.

## The named trap
The *Optimistic NOT NULL* family — if the developer wants the audit columns `NOT NULL` on a populated table without a backfill, the publish vetoes because existing rows have no `CreatedOn`. A `DEFAULT` covers new rows but **not** existing ones unless `GenerateSmartDefaults` stamps them (which Strict refuses, on purpose). This is the tightening class applied to a fresh column — see `../../_index/tightening-class/SKILL.md`; do not re-derive the row-presence guard here. (`make-mandatory` owns the class spine.)

## How it flips (the specifics only)
- nullable / table empty → **M1**, single-phase, Tier 1.
- **`NOT NULL` + populated** → **M3** pre-deploy backfill, single-PR. The backfill that clears the veto is the proof. Tier 2.
- **+ CDC on the table** → +1 Tier; adding columns to a CDC-enabled table needs a capture-instance refresh — see `../recreate-capture-instance/SKILL.md` and `../../_index/cdc/SKILL.md`.
- **+ >1M rows** → +1 Tier; the backfill is a batched operation.

## Prove it
If `NOT NULL`, Strict must veto on the existing rows with no audit value; the pre-deploy backfill must clear it; the Permissive run shows exactly what `GenerateSmartDefaults` would have silently stamped (so the developer sees what the veto was protecting). See `prove-on-dacpac` / `talk-to-local-sql`. On the sample, exercise with the `Customer` seed (AUD-03).

## Verdict to the developer
"Nullable audit columns add in one release. If you want them mandatory, SSDT vetoes because your existing rows have no value — I proved that, and the pre-deploy backfill that stamps them clears it. One release, with the backfill proven."

## Teach it (the graduation)
A `DEFAULT` describes the future, not the past — making an existing column mandatory always confronts the rows already there, and the proof is the backfill plus the now-clean Strict run; the fail mode avoided is expecting NOT-NULL-with-default to "just work" on live data. Full WHY: `../../_index/tightening-class/SKILL.md`.
