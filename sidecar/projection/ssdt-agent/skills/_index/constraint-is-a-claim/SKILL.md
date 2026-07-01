---
name: constraint-is-a-claim
description: Cross-cutting KNOWLEDGE shared by define-PK, create-fk-clean, create-fk-orphan, add-unique, add-check, toggle-trust, and modify-index->unique. Owns the truth that a PK/FK/unique/check is a CLAIM about existing data, proven at APPLY time — the probe PREDICTS (orphan LEFT JOIN, duplicate GROUP BY HAVING, violation WHERE NOT(pred), NULL count) while the Strict publish PROVES. Owns the reconcile-first pattern, the NOCHECK -> reconcile -> WITH CHECK CHECK trust ladder + the is_not_trusted=0 end-state, UNIQUE-allows-one-NULL + the filtered-index remedy, and the clean->M1 / dirty->M3/M4 discriminator. Per-op skills POINT here. DISTINCT from tightening-class: this vetoes on data-VIOLATION (a value); tightening-class vetoes data-BLIND on row-PRESENCE.
---

# A constraint is a claim about existing data — proven at APPLY time

> A PK, FK, UNIQUE, or CHECK is not a passive decoration you *add* — it is a **claim** SQL Server
> **validates against every existing row** the moment you apply it. If the data already breaks the
> claim, the apply **vetoes.** Every constraint op points here so the probe-first reflex and the
> trust ladder are the same, every time.

You are helping an **OutSystems-native developer** who is declaring a key, drawing a reference, or
demanding a value be unique / within a range. In OutSystems the Identifier was automatic and
References were lines they drew; now the constraint is validated against the **existing rows** at
deploy time — and that validation is exactly where the bucket flips.

## The core truth

The **same** `ADD CONSTRAINT` is a clean **Mechanism 1** against data that already satisfies the
claim, and a vetoed, script-grade **Tier 3** against a single row that violates it. The engine is
conservative because a constraint is a *promise about the data it cannot make on data that already
breaks it.* Only the data — not the `.sql` — tells you which.

## The probe-first reflex (predict) vs. the Strict publish (prove)

Run the probe **first** to know what to look for; let the Strict publish **prove** it:

| Op | Probe that PREDICTS a veto |
|---|---|
| define-PK | duplicates: `SELECT key, COUNT(*) FROM t GROUP BY key HAVING COUNT(*) > 1`; + NULL count |
| create-fk | orphans: `SELECT c.* FROM child c LEFT JOIN parent p ON p.k = c.fk WHERE p.k IS NULL` |
| add-unique | duplicates: `GROUP BY col HAVING COUNT(*) > 1` |
| add-check | violations: `SELECT COUNT(*) FROM t WHERE NOT (<predicate>)` |

The probe **predicts**; the Strict publish **proves.** A clean probe is *necessary* — but you still
publish, because the delta is the honest oracle.

## The discriminator: clean data → M1, dirty data → M3/M4

- **Data satisfies the claim** → the constraint lands clean → **Mechanism 1 Pure Declarative,
  single-phase.** Tier 2 (contractual — inserts/updates are now validated; an inter-table
  dependency is introduced).
- **Data violates the claim** → the apply **vetoes** → **reconcile first**, which flips the
  mechanism: a pre-deploy reconcile is **Mechanism 3**; a NOCHECK→reconcile→re-validate orchestration
  is **Mechanism 4 Script-Only** (or **Mechanism 5** if the reconcile must be staged across releases
  while the app keeps producing violations). Tier 3 (existing data changes).

## The reconcile-first pattern and the trust ladder (FK)

When the data is dirty, the honest path is **reconcile before you claim** — delete the offending
rows, point them at a real parent, insert the missing parents, or fix the failing values. For an FK
whose validation would veto, the ladder is:

1. `ALTER TABLE ... WITH NOCHECK ADD CONSTRAINT [FK_...]` — adds the constraint but **untrusted**
   (skips validation). `sys.foreign_keys.is_not_trusted = 1`.
2. Reconcile the orphans.
3. `ALTER TABLE ... WITH CHECK CHECK CONSTRAINT [FK_...]` — re-validates and **restores trust**.

**The trap:** stopping at `WITH NOCHECK`. An **untrusted** constraint protects nothing reliably
*and* the optimizer **ignores it** — a guarantee you can't trust is worse than none, because
everyone believes it's there. The `WITH CHECK CHECK` re-validation is **mandatory, not optional
polish.** **The end-state proof is `is_not_trusted = 0`** — prove it, or you have not finished.

## UNIQUE allows exactly one NULL (the filtered-index edge)

A SQL `UNIQUE` constraint treats NULLs as distinct-ish but permits **exactly one** NULL row — which
surprises developers who expect "unique" to also forbid a second NULL, or who want uniqueness only
over non-NULL values. The remedy for "unique among the rows that have a value" is a **filtered
unique index**: `CREATE UNIQUE INDEX ... WHERE col IS NOT NULL`. Name this when the developer's
intent is "no two *filled* values collide."

## NOT the same as tightening-class (keep them separate — deliberately)

This is the distinction the proving ground exists to teach, and it must **not** be collapsed:

- **constraint-is-a-claim** (here) vetoes on an actual data **VIOLATION** — a *value* that breaks
  the rule (a duplicate, an orphan, a failing predicate). The remedy is **reconcile the data**.
- **`../tightening-class/SKILL.md`** vetoes **DATA-BLIND** on row **PRESENCE** — the
  `IF EXISTS(any row) RAISERROR` guard never looks at a value. The remedy is **empty table, or a
  named gate-relaxation, or multi-phase.**

The overlap that fools people: `add-check`, `add-unique`, and `define-PK` can refuse on a *populated
but clean* table (that is tightening-class, row-presence) **and** on *dirty data* (that is this
concern, value-violation). Diagnose which veto you actually got: a `BlockOnPossibleDataLoss`
`IF EXISTS` guard = tightening-class; a validation failure naming the offending rows/keys = a claim
violation. Different veto, different remedy.

## The ops this governs (and their distinguishing note)

- **define-PK** — a PK is a uniqueness *promise* + a clustered-index build over every row; dirty
  keys (dups/NULLs) veto → dedupe first.
- **create-fk-clean** — prove zero orphans → clean M1; the most generalizable discriminator in the
  family.
- **create-fk-orphan** — the NOCHECK→reconcile→WITH CHECK CHECK path; prove `is_not_trusted = 0`.
- **add-unique** — duplicates veto; UNIQUE-allows-one-NULL + the filtered-index remedy.
- **add-check** — `WHERE NOT(pred)` violations veto; the predicate is the claim.
- **toggle-trust** — OPERATIONAL: `WITH CHECK CHECK` to restore trust, or `NOCHECK` to suspend it;
  the end-state `is_not_trusted` value is the whole point.

## Prove it (pointer, not a re-scaffold)

For the publish loop that PROVES the veto and the clean re-run after reconcile, see
`../../prove-on-dacpac/SKILL.md`; for the orphan / duplicate / violation probes and the
`is_not_trusted` check, see `../../talk-to-local-sql/SKILL.md`.

## Handbook

Cite by **filename**: **08-Referential-Integrity-Basics.md** (FK validation + trust), handbook
**13** (= §16; the operation reference for keys/constraints), and handbook **16** (= §19; §19.2
Optimistic NOT NULL's constraint cousins and §19.3 Forgotten FK Check).
