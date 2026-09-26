# Category: turn on Auto Number for Id (a table rebuild the data-loss gate allows; every id preserved, the seed switches to IDENTITY_INSERT)

## Verdict
This PR turns on IDENTITY (Auto Number) for `Category.Id`. IDENTITY cannot be `ALTER`ed onto an
existing column, so SSDT rebuilds the whole table: a shadow copy under `SET IDENTITY_INSERT` that
preserves every id, then a `DROP`+`sp_rename` swap. The data-loss gate **allows** it — a rebuild
moves rows, it does not drop them. `Category` has no incoming foreign keys, so it ships in one
release. The one edit that rides with it: the seed inserts explicit `Category` ids, which fails
`Msg 544` once the column is IDENTITY unless it is bracketed with `SET IDENTITY_INSERT`. Confirm every
id is preserved and the seed (and any app insert of an explicit id) is bracketed before promoting.

## Intent
The developer's stated intent for this PBI: make `Category.Id` database-generated (Auto Number). No
work item supplied — attach one before merge.

## What changes
- `dbo.Category`: `Id INT NOT NULL` → `Id INT IDENTITY(1,1) NOT NULL`. SSDT realizes this as a
  shadow-table rebuild (IDENTITY cannot be added in place).
- `Data/Seed.sql`: the `Category` MERGE is bracketed with `SET IDENTITY_INSERT dbo.Category ON … OFF`,
  because it inserts explicit ids (1, 2, 3) into what is now an IDENTITY column.

## Before promoting
- Confirm the generated delta is a shadow-table rebuild with `SET IDENTITY_INSERT` (below) — not a
  no-op. If SSDT does not show `Starting rebuilding table [dbo].[Category]`, the IDENTITY edit did not
  register.
- Confirm every existing `Category.Id` is preserved and every `Product.CategoryId` still resolves.
- Confirm the seed and any application code that inserts a `Category` with an explicit id is bracketed
  with `SET IDENTITY_INSERT` — otherwise it fails `Msg 544`. From now on the database owns new ids.

## The data
- 3 `Category` rows (ids 1, 2, 3: Hardware, Software, Service), all preserved by `SET IDENTITY_INSERT`
  during the rebuild; the counter reseeds to `IDENT_CURRENT = 3`, so the next generated id is 4.
- 5 `Product` rows carry `CategoryId` (1, 2, 3, 1, 2) as plain values; the preserved ids keep every
  one resolving.

## How it ships
- One release. On a populated table with **no incoming foreign keys** — which is `Category` here —
  the rebuild moves the rows into the shadow table under `SET IDENTITY_INSERT` and the data-loss gate
  does not block it, so there is nothing to stage. The seed fix ships in the same release. (A table
  *with* incoming foreign keys would additionally drop and recreate them around the rebuild; that is
  not exercised here because nothing references `Category`.)

## What proving showed (published to a throwaway copy, this branch)
Published onto a populated copy (`pg_idsw_before`, sqlpackage 170.4.83.3, `BlockOnPossibleDataLoss = True`).
- **Tried:** publish the IDENTITY edit onto the populated copy. The delta is a **shadow-table rebuild** —
  `CREATE TABLE [dbo].[tmp_ms_xx_Category] ([Id] INT IDENTITY(1,1) …)`, then
  `SET IDENTITY_INSERT [dbo].[tmp_ms_xx_Category] ON; INSERT … SELECT [Id],[Code],[IsActive] FROM
  [dbo].[Category] ORDER BY [Id]; SET IDENTITY_INSERT … OFF;`, then `DROP TABLE [dbo].[Category];` and
  `sp_rename` of the shadow table and its PK. The publish logged `Starting rebuilding table
  [dbo].[Category]...` and the data-loss gate did **not** block the rebuild.
- **Did:** the first attempt failed in the post-deploy seed — `Error SQL72014 … Msg 544, Level 16:
  Cannot insert explicit value for identity column in table 'Category' when IDENTITY_INSERT is set to
  OFF.` Bracketing the `Category` MERGE with `SET IDENTITY_INSERT dbo.Category ON … OFF` cleared it,
  and the publish succeeded.
- **Realized:** after the rebuild `is_identity = 1`, the ids are still `1, 2, 3`,
  `IDENT_CURRENT('dbo.Category') = 3`, every `Product.CategoryId` resolves (0 orphans), and a second
  publish makes no change (no rebuild — the shape already matches). The `Msg 544` is the real trap of
  this "one-line edit": turning on IDENTITY breaks every explicit-id insert, the seed included, until
  it moves to `IDENTITY_INSERT`.

## After deploy — check (each environment)
```sql
-- expect is_identity = 1: the Id column is now database-generated
SELECT name, is_identity FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Category') AND name = 'Id';

-- expect current_seed >= max_id: the next generated id cannot collide with an existing row
SELECT IDENT_CURRENT('dbo.Category') AS current_seed, MAX(Id) AS max_id FROM dbo.Category;

-- expect 0 rows: every Product still points at a real Category (the rebuild preserved the ids)
SELECT p.Id FROM dbo.Product p LEFT JOIN dbo.Category c ON c.Id = p.CategoryId WHERE c.Id IS NULL;
```

## How to roll this back
Backing this out is itself a table rebuild in the other direction — removing the IDENTITY property
with the same shadow-table copy under `SET IDENTITY_INSERT`, and reverting the seed to a plain insert.
It is not a single statement and not auto-reversible; the forward rebuild preserves every id, so there
is no data-value change to undo — only the physical rebuild to repeat. Backing the change out was not
exercised.

## Not checked / still open
- Application impact — after Auto Number is on, the database owns the id: any insert that supplies an
  explicit id fails with `Msg 544` unless it wraps the insert in `SET IDENTITY_INSERT`. Application-side
  id handling beyond the seed is not confirmed here (app owner).
- Incoming foreign keys — `Category` has none, so the drop-and-recreate-FK leg of an identity-swap is
  not exercised by this change. A table with incoming foreign keys would additionally stage those
  around the rebuild.
- Other environments — the rebuild and id preservation are proven on a copy of Dev only; QA, UAT,
  and Prod hold row counts this copy cannot see. Run the verification queries before promotion.
- Production scale — the data copy is the expensive part of the rebuild; at production row counts it
  may block writes or run long. Schedule a window.
