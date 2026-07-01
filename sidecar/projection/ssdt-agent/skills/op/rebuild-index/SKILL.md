---
name: rebuild-index
description: Use when the developer says "the index is fragmented, rebuild it", "reorganize the index to fix performance", "run index maintenance on this table", "make SSDT rebuild the index on deploy" — OPERATIONAL maintenance with NO declarative destination. Refuse-and-route to the maintenance job.
---

# Rebuild / reorganize an index — ⚠️ OPERATIONAL, NOT DECLARATIVE

> **Default (provisional).** NOT a declarative mechanism at all. Job-owned, operational work. There is no bucket — the correct output is a refuse-and-route to the maintenance job.

## OutSystems phrasing
"the index is fragmented, rebuild it", "reorganize the index to fix performance", "run index maintenance on this table".

## SSDT meaning
`ALTER INDEX … REBUILD` / `ALTER INDEX … REORGANIZE`. A **maintenance operation against the data**, not a change to the described destination — the index's *definition* is identical before and after; only its physical storage is defragmented. **There is no declarative destination for "rebuild me"** (handbook file 15 = §18.3: "Rebuild/reorganize → ❌ No — maintenance operation, not schema").

## The named trap
Trying to express maintenance declaratively. If a developer asks you to "make SSDT rebuild the index on deploy", **stop** — that does not belong in the dacpac. Putting `ALTER INDEX … REBUILD` in a post-deploy script makes it re-run on EVERY publish (a recurring blocking operation disguised as a deploy step; anti-idempotent by construction). This operational-not-declarative one-liner is shared with `../toggle-trust/SKILL.md` but not lifted (only two ops share it — below the N≥3 bar).

## How it flips (the specifics only)
It does not flip between SSDT buckets — it is outside the SSDT model. The only escalation is operational: a REBUILD on a large table takes a lock (online vs offline, Enterprise-gated) with a real duration. **Always flag it as out-of-band.**

## Prove it
None from the dacpac — there is no delta. The "proof" is that you correctly **refused** to put it in the schema project and routed it to the maintenance job. If the developer insists it be on-deploy, prove the harm: show a post-deploy `ALTER INDEX … REBUILD` re-runs on every redeploy (the opposite of idempotent intent). See `../../prove-on-dacpac/SKILL.md` for the no-delta demonstration.

## Verdict to the developer
"Rebuilding the index isn't a schema change — it's maintenance, like vacuuming. It doesn't go in the SSDT project; it lives in a scheduled job that runs when fragmentation crosses a threshold. I'll flag it for the DBA/maintenance plan instead of putting it in this PR."

## Teach it (the graduation)
The moment a request is about *the state of the data's storage* rather than *the shape SSDT can converge to*, it has left the declarative world and belongs in a job keyed to measured fragmentation. Fail mode avoided: a blocking rebuild re-running on every single publish.
