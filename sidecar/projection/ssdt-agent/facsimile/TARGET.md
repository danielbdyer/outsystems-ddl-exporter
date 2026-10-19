# The Facsimile: target implementation

*What "go" executes. `ARCHITECTURE.md` says what the facsimile is; `INVARIANTS.md` says what must hold;
`cases/` says how each is tested. This document says how it is built: the realization, the layout,
the dependencies, the coding style, the non-functional requirements, the test strategy, the
distribution, and the order of work. Written 2026-10-18.*

---

## 1. The realization: C# on .NET, a CLI with an MCP mode

**The decision.** The facsimile is one small C# program, distributed as a `dotnet` tool. It is
CLI-first, and the same executable also runs as a stdio MCP server (`facsimile mcp`).

**Language and surface are two separate choices.**
- **The language** is C# (or PowerShell, the fallback in Appendix A). It decides how the core is
  built and kept.
- **The surface** is a terminal command (always) and MCP (enabled in this organization). It
  decides how Copilot calls the tool.

They were conflated earlier; they are independent. MCP is never the floor, because GitHub's "MCP
servers in Copilot" policy is off by default for Business and Enterprise.

**Why C# now, when PowerShell was chosen first.** PowerShell was chosen for corporate-friction
reasons: no build, no binary, nothing for AppLocker to block. It was not chosen as the better
engineering. Specifying the core then showed what it really has to do:
- read a delta together with the environment's column catalog;
- match batches token by token;
- resolve coordinates created or renamed in the same delta;
- attribute failures to phases;
- tell a guard from a counterfeit.

Those are a T-SQL parser's and a schema model's jobs, and Microsoft ships both:
`Microsoft.SqlServer.TransactSql.ScriptDom` and DacFx's `TSqlModel`. In PowerShell's
ConstrainedLanguage mode neither is usable, and the work falls to regular expressions over generated
text and hand-parsing `model.xml`. Choosing the libraries is this repository's own "built-in
obligation", and the reason does not depend on taste. Two more reasons follow:
- **The team writes C#.** OutSystems extensions are C#, so the code is in a language its keepers
  already read.
- **The suite runs as ordinary tests.** The 1,123 cases become xUnit theories that run in Visual
  Studio's Test Explorer.

**The gate is settled (2026-10-18, by the operator).** The team already builds and runs C#, and
already runs MCP servers in Copilot. That establishes all three conditions below: .NET is present,
NuGet restores, and the organization's MCP policy is enabled. The realization is C#, and MCP mode is a
first-class surface. The CLI remains for CI, scripts and people, and as the floor if a machine's
MCP setup breaks. Appendix A stays only as the record of the alternative.

The conditions, kept for any new machine or team:

C# holds only if three facts hold on a real team laptop. All three come from the
extended laptop check (`../../../../TWIN_SUCCESSION.md` Appendix C):

| Fact | How it is read | If it fails |
|---|---|---|
| A .NET 8 or later runtime and SDK is present, or installable without admin | `dotnet --list-sdks`, `dotnet --list-runtimes` | Appendix A, the PowerShell realization |
| Code integrity does not block unsigned assemblies run by the Microsoft-signed `dotnet` host | `WDAC user mode` is 0 or 1 | ask IT to sign internal tools, or Appendix A |
| A NuGet feed is reachable (an Azure Artifacts feed with an upstream to nuget.org, or nuget.org) | `dotnet nuget list source`, plus one restore | Appendix A |

AppLocker's default exe rules allow `dotnet.exe`, because it lives under Program Files. The tool's
own code is a framework-dependent assembly that `dotnet` loads, so no unsigned `.exe` is ever
started. The spec and the cases are the same under either realization. Appendix B lists the ~40
realization-bound cases, which are re-expressed in milestone M-a.

## 2. The solution layout

Two projects, no more:

