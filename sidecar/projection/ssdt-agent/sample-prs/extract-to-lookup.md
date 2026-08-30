# Order.StatusText → Status FK: promote free text to a lookup (across releases; total mapping proven before the drop)

## Verdict
This PR promotes the free-text `Order.StatusText` into a proper `Status` lookup behind a foreign key.
It stages across releases: create the lookup, seed the distinct values, add the FK column, backfill,
then drop the old text column — the old and new shapes coexist while readers migrate. It hinges on a
total mapping: every existing `StatusText` must resolve to a seeded `Status` row, or a value silently
becomes NULL. Confirm zero unmapped in each environment before the drop.

## Intent
The developer's stated intent for this PBI: replace the free-text Status column with a `Status` lookup
and a foreign key, so a typo becomes impossible. No work item supplied — attach one before merge.

## What changes
- `dbo.Status` (lookup) holds the distinct values; `Order.StatusId` is the foreign key backfilled by
  joining `StatusText → Status.Code`; `Order.StatusText` is dropped in the last release. On this estate
  the lookup and the FK column already exist and the FK is backfill-consistent with the text — the
  remaining change this program licenses is the drop of the free-text column.

## Before promoting
- Run the total-mapping query (below) in each environment and confirm 0 rows — every existing
  `StatusText` maps to a seeded `Status`. The old-column drop is blocked until this holds. If any value
  is unmapped, reconcile it before the backfill so nothing maps to NULL.

## The data
- 4 `Order` rows. The distinct `StatusText` values (`Pending`, `Shipped`, `Cancelled`) each map to a
  seeded `Status` (`Pending → 1`, `Shipped → 2`, `Cancelled → 3`); `Pending` covers 2 orders, the
  others 1 each. Every order's existing `StatusId` already equals its mapped value.

## How it ships
- Across releases: create the lookup → seed → add the FK column → backfill → drop the old column. The
  old and new shapes coexist while readers migrate. The drop is a populated-column drop gated on
  row-presence and licensed only by the total-mapping proof (zero unmapped values); it ships as the
  two-release column-drop pattern (`delete-attribute`).

## What proving showed (published to a throwaway copy, this branch)
Proven on copies this branch (`pg_base` positive; `pg_move` negative; sqlpackage 170.4.83.3).
- **Tried:** the total-mapping query — `DISTINCT StatusText NOT IN (SELECT Code FROM Status)` —
  returned **0 rows** on `pg_base`: every existing `StatusText` maps to a seeded `Status`, so nothing
  silently becomes NULL.
- **Did:** the backfill-consistency check — `Order` rows where the existing `StatusId` differs from the
  `StatusText → Status.Code` mapping — returned **0 rows**: the foreign key already agrees with the free
  text, so the text is redundant and safe to drop once the mapping is proven total.
- **Realized:** injecting an order with `StatusText = 'Backordered'` (`pg_move`) made the total-mapping
  query return `Backordered` — a value with no `Status.Code`. In one publish that value silently becomes
  NULL or blocks the FK from validating. The mapping must be proven total before the drop, and the drop
  itself stays blocked by the data-blind row-presence guard regardless — the mapping proof clears the
  reviewer's doubt, not the gate.

## After deploy — check (each environment)
```sql
-- expect 0 rows: every source value maps to a seeded lookup row (the mapping is total)
SELECT DISTINCT StatusText FROM dbo.[Order] WHERE StatusText NOT IN (SELECT Code FROM dbo.Status);

-- expect 0 rows: the backfill left no order without a Status id
SELECT Id FROM dbo.[Order] WHERE StatusId IS NULL;

-- expect 0 rows: the FK column agrees with the free text for every row (backfill is consistent)
SELECT o.Id FROM dbo.[Order] o JOIN dbo.Status s ON s.Code = o.StatusText WHERE o.StatusId <> s.Id;
```

## How to roll this back
The final release drops the old free-text column. Because the mapping was proven total before the drop,
the original text is reconstructable by joining the FK column back to `Status.Code`; re-adding the
column and backfilling from that join restores it. The lookup and the FK drop cleanly. The column drop
is not auto-reversed; any value reconciled rather than directly mapped is restored from the reconcile's
recorded originals. Backing the change out was not exercised.

## Not checked / still open
- Application impact — code that reads or writes the old free-text column directly, rather than through
  the FK, breaks once the column is dropped; that every reader and writer has moved to the FK is not
  confirmed here (app owner).
- Other environments — the distinct source values were enumerated on a copy of Dev only; QA, UAT, and
  Prod may hold values never seeded into the lookup. Run the total-mapping query before promotion in each.
- Production scale — the seed, backfill, and drop are exercised at seed scale (4 orders) only.
