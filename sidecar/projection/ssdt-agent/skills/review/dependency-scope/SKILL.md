---
name: dependency-scope
description: Scope before judgment — the dependency-closure pass that bounds every review disposition. Use after review-change reproduces a change and before the adversary attacks. Enumerates the dependency closure (FKs in/out, dependent views, procs, indexes, CDC capture instances, external/ETL consumers) and counts affected rows on the isolated DB. Owns the hard invariant: no disposition may exceed the scope this establishes — an unscoped cascade cannot be approved; an unscoped external consumer forces at least Approved with a named risk. Produces the dependency map an escalation carries to the lead. Points to _index/cdc + _index/constraint-is-a-claim + _index/multi-phase for the reasoning; owns none of it.
---

# Dependency scope (scope before judgment)

> **Why this.** A disposition is only as trustworthy as the boundary it was drawn inside. A clean
> Strict publish proves the schema transition is safe for the *data in this catalog* — it says
> nothing about the view that reads the column, the proc that joins the table, the ETL that pulls it
> nightly, or the CDC instance frozen to the table's current columns. A change that was never mapped
> cannot be approved. Scope is not a courtesy paragraph — it is the denominator of every disposition,
> and an unscoped dependency is an unbounded risk under a clean result.

This pass runs **before** the adversary and **before** the verdict. It produces the **dependency
map**: the closure of everything the change touches, plus the row counts. The hard invariant it owns:

> **No disposition may exceed the scope this pass establishes.**
> - Approved on a change with an un-enumerated cascade is invalid — it drops to at least Approved
>   with a named risk.
> - An un-scoped **external / ETL consumer** forces **at least** Approved with a named risk (the
>   disposable copy holds one catalog; cross-database effects are out of frame — `prove-on-dacpac`'s
>   named edge).
> - A cascade or CDC gap deep enough to be irreversible-adjacent forces **Escalated — one question
>   for the lead**, and the dependency map is the homework attached to it.

## The closure to enumerate (on the isolated `$DB`)

For the change's target object(s), enumerate — with the dependency + row-count probe SQL owned by
`talk-to-local-sql` — every edge:

| Dimension | What to enumerate | Why it bounds the disposition (owner) |
|---|---|---|
| **FKs OUT** | FKs the target column/table participates in as child | a tightening/drop on a child breaks the parent's trust — `_index/constraint-is-a-claim` |
| **FKs IN** | FKs pointing *at* the target as parent (e.g. `OrderLine -> Order`) | a delete-rule/drop cascades inward; count the graph **depth**, not just one edge — `_index/multi-phase` |
| **Dependent views** | views referencing the target (esp. `SELECT *` — `vOrderSummary`) | a column drop/rename can invalidate or silently drift a view — `create-view`/`compat-view` |
| **Procs / functions** | modules joining or selecting the target | out-of-band readers the dacpac won't recompile |
| **Indexes** | indexes on the target column (incl. covering/filtered/unique) | a narrow/drop forces a rebuild; a unique index can block the deployment on its own — `_index/constraint-is-a-claim` |
| **CDC capture instances** | is the target CDC-tracked? which capture instance? | added scrutiny on every op touching a CDC table; the capture instance is frozen to the table's current columns — `_index/cdc` |
| **External / ETL / cross-DB** | nightly extracts, reports, synonyms, other databases | **cannot be proven here** — the disposable copy is one catalog; name it, do not assert it away |
| **Row counts** | rows in the target table; rows the change would touch/lose | the denominator of every "N rows" finding, and the count of rows a subtractive change is observed to lose on the disposable copy |

## CDC tracking is a scope fact, not a judgment call

If the target is CDC-tracked, the capture instance **is in scope**, and the added scrutiny every op
touching it carries is a scope fact, not a judgment call — this is `_index/cdc`'s finding. A packet
that reports no added scrutiny on a CDC-tracked target has a scope error the disposition must
correct. This pass does **not** re-explain why CDC constrains the change (that is `_index/cdc`'s); it
**records the capture instance as in-scope** and lets the verdict apply the added scrutiny.

## The external-consumer boundary (name it, never prove it)

Some edges are structurally outside the disposable copy: the nightly ETL, a report, a proc in another
database, the running OutSystems app. `prove-on-dacpac` names these as its honest limits. This pass
**surfaces them by name** in the dependency map so the verdict can attach the named risk — a clean
Strict publish is silent on them, and silence is not safety. "The view vOrderSummary and the nightly
ETL both read this column; neither is in the dacpac" is a scope finding, and it caps the disposition
at Approved with a named risk unless the lead accepts the out-of-band consumers.

## The dependency map (what feeds the verdict)

A terse structured map, in the record register:

```
DEPENDENCY MAP — <op> on <object>
  rows in target:        <n>
  rows change touches:   <n>            (loss / stamp / rebuild)
  FKs in:                <list, with cascade depth>
  FKs out:               <list>
  views:                 <list, flag SELECT *>
  procs:                 <list>
  indexes:               <list, flag unique>
  CDC:                   <instance name | none>   (added scrutiny if present)
  external/ETL (named):  <list — cannot be proven here>
  scope ceiling:         Approved | Approved with a named risk | Escalated
                                                (the highest disposition this scope allows)
```

The `scope ceiling` is the invariant made explicit: it is the **highest** disposition the scope
permits. The verdict skill may go **lower** (a caught defect drops it further) but never **higher**.

## What this skill reuses (does not rebuild)

- `talk-to-local-sql` — the dependency-closure + row-count probe SQL (sys.foreign_keys, sys.sql_expression_dependencies, sys.indexes, `sp_cdc_help_change_data_capture`, `COUNT(*)`).
- `_index/cdc` — why the capture instance is in scope and adds scrutiny.
- `_index/constraint-is-a-claim` — the FK / trust / index closure reasoning.
- `_index/multi-phase` — the cascade + coexistence closure reasoning.
- `prove-on-dacpac`'s named limits — app impact, production scale, external consumers — as the scope
  boundary that must be **named, not proven**.

## What this skill deliberately does not build

- **No new probe SQL** — every enumeration query is `talk-to-local-sql`'s.
- **No re-explanation of why CDC adds scrutiny, the constraint claim, or cascade coexistence** — each
  reason points to its `_index` owner; this skill only records what is *in scope*, not why it matters.