```
facsimile/
├── facsimile.sln
├── src/Facsimile/                      net8.0 · RollForward=LatestMajor · PackAsTool · ToolCommandName=facsimile
│   ├── Core/                           pure: no I/O, no clock, no randomness, no SqlClient, no DacFx
│   │   ├── Model.cs                    the domain records: Question, Answer, Witness, Plan, Verdict, Stop, Done, …
│   │   ├── Vocabulary.cs               the rule table: statement shape → question kind → probe → witness (data)
│   │   ├── Questions.cs                Derive: delta + catalog + vocabulary → questions + measures, or a stop
│   │   ├── Deltas.cs                   Compare: an environment's delta vs the PR's → lag and drift
│   │   ├── Tiering.cs                  Assign: questions, answers, measures, ledger, config → tier and flags
│   │   ├── Planning.cs                 Plan: questions, answers, catalog → the witness plan and its needs
│   │   ├── Acceptance.cs               Accept: env answers vs copy answers → ok, or a stop
│   │   ├── Outcomes.cs                 Classify: publish messages + delta → verdict, or a stop
│   │   ├── Records.cs                  Record: everything above → the record fields for the ten-section PR
│   │   ├── Config.cs                   Read: facsimile.json text → config, or every problem in one stop
│   │   └── Stops.cs                    the closed stop list: kind, exit code, who acts, the next-step template
│   ├── Shell/                          thin I/O adapters; each owns one external system
│   │   ├── Environments.cs             SqlClient, read-only: the batched counts, the catalog of an environment
│   │   ├── Dac.cs                      DacFx: Extract, offline Script, Publish with captured options; a model → Core's Catalog
│   │   ├── Builds.cs                   MSBuild via vswhere (classic .sqlproj) or dotnet build (SDK-style); git merge-base and worktree
│   │   ├── LocalServer.cs              LocalDB through SqlLocalDB.exe, or a configured server: create, backup, restore, drop; the registry
│   │   ├── Planter.cs                  runs plant.sql with the plan inside the suspend-and-restore envelope
│   │   ├── ProofFiles.cs               proofs/<slug>/ and .facsimile/: writing, hashing, stamps
│   │   └── Pipeline.cs                 the read, prove and up sequences of ARCHITECTURE §5, composing Core and the adapters
│   ├── Entry/
│   │   ├── Cli.cs                      verbs and arguments, the one stdout line, exit codes, the top-level catch
│   │   └── Mcp.cs                      `facsimile mcp`: the same verbs as MCP tools over stdio
│   └── Sql/                            embedded resources the team reads as SQL
│       ├── probes.sql   plant.sql   catalog.sql   shape.sql   hash.sql
├── tests/Facsimile.Tests/              xUnit
│   ├── Catalog/                        the case parser (the grammar of cases/README.md), and the lint as tests
│   ├── Kinds/                          one theory per kind, executing every case of that kind
│   ├── Laws/                           Boundary, Budgets, Mutation, Determinism, Stops
│   └── Fixtures/<case-id>/             materialized corpus fixtures: env.sql + head.sql, or project/
└── cases/  ARCHITECTURE.md  INVARIANTS.md  TARGET.md  README.md
```

**The purity of `Core/` is a test, not a convention.** `Laws/Boundary` fails if any file under
`Core/` refers to:
- `System.IO`, `System.Net`, `System.Diagnostics.Process`;
- `Microsoft.Data`, `Microsoft.SqlServer.Dac`;
- `Environment.`, `DateTime.Now`, `DateTimeOffset.Now`, `Guid.NewGuid`, `Random`.

ScriptDom is allowed in `Core/`: it is a parser, with no I/O.

## 3. Module map: which module holds which law

**The cases name the pure functions by their logical names.** The case parser maps each `action:`
name to its C# member, so no case changes with the realization:

| Logical name (as the cases write it) | C# member |
|---|---|
| `Get-FxQuestions` | `Questions.Derive` |
| `Compare-FxDeltas` | `Deltas.Compare` |
| `Get-FxTier` | `Tiering.Assign` |
| `Get-FxWitnessPlan` | `Planning.Plan` |
| `Test-FxAcceptance` | `Acceptance.Accept` |
| `Get-FxOutcome` | `Outcomes.Classify` |
| `Get-FxRecord` | `Records.Record` |
| `Read-FxConfig` | `Config.Read` |


