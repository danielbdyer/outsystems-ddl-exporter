# Phase 2 — the instructional curriculum

**A plan for the second act: moving the team from using the tool to not needing it.** Written
2026-08-24, to be executed after the cutover stabilizes. Phase 1 is the tool — the agent authors
and reviews schema changes safely, now (see `ENABLEMENT_PROGRAM.md` and `ASSESSMENT_2026_08_24.md`).
Phase 2 is the curriculum that turns the four reviewers and the wider team into people who can
author and review without the agent driving, so the manager can leave the individual-contributor
review rotation and run the team.

This plan does not invent much new material. Most of what a curriculum needs already exists in the
tree — 41 worked pull requests, a golden exemplar, 52 graded prompts, the review scenarios and their
rubric, the proving substrate, and the record standard. Phase 2 sequences that material into a
learning path, adds a way to score a *person's* work rather than an agent's, and sets a cadence. The
objectives it builds on are named in `ENABLEMENT_PROGRAM.md`: O5 (real certification), O6 (the estate
ledger), O8 (the dojo), and the weekly competence loop of §5.

---

## 1. The objective

The team is **self-sufficient** when it authors and reviews routine schema changes correctly without
the agent driving — the agent becomes a checker and a safety net, not the pilot. Two outcomes follow
from that, and they are the real goals:

- The four named reviewers review routine changes on their own judgment, reproducing the proof when
  they need to and, on the common operations, catching a wrong classification without re-running it.
- The manager (and the principal) leave the routine review rotation. They stay for genuine
  escalations and the occasional audit, not for the day-to-day. That is what "get back to managing"
  means in practice, and it is the plan's end state.

Self-sufficiency is measured by competence, not by a calendar. The plan below is a route to it, not a
schedule that reaches it by a date.

---

## 2. What "self-sufficient" means, concretely

A person moves through four capabilities, in order, each one less dependent on the agent than the
last:

1. **Read the record.** Understand a proven pull request and act on it — run its checks, promote it,
   catch when its "Not checked" section names something they own. This is passive, and it is where an
   SSDT-new developer starts.
2. **Review with reproduction.** Reproduce the agent's proof on a disposable copy and judge the
   disposition against it. This is the cutover-week baseline for the four reviewers: the agent's
   reproduced proof is the compensating control while their own SSDT depth is still building.
3. **Review without reproduction.** Catch a wrong classification because they understand the
   mechanism — the locked gate, the table-has-rows guard, the phantom rename, constraint trust —
   without needing to re-run the agent for the common operations. This is the graduation from
   trusting the proof to knowing why it holds.
4. **Author without the agent driving.** Shape a change correctly before proving — edit the CREATE,
   name the two-release, spot the trap — and use proving to *confirm* rather than to *discover*. The
   agent becomes a checker.

The curriculum is a ladder of decreasing dependence: the agent drives, then the agent proves and the
human judges, then the human judges and the agent confirms, then the human authors and the agent
checks. Self-sufficiency is the top of that ladder held by enough of the team.

---

## 3. The competency ladder

The playbook already carries a five-level capability ladder (`ssdt-playbook/Process/Capability-Development.md`:
Observer → Supported Contributor → Independent Contributor → Trusted Contributor → Dev Lead). Phase 2
pins each level to a capability from section 2 and to a place on the agent-dependence ladder, so
"ready to move up" has a concrete test rather than a feeling.

| Level | Capability held | The agent's role | The test to move up |
|---|---|---|---|
| **Observer** | Reads the record (1) | Drives everything | Completes the first three authoring katas with the agent driving, and can explain what each proof showed |
| **Supported Contributor** | Reviews with reproduction (2); authors simple ops with the agent | Drives; the human judges the result | Reproduces a proof on a disposable copy and reaches the right disposition, with help available |
| **Independent Contributor** | Reviews with reproduction reliably; authors most single ops | Proves; the human judges without help | Passes the review-ready certification with reproduction, across the common op families |
| **Trusted Contributor** | Reviews the common ops *without* reproduction; authors the hard ops (tightening, multi-phase) with the agent as checker | Checks; the human leads | Passes the review-ready certification *without* reproduction on the common ops, and the author-ready certification |
| **Dev Lead** | The full reviewer role — escalations, the hard dispositions, mentoring | Safety net | Sustained Trusted-level work plus handling real escalations soundly |

