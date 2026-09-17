# V3 — The Change Engine

*What the third generation of this repository looks like, and why. Written 2026-09-17 on
`claude/v3-architecture-design-sl95mx`, after a full read of both prior generations: the C# trunk
at the repository root (v1) and the F# sidecar at `sidecar/projection/` (v2), including v2's
document corpus, its audits, its decision log, its Twin, and its `ssdt-agent` skill tree.*

---

## 0. How to read this, and the answer in one breath

This is a design document, not a plan of record. It proposes; it does not amend anything on its
own authority. It is written to be argued with, and it says where it is uncertain.

It is long because the question is large and because the evidence is specific. But it is built
so that you can stop early. §0 is the answer. §1–§4 are the diagnosis. §5–§10 are the design.
§11–§12 say what is cut from v2 and what is carried from v1, in one page each. §13–§16 are the
laws, the migration path, the process rules for agent sessions, and the risks and decisions
only the operator can make. The appendices hold the glossary, the numbers, the operation
catalog, the two item-by-item ledgers, and a reading list.

**Contents.** §0 the answer · §1 lineage · §2 the job · §3 v1 · §4 v2 · §5 thesis · §6 shape and
budgets · §7 kernel types · §8 verbs · §9 knowledge · §10 substrate and lanes · §11 the cut ·
§12 the carry · §13 laws · §14 migration · §15 sessions · §16 risks and decisions · A glossary ·
B numbers · C operations · D v2 ledger · E v1 ledger · F reading list.

**The answer.** v3 is a *change engine*. The unit of work is no longer a model to be projected;
it is a change to be proven. A small C# kernel reads a schema from wherever it lives (an
OutSystems metamodel, a live SQL Server, an SSDT project or dacpac), profiles the data beneath
it, computes the change between two states, proves that change against a real-shaped
disposable copy by letting DacFx publish it, and writes the record a reviewer can approve by
reading. The knowledge tree that already exists (the operation catalog, the shared reasoning,
the record forms) becomes the product's front door. The synthetic substrate (the Twin) becomes
the thing every proof runs on. Everything is sized to what has a consumer today: about a
quarter of v2's source, written in C# with v2's F# as the specification (§6.6), a fifth of its
tests, a twelfth of the repository's hand-written prose, and one CLI.

**Why this and not more of v2.** This repository discovered the right idea three times and then
buried it under its own apparatus. The right idea, in the `ssdt-agent` tree's words, is that
*proving is classifying*: you cannot tell what a schema change does by reading its SQL, only by
publishing it against data and reading what the engine did. v1 first found it as
*evidence-gated tightening* (its default policy mode: `NOT NULL` only when the profile shows no
NULLs). v2 typed it and then proved it after the fact with a canary. The tree found it again,
sharpest, as the classification of a change by a Strict publish, after the cutover had largely
happened. Meanwhile the engine itself grew to 118,686 lines of F# with 118,513 lines
of tests, a 30,911-line decision log, 179 root documents totalling 119,384 lines, forty-eight
axioms, eighteen theorems, and a survival list of fifteen traps that cost agents real time. The
repository's own most recent review says it plainly: *over-documented and under-mechanized*.
v3 takes that review at its word and finishes the turn v2 had already begun.

**What v3 keeps.** The typed statement stream over ScriptDom (the single best engineering
decision in either generation). The round-trip canary as the definition of fidelity. The
tightening decision tables. The synthetic-data engine and its laws. The operation catalog and
the ten-section record. The OSSYS rowset SQL. The two-release finding. The proving loop.

**What v3 cuts.** The algebra as a governing vocabulary (kept only as tests). The writer monads,
the transform registry, the four advisory passes, the estate board, the episode store, the
capability ladders, the Voice/View presentation layer, the perf-gate hooks, the lint discipline
with 566 exemptions, the chapter/handoff/decision ritual, and every document that restates
what the code or a generated artifact already says.

**What v3 changes.** C# instead of F#, with v2's F# kept as the specification the port is
checked against (§6.6). One repository layout instead of a trunk and a sidecar. One CLI instead
of two plus a script pack. Personas become phases. Docs become generated status plus a short,
hand-written spine. Agent sessions get a write budget.

The rest of this document earns those sentences. Its companion, `V3_INSTRUCTION_ARCHITECTURE.md`,
designs the documents and the agent-facing surfaces (every file, its reader and its moment; the
values register; the hooks, permissions and session protocols; the mechanisms that keep the prose
true) and should be read after §15.

---

### 0.1 The measured state of the repository (2026-09-17)

Every number below was re-measured for this document with `wc -l`, `find`, and `grep` against
the working tree at commit `4e844fc`. Where a digest or a v2 document disagreed with the tree,
the tree wins and the disagreement is noted in Appendix B.

| Region | Code (lines) | Tests (lines) | Docs (lines) | Notes |
|---|---:|---:|---:|---|
| v1 trunk (`src/Osm.*`, C#) | 78,461 | 51,969 | 5,405 root + 23,945 `docs/` + 31,364 `notes/` | 9 projects, 970 tests (936 pass, 25 pre-existing env failures, 9 skipped) |
| v1 handbook + playbook | — | — | 8,980 + 8,265 | the human SSDT handbook, twice (31 chapters and its regrouped successor) |
| v2 sidecar (`sidecar/projection/src`, F#) | 118,686 | 118,513 | 119,384 in 179 root files | 15 projects; 5,344 test functions; 209 skipped by attribute; 89 files (28,444 lines) cannot run without Docker |
| v2 decision log alone | — | — | 30,911 | one file, 2.2 MB, 480 dated entries |
| `ssdt-agent/` | 1,875 (JS) | 41 nightly facts | 21,724 in 248 files | 65 skills: 45 operations, 6 shared, 4 review, 10 top-level |
| `.claude/` | 1,405 | — | — | generated dispatch pointers + 4 hooks |
| **Total** | **~199,000** | **~170,500** | **~221,000** | ≈ 590,000 lines |

Three ratios matter more than the totals.

- **Docs to code, v2:** roughly 1.2 lines of prose per line of source. The tool's own August
  review estimated the agent-facing prose at three to four times what its consumers can use.
- **Tests to code, v2:** 1.0. The June audit named the reason: invariants are held by
  convention, so the tests carry the proof the types do not. Inside that mass, 209 tests are
  skipped by attribute (38 of the 123 axiom witnesses among them), and the two integration
  pools (89 files, 28,444 lines) cannot run without Docker and, per v2's own survival rule 12,
  skip themselves in a way that is indistinguishable from passing at summary level.
- **Ceremony to logic, v2:** `NullabilityRules.fs` is 308 lines; the decision it makes is about
  40 of them. Across `src/` there are 566 lint exemptions, 279 benchmark scopes, 619 qualified-
  access attributes, and 338 hand-built error sites (occurrence counts, `grep -rao … | wc -l`).
  Twenty-four files exceed 1,000 lines and twelve exceed 1,500; the largest two are 3,414 and
  3,402.

None of this is a moral judgement about the people or the agents who built it. It is what
happens when a strong idea is developed by many high-context sessions, each of which is
rewarded for leaving something named behind. v3's process rules (§15) exist to change that
reward.

---

## 1. The lineage, told plainly

Three generations, one job, and a job that moved under the tool's feet.

### 1.1 v1 — the exporter (C#, the repository root)

v1 answers one question: *given an OutSystems 11 application, what should its tables look like
as an SSDT project?* It runs a two-phase T-SQL script against the platform's `OSSYS_*`
metamodel and `sys.*` catalog views, producing 23 rowsets that become a JSON model (`schema/
cir-v1.json`, 422 lines). It profiles the live database for the facts the metamodel cannot
know: row counts, NULL counts, duplicates, orphans. It runs a tightening policy over the model
and the evidence to decide, per column, whether the emitted DDL may say `NOT NULL`, whether a
modelled unique index may be enforced, whether a logical reference may become a real foreign
key. It builds SMO objects and ScriptDom ASTs and writes one `.sql` file per table under
`Modules/<Module>/`, plus MERGE seeds for static entities, a `.sqlproj`, a manifest, remediation
scripts, and a telemetry bundle. Around that core it grew a UAT-users remap pipeline, a load
harness, a DMM parity comparator, an evidence cache, and an 11-verb CLI.

v1 is 78,461 lines of C# in nine projects, with 51,969 lines of tests. Its pure layers are
green. Its shape is a straight line: ingest, analyze, emit, orchestrate, operate. Its
architecture guardrails fit on one page and were mostly honoured. Its two failure modes were
the ordinary ones of a growing C# codebase: a 12-step `BindAsync` chain that returns a
30-field result record, and a handful of files that swallowed whole concerns (`CommandConsole.cs`
at 2,511 lines, `EntityDependencySorter.cs` at 2,188).

v1 shipped. It was the tool that got the first environments cut over.

### 1.2 v2 — the projection engine (F#, `sidecar/projection/`)

v2 opened on 2026-05-06 with a mandate to rebuild v1's core as a pure algebra: a `Catalog`
(the model), a `Policy` (operator intent on four axes), a `Profile` (evidence), and a factored
projection `Project = Π ∘ E` where passes enrich the catalog and sibling emitters (SSDT, JSON,
distributions, later data seeds and a dacpac) project it. Determinism, provenance, and
identity-under-rename were to be constitutive rather than disciplinary.

It delivered that, and it kept going. In roughly twelve weeks of agent-driven sessions,
organised as chapters with opens, closes, pre-scopes, handoff letters, and a supreme operating
discipline, v2 added:

- a typed statement stream rendered through ScriptDom's `Sql160ScriptGenerator`, replacing
  every string-built emitter (the one decision every later document is right to be proud of);
- a canary: deploy to an ephemeral SQL Server, read the schema back, diff it against intent;
- `CatalogDiff`, a refactor log, `migrate A B`, episodes and a lifecycle store, an eject;
- a transfer engine for moving rows between environments with surrogate-key remap, two-phase
  FK loading, capability archetypes for on-prem versus managed-cloud sinks, resume, revert,
  journals, and a "reverse leg" sized for hundreds of millions of rows;
- an estate board comparing N environments against one authored model, with findings sorted
  into lanes (decide / repair / relax / watch) and a verdict (unified / converging / forked);
- a synthetic-data engine, σ : Profile → Data, with a privacy contract and a stability law;
- a formal apparatus around all of it: 48 axioms, 18 theorems, product axioms in six groups,
  nine pillars, named failure modes, a lint with 27 rules, a perf gate, and a decision log.

By 2026-06-25 the source tree was 77,000 lines. By 2026-08-29 it was 118,686. The tests grew
to match. The root of `sidecar/projection/` holds 179 markdown files.

The vision moved as it went. `VISION.md` said *V1 ships the cutover; V2 makes it verifiable,
reversible, repeatable.* `NORTH_STAR.md` said the target is the *Total Projection*: fidelity as
a theorem the engine proves about itself. `THE_USE_CASE_ONTOLOGY.md` said the engine is a
*publication-and-provenance engine that terminates at an eject*. Each is true. Each is larger
than the one before.

### 1.3 The turn — the Twin and the `ssdt-agent` (July–August 2026)

Then the cutover began to happen, in stages, and the ground shifted. By late August 2026 QA
and UAT were already SSDT-managed, each baselined by its own publish; production had not yet
been released to; and Dev's trunk, the last to switch, was scheduled for the weekend after
2026-08-26. Nothing in the repository dated after that records the outcome, so this document
does not claim it happened; it claims the condition it creates has already arrived in
substance for two of four environments. (v2's `seal`/eject *operation*, which freezes the
episode timeline, is a different thing from this business event, and there is no evidence it
was ever run against the real estate.) After the switch there is no upstream to re-derive
from. The SSDT repository *is* the schema. Every future change is a hand edit to a
`CREATE TABLE`, made by a mixed-experience OutSystems team, in GitHub Copilot inside Visual
Studio on Windows, promoted through Azure DevOps into Octopus with `BlockOnPossibleDataLoss`
permanently on.

v2 responded with two things that are not projections at all:

- **The Twin** (`THE_TWIN.md`, charter complete 2026-07-18): one command that holds a local SQL
  Server current with the repository's definitions and fills it with deterministic, masked,
  distribution-faithful synthetic data. It reuses the kernel's σ. It is designed to be peeled
  off into its own tool. It is ~5,000 lines and 128 tests, and its own review calls it lean.
- **The `ssdt-agent` tree** (July–August 2026): a skill tree for an agent that helps a developer
  state a change in plain words, prove it on a disposable copy, and open a pull request a
  reviewer can approve by reading. Forty-five operation skills. Six shared-reasoning skills.
  A nine-state authoring machine. A ten-section record. Twenty numbered findings about what
  DacFx actually does, of which eighteen stand and two are recorded refutations of the tree's
  own earlier prose (F5 overturned by F9; F8's mechanism corrected by F9). A nightly lane that
  re-proves 41 sample changes against live SQL Server.

The tree's governing sentence is: *you cannot classify a change from the `.sql` text alone; you
publish it to a disposable copy populated with real-shaped data and read what the engine does.*
Its August review found the tree over-documented and under-mechanized, named four missing
mechanisms (a pulled substrate, a one-command verdict, molecular proofs, a PR-side
reproduction), and by the end of that same session had built three of them as scripts.

Notice what happened, precisely. The *projection* half of the F# engine (metamodel → catalog →
passes → SSDT bundle, the 40,564-line pipeline and the 14,097-line CLI in front of it) is, in
the tree's own words, "an optional accelerant, not wired": the tree's proving loop calls
sqlpackage and DacFx directly through 1,875 lines of JavaScript. The *synthesis* half (σ, the
Twin, the transfer realization it reuses) *is* wired: the nightly proof lane runs the 41 sample
changes inside `Twin.Tests.Integration` on the Twin's own minted estate. So the thing the team
will actually use is a knowledge tree, a script pack, and the Twin. The part of the engine that
was the point of v2 sits beside that, not under it.

### 1.4 What the three generations have in common

Each generation found the same truth at a different altitude:

| Generation | Where the truth showed up | What it was called |
|---|---|---|
| v1 | the tightening policy | *evidence-gated*: `NOT NULL` only when the profile shows zero NULLs (or within budget) |
| v2 | the canary and the estate | *fidelity as a theorem*: deploy, read back, diff; `Ingest ∘ Project = id` modulo named erasures |
| v2's turn | the `ssdt-agent` tree | *proving is classifying*: publish the change to a real-shaped copy; the engine's behaviour is the classification |

The truth is that **a schema decision is a claim about data, and only the database can settle
it.** v1 asked the database before emitting. v2 asked it after emitting and called the answer
fidelity. The tree asks it for every change, before the change ships, and calls the answer the
record. v3 makes that the whole design.

### 1.5 What v3 is, in the lineage's own terms

v1 was a **projection** (model → schema). v2 was a **projection with a proof** (model → schema
→ deployed → read back → diff). v3 is a **change with a proof**: (state A, state B, data)
→ delta → published on a copy → verdict → record. The model is one of several places a state
can come from; it is no longer the centre. The proof is the centre.

---

## 2. The job

A design is only as good as its picture of the work. This is the work, in concrete terms,
assembled from `KICKOFF.md`, `V2_PRODUCTION_CUTOVER.md`, `THE_USE_CASE_ONTOLOGY.md`, the
`ssdt-agent` estate ledgers, and the 2026-08-26 session handoff (which states that its facts
"override anything the older documents say").

### 2.1 The estate

- **One OutSystems 11 application** of roughly 300 tables (the recurring figure in v2's
  fixtures and handoffs is "300-table" and "~310 tables"), undergoing an *External Entities*
  cutover: every entity's storage moves from platform-managed `OSUSR_*` tables to external,
  SSDT-managed SQL Server tables that the application then references through Integration
  Studio. The logical model stays in Service Studio; the physical schema stops being generated
  by the platform and becomes a repository the team owns.
- **Four environments**: Dev, QA, UAT, Prod. Two renditions of the same model exist during the
  transition: the *physical* cloud rendition (`OSUSR_*` names) and the *logical* on-prem
  rendition (human names). QA and UAT are already SSDT-managed, each set up by its own baseline
  publish outside the release train. Dev is last; as of the newest document in the repository
  (2026-08-26) its trunk was scheduled to switch the following weekend, and nothing later
  records whether it did. Prod has not been released to.
- **Two sink archetypes** drove v2's transfer design: *full-rights* on-prem SQL Server (DDL,
  `IDENTITY_INSERT`, sink-resident progress tables) and *managed DML-only* cloud environments
  (no DDL, the sink mints keys). v2 measured 3.5 million rows in flight at the time of writing
  and sized for ~200 million.
- **Consumers beyond the app**: an SSIS team that maps a legacy database against the published
  schema each sprint (their contract is a per-release changelog and the refactorlog, §8.10); Change Data Capture running in production with features that depend on
  it (a spurious change record is a real defect, which is why "CDC-silence on idempotent
  redeploy" was v2's highest-stakes guarantee); user identities (`CreatedBy`/`UpdatedBy`) that
  must be re-keyed per environment.

- **The second half of every change.** OutSystems still owns the logical model. After a column
  lands in a database, the application cannot see it until someone opens Integration Studio,
  refreshes the external entity *from that database*, and publishes the extension: a manual act,
  per environment, with a different tool, after the deploy. An entity in the repository that was
  never refreshed is invisible to the application; an attribute the extension still maps that
  the repository dropped fails at runtime. `BlockOnPossibleDataLoss` cannot see either. Nine of
  the tree's 45 ops mention the refresh; nothing owns it. v3 gives it an owner (§9.3) and a check
  (§8.8).

### 2.2 The pipeline and its one immovable rule

Azure DevOps builds the dacpac; Octopus publishes it. The publish profile has
`BlockOnPossibleDataLoss` on, and **no single deploy can relax it**. That rule is the axiom the
whole authoring machine is built around, because DacFx's guard is *data-blind*: it fires on
`IF EXISTS (SELECT TOP 1 1 FROM <table>)`, on row presence, not on whether any row actually
violates the new constraint. Consequently:

- a `NULL → NOT NULL` on a populated table blocks even after every NULL is backfilled
  (`FINDINGS_AND_CHANGES.md` F7, proven live 2026-08-21);
- the working shape is **two releases**: R1 makes the change physically in a pre-deploy script
  while the model still declares the old shape; R2 lets the model catch up as a no-op (F4);
- putting the `ALTER` in a pre-deploy *and* changing the model in the same release both blocks
  and half-applies, leaving the pre-deploy's side effect behind (F2, F6) — the one transition
  the state machine forbids by construction;
- and the two-release pattern has its own failure mode, which presents as a green deploy: after
  R1 lands, a second publish of the *same* release, with the model still declaring the old
  shape, reverts the column (F3); worse, one more publish after a contract-phase R1 re-created
  a column with every row backfilled from its default and reported *Successfully published
  database* (F17, proven 2026-08-28). The window between R1 and R2 is therefore not a note in a
  ledger; it must be a lock, which is why the pull-request gate (§10) refuses any release that
  touches a table with an open window.

The guard has a second consequence nobody has met yet. Prod has not been released to, and its
first release is a baseline publish, not an incremental one. Prod holds rows in tables Dev does
not, so a change proven clean on Dev can block on Prod for no reason except population. Every
"will this block?" answer in the corpus is extrapolated from a copy of Dev, and
`estate/row-tiers.md` is thirty lines.

There are also toolchain facts with teeth. Constraint-trust behaviour differs across DacFx
versions: a declarative FK add read *untrusted* on 162.5.57, the engine the Twin corpus runs
in-process, and lands *trusted* on sqlpackage 170.x, the engine the hand proofs ran. The estate
pipeline's own DacFx version and the sqlpackage row are both `UNPINNED` in
`estate/toolchain.md`, so every trust-state finding in the catalog is asserted on an engine the
pipeline may not use. The warm SQL Server image is a floating `2022-latest` tag, which the same
file calls a named risk. Pinning is an afternoon's work and has been open for three weeks.

### 2.3 The people

Four people review pull requests and no one else does: three senior developers fluent in SQL
and OutSystems who have never used SSDT, and one principal fluent in SSDT who was out of office
during the initial cutover window. A dev lead is the one approver class (the principal tier was
retired on 2026-08-28). The team develops in GitHub Copilot inside Visual Studio on Windows,
against the Azure DevOps repository. They do not use Claude Code. Their machines may not have
Docker; the hosted build agents have LocalDB.

This paragraph is the most important one in the document for sizing. Every layer of prose an
agent must traverse on a Windows laptop with a modest context budget is a failure point. The
consumer of this system is a Copilot session, not a monorepo agent with an hour to read.

### 2.4 The workflows, post-eject

v2's ontology catalogued nine "proteins" for the cutover era (loads, re-keys, publications,
idempotent redeploy, in-place evolution, eject, drift, canary). After the eject, the daily
workflows collapse to these:

1. **Author a change.** A developer says "make Email required" or "split Customer into Customer
   and CustomerAddress." Someone must name the operation, gather the three facts that decide
   how it ships (is the table populated; does existing data violate the new rule; must old and
   new app code coexist), edit the `CREATE`, prove the change on a real-shaped copy, decide the
   shipping shape, pose the one business question only a human can answer, write the record,
   and, after each environment's deploy, refresh the external entity in Integration Studio,
   the half of the change no publish guard can see.
2. **Review a change.** A lead reads a pull request and approves it without a meeting, because
   the record carries the finding on top and the proof beneath, or reproduces the proof when
   trust is in question.
3. **Gate a change.** A build-validation pipeline rebuilds the dacpac, restores a versioned
   substrate, publishes the pull request's *combined* delta under the production posture, and
   posts the verdict. A release that collides with an open multi-phase lag window is refused.
4. **Keep the substrate current.** A developer's local database matches the repository and holds
   masked, distribution-faithful data. Profile Dev once; commit the literal-free shape tier;
   mint anywhere. No real data on laptops.
5. **Watch the estate.** Detect drift between environments and the repository; between the
   repository and what OutSystems believes the external entities look like; between what was
   promised (a two-release change) and what landed.
6. **Remember.** Which operations have shipped on this estate (the first time an op ships gets
   added scrutiny); which tables hold how many rows; which multi-phase changes are in flight and
   when their windows close; which toolchain versions the proofs were taken on.

The cutover-era workflows (load an environment from the cloud, transfer hundreds of millions of
rows back up, eject) are not gone, but they are *finite*. They end. The post-eject workflows do
not. That asymmetry decides what the kernel is for.

### 2.5 What "done" means for v3

- A developer states a change in their own words and, inside the same session, gets a proven
  shipping shape and a pull request the lead can approve by reading.
- The verdict is one tool call with a structured result, not eight shell commands and folklore.
- The substrate is pulled, not built, and never contains real data.
- The pipeline reproduces the proof; the human makes the business call.
- The repository's description of itself is generated where it can be and short where it
  cannot, so that it cannot lie for long.
- The whole thing is small enough that one person can hold it in their head and one agent can
  hold it in its context.

---

## 3. v1 — what it got right, what it got wrong

v1 deserves a fair reading. v2's documents treat it as an "editorial donor," which is generous
in tone and dismissive in substance. Read on its own terms, v1 is a competent, conventionally
layered C# system that solved the first problem well enough to ship, and that discovered most
of the domain truths v2 later formalised.

### 3.1 What v1 got right

**The contract with the platform.** `outsystems_metadata_rowsets.sql` (1,253 lines) is the one
artifact both generations agree on; v2 carbon-copied it byte for byte on 2026-05-17. It reads
`OSSYS_Espace`, `ossys_Entity`, `ossys_Entity_Attr`, and the `sys.*` catalog views in two
phases, reconciles what the platform *intends* with what the database *has*, and emits 23
rowsets. Its comments carry real domain knowledge: default-collation suppression ("restating it
couples emitted DDL to the source instance"), tolerance for estates whose `ossys_Entity_Attr`
lacks `Order_Num`, and authored column order (`Order_Num` ascending, never PK-first). This is
the kind of thing that only exists because someone stared at a real estate. v3 keeps it.

**The tightening algebra.** Six named signals (`S1_PK`, `S2_DB_NOT_NULL`, `S3_FK_SUPPORT`,
`S4_UNIQUE_CLEAN`, `S5_LOGICAL_MANDATORY`, `S7_DEFAULT_PRESENT`) plus one evidence gate
(`D1_DATA_NO_NULLS`), composed by `AllOf`/`AnyOf` combinators into three modes: Cautious (trust
only physical evidence), EvidenceGated (default; metadata *and* data must agree), Aggressive
(trust metadata; remediate data). A null budget (default 0.0). Three remediation options per
violating column: `UPDATE` to a sentinel, `DELETE`, or `SELECT` for review. This is the germ of
"proving is classifying": the schema you may emit depends on the data you found. v2 kept the
decision but reified the outcome as a typed ternary (`EnforceNotNull | KeepNullable |
RequireOperatorApproval`); v3 keeps that shape and flattens the rest.

**Emission through the object model.** v1's guardrail 5 ("SMO and ScriptDom as the sole DDL
authorities; any helper that manipulates raw SQL must exist purely for assertions") was
honoured with exactly one self-flagged exception, the `NOCHECK` foreign-key statement at
`CreateTableStatementBuilder.cs:267`. v2 finished the job with ScriptDom's typed AST end to
end. The instinct was v1's.

**The output layout.** `Modules/<Module>/<Schema>.<Table>.sql`, seeds as idempotent `MERGE`
blocks, a `.sqlproj`, a manifest with coverage counts, a post-deployment bootstrap with guard
logic. v2 mirrored the layout for parity; the `ssdt-agent` proving ground uses the same shape.
This is now the estate's actual repository layout. It stays.

**Fixture-first testing and a page of guardrails.** `tests/Fixtures/emission/{edge-case,
edge-case-rename, edge-case-untrusted}` pin emitted trees against golden output; the tightening
matrix test enumerates mode × signal; the JSON round-trip test is 1,707 lines because the
contract is wide. `architecture-guardrails.md` is 48 lines and was mostly followed. A
970-test suite with 936 green and every failure triaged by name is a healthy place to be.

**The handbook.** `handbook/` (31 chapters, 8,980 lines) and its regrouped successor
`ssdt-playbook/` (8,265 lines) are a genuine SSDT curriculum for OutSystems people: state-based
modelling versus migrations, idempotency, the refactorlog and rename discipline, multi-phase
evolution, CDC and schema evolution, an anti-patterns gallery, an operation reference. The
editorial review scored it 95/100. The `ssdt-agent` op skills cite its chapters by filename.
It is the ancestor of the knowledge layer and much of it survives into v3 unchanged.

**Two features the post-eject world still needs.** The UAT-users remap (8 steps: discover the
user-FK catalog, load two inventories, analyze FK value distributions, match by strategy,
prepare and validate a map, emit batched `UPDATE`s) is the only place either generation handles
the "same logical user, different id per environment" problem end to end. And the DMM lens
machinery (project SMO, T-SQL, or an SSDT project into one canonical `DmmTable[]` and diff) is
the only place either generation compares *arbitrary* schema sources rather than "model versus
deployed." v2 sunset the second and carried the first only as `UserFkReflowPass`. v3 wants both,
smaller.

### 3.2 What v1 got wrong

**It built an application server to run a batch job.** `IApplicationService<TInput,TResult>`
(seven implementations repeating one five-step control flow), a MediatR-style
`ICommandDispatcher`, `IBuildSsdtStep<TState,TNextState>` with twelve DI-injected steps chained
by `BindAsync`, a 30-field `BuildSsdtPipelineResult`, seven option binders repeating one shape,
eleven verb factories. All of this is machinery for extensibility that a deterministic file
transform does not need. The whole of `build-ssdt` is: read model, read profile, decide, emit,
write. v2 rightly replaced the dispatch with functions; v3 keeps it that way.

**Its biggest files are its most important logic.** `EntityDependencySorter.cs` is 2,188 lines
(Kahn, Tarjan, automatic two-node and multi-node cycle peeling, scored manual ordering,
alphabetical fallback, diagnostics at every stage). `CommandConsole.cs` is 2,511 lines of
output formatting for every verb. `TighteningOpportunitiesAnalyzer.cs` is 1,027;
`PolicyCommandFactory.cs` is 1,194; `SqlDynamicEntityDataProvider.cs` is 936. The domain logic
that matters most (ordering under cycles) is in the least readable file. v2's
`TopologicalOrderPass.fs` (977 lines) is the same problem solved again with an exact
minimum-feedback-set solver; v3 wants one implementation, small, with the cycle policy as data.

**String rationales as the decision record.** Decisions carried `SortedSet<string>` of ~30
constant labels; the summary formatter classified them into six buckets by substring. It works,
and it is exactly the kind of implicit contract that drifts. v2's typed outcome DUs are the
right correction.

**The JSON intermediate was lossy.** v1 wrote the metamodel to `osm_model.json` and read it
back; eight of the 23 rowsets exist only to build that JSON. Duplicate attribute rows appeared
when a reference target had multiple active/inactive versions, and a deduplicator with a
tie-break rule was needed to repair what the projection had broken. v2 solved this structurally
by reading rowsets directly into its IR ("the JSON-projection-lossiness class structurally
closed"). v3 inherits the rowset path and drops the JSON path.

**It never asked DacFx.** v1 emitted, validated the project with SMO and DacFx (`SsdtSqlValidator`),
and compared against a DMM baseline. It never *published* the emitted schema to a database and
read back what happened. That is the step v2 added as the canary and the step the
`ssdt-agent` made the centre of everything. v1's tightening asked the data before emitting;
nothing asked the engine after.

**Documentation as a second codebase.** `notes/` holds 31,364 lines, including a 9,979-line
early handbook draft that the real handbook superseded; `docs/` holds 23,945 lines of milestone
specs (M1.0 to M2.4) and architecture drafts (`entity-pipeline-unification-v2.md` at 2,865 lines,
`domain-model-constitution.md` at 2,323). The handbook exists twice. `TEMPLATED_LOGIC_AND_
BUSINESS_RULES.md` (2,948 lines) is the authoritative catalogue of v1's rules and is also the
only one of these that v3 should mine before archiving.

### 3.3 v1's ledger in one line

Keep the SQL contract, the tightening decision, the object-model emission, the output layout,
the handbook, the UAT-users idea, the DMM idea, and the fixture-first tests. Leave the
application-server architecture, the string rationales, the JSON intermediate, and the second
codebase of notes.

---

## 4. v2 — what it got right, what it got wrong

v2 is harder to assess than v1 because it audited itself relentlessly and most of what can be
said against it, it already said. Twelve adversarial audits, a 25-finding reconnaissance, a
"crystalline form" study that tried to falsify its own compression claims and succeeded twice,
and an August review of the agent tree that opens with "over-documented and under-mechanized."
This section does not repeat those documents. It says which of their verdicts v3 accepts,
which it sharpens, and what they could not see from inside.

### 4.1 What v2 got right

**The typed statement stream.** Every SQL byte v2 emits comes from a 26-variant `Statement` DU
(`Targets.SSDT/Statement.fs`) rendered through ScriptDom's `Sql160ScriptGenerator` with pinned
options. `Render.fs` collapsed to four public functions once the last `StringBuilder` relic was
retired. Identifier quoting has one owner (`SqlIdentifier.quote`, byte-verified against
`EncodeIdentifier`, which fixed a real `]` bug). The `MERGE` for seeds is a typed
`MergeStatement` with a typed change-detection predicate. This is the single best engineering
decision in either generation. v3 keeps `Statement`, `ScriptDomBuild`, and `Render` nearly
verbatim.

**The canary as the definition of fidelity.** Deploy the emitted DDL to an ephemeral SQL Server,
read the schema back through `INFORMATION_SCHEMA`/`sys.*`, diff it against intent. Equality is
*in a quotient*: `PhysicalColumn.Type` is the coarse `PrimitiveType`, not the storage type, and
`CRYSTALLINE_FORM.md` §3.2 shows exactly why the coarseness is load-bearing (a `BIGINT → INT`
regression is invisible by design, and making the quotient finer would fail every faithful
redeploy on the first `SqlStorage = None` readback). Named tolerances (`ToleratedDivergence`, a
closed DU where each variant carries a machine-parsed ladder tag) are the quotient's defining
relations. This is the correct way to say what "the same schema" means. v3 keeps the canary,
the quotient, and the tolerance set, and drops the axiom numbering around them.

**Identity separate from name.** `SsKey` (four variants: `OssysOriginal`, `Synthesized`,
`DerivedFrom`, `V1Mapped`) means a rename can never re-key an entity, and the refactorlog entry
is derived from identity rather than authored alongside it. The rename projection bug of
2026-07-06 (a flat `Map<Name,Name>` that emptied `Order.Status` when `Invoice.Status` was
renamed) is precisely the bug this design prevents once applied consistently. v3 keeps a
stable-identity key, though it can be simpler than four variants post-eject (§7).

**The tightening decision, typed.** `NullabilityOutcome = EnforceNotNull of evidence |
KeepNullable of reason | RequireOperatorApproval of conflict`, with the evidence and reasons as
closed DUs. `ForeignKeyOutcome` with eight named keep-reasons. `UniqueIndexOutcome` with the
"promotion advised, not applied" arm that surfaces a candidate without acting on it. Every
decision total, every skip named. The decision *tables* (§7 of the kernel roll-up reproduces
them from source) are the distilled domain knowledge of both generations and go into v3 as
plain functions.

**Purity at the core, I/O at the edge.** `Projection.Core` has no I/O, no clock, no `Task`. A
typed-tree analyzer enforces it. Determinism is constructed (sort by key, `decimal` for
continuous evidence, boundary-supplied clocks), and byte-identical output across hosts is a
tested law. v3 keeps this discipline and loses the analyzer plugin in favour of the compiler:
if the kernel has no dependency that can do I/O, it cannot do I/O.

**Discover once, derive pure.** `EvidenceCache` materialises the row stream into aggregates,
and every `Profile` axis is a total, pure function of the cache and the catalog. This is the
correct factoring of profiling, and it is why a live profiler and a synthetic profiler can share
one derivation. ~6,000 SQL round-trips became ~900 at 300-table scale. v3 keeps it.

**σ: Profile → Data.** The synthetic-data engine mints deterministic, FK-aware, masked rows from
evidence; `π ∘ σ ≈ id` is a tested law; values are content-addressed to `(master, kind, column,
row)` so a schema edit re-mints only the columns it touched ("S-stable"). The Twin wraps it in
one command, a closed config, evidence tiers (a committed literal-free shape tier; a rich tier
kept out of the repo), scenarios that rewrite but never generate, and a post-mint trust gate.
~5,000 lines, 128 tests. The Twin is the one v2 subsystem whose size matches its job.

**The `ssdt-agent` tree's findings.** F1–F20 in `FINDINGS_AND_CHANGES.md` are engine facts
captured with real output: the data-blind row-presence guard; the pre-deploy-plus-model
half-application; the surviving side effect; the two-release shape; `Msg 515` when a seed still
writes NULL after the tightening; the phantom rename and delete under
`DropObjectsNotInSource=false`; the reconcile-then-trust FK shape (which overturned the tree's
own F5). The 45 op skills, six shared-reasoning skills, the nine-state authoring machine, and
the ten-section record are the product. They stay.

**The habit of publishing its own refutations.** F5 struck through with a dated note rather than
edited. `CRYSTALLINE_FORM.md`'s own correction banner. Every deferral with a named trigger.
The reconnaissance handoff: "don't defer silently; raise every deferral for approval first."
This is the culture v3 must not lose while it removes the ceremony that culture accreted.

### 4.2 What v2 got wrong

The reconnaissance's one-paragraph framing is exactly right and worth quoting: *"the recurring
shape of the debt is not sloppiness — it is laws honored by vigilance that want to become
structural, plus three or four migrations that stopped near 50%."* v3 accepts that diagnosis
and adds four more that the reconnaissance could not see because it was standing inside them.

**(a) The algebra became the governing vocabulary instead of the tests.** "Its formal soul is
one adjunction ... lifted from states to the displacements between them: state is a torsor over
delta; minimality is measured (CDC capture count = the data norm)." Every one of those claims
is *true*, and every one has a test. But 48 axioms and 18 theorems, three layers (L1 witness,
L2 faithful, L3 composed), six totalities, a matrix regenerated by script, a verifiability gate
that refuses phantom buckets, and the requirement that every axiom change ship with its
`AxiomTests.fs` entry in the same commit — this is an apparatus for *talking about* correctness
that outgrew the correctness it talks about. The tests are 123 facts in a 1,720-line file, 38
of them skip-stubs with named triggers, 85 live. A v3 reader needs the twelve laws in §13 and
the tests that pin them, in plain names. The numbering can go.

**(b) The engine kept generalising after the job had specialised.** The estate board (2,633
lines) with four lanes and three verdicts; episodes, an episodic lifecycle, an eject that
verifies itself by reconstruction; a `Ledger` that is a generic two-phase admission algebra
with a `Verified<'entry>` proof token; capability ladders and "lane descent" for a sink class
(managed DML-only cloud) that exists to serve the *reverse leg* of a cutover that is finishing;
four advisory passes (profile anomaly, schema complexity, cascade shock, query hints) whose only
consumer outside the kernel is the manifest, and two more (centrality, bounded contexts) whose
only other consumer is the synthetic-volume allocator, one of them behind a default-off flag. Each was built under
a named trigger, and most triggers were real when they fired. But the post-eject job (§2.4) is
author, review, gate, substrate, watch, remember, and none of it needs a torsor.

**(c) Ceremony outgrew logic.** `NullabilityRules.fs` is 308 lines for a decision that fits in
40; the rest is `toStructured`/`toDiagnosticString` renderers for every DU. Across `src/`:
566 lint exemptions each carrying a four-question rationale, 279 benchmark scopes, 619
`RequireQualifiedAccess` attributes, 338 hand-built error sites, a `Lineage<Diagnostics<'a>>`
double writer with its own computation-expression builders and a "writer-fidelity" law, a
transform registry that exists so that `registered ⇔ executed` can be a property test rather
than a fact (the chain is one literal list; the registry projects two views of it). The
sibling-wrapper discipline, the four-question naming analysis, the performance-of-compliance
failure mode: these are good instincts written down as law and then enforced by grep. The cost
shows up as twenty-four files over 1,000 lines, twelve over 1,500, the largest two over 3,400.

**(d) The documentation ritual generated the mass.** Chapters open and close with an eight-item
ritual. Handoffs are prepend-only letters (3,887 lines). Decisions are appended (30,911 lines,
480 dated entries, more than half marked as operator decisions). The README's "Status at chapter X close" sections
are a changelog inside a front door. `CLAUDE.md` was rebuilt from scratch because "the
predecessor's table drifted against the code within weeks," and its rebuilt form declares any
restated count a first-class defect, then restates fifteen survival rules because each cost an
agent real time. This is a system that knows its docs lie and has responded by writing more
docs about not lying. The August review measured the agent-facing prose at three to four times
what its consumer can use; `CRYSTALLINE_FORM.md` estimated 35–55% of the corpus as compressible
sediment. Both are conservative.

**(e) The migrations that stopped at 50%.** The reconnaissance lists them: typed AST never
reached the pipeline's reverse-leg SQL (fixed on its branch), the Voice migration stalled with
~121 raw-prose emitters beside ~103 voiced, `registered ⇔ executed` reached the pass chain but
not the binder registry, the value-object lift reached the catalog but not its leaf fields.
Most were then landed in a single sweep (17 of 25 fully). The pattern is the point: a strong
idea applied once, celebrated, and not propagated, because the next session had a new chapter
to open.

**(f) It built the engine the agent would use and then the agent used scripts.** The
`ssdt-agent` tree says the F# engine is "an optional accelerant, not wired." That is exactly
true of the projection half (metamodel → catalog → 21 passes → SSDT bundle, plus the
40,564-line pipeline and 14,097-line CLI around it) and exactly false of the synthesis half:
the Twin calls `Transfer.runSynthetic`, `Deploy.executeBatch`, and the transfer's wipe, and the
nightly proof lane runs the 41 sample changes inside `Twin.Tests.Integration` on the Twin's
minted estate. The tree's proving loop is `prove.mjs`: build the `.sqlproj`, script the delta,
publish under the Strict profile, parse the outcome, return one JSON verdict. Its substrate is
`bake.mjs`: a fingerprint-versioned `.bacpac`. Its gate is `inflight-check.mjs` plus an ADO
pipeline template. So of the 118,686-line engine, the part the team's workflow rests on is the
Twin (~5,000 lines) and the σ and publish code it reuses; the part that was v2's reason to
exist is not in the loop. Meanwhile, the CLI that is meant to be the operator surface
requires `dotnet run --project src/Projection.Cli --` from inside `sidecar/projection`, a
pinned SDK with `rollForward: disable`, and Docker for its most useful verbs. There is no
published binary. The most important verdict in this document is the simplest: **the working
product is the knowledge tree plus a proving loop plus a substrate, and it is 1,875 lines of
JavaScript away from the engine that was supposed to be its core.**

**(g) The parity audit found three blocker-class regressions versus v1, and the response shows
both the strength and the cost of the method.** The 2026-07-17 audit (a deployed-database diff
of identical estates) named data-lane topological ordering (one unresolvable cycle threw the
whole catalog into an alphabetical fallback), authored default-expression rendering
(`getutcdate()` became `CAST('getutcdate()' AS datetime2(7))`, a default that deploys and then
fails at first insert), and composite-PK FK truncation (a multi-leg foreign key emitted with one
leg, `Msg 1776` at deploy). All three were addressed the next day, 2026-07-18: the first by a
partial topological order that confines the fallback to the affected component, the second by
classifying authored defaults at the lift (`SqlLiteral.ExpressionLit`), and the third by a
*refusal gate* rather than a fix (a reference targeting a composite primary key refuses the
publish, because the `Reference` IR still carries no referenced-column list). That is the
strength: an empirical oracle found real bugs, and the culture closed them in a day. The cost is
in the same day's decision log: eleven other entries landed on 2026-07-18 alongside these three,
on cycle certificates, T18, A46, the condensation carrier, and the Twin's charter. The repair
and the expansion ran at the same speed. v3's §14 keeps the oracle (a deployed-database diff
against v1's emission and the 41 proven changes) and §7 carries the referenced-column list into
the IR so the third blocker is a feature rather than a refusal. The same audit found one axis
on which v1 simply beats v2: v1 preserves an index's `DATA_COMPRESSION` (row or page) and v2
drops it, so a compressed table decompresses on redeploy. v3's `IndexOptions` carries
compression and law 2 (§13) covers it.

### 4.3 The open defects v3 inherits, and how each is resolved

| Defect (source) | Status in v2 | In v3 |
|---|---|---|
| B-1 data-lane ordering falls back to alphabetical for the whole catalog on one cycle (parity audit) | fixed 2026-07-18 (partial order per component) | by construction: `Order.Cycles` is per component (§7.7) |
| M-1 authored defaults rendered as string literals (`getutcdate()` → `CAST('getutcdate()' …)`) | fixed 2026-07-18 (`SqlLiteral.ExpressionLit`) | kept: `SqlLiteral.Expression of call` |
| B-3 composite-PK foreign key emitted with one leg (`Msg 1776`) | converted to a refusal 2026-07-18; still unsupported | fixed: `Reference.Columns` is a list (§7.3) |
| index `DATA_COMPRESSION` dropped on emit (parity audit) | open | `IndexOptions.Compression`; covered by law 2 |
| `SchemaComplexityPass` may compute over an empty topology (`CRYSTALLINE_FORM.md` §3.4) | the current chain wiring appears to lift it with the computed topology; unconfirmed | moot: the pass is deleted |
| `SelectionPolicy.filterCatalog` marked "DORMANT, unregistered, no pipeline wiring" in its own comment | open | moot: selection is `read --modules`/`--tables`, not a policy axis |
| 38 of 123 axiom witnesses are skip-stubs with triggers | open by design | moot: twelve laws, no stubs; a law without a green test is not listed |
| the two proof corpora run on different DacFx versions; the pipeline's version is unpinned (`estate/toolchain.md`) | open, owner-side | `prove` refuses when the pinned version and the project's package disagree (§10.2); the pin is an operator decision (§16) |
| two-release lag window is a ledger, not a lock (F17) | mechanism added 2026-08-28 (`inflight-check.mjs`) | `estate gate` refuses the collision (§8.10) |
| `ReadSide` marks every reconstructed data-bearing table `Static`, so profiling a readback catalog yields an empty evidence cache (`CLAUDE.md` survival rule 8) | open; a known trap on a secondary path | on v3's *primary* path (the live database is one operand of every delta): `read --from sql` sets `Seed = None` unless the table is named static in the repository; law 2 covers it |
| `ForeignKeyRules.isIgnoreRule` is hardcoded `false`, so `DeleteRuleIgnored` is unreachable while the platform's delete-rule vocabulary includes *Ignore* | open (`ForeignKeyRules.fs:216`) | `ReferenceAction` carries `Ignore` from the reader; the decision table has no unreachable arm |
| a trigger body ScriptDom cannot parse degrades to a comment marker (`ToleratedDivergence.TriggerBodyUnparsedDropped`) and would vanish on redeploy | open, tolerated | `read --from ssdt` refuses an unparsable body (exit 9) rather than tolerating it; law 3 catches any survivor |
| the warm SQL Server image is a floating `2022-latest` tag under version-stamped guard evidence (`estate/toolchain.md`) | open, named | `ci/` pins a CU digest; `twin bake` records it in the artifact name |

### 4.4 What the audits could not see

Every v2 audit optimised inside the frame "v2 is the engine; make it smaller, purer, more
coherent." `THE_PROJECTION_PRINCIPLE.md` is the high point of that frame: one signature move
(measure once, project many ways, let agreement be the law) applied self-similarly down the
tower. It is a beautiful document, and it proposes five more keystone abstractions.

What the frame cannot see is that the job changed underneath the tower. The cutover is finite
and nearly done. After it, the engine is a library that three post-eject tools call into: a
prover, a substrate, and a watcher. The question v3 asks is not "how do we make the projection
engine crystalline" but "what is the smallest thing that proves a change, and which parts of
v2 are that thing." §11 answers module by module.

### 4.5 v2's ledger in one line

Keep the statement stream, the canary and its quotient, the identity key, the decision tables,
the pure core, the evidence cache, σ and the Twin, the op tree and its findings, the culture of
refutation. Leave the algebra as vocabulary, the estate/episode/ledger generality, the
ceremony-to-logic ratio, the documentation ritual, and the unfinished sweeps. Wire the engine
to the tool that will actually be used, or admit it is a library and size it as one.

---

## 5. The v3 thesis

Five sentences. Everything after this section is their consequence.

### 5.1 The unit of work is a change

Not a model. Not a projection. A *change*: the displacement between two states of a schema,
carried by a pull request, published against data. v1 produced a state from a model. v2
produced a state from a model and then learned to compute displacements between states
(`CatalogDiff`, `migrate A B`, the refactorlog). The `ssdt-agent` tree produced only changes and
never a state. v3 starts where the tree starts. A state is something a change is *between*; a
model is one of several places a state can be read from.

Consequence: the kernel's central types are `Schema` (a state), `Delta` (a displacement between
two schemas, with a rename map), and `Verdict` (what the engine did when the delta was published
against data). Everything else serves those three.

### 5.2 Proof is the classifier

A change is classified by publishing it, not by reading it. The classification has two findings
that are never collapsed: *how it ships* (one release; one release with the gate relaxed, which
this estate cannot do; two releases with a pre-deploy) and *what the approver weighs* (existing
data affected; first time on this estate; more than a million rows; a business fork only a human
can settle). v1 called the first finding "tightening" and computed it from a profile. The tree
computes it from a Strict publish and reads the guard in the generated script. v3 does both, in
that order: profile to *predict*, publish to *prove*, and the record says which is which.

Consequence: `prove` is one verb, one tool call, one structured result, with exit codes that a
Copilot session cannot misread (`0` clean, `3` blocked, everything else a tool failure). The
predictor (the tightening tables) is a pure function that runs in milliseconds; the prover
(DacFx against a disposable copy) runs in seconds and is the only thing allowed to say
"ships as".

### 5.3 The record is the product

The thing a lead approves is the ten-section record: verdict, intent, what changes, before
promoting, the data, how it ships, what proving showed, after-deploy checks, rollback, not
checked. It is agentless, finding-on-top, proof-beneath, in a DBA's words. v2's `THE_VOICE.md`
(twelve rules) and the tree's `THE_RECORD.md` (nine rules) already say how to write it. The
sample pull request for make-mandatory is the exemplar. The pipeline reproduces its proof;
the human makes the business call.

Consequence: every kernel output that reaches a human is shaped for the record. Diagnostics are
findings with codes; verdicts carry the verbatim `Msg`; rollback is computed as the inverse
delta where one exists and named as "not auto-undone" where it does not. There is no separate
"voice" layer with its own AST, because the record is the only voice.

### 5.4 The kernel is small because it is sized to consumers

A module earns its place by having a consumer *today* in one of the six post-eject workflows
(author, review, gate, substrate, watch, remember) or by being required to finish the finite
cutover (transfer, reverse leg, eject), in which case it is kept in a clearly labelled wing and
retired when the cutover ends. Nothing is kept because a law would be prettier with it. v2's
own rule ("primitives at the second consumer; zero-consumer symmetry-builds get deleted") is
applied to v2 itself, module by module, in §11.

Consequence: about 20,000 lines of F# in the kernel, adapters, and CLI together (§6.4), down
from 118,686; tests of about 20,000 lines, mostly property-based and integration, down from
118,513; the advisory analytics, the estate board, the episode store, the ledger algebra, the
capability ladders, the transform registry, the writer monads, and the presentation AST are
gone or folded.

### 5.5 Docs are generated or short, and knowledge is kept

Two kinds of prose exist. *Knowledge* is what only a human or a real engine run can discover:
the op skills, the six shared-reasoning skills, the findings ledger, the record forms, the
handbook chapters the skills cite, the estate ledgers. That is kept and curated. *Description*
is what the code, the tests, the config schema, or git already know: status, counts, verb
tables, the pass chain, the axiom matrix, chapter histories, handoff letters. That is generated
where it can be (CLI help, config schema, a status page from tests), and where it cannot be
generated it is short: one README per package, one architecture page, an ADR log of one-line
entries. The v2 corpus is archived out of the working tree, whole, with an index, because it is
provenance and provenance should be findable, not read.

Consequence: hand-written docs in the working tree under 7,000 lines outside the knowledge
tree, and the knowledge tree itself under 11,000; agent sessions have a write budget (§15) that forbids new doctrine documents.

### 5.6 Three corollaries

**One language, one solution, one CLI.** C#, with v2's F# kept as the specification the port is
checked against (§6.6): closed hierarchies encode the unions, ScriptDom and DacFx are C# libraries,
and the tree's proof scripts become verbs of the engine they used to call.
v1's C# is frozen as a reference implementation until v3 passes the parity oracle, then
archived. Twin's verbs fold into the one CLI as `twin` subcommands; the JavaScript scripts
become verbs.

**Personas are phases.** Intake, author, and review are stages of one conversation with one
agent, not three agents with a handoff protocol. The reviewer keeps a separate skill because
the reviewer is a separate person with a separate trust model; but the *gate* is the pipeline.

**Laws are tests with plain names.** The twelve laws in §13 (byte-determinism, round-trip in
the quotient, sibling agreement, CDC-silence on idempotent redeploy, identity survives rename,
zero orphans after a mint, and so on) each have exactly one test whose name is the law in
English. There is no axiom index, no bucket, no matrix, no verifiability gate; a law without a
test is not a law, and the test list is the law list.

---

## 6. The shape

### 6.1 One layout

v3 does not live in a sidecar. It is the repository. v1 and v2 are archived in place under
`archive/` (still buildable, still cherry-pickable, no longer the working tree) until §14's
parity oracle retires them.

```
/
  README.md                 what this is, in 150 lines; the verbs; where knowledge lives
  ARCHITECTURE.md           this document's §6–§10, kept current by the rule in §15
  DECISIONS.md              one line per decision: date · decision · link to PR; no prose
  LAWS.md                   GENERATED from tests: the law list with each test's name and status
  global.json               .NET SDK pin: the current LTS feature band (rollForward: latestPatch, not disable)
  Estate.sln

  kernel/                   C# library. Pure. No I/O, no clock, no Task. ≤ 11,000 lines (§6.6).
    Schema.cs               the state: Schema · Table · Column · Reference · Index · Check · Trigger · Sequence
    Identity.cs             Key (stable identity) and Name (display); the rename map
    SqlType.cs              storage types, literals, identifiers (from v2's SqlStorageType/SqlLiteral/SqlIdentifier)
    Evidence.cs             Profile: per-column facts, per-reference orphans, per-index duplicates, distributions
    Decide.cs               the tightening tables: nullability · foreign key · unique (pure functions)
    Delta.cs                the change: ChannelDiff per channel + rename map + facet changes
    Order.cs                topological order with an explicit cycle policy
    Statement.cs            the typed SQL statement DU (from v2, verbatim)
    Synth.cs                σ : Evidence → rows (from v2's SyntheticData, corrections folded in)
    Record.cs               the ten-section record as a type, with its render

  io/                       C# library. Everything that touches a disk, a socket, or a clock. ≤ 17,000 lines.
    Ossys.cs                the OSSYS rowsets: the SQL contract + 22 handlers → Schema (from v2's Adapters.OssysSql/Osm)
    SqlServer.cs            read-side (INFORMATION_SCHEMA/sys.*) → Schema; profiler probes → Evidence; fingerprints
    Ssdt.cs                 read an SSDT project or a dacpac → Schema (DacFx TSqlModel); write the bundle
    Render.cs               Statement → text through ScriptDom (from v2's ScriptDomBuild + Render, verbatim)
    Emit.cs                 Schema → bundle: per-table .sql, seeds, refactorlog, Verify/ queries; a .sqlproj only on --init
    Publish.cs              DacFx publish (Strict | Permissive) → Verdict; read the generated script's guards
    Twin.cs                 container lifecycle · bake/restore · mint orchestration · trust gate (from Twin.Runtime)
    Move.cs                 [cutover wing] transfer: ingest · plan · phase-1 · phase-2 · revert (from TransferRun, reduced)

  cli/                      one executable: `estate`. ≤ 2,000 lines. Verbs in §8.
  knowledge/                the ssdt-agent tree, trimmed: authoring.md · reviewing.md · record.md · ops/ · shared/ · findings.md · samples/ · ledgers/ · handbook/ · copilot/ (§9)
  ci/                       proof lane · bake lane · PR gate (GitHub Actions + Azure DevOps templates)
  tests/
    Kernel.Tests            property tests on the pure kernel (CsCheck) + the twelve laws
    Io.Tests                integration against SQL Server (Docker or LocalDB), serial
    Golden/                 the golden schema + its emitted bundle; the 41 proven changes
  archive/
    v1/                     the C# trunk, frozen at 4e844fc
    v2/                     sidecar/projection, frozen; its 179 documents indexed by ARCHIVE_INDEX.md
```

Three packages of code, one of knowledge, one of CI. The dependency direction is a straight
line: `kernel ← io ← cli`; `knowledge` and `ci` depend on `cli`'s verbs and nothing depends on
them. `kernel` references no package that can perform I/O, which is how purity is enforced: by
the absence of a capability, not by an analyzer.

Two dependency laws are kept from v2, because they were cheap, they survived, and they are
tests rather than doctrine. The first is v2's "pure core" (`Projection.Core` has no I/O, no
clock, no `Task`; enforced by a 142-line typed-tree analyzer plugin). v3 enforces it as a test
that asserts the kernel project's package references are the BCL and
`System.Collections.Immutable` only, plus the banned-API analyzer's list (`BannedSymbols.txt`)
forbidding `System.IO`, `System.Net`, `DateTime.Now`, `Random`, and `Task` in `kernel/`. The
second is the Twin's kernel manifest (`Twin.Core → Projection.Core` only, enforced by
`BoundaryTests.fs`). v3's form: `io/` does not reference `cli/`, and nothing references
`knowledge/` or `ci/` except by reading files. Both tests live beside the twelve laws (§13).

