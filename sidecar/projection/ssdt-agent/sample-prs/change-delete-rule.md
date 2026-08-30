# Order → Status: change the delete rule to Cascade (deleting a Status deletes its Orders)

## Verdict
This PR changes the foreign key `FK_Order_Status` so that deleting a Status now deletes every Order
that points to it. No existing row changes on deploy, but the delete behaviour changes — confirm that
cascading a Status delete to its Orders is intended in each environment before promoting.

## Intent
The developer's stated intent for this PBI: when a Status is removed, its Orders should go with it
rather than block the delete — "turn on cascade delete for the Status reference." No work item
supplied — attach one before merge.

## What changes
- `FK_Order_Status` (`Order.StatusId → Status.Id`): the delete rule changes from `NO ACTION` to `CASCADE`.

## Before promoting
- Confirm the cascade is intended: after this, deleting a Status also deletes every Order with that
  StatusId. It does not chain further on its own — on the copy the deleted Orders' OrderLines were
  left orphaned — so decide with the data owner whether orphaned children are acceptable, or the
  chain must cascade too.
- No existing row changes when this deploys; the change is to future delete behaviour only.

## The data
- No row is written. The change is to the key's delete rule, not to any row. The re-add re-validates
  every child row against the parent (a read), which at production row counts is a scan.

## How it ships
- One release, applied in place. SQL Server cannot change a delete rule in place, so the generated
  script drops the key and re-adds it with the new rule — proven below. No row is written, but the
  re-add re-validates every child row against the parent (a read), so the key ends trusted
  (`is_not_trusted = 0`). At production scale that re-validation is a scan.

## What proving showed
Published to a throwaway copy on this branch.
- **Tried / Did:** publish the delete-rule change → the generated script is, in order,
  `ALTER TABLE [dbo].[Order] DROP CONSTRAINT [FK_Order_Status];`,
  `ALTER TABLE [dbo].[Order] WITH NOCHECK ADD CONSTRAINT [FK_Order_Status] FOREIGN KEY ([StatusId]) REFERENCES [dbo].[Status] ([Id]) ON DELETE CASCADE;`,
  then `ALTER TABLE [dbo].[Order] WITH CHECK CHECK CONSTRAINT [FK_Order_Status];`. The publish is
  clean; afterward `delete_referential_action_desc = CASCADE` and `is_not_trusted = 0`. No row is written.
- **Realized:** the risk is not in the deploy, which reads but does not write rows, but in the new
  delete behaviour — a Status delete now reaches its Orders.
- **Also proved (cascade reach):** on the copy, deleting one Status removed its 2 Orders — the cascade
  fired — and left their 4 OrderLines behind, orphaned, because no cascading foreign key reaches
  OrderLine. The cascade goes one level, to the child whose key declares CASCADE, and stops.

## After deploy — check
```sql
-- the delete rule is CASCADE, expect delete_referential_action_desc = CASCADE
SELECT name, delete_referential_action_desc FROM sys.foreign_keys WHERE name = 'FK_Order_Status';
```

## How to roll this back
Change the rule back the same way — drop and re-add the key with `ON DELETE NO ACTION`. No data is
lost by the change itself. Backing the change out was not exercised.

## Not checked / still open
- Application impact — any code that relied on a Status delete being blocked by its Orders now finds
  the Orders deleted instead. That this is intended everywhere is not confirmed here (app owner).
- Cascade reach — on the copy the cascade reached the Orders and stopped: their OrderLines were left
  orphaned, because no cascading key reaches OrderLine. Whether that is acceptable, or OrderLine
  should cascade too, is a data-model decision — settle it before promotion.
- Production scale and timing — a cascading delete at production row counts can remove far more than
  expected and run long; a small copy does not show that.
