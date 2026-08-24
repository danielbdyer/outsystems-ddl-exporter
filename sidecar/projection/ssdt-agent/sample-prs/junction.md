# CustomerProduct: add a many-to-many bridge (additive — the shape blocks orphan and duplicate pairs)

## Verdict
This PR adds a `dbo.CustomerProduct` bridge table linking `dbo.Customer` and `dbo.Product`
many-to-many, with a composite primary key over the two foreign keys. It is additive and reads or
writes no existing data, so it applies clean in place. Confirm the two parents `dbo.Customer` and
`dbo.Product` are present in each environment before promoting; if the bridge ships with seed pairs,
confirm every pair has a real row on both sides.

## Intent
The developer's stated intent for this PBI: model a many-to-many relationship where a Customer can
have many Products and a Product many Customers, through a bridge entity. No work item supplied —
attach one before merge.

## What changes
- `dbo.CustomerProduct`: new bridge table with `CustomerId` and `ProductId`, a composite primary key
  `PK_CustomerProduct (CustomerId, ProductId)`, a foreign key `FK_CustomerProduct_Customer` to
  `dbo.Customer(Id)`, and a foreign key `FK_CustomerProduct_Product` to `dbo.Product(Id)`.

## Before promoting
- Confirm `dbo.Customer` and `dbo.Product` both exist in the project for each environment — each
  foreign key needs its parent present, or the build fails before deploy.
- If the bridge is seeded with initial pairs, run the two orphan queries (below) and confirm 0 rows
  from each — every seeded pair must have a real Customer and a real Product, or the publish is
  blocked.

## The data
- No existing data is touched. The table is created empty; there are no pairs for the deploy to be
  conservative about.
- The composite primary key over `(CustomerId, ProductId)` forbids the same pair twice; the two
  foreign keys forbid a pair pointing at a Customer or Product that does not exist.

## How it ships
- One release, applied in place. DacFx emits `CREATE TABLE [dbo].[CustomerProduct]`. The table is
  created empty, so both foreign keys are validated with no rows to check and land trusted
  (`is_not_trusted = 0`), and the composite primary key is built with no rows to check.
- If the bridge is seeded with pairs referencing a missing parent, the foreign-key validation blocks
  the publish and the change becomes an orphan reconcile (see create-fk-orphan.md), which modifies
  existing data and ships as a scripted change.

## What proving showed
Published to a throwaway copy on this branch.
- **Tried:** add `Modules/CustomerProduct.sql` with both parents present and no seed pairs, publish
  with the data-loss gate on → `Creating Table [dbo].[CustomerProduct]…` then `Successfully published
  database.`
- **Did:** read the end state — both `FK_CustomerProduct_Customer` and `FK_CustomerProduct_Product`
  landed with `is_not_trusted = 0`, and `PK_CustomerProduct` is a primary key over 2 key columns.
- **Realized:** the shape carries the whole guarantee. Inserting the pair `(1, 1)` twice was rejected:
  `Msg 2627 … Violation of PRIMARY KEY constraint 'PK_CustomerProduct'. Cannot insert duplicate key
  in object 'dbo.CustomerProduct'. The duplicate key value is (1, 1).` Inserting a pair with a missing
  Product `(1, 999)` was rejected: `Msg 547 … The INSERT statement conflicted with the FOREIGN KEY
  constraint "FK_CustomerProduct_Product". The conflict occurred in … table "dbo.Product", column 'Id'.`

## After deploy — check
```sql
-- every pair points at a real parent on both sides, expect 0 rows from each
SELECT b.CustomerId FROM dbo.CustomerProduct b
LEFT JOIN dbo.Customer c ON c.Id = b.CustomerId WHERE c.Id IS NULL;
SELECT b.ProductId FROM dbo.CustomerProduct b
LEFT JOIN dbo.Product p ON p.Id = b.ProductId WHERE p.Id IS NULL;

-- no duplicate pair exists — the composite primary key forbids it, expect 0 rows
SELECT b.CustomerId, b.ProductId, COUNT(*) FROM dbo.CustomerProduct b
GROUP BY b.CustomerId, b.ProductId HAVING COUNT(*) > 1;
```

## How to roll this back
Remove `Modules/CustomerProduct.sql` from the project and republish; DacFx emits `DROP TABLE
[dbo].[CustomerProduct]` under a drop-enabled posture, or the drop is an explicit scripted step under
the production pipeline (see delete-entity.md). The drop is lossless only while the bridge is
unwritten; once the application writes pairs, dropping the table discards them.

## Not checked / still open
- Application impact — a new bridge nothing yet reads or writes does not change existing behaviour;
  any application code that writes pairs is not exercised on the copy. Once the table is live an
  inserted pair pointing at a missing parent is rejected (`Msg 547`) and a duplicate pair is rejected
  (`Msg 2627`) — the app owner confirms the write paths handle both.
- Other environments — the parents were confirmed present in this project only; if the bridge ships
  with seed pairs, Test, UAT, and Prod may hold parent rows the copy cannot see. Run the orphan
  queries before promotion.
- Production scale — at large row counts in either parent the foreign-key validation's duration and
  locking are not shown by the small copy.
- Reversibility — the forward create is proven; once pairs are written, dropping the bridge is lossy.
