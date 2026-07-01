---
name: modify-index
description: Use when the developer says "change the index to cover these columns too", "make this index unique so we stop getting duplicates", "the index should be on a different attribute now" — editing an index definition. SSDT does a DROP+CREATE rebuild; adding UNIQUE vetoes on duplicates.
---

# Modify an index (change key columns / non-unique → unique / change include list)

> **Default (provisional — the data decides).** Mechanism 1 Pure Declarative, single-phase, Tier 1–2 for a non-unique key change. Adding UNIQUE is a claim over the data — PROVE no duplicates, or it flips.

## OutSystems phrasing
"change the index to cover these columns too", "make this index unique so we stop getting duplicates", "the index should be on a different attribute now".

## SSDT meaning
You edit the index definition in the `.sql`. SSDT does **not** ALTER in place — it emits `DROP INDEX` + `CREATE INDEX` (a full rebuild over all rows). Changing a non-unique index to **UNIQUE** additionally enforces uniqueness at build time.

## The named trap
The **non-unique → unique build fails on duplicates** — SSDT emits the unique index and the deploy fails the instant a duplicate key value exists. This is the index-grade constraint-is-a-claim veto (a value collision, not row-presence) — see `../../_index/constraint-is-a-claim/SKILL.md`; do not re-derive the duplicate mechanics here. A key/include change with no uniqueness added never vetoes on data.

## How it flips (the specifics only)
- key/include change, no uniqueness added → M1, single-phase, Tier 1–2 (the DROP+CREATE rebuild blocks writes like add-index; size pushes the tier).
- non-unique → unique, NO duplicates → still M1, single-phase (prove it).
- non-unique → unique, duplicates PRESENT → **FLIP to M3 Pre-Deploy+Declarative, single-PR**: a pre-deploy de-dupe clears duplicates BEFORE the unique build, Tier 3 — the veto is on the actual duplicate values, see `../../_index/constraint-is-a-claim/SKILL.md`.
- \+ >1M rows → **+1 Tier** (rebuild + dedupe cost).
- \+ CDC-enabled → **+1 Tier** (see `../../_index/cdc/SKILL.md`).

## Prove it
For a uniqueness change, run the duplicate probe FIRST: `SELECT <keycols>, COUNT(*) FROM <table> GROUP BY <keycols> HAVING COUNT(*) > 1`. Then build + Strict publish: clean → no duplicates; a build failure ("CREATE UNIQUE INDEX … terminated because a duplicate key was found") is the veto. Author the pre-deploy de-dupe, re-run Strict, the clean re-run is the proof. See `../../prove-on-dacpac/SKILL.md` + `../../talk-to-local-sql/SKILL.md`. Seed: Product's `DUPE` rows drive the unique-veto flip.

## Verdict to the developer
"You asked to make that index unique. I published it to a copy of your data and SSDT refused — 3 rows share the same value, so a unique index can't be built. This is a Pre-Deploy + Declarative change: I dedupe those 3 rows first, then the unique index builds clean. Proven."

## Teach it (the graduation)
Any change that adds a *constraint* over existing data (UNIQUE here) could flip on the data — probe first (`GROUP BY … HAVING COUNT(*) > 1`); the duplicate probe *is* the mechanism decision. See `../../_index/constraint-is-a-claim/SKILL.md`. Fail mode avoided: guessing single-vs-multi from the SQL.
