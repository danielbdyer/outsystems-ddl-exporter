---
name: author-pr
description: Use to turn a proven schema change into the pull request a reviewer approves by reading. THE terminal artifact of the tree — every operation skill feeds it. Produces the canonical Azure DevOps PR body (Summary · Review & release · Changes · Data remediation · Deployment evidence · Verification · Rollback · Not verified) in the record register of THE_RECORD.md. Evidence is summarized in the body; scripts ship inside the sqlproj; nothing is attached for the reviewer to run by hand. Use after prove-on-dacpac has confirmed the change and any remediation is durable at source.
---

# Author the pull request

> **The record, not the story.** The pull request is the one artifact a reviewer reads to decide.
> It is written entirely in the **record register** (`../../THE_RECORD.md` §2): agentless findings,
> proof beneath each, the next move named, and an honest account of what was not checked. No numbered
> axes (`THE_RECORD.md` §5), no nicknames (§7), no teaching — the teaching happened in the
> conversation with the developer and stays there. A Principal Engineer trained as a DBA approves
> this by reading it, without a meeting and without running anything.

You compose the PR body from what `prove-on-dacpac` established. You do not re-prove; you report.
Every claim in the body is one the disposable-copy run already demonstrated, carried here with its
evidence.

## What ships where (the reviewer runs nothing by hand)

- **The change ships in the sqlproj.** Edited `CREATE`s, the `.refactorlog`, `Script.PreDeployment.sql`,
  `Script.PostDeployment.sql` — all in the project, all part of the diff. The reviewer reads them in
  the PR; they are not attachments.
- **Evidence is summarized in the body**, not attached. The verbatim error, the row counts, the
  end-state probes go in **Deployment evidence** as text a reviewer reads. A `.json` artifact or a
  raw `.sql` transcript is not attached for the reviewer to execute — the finding plus its proof is
  the deliverable.
- **Verification queries are inline** so the reviewer can spot-check and the deployer can re-run them
  in each environment after promotion. They read; they do not depend on running.

## The canonical body

Fill every section. A section with nothing to report says so in one line (`No data is remediated.`)
— it is never dropped, because its absence is itself a finding a reviewer relies on.

```
# <table>: <plain summary of the change> (<one-clause consequence, if any>)

## Summary
<1–3 sentences: what changes, on which table/columns, and the business reason. Name the work item.>

## Review & release
- <Who must review, and why — one plain finding. THE_RECORD.md §5.>
- <How it ships — one plain finding. THE_RECORD.md §5.>
- <Added scrutiny, if any: CDC / large table / first-time — each its own line, or "None.">

## Changes
| File | Change |
|---|---|
| <path> | <what changed, plainly> |
<Then one line naming what is NOT touched where a reviewer would wonder: refactorlog unchanged;
 no index/view/procedure changes.>

## Data remediation
<If existing data is changed to let the constraint land: state the violating rows by name and count,
 the decision taken and who approved it, and the original values for audit. If none: "No data is
 remediated." >

## Deployment evidence — <disposable copy, e.g. Dev>, <date>, sqlpackage <version>
<What the publish did, as findings with proof beneath:
 - the blocked publish (before remediation), verbatim error + count, if applicable
 - the clean publish (after remediation), and the proven end state (is_not_trusted = 0, counts)
 - what the generated deploy script contained (the shape: ADD CONSTRAINT / sp_rename / no rebuild)
 - the second, no-op publish if idempotency matters>

## Verification — run in each environment after deployment
```sql
-- <what it proves, and the expected result>
<query>
```

## Rollback
<How the change is backed out and whether that is lossless; what is NOT auto-reversible, with the
 recorded originals that a manual restore would use.>

## Not verified
<Mandatory. The standing limits of a disposable-copy publish, specific to this change:
 - application impact — the exact new failure the running app will hit, and who owns confirming it
 - other environments — anything about Test/UAT/Prod data this copy cannot know
 - production scale / timing — blocking or duration the small copy cannot show
 - reversibility — if the forward publish is all that was proven>
```

## Section rules

- **Title.** `<table>: <change>` in plain words, with a parenthetical consequence only when there is
  one (`(one orphan row remediated)`). No axis, no nickname.
- **Summary.** The business reason belongs here — *why*, in one clause — because a reviewer approves
  intent, not just mechanics. Name the work item so the record is traceable.
- **Review & release.** Exactly the two findings of `THE_RECORD.md` §5, plus any added-scrutiny line.
  Never a number, never a grid.
- **Changes.** A file table, then the honest negative — what a reviewer might fear changed and did
  not. The negative is load-bearing: `refactorlog unchanged` tells a reviewer no rename is hiding.
- **Data remediation.** This is where a change earns or loses trust. Name the rows. State the
  decision and its human owner. Record the original values — a reviewer must be able to see what was
  changed and reconstruct it. "Nobody uses it" and "the data's clean" are claims; state the proof or
  state that it is unproven.
- **Deployment evidence.** Findings with proof beneath (`THE_RECORD.md` §2 rule 2). The blocked
  publish is *evidence the guard works*, not a failure to hide — show it, then show the clean re-run.
  Name what the generated script did so the reviewer need not imagine it. Stamp the sqlpackage
  version — the guard behaviour is empirical and version-bound.
