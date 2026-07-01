---
name: add-optional
description: Use when the developer says "add an optional attribute", "add a MiddleName field it can be blank", "add a field that doesn't have to be filled" — a new nullable column. The safest change in the catalog.
---

# Add optional attribute

> **Default (provisional — the data decides).** Mechanism 1 Pure Declarative, single-phase, Tier 1 — purely additive, existing rows just get NULL.

## OutSystems phrasing
"add an optional attribute", "add a MiddleName field, it can be blank".

## SSDT meaning
Add a `NULL` column inside the `CREATE TABLE`. SSDT emits `ALTER TABLE ... ADD [Col] <type>
NULL`. Existing rows get `NULL`; a metadata-only operation on modern SQL Server. Never author
the `ALTER` — edit the CREATE.

## The named trap
None material at the data layer. The one edge: if `IgnoreColumnOrder=False` a mid-table insert
can trigger a rebuild — the proving-ground profiles keep `IgnoreColumnOrder=True`, so position is
a non-issue.

## How it flips (the specifics only)
- any table state (empty or populated) → **M1, single-phase, Tier 1** (NULL is always a valid existing-row value)
- table is CDC-enabled and the new column must be tracked → the capture instance does not include it → **M4/M5 + +1 Tier**, recreate the capture instance (see `../../_index/cdc/SKILL.md`)

## Prove it
Strict publish succeeds clean; the delta is a single `ALTER TABLE ... ADD ... NULL`; no veto
regardless of row count. That clean run on a populated copy is the whole proof. For the publish
loop, see `../../prove-on-dacpac/SKILL.md`.

## Verdict to the developer
"Adding an optional attribute is the safest change there is — existing rows just get NULL. I
proved it publishes clean on a copy of your populated table. Pure Declarative, single-phase,
Tier 1."

## Teach it (the graduation)
An optional add never vetoes because NULL is always a valid existing-row value — the
discriminator is whether existing rows *can already satisfy the new rule*, which is exactly why
the *mandatory* sibling is a different animal. Watch only the CDC case (see
`../../_index/cdc/SKILL.md`). Fail mode avoided: fearing additive columns and mis-locating the risk.
