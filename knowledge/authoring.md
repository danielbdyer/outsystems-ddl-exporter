# Authoring a change

One session takes a request from the developer's words to a pull request, through nine steps in
order. Each step names the `dbchange` verb that does it and the condition for leaving it; no step is
skipped, and none is left on a guess. The conversation with the developer and the pull request
description are written in the two registers of [description.md](description.md).

Four rules hold in every step. The project is the schema: edit the `CREATE`, never write an `ALTER`,
and DacFx plans the statements. A finding before a proof says *provisional* in its first word. The
developer answers one business question, and the session never answers it by reading data. Nothing
is published anywhere but a disposable copy on the local server.

## S0 · Intake          `dbchange doctor`
First, the compound check: when the request names more than one change, or one need that entails
several (new tables, a reshaped table, a dropped column), split it before naming anything, and
return an ordered list of pull requests with the reason for the order. List each operation with its
object. Group the operations by the existing table each one reshapes: one table's reshapes are one
pull request, run one after another, and a new table with what builds it is a pull request of its
own; a reference to another pull request's table is an ordering edge and never a reason to merge.
Order within each: a rename or retype first, the additive changes next, data moves in dependency
order, and drops and constraints last. A release is one deploy script, decided by its strictest
change (F13), so each release is proven whole. Then run S1 to S8 once per pull request.

Run `dbchange doctor` and quote its line; when it is missing or reads DEGRADED, follow
[README.md](README.md)'s rule for a machine without `dbchange`. Then, for each operation:
- Hear the request in the developer's words and name its file under `knowledge/ops/`, whose
  description is written in those words. When a phrase fits several ("change the customer email" can
  widen, retype, rename or make mandatory), offer the candidates and let the developer pick.
- Say the destination back in both vocabularies: *you want `Customer.Email` to become required; in
  the table that is `Email NVARCHAR(256) NOT NULL`*. An External Entity's table is outside the
  project: say so and stop.
- Record the three facts (populated; violating; old and new code side by side) as known only when
  the developer knows them, and otherwise as *unknown: prove it*. A recollection of row counts is a
  guess.
- Ask the one business question the remedy depends on, in the developer's terms: the value that
  fills the blanks, delete or reassign an orphan, truncate or pick a bigger size. Never ask the
  developer to measure; S2 does. A loosening with no business choice asks nothing.

Leave when the object, the operation and the intent are captured and the question is posed.

## S1 · Edit            `dbchange diff --from ref:main --to ref:HEAD`
Edit the `CREATE` to the destination. A rename is the renamed column with its refactorlog entry
(the file that records a rename so the deploy keeps the data), which `dbchange diff --refactorlog`
writes. Read the change back with `dbchange diff`: one property per altered line, the deploy scripts
and the refactorlog included. A dropped and a created column where a rename was meant is a missing
entry, and the deploy would lose the column's data. Leave when the project builds and the diff holds
only the intended change.

## S2 · Measure         `dbchange predict --env dev,qa,uat`
`dbchange predict` reads each environment the session can reach, read-only, and returns integers:
whether each table the change reaches holds rows, and how many rows break the new rule (NULLs,
orphans, duplicates, values too long or that fail the conversion). For a column a script will read,
`dbchange profile --env <e> <schema.table.column>` returns its value-free shape. Leave when the
counts are in hand per environment; one the session cannot read is named *not read*.

## S3 · Classify        `dbchange classify --from ref:main --to ref:HEAD`
`dbchange classify` names the operation and gives a provisional pair of findings, how it ships and
what the lead weighs, with the fact that would flip the shape. It refuses a rename typed without its
refactorlog entry and a column-list change on a table CDC captures. Only the additive corner (a
nullable column, a new table, a new index on a new column) is settled on sight; every other change
is proven. Leave with the provisional pair.

## S4 · Prove           `dbchange synthetic-copy up` · `dbchange prove --target synthetic-copy`
`dbchange prove` publishes the change, under the pipeline's Strict profile, to a fresh copy planted
with each environment's existing data, and reports *published*, or *blocked* with the message
verbatim, or *failed*. On a block, `--permissive` publishes it again on the copy and shows what the
block protected: the rows and values the change would lose. A remedy is proven the same way, and so
is a second publish, which must issue nothing. Leave with a verdict from this branch; a prior run, a
precedent or a sample is never this change's proof.

