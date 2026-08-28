# An architecture review — the ssdt-agent tree, the Twin, and the synthetic data pipeline

**Written 2026-08-28, on request: a radically candid assessment of whether this system is
overbuilt or underbuilt for its purpose — an OutSystems-native team making safe SSDT schema
changes through GitHub Copilot in Visual Studio — and what the ideal agentic form factor for
the enablement and local-development system is.**

The evidence base: a full read of the tree's charters, agents, skills, sample PRs, self-test,
proving ground, packaging, and CI; the Twin charter and its source (`../THE_TWIN.md`,
`Twin.Core` / `Twin.Runtime` / `Twin.Cli`, 4,683 source lines, 113 tests); the synthetic-data
design (`../THE_SYNTHETIC_DATA_DESIGN.md`) and its engine modules; the prior assessment
(`ASSESSMENT_2026_08_24.md`) and everything landed since it (PR #700's Copilot packaging,
`PROVING_PATH_WINDOWS.md`, the decompose skill); and a check of the current Visual Studio
2026 Copilot extensibility surface against Microsoft's published documentation.

---

## 1. The verdict in one page

The method is right, the engineering quality is high, and the epistemic culture — prove on a
disposable copy, publish your own refutations, gate every surface — is the best thing about
this repository. The table-has-rows finding alone justifies the tree's existence: it is
non-obvious, it is proven, and a team without it would ship the disproven backfill recipe.

The candid problem is an inverted investment ratio. The tree holds roughly 20,000 lines of
markdown across 253 files — 8,565 lines of skills, 3,026 lines of sample PRs, 2,374 lines of
self-test, and on the order of 2,500 lines of program-management and register-governance prose
— wrapped around a proving loop that is still eight fragile shell commands with a runtime
shim and read-the-text-not-the-exit-code folklore, running against a substrate that is still
not the team's data, packaged for a consumer (Copilot in Visual Studio) that has not yet run
a single change end to end. The prose layer is three to four times larger than its consumers
need; the mechanism layer — the part that decides whether a Copilot session on a Windows
laptop actually succeeds — is the part that is still thin.

So the answer to "overdone or lacking?" is: **both, in a specific and fixable pattern.
Over-documented and under-mechanized.** The sections below name what earns its keep, what is
ceremony, what is missing, and the form factor that resolves the pattern.

Three sharp findings surfaced during this review that earlier evaluations did not name:

1. **The corpus proves atoms and ships molecules.** All 41 operations carry an independent
   proof; the 52 self-test prompts contain zero compound scenarios; no proof anywhere
   publishes a combined multi-operation delta; and the multi-phase programs (split-table,
   merge-tables, move-attribute, extract-to-lookup) are proven only at their additive first
   phase — the dangerous cutover and contract legs have no executable witness.
2. **The two proof corpora run on divergent engines, and the automated one runs on the risky
   version.** The markdown receipts were captured on sqlpackage 170.4.83.3; the 41 CI facts
   run on DacFx 162.5.57 — the version family on which a new foreign key reads untrusted,
   which `estate/toolchain.md` itself names the sharpest correctness risk. The estate
   pipeline's own version is still unpinned.
3. **The Twin already contains most of the ideal form factor, unshipped.** `twin bake`
   exists; the evidence tiers exist; S-stable determinism exists. What is missing is the
   wiring that turns them into a pulled artifact on a developer's machine — and the Windows
   runbook currently routes around the Twin entirely, telling developers to restore a real
   Dev `.bak` instead, which reintroduces the PII-on-laptop problem the synthetic pipeline
   was built to remove.

---

## 2. What earns its keep — do not trim these

- **The thesis and its proofs.** "Proving is classifying" is correct and rare. The
  discovered laws — the data-blind row-presence guard, the phantom rename and delete under
  `DropObjectsNotInSource=false`, the mid-deploy `Msg 515` seed failure, the F2
  half-application, the reconcile-then-trust foreign-key shape — are real engine behavior,
  captured with real output, and several were discovered *by* the per-op proving campaign
  overturning the tree's own earlier prose. That discovery record is the strongest argument
  the per-op proofs already paid for themselves.
- **The 41-operation catalog as a dispatch and knowledge layer.** The trigger phrases are
  written in the developer's own vocabulary ("tick the Mandatory checkbox"), which is
  exactly what Copilot's skill discovery matches on, and the flip conditions (empty vs
  populated, clean vs violating, coexistence) are the domain's real type system. At an
  average of 111 lines per op this layer is not bloated. Keep it.
