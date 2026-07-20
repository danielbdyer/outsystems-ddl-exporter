---
name: backfill-rows
description: Use when the developer says "fill in the existing rows too", "set every existing Region to Unknown", "the default only covers new rows — update the old ones", "backfill the blanks" — a data-plane UPDATE of existing rows, not a schema change. The companion to add-default / modify-default / make-mandatory when the ask includes existing data. Ships as a post-deployment idempotent guarded UPDATE; a dev lead must review it because existing data is modified.
---

# Backfill existing rows (the data-plane UPDATE)

> **Default (provisional — the data decides).** Ships as one release: after any schema change
> lands, a post-deployment **guarded, idempotent UPDATE** re-stamps only the rows that need it.
> A dev lead must review this: existing data is modified. Prove the UPDATE touches exactly the
> rows it should — and that a redeploy touches zero — before classifying.

## OutSystems phrasing
"fill in the existing rows too", "set every existing Region to Unknown", "the default only covers
new rows — update the old ones as well", "backfill the blanks", "re-stamp the old orders to the
new status".

## SSDT meaning
A **data-plane UPDATE** of existing rows, carried in `Script.PostDeployment.sql` so it runs
**after** the schema lands. It is **not** a schema change and **never** an ALTER — the CREATE is
untouched. This is the op the default/tightening skills hand off to: `add-default` and
`modify-default` govern only *future* rows; `make-mandatory` needs the blanks gone before a
populated table can tighten. Filling the *existing* rows is this op, and it is proven the same
way — on a disposable copy, by what a redeploy does **not** do.

## The named trap
The **unguarded UPDATE** that re-stamps every row on **every** deploy — the same failure the seed
MERGE guards against, now on business data. It is owned by `../../_index/idempotent-seed/SKILL.md`
(the guarded, null-safe write; silence-is-the-proof); do not re-derive the guard here. Two faces of
the same trap:
- **CDC over-capture** — on a CDC-tracked table an unguarded re-stamp floods the capture feed with
  phantom changes on every redeploy (`../../_index/cdc/SKILL.md`).
- **The full-table write at scale** — an ungated `UPDATE <t> SET …` locks and runs long at
  production row counts; guard it to the rows that differ, and batch it when the count is large.

A backfill that *removes* data (a bulk `DELETE`, or overwriting values that cannot be reconstructed)
is not this op — route a deletion of populated rows to `../delete-attribute/SKILL.md` /
`../delete-seed-value/SKILL.md`; a principal must review an irreversible removal.

## How it flips (the specifics only)
- fill the blanks / re-stamp existing rows to a value → ships as one release: the schema change (if
  any), then a post-deployment **guarded** UPDATE that touches only the rows whose value differs. A
  dev lead must review it: existing data is modified. Record the original values for the audit trail.
- paired with `make-mandatory` on a populated table → the backfill clears the NULLs but **does not**
  clear the tightening block (the guard is table-has-rows, not column-has-NULLs) — the NOT NULL
  still needs the deliberate gate call in `../make-mandatory/SKILL.md`. The backfill is necessary,
  never sufficient, there.
- no row needs the change (every value already matches) → the guarded UPDATE touches **0 rows**; it
  is a semantic no-op, and its silence is the proof it was idempotent.
- **+ CDC-tracked** → added scrutiny: the guard is mandatory so a redeploy captures 0, not the whole
  table (`../../_index/cdc/SKILL.md`).
- **+ >1M rows** → added scrutiny: a single-statement UPDATE blocks writes or runs long at
  production scale; batch it and schedule a window (the small copy cannot show the duration).

## Prove it
1. **Predict** — probe how many rows need the change: `SELECT COUNT(*) FROM <t> WHERE <col> IS NULL`
   (or `WHERE <col> <> <newvalue>`, null-safe). That count is what the UPDATE must touch, and no more.
2. Author the guarded UPDATE in `Script.PostDeployment.sql` — a `WHERE` that selects only the rows
   whose value actually differs (null-safe: `NULL` is distinct from `''`), so a re-run is a no-op.
3. Deploy → prove the UPDATE touched **exactly** the predicted count (not the table size).
4. **Redeploy unchanged** → prove **0 rows affected** + an **identical content-hash** (+ **0 CDC
   captures** if the table is tracked). That silence is the idempotency proof — see
   `../../_index/idempotent-seed/SKILL.md` for the discipline and `../../talk-to-local-sql/SKILL.md`
   for the hash and rowcount probes; `../../prove-on-dacpac/SKILL.md` for the deploy-twice loop.

On the sample, backfilling `dbo.Customer.Region` (blank on some rows) to `N'Unknown'` exercises the
guarded UPDATE and the silent redeploy. (No self-test case is registered for this op yet — see
`../../../CERTIFICATION_PLAN.md` Stage 1.)

## The verdict (to the developer)
You asked to fill in the existing rows, not just the new ones. That's a change to data you already
have, so it rides in the post-deployment script as a guarded update — it touches only the rows that
actually need it, and I proved that: it updated exactly the blank rows, and a second deploy moved
nothing. Because it edits existing values, a dev lead reviews it, and I've recorded the original
values so it can be backed out. Do the blanks all get the same value, or does it depend on the row?

## The reasoning (in conversation)
A default is a rule about future writes; a backfill reaches back and changes rows that already
exist — different risk, different reviewer. The discipline that keeps it safe is the same one the
seed data uses: guard the write so it only touches rows that differ, and a redeploy stays silent.
The failure this avoids is the ungated `UPDATE … SET` that re-stamps every row on every deploy — on
a change-data-capture table that reports the whole table as changed, phantom edits that never
happened. Silence on the second run is the proof it was done right
(`../../_index/idempotent-seed/SKILL.md`).

## On the record
Fragments for the pull request (`../../author-pr/SKILL.md`), record register.

**Review & release**
- A dev lead must review this: existing data is modified — an existing column's values are
  re-stamped on the rows that need it.
- Ships as one release: after the schema change lands, a post-deployment guarded UPDATE re-stamps
  only the differing rows. No table rebuild; the CREATE is unchanged.
- Added scrutiny, when it applies: the table feeds a change-data-capture stream (the guard is
  mandatory so the redeploy captures only the changed rows); or at production row counts the UPDATE
  blocks writes or runs long — batch it and schedule a window.

**Verification** — run in each environment after deployment
```sql
-- expect 0 rows: no row still holds the pre-backfill value
SELECT COUNT(*) AS unfilled FROM dbo.Customer WHERE Region IS NULL;
```

**Rollback**
The forward UPDATE is not auto-reversed — a backfill overwrites values in place. The original values
recorded under Data remediation are what a manual restore uses; without them the change is not
losslessly reversible. State that plainly rather than claiming a clean rollback.

**Not verified**
- Application impact — code paths that distinguished a blank from the backfilled value now see the
  value everywhere; whether any logic relied on the blank is not confirmed here (@app-owner).
- Other environments — Test, UAT, and Prod may hold a different set of rows needing the backfill;
  run the predict probe in each before promotion, since the count and the affected rows differ.
- Production scale and timing — on a large table the UPDATE may block writes or run long; the small
  copy does not show the duration.
- Reversibility — the forward backfill is proven; restoring the pre-backfill values is not exercised
  here and depends on the recorded originals.
