---
name: audit-columns
description: Use when the developer says "add CreatedBy/CreatedOn/ModifiedBy/ModifiedOn", "stamp who changed it and when", "basic audit fields", "add created/modified tracking columns" — ordinary audit stamp columns. SSDT destination = declarative nullable columns (or a pre-deployment backfill if the columns are NOT NULL on a populated table).
---

# Add manual audit columns (Optimistic-NOT-NULL trap)

> **Default (provisional — prove before you classify).** Nullable audit columns ship as a single schema
> change, applied in place; a dev lead approves them with the lightest look, since the change is additive and the running
> application is unaffected. NOT NULL on a populated table ships as one release with a pre-deployment
> backfill, and the approval weighs that existing data is modified — prove the backfill clears
> the block before you classify it.

> **SHIP terminal: ONE RELEASE, in place** (nullable columns, or `NOT NULL` with an explicit default
> that stamps every existing row as the columns land); **ONE RELEASE with a pre-deploy backfill** for
> `NOT NULL` with no default on a populated table. Proven live on this branch (SQL Server 2022,
> `sqlpackage 170.4.83.3`), adding `CreatedBy`/`CreatedOn`/`ModifiedBy`/`ModifiedOn` to
> `dbo.Customer` (5 rows): as `NOT NULL` with `DEFAULT (SUSER_SNAME())` / `DEFAULT (SYSUTCDATETIME())`
> the strict publish returns `Successfully published database.` and all 5 existing rows are stamped
> (for example `CreatedBy = sa`); the same four columns added as `NULL` also publish clean, leaving
> the 5 rows NULL.
>
> **Proven precedent:** `../../../sample-prs/audit-columns.md` — the worked instance of the
> ten-section pull-request template (`../../author-pr/SKILL.md`) for this op, carrying the live proof
> messages.

## OutSystems phrasing
"add CreatedBy / CreatedOn / ModifiedBy / ModifiedOn", "stamp who changed it and when", "basic audit fields".

## SSDT meaning
Ordinary nullable columns (often with `DEFAULT SYSUTCDATETIME()` / `DEFAULT SUSER_SNAME()`) plus app-side or trigger-side stamping. SSDT ADDs them declaratively. Never write ALTER.

## The named trap
The *Optimistic NOT NULL* family — if the developer wants the audit columns `NOT NULL` on a populated table with no value supplied, the deployment is blocked because existing rows have no `CreatedOn`. This is the same value-needed refusal as `../add-mandatory/SKILL.md`, and it is deliberately **not** the tightening class: a fresh column's block is **cured by supplying the value** — an explicit `DEFAULT` (e.g. `SYSUTCDATETIME()`) stamps every existing row as the column lands and a populated table applies clean (proven: `../../../sample-prs/add-default.md`), which the tightening class's data-blind row-presence guard would never allow. The neighbouring *existing-column* tightening (`make-mandatory`) is the class no default can cure — `../../_index/tightening-class/SKILL.md` keeps the two apart; do not collapse them.

## How it flips (the specifics only)
- nullable / table empty → ships as a single schema change, applied in place; the lightest look,
  since the change is additive and the application is unaffected.
- **`NOT NULL` + populated + explicit DEFAULT** (`SYSUTCDATETIME()` / `SUSER_SNAME()`) → ships as
  a single schema change, applied in place — the default stamps every existing row as the columns
  land (the `add-mandatory`/`add-default` proven shape); a dev lead approves it, and the stamped values are named on the record.
- **`NOT NULL` + populated, no default** → the deployment is blocked (value-needed); ships as one
  release with a pre-deployment staging backfill, then the columns land validated. The approval
  weighs that existing data is modified.
- **+ >1M rows** → added scrutiny: the backfill is a batched operation and may run long at production
  row counts.

## Prove it
If `NOT NULL` with an explicit DEFAULT, Strict must publish clean and the delta must show the default stamping as the columns land — prove the stamped values. If `NOT NULL` with no default, Strict must block the publish on the existing rows with no audit value (value-needed); the staging backfill must clear it; the Permissive run shows exactly what `GenerateSmartDefaults` would have silently stamped, so the developer sees what the block was protecting. See `prove-on-dacpac` / `talk-to-local-sql`. On the sample, exercise with the `Customer` seed (AUD-03).

## The verdict (to the developer)
You asked for CreatedBy / CreatedOn / ModifiedBy / ModifiedOn. As nullable columns they add in a single release — nothing in your existing data can conflict, so SSDT just applies them. If you want them mandatory (NOT NULL) instead, the rows already there need a value: with an explicit default (like "now" for CreatedOn) SQL Server stamps every existing row as the columns land and it still applies in one clean step — confirmed on a disposable copy of Dev. Without a default it's refused, and a staging backfill has to fill the rows first. Do you need these mandatory, and if so, is a stamped default value right for the rows that already exist?

## The reasoning (in conversation)
A fresh column's block is about a missing value, and the cure is supplying it: an explicit default stamps the rows that are already there as the column lands, and the clean Strict run is the proof. On an existing column the same word behaves oppositely — a default describes future inserts and never reaches back — which is why the two shapes must not be conflated (see `../add-default/SKILL.md`). The trap to avoid is letting `GenerateSmartDefaults` decide the value silently: who supplies the stamp for existing rows is a data-owner decision, made explicitly. The neighbouring existing-column tightening — which no default can cure — is `../../_index/tightening-class/SKILL.md`.

## On the record
The pull request is an instance of the ten-section template in `../../author-pr/SKILL.md`; the worked
instance for this op — with the live proof messages — is `../../../sample-prs/audit-columns.md`. SHIP
terminal: **ONE RELEASE, in place** (nullable, or `NOT NULL` with an explicit default); **ONE RELEASE
with a pre-deploy backfill** for `NOT NULL` with no default. Pick the branch the change actually took.

**Review & release**
- Nullable columns:
  - Ships as a single schema change, applied in place. No data is read or written.
  - A dev lead approves this: the change is additive and the running application is unaffected — the lightest look on this estate.
- `NOT NULL` on a populated table, explicit DEFAULT:
  - Ships as a single schema change, applied in place — the default stamps every existing row as
    the columns land; the stamped values are named here.
  - A dev lead approves this: existing rows receive stamped values,
    and the running application must keep the columns filled.
- `NOT NULL` on a populated table, no default:
  - Ships as one release: a pre-deployment script backfills the existing rows, then the schema change
    lands validated.
  - A dev lead approves this, weighing that existing data is modified.
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
  default write rejects the next insert on a NULL violation. Owner: app owner.
- Other environments: QA / UAT / Prod may hold rows this copy does not, which a NOT NULL backfill
  must also cover. Run the verification query before promotion.
- Production scale / timing: at more than ~1M rows the backfill runs batched; its duration and
  locking are not shown on the small disposable copy.
