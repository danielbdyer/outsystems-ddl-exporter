# Next — rewritten by the session that finishes something; never appended
Updated 2026-10-19 by #705: M2 has started on the operator's go, with WP 2.1's first slice; the local server moves to LocalDB and a
native SQL Server Express; the operator's rulings of 2026-10-19 are in `DECISIONS.md` and the plan; spike S2 ran on both DacFx releases.

## The operator's actions
- Run the laptop check against Dev (M1 exit 1, spikes S1 and S3) with the tool folder built at the branch head: in the
  v3 clone, `git switch claude/v3-architecture-design-sl95mx`, `git pull` and `ci/publish.ps1`; then, in the SSDT
  repository's clone and with `<v3 clone>\dist\dbchange\dbchange.exe` as `dbchange`, run
  `dbchange check drift --target env:dev --at <Dev's deployed tag> --json` and `dbchange read --from env:dev --json`,
  and read Dev's collation with
  `sqlcmd -S <Dev's server> -d <Dev's database> -E -Q "SELECT collation_name FROM sys.databases WHERE name = DB_NAME()"`.
  Report on the review page check drift's exit code and its whole JSON answer, which carries its `elapsedMs`, the
  read's `elapsedMs` and the `collation_name`: M1 exit 1 passes on exit 0, or 5 naming each differing object, in a minute.
- At M0, install the SessionStart and SessionEnd hooks in `.claude/hooks/` as `CLAUDE.md` describes them; no agent may edit them.
- Answer the cards still open in round two of the M1 review (linked in #704's description).
- Move or delete the v2 worktrees under archive/v2/.claude/worktrees (3.6 GB), which Visual Studio and Copilot scan.
- Send S5 to S8, and §17 items 21 and 22 of `V3_MILESTONES.md`, to the people below.

## In flight
- WP 2.1's first slice (kernel `Preconditions` and `Outcome.Predict`); the local server's move off Docker; the rulings
  of 2026-10-19 carried into `V3_MILESTONES.md`, `VALUES.md` and `V3_PARITY.md`.

## Next
- WP 2.2 (its reader replaces `BlockOnPossibleDataLossChecks.In` in `Io.Tests: BlockOnPossibleDataLossCheckTests`, held to
  `tests/Golden/data-loss-checks/`), WP 2.3, WP 3.8 and spikes S9 to S11 (their verbs added to `tests/DacFx162/`); then WP 3.4's remainder with spike S4,
  WP 2.5, WP 2.6, WP 3.5 and WP 7.1. WP 2.6 moves the Facsimile's case catalog to `tests/Cases/` (not held yet: WP 2.6). `ParityResolves` prints where the ledger stands.
- `kernel/Environments.cs` LocalServerKind parses the unread key `localServer` as `docker` or `localdb`, and
  `tests/Golden/dbchange/environments.json` writes `docker`; WP 3.4 drops the key from both and from EnvironmentsTests and PublishProfilesTests.
- Before M5 and M6, correct the plan where it contradicts itself: WP 5.2's page cells, the vendoring verb's two
  names, R18's exit, who writes deployments.md, and the gate's wiki cells.

## Waiting on others
- S5, a lead who reads Dev's platform database: which `ossys_` tables and columns hold consumer references and publish times.
- S6, whoever manages the machines (laptops run LocalDB 16.x, the environments' network no Docker): open are the .NET 10 SDK
  on laptops and hosted agents, sudo there for `ci/sql.sh up`, the Octopus workers' .NET 10 runtime, and a package feed (§17 item 22).
- S7, whoever owns the deployment step: the publish profile, the DacFx version (which decides the Query Store state of a database whose
  project declares none: OFF on 162.5.57, READ_WRITE on 170.5.96), and the Octopus login's language and DATEFORMAT.
- S8, the dev leads: Dev's, QA's and UAT's SQL Server version, compatibility level, RCSI and collation.
- The owners of the directory groups: a login holding only `VIEW DEFINITION` on Prod, and on QA and UAT for any
  unattended reader, asked for by that exact grant (§17 item 21).
