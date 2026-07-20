---
name: constraint-is-a-claim
description: Cross-cutting KNOWLEDGE shared by define-PK, create-fk-clean, create-fk-orphan, add-unique, add-check, toggle-trust, and modify-index->unique. Owns the truth that a PK/FK/unique/check is a CLAIM about existing data, proven at APPLY time — the probe PREDICTS (orphan LEFT JOIN, duplicate GROUP BY HAVING, violation WHERE NOT(pred), NULL count) while the Strict publish PROVES. Owns the reconcile-first pattern, the NOCHECK -> reconcile -> WITH CHECK CHECK trust ladder + the is_not_trusted=0 end-state, UNIQUE-allows-one-NULL + the filtered-index remedy, and the discriminator: clean data lands as a single in-place schema change, dirty data ships as a scripted reconcile. Per-op skills POINT here. DISTINCT from tightening-class: this blocks on data-VIOLATION (a value); tightening-class blocks data-BLIND on row-PRESENCE.
---

# A constraint is a claim about existing data — proven at APPLY time

> A PK, FK, UNIQUE, or CHECK is not a passive decoration you *add* — it is a **claim** SQL Server
> **validates against every existing row** the moment you apply it. If the data already breaks the
> claim, the apply **is blocked.** Every constraint op points here so the probe-first reflex and the
> trust ladder are the same, every time.

You are helping an **OutSystems-native developer** who is declaring a key, drawing a reference, or
requiring a value to be unique or within a range. In OutSystems the Identifier was automatic and
References were lines they drew; now the constraint is validated against the **existing rows** at
deploy time — and that validation is exactly what decides whether the change lands clean or is
blocked.

## The core truth

The **same** `ADD CONSTRAINT` lands as a single in-place schema change against data that already
satisfies the claim, and is **blocked** — needing a scripted reconcile that modifies existing data —
against a single row that violates it. The engine is conservative because a constraint is a *promise
about the data it cannot make on data that already breaks it.* Only the data — not the `.sql` —
tells you which.

## The probe-first reflex (predict) vs. the Strict publish (prove)

Run the probe **first** to know what to look for; let the Strict publish **prove** it:

| Op | Probe that PREDICTS a block |
|---|---|
| define-PK | duplicates: `SELECT key, COUNT(*) FROM t GROUP BY key HAVING COUNT(*) > 1`; + NULL count |
| create-fk | orphans: `SELECT c.* FROM child c LEFT JOIN parent p ON p.k = c.fk WHERE p.k IS NULL` |
| add-unique | duplicates: `GROUP BY col HAVING COUNT(*) > 1` |
| add-check | violations: `SELECT COUNT(*) FROM t WHERE NOT (<predicate>)` |

The probe **predicts**; the Strict publish **proves.** A clean probe is *necessary* — but you still
publish, because the publish delta is the honest proof.

## The block's signature (what the refusal actually says)

When the claim is violated, the block is a specific SQL Server error, and the **number names the
cause.** These are the signatures to read in the publish output and carry into the proof packet:

| The op / cause | The block, as SQL Server reports it on publish |
|---|---|
| define-PK · add-unique · modify-index→unique — **duplicate values** | `Msg 1505` — "The CREATE UNIQUE INDEX statement terminated because a duplicate key was found …", naming the duplicate key value (a PK/unique constraint builds a unique index) |
| define-PK — **NULL in a column declared nullable** | `Msg 8111` — "Cannot define PRIMARY KEY constraint on nullable column …". A NOT NULL tightening of a *populated* key column is instead the tightening-class block — `Msg 515` / `Msg 50000` behind the data-loss guard (`../tightening-class/SKILL.md`) |
| add-check — **rows violating the predicate** (added WITH CHECK) | `Msg 547` — "The ALTER TABLE statement conflicted with the CHECK constraint …", naming the table and column |
| create-fk-clean · create-fk-orphan — **orphan children** (added WITH CHECK) | `Msg 547` — "… conflicted with the FOREIGN KEY constraint …", naming the parent table and column |

