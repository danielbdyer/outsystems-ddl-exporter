# OrderLine: archive the old lines (a data move, not a drop — every row is conserved, byte-identical, and batched)

**In OutSystems** — You archive old `OrderLine` records — "move the historical rows out to an archive table", "move records we don't need live anymore". You keep the data; you just stop carrying it in the live table.
**In SSDT** — this is a **data movement**, which SSDT does not express declaratively. The archive destination is a table (created declaratively, or scripted); the row move is a **batched** `DELETE ... OUTPUT DELETED.* INTO archive.X` in a post-deployment step. SSDT describes *shapes*, not *data motion*.

## Summary

You archive the older `OrderLine` rows — move them out of the live table into an archive table, keeping every one. Archiving is the **safe retirement** of data, and it is the counterpoint to the drops elsewhere in this wave: nothing is lost, nothing is dropped, the rows are *relocated*. This was proven objectively against a Twin — a disposable SQL Server database published from this estate and filled with real-shaped synthetic data.

Because this is a move, not a shape change, the proof is a **conservation check**, not a schema diff: after the move, does every original row still exist somewhere — either still live, or in the archive — with none dropped and none duplicated? On the Twin the counts reconciled exactly: 25 rows in, split into 12 kept live and 13 archived, 25 accounted for, the archived rows **byte-identical** to the originals, and **no row in both tables**. The move ran in **batches** that each commit, so the transaction log stays bounded rather than growing to hold the whole move at once.

The one question that decides whether this is safe: **shape change, or data move?** The moment it is a data move, the failure to avoid is an unbatched move that silently loses or doubles rows and looks identical in the schema — because the schema never described the rows in the first place. No work item was provided with the request; attach one before merge so the record is traceable.

## Review & release

- A **dev lead** must review this: existing rows are moved out of the live table. A **principal** reviews it instead when the move cannot be undone (a cross-database archive loses FK enforcement) or the volume is large.
- It ships **across releases**: the archive table is added first, then a batched post-deployment script moves the rows (`DELETE ... OUTPUT DELETED.* INTO archive.OrderLine`), then the counts are reconciled — so the running application keeps reading live data while the move is in flight.
- Added scrutiny, when it applies: at large volume (>1M rows) the move may block writes or run long and **batching is mandatory** — schedule a window.

## Changes

| File | Change |
|---|---|
| `Tables/archive.OrderLine.sql` (new) | **New** — the archive destination table (`[archive].[OrderLine]`, a passive store: no `IDENTITY`, so the original `Id`s are preserved) |
| *(post-deployment script, not a table definition)* | The **batched** `DELETE TOP (n) ... OUTPUT DELETED.* INTO [archive].[OrderLine]` that relocates the rows — SSDT cannot express data motion, so this is authored, not modeled |

No column or key change to `dbo.OrderLine` itself — the live table keeps its shape; only its older rows relocate.

## Data remediation

This change *is* a data operation, so the "remediation" is the move itself, and its correctness is the conservation proof. The invariant that must hold after the move: `live_rows + archived_rows == the recorded pre-move total`, with the archived rows byte-identical to the originals and no `Id` present in both tables. Child rows with foreign keys must move (or their FKs be disabled) **before** their parents; a cross-database archive loses FK enforcement on the archived copy. The move must be **batched** so the transaction log stays bounded.

## Deployment evidence — objective proof, 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, materializes real-shaped data, creates the archive destination, and runs the **batched** move on the disposable copy — then asserts the conservation facts directly (no row dropped, none duplicated, the archived rows byte-identical, the move committed in more than one batch).

**Test:** `Twin.Tests.Integration.SamplePrRemovalTests+SamplePrRemovalTests.archive-entity: retiring rows to an archive table conserves every row (live + archived == original), byte-identical and batched`

```
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 1 m 19 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact 1 — the counts reconcile exactly: every row is conserved, none duplicated.** `dbo.OrderLine` held **25 rows**. The older half (the 13 rows with `Id <= 13`) moved to `[archive].[OrderLine]`; **12** stayed live. After the move, `12 + 13 = 25` — the original total, with **zero** rows present in both tables. Verbatim from the run:

```
baseline: dbo.OrderLine total rows=25; retire the older half (Id <= 13) = 13 rows; keep 12 live; moved-rows digest=-1779981349
batched move (DELETE TOP (7) ... OUTPUT DELETED.* INTO [archive].[OrderLine] WHERE Id <= 13): committed in 2 batches
  after move: live rows=12, archived rows=13, live + archived=25 (original total=25 -> conserved=true)
  archived-rows digest=-1779981349 (moved-rows digest before=-1779981349 -> byte-identical=true); overlap (Id in both tables)=0 (0 = none duplicated)
```

Reading the facts: `live + archived = 25` equals the original total (`conserved=true`) — **no row lost, none doubled**. The archived-rows digest (`-1779981349`) equals the digest those rows carried *before* the move (`byte-identical=true`) — the archive holds the same values, not a lossy copy. `overlap ... = 0` — no `Id` is in both tables, so nothing was duplicated. And the move **committed in 2 batches** (`DELETE TOP (7)` over 13 rows), demonstrating the log-bounded batched form rather than one all-or-nothing transaction.

## Verification — run in each environment after deployment

```sql
-- expect live_rows + archived_rows to equal the recorded pre-move total: no row lost, none doubled.
SELECT
  (SELECT COUNT(*) FROM dbo.OrderLine)      AS live_rows,
  (SELECT COUNT(*) FROM archive.OrderLine)  AS archived_rows;

-- expect 0 rows: no Id is present in both tables (nothing duplicated).
SELECT COUNT(*) AS in_both
FROM dbo.OrderLine d JOIN archive.OrderLine a ON a.Id = d.Id;
```

## Rollback

Reversal is a reverse batched move from the archive back to the source. The moved rows are preserved byte-identical in the archive (proven by the before/after digest), so the data itself is recoverable; the reverse move is a scripted operation, not automatic. Where the archive lives in another database, FK enforcement was already lost on the archived copy. Only the forward move was exercised here.

## Not verified

- **Application impact.** Any report, screen, or export that reads the archived rows from the live source will now miss them. Whether application and reporting code expects those rows in the live table is not confirmed here (@app-owner).
- **Other environments.** Test, UAT, and Prod hold different row counts the disposable copy cannot see. Run the verification query before promotion.
- **Production scale and timing.** At production row counts the batched move may run long or block writes; the small copy proves the batches commit and the counts conserve in shape, not the duration at scale.
- **Reversibility.** A cross-database archive loses FK enforcement, and the reverse move is not exercised on the copy; the forward move is all that was proven.
