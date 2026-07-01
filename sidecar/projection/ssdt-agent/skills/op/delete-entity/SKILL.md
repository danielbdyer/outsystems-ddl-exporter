---
name: delete-entity
description: Use when the developer says "delete the Entity", "drop the table, we don't need it", "remove the old AuditLog" — removing a whole table. Danger is not release-count; the BlockOnPossibleDataLoss veto is the safety proof.
---

# Delete entity

> **Default (provisional — the data decides).** Mechanically Mechanism 4 Script-Only / single-PR, but Tier 4 — data is lost irreversibly. The cleanest example of danger ≠ release-count.

## OutSystems phrasing
"delete the Entity", "drop the table, we don't need it", "remove the old AuditLog".

## SSDT meaning
Remove the `.sql` file; with `DropObjectsNotInSource=True` SSDT emits `DROP TABLE
[schema].[Name]`. On a populated table `BlockOnPossibleDataLoss=True` **vetoes** the publish —
that veto is your safety proof, not a failure. In prod `DropObjectsNotInSource` is usually
**False**, so the drop needs an explicit pre-deploy `DROP`, not mere absence.

## The named trap
Dropping a table with inbound **FKs** (drop fails) or that is **CDC-enabled** (orphans the
capture instance — see `../../_index/cdc/SKILL.md`). The populated-table veto is the row-presence
gate — see `../../_index/tightening-class/SKILL.md` for why it is data-blind; do not re-derive
the guard here.

## How it flips (the specifics only)
- table empty, no dependents → mechanically a clean drop, but still **Tier 4** if it held business data; Tier 3 if provably scratch
- table populated → `BlockOnPossibleDataLoss` veto (row-presence — see `../../_index/tightening-class/SKILL.md`); the drop is the irreversible act → **Tier 4**
- inbound FKs exist → drop those FKs **first** (own the sequence) → small multi-step script
- CDC-enabled → **disable CDC first** or orphan the capture objects → **+1 Tier** (see `../../_index/cdc/SKILL.md`)
- >1M rows / first-time drop → **+1 Tier**

## Prove it
Strict publish must **veto** on `BlockOnPossibleDataLoss` when rows exist — show that veto with
the row count as the safety proof. Then prove the ordered remedy (drop FKs → disable CDC → drop)
on the throwaway DB. Run `sys.dm_sql_referencing_entities` against the table to enumerate what
still points at it. For the publish loop, see `../../prove-on-dacpac/SKILL.md`; probes,
`../../talk-to-local-sql/SKILL.md`.

## Verdict to the developer
"Mechanically this is one drop in one PR. But it's Tier 4: the data is gone for good, and SSDT's
BlockOnPossibleDataLoss vetoed it on a copy because the table holds N rows. Before shipping I
drop the inbound FKs and disable CDC first — I proved that order clears the veto cleanly."

## Teach it (the graduation)
The veto is the gate being conservative because it cannot know your intent (see
`../../_index/tightening-class/SKILL.md`), and danger lives in a different axis than mechanics —
one statement in one PR can be Tier 4. Fail mode avoided: reaching for `DropObjectsNotInSource`
to "make it work" instead of proving the table is truly safe to lose.