## S5 · Ship            `dbchange check inflight`
Read the shape from the verdict: in place; one release with a post-deploy script; one release with
a pre-deploy script first; scripted; across releases; or refused. The pipeline cannot relax
`BlockOnPossibleDataLoss`, so a change it blocks on a populated table (a narrowing, a populated
`NOT NULL`, a dropped column) ships across two releases (F4):
- Release one: a pre-deploy script makes the change physically while the project still declares the
  old shape, so the plan holds no data-loss step; the script fixes the data first, then alters.
- Release two: the project catches up, and its plan against each environment is empty. It goes up
  an environment only after release one has landed there.

No release carries both the project's change and the pre-deploy `ALTER`: it blocks and half-applies
(F2). A constraint SQL Server refuses on a value (Msg 547, Msg 1505) is a different block: a
pre-deploy reconcile and the declarative add ship in one release, and the key lands trusted (F8,
F10). Before release one first reaches QA or UAT, run `dbchange check drift --target env:qa --at
<the tag deployed now>`; drift left from that environment's cutover is reconciled first. A
multi-release change opens its row in `dbchange/ledgers/in-flight.md`, and `dbchange check inflight`
refuses a change to a table whose window is open. Leave with a terminal shape.

## S6 · Fork            `dbchange predict`
When a proof surfaces a decision only a person can make, pose it in one shape: the measured fact,
with names and counts from `dbchange predict`; two or three options, each with what ships, what the
lead weighs, and what is lost or kept; and one question. Orphans: reassign or delete, and a delete
cascades to the named children. A populated `NOT NULL`: the value of the blanks, since the shape is
already two releases. Values too long: truncate, a bigger size, or keep them. Duplicates: which row
survives, by what rule. A merge that is not one-to-one: a design decision for the lead. A constraint
left untrusted: fix the data first, or accept in writing a rule that is not enforced for existing
rows. Recommend only after the costs are stated, and label it. Record the answer with who gave it;
when nobody can answer yet, name who can and carry the question into the description. Leave when
the fork is posed and recorded, or answered.

## S7 · Describe        `dbchange describe`
`dbchange describe` renders the ten sections of [description.md](description.md) from each proof's
provenance, and writes a placeholder for the intent, for the answer with its owner, and for what
was not checked. Fill the three; a section with nothing to report says so in a sentence. Leave when
all ten are present and the verdict, the shape and *Before promoting* agree.

## S8 · Verify          `dbchange gate`
Read the description against [description.md](description.md): each sentence names a count, an
object, a type, a state, a message or a quote; each claim carries its provenance; the shape matches
the proof; no relaxed profile and no invented schema is claimed; *Not checked* is not empty and
names the Integration Studio refresh in each environment as the step that completes the change.
`dbchange gate` then reproduces the proof from the clone, as the pipeline will. Leave when the gate
agrees; a failed check returns to S7.

## Writing a remedy
- A remedy finds its rows by predicate, by the rule they break (`WHERE [Email] IS NULL`, a `NOT
  EXISTS` on the parent), and never by a literal value read from one environment's rows, since each
  environment holds different rows and no value read from one reaches the repository.
- It checks its own preconditions (the column exists with the type it expects, the rows it reads are
  there) and stops with `THROW` before any write when one fails, so it never runs half against a
  database it was not written for.
- A pre- or post-deploy script's work is not rolled back when the deploy fails (F6), so the script
  is written to run again safely over any partial state; a seed's `MERGE` guards `WHEN MATCHED`, and
  its second deploy touches no row.
- A check constraint is written in the `OR` form SQL Server stores, `CHECK ([Status] = 'A' OR
  [Status] = 'B')`, and never with `IN`: SQL Server stores an `IN` list as a chain of `OR`s, so
  every plan against a database that holds it drops it and creates it again.
- A remedy adds no permanent table, column or constraint; a fix that seems to need one is a fork.
  A remedy that changes data records rows before, rows after, and who approved it.

## Teach in the conversation
Give the developer the reason in their terms, specialized to the change, and name the mistake
avoided: *SSDT checks whether the table has rows and never reads the column, so filling the blanks
first does not let the change through.* The reasoning stays in the conversation; the description
carries findings.

## Refuse and route
Sequences, triggers, views, synonyms, computed columns, collation changes, CDC capture instances, an
External Entity's table, and an operational request (rebuild an index, trust a constraint now) have
no operation file. Say so, name who does it (the DBA, or the owner of the external system), and name
what to check. Never guess a shape for them.
