---
name: create-fk-orphan
description: Use when the developer says "add a reference to Customer" but the data is dirty — some child rows point at parents that do not exist (orphans). The NOCHECK → reconcile → WITH CHECK CHECK script path that ends with a TRUSTED foreign key.
---

# Create a foreign key with orphans (NOCHECK → reconcile → WITH CHECK CHECK)

> **Default (provisional — the data decides).** Mechanism 4 Script-Only, single-PR, Tier 3 — the orphans force a reconcile before the constraint can be honest. Flips to Mechanism 5 Multi-Phase if the reconcile must stage across releases.

## OutSystems phrasing
Same as create-fk-clean ("add a reference to Customer", "Order belongs to a Customer"), but some child rows point at parents that do not exist.

## SSDT meaning
The clean declarative FK would veto. The script path: add the constraint `WITH NOCHECK` (constraint exists but **untrusted**) → reconcile the orphans (delete them, repoint them, or insert the missing parents) → `ALTER TABLE ... WITH CHECK CHECK CONSTRAINT [FK_...]` to validate and **restore trust** so the optimizer honors it.

## The named trap
Stopping at `WITH NOCHECK` — the constraint is present but **untrusted** (`is_not_trusted = 1`), protecting nothing and ignored by the optimizer. The `WITH CHECK CHECK` re-validation is mandatory. This is the NOCHECK→reconcile→re-trust ladder owned by the constraint-is-a-claim family — see `../../_index/constraint-is-a-claim/SKILL.md`; do not re-derive the trust mechanics here.

## How it flips (the specifics only)
- orphans reconcilable in one release → M4 Script-Only, single-PR, Tier 3.
- reconcile must wait on an app change (orphans still being created) → M5 Multi-Phase, coexistence concern (see `../../_index/multi-phase/SKILL.md`).
- orphan reconcile **deletes** child rows → data loss → Tier 4 consideration.
- CDC-enabled / >1M rows → **+1 Tier** (see `../../_index/cdc/SKILL.md`).

## Prove it
First prove the **veto** the clean FK produces (orphan count via `LEFT JOIN ... WHERE p.<pk> IS NULL`). Then prove the full script on the throwaway DB: `NOCHECK` adds the constraint untrusted (`SELECT is_not_trusted FROM sys.foreign_keys WHERE name='FK_...'` → 1), the reconcile clears the orphans, and `WITH CHECK CHECK` flips it back to trusted (`is_not_trusted = 0`) with no veto. That trusted re-validation is the proof. See `../../prove-on-dacpac/SKILL.md` + `../../talk-to-local-sql/SKILL.md`. Seed: the `Order.CustomerId=999` orphan drives the whole sequence.

## Verdict to the developer
"The reference vetoed on a copy — 8 Orders point at Customers that don't exist. So it's a Script-Only change this release: add the constraint without checking, fix the 8 orphans, then re-validate to make it trusted. I proved the whole sequence ends with a trusted foreign key. Tier 3 because we're changing existing data."

## Teach it (the graduation)
"The constraint exists" is not "the constraint is trusted" — after any NOCHECK shortcut, prove `is_not_trusted = 0` or you haven't finished. See `../../_index/constraint-is-a-claim/SKILL.md`. Fail mode avoided: shipping a silent untrusted FK that guards nothing.
