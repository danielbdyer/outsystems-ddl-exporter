# ReferenceData/ — permanent · idempotent (reference-data seeds)

The same permanence class as `../Migrations/`, scoped to **reference / static-entity seeds** — the
small enumerated tables (Status, Country, OrderType) whose rows *are* the model, not user input.
Seeds are **permanent and idempotent** and carry **no death certificate**.

Contract:
- **Guarded, null-safe `MERGE`** — `WHEN MATCHED AND <value differs>` (never an unconditional
  `WHEN MATCHED`, which rewrites every row on every deploy — the CDC-silence violation); explicit
  IDs, not IDENTITY, so a constant means the same row in every environment; deactivate
  (`IsActive = 0`), never hard-delete a referenced value. Owned by
  `../../skills/_index/idempotent-seed/SKILL.md` — do not re-derive it here.
- **Silent on redeploy** — 0 rows · identical hash · 0 CDC.
- **Header** — `[PERMANENT · idempotent]`, `Retire: never`.

The proving-ground's live reference seed is `../Data/Seed.sql` (the make-mandatory / FK / dedupe
scenarios ride on it), included by `../Script.PostDeployment.sql`. New reference seeds an agent
authors during a proof go here and are `:r`-included under the reference-data heading.
