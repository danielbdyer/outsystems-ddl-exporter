---
name: indexed-view
description: Use when the developer says "materialize this aggregation for speed", "cache the joined view", "make the summary view fast", "store the results of this view physically" — a SCHEMABINDING view with a unique clustered index. SSDT destination = a declarative view + UNIQUE CLUSTERED index that physically stores results and binds its base columns.
---

# Indexed / materialized view (SCHEMABINDING) (deterministic-only / base-column-binding trap)

> **Default (provisional — the data decides).** Mechanism 1 Pure Declarative to create on a small/empty base — but it flips toward Multi-Phase as the base grows.

## OutSystems phrasing
"materialize this aggregation for speed", "cache the joined view", "make the summary view fast".

## SSDT meaning
A view with `WITH SCHEMABINDING` plus a `UNIQUE CLUSTERED` index, which causes SQL Server to **physically store** the view's result and maintain it on every base-table write. Unlike a plain view, this one holds data, costs storage and write-time, and **binds the base tables** so they cannot be altered out from under it. Never write ALTER.

## The named trap
The **deterministic-only / SCHEMABINDING coupling** — an indexed view requires every expression to be deterministic (no `GETDATE()`, no non-deterministic functions) and `WITH SCHEMABINDING`, which means **you cannot change any bound base column** without first dropping the index/view. A "trivial" base-table widen now fails because the indexed view binds it — SSDT's delta shows the index/view dropped and rebuilt, expensive and blocking on a large base. This is the closest a view gets to the table-rebuild family (`../identity-swap/SKILL.md` owns that mechanic; cross-referenced here). A single-op coupling — kept inline, not lifted.

## How it flips (the specifics only)
- **small base table** → **M1**, single-phase. Tier 2 (a stored object, not a plain view).
- **large base table** → the unique-clustered-index build is expensive/blocking → **M3/5**, single-PR-with-care or multi-phase; **Tier 3**.
- any later change to a **bound base column** → SSDT must drop+rebuild the indexed view → treat that downstream change as **+1 complexity**; prove the rebuild in the delta.
- **+ >1M rows in the base** → +1 Tier; the maintenance cost on writes is real and ongoing.

## Prove it
Preview the delta and CONFIRM the unique clustered index is built (not just a view CREATE). Then prove the binding cost: edit a bound base column and show SSDT's delta **drops and rebuilds** the indexed view — that rebuild is the hidden price of materialization, and the developer must see it before committing. See `prove-on-dacpac`. On the sample, add `WITH SCHEMABINDING` + `UNIQUE CLUSTERED` to `dbo.vOrderSummary` in a scratch edit (VIE-04).

## Verdict to the developer
"Materializing this view stores its results physically and keeps them in sync on every write — that's the speed you want, but it locks the underlying entities: I proved that changing a bound column forces SSDT to rebuild the whole indexed view, which is expensive on a large table. Worth it for reads, but it raises the cost of future schema changes."

## Teach it (the graduation)
Materialization is not a read-only optimization — it imposes a standing tax on every future change to the bound columns, so the speed-up is bought with rigidity downstream; the fail mode avoided is a later "trivial" base widen turning into a blocking indexed-view rebuild nobody scheduled.
