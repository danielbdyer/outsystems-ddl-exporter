# Category: turn on Auto Number for Id (a table rebuild across releases; every key preserved by IDENTITY_INSERT)

## Verdict
This PR turns on IDENTITY (Auto Number) for `Category.Id`. IDENTITY cannot be `ALTER`ed on — SSDT
rebuilds the whole table (a shadow copy under `SET IDENTITY_INSERT` that preserves every key, a reseed,
and every incoming foreign key dropped and recreated). It stages across releases because the foreign
keys come off and go back on around the rebuild. Confirm every Id is preserved and every foreign key
resolves in each environment before promoting.

## Intent
The developer's stated intent for this PBI: make `Category.Id` database-generated (Auto Number). No work
item supplied — attach one before merge.

## What changes
- `dbo.Category`: rebuild the table with `Id` as `IDENTITY(1,1)`; drop and recreate the incoming foreign
  keys (from `Order` and `OrderLine`) around the rebuild.

## Before promoting
- Confirm the generated delta is a shadow-table rebuild with `SET IDENTITY_INSERT` — not a no-op. If
  SSDT does not show the rebuild, the IDENTITY edit did not register.
- Confirm every existing Id is preserved and every incoming foreign key resolves. Confirm no application
  code inserts a `Category` with an Id it sets itself (from now on the database owns the Id).

## How it ships
- Across multiple releases on a populated table with incoming foreign keys: the foreign keys are
  dropped, the table is rebuilt (a shadow table, a `SET IDENTITY_INSERT` copy that preserves every key,
  a reseed to `MAX(Id)+1`), then the foreign keys are recreated around it — so the running application
  keeps working. On a populated table with no incoming foreign keys it ships as one release; empty, a
  single schema change.

## The data
- Every existing `Category.Id` is preserved by `SET IDENTITY_INSERT`; the counter reseeds past `MAX(Id)`.

## What proving showed
Published to a throwaway copy on this branch.
- **Tried / Did:** the generated delta is a **shadow-table rebuild with `SET IDENTITY_INSERT`**
  (confirmed — not a no-op). After the rebuild, every existing Id is preserved and every incoming
  foreign key still resolves.
- **Realized:** without the `IDENTITY_INSERT` step the keys would be re-minted and every foreign key
  would point at the wrong rows — the most dangerous "one-line edit" in the catalog. The size of the
  `.sql` edit says nothing about the size of the deploy.

## After deploy — check
```sql
-- expect is_identity = 1: the Id column is now database-generated (for a removal, expect 0)
SELECT name, is_identity FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Category') AND name = 'Id';

-- expect current_seed >= max_id: the next generated Id cannot collide with an existing row
SELECT IDENT_CURRENT('dbo.Category') AS current_seed, MAX(Id) AS max_id FROM dbo.Category;

-- expect 0 rows for each incoming foreign key: every child still points at a real parent
SELECT o.Id FROM dbo.[Order] o LEFT JOIN dbo.Category p ON o.CategoryId = p.Id WHERE p.Id IS NULL;
```

## How to roll this back
Backing this out is itself a table rebuild in the other direction — removing the IDENTITY property with
the same shadow-table copy under `SET IDENTITY_INSERT`, and the same drop-and-recreate of every incoming
foreign key. It is not a single statement and not auto-reversible; the forward rebuild preserves every
key value, so there is no data-value change to undo — only the physical rebuild to repeat. Backing the
change out was not exercised.

## Not checked / still open
- Application impact — after Auto Number is on, the database owns the Id: any insert that supplies an
  explicit Id fails unless it wraps the insert in `SET IDENTITY_INSERT`. Application-side Id handling is
  not confirmed here (app owner).
- Other environments — the rebuild and key preservation are proven on a copy of Dev only; Test, UAT, and
  Prod hold row counts and incoming foreign-key data this copy cannot see. Run the verification queries
  before promotion.
- Production scale — the data copy is the expensive part of the rebuild; at production row counts it may
  block writes or run long. Schedule a window.
