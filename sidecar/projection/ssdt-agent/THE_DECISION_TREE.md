# THE DECISION TREE — how a schema-change PR gets written

This is the procedure `change-author` runs, start to finish. It turns one OutSystems change
request into one pull request a reviewer can act on. `THE_RECORD_FORMS.md` governs the words;
this governs the steps. Read both before rewriting any skill file — every op skill is a
specialisation of this tree.

---

## Invariants — true at every step

- **The developer is the author; the agent is invisible.** It never refers to itself. It
  writes on the developer's behalf, to the reviewer.
- **Prove, do not guess.** Every classification comes from a real publish to a throwaway copy
  of the database, on this branch. Never cite a prior run as this change's proof.
- **Referent, not reference.** Every sentence names a fact — a count, an object, a message —
  or it is cut. No pointer standing in for the fact ("the precedent", "this operation").
- **Plain words.** A global team reads this; explain each SQL term once, in plain English.
  Bullets over prose. Short.
- **Direct imperatives to the reviewer:** `Confirm…` · `Schedule…` · `Check with…`.
- **A remedy adds no permanent schema.** A new table, column, or constraint is a product
  decision with its own PR. If a fix seems to need one, stop and ask (Node 6a).

---

## The nodes, in order

### Node 0 — Read intent
- **In:** the PBI / change request.
- **Do:** name the object, the operation, and the developer's intent.
- **Emit → Intent:** `The developer's stated intent for this PBI:` then a paraphrase, with a
  direct quote for the one crucial constraint.

### Node 1 — Write the desired-state edit
- **Do:** edit the `CREATE` to the target shape. Never write `ALTER` — SSDT computes the
  difference.
- **Emit → What changes:** `<object>: <from> → <to>`.

### Node 2 — Probe the data on a copy
Answer three questions against real-shaped data — two can only be learned by looking:
1. **Does the table have rows?** (`SELECT COUNT(*)`)
2. **Does existing data break the new rule?** — the op-specific probe: too-long values
   (`LEN`), NULLs, rows with no parent (orphans), duplicates.
3. **Must old and new application code run at the same time** while the change is in flight?
- **Emit → The data:** the counts and the bad rows, named. Headline-colon-detail.

### Node 3 — Prove on THIS branch (tried / did / realized)
sqlpackage is on the box; there is no excuse to skip this and no prior run to borrow.
- Publish the change to a throwaway copy under the **safe default**
  (`BlockOnPossibleDataLoss = true`).
  - **Applies clean** → record "applied; nothing blocked." Skip to Node 4.
  - **Refused** → record the exact `Msg` number and text. That is one **Tried**. Take the next
    real step — shorten a value, reconcile a row, relax the one gate, fix the parent — publish
    again, and record what happens. Repeat until it applies. Each step is a **Did**.
- Publish once more with no change → confirm it does nothing (safe to re-run).
- Name the one thing the data taught — the **Realized**.
- **Emit → What proving showed:** the `Tried / Did / Realized` sequence with the real messages.

### Node 4 — Classify the risk (drives the verdict)
Read the effect on existing data straight off the proof:

| Effect on existing data | Risk | The verdict's call to action |
|---|---|---|
| pure add — nothing existing read or written | none | `Safe to apply; nothing existing changes.` |
| existing values rewritten | data-change | `Confirm the changed values are correct in each environment before promoting.` |
| rows or values removed, not recoverable from the schema | data-loss | `Removes data that cannot be recovered — confirm it is truly unwanted; the only rollback is a restore.` |
| a constraint checked against every existing row | integrity | `Confirm no existing row is rejected in each environment before promoting.` |
| + old and new app code must run at once (Q3 yes) | app-first | `The running app must be updated to match before this deploys.` |
| + big table / never done here before | (add) | `can lock or run long — schedule a window` / `first time on this estate.` |

- **Emit → Verdict** (one line): `<what it does>. <the call to action>. <the one blocker, or nothing>.`
- **Emit → Before promoting:** the concrete confirmations, per environment (dev → test → UAT →
  prod), driven by the risk row. This replaces "who must review" — every PR is reviewed; what
  matters is *what to confirm before it moves up*.

### Node 5 — State how it ships (only the non-routine)
- Clean single apply → say nothing.
- **A gate is relaxed** → state it exactly: *this one publish runs with
  `BlockOnPossibleDataLoss = false`; that is a setting on the publish command, not a state in
  the database; the next deploy uses the safe default on its own; no second PR turns it back
  on.*
  > **Superseded for this estate — the gate cannot be toggled** (Azure DevOps → Octopus dacpac
  > deploy). A single pre-deploy `ALTER` does **not** suffice for a narrowing or a populated
  > `NOT NULL` — proven to block *and* half-apply (`FINDINGS_AND_CHANGES.md` F2). The proven
  > shipping shape is the **two-release pattern** in `FINDINGS_AND_CHANGES.md` Part 3, which
  > replaces this bullet (fold-in pending).
- **A pre-deploy step is needed** → name what it does and that it is transient.
- **Staged across PRs** → name the phases and why the app needs both shapes at once.
- **Emit → How it ships** (omit if routine).

### Node 6 — Emit the tail
- **After deploy — check:** the before/after queries to run in each environment, each with its
  expected result.
- **How to roll this back:** the reverse steps, and what is *not* undone automatically.
- **Not checked / still open:** the honest limits, and any question still owed to a human.

### Node 6a — The fork (a decision only a human can make)
When the proof surfaces a choice — an orphan to delete or reassign, a value to truncate, a
duplicate to drop — do not choose it silently, and never invent schema to sidestep it. Pose one
question:
- State the **measured fact** (the object, the exact rows, the count).
- Give **2–4 options**, each with its **consequence** and its **cost**, and a **schema line**
  (`adds no object` / `adds <X> — a separate PR`).
- Offer a **custom answer** — but a custom answer that needs a new object becomes its own PR.
- Ask **one** question, in the developer's words.
- Record the answer as **one line** in the PR (the object, the action, the owner, the date).
  While it is open, the PR says so plainly and carries no invented schema in the diff.

---

## The section order (fixed spine, variable depth, collapse-don't-drop)

Every PR, top to bottom:

1. **Verdict** — one line.
2. **Intent** — the developer's stated intent for the PBI.
3. **What changes** — the schema edit.
4. **Before promoting** — the risk-driven confirmations, per environment.
5. **How it ships** — the non-routine mechanics (omit if routine).
6. **The data** — the counts and bad rows.
7. **What proving showed** — tried / did / realized, on this branch.
8. **After deploy — check** — the per-environment queries.
9. **How to roll this back** — the reverse, and what is not auto-undone.
10. **Not checked / still open** — the limits and any open fork.

A section with nothing real collapses to one honest line. It is never padded and never dropped
without a word — its absence would itself be a finding.

---

## What flexes across ops (same tree, different depth)

- **Add a nullable column:** Node 2 finds nothing; Node 3 is one line ("applied; second publish
  did nothing"); *The data* and *Roll back* are a line each. The boring change stays boring.
- **Make a column required, populated table:** Node 3 shows the block is *table-has-rows*, not
  *column-has-NULLs* — a backfill to zero NULLs still blocks; the honest path is a logged gate
  relaxation or staging across PRs.
- **Add a foreign key with an orphan row:** Node 3 shows `Msg 547`; Node 6a poses the orphan's
  fate; the reconcile lands the key trusted (`is_not_trusted = 0`).
- **Delete a table with rows:** Node 4 is data-loss → the verdict says the only rollback is a
  restore; Node 3 shows the block that proves the data is really there.

The tree does not change. Only which nodes carry weight.
