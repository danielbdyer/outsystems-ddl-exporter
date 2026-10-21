# Reviewing a change

The SSDT repository's pipeline runs `dbchange gate` on every pull request: it builds the copies,
proves the change once per environment's existing data and posts the evidence, so the reproduction a
reviewer once did by hand is done for every change. This file is for the dev lead's own review, for
a reviewing session working for the lead, and for a change the gate could not reproduce in full.
A dev lead approves every schema change and never their own; a lead's own change goes to the other
lead.

A review audits the proof and never re-authors the change. A disposition without a reproduced
result is an opinion.

## Two roles
- **Backstop**, on a developer's change: gate it. Every claim of the description is an obligation to
  discharge, and all four dispositions are open, so a fixable defect goes back to the author and the
  lead's queue holds only decisions.
- **Sparring**, on the lead's own change: argue. Give the strongest case against it, a concrete
  counter-design and the proof behind both, and withdraw the objection at once when the lead
  disproves it. *Returned to the author* does not exist here.

Both are written in the register of [description.md](description.md), for a lead who is fluent in
SQL Server: the finding first, then the count and the message verbatim, a one-clause gloss of an SSDT
term at first use, one question at a time, and no explanation of what the lead already knows.

## Reproduce          `dbchange prove --target synthetic-copy`
Prove the change on a copy of the reviewer's own, at the existing data the description's machine
block carries, and compare each claim with what reproduced:
- the shape and the weight: the outcome that forces the shape reproduces, and each added-scrutiny
  line agrees with `dbchange/ledgers/operations.md` and `dbchange/ledgers/row-tiers.md`;
- the plan: the same statements in kind (`sp_rename` or a drop and a create; where the
  `BlockOnPossibleDataLoss` check sits);
- the proof: the same block with a comparable count, the remedy's clean publish, and a second
  publish that issues nothing;
- the change set: a refactorlog entry for every rename, a guarded `MERGE`, a key that ends trusted;
- the trap the author named, and the one the author missed.

A claim that does not reproduce is false as stated, and the review re-runs `dbchange classify` only
then, to name what DacFx forces. A populated `NOT NULL` is the one case where a block that persists
after every blank is filled is the correct reproduction (F7). What the copy cannot show, each
higher environment's counts, the reviewer reads with `dbchange predict --env qa,uat`, read-only,
and writes into the disposition; a count that changes the shape returns the change to the author.

## Scope               `dbchange diff --from ref:main --to ref:HEAD`
Map what the change reaches before attacking it, with row counts: the foreign keys into the target,
to the depth a cascade reaches; the foreign keys out of it; the procedures, functions and views that
read it; its indexes, a unique one flagged; and the consumers outside the project (an ETL job, a
report, another database, the running OutSystems application), which no copy can prove and the map
names. The map sets the highest disposition the change can reach: a consumer outside the project
caps it at *Approved with a named risk*, and a cascade that reaches grandchildren or an irreversible
drop with no recovery path caps it at *Escalated*. No disposition exceeds the scope established,
and a change whose cascade was never mapped cannot be approved.

## Challenge           `dbchange synthetic-copy up` · `dbchange prove --permissive`
The author's copy held the rows the author planted; the challenge plants the rows the author left
out. Two moves serve every challenge:
- **The consequence run**, for a change that removes data: after Strict blocks, `--permissive`
  publishes it on the copy, and the rows and values lost are named with their count.
- **The injected row**, for a tightening or a constraint: a `-- witness:` block in
  `dbchange/witnesses.sql` plants one violating row (a NULL, an orphan, a duplicate, a value too
  long), and the proof captures the message number and the offending value.

By operation class: a rename's plan is read for a drop and a create, and its refactorlog for a stale
entry; a populated tightening is filled to zero violations and must still block; a foreign key gets
an orphan and must end trusted once reconciled; a narrowing gets an over-length value; a delete rule
gets its cascade counted; a seed is deployed twice and its second deploy must touch no row. A change
that cannot block (a nullable column, a seed's `MERGE`) is not given one: name the absence and stop.

## Dispose
- **Approved.** Every obligation discharged within the scope; straight to the deploy.
- **Approved with a named risk.** Sound given one risk the lead accepts in a line: a consumer
  outside the project, a reversal asserted and never exercised, a correctly shaped change that
  modifies or removes existing data. A risk without its evidence is an undischarged obligation.
- **Returned to the author.** A defect the developer can fix without the lead: a missing refactorlog
  entry, a skipped orphan count, a populated tightening claimed as one release, an unguarded `MERGE`.
  The lead is not tagged; the authoring session explains the fix to the developer in conversation.
- **Escalated, one question for the lead.** A design decision or an irreversible step, with the
  scope map attached and one question answerable in a word. A fixable defect sent to the lead spends
  the time a review exists to save.

A named risk and an escalation each append a row to `dbchange/ledgers/refusals.md`, with the
disposition, the risk or the question, and the evidence it rests on.

## The written disposition
It opens with what reproduced, on which copy and DacFx version, then the finding, then the next
move. For example:

> Returned to the author. The description states the foreign key lands in place. Reproduced on a
> fresh copy: Msg 547, conflicted with `FK_Order_Customer_CustomerId`, 8 orphan rows; the orphan
> count was never run. Reconcile the 8 rows per the recorded answer in a pre-deploy script, publish
> again, and confirm `is_not_trusted = 0`. This does not need the lead.
