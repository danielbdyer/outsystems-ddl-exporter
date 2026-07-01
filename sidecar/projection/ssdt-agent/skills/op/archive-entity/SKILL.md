---
name: archive-entity
description: Use when the developer says "archive old orders", "move the historical rows out to an archive table", "move records we don't need live anymore" — a data movement between tables, not a shape change.
---

# Archive entity

> **Default (provisional — the data decides).** Mechanism 5 Multi-Phase, multi-PR, Tier 3 (Tier 4 if the move is irreversible / large). Create destination → migrate (batched) → verify counts.

## OutSystems phrasing
"archive old orders", "move the historical rows out to an archive table".

## SSDT meaning
This is a **data movement**, which SSDT does not express declaratively. You create the archive
destination (declarative) and script the row move (post-deploy), typically batched `DELETE ...
OUTPUT DELETED.* INTO archive.X` to spare the transaction log. SSDT describes *shapes*, not
*data motion*.

## The named trap
Unbatched moves bloat the transaction log; child rows with FKs must move (or their FKs be
disabled) **before** parents; cross-database archives lose FK enforcement. The coexistence
obligation (live + archive both readable during the move) is the multi-phase concern — see
`../../_index/multi-phase/SKILL.md`; do not re-derive the coexistence shape here.

## How it flips (the specifics only)
- new archive table + scripted move → **M5 Multi-Phase** (additive → migrate → verify; see `../../_index/multi-phase/SKILL.md`)
- large volume (>1M rows) → **+1 Tier**, mandatory batching
- source is CDC-enabled → the deletes generate capture rows; coordinate → **+1 Tier** (see `../../_index/cdc/SKILL.md`)
- active queries must see only live data during the move → coexistence, stays multi-PR

## Prove it
Prove **source-count + archive-count == original total** after the batched move (no rows dropped,
none duplicated) and that each batch commits (log stays bounded) — this is the conservation proof
the multi-phase index owns. Permissive run snapshots before/after row hashes to prove the moved
rows are byte-identical in the archive. For the publish loop, see
`../../prove-on-dacpac/SKILL.md`; probes, `../../talk-to-local-sql/SKILL.md`.

## Verdict to the developer
"Archiving is a data move, and SSDT has no declarative 'move' — so it's Multi-Phase, multi-PR:
create the archive table, then batch the rows across. I proved the counts reconcile exactly and
each batch commits. Tier 3, and +1 because it's over a million rows."

## Teach it (the graduation)
The instant a request is about *moving data between tables* rather than *changing a table's
shape*, you have left the declarative world and the proof becomes a conservation check (see
`../../_index/multi-phase/SKILL.md`). Fail mode avoided: an unbatched move that silently loses or
doubles rows and looks identical in the schema. Ask "shape change or data move?"
