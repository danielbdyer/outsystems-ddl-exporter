# CERTIFICATION PLAN — making the ssdt-agent's word machine-checkable

> **Status: PLAN (nothing wired yet).** Companion to `ACCELERANT_PLAN.md` (which wires the F#
> engine as a fast-path) and married to `../THE_TWIN.md` (the post-eject synthetic-data
> sidecar, charter-complete 2026-07-18) — the Twin is this plan's proving substrate, §3.6.
> This plan wires **trust**: the staged, verify-first path from "a well-written PR the
> reviewer believes" to "a PR whose proof is reproduced by machines and whose evidence any
> team member can check in one read." Grounded in a 2026-07-20 audit of the whole tree (all
> 48 op skills, the 6 `_index` concerns, `self-test/`, `proving-ground/`, `.github/`),
> summarized in §2.

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
  `diff` dependency maps) are designed and unwired — and since 2026-07-18 the Twin
  (`../THE_TWIN.md`) productizes the substrate half of this gap outright (§3.5).
- **F11 — No estate memory.** "This operation has not been performed on this estate before" is
  a standing added-scrutiny line no one can actually answer; nothing records what shipped, with
  what proof, decided by whom.
- **F12 — Adoption is manual.** `CONNECTORS.md` §1 (`.claude/skills/` wiring) is designed and
  unwired; the front door is "read this file."
- **F13 — Realistic op gaps.** Computed columns (a cutover dealbreaker class —
  `CUTOVER_BOARD_POPULATION_PLAN.md` decision 12), collation change, and procedures/functions
  have no op; triggers/sequences/partitioning deserve a one-line route-to-DBA note.

## 3 — The certification spine (the design)

> **Amendment (2026-07-20) — trust is not agent-to-agent.** The earlier framing of §3.1 (an
> author capsule and a reviewer capsule that must *agree* to make "reproduced" true) is
> **retired**. Reproducibility is a property of the **Twin**, not of two agents matching: the
> Twin's determinism means the base dataset regenerates byte-identically for anyone, so no
> second agent's re-run is what earns trust. Trust now rests on two anchors — the Twin's
> determinism (§3.1) and the plain showcase of the risk-averse checks the agent ran (§3.1) —
> and the process the agent walks to place, prove, and retire deployment scripts is
> `skills/deploy-scripts` (the deployment-script lifecycle rails). The pieces below are
> re-aimed accordingly; the golden corpus (§3.2) is reframed as evidence-presentation
> exemplars, not agent-diff fixtures.

Six pieces, smallest first. Each is independently useful; together they close the loop from
"the agent says" to "the substrate is reproducible by construction and the evidence is on the
record for any developer to read."

### 3.1 The two trust anchors — Twin determinism + the evidence showcase

Trust comes from two places, **neither of them a second agent**:

- **The Twin's inherent determinism** (`../THE_TWIN.md`). `twin up` mints a deterministic,
  masked, distribution-faithful dataset over the estate's own definitions; `twin check` proves
  π∘σ≈id, mints are byte-identical (T1), and a schema edit re-mints only the columns it touches
  (S-stable). The evidence-profiling config (`twin.json`) is version-controlled beside the model
  and **evolves as the schema evolves** — this is the local dev environment the agent proves
  against, and its base dataset is reproducible *by construction*, so a reviewer regenerates the
  identical data without re-running the agent. The substrate is the guarantee.
- **The plain showcase of the risk-averse checks the agent ran.** Presented as evidence and
  little else, in the record register (`THE_RECORD.md` §2, pushed evidence-forward): the exact
  pre-/post-deployment scripts (each with its permanence class + header + retirement condition),
  the **idempotency proof** (the no-op redeploy's three assertions — 0 rows · identical content
  digest · 0 CDC captures if tracked), and the sanity/verification SQL run before the change was
  allowed to land. The precise changes required, **no more and no less** (Gate 0 refuses a script
  when the diff already does the work). This is what `skills/deploy-scripts` + `skills/author-pr`
  now produce; it needs no separate machine artifact for another agent to match.

### 3.2 The golden corpus — one certified exemplar per family × flip

