---
name: author-pr
description: Use to turn a proven schema change into the pull request a reviewer approves by reading — THE terminal artifact of the tree, the hyper-clear PR template every op produces. Ten fixed sections (Verdict · Intent · What changes · Before promoting · How it ships · The data · What proving showed · After deploy — check · How to roll this back · Not checked), written in the plain record register of THE_RECORD_FORMS.md, following the S-state machine of THE_DECISION_TREE.md. Use after the change is proven on a copy.
---

# The pull request — the template

> **This is the one thing a reviewer reads to decide.** It is the developer speaking to the
> reviewer; the agent is invisible and never refers to itself. It is plain enough for a global team
> — a reviewer in Pune or Porto — and exact enough for a DBA. Ten sections, fixed order, each only
> as deep as the change needs. `THE_DECISION_TREE.md` is the procedure that fills it;
> `THE_RECORD_FORMS.md` is the register every word obeys; `FINDINGS_AND_CHANGES.md` holds the
> proofs the shipping shapes rest on.

## The register, in one breath

- The **developer** is the author. Never a sentence whose subject is the agent — no *I*, *we*, *the agent*.
- **State the fact, not a pointer to it.** A count, an object, a real message — never "the precedent" or "this operation".
- **Plain words.** Explain each SQL term once. No idioms. Lead with the conclusion, then explain.
- **Direct the reviewer** with plain imperatives: `Confirm…` · `Schedule…` · `Check with…`.
- **No trap-names, no taxonomy, no trivial negatives.** Report what the data does.
- **The proof reads `tried / did / realized`** with the real messages from the run on this branch.

## The template — the ten sections

Fixed order. A section with nothing real collapses to one honest line; it is never padded and never
silently dropped.

```
# <object>: <plain change> (<one-clause consequence, if any>)

## Verdict
<One line. What this does · the risk-driven call to action (Confirm <the thing> in each environment
 before promoting) · the one open item, or nothing. The call to action IS the review ask — not a
 role assignment ("a dev lead must review" says nothing; every PR is reviewed). Drawn from the risk
 table in THE_RECORD_FORMS.md.>

## Intent
The developer's stated intent for this PBI: <paraphrase>, with a direct quote for the one crucial
constraint: "<the developer's words>". <Name the work item, or: No work item supplied — attach one before merge.>

## What changes
- `<object>`: `<from>` → `<to>`. <One line per real change. No rationale here — that is Intent.>

## Before promoting
- <The risk-driven confirmations, per environment, as imperatives. What to run, what to check, who to
  ask, before this moves up a level. This is "who reviews" made concrete and true to how the change
  moves Dev → QA → UAT → Prod. Name, per open item, who runs it — the author before merge, or the
  reviewer read-only in the higher environments — so nothing is left assigned to nobody.>

## The data
- <The counts and the bad rows that decide the risk, named. Headline: detail. "No existing data is touched." if additive.>

## How it ships
- <The SHIP terminal from THE_DECISION_TREE.md S5, stated plainly. ONE RELEASE → say nothing beyond
  the change. TWO RELEASES (a data-loss change on this locked-gate pipeline) → name R1 (a pre-deploy
  physical change with the model unchanged) and R2 (the model catches up), and that the gate is not
  relaxed because it cannot be. This estate cannot relax the gate; a two-release is the shape, never a
  relaxation. A declarative FK/constraint add re-validates and trusts itself (WITH NOCHECK ADD
  + WITH CHECK CHECK in one publish); dirty child data blocks it (Msg 547) until reconciled — never
  invent a manual trust step (FINDINGS F9).>

## What proving showed
<Published to a throwaway copy on this branch. Never a prior run.>
- **Tried:** <the publish, and the exact Msg on a block>.
- **Did:** <the next real step, and what happened>.
- **Realized:** <the one thing the data taught>.
- <build/end-state facts: the composed model, is_not_trusted = 0, row counts. If no publish ran this
  session, say so plainly here and put the unproven claims under Not checked — never dress precedent as this change's proof.>

## After deploy — check
```sql
-- <what it proves>, expect <result>
<query, runnable in each environment>
```

## How to roll this back
<The reverse step, and whether the backout itself loses data. What is NOT auto-reversed, and where the
 recorded originals live for a manual restore. "Backing the change out was not exercised." if so.>

## Not checked / still open
- <The honest limits of a copy: application impact (the exact new failure and its owner) · other
  environments · production scale/timing · reversibility · anything the copy could not run this
  session · any open fork awaiting a human decision. Never empty, never generic.>
```

## Section rules that carry the most weight

- **Verdict.** The whole PR in one line. It names the risk and the one thing to confirm — not a
  reviewer's rank. If more than about five facts change the reviewer's mind, the change is too big
  for one PR (`THE_DECISION_TREE.md` S5): split it.
- **How it ships.** This is where the deployment reality lives. A narrow, a drop, or a populated
  `NOT NULL` on this pipeline is **two releases** — R1 changes the database physically while the
  model lags; R2 the model catches up. Never claim one declarative release, and never claim a gate
  relaxation this pipeline cannot perform (`FINDINGS_AND_CHANGES.md` F2).
