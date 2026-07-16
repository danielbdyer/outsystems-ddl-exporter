---
name: rebuild-index
description: Use when the developer says "the index is fragmented, rebuild it", "reorganize the index to fix performance", "run index maintenance on this table", "make SSDT rebuild the index on deploy" — operational maintenance with no declarative destination. Refuse-and-route to the maintenance job.
---

# Rebuild / reorganize an index — ⚠️ OPERATIONAL, NOT DECLARATIVE

> **Default (provisional).** This is not a declarative change at all: a rebuild or reorganize is
> operational maintenance against the data's physical storage, not a shape SSDT converges to, so it
> does not ship in the dacpac. There is no schema disposition to assign — the correct outcome routes
> it to a scheduled maintenance job keyed to measured fragmentation. Prove the no-delta before
> classifying it as a schema change.

## OutSystems phrasing
"the index is fragmented, rebuild it", "reorganize the index to fix performance", "run index maintenance on this table".

## SSDT meaning
`ALTER INDEX … REBUILD` / `ALTER INDEX … REORGANIZE`. A **maintenance operation against the data**,
not a change to the described destination — the index's *definition* is identical before and after;
only its physical storage is defragmented. **There is no declarative destination for "rebuild me"**
(handbook file 15 = §18.3: "Rebuild/reorganize → ❌ No — maintenance operation, not schema").

## The named trap
Trying to express maintenance declaratively. If a developer asks you to "make SSDT rebuild the index
on deploy", **stop** — that does not belong in the dacpac. Putting `ALTER INDEX … REBUILD` in a
post-deploy script makes it re-run on EVERY publish (a recurring blocking operation disguised as a
deploy step — anti-idempotent by construction). This operational-not-declarative one-liner is shared
with `../toggle-trust/SKILL.md` but not lifted (only two ops share it — below the N≥3 bar).

## How it flips (the specifics only)
It does not flip between declarative outcomes — it sits outside the model SSDT converges. The only
thing that raises the stakes is operational: a REBUILD on a large table takes a lock (online vs
offline, Enterprise-gated) with a real duration. **Always flag it as out-of-band.**

## Prove it
There is no delta from the dacpac — nothing publishes, so there is nothing to prove there. The proof
is the refusal itself: the request is kept out of the schema project and routed to the maintenance
job. If the developer insists it run on deploy, prove the harm — a post-deploy `ALTER INDEX …
REBUILD` re-runs on every redeploy (the opposite of idempotent intent). See
`../../prove-on-dacpac/SKILL.md` for the no-delta demonstration.

## The verdict (to the developer)
You asked to rebuild the index because it's fragmented — that's genuine maintenance, but it isn't a
schema change, so it doesn't belong in the SSDT project or in this PR. The index's definition is
identical before and after; only its physical storage is defragmented. The right home is a scheduled
job that runs when fragmentation crosses a threshold, not a deploy step: if `ALTER INDEX … REBUILD`
goes in a post-deploy script it re-runs on every single publish, taking a blocking lock each time.
Is there already a maintenance plan for this database to key it to, or should it go to the DBA to
set one up?

## The reasoning (in conversation)
The tell is what the request is really about. When it concerns the *state of the data's storage* —
fragmentation, physical layout — rather than the *shape SSDT can converge to*, it has left the
declarative world and belongs in a job keyed to measured fragmentation, not the dacpac. The failure
this avoids: a blocking rebuild wired into a post-deploy script, re-running on every single publish.

## On the record

The fragment this op contributes to the pull request (`../../author-pr/SKILL.md`), record register.
For a rebuild or reorganize the contribution is a boundary finding: it does not belong in the schema
PR at all.

**Review & release**
- This is not a schema change and does not ship in the dacpac: a rebuild or reorganize alters only
  the index's physical storage, not its definition, so it is routed to a scheduled maintenance job
  keyed to measured fragmentation — not reviewed as a schema change.
- Added scrutiny, when it rides alongside a real change as an out-of-band step on a large table: a
  REBUILD takes a lock (online vs offline, Enterprise-gated) with a real duration at production row
  counts — schedule a window.

**Verification** — run in each environment after deployment
```sql
-- The schema deploy carries no delta for this: a publish issues no ALTER INDEX statement.
-- The operational trigger the maintenance job keys on is fragmentation, measured directly:
SELECT avg_fragmentation_in_percent
FROM sys.dm_db_index_physical_stats(DB_ID(), OBJECT_ID('<table>'), NULL, NULL, 'LIMITED')
WHERE index_id > 0;
```

**Rollback**
Nothing to roll back in the schema — the dacpac is unchanged. A rebuild or reorganize changes only
physical storage, not the index definition, so it leaves no schema state to reverse.

**Not verified**
- Production scale and timing — the lock a REBUILD takes (online vs offline, Enterprise-gated) and
  how long it runs at production row counts are operational and not shown by any dacpac publish; the
  DBA/maintenance plan owns scheduling the window.
- The maintenance itself — whether fragmentation has actually crossed the threshold that warrants a
  rebuild is measured per environment by the job, out of band from this PR.
