# Operations — Tables (Entities) — FAMILY INDEX

> **This file is now an INDEX.** The op specifics live in the per-op skills under
> `../op/<slug>/SKILL.md`; the shared reasoning lives in `../_index/`. Nothing here restates a
> guard or a flip mechanism.

Whole-table operations. The developer thinks in **Entities**; you think in `CREATE TABLE`
destinations and what SSDT's publish engine does to existing data. This family's character:
*additive at one end (create, junction) and irreversible at the other (delete, archive)* — the
danger axis and the release axis diverge most sharply here (a one-PR `DROP TABLE` is Tier 4).
**Proving is classifying.** Never report a mechanism you have not confirmed on the proving
ground (`../prove-on-dacpac/SKILL.md`).

## The ops (table of contents)

| Op | Per-op skill | What it is / how it flips |
|---|---|---|
| create-entity | `../op/create-entity/SKILL.md` | New table. Additive M1/Tier 1; the only risk is a dependency (missing FK parent), not a data flip. |
| rename-entity | `../op/rename-entity/SKILL.md` | Naked Rename. With refactorlog → `sp_rename` M1/Tier 3; without → DROP+CREATE catastrophe. |
| delete-entity | `../op/delete-entity/SKILL.md` | Drop a table. M4/single-PR mechanically but Tier 4; the populated-table veto is the safety proof. |
| move-schema | `../op/move-schema/SKILL.md` | Schema change. The rename trap again — refactorlog or `ALTER SCHEMA TRANSFER`, else DROP+CREATE. |
| archive-entity | `../op/archive-entity/SKILL.md` | Data move, not a shape change. M5 Multi-Phase; conservation-count proof. |
| junction | `../op/junction/SKILL.md` | M:N bridge — composite PK over two FKs. M1/Tier 2; the shape *is* the guarantee (no orphans, no dup pairs). |

## Shared concerns for this family (the `_index` layer)

- `../_index/identity-and-refactorlog/SKILL.md` — governs **rename-entity, move-schema** (and the compat-view bridge): identity is separate from name; a cross-table move has no refactorlog identity mapping.
- `../_index/tightening-class/SKILL.md` — the **delete-entity** populated-table row-presence veto.
- `../_index/multi-phase/SKILL.md` — **archive-entity** coexistence + the conservation proof before any subtractive move.
- `../_index/constraint-is-a-claim/SKILL.md` — **junction** orphan-pair FK validation.
- `../_index/cdc/SKILL.md` — the `+1` face of every op on a CDC-tracked table.

## Handbook citation reminder

Handbook files are cited by FILENAME with a **+3 offset**: `13` = §16 (Operation Reference),
`14` = §17 (patterns), `15` = §18, `16` = §19 (anti-patterns).
