# ENABLEMENT_PROGRAM.md — the ranked program for making this tree succeed

**Status: PROGRAM (2026-08-11).** The staged, evidence-grounded plan to take the ssdt-agent
skill tree from "excellent design" to "a mixed-experience OutSystems team maintains its
database with it, at the efficiency they had under managed Service Studio schema." Assembled
from a full-tree audit (five parallel sweeps over `agents/`, `skills/`, `sample-prs/`,
`self-test/`, `proving-ground/`, the curriculum trees, CI, and the engine seams), with the
highest-stakes findings re-verified directly against the files and the Twin-proven corpus.

Three operator rulings are folded in as given (2026-08-11):

1. **`ssdt-playbook/` is the final edition** of the curriculum; `handbook/` is provenance.
2. **The estate's target shape is the logical model** — produced, logically-named schema, not
   `OSUSR_*` physical shape. Physical-name fidelity is a non-goal for the proving substrate.
3. **CDC is demoted to a metric** — the engine's change-measurement plane, not a team-facing
   operation. No CDC op kit is prioritized.

Companion surfaces: `ACCELERANT_PLAN.md` (the engine seam this program's O7 executes),
`CONNECTORS.md` (the wiring seams O4 and O10 execute), PR #687 (prior work this program
harvests in slices — see "The #687 harvest").

---

## 1 — The mission, and the praxis being scaled

The team: OutSystems-native developers, mixed SQL depth (call the median B-minus), SSDT
experience rare. The pivot: from a platform-managed schema to an SSDT repository they own.
Success is **efficiency parity with the managed workflow, at higher safety** — a developer
states an intent in their own words, and within the same working session holds a proven
change and a pull request a reviewer approves by reading.

The tree already contains the praxis that makes this possible. Every objective below serves
one of three jobs relative to it:

- **Make the praxis executable** — a fresh agent on a fresh machine can actually run it.
- **Keep the praxis true** — no surface can silently drift from what the engine proves.
- **Extend the praxis's coverage** — more operations, more estate reality, more reviewers.

The load-bearing ideas being scaled (preserve these; every one was independently confirmed
in the audit as real and operating):

1. **Classify by proving.** The publish against real-shaped data is the classification; the
   `.sql` text alone never is (`README.md` §"The thesis").
2. **Two findings, never collapsed** — how it ships, and who must review and why
   (`THE_RECORD.md` §5; `classify-mechanism`).
3. **Two registers** — teaching in the conversation, evidence on the record, formally
   split with a banned list (`THE_RECORD.md` §1–§3, §7).
4. **Named absence is a result** — a change class that cannot block is reported as such;
   a manufactured block is a review defect (`prove-on-dacpac`; `review/adversary`).
5. **The scope ceiling** — an approval above an un-enumerated dependency scope is invalid
   by construction (`review/dependency-scope`, `review/verdict`).
6. **Measuring is the tree's job; deciding is the developer's** (`ask-the-developer`).
7. **The corpus refutes itself in public** — the corrected make-mandatory recipe, stamped
   and dated, with the captured run beneath it (`prove-on-dacpac`; `Script.PreDeployment.sql`).

The strategic asset picture (verified): a 41-op catalog with 100% template uniformity;
**41/41 ops carrying a Twin-proven sample PR backed by a green integration fact**
(`tests/Twin.Tests.Integration/SamplePr*Tests.fs` — 11 classes, 41 facts, zero skips);
52 self-test prompts covering 41/41 ops; a deterministic synthetic-data substrate (the Twin,
`THE_TWIN.md`, charter complete) able to hold a local SQL Server current with an estate
definition in one command; an engine that emits SDK-style `.sqlproj`/dacpac from a real
catalog (`Projection.Targets.SSDT`) and rehearses deploys (`projection check deploy`); and a
62-file curriculum (`ssdt-playbook/`) in the right register for exactly this audience.

The program's one-sentence diagnosis: **the tree's intelligence is ahead of its wiring.**
The praxis is right; the assets exist; what fails today is the connective tissue — a
first-run path that dead-ends, two proof loops that never reference each other, contradictions
between scored surfaces and proven surfaces, and zero CI over any of it.

