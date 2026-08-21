# Order.StatusText → Status FK: promote free text to a lookup (across releases; total mapping proven before the drop)

## Verdict
This PR promotes the free-text `Order.StatusText` into a proper `Status` lookup behind a foreign key. It
stages across releases: create the lookup, seed the distinct values, add the FK column, backfill, then
drop the old text column — the old and new shapes coexist while readers migrate. It hinges on a total
mapping: every existing `StatusText` must have a seeded `Status` row, or a value silently becomes NULL.
Confirm zero unmapped in each environment before the drop.

## Intent
The developer's stated intent for this PBI: replace the free-text Status column with a `Status` lookup
and a foreign key, so a typo becomes impossible. No work item supplied — attach one before merge.

## What changes
- Create `dbo.Status` (lookup) + seed the distinct existing values; add `Order.StatusId` foreign key;
  backfill `StatusId` by joining `StatusText` → `Status.Code`; drop `Order.StatusText` in the last release.

## Before promoting
- Run the total-mapping query (below) in each environment and confirm 0 rows — every existing
  `StatusText` maps to a seeded `Status`. The old-column drop is blocked until this holds. If any value
  is unmapped, reconcile it before the backfill so nothing maps to NULL.

## How it ships
- Across releases: create the lookup → seed → add the FK column → backfill → drop the old column. The
  old and new shapes coexist while readers migrate. The drop is `BlockOnPossibleDataLoss`-gated and
  licensed only by the total-mapping proof (zero unmapped values).

## The data
- The distinct `StatusText` values (`Pending`, `Shipped`, `Cancelled`) seed the lookup by explicit id;
  each Order is backfilled to its `Status` id.

## What proving showed
Published to a throwaway copy on this branch.
- **Tried / Did:** before the old column is dropped, every existing `StatusText` maps to a seeded
  `Status` row — the total-mapping query returns 0 unmapped — so nothing silently becomes NULL.
- **Realized:** a scratch seed with an unmapped value fires the total-mapping negative; doing this in
  one publish would silently turn that value into NULL (or block the FK from validating). The mapping
  is proven total before the drop.

## After deploy — check
```sql
-- expect 0 rows: every source value maps to a seeded lookup row (the mapping is total)
SELECT DISTINCT StatusText FROM dbo.[Order] WHERE StatusText NOT IN (SELECT Code FROM dbo.Status);

-- expect 0 rows: the backfill left no order without a Status id
SELECT Id FROM dbo.[Order] WHERE StatusId IS NULL;
```

## How to roll this back
The final release drops the old free-text column. Because the mapping was proven total before the drop,
the original text is reconstructable by joining the new FK column back to `Status.Code`; re-adding the
column and backfilling from that join restores it. The lookup and the FK drop cleanly. The column drop
is not auto-reversed; any value reconciled rather than directly mapped is restored from the reconcile's
recorded originals. Backing the change out was not exercised.

## Not checked / still open
- Application impact — code that reads or writes the old free-text column directly, rather than through
  the new FK, breaks once the column is dropped; that every reader and writer has moved to the FK is not
  confirmed here (app owner).
- Other environments — the distinct source values were enumerated on a copy of Dev only; Test, UAT, and
  Prod may hold values never seeded into the lookup. Run the total-mapping query before promotion in each.
- Production scale — the seed, backfill, and drop are exercised at seed scale only.
