---
name: add-default
description: Use when the developer says "give this attribute a default value", "new rows should default to Active", "everything new should start as Pending" — adding a named DEFAULT constraint. Always Pure Declarative; it fills only NEW rows and never backfills existing ones.
---

# Add a default

> **Default (provisional — the data decides).** Mechanism 1 Pure Declarative, single-phase, Tier 1 in any state — adding a default never touches existing row values.

## OutSystems phrasing
"give this attribute a default value", "new rows should default to Active", "everything new should start as Pending".

## SSDT meaning
A named default constraint on the column — `CONSTRAINT DF_<Table>_<Col> DEFAULT (<value>) FOR <Col>` (or inline). SSDT emits `ADD CONSTRAINT`. It affects **future inserts only** — it does NOT backfill existing rows.

## The named trap
The **unnamed default**: if you let SSDT auto-name (`DF__Table__Col__<hash>`), the name differs per environment and diffing/refactoring becomes fragile — always name it `DF_<Table>_<Col>`. Second surprise: a developer who expects existing NULLs to fill in is wrong — the default touches only new rows; the backfill is a separate op (see `../make-mandatory/SKILL.md` for the NOT-NULL-with-backfill path).

## How it flips (the specifics only)
- add a default → M1, single-phase, Tier 1 (any state).
- developer ALSO wants existing rows backfilled → a separate op: M2 Declarative+Post-Deploy (idempotent UPDATE — see `../../_index/idempotent-seed/SKILL.md`), or M3 if the column is becoming NOT NULL (see `../make-mandatory/SKILL.md`). The default itself stays Tier 1.
- \+ CDC-enabled → CDC does not track constraints (handbook file 15 = §18.5); no +1 from CDC for the default alone.

## Prove it
Build + Strict `sqlpackage /Action:Script`; confirm the delta is a clean `ALTER TABLE … ADD CONSTRAINT DF_…` with **no UPDATE of existing rows** — that absence *is* the proof the default does not backfill. See `../../prove-on-dacpac/SKILL.md` + `../../talk-to-local-sql/SKILL.md`.

## Verdict to the developer
"Adding the default is a pure declarative change — I published it to a copy of your data and SSDT just added the constraint, no existing rows touched. Note: the default only fills NEW rows; if you also want the existing blanks filled, that's a separate backfill step I can prove too."

## Teach it (the graduation)
Distinguish a rule about *new* writes (free, declarative) from a change to *existing* values (a separate, proven backfill) — they look like one request and are two. Fail mode avoided: being surprised by the still-blank column after deploy.