### 6.2 The dependency graph, drawn

```
        knowledge/ (skills cite verbs)          ci/ (lanes call verbs)
                     \                              /
                      v                            v
                            cli/  (estate)
                               |
                               v
                             io/   Ossys · SqlServer · Ssdt · Render · Emit · Publish · Twin · [Move]
                               |
                               v
                           kernel/  Schema · Identity · SqlType · Evidence · Decide · Delta · Order · Statement · Synth · Record

   external:  ScriptDom (Render, Ssdt)   DacFx (Ssdt, Publish, Twin)   Microsoft.Data.SqlClient (SqlServer, Twin, Move)
              Testcontainers (tests only)   Bogus (Synth realization, io side)   CsCheck (tests only)
```

Compare v2: fifteen projects, with `Projection.Pipeline` (40,564 lines) sitting between the
kernel and the CLI as an orchestration layer that owned config binding, forty-six run kinds,
seven declared spines, three seams, and the estate. v3 has no orchestration layer. A verb is a
function in `cli/` that calls `io/` functions that call `kernel/` functions. If a verb needs
staging, it is staged in the verb. "Declared ⇔ executed" is true because the verb *is* the
declaration.

### 6.3 What each package owns, and what it refuses to own

**kernel/** owns every decision and every shape. It refuses to know where anything came from
(`Schema` carries no connection, no path), when anything happened (no clock; the boundary
stamps), or how anything is displayed (no renderers per DU; `Record.render` is the one
exception, because the record is a kernel value). It has one error type, one finding type,
and returns plain records and lists. There is no `Lineage<Diagnostics<'a>>`; a decision
function returns its decisions and its findings as two fields of one record, and the caller
appends them to the run's findings. There is no transform registry; the order of passes is a
list of functions in `Decide.fs`, and the test that the list is complete is the compiler.

**io/** owns every effect, in modules named for the thing they touch. It refuses to make
decisions: `SqlServer.profile` returns evidence, never a verdict; `Publish.strict` returns what
DacFx did, never what it means; `Ossys.read` returns a `Schema`, and the OutSystems-type to
SQL-type mapping it applies is a kernel function it calls (`SqlType.ofOutSystems`), not logic it
owns. This is v2's "discover, then derive" membrane made structural.

**cli/** owns the verbs, the exit codes, the config file, and the human-facing rendering of
findings and verdicts. It refuses to contain logic that a test could not reach through a
library call: every verb is `parse args → call io → render`. There is no `View` AST, no
`Voice` register, no themed TTY renderer; there is Markdown to stdout, JSON with `--json`, and
the record.

**knowledge/** owns what only a human or an engine run can know. It refuses to restate what
the CLI can print: no verb tables, no config references, no status. Skills cite verbs by name
and findings by number.

**ci/** owns the three lanes and nothing else.

### 6.4 The size budget, and where it comes from

The budget is derived, not wished. Each line is the v2 module the v3 module descends from, its
current size, what is removed, and the floor that remains. Where the planning pass measured a
floor directly (`Render`, `ReadSide`, the Twin), that floor is used; where this design deletes
more than the pass assumed (the readback type, the manifest, the migration emitter), the smaller
number is used and the reason is in the row.

| v3 module | Descends from (v2) | v2 lines | What goes | v3 budget |
|---|---|---:|---|---:|
| `kernel/Schema.fs` | `Core/Catalog.fs`, `PhysicalSchema.fs`, `KindColumns.fs` | 3,312 | per-DU `toStructured` renderers, smart-ctor `Result` on every leaf, the Module aggregate (post-eject a schema has schemas, not espaces), extended-property carriage at every level, the separate readback type (the quotient is `SqlType.coarsen`, applied at comparison; the two types are not fused) | 900 |
| `kernel/Identity.fs` | `Core/Identity.fs`, `Coordinates.fs`, `Types.fs`, `Twin.Core/Coordinate.fs`, `TwinIdentity.fs` | 1,054 | `V1Mapped`/`DerivedFrom` variants, five near-identical value-object modules; `UuidV5` folds into `Key.ofName`; identifier-budget hashing moves to `Emit` | 250 |
| `kernel/SqlType.fs` | `SqlStorageType.fs`, `SqlLiteral.fs`, `SqlIdentifier.fs`, `PrimitiveType.fs`, `SqlTypeCorrespondence.fs`, `RawValueCodec.fs` | 956 | correspondence prose; keep the tables, the typed literal, the codec boundary | 700 |
| `kernel/Evidence.fs` | `Profile.fs`, `ProfileDerivation.fs`, `EvidenceCache.fs`, `SamplingPolicy.fs`, `Statistics.fs`, `Meter.fs` | 2,952 | the user axes (cutover), joint distributions (unbuilt; σ needs marginals), FK selectivity, the god-file derivation | 1,300 |
| `kernel/Decide.fs` | the four `Strategies/*Rules.fs`, the four tightening passes, `Composition.fs`, `DecisionOverlay.fs`, `ConflictDetector.fs`, `Migration.fs`; v1's `RemediationQueryBuilder.cs` | 3,047 | every renderer, `fanOut`, the pass/strategy split, the categorical-uniqueness strategy; keep the tables, the inexpressible-`ALTER` refusal, and the three-option remediation SQL (§12) | 900 |
| `kernel/Delta.fs` | `CatalogDiff.fs`, `ChangeManifest.fs` | 1,195 | episode coordinates, the norm, tolerance residuals; keep the nine facets, rename detection, and the changelog shape | 700 |
| `kernel/Order.fs` | `TopologicalOrder.fs`, `Passes/TopologicalOrderPass.fs`, `Strategies/CycleResolution.fs` | 2,253 | v7's exact minimum-feedback-set solver, certificates, the condensation carrier, cascade-shock zones; keep Kahn + Tarjan + `CyclePolicy` | 700 |
| `kernel/Statement.fs` | `Targets.SSDT/Statement.fs` | 443 | nothing | 450 |
| `kernel/Synth.fs` | `SyntheticData.fs`, `SyntheticCorrection.fs`, `SyntheticVolume.fs`, `Centrality.fs` + its pass, `Twin.Core/Evidence.fs`, `DerivedEvidence.fs` | 2,112 | the ranking's manifest reporting; nothing else material (σ and the shape tier are the substrate) | 1,500 |
| `kernel/Record.fs` | new; `THE_RECORD_FORMS.md` as a type | — | — | 300 |
| **kernel total** | | **~17,300** | | **~7,700 (≤ 8,000)** |
| `io/Render.fs` | `ScriptDomBuild.fs`, `ScriptDomGenerate.fs`, `Render.fs`, `ConstraintFormatter.fs` | 4,006 | the v1-shape post-processor (~100 lines of house style stay); the builders are split by statement family and kept whole | 2,700 |
| `io/Emit.fs` | `SsdtDdlEmitter.fs`, `SsdtBundle.fs`, `PostDeployEmitter.fs`, `BatchSplitter.fs`, `IndexNaming.fs`, `DataStatementArgs.fs`, `RefactorLogEmitter.fs` + `RefactorLogRender.fs`, the five kept `Targets.Data` files, `SchemaMigrationEmitter.fs` | 4,576 | `ManifestEmitter` (1,080), `SqlprojEmitter`, `ApplyRunbookEmitter`, `DataEmissionComposer`, `MigrationDependenciesEmitter`, `BootstrapEmitter`, `CsvExport`, `DistributionsEmitter`; the migration emitter shrinks to the expectation `prove` prints beside DacFx's script | 3,000 |
| `io/Json.fs` | `CatalogCodec.fs`, `ProfileCodec.fs`, `JsonCodecKernel.fs`, `GoldenCodec.fs`, `JsonEmitter.fs` | 1,929 | 932 lines of hand-written pairs → a derived codec with two escape hatches | 700 |
| `io/Ssdt.fs` | new (DacFx `TSqlModel` read of a project or a dacpac); `DacpacEmitter.fs` | 271 | — | 600 |
| `io/SqlServer.fs` | `ReadSide.fs`, `LiveProfiler.fs`, `EvidenceFingerprint.fs`, `ServerDigest.fs`, `DataIntegrityChecker.fs`, `AsyncStream.fs`, `ConnectionResolver.fs`, `SqlPolicy.fs`, `Retry.fs` | 3,573 | fifteen hand-rolled drain loops → one `readRows`; the `Static` marking trap; three connection-spec parsers → one | 2,300 |
| `io/Publish.fs` | `Deploy.fs`, `DeployParallelism/Feasibility/ConnectionString.fs`, `Preflight.fs` (the CDC gate), `RowFidelity.fs`, `CanaryResidual.fs`, `Tolerance.fs` | 3,374 | parallelism ladders, migrate-era preflight, the tolerance set-wrapper (a named-divergence list remains in law 2's test) | 1,500 |
| `io/Twin.fs` | `Twin.Core/*` (config, scenarios, fingerprint, estate definition), `Twin.Runtime/*`, `Pipeline/Bulk.fs`, `FakerRealization.fs`, `DockerImageEmitter.fs`, `Twin.Cli/Render.fs`; `scripts/bake.mjs` (251 lines of JavaScript) | 4,464 | two of three substrate stand-up paths; Voice rendering; the config surface trimmed to one database | 3,300 |
| `io/Ossys.fs` **[optional, §16]** | `MetadataSnapshotRunner.fs`, `OssysRowsetReader.fs`, `OssysTranslation.fs`, `OssysRowsetTypes.fs`, `MetadataExtractionError.fs`, `MetadataContractOverrides.fs`, `OssysTypeMapping.fs`; the rowset SQL (1,253 lines, kept verbatim as a resource); v1's supplemental Users model (341 lines of JSON) | 4,393 | the JSON reader (`OssysJsonReader.fs`, 801: its only job was v1's output); the 22 row-handler closures collapse to a table where the rowset shape allows (about 1,400 of the runner's 1,722 lines are those handlers, and most of that is irreducible) | 3,600 |
| `io/Move.fs` **[cutover wing, frozen]** | `TransferRun.fs`, the nine `Transfer*.fs`, `PeerTransfer.fs`, `SurrogateCapture`/`PackedSurrogateRemap`/`KeymapSpill`/`CaptureJournal`, `MovementSurface.fs`, `MovementSpec.fs`, `CapabilitySurvey.fs`, `MigrationRun.fs`, `FidelityCompareRun.fs`, `Cli/Faces/Transfer.fs` | ~15,400 | not ported. The reverse leg is built, canary-gated, and has not run; the engine stays buildable in `archive/v2` until the leg runs or is cancelled (§16). If step 8 (§14.2) happens, the port is ≤ 3,000 lines | 0 (≤ 3,000) |
| **io total** | | **~22,200 (+4,400 optional, +15,400 frozen)** | | **~14,100 (≤ 15,000; +3,600 optional)** |
| `cli/` | `Projection.Cli/*` (14,097), `Twin.Cli/Program.fs` (225), `Pipeline/Config.fs` (2,189) | 16,511 | `Voice.fs`, `View.fs`, `Watch.fs`, `Navigator`, `TtyRenderer`, the boards, 30+ plan actions, the 60-key config; keep `CliExit.fs` (73), the delta rendering (~200), the dispatcher (~200), the Twin verbs (~350) | 1,500 |
| dependency laws | `Projection.Analyzers` (142) | 142 | — | 150 |
| **code total** | | **~60,500 named (of 118,686)** | | **~23,500, call it 24,000 (≤ 25,000); ~27,500 with `io/Ossys`; ≤ 30,000 with `io/Move`** |

Three readings of that table.

First, the ~43,000 lines of v2 that appear in neither column (neither a descendant nor the frozen
wing) are the deletions with no replacement: `Pipeline.fs` (3,414) and its `Compose.run*`
family, `Estate.fs` and its five companions (~4,100, replaced by one `check environments`
table), the four advisory passes with their tuning and `BoundedContext` (~750), `Episode`/
`Lifecycle`/`Ledger`/`ApprovalWorkflow`/`ActConsent` (~1,300), the twelve `*Binding` modules
(2,208) and four `*Seam` modules (871), `RunSpine`/`Run*`/`LogSink`/`EventProjection` (~2,600),
`GoBoard`/`TransferImpact`/`TransferTriage`/`Capability*` (~1,300), `BridgeRetarget*`/
`BridgeRowStaging*`/`BridgeStagingCache` (~2,300), `Slice*` (~350), `Fidelity*`/`ProofManifest`
(~1,500), `Lineage`/`LineageBuffer`/`Diagnostics`/`Message`/`StructuredString`/`Optics`/
`Fixpoint`/`PassChainAdapter`/`ComposeState`/`TransformRegistry`/`RegisteredTransforms`/
`StrategyRegistrations`/`Classification`/`VersionedPolicy`/`PolicyExpr`/`ModuleFilter`/
`Bench`/`PinnedWriting` (~5,300), and the manifest (1,080). Appendix D has every row.

Second, the budget is three numbers, not one, because one number hides the decisions that set
it. **Kernel ≤ 8,000. io ≤ 15,000, plus ≤ 4,000 if the OSSYS reader survives. cli ≤ 1,500.** The
planning pass summed its own floors to ~28,400 with the reader in and the Twin counted
separately; this table lands at ~24,000 without the reader and ~27,500 with it. The gap between
the two passes is real deletion, not optimism: the readback type folded into a function, the
manifest gone, the migration emitter reduced to a preview, the estate board reduced to a table.
These floors are in F# notation, the language of the specification; §6.6 restates them for the
C# the team will build. The three decisions that move the total are the OSSYS reader (±3,600),
the Move wing (0 or ≤ 3,000), and nothing else; `Render` keeps all 26 statement kinds on purpose, because v3 still
emits (the bundle for the Twin, the seeds, the refactorlog, and the drift report in file terms),
and a statement stream that can express the whole repository is what makes law 3 possible.

Third, the two largest line items are `Emit` and `Render`; with `Twin` they are half of io.
Together with `Ossys` they are the domain: what the platform says, what SQL Server accepts, how
to write it, and how to fill it. That is where the lines should be. The budget is enforced as a
test: `wc -l` per package against this table, failing above the ceiling, so that the next
"just one more pass" is a red build and not a chapter.

### 6.5 Tests, docs, and the two budget lines v2 never drew

| Region | v2 today | v3 budget | How |
|---|---:|---:|---|
| kernel tests | ~60,000 (pure pool share) | 7,500 | property tests on `Decide`, `Delta`, `Order`, `Synth`, `Statement → Render` round trips; the kernel halves of the twelve laws; no per-DU renderer tests because there are no per-DU renderers |
| io tests | 21,742 (Integration) + 6,702 (Twin integration) + ~26,000 (pure pool, adapters and targets) | 8,500 | one SQL Server fixture (Docker or LocalDB, chosen by environment; the CDC class isolated per instance), the read-side round trip, the publish verdicts, the Twin's laws, `Move`'s phase tests while the wing exists. This is the half that proves anything about DacFx; it is not to be crowded out by the pure pool |
| the proven changes | 41 `SamplePr*` facts in 13 classes | 2,500 | the 45 (and the compound) archetypes as rows of data under `tests/Golden/changes/`, run by one parameterised test in the proof lane (§10.3) |
| cli tests | ~5,000 | 1,000 | verb parsing, exit codes, `--json` shape |
| **tests total** | **118,513** | **~19,500 (≤ 20,000)** | 0.8× the kernel, because the two things that need combinatorial testing (the diff and the decision tables) stay and the things that needed tests because the types did not carry the invariant go |
| hand-written docs outside `knowledge/` | 119,384 (v2 root) + ~60,000 (v1: `readme.md`, `docs/`, `handbook/`, `ssdt-playbook/`, `notes/`) | ≤ 7,000 | `README.md` 150; `ARCHITECTURE.md` (this document's successor) ≤ 3,000; `DECISIONS.md` one line per entry; one README per package (≤ 80 × 8); `PORTABILITY.md` (94); the Twin charter and synthetic-data design (619, kept as they are, beside `io/Twin`) |
| `knowledge/` | 21,724 (`ssdt-agent`, markdown) | ≤ 11,000 | 45 ops ≈ 3,400 (about 75 lines each); shared ≈ 750; `authoring.md` + `reviewing.md` + `record.md` ≈ 1,500; `findings.md` ≈ 500; a dozen samples ≈ 950; ledgers ≈ 300; the eight handbook chapters ≈ 2,500 |
| generated surfaces | `.claude/` 1,405 + `copilot-package/` 1,210 + `NORTH_STAR.matrix.generated.md` | unbounded, excluded from every count | `LAWS.md` from tests; the verb reference from `--help`; the config reference from the schema; the Copilot bundle and `.claude/` pointers from `knowledge/`; never hand-edited |
| the fixture corpus | v1: ~26,000 lines of fixture JSON, SQL and `.sqlproj`; v2: `GoldenCatalog.fs` (468) + `proving-ground/` + `twin.scale.json` | ≤ 3,000 | one golden schema (≤ 500 lines of F#), its emitted bundle, the proven changes as data, `twin.scale.json`; v1's three edge-case fixture sets stay in `archive/v1` and serve as the OSSYS oracle (§14.1) only while `io/Ossys` exists |

The archive keeps everything. Nothing is destroyed. The working tree stops carrying it.

### 6.6 The C# encoding

v3 is built in C#, not F#. The team that will maintain it maintains C#; the review §2.3 asks
for is a review of code as well as of records; and an engine nobody on the team can read is
what v2 became. v2's F# is not thrown away. It is the specification v3 is ported from, its
tests are the first thing ported, and the kernel types in §7 are shown in F# because it is the
most compact notation for a type and the one the specification is written in. The encoding into
C# is mechanical. This section gives its rules, so a reader of §7 can write the C# without
asking, and gives the budget in the language that will actually be counted.

**What F# was buying, and the C# form of each.**

| F# benefit the design relies on | Where it matters | C# form |
|---|---|---|
| closed unions with exhaustive matching | `Statement` (26 cases), `NullabilityDecision`, `Reader`, `Shipping`, `CyclePolicy`, the `Refusal` codes, `SqlType` | an abstract record with a private constructor and nested sealed derived records, so nothing outside the file can add a case, plus one `Match` method whose signature takes a delegate per case. Exhaustiveness moves from the compiler's pattern check to the method signature. Hand-written for unions under eight cases; generated (`dunet`, or a forty-line in-repo source generator) for `Statement` |
| immutable records with structural equality and copy-with | every kernel type; laws 1, 6, 7; `Delta.between` | C# records. One trap: record equality over a collection compares the reference, which would silently break law 1 and every delta comparison. The kernel gets one `Seq<T>` (a readonly struct over `ImmutableArray<T>` with element-wise equality and ordinal-sorted construction, about eighty lines), and every kernel list is a `Seq<T>` |
| smart constructors, no nulls | `Key`, `Name`, `Refusal` | `readonly record struct` with a private constructor and static factories returning `Result<T, Refusal>`, an in-repo type of about sixty lines rather than a library. Nullable reference types on, warnings as errors; `Option` is not missed. No LanguageExt or similar: making C# imitate F# defeats the reason for the switch |
| purity by construction; compile-order acyclicity | the kernel; the dependency laws | the kernel project references the BCL and `System.Collections.Immutable` only; `Microsoft.CodeAnalysis.BannedApiAnalyzers` with a `BannedSymbols.txt` forbids `System.IO`, `System.Net`, `DateTime.Now`, `Random`, and `Task` in `kernel/`; a NetArchTest-style test pins the direction between the four projects. C# permits cycles inside a project that F#'s file order does not; the test is the substitute |
| decision tables as pattern matches | `Decide` | `switch` expressions with tuple and property patterns. This ports almost verbatim, and a reviewer who knows SQL and C# can read `Decide.Nullability` against the tightening-class skill, which is what "the record is the product" needs: the engine that produces the record has to be legible to the people who approve records |
| property-based tests | the twelve laws | CsCheck (or FsCheck's C# API); the law names and `LAWS.md` are unchanged |

**Two of §7's types, encoded.** The statement stream as a closed hierarchy:

```csharp
public abstract record Statement
{
    private Statement() { }                                    // the set of cases is closed

    public sealed record CreateTable(Table Table) : Statement;
    public sealed record AddForeignKey(Name Table, Reference Reference) : Statement;
    public sealed record AlterColumn(Name Table, Column Before, Column After) : Statement;
    // ... one nested sealed record per v2 case, twenty-six in all

    public T Match<T>(Func<CreateTable, T> createTable,
                      Func<AddForeignKey, T> addForeignKey,
                      Func<AlterColumn, T> alterColumn /* , ... */) => this switch
    {
        CreateTable s   => createTable(s),
        AddForeignKey s => addForeignKey(s),
        AlterColumn s   => alterColumn(s),
        _               => throw new UnreachableException(),  // provably: the constructor is private
    };
}
```

And §7.5's nullability table, arm for arm:

```csharp
public static NullabilityDecision Nullability(Policy p, Table t, Column c, ColumnEvidence? ev)
{
    if (p.Overrides.KeepsNullable(c.Key)) return new KeepNullable(KeepReason.OperatorOverride);
    if (t.PrimaryKey.Contains(c.Name))    return new EnforceNotNull(NullEvidence.PrimaryKey);
    if (!c.Nullable)                      return new EnforceNotNull(NullEvidence.PhysicallyNotNull);
    if (!c.Mandatory)                     return new KeepNullable(KeepReason.NoSignal);
    return ev switch
    {
        null                                        => new EnforceNotNull(NullEvidence.MandatoryNoProfile),
        { Nulls: 0 }                                => new EnforceNotNull(new MandatoryNoNulls(ev.Rows)),
        var e when e.Nulls <= e.Rows * p.NullBudget => new EnforceNotNull(new MandatoryWithinBudget(e.Nulls, e.Rows, p.NullBudget)),
        var e when p.AllowMandatoryRelaxation       => new KeepNullable(new RelaxedUnderEvidence(e.Nulls, e.Rows, p.NullBudget)),
        var e                                       => new AskOperator(new MandatoryButNullsBeyondBudget(e.Nulls, e.Rows, p.NullBudget)),
    };
}
```

Cases that carry no data (`PrimaryKey`, `NoSignal`) are singletons on their hierarchy; cases
that carry data are nested records. One C# trap replaces an F# one: a `with` expression on a
record copies fields and bypasses any validating factory, so an invariant that spans fields (a
primary key names existing columns; a reference's columns exist on both tables) is checked by
`Schema.Create` and by the laws, never by a setter, and the value types (`Key`, `Name`) expose
no settable field for `with` to reach. The F# in §7.5 and this C# are the same table; the test
that pins it (v1's mode × signal matrix, §12.2) runs against either.

**Where C# is better, not merely equal.** DacFx, ScriptDom, and System.Text.Json are C#
libraries. v2's `ScriptDomBuild` is 2,834 lines of setting properties on ScriptDom objects
through F# mutation syntax; it gets shorter in C#. v2's 932 hand-written codec lines existed
because System.Text.Json could not see F# unions; with `[JsonDerivedType]` on the closed
hierarchies and a source-generated serializer context, `io/Json` roughly halves. `io/Publish`
loses the interop friction around DacFx's progress events. Visual Studio's analyzers run in
the editor the team already uses. And v1's trunk becomes a donor again in the places §12 names:
the SSDT-project lens, the remediation builder, the order validator, the ScriptDom fixtures.

**The budget, in C#.** Records and hierarchies run longer than unions and inference. The
multipliers below are estimates against §6.4's floors: 1.4× for the kernel, 1.15× for io
(interop shrinkage offsets boilerplate), 1.2× for the CLI, 1.25× for tests.

| Package | F# floor (§6.4) | C# budget |
|---|---:|---:|
| `kernel/` | ~7,700 | **≤ 11,000** |
| `io/` without the OSSYS reader | ~14,100 | **≤ 17,000** |
| `io/Ossys` (optional, §16) | ~3,600 | ≤ 4,500 |
| `cli/` | ~1,500 | **≤ 2,000** |
| dependency laws | 150 | 150 |
| **code total** | **~24,000** | **≈ 30,000 (≤ 31,000); ≈ 34,000 with the reader** |
| tests | ~19,500 | ≤ 25,000 |
| docs, knowledge, generated surfaces | as §6.5 | unchanged |

Thirty thousand lines of C# is a quarter of v2's source and under two-fifths of v1's 78,461
lines of C#, for a tool that does what neither did. The budget is enforced the same way (§6.4):
a test over `wc -l` per package that fails above the ceiling.

**No F# island.** It is tempting to keep the proven pieces (the statement builders, σ, the
Twin's check) as F# libraries under a C# kernel. That produces the shape v2 died of: a corner
of the codebase one person understands, which then becomes the corner nobody touches and
everybody routes around. Everything is ported. v2's F# is the specification: step 1 of §14.2
ports its kernel tests first, then the types, then the functions, with the byte-level parity
oracle (§14.1) unchanged because it never cared what language produced the bytes. σ gets one
extra fixture, v2's minted rows at a fixed seed, so a port that drifts is caught by law 11 on
the first run.

**Determinism has to be designed in, because nothing in C# nudges toward it.** F#'s structural
equality and ordered maps gave v2 stable output for free. In C#: every kernel collection is a
`Seq<T>` sorted at construction with ordinal comparers (v2's canonicalize pass, as a
constructor); no emitter iterates a `Dictionary`; no `GetHashCode` of a string reaches an
output; `Random` is banned from the kernel and σ takes its generator as a parameter. Laws 1 and
11 land in step 1, before any emitter exists, so that the first emitter is born under them.

**Toolchain, stated once.** .NET 10, the current LTS (.NET 9 is a standard-term release and
leaves support in November 2026; both prior generations pin 9.0.314 with roll-forward disabled),
with `global.json` pinned to a feature band and `rollForward: latestPatch`; `<Nullable>`
enabled and `<TreatWarningsAsErrors>` on in every project; `<ImplicitUsings>` off in `kernel/`;
the banned-API analyzer in `kernel/` and `io/`; `sealed` by default on classes; records for
aggregates, `readonly record struct` for values; xUnit, CsCheck, and NetArchTest in `tests/`;
`dunet` if the team prefers generated `Match` methods to hand-written ones; no other package in
the kernel. The list is short on purpose and a new dependency is a decision line in
`DECISIONS.md`.

---

## 7. The kernel, in types

This section is the design. The types below are meant to compile; where a v2 type is kept
verbatim, it says so and does not repeat it. Where v3 departs from v2, the departure is
explained in the line after the type. Field counts are deliberate: `Attribute` in v2 has
twenty fields; `Column` here has eleven, and each of the nine removed is accounted for.

The types are written in F# because it is the most compact notation for a type and because
v2's F# is the specification v3 is ported from. v3 itself is C# (§6.6); the encoding of every
type below follows §6.6's rules mechanically, and §6.6 shows two of them worked.

### 7.1 `Identity.fs` — one key, one name

```fsharp
namespace Estate

