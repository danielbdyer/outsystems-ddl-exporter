---
name: narrow
description: Use when the developer says "shorten Code to 10 chars", "tighten this field", "reduce the precision", "make it smaller" — shrinking length/precision. The Ambitious Narrowing trap; a member of the table-has-rows tightening class.
---

# Narrow (Ambitious Narrowing) — tightening class

> **Default (provisional — prove before you classify).** On an empty table, narrowing ships as a single
> schema change applied in place — no data is read or written, and a dev lead approves this.
> On a populated table it is not a clean in-place change: the data-blind guard blocks it regardless
> of whether every value fits, so it ships as **two releases**. Prove first.

> **SHIP terminal: TWO-RELEASE.** This pipeline (Azure DevOps → Octopus) cannot relax
> `BlockOnPossibleDataLoss`, so a populated narrowing ships as R1 (a pre-deploy that reconciles the
> over-length values + `ALTER COLUMN` narrower, with the model still declaring the wider type) then
> R2 (the model catches up as a no-op). Proven live 2026-08-21; `FINDINGS_AND_CHANGES.md` F1–F4.
> Relaxing the gate for one publish is not available on this estate — do not offer it.

> **Proven precedent:** `../../../sample-prs/narrow.md` — the worked instance of the
> `../../author-pr/SKILL.md` template for this op. Its *What proving showed* carries the real
> `Msg 50000` block, the `Msg 2628` seed truncation after the narrowing, and the two-release land.

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
  read or written. A dev lead approves this.
- **populated, `MAX(LEN) <= new size`** (every value fits) → **still blocked under Strict** — not
  a clean in-place change. Ships as **two releases**: R1 runs `ALTER COLUMN` narrower in a
  pre-deploy with the model still declaring the wider type; R2 the model catches up. A dev lead approves this (the running application must respect the new limit).
  Same shape as make-mandatory — see `../../_index/tightening-class/SKILL.md`.
- **populated, any value exceeds new size** → real truncation: the over-length rows are reconciled
  first (a data change), and the seed that feeds the column stops writing over-length values in the
  same change set (else `Msg 2628` at the post-deploy seed). Ships as **two releases**: R1 the
  pre-deploy reconciles the values and narrows the column with the model lagging; R2 the model
  catches up. A dev lead approves this, weighing that existing data is modified.
- **>1M rows** → added scrutiny: at production row counts the `ALTER COLUMN` rewrite may block
  writes or run long — schedule a window.

## Prove it
Run the `MAX(LEN(Col))` probe AND a `WHERE LEN(Col) > <new>` count to **quantify** how many rows
truncate. Under Strict, the publish must **block** on data loss when over-length rows exist — show
the count. Run Permissive (`BlockOnPossibleDataLoss=False`) and the before/after data hash to show
*exactly* which values would have been truncated. Author the reconcile, re-run Strict. For the
publish loop, see `../../prove-on-dacpac/SKILL.md`; probes, `../../talk-to-local-sql/SKILL.md`.

## The verdict (to the developer)
"You asked to shorten Code to 10 — it looks like a one-liner. On a copy of
Dev, SSDT refused it: the guard fires because the table has rows, not because a value is too long.
`<N>` codes are longer than 10 and would be cut; a permissive run shows exactly which characters
go. So the real question is those over-length codes: cut them to 10 on purpose, or is the extra
length real data to keep? Either way, with rows in the table it ships as two releases — R1
reconciles the values and narrows the column in a pre-deploy while the model still says wide, then
R2 lets the model catch up. On an empty table it would have been a clean one-liner."

## The reasoning (in conversation)
Narrowing shares one guard behaviour and one remedy shape with make-mandatory and delete-attribute
— making a column required, and dropping a column. The first question is never `MAX(LEN)`; it is
*is the table empty?* On an empty table the guard is inert and the narrowing just applies; on a
populated table the same data-blind guard blocks it, whether or not the values fit. Learn that
once and you stop re-discovering the same block one operation at a time. (The shared guard lives in
`../../_index/tightening-class/SKILL.md`.)

## On the record
Assemble the pull request from the `../../author-pr/SKILL.md` template; the worked instance for
this op is `../../../sample-prs/narrow.md`. **SHIP terminal: TWO-RELEASE** on a populated table,
ONE-RELEASE on an empty one. Take the line the data proves.

**Review & release**
- Empty table: `A dev lead approves this: the table is empty, so no data can be lost.` ·
  `Ships as a single schema change, applied in place. No data is read or written.`
- Populated, every value fits: `A dev lead approves this: after
  narrowing, the running application can no longer store values longer than the new size.` ·
  `Ships as two releases: R1 narrows the column in a pre-deploy with the model lagging, R2 the
  model catches up. The data-loss guard is not relaxed, because this pipeline cannot relax it.`
- Populated, values exceed the new size: `A dev lead approves this, weighing that existing data is modified —
  over-length values are reconciled before the column narrows.` · `Ships as two releases: R1
  reconciles the over-length values and narrows the column with the model lagging (the seed that
  feeds the column is reconciled in the same change set); R2 the model catches up.`
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
- Other environments — QA/UAT/Prod may hold longer values than this copy; run the verification
  query before promotion.
- Production scale / timing — the `ALTER COLUMN` rewrite cost at production row counts is not shown
  by the disposable copy.
- Reversibility — only the forward narrowing is exercised; a truncating reconcile cannot be undone.