- **The `_index` factoring.** Six shared-knowledge skills owning the cross-cutting WHY, with
  op skills pointing instead of restating, enforced by gates — this is textbook knowledge
  architecture, and the tightening-class / constraint-is-a-claim distinction it preserves is
  load-bearing.
- **The ten-section record and the two-register split.** The sample PRs are genuinely the
  product: `sample-prs/make-mandatory.md` is a model of what a schema-change PR should look
  like. Teaching in conversation, evidence on the record, is a real insight most teams never
  articulate.
- **The citations/register/mirror/packaging/estate gates and the generated packaging.** The
  five-minute PR gate plus the pointer-based dual packaging (`.claude/` and
  `copilot-package/`, one generator, fingerprinted) is exactly how a tree this
  cross-referenced stays true. The path-scoped instruction files in the Copilot bundle —
  guardrails that attach whenever a `.sql`, deployment script, or publish profile is open —
  are the single most reliable Copilot surface, and the bundle uses them for precisely the
  never-rules. Correct call.
- **The Copilot degradation ladder.** The four-rung design (Visual Studio 2026 18.5+ with
  auto-discovered skills and agents, 18.4 with agents plus the generated `skills/INDEX.md`,
  2022 with prompt files, ask-only as a manual) matches the actual shipped Visual Studio
  Copilot surface — `.github/skills/` per the agentskills.io SKILL.md specification and
  `.github/agents/*.agent.md` custom agents — and degrades honestly.
- **The Twin and the synthetic engine themselves.** Judged on its own: ~4,700 source lines
  for one-command convergence, deterministic masked minting, executable laws, and an
  ejection design is lean, not bloated. And for this use case the v1 fidelity boundary is
  not a real limitation: every publish guard the tree cares about fires on marginals — row
  presence, NULL counts, duplicates, orphans, lengths — never on joint distributions, so
  "no L3 joint synthesis" costs the proving substrate nothing.

---

## 3. Where the tree is overbuilt

**3.1 The persona ceremony models an organization that does not exist inside one chat.**
Intake and change-author are two agents connected by a structured CHANGE-SPEC contract —
a formal handoff between two roles that, in the team's actual surface, are one Copilot
session responding to one developer. The valuable content of intake (disambiguate to one
op-slug, gather the three state-variables, ask exactly one business question) is a *phase*
of authoring, not a persona; the handoff format, the "invoked cold" recovery path, and the
`estate/handoffs/` lifecycle are apparatus for a multi-session fleet the estate will rarely
run. The reviewer persona is different — a separate reader, a separate trust model, a
separate repository event — and earns its separation, though §6 argues its center of
gravity belongs in the pipeline, not the IDE.

