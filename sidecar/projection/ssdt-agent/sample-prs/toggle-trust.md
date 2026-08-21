# Order → Customer: re-trust the foreign key after a bulk load (WITH CHECK CHECK; ends is_not_trusted = 0)

## Verdict
This PR re-trusts `FK_Order_Customer_CustomerId`, which a bulk load left untrusted by running with the
constraint disabled. It re-validates every child row and ends trusted, so the optimizer honors it
again. This is an operational script step, not a schema change. Confirm the child data is clean (no
orphan) so the re-validation passes.

## Intent
The developer's stated intent for this PBI: restore trust on a foreign key that a bulk load left
untrusted (`is_not_trusted = 1`), so it is enforced and the query planner can rely on it. No work item
supplied — attach one before merge.

## What changes
- Nothing in the table definition. A script step:
  `ALTER TABLE dbo.[Order] WITH CHECK CHECK CONSTRAINT FK_Order_Customer_CustomerId;`

## Before promoting
- Run the orphan query (below) in each environment and confirm 0 rows. If any orphan exists, the
  `WITH CHECK CHECK` blocks (`Msg 547`) — reconcile the orphan first, then re-trust.

## How it ships
- An operational script step, not part of the dacpac model. A *fresh* declarative FK or CHECK add
  already re-validates and trusts itself (F9/F10), so this op is not that — it re-trusts a constraint
  left untrusted **another way**: a legacy hand-written `WITH NOCHECK`, or one disabled for a bulk load.

## The data
- No row is written. The `WITH CHECK CHECK` reads every child row to re-validate the key.

## What proving showed
Published to a throwaway copy on this branch.
- **Tried / Did:** over clean child data, `WITH CHECK CHECK` flips the key `is_not_trusted` from 1 to 0
  — validated and trusted.
- **Realized:** over a violating row the same statement fails (`Msg 547`) and the constraint stays
  untrusted — trust cannot be granted over violating data, so the reconcile comes first. The end-state
  proof is `is_not_trusted = 0`.

## After deploy — check
```sql
-- every child points at a real parent, expect 0 rows
SELECT o.Id, o.CustomerId FROM dbo.[Order] o
WHERE NOT EXISTS (SELECT 1 FROM dbo.Customer c WHERE c.Id = o.CustomerId);

-- the key is trusted, expect is_not_trusted = 0
SELECT name, is_disabled, is_not_trusted FROM sys.foreign_keys WHERE name = 'FK_Order_Customer_CustomerId';
```

## How to roll this back
Re-running `ALTER TABLE dbo.[Order] NOCHECK CONSTRAINT FK_Order_Customer_CustomerId;` stops enforcement
again, but a constraint returned to NOCHECK ends untrusted (`is_not_trusted = 1`) and guards nothing.
Backing out a re-trust removes protection rather than restoring a safe prior state; the resting state to
return to is the trusted one. Backing the change out was not exercised.

## Not checked / still open
- Other environments — the ending trust state is proven on a copy only. A `WITH CHECK CHECK` that meets
  violating data in another environment leaves the constraint untrusted (`Msg 547`); re-probe
  `is_not_trusted` after the script runs in each environment before relying on it.
- Application impact — once trusted, an insert or update that points an Order at a missing Customer is
  rejected (error 547); application-side handling is not confirmed here (app owner).
