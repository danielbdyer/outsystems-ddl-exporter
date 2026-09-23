# V3 — The Milestones

*A build plan for `V3_ARCHITECTURE.md`, `V3_INSTRUCTION_ARCHITECTURE.md`, and the change-validation
lifecycle as `LIFECYCLE_BACKPORT_PROMPT.md` states it for git as the only store. It is one step more
specific than any of them. It proposes; the operator decides. It is the last design document: from
M0 on, `NEXT.md` carries the live state, and this file is archived with the other two at M8.*

**Contents.** §0 the answer · §1 what was measured before writing · §2 the principle · §3 the five
inputs, the two instruments, the branch · §4 what this changes in the architecture, and what it
keeps · §5 the milestones at a glance · §6–§14 M0 to M8 and the wing · §15 the estate side ·
§16 how agents build it · §17 decisions and prerequisites · §18 risks · §19 the first hour ·
A requirements map · B the germ, mapped · C the laws, mapped · D the instruction tests, mapped ·
E the spike code · F the budget by file.

---

## 0. The answer in one breath

**Build the lifecycle first, in eight milestones and one optional wing.** The critical path is
about thirteen working days with four agent lanes. The first useful thing lands on day three:
one command, read-only, says whether Dev matches the repository at a tag. Prediction lands on
day five, the Twin on day six (at branch volume; shape volume on day eight), proof on day nine,
the gate on day eleven, and the after-deploy checks with the status page on day thirteen. The
knowledge tree and the agent surfaces are rebuilt in a parallel lane that closes with the Copilot
pilot. The cutover wing (`emit`, `decide`, `move`, and the full OSSYS reader) stays in v2, frozen
and buildable, until Prod is cut over; then the operator ports it or deletes it.

**Few lines, by one rule: read whole, bind late.** Every surface v3 touches already describes
itself. DacFx models every SQL object as a type with named properties and relationships, and it
reads a project, a package and a live database into that one model. A deploy script carries its
own data-loss guards as ordinary `SELECT`s. The OutSystems metamodel is tables. A wiki is a git
repository. So v3 has one adapter per substrate, and each adapter reads its substrate whole,
through the substrate's own self-description, without knowing which verb asked. A verb is a lens
that binds that reading to one directive, and most lenses are data. Adapters grow with
substrates, lenses grow with directives, and nothing grows with their product. The lifecycle
engine is budgeted at **≤ 13,500 lines of C#** (kernel ≤ 6,000, io ≤ 6,000, cli ≤ 1,500), against
`V3_ARCHITECTURE.md` §6.6's ≤ 31,000 for everything; the wing, if ported, brings its own §6.6
numbers and the sum stays under that ceiling.

**Fidelity, by one refusal: never re-derive the engine.** Whether a change blocks is the deploy
script's own guard, run read-only. Whether a value survives a retype is the engine's own
conversion. Whether an environment matches a tag is an empty deploy plan under the pipeline's own
publish profile. What a change does is DacFx publishing it to a copy, from a package built with
the pipeline's own build semantics. v3 re-implements none of these, so its answers cannot drift
from the engine's. Where v3 must write a query itself (a violation count), a law holds the query
to the engine on every archetype: the prediction on a copy must equal the proof on that copy.

**The germ, as types.** An outcome is a function of five inputs: the delta, the target's schema,
the target's data facts, the engine, the profile. The chain has two instruments: prediction reads
*which branch* an environment is on; proof shows *what the engine does* on a branch. So v3 makes
the **branch** a first-class value that travels with the change. `estate predict` reads it,
read-only, from each environment the author or the lead can reach; the pull request carries it;
the gate mints a disposable copy at exactly that branch and proves there. A proof transfers by
construction, and a lead proves "as UAT" without a row of UAT leaving UAT. The committed evidence
carries realism and the live branch carries correctness, so the Twin asks for a refresh only when a
live branch disagrees with it.

---

## 1. What was measured before writing this (2026-09-23)

The plan rests on ten facts. Eight were measured today in this repository's session container
(.NET SDK 9.0.314, DacFx 162.5.57 from the package cache, SQL Server 2022 in Docker); two were read
from v2's source and the package's documentation. The spike code is Appendix E, and it becomes M1's
first tests.

