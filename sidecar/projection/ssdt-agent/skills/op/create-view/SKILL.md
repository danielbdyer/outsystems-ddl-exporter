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
**SELECT \* View** (handbook 16 = §19). `SELECT * FROM dbo.Order` does not freeze the column list; its resolved shape is **cached metadata** that only updates when the module is rebound. SSDT auto-rebinds dependents on publish (`sp_refreshsqlmodule`), so *through SSDT* a `SELECT *` view stays current — but a base-table change made **out of band** (a raw `ALTER` outside the dacpac) leaves the view bound to the OLD shape until someone runs `sp_refreshview`. So the view's contract lives *outside* its own .sql and can drift with **no reviewable diff**. Always enumerate columns explicitly; SSDT will not flag the `*`. This trap recurs only in create-view and compat-view — it is NOT lifted to an index; it lives here and in `../compat-view/SKILL.md`.

## How it flips (the specifics only)
- plain enumerated-column view → **M1**, Tier 1.
- view that downstream apps/reports/ETL depend on → still M1 mechanically, but **Tier 3** by dependency scope (external deps): changing it later is a cross-system change.
- `SELECT *` view → mechanically M1, but a **latent defect** — fix the `*` before shipping, not a flip.
- indexed/materialized view → see `../indexed-view/SKILL.md`; the materialization changes the mechanism.

## Prove it
Preview the Strict delta — a clean CREATE/ALTER VIEW, no rebuild, no veto. Proving the `SELECT *` trap needs care (verified on the proving ground): a **pure SSDT publish auto-emits `EXECUTE sp_refreshsqlmodule`** for the dependent view, so a *through-model* base-column add makes the `SELECT *` view **stay correct** (it picks up the new column) — that is NOT the trap, it's SSDT protecting you. To prove the trap as a *silent defect*, add the base column via a **non-SSDT path** (a raw `ALTER TABLE` outside the dacpac — a hotfix by another team), then show the `SELECT *` view is still bound to the OLD column set until someone runs `sp_refreshview` manually. That out-of-band drift is where a `SELECT *` view rots. See `prove-on-dacpac`. On the sample, `dbo.vOrderSummary` ships an enumerated variant plus a documented `SELECT *` variant for the scratch-copy drift proof (VIE-01).

## Verdict to the developer
"Your view publishes clean — it holds no data so there's nothing to lose. I enumerated the columns explicitly instead of SELECT *, so it won't silently change shape when the underlying entity does."

## Teach it (the graduation)
A definition that defers to "whatever's there" has no contract, and a change with no visible diff is the hardest to catch — freeze the surface explicitly so drift shows up as a reviewable diff; the fail mode avoided is a `SELECT *` view rotting silently as its base evolves.
