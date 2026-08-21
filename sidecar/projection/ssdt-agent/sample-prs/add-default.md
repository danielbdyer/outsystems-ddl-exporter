# Order: default StatusText to 'Pending' for new rows (fills only new rows; never backfills existing)

## Verdict
This PR adds a named DEFAULT on `Order.StatusText` so a new row inserted without a value gets
`'Pending'`. Existing rows are not touched — a default fills only new rows. Confirm whether existing
rows should also be backfilled (a separate step) before promoting.

## Intent
The developer's stated intent for this PBI: new Orders should default `StatusText` to `'Pending'` when
no value is supplied. No work item supplied — attach one before merge.

## What changes
- `dbo.[Order]`: add a named DEFAULT constraint `DF_Order_StatusText` = `'Pending'` for `StatusText`.

## Before promoting
- Confirm existing rows are meant to stay as they are — the default does not backfill them. If existing
  blanks should be filled too, that is a separate backfill step (a post-deploy idempotent UPDATE).
- Name the constraint `DF_Order_StatusText`; an auto-named default differs per environment and makes
  diffing fragile.

## The data
- Existing `StatusText` values are unchanged. The default applies only to rows inserted from here on.

## How it ships
- One release, applied in place. SSDT emits `ALTER TABLE … ADD CONSTRAINT DF_Order_StatusText DEFAULT …`;
  it affects future inserts only and touches no existing row.

## What proving showed
Published to a throwaway copy on this branch.
- **Tried / Did:** publish → the delta is a clean `ADD CONSTRAINT` with no UPDATE of existing rows.
- **Realized (F10):** after adding a default, an existing `NULL` stayed `NULL` and an existing value was
  unchanged; only a fresh insert that omitted the column received the default. A default never
  backfills. (The mirror case — a default riding a *new* `NOT NULL` column — does stamp every existing
  row as the column lands; that is `add-mandatory`, a different op.)

## After deploy — check
```sql
-- expect one row: the named default constraint exists on the column
SELECT dc.name, c.name AS column_name, dc.definition
FROM sys.default_constraints dc
JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
WHERE dc.name = 'DF_Order_StatusText';
```

## How to roll this back
Lossless: `ALTER TABLE dbo.[Order] DROP CONSTRAINT DF_Order_StatusText;`. No existing row was written,
so nothing is restored. Backing the change out was not exercised.

## Not checked / still open
- Application impact — inserts that omit this column now receive `'Pending'` instead of NULL; whether any
  code relies on that distinction is not confirmed here (app owner).
- Other environments — an existing unnamed default (`DF__Order__StatusText__<hash>`) on this column in
  Test/UAT/Prod must be dropped before this one lands; the copy cannot see it. Run the verification
  query before promotion.
