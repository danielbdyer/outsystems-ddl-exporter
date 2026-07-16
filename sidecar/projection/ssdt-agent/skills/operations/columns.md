# Operations — Columns (Attributes) — FAMILY INDEX

> **This file is now an INDEX.** The op specifics live in the per-op skills under
> `../op/<slug>/SKILL.md`; the shared reasoning lives in `../_index/`. Nothing here restates a
> guard or how a change flips.

Attribute-level operations. The developer thinks in **Entity Attributes**; in SQL these are `ALTER
COLUMN` destinations and what SSDT does to the existing rows. This is the **densest, most
flip-heavy** family in the catalog: the *same* one-line edit (make-mandatory, narrow, retype) ships
a different way — and can need a different reviewer — depending entirely on the data, which the
`.sql` text does not reveal. **Proving is classifying.** How a change ships, and who must review it,
is stated only after it is confirmed on a disposable copy of Dev (`../prove-on-dacpac/SKILL.md`).
And the `ALTER` is **never authored by hand** — edit the CREATE to the destination, and SSDT
computes the ALTER.

## The ops (table of contents)

| Op | Per-op skill | What it is / how it flips |
|---|---|---|
| add-optional | `../op/add-optional/SKILL.md` | New NULL column. The safest change — applies in place and any team member can review it, in any table state (NULL is always valid). |
| add-mandatory | `../op/add-mandatory/SKILL.md` | New NOT NULL column (the Optimistic NOT NULL trap). A populated table needs a DEFAULT, or the deployment is blocked. |
| make-mandatory | `../op/make-mandatory/SKILL.md` | NULL→NOT NULL. **The canonical tightening-class change** — an empty table applies in place; a populated table is blocked on row-presence even with zero NULLs. |
| make-optional | `../op/make-optional/SKILL.md` | NOT NULL→NULL. A loosening is never blocked; the risk is downstream consumers. |
| widen | `../op/widen/SKILL.md` | Enlarge type. Applies in place; the one coupling is the index-key byte limit. |
| narrow | `../op/narrow/SKILL.md` | Shrink type (the Ambitious Narrowing trap). Tightening class — a populated table is blocked even when every value fits. |
| retype-implicit | `../op/retype-implicit/SKILL.md` | Widening/lossless cast (INT→BIGINT). Applies in place; any team member can review it. |
| retype-explicit | `../op/retype-explicit/SKILL.md` | Value-reshaping/lossy cast (Text→Date). Ships across releases (multi-phase); the TRY_CONVERT count drives it. |
| rename-attribute | `../op/rename-attribute/SKILL.md` | Rename at column grain. A refactorlog entry makes it `sp_rename` (data preserved); a rename with no refactorlog entry becomes DROP COLUMN + ADD and loses the column's data. |
| delete-attribute | `../op/delete-attribute/SKILL.md` | Drop a column. The 4-phase deprecation; a populated column is blocked on row-presence; a principal must review once data would be lost. |

## Shared concerns for this family (the `_index` layer)

- `../_index/tightening-class/SKILL.md` — governs **make-mandatory, narrow, delete-attribute** (the data-blind table-has-rows guard). The failure of classifying from the `.sql` text lives here.
- `../_index/identity-and-refactorlog/SKILL.md` — governs **rename-attribute**: identity is separate from name.
- `../_index/multi-phase/SKILL.md` — **retype-explicit, delete-attribute** coexistence + conservation proof.
- `../_index/cdc/SKILL.md` — the added-scrutiny face of every column op on a CDC-tracked table (a new tracked column is silent until the capture instance is recreated).

## Handbook citation reminder

Handbook files are cited by FILENAME with a **+3 offset**: `13` = §16 (Operation Reference),
`14` = §17 (patterns), `15` = §18, `16` = §19 (anti-patterns).
