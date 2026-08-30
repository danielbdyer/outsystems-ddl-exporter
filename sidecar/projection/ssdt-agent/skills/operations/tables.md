# Operations — Tables (Entities) — FAMILY INDEX

> **This file is now an INDEX.** The op specifics live in the per-op skills under
> `../op/<slug>/SKILL.md`; the shared reasoning lives in `../_index/`. Nothing here restates a
> guard or how a change flips.

Whole-table operations. The developer thinks in **Entities**; in SQL these are `CREATE TABLE`
destinations and what SSDT's publish engine does to existing data. This family's character:
*additive at one end (create, junction) and irreversible at the other (delete, archive)*. It is
where the two findings pull furthest apart — a `DROP TABLE` ships as a single scripted release, yet
a dev lead approves this, with the strongest weigh-line because the data is removed and cannot be recovered. **Proving is
classifying.** How a change ships, and what the approving dev lead weighs, is stated only after it is confirmed on a
disposable copy of Dev (`../prove-on-dacpac/SKILL.md`).

## The ops (table of contents)

Every line names the op's **SHIP terminal** — the deployment shape the S5 sub-machine decides
(`../../THE_DECISION_TREE.md`), proven live on this branch and written up in the op's worked PR
(`../../sample-prs/<op>.md`, an instance of the ten-section template `../author-pr/SKILL.md`).

| Op | Per-op skill | SHIP terminal · what it is / how it flips |
|---|---|---|
| create-entity | `../op/create-entity/SKILL.md` | **ONE RELEASE, in place.** New table, additive — `CREATE TABLE` emitted verbatim, no existing data touched; the new table's foreign key lands trusted. The only risk is a dependency (a missing FK parent or a file the glob misses), caught at build time, not a data flip. |
| rename-entity | `../op/rename-entity/SKILL.md` | **ONE RELEASE, in place — only with the refactorlog.** A refactorlog entry makes the delta a metadata `EXEC sp_rename … 'OBJECT'` (every row and the object_id preserved); without it the delta is `DROP TABLE` + `CREATE TABLE` and the rows are lost. Every caller of the old name must change. |
| delete-entity | `../op/delete-entity/SKILL.md` | **ONE RELEASE plus a human fork.** An explicit, idempotent scripted `DROP TABLE` with the `.sql` removed in the same release, under the production posture so DacFx neither generates the drop (the gate blocks it on a populated table — `Msg 50000`) nor re-creates the table. The narrow's two-release pattern does not transfer. The fork: is the data truly safe to lose? |
| move-schema | `../op/move-schema/SKILL.md` | **ONE RELEASE, in place.** `ALTER SCHEMA TRANSFER` (or a refactorlog entry) preserves the data, `object_id`, and counts — the same identity discipline as a rename. Without the identity mapping a header edit does DROP + CREATE and the rows are lost. Every `schema.Table` reference must follow the move. |
| archive-entity | `../op/archive-entity/SKILL.md` | **MULTI-PHASE across releases.** A data move, not a shape change: the archive table is added (additive, one release), then a batched `DELETE … OUTPUT DELETED.* INTO archive` moves the rows (raw DML the gate does not govern), then the counts are reconciled. The conservation-count proof settles it. |
| junction | `../op/junction/SKILL.md` | **ONE RELEASE, in place.** M:N bridge — one `CREATE TABLE` whose composite PK spans two FK columns; no existing data touched. The shape *is* the guarantee (no orphan pair via the two FKs — `Msg 547`; no duplicate pair via the composite PK — `Msg 2627`). Seed pairs with a missing parent block the publish and route to `../op/create-fk-orphan/SKILL.md`. |

## Shared concerns for this family (the `_index` layer)

- `../_index/identity-and-refactorlog/SKILL.md` — governs **rename-entity, move-schema**: identity is separate from name; a cross-table move has no refactorlog identity mapping.
- `../_index/tightening-class/SKILL.md` — the **delete-entity** populated-table row-presence guard.
- `../_index/multi-phase/SKILL.md` — **archive-entity** coexistence + the conservation proof before any subtractive move.
- `../_index/constraint-is-a-claim/SKILL.md` — **junction** orphan-pair FK validation.

## Handbook citation reminder

Handbook files are cited by FILENAME with a **+3 offset**: `13` = §16 (Operation Reference),
`14` = §17 (patterns), `15` = §18, `16` = §19 (anti-patterns).
