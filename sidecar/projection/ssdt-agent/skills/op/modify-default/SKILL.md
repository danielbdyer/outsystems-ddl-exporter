---
name: modify-default
description: Use when the developer says "change the default value", "new orders should default to Shipped now, not Pending", "stop defaulting this attribute" — changing or removing a DEFAULT constraint. SSDT does a DROP-then-ADD; still Pure Declarative, never backfills.
---

# Modify or remove a default

> **Default (provisional — the data decides).** Mechanism 1 Pure Declarative, single-phase, Tier 1 in any state — changing or removing a default never touches existing row values.

## OutSystems phrasing
"change the default value", "new orders should default to Shipped now, not Pending", "stop defaulting this attribute".

## SSDT meaning
**Modify** = SSDT `DROP`s then re-`ADD`s the named constraint (a brief no-default window inside the deploy transaction). **Remove** = `DROP CONSTRAINT`. Neither touches existing row values — a default only ever governed future inserts.

## The named trap
Same as add-default: the **unnamed default** makes the DROP-then-ADD fragile across environments — insist the constraint is named `DF_<Table>_<Col>`. And a modified default still does NOT retro-change existing rows that were written under the old default; if the developer wants old rows re-stamped, that is a separate backfill (see `../add-default/SKILL.md` and `../../_index/idempotent-seed/SKILL.md`).

## How it flips (the specifics only)
- modify / remove a default → M1, single-phase, Tier 1 (any state).
- developer ALSO wants existing rows re-stamped to the new value → separate op: M2 Declarative+Post-Deploy (idempotent UPDATE — see `../../_index/idempotent-seed/SKILL.md`).
- \+ CDC-enabled → CDC does not track constraints (handbook file 15 = §18.5); no +1 for the default change alone.

## Prove it
Build + Strict `sqlpackage /Action:Script`; for a *modify*, confirm SSDT emits DROP-then-ADD of `DF_…` and the Strict publish is clean with **no UPDATE of existing rows**. For a *remove*, confirm a clean `DROP CONSTRAINT DF_…` with no row touch. See `../../prove-on-dacpac/SKILL.md` + `../../talk-to-local-sql/SKILL.md`.

## Verdict to the developer
"Changing the default is a pure declarative change — SSDT drops the old default constraint and adds the new one, and I proved on a copy that no existing rows are touched. Old rows keep the value they were written with; if you want those re-stamped, that's a separate backfill I can prove."

## Teach it (the graduation)
A default is a rule about *future* inserts — changing or dropping it never reaches back to yesterday's rows; a retro re-stamp is a separate, proven backfill. Fail mode avoided: expecting a modified default to rewrite history.
