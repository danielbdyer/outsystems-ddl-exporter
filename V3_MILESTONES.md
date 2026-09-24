# V3 — The Milestones

*A build plan for `V3_ARCHITECTURE.md`, `V3_INSTRUCTION_ARCHITECTURE.md`, and the change-validation
lifecycle as `LIFECYCLE_BACKPORT_PROMPT.md` states it for git as the only store. It is one step more
specific than any of them. It proposes; the operator decides. It is the last design document: from
M0 on, `NEXT.md` carries the live state, and this file is archived with the other three at M8.*

**Contents.** §0 the answer · §1 what was measured before writing · §2 the principle · §3 the five
inputs, the two instruments, the branch · §4 what this changes in the architecture, and what it
keeps · §5 the milestones at a glance · §6–§14 M0 to M8 and the wing · §15 the estate side ·
§16 how agents build it · §17 decisions and prerequisites · §18 risks · §19 the first hour ·
A requirements map · B the germ, mapped · C the laws, mapped · D the instruction tests, mapped ·
E the spike code · F the budget by file.

---

## 0. The answer in one breath

**Build the lifecycle first, on .NET 10, in eight milestones and one optional wing.** The critical
path is about thirteen working days with four agent lanes running at once, and about twenty where a
workflow runs two agents at a time, as on a 4-CPU container. The first useful thing lands on day
three: one command, read-only, says whether Dev matches the repository at a tag. Prediction lands
on day five, the Twin on day six (at branch volume; shape volume on day eight), proof on day nine,
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
engine is budgeted at **≤ 14,900 lines of C#** (kernel ≤ 6,800, io ≤ 6,400, cli ≤ 1,700; about
14,000 planned), against `V3_ARCHITECTURE.md` §6.6's ≤ 31,000 without the OSSYS reader (≈ 34,000
with it). The wing, if ported, adds its own §6.6 figures on top.

**Fidelity, by one refusal: never re-derive the engine.** Whether a change blocks is the deploy
script's own guard, run read-only. Whether a value survives a retype is the engine's own
conversion. Whether an environment matches a tag is an empty deploy plan under the pipeline's own
publish profile. What a change does is DacFx publishing it to a copy, from a package built with
the pipeline's own build semantics. v3 re-implements none of these, so its answers cannot drift
from the engine's. Where v3 must write a query itself (a violation count), a law holds the query
to the engine on every archetype: on a copy, the prediction must equal the proof.

**The germ, as types.** An outcome is a function of five inputs: the delta, the target's schema,
the target's data facts, the engine, the profile. The chain has two instruments: prediction reads
*which branch* an environment is on; proof shows *what the engine does* on a branch. So v3 makes
the **branch** a first-class value that travels with the change. `estate predict` reads it,
read-only, from each environment the author or the lead can reach; the pull request carries it;
the gate mints a disposable copy at exactly that branch and proves there, on the same engine
under the same profile. A proof transfers by construction, and a lead proves "as UAT" without a
row of UAT leaving UAT. The committed evidence carries realism and the live branch carries
correctness, so the Twin asks for a refresh only when a live branch disagrees with it.

---

## 1. What was measured before writing this (2026-09-23)

The plan rests on twelve facts. Ten were measured today in this repository's session container
against SQL Server 2022 in Docker: facts 1 to 7 and 11 on DacFx 162.5.57 and again on 170.5.96 with
the same results, facts 1 to 7 once more on the .NET 10 SDK (10.0.401) with DacFx 170.5.96's
`net10.0` build, fact 10 on 162.5.57, and fact 12 on the container itself. Two were read from v2's
source and from the package's documentation. The spike code is Appendix E, and it becomes M1's
first tests.

