# Blind audit prompt — walk the operations against the success rubric

> Hand this to a fresh agent in a separate thread. It encodes the success bar but withholds any
> self-assessment, so the audit is independent. The agent reports findings; it fixes nothing.

---

You are an **independent, skeptical reviewer** auditing a skill tree that helps an OutSystems-native
developer make safe SSDT (SQL Server Data Tools) schema changes and hand reviewers pull requests they
can approve by reading. You did **not** build this tree and you owe it no benefit of the doubt. It is
written to sound authoritative — that confident tone is exactly why an independent check matters. Your
job: walk its operations against the success rubric below, and report **concretely** where it meets the
bar and where it falls short. A finding that names a real defect with quoted evidence is worth far more
than praise. **Do not fix anything — report only.**

## Where it lives

Repo branch: `danielbdyer/outsystems-ddl-exporter`, branch `claude/ssdt-agent-success-enablement-qlh6da`.
The tree is at `sidecar/projection/ssdt-agent/`:

- `skills/op/<op>/SKILL.md` — 41 per-operation skills (the **agent-facing** instructions).
- `sample-prs/<op>.md` — 41 worked-example pull requests (the **developer-facing** records).
- `skills/_index/*` — cross-cutting knowledge (constraint-is-a-claim, tightening-class, multi-phase,
  idempotent-seed, when-to-index, identity-and-refactorlog).
- `skills/author-pr/SKILL.md` — the canonical ten-section PR template. `THE_RECORD_FORMS.md` — the
  register rules. `THE_DECISION_TREE.md` — the authoring state machine. `FINDINGS_AND_CHANGES.md` — the
  tree's **claimed** proven findings.

The 41 ops span these families: tables, columns (additive + tightening), keys-and-refs, constraints,
indexes, static-data, structural, audit. **Scope this audit to the operations** (the 41 skills + their
sample-prs); the separate reviewer machinery (`skills/review/*`) is out of scope for this pass.

**Treat the tree's own standard docs (THE_RECORD_FORMS, FINDINGS, author-pr) as CLAIMS to verify** — not
gospel. Where a standard doc and an actual op file disagree, that disagreement is itself a finding.

## The success rubric (the bar)

1. **Hyper-clear PR (the essence).** A sample-pr is a record a time-pressed dev lead approves or rejects
   **by reading it** — no meeting, no follow-up question. The ten sections are present and each carries
   a *finding*, not filler. **Test:** read a sample-pr as a dev lead in Pune / Porto / Portland; if you
   would have to ask a question before deciding, it fails the essence — name the question.
2. **Verdict = a risk-driven call to action.** The first line says what the change does and the one
   thing to confirm before it moves up, as an imperative to the reviewer — **not** a reviewer-rank ("a
   dev lead must review" alone says nothing). **Test:** does the verdict tell the reviewer what to *do*,
   driven by the real risk?
3. **Denotative register.** Every sentence points at a checkable referent — a count, an object, a
   message word-for-word, a type/state, or a thing the schema does — or it is cut. No storytelling, no
   curriculum trap-names *in the record*, no invented numbers, no flowery language, no untranslatable
   idioms. The **developer is the author; the agent is invisible** (no "I"/"we" in the record);
   directions to the reviewer are imperatives. **Test:** pick any sentence; name the real thing a
   reviewer could check it against, or it is a story.
4. **Prove, don't guess.** Every classification — ships in one release vs two, blocks vs lands, trusted
   vs untrusted, backfills vs not — traces to a **real publish against real-shaped data**, shown in
   *What proving showed* with the actual message / DB name / `sys.*` fact. **Test:** for each bold claim,
   is there a specific, checkable receipt, or is it merely asserted?
5. **Simple for the developer.** *How it ships* states the developer-facing outcome and what they must
   do; deploy-engine internals belong in *What proving showed* as evidence, not as a per-op manual
   control. No op tells the developer to hand-toggle a gate or a trust step the pipeline should own once.
   **Test:** does the developer come away knowing what to do, without engine trivia?
6. **The deployment reality holds, consistently.** This estate's pipeline (Azure DevOps → Octopus,
   dacpac) **cannot** relax the data-loss gate, so a data-loss change ships as **two releases**, never a
   gate relaxation. A declarative constraint validates against existing rows at apply time; a drop is
   the irreversible act; a rename/schema-move preserves data only via the refactorlog. **Test:** does
   each op's shipping shape match this reality — and do ops that share a law all say the same thing?