- **Verification.** Queries that return an unambiguous expected result (`-- expect 0 rows`), runnable
  in any environment. These outlive the PR; they are how the deployer confirms each promotion.
- **Rollback.** State plainly whether it is lossless. A remediation `UPDATE` is not auto-reversed —
  say so, and point at the recorded originals. Do not claim reversibility the run did not prove
  (`THE_RECORD.md` §2 rule 7).
- **Not verified.** Required, every time. This is the sidekick admitting the edges: a disposable copy
  proves the schema transition against the data's *shape*, and is silent on the application, other
  environments, production scale, and backing the change out. Name the specific unverified thing for
  *this* change and who owns closing it — never a generic disclaimer, never omitted.

## Worked example — the go-live Order hardening

The change: add `FK_Order_Customer` (`Order.CustomerId` → `Customer.Id`) and `CK_Order_Total`
(`Total > 0`) to `dbo.[Order]`. One seeded row (`Order 4`, `CustomerId 999`) has no parent; all
totals are already positive.

```
# Order: add FK_Order_Customer and CK_Order_Total (one orphan row remediated)

## Summary
Two constraints are added to dbo.[Order] ahead of go-live (AB#1234). FK_Order_Customer makes
Order.CustomerId a real reference to Customer, so an order can no longer point at a customer that
does not exist. CK_Order_Total enforces Total > 0, so zero- and negative-value orders are rejected.

## Review & release
- A dev lead must review this: existing data is modified (one row) and a cross-table relationship is added.
- Ships as one release: a pre-deployment remediation, then both constraints land validated and trusted.
- Added scrutiny: none. dbo.[Order] is not CDC-tracked (cdc.change_tables checked 2026-07-16).

## Changes
| File | Change |
|---|---|
| Modules/Order.sql | Adds FK_Order_Customer and CK_Order_Total to the table definition |
| Script.PreDeployment.sql | Reassigns one orphan Order row before the foreign key validates |
No renames (refactorlog unchanged). No index, view, or procedure changes.

## Data remediation
One row violates the foreign key: Order 4 holds CustomerId 999, and no such customer exists.
- Decision: reassign to CustomerId 1 (approved by J. Whoever, AB#1234). Deletion was rejected — it
  would also remove OrderLines 7 and 8.
- Rows affected: 1. Original value recorded for audit: Order 4, CustomerId 999 → 1.
- The check constraint has 0 violating rows: all four Totals are positive (verified 2026-07-16).

## Deployment evidence — disposable copy of Dev, 2026-07-16, sqlpackage 170.4.83
- Without the remediation, the deployment is blocked: Msg 547 — conflicted with FOREIGN KEY
  constraint "FK_Order_Customer" (dbo.Customer, column Id). This confirms the constraint validates.
- With the remediation, the deployment succeeds. Both constraints end trusted (is_not_trusted = 0).
- The generated deploy script adds each constraint WITH NOCHECK, then re-validates WITH CHECK CHECK —
  two ADD CONSTRAINT statements and the one remediation UPDATE. No table rebuild, no drops.
- A second publish of the same build issued no changes.

## Verification — run in each environment after deployment
```sql
-- expect 0 rows: every order points at a real customer
SELECT o.Id, o.CustomerId FROM dbo.[Order] o
LEFT JOIN dbo.Customer c ON c.Id = o.CustomerId WHERE c.Id IS NULL;

-- expect both rows, is_not_trusted = 0
SELECT name, is_not_trusted FROM sys.foreign_keys     WHERE name = 'FK_Order_Customer'
UNION ALL
SELECT name, is_not_trusted FROM sys.check_constraints WHERE name = 'CK_Order_Total';
```

## Rollback
Both constraints drop without data loss:
ALTER TABLE dbo.[Order] DROP CONSTRAINT FK_Order_Customer;
ALTER TABLE dbo.[Order] DROP CONSTRAINT CK_Order_Total;
The remediation UPDATE is not auto-reversed; the original value (Order 4 → 999) is recorded above.

## Not verified
- Application impact. Any code path that inserts an Order before its Customer exists, or writes
  Total <= 0, will now fail with error 547. Application-side validation is not confirmed — @app-owner.
- Other environments. The origin of CustomerId 999 is unknown; if it was a placeholder convention,
  Test and UAT may hold more orphans. Run the verification query before promotion.
```

## Composing from the operation skills

Each operation skill states, in its own `## On the record` fragment, what it contributes to these
sections: its **Review & release** findings, its **Verification** query, its **Rollback** statement,
and its standing **Not verified** items. Assemble the body from those fragments; the operation skill
owns the specifics, this skill owns the shape and the register.

## Hard rules

- **The register is `../../THE_RECORD.md` §2.** Agentless, findings-first, proof beneath, next move
  named, limits admitted. Run the §7 banned list before the body lands.
- **Nothing is attached for the reviewer to run.** Scripts ship in the sqlproj; evidence is
  summarized as text; queries are inline for reading and later re-running.
- **Do not re-prove in the PR.** The body reports what `prove-on-dacpac` established. If a claim was
  not proven, it goes under **Not verified**, not under **Deployment evidence**.
- **Everything authored stays under `ssdt-agent/`.** The PR body is composed, not written into the
  repo tree, except where a real change set lives in the proving ground or a real project.
