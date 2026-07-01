---
name: toggle-trust
description: Use when the developer says "trust the constraint now that the data is clean", "turn the FK back on", "temporarily disable the check so this load can run" — toggling a constraint's enforcement/trust state. OPERATIONAL, NOT declarative; refuse-and-route to a script, prove it ends TRUSTED.
---

# Enable / disable constraint trust — ⚠️ OPERATIONAL, NOT DECLARATIVE

> **Default (provisional).** NOT a declarative mechanism — operational / script-only work. There is no bucket; the correct output routes it to a pre/post-deploy script (or runbook) and proves the *ending* trust state.

## OutSystems phrasing
"trust the constraint now that the data is clean", "turn the FK back on", "temporarily disable the check so this load can run".

## SSDT meaning
`ALTER TABLE … WITH CHECK CHECK CONSTRAINT <name>` (re-validate and mark trusted) / `ALTER TABLE … NOCHECK CONSTRAINT <name>` (stop enforcing). This toggles the runtime **enforcement/trust state** of an existing constraint — **not a change to the described destination**. Handbook file 15 = §18.3: "Enable/disable constraint → ❌ No — Script-only (operational, not declarative)."

## The named trap
An **untrusted constraint** — left in NOCHECK, the constraint exists but the optimizer ignores it (`is_not_trusted = 1`), so it neither enforces nor helps plans. The pairing to remember is the FK-with-orphans remedy: `NOCHECK` → reconcile → `WITH CHECK CHECK` to re-trust (see `../create-fk-orphan/SKILL.md` and `../../_index/constraint-is-a-claim/SKILL.md` for the trust ladder). This operational-not-declarative one-liner is shared with `../rebuild-index/SKILL.md` but not lifted (two ops — below the N≥3 bar).

## How it flips (the specifics only)
It does not flip between declarative SSDT buckets — it is outside the declarative model. As a script step within a larger change it inherits that change's tier (e.g. the FK-with-orphans remedy is Tier 3). **Always flag it as operational / script-only.**

## Prove it
The proof is the **trust state before and after**. Snapshot `SELECT name, is_disabled, is_not_trusted FROM sys.check_constraints` (and `sys.foreign_keys`) before and after your script. The pass condition is `is_not_trusted = 0` **after** the `WITH CHECK CHECK` — proving the constraint ends trusted, not merely present. Ending at `is_not_trusted = 1` is the failure to catch here, not in prod. See `../../prove-on-dacpac/SKILL.md` + `../../talk-to-local-sql/SKILL.md`.

## Verdict to the developer
"Toggling whether a constraint is trusted isn't a schema change — it's an operational step that lives in a script, not the SSDT project. I'll wire it as disable → clean the data → re-trust WITH CHECK CHECK, and I'll prove the constraint ends up TRUSTED, because a left-untrusted constraint silently stops protecting you."

## Teach it (the graduation)
When a request is about *transitioning* enforcement rather than *declaring* the resting shape, it belongs in a script, and the proof is the *after* state (`is_not_trusted = 0`), not the act. Fail mode avoided: leaving a permanently untrusted constraint that guards nothing.
