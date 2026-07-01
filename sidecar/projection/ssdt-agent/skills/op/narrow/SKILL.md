---
name: narrow
description: Use when the developer says "shorten Code to 10 chars", "tighten this field", "reduce the precision", "make it smaller" — shrinking length/precision. The Ambitious Narrowing trap; a member of the table-has-rows tightening class.
---

# Narrow (Ambitious Narrowing) — tightening class

> **Default (provisional — the data decides).** EMPTY table → Mechanism 1, single-phase, Tier 1. POPULATED table → NOT clean single-phase, regardless of whether every value fits. Prove first.

## OutSystems phrasing
"shorten Code to 10 characters", "tighten this field", "reduce the precision".

## SSDT meaning
Shrink length/precision (`NVARCHAR(50)`→`NVARCHAR(10)`). SSDT emits `ALTER COLUMN` to the
narrower type. Any existing value longer than the new size would **truncate** (data loss), so
`BlockOnPossibleDataLoss=True` vetoes it. Edit the CREATE; never write `ALTER`.

## The named trap
**Ambitious Narrowing** (handbook 16 = §19.4) — the build succeeds; the deploy either vetoes
(Block on) or **silently truncates** (Block off). This is **the tightening class** — SSDT injects
the same data-blind `IF EXISTS(SELECT TOP 1 1 FROM <t>) RAISERROR` guard, so Strict vetoes
narrowing on any non-empty table **even when every value already fits** (proven via the
make-mandatory zero-NULL twin). See `../../_index/tightening-class/SKILL.md`; do not re-derive the
guard here.

## How it flips (empty-vs-populated dominates; fit is a second axis)
- **empty table** → **M1 Pure Declarative, single-phase, Tier 1** (guard false)
- **populated, `MAX(LEN) <= new size`** (every value fits) → **STILL vetoes under Strict** — not clean M1. Honest verdict: a named `BlockOnPossibleDataLoss` gate-relaxation for this one change after proving `MAX(LEN)` fits (operationally M4/Script-Only, logged). Tier 2. (Same shape as make-mandatory with zero NULLs — see `../../_index/tightening-class/SKILL.md`.)
- **populated, any value exceeds new size** → real truncation: reconcile the over-length rows first (a data change) **and** still face the gate → **M5 Multi-Phase** if data must be preserved (see `../../_index/multi-phase/SKILL.md`), or a gate-relaxation after a truncate-with-intent reconcile. Tier 3+.
- CDC-enabled / >1M rows → **+1 Tier** (see `../../_index/cdc/SKILL.md`)

## Prove it
Run the `MAX(LEN(Col))` probe AND a `WHERE LEN(Col) > <new>` count to **quantify** how many rows
truncate. Strict publish must **veto** on data loss when over-length rows exist — show the count.
Run Permissive (`BlockOnPossibleDataLoss=False`) and the before/after data hash to show *exactly*
which values would have been chopped. Author the reconcile, re-run Strict. For the publish loop,
see `../../prove-on-dacpac/SKILL.md`; probes, `../../talk-to-local-sql/SKILL.md`.

## Verdict to the developer
"Shortening to 10 looks like a one-liner, but I probed your data: the longest Code is 14
characters and 37 rows exceed 10. SSDT vetoed the change on a copy to protect them, and the
permissive run showed exactly what would have been chopped. Pre-Deploy+Declarative, single-PR,
Tier 3 — here's the reconcile that makes it pass. (On an empty table it would have been a clean
one-liner.)"

## Teach it (the graduation)
Narrow shares ONE gate behavior and ONE remedy shape with make-mandatory and delete-attribute — the
first question is never `MAX(LEN)`, it is *is the table empty?* (see
`../../_index/tightening-class/SKILL.md`). Fail mode avoided: re-discovering the same veto op by
op instead of learning the class.
