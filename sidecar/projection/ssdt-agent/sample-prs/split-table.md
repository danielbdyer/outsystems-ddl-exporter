# Customer → CustomerContact: split the contact field into its own table (three releases; the copy is proven hash-total before the drop)

## Verdict
This PR is the first of three releases that split `ContactPhone` out of `Customer` into a new
`CustomerContact` table. This release is additive — it creates the table and copies the phone values;
no data is lost and the old column stays. The old column is dropped only in the third release, after
the copy is proven complete. Confirm the copy hashes match in each environment before that drop.

## Intent
The developer's stated intent for this PBI: pull the contact details out of `Customer` into their own
entity. No work item supplied — attach one before merge.

## What changes
- **Release 1 (this PR):** `dbo.CustomerContact` — a new `CREATE TABLE` (`Id` IDENTITY, `CustomerId`,
  `Phone`); a post-deploy script copies `ContactPhone` keyed by `Customer.Id`; the application begins
  dual-writing into the new table.
- Release 2 repoints reads; Release 3 drops `ContactPhone` from `Customer`.

## Before promoting
- After the copy, run the hash query (below) and confirm the source and new-table hashes match — every
  phone value arrived. **Alias both sides to the same column names** (`k`, `v`): `FOR XML RAW` encodes
  the column names into the XML, so mismatched names hash unequal even when every value is identical.
- Confirm the application dual-writes into `CustomerContact` before Release 2 repoints reads.

## The data
- 5 `Customer` rows, each with a populated `ContactPhone` (`+1-206-555-0101 … 0105`). The copy is 1:1
  keyed by `Customer.Id`; no existing row is removed in this release.

## How it ships
- Across three releases; the old and new shapes coexist while readers migrate. R1 (this PR): additive
  `CREATE` + post-deploy copy + dual-write — purely additive, so it publishes clean in one release.
  R2: repoint reads to `CustomerContact`. R3: drop `ContactPhone` from `Customer` — a populated-column
  drop, which ships as the two-release column-drop pattern (`delete-attribute`) and is licensed only
  once the copy is proven hash-total. An empty source would collapse the whole thing to a single
  additive release.

## What proving showed (published to a throwaway copy, this branch)
Published onto a fresh copy (`pg_split`, sqlpackage 170.4.83.3, `BlockOnPossibleDataLoss = True`).
- **Tried:** R1 — the additive `CREATE TABLE [dbo].[CustomerContact]` plus the post-deploy copy —
  published clean (`Creating Table [dbo].[CustomerContact]... Successfully published database.`).
- **Did:** after the copy, `CustomerContact` holds 5 rows and every `Customer` has its contact copy
  (0 missing). The content hash of the moving column matched **only when both sides were aliased to the
  same names** (`Id AS k, ContactPhone AS v` vs `CustomerId AS k, Phone AS v` →
  `51703987…`=`51703987…`). Aliasing each side to its own column names produced two *different* hashes
  over identical data — the `FOR XML RAW` encoding carries the column names, not just the values.
- **Realized:** the proof that licenses R3's drop is the value-hash, and the hash is only meaningful
  when the two projections share attribute names. R3's drop stays blocked by the data-blind
  row-presence guard regardless; the hash clears the reviewer's doubt that every value already arrived,
  not the gate.

## After deploy — check (each environment)
```sql
-- run after the copy, before the Release-3 drop: expect equal hashes. BOTH sides MUST alias to the
-- same column names (k, v) — FOR XML RAW encodes column names, so mismatched names never match.
SELECT
  CONVERT(CHAR(64), HASHBYTES('SHA2_256', CAST((SELECT Id AS k, ContactPhone AS v FROM dbo.Customer
    ORDER BY Id FOR XML RAW) AS VARBINARY(MAX))), 2) AS source_hash,
  CONVERT(CHAR(64), HASHBYTES('SHA2_256', CAST((SELECT CustomerId AS k, Phone AS v FROM dbo.CustomerContact
    ORDER BY CustomerId FOR XML RAW) AS VARBINARY(MAX))), 2) AS newtable_hash;

-- expect 0 rows: every Customer has its contact copy
SELECT c.Id FROM dbo.Customer c LEFT JOIN dbo.CustomerContact a ON a.CustomerId = c.Id WHERE a.CustomerId IS NULL;
```

## How to roll this back
Before the Release-3 drop, backing out is lossless: `ContactPhone` still lives in `Customer`, so
dropping `CustomerContact` and repointing reads back leaves `Customer` whole. The Release-3 drop is not
auto-reversible — once the old column is dropped it is gone from `Customer`; recovery means re-adding it
and copying back from `CustomerContact` (proven hash-equal before the drop). Keep the source column
recoverable until the drop is confirmed durable. Backing the change out was not exercised.

## Not checked / still open
- Application impact — the application must dual-write into `CustomerContact` during Release 1 and read
  it after cutover; that every reader and writer is repointed off `ContactPhone` before it drops is not
  confirmed here (app owner).
- Other environments — the copy's completeness is proven on a copy of Dev only; QA, UAT, and Prod hold
  their own rows — run the hash and orphan queries before the drop in each.
- Production scale — the copy and drop are exercised at seed scale (5 rows) only; blocking and duration
  at large row counts are not shown by the small copy.
