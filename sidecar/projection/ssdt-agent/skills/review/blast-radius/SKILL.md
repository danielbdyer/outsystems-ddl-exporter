---
name: blast-radius
description: Scope BEFORE judgment — the dependency-closure pass that BOUNDS every review verdict. Use after review-change reproduces a change and before the adversary attacks. Enumerates the dependency closure (FKs in/out, dependent views, procs, indexes, CDC capture instances, external/ETL consumers) and counts affected rows on the isolated DB. Owns the hard invariant: NO verdict may exceed the scope this establishes — an unscoped cascade cannot be BLESSed; an unscoped external consumer forces at least a NAMED-RISK. Produces the "blast map" a REFUSE-ESCALATE carries to the lead. Points to _index/cdc + _index/constraint-is-a-claim + _index/multi-phase for the WHY; owns none of it.
---

# Blast radius (scope before judgment)

> **Why this (and what it teaches).** A verdict is only as trustworthy as the boundary it was drawn
> inside. "Clean Strict publish" proves the schema transition is safe for the *data in this catalog*
> — it says nothing about the view that reads the column, the proc that joins the table, the ETL
> that pulls it nightly, or the CDC instance frozen to the old shape. You cannot BLESS what you never
> mapped. What this teaches: scope is not a courtesy paragraph — it is the **denominator** of every
> verdict, and an unscoped dependency is an unbounded risk wearing a green checkmark.

You run **before** the adversary and **before** the verdict. You produce the **blast map**: the
closure of everything the change touches, plus the row counts. The hard law you own:

> **No verdict may exceed the scope this pass establishes.**
> - A `BLESS` on a change with an un-enumerated cascade is **invalid** — downgrade to at least NAMED-RISK.
> - An un-scoped **external/ETL consumer** forces **at least** BLESS-WITH-NAMED-RISK (the proving
>   ground holds one catalog; cross-DB effects are out of frame — `prove-on-dacpac`'s named edge).
> - A cascade or CDC gap deep enough to be irreversible-adjacent forces **REFUSE-ESCALATE**, and the
>   blast map is the homework you attach.

## The closure to enumerate (on the isolated `$DB`)

For the change's target object(s), enumerate — with the dependency + row-count probe SQL owned by
`talk-to-local-sql` — every edge:

| Dimension | What to enumerate | WHY it bounds the verdict (owner) |
|---|---|---|
| **FKs OUT** | FKs the target column/table participates in as child | a tightening/drop on a child breaks the parent's trust — `_index/constraint-is-a-claim` |
| **FKs IN** | FKs pointing *at* the target as parent (e.g. `OrderLine -> Order`) | a delete-rule/drop cascades inward; count the graph **depth**, not just one edge — `_index/multi-phase` |
| **Dependent views** | views referencing the target (esp. `SELECT *` — `vOrderSummary`) | a column drop/rename can invalidate or silently drift a view — `create-view`/`compat-view` |
| **Procs / functions** | modules joining or selecting the target | out-of-band readers the dacpac won't recompile |
| **Indexes** | indexes on the target column (incl. covering/filtered/unique) | a narrow/drop forces a rebuild; a unique index carries its own veto — `_index/constraint-is-a-claim` |
| **CDC capture instances** | is the target CDC-tracked? which capture instance? | the **+1 tax** on every op touching a CDC table; the instance is frozen to the old shape — `_index/cdc` |
| **External / ETL / cross-DB** | nightly extracts, reports, synonyms, other databases | **cannot be proven here** — the proving ground is one catalog; name it, do not assert it away |
| **Row counts** | rows in the target table; rows the change would touch/lose | the denominator of the magic line ("N rows") and the CONSEQUENCE ORACLE's corpse count |

## The CDC +1 is a scope fact, not a Tier opinion

If the target is CDC-tracked, the capture instance **is in scope** and every op touching it carries
the +1 — this is `_index/cdc`'s law, not a judgment call. A packet claiming Tier 1 on a CDC-tracked
target has a scope error the verdict must correct. You do **not** re-explain the CDC tax (that is
`_index/cdc`'s); you **record the instance as in-scope** and let the verdict apply the +1.

## The external-consumer boundary (name it, never prove it)

Some edges are structurally outside the proving ground: the nightly ETL, a report, a proc in another
database, the running OutSystems app. `prove-on-dacpac` names these as its honest limits. Your job is
to **surface them by name** in the blast map so the verdict can attach the NAMED-RISK — a clean
Strict publish is silent on them, and silence is not safety. "vOrderSummary and the nightly ETL both
read this column; neither is in the dacpac" is a scope finding, and it caps the verdict at NAMED-RISK
unless the lead accepts the out-of-band consumers.

## The blast map (what you hand to the verdict)

A terse structured map, peer-register:

```
BLAST MAP — <op> on <object>
  rows in target:        <n>
  rows change touches:   <n>            (loss / stamp / rebuild)
  FKs in:                <list, with cascade depth>
  FKs out:               <list>
  views:                 <list, flag SELECT *>
  procs:                 <list>
  indexes:               <list, flag unique>
  CDC:                   <instance name | none>   (+1 if present)
  external/ETL (NAMED):  <list — cannot be proven here>
  scope ceiling:         BLESS | NAMED-RISK | ESCALATE   (the max verdict this scope allows)
```

The `scope ceiling` is the invariant made explicit: it is the **highest** verdict the scope permits.
The verdict skill may go **lower** (a caught defect downgrades further) but never **higher**.

## What this skill REUSES (does not rebuild)

- `talk-to-local-sql` — the dependency-closure + row-count probe SQL (sys.foreign_keys, sys.sql_expression_dependencies, sys.indexes, `sp_cdc_help_change_data_capture`, `COUNT(*)`).
- `_index/cdc` — the capture-instance-in-scope + the +1 tax WHY.
- `_index/constraint-is-a-claim` — the FK/trust/index closure WHY.
- `_index/multi-phase` — the cascade + coexistence closure WHY.
- `prove-on-dacpac`'s "HONESTLY CANNOT prove" edges — app impact, prod scale, external consumers — as
  the scope boundary that must be **named, not proven**.

## What this skill deliberately does NOT build

- **No new probe SQL** — every enumeration query is `talk-to-local-sql`'s.
- **No re-explanation of the CDC tax, the constraint claim, or cascade coexistence** — each WHY points
  to its `_index` owner; this skill only records what is *in scope*, not why it matters.
