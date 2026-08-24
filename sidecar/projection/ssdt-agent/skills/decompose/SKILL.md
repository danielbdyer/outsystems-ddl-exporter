---
name: decompose
description: Use FIRST when one request needs several schema changes at once — "stand up returns and clean up the customer model", a feature that adds a few tables, refactors one, and drops some columns. Breaks a compound need into the minimum set of well-separated pull requests, each a sequence of catalog operations in the right order, then hands each operation to confirm-intent. Use before confirm-intent, which handles one operation at a time.
---

# Decompose a compound change

> **The move.** A real request is rarely one operation. It is a *need* — "add returns", "clean up
> the customer model" — that entails several: new tables, a refactor, columns added and dropped. This
> skill turns that need into the **fewest pull requests that still separate the concerns cleanly**,
> each a correctly ordered sequence of catalog operations. It does not prove anything and it does not
> edit SQL. It scopes the work so the normal flow — confirm-intent, then change-author proving each
> operation — can run on one atom at a time.

You are helping an OutSystems-native developer who has described a whole feature, not a single edit.
Do not try to author it as one giant change. Decompose it first.

## The one idea

A compound change is an **expand → migrate → contract** at the whole-feature scale, exactly as a
single multi-phase operation (`../_index/multi-phase/SKILL.md`) is at the operation scale. Everything
additive can happen first and can be batched. Every data move happens in the middle, in dependency
order. Every drop happens last, after the things that depend on it are gone. The locked gate
(`../../FINDINGS_AND_CHANGES.md` Part 1) forces any tightening or drop on populated data into its own
two-release. Decomposition is packing the atoms into the fewest releases that honor that shape, and
splitting them into the fewest pull requests that keep unrelated concerns apart.

## The method, in five steps

**1. Enumerate the atoms.** List every catalog operation the need entails, each as an op-slug with
its object(s). Every atom must be one of the 41 operations — if something does not map to an op, that
is a finding to raise, not an operation to invent. (Name each atom in the developer's words too, so
the plan is legible: "a Return table", not only `create-entity`.)

**2. Classify each atom's shape** — from its op skill's SHIP terminal, not from the text. Three
classes decide the packing:
- **Additive** (in place, no data touched, or a new NOT NULL column with a DEFAULT that backfills):
  `add-optional`, `create-entity`, `create-static-seed`, `add-index`, a clean `create-fk` over an
  empty or clean child, `add-mandatory` with a default. These can share a release.
- **Tightening or destructive on populated data**: `make-mandatory`/`narrow` on rows,
  `delete-attribute`, `delete-entity`, `add-unique`/`add-check` over dirty data. The locked gate
  forces each into a two-release (or a scripted release for a table drop).
- **Multi-phase programs**: `extract-to-lookup`, `merge-tables`, `split-table`, `move-attribute`,
  `retype-explicit`, `temporal-convert`, `archive-entity`. Each already carries its own
  expand/migrate/contract sequence (`../_index/multi-phase/SKILL.md`); treat the whole program as
  one atom here and let its own skill sequence its phases.

**3. Draw the dependency edges — and only the ones that cross a release.** Inside a single publish,
DacFx orders object creation itself (it creates a lookup table before the foreign key that references
it), so intra-release ordering is not yours to plan. What you plan is what crosses releases or moves
data:
- a backfill can only run after the column it fills exists and the rows it reads from are present;
- a drop can only run after every object and every application path that depends on it is gone;
- a foreign key to a new lookup can only be trusted after the lookup is seeded.
These edges give the order within a concern.

**4. Cluster into concerns — the connected components of the coupling graph.** Two atoms are coupled
when they share an object or a data flow (Return references OrderLine; the Region lookup backfills a
Customer column). Follow the couplings; each connected group is one **concern**, and one concern is
one pull request — coherent, reviewable on its own, revertible on its own. Atoms in different groups
are independent: they go in separate pull requests and need not wait on each other.

One caution the object graph alone misses: **two multi-phase reshapes of the same table contend even
when they touch different columns**, because each keeps that table's definition lagging its database
for a window, and a second publish in that window reverts the first (the concurrent-publish hazard).
So treat "reshapes the same table" as a coupling too: either serialize those concerns (one merges
fully before the next starts) or combine them into one concern. Do not run them in overlapping
windows.

**5. Pack each concern into the fewest releases, and order the concerns.** Within a concern:
- put every additive atom in the **first release** (the expand);
- run the data moves next, in the dependency order from step 3 (the migrate);
- put every drop **last** (the contract), each as the two-release its op requires;
- fold a trivial additive atom into an existing additive release as a free rider rather than giving
  it its own pull request, when it shares that concern's table.

Across concerns: independent concerns can proceed in parallel; concerns that contend on a shared
table are serialized (step 4). The result is the minimum number of moves — nothing is staged that the
gate does not force, and nothing independent is serialized that need not be.

## What decompose produces

