# Order → Status: drop the foreign key (removes the rule that every Order points at a real Status)

## Verdict
This PR drops `FK_Order_Status`, so the database no longer requires an Order's `StatusId` to match a
real Status. No row changes. Confirm nothing still depends on the database enforcing this — application
code and query plans — before promoting.

## Intent
The developer's stated intent for this PBI: remove the reference from Order to Status. No work item
supplied — attach one before merge, and state why the reference is going (the relationship is being
replaced, or Status is being retired).

## What changes
- `dbo.[Order]`: drop the foreign key `FK_Order_Status` (`Order.StatusId → Status.Id`). The `StatusId`
  column stays; only the constraint is removed.

## Before promoting
- Confirm a missing or wrong `StatusId` is now acceptable, or that the application still guarantees it
  points at a real Status. After this, the database no longer rejects an Order whose Status does not
  exist — nothing replaces that check unless the application does it.
- Re-check the plans of queries that join Order to Status. A trusted foreign key lets the optimizer
  drop the join or sharpen its row estimates; removing it can change those plans and slow the query.

## The data
- No data is touched. Dropping a foreign key is a metadata change only; every row stays as it is.

## How it ships
- One release, applied in place. The generated script is a single
  `ALTER TABLE [dbo].[Order] DROP CONSTRAINT [FK_Order_Status];` — proven below. No row is read or
  written, and the publish never blocks: a drop has nothing to validate.

## What proving showed
Published to a throwaway copy on this branch, starting from a state where `FK_Order_Status` existed
and was trusted.
- **Tried / Did:** publish the drop → the generated script is the single statement
  `ALTER TABLE [dbo].[Order] DROP CONSTRAINT [FK_Order_Status];`. `Successfully published database.`
- **Realized:** the key is gone (`sys.foreign_keys` count for `FK_Order_Status` is 0) and no row
  changed.

## After deploy — check
```sql
-- the key is gone, expect 0 rows
SELECT name FROM sys.foreign_keys WHERE name = 'FK_Order_Status';
```

## How to roll this back
Re-add the key. If every Order still points at a real Status, that is the `create-fk-clean` case and
it lands trusted in one release; see `create-fk-clean.md`. If an Order with a missing Status was
written while the key was gone, the re-add blocks with `Msg 547` and becomes the `create-fk-orphan`
case (reconcile first); see `create-fk-orphan.md`. The drop itself loses no data.

## Not checked / still open
- Application impact — any code path that relied on error 547 to catch a bad `StatusId` no longer
  gets it. That the application validates the Status another way is not confirmed here (app owner).
- Query plans — a plan regression from losing the trusted key is not shown by a small copy; measure
  the hot Order–Status queries at production scale.
- Re-adding later — while the key is gone, orphan Orders can accumulate; if the reference is restored
  later it may then be the `create-fk-orphan` case rather than `create-fk-clean`.