Two people reaching Trusted Contributor is the gate for the manager's exit (section 7). Not everyone
needs to become a Dev Lead; Trusted Contributor is a successful end state, as the playbook already
says.

---

## 4. The curriculum — the dojo

The dojo (`ENABLEMENT_PROGRAM.md` O8) is the vehicle. It is a graded sequence of real changes —
katas — that a learner works on the proving ground, then compares against the known-good outcome the
tree already holds. Each kata is: run the real loop on a disposable copy, produce the pull request,
then diff it against the matching worked example under `sample-prs/`. The two tracks share the same
cases:

- **The authoring track** — ten katas from the safest change to the hardest gate, roughly:
  1. `add-optional` — the safest change; also where the two-register discipline is taught (a developer
     reads the conversation, a reviewer reads the record).
  2. `create-entity`, `create-static-seed` — additive shapes and the seed.
  3. `create-fk-clean` then `create-fk-orphan` — a constraint as a claim, and the reconcile.
  4. `widen` then `narrow` — the same-looking edit that ships two different ways, and the tightening
     trap.
  5. `rename-attribute` — the refactorlog, and the phantom rename.
  6. `delete-attribute` — the two-release drop.
  7. A multi-phase program — `extract-to-lookup` or `merge-tables` — expand, migrate, contract.
  8. **Capstone: `make-mandatory` on a populated table**, reproducing the golden. This is the change
     that corrects an intuition (clearing the blanks does not clear the block), so a learner who
     reproduces it has met the tree's central proof first-hand.
  A new kata is added past the capstone: **compound decomposition** — take one compound need through
  the `decompose` skill, produce the plan, and check it against the worked example in that skill. This
  teaches the whole-feature shape, not just the single op.
- **The reviewing track** — the same cases, worked from the review rubric: reproduce the author's
  proof, scope the dependencies, attack the claim, reach a disposition. The reviewer katas ride the
  authoring cases plus the planted-defect scenarios in `self-test/review-prompts.md`. A capstone kata
  here is **review without reproduction**: read a pull request cold, on an op the learner has already
  certified with reproduction, and catch a planted wrong classification from the mechanism alone. That
  kata is the test for the Trusted level.

What is reused: the 41 sample PRs (the worked answers), the golden (the model), the 52 prompts (the
exercises), the review scenarios and rubric (the graded cases), the proving substrate, and the record
standard. What Phase 2 builds is the `dojo/` path that orders these into a route with an entry and an
exit per rung, and the Start-Here link repair the playbook needs first (`ENABLEMENT_PROGRAM.md` O8).

---

## 5. Certification — how a person is measured

The self-test apparatus already scores an *agent's* run — retained artifacts, a rubric, a golden,
planted defects, anti-gaming (`ENABLEMENT_PROGRAM.md` O5). Phase 2 points the same apparatus at a
*person's* work product. There are two certifications, and they map to the ladder:

