# CERTIFICATION PLAN — making the ssdt-agent's word machine-checkable

> **Status: PLAN (nothing wired yet).** Companion to `ACCELERANT_PLAN.md` (which wires the F#
> engine as a fast-path); this plan wires **trust**. It is the staged, verify-first path from
> "a well-written PR the reviewer believes" to "a PR whose proof is reproduced by machines and
> whose evidence any team member can check in one read." Grounded in a 2026-07-20 audit of the
> whole tree (all 48 op skills, the 6 `_index` concerns, `self-test/`, `proving-ground/`,
> `.github/`), summarized in §2.

## 1 — The problem this plan solves

The tree's stated intent (`README.md`): help an OutSystems-native developer make a safe SSDT
data-model change, and hand the reviewer a pull request they can approve by reading. The
organizational fact underneath it: most of the team knows Advanced SQL, only a handful are
qualified to review DDL, and fewer still know the lifecycle gotchas (`handbook/03`,
`handbook/23`). The principals are the backstop, and that is the bottleneck the cutover cannot
afford — ~300 tables, ~200 CDC-tracked, four environments (`AGENTS.md`).

Today the chain of trust ends at the agent's prose. The proving loop is genuinely rigorous —
a real sqlpackage delta against a real isolated database is the oracle — but everything above
the engine is human-read text: the PR body asserts what the publish did, the reviewer persona
reproduces it *if run*, the self-test is scored by a person reading a rubric, and nothing runs
in CI. An agent that wrote fluent prose about a proof it never ran would today be caught only
by a diligent human reading the transcript. That is exactly the failure mode a trusted team
member cannot have.

**The thesis of this plan: the proof already exists at the engine boundary — capture it as
data, reproduce it by machine, and let the human read findings that a green check has already
re-verified.** The record register (`THE_RECORD.md`) stays the human surface, unchanged; the
certification layer sits beneath it.

## 2 — Audit findings (2026-07-20), each with its owner

Strengths to preserve (do not rebuild): the classify-by-proving thesis and the Strict/Permissive
two-profile discipline (`skills/prove-on-dacpac`); the uniform 48-op template with the
two-findings model held everywhere; the pointer discipline (ops cite `_index/`, never
re-derive); the two-register voice spec (`THE_RECORD.md`); the reviewer persona's
reproduce-first posture; the PR template mirror (`.github/PULL_REQUEST_TEMPLATE/schema-change.md`).

The defects, ranked by what they cost:

- **F1 — One golden exemplar of ~69 self-test cases.** `self-test/golden/` holds only the
  make-mandatory pair, as prose to imitate, not fixtures to diff. No expected `delta.sql`, no
  expected block signature, no expected probe counts exist for the other ~60 authoring cases
  and all 8 review scenarios.
- **F2 — Zero CI.** No workflow builds the dacpac, runs a single publish, or exercises any
  self-test case. The three `.github/workflows/*` all target the F# codebase.
- **F3 — Scoring is human.** `self-test/rubric.md` / `review-rubric.md` are prose checklists;
  no scorer reads `delta.sql` + the publish log and emits a verdict. The rubric's "engine wins
  ties; fix the stale prompt" rule cannot distinguish tool-version drift from a wrong
  expectation without a pinned fixture.
- **F4 — A promised op does not exist.** The data-plane backfill / idempotent-UPDATE op is
  routed to by `skills/op/add-default/SKILL.md:26` (which mis-points at `make-mandatory`),
  `op/modify-default/SKILL.md:32`, `operations/constraints.md:26`, and
  `_index/idempotent-seed`. "Add a default AND backfill existing rows" is among the most common
  real requests; today it dead-ends.
- **F5 — Block signatures are uneven.** The tightening class names its exact evidence
  (`Msg 50000, Level 16, State 127`, `SQL72014`, `Msg 515`); the constraint-is-a-claim family
  carries **zero** error codes — `define-pk`, `add-unique`, `add-check` describe blocks in
  prose with no `Msg 2627`/`1505`/`547`/`8152` an agent could grep publish output for.
- **F6 — README drift.** `README.md`'s tree map omits three live skill directories:
  `skills/os-vocabulary/` (load-bearing, cited by `confirm-intent`), `skills/ask-the-developer/`,
  `skills/author-review/`. Index drift is a first-class defect by the sidecar's own law
  (`../CLAUDE.md` §0).
- **F7 — The substrate is pinned to one machine.** Five files hardcode
  `C:/Users/danny/...` (`prove-on-dacpac`, `talk-to-local-sql`, `proving-ground/README.md`,
  `self-test/PROTOCOL.md` ×2). A new team member — or a CI runner — cannot run the loop
  without hand-editing scaffolds.
- **F8 — The block verdict rides a string match.** A blocked publish does not reliably exit
  non-zero (`PROTOCOL.md:73-80`); the loop greps output text. Fine for an agent; fatal for
  automation unless the signature set is pinned (F5) and asserted per case.
