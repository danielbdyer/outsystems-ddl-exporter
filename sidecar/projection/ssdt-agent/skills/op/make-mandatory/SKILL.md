---
name: make-mandatory
description: Use when the developer says "make Email required", "tick the Mandatory checkbox", "this attribute must be filled", "change it from optional to required" — an existing column NULL→NOT NULL. THE canonical table-has-rows tightening flip.
---

# Make mandatory (NULL → NOT NULL) — the tightening-class change

> **Default (provisional — the data decides).** On an EMPTY table this ships as a single schema
> change applied in place, and any team member can review it. On a POPULATED table — NULLs
> present or already zero, it does not matter — it does not ship in place and it does not land by
> a pre-deployment backfill in one release either; the tightening cannot ride the same release as
> the model, so it ships as **two releases**, and a dev lead must review it because existing data
> is affected. Prove before you classify.

> **SHIP terminal: TWO-RELEASE.** This pipeline (Azure DevOps → Octopus) cannot relax
> `BlockOnPossibleDataLoss`, so a populated `NULL → NOT NULL` ships as R1 (a pre-deploy backfill +
> `ALTER … NOT NULL` with the model still declaring `NULL`) then R2 (the model catches up as a
> no-op). Proven live 2026-08-21; `FINDINGS_AND_CHANGES.md` F7. The old "relax the gate for one
> publish" remedy is not available on this estate — do not offer it.

> **Proven precedent:** `../../../sample-prs/make-mandatory.md` — the worked instance of the
> `../../author-pr/SKILL.md` template for this op. Its *What proving showed* carries the real
> `Msg 50000` block, the `Msg 515` seed failure after the tightening, and the two-release land.

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
  prod-strict gate in one release (see `../../_index/tightening-class/SKILL.md`). After proving
  `COUNT(*) WHERE Col IS NULL = 0` (necessary, not sufficient), it ships as **two releases**
  (`FINDINGS_AND_CHANGES.md` F7; proven live in `../../../sample-prs/make-mandatory.md`):
    - **R1** — a pre-deploy backfills the NULLs and runs `ALTER … NOT NULL`, with the model still
      declaring `NULL`, so DacFx generates no tightening step and the guard never fires. Idempotent
      and safe over a partial state (F6). Publish once — re-publishing R1 reverts the column to
      `NULL`, because the model still declares `NULL` against a database already `NOT NULL`. **The
      remediation must be durable at source:** a post-deployment seed that still writes NULLs into
      the tightened column fails after the ALTER lands (`Msg 515` — the publish is not atomic across
      the schema transaction and the post-deployment script), so the corrected seed rows are part of
      the change set.
    - **R2** — the model declares `NOT NULL` with no pre-deploy; the database is already `NOT NULL`,
      so DacFx generates nothing. R2 goes up an environment only after R1 has landed there.
  A dev lead must review this because existing data is affected; add scrutiny if the table holds
  more than a million rows, or this is the first time on this estate. Relaxing the gate for one
  publish is not available on this estate — do not offer it.

## Prove it (COL-03 / COL-03C — discover, don't assert)
1. Edit `NULL` → `NOT NULL`, build, Strict publish → prove the deployment is blocked, and **read
   the delta** to SEE the `IF EXISTS(...) RAISERROR(...,16,127)` guard ABOVE the `ALTER COLUMN`
   (table-has-rows).
2. Author the pre-deploy backfill, re-run the NULL probe → prove `0` NULLs remain.
3. Re-run Strict → prove it is **STILL blocked** and the column **stays nullable**. This step is
   the key finding.
4. Deliver the corrected verdict: (a) a named gate relaxation after proven-zero-NULL, or (b) the
   multi-phase path — and prove the chosen path lands the `NOT NULL`, including that no
   post-deployment script re-writes NULLs into the column afterward (a seed still declaring them
   fails with `Msg 515` once the column is tightened — fix the seed in the same change set).

The `COL-03C` twin (zero NULLs from the start) is still blocked; the `COL-03B` twin (EMPTY)
publishes clean and ships as a single in-place schema change. For the publish loop, see
`../../prove-on-dacpac/SKILL.md`.

## The verdict (to the developer)
You asked to make Email required. On a disposable copy of Dev, SSDT refused it: the guard it
generates is `IF EXISTS (SELECT TOP 1 1 FROM Customer) RAISERROR(...)` *before* the ALTER, so it
checks whether the table has any rows, not whether Email has blanks. That's proven here — every
NULL was backfilled (0 remain) and Strict still blocked the change and left the column nullable.
On an empty table it would just apply. With data in the table, it ships as two releases: R1 fills
the blanks and tightens the column in a pre-deploy while the model still says optional, then R2
lets the model catch up. The one call for you is the fill value for the blank rows.

## The reasoning (in conversation)
Run the change on a disposable copy rather than reasoning from the `.sql`: the guard keys on
table-has-rows, not on the column's contents (see `../../_index/tightening-class/SKILL.md`). A
clean NULL probe is necessary but never sufficient — it can read green while the change is still
blocked. The mistake to avoid is trusting the backfill alone and shipping the disproven recipe
instead of making the conscious, documented gate call.

## On the record

Assemble the pull request from the `../../author-pr/SKILL.md` template; the worked instance for
this op is `../../../sample-prs/make-mandatory.md`. **SHIP terminal: TWO-RELEASE.**

**Review & release**
- A dev lead must review this: existing data is affected — an existing column is tightened to
  `NOT NULL` while the table holds rows. (On an empty table the change ships in place and any
  team member can review it.)
- Ships as **two releases**: R1 fills the NULLs and runs `ALTER … NOT NULL` in a pre-deploy with
  the model still declaring `NULL` (published once); R2 lets the model catch up as a no-op. The
  seed that feeds the column stops writing NULL in the same change set. The data-loss guard is not
  relaxed, because this pipeline cannot relax it.
- Added scrutiny, if any: the table holds more than a million rows; or this tightening has not been
  performed on this estate before.

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
- Production scale and timing. On a large table the `ALTER COLUMN` may block writes
  or run long; the small copy cannot show that. Schedule a window.
- Reversibility. The forward change and its lossless re-widening are the limit of what the copy
  proves; restoring backfilled values is not exercised here.
