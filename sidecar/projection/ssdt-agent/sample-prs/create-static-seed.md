# Category: add a static lookup entity (a declarative table + an idempotent seed; redeploys stay silent)

## Verdict
This PR adds the static lookup `dbo.Category` — a declarative table plus an idempotent MERGE seed. The
ids are explicit (not IDENTITY), so a Category id means the same row in every environment. No existing
data changes. Confirm the redeploy is silent — the seed touches 0 rows on a re-run — before promoting.

## Intent
The developer's stated intent for this PBI: add a Category lookup whose rows are part of the model, so
the application can reference a Category by a stable id. No work item supplied — attach one before merge.

## What changes
- `dbo.Category`: a new `CREATE TABLE` with an explicit-id primary key (no IDENTITY).
- `Script.PostDeployment.sql` → `Data/Seed.sql`: a guarded MERGE that seeds the Category rows.

## Before promoting
- Deploy, then redeploy unchanged in each environment and confirm the second deploy touches 0 rows and
  leaves an identical content hash — the redeploy is silent.
- Confirm no other environment already holds this lookup with drifted (IDENTITY-assigned) ids; the ids
  are model constants and must match everywhere.

## The data
- 3 `Category` rows carry explicit ids: `1 = Hardware`, `2 = Software`, `3 = Service`
  (`CHECKSUM_AGG(BINARY_CHECKSUM(Id, Code, IsActive)) = -1487866545`). No existing data is touched — the
  table is new.

## How it ships
- One release: the schema change, then the post-deploy seed runs the idempotent MERGE after the table
  lands. The lookup keys are explicit ids, never IDENTITY — a constant must mean the same row in every
  environment.

## What proving showed (published to a throwaway copy, this branch)
Proven on copies this branch (`pg_seed` first converge; `pg_base` re-run; sqlpackage 170.4.83.3).
- **Tried:** publish → the table lands and the post-deploy MERGE seeds its 3 rows
  (`Successfully published database.`), content hash `-1487866545`.
- **Did:** re-running the **guarded** MERGE over the unchanged rows touched **0 rows** (`@@ROWCOUNT = 0`)
  and left the content hash identical — the redeploy is silent.
- **Realized:** the silence is the guard. The same MERGE written **unguarded** (`WHEN MATCHED THEN
  UPDATE` with no column comparison) touched **3 rows** — it rewrites every row on every deploy even
  when nothing changed. The correctness property is that a no-op redeploy touches 0 rows and keeps an
  identical hash; the after-deploy hash re-checks it in each environment
  (`../skills/_index/idempotent-seed`).

## After deploy — check (each environment)
```sql
-- the lookup's model rows, by explicit id
SELECT Id, Code, IsActive FROM dbo.Category ORDER BY Id;

-- redeploy unchanged: expect an identical content hash across environments (the ids are explicit, so
-- the content matches). The guarded MERGE touches 0 rows on the re-run.
SELECT COUNT(*) AS rows, CHECKSUM_AGG(BINARY_CHECKSUM(Id, Code, IsActive)) AS content_hash FROM dbo.Category;
```

## How to roll this back
The seed rows are model data held in `Data/Seed.sql`, so removing the lookup loses no unique values:
drop any child foreign keys that reference it, then drop the table; a redeploy re-runs the same
idempotent seed to restore the rows. Backing the change out was not exercised.

## Not checked / still open
- Application impact — whether the running application references a Category id the seed does not provide
  is not confirmed by a copy (app owner).
- Other environments — a copy proves the redeploy is silent for this data shape; whether another
  environment already holds drifted ids from a prior IDENTITY-seeded table is not visible here. Run the
  hash check before promotion.
- Production scale — the MERGE's cost at production row counts is not shown by a small copy.
