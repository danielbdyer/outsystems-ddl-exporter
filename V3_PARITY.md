# V3 — Parity with v2: what v2 does, and the proof that v3 does it

*Written 2026-10-19 at `2a87f39`, when v3 had built M0 and M1 and v2 still proved its own behavior in
CI from the archive. Each row of the ledger below names one thing v2 does, the line in the archive
that proves it there, how v3 does it, and the test that holds it in v3, or the work package that
will. `Budgets.Tests: ParityResolves` reads every row on every push. A v2 citation must exist, down
to its line; a v3 test, law, value or exit must exist; and a row still waiting on a work package
fails once that package's milestone is complete, until it names its test. This file is archived at
M8 with the other root design documents.*

**To continue v3 to completion, paste this into a new session:**

> Use a workflow to continue v3 to completion. Follow `V3_PARITY.md`, then `V3_BUILD_PROMPT.md`, on
> branch `claude/v3-architecture-design-sl95mx` until pull request #705 merges and on `main` after.

That sentence is the operator's opt-in to multi-agent orchestration. The session that receives it
reads §0 and §5 here first, then works the loop in `V3_BUILD_PROMPT.md` §4.

**Contents.** §0 where v3 stands · §1 how a row is proven · §2 the ledger, by family · §3 the proof
lane, fact by fact · §4 the findings, one by one · §5 the way to completion · §6 what the ledger
does not prove · §7 keeping it true.

---

## 0. Where v3 stands (2026-10-19, at `2a87f39`, with the operator's rulings of that day)

