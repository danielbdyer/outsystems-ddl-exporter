# Customer.Region → Account: move the attribute (multiple releases; a 1:1 join, proven, or the value is ambiguous)

## Verdict
This PR moves `Region` from `Customer` to `Account` by copy-then-drop, never a rename (a cross-table
rename has no refactorlog entry and loses the data). It hinges on the `Customer → Account` relationship
being 1:1 — if a Customer maps to many Accounts the moved value is ambiguous. Confirm 1:1 in each
environment before promoting; the source column is dropped only after the copy is proven.

## Intent
The developer's stated intent for this PBI: move `Region` from `Customer` to `Account`, where it belongs.
No work item supplied — attach one before merge.

## What changes
- **Release 1 (this PR):** `dbo.Account` — add `Region` (nullable); a post-deploy copy keyed by the 1:1
  `Customer.AccountId` relationship.
- Release 2 repoints readers; Release 3 drops `Region` from `Customer`.

## Before promoting
- Run the 1:1 check (below): a parent with more than one child means the value is ambiguous — **stop**,
  it is a design decision (which Region wins?), not a shipping shape.
- After the copy, confirm the source and destination hashes match. The Release-3 drop is blocked until they do.

## How it ships
- Across multiple releases; the two tables coexist while readers migrate. The source-column drop is
  `BlockOnPossibleDataLoss`-gated until the copy is proven hash-equal. A move crosses tables and has no
  refactorlog identity mapping, so it must be copy-then-drop, never a rename — a rename with no
  refactorlog entry would drop the column and lose its data.

## The data
- `Customer → Account` is 1:1; `Region` is copied onto `Account` keyed by that relationship.

## What proving showed
Published to a throwaway copy on this branch.
- **Tried / Did:** the join is proven 1:1 (each Customer maps to one Account); the copy lands with the
  source and destination hashes matching.
- **Realized:** SSDT keeps the source-column drop blocked until that hash-match is proven. A not-1:1
  relationship is a design fork (which Region wins for an Account with many Customers?), not a change in
  how it ships.

## After deploy — check
```sql
-- expect 0 rows: the relationship is 1:1 (a returned row is a parent with more than one child — stop)
SELECT AccountId, COUNT(*) AS children FROM dbo.Customer GROUP BY AccountId HAVING COUNT(*) > 1;

-- run after the copy, before the source-column drop: expect equal hashes
SELECT
  CONVERT(CHAR(64), HASHBYTES('SHA2_256', CAST((SELECT AccountId, Region FROM dbo.Customer
    WHERE Region IS NOT NULL ORDER BY AccountId FOR XML RAW) AS VARBINARY(MAX))), 2) AS source_hash,
  CONVERT(CHAR(64), HASHBYTES('SHA2_256', CAST((SELECT Id, Region FROM dbo.Account
    WHERE Region IS NOT NULL ORDER BY Id FOR XML RAW) AS VARBINARY(MAX))), 2) AS destination_hash;
```

## How to roll this back
Before the source-column drop, backing out is lossless: drop the destination column and repoint readers
back to `Customer.Region`, which still holds its values. The drop is not auto-reversible — once the
source column is gone the values live only on `Account`; restoring means re-adding it and copying back
(proven equal before the drop). Keep the source column recoverable until the drop is durable.

## Not checked / still open
- Application impact — any read or write still pointing at `Customer.Region` breaks once it is dropped;
  that every reader is repointed to `Account` is not confirmed here (app owner).
- Other environments — the relationship is proven 1:1 on a copy of Dev only; Test, UAT, and Prod may
  hold a one-to-many parent this copy does not. Run the 1:1 check before the copy in each.
- Production scale — the copy and drop are exercised at seed scale only.
