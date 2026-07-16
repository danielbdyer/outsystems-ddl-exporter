---
name: make-mandatory
description: Use when the developer says "make Email required", "tick the Mandatory checkbox", "this attribute must be filled", "change it from optional to required" — an existing column NULL→NOT NULL. THE canonical table-has-rows tightening flip.
---

# Make mandatory (NULL → NOT NULL) — the tightening-class change

> **Default (provisional — the data decides).** On an EMPTY table this ships as a single schema
> change applied in place, and any team member can review it. On a POPULATED table — NULLs
> present or already zero, it does not matter — it does not ship in place and it does not land by
> a pre-deployment backfill either; it needs a conscious gate decision, and a dev lead must
> review it because existing data is affected. Prove before you classify.

## OutSystems phrasing
"make Email required", "tick the Mandatory checkbox on this attribute".

## SSDT meaning
Change an existing column from `NULL` to `NOT NULL`. SSDT emits `ALTER TABLE ... ALTER COLUMN
[Col] <type> NOT NULL` — but on a populated table it guards that ALTER with a **data-blind
`BlockOnPossibleDataLoss` check that fires on table-has-rows, NOT column-has-NULLs**. Edit the
CREATE; never write `ALTER`.

## The named trap
This is **the tightening class** — see `../../_index/tightening-class/SKILL.md` for the
`IF EXISTS(SELECT TOP 1 1 FROM <t>) RAISERROR(...,16,127)` guard, the empty-vs-populated ladder,
and the proven **why** (SSDT computes the whole deploy script once, up front, so a same-release
backfill cannot satisfy it). Do not re-derive the guard here. The failure this op exists to
catch: classifying from the `.sql` text or a clean NULL probe — both *look* green, yet the
deployment is still blocked. The old recipe — backfill, then a clean `NOT NULL` under Strict —
does not work and must not be used: it was disproven, a pre-deploy backfill cleared every NULL
and Strict still blocked the change.

## How it flips (the specifics only)
- **table EMPTY** → ships as a single schema change applied in place, and any team member can
  review it (the `IF EXISTS` is false; the ALTER lands — verify genuinely empty first)
- **table POPULATED — NULLs present OR zero NULLs, does not matter** → cannot pass the
  prod-strict gate by backfill alone (see `../../_index/tightening-class/SKILL.md`). After proving
  `COUNT(*) WHERE Col IS NULL = 0` (necessary, not sufficient), choose ONE:
    - **(a) a named gate relaxation** — ships as a scripted change: disable
      `BlockOnPossibleDataLoss` for this one targeted change, logged, with the proof packet
      carrying **both** the zero-NULL probe and the relaxation decision.
    - **(b) restructure so the change ships across releases** and the engine never has to relax
      its guard (see `../../_index/multi-phase/SKILL.md`).
  Either way, a dev lead must review this because existing data is affected; add scrutiny if the
  table feeds CDC, holds more than a million rows, or this is the first time on this estate.
- **+ CDC, no-gap** → push toward the multi-phase path, with added scrutiny for the capture
  stream (see `../../_index/cdc/SKILL.md`)

## Prove it (COL-03 / COL-03C — discover, don't assert)
1. Edit `NULL` → `NOT NULL`, build, Strict publish → prove the deployment is blocked, and **read
   the delta** to SEE the `IF EXISTS(...) RAISERROR(...,16,127)` guard ABOVE the `ALTER COLUMN`
   (table-has-rows).
2. Author the pre-deploy backfill, re-run the NULL probe → prove `0` NULLs remain.
3. Re-run Strict → prove it is **STILL blocked** and the column **stays nullable**. This step is
   the key finding.
4. Deliver the corrected verdict: (a) a named gate relaxation after proven-zero-NULL, or (b) the
   multi-phase path — and prove the chosen path lands the `NOT NULL`.

The `COL-03C` twin (zero NULLs from the start) is still blocked; the `COL-03B` twin (EMPTY)
publishes clean and ships as a single in-place schema change. For the publish loop, see
`../../prove-on-dacpac/SKILL.md`.

## The verdict (to the developer)
You asked to make Email required. On a disposable copy of Dev, SSDT refused it: the guard it
generates is `IF EXISTS (SELECT TOP 1 1 FROM Customer) RAISERROR(...)` *before* the ALTER, so it
checks whether the table has any rows, not whether Email has blanks. That's proven here — every
NULL was backfilled (0 remain) and Strict still blocked the change and left the column nullable.
On an empty table it would just apply. With data in the table, this needs a deliberate call:
relax `BlockOnPossibleDataLoss` for this one column after proving zero blanks (logged,
script-only), or stage it across two releases. If the table feeds a change-data-capture stream,
the multi-phase path is the safer one. Which would you prefer?

## The reasoning (in conversation)
Run the change on a disposable copy rather than reasoning from the `.sql`: the guard keys on
table-has-rows, not on the column's contents (see `../../_index/tightening-class/SKILL.md`). A
clean NULL probe is necessary but never sufficient — it can read green while the change is still
blocked. The mistake to avoid is trusting the backfill alone and shipping the disproven recipe
instead of making the conscious, documented gate call.

## On the record

The fragment this op contributes to the pull request (`../../author-pr/SKILL.md`).

**Review & release**
- A dev lead must review this: existing data is affected — an existing column is tightened to
  `NOT NULL` while the table holds rows. (On an empty table the change ships in place and any
  team member can review it.)
- Ships as a scripted change: the data-loss guard `BlockOnPossibleDataLoss` is relaxed for this
  one column after the zero-NULL count is proven, or the column is filled and tightened across
  two releases.
- Added scrutiny, if any: the table feeds a change-data-capture stream; the table holds more than
  a million rows; or this tightening has not been performed on this estate before.

**Verification** — run in each environment after deployment
```sql
-- expect 0: no row holds a NULL in the tightened column
SELECT COUNT(*) AS null_rows FROM dbo.Customer WHERE Email IS NULL;

-- expect is_nullable = 0: the column landed NOT NULL
SELECT is_nullable FROM sys.columns
WHERE object_id = OBJECT_ID('dbo.Customer') AND name = 'Email';
```

**Rollback**
Re-widening the column is lossless: `ALTER TABLE dbo.Customer ALTER COLUMN Email <type> NULL`
restores the nullable column with no data loss. Any values written to backfill NULLs before the
tightening are not auto-reversed; the pre-backfill values belong in the remediation record for a
manual restore.

**Not verified**
- Application impact. Any code path that inserts the row without the column, or writes NULL to it,
  will now fail once the column is `NOT NULL`. Application-side validation is not confirmed here —
  the app owner owns closing it.
- Other environments. Test, UAT, and Prod may hold NULLs this disposable copy cannot see. Run the
  NULL probe in each before promotion.
- Production scale and timing. On a large or CDC-tracked table the `ALTER COLUMN` may block writes
  or run long; the small copy cannot show that. Schedule a window.
- Reversibility. The forward change and its lossless re-widening are the limit of what the copy
  proves; restoring backfilled values is not exercised here.