| # | The claim | How it was measured | Result |
|---|---|---|---|
| 1 | A classic (Visual Studio format) `.sqlproj` builds with no Visual Studio and no download | a minimal classic project; `dotnet build -p:NetCoreBuild=true -p:NETCoreTargetsPath=<tool> -p:SQLDBExtensionsRefPath=<tool> -p:TargetFrameworkRootPath=<stub>`, where `<tool>` is a published console app referencing DacFx plus the package's `Microsoft.Data.Tools.Schema.SqlTasks.targets`, and `<stub>` holds one reference assembly (`mscorlib.dll`, 2.7 MB) with its `FrameworkList.xml` | builds on Linux with either engine's targets; the dacpac carries `refactor.xml` and `postdeploy.sql`. The package's `lib` folder alone fails (`SqlBuildTask` needs DacFx's dependencies beside it); a published tool folder has them (framework-dependent: 60 files and 91 MB on 162.5.57; 65 files and 107 MB on 170.5.96; 67 files and 107 MB for `net10.0` on 170.5.96, built with the .NET 10 SDK) |
| 2 | `Script` and `DeployReport` need nothing beyond read | a login holding only `VIEW DEFINITION` and `db_datareader` on one database; `DacServices.Script(package, db, PublishOptions { GenerateDeploymentScript, GenerateDeploymentReport })` | succeeds |
| 3 | The data-loss guard can be lifted from the script and run read-only | ScriptDom `TSql160Parser` over the script with SQLCMD lines removed (0 parse errors); the guard is `IF EXISTS (SELECT TOP 1 1 FROM [dbo].[Customer]) RAISERROR (…, 16, 127)`; its predicate run verbatim as the read-only login | returns 1 on a populated table. The script holds other `IF EXISTS` blocks (database-option checks against `master.dbo.sysdatabases`), so the lens matches the `RAISERROR` at state 127, not the `IF` alone |
| 4 | The convergence oracle is an empty plan, not a default schema comparison | the base package against the database it was just published to, as the read-only login | the deploy report has no `<Operations>` element. `SchemaComparison` with default options reports two differences: the reader's own user and the role membership granted to it. The oracle is the empty plan under the pipeline's profile; a comparison is used only to name objects, and only with the profile's options |
| 5 | A live database reads into the same model a build produces | `TSqlModel.LoadFromDatabase` as the read-only login | succeeds |
| 6 | Property changes are found generically | a walk over `ModelTypeClass.Properties` on the base and head `Column` objects (reached through `Table.Columns`; a column is not a top-level type) | one change, `Column.Nullable: True → False`. DacFx knows 29 properties and 4 relationships on a column; one walk covers them all, and every other type the same way |
| 7 | The deploy report is too coarse to classify from | the same head package | one operation, `Alter [dbo].[Customer] (SqlTable)`, and no alert. The claim comes from the property walk; the guard comes from the script |
| 8 | v2's Twin cannot see a rename carried by the refactorlog, or pre- and post-deploy scripts | `Twin.Runtime/EstateModel.fs` builds with `TSqlModel.AddObjects` and the three-argument `BuildPackage` (no `PackageOptions.RefactorLogPath`); `DacpacEmitter.fs` records that a model-built dacpac is schema-only; both rename facts in `SamplePrRenameTests.fs` are the no-entry kind, with the entry's effect imitated by a hand-run `sp_rename` | confirmed. `prove.mjs` builds the real project and does see them. v3 has one build, the real one (M1) |
| 9 | Every DacFx call the plan uses exists on the pinned engine | the XML documentation shipped in the 162.5.57 package; the spike compiled and ran against 170.5.96 | present: `TSqlModel.LoadFromDatabase`; `DacServices.Script` and `Publish` with `PublishOptions`, returning `DatabaseScript` and `DeploymentReport`; `DacProfile.Load(…).DeployOptions`; `PackageOptions.RefactorLogPath`; `SchemaComparison` with dacpac and database endpoints (a project endpoint type exists, but the package's own message `SchemaComparisonOnlySupportsDacpacAndDatabase` limits comparison to those two); `ModelTypeClass.Properties` and `.Relationships`; the typed metadata classes (`Column.Nullable`, `Column.Length`, `CheckConstraint.Expression`, `ForeignKeyConstraint.ForeignTable`) |
| 10 | The pipeline's publish profile is the options, and a hand-kept copy drifts | `DacProfile.Load(path).DeployOptions` over the proving ground's three profiles | all three load. `ProvingGround.Strict.publish.xml` sets `DropObjectsNotInSource=True` where `ProvingGround.Pipeline.publish.xml` sets `False` (the guard is on in both, off in Permissive). All three also carry a `TargetConnectionString` with a literal `sa` password. So v3 keeps no Strict file, and reads nothing from a profile but its deploy options and SQLCMD values: Strict is the pipeline's profile as loaded, and Permissive is the same with only the guard flipped |
| 11 | Whether a new foreign key lands trusted is decided by the profile, on either engine | a clean foreign key declared inline on a populated child table, published with default options, then with `ScriptNewConstraintValidation=false` | `is_not_trusted = 0` with the defaults, on 162.5.57 and on 170.5.96; `1` with validation off, on both. v2's finding that a proof on 162.5.57 left a key untrusted does not reproduce with the defaults, so trust depends on how the key is published, not on the engine family. Law 10 is a property of the pipeline's profile, and that profile is a prerequisite (§17 item 15) |
| 12 | SQL Server comes from Docker, change data capture included | `docker pull mcr.microsoft.com/mssql/server:2022-latest` (digest `sha256:4402d880…`); a container started with `MSSQL_AGENT_ENABLED=true`; a table enabled for CDC and two rows inserted | the pull reaches the registry; the container answers within seconds as SQL Server 2022 Developer (16.0.4295.3); the capture job records both rows within two seconds. Docker is therefore the substrate wherever it is installed, and the only one on which CDC can be proven; LocalDB is the fallback |

What was *not* measured, and is therefore M1's first work: the same build on `windows-latest` and
on a team laptop; a project that references `master.dacpac` (the tool folder must then carry the
system dacpacs); the estate's 300-table project for time; the same read-only calls against Dev
under the developers' Active Directory group; the profile the Octopus step actually applies; and
the SQL Server version the environments run, which the pinned image must match.

---

## 2. The principle: read whole, bind late

### 2.1 Three rules

1. **One adapter per substrate, and it reads the substrate whole.** An adapter is written against
   the substrate's own self-description, never against a verb's need. The model adapter walks every
   type DacFx knows, every property and every relationship, through `ModelTypeClass.Properties` and
   `.Relationships`, plus the package's pre- and post-deploy scripts and its refactorlog; it has no
   list of "the columns we care about". The probe executor runs any probe whose shape the closed
   allowlist admits (WP 1.4); it has no list of "the checks we do". So fingerprints, drift, the diff
   and the convergence oracle see everything the engine sees, and a change nobody wrote a lens for
   surfaces as an *unclassified change* that routes to proof, instead of vanishing. **One
   exception, by design:** the OutSystems metamodel's database also holds user records and site
   properties, so its adapter reads a committed allowlist of tables and columns and nothing else.
   The privacy closure outranks "whole".
2. **A directive is a lens, and a lens is data wherever it can be.** The operation catalog's
   recognition patterns live in the operation files. The profile is the pipeline's own
   `.publish.xml`. The environments, their classification, their cohorts and their references are
   one committed file, `estate/posture.json`. The agent surfaces are rows in a targets file. The
   record's sections and banned words are `knowledge/record.md`. Code interprets these; adding an
   operation, an environment or an agent surface is a file, not a function.
3. **Capabilities are types.** A named environment and a disposable copy are different C# types.
   A `Copy` has no public constructor: only `io/Substrate` makes one, for a database it created
   locally and recorded in `.estate/copies.json`, and `copy:<name>` resolves only against that
   registry. Only a `Copy` has a publish method; the Permissive profile can be constructed only for
   a `Copy`; the probe executor for a named environment accepts only the allowlist. "Only Octopus
   writes" and "Permissive only on a copy" are compile-time facts with one test each, not rules an
   agent must remember.

### 2.2 The surface register

| Surface | Its own self-description | The one adapter | Access | Replaces | Lines |
|---|---|---|---|---|---:|
| The SSDT project (`.sqlproj`, `.sql`, refactorlog, pre- and post-deploy, SQLCMD variables) | MSBuild and DacFx's build targets | `io/Ssdt.Build`: `dotnet build` with the committed targets (§1 fact 1), telemetry off; Visual Studio's MSBuild with Visual Studio's own SSDT targets as the fallback, stamped as that engine | read | the Twin's `AddObjects` build; the build fork in `prove.mjs` | ~160 |
| A package (`.dacpac`) | the DacFx model: types with properties and relationships; the package's deploy scripts and refactorlog | `io/Ssdt.Walk`: every object, property and relationship into kernel `Element`s, plus one element each for the pre- and post-deploy scripts and one per refactorlog entry | read | the lifts in `ReadSide`, `EstateModel` and `CatalogDiff`'s inputs | ~180 |
| A live database's schema (Dev, QA, UAT, a copy) | the same model, loaded from the database | `io/SqlServer.Model`: `TSqlModel.LoadFromDatabase`, then the same walk | read (`VIEW DEFINITION`) | `ReadSide.fs` and the read side's hand-rolled drain loops | ~60 |
| A live database's plan | DacFx's own deploy script and report | `io/SqlServer.Plan`: `DacServices.Script` under the pipeline's profile; the script's AST; the guard sites | read | `sqlpackage /Action:Script` in `prove.mjs` | ~250 |
| A live database's data | SQL Server's own aggregates | `io/SqlServer.Probe`: one executor; a closed allowlist checked on the ScriptDom AST before anything runs; a failed probe against a real environment reports its error number only; a query log | read (integer aggregates only) | `LiveProfiler.fs`, `EvidenceImport.fs`, `DataIntegrityChecker.fs`, probe SQL in the skills | ~380 |
| A disposable copy | a database the tool created locally and will drop | `io/Substrate` (Docker with the pinned SQL Server image, pulled when absent, wherever Docker is installed; LocalDB where it is not; a local developer-edition instance) with its registry, and `Publish`, which exists only on the `Copy` type | write, copy only | `TwinContainer.fs`, `TwinDatabase.fs`, `bake.mjs`, `DockerImageEmitter.fs` | ~460 |
| The OutSystems metamodel | its `ossys_*` tables | `io/Ossys`: single `SELECT`s over a committed table-and-column allowlist | read | nothing (the refresh and consumer checks are new); v1's rowset SQL is the donor, rewritten | ~280 |
| Git (the estate, the wiki) | refs, trees, commits | `io/Git`: a worktree at a ref; changed paths; a commit on a named branch and its push | read; commit and push a named branch | the base checkout in the gate and in `inflight-check.mjs` | ~180 |
| Azure DevOps (policy, pull request) | a branch policy and a REST API | none in the tool. The pipeline template's host step (≤ 25 lines of PowerShell) writes the PR body to a file and posts the comment | host step | the placeholders in `ssdt-agent-pr-validation.yml` | 0 |
| The agents' surfaces (Copilot in Visual Studio, Claude Code) | files at known paths | `io/Knowledge`: one generator; the targets as data | write, generated only | `ssdt-agent-package.mjs`, `ssdt-agent-gates.mjs` | ~400 |
| The machine | the SDK, LocalDB, Docker, the committed tool | `io/Doctor` | read | four hooks, 814 lines | ~140 |
| The engine | the committed DacFx (170.5.96 until the pin lands) and the SQL Server image's digest | a stamp in every receipt | read | two engines on two paths (in-process 162, external `sqlpackage` 170) | ~30 |

### 2.3 The lens register

| The directive | Verb | Reads | Parameterized by | Kernel function | Output |
|---|---|---|---|---|---|
| Can this machine do the work? | `doctor` | the machine, the engine | `estate/ledgers/toolchain.md` | — | `estate.doctor/1` |
| What is this schema? | `read` | a ref, a package or a database | — | `Element` graph and its fingerprint | `estate.read/1` |
| What changes? | `diff` | any two of those | the refactorlog | `Change.Between` | `estate.diff/1` |
| What is it, provisionally? | `classify` | the diff and the committed evidence | the `recognize:` block in each `knowledge/ops/*.md`; `estate/ledgers/cdc-tracked.md` | `Classify.Of` | `estate.classify/1` |
| Which branch is each environment on? | `predict` | the head package; each reachable environment's plan and data | `estate/posture.json` | `Claims.Of`, `Branch.Of`, `Outcome.Predict` | `estate.predict/1`, and a Markdown block carrying a machine block |
| What does the data look like? | `profile` | an environment's data | its classification (what may be read) | `Evidence.Of` | `estate/evidence.shape.json`; row tiers for real environments |
| A current substrate | `twin` | a ref, the evidence, the seed, optionally a branch | the tier | `Synth.Mint` | `estate.twin/1` |
| What does the engine do? | `prove` | a fresh copy per branch, restored from the Twin | the pipeline's profile (Strict and Permissive differ in the guard alone) | `Outcome.Prove`, `Transfers` | `estate.prove/1` |
| The pull request body | `record` | the receipts | `knowledge/record.md` | `Record.Of` | the body |
| Reproduce it | `gate` | git, the PR body, the ledgers | `estate/ledgers/in-flight.md` | the functions above | `estate.gate/1`, `changelog.json` |
| Deployed means converged | `check drift` | the package at a tag; the environment's plan | the pipeline's profile | `Outcome.Converged` | `estate.check/1` |
| Did the platform follow? | `check outsystems` | the metamodel; the package at a tag | the metamodel allowlist | `Platform.Compare` | `estate.check/1` |
| One page | `check environments --page` | the checks above; `deployments.md` beside the page | `estate/posture.json` | `Page.Of` | a wiki page |
| Is the evidence still true? | `check evidence` | the committed evidence; a live branch over its standing sites | — | `Branch` equality | `estate.check/1` |
| Is a table locked? | `check inflight` | the diff; the ledger | `estate/ledgers/in-flight.md` | `Locks.Collide` | `estate.check/1` |
| Which tables does CDC capture? | `check cdc` | an environment's catalog | — | — | rows proposed for `estate/ledgers/cdc-tracked.md` |
| The front door | `knowledge package --check`, `knowledge vendor` | `knowledge/` | `knowledge/targets.json` | — | generated files; a vendoring pull request |

### 2.4 Where the lines go missing

v2 read a schema four ways through three lifts (`V3_ARCHITECTURE.md` §8.1), drained rows in fifteen
hand-rolled loops, proved in two languages on two engines, and stood a substrate up three ways: the
cost was readers times consumers. Here the product is gone. One walk reads every type for every
consumer; one executor runs every probe for every consumer; one build serves the Twin and every
proof. An object type DacFx already knows costs zero lines, because it is already in the walk, the
fingerprint, the diff and the drift check. An operation costs one knowledge file with a
`recognize:` block. An environment costs one entry in `estate/posture.json`. An agent surface
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

// A proof speaks for an environment only at that environment's branch, on the same engine, under the same profile.
public static bool Transfers(Proof proof, Prediction prediction) =>
    proof.Branch == prediction.Branch
    && proof.Receipt.Engine == prediction.Receipt.Engine
    && proof.Receipt.Profile == prediction.Receipt.Profile;
```

A `Branch` holds every site in that environment's own plan. So baseline drift (a plan with
operations the change does not make), a different engine and a different profile each break the
transfer, and the record names the input that differs.

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
   branch, engine or profile other than the one read for that environment (law T, M4).
2. **The Twin rarely needs a person.** A schema change is absorbed because the Twin is rebuilt from
   the repository at the ref. Data drift matters only when it changes a branch, and every
   prediction re-reads the live branch for exactly the tables the change touches.
   `check evidence` compares the committed evidence's branch with a live one and asks for
   `estate profile` only when they differ. The seed is the ISO week of the head commit, so the
   population's identity renews weekly with nobody touching anything, and the gate reproduces the
   author's rows exactly. This is the plan's answer to "a Twin that almost never needs
   maintaining".
3. **A lead proves as UAT without UAT's data.** UAT's branch is a list of booleans derived from
   counts, which the privacy closure admits; the copy minted at it holds only synthetic rows.
4. **A retype's branch comes only from a prediction.** Whether any value fails to convert is known
   only from the environment, counted read-only with the `TRY_` form of the engine's own
   conversion, so no error message (which would carry the value) is ever raised there. The mint
   then realizes one failing value, so the proof shows what the engine does with it. No committed
   file claims to know which values convert.
5. **The gap between the read and the deploy is not covered by either instrument.** The record
   says so in "Not checked", and the convergence oracle before and after the deploy bounds and
   closes it (M6).

---

## 4. What this changes in the architecture, and what it keeps

| # | Topic | The design documents say | This plan | Why | Reverses if |
|---|---|---|---|---|---|
| 1 | Order | kernel and `emit` first (§14.2 steps 1–2); byte parity with v2's emitter before any lifecycle verb | the lifecycle first (M1–M6); `emit`, `decide`, `move` and the full OSSYS reader form the wing (W) | the lifecycle has consumers every day; the wing's only consumer is Prod's cutover, which v2 serves, frozen | a second estate, or a re-emission from OSSYS, needs the wing before Prod is done |
| 2 | Reading a live database | port v2's read side and profiler into `io/SqlServer` (~2,300 F# lines) | `TSqlModel.LoadFromDatabase` and the one walk for the schema (~240 lines); one probe executor for the data (~380) | the reader DacFx itself uses when it plans the pipeline's deploy; verified read-only (§1 facts 2, 5) | a needed catalog fact is one DacFx does not model. Two are known (constraint trust, CDC tracking), and the executor reads both from the catalog |
| 3 | The delta | a typed nine-facet `Delta.between` in the kernel (~700) | `Change.Between` over `Element`s, covering every property DacFx knows and the package's scripts and refactorlog (~280), with DacFx's own plan as the statement of what ships | complete by construction; an unmodelled change surfaces instead of vanishing | — |
| 4 | Building a package | `dotnet build` or MSBuild for proofs; the Twin's own `AddObjects` build | one build for everything: `dotnet build` with the committed DacFx targets | it carries the refactorlog and the pre- and post-deploy scripts (§1 facts 1, 8); the build engine is the committed engine; no Visual Studio and no download | S1 finds the estate's project cannot build this way; then, and only then, Visual Studio's own MSBuild and SSDT targets, stamped as that engine |
| 5 | The substrate | bake a `.bacpac` and an image, publish them as a pipeline artifact, restore | derived locally from committed inputs and cached by fingerprint; `twin bake`, `twin restore` and `twin gc` do not exist, and the bake lane retires | git is the only store; determinism makes any artifact a cache | an artifact store arrives: a restore path is added and nothing else changes (lifecycle prompt §9) |
| 6 | Prediction | `classify`, pure, from committed evidence | `classify` stays provisional; a new verb, `predict`, reads each environment's branch read-only from DacFx's own guards; proofs run at each environment's branch | the germ's two instruments and its transfer condition | — |
| 7 | Laws | twelve | fourteen: adds **P** (on a copy, prediction equals proof) and **T** (a proof is reported for an environment only at that environment's branch, engine and profile). Laws 1–5 take lifecycle forms here and keep their emit forms for the wing; 11 and 12 are restated to match the branch tier (Appendix C) | P holds every query v3 writes to the engine's behaviour; T is the transfer condition | — |
| 8 | Verbs | thirteen, with `knowledge package` used but not counted | twelve in the lifecycle (adds `predict`; counts `knowledge`) and three in the wing (`emit`, `decide`, `move`); `read --from ossys` moves to the wing | test 9 checks the verb list; the list should be true | — |
| 9 | Budget | ≤ 31,000 lines of C# without the OSSYS reader (≈ 34,000 with it) | ≤ 14,900 for the lifecycle; the wing at §6.6's own figures, if ported | §2.4 | a milestone's exit cannot be met under its ceiling: raise it with one decision line, never silently |
| 10 | Distribution | a pinned .NET tool from a feed (instruction architecture §9.6 (e)) | the published tool committed in Git LFS, run by `estate.cmd` and `estate.ps1` at the estate's root; `net10.0` throughout, published framework-dependent, with DacFx's `net10.0` build assemblies as the build route's targets; the .NET 10 SDK is a prerequisite on every machine that builds | the lifecycle prompt's R1 and R2; MSBuild loads the build task into its own runtime, so the targets and the SDK must agree (§1 fact 1, re-measured on SDK 10.0.401) | a feed arrives: the folder becomes a tool manifest |
| 11 | Refreshing evidence | the bake lane, on a schema change and weekly | when a live branch disagrees with the committed one (`check evidence`, and every prediction) | §3 consequence 2 | — |
| 12 | The OSSYS reader | an optional package (~3,600) serving `read --from ossys` and `check outsystems` | a post-cutover subset (~280) over a table-and-column allowlist serves `check outsystems`; the full reader is in the wing | the lifecycle asks the metamodel three questions, not for the whole model, and the platform database holds user records | — |
| 13 | Where the Twin's fingerprints live | R5: written into the database; values O6: a lock on `[twin].[__state]` | in the Twin database's name (`estate_twin_<schema>_<evidence>_<seed>_<tier>[_<branch>]`), with a lock file under `.estate/` | a state table would make the Twin's schema differ from the repository's, and the drift oracle could no longer check the Twin exactly; the name carries the same fingerprints and `twin status` verifies them | — |
| 14 | The estate's configuration | `estate/posture.json` (the publish posture, the writable targets, the substrate) and two mirrored profiles, `estate/profiles/{strict,permissive}.publish.xml` | `estate/posture.json` keeps its name and holds each environment (classification, cohorts, references, the profile's path, SQLCMD values, the metamodel's reference) and the substrate preference; `estate/profiles/` holds the pipeline's one profile | §1 fact 10: a hand-kept Strict copy had already drifted from the pipeline's | — |
| 15 | What exit 3 means | blocked: the publish guard refused | blocked by the data: `kind: guard` (the row-presence guard) or `kind: violation` (the engine refused the change on existing rows, such as Msg 547 or Msg 2628); anything else is a tooling failure | the operation catalog already says a foreign key over orphans "blocks"; one code with a kind keeps both honest | — |
| 16 | The substrate's engine | LocalDB on every machine; Docker optional and never pulled (the lifecycle prompt) | Docker with the SQL Server image pinned by tag and digest (pulled when absent) wherever Docker is installed, including cloud sessions and the gate; LocalDB where it is not | §1 fact 12: the pull works and the container runs CDC, which LocalDB cannot; one engine for laptops, cloud sessions and CI | a machine has no Docker: LocalDB, with CDC reported *not provable here* |

Everything else in the design documents stands: the thesis (§5), the C# encoding and its toolchain
(§6.6), the exit codes and the versioned JSON contract, the kernel's purity and the dependency laws,
the knowledge tree's shape (§9), the documents and their manifest, the session protocols, the
archive, and the write budget. WP 0.5 makes the instruction architecture's drafts match this plan
where they assumed a feed, a store or the wing:
- the SessionStart hook installs the .NET 10 SDK when it is absent and builds and runs the local
  tool instead of `dotnet tool restore`; in remote sessions only, it also starts Docker and the
  SQL Server container, an exception to "no hook starts a daemon" recorded as a decision line;
- the permissions ask before any verb that pushes (`profile --commit`, `check cdc --commit`,
  `check environments --page --commit`, `knowledge vendor`), and name no `decide` and no `sql:`
  target;
- `cli/CONFIG.md` is generated from `estate/posture.json`'s schema;
- `README.md` and the doctor line speak of `twin up` and the local cache, not `twin restore`;
- in `VALUES.md`, D1, D7 and E1 to E9 read `pending W`; D5 lands in M4 (a second publish changes
  no row); S5 lands in M4 and M5; S7 lands in M1 (its `move` clause reads `pending W`); G4 and G5
  land in M5; G9 lands in M7; O5 and O6 name the copy registry and the Twin's fingerprinted name
  (M3); X5 and O10 drop `twin restore`, name Docker's pull of the pinned SQL Server image as the one
  fetch, and name the outbound-deny lane (M0).

---

## 5. The milestones at a glance

| M | Name | Afterwards, a person can… | Requirements (lifecycle prompt) | Laws green | Retires (to the archive) | C# added | When | Needs |
|---|---|---|---|---|---|---:|---|---|
| M0 | Ground | build and test an empty engine under its laws and budgets; read `AGENTS.md` | R24 (engine side) | the two dependency laws | four hooks (814 lines); root agent files | ~1,120 | day 1 | — |
| M1 | Read | ask, read-only, whether Dev matches the repository at a tag; see what a branch changes, property by property | R12 (Dev half), R13 (stamp and window), R14 (principal, extended events), R15 (types), R16 | 2′, 3′, and "a named environment cannot be written" | the read side as a read path | ~2,560 | days 2–3 | M0 |
| M2 | Predict | see, read-only, whether a change blocks or applies on each environment they can read, and why; a lead pastes QA and UAT | R7 (scan, first half), R11, R12 (drift line), R14 (allowlist), R16 | 9 (as a property) | probe SQL in the skills | ~1,880 | days 4–5 | M1 |
| M3 | Twin | stand up a current Twin from the clone in one command, at branch, shape or scale; refresh evidence in one command and one pull request | R3, R4, R5, R6 (population), R7, R8, R12 (Twin half) | 11′, 12′ | `bake.mjs`, the Twin CLI, `twin.json`, scenarios | ~4,840 | days 2–8, lane T; `twin up` at branch volume by day 6 | M0 (σ); M1 (`twin up`) |
| M4 | Prove | prove a change on a fresh copy at each environment's branch, with a receipt, and see whether it transfers | R6 (blocks at every tier), R9, R10, R13 (trust), R15, R25, R27 (tool side) | 6, 7, 8, 9, 10, P, T | `prove.mjs`; the `sqlpackage` install step | ~790 | days 7–9 | M2; WPs 3.1–3.5 |
| M5 | Record and gate | open a pull request whose body is generated and whose gate reproduces it from the clone in under ten minutes | R1, R2, R16 (whole chain), R17, R18, R19, R23 (the gate's cells), R24 (estate side), R26 (in part) | 1′ (record), 5′ | `inflight-check.mjs`; the placeholder pipeline; the ledger overwrite | ~1,440 | days 10–11; the record from day 6 | M4; WP 7.2's `record.md` |
| M6 | After deploy | see per environment whether it is deployed, converged, refreshed, republished and in sync, on one generated page | R7 (scan, whole chain), R20, R21, R22, R23 | 1′ (page) | — | ~880 | days 12–13 | M5 |
| M7 | Front door | work through Copilot or Claude Code along one authoring path and one reviewing path, generated for each agent from one tree | R26, R27, R28 | the instruction tests (Appendix D) | the gates and packager scripts; the three-agent hand-off; four surfaces per operation | ~460 | days 2–13, lane C | M0; M2's patterns; M5's vendoring |
| M8 | One tool | use one tool and one set of documents; the archive leaves `main` | — | all of them, generated into `LAWS.md` | the archive's CI | ~0 | Prod's cutover + 30 days | M7 |
| W | The wing | only if a consumer exists: re-emit from OSSYS; decide; move data | — | 1–5 in their emit forms; oracles 1 and 4 | v2 | §6.6 | on a decision | §17, item 9 |

The C# column sums to about 13,970 against the 14,900 ceiling; tests are budgeted at ≤ 10,500
(Appendix F). The days assume agents writing against executable exit checks, one independent
review per pull request, the operator reviewing the kernel and contract pull requests, and four
lanes running at once. A workflow on a 4-CPU container runs two agents at a time, which stretches
the calendar to about twenty working days.

**The lanes, by day.** Numbers are work packages.

| Lane | Day 1 | Days 2–3 | Days 4–5 | Day 6 | Days 7–9 | Days 10–11 | Days 12–13 |
|---|---|---|---|---|---|---|---|
| A · kernel | 0.2, 0.3 | 1.3, 1.5 | 2.1, 2.4 | 5.1 (the record, early) | 4.2 | 5.1 (locks, lint) | 6.2 |
| B · io and SQL | 0.4, 0.7 | 1.1, 1.2, 1.4, 1.6, 1.7 | 2.2, 2.3, 2.5 | 3.4 | 4.1, 4.3, 4.5 | 5.2, 5.4 | 6.1, 6.3 |
| T · σ and the Twin | 0.3 (with A) | 3.1, 3.2, 3.3 | 3.3 | 3.5 | 3.6, 3.7 | — | — |
| C · words, corpus, CI | 0.1, 0.5, 0.6 | 1.8, 7.1 | 2.6, 7.2 (`record.md` first) | 7.2 | 4.4 | 5.3, 7.3 | 6.4, 7.4 |
| Lands | — | drift (day 3) | predict (day 5) | the Twin at branch volume | shape volume (day 8), proof (day 9) | the gate (day 11) | the page, the pilot (day 13) |

**What lights up when.**

| Station | The one command | Lit at |
|---|---|---|
| Can this machine do the work? | `estate doctor` | M1 (a stub at M0) |
| Is the baseline true? | `estate check drift --target env:dev --at <Dev's deployed tag>` | M1 |
| Intent to operation | the agent, `knowledge/authoring.md` S0, with `estate classify` naming the operation | M7 (the verb at M2) |
| Edit the CREATE | the agent | M7 |
| Predict | `estate predict` (a lead adds `--env qa,uat`) | M2 |
| The substrate | `estate twin up` | M3 |
| Prove | `estate prove` | M4 |
| The record | `estate record` | M5 |
| Pull request and gate | the branch policy runs `estate gate`; a developer can run it first | M5 |
| Review | read the evidence table; `estate gate` to reproduce | M5 |
| Before the deploy | `estate check drift --at <the tag deployed now>` stops a release into a drifted environment | M6 |
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
| 0.1 | C | **Freeze and archive.** Tag `v2-final` at the commit v3 starts from. `git mv` the v1 trunk (`src/`, `tests/`, `config/`, `docs/`, `notes/`, `handbook/`, `ssdt-playbook/`, `schema/`, `tools/`, the solution, v1's root files and its `global.json`) into `archive/v1/`, and `sidecar/projection/` into `archive/v2/`. Re-path the six workflows so v2's lanes keep running from the archive until M8. Remove the `.claude/agents` and `.claude/skills` pointers, which point into v2's tree, with a `NEXT.md` line: M7 regenerates them from `knowledge/`. The same push carries WP 0.5's `.claude/settings.json` and hooks, because today's hooks read `sidecar/projection/global.json` and start SQL Server through `sidecar/projection/scripts/warm-sql.sh`, both of which this move relocates. Generate `archive/INDEX.md` (one line per document: path, date, status). The five root documents (`V3_ARCHITECTURE.md`, `V3_INSTRUCTION_ARCHITECTURE.md`, `LIFECYCLE_BACKPORT_PROMPT.md`, this file and `V3_BUILD_PROMPT.md`) stay at the root, each a manifest row (reader: the operator or the build session; moment: until M8; budget: its size at M0). | both archived solutions build and pass their fast suites with the counts they had before the move, recorded in the pull request |
| 0.2 | A | **The solution.** `Estate.sln`; `kernel/` (references the BCL and `System.Collections.Immutable` only; implicit usings off), `io/` (kernel, DacFx 170.5.96, ScriptDom, `Microsoft.Data.SqlClient`, Bogus), `cli/` (assembly name `estate`), `tests/{Kernel.Tests, Io.Tests, Budgets.Tests}` (xUnit, CsCheck, NetArchTest). `net10.0` throughout; `global.json` on the .NET 10 SDK band, `rollForward: latestPatch`. `Directory.Build.props`: nullable, warnings as errors, deterministic, the culture and ordinal rules (CA1304, CA1305, CA1307, CA1309) as errors, `BannedApiAnalyzers` with `kernel/BannedSymbols.txt`: `System.IO`, `System.Net`, `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now`, `DateTimeOffset.UtcNow`, `Guid.NewGuid`, `Random`, `RandomNumberGenerator`, `Stopwatch`, `Environment`, `CultureInfo.CurrentCulture`, `Task`. `Directory.Packages.props` with lock files, locked restore in CI. `.editorconfig`; `.gitattributes` (LF for text). | each banned symbol, planted in `kernel/`, is a build error; the solution builds clean |
| 0.3 | A | **The first kernel types.** `Seq<T>` (element-wise equality, ordinal-sorted construction), `Result<T>` and `Refusal` (code, message, remedy), `Name`, `Fingerprint` (SHA-256 over canonical bytes), and §3's `Receipt` and `Engine`. | CsCheck properties for `Seq`'s equality and order and for `Fingerprint`'s stability across processes pass |
| 0.4 | B | **The contract.** A verb table that generates `--help --json` and the schemas under `cli/schemas/`; the envelope (`schema`, `engine`, `receipt`, `verdict`, `findings`, `exit`); the frozen exit table (0, 1, 2, 3, 4, 5, 6, 7, 9, 130) with a test that it never shrinks; `io/Write.cs` (UTF-8 without a BOM, LF, atomic replace, an existing file's declared line ending preserved). `cli` sets `DACFX_TELEMETRY_OPTOUT=1` and `DOTNET_CLI_TELEMETRY_OPTOUT=1` in its own process before DacFx loads. | `estate --help --json` validates against its schema; the writer's tests pass on both CI operating systems |
| 0.5 | C | **The words.** `README.md`, `AGENTS.md`, `CLAUDE.md` (`@AGENTS.md` and the hooks), `VALUES.md` (the instruction architecture's §4 with §4 above's re-pointing; each `Where` names the test a milestone will add, written `pending M<n>` or `pending W` until then), `DECISIONS.md` (opened with this plan's decisions: C#; lifecycle first; `predict` added and `knowledge` counted; the wing; git as the only store; `net10.0`; DacFx 170.5.96 until the pin; Docker as the substrate), `NEXT.md`, `ci/docs.manifest.json`, `ci/budgets.json` (Appendix F), `ci/packages.allow`, `.claude/settings.json` (the permissions as §4 redrafts them) and the two hooks. Start: install the .NET 10 SDK `global.json` names when it is absent (`dotnet-install.sh`), build `cli` into `.estate/bin/` when stale, in remote sessions only start Docker and the shared SQL Server container, and run `estate doctor`. End: `estate twin down --if-idle`. The Docker step is an exception to "no hook starts a daemon" and gets its decision line. | the fast tests in 0.6 pass over them; a fresh cloud session reaches `estate doctor` with the .NET 10 SDK and SQL Server up |
| 0.6 | C | **The fast tests.** `Budgets`, `Manifest`, `NoSkips`, `PackagesAllowlist`, `Decisions`, `Citations` (the engine's documents), `Register.Prose` (on `VALUES.md` first), `NoRestatedCounts`, the two dependency laws, and the archive exclusion. The five root documents are manifest rows but are excluded from `Register.Prose`, `Vocabulary`, `NoRestatedCounts` and `Citations` until M8, because they must name v1's and v2's retired terms, their counts, and verbs not yet built. `ValuesResolve` accepts `pending M<n>` until that milestone's exit and `pending W` until M8, and fails after. | green in the fast lane, under five minutes |
| 0.7 | B | **The tool folder, the first fixture, CI.** `ci/publish.{ps1,sh}`: `dotnet publish cli` (`net10.0`, framework-dependent) into `dist/estate/`, then the `net10.0` `Microsoft.Data.Tools.Schema.SqlTasks.targets` from the DacFx package and the reference stub (§1 fact 1) beside it, size printed. `Io.Tests` share one SQL Server per run, with a registered database per test (`estate_<host>_<pid>_<rand>`) so concurrent agents never collide: a container from the pinned image (pulled when absent, SQL Server Agent enabled) on Ubuntu and in cloud sessions, LocalDB on Windows. `tests/Golden/classic-minimal/`: Appendix E's classic project. GitHub Actions, on both CI operating systems (Ubuntu with SQL Server in Docker; Windows with LocalDB): a fast lane, and a SQL lane running `Io.Tests` (the CDC tests on Ubuntu), one of whose jobs denies all outbound traffic except the SQL port once the image is present. | `dist/estate/estate --version` runs; the minimal classic project builds against `dist/estate/` on both operating systems; the outbound-deny job passes |

**Day-one spikes, run where each fact lives.**

| Spike | The question | Where it runs | The answer lands in | Blocks |
|---|---|---|---|---|
| S1 | Does §1's build run on `windows-latest` and on a team laptop, and does the estate's project reference `master.dacpac` (so the tool folder must carry the system dacpacs)? | engine CI; one laptop | `estate/ledgers/toolchain.md`; a decision line | WP 1.1 |
| S2 | Which guard shapes does the pipeline's engine emit for each archetype, and do they match the committed engine's? | engine CI, both engines | the extracted guards only, under `tests/Golden/guards/` | WP 2.2 |
| S3 | Do `Script`, `LoadFromDatabase` and a probe run against Dev as the developers' group, and how long does `LoadFromDatabase` take there? | a developer's laptop | `STATE.md`; `estate/ledgers/toolchain.md` | M1 exit 1 |
| S4 | How long does a restore of a shape-volume Twin take at the estate's size, in the container and in LocalDB? | engine CI; a laptop | a decision line | WP 3.4 |
| S5 | Which OutSystems 11 metamodel tables and columns hold consumer references and publish times? | Dev's platform database, read-only | `io/Ossys`'s allowlist and SQL | WP 6.1 |
| S6 | Do the laptops and the hosted agents have the .NET 10 SDK and Docker? | the corporate agent's `STATE.md` | §17 item 7 | building on the estate |
| S7 | Which profile and which engine does the Octopus step apply? | the Octopus project's settings, read by release engineering | `estate/profiles/pipeline.publish.xml` (options only); the toolchain row | M4 exit 4's meaning; §17 items 1 and 15 |
| S8 | Which SQL Server version and compatibility level do Dev, QA and UAT run? | each environment, read-only (`SELECT @@VERSION`; `sys.databases.compatibility_level`) | the pinned image's tag and digest in the toolchain row | WP 3.4's pin; §17 item 19 |

**Exit.**
1. `dotnet build Estate.sln` is clean with warnings as errors, and
   `dotnet test --filter Category=fast` is green.
2. `archive/v1` and `archive/v2` build and pass their fast suites with unchanged counts.
3. `estate doctor` prints `DEGRADED` with a remedy per missing item; it does not claim M1 exists.
4. `tests/Golden/classic-minimal/` builds against `dist/estate/` on both CI operating systems.

**Retires.** The four hooks (814 lines), the root agent files and `sidecar/projection/CLAUDE.md`,
all archived with their trees.

**Watch for.** The runtime. Everything targets `net10.0`, the LTS the design documents name. A
machine that builds a `.sqlproj` needs the .NET 10 SDK, because MSBuild loads DacFx's `net10.0`
build task into its own runtime. The hook installs it in cloud sessions, Visual Studio 2026 installs
it on a laptop, and S6 confirms the hosted agents. The tool ships framework-dependent. Visual
Studio's own MSBuild and SSDT targets are a fallback only if S1 finds the estate's project cannot
build the committed way.

---

## 7. M1 — Read (days 2–3)

**Outcome.** From a clone, one command says, read-only and as the caller's own identity, whether
an environment matches the repository at a tag, and names each object that differs. Another says
what a branch changes, property by property, for every object type DacFx knows and for the
package's scripts and refactorlog.

| WP | Lane | What | Done when |
|---|---|---|---|
| 1.1 | B | `io/Ssdt.Build`: §1's command against the committed tool folder with `-p:DacFxTelemetryEnabled=false`, output under `.estate/build/<sha>/`; Visual Studio's MSBuild with Visual Studio's own SSDT targets through `vswhere` only if S1 finds the estate's project cannot build the committed way, stamped as that engine; a machine without the .NET 10 SDK is exit 6 with the remedy; exit 7 with the log's errors. `Load`; the refactorlog read. A ref builds through `io/Git.At`. | the classic fixtures build on both CI operating systems with the refactorlog and both deploy scripts inside; a broken `.sql` exits 7 naming the file |
| 1.2 | B | `io/Ssdt.Walk`: a `TSqlModel` into `Seq<Element>`, generic over `ObjectType.Properties` and `.Relationships`, descending from each top-level object through its composing relationships (columns, constraints, indexes); values into the kernel's closed `Value` (boolean, integer, string, enumeration name, null); a property DacFx cannot read on an object is skipped. From a package, it also emits one element each for the pre- and post-deploy scripts (their text as the build inlined it) and one per refactorlog entry. No code per type. | WP 1.3's properties pass against it |
| 1.3 | A | kernel `Element` and `Change`: canonical order; `Fingerprint.Of(Seq<Element>)`; `Change.Between(before, after, renames)` into added, removed, renamed (from the refactorlog) and changed (per property, before and after), the deploy scripts included. Pure; CsCheck over generated element sets. | law 3′ passes; the diff of each archetype pair names exactly what the archetype changes, a seed or pre-deploy edit included |
| 1.4 | B | `io/SqlServer` and a minimal `io/Substrate`. The target grammar: `env:<name>` from `estate/posture.json`; `copy:<name>`, resolved only against `.estate/copies.json`; `twin`; `ref:<git ref>`; `dacpac:<path>`. The `Named` and `Copy` types: `Copy` has no public constructor, `Substrate.Create` makes one for a local database it names `estate_<host>_<pid>_<rand>` and registers, and `Publish` exists only on `Copy`. Connections: the caller's integrated identity by default, or a reference (`env:VAR`, `file:path`) that may resolve to any connection string, SQL authentication included; a literal connection string in `estate/posture.json`, a profile or an argument is exit 6; a resolved value is never printed. `Model(target)` through `LoadFromDatabase` and the walk. `Plan(package, target, profile)` through `DacServices.Script` with both outputs. **The probe executor**, one statement at a time, parsed and checked before it runs. Against a named environment the outermost select list holds only `COUNT` or `COUNT_BIG` (of `*` or of `DISTINCT` a column), `SUM(CASE WHEN <predicate> THEN 1 ELSE 0 END)`, `MIN` or `MAX` over `LEN()` or `DATALENGTH()`, `CASE WHEN EXISTS (<subquery>) THEN 1 ELSE 0 END`, and integer literals, so every result is an integer. Bucket boundaries are lengths or literals, never read from the data. Names have one or two parts. `STRING_AGG`, `MIN`/`MAX`/`AVG` over a bare column, `INTO`, `EXEC`, DML, DDL, `OPENROWSET`, `OPENQUERY`, `OPENDATASOURCE` and `OPENXML` are refused. Against an environment classified real, a failed probe records its error number and its claim site and nothing else ("probe failed: Msg 245; message withheld"). Every statement and its row count go to `.estate/runs/<id>/queries.log`. | the compile-fail test passes; the allowlist's fuzz corpus (each forbidden form planted as a negative case) passes |
| 1.5 | A | kernel `NamedEnvironment`, and profile loading in `io`. `estate/posture.json` holds, per environment: its classification (`synthetic` or `real`, and `real` until a named lead's dated confirmation is committed), the cohorts that read it, its connection reference, the profile's path, its SQLCMD values, and the metamodel's reference. A profile contributes its deploy options and SQLCMD values only: `DacProfile.Load(path).DeployOptions`, with `TargetConnectionString` and `TargetDatabaseName` discarded at load, never used or printed; a profile containing `Password=` is exit 6 naming the file. Strict is the pipeline's profile as loaded (§1 fact 10); Permissive is the same with only `BlockOnPossibleDataLoss` flipped and can be constructed only for a `Copy`; a named environment whose profile has the guard off is a refusal. A SQLCMD value is a literal only when marked non-sensitive, otherwise a reference; a credential-shaped name holding a literal is exit 6; scripts are kept with their SQLCMD variables unsubstituted, and the substituted text lives only in memory. | the refusals carry remedies and pass `Register.Refusals`; a Permissive publish to a copy, under a profile naming a sentinel server, never connects to the sentinel |
| 1.6 | B | `io/Git`: `At(ref)` as a detached worktree under `.estate/worktrees/`, swept; `MergeBase`; `ChangedPaths`; commit and push a named branch with the caller's own git credential. | two concurrent builds of two refs share nothing |
| 1.7 | B | the verbs `doctor` (the SDK and runtime; the committed tool and its DacFx against `estate/ledgers/toolchain.md`; the build route; LocalDB and Docker; LFS), `read`, `diff`, `check drift`; and `ci/laws.sh`, which generates `LAWS.md` from the test names. | each verb's JSON validates against its schema; `LAWS.md` regenerates byte-identically |
| 1.8 | C | `tests/Golden/`: the proving ground's project as a classic twin (the proof corpus builds only from the classic form, with the committed targets, so one engine builds and publishes it), its Pipeline profile with `TargetConnectionString` removed, and the read-only principal fixture (a login with `VIEW DEFINITION` and `db_datareader` only, reached through a `file:` reference). | Appendix E runs as `Io.Tests` |

**Exit.**
1. On a team laptop, as the developer's identity, `estate check drift --target env:dev --at <Dev's
   deployed tag>` exits 0 with "Dev matches <tag>", or 5 naming each differing object, within a
   minute (R12, Dev's half). In CI every named-environment call runs as the read-only principal (R14).
2. `estate diff --from ref:main --to ref:HEAD` on the make-mandatory archetype prints
   `Column [dbo].[Customer].[Email]: Nullable true → false` and nothing else.
3. Law 2′, *a published copy converges*: publish the golden to a fresh copy and `check drift`
   exits 0; alter one column on the copy and it exits 5 naming it.
4. Law 3′, *the read is complete*: two builds of one project fingerprint equally; every archetype
   edit, seed and pre-deploy edits included, changes the fingerprint; `DacServices.Script` from a
   package to its own published copy is empty. Walk fingerprints are compared only between like
   sources, package with package and database with database.
5. *A named environment cannot be written*: `Publish` does not compile against `Named`; Permissive
   cannot be constructed for it; `copy:<a name the registry does not hold>` is exit 9, and so is a
   substrate on a host that any environment's reference resolves to (R15).
6. R13's stamp and window: every receipt names its engine; the committed engine equals the pin in
   `estate/ledgers/toolchain.md` or the release immediately before it, and anything else, newer
   included, is exit 6; while the row reads `UNPINNED`, every receipt says so.
7. R16: a literal connection string in `estate/posture.json`, a profile or an argument is exit 6; a
   denied login prints one sentence naming the environment and saying a lead's prediction will
   appear on the pull request.
8. R14's log: an Extended Events session on the read-only principal, running through a full
   `check drift`, records no DML, no DDL and no `EXEC` but DacFx's `xp_instance_regread` of the default paths.

**Retires.** v2's read side as the lifecycle's read path. It stays in the archive as the oracle for
M3's typed lens.

**Watch for.** S1 and S3 are this milestone's first work. DacFx's own catalog queries are not ours
to allowlist; the principal and the extended-events session check them, and the query log checks
ours.

---

## 8. M2 — Predict (days 4–5)

**Outcome.** A developer asks, before anything is published anywhere, whether their branch would
block or apply on Dev, and why. A dev lead asks the same of QA and UAT and pastes one block into
the pull request. Nothing is written, and no value leaves an environment.

| WP | Lane | What | Done when |
|---|---|---|---|
| 2.1 | A | kernel `Claims` and `Outcome.Predict`. The claim table is small and pure: `Nullable true → false` is NotNull; a type, length, precision or scale change that narrows is Fits; a new foreign key is Reference; a new primary key, unique constraint or unique index is Unique; a new or changed check is Check; a guard site is Presence. `Branch.Of(counts)`; `Outcome.Predict(branch, guards, claims)` gives `applies` or `blocks` and a shape: two releases when a populated Presence site guards a tightening; otherwise the shape the operation's `recognize:` block names (drop column, drop table, table rebuild, add mandatory), or *prove it* when none does. Operations in the environment's plan that the branch's own delta does not make are reported, by object, as *baseline drift*. | CsCheck properties pass, among them law 9 as a property: a populated guarded table predicts `blocks` however clean its rows |
| 2.2 | B | the guard lens in `io/SqlServer.Plan`: drop SQLCMD directive lines; substitute `$(var)` in memory from the environment's SQLCMD values; parse with `TSql160Parser`; collect each `IF EXISTS (…) RAISERROR` at state 127 (§1 fact 3) with its table and the statements it guards; re-generate the predicate and run it as `SELECT CASE WHEN EXISTS (<predicate>) THEN 1 ELSE 0 END` through the executor. | the lens finds exactly the guards S2 extracted, from both engines |
| 2.3 | B | `io/Probes`: one ScriptDom builder per claim kind, each asking the engine rather than re-deriving it, and none able to raise an error that carries a value. NotNull: rows where the column is null. Fits, for a type change: rows whose value is not null and whose `TRY_CONVERT` to the new type is null. Fits, for a character or binary narrowing: rows whose `DATALENGTH` exceeds the new length in bytes, with the trailing-space case pinned by an archetype under law P. Reference: child rows with every key column non-null and no parent. Unique: groups over the key with more than one row, under the constraint's own null semantics. Check: rows where `NOT (<CheckConstraint.Expression>)` holds, which counts false and not unknown, as SQL Server does; an expression that converts is rewritten to its `TRY_` form, or the site reads *not predictable read-only*. | every builder's output passes the allowlist; law P (M4) then holds each to the proof |
| 2.4 | A | kernel `Classify`: a matcher over the change set and the committed evidence, with patterns read from each operation file's `recognize:` block (until M7 moves them there, from `tests/Golden/patterns/`, seeded with the twelve most frequent operations), and evidence from `tests/Golden/evidence/` until M3 produces the real file. Its output's first word is *provisional*. It raises the findings no publish can: a rename without its refactorlog entry, for a column (a removed and an added column of one type at one ordinal), a table (a removed and an added table with the same columns) or a schema move (a same-named table in another schema); and a column-list change on a table in `estate/ledgers/cdc-tracked.md`. DML in a pre-deploy script is a data-modifying step. A change no pattern matches is *unclassified change: prove it*. | every archetype classifies as its operation; each rename-by-typing archetype is refused with its remedy |
| 2.5 | B | `io/Predict` and the verbs `predict`, `classify`, `check cdc`. For each requested environment the caller can reach: build head, plan against it, lift the guards, derive the claims, run the probes, and write `estate.predict/1` and a Markdown block whose last lines are a fenced machine block (the branch per environment, the counts, the receipt). The machine block's parser is built here and the gate reuses it. `check cdc --env <e>` reads `sys.tables.is_tracked_by_cdc` (a catalog read) and prints the rows for `estate/ledgers/cdc-tracked.md`; with `--commit` it commits and pushes them on a new branch, for review. | the JSON validates; the block round-trips through its parser |
| 2.6 | C | the corpus begins: `tests/Golden/changes/<operation>/` holds, per archetype, the edit to the golden project, the expected claims, and the expected prediction on an empty, a populated and a violating copy, each populated by a plain `INSERT` script under `tests/Golden/` until σ replaces them in M3; ported from v2's `SamplePr*` facts. | the archetypes so far run in one parameterized test |

**Exit.**
1. On a branch that makes a populated column NOT NULL, a developer's `estate predict` prints,
   labelled *predicted · read-only · time · engine*: Dev blocks; the guard is row presence on
   `dbo.Customer`; this many rows would violate NOT NULL on `Email`; it ships in two releases. On a
   branch whose table is empty in Dev, it prints *applies*. While Dev's plan holds operations the
   branch does not make, the prediction lists them under *baseline drift* (R11, R12).
2. A dev lead's `estate predict --env qa,uat` prints the pasteable block with its machine block. A
   developer's run against UAT fails at the login and says in one sentence that a lead's
   prediction will appear on the pull request (R16).
3. The executor's log of both runs holds only the allowlist's shapes, and the extended-events
   session on the read-only principal records no DML, DDL or `EXEC` during a full `predict` (R14).
4. The canary scan, first half: a fixture environment classified `real`, with a planted value, a
   check expression that fails to convert it, and a retype whose conversion fails on it, run through
   `predict` and `classify`. The value appears in no output, log or block, and the failed probes
   report error numbers only (R7, R16).
5. `estate classify` on each rename-by-typing archetype (column, table, schema move) refuses and
   names the remedy (`estate diff --refactorlog`, M4).

**Retires.** The probe SQL scattered through the skills; the skills cite the verb from M7.

**Watch for.** Retype semantics. The engine's explicit conversion (`TRY_CONVERT`) and the implicit
one an `ALTER COLUMN` performs can differ for some pairs (styled dates, some numeric narrowings).
Law P catches a difference on every archetype; a pair with no archetype predicts with the line
*probe semantics unproven for this conversion* until one is added.

---

## 9. M3 — Twin (days 2–8, lane T; `twin up` at branch volume by day 6)

**Outcome.** One command, from the estate's clone alone, stands up a Twin current to any ref: the
repository's schema built and published the way the pipeline does it, with rows minted from the
committed evidence and the head commit's week, at branch, shape or scale volume. The second time
takes seconds. A lead refreshes the evidence with one command and one pull request, and only when a
live branch says it is stale.

WPs 3.1 to 3.5 land by day 6, because M4 proves on copies of the branch-tier Twin. σ's port (3.1
to 3.3) needs only M0; `twin up` (3.5) needs M1's build.

| WP | Lane | What | Done when |
|---|---|---|---|
| 3.1 | T | kernel `Schema`, the typed lens σ reads over `Element`s (tables; columns with their type facets; keys; references; uniques; checks; defaults; identity; computed and temporal columns flagged), and `Order` (a mint order over references; nullable legs deferred to a second pass; a cycle with no nullable leg is a refusal naming it). | `Schema.Of` over a published copy's walk equals, mapped, the catalog v2's read side writes as JSON for the same copy (v2's CLI, run from the archive) |
| 3.2 | T | kernel `Evidence`: measures per table, column, reference and unique candidate; disclosure classes (count, bucket, vocabulary), where a vocabulary can be constructed only with an environment *confirmed* synthetic as its provenance, so the committed shape tier is literal-free by type; row tiers; and the derivation rule for a column the evidence predates (null in every existing row unless a default fills it). A codec with a round-trip property. The evidence's *standing sites* (every table's presence, every column's nulls and maximum length, every candidate reference's orphans, every unique candidate's duplicates) are what `check evidence` compares. | the codec's round trip and the literal-freedom constructor tests pass |
| 3.3 | T | kernel `Synth`, σ, ported with its tests first from v2's `SyntheticData.fs`, `SyntheticCorrection.fs`, `SyntheticVolume.fs`, `Centrality.fs`, and `Twin.Core`'s `Evidence.fs` and `DerivedEvidence.fs`. The generator is a parameter (no `Random` and no clock in the kernel); the seed is the ISO week of the head commit's committer date, computed in `io` and passed in; S-stability is kept (a schema edit re-mints only the touched tables' columns). Three tiers. The **branch tier** is exact to R6 (a row where the environment has rows, a null where it has nulls, an orphan where it has orphans, two equal values where it has duplicates, a value at the recorded maximum length), and given a `Branch` it realizes that branch and nothing more. Orphans are minted only on references the evidence records as orphaned and no trusted constraint declares. | v2's minted rows at one seed, per table ordered by key, are byte-identical (law 11′'s differential half); S-stability holds |
| 3.4 | B | `io/Substrate`, completed from M1's minimal form: Docker with the SQL Server image pinned by tag and digest in `estate/ledgers/toolchain.md` (pulled when absent; SQL Server Agent enabled, so CDC works), wherever Docker is installed; LocalDB through `sqllocaldb` where it is not; or a local developer-edition instance. The image's digest joins the engine in every receipt. Every copy registered in `.estate/copies.json` under `estate_<host>_<pid>_<rand>`; the sweep drops only this host's registered names older than a day; a substrate on a host that any environment's reference resolves to is exit 9; backup and restore into `.estate/cache/`. | two concurrent runs share nothing; a killed run is swept by the next |
| 3.5 | T | `io/Twin` and `io/Realize`, and `twin up [--tier branch\|shape\|scale] [--at <ref>] [--branch <file>]`: build at the ref (M1); create a database named from its fingerprints (`estate_twin_<schema>_<evidence>_<seed>_<tier>[_<branch>]`, §4 row 13) under a lock file in `.estate/`; publish under the pipeline's profile with drop-not-in-source, so the post-deploy seeds run as they do in every environment; mint; bulk-load in mint order; fill the deferred legs; validate every constraint `WITH CHECK CHECK` and refuse by name on a violation; back it up into the cache. Shape-tier values are realized with Bogus in `io`, never in the kernel. The Twin carries no state table, so its schema is exactly the repository's and `check drift --target twin` is its health check. | after `twin up --at <Dev's deployed tag>`, `check drift --target twin --at <that tag>` exits 0 (R12, the Twin's half) |
| 3.6 | T | `twin check`, `status`, `down`: mint twice with identical digests; every trusted reference resolves, and orphans exist exactly where the evidence records them; re-profiling the mint recovers the evidence within ε; converged by the drift oracle; `status` reads the fingerprints from the name and verifies them; `down --if-idle` drops this host's Twin copies and stops the LocalDB instance only when no `estate` process holds the lock. | law 11′ green |
| 3.7 | T | `io/Profile` and the verbs `profile` and `check evidence`. `estate profile --env dev [--vocabulary] [--counts] [--commit]` runs M2's builders over every standing site the schema declares, plus buckets, and writes `estate/evidence.shape.json`. `--vocabulary` is exit 9 until the environment is confirmed synthetic. An environment classified real is always profiled as `--counts`: row tiers only, into `estate/ledgers/row-tiers.md`, with the environment and the date on every row. `--commit` commits on a new branch, pushes it with the caller's git credential, and prints the URL that opens the pull request. `check evidence` compares the committed evidence's standing sites with a live branch over the same sites, and refuses a file that is not literal-free. | law 12′ green; the canary scan's second half passes |

**Exit.**
1. On a clean laptop with Docker or LocalDB and nothing but the estate's clone, `estate twin up`
   reaches a current Twin at shape volume in under ten minutes, and from the cache in under a minute
   (R5).
2. `estate twin check` passes; two mints under one seed are byte-identical (R8; law 11′).
3. At branch volume, probes on the Twin find each of R6's population rules met.
4. A lead's `estate profile --env dev --commit` ends at a pull request that changes only
   `estate/evidence.shape.json`. The canary scan's second half: `profile` against the fixture
   classified `real` stores no value, and `check evidence` refuses a file carrying a planted literal
   (R3, R7; law 12′).
5. No `.bak`, `.bacpac`, `.dacpac` or minted file is tracked, and `.estate/` is ignored (R4).

**Retires.** `bake.mjs` (251 lines); the Twin CLI; `twin.json` (its evidence moves to
`estate/evidence.shape.json`, and branches and tiers replace its scenarios); the hand-seeded
proving ground as a substrate (it stays as a fixture).

**Watch for.** σ is the largest new code (about 1,800 lines against v2's 1,260) and the only piece
with a byte oracle; port its tests first and keep the seed fixture red until it goes green. S4
decides whether a fresh copy for a proof is a restore (assumed under twenty seconds) or a
branch-tier mint from scratch (seconds, always available); proofs use whichever is faster, and the
shape tier keeps the restore.

---

## 10. M4 — Prove (days 7–9)

**Outcome.** One command proves a change on a fresh copy at each environment's branch, under the
pipeline's own profile and the committed engine, and returns one JSON object, one exit code and a
receipt. From here the record can say, per environment: predicted, proven at its branch, transfers
or not, and why.

| WP | Lane | What | Done when |
|---|---|---|---|
| 4.1 | B | `io/Prove`. For each distinct branch (Dev's by default; those in a prediction file or a PR body's machine block with `--branch-from`): a fresh copy, restored from the cached Twin at that branch or minted at branch tier, never the Twin itself; build head; plan against the copy; publish **Strict** (the pipeline's profile as loaded) and record the outcome: *published*; *blocked*, with `kind: guard` (the row-presence guard, Msg 50000) or `kind: violation` (the engine refused the change on existing rows, such as Msg 547, 515, 1505, 2628 or 245), and the message verbatim, since the copy holds only minted rows; or *failed* for anything else. On a block, publish **Permissive** on the same copy and record the consequence (rows before and after per touched table, widths, trust, and the error number at each violating site). Read back trust (`is_not_trusted` on every foreign key and check, a catalog read). Compute conservation hashes for a multi-phase step. Publish again and require the plan empty and every seeded table's content hash unchanged (value D5). Assert CDC silence on the Docker substrate, where SQL Server Agent runs the capture job; on LocalDB record *not provable here*. The receipt: the copy's name, the script's SHA-256, the engine, the time, the five fingerprints. Drop the copy unless `--keep`. | the make-mandatory archetype yields exit 3, `kind: guard`, the verbatim message and a receipt |
| 4.2 | A | kernel `Outcome.Prove`, `Readback`, `Transfers` (branch, engine and profile, §3); the shapes (`one release`, `two releases`, `refused`), each with its reason; "does not transfer: <the input that differs>". | law T as a kernel property |
| 4.3 | B | the verb `prove`: `--target twin` means fresh copies restored from the Twin, and `--target copy:<registered name>` a copy the registry holds; `env:` is exit 9 with "prove never publishes to a named environment; use predict". A one-screen Markdown summary by default and the JSON with `--json` (R10). With no substrate on the machine, exit 4 with the remedy, and nothing classified from the text (R27). The refactorlog writer: `estate diff --refactorlog <old> <new>` writes the entry with `XmlWriter`, with v2's `RefactorLogEmitter` as the specification. | law 6 green |
| 4.4 | C, A | the corpus completed: every operation archetype and the compound case, each with its expected classification, its prediction per branch, and its proof, the `INSERT` fixtures replaced by branch-tier mints; one parameterized test runs them all (the proof lane: nightly, on both CI operating systems). | the proof lane green on the committed engine |
| 4.5 | B | laws 6, 7, 8, 9, 10, P and T as `Io.Tests` (Appendix C); law 8's CDC half on the Docker substrate. | all green |

**Exit.**
1. The make-mandatory archetype: `estate prove` exits 3 with the guard's message verbatim, the
   Permissive consequence and a receipt, and the record line for Dev reads *predicted blocks ·
   proven blocked at Dev's branch · transfers*. A proof whose paired prediction reported baseline
   drift carries that line (R9, R10, R12).
2. Every archetype in the corpus is green on the committed engine (R9). Law P holds on every
   archetype: `blocks` is predicted exactly when Strict blocks; a site is predicted violating exactly
   when the Permissive publish fails there with the claim's error number; the count predicted on the
   copy equals the count the mint realized. Law T holds: no proof is reported at a branch, engine or
   profile it did not run at.
3. R6's check: NOT NULL on a populated table, a reference over recorded orphans, and a narrowing
   below the recorded maximum each block at branch, shape and scale.
4. R13's trust check: under the pipeline's profile as committed (S7), a declaratively added foreign
   key over clean rows ends `is_not_trusted = 0` (law 10); a profile that turns constraint
   validation off makes it a finding, never a silent success (§1 fact 11).
5. R15: `prove --target env:dev` is exit 9, and M1's compile-fail test still holds.
6. R25: a proof killed mid-publish leaves a registered copy that the next run sweeps; two
   concurrent proofs on one machine both pass.
7. R27: on a machine with no substrate, `prove` exits 4 with the remedy, and `classify` still says
   *provisional* rather than guessing from the text.

**Retires.** `prove.mjs` (427 lines) and its `sqlpackage` dependency; the `sqlpackage` install step
in the pipeline.

**Watch for.** Until S7 lands the Octopus step's profile, every proof runs under the proving
ground's Pipeline profile and its receipt says *profile not verified against the Octopus step*;
law 10's meaning is relative to it (§17 items 1 and 15).

---

## 11. M5 — Record and gate (days 10–11; the record from day 6)

**Outcome.** The pull request body is generated from receipts and leads with an evidence table,
and the three sections only a person can write are placeholders the gate will not let through. A
branch policy runs the same gate on a hosted Windows agent from the clone alone, in under ten
minutes, and posts the table.

| WP | Lane | What | Done when |
|---|---|---|---|
| 5.1 | A | kernel `Record` and `Locks`, reading `knowledge/record.md` (WP 7.2 lands it by day 5). The evidence table first: the build; the delta, with its data-loss steps and renames with refactorlog presence; *first time on this estate*, from `estate/ledgers/operations.md` (value G4); the prediction per environment, or *pending a lead*; the proof per branch, with *transfers* or the input that differs; baseline drift; in-flight collision; the engine pin; the profile's provenance; consumers to republish, or *not checked*. Then the ten sections, with "The data" carrying rows before, rows after and the approver for any data-modifying pre-deploy (value S5), and "Not checked" pre-filled with the germ's four standing items (application behaviour in Service Studio; the correctness of the business answer; Prod's population until it is profiled; any change between the last read and the deploy) above a placeholder for the author's own. Placeholders for the intent and the business answer with its owner. The register lint (the banned list) as a pure function. `Locks`: open windows from `estate/ledgers/in-flight.md`; a delta with data-loss steps on more than one table and no program row. | a record over the corpus passes `Register.Samples`' rules; every claim in it carries a receipt with its five inputs or names the one it lacks (law 5′); placeholders are detected by position |
| 5.2 | B | `io/Gate` and the verbs `gate`, `record`, `check inflight`. Base and head from git. `classify`'s blocking findings (a rename without its entry, for a column, a table or a schema move; a CDC column-list change) and the lock checks run first and exit 9 with the row, before any proof. The branches come from the PR body's machine blocks; a block whose receipt names a different head package is stale and reported so; with no block, Dev's branch comes from the committed evidence. The Twin at branch volume; a proof per distinct branch (environments that share a branch share a proof; the base is published once and restored per branch). The gate reads `estate/ledgers/row-tiers.md` for each environment it reports on, and an environment with no dated row reads *not profiled* (value G5). The record is regenerated; placeholders and banned words fail the gate, by line (R18); a body section that disagrees with the regenerated one, and an author's receipt (script SHA-256, engine, profile, branch) that disagrees with the gate's, are findings posted with the table, not failures. `changelog.json` (added, removed, renamed with refactorlog keys, retyped; the open windows). The page's repository cells (open windows, changes in review) are regenerated (R23). `gate.json`; exit 0, 3 with the verdict attached, 9, or a tooling code that says it is one. | `estate gate` run locally and in CI on one branch give identical `gate.json` apart from the receipt's place and time |
| 5.3 | C | `ci/azure/`: `gate.yml` with two jobs from the clone, each checking out with LFS and running `pwsh ./estate.ps1 gate …` on the .NET 10 SDK the hosted image carries: `ubuntu-latest` with the pinned SQL Server container (the proof, CDC included, and the posted table) and `windows-latest` with LocalDB (the Windows checkout's exactness and parity); the `RecordInPullRequest` and `VendoredCitations` checks; a host step of at most twenty-five lines of PowerShell that writes the PR body to a file and posts the evidence table as a comment with the job's access token), its README with the branch-policy steps (a YAML `pr:` trigger does not fire on Azure Repos), and the pre- and post-deploy snippets M6 uses. | the template runs end to end when queued by hand on a scratch pull request in the estate, before the policy is registered |
| 5.4 | B | `estate knowledge vendor --to <estate checkout>`, on a new branch as one pull request: `tools/estate/` (the published tool, `net10.0` and framework-dependent, with the `net10.0` targets and the reference stub), `estate.cmd` and `estate.ps1` at the estate's root (both export the telemetry opt-outs), the LFS rules for the tool's binaries, `.estate/` in `.gitignore`, the pipeline templates, and `estate/posture.json` and the ledgers seeded *only if absent*, every environment seeded `real`. Line endings: the vendoring proposes `.gitattributes` and `.editorconfig` lines for `*.sql`, `*.sqlproj`, `*.refactorlog` and `*.publish.xml` that declare the endings the files already carry (`STATE.md`), and never edits those estate-owned files in place. A new tool version appends exactly one dated row to `estate/ledgers/toolchain.md`; the README gives the `git lfs prune` step for the retired one. v2's packager overwrote the ledgers (`ssdt-agent-package.mjs:604–632`). | re-vendoring the same version changes no byte; a new version changes the tool folder and adds one ledger row, nothing else |

**Exit.**
1. A real pull request on the estate: the gate runs end to end in under ten minutes; its log holds
   no download but the pinned SQL Server image; the evidence table is posted (R17).
2. A body with a placeholder or a banned word fails, naming the line (R18). A body whose sections
   disagree with the regenerated ones gets a finding naming the section, posted with the table.
3. A pull request touching a table in an open window fails with exit 9 naming the ledger row; so
   does a delta with data-loss steps on two tables and no program row, and a rename without its
   entry (R19).
4. On a clean laptop, `git clone` then `.\estate.cmd --version` works; `git lfs ls-files` lists the
   binaries; the estate's non-LFS size grew by under a megabyte (R1, R2).
5. The drift oracle is exact on the estate's checkout on Windows with `core.autocrlf=true` and on
   Linux (R24).
6. R16's check: the whole chain (`read`, `diff`, `check drift`, `predict`, `profile`, `prove`,
   `record`, `gate`) runs in CI through a `file:` reference whose connection string carries a
   planted password. A search of every output, log, receipt, block, `gate.json`, `changelog.json`
   and record finds neither the password nor the connection string.

**Retires.** `inflight-check.mjs` (145 lines); the placeholder pipeline; the ledger overwrite.

**Watch for.** The hosted image's LocalDB start and the ten-minute budget at the estate's size.
The branch tier mints in seconds, so the builds and the publishes are the long pole. How the tool
folder crosses into the corporate network is §17's item 14, and LFS on the estate (item 4) gates
exit 4 there.

---

## 12. M6 — After deploy (days 12–13)

**Outcome.** Before Octopus deploys, one read-only step says whether the environment still
matches what was last deployed. After it deploys, the same step says whether it converged. After
someone refreshes the extension in Integration Studio and republishes the consumers in Service
Studio, one read-only check says so, entity by entity. One generated page in the wiki shows every
environment at once, and says *in sync* only when all four are true.

| WP | Lane | What | Done when |
|---|---|---|---|
| 6.1 | B | `io/Ossys`: single `SELECT`s over a committed allowlist of tables and columns: `ossys_Entity` (`Is_External`, `Physical_Table_Name`, …), `ossys_Entity_Attr`, `ossys_Espace`, and the tables and columns S5 names for consumer references and publish times. `ossys_User*` and anything holding site properties are refused by name. v1's rowset SQL is the donor, rewritten: it builds `#` temporary tables and calls `sp_executesql`, so no fragment runs verbatim. | the queries run as the read-only principal against a fixture of metamodel rows; a query naming a table outside the allowlist is refused |
| 6.2 | A | kernel `Platform.Compare`. Per repository table: *mapped* (an external entity maps it and every attribute matches); *refresh pending* (the repository has an attribute the extension does not map yet); *stale mapping* (the extension maps an attribute the repository dropped: a runtime error waiting); *SSDT-only* (no entity maps it, and none is expected to). Per platform table the repository does not declare: *platform-owned*, and `classify` refuses a change to it. Consumers to republish: modules published before the extension was. `Page.Of`: one row per environment with the deployed tag, converged, refreshed, consumers republished, open windows, baseline drift, the engine of the last deploy, and **in sync** (the lifecycle prompt's definition: the deployed tag equals the repository's, the plan is empty, the extension maps exactly the repository's attributes, and every consumer republished since the extension did); Prod greyed until profiled; the counts of each table class. | CsCheck properties over generated platform views pass |
| 6.3 | B | the verbs `check outsystems` and `check environments --page <wiki checkout> [--commit]`. The deployed tag and the engine of the last deploy come from `deployments.md` beside the page. The page carries a banner and the fingerprints of its inputs, so regeneration is byte-identical and a hand edit is detectable. | law 1′ for the page |
| 6.4 | C | the release steps. The Azure DevOps build adds `tools/estate/` and `estate/posture.json` to the Octopus package, so the steps run the tool from the release itself. **Before the deploy:** `estate check drift --target env:<e> --at <the tag deployed now>`, stopping the release on exit 5. **After it:** append a row to `deployments.md` (environment, tag, UTC time, engine), then `estate check drift --at <the new tag>`, `estate check outsystems`, and the page. Where a worker cannot reach an environment, a lead runs the same commands. | the snippets and their README are in `ci/` |

**Exit.**
1. After a real Dev deploy, `check drift` exits 0 (R20); a release into a Dev that was altered by
   hand stops at the step before the deploy.
2. After a real refresh in Integration Studio, `check outsystems --env dev` moves from *refresh
   pending* to *mapped* with no input, and consumers to republish are listed (R21, R22).
3. The page regenerates byte-identically from the same inputs, is committed to the wiki, and greys
   Prod (R23).
4. End to end, on Dev, one real change from intent to *in sync*: the page's cells turn in order, and
   each only after its act (the deploy; the convergence; the refresh; the consumers' republish).
5. The canary scan across the whole chain: `predict`, `prove` at the pasted branch, `record`,
   `gate`, `check drift`, `check outsystems` and the page, against the fixture classified `real`
   with its planted value; the value appears nowhere (R7).

**Watch for.** This plan does not assert which OutSystems 11 tables hold consumer references and
publish times; S5 names them from Dev's platform database before WP 6.1 starts.

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
| 7.1 | Citations rewritten: findings by identifier, samples by shape, handbook by title, ledgers by their new path (`knowledge/ledgers/` for the templates the engine seeds; `estate/ledgers/` for the estate's own). | the citation test is green at every commit |
| 7.2 | `knowledge/`, with `record.md` first (by day 5, because WP 5.1 reads it): then `README.md`, `authoring.md` (S0 to S8, each with its verb), `reviewing.md`, the operations with their `recognize:` blocks (moved from `tests/Golden/patterns/`; the classifier reads whatever files exist), the shared reasoning, `findings.md`, twelve samples, the ledger templates (with `cdc-tracked.md`), the eight handbook chapters. The entry skill's first step is the compound check (R28). The router's rule when `estate` is absent: stop, author the change, open the pull request marked *provisional*, and let the gate prove it (R27). | the knowledge budgets hold (≤ 11,000 lines; ≤ 90 per operation) |
| 7.3 | `io/Knowledge` and the verb `knowledge package --check`, driven by `knowledge/targets.json`: the Copilot router, five path-scoped instructions, two agents, one prompt, the skills index, the two schema pull-request templates, and the `.claude/` pointers. `knowledge vendor` (WP 5.4) carries the generated bundle to the estate. | `knowledge package --check` is clean; the instruction tests are green (Appendix D) |
| 7.4 | The pilot: one champion laptop, the vendored bundle, one real change from intent to pull request, and one compound request, which must come back as an ordered list of pull requests with the reason for the order (R28). The rung that held is a decision line. | the decision line exists |

**Exit.** `estate knowledge package --check` is clean; every instruction test is green; the first
vendoring pull request is merged in the estate; and a new developer, given only the clone, takes a
change from intent to a pull request with the evidence table in one session (R26).

**Retires.** `ssdt-agent-gates.mjs` (370 lines) and `ssdt-agent-package.mjs` (682); the
three-agent hand-off; the self-test rubric; four surfaces per operation.

---

## 14. M8 — One tool (Prod's cutover plus thirty days), and W — the wing

**M8.** The wing is decided. `archive/` leaves `main` for an `archive` branch and the `v2-final`
tag, and its CI is deleted. `cli/VERBS.md`, `cli/CONFIG.md` and `DOCS.md` are generated beside
`LAWS.md`; `ARCHITECTURE.md` is written from the design documents and this one, which move to
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

1. **Ejected engine, embedded tool.** The engine is built here and ejected from the estate: its
   source, its tests and its CI never enter the corporate repository. What the estate embeds is the
   published tool folder, two shims and the generated knowledge bundle, committed by one vendoring
   pull request per version. Whether the two repositories should become one is the operator's
   decision (§17 item 13); if they merge, the vendoring step becomes a build output and nothing else
   in this plan changes.
2. **The R-checks are the acceptance tests for both.** Every exit above names the requirements it
   meets, with the checks the backport prompt states. Whichever implementation passes a
   requirement's check first serves it; v3 replaces a backported station only when its own check
   passes on the estate.
3. **Early vendoring.** From M1 the published folder can be copied into the estate by hand
   (`tools/estate/` and the two shims) and run beside the backport: `check drift` from day 3,
   `predict` from day 5. Running both on one branch is a differential test in the field, and a
   disagreement is a finding for both.
4. **One thing crosses the network:** the tool folder and its shims, as a pull request to the
   estate (§17 item 14). No data crosses in either direction, and no knowledge travels outside the
   vendored bundle. At run time the tool opens no connection but the SQL Server it was given, and
   the one fetch is Docker pulling the pinned SQL Server image when it is absent.
5. **The estate owns** `estate/posture.json`, `estate/ledgers/`, `estate/evidence.shape.json`,
   `estate/profiles/`, `.gitattributes`, `.editorconfig` and the SSDT project. Vendoring seeds what
   is absent and never overwrites (M5's test); the verbs that propose a change to one (`profile`,
   `check cdc`, a new tool version's toolchain row, the line-ending lines) do it as a commit on a
   new branch, for review.
6. **The corporate agent's `STATE.md`** answers S1, S3, S5, S6, S7 and S8 on its first day. Those
   are the facts this repository cannot measure (§1's last paragraph).

---

## 16. How agents build it

- **Four lanes, one pull request per work package.** A: the kernel. B: io and SQL. T: σ and the
  Twin. C: knowledge, documents, the corpus and CI. A work package is a pull request of about six
  hundred changed lines at most, its test written first. The test encodes the "done when", and the
  pull request cannot merge red.
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
  before it merges; a finding is fixed in the same pull request or refused in one line. This plan
  was reviewed that way before it was pushed a second time.
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
| 1 | The engine version of the Octopus publish step, pinned (S7) | release engineering | M1 exit 6's window; M4's meaning | commit DacFx 170.5.96, the current release, on which §1 was re-measured and whose family F9's auto-trust ran on; every receipt stamps it; the ledger row stays `UNPINNED` and every receipt says so |
| 2 | The environment classification, confirmed by a named lead with a date | the dev leads | vocabulary in the evidence (WP 3.7) | every environment `real`: counts only, and `--vocabulary` refused, until the confirmation is committed |
| 3 | Read access per cohort, and read on the metamodel's allowlisted tables | the owners of the AD groups | M1 exit 1; M2 exit 2; M6 | developers already read Dev; the leads' metamodel access is requested on day one |
| 4 | Git LFS enabled on the estate | the Azure DevOps administrator | M5 exit 4 on the estate, and nothing else | the rest of M5 runs on the engine repository and a scratch estate |
| 5 | The gate registered as a branch policy | the Azure DevOps administrator | M5 exit 1 | the gate is queued by hand on a pull request until then |
| 6 | One dev lead for evidence refreshes and QA and UAT predictions | the operator | M2 exit 2; M3 exit 4 | the operator |
| 7 | The .NET 10 SDK and Docker on every laptop and hosted agent (S6) | IT; the corporate agent's `STATE.md` | building a `.sqlproj` there, and so every verb that builds | `estate doctor` names a missing SDK with its remedy; without Docker, LocalDB is the substrate and CDC reads *not provable here* |
| 8 | The Visual Studio Copilot pilot | the operator and one champion | M7's exit | the 18.4 rung (`V3_ARCHITECTURE.md` §16.1, item 2) |
| 9 | The wing: will the reverse leg run, and will anything re-emit from OSSYS | the operator | W; M8 | frozen in the archive; no port |
| 10 | Which tables CDC captures, per environment | the dev leads | M2's CDC finding; M4's CDC proofs | `check cdc` proposes the ledger on day five; CDC proofs run on the Docker substrate (§1 fact 12) |
| 11 | Prod's counts before its first release | the operator | the page's Prod row; any prediction for Prod | Prod greyed; `estate profile --env prod --counts` before its first release |
| 12 | The reviewers roster | the operator | nothing in code; the gate is load-bearing until the roster is data | `V3_ARCHITECTURE.md` §16.1, item 5 |
| 13 | One repository or two | the operator | the vendor contract | two (§15 item 1) |
| 14 | How the tool folder crosses into the corporate network | the operator and IT | WP 5.4 on the estate | a person copies the published folder into a pull request |
| 15 | The profile the Octopus step applies, committed as the estate's one `.publish.xml` (options only) (S7) | release engineering | the meaning of every proof, the drift oracle and law 10 | the proving ground's Pipeline profile, stripped of its connection string; every receipt reads *profile not verified against the Octopus step* |
| 16 | Declared bytes: the estate's `.gitattributes` and `.editorconfig` lines for its SSDT files | the dev leads | M5 exit 5 (R24) | WP 5.4 proposes lines matching what the files carry; the oracle's exactness on Windows waits for the merge |
| 17 | A service account holding every cohort's read access and the metamodel's, for a self-hosted agent | the owners of the AD groups | the gate predicting QA and UAT itself (the backport prompt's prerequisite 7) | the lead's pasted block is the prediction of record |
| 18 | The first real Dev deploy of a declarative foreign key, read back | the team, at their next such change | turns law 10 from a copy's fact into the estate's | `check drift` and the trust probe after that deploy; the result recorded in `knowledge/findings.md` with its receipt |
| 19 | The SQL Server version and compatibility level the environments run (S8) | the dev leads | the pinned image's tag | SQL Server 2022 (`mcr.microsoft.com/mssql/server:2022-latest`, pinned by digest) until S8 answers |

---

## 18. Risks this plan adds or sharpens

- **The build on the estate's real project.** §1 fact 1 was measured on a minimal project on
  Linux. The estate's project may carry database references, SQLCLR or build settings that need
  more of Visual Studio. S1 answers on day one; the fallback is Visual Studio's own MSBuild and
  SSDT targets, stamped; either way it is the project's own build, never `AddObjects`.
- **Probe semantics drifting from the engine.** Every query v3 writes is a claim about what the
  engine will do. Law P holds each one to a proof on every archetype, and a conversion with no
  archetype is labelled unproven.
- **A value escaping in an error.** SQL Server writes the offending value into conversion and
  overflow messages (Msg 245, 248, 220) and into truncation messages (Msg 2628). The probes use the
  `TRY_` forms and length tests, so they raise none of these against an environment; a failed probe
  against a real environment reports only its number; the messages that do carry values come from
  copies, whose rows are minted.
- **The time-of-check gap.** A prediction reads a moment, and UAT can change before the deploy.
  The record says so; a lead predicts again on the day of promotion; the drift oracle before the
  deploy bounds the gap and the one after closes it.
- **A pasted block is only as honest as the lead who pasted it.** A hosted agent cannot reach QA
  or UAT, so the lead's machine block is the prediction of record. The gate checks its consistency
  (the head package it names, the identity, the time) but not its origin. A service account on a
  self-hosted agent (§17 item 17) retires the paste.
- **σ's port.** The largest new code and the only byte oracle. Tests first; the seed fixture; lane
  T starts on day two.
- **The image drifting.** `2022-latest` moves. The pin is by digest in the toolchain ledger, every
  receipt stamps it, and a new digest is a decision line, never a silent pull.
- **The profile and the engine are still unverified.** Until S7 lands, every trust-state finding is
  relative to the committed engine and the proving ground's profile, and §1 fact 11 shows the
  profile can decide the answer. The stamp, the ledger row, the version window and the first real
  Dev foreign-key deploy (§17 item 18) bound it.
- **The walk at scale.** A 300-table estate is on the order of a hundred thousand property values:
  small in memory, but `LoadFromDatabase`'s time on Dev is unmeasured until S3.
- **This plan's own gravity.** Thirteen days of agents can still become v2's curve. The budget test
  is the brake, the write budget is the rule, and after M0 `NEXT.md` is the only plan.

---

## 19. The first hour

Open one pull request on the skeleton of WP 0.2: `tests/Io.Tests/SpikeTests.cs`, Appendix E as nine
assertions, one per measured fact, run against the committed engine. The classic build carries the
refactorlog and the post-deploy script. `Script` runs as the read-only login. The guard is found at
state 127 and its predicate returns 1 on a populated table. The deploy report of a converged copy
has no operations. `LoadFromDatabase` runs as that login. The property walk finds `Nullable` and
nothing else. The report stays coarse. The profile loads as options. A clean foreign key lands
trusted, and lands untrusted with validation off. When it is green on both CI operating systems, M0
has begun, and every measured fact in §1 is a test instead of a sentence. Then send S3, S6, S7 and S8
to the corporate agent as the first four questions in its `STATE.md`.

---

## Appendix A — The requirements, mapped

The lifecycle prompt's requirements, each with the milestone that meets it and the exit that runs
the prompt's own check, so the mapping holds for the backport and for v3 alike (§15).

| R | Statement, short | Milestone | Checked at |
|---|---|---|---|
| R1 | the tool is committed and runs on what laptops have | M5 | M5 exit 4 |
| R2 | binaries in LFS, pinned by commit, a dated row per version, pruned | M5 | M5 exit 4; WP 5.4 |
| R3 | committed inputs are literal-free or synthetic | M3 | M3 exit 4 |
| R4 | nothing minted is committed or shared | M3 | M3 exit 5 |
| R5 | one command builds a current Twin | M3 | M3 exit 1 (the fingerprints in the name, §4 row 13) |
| R6 | tiered volumes; the branch tier exact | M3, M4 | M3 exit 3 (population); M4 exit 3 (blocks at every tier) |
| R7 | evidence refreshed by one command and one pull request; the canary scan | M2, M3, M6 | M3 exit 4 (the pull request); M2 exit 4, M3 exit 4 and M6 exit 5 (the scan, to the whole chain) |
| R8 | the seed policy, deterministic | M3 | M3 exit 2 |
| R9 | a fresh copy and a receipt for every proof | M4 | M4 exits 1, 2 |
| R10 | one command, one validated JSON object, one screen | M4 | M4 exit 1; WP 4.3 |
| R11 | a read-only prediction per readable environment, blocks and applies | M2 | M2 exits 1, 2 |
| R12 | the baseline verified against Dev, and drift carried on every result | M1, M2, M3, M4 | M1 exit 1; WP 3.5; M2 exit 1; M4 exit 1 |
| R13 | the engine pinned, windowed, stamped; trust read back | M1, M4 | M1 exit 6; M4 exit 4; §17 item 18 |
| R14 | read-only by construction; a log of the whole run | M1, M2 | M1 exits 1, 8; M2 exit 3 |
| R15 | only Octopus writes; Permissive only on a copy | M1, M4 | M1 exit 5; M4 exit 5 |
| R16 | no credentials; refusals explained; the whole chain searched | M1, M2, M5 | M1 exit 7; M2 exits 2, 4; M5 exit 6 |
| R17 | the gate reproduces from git alone, with the pinned SQL Server image as the one fetch | M5 | M5 exit 1 |
| R18 | the evidence table; placeholders and banned words refused | M5 | M5 exit 2 |
| R19 | windows are locks; compound deltas need a program | M5 | M5 exit 3 |
| R20 | deployed means converged | M6 (the verb from M1) | M6 exit 1 |
| R21 | the platform's view is checked | M6 | M6 exit 2 |
| R22 | consumers are listed | M6 | M6 exit 2 |
| R23 | one generated page, regenerated by the gate and after each deploy | M5, M6 | WP 5.2; M6 exits 3, 4 |
| R24 | bytes are declared | M0, M5 | WP 0.2; M5 exit 5 (§17 item 16) |
| R25 | disposable copies named, swept, never shared | M3, M4 | WP 3.4; M4 exit 6 |
| R26 | one click per station, no fetch | M7 | M7's exit |
| R27 | without the tool or the substrate, stop and say so | M4, M7 | M4 exit 7; WP 7.2 |
| R28 | a compound request becomes ordered pull requests | M7 | WP 7.2; WP 7.4 |

---

## Appendix B — The germ, mapped

| The germ's line | In v3 as | Lands in |
|---|---|---|
| Five inputs fix an outcome | `Receipt` carries the five fingerprints and names a missing one; law 5′ | M0 (the type); M1 to M5 (filled and tested) |
| The transfer condition | `Branch`; `Transfers` over branch, engine and profile; law T; a proof at each environment's branch | M2, M4 |
| The read-only closure | `Named` has no `Publish`; `Copy` only from the registry; the executor's allowlist; the read-only principal; the extended-events log | M1, M2 |
| The privacy closure | integer-only probes; withheld error messages; disclosure classes; vocabulary only from environments confirmed synthetic; the canary scan across the whole chain | M1, M2, M3, M6 |
| Receipts, then reproduction | the gate recomputes from the same inputs and compares the author's receipt with its own; a disagreement is a finding | M5 |
| One oracle for convergence | the empty plan under the pipeline's profile (§1 fact 4), before the deploy and after it | M1, M6 |
| Two locks | `check inflight` for windows; `classify`'s rename findings (column, table, schema move), enforced by the gate before any proof; the refactorlog writer | M2, M4, M5 |
| The human boundary | placeholders the gate refuses; the page's cells that turn only after the refresh and the republish | M5, M6 |
| Derivation, not maintenance | the Twin, the record, the page and the bundle as functions of fingerprinted inputs; law 1′ | M3, M5, M6, M7 |
| Prerequisites, in dependency order | §17, with declared bytes (item 16) and the service account (item 17) | — |
| Outside the set, by construction | "Not checked", pre-filled with the four standing items and never empty | M5 |

---

## Appendix C — The laws, mapped

| # | `V3_ARCHITECTURE.md` §13 | Its form in the lifecycle | Lands in | Its form in the wing |
|---|---|---|---|---|
| 1 | the same inputs emit the same bytes | **1′** every generated artifact (a mint, a record, the page, the bundle, `changelog.json`) is byte-identical from the same fingerprinted inputs | M3, M5, M6, M7 | `emit` byte-identical (oracle 1) |
| 2 | emit then read is the identity | **2′** a published copy converges: the plan against it is empty | M1 | as stated |
| 3 | emit is faithful to the repository | **3′** the read is complete: stable across builds, sensitive to every archetype edit (scripts included), and a package's plan against its own published copy empty | M1 | as stated |
| 4 | a vanilla policy changes nothing | none; the lifecycle has no policy | — | as stated |
| 5 | every decision names its evidence | **5′** every claim carries its receipt, with all five inputs or the one it lacks | M5 (WP 5.1) | as stated |
| 6 | a rename keeps its key | as stated: with the entry, the plan renames and the copy keeps its data; without it, `classify` refuses and the gate stops | M2, M4, M5 | — |
| 7 | a delta's inverse undoes it | the plan from head back to base, published Permissive on the copy after the change, converges to base | M4 | — |
| 8 | an idempotent redeploy is silent | as stated: the second plan is empty, every seeded table's content hash is unchanged, and zero capture rows appear where CDC exists | M4 | — |
| 9 | the publish guard is data-blind | as stated, and as a property of `Outcome.Predict` | M2, M4 | — |
| 10 | a foreign key lands trusted | under the pipeline's profile, on the committed engine; a profile that turns validation off is a finding (§1 fact 11) | M4 | — |
| 11 | a mint has no orphans and repeats itself | **11′** a mint repeats itself byte for byte under one seed and is S-stable; every trusted reference resolves, and orphans exist only where the evidence records them on references no trusted constraint declares; re-profiling recovers the evidence within ε | M3 | — |
| 12 | the substrate carries no literal | **12′** the committed evidence holds no value except a vocabulary from an environment confirmed synthetic; by type, by `check evidence`, and by the canary scan | M3 | — |
| P | — | on a copy, the prediction equals the proof: `blocks` exactly when Strict blocks; *violating* exactly when Permissive fails at that site with the claim's error number; the predicted count equals the count the mint realized | M4 | — |
| T | — | a proof is reported for an environment only at that environment's branch, engine and profile | M4 | — |

---

## Appendix D — The instruction architecture's tests, mapped

| # | Test (`V3_INSTRUCTION_ARCHITECTURE.md` §10) | Lands in |
|---|---|---|
| 1 | `Manifest` | M0 (WP 0.6), with a row for each of the five root documents |
| 2 | `Budgets` | M0, with Appendix F's ceilings |
| 3 | `Register.Samples` | M7 |
| 4 | `Register.Refusals` | M1 (WP 1.5), when the first refusals exist |
| 5 | `Register.Prose` | M0, on `VALUES.md` first; the five root documents excluded until M8 |
| 6 | `Vocabulary` | M7; the five root documents excluded until M8 |
| 7 | `NoRestatedCounts` | M0; the five root documents excluded until M8 |
| 8 | `Citations` | M0 (WP 0.6) for the engine's documents; M7 for `knowledge/`; the five root documents excluded until M8 |
| 9 | `VerbsMatchArchitecture` | M8, when `ARCHITECTURE.md` exists; until then the verb reference is generated from `--help --json` and cannot drift |
| 10 | `LawsMatchArchitecture` | M8; `LAWS.md` is generated from M1 on (WP 1.7's `ci/laws.sh`) |
| 11 | `PackagerCheck` | M7 |
| 12 | `NoSkips` | M0 |
| 13 | `PackagesAllowlist` | M0 |
| 14 | `Decisions` | M0 |
| 15 | `FindingsAppendOnly` | M7, when `findings.md` moves |
| 16 | `ValuesResolve` | M0, accepting `pending M<n>` and `pending W`; strict at M8 |
| 17 | `VendoredCitations` (the estate's pipeline) | M5 (WP 5.3's `gate.yml`) |
| 18 | `RecordInPullRequest` (the estate's pipeline, in the gate) | M5 (WP 5.3) |

---

## Appendix E — The spike, as the first tests

§1's measured facts, reduced to the lines that established them. The spike ran each against DacFx
162.5.57 and 170.5.96, and facts 1 to 7 again on the .NET 10 SDK. WP 1.8 turns them into assertions
against the committed engine, on `net10.0`.

```bash
# fact 1: the build, with the committed tool folder as the SSDT targets path
dotnet build Estate.sqlproj -c Release -p:DacFxTelemetryEnabled=false \
  -p:NetCoreBuild=true -p:NETCoreTargetsPath="$TOOL" -p:SQLDBExtensionsRefPath="$TOOL" \
  -p:TargetFrameworkRootPath="$TOOL/refasm"   # .NETFramework/v4.7.2/{mscorlib.dll, RedistList/FrameworkList.xml}
```

```csharp
// facts 2, 4 and 10: plan as the read-only principal, under the pipeline's profile's options only;
// a report with no operations is convergence
var options = DacProfile.Load(pipelineProfile).DeployOptions;   // its TargetConnectionString is never read
var plan = new DacServices(readOnlyServer).Script(package, database, new PublishOptions
{
    GenerateDeploymentScript = true,
    GenerateDeploymentReport = true,
    DeployOptions = options,
});
XNamespace report = "http://schemas.microsoft.com/sqlserver/dac/DeployReport/2012/02";
bool converged = !XDocument.Parse(plan.DeploymentReport).Descendants(report + "Operation").Any();

// fact 3: the guard, lifted from DacFx's own script and run verbatim inside the allowlisted form
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
// (io/Ssdt's walk also descends composing relationships and skips a property DacFx cannot read on an object)
static IEnumerable<(string Property, object? Before, object? After)> Changes(TSqlObject before, TSqlObject after) =>
    before.ObjectType.Properties
          .Select(p => (p.Name, Before: before.GetProperty(p), After: after.GetProperty(p)))
          .Where(c => !Equals(c.Before, c.After));

// fact 11: trust after a declarative foreign-key add follows the profile, on either engine
var trusted   = new DacDeployOptions { BlockOnPossibleDataLoss = true };                                        // is_not_trusted = 0
var untrusted = new DacDeployOptions { BlockOnPossibleDataLoss = true, ScriptNewConstraintValidation = false }; // is_not_trusted = 1
```

---

## Appendix F — The budget, by file

Code only; tests are budgeted separately below. `ci/budgets.json` carries these ceilings from M0.

**`kernel/` (ceiling 6,800; planned 6,460)**

| File | Lines | M |
|---|---:|---|
| `Seq.cs`, `Result.cs`, `Refusal.cs`, `Name.cs`, `Fingerprint.cs` | 410 | M0 |
| `Receipt.cs` (the five inputs, `Engine`) | 180 | M0 |
| `Element.cs` | 200 | M1 |
| `Change.cs` (the deploy scripts and refactorlog included) | 280 | M1 |
| `NamedEnvironment.cs` (classification and its confirmation) | 160 | M1 |
| `Claims.cs` (claims, `Branch`, baseline drift) | 320 | M2 |
| `Outcome.cs` (predict, prove, transfer, converged) | 320 | M2, M4 |
| `Classify.cs` (renames of three kinds, shapes from `recognize:`) | 450 | M2 |
| `Schema.cs` | 520 | M3 |
| `Order.cs` | 180 | M3 |
| `Evidence.cs` (standing sites, confirmed provenance) | 720 | M3 |
| `Synth.cs` | 1,800 | M3 |
| `Record.cs`, `Locks.cs` | 570 | M5 |
| `Platform.cs`, `Page.cs` (table classes, *in sync*) | 350 | M6 |

**`io/` (ceiling 6,400; planned 5,980)**

| File | Lines | M |
|---|---:|---|
| `Write.cs`, `Json.cs`, the telemetry opt-out | 250 | M0 |
| `Ssdt.cs` (build, load, walk with scripts and refactorlog, refactorlog read) | 400 | M1 |
| `SqlServer.cs` (targets, the registry-bound `Copy`, model, plan, executor, allowlist, withheld messages) | 700 | M1 |
| a minimal `Substrate.cs` (create, register, drop) | 60 | M1 |
| `Profiles.cs` (options only; SQLCMD sensitivity), `Git.cs`, `Doctor.cs` | 440 | M1 |
| the guard lens, `Probes.cs`, `Predict.cs`, the machine block's parser | 690 | M2 |
| `Substrate.cs` completed, `Twin.cs`, `Realize.cs`, `Profile.cs` | 1,450 | M3 |
| `Prove.cs`, `RefactorLog.cs` | 550 | M4 |
| `Gate.cs`, `Vendor.cs`, `Json.cs` additions | 670 | M5 |
| `Ossys.cs` (the allowlist), the page's commit and `deployments.md` | 370 | M6 |
| `Knowledge.cs` | 400 | M7 |

**`cli/` (ceiling 1,700; planned 1,530):** the dispatcher, the verb table and the renderers (280,
M0), then one file per verb at about a hundred lines (M1 320 · M2 220 · M3 170 · M4 120 · M5 200 ·
M6 160 · M7 60).

**Per milestone (code):** M0 1,120 · M1 2,560 · M2 1,880 · M3 4,840 · M4 790 · M5 1,440 · M6 880 ·
M7 460; about 13,970 in all, against a ceiling of 14,900.

**Tests (ceiling 10,500; planned 10,200):** M0 700 · M1 1,500 · M2 1,300 · M3 2,300 · M4 1,600 ·
M5 1,200 · M6 700 · M7 900. The corpus under `tests/Golden/` is data, not code, and is held to
≤ 3,000 lines as `V3_ARCHITECTURE.md` §6.5 sets; S2 contributes the extracted guards, not whole
scripts.