Grow `self-test/golden/` from 1 pair to ~14: for each op family, the case whose outcome flips
on data, captured from a live run as an **evidence bundle** — PR body + conversation + the
scripts shipped + the silent-redeploy assertions + `delta.sql` + publish-log excerpt. These are
**evidence-presentation exemplars** (the existing make-mandatory PR already is one), not
agent-diff fixtures. Candidates: make-mandatory triple (exists) ·
rename-attribute with/without refactorlog · create-fk-orphan · add-unique on dupes · narrow
past the data · delete-entity populated (the principal-review exemplar) ·
recreate-capture-instance · split-table multi-phase · edit-seed idempotency ·
identity-swap · widen (the clean collapse-to-verdict exemplar) · create-view + the out-of-band
`SELECT *` trap. These are the "real examples that demonstrate a certifiably valid result" —
each one is simultaneously teaching material, a scoring fixture, and a drift canary.

### 3.3 The mechanical scorer — split the rubric

The rubric has an objective half a script can assert (probe ran before publish; expected block
fired with the expected signature; flip twins produced different outcomes from the same op;
teardown ran; the evidence's counts match the prompt's legend) and a subjective half only a
reader can judge (voice, teaching, the one question). Split them: the scorer reads the presented
evidence (the scripts shipped, the silent-redeploy assertions, the delta) and emits per-case
verdicts; the human rubric keeps the register and pedagogy. Seed variants
(empty / clean / violating) become named Twin scenarios (§3.5) instead of hand-typed scratch
edits — declarative, refused-by-name when wrong, byte-identical on every re-mint.

### 3.4 The reproduction gate + the knowledge canary — CI

Two jobs, both on a container runner (SQL Server in Docker + sqlpackage as a dotnet tool;
once §3.5 lands, the runner pulls the `twin bake` pre-seeded image instead of deploying and
seeding from scratch):

- **Smoke gate (per PR touching the tree):** build the proving-ground dacpac; run the
  make-mandatory triple + rename-with/without-refactorlog + create-fk-orphan against fresh
  DBs; assert each evidence bundle against its golden. ~5 cases, minutes, catches both skill-text rot
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

### 3.5 The Twin substrate — the proving ground grows real, and survives the eject

The Twin (`../THE_TWIN.md`) is the marriage this plan was missing when first drafted: **one
command (`twin up`) holds a local SQL Server current with the estate's own definitions and
fills it with deterministic, masked, distribution-faithful synthetic data.** That is exactly
what "a disposable copy of Dev, populated with real-shaped data" wants to be — and after the
eject there IS no Dev upstream to copy, so the disposable copy *becomes* the Twin by
necessity, not preference. Marry them now so the agent's habits survive the eject unchanged.
What each Twin property buys this plan:

- **The BEFORE state comes from `twin up`/`twin seed`** — real estate schema (the SSDT repo's
  own definitions, coordinate-total), real-shaped rows — instead of the 9-table hand-authored
  sample. The agent's loop is unchanged on top: edit the CREATE, build, then the Strict /
  Permissive sqlpackage publishes against the Twin-established state. The two-profile
  discipline stays skill-owned (the `ACCELERANT_PLAN.md` §4 guardrail, unchanged); the Twin
  supplies the substrate, never the verdict.
- **Scenarios are the seed-variant harness §3.3 needs.** The self-test's flip twins — empty /
  clean / violating — stop being hand-typed scratch edits and become named scenario overlays
  (volumes, weights, date windows, pins), compiled and refused-by-name by
  `Twin.Core/ScenarioCompiler.fs`. The `prove-on-dacpac` violating-row probe (the injected
  orphan / dup / over-length / NULL) becomes a **pin**: an operator-authored exact row,
  declared in the scenario, merged after realization, reproducible forever.
- **Determinism makes the base data reproducible by construction.** T1 byte-identical re-mints
  plus `S-stable` (a schema edit re-mints only the columns it touches; everything else holds
  byte-identical) mean a run over the same `(estate, evidence, scenario, corrections, seed)`
  fingerprint stands on the **identical base data every time** — a reviewer regenerates it exactly
  without re-running the agent. That is what makes "reproduced" a mechanical word: the substrate is
  the guarantee, not an agent-to-agent match. Evidence captured against a fixed fingerprint differs
  only in what the change did.
- **`twin bake` is the CI substrate.** The §3.4 smoke gate and canary pull the baked,
  pre-seeded image instead of paying schema-deploy + mint time per run — deterministic
  starting state, minutes saved, no seed drift between CI runs.
- **Masking makes the goldens committable.** The shape tier is literal-free (Twin law 3), a
  real high-cardinality value is never emitted, and PII renders through the seeded Faker
  realization — so golden captures, evidence bundles, and logbook entries built on Twin data carry
  no production literal and can live in the repo without a data-governance question. A golden
  corpus captured from restored real Dev data could never say that.
