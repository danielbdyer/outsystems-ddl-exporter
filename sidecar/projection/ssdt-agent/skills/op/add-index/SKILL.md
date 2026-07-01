---
name: add-index
description: Use when the developer says "add an index on Customer.Email", "make this attribute searchable", "the list screen is slow, can we index it" — adding a nonclustered index. Additive and always Pure Declarative, but the build runs over every row and takes a write-blocking lock.
---

# Add an index

> **Default (provisional — the data decides).** Mechanism 1 Pure Declarative, single-phase, Tier 1 — additive; nothing lost. But the *build cost* (a write-blocking lock scaling with rows) lives in the data, not the `.sql`.

## OutSystems phrasing
"add an index on Customer.Email", "make this attribute searchable", "the list screen is slow, can we index it".

## SSDT meaning
An index definition added to the table's `.sql` (inline `INDEX IX_Customer_Email (Email)` or a separate `CREATE NONCLUSTERED INDEX` object). SSDT's publish engine emits `CREATE INDEX` and **builds the index over every existing row** at deploy time — a real, blocking operation on a populated table, not a metadata flip.

## The named trap
Not a §19 anti-pattern by name — the silent cost IS the trap: a non-`ONLINE` build takes a schema-modification lock and **blocks all writes for the build's duration** (an unasked-for outage on a big table). `WITH (ONLINE = ON)` makes it non-blocking but is **Enterprise/Developer edition only** — it fails on Standard. This ONLINE=Enterprise coupling is a single-op concern; it stays inline here (not lifted).

## How it flips (the specifics only)
- table empty / small → M1, single-phase, Tier 1.
- table populated, large (build blocks writes) → still M1 declaratively, but the blocking build pushes review to **Tier 2** (pair-supported); name the maintenance window.
- \+ >1M rows → **+1 Tier** (Tier 2→3); `ONLINE = ON` may be required (Enterprise-gated) to avoid a write outage.
- ONLINE=ON needed but target is Standard → the declarative build stays; flag the online option is unavailable; the build WILL block.
- CDC-enabled → **+1 Tier** (CDC ignores indexes, but the table is high-stakes — see `../../_index/cdc/SKILL.md`).

## Prove it
Build the dacpac, run Strict `sqlpackage /Action:Script`, confirm the delta is a clean `CREATE INDEX` with **no drop, no table rebuild** — a clean Strict publish proves Pure Declarative. The proving ground is small, so the observed build time is NOT the prod build time — **row count is the predictor**; say so explicitly if the target is large. See `../../prove-on-dacpac/SKILL.md` + `../../talk-to-local-sql/SKILL.md`.

## Verdict to the developer
"Adding the index is a pure declarative change — I published it to a copy of your data and SSDT just ran CREATE INDEX, nothing lost. One caveat: on the real table this build locks writes while it runs, so we schedule it in a window (or use ONLINE if you're on Enterprise)."

## Teach it (the graduation)
Separate *what the engine does* (CREATE INDEX, always clean) from *what it costs* (a lock whose duration scales with rows — only the row count tells you). Fail mode avoided: shipping an index blind and blocking prod writes.