| Module | Holds (INVARIANTS) | Witnessed by |
|---|---|---|
| `Core/Questions`, `Core/Vocabulary` | F4, S3, the predicate exception (P2), coordinate resolution | `10-questions`, `40`–`43` |
| `Core/Deltas` | lag and drift (F7, E3) | `50-environments` |
| `Core/Tiering` | ARCH §7, the flags | `30-outcomes` TIER |
| `Core/Planning` | M2, D3, P5, links and ancestors | `20-planter` |
| `Core/Acceptance` | F1, H2 | `20-planter`, corpus |
| `Core/Outcomes` | H5, H6, the verdict table | `30-outcomes` CLS |
| `Core/Records` | H4, H7, D6's record, the two findings | `30-outcomes` REC |
| `Core/Config` | E4, F6 (refusing `BlockOnPossibleDataLoss=False`) | `70-process` CFG |
| `Core/Stops` | H3, A3, A5 | `70-process` CLI |
| `Shell/Environments` | P1–P4, M3 | `60-privacy`, `50-environments` |
| `Shell/Dac` | F5, F6, the phase order | `50`, `43-compound` |
| `Shell/LocalServer` | S1, S4, S5, S6, D5 | `70-process`, `00-invariants` |
| `Shell/Planter` | F2, F9, S2, P5 | `20-planter` |
| `Shell/ProofFiles` | D1, D2, E3, H7 | `00-invariants`, `70-process` |
| `Shell/Pipeline` | F3, F7, F8, D4, D6 | corpus |
| `Entry/Cli`, `Entry/Mcp` | H1, A1, A3 | `70-process` |

## 4. Dependencies

There are at most four runtime packages, each pinned, and no others:

| Package | Version | Why |
|---|---|---|
| `Microsoft.SqlServer.DacFx` | **the Octopus step's version.** Until it is captured: 170.5.96 | Extract, Script, Publish, `TSqlModel`, `DacProfile` |
| `Microsoft.Data.SqlClient` | the version DacFx depends on | probes, the planter, LocalDB control |
| `Microsoft.SqlServer.TransactSql.ScriptDom` | the version DacFx depends on, referenced explicitly | parsing deltas and authored scripts |
| `ModelContextProtocol` | 2.2.0 | MCP mode only. It brings `Microsoft.Extensions.Hosting.Abstractions`, used only inside `Entry/Mcp.cs` |

**Tests:** `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`.

**What is not used:** a DI container (outside `Mcp.cs`), a logging framework, a CLI-parsing library,
a JSON library other than `System.Text.Json`, an assertion library, Polly, AutoMapper, analyzers
beyond the SDK's own, and source generators.

**The target is `net8.0` with `RollForward=LatestMajor`,** so the tool runs on any installed .NET 8,
9 or 10 runtime. The .NET 8 runtime leaves support in November 2026, and roll-forward lets laptops
move to 10 with no rebuild. The target moves to `net10.0` once every laptop has .NET 10.

## 5. Coding style

The style is plain, current C#, written for the team that keeps it. It is not this repository's V2
F# discipline (§10).

**Language settings:**
- C# 12;
- `Nullable=enable`, `TreatWarningsAsErrors=true`;
- `ImplicitUsings=disable`, so every dependency of a file is visible at its top, which is what keeps
  the Core boundary legible;
- file-scoped namespaces.

**Data:**
- Values are `sealed record`s.
- Closed vocabularies are enums: `QuestionKind`, `ValueClass`, `StopKind`, `BlockReason`, `Tier`,
  `Phase`.
- The one hierarchy is the outcome: `abstract record Outcome` with sealed `Verdict`, `Stop` and
  `Done`, consumed by exhaustive `switch` expressions.
- There is no other inheritance.
- There is no interface without two real implementations. `LocalServer` serves LocalDB and a
  configured server through one class and a target record.

**Functions:**
- Core functions are static members of static classes named for the domain concept:
  `Questions.Derive`, `Outcomes.Classify`, `Planning.Plan`.
