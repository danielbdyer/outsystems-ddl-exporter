# Operations — Keys and References (FAMILY INDEX)

> In OutSystems the **Identifier** was automatic and **References** were lines you drew between
> entities; now you declare primary keys and foreign keys explicitly, and SSDT validates them
> against the **existing rows** at deploy time. That validation is exactly where the bucket
> flips: a foreign key against clean data is single-phase; the same foreign key against one
> orphan row vetoes and becomes a script. The `.sql` text cannot tell you which. **Proving is
> classifying.** This is the sharpest constraint-is-a-claim family in the catalog.

**This file is now an INDEX.** The op specifics live in the per-op skills; the shared reasoning
lives in `_index/`. Nothing here restates a guard or a flip mechanism.

## The ops (table of contents)

| Op | Per-op skill | What it is / how it flips |
|---|---|---|
| define-PK | `../op/define-pk/SKILL.md` | The Identifier. New table = M1 inline; populated = clustered-index build, vetoes on duplicate/NULL keys → M3/M5. |
| create-FK clean | `../op/create-fk-clean/SKILL.md` | Clean child data. M1 Tier 2. Prove zero orphans; if orphans, route to create-fk-orphan. |
| create-FK with orphans | `../op/create-fk-orphan/SKILL.md` | Dirty data. M4 Script-Only Tier 3: NOCHECK → reconcile → WITH CHECK CHECK, ends trusted. |
| change-delete-rule / cascade | `../op/change-delete-rule/SKILL.md` | Protect/Ignore/Delete → ON DELETE action via DROP+ADD. M1 publish, Tier 3 behavioral (CASCADE blast radius). |
| drop-FK | `../op/drop-fk/SKILL.md` | Remove the reference. Always M1 clean; Tier 2 for lost integrity + optimizer-plan shift. |

## Shared concerns for this family

- **Every FK/PK veto** is a claim about existing data proven at apply time → `../_index/constraint-is-a-claim/SKILL.md` (orphan probe, duplicate/NULL probe, the NOCHECK→reconcile→WITH CHECK CHECK trust ladder, `is_not_trusted=0` end-state).
- **Multi-phase** staging when a reconcile spans releases (orphan FK, coexistence) → `../_index/multi-phase/SKILL.md`.
- **CDC** +1 tripwire on a tracked table → `../_index/cdc/SKILL.md`.

## Handbook offset reminder
Uniform +3: file `13` = §16 (Operation Reference), `14` = §17 (patterns), `15` = §18 (decision
cascade / declarative table), `16` = §19 (anti-patterns gallery). Cite by filename.
