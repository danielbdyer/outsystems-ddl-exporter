---
name: delete-seed-value
description: Use when the developer says "delete the Cancelled status value", "retire this lookup value", "remove the option nobody picks anymore", "drop this order type from the list" — retiring a row from an OutSystems Static Entity. SSDT destination = set IsActive = 0 in the seed MERGE, NOT a hard DELETE.
---

# Retire a static lookup value (hard-DELETE-orphans trap — deactivate, don't delete)

> **Default (provisional — the data decides).** Deactivate via `IsActive = 0`: Mechanism 2 Declarative + Post-Deploy, single-PR, Tier 1–2 — REFUSE the hard DELETE if the value is referenced.

## OutSystems phrasing
"delete the Cancelled status value", "retire this lookup value", "remove this order type from the list".

## SSDT meaning
Set `IsActive = 0` on the seed row (the guarded `WHEN MATCHED` fires for that one row), preserving the row's identity and history. A hard `DELETE` of a seed row is the wrong move when anything references it. Never write ALTER.

## The named trap
**Deleting a lookup value the app still references** — a hard DELETE orphans every fact row pointing at it and breaks the app's `StatusId = 3` constant. The discipline is **deactivate, don't delete** — owned by `../../_index/idempotent-seed/SKILL.md`; do not re-derive it here.

## How it flips (the specifics only)
- value unreferenced, no fact rows point at it → a clean subtractive seed edit is defensible, but default to `IsActive = 0` anyway (Tier 1).
- **value the app / fact rows reference** → `IsActive = 0` (single-PR, Tier 1–2); a hard DELETE that orphans fact rows is **Tier 3+** and usually wrong — refuse the DELETE, propose deactivation.
- **+ CDC-tracked** → guarded `WHEN MATCHED` mandatory; +1 Tier — see `../../_index/cdc/SKILL.md`.

## Prove it
Prove the reference exists before choosing: `SELECT COUNT(*) FROM <factTable> WHERE <fk> = <valueId>` — nonzero means DELETE orphans, deactivate instead. After the `IsActive = 0` edit, redeploy unchanged and assert **0 rows affected** + identical hash + (if CDC-tracked) 0 captures. See `prove-on-dacpac` / `talk-to-local-sql`. On the sample, `dbo.Category` is referenced by `dbo.Product.CategoryId`, so a hard DELETE of a Category value fires the orphan negative (STA-04N).

## Verdict to the developer
"For the retired value I set IsActive = 0 instead of deleting it — the N orders that still point at it keep their referential integrity. Nothing orphaned, proven."

## Teach it (the graduation)
Reference rows are referenced — retiring one is a referential decision, not a data edit, and the safe move preserves the identity; the fail mode avoided is a hard DELETE silently orphaning fact rows. Full WHY: `../../_index/idempotent-seed/SKILL.md`.
