---
name: rename-attribute
description: Use when the developer says "rename the attribute", "change FirstName to GivenName", "I renamed the field in Service Studio", "rename this column" — an existing column getting a new name. The Naked Rename trap at column grain.
---

# Rename attribute (Naked Rename)

> **Default (provisional — the data decides).** With refactorlog → Mechanism 1 Pure Declarative (a metadata `sp_rename`), Tier 3 (every caller breaks). Without refactorlog → CATASTROPHE (DROP COLUMN+ADD, all values lost).

## OutSystems phrasing
"rename the attribute", "change FirstName to GivenName", "I renamed the field in Service Studio".

## SSDT meaning
With a **refactorlog entry**, SSDT emits `EXEC sp_rename 'schema.Table.Old', 'New', 'COLUMN'` —
data preserved. **Without** it, SSDT sees the old column gone and a new one appear and emits `DROP
COLUMN [Old]` + `ADD [New]` — all values in that column lost. Edit the CREATE; never write `ALTER`.

## The named trap
**Naked Rename** (handbook 16 = §19.1), with its companion **Refactorlog Cleanup** (§19.6). This
is the identity-vs-name concern — see `../../_index/identity-and-refactorlog/SKILL.md`; do not
re-derive the refactorlog mechanics here.

## How it flips (the specifics only)
- refactorlog entry present → **M1**, delta is `sp_rename ... 'COLUMN'`, Tier 3 (views, procs, ORM mappings, reports, ETL break)
- **refactorlog entry MISSING → CATASTROPHE** — delta is `DROP COLUMN`+`ADD`; STOP and demand the refactorlog (see `../../_index/identity-and-refactorlog/SKILL.md`)
- CDC-enabled → capture instance names the old column → **M5, +1 Tier**, recreate (see `../../_index/cdc/SKILL.md`)
- external consumers must keep the old name → backward-compat view (`../compat-view/SKILL.md`) → multi-PR coexistence

## Prove it
Script the delta and **read it** — it MUST be `sp_rename ... 'COLUMN'`. If you see `DROP
COLUMN`/`ADD`, the refactorlog is missing; catch it in the delta, never after deploy. Confirm the
`.refactorlog` changed when the rename was authored. For the publish loop, see
`../../prove-on-dacpac/SKILL.md`.

## Verdict to the developer
"Renaming the attribute keeps the data only if the refactorlog entry exists — I read the
generated delta and confirmed it's `sp_rename`, not a drop-and-add. Without that entry SSDT would
have dropped the column and lost every value. Pure Declarative with refactorlog, Tier 3 because
every caller of that column name has to change."

## Teach it (the graduation)
The refactorlog carries identity, not cosmetics (see
`../../_index/identity-and-refactorlog/SKILL.md`). Fail mode avoided: renaming a field by editing
text alone — read the delta every time and catch the loss in the script where it's free, not in
production where it isn't.
