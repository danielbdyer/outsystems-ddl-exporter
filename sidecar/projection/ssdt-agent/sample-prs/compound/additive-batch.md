# Returns feature: two new entities, a seed, three references, and a required flag — one release

**Atoms in this release:** `create-static-seed` (ReturnReason + its seed) · `create-entity`
(Return) · `create-fk-clean` ×2 (Return → OrderLine, Return → ReturnReason) · `create-fk-clean`
(CustomerAddress → Customer, populated child) · `add-mandatory` + `add-default`
(Order.ReturnsAllowed, `BIT NOT NULL DEFAULT (1)` on a populated table).

## Verdict
This change stands up the Returns feature and tightens nothing that exists: every atom is
additive, so the whole batch ships as ONE release, in one publish, and any team member can
review it. The engine orders the objects itself — no hand-sequencing inside a release. Confirm
only the seed values for ReturnReason with the product owner. No work item supplied — attach
one before merge.

## Intent
The developer's stated intent: customers can return order lines with a reason, returns need
reporting, existing orders allow returns by default, and the customer-address link gets the
foreign key it always implied.

## What changes
- New `dbo.ReturnReason` (explicit-Id lookup) with an idempotent seed of 3 rows (`Damaged`,
  `WrongItem`, `NoLongerNeeded`) in the post-deployment seed.
- New `dbo.[Return]` with foreign keys to `dbo.OrderLine` and `dbo.ReturnReason`.
- `dbo.[Order]`: new column `ReturnsAllowed BIT NOT NULL` with default `(1)`.
- `dbo.CustomerAddress`: new foreign key `FK_CustomerAddress_Customer_CustomerId` over the
  existing populated rows.

## Before promoting
- Confirm the three ReturnReason values with the product owner; the seed is the record of them.
- Nothing else: no atom reads or rewrites existing data, and no application path changes.

## The data
- `dbo.[Order]` holds 4 rows — populated, which is why the new NOT NULL column carries a
  default. `dbo.CustomerAddress` holds 5 rows, every one pointing at a real Customer, which is
  why its foreign key can land in the same release. The new tables start empty.

## How it ships
- Ships as ONE release: a single publish carrying the whole batch. The generated script creates
  `[dbo].[Return]` and `[dbo].[ReturnReason]` before the foreign keys that reference them —
  DacFx orders object creation inside a release; only cross-release ordering is ever planned by
  hand (`../../skills/decompose/SKILL.md` step 3).

## What proving showed
Published to a throwaway copy on this branch (sqlpackage 170.5.76), as one delta.
- **Tried:** build the whole batch, Strict publish → published, first attempt. Nothing blocked:
  every atom is additive, so the release inherits no guard.
- The delta creates both tables, then adds the foreign keys as DacFx's own pair — `WITH NOCHECK
  ADD` followed by `WITH CHECK CHECK` — so each key re-validates and ends trusted.
- After the publish: all 4 Order rows carry `ReturnsAllowed = 1` (the default stamped existing
  rows as the column landed); `dbo.ReturnReason` holds its 3 seeded rows; every foreign key in
  the database reads `is_not_trusted = 0`, including `FK_CustomerAddress_Customer_CustomerId`
  over the 5 populated child rows.

## After deploy — check
```sql
-- expect 4: every existing order allows returns
SELECT COUNT(*) FROM dbo.[Order] WHERE ReturnsAllowed = 1;

-- expect 3: the lookup seeded
SELECT COUNT(*) FROM dbo.ReturnReason;

-- expect 0 rows: every foreign key is trusted
SELECT name FROM sys.foreign_keys WHERE is_not_trusted = 1;
```

## How to roll this back
Reversing the batch is the mirror program, in the reverse order: drop the two foreign keys on
`Return`, drop `Return`, drop `ReturnReason` and its seed block, drop
`FK_CustomerAddress_Customer_CustomerId`, and drop `Order.ReturnsAllowed` — the last is a
column drop on a populated table, which the locked gate turns into its own two-release
(`../delete-attribute.md`). An additive batch is cheap to ship and not symmetric to unwind.

## Not checked / still open
- The ReturnReason values are the product owner's call; the seed records the current answer.
- Application impact — no existing path changes, and no new path is exercised here.
- Other environments — proven on a disposable copy of Dev only; the batch carries no
  data-dependent step, so the same delta is expected everywhere. Run the checks after each
  promotion.
