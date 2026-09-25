# Values — what the engine upholds beyond its laws

Each row names a value, its requirement in one sentence, the mechanism that holds it, and where
that mechanism lives. A `Where` names an existing test, verb, analyzer rule, hook or file; or the
test a milestone adds, marked `pending M<n>` until that milestone's exit and `pending W` for the
wing; or says *prose only*, which marks a known weakness. People judge three things no test can:
whether a name is the domain's, whether a rationale is substantive, and whether a record reads
well to the lead. Where v2 stated each value, and whether it had a mechanism there, is in
`V3_INSTRUCTION_ARCHITECTURE.md` §4.

## Safety

| # | Value | Requirement | Mechanism | Where |
|---|---|---|---|---|
| S1 | The guard is never relaxed | No verb, flag, profile or script relaxes `BlockOnPossibleDataLoss` against a named environment; Permissive exists only for a disposable copy. | a profile contributes deploy options and SQLCMD values only; Permissive is the pipeline's profile with only the guard flipped and is constructed only for a `Copy`; a named environment whose profile has the guard off is a refusal | `Io.Tests: "permissive never reaches an environment"` |
| S2 | No silent downgrade | Where the tool cannot do exactly what was asked it refuses with a code and a remedy; it never approximates, drops or comments out. | `Refusal` (code, message, remedy) is the only failure type | `Kernel.Tests: "no arm returns success by default"` — pending M1; `Budgets.Tests: Register.Refusals` |
| S3 | The explicit negative is a finding | Every record section renders; a section with nothing to report says so in one sentence and is never omitted or padded. | `Record` refuses an empty section | `Kernel.Tests: "every section renders"` — pending M5 |
| S4 | A rename keeps its data | A column rename, a table rename or a schema move with no refactorlog entry is refused before any publish. | `classify` refuses a rename by typing and names the remedy (`estate diff --refactorlog`); with the entry, the plan renames and the copy keeps its rows (law 6) | `Kernel.Tests: "a rename without its refactorlog entry is refused"` — pending M2; law 6 — pending M4 |
| S5 | Data corrections leave a receipt | A pre-deploy script that modifies rows is recorded with rows before, rows after and the approving person in the record's data section. | `prove` records rows before and after per touched table; the record's data section carries the approver; the gate refuses a data-modifying pre-deploy whose record lacks them | `Io.Tests: "rows before and after are recorded"` — pending M4; `Io.Tests: "reconcile without receipt is refused"` — pending M5 |
| S6 | The lag window is a lock | A release that touches a table with an open multi-phase window is refused at the gate. | `estate check inflight` reads `estate/ledgers/in-flight.md` and exits 9 naming the row | `Io.Tests: "a change inside an open window is refused"` — pending M5 |
| S7 | Least privilege by type | No lifecycle verb writes to a named environment; `move`, in the wing, writes only to one listed as writable in `estate/posture.json`, and never to Prod. | `Publish` exists only on `Copy`, which only the registry makes, so a publish to `Named` does not compile; a `copy:` name the registry does not hold is exit 9; `move` checks the writable list | `Io.Tests: "no verb writes to a named environment"`; the `move` clause — pending W |
| S8 | CDC-tracked tables are known | A column-list change on a CDC-tracked table blocks until the pre-deploy names the capture-instance step. | `classify` reads `estate/ledgers/cdc-tracked.md`; `check cdc` fills it from `sys.tables.is_tracked_by_cdc` | `Kernel.Tests: "a column-list change on a CDC-tracked table blocks"` — pending M2 |

## Determinism and idempotence

