# Next — rewritten by the session that finishes something; never appended
Updated 2026-10-24 by #705: dbchange-lite's review fixes, must-fix and should-fix (`DBCHANGE_LITE.md` §13.6); before them, the tool built under `tools/dbchange-lite/` (§14) and wave 7.

## The operator's actions
- The laptop check against Dev (M1 exit 1, spikes S1 and S3) waits until the operator brings the branch to a laptop: in the v3 clone,
  `git switch claude/v3-architecture-design-sl95mx`, `git pull` and `ci/publish.ps1`; then, in the SSDT repository's clone, with
  `<v3 clone>\dist\dbchange\dbchange.exe` as `dbchange`, run `dbchange check drift --target env:dev --at <Dev's deployed tag> --json` and
  `dbchange read --from env:dev --json`, and, on Dev's database under your own login, `SELECT collation_name FROM sys.databases WHERE name = DB_NAME()`.
  Report check drift's exit code and its JSON answer (its `elapsedMs`), the read's `elapsedMs` and the `collation_name`: M1 exit 1 passes on exit 0, or 5 naming each differing object, in a minute.
- dbchange-lite's Step 0 on a work laptop and Dev (`DBCHANGE_LITE.md` §14), and its §15 decisions, each with the default the build took.
- At M0, install the SessionStart and SessionEnd hooks in `.claude/hooks/` as `CLAUDE.md` describes them; no agent may edit them.
- Answer the cards still open in round two of the M1 review (linked in #704's description).
- Move or delete the v2 worktrees under archive/v2/.claude/worktrees (3.6 GB), which Visual Studio and Copilot scan.
- Send S5 to S8, and §17 items 21 and 22 of `V3_MILESTONES.md`, to the people below.
- Run the sql-windows job on the branch: DacFx 162.5.57 plans LocalDB 17.0 as Sql160, and no run of the 162 half there is measured.

## Next
- dbchange-lite: L8's edits to the skills as a vendoring patch for the SSDT repository; L9, the MCP adapter, after the trial (`DBCHANGE_LITE.md` §14); the review's nits, untriaged.
- WP 2.5's predict chains `Vocabulary.Read` and `Coordinates.Of` (a `Declared` takes a person's state; an unclassified statement goes
  to proof), reads the checks with `BlockOnPossibleDataLoss.Read` and their sites into `Preconditions.Of`, writes the existing data
  `up` reads with its trust list, and predicts as `Io.Tests: SampleChangePredictionTests` does for the corpus, whose INSERT scripts
  the planter's copies replace in M3. Then WP 3.6's `synthetic-copy check`, `status`, `down` and O8's scale test; WP 3.7's `profile`.
- S4's laptop half, the SSDT repository's own schema on LocalDB 16.x, waits on S6; its CI half is a decision line.
- WP 7.2 rewrites each moved skill's citations to the forms `Citations` accepts (no rewrite map is kept). Of v2's skills/ and agents/,
  one passing block still cites v2 outside the words `Register.Prose` refuses, add-index's line 29, by a bare §19 (the handbook's
  anti-pattern chapter); WP 7.2 reads by hand each plain use of intake, decompose, adversary and reviewer, v2's names only by sense.
- The `verify` cases of `tests/Cases/` that S9 to S11 settle still cite no test: PLT-87, 88, 90, QST-55, 59, OP-toggle-trust-03 to 05,
  OP-add-default-04, OP-temporal-convert-04, INV-H6-01, CLS-14, REC-19; each to cite `Io.Tests: SpikeReceiptTests`; then M2's exit checks.
- Before M5 and M6, correct the plan where it contradicts itself: WP 5.2's page cells, the vendoring verb's two names, R18's exit,
  who writes the deployments file beside the wiki page, and the gate's wiki cells. `ParityResolves` prints where the ledger stands.

## Waiting on others
- S5, a lead who reads Dev's platform database: which `ossys_` tables and columns hold consumer references and publish times.
- S6, whoever manages the machines (laptops run LocalDB 16.x, the environments' network no Docker): open are the .NET 10 SDK
  on laptops and hosted agents, sudo there for `ci/sql.sh up`, the Octopus workers' .NET 10 runtime, and a package feed (§17 item 22).
- S7, whoever owns the deployment step: the publish profile, the DacFx version (which decides the Query Store state of a database whose
  project declares none: OFF on 162.5.57, READ_WRITE on 170.5.96), and the Octopus login's language and DATEFORMAT.
- S8, the dev leads: Dev's, QA's and UAT's SQL Server version, compatibility level, RCSI and collation.
- The owners of the directory groups: a login holding only `VIEW DEFINITION` on Prod, and on QA and UAT for any
  unattended reader, asked for by that exact grant (§17 item 21).
