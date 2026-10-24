# dbchange-lite: the SSDT slice, about three thousand lines over DacFx

2026-10-22 · Daniel Dyer · revised 2026-10-24 to the form a build executes, and to what the build measured

dbchange-lite is a C# command-line tool over DacFx for the SSDT repository that carries an OutSystems system's external entities. It answers three read-only questions: does the environment hold what the repository says it holds, what does this branch change, and will the change block when the deployment step deploys it. Where reading cannot settle the third question, it deploys the change to a disposable copy on a local SQL Server, never to a shared database.

Each answer is one JSON object. It names what was measured, what was assumed, and every item still open, each with the command that closes it or the person who must answer it. The skill files carry the reasoning; the tool does only what an agent cannot do by reading SQL: build a dacpac, ask DacFx for the deploy plan, read row presence and rule violations, and deploy to a local copy. The agent's job is to drive every open item to closed, or to a named person.

v3 on `claude/v3-architecture-design-sl95mx` is the specification. Every term here is v3's term for the same thing: a BlockOnPossibleDataLoss check, a precondition, an outcome, a finding, the exit table. The lite tool ports a v3 behaviour when a false answer in the field shows the need, the way v3 ports from v2. It is built in this repository under `tools/dbchange-lite/`, against v3's corpus, and moves to the SSDT repository once the corpus passes; from that commit the SSDT repository is its only home.

This document is the specification the build executes: §3 to §8 are the contracts, §9 and §10 the files and their tests, §13 the risks with the test that holds each fix, §14 the work packages and what landed. A sentence here that the code contradicts is a defect in one of them, settled by a test.

## 1. The confidence levels

Full confidence means three things. Dev, and every environment a lead reads, stand at level 3 or higher. Prod, which nobody reads (`V3_MILESTONES.md` §17 item 11, settled 2026-10-18), ships in the shape that holds under the worst case and is marked *assumed*. And level 4 is reached wherever the plan holds a statement no read-only query settles.

Each level is settled by evidence DacFx or SQL Server produced, never by reading the change's SQL. This keeps v2's rule that proving is classifying, with a read-only level added below the deploy.

| Level | Name | What is settled | The evidence | Who produces it |
| --- | --- | --- | --- | --- |
| 0 | Intent | The operation is named from the catalog, the developer confirmed it, and the plan's operations are the ones the operation can produce | The operation's name, the developer's yes, and `predict --intent` listing no unexplained operation. `--intent` compares deploy-report kinds, so it catches a rename planned as a drop and a create, or a rebuild, and passes a wrong operation that plans the same `Alter SqlTable` (`make-mandatory` and `widen` both do) | `confirm-intent`, `classify-mechanism`, `predict` |
| 1 | In sync | The environment holds what the repository holds at the release it was deployed from | `drift` answers `in-sync`, or `differs` with every difference carrying a known cause | `dbchange-lite drift` |
| 2 | Planned | What DacFx will do under the deployment step's publish profile: each operation, each rebuild, each drop, and each BlockOnPossibleDataLoss check | The deploy report and script DacFx writes for the change against the environment's extracted package | `dbchange-lite predict` |
| 3 | Measured | Whether each check fires and each precondition holds, per environment | Row presence per check and a violation query per precondition, each run read-only, each with its query and its answer | `dbchange-lite predict` |
| 4 | Proven | The verdict a real deploy gives on rows shaped like the environment's | The deploy under the profile with `BlockOnPossibleDataLoss` on, to a disposable local copy, and DacFx's result | `dbchange-lite prove`, driven by `prove-on-dacpac` |

Level 4 is required only when `predict` lists an item no read-only query settles (§7.4): a statement outside the closed list of statements that move no rows, a table rebuild, an `IDENTITY` change, a constraint re-validated `WITH CHECK CHECK CONSTRAINT`, a pre- or post-deploy statement that writes a table the plan alters, or a table the CDC ledger names. Everywhere else level 3 predicts the outcome exactly, because it answers the predicate DacFx itself wrote: the check for `make-mandatory` is `IF EXISTS (SELECT TOP 1 1 FROM [dbo].[Customer])`, row presence on the table, and the tool runs that predicate.

**The agent's stopping rule.** Every answer carries an `open` list. Each item names the environment, the level it holds back, why, and what closes it: a command, or a person and the question.

