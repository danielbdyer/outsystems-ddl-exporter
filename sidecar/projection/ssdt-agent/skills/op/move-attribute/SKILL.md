---
name: move-attribute
description: Use when the developer says "move the Region attribute from Customer to Account", "this field is on the wrong entity", "relocate DiscountRate to the parent" — a single attribute moving from one entity to another, with data. SSDT destination = a multi-phase add-column-to-destination / copy / repoint / drop-from-source program.
---

# Move an attribute between entities (relationship-ambiguity / Naked-Rename trap)

> **Default (provisional — the data decides).** Mechanism 5 Multi-Phase, multi-PR, Tier 3 when the source is populated — but PROVE the join is 1:1 first.

## OutSystems phrasing
"move the Region attribute from Customer to Account", "this field is on the wrong entity", "relocate DiscountRate to the parent".

## SSDT meaning
A one-column split. ADD the column to the destination CREATE, copy the values keyed by whatever relationship links the two entities, repoint readers, then drop the column from the source CREATE. SSDT ADDs declaratively; the copy is post-deploy; the source-column drop is a `BlockOnPossibleDataLoss` veto. Never write ALTER.

## The named trap
**Relationship ambiguity** — the copy needs a join key, and if the relationship is not 1:1 the value is ambiguous (which Customer's Region wins for an Account with many Customers?). Same collision shape as `../merge-tables/SKILL.md`. Second trap: *Naked Rename* if the agent treats "move" as a rename and lets SSDT DROP+CREATE — a **cross-table move has NO refactorlog identity mapping**, so it must be copy-then-drop, never a rename. See `../../_index/identity-and-refactorlog/SKILL.md`. The coexistence WHY is `../../_index/multi-phase/SKILL.md`. Do not re-derive either.

## How it flips (the specifics only)
- source column empty / all-NULL → drop from source as **M3** and add to destination as **M1**, single-PR each. No data to move.
- source populated, **1:1 relationship** → **M5 Multi-Phase, multi-PR**. Tier 3.
- relationship **not 1:1** → STOP; ambiguous, a design decision, not a flip.
- **+ CDC / >1M rows / first-time** → +1 Tier — see `../../_index/cdc/SKILL.md`.

## Prove it
Prove the join is 1:1 (count check), then hash the moving values source-vs-destination and prove **equal** before the source-column drop. Strict must veto the drop until equal. See `prove-on-dacpac` / `talk-to-local-sql`. On the sample, move `Customer.Region` to `Account` via the seeded 1:1 `Customer.AccountId` (STR-03).

## Verdict to the developer
"Moving Region from Customer to Account is a column moving between tables — I proved the relationship is 1:1 so the values aren't ambiguous, copied them (hashes match), and SSDT vetoes the source drop until that's proven. Multi-PR."

## Teach it (the graduation)
The word the developer uses ("move") does not determine the mechanism; the *relationship between the two tables* does, and only the data tells you whether it's 1:1 — a move crosses tables and has no identity mapping, so it's copy-then-drop, never a rename. Full WHY: `../../_index/multi-phase/SKILL.md` + `../../_index/identity-and-refactorlog/SKILL.md`.
