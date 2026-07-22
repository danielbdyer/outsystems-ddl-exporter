---
name: temporal-convert
description: Use when the developer says "add full history to our existing populated entity", "turn on system versioning for Customer which already has data", "make this live table temporal", "start keeping history on an entity that's already in production" — converting an EXISTING populated table to temporal. SSDT destination = a staged add-period-columns / backfill / enable-versioning program across several releases.
---

# Convert an existing populated table to temporal (backfilled-ROW-START trap)

> **Default (provisional — the data decides).** A dev lead must review this: existing data is
> modified — the period columns are backfilled into every existing row, and left without sensible
> historical defaults every row falsely claims to have begun at conversion time. Ships across several
> releases so the running application keeps working while the period columns are added, backfilled,
> and system versioning is turned on. Prove the period-column backfill produces sane `ROW START`
> values on a disposable copy before classifying. (A new entity is `../temporal-new/SKILL.md`, a
> single release.)

## OutSystems phrasing
"add full history to our existing populated entity", "turn on system versioning for Customer which already has data", "make this live table temporal".

## SSDT meaning
Add the (hidden) `GENERATED ALWAYS AS ROW START/END` period columns with backfilled start times, create the paired history table, then turn `SYSTEM_VERSIONING = ON` against live data. Staged because you cannot swap the shape under a running app atomically. Never write ALTER.

> **The application-side cutover is part of this change.** The Integration Studio / Service Studio
> republish order, the two sequencing rules (the app reads the new shape only after the schema
> release; the old shape drops only after the app stops writing it), and what the pull request
> names under Not verified are owned by `../../_index/multi-phase/SKILL.md` — plan no phase
> without it.

## The named trap
Adding the period columns to a populated table needs **sensible historical defaults for `ROW START`**, or every existing row claims to have begun at conversion time. Also confirm the developer wants point-in-time history, not a row-level change feed (a different mechanism handled outside this agent; see `../temporal-new/SKILL.md`). The coexistence WHY is `../../_index/multi-phase/SKILL.md`; do not re-derive it.

## How it flips (the specifics only)
- existing **populated** table (this op) → ships across several releases: add the period columns with
  backfilled start times, create the history table, enable versioning; a dev lead must review it
  because existing data is modified.
- existing **empty** table → collapses to a single schema change applied in place, one release — no
  data to backfill.
- **+ >1M rows / first-time** → added scrutiny: at production row counts the backfill and the
  versioning switch may block writes or run long, and this may be the first temporal conversion on
  the estate.

## Prove it
Prove the period-column backfill produces sane `ROW START` values and that enabling versioning is not blocked on the populated table; hash the data before/after to prove the rows themselves are untouched. See `prove-on-dacpac` / `talk-to-local-sql`. On the sample, convert the existing populated `Customer` in a disposable copy (AUD-02).

## The verdict (to the developer)
You asked to start keeping history on Customer, which already has data. This is staged across a few releases, because the period columns have to be added and backfilled first — otherwise every existing row would claim it began at conversion time instead of when it actually did. On a disposable copy of Dev I confirmed the backfill produces sane start times and leaves the existing rows' data untouched. One call is yours: what start time should the existing rows carry — the conversion date, or a real historical date if the business tracks one?

## The reasoning (in conversation)
Converting an existing populated table is staged because the old shape and the new one have to coexist while the period columns are added and backfilled — the shape can't be swapped under a running app in one atomic step. The failure this avoids is subtle: every historical row falsely dated to conversion time instead of when it actually began, which quietly corrupts the very history versioning was turned on to keep. Full reasoning: `../../_index/multi-phase/SKILL.md`.

## On the record
The fragment this op contributes to the pull request (`../../author-pr/SKILL.md`), record register.

**Review & release**
- A dev lead must review this: existing data is modified — the period columns are backfilled into
  every existing row on a live table.
- Ships across several releases so the running application keeps working while the period columns are
  added, backfilled, and system versioning is turned on.
- Added scrutiny, when it applies: a large table or a first-time temporal conversion on the estate —
  at production row counts the backfill and the versioning switch may block writes or run long.

**Verification** — run in each environment after deployment
```sql
-- expect one row, temporal_type_desc = 'SYSTEM_VERSIONED_TEMPORAL_TABLE', with a paired history table
SELECT t.name, t.temporal_type_desc, h.name AS history_table
FROM sys.tables t
LEFT JOIN sys.tables h ON h.object_id = t.history_table_id
WHERE t.name = 'Customer';

-- expect 0 rows: every existing row's ROW START (the period-start column) predates the conversion
SELECT Id, ValidFrom FROM dbo.Customer WHERE ValidFrom >= '<conversion timestamp>';
```

**Rollback**
Turn `SYSTEM_VERSIONING = OFF`, then drop the period columns and the history table — the mirror of the conversion. The existing rows' pre-existing column values are unchanged by the conversion (the before/after content hash matches), so backing out the schema is lossless for them; any history rows accumulated after go-live are lost when the history table is dropped, and are not recoverable.

**Not verified**
- Application impact — how the running application behaves against a system-versioned table: explicit
  column-list writes, `SELECT *`, and any attempt to write the hidden period columns are not confirmed
  here (@app-owner).
- Other environments — whether Test/UAT/Prod row counts change the backfill outcome or the timing is
  not shown by this copy.
- Production scale and timing — enabling versioning and backfilling against a large table may block
  writes or run long; the small copy does not exercise it.
- Reversibility — only the forward conversion is proven on the disposable copy; disabling versioning
  and dropping the period columns and history table is not exercised here.