| # | The claim | How it was measured | Result |
|---|---|---|---|
| 1 | A classic (Visual Studio format) `.sqlproj` builds with no Visual Studio and no download | a minimal classic project; `dotnet build -p:NetCoreBuild=true -p:NETCoreTargetsPath=<tool> -p:SQLDBExtensionsRefPath=<tool> -p:TargetFrameworkRootPath=<stub>`, where `<tool>` is a published console app referencing DacFx plus the package's `Microsoft.Data.Tools.Schema.SqlTasks.targets`, and `<stub>` holds one reference assembly (`mscorlib.dll`, 2.7 MB) with its `FrameworkList.xml` | builds on Linux; the dacpac carries `refactor.xml` and `postdeploy.sql`. The package's `lib` folder alone fails (`SqlBuildTask` needs DacFx's dependencies beside it); a published tool folder has them (60 files, 91 MB framework-dependent) |
| 2 | `Script` and `DeployReport` need nothing beyond read | a login holding only `VIEW DEFINITION` and `db_datareader` on one database; `DacServices.Script(package, db, PublishOptions { GenerateDeploymentScript, GenerateDeploymentReport })` | succeeds |
| 3 | The data-loss guard can be lifted from the script and run read-only | ScriptDom `TSql160Parser` over the script with SQLCMD lines removed (0 parse errors); the guard is `IF EXISTS (SELECT TOP 1 1 FROM [dbo].[Customer]) RAISERROR (…, 16, 127)`; its predicate run verbatim as the read-only login | returns 1 on a populated table. The script holds other `IF EXISTS` blocks (database-option checks against `master.dbo.sysdatabases`), so the lens matches the `RAISERROR` at state 127, not the `IF` alone |
| 4 | The convergence oracle is an empty plan, not a default schema comparison | the base package against the database it was just published to, as the read-only login | the deploy report has no `<Operations>` element. `SchemaComparison` with default options reports two differences: the reader's own user and the role membership granted to it. The oracle is the empty plan under the pipeline's profile; a comparison is used only to name objects, and only with the profile's options |
| 5 | A live database reads into the same model a build produces | `TSqlModel.LoadFromDatabase` as the read-only login | succeeds |
| 6 | Property changes are found generically | a walk over `ModelTypeClass.Properties` on the base and head `Column` objects (reached through `Table.Columns`; a column is not a top-level type) | one change, `Column.Nullable: True → False`. DacFx knows 29 properties and 4 relationships on a column; one walk covers them all, and every other type the same way |
| 7 | The deploy report is too coarse to classify from | the same head package | one operation, `Alter [dbo].[Customer] (SqlTable)`, and no alert. The claim comes from the property walk; the guard comes from the script |
| 8 | v2's Twin cannot see a rename carried by the refactorlog, or pre- and post-deploy scripts | `Twin.Runtime/EstateModel.fs` builds with `TSqlModel.AddObjects` and the three-argument `BuildPackage` (no `PackageOptions.RefactorLogPath`); `DacpacEmitter.fs` records that a model-built dacpac is schema-only; both rename facts in `SamplePrRenameTests.fs` are the no-entry kind, with the entry's effect imitated by a hand-run `sp_rename` | confirmed. `prove.mjs` builds the real project and does see them. v3 has one build, the real one (M1) |
| 9 | Every DacFx call the plan uses exists on the pinned engine | the XML documentation shipped in the 162.5.57 package | present: `TSqlModel.LoadFromDatabase`; `DacServices.Script` and `Publish` with `PublishOptions`, returning `DatabaseScript` and `DeploymentReport`; `DacProfile.Load(…).DeployOptions`; `PackageOptions.RefactorLogPath`; `SchemaComparison` (dacpac and database endpoints only); `ModelTypeClass.Properties` and `.Relationships`; the typed metadata classes (`Column.Nullable`, `Column.Length`, `CheckConstraint.Expression`, `ForeignKeyConstraint.ForeignTable`) |
| 10 | The pipeline's publish profile is the options, and a hand-kept "Strict" profile can drift from it | `DacProfile.Load(path).DeployOptions` over the proving ground's three profiles | all three load. `ProvingGround.Strict.publish.xml` sets `DropObjectsNotInSource=True` where `ProvingGround.Pipeline.publish.xml` sets `False` (the guard is on in both, off in Permissive). So v3 keeps no Strict file: Strict is the pipeline's profile as loaded, and Permissive is the same with only the guard flipped |

What was *not* measured, and is therefore M1's first work: the same build on `windows-latest` and
on a team laptop; a project that references `master.dacpac` (the tool folder must then carry the
system dacpacs); the estate's 300-table project for time; and the same read-only calls against Dev
under the developers' Active Directory group.

---

## 2. The principle: read whole, bind late

### 2.1 Three rules

1. **One adapter per substrate, and it reads the substrate whole.** An adapter is written against
   the substrate's own self-description, never against a verb's need. The model adapter walks every
   type DacFx knows, every property and every relationship, through `ModelTypeClass.Properties` and
   `.Relationships`; it has no list of "the columns we care about". The probe executor runs any
   aggregate the allowlist admits; it has no list of "the checks we do". So fingerprints, drift, the
   diff and the convergence oracle see everything the engine sees, and a change nobody wrote a lens
   for surfaces as an *unclassified change* that routes to proof, instead of vanishing.
2. **A directive is a lens, and a lens is data wherever it can be.** The operation catalog's
   recognition patterns live in the operation files. The profiles are the pipeline's own
   `.publish.xml`. The environments, their classification and their cohorts are one committed JSON
   file. The agent surfaces are rows in a targets file. The record's sections and banned words are
   `knowledge/record.md`. Code interprets these; adding an operation, an environment or an agent
   surface is a file, not a function.
3. **Capabilities are types.** A named environment and a disposable copy are different C# types.
   Only the copy has a publish method; the Permissive profile can only be constructed for a copy;
   the probe executor for a named environment accepts only the allowlist. "Only Octopus writes"
   and "Permissive only on a copy" are then compile-time facts with one test each, not rules an
   agent must remember.

### 2.2 The surface register

| Surface | Its own self-description | The one adapter | Access | Replaces | Lines |
|---|---|---|---|---|---:|
| The SSDT project (`.sqlproj`, `.sql`, refactorlog, pre- and post-deploy, SQLCMD variables) | MSBuild and DacFx's build targets | `io/Ssdt.Build`: `dotnet build` with the committed targets (§1 fact 1); Visual Studio's MSBuild through `vswhere` as the fallback, stamped | read | the Twin's `AddObjects` build; the build fork in `prove.mjs` | ~150 |
| A package (`.dacpac`) | the DacFx model: types with properties and relationships | `io/Ssdt.Walk`: every object, property and relationship into kernel `Element`s | read | the lifts in `ReadSide`, `EstateModel` and `CatalogDiff`'s inputs | ~150 |
| A live database's schema (Dev, QA, UAT, a copy) | the same model, loaded from the database | `io/SqlServer.Model`: `TSqlModel.LoadFromDatabase`, then the same walk | read (`VIEW DEFINITION`) | `ReadSide.fs` and the read side's hand-rolled drain loops | ~60 |
| A live database's plan | DacFx's own deploy script and report | `io/SqlServer.Plan`: `DacServices.Script` under the pipeline's profile; the script's AST; the guard sites | read | `sqlpackage /Action:Script` in `prove.mjs` | ~250 |
| A live database's data | SQL Server's own aggregates | `io/SqlServer.Probe`: one executor; a closed allowlist checked on the ScriptDom AST before anything runs; a query log | read (aggregates only) | `LiveProfiler.fs`, `EvidenceImport.fs`, `DataIntegrityChecker.fs`, probe SQL in the skills | ~350 |
| A disposable copy | a database the tool created and will drop | `io/Substrate` (LocalDB; Docker when the image is already present; a named server) and `Publish`, which exists only on the copy type | write, copy only | `TwinContainer.fs`, `TwinDatabase.fs`, `bake.mjs`, `DockerImageEmitter.fs` | ~450 |
| The OutSystems metamodel | its `ossys_*` tables | `io/Ossys`: committed SQL for the post-cutover subset, through the executor's catalog mode | read | nothing (the refresh and consumer checks are new); v1's rowset SQL is the donor | ~250 |
| Git (the estate, the wiki) | refs, trees, commits | `io/Git`: a worktree at a ref; changed paths; a commit on a named branch | read; commit to a named branch | the base checkout in the gate and in `inflight-check.mjs` | ~180 |
| Azure DevOps (policy, pull request) | a branch policy and a REST API | none in the tool. The pipeline template's host step (≤ 25 lines of PowerShell) writes the PR body to a file and posts the comment | host step | the placeholders in `ssdt-agent-pr-validation.yml` | 0 |
| The agents' surfaces (Copilot in Visual Studio, Claude Code) | files at known paths | `io/Knowledge`: one generator; the targets as data | write, generated only | `ssdt-agent-package.mjs`, `ssdt-agent-gates.mjs` | ~420 |
| The machine | the SDK, LocalDB, Docker, the committed tool | `io/Doctor` | read | four hooks, 814 lines | ~140 |
| The engine | the committed DacFx and its version | a stamp in every receipt | read | two engines on two paths (in-process 162, external `sqlpackage` 170) | ~30 |

### 2.3 The lens register

| The directive | Verb | Reads | Parameterized by | Kernel function | Output |
|---|---|---|---|---|---|
| Can this machine do the work? | `doctor` | the machine, the engine | `ledgers/toolchain.md` | — | `estate.doctor/1` |
| What is this schema? | `read` | a ref, a package or a database | — | `Element` graph and its fingerprint | `estate.read/1` |
| What changes? | `diff` | any two of those | the refactorlog | `Change.Between` | `estate.diff/1` |
| What is it, provisionally? | `classify` | the diff and the committed evidence | the `recognize:` block in each `knowledge/ops/*.md`; `ledgers/cdc-tracked.md` | `Classify.Of` | `estate.classify/1` |
| Which branch is each environment on? | `predict` | the head package; each reachable environment's plan and data | `estate.environments.json` | `Claims.Of`, `Branch.Of`, `Outcome.Predict` | `estate.predict/1`, and a Markdown block carrying a machine block |
| What does the data look like? | `profile` | an environment's data | its classification (what may be read) | `Evidence.Of` | `evidence.json`; row tiers for real environments |
| A current substrate | `twin` | a ref, the evidence, the seed, optionally a branch | the tier | `Synth.Mint` | `estate.twin/1` |
| What does the engine do? | `prove` | a fresh copy per branch | the pipeline's profile (Strict and Permissive differ in the guard alone) | `Outcome.Prove`, `Transfers` | `estate.prove/1` |
| The pull request body | `record` | the receipts | `knowledge/record.md` | `Record.Of` | the body |
| Reproduce it | `gate` | git, the PR body, the ledgers | `ledgers/in-flight.md` | the functions above | `estate.gate/1`, `changelog.json` |
| Deployed means converged | `check drift` | the package at a tag; the environment's plan | the pipeline's profile | `Outcome.Converged` | `estate.check/1` |
| Did the platform follow? | `check outsystems` | the metamodel; the package at a tag | — | `Platform.Compare` | `estate.check/1` |
| One page | `check environments --page` | the checks above | the environments file | `Page.Of` | a wiki page |
| Is the evidence still true? | `check evidence` | the committed evidence; a live branch | — | `Branch` equality | `estate.check/1` |
| Is a table locked? | `check inflight` | the diff; the ledger | `ledgers/in-flight.md` | `Locks.Collide` | `estate.check/1` |
| The front door | `knowledge package` | `knowledge/` | `knowledge/targets.json` | — | generated files |

### 2.4 Where the lines go missing

v2 read a schema four ways through three lifts (`V3_ARCHITECTURE.md` §8.1), drained rows in fifteen
hand-rolled loops, proved in two languages on two engines, and stood a substrate up three ways: the
cost was readers times consumers. Here the product is gone. One walk reads every type for every
consumer; one executor runs every aggregate for every consumer; one build serves the Twin and every
proof. An object type DacFx already knows costs zero lines, because it is already in the walk, the
fingerprint, the diff and the drift check. An operation costs one knowledge file with a
`recognize:` block. An environment costs one row in `estate.environments.json`. An agent surface
costs one row in `knowledge/targets.json`. The kernel stays pure because the walk hands it plain
`Element` records: the diff, the claims, the branch, the classification, the typed schema that
σ reads, and the record are all pure functions over them, and all property-testable without a
database.

---

## 3. The five inputs, the two instruments, the branch

The germ (the invariants written for the lifecycle) says an outcome is fixed by five inputs and
that the chain needs two instruments. This section is that sentence as types; every later milestone
fills one of them in.

```csharp
// Every claim carries all five inputs, or names the one it lacks.
public sealed record Receipt(
    Fingerprint Delta, Fingerprint Target, Fingerprint? DataFacts, Engine Engine, Fingerprint Profile,
    string Where, DateTimeOffset At);

// A place the engine branches: a guarded table, or a rule the new schema claims about existing rows.
public sealed record ClaimSite(Name Table, Seq<Name> Columns, ClaimKind Kind);  // Presence · NotNull · Fits · Reference · Unique · Check
public sealed record BranchSite(ClaimSite Site, bool Populated, bool Violating);
public sealed record Branch(Seq<BranchSite> Sites);                             // Seq equality is element-wise

public sealed record Prediction(EnvironmentName Environment, Branch Branch, Seq<GuardSite> Guards,
                                Seq<ClaimCount> Counts, Shipping Predicted, Receipt Receipt);
public sealed record Proof(CopyName Copy, Branch Branch, PublishOutcome Strict, PublishOutcome? Permissive,
                           Readback Readback, Receipt Receipt);

public static bool Transfers(Proof proof, Prediction prediction) => proof.Branch == prediction.Branch;
```

**The branch travels with the change.**

```
author's laptop                            the pull request                 the gate (a hosted agent, the clone only)
estate predict            ─► branch(dev) ─┐
lead's laptop                             ├─► a machine block in the body ─► per distinct branch: mint a fresh copy at it,
estate predict --env qa,uat ─► branch(qa) │                                   prove, compare with the author's receipt
                               branch(uat)┘                                 ─► the evidence table: per environment,
                                                                               predicted · proven at its branch · transfers
```

Five consequences, each a line of a later milestone:

1. **A proof transfers by construction.** The record never reports a proof for an environment at a
   branch other than the one read from that environment (law T, M4).
2. **The Twin rarely needs a person.** A schema change is absorbed because the Twin is rebuilt from
   the repository at the ref. Data drift matters only when it changes a branch, and every
   prediction re-reads the live branch for exactly the tables the change touches.
   `check evidence` compares the committed evidence's branch with a live one and asks for
   `estate profile` only when they differ. The seed is the ISO week, so the population's identity
   renews weekly with nobody touching anything. This is the plan's answer to "a Twin that almost
   never needs maintaining".
3. **A lead proves as UAT without UAT's data.** UAT's branch is a list of booleans derived from
   counts, which the privacy closure admits; the copy minted at it holds only synthetic rows.
4. **A retype's branch comes only from a prediction.** Whether any value fails to convert is known
   only from the environment, counted read-only with the engine's own conversion. The mint then
   realizes one failing value, so the proof shows what the engine does with it. No committed file
   claims to know which values convert.
5. **The gap between the read and the deploy is not covered by either instrument.** The record
   says so in "Not checked", and the convergence oracle after the deploy closes it.

---

## 4. What this changes in the architecture, and what it keeps

| # | Topic | `V3_ARCHITECTURE.md` says | This plan | Why | Reverses if |
|---|---|---|---|---|---|
| 1 | Order | kernel and `emit` first (§14.2 steps 1–2); byte parity with v2's emitter before any lifecycle verb | the lifecycle first (M1–M6); `emit`, `decide`, `move` and the full OSSYS reader form the wing (W) | the lifecycle has consumers every day; the wing's only consumer is Prod's cutover, which v2 serves, frozen | a second estate, or a re-emission from OSSYS, needs the wing before Prod is done |
| 2 | Reading a live database | port v2's read side and profiler into `io/SqlServer` (~2,300 F# lines) | `TSqlModel.LoadFromDatabase` and the one walk for the schema (~210 lines); one probe executor for the data (~350) | the reader DacFx itself uses when it plans the pipeline's deploy; verified read-only (§1 facts 2, 5) | the estate needs a catalog fact DacFx does not model (none is known) |
| 3 | The delta | a typed nine-facet `Delta.between` in the kernel (~700) | `Change.Between` over `Element`s, covering every property DacFx knows (~260), with DacFx's own plan as the statement of what ships | complete by construction; an unmodelled change surfaces instead of vanishing | — |
| 4 | Building a package | `dotnet build` or MSBuild for proofs; the Twin's own `AddObjects` build | one build for everything: `dotnet build` with the committed DacFx targets | it carries the refactorlog and the pre- and post-deploy scripts (§1 facts 1, 8); the build engine is the pinned engine; no Visual Studio and no download | the hosted agent or a laptop cannot run it (M1's first check); then Visual Studio's MSBuild, stamped |
| 5 | The substrate | bake a `.bacpac` and an image, publish them as a pipeline artifact, restore | derived locally from committed inputs and cached by fingerprint; the bake lane retires | git is the only store; determinism makes any artifact a cache | an artifact store arrives: a restore path is added and nothing else changes (lifecycle prompt §9) |
| 6 | Prediction | `classify`, pure, from committed evidence | `classify` stays provisional; a new verb, `predict`, reads each environment's branch read-only from DacFx's own guards; proofs run at each environment's branch | the germ's two instruments and its transfer condition | — |
| 7 | Laws | twelve | fourteen: adds **P** (on a copy, prediction equals proof) and **T** (a proof is reported for an environment only at that environment's branch). Laws 1–5 take lifecycle forms here and keep their emit forms for the wing (Appendix C) | P holds every query v3 writes to the engine's behaviour; T is the transfer condition | — |
| 8 | Verbs | thirteen, with `knowledge package` used but not counted | twelve in the lifecycle (adds `predict`; counts `knowledge`) and three in the wing (`emit`, `decide`, `move`); `read --from ossys` moves to the wing | test 9 checks the verb list; the list should be true | — |
| 9 | Budget | ≤ 31,000 lines of C# (≈ 34,000 with the OSSYS reader) | ≤ 13,500 for the lifecycle; the wing at §6.6's own figures, if ported | §2.4 | a milestone's exit cannot be met under its ceiling: raise it with one decision line, never silently |
| 10 | Distribution | a pinned .NET tool from a feed (instruction architecture §9.6 (e)) | the published tool committed in Git LFS, run by `estate.cmd` and `estate.ps1` at the estate's root | the lifecycle prompt's R1 and R2 | a feed arrives: the folder becomes a tool manifest |
| 11 | Refreshing evidence | the bake lane, on a schema change and weekly | when a live branch disagrees with the committed one (`check evidence`, and every prediction) | §3 consequence 2 | — |
| 12 | The OSSYS reader | an optional package (~3,600) serving `read --from ossys` and `check outsystems` | a post-cutover subset (~250) serves `check outsystems`; the full reader is in the wing | the lifecycle asks the metamodel three questions, not for the whole model | — |

Everything else in the two design documents stands: the thesis (§5), the C# encoding and its
toolchain (§6.6), the exit codes and the versioned JSON contract, the kernel's purity and the
dependency laws, the knowledge tree's shape (§9), the values register, the documents and their
manifest, the two hooks and the permissions, the session protocols, the archive, and the write
budget. Where this plan names a module differently (`io/Publish` becomes the copy type's
`Publish` plus `io/Prove`; `io/SqlServer`'s read side becomes the walk), `VALUES.md`'s `Where`
column follows at M0.

---

## 5. The milestones at a glance

| M | Name | Afterwards, a person can… | Requirements (lifecycle prompt) | Laws green | Retires (to the archive) | C# added | When | Needs |
|---|---|---|---|---|---|---:|---|---|
| M0 | Ground | build and test an empty engine under its laws and budgets; read `AGENTS.md` | R24 (engine side) | the two dependency laws | four hooks (814 lines); root agent files | ~1,100 | day 1 | — |
| M1 | Read | ask, read-only, whether Dev matches the repository at a tag; see what a branch changes, property by property | R12, R13 (stamp and refusal), R14 (principal), R16 | 2′, 3′, "a named environment cannot be written" | the read side as a read path | ~2,260 | days 2–3 | M0 |
| M2 | Predict | see, read-only, whether a change blocks on each environment they can read, and why; a lead pastes QA and UAT | R11, R14 (allowlist), R16 | — (P waits for M4) | probe SQL in the skills | ~1,660 | days 4–5 | M1 |
| M3 | Twin | stand up a current Twin from the clone in one command, at branch, shape or scale; refresh evidence in one command | R3, R4, R5, R6 (population), R7, R8, R25 | 11, 12 | `bake.mjs`, the Twin CLI, `twin.json`'s evidence, scenarios | ~4,400 | days 2–8, lane T; branch tier by day 6 | M1 |
| M4 | Prove | prove a change on a fresh copy at each environment's branch, with a receipt, and see that it transfers | R6 (blocks at every tier), R9, R10, R13 (trust), R15, R27 (tool side) | 6, 7, 8, 9, 10, P, T | `prove.mjs`; the `sqlpackage` install step | ~720 | days 7–9 | M2; M3's branch tier |
| M5 | Record and gate | open a pull request whose body is generated and whose gate reproduces it from the clone in under ten minutes | R1, R2, R17, R18, R19, R24 (estate side), R26 (in part) | 1′ (record), 5′ | `inflight-check.mjs`; the placeholder pipeline; the ledger overwrite | ~1,320 | days 10–11; the record from day 6 | M4 |
| M6 | After deploy | see per environment whether it is deployed, converged, refreshed and republished, on one generated page | R20, R21, R22, R23 | 1′ (page) | — | ~740 | days 12–13 | M5 |
| M7 | Front door | work through Copilot or Claude Code along one authoring path and one reviewing path, generated for each agent from one tree | R26, R27, R28 | the instruction tests (Appendix D) | the gates and packager scripts; the three-agent hand-off; four surfaces per operation | ~500 | days 2–13, lane C | M0; M2's patterns |
| M8 | One tool | use one tool and one set of documents; the archive leaves `main` | — | all of them, generated into `LAWS.md` | the archive's CI | ~0 | Prod's cutover + 30 days | M7 |
| W | The wing | only if a consumer exists: re-emit from OSSYS; decide; move data | — | 1–5 in their emit forms; oracles 1 and 4 | v2 | §6.6 | on a decision | §17, item 9 |

The C# column sums to about 12,700 against the 13,500 ceiling; tests are budgeted at ≤ 10,000
(Appendix F). The days assume agents writing against executable exit checks, one independent
review per pull request, and the operator reviewing the kernel and contract pull requests.

**The calendar, four lanes.**

```
day              1    2    3    4    5    6    7    8    9    10   11   12   13
lane A  kernel   M0   M1   M1   M2   M2   M5·  M4   M4   M4   M5   M5   M6   M6
lane B  io/SQL   M0   M1   M1   M2   M2   M3   M4   M4   M4   M5   M5   M6   M6
lane T  twin     M0   σ    σ    σ    M3   M3   M3   M3   ·    ·    ·    ·    ·
lane C  words    M0   M7 ────────────────────────────────────────────────────── M7
lands                      drift          branch Twin    prove               page, pilot
                                     predict        shape Twin     gate
```

`M5·` on day 6 is lane A starting `Record` early, because nothing else in the kernel waits.

**What lights up when.**

| Station | The one command | Lit at |
|---|---|---|
| Can this machine do the work? | `estate doctor` | M1 (a stub at M0) |
| Is the baseline true? | `estate check drift --target env:dev --at <Dev's tag>` | M1 |
| Intent to operation | the agent, `knowledge/authoring.md` S0, with `estate classify` naming the operation | M7 (the verb at M2) |
| Edit the CREATE | the agent | M7 |
| Predict | `estate predict` (a lead adds `--env qa,uat`) | M2 |
| The substrate | `estate twin up` | M3 |
| Prove | `estate prove` | M4 |
| The record | `estate record` | M5 |
| Pull request and gate | the branch policy runs `estate gate`; a developer can run it first | M5 |
| Review | read the evidence table; `estate gate` to reproduce | M5 |
| Promote | Octopus, unchanged | — |
| Converged | `estate check drift` as the post-deploy step, or a lead's command | M6 (the verb at M1) |
| Refreshed; consumers republished | `estate check outsystems` | M6 |
| In sync, at a glance | the wiki page from `estate check environments --page` | M6 |

---

## 6. M0 — Ground (day 1)

**Outcome.** An empty engine that builds with warnings as errors under its budget and dependency
tests, beside a v1 and a v2 that still build from the archive; the documents an agent reads
first; and a published tool folder that already builds a classic project.

| WP | Lane | What | Done when |
|---|---|---|---|
| 0.1 | C | **Freeze and archive.** Tag `v2-final` at the commit v3 starts from. `git mv` the v1 trunk (`src/`, `tests/`, `config/`, `docs/`, `notes/`, `handbook/`, `ssdt-playbook/`, `schema/`, `tools/`, the solution, v1's root files and its `global.json`) into `archive/v1/`, and `sidecar/projection/` into `archive/v2/`. Re-path the six workflows so v2's lanes keep running from the archive until M8. Regenerate the `.claude/agents` and `.claude/skills` pointers against the new path with v2's packager, or remove them with a `NEXT.md` line until M7 regenerates them. Generate `archive/INDEX.md` (one line per document: path, date, status). The design documents stay at the root. | both archived solutions build and pass their fast suites with the counts they had before the move, recorded in the pull request |
| 0.2 | A | **The solution.** `Estate.sln`; `kernel/` (references the BCL and `System.Collections.Immutable` only; implicit usings off), `io/` (kernel, DacFx, ScriptDom, `Microsoft.Data.SqlClient`, Bogus), `cli/` (assembly name `estate`), `tests/{Kernel.Tests, Io.Tests, Budgets.Tests}` (xUnit, CsCheck, NetArchTest). `global.json` on the band S6 chooses, `rollForward: latestPatch`. `Directory.Build.props`: nullable, warnings as errors, deterministic, the culture and ordinal rules (CA1304, CA1305, CA1307, CA1309) as errors, `BannedApiAnalyzers` with `kernel/BannedSymbols.txt` (`System.IO`, `System.Net`, `DateTime.Now`, `Random`, `Task`). `Directory.Packages.props` with lock files, locked restore in CI. `.editorconfig`; `.gitattributes` (LF for text). | a planted `DateTime.Now` in `kernel/` is a build error; the solution builds clean |
| 0.3 | A | **The first kernel types.** `Seq<T>` (element-wise equality, ordinal-sorted construction), `Result<T>` and `Refusal` (code, message, remedy), `Name`, `Fingerprint` (SHA-256 over canonical bytes), and §3's `Receipt` and `Engine`. | CsCheck properties for `Seq`'s equality and order and for `Fingerprint`'s stability across processes pass |
| 0.4 | B | **The contract.** A verb table that generates `--help --json` and the schemas under `cli/schemas/`; the envelope (`schema`, `engine`, `receipt`, `verdict`, `findings`, `exit`); the frozen exit table (0, 1, 2, 3, 4, 5, 6, 7, 9, 130) with a test that it never shrinks; `io/Write.cs` (UTF-8 without a BOM, LF, atomic replace, an existing file's declared line ending preserved). | `estate --help --json` validates against its schema; the writer's tests pass on both CI systems |
| 0.5 | C | **The words.** `README.md`, `AGENTS.md`, `CLAUDE.md` (`@AGENTS.md` and the hooks), `VALUES.md` (the instruction architecture's §4, its `Where` column naming the test each milestone will add, written `pending M<n>` until then), `DECISIONS.md` (opened with this plan's decisions: C#; lifecycle first; `predict` added and `knowledge` counted; the wing; git as the only store; the TFM), `NEXT.md`, `ci/docs.manifest.json`, `ci/budgets.json` (Appendix F), `ci/packages.allow`, `.claude/settings.json` and the two hooks (start: make `dotnet` exist and run `estate doctor`; end: `estate twin down --if-idle`). | the fast tests in 0.6 pass over them |
| 0.6 | C | **The fast tests.** `Budgets`, `Manifest`, `NoSkips`, `PackagesAllowlist`, `Decisions`, `Register.Prose` (on `VALUES.md` first), `NoRestatedCounts`, the two dependency laws, and the archive exclusion. `ValuesResolve` accepts `pending M<n>` until that milestone's exit and fails after it. | green in the fast lane, under five minutes |
| 0.7 | B | **The tool folder and CI.** `ci/publish.{ps1,sh}`: `dotnet publish cli` into `dist/estate/`, then the package's `Microsoft.Data.Tools.Schema.SqlTasks.targets` and the reference stub (§1 fact 1) beside it, size printed. GitHub Actions: a fast lane on Ubuntu; a SQL lane on Ubuntu with SQL Server in Docker and on `windows-latest` with LocalDB, both running `Io.Tests`. | `dist/estate/estate --version` runs; the classic fixture builds against `dist/estate/` on both lanes |

**Day-one spikes, run where each fact lives.**

| Spike | The question | Where it runs | The answer lands in | Blocks |
|---|---|---|---|---|
| S1 | Does §1's build run on `windows-latest` and on a team laptop, and does the estate's project reference `master.dacpac` (so the tool folder must carry the system dacpacs)? | engine CI; one laptop | `ledgers/toolchain.md`; a decision line | WP 1.1 |
| S2 | Which guard shapes does the pipeline's engine emit for each archetype, and do they match the pinned engine's? | engine CI, both engines | `tests/Golden/scripts/` | WP 2.2 |
| S3 | Do `Script`, `LoadFromDatabase` and a probe run against Dev as the developers' group, and how long does `LoadFromDatabase` take there? | a developer's laptop | `STATE.md`; `ledgers/toolchain.md` | M1 exit 1 |
| S4 | How long does a LocalDB restore of a shape-volume Twin take at the estate's size? | `windows-latest`; a laptop | a decision line | WP 3.4 |
| S5 | Which OutSystems 11 metamodel tables hold consumer references and publish times? | Dev's platform database, read-only | `io/Ossys`'s SQL resources | WP 6.1 |
| S6 | Which .NET runtimes and SDKs do laptops and hosted agents have? | the corporate agent's `STATE.md` | the TFM decision line | WP 0.2 |

**Exit.**
1. `dotnet build Estate.sln` is clean with warnings as errors, and `dotnet test --filter Category=fast` is green.
2. `archive/v1` and `archive/v2` build and pass their fast suites with unchanged counts.
3. `estate doctor` prints `DEGRADED` with a remedy per missing item; it does not claim M1 exists.
4. The classic fixture builds against `dist/estate/` on Ubuntu and on `windows-latest`.

**Retires.** The four hooks (814 lines), the root agent files and `sidecar/projection/CLAUDE.md`,
all archived with their trees.

**Watch for.** The TFM. Target `net10.0` if S6 finds the .NET 10 runtime on every laptop and
hosted agent (Visual Studio 2026 installs it). Otherwise target `net8.0` with `RollForward=Major`,
which runs on 8, 9 or 10, and move to `net10.0` by one decision line when the rollout lands. If a
laptop has no suitable runtime at all, publish self-contained `win-x64`; it adds about 70 MB to
LFS and removes the question.

---

## 7. M1 — Read (days 2–3)

**Outcome.** From a clone, one command says, read-only and as the caller's own identity, whether
an environment matches the repository at a tag, and names each object that differs. Another says
what a branch changes, property by property, for every object type DacFx knows.

| WP | Lane | What | Done when |
|---|---|---|---|
| 1.1 | B | `io/Ssdt.Build`: §1's command against the committed tool folder, output under `.estate/build/<sha>/`; Visual Studio's MSBuild through `vswhere` when no .NET SDK is present, stamped in the receipt; exit 7 with the log's errors. `Load`; the refactorlog read. A ref builds through `io/Git.At`. | the fixture builds on both lanes with the refactorlog and both deploy scripts inside; a broken `.sql` exits 7 naming the file |
| 1.2 | B | `io/Ssdt.Walk`: a `TSqlModel` into `Seq<Element>`, generic over `ObjectType.Properties` and `.Relationships`, descending from each top-level object through its composing relationships (columns, constraints, indexes); values into the kernel's closed `Value` (boolean, integer, string, enumeration name, null). No code per type. | WP 1.3's properties pass against it |
| 1.3 | A | kernel `Element` and `Change`: canonical order; `Fingerprint.Of(Seq<Element>)`; `Change.Between(before, after, renames)` into added, removed, renamed (from the refactorlog) and changed (per property, before and after). Pure; CsCheck over generated element sets. | law 3′ passes; the diff of each archetype pair names exactly the property the archetype changes |
| 1.4 | B | `io/SqlServer`: the target grammar (`env:<name>` from `estate.environments.json`, `copy:<name>`, `twin`, `ref:<git ref>`, `dacpac:<path>`); the `Named` and `Copy` types, with `Publish` only on `Copy`; connection references (`env:VAR`, `file:path`, integrated security by default; a literal credential is exit 6); `Model(target)` through `LoadFromDatabase` and the walk; `Plan(package, target, profile)` through `DacServices.Script` with both outputs; the probe executor (one statement, parsed and checked against the allowlist before it runs: the outermost select list holds aggregates or constants only; no `INTO`, `EXEC`, DML, DDL, `OPENROWSET` or system procedure; each statement and its row count appended to `.estate/runs/<id>/queries.log`). | the compile-fail test and the allowlist's fuzz test pass |
| 1.5 | A | kernel `Environment`, and profile loading in `io`: `estate.environments.json` (name; classification `synthetic` or `real`; the cohorts that read it; the connection reference; the publish profile; SQLCMD values; the metamodel's connection reference); `DacProfile.Load(path).DeployOptions`; Strict is the pipeline's profile as loaded (§1 fact 10), and Permissive is the same with only `BlockOnPossibleDataLoss` flipped and can be constructed only for a `Copy`; a named environment whose profile has the guard off is a refusal. | the refusals carry remedies and pass `Register.Refusals` |
| 1.6 | B | `io/Git`: `At(ref)` as a detached worktree under `.estate/worktrees/`, swept; `MergeBase`; `ChangedPaths`. | two concurrent builds of two refs share nothing |
| 1.7 | B | the verbs `doctor` (the SDK and runtime; the committed tool and its DacFx against `ledgers/toolchain.md`; the build route; LocalDB and Docker; LFS), `read`, `diff`, `check drift`. | each verb's JSON validates against its schema |
| 1.8 | C | `tests/Golden/`: the proving ground's project in both formats (its SDK-style original and a classic twin), its three profiles, and the read-only principal fixture (a login with `VIEW DEFINITION` and `db_datareader` only). | Appendix E runs as `Io.Tests` |

**Exit.**
1. On a team laptop, as the developer's identity, `estate check drift --target env:dev --at <Dev's
   deployed tag>` exits 0 with "Dev matches <tag>", or 5 naming each differing object, within a
   minute (R12). In CI every named-environment call runs as the read-only principal (R14).
2. `estate diff --from ref:main --to ref:HEAD` on the make-mandatory archetype prints
   `Column [dbo].[Customer].[Email]: Nullable true → false` and nothing else.
3. Law 2′, *a published copy converges*: publish the golden to a fresh copy and `check drift`
   exits 0; alter one column on the copy and it exits 5 naming it.
4. Law 3′, *the read is complete*: two builds of one project fingerprint equally; every archetype
   edit changes the fingerprint; the walk of a published copy agrees with the walk of its package
   under the profile's ignore rules.
5. *A named environment cannot be written*: `Publish` does not compile against `Named`, and
   Permissive cannot be constructed for it.
6. R13's stamp: every receipt names its engine; a `ledgers/toolchain.md` pin more than one version
   from the committed engine is exit 6.
7. R16: a connection reference that resolves to a literal password is exit 6; a denied login
   prints one sentence naming the environment and saying a lead's prediction will appear on the
   pull request.

**Retires.** v2's read side as the lifecycle's read path. It stays in the archive as the oracle for
M3's typed lens.

**Watch for.** S1 and S3 are this milestone's first work. DacFx's own catalog queries are not ours
to allowlist; R14's check for them is the read-only principal, and for ours it is the log.

---

## 8. M2 — Predict (days 4–5)

**Outcome.** A developer asks, before anything is published anywhere, whether their branch would
block on Dev and why. A dev lead asks the same of QA and UAT and pastes one block into the pull
request. Nothing is written, and no value leaves an environment.

| WP | Lane | What | Done when |
|---|---|---|---|
| 2.1 | A | kernel `Claims` and `Outcome.Predict`. The claim table is small and pure: `Nullable true → false` is NotNull; a type, length, precision or scale change that narrows is Fits; a new foreign key is Reference; a new primary key, unique constraint or unique index is Unique; a new or changed check is Check; a guard site is Presence. `Branch.Of(counts)`; `Outcome.Predict(branch, guards, claims)` gives `applies` or `blocks` and the shape, two releases when a populated Presence site guards a tightening. | CsCheck properties pass, among them F7 as a property: a populated guarded table predicts `blocks` however clean its rows |
| 2.2 | B | the guard lens in `io/SqlServer.Plan`: drop SQLCMD directive lines; substitute `$(var)` from the environment's SQLCMD values; parse with `TSql160Parser`; collect each `IF EXISTS (…) RAISERROR` at state 127 (§1 fact 3) with its table and the statements it guards; re-generate the predicate and run it verbatim through the executor. | the lens finds exactly the recorded guards in S2's scripts, from both engines |
| 2.3 | B | `io/Probes`: one ScriptDom builder per claim kind, each asking the engine rather than re-deriving it. NotNull: rows where the column is null. Fits: rows whose value does not survive the engine's own conversion to the new type. Reference: child rows with every key column non-null and no parent. Unique: groups over the key with more than one row, under the constraint's own null semantics. Check: rows where `NOT (<CheckConstraint.Expression>)` holds, which counts false and not unknown, as SQL Server does. | every builder's output passes the allowlist; law P (M4) then holds each to the proof |
| 2.4 | A | kernel `Classify`: a matcher over the change set and the committed evidence, with patterns read from each operation file's `recognize:` block (until M7 moves them there, from `tests/Golden/patterns/`, seeded with the twelve most frequent operations). Its output's first word is *provisional*. It raises the two findings no publish can: a rename without its refactorlog entry (a removed and an added column of one type at one ordinal, with no entry), and a column-list change on a table in `ledgers/cdc-tracked.md`. A change no pattern matches is *unclassified change: prove it*. | every archetype classifies as its operation; the rename-by-typing archetype is refused with its remedy |
| 2.5 | B | `io/Predict` and the verbs `predict`, `classify`, `check cdc`. For each requested environment the caller can reach: build head, plan against it, lift the guards, derive the claims, run the probes, and write `estate.predict/1` and a Markdown block whose last lines are a fenced machine block (the branch per environment, the counts, the receipt). `check cdc --env <e>` reads `sys.tables.is_tracked_by_cdc` (a catalog read) and prints the rows for `ledgers/cdc-tracked.md`; with `--commit` it commits them on a new branch for review. | the JSON validates; the block round-trips through the gate's parser |
| 2.6 | C | the corpus begins: `tests/Golden/changes/<operation>/` holds, per archetype, the edit to the golden project, the expected claims, and the expected prediction on an empty, a populated and a violating copy, ported from v2's `SamplePr*` facts. | the archetypes that exist so far run in one parameterized test |

**Exit.**
1. On a branch that makes a populated column NOT NULL, a developer's `estate predict` prints,
   labelled *predicted · read-only · time · engine*: Dev blocks; the guard is row presence on
   `dbo.Customer`; this many rows would violate NOT NULL on `Email`; it ships in two releases (R11).
2. A dev lead's `estate predict --env qa,uat` prints the pasteable block with its machine block. A
   developer's run against UAT fails at the login and says in one sentence that a lead's
   prediction will appear on the pull request (R16).
3. The query log of both runs holds only the allowlist's shapes beside DacFx's own reads (R14).
4. The canary scan, first half: a fixture environment classified `real` with a planted value, run
   through `predict` and `classify`; the value appears in no output, log or block (R7).
5. `estate classify` on the rename-by-typing archetype refuses and names the remedy
   (`estate diff --refactorlog`, M4).

**Retires.** The probe SQL scattered through the skills; the skills cite the verb from M7.

**Watch for.** Retype semantics. The engine's explicit conversion and the implicit one an `ALTER
COLUMN` performs can differ for some pairs (styled dates, some numeric narrowings). Law P catches
a difference on every archetype; a pair with no archetype predicts with the line *probe semantics
unproven for this conversion* until one is added.

---

## 9. M3 — Twin (days 2–8, lane T; the branch tier by day 6)

**Outcome.** One command, from the clone alone, stands up a Twin current to any ref: the
repository's schema built and published the way the pipeline does it, with rows minted from the
committed evidence and the week's seed, at branch, shape or scale volume. The second time takes
seconds. A lead refreshes the evidence with one command, and only when a live branch says it is
stale.

The first four work packages land by day 6, because M4 proves on the branch tier.

| WP | Lane | What | Done when |
|---|---|---|---|
| 3.1 | T | kernel `Schema`, the typed lens σ reads over `Element`s (tables; columns with their type facets; keys; references; uniques; checks; defaults; identity; computed and temporal columns flagged), and `Order` (a mint order over references; nullable legs deferred to a second pass; a cycle with no nullable leg is a refusal naming it). | `Schema.Of` over a published copy's walk equals, mapped, the catalog v2's read side writes as JSON for the same copy (v2's CLI, run from the archive) |
| 3.2 | T | kernel `Evidence`: measures per table, column, reference and unique candidate; disclosure classes (count, bucket, vocabulary), where a vocabulary can be constructed only with a synthetic environment as its provenance, so the committed shape tier is literal-free by type; row tiers; and the derivation rule for a column the evidence predates (null in every existing row unless a default fills it). A codec with a round-trip property. | the codec's round trip and the literal-freedom constructor tests pass |
| 3.3 | T | kernel `Synth`, σ, ported with its tests first from v2's `SyntheticData.fs`, `SyntheticCorrection.fs`, `SyntheticVolume.fs`, `Centrality.fs`, and `Twin.Core`'s `Evidence.fs` and `DerivedEvidence.fs`. The generator is a parameter (no `Random` in the kernel); the seed is the ISO week unless pinned; three tiers. The **branch tier** is exact to R6 (a row where the environment has rows, a null where it has nulls, an orphan where it has orphans, two equal values where it has duplicates, a value at the recorded maximum length), and given a `Branch` it realizes that branch and nothing more. | v2's minted rows at one seed, per table ordered by key, are byte-identical (law 11's differential half) |
| 3.4 | B | `io/Substrate`: LocalDB through `sqllocaldb`; Docker only when the image is already on the machine (the tool never pulls); or a named server. Per-session names (`estate_<fingerprint6>_<pid>`); a sweep of anything older than a day; backup and restore into `.estate/cache/`. | two concurrent runs share nothing; a killed run is swept by the next |
| 3.5 | T | `io/Twin` and `io/Realize`, and `twin up [--tier branch\|shape\|scale] [--at <ref>] [--branch <file>]`: build at the ref (M1); create; publish under the pipeline's profile with drop-not-in-source, so the post-deploy seeds run as they do in every environment; mint; bulk-load in mint order; fill the deferred legs; validate every constraint `WITH CHECK CHECK` and refuse by name on a violation; back up into the cache under the fingerprints of the schema, evidence, seed, tier and branch. Shape-tier values are realized with Bogus in `io`, never in the kernel. The Twin carries no state table (the fingerprints name its cache entry), so its schema is exactly the repository's and `check drift --target twin` is its health check. | `check drift --target twin --at <ref>` exits 0 after `twin up --at <ref>` |
| 3.6 | T | `twin check`, `status`, `down`: mint twice with identical digests; zero orphans by the Reference probe; re-profiling the mint recovers the evidence within ε; converged by the drift oracle. | law 11 green |
| 3.7 | B | `io/Profile` and the verbs `profile` and `check evidence`. `estate profile --env dev [--vocabulary] [--commit]` runs M2's builders over every claim site the schema declares, plus buckets, and writes `evidence.json`; for an environment classified real it writes only row tiers, into `ledgers/row-tiers.md`, with the environment and the date on every row; `--commit` makes one commit on a new branch and prints the push. `check evidence` compares the committed evidence's branch with a live one, and refuses a file that is not literal-free. | law 12 green; the canary scan's second half passes |

**Exit.**
1. On a clean laptop with LocalDB and nothing but the clone, `estate twin up` reaches a current
   Twin at shape volume in under ten minutes, and from the cache in under a minute (R5).
2. `estate twin check` passes; two mints under one seed are byte-identical (R8; law 11).
3. At branch volume, probes on the Twin find each of R6's population rules met.
4. The canary scan, second half: `profile` against the fixture classified `real` stores no value,
   and `check evidence` refuses a file carrying a planted literal (R3, R7; law 12).
5. No `.bak`, `.bacpac`, `.dacpac` or minted file is tracked, and `.estate/` is ignored (R4); two
   concurrent `twin up`s share nothing and a killed one is swept by the next (R25).

**Retires.** `bake.mjs` (251 lines); the Twin CLI; `twin.json`'s evidence section; the scenario
compiler (branches and tiers replace scenarios); the hand-seeded proving ground as a substrate (it
stays as a fixture).

**Watch for.** σ is the largest new code (about 1,450 lines) and the only piece with a byte
oracle; port its tests first and keep the seed fixture red until it goes green. S4 decides whether
a fresh copy for a proof is a restore (assumed under twenty seconds) or a branch-tier mint from
scratch (seconds, always available); proofs use whichever is faster, and the shape tier keeps the
restore.

---

## 10. M4 — Prove (days 7–9)

**Outcome.** One command proves a change on a fresh copy at each environment's branch, under the
pipeline's own profile and the pinned engine, and returns one JSON object, one exit code and a
receipt. From here the record can say, per environment: predicted, proven at its branch,
transfers.

| WP | Lane | What | Done when |
|---|---|---|---|
| 4.1 | B | `io/Prove`. For each distinct branch (Dev's by default; those in a prediction file or a PR body's machine block with `--branch-from`): a fresh copy (a restore of the cached Twin at that branch, or a branch-tier mint, never the Twin itself); build head; plan against the copy; publish **Strict** (the pipeline's profile, guard on) and record *published*, *blocked* with the message verbatim, or *failed* with it; on a block, publish **Permissive** on the same copy and record the consequence (rows before and after per touched table, widths, trust); read back trust (`is_not_trusted` on every foreign key and check, a catalog read); conservation hashes for a multi-phase step; plan again and require it empty; CDC silence where the substrate has CDC, otherwise *not provable here*; the receipt (the copy's name, the script's SHA-256, the engine, the time, the five fingerprints); drop the copy unless `--keep`. | the make-mandatory archetype yields exit 3 with the verbatim message and a receipt |
| 4.2 | A | kernel `Outcome.Prove`, `Readback`, `Transfers`; the shapes (`one release`, `two releases`, `refused`), each with its reason. | law T as a kernel property |
| 4.3 | B | the verb `prove` (`--target` accepts `twin` or `copy:` only; `env:` is exit 9 with "prove never publishes to a named environment; use predict"), and the refactorlog writer: `estate diff --refactorlog <old> <new>` writes the entry with `XmlWriter`, with v2's `RefactorLogEmitter` as the specification. | law 6 green |
| 4.4 | C, A | the corpus completed: every operation archetype and the compound case, each with its expected classification, its prediction per branch, and its proof; one parameterized test runs them all (the proof lane: nightly, both CI systems). | the proof lane green on the pinned engine |
| 4.5 | B | laws 6, 7, 8, 9, 10, P and T as `Io.Tests` (Appendix C). | all green |

**Exit.**
1. The make-mandatory archetype: `estate prove` exits 3 with the guard's message verbatim, the
   Permissive consequence and a receipt, and the record line for Dev reads *predicted blocks ·
   proven blocked at Dev's branch · transfers* (R9, R10).
2. The corpus is green on the pinned engine. Law P holds on every archetype: the prediction on a
   copy equals the proof on it, outcome and counts. Law T holds: no proof is reported at a branch
   it was not proven at.
3. R6's check: NOT NULL on a populated table, a reference over recorded orphans, and a narrowing
   below the recorded maximum each block at branch, shape and scale.
4. R13's trust check: a declaratively added foreign key ends `is_not_trusted = 0` on the pinned
   engine (law 10), or the verdict is a refusal naming the engine. The first real Dev deploy of
   such a key is read back with `check drift` and the trust probe and recorded in
   `knowledge/findings.md` with its receipt.
5. R15: `prove --target env:dev` is exit 9, and M1's compile-fail test still holds.

**Retires.** `prove.mjs` (427 lines) and its `sqlpackage` dependency; the `sqlpackage` install
step in the pipeline.

**Watch for.** Until the Octopus step's engine is pinned (§17, item 1), every proof is on the
committed engine and says so, and law 10's meaning is relative to it.

---

## 11. M5 — Record and gate (days 10–11; the record from day 6)

**Outcome.** The pull request body is generated from receipts and leads with an evidence table,
and the three sections only a person can write are placeholders the gate will not let through. A
branch policy runs the same gate on a hosted Windows agent from the clone alone, in under ten
minutes, and posts the table.

| WP | Lane | What | Done when |
|---|---|---|---|
| 5.1 | A | kernel `Record` and `Locks`. The evidence table first (the build; the delta, with its data-loss steps and renames with refactorlog presence; the prediction per environment, or *pending a lead*; the proof per branch; baseline drift; in-flight collision; the engine pin; consumers to republish, or *not checked*), then the ten sections of `knowledge/record.md`, with placeholders for the intent, the business answer with its owner, and what was not checked. The register lint (the banned list) as a pure function. `Locks`: open windows from `ledgers/in-flight.md`; a delta with data-loss steps on more than one table and no program row. | a record over the corpus passes `Register.Samples`' rules; placeholders are detected by position |
| 5.2 | B | `io/Gate` and the verbs `gate`, `record`, `check inflight`. Base and head from git; the lock checks (exit 9 with the row); the branches from the PR body's machine blocks, or Dev's from the committed evidence when none is present; the Twin at branch volume; a proof per branch; the record regenerated and diffed against the body's evidence table, "How it ships" and "What proving showed"; placeholders and banned words refused by line; `changelog.json` (added, removed, renamed with refactorlog keys, retyped; the open windows); `gate.json`; exit 0, 3 with the verdict attached, 9, or a tooling code that says it is one. | `estate gate` run locally and in CI on one branch give identical `gate.json` apart from the receipt's place and time |
| 5.3 | C | `ci/azure/`: `gate.yml` (`windows-latest`; checkout with LFS; start LocalDB; `.\estate.cmd gate …`; a host step of at most twenty-five lines of PowerShell that writes the PR body to a file and posts the evidence table as a comment with the job's access token), its README with the branch-policy steps (a YAML `pr:` trigger does not fire on Azure Repos), and the post-deploy snippet M6 uses. | the template runs end to end when queued by hand on a scratch pull request in the estate, before the policy is registered |
| 5.4 | B | `estate knowledge vendor --to <estate checkout>`: `tools/estate/` (the published tool, the targets, the reference stub), `estate.cmd` and `estate.ps1` at the estate's root, the LFS rules for the tool's binaries in `.gitattributes`, `.estate/` in `.gitignore`, the pipeline templates, and `estate.environments.json` and the ledgers seeded *only if absent*. v2's packager overwrote the ledgers (`ssdt-agent-package.mjs:604–632`); a test pins that a second vendoring changes no ledger byte. | the second-vendoring test passes |

**Exit.**
1. A real pull request on the estate: the gate runs end to end in under ten minutes; its log holds
   no download; the evidence table is posted (R17).
2. A body with a placeholder or a banned word fails, naming the line; a body whose "How it ships"
   disagrees with the proof fails, naming the section (R18).
3. A pull request touching a table in an open window fails with exit 9 naming the ledger row; so
   does a delta with data-loss steps on two tables and no program row (R19).
4. On a clean laptop, `git clone` then `.\estate.cmd --version` works; `git lfs ls-files` lists the
   binaries; the repository's non-LFS size grew by under a megabyte (R1, R2).
5. The drift oracle is exact on a Windows checkout with `core.autocrlf=true` and on Linux (R24).

**Retires.** `inflight-check.mjs` (145 lines); the placeholder pipeline; the ledger overwrite.

**Watch for.** The hosted image's LocalDB start and the ten-minute budget at the estate's size.
The branch tier mints in seconds, so the builds and the publishes are the long pole: environments
that share a branch share one proof, and the base is published once and restored per distinct
branch. How the tool folder
crosses into the corporate network is §17's item 14.

---

## 12. M6 — After deploy (days 12–13)

**Outcome.** After Octopus deploys, one read-only step says whether the environment converged.
After someone refreshes the extension in Integration Studio, one read-only check says whether it
maps the repository's attributes, and names the modules still to republish. One generated page in
the wiki shows every environment at once.

| WP | Lane | What | Done when |
|---|---|---|---|
| 6.1 | B | `io/Ossys`: the metamodel subset as committed SQL, run through the executor's catalog mode (metadata rows, no user data): external entities (`ossys_Entity` with `Is_External` and `Physical_Table_Name`) and their mapped attributes (`ossys_Entity_Attr`), both already read by v1's rowset SQL, whose fragments are reused verbatim; consumer references and publish times from the tables S5 names. | the queries run as the read-only principal against a fixture of metamodel rows |
| 6.2 | A | kernel `Platform.Compare`: per external entity, *current*, *refresh pending* (the repository has an attribute the extension does not map yet), or *stale mapping* (the extension maps an attribute the repository dropped: a runtime error waiting); consumers to republish (published before the extension was). `Page.Of`: one row per environment (the deployed tag; converged; refreshed; consumers republished; open windows; baseline drift; the engine of the last deploy; Prod greyed until profiled). | CsCheck properties over generated platform views pass |
| 6.3 | B | the verbs `check outsystems` and `check environments --page <wiki checkout> [--commit]`. The page carries a banner and the fingerprints of its inputs, so regeneration is byte-identical and a hand edit is detectable. | law 1′ for the page |
| 6.4 | C | the post-deploy step: `estate check drift --target env:<e> --at <tag>`, then `estate check outsystems`, then the page. It runs as an Octopus step or a release stage where a worker can reach the environment, and as a lead's one command until one can. | the step's snippet is in `ci/` with its README |

**Exit.**
1. After a real Dev deploy, `check drift` exits 0 (R20).
2. After a real refresh in Integration Studio, `check outsystems --env dev` moves from *refresh
   pending* to *current* with no input, and consumers to republish are listed (R21, R22).
3. The page regenerates byte-identically from the same inputs, is committed to the wiki, and greys
   Prod (R23).

**Watch for.** This plan does not assert which OutSystems 11 tables hold consumer references and
publish times. S5 names them from Dev's platform database before WP 6.1 starts.

---

## 13. M7 — Front door (lane C, days 2–13)

**Outcome.** A developer in Visual Studio with Copilot, or a maintainer in Claude Code, meets one
authoring path whose every step is a verb, one reviewing path, one page for the record, the
operation catalog with its recognition patterns, and instructions generated for each agent from
one tree.

Work runs in the order the instruction architecture fixes (its §11): citations first, deletions
last.

| WP | What | Done when |
|---|---|---|
| 7.1 | Citations rewritten: findings by identifier, samples by shape, handbook by title, ledgers by their new path. | the citation test is green at every commit |
| 7.2 | `knowledge/`: `README.md`, `authoring.md` (S0 to S8, each with its verb), `reviewing.md`, `record.md`, the operations with their `recognize:` blocks (moved from `tests/Golden/patterns/`; the classifier reads whatever files exist), the shared reasoning, `findings.md`, twelve samples, the ledgers (with `cdc-tracked.md`), the eight handbook chapters. The entry skill's first step is the compound check (R28). The router's rule when `estate` is absent: stop, author the change, open the pull request marked *provisional*, and let the gate prove it (R27). | the knowledge budgets hold (≤ 11,000 lines; ≤ 90 per operation) |
| 7.3 | `io/Knowledge` and the verb `knowledge package [--check] [--vendor]`, driven by `knowledge/targets.json`: the Copilot router, five path-scoped instructions, two agents, one prompt, the skills index, the two schema pull-request templates, and the `.claude/` pointers. | `knowledge package --check` is clean; the instruction tests are green (Appendix D) |
| 7.4 | The pilot: one champion laptop, the vendored bundle, one real change from intent to pull request. The rung that held is a decision line. | the decision line exists |

**Exit.** `estate knowledge package --check` is clean; every instruction test is green; the first
vendoring pull request is merged in the estate; and a new developer, given only the clone, takes a
change from intent to a pull request with the evidence table in one session (R26).

**Retires.** `ssdt-agent-gates.mjs` (370 lines) and `ssdt-agent-package.mjs` (682); the
three-agent hand-off; the self-test rubric; four surfaces per operation.

---

## 14. M8 — One tool (Prod's cutover plus thirty days), and W — the wing

**M8.** The wing is decided. `archive/` leaves `main` for an `archive` branch and the `v2-final`
tag, and its CI is deleted. `LAWS.md`, `cli/VERBS.md`, `cli/CONFIG.md` and `DOCS.md` are
generated; `ARCHITECTURE.md` is written from the two design documents and this one, which move to
`archive/design/`. `ValuesResolve` stops accepting `pending`. Each budget drops to what was used
plus a tenth.

**W, only on a decision.** If a consumer exists (a second estate, a re-emission from OSSYS, the
reverse leg), port `read --from ossys`, `decide`, `emit` and `move` as `V3_ARCHITECTURE.md` §8.1,
§8.3, §8.4 and §8.11 describe them, at §6.6's budgets, held by oracle 1 (byte identity with v2's
emitter on the golden and on the estate), oracle 4 (v1's edge-case fixtures), and laws 1 to 5 in
their emit forms. The ported renderer and emitter read the same `Element`s and write through the
same `io/Write`. If no consumer exists, the wing leaves the archive's CI and stays findable at
`v2-final`.

---

## 15. The estate side: how v3 reaches the corporate repository

v3 is built in this repository. It is used in the corporate one, where a backported v2 is being
extended to the lifecycle today under `LIFECYCLE_BACKPORT_PROMPT.md`. The two meet through the
requirements, not through code.

1. **The R-checks are the acceptance tests for both.** Every exit above names the requirements it
   meets, with the checks the backport prompt states. Whichever implementation passes a
   requirement's check first serves it; v3 replaces a backported station only when its own check
   passes on the estate.
2. **Early vendoring.** From M1 the published folder can be copied into the estate by hand
   (`tools/estate/` and the two shims) and run beside the backport: `check drift` from day 3,
   `predict` from day 5. Running both on one branch is a differential test in the field, and a
   disagreement is a finding for both.
3. **One thing crosses the network:** the tool folder and its shims, as a pull request to the
   estate (§17, item 14). No data crosses in either direction, and no knowledge outside the
   vendored bundle.
4. **The estate owns** `estate.environments.json`, the ledgers, the evidence, the publish profiles
   and the SSDT project. Vendoring seeds each once and never overwrites it (M5's test); the verbs
   that propose a change to one (`profile`, `check cdc`) make a commit on a new branch, for review.
5. **The corporate agent's `STATE.md`** answers S1, S3, S5 and S6 on its first day. Those are the
   facts this repository cannot measure (§1's last paragraph).

---

## 16. How agents build it

- **Four lanes, one pull request per work package.** A: the kernel. B: io and SQL. T: σ and the
  Twin. C: knowledge, documents and CI. A work package is a pull request of about six hundred
  changed lines at most, its test written first. The test encodes the "done when", and the pull
  request cannot merge red.
- **The specification is named per port.** A work package that ports names the v2 files it ports
  from (the ledger is `V3_ARCHITECTURE.md` Appendix D), ports their tests before the code, and is
  reviewed against them. Where v2 produced an output, that output is the oracle: σ's rows, the
  read side's catalog, the recorded verdicts.
- **Every session keeps the instruction architecture's protocol (a).** The doctor line;
  `AGENTS.md`; `NEXT.md`; the package's README. It works under the laws and the budgets, rewrites
  `NEXT.md`, and adds at most one line to `DECISIONS.md`. A question only the operator can answer
  goes under *Waiting on a person*, and the session stops that thread instead of guessing.
- **Review.** The operator reviews the kernel and contract pull requests, because they define what
  a lead will read. Every other pull request gets one independent review by a fresh session
  before it merges; a finding is fixed in the same pull request or refused in one line.
- **What no session writes:** a chapter, a handoff letter, a plan, a vision, a self-assessment.
  The pull request is the handoff.
- **Where strength goes.** Planning-strength sessions take the kernel types, the claim table, the
  allowlist and the contract. Standard sessions take adapters and ports against named tests.
  Light sessions take citation rewrites and mechanical moves.
- **The brake is the budget test.** A work package that cannot meet its exit under its ceiling
  stops and asks for one decision line. It never lands over the ceiling.

---

## 17. Decisions and prerequisites, by what they gate

| # | Decision or prerequisite | Owner | Gates | Default assumed, so no milestone waits |
|---|---|---|---|---|
| 1 | The engine version of the Octopus publish step, pinned | release engineering | M1 exit 6's meaning; M4 exit 4 | commit DacFx 162.5.57; every receipt stamps it; the ledger row stays `UNPINNED` and every proof says so |
| 2 | The environment classification, confirmed by the leads | the dev leads | M3 (profiling anything classified real) | Dev and QA synthetic; UAT and Prod real |
| 3 | Read access per cohort, and read on the metamodel tables | the owners of the AD groups | M1 exit 1; M2 exit 2; M6 | developers already read Dev; the leads' access to the metamodel is requested on day one |
| 4 | Git LFS enabled on the estate | the Azure DevOps administrator | M5 | the engine repository uses LFS meanwhile |
| 5 | The gate registered as a branch policy | the Azure DevOps administrator | M5 exit 1 | the gate is queued by hand on a pull request until then |
| 6 | One dev lead for evidence refreshes and QA and UAT predictions | the operator | M2 exit 2; M3 exit 4 | the operator |
| 7 | The runtimes and SDKs on laptops and hosted agents (S6) | IT; the corporate agent's `STATE.md` | WP 0.2 | `net10.0` if present everywhere; else `net8.0` with `RollForward=Major`; self-contained `win-x64` as the fallback |
| 8 | The Visual Studio Copilot pilot | the operator and one champion | M7's exit | the 18.4 rung (`V3_ARCHITECTURE.md` §16.1, item 2) |
| 9 | The wing: will the reverse leg run, and will anything re-emit from OSSYS | the operator | W; M8 | frozen in the archive; no port |
| 10 | Which tables CDC captures, per environment | the dev leads | M2's CDC finding; M4's CDC proofs | `check cdc` proposes the ledger on day five; CDC proofs run only on Docker or a server |
| 11 | Prod's counts before its first release | the operator | the page's Prod row; any prediction for Prod | Prod greyed; `estate profile --env prod --counts` before its first release |
| 12 | The reviewers roster | the operator | nothing in code; the gate is load-bearing until the roster is data | `V3_ARCHITECTURE.md` §16.1, item 5 |
| 13 | One repository or two | the operator | the vendor contract | two, as both design documents assume |
| 14 | How the tool folder crosses into the corporate network | the operator and IT | WP 5.4 on the estate | a person copies the published folder into a pull request |

---

## 18. Risks this plan adds or sharpens

- **The build on the estate's real project.** §1 fact 1 was measured on a minimal project on
  Linux. The estate's project may carry database references, SQLCLR or build settings that need
  more of Visual Studio. S1 answers on day one; the fallback is Visual Studio's own MSBuild,
  stamped; either way it is the project's own build, never `AddObjects`.
- **Probe semantics drifting from the engine.** Every query v3 writes is a claim about what the
  engine will do. Law P holds each one to a proof on every archetype, and a conversion with no
  archetype is labelled unproven.
- **The time-of-check gap.** A prediction reads a moment, and UAT can change before the deploy.
  The record says so; a lead predicts again on the day of promotion; the convergence oracle
  closes it after the deploy.
- **A pasted block is only as honest as the lead who pasted it.** A hosted agent cannot reach QA
  or UAT, so the lead's machine block is the prediction of record. The gate checks its
  consistency (the receipt's schema fingerprint against the head package, the identity and the
  time) but not its origin. A self-hosted agent with read access (the backport prompt's
  prerequisite 7) retires the paste.
- **σ's port.** The largest new code and the only byte oracle. Tests first; the seed fixture; lane
  T starts on day two.
- **The engine is still unpinned.** Until §17 item 1 lands, every trust-state finding is relative
  to the committed engine. The stamp, the ledger row, the refusal beyond one version, and the
  first real Dev foreign-key deploy read back (M4 exit 4) bound it.
- **The walk at scale.** A 300-table estate is on the order of a hundred thousand property values:
  small in memory, but `LoadFromDatabase`'s time on Dev is unmeasured until S3.
- **This plan's own gravity.** Thirteen days of agents can still become v2's curve. The budget test
  is the brake, the write budget is the rule, and after M0 `NEXT.md` is the only plan.

---

## 19. The first hour

Open one pull request on the skeleton of WP 0.2: `tests/Io.Tests/SpikeTests.cs`, Appendix E as four
assertions. The build carries the refactorlog and the post-deploy script. `Script` runs as the
read-only login. The guard is found at state 127, and its predicate returns 1 on a populated
table. The deploy report of a converged copy has no operations. When it is green on both CI
systems, M0 has begun, and every fact in §1 is a test instead of a sentence. Then send S3 and S6 to
the corporate agent as the first two questions in its `STATE.md`.

---

## Appendix A — The requirements, mapped

The lifecycle prompt's requirements, each with the milestone that meets it and the exit that
checks it. The check is the prompt's own, so it holds for the backport and for v3 alike (§15).

| R | Statement, short | Milestone | Checked at |
|---|---|---|---|
| R1 | the tool is committed and runs on what laptops have | M5 | M5 exit 4 |
| R2 | binaries in LFS, pinned by commit, pruned | M5 | M5 exit 4 |
| R3 | committed inputs are literal-free or synthetic | M3 | M3 exit 4 |
| R4 | nothing minted is committed or shared | M3 | M3 exit 5 |
| R5 | one command builds a current Twin | M3 | M3 exit 1 |
| R6 | tiered volumes; the branch tier exact | M3, M4 | M3 exit 3 (population); M4 exit 3 (blocks at every tier) |
| R7 | evidence refreshed by one click and one pull request; the canary scan | M2, M3 | M2 exit 4; M3 exit 4 |
| R8 | the seed policy, deterministic | M3 | M3 exit 2 |
| R9 | a fresh copy and a receipt for every proof | M4 | M4 exit 1 |
| R10 | one command, one validated JSON object | M4 | M4 exit 1 |
| R11 | a read-only prediction per readable environment | M2 | M2 exits 1, 2 |
| R12 | the baseline verified against Dev | M1 | M1 exit 1 |
| R13 | the engine pinned and stamped | M1, M4 | M1 exit 6; M4 exit 4 |
| R14 | read-only by construction | M1, M2 | M1 exits 1, 5; M2 exit 3 |
| R15 | only Octopus writes; Permissive only on a copy | M1, M4 | M1 exit 5; M4 exit 5 |
| R16 | no credentials; refusals explained | M1, M2 | M1 exit 7; M2 exit 2 |
| R17 | the gate reproduces from git alone | M5 | M5 exit 1 |
| R18 | the evidence table; placeholders refused | M5 | M5 exit 2 |
| R19 | windows are locks; compound deltas need a program | M5 | M5 exit 3 |
| R20 | deployed means converged | M6 (the verb from M1) | M6 exit 1 |
| R21 | the platform's view is checked | M6 | M6 exit 2 |
| R22 | consumers are listed | M6 | M6 exit 2 |
| R23 | one generated page | M6 | M6 exit 3 |
| R24 | bytes are declared | M0, M5 | WP 0.2; M5 exit 5 |
| R25 | disposable copies named, swept, never shared | M3 | M3 exit 5 |
| R26 | one click per station, no fetch | M7 | M7's exit |
| R27 | without the tool, stop and say so | M4, M7 | M4 exit 5; WP 7.2 |
| R28 | a compound request becomes ordered pull requests | M7 | WP 7.2 |

---

## Appendix B — The germ, mapped

| The germ's line | In v3 as | Lands in |
|---|---|---|
| Five inputs fix an outcome | `Receipt` carries the five fingerprints and names a missing one | M0 (the type); M1 to M4 (filled) |
| The transfer condition | `Branch`; `Transfers`; law T; a proof at each environment's branch | M2, M4 |
| The read-only closure | `Named` has no `Publish`; the executor's allowlist; the read-only principal | M1, M2 |
| The privacy closure | disclosure classes; vocabulary only from synthetic environments; the canary scan | M2, M3 |
| Receipts, then reproduction | the gate recomputes from the same inputs and compares receipts | M5 |
| One oracle for convergence | the empty plan under the pipeline's profile (§1 fact 4); `check drift` | M1, M6 |
| Two locks | `check inflight` for windows; `classify`'s rename finding and the refactorlog writer for renames | M2, M4, M5 |
| The human boundary | placeholders the gate refuses; `check outsystems` after the refresh | M5, M6 |
| Derivation, not maintenance | the Twin, the record, the page and the bundle as functions of fingerprinted inputs; law 1′ | M3, M5, M6, M7 |
| Prerequisites, in dependency order | §17 | — |
| Outside the set, by construction | the record's *Not checked*, never empty | M5 |

---

## Appendix C — The laws, mapped

| # | `V3_ARCHITECTURE.md` §13 | Its form in the lifecycle | Lands in | Its form in the wing |
|---|---|---|---|---|
| 1 | the same inputs emit the same bytes | **1′** every generated artifact (a mint, a record, the page, the bundle, `changelog.json`) is byte-identical from the same fingerprinted inputs | M3, M5, M6, M7 | `emit` byte-identical (oracle 1) |
| 2 | emit then read is the identity | **2′** a published copy converges: the plan against it is empty | M1 | as stated |
| 3 | emit is faithful to the repository | **3′** the read is complete: stable across builds, sensitive to every archetype edit, package and published copy agreeing | M1 | as stated |
| 4 | a vanilla policy changes nothing | none; the lifecycle has no policy | — | as stated |
| 5 | every decision names its evidence | **5′** every claim carries its receipt, with all five inputs or the one it lacks | M5 | as stated |
| 6 | a rename keeps its key | as stated: with the entry, the plan renames and the copy keeps its data; without it, `classify` refuses | M2, M4 | — |
| 7 | a delta's inverse undoes it | the plan from head back to base, published after the change, converges to base | M4 | — |
| 8 | an idempotent redeploy is silent | as stated: the second plan is empty; zero capture rows where CDC exists | M4 | — |
| 9 | the publish guard is data-blind | as stated, and as a property of `Outcome.Predict` | M2, M4 | — |
| 10 | a foreign key lands trusted | as stated, on the pinned engine | M4 | — |
| 11 | a mint has no orphans and repeats itself | as stated, with the byte oracle against v2's rows | M3 | — |
| 12 | the substrate carries no literal | as stated, by type and by the canary scan | M3 | — |
| P | — | on a copy, the prediction equals the proof, in outcome and counts | M4 | — |
| T | — | a proof is reported for an environment only at that environment's branch | M4 | — |

---

## Appendix D — The instruction architecture's tests, mapped

| # | Test (`V3_INSTRUCTION_ARCHITECTURE.md` §10) | Lands in |
|---|---|---|
| 1 | `Manifest` | M0 |
| 2 | `Budgets` | M0, with Appendix F's ceilings |
| 3 | `Register.Samples` | M7 |
| 4 | `Register.Refusals` | M1, when the first refusals exist |
| 5 | `Register.Prose` | M0, on `VALUES.md` first |
| 6 | `Vocabulary` | M7 |
| 7 | `NoRestatedCounts` | M0 |
| 8 | `Citations` | M0 for the engine's documents; M7 for `knowledge/` |
| 9 | `VerbsMatchArchitecture` | M8, when `ARCHITECTURE.md` exists; until then the verb reference is generated from `--help --json` and cannot drift |
| 10 | `LawsMatchArchitecture` | M8; `LAWS.md` is generated from M1 on |
| 11 | `PackagerCheck` | M7 |
| 12 | `NoSkips` | M0 |
| 13 | `PackagesAllowlist` | M0 |
| 14 | `Decisions` | M0 |
| 15 | `FindingsAppendOnly` | M7, when `findings.md` moves |
| 16 | `ValuesResolve` | M0, accepting `pending M<n>`; strict at M8 |
| 17 | `VendoredCitations` (the estate's pipeline) | M7 |
| 18 | `RecordInPullRequest` (the estate's pipeline, in the gate) | M5 |

---

## Appendix E — The spike, as the first tests

§1's facts, reduced to the lines that established them. WP 1.8 turns them into assertions.

```bash
# fact 1: the build, with the committed tool folder as the SSDT targets path
dotnet build Estate.sqlproj -c Release \
  -p:NetCoreBuild=true -p:NETCoreTargetsPath="$TOOL" -p:SQLDBExtensionsRefPath="$TOOL" \
  -p:TargetFrameworkRootPath="$TOOL/refasm"   # .NETFramework/v4.7.2/{mscorlib.dll, RedistList/FrameworkList.xml}
```

```csharp
// facts 2 and 4: plan as the read-only principal; a report with no operations is convergence
var plan = new DacServices(readOnlyServer).Script(package, database, new PublishOptions
{
    GenerateDeploymentScript = true,
    GenerateDeploymentReport = true,
    DeployOptions = DacProfile.Load(pipelineProfile).DeployOptions,
});
XNamespace report = "http://schemas.microsoft.com/sqlserver/dac/DeployReport/2012/02";
bool converged = !XDocument.Parse(plan.DeploymentReport).Descendants(report + "Operation").Any();

// fact 3: the guard, lifted from DacFx's own script and run verbatim
var body = string.Join('\n', plan.DatabaseScript.Split('\n').Where(l => !l.TrimStart().StartsWith(':')));
var script = new TSql160Parser(initialQuotedIdentifiers: true).Parse(new StringReader(body), out var errors);

sealed class Guards(List<QueryExpression> found) : TSqlFragmentVisitor
{
    public override void ExplicitVisit(IfStatement s)
    {
        if (s.Predicate is ExistsPredicate e
            && s.ThenStatement is RaiseErrorStatement { ThirdParameter: IntegerLiteral { Value: "127" } })
            found.Add(e.Subquery.QueryExpression);   // re-generated, then run as
        base.ExplicitVisit(s);                        // SELECT CASE WHEN EXISTS (<it>) THEN 1 ELSE 0 END
    }
}

// fact 6: one walk, no code per property, for every type DacFx knows
// (the walk in io/Ssdt also descends composing relationships and skips a property DacFx cannot read on an object)
static IEnumerable<(string Property, object? Before, object? After)> Changes(TSqlObject before, TSqlObject after) =>
    before.ObjectType.Properties
          .Select(p => (p.Name, Before: before.GetProperty(p), After: after.GetProperty(p)))
          .Where(c => !Equals(c.Before, c.After));
```

---

## Appendix F — The budget, by file

Code only; tests are budgeted separately below. `ci/budgets.json` carries these ceilings from M0.

**`kernel/` (ceiling 6,000; planned 5,800)**

| File | Lines | M |
|---|---:|---|
| `Seq.cs`, `Result.cs`, `Refusal.cs`, `Name.cs`, `Fingerprint.cs` | 410 | M0 |
| `Receipt.cs` (the five inputs, `Engine`) | 180 | M0 |
| `Element.cs` | 200 | M1 |
| `Change.cs` | 260 | M1 |
| `Environment.cs` | 140 | M1 |
| `Claims.cs` (claims, `Branch`) | 300 | M2 |
| `Outcome.cs` (predict, prove, transfer, converged) | 280 | M2, M4 |
| `Classify.cs` | 380 | M2 |
| `Schema.cs` | 520 | M3 |
| `Order.cs` | 180 | M3 |
| `Evidence.cs` | 700 | M3 |
| `Synth.cs` | 1,450 | M3 |
| `Record.cs`, `Locks.cs` | 520 | M5 |
| `Platform.cs`, `Page.cs` | 280 | M6 |

**`io/` (ceiling 6,000; planned 5,410)**

| File | Lines | M |
|---|---:|---|
| `Write.cs`, `Json.cs` | 300 | M0, M5 |
| `Ssdt.cs` (build, load, walk, refactorlog read) | 360 | M1 |
| `SqlServer.cs` (targets, model, plan, executor, allowlist; the guard lens at M2) | 680 | M1, M2 |
| `Profiles.cs`, `Git.cs`, `Doctor.cs` | 400 | M1 |
| `Probes.cs`, `Predict.cs` | 500 | M2 |
| `Substrate.cs`, `Twin.cs`, `Realize.cs`, `Profile.cs` | 1,390 | M3 |
| `Prove.cs`, `RefactorLog.cs` | 520 | M4 |
| `Gate.cs`, `Vendor.cs` | 530 | M5 |
| `Ossys.cs` and the page's commit | 310 | M6 |
| `Knowledge.cs` | 420 | M7 |

**`cli/` (ceiling 1,500; planned 1,490):** the dispatcher, the verb table and the renderers (280,
M0), then one file per verb at about a hundred lines (1,210, M1 to M7).

**Per milestone (code):** M0 1,100 · M1 2,260 · M2 1,660 · M3 4,400 · M4 720 · M5 1,320 · M6 740 ·
M7 500; about 12,700 in all, against a ceiling of 13,500.

**Tests (ceiling 10,000; planned 9,300):** M0 700 · M1 1,300 · M2 1,200 · M3 2,200 · M4 1,500 ·
M5 1,000 · M6 500 · M7 900. The corpus under `tests/Golden/` is data, not code, and is held to
≤ 3,000 lines as `V3_ARCHITECTURE.md` §6.5 sets.