- **What proving showed.** The heart. It shows the reviewer the change was published to a copy and
  what the database actually did, as a plain sequence with the real messages. A claim that was not
  proven this session goes under **Not checked**, not here.
- **Not checked.** Required, every time. A copy proves the schema transition against the data's
  shape; it is silent on the running app, other environments, production scale, and the backout.
  Name the specific unverified thing for *this* change and who owns it.

## Worked example — a foreign key with an orphan (proven live, 2026-08-21)

Real messages from a publish to SQL Server 2022 on this branch. `Order 4` points to `CustomerId 999`,
which does not exist.

```
# Order → Customer: add a foreign key (one orphan order removed so the add does not block)

## Verdict
This PR adds a rule that every Order must point to a real Customer, and removes 1 order that points
to a customer who does not exist. Confirm that order is junk, not a real order, in each environment
before promoting. Removing it cannot be undone from the schema — the only rollback is a restore.

## Intent
The developer's stated intent for this PBI: make the database reject any Order that does not belong
to a real Customer, so a missing or wrong customer id becomes impossible. No work item supplied —
attach one before merge.

## What changes
- `dbo.[Order].CustomerId`: add a foreign key to `dbo.Customer(Id)`, named `FK_Order_Customer_CustomerId`.

## Before promoting
- Run the orphan query (below) and confirm every order it lists is junk that can be removed — the set
  differs per environment. If one is real, stop and reassign it to the right customer instead.
- The key is made trusted, so SQL Server validates every existing row and the query planner can rely on it.

## The data
- 4 orders. 1 is an orphan: `Order 4 → CustomerId 999`, and no Customer 999 exists. It has 2 order lines.
- Orders 1–3 point to real customers.

## How it ships
- A pre-deploy step removes orders with no matching customer (their order lines first, then the
  orders). Idempotent — re-running removes nothing more.
- The seed no longer plants the orphan, so a fresh database is clean from the start.
- The row removal is a plain pre-deploy `DELETE`, which `BlockOnPossibleDataLoss` does not govern, so
  no gate change is needed.
- The orphan must be reconciled before the key is added — otherwise the publish is refused
  (`Msg 547`). Reconciled, the key validates and trusts itself (`is_not_trusted = 0`); nothing to add.

## What proving showed
Published to a throwaway copy on this branch.
- **Tried:** publish the key, orphan still present → refused. `Msg 547`: the ALTER conflicted with
  `FK_Order_Customer_CustomerId` on `dbo.Customer.Id`. The orphan has no parent.
- **Did:** remove the orphan and its lines in a pre-deploy step; fix the seed; publish → succeeds,
  0 orphans remain.
- **Realized:** the generated script adds the key `WITH NOCHECK` and then re-validates it
  `WITH CHECK CHECK` in the same publish; with the orphan gone the key lands trusted
  (`is_not_trusted = 0`) on its own.
- **Confirmed:** the full change set on a fresh copy → key trusted automatically, 3 orders remain;
  re-publish → nothing changes, still trusted. A manual post-deploy `WITH CHECK CHECK` added on top
  changed nothing — it is redundant.

## After deploy — check
```sql
-- every order points at a real customer, expect 0 rows
SELECT o.Id, o.CustomerId FROM dbo.[Order] o
WHERE NOT EXISTS (SELECT 1 FROM dbo.Customer c WHERE c.Id = o.CustomerId);

-- the key is trusted, expect 0
SELECT is_not_trusted FROM sys.foreign_keys WHERE name = 'FK_Order_Customer_CustomerId';
```

## How to roll this back
Drop the key: `ALTER TABLE dbo.[Order] DROP CONSTRAINT FK_Order_Customer_CustomerId;` — dropping loses
no data. The removed orders are not restored by dropping the key; they are in the pre-deploy step's
output for the run that removed them. Backing the change out was not exercised.

## Not checked / still open
- The orphan's fate is the developer's call. This PR removes it as junk. If it is a real order,
  reassign it instead — a separate reconcile.
- The pre-deploy step also removes the orphan's order lines. If order lines feed a report or export,
  confirm that is safe.
- No load test: on a large table, validating the key and deleting rows can run long — schedule a window.
```

## Where each op fills in

Every operation skill states, in the plain register, what it contributes to this template for its
change: the **Verdict**'s risk class, its **How it ships** shape (its SHIP terminal), the exact
**tried / did / realized** its proof produces, its **After deploy** query, its **Rollback**, and its
standing **Not checked** items. Assemble the PR from those; the op owns the specifics, this skill owns
the shape and the register.

## Hard rules

- **The register is `THE_RECORD_FORMS.md`.** The developer is the author, the agent invisible; every
  sentence denotes a referent; plain global English; no trap-names.
- **Nothing is attached for the reviewer to run.** The change ships in the sqlproj (edited `CREATE`s,
  the refactorlog, the pre/post-deploy scripts); evidence is summarized as text; queries are inline.
- **Do not re-prove in the PR — report what the branch's publish established.** A claim that was not
  proven goes under **Not checked**, never dressed as this change's evidence.
- **The shipping shape is decided by the state machine, not chosen.** A data-loss change on this
  locked-gate pipeline is two releases (`THE_DECISION_TREE.md` S5).
