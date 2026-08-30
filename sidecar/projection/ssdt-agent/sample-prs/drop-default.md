# Product.LegacyCode: stop defaulting new rows (drop DF_Product_LegacyCode)

## Verdict
This change removes the default `N'LEGACY'` from `dbo.Product.LegacyCode`, so a new product row
must supply the value explicitly; the column stays `NOT NULL`. It ships as a single in-place
schema change and never blocks. Confirm every insert path supplies `LegacyCode` before
promoting — the column is `NOT NULL`, so an insert that omitted it fails from the moment the
default is gone. No work item supplied — attach one before merge.

## Intent
The developer's stated intent: new products should not receive the placeholder `LEGACY` code
automatically; the loader that creates products supplies a real code and the placeholder hides
mistakes. No work item supplied — attach one before merge.

## What changes
- `dbo.Product.LegacyCode`: the named default `DF_Product_LegacyCode DEFAULT (N'LEGACY')` is
  removed from the CREATE. The column definition is otherwise unchanged
  (`NVARCHAR(40) NOT NULL`).

## Before promoting
- Check with the application owner that every code path inserting a Product supplies
  `LegacyCode`. After this lands, an insert that omits it fails with `Msg 515`.
- Confirm the post-deployment seed supplies `LegacyCode` for every row it inserts. On the
  copy it does; environment-specific scripts are not covered here.

## The data
- `dbo.Product` holds 5 rows. Existing values are untouched by the change — a default fills
  only new rows at insert time. No existing row carries the placeholder value `N'LEGACY'`.

## How it ships
- Ships as a single schema change, applied in place. No data is read or written. The generated
  script is one statement:
  `ALTER TABLE [dbo].[Product] DROP CONSTRAINT [DF_Product_LegacyCode];`

## What proving showed
Published to a throwaway copy on this branch (sqlpackage 170.5.76).
- **Tried:** remove the default from the CREATE, build, Strict publish → published. The delta
  contains the single `DROP CONSTRAINT` statement and nothing else for this table.
- **Realized:** the publish cannot surface the real risk. The deployment engine validates
  nothing for a default drop; the consequence lives in the next insert that omits the column,
  which fails at runtime, not at deploy.

## After deploy — check
```sql
-- expect 0 rows: the default no longer exists
SELECT name FROM sys.default_constraints WHERE name = 'DF_Product_LegacyCode';

-- expect no error: an insert that supplies LegacyCode still works (application smoke check)
```

## How to roll this back
Re-adding the default reverses the change in place, touching no data:
`ALTER TABLE dbo.Product ADD CONSTRAINT DF_Product_LegacyCode DEFAULT (N'LEGACY') FOR LegacyCode;`
Rows inserted while the default was absent keep the values they were given; nothing is
backfilled either way.

## Not checked / still open
- Application insert paths. Whether every path that creates a Product supplies `LegacyCode` is
  not confirmed here; the application owner confirms it before promotion.
- Environment-specific scripts. Any load or fix-up script outside this project that relied on
  the default is not checked here.
- Other environments. QA, UAT, and Prod were not published here; run the check query in each
  before promotion.
