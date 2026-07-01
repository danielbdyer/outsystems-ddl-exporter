---
name: rename-entity
description: Use when the developer says "rename the Entity", "change the table name from Customer to Client", "I renamed it in Service Studio" — an existing table getting a new name. The Naked Rename trap; identity must be carried by the refactorlog.
---

# Rename entity (Naked Rename)

> **Default (provisional — the data decides).** With refactorlog → Mechanism 1 Pure Declarative (a metadata `sp_rename`), Tier 3 (every caller breaks). Without refactorlog → CATASTROPHE (DROP+CREATE, all rows lost).

## OutSystems phrasing
"rename the Entity", "change the table name from Customer to Client", "I renamed it in Service Studio".

## SSDT meaning
With a **refactorlog entry**, SSDT emits `EXEC sp_rename 'schema.Old', 'New', 'OBJECT'` — data
and `object_id` preserved. **Without** the entry SSDT sees one table vanish and a new one
appear and emits `DROP TABLE [Old]` + `CREATE TABLE [New]` — all rows lost. Never write `ALTER`.

## The named trap
**Naked Rename** (handbook 16 = §19.1), with its companion **Refactorlog Cleanup** (§19.6).
This is the identity-vs-name concern — see `../../_index/identity-and-refactorlog/SKILL.md`; do
not re-derive the refactorlog mechanics here.

## How it flips (the specifics only)
- refactorlog entry present → **M1**, delta is `sp_rename`, Tier 3 (cross-boundary: FKs, views, procs, ETL, reports all reference the name)
- **refactorlog entry MISSING → CATASTROPHE** — delta is `DROP`+`CREATE`; STOP and demand the refactorlog before anything else (see `../../_index/identity-and-refactorlog/SKILL.md`)
- table is CDC-enabled → the capture instance still names the old table → **M5 Multi-Phase, +1 Tier**, recreate (see `../../_index/cdc/SKILL.md`)
- external consumers must keep the old name → add a backward-compat view (`../compat-view/SKILL.md`), pushing toward multi-PR coexistence

## Prove it
Run `sqlpackage /Action:Script` and **read the delta** — it MUST be `sp_rename`. If you see
`DROP TABLE`/`CREATE TABLE`, the refactorlog is missing; this is the single most important
delta-read in the whole catalog. Confirm the `.refactorlog` file changed when the rename was
authored. For the publish loop, see `../../prove-on-dacpac/SKILL.md`.

## Verdict to the developer
"You renamed the entity. I published it to a copy — SSDT generated `sp_rename`, so the rows are
preserved. That only works because the refactorlog entry exists; without it SSDT would DROP and
re-CREATE and lose everything. Pure Declarative with refactorlog, Tier 3 because every caller
breaks."

## Teach it (the graduation)
The refactorlog carries *identity, not text* — see `../../_index/identity-and-refactorlog/SKILL.md`
for the full why. The fail mode avoided: renaming by editing the CREATE alone, the most
expensive silent loss in the catalog.
