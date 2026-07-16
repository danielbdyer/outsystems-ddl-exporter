---
name: verdict
description: The four dispositions, the refusal ledger, and escalation routing for the lead's adversarial reviewer. Use after review-change has reproduced a change and dependency-scope + adversary have run. Folds reproduce + scope + attack into exactly one disposition — Approved, Approved with a named risk, Returned to the author, or Escalated with one question for the lead — logs every named risk and escalation in the ledger with its proof artifact, and routes it: a return goes to persona-1 (change-author re-renders the finding as a teaching fix in conversation; the lead never sees it), an escalation goes to the human lead with the dependency map and the single specific question, the homework done. Reuses self-test/rubric.md's dimensions as the audit checklist a change must pass to be approved.
---

# Verdict (the disposition)

> **The disposition carries its route.** A review that ends at "looks fine" or "no" is noise. Each
> change resolves to exactly one of four dispositions, and each disposition names where it goes next:
> an approval goes straight to the deploy gate; a fixable defect returns to the author without ever
> reaching the lead; only an irreducible judgment reaches the human lead — and it arrives with the
> dependency map and the single question, the homework done. Escalation is the one resource this tree
> exists to conserve; it is spent only on the design decision that cannot be resolved below it.

The inputs are the reproduce result, the dependency map (with its scope ceiling), and the adversary's
findings. The output is exactly one of four dispositions — logged in the refusal ledger and routed. No
guard or rule is restated here; every finding points to its `_index` owner for the why.

## The rubric is the audit checklist (reused, not rebuilt)

A change is **Approved** only if it passes the same fitness bar the author was scored on.
`self-test/rubric.md`'s **six criteria** and **seven metrics** are the reviewer's grading lens — the
change is graded by the same dimensions the author is:

1. intent + op-slug correct · 2. state-variables proven (not read) · 3. both findings emitted (how it
ships, who must review) · 4. named trap caught in the delta · 5. the decisive finding named, with a
remedy that re-passes · 6. reasoning surfaced from the correct `_index` owner. Plus the metrics:
how-it-ships and who-reviews accuracy, block-prediction, negative-case refusal-correctness,
flip-discriminator, reasoning-source. **A packet that fails any of the six is not Approved** — it is
at least Returned to the author, and which criterion failed selects the route.

The change under review arrives as a pull request authored per `skills/author-pr`; the disposition is
recorded against that body, and every criterion is checked against what the PR states and proves.

## The decision tree (finding -> disposition)

```
reproduce failed? ─── yes ──> is it a design fork / irreversible step?
      │                           │
      │                           ├─ yes ──> Escalated
      │                           └─ no  ──> Returned to the author
      │
      no (reproduces as claimed)
      │
      ├─ adversary found a DEFECT the developer can fix without the lead?
      │       (missing refactorlog · skipped orphan probe · over-length narrow ·
      │        unguarded MERGE · NOCHECK-left-untrusted · stale refactorlog)
      │            └─ yes ──> Returned to the author
      │
      ├─ adversary / dependency map found an IRREDUCIBLE judgment?
      │       (populated make-mandatory still blocks after backfill = relax the data-loss
      │        guard vs stage over releases · CDC no-gap vs tolerable-gap · a deep cascade ·
      │        an irreversible drop that removes data)
      │            └─ yes ──> Escalated  (attach dependency map + one question)
      │
      ├─ dependency map has an un-scoped external/ETL consumer OR a reversibility
      │  asserted but not proven the lead should accept?
      │            └─ yes ──> Approved with a named risk  (one-line lead accept/override)
      │
      └─ every obligation discharged, no downgrading finding, within scope ceiling
                   └──────> Approved
```

**The scope ceiling caps the disposition.** The dependency map's `scope ceiling` is the highest
disposition available for the change. A caught defect can lower it, but nothing raises it above the
ceiling — an Approved above an un-enumerated cascade or external consumer is invalid.

## The four dispositions (and their routes)

- **Approved** — every proof obligation discharged, reproduced clean or blocked exactly as claimed,
  no downgrading finding, within the scope ceiling. **Route:** straight to the deploy gate. Zero lead
  time. The discharged obligations are logged in the ledger.
  > "Approved. Strict clean on a fresh DB, the delta is one sp_rename, the refactorlog is present. Straight to the deploy gate."