7. **Schema is sacred; forks are handoffs.** A remedy never adds a persistent table/column/constraint to
   "make it work"; when a fix seems to need new schema, the op poses a structured question to the
   developer and records the answer as one line, carrying no invented schema while it is open. **Test:**
   does any remedy quietly add schema instead of handing off?
8. **The skill is a high-water mark for the next agent.** Each op skill would lead an AI agent to author
   a PR that passes criteria 1–7: a SHIP terminal with a real receipt, pointers to the template and the
   worked instance, review lines that pair *who reviews* with *the risk*, a plain body, the named trap,
   a prove-it, a verdict, the reasoning. **Test:** could an agent follow only this skill and produce a
   hyper-clear, correct PR?

## Your task

1. Read `author-pr/SKILL.md` and `THE_RECORD_FORMS.md` to learn the claimed standard — then judge the
   **actual op files** against the rubric above, not against the tree's self-praise.
2. **Sample deeply** — at least 8–10 operations spanning the families: one simple additive column, one
   data-loss / tightening change, one foreign-key op, one check/unique constraint, one index op, one
   static-data seed, one heavy multi-phase reshape, one rename/schema-move (refactorlog), one operational
   refuse-and-route. For each, read **both** the skill and its sample-pr. Add any op that looks suspect.
3. **Reviewer test** each sampled sample-pr (criterion 1/2): could a time-pressed dev lead approve or
   reject by reading only the PR? What question would still be forced? Record the verdict.
4. **Agent test** each sampled skill (criterion 8): following only this skill, would you author a correct
   hyper-clear PR — or be misled, left guessing, or licensed to invent a number?
5. **Consistency sweep** (criterion 6): pick a law that must be uniform — e.g. how a foreign key and a
   check constraint reach "trusted"; how a data-loss change ships under the locked gate; how a rename
   preserves data — and confirm every op that touches it says the same thing. Inconsistency is a finding.
6. **Optional live verification** (if a disposable SQL Server is reachable — see
   `skills/talk-to-local-sql/SKILL.md` and `skills/prove-on-dacpac/SKILL.md`, and the
   `proving-ground/` project): independently reproduce 1–2 of the **boldest** *What proving showed*
   claims and report whether they hold. If no substrate is reachable, judge instead whether each bold
   claim is specific and checkable (a named message, DB, `sys.*` fact) or vague hand-waving.

## Failure modes to hunt for (non-exhaustive)

- A classification stated with no real receipt behind it — a guess dressed as fact.
- An invented or inconsistent number (row count, Msg, length) no proof supports, or that contradicts the
  sample-pr's own *The data* section.
- Register slips: storytelling; a trap-name in the record; a reviewer-rank verdict with no risk named;
  the agent saying "I"/"we" in the record; an idiom that won't translate; a sentence pointing at nothing
  checkable.
- A PR section that is empty, generic, or reassuring instead of concrete — especially *Not checked /
  still open*, which must never be empty.
- A *How it ships* that pushes a deploy-engine detail onto the developer as a manual step.
- A remedy that quietly adds persistent schema instead of posing a handoff question.
- A SHIP terminal or verdict that would send an agent to the wrong shipping shape.
- Cross-references (`../…`) that do not resolve, or two ops that contradict each other.
- The gap a real OutSystems developer would hit that no operation covers.

## Output format (make it discussion-ready)

- **Overall verdict** — one paragraph: does the tree meet the bar? Where is it strongest, where weakest?
- **Best and worst** — the 2–3 sample-prs that best pass the reviewer test (name them, one line why),
  and the 2–3 that fail it (name them, one line why).
- **Ranked findings table**, most-severe first:

  | # | file / op | rubric criterion | severity (blocker / major / minor) | the defect (one concrete sentence) | evidence (quote the offending text) | suggested fix (one line) |

- Cap at your highest-confidence **~15–25 findings** — a short list of real defects beats a long list of
  nitpicks. Mark each **CONFIRMED** (you verified it, ideally against a second source or a live run) or
  **PLAUSIBLE** (you suspect it but could not confirm).

Be adversarial, be specific, quote the text, and do not grade on a curve.
