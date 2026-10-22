# dbchange-lite: the SSDT slice in under 2,000 lines

2026-10-22 · Daniel Dyer

dbchange-lite is a C# command-line tool of about 1,850 lines over DacFx. It answers three read-only questions: is the environment where the repository says it is, what does this branch change, and will the change block when it deploys. Where reading cannot settle the last, it deploys the change to a disposable local copy, never to a shared database.

Each answer is one JSON object that lists what is still unproven. The skill files carry the reasoning; the tool does only what an agent cannot do by reading SQL: build a dacpac, ask DacFx for a deploy plan, check rows, and deploy to a local copy. The agent's job is to drive every open item in the tool's answer to proven, or to a named person who must answer it.

v3 on `claude/v3-architecture-design-sl95mx` stays the specification. The slice ports a v3 behaviour only when a false finding in the field shows it is needed, the way v3 ports from v2's F#. The source lives in the SSDT repository, where the team can read it.

## The confidence ladder

Full confidence means three things. Dev, and each environment a lead reads, stand on rung 3 or higher. Prod, which nobody reads by your ruling of Oct 18, ships in the shape that holds under the worst case and is marked *assumed*. And rung 4 is reached wherever the plan holds something no read-only query settles.

Each rung is settled by evidence an engine produced, never by reading the change's SQL. This keeps v2's "proving is classifying", with a read-only rung added below the deploy.

| Rung | What is settled | The evidence | Who produces it |
| --- | --- | --- | --- |
| 0 · Intent | The operation is named from the catalog, the developer confirmed it, and the plan's operations match it | The operation's slug, the developer's yes, and `predict --intent` naming no unexplained operation | `confirm-intent`, `classify-mechanism`, `predict` |
| 1 · Baseline | The environment matches the repository at the release it was deployed from | `drift` answers no differences, or only known false-drift cases | `dbchange-lite drift` |
| 2 · Plan | What DacFx will do under the Octopus step's profile: each operation, rebuild and drop, and each data-loss guard | The deploy report and script DacFx generates against the environment's model | `dbchange-lite predict` |
| 3 · Measured | Whether each guard fires and each precondition holds, per environment | Each guard's row presence read from `sys.partitions`, and each orphan, duplicate or violating-row check run read-only, each with its query and result | `dbchange-lite predict` |
| 4 · Proven | The verdict a real deploy gives on rows shaped like the environment's | The deploy output under `BlockOnPossibleDataLoss`, on a disposable LocalDB copy | `dbchange-lite prove`, driven by `prove-on-dacpac` |

Rung 4 is required only when `Triggers.cs` finds something no read-only query settles: a statement it does not recognise, a table rebuild, an `IDENTITY` swap, a foreign key returned to trusted, a deploy script that touches the plan's tables, or a CDC-tracked table. Elsewhere rung 3 predicts the block exactly, because it answers the predicate DacFx itself wrote. That is how the make-mandatory finding is caught without a deploy: the guard is `IF EXISTS (SELECT TOP 1 1 FROM dbo.Customer)`, row presence, not blank values.

**The agent's stopping rule.** Every answer carries an `open` list. Each item names the environment, the rung it holds back, and the next command or the person who must answer.

- The agent runs the named command for each open item, then asks again.
- It stops when `open` is empty for every environment the change reaches, and writes the pull request.
- An item it cannot close, such as what the blank Emails should become, goes into the pull request as a named question.
- The pull request quotes the confidence sentence the tool wrote, for example "Measured on Dev and QA; UAT not read; Prod assumed."
- It never claims a rung from the SQL text, and never retries a stop unchanged.

No rung proves reversibility, whether the running OutSystems application keeps working, or how long a rebuild takes at production size. Those stay as standing *Not verified* lines, as `prove-on-dacpac` already says.

## The verbs

Five verbs, each a few DacFx calls that v3's `io/DacFx.cs` already uses. Four only read; `prove` is the one write path, and only to a local server. Every verb loads the Octopus step's publish profile, writes one JSON object to stdout, and exits with a fixed code: 0 done, 1 bad arguments, 3 blocked, 4 unreachable, 5 differs, 6 configuration refused, 7 build failed, 9 refused by name.

