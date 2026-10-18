# Prompt: write the v3 blueprint

> Paste this to start the session:
>
> Use a workflow to write the v3 blueprint. Follow `V3_BLUEPRINT_PROMPT.md` (on `main` once pull
> request #705 merges; until then on branch `claude/v3-architecture-design-sl95mx`).

---

## 0. The job, and how it is judged

Write one document, `V3_BLUEPRINT.md`. It is the first thing an implementing agent reads. It
places every invariant and every use case of this repository onto a code architecture whose core
is shaped by the surfaces the system touches. The use case rides on that core as a small,
replaceable layer.

The blueprint is judged by one test. The owner hands it, with this repository, to an agent
working inside the estate's network. That agent has:

- the estate repository;
- read-only access to the environments;
- Visual Studio with Copilot, or Claude Code;
- perhaps no Docker and no package feed.

Without this conversation, the agent must know how to move the codebase forward:

- what to extract from v1, v2 and the ssdt-agent tree;
- what to rewrite, and what to delete;
- in what order, and against which tests;
- until a C# engine with an MCP surface does the whole lifecycle in about one tenth of today's
  code.

§8 turns that test into questions you put to fresh agents. The blueprint is done when they pass.

## 1. The direction, in the owner's words and as rules

The owner asked for a system that is:

- "ultra-empowered, ultra-powerful, generic to the implementation surface instead of the use
  case, yet able to then accomplish the use case in 1/10th of the lines of code";
- written in C# with MCP, "highly extensible to future use cases", with "the core super rock
  solid".

Earlier, the owner put it as "a hand-in-glove fit, not purpose-built to a given directive, but
agnostic and blanketed to the source substrate, with a consumption that marries it to the needed
directive". As design rules:

1. **The core is shaped by the surfaces, not the use case.** Its nouns come from the surfaces'
   own self-descriptions: DacFx's model metadata, SQL Server's catalog, git's objects, MCP's
   tools, resources and prompts. They are read whole and bound late (`V3_MILESTONES.md` §2). No
   OutSystems noun, environment name or operation name appears in the core.
2. **The use case is a pack.** A pack is data, plus small lenses that compose core capabilities.
   The data covers operations as recognition patterns, the claim table, the posture, the record
   register and the knowledge. A new use case is a new pack, and the core does not change.
3. **One definition per capability, many faces.** One verb table generates the CLI, the MCP
   tools, resources and prompts, the JSON contracts, and the reference docs.
4. **Safety is a type, not a rule.** A named target is read-only, and only a `Copy` can be
   written. A fact read from a real environment is an integer. No verb, tool or pack can
   construct a way around this.
5. **Every invariant is structural or witnessed.** Either a type makes breaking it
   unrepresentable, or a named test fails when it breaks. An invariant held only in prose is a
   defect.
6. **The line budget is a test,** not a hope.

## 2. Where things stand

### 2.1 Read in this order

- `AGENTS.md` (repository rules), `architecture-guardrails.md`, and
  `notes/meta/directory-map.md`.
- `V3_MILESTONES.md`, the current plan:
  - §1, the facts measured on .NET 10 with DacFx 170.5.96 and Docker SQL Server;
  - §2, read whole, bind late, with the surface and lens registers;
  - §3, the five inputs, the two instruments, and the branch as a value, with laws P and T;
  - §6–§14, the milestones;
  - §17, the decisions and prerequisites.
- `V3_ARCHITECTURE.md`:
  - §6, the layout, budgets and C# encoding (§6.6);
  - §7, the kernel;
  - §8, the verbs and their exit codes;
  - §13, the laws;
  - Appendix C, the 45 operations;
  - Appendix D, the v2 deletion ledger;
  - Appendix E, the v1 carry ledger.
- `V3_INSTRUCTION_ARCHITECTURE.md`:
  - §4, the values register;
  - §9, the agentic interface, where §9.3 leaves MCP as an open question;
  - §10, the tests that keep prose true.
- `LIFECYCLE_BACKPORT_PROMPT.md`: R1–R28, each with its acceptance check.
- `V3_BUILD_PROMPT.md`: how building is meant to run.

### 2.2 Code to mine

These are non-blank line counts, measured at `438a8b0`:

| Source | Product lines | Test lines |
|---|---:|---:|
| v1 (C#), `src/Osm.*` | 66,839 | 45,114 |
| v2 (F#), `sidecar/projection/src` | 111,393 | 106,792 |
| ssdt-agent scripts | 1,723 | — |
| **Total** | **≈ 180,000** | **≈ 152,000** |

One tenth is about 18,000 product lines and 15,000 test lines. `V3_MILESTONES.md` already budgets
14,900 product lines for kernel, io and CLI, without MCP.

Three parts of the tree matter most:

- v2's `AXIOMS.md` and `AxiomTests.fs`.
- `sidecar/projection/ssdt-agent/`, the knowledge tree. Its `skills/_index/*` (constraint is a
  claim, the tightening class, identity and the refactorlog, idempotent seeds, multi-phase) and
  `FINDINGS_AND_CHANGES.md` hold measured engine behavior. That behavior is a fact about the
  surface, not about the use case.
- v1's `Osm.Smo`, `Osm.Pipeline` and `Osm.Validation`, and the contracts their tests pin.

### 2.3 The central question the blueprint settles

`V3_ARCHITECTURE.md` §7 keeps a kernel inherited from the emission era:

- a catalog of kinds, attributes and references;
- tightening tables;
- statements;
- σ;
- a record type.

`V3_MILESTONES.md` §2 already moved reading onto the surfaces: DacFx's model walked through its
own `ModelTypeClass` properties, the deploy script's own guards, and the metamodel through an
allowlist.

Decide, type by type and verb by verb, which of these each becomes:

- core;
- pack data;
- an adapter;
- gone, because a surface already does the work.

Show the evidence for each decision.

### 2.4 A starting hypothesis, to test and not to adopt

- **The core nouns:**
  - **Element:** a kind, properties and relationships, named by the surface's metadata.
  - **Model:** the elements at a ref.
  - **Delta:** property-level changes, plus an identity map from the refactorlog.
  - **Fact:** an integer from an allowlisted probe.
  - **Claim:** a change pattern, a predicate over facts, and the branch it selects.
  - **Branch.**
  - **Prediction and Proof,** joined by `Transfers`.
  - **Receipt:** engine, profile, target, script hash and time.
  - **Record** and **Gate.**
  - **Capability:** `Named` or `Copy`.
- **The verbs** are compositions of these nouns.
- **The 45 operations** are recognition patterns over a Delta.
- **The platform check** is a lens over a second Model, read from the metamodel.

Refute or refine this against the ledgers. Do not inherit it.

## 3. What `V3_BLUEPRINT.md` contains

You may reorder the sections. Each one must achieve what is written here.

1. **The answer on one page.** One diagram (core, surface ports, adapters, packs, faces) and the
   six rules.
2. **The surface register, deepened.** One entry per surface:
   - **SQL Server:** the catalog views, the DMVs, permissions, CDC, and query execution under a
     read-only principal.
   - **DacFx:** build, load, compare, script, deploy report, publish, `ModelTypeClass` metadata,
     and the deploy options.
   - **The SSDT project:** the `.sqlproj`, the refactorlog, the pre- and post-deploy scripts,
     and the publish profiles.
   - **The substrate:** a Docker SQL Server or LocalDB, and the copy registry.
   - **git:** refs, trees, diffs, tags, LFS.
   - **The git host:** pull-request bodies, checks and branch policies, on GitHub and Azure
     DevOps.
   - **The deployment tool:** its release steps, and the engine and profile of its publish step.
   - **The application platform's metamodel:** the OutSystems OSSYS tables, through an
     allowlist.
   - **MCP:** tools, resources, prompts, the transport, and host policies.
   - **Agent instruction formats:** Copilot and Claude Code.

   Each entry states:

   - what the surface says about itself;
   - what the system reads and writes;
   - the fidelity bar: what is modeled exactly and what is passed through;
   - the port, as a C# interface sketch;
   - the adapter;
   - the oracle test;
   - the spike that confirms the entry inside the estate's network;
   - its lines.
3. **The core.** The types and operations, each justified by the invariants it carries, and
   their C# encoding:
   - records and closed hierarchies;
   - smart constructors and result types;
   - no nulls;
   - no I/O, and no `Task` in a kernel signature.

   Say what the core refuses to know.
4. **The invariant ledger.** Merge these sources into one ID space:
   - the laws (`V3_ARCHITECTURE.md`; `V3_MILESTONES.md` P, T, 1′–3′, 5′, 11′ and 12′);
   - R1–R28;
   - the values register;
   - the instruction tests;
   - the v2 axioms that survive;
   - the v1 contracts that survive;
   - the engine facts in the ssdt-agent `_index` skills and the F-numbered findings.

   Each row carries:

   - ID and statement;
   - kind: surface fact, core law, pack rule or process;
   - mechanism: type, constructor, property test, oracle or gate;
   - module, and test name;
   - the use cases it serves;
   - sources, cited as `file:line`.

   List the duplicates you merged and the contradictions you resolved.
5. **The use-case catalog.** The actors:
   - the author and the reviewer;
   - the operator;
   - the deployment step;
   - the person who refreshes the platform;
   - an MCP client agent;
   - the backport agent.

   Cross them with the stations: author, read, diff, classify, predict, prove, record, gate,
   deploy, converge, refresh, report. Then add the 45 operations, by class.

   Write each use case as a composition: core capabilities, pack data and adapters, the verb or
   tool that exposes it, and its acceptance test. At least these:

   - make-mandatory on a populated table;
   - rename-attribute with a refactorlog entry;
   - create-fk-orphan;
   - extract-to-lookup, as a multi-phase program;
   - delete-entity;
   - a compound request;
   - drift before a deploy;
   - the platform mapping check after a refresh.
6. **The packs.**
   - The lifecycle pack, as files with schemas.
   - The pack contract:
     - what a pack may contribute: recognition patterns, claim rows, probes from the allowlist,
       posture, lenses, knowledge, record sections;
     - what it may not do: I/O, a new surface access, a write to a named target.
   - Three worked future use cases, each added without touching the core. Name the files each
     adds and its lines. Candidates (choose better ones if they exist):
     - a second estate on the same platform;
     - governing seed data across environments;
     - a downstream consumer's contract, for SSIS or CDC subscribers.
7. **The faces.**
   - **The verb table.** One row per capability: name, inputs, outputs, exit codes, the
     capability it needs, and its MCP tool, resource or prompt.
   - **How the faces are generated** from that table: the CLI, MCP, the JSON contracts and the
     docs.
   - **The MCP design:**
     - the tools, resources and prompts;
     - the transport;
     - what happens when a host's policy disables MCP: the CLI fallback, with identical
       semantics;
     - how the server proves it cannot write to a named target, and cannot return a row value
       from a real environment.
8. **The extraction ledger.** Every component of v1, v2 and the ssdt-agent tree that the
   blueprint keeps. For each:
   - source files and target module;
   - method: port verbatim, port and simplify, replace with data, replace with a surface call,
     or delete;
   - the oracle that proves it: golden bytes, the 45 verdicts, v1's edge-case fixtures, or
     recorded outputs;
   - its order.

   Reconcile with Appendices D and E, and say where you differ and why.
9. **The build path inside the estate's network.**
   - **What the agent needs, and how each arrives through git alone:** the toolchain; the
     packages, as a vendored folder in LFS plus a local NuGet source; the published DacFx tool
     folder; the pinned SQL Server image or LocalDB.
   - **The first hour, the first day and the first week,** with commands.
   - **Spikes S1–S8,** with exact commands.
   - **What it may read:** schema, the metamodel allowlist, counts and distributions. **What it
     never reads:** row values.
   - **The milestones, re-cut** around the core, the packs and MCP. Each has an exit a person can
     check.
   - **What runs anywhere, and what needs the network.**
10. **The budget.**
    - The baseline, re-measured, with the command.
    - The one-tenth target.
    - Ceilings per module that sum under it: core, each adapter, the faces, the lifecycle pack,
      tests, and data counted separately.
    - Why each ceiling is credible: which surface does the work instead.
    - The budget test.
11. **Risks, unknowns, and the decisions only the owner makes.** Each has the default that lets
    work proceed. Carry forward `V3_MILESTONES.md` §17, and add MCP and the pack contract.
12. **What this supersedes.** A table of the sections in the four design documents that the
    blueprint replaces or amends. In those documents, add one-line pointers; do not rewrite
    them.

**Appendix.** The ledgers (invariants, surfaces, use cases, verbs, extraction) as fenced YAML
with stable IDs, so M0 can turn them into tests. Add a paste line and first instructions for an
agent inside the estate's network.

## 4. How to work

1. **Extract.** Run these in parallel; small, fast reader agents are fine here.
   - Shard the sources.
   - Each reader writes JSONL rows (invariant, use case, surface capability or component), each
     with a `file:line` citation.
   - Validate every row against its file. Small readers over-report, so decide rows in groups,
     not one by one.
   - The Read tool refuses a call over 25,000 tokens, so readers take about 60 KB per call.
   - Earlier sessions capped subagent concurrency at 20. Workflow concurrency is
     min(16, CPUs − 2).
2. **Synthesize.** You merge, deduplicate and decide, and write the blueprint.
3. **Measure; claim nothing from memory.**
   - Re-measure the line counts.
   - Confirm API facts with spikes in the scratchpad, where Docker SQL Server works:
     - the C# MCP SDK's current package, version and server API on .NET 10, using a stdio
       server exposing one verb that answers `initialize`, `tools/list` and `tools/call`;
     - the DacFx calls each port names;
     - anything `V3_MILESTONES.md` §1 does not already cover.
   - Record each result as a measured fact with its command, the way that §1 does.
4. **Cold read** (§8): ask fresh agents who have only the repository.
5. **Adversarial review.** A fresh agent attacks the blueprint for:
   - a contradiction with the five design documents;
   - an invariant with no mechanism;
   - a use case that needs the core to change;
   - a face that can write to a named target or return a value;
   - a budget that does not add up;
   - a step inside the estate's network that needs a feed, or a person §3.11 does not name.
6. **Fix and repeat** steps 4 and 5 until clean. Then commit, push, and update the pull request.

## 5. Rules

- **Planning artifacts only.** Spikes stay in the scratchpad and are cited, not committed.
  Implementation starts at M0, under `V3_BUILD_PROMPT.md`.
- **Generic words.** Write for the use case. Use the estate, the estate's network and the
  deployment tool; name no employer, team or person. Name Octopus and Azure DevOps only where the
  plan builds against them. Put no model identifiers in any committed file, commit or pull
  request.
- **Density.**
  - Write for an agent that never saw this conversation.
  - Use plain declarative sentences, and prefer tables to prose.
  - Every claim cites a file, a measurement or a decision.
  - Keep the blueprint under about 2,000 lines. Ledgers that push past that go into the YAML
    appendix.
- **Stable numbering.** Section numbers, requirement IDs and work-package IDs in the other
  documents stay stable. If the milestones are re-cut, amend `V3_MILESTONES.md` in place; keep IDs
  wherever the scope is unchanged. Update `V3_BUILD_PROMPT.md`'s read order and wave inputs.
- **Standing constraints:**
  - no environment data on developer machines, only counts and distributions;
  - read-only access to named environments;
  - only a `Copy` is published;
  - git is the only store, with one fetch: the SQL Server image.
- **A shared branch.** Another session may be sweeping the documents for employer-specific
  wording on this branch. Fetch and merge before every push; never rewrite history.

## 6. Done means

- **The blueprint is committed.** `V3_BLUEPRINT.md` is committed with its ledgers.
- **The other documents point to it.** Each carries one-line pointers to what the blueprint
  supersedes, and `V3_BUILD_PROMPT.md` reads the blueprint first and names the MCP lane.
- **The cold read passes.** Two fresh agents in a row pass the questions in §8.
- **The review is clean.** The adversarial review leaves no open blocking finding.
- **The pull request is updated.** It states the shape, the measured facts, and the decisions
  left to the owner.

## 7. Report back

Send the owner a short note:

- the shape, in one paragraph;
- the budget;
- the decisions they must make;
- the paste line for the agent inside the estate's network.

## 8. The cold-read questions

Give a fresh agent only the repository, and ask it to answer from `V3_BLUEPRINT.md` with section
and ID citations:

1. **The start.** You work inside the estate's network, with this repository and the estate
   repository. What do you do in your first hour, first day and first week? Name the commands
   and files.
2. **Placing invariants.** For five invariant IDs drawn at random: where does each live, and
   which test fails if it breaks?
3. **A new use case.** Add a use case the blueprint does not list; the reviewer picks it. Which
   files change? Show that the core does not.
4. **Porting.** For two components drawn at random: which v1 or v2 files do you read to port
   it, and which oracle proves the port?
5. **Owner decisions.** What can you not do without a person, and what default lets you
   continue meanwhile?
6. **The budget.** What is the line ceiling of two modules drawn at random, and why does the
   total stay under one tenth of today's product code?
7. **MCP safety.** Which MCP tools exist, and what can each touch? Why can none write to a named
   environment or return a row value from a real one? What happens when the host disables MCP?
8. **Supplies.** What do you need that the network may not have, and how does each arrive
   through git alone?

A question passes when the answer is specific, correct and cited, and needed nothing outside the
repository.
