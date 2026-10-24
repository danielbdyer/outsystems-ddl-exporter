# CustomerPreference: add a new table (additive — no existing data is touched)

## Verdict
This PR adds a brand-new `dbo.CustomerPreference` table with a foreign key to `dbo.Customer`. It is
additive and reads or writes no existing data, so it applies clean in place. Confirm the foreign-key
parent `dbo.Customer` is present in each environment's project before promoting; nothing else needs
to change.

## Intent
The developer's stated intent for this PBI: add a `CustomerPreference` entity to hold per-customer
preference keys and values, linked to the `Customer` entity. No work item supplied — attach one
before merge.

## What changes
- `dbo.CustomerPreference`: new table with `Id` (identity primary key), `CustomerId`, `PrefKey`,
  `PrefValue`, a primary key `PK_CustomerPreference_Id`, and a foreign key
  `FK_CustomerPreference_Customer` to `dbo.Customer(Id)`.

## Before promoting
- Confirm `dbo.Customer` exists in the project for each environment — the foreign key needs its
  parent present, or the build fails before deploy.
- Confirm the new `.sql` file is included by the project glob, so the table actually deploys.

## The data
- No existing data is touched. The table is created empty; there are no rows for the deploy to be
  conservative about.

## How it ships
- One release, applied in place. DacFx emits `CREATE TABLE [dbo].[CustomerPreference]` and nothing
  else touching an existing table. The table is created empty, so the foreign key is validated with
  no rows to check and lands trusted (`is_not_trusted = 0`) — SQL Server can rely on it and the query
  planner can use it.

## What proving showed
Published to a throwaway copy on this branch.
- **Tried:** add `Modules/CustomerPreference.sql`, build, script the delta → the only object change
  is `Creating Table [dbo].[CustomerPreference]…`, followed by the foreign key added `WITH NOCHECK`
  and then `WITH CHECK CHECK` to validate it. No existing table is dropped or altered.
- **Did:** publish with the data-loss gate on → `Creating Table [dbo].[CustomerPreference]…` then
  `Successfully published database.` The table exists as a `USER_TABLE`, and
  `FK_CustomerPreference_Customer` landed with `is_not_trusted = 0`.
- **Realized:** a create carries no data risk — the only thing that can go wrong is a missing
  foreign-key parent or a file the glob misses, both caught at build time. A second publish with no
  change was a clean no-op.

## After deploy — check
```sql
-- the new table exists, expect one row
SELECT name, type_desc FROM sys.tables WHERE object_id = OBJECT_ID('dbo.CustomerPreference');

-- the foreign key is trusted, expect 0
SELECT is_not_trusted FROM sys.foreign_keys WHERE name = 'FK_CustomerPreference_Customer';
```

## How to roll this back
Remove `Modules/CustomerPreference.sql` from the project and republish; DacFx emits `DROP TABLE
[dbo].[CustomerPreference]` — but only under a drop-enabled posture, and the production pipeline runs
with drops off, so the drop is an explicit scripted step (see delete-entity.md). The drop is lossless
only while the table is unwritten; once the application writes rows, dropping the table discards them.

## Not checked / still open
- Application impact — a new table nothing yet reads or writes does not change existing behaviour, but
  any application code intended to read or write it is not exercised on the copy — the app owner
  confirms it.
- Dependencies — the clean publish confirms `dbo.Customer` is present in this project only; that the
  parent and the project glob are the same in every environment is not confirmed here.
- Reversibility — the forward create is proven; once rows are written, dropping the table is lossy.