**3.2 The register governance outweighs the register.** The register itself — the
ten-section record, agentless findings, evidence beneath, admit the unverified — is right.
But it is now carried by three documents (`THE_RECORD.md` 302 lines, `THE_RECORD_FORMS.md`
142, plus the standard's worked-wrong examples) and a 14-pattern CI lint, in a tree whose
records are written almost exclusively by frontier models following templates. One page of
rules beside the template, plus the lint, would hold the same line. The current shape is the
project talking to itself about how it talks.

**3.3 Four synchronized surfaces per operation is a chosen maintenance tax.** Each op
carries a skill, a sample PR, a self-test prompt, and a Twin fact — four surfaces that must
agree, which is exactly why 978 lines of gate and packager code exist to hold them together.
The gates are good engineering *given* the surface count; the surface count is the choice.
The op skill and the executable fact are the two that matter (the knowledge and the proof);
the sample PR is valuable as a worked exemplar but 41 of them at 74 average lines is a
gallery where a dozen would teach the same shapes; the 983-line prompt matrix is
certification machinery for a certification loop that does not run (see 3.5).

**3.4 The program literature ships inside the working artifact.** ENABLEMENT_PROGRAM (552
lines), the assessment (345), the curriculum plan (282), the session handoff (244), the
accelerant plan (110) — roughly 1,500 lines of program management that no schema-change
session will ever read, vendored into the estate repository by default. `copilot-package/ADOPTION.md`
already names the prune list; pruning should be the generator's default for the vendored
layout, not a manual instruction.

**3.5 The self-test is a manual ceremony whose regression duty the Twin facts already
discharge.** 2,374 lines of protocol, prompts, and rubrics; scored by hand; its own audit
(ENABLEMENT_PROGRAM F18/F19) says the artifacts the rubric needs are deleted by its own
teardown and the rubric is gameable from adjacent files. Meanwhile the 41 integration facts
run nightly and on every touching PR, and they — not the prompt matrix — caught the real
contradictions. The honest move is to shrink the self-test to the cases the facts cannot
express (conversation quality, fork-posing) and let CI own engine truth.

**3.6 The house documentation culture leaked in.** The V2 sidecar's ritual density — ~190
root documents, chapter opens and closes, named doctrines for every decision — is a
reasonable culture for a monorepo inhabited by high-context agent sessions. The enablement
tree inherited the reflex: every insight becomes a named surface, every surface a citation
target, every citation a gate. For the tree's actual consumer — a Copilot agent on a Windows
laptop with a modest context budget, following pointer → body → `_index` → findings → sample
across four to six hops — every additional surface and hop is a failure point, not an asset.

---

## 4. What is still missing

**4.1 Compound proof.** The user-facing question — "do we need independently proved skill
files given most changes are combinations?" — has a precise answer in the corpus's own
terms, and §5 gives it. The gap itself: zero compound self-test prompts, zero combined-delta
sample PRs, zero executable facts for a batched release, and phase-1-only facts for every
multi-phase program. The decompose skill plans molecules by reading atom labels — which is
exactly the classify-from-the-text move the tree forbids everywhere else — and no proof ever
checks the plan.

**4.2 The substrate is still not the team's data, and the two answers to that are not
integrated.** Proving today runs against 8 tables and ~35 rows (the proving-ground twin.json
carries no evidence section, so even the Twin substrate mints flat 100-row defaults there).
The Windows runbook's answer is "restore a Dev `.bak`, sanitized if sensitive" — a wish with
no mechanism, reintroducing real data on laptops. The Twin's answer — profile real Dev once,
commit the literal-free shape tier, mint masked distribution-faithful data anywhere — is
built and unwired to the estate path. This is the single most valuable integration not yet
done.

**4.3 One command that returns a verdict.** The proving loop is eight commands, a runtime
shim, a Git Bash path-mangling flag, and the standing rule that the block lives in the
printed text because the exit code lies. Every one of those is a place a Copilot agent —
markedly less reliable than a Claude Code session at long tool liturgies — can silently
fail, and the failure mode is the worst one: a blocked publish read as success. The folklore
exists because the interface is raw; fold it into a tool.

**4.4 The pull request gate.** CONNECTORS §5 / ENABLEMENT O10 name it and nothing runs: an
Azure DevOps build-validation pipeline that rebuilds the dacpac, stands up a Twin, publishes
under the production posture, and posts the deploy report and block findings on the PR.
Until it exists, the ten-section record is self-reported evidence, and the reviewer persona
is an IDE-side patch over a CI-shaped hole.

**4.5 The engine pin and the trust check.** Still open, still the sharpest silent-failure
risk, and now compounded by the corpus divergence named in §1: the nightly proof lane runs
on the engine family whose FK-trust behavior differs from the receipts'. Pinning the estate
pipeline's DacFx and running the one `is_not_trusted` check is an afternoon on the right
machine.

**4.6 A mechanism for the concurrent-publish revert.** The two-release lag window is
documented and ledgered (`estate/in-flight.md`), but a ledger is a register, not a lock. An
in-flight row should gate something: at minimum the PR pipeline of 4.4 refusing to publish a
release that reshapes a table with an open lag window.

**4.7 Known catalog blind spots.** Already named in the enablement program (drop-inverses,
views, computed columns, collation, triggers); the drop-inverses matter most because every
add-op's rollback section points at an unowned inverse.

---

## 5. Atoms, molecules, and the unit of proof

Keep the per-op skill files. They are the vocabulary the developer's words dispatch into,
and their flip conditions are real knowledge that combinations do not dissolve — a
make-mandatory inside a five-op release is still governed by the row-presence guard.

But stop treating the atom as the unit of *proof*. DacFx compiles one script per release;
publish-time interactions — ordering, a pre-deploy that half-applies and poisons the atoms
behind it, a rename and a tightening riding one delta — live at the release grain, and
per-op proofs do not compose to them. The tree's own thesis says you cannot classify a
change by reading its SQL; the same holds one level up: you cannot classify a release by
reading its atoms' labels. In practice change-author already proves the actual combined
edit — the doctrine and the corpus just don't say so. Three changes close the gap:

1. **State the invariant: the unit of proof is the release delta.** Decompose's packing is
   provisional the way classify-mechanism's cascade is provisional — confirmed or flipped by
   publishing each planned release's combined delta on the copy.
2. **Prove a handful of molecules.** Run decompose's own worked example for real: the
   one-release additive batch (new entity + seed + two FKs + defaulted NOT NULL column) as
   one published delta; one reshape concern serializing a rename and a tightening; and one
   multi-phase program (extract-to-lookup or split-table) driven end to end through its
   cutover and contract phases — the legs no fact currently witnesses. Capture them as
   compound sample PRs and compound facts; add compound prompts to the self-test.
3. **Give the record a multi-op form.** author-pr is implicitly single-op-shaped; a
   compound PR needs the ten sections once per release, with the atoms enumerated inside,
   not ten sections per atom.

After that, the marginal investment in new atoms (beyond the drop-inverses) is worth less
than almost anything else on the list.

---

## 6. The ideal form factor

The system's center of gravity should move from prose the agent reads to mechanisms the
agent operates. Four layers, smallest first:

**Layer 0 — the substrate is a versioned artifact you pull, not a machine you build.**
CI (source repo now, estate repo after ejection) profiles real Dev on a schedule or on
schema change, holds the literal-free shape tier in the repo, mints at row-tier volumes,
and runs `twin bake` — publishing **two** artifacts: a pre-seeded container image for
Docker machines, and a `.bacpac`/`.bak` for LocalDB machines, because synthetic data can
travel as data and thereby drops the Docker-Desktop dependency entirely. Versioned by the
schema fingerprint the Twin already computes. A developer's machine runs one pull-or-restore
step and holds a current, masked, distribution-faithful copy of Dev in minutes. This
replaces the restore-a-real-backup step of `PROVING_PATH_WINDOWS.md`, closes the
PII-on-laptop hole, and is mostly assembly: bake, evidence tiers, S-stable determinism, and
the fingerprint all exist today.

**Layer 1 — the verdict is a tool call.** One command — a `twin prove` verb or a sibling —
that runs build → script → strict publish → parse, and returns a structured verdict:
blocked or clean, the verbatim `Msg`, the guard shape, the delta path, data-loss steps,
with correct exit codes; a `--permissive` leg for consequence capture; reset built in. The
scaffolded eight-step loop becomes the tool's documentation. This deliberately amends the
"you scaffold; the agent runs" rule at the estate boundary: that rule optimizes for
teaching agents inside the monorepo, and it was the right rule for discovering the laws;
the estate's goal is parity-with-safety in week one, and a Copilot agent invoking one tool
and reasoning over structured output is categorically more reliable than one orchestrating
an environment-sensitive command liturgy. The skills keep owning the reasoning about the
verdict; the tool owns producing it.

**Layer 2 — skills as reasoning, personas as phases.** The Copilot-facing surface
collapses to: one authoring skill (the phases: confirm intent → probe → prove → remediate →
record, absorbing intake), the reviewer skill, the six `_index` knowledge files, the op
catalog as the reference library the authoring skill dispatches into (unchanged bodies, no
longer the primary entry points), and the path-scoped instruction files as the standing
guardrails. Fewer hops, fewer entry points, the same knowledge.

**Layer 3 — the reviewer's engine moves to the pull request.** The ADO build-validation
pipeline of §4.4: rebuild, stand up the baked Twin, publish Strict under the production
posture, post the report, check the record's sections mechanically, and refuse a release
that collides with an open in-flight lag window. The human reviewer then does the one thing
only a human should: the business call. The IDE reviewer persona remains for the lead's
sparring use; the *gate* is the pipeline. This is what makes "a PR a reviewer approves by
reading" honest at team scale — the evidence is reproduced by machinery, not asserted by
the author's agent.

**What this does to the tree:** nothing in the knowledge layer is lost. The 41 ops, the
`_index` concerns, the record forms, the findings ledger all remain the source the layers
compile from. What changes is what the consuming agent touches: an artifact, a tool, a
short skill stack, and a pipeline — instead of 253 files and folklore.

---

## 7. What to do, in order

1. **Run the Visual Studio pilot that is already written** (`copilot-package/ADOPTION.md`,
   the six-step checklist) on one champion laptop. Nothing in the packaging is verified on
   the team's actual build, and the riskiest assumption in the whole system is Copilot's
   instruction-following depth against these bodies. One afternoon converts assumption to
   fact and would re-rank everything below.
2. **Pin the pipeline's DacFx and run the constraint-trust check once** (steps already in
   `PROVING_PATH_WINDOWS.md` §6). Close the silent-failure risk before habits form.
