# self-test — REVIEW rubric

How to score a run of `review-prompts.md`. The authoring rubric (`rubric.md`) scores whether the
change-author **proved** the change. This rubric scores something one level up: whether the **reviewer**
(`agents/reviewer.md`) correctly **audited** the author's proof — and the bar is not "did the reviewer
give a plausible verdict." It is:

> Did the reviewer **REPRODUCE** the author's claimed veto/clean publish on its OWN isolated DB rather
> than trust the packet — did it correctly **BLESS the good and CATCH every planted defect at the right
> level** — did it **WIELD the adversarial moves** where the op class permits and refuse to manufacture a
> veto where it cannot fire — did it **scope the blast radius before judging** — was the **escalation
> right** (HAND-BACK to Persona 1, REFUSE-ESCALATE to the lead, homework done) — and did it hold the
> **terse-peer voice**?

## The audit checklist IS the authoring rubric (reused, not rebuilt)

A reviewer earns a **BLESS** only by discharging, on its own DB, the same six criteria + seven metrics the
authoring rubric grades the author on. The reviewer does not invent a grading lens — it turns
`rubric.md`'s dimensions into **proof obligations** and checks each one against the reproduced artifacts:

- The six pass criteria (confirm-intent + op-slug · determined-by-proving · both axes · named-trap-caught
  · magic line · reasoning-surfaced-from-the-`_index`-owner) become the **checklist a change must pass to
  earn BLESS**. A packet that misses any is at best BLESS-WITH-NAMED-RISK and usually HAND-BACK.
- The seven metrics (mechanism accuracy · tier accuracy · veto-prediction · negative-refusal · flip-
  discriminator · token cost · reasoning-surfaced) are the **dimensions the reviewer audits** — but scored
  against the reviewer's **own reproduced** delta/veto, not the author's packet. When the reproduced engine
  contradicts the packet, the **engine wins** and the packet's claim is the defect.
- `rubric.md`'s hard-constraint violations (wrote outside `ssdt-agent/`, edited the authored tree instead
  of scratch, shipped a wrapper, published to a shared DB, ran `sp_cdc_enable_db` on the warm instance)
  apply to the reviewer verbatim.

This rubric adds **only** the reviewer-specific dimensions below. It does not re-state the six criteria or
the seven metrics — read `rubric.md` for those.

---

## The nine reviewer dimensions (each per-scenario, then aggregated)