- **`twin check` joins the reviewer's independent corroboration** (beside `projection check` /
  `check data` / `compare` from `ACCELERANT_PLAN.md` §C): the π ∘ σ ≈ id proof on a throwaway
  database, marshaled as evidence the substrate itself is sound.

The honesty boundary, named plainly: Twin data is **evidence-faithful synthetic, not the
environment's rows**. A proof on the Twin establishes the *mechanism* — what SSDT's publish
engine does to data of this shape — which is what classification needs. It does not establish
the *instance* — whether UAT or Prod holds the orphan the shape tier didn't see. The PR's
**Verification** queries (run in each environment) and the standing **Not verified** section
carry that gap, exactly as they do today; nothing about the record register changes.

### 3.6 The estate logbook — memory that compounds

An append-only `logbook/` of shipped changes: date · op-slug(s) · object · the two findings ·
evidence-bundle reference · disposition · the business decision and its decider. It makes
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
- **Stage 1 — the lifecycle rails + the Twin re-anchor (DONE).** Authored `skills/deploy-scripts`
  (the deployment-script lifecycle: folder = permanence class, header = death certificate, silent
  redeploy = proof, retirement = tracked act, the six gates); re-anchored trust on Twin determinism
  + the evidence showcase; sharpened the evidence-only record register; added the proving-ground
  class folders. **The agent-to-agent `proof.json` capsule is retired** — reproducibility is the
  Twin's property (§3.1), so no per-loop capsule is written. What the loop presents is the evidence
  bundle (the scripts + the silent-redeploy assertions + the delta), plain, in the record.
- **Stage 2 — make the Twin substrate real (this stage).** Author `proving-ground/twin.json` (the
  first real evidence-profiling config, resolving against the config dir); operationalize the
  substrate in `talk-to-local-sql` + `prove-on-dacpac` (detect `twin.json` + the `twin` CLI →
  `twin up` establishes the deterministic BEFORE state on its own container via DacFx, no sqlpackage;
  the sqlpackage proof targets the Twin; the isolation rules); the sample stays the fallback.
  Supersedes the old "Stage 4 — estate anchoring" for the substrate half. The real-estate twin.json
  (with evidence `sources` in logical/physical rendition, covering the ~200 tracked tables) is the
  production sibling and follows. **Discovered gap (2026-07-20):** the Twin's pre-mint wipe fails on a
  view over multiple base tables (`vOrderSummary`); the schema publishes but the data mint blocks —
  the proper fix is a small Twin-engine change (skip non-updatable views in the wipe), out of this
  tree's scope. Verified here through container-start + DacFx schema publish; the sample fallback is
  unaffected.
- **Stage 3 — the scorer.** A script that reads the **evidence bundle** the Twin loop produces (the
  captured `delta.sql`, the silent-redeploy assertions, the block text with its signature) and
  asserts the objective half of the rubric (probe-ran-before-publish, expected block + signature,
  flip twins differ, teardown ran); run it over a full self-test fleet once by hand; fix what it
  exposes. Not a "capsule-reader" — it reads the plain evidence, structured only as far as the
  objective checks need. The human rubric keeps the register and pedagogy.
- **Stage 4 — CI.** The smoke gate, then the knowledge canary (§3.4); once Stage 2 matures, the
  runner pulls the `twin bake` pre-seeded image. Gate on the goldens. Record the CI-exception.
- **Stage 5 — adoption + the funnel.** Wire `CONNECTORS.md` §1 so intake is a slash-command away;
  surface the reviewer disposition as a PR check; count the funnel — % approved by reading alone,
  disposition mix, returned-to-author reasons, escalations. The funnel is how "the principals stopped
  being the bottleneck" is demonstrated instead of asserted, and the returned-to-author reasons are
  the next curriculum chapter, discovered rather than guessed.

Stages 0–2 need no new infrastructure and de-risk everything after; Stage 4 is the first CI spend;
5 rides existing plans (`ACCELERANT_PLAN.md`, `CONNECTORS.md`).

## 5 — Guardrails

- **The human surfaces do not change.** `THE_RECORD.md`'s two registers, the PR body shape,
  and the conversation stay exactly as specified; the evidence bundle is the plain scripts +
  proof already in the record, not a separate artifact beneath it.
- **No wrapper for the developer loop.** The agent still runs `docker`/`dotnet`/`sqlpackage`/`twin`
  itself. CI's scripts are the named exception and live outside `skills/`.
- **The engine and the Twin stay optional** (`ACCELERANT_PLAN.md` governing principle,
  extended). The generic path — hand-authored sample + raw probes — must keep passing the
  self-test, engine and Twin absent. `proving-ground/SampleCatalog` is not deleted; it remains
  the deterministic fixture and the fallback.