| # | Value | Requirement | Mechanism | Where |
|---|---|---|---|---|
| D1 | Same inputs, same bytes | `emit` over the same schema and decisions yields a byte-identical bundle on any machine, any operating system, any input ordering. | `Seq<T>` sorted at construction with ordinal comparers; `Random` and the clock banned from the kernel | law 1, its emit form — pending W |
| D2 | Culture never reaches output | Every number, date and string comparison in JSON, logs, records and scripts uses the invariant culture; a German or Turkish laptop writes the same bytes as the build agent. | CA1304, CA1305, CA1307 and CA1309 are errors and `InvariantGlobalization` is on, in `Directory.Build.props`; `CultureInfo.CurrentCulture` is in `kernel/BannedSymbols.txt`; law 1′ runs under `tr-TR` and `de-DE` | `Directory.Build.props`; `BannedSymbolsTests`; `Kernel.Tests: "law 1′ under tr-TR"` — pending M3 |
| D3 | Bytes are pinned, and written through one door | Every file the tool writes is UTF-8 without a byte-order mark with LF line endings; a file it overwrites keeps the line ending it declared; a failed write leaves the repository as it was. | `io/Write.cs`: the pin, preserve-on-overwrite, atomic replace; `.gitattributes` declares LF for text | `.gitattributes`; `WriteTests`; the same tests on both CI operating systems: the fast jobs of `.github/workflows/estate.yml` |
| D4 | Redeploy is silent | Publishing an unchanged package twice gives an empty second plan and zero CDC capture rows. | law 8, its CDC half on the Docker substrate | law 8 — pending M4 |
| D5 | Seeds converge | A second publish changes no row: the plan is empty and every seeded table's content hash is unchanged. | `prove` publishes again and requires both | `Io.Tests: "a second publish changes no row"` — pending M4 |
| D6 | The mint repeats itself | The same seed mints byte-identical rows; the mint takes its generator and its week as parameters. | law 11′; `Random` and the clock banned from the kernel; the seed is the ISO week of the head commit, computed in `io` | law 11′, with v2's rows at one seed as the fixture — pending M3 |
| D7 | Read-back fidelity | Reading back what was emitted reproduces it after `SqlType.coarsen`, modulo a named list that starts empty. | law 2, its emit form | law 2, its emit form — pending W |

## Privacy

| # | Value | Requirement | Mechanism | Where |
|---|---|---|---|---|
| P1 | No real data on a laptop | The substrate is minted from a literal-free shape tier; no repository holds a row value; no runbook restores a real backup. | law 12′: a vocabulary is constructed only with an environment confirmed synthetic as its provenance; `check evidence` refuses a file carrying a literal | law 12′ — pending M3 |
| P2 | Profiling reads shape only | `profile` captures counts, null rates, lengths, distinct counts and distributions, and writes no row value into evidence, a log or a verdict. | `Evidence` has no field for a value; the probe executor accepts only select lists whose every result is an integer | `Io.Tests: "the allowlist admits each allowed form of its corpus and refuses each forbidden one"`; `Kernel.Tests`: the literal-freedom constructor — pending M3 |
| P3 | Masking is the default | A value that must look real (a name, an email, a phone number) is minted, never copied. | shape-tier values are realized with Bogus in `io`, never in the kernel | `Kernel.Tests: "a PII column is masked"` — pending M3 |

## Security and secrets

| # | Value | Requirement | Mechanism | Where |
|---|---|---|---|---|
| X1 | No secret in a file | A connection is a reference (`env:NAME` or `file:path`); a literal connection string in `estate/posture.json`, a profile or an argument is exit 6. | references only; a profile containing `Password=`, or giving a SQLCMD value that is a connection string, is exit 6 naming the file, and its target is removed at load | `Io.Tests: "inline credential refused"` |
| X2 | No secret in output | No verdict, log line, `gate.json` or message carries a connection string, a password, or a row value from a named environment. | a resolved connection is never printed; a failed probe against an environment classified real reports its error number only; messages that carry a value come from copies, whose rows are minted | `Io.Tests: "no output contains Password="`; the whole chain through a planted password — pending M5 |
| X3 | The supply chain is pinned | Every package version is pinned centrally, restored in locked mode, and listed in `ci/packages.allow`; a new package is a decision line. | `Directory.Packages.props`; `packages.lock.json` in every project and `RestoreLockedMode` under CI | `Budgets.Tests: PackagesAllowlist` |
| X4 | Builds are deterministic | The same commit produces the same binaries. | `Deterministic`, and `ContinuousIntegrationBuild` under CI, in `Directory.Build.props` | `Directory.Build.props`; a CI comparison of two builds' hashes, prose only |
| X5 | No telemetry, no phone-home | At run time `estate` opens no connection but the SQL Server it was given, apart from the DNS lookups that compare a substrate's host with each environment's server (R15); the one fetch is Docker's pull of the pinned SQL Server image when it is absent; git runs with LFS downloads off (`GIT_LFS_SKIP_SMUDGE=1`); the build sends nothing. | `System.Net` banned from the kernel; `cli` sets `DACFX_TELEMETRY_OPTOUT=1` and `DOTNET_CLI_TELEMETRY_OPTOUT=1` in its own process before DacFx loads; the build passes `DacFxTelemetryEnabled=false`; the outbound-deny job in CI allows only the SQL port once the image is present | `BannedSymbolsTests`; the sql-offline job of `.github/workflows/estate.yml` |
| X6 | The archive is read-only | Until M8 `archive/` is readable, because v2 is the specification a port reads; no agent edits it. | `.claude/settings.json` denies `Edit(./archive/**)`; `AGENTS.md` says so | `AGENTS.md`; `.claude/settings.json` — pending M0 |

