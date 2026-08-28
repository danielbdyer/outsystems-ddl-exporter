# Order.StatusText → the Status lookup: the full program, driven end to end

**The program:** `extract-to-lookup` on `dbo.[Order]` — the free-text `StatusText` column is
replaced by the `StatusId` reference to `dbo.Status`. The additive leg (the lookup and the
reference column) predates this program; what ships here is the half no single-operation
example exercises: **migrate** (reconcile the free text, land the foreign key) and **contract**
(retire the free-text column under the locked gate). Atoms: `edit-seed` + `create-fk-clean`
(migrate), then `delete-attribute` as a two-release (contract).

## Verdict
The program ships as THREE releases. Release M reconciles the one unmapped status value in a
pre-deployment step and lands `FK_Order_Status_StatusId` trusted in the same publish. Release
C1 drops `StatusText` physically in a pre-deployment step with the model still declaring it —
the naive declarative drop is refused by the data-loss guard — and Release C2 lets the model
catch up as a no-op. A dev lead must review each release; existing data is modified, and the
contract leg is irreversible once the free text is gone. Hold other publishes to an
environment while C1 is landed and until C2 follows it there: a publish carrying the lagging
model re-creates the dropped column, backfilled with its default, on a green publish.

## Intent
The developer's stated intent: order status is a real reference, not free text; typos like the
stray "On Hold" prove the point. The free-text column retires once the reference carries the
truth.

## What changes
- Release M: the seed's `Status` block is the record of the mapping decision — the unmapped
  value becomes Status row 4 (`On Hold`); the `Order` seed repoints row 3 to it; and
  `dbo.[Order]` gains `CONSTRAINT FK_Order_Status_StatusId FOREIGN KEY (StatusId) REFERENCES
  dbo.Status (Id)`. A pre-deployment step makes the same reconcile against live rows.
- Release C1: a pre-deployment step drops `DF_Order_StatusText` and `StatusText`; the model
  still declares both; the `Order` seed stops naming `StatusText`.
- Release C2: the model removes `StatusText` and its default; the C1 pre-deployment block is
  retired.

## Before promoting
- The mapping is the product owner's decision, recorded in the seed: "On Hold" is a real
  status (row 4), not a typo to fold into an existing one.
- Land M, then C1, then C2 in each environment, in order, C2 promptly after C1.
- Hold other publishes to the environment between C1 and C2 — the lag window is live (see
  below, proven).

## The data
- `dbo.[Order]` holds 4 rows. One (`Id = 3`) carried the free-text value `On Hold`, which
  matched no `Status.Code` (1 unmapped) and disagreed with its own `StatusId` (1 disagreement)
  — measured before anything shipped. `dbo.Status` held 3 rows and holds 4 after Release M.

## How it ships
- Release M — one release: the pre-deployment reconcile, then the declarative foreign-key add
  lands validated in the same publish.
- Releases C1 and C2 — the locked gate's two-release: the naive declarative drop is refused
  (`Msg 50000`, the row-presence guard above `DROP COLUMN` in the generated script), so the
  physical drop rides C1's pre-deployment step with the model lagging, and C2 closes the gap
  as a no-op. C2's generated script carries no statement touching `StatusText`.

## What proving showed
Published to a throwaway copy on this branch (sqlpackage 170.5.76), release by release. Four
findings, three of them program-grain — visible only because the whole program ran:

- **The seed undoes a pre-deploy repoint it disagrees with.** Release M's first shape put the
  reconcile only in the pre-deployment step. The publish was green — and the post-deployment
  seed, still declaring row 3's old `StatusId`, put it back in the same publish. The
  reconcile's truth belongs in the seed; the pre-deployment step only makes live rows match
  it. With the seed corrected, Release M published and `FK_Order_Status_StatusId` landed
  trusted (`is_not_trusted = 0`) over the populated child, 0 disagreements after.
- **The seed's claim over a dropped column fails the publish after the drop commits.** C1's
  first shape left `StatusText` in the `Order` seed: the pre-deployment drop committed, then
  the seed failed with `Msg 207` ("Invalid column name 'StatusText'") — a failed publish on a
  half-applied release. The corrected seed is part of C1's change set, not a follow-up.
- **A phase-bound pre-deployment block breaks the phase after it.** Release M's reconcile
  block, left in place, reads `StatusText` — which C1 drops. C1's publish failed with
  `Msg 207` from the stale block before anything else ran. A pre-deployment step retires when
  its phase completes; the retirement is part of the next phase's change set.
- **The revert hazard, on a green publish.** With C1 landed (column gone) and the model still
  lagging, one more publish of the same release reported `Successfully published database` —
  and re-created `StatusText`, every row backfilled to the default `Pending`. The free-text
  values were destroyed and nothing failed. Harmless here for exactly one reason: Release M
  had already moved the information into `StatusId`. This is the lag window measured, and the
  reason C2 follows C1 promptly and other publishes hold in between.

## After deploy — check
```sql
-- Release M, expect 0: every order's status resolves through the reference
SELECT COUNT(*) FROM dbo.[Order] o LEFT JOIN dbo.Status s ON s.Id = o.StatusId WHERE s.Id IS NULL;

-- Release M, expect 0 rows: the foreign key is trusted
SELECT name FROM sys.foreign_keys WHERE name = 'FK_Order_Status_StatusId' AND is_not_trusted = 1;

-- Release C2, expect NULL: the free-text column is gone
SELECT COL_LENGTH('dbo.[Order]', 'StatusText') AS statustext;
```

## How to roll this back
Before C1, everything reverses: the foreign key drops cleanly and the seed edits revert.
After C1, the free-text values are gone — re-adding the column re-creates it backfilled with
the default, not with the original text. The information survives only as `StatusId`; that is
the program's point, and it is why C1 is the commitment step and a dev lead reviews it.

## Not checked / still open
- Application reads of `StatusText` — every reader must be on `StatusId` before C1; the
  application owner confirms it. The disposable copy cannot see readers.
- Production scale — the reconcile UPDATE and the column drop were measured on 4 rows;
  schedule a window at production row counts.
- Other environments — each environment carries its own unmapped values; run the Release M
  probe (`StatusText` not matching any `Status.Code`) in each before promoting M there.
