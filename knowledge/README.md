# Changing the data model of these environments

This folder helps a developer who knows OutSystems and SQL, and is new to SSDT, make a schema change
in the SSDT repository that deploys safely to Dev, QA, UAT and Prod, and hand a dev lead a pull
request the lead approves by reading. A developer thinks in entities, attributes, references and
static entities; the SSDT project holds the `CREATE TABLE` each one becomes, and DacFx (the library
the deployment tool runs) works out the statements that take each database from what it holds to
what the project declares. Every step of the work is a `dbchange` verb, and every verb prints JSON
with `--json`.

## The same edit ships differently on different data
"Make this attribute mandatory" is a one-line `NOT NULL` edit to the `CREATE TABLE`. Whether it
applies in place, or is blocked and ships across two releases, depends on whether the table holds
rows at all: under the pipeline's Strict profile, `BlockOnPossibleDataLoss` writes a check into
the deploy script that stops the deploy when the table has any row, and it never reads the column,
so filling every blank first does not let it through (F7). The SQL text cannot tell the two cases
apart. `dbchange predict` reads, read-only, what each environment holds where the change reaches;
`dbchange prove` publishes the change to a disposable copy planted with those rows and reports what
DacFx and SQL Server did. That report is the classification.

In the conversation, the developer hears it plainly:

> You asked to make Email required. On a disposable copy of Dev the deploy was blocked: SSDT checks
> whether `dbo.Customer` has any rows and never looks at Email, so it blocks the change while the
> table holds data, even after the blanks are filled. On an empty table it would apply. With data
> in the table it ships as two releases, because this pipeline cannot relax that check: release one
> fills the blanks and tightens the column while the project still says optional, and release two
> brings the project up to date. The one call that is yours: what should the blank Emails become?

## Two findings, always both
**How it ships**: in place; one release with a post-deployment script; one release with a
pre-deployment script first; a scripted change, when the end state cannot be written as a table
definition; or across several releases, so the running application keeps working while the change
is in flight. **What the approving dev lead weighs**: the change is additive and the application is
unaffected; the application must change; existing data is modified or a relationship between tables
is added; or data is removed and the removal cannot be undone. Two facts add scrutiny when they
hold: the change may block writes or run long at production row counts, and the operation is a
first on these environments. The two findings are independent. Dropping a populated table ships in
one release and still carries the heaviest weight, because its rows are gone.

## Three facts the data settles
1. Is the table populated?
2. Does existing data break the new rule: a NULL, an orphan, a duplicate, a value too long?
3. Must the old and the new application code run side by side while the change is in flight?

`dbchange predict` answers the first two on each environment the session can read, and
`dbchange prove` shows what they do to the deploy. The third is the developer's to answer.

## The map
- [authoring.md](authoring.md): the one path a change takes, S0 to S8, each step naming its verb.
- [reviewing.md](reviewing.md): how a review reproduces the proof, bounds what the change reaches,
  challenges it, and ends in one of four dispositions.
- [description.md](description.md): the register and the ten sections of every pull request
  description.
- [ops/](ops/): one file per operation, in the developer's words: the trap, how it flips on the
  data, how to prove it.
- [shared/](shared/): the reasoning several operations share: tightening a column, a constraint as
  a claim about existing rows, seeds, identity and the refactorlog, changes across releases, when to
  index, the OutSystems and SQL Server vocabulary, and deploy scripts.
- [The findings](findings.md): what has been proven on a live SQL Server, each finding with the
  database and the DacFx version that prove it.
- [samples/](samples/): pull request descriptions, one per shape of change.
- [ledgers/](ledgers/): the templates of the SSDT repository's own ledgers, which live under
  `dbchange/ledgers/`: what has shipped, row tiers, open release windows, refusals, reviewers, the
  toolchain pin, the tables CDC captures, and scale measurements.
- [handbook/](handbook/): the chapters of the SSDT handbook the operations cite, by title.

## Read order
[description.md](description.md), then [authoring.md](authoring.md), then the operation file that
matches the request, then the shared file it points to. That is the whole path for one change.

## If `dbchange` is not on this machine
Run `dbchange doctor` first and quote its line. When `dbchange` is missing, or the line reads
DEGRADED because no local server answers, stop and say so: the SQL text does not settle how a
change ships, and a guess from it is the error this folder exists to prevent. The change can still
be authored. Edit the `CREATE`, write the description in the register with *provisional* as the
first word of its verdict, and open the pull request; the gate the SSDT repository's pipeline runs
on every pull request (`dbchange gate`) builds the copies, proves the change and posts the evidence.

## Not covered
Sequences, triggers, views, synonyms, computed columns, collation changes and CDC capture instances
have no operation file yet. [authoring.md](authoring.md) says how to refuse such a request and whom
to route it to.

## Two boundaries
- Publish only to a disposable copy. `dbchange prove` refuses a named environment as its target, and
  no publish profile in the SSDT repository points at one.
- Only the deployment tool writes to Dev, QA, UAT and Prod. Every verb that reads an environment
  reads it under a read-only login.

## Contributing to this folder
This folder is vendored into the SSDT repository from the dbchange repository; the router's first
line carries its fingerprint. To correct an operation or record a finding, open a pull request in
the dbchange repository. A hand edit here is reverted by the next vendoring, and the pipeline's
check (`dbchange knowledge package --check`) fails on it.
