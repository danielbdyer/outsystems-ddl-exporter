---
name: ask-the-developer
description: Use whenever proving surfaces a decision only a human can make — an orphan row that must be deleted or reassigned, a populated table that blocks a NOT NULL, over-length values facing truncation, duplicates facing a unique constraint, a cardinality that is not 1:1, an unmapped lookup value, a CDC gap tolerance. Owns the shape of the mid-flow question: state the measured fact, lay out each option with its consequence, ask exactly one question in the developer's words, and record the answer with its owner in the pull request. Intake owns the one up-front question; this skill owns the forks that only appear after publishing to a disposable copy.
---

# Ask the developer (the mid-flow decision)

> **Why this exists.** The disposable copy can establish what the data *is*; it can never establish
> what the business *wants done about it*. When a proof surfaces a fork, the two failure modes are
> mirror images: deciding for the developer (a silent truncation, a silent delete, an unlogged
> gate-relaxation), or dumping raw options on them with no consequences attached. The middle path is
> one disciplined shape, used every time. `confirm-intent` asks the one question *before* proving;
> this skill owns the questions that only exist *after*.

## The shape (every fork, the same three parts)

1. **The measured fact.** What the copy proved, with names and counts — never "some rows".
   *"Order 4 points at customer 999, and no such customer exists — one row."*
2. **Each option, with its consequence.** In the two findings' terms: what ships, who must review,
   what is lost or kept, and what it costs later. Two or three options; no more.
   *"Reassign it to a real customer — one UPDATE, a dev lead reviews it. Or delete it — that also
   deletes its two order lines, the loss is permanent, and a principal must review it."*
3. **Exactly one question, in their words.** A choice between the named options, or a value only
   they know. *"Reassign or delete?"* — then stop and wait.

Then **record the answer**: the decision, who made it, and the work item, on the pull request's
Data remediation section (or the release plan for a staged change). An answer that lives only in
chat is not a decision; it is a memory.

## The catalog (the known forks, ready to pose)

| Fork | The measured fact to state | The options and their consequences | The question |
|---|---|---|---|
| **Orphan rows** (adding a foreign key) | the orphan rows by id and count, and what they point at | reassign (an UPDATE; dev-lead review) · delete (cascades to named children; permanent; principal review) | "Reassign or delete?" |
| **NOT NULL on a populated table** | the blank count, and that clearing blanks alone still leaves the deployment blocked (the guard checks rows, not blanks) | relax the data-loss guard for this one change after a proven zero-blank count (a logged, scripted decision) · fill and tighten across two releases | "Relax the guard once, or stage it?" — plus the value question: "What should the blank ones become?" |
| **Over-length values** (narrowing) | the longest value, and the count that will not fit | truncate (the named values are cut; permanent) · widen the target size (nothing lost; is the requirement wrong?) · keep the values and stage the change | "Truncate them, pick a bigger size, or keep them?" |
| **Duplicates** (unique constraint/index) | the duplicated values and how many rows share each | pick the surviving row by a stated rule (newest? most complete?) · fix the duplicates at the source first | "Which row wins, and by what rule?" |
| **Check-constraint violators** | the violating rows and the predicate they fail | correct them to a stated value · exempt old rows by staging the rule | "What is the correct value for these rows — or do old rows get grandfathered?" |
| **Cardinality is not 1:1** (merge / move) | the actual counts on each side of the join | stop — this is a design decision, not a data fix; a naive copy silently keeps one row per parent and drops the rest | "These tables aren't one-to-one — which rows should survive a merge, or should the merge not happen?" |
| **Unmapped values** (extract-to-lookup) | the distinct values with no lookup row, by name and count | add the lookup row(s) · retire the value (deactivate, remap its rows) · hold the final phase until mapped | "Add 'Backordered' as a real status, or retire it?" |
| **CDC gap tolerance** | the table is change-tracked; recreating the capture instance either pauses the feed or requires a dual-instance staged rollout | tolerable gap (drop and recreate — simpler, one release) · no gap (two instances, staged, drained and cut over) | "Can the downstream consumer tolerate a capture gap — yes or no?" |
| **A constraint left unvalidated** (NOCHECK requested) | the constraint would exist but be untrusted (`is_not_trusted = 1`): the optimizer ignores it and existing bad rows stay | fix the data first, then validate (the constraint ends trusted) · accept untrusted as an explicit, logged exception | "Fix the data first, or accept — in writing — a rule that isn't enforced for existing rows?" |

The consequences column is the load-bearing part. An option without its cost is an invitation to
pick blind; the developer decides well exactly when the costs are laid flat.

## Rules

- **One question at a time.** A fork with two decisions inside it (the path *and* the value) is
  still asked as two plain questions, the second after the first is answered.
- **Never ask the developer to measure.** The counts, the ids, the longest value — measuring is
  this tree's job; deciding is theirs.
- **Consequences before preference.** Do not recommend until the options are stated with their
  costs; then a recommendation is fine, labelled as one: *"Reassigning is the safer default —
  deleting also removes the order lines."*
- **"I don't know" routes, it does not guess.** If the developer cannot answer, name who can
  (the data's owner, the app owner, the lead) and record the question as open on the pull
  request's Not verified section — the change can often proceed to review with the fork named.
- **The answer lands on the record.** Decision, decider, work item — in the PR's Data remediation
  section. The conversation is where the question lives; the record is where the answer does.

## Wiring

Posed from within `prove-on-dacpac` results by the change author (`../../agents/change-author.md`,
step 5) — the fork is discovered by the publish, posed by this shape, and its answer feeds the
remediation and the pull request (`../author-pr/SKILL.md`). The up-front business question stays
with `../confirm-intent/SKILL.md`; the vocabulary both are asked in stays with
`../os-vocabulary/SKILL.md`.
