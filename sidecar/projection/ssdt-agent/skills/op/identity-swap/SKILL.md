---
name: identity-swap
description: Use when the developer says "turn on Auto Number for the Id", "make the Id auto-increment", "stop auto-numbering, I want to set Ids myself", "switch this entity to a database-generated key" — adding or removing IDENTITY. SSDT destination = a table rebuild (shadow table + IDENTITY_INSERT copy + reseed + FK drop/recreate), NOT a simple alter.
---

# Add / remove IDENTITY (Auto Number) (silent-table-rebuild trap)

> **Default (provisional — the data decides).** Mechanism 5 Multi-Phase / Script-Only-grade orchestration, multi-PR on a populated table with FKs — but PREVIEW the delta and CONFIRM it is a rebuild with `SET IDENTITY_INSERT`.

## OutSystems phrasing
"turn on Auto Number for this entity's Id", "make the Id auto-increment", "stop auto-numbering, I want to set Ids myself".

## SSDT meaning
**You cannot `ALTER` a column into or out of `IDENTITY`** — it is a table property fixed at column creation. SSDT implements it as a **table rebuild**: create a shadow table with the new IDENTITY property, copy all data with `SET IDENTITY_INSERT ON` to preserve key values, reseed IDENTITY to `MAX(Id)+1`, recreate every FK that pointed at the table, drop the old table, rename the shadow into place. Handbook file 14 (=§17.3). Never write ALTER.

## The named trap
**The silent rebuild** — the .sql edit (adding `IDENTITY(1,1)` to the CREATE) looks trivial but SSDT's generated delta is a full shadow-table swap that drops and recreates the table. If FKs aren't all recreated or `IDENTITY_INSERT` isn't used, existing keys are re-minted and every FK points at the wrong rows. This is the most dangerous "one-line edit" in the catalog. Adjacent: the *Naked Rename* family (a rebuild that loses the mapping re-mints keys the way a naked rename loses a column) — see `../../_index/identity-and-refactorlog/SKILL.md`. This shadow-table-rebuild mechanic is owned here; `../retype-explicit/SKILL.md` and `../indexed-view/SKILL.md` cross-reference it.

## How it flips (the specifics only)
- table **empty, no FKs** → the rebuild is trivial; effectively **M1** (still confirm the delta is a rebuild). Tier 2.
- table populated, **no incoming FKs** → **M3/4**, single-PR with `IDENTITY_INSERT` reseed proven. Tier 3.
- table populated **WITH incoming FKs** → **M5 Multi-Phase, multi-PR**; the FK drop/recreate must bracket the rebuild. Tier 3, +1 if first-time.
- **+ CDC** → +1 Tier; the rebuild invalidates the capture instance — disable CDC first, rebuild, re-enable — see `../../_index/cdc/SKILL.md`.
- **+ >1M rows** → +1 Tier; the data copy is the expensive part.

## Prove it
Preview the Strict delta and CONFIRM it is a **shadow-table rebuild with `SET IDENTITY_INSERT`**, not a no-op — if SSDT does not show the rebuild, the IDENTITY edit did not register. After a permissive publish, hash every Id before/after and prove they are **unchanged** (reseed preserved them) and that every FK still resolves (zero orphans introduced). See `prove-on-dacpac` / `talk-to-local-sql`. On the sample, add IDENTITY to `dbo.Category` (explicit-id, NO IDENTITY — the source) with `dbo.Order` / `dbo.OrderLine` as the incoming-FK shape (STR-04).

## Verdict to the developer
"Turning on Auto Number can't be a simple alter — SSDT has to rebuild the whole table. I proved on a copy that the rebuild preserves every existing Id and every foreign key still resolves; without the IDENTITY_INSERT step your keys would be re-minted and references broken. It's sequenced across multiple PRs because the FKs have to be dropped and recreated around the rebuild."

## Teach it (the graduation)
The size of an .sql edit predicts nothing about the size of the deploy — the most dangerous change in the catalog is a one-line edit whose delta is a table swap, and the only honest way to know is to preview the journey and confirm key preservation. Full WHY (identity vs. name): `../../_index/identity-and-refactorlog/SKILL.md`.
