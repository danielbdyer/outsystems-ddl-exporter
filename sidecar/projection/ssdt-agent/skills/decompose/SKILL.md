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

**2. Read each atom's ship shape from its own SHIP terminal — do not restate the count here.** How
many releases an atom needs, and what forces the staging, is owned by its op skill and the `_index`
concern behind it; decompose reads that, it does not re-derive it. For packing, sort each atom into
just two coarse buckets:

- **One-release (batchable).** Ships in a single release, in place. This is the additive corner
  (`add-optional`, `create-entity`, `create-static-seed`, `add-index`, `junction`, a clean
  `create-fk`, `add-mandatory` *with* a DEFAULT that backfills) and the lossless or loosening
  in-place ops (`rename-attribute` and `move-schema`, each with its refactorlog entry; `widen`;
  `retype-implicit`; `make-optional`; `add-default`; `modify-default`; `drop-fk`; `drop-index`). It
  also includes the constraint builds `add-unique`, `add-check`, `define-pk` — these ship in **one**
  release even over dirty data, because a violating row is reconciled in a pre-deploy in the *same*
  publish and the declarative build then re-validates (`../_index/constraint-is-a-claim/SKILL.md`).
  A constraint build blocks on a value (`Msg 1505`, `Msg 547`), which is **not** the locked
  data-loss gate — never treat it as a two-release. These atoms can share a release with one
  another, subject to the ordering edges in step 3.
- **Staged (its own release sequence).** Ships across more than one release: the tightening and
  destructive ops on populated data (`make-mandatory` / `narrow`, `delete-attribute`,
  `retype-explicit`'s drop leg — each a two-release the locked gate forces; `delete-entity` — a
  scripted single release, still its own), and the multi-phase programs (`extract-to-lookup`,
  `merge-tables`, `split-table`, `move-attribute`, `temporal-convert`, `archive-entity` —
  `../_index/multi-phase/SKILL.md`). Take the exact release count from each atom's SHIP terminal;
  never share these releases with another atom's.

**3. Draw the dependency edges — and only the ones that cross a release.** Inside a single publish,
DacFx orders object creation itself (it creates a lookup table before the foreign key that
references it), so intra-release ordering is not yours to plan. What you plan is what crosses
releases, moves data, or waits on another atom's edit to land first:
- a backfill can only run after the column it fills exists and the rows it reads from are present;
- a drop can only run after every object and every application path that depends on it is gone;
- a foreign key to a new lookup can only be trusted after the lookup is seeded;
- **an atom that names a column depends on the atom that renames or retypes that column.** A unique
  index or check on `Sku` can only be built after the `Code → Sku` rename has landed; a backfill
  into a retyped column waits for the retype. Whenever one atom changes a column's name or type and
  another atom references that column, draw the edge from the referencing atom to the edit.

These edges give the order both within a concern and across concerns.

**4. Cluster into concerns — but only *reshape*-coupling clusters.** Two atoms can relate in two very
different ways, and only one of them is a reason to share a pull request:
- **Reshape-coupling.** Two atoms make a definitional change to the *same existing table* — add,
  rename, retype, tighten, or drop a column on it; add a constraint to it; split or merge it. These
  belong in **one concern**, and they must be **serialized on that table**: each keeps the table's
  definition lagging its database for a window, and a second publish in that window reverts the
  first (the concurrent-publish hazard). Every definitional change to one table is one concern.
- **Reference-coupling.** One atom takes a foreign key to, or reads from, a table that *another*
  atom reshapes, but does not itself reshape that table. This is **not** a reason to merge concerns.
  It is only an **ordering edge** (step 3): the referencing atom waits if it depends on the change,
  and is otherwise free. A new feature that references an existing table stays its own pull request
  even while that table is being renovated elsewhere.

So **the concerns are the connected components of the *reshape*-coupling graph**: group the atoms by
the existing table each one reshapes, and let a brand-new table plus the atoms that build it be
their own group. Reference and data-flow edges cross *between* concerns as sequencing, never as
merges — that is what keeps a new feature that merely points at a renovated table from being
swallowed into the renovation. One concern is one pull request: coherent, reviewable on its own,
revertible on its own.

**5. Pack each concern into the fewest releases, in order.** A concern that reshapes one existing
table may hold several atoms — a rename, a split, a tightening, a constraint-add. Order them so no
two lag windows overlap and every atom sees the column shapes it expects:
1. **Rename or retype first.** Land the `rename-attribute` / `retype` atoms (one release each, in
   place) so every later atom references the final column names and types.
2. **Expand** — batch the additive atoms into one release where they do not depend on each other.
3. **Each staged reshape lands fully before the next starts.** A `split-table` (its several
   releases) completes, then a `make-mandatory` (its two releases) completes — never interleaved,
   because both keep the table's definition lagging and an overlap reverts one.
4. **Constraint-adds and drops last**, each after the columns it touches are in final shape: a
   unique index on `Sku` after the rename lands; a column drop after its readers are gone.

A one-release atom (a rename, a constraint-add, a widen) does not get its own pull request — it
rides in its concern's sequence at the point step 3's edges allow. A brand-new-table concern that is
all additive collapses to a single release.

Across concerns: independent concerns proceed in parallel; a reference edge from one concern to
another only delays the referencing atom until its dependency lands. The result is the minimum
number of moves — nothing staged that the gate does not force, nothing independent serialized that
need not be, and no two reshapes of one table in flight at once.

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

**Step 3–4 — dependencies and concerns.** The Returns atoms build *new* tables (ReturnReason,
Return) and take references to existing ones (OrderLine, ReturnReason). Referencing OrderLine is a
reference edge, not a reshape of OrderLine, so Returns does not merge into any OrderLine work — it is
its own concern. Region and CustomerAddress both *reshape* Customer (Region adds RegionId and drops
the text column; the merge absorbs the address columns), so they reshape-couple on Customer: one
concern each, serialized. LoyaltyTier is a lone additive column on Customer — it rides in whichever
Customer concern lands first. LegacyCode is a lone drop on Product. The concerns:

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
- **Read each atom's release count; never restate it.** How many releases an atom needs, and which
  gate forces the staging, is owned by its op skill and its `_index` concern. Decompose reads the
  SHIP terminal and packs; it does not assert a count of its own. A count decompose invents is a
  count that can be wrong — the locked data-loss gate and a constraint's `Msg 1505` value-block are
  different mechanisms, and only the op skill knows which one an atom meets.
- **Expand before contract, always.** No drop ships before the things that depend on it are gone. If
  the plan puts a drop before its dependents, the plan is wrong.
- **One concern, one pull request.** Do not batch unrelated concerns to save a review; do not split a
  coupled program across pull requests.
- **Name what you could not place.** If an atom maps to no catalog operation, or two concerns cannot
  be cleanly separated, say so plainly and raise it — that is a real finding, and inventing an
  operation or a merge to hide it is the failure this skill exists to prevent.