| Verb | The question | What it calls | Exits |
| --- | --- | --- | --- |
| `doctor` | Can this machine do the work? | The .NET runtime, the DacFx pin against the Octopus step's, MSBuild, each SQLCMD variable and reference, the project's platform against each environment, LocalDB, and each environment's read grant | 0, or 4 or 6 naming each missing item and its fix |
| `drift --env dev` | Does the environment match the repository at the release it was deployed from? | Reads the release tag the deploy stamped in the database, or takes `--at <tag>` until the stamp exists; builds that tag, or takes the release's dacpac; `DacServices.Extract` of the environment into a package in memory; `DacServices.Script` of one package against the other, which connects to nothing | 0, or 5 listing each difference and each unmanaged object |
| `diff --from <ref> --to <ref>` | What does this branch change? | Builds both refs; `DacServices.Script` of one against the other; the report's operations, property by property | 0 |
| `predict --env dev,qa [--intent <op>]` | Will the change block on each environment, and why? | The branch merged onto main's head; `drift`'s packages per environment; `DacServices.Script` under the profile; the guards parsed with ScriptDom; each guard and precondition answered read-only | 0 applies, 3 blocks (with `blockedBy`), 5 the baseline drifted |
| `prove --from-env dev --rows <file>` | What does a real deploy do on rows shaped like the environment's? | The environment's package (for Prod, the dacpac of the release Prod runs, since Prod is never read) deployed to a fresh LocalDB database, the planted rows inserted, then the branch deployed under the profile with `BlockOnPossibleDataLoss` on, and DacFx's result read | 0 applies, 3 blocks (with `blockedBy`), 9 refused because the server is not local |

`predict` reads two kinds of check. The first is each guard DacFx writes into its script: an `IF EXISTS (…)` whose body is one `RAISERROR` at state 127, matched on the ScriptDom tree, never on the text. v3's spike S2 found DacFx 162.5.57 and 170.5.96 write only one form, row presence on a table, so each guard is answered from `sys.partitions` with `VIEW DEFINITION` alone, reading no rows. The same spike found three lookalikes the tree match must skip: a rebuild's copy step, the verification of a constraint added `WITH NOCHECK`, and the guard's text quoted in a comment.

The second kind is what fails at execution rather than at a guard: orphans for a new foreign key, violating rows for a new check, and duplicates for a new unique index. Each is an `EXISTS` query built from the plan's operation, with a `COUNT_BIG` only under `--counts`. These need `SELECT` on the table.

Against an environment, `predict` sends only DacFx's own catalog reads and those `SELECT` statements, under the caller's integrated login. That login holds `VIEW DEFINITION`, which Extract and Script require (`db_datareader` alone is refused), plus `SELECT` where a violation check needs it, so SQL Server itself refuses a write. DacFx also sends `EXEC xp_msver` during its reads, which the backport's R14 forbids; the risks section leaves that call to you.

One answer, abbreviated:

```json
{ "verb": "predict", "branch": "feature/email-required", "mergedOnto": "main@4f2c1a7",
  "stamp": { "dacfx": "162.5.57", "profile": "sha256:9b1e…", "profileVerified": false, "at": "2026-10-24T09:12:03Z" },
  "confidence": "Measured on Dev; QA not read; Prod assumed.",
  "environments": [
    { "env": "dev", "rung": 3, "outcome": "blocks", "blockedBy": "block-on-possible-data-loss",
      "guards": [ { "table": "dbo.Customer", "fires": true, "readFrom": "sys.partitions" } ],
      "plan": { "rebuilds": [], "drops": [], "unexplained": [] } },
    { "env": "qa", "rung": 1, "outcome": "unknown" },
    { "env": "prod", "rung": "assumed", "baseline": "the release Prod was deployed from, unread",
      "outcome": "blocks if dbo.Customer has rows",
      "beforePromoting": [ "SELECT CASE WHEN EXISTS (SELECT TOP 1 1 FROM [dbo].[Customer]) THEN 1 ELSE 0 END" ] } ],
  "open": [ { "env": "qa", "holds": 1, "next": "a lead: run predict --env qa and paste the answer" } ],
  "exit": 3 }
```