## Reproducibility and provenance

| # | Value | Requirement | Mechanism | Where |
|---|---|---|---|---|
| R1 | The engine is pinned and stamped | Every receipt names the DacFx version, the SQL Server image's digest and the tool version; a committed engine other than the pin or the release before it is exit 6; while the pin reads `UNPINNED`, every receipt says so. | the kernel's `Receipt` and `Engine`; `estate/ledgers/toolchain.md`, read by every verb that builds | `Io.Tests: "every receipt names its engine"` |
| R2 | Every claim has a receipt | A claim carries the copy's name, the script's SHA-256, the engine, the time and the five fingerprints, or names the one it lacks. | law 5′ | law 5′ — pending M5 |
| R3 | Git is the only store | There is no run store and no artifact store; every artifact is derived locally from committed inputs and cached by fingerprint under `.estate/`, which git ignores; the pull request is the record of a change. | `.gitignore` holds `.estate/`; nothing mints, backs up or builds into a tracked path | `Io.Tests: "no minted file is tracked"` — pending M3 |
| R4 | Findings are refuted in the open | A finding is overturned only by a dated entry naming the receipt that overturns it; the old entry is struck through and kept. | the findings file's format; no `F<n>` heading disappears between commits | `Budgets.Tests: FindingsAppendOnly` — pending M7 |
| R5 | The supported window is stated | .NET: the current LTS only (10). SQL Server: 2022, the image pinned by digest, until S8 names the environments' versions. DacFx: the pin and the release before it. Visual Studio: the rung the pilot lands on. | `global.json` pins the 10.0.4xx band with `rollForward: latestPatch`; the substrate probes `@@VERSION` and refuses an engine outside the window by name | `global.json`; `Io.Tests: "an engine outside the window is refused"` — pending M3 |

## Portability and operability

| # | Value | Requirement | Mechanism | Where |
|---|---|---|---|---|
| O1 | Docker is the substrate; LocalDB the fallback | Wherever Docker is installed the substrate is the SQL Server image pinned by tag and digest, pulled when absent; elsewhere LocalDB, where a CDC proof reads *not provable here*; the verb says which it chose. | `io/Substrate.cs` over both, from M3; the image's digest in every receipt | `Io.Tests` on both substrates — pending M3; CDC on LocalDB reads *not provable here* — pending M4 |
| O2 | Windows is a first-class host | The fast and fixture lanes run on `windows-latest`; object names compare case-insensitively and file names ordinally; `check drift` is exact on a Windows checkout with `core.autocrlf=true`. | CI on Ubuntu and Windows; `.gitattributes` | the windows-latest fast and sql jobs of `.github/workflows/estate.yml`; `Io.Tests: "check drift is exact on Windows"` — pending M5 |
| O3 | One command per intent | Every step of every workflow is an `estate` verb with `--json`; outside the handbook no document shows a `sqlpackage`, `sqlcmd` or `docker` invocation. | a documents test over prose | `Budgets.Tests: "no folklore"` — pending M7 |
| O4 | Refuse and route | An unsupported object or operation is refused with who does it and what to check; the tool never guesses. | refusal codes and remedies catalogued in `--help --json` | `Budgets.Tests: Register.Refusals` |
| O5 | Cleanup is guaranteed | A copy is named `estate_<host>_<pid>_<rand>`, registered in `.estate/copies.json`, dropped on exit or cancellation, and swept by the next run if a crash left it; the sweep drops only this host's registered names older than a day. | `Copy` exists only through the registry; `try/finally` around every copy; cancellation threaded through `io/` | `Io.Tests: "a killed run is swept by the next"` — pending M3 |
| O6 | Two sessions do not collide | Two `estate` processes on one machine never share a database, a worktree, an output or the Twin. | one prefix per process (`estate_<host>_<pid>_<rand>`); a worktree per ref under `.estate/worktrees/`; the Twin's database named from its fingerprints (`estate_twin_<schema>_<evidence>_<seed>_<tier>[_<branch>]`) under a lock file in `.estate/` | `Io.Tests: "two concurrent runs share nothing"` — pending M3 |
| O7 | Long operations can be cancelled | Every verb honours Ctrl-C and `--timeout` within a second, leaves the state a normal exit leaves, and exits 130. | one `CancellationToken` from `cli/` through `io/`; exit 130 in the frozen exit table | `Io.Tests: "cancel mid-prove"` — pending M4 |
| O8 | Time is budgeted | On a 300-table estate: `check drift` within a minute; `twin up` at shape volume under ten minutes, and from the local cache under a minute; `prove` bounded by the engine; the gate under ten minutes; every probe under a timeout. | the milestone exits measure each on the estate; one scale test over the golden schema replicated to 300 tables | `Io.Tests`: the scale test — pending M3 |
| O9 | Memory is bounded | `read` stays under 1 GB peak on 300 tables; `profile` streams and never materialises a table; the fast lane runs under 2 GB. | rows as `IAsyncEnumerable`; the scale test asserts peak working set | `Io.Tests`: the scale test — pending M3 |
| O10 | Offline works | With no network, every verb succeeds against a local SQL Server once the pinned image is present, and the build restores from the local cache; Docker's pull of the pinned SQL Server image is the one fetch. | X5's bans; `RestoreLockedMode` with a warmed cache | the sql-offline job of `.github/workflows/estate.yml` |
| O11 | Large output is truncated | A payload that can be large carries `"truncated": true, "full": "<path>"` and a `--summary` form; a 300-table change renders short by default. | the truncation fields in the JSON contract | `Io.Tests: "a large change renders short"` — pending M1 |
| O12 | Time is UTC, stamped at the boundary | Every date a record, a ledger row or a fingerprint carries is UTC, from one clock in `io`. | the clock banned from the kernel; one clock in `io` | `BannedSymbolsTests`; `Budgets.Tests`: the ledger date format — pending M3 |

