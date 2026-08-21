# Customer → CustomerAddress: split off the address fields (three releases; the column drop is gated on the copy)

## Verdict
This PR is the first of three releases that split the address fields out of `Customer` into a new
`CustomerAddress` table. This release is additive — it creates the table, copies the address rows, and
the application dual-writes; no data is lost. The old columns are dropped only in a later release, after
the copy is proven complete. Confirm the copy hashes match in each environment before that drop.

## Intent
The developer's stated intent for this PBI: pull the address fields out of `Customer` into their own
entity. No work item supplied — attach one before merge.

## What changes
- **Release 1 (this PR):** `dbo.CustomerAddress` — a new `CREATE TABLE` with a foreign key back to
  `Customer`; a post-deploy script copies the address columns keyed by `Customer.Id`; the application
  begins dual-writing into the new table.
- Release 2 repoints reads; Release 3 drops the old address columns from `Customer`.

## Before promoting
- After the copy, run the hash query (below) and confirm the source and new-table hashes match — every
  address row arrived. The Release-3 column drop is blocked until this holds in each environment.
- Confirm the application dual-writes into `CustomerAddress` before Release 2 repoints reads.

## How it ships
- Across three releases; the old and new shapes coexist while readers migrate. R1 (this PR): additive
  CREATE + FK + copy + dual-write. R2: repoint reads to `CustomerAddress`. R3: drop the old columns from
  `Customer` — `BlockOnPossibleDataLoss` blocks that drop until the copy is proven hash-equal. An empty
  source would collapse this to a single additive release.

## The data
- The address columns are copied 1:1 keyed by `Customer.Id`. No existing row is removed in this release.

## What proving showed
Published to a throwaway copy on this branch.
- **Tried / Did:** R1 publishes the additive `CREATE` + FK clean; the post-deploy copy runs; the source
  and new-table hashes over the moving columns **match** — every row copied.
- **Realized:** R3's column drop is blocked under Strict until that hash-equality is proven — SSDT
  refuses to drop the old columns while it cannot see the values already arrived. The proof that
  licenses the subtractive phase is the before/after hash, not the schema diff.

## After deploy — check
```sql
-- run after the copy, before the Release-3 drop: expect equal hashes — the moving columns hold the
-- same content in CustomerAddress as in Customer
SELECT
  CONVERT(CHAR(64), HASHBYTES('SHA2_256', CAST((SELECT Id, Line1, City, PostalCode FROM dbo.Customer
    ORDER BY Id FOR XML RAW) AS VARBINARY(MAX))), 2) AS source_hash,
  CONVERT(CHAR(64), HASHBYTES('SHA2_256', CAST((SELECT CustomerId, Line1, City, PostalCode FROM dbo.CustomerAddress
    ORDER BY CustomerId FOR XML RAW) AS VARBINARY(MAX))), 2) AS newtable_hash;

-- expect 0 rows: every Customer has its address copy
SELECT c.Id FROM dbo.Customer c LEFT JOIN dbo.CustomerAddress a ON a.CustomerId = c.Id WHERE a.CustomerId IS NULL;
```

## How to roll this back
Before the Release-3 drop, backing out is lossless: the address columns still live in `Customer`, so
dropping `CustomerAddress` and repointing reads back leaves `Customer` whole. The Release-3 drop is not
auto-reversible — once the old columns are dropped they are gone from `Customer`; recovery means
re-adding them and copying back from `CustomerAddress` (proven hash-equal before the drop). Keep the
address columns recoverable until the drop is confirmed durable.

## Not checked / still open
- Application impact — the application must dual-write into `CustomerAddress` during Release 1 and read
  it after cutover; that every reader and writer is repointed off the old columns before they drop is
  not confirmed here (app owner).
- Other environments — the copy's completeness is proven on a copy of Dev only; Test, UAT, and Prod hold
  their own rows — run the hash and orphan queries before the drop in each.
- Production scale — the copy and drop are exercised at seed scale only; blocking and duration at
  >1M rows are not shown by the small copy.
