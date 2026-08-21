# OrderLine: define the primary key on (OrderId, LineNumber) (clean key — builds in one release)

## Verdict
This PR makes (`OrderId`, `LineNumber`) the primary key of `dbo.OrderLine`. Every existing pair is
unique and neither column is null, so the key builds in one release. Confirm in each environment that
no two lines share an (`OrderId`, `LineNumber`) and neither column holds a null before promoting — a
duplicate or a null stops the build.

## Intent
The developer's stated intent for this PBI: identify an order line by its order and its line number,
so the pair is the entity's key. No work item supplied — attach one before merge.

## What changes
- `dbo.OrderLine`: the primary key becomes the composite (`OrderId`, `LineNumber`). Both columns are
  already `NOT NULL`.

## Before promoting
- Run the duplicate query and the null query (below) in each environment and confirm both return 0
  rows. A duplicate pair or a null in either column blocks the build; reconcile the data first if
  either query finds rows.
- A primary key is a claim about the existing data: it is checked when the index is built, so the
  data must satisfy it before the key can land.

## How it ships
- One release, applied in place. The key builds over the existing rows because they are already
  unique and not null.
- If a duplicate or a null were present, this would ship as a scripted change instead: a pre-deploy
  step reconciles the data (dedupe, or fill the null), then the key builds — the same
  reconcile-then-constraint shape as a foreign key or a unique constraint.

## The data
- 8 order lines. Every (`OrderId`, `LineNumber`) pair is distinct, and both columns are `NOT NULL`.
  Nothing blocks the key.

## What proving showed
The block behaviour was proven on a throwaway copy on this branch, on a small table shaped like the
key column:
- **A duplicate key is refused:** building a primary key over a column holding two equal values →
  `Msg 1505`, "The CREATE UNIQUE INDEX statement terminated because a duplicate key was found",
  then `Msg 1750`, "Could not create constraint or index."
- **A nullable key is refused:** building a primary key over a nullable column → `Msg 8111`, "Cannot
  define PRIMARY KEY constraint on nullable column", then `Msg 1750`.
- On the clean (`OrderId`, `LineNumber`) data, neither fires and the key builds.

## After deploy — check
```sql
-- no duplicate pair, expect 0 rows
SELECT OrderId, LineNumber, COUNT(*) FROM dbo.OrderLine
GROUP BY OrderId, LineNumber HAVING COUNT(*) > 1;

-- the composite primary key exists, expect one row
SELECT name FROM sys.key_constraints
WHERE parent_object_id = OBJECT_ID('dbo.OrderLine') AND type = 'PK';
```

## How to roll this back
Drop the key: `ALTER TABLE dbo.OrderLine DROP CONSTRAINT <pk name>;` — dropping the key loses no data.
If a prior primary key was replaced, restoring it is a separate change. Backing the change out was not
exercised.

## Not checked / still open
- Application impact — code that assumed a different key, or that inserts a duplicate (`OrderId`,
  `LineNumber`), now fails. That every write respects the new key is not confirmed here (app owner).
- Other environments — the copy's data was clean; Test, UAT, and Prod may hold a duplicate pair or a
  null. Run the duplicate and null queries before promotion.
- Production scale and timing — building the key at production row counts may run long and lock the
  table; a small copy does not show that.
