# Operations — Constraints (FAMILY INDEX)

> Constraints — DEFAULTs, UNIQUE, CHECK, and constraint-trust state — are where "the text
> builds but the data refuses" bites hardest: a UNIQUE on duplicates and a CHECK on violating
> rows both build clean and **block the deployment at deploy**. These cannot be classified from
> the `.sql` text alone. **Proving is classifying.** One op here — enable/disable constraint
> *trust* — is **OPERATIONAL, not declarative**.

**This file is now an INDEX.** The op specifics live in the per-op skills; the shared reasoning
lives in `_index/`. Nothing here restates a guard or the flip specifics.

## The ops (table of contents)

| Op | Per-op skill | What it is / how it flips |
|---|---|---|
| add-default | `../op/add-default/SKILL.md` | Named DEFAULT for new rows. Applied in place, no data touched; any team member can review. Never backfills existing rows. |
| modify-default | `../op/modify-default/SKILL.md` | Change/remove default = DROP-then-ADD. Applied in place, no data touched; any team member can review. Never re-stamps old rows. |
| add-unique | `../op/add-unique/SKILL.md` | UNIQUE over all rows. Duplicates block the deployment → ships with a pre-deployment script first; one-NULL-only bites nullable cols (filtered-index fix). |
| add-check | `../op/add-check/SKILL.md` | CHECK WITH CHECK over all rows. Violators block the deployment → ships with a pre-deployment script first; NOCHECK leaves it untrusted. |
| toggle-trust | `../op/toggle-trust/SKILL.md` | ⚠️ OPERATIONAL — refuse-and-route. NOCHECK/WITH CHECK CHECK is a script verb; prove it ends `is_not_trusted=0`. |

## Shared concerns for this family

- **The UNIQUE / CHECK deploy-time block, the NOCHECK→re-trust ladder, UNIQUE-one-NULL + filtered index** — a constraint is a claim proven at apply time → `../_index/constraint-is-a-claim/SKILL.md`.
- **Multi-phase** when violators can't be fixed in-place (grandfather / quarantine) → `../_index/multi-phase/SKILL.md`.
- **Backfill** of existing rows after a default (the separate idempotent-UPDATE op) → `../_index/idempotent-seed/SKILL.md`.
- **CDC** adds scrutiny on a tracked table → `../_index/cdc/SKILL.md` (CDC does not track constraints; the added scrutiny is the high-stakes-table rule).

## Handbook offset reminder
Uniform +3: file `13` = §16 (Operation Reference), `14` = §17 (patterns), `15` = §18 (decision
cascade / declarative table), `16` = §19 (anti-patterns gallery). Cite by filename.