/// A stable identity that survives renames. Minted once (a GUID) and carried as the
/// extended property `Estate.Key` on the emitted object; when an object carries none,
/// derived deterministically from its qualified name (UUIDv5 in the Estate namespace).
[<Struct>] type Key = private Key of System.Guid
module Key =
    let ofGuid (g: System.Guid) : Key
    let ofName (qualifiedName: string) : Key         // UUIDv5, stable across machines
    let value (Key g) = g

/// A display name. Never an identity. Non-blank, ≤ 128 characters, no control characters.
[<Struct>] type Name = private Name of string
module Name =
    let create : string -> Result<Name, Refusal>
    let value (Name s) = s

/// The one error type of the kernel. Code is dot-separated and stable (it is what a record
/// cites and what a test asserts). Where is a Key when the refusal is about one object.
type Refusal = { Code: string; Message: string; Where: Key option }

/// The one finding type. A finding is not an error: the run continues. Codes are the same
/// namespace as refusals so a record can cite either.
type Severity = Info | Warning | Error
type Finding = { Code: string; Severity: Severity; Subject: Key option; Message: string
                 Evidence: (string * string) list }
```

Departures from v2: `SsKey`'s four variants (`OssysOriginal`, `Synthesized`, `DerivedFrom of
reason`, `V1Mapped`) collapse to one `Key`. Post-eject the repository is the truth, so the only
question identity answers is "is this the same object after a rename?", and one GUID carried
in an extended property answers it (v2 already emits `Projection.SsKey` as an extended
property; v3 renames it and makes it the only mechanism). The `DerivedFrom` reason DU existed
so inverse references and synthesized keys could carry provenance; v3's `Reference` is directed
and inverses are not materialised, so the reason has no consumer. `V1Mapped` was a bridge to v1
and v1 is archived.

`ValidationError` (338 construction sites in v2) becomes `Refusal`; `DiagnosticEntry` becomes
`Finding`; `Lineage<Diagnostics<'a>>` becomes "a function returns its value and its findings as
two fields." There is no writer monad because there is nothing to write except a list.

### 7.2 `SqlType.fs` — what SQL Server stores

Kept from v2 with the renderers removed: `SqlStorageType` (the full storage vocabulary —
`BigInt | Int | SmallInt | TinyInt | Bit | Decimal of p*s | Numeric of p*s | Money | Float |
Real | Date | Time of p | DateTime | DateTime2 of p | DateTimeOffset of p | SmallDateTime |
Char of n | VarChar of n | NChar of n | NVarChar of n | Binary of n | VarBinary of n | Xml |
UniqueIdentifier | SqlVariant | RowVersion`, with `Max` as a length), `SqlLiteral`
(`Int | Decimal | String | Bytes | Bool | Date… | Null | Expression of call`), and
`SqlIdentifier.quote`/`qualified` (byte-verified against `EncodeIdentifier`). The
OutSystems-type to storage-type mapping (`OssysTypeMapping.tryParse`, the 2026-07-18 platform
mapping decisions: email/phone are ANSI `VARCHAR`, date-only stores as `DATETIME`, text to
4000 verbatim) lives here as one table, `SqlType.ofOutSystems`.

Departure: v2 carried both a coarse `PrimitiveType` (ten cases) and `SqlStorageType`, because
the read-side could only recover the coarse type and the canary compared in that quotient. v3
keeps one type and one explicit quotient function, `SqlType.coarsen`, which the canary applies
to both sides before comparing. The quotient is still load-bearing (`CRYSTALLINE_FORM.md`
§3.2); it is now a function rather than a second type family.

### 7.3 `Schema.fs` — a state

```fsharp
type Schema = { Tables: Table list; Sequences: Sequence list; Schemas: Name list }

and Table =
    { Key: Key; Schema: Name; Name: Name
      Folder: string                          // emission folder (an espace name, or an override)
      Columns: Column list                    // in authored order
      PrimaryKey: Name list                   // column names; empty = heap (allowed, named)
      References: Reference list
      Indexes: Index list
      Checks: Check list
      Triggers: Trigger list
      Temporal: Temporal option
      Seed: Row list option                   // Some rows = a static entity; the rows ARE the model
      Description: string option
      Properties: (Name * string) list }      // extended properties other than Estate.Key

and Column =
    { Key: Key; Name: Name
      Type: SqlStorageType
      Nullable: bool                          // what the schema SAYS
      Mandatory: bool                         // what the platform INTENDS (drives tightening)
      Identity: (int64 * int64) option        // seed, increment
      Default: (Name option * SqlLiteral) option   // constraint name (if named), value or expression
      Computed: (string * bool) option        // definition, persisted
      Collation: string option                // only when non-default (v1's rule, kept)
      Description: string option
      Order: int }

and Reference =
    { Key: Key; Name: Name
      Columns: (Name * Name) list             // (own column, referenced column) — composite is a list
      Target: Name * Name                     // schema, table
      OnDelete: Action; OnUpdate: Action
      State: ConstraintState                  // v2's DU, verbatim: NoDbConstraint | Trusted | Untrusted
      IsUser: bool }                          // a user FK (CreatedBy/UpdatedBy): re-keyed per environment

and Action = NoAction | Cascade | SetNull | SetDefault
and Index =
    { Key: Key; Name: Name
      Columns: (Name * Direction) list; Included: Name list
      Uniqueness: Uniqueness                  // v2's DU, verbatim: NotUnique | Unique | PrimaryKey
      Filter: string option
      Options: IndexOptions                   // fill factor, padding, locks, compression, data space; all defaulted
      IsDisabled: bool; IsPlatformAuto: bool }
and Check = { Key: Key; Name: Name; Expression: string; State: ConstraintState }
and Trigger = { Key: Key; Name: Name; Definition: string; IsDisabled: bool }
and Sequence = { Key: Key; Schema: Name; Name: Name; Type: SqlStorageType; Start: int64
                 Increment: int64; Min: int64 option; Max: int64 option; Cycle: bool; Cache: int option }
and Temporal = { HistoryTable: Name * Name; PeriodStart: Name; PeriodEnd: Name
                 Retention: (int * RetentionUnit) option }
and Row = { Cells: (Name * SqlLiteral) list }
```

Departures from v2's `Catalog`, each with its reason:

- **No `Module` aggregate.** v2's `Catalog = { Modules: Module list; Sequences }` with
  `Module = { SsKey; Name; Kinds; IsActive; ExtendedProperties }`. An espace is an OutSystems
  concept; after the eject it survives as a *folder* in the emitted layout and nothing else.
  `Table.Folder` carries it. The OSSYS reader sets it from the espace; the SSDT reader sets it
  from the file path; overrides may rename it.
- **`Kind` becomes `Table`, `Attribute` becomes `Column`.** v2's generic names ("the algebra
  is source-agnostic") bought nothing a second source ever cashed; the tree, the handbook, and
  the team say table and column. So does v3.
- **`Column` loses nine of `Attribute`'s twenty fields.** `Type: PrimitiveType` and
  `SqlStorage: SqlStorageType option` collapse to `Type: SqlStorageType` (§7.2). `Length`,
  `Precision`, `Scale` are facets of the storage type, not separate fields. `IsPrimaryKey`
  moves to `Table.PrimaryKey` (a key is a property of the table). `IsActive` goes: an inactive
  attribute is not emitted, so the reader drops it and reports a finding. `DefaultName` and
  `DefaultValue` merge. `OriginalName` and `ExternalDatabaseType` go: they were v1-parity
  carriage for the metamodel's rename history and external-connector type strings, and neither
  has a consumer once the schema is the repository. `ExtendedProperties` on a column go: the
  only one anyone emitted was the description.
- **`Reference.Columns` is a list.** This is the composite-PK fix (§4.2 g). v2's `Reference`
  had `SourceAttribute: SsKey` and `TargetKind: SsKey`, one leg; v3 carries the pairs.
- **`Seed: Row list option` replaces `ModalityMark.Static` plus a separate static-population
  side table.** A static entity is a table whose rows are part of the model. Making the rows a
  field of the table removes the "ReadSide marks everything Static" trap (survival rule 8) and
  the `NormalizeStaticPopulations` pass (rows are sorted at construction).
- **`Mandatory` stays beside `Nullable`.** This is the one place logical intent lives in the
  schema, because the nullability decision (§7.5) needs both. The reader sets `Mandatory` from
  `ossys_Entity_Attr.Is_Mandatory`; the SSDT reader sets it equal to `not Nullable`.
- **No smart constructor per record.** `Schema.validate : Schema -> Refusal list` runs once at
  the boundary (unique names per scope, PK columns exist, reference targets exist, index columns
  exist, seed cells match columns). Inside the kernel a `Schema` is trusted. v2 ran `Result`
  through every `create`; the cost was 75 constructors and the `{ X.create … with … }` default-
  inheritance trap (survival rule 7).

### 7.4 `Evidence.fs` — what the data says

