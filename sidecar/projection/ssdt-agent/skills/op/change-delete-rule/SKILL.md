---
name: change-delete-rule
description: Use when the developer changes the Delete Rule on a reference — "change the Delete Rule to Protect/Ignore/Delete", "turn on cascade delete", "deleting a Customer should delete its Orders". A DROP+ADD of the FK to set ON DELETE action; behaviorally dangerous, especially CASCADE.
---

# Change the delete rule / cascade (Protect / Ignore / Delete)

> **Default (provisional — the data decides).** Mechanism 1 Pure Declarative (DROP + ADD the FK), single-phase, Tier 3 — the publish is clean but the behavioral blast radius (especially CASCADE) drives the tier.

## OutSystems phrasing
The **Delete Rule** on the reference — **Protect** ("can't delete a Customer with Orders"), **Ignore** ("let the Customer go, leave the Orders"), **Delete** ("delete the Customer and its Orders").

## SSDT meaning
The FK's `ON DELETE` action. Mapping: **Protect → `ON DELETE NO ACTION`**; **Ignore → no clean single-DB equivalent** (either `NO ACTION` + app-tolerated dangling refs, which still *blocks* the parent delete, **or** `ON DELETE SET NULL` if the FK column is nullable — ask which the developer means, don't silently pick); **Delete → `ON DELETE CASCADE`**. Changing the rule is a **DROP + ADD** of the FK (you cannot alter the action in place).

## The named trap
Turning on **CASCADE** silently changes runtime behavior — a delete that previously *failed* now *removes child rows*, possibly **chaining** through multiple tables; cascaded deletes may also bypass expected CDC/audit capture. None material to the *publish* (DROP+ADD never vetoes on data) — the danger is entirely behavioral.

## How it flips (the specifics only)
- the DROP+ADD is schema-only and never vetoes on data → M1, single-phase mechanically.
- toward CASCADE → silent multi-table deletes → Tier 3; map the full cascade graph before shipping.
- if you also tighten the FK (re-validate existing rows) → the create-fk orphan rules apply → could flip to script (see `../create-fk-orphan/SKILL.md`).
- CDC-enabled → cascaded deletes may bypass expected capture → **+1 Tier** (see `../../_index/cdc/SKILL.md`).

## Prove it
Script the delta and confirm it is `DROP CONSTRAINT` + `ADD CONSTRAINT ... ON DELETE <action>` (NOT a table rebuild). For CASCADE, **prove the blast radius**: on the throwaway DB, delete one parent and snapshot which child rows vanish across the whole cascade chain. See `../../prove-on-dacpac/SKILL.md` + `../../talk-to-local-sql/SKILL.md`. Seed: the Order → OrderLine chain (KEY-01) makes the cascade chain visible across two levels.

## Verdict to the developer
"Changing the Delete Rule to Delete means `ON DELETE CASCADE`: deleting a Customer now also deletes its Orders — and on a copy I proved it chains to OrderLines too. The change itself is Pure Declarative (drop and re-add the foreign key), but it's Tier 3 because a single delete now silently removes rows in three tables."

## Teach it (the graduation)
A clean publish is not a safe change — some edits are dangerous in what they *do* at runtime, not in what they do to existing rows; *danger is not release-count*. Fail mode avoided: trusting "it deployed clean" for a CASCADE that silently deletes across tables.
