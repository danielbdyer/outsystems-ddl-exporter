---
name: delete-attribute
description: Use when the developer says "remove the attribute", "delete the LegacyCode field nobody uses", "drop this column", "get rid of this field" — removing a column. The 4-phase deprecation; danger is not release-count.
---

# Delete attribute (4-phase deprecation)

> **Default (provisional — the data decides).** Mechanism 5 Multi-Phase, multi-PR, Tier 3–4 (Tier 4 once real data is destroyed). Mechanically the final drop is one statement, but the safe path spans releases.

## OutSystems phrasing
"remove the attribute", "delete the LegacyCode field, we don't use it".

## SSDT meaning
Remove the column from the `CREATE`. SSDT emits `ALTER TABLE ... DROP COLUMN [Col]`. On a column
that holds data, `BlockOnPossibleDataLoss=True` **vetoes** it; the values are irrecoverable
without a backup. Edit the CREATE; never write `ALTER`.

## The named trap
Dropping a column still referenced by a view/proc/computed-column/index (those break or block the
drop); and dropping before the app has genuinely stopped reading it. The populated-column veto is
the tightening-class row-presence gate — see `../../_index/tightening-class/SKILL.md`. The
coexistence obligation (the 4-phase deprecation) is the multi-phase concern — see
`../../_index/multi-phase/SKILL.md`. Do not re-derive either here.

## How it flips (the specifics only)
- column empty / provably unused, no dependents → mechanically **M1**, but danger keeps it **Tier 3**
- column holds data → `BlockOnPossibleDataLoss` veto (row-presence — see `../../_index/tightening-class/SKILL.md`); irreversible → **Tier 4**
- app still reads/writes the column → coexistence required → **M5 multi-PR** 4-phase deprecation (soft-deprecate → stop writes → verify unused → drop; see `../../_index/multi-phase/SKILL.md`)
- referenced by a view/proc/index → drop those first → ordered multi-step
- CDC-enabled / >1M rows → **+1 Tier** (see `../../_index/cdc/SKILL.md`)

## Prove it
Strict publish **vetoes** on `BlockOnPossibleDataLoss` when the column has data — show the veto.
Run `sys.dm_sql_referencing_entities` to prove nothing still references it. Prove the ordered drop
(dependents first) on the throwaway DB. The clean Strict re-run after the column is provably
empty/unused is the proof. For the publish loop, see `../../prove-on-dacpac/SKILL.md`; probes,
`../../talk-to-local-sql/SKILL.md`.

## Verdict to the developer
"Removing the attribute destroys its data for good — SSDT vetoed it on a copy because the column
still holds values, and two views reference it. Safe path is the 4-phase deprecation across
releases: stop writing it, confirm nothing reads it, then drop. Multi-Phase, multi-PR, Tier 4
because the data is gone once it's gone."

## Teach it (the graduation)
A drop is a *sequence ending in an irreversible act*, not a single edit — old and new must coexist
until cutover (see `../../_index/multi-phase/SKILL.md`), and danger lives in a different axis than
the one-statement mechanics. Fail mode avoided: blind-dropping an "unused" column before
`sys.dm_sql_referencing_entities` and a stop-writes phase prove it truly dead.
