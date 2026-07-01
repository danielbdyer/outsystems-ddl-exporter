---
name: create-static-seed
description: Use when the developer says "add a Status lookup", "create a static entity for order types", "I need a reference table with these fixed values", "a lookup entity with Active/Inactive" — a new OutSystems Static Entity whose rows are part of the model. SSDT destination = a declarative CREATE TABLE plus an idempotent MERGE seed in the post-deploy script.
---

# Create static / lookup entity (non-idempotent-seed + IDENTITY-lookup traps)

> **Default (provisional — the data decides).** Mechanism 2 Declarative + Post-Deploy, single-PR, Tier 1 — but PROVE the redeploy is silent.

## OutSystems phrasing
"add a Status lookup", "create a static entity for order types", "a reference table with these fixed values", "a lookup with Active/Inactive".

## SSDT meaning
A declarative `CREATE TABLE` for the lookup (schema slot) PLUS a seed in `Script.PostDeployment.sql` (data slot), usually `:r`-including `Data/Seed.sql`. The CREATE is pure structure; the rows are an idempotent MERGE. Lookup keys are **explicit IDs, NOT IDENTITY** — the IDs are part of the model and must be identical in every environment so the app can reference `StatusId = 3` by constant. Never write ALTER.

## The named trap
Two named traps, both owned elsewhere: the **non-idempotent seed** (a bare INSERT that duplicate-keys on the second deploy) and the **IDENTITY lookup** (auto-assigned IDs drift between environments). Both are the idempotent-seed concern — see `../../_index/idempotent-seed/SKILL.md`; do not re-derive the guarded-MERGE or explicit-ID reasoning here.

## How it flips (the specifics only)
- fresh lookup, explicit IDs, guarded MERGE → M2, single-PR, Tier 1.
- lookup is an **FK target** for other entities → seed the lookup **before** its children (parents-first); still M2, but a missing parent row makes a child FK veto.
- **+ CDC-tracked** (table or downstream consumer) → the `WHEN MATCHED` MUST be guarded or the redeploy over-captures — see `../../_index/cdc/SKILL.md`. +1 Tier.
- **+ >1M reference rows** (rare) → +1 Tier for the data operation.

## Prove it
Deploy once (seed lands), then **deploy a SECOND time unchanged** and assert the post-deploy reports **0 rows affected** + an **identical data-hash** (order-independent `SHA2_256(FOR XML RAW)` sum). If CDC-tracked, assert the second deploy captures **0** rows. A changed hash on an unchanged seed means the MERGE is rewriting rows — fix the guard. See `prove-on-dacpac` for the publish loop and `talk-to-local-sql` for the hash probe. On the enriched sample, `dbo.Category` (explicit-id, `IsActive DEFAULT 1`) is the ready-made seed target.

## Verdict to the developer
"I created the Status lookup as a declarative table plus an idempotent seed with explicit IDs so they match across every environment. I proved it: the second deploy touched zero rows, so redeploys are silent and safe."

## Teach it (the graduation)
For reference data, *re-running changes nothing* is the correctness property, and a silent second deploy is how you prove it — the fail mode avoided is a seed that rewrites rows on every deploy (broken even when the values match). Full WHY: `../../_index/idempotent-seed/SKILL.md`.