```fsharp
type Evidence = { Tables: Map<Key, TableEvidence>; CapturedAt: System.DateTimeOffset; Source: string }

and TableEvidence =
    { RowCount: int64
      Columns: Map<Name, ColumnEvidence>
      Orphans: Map<Name, OrphanEvidence>        // by reference name
      Duplicates: Map<Name, DuplicateEvidence>  // by index name (declared or candidate)
      Probe: Probe }

and ColumnEvidence =
    { Nulls: int64; Distinct: int64 option; MaxLength: int option
      Distribution: Distribution option; Probe: Probe }
and OrphanEvidence = { Count: int64; Sample: SqlLiteral list; Probe: Probe }
and DuplicateEvidence = { Groups: int64; Sample: SqlLiteral list list; Probe: Probe }
and Distribution =
    | Categorical of (SqlLiteral * int64) list * truncated: bool
    | Numeric of NumericShape                 // min p25 p50 p75 p95 p99 max, as decimal
    | Temporal of NumericShape                // ticks
and Probe = Observed | Sampled of rows: int64 | TimedOut | Skipped of reason: string
```

Kept from v2: the shape of `ColumnProfile`, `ForeignKeyReality`, and the two distributions;
the `Probe` outcome that distinguishes "we looked" from "we did not" (v1 conflated these, and
v2's `ProbeStatus` was its correction); the discover-once/derive-pure factoring (the io side
materialises row streams into aggregates; every field above is a pure function of those
aggregates and the schema).

Departures: v2's `Profile` had twelve top-level collections including `CdcAwareness`,
`SourceUsers`/`TargetUsers`, `ForeignKeyCardinalities`, `ForeignKeySelectivities`, and
`JointDistributions`. The user populations belong to `Move` (the cutover wing) and are read
there. CDC awareness belongs to `Publish` (a CDC-enabled table changes what an `ALTER` may do;
that is a verdict input, not evidence about data). Cardinality, selectivity, and joint
distributions had one consumer each (bridge retarget readiness; the Twin's `perParent`; a
synthesis mode explicitly bounded as "v1 fidelity"); the Twin's `perParent` becomes a scenario
knob. What remains is what the decision tables and σ actually read.

### 7.5 `Decide.fs` — the tightening tables

The three decision functions are v2's, with the renderers removed and the pass/strategy split
collapsed (a decision is one function over one column, reference, or index; the "pass" is a
`List.map`). The tables are reproduced because they are the distilled domain knowledge.

```fsharp
type Policy =
    { NullBudget: decimal                       // fraction of rows; default 0.0
      AllowMandatoryRelaxation: bool            // default false: beyond budget → operator decides
      CreateForeignKeys: bool; AllowNoCheck: bool; AllowCrossSchema: bool
      EnforceSingleUnique: bool; EnforceCompositeUnique: bool; ApplyUniquePromotions: bool
      Overrides: Override list }                // KeepNullable key | Untracked reference | Relax index
    static member Vanilla                       // every flag off: the faithful projection

type NullabilityDecision =
    | EnforceNotNull of NullEvidence            // PrimaryKey | PhysicallyNotNull | MandatoryNoProfile
                                                // | MandatoryNoNulls of rows | MandatoryWithinBudget of nulls*rows*budget
    | KeepNullable of KeepReason                // OperatorOverride | NoSignal | RelaxedUnderEvidence of nulls*rows*budget
    | AskOperator of Conflict                   // MandatoryButNullsBeyondBudget of nulls*rows*budget

let nullability (p: Policy) (t: Table) (c: Column) (ev: ColumnEvidence option) : NullabilityDecision =
    if p.Overrides |> keepsNullable c.Key then KeepNullable OperatorOverride
    elif t.PrimaryKey |> List.contains c.Name then EnforceNotNull PrimaryKey
    elif not c.Nullable then EnforceNotNull PhysicallyNotNull
    elif not c.Mandatory then KeepNullable NoSignal
    else
        match ev with
        | None -> EnforceNotNull MandatoryNoProfile
        | Some e when e.Nulls = 0L -> EnforceNotNull (MandatoryNoNulls rows)
        | Some e when decimal e.Nulls <= decimal rows * p.NullBudget -> EnforceNotNull (MandatoryWithinBudget (e.Nulls, rows, p.NullBudget))
        | Some e when p.AllowMandatoryRelaxation -> KeepNullable (RelaxedUnderEvidence (e.Nulls, rows, p.NullBudget))
        | Some e -> AskOperator (MandatoryButNullsBeyondBudget (e.Nulls, rows, p.NullBudget))
```

`foreignKey` keeps v2's order of gates exactly (operator-untracked first, then missing target,
then a real constraint present on the source, then policy disabled, then the evidence-missing
branch with its `AllowNoCheck` and cross-schema arms, then orphans-with-`NoCheck` as
success-with-caveat, then orphans-without as `DoNotEnforce (DataHasOrphans n)`, then the clean
case). Its eight keep-reasons and five evidence variants are v2's. One v2 arm is deleted:
`DeleteRuleIgnored`, whose predicate was hardcoded to `false`.

`unique` keeps v2's binary outcome and the `PromotionAdvisedNotApplied` arm, which surfaces a
candidate without acting on it unless `ApplyUniquePromotions` is on. `CategoricalUniqueness`
(a distribution-aware uniqueness strategy with one consumer) is deleted; if a categorical
column with no duplicates is a promotion candidate, the `unique` table already says so.

```fsharp
type Decisions =
    { Nullability: (Key * NullabilityDecision) list
      ForeignKeys: (Key * ForeignKeyDecision) list
      Unique:      (Key * UniqueDecision) list
      Findings: Finding list }
let decide : Policy -> Schema -> Evidence -> Decisions          // pure; runs in milliseconds
let apply  : Decisions -> Schema -> Schema                       // rewrites Nullable / State / Uniqueness
```

There is no `Classification` (`DataIntent | OperatorIntent of OverlayAxis`) and no transform
registry. The property those existed to prove — that the vanilla projection is byte-identical
and every opinion is named — is one test: `decide Policy.Vanilla s e` yields empty decision lists
and `apply` is the identity.

### 7.6 `Delta.fs` — a change

```fsharp
type ChannelDiff<'change> =
    { Added: Key list; Removed: Key list; Renamed: (Key * Name * Name) list; Changed: 'change list }

type ColumnFacet = Type | Nullability | Identity | Default | Computed | Collation | Order
type ColumnChange = { Key: Key; Facets: Set<ColumnFacet> }        // non-empty by construction
type ReferenceChange = { Key: Key; Facets: Set<ReferenceFacet> }  // Columns | Target | Actions | State
type IndexChange = { Key: Key; Facets: Set<IndexFacet> }

type TableDelta =
    { Key: Key
      Columns: ChannelDiff<ColumnChange>; References: ChannelDiff<ReferenceChange>
      Indexes: ChannelDiff<IndexChange>; Checks: ChannelDiff<Key>; Triggers: ChannelDiff<Key>
      PrimaryKey: (Name list * Name list) option; Seed: SeedDelta option }
type Delta =
    { Tables: ChannelDiff<TableDelta>; Sequences: ChannelDiff<Key>
      Renames: (Key * (Name * Name) * (Name * Name)) list }       // the refactorlog, derived

let between : from: Schema -> to': Schema -> Delta
let inverse : Delta -> Delta option        // Some when every change has a lossless inverse; the rollback section
let isEmpty : Delta -> bool
let dataLoss : Delta -> DataLossStep list  // narrow · drop column · drop table · NOT NULL on populated · lossy retype
```

This is v2's `ChannelDiff<'change>` (the collapse `CRYSTALLINE_FORM.md` recommended and v2
shipped), with the episode coordinates, the schema norm, the tolerance residual, and the
manifest series removed. Two functions are new and are the reason `Delta` is central: `inverse`
computes the rollback where one exists (a widening is the inverse of a narrowing; an added
nullable column's inverse is a drop, which is lossless only when the column is still empty),
and `dataLoss` names the steps DacFx's guard will fire on, so the *predictor* can say "this
will block on a populated table" before the *prover* confirms it. A rename is matched by `Key`
when both sides carry keys and by the refactorlog otherwise; `Renames` is the refactorlog's
content, derived, never authored.

### 7.7 `Order.fs` — load order with an explicit cycle policy

```fsharp
type CyclePolicy =
    | Refuse                                   // default for schema: a cycle is a finding, not a fallback
    | DeferNullable                            // data lanes: phase-1 insert with the nullable leg NULL, phase-2 update
    | Manual of (Name * Name * int) list       // v1's allowlist: explicit positions

type Order = { Tables: Key list; Deferred: (Key * Name) list; Cycles: Key list list }
let order : CyclePolicy -> Schema -> Result<Order, Refusal>
```

Kahn plus Tarjan, ~400 lines. v1's 2,188-line sorter with automatic two-node and multi-node
peeling, scoring, and alphabetical fallback, and v2's 977-line v7 pass with the exact
minimum-feedback-set solver, certificates, and condensation carrier, both existed to make a
cycle *go away*. v3 makes a cycle *visible* and lets the policy say what to do. The 2026-07-18
partial-order fix (an unresolvable cycle no longer degrades the whole catalog) is preserved by
construction: `Cycles` is per component and `Tables` orders everything outside them.

### 7.8 `Statement.fs` — kept verbatim

v2's 26-case `Statement` DU (`Targets.SSDT/Statement.fs`) moves into the kernel unchanged, with
its `ColumnDef`/`PrimaryKeyDef`/`ForeignKeyDef`/`IndexDef`/`CellValue`/`MergeBuildArgs`/
`UpdateBuildArgs` companions. It is the contract between the kernel and `Render.fs`. One
addition: `AlterTableAlterColumn` gains a `guarded: bool` so the emitter can mark a statement
the publish guard will refuse on a populated table, which the record cites.

### 7.9 `Synth.fs` — σ, kept

v2's `SyntheticData.generate` with its constraint hierarchy (PK/identity first, FKs drawn from
already-minted parents so orphans are impossible, single-column uniques, then distributions),
splitmix64 seeding, `decimal` arithmetic, content-addressing to `(master, table, column, row)`
for S-stability, the hybrid-by-cardinality privacy contract (≤ 50 distinct values preserve,
above that synthesize), and the corrections overlay (PII classification → Faker realization at
the boundary). The Twin's `DerivedEvidence` (a schema-derived floor: email-named columns mint
emails, amount-named numerics are non-negative, status-named text gets a small vocabulary)
moves in as `Synth.floor : Schema -> Evidence`.

Kept, too, after a correction: volume weighting by centrality. The PageRank pass was filed with
the advisory analytics, but `SyntheticVolume.byCentrality` is its consumer, and `Twin.Runtime/Mint.fs`
and `Pipeline/SyntheticLoadRun.fs` call it to decide how many rows each table is minted with.
The pass's manifest reporting goes; the ranking (~90 lines) stays as `Synth.volumes : Schema ->
RowTiers -> Volume`, with the row tiers from the ledger overriding the rank where they exist.

### 7.10 `Record.fs` — the product as a type

```fsharp
type Record =
    { Verdict: string                               // one line
      Intent: string
      WhatChanges: Delta
      BeforePromoting: Check list                   // per environment
      TheData: Fact list                            // counts and offending rows, from Evidence; for a reconcile: rows before, rows after, who approved
      HowItShips: Shipping                          // OneRelease | OneReleaseRelaxed | TwoRelease of pre: string * note: string | Refused of Refusal
      WhatProvingShowed: Attempt list               // tried / did / realized, each with the verbatim Msg
      AfterDeploy: Query list
      Rollback: RollbackNote                        // inverse delta rendered, plus "not auto-undone"
      NotChecked: string list }
