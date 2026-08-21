# Order → Customer: add a foreign key (one orphan order removed so the add does not block)

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
- Confirm the pre-deploy `DELETE` matches the orphan set in each environment: it removes exactly the
  orders the orphan query returns, and their order lines. Test, UAT, and Prod may hold more, fewer,
  or different orphans than the copy.

## How it ships
- One release. The row removal is a plain pre-deploy `DELETE`, which the data-loss gate does not govern,
  so no gate change is needed.
- The orphan must be removed **before** the key is added, or the publish blocks: the generated script
  adds the key `WITH NOCHECK` and then re-validates it `WITH CHECK CHECK` in the same publish, and that
  re-validation fails on the orphan with `Msg 547`. With the orphan gone, the same re-validation passes
  and the key lands trusted (`is_not_trusted = 0`) — no separate trust step.
- The child table is seeded in this project, so the seed that plants the orphan is fixed in the same
  change set (the orphan's row is repointed to a real customer); otherwise the post-deploy seed
  re-inserts the orphan and the publish fails.

## The data
- 4 orders. 1 is an orphan: `Order 4 → CustomerId 999`, and no Customer 999 exists. It has 2 order lines.
- Orders 1–3 point to real customers.

## What proving showed
Published to a throwaway copy on this branch.
- **Tried:** add the key, publish, orphan still present → refused. `Msg 547`: the ALTER conflicted
  with `FK_Order_Customer_CustomerId` on `dbo.Customer.Id`. The orphan has no parent. The failed
  publish leaves the key present but untrusted — the deploy is not complete.
- **Did:** remove the orphan and its lines in a pre-deploy step; fix the seed; publish → succeeds,
  0 orphans remain.
- **Realized:** the generated script adds the key `WITH NOCHECK` and re-validates it
  `WITH CHECK CHECK` in the same publish; with the orphan gone the key lands trusted —
  `is_not_trusted = 0`. A re-publish changed nothing.
- **Also tried:** a manual post-deploy `WITH CHECK CHECK` on top → `is_not_trusted = 0`, identical.
  The manual step is redundant; the declarative add already trusts the key.

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
- Trust on a different build config — the key landed trusted on this project's build; a project whose
  DacFx settings suppress the `WITH CHECK CHECK` would leave it untrusted. Confirm `is_not_trusted = 0`
  after deploy; if it is 1, a post-deploy `ALTER TABLE dbo.[Order] WITH CHECK CHECK CONSTRAINT
  FK_Order_Customer_CustomerId` re-trusts it.
- No load test: on a large table, the re-validation and the delete can run long — schedule a window.
