---
name: add-mandatory
description: Use when the developer says "add a required attribute", "add a Status field everyone must have one", "add a field that can't be blank" — a new NOT NULL column. The Optimistic NOT NULL trap; a populated table needs a DEFAULT.
---

# Add mandatory attribute (Optimistic NOT NULL)

> **Default (provisional — the data decides).** With an explicit default → Mechanism 1 Pure Declarative, single-phase, Tier 2 (contractual). Populated with no default → veto.

## OutSystems phrasing
"add a required attribute", "add a Status field, everyone must have one".

## SSDT meaning
Add a `NOT NULL` column. On a populated table SQL Server must put *something* in every existing
row — so it needs a **DEFAULT**. With `NOT NULL CONSTRAINT DF_... DEFAULT(...)`, SSDT emits `ALTER
... ADD [Col] <type> NOT NULL CONSTRAINT ... DEFAULT ...` and stamps existing rows. Without a
default, the publish fails on the populated table. Edit the CREATE; never write `ALTER`.

## The named trap
**Optimistic NOT NULL** (handbook 16 = §19.2) — `NOT NULL` with no default on a populated table:
build succeeds (SSDT cannot see your rows), **deploy fails** "Cannot insert NULL". If
`GenerateSmartDefaults=True`, SSDT **silently** backfills (e.g. empty string) — Permissive lets
you observe what would have been stamped. Note this is NOT the tightening class (that vetoes on
row-presence for an existing column); here the veto is a genuine can't-insert-NULL on a *new*
column, cured by supplying a value.

## How it flips (the specifics only)
- table empty → **M1, single-phase, Tier 1** (no rows to fill; default optional)
- populated + explicit DEFAULT → **M1, single-phase, Tier 2** (SQL Server fills existing rows from the default)
- populated, **no DEFAULT** → publish **vetoes** → add a default (back to M1) or **M3 Pre-Deploy backfill**; do **not** let `GenerateSmartDefaults` silently decide
- CDC-enabled / >1M rows → **+1 Tier** (see `../../_index/cdc/SKILL.md`)

## Prove it
With a default, Strict publishes clean and the delta shows the `DEFAULT`. Drop the default and
prove the Strict **veto** ("Cannot insert NULL"); then run Permissive with
`GenerateSmartDefaults=True` and snapshot the data hash to show the developer exactly what value
SSDT *would* have silently stamped. For the publish loop, see `../../prove-on-dacpac/SKILL.md`.

## Verdict to the developer
"A required attribute needs a value for the rows that already exist. With the default you gave
it, SSDT stamps every existing row and it publishes clean — Pure Declarative, Tier 2. Without a
default it would have failed at deploy, or worse, silently filled empty strings — I proved both
on a copy."

## Teach it (the graduation)
The DEFAULT is the only thing that lets the engine fill rows it cannot know how to populate; the
no-default veto is the gate refusing to invent your data, and `GenerateSmartDefaults` is the
dangerous opposite — it *invents* silently. Ask "who supplies the value for existing rows — me,
explicitly, or the engine, silently?" and never let it be "silently."