let render : Record -> string                       // Markdown, agentless register, one section per field
```

A field with nothing real renders as one honest line; it is never dropped and never padded
(`THE_DECISION_TREE.md`'s rule). The nine register rules of `THE_RECORD.md` §2 become the
render function's tests: no first or second person, the finding on top and the proof beneath,
the true verb, numbers as numbers.

### 7.11 What the kernel does not contain, and why

| Not in the kernel | v2 lines | Why |
|---|---:|---|
| `Lineage`, `LineageBuffer`, `Diagnostics` writer, `Message`, `StructuredString` | ~1,400 | a function returns findings as a list |
| `TransformRegistry`, `RegisteredTransforms`, `StrategyRegistrations`, `PassChainAdapter`, `ComposeState`, `Classification` | ~1,300 | the chain is a list of functions; completeness is the compiler |
| `Episode`, `Lifecycle`, `ChangeManifest`, `Ledger` | ~850 | git is the timeline; the refactorlog is derived from `Delta`; the operations ledger is a file in `knowledge/ledgers/` |
| `ApprovalWorkflow`, `ActConsent`, `ApprovedDataCorrections`, `DataCorrectionReceipt`, `WriteSignoff` | ~1,300 | approval is the pull request; data corrections are a pre-deploy script the record names |
| `EstateFinding` (20+ kinds, four lanes, five planes), `Reconciliation`, `Tolerance` as a set-wrapper type | ~1,400 | `check` verbs return `Finding` lists; tolerance is a `SqlType.coarsen` plus a short list of named divergences in the canary test |
| `SchemaComplexity`, `ProfileAnomaly`, `QueryHint`, cascade-shock, `AdvisoryTuning` | ~550 | only consumer was the manifest |
| `BoundedContext` (discovery + pass) | ~200 | its one non-manifest consumer is a default-off FK-clustering flag that is byte-identical when off |
| `CatalogRisk`, `JointDependencyDiagnostics`, `FkSelectivityDiagnostics`, `InactiveAttributeDiagnostics` | ~300 | advisory scoring with no acting consumer; `IsActive` is an OSSYS concept |
| `Optics`, `Fixpoint`, `Bench`, `Meter`, `PinnedWriting`, `UuidV5` (folded into `Key.ofName`) | ~750 | no consumer once the passes are functions |
| `Policy` axes `Selection` (dormant, unwired), `Emission` (moves to the emitter's options), `Insertion`, `UserMatching` (moves to `Move`), `BridgeRetarget` | ~1,000 | one `Policy` record with nine fields is the tightening policy; emission options are the emitter's |
| `SliceSpec`, `Closure`, `DataLoadPlan`, `BridgeRowDelta`, `BridgeRetarget`, `SurrogateRemap`, `UserRemap`, `UserIdentity`, `RowFidelity`, `RawValueCodec`, `CanaryResidual`, `SyntheticVolume` | ~3,300 | `Move` (the cutover wing) keeps a reduced `SurrogateRemap`/`UserRemap`; the rest had one consumer that is deleted or is the Twin's scenario knob |

---

## 8. The verbs, end to end

One executable, `estate`. Thirteen verbs. Every verb is `parse → io → render`, prints Markdown by
default and JSON with `--json`, and exits with one of ten codes shared across all verbs:

| Exit | Meaning |
|---:|---|
| 0 | done |
| 1 | bad arguments |
| 2 | could not parse an input (schema, config, project) |
| 3 | **blocked** — the publish guard refused; this is a finding, not a failure (`prove` only) |
| 4 | target unreachable (SQL Server, Docker, LocalDB) |
| 5 | divergence found (`check`, `diff --fail-on-change`) |
| 6 | configuration refused (unknown key, credential inline, toolchain pin mismatch) |
| 7 | build failed |
| 9 | refused by name (a `Refusal` the kernel raised; the code is printed) |
| 130 | interrupted (Ctrl-C or `--timeout`); the cleanup ran and the state is as before |

These are the `ssdt-agent` tree's `prove.mjs` codes and the Twin's codes, merged; v2's
`projection` CLI used a different nine and its `--json` had a `View` AST. There is one table
now.

### 8.1 `estate read` — a state from anywhere

```
estate read --from ossys:<conn-ref> [--modules A,B] [--include-system] --out schema.json
estate read --from sql:<conn-ref>   [--schemas dbo]                        --out schema.json
estate read --from ssdt:<dir-or-sqlproj>                                   --out schema.json
estate read --from dacpac:<file>                                           --out schema.json
```

1. Resolve the connection reference (`env:VAR`, `file:path`; never a literal in config; v2's
   D9 discipline kept).
2. Dispatch on the source:
   - **ossys**: run `outsystems_metadata_rowsets.sql` (v1's script, byte-identical), read the 22
     rowsets, lift to `Schema` (`Ossys.read`). Folder = espace name. `Mandatory` from the
     metamodel. Keys minted from the metamodel's attribute/entity identities via `Key.ofName`
     on `espace.entity` and `espace.entity.attribute` (stable across environments, which is what
     the cutover needs and what v2's `OssysOriginal` GUIDs gave).
   - **sql**: `INFORMATION_SCHEMA` and `sys.*` (v2's `ReadSide`, reduced to one `readRows`
     kernel); `Key` from the `Estate.Key` extended property when present, else `Key.ofName`.
   - **ssdt**: DacFx `TSqlModel` over the project's files; the same lift as `sql`, from the
     model's objects rather than a live catalog; `Folder` from the file path.
   - **dacpac**: `TSqlModel` from the package.
3. `Schema.validate`; refusals exit 2 with their codes.
4. Write `schema.json` (one codec, round-trippable, tested by property).

v2 had `Adapters.Osm` (JSON and rowset paths), `Adapters.OssysSql`, `Adapters.Sql/ReadSide`,
and the Twin's `EstateModel` (DacFx). Four readers, three of which produced `Catalog` through
different lifts. v3 has one `Schema` and four sources behind one verb.

### 8.2 `estate profile` — what the data says

```
estate profile --from sql:<conn-ref> --schema schema.json [--sample 100000] [--tables dbo.Customer,...] --out evidence.json
```

1. For every table in the schema, one batched probe: `COUNT_BIG(*)`, per-column `SUM(CASE WHEN
   col IS NULL …)`, `MAX(LEN(col))` for text, distinct counts where cheap; sampled above the
   threshold (v1's `TableSamplingPolicy`, v2's `SamplingPolicy`), with `Probe` recording which.
2. For every reference not backed by a trusted constraint: an orphan count and a sample.
3. For every declared or candidate unique index: a duplicate-group count and a sample.
4. Distributions (categorical top-N with a truncation flag; numeric and temporal percentiles)
   only when `--distributions` is given, because they are what σ needs and what a privacy
   review must see.
5. A fingerprint per table (`COUNT_BIG`, `MAX(pk)`, `CHECKSUM_AGG(BINARY_CHECKSUM(…))`), v2's
   one-batch staleness probe, so `check` can tell whether evidence is stale.
6. Write `evidence.json`. The literal-free *shape tier* (counts, null rates, lengths, no values)
   is what may be committed; the rich tier stays out of the repository (Twin law 3, kept).

### 8.3 `estate decide` and `estate classify` — the predictor

```
estate decide   --schema schema.json --evidence evidence.json [--policy policy.json] --out decisions.json
estate classify --from <schema A> --to <schema B> --evidence evidence.json [--op make-mandatory]
```

Both pure; both run in milliseconds; neither touches a database.

`decide` is `Decide.decide` over the schema and the evidence; it prints the decisions as a
table with their evidence and reasons; `AskOperator` outcomes exit 0 with a finding, because a
prediction is not a gate. `--apply` writes the tightened schema (`Decide.apply`). This is v1's
`analyze` and v2's tightening passes as one verb.

`classify` is the thing a Copilot session needs in its first thirty seconds and the thing the
tree's `classify-mechanism` skill does by hand: given the change (`Delta.between`) and the
evidence, name the *provisional* shipping shape and approval weight before anything is
published. It reads the data-loss steps (`Delta.dataLoss`), the row counts of the affected
tables (populated or empty), whether existing rows violate the new rule (nulls, orphans,
duplicates, over-length), and the op's flip conditions from `knowledge/ops/<op>.md` when
`--op` names one, and prints: *provisional: two releases (populated table, row-presence
guard); the lead weighs: existing data affected; first time on this estate; prove to confirm.*
The output is marked provisional in its first word. Only `prove` may drop that word.

Two findings `classify` raises that no publish can: **a rename without its refactorlog entry**
(the delta shows a `Renamed` pair, or a `Removed`+`Added` pair of the same type at the same
ordinal, and `<Project>.refactorlog` has no entry for it; without the entry DacFx drops and
re-adds the column, every value is lost, and the publish is green), and **a column-list change
on a CDC-tracked table** (`knowledge/ledgers/cdc-tracked.md` names the tables; a capture
instance is bound to the column list it was enabled with, so an added column is not captured
and a rebuild leaves the instance pointing at the old object). Both are `Block` findings with
the remedy named: write the entry (`estate emit --refactorlog` derives it from the delta); add
the capture-instance step to the pre-deploy script.

### 8.4 `estate emit` — the bundle

```
estate emit --schema schema.json [--decisions decisions.json] [--renames refactor.json] --out <dir>
```

1. `Decide.apply` if decisions are given (a vanilla emit is the faithful projection; tested).
2. `Order.order Refuse` for the file manifest and `Order.order DeferNullable` for the seed lanes.
3. Per table: `CreateTable` (columns, PK, inline FKs for trusted references, checks, temporal),
   `CreateIndex` per non-PK index, `SetExtendedProperty` for `Estate.Key` and descriptions,
   post-CREATE `AlterTableNoCheckConstraint` for untrusted references, `CreateTrigger`;
   rendered by `Render` through ScriptDom into `Modules/<Folder>/<Schema>.<Table>.sql`.
4. Per schema: `Schemas/<name>.sql`. Per sequence: `Sequences/<schema>.<name>.sql`.
5. Seeds: one `MERGE` per static table (v2's typed `MergeStatement`, v1's idempotent shape:
   explicit IDs, `WHEN MATCHED AND value-differs`, null-safe), phase-1/phase-2 for deferred
   nullable FK legs, under `Data/Seeds/<Folder>/<table>.sql`, included from
   `Script.PostDeployment.sql`.
6. `<Project>.refactorlog` from `Delta.Renames` when `--renames` or a prior schema is given,
   and the record's "after deploy" queries as `Verify/*.sql`. A `.sqlproj` is written only by
   `emit --init` for a project that does not exist yet, in the classic format (the one the
   team's Visual Studio opens; `ADOPTION.md` records that the 2026 build carries only the
   classic format), and an existing project's format is never changed: the format is a row in
   `ledgers/toolchain.md` beside the DacFx pin. There is no manifest and no runbook; the
   record's sections carry what those files used to.

The bundle *is* the estate repository's layout. `estate emit` against the current
repository's own `read --from ssdt` must be a no-op (byte-identical), which is the law
"emit ∘ read = id" in §13.

### 8.5 `estate diff` — the change

```
estate diff --from <schema.json | ssdt:… | sql:…> --to <…> [--renames refactor.json] [--fail-on-change]
```

`Delta.between`, rendered as Markdown: added, removed, renamed, changed per channel, with
facets named; `dataLoss` steps listed under a heading the record reuses verbatim; `inverse`
rendered as the rollback where it exists. `--json` gives the `Delta`. Exit 5 with
`--fail-on-change` when the delta is non-empty (the drift check in CI).

`--only columns,keys,indexes,references,checks,properties` scopes the comparison, which is v1's
`DmmComparisonFeatures` (14 lines) carried over: a drift report that can ignore extended
properties is the difference between a board people read and one they mute.

### 8.6 `estate prove` — the verdict

```
estate prove --project <sqlproj> | --bundle <dir>
             --target sql:<conn-ref> | twin
             [--permissive] [--script-only] [--baseline <dacpac>] --out verdict.json
```

This is `prove.mjs` as a verb, with the Twin as the default target.

1. Build the project to a dacpac (`dotnet build` for SDK-style; MSBuild for classic when
   available; exit 7 with the build log otherwise).
2. Ensure the target: `twin` means "the Twin is up and current" (`Twin.up` is called; a stale
   or absent Twin is converged first); a `sql:` target is used as given. A disposable copy is
   always a *copy*: `prove` never publishes to a named environment.
3. `DacServices.GenerateDeployScript` under the **Strict** profile (`BlockOnPossibleDataLoss=true`,
   `DropObjectsNotInSource=false` as the estate pipeline runs it; the profile is data, in
   `ci/profiles/`, and the record cites which one). Parse the script for the guards
   (`IF EXISTS (SELECT TOP 1 1 FROM …) RAISERROR`) and the data-loss steps.
4. `DacServices.Deploy` under Strict. Capture the outcome: published, or blocked with the
   verbatim `Msg`, or failed.
5. If blocked and `--permissive`: publish again under the Permissive profile to observe the
   consequence (rows lost, column widths, trust state), on the same disposable copy, then reset
   it. This is the tree's two-profile discipline, kept.
6. Re-read the schema (`read --from sql`) and re-validate constraint trust (`is_not_trusted`)
   so a foreign key that landed untrusted is a finding, not a silent success (the F5/F9 lesson
   and the DacFx-version risk, made mechanical).
7. Publish once more unchanged; a non-empty second script is a finding ("not idempotent").
8. Write `verdict.json`:

```json
{ "outcome": "blocked", "msg": "Msg 50000 … Rows were detected. The schema update is terminating because data loss might occur.",
  "guards": [{ "table": "dbo.Customer", "statement": "ALTER TABLE … ALTER COLUMN Email NVARCHAR(256) NOT NULL", "kind": "row-presence" }],
  "dataLoss": ["narrow dbo.Customer.Email"], "trust": [{ "fk": "FK_Order_Customer", "trusted": true }],
  "idempotent": true, "engine": { "dacfx": "162.5.57", "sqlpackage": null }, "profile": "Strict",
  "script": "bin/prove/delta.sql", "elapsedMs": 4210 }
```

Exit 0 published, 3 blocked, 4 unreachable, 6 config, 7 build, 9 indeterminate. A Copilot
session reads one integer and one JSON object.

What the substrate cannot prove, the verdict says. LocalDB has no Change Data Capture, no
Agent, and a 10 GB ceiling, so a proof over a table named in `ledgers/cdc-tracked.md`, or the
scale lane above a million rows, needs a full instance (Docker or a developer-edition
install). On LocalDB those proofs return exit 4 with the reason, never a vacuous 0.

### 8.7 `estate twin` — the substrate

```
estate twin up [--scenario s]   estate twin seed   estate twin status   estate twin check
estate twin bake --out <artifact>   estate twin restore <artifact>
estate twin evidence import --from sql:<conn> --out evidence.rich.json
estate twin evidence derive --rich evidence.rich.json --out evidence.shape.json
estate twin evidence verify --schema schema.json --evidence evidence.shape.json
estate twin down | reset
```

The Twin's verbs, unchanged in behaviour (`THE_TWIN.md` §7), reading the same `schema.json`
and `evidence.json` as everything else instead of its own `twin.json` evidence section. `up`:
fingerprints match → nothing to do; else ensure the container (Docker) or LocalDB instance
(Windows), build the model, publish with drop-not-in-source, clean slate, apply seeds, read
back, mint (`Synth.mint`), load, revalidate every check and foreign key (`WITH CHECK CHECK`;
refuse by constraint name on violation), write fingerprints to `[twin].[__state]`. `bake`
exports the converged database as a fingerprint-versioned `.bacpac` (and a container image
where Docker exists); `restore` pulls it. Five laws, five tests (§13).

### 8.8 `estate check` — watching

```
estate check drift        --schema schema.json --target sql:<conn>          # emitted vs deployed
estate check environments --schema schema.json --targets dev,qa,uat          # N deployed vs the model
estate check outsystems   --schema schema.json --from ossys:<conn>           # what the platform believes
estate check evidence     --schema schema.json --evidence evidence.json --target sql:<conn>   # stale?
```

Each is `read` both sides, `Delta.between`, findings; exit 5 on divergence. `environments`
prints one table per divergence with a column per environment. That is the whole of v2's
2,633-line estate board: the lanes (decide/repair/relax/watch), the verdict (unified/
converging/forked), the burndown, the streak, the posture, the remediation SQL blocks, and the
evidence store are gone. A divergence is a row; what to do about it is the record's job.
`check outsystems` is new and cheap: after the eject the platform still holds a logical model
of the external entities, and a developer who refreshes Integration Studio against a table
whose shape the repository changed should be told before the platform tells them. It is also
the only check that can see the failure class the publish guard cannot: an attribute the
extension still maps that the repository no longer declares fails at runtime, not at deploy.
`check outsystems` is the reason the OSSYS reader survives at all (§16); if the reader is
dropped, this verb goes with it and workflow 5 (§2.4) loses its third axis.

### 8.9 `estate record` — the pull request body

```
estate record --intent "make Email required" --delta delta.json --evidence evidence.json --verdict verdict.json [--verdict-permissive …] --out PR.md
```

`Record.render` over the inputs: verdict from the delta and the verdict (`OneRelease` when the
delta has no data-loss step and the publish was clean; `TwoRelease` when it blocked on a
row-presence guard; `Refused` when no safe shape exists); "the data" from evidence; "how it
ships" with the pre-deploy shape the op skill prescribes; "what proving showed" as
tried/did/realized with the verbatim `Msg`; "after deploy" from the bundle's `Verify/`;
rollback from `Delta.inverse`; "not checked" from what the run could not see (other
environments, application code, production scale). The record is what the agent pastes into
the pull request and what the PR gate re-generates and compares.

### 8.10 `estate gate` — the release-grain check

```
estate gate --project <sqlproj> --base <git-ref> [--substrate <artifact>] [--pr-body PR.md] --out gate.json
```

The pull-request gate as one verb, so the CI lane is four lines and a developer can run the
same check locally before pushing. In order:

1. `check inflight`: the tables this pull request reshapes (from `diff --from <base> --to HEAD`)
   against `knowledge/ledgers/in-flight.md`; a collision with an open window is exit 9 with the
   row printed.
2. `twin restore` the named substrate artifact (or `twin up` when none is named).
3. `prove --project … --target twin` on the pull request's *combined* delta, Strict.
4. `record` regenerated from the verdict, the delta, and the evidence; if `--pr-body` is given,
   its "how it ships" and "what proving showed" sections are compared to the regenerated ones,
   and a mismatch is a finding (the machine checks the shape; the human reads the rest).
5. `changelog.json`: the delta between base and head as data (tables and columns added,
   removed, renamed with their refactorlog keys, and retyped), the release tag, and the open
   lag windows that touch it. This is the contract the SSIS team maps against each sprint;
   v2 owned it as `ChangeManifest`, and the gate is where it is produced now.
6. `gate.json` with the verdict, the collision check, and the record diff; exit 0 clean, 3
   blocked (the pull request must carry its two-release shape), 9 collision, else tooling.

### 8.11 `estate move` — the cutover wing

```
estate move --from sql:<conn> --to sql:<conn> --schema schema.json [--rekey users.csv] [--tables …] [--resume] [--revert] [--go]
```

Kept while the cutover and its reverse leg exist, then retired. Ingest, plan (surrogate
remap where the sink mints keys; user re-key from a CSV map), phase-1 bulk insert in
`Order.order DeferNullable` order with deferred legs NULL, phase-2 `UPDATE`, verify counts and
row hashes, exit 9 fail-loud on dropped rows unless `--allow-drops`. Resume is a checkpoint
table on the sink; revert is a delete of captured keys. What is *not* kept from v2's 3,402-line
`TransferRun`: streaming-versus-materialized selection, capability descent ladders, keymap
spill to temp tables, bridge-row staging, consent fingerprints, per-act signoff, journals as a
second resume mechanism, slices, and the go board. If the reverse leg at 200 million rows
genuinely needs the streaming realization, that decision is §16's, made with numbers from a
real run, not inherited.

### 8.12 `estate doctor` — can this machine do the work?

```
estate doctor [--install] [--json]
```

One line: `READY` or `DEGRADED`, then the SDK, the DacFx pin against `ledgers/toolchain.md`,
the substrate found (Docker or LocalDB, with the SQL Server version), the Twin artifact's
fingerprint and age, and the estate checkout's posture. Every `DEGRADED` item carries its
remedy (`--install` for the SDK and the local tool; `twin up`; `twin restore`). It is the
SessionStart hook's whole body, the first thing every entry file says to run, and the thing a
session quotes before claiming a tool is missing. It sweeps disposable databases and
containers older than a day (the crash-cleanup guarantee) and prints what it swept. The
companion document's §9.2 gives the line's exact shape.

### 8.13 What is gone from the verb surface

v2's `projection` had, per its own `THE_CLI.md`: `<flow> --go`, `check` (eight subcommands),
`diff`, `compare`, `explain` (five), `seal` (two), `report`, `synth-correct`, `inspect`,
`init`, `revert`, `setup`, four `slice-*` verbs, and a deprecated `--watch`; plus the Twin's
ten; plus five scripts. v3's thirteen verbs cover the six post-eject workflows and the cutover wing.
Named flows in config (`projection <flow>`) are gone: a flow was a saved argument list, and a
saved argument list is a shell script or a CI step, which is where v3 puts it.

---

## 9. The knowledge layer

The `ssdt-agent` tree is the part of v2 that was right about the job. v3 keeps its content and
changes its shape, following the August review's Layer 2 ("skills as reasoning, personas as
phases") and going one step further where the verbs make it possible.

### 9.1 What is kept, and where it lives

```
knowledge/
  README.md              the front door: the thesis in one page; how a session proceeds; where things are
  authoring.md           the ONE authoring skill: confirm intent → probe → prove → classify → ship → fork → record → verify
  reviewing.md           the ONE review skill: reproduce → scope → attack → dispose
  record.md              the register (nine rules) and the ten-section template, on one page, with the make-mandatory exemplar
  ops/<op-slug>.md       the 45 operation skills, trimmed of restated mechanics (each ≈ 60–90 lines)
  shared/                the six shared-reasoning skills: tightening-class · constraint-is-a-claim · idempotent-seed
                         · identity-and-refactorlog · multi-phase · when-to-index (+ os-vocabulary, deploy-scripts)
  findings.md            F1–F20 and successors: the proven engine facts, dated, struck-through when overturned
  samples/               a dozen sample records (not 45): one per shape (add-optional, make-mandatory, narrow,
                         create-fk-orphan, delete-entity, split-table phase 1/2/3, compound release, rename)
  ledgers/               operations.md · row-tiers.md · in-flight.md · refusals.md · toolchain.md · reviewers.md
                         · scale-datapoints.md (unchanged format) · cdc-tracked.md (new: which tables CDC captures, per environment)
  handbook/              the eight handbook chapters the ops cite (state-based vs migrations; pre/post-deploy;
                         idempotency; referential integrity; refactorlog; deployment safety; multi-phase; CDC)
  copilot/               the generated Copilot bundle (router, path-scoped instructions, one prompt, PR template)
```

The `_index` is renamed `shared/` because "index" means a table of contents to everyone who is
not the tree's author. `agents/intake.md` and `agents/change-author.md` fold into
`authoring.md` as phases; `agents/reviewer.md` becomes `reviewing.md`; the four `review/`
skills fold into it. `THE_RECORD.md` (308 lines), `THE_RECORD_FORMS.md` (143), and the
register lint fold into `record.md` (one page beside the template, as the review recommended).

### 9.2 What is removed from the tree

| Removed | Lines | Why |
|---|---:|---|
| `ENABLEMENT_PROGRAM.md`, `ASSESSMENT_2026_08_24.md`, `ARCHITECTURE_REVIEW_2026_08_28.md`, `PHASE_2_CURRICULUM.md`, `HANDOFF_SESSION_2026_08_26.md`, `ACCELERANT_PLAN.md`, `CONNECTORS.md`, `PROVING_PATH_WINDOWS.md` (`PORTABILITY.md` is kept and moves to `knowledge/`) | ~2,400 | program history; archived with v2; the review itself said this literature "no schema-change session will ever read" |
| `self-test/` (protocol, 983-line prompt matrix, rubrics, golden runs) | ~2,400 | the nightly proof lane over the sample records discharges the regression duty; conversation quality is judged in the pilot, not by a rubric an agent can game from adjacent files |
| 38 of the 50 `sample-prs/` (46 top-level, four compound) | ~2,800 | a gallery where a dozen teach the same shapes; the 45 remain as *data* for the proof lane (§10), not as prose |
| `scripts/*.mjs` | 1,875 | `prove.mjs` → `estate prove`; `bake.mjs` → `estate twin bake/restore`; `inflight-check.mjs` → `estate check inflight` (~80 lines: it takes the touched tables from the delta, not from regexing the SQL, and normalises schema, brackets and case against `ledgers/in-flight.md`); `ssdt-agent-gates.mjs` → three tests in `Io.Tests` (citations resolve; op count = sample-record count = proof-lane facts; register rules hold on `samples/`); `ssdt-agent-package.mjs` → `estate knowledge package` (generate the Copilot bundle and `.claude/` pointers) |
| `proving-ground/` as a hand-authored SSDT project | ~1,300 | the golden schema in `tests/Golden/` is the proving ground; `estate emit` produces the project; the Twin fills it |
| `THE_DECISION_TREE.md` as a separate document | 215 | it *is* `authoring.md`'s spine; the state machine and its guards move there verbatim |

The op skills keep their trigger phrases (they are what Copilot's skill discovery matches),
their flip conditions (empty/populated, clean/violating, coexistence), their named trap, their
"prove it" steps, and their verdict paragraph. They lose: the restated proving-loop mechanics
(now "run `estate prove`"), the restated register rules, the per-op review-and-release
boilerplate, and the relative-path citation thickets. From ~111 lines average to ~75.

### 9.3 Personas become phases

`authoring.md` is `THE_DECISION_TREE.md`'s nine states, each with its exit guard, each naming
the verb it runs:

| State | Verb(s) | Exit guard |
|---|---|---|
| S0 intake | (conversation; `ops/` dispatch) | object + operation + intent captured; the one business question asked |
| S1 edit | (edit the `CREATE`; never write `ALTER`) | the desired-state `.sql` exists |
| S2 probe | `estate profile --tables …` | counts and violating rows captured |
| S3 prove | `estate prove --target twin [--permissive]` | a real verdict from this branch |
| S4 classify | `estate diff` (data-loss steps) + the verdict | shipping shape and approval weight set |
| S5 ship | (the sub-machine: one release / two releases; F2 edge forbidden) | terminal reached |
| S6 fork | (`ask-the-developer` form) | posed and recorded; emit-and-flag |
| S7 emit | `estate record` | ten sections present |
| S8 verify | (self-check against `record.md`) | every sentence denotes; shape matches proof |

One addition the tree does not have: the record's *after deploy* section names the Integration
Studio refresh, per environment, as the step that completes the change. Nine of the 45 ops
mention the refresh and `os-vocabulary` files it as "application-side"; no record section,
no template, and no state of the decision tree owns it. A change the application cannot see is
not shipped, and it is the one failure the publish guard is structurally blind to (§2.1).

The reviewer's `reviewing.md` runs `estate prove` on its own copy, `estate diff` for scope,
the adversarial moves (inject a violating row; play a blocked change forward under
`--permissive`), and renders one of four dispositions. The reviewer stays a separate skill
because the reviewer is a separate person; but the reviewer's *engine* is the PR gate (§10.3),
and the skill is for the lead's sparring, not the queue.

### 9.4 The Copilot bundle

Generated by `estate knowledge package`, unchanged in structure from v2's `copilot-package/`:
`.github/copilot-instructions.md` (the router), `.github/instructions/*.instructions.md`
(path-scoped guardrails that attach when a `.sql`, a deployment script, or a publish profile is
open: the never-rules), `.github/agents/*.agent.md` (authoring, reviewing), `.github/skills/`
(op pointers), `.github/prompts/ssdt-schema-change.prompt.md` (the one door), and the PR
template for GitHub and Azure DevOps. The degradation ladder (Visual Studio 2026 18.5+ with
discovered skills; 18.4 with agents plus the index; 2022 with prompt files; ask-only) is kept
because it matches what ships.

The bundle is generated into a *different* repository (the team's Azure DevOps project), so the
contract is stated: the bake lane runs `estate knowledge package` on every merge to `main`,
publishes the bundle as a fingerprinted artifact, and opens a pull request against the estate
repository when the fingerprint moved. Nobody hand-edits the copy; a drifted copy is a failed
check, not a mystery.

The pilot the August review put first ("run it on one champion laptop; nothing in the
packaging is verified on the team's actual build") is still the first thing to do. §16 lists
it as the operator decision it is.

### 9.5 What it costs to keep the tree true

The August review's §3.3 named the tax: each operation carried four surfaces (a skill, a sample
pull request, a self-test prompt, a Twin fact), and 978 lines of gate and packager code (1,052
today: `ssdt-agent-gates.mjs` 370, `ssdt-agent-package.mjs` 682) existed to hold them in
agreement. v3 keeps two of the four: the skill (knowledge) and the fact (proof, as a row of
data in `tests/Golden/changes/`). The sample records shrink to a dozen shapes and are checked
by the proof lane like any other change. The self-test prompts go. With two surfaces, the gate
code is three tests (§9.2) and the packager is one verb. The count that must agree is one:
`ops/` directories = golden change archetypes, and the proof lane fails when it does not.

### 9.6 Compound changes

The tree proves atoms and ships molecules (the review's first finding), and its addendum added
a compound corpus. v3 makes the release the unit of proof by construction: `estate prove` takes
a project, not an operation, so a pull request that adds an entity, a seed, two foreign keys,
and a defaulted `NOT NULL` column is proven as one delta. `decompose` (planning a compound
request into ordered pull requests) stays in `authoring.md` as S0's second question, and the
in-flight ledger holds the multi-phase programs with their windows and the tables they hold.

---

## 10. The substrate and the three lanes

The August review's four layers, as v3 builds them.

### 10.1 Layer 0 — the substrate is pulled, not built

- **Source of truth for shape:** the estate repository's own `.sql` files, read by `estate read
  --from ssdt`.
- **Source of truth for data shape:** `evidence.shape.json`, committed to the repository,
  literal-free (counts, null rates, distinct counts, lengths, fan-out; no values). Produced once
  from real Dev by `estate profile --distributions` followed by `estate twin evidence derive`,
  and refreshed by the bake lane when the schema fingerprint moves. The rich tier, with values,
  never enters the repository; it lives where the bake lane runs.
- **The artifact:** `estate twin bake` produces a `.bacpac` (and a container image where Docker
  is available), named by the schema fingerprint and the evidence fingerprint, published as a
  pipeline artifact. A developer's machine runs `estate twin restore <artifact>` once and holds
  a current, masked, distribution-faithful copy in minutes. On Windows without Docker, LocalDB is
  the substrate; on the hosted build agent, LocalDB; on a Mac or a monorepo agent, Docker. The
  verb chooses; the developer does not.
- **Realism:** the row tiers (`ledgers/row-tiers.md`) drive mint volumes so a table the estate
  holds at `>1M` rows is minted at a volume where the publish guard's cost is visible (the scale
  lane measured the index build as the first engine cost visible over tool overhead at ~1M
  rows). The `PROVING_PATH_WINDOWS.md` route of restoring a real Dev backup is gone; it put
  real data on laptops, which the synthetic engine was built to prevent.

- **What LocalDB cannot hold:** Change Data Capture, SQL Agent, and databases over 10 GB. The
  bake lane therefore produces the `.bacpac` for everyday proving and keeps one full instance
  (the Docker image, or the hosted agent's own SQL Server) for the CDC class and the scale
  lane. `prove` refuses those proofs on LocalDB with the reason (§8.6); the lanes never report
  a CDC proof as green because it ran where CDC does not exist.

### 10.2 Layer 1 — the verdict is a tool call

`estate prove` (§8.6). One process, one JSON object, one integer. The Strict and Permissive
profiles are files under `ci/profiles/`, mirrored from the estate pipeline's publish task, and
the verdict names the profile and the engine version it ran. The DacFx version is pinned in
`ledgers/toolchain.md` *and* in the project file, and `prove` refuses with exit 6 if they
disagree. That closes the "two corpora on divergent engines" risk the review named as the
sharpest unresolved one.

v2 ended with two proving mechanisms in two languages on two engines (`Pipeline/Deploy.fs`,
1,303 lines of F# on in-process DacFx 162.5.57; `scripts/prove.mjs`, 427 lines of JavaScript on
an external sqlpackage 170.x) and three ways to stand up a substrate (`DockerImageEmitter`,
`TwinContainer`, `bake.mjs`). Two proof corpora on divergent engines was already the tree's
own second finding. v3 has one of each: `io/Publish` proves in-process on the pinned package,
and `io/Twin` stands the substrate up. The JavaScript is retired at step 4 (§14.2).

### 10.3 Layer 3 — the reviewer's engine is the pipeline

Three lanes, as templates for GitHub Actions and Azure DevOps, in `ci/`:

**The proof lane** (nightly, and on any change to `ops/`, `samples/`, `tests/Golden/`, or the
kernel). For each sample record in `samples/` and each of the 45 op archetypes in
`tests/Golden/changes/`: apply the change to the golden schema, `estate prove --target <fresh>`,
assert the verdict matches the recorded one (blocked/clean, guard kind, trust state,
idempotent). This is the tree's 41 `SamplePr*` facts, as data driven through the verb rather
than as 13 F# test classes that only compile in Debug. A finding that changes (an engine
update that makes an FK land untrusted) fails the lane by name.

**The bake lane** (on schema change to the estate repository's main branch, and weekly). `estate
read --from ssdt` → fingerprint → if moved: `estate twin up` on the agent, `estate twin bake`,
publish the artifact, update `ledgers/row-tiers.md` from the evidence. Never touches real data
unless the rich tier is present on the agent, in which case it re-profiles Dev and re-derives
the shape tier, and the shape tier's diff is a reviewable commit.

**The PR gate** (build validation on the estate repository). `estate check inflight` (refuse a
pull request that touches a table held by an open lag window), `estate twin restore` (the
current artifact), `estate prove --project … --target twin` on the pull request's *combined*
delta, `estate record` regenerated from the verdict and diffed against the PR body's sections
(the machine checks that "how it ships" matches the proof; the human reads the rest), the
verdict published as a build artifact. Exit 3 fails the check *with the verdict attached*, so a
blocked change must carry its two-release shape. Exit 0 passes. Anything else fails loudly as a
tooling failure. The template `pipelines/ssdt-agent-pr-validation.yml` is the model; v3's is
the same steps with the scripts replaced by verbs.

The PR gate also publishes `changelog.json` (§8.10) as a pipeline artifact on every green run,
so the SSIS team's per-sprint mapping has a machine-readable source that names renames by
refactorlog key. During a two-release window the changelog carries both the shape the model
declares and the shape the database holds, marked, because for one release they disagree and
the SSIS team must know which one they are mapping.

### 10.4 What the lanes do not do

They do not approve. They do not publish to a named environment. They do not relax
`BlockOnPossibleDataLoss`. They do not read real data on a pull request. The one human act (the
business call: what value fills the blank rows; whether the orphan is deleted or reassigned)
stays a human act, posed by the record's "not checked / still open" section and answered in
review.

---

## 11. The cut, in one page

Appendix D is the ledger: every v2 module with its line count, its consumers today, one of
five verdicts, and its v3 replacement. This section is what the ledger says when it is read
whole. The verdicts are: **KEEP** (survives close to verbatim), **KEEP-SIMPLIFIED** (the
capability survives, the apparatus around it does not), **FOLD-INTO** (the concept survives
inside another module), **RETIRE-AFTER-EJECT** (finite cutover-era machinery, frozen until the
last leg runs), and **DELETE** (no consumer today and none in the six post-eject workflows).

### 11.1 The roll-up

Of the 118,686 lines in `sidecar/projection/src`:

| Verdict | Lines named | What it is |
|---|---:|---|
| KEEP / KEEP-SIMPLIFIED | ~46,000 → **~24,000 kept** | the IR, the evidence, the four decision tables, the diff, the statement stream and its ScriptDom builders, the readers, σ and the Twin, the seed emitter, the refactorlog, the exit codes, the purity analyzer |
| FOLD-INTO | ~1,500 | small helpers absorbed by their one caller |
| RETIRE-AFTER-EJECT | ~34,000 | transfer and movement, the estate timeline, episodes and the eject, data-correction approvals, the OSSYS extraction path (with §16's caveat), and every cutover-only policy axis |
| DELETE | ~37,000 | the registry and its adapters, the writer monads, config with its bindings and seams, `Voice`/`View`/`Watch`/`Navigator`, the run store, the advisory analytics, the manifest, the perf and lint apparatus |

### 11.2 Five sentences

1. **The largest deletable cluster is not a subsystem; it is plumbing that exists because the
   chain is long.** `TransformRegistry` (565) + `RegisteredTransforms` (227) + `PassChainAdapter`
   (162) + `StrategyRegistrations` (119) + `RegisteredAllTransforms` (133) + `Composition` (179)
   + `Classification` (184) + the twelve `*Binding` modules (2,208) + the four `*Seam` modules
   (871) + `Lineage` (485) + `LineageBuffer` (106) + `ComposeState` (124) + `RunSpine` (470) is
   5,833 lines, none of which computes anything about a schema, plus the tests that pin all of
   it. With a chain of four function calls, `registered ⇔ executed` is not a property to prove;
   it is the compiler.
2. **4,479 lines of terminal presentation for a consumer that reads JSON.** `Voice` (1,602),
   `View` (655), `Watch` (798), `TtyRenderer` (495), `Navigator` and `ReviewNavigator` (929). The
   consumer is a Copilot session and a pipeline; both want a structured verdict and an exit code.
3. **4,600 lines configuring a run when the unit of work is a change.** `Config` (2,189),
   `ConfigSchema` (203), the `*Binding` family (2,208), a 680-line generated schema drift-tested
   byte-for-byte, and an axiom (A44, "expressible ⇔ reachable") to keep it honest. What is left
   to configure post-eject is the publish posture and the substrate: about twenty keys.
4. **A 1,600-line subtree with one circular consumer.** Four advisory passes and their tuning
   (~550) feed `ManifestEmitter` (1,080), which reports coverage of an export nobody performs
   after the eject. The two passes that looked advisory and were not (`Centrality`, which sets
   the Twin's row allocation; `BoundedContext`, behind a default-off flag) are the correction
   §7.9 records.
5. **~15,400 lines of transfer and movement that are finite and not yet finished.** `TransferRun`
   (3,402), `MovementSurface` (2,825), `Cli/Faces/Transfer` (2,233), `FidelityCompareRun` (1,110),
   `MigrationRun` (1,000), `MovementSpec` (979), the `Transfer*` family (1,822), `PeerTransfer`
   (546), the capture and journal modules (993), `CapabilitySurvey` (449). The reverse leg they
   exist for is built and canary-gated and has not run. Frozen, buildable, not deleted, until it
   runs or is cancelled (§16).

### 11.3 What the verdicts do not touch

Nine things are carried close to verbatim, because each one was proven against a live engine
and the proof would have to be repeated if a line moved: `Statement.fs`; `ScriptDomBuild.fs`
with `ScriptDomGenerate.fs` and `Render.fs`; the rows of the four decision tables; `SyntheticData.fs`;
`RefactorLogEmitter.fs` with its UUIDv5 keys; `StaticSeedsEmitter.fs` and `MergeRender.fs`;
`outsystems_metadata_rowsets.sql`; `Twin.Runtime/Check.fs`; and `NoUnsafeTimeInCoreAnalyzer.fs`.

### 11.4 The documents

| Family (v2 root) | Files / lines | Disposition |
|---|---:|---|
| `DECISIONS.md` | 1 / 30,911 (480 entries over 69 days) | archive whole; the 44 Active Deferrals are read once for §16 before the move |
| chapters (`CHAPTER_*.md`) | 56 / 11,692 | archive; git history is the record of what each session did |
| `HANDOFF.md` and the backlogs | 5 / 6,733 | archive; a `NEXT.md` of at most forty lines replaces the letter |
| audits (`AUDIT_*`, `PAY_ONCE`, `SCALAR_*`, `ATOMIC_*`, `TRANSFER_ISOMORPHISM`, `HOLDOUT`) | 18 / 9,422 | archive; three are named in Appendix F because they still carry findings |
| vision (`NORTH_STAR`, `VISION`, `HORIZON`, `CONSTELLATION`, the four `VECTOR`s, `CRYSTALLINE_FORM`, `THE_PROJECTION_PRINCIPLE`, …) | 14 / 12,869 | archive; `CRYSTALLINE_FORM.md` in Appendix F |
| cutover (`V1_PARITY_MATRIX` 3,152, `V2_DRIVER`, `V2_PRODUCTION_CUTOVER`, `REVERSE_LEG_WORK_PLAN`, `CUTOVER_READINESS_BRIEF`, …) | 16 / 12,408 | archive once the reverse leg's fate is decided; the parity matrix is provenance for §12 |
| spine (`README`, `CLAUDE`, `KICKOFF`, `EXECUTION_PLAN`, `STORYBOARD`, the four `USE_CASE_ONTOLOGY`s, …) | 12 / 8,154 | `CLAUDE.md` §4's fifteen survival rules move to `knowledge/` where each is still true of v3 (most are about the Docker pool and the read side); the rest archives |
| principles (`AXIOMS`, `PRODUCT_AXIOMS`, `ADMIRE`, `THE_VOICE`, `THE_INSTRUMENT`) | 5 / 6,574 | archive; the laws are tests (§13) and `ADMIRE.md`'s v1→v2 donation ledger is superseded by §12 |
| Twin (`THE_TWIN.md`, `THE_SYNTHETIC_DATA_DESIGN.md`) | 2 / 619 | kept as they are, beside `io/Twin` |
| the remainder (`CONFIG_REFERENCE`, `PORTABILITY`, `PERF_HARNESS`, `THE_CONFIG_CONTROL_PLANE`, …) | ~57 / ~29,000 | `PORTABILITY.md` moves to `knowledge/`; the rest archives |

The family counts are the planning pass's grouping and overlap a little at the edges; the
measured total is 179 files and 119,384 lines. `docs/`, `handbook/`, `ssdt-playbook/` and
`notes/` at the v1 root (72,554 lines) archive with v1, except the eight handbook chapters the
op skills cite, which move under `knowledge/handbook/`.

### 11.5 The tree

`ssdt-agent/` is the part of v2 with the highest keep ratio. Kept: the 45 op skills (4,961
lines, trimmed to ~3,400), the six shared skills (750), `FINDINGS_AND_CHANGES.md` (502),
`THE_DECISION_TREE.md` (215, as the spine of `authoring.md`), the record forms (451 → one
page), the eight ledgers (272 lines, plus `cdc-tracked.md`), `PORTABILITY.md` (94), the PR
template (87), and the pipeline template. Simplified: three agents (743) → two skills; four
review skills (495) → one; 50 sample pull requests (3,755) → a dozen; `self-test/` (2,493) → a
handful of conversation cases in the pilot's notes; the gates and packager (1,052 lines of
JavaScript) → three tests and one verb. Deleted: `skills/operations/` (381, a second index over
an index), and the nine program documents §9.2 names (~2,500), of which
`ARCHITECTURE_REVIEW_2026_08_28.md` is read once more (it is v3's brief) and then archived.

---

## 12. What v3 takes from v1

The question this section was planned to answer was "what did v1 have that v2 never carried?"
The honest answer, after reading v1's trunk and the parity matrix's sixty-odd NOT-MAPPED rows
against the six post-eject workflows (§2.4), is short: **v2 dropped almost nothing the
post-eject job needs, and the one thing it dropped that matters most, reading an SSDT project
or a dacpac as a first-class operand, is the thing v3 is built around.** Appendix E is the
ledger; this is its conclusion.

### 12.1 Five carries

1. **The SSDT project and the dacpac as readers.** v1's `Osm.Dmm` read a project through
   `SsdtProjectDmmLens` (276 lines) and `ScriptDomDmmLens` (619). v2's `compare` verb was built
   with `DiffSource = LiveEnv | StoredRun | ModelFile` and nothing else, though its own cash-out
   named `SsdtProject` and `DacpacFile`. Post-eject the project *is* the schema. `io/Ssdt` (~600
   lines) reads both through DacFx's `TSqlModel`, not by re-parsing the files, so what v3 reads
   is what DacFx will publish.
2. **The supplemental Users model.** `config/supplemental/ossys-user.json` (341 lines) and
   `OutSystemsInternalModel.cs` (285) describe the platform-owned Users entity that every
   external entity's `CreatedBy`/`UpdatedBy` references. v2 has no counterpart. It becomes a
   resource of `io/Ossys` while that exists, and a table in `tests/Golden/` regardless, because
   the Twin has to mint those foreign keys against something.
3. **The three-option remediation generator.** `RemediationQueryBuilder.cs` is 73 lines that
   produce, for a NULL-violating column, exactly three statements: `UPDATE … SET col = <default>
   WHERE col IS NULL`, `DELETE … WHERE col IS NULL`, and `SELECT <pk>, * … WHERE col IS NULL
   ORDER BY <pk>`. v2's `RemediationEmitter` (561) serves the estate board's repair lane, a
   different shape. In v3, `Decide` returns the three statements with every `AskOperator`,
   extended to orphans (reassign to a named parent / delete / list) and duplicates (list), and
   the record's "the data" section prints them. This is `ask-the-developer` with its SQL
   attached.
4. **The last-row failure context.** v1's `SqlMetadataLog` records which rowset, which row and
   which column the reader was on when it failed; v2's runner returns a single `ValidationError`
   with no partial-state context. Every `Refusal` in v3 carries `Where` (§7), and the OSSYS and
   SQL readers fill it.
5. **Scoped comparison.** `DmmComparisonFeatures.cs` is 14 lines (columns, primary keys,
   indexes, foreign keys, extended properties). `estate diff --only …` (§8.5) is the same
   fourteen lines; a drift report that can ignore extended properties is one people keep
   reading.

### 12.2 Two carries as tests

- **`TopologicalOrderingValidator.cs`** (416 lines) proves that a *given* order is a valid
  topological order. v2 computes the order and never separately validates one. In v3 it is a
  property on `Order`: for every generated schema, every reference's target precedes its source
  in `Order.load`, and the cycle policy's output is a permutation of the input.
- **The tightening matrix.** v1's mode × signal table (`Cautious`/`EvidenceGated`/`Aggressive`
  against S1–S5, S7, D1) is a truth table with test cases. v2 replaced the modes with the
  two-valued `TighteningDirection` and per-intervention configuration, which is the better
  shape and v3 keeps it (§7.5); but v1's table is the most legible statement of what the
  decision must do at each cell, and it becomes a table test on `Decide.nullability`.

### 12.3 Three corrections to the premise

- **Transient retry is v2's, not v1's.** The outline listed "Polly retry" as something v1 had
  and v2 lost. v1's `src/` has no reference to Polly at all; its parity-matrix row reads
  "implicit delegation to caller." v2 built `Retry.fs` (113 lines, Polly 8.5.0) with exponential
  backoff and jitter over a named set of transient `SqlException` numbers, wired at
  `MetadataSnapshotRunner.fs:837`. It moves to `io/SqlServer`. Its one gap is the one its own
  header names: transients after the reader is open (`ReadAsync`, `NextResultAsync`) are not
  retried, and v3 does not promise otherwise.
- **The tunable timeout was carried.** A roll-up said v2 sets `CommandTimeout` to unlimited
  unconditionally. `SqlPolicy.fs` has `CommandTimeoutPolicy` with a 300-second default and a
  `PROJECTION_COMMAND_TIMEOUT_SEC` override, which is v1's `OSM_CLI_SQL_COMMAND_TIMEOUT` under a
  new name. Kept.
- **Multi-environment consensus is a finding, not a mode.** v1 profiled several environments
  and voted (`MultiEnvironmentConstraintConsensus.cs`, 540 lines, and its report, 645). v3
  profiles per environment (`estate check evidence --targets dev,qa,uat`) and lets the record
  show where they disagree; a vote hides the environment that would block.

### 12.4 What stays in v1's archive

The DMM three-lens comparator (2,201 lines: the canary is its successor and `diff` is its
verb); the load-harness DMV probes (572: the scale lane measures wall-clock on the Twin, and
wait statistics return only if a scale finding needs them); the UAT-users remap (42 files,
~5,050 lines: a finite promotion act that belongs to `Move` if it belongs anywhere); the
evidence cache with ten invalidation reasons (1,499: `EvidenceFingerprint`'s one round-trip is
the answer); the HTML report and browser launch (711 + 1,194: the pull request is the report);
the Spectre progress service (v2 grew it to 1,467 lines of board and then v3 removes both);
the CIR JSON Schema (422: the schema is the F# type and a derived codec); the three-tier
type-mapping policy (787: `SqlType`'s tables); and `EntityDependencySorter.cs` (2,188:
`Order.CyclePolicy.Manual` is the allowlist, as a value instead of a config file).

---

## 13. The laws, as tests

v2 had 48 axioms and 18 theorems, with 81 live witnesses and 38 skip-stubs. v3 has twelve
laws. Each is one test whose name is the law in English, each is listed in `LAWS.md` by a
script that reads the test tree, and a law without a green test is not in the list. There are
no buckets, no ladder, no matrix, no numbering. The v2 identifiers are given here once, for
the archaeology, and never again.

| # | The law (the test's name) | Where it runs | What it descends from |
|---|---|---|---|
| 1 | **the same inputs emit the same bytes** — `emit` over the same `Schema`+`Decisions` yields a byte-identical bundle across runs, machines, and orderings of the input lists | `Kernel.Tests`, property over generated schemas | T1 |
| 2 | **emit then read is the identity, in the quotient** — `read --from ssdt (emit s)` equals `s` after `SqlType.coarsen` on both sides and modulo the named divergences list | `Io.Tests`, golden + property | the canary; `Ingest ∘ Project = id`; A18/T16 |
| 3 | **emit is faithful to the repository** — `emit (read --from ssdt <estate repo>)` is byte-identical to the repository | `Io.Tests`, on the golden and on the estate | the idempotent redeploy; L3-S1 |
| 4 | **a vanilla policy changes nothing** — `decide Policy.Vanilla` yields no decisions and `apply` is the identity | `Kernel.Tests` | skeleton purity; pillar 9 |
| 5 | **every decision names its evidence** — for every column/reference/index, `decide` returns exactly one outcome, and the outcome's evidence or reason is consistent with the inputs (a property over generated evidence) | `Kernel.Tests` | total decisions, named skips |
| 6 | **a rename keeps its key** — `between s (rename s)` reports one `Renamed` and zero `Added`/`Removed`; the emitted refactorlog carries it; DacFx generates `sp_rename` and not `DROP`+`ADD`; `classify` refuses a rename whose entry is missing | `Kernel.Tests` + `Io.Tests` (publish and read back) | A1; identity survives rename |
| 7 | **a delta's inverse undoes it** — where `inverse d = Some d'`, `between (apply d s) (apply d' (apply d s))` is empty | `Kernel.Tests` | the rollback section; T12 in spirit |
| 8 | **an idempotent redeploy is silent** — publishing an unchanged bundle twice generates an empty second script, and on a CDC-enabled copy produces zero capture rows | `Io.Tests` (Docker, CDC-isolated fixture) | CDC-silence; T15 |
| 9 | **the publish guard is data-blind** — `prove` of a `NULL → NOT NULL` on a populated table is blocked before and after every NULL is backfilled; on an empty table it is clean | `Io.Tests`, the make-mandatory archetype | F1/F7; the tree's flagship finding |
| 10 | **a foreign key lands trusted** — a clean declarative FK add ends with `is_not_trusted = 0` on the pinned engine; the verdict says so, and a different engine version is a refusal | `Io.Tests` | F5→F9; the DacFx-version risk |
| 11 | **a mint has no orphans and repeats itself** — `Synth.mint` under the same seed yields byte-identical rows; every FK cell references a minted parent; re-profiling the mint recovers the shape tier within ε; a schema edit re-mints only touched columns | `Kernel.Tests` + `Io.Tests` (the Twin's `check`) | π∘σ≈id; S-stable; Twin laws 1 and 6 |
| 12 | **the substrate carries no literal** — the committed shape tier contains no captured value; `twin evidence verify` refuses one that does | `Io.Tests` | Twin law 3 |

Two more are dependency laws, tested by xUnit but about the tree rather than the domain: **the
kernel cannot do I/O** (its package references are the BCL and `System.Collections.Immutable`
only; the banned-API analyzer forbids `System.IO`, `System.Net`, `DateTime.Now`, `Random`, and
`Task` in `kernel/`), and **dependencies
point one way** (`io/` does not reference `cli/`; nothing references `knowledge/` or `ci/`).
These are v2's pure-core analyzer and the Twin's kernel manifest, kept as tests.

Three more are process laws, tested by the lanes rather than by xUnit: the proof lane
re-proves every sample record nightly; the PR gate refuses a release into an open lag window;
`estate emit` in CI over the estate is a no-op (law 3, run on every merge).

Everything else in `AXIOMS.md` is either a consequence of these (sibling emitters agree on the
keyset because there is one emitter; `registered ⇔ executed` because the chain is the code;
channel orthogonality because there are no channels; v1's `TopologicalOrderingValidator` returns
as a property on `Order`, not a law), or a statement about v2's own
construction that v3 does not make (the torsor, the two rulers, the writer-fidelity law, the
commuting square), or a property of a subsystem v3 does not carry (the estate verdict's
totality, the reverse-leg realization selector, the certificate on three surfaces).

---

## 14. The path from here to v3

Not a rewrite. A strangler, in an order chosen so that every step leaves something the team can
use and nothing the team relies on gets worse. The parity oracle is fixed before any code moves.

### 14.1 The oracle

Three things must be true of every step, and they are checked by machine before it merges:

1. **v3's `emit` over the golden schema and over the estate repository is byte-identical to v2's
   `SsdtDdlEmitter` output** for every artifact both produce, modulo a named list of intended
   differences that starts empty. The golden corpus (`GoldenCatalog.fs` and its emitted bundle)
   is the fixture; `tests/Golden/` in v3 is its port.
2. **v3's `prove` reproduces the 45 recorded verdicts** (the proof lane, §10.3) on the pinned
   engine.
3. **v3's `read --from sql` of a database v2 deployed equals v2's `ReadSide` catalog** in the
   quotient, for the golden and for a Twin-minted estate.

v1's fixtures (`tests/Fixtures/emission/{edge-case, edge-case-rename, edge-case-untrusted}`)
are a fourth check for the OSSYS reader: `read --from ossys` over the fixture manifest must
produce the schema v1's `model.edge-case.json` describes.

### 14.2 The order

| Step | What moves | What proves it | What the team gets |
|---|---|---|---|
| 0 | Freeze. Tag `4e844fc` as `v2-final`. Move `sidecar/projection` to `archive/v2/` and the C# trunk to `archive/v1/`, both still building under their own solutions. Write `archive/INDEX.md`: one line per v2 document, its date, its status (provenance / superseded / knowledge moved to `knowledge/`), and archive the 179 documents as one compressed bundle beside the index so that search tools find the index and not a specification v3 replaced; the code stays buildable. | both archived solutions build; CI keeps running v2's proof lane against the archive until step 6 | nothing changes for them yet |
| 1 | `kernel/`: `Identity`, `SqlType`, `Schema`, `Statement` (verbatim), `Delta`, `Order`, `Decide`, `Evidence`, `Synth`, `Record`. Port v2's kernel tests to C# first (they are the specification; `CatalogDiffTests` alone is 54 facts), then the types, then the functions. Port the decision tables and σ with their property tests, and add σ's golden fixture (v2's minted rows at one seed) so a drifting port fails law 11 on day one; write the twelve laws' kernel halves. | laws 1, 4, 5, 6, 7, 11 (kernel halves); the tightening matrix test from v1 as a table | — |
| 2 | `io/Render` (verbatim from `ScriptDomBuild`+`Render`) and `io/Emit`; `estate emit`; `estate diff`. | oracle 1 (byte-identity against v2's emitter on the golden); law 3 on the estate | a bundle they can diff against their repository |
| 3 | `io/Ssdt` (DacFx `TSqlModel` read), `io/SqlServer` (read-side + profiler), `io/Ossys` (if it survives §16's first decision); `estate read`, `estate profile`, `estate decide`. | oracle 3; oracle 4; law 2 | `check drift` becomes possible |
| 4 | `io/Publish`; `estate prove`. Retire `prove.mjs`. | oracle 2 (the 45 verdicts); laws 8, 9, 10 | **the verdict as a verb** — the pilot can run |
| 5 | `io/Twin` (from `Twin.Runtime`); `estate twin *`. Retire `bake.mjs`, the Twin CLI, `proving-ground/`. | law 11, 12; the Twin's five laws as `Io.Tests` | **the pulled substrate**; the bake lane |
| 6 | `knowledge/`: fold agents into `authoring.md`/`reviewing.md`; trim ops; `record.md`; `samples/` to a dozen; `estate knowledge package`. Retire the gates/packager scripts; three tests replace the gates. The proof lane moves to v3. The bake lane starts publishing the Copilot bundle to the estate repository by pull request (§9.4). | the proof lane green on v3; the Copilot bundle regenerated byte-identically to the last v2 bundle except for verb names | the Copilot bundle |
| 7 | `estate check` (drift, environments, outsystems, evidence, inflight); `estate record`; the PR gate template. Retire `inflight-check.mjs`. | the PR gate runs on a real pull request against the estate | **the gate** |
| 8 | `io/Move` (the cutover wing), reduced from `TransferRun`; `estate move`. Only if the reverse leg is going to run (§16); otherwise the transfer engine stays frozen in `archive/v2`, buildable, until the leg is cancelled, and is then deleted. | a Twin-to-Twin transfer with re-key and revert; the reverse-leg canary from v2's integration pool, ported | the reverse leg, if still needed (§16) |
| 9 | Delete `archive/v2`'s CI. Keep the archive. Regenerate `LAWS.md`, the verb reference, and the config reference. Cut a release. | everything above, green, on one solution | one tool |

Steps 1–2 can proceed in parallel with 3; step 4 needs 2 and 3; steps 5–7 can proceed in
parallel after 4; step 8 is last and optional. Each step is a pull request series that ends
with the oracle green, never a branch that lives for a month.

### 14.3 What is not ported

Nothing in §7.11's table, nothing in Appendix D marked DELETE, and no document. Where a v2 test
pinned behaviour v3 keeps, the test is ported with its name changed to English. Where a v2 test
pinned behaviour v3 drops (the estate lanes, the episode replay, the capability descent, the
Voice bijection, the transform registry), it is not ported and its absence is noted once in
`archive/INDEX.md`.

### 14.4 Freeze points and rollback

Two standing v2 commitments are honoured, not silently dropped. *"V1 stays warm through
cutover+30 regardless"*: `archive/v1` keeps its solution buildable and its fixture tests
running in CI until the parity oracle (§14.1) is green at step 4, and then for thirty days after
the last environment's cutover date, whichever is later; retiring that lane is a one-line
decision. *"V2 is self-contained; every commit cherry-pickable"*: the archive is a move, not a
rewrite, so v2's history and its cherry-pickability are intact; what ends is the promise that
new work lands there.

v2 keeps running its own CI from the archive until step 6, so at any point before that the
team's actual workflow (the tree, `prove.mjs`, the Twin CLI) is untouched. Steps 4 and 5 are
the two that change what the team runs; each ships with the old script kept for one release
as a fallback that prints a deprecation line. If the pilot (§16) fails on Copilot's
instruction-following against the new bodies, the fallback is the old bundle, regenerated from
the archive.

### 14.5 Effort

This document does not estimate sessions. v2's history shows why: 183 commits in 55 days added
54,000 lines of source and 40,000 of documents, and effort estimates in that history were
consistently wrong in the direction of "more got built than was planned." The honest statement
is the size of the target (§6.6: ~30,000 lines of C#, ~25,000 of tests) and the size of the
donor (the modules named in §6.4's left column, ~63,000 lines, of which about a third is kept
close to verbatim). Most of the work is deletion and re-plumbing, not invention. The one
genuinely new module is `io/Ssdt` (read an SSDT project through DacFx), and the Twin already
does most of it in `EstateModel.fs`. The language changes too: v3 is C# and v2's F# is the
specification it is ported from (§6.6). The port is mechanical for most of the kernel, because
v2's code is already mostly data and pure functions; the two places it is not (σ's determinism
and the statement builders' completeness) are covered by law 11's golden fixture and by law 2.

---

## 15. How sessions work in v3

v2's mass was not written by anyone who wanted mass. It was written by sessions that were each
asked to leave the repository more legible than they found it, and that each concluded the
best way to do so was a new named surface: a chapter close, a handoff letter, a decision
entry, an axiom, a pillar, a survival rule, a codified failure mode. Every one of those was
locally reasonable. The sum is 221,000 lines of prose. v3 changes the reward, not the people.

### 15.1 The write budget

v2's `DECISIONS.md` has 480 dated entries across 69 days, 29 of them on its busiest day: about
seven decisions written up per working day for four months. That is the cadence the budget is
sized against. A session (human or agent) working on v3 may write:

- **code and tests**, without limit, subject to the oracle and the laws;
- **one line in `DECISIONS.md`** per decision, of the form `2026-09-17 · <decision in one
  sentence> · #<PR>`; the reasoning is in the pull request, which git keeps;
- **`knowledge/`**, subject to §15.3;
- **`ARCHITECTURE.md`**, only to make it true again when code changed what it says, and only
  the paragraph that became false;
- **the ledgers** (`operations.md`, `row-tiers.md`, `in-flight.md`, `refusals.md`,
  `toolchain.md`, `reviewers.md`, `cdc-tracked.md`), which are append-only tables with a fixed
  shape and live in the estate repository; the engine holds only their formats and seeds them
  empty once.

A session may not write: a new top-level document; a chapter open or close; a handoff letter;
a status section in a README; an axiom; a pillar; a named failure mode; a "survival rule";
a plan document; an assessment of itself. If a session has learned something that fits none of
the allowed places, it has learned something that belongs in a test, a finding (`findings.md`,
which is knowledge), or the pull request description.

### 15.2 Handoff is the pull request

There is no `HANDOFF.md`. A session ends by opening or updating a pull request whose
description says what it did, what it verified, and what is left, in the record's register
(agentless, finding on top, proof beneath). The next session reads the open pull requests and
`git log`. v2's letters were "forward-looking, second-person" by rule; the rule existed because
the letters were the only place state lived. In v3 state lives in the tree, the tests, the
ledgers, and the open PRs.

### 15.3 Knowledge is curated, not accreted

`knowledge/` grows only by: a new op skill (when the team meets an operation the catalog lacks,
proven first on the Twin), a new finding (with its captured output), a struck-through finding
(with the date and what overturned it), a new sample record when a new *shape* appears (not a
new instance of an existing shape), and edits that make an existing skill shorter or truer.
The three tests that replaced the gates (citations resolve; op count = archetype count =
sample count; register rules hold) run on every change to `knowledge/`. A skill that grows
past 120 lines is a finding to split, not a skill to keep.

### 15.4 Generated surfaces

- `LAWS.md` — from the test tree, by a 40-line script, on every CI run; the twelve laws with
  their test names and last green run.
- The verb reference — from `estate --help --json`.
- The config reference — from the config schema the CLI validates against.
- The archive index — written once at step 0 and never touched.
- The Copilot bundle and the `.claude/` pointers — from `knowledge/` by `estate knowledge
  package`; checked in, fingerprinted, never hand-edited, and excluded from every line count.
- The document inventory (`DOCS.md`) — from `ci/docs.manifest.json`, the file that lists every
  document with its reader, its moment and its budget; the test that the tree equals the
  manifest is what turns this section's write budget from a rule into a gate (companion §10).

A generated file is never edited by hand. A hand-written file never restates what a generated
one says. `README.md` is the one hand-written surface that says where everything is, and its
budget is 150 lines.

### 15.5 What an agent session reads first

`AGENTS.md` (120 lines, imported by `CLAUDE.md`), `NEXT.md` (40), the README of the package
being changed (80), and the one line `estate doctor` prints. Under 300 lines before code.
`ARCHITECTURE.md` and `VALUES.md` are read when the change is architectural, not at every
session; `knowledge/README.md` when the change is to the domain. The companion document's §9
gives the session protocols in full.
v2's Tier 1 reading order was "~40 minutes," five documents, one of them a 1,172-line ontology;
its `CLAUDE.md` had fifteen survival rules because the environment had fifteen ways to hurt a
session. v3's environment has fewer ways: one solution, `dotnet build` and `dotnet test`, one
SQL Server fixture that is Docker or LocalDB, no warm container to resurrect, no perf gate to
void, no lint with exemptions, no pools to keep apart. The rules that remain are in the test
README, and there are four.

### 15.6 The one ritual kept

Refute in the open. When a finding is overturned, strike it through with a date and the run
that overturned it; never edit it away. When a law's test goes red for a reason that is not a
bug, the law changes in the same pull request as the test, and the decision line says why.
When a session disagrees with this document, it says so in the pull request and changes
`ARCHITECTURE.md` in the same change. This is the culture v2 built and the only ceremony v3
keeps.

---

## 16. Risks, open questions, and the decisions only the operator can make

This section is ranked by how much of the design would change if the answer went the other
way. Each item names what it moves and the default this document has assumed, so that the
document is complete under stated assumptions and no section waits on an answer.

### 16.1 Decisions only the operator can make

One decision is already made and recorded rather than listed: the language is C#, with v2's F#
as the specification (§6.6).

1. **Pin the pipeline's DacFx and run the one `is_not_trusted` check.** A declarative foreign-key
   add read *untrusted* on DacFx 162.5.57 (the engine the Twin corpus runs in-process) and lands
   *trusted* on sqlpackage 170.x (the engine the hand proofs ran). `estate/toolchain.md` lists
   both the sqlpackage row and the pipeline's DacFx row as `UNPINNED`. Every trust-state finding
   in the catalog (F5 → F9, F10, F19) is therefore asserted on an engine the pipeline may not
   run. *Moves:* the constraint half of the op catalog; which engine `io/Publish` pins; law 10's
   meaning. *Default assumed:* the pipeline is on the 162 family until shown otherwise, so the
   package pin starts at 162.5.57, every record re-stamps its engine, and the first act after
   the pin is one real publish with `is_not_trusted` read back. An afternoon on the right
   machine; open for three weeks.
2. **Run the Visual Studio Copilot pilot on one champion laptop.** Nothing in the packaging has
   run on the team's actual build; the review called it the riskiest assumption in the system.
   *Moves:* whether `knowledge/` is the product's front door or an artifact only a monorepo agent
   reads; which rung of the degradation ladder the team lands on; whether `authoring.md` at one
   skill is followed or ignored by Copilot's skill discovery. *Default assumed:* the 18.4 rung
   (agents plus the index), with the path-scoped instruction files as the surface that holds.
3. **Does the OSSYS reader survive?** Post-eject there is no upstream to re-derive the schema
   from, except that OutSystems still owns the logical model and Integration Studio refreshes
   *from* the database (§2.1). Reading OSSYS is the only way to see what the platform believes,
   and so the only way to check the one failure the publish guard cannot see. *Moves:* ~3,600
   lines of `io/Ossys`, `estate check outsystems`, the third axis of workflow 5, the fourth
   parity check (§14.1). *Default assumed:* it survives as an explicit optional package with its
   own budget line. If the operator says no: delete the package, rewrite workflow 5 to two
   axes, and keep the rowset SQL in the archive where years of metamodel knowledge stay
   findable.
4. **Is the reverse leg going to run?** It is built, canary-gated, sized for ~200 million rows
   against a managed-DML sink, and no document after 2026-07-25 records it executing. Prod has
   not been released to. *Moves:* whether ~15,400 lines of transfer and movement are frozen or
   deleted; whether step 8 (§14.2) exists. *Default assumed:* frozen in `archive/v2`, buildable,
   with its integration tests running; no port. The day it is cancelled, roughly a thousand
   lines of surrogate-remap and keymap-spill machinery whose only consumer it was become
   deletable the same day.
5. **Who are the dev leads?** `estate/reviewers.md` has two rows that read "fill in at the Dev
   cutover"; its own rule is that at least one available row must exist for the estate to ship;
   no self-approval at any seniority with a pool of four means a lead's own change needs the
   other lead; and the one SSDT-fluent person was out during the cutover window. *Moves:*
   whether the PR gate (§10.3) is a convenience or the substitute for reviewer expertise. It is
   the latter until the roster is data. *Default assumed:* the gate is load-bearing and the
   register page is written for a reviewer who knows SQL and is new to SSDT.
6. **Which tables does CDC capture, in which environments?** The tree removed its CDC skills on
   2026-08-21 as out of scope; CDC runs in production and features depend on it. A capture
   instance is bound to the column list it was enabled with, so an added column is not captured
   until the instance is recreated, and a rebuild (`identity-swap`) leaves the instance on the
   old object. *Moves:* `ledgers/cdc-tracked.md`, the `classify` finding (§8.3), which proofs
   need a full instance rather than LocalDB (§10.1), and whether `recreate-capture-instance`
   returns as an op. *Default assumed:* the ledger is filled by `estate check cdc` reading
   `sys.tables.is_tracked_by_cdc` from each environment once, committed, and the op returns in
   v3's first quarter.
7. **Prod's first release is a baseline publish.** QA and UAT were set up by their own cutover
   publishes; Prod has never been published to; its row counts are not in the row-tier ledger
   (which holds four sample tables from the proving ground). The data-blind guard fires on
   population, so a change proven clean on a copy of Dev can block on Prod for no reason but
   rows. *Moves:* whether any "will this block?" answer in the corpus applies to Prod. *Default
   assumed:* before any Prod release, `estate profile --target prod --counts` runs once and
   the counts land in `row-tiers.md` with their date; the gate reads the ledger for the target
   environment, not for Dev.
8. **Run the Twin's evidence import against real Dev.** `twin evidence import/derive/verify`
   exist as verbs; the proving loop still runs against eight tables and about thirty-five rows,
   and the review called the wiring "the single most valuable integration not yet done."
   *Moves:* whether the substrate is distribution-faithful or schema-derived. *Default assumed:*
   the derived floor until the import runs, and the bake lane refuses to label an artifact
   "faithful" until it has.
9. **One repository or two?** The Copilot bundle is generated into the team's Azure DevOps
   repository. *Moves:* §9.4's packager-by-pull-request contract versus a monorepo where the
   bundle is a build output. *Default assumed:* two repositories and the contract as stated.
10. **Retire or retain v1 warm.** The standing commitment is "V1 stays warm through cutover+30
    regardless." *Moves:* how long `archive/v1` builds in CI. *Default assumed:* §14.4, and the
    retirement is a one-line decision afterwards.

### 16.2 Risks this document names and the outline did not

- **The refresh nobody owns.** Every change has a second half: a human refreshing the external
  entity in Integration Studio, per environment, after the deploy. Nine of the 45 ops mention
  it; no record section, template, or decision-tree state owned it until §9.3. The failure is a
  runtime error in the application, invisible to every publish and every gate. `check
  outsystems` sees it and nothing else does.
- **A green deploy that destroys data.** F17: with a contract-phase R1 landed, one more publish
  of the same release re-created the column, backfilled every row from its default, and reported
  *Successfully published database*. This is why the lag window is a lock (§8.10) and why the
  gate is not optional. It is also why a data correction with no receipt is unrecoverable and
  looks successful: F15 showed the seed silently undoing a pre-deploy repoint. The record's
  data section therefore carries rows-before, rows-after, and who approved (§7.10).
- **A rename by typing.** Post-eject the refactorlog is maintained by Visual Studio's rename
  refactoring and not at all by editing the `.sql` text. Without the entry DacFx drops and
  re-adds the column and the publish is green. `classify` refuses the delta (§8.3) and `emit
  --refactorlog` writes the entry; the companion trap (a "refactorlog cleanup" that removes an
  entry before every environment has deployed past it) is a `check environments` finding.
- **Objects with no op.** Sequences, triggers, views, synonyms, computed columns, collation
  changes, and CDC capture instances have no skill (Appendix C). The kernel reads and emits the
  first three; the tree says nothing about them. A trigger body ScriptDom cannot parse degrades
  to a comment marker in v2 and would vanish on the next redeploy; v3 refuses instead (§4.3).
- **Two mechanisms for one job.** v2 ended with proving in F# on in-process DacFx 162 and
  proving in JavaScript on external sqlpackage 170, and three substrate stand-up paths. Two proof
  corpora on divergent engines was already the tree's second finding. §10.2 picks one of each;
  the seam that remains (`Engine = InProcess of version | SqlPackage of path`) exists only for an
  engine the package feed does not offer, and the verdict names which ran.
- **The gate code is a hidden coupling.** 1,052 lines of gate and packager script hold four
  surfaces per op in agreement; trimming the surfaces without re-scoping the gates leaves a gate
  that fails on every op. §9.5 reduces the surfaces to two, the gate to three tests, and the
  count that must agree to one.
- **v3 repeating v2's curve.** v2 grew from ~77,000 to 118,686 source lines in nine weeks, with
  about seven decision entries written per working day. Nothing about a smaller start prevents
  the same slope. The budget is a CI test (§6.4), the write budget is a rule with a list of
  forbidden document kinds (§15.1), the laws are generated from tests (§15.4), and the archive
  index is written once. Those are the mechanisms; the review's diagnosis ("every insight
  becomes a named surface, every surface a citation target, every citation a gate") is the
  reason they are mechanisms and not advice.
- **Scale.** F20 found the nonclustered index build to be the first engine cost visible over
  tool overhead, at about a million rows. Whether any Prod table is above that is unknown here
  (item 7). LocalDB's 10 GB ceiling sits near the scale lane's largest scenario (1.18 million
  rows); the scale lane runs on a full instance.
- **The readback path was secondary and is now primary.** `ReadSide` marks every reconstructed
  data-bearing table `Static`, so profiling a readback catalog yields an empty evidence cache
  (survival rule 8). Post-eject the live database is one operand of every delta. Fixed by
  construction in `read --from sql` (§4.3), and covered by law 2, which would otherwise fail on
  the first real estate.
- **Cross-module references.** v2's read side carried an assumption that a reference's target
  lives in the same module. v3's `Reference.Target` is a `Key`, and modules are not part of the
  join, so a 300-table estate with foreign keys across espaces reads whole; the golden schema
  gets one such reference so the assumption cannot return unnoticed.

### 16.3 Open questions this document could not settle from the tree

- Did the Dev trunk switch on the weekend after 2026-08-26? The newest document says
  "next weekend" in the future tense and nothing later records it. Every "post-eject" sentence
  above is written for the day it does.
- Which DacFx does the Octopus publish step run? (Item 1.)
- Has any change shipped through the pipeline yet? `estate/operations.md` opens empty by
  design and has no rows; `in-flight.md` has none.
- Were the 41 nightly facts ever run against a Twin minted from real Dev evidence rather than
  the proving ground's seed? (Item 8.)
- Does the SSIS team map from the model or from the database during a two-release window? The
  changelog (§8.10) carries both shapes so that either answer works, but the question should be
  asked once.

---

## Appendix A — Glossary and renames

One vocabulary. Where v1 or v2 used a different word for the same thing, the old word is
listed so a reader coming from either tree can find their footing.

| v3 term | Meaning | v1 called it | v2 called it |
|---|---|---|---|
| **schema** (`Schema`) | a state: tables, sequences, SQL schemas, read from anywhere | `OsmModel` | `Catalog` |
| **table** (`Table`) | one relational table, with its columns, keys, references, indexes, checks, triggers, temporal config, and (for a static entity) its seed rows | `EntityModel` | `Kind` |
| **column** (`Column`) | one column, with its storage type, nullability, intent (`Mandatory`), identity, default, computed definition | `AttributeModel` | `Attribute` |
| **reference** (`Reference`) | a foreign key, with its column pairs, target, actions, and constraint state | `RelationshipModel` | `Reference` |
| **key** (`Key`) | the stable identity of an object across renames; a GUID carried as the `Estate.Key` extended property, or derived from the qualified name | `EntitySsKey` (optional) | `SsKey` (four variants) |
| **folder** (`Table.Folder`) | where a table's file goes in the bundle; an espace name by default | module | `Module` |
| **evidence** (`Evidence`) | what the data says: row counts, nulls, orphans, duplicates, lengths, distributions, with a probe status per fact | `ProfileSnapshot` | `Profile` |
| **shape tier / rich tier** | evidence without values (committable) / with values (never committed) | — | Twin `ShapeTier`/`RichTier` |
| **policy** (`Policy`) | the tightening knobs and operator overrides; `Vanilla` is the faithful projection | `TighteningOptions` | `Policy.Tightening` + interventions |
| **decision** | the outcome of one tightening table for one column, reference, or index, with its evidence or reason | `NullabilityDecision` etc. (bools + rationale strings) | `NullabilityOutcome` etc. (DUs) |
| **delta** (`Delta`) | a change between two schemas: added, removed, renamed, changed per channel, with facets and a derived refactorlog | (DMM diff) | `CatalogDiff` / `ChannelDiff` |
| **data-loss step** | a statement in a delta the publish guard will refuse on a populated table: narrow, drop, `NOT NULL` on populated, lossy retype | — | — (the tree's D0) |
| **verdict** (`Verdict`) | what DacFx did when a delta was published to a disposable copy under a named profile: clean / blocked with the verbatim `Msg` / failed; guards; trust; idempotence; engine version | — | `prove.mjs` output |
| **record** (`Record`) | the ten-section pull-request body a reviewer approves by reading | — | the tree's record (`THE_RECORD.md`) |
| **shipping shape** | one release / one release with the gate relaxed (not on this estate) / two releases / refused | — | the tree's S5 terminal |
| **bundle** | the emitted SSDT project: per-table files, schemas, sequences, seeds, refactorlog, sqlproj, manifest, verify queries | the output root | `SsdtBundle` |
| **substrate / the Twin** | the disposable, converged, synthetic-data copy every proof runs on | (a real backup) | the Twin |
| **mint** | one deterministic synthetic generation run | — | mint (σ) |
| **scenario** | a named overlay on evidence, volumes, corrections, and pins; never a generator | — | scenario |
| **order** (`Order`) | a load order under an explicit cycle policy: refuse, defer nullable legs, or manual | `EntityDependencySorter` | `TopologicalOrderPass` |
| **finding** (`Finding`) | something a run noticed and continued past; coded, with evidence | `PipelineInsight`, `Opportunity` | `DiagnosticEntry`, `EstateFinding` |
| **refusal** (`Refusal`) | something a run would not do; coded, named, exit 9 | `ValidationError` | `ValidationError`, `CapabilityRefusal` |
| **ledger** | an append-only table in `knowledge/ledgers/`: operations, row tiers, in-flight, refusals, toolchain | — | the estate ledgers; not v2's `Ledger` algebra |
| **the guard** | `BlockOnPossibleDataLoss`: DacFx's data-blind, row-presence check | — | the row-presence guard |
| **the cutover wing** | `io/Move` and `estate move`: the finite transfer machinery, retired after the eject | UAT users; `full-export` | `TransferRun`, the reverse leg |

Words v3 does not use: projection (as a system name), catalog, kind, attribute, espace (except
in `Ossys.fs`), SsKey, lineage, diagnostics (as a type), episode, lifecycle, manifest (as a
provenance record; the bundle's `manifest.json` stays), torsor, delta as a norm, CDC-as-norm,
adjunction, pillar, axiom, theorem, bucket, ladder, matrix, chapter, handoff, spine, seam,
binding, face, voice, view, board, lane (except in the Twin's data lanes), verdict (in the
estate sense), posture, archetype (kept only inside `Move` for the two sink classes), rendition,
protein, amino acid, canary (the law is "emit then read is the identity"; the word survives
only as the test's nickname).

---

## Appendix B — The numbers, and the claims they corrected

Every figure in this document was measured against the working tree at `4e844fc` on
2026-09-17 with `wc -l`, `find`, and `grep`. This appendix records the measurements and the
places where a v2 document or an ingestion pass said something else.

### B.1 Measured

| Quantity | Value | Command |
|---|---:|---|
| v1 C# source lines, `src/` | 78,461 | `find src -name '*.cs' \| xargs cat \| wc -l` |
| v1 T-SQL lines, `src/AdvancedSql` | 2,198 | same, `*.sql` |
| v1 test lines, `tests/` | 51,969 | same, `*.cs` |
| v1 test baseline | 970 total · 936 pass · 25 fail (pre-existing, environment) · 9 skipped | `test-baseline-summary.md` |
| v1 projects | 9 `.csproj` in `src/` | `find src -name '*.csproj'` |
| v2 F# source lines, `sidecar/projection/src` | 118,686 | `find … -name '*.fs' \| xargs cat \| wc -l` |
| v2 F# test lines | 118,513 | same, `tests/` |
| v2 test functions | 5,157 `[<Fact>]` + 173 `[<Property>]` + 14 `[<Theory>]` | `grep -rn` |
| v2 tests skipped by attribute | 209 (all in `Projection.Tests`; 38 of them in `AxiomTests.fs`) | `grep -rn 'Skip *='` |
| v2 tests that cannot run without Docker | 89 files · 28,444 lines (the two integration pools) | `find … -name '*.fs' \| xargs wc -l` |
| v2 root markdown | 179 files · 119,384 lines | `ls *.md \| wc -l`; `cat *.md \| wc -l` |
| `DECISIONS.md` | 30,911 lines · 2.2 MB · 480 dated entries · 44 active deferrals | `grep -c '^## 2026-'` |
| `HANDOFF.md` | 3,887 lines | `wc -l` |
| `ssdt-agent/` | 248 `.md` files · 21,724 lines; 5 scripts · 1,875 lines JS | `find`, `wc` |
| `ssdt-agent/skills/op` | 45 directories | `ls \| wc -l` |
| `.claude/` | 73 files · 1,405 lines | `find`, `wc` |
| root docs | `readme.md` 1,162 · `AGENTS.md` 253 · guardrails 48 · `tasks.md` 264 · editorial 659 · templated rules 2,948 | `wc -l` |
| `docs/` · `handbook/` · `ssdt-playbook/` · `notes/` | 23,945 · 8,980 · 8,265 · 31,364 | `find … -name '*.md' \| xargs cat \| wc -l` |
| v2 files over 1,500 lines | 12 (`Pipeline.fs` 3,414 · `TransferRun.fs` 3,402 · `ScriptDomBuild.fs` 2,834 · `MovementSurface.fs` 2,825 · `Estate.fs` 2,633 · `Cli/Faces/Transfer.fs` 2,233 · `Config.fs` 2,189 · `Catalog.fs` 2,095 · `ReadSide.fs` 1,973 · `MetadataSnapshotRunner.fs` 1,722 · `Voice.fs` 1,602 · `SsdtDdlEmitter.fs` 1,552); 24 over 1,000 | `wc -l \| awk '$1>=1000'` |
| v2 ceremony counts, `src/` (occurrences) | 566 `LINT-ALLOW` · 279 `Bench.scope` · 619 `RequireQualifiedAccess` · 338 `ValidationError.create` · 75 smart `create` ctors · 15 `toStructured` + 20 `toDiagnosticString` defs | `grep -rao '<pattern>' --include=*.fs . \| wc -l` (line-based `grep -rn` gives 552/279/616: some lines carry two) |
| pass chain length | 21 steps in `RegisteredTransforms.chainStepsWithPins` (22 with the cascade-shock advisory counted separately) | direct read |
| advisory passes with the manifest as sole outside consumer | 4 (`QueryHints`, `SchemaComplexity`, `CascadeShockZones`, `ProfileAnomalies`); `CentralityRanking` also feeds `SyntheticVolume.byCentrality` (`Twin.Runtime/Mint.fs`, `Pipeline/SyntheticLoadRun.fs`); `BoundedContexts` feeds one default-off flag | `grep -rn byCentrality\|clusterFksByContext` |
| `ssdt-agent/sample-prs` | 50 files: 46 top-level + 4 in `compound/` | `find`, `ls` |
| `ssdt-agent/estate` ledgers | 8 files · 272 lines; `reviewers.md` has two unfilled approver rows | `wc`, `grep 'fill in'` |
| op skills that mention the Integration Studio refresh | 9 of 45 (+ `os-vocabulary`, `_index/multi-phase`); 0 mentions in the record template, `THE_RECORD.md`, or the decision tree | `grep -ril` |
| op skills that mention CDC | 0 of 45 (removed 2026-08-21, commit `a5827e8` and siblings) | `grep -rilw cdc`, `git log` |
| `Statement` DU cases | 26 | direct read of `Statement.fs` |
| `Attribute` fields | 20; `Reference` 8; `Index` 17; `Policy` 6 | direct read of `Catalog.fs`, `Policy.fs` |
| `AxiomTests.fs` | 1,720 lines · 123 facts · 38 skipped · 85 live | `grep -o` |
| axioms / theorems | A1–A48 · T1–T18 | `AXIOMS.md` |
| commits in local history | 183, 2026-07-06 → 2026-08-29; 140 with an AI co-author trailer, 43 by the owner | `git log` |
| v2 source growth | 77,000 (recon, 2026-06-25) → 118,686 (2026-09-17): +54% | recon header vs. `wc` |
| `Projection.Core` growth | 20,913 (`CRYSTALLINE_FORM.md`, 2026-06-04) → 32,267 | same |

### B.2 Claims corrected along the way

| Claim | Source | What the tree says |
|---|---|---|
| v1 tests are ~101,446 lines | an ingestion digest | 51,969; the digest double-counted |
| v1 has ~15 projects | `V1_ARCHITECTURE_COMPENDIUM.md` §1 | 9 `.csproj`; the rest are folders inside `Osm.Pipeline` |
| v1's signals are S1–S7 | `TEMPLATED_LOGIC_AND_BUSINESS_RULES.md`, `readme.md` | S1, S2, S3, S4, S5, S7 and `D1`; there is no S6 |
| `EntityDependencySorter` is ~200 lines | the compendium | 2,188 |
| the UAT-users pipeline has 6 steps | the compendium | 8 |
| v2 has 785 skipped tests | an ingestion digest | 209 by attribute; the digest counted the domain field `SkippedReferences` |
| v2 has 66 `Ignore`d tests | an ingestion digest | xUnit has no `Ignore`; the hits were the OutSystems delete rule and `IgnoreDuplicateKey` |
| `Policy` has ~12 axes | two ingestion digests | 6 fields |
| `CatalogDiff` is `DiffOf<'v> = Added \| Removed \| Modified` | an ingestion digest | `ChannelDiff<'change> = { Added; Removed; Renamed; Reshaped }` — the collapse `CRYSTALLINE_FORM.md` recommended, already shipped |
| `DECISIONS.md` has 351 entries | an ingestion digest | 480 |
| the dead algebra (`Prism`, `PassContext`, `LineageTree`, `Certificate`, `DiagnosticLattice`) was never deleted | a roll-up | four of five are gone from source; `DiagnosticLattice` was trimmed to one predicate with a test |
| the three parity blockers were never closed | a roll-up | all three addressed 2026-07-18 (`DECISIONS.md` lines 28402, 28452, 28494): two fixed, one converted to a refusal |
| the tree has 41 operations | `ARCHITECTURE_REVIEW_2026_08_28.md`, the tree's own README | 45 directories; 41 was true when the review was written |
| the pillars number eight | `KICKOFF.md`, `V2_DRIVER.md`, `NORTH_STAR.md` | seven (2026-05-09), eight (2026-05-10), nine (2026-05-15) per `DECISIONS.md`; the three documents never caught up |
| doc compression available is ~35% | `CRYSTALLINE_FORM.md` line 21 | the same document's table says 45–55% |
| `SchemaComplexityPass` runs over an empty topology | `CRYSTALLINE_FORM.md` §3.4 | the chain wiring lifts the pass with the computed topology; the claim may have been true on 2026-06-04 |
| six advisory passes have the manifest as their only consumer | the outline, two roll-ups, an early draft of this document | four; `CentralityRanking` feeds the Twin's row allocation and `BoundedContexts` a default-off flag |
| v2 sets `CommandTimeout` to 0 unconditionally | a roll-up | `CommandTimeoutPolicy` defaults to 300 s and reads `PROJECTION_COMMAND_TIMEOUT_SEC`; v1's tunable timeout was carried |
| v1 has a Polly retry policy v2 never carried | the outline's own question list | v1 has no Polly reference; v2 built `Retry.fs` (113 lines, Polly 8.5.0) and wired it at `MetadataSnapshotRunner.fs:837` |
| the sample pull requests number 41, 45, or 46 | the August review, its addendum, a roll-up | 50 files: 46 top-level plus four compound; 41 was true when the review was written |

The pattern in B.2 is the pattern of the whole corpus: directionally honest, numerically loose,
and never reconciled because reconciliation had no owner. §15.4's generated surfaces are the
owner.

---

## Appendix C — The operation catalog (kept, 45)

The op skills are the knowledge layer's vocabulary. They dispatch on the developer's own
phrasing ("tick the Mandatory checkbox") and carry the flip conditions that decide how a change
ships. All 45 are kept; each becomes shorter. Grouped by family, with the shipping shape the
catalog has proven on a populated table.

| Family | Operations | Proven shape on populated data |
|---|---|---|
| Columns, add | `add-optional` · `add-mandatory` · `add-default` · `audit-columns` | one release (`add-mandatory` needs a default on a populated table) |
| Columns, change | `make-mandatory` · `make-optional` · `widen` · `narrow` · `retype-implicit` · `retype-explicit` · `rename-attribute` · `modify-default` · `drop-default` | `make-mandatory`, `narrow`, `retype-explicit`: **two releases** (the guard); `widen`, `retype-implicit`, `make-optional`, defaults: one release; `rename-attribute`: one release with the refactorlog entry, else data loss |
| Columns, remove | `delete-attribute` | two releases (pre-deploy drop with the model lagging; the 4-phase deprecation) |
| Tables | `create-entity` · `rename-entity` · `delete-entity` · `move-schema` · `archive-entity` · `junction` | `delete-entity`: one release with an explicit pre-deploy `DROP TABLE` (the removed file is a phantom under `DropObjectsNotInSource=false`); `rename-entity`/`move-schema`: refactorlog or data loss |
| Keys | `define-pk` · `drop-pk` · `create-fk-clean` · `create-fk-orphan` · `drop-fk` · `change-delete-rule` · `toggle-trust` · `identity-swap` | `create-fk-clean` lands trusted in one release (F9); `create-fk-orphan` needs a reconcile pre-deploy, then lands trusted; `drop-pk` refuses when referenced (`SQL71516`); `toggle-trust` is operational, refuse-and-route; `identity-swap` is a table rebuild |
| Indexes | `add-index` · `drop-index` · `modify-index` · `add-unique` · `drop-unique` · `rebuild-index` | `add-unique` blocked by duplicates (`Msg 1505`) and by multiple NULLs (use a filtered index); `rebuild-index` is maintenance, refuse-and-route |
| Checks | `add-check` · `drop-check` | `add-check` re-validates every row; a violating row blocks (`Msg 547`) |
| Static entities (seeds) | `create-static-seed` · `edit-seed` · `delete-seed-value` | idempotent `MERGE`; explicit IDs; deactivate, never delete |
| Multi-phase programs | `split-table` · `merge-tables` · `move-attribute` · `extract-to-lookup` | additive → cutover → subtractive, with totality proofs before any drop; a row in `in-flight.md` |
| Temporal | `temporal-new` · `temporal-convert` | system-versioned tables; conversion is a rebuild program |

**Not covered, and said so.** Sequences, triggers, views, synonyms, computed columns, collation
changes, and CDC capture instances have no op. Views and synonyms were removed from the tree on
2026-08-21 as out of scope; CDC was removed the same day, while CDC runs in production. For
each, `authoring.md` answers as `rebuild-index` does: *I don't do this; here is who does and
what to check* (refuse-and-route), rather than guessing. The kernel still reads and emits
sequences, triggers, and computed columns (`Statement` has their cases), so the gap is in
guidance and proof, not in fidelity. Two of the seven deserve ops in v3's first quarter: CDC
capture instances (§16) and sequences.

The six shared-reasoning skills the families point to: `tightening-class` (the data-blind
guard and the two-release shape), `constraint-is-a-claim` (a key or check is a claim about
existing data, proven at apply time; reconcile first), `idempotent-seed` (the guarded `MERGE`;
silence is the proof), `identity-and-refactorlog` (identity is separate from name; the
refactorlog carries it), `multi-phase` (additive → cutover → subtractive; the conservation
proofs), `when-to-index` (the evidence an index needs).

---

## Appendix D — The v2 deletion ledger

Every path is relative to `sidecar/projection/`. Line counts are `wc -l` on the working tree at
`4e844fc`. "Consumers" means files outside the defining module that reference the concept,
where that number decided the verdict. Verdicts: **KEEP** · **KEEP-SIMPLIFIED** · **FOLD-INTO**
· **RETIRE-AFTER-EJECT** (frozen with the cutover wing, §16 item 4) · **DELETE**. The v3 column
names where the surviving capability lives (§6, §7, §8).

### D.1 `Projection.Core` — model and IR

| Module | Lines | Consumers / evidence | Verdict | v3 |
|---|---:|---|---|---|
| `Catalog.fs` (Module / Kind / Attribute / Reference / Index / Sequence) | 2,095 | every pass, emitter, adapter, and the Twin; `Attribute` alone has 20 fields, most read only by `SsdtDdlEmitter` | KEEP-SIMPLIFIED | `kernel/Schema` (§7.3): `Column` with 12 fields, no Module aggregate |
| `Identity.fs` (`SsKey`, four variants) | 327 | `Map<SsKey,_>` everywhere; post-eject there is no OSSYS guid to distinguish `OssysOriginal` from `V1Mapped` | KEEP-SIMPLIFIED | `Key` (one private GUID, §7.1) |
| `Types.fs` (`Name`) | 114 | collapses v1's eleven naming value objects | KEEP | `Name` |
| `Coordinates.fs` (`TableId`/`SchemaName`/`TableName`/`ColumnName`; SHA-256 truncation) | 371 | four near-identical smart constructors; the truncation is real | KEEP-SIMPLIFIED | one `Coordinate` module; truncation in `io/Emit` |
| `PrimitiveType.fs` | 20 | the canary's equivalence relation | KEEP | `SqlType.coarsen`'s codomain |
| `SqlStorageType.fs` | 264 | the BIGINT-versus-INT distinction the quotient must not erase | KEEP | `SqlType` |
| `SqlLiteral.fs` (typed literals, `ExpressionLit`) | 311 | DEFAULT values, computed expressions, MERGE rendering; the M-1 fix lives here | KEEP | `SqlType.Literal` |
| `PhysicalSchema.fs` (the readback quotient) | 1,041 | canary, overlay threading, drift; `CRYSTALLINE_FORM.md` §3.2 shows fusing it with `Attribute` blinds the canary | KEEP-SIMPLIFIED | not a second type: one `Schema`, compared after `SqlType.coarsen`; the DO-NOT is honoured by comparing in the quotient |
| `ModalityMark` (Static / TenantScoped / SoftDeletable / SystemOwned / Temporal) | in `Catalog.fs` | `Static` and `Temporal` have consumers; the other three only manifest predicates | KEEP-SIMPLIFIED | `Table.Seed: Row list option`, `Table.Temporal` |
| `Origin` (Native / ExternalIndirect / ExternalDirect) | in `Catalog.fs` | about the platform's storage; post-eject every table is external-direct | RETIRE-AFTER-EJECT | dropped |
| `ConstraintState`, `ReferenceAction`, `IndexUniqueness`, `ColumnRealization` | in `Catalog.fs` | each forbids an illegal quadrant (untrusted-without-constraint; PK-but-not-unique) | KEEP | the same invariants on `Reference`, `Index`, `Column` |
| `Optics.fs` | 140 | two call sites; `Prism` already retired | DELETE | record update syntax |
| `Fixpoint.fs` | 35 | four files, no tests | FOLD-INTO | its one caller |
| `StructuredString.fs` | 115 | ten files, zero direct tests | DELETE | `Finding.Message` is a string |
| `SqlIdentifier.fs` | 33 | emitters, refactorlog, sqlproj XML all delegate here | KEEP | `SqlType.bracket` |
| `UuidV5.fs` | 112 | `RefactorLogEmitter`'s deterministic keys | KEEP | `Key.ofName` |
| `KindColumns.fs` | 176 | a projection helper | FOLD-INTO | a function on `Table` |
| `OssysTypeMapping.fs` | 128 | the OSSYS reader only | RETIRE-AFTER-EJECT, with `io/Ossys` | a table in `io/Ossys` |
| `SqlTypeCorrespondence.fs` | 127 | the correspondence suite | KEEP-SIMPLIFIED | a table in `SqlType` |
| `Message.fs`; `Result.fs` (`ValidationError`, 338 `create` sites) | 58; 269 | four vocabularies for one thing, with `Diagnostics.fs` and `EstateFinding.fs` | FOLD-INTO | `Refusal` and `Finding` (§7); the library `Result` |

### D.2 `Projection.Core` — evidence and profile

| Module | Lines | Consumers / evidence | Verdict | v3 |
|---|---:|---|---|---|
| `Profile.fs` (twelve axes) | 1,429 | every `*Rules.evaluate`, σ, the estate, the Twin; `SourceUsers`/`TargetUsers` are cutover-era; `JointDistributions` unbuilt | KEEP-SIMPLIFIED | `Evidence` (§7.4), marginals only |
| `ColumnProfile`, `ProbeStatus`/`ProbeOutcome`, `ForeignKeyReality`, the distributions | in `Profile.fs` | the four numbers the tightening class turns on; observed-versus-trusted; `Msg 547`'s orphan count | KEEP | same |
| `ProfileDerivation.fs` | 1,040 | discover-once/derive-pure is right; the file is a god-file for it | KEEP-SIMPLIFIED | ~300 lines |
| `EvidenceCache.fs` | 366 | the shape tier's source | KEEP | `Evidence` store |
| `SamplingPolicy.fs`; `SamplingDiagnostics.fs` | 57; 29 | `Twin.Runtime/EvidenceImport.fs` | KEEP; FOLD-INTO | `Evidence.Probe` |
| `Statistics.fs`; `Meter.fs` | 30; 30 | percentile math | KEEP | same |
| `Centrality.fs` + `Passes/CentralityPass.fs` | 18 + 173 | `SyntheticVolume.byCentrality` → `Twin.Runtime/Mint.fs`, `Pipeline/SyntheticLoadRun.fs` (the correction §7.9 records) | KEEP-SIMPLIFIED | `Synth.volumes`; manifest reporting goes |
| `SyntheticVolume.fs` | 73 | `Mint.fs`, `SyntheticLoadRun.fs` | KEEP | `Synth` |
| `BoundedContext.fs` + `Passes/BoundedContextPass.fs` | 22 + 177 | the manifest, and one default-off flag (`clusterFksByContext`, byte-identical when off) | DELETE | nothing |
| `SchemaComplexityMetrics.fs` + `Passes/SchemaComplexityPass.fs` | 24 + 161 | zero consumers outside Core | DELETE | nothing |
| `ProfileAnomaly.fs` + pass | 14 + 147 | the manifest only | DELETE | nothing |
| `QueryHints.fs` + pass | 11 + 117 | the manifest only; `when-to-index` owns the advice | DELETE | the knowledge file |
| cascade-shock advisory (in `TopologicalOrderPass.fs`) | — | the manifest only | DELETE | `change-delete-rule`'s cascade-reach finding |
| `Passes/AdvisoryTuning.fs` | 97 | the four passes | DELETE | nothing |
| `CatalogRisk.fs`; `JointDependencyDiagnostics.fs`; `FkSelectivityDiagnostics.fs`; `InactiveAttributeDiagnostics.fs` | 71; 92; 83; 48 | two files each, no acting consumer; `IsActive` is an OSSYS concept | DELETE | nothing |

### D.3 `Projection.Core` — policy and decisions

| Module | Lines | Consumers / evidence | Verdict | v3 |
|---|---:|---|---|---|
| `Policy.fs` (Selection / Emission / Insertion / Tightening / UserMatching / BridgeRetarget) | 1,318 | the config surface of a projection engine; v3's unit is a change | KEEP-SIMPLIFIED | `Policy` with nine fields (§7.5) |
| `SelectionPolicy` | in `Policy.fs` | "DORMANT / unregistered / no pipeline wiring" in its own comment | DELETE | `read --modules` |
| `EmissionPolicy` (~10 flags) | in `Policy.fs` | most are v1-parity toggles, byte-identical when off | KEEP-SIMPLIFIED | three emitter options |
| `InsertionPolicy`; `DeleteScopePolicy`; `DataStagingPolicy`; `UserMatchingStrategy` | in `Policy.fs` | data lanes, convergent delete, bulk staging, the UAT re-key | RETIRE-AFTER-EJECT | `Move`, if ported |
| `TighteningPolicy` / `TighteningIntervention`; `TighteningDirection` | in `Policy.fs` | the intervention registry supports config-declared interventions; `RelaxationOnly` is a real two-valued axis | KEEP-SIMPLIFIED; KEEP | direct calls; the axis kept |
| `BridgeRetarget.fs` + pass; `BridgeRowDelta.fs` | 559 + 69; 309 | FK re-routing through a proxy table is a cutover repair | RETIRE-AFTER-EJECT | nothing |
| `Strategies/NullabilityRules.fs` | 308 | 308 lines for ~40 of decision; the table (steps 0–5) is the asset | KEEP-SIMPLIFIED | `Decide.nullability` (§7.5), ~60 lines |
| `Strategies/ForeignKeyRules.fs` | 406 | eight keep-reasons × five evidence variants; `isIgnoreRule` hardcoded `false` leaves one arm unreachable | KEEP-SIMPLIFIED | ~80 lines; the arm reachable |
| `Strategies/UniqueIndexRules.fs` | 277 | advise-only unless opted in is a real affordance | KEEP-SIMPLIFIED | ~60 lines |
| `Strategies/CategoricalUniquenessRules.fs` + pass | 261 + 133 | the only distribution-aware tightening; post-eject a developer states uniqueness and the publish proves it | DELETE | `add-unique` + `Msg 1505` |
| `Strategies/Composition.fs` (`fanOut`); `StrategyRegistrations.fs` | 179; 119 | registry plumbing | DELETE | four direct calls |
| the four tightening passes | ~1,045 | registry lift plus writer plumbing around the tables | FOLD-INTO | `Decide.decide` |
| `Strategies/CycleResolution.fs` (v7 exact per-SCC break) | 534 | FK cycles are real in this estate; the ≤2¹² subset solver is more than 300 tables need | KEEP-SIMPLIFIED | `Order.CyclePolicy` (§7.7) |
| `Passes/TopologicalOrderPass.fs`; `TopologicalOrder.fs` | 977; 742 | deploy order, seeds, the apply runbook | KEEP-SIMPLIFIED | `Order`, ~700 |
| `Classification.fs` (pillar 9 overlay axes) | 184 | proves a *projection* is reachable without operator intent; v3 has no skeleton projection | DELETE | nothing |
| `DecisionOverlay.fs`; `ConflictDetector.fs` | 139; 120 | the applied-decision carrier | FOLD-INTO | `Decision list`; `AskOperator of Conflict` |
| `VersionedPolicy.fs` | 466 | `seal approve`, the E7 audit trail | RETIRE-AFTER-EJECT | the pull request is the approval |
| `PolicyExpr.fs` | 188 | a policy expression language for a config surface v3 lacks | DELETE | nothing |
| `ModuleFilter.fs` | 464 | selecting espaces to export | RETIRE-AFTER-EJECT | `read --modules` |
| `Strategies/ForeignKeyReadback.fs` | 70 | `is_not_trusted` readback | KEEP | `io/SqlServer` |

### D.4 `Projection.Core` — passes, writers, registry

| Module | Lines | Consumers / evidence | Verdict | v3 |
|---|---:|---|---|---|
| `RegisteredTransforms.fs` (the 21-step chain); `TransformRegistry.fs`; `PassChainAdapter.fs`; `ComposeState.fs` | 227; 565; 162; 124 | a single-definition-site registry is elegant *because* the chain is long | DELETE | read → profile → decide → emit, four calls |
| `Passes/CanonicalizeIdentity.fs` | 145 | permutation invariance is a sort | FOLD-INTO | `Schema.create` sorts |
| `Passes/VisibilityMask.fs`; `Passes/NamingMorphism.fs` | 147; 138 | wired with `emptyMask` and `identity` by default | DELETE | an adapter-side filter |
| `Passes/NormalizeStaticPopulations.fs` | 95 | a sort before seed rendering | FOLD-INTO | the seed emitter |
| `Passes/SymmetricClosure.fs` | 222 | inverse edges served only the deleted analytics | DELETE | nothing |
| `Passes/LogicalTableEmission.fs`; `LogicalColumnEmission.fs` | 199; 157 | `OSUSR_*` → human names: *the* cutover transform | RETIRE-AFTER-EJECT | the repository holds the logical names |
| `Passes/TableRename.fs` | 234 | operator-declared physical renames; `[]` by default | FOLD-INTO `Move` | post-eject a rename is a refactorlog entry |
| `Passes/UserFkReflowPass.fs`; `UserRemap.fs`; `UserIdentity.fs` | 378; 200; 217 | the UAT re-key | RETIRE-AFTER-EJECT | `Move`, if ported |
| `Lineage.fs`; `LineageBuffer.fs`; `RemovalReason` | 485; 106; — | every pass's return type | DELETE | a function returns `Finding list` |
| `Diagnostics.fs` (entry + writer + `DiagnosticLattice`) | 624 | the entry type is right; the writer threads a log through a chain v3 lacks; the lattice's only consumers are its own tests | KEEP-SIMPLIFIED | `Finding`; no writer |
| `Bench.fs`; `PinnedWriting.fs` | 360; 157 | 279 `Bench.scope` sites and one gate script whose verdict is void under concurrent load | DELETE | the scale lane measures wall-clock |
| `Projection.Analyzers/NoUnsafeTimeInCoreAnalyzer.fs` | 142 | makes purity a build fact | KEEP | the dependency laws (§13) |
| `ArtifactByKind.fs` | 195 | T11 only matters with two sibling emitters | DELETE | `Map<Table, Artifact>` |
| `RawValueCodec.fs` | 201 | the scalar encode/decode boundary | KEEP | `SqlType` |
| `IndexNaming.fs` | 90 | single-sourced index naming | KEEP | `io/Emit` |
| `EmissionMode.fs` | 69 | bulk/incremental data-lane selector | RETIRE-AFTER-EJECT | nothing |

### D.5 `Projection.Core` — change algebra and provenance

| Module | Lines | Consumers / evidence | Verdict | v3 |
|---|---:|---|---|---|
| `CatalogDiff.fs` (`ChannelDiff<'change>`, nine `AttributeFacet`s, `norm`) | 1,096 | `diff`, `compare`, rename detection, refactorlog, estate, `ChangeManifest`; the only typed change representation in either generation | KEEP-SIMPLIFIED | `Delta` (§7.6), v3's centre; the facet list re-checked for `Description` and `Order`, which a roll-up suspected undiffed |
| `Episode.fs`; `Lifecycle.fs`; `Ledger.fs` | 279; 193; 124 | the pre-eject provenance timeline | RETIRE-AFTER-EJECT | git is the timeline |
| `DataObservation` (CDC capture count) | in `Episode.fs` | CDC-silence is the estate's highest-stakes guarantee | KEEP-SIMPLIFIED | law 8's check in the lanes |
| `ChangeManifest.fs` | 99 | the SSIS team's per-sprint changelog | KEEP-SIMPLIFIED | `changelog.json` from `estate gate` (§8.10) |
| `Migration.fs` | 249 | the inexpressible-`ALTER` refusal (exit 9) is real; DacFx ships the ALTER | KEEP-SIMPLIFIED | a predicate on `Delta` |
| `Tolerance.fs` (`ToleratedDivergence`, `@ladder` tags) | 458 | "retiring a variant deletes its tag, so the generator auto-flips the axis" is the honesty mechanism §15.4 copies | KEEP-SIMPLIFIED | a named-divergence list in law 2's test; the tag idea in `LAWS.md`'s generator |
| `CanaryResidual.fs` | 79 | the canary's diff-after-quotient | KEEP | law 2 |
| `RowFidelity.fs` | 220 | `''` ≠ `NULL`, collation-blind byte comparison; the conservation proofs need exactly this | KEEP | `io/Publish`'s comparator |
| `DataCorrectionReceipt.fs`; `ApprovedDataCorrections.fs`; `ApprovalWorkflow.fs`; `ActConsent.fs` | 237; 526; 208; 329 | in-flight correction approvals | RETIRE-AFTER-EJECT | the receipt survives as three fields of the record's data section; the workflow does not |

### D.6 `Projection.Core` — synthesis and movement

| Module | Lines | Consumers / evidence | Verdict | v3 |
|---|---:|---|---|---|
| `SyntheticData.fs` (σ) | 824 | the Twin's mint; `π ∘ σ ≈ id`; zero orphans by construction | KEEP | `Synth` |
| `SyntheticCorrection.fs` (`PiiKind`/`MaskRule`/`FakerGenerator`) | 345 | "no real data on laptops" is how | KEEP | `Synth` |
| `SurrogateRemap.fs`; `Reconciliation.fs`; `SliceSpec.fs`; `Closure.fs`; `DataLoadPlan.fs`; `Transfer.fs`; `TransferScope.fs` | 398; 506; 162; 306; 303; 135; 70 | transfer machinery | RETIRE-AFTER-EJECT | `Move`, if ported |
| `EstateFinding.fs` (20+ kinds × 4 lanes × 5 planes) | 845 | workflow 5 is permanent; the taxonomy is not | KEEP-SIMPLIFIED | `check` verbs return `Finding list` |

### D.7 `Projection.Targets.SSDT` (9,737 lines, 19 files)

| Module | Lines | Consumers / evidence | Verdict | v3 |
|---|---:|---|---|---|
| `Statement.fs` (26 cases) | 443 | the typed statement stream | KEEP | verbatim (§7.8) |
| `ScriptDomBuild.fs`; `ScriptDomGenerate.fs`; `Render.fs` | 2,834; 420; 196 | every construct needs its builder; determinism is a generator-options fact | KEEP | `io/Render`, split by statement family |
| `ConstraintFormatter.fs` | 556 | post-processing into *v1's* multi-line shape; post-parity v1's shape has no claim | KEEP-SIMPLIFIED | ~100 lines of house style |
| `SsdtDdlEmitter.fs` | 1,552 | per-kind driver plus overlay application | KEEP-SIMPLIFIED | `io/Emit`, ~850 |
| `SsdtBundle.fs`; `PostDeployEmitter.fs`; `BatchSplitter.fs`; `PhysicalSchemaReader.fs`; `DataStatementArgs.fs` | 78; 75; 182; 178; 147 | the bundle map; the seed is a permanent surface (F15); GO handling; AST-side readback | KEEP | `io/Emit`, `io/Ssdt` |
| `SqlprojEmitter.fs` | 131 | post-eject the `.sqlproj` is hand-owned; generating it overwrites the team's edits | RETIRE-AFTER-EJECT | nothing |
| `ApplyRunbookEmitter.fs` | 86 | an operator checklist for a manual apply; Octopus applies | RETIRE-AFTER-EJECT | the record's after-deploy section |
| `RefactorLogEmitter.fs`; `RefactorLogRender.fs` | 567; 200 | the only thing that makes a rename keep its data; UUIDv5 keys | KEEP | `io/Emit`; `emit --refactorlog` |
| `ManifestEmitter.fs` (+ 17 `PredicateName` variants, coverage) | 1,080 | the only consumer of the four advisory passes; reports coverage of an export nobody performs | DELETE | nothing |
| `SchemaMigrationEmitter.fs` | 451 | the closest v2 gets to "show me what this change does"; DacFx's script is authoritative | KEEP-SIMPLIFIED | the expectation `prove` prints beside DacFx's script |
| `DacpacEmitter.fs` | 271 | v3 must build a dacpac to prove anything | KEEP | `io/Ssdt`'s build step |
| `DockerImageEmitter.fs` | 290 | one of three substrate paths | FOLD-INTO `io/Twin` | one path |

### D.8 `Projection.Targets.{Data, Json, Distributions, OperationalDiagnostics}`

| Module | Lines | Consumers / evidence | Verdict | v3 |
|---|---:|---|---|---|
| `StaticSeedsEmitter.fs`; `MergeRender.fs`; `StagedMerge.fs`; `DataSeedFormatter.fs`; `DataInsertScript.fs` | 443; 266; 197; 211; 117 | the guarded MERGE (F12: guarded touches 0 rows, unguarded touches 3, and CDC is in prod) | KEEP | `io/Emit`'s seed leg |
| `StaticPopulationEmitter.fs` | 168 | two emitters for one concern | FOLD-INTO | the seed emitter |
| `DataEmissionComposer.fs`; `MigrationDependenciesEmitter.fs`; `BootstrapEmitter.fs`; `CsvExport.fs` | 763; 376; 188; 167 | full-export lanes | RETIRE-AFTER-EJECT | nothing |
| `RegisteredDataTransforms.fs` | 50 | registry | DELETE | nothing |
| `CatalogCodec.fs`; `ProfileCodec.fs`; `JsonCodecKernel.fs`; `GoldenCodec.fs`; `JsonEmitter.fs` | 932; 454; 117; 152; 274 | hand-written pairs; `CRYSTALLINE_FORM.md` Probe B: ~58/60 collapse with two escape hatches | KEEP-SIMPLIFIED | `io/Json`, a derived codec |
| `CorrectionCodec.fs`; `SliceCodec.fs` | 295; 178 | corrections and slices | RETIRE-AFTER-EJECT | nothing |
| `DistributionsEmitter.fs` | 344 | operator diagnostics; distributions reach σ directly | DELETE | nothing |
| `RemediationEmitter.fs` | 561 | the estate repair lane; the *need* (SQL to fix orphans before an FK lands) is permanent | KEEP-SIMPLIFIED | `Decide`'s three-option remediation (§12) |
| `SummaryFormatter.fs`; `DecisionLogEmitter.fs`; `SuggestConfigEmitter.fs`; `ActionableDiagnostics.fs`; `EstateOverlayEmitter.fs`; `Routing.fs` | 343; 321; 157; 152; 98; 104 | board rendering; a per-column rationale dump; ranked config edits; the RELAX lane | DELETE | the record; `Finding` |

### D.9 `Projection.Adapters.*` (9,456 lines, 21 files)

| Module | Lines | Consumers / evidence | Verdict | v3 |
|---|---:|---|---|---|
| `OssysRowsetReader.fs`; `OssysTranslation.fs`; `OssysRowsetTypes.fs`; `MetadataSnapshotRunner.fs`; `MetadataExtractionError.fs`; `MetadataContractOverrides.fs` | 1,040; 555; 479; 1,722; 164; 305 | the live OSSYS path; ~1,400 of the runner is 22 hand-written row handlers; `MetadataContractOverrides` handles real estate differences (NM-72) | KEEP-SIMPLIFIED, **optional** (§16 item 3) | `io/Ossys` |
| `outsystems_metadata_rowsets.sql` | 1,253 (SQL) | byte-identical to v1's; years of metamodel knowledge | KEEP | verbatim, as a resource |
| `OssysJsonReader.fs` | 801 | reads v1's `osm_model.json`; dies with v1 | RETIRE-AFTER-EJECT | nothing |
| `CatalogReader.fs` | 168 | reader dispatch | FOLD-INTO | the `Reader` DU (§8.1) |
| `Retry.fs` (Polly 8.5.0) | 113 | `MetadataSnapshotRunner.fs:837`; a v2 extension, not a v1 carry | KEEP | `io/SqlServer` |
| `ReadSide.fs` | 1,973 | post-eject the *primary* reader; ~400 lines reclaimable through one `readRows`; marks every table `Static` (rule 8) | KEEP-SIMPLIFIED | `io/SqlServer`, ~1,200, the marking fixed |
| `LiveProfiler.fs` | 872 | profile capture; the Twin's evidence import | KEEP | `io/SqlServer` |
| `EvidenceFingerprint.fs`; `ServerDigest.fs`; `DataIntegrityChecker.fs`; `AsyncStream.fs`; `ConnectionResolver.fs`; `SqlPolicy.fs` | 128; 124; 150; 84; 73; 56 | the one-round-trip staleness probe; `CHECKSUM_AGG`; the probes that predict what the publish proves; secret-free connections; the 300 s default timeout | KEEP | `io/SqlServer` |
| `Ingestion.fs`; `ClosureOracle.fs`; `BridgeSnapshotReader.fs` | 260; 181; 136 | transfer reads | RETIRE-AFTER-EJECT | `Move`, if ported |

### D.10 `Projection.Pipeline` (40,564 lines, 103 files)

| Module | Lines | Consumers / evidence | Verdict | v3 |
|---|---:|---|---|---|
| `Pipeline.fs` (`Compose.run*`, read, bind, seams, emit) | 3,414 | every run face; five concerns in one file | KEEP-SIMPLIFIED | ~600 lines across four verbs |
| `Config.fs`; `ConfigSchema.fs` (+ the generated 680-line schema) | 2,189; 203 | 60+ keys; A44 | DELETE | `posture.json`, ~20 keys |
| `MovementSurface.fs`; `MovementSpec.fs` | 2,825; 979 | the transfer engine's vocabulary | RETIRE-AFTER-EJECT (frozen) | nothing |
| `TransferRun.fs`; the nine `Transfer*.fs`; `PeerTransfer.fs`; `SurrogateCapture`/`PackedSurrogateRemap`/`KeymapSpill`/`CaptureJournal` | 3,402; 1,822; 546; 993 | the reverse leg's realization; the Twin calls `Transfer.runSynthetic` | RETIRE-AFTER-EJECT (frozen), one carve-out | the mint loader's ~300 lines move to `io/Twin` |
| `Bulk.fs`; `FakerRealization.fs` | 232; 295 | the mint needs a bulk loader; masked values at the boundary | KEEP | `io/Twin` |
| `CapabilitySurvey.fs`; `CapabilityRefusal.fs` | 404; 45 | a two-archetype problem; post-eject there is one archetype | RETIRE-AFTER-EJECT | nothing |
| `Estate.fs` + `EstateEvidenceStore`/`History`/`Posture`/`Remediation`/`StoreLocation` | 2,633 + 1,006 | workflow 5 is permanent; a 2,633-line board whose findings feed no automation is not | KEEP-SIMPLIFIED | `check environments`, ~500; the fingerprint gate kept |
| `Readiness.fs` | 292 | the first-promotion drift check | KEEP-SIMPLIFIED | `check environments` |
| `Compare.fs` (`LiveEnv | StoredRun | ModelFile`) | 362 | no `SsdtProject`, no `DacpacFile` (§12) | KEEP-SIMPLIFIED | `diff` with the two missing operands |
| `ModelFidelity.fs` | 1,143 | "if A's data lands in B's schema, what breaks?" | KEEP-SIMPLIFIED | `io/Publish`'s row digest, ~300 |
| `FidelityCompareRun.fs`; `FidelityProofCache.fs`; `ProofManifest.fs` | 1,110; 200; 201 | proves a transfer moved rows faithfully | RETIRE-AFTER-EJECT | nothing |
| `Deploy.fs` + `DeployParallelism`/`Feasibility`/`ConnectionString` | 1,303 + 458 | the heart of `prove`; one of two proving mechanisms (§10.2) | KEEP-SIMPLIFIED | `io/Publish`, ~850 |
| `Preflight.fs` | 856 | the CDC-readiness gate is permanent; the rest is migrate-era | KEEP-SIMPLIFIED | ~200 |
| `MigrationRun.fs`; `FullExportRun.fs` | 1,000; 326 | Octopus migrates; nothing is exported | RETIRE-AFTER-EJECT | nothing |
| `RunSpine.fs`; `Run.fs`; `RunLedger.fs`; `RunHistory.fs`; `RunEnvelope.fs` | 470; 357; 134; 47; 101 | a compiler-checked stage arc for 46 run kinds; v3 has twelve verbs | DELETE | the pull request is the record |
| `LifecycleStore.fs`; `EjectRun.fs`; `ApprovalStore.fs`; `ReportRun.fs` | 611; 60; 153; 200 | `seal`, `seal approve`, `report` | RETIRE-AFTER-EJECT | git tags and the changelog |
| `EventProjection.fs`; `LogSink.fs`; `NoticeSink.fs`; `BenchSink.fs` | 284; 1,104; 73; 75 | NDJSON events for a board; `LogSink.fs` is the ninth-largest file in the repository | DELETE | a progress line |
| `Hydration.fs`; `Source.fs`; `LiveModelRead.fs`; `ModelResolution.fs`; `CatalogResolution.fs`; `ScopedRead.fs`; `CatalogRendition.fs` | 1,205 | model acquisition | KEEP-SIMPLIFIED | `estate read` |
| the twelve `*Binding.fs` + `Binding.fs` | 2,208 | once-bound operator intent for the config | DELETE | nothing |
| the four `*Seam.fs` | 871 | skeleton/overlay separation (pillar 9) | DELETE | nothing |
| `WriteSignoff.fs`; `ActEvidence.fs` | 216; 248 | destructive emission gates | RETIRE-AFTER-EJECT | `BlockOnPossibleDataLoss` and the reviewer |
| `GoBoard.fs`; `Slice*Run.fs` | 322; 335 | `check go`, the slice verbs | RETIRE-AFTER-EJECT | nothing |
| `SyntheticLoadRun.fs` | 234 | the pre-Twin mint path | FOLD-INTO | `io/Twin` |
| `DriftRun.fs`; `ProfileCaptureRun.fs` | 34; 53 | permanent workflows | KEEP | `check drift`; `profile` |
| `CorrectionProposeRun.fs`; `CsvExportRun.fs`; `CsvReferencedPull.fs` | 47; 162; 53 | corrections and CSV | RETIRE-AFTER-EJECT | nothing |
| `PolicyDiff.fs`; `RegisteredAllTransforms.fs` | 233; 133 | diffing two policies; the totality test | DELETE | nothing |
| `DockerDaemon.fs`; `DatabaseNameGenerator.fs` | 205; 47 | the substrate; disposable database names | KEEP | `io/Twin`; `io/Publish` |
| `NameAlignment.fs`; `RenameProjection.fs`; `Ref.fs`; `SupportingScope.fs`; `SpecialCircumstancesDiagnostics.fs`; `BridgeStagingCache.fs` | 327; 87; 117; 437; 163; 333 | cutover naming and staging | RETIRE-AFTER-EJECT / DELETE | nothing |

### D.11 `Projection.Cli` (14,097 lines, 38 files)

| Module | Lines | Consumers / evidence | Verdict | v3 |
|---|---:|---|---|---|
| `Program.fs` (39 verbs) | 691 | operators | KEEP-SIMPLIFIED | thirteen verbs, ~200 |
| `Faces/Transfer.fs`; `Faces/Migrate.fs`; `Faces/Fidelity.fs` | 2,233; 668; 487 | the finite half of the job; `Transfer.fs` is the sixth-largest file in the repository | RETIRE-AFTER-EJECT | nothing |
| `Faces/Estate.fs` + `EstateBoardView`, `GoBoardView`, `TransferImpactView`, `TransferPlanView` | 1,509 | boards | KEEP-SIMPLIFIED (Estate only) | the `check environments` table |
| `Faces/Canary`, `Diff`, `Emit` | ~470 | verbs v3 keeps | KEEP-SIMPLIFIED | `prove`, `diff`, `emit` |
| `Faces/Explain`, `Export`, `Inspect`, `Operational`, `Slice`, `Synthetic`, `Approve`, `CsvExport`, `Deploy`, `Common` | ~1,460 | verbs v3 folds or drops | DELETE / RETIRE | nothing |
| `Voice.fs` (code ⇔ copy, twelve register rules); `View.fs`; `TtyRenderer.fs`, `Theme.fs`, `ProgressRenderer.fs`, `OperatorConsole.fs`, `Surface.fs`; `Watch.fs` (the `--watch` flag was deprecated 2026-06-17) | 1,602; 655; 953; 798 | terminal presentation for a consumer that reads JSON | DELETE | plain text and `--json` |
| `Navigator.fs`; `ReviewNavigator.fs`; `Intervene.fs` | 434; 495; 122 | interactive prompts, duplicated across three faces, for a team that works in Visual Studio | DELETE | nothing |
| `Comparison.fs` | 754 | rendering a delta for a human | KEEP-SIMPLIFIED | `diff`'s rendering, ~200 |
| `Query.fs` (JSONPath subset) | 125 | `--query` | DELETE | `jq` |
| `CliExit.fs` (total classifier, 0–9) | 73 | every verb; `prove.mjs` mirrors the idea with a different ladder | KEEP | the exit-code table (§8), reconciled |
| `RelaxationStore.fs`; `Shell.fs` | 296; 269 | the RELAX lane; run bracketing | DELETE | nothing |

### D.12 `Twin.*` (4,995 lines, 19 files)

| Module | Lines | Consumers / evidence | Verdict | v3 |
|---|---:|---|---|---|
| `Twin.Core/TwinConfig.fs` | 884 | a closed schema describing one local database | KEEP-SIMPLIFIED | `twin.json` |
| `Twin.Core/Evidence.fs`; `DerivedEvidence.fs` | 507; 172 | the literal-free shape tier; the zero-config floor | KEEP | `Synth` |
| `Twin.Core/Coordinate.fs`; `TwinIdentity.fs` | 135; 107 | post-eject identity; the name→key binder | KEEP | `Identity` |
| `Twin.Core/Fingerprint.fs`; `EstateDefinition.fs`; `ScenarioCompiler.fs` | 68; 88; 477 | the no-op gate and the artifact version; scenarios rewrite evidence and never generate | KEEP / KEEP-SIMPLIFIED | `io/Twin` |
| `Twin.Runtime/Runs`, `TwinDatabase` (the post-mint `WITH CHECK CHECK` trust gate), `TwinContainer`, `Mint`, `Check` (M5), `EvidenceImport`, `EstateModel`, `EstateFiles`, `Readback` | 2,130 | `twin up`'s convergence law; the model for §13 | KEEP | `io/Twin`; the evidence import wired into the bake lane |
| `Twin.Cli/Program.fs`; `Render.fs` | 427 | fourteen verbs in 225 lines | KEEP-SIMPLIFIED | `estate twin` |

### D.13 Tests, scripts, and the formal apparatus

| Item | Lines | Evidence | Verdict | v3 |
|---|---:|---|---|---|
| `AxiomTests.fs` (123 facts, 38 skips) | 1,720 | 85 live witnesses; 38 permanent placeholders | KEEP-SIMPLIFIED | twelve laws with English names, no stubs |
| `AXIOMS.md`; `PRODUCT_AXIOMS.md` | 2,243; 492 | the tests cite them; A48/T18 with English names is twelve sentences | DELETE (archive) | `LAWS.md`, generated |
| `TotalityFunctor.assertBidirectionalSubset` | — | all four consumers (capability survey, registry, Voice, manifest) are deleted | DELETE | nothing |
| `GoldenCatalog.fs` | 468 | the parity oracle | KEEP | `tests/Golden/` |
| `CatalogDiffTests.fs` (54 facts) | 1,156 | v3's centre deserves its combinatorics | KEEP | `Delta`'s tests |
| `EphemeralContainerFixture` / `IsolatedContainerFixture` | — | `sp_cdc_enable_db` is instance-wide | KEEP | the CDC class isolated |
| `SamplePr*Tests.fs` (13 classes, 41 facts) | — | the only executable proof of the catalog's claims | KEEP | data under `tests/Golden/changes/`; one parameterised test |
| `Projection.Tests` (332 files); `Projection.Tests.Integration` (69 files); `Twin.Tests.Integration` (20 files) | 86,371; 21,742; 6,702 | the pure pool; the Docker pools | KEEP-SIMPLIFIED | ~7,500 pure; ~8,500 fixture (§6.5) |
| `scripts/test.sh`; `warm-sql.sh`; `run-analyzers.sh` | — | running the pools together OOM-kills the host (rule 1) | KEEP | `ci/` |
| `scripts/perf-gate.sh` + `bench/`; `lint-discipline.sh` (609 lines, 566 exemptions); `verifiability-gate.sh` | — | a verdict void under load; a lint with an exemption every 210 lines; bucket honesty for buckets that go | DELETE | the scale lane; the dependency laws |
| `scripts/matrix-status.sh` → `NORTH_STAR.matrix.generated.md` | — | the one generated document that works | KEEP-SIMPLIFIED | the model for `LAWS.md` |
| `examples/`; `audit-2026-07-17-evidence/` (246 findings) | — | the parity audit's register | archive | read once for §4.3 |

### D.14 `ssdt-agent/`

Summarised in §11.5 and §9.2; the per-part table is short enough to repeat.

| Part | Files / lines | Verdict | v3 |
|---|---:|---|---|
| 45 op skills | 45 / 4,961 | KEEP | `knowledge/ops/`, ~75 lines each |
| 6 `_index` skills | 6 / 750 | KEEP | `knowledge/shared/` |
| 4 review skills | 4 / 495 | KEEP-SIMPLIFIED | `reviewing.md` |
| 10 top-level authoring skills | 10 / 2,289 | KEEP-SIMPLIFIED | `authoring.md` and its reference |
| `skills/operations/*.md` (family overviews) | 10 / 381 | DELETE | the ops README |
| agents (`intake`, `change-author`, `reviewer`) | 3 / 743 | KEEP-SIMPLIFIED | two skills; personas are phases |
| `sample-prs/` | 50 / 3,755 | KEEP-SIMPLIFIED | a dozen shapes |
| `self-test/` | 9 / 2,493 | DELETE | a handful of conversation cases |
| `estate/` ledgers | 8 / 272 | KEEP | `knowledge/ledgers/`, plus `cdc-tracked.md` |
| `FINDINGS_AND_CHANGES.md`; `THE_DECISION_TREE.md`; `THE_RECORD*.md`; `PORTABILITY.md`; `PROVING_PATH_WINDOWS.md` | 502; 215; 451; 94; 231 | KEEP; KEEP; KEEP-SIMPLIFIED; KEEP; DELETE | `findings.md`; `authoring.md`; `record.md`; `knowledge/`; nothing (the `.bak` route put real data on laptops) |
| the program documents (§9.2) | 9 / ~2,500 | DELETE (archive) | nothing |
| `ARCHITECTURE_REVIEW_2026_08_28.md` | 1 / 357 | KEEP until v3 lands, then archive | Appendix F |
| `scripts/prove.mjs`; `bake.mjs`; `inflight-check.mjs`; `ssdt-agent-gates.mjs`; `ssdt-agent-package.mjs` | 427; 251; 145; 370; 682 | the shipped halves of Layers 1, 0 and 3; the four-surface tax | KEEP-SIMPLIFIED, as verbs | `prove`; `twin bake`; `check inflight`; three tests; `knowledge package` |
| `proving-ground/` | 8 tables, ~35 rows | KEEP-SIMPLIFIED | the golden schema; the Twin fills it |
| `copilot-package/`; `.claude/` | 80 / 1,210; 73 / 1,405 | KEEP (generated) | regenerated, never hand-edited |
| `pipelines/`; `pr-template/` | —; 1 / 87 | KEEP | `ci/`; the record template |

---

## Appendix E — The v1 carry ledger

Source: the parity matrix's NOT-MAPPED and V1-SUNSET rows and the v1 trunk itself, tested
against one question: does a post-eject workflow (author, review, gate, substrate, watch,
remember) need it? Sizes are `wc -l` on `src/` at `4e844fc`.

| v1 capability | v1 location · lines | What v2 did | Verdict | v3 |
|---|---|---|---|---|
| DMM three-lens comparator (`IDmmLens<T>`, `DmmComparator`; SMO / ScriptDom / SSDT-project lenses) | `Osm.Dmm/` · 8 files · 2,201 | NOT-MAPPED; the canary covers the deployed↔emitted pair; `compare` covers `LiveEnv`/`StoredRun`/`ModelFile` | **LEAVE, carry the two missing operands** | `estate diff` over any two of the four readers; `io/Ssdt` reads a project and a dacpac through `TSqlModel` |
| compare-feature flags (`DmmComparisonFeatures`) | `Osm.Dmm/` · 14 | not carried | **CARRY** | `estate diff --only columns,keys,indexes,references,checks,properties` |
| transient retry | not in v1 (matrix row 34: "implicit delegation to caller"); no Polly reference in `src/` | v2 built `Retry.fs` (113, Polly 8.5.0), wired at `MetadataSnapshotRunner.fs:837` | **corrected: v2's, keep v2's** | `io/SqlServer`; mid-stream transients after the reader is open are not retried, and the gap is named |
| tunable command timeout (`OSM_CLI_SQL_COMMAND_TIMEOUT`) | `Osm.Pipeline/Configuration/CliConfigurationService.cs` | carried: `CommandTimeoutPolicy`, 300 s default, `PROJECTION_COMMAND_TIMEOUT_SEC` | **already carried** | `io/SqlServer` |
| load-harness DMV probes (`dm_os_wait_stats` filtered to LCK/PAGEIOLATCH/WRITELOG/CXPACKET, `dm_tran_locks`, top-20 fragmentation) | `Osm.LoadHarness/` · 6 files · 572 | NOT-MAPPED; v2 has no DMV probe | **LEAVE** | the scale lane measures wall-clock on the Twin; wait statistics return only if a scale finding needs them |
| UAT-users remap (8-step pipeline; `UatUsersVerifier`, `FkCatalogCompletenessVerifier`, `TransformationMapVerifier`, `SqlSafetyAnalyzer`) | `Osm.Pipeline/UatUsers/` · 42 files · ~5,050 | carried as `UserFkReflowPass` + `UserRemap` + `UserIdentity` (795), a smaller shape | **LEAVE (cutover)** | `Move`, if the wing is ported; otherwise `archive/v1` and `archive/v2` |
| multi-environment consensus profiling (vote across dev/uat/prod) | `MultiEnvironmentConstraintConsensus.cs` 540 + `MultiEnvironmentProfileReport.cs` 645 + the multi-target profiler | carried per environment; the vote was not | **LEAVE, as a finding** | `estate check evidence --targets …` reports per-environment violations; the record shows the disagreement; no vote |
| evidence cache manifest with ten invalidation reasons | `Osm.Pipeline/Evidence/` · 1,499 | carried as `EvidenceCache.fs` (366) + `EvidenceFingerprint.fs` (128) | **LEAVE** | the fingerprint's one round-trip answers staleness; `Evidence.Fingerprint` (§7.4) |
| supplemental Users model | `config/supplemental/ossys-user.json` 341 + `OutSystemsInternalModel.cs` 285 | not carried | **CARRY** | a resource of `io/Ossys`; a table in `tests/Golden/` |
| circular-dependency allowlist + `EntityDependencySorter` (Kahn + SCC + auto-resolution + manual map + alphabetical fallback) | `Osm.Emission/Seeds/EntityDependencySorter.cs` · 2,188 | carried and superseded: `TopologicalOrderPass` v7 (977) + `CycleResolution` (534) | **LEAVE** | `Order` with `CyclePolicy.Manual of order` as the allowlist |
| three-option remediation generator (UPDATE to default / DELETE / SELECT offenders) | `Osm.Validation/Tightening/RemediationQueryBuilder.cs` · 73 | carried differently: `RemediationEmitter` (561) for the estate repair lane | **CARRY, extended** | `Decide` returns the three statements with every `AskOperator`, for NULLs, orphans and duplicates; the record prints them |
| HTML report and browser launch (`--open-report`) | `Osm.Cli/PipelineReportLauncher.cs` 711 + `PolicyCommandFactory.cs` 1,194 | not carried; v2 rendered to a TTY | **LEAVE** | the pull request is the report |
| Spectre.Console progress | `Osm.Cli/` · 67 + 35 | carried and grown to `Watch.fs` 798 + renderers 669 | **LEAVE** | a progress line; the runs are a dacpac build and a publish |
| table sampling policy | `Osm.Pipeline/Profiling/TableSamplingPolicy.cs` · ~61 | carried: `SamplingPolicy.fs` (57), consumed by the Twin's evidence import | **already carried** | `Evidence.Probe` |
| operator-debugging telemetry: `SqlMetadataLog`, last-row failure context, `--sql-metadata-out` | `Osm.Pipeline/Sql/SqlMetadataLog.cs` + `SqlMetadataDiagnosticsWriter` | NOT-MAPPED (matrix row 30): one `ValidationError`, no partial-state context | **CARRY** | `Refusal.Where` on every reader failure |
| `TopologicalOrderingValidator` (proves a *given* order is topological) | `Osm.Pipeline/Orchestration/` · 416 | v2 computes the order, never validates a supplied one | **CARRY, as a test** | a property on `Order` |
| CIR JSON Schema (`additionalProperties: false` at every level, validated non-blockingly) | `schema/cir-v1.json` · 422 | v2 generated a 680-line *config* schema; hand-written catalog codecs | **LEAVE** | the schema is the F# type; `io/Json`'s derived codec |
| three-tier type-mapping policy (on-disk → external → attribute; ~11 strategies; `config/type-mapping.default.json` 239) | `Osm.Smo/TypeMapping*.cs` · 787 | carried as `OssysTypeMapping` (128) + `SqlStorageType` (264) + `SqlTypeCorrespondence` (127) | **already carried** | `SqlType`'s tables |
| the three tightening modes (`Cautious` / `EvidenceGated` / `Aggressive`; `NullBudget` 0.0) | `Osm.Domain/Configuration/TighteningOptions.cs` | carried as `TighteningDirection` (`EvidenceDriven`/`RelaxationOnly`) + per-intervention config | **already carried, better** | `Policy` (§7.5); v1's mode × signal table becomes a table test |
| the OSSYS rowset SQL (23 rowsets) | `src/AdvancedSql/outsystems_metadata_rowsets.sql` · 1,253 | carried byte-identically as an embedded resource | **already carried** | verbatim, in `io/Ossys` while it exists, in the archive otherwise |
| fixture-based emission snapshots (`tests/Fixtures/emission/{edge-case, edge-case-rename, edge-case-untrusted}`) | `tests/Fixtures/` · ~26,000 lines of JSON/SQL | v2 has `GoldenCatalog.fs` (468) and a golden bundle | **LEAVE, used once** | the fourth parity check (§14.1) for the OSSYS reader; not ported |
| the five v1 bugs v2 fixed (reference-by-name mis-join; FK rules dropped to NO ACTION; untrusted FK left DISABLED; per-column COLLATE dropped; XML widened to NVARCHAR(MAX); no CREATE SCHEMA) | various | fixed in v2 (parity audit) | **stay fixed** | law 2 and the golden schema carry each as a case |

Five carries, two carries as tests, four already carried, the rest left. The sentence that
matters is §12's: v2 dropped almost nothing the post-eject job needs, and what it dropped that
matters most is what v3 is built around.

---

## Appendix F — Reading list, and how this document was made

### F.1 Seven documents worth reading in the archive

In order of return on the hour. Each is named because it carries a finding this document
depends on and does not restate.

1. `ssdt-agent/ARCHITECTURE_REVIEW_2026_08_28.md` (357 lines) — v3's brief: "over-documented
   and under-mechanized", the four missing mechanisms, the four-layer form factor.
2. `ssdt-agent/FINDINGS_AND_CHANGES.md` (502) — F1–F20, the engine facts proven against a live
   SQL Server, with the database names as receipts. The most valuable file in either generation.
3. `sidecar/projection/CRYSTALLINE_FORM.md` (305) — the adversarial audit that tested its own
   three boldest collapses and rejected two; the Intent/Quotient discriminating input; the
   measured 5% reclaim ceiling on Core.
4. `sidecar/projection/AUDIT_2026_07_17_V1_V2_PARITY.md` (563) — the highest-confidence
   empirical comparison of the two engines, with the 246-finding register beside it.
5. `ssdt-agent/ASSESSMENT_2026_08_24.md` (345) — the data-blind guard explained from the
   engine's side.
6. `sidecar/projection/THE_TWIN.md` and `THE_SYNTHETIC_DATA_DESIGN.md` (619 together) — the
   substrate's charter and its five laws, the model §13 follows.
7. `sidecar/projection/CLAUDE.md` §4 — the fifteen survival rules, each of which cost an agent
   real time.

### F.2 How this document was made

The repository was read in three passes on 2026-09-17 against `4e844fc`. Twenty-eight
ingestion digests covered every directory and every document family; four roll-ups
consolidated them by region (v1, the v2 kernel, the v2 shell, the v2 document history); a
planning pass critiqued the outline and the first drafts, produced the deletion and carry
ledgers, checked the size budgets bottom-up, and named the risks the outline had not. The
author read the load-bearing sources directly (the kernel types, the statement DU, the
decision tables, the review, the findings, the decision tree, the record forms, the estate
ledgers, the proof lane, the sample records) and re-measured every number that a section
depends on. Appendix B records where the measurements disagreed with the documents or the
passes, and the pattern is the one the document-history roll-up named: *directionally honest,
numerically loose*. Trust what the corpus says is wrong with itself; verify what it says the
wrongness measures to.
