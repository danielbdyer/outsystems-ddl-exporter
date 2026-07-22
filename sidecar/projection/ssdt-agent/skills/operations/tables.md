# Operations — Tables (Entities) — FAMILY INDEX

> **This file is now an INDEX.** The op specifics live in the per-op skills under
> `../op/<slug>/SKILL.md`; the shared reasoning lives in `../_index/`. Nothing here restates a
> guard or how a change flips.

Whole-table operations. The developer thinks in **Entities**; in SQL these are `CREATE TABLE`
destinations and what SSDT's publish engine does to existing data. This family's character:
*additive at one end (create, junction) and irreversible at the other (delete, archive)*. It is
where the two findings pull furthest apart — a `DROP TABLE` ships as a single scripted release, yet
a principal must review it because the data is removed and cannot be recovered. **Proving is
classifying.** How a change ships, and who must review it, is stated only after it is confirmed on a
disposable copy of Dev (`../prove-on-dacpac/SKILL.md`).

## The ops (table of contents)

| Op | Per-op skill | What it is / how it flips |
|---|---|---|
| create-entity | `../op/create-entity/SKILL.md` | New table. Additive — ships in place as a single schema change, any team member can review it; the only risk is a dependency (a missing FK parent, caught at build time), not a data flip. |
| rename-entity | `../op/rename-entity/SKILL.md` | Rename an existing table. A refactorlog entry makes it a metadata `sp_rename` (ships in place, data preserved), reviewed by a dev lead or experienced developer because every caller of the old name must change; a rename with no refactorlog entry becomes DROP + CREATE and every row is lost. |
| delete-entity | `../op/delete-entity/SKILL.md` | Drop a table. Ships as a scripted change in one release, but a principal must review it — the data is removed and cannot be undone; the block on a populated table is the safety proof. |
| move-schema | `../op/move-schema/SKILL.md` | Schema change — the same refactorlog trap as a rename. With the refactorlog (or a scripted `ALTER SCHEMA TRANSFER`) the data, `object_id`, and counts are preserved; without it, DROP + CREATE and the rows are lost. A dev lead or experienced developer reviews it — every `schema.Table` reference must follow the move. |
| archive-entity | `../op/archive-entity/SKILL.md` | Data move, not a shape change. Ships across releases (multi-phase) — the archive table is added, then a batched migrate, then the counts are reconciled; a dev lead reviews the relocation of existing data (a principal at large volume). The conservation-count proof settles it. |
| junction | `../op/junction/SKILL.md` | M:N bridge — a composite PK over two FKs. Ships in place as a single schema change; a dev lead reviews the two cross-table relationships added. The shape *is* the guarantee (no orphans via the two FKs, no duplicate pairs via the composite PK). |

## Shared concerns for this family (the `_index` layer)

- `../_index/identity-and-refactorlog/SKILL.md` — governs **rename-entity, move-schema**: identity is separate from name; a cross-table move has no refactorlog identity mapping.
- `../_index/tightening-class/SKILL.md` — the **delete-entity** populated-table row-presence guard.
- `../_index/multi-phase/SKILL.md` — **archive-entity** coexistence + the conservation proof before any subtractive move.
- `../_index/constraint-is-a-claim/SKILL.md` — **junction** orphan-pair FK validation.

## Handbook citation reminder

Handbook files are cited by FILENAME with a **+3 offset**: `13` = §16 (Operation Reference),
`14` = §17 (patterns), `15` = §18, `16` = §19 (anti-patterns).