| Milestone | State | What proves it | What waits, and on whom |
|---|---|---|---|
| M0 Foundation | built (#704) | exits 1, 3 and 4 by tests declaring `Exit` M0.1, M0.3 and M0.4 (`LAWS.md`) | exit 2 (the archived solutions' counts) is recorded in #704; no test repeats it |
| M1 Read | built (#704) | exits 2 to 8 by tests declaring `Exit` M1.2 to M1.8 | exit 1: the laptop check against Dev (spikes S1 and S3), the operator's, with the commands in `NEXT.md` |
| M2 Predict | started 2026-10-19 on the operator's go, with WP 2.1's first slice | — | no person; spike S2 ran on both DacFx releases, so WP 2.2 can start |
| M3 Synthetic copy | started: WP 3.1 (#705); its re-cut confirmed by the operator on 2026-10-19 (§17 item 20 of `V3_MILESTONES.md`) | — | no person; WP 3.4, then WP 3.8 and WP 3.5 |
| M4 to M7 | not started | — | their predecessors (`V3_MILESTONES.md` §5) |

**CI on `2a87f39` is green in all eleven checks** across six workflow runs:
[dbchange](https://github.com/danielbdyer/outsystems-ddl-exporter/actions/runs/39188714136)
(the fast lane on Ubuntu and Windows; the fixture lane on SQL Server in Docker, on LocalDB, and with
outbound traffic denied),
[ssdt-agent proof lane](https://github.com/danielbdyer/outsystems-ddl-exporter/actions/runs/39188714108),
[ssdt-agent gates](https://github.com/danielbdyer/outsystems-ddl-exporter/actions/runs/39188714093),
[analyzers](https://github.com/danielbdyer/outsystems-ddl-exporter/actions/runs/39188714141),
[verifiability](https://github.com/danielbdyer/outsystems-ddl-exporter/actions/runs/39188714153) and
[lint](https://github.com/danielbdyer/outsystems-ddl-exporter/actions/runs/39188714099).

**v2 is still a live oracle.** Its lanes run from `archive/v2/` on every push until M8: the proof lane
(47 `SamplePr` facts and the view-loop regression, §3), the ssdt-agent gates (with the Facsimile's
catalog lint), and the analyzer, verifiability and lint guardrails over v2's F#. Measured
2026-10-19: `AxiomTests.fs` holds 82 facts and 38 skipped stubs; the Twin's pure pool holds 73 facts
and its Docker pool 55. While both run, a disagreement between v3 and v2 on the same change is a
finding against one of them, and a publish decides it (§7).

**The tally.** `dotnet test tests/Budgets.Tests/Budgets.Tests.csproj --filter
"FullyQualifiedName~ParityResolves"` prints how many rows are held, partly held, not held yet,
waiting on the cutover tools, and retired. On 2026-10-19, after the operator's rulings of that day: 21 held, 22 partly held, 75 not held yet, 3 waiting on the cutover tools, 6 retired.

---

## 1. How a row is proven

A row's last cell is its proof in v3. Four kinds of proof count, strongest first:

1. **A differential.** v3 and an independent oracle give the same answer on the same input: v3
   against v2's output (WP 3.1's `Schema.Of` against v2's read-side catalog; WP 4.4's expected
   verdicts, which are v2's recorded verdicts, `V3_MILESTONES.md` §16), v3 against a full copy
   (law F), or v3's emitter against v2's bytes (oracle 1, among the cutover tools).
2. **The fact measured again.** A behavior v2 recorded is asserted again by a v3 test against the
   committed DacFx: §1's facts as `Io.Tests: SpikeTests`, and v2's findings as their sample changes.
3. **A law over v3's own behavior.** A property test or a fixture test states v2's axiom in v3's
   form: law 3′'s fingerprint properties carry v2's T1, law 2′ carries v2's canary.
4. **Retired with its case.** v3 does not carry it, and a case shows no verdict depends on it: the
   Facsimile's retirement cases in `archive/v2/ssdt-agent/facsimile/cases/80-twin-parity.md`, or a
   finding v2 itself overturned.

The last cell's grammar is `ParityResolves`'s: clauses separated by `;`; a clause holds when it
names a test (`Io.Tests: "its English name"` or a class), or a law, value or exit some test declares
(`law 2′`, `value S7`, `exit M1.3`); a clause ending `— not held yet: WP n.m` waits on a work package
whose milestone is not complete; `— not held yet: the cutover tools` waits on W; a clause beginning
`retired:` names the file that shows no verdict depends on it. A row is *partly held* when some of
its clauses hold and others wait.

---

## 2. The ledger, by family

### 2.1 Reading, building and comparing schemas

| ID | What v2 does | Where v2 proves it | How v3 does it | Where v3 holds it |
|---|---|---|---|---|
| PAR-001 | Builds the SSDT project into a package that carries the refactorlog and both deploy scripts. The Twin built with `TSqlModel.AddObjects` and lost both (§1 fact 8 of `V3_MILESTONES.md`); `prove.mjs` built the real project | `archive/v2/ssdt-agent/scripts/prove.mjs:216` | `io/Ssdt.Build`: the classic build against the committed DacFx targets, one build for every verb | `Io.Tests: "Fact 1 the classic build of the golden project carries the refactorlog and both deploy scripts"`; exit M0.4 |
| PAR-002 | Reads a live database's schema | `archive/v2/src/Projection.Adapters.Sql/ReadSide.fs` | `io/SqlServer.Model`: `TSqlModel.LoadFromDatabase`, then the one element reader | `Io.Tests: "Fact 5 LoadFromDatabase runs as the read only login into the model the build produced"`; `Io.Tests: "Schema of a published copy equals the catalog v2's read side writes for it"`; law 3′ |
| PAR-003 | Puts every key of two catalogs in exactly one of four partitions: added, removed, renamed, altered (A38) | `archive/v2/AXIOMS.md:1367` | `Change.Between` over `Element`s: created, dropped, renamed (from the refactorlog), altered per property | `Kernel.Tests: "A change names only elements its two models hold"`; `Kernel.Tests: "A single generated edit yields exactly that one change and undoing it the mirror"`; exit M1.2 |
| PAR-004 | Keeps identity through a rename: a rename changes the name and never the key, and the refactorlog forces it (A1, A3, A43) | `archive/v2/AXIOMS.md:1981` | renames read from the refactorlog; a rename with no entry is a drop and a create; law 6 on a copy | `Kernel.Tests: "Renames made one at a time are reported as renames whichever keys their entries were recorded under"`; `Kernel.Tests: "A rename without its refactorlog entry is a drop and an add"`; law 6 on a copy — not held yet: WP 4.5 |
| PAR-005 | Makes a change invertible: replay folds the changes and the inverse is the rollback (T12, T13) | `archive/v2/AXIOMS.md:1906` | `Change.Between` mirrors back; law 7 publishes head back to base on the copy | `Kernel.Tests: "The change from one model to another mirrors the change back through the inverted renames"`; law 7 — not held yet: WP 4.5 |
| PAR-006 | Proves a database matches its model: emit then read is the identity, in the quotient (the canary; law 2) | `archive/v2/src/Projection.Cli/Faces/Canary.fs` | `check drift`: the deploy plan under the pipeline's profile is empty | law 2′; exit M1.3; `Io.Tests: "Fact 4 the deploy report of a copy that matches its package has no operations"` |
| PAR-007 | Makes identical inputs give identical bytes, and snapshots content-addressed (T1, A22) | `archive/v2/AXIOMS.md:371` | `Fingerprint` over canonical bytes; law 1′ for every generated artifact | `Kernel.Tests: "Known vectors pin the fingerprint in every process"`; law 3′; law 1′ over generated sets — not held yet: WP 3.6 |
| PAR-008 | Names the built copy by a fingerprint that is deterministic, sensitive to content and length-prefixed (`bake.mjs`'s selftest) | `archive/v2/ssdt-agent/scripts/bake.mjs:228` | `Fingerprint.Of` over a model's canonical serialization | `Kernel.Tests: "Models a serialization without lengths tags or counts would confuse fingerprint differently"`; `Kernel.Tests: "The fingerprint of a model is independent of the order its elements are given in"` |
| PAR-009 | Writes the refactorlog entry in DacFx's format (`RefactorLogEmitter`; the format in `identity-and-refactorlog`) | `archive/v2/ssdt-agent/skills/_index/identity-and-refactorlog/SKILL.md:45` | `dbchange diff --refactorlog` writes the entry with `XmlWriter` | a malformed entry is refused: `Io.Tests: "A package whose refactor xml is malformed is refused as a refactorlog naming the package and a missing one as absent"`; the writer — not held yet: WP 4.3 |

### 2.2 What may cross an environment's boundary

| ID | What v2 does | Where v2 proves it | How v3 does it | Where v3 holds it |
|---|---|---|---|---|
| PAR-010 | Publishes only to a database it made: the Twin's own container, or the proving target `prove.mjs` resolves | `archive/v2/ssdt-agent/scripts/prove.mjs:133` | `Copy`, made only by `LocalServer` and registered; `Publish` exists only on it | value S7; exit M1.5 |
| PAR-011 | Profiles an environment's data (`LiveProfiler`): counts, null rates, lengths and distributions, and in its rich tier the values themselves, kept off the repository | `archive/v2/src/Projection.Adapters.Sql/LiveProfiler.fs` | `SqlServer.Measure`: a closed allowlist checked on the ScriptDom tree before anything runs; integers, and in one form a column's character-class signatures with their counts, the rare ones folded (`V3_MILESTONES.md` §17 item 24), so no value leaves | value P2 |
| PAR-012 | Imports evidence with `SELECT` queries, under whatever login it is given | `archive/v2/src/Twin.Runtime/EvidenceImport.fs` | the read-only principal, traced by Extended Events: no DML, no DDL, no `EXEC` but DacFx's one registry read | exit M1.8; `Io.Tests: "Fact 2 Script of a head against a published copy runs as the read only login"` |
| PAR-013 | Keeps captured values out of committed evidence (Twin law 3) | `archive/v2/THE_TWIN.md:55` | `ExistingData` is booleans from counts; law 12′ in its planted form | the planted-value scan — not held yet: WP 3.7 |
| PAR-014 | Takes secrets only as `env:` or `file:` references (`twin.json`'s D9) | `archive/v2/tests/Twin.Tests/TwinConfigTests.fs` | references resolved in io and never printed | value X1; value X2 |
| PAR-015 | Refuses an unknown configuration key by its path, every problem in one pass | `archive/v2/src/Twin.Core/TwinConfig.fs` | `dbchange/environments.json`, read by `EnvironmentsFile.Read` | `Io.Tests: "An unknown key is refused by its place in the environments file"` |
| PAR-016 | Resolves every coordinate or refuses it by name and place (Twin law 2) | `archive/v2/THE_TWIN.md:52` | the coordinate rules of WP 2.1: answered from the change, counted under the old name, counted where it is, or refused | `Kernel.Tests: "Each precondition is answered by the change counted where each object was or refused by name"`; `Kernel.Tests: "A case of the coordinate rules is answered or counted where the environment holds each object"`; `Kernel.Tests: "A column the environment lacks and the change does not create is refused by name"`; `Io.Tests: "A column added that allows NULL holds NULL in every existing row whatever its default"`; `Io.Tests: "Check counts the rows that make it false or fail its conversion and reads a created column as its default"`; `Io.Tests: "A key over a column the change creates is a duplicate once the table holds two rows"` |

### 2.3 The toolchain and the publish profile

| ID | What v2 does | Where v2 proves it | How v3 does it | Where v3 holds it |
|---|---|---|---|---|
| PAR-017 | Publishes Strict, and Permissive only after a block (`prove.mjs`) | `archive/v2/ssdt-agent/scripts/prove.mjs:267` | the pipeline's profile as loaded is Strict; Permissive flips `BlockOnPossibleDataLoss` alone and exists only for a `Copy` | `Io.Tests: "Fact 10 the pipeline profile loads as deploy options names no target and is the only profile"`; value S1 |
| PAR-018 | Records the DacFx version and leaves the pin open until the deployment step's version is known (F19) | `archive/v2/ssdt-agent/FINDINGS_AND_CHANGES.md:479` | `Toolchain`: the committed DacFx inside the ledger's window, every provenance record stamped, `UNPINNED` said aloud | exit M1.6; value R1 |
| PAR-019 | Proved in two DacFx releases on two paths, in-process 162.5.57 and external `sqlpackage` 170.x | `archive/v2/ssdt-agent/FINDINGS_AND_CHANGES.md:298` | one DacFx, 170.5.96, in-process for the build and every publish; the pipeline's presumed release, 162.5.57, plans each sample change beside it in the fixture lane (S2) | `Io.Tests: "A tool folder whose task is another DacFx is refused before the build"`; value R1; `Io.Tests: "Both DacFx releases plan each sample change as the same statements but the Query Store block 162.5.57 adds"`; `Io.Tests: "An unset Query Store is OFF to DacFx 162.5.57 and READ_WRITE to 170.5.96 and each plans it on the other's copy"` |

### 2.4 Predicting

| ID | What v2 does | Where v2 proves it | How v3 does it | Where v3 holds it |
|---|---|---|---|---|
| PAR-020 | Finds `BlockOnPossibleDataLoss` data-blind: it fires on row presence and never asks whether a row violates (F1, F7; v2's central finding) | `archive/v2/ssdt-agent/skills/_index/tightening-class/SKILL.md:51` | the check read from DacFx's own script at state 127 and run read-only; law 9 as a property of `Outcome.Predict` | `Io.Tests: "Fact 3 the data loss check is found at state 127 and its predicate returns 1 as the read only login"`; `Io.Tests: "Each sample change's plan writes the data loss checks committed for it on both DacFx releases"`; law 9; law 9 on a publish — not held yet: WP 4.5 |
| PAR-021 | Treats a constraint as a claim: one violating row refuses it, a clean table takes it, and an aggregate query predicts which | `archive/v2/ssdt-agent/skills/_index/constraint-is-a-claim/SKILL.md:31` | one aggregate query per precondition kind; law P holds each to the proof | `Io.Tests: AggregateQueriesTests`; `Io.Tests: AggregateQueriesCountTests`; law P — not held yet: WP 4.5 |
| PAR-022 | Classifies from the script's text signals (`prove.mjs`), which the deploy report is too coarse to replace | `archive/v2/ssdt-agent/scripts/prove.mjs:283` | the property read finds the precondition; the report stays coarse | `Io.Tests: "Fact 6 one read of every property finds Nullable true to false and nothing else"`; `Io.Tests: "Fact 7 the deploy report stays coarse one Alter on the table and no alert"` |
| PAR-023 | Names the operation and refuses a rename made by typing (`classify-mechanism`) | `archive/v2/ssdt-agent/skills/classify-mechanism/SKILL.md` | `Classify.Of` over each operation's `recognize:` block; *provisional* first | `Kernel.Tests: "An element the change creates is one entry with what it holds and what names only it"`; `Kernel.Tests: "An alteration that reorders its targets or gains a created element it does not hold is an entry of its own"`; `Kernel.Tests: "A clause recognizes an alteration only when it accounts for every property the alteration changes"`; `Kernel.Tests: "The first word of every classification is provisional"`; `Io.Tests: "Each sample change classifies as the operation its classify txt names"`; `Kernel.Tests: "A column renamed by typing without its refactorlog entry is refused with the remedy that writes the entry"`; `Io.Tests: "Each rename made by typing is refused at exit 9 with the remedy that writes its refactorlog entry"`; the blocks in their operations' files under `knowledge/ops/` — not held yet: WP 7.2 |

### 2.5 Proving

| ID | What v2 does | Where v2 proves it | How v3 does it | Where v3 holds it |
|---|---|---|---|---|
| PAR-024 | Proves a change: build, plan, publish Strict, classify the outcome from DacFx's messages (`blocked`, `published`, `indeterminate`, `unreachable`), Permissive after a block | `archive/v2/ssdt-agent/scripts/prove.mjs:401` | `io/Prove` and the verb `prove`, with the Facsimile's verdict table mapped onto `blockedBy` | `Kernel.Tests: "A message quoting a SQL Server error carries its number"`; `Io.Tests: "A publish the data loss check stops is server failed by Msg 50000 quoting DacFx s errors and not its informational messages"`; the verb and its verdicts — not held yet: WP 4.1 |
| PAR-025 | Lands a declarative foreign key trusted, through `WITH NOCHECK ADD` and `WITH CHECK CHECK` (F8, F9, F19) | `archive/v2/ssdt-agent/FINDINGS_AND_CHANGES.md:87` | §1 fact 11 measured again on 162.5.57 and 170.5.96; law 10 under the pipeline's profile | `Io.Tests: "Fact 11 a clean foreign key on a populated child lands trusted by default and untrusted with validation off"`; law 10 — not held yet: WP 4.5 |
| PAR-026 | Keeps a redeploy silent: the second plan empty, seeded rows unchanged, no capture rows (law 8; T15, CDC as the norm) | `archive/v2/AXIOMS.md:1944` | the second publish of every proof; CDC silence is a check a principal runs on a full SQL Server with SQL Server Agent, recorded with its command, since no local server runs SQL Server Agent | law 8 — not held yet: WP 4.5 |
| PAR-027 | Learned a failed deploy is not atomic: pre-deploy work stays, and constraint validation lands after the commit (F2, F6; verified 2026-10-15) | `archive/v2/TWIN_SUCCESSION.md:149` | the record by phase after a block, and no claim of a rollback | the record by phase — not held yet: WP 4.1 |
| PAR-028 | Holds every Twin responsibility kept or safely retired, each with its case (the Facsimile's gate G3) | `archive/v2/ssdt-agent/facsimile/ARCHITECTURE.md:73` | M4's exit 8 | the parity cases — not held yet: WP 4.4 |

### 2.6 The synthetic copy (v2's Twin)

| ID | What v2 does | Where v2 proves it | How v3 does it | Where v3 holds it |
|---|---|---|---|---|
| PAR-029 | Converges: after a green `up`, `up` again applies nothing (Twin law 1) | `archive/v2/tests/Twin.Tests.Integration/TwinSchemaLoopTests.fs:41` | `synthetic-copy up`, named from its fingerprints; identity: the plan from the base is empty | `check` and `status` — not held yet: WP 3.6 |
| PAR-030 | Mints the same rows under one seed (Twin law 6, T1 at the copy's grain) | `archive/v2/tests/Twin.Tests.Integration/TwinMintLoopTests.fs:103` | ordinal literals: the same inputs plant the same rows (D1) | law 11′, planted — not held yet: WP 3.6 |
| PAR-031 | Mints no orphan: every reference resolves (Twin law 6, K1 and L1) | `archive/v2/tests/Twin.Tests.Integration/TwinMintLoopTests.fs:64` | the planter links every child it plants; an orphan only where `ExistingData` holds one | law 11′, planted — not held yet: WP 3.6 |
| PAR-032 | Re-mints only the columns a schema edit touches (S-stable) | `archive/v2/tests/Projection.Tests/SyntheticDataTests.fs:630` | ordinal-addressed literals (D3) | the planter's plan — not held yet: WP 3.8 |
| PAR-033 | Pins exact rows a scenario declares | `archive/v2/src/Twin.Core/ScenarioCompiler.fs` | declared witnesses, DML only, step 5 of the copy build | the declared witnesses — not held yet: WP 3.5 |
| PAR-034 | Forces every constraint trusted after the mint | `archive/v2/src/Twin.Runtime/TwinDatabase.fs` | inverted: the copy reproduces the environment's trust state, because forcing trust turned a block into a wrong clean | the trust step — not held yet: WP 3.5 |
| PAR-035 | Mints rows to the measured distribution, σ with π ∘ σ ≈ id | `archive/v2/src/Projection.Core/SyntheticData.fs` | off the critical path, because no verdict depends on a distribution; returns on §17 item 20's trigger | the generated tier, on the trigger — not held yet: WP 3.3 |
| PAR-036 | Imports, derives and verifies evidence tiers, and commits the shape tier | `archive/v2/src/Twin.Core/DerivedEvidence.fs` | the evidence file returns with the shape tier | `Evidence`, on the trigger — not held yet: WP 3.2 |
| PAR-037 | Bakes the copy into a `.bacpac` or an image and restores it on laptops | `archive/v2/ssdt-agent/scripts/bake.mjs` | nothing is baked; a local cache is keyed by fingerprint (§4 row 5 of `V3_MILESTONES.md`) | retired: git is the only store, `archive/v2/ssdt-agent/facsimile/ARCHITECTURE.md` |
| PAR-038 | Masks values that look real (corrections, Faker, `twin classify`) | `archive/v2/src/Projection.Core/SyntheticCorrection.fs` | no real value reaches a copy, so nothing needs masking | retired: nothing left to mask, `archive/v2/ssdt-agent/facsimile/cases/80-twin-parity.md` |
| PAR-039 | Refuses a table two evidence sources claim (Twin law 4) | `archive/v2/THE_TWIN.md:58` | there is no evidence source left to collide | retired: no second source exists, `archive/v2/ssdt-agent/facsimile/cases/80-twin-parity.md` |
| PAR-040 | Keeps one state table, `[twin].[__state]`, excluded from comparison (Twin law 5, which no v2 test held) | `archive/v2/THE_TWIN.md:60` | no state object in any database; the fingerprints live in the copy's name | retired: no state table, `archive/v2/ssdt-agent/facsimile/ARCHITECTURE.md` |

### 2.7 The record, the gate and the lock

| ID | What v2 does | Where v2 proves it | How v3 does it | Where v3 holds it |
|---|---|---|---|---|
| PAR-041 | Holds a change that touches a table in an open two-release window (`inflight-check.mjs`; the revert F17 found) | `archive/v2/ssdt-agent/scripts/inflight-check.mjs:126` | `check inflight` and `Locks` over `dbchange/ledgers/in-flight.md` | the lock — not held yet: WP 5.2 |
| PAR-042 | Writes the pull request record in its register, each claim with its evidence (`author-pr`; law 5) | `archive/v2/ssdt-agent/skills/author-pr/SKILL.md` | `describe` from the provenance records; law 5′ | law 5′ — not held yet: WP 5.1 |
| PAR-043 | Validates a pull request in a pipeline, which proved into a new, empty database where every populated tightening passes | `archive/v2/ssdt-agent/pipelines/ssdt-agent-pr-validation.yml` | `gate`, on copies planted at each environment's existing data; the template is never instantiated | the gate — not held yet: WP 5.2 |

### 2.8 Knowledge, packaging and the documents' gates

| ID | What v2 does | Where v2 proves it | How v3 does it | Where v3 holds it |
|---|---|---|---|---|
| PAR-044 | Gives a developer's session the operation catalog, the shared reasoning and the findings | `archive/v2/ssdt-agent/skills` | `knowledge/`, rebuilt in lane C; findings append-only | `knowledge/` — not held yet: WP 7.2 |
| PAR-045 | Generates the agents' surfaces and keeps them in sync (`ssdt-agent-package.mjs`) | `archive/v2/ssdt-agent/scripts/ssdt-agent-package.mjs` | `knowledge package --check`, driven by `knowledge/targets.json` | the packager — not held yet: WP 7.3 |
| PAR-046 | Gates the tree's documents: citations resolve, the register holds, the mirror and the packaging agree | `archive/v2/ssdt-agent/scripts/ssdt-agent-gates.mjs` | the fast document tests | `Budgets.Tests: Citations`; value L1; value L4 |
| PAR-047 | Resolves every citation of a law to a live test (M16) | `archive/v2/AXIOMS.md:434` | `LAWS.md`, generated from the tests' traits | value L6 |
| PAR-048 | Keeps output byte-identical whatever the machine's culture | `archive/v2/AXIOMS.md:371` | the culture analyzers as errors; one invariant culture per command | value D2 |

### 2.9 Emission and the cutover tools (W)

These stay in v2, frozen and proved by v2's own CI, until the operator decides whether to port them
(`V3_MILESTONES.md` §14 and §17 item 9).

| ID | What v2 does | Where v2 proves it | How v3 does it | Where v3 holds it |
|---|---|---|---|---|
| PAR-049 | Emits SSDT DDL from the OutSystems metadata, the same bytes for the same inputs | `archive/v2/src/Projection.Targets.SSDT/SsdtDdlEmitter.fs` | `emit`, held by oracle 1, if ported | `emit` — not held yet: the cutover tools |
| PAR-050 | Decides NOT NULL, UNIQUE and foreign keys from the profile, and emits each decision faithfully (A12, A42) | `archive/v2/AXIOMS.md:1767` | `decide`, if ported | `decide` — not held yet: the cutover tools |
| PAR-051 | Moves data byte-faithfully and loads cycles in two phases with no residue (T17, T18) | `archive/v2/AXIOMS.md:2101` | `move`, if ported | `move` — not held yet: the cutover tools |
| PAR-052 | Reads the OutSystems metamodel whole | `archive/v2/src/Projection.Adapters.OssysSql` | `check outsystems` over a table-and-column allowlist; the full reader among the cutover tools | the allowlist — not held yet: WP 6.1 |

---

## 3. The proof lane, fact by fact

v2's proof lane (`.github/workflows/ssdt-agent-proof-lane.yml`, job `twin-facts`) runs every
`SamplePr*` fact and the view-loop regression in `archive/v2/tests/Twin.Tests.Integration/`, in
Debug, on every push. Its title says 41 facts; it runs 47 and the regression, because
`SamplePrInverseTests.fs` and `SamplePrCompoundTests.fs` arrived on 2026-08-28, after the title was
written. Each fact publishes a sample change with DacFx under the Strict profile
(`BlockOnPossibleDataLoss` on, `DropObjectsNotInSource` off) onto a database holding minted rows, and
asserts the outcome. They are v2's behavioral oracle. Each becomes a sample change in v3's corpus:
WP 2.6 seeds it, and WP 4.4 completes it with a proof per `ExistingData` value whose expected verdict
is the one v2 recorded. Where v3 already reads the same operation, the row cites that test.

| ID | What v2 asserts | Where v2 proves it | How v3 does it | Where v3 holds it |
|---|---|---|---|---|
| PAR-101 | **make-mandatory**: `OrderLine.Note` NULL → NOT NULL. Populated: Strict stops at Msg 50000 (`Rows were detected`) before any Msg 515, rows unchanged. Empty: applies | `archive/v2/tests/Twin.Tests.Integration/SamplePrMakeMandatoryTests.fs:125` | the sample change `make-mandatory`, published by `Copy.Publish` under the pipeline's profile | `Io.Tests: "A publish the data loss check stops is server failed by Msg 50000 quoting DacFx s errors and not its informational messages"`; `Io.Tests: "Fact 3 the data loss check is found at state 127 and its predicate returns 1 as the read only login"`; exit M1.2; the empty and violating copies — not held yet: WP 4.4 |
| PAR-102 | **add-mandatory**: a NOT NULL column with no default on a populated table: SQL72015 and Msg 50000, the column absent. Empty: applies | `archive/v2/tests/Twin.Tests.Integration/SamplePrTighteningTests.fs:144` | a sample change; `TableEmpty` predicts it | the verdict on each copy — not held yet: WP 4.4 |
| PAR-103 | **narrow**: `Customer.Email` below its longest value: Permissive fails at Msg 2628; Strict stops at Msg 50000. Empty: applies | `archive/v2/tests/Twin.Tests.Integration/SamplePrTighteningTests.fs:209` | a sample change; `Fits` counts the over-length class; the planter plants one over-length row | the verdict and the Permissive consequence — not held yet: WP 4.4 |
| PAR-104 | **add-unique**: a unique index over two equal values fails at Msg 1505 and is absent; over unique values it builds | `archive/v2/tests/Twin.Tests.Integration/SamplePrTighteningTests.fs:304` | a sample change; `Unique` counts duplicate groups per collation class | the verdict — not held yet: WP 4.4 |
| PAR-105 | **retype-explicit**: INT → TINYINT: Permissive fails at Msg 220 on 1000; Strict stops at Msg 50000. INT → BIGINT applies | `archive/v2/tests/Twin.Tests.Integration/SamplePrTighteningTests.fs:382` | a sample change; `Fits` counts the overflow class with `TRY_CONVERT` | the verdict — not held yet: WP 4.4 |
| PAR-106 | **add-check**: `Quantity > 0` over a zero fails at Msg 547 and leaves the check untrusted; over conforming rows it lands trusted | `archive/v2/tests/Twin.Tests.Integration/SamplePrTighteningTests.fs:471` | the sample change `add-a-check-constraint`; `Check` counts `NOT (<expression>)` | the change read: `Io.Tests: "Each sample change to the golden project changes the fingerprint"`; the verdict — not held yet: WP 4.4 |
| PAR-107 | **add-optional**: a nullable column on a populated table applies, rows unchanged | `archive/v2/tests/Twin.Tests.Integration/SamplePrCleanApplyTests.fs:160` | the sample change `add-a-nullable-column` | the change read: `Kernel.Tests: "Adding a column reports it created and appends it to its table s columns"`; the verdict — not held yet: WP 4.4 |
| PAR-108 | **add-default**: a NOT NULL column with `DEFAULT ((1))` on a populated table applies and fills every row | `archive/v2/tests/Twin.Tests.Integration/SamplePrCleanApplyTests.fs:202` | a sample change | the verdict and the filled rows — not held yet: WP 4.4 |
| PAR-109 | **add-index**: a nonclustered index on a populated table applies, rows unchanged | `archive/v2/tests/Twin.Tests.Integration/SamplePrCleanApplyTests.fs:258` | a sample change | the verdict — not held yet: WP 4.4 |
| PAR-110 | **widen**: `Email` 250 → 400 applies, the values' digest unchanged | `archive/v2/tests/Twin.Tests.Integration/SamplePrCleanApplyTests.fs:308` | the sample change `widen-a-column` | the change read: `Io.Tests: "Each sample change to the golden project changes the fingerprint"`; the verdict — not held yet: WP 4.4 |
| PAR-111 | **make-optional**: NOT NULL → NULL applies, the non-NULL count unchanged | `archive/v2/tests/Twin.Tests.Integration/SamplePrCleanApplyTests.fs:360` | a sample change | the verdict — not held yet: WP 4.4 |
| PAR-112 | **retype-implicit**: INT → BIGINT applies, the sum and maximum unchanged | `archive/v2/tests/Twin.Tests.Integration/SamplePrCleanApplyTests.fs:403` | a sample change | the verdict — not held yet: WP 4.4 |
| PAR-113 | **create-entity**: a new table with an identity key and a foreign key lands trusted and empty | `archive/v2/tests/Twin.Tests.Integration/SamplePrCleanApply2Tests.fs:185` | a sample change | the verdict and the trust read back — not held yet: WP 4.4 |
| PAR-114 | **audit-columns**: three NOT NULL columns with function defaults apply on a populated table, no NULL left | `archive/v2/tests/Twin.Tests.Integration/SamplePrCleanApply2Tests.fs:256` | a sample change | the verdict — not held yet: WP 4.4 |
| PAR-115 | **temporal-new**: a new system-versioned table with its history table applies | `archive/v2/tests/Twin.Tests.Integration/SamplePrCleanApply2Tests.fs:338` | a sample change; `Temporal` arrives with its cases | the verdict — not held yet: WP 4.4 |
| PAR-116 | **modify-default**: a changed default applies to new rows only; old rows keep the old value | `archive/v2/tests/Twin.Tests.Integration/SamplePrCleanApply2Tests.fs:411` | a sample change | the verdict and the unchanged rows — not held yet: WP 4.4 |
| PAR-117 | **change-delete-rule**: a foreign key to `ON DELETE CASCADE` applies as a drop and a re-add, and a parent delete then removes its children | `archive/v2/tests/Twin.Tests.Integration/SamplePrSchemaChangeTests.fs:164` | a sample change | the verdict — not held yet: WP 4.4 |
| PAR-118 | **modify-index**: a changed key and include list applies, rows unchanged | `archive/v2/tests/Twin.Tests.Integration/SamplePrSchemaChangeTests.fs:264` | a sample change | the verdict — not held yet: WP 4.4 |
| PAR-119 | **move-schema** with no refactorlog entry: the publish succeeds and strands the rows, the new table empty and the old one full; `ALTER SCHEMA … TRANSFER` keeps them | `archive/v2/tests/Twin.Tests.Integration/SamplePrSchemaChangeTests.fs:338` | `classify` refuses a schema move without its entry before any proof | a rename without its entry diffs as a drop and an add: `Kernel.Tests: "A rename without its refactorlog entry is a drop and an add"`; the refusal: `Kernel.Tests: "A table moved to another schema without its refactorlog entry is refused with the remedy that writes the entry"`; `Io.Tests: "Each rename made by typing is refused at exit 9 with the remedy that writes its refactorlog entry"` |
| PAR-120 | **rebuild-index** out of band: the next publish has nothing to apply | `archive/v2/tests/Twin.Tests.Integration/SamplePrSchemaChangeTests.fs:430` | `check drift`: the plan against the copy is empty | a copy in sync plans nothing: law 2′; the out-of-band rebuild — not held yet: WP 4.4 |
| PAR-121 | **drop-index**: removing the index file drops it, rows unchanged | `archive/v2/tests/Twin.Tests.Integration/SamplePrRemovalTests.fs:153` | a sample change | the verdict — not held yet: WP 4.4 |
| PAR-122 | **drop-fk**: removing the foreign key applies, and an orphan insert is then allowed | `archive/v2/tests/Twin.Tests.Integration/SamplePrRemovalTests.fs:229` | a sample change | the verdict — not held yet: WP 4.4 |
| PAR-123 | **delete-attribute**: dropping a populated column is refused for data loss and the column survives | `archive/v2/tests/Twin.Tests.Integration/SamplePrRemovalTests.fs:316` | the sample change `drop-a-column` | the change read: `Kernel.Tests: "Dropping a column reports it dropped and takes it out of its table s columns"`; the verdict — not held yet: WP 4.4 |
| PAR-124 | **delete-entity**: with `DropObjectsNotInSource` off, removing the table's file publishes and leaves the table and its rows | `archive/v2/tests/Twin.Tests.Integration/SamplePrRemovalTests.fs:376` | the pipeline's profile as loaded decides it | `Io.Tests: "Fact 10 the pipeline profile loads as deploy options names no target and is the only profile"`; the verdict — not held yet: WP 4.4 |
| PAR-125 | **archive-entity**: a batched `DELETE … OUTPUT INTO` moves rows to an archive with live plus archived equal to the original | `archive/v2/tests/Twin.Tests.Integration/SamplePrRemovalTests.fs:448` | conservation hashes for a multi-phase step | the hashes — not held yet: WP 4.1 |
| PAR-126 | **create-fk-clean**: a foreign key over a populated child with no orphans lands trusted | `archive/v2/tests/Twin.Tests.Integration/SamplePrReferenceIntegrityTests.fs:202` | the sample change `add-a-foreign-key-on-a-populated-child` | `Io.Tests: "Fact 11 a clean foreign key on a populated child lands trusted by default and untrusted with validation off"`; law 10 — not held yet: WP 4.5 |
| PAR-127 | **define-pk**: a primary key on a heap with no NULL or duplicate keys applies, rows unchanged | `archive/v2/tests/Twin.Tests.Integration/SamplePrReferenceIntegrityTests.fs:271` | a sample change | the verdict — not held yet: WP 4.4 |
| PAR-128 | **junction**: a bridge table with a composite key and two trusted foreign keys; a duplicate pair fails at Msg 2627 | `archive/v2/tests/Twin.Tests.Integration/SamplePrReferenceIntegrityTests.fs:343` | a sample change | the verdict — not held yet: WP 4.4 |
| PAR-129 | **create-fk-orphan**: a foreign key over one orphan fails at Msg 547 and is left untrusted; reconciled, it lands trusted | `archive/v2/tests/Twin.Tests.Integration/SamplePrReferenceIntegrityTests.fs:441` | a sample change; `ForeignKey` counts orphans; the planter plants one | the verdict and the trust read back — not held yet: WP 4.4 |
| PAR-130 | **toggle-trust**: `WITH CHECK CHECK` re-trusts a clean key and fails at Msg 547 over an orphan | `archive/v2/tests/Twin.Tests.Integration/SamplePrReferenceIntegrityTests.fs:520` | `is_not_trusted` read back after every proof | the read-back — not held yet: WP 4.1 |
| PAR-131 | **rename-attribute** with no refactorlog entry: refused, data kept; `sp_rename` keeps the values | `archive/v2/tests/Twin.Tests.Integration/SamplePrRenameTests.fs:164` | the sample change `rename-a-column`, with its entry | `Kernel.Tests: "A rename without its refactorlog entry is a drop and an add"`; `Kernel.Tests: "Renaming a column with its refactorlog entry is one rename not a drop and an add"`; law 6 — not held yet: WP 4.5 |
| PAR-132 | **rename-entity** with no refactorlog entry: the publish succeeds and strands the rows in the old table | `archive/v2/tests/Twin.Tests.Integration/SamplePrRenameTests.fs:256` | `classify` refuses a table renamed without its entry | `Kernel.Tests: "Renaming a table moves its columns and index and the references to them with it"`; the refusal: `Kernel.Tests: "A table renamed by typing without its refactorlog entry is refused with the remedy that writes the entry"`; `Io.Tests: "Each rename made by typing is refused at exit 9 with the remedy that writes its refactorlog entry"` |
| PAR-133 | **create-static-seed**: a `MERGE` seed lands three rows; a second run touches none and keeps the hash | `archive/v2/tests/Twin.Tests.Integration/SamplePrSeedTests.fs:176` | the second publish of every proof | a second publish changes no row — not held yet: WP 4.1 |
| PAR-134 | **edit-seed**: a label edit touches exactly one row; a re-run touches none | `archive/v2/tests/Twin.Tests.Integration/SamplePrSeedTests.fs:260` | the sample change `edit-the-post-deploy-seed` | `Kernel.Tests: "A post deploy seed edit changes the post deploy script alone"`; the rows touched — not held yet: WP 4.4 |
| PAR-135 | **extract-to-lookup**, scripted: a lookup, a key and a backfill leave no unmapped value and a trusted key | `archive/v2/tests/Twin.Tests.Integration/SamplePrSeedTests.fs:338` | a two-release program in the corpus | the program — not held yet: WP 4.4 |
| PAR-136 | **delete-seed-value**: an additive `MERGE` leaves an unused value; deleting a referenced one fails at Msg 547 | `archive/v2/tests/Twin.Tests.Integration/SamplePrSeedTests.fs:436` | a sample change | the verdict — not held yet: WP 4.4 |
| PAR-137 | **split-table**, phase 1: the copy loses and duplicates nothing; dropping the source column is refused for data loss | `archive/v2/tests/Twin.Tests.Integration/SamplePrStructuralTests.fs:235` | a multi-phase program with conservation hashes | the hashes — not held yet: WP 4.1 |
| PAR-138 | **merge-tables**, phase 1: absorbed rows equal distinct parents, and a one-to-many case is caught | `archive/v2/tests/Twin.Tests.Integration/SamplePrStructuralTests.fs:344` | a multi-phase program | the program — not held yet: WP 4.4 |
| PAR-139 | **move-attribute**, phase 1: a backfill by a one-to-one join; dropping the source is refused for data loss | `archive/v2/tests/Twin.Tests.Integration/SamplePrStructuralTests.fs:464` | a multi-phase program | the program — not held yet: WP 4.4 |
| PAR-140 | **identity-swap**: removing `IDENTITY` rebuilds the table (`tmp_ms_`) with every row, key and trust kept | `archive/v2/tests/Twin.Tests.Integration/SamplePrRebuildTests.fs:167` | `Rebuild` arrives with its cases | the rebuild's verdict — not held yet: WP 4.4 |
| PAR-141 | **temporal-convert**: a populated table becomes system-versioned with its rows kept | `archive/v2/tests/Twin.Tests.Integration/SamplePrRebuildTests.fs:307` | `Temporal` arrives with its cases | the verdict — not held yet: WP 4.4 |
| PAR-142 | **drop-default**: adding then dropping a default applies both ways | `archive/v2/tests/Twin.Tests.Integration/SamplePrInverseTests.fs:160` | the plan from head back to base, published on the copy | law 7 — not held yet: WP 4.5 |
| PAR-143 | **drop-check**: a check dropped, a violating value written, and the check's re-add fails | `archive/v2/tests/Twin.Tests.Integration/SamplePrInverseTests.fs:194` | law 7 | law 7 — not held yet: WP 4.5 |
| PAR-144 | **drop-unique**: a unique constraint dropped, a duplicate inserted, and the re-add fails | `archive/v2/tests/Twin.Tests.Integration/SamplePrInverseTests.fs:243` | law 7 | law 7 — not held yet: WP 4.5 |
| PAR-145 | **drop-pk**: a referenced primary key's drop fails the build at SQL71516; an unreferenced one leaves a heap with its rows | `archive/v2/tests/Twin.Tests.Integration/SamplePrInverseTests.fs:291` | the build's own error at exit 7 | the two drops — not held yet: WP 4.4 |
| PAR-146 | **additive batch**: two tables, two keys and a defaulted NOT NULL column in one publish apply, every row filled | `archive/v2/tests/Twin.Tests.Integration/SamplePrCompoundTests.fs:154` | the compound case of the corpus | the compound case — not held yet: WP 4.4 |
| PAR-147 | **atomic veto**: one publish holding an additive and a tightening change stops on rows, and neither lands | `archive/v2/tests/Twin.Tests.Integration/SamplePrCompoundTests.fs:187` | the compound case, and the record by phase | the compound case — not held yet: WP 4.4 |
| PAR-148 | **a view in the schema**: building the local copy with a multi-table view succeeds, where it once failed to wipe | `archive/v2/tests/Twin.Tests.Integration/TwinViewLoopTests.fs:33` | the planter skips views and the publish keeps them | the copy build — not held yet: WP 3.5 |

---

## 4. The findings, one by one

v2's engine findings live in `archive/v2/ssdt-agent/FINDINGS_AND_CHANGES.md`: F1 to F12 measured on
`sqlpackage` 170.4.83.3 and SQL Server 2022 (2026-08-16 to 08-21), F13 to F20 through `prove.mjs` on
170.5.76 (2026-08-28). Its skills under `archive/v2/ssdt-agent/skills/_index/` state the reasoning
built on them. v3's committed DacFx is 170.5.96, so a finding v3 holds was measured again there.

| ID | What v2 found | Where v2 proves it | How v3 does it | Where v3 holds it |
|---|---|---|---|---|
| PAR-201 | **F1**: narrowing a populated column is refused at Msg 50000; the check looks at row presence only | `archive/v2/ssdt-agent/FINDINGS_AND_CHANGES.md:38` | `Fits` and `TableEmpty`; law 9 | `Io.Tests: "Fact 3 the data loss check is found at state 127 and its predicate returns 1 as the read only login"`; law 9; law 9 on a publish — not held yet: WP 4.5 |
| PAR-202 | **F2**: narrowing in the model and in a pre-deploy in one release fails with the schema half-applied | `archive/v2/ssdt-agent/FINDINGS_AND_CHANGES.md:43` | the record by phase | the record — not held yet: WP 4.1 |
| PAR-203 | **F3**: a pre-deploy `ALTER` with the model unchanged lands, and the next publish restores the model | `archive/v2/ssdt-agent/FINDINGS_AND_CHANGES.md:51` | the plan re-asserts the model, and `check drift` names the difference | a column altered on a copy is drift: law 2′; the restore — not held yet: WP 4.4 |
| PAR-204 | **F4**: two releases land a populated tightening and stay stable on re-publish | `archive/v2/ssdt-agent/FINDINGS_AND_CHANGES.md:56` | the shape `two releases`; a program proved release by release on one copy | the program — not held yet: WP 4.4 |
| PAR-205 | **F5**, overturned 2026-08-21: a manual trust step after a foreign key | `archive/v2/ssdt-agent/FINDINGS_AND_CHANGES.md:63` | none: F9 overturned it, and §1 fact 11 measured the automatic trust again | retired: v2 overturned it, `archive/v2/ssdt-agent/FINDINGS_AND_CHANGES.md` |
| PAR-206 | **F6**: work a pre-deploy committed is not rolled back when the main script fails | `archive/v2/ssdt-agent/FINDINGS_AND_CHANGES.md:75` | the record by phase; knowledge says a remedy must be safe to re-run | the record — not held yet: WP 4.1 |
| PAR-207 | **F7**: make-mandatory behaves as narrowing, and two releases land it | `archive/v2/ssdt-agent/FINDINGS_AND_CHANGES.md:80` | the sample change `make-mandatory` | `Io.Tests: "A publish the data loss check stops is server failed by Msg 50000 quoting DacFx s errors and not its informational messages"`; the two releases — not held yet: WP 4.4 |
| PAR-208 | **F8**: a clean foreign key lands trusted in one release, through `WITH NOCHECK ADD` and `WITH CHECK CHECK` | `archive/v2/ssdt-agent/FINDINGS_AND_CHANGES.md:87` | §1 fact 11 | `Io.Tests: "Fact 11 a clean foreign key on a populated child lands trusted by default and untrusted with validation off"` |
| PAR-209 | **F9**: a foreign key over an orphan fails at Msg 547 and stays untrusted; a delete-rule change is a drop and a re-add; cascade reaches one level | `archive/v2/ssdt-agent/FINDINGS_AND_CHANGES.md:96` | sample changes; `ForeignKey` | the verdicts — not held yet: WP 4.4 |
| PAR-210 | **F10**: a check add fails at Msg 547 on a violating row; a unique index builds or fails at Msg 1505 and allows one NULL; a default never fills an existing column | `archive/v2/ssdt-agent/FINDINGS_AND_CHANGES.md:122` | sample changes; `Check` and `Unique` | the verdicts — not held yet: WP 4.4 |
| PAR-211 | **F11**: a foreign key does not index its child column | `archive/v2/ssdt-agent/FINDINGS_AND_CHANGES.md:140` | knowledge for the author | `knowledge/` — not held yet: WP 7.2 |
| PAR-212 | **F12**: a rebuild through `tmp_ms_xx`; a seed's explicit identity needs `IDENTITY_INSERT` (Msg 544); `DECIMAL(18,2)` → INT refused at SQL72015 and Msg 50000; a guarded seed touches no row | `archive/v2/ssdt-agent/FINDINGS_AND_CHANGES.md:148` | sample changes; `Rebuild` | the verdicts — not held yet: WP 4.4 |
| PAR-213 | **F13**: a release's strictest change vetoes the whole release | `archive/v2/ssdt-agent/FINDINGS_AND_CHANGES.md:438` | the compound case | the compound case — not held yet: WP 4.4 |
| PAR-214 | **F14**: a batch of additive changes lands in one publish, defaults fill rows, and every key ends trusted | `archive/v2/ssdt-agent/FINDINGS_AND_CHANGES.md:445` | the compound case | the compound case — not held yet: WP 4.4 |
| PAR-215 | **F15**: a post-deploy seed undid a pre-deploy repoint on a green publish; a seed naming a dropped column fails at Msg 207 | `archive/v2/ssdt-agent/FINDINGS_AND_CHANGES.md:451` | the `Authored` and `SeedDrift` kinds; `blockedBy: authored-script` | the kinds — not held yet: WP 2.1 |
| PAR-216 | **F16**: a leftover pre-deploy block reading a dropped column fails at Msg 207 before anything runs | `archive/v2/ssdt-agent/FINDINGS_AND_CHANGES.md:460` | `Authored`; a sample change | the verdict — not held yet: WP 4.4 |
| PAR-217 | **F17**: re-publishing a lagging release re-created a dropped column on a green publish and destroyed its values | `archive/v2/ssdt-agent/FINDINGS_AND_CHANGES.md:465` | `check inflight`; the lag flag on each environment's plan | the lock — not held yet: WP 5.2 |
| PAR-218 | **F18**: a referenced primary key's drop fails the build at SQL71516 | `archive/v2/ssdt-agent/FINDINGS_AND_CHANGES.md:473` | exit 7 with the build's errors | the drop — not held yet: WP 4.4 |
| PAR-219 | **F19**: every declarative constraint add ended trusted on 170.5.76 | `archive/v2/ssdt-agent/FINDINGS_AND_CHANGES.md:479` | §1 fact 11 on 170.5.96 | `Io.Tests: "Fact 11 a clean foreign key on a populated child lands trusted by default and untrusted with validation off"` |
| PAR-220 | **F20**: at 181,000 and 1.18 million rows only the nonclustered index build showed engine cost | `archive/v2/ssdt-agent/FINDINGS_AND_CHANGES.md:485` | a residual flag on the record, never a verdict; the scale tier on §17 item 20's trigger | retired: scale is a residual flag, `archive/v2/ssdt-agent/facsimile/ARCHITECTURE.md` |
| PAR-221 | **constraint-is-a-claim**: the same `ADD CONSTRAINT` lands on clean data and is refused by one violating row | `archive/v2/ssdt-agent/skills/_index/constraint-is-a-claim/SKILL.md:21` | prediction and proof agree on every sample change | law P — not held yet: WP 4.5 |
| PAR-222 | **tightening-class**: the check's text at state 127, computed once from the model before the publish | `archive/v2/ssdt-agent/skills/_index/tightening-class/SKILL.md:37` | the check read from DacFx's own script | `Io.Tests: "Fact 3 the data loss check is found at state 127 and its predicate returns 1 as the read only login"` |
| PAR-223 | **identity-and-refactorlog**: with an entry a rename is `sp_rename`; without one it is a drop and an add; a fresh deploy replays every entry | `archive/v2/ssdt-agent/skills/_index/identity-and-refactorlog/SKILL.md:25` | renames from the refactorlog; the build embeds it | `Kernel.Tests: "A rename without its refactorlog entry is a drop and an add"`; `Io.Tests: "Fact 1 the classic build of the golden project carries the refactorlog and both deploy scripts"` |
| PAR-224 | **idempotent-seed**: a guarded `MERGE` redeploys silently, zero rows and the same hash | `archive/v2/ssdt-agent/skills/_index/idempotent-seed/SKILL.md:52` | the second publish of every proof | the second publish — not held yet: WP 4.1 |
| PAR-225 | **multi-phase**: before a drop, hashes equal, absorbed rows equal distinct parents, and nothing references the source | `archive/v2/ssdt-agent/skills/_index/multi-phase/SKILL.md:83` | conservation hashes | the hashes — not held yet: WP 4.1 |
| PAR-226 | **when-to-index**: an index build takes a lock that grows with rows; online builds need Enterprise or Developer edition | `archive/v2/ssdt-agent/skills/_index/when-to-index/SKILL.md:45` | knowledge for the author | `knowledge/` — not held yet: WP 7.2 |
| PAR-227 | **v2 disagrees with itself.** Its skills say a clean add-check, add-unique or define-pk on a populated table can be refused on row presence alone (`tightening-class` line 29, `constraint-is-a-claim` line 98); its findings and proof lane show each landing (F10; `SamplePrTighteningTests.fs` lines 304 and 471; `SamplePrReferenceIntegrityTests.fs` line 271) | `archive/v2/ssdt-agent/skills/_index/tightening-class/SKILL.md:29` | v3's corpus publishes each on a populated, clean copy, and `knowledge/` states what the publish showed | the three publishes — not held yet: WP 4.4 |

---

## 5. The way to completion

v3 is complete when every row above is held, retired, or waits only on the cutover tools, and every
exit of M0 to M7 that can run here is green. What remains, in the order the work can move:

**Now, needing no person.**
1. Done: the pre-M2 code the rulings of 2026-09-25 call for, with DF-5 (`Io.Tests: SsdtTests`) and an environment that names no host (`Io.Tests: PublishProfilesTests`).
2. Spikes in CI: S2 is done (the check forms and the vocabulary's statement shapes on both DacFx releases, in
   `Io.Tests: BlockOnPossibleDataLossCheckTests` against `tests/Golden/data-loss-checks/`); S4 (CREATE, RESTORE and DROP timed in
   LocalDB 16.x), and S9 to S11 (trust state through a schema read; constraint validation after the post-deploy script on 162.x;
   system-named constraints through a publish), which run DacFx 162.5.57 as S2 does, through the child process `tests/DacFx162/`.
3. WP 3.4 (`LocalServer` completed) and WP 3.5's first step (the base built, published and backed up), then WP 3.8
   (`Witness`), now that the re-cut is confirmed; WP 3.1 (`Schema`, `Order`) is done.
4. Lane C's WP 7.1 (citations rewritten), which waits on nothing.

**Decided by the operator, 2026-10-19.** M2 has started, and with it PAR-016, 020, 021, 023, 119, 132, 201 and 215
can turn. §17 item 20 confirmed the re-cut of M3: WP 3.8 is built and PAR-030 to 034 and 148 follow; PAR-035 and 036
wait on the re-cut's trigger. Item 23 gives the agent's next step exit 8 `needs-input`, when a change first raises
one; item 24 lets WP 3.7 read a column's value-free shape from an environment classified real. Neither M2 nor M3
waits on the operator.

**Then, milestone by milestone.**

| Milestone | The rows it turns | The proof that turns them |
|---|---|---|
| M2 | the precondition kinds, the coordinate rules, the classifier's refusals, law 9 | CsCheck properties over `Outcome.Predict`; the allowlist's corpus; the corpus seeded from §3 |
| M3 | the copy: convergence, determinism, no orphans, the trust step, the declared witnesses | law 11′ and law 12′ in their planted forms; acceptance and identity on every copy |
| M4 | most of §3 and §4: every verdict v2 recorded, laws 6 to 10, F, P and T | the proof lane in v3, expected verdicts taken from v2's; law F against a full copy |
| M5 | the record, the gate, the lock | `describe` and `gate` against the corpus; `check inflight` with `inflight-check.mjs`'s cases |
| M6 | the metamodel's allowlist | `check outsystems` against a fixture of metamodel rows |
| M7 | knowledge and packaging, F11 and the indexing facts | the instruction tests; `knowledge package --check` |

**People, throughout** (`NEXT.md`, *Waiting on others*): the laptop check (M1 exit 1), S5 to S8, a
`VIEW DEFINITION`-only login for Prod (§17 item 21), and whether a package feed exists (item 22).

---

## 6. What the ledger does not prove

- **That v3 and v2 agree on the same run.** No harness runs v2's proof lane and v3's `prove` on one
  change and compares the two; WP 4.4 takes v2's recorded verdicts as its expectations instead, and
  v2's lane keeps running until M8, so a later disagreement shows up as a red test on one side.
- **Anything inside the environments' network.** Dev, QA, UAT and the laptops are out of reach from
  this repository's CI; the exits that need them are recorded with the command a person runs.
- **The Facsimile's `verify` cases.** Each first run decides its case (`archive/v2/ssdt-agent/facsimile/ARCHITECTURE.md` §11).
- **v2's emission, policy and data movement.** v2's own CI proves them until M8; v3 holds none of
  them unless the operator decides to port the cutover tools.
- **Most of `AXIOMS.md`.** Its entries about emission, policy, passes, lineage, the registry and
  the snapshot store describe how v2 projects and how it was built. v3 carries the ones about
  identity through a rename, diffing, inversion, determinism and the silent redeploy (§2.1, §2.5) and
  leaves the rest with v2 (`V3_ARCHITECTURE.md` §13, closing paragraph).

---

## 7. Keeping it true

- **The pull request that adds a test holding a row changes that row's last cell in the same
  commit.** `ParityResolves` fails a row whose cited test is gone, and a row still waiting on a work
  package whose milestone has closed.
- **A new v2 behavior found to matter gets a row,** with its archive line, before the work package
  that carries it starts.
- **A disagreement between v3 and v2 on the same change is a finding.** A publish decides it, the
  finding is recorded with its provenance (`AGENTS.md`, *When something fails*), and the losing side's
  row or test changes.
- **The tally is a dated line in §0,** rewritten by the session that changes it; `ParityResolves`
  prints the live one.
- **At M8** this file is archived with the other root design documents, after the last row is held,
  retired, or left with the cutover tools by the operator's decision.
