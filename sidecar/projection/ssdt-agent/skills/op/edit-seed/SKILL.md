---
name: edit-seed
description: Use when the developer says "add 'Refunded' to the Status list", "change the label on this lookup value", "we have a new order type", "rename the Cancelled option" — adding or amending rows in an existing OutSystems Static Entity. SSDT destination = extend or amend the VALUES block of the idempotent MERGE in the post-deploy seed.
---

# Edit static seed records (unconditional-WHEN-MATCHED trap)

> **Default (provisional — the data decides).** Mechanism 2 Declarative + Post-Deploy, single-PR, Tier 1 — but PROVE the redeploy is silent and the label change touches ONE row.

## OutSystems phrasing
"add 'Refunded' to the Status list", "change the label on this lookup value", "we have a new order type".

## SSDT meaning
Extend or amend the `VALUES` block of the MERGE in the post-deploy seed. Adding a value = a new `WHEN NOT MATCHED THEN INSERT` row; changing a label = the **guarded** `WHEN MATCHED THEN UPDATE` path fires for that one row. Never write ALTER.

## The named trap
An **unconditional `WHEN MATCHED`** that rewrites every row on every deploy (the CDC-silence violation) — this is the idempotent-seed concern; see `../../_index/idempotent-seed/SKILL.md`. (Retiring a referenced value is `delete-seed-value` — route there; that op owns deactivate-don't-delete.)

## How it flips (the specifics only)
- add a new value / change a label → M2, single-PR, Tier 1.
- retire a value the app references → route to `../delete-seed-value/SKILL.md` (deactivate, don't delete).
- **+ CDC-tracked** → guarded `WHEN MATCHED` mandatory; +1 Tier — see `../../_index/cdc/SKILL.md`.

## Prove it
Deploy the new/changed seed, then redeploy unchanged and assert **0 rows affected** + identical data-hash. For a label change, additionally prove the guarded MERGE updates **only the one changed row** — snapshot the `WHEN MATCHED` branch rowcount; it must equal 1, not the table size. If CDC-tracked, the no-op redeploy captures 0. See `prove-on-dacpac` / `talk-to-local-sql` for how. On the sample, add a 'Refunded'-shaped value to `dbo.Category` (STA-02).

## Verdict to the developer
"I added 'Refunded' to the Status seed and proved it idempotent — the second deploy added nothing, and the label change touched exactly one row, not the whole table."

## Teach it (the graduation)
A label change must touch *one* row, not rewrite the table, or a CDC-tracked feed reports the whole table as phantom changes — the fail mode avoided. Full WHY: `../../_index/idempotent-seed/SKILL.md` (guarded MERGE) and `../../_index/cdc/SKILL.md` (the over-capture face).