> **Read the number; it tells you which claim broke.** `Msg 1505` = a duplicate, `Msg 547` = an
> orphan or a failing predicate, `Msg 8111` = a NULL in a key. sqlpackage surfaces the inner SQL
> Server error during publish — commonly wrapped as `SQL72014: .NET SqlClient Data Provider: Msg
> NNNN, Level 16, State S, …` above the `Could not deploy package` line, so parse the **text**, not
> the exit code (a blocked publish does not reliably exit non-zero — see `self-test/PROTOCOL.md`).
> These are engine- and version-bound: **capture the verbatim `Msg` and the offending value from the
> blocked run** (`../../prove-on-dacpac/SKILL.md`'s violating-row probe does exactly this), rather
> than asserting them from memory. This is the value-violation counterpart to tightening-class's
> data-blind `Msg 515` / `Msg 50000` — a different number for a different cause.

## The discriminator: clean data lands in place, dirty data needs a script

- **Data satisfies the claim** → the constraint lands clean → it **ships as a single schema change,
  applied in place, with no data remediation.** A dev lead or an experienced developer should review
  it: inserts and updates are now validated, so the running application must produce conforming data
  — and for a foreign key, a cross-table relationship is added.
- **Data violates the claim** → the apply is **blocked** → **reconcile first**, which changes how it
  ships: a pre-deployment reconcile ships as one release (the pre-deployment script prepares the
  data, then the schema change lands validated); a NOCHECK→reconcile→re-validate orchestration ships
  as a scripted change — it cannot be expressed as a table definition — or ships across multiple
  releases if the reconcile must be staged while the app keeps producing violations. A dev lead must
  review it: existing data is modified.

## The reconcile-first pattern and the trust ladder (FK)

When the data is dirty, the honest path is **reconcile before you claim** — delete the offending
rows, point them at a real parent, insert the missing parents, or fix the failing values. For an FK
whose validation would be blocked, the ladder is:

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

This is the distinction the seed scenarios are built to teach, and it must **not** be collapsed:

- **constraint-is-a-claim** (here) is blocked by an actual data **VIOLATION** — a *value* that breaks
  the rule (a duplicate, an orphan, a failing predicate). The remedy is **reconcile the data**.
- **`../tightening-class/SKILL.md`** is blocked **DATA-BLIND** on row **PRESENCE** — the
  `IF EXISTS(any row) RAISERROR` guard never looks at a value. The remedy is **an empty table, a
  named gate-relaxation, or multi-phase.**

The overlap that fools people: `add-check`, `add-unique`, and `define-PK` can refuse on a *populated
but clean* table (that is tightening-class, row-presence) **and** on *dirty data* (that is this
concern, value-violation). Diagnose which refusal you actually got: a `BlockOnPossibleDataLoss`
`IF EXISTS` guard = tightening-class; a validation failure naming the offending rows/keys = a claim
violation. Different cause, different remedy.

## The ops this governs (and their distinguishing note)

- **define-PK** — a PK is a uniqueness *promise* + a clustered-index build over every row; dirty
  keys (dups/NULLs) block the apply → dedupe first.
- **create-fk-clean** — prove zero orphans → lands clean as a single in-place schema change; the
  most generalizable discriminator in the family.
- **create-fk-orphan** — the NOCHECK→reconcile→WITH CHECK CHECK path; prove `is_not_trusted = 0`.
- **add-unique** — duplicates block the apply; UNIQUE-allows-one-NULL + the filtered-index remedy.
- **add-check** — `WHERE NOT(pred)` violations block the apply; the predicate is the claim.
- **toggle-trust** — OPERATIONAL: `WITH CHECK CHECK` to restore trust, or `NOCHECK` to suspend it;
  the end-state `is_not_trusted` value is the whole point.

## Prove it (pointer, not a re-scaffold)

For the publish loop that proves the block and the clean re-run after reconcile, see
`../../prove-on-dacpac/SKILL.md`; for the orphan / duplicate / violation probes and the
`is_not_trusted` check, see `../../talk-to-local-sql/SKILL.md`.

## Handbook

Cite by **filename**: **08-Referential-Integrity-Basics.md** (FK validation + trust), handbook
**13** (= §16; the operation reference for keys/constraints), and handbook **16** (= §19; §19.2
Optimistic NOT NULL's constraint cousins and §19.3 Forgotten FK Check).
