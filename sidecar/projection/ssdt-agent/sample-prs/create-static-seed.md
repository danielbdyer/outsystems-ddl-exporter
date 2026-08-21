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
- Deploy, then redeploy unchanged in each environment and confirm the second deploy reports 0 rows
  affected and leaves an identical seed hash — the redeploy is silent.
- Confirm no other environment already holds this lookup with drifted (IDENTITY-assigned) ids; the ids
  are model constants and must match everywhere.

## How it ships
- One release: the schema change, then the post-deploy seed runs the idempotent MERGE after the table
  lands. The lookup keys are explicit ids, never IDENTITY — a constant must mean the same row in every
  environment.

## The data
- The Category rows carry explicit ids. No existing data is touched — the table is new.

## What proving showed
Published to a throwaway copy on this branch.
- **Tried / Did:** publish → the table lands and the post-deploy MERGE seeds its rows. `Successfully
  published database.`
- **Realized:** the correctness property is silence — a no-op redeploy must touch 0 rows and keep an
  identical hash (the guarded `WHEN MATCHED` compares each column before updating). That silent-redeploy
  property is the idempotent-seed law (`skills/_index/idempotent-seed`, Twin-proven); the after-deploy
  hash re-checks it in each environment.

## After deploy — check
```sql
-- the lookup's model rows, by explicit id, unchanged
SELECT Id, Code, IsActive FROM dbo.Category ORDER BY Id;

-- redeploy unchanged: expect 0 rows affected by the seed MERGE and an identical content hash across
-- environments (the ids are explicit, so the content matches):
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
