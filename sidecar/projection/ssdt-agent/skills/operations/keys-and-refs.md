# Operations — Keys and References (FAMILY INDEX)

> In OutSystems the **Identifier** was automatic and **References** were lines you drew between
> entities; now you declare primary keys and foreign keys explicitly, and SSDT validates them
> against the **existing rows** at deploy time. That validation is where the shipping shape is
> decided: a foreign key against clean data is single-phase; the same foreign key against one
> orphan row is blocked and must ship as a script. The `.sql` text cannot tell you which — only a
> publish against the real rows can. **Proving is classifying.** This is the sharpest
> constraint-is-a-claim family in the catalog.

**This file is now an INDEX.** The op specifics live in the per-op skills; the shared reasoning
lives in `_index/`. Nothing here restates a guard or the specifics of how a change flips.

## The ops (table of contents)

| Op | Per-op skill | What it is / how it flips |
|---|---|---|
| define-PK | `../op/define-pk/SKILL.md` | The Identifier. New table: ships in place, additive — any team member can review. Populated: a clustered-index build over every row; blocked by duplicate or NULL keys, which routes to a pre-deployment fix or a staged release; a dev lead or experienced developer reviews the build over live data. |
| create-FK clean | `../op/create-fk-clean/SKILL.md` | Clean child data. Ships in place — one `ADD CONSTRAINT`, no data modified; a dev lead reviews the new cross-table relationship. Prove zero orphans; if any, route to create-fk-orphan. |
| create-FK with orphans | `../op/create-fk-orphan/SKILL.md` | Dirty data. Ships as a scripted change: NOCHECK → reconcile → WITH CHECK CHECK, ends trusted; a dev lead reviews, existing data is modified. |
| change-delete-rule / cascade | `../op/change-delete-rule/SKILL.md` | Protect/Ignore/Delete → an ON DELETE action, applied in place via DROP+ADD; no data modified. A dev lead reviews the behavioural change — CASCADE lets one parent delete remove child rows in another table, widening the dependency scope. |
| drop-FK | `../op/drop-fk/SKILL.md` | Remove the reference. Always ships in place, clean — the publish never blocks. A dev lead or experienced developer reviews the weakened integrity and the optimizer-plan shift. |

## Shared concerns for this family

- **Every blocked FK or PK** is a claim about existing data, proven at apply time → `../_index/constraint-is-a-claim/SKILL.md` (orphan probe, duplicate/NULL probe, the NOCHECK→reconcile→WITH CHECK CHECK trust ladder, `is_not_trusted=0` end-state).
- **Multi-phase** staging when a reconcile spans releases (orphan FK, coexistence) → `../_index/multi-phase/SKILL.md`.

## Handbook offset reminder
Uniform +3: file `13` = §16 (Operation Reference), `14` = §17 (patterns), `15` = §18 (decision
cascade / declarative table), `16` = §19 (anti-patterns gallery). Cite by filename.
