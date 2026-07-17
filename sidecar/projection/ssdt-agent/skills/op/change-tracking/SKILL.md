---
name: change-tracking
description: Use when the developer says "I just need to know which rows changed since last sync", "change tracking for the mobile sync", "a lightweight what's-new since timestamp X", "tell me which records moved, I don't need the old values" — the light sibling of CDC. SSDT destination = an operational ALTER script, not declarative — it ships as a scripted change outside the dacpac model, with none of CDC's standing capture-instance obligation.
---

# Enable change tracking (CDC-vs-change-tracking conflation trap)

> **Default (provisional — the data decides).** A dev lead or an experienced developer should review
> this: no existing data is touched, but the sync consumer that reads the change data is new
> application code shipping alongside the enable. Ships as a
> scripted change — enabling change tracking cannot be expressed as a table definition — far lighter
> than CDC: all editions, no capture instances, no per-feed management, and none of CDC's standing
> capture-instance obligation. Prove it on a disposable copy before classifying.

## OutSystems phrasing
"I just need to know *which* rows changed since last sync", "change tracking for the mobile sync", "a lightweight 'what's new' since timestamp X".

## SSDT meaning
`ALTER DATABASE … SET CHANGE_TRACKING = ON` + `ALTER TABLE … ENABLE CHANGE_TRACKING`. Lighter than
CDC — it records **which rows / which columns changed** and a version number, but **not the changed
values** (no historical data). All editions, much lower overhead, but still **not declarative** — it
is an operational `ALTER`, outside the dacpac model. It ships as a scripted change, not a schema edit.

## The named trap
**Confusing it with CDC** — developers say "track changes" for both. Change tracking gives "row 42
changed" (sync-oriented); CDC gives "row 42 went from X to Y" (a full change feed). If the developer
needs the old values, change tracking is the wrong tool; reaching for CDC when change tracking would
do takes on the entire CDC obligation for nothing. The intent-naming discipline is owned by
`../../_index/cdc/SKILL.md` (change-tracking is its lighter sibling); do not re-derive the CDC weight
here.

## How it flips (the specifics only)
- enable change tracking → ships as a scripted change, one release: enabling change tracking cannot be
  expressed as a table definition. A dev lead or an experienced developer should review it — no
  existing data is touched, but the sync consumer that reads the feed is new application code.
- retention/cleanup configuration → operational, lives in DB settings; flag it as job-owned.
- it carries **none** of CDC's standing capture-instance obligation — no capture instance is frozen to
  the table's columns, so future changes to the table take on no added scrutiny from it. That is the
  whole point of preferring it when the old values aren't needed.

## Prove it
Confirm the enable is an operational `ALTER` the dacpac does not own (declarative attempt produces
nothing), run it on the isolated DB, and prove `CHANGETABLE(CHANGES …)` reports changed row keys but
**not** old values — so the developer sees the boundary between change tracking and CDC concretely.
Isolation still applies (see `../../_index/cdc/SKILL.md`, `talk-to-local-sql`). On the sample,
`dbo.CdcCandidate` is the target (AUD-06).

## The verdict (to the developer)
You want to know which rows changed since the last sync — change tracking is the light option for
that. It records which rows and columns changed and a version number, not what the values were
before, so on a disposable copy of Dev you can watch it report the changed row keys with no old
values attached. It works on all editions with far less overhead than CDC, and it carries none of
CDC's standing obligation: enabling it here adds nothing to future changes on this table. It ships as
an operational script, not a schema edit. The one thing to settle: do you need the old values, or only
which keys moved? If only which keys moved, this is the right, lighter tool; if you need the
before-values, that is CDC, and it is the heavier road.

## The reasoning (in conversation)
Match the tool to what you actually need: "do you need the old values, or only which keys moved?" is
the question that decides it, and the lighter answer is usually enough. The mistake to avoid is
reaching for CDC when change tracking would do — CDC lays a standing obligation on every future change
to the table (the capture instance has to be managed, or a column silently drops out of the feed), and
taking that on for a mobile sync that only needs "which keys moved" is a cost with no return. The full
reasoning — name what you need, then pick the lightest tool that gives it — is in
`../../_index/cdc/SKILL.md`.

## On the record
Fragments for the pull request (`../../author-pr/SKILL.md`), record register.

**Review & release**
- A dev lead or an experienced developer should review this: no existing data is touched, but the
  sync consumer that reads the change data is new application code shipping alongside.
- Ships as a scripted change — enabling change tracking cannot be expressed as a table definition — in
  a single release.
- Added scrutiny: none. Change tracking carries no standing capture-instance obligation, so future
  changes to this table take on no added scrutiny from it; retention and cleanup are a database setting
  a job owns.

**Verification** — run in each environment after deployment
```sql
-- expect 1 row: change tracking is enabled on the table (is_track_columns_updated_on as configured)
SELECT OBJECT_NAME(object_id) AS table_name, is_track_columns_updated_on
FROM sys.change_tracking_tables
WHERE object_id = OBJECT_ID('dbo.<table>');
```

**Rollback**
Disable change tracking in reverse of the enable: `ALTER TABLE <table> DISABLE CHANGE_TRACKING;`, then
`ALTER DATABASE … SET CHANGE_TRACKING = OFF` once no table on the database is tracked. Lossless at the
schema level — no existing data is modified — but the accumulated change history is discarded, and a
sync consumer mid-stream loses its baseline version and must re-sync from a fresh one.

**Not verified**
- Application impact — the sync consumer that reads `CHANGETABLE(CHANGES …)` is new code and is not
  exercised by the disposable copy; that it reads the feed and handles version boundaries correctly is
  not confirmed here (@app-owner).
- Retention and cleanup — the auto-cleanup interval and the job that enforces it are a standing
  database setting; that they are configured and owned in each environment is not verified on the copy.
- Other environments — change tracking must be on at the database level before the table-level enable;
  whether Test / UAT / Prod already have the database-level setting on is not known here.
- Production scale and timing — the write-path overhead of change tracking at production volumes is not
  shown by the small copy.
