# Category: remove the primary key (drop PK_Category_Id) — and the face that refuses at build

## Verdict
This change removes `PK_Category_Id` from `dbo.Category`. Nothing references Category, so it
ships as a single in-place schema change — and leaves the table a heap with no identity rule:
duplicate Ids become writable and lookups scan. A dev lead approves this. The companion
finding on the record below: the same edit on a referenced table does not reach the engine at
all — the project refuses to build. Confirm the end state actually wanted; a request like this
is usually a key change (drop plus define), not a bare removal. No work item supplied — attach
one before merge.

## Intent
The developer's stated intent: Category is being restaged as an external reference feed and
the incoming feed carries its own identity; the local key gets removed ahead of the re-keying.
No work item supplied — attach one before merge.

## What changes
- `dbo.Category`: the constraint `PK_Category_Id PRIMARY KEY CLUSTERED (Id)` is removed from
  the CREATE. Columns are unchanged; the clustered index goes with the key.

## Before promoting
- Confirm the follow-up key. A table left keyless accepts duplicate Ids from the first insert
  on; if a new key is planned, ship this and the define-key as one reviewed program, not as a
  bare drop.
- Confirm no environment holds a foreign key onto Category that this project does not know
  about — a reference that exists only in a drifted environment fails this publish there.

## The data
- `dbo.Category` holds 3 rows; all survive the change untouched. No foreign key references
  Category in this project, which is the only reason the change ships at all (see the refusal
  below).

## How it ships
- Ships as a single schema change, applied in place. No data is read or written. The generated
  script is one statement:
  `ALTER TABLE [dbo].[Category] DROP CONSTRAINT [PK_Category_Id];`
- The referenced face, proven alongside: with `FK_OrderLine_Order_OrderId` present, removing
  `PK_Order_Id` does not produce a delta at all — the build refuses:
  `Build error SQL71516: The referenced table '[dbo].[Order]' contains no primary or candidate
  keys that match the referencing column list in the foreign key.` The model is the first
  gate; the engine is never reached.

## What proving showed
Published to a throwaway copy on this branch (sqlpackage 170.5.76).
- **Tried (referenced face):** add `FK_OrderLine_Order_OrderId` (published clean, trusted),
  then remove `PK_Order_Id` from the CREATE → the build fails with `SQL71516` naming the
  foreign key's file and line. No dacpac, no delta, no publish.
- **Tried (unreferenced face):** remove `PK_Category_Id` → build clean, Strict publish →
  published. The delta is the single `DROP CONSTRAINT`; the 3 rows are intact;
  `sys.indexes` shows the table as a heap afterward.
- **Realized:** neither face loses a row, and only one of them is stopped — the unreferenced
  drop is a green publish whose entire cost (no identity rule, no clustered organization)
  accrues after deploy.

## After deploy — check
```sql
-- expect 0 rows: the primary key no longer exists
SELECT name FROM sys.key_constraints
WHERE parent_object_id = OBJECT_ID('dbo.Category') AND type = 'PK';

-- expect HEAP: the clustered index went with the key
SELECT type_desc FROM sys.indexes
WHERE object_id = OBJECT_ID('dbo.Category') AND index_id IN (0, 1);
```

## How to roll this back
Re-adding the key reverses the change:
`ALTER TABLE dbo.Category ADD CONSTRAINT PK_Category_Id PRIMARY KEY CLUSTERED (Id);`
The build validates the key over every existing row — a duplicate or NULL Id written while the
key was absent blocks it until reconciled — and rebuilding the clustered index on a large
table needs a window. The drop itself loses no data.

## Not checked / still open
- Writes during the gap. Nothing prevents duplicate Ids while the key is absent; the table's
  owner watches for them until a key returns.
- Read behavior at scale. A heap's performance at production row counts does not show on a
  3-row copy.
- Other environments. QA, UAT, and Prod were not published here; a drifted environment holding
  its own foreign key onto Category fails this publish there. Run both check queries in each
  environment before promotion.
