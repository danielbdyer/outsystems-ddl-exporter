---
name: compat-view
description: Use when the developer says "I renamed Customer to Account but the old reports still ask for Customer", "keep the old entity name working after the rename", "don't break the integrations that read the old table", "the SSIS package still expects the old name" — a bridge that keeps an old entity name readable after a rename/split. SSDT destination = a CREATE VIEW bearing the OLD name selecting from the renamed table, enumerated and temporary.
---

# Backward-compatibility view (SELECT * / forgotten-it-is-temporary trap) — recipe AUTHORED HERE

> **AUTHORED-HERE NOTICE.** Handbook file 14 references §17.8 "backward-compatibility view" as the companion to a rename/split, but the template body is empty. The recipe below is authored here to fill the gap; treat it as the working contract and fold it back when file 14 is completed.

> **Default (provisional — the data decides).** Mechanism 1 Pure Declarative for the view itself, but part of a multi-PR rename/split program, Tier 3 (external dependency scope).

## OutSystems phrasing
"I renamed Customer to Account but the old reports still ask for Customer", "keep the old entity name working after the rename", "don't break the integrations that read the old table".

## SSDT meaning
After a rename or split changes the real table's name/shape, create a **view bearing the OLD name** that SELECTs from the new table, mapping the old column list to the new one. External consumers keep reading `dbo.Customer` (now a view) while the real data lives in `dbo.Account` (the table). The view is the bridge that lets old and new names coexist. Never write ALTER.

## Why it exists
A rename is only safe inside the dacpac if the refactorlog records it (so SSDT emits `sp_rename`, not the data-destroying DROP+CREATE — see `../../_index/identity-and-refactorlog/SKILL.md`). But the refactorlog protects only consumers **inside** the SSDT model. Anything outside it — SSIS, Power BI, hand-written procs, the SSAS feed — still asks for the old name and breaks the instant the table is renamed. The compat view restores the old name as a readable surface.

## The named trap
Making the compat view a `SELECT *` (re-triggers the *SELECT \* View* trap and defeats the purpose — enumerate and alias the old column names; see `../create-view/SKILL.md`), and forgetting it is **temporary** — a compat view is debt with a sunset date. Leaving it forever recreates the name ambiguity the rename resolved.

## The recipe (authored here)
1. **Inside the model**, perform the rename with the refactorlog entry so internal consumers and the dacpac see `sp_rename` (no data loss). Prove the delta is `sp_rename`, NOT DROP+CREATE.
2. **Create a view bearing the OLD name**: `CREATE VIEW dbo.Customer AS SELECT AccountId AS Id, AccountName AS Name, … FROM dbo.Account;` — enumerate every old column, aliasing new names back to old so the external contract is byte-for-byte the same shape.
3. **Mark it temporary**: comment the view with the consumers it serves and the sunset trigger ("drop when SSIS package X and report Y have migrated to dbo.Account").
4. **Sunset (a later PR)**: once every external consumer has moved, drop the compat view as a clean subtractive change.

## How it flips (the specifics only)
- **no external consumers** of the old name (all inside the SSDT model) → you do not need the compat view; the refactorlog alone suffices. **M1**, no view.
- **external consumers exist** → ship the compat view alongside the rename (M1 view + the rename's mechanism), **Tier 3**, and schedule the sunset PR.

## Prove it
(a) the rename delta is `sp_rename` not DROP+CREATE (prove the refactorlog is honored — see `../../_index/identity-and-refactorlog/SKILL.md`); (b) `SELECT` from the compat view returns the SAME shape and SAME row hashes as the pre-rename table — hash the old table before, hash the compat view after, prove **equal** (the bridge is transparent). See `prove-on-dacpac` / `talk-to-local-sql`. Exercise with the `Customer` rename scenario; `dbo.vOrderSummary` is the enumerated-view target (VIE-02).

## Verdict to the developer
"I renamed the entity safely (SSDT does an sp_rename, not a drop-and-recreate — I proved that). For the reports and integrations that still ask for the old name, I added a view named `Customer` that reads from the renamed table and returns the exact same shape — proven by matching row hashes. It's marked temporary: once those consumers move, a later PR drops it."

## Teach it (the graduation)
The refactorlog's reach stops at the model boundary, so a rename's true blast radius is everything outside it — the bridge is debt with a sunset date, not a permanent fixture; the fail mode avoided is renaming a table out from under an SSIS/Power BI consumer that still asks for the old name. Full WHY (identity vs. name): `../../_index/identity-and-refactorlog/SKILL.md`.
