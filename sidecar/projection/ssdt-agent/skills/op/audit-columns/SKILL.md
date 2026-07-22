---
name: audit-columns
description: Use when the developer says "add CreatedBy/CreatedOn/ModifiedBy/ModifiedOn", "stamp who changed it and when", "basic audit fields", "add created/modified tracking columns" — ordinary audit stamp columns. SSDT destination = declarative nullable columns (or a pre-deployment backfill if the columns are NOT NULL on a populated table).
---

# Add manual audit columns (Optimistic-NOT-NULL trap)

> **Default (provisional — the data decides).** Nullable audit columns ship as a single schema
> change, applied in place; any team member can review, since the change is additive and the running
> application is unaffected. NOT NULL on a populated table ships as one release with a pre-deployment
> backfill, and a dev lead must review because existing data is modified — prove the backfill clears
> the block before you classify it.

## OutSystems phrasing
"add CreatedBy / CreatedOn / ModifiedBy / ModifiedOn", "stamp who changed it and when", "basic audit fields".

## SSDT meaning
Ordinary nullable columns (often with `DEFAULT SYSUTCDATETIME()` / `DEFAULT SUSER_SNAME()`) plus app-side or trigger-side stamping. SSDT ADDs them declaratively. Never write ALTER.

## The named trap
The *Optimistic NOT NULL* family — if the developer wants the audit columns `NOT NULL` on a populated table without a backfill, the deployment is blocked because existing rows have no `CreatedOn`. A `DEFAULT` covers new rows but **not** existing ones unless `GenerateSmartDefaults` stamps them (which Strict refuses, on purpose). This is the tightening class applied to a fresh column — see `../../_index/tightening-class/SKILL.md`; do not re-derive the row-presence guard here (`make-mandatory` owns the canonical treatment of this class).

## How it flips (the specifics only)
- nullable / table empty → ships as a single schema change, applied in place; any team member can
  review, since the change is additive and the application is unaffected.
- **`NOT NULL` + populated** → ships as one release with a pre-deployment backfill, then the columns
  land validated; the backfill that clears the block is the proof. A dev lead must review, because
  existing data is modified.
- **+ >1M rows** → added scrutiny: the backfill is a batched operation and may run long at production
  row counts.

## Prove it
If `NOT NULL`, Strict must block the publish on the existing rows with no audit value; the pre-deploy backfill must clear it; the Permissive run shows exactly what `GenerateSmartDefaults` would have silently stamped, so the developer sees what the block was protecting. See `prove-on-dacpac` / `talk-to-local-sql`. On the sample, exercise with the `Customer` seed (AUD-03).

## The verdict (to the developer)
You asked for CreatedBy / CreatedOn / ModifiedBy / ModifiedOn. As nullable columns they add in a single release — nothing in your existing data can conflict, so SSDT just applies them. If you want them mandatory (NOT NULL) instead, SSDT refuses on a populated table, because the rows already there have no value to put in the new columns — that's confirmed on a disposable copy of Dev. A pre-deployment backfill that stamps those rows clears it, and it still ships as one release. Do you need these mandatory, or is nullable enough?

## The reasoning (in conversation)
A `DEFAULT` describes the future, not the past: it fills new rows, but it never reaches back to the rows already in the table. So making a column mandatory always has to deal with the rows already there — the backfill stamps them, and the now-clean Strict run is the proof it worked. The trap to avoid is expecting NOT NULL with a default to just work on live data; it works on an empty table and blocks on a populated one. The full reasoning is in `../../_index/tightening-class/SKILL.md`.

## On the record
The fragment this operation contributes to the pull request (`../../author-pr/SKILL.md`). Pick the
branch the change actually took.

**Review & release**
- Nullable columns:
  - Ships as a single schema change, applied in place. No data is read or written.
  - Any team member can review this: the change is additive and the running application is unaffected.
- `NOT NULL` on a populated table:
  - Ships as one release: a pre-deployment script backfills the existing rows, then the schema change
    lands validated.
  - A dev lead must review this: existing data is modified.
- Added scrutiny, when it applies:
  - Added scrutiny: at production row counts the backfill may block writes or run long — schedule a
    window.

**Verification** — run in each environment after deployment
```sql
-- expect 0: no existing row is missing an audit value (meaningful only when the columns are NOT NULL)
SELECT COUNT(*) FROM <table>
WHERE CreatedBy IS NULL OR CreatedOn IS NULL OR ModifiedBy IS NULL OR ModifiedOn IS NULL;
```

**Rollback**
Both branches back out by dropping the added columns:
`ALTER TABLE <table> DROP COLUMN CreatedBy, CreatedOn, ModifiedBy, ModifiedOn;`. This returns the
table to its prior shape without data loss — the columns held only audit values introduced by this
change (including any the pre-deployment backfill stamped), and no pre-existing data is touched.

**Not verified**
- Application impact: whether the application or a trigger stamps these columns going forward is not
  confirmed here. A nullable column left unwritten stays NULL; a NOT NULL column with no app-side or
  default write rejects the next insert on a NULL violation. Owner: @app-owner.
- Other environments: Test / UAT / Prod may hold rows this copy does not, which a NOT NULL backfill
  must also cover. Run the verification query before promotion.
- Production scale / timing: at more than ~1M rows the backfill runs batched; its duration and
  locking are not shown on the small disposable copy.
