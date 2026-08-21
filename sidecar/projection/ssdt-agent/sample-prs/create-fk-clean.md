# Order → Status: add a foreign key (clean data, so it lands trusted in one release)

## Verdict
This PR adds a rule that every Order must point to a real Status. Every existing order already does,
so it applies in one release and the key is trusted from the start. Confirm no order in each
environment points at a missing Status before promoting — the query is below.

## Intent
The developer's stated intent for this PBI: make the database reject an Order whose Status does not
exist, so a wrong or missing StatusId becomes impossible. No work item supplied — attach one before merge.

## What changes
- `dbo.[Order].StatusId`: add a foreign key to `dbo.Status(Id)`, named `FK_Order_Status`.

## Before promoting
- Run the orphan query (below) in each environment and confirm it returns 0 rows — every order points
  at a real Status. If any environment holds an order with a missing Status, it is the
  create-fk-orphan case instead (a pre-deploy reconcile first); see `create-fk-orphan.md`.
- The key is trusted, so SQL Server has validated every existing row and the query planner can rely on it.

## How it ships
- One release, applied in place. No pre-deploy is needed because the child data is already clean.
  DacFx adds the key `WITH CHECK`, which validates the existing rows and marks it trusted — proven below.
- This is the clean counterpart to `create-fk-orphan`: there, an orphan forces a pre-deploy reconcile,
  and because a pre-deploy is present DacFx adds the key `WITH NOCHECK` (untrusted), so a post-deploy
  `WITH CHECK CHECK` is required. Here, with no pre-deploy, the key is trusted in one step.

## The data
- 4 orders. Every StatusId (1, 2, 3) matches a seeded Status row. No order points at a missing Status.

## What proving showed
Published to a throwaway copy on this branch.
- **Tried:** add the key, publish under the data-loss guard → `Successfully published database.` No
  block: the child data is clean.
- **Realized:** with clean data and no pre-deploy, DacFx adds the key `WITH CHECK` and it lands
  trusted — `is_not_trusted = 0`. A re-publish changed nothing.

## After deploy — check
```sql
-- every order points at a real status, expect 0 rows
SELECT o.Id, o.StatusId FROM dbo.[Order] o
WHERE NOT EXISTS (SELECT 1 FROM dbo.Status s WHERE s.Id = o.StatusId);

-- the key is trusted, expect 0
SELECT is_not_trusted FROM sys.foreign_keys WHERE name = 'FK_Order_Status';
```

## How to roll this back
Drop the key: `ALTER TABLE dbo.[Order] DROP CONSTRAINT FK_Order_Status;` — dropping loses no data.
Backing the change out was not exercised.

## Not checked / still open
- Application impact — any code path that writes an Order with a StatusId that does not exist is now
  rejected with error 547. Application-side validation is not confirmed here; the app owner owns closing it.
- Other environments — the copy's orders were clean; Test, UAT, and Prod may hold an order with a
  missing Status. Run the orphan query before promotion; if it finds rows, use `create-fk-orphan`.
- Production scale and timing — validating the key at large row counts may run long; a small copy
  does not show that.
