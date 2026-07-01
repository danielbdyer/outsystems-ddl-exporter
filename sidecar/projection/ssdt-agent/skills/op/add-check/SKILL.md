---
name: add-check
description: Use when the developer says "Total must be positive", "Status has to be one of these values", "age can't be negative" — any business rule enforced at the data layer via a CHECK constraint. Validates every existing row at deploy; violating rows veto; NOCHECK is a trap.
---

# Add a check constraint

> **Default (provisional — the data decides).** Mechanism 1 Pure Declarative, single-phase, Tier 2 when all existing rows satisfy the predicate — but PROVE zero violations first.

## OutSystems phrasing
"Total must be positive", "Status has to be one of these values", "age can't be negative" — any business rule the developer wants enforced at the data layer.

## SSDT meaning
`CONSTRAINT CK_<Table>_<Col> CHECK (<predicate>)`. SSDT adds the constraint **WITH CHECK** by default, validating **every existing row** at deploy.

## The named trap
**Existing rows that violate the predicate veto the deploy.** The escape hatch — **WITH NOCHECK** — is its own trap: it skips validation but leaves the constraint **untrusted** (`is_not_trusted = 1`), so the optimizer ignores it and bad rows can already be present. Both are the constraint-is-a-claim family (violation-on-a-value veto + the trust ladder) — see `../../_index/constraint-is-a-claim/SKILL.md`; do not re-derive the claim or NOCHECK mechanics here.

## How it flips (the specifics only)
- all existing rows satisfy the predicate → M1, single-phase, Tier 2 (prove it).
- violating rows PRESENT → **FLIP to M3 Pre-Deploy+Declarative, single-PR**: pre-deploy fix-up brings violators into compliance BEFORE the WITH CHECK validation, Tier 3 — see `../../_index/constraint-is-a-claim/SKILL.md`.
- violating rows that CANNOT be fixed in-place (legitimate legacy data) → consider M5 Multi-Phase (quarantine / grandfather — see `../../_index/multi-phase/SKILL.md`), or accept WITH NOCHECK *only* as a named, documented, explicitly-untrusted decision.
- \+ >1M rows / first-time → **+1 Tier**.
- \+ CDC-enabled → apply the team's +1 rule (see `../../_index/cdc/SKILL.md`).

## Prove it
Run the violation probe FIRST: `SELECT COUNT(*) FROM <table> WHERE NOT (<predicate>)`. Then build + Strict publish (adds WITH CHECK): clean → zero violations; a build failure ("conflicted with the CHECK constraint") is the veto. Author the pre-deploy fix-up, re-run Strict clean. If anyone proposes WITH NOCHECK, prove the cost: `SELECT is_not_trusted FROM sys.check_constraints WHERE name='CK_…'` returns 1. See `../../prove-on-dacpac/SKILL.md` + `../../talk-to-local-sql/SKILL.md`.

## Verdict to the developer
"You asked to enforce Total > 0. I published the check to a copy of your data and SSDT refused — 7 existing rows have Total of 0 or less. This is a Pre-Deploy + Declarative change: I fix those 7 rows first, then the check validates clean and stays TRUSTED. (We could skip validation with NOCHECK, but then the rule is untrusted and the optimizer ignores it — I don't recommend it.)"

## Teach it (the graduation)
Run the violation probe (`WHERE NOT (<predicate>)`) first, and treat NOCHECK as a debt (prove the untrusted state, end trusted) rather than a shortcut. See `../../_index/constraint-is-a-claim/SKILL.md`. Fail mode avoided: "just add the rule" over legacy data that violates it.
