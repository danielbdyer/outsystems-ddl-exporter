---
name: move-schema
description: Use when the developer says "move the entity to the archive schema", "put this table under a different namespace/module", "change its schema" — a schema change on an existing table. The rename trap wearing a different hat.
---

# Move schema (between schemas)

> **Default (provisional — the data decides).** Mechanism 1 Pure Declarative (with refactorlog) OR Mechanism 4 Script-Only (`ALTER SCHEMA TRANSFER`), Tier 3 — cross-boundary references break. Without refactorlog → DROP+CREATE data loss.

## OutSystems phrasing
"move the entity to the archive schema", "put this table under a different namespace/module".

## SSDT meaning
Change the schema in the `CREATE TABLE` header. With a **refactorlog** entry SSDT treats it as a
move; the cleaner, `object_id`-preserving path is a script: `ALTER SCHEMA target TRANSFER
source.Table`. Without refactorlog, SSDT reads it as **drop the old + create the new** — data
loss, the same shape as Naked Rename. Never write `ALTER COLUMN` here.

## The named trap
Same family as **Naked Rename** — no refactorlog → `DROP`+`CREATE`, and every fully-qualified
`dbo.X` reference breaks. This is the identity-vs-name concern — see
`../../_index/identity-and-refactorlog/SKILL.md`, which explicitly names that the two-part
`schema.Table` name is just an address. Do not re-derive it here.

## How it flips (the specifics only)
- refactorlog present, table empty/small → **M1**, Tier 3
- prefer `object_id` preservation / large table → **M4 Script** (`ALTER SCHEMA TRANSFER`), one operation, data untouched
- refactorlog MISSING → **drop+create data loss** — STOP, same remedy as rename (see `../../_index/identity-and-refactorlog/SKILL.md`)
- CDC-enabled → capture instance references the old two-part name → **+1 Tier**, recreate (see `../../_index/cdc/SKILL.md`)

## Prove it
Script the delta. A move must be `sp_rename` (schema-qualified) or your authored `ALTER SCHEMA
TRANSFER` that leaves row counts and `object_id` intact — prove both. A `DROP`+`CREATE` in the
delta is the catastrophe signal. For the publish loop, see `../../prove-on-dacpac/SKILL.md`.

## Verdict to the developer
"Moving the entity to another schema is the same trap as a rename: with the refactorlog SSDT
keeps the data, without it SSDT drops and recreates. I proved the move with `ALTER SCHEMA
TRANSFER` — row counts unchanged. Tier 3 because every `schema.Table` reference has to follow."

## Teach it (the graduation)
Identity is separate from name — the two-part name is an address (see
`../../_index/identity-and-refactorlog/SKILL.md`). Fail mode avoided: relearning the DROP+CREATE
proof per operation instead of recognizing "this is the rename trap again."
