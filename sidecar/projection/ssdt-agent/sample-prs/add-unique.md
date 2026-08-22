# Customer: no two customers share an Email (Email is optional, so uniqueness is filtered to the filled values)

## Verdict
This PR requires that no two Customers share the same Email. Email is optional, and a plain unique rule
would be refused because two customers have no email yet — a unique index allows only one blank. A
filtered unique index enforces uniqueness among the customers who have an email and allows any number
without one. Confirm no two filled emails collide in each environment before promoting — the query is below.

## Intent
The developer's stated intent for this PBI: stop two Customers from having the same Email, so a
duplicate email becomes impossible — while still allowing a customer to have no email. No work item
supplied — attach one before merge.

## What changes
- `dbo.Customer`: add a filtered unique index `UIX_Customer_Email` on `(Email)` where `Email IS NOT NULL`.

## Before promoting
- Run the duplicate query (below) in each environment and confirm it returns 0 rows — no two filled
  emails match. The set differs per environment.
- If it finds duplicates, reconcile them in a pre-deploy first (merge the records, or correct the
  email) — a data-owner decision; do not guess it.

## The data
- 5 customers. 3 have distinct emails; 2 have none (`NULL`). No two filled emails match.

## How it ships
- One release, applied in place — the index builds over the existing rows. Nothing is written.
- The index is filtered (`WHERE Email IS NOT NULL`) because Email is optional: a plain unique index
  would be refused, because two customers have no email and a unique index allows only one blank.
- A duplicate among the filled emails would block the build the same way, until it is reconciled.

## What proving showed
Published to a throwaway copy on this branch.
- **Tried (plain):** add a plain unique index on `Email`, publish → refused. `Msg 1505`: "the CREATE
  UNIQUE INDEX statement terminated because a duplicate key was found … The duplicate key value is
  (<NULL>)" — the two customers without an email are the collision, because a unique index permits only
  one NULL.
- **Did (filtered):** add the index as `WHERE Email IS NOT NULL`, publish → `Successfully published
  database.` `is_unique = 1`, `has_filter = 1`: the 3 filled emails are enforced, the 2 blanks are allowed.
- **Realized:** a duplicate among the filled emails blocks the build the same way — `Msg 1505` naming
  the duplicate value — so it is a value block (reconcile first), not a row-presence block.

## After deploy — check
```sql
-- no two filled emails match, expect 0 rows
SELECT Email, COUNT(*) FROM dbo.Customer WHERE Email IS NOT NULL GROUP BY Email HAVING COUNT(*) > 1;

-- the filtered unique index exists, expect one row with is_unique = 1, has_filter = 1
SELECT name, is_unique, has_filter FROM sys.indexes WHERE name = 'UIX_Customer_Email';
```

## How to roll this back
Drop the index: `DROP INDEX [UIX_Customer_Email] ON dbo.Customer;` — dropping loses no data. If a
pre-deploy reconcile merged or corrected any row, that is not auto-restored — the originals are
recoverable only from a backup taken before the reconcile, or from a durable record the reconcile
script was written to keep, not from the deploy log. Backing the change out was not exercised.

## Not checked / still open
- Application impact — any insert or update that gives two customers the same filled email is now
  rejected ("duplicate key was found"); application-side handling is not confirmed here (app owner).
- Whether uniqueness should include the blanks — this PR allows many customers with no email. If the
  rule should instead require every customer to have a (unique) email, that is make-mandatory on Email
  first, then a plain unique index — a different, larger change.
- Other environments — the copy's filled emails were distinct; Test, UAT, and Prod may hold duplicates.
  Run the duplicate query before promotion.
- Production scale and timing — building the index at production row counts may run long or block
  writes; a small copy does not show that.
