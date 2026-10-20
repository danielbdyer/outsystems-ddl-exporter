# Next — rewritten by the session that finishes something; never appended
Updated 2026-10-20 by #705, after wave 5 of the build. Wave 4 landed WP 3.1 (kernel `Schema` and `Order`), WP 2.3 (`io/AggregateQueries.cs`
and its partial files), WP 2.4 (kernel `Classify`), spike S2 (DacFx 162.5.57 beside 170.5.96, `tests/DacFx162/`) and io's refusal of
DacFx's `xp_msver` call, so the read-only principal sends no `EXEC`. Wave 5 lands WP 2.2 (the BlockOnPossibleDataLoss reader), WP 3.8
(kernel `Witness`), spikes S9 to S11 with check drift's warning for a check written with IN, and the review findings of waves 3 and 4.

## The operator's actions
- The laptop check against Dev (M1 exit 1, spikes S1 and S3) waits until the operator brings the branch to a laptop:
  in the v3 clone, `git switch claude/v3-architecture-design-sl95mx`, `git pull` and `ci/publish.ps1`; then, in the SSDT
  repository's clone, with `<v3 clone>\dist\dbchange\dbchange.exe` as `dbchange`, run
  `dbchange check drift --target env:dev --at <Dev's deployed tag> --json` and `dbchange read --from env:dev --json`,
  and `sqlcmd -S <Dev's server> -d <Dev's database> -E -Q "SELECT collation_name FROM sys.databases WHERE name = DB_NAME()"`.
  Report check drift's exit code and its JSON answer (its `elapsedMs`), the read's `elapsedMs` and the `collation_name`:
  M1 exit 1 passes on exit 0, or 5 naming each differing object, in a minute.
- At M0, install the SessionStart and SessionEnd hooks in `.claude/hooks/` as `CLAUDE.md` describes them; no agent may edit them.
- Answer the cards still open in round two of the M1 review (linked in #704's description).
- Move or delete the v2 worktrees under archive/v2/.claude/worktrees (3.6 GB), which Visual Studio and Copilot scan.
- Send S5 to S8, and §17 items 21 and 22 of `V3_MILESTONES.md`, to the people below.
- Run the sql-windows job on the branch: DacFx 162.5.57 plans LocalDB 17.0 as Sql160, and no run of the 162 half there is measured.

## Next
- WP 2.1's second slice: the statement kinds the plan's §4 shapes name, with the four refinements of spike S2's decision lines.
- WP 3.4's remainder with spike S4; it drops `kernel/Environments.cs`' unread key `localServer` (`docker` or `localdb`) and
  `tests/Golden/dbchange/environments.json`'s `docker`, with EnvironmentsTests and PublishProfilesTests.
- WP 7.1, the citations rewritten. Then WP 2.5, which reads the checks with `BlockOnPossibleDataLoss.Read` and their sites into
  `Preconditions.Of`; WP 2.6, which moves the Facsimile's case catalog to `tests/Cases/` (not held yet: WP 2.6) and re-reads against
  `Io.Tests: SpikeReceiptTests` the cases S9 to S11 bear on: PLT-87, 88, 90, QST-55, 59, OP-toggle-trust-03 to 05, OP-add-default-04,
  OP-temporal-convert-04, INV-H6-01, CLS-14, REC-19; WP 3.5, whose identity step calls `CheckWrittenWithIn.Of`; then M2's exit checks.
- Before M5 and M6, correct the plan where it contradicts itself: WP 5.2's page cells, the vendoring verb's two
  names, R18's exit, who writes deployments.md, and the gate's wiki cells. `ParityResolves` prints where the ledger stands.

## Waiting on others
- S5, a lead who reads Dev's platform database: which `ossys_` tables and columns hold consumer references and publish times.
- S6, whoever manages the machines (laptops run LocalDB 16.x, the environments' network no Docker): open are the .NET 10 SDK
  on laptops and hosted agents, sudo there for `ci/sql.sh up`, the Octopus workers' .NET 10 runtime, and a package feed (§17 item 22).
- S7, whoever owns the deployment step: the publish profile, the DacFx version (which decides the Query Store state of a database whose
  project declares none: OFF on 162.5.57, READ_WRITE on 170.5.96), and the Octopus login's language and DATEFORMAT.
- S8, the dev leads: Dev's, QA's and UAT's SQL Server version, compatibility level, RCSI and collation.
- The owners of the directory groups: a login holding only `VIEW DEFINITION` on Prod, and on QA and UAT for any
  unattended reader, asked for by that exact grant (§17 item 21).