- The agent runs each open item's command, then runs `predict` again.
- It stops when `open` is empty for every environment the change reaches, and writes the pull request.
- An item only a person can close (what the blank Emails become, which orphan rows to delete) goes into the pull request as a question, with the person's role.
- The pull request quotes the `confidence` sentence the tool wrote, with the provenance (the commit, the DacFx release, the profile's hash, the time).
- It never states a level the tool did not reach, and never runs a stopped command unchanged.

**The confidence sentence** is written by the tool, one clause per environment asked for, in deploy order (`deployOrder`, then the file's order), joined by semicolons: `Proven on <env>` at level 4, `Measured on <env>` at level 3, `Planned on <env>` at level 2 with an item open, `<env> not read` where the tool did not connect, and `<env> assumed` for an environment the file marks as never read. Example, for `--env dev,qa,uat,prod`: `Measured on dev; qa not read; uat not read; prod assumed.`

No level proves reversibility, whether the running OutSystems application keeps working, or how long a rebuild takes at production size. Those stay standing *Not verified* lines in the pull request, as `prove-on-dacpac` writes them today.

## 2. One change, front to back

A developer asks for Customer.Email to become mandatory. `confirm-intent` names the operation `make-mandatory` and the developer confirms. The developer's branch edits `Modules/Customer.sql` from `NULL` to `NOT NULL` and commits.

1. `dbchange-lite doctor` answers `ready`, exit 0: the .NET SDK, the DacFx release at the ledger's pin, the build targets, the environments file with its profile, and a connection to Dev as the developer's own login.
2. `dbchange-lite drift --env dev --at ref:v2026.10.3` answers `in-sync`, exit 0: Dev holds what the repository held at the release tag Dev was deployed from.
3. `dbchange-lite predict --env dev,qa,prod --intent make-mandatory` builds the branch merged onto `origin/main`, plans it against Dev's extracted package under the pipeline's profile, finds one check (`[dbo].[Customer]`, row presence) and one precondition (`NotNull` on `[dbo].[Customer] ([Email])`), runs both read-only on Dev, cannot connect to QA as the developer, plans Prod against `origin/main`'s package as the worst case, and answers `blocked`, exit 3, with `confidence` `Measured on dev; qa not read; prod assumed.`, `blockedBy` `block-on-possible-data-loss`, and two open items: QA, level 1, `a lead who reads qa runs dbchange-lite predict --env qa --to ref:HEAD --onto ref:origin/main --intent make-mandatory and pastes the answer`; and Dev, level 3, `the developer: what becomes of the rows of [dbo].[Customer] that violate NotNull ([Email])?`.
4. The agent asks the developer the one question and reads `make-mandatory`'s shape for a populated table: two releases, the first with a default or a backfill. The developer adds a pre-deploy `UPDATE` for the blank rows. `predict` on the new commit lists the `UPDATE` as a statement that writes `[dbo].[Customer]`, which the plan alters, so level 4 is required: `open` holds `run dbchange-lite prove --env dev --to ref:<the commit> --rows <file>`.
5. `prove-on-dacpac` writes the rows the copy needs from level 3's answer (Customer has rows; two blank Emails) and runs `prove`. The tool extracts Dev's schema, creates `dbchange_lite_20261024091203_4812` on the local server with Dev's collation, deploys the extracted package, runs the rows file, measures the copy and finds it answers every check and precondition as Dev did, deploys the branch under the profile, and answers `blocked`, exit 3: `BlockOnPossibleDataLoss` stopped the publish because `[dbo].[Customer]` has rows, and the message names the table. The remedy is `make-mandatory`'s, which the skill already states: the two-release shape.
6. The developer splits the change. `predict` on the first release answers `done`, exit 0 for Dev; a lead pastes QA's answer, also `done`; UAT is not read until a lead runs it; Prod is assumed, and the answer prints the one query to run before promoting: `SELECT CASE WHEN EXISTS (SELECT TOP 1 1 FROM [dbo].[Customer]) THEN 1 ELSE 0 END`.
7. `author-pr` fills the evidence sections from the JSON, quotes the confidence sentence and the provenance, lists the planted rows, and writes the question the lead must answer for UAT. The deployment step deploys; the agent did not.

## 3. The verbs

Five verbs, each a few DacFx calls that v3's `io/DacFx.cs` already makes. Four only read. `prove` is the one write path, and only to a local server. Every verb loads the publish profile the environments file names, writes one JSON object to standard output, and exits with a code from the table below.

```
dbchange-lite doctor
dbchange-lite drift   --env <name> --at <source>
dbchange-lite diff    --from <source> --to <source>
dbchange-lite predict --env <name>[,<name>…] [--to <source>] [--onto <ref>|--onto none] [--intent <operation>] [--at <source>] [--counts]
dbchange-lite prove   --env <name> --rows <file> [--to <source>] [--onto <ref>|--onto none] [--at <source>]
dbchange-lite --help [--json]      dbchange-lite --version
```

A `<source>` is `ref:<git ref>`, a commit of the SSDT repository built in a worktree of its own; `dacpac:<path>`, a package already built, such as the release the deployment step deployed; or `project:<path>`, a folder holding one `.sqlproj`, built as it stands. A relative path resolves from the folder the verb runs in. `--to` defaults to `ref:HEAD`, the last commit: an uncommitted edit is not in it, and the answer warns `source.uncommitted` with the count of changed files. `predict` plans `--to` merged onto `--onto`, which defaults to `ref:origin/main`, because a deploy past Dev is a release cut from main; `--onto none` plans the source alone. A merge that conflicts is `needs-input`, exit 8, `merge.conflict`, naming each conflicting file; a merge git refuses for another reason (unrelated histories) is exit 8, `merge.failed`, quoting git. `--at` names the package an environment marked `neverRead` is planned against (`--onto` when absent); `prove` takes the same `--onto` as `predict`, so a proof proves the plan `predict` read.

| Verb | The question | What it does | Outcomes |
| --- | --- | --- | --- |
| `doctor` | Can this machine do the work? | The .NET SDK; the DacFx release against the ledger's pin; the build targets in the tool folder; the environments file, its profile and each environment's SQLCMD values; each project reference; the project's platform against each environment's version; the local server; a connection to each environment as this login, and a note for an environment whose connection reference this machine does not hold, since a person who holds it reads that environment | `ready` 0; `degraded` 6 naming each missing item and its remedy; `unreachable` 4 |
| `drift` | Does the environment hold what the repository held at the release it was deployed from? | Builds `--at`, or takes its dacpac; `DacServices.Extract` of the environment into a package in memory; `DacServices.Script` of the package against the extract, package to package, which connects to nothing; each operation of the report is a difference, with its known cause where `causes.json` names one; each object only the environment holds is listed as *unmanaged* | `in-sync` 0; `differs` 5 |
| `diff` | What does this branch change? | Builds both sources; `DacServices.Script` of `--to` against `--from`; the report's operations, one line each as `change.txt` of the corpus writes them | `done` 0 |
| `predict` | Will the change block on each environment, and why? | For each environment: the extract, the plan of `--to` merged onto `--onto` against it under the profile, the checks read from the script, the preconditions read from the plan, each answered read-only; the statements no read-only query settles; the operations `--intent` does not explain; Prod's worst case | `done` 0; `blocked` 3; `needs-input` 8; `unreachable` 4 when no environment answered |
| `prove` | What does a real deploy do on rows shaped like the environment's? | The environment's extract (`--at dacpac:` for Prod, which is never read) deployed to a fresh copy on the local server with the environment's collation; the rows file run; the copy measured as `predict` measures and compared with the environment's answers; the change deployed under the profile with `BlockOnPossibleDataLoss` on; DacFx's result read; the copy dropped | `done` 0; `blocked` 3; `needs-input` 8 when the copy's rows answer otherwise than the environment's; `refused-by-name` 9 |

**The exit table** is v3's `cli/exits.frozen`, byte for byte, so a skill's reading of an exit holds for both tools: 0 `done`, 1 `bad-arguments`, 2 `unparsed-input`, 3 `blocked`, 4 `unreachable`, 5 `differs`, 6 `configuration-refused`, 7 `build-failed`, 8 `needs-input`, 9 `refused-by-name`, 130 `interrupted`. Each exit's meaning and remedy are v3's sentences from `cli/Contract.cs`; `--help` prints them and `--help --json` carries them.

**One exit for several environments.** `predict` exits 1, 6 or 7 before any environment is read: a bad argument (an `--env` naming nothing, an `--intent` `operations.json` does not name, a `neverRead` environment with neither `--at` nor `--onto`), a refused configuration (a literal credential or a key of the wrong kind in the environments file, a DacFx release outside the pin's window, a profile that does not load), a failed build. Then, over the environments asked for: 3 when any environment blocks; else 4 when no environment answered and each is an open item; else 8 when `open` holds an item; else 0. An environment the tool could not reach, whose connection reference this machine does not hold or does not parse, or whose catalog read timed out behind a schema change's lock is a row at level 0 with `notRead` and an open item at level 1, so one such environment beside a measured one is exit 8 with the measured answer in full.

**What reaches a server.** `drift`, `diff` and `predict` send DacFx's own catalog reads, the catalog reads of §7.2 and §7.3, and the queries of §7.3, every one of them a `SELECT` whose result is a count, a 0 or 1, or a catalog row, never a row of a user table. DacFx 170.5.96 also sends `exec xp_msver` on each new connection for telemetry the tool turns off; the tool refuses that call before SqlClient sends it, as v3's `io/ServerProbe.cs` does, so R14 of `LIFECYCLE_BACKPORT_PROMPT.md` holds verbatim: never a write, DDL, `EXEC`, or a `SELECT` that returns row values. The queries log of each run (§4) records every statement the tool composes; DacFx's own catalog reads reach the server under the Application Name `dbchange-lite`, and the server's trace is their record.

## 4. The answer

One JSON object per run, the fields of v3's envelope (`cli/schemas/dbchange.envelope.1.schema.json`) first, then the lite tool's own. The summary is written to standard output; when the answer would exceed 400 lines, the full answer goes to the run folder, `truncated` is true with `full` naming the file, and the summary keeps every check, precondition, open item and finding and drops the plan's operations list, so the evidence never leaves the summary.

```json
{ "schema": "dbchange-lite.predict/1", "outcome": "blocked", "exit": 3,
  "message": "dev blocks: BlockOnPossibleDataLoss stops the publish because [dbo].[Customer] has rows.",
  "blockedBy": "block-on-possible-data-loss",
  "findings": [ { "code": "check.fires", "severity": "error", "subject": "[dbo].[Customer]",
                  "message": "On dev the table has rows, so the BlockOnPossibleDataLoss check DacFx writes for it stops the publish.",
                  "remedy": "Ship make-mandatory in two releases, as its skill states." } ],
  "version": "0.1.0", "dacfx": "170.5.96", "pin": "UNPINNED",
  "provenance": { "change": "a1b2c3d…", "onto": "4f2c1a7…", "merged": "tree:9e0f…",
                  "publishProfile": "sha256:9b1e…", "profileVerified": false, "at": "2026-10-24T09:12:03Z" },
  "confidence": "Measured on dev; qa not read; prod assumed.",
  "environments": [
    { "env": "dev", "level": 3, "outcome": "blocks", "blockedBy": "block-on-possible-data-loss",
      "baseline": "extract sha256:5d1c…", "collation": "SQL_Latin1_General_CP1_CI_AS",
      "checks": [ { "table": "[dbo].[Customer]", "fires": true, "readBy": "exists",
                    "query": "SELECT CASE WHEN EXISTS (SELECT TOP 1 1 FROM [dbo].[Customer]) THEN 1 ELSE 0 END" } ],
      "preconditions": [ { "kind": "NotNull", "table": "[dbo].[Customer]", "columns": ["Email"], "violated": true, "count": null,
                           "query": "SELECT CASE WHEN EXISTS (SELECT 1 FROM [dbo].[Customer] WHERE [Email] IS NULL) THEN 1 ELSE 0 END" } ],
      "plan": { "operations": [ { "kind": "Alter", "type": "SqlTable", "name": "[dbo].[Customer]" } ],
                "rebuilds": [], "drops": [], "unexplained": [] },
      "notPredictable": [] },
    { "env": "qa", "level": 0, "outcome": "unknown", "notRead": "SQL Server did not answer for qa: Msg 18456." },
    { "env": "prod", "level": null, "assumed": "worst", "outcome": "blocks if [dbo].[Customer] has rows", "baseline": "ref:origin/main",
      "beforePromoting": [ "SELECT CASE WHEN EXISTS (SELECT TOP 1 1 FROM [dbo].[Customer]) THEN 1 ELSE 0 END" ] } ],
  "open": [
    { "env": "qa", "level": 1, "why": "not read: SQL Server did not answer for qa: Msg 18456.",
      "next": { "command": "dbchange-lite predict --env qa --to ref:HEAD --onto ref:origin/main --intent make-mandatory", "by": "a lead who reads qa" } },
    { "env": "dev", "level": 3, "why": "NotNull on [dbo].[Customer] ([Email]) holds violating rows",
      "next": { "person": "the developer", "question": "What becomes of the rows of [dbo].[Customer] that violate NotNull ([Email])?" } } ],
  "truncated": false, "full": null, "log": ".dbchange/runs/20261024T091203Z-4812-predict/queries.log", "elapsedMs": 8412 }
```

- `outcome` and `exit` pair as v3's envelope pairs them. `blockedBy` is `block-on-possible-data-loss`, `constraint-violation` or null, and is set exactly when `exit` is 3.
- A `finding` is v3's: `code` (two or more dot-separated kebab-case words), `severity` (`error`, `warning`, `note`), `subject`, `message`, and `remedy`, one imperative sentence, required on an error.
- `provenance` names the commit of `--to` (`change`), of `--onto` (`onto`) and the merge's tree (`merged`), the publish profile's SHA-256 over its canonical text (BOM dropped, line ends LF, UTF-8), whether the deployment step's owner confirmed that profile, and the time in UTC.
- An environment carries its `level` (0 to 4, 0 when it was not read), its `outcome` (`applies`, `blocks`, or `unknown` when it was not read or nothing fired and an item went unanswered), `blockedBy`, what the plan was read against (`baseline`: `extract <schema fingerprint>` for an environment `predict` read, the `--at` source for `drift` and for a `neverRead` environment, the copy's name for `prove`), its collation, each check with how it was read (`exists`, or `sys.partitions` when the login lacks `SELECT`) and, under `--counts`, its row count, each precondition with `violated` and, under `--counts`, `count`, the plan's operations, rebuilds and drops, the operations the change does not explain (`unexplained`, each with its `cause` where one is known), and `notPredictable`, each item with its `why` and the statement.
- An environment the file marks `neverRead` carries `level` null, `assumed` `worst`, an `outcome` sentence, and `beforePromoting`, the queries a person runs there before promoting.
- An `open` item names the environment, the level it holds back, `why`, and `next`: a `command` and who runs it (`by`), or a `person` and the `question`.
- `log` names the run's queries log: one line per statement the tool sends, with the server, the database, the statement, the elapsed milliseconds and whether it answered, failed or timed out; a rows-file batch is logged by its number and length, never its text.

The run folder is `.dbchange/runs/<UTC stamp>-<process id>-<verb>/` under the SSDT repository root, so two runs started in the same second write apart, the folder v3 uses, holding `answer.json`, `queries.log`, the deploy report and script of each environment, and the worktree while a build runs. `.dbchange/` is git-ignored by the vendoring pull request.

## 5. The data files

The SSDT repository owns these files, as `V3_MILESTONES.md` §15 item 5 says. The lite tool reads them and writes none; a change to one is a pull request.

**`dbchange/environments.json`**, the file v3 defines (`kernel/Environments.cs`): each environment by name with its `host`, its `connection` as a reference (`env:NAME` or `file:path`, never a literal), its publish `profile` path from the repository root, its `sqlcmd` values, and `readerGroups`. The lite tool reads two more keys and ignores the rest: `neverRead: true` on Prod, so `predict` assumes the worst case and prints the queries to run before promoting; and `deployOrder`, a whole number, so the confidence sentence lists environments in deploy order. An environment that is not an object, a `neverRead` that is not `true` or `false`, or a `deployOrder` that is not a whole number is `configuration-refused`, exit 6, naming the key. A literal connection string or a `Password=` anywhere in the file is `configuration-refused`, exit 6, quoting nothing (R16). The host of each environment is what `prove` refuses to write to, by spelling and by resolved address (R15).

**`dbchange/ledgers/toolchain.md`**, v3's ledger: the DacFx release the deployment step runs, pinned, or `UNPINNED` until whoever owns the deployment step names it (spike S7). The tool runs its own DacFx only at the pin or the release before it, and while the row reads `UNPINNED` every answer's `pin` says so. The ledger's second table, `profile`, records the publish profile's path, its hash and who confirmed it matches the deployment step's, with the date; until a row exists, `profileVerified` is false in every answer.

**The publish profile**, `dbchange/profiles/pipeline.publish.xml` or the path the environment names, loaded through `DacProfile.Load`, its connection string removed first. Every plan runs under its options. `BlockOnPossibleDataLoss` is forced on for `prove` whatever the file says (`VALUES.md` S1). `GenerateSmartDefaults`, `DropObjectsNotInSource`, `IncludeTransactionalScripts` and the rest take the file's values, because the plan the tool predicts is the plan the deployment step runs.

**`dbchange/ledgers/cdc-tracked.md`**, v3's ledger of the tables Change Data Capture captures per environment. A plan that alters, rebuilds or drops a table the ledger names, or changes its column list, is not predictable read-only (§7.4). Where the login holds `VIEW DEFINITION`, `predict` also reads `sys.tables.is_tracked_by_cdc` and notes a table the ledger misses.

**`operations.json`**, in the tool folder: for each of the operation catalog's names (`V3_ARCHITECTURE.md` Appendix C), the deploy report operations it may produce, as pairs of the report's `Operation` name (`Create`, `Alter`, `Drop`, `Rename`, `TableRebuild`) and `Item` type (`SqlTable`, `SqlSimpleColumn`, `SqlForeignKeyConstraint`, `SqlCheckConstraint`, `SqlDefaultConstraint`, `SqlIndex`, `SqlPrimaryKeyConstraint`, `SqlUniqueConstraint`, and the rest DacFx writes). `predict --intent <operation>` lists every plan operation outside the named operation's pairs as `unexplained`, an open item for the developer: the developer said rename, and the plan shows a drop and a create.

**`causes.json`**, in the tool folder: the known causes of a difference `drift` reports and of an operation `predict` cannot explain, each with the v3 decision it came from and what to do.

| Cause | Why it shows | What the tool does |
| --- | --- | --- |
| `check-written-with-in` | SQL Server stores a `CHECK (x IN (…))` as a chain of `OR`s, so DacFx drops and re-creates the check on every plan (`DECISIONS.md` 2026-10-19) | Reports the difference with this cause and the remedy: write the check in the `OR` form SQL Server stores. The difference stands; `drift` stays strict |
| `clustered-key-rebuild` | A clustered primary key moved to other columns makes DacFx rebuild the table, and the rebuild's `tmp_ms_xx_` statements name objects the change did not | Counts those statements as the change's own, since they name the table the change alters |
| `query-store-option` | A model that declares no Query Store option plans `OFF` on DacFx 162.5.57 and `READ_WRITE` on 170.5.96 (`DECISIONS.md` 2026-10-19) | `doctor` refuses a DacFx release outside the pin's window, so the plan runs on the deployment step's release and this difference cannot arise; the cause stays as the record |
| `case-only-rename` | Under a case-insensitive collation a rename that differs only by case plans as nothing, or as a drop and a create | Reports the difference with this cause as an open item for a person |

## 6. The data a check and a precondition rest on

A **BlockOnPossibleDataLoss check** is a statement DacFx writes into its deploy script when `BlockOnPossibleDataLoss` is on and an operation could lose rows: `IF EXISTS (SELECT TOP 1 1 FROM [schema].[table]) RAISERROR (…, 16, 127)`. Spike S2 measured, on DacFx 162.5.57 and 170.5.96 alike, that DacFx writes one form only, row presence on a table, one check per table, and three lookalikes a text match would take and a parse-tree match must skip: the copy step of a table rebuild, `IF EXISTS (SELECT TOP 1 1 FROM [T]) BEGIN … INSERT … END`; the validation of a constraint added `WITH NOCHECK`, `RAISERROR (…, 16, 127)` under `IF @@ROWCOUNT > 0`; and the check's text quoted in the pre-deploy script's block comment. The tool reads the script with ScriptDom and takes an `IF` whose condition is `EXISTS` over `SELECT TOP 1 1 FROM` one table, whose then-branch is one `RAISERROR` at state 127, and which has no `ELSE`. Before the parse, the script's SQLCMD lines (`:setvar`, `:on error`, `:r`, `GO`) are dropped and each `$(name)` is substituted from the script's own `:setvar` lines and then from the environment's SQLCMD values, in memory; the substituted text is never written.

A **precondition** is what SQL Server itself validates against existing rows when a statement of the plan runs, in v3's kinds and v3's spelling, read from the plan's statements and the environment's model:

| Kind | From the statement | The query, answered 0 or 1, counted under `--counts` |
| --- | --- | --- |
| `ForeignKey` on the child table (its columns) | `ALTER TABLE … ADD [CONSTRAINT …] FOREIGN KEY (…) REFERENCES …`, named or unnamed, `WITH CHECK` or `WITH NOCHECK`, since DacFx validates an unnamed one afterwards through `sp_executesql` | A child row whose key columns are all non-null and match no parent row |
| `Check` on the table (its columns) | `ALTER TABLE … ADD [CONSTRAINT …] CHECK (expression)` | A row for which the expression is false; a row for which it is unknown passes, as SQL Server passes it |
| `Unique` on the table (its columns) | `ADD CONSTRAINT … UNIQUE`, `ADD CONSTRAINT … PRIMARY KEY`, `CREATE UNIQUE INDEX … [WHERE filter]` | Two rows with the same key values, the filter applied; several nulls are duplicates, as a unique index treats them |
| `NotNull` on the table (the column) | `ALTER TABLE … ALTER COLUMN … NOT NULL` where the environment's model holds the column nullable | A row where the column is null |
| `TableEmpty` on the table | Each BlockOnPossibleDataLoss check | Row presence: DacFx's own predicate, `SELECT TOP 1 1 FROM` the table, as `EXISTS` |

A precondition's columns are spelled in name order (ordinal string comparison), as v3 spells them (`Unique on [dbo].[OrderLine] ([LineNumber], [OrderId])`), whatever order the key or the table declares. A precondition on a table the plan creates is answered clean without a query, since the environment holds no rows for it. One on an existing table that reads a column the plan adds is answered by no query, since the column does not exist before the deploy: it is an open item at level 4 for `prove` (a check over a new and an existing column, or a unique key on a new nullable column of a table with two rows, fails at the deploy). A foreign key whose key holds a new nullable column without a default stays clean without a query, since SQL Server checks no row whose key holds a null. The constraints a rebuild declares on its `tmp_ms_xx_` copy, and the columns it declares `NOT NULL` where the environment holds them nullable, are preconditions on the original table, whose rows the copy step moves. `Fits`, whether every value of a narrowed or retyped column fits its new type, is deferred (§12): on a populated table DacFx's own check blocks the narrowing before any value is read, and on an empty table nothing can fail to fit. Every query is composed from identifiers and expressions ScriptDom parsed from the plan and rendered again, then parsed once more before it is sent, so a query the tool did not compose is never sent.

**How each is read.** Row presence runs as DacFx's own predicate when the login holds `SELECT` on the table; when SQL Server refuses it (Msg 229), the tool reads `SUM(rows)` over `sys.partitions` for the table's heap or clustered index, which `VIEW DEFINITION` alone allows, and marks the check `readBy: sys.partitions`, since that count is approximate. Each precondition query needs `SELECT` on its tables; a refusal is an open item for a DBA quoting SQL Server's message, which names the object refused (the parent table of a foreign key among them). Each statement runs under `SET LOCK_TIMEOUT 5000` and a command timeout of 30 seconds; a timeout is an open item, never a guess: a lock wait (Msg 1222) is run again when no session holds the lock, and a query past 30 seconds goes to a DBA. A catalog read that times out leaves the environment not read. Existence decides first; a count runs only under `--counts`, which counts every row of each table it reads, so a shared environment is never scanned to answer a yes or no.

## 7. How `predict` decides

### 7.1 Per environment

1. The environment's package: `DacServices.Extract` of the database into memory, reused while its schema fingerprint (SHA-256 over `object_id`, `type` and `modify_date` of `sys.objects`, user objects only, and every row of `sys.extended_properties`) is unchanged.
2. The change's package: `--to` merged onto `--onto` in a worktree under the run folder, built with `dotnet build` and the tool folder's DacFx targets (the route `io/Ssdt.cs` settled with spike S1), reused by the merge's tree hash.
3. The plan: `DacServices.Script` of the change's package against the environment's package under the profile, with its deploy report and script.
4. The checks (§6) from the script, the preconditions (§6) from the statements against the environment's model, the statements no read-only query settles (§7.4), and, under `--intent`, the operations the intent does not explain.
5. Each check and precondition answered read-only; the level set: 2 when a query was refused, timed out or reads a column the plan adds, 3 when every one answered.
6. The outcome: `blocks` when any check fires or any precondition is violated, with `blockedBy` `block-on-possible-data-loss` when a check fires, else `constraint-violation`; `unknown` when an item went unanswered; `applies` otherwise.

### 7.2 Drift the change does not explain

The plan against an environment holds every difference between the change's package and the environment, so an operation the change itself did not make is drift. An operation is the change's own when its object is one `diff` of `--to` against `--onto` names, a `tmp_ms_xx_` object of a rebuild on such a table, or `[dbo].[__RefactorLog]`. Every other operation is `unexplained`, listed with its known cause from `causes.json` where one applies, and an open item for a person otherwise: a lead confirms it is Dev's drift and runs `drift`, or the change is planned again after the environment is corrected.

### 7.3 Prod, never read

An environment the file marks `neverRead` is planned against the package of the release it was deployed from (`--at dacpac:` or the release tag), never extracted. Every check is taken to fire and every precondition to be violated, which `V3_MILESTONES.md` §4 row 22 settled as the honest answer for an unread environment. The environment's row carries `assumed: worst`, the outcome sentence that follows, and `beforePromoting`: the row-presence query of each check and the violation query of each precondition, for whoever runs them under an identity with Prod rights, by the decision in §15.

### 7.4 What no read-only query settles

The plan's statements are read with ScriptDom, pre-deploy script, DacFx's own statements and post-deploy script in order, inside each `IF`, `BEGIN … END` and `TRY … CATCH`. The tool holds a closed list of statement kinds that move no rows: `CREATE`, `ALTER` and `DROP` of a table, index (XML, columnstore and full-text among them), constraint, view, procedure, function, trigger, schema, sequence, synonym, type, role, user, partition function or partition scheme; `ALTER SCHEMA … TRANSFER` and `ALTER AUTHORIZATION`; `GRANT`, `REVOKE` and `DENY`; `ALTER TABLE … SET`; `EXECUTE` of `sp_rename`, `sp_refreshsqlmodule`, `sp_refreshview`, `sp_addextendedproperty`, `sp_updateextendedproperty` and `sp_dropextendedproperty`; the `INSERT` into `[dbo].[__RefactorLog]`; the checks of §6; `PRINT`, `SET` in every form (`SET IDENTITY_INSERT` among them), `USE`, `DECLARE`, variable assignment, `RAISERROR`, transaction control, every statement on a temporary table (DacFx's `#tmpErrors` among them), and DacFx's validation loop over `sys.check_constraints` and `sys.foreign_keys` with its `EXECUTE sp_executesql` of an `ALTER TABLE [ … CHECK CONSTRAINT [ …` composed from literals (DacFx 170.5.96 writes `WITH ' + CASE @is_not_trusted WHEN 0 THEN N'CHECK' ELSE N'NOCHECK' END + N' CHECK CONSTRAINT [`). Every other statement is one of these, each an open item at level 4 with the command that proves it:

| Why | The statement | What a deploy would do that no query shows |
| --- | --- | --- |
| `rebuild` | `INSERT INTO [dbo].[tmp_ms_xx_…] … SELECT … FROM [dbo].[…]`, with the `DROP` and `sp_rename` that follow | Copies every row under a lock; its time at production size; a capture instance or a trigger bound to the old `object_id` |
| `identity` | A rebuild whose new table declares `IDENTITY` where the old did not, or drops it | Reseeds; `IDENTITY_INSERT` on the copy; the gaps the copy keeps |
| `re-trust` | `ALTER TABLE … WITH CHECK CHECK CONSTRAINT …` on a constraint the environment holds untrusted | Validates every row against a constraint already in place (Msg 547 at the deploy, which §6's query predicts only for a constraint the plan adds) |
| `authored` | An `INSERT`, `UPDATE`, `DELETE` or `MERGE` of a pre- or post-deploy script that writes a table the plan alters, rebuilds or drops, a target written through an alias read from the `FROM` clause | Runs against the new shape of the table on existing rows, with the script's file untouched since the last deploy |
| `cdc` | Any operation on a table `dbchange/ledgers/cdc-tracked.md` names, or a column-list change on it | A rebuild drops the capture instance; a new column is never captured; the consumer loses rows with no error (v3 `kernel/Classify.cs`, `cdc.column-list` and `cdc.table-rebuild`) |
| `unrecognised` | Any statement outside the closed list | Unknown never means applies; the item names the statement's kind and its first line |

A table the plan creates and a post-deploy seed then fills is settled read-only: the new table holds no rows, so the seed can fail only on its own content, which is the same on every environment. A seed `MERGE` on a table the plan does not alter is settled the same way. Both are the corpus's cases `add-a-lookup-and-its-seed` and `edit-the-post-deploy-seed`, which apply.

Three more items go to a person rather than to a proof, each at level 0: an operation `--intent` does not explain, an `--intent` whose operations the plan holds none of (`plan.intent-absent`: the object may already be there, or `DropObjectsNotInSource=False` keeps a drop out of the plan), and a contract change for OutSystems consumers, a drop, rename, move to another schema, type change, tightening or loosening of a column an external entity reads, the tightening and the loosening named only where the environment's column changes nullability, named with the order of deploy, Integration Studio refresh and publish, which `_index/multi-phase` decides. One warning goes to the lead without an item: a `NOT NULL` column the plan adds with a default the project does not declare is `GenerateSmartDefaults` stamping every existing row with a value nobody chose (`smart-default`).

## 8. How `prove` works

`prove` is the one write path, to one place. The local server is the SQL Server `DBCHANGE_SQL` names, else the one `~/.dbchange/sql.env` names (the file `ci/sql.sh up` writes), else LocalDB on Windows, chosen as v3's `io/LocalServer.cs` chooses it. The tool refuses, exit 9, before it connects, a server whose host is not loopback or LocalDB, or whose host equals an environment's host in `dbchange/environments.json` by spelling or by resolved address (R15). It never takes a connection string from an argument.

1. The environment's package and collation are read as `predict` reads them; for `neverRead`, `--at` must name the release's dacpac, the collation is the release package's, and the expected answers are the worst case.
2. The copy, `dbchange_lite_<UTC stamp>_<pid>`, is created with the environment's collation. Copies of this name pattern older than a day are dropped first.
3. The environment's package is deployed to the copy; for `neverRead` it is the release's package, whose pre- and post-deploy scripts run, so the copy holds what the release's seed writes. The rows file is run, batch by batch at each `GO`; a batch that fails is `needs-input`, exit 8, `rows.failed`, naming the batch and the SQL Server message. The rows are the agent's, written from level 3's answer; the tool never copies a row from an environment (R14).
4. The copy is measured as `predict` measures the environment, and each check and precondition is compared with the environment's answer. A difference is `needs-input`, exit 8: `the copy's [dbo].[Customer] is empty and dev's has rows; add rows`. An item the environment did not answer is a difference too, and a precondition that reads a column the plan adds is left to the deploy. The proof stands on rows that answer as the environment's do, or it does not run.
5. The change's package is deployed to the copy with `DacServices.Deploy` under the profile's options with `BlockOnPossibleDataLoss` on. DacFx's result is read once: success is `done`; a `DacServicesException` carrying Msg 50000 at state 127 is `blocked` by `block-on-possible-data-loss`; one carrying Msg 547, 1505, 2627, 2628, 515 or 1946 is `blocked` by `constraint-violation` (1505 is a unique key or index meeting duplicate rows); anything else is `configuration-refused` with DacFx's messages as findings.
6. The copy is measured once more. A precondition violated on the planted copy that holds after the deploy is the warning `prove.rows-changed`: a deploy script corrected the rows before DacFx validated the constraint, so the read-only prediction and the proof differ by design. Measured 2026-10-24 on the corpus: the golden seed's `MERGE` on `[dbo].[Product]` matches on `Id` and rewrites `Code`, so a violating row the seed reaches ships, and one with an `Id` the seed does not name blocks with Msg 547.
7. The copy is dropped, on success, on failure and on Ctrl-C (exit 130), and the answer carries level 4 and `Proven on <env>`, or for `neverRead` `assumed: worst` and `<env> assumed; its worst case deployed`, the copy's name, the server's version and edition, the plan's script and report in the run folder, the planted rows file's path and hash, the verdict with the table or the precondition that stopped it, and the open items `predict` raises for a person on the same change (the CDC consumer's owner, the lead for a contract change).

## 9. The files

Under `tools/dbchange-lite/`, a folder that lifts out of this repository whole: its own `DbChangeLite.sln`, `global.json`, `NuGet.config`, `Directory.Build.props` and `Directory.Packages.props` stop the repository's build settings and package pins at the folder's edge, as `tests/Golden/` does, so the folder builds the same here and in the SSDT repository. The code has no comments: a name says what a thing is, a test says what it does, and this document says why. The ceilings are a test: 3,500 physical lines of C# under `src/`, 200 a file. The lines below were measured on 2026-10-24, after the review's fixes (§13.6).

| File | Its one job | Ports from v3 | Lines |
| --- | --- | --- | ---: |
| `Program.cs` | The telemetry opt-out first, the `xp_msver` refusal second, the invariant culture, Ctrl-C and SIGTERM, then the verb; every failure is one answer | `cli/Program.cs` | 107 |
| `Contract.cs` | The verb table, each verb's flags and outcomes, the exit table with v3's meanings and remedies, `--help` and `--help --json` | `cli/Contract.cs` | 168 |
| `Answer.cs` | The envelope, findings, provenance, open items, the rows of an environment not read and of one assumed; the full answer written to the run folder, the summary when it would exceed 400 lines | `cli/Render.cs` | 103 |
| `Run.cs` | The repository root, the folder the verb runs in, the run folder keyed by stamp, process and verb, the tool folder, the queries log | `io/Checkout.cs` | 74 |
| `Fingerprint.cs` | SHA-256 over canonical text (byte-order mark dropped, line ends LF) and over bytes | `kernel/Fingerprint.cs` | 29 |
| `Environments.cs` | `dbchange/environments.json` read as data, each key of its kind, references resolved where they connect, a literal credential refused, hosts compared for R15 | `kernel/Environments.cs`, `io/Environments.cs` | 174 |
| `PublishProfiles.cs` | A `.publish.xml` through `DacProfile`, its options and SQLCMD values, its canonical hash, a password refused; `BlockOnPossibleDataLoss` forced on for a proof | `io/PublishProfiles.cs` | 68 |
| `Toolchain.cs` | The ledger's pin and window, the profile confirmation rows, the DacFx release running (`DacFxRelease`) | `io/Doctor.cs` | 89 |
| `Ssdt.cs` | A source parsed and built or opened: a ref in a worktree merged onto another, with the uncommitted-change warning for `ref:HEAD`, a project folder, a dacpac; the one project of a folder | `io/Ssdt.cs`, `io/Git.cs` | 139 |
| `Ssdt.Commands.cs` | `dotnet build` with the tool folder's DacFx targets and the stop files, its SSDT error codes read; git; a bounded process that Ctrl-C stops | `io/Ssdt.cs`, `io/Command.cs`, `io/LocalState.cs` | 96 |
| `DacFx.cs` | Extract; Script package to package with its report and script, written to the run folder; Deploy for `prove`; every failure classified by its SQL Server number | `io/DacFx.cs`, `io/DacFx.Scripts.cs` | 172 |
| `Model.cs` | What a package holds: tables, columns and their nullability, identity, check constraints' expressions, defaulted columns, collation, the objects `drift` lists; a name split into its parts | `io/ModelRead.cs` | 83 |
| `ModelDiff.cs` | Two packages compared element by element, property and relationship, with the refactorlog's renames and schema moves and the deploy scripts' text, in the lines the corpus's `change.txt` writes | `io/ModelDiff.cs` | 173 |
| `ServerProbe.cs` | DacFx's `exec xp_msver` refused before SqlClient sends it | `io/ServerProbe.cs` | 58 |
| `Cache.cs` | A package by the merge's tree hash or the folder's fingerprint; an extract by the schema fingerprint; each written to a partial file and moved into place | `io/Ssdt.cs` | 59 |
| `DeployScript.cs` | The script's statements with ScriptDom, SQLCMD lines dropped and `$(name)` substituted, each with its part, line and kind | `io/PlanStatements.Read.cs` | 154 |
| `BlockOnPossibleDataLoss.cs` | Each check on the parse tree, the three lookalikes skipped | `io/BlockOnPossibleDataLoss.cs` | 41 |
| `Preconditions.cs` | `ForeignKey`, `Check`, `Unique`, `NotNull` and `TableEmpty` from the statements and the model, a rebuild copy's constraints on the original table, none on a table DacFx creates for itself; each query composed and parsed again | `io/AggregateQueries.cs` | 158 |
| `Measure.cs` | One `SELECT` per statement, the lock and command timeouts told apart, a refusal with SQL Server's message, the exact predicate then `sys.partitions`, the queries log, the catalog reads and the schema fingerprint | `io/SqlServer.Measure.cs`, `io/SqlServer.Query.cs` | 191 |
| `NotPredictable.cs` | `rebuild`, `identity`, `re-trust`, `authored`, `cdc`, `unrecognised`, and the operations `--intent` does not explain or does not produce | `kernel/Classify.cs` | 173 |
| `NotPredictable.List.cs` | The closed list of statements that move no rows, and DacFx's housekeeping names (`tmp_ms_xx_`, `[dbo].[__RefactorLog]`) | `kernel/Vocabulary.cs` | 41 |
| `NotPredictable.Shape.cs` | The tables the plan alters, drops, rebuilds and moves; consumer changes; smart defaults | `kernel/Classify.cs` | 148 |
| `Causes.cs` | `causes.json`, and the match of each cause to a difference | — | 58 |
| `Baseline.cs` | An environment read once: session, collation, schema fingerprint, extract, model; the CDC ledger; a connection string parsed or refused | `io/DriftCheck.cs` | 85 |
| `Doctor.cs` | Each check of §3's `doctor` row with its remedy | `io/Doctor.cs` | 152 |
| `Drift.cs` | §3's `drift` row | `io/DriftCheck.cs` | 56 |
| `Diff.cs` | §3's `diff` row | `io/ModelDiff.cs` | 32 |
| `Predict.cs` | §7, composed from the files above; the configuration refused before any environment is read; the exit and message over several environments | — | 192 |
| `Predict.Answers.cs` | The open items, findings, Prod's worst case and the confidence sentence | — | 135 |
| `LocalServer.cs` | The local server chosen as v3 chooses it, refused by name and by address, the copy's name, the sweep, the drop | `io/LocalServer.cs` | 88 |
| `Prove.cs` | §8 | `io/SyntheticCopy.cs` | 162 |
| | **Total** | | **3,458** |

Beside the code: `operations.json` (§5), `causes.json` (§5), `exits.frozen` (v3's, copied and held equal by a test), `ci/publish.sh` and `ci/publish.ps1` (the tool folder, as v3's assemble it: the dll with its dependencies, DacFx's `Microsoft.Data.Tools.Schema.SqlTasks.targets`, the .NET Framework 4.7.2 reference assemblies, SQL Server 2022's `master.dacpac` and `msdb.dacpac`), `knowledge/dbchange-lite.md` (the entry skill, §11), and `README.md`.

The tests read three inputs from outside the folder: v3's `tests/Golden/` (the golden project, its sample changes and copies, and the data-loss scripts) and this document; when the folder moves to the SSDT repository, they move beside it. Tests sit in `tests/`, outside the ceiling, in v3's categories: `fast` needs nothing, `fixture` needs the local server the tool itself chooses (`DBCHANGE_SQL`, `~/.dbchange/sql.env` or LocalDB), `build` runs `dotnet build` through the tool. No test skips itself and none retries.

## 10. Tests, and the corpus as the acceptance test

**Fast.** The exit table equals v3's `cli/exits.frozen` byte for byte. Every `--help --json` outcome pairs with its exit as v3's envelope pairs them. Each check of `tests/Golden/data-loss-checks/` is found with its table, and the three lookalikes of spike S2 are skipped, on a script shaped as v3's `tests/Io.Tests/BlockOnPossibleDataLossReaderTests.cs` shapes them. Each row of §7.4 is classified from a script that holds it, and DacFx's own forms are admitted: `sp_rename`, the `__RefactorLog` row, the `#__checkStatus` table, the validation loop's `sp_executesql`, `SET IDENTITY_INSERT`. Each precondition kind is read from its statement in v3's spelling and its query parses as one `SELECT`. A literal credential or a password in the environments file is refused, exit 6, quoting nothing. The profile hash is the same under LF and CRLF. The toolchain ledger gives the pin, the release before and the confirmed profiles, and the window holds. Every `dbchange-lite` command line in `knowledge/` and in this document names a verb and flags `--help --json` lists. The ceilings hold. No `.cs` file under `src/` holds a comment. `prove` refuses by name, before it connects or builds, a local server that is an environment's host and a server that is not local. From recorded answers and no server (`OpenItemTests`): the exit precedence of §3 and the message naming what blocks; a check read from `sys.partitions` after Msg 229; a lock wait, a slow query and a refusal, each with its next step; a check's count under `--counts`; the confidence sentence. An environment entry of the wrong kind is exit 6 naming the key; the proof options keep the profile's other options and SQLCMD values; a cached package is built once; `exec xp_msver` is refused and a `SELECT` naming it passes; Ctrl-C stops a child process within seconds; a merge that conflicts and one git refuses are exit 8 apart; an unknown `--intent` and a missing `--at` are exit 1 before any connection; a relative path resolves from the folder the verb runs in.

**Fixture.** The corpus: `tests/Golden/changes/` (59 sample changes on 2026-10-24), each predicted on the three copies v3's `SampleChangePredictionTests` builds, the empty copy, the populated copy from `tests/Golden/copies/populated.sql`, and the violating copy from the change's `violating.sql` where it has one. The harness (`tests/CorpusTests.cs`) deploys the golden base to one database on the local server, writes an environments file naming it as the environment `corpus` on the host `corpus.invalid`, and for each change applies its `edits.txt` to a copy of the project and runs `predict --env corpus --to project:<the copy> --onto none` three times, emptying and planting the database between runs. It compares each copy's outcome with the copy's line in `predict.txt` up to its first semicolon: `applies`, or `blocks by` the same preconditions in the same spelling, as a set. The clause after the semicolon, how the change ships, is the skills' and is never compared. A `Fits` or `KeyLength` precondition v3 lists is allowed to be absent from the lite tool's list (§12). Where v3's line reads `prove it`, the lite tool's `notPredictable` holds at least one item. A truncated answer is read from the file `full` names. `drift` answers `in-sync` against the copy the base was published to, notes a table created by hand as unmanaged, and answers `differs`, exit 5, naming `[dbo].[Customer]` after a column was widened by hand. `diff` prints each sample change's `change.txt` where the change has one. Each copy's exit follows from its outcome and its `open` list: 3 when it blocks, else 8 when an item is open, else 0. A lock on the catalog answers `predict` exit 4 with the environment as an open item. An extended property added by hand changes the schema fingerprint. `prove` runs end to end on three changes: `make-mandatory` on the populated copy, blocked by `block-on-possible-data-loss`; `add-a-check-constraint` with a violating row whose `Id` the seed does not name, blocked by `constraint-violation`, and with the corpus's own violating row, which the seed corrects, done with the `prove.rows-changed` warning; `add-a-nullable-column` on the populated copy, done; and refuses, exit 8, rows that answer otherwise than the environment's do. Its blocked message names `[dbo].[Customer]`, and with the CDC ledger present it keeps the CDC consumer's question. **Build:** `ref:HEAD` builds the commit and warns `source.uncommitted` beside an uncommitted edit; a broken post-deployment script names `SQL72007`.

**Four cases of the lite tool's own**, under `tests/Golden/lite/`, beside the corpus and outside v3's harness, since each carries a file v3's harness does not read. Their `predict.txt` lines were measured on 2026-10-24 on SQL Server 2022 and DacFx 170.5.96:

| Case | What it adds | Measured |
| --- | --- | --- |
| `make-mandatory-on-a-cdc-tracked-table` | A `cdc-tracked.md` in the case folder naming `[dbo].[Customer]`, which the harness writes as the ledger | `populated: blocks by TableEmpty on [dbo].[Customer]`, `violating` adds `NotNull`, and a `cdc` item on every copy, since the ledger speaks whatever the rows say |
| `add-a-mandatory-column-to-a-seeded-table` | A `NOT NULL` column without a default on `[dbo].[Status]`, whose post-deploy seed `MERGE` does not set it | `populated: blocks by TableEmpty on [dbo].[Status]`, and an `authored` item on every copy for the seed that writes a table the plan alters |
| `add-mandatory-no-default-with-smart-defaults` | A `profile.publish.xml` with `GenerateSmartDefaults` on, which the harness names as the environment's profile, and a `findings.txt` naming `smart-default`, which the harness requires on every copy | `applies` on every copy, no check written, and the `smart-default` warning for the lead |
| `rename-a-column-already-deployed` | The rename with its refactorlog entry and the seed edited to the new name, deployed to the copy, then predicted again (`after-deploy.txt`) | Before the deploy: `applies` with one `Rename` operation, an `authored` item for the seed and a contract item for the rename. After the deploy: `applies` with no operation, so DacFx does not repeat a rename `[dbo].[__RefactorLog]` records |

The harness runs in this repository's CI as the job `lite` of `.github/workflows/dbchange.yml`, on Ubuntu against the SQL Server 2022 Express `ci/sql.sh up` installs and on Windows against LocalDB, beside v3's lanes and touching none of them.

## 11. What the skill files carry

The skills keep their reasoning and lose their mechanics. Each swaps a hand-run `sqlpackage` or `sqlcmd` step for a verb and reads the verb's `open` list to decide what comes next. The operation skills under `skills/op/` (45 on 2026-10-23) keep their trigger phrases, their named trap and their verdict wording.

| Skill | What it keeps | The one change |
| --- | --- | --- |
| `confirm-intent` | Naming one operation from the developer's words | Passes the operation to `predict --intent`, which checks it against the plan |
| `classify-mechanism` | The provisional shipping shape and what the approving lead weighs | Ends by running `predict`, and hands no specification to another agent |
| `prove-on-dacpac` | The proving loop, the deploy output as the signal, what a copy cannot prove | Runs `prove` when `predict` lists a level-4 item, writing the rows the copy needs from level 3's answer |
| `talk-to-local-sql` | LocalDB setup and reset for the disposable copy | Drops its ad hoc row, null, duplicate and orphan queries, which `predict` now runs |
| `ask-the-developer` | The decisions only a person makes | Reads them from `predict`'s `open` items with `person` |
| `author-pr` | The ten-section pull request description and its register | Fills the evidence sections from the JSON, quotes `confidence` and `provenance` verbatim, lists every planted row |
| `_index/multi-phase` | When old and new application code must coexist, and how many releases that takes | Reads the contract-changing operations `predict` lists for OutSystems consumers |
| `review-change`, `dependency-scope`, `adversary` | Their reasoning, the review's register | `review-change` reproduces by running `predict` and `prove` at the pull request's provenance and compares `confidence` and `blockedBy`, in place of its own sqlpackage Strict and Permissive loop; `dependency-scope` reads the closure from the project and `plan.operations`, in place of its catalog queries; `adversary`'s violating rows become a rows file for `prove`, and its consequence run on a shared copy is dropped |
| `decompose`, `deploy-scripts`, the rest of `_index/*` | All of it | None |
| `knowledge/dbchange-lite.md`, the entry skill | The confidence levels, the stopping rule, the worst-case rule for Prod | New, about 80 lines |

The entry skill says: run `doctor`; name the operation; run `drift` and `predict` for each environment the developer reads, and ask a lead for QA and UAT; for Prod, choose the shape that holds when every table has rows and every constraint is violated; work the `open` list until it is empty or names a person; then write the pull request. v2's three-agent sequence (intake, authoring, review) becomes one authoring session and a separate review, as v3's design chose. Each verb's `--help` and each refusal carry the stopping rule in one line, so a Copilot session that never loaded the skills is still steered by the tool.

## 12. What it leaves to v3

The lite tool cuts everything that is neither a read-only answer nor a proof on a local copy. Each cut returns as a port from v3 when a false answer in the field calls for it, with a decision line in the SSDT repository naming the finding.

- **Left to the skills:** the rows a disposable copy needs (`prove-on-dacpac`) and the pull request description (`author-pr`).
- **Left in v3:** the synthetic copy and its planter, the CI gate, the after-deploy checks and the environments page, `profile`, v3's CDC ledger verb, and v3's MCP server. The lite tool's own MCP adapter is optional, about 150 lines, after the trial.
- **Replaced by simpler means:** v3's statement allowlist and its monitoring of DacFx's own statements give way to a one-`SELECT` parse of each statement the tool composes, the queries log, and the `xp_msver` refusal. v3's law and manifest tests give way to the corpus and the ceilings.
- **Deferred until a pull request needs them:** `Fits` and `KeyLength`, v3's fit queries (`TRY_CONVERT` with a `DATALENGTH` test) and its key-width arithmetic over the model. DacFx's check blocks a narrowing on any populated table, and Msg 1946 at an index build is a constraint violation the proof shows.

## 13. Risks, weak spots and blind spots

The tool fails in five ways: it plans against the wrong thing, it misses what matters, it touches what it must not, the agent claims more than the tool showed, or the team stops trusting or maintaining it. Each fix below names the test or case that holds it.

### 13.1 It plans against the wrong thing

| Risk | The fix | Held by |
| --- | --- | --- |
| **The plan uses other options than the deploy.** The profile decides the plan: with `GenerateSmartDefaults`, a new `NOT NULL` column lands on a populated table instead of blocking. v3 found a hand-kept profile had drifted from the pipeline's | Every verb loads the one `.publish.xml` the environments file names, through `DacProfile`, and stamps its hash; until the ledger's `profile` row names who confirmed it, every answer says `profileVerified: false` | the smart-defaults case; `FilesTests`' profile hash test |
| **DacFx differs from the deploy's.** Plans are version-bound: Query Store plans `OFF` on 162.5.57 and `READ_WRITE` on 170.5.96 | `doctor` refuses a release outside the ledger's window, exit 6; a pin change is a pull request that runs the corpus on the new release | `FilesTests`' toolchain-window test |
| **The baseline is the wrong release.** `--at` is only as right as the tag a person types | Spike S7's owner adds one post-deploy line that stamps the release tag into the database as an extended property, and the tool reads it when present; until then `--at` is required and the answer names it | `DriftProveTests`' drift tests; S7 |
| **Drift the deploy never sees.** With `DropObjectsNotInSource` off, a table or view added by hand in QA never appears in a plan. An index does: `DropIndexesNotInSource` is its own option and defaults to true, so the deploy drops a hand-made index (measured 2026-10-24) | `drift` lists each object only the environment holds as *unmanaged*, as information, and reports the drop of a hand-made index as the difference it is | `DriftProveTests`' unmanaged-table and hand-made-index tests |
| **A rename already deployed.** DacFx records each applied rename in `[dbo].[__RefactorLog]`, and a plan against an extracted package cannot read that table | Measured 2026-10-24 on DacFx 170.5.96: the plan of the renaming package against an extract of a database the rename was deployed to holds no operation, so the rename is not repeated; a DacFx release that changes this fails the case | the `rename-a-column-already-deployed` case |
| **Variables, references and platform fail late.** A SQLCMD variable with no value for an environment, a missing reference dacpac, a project platform newer than an environment's SQL Server | `doctor` checks each per environment (spike S8 gives the versions) | none yet (§16) |
| **A merge the developer did not make.** `predict` plans the branch merged onto main, which the developer may never have built | The answer's `provenance` names `onto` and the merge's tree; a conflict is exit 8 naming the files, never a plan of the branch alone | `ProcessTests`' merge test |

### 13.2 It misses what matters

| Risk | The fix | Held by |
| --- | --- | --- |
| **A statement the tool does not recognise reads as applies** | Fail closed: every statement outside the closed list of §7.4 is a level-4 item | `RulesTests`' closed-list tests |
| **The data moves between prediction and deploy.** A table empty on Tuesday has rows on Thursday, and a blocked deploy stops part way under the default options | Answers carry their time and their queries; the same queries run again on the release just before each promotion | process; `beforePromoting` |
| **The change ships with others.** A release from main carries what merged ahead of the branch | `predict` plans the branch merged onto main's head; the promotion check plans the release's tag | `ProcessTests`' merge test, which merges one ref onto another |
| **A CDC-tracked table.** A rebuild drops the capture instance; a new column is never captured | The CDC ledger and `is_tracked_by_cdc` make any operation on such a table a level-4 item and a question for the consumer's owner | the CDC case; `RulesTests`' CDC test; `DriftProveTests`' make-mandatory proof |
| **The running OutSystems application.** A drop, rename, type change or tightened column breaks consumers until each environment's extension is refreshed and republished; no level sees it | Each contract-changing operation is an open item for a person, with the order of deploy, refresh and publish; `multi-phase` decides coexistence | `RulesTests`' contract-change and schema-move tests |
| **Deploy scripts that did not change.** A post-deploy seed runs on every deploy, before DacFx validates a new constraint (spike S10), and can fail on the new shape with its file untouched, or correct the very rows a read-only prediction counted as violating | An `authored` item for every script statement that writes a table the plan alters, so the change goes to `prove`; `prove` measures the copy after the deploy and warns `prove.rows-changed` where a violation the copy held no longer holds | the seeded-table case; `prove`'s rows-changed test |
| **Row presence read approximately.** `sys.partitions` counts are approximate | DacFx's own predicate runs first; `sys.partitions` only when `SELECT` is refused, and the answer says so | `OpenItemTests`' `sys.partitions` test |
| **Level 4 is only as real as its rows** | `prove` measures the copy after planting and refuses to deploy, exit 8, when the copy answers any check or precondition otherwise than the environment did | `DriftProveTests`' mismatch test; `RulesTests`' unanswered-item test |

### 13.3 It touches what it must not

| Risk | The fix | Held by |
| --- | --- | --- |
| **Prod.** Nobody reads Prod, so level 3 cannot exist there | The worst case is assumed, the row says so, and `beforePromoting` prints the queries | `AnswerTests`' never-read test |
| **A proof deployed to a shared database** | `prove` accepts only a loopback or LocalDB server, refuses by name and by address any host the environments file names (exit 9), and takes no connection string from an argument | `RefusalTests`, which connect to nothing; `FilesTests`' loopback test |
| **Heavy reads on QA or UAT** | Existence first, counts only under `--counts`, `SET LOCK_TIMEOUT 5000`, a 30-second command timeout, and a timeout as an open item | `OpenItemTests`' lock, timeout and refusal test; `DriftProveTests`' lock test |
| **DacFx's own `EXEC`** | `ServerProbe.cs` refuses `xp_msver` before SqlClient sends it, and a DacFx release that stops catching the refusal fails the read as `dacfx.failed` naming the call | `FilesTests`' server-probe test |
| **A write the tool itself composes** | Every query is parsed before it is sent and must be one `SELECT` whose result is a count, a 0 or 1, or a catalog row; the queries log records every statement the tool sends | `RulesTests`' one-`SELECT` test; `ContractTests`' log test |
| **Credentials and row values in output** | References only in the environments file (exit 6 for a literal); answers carry booleans, counts and object names, never a row of an environment. A proof's DacFx messages can quote a planted row's key value (Msg 2627 names the duplicate), which is the agent's own row | `FilesTests`' literal-credential and profile-password tests |
| **Who may read QA and UAT** | A developer's own login reads Dev; a lead runs `predict` on QA and UAT and pastes the answer, which is the prediction of record until a service account exists (`V3_MILESTONES.md` §17 item 17) | the entry skill |

### 13.4 The agent claims more than the tool showed

| Risk | The fix | Held by |
| --- | --- | --- |
| **It stops early, or paraphrases its confidence** | The tool writes the confidence sentence and the lowest level per environment; `author-pr` quotes them with the provenance | `OpenItemTests`' confidence test |
| **It names the wrong operation** | `operations.json` and `--intent`: every unexplained operation is an open item; an intent the plan does not produce at all is one too | `RulesTests`' intent tests; `OpenItemTests`' intent items |
| **A remedy that looks right and does not clear the block.** Backfilling every blank Email leaves the row-presence check firing | The stopping rule runs `predict` again on the remedied branch, and a data-modifying pre-deploy statement is an `authored` item | §2's example; `RulesTests`' `authored` tests |
| **Copilot in Visual Studio never loads the skills.** v2's bundle lists this as unverified | The trial checks it first; every `--help` and refusal carries the stopping rule; the MCP adapter follows the trial | the trial |
| **Long answers swamp the context** | Summary first; the full answer in the run folder, named by `full` | `AnswerTests`' summary test |
| **Repeated runs are slow** | `Cache.cs` reuses a package by tree hash and an extract by schema fingerprint, the fingerprint covering extended properties | `FilesTests`' cache test; `DriftProveTests`' fingerprint test |

### 13.5 The team stops trusting it, or stops maintaining it

| Risk | The fix | Held by |
| --- | --- | --- |
| **One wrong applies ends trust** | The deployment step keeps `BlockOnPossibleDataLoss` on, so a miss blocks rather than damages; each miss becomes a corpus case and its fix in one pull request | process |
| **It grows back.** The forces that took v2 to 118,686 lines (2026-08-29) act on any codebase agents maintain | The ceilings are a test; a new behaviour's pull request names the field finding; no new documents | `CeilingTests` |
| **Skills cite flags the tool no longer has** | Every command line in inline code or a fenced block of `knowledge/` and this document names a verb and flags `--help --json` lists | `CeilingTests`' command-line test |
| **Three tools at once.** The v2 zip, the lite tool and v3 | The lite tool replaces the zip's read side; the zip leaves the SSDT repository once `drift` and `predict` pass on the real project; v3 is never handed to the team | decision |
| **A switch mid-sprint** | Run it beside the current setup on one lead's machine, where a disagreement is a finding for both (`V3_MILESTONES.md` §15 item 3); switch at a sprint boundary | rollout |
| **Only one person can fix it** | No comments in the code, so the names and the tests carry it; this document names each file's purpose and the v3 file it ports from; a red corpus case names its change; a second maintainer reviews every change | decision |
| **It is built on the operator's evenings** | Agent sessions build §14's work packages against the corpus, as v3 was built; the operator's time goes to Step 0, the decisions and the trial | §14 |

### 13.6 Found by the review of 2026-10-24, and fixed

Thirteen reviewers each read the tool through one concern and ran it against the local server. The defects in the first table each changed an answer a developer or a lead reads, and were fixed first; the findings rated should-fix, triaged the same day, are in the second, each fixed with the test that holds it.

| Defect | The fix | Held by |
| --- | --- | --- |
| A source printed as its record (`Project { Path = … }`), so every open item's command failed to parse and `predict` answered `internal.unexpected`, exit 6, losing the measured answer, whenever an environment was unreachable or a query timed out | `Source.ToString` sealed; commands joined, never formatted | `AnswerTests`, `FilesTests` |
| Every `ref:` source, the default `--to ref:HEAD` among them, answered "holds no .sqlproj": the worktree lies under `.dbchange/` | The project search reads paths below the folder it searches | `FilesTests` |
| An unset connection reference, a catalog read behind a schema change's lock, and an `--env` naming nothing stopped or emptied the run | Not read with an open item; exit 1 for an empty `--env`; `doctor` notes an environment this machine does not read | `AnswerTests`, `DriftProveTests` |
| A constraint on an existing table over a column the plan adds was answered clean without a query; a row with an unanswered item said `applies`; the summary kept 20 findings; an aliased `UPDATE` or `DELETE` in a deploy script, and DacFx's validation loop for an unnamed constraint, were misread; a retype of a `NOT NULL` column was called a tightening | An open item at level 4; `unknown`; every finding kept; the alias resolved from `FROM`; the loop's `CASE` read; nullability compared with the environment's column | `RulesTests`, `AnswerTests` |
| `prove` called Msg 1505 a failure of the tool, stood on items the environment did not answer, wrote `profileVerified` false, crashed on a rows batch a release seed had written, claimed level 4 for a never-read environment, and accepted a local server at `127.0.0.1` beside an environment on `localhost` (R15) | `constraint-violation`; a difference; the ledger read; `rows.failed`, exit 8; `assumed: worst`; two loopback hosts are one machine | `DriftProveTests`, `RulesTests`, `FilesTests` |
| The `smart-default` warning never fired: DacFx writes the default as a table constraint, `DEFAULT N'' FOR [col]`, and the harness compared outcomes only; a check's remedy offered to empty the table before the deploy | The table constraint read; the remedy names the two releases alone | `RulesTests`, the smart-defaults case |
| `drift` wrote level 1 beside a difference with no known cause and called an index the plan drops unmanaged; check expressions read empty, so `check-written-with-in` never matched; `diff` kept one of several unnamed constraints | Level 0; the index listed once; the model's `Expression` property; unnamed elements keyed by what they reference | `DriftProveTests`, `RulesTests`, `DiffTests` |

| Finding | The fix | Held by |
| --- | --- | --- |
| `predict` could never answer `unreachable`, exit 4, since a not-read environment always added an open item; that row stood at level 1; a blocked message named no table; an answer whose every environment is `neverRead` said "Every environment read applies" | 4 before 8 when no environment answered; level 0 on the row, 1 on its open item; the table or the precondition named; "No environment was read; …" with each assumed outcome | `OpenItemTests`, `AnswerTests`, `DriftProveTests` |
| An unknown `--intent`, a missing `--at` and each profile were checked inside the loop after environments were read, and any refusal of one environment's configuration discarded the rows already measured | Checked before the loop; every refusal of one environment leaves that environment not read | `AnswerTests` |
| Under `--counts` a check's count was read and dropped; Msg 1222 read as a 30-second timeout; a refusal named the child table when SQL Server refused the parent; the read path took a sealed session, so no fast test reached it | The count reported; a lock wait told apart; SQL Server's message quoted to the DBA; the reads take a query function | `OpenItemTests` |
| A malformed connection string, a package DacFx cannot load and an environment entry of the wrong kind answered `internal.unexpected`, or were read silently, so `"neverRead": "true"` read Prod | `environment.connection`, `package.unreadable` and `environments.<key>`, each exit 6 | `AnswerTests`, `RulesTests`, `FilesTests` |
| Ctrl-C waited for `dotnet build` or git to end; two runs in one second shared a run folder; an interrupted run left a half-written cache file that the next run reused; `ref:HEAD` dropped uncommitted edits silently; a merge git refused claimed a conflict in `.`; SSDT's `Build error SQL72007` was not read as an error; relative paths resolved from the repository root | The child killed on cancel; the process id in the folder name; a partial file moved into place; `source.uncommitted`; `merge.failed`; the error code read; the folder the verb runs in | `ProcessTests`, `FilesTests`, `SourceTests`, `AnswerTests` |
| `ALTER INDEX`, `ALTER SCHEMA … TRANSFER` and partition functions and schemes were `unrecognised`; DacFx's `#__checkStatus` and `[dbo].[__RefactorLog]` carried `Unique` preconditions; a created table whose foreign key DacFx adds apart counted as altered, so its seed was `authored`; a move to another schema was neither a rename in `diff` nor a consumer change; a narrowing's default, dropped and re-created, was unexplained under `--intent`; an `--intent` the plan never produces answered `done` | The closed list widened; DacFx's own tables skipped; a created table kept as new; a rename and a consumer change; the pairs added to `operations.json`; `plan.intent-absent` | `RulesTests`, `OpenItemTests` |
| `prove` dropped the person items `predict` raises, created a never-read environment's copy in the local server's collation, and opened a session nothing read; an extended property added by hand left a stale extract in use | The person items kept; the release package's collation; no session; the fingerprint reads `sys.extended_properties` | `DriftProveTests` |
| A name holding a dot or a doubled bracket was split on `].[`; `Dacfx` and `DacFx` differed by case; the class `Measure` was shadowed by a method; the consumer contract and the verb table shared `Contract` | `Names.Parts` through ScriptDom; `DacFxRelease`; `RowPresence`; `ConsumerChanges` | `RulesTests` |
| Remedies hedged or cited rule numbers the SSDT repository does not hold (`R14`, `R15`); DacFx's sessions reached the server as `DacFx`; `internal.unexpected` left no trace; this document cited twelve tests that did not exist, and the skill-command test matched no verb | Imperative remedies naming the object; Application Name `dbchange-lite` on every session; `error.txt` in the run folder; each cell of §13 names a test that runs; the test reads every command in inline code and fenced blocks | `CeilingTests` |

Not fixed, each for its reason: a case-only rename without a refactorlog plans as nothing under a case-insensitive collation, so `predict` answers `applies` with no item, and the deploy does nothing either; a 128-character table name, which DacFx truncates in its `tmp_ms_xx_` copy, misreads that rebuild's preconditions, and no OutSystems-generated name reaches 128 characters; `baseline` keeps three readings, each defined in §4. The review's nits are not triaged.

**To hold the code near 2,500 lines,** cut in this order: `ModelDiff.cs` (`diff` then prints the deploy report's operations, which lose the column-level lines), `Cache.cs` (repeated runs slow down), the unmanaged-objects list, the `dacpac:` source, then `doctor`'s reference and platform checks. Below that the cuts are `PublishProfiles.cs`, `NotPredictable.cs` and `prove`'s refusal, and those are what stop a wrong applies and a write to a shared database. Keep them.

## 14. The build: work packages for a workflow

The lite tool is built as v3 is built (`V3_MILESTONES.md` §16): one work package at a time, its test written first, in a worktree, verified by a fresh reviewer per concern, fixed for at most two rounds, then merged. The wave workflow of `V3_BUILD_PROMPT.md` Appendix A runs it unchanged with the work packages below as `args.wps`; its `spec` reviewer reads this document in place of `V3_MILESTONES.md`, its `safety` reviewer holds the code to §3's last paragraph, §6, §7.4 and §8 word for word, and its `budgets` reviewer runs the ceiling tests and this repository's own `Budgets.Tests`. A wave is a set of work packages whose dependencies are merged and whose files do not overlap; on a four-CPU container two run at once.

| WP | Title | Files | Done when | Needs | Reviewers | Effort |
| --- | --- | --- | --- | --- | --- | --- |
| L0 | The folder and the contract | the stop files, `DbChangeLite.sln`, `DbChangeLite.csproj`, `Program.cs`, `Contract.cs`, `Answer.cs`, `exits.frozen`, the ceiling tests, `README.md` with its manifest row, the `lite` CI job | `--help --json` lists every verb with its flags and outcomes paired to v3's exits; `exits.frozen` equals v3's; the ceilings hold; this repository's `Budgets.Tests` pass with the folder present | — | spec, budgets | max |
| L1 | Environments, profiles, toolchain, doctor | `Environments.cs`, `PublishProfiles.cs`, `Toolchain.cs`, `Doctor.cs` | A literal credential is exit 6; a profile's hash is the same under LF and CRLF; `doctor` names each missing item with a remedy on a bare machine and answers `ready` on CI | L0 | spec, safety | default |
| L2 | Sources, DacFx, drift and diff | `Ssdt.cs`, `DacFx.cs`, `Model.cs`, `ServerProbe.cs`, `Cache.cs`, `Drift.cs`, `Diff.cs`, `ci/publish.sh`, `ci/publish.ps1` | `drift` answers `in-sync` then `differs` as §10 says; `diff` prints each `change.txt`; a merge conflict is exit 8; `ServerProbe.cs` refuses `xp_msver` | L0 | spec, safety | max |
| L3 | The script and its checks | `DeployScript.cs`, `BlockOnPossibleDataLoss.cs` | Each check of `tests/Golden/data-loss-checks/` found; the three lookalikes skipped; SQLCMD substitution as §6 says | L0 | spec | default |
| L4 | Preconditions and measurement | `Preconditions.cs`, `Measure.cs` | Each kind's query answers the three copies as §6 says; the exact predicate falls back to `sys.partitions` on Msg 229; a timeout is an open item; the log holds only `SELECT`s; no output holds a planted password | L0, L3 | spec, safety | max |
| L5 | What no query settles | `NotPredictable.cs`, `Causes.cs`, `operations.json`, `causes.json` | Each row of §7.4 is classified from a script that holds it; the closed list admits DacFx's validation loop and `sp_rename`; `--intent` lists an unexplained drop and create for a rename; each cause matches its difference | L0, L3 | spec | max |
| L6 | Predict and the corpus | `Predict.cs`, the corpus harness, the four new cases | 59 of 59 outcomes equal, the deferred `Fits` reported; the four new cases measured and committed with their `predict.txt`; the exit precedence of §3 | L1 to L5 | spec, safety, budgets | max |
| L7 | Prove | `Prove.cs` | The refusals of §8 without a connection; the three end-to-end proofs of §10; a planted-rows mismatch is exit 8; the copy is dropped on Ctrl-C | L2, L4 | spec, safety | max |
| L8 | The entry skill and the skill edits | `knowledge/dbchange-lite.md`; the edits of §11 as a vendoring patch for the SSDT repository | The skill-command test; one corpus change taken from the developer's words to a pull request body by an agent session following the entry skill alone | L6, L7 | spec | default |
| L9 | The MCP adapter, optional | `Mcp.cs` | Each verb as a typed tool call with the same answer; after the trial | L8; the operator | spec | default |

Waves under the medium guideline: L0 alone and inline, since every other package builds on it; then {L3, L5} and {L1, L2}, which share no file; then {L4}; then {L6, L7}; then {L8}. A wave's `args` are saved before it launches and its run resumed from `journal.jsonl`, as `CLAUDE.md` says.

**What landed, 2026-10-24.** L0 to L7 are built in this repository: the folder, the contract and the five verbs; the fast tests; the corpus harness, which predicts every sample change of `tests/Golden/changes/` on the three copies as its `predict.txt` states, with the four cases of §10 beside it; `drift`, `diff` and `prove` end to end on a local SQL Server 2022; and the `lite` CI job. L8 landed in part: the entry skill is `knowledge/dbchange-lite.md`; the edits of §11 to the skills wait as a vendoring patch for the SSDT repository. L9 waits on the trial. Step 0 and the field steps wait on the operator. A review by concern followed the same day; §13.6 lists what it found and what was fixed.

**What the operator does by hand, and when.** Step 0 needs a work laptop and Dev, and no code, and can run before, during or after L0 to L7: build the SSDT project at Dev's deployed tag with the tool folder's targets; `sqlpackage /Action:Extract` of Dev, timed; `sqlpackage /Action:DeployReport` of the built package against the extract under the deployment step's profile. Zero operations means the design holds on the real project; each operation is real drift or a new row of `causes.json`. Repeat with a branch that touches a column renamed in an earlier release. The same run answers v3's M1 exit 1. After L7: `doctor`, `drift --env dev` and `predict --env dev` on the real project beside the current setup; after L8: one real change end to end with one lead, at a sprint boundary.

## 15. Decisions for the operator, each with the default the build takes

- [ ] **Where it is built.** Default: this repository, under `tools/dbchange-lite/`, where the corpus, the SQL Server CI, the ceilings and the agent workflow already are; nothing private enters, since the environments file, the ledgers and the profile stay in the SSDT repository. A private clone of this branch changes one remote and nothing in §14.
- [ ] **Prod's check before promoting.** Default: the deployment step runs the `beforePromoting` queries just before it deploys, under the identity that already deploys; otherwise a person with Prod rights runs them.
- [ ] **DacFx's `EXEC xp_msver` under R14.** Default: refused as v3 refuses it (`ServerProbe.cs`, 40 lines), so R14 holds verbatim; the alternative is to admit DacFx's read-only system calls and say so in every answer.
- [ ] **The release-tag stamp:** one post-deploy line and one SQLCMD value the deployment step sets, agreed with its owner (S7). Default: `--at` is required until then.
- [ ] **The MCP adapter.** Default: after the trial, since the CLI is needed either way and the trial shows whether Copilot reads terminal output well.
- [ ] **Whether v3 keeps building** toward proving, the gate and the agent instructions. Default: v3 stays the specification and the corpus; its build continues as the operator allows.
- [ ] **QA and UAT reads.** Default: leads paste `predict`'s answer until a service account holds the read groups (§17 item 17).
- [ ] **A second maintainer** who reviews every change to `tools/dbchange-lite/`.

## 16. Not checked

- Nothing has run inside the environments' network: Step 0, the real project's build, its profile, its past renames and its size are the operator's to measure.
- The corpus ran here on SQL Server 2022 Developer Edition in a container and DacFx 170.5.96, after every fix of §13.6; CI runs it on SQL Server 2022 Express and LocalDB.
- Whether Copilot in Visual Studio loads the skills waits on the trial.
- CDC is never proven in this repository, since no local server runs SQL Server Agent (`DECISIONS.md` 2026-10-19); the CDC case holds the classification, never a capture instance.
- DacFx's validation loop re-trusts every untrusted constraint of a table that gains an unnamed one; a constraint already untrusted there is not read.
- `doctor`'s SQLCMD, project-reference and platform checks have no test; `--counts` ran only on the corpus's small tables, so its `COUNT_BIG(*)` on a large table is unmeasured; the fast tests ran on Linux only, and the `ping` form of the interrupt test waits for CI's Windows job.