A **decomposition plan**, in the record register (`../../THE_RECORD.md`): the need in one line, then
one block per pull request, each naming its concern, its atoms as op-slugs in release order, and its
release count with the reason for any staging. A multi-release or multi-phase concern also opens a row
in `../../estate/in-flight.md`, so a later phase is not forgotten. Then hand each atom, in order, to
`../confirm-intent/SKILL.md` — decompose plans, confirm-intent scopes one operation, change-author
proves it. Decomposition never replaces proving; it feeds it.

## Worked example

**The need (one PBI):** "Stand up returns and clean up the customer model. Customers can return order
lines with a reason, and we want returns reporting. Also: the customer Region is free text and full of
typos — make it a real lookup. The separate CustomerAddress table is always one-to-one with Customer,
so fold it back in. Drop Product.LegacyCode, which nothing uses. And give Customer a LoyaltyTier."

**Step 1–2 — atoms and shapes:**

| atom | op-slug | shape |
|---|---|---|
| a ReturnReason lookup + its seed | `create-static-seed` | additive |
| a Return table | `create-entity` | additive |
| Return → OrderLine reference | `create-fk-clean` (Return is empty) | additive |
| Return → ReturnReason reference | `create-fk-clean` | additive |
| Order gets a ReturnsAllowed flag (NOT NULL, default 1) | `add-mandatory` + `add-default` | additive (the default backfills existing rows in place) |
| Customer gets a LoyaltyTier (optional) | `add-optional` | additive |
| Region free text → a Region lookup + FK | `extract-to-lookup` | multi-phase |
| CustomerAddress folds into Customer (1:1) | `merge-tables` | multi-phase |
| drop Product.LegacyCode | `delete-attribute` | destructive (two-release) |

**Step 3–4 — dependencies and concerns.** Return references OrderLine and ReturnReason, and the
ReturnsAllowed flag is part of the same feature: one connected group. The Region work reshapes
Customer; the CustomerAddress merge also reshapes Customer; both are multi-phase, so they contend and
must serialize. LoyaltyTier is a lone additive column on Customer. LegacyCode is a lone drop on
Product. The concerns:

- **PR A — Returns.** ReturnReason lookup + seed, Return table, both foreign keys, Order.ReturnsAllowed.
- **PR B — Region normalization.** The `extract-to-lookup` program, plus LoyaltyTier riding in its
  first (additive) release, since both are additive Customer changes.
- **PR C — Address fold-in.** The `merge-tables` program. Runs after B has fully merged — both
  reshape Customer.
- **PR D — Product cleanup.** Drop LegacyCode.

**Step 5 — releases and order:**

- **PR A: one release.** Every atom is additive, and the foreign keys are clean because Return starts
  empty, so they land trusted in the same publish; the ReturnsAllowed default backfills existing
  Orders in place. DacFx orders the table and seed and keys within the publish. Independent of the
  Customer and Product work — it can go first, or in parallel.
- **PR B: three moves.** Release 1 (expand) creates the Region lookup, seeds it, adds Customer.RegionId
  and Customer.LoyaltyTier. Release 2 (migrate) backfills RegionId from the text column, reconciling
  the typos to real regions — the mapping is the developer's decision, gathered by
  `../ask-the-developer/SKILL.md`, not guessed. Release 3 (contract) drops the Region text column as
  a two-release, the locked gate forcing the split. Opens an in-flight row.
- **PR C: after B.** The `merge-tables` sequence — add the absorbed address columns to Customer
  (expand), copy the data and prove the 1:1 cardinality before anything drops, then drop
  CustomerAddress (contract). Opens an in-flight row. Held until B has merged, because both reshape
  Customer.
- **PR D: two releases.** `delete-attribute` on Product.LegacyCode — pre-deploy drop with the model
  lagging, then the model catches up. Independent of everything else; can go anytime.

**Why this is the minimum.** The additive Returns feature collapses to a single release rather than
one publish per table. LoyaltyTier rides B's expand instead of taking its own pull request. B and C
each stage only the releases the locked gate and the coexistence rule force, no more. A, B/C, and D
are separate pull requests because they are genuinely separate concerns — merging any two would put
unrelated changes under one review — and B precedes C only because they share a table. Four reviews,
each coherent; no release staged that the gate does not require; no independent concern blocked on
another.

## The invariants decompose must not break

- **Every atom is still proven.** The plan is a sequence of catalog operations; each one still runs
  the full confirm-intent → change-author → prove loop on a disposable copy. Decomposition orders and
  groups; it never lets an operation ship on the strength of the plan alone.
- **Expand before contract, always.** No drop ships before the things that depend on it are gone. If
  the plan puts a drop before its dependents, the plan is wrong.
- **One concern, one pull request.** Do not batch unrelated concerns to save a review; do not split a
  coupled program across pull requests.
- **Name what you could not place.** If an atom maps to no catalog operation, or two concerns cannot
  be cleanly separated, say so plainly and raise it — that is a real finding, and inventing an
  operation or a merge to hide it is the failure this skill exists to prevent.
