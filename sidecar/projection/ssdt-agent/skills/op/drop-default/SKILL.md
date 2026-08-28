---
name: drop-default
description: Use when the developer says "remove the default value", "stop defaulting this attribute", "new rows shouldn't get a value automatically anymore" — removing a DEFAULT constraint without replacing it. Ships in place as a single schema change; existing rows keep their values; the risk is every insert that relied on the default.
---

# Drop a default

> **Default (provisional — prove before you classify).** Ships as a single schema change, applied in
> place — one `ALTER TABLE ... DROP CONSTRAINT`, no data read or written, and the publish never
> blocks. Any team member can review it when the column is nullable. When the column is `NOT NULL`,
> a dev lead or an experienced developer reviews it: every insert path that omitted the column and
> relied on the default now fails at runtime, so the running application may need to change.

> **SHIP terminal: ONE RELEASE, in place.** Proven live on this branch (database `PG_inv_x1`,
> sqlpackage 170.5.76): removing `DF_Product_LegacyCode` from the CREATE generated the single
> statement `ALTER TABLE [dbo].[Product] DROP CONSTRAINT [DF_Product_LegacyCode];` and the Strict
> publish returned `Successfully published database.` with every existing `LegacyCode` value
> unchanged.
>
> **Proven precedent:** `../../../sample-prs/drop-default.md` — the worked instance of the
> ten-section template (`../../author-pr/SKILL.md`) for this op.

## OutSystems phrasing
"remove the default value", "clear the Default Value on this attribute", "stop defaulting it".

## SSDT meaning
Remove the named `CONSTRAINT DF_... DEFAULT (...)` from the column in the CREATE. SSDT emits
`ALTER TABLE ... DROP CONSTRAINT [DF_...]`. A default fills only new rows at insert time, so
removing one touches no existing row — the change is entirely about what happens to the next
insert. (Changing the value instead of removing it is `../modify-default/SKILL.md`.)

## The named trap
The publish is green and the damage is deferred. On a `NOT NULL` column, an application insert
that omitted the column and relied on the default fails from the first post-deploy insert on —
at runtime, with `Msg 515`, not at deploy. The deployment cannot surface this; only the
application's insert paths can. The other face: the post-deployment seed. A seed that omitted
the column because the default filled it now fails on the next new row it inserts — the seed is
part of the change set, exactly as `../../_index/idempotent-seed/SKILL.md` requires.

## How it flips (the specifics only)
- **column is nullable** → the next insert that omits the column writes NULL instead of the
  default. Nothing fails; the meaning of "omitted" changes. Any team member can review it.
- **column is NOT NULL** → the next insert that omits the column fails with `Msg 515`. A dev
  lead or an experienced developer reviews it, because the application must supply the value
  everywhere before the default goes.
- **the default was doing backfill duty on a new NOT NULL column** (`../add-mandatory/SKILL.md`)
  → dropping it after the column landed is safe for existing rows; the insert-path risk above
  still applies.

## Prove it
Strict publishes clean; the delta is a single `DROP CONSTRAINT`; nothing blocks (removing a
default reads no data). Prove the two things the publish cannot show: probe
`sys.default_constraints` before and after, and confirm with the developer which insert paths —
application and seed — omit the column today. See `../../prove-on-dacpac/SKILL.md`.

## The verdict (to the developer)
"You asked to stop defaulting this attribute. The removal itself always publishes clean and
touches no existing row — the default only ever filled new rows. The one thing that matters is
what inserts a row without this attribute today: on a required attribute those inserts start
failing the moment the default is gone, and that includes our own seed script. Confirm every
insert path supplies the value, then this ships as a single in-place change."

## The reasoning (in conversation)
A default is insert-time behavior, not stored data, so removing it cannot lose anything — and
cannot be blocked. The risk moved from the database to the application's insert paths, which is
why the review is about code paths, not about the publish. The mistake to avoid is reading the
green publish as proof the change is consequence-free.

## On the record

The pull request is an instance of the ten-section template in `../../author-pr/SKILL.md`; the
worked instance is `../../../sample-prs/drop-default.md`. SHIP terminal: **ONE RELEASE, in
place.** The fragment this operation contributes:

**Review & release**
- On a nullable column, any team member can approve this: the change is insert-time behavior
  only and the running application is unaffected. On a `NOT NULL` column, a dev lead or an
  experienced developer must review it: every insert that omitted the column now fails.
- Ships as a single schema change, applied in place — one `ALTER TABLE ... DROP CONSTRAINT`.
  No data is read or written, and the publish never blocks.
- Added scrutiny: none. The drop reads and writes no data, so row count is not a factor.

**Verification** — run in each environment after deployment
```sql
-- expect 0 rows: the default constraint no longer exists
SELECT name FROM sys.default_constraints WHERE name = 'DF_<Table>_<Column>';
```

**Rollback**
Re-creating the constraint reverses the drop:
`ALTER TABLE <table> ADD CONSTRAINT DF_<Table>_<Column> DEFAULT (<value>) FOR <Column>;` —
in place, no data touched. Rows inserted while the default was absent keep whatever they were
given (NULL, or an application-supplied value); no backfill is auto-applied.

**Not verified**
- Application insert paths — whether every path that omitted the column now supplies a value is
  not confirmed here; the application owner confirms it before promotion.
- The post-deployment seed — confirmed only for the tables the disposable copy carries; any
  environment-specific script that relied on the default is not checked here.
- Other environments — the drop was proven on a disposable copy of Dev only. Run the
  verification query in each environment before promotion.