## The files

Sixteen code files under `tools/dbchange-lite/` in the SSDT repository, about 1,850 lines, so one person can read the whole tool in an afternoon. No file is over 200 lines. The risks section explains the 350 lines added to the first sketch.

| File | Its one job | Lines |
| --- | --- | ---: |
| `DbChangeLite.csproj` | Pins DacFx to the Octopus step's version, ScriptDom and Microsoft.Data.SqlClient, and nothing else | 25 |
| `Program.cs` | Reads the arguments, picks the verb, writes the summary first and the detail to a file, sets the exit code | 130 |
| `Answer.cs` | The JSON records, the stamp (commit, DacFx, profile hash, time) and the confidence sentence | 100 |
| `Environments.cs` | `dbchange/environments.json`: each environment's server, database, SQLCMD values and who reads it; refuses an inline credential | 90 |
| `Profile.cs` | Loads the Octopus step's `.publish.xml` through `DacProfile` | 60 |
| `Build.cs` | A ref, or a ref merged onto main, built into a dacpac through MSBuild; the release's own dacpac when given one | 140 |
| `Dac.cs` | `Extract`, `Script` of package against package, and the deploy report's XML read into operations and alerts | 180 |
| `Cache.cs` | Reuses a dacpac by commit, and an extract while the environment's schema fingerprint is unchanged | 50 |
| `Guards.cs` | Finds each row-presence guard on the ScriptDom tree and skips the three lookalikes | 140 |
| `Preconditions.cs` | The orphan, violating-row and duplicate check for each operation, existence first | 180 |
| `Triggers.cs` | What needs rung 4 or a person: unrecognised row-moving statements, rebuilds, `IDENTITY` changes, re-trusted keys, deploy scripts touching the plan's tables, CDC-tracked tables, contract changes for OutSystems consumers, operations the intent does not explain | 140 |
| `Measure.cs` | Confirms each statement is one `SELECT`, answers row presence from `sys.partitions`, runs under a lock timeout and a statement timeout | 100 |
| `Noise.cs` and `noise.json` | Known false-drift cases as data, each with the v3 decision line it came from | 70 |
| `Doctor.cs` | The machine check and its remedies | 110 |
| `Verbs.cs` | `drift` (with unmanaged objects), `diff` and `predict`, composed from the files above | 200 |
| `Prove.cs` | The one write path: LocalDB or `localhost` only, refusing every server `environments.json` lists | 130 |
| | **Total** | **1,845** |

Beside the code: `ops.json`, about 45 lines of data mapping each catalog operation to the plan operations it may produce, and an optional `Mcp.cs` of about 150 lines.

Tests sit beside it at about 800 lines, outside the budget, and a budget test fails the build above 2,200 lines of code. Their oracle already exists: v3's corpus, `tests/Golden/changes/`, holds 59 sample changes, each with the prediction it expects in `predict.txt`. The lite tool passes when it predicts the same outcome for each, on the same copies, so v3 is the acceptance test for the lite tool.

The cases move with the tool to the SSDT repository. Four new cases cover what the risks section adds: a rename already deployed, a CDC-tracked table, a seed that touches a changed table, and a profile with `GenerateSmartDefaults` on.

## What the skill files carry

The skills keep their reasoning and lose their mechanics. Each one swaps hand-run `sqlpackage` and `sqlcmd` steps for a verb, and reads the verb's `open` list to decide what comes next. The 45 operation skills under `skills/op/` keep their trigger phrases, their named trap and their verdict wording.