## Legibility and maintainability

| # | Value | Requirement | Mechanism | Where |
|---|---|---|---|---|
| L1 | The register applies to every surface | Every record, refusal message, README and this file are agentless, finding-first, true-verbed, evidence-beneath, exact-named, unverified-admitted, and free of the retired vocabulary. | the register tests over every hand-written file in the manifest, every refusal message and every sample | `Budgets.Tests: Register.Prose`; `Budgets.Tests: Register.Refusals`; `Budgets.Tests: Register.Samples` — pending M7 |
| L2 | No restated count | A hand-written file never carries a count, a line number, a file list or a verb list that a generated file carries. | a documents test over the manifest's hand-written rows | `Budgets.Tests: NoRestatedCounts` |
| L3 | Budgets are tests | Code per package, tests, the corpus and every hand-written document have a ceiling, and exceeding one is a red build. | `ci/budgets.json`; the budgets in `ci/docs.manifest.json` | `Budgets.Tests: Budgets` |
| L4 | The inventory is data | Every markdown file outside `archive/` is a row in `ci/docs.manifest.json` with reader, moment, budget, kind and owner. | the manifest test | `Budgets.Tests: Manifest` |
| L5 | One vocabulary | A term is defined once, in the glossary; the retired vocabulary is banned from every surface; an SSDT term is defined at first use. | the vocabulary test | `Budgets.Tests: Vocabulary` — pending M7 |
| L6 | Laws are tests | A law is listed in `LAWS.md`, generated from M1, only from a green test with an English name; no test skips. | `LAWS.md` generated by `ci/laws.sh` from M1; zero skip attributes | `ci/laws.sh`; `Budgets.Tests: Laws`; `Budgets.Tests: NoSkips` |
| L7 | Dependencies point one way | `kernel` references the BCL and `System.Collections.Immutable` only, and no public kernel member returns a `Task`, `ValueTask` or `IAsyncEnumerable`; `io` does not reference `cli`; no project references the knowledge files or `ci/`. | NetArchTest; `kernel/BannedSymbols.txt` | `BannedSymbolsTests`; `Budgets.Tests: KernelCannotDoIo`; `Budgets.Tests: DependenciesPointOneWay` |
| L9 | Tests never retry, never skip | A flaky test is quarantined by name with a dated finding and an owner, or deleted; no retry package is allowed; no test skips itself in a way that reads as a pass. | `ci/packages.allow` holds no retry package; zero skip attributes; the TRX logger by default | `Budgets.Tests: NoSkips`; `Budgets.Tests: PackagesAllowlist` |
| L10 | CI is fast enough to wait for | The fast lane finishes in five minutes, the fixture lane in twenty, the gate in ten, the proof lane in sixty; every job declares `timeout-minutes` and prints its elapsed time. | `timeout-minutes` on every job | `.github/workflows/estate.yml`: every job declares its timeout and prints its elapsed time |
| L11 | Reading time is budgeted | The README reads in five minutes, the front door in five, an operation in three; the prose a Copilot session holds to author one change fits beside the open file, and the sum is a test. | the one-door path sum, with the ceiling the pilot sets | `Budgets.Tests: "one-door path"` — pending M7 |
| L12 | Written for the team | Every agent-facing and reviewer-facing file is written for a developer who knows SQL and OutSystems and has never used SSDT; a record is approvable with no term the reviewer must look up. | the vocabulary test catches the negative half; one anchored-explanation page; the pilot's reviewers judge the rest | `Budgets.Tests: Vocabulary` — pending M7; the pilot — pending M7; the rest, prose only |