3. **Build Layer 0** — bake → versioned artifact (image + bacpac) → pull/restore; wire
   `twin evidence import` against restored Dev once; commit the shape tier.
4. **Build Layer 1** — the `prove` verb with structured verdicts.
5. **Prove the molecules** (§5): three to five compound exemplars including one full
   multi-phase program end to end; compound prompts; the multi-op record form.
6. **Stand up Layer 3** — the ADO PR pipeline, including the in-flight collision check.
7. **Then trim**: personas → phases in the Copilot bundle; the vendored prune list becomes
   the generator's default; the self-test shrinks to what the facts cannot express; the
   register governance folds to one page beside the template.

And two things deliberately *not* to do: do not delete the op catalog or the sample-PR
gallery wholesale (trim the gallery, keep the traps), and do not build more curriculum
machinery until the cutover has stabilized and the pilot has produced its first real
lessons — the existing Phase-2 plan already says this correctly.

---

## 8. Execution addendum (2026-08-28, same branch)

The ordered list in §7 has been executed to the edge of what this environment can reach; the
review stands as written and this addendum only records what landed, so a later reader does
not mistake the plan for the remaining work. Landed, each proven live and gated: the portable
proving loop (`scripts/prove.mjs`, the one-command verdict; `PORTABILITY.md`); the substrate
as a pulled fingerprint-versioned artifact (`scripts/bake.mjs`, the bake lane, both
production routes content-hash identical); the compound corpus (`sample-prs/compound/`, the
release-delta doctrine, the compound record form, findings F13–F17); the four drop-inverse
kits with both drop-pk faces (F18); constraint-trust re-confirmed on the current engine
(F19); Twin facts for the inverse and compound archetypes (`SamplePrInverseTests`,
`SamplePrCompoundTests`); the lag-window hold as a mechanism (the in-flight `tables` column +
`scripts/inflight-check.mjs`, and the ADO PR-validation template); the vendor verb with the
prune list as generator behavior; the compound reviewer scenarios (REV-09, REV-10); and the
schema-derived evidence floor (`Twin.Core/DerivedEvidence.fs` — zero-configuration realism
under every captured tier); the mint trust gate (`TwinDatabase.revalidateConstraints` —
`WITH CHECK CHECK` over every user CHECK and foreign key after each mint, refusing with
`twin.mint.constraintViolation` instead of handing over silently-untrusted data); the
one-approver recalibration (the principal level retired by the owner's 2026-08-28 call —
`THE_RECORD.md` §5's second finding is now *what the approving dev lead weighs*, recalibrated
tree-wide with `estate/reviewers.md` rewritten for the single class); and two form-factor
refactors from §6 — the one-door Copilot prompt (`#prompt:ssdt-schema-change`,
packager-synthesized, intake → change-author in one conversation) and `talk-to-local-sql`
restructured tool-first (prove/twin/bake lead; the per-machine rungs demoted to the fallback
they encode); and the scale lane (`proving-ground/twin.scale.json` + `estate/scale-datapoints.md`)
— whose first run surfaced and fixed three engine defects (width-blind unique tokens, two
Release-only FS3511 shapes, O(rows × pool) list indexing in σ's draws: 75 s → 5.9 s at 181k,
a 13-minute wall → 28 s at 1.18M) and measured where the added-scrutiny line's teeth begin
(the index build is the first engine cost visible over tool overhead, at ~1M rows; F20).
Still owner-side: the Visual Studio pilot, the pipeline's DacFx
pin, `twin evidence import` against real Dev, and the post-pilot trim decisions
(personas → phases, the register fold).

## 9. The one-line stance

A genuinely excellent body of engine truth and knowledge architecture, wrapped in three
times more prose than its consumers can use, still missing the four mechanisms — a pulled
substrate, a one-command verdict, molecular proofs, and a PR-side reproduction — that would
let its intelligence survive contact with the environment it was built for. Ship the
mechanisms, trim the ceremony, and this becomes the rare AI-enablement system that is
actually load-bearing.
