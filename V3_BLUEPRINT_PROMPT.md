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

The blueprint is judged by one test. The operator hands it, with this repository, to an agent
working inside the environments' network. That agent has:

- the SSDT repository;
- read-only access to the environments its directory groups allow: Dev for a developer, QA and
  UAT for a dev lead, and Prod for nobody;
- Visual Studio with Copilot, whose MCP policy is enabled, or Claude Code;
- LocalDB 16.x and no Docker, and perhaps no package feed.

Without this conversation, the agent must know how to move the codebase forward:

- what to extract from v1, v2, the ssdt-agent tree and the Facsimile's specification;
- what to rewrite, and what to delete;
- in what order, and against which tests;
- until a C# engine with an MCP surface does the whole lifecycle in about one tenth of today's
  code.

§8 turns that test into questions you put to fresh agents. The blueprint is done when they pass.

## 1. The direction, in the operator's words and as rules

The operator asked for a system that is:

- "ultra-empowered, ultra-powerful, generic to the implementation surface instead of the use
  case, yet able to then accomplish the use case in 1/10th of the lines of code";
- written in C# with MCP, "highly extensible to future use cases", with "the core super rock
  solid".

Earlier, the operator put it as "a hand-in-glove fit, not purpose-built to a given directive, but
agnostic and blanketed to the source substrate, with a consumption that marries it to the needed
directive". As design rules:

1. **The core is shaped by the surfaces, not the use case.** Its nouns come from the surfaces'
   own self-descriptions: DacFx's model metadata, SQL Server's catalog, git's objects, MCP's
   tools. They are read whole and bound late (`V3_MILESTONES.md` §2). No OutSystems noun,
   environment name or operation name appears in the core.
2. **The use case is a pack.** A pack is data, plus small lenses that compose core capabilities.
   The data covers the operations as recognition patterns, the precondition vocabulary (the
   Facsimile's table from a plan's statement to a precondition, its count and its witness), the
   environments file, the record register and the knowledge. A new use case is a new pack, and
   the core does not change.
3. **One definition per capability, many faces.** One verb table generates the CLI, the MCP
   tools, the JSON contracts and the reference docs. MCP resources and prompts appear only where
   the blueprint shows a consumer: the Facsimile's design has none, because knowledge lives in the
   skill files (`archive/v2/ssdt-agent/facsimile/TARGET.md` §8).
4. **Safety is a type, not a rule.** A named environment is read-only, and only a `Copy` can be
   written. A fact read from a real environment is an integer, unless the operator admits another
   disclosure class (`V3_MILESTONES.md` §17 item 24). No verb, tool or pack can construct a way
   around this.
5. **Every invariant is structural or witnessed.** Either a type makes breaking it
   unrepresentable, or a named test fails when it breaks. An invariant held only in prose is a
   defect.
6. **The line budget is a test,** not a hope.

## 2. Where things stand

### 2.1 What exists