## Agent conduct

| # | Value | Requirement | Mechanism | Where |
|---|---|---|---|---|
| A1 | Prove before claiming | An agent never states how a change ships without a verdict; a pre-proof classification says *provisional* in its first word. | `classify` prints `provisional:`; the gate regenerates the record from the proof and reports where the body differs | `Kernel.Tests: "classify says provisional first"` — pending M2; `estate gate` — pending M5 |
| A2 | Verify before diagnosing | Before claiming a tool, a daemon or a database is missing, an agent runs `estate doctor` and quotes its line. | the verb; the SessionStart hook runs it | `AGENTS.md`; `DoctorTests`; the SessionStart hook — pending M0 |
| A3 | Ask one question | Authoring poses exactly one business question a person must answer, in the developer's words, and never answers it by reading data. | the authoring path's first step; the record's *Not checked* names the open question | `Budgets.Tests: Register.Samples` — pending M7 |
| A4 | A session ends in a pull request | A session ends with code, tests, a pull request body in the register, at most one decision line and a rewritten `NEXT.md`; it writes no other document. | the manifest; `NEXT.md`'s budget | `Budgets.Tests: Manifest` |
| A5 | No self-approval, no machine approval | The gate reports and never approves; a change's author never approves it, at any seniority. | the lanes hold no approval token; the branch policy requires a dev lead | the Azure DevOps branch policy — pending M5; the author rule, prose only |
| A6 | One hop | An agent reads the entry file for its role and one hop from it; it reads `archive/` when a port or the task names it, and never edits it. | `AGENTS.md`'s read order; X6's permission | `AGENTS.md`; `.claude/settings.json` — pending M0 |
| A7 | Refuted findings stay visible | An agent that overturns a finding writes the new entry with its receipt and strikes the old; it never edits history. | R4 | `Budgets.Tests: FindingsAppendOnly` — pending M7 |
| A8 | A decision is one line | `2026-09-23 · <the decision in one sentence> · #<pull request>`; the reasoning is in the pull request. | `DECISIONS.md`'s format test | `Budgets.Tests: Decisions` |
| A9 | The record is what the gate reads | The pull request body's *how it ships* and *what proving showed* match the regenerated record, or the gate says where they differ. | `estate gate` regenerates the record; `RecordInPullRequest` runs in the estate's pipeline | `RecordInPullRequest` — pending M5 |
| A10 | A new document is a review | Adding a markdown file adds its manifest row (reader, moment, budget, owner) in the same pull request. | L4 | `Budgets.Tests: Manifest` |

## Emission fidelity

These rows hold `emit`, which is ported only if the wing is decided (`V3_MILESTONES.md` §17 item 9).

