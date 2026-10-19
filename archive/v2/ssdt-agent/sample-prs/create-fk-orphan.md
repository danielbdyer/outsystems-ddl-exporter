# Order → Customer: add a foreign key (one orphan order removed so the key can be trusted)

## Verdict
This PR adds a rule that every Order must point to a real Customer, and removes 1 order that points
to a customer who does not exist. Confirm that order is junk, not real, in each environment before
promoting. Removing it cannot be undone from the schema — the only rollback is a restore.

## Intent
The developer's stated intent for this PBI: make the database reject any Order that does not belong
to a real Customer, so a missing or wrong customer id is impossible going forward.

## What changes
- `dbo.[Order].CustomerId`: add a foreign key to `dbo.Customer(Id)`, named `FK_Order_Customer_CustomerId`.

## Before promoting
- Run the orphan query (below) in each environment and confirm every order it lists is junk that can
  be removed — the set differs per environment. If one is real, stop and reassign it to the right
  customer instead.
- The key is made trusted — SQL Server validates every existing row, enforces it, and the query
  planner can rely on it.

## The data
- 4 orders. 1 is an orphan: `Order 4 → CustomerId 999`, and no Customer 999 exists. It has 2 order lines.
- Orders 1–3 point to real customers.

## How it ships
- A pre-deploy step removes orders with no matching customer (their order lines first, then the
  orders). Idempotent — re-running removes nothing more.
- The seed no longer plants the orphan, so a fresh database is clean from the start.
- The row removal is a plain pre-deploy `DELETE`, which `BlockOnPossibleDataLoss` does not govern, so
  no gate change is needed.
- The orphan must be reconciled before the key is added — otherwise the publish is refused
  (`Msg 547`). Reconciled, the key validates and trusts itself (`is_not_trusted = 0`); nothing to add.

## What proving showed (published to a throwaway copy, this branch)
- **Tried:** publish the key, orphan still present → refused. `Msg 547`: the ALTER conflicted with
  `FK_Order_Customer_CustomerId` on `dbo.Customer.Id`. The orphan has no parent.
- **Did:** remove the orphan and its lines in a pre-deploy step; fix the seed; publish → succeeds,
  0 orphans remain.
- **Realized:** the generated script adds the key `WITH NOCHECK` and then re-validates it
  `WITH CHECK CHECK` in the same publish; with the orphan gone the key lands trusted
  (`is_not_trusted = 0`) on its own.
- **Confirmed:** the full change set on a fresh copy → key trusted automatically, 3 orders remain;
  re-publish → nothing changes, still trusted. A manual post-deploy `WITH CHECK CHECK` added on top
  changed nothing — it is redundant.

## After deploy — check (each environment)
```sql
-- before: the orders that will be removed here
SELECT o.Id, o.CustomerId FROM dbo.[Order] o
WHERE NOT EXISTS (SELECT 1 FROM dbo.Customer c WHERE c.Id = o.CustomerId);

-- after: the same query returns 0 rows, and the key is trusted (expect 0)
SELECT is_not_trusted FROM sys.foreign_keys WHERE name = 'FK_Order_Customer_CustomerId';
```

## How to roll this back
Drop the key: `ALTER TABLE dbo.[Order] DROP CONSTRAINT FK_Order_Customer_CustomerId;` — dropping loses
no data. The removed orders are not restored by dropping the key; they are in the pre-deploy step's
output for the run that removed them — restore by hand if any turn out to be real. Not tested.

## Not checked / still open
- The orphan's fate is the developer's call. This PR removes it as junk. If it is a real order,
  reassign it instead — a separate reconcile.
- The pre-deploy step also removes the orphan's order lines. If order lines feed a report or export,
  confirm that is safe.
- No load test: on a large table, validating the key and deleting rows can run long — schedule a window.
