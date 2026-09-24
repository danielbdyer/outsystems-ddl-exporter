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
- **Release 1 (this PR):** `dbo.Customer` — add the absorbing address columns (`Line1`, `City`,
  `PostalCode`, nullable); a post-deploy step copies them from `CustomerAddress` keyed by `CustomerId`;
  the application dual-writes.
- Release 2 repoints reads, foreign keys, and views; Release 3 drops `CustomerAddress`.

## Before promoting
- Run the cardinality query (below): `absorbed_rows` must equal `distinct_parents`. Unequal = 1:many →
  **stop**, the merge is unsafe as stated (a design decision, not a shipping shape).
- After the copy, confirm the absorbed-vs-survivor hashes match. **Alias both sides to the same column
  names** — `FOR XML RAW` encodes the names, so a perfect copy hashes unequal unless the names match.
  The Release-3 drop is licensed only once they do.

## The data
- `CustomerAddress` holds 5 rows, one per `Customer` (distinct `CustomerId` = 5), so it is 1:1. The
  absorbed columns are copied onto the survivor; no existing row is removed in this release.

## How it ships
- Across three releases; the two tables coexist while readers migrate. R1 additive columns + copy +
  dual-write — purely additive, so it publishes clean in one release. R2 cutover (repoint
  reads/FKs/views). R3 drop the absorbed table — a populated-table drop that ships as the one-release
  scripted `DROP TABLE` (`delete-entity`), licensed only once the copy is proven. Prove cardinality 1:1
  **first**, before the value-hash — a 1:many absorbed side silently drops rows and the hash will not
  catch it (it only compares surviving rows).

## What proving showed (published to a throwaway copy, this branch)
Published onto a fresh copy (`pg_merge`, sqlpackage 170.4.83.3, `BlockOnPossibleDataLoss = True`).
- **Tried:** R1 — add `Line1`/`City`/`PostalCode` to `Customer` plus the post-deploy copy — published
  clean.
- **Did:** the cardinality check returned `absorbed_rows = 5`, `distinct_parents = 5` — 1:1. The
  absorbed-vs-survivor content hash matched (`70353E7E…` = `70353E7E…`) **only with both projections
  aliased to the same names** (`CustomerId AS k, Line1 AS a, …` vs `Id AS k, Line1 AS a, …`); mismatched
  names hash unequal over identical data because `FOR XML RAW` carries the column names.
- **Realized:** inserting a second `CustomerAddress` for one `Customer` flipped the cardinality to
  `absorbed_rows = 6`, `distinct_parents = 5` — the 1:many refusal. A straight copy would keep one row
  per parent and silently drop the rest, and the value-hash would *not* catch it (it compares only the
  rows that survived). That is why the count comes first.

## After deploy — check (each environment)
```sql
-- expect absorbed_rows = distinct_parents: the absorbed side is 1:1 (unequal = 1:many; stop)
SELECT (SELECT COUNT(*) FROM dbo.CustomerAddress) AS absorbed_rows,
       (SELECT COUNT(DISTINCT CustomerId) FROM dbo.CustomerAddress) AS distinct_parents;

-- run after the copy, before the Release-3 drop: expect equal hashes. ALIAS both sides to the same
-- names (k, a, b, c) — FOR XML RAW encodes column names, so different names never match.
SELECT
  CONVERT(CHAR(64), HASHBYTES('SHA2_256', CAST((SELECT CustomerId AS k, Line1 AS a, City AS b, PostalCode AS c
    FROM dbo.CustomerAddress ORDER BY CustomerId FOR XML RAW) AS VARBINARY(MAX))), 2) AS absorbed_hash,
  CONVERT(CHAR(64), HASHBYTES('SHA2_256', CAST((SELECT Id AS k, Line1 AS a, City AS b, PostalCode AS c
    FROM dbo.Customer WHERE Line1 IS NOT NULL ORDER BY Id FOR XML RAW) AS VARBINARY(MAX))), 2) AS survivor_hash;
```

## How to roll this back
Before the Release-3 drop, backing out is lossless: drop the added survivor columns and repoint reads,
foreign keys, and views back to `CustomerAddress`, which still holds its data. The Release-3 drop is not
auto-reversible — once `CustomerAddress` is dropped, recovery means recreating it and copying back from
the survivor (proven hash-equal before the drop). Keep the absorbed table recoverable until the drop is
durable. Backing the change out was not exercised.

## Not checked / still open
- Application impact — the application must dual-write into the new columns during Release 1 and read the
  survivor after cutover; that every reader and writer is repointed off the absorbed table is not
  confirmed here (app owner).
- External consumers — an outside reference may still read `CustomerAddress` by name; known ones are
  repointed in Release 2, unknown ones are not covered.
- Other environments — cardinality is proven on a copy of Dev only; QA, UAT, and Prod may hold a
  1:many parent this copy does not. Run the cardinality query before the copy in each.
- Production scale — the copy and drop are exercised at seed scale (5 rows) only.
