---
name: create-view
description: Use when the developer says "give me a view that joins Order and Customer", "an Advanced Query entity for active customers", "expose a read-only combined entity", "a saved query I can read like a table" — a new OutSystems view / Advanced Query. SSDT destination = a declarative CREATE VIEW with columns enumerated explicitly (never SELECT *).
---

# Create a view (SELECT * View trap)

> **Default (provisional — the data decides).** Mechanism 1 Pure Declarative, single-phase, Tier 1 — but enumerate the columns.

## OutSystems phrasing
"give me a view that joins Order and Customer", "an Advanced Query entity for active customers", "expose a read-only combined entity".

## SSDT meaning
`CREATE VIEW dbo.SomeView AS SELECT … FROM …`. SSDT publishes it declaratively as CREATE/ALTER VIEW; a view holds no data of its own, so there is no `BlockOnPossibleDataLoss` concern. Never write ALTER.

## The named trap
**SELECT \* View** (handbook 16 = §19). `SELECT * FROM dbo.Order` does not freeze the column list — it re-binds to whatever columns the base table has at each publish, so the view's contract lives *outside* its own .sql: the file can stay byte-for-byte identical while the shape it returns drifts. Always enumerate columns explicitly; SSDT will not flag the `*`. This trap recurs only in create-view and compat-view — it is NOT lifted to an index; it lives here and in `../compat-view/SKILL.md`.

## How it flips (the specifics only)
- plain enumerated-column view → **M1**, Tier 1.
- view that downstream apps/reports/ETL depend on → still M1 mechanically, but **Tier 3** by dependency scope (external deps): changing it later is a cross-system change.
- `SELECT *` view → mechanically M1, but a **latent defect** — fix the `*` before shipping, not a flip.
- indexed/materialized view → see `../indexed-view/SKILL.md`; the materialization changes the mechanism.

## Prove it
Preview the Strict delta — a clean CREATE/ALTER VIEW, no rebuild, no veto. If the developer wrote `SELECT *`, prove the drift: add a column to the base table, re-publish, and show the view's resolved column list changed *without the view's .sql changing* — that is the SELECT-\* trap made visible. See `prove-on-dacpac`. On the sample, `dbo.vOrderSummary` ships an enumerated variant plus a documented `SELECT *` variant for the scratch-copy drift proof (VIE-01).

## Verdict to the developer
"Your view publishes clean — it holds no data so there's nothing to lose. I enumerated the columns explicitly instead of SELECT *, so it won't silently change shape when the underlying entity does."

## Teach it (the graduation)
A definition that defers to "whatever's there" has no contract, and a change with no visible diff is the hardest to catch — freeze the surface explicitly so drift shows up as a reviewable diff; the fail mode avoided is a `SELECT *` view rotting silently as its base evolves.
