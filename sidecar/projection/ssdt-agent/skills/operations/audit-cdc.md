# Operations — Audit, temporal versioning & CDC (FAMILY INDEX)

> **CDC is the team's single biggest tripwire.** Read `../_index/cdc/SKILL.md` before touching
> any CDC-related change. CDC is **not expressible in the declarative dacpac model**, is
> **Enterprise/Standard-licensed**, and once on, **every subsequent schema change on that table
> needs capture-instance management** — a standing added scrutiny that lands on every later change
> to the table and never goes away.

> **This file is now an INDEX.** The op specifics live in the per-op skills under `../op/`; the
> shared reasoning lives in `../_index/`. Nothing here restates a guard or how an op flips.

**Family framing.** The audit/versioning family the developer reaches for when they say "track
changes": temporal (system-versioned) tables, manual audit columns, CDC, recreating a capture
instance after a schema change, and change-tracking. Your job is to find out **which kind of
history** they mean and to make the CDC consequence loud, because it is the thing that bites the
whole team for months. The dominant state-variable here is **(3) CDC-enabled + no-gap required?** —
a no-gap requirement turns almost any co-occurring change into one staged across releases (multi-PR).

## Ops in this family

| Op | Per-op skill | What it is / how it flips |
|---|---|---|
| temporal-new | `../op/temporal-new/SKILL.md` | system-versioning on a NEW entity; declarative from birth (ships in place); lead / experienced-dev review |
| temporal-convert | `../op/temporal-convert/SKILL.md` | system-versioning on an EXISTING populated table; staged across releases (multi-PR); dev-lead review; backfill ROW START |
| audit-columns | `../op/audit-columns/SKILL.md` | CreatedBy/On etc.; nullable ⇒ additive, any-team review; NOT NULL + populated ⇒ pre-deploy backfill (tightening class) |
| enable-cdc | `../op/enable-cdc/SKILL.md` | **the tripwire**; script-only; dev-lead review, with added scrutiny the first time on the estate; NOT in the dacpac; a standing per-change scrutiny on the table that never goes away |
| recreate-capture-instance | `../op/recreate-capture-instance/SKILL.md` | schema change on a CDC table; dual-instance v1/v2; staged across releases (multi-PR) when no-gap required; silent-missing-column |
| change-tracking | `../op/change-tracking/SKILL.md` | the light CDC sibling; script-only, lead / experienced-dev review; which-rows not old-values; no standing scrutiny |

## Shared concerns for this family
- **`../_index/cdc/SKILL.md`** — the standing added-scrutiny consequence, the CDC-Surprise three
  faces, dual-instance capture management, silence-as-the-failure-mode, and the **mandatory
  isolation rule** (`sp_cdc_enable_db` flips instance-wide state — unique DB only, never the shared
  warm container; CLAUDE.md survival rule 1 + PROTOCOL §8). (Governs enable-cdc,
  recreate-capture-instance, change-tracking, and the added-scrutiny face of every op on a CDC
  table.)
- **`../_index/multi-phase/SKILL.md`** — the coexistence staging behind temporal-convert and the
  no-gap dual-instance migration.
- **`../_index/tightening-class/SKILL.md`** — the row-presence guard behind NOT-NULL audit columns
  on a populated table: the deployment is blocked while the table holds rows (audit-columns
  delegates the guard here; `make-mandatory` owns it).

> Handbook offset reminder (+3): file `14` = §17 (`§17.9` recreate-capture-instance), `16` = §19
> (the **CDC Surprise** anti-pattern), `12` = CDC-and-Schema-Evolution, `27` = CDC-Table-Registry.
> Cite by filename.

## Connector points
- CDC proving **must** run on an isolated, disposable DB, never the shared warm container — a hard
  boundary (CLAUDE.md survival rule 1; `../_index/cdc/SKILL.md` and `talk-to-local-sql` carry the
  isolation pattern).
- The F# engine's `PostDeployEmitter` can generate the CDC/change-tracking enable scripts from a
  real catalog (see `CONNECTORS.md`); the "dacpac ignores the declarative attempt, so it must be a
  script" proof is unchanged.
- Any review packet shipping a CDC-touching change should carry the **standing added-scrutiny
  consequence** explicitly — the capture instance is frozen to the table's current columns and
  every later schema change on it needs handling — so the reviewer and the promotion gate both see
  the future cost.
