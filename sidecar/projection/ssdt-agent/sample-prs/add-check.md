# Order: enforce Total > 0 (a positive-total rule; a violating order must be reconciled or the add blocks)

## Verdict
This PR adds a rule that every Order's Total must be greater than 0. On the copy every order already
satisfies it, so it applies in one release and the rule is enforced from the start. Confirm no order in
each environment has a Total of 0 or less before promoting — the query is below; a violating order
blocks the deploy until it is reconciled.

## Intent
The developer's stated intent for this PBI: make the database reject any Order whose Total is not
positive, so a zero or negative total becomes impossible. No work item supplied — attach one before merge.

## What changes
- `dbo.[Order]`: add a CHECK constraint `CK_Order_Total` requiring `Total > 0`.

## Before promoting
- Run the violation query (below) in each environment and confirm it returns 0 rows. The set differs
  per environment.
- If any order has `Total <= 0`, the deploy blocks until those rows are reconciled — correct the value,
  or handle them another way, in a pre-deploy step. That is a data-owner decision; do not guess it.

## The data
- 4 orders. Every `Total` is positive (the smallest is `75.25`). No order violates `Total > 0`.

## How it ships
- One release, applied in place. Every order already satisfies the rule, so the check validates and
  trusts itself on publish (`is_not_trusted = 0`) — nothing to add.
- If a violating order were present, the publish would be refused (`Msg 547`) until it is reconciled in
  a pre-deploy step; then the check validates and trusts itself the same way.

## What proving showed
Published to a throwaway copy on this branch.
- **Tried (violating leg):** set one order to `Total = -5`, add the check, publish → refused.
  `Msg 547`: the ALTER conflicted with `CK_Order_Total` on `dbo.[Order]`, column `Total`.
- **Did (clean leg):** with every `Total` positive, add the check, publish → `Successfully published
  database.`
- **Realized:** the generated script adds the check `WITH NOCHECK` and then re-validates it
  `WITH CHECK CHECK` in the same publish; over clean data it lands trusted (`is_not_trusted = 0`) on
  its own. A blocked publish (a violating row) leaves the check present but untrusted — reconcile the
  row and re-publish.

## After deploy — check
```sql
-- every order satisfies the rule, expect 0 rows
SELECT Id, Total FROM dbo.[Order] WHERE NOT (Total > 0);

-- the check is trusted, expect 0
SELECT is_not_trusted FROM sys.check_constraints WHERE name = 'CK_Order_Total';
```

## How to roll this back
Drop the check: `ALTER TABLE dbo.[Order] DROP CONSTRAINT CK_Order_Total;` — dropping loses no data.
This is also the cleanup for an untrusted check left behind by a blocked attempt. If a pre-deploy
reconcile changed any `Total`, that is not auto-restored — the original values are in the pre-deploy
step's output for the run that changed them. Backing the change out was not exercised.

## Not checked / still open
- Application impact — any write that sets an order's `Total` to 0 or less is now rejected with error
  547; application-side validation is not confirmed here (app owner).
- Other environments — the copy's orders were clean; Test, UAT, and Prod may hold an order with
  `Total <= 0`. Run the violation query before promotion; if it finds rows, reconcile them first.
- Production scale and timing — validating the check at production row counts may run long or block
  writes; a small copy does not show that.
