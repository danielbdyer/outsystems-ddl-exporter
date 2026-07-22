---
name: author-review
description: Use when the reviewer writes its disposition — the review comment an author or lead actually reads. THE terminal artifact of the review half, the way author-pr is the authoring half's. Owns the canonical bodies for the four dispositions (approved · approved with a named risk · returned to the author · escalated with one question), each in the record register with its reproduced evidence beneath. The reviewer's procedure (reproduce, scope, attack, judge) lives in skills/review/*; this skill owns only what gets written down at the end.
---

# Author the review (the disposition, written down)

> **A disposition without its reproduced evidence is an opinion.** The review half's value is that
> every claim was re-run on a fresh copy — so the written disposition leads with what reproduced,
> not with agreement. It is written entirely in the record register (`../../THE_RECORD.md` §2):
> agentless findings, proof beneath, the next move named. It teaches nothing — when a returned
> change needs explaining, the change author re-renders the finding for the developer
> (`../../agents/change-author.md`, the return leg); the review comment itself stays terse.

## Where each disposition lands

| Disposition | In the PR tool |
|---|---|
| Approved | approve, with the evidence comment |
| Approved with a named risk | approve + the risk as a required-visibility comment for the lead's one-line accept |
| Returned to the author | request changes; the finding + fix as the comment. The lead is not tagged. |
| Escalated — one question | request changes + assign the lead; the question is the comment's last line |

One disposition per review. The evidence comment is short — a reviewer's credibility is spent by
every line that is not a finding.

## The four bodies

### Approved

Structure: what was reproduced (each claim → its observed result), then the release note.

```
Approved.

Reproduced on a fresh copy (PG_REV_..., sqlpackage 170.4.83):
- The generated script is a single sp_rename; the refactorlog entry is present. No drop, no rebuild.
- Strict publish: clean. All 5 rows present after the rename; the content-hash check moved only the
  renamed column's shape, as expected.
- Second publish: no changes issued.

Ready for the gate. Nothing requires the lead.
```

### Approved with a named risk

Structure: the approval, the risk with its artifact, the one-line ask. One risk per line; a risk
without its artifact is an undischarged obligation, not a named risk.

```
Approved with a named risk.

Reproduced clean on a fresh copy: the column widens in place, no rows rewritten, second publish
issues nothing.

The named risk: two consumers outside this project read Order.Total — the nightly ETL job and a
downstream reporting extract — and neither is in the dacpac, so their behaviour under the wider type
is not verified here (dependency map attached).

Accept that risk in a line, or hold for confirmation from the ETL owner.
```

### Returned to the author

Structure: the claim as submitted, what actually reproduced (verbatim error + count), the fix, the
re-prove ask. Goes to the author only — the change author re-renders it as a teaching fix for the
developer; this comment does not do the teaching.

```
Returned to the author.

The submission states the foreign key lands clean. Reproduced on a fresh copy: the deployment is
blocked — Msg 547, conflicted with FK_Order_Customer_CustomerId — 8 orphan rows (the orphan probe
was not run before submission).

The fix: reconcile the 8 rows per the recorded business decision, make the reconcile durable at
source (pre-deployment script, not a one-off UPDATE — the seed re-plants it), re-run the Strict
publish clean, and confirm is_not_trusted = 0.

Resubmit with the clean re-run in Deployment evidence. This does not need the lead.
```

### Escalated — one question

Structure: two lines of context, the homework named as done, the dependency map attached, then the
single question — last line, answerable in a word. Never three questions; never a status report.

```
Escalated — one question.

Making Email required on a populated table: the zero-blank backfill is proven (0 remain) and the
deployment is still blocked — the guard checks row presence, not blank values. The two honest paths
are a logged one-time relaxation of the data-loss guard, or a two-release staging. Both are
reproduced on a fresh copy; the dependency map is attached.

The question: is a one-time relaxation of the data-loss guard acceptable for this column — yes or no?
```

## Rules

- **Evidence first, always.** Every body opens with what reproduced, stamped with the copy's name
  and the sqlpackage version. A disposition that cannot cite its reproduction is not written.
- **The register is `../../THE_RECORD.md` §2.** Agentless — findings, not deeds ("Reproduced on a
  fresh copy", never "I reproduced"). True verbs. No axis numbers, no ceremony words, no teaching.
- **Route by disposition, exactly.** Returned-to-author never tags the lead; escalation always
  does, with exactly one question. A fixable defect escalated to the lead is a discipline failure —
  it spends the one resource the review half exists to conserve.
- **The ledger row rides along.** A named risk or an escalation also lands its row in the refusal
  ledger (`../review/verdict/SKILL.md`) with the proof artifact's path. The comment is the human
  surface; the ledger is the audit surface.
- **Terse is not curt.** Short sentences, complete ones. The author reading "Returned" should know
  the finding, the fix, and that it is routine — not feel graded.

## Wiring

Produced at the end of the review sequence (`../review/review-change/SKILL.md` →
`../review/dependency-scope/` → `../review/adversary/` → `../review/verdict/`): `verdict` decides
the level; this skill writes it down. The counterpart artifact on the authoring side is
`../author-pr/SKILL.md` — author writes the PR, reviewer writes the review, both from templates,
both in the record register.