- Each takes records and returns records.
- Time and random suffixes are passed in by the shell, never read.
- A function stays under ~40 lines, and a file under ~250.

**Expected outcomes are values:**
- A stop is a `Stop`, returned, never thrown.
- Exceptions mean a programmer error or an unexpected I/O failure.
- `Entry/Cli.cs` holds the only top-level catch, which turns any exception into `stop internal`.
- `Core/` has no try/catch at all.

**Naming:**
- The spec's words verbatim: Question, Answer, Witness, Plan, Verdict, Stop, Copy, Environment,
  Delta, Vocabulary, Residual, Stamp, Measure.
- No `Helper`, `Util`, `Manager`, `Service`, `Factory`, `Processor` or `Handler`.
- A verb names a domain act (Derive, Classify, Plant, Accept, Extract, Publish).

**SQL:**
- T-SQL lives in `Sql/*.sql`, as SQL the team can read and fix.
- C# never concatenates an identifier. Identifiers come from the catalog, are validated against the
  environment (`OBJECT_ID`, `COL_LENGTH`), and are quoted by one function.
- Values always travel as `SqlParameter`s.
- A probe is a named template with `@parameters`.

**Text:**
- Every format and parse uses `CultureInfo.InvariantCulture`.
- Output is UTF-8 without a BOM, with LF line endings.
- Cultures stay on (no `InvariantGlobalization`), because DacFx and SqlClient need them (a v3
  lesson).

**Async:**
- Core is synchronous.
- The shell uses synchronous SqlClient and DacFx calls.
- MCP handlers wrap the same synchronous verbs.

**Comments:**
- A one-line purpose at the top of each file.
- `// Laws: F4 S3` above a function that holds a law.
- Otherwise only "why" comments, never "what".

**Never:**
- reflection or `dynamic`;
- an `#if` per platform, except the LocalDB executable's path;
- a string-built T-SQL statement other than through the templates and the quote function;
- a feature with no case.

## 6. Non-functional requirements