---

## 2 — The findings (what stands between the tree and success)

Grouped by how each defeats the mission. Citations are to current files; the four findings
marked ✱ were re-verified directly during this audit (grep/read), and the three marked ⚖
carry an empirical adjudication from the Twin-proven corpus.

### A. First-run defeaters — a fresh agent following the tree fails

- **F1 ✱ The intake → change-author contract is broken.** `agents/change-author.md:58`
  opens "Intake handed you an **op-slug**"; `agents/intake.md` contains zero occurrences of
  `op-slug`, `opSkill`, or `sharedConcern` — its CHANGE-SPEC template emits a family-index
  path (`skills/operations/<file>.md`), whose files state they restate nothing. Followed
  literally, step 1 dead-ends.
- **F2 ✱ The refactorlog has no worked example.** The "single most important catch in the
  set" (`change-author.md:122-124`) instructs adding a refactorlog entry; no
  `<Operation Name="Rename Refactor">` element with real attributes exists anywhere in the
  tree — `SampleCatalog.refactorlog` is an empty scaffold that points at index files. Every
  rename operation is blocked on hand-authoring DacFx XML from memory.
- **F3 The blocked-publish detection rule is stranded.** A blocked `sqlpackage` publish does
  not reliably exit non-zero; the signal is the output text (`self-test/PROTOCOL.md` §0).
  `prove-on-dacpac` — the skill the solo path actually runs — never states this, and tells
  the solo developer they may skip PROTOCOL. A solo agent reads a block as success, which
  inverts the verdict the whole tree exists to produce.
- **F4 No reset discipline on the solo path.** Permissive mutates the database and a blocked
  publish is non-atomic (`prove-on-dacpac` §on-block), yet the solo loop never mandates a
  reset before the "clean Strict re-run" that is the central proof. PROTOCOL closes this
  with per-executor databases; the solo path does not.
- **F5 Environment assumptions are one developer's box.** `C:/Users/danny/...` paths are the
  normative form in `prove-on-dacpac`, `talk-to-local-sql`, and `proving-ground/README.md`
  (the portable form exists only in PROTOCOL §0); the stated working directory ("repo root")
  is wrong — everything lives under `sidecar/projection/`; `delta.sql` has three different
  canonical paths across three files; the SQL Server image tag floats (`2022-latest`) under
  version-stamped guard evidence; arm64 is unaddressed; the SDK pin (`rollForward: disable`)
  and sqlpackage's requirement (`DOTNET_ROLL_FORWARD=Major`) are opposite policies that must
  coexist in one shell.
- **F6 The gate-relaxation mechanism is under-surfaced.** ~~The flagship finding forces a
  choice between a named `BlockOnPossibleDataLoss` relaxation and multi-phase staging, but
  the actual flag appears only in `op/narrow`, the Permissive profile, and the golden — not
  in `op/make-mandatory` or `prove-on-dacpac`, where the choice is actually confronted.~~
  **SUPERSEDED:** the locked-gate axiom (`FINDINGS_AND_CHANGES.md` Part 1) later established that
  this estate's pipeline **cannot** relax `BlockOnPossibleDataLoss` at all. Gate-relaxation was
  retired tree-wide, not surfaced further — a data-loss change ships as the **two-release**. There is
  no relax-vs-stage choice to surface.

### B. Wrong teachings — surfaces that contradict what the engine proves

