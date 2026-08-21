# Order → Customer: add a foreign key (one orphan order removed so the key can be trusted)

## Verdict
This PR adds a rule that every Order must point to a real Customer, and removes 1 order that points
to a customer who does not exist. Confirm that order is junk, not a real order, in each environment
before promoting. Removing it cannot be undone from the schema — the only rollback is a restore.

## Intent
The developer's stated intent for this PBI: make the database reject any Order that does not belong
to a real Customer, so a missing or wrong customer id becomes impossible. No work item supplied —
attach one before merge.

## What changes
- `dbo.[Order].CustomerId`: add a foreign key to `dbo.Customer(Id)`, named `FK_Order_Customer_CustomerId`.

## Before promoting
- Run the orphan query (below) in each environment and confirm every order it lists is junk that can
  be removed — the set differs per environment. If one is real, stop and reassign it to the right
  customer instead.
- The key is made trusted, so SQL Server validates every existing row and the query planner can rely on it.

## How it ships
- One release. The row removal is a plain pre-deploy `DELETE`, which the data-loss gate does not govern,
  so no gate change is needed.
- DacFx adds the key `WITH NOCHECK` when a pre-deploy script is present, which leaves it untrusted. A
  post-deploy `ALTER TABLE dbo.[Order] WITH CHECK CHECK CONSTRAINT FK_Order_Customer_CustomerId`
  validates and trusts it. Without that step the key exists but SQL Server does not trust it. (A clean
  foreign key with no pre-deploy lands trusted in one step — see `create-fk-clean.md`.)

## The data
- 4 orders. 1 is an orphan: `Order 4 → CustomerId 999`, and no Customer 999 exists. It has 2 order lines.
- Orders 1–3 point to real customers.

## What proving showed
Published to a throwaway copy on this branch.
- **Tried:** add the key, publish → refused. `Msg 547`: the ALTER conflicted with
  `FK_Order_Customer_CustomerId` on `dbo.Customer.Id`. The orphan has no parent.
- **Did:** remove the orphan and its lines in a pre-deploy step; fix the seed; publish → succeeds.
- **Realized:** the key landed untrusted — `is_not_trusted = 1`; the generated script shows
  `ALTER TABLE [dbo].[Order] WITH NOCHECK ADD CONSTRAINT …`.
- **Did:** post-deploy `WITH CHECK CHECK` → `is_not_trusted = 0`. Full change set on a fresh copy →
  trusted automatically, 3 orders remain; re-publish → nothing changes, still trusted.

## After deploy — check
```sql
-- every order points at a real customer, expect 0 rows
SELECT o.Id, o.CustomerId FROM dbo.[Order] o
WHERE NOT EXISTS (SELECT 1 FROM dbo.Customer c WHERE c.Id = o.CustomerId);

-- the key is trusted, expect 0
SELECT is_not_trusted FROM sys.foreign_keys WHERE name = 'FK_Order_Customer_CustomerId';
```

## How to roll this back
Drop the key: `ALTER TABLE dbo.[Order] DROP CONSTRAINT FK_Order_Customer_CustomerId;` — dropping loses
no data. The removed orders are not restored by dropping the key; they are in the pre-deploy step's
output for the run that removed them. Backing the change out was not exercised.

## Not checked / still open
- The orphan's fate is the developer's call. This PR removes it as junk. If it is a real order,
  reassign it instead — a separate reconcile.
- The pre-deploy step also removes the orphan's order lines. If order lines feed a report or export,
  confirm that is safe.
- No load test: on a large table, validating the key and deleting rows can run long — schedule a window.
