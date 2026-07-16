---
name: enable-cdc
description: Use when the developer says "turn on Change Data Capture for Customer", "we need a change feed for the ETL", "track every insert/update/delete so the warehouse can pick it up", "enable CDC on this entity". CDC is not in the dacpac model — SSDT cannot create, diff, or publish it, so enablement lands as a post-deployment / operational script.
---

# Enable CDC

> **Default (provisional — the data decides).** Ships as a scripted change in a single release — a
> post-deployment / operational script (`sys.sp_cdc_enable_table`); enabling CDC cannot be expressed
> as a table definition, so SSDT cannot create, diff, or publish it. A dev lead must review this:
> enabling CDC stands up capture infrastructure outside the dacpac and binds every later schema
> change on this table to capture-instance management, with added scrutiny for a first-time CDC
> enablement on the estate. The danger drives the review need, not the release count. Prove before
> you classify.

## OutSystems phrasing
"turn on Change Data Capture for Customer", "we need a change feed for the ETL", "track every
insert/update/delete so the warehouse can pick it up".

## SSDT meaning
`sys.sp_cdc_enable_table` (and `sp_cdc_enable_db` at the database level) — this creates `sys`-owned
change tables, a capture instance, and SQL Agent capture/cleanup jobs. **None of this is in the
dacpac model.** SSDT cannot CREATE it, diff it, or publish it — CDC enablement lives in a post-deploy
/ operational script, full stop. Never write ALTER; never try to express it declaratively.

## The named trap
**CDC Surprise** (handbook 16 = §19) with three faces: (1) it's outside the declarative model — a
declarative attempt is silently ignored; (2) it's Enterprise/Standard-licensed — the wrong edition
fails at *runtime*, after deploy; (3) the standing consequence — every future schema change on this
table now needs capture-instance management, for as long as CDC stays on. This whole concern is owned
by `../../_index/cdc/SKILL.md`; do not re-derive the three faces here.

## How it flips (the specifics only)
CDC barely "flips" — it is heavy from the start:
- enable CDC, no no-gap requirement → ships as a scripted change in a single release (a post-deploy /
  operational script); a dev lead must review it, with added scrutiny for a first-time enablement on
  the estate.
- enable CDC on a wrong-edition target → does not change how it ships; it **fails at runtime**, after
  deploy — a disposable copy of Dev must catch this.
- any co-occurring schema change on a now-CDC table → see `../recreate-capture-instance/SKILL.md`;
  that change ships across releases (multiple pull requests) when the capture must miss nothing (a
  no-gap requirement).

## Prove it
(a) Prove attempting CDC declaratively produces **nothing** in the SSDT delta (the dacpac ignores it)
— so the developer sees it must be a script. (b) Run the enable script against the disposable
isolated DB and confirm the change tables + capture instance appear. (c) **Isolation is mandatory**:
`sp_cdc_enable_db` flips instance-wide state — do CDC proving on a disposable isolated DB, **never**
the shared warm container (CLAUDE.md survival rule 1; PROTOCOL §8; see `../../_index/cdc/SKILL.md` and
`talk-to-local-sql`). (d) Confirm the edition supports CDC before claiming success. On the sample,
`dbo.CdcCandidate` is the isolated-DB CDC target (AUD-04).

## The verdict (to the developer)
You asked to turn on Change Data Capture. This isn't a schema edit — SSDT can't describe CDC, so it
can't create, diff, or publish it; it lives in a script, and I proved that by showing the dacpac
ignores the declarative attempt entirely. Two things matter before it ships. It needs Enterprise or
Standard edition: on the wrong edition it won't fail now, it fails at runtime after deploy, so I
confirm the edition on a disposable copy first. And here's the part that lasts: once CDC is on this
entity, every future change to it has to recreate or manage the capture instance, or the change feed
silently drops the new column — no error, just missing data in the warehouse weeks later. That
standing obligation is why this needs a careful review even though the script itself is short. One
thing to confirm: does every environment this reaches — Test, UAT, Prod — run an edition that
supports CDC?

## The reasoning (in conversation)
When a change isn't in the declarative model, ask what *standing* obligation it puts on every future
change to that table, not just what it does today. Enabling CDC is the clearest case: from here on,
every schema change to the table has to recreate or manage the capture instance, or a column silently
drops out of the warehouse feed — and that surfaces months later, long after the short enable script
is forgotten. The failure this avoids is reading a short script as a small change. Full reasoning:
`../../_index/cdc/SKILL.md`.

## On the record
The fragment this op contributes to the pull request (`../../author-pr/SKILL.md`), record register.

**Review & release**
- A dev lead must review this: enabling CDC stands up capture infrastructure outside the dacpac
  (sys-owned change tables, a capture instance, SQL Agent capture/cleanup jobs) and binds every later
  schema change on this table to capture-instance management, or the change feed silently drops
  records (see `../recreate-capture-instance/SKILL.md`).
- Ships as a scripted change in a single release — a post-deployment / operational script
  (`sys.sp_cdc_enable_table`, with `sp_cdc_enable_db` first if the database is not yet CDC-enabled).
  Enabling CDC cannot be expressed as a table definition, so SSDT cannot create, diff, or publish it.
- Added scrutiny: a first-time CDC enablement on this estate. CDC also requires Enterprise or
  Standard edition — the wrong edition fails at runtime, after deploy, not in the publish.

**Verification** — run in each environment after deployment
```sql
-- expect an edition that supports CDC (Enterprise, or Standard on SQL Server 2016 SP1+)
SELECT SERVERPROPERTY('Edition') AS edition, SERVERPROPERTY('EngineEdition') AS engine_edition;

-- expect 1 row, is_tracked_by_cdc = 1: CDC is enabled on the table
SELECT name, is_tracked_by_cdc FROM sys.tables WHERE name = 'CdcCandidate';

-- expect >= 1 row: the capture instance and its change table exist
SELECT capture_instance, source_object_id FROM cdc.change_tables;
```

**Rollback**
CDC is turned off with `sys.sp_cdc_disable_table` (and `sys.sp_cdc_disable_db` at the database level)
— the mirror of the enable script, and likewise an explicit operational script, not the absence of
anything in the dacpac. Disabling drops the capture instance and its change tables, so the change
history they hold is lost; the source table's own rows are untouched. The forward enable carries no
data change to reverse.

**Not verified**
- Application and ETL impact — CDC starts a change feed, but whether the downstream warehouse/ETL
  consumer is configured to read this capture instance, and drains it before the retention window
  cleans it up, is not confirmed here (@etl-owner).
- Edition of other environments — the disposable isolated DB confirms CDC enables on that copy's
  edition only; Test, UAT, or Prod may run an edition that does not support CDC, which fails at
  runtime after deploy. Confirm the edition of each target before promotion.
- Instance-wide state — `sp_cdc_enable_db` sets database-wide state; whether each target database is
  already CDC-enabled, or must be enabled first, is not shown by this copy.
- Production scale and timing — the capture jobs add ongoing write overhead the small copy does not
  exercise; the load on a production-scale table is not shown here.
- Reversibility — only the forward enable is proven; disabling CDC drops the change tables and the
  change history they hold, and that loss is not exercised here.
