---
name: recreate-capture-instance
description: Use when the developer says "I added a column but CDC isn't picking it up", "the ETL feed is missing the new field", "why doesn't the change feed see my new attribute", "the warehouse stopped getting the new column" — a schema change on a CDC-tracked table. The capture instance is frozen to the table's old shape, so the change ships as a script that recreates it: a dual-instance (v1/v2) migration staged across releases when the change feed must miss nothing.
---

# Recreate the CDC capture instance

> **Default (provisional — the data decides).** When the change feed must miss nothing (a no-gap
> requirement), this ships across releases (multiple pull requests) as a dual-instance migration — a
> second capture instance (`Customer_v2`) runs beside `Customer_v1` until consumers cut over —
> because recreating a capture instance cannot be expressed as a table definition. A principal must
> review this: a mistake silently drops change records from the feed, and the lost events cannot be
> recovered. The danger drives the review need, not the release count. Prove before you classify.

## OutSystems phrasing
"I added a column to Customer but CDC isn't picking it up", "the ETL feed is missing the new field",
"why doesn't the change feed see my new attribute".

## SSDT meaning
A CDC capture instance is **frozen at the table shape it was created for** — adding or altering a
column does not retrofit it. The safe procedure is the **dual-instance pattern**: create a *second*
capture instance (`Customer_v2`) for the new shape with
`sp_cdc_enable_table @capture_instance = 'Customer_v2'`, let consumers drain the old instance
(`Customer_v1`), cut them over to v2, then drop v1. Handbook file 14 (=§17.9). Never write ALTER;
this is a script.

## The named trap
**CDC Surprise** — here the standing obligation from `enable-cdc` comes due. A "trivial" add-column
change on a CDC-tracked table becomes a capture-instance migration staged across releases, and
skipping it leaves the new column's changes **silently absent from the feed** — no error, just
missing data downstream. The companion is the **Refactorlog Cleanup** family: a schema change the
refactorlog handles for the dacpac still needs the capture instance rebuilt independently. The
standing-obligation WHY is `../../_index/cdc/SKILL.md`; the coexistence WHY is
`../../_index/multi-phase/SKILL.md`. Do not re-derive either.

## How it flips (the specifics only)
- schema change on a CDC table, **no-gap NOT required** (consumers tolerate a brief gap) → drop and
  recreate the single capture instance. Ships as a scripted change in a single release (one pull
  request) — recreating the capture instance cannot be expressed as a table definition. A dev lead
  must review this: the change feed briefly stops while the instance is rebuilt.
- schema change on a CDC table, **no-gap required** → dual-instance v1/v2. Ships across releases
  (multiple pull requests) so consumers keep reading without a gap while the change is in flight. A
  principal must review this: a mistake silently drops change records from the feed, and the lost
  events cannot be recovered.
- **+ >1M rows** → added scrutiny: at production row counts both capture instances are populated
  during the overlap and the migration may run long — schedule a window.

## Prove it
Prove the gap exists — add a column to a CDC-enabled table on the isolated DB, then show the existing
capture instance does **not** surface the new column (`sys.sp_cdc_get_captured_columns` lacks it).
Then prove the dual-instance fix: `Customer_v2` surfaces the new column while `Customer_v1` is still
drainable. Isolation is mandatory (see `../../_index/cdc/SKILL.md`, `talk-to-local-sql`). On the
sample, add a column to `dbo.CdcCandidate` after capture (AUD-05).

## The verdict (to the developer)
Your new column looks trivial, but CDC is on this entity, so it isn't. The change feed is frozen to
the table's old shape: it silently won't include the new field — no error, just missing data in the
warehouse. I proved that on a disposable copy of Dev, where the existing capture instance does not
surface the new column. The fix runs two capture instances side by side (`Customer_v2` beside
`Customer_v1`) so the ETL never misses a change during the switch, which is why it ships staged
across releases rather than a one-line add. One thing to confirm: can your downstream consumers
tolerate a brief gap in the feed, or must they miss nothing? That decides whether this is a single
rebuild or the staged dual-instance migration.

## The reasoning (in conversation)
With CDC the dangerous outcome isn't a loud refusal — it's a quiet gap. So the proof is inverted from
the rest of this work: you show the existing capture instance does NOT surface the new column, then
show the second instance does. The failure mode this avoids is the warehouse silently missing a
column for a month, long after the "small" add-column change was forgotten. Full reasoning:
`../../_index/cdc/SKILL.md`.

## On the record
The fragment this op contributes to the pull request (`../../author-pr/SKILL.md`), record register.

**Review & release**
- A principal must review this when the change feed must miss nothing: a mistake in the dual-instance
  cutover silently drops change records from the feed, and the lost events cannot be recovered. When
  consumers tolerate a brief gap, a dev lead must review this: the feed stops only while the single
  instance is rebuilt.
- Ships as a scripted change — recreating a capture instance cannot be expressed as a table
  definition. When the feed must miss nothing, it ships across releases (multiple pull requests) as a
  dual-instance migration (`Customer_v2` beside `Customer_v1`) so consumers keep reading without a
  gap; when a brief gap is tolerable, the single instance is dropped and recreated in one release.
- Added scrutiny: this table feeds a change-data-capture stream, so the capture instance is frozen to
  the table's current columns and must be recreated for the new shape. At more than a million rows,
  both capture instances are populated during the overlap and the migration may run long — schedule a
  window.

**Verification** — run in each environment after deployment
```sql
-- expect the new column present: the recreated (v2) capture instance surfaces it
EXEC sys.sp_cdc_get_captured_columns @capture_instance = 'Customer_v2';

-- during the dual-instance overlap, expect the new column ABSENT from the old (v1) instance
EXEC sys.sp_cdc_get_captured_columns @capture_instance = 'Customer_v1';
```

**Rollback**
Before consumers cut over, `Customer_v1` still carries every previously tracked column, so the change
is backed out by leaving consumers on `Customer_v1` and dropping the unused `Customer_v2` capture
instance (`sys.sp_cdc_disable_table`); no source-table data changes, so this is lossless. After
`Customer_v1` is dropped following cutover, the change rows it held during the overlap are gone — that
is not auto-reversible.

**Not verified**
- Consumer cutover — whether every downstream consumer (the ETL / warehouse) has actually been
  repointed from `Customer_v1` to `Customer_v2`, and has drained v1 before it is dropped, is a
  consumer-side change this copy cannot confirm (@etl-owner).
- Other environments — the disposable isolated copy proves the frozen-capture gap and the
  dual-instance fix on sample data only; the Test, UAT, and Prod capture instances are not exercised
  here.
- Production scale and timing — at more than a million rows both capture instances are populated
  during the overlap; how long that overlap runs and its write impact are not shown by the small copy.
  Schedule a window.
- Reversibility — only the forward migration is proven. Once `Customer_v1` is dropped after cutover,
  the change rows captured during the overlap are gone and are not recoverable here.