| # | Value | Requirement | Mechanism | Where |
|---|---|---|---|---|
| E1 | Emit then read is the identity | Reading back what was emitted reproduces it after `SqlType.coarsen`, modulo a named list that starts empty. | law 2, its emit form | law 2, its emit form — pending W |
| E2 | Emit over the estate changes no byte | `estate emit` against the repository's own state writes byte-identical files, on every merge. | law 3, its emit form, run in CI | law 3, its emit form — pending W |
| E3 | A vanilla policy changes nothing | With no decisions, emission reproduces the source faithfully. | law 4 | law 4 — pending W |
| E4 | SQL is built, never concatenated | Every emitted statement is a ScriptDom value rendered through its generator; in the emitting modules `string.Concat`, `string.Format`, `StringBuilder` and interpolation are banned except where the text is finally written. | a closed `Statement` hierarchy; a banned-symbol list scoped to those modules | `Io.Tests`: the analyzer runs — pending W |
| E5 | An unparseable object is refused | A trigger body ScriptDom cannot parse, or any object the reader cannot represent exactly, is a refusal with a code (`read.unparseable`). | one refusal code; one test per object kind | `Io.Tests: "unparseable trigger is refused"` — pending W |
| E6 | Data-loss steps are named before the publish | Every statement the guard will refuse on a populated table is listed before any publish runs. | the delta's data-loss steps | `Kernel.Tests` — pending W |
| E7 | Rollback is computed or admitted | Every record says how to reverse the change or names what is not auto-undone, with the recorded originals a manual restore would use. | law 7; the record's rollback section is required | `Kernel.Tests` — pending W |
| E8 | Load order has an explicit cycle policy | A cycle is refused, deferred on a nullable leg, or broken by a named allow-list; never silently ordered. | the cycle policy of `Order` | `Kernel.Tests` — pending W |
| E9 | Windows paths fit | Every path in a bundle is under 260 characters from a plausible root, no two emitted files differ only by case, and file names compare ordinally. | an emit test over the golden schema replicated to 300 tables | `Io.Tests: "bundle paths fit Windows"` — pending W |

## Process and gating

| # | Value | Requirement | Mechanism | Where |
|---|---|---|---|---|
| G1 | The pipeline reproduces the proof | The gate builds the package, stands up the Twin at branch volume, proves the pull request's change per distinct branch under the pipeline's profile, and posts the evidence table. | `estate gate`; the gate template under `ci/azure/` from M5 | `Io.Tests: "the gate agrees locally and in CI"` — pending M5 |
| G2 | A person makes the business call | The one question only a person can answer is posed, recorded with its owner, and never answered by reading data. | the record's placeholders for the intent and the business answer, which the gate refuses to pass | the gate's placeholder check — pending M5; `Budgets.Tests: Register.Samples` — pending M7 |
| G3 | One approver class | A dev lead approves every schema change and never their own; a lead's own change needs the other lead; the gate reports and never approves. | the Azure DevOps branch policy; the reviewers roster | the branch policy — pending M5; the roster, prose only |
| G4 | First time on this estate is a lookup | The added-scrutiny line comes from `estate/ledgers/operations.md`, appended at the production apply, never from memory; a shipped change with no row is a gate finding. | the record's evidence table reads the ledger | `Kernel.Tests: "first time on this estate comes from the ledger"` — pending M5 |
| G5 | Prod is not Dev | Before any Prod release, Prod's row counts land in `estate/ledgers/row-tiers.md` with their date; the gate reads the tier per environment, and one with no dated row reads *not profiled*. | `estate profile --env prod --counts`; the gate's per-environment read | `Io.Tests: "an unprofiled environment reads not profiled"` — pending M5 |
| G6 | Nothing is proven on a shared database | Every proof runs on a copy the session created and owns. | `Publish` exists only on `Copy`; `prove --target env:` is exit 9 | `Io.Tests: "no verb writes to a named environment"`, the compile-fail test; `Io.Tests: "prove never publishes to a named environment"` — pending M4 |
| G7 | The invisible half has an owner | A change is complete when the external entity is refreshed in Integration Studio in every environment, and the record's after-deploy section says so per environment. | the record's after-deploy section; `check outsystems` sees a missing refresh | `Kernel.Tests` — pending M5; `Io.Tests` — pending M6 |
| G8 | A green deploy can still lose data, and the design says so | A second publish of a landed contract-phase release can re-create a column, backfill it and report success; the lag window is a lock, and the data section carries receipts, because of it. | S5, S6, law 9 | law 9 — pending M4; `estate check inflight` — pending M5 |
| G9 | The bundle is fresh, visibly | A developer sees at a glance whether the bundle in the estate is current, and the estate's pipeline refuses a hand-edited copy. | the fingerprint in the router's first line and in `estate --version`; `estate knowledge package --check` | `Budgets.Tests: PackagerCheck` — pending M7 |
