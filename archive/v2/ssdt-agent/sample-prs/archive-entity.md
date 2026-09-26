# Order → archive.OrderArchive: move retired orders to an archive table (every row is conserved, none dropped or doubled)

## Verdict
This PR moves retired `Order` rows out of the live table into `archive.OrderArchive`, conserving
every row — each ends up either still live or in the archive, none dropped and none duplicated.
Confirm in each environment that nothing still reads the archived orders as if they were live — a
report, a screen, an export — before promoting, and schedule a window at production volume because
the move runs row by row.

## Intent
The developer's stated intent for this PBI: move orders that are no longer active out of the live
`Order` table into an archive table, keeping the data available but out of the live path. The
retired set here is the Cancelled orders (`StatusId = 3`). No work item supplied — attach one before
merge.

## What changes
- `archive.OrderArchive`: new archive table with the same columns as `dbo.[Order]`.
- The retired `Order` rows are moved to it by a batched `DELETE dbo.[Order] OUTPUT DELETED.* INTO
  archive.OrderArchive WHERE StatusId = 3`, which removes each row from the live table and writes it
  to the archive in one statement.

## Before promoting
- Run the conservation query (below) after the move in each environment and confirm live rows plus
  archived rows equal the pre-move total — no row lost, none doubled.
- Confirm nothing still needs the archived orders in the live table — check with the report, screen,
  and export owners, because a consumer that reads `dbo.[Order]` will no longer see the moved rows.
- Confirm Release 1 (the archive table added) has landed in an environment before running the move
  there, so the application can read both tables while the move is in flight.

## The data
- 4 rows in `dbo.[Order]` before the move. 1 is Cancelled (`StatusId = 3`) and is moved to the archive;
  3 remain live.
- After the move: 3 live + 1 archived = 4, the pre-move total. The moved row is byte-identical in the
  archive — its content hash before the delete equals its content hash in the archive.

## How it ships
- Across more than one release, because a running application cannot switch which table it reads in
  the same instant the rows move. Release 1 adds `archive.OrderArchive` (additive, one declarative
  release). Release 2 runs the batched move once the application can read both. The move itself is a
  data motion, which SSDT does not express declaratively — it is a scripted `DELETE … OUTPUT DELETED.*
  INTO …` that the data-loss gate does not govern, run in batches so the transaction log stays bounded.
- The archive table's creation and the row move are separate steps for a reason: the additive create
  is safe on its own, and the move is the reviewed, reversible-with-effort data motion.

## What proving showed
Published to a throwaway copy on this branch.
- **Tried:** publish the full project to establish `dbo.[Order]` with 4 rows, then add
  `archive.OrderArchive` and run the batched `DELETE … OUTPUT DELETED.* INTO archive.OrderArchive
  WHERE StatusId = 3` on the copy.
- **Did:** count both sides — the live table holds 3 rows, the archive holds 1, and 3 + 1 = 4, the
  recorded pre-move total. No row was lost and none doubled.
- **Realized:** the moved row is preserved exactly. A `SHA2_256` content hash of the retired row taken
  before the delete equalled the hash of the row in the archive afterward (`BYTE-IDENTICAL`). The
  proof for a data move is conservation and a content hash, not a schema difference, because the
  schema never described the rows.

## After deploy — check
```sql
-- live rows plus archived rows equal the recorded pre-move total, expect them to sum to it
SELECT
  (SELECT COUNT(*) FROM dbo.[Order])            AS live_rows,
  (SELECT COUNT(*) FROM archive.OrderArchive)   AS archived_rows,
  (SELECT COUNT(*) FROM dbo.[Order])
    + (SELECT COUNT(*) FROM archive.OrderArchive) AS live_plus_archived;
```

## How to roll this back
The move reverses as a batched move from the archive back to the live table: `DELETE
archive.OrderArchive OUTPUT DELETED.* INTO dbo.[Order]`. The moved rows are preserved byte-identical
in the archive, so the data itself is recoverable; the reverse move is a scripted step, not
automatic. Dropping the empty archive table afterward is lossless only while it is empty.

## Not checked / still open
- Application impact — any report, screen, or export that reads the archived orders from the live
  table will now miss them. That application and reporting code do not expect those rows in the live
  table is not confirmed on the copy — the app owner confirms it.
- Other environments — QA, UAT, and Prod hold different `Order` counts the copy of Dev cannot see.
  Run the conservation query before and after the move in each.
- Production scale and timing — at production row counts the batched move may run long or block
  writes; the small copy proves the counts conserve and the row is byte-identical, not the duration
  at scale. Schedule a window.
- Reversibility — the reverse move was not exercised on the copy; only the forward move and its
  conservation were proven.