- **F7 ⚖ The posture split is unreconciled, and the op skills teach the wrong side.** The
  proving-ground Strict profile sets `DropObjectsNotInSource=True` ("safe because this copy
  is disposable") and is taught as prod-faithful; the Twin-proven sample PRs publish the
  production posture (`DropObjectsNotInSource=false`, DacFx 162.5.57) and prove the
  **opposite mechanisms**: a rename or entity-removal under the production posture is not a
  drop or a block — it is a **phantom** (`Ok` returned, empty new table created, populated
  original stranded; `sample-prs/rename-entity.md`, `delete-entity.md`, `move-schema.md`).
  The op skills document only the disposable-copy mechanics. On a real estate, the phantom
  is the reality that matters — a green deploy that did not do what was asked.
- **F8 ⚖ A scored self-test case demands a disproven answer.** `self-test/prompts.md`
  COL-06B: fitting narrow "publishes clean, applied in place." The op skill, the `_index`
  skill, and the Twin-proven sample PR all state the guard is row-presence — refused on a
  populated table even when every value fits (`sample-prs/narrow.md`, proven at
  `SamplePrTighteningTests.fs:210`). `sample-prs/README.md`'s catalog row repeats the wrong
  summary against its own PR body.
- **F9 ⚖ `add-default` models two different operations under one slug.** The skill: a
  default "affects future inserts only — it does NOT backfill." The Twin-proven PR: adding a
  new NOT NULL column with a default **backfills every existing row** (that backfill is why
  a populated table applies clean). Both are true of different shapes (constraint on an
  existing column vs. new column with default); the skill conflates them and its PR fragment
  cannot be lifted as written.
- **F10 `add-mandatory` vs `audit-columns` disagree on the same mechanism** (new NOT NULL
  column on populated table): one refuses tightening-class governance, the other claims it,
  and the self-test sides with the latter — so `add-mandatory` fails its own suite's
  criterion 6 by construction.
- **F11 Engine versions diverge silently across proof surfaces** — sqlpackage 170.4.83 in
  the golden and `_index` citations; DacFx 162.5.57 in all 41 sample PRs — with no surface
  naming the pair or when they were last reconciled.

### C. Disconnected assets — value built, not wired

- **F12 ✱ The 41 Twin-proven sample PRs are orphaned.** Zero references from `agents/`,
  `skills/`, `README.md`, or `THE_RECORD.md`; the word "Twin" does not appear in the
  pipeline's skills. The tree's largest evidentiary asset is invisible to the pipeline it
  should anchor.
- **F13 The tree is not packaged for any agent runtime.** `CONNECTORS.md` §1 (adoption into
  `.claude/skills/` is "a copy, not a rewrite" — frontmatter already conforms) remains
  unwired; repo `.claude/` carries hooks only. The praxis exists as files an agent must be
  told to go read. **[RESOLVED 2026-08-11 — O4 landed: the tree is packaged into
  `.claude/skills/` + `.claude/agents/` as generated dispatch pointers
  (`sidecar/projection/ssdt-agent/scripts/ssdt-agent-package.mjs apply`), kept in sync by the
  `packaging` CI gate.]**
- **F14 The engine seam is unproven, with one known gap.** `ACCELERANT_PLAN.md` Stage 0 has
  not been run; a concrete divergence was found ahead of it: the F# `SqlprojEmitter` has
  **no PreDeploy item support** (zero hits in `Projection.Targets.SSDT/`), while the
  proving loop's remedy slot depends on `Script.PreDeployment.sql`. Swapping in an emitted
  bundle today silently drops the remedy lane. (The root C# `build-ssdt` emits classic
  non-SDK `.sqlproj` — not the seam.)
- **F15 PR #687 is stranded conflict-dirty** while containing already-built material this
  program needs: the Twin wiring into `talk-to-local-sql`/`prove-on-dacpac`, the
  deploy-script lifecycle rails, the certification-plan staging, a `backfill-rows` op, the
  principal-gating of view authoring, and a live Twin engine fix (view-aware wipe).

### D. No flywheel — nothing keeps it true or makes it better

- **F16 ✱ Zero CI over any enablement surface.** The three workflows cover the F# sidecar
  only; nothing builds the proving ground, runs a Twin fact, checks a citation, or lints
  the record register. (Grep across `.github/workflows/` for `ssdt-agent|proving-ground|twin`:
  none.)
- **F17 Citation drift is structural.** The convention's own exemplar names a file that does
  not exist ("16-Anti-Patterns.md", cited at `README.md:140`, `change-author.md:223`,
  `reviewer.md:226` ✱); the "+3 offset" arithmetic taught in nine files is wrong past
  handbook file 18; skills silently depend on playbook-only content while naming the
  handbook; two incompatible relative-path conventions split `agents/` from `skills/`;
  `_index/multi-phase` carries a broken relative path; `sample-prs/README.md`'s test command
  omits `sidecar/projection/`. Ruling 1 (playbook is final) converts much of this from
  ambiguity into a mechanical re-pointing task — e.g., the §19.5 = Refactorlog Cleanup
  citations are already correct against the playbook.
- **F18 The self-test cannot regress anything.** Fully manual; no runner, no stored scores,
  no baseline, no trigger when a skill file changes; the artifacts the rubric instructs the
  scorer to read are unconditionally deleted by the protocol's own teardown before any
  scorer could look.
- **F19 The rubric is gameable.** Expected block texts are published verbatim in adjacent
  files; block-prediction ordering is unfalsifiable from prose; digests are decorative
  (nothing recomputes them); the make-mandatory "discovery" is now reproducible from
  documentation alone. An agent can score well without ever invoking the engine.
- **F20 Review-side and exemplar coverage is thin.** Reviewer scenarios: 6/41 ops, with a
  ghost id (REV-06 absent ✱, still cited by the review rubric). Golden exemplars: 1/41 ops,
  and the golden cannot demonstrate 2 of the rubric's 7 metrics (no transcript artifacts).
- **F21 Handoff state has no home.** Change-spec, change-order, review packet, dependency
  map, refusal ledger: formats specified, locations unspecified; "first time on this estate"
  and production row counts have no registry, so both added-scrutiny lines are permanently
  asserted rather than proven, and the reviewer's obligation to confirm them cannot be
  discharged.

### E. Reality gaps — sample vs. estate (re-scoped by rulings 2–3)

- **F22 Scale and topology.** 8 populated tables, ~35 seed rows, deepest chain length 2 —
  against an intended estate of 150–300 cycle-dense, logically-named tables. Dependency-scope
  review currently has almost no graph to be wrong about; production-scale timing evidence
  ("may block writes or run long") is structurally unobtainable on the sample.
- **F23 Layout.** Single schema, flat `Modules/`; the engine's emitted layout is
  per-module/per-schema. The loop has never run against the directory shape the team will
  actually own.
- **F24 Catalog blind spots, re-prioritized.** With CDC demoted (ruling 3): the prescribed
  computed-column bridge has no owning op (`_index/identity-and-refactorlog` cites playbook
  §17.8 as the Phase-2 remedy; no skill authors/verifies/rolls it back); view changes have
  no op (and #687 already rules view *authoring* principal-only — adopt that posture);
  **adds exist without their drops** (`drop-check`/`drop-unique`/`drop-default`/`drop-pk`
  absent while every add-op's rollback section describes exactly those inverses); triggers,
  collation change, synonyms, permissions (both profiles set `IgnorePermissions=True`)
  remain unowned. Three op skills also lack the mandatory `**Rollback**` record fragment
  (`define-pk`, `delete-attribute`, `rename-attribute`).

---

## 3 — The solution surface considered

Breadth before ranking; thirteen directions were weighed. Kept and ranked below: run-path
repair (→O1), truth reconciliation on the Twin (→O2), CI truth-gates (→O3), skills
packaging (→O4), the scorer/certification loop (→O5), estate ledger + state homes (→O6),
engine-bundle round-trip (→O7), the onboarding dojo (→O8), catalog expansion (→O9), the
ADO promotion seam (→O10). Weighed and deliberately folded or deferred: an environment
"doctor" (folded into O1/O4 as probes — the repo's hook precedent covers it; a standalone
orchestrator would collide with the no-wrapper rule); curriculum single-sourcing beyond the
canonicality ruling (folded into O3's link gate + an operator decision on `handbook/`
disposition); rewriting the self-test protocol around a wrapper harness (rejected — the
no-wrapper law stands; O5 verifies artifacts instead of orchestrating commands).

---

## 4 — The ranked program

Ranked by the joint maximum of **achievability** (can be landed with certainty, quickly,
with what the repo already has) and **efficacy** (moves the team-success outcome, not a
proxy). Effort is in focused working sessions. Every objective lands with its own
regression detector — that is the program's ratchet rule (§5).

### Tier 1 — the conversion tier (design → running system)

---

**O1. Repair the run path end-to-end.**
*Fixes F1–F6. Achievability: near-certain (documentation edits with executable checks).
Efficacy: converts the tree from unrunnable-as-written to runnable; every other objective
assumes it.*

Scope of done:
- Intake's CHANGE-SPEC template carries `op-slug`, `opSkill`, `sharedConcern` — the fields
  `confirm-intent` already produces and `change-author` already expects (F1).
- A worked refactorlog example — a real `<Operation Name="Rename Refactor">` element with
  element/new-name attributes — lands in `_index/identity-and-refactorlog` and as a
  commented exemplar in `SampleCatalog.refactorlog` (F2).
- The blocked-publish text-detection rule and the reset-before-re-prove discipline move into
  `prove-on-dacpac` itself (F3, F4).
- One canonical `delta.sql` path; the working directory stated correctly everywhere; the
  portable environment block becomes the normative form with the Windows box as the worked
  aside; the SQL Server image gets a digest-pinned tag (F5).
- ~~The relaxation mechanism (`/p:BlockOnPossibleDataLoss=False`, named and logged) appears
  where the choice is confronted: `op/make-mandatory` and `prove-on-dacpac` (F6).~~
  **SUPERSEDED 2026-08-22, with F6** — the locked-gate axiom (`FINDINGS_AND_CHANGES.md`
  Part 1) removed the relax-vs-stage choice tree-wide; the two-release shape is the remedy,
  and no surface offers the relaxation.

First move: the intake template edit (F1) — the single highest-value diff in the tree.
Effort: 1 session. Risk: none worth naming.

---

**O2. One truth: reconcile every surface against the Twin-proven corpus.**
*Fixes F7–F12. Achievability: high — the arbiter already exists (41 green facts).
Efficacy: removes the class of error that would actually burn the team in production
(the phantom rename shipping green) and ends the two-proof-loops split.*

Scope of done:
- **The production posture (`DropObjectsNotInSource=false`) becomes the primary teaching**
  in every affected op skill; the disposable-copy posture is re-labeled as the diagnostic
  instrument it is. The rename/delete/move family documents the phantom mechanism first,
  with the sample PR's captured evidence cited (F7).
- COL-06B and the `sample-prs/README.md` catalog row corrected to the proven row-presence
  finding — or, where any doubt remains, the contested cell is re-proven fresh and the
  losing surface corrected with the run cited (the tree's own discovery protocol) (F8).
- `add-default` split into its two true shapes; `add-mandatory`/`audit-columns` reconciled
  under one `_index` ruling; the self-test's `_index` column updated to match (F9, F10).
- Every op skill gains a **Proven precedent** line: its sample PR and the exact green fact
  that proves it. The word "Twin" enters `talk-to-local-sql`/`prove-on-dacpac` as the named
  preferred substrate (harvest #687's Stage-2 wiring rather than re-deriving it) (F12).
- The engine-version pair (sqlpackage vs DacFx) named once, in one place, with the
  reconciliation date (F11).

First move: re-point the rename/delete/move family at the phantom findings. Effort: 2
sessions. Risk: low; the only judgment calls are adjudications, and the arbiter is executable.

---

**O3. Stand up the CI truth-gate.**
*Fixes F16, F17, and freezes every O1/O2 repair. Achievability: high — the repo already has
the exact regenerate-and-diff precedent (`verifiability-projection.yml`) and a scriptable
test lane (`scripts/twin-test.sh docker`). Efficacy: it is the ratchet — without it every
fix above decays the way the current drift proves things decay.*

Scope of done, as one workflow family over `sidecar/projection/ssdt-agent/**` and
`ssdt-playbook/**`:
- **Citation gate**: every cross-reference in the tree resolves — playbook-canonical paths
  (ruling 1), no handbook-numbered arithmetic, no dead relative paths, no ghost ids
  (REV-06). This single job retires F17's entire finding class.
- **Build gate**: `dotnet build SampleCatalog.sqlproj` (no Docker needed) — the proving
  ground can never silently stop compiling.
- **Proof gate**: the 41 SamplePr facts behind a SQL service container, on a schedule and
  on any PR touching `sample-prs/`, `skills/op/`, or the Twin (label-gated if runner budget
  demands) — the proven corpus stays proven.
- **Register gate**: THE_RECORD's banned list as a lint over record surfaces (`sample-prs/`,
  `golden/`, the op skills' "On the record" fragments).
- **Mirror gate**: `.github/PULL_REQUEST_TEMPLATE/schema-change.md` diffed against
  `skills/author-pr/SKILL.md` per its own precedence note.

First move: the citation gate (pure script, biggest standing debt). Effort: 1–2 sessions.
Risk: Docker-lane cost on hosted runners — mitigated by schedule + path-trigger gating; the
build and citation gates carry no such cost.

---

**O4. Package the tree as loadable skills.**
*Fixes F13. Achievability: high — CONNECTORS §1 says copy-not-rewrite and the frontmatter
already conforms. Efficacy: this is the difference between praxis-on-paper and the praxis
running inside every developer's actual agent session — the shortest path from this repo to
the team's fingers.*

Scope of done:
- `.claude/skills/` entries for the capability skills, op catalog, and review skills;
  `agents/intake|change-author|reviewer` registered as subagents; a scoped `CLAUDE.md` at
  the tree so any session opening the estate routes "make Email required" into intake
  without being told where to read.
- Verified live: a fresh session completes one full change (intake → prove → PR body) with
  skills auto-loaded, on the proving ground.
- The packaging is written to survive the peel — the same layout drops into the post-eject
  estate repository unchanged (the Twin's §8 discipline applied to the skill tree).

First move: wire the four capability skills + intake, run the make-mandatory case live.
Effort: 0.5–1 session. Risk: none structural; verify current skill-loading behavior once.

### Tier 2 — the flywheel tier (self-improving, estate-real)

---

**O5. Make certification real: retained evidence, machine-scored, regression-triggered.**
*Fixes F18–F20. Builds #687's Stage-3 scorer on O3's rails. Achievability: medium-high.
Efficacy: turns "the corpus was proven once" into "the corpus cannot silently rot," and
makes skill edits measurable.*

Scope of done:
- Executors write their evidence (delta.sql, publish output, probe results, hashes) to a
  **retained per-run artifact directory** before teardown; the rubric scores artifacts, not
  prose (resolves the rubric's own impossibility, F18).
- Each prompt gains machine-checkable expected-outcome fields (block-signature patterns,
  expected shipping shape, expected reviewer line); a scorer verifies artifacts against
  them; scores land in a committed ledger so ≥90% has a trend and a skill edit has a
  before/after. The no-wrapper law holds: agents still run the commands; the scorer only
  verifies what they left behind.
- Anti-gaming: a scored run must present artifacts whose content the scorer re-derives
  (recompute the digest, re-grep the block text from the captured output file, not the PR
  prose) (F19).
- Reviewer scenarios extended 6 → 20+ (structural family first — the ops where review
  matters most); the ghost REV-06 resolved; a golden-minting rule: any certification run
  that surfaces a new engine behavior mints the golden for its op (F20).

Effort: 2–3 sessions. Risk: scoring-design care to keep "engine is ground truth" from
becoming an escape hatch — the ledger records prompt edits, and a prompt correction requires
the citing run.

---

**O6. Give state a home: the estate ledger.**
*Fixes F21; makes the two added-scrutiny lines dischargeable. Achievability: high (a
directory, three file formats, a CI reminder). Efficacy: high — this is what makes
multi-release changes survivable by a team, which is exactly where a B-minus team gets
hurt.*

Scope of done:
- An `estate/` ledger (location: the estate repo post-eject; staged here first): operations
  performed (op, date, PR, proof artifact) — "first time on this estate" becomes a lookup;
  row-count tiers per table refreshed from the Twin/estate — "at production row counts"
  becomes a fact; the refusal ledger gets its named file.
- An **in-flight register** for multi-phase changes: phase N of M, what ships next, what
  unblocks it — with a CI check that an in-flight entry older than its stated window fails
  loudly. The forgotten Phase 2 is the failure mode this retires.
- Handoff artifacts (change-spec → review packet) get one specified directory and lifetime.

Effort: 1 session. Risk: none structural; the discipline cost is real and the CI reminder
is the mitigation.

---

**O7. Prove the loop on the real estate shape: the engine-bundle round-trip.**
*Fixes F14, F22, F23 — re-scoped by ruling 2: the target is the logical model, which is
precisely what the engine emits. Achievability: medium (Stage 0 is designed;
one named emitter gap). Efficacy: closes "works on the sample, unknown on the estate" —
and after the eject, the emitted logical bundle IS the estate, so this is rehearsing the
actual future.*

Scope of done:
- The `SqlprojEmitter` PreDeploy lane added (small, well-specified; the remedy slot must
  survive emission) with its test (F14).
- ACCELERANT Stage 0 executed as written: emit a logical-rendition bundle from a real
  catalog, `dotnet build` → dacpac, run the unchanged proving loop against it; the two
  §3 gates checked (reclassification, DSP) plus the PreDeploy gate this audit added.
- A scale tier: Twin scenarios at 50k–1M rows on the emitted estate, so "may block writes
  or run long" carries a measured order of magnitude at least once per mechanism family
  (F22) — and the loop exercised against the per-module/per-schema layout (F23).
- `SampleCatalog` is retained as the deterministic fixture (the plan's own rule).

Effort: 2–3 sessions. Risk: medium — real-catalog access for the emission; the Twin's
evidence tiers were built for exactly this and keep literals out of the repo.

---

**O8. The dojo: a graded first-ten-changes curriculum.**
*Extends the praxis to the humans directly. Achievability: high — the material exists
(41 worked PRs, 52 prompts, the playbook); this is sequencing plus link repair. Efficacy:
high for the stated audience — it is the difference between "the agent can do it" and "the
team can supervise, review, and eventually not need the rails."*

Scope of done:
- A `dojo/` path sequencing ten katas from `add-optional` to `make-mandatory` (capstone:
  reproduce the golden), each kata = run the real loop on the proving ground, then diff
  your PR against the sample PR; reviewer katas ride the same cases with the review rubric.
- `Start-Here` link repair in the playbook (the current first-contact page has dead links
  in every reading path — F17's most user-visible face) and one stated canonicality banner
  in `handbook/` per ruling 1.
- The two-register discipline taught explicitly in kata 1 (developers read conversations;
  reviewers read records).

Effort: 1–2 sessions. Risk: none structural.

### Tier 3 — the expansion tier

---

**O9. Extend the catalog where the estate will actually go next.**
*Fixes F24 under ruling 3 (CDC demoted to a metric — no op kit; note it in
`classify-mechanism` as a measurement surface only). Achievability: high per-op — the
op-kit recipe (skill + sample PR + Twin fact + prompt) is proven 41 times. Efficacy:
compounding; each kit removes a future "the tree doesn't know this" moment.*

Priority order, re-ranked per ruling 3:
1. **The drop-inverses** (`drop-check`, `drop-unique`, `drop-default`, `drop-pk`) — every
   add-op's rollback currently points at an unowned inverse; plus the three missing
   `**Rollback**` fragments.
2. **`backfill-rows`** — harvested from #687 (already authored there).
3. **The computed-column bridge** — a prescribed Phase-2 remedy with no owning op.
4. **Views** — adopt #687's principal-only authoring posture; the op teaches the routing
   and the dependency-scope consequences, not authoring.
5. Then by estate demand: synonyms, triggers, collation, permissions (requires deciding the
   `IgnorePermissions` posture deliberately).

Effort: ~1 session per kit once O2/O3 exist. Risk: none structural; the recipe is the
flywheel's Loop C (§5).

---

**O10. The ADO promotion seam.**
*Executes CONNECTORS §5 on O3's gates. Achievability: medium (org-external variables).
Efficacy: very high at steady state — the reviewer's trust stops depending on the author's
agent having run the loop, because the pipeline reproduces the proof.*

Scope of done: a PR-validation pipeline in the estate repo that builds the dacpac, brings
up a Twin, publishes under the production posture, and posts the deploy report + block
findings as the PR's evidence artifact; promotion gates on the Strict-clean re-run after
remedy, per CONNECTORS §5. Effort: 1–2 sessions plus org wiring. Risk: ADO agent/runner
constraints; the Twin's exit-code vocabulary and `check deploy` verb are the fallbacks.

---

### The #687 harvest (cross-cutting)

PR #687 is treated as a quarry, not a merge problem: extract in slices onto fresh branches —
(a) the Twin engine view-wipe fix + regression test (engine-side, independent, land first);
(b) the Stage-2 Twin wiring and deploy-script lifecycle rails into O2; (c) the certification
plan + scorer staging into O5; (d) `backfill-rows` and the view-gating posture into O9.
Each slice re-verified against current main before landing; the dirty branch is closed with
a pointer to its successors once emptied.

---

## 5 — The flywheel operating model

Four loops, each runnable by an agent session today; the program is complete when all four
turn without prompting.

- **Loop A — truth.** Any edit to a skill/sample/prompt → O3's gates re-check citations,
  register, build, and (path-triggered) the Twin facts. Drift becomes a red check instead
  of an archaeology finding. *Cadence: every PR; Docker lane nightly.*
- **Loop B — competence.** O5's certification runs the prompt matrix (parallel executors,
  per the existing PROTOCOL), scores from retained artifacts, commits the ledger; the
  lowest-scoring skill gets the next fix session; the fix's before/after is two ledger
  rows. *Cadence: weekly, and on any `skills/**` change.*
- **Loop C — coverage.** Real developer requests that miss the dispatch table, plus estate
  events with no owning op, land as one-line entries in a `coverage-gaps` file; each becomes
  an op kit via the proven recipe (skill + sample PR + fact + prompt), entering Loops A/B on
  merge. *Cadence: as they occur; kit-building batched.*
- **Loop D — estate.** O6's ledger accrues operations, proofs, row tiers, in-flight phases;
  added-scrutiny lines become lookups; the review-routing map tightens as the team's own
  history accumulates. *Cadence: continuous, enforced by the in-flight CI check.*

**The ratchet rule** (the program's one standing law): *no fix lands without the detector
that would have caught its absence.* O1's repairs land with O3's citation gate covering
them; O2's adjudications land with the proof gate covering them; O5's scores land in a
ledger CI can trend. This is how the flywheel holds what it gains.

---

## 6 — Operator decisions (named, non-blocking)

1. **`handbook/` disposition** under ruling 1: archive banner + freeze, or delete after the
   playbook absorbs any handbook-only entries still wanted. (The playbook currently lacks
   two handbook anti-patterns; with CDC demoted, "The CDC Surprise" may stay retired —
   "The SELECT \* View" is worth keeping alongside the view-op posture.)
2. **Docker CI budget**: PR-triggered vs. nightly for the 41-fact proof gate (O3), and
   whether a label gates the expensive lane.
3. **`IgnorePermissions` posture** before any permissions op kit (O9.5).
4. **Platform-column question** for the logical model: whether the emitted estate carries
   platform-ish attributes as ordinary columns; O7's Stage 0 will surface it empirically.
5. **The certification pass bar** (O5): keep the current PASS thresholds or re-derive them
   once the first scored ledger exists.

---

## 7 — Done-when (program-level)

1. A fresh agent on a clean machine completes intake → proven change → PR record on the
   proving ground following only the tree (O1, O4).
2. No surface contradicts a Twin-proven finding, and CI would catch a new contradiction
   within one PR (O2, O3).
3. Every op kit is complete (skill + sample PR + green fact + prompt + rollback fragment),
   and the certification ledger shows a stable-or-rising pass trend across skill edits
   (O5, O9).
4. The loop has passed against an engine-emitted, logical-rendition estate at
   representative scale, including the per-module layout and the PreDeploy lane (O7).
5. The estate ledger discharges both added-scrutiny lines from data, and no in-flight
   multi-phase change can silently stall (O6).
6. A cohort developer completes the dojo's ten katas, and their tenth PR is approved by
   reading, by a reviewer who reproduced it (O8, O10).

*The praxis is already right. The program wires it, proves it, and lets it keep proving
itself.*
