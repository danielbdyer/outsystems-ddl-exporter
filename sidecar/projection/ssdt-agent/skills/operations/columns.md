# Operations — Columns (Attributes) — FAMILY INDEX

> **This file is now an INDEX.** The op specifics live in the per-op skills under
> `../op/<slug>/SKILL.md`; the shared reasoning lives in `../_index/`. Nothing here restates a
> guard or a flip mechanism.

Attribute-level operations. The developer thinks in **Entity Attributes**; you think in `ALTER
COLUMN` destinations and what SSDT does to existing rows. This is the **densest, most flip-heavy**
family in the catalog: the *same* one-line edit (make-mandatory, narrow, retype) lands in a
different mechanism depending entirely on the data — the `.sql` text cannot tell you which.
**Proving is classifying.** Never report a mechanism you have not confirmed on the proving ground
(`../prove-on-dacpac/SKILL.md`). And **you never write `ALTER`** — edit the CREATE to the
destination; SSDT computes the ALTER.

## The ops (table of contents)

| Op | Per-op skill | What it is / how it flips |
|---|---|---|
| add-optional | `../op/add-optional/SKILL.md` | New NULL column. Safest change — M1/Tier 1 any table state (NULL is always valid). |
| add-mandatory | `../op/add-mandatory/SKILL.md` | New NOT NULL column (Optimistic NOT NULL). Populated needs a DEFAULT or it vetoes. |
| make-mandatory | `../op/make-mandatory/SKILL.md` | NULL→NOT NULL. **THE tightening-class spine** — empty=M1, populated vetoes on row-presence even with zero NULLs. |
| make-optional | `../op/make-optional/SKILL.md` | NOT NULL→NULL. Loosening never vetoes; risk is downstream consumers. |
| widen | `../op/widen/SKILL.md` | Enlarge type. M1/Tier 2; the one coupling is the index-key byte limit. |
| narrow | `../op/narrow/SKILL.md` | Shrink type (Ambitious Narrowing). Tightening class — populated vetoes even when every value fits. |
| retype-implicit | `../op/retype-implicit/SKILL.md` | Widening/lossless cast (INT→BIGINT). M1/Tier 2. |
| retype-explicit | `../op/retype-explicit/SKILL.md` | Value-reshaping/lossy cast (Text→Date). M5 Multi-Phase; TRY_CONVERT count. |
| rename-attribute | `../op/rename-attribute/SKILL.md` | Naked Rename at column grain. refactorlog `sp_rename` or DROP COLUMN+ADD catastrophe. |
| delete-attribute | `../op/delete-attribute/SKILL.md` | Drop a column. 4-phase deprecation; populated-column veto; Tier 4 once data destroyed. |

## Shared concerns for this family (the `_index` layer)

- `../_index/tightening-class/SKILL.md` — the flagship: governs **make-mandatory, narrow, delete-attribute** (the data-blind table-has-rows guard). The `.sql`-text-classification failure lives here.
- `../_index/identity-and-refactorlog/SKILL.md` — governs **rename-attribute**: identity is separate from name.
- `../_index/multi-phase/SKILL.md` — **retype-explicit, delete-attribute** coexistence + conservation proof.
- `../_index/cdc/SKILL.md` — the `+1` face of every column op on a CDC-tracked table (a new tracked column is silent until the capture instance is recreated).

## Handbook citation reminder

Handbook files are cited by FILENAME with a **+3 offset**: `13` = §16 (Operation Reference),
`14` = §17 (patterns), `15` = §18, `16` = §19 (anti-patterns).