- **F9 — The knowledge is version-bound with no re-verification trigger.** The tree's central
  empirical findings (table-has-rows guard; `Msg 515` seed-vs-tightened-column; the non-atomic
  FK block leaving `is_not_trusted = 1`; `sp_refreshsqlmodule` auto-refresh; refactorlog →
  `sp_rename`) are all stamped "sqlpackage 170.4.83." Nothing re-proves them when the tool
  moves.
- **F10 — Proofs run against a 9-table sample, not the estate.** `ACCELERANT_PLAN.md` stages
  0–2 (engine-emitted proving ground from a real catalog, `profile.json` block prediction,
  `diff` dependency maps) are designed and unwired.
- **F11 — No estate memory.** "This operation has not been performed on this estate before" is
  a standing added-scrutiny line no one can actually answer; nothing records what shipped, with
  what proof, decided by whom.
- **F12 — Adoption is manual.** `CONNECTORS.md` §1 (`.claude/skills/` wiring) is designed and
  unwired; the front door is "read this file."
- **F13 — Realistic op gaps.** Computed columns (a cutover dealbreaker class —
  `CUTOVER_BOARD_POPULATION_PLAN.md` decision 12), collation change, and procedures/functions
  have no op; triggers/sequences/partitioning deserve a one-line route-to-DBA note.

## 3 — The certification spine (the design)

Five pieces, smallest first. Each is independently useful; together they close the loop from
"the agent says" to "the machine re-verified and any developer can see that it did."

### 3.1 The evidence capsule — the proof as data

Every proving run already produces the facts; capture them once, structured, beside the prose:
a small `proof.json` written by the agent at the end of the loop (it is **data the agent
writes, not a wrapper that runs the loop** — the no-orchestration constraint stands).

Fields (schema versioned): run id · date · sqlpackage version · dacpac SHA-256 · seed
fingerprint · target DB name · per-step records (command, outcome, the extracted block
signature `Msg`/`SQL72xxx` when one fired, probe SQL + returned count) · `delta.sql` SHA-256 +
the guard excerpt · the two findings (how it ships / who reviews) + added scrutiny · the
Not-verified list. The PR body remains the human surface per `THE_RECORD.md` and
`author-pr`'s "nothing attached for the reviewer to run" rule — the capsule is for the
reviewer *agent* and CI, and it is what makes "reproduced" checkable: two capsules from two
isolated runs must agree on every deterministic field (block fired y/n, signatures, counts,
delta shape). Disagreement is itself the finding.

### 3.2 The golden corpus — one certified exemplar per family × flip

Grow `self-test/golden/` from 1 pair to ~14: for each op family, the case whose outcome flips
on data, captured from a live run as (PR body + conversation + **capsule + `delta.sql` +
publish-log excerpt**). Candidates: make-mandatory triple (exists — add its capsule) ·
rename-attribute with/without refactorlog · create-fk-orphan · add-unique on dupes · narrow
past the data · delete-entity populated (the principal-review exemplar) ·
recreate-capture-instance · split-table multi-phase · edit-seed idempotency ·
identity-swap · widen (the clean collapse-to-verdict exemplar) · create-view + the out-of-band
`SELECT *` trap. These are the "real examples that demonstrate a certifiably valid result" —
each one is simultaneously teaching material, a scoring fixture, and a drift canary.

### 3.3 The mechanical scorer — split the rubric

The rubric has an objective half a script can assert (probe ran before publish; expected block
fired with the expected signature; flip twins produced different outcomes from the same op;
teardown ran; the capsule's counts match the prompt's legend) and a subjective half only a
reader can judge (voice, teaching, the one question). Split them: the scorer consumes capsules
and emits per-case verdicts; the human rubric keeps the register and pedagogy. Seed variants
(empty / clean / violating) become parameterized fixtures instead of hand-typed scratch edits.

### 3.4 The reproduction gate + the knowledge canary — CI

Two jobs, both on a container runner (SQL Server in Docker + sqlpackage as a dotnet tool —
the same substrate `warm-sql.sh` already assumes):

- **Smoke gate (per PR touching the tree):** build the proving-ground dacpac; run the
  make-mandatory triple + rename-with/without-refactorlog + create-fk-orphan against fresh
  DBs; assert each capsule against its golden. ~5 cases, minutes, catches both skill-text rot
  and substrate rot.
- **Knowledge canary (scheduled + on sqlpackage version bump):** re-prove the §2-F9 findings
  and diff against the goldens. On divergence, the canary opens the issue that says "the
  curriculum's central claim changed under sqlpackage X.Y — re-capture and re-stamp," instead
  of a developer discovering it mid-change. This is what keeps a written-down empirical
  finding *certified* rather than merely remembered.