- **The Twin supplies substrate, never verdicts.** The two-profile Strict/Permissive
  discipline and the content-hash check stay skill-owned (`ACCELERANT_PLAN.md` §4 guardrail),
  and the sqlpackage proof — not the Twin — establishes the refactorlog / rename / pre-post-deploy
  verdict on top of the Twin base. The proving loop never touches the warm projection container
  from the Twin side: Twin containers are the Twin's own, per `../THE_TWIN.md` §6.
- **Goldens are re-captured, never hand-edited.** A golden that no longer matches the engine
  is re-proven and re-stamped with the new sqlpackage version, per `self-test/golden/README.md`
  — the canary exists to force exactly that, loudly.
- **The logbook is append-only** and carries decisions with their deciders, in the record
  register.

## 6 — Open questions

- **Block-signature extraction (was "capsule extraction"):** the block signature must be extracted
  from publish output by rule (F8's string-match, now load-bearing) — there is no capsule schema to
  pin it in anymore. Pin the exact grep set per signature where the Stage-3 scorer reads the evidence
  bundle, and version it with sqlpackage. The signatures themselves are already lifted into
  `_index/constraint-is-a-claim` (F5, Stage 0).
- **CDC cases in CI:** `sp_cdc_enable_db` is instance-wide and capture timing is
  agent-dependent (`PROTOCOL.md` §8) — the smoke gate should exclude the CDC family; the
  canary runs them serialized with poll-and-timeout, or on a dedicated instance.
- **Where the logbook lives:** in-repo markdown vs the deploy repo vs ADO work items. In-repo
  first (grep-able, PR-diffable); revisit at the ADO seam (`CONNECTORS.md` §5).
- **Runner provisioning:** the smoke gate needs Docker + the sqlpackage tool on a hosted or
  self-hosted runner; verify image pull + license posture before Stage 4 is scheduled.
- **Flip-twin variants — seed-based for the sample, scenario/pin-based for synthetic estates
  (resolved).** Traced in the Twin code: the proving-ground's data comes from the **static seed**
  (`Data/Seed.sql`), and the Twin treats lane-seeded kinds as *provided*, not synthetically minted —
  so Twin scenarios/pins do **not** drive the sample's empty/clean/violating variants; those stay
  seed-based (the self-test scratch-edit path). Scenarios + pins are the flip mechanism for a
  **synthetically-minted real estate**, where evidence-profiled data fills the tables. The original
  concern (zero-FK-orphan mint law K1 vs a violating pin) applies there: verify each violating pin
  binds against the pre-change estate before Stage 3's synthetic-estate scenarios rely on it.
- **`twin up` mid-proof is a hazard (resolved — rule encoded).** The loop deliberately diverges the
  twin (it publishes the edited dacpac). The rule, now in `talk-to-local-sql`: the Twin sets BEFORE;
  from then until `twin reset`, only sqlpackage touches the DB. A second `twin up` would reconcile
  the edit away.
- **`twin check` may share the warm container (new).** `twin check` acquires a throwaway DB via
  `PROJECTION_MSSQL_CONN_STR`; if that points at `projection-mssql-warm` it lands there transiently.
  Give the proving-ground Twin its own throwaway container/env so `twin check` never shares the warm
  instance. Encoded as an isolation rule in `talk-to-local-sql`.
- **Twin view-wipe gap (new, Stage 2 finding).** The Twin's pre-mint wipe issues a DELETE against
  every read-back object, and a view over multiple base tables is not deletable —
  `twin up` publishes the schema but the data mint blocks on `vOrderSummary`. The proper fix is a
  small Twin-engine change (skip non-updatable views in the wipe); out of this tree's scope. Owner:
  the Twin (`Twin.Runtime`). Until then the Twin path establishes the schema base but not the data;
  the sample fallback is unaffected.
- **Parallel executors on one Twin:** the Twin maintains one persistent twin per config; the
  self-test fleet needs per-executor isolation. Candidates: unique databases on the twin container,
  per-executor `twin.json` with a unique container name + port, or the `twin check`-style throwaway
  database. Decide before the fleet rides the Twin (Stage 3); until then `PROTOCOL.md` isolation
  stands as-is.
- **CDC stays outside the Twin:** the Twin does not manage capture instances; the enable-cdc /
  recreate-capture-instance family keeps its isolated-instance, serialized discipline
  (`PROTOCOL.md` §8) regardless of substrate.