| Area | Requirement | Measured by |
|---|---|---|
| Correctness | Every non-manual case passes. The differential (F3) agrees on 100% of corpus cases. No corpus delta stops `unknown-statement` | `dotnet test`; the corpus job |
| Speed: tests | The pure suite runs in ≤ 10 s; the lint in ≤ 5 s | test timing |
| Speed: verbs | `read` ≤ 60 s for a ~300-table estate, dominated by Extract. `prove` with a warm cache ≤ 60 s. A cold base cache ≤ 5 min. `up` as a no-op ≤ 2 s | `.facsimile/out/run-*.json` step timings; ARCH §11 question 6 checks the estimates on the real estate |
| Speed: CI | The full corpus runs in ≤ 25 min | the workflow |
| Footprint | The tool package is ≤ 60 MB (DacFx dominates). It needs no admin rights, runs no service, and opens no listening port. `.facsimile/` keeps the 10 newest base caches and evicts the rest | package size; `doctor` |
| Network | The tool connects only to configured environment servers (read-only) and the local server. Telemetry is off: `DACFX_TELEMETRY_OPTOUT=1` is set in-process before DacFx loads, and `DOTNET_CLI_TELEMETRY_OPTOUT=1` in the shims | a connection trace (P4) |
| Privacy and security | P1–P6. No secret appears in a file, an argument, stdout, stderr or a log. Secrets are `env:` or `file:` references. Environment connections are `ApplicationIntent=ReadOnly`. Only registered databases are ever published to | `60-privacy`; S1 |
| Reliability | H1's outcomes; every verb is idempotent; a crash leaves only reapable state; LocalDB's first connect is retried once; DDL on the local server waits up to 180 s; a DROP uses `SINGLE_USER WITH ROLLBACK IMMEDIATE` and `ClearPool` | `70-process`, `00-invariants` |
| Determinism | Artifacts are byte-identical between runs apart from timestamp fields (D1) | the determinism law test |
| Portability | Windows 10/11 x64 with LocalDB (laptops, Azure DevOps hosted agents). Linux x64 against a configured SQL Server (this sandbox, the corpus job). DacFx and SqlClient run on both | the CI matrix |
| Compatibility | Environments on SQL Server 2019 or later. LocalDB 2022 or later, or an older one with the stamped override. DacFx pinned to Octopus's version. Visual Studio 2022 17.14 or VS 2026 (terminal everywhere; MCP where enabled) | `doctor`; the laptop check |
| Maintainability | Production code ≤ 1,900 lines (C# ≤ 1,300, SQL ≤ 600). Two projects, four runtime packages. A new vocabulary rule is one rule, one case and one fixture, under an hour for a principal | `Laws/Budgets`; the E1 ratchet |
| Observability | One stdout line. At most 15 progress lines on stderr. `--verbose` adds the step trace with timings. A run file per invocation under `.facsimile/out/`. Nothing else | `70-process` |
| Agent usability | Every stop message is an instruction (A5). Output JSON has versioned schemas (`facsimile.verdict/1`, …). Each MCP tool description is at most two sentences | `70-process` |

## 7. The test strategy: the catalog is the suite

- **The parser.** `tests/Catalog` reads `cases/*.md` in the grammar of `cases/README.md` into
  `Case` records. The lint (a port of `tests/catalog_lint.py`) runs as tests. The Python prototype
  retires the day the C# lint is green in CI.
- **By kind:**
  - `pure`: call the Core function the case names.
  - `sql`: needs `FACSIMILE_TEST_SERVER`, the SQL Server 2022 container in this sandbox and the
    Linux CI job, or LocalDB in the Windows job.
  - `corpus`: build the stand-in environment from the case's fixture, run `read` under a read-only
    login, run `prove`, and compare with the oracle (head published onto a full copy of the
    environment).
  - `process`: run the CLI in-process against a temporary working tree.
  - `manual`: reported, and skipped as "manual: laptop check".
- **Fixtures.** Each corpus case materializes into `Fixtures/<id>/`:
  - `env.sql` (the `pg` schema, its deltas and the data shapes) plus `head.sql`; or
  - `project/` (a proving-ground copy with the case's edits), when authored scripts are involved.

  A fixture's hash is part of the test's display name, so a case edited without its fixture is
  visible. Fixtures are data, outside the line budget.
- **Laws as tests:**
  - **Boundary:** Core's purity (§2).
  - **Budgets:** M4.
  - **Mutation:** M1. A test-only knockout set disables one vocabulary rule, witness kind or pipeline
    step at a time, and at least one case must fail each time. It runs nightly.
  - **Determinism:** D1. Run twice and compare bytes.
  - **Stops:** every stop kind's template fills, and the exit-code map is exact.
- **CI in this repository** (GitHub Actions):

  | Job | Runs on | Runs | When |
  |---|---|---|---|
  | `build-pure` | ubuntu | build, the pure cases and the lint | every push |
  | `sql-corpus` | ubuntu, with the SQL Server 2022 container | the sql and corpus cases | every PR touching `facsimile/` |
  | `localdb` | windows-latest | the sql cases, a corpus subset and the process cases on LocalDB | every PR, and nightly |
  | `mutation` | — | M1 | nightly |

- **In the estate repository (Azure DevOps):** the PR gate runs `dotnet facsimile prove` on a
  Windows hosted agent from the committed `proofs/<slug>/`, with no environment access.

## 8. Distribution and the MCP surface

**The tool package:**
- **Publishing.** CI runs `dotnet pack` (`PackAsTool`) and pushes `Facsimile` to the team's Azure
  Artifacts feed.
- **The estate repository** commits:
  - `.config/dotnet-tools.json`, which pins the version;
  - `nuget.config`, which names the feed.
- **A developer** runs `dotnet tool restore` once, then `dotnet facsimile <verb>`.
- **Upgrading** is a reviewed PR that bumps the manifest. Every verdict stamps the version (H7).

**Without a feed** the tool runs from source with
`dotnet run --project facsimile/src/Facsimile -- <verb>`, which needs a NuGet restore once. The
source travels with the vendored bundle, so Copilot can always read, explain and fix the tool it is
calling.

**MCP mode:**
- **Configuration.** `.mcp.json` at the solution root:
  `{"servers":{"facsimile":{"type":"stdio","command":"dotnet","args":["facsimile","mcp"]}}}`.
- **Tools:** `doctor`, `read`, `prove`, `shape`, `up`, `status`, `down`. `init` stays CLI-only.
  - Each tool's input schema mirrors the CLI's arguments.
  - Its result is the JSON the CLI writes, plus the one line.
- **No resources and no prompts.** Knowledge lives in the skill files.
- **Server instructions, three sentences:** every call ends in a verdict, a stop or done; a stop
  names the next step, so do that step; never retry a stop unchanged.
- **stdout is the protocol,** and all progress goes to stderr.
- **Side effects.** Every tool is read-only towards environments. `prove` and `up` write only under
  `proofs/`, under `.facsimile/`, and to the tool's own `fx_` databases.

## 9. Constraints carried from v3 and this session's experiments

1. **DacFx needs cultures.** Never set `InvariantGlobalization`; format under the invariant culture
   instead.
2. **Telemetry opt-out comes first.** Set it before DacFx loads.
3. **Load dacpacs from a stream,** never by path.
4. **A Strict block arrives with no `SqlException`.** It is a `DacServicesException` whose messages
   carry "Msg N" inside SQL72014. Read the numbers from the messages, and match the SQL72045 batch
   token by token.
5. **LocalDB is slow and fragile at the edges:**
   - CREATE and DROP can exceed 30 s, so wait 180 s;
   - DROP goes through `SINGLE_USER` + `ClearPool` on an unpooled `master` connection;
   - the first connect can time out, so retry once.
6. **Never keep a hand-written Strict profile.** Load the captured effective options, and flip only
   `BlockOnPossibleDataLoss` for Permissive.
7. **VIEW DEFINITION is what extraction needs.** A data-reader-only login is refused by Extract and
   Script. A VIEW DEFINITION-only login extracts the schema, generates the delta and reads
   `sys.partitions`. (Receipt: TWIN_SUCCESSION §6b.)
8. **The offline delta equals the live delta,** guard included. (Receipt: TWIN_SUCCESSION §0.1.)
9. **Failed deploys are not atomic** under the default options. Constraint validation was observed
   running after post-deploy. (Red team #4; the 2026-10-18 observation; `verify` per version.)

## 10. Relationship to this repository's disciplines

`../../../../AGENTS.md`'s pillars govern the F# V2 sidecar. The facsimile lives inside `sidecar/projection/ssdt-agent/`
but is not V2. It is written for a team that will keep it without its author.

**Kept, in spirit:**

| Pillar | How it applies here |
|---|---|
| 1, typed data with strings only at the boundary | ScriptDom parses rather than regex; SQL lives in files; identifiers are quoted by one function |
| 3, built-in obligation | DacFx, ScriptDom, SqlClient, `System.Text.Json` |
| 6, no back-compat paths | none inside the facsimile |
| 8, domain-first naming | the spec's vocabulary, verbatim |

**Deliberately not adopted:**
- `Bench` scopes on every function;
- `LINT-ALLOW` markers;
- point-free functional style;
- custom analyzers;
- the chapter and handoff rituals.

A future agent must not import them. If the facsimile needs one of them, a case will show it.

## 11. The order of work when you say "go"

Each milestone ends with every case of its scope green, the budgets checked, a commit and a push.

| Milestone | Builds | Done when | Estimate |
|---|---|---|---:|
| **M-a** | The solution skeleton. The case parser, and the lint ported to tests. The ~40 realization-bound cases re-expressed for C# (Appendix B). `Core/Model`, `Stops`, `Config`, `Outcomes`, `Tiering`, `Records` | every pure CFG, CLS, TIER and REC case is green; the Boundary and Budgets laws hold | 1 day |
| **M-b** | `Core/Vocabulary`, `Questions` (ScriptDom over the delta, plus the catalog), `Deltas`, `Planning`, `Acceptance` | every pure QST and planner case is green | 1 day |
| **M-c** | `Shell/Dac`, `Environments`, `LocalServer` (against a configured server), `Planter`, `ProofFiles`; the SQL files | every `sql` case is green on the SQL Server 2022 container here | 1–2 days |
| **M-d** | `Shell/Pipeline` (read, prove); fixture materialization; the differential harness | every corpus case is green on the container; F3 agrees 100% | 2 days (fixtures dominate; drafted per family in parallel) |
| **M-e** | `up`, `status`, `down`, `init`, `doctor`, `shape`; `Entry/Mcp`; the Windows LocalDB CI job | every process case is green; the LocalDB job is green | 1 day |
| **M-f** | Twin parity: the TWN cases; `dotnet pack`; the vendoring layout; the G4 skill-tree changes *drafted* (not applied) | every TWN case is green: this is gate G3 | 1 day |

**What is needed from you, and when:**

| Need | Before | Why |
|---|---|---|
| Nothing | M-a to M-d | they run entirely in this sandbox |
| ~~`sqllocaldb versions` from one team laptop~~ | — | settled 2026-10-19: 16.x (SQL Server 2022), which meets a Sql160 project (S4). DacFx runs in-process, so sqlpackage is not needed on laptops at all |
| The Octopus step's effective options and DacFx version | trusting any verdict on the real estate | not needed to build anything |
| Whether there is an Azure Artifacts feed | M-f | distribution |
| ~~Whether the MCP policy is enabled~~ | — | settled: enabled |

## 12. Risks

| Risk | Mitigation |
|---|---|
| WDAC blocks unsigned assemblies on laptops | the gate (§1); Appendix A; internal code signing |
| There is no NuGet feed | run from source with a one-time restore, or Appendix A |
| The Octopus DacFx is 162.x and behaves differently (trust, phase order) | pin to it; the corpus runs on the pinned version (E2); `verify` cases per version |
| The fixture work for 410 corpus cases is underestimated | materialized per family in parallel; shared `pg` base; small deltas |
| LocalDB timing is unrepresentative | NFR timings are measured on the real estate (ARCH §11 question 6); scale is a residual, never a verdict |
| The team does not adopt it | CLI first; `facsimile up` keeps the Twin's daily verbs; the side-by-side window (G4) |

---

## Appendix A: the PowerShell realization, if the gate fails

The spec and the cases are unchanged. Only the module internals change:

| C# module | PowerShell form |
|---|---|
| `Core/*` | `core.ps1`: the same functions, ConstrainedLanguage-safe |
| `Core/Questions` using ScriptDom and `TSqlModel` | batches split on `GO`, matched with normalized regular expressions; the catalog read from `model.xml` through `[xml]` |
| `Shell/Dac` | `sqlpackage` Extract, Script and Publish, with `/p:` from the captured options |
| `Shell/Environments` and `Shell/Planter` | `sqlcmd -v` with the same `.sql` templates |
| `Shell/LocalServer` | `SqlLocalDB.exe` plus `sqlcmd` |
| `Entry/Cli` | `facsimile.ps1` |
| `Entry/Mcp` | none (CLI only) |
| `tests/*` (xUnit) | `tests/run-cases.ps1` (the runner and the lint) |
| Distribution (the dotnet tool) | the files themselves, vendored |

The budget is ARCHITECTURE §3's: about 1,600 lines.

## Appendix B: the realization-bound cases

These cases name PowerShell, ConstrainedLanguage, `run-cases.ps1` or `.psd1` (counts as of
2026-10-18). Milestone M-a re-expresses them for C#, keeping their IDs and their laws:

| File | Mentions |
|---|---:|
| `cases/70-process.md` | 38 (toolchain, CI, language mode) |
| `cases/00-invariants.md` | 21 (mostly A2) |
| `cases/80-twin-parity.md` | 7 |
| `cases/60-privacy.md` | 3 |
| `cases/10-questions.md` | 2 |

Under C#, A2's object changes. It becomes "runs under the laptop's application-control policy as
the laptop check measures it: framework-dependent, loaded by the signed `dotnet` host, no unsigned
native code, no elevation".