| Skill | What it keeps | The one change |
| --- | --- | --- |
| `confirm-intent` | Naming one operation from the developer's words | Passes the operation to `predict --intent`, which checks it against the plan |
| `classify-mechanism` | The provisional shipping shape and what the approving lead weighs | Ends by calling `predict`, rather than handing a change-spec to another agent |
| `prove-on-dacpac` | The proving loop, the deploy output as the signal, what a copy cannot prove | Drives `dbchange-lite prove` when `predict` lists a rung-4 item, writing the rows the copy needs from rung 3's findings |
| `talk-to-local-sql` | LocalDB setup and reset for the disposable copy | Drops its ad hoc row, NULL, duplicate and orphan probes, which `predict` now runs |
| `ask-the-developer` | The decisions only a person makes (what blank Emails become, which orphans to delete) | Reads them from `predict`'s `open` items |
| `author-pr` | The ten-section pull request description and its register | Fills the evidence sections from the JSON, quotes the tool's confidence sentence and stamp verbatim, and lists every planted row |
| `_index/multi-phase` | When old and new application code must coexist, and how many releases that takes | Reads the contract-changing operations `predict` lists for OutSystems consumers |
| `decompose`, `deploy-scripts`, `review/*`, the rest of `_index/*` | All of it | None |
| The entry skill (new, about 80 lines) | The confidence ladder, the stopping rule, and the worst-case rule for Prod | New |

The entry skill is the one new piece of text. It says: run `doctor`; name the operation; run `drift` and `predict` for each environment the developer can read, and ask a lead for QA and UAT; for Prod, choose the shape that holds when every table has rows and every constraint is violated; work the `open` list until it is empty or names a person; then write the pull request. v2's three-agent hand-off (intake, change-author, reviewer) becomes one authoring session and a separate review, as v3's design already chose.

## What it leaves to v3

The lite tool cuts everything that is not a read-only answer or a proof on a local copy. Each cut can return later as a port from v3, with a decision line in the SSDT repository naming the field finding that called for it.