The no-wrapper constraint governs the developer-facing skill loop, where the agent must reason
through each command; CI is a different reader whose whole job is deterministic reproduction.
Resolution: CI scripts live under `ssdt-agent/ci/` (or `.github/`), are never referenced by
any skill body, and the skills continue to scaffold commands only. Record this as the standing
exception when Stage 3 lands.

### 3.5 The estate logbook — memory that compounds

An append-only `logbook/` of shipped changes: date · op-slug(s) · object · the two findings ·
capsule reference · disposition · the business decision and its decider. It makes
"first time on this estate" a lookup instead of a shrug (F11), lets a PR cite precedent
("matches the widen shipped 2026-08-12, entry 47") — the single cheapest trust-builder for a
wary reviewer — and turns every real change into a golden-corpus candidate. Seed it during the
cutover dry-runs themselves: the UAT dry-run the T-30 gate requires is the estate's first
logbook chapter, so the agent enters day one already carrying the estate's history.

## 4 — Staged, verify-first

- **Stage 0 — paper cuts (docs-only, no infra).** Author `op/backfill-rows` and fix the four
  routes to it (F4); lift the violation-block signatures into
  `_index/constraint-is-a-claim` and cite them from `define-pk`/`add-unique`/`add-check` (F5);
  fix the README tree map (F6); parameterize the five hardcoded paths behind documented env
  vars with per-OS worked examples (F7); add the F13 route-to-DBA note. Every item is a
  same-day edit.
- **Stage 1 — capsule + goldens.** Define `proof.json` (schema + a worked example); amend
  `prove-on-dacpac` and `review-change` to end every loop by writing one; re-run the
  make-mandatory golden capturing its capsule; capture 4–5 more goldens (3.2), each from a
  live run.
- **Stage 2 — the scorer.** The capsule-reader that asserts the objective rubric half; run it
  over a full self-test fleet once by hand; fix what it exposes (it will expose things).
- **Stage 3 — CI.** The smoke gate, then the knowledge canary. Gate on the goldens from
  Stage 1–2. Record the CI-exception decision (§3.4).
- **Stage 4 — estate anchoring.** `ACCELERANT_PLAN.md` stages 0–2 as written (engine bundle →
  real schema; `profile.json` → predicted blocks + CDC awareness across the ~200 tracked
  tables; `diff` → dependency scope), plus the logbook seeded from the cutover dry-runs.
- **Stage 5 — adoption + the funnel.** Wire `CONNECTORS.md` §1 so intake is a slash-command
  away; surface the reviewer disposition as a PR check; start counting the funnel — % of
  changes approved by reading alone, reproduction-mismatch rate, disposition mix
  (any-team-member / dev-lead / principal), returned-to-author reasons, escalations carrying
  one question + the map. The funnel numbers are how "the principals stopped being the
  bottleneck" is demonstrated instead of asserted — and the returned-to-author reasons are the
  next curriculum chapter, discovered rather than guessed.

Stages 0–2 need no new infrastructure and de-risk everything after; 3 is the first
infrastructure spend; 4–5 ride existing plans (`ACCELERANT_PLAN.md`, `CONNECTORS.md`).

## 5 — Guardrails

- **The human surfaces do not change.** `THE_RECORD.md`'s two registers, the PR body shape,
  and the conversation stay exactly as specified; the capsule sits beneath the record, never
  in it.
- **No wrapper for the developer loop.** The agent still runs `docker`/`dotnet`/`sqlpackage`
  itself; the capsule is output, not orchestration. CI's scripts are the named exception and
  live outside `skills/`.
- **The engine stays optional** (`ACCELERANT_PLAN.md` governing principle). The generic path —
  hand-authored sample + raw probes — must keep passing the self-test with capsules, engine
  absent.
- **Goldens are re-captured, never hand-edited.** A golden that no longer matches the engine
  is re-proven and re-stamped with the new sqlpackage version, per `self-test/golden/README.md`
  — the canary exists to force exactly that, loudly.
- **The logbook is append-only** and carries decisions with their deciders, in the record
  register.

## 6 — Open questions

- **Capsule extraction discipline:** the block signature must be extracted from publish output
  by rule (F8's string-match, now load-bearing). Pin the exact grep set per signature in the
  capsule schema, and version it with sqlpackage.
- **CDC cases in CI:** `sp_cdc_enable_db` is instance-wide and capture timing is
  agent-dependent (`PROTOCOL.md` §8) — the smoke gate should exclude the CDC family; the
  canary runs them serialized with poll-and-timeout, or on a dedicated instance.
- **Where the logbook lives:** in-repo markdown vs the deploy repo vs ADO work items. In-repo
  first (grep-able, PR-diffable); revisit at the ADO seam (`CONNECTORS.md` §5).
- **Runner provisioning:** the smoke gate needs Docker + the sqlpackage tool on a hosted or
  self-hosted runner; verify image pull + license posture before Stage 3 is scheduled.
