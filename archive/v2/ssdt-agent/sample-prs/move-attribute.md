# Customer.Region → Account: move the attribute (multiple releases; a 1:1 join, proven, and the unmapped rows named)

## Verdict
This PR moves `Region` from `Customer` to `Account` by copy-then-drop, never a rename (a cross-table
rename has no refactorlog entry and loses the data). It hinges on the `Customer → Account` link being
1:1 — proven here — and on every `Customer` actually having an `Account`: two do not, so their `Region`
has nowhere to move. Confirm the 1:1 join and settle the unmapped rows in each environment before the
source column is dropped.

## Intent
The developer's stated intent for this PBI: move `Region` from `Customer` to `Account`, where it belongs.
No work item supplied — attach one before merge.

## What changes
- **Release 1 (this PR):** `dbo.Account.Region` receives the value; a post-deploy step copies it from
  `Customer` keyed by the `Customer.AccountId` link.
- Release 2 repoints readers; Release 3 drops `Region` from `Customer`.

## Before promoting
- Run the 1:1 check (below, **excluding NULL `AccountId`**): a row returned is an `Account` with more
  than one `Customer` — the moved value is ambiguous, **stop**, it is a design decision (which Region
  wins?), not a shipping shape.
- Run the coverage query: a `Customer` with a NULL `AccountId` has no `Account` to receive its `Region`.
  Those rows are named on the record as a fork — give them an `Account`, or accept the loss — before the
  source column drops.
- After the copy, confirm the source and destination hashes match, aliasing both sides to the same
  names.

## The data
- 5 `Customer` rows, all with a populated `Region` (West, East, Central, North, South). `AccountId`
  links three of them (1→West, 2→East, 4→North) to Accounts 1/2/3; two (Initech/Central,
  Stark/South) have a **NULL `AccountId`** — no `Account` to move into.

## How it ships
- Across multiple releases; the two tables coexist while readers migrate. The source-column drop is
  gated on row-presence, licensed by the proven copy. A move crosses tables and has no refactorlog
  identity mapping, so it must be copy-then-drop, never a rename — a rename with no refactorlog entry
  would drop the column and lose its data.

## What proving showed (published to a throwaway copy, this branch)
Published onto a fresh copy (`pg_move`, sqlpackage 170.4.83.3, `BlockOnPossibleDataLoss = True`).
- **Tried:** the post-deploy copy `UPDATE Account SET Region = Customer.Region` across the `AccountId`
  join. `Account.Region` (started empty) filled to West / East / North on Accounts 1 / 2 / 3.
- **Did:** the 1:1 check — accounts with more than one customer, **excluding NULL `AccountId`** —
  returned 0 rows: the link is 1:1. The source-vs-destination content hash matched
  (`0DDC0E13…` = `0DDC0E13…`) with both sides aliased to the same names.
- **Realized:** two customers (Initech/Central, Stark/South) have a NULL `AccountId`, so their `Region`
  has no destination — the move copies 3 of 5 regions and strands 2. The `GROUP BY AccountId` check
  **must** exclude NULLs, or the two unmapped rows group together and falsely read as a 1:many
  violation. The coverage gap is a fork for the developer, not a copy the tree can complete alone.

## After deploy — check (each environment)
```sql
-- expect 0 rows: the link is 1:1 (a returned row is an Account with more than one Customer — stop).
-- EXCLUDE NULL AccountId, or the unmapped rows group together and false-positive as 1:many.
SELECT AccountId, COUNT(*) AS children FROM dbo.Customer
WHERE AccountId IS NOT NULL GROUP BY AccountId HAVING COUNT(*) > 1;

-- coverage: a returned row is a Customer whose Region has no Account to move into (settle before the drop)
SELECT Id, Name, Region FROM dbo.Customer WHERE AccountId IS NULL;

-- run after the copy, before the source-column drop: expect equal hashes. ALIAS both sides (k, v) —
-- FOR XML RAW encodes column names, so different names hash unequal over identical data.
SELECT
  CONVERT(CHAR(64), HASHBYTES('SHA2_256', CAST((SELECT AccountId AS k, Region AS v FROM dbo.Customer
    WHERE AccountId IS NOT NULL ORDER BY AccountId FOR XML RAW) AS VARBINARY(MAX))), 2) AS source_hash,
  CONVERT(CHAR(64), HASHBYTES('SHA2_256', CAST((SELECT Id AS k, Region AS v FROM dbo.Account
    WHERE Region IS NOT NULL ORDER BY Id FOR XML RAW) AS VARBINARY(MAX))), 2) AS destination_hash;
```

## How to roll this back
Before the source-column drop, backing out is lossless: drop the destination column (or clear it) and
repoint readers back to `Customer.Region`, which still holds its values. The drop is not auto-reversible
— once the source column is gone the values live only on `Account`, and the two stranded regions
(Central, South) live nowhere unless captured first; restoring means re-adding the column and copying
back (proven equal before the drop) plus a separate record of the unmapped rows. Keep the source column
recoverable until the drop is durable. Backing the change out was not exercised.

## Not checked / still open
- The unmapped rows — Initech (Central) and Stark (South) have no `Account`; whether to create Accounts
  for them or accept losing those two regions is the developer's call and is not settled on a copy.
- Application impact — any read or write still pointing at `Customer.Region` breaks once it is dropped;
  that every reader is repointed to `Account` is not confirmed here (app owner).
- Other environments — the relationship is proven 1:1 on a copy of Dev only; QA, UAT, and Prod may
  hold a one-to-many parent or more unmapped customers this copy does not. Run the 1:1 and coverage
  checks before the copy in each.
- Production scale — the copy and drop are exercised at seed scale (5 rows) only.
