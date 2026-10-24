# Order: change the StatusText default to 'Shipped' for new rows (never re-stamps existing rows)

## Verdict
This PR changes the DEFAULT on `Order.StatusText` from `'Pending'` to `'Shipped'` (SSDT does a
DROP-then-ADD of the named constraint). Existing rows keep the value they were written with — a default
governs only future inserts. Confirm existing rows are meant to stay as-is before promoting.

## Intent
The developer's stated intent for this PBI: new Orders should default `StatusText` to `'Shipped'` rather
than `'Pending'`. No work item supplied — attach one before merge.

## What changes
- `dbo.[Order]`: change `DF_Order_StatusText` from `'Pending'` to `'Shipped'` — SSDT emits `DROP
  CONSTRAINT` then `ADD CONSTRAINT` with the new value.

## Before promoting
- Confirm no retro re-stamp is wanted: existing rows keep their current `StatusText`. If the new value
  must apply to old rows too, that is a separate, proven backfill.

## The data
- No row changes. Old rows keep the value they were written with under the previous default.

## How it ships
- One release, applied in place. SSDT does a `DROP`-then-`ADD` of the named constraint (a brief
  no-default window inside the deploy transaction). No existing row value changes; no table rebuild.

## What proving showed
Published to a throwaway copy on this branch.
- **Tried / Did:** publish the modify → the delta is `DROP CONSTRAINT DF_Order_StatusText` then `ADD
  CONSTRAINT DF_Order_StatusText DEFAULT 'Shipped'`, with no UPDATE of existing rows.
- **Realized:** a DEFAULT fills only new rows and never backfills — changing or dropping it never
  reaches back to rows already written, which keep their values.

## After deploy — check
```sql
-- modify: expect one row — DF_Order_StatusText exists carrying the new default definition
SELECT dc.name, c.name AS column_name, dc.definition
FROM sys.default_constraints dc
JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
WHERE dc.name = 'DF_Order_StatusText';
```

## How to roll this back
Lossless: no existing rows change either way. Backing out re-creates `DF_Order_StatusText` with its
previous definition (`'Pending'`); record the prior definition so the restore is exact. Backing the
change out was not exercised.

## Not checked / still open
- Application impact — inserts that omit this column now receive the new default; whether any code
  relies on the old behaviour is not confirmed here (app owner).
- Other environments — a default created unnamed or by an ad-hoc script may differ per environment; the
  copy cannot see it. Run the verification query before promotion.
- Retro re-stamp — existing rows are deliberately left as written; if the new value must apply to old
  rows, that is a separate, proven backfill and is not part of this change.