- **Approved with a named risk** — sound *given* a logged, accepted risk (an out-of-band consumer the
  dacpac cannot see; a reversibility asserted but not proven — `prove-on-dacpac` proves the forward
  publish only). **Route:** to the lead as a one-line accept/override, not a discussion. The risk is
  logged in the ledger with its artifact.
  > "Approved with a named risk. vOrderSummary and the nightly ETL both read this column and neither is in the dacpac, so their behaviour is not verified here. Accept the out-of-band consumers in a line, or hold for confirmation."

- **Returned to the author** — a real defect the developer can fix without the lead (missing
  refactorlog, skipped orphan probe, over-length narrow, unguarded seed MERGE, FK left at NOCHECK).
  **Route:** back to persona-1; `change-author` re-renders the finding as a teaching fix in the
  conversation register, and the developer re-proves. The lead never sees it.
  > "Returned to the author. Labeled an in-place change, no data touched; reproduced, Strict blocks the publish — 8 orphans (Msg 547), and the orphan probe was never run. Fix: NOCHECK -> reconcile the 8 -> WITH CHECK CHECK, prove is_not_trusted=0. Does not need the lead."

- **Escalated — one question for the lead** — a genuine design fork or irreversible-step judgment
  (populated make-mandatory that still blocks at zero NULLs; CDC no-gap vs tolerable-gap; a deep
  cascade; an irreversible drop that removes data). **Route:** to the human lead, with the dependency
  map attached and one specific question, the homework done. Name the escalation plainly — a real
  judgment for the lead, not a hedge.
  > "Escalated — one question for the lead. Make-mandatory, populated, 1.2M rows, CDC-tracked. Backfill reaches zero NULLs and Strict still blocks — the guard fires on table-has-rows, not column NULLs. The call is between relaxing the data-loss guard once the zero-NULL count is proven and staging over releases; the table is CDC-tracked, so the capture instance is frozen to its current columns and needs handling. Dependency map attached. One question: do downstream consumers tolerate a capture gap, yes or no?"

## The refusal ledger

Every named risk and every escalation logs a row — the disposition is auditable, not verbal:

```
LEDGER — <op> on <object>
  disposition:      Approved with a named risk | Escalated
  risk/escalation:  <one line — the named consumer / the design fork>
  proof artifact:   <the delta line · the Msg + count · the still-blocks probe · the dependency map path>
  routed to:        lead (accept/override) | lead (escalation + 1 question) | persona-1 (teaching fix)
  the one question (if escalated): <the single yes/no the lead must answer>
```

A downgrade is never silent: a risk without its artifact in the ledger is not a named risk — it is an
un-discharged obligation, and the change is not approved.

## The escalation contract (the two routes, exactly)

- **Returned to the author -> persona-1.** The finding is terse and peer-level, but it does not reach
  the lead. It reaches `change-author.md`, which re-renders it in the conversation register (§3) so
  the developer learns the *why* and re-proves. The reviewer's terse finding becomes the developer's
  lesson in conversation.
- **Escalated -> the human lead.** Only the irreducible judgment. It arrives with the dependency map
  and **one** question — not three, not a status report. The homework — reproduce, scope, the rows
  that would be lost or the Msg, the still-blocks proof — is already done; the lead makes the call,
  not the diligence.

Escalate **only** the irreducible judgment. A fix the developer could make, escalated to the lead
instead, is a discipline failure — it wastes the one resource the whole tree exists to conserve.

## What this skill REUSES (does not rebuild)

- `self-test/rubric.md` — the six criteria + seven metrics, as the audit checklist a change passes to be approved. No new rubric.
- `skills/review/dependency-scope` — the scope ceiling that caps the disposition.
- `skills/review/adversary` — the findings that force a downgrade.
- `agents/change-author.md` — the re-render target for a change returned to the author (persona-1).

## What this skill deliberately does NOT build

- **No new grading rubric** — the fitness dimensions are `self-test/rubric.md`'s; the reviewer-specific
  additions (reproduced-not-read, disposition-correct, escalation-discipline, terse-peer-voice) live
  in `self-test/review-rubric.md`, not here.
- **No guard, mechanic, or trap is re-explained here** — every finding points to its `_index` owner for the why.
