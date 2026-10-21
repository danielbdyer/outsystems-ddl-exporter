# The pull request description

Every schema change reaches a dev lead as a pull request, and the lead approves it by reading its
description. The description is the record of the change: what it does to which object, what a
publish to a disposable copy showed, and what nobody checked. `dbchange describe` renders seven of
its ten sections from the provenance of each proof, and writes a placeholder into the three only a
person can fill: the intent in the developer's words, the answer to the open question with its
owner, and what was not checked beyond what the run could see. The pipeline's gate refuses a body
that still holds a placeholder. This page is the register and the template both follow.

## Two surfaces, two registers
- **The conversation** with the developer may say *you*, gives the reason a change ships as it does
  in a sentence or two, and asks the one question that is the developer's to answer.
- **The description** is agentless and teaches nothing: a reviewer needs the finding, its evidence
  and what to check, and the reasoning a developer is owed stays in the conversation.

## The register (nine rules)
1. **Agentless.** No *I*, *we* or *you*: write `Published to a disposable copy of Dev; the deploy
   blocked.` The developer is the author, and the session that helped never appears.
2. **The finding first, the proof beneath.** Each item opens with a sentence a reviewer can read
   aloud, and carries its proof one level down: the message verbatim, the row count, the query.
3. **The true verb.** *Drops, deletes, narrows, rewrites, blocks, refuses.* A dropped column is
   dropped; *removes* and *cleans up* hide it, and *destroys* dramatizes it.
4. **Findings asserted, interpretation labelled.** What the publish showed is stated plainly. A
   recommendation or an unproven risk is marked as judgement.
5. **Every claim carries its provenance inline:** the copy it ran on, the count, the message
   number (`0 rows`, `Msg 547`, `is_not_trusted = 0`).
6. **The exact object, as the subject.** `dbo.[Order]`, `CustomerId`,
   `FK_Order_Customer_CustomerId`, `Order 4`. The subject of a sentence is a person, a named object
   or a measured fact: *`dbo.Customer` holds 5 rows*; *the Strict publish blocked the change*.
7. **The unverified admitted.** Application behaviour, other environments, production scale and
   reversibility go under *Not checked*, by name, and are never upgraded to proven.
8. **The next action last,** as a bare imperative at the object: `Run the orphan query in QA and
   UAT before promoting.` A section never ends at *done* without saying what remains.
9. **No numbered axis and no private nickname.** How it ships and what the lead weighs are
   sentences. *Magic line, the spine, the flip, the corpse, blast radius* and *naked rename* name
   nothing a reviewer can check; write the finding they stand for.

Write for a dev lead who knows SQL Server well and may be new to SSDT: gloss an SSDT term in one
clause where it first appears (*the refactorlog, the file that records a rename so the deploy keeps
the column's data*). The environments are Dev, QA, UAT and Prod; there is no environment called
Test. A sentence that names no count, object, type, state, message or quote is cut.

## The two findings, stated apart
**How it ships**, from the proof:
- `Ships as a single schema change, applied in place. No data is read or written.`
- `Ships as one release: the schema change, then a post-deployment script that runs after it lands.`
- `Ships as one release: a pre-deployment script prepares the data, then the schema change lands validated.`
- `Ships as a scripted change: <what> cannot be expressed as a table definition.`
- `Ships across <N> releases so the running application keeps working while the change is in flight.`

**What the approving dev lead weighs.** A dev lead approves every schema change and never their own,
so the finding names the weight: the change is additive and the running application is unaffected;
the running application must change to keep working; existing data is modified, or a relationship
between tables is added; data is removed and the removal cannot be undone, named in the approval.
Two facts add scrutiny when they hold: at production row counts the change may block writes or run
long (from `dbchange/ledgers/row-tiers.md`), and the operation is a first on these environments (no
row in `dbchange/ledgers/operations.md`). Dropping a populated table ships in one release and still
carries the heaviest weight: how simply a change ships says nothing of what its approval weighs.

## The ten sections
<!-- template:begin -->
# <object>: <plain change> (<one-clause consequence, if any>)
## Verdict
<!-- One line: what the change does, the confirmation it needs before promoting, and the one open item, if any. -->
## Intent
<!-- PLACEHOLDER: the developer's words, quoted, and the work item. -->
## What changes
<!-- `<object>`: `<from>` → `<to>`, one line per change; then the negative a reviewer would otherwise assume. -->
## Before promoting
<!-- One imperative per environment, each with who runs it: the author before merge, or the reviewer read-only. -->
## The data
<!-- The counts and the offending rows by name; for a remedy, rows before, rows after and who approved it. -->
<!-- PLACEHOLDER, when a question was asked: the answer, who gave it, and the work item. -->
## How it ships
<!-- The shape from the proof, and only the mechanics the developer must act on. -->
## What proving showed
<!-- Tried, did, realized: each publish, its message verbatim, the copy's name and the plan's script hash. -->
## After deploy — check
<!-- Queries with `-- expect <result>`; the Integration Studio refresh of the external entity, per environment. -->
## How to roll this back
<!-- The reverse steps; what is not undone automatically, with the recorded originals. -->
## Not checked / still open
<!-- PLACEHOLDER: the limits this change carries beyond the standing four, and the open question with its owner. -->
<!-- template:end -->

## What each section must carry
- **Verdict.** The whole description in one line, with no role in it: every change has a dev lead
  as its approver, so *a dev lead approves this* alone says nothing. When more than about five facts
  would change the lead's mind, the change is too large for one pull request; split it.
- **What changes.** The explicit negative is a finding: `No renames (refactorlog unchanged).`
  `No data is remediated.` is a complete section where nothing is.
- **How it ships.** The plan's own statements (`WITH NOCHECK ADD`, the `BlockOnPossibleDataLoss`
  check) are evidence and go under *What proving showed*. A change that loses data under this
  pipeline's Strict profile ships across two releases; never claim the profile was relaxed.
- **What proving showed.** Only the publishes of this branch. Each entry names the disposable copy
  and the script hash the proof carried; a claim with no provenance is an opinion and goes under
  *Not checked*. When a comparison of rows disagrees with a plain `=`, suspect the split between
  `''` and `NULL`, which the comparison keeps apart, or a collation that folds case, before the change.
- **After deploy — check.** Queries only, each runnable read-only in every environment. The change
  is complete when Integration Studio has refreshed the external entity in each environment.
- **How to roll this back.** Say whether the reverse loses data. A row a remedy deleted is restored
  only from its recorded original; when reversing was not exercised, say so.
- **Not checked.** Never empty. The standing four come first: the application's behaviour in Service
  Studio; whether the business answer is right; Prod's population until it is measured; any change
  between the last read of an environment and the deploy. Then what this change adds.

## An open question
When a proof surfaces a decision only a person can make (an orphan to delete or reassign, a value
to truncate, the value that fills the blanks), the description still ships: the question goes under
*Not checked / still open* with its owner, and the confirmation it forces goes under *Before
promoting*. The answer, when it comes, is recorded with who gave it and the work item.

## A compound pull request
A pull request that carries several operations writes the ten sections once. A line under the title
names each operation and its object; *What changes* and *The data* cover every one; *How it ships*
gives one line per release, which operations ride it and what forces the staging; *What proving
showed* reports each release's combined publish, since one release is one deploy script and its
strictest change decides it (F13); and *How to roll this back* runs the releases in reverse, naming
the release after which reversing loses data.