- **Review-ready.** The person catches every planted defect in the review suite and reaches the right
  disposition — first *with* reproduction (the Independent level), then *without* it on the common ops
  (the Trusted level). The cases rotate and the data differs from the golden, so the certification
  cannot be passed by memorizing an answer — the same discipline that keeps the agent's own scoring
  honest (O5's anti-gaming: score the artifacts, re-derive the numbers, never the prose).
- **Author-ready.** The person's pull request on a fresh case scores at or above the bar against the
  matching sample-PR standard, with the agent used as a checker rather than a driver — the person
  shapes the change and names how it ships before proving confirms it.

Scores land in the same kind of committed ledger O5 builds for the agent, so each person has a trend
and each teaching session has a before and after. The ledger is per-person for the four reviewers and
per-cohort for the wider team.

One rule keeps certification from becoming a checkbox: a certification is a *live* judgment, not a
badge. It is re-taken when the tool version changes (a new engine behavior can move a guard) or when a
person has not exercised an op family in a while. This mirrors the tree's own liveness discipline —
a proof decays, and so does a person's currency on a rarely-used operation.

---

## 6. The teaching machinery

The curriculum does not run on katas alone. Four things carry it, and three of them already exist:

- **The agent teaches, in conversation.** Every real change the team ships is a teaching moment, by
  design: the developer reads the conversation register (`THE_RECORD.md` §3), which explains why a
  change behaves as it does, in one or two plain sentences. The reviewer reads the record register,
  which teaches nothing and shows evidence. The two-register split is itself a teaching structure —
  learn from the conversation, judge from the record.
- **The team's own history is the best casebook.** The sample PRs are worked examples on a toy
  schema; the team's *own* merged pull requests, on their real tables, are more relevant and more
  memorable. The estate ledger (O6) already accrues every shipped change; Phase 2 curates a running
  index of the team's own changes as teaching cases — the first orphan they reconciled, the first
  two-release they shipped, the first drift they caught on a QA promotion. This casebook is fed by the
  work, not written separately, and it grows more valuable than the sample PRs as the estate accrues
  precedent.
- **Pairing.** The absence rule already pairs two senior reviewers on a principal-level change; Phase 2
  uses the same pairing as teaching — the pair reproduces together, and the more-fluent one narrates
  the mechanism. When the principal returns, they pair with each senior on the hard ops before signing
  off alone becomes routine.
- **The weekly rhythm.** The competence loop (`ENABLEMENT_PROGRAM.md` §5, Loop B) runs weekly: the
  certification runs, the scores land in the ledger, and the lowest-scoring skill or the weakest
  capability gets the next teaching session. The fix's before and after are two ledger rows. This is
  the flywheel that makes the curriculum improve itself rather than being written once and decaying.

---

## 7. The manager's exit — the milestone that matters

The plan's end state is the manager leaving the routine review rotation. It is a real milestone with
real gates, and it should not be crossed early. It is reached when all of these hold:

1. **At least two of the four reviewers are at Trusted Contributor** — they review the common ops
   without reproduction and have passed both certifications. Two, not one, so the estate is never
   dependent on a single person's availability.
2. **The estate has precedent.** The operations ledger holds enough shipped history that "first time
   on this estate" is rare on routine changes — the team is applying operations it has applied before,
   with its own proof to lean on, not the sample's.
3. **Escalations are rare and genuinely irreducible.** The changes that still reach the manager or the
   principal are real design decisions, not mechanics the team should now own. If routine mechanics are
   still escalating, the team is not ready and the teaching is not done.
4. **The principal has returned and the deputization has wound down** — the absence rule is no longer
   the standing mode, and principal-level changes have a principal again.

When those hold, the manager exits the routine rotation. They keep three roles: the occasional audit
(read a sample of merged PRs against the standard), the genuine escalation (the irreducible design
call), and the owner of the curriculum's cadence (the weekly loop still needs someone to run it, at
least until it is fully self-sustaining). Full self-sufficiency is when even the cadence runs without
the manager prompting it — the fourth loop turning on its own.

---

## 8. What to build

Most of the content exists; Phase 2 is assembly and a scorer. In rough order of value:

- **The `dojo/` path (O8).** Sequence the katas into a route with an entry and exit per rung; repair
  the playbook's Start-Here links so the reading paths are not dead. Add the compound-decomposition
  kata (the `decompose` skill's worked example is the answer key) and the review-without-reproduction
  capstone. *Effort: 1–2 sessions; the material exists.*
- **Human certification (O5, extended).** The self-test already scores an agent; extend the scorer to
  score a person's submitted pull request or review disposition against the same rubric and golden,
  and stand up the per-person and per-cohort ledgers. Add the "score without reproduction" mode for
  the Trusted-level test. *Effort: 2–3 sessions; builds on O5's rails.*
- **The estate casebook (O6, curated).** A running index of the team's own merged pull requests as
  teaching cases, fed by the estate ledger. *Effort: light and ongoing; it is curation, not
  authoring.*
- **The cadence (the flywheel's Loop B, staffed).** A short operating-rhythm note: who runs the weekly
  certification, how the lowest-scoring capability gets the next session, how the manager's audit
  sample is drawn. *Effort: one session to write; the discipline is the work.*

Everything here obeys the tree's ratchet rule (`ENABLEMENT_PROGRAM.md` §5): no piece of the curriculum
lands without the detector that would catch its absence — a kata lands with its scoring, a
certification lands with its ledger, so the curriculum cannot silently rot any more than the corpus
can.

---

## 9. Timeline, relative to the cutover

Phase 2 follows Phase 1; you cannot teach off a tool that is not yet delivering. A realistic shape,
in seasons rather than dates:

- **Cutover week and stabilization.** Phase 1 is in use; the four reviewers work at the Supported
  level with the agent's reproduced proof as the compensating control. No curriculum yet — the tool is
  proving itself and the team is using it under the absence rule.
- **Weeks after stabilization.** Build the `dojo/` path and run the first authoring and reviewing
  katas. The team moves Observer → Supported → Independent as the katas and the real work accrue. The
  estate casebook starts filling from real changes.
- **The following months.** The weekly competence loop runs. Reviewers reach for the review-without-
  reproduction certification on the common ops, moving toward Trusted. The estate ledger accrues
  precedent, and "first time on this estate" thins out.
- **The manager-exit milestone.** When section 7's gates hold — two reviewers at Trusted, precedent in
  the ledger, escalations rare, the principal back — the manager leaves the routine rotation.

The playbook's own progression expectations are the honest pace: Observer to Supported in about a
week, the later rungs variable and part-time, because the team has a day job. Do not compress it into a
schedule; let competence set the pace.

---

## 10. Risks and dependencies

- **Phase 2 depends on Phase 1 being delivered where the team works.** A curriculum built on a tool the
  team cannot run in GitHub Copilot and Visual Studio, against their real data, teaches nothing. The
  proving path on a real Windows machine (`PROVING_PATH_WINDOWS.md`) and the Copilot packaging
  (`copilot-package/`) are hard prerequisites, not Phase 2 work.
- **Certification can rot into a checkbox.** The whole value is that it catches a person who cannot yet
  catch a defect. The anti-gaming discipline (rotate the cases, score the artifacts, re-derive the
  numbers) and the "catch it cold" bar are what keep it real. A certification that everyone passes on
  the first try is measuring nothing.
- **Time.** The seniors have day jobs; the ladder is climbed part-time. Over-scheduling the curriculum
  will stall the real work it is meant to support. The weekly loop is a light touch, not a training
  program that competes with delivery.
- **Exiting the rotation too early.** The manager's exit is gated by competence, not by wanting it. If
  the seniors are still escalating routine mechanics, the exit is premature and the safety net is gone
  at the worst time. Hold the gate.

---

## 11. Done — when the team does not need this plan

Phase 2 is complete when the team is self-sufficient, by these signs, which extend the program's own
done-when (`ENABLEMENT_PROGRAM.md` §7, item 6):

- A cohort developer completes the dojo's katas, and their next real pull request is approved by
  reading, by a peer who reproduced it — not by the manager, not by the principal.
- Two of the four reviewers are at Trusted Contributor: they review the common ops without
  reproduction, and their certification ledgers show a stable or rising trend.
- The estate ledger holds enough precedent that routine changes are no longer "first time on this
  estate," and the review-routing map runs on the team's own history.
- The manager has left the routine review rotation, and the weekly competence loop runs without the
  manager prompting it.

At that point the tree has done what it was built to do: it disappears into the team's own competence,
and what remains is a group that owns its database, changes it safely, and reviews itself — with the
agent as a checker and a safety net, not a pilot.
