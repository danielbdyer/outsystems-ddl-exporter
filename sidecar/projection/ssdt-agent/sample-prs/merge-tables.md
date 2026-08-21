# CustomerAddress → Customer: merge back (three releases; the row-count cardinality proof comes before the copy)

## Verdict
This PR is the first of three releases that fold `CustomerAddress` back into `Customer`. This release
adds the absorbing columns and copies the data; no data is lost. It hinges on a cardinality proof — the
absorbed side must be 1:1 with `Customer`, or a straight copy silently drops the extra rows. The
absorbed table is dropped only in the third release, after the copy is proven. Confirm 1:1 in each
environment before promoting.

## Intent
The developer's stated intent for this PBI: fold `CustomerAddress` back into `Customer` — two entities
becoming one. No work item supplied — attach one before merge.

## What changes
- **Release 1 (this PR):** `dbo.Customer` — add the absorbing address columns (nullable); a post-deploy
  copy from `CustomerAddress`; the application dual-writes.
- Release 2 repoints reads, foreign keys, and views; Release 3 drops `CustomerAddress`.

## Before promoting
- Run the cardinality query (below): `absorbed_rows` must equal `distinct_parents`. Unequal = 1:many →
  **stop**, the merge is unsafe as stated (a design decision, not a shipping shape).
- After the copy, confirm the absorbed-vs-survivor hashes match. The Release-3 drop is blocked until they do.

## How it ships
- Across three releases; the two tables coexist while readers migrate. R1 additive columns + copy +
  dual-write. R2 cutover (repoint reads/FKs/views). R3 drop the absorbed table — `BlockOnPossibleDataLoss`
  blocks the `DROP TABLE` until the copy is proven. Prove cardinality 1:1 **first**, before the
  value-hash — a 1:many absorbed side silently drops rows and the hash will not catch it.

## The data
- `CustomerAddress` is 1:1 with `Customer`; the absorbed columns are copied onto the survivor.

## What proving showed
Published to a throwaway copy on this branch.
- **Tried / Did:** the row-count cardinality check (`absorbed_rows == distinct_parents`) confirms 1:1;
  the copy publishes clean and the absorbed-vs-survivor hashes match.
- **Realized:** a scratch seed adding a second `CustomerAddress` for one Customer fires the 1:many
  refusal — a straight copy keeps one row per parent and silently drops the rest, and the value-hash
  does not catch it (it only compares surviving rows). That is why the count comes first.

## After deploy — check
```sql
-- expect absorbed_rows = distinct_parents: the absorbed side is 1:1 (unequal = 1:many; stop)
SELECT (SELECT COUNT(*) FROM dbo.CustomerAddress) AS absorbed_rows,
       (SELECT COUNT(DISTINCT CustomerId) FROM dbo.CustomerAddress) AS distinct_parents;

-- run after the copy, before the Phase-3 drop: expect equal hashes
SELECT
  CONVERT(CHAR(64), HASHBYTES('SHA2_256', CAST((SELECT CustomerId, Line1, City, PostalCode FROM dbo.CustomerAddress
    ORDER BY CustomerId FOR XML RAW) AS VARBINARY(MAX))), 2) AS absorbed_hash,
  CONVERT(CHAR(64), HASHBYTES('SHA2_256', CAST((SELECT Id, Line1, City, PostalCode FROM dbo.Customer
    WHERE Line1 IS NOT NULL ORDER BY Id FOR XML RAW) AS VARBINARY(MAX))), 2) AS survivor_hash;
```

## How to roll this back
Before the Phase-3 drop, backing out is lossless: drop the added survivor columns and repoint reads,
foreign keys, and views back to `CustomerAddress`, which still holds its data. The Phase-3 drop is not
auto-reversible — once `CustomerAddress` is dropped, recovery means recreating it and copying back from
the survivor (proven hash-equal before the drop). Keep the absorbed table recoverable until the drop is durable.

## Not checked / still open
- Application impact — the application must dual-write into the new columns during Release 1 and read the
  survivor after cutover; that every reader and writer is repointed off the absorbed table is not
  confirmed here (app owner).
- External consumers — an outside reference may still read `CustomerAddress` by name; known ones are
  repointed in Release 2, unknown ones are not covered.
- Other environments — cardinality is proven on a copy of Dev only; Test, UAT, and Prod may hold a
  1:many parent this copy does not. Run the cardinality query before the copy in each.
- Production scale — the copy and drop are exercised at seed scale only.