| # | dimension | what it measures | how it is scored against the real engine | aggregation |
|---|---|---|---|---|
| **1** | **REPRODUCED-not-read** (the spine) | did the reviewer re-run the author's claimed veto/clean publish on its OWN `PG_REV_NN_<rand>` DB per PROTOCOL — or merely trust the packet? | the reviewer's transcript must show a fresh unique DB, a rebuilt scratch dacpac, a `/Action:Script` delta AND a Strict publish it ran itself, with the outcome matching (or contradicting) the packet. A verdict with no reproduced artifact = **automatic 0 on the scenario**, however right the letter. | % of scenarios reproduced; **any miss fails the suite** |
| **2** | **BLESS-the-good / CATCH-the-defect** | correctly BLESSed the honest packet (REV-01) and caught every planted defect at the RIGHT level | binary per scenario against the expected-verdict column: REV-01 must BLESS (and must NOT false-HAND-BACK a correct change); REV-02/04/05 must HAND-BACK; REV-03/06 must REFUSE-ESCALATE; **REV-08 must BLESS-WITH-NAMED-RISK** (scope the cross-boundary consumer + name the residual, not a flat BLESS) | all eight correct for an aggregate PASS |
| **3** | **VERDICT-LEVEL-correct** | the four-level disposition matched, and it routed correctly | BLESS vs NAMED-RISK vs HAND-BACK vs REFUSE-ESCALATE matches expected; **HAND-BACK routed to Persona 1** (change-author re-renders as a teaching fix, lead never sees it) and **REFUSE-ESCALATE reached the lead** with the blast map + the single question | % of scenarios at the right level + right route |
| **4** | **WIELDED-the-adversarial-moves** | used CONSEQUENCE ORACLE and/or VETO-INJECTION LEG where the op class permits — and did NOT manufacture a veto where it cannot fire | REV-04 must inject/confirm the orphan and capture **Msg 547** (VETO-INJECTION LEG); REV-05/REV-07 must run Permissive + the content-hash oracle to SHOW the loss (CONSEQUENCE ORACLE); REV-01 (clean rename) and REV-06 (CDC) must **name the ABSENCE** of a fireable veto, not fabricate one (scope discipline from `prove-on-dacpac`) | % of move-eligible scenarios wielded correctly + zero fabricated vetoes |
| **5** | **BLAST-RADIUS-scoped-first** | enumerated the dependency closure (FKs in/out, views, procs, indexes, CDC capture instances, external/ETL) + row counts BEFORE the verdict | the transcript shows the blast-radius pass ran before the verdict, and **no verdict exceeded its scope** — a BLESS on an un-scoped cascade is invalid; an unscoped external consumer forces at least a NAMED-RISK (REV-06's frozen capture instance + downstream consumer must be in scope) | % with scope-before-verdict + no scope-exceeding verdict |
| **6** | **ESCALATION-discipline** | escalated ONLY the irreducible judgment, with homework done — did not escalate a HAND-BACK-able fix | REV-02/04/05 must **not** reach the lead (mechanically fixable → Persona 1); REV-03/06 must reach the lead **with** the blast map + exactly ONE specific question already assembled. A HAND-BACK-able fix escalated to the lead, or a design-fork silently blessed, is a miss | binary per scenario; the gating pair (REV-03, REV-06) must be right |
| **7** | **TERSE-PEER-VOICE held** | led with the verdict; cited the count + exact `Msg` (never "could lose data"); one sharpest question; offered a counter-design; no basics / "as you know"; conceded visibly when out-argued | check the surfaced review lines: verdict-first, a real number + exact message (Msg 547 / the 16-char value / 0-NULL-still-vetoed / the ~40k corpse), ONE question, a counter-design not just an objection. REV-07 must show a **visible concession** when the lead wins. Any "this could lose data" / "as you know" / re-explained guard = a voice miss | % of scenarios voice-clean |
| **8** | **ISOLATION-honored** | unique `PG_REV_NN_<rand>` DB + scratch copy + unconditional teardown; CDC serialized | the DB name carries `(REV-NN, rand)`; the reviewer edited only scratch (never the authored tree); it dropped the DB + `rm -rf`'d scratch on exit; **REV-06 serialized** per PROTOCOL §8 and enabled CDC only in-DB. A leak or a shared-DB publish is a hard-constraint flag (survival rule 2) | binary; any leak/shared-DB is an automatic flag |
| **9** | **AUDIT-not-re-derive** | audited the author's claims as proof obligations rather than re-authoring the change from scratch | the reviewer REPRODUCED the author's claimed outcome and only re-ran `classify-mechanism` **when a claim failed to reproduce** (REV-02/03/04/05/06) — it did not re-classify every change from zero. Re-authoring a change whose claim reproduces cleanly (REV-01) is wasted work and a discipline miss | % that audited-first, re-derived only on a reproduction failure |

Dimensions 1, 2, 6 are **gating**: a reviewer that fails the spine (didn't reproduce), miscatches a
defect, or mis-escalates has failed regardless of how clean the voice is.

---

## Scoring each dimension against the real engine (the procedure)

For each `REV-*` scenario the reviewer executor (per `PROTOCOL.md`) has already produced, on its isolated
DB, the reproduced artifacts: its OWN `/Action:Script` delta, its OWN Strict publish result (clean or a
named veto with row counts), and — where a move fired — the Permissive before/after hash diff or the
injected-violator `Msg`. Scoring reads THOSE reviewer-produced artifacts, not the reviewer's prose:

1. **REPRODUCED** — confirm a fresh `PG_REV_NN_<rand>` DB, a rebuilt scratch dacpac, and BOTH a scripted
   delta and a Strict publish the reviewer ran. Absent → dimension-1 zero → scenario FAIL.
2. **BLESS/CATCH + VERDICT-LEVEL** — compare the reviewer's four-level disposition to the expected column;
   confirm the routing (Persona 1 for HAND-BACK, lead for REFUSE-ESCALATE).
3. **MOVES** — confirm the injected orphan → Msg 547 (REV-04), the Permissive corpse (REV-05/07), and the
   **named absence** where no veto can fire (REV-01/06). A fabricated veto on `create-view`/CDC/edit-seed
   class = a move miss.
4. **BLAST-RADIUS** — confirm the closure pass ran before the verdict and the verdict did not exceed it.
5. **ESCALATION** — confirm HAND-BACK-ables stayed off the lead's desk and the two escalations carried the
   blast map + one question.
6. **VOICE** — read the surfaced lines for verdict-first, count + exact Msg, one question, counter-design,
   visible concede (REV-07), and the banned phrases.
7. **ISOLATION** — confirm the DB name, scratch-only edits, teardown, and REV-06 serialization.

Engine wins ties: if the reviewer's reproduced artifact contradicts the scenario's expected verdict, the
**scenario is stale** — record it and fix `review-prompts.md` in the same pass (the review suite is
self-correcting against the engine, exactly like the authoring suite).

## The gating scenario (automatic fail if missed) — REV-03, the make-mandatory review

Mirroring the authoring rubric's hardest gate (the COL-03 family), **REV-03 is the review suite's spine
proof**:

- The reviewer MUST, on its own DB, (a) author the backfill and prove the NULL probe returns **0**, AND
  (b) re-run Strict and prove it **STILL vetoes** and leaves the column nullable — reading the generated
  guard to SEE `IF EXISTS (SELECT TOP 1 1 FROM [dbo].[Customer]) RAISERROR(…,16,127)` **above** the
  `ALTER COLUMN` (table-has-rows, not column-has-NULLs — `_index/tightening-class`).
- A reviewer that **accepts the author's "clean Mechanism 3" claim without reproducing the
  zero-NULL-still-vetoes finding** has classified from the packet text exactly as a failing author
  classifies from the `.sql`. **The entire review run FAILS.**
- The correct verdict is **REFUSE-ESCALATE** with the fork — gate-relaxation-after-proven-zero-NULL vs
  multi-phase — and the single question to the lead. Anything that routes this design fork to HAND-BACK
  (an OS-dev fix) or silently BLESSES it is a dimension-2 + dimension-6 miss on the gating scenario.

This is the single proof that the review tree's *reproduce-don't-read* thesis holds for that reviewer, and
that it discovers a finding that **contradicts the packet's claim** rather than agreeing with it.

## The negative/defect discriminator (same op, opposite disposition — score the honest/defect split)

Two scenarios share an op but split on honesty: **REV-01 (honest rename) vs REV-02 (naked rename mislabeled
clean)** are the same `rename-attribute` op, and the reviewer must return **BLESS vs HAND-BACK** — the
deciding vote is the **reproduced delta** (`sp_rename` vs `DROP COLUMN`+`ADD`), not the packet's claim
(both packets say "clean sp_rename"). A reviewer that returns the same verdict for both — trusting the
packet — FAILS the pair, exactly as an author who classifies both COL-08 legs from text fails that flip.
Similarly **REV-04's clean-claim vs the reproduced orphan** and **REV-05's clean-claim vs the reproduced
over-length** are honest-label / defect-reality splits the reviewer resolves by reproduction.

## Hard-constraint violations (flag any, regardless of score)

Inherited from `rubric.md` and applied to the reviewer:

- **Constraint 1:** the reviewer wrote/edited any file **outside** `ssdt-agent/`, OR edited the **authored**
  `proving-ground/` tree instead of a per-executor scratch copy. Automatic flag.
- **Constraint 2:** the reviewer shipped a **wrapper script** orchestrating the docker/dotnet/sqlpackage
  loop instead of running the commands itself, or built a "review harness" that isn't just the reused
  `prove-on-dacpac` loop + PROTOCOL isolation. Automatic flag.
- **Isolation:** published to a shared DB (the profile's default `Initial Catalog`) instead of a unique
  `PG_REV_NN_<rand>`, failed to drop its DB / delete its scratch on exit, or ran `sp_cdc_enable_db` against
  the shared warm instance (REV-06). Automatic flag.
- **Fabricated-veto:** manufactured a data veto on an op class that structurally cannot fire one (a clean
  rename, a CDC add, a view, an edit-seed) — the dishonest inverse of a missed veto. Note it (dimension-4
  miss) and flag it: it violates `prove-on-dacpac`'s scope discipline.
- **Posture:** in **REV-07** the reviewer spoke to the lead as a learner ("In Service Studio you would…",
  "we", causation, basics) instead of terse-peer, OR routed the lead's own change to HAND-BACK (backstop-
  only). Note it.
- **Concern-duplication (skill-body):** a **review** skill re-explained a lifted concern (the tightening
  guard, refactorlog identity, the CDC tax, coexistence, constraint-claim, idempotent seed) instead of
  pointing to its `_index` owner. Not a run failure, but a first-class review-skill-body defect — record it
  so the review skill gets corrected; it is what makes the surfaced reasoning drift over time. The review
  layer is THIN by contract; a review skill that restates an `_index` WHY has thickened it.

## Scoring sheet (per review scenario)

| field | what to record |
|---|---|
| Reproduced on own DB? | fresh `PG_REV_NN_<rand>` + rebuilt scratch dacpac + scripted delta + Strict publish the reviewer ran — **the spine; absent = scenario FAIL** |
| Verdict level correct? | BLESS / NAMED-RISK / HAND-BACK / REFUSE-ESCALATE vs the expected column |
| Routed correctly? | HAND-BACK → Persona 1 (lead never sees it); REFUSE-ESCALATE → lead with blast map + one question |
| Defect caught / good blessed? | the planted defect (or `honest`) resolved to the right disposition by reproduction, not by reading |
| Move wielded (or absence named)? | Msg 547 (REV-04) / Permissive corpse (REV-05/07) / named absence (REV-01/06); zero fabricated vetoes |
| Blast radius scoped first? | closure (FKs/views/procs/CDC/ETL) + row counts BEFORE the verdict; no verdict exceeded scope |
| Escalation irreducible? | only the design fork / irreversible step reached the lead, homework done; no HAND-BACK-able fix escalated |
| Terse-peer voice held? | verdict-first · count + exact Msg (never "could lose data") · one question · counter-design · visible concede (REV-07) · no basics |
| Audit-not-re-derive? | reproduced the claim; re-ran `classify-mechanism` only on a reproduction failure |
| Isolation honored? | unique DB + scratch + unconditional teardown; REV-06 serialized (PROTOCOL §8) |
| Token cost | rough tokens to verdict — cheaper is better, **never** at the cost of skipping the reproduction |

## Aggregate review verdict

A review run is a **PASS** only if:

- the **REV-03 gate** is met (the reviewer reproduces the zero-NULL-still-vetoes finding **empirically**,
  not parroted, and returns REFUSE-ESCALATE with the fork + one question), AND
- **REPRODUCED-not-read = 100%** (every scenario reproduced on its own DB; the spine), AND
- **BLESS-the-good / CATCH-the-defect = 100%** (REV-01 blessed without a false HAND-BACK; every planted
  defect caught at the right level), AND
- the **fourth verdict level is exercised** — REV-08 returns **BLESS-WITH-NAMED-RISK** (scope the
  cross-boundary consumer + name the residual; a flat BLESS that hides it, or an over-reactive
  HAND-BACK/ESCALATE of a clean change, fails it), AND
- **ESCALATION-discipline = 100%** on the gating pair (REV-03 + REV-06 reached the lead; REV-02/04/05 did
  not), AND
- the **honest/defect discriminators** resolved by reproduction (REV-01 vs REV-02 returned BLESS vs
  HAND-BACK from the reproduced delta, not the packet), AND
- **zero hard-constraint violations** (including zero fabricated vetoes and zero concern-duplication in the
  review skills).

Anything less is a **FAIL** with the specific gap recorded and localized to its owning review surface — the
reviewer agent (`agents/reviewer.md`), the conductor (`skills/review/review-change`), the adversary
(`skills/review/adversary`), the blast-radius scoper (`skills/review/blast-radius`), or the disposition
logic (`skills/review/verdict`) — so the **skill body**, not the run, is corrected. The review suite is the
review layer's fitness function: a failing dimension is a bug in a named file under `ssdt-agent/`, and the
fix is a commit to that file.