- **v3 is being built on this branch.** M0 and M1 are built: `kernel/`, `io/`, `cli/` and
  `tests/`, with fast tests that hold the documents to their manifest, budgets, citations and
  register (pull request #704, merged here). M2 waits for the operator (`NEXT.md`). The operator's
  rulings N1 to N23 (`DECISIONS.md`, 2026-09-25) settle the names: the tool is `dbchange`;
  `Precondition`, `PreconditionState`, `ExistingData` and `AppliesTo` keep their names; the local
  server is `LocalServer`; the environments file is `dbchange/environments.json`. The blueprint
  uses these names, and proposes a rename only with the evidence a ruling needs.
- **The Facsimile**, the Twin's successor (pull request #706, merged here), is specified
  test-first at `archive/v2/ssdt-agent/facsimile/`: invariants in nine families, a case catalog
  held by a lint, a target realization, and a handoff into v3. `V3_MILESTONES.md` §3.1 reconciles
  it with the plan, and M3 is re-cut around its planter, pending the operator (§17 item 20).
- **Settled by the operator**, and not to be reopened (`V3_MILESTONES.md` §17, after the table):
  C#; MCP as a first-class surface with the CLI as its floor; LocalDB 16.x on the laptops and no
  Docker in the environments' network; who reads which environment; two principals, named in no
  repository, keep the tool; the Twin stays in service until parity and one release cycle side by
  side.
- **The vocabulary sweep** (pull request #707, merged here) made every document tool-neutral and
  employer-neutral. Keep its words.

### 2.2 Read in this order

- `AGENTS.md`, `NEXT.md`, `DECISIONS.md` (the rulings of 2026-09-25 and the lines of 2026-10-19),
  `VALUES.md` and `LAWS.md`.
- `V3_MILESTONES.md`, the current plan:
  - §1, the facts measured on .NET 10 with DacFx 170.5.96, and what was learned since;
  - §2, read each source whole and bind it to a verb late: the sources, their adapters and the
    verb table;
  - §3, the five inputs, prediction and proof, `ExistingData`; §3.1, the Facsimile;
  - §4, what the plan changes in the design documents, rows 19 to 22 among them;
  - §6–§14, the milestones;
  - §17, the decisions, the prerequisites, and what is settled.
- `archive/v2/ssdt-agent/facsimile/HANDOFF.md`, then the reading order in its `README.md`:
  `ARCHITECTURE.md`, `INVARIANTS.md`, `cases/` (with `cases/90-traceability.md`, generated) and
  `TARGET.md`. Its receipts are in `archive/v2/TWIN_SUCCESSION.md`.
- `V3_ARCHITECTURE.md`:
  - §6, the layout, budgets and C# encoding (§6.6);
  - §7, the kernel;
  - §8, the verbs and their exit codes;
  - §13, the laws;
  - Appendix C, the 45 operations;
  - Appendix D, the v2 deletion ledger, as the Facsimile amends D.12;
  - Appendix E, the v1 carry ledger.
- `V3_INSTRUCTION_ARCHITECTURE.md`:
  - §4, the values register;
  - §9, the agentic interface (§9.3: MCP, settled 2026-10-18);
  - §10, the tests that keep prose true.
- `LIFECYCLE_BACKPORT_PROMPT.md`: R1–R28, each with its acceptance check.
- `V3_BUILD_PROMPT.md`: how building runs.

### 2.3 Code to mine

These are non-blank line counts at `138e037`, from the command below:

| Source | Product lines | Test lines |
|---|---:|---:|
| v1 (C#), `archive/v1/src/Osm.*` | 66,839 | 45,114 |
| v2 (F#), `archive/v2/src` | 111,394 | 106,799 |
| ssdt-agent scripts, `archive/v2/ssdt-agent/scripts` | 1,725 | — |
| **Today's product: v1, v2 and the scripts** | **≈ 180,000** | **≈ 152,000** |
| v3 so far (M0 and M1): `kernel`, `io`, `cli`; `tests` without `tests/Golden` | 7,610 | 13,002 |

```bash
nb(){ git ls-files -- "$@" | xargs -r grep -cv '^[[:space:]]*$' | awk -F: '{s+=$NF} END {print s+0}'; }
nb 'archive/v1/src/Osm.*/**/*.cs' 'archive/v1/src/Osm.*/*.cs'   # v1 tests: 'archive/v1/tests/**/*.cs'
nb 'archive/v2/src/**/*.fs' 'archive/v2/src/*.fs'               # v2 tests: 'archive/v2/tests/**/*.fs'
nb 'kernel/**/*.cs' 'kernel/*.cs' 'io/**/*.cs' 'io/*.cs' 'cli/**/*.cs' 'cli/*.cs'
```

One tenth is about 18,000 product lines and 15,000 test lines. On 2026-10-19 `ci/budgets.json` held
26,600 product lines for kernel, io and cli, about 11,400 of them planned after the re-cut of M3, and
no MCP yet; its test ceiling, 43,100, is above one tenth, and v3's tests stood at 13,002 when M1
closed: the blueprint says whether those ceilings hold, and why. The Facsimile's specification adds
about 19,600 non-blank lines of Markdown, most of them a case catalog, a suite written as data that
the plan moves to `tests/Cases/` under a budget of its own (WP 2.6).

Four parts of the tree matter most:

- v2's `archive/v2/AXIOMS.md` and its `AxiomTests.fs`.
- `archive/v2/ssdt-agent/`, the knowledge tree. Its `skills/_index/*` (constraint is a claim,
  the tightening class, identity and the refactorlog, idempotent seeds, multi-phase) and
  `FINDINGS_AND_CHANGES.md` hold measured engine behavior. That behavior is a fact about the
  surface, not about the use case.
- The Facsimile's vocabulary and case catalog: the engine's questions as data, each with its case.
- v1's `Osm.Smo`, `Osm.Pipeline` and `Osm.Validation` under `archive/v1/src/`, and the contracts
  their tests pin.

### 2.4 The central question the blueprint settles

`V3_ARCHITECTURE.md` §7 keeps a kernel inherited from the emission era:

- a catalog of kinds, attributes and references;
- tightening tables;
- statements;
- σ;
- a record type.

`V3_MILESTONES.md` §2 moved reading onto the surfaces: DacFx's model walked through its own
`ModelTypeClass` properties, the deploy script's own `BlockOnPossibleDataLoss` checks, and the
metamodel through an allowlist. M0 and M1 then built part of the result: `Element`, `Change`,
`Plan`, `Provenance`, `Target`, the environments and the toolchain in the kernel; the model read,
the plan, `SqlServer.Measure`'s allowlist and the local server in io. The Facsimile adds a pure
core of its own (`Questions.Derive`, `Deltas.Compare`, `Tiering.Assign`, `Planning.Plan`,
`Acceptance.Accept`, `Outcomes.Classify`, `Records.Record`, `Config.Read`: its `TARGET.md` §3) and
a vocabulary that is data.

Decide, type by type and verb by verb, which of these each becomes:

- core;
- pack data;
- an adapter;
- gone, because a surface already does the work.

For code that exists, say whether it stays as it is. Show the evidence for each decision.

Five seams are open. Settle each with evidence, or hand it to the operator with a default:

1. **Where preconditions come from:** the property read (WP 2.1 as first written), the plan's own
   statements (the Facsimile's vocabulary), or both, as `V3_MILESTONES.md` §4 row 20 has it for
   now. Spike S2 measures the statement shapes on both DacFx releases.
2. **What may leave a real environment:** integers alone (`SqlServer.Measure`'s allowlist), or
   also the Facsimile's value-free signatures (its P6), which §17 item 24 puts to the operator.
3. **Where a copy's base comes from:** the repository at the environment's deployed tag (v3), or
   the environment's extracted schema (the Facsimile), with acceptance (F1) checking either
   (`V3_MILESTONES.md` §3.1).
4. **The agent's next step as an exit code** (§17 item 23).
5. **How the case catalog counts** against the one-tenth target and the test budget.

### 2.5 A starting hypothesis, to test and not to adopt

- **The core nouns, in the names the rulings settled:**
  - **Element:** a kind, properties and relationships, named by the surface's metadata.
  - **Model:** the elements at a ref (`ModelElements`).
  - **Change:** property-level differences between two models, with renames from the
    refactorlog.
  - **Plan:** DacFx's own statements for one target, typed, with their
    `BlockOnPossibleDataLoss` sites.
  - **Precondition:** a place where existing rows decide the outcome, of a kind the vocabulary
    names. **PreconditionState:** populated, and violating. **ExistingData:** one environment's
    states.
  - **Prediction** and **Proof,** joined by `AppliesTo`.
  - **Provenance:** the five inputs (the change, the schema, the existing data, DacFx and the
    server, the publish profile), the target and the time.
  - **Witness:** a planted row that realizes one state.
  - **Capability:** `EnvironmentDatabase`, read-only, or `Copy`, publishable.
  - **The record** (the pull request description) and **the gate.**
- **The verbs** are compositions of these nouns.
- **The 45 operations** are recognition patterns over a Change, and **the vocabulary** maps a
  plan's statements to preconditions.
- **The platform check** is a lens over a second model, read from the metamodel.

Refute or refine this against the ledgers. Do not inherit it.

## 3. What `V3_BLUEPRINT.md` contains

You may reorder the sections. Each one must achieve what is written here.

1. **The answer on one page.** One diagram (core, surface ports, adapters, packs, faces) and the
   six rules.
2. **The surface register, deepened.** One entry per surface:
   - **SQL Server:** the catalog views, the DMVs, permissions, CDC, and query execution under a
     read-only principal, including what a `VIEW DEFINITION`-only login reads.
   - **DacFx:** build, load, extract, compare, script, deploy report, publish, `ModelTypeClass`
     metadata, and the deploy options.
   - **The SSDT project:** the `.sqlproj`, the refactorlog, the pre- and post-deploy scripts,
     and the publish profiles.
   - **The local server:** LocalDB 16.x in the environments' network, which has no Docker;
     Docker with the pinned image in this repository's CI and cloud sessions; a configured
     instance; and the copy registry.
   - **git:** refs, trees, diffs, tags, LFS.
   - **The git host:** pull-request bodies, checks and branch policies, on GitHub and Azure
     DevOps.
   - **The deployment tool:** its release steps, and the engine, the effective options and the
     login of its publish step.
   - **The application platform's metamodel:** the OutSystems OSSYS tables, through an
     allowlist.
   - **MCP:** tools, the stdio transport, server instructions, and the host's policy.
   - **Agent instruction formats:** Copilot and Claude Code.

   Each entry states:

   - what the surface says about itself;
   - what the system reads and writes;
   - the fidelity bar: what is modeled exactly and what is passed through;
   - the port, as a C# interface sketch;
   - the adapter, and the code that exists for it;
   - the oracle test;
   - the spike that confirms the entry inside the environments' network;
   - its lines.
3. **The core.** The types and operations, each justified by the invariants it carries, and
   their C# encoding:
   - records and closed hierarchies;
   - smart constructors and result types;
   - no nulls;
   - no I/O, and no `Task` in a kernel signature.

   Say what the core refuses to know.
4. **The invariant ledger.** Merge these sources into one ID space:
   - the laws (`V3_ARCHITECTURE.md` §13; `V3_MILESTONES.md` F, P, T, 1′–3′, 5′, 11′ and 12′);
   - R1–R28;
   - the values register;
   - the instruction tests;
   - the Facsimile's invariants (families F, H, P, S, D, M, E, A and T) and its case IDs, which
     stay stable;
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
   - the operator, and the two principals who keep the tool;
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
   - a remedy script whose shapes are looked at and declared as witnesses;
   - a dev lead extending a proof to QA and UAT;
   - Prod, proved as assumed worst;
   - drift before a deploy;
   - the platform mapping check after a refresh.
6. **The packs.**
   - The lifecycle pack, as files with schemas.
   - The pack contract:
     - what a pack may contribute: recognition patterns, vocabulary rows, aggregate queries
       from the allowlist, the environments file, lenses, knowledge, record sections;
     - what it may not do: I/O, a new surface access, a write to a named environment.
   - Three worked future use cases, each added without touching the core. Name the files each
     adds and its lines. Candidates (choose better ones if they exist):
     - a second OutSystems system on the same platform;
     - governing seed data across environments;
     - a downstream consumer's contract, for SSIS or CDC subscribers.
7. **The faces.**
   - **The verb table.** One row per capability: name, inputs, outputs, exit codes, the
     capability it needs, and its MCP tool.
   - **How the faces are generated** from that table: the CLI, MCP, the JSON contracts and the
     docs.
   - **The MCP design:**
     - the tools, and any resource or prompt with its consumer (rule 3);
     - the transport and the server instructions;
     - the CLI as the floor, with identical semantics, for any host that does not load the
       server;
     - how the server proves it cannot write to a named environment, and cannot return a row
       value from a real one.
8. **The extraction ledger.** Every component of v1, v2, the ssdt-agent tree and the Facsimile's
   specification that the blueprint keeps. For each:
   - source files and target module;
   - method: port verbatim, port and simplify, replace with data, replace with a surface call,
     or delete;
   - the oracle that proves it: golden bytes, the 45 verdicts, the Facsimile's cases, v1's
     edge-case fixtures, or recorded outputs;
   - its order.

   Reconcile with Appendices D and E of `V3_ARCHITECTURE.md` and with the Facsimile's
   `ARCHITECTURE.md` §2, and say where you differ and why.
9. **The build path inside the environments' network.**
   - **What the agent needs, and how each arrives through git alone:** the toolchain; the
     packages, as a vendored folder in LFS plus a local NuGet source; the published DacFx tool
     folder; LocalDB 16.x.
   - **The first hour, the first day and the first week,** with commands.
   - **Spikes S1–S11,** with exact commands.
   - **What it may read:** schema, the metamodel allowlist, counts and distributions. **What it
     never reads:** row values.
   - **The milestones, re-cut** around the core, the packs and MCP. Each has an exit a person can
     check.
   - **What runs anywhere, and what needs the network.**
10. **The budget.**
    - The baseline, re-measured, with the command.
    - The one-tenth target.
    - Ceilings per module that sum under it: core, each adapter, the faces, the lifecycle pack,
      tests, and data (the golden corpus and the case catalog) counted separately.
    - Why each ceiling is credible: which surface does the work instead.
    - The budget test.
11. **Risks, unknowns, and the decisions only the operator makes.** Each has the default that
    lets work proceed. Carry forward `V3_MILESTONES.md` §17, items 20 to 24 among them, and add
    the pack contract.
12. **What this supersedes.** A table of the sections in the root design documents that the
    blueprint replaces or amends. In those documents, add one-line pointers; do not rewrite
    them.

**Appendix.** The ledgers (invariants, surfaces, use cases, verbs, extraction) as fenced YAML
with stable IDs, so a work package can turn them into tests. Add a paste line and first
instructions for an agent inside the environments' network.

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
   - Confirm API facts with spikes in the scratchpad, where the Docker SQL Server works:
     - the C# MCP SDK's current package, version and server API on .NET 10 (the Facsimile's
       `TARGET.md` §4 names `ModelContextProtocol` 2.2.0; confirm it), using a stdio server
       exposing one verb that answers `initialize`, `tools/list` and `tools/call`;
     - the DacFx calls each port names;
     - anything `V3_MILESTONES.md` §1 does not already cover.
   - Record each result as a measured fact with its command, the way that §1 does.
4. **Cold read** (§8): ask fresh agents who have only the repository.
5. **Adversarial review.** A fresh agent attacks the blueprint for:
   - a contradiction with the root design documents or the Facsimile's specification;
   - an invariant with no mechanism;
   - a use case that needs the core to change;
   - a face that can write to a named environment or return a value;
   - a budget that does not add up;
   - a step inside the environments' network that needs Docker, a feed, or a person §3.11 does
     not name.
6. **Fix and repeat** steps 4 and 5 until clean. Then commit, push, and update the pull request.

## 5. Rules

- **Planning artifacts only.** Spikes stay in the scratchpad and are cited, not committed. M0 and
  M1 are built; building continues under `V3_BUILD_PROMPT.md`, never in this session.
- **The repository's words.** Write in v3's vocabulary: `dbchange`, the SSDT repository, the
  environments (Dev, QA, UAT and Prod), the environments' network, the deployment tool, and the
  type names the rulings settled. Name no employer, team or person. Name Octopus and Azure DevOps
  only where the plan builds against them. Put no model identifiers in any committed file, commit
  or pull request.
- **Density.**
  - Write for an agent that never saw this conversation.
  - Use plain declarative sentences, and prefer tables to prose.
  - Every claim cites a file, a measurement or a decision.
  - Keep the blueprint under about 2,000 lines. Ledgers that push past that go into the YAML
    appendix.
- **The fast tests hold the documents.** `V3_BLUEPRINT.md` gets a row in `ci/docs.manifest.json`
  and joins `Documents.RootDesign` in `tests/Budgets.Tests/Documents.cs` in the commit that adds
  it, with a `DECISIONS.md` line. Run
  `dotnet test tests/Budgets.Tests/Budgets.Tests.csproj --filter Category=fast` before every push.
- **Stable numbering.** Section numbers, requirement IDs, work-package IDs, and the Facsimile's
  case and law IDs stay stable. If the milestones are re-cut, amend `V3_MILESTONES.md` in place;
  keep IDs wherever the scope is unchanged. Update `V3_BUILD_PROMPT.md`'s read order and wave
  inputs.
- **Standing constraints:**
  - no environment data on developer machines, only counts and distributions;
  - read-only access to named environments;
  - only a `Copy` is published;
  - git is the only store, with one fetch where Docker exists, the SQL Server image, and none
    inside the environments' network.
- **One branch.** Fetch and merge before every push; never rewrite history.

## 6. Done means

- **The blueprint is committed.** `V3_BLUEPRINT.md` is committed with its ledgers, its manifest
  row and its `DECISIONS.md` line, and the fast tests are green.
- **The other documents point to it.** Each carries one-line pointers to what the blueprint
  supersedes, and `V3_BUILD_PROMPT.md` reads the blueprint first and names the MCP lane.
- **The cold read passes.** Two fresh agents in a row pass the questions in §8.
- **The review is clean.** The adversarial review leaves no open blocking finding.
- **The pull request is updated.** It states the shape, the measured facts, and the decisions
  left to the operator.

## 7. Report back

Send the operator a short note:

- the shape, in one paragraph;
- the budget;
- the decisions they must make;
- the paste line for the agent inside the environments' network.

## 8. The cold-read questions

Give a fresh agent only the repository, and ask it to answer from `V3_BLUEPRINT.md` with section
and ID citations:

1. **The start.** You work inside the environments' network, with this repository and the SSDT
   repository. What do you do in your first hour, first day and first week? Name the commands
   and files.
2. **Placing invariants.** For five invariant IDs drawn at random: where does each live, and
   which test fails if it breaks?
3. **A new use case.** Add a use case the blueprint does not list; the reviewer picks it. Which
   files change? Show that the core does not.
4. **Porting.** For two components drawn at random: which v1, v2 or Facsimile files do you read
   to port it, and which oracle proves the port?
5. **Operator decisions.** What can you not do without a person, and what default lets you
   continue meanwhile?
6. **The budget.** What is the line ceiling of two modules drawn at random, and why does the
   total stay under one tenth of today's product code?
7. **MCP safety.** Which MCP tools exist, and what can each touch? Why can none write to a named
   environment or return a row value from a real one? What happens on a host that does not load
   the server?
8. **Supplies.** What do you need that the network may not have, Docker and a feed among them,
   and how does each arrive through git alone, or what replaces it?

A question passes when the answer is specific, correct and cited, and needed nothing outside the
repository.
