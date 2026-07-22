---
name: modify-index
description: Use when the developer says "change the index to cover these columns too", "make this index unique so we stop getting duplicates", "the index should be on a different attribute now" — editing an index definition. SSDT does a DROP+CREATE rebuild; adding UNIQUE is blocked when duplicate values exist.
---

# Modify an index (change key columns / non-unique → unique / change include list)

> **Default (provisional — the data decides).** A key or include change ships as a single
> declarative schema change, applied in place, and any team member can review it. Adding UNIQUE is a
> claim over the data — prove no duplicates before classifying; with duplicates present it flips to a
> pre-deployment de-dupe plus the declarative change.

## OutSystems phrasing
"change the index to cover these columns too", "make this index unique so we stop getting duplicates", "the index should be on a different attribute now".

## SSDT meaning
The index definition is edited in the `.sql`. SSDT does **not** ALTER in place — it emits `DROP
INDEX` + `CREATE INDEX` (a full rebuild over all rows). Changing a non-unique index to **UNIQUE**
additionally enforces uniqueness at build time.

## The named trap
The **non-unique → unique build fails on duplicates** — SSDT emits the unique index and the deploy
fails the instant a duplicate key value exists. This is the index-grade case of
constraint-is-a-claim: the block is on a value collision, not on row presence — see
`../../_index/constraint-is-a-claim/SKILL.md`; do not re-derive the duplicate mechanics here. A key
or include change with no uniqueness added is never blocked by the data.

## How it flips (the specifics only)
- key or include change, no uniqueness added → ships as a single declarative schema change, applied
  in place; any team member can review it, though the DROP+CREATE rebuild blocks writes like
  add-index, and a large table pushes it to an experienced developer with a named window.
- non-unique → unique, NO duplicates → still a single declarative schema change, applied in place
  (prove it).
- non-unique → unique, duplicates PRESENT → **flip to a pre-deployment de-dupe plus the declarative
  change, in one PR**: the pre-deploy de-dupe clears the duplicates BEFORE the unique build; a dev
  lead must review this because existing data is modified — the block is on the actual duplicate
  values, see `../../_index/constraint-is-a-claim/SKILL.md`.
- \+ >1M rows → **added scrutiny**: at production row counts the rebuild and any de-dupe may block
  writes or run long (schedule a window — rebuild + dedupe cost).

## Prove it
For a uniqueness change, run the duplicate probe FIRST: `SELECT <keycols>, COUNT(*) FROM <table>
GROUP BY <keycols> HAVING COUNT(*) > 1`. Then build + Strict publish: clean → no duplicates; a build
failure ("CREATE UNIQUE INDEX … terminated because a duplicate key was found") means the deployment
is blocked. Author the pre-deploy de-dupe, re-run Strict, the clean re-run is the proof. See
`../../prove-on-dacpac/SKILL.md` + `../../talk-to-local-sql/SKILL.md`. Seed: Product's `DUPE` rows
drive the flip to a blocked deploy.

## The verdict (to the developer)
You asked to make that index unique. On a disposable copy of Dev, SSDT refused it: 3 rows share the
same value, and a unique index is built over every existing row, so it can't be built while those
duplicates are there. The way through is one release with a pre-deployment de-dupe that clears those
3 first — then the unique index builds clean on the copy. Are those three duplicate rows safe to
merge or delete?

## The reasoning (in conversation)
Any change that adds a *constraint* over existing data — UNIQUE here — can flip on what the data
holds, so probe first (`GROUP BY … HAVING COUNT(*) > 1`): the duplicate probe is what decides how
this ships, never the SQL statement. See `../../_index/constraint-is-a-claim/SKILL.md`. The failure
this avoids: guessing from the SQL whether the unique index just builds or needs a pre-deployment
de-dupe first.

## On the record
Fragments for the pull request (`../../author-pr/SKILL.md`), record register.

**Review & release**
- Any team member can review a key or include change: it is a structural rebuild and the running
  application is unaffected. Adding UNIQUE is a rule the running application must satisfy, so an
  experienced developer should review it; when a pre-deployment de-dupe is needed, a dev lead must
  review this: existing data is modified.
- Ships as a single declarative schema change, applied in place: SSDT emits `DROP INDEX` + `CREATE
  INDEX`, a full rebuild over all rows. With remediation, it ships as one release: a pre-deployment
  de-dupe clears the duplicates, then the unique index builds validated.
- Added scrutiny, when it applies: at production row counts the rebuild and any de-dupe may block
  writes or run long (schedule a window).

**Verification** — run in each environment after deployment
```sql
-- expect 0 rows: no value is shared across the key, so the uniqueness holds
SELECT <keycols>, COUNT(*) FROM <table> GROUP BY <keycols> HAVING COUNT(*) > 1;

-- expect 1 row, is_unique = 1: the modified index exists and enforces uniqueness
SELECT name, is_unique FROM sys.indexes
WHERE object_id = OBJECT_ID('<table>') AND name = '<index>';
```

**Rollback**
Revert the `.sql` edit and republish; SSDT emits `DROP INDEX` + `CREATE INDEX` to restore the prior
index shape. The index change is lossless — an index holds no source data, only a derived structure
— but re-creating it runs the same write-blocking rebuild. A pre-deployment de-dupe is not
auto-reversed; the rows it removed or merged, recorded under Data remediation, are what a manual
restore uses.

**Not verified**
- Application impact — once the index is unique, any insert or update that would create a duplicate
  key value now fails ("duplicate key was found"). Application-side handling is not confirmed here
  (@app-owner).
- Other environments — Test, UAT, and Prod may hold duplicates the disposable copy of Dev cannot
  see. Run the verification query before promotion.
- Production scale and timing — on a large table the DROP+CREATE rebuild and any de-dupe may block
  writes or run long; the small copy does not show it.
- Reversibility — reverting the index shape is lossless, but a pre-deployment de-dupe is not
  exercised in reverse here; the recorded originals are what a manual restore would use.
