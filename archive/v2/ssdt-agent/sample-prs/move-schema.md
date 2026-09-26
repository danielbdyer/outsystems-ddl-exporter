# Category → archive.Category: move the table to another schema (all 3 rows and the identity are kept)

## Verdict
This PR moves `dbo.Category` to the `archive` schema, keeping all 3 rows and the table's identity,
through `ALTER SCHEMA archive TRANSFER dbo.Category`. Confirm every fully-qualified `dbo.Category`
reference — in views, procedures, synonyms, and application code — is repointed to `archive.Category`
before promoting, because the two-part name is how callers reach the table and each one breaks when
the schema changes.

## Intent
The developer's stated intent for this PBI: move the `Category` entity under the `archive` schema so
it is grouped with retired reference data, without losing its rows. No work item supplied — attach
one before merge.

## What changes
- `dbo.Category` → `archive.Category`: the table moves to the `archive` schema through a scripted
  `ALTER SCHEMA archive TRANSFER dbo.Category`, which preserves the table's object_id and every row.

## Before promoting
- Confirm the `archive` schema exists in each environment (create it in the same release if not).
- Confirm every fully-qualified reference to `dbo.Category` — views, procedures, synonyms, reports,
  ETL, application code — is repointed to `archive.Category`. The old two-part name stops resolving
  the moment the move lands.
- Script the delta and confirm the move is `ALTER SCHEMA … TRANSFER` (or `sp_rename` with the
  refactorlog), not `DROP TABLE` + `CREATE TABLE`. A drop-and-recreate in the delta is the data-loss
  signal — stop and add the identity mapping first.

## The data
- 3 rows in `dbo.Category`, object_id `933578364`. The transfer keeps every row and keeps the
  object_id, so the table under the new schema is the same table.

## How it ships
- One release, applied in place. `ALTER SCHEMA archive TRANSFER dbo.Category` is a metadata operation:
  the object_id and every row are preserved, and no data is read or rewritten. The data-loss gate is
  not engaged, because no data is moved.
- A two-part name is only an address, so a header edit that changes the schema with no identity
  mapping fails the same way a rename with no refactorlog entry does: DacFx sees two different
  addresses. Under the production posture (drops off) it creates an empty table at the new address and
  strands the populated original at the old one; under a drop-enabled posture it drops and recreates,
  and the rows are lost. Ship the scripted transfer or the refactorlog entry, not the bare header edit.

## What proving showed
Published to a throwaway copy on this branch.
- **Tried:** publish the full project to establish `dbo.Category` with 3 rows at object_id `933578364`.
- **Did:** run `CREATE SCHEMA archive` then `ALTER SCHEMA archive TRANSFER dbo.Category` on the copy.
- **Realized:** `archive.Category` holds the same 3 rows and the same object_id `933578364`, and
  `dbo.Category` no longer exists — the table moved rather than being dropped and rebuilt. The
  identity mapping is what a schema move turns on: with it the rows and object_id come through; without
  it a header edit does a drop-and-recreate, the same behaviour a rename with no refactorlog entry
  showed on this branch.

## After deploy — check
```sql
-- exactly one row, under the archive schema, with the full row count, expect one row
SELECT SCHEMA_NAME(t.schema_id) AS schema_name, t.name, SUM(p.rows) AS row_count, t.object_id
FROM sys.tables t
JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
WHERE t.name = 'Category'
GROUP BY SCHEMA_NAME(t.schema_id), t.name, t.object_id;

-- the old address no longer resolves, expect NULL
SELECT OBJECT_ID('dbo.Category', 'U') AS old_address_object_id;
```

## How to roll this back
The move reverses without data loss: transfer the table back with `ALTER SCHEMA dbo TRANSFER
archive.Category`, which preserves the rows and object_id, and repoint every reference back to
`dbo.Category`. The reference edits are not auto-reversed. A move that ever went through as a
drop-and-recreate has already lost the rows and cannot be rolled back from the schema — the only way
back there is a database backup.

## Not checked / still open
- Application impact — every fully-qualified `dbo.Category` reference breaks when the schema changes;
  that all of them were found and repointed is not confirmed on a copy — the app owner owns closing
  this before promotion.
- Other environments — the move was proven on a copy of Dev; that the `archive` schema exists and the
  reference set is the same in QA, UAT, and Prod is not confirmed here. Run the verification query
  before each promotion.
- Production scale and timing — the transfer is a metadata operation, but any dependent rebuild is
  exercised at seed scale only; duration and locking at production row counts are not shown here.
- Reversibility — only the forward move was exercised; transferring the table back and repointing
  every reference was not.