- **Left to the skills:** the rows a disposable copy needs (`prove-on-dacpac`) and the pull request description (`author-pr`).
- **Left in v3 for now:** the synthetic copy and its planted rows, the CI gate, the after-deploy checks and the environments page, `profile`, v3's CDC ledger check, and v3's MCP server (the lite tool's own adapter is optional, about 150 lines).
- **Replaced by simpler means:** v3's statement allowlist and its monitoring of DacFx's own statements give way to a read-only login and a one-`SELECT` parse. Its law and manifest tests give way to the golden corpus and one budget test.
- **Deferred until a pull request needs them:** v3's fit probes for narrowing and retyping (`TRY_CONVERT` with a `DATALENGTH` test). DacFx's guard already blocks a narrowing on any populated table.

**The false-drift cases `noise.json` starts with**, each from v3's `DECISIONS.md`:

| Case | Why it shows as a difference | What the lite tool does |
| --- | --- | --- |
| A check written with `IN` | SQL Server stores it as `OR`, so DacFx drops and re-creates it on every plan | Names it as `check-written-with-in`, not drift, and the skill says to write the `OR` form |
| A clustered primary key moved to other columns | DacFx rebuilds the table, and the rebuild's drops and creates look like baseline drift | Counts them as the change's own |
| Query Store with no option declared | DacFx 162.5.57 plans it `OFF` and 170.5.96 plans it `READ_WRITE`, while SQL Server 2022 creates `READ_WRITE` | Pins DacFx 162.5.57; `doctor` refuses another version |
| A rename that differs only by case | Under a case-insensitive collation it can plan as nothing, or as a drop and create | Names it as `case-only-rename` for a person to confirm |

## Risks, weak spots and blind spots

The tool fails in five ways: it plans against the wrong thing, it misses what matters, it touches what it must not, the agent claims more than the tool showed, or the team stops trusting or maintaining it. The fixes below add about 350 lines, taking the tool to about 1,850. Most are small ports of v3 behaviour; the last paragraph says what to cut to hold 1,500.

### It plans against the wrong thing

| Risk | The fix | Lands in |
| --- | --- | --- |
| **The plan uses other options than the deploy.** The Octopus step's profile decides the plan: with `GenerateSmartDefaults`, a new NOT NULL column lands on a populated table instead of blocking. v3 found a hand-kept copy had drifted from the pipeline's. | Every verb loads the one `.publish.xml` the Octopus step applies, through `DacProfile`, and stamps its hash. Until S7's owner confirms the match, each answer says *profile not verified*. | `Profile.cs`; S7 |
| **DacFx differs from the deploy's.** Plans are version-bound: with no option declared, Query Store plans `OFF` on 162.5.57 and `READ_WRITE` on 170.5.96. | Pin the tool's DacFx to the Octopus step's. `doctor` refuses a mismatch, and a pin change is a pull request that runs the corpus under the new version. | `.csproj`, `Doctor.cs`; S7 |
| **The baseline is the wrong release.** `drift --at` is only as right as the tag someone types, and a rebuild may not match the dacpac Octopus deployed. | Each deploy writes its release tag into the database as an extended property: one post-deploy line and a SQLCMD value Octopus sets. `drift` reads the tag from the environment, and uses the release's own dacpac when given one. | post-deploy script, `Build.cs`; S7 |
| **Drift the deploy never sees.** With `DropObjectsNotInSource` off, an index added by hand in QA never appears in a plan. | `drift` also lists objects that exist only in the environment, marked *unmanaged*, as information rather than as a difference. | `Verbs.cs` |
| **Renames already deployed.** DacFx records each applied rename in the database's `dbo.__RefactorLog` table, which a plan against an extracted package may not read. Not checked. | Step 0 tests it on a column renamed in an earlier release. If the plan repeats the rename, `Dac.cs` extracts that table's rows with the package; v3 reads both refactorlogs itself (`io/ModelDiff.cs`). | Step 0, `Dac.cs` |
| **Variables, references and platform fail late.** A SQLCMD variable with no value for an environment, a missing reference dacpac, or a project platform newer than an environment's SQL Server. | `doctor` checks each variable has a value per environment, each reference resolves, and the project's platform against each environment's version (S8). | `Doctor.cs` |

### It misses what matters

| Risk | The fix | Lands in |
| --- | --- | --- |
| **An operation the tool does not recognise reads as "applies".** | Fail closed. Any plan statement that reads or moves rows and is not on a short metadata-only list goes to rung 4, as v3 routes an unclassified change to proof. Unknown never means applies. | `Triggers.cs` |
| **The data moves between prediction and deploy.** A table empty on Tuesday has rows on Thursday, and v2 found a blocked deploy stops part-way rather than rolling back. | Answers are timestamped and carry their queries. The same read-only checks run again on the release just before each promotion. | `Answer.cs`; decision |
| **The change ships with others.** A deploy past Dev is the cut, an Octopus release from main, so a plan of the branch alone misses what merged ahead of it. | `predict` plans the branch merged onto main's head. The promotion check plans the release's own tag. | `Build.cs`, `Verbs.cs` |
| **A CDC-tracked table.** A rebuild drops the table's capture instance, and a new column is never captured: the downstream consumer loses data with no error. | `predict` reads `sys.tables.is_tracked_by_cdc`, which `VIEW DEFINITION` allows, and makes any operation on such a table a rung-4 item and a question for the consumer's owner. | `Triggers.cs` |
| **The running OutSystems application.** A drop, rename, type change or tightened column breaks consumers until each environment's Integration Studio extension is refreshed and its consumers republished. No rung sees it. | Every contract-changing operation becomes an open item for a person, naming the entity and the order of deploy, refresh and publish. `multi-phase` decides when old and new code must coexist. | `Triggers.cs`, entry skill |
| **Deploy scripts that did not change.** A post-deploy seed `MERGE` runs on every deploy, and can fail on the new schema with its file untouched. | Rung 4 also triggers when a pre- or post-deploy script references a table the plan touches, read from the ScriptDom tree. | `Triggers.cs` |
| **Rung 4 is only as real as its rows.** The lite tool drops v3's planter, so the agent writes the rows the copy needs. | The rows come from rung 3's findings (a table with rows, an orphan, a duplicate). The pull request lists each planted row, so a reviewer sees what the proof stood on. | `prove-on-dacpac`, `author-pr` |

### It touches what it must not

| Risk | The fix | Lands in |
| --- | --- | --- |
| **Prod.** Nobody reads Prod, by your ruling of Oct 18, so rung 3 cannot exist there. | The agent ships the shape that holds in the worst case (every table populated, every constraint violated), as v3's row 22 does. Prod is marked *assumed*, and the answer prints the queries to run before promoting. | entry skill, `Answer.cs`; decision |
| **A proof deployed to a shared database.** `prove` is the one write path. | `prove` accepts only LocalDB or `localhost`, and refuses by name any server `environments.json` lists (exit 9), as the backport's R15 requires. | `Prove.cs` |
| **Heavy reads on QA or UAT.** An orphan count on a large table can run for minutes and hold shared locks. | Existence first: one `EXISTS` decides "blocks", and counts run only with `--counts`. Each query runs under `SET LOCK_TIMEOUT` and a statement timeout, and a timeout becomes an open item. Row presence comes from `sys.partitions`, reading no rows. | `Measure.cs` |
| **DacFx's own `EXEC` calls.** DacFx sends `EXEC xp_msver` during its reads; the backport's R14 forbids any `EXEC`, and v3 refuses that call before SqlClient sends it. | Your call: accept DacFx's read-only system calls and say so in the stamp, or port v3's refusal, about 40 lines. | decision |
| **Credentials and row values in output.** | Integrated authentication only; `environments.json` refuses an inline password (exit 6, R16). Answers carry booleans, counts and object names, never a row value (R14). | `Environments.cs`, `Measure.cs` |
| **Who may read QA and UAT.** | A developer's own login reads Dev. A lead runs `predict` on QA and UAT and pastes the answer into the pull request, which is the prediction of record until a service account exists (v3 §17 item 17). | entry skill; item 17 |

### The agent claims more than the tool showed

| Risk | The fix | Lands in |
| --- | --- | --- |
| **It stops early, or paraphrases its confidence.** | The tool writes the confidence sentence itself, the lowest rung per environment, and `author-pr` quotes it verbatim with the stamp: commit, DacFx version, profile hash, time. | `Answer.cs`, `author-pr` |
| **It names the wrong operation.** The developer says rename; the plan shows a drop and an add. | `ops.json` maps each catalog operation to the plan operations it may produce. `predict --intent <op>` turns any plan operation the intent does not explain into an open item. | `ops.json`, `Triggers.cs` |
| **A remedy that looks right and does not clear the block.** Backfilling every blank Email leaves a row-presence guard firing. | The stopping rule re-runs `predict` on the remedied branch, and a data-modifying pre-deploy script always needs rung 4. | entry skill |
| **Copilot in Visual Studio never loads the skills.** v2's bundle lists this as unverified. | The trial checks it first. Each verb's `--help` and refusals carry the stopping rule, so the tool steers without a skill. An MCP adapter of about 150 lines gives typed tool calls, and the team already runs MCP servers in Copilot. | trial, `Mcp.cs` (optional) |
| **Long answers swamp its context.** A drift on a large schema can list hundreds of objects. | Summary first (outcome, confidence, `open`); the full detail goes to a file whose path the answer names. | `Program.cs` |
| **Repeated runs are slow.** Each `predict` builds and extracts, and the agent calls it in a loop. | `Cache.cs` reuses a dacpac by commit, and an extract while the environment's schema fingerprint is unchanged. | `Cache.cs` |

### The team stops trusting it, or stops maintaining it

| Risk | The fix | Lands in |
| --- | --- | --- |
| **One wrong "applies" ends trust.** | The deploy keeps `BlockOnPossibleDataLoss` on, so a miss blocks rather than damages. Each miss becomes a corpus case and its fix in one pull request. | process |
| **It grows back.** The forces that took v2 to 118,686 lines act on any codebase agents maintain. | A test fails the build above 2,200 lines. A new feature's pull request names the field finding that needs it. No new documents. | budget test, `AGENTS.md` |
| **Skills cite flags the tool no longer has.** | A test reads every `dbchange-lite` command line in the skills and checks it against `--help --json`. | test |
| **Three tools at once.** The v2 zip, the lite tool and v3 would confuse a team this week's handoff already confused. | The lite tool replaces the zip's read side, and the zip leaves the SSDT repository once `drift` and `predict` pass on the real project. v3 is never handed to the team; it stays the specification and the corpus. | decision |
| **A switch mid-sprint.** | Run it beside the current setup on one lead's machine, where a disagreement is a finding for both (v3 §15). Switch at a sprint boundary. | rollout |
| **Only you can fix it.** | Each file's header names the DacFx behaviour it relies on and the finding that proved it. A red corpus test names its case. A second maintainer reviews every change. | decision |
| **It is built on your evenings.** | Agent sessions write Steps 1 to 4 from this doc against the golden corpus, as v3 was built. Your time goes to Step 0, the decisions and the trial. | order of work |

**To hold 1,500 lines,** cut in this order: `Cache.cs` (repeated runs get slower), the unmanaged-objects list, the release-dacpac option, then `doctor`'s reference and platform checks. That reaches about 1,700. Below that the cuts are `Profile.cs`, `Triggers.cs` and `prove`'s refusal, and those are what stop a wrong "applies" and a write to a shared database. Keep them.

## Order of work, and what is yours to decide

Step 0 comes first, because the riskiest unknowns sit in the real SSDT project rather than in the code: its build, its size, its profile and its past renames. It needs a work laptop and Dev, and no code. Steps 1 to 4 need neither: agent sessions can write them from this doc against v3's golden project on LocalDB or SQL Server Express.

**Step 0, the spike, by hand, about an hour.** Build the SSDT project at Dev's deployed tag with MSBuild. Run `sqlpackage /Action:Extract` on Dev into a dacpac, and time it. Run `sqlpackage /Action:DeployReport` of the first dacpac against the second, with the Octopus step's profile. Zero operations means the design holds on the real project; each operation is either real drift or a new `noise.json` row. Repeat with a branch that touches a column renamed in an earlier release. The same run answers v3's milestone-1 laptop check.

1. `Build.cs`, `Profile.cs`, `Dac.cs` and `drift`, against a copy of the golden project.
2. `Guards.cs`, tested on the DacFx scripts in v3's `tests/Golden/data-loss-checks/`, with no SQL Server.
3. `Preconditions.cs`, `Triggers.cs`, `Measure.cs` and `predict`, checked against each sample change's `predict.txt` and the four new cases.
4. `Prove.cs`, `Doctor.cs`, `noise.json`, and the JSON contract in `Program.cs`.
5. On a work laptop: `doctor`, then `drift --env dev` and `predict --env dev` on the real project, beside the current setup.
6. The entry skill and the skill changes above; then one real change taken end to end with one lead, at a sprint boundary.

**Decisions only you can make:**

- [ ] **Where it is built, and where it lives.** Recommended: built where the agent sessions, the corpus and the SQL Server Express CI already are, in a private repository (this one is public), then moved into the SSDT repository once it passes the corpus. From that commit the SSDT repository is its only home, and v3 stays the specification.
- [ ] **Prod's check before promoting.** Prod stays unread under your Oct 18 ruling. Recommended: the Octopus process runs the printed queries just before it deploys, under the identity that already deploys; otherwise a person with Prod rights runs them.
- [ ] **DacFx's `EXEC xp_msver` under R14.** Accept DacFx's read-only system calls and record them in the stamp, or port v3's refusal.
- [ ] **The release-tag stamp:** one post-deploy line and an Octopus SQLCMD value, agreed with the deployment step's owner (S7).
- [ ] **The MCP adapter.** Recommended: after the trial, since the CLI is needed either way and the trial shows whether Copilot reads terminal output well.
- [ ] **Whether v3 keeps building** toward proving, the gate and the agent instructions. Continuing costs your review time; pausing keeps its corpus and findings as the lite tool's specification.
- [ ] **QA and UAT reads.** Leads paste `predict`'s answer until a service account holds the read groups (v3 §17 item 17).
- [ ] **A second maintainer** who reviews every change to `tools/dbchange-lite/`, so the team maintains the tool rather than one person.
