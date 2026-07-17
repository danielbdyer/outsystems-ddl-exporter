---
name: toggle-trust
description: Use when the developer says "trust the constraint now that the data is clean", "turn the FK back on", "temporarily disable the check so this load can run" — toggling a constraint's enforcement/trust state. OPERATIONAL, NOT declarative; refuse-and-route to a script, prove it ends TRUSTED.
---

# Enable / disable constraint trust — ⚠️ OPERATIONAL, NOT DECLARATIVE

> **Default (provisional).** Not a declarative change: toggling a constraint's enforcement/trust
> state is operational, script-only work against an existing constraint, not a shape SSDT converges
> to, so it does not ship as a table definition. There is no schema disposition to assign — the
> correct outcome routes it to a pre/post-deployment script (or a runbook). Ships as a scripted
> change; who must review is inherited from the change this step serves. Prove the *ending* trust
> state before classifying it as a schema change.

## OutSystems phrasing
"trust the constraint now that the data is clean", "turn the FK back on", "temporarily disable the
check so this load can run".

## SSDT meaning
`ALTER TABLE … WITH CHECK CHECK CONSTRAINT <name>` (re-validate and mark trusted) / `ALTER TABLE …
NOCHECK CONSTRAINT <name>` (stop enforcing). This toggles the runtime **enforcement/trust state** of
an existing constraint — **not a change to the described destination**. Handbook file 15 = §18.3:
"Enable/disable constraint → ❌ No — Script-only (operational, not declarative)."

## The named trap
An **untrusted constraint** — left in NOCHECK, the constraint exists but the optimizer ignores it
(`is_not_trusted = 1`), so it neither enforces nor helps plans. The pairing to remember is the
FK-with-orphans remedy: `NOCHECK` → reconcile → `WITH CHECK CHECK` to re-trust (see
`../create-fk-orphan/SKILL.md` and `../../_index/constraint-is-a-claim/SKILL.md` for the trust
ladder). This operational-not-declarative one-liner is shared with `../rebuild-index/SKILL.md` but
not lifted (two ops — below the N≥3 bar).

## How it flips (the specifics only)
It does not flip between declarative SSDT buckets — it is outside the declarative model. As a script
step within a larger change, its review need is inherited from that change: the FK-with-orphans
remedy, for example, needs a dev lead, because existing data is reconciled. **Always flag it as
operational / script-only.**

## Prove it
The proof is the **trust state before and after**. Snapshot `SELECT name, is_disabled,
is_not_trusted FROM sys.check_constraints` (and `sys.foreign_keys`) before and after the script. The
pass condition is `is_not_trusted = 0` **after** the `WITH CHECK CHECK` — proving the constraint
ends trusted, not merely present. Ending at `is_not_trusted = 1` is the failure to catch on the
disposable copy, not in production. See `../../prove-on-dacpac/SKILL.md` +
`../../talk-to-local-sql/SKILL.md`.

## The verdict (to the developer)
Toggling whether a constraint is trusted isn't a schema change — it's an operational step that lives
in a script, not the SSDT project. I'll wire it as disable, clean the data, then re-trust with
`WITH CHECK CHECK`, and prove the constraint ends up trusted — because a constraint left untrusted
still exists but silently stops protecting you, and the optimizer ignores it for plans too.

## The reasoning (in conversation)
The tell is whether the request is about *changing* enforcement or *declaring* the resting shape.
When it's a transition — turn enforcement off, reconcile, turn it back on — it belongs in a script,
and the proof that matters is the after-state (`is_not_trusted = 0`), not that the command ran. The
failure this avoids: a constraint left permanently untrusted — present in the schema, but guarding
nothing and invisible to the optimizer.

## On the record

The fragment this op contributes to the pull request (`../../author-pr/SKILL.md`), record register.
As an operational step it usually rides inside a larger change (the FK-with-orphans remedy); these
are its own lines.

**Review & release**
- Ships as a scripted change — a constraint's enforcement/trust state cannot be expressed as a table
  definition; it lives in a pre/post-deployment script or a runbook.
- The review need is inherited from the change this step serves. Where it re-trusts a constraint
  after reconciling data (the FK-with-orphans remedy): a dev lead must review this, because existing
  data is modified.

**Verification** — run in each environment after deployment
```sql
-- expect is_not_trusted = 0: the constraint ends trusted, not merely present
SELECT name, is_disabled, is_not_trusted FROM sys.check_constraints WHERE name = '<name>';
-- (and sys.foreign_keys for a foreign key)
```

**Rollback**
Re-running `ALTER TABLE … NOCHECK CONSTRAINT <name>` stops enforcement again, but a constraint
returned to NOCHECK ends untrusted (`is_not_trusted = 1`) and guards nothing. Backing out a re-trust
removes protection rather than restoring a safe prior state; the resting state to return to is the
untrusted one.

**Not verified**
- Other environments. The ending trust state is proven on a disposable copy of Dev only. A
  `WITH CHECK CHECK` that meets violating data in another environment leaves the constraint untrusted
  (`is_not_trusted = 1`) — re-probe `is_not_trusted` after the script runs in each environment before
  relying on it.
- Reversibility. Only the forward move to trusted is exercised; backing it out with `NOCHECK`
  returns the constraint to the untrusted, unenforced state, which is not a safe resting state.
