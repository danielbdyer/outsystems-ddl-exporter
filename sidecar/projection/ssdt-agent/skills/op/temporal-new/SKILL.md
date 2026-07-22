---
name: temporal-new
description: Use when the developer says "I want full history on this new entity", "keep every version of every row (new table)", "system-versioned table from the start", "show me what this record looked like last Tuesday — for a brand-new entity" — temporal versioning on a NEW entity. SSDT destination = a declarative system-versioned CREATE (SYSTEM_VERSIONING = ON + history table + period columns).
---

# Add temporal versioning — new entity

> **Default (provisional — the data decides).** A dev lead or an experienced developer should
> review this: turning on system versioning is a design commitment — a paired history table that
> grows with every update — even though the table is new and no existing data is touched. Ships as a
> single schema change, applied in place: temporal versioning IS expressible declaratively for a new
> table, so SSDT publishes the system-versioned CREATE — the table, its paired history table, and the
> two period columns — clean. Prove the clean publish on a disposable copy before classifying.
> (Converting an EXISTING populated table is a different op — route to `../temporal-convert/SKILL.md`.)

## OutSystems phrasing
"I want full history on this new entity", "keep every version of every row (new table)", "point-in-time history from birth".

## SSDT meaning
`SYSTEM_VERSIONING = ON` with a paired history table and two `GENERATED ALWAYS AS ROW START/END` `datetime2` period columns. SQL Server maintains the history table on every write. Temporal **is expressible declaratively for a new table** — SSDT can publish the system-versioned CREATE. Never write ALTER.

## The named trap
**Building the history the developer didn't ask for.** "Keep history" can mean two different things: *point-in-time row history* — what a row looked like at a past moment (temporal versioning, this op) — or a *row-level change feed* of old→new values for a downstream consumer, which is a different mechanism handled outside this agent. Settle which one at **intake**, before building; standing up system versioning when a change feed was wanted (or the reverse) is a design error, cheapest to catch up front.

## How it flips (the specifics only)
- **new table** (this op) → ships as a single schema change, applied in place: the system-versioned CREATE, its history table, and the period columns publish clean, nothing to transition. A dev lead or an experienced developer should review it — system versioning is a design commitment — but no existing data is touched.
- existing **empty** table → the same single in-place schema change; with no rows there is nothing to backfill.
- existing **populated** table → NOT this op; route to `../temporal-convert/SKILL.md`, where the period columns must be backfilled first and the change stages across releases.
- **+ the entity is expected to reach large row counts (> 1M), or this is the first system-versioned table on the estate** → added scrutiny: the paired history table grows with every update, and a first-time temporal build has no prior proof on this estate.

## Prove it
Preview the Strict delta and confirm it publishes the system-versioned CREATE clean — the history table and period columns appear, and the publish is not blocked. See `prove-on-dacpac`. On the sample, temporal-new (AUD-01) is a scratch-authored brand-new system-versioned table (greenfield — no authored seed needed).

## The verdict (to the developer)
You asked for full history on a new entity, and there are two kinds of history to be clear about
first. Temporal versioning gives you point-in-time row history — what a row looked like at any past
moment — at no licensing cost, and because the entity is brand new it publishes clean in a single
release: SSDT creates the table, its history table, and the period columns on a disposable copy of
Dev with nothing blocked. The other kind is a row-level change feed — a stream of old-to-new values
for a downstream system — which is a different mechanism handled outside this agent. So the one thing
to settle before this ships: do you want point-in-time history (temporal, this op), or a change feed?

## The reasoning (in conversation)
When one plain word covers two different mechanisms, settle which one at intake before building
anything. "Keep history" and "track changes" are exactly those words — they can mean point-in-time
row history (temporal, this op) or a row-level change feed for a downstream consumer (handled
elsewhere). The mistake to avoid is standing up system versioning when a change feed was all that was
wanted, or the reverse.

## On the record
Fragments for the pull request (`../../author-pr/SKILL.md`), record register.

**Review & release**
- A dev lead or an experienced developer should review this: turning on system versioning is a
  design commitment — a paired history table that grows with every update. No existing data is
  touched: the entity is new.
- Ships as a single schema change, applied in place: temporal versioning is expressible
  declaratively, so SSDT publishes the system-versioned CREATE — the table, its paired history
  table, and the two `GENERATED ALWAYS AS ROW START/END` period columns — verbatim. No existing data
  is read or written.
- Added scrutiny, when it applies: if the entity is expected to reach large row counts (> 1M), the
  paired history table grows with every update; and a first system-versioned table on the estate has
  no prior proof.

**Verification** — run in each environment after deployment
```sql
-- expect 1 row, temporal_type_desc = SYSTEM_VERSIONED_TEMPORAL_TABLE, history_table named
SELECT t.name, t.temporal_type_desc, h.name AS history_table
FROM sys.tables t
LEFT JOIN sys.tables h ON h.object_id = t.history_table_id
WHERE t.object_id = OBJECT_ID('dbo.<table>');

-- expect 2 rows: the period columns, GENERATED ALWAYS AS ROW START and ROW END
SELECT c.name, c.generated_always_type_desc
FROM sys.columns c
WHERE c.object_id = OBJECT_ID('dbo.<table>') AND c.generated_always_type <> 0;
```

**Rollback**
Remove the system-versioned CREATE from the project and republish. A system-versioned table cannot
be dropped directly: the generated delta sets `SYSTEM_VERSIONING = OFF` first (which unlinks the
history table), then drops the main table and its history table. Lossless only while both tables are
unwritten — they are created empty; once the application writes rows, dropping the pair discards the
current rows and their accumulated history.

**Not verified**
- Application impact — a new entity nothing yet reads or writes does not change existing behaviour,
  but the application code that will query the history (`FOR SYSTEM_TIME`) is new and is not
  exercised by the disposable copy (@app-owner).
- Design intent — the disposable copy proves the system-versioned table publishes clean; it cannot
  confirm that point-in-time history, and not a row-level change feed, is what the use case needs. That
  is a design confirmation owed at intake, not something the copy can settle.
- History growth, retention, and production timing — the paired history table grows with every
  update and has no cleanup unless a retention policy is set; whether a retention policy is
  configured, and the versioning write overhead at production volumes, is not shown by the small
  copy.
- Reversibility — only the forward create is proven; once the application writes rows, dropping the
  table and its history is lossy (see Rollback).
