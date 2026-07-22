---
name: narrow
description: Use when the developer says "shorten Code to 10 chars", "tighten this field", "reduce the precision", "make it smaller" — shrinking length/precision. The Ambitious Narrowing trap; a member of the table-has-rows tightening class.
---

# Narrow (Ambitious Narrowing) — tightening class

> **Default (provisional — the data decides).** On an empty table, narrowing ships as a single
> schema change applied in place — no data is read or written, and any team member can review it.
> On a populated table it is not a clean in-place change: the data-blind guard blocks it regardless
> of whether every value fits. Prove first.

## OutSystems phrasing
"shorten Code to 10 characters", "tighten this field", "reduce the precision".

## SSDT meaning
Shrink length/precision (`NVARCHAR(50)`→`NVARCHAR(10)`). SSDT emits `ALTER COLUMN` to the
narrower type. Any existing value longer than the new size would **truncate** (data loss), so
under Strict `BlockOnPossibleDataLoss=True` blocks the deployment. Edit the CREATE; never write
`ALTER`.

## The named trap
**Ambitious Narrowing** (handbook 16 = §19.4) — the build succeeds; the deploy either **blocks**
(Block on) or **silently truncates** (Block off). This is **the tightening class** — SSDT injects
the same data-blind `IF EXISTS(SELECT TOP 1 1 FROM <t>) RAISERROR` guard, so Strict blocks
narrowing on any non-empty table **even when every value already fits** (proven on the
make-mandatory zero-NULL scenario). See `../../_index/tightening-class/SKILL.md`; do not re-derive
the guard here.

## How it flips (empty vs populated dominates; whether the values fit is the second question)
- **empty table** (guard false) → ships as a single schema change, applied in place; no data is
  read or written. Any team member can review it.
- **populated, `MAX(LEN) <= new size`** (every value fits) → **still blocked under Strict** — not
  a clean in-place change. Honest disposition: relax `BlockOnPossibleDataLoss` for this one change
  after proving `MAX(LEN)` fits — ships as a scripted change, logged. A dev lead or an experienced
  developer should review it (the running application must respect the new limit). Same shape as
  make-mandatory with zero NULLs — see `../../_index/tightening-class/SKILL.md`.
- **populated, any value exceeds new size** → real truncation: reconcile the over-length rows
  first (a data change) **and** still face the guard. Ships across releases if the values must be
  preserved (see `../../_index/multi-phase/SKILL.md`), or as a scripted change after a
  truncate-with-intent reconcile. A dev lead must review this: existing data is modified.
- **>1M rows** → added scrutiny: at production row counts the `ALTER COLUMN` rewrite may block
  writes or run long — schedule a window.

## Prove it
Run the `MAX(LEN(Col))` probe AND a `WHERE LEN(Col) > <new>` count to **quantify** how many rows
truncate. Under Strict, the publish must **block** on data loss when over-length rows exist — show
the count. Run Permissive (`BlockOnPossibleDataLoss=False`) and the before/after data hash to show
*exactly* which values would have been truncated. Author the reconcile, re-run Strict. For the
publish loop, see `../../prove-on-dacpac/SKILL.md`; probes, `../../talk-to-local-sql/SKILL.md`.

## The verdict (to the developer)
"You asked to shorten Code to 10 — it looks like a one-liner. I checked your data first: the
longest Code is 14 characters, and 37 rows are longer than 10. On a disposable copy of Dev, SSDT
refused the change to protect those 37 rows, and a permissive run showed exactly which characters
would have been truncated. So the real question is those 37 rows: do you want them deliberately
truncated to 10, or is the extra length real data we have to keep? If they can be truncated, this
ships as a scripted change with the reconcile below; if the values must be kept, it stages across
two releases. (On an empty table it would have been a clean one-liner.)"

## The reasoning (in conversation)
Narrowing shares one guard behaviour and one remedy shape with make-mandatory and delete-attribute
— making a column required, and dropping a column. The first question is never `MAX(LEN)`; it is
*is the table empty?* On an empty table the guard is inert and the narrowing just applies; on a
populated table the same data-blind guard blocks it, whether or not the values fit. Learn that
once and you stop re-discovering the same block one operation at a time. (The shared guard lives in
`../../_index/tightening-class/SKILL.md`.)

## On the record
The fragment this operation contributes to the pull request (`../../author-pr/SKILL.md`), drawn
from the cases above. Take the line the data proves.

**Review & release**
- Empty table: `Any team member can review this: the table is empty, so no data can be lost.` ·
  `Ships as a single schema change, applied in place. No data is read or written.`
- Populated, every value fits: `A dev lead or an experienced developer should review this: after
  narrowing, the running application can no longer store values longer than the new size.` ·
  `Ships as a scripted change — the data-loss guard is relaxed for this one column after MAX(LEN)
  is proven to fit.`
- Populated, values exceed the new size: `A dev lead must review this: existing data is modified —
  over-length values are reconciled before the column narrows.` · `Ships across releases if the
  values must be preserved, or as a scripted change: reconcile the over-length rows, then narrow.`
- Added scrutiny, when it applies: `Added scrutiny: at production row counts the ALTER COLUMN
  rewrite may block writes or run long — schedule a window.`

**Verification** — run in each environment after deployment:
```sql
-- expect 0 rows: no value exceeds the new size
SELECT <key>, LEN(Col) AS len FROM <t> WHERE LEN(Col) > <new>;
```

**Rollback** — widening `Col` back to its original size applies without data loss; any value
shortened by the reconcile is not recoverable from the schema — the before/after hash from the
permissive run holds the originals for a manual restore.

**Not verified**
- Application impact — any code path that writes a value longer than the new size is now rejected
  (or was silently truncated under a permissive publish); application-side length validation is not
  confirmed here.
- Other environments — Test/UAT/Prod may hold longer values than this copy; run the verification
  query before promotion.
- Production scale / timing — the `ALTER COLUMN` rewrite cost at production row counts is not shown
  by the disposable copy.
- Reversibility — only the forward narrowing is exercised; a truncating reconcile cannot be undone.
