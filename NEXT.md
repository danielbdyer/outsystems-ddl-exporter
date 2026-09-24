# Next — rewritten by the session that finishes something; never appended
Updated 2026-09-23 by #704, after M0's exit checks ran on the operator's Windows machine.

## In flight
- M0 on `claude/v3-build` (#704, unpushed and without its pull request): WP 0.1 to 0.7 landed. On
  2026-09-23 exits 1 to 3 pass here: the build is clean and the fast lane green in under a minute;
  v1's unit suites and v2's test suites match the counts in the archive commit's message;
  `estate doctor` prints `DEGRADED`, a remedy per missing item, and claims nothing M1 builds.
- Exit 4 and the outbound-deny job wait on CI: push, open #704 with WP 0.1's counts, then read
  `estate.yml`'s `fast (ubuntu-latest)`, `fast (windows-latest)` and `sql-offline` jobs.

## Next, before M0's exit raises `Contract.Milestone`
- Every `pending M0` in `VALUES.md` resolves, or `ValuesResolve` fails once the milestone rises:
  D3, X5, O2, O10 and L10 by that CI run; X6, A2 and A6 by the operator's settings and hooks.
- `estate.yml`: every job declares `timeout-minutes` and prints its elapsed time (L10); none does.
- WP 0.1 removed `.claude/agents` and `.claude/skills`, which return from M7, from `knowledge/`.
- Engine-CI spikes with no job yet: S1's Windows half (the `fast (windows-latest)` classic build
  answers it into `estate/ledgers/toolchain.md`, which the first answer creates); S2, the guards
  from both engines under `tests/Golden/guards/`, before WP 2.2; S4, a restore timed in the
  container and in LocalDB, before WP 3.4.
- Then M1: WP 1.1, 1.3 and 1.8; then 1.2, 1.5 and 1.6; then 1.4; then 1.7.

## Waiting on a person
- The operator: install `.claude/settings.json` with §4's permissions (`Edit(./archive/**)` denied)
  and the SessionStart and SessionEnd hooks at M0 in `.claude/hooks/`, as WP 0.5 describes; the
  permission check refuses an agent's edit to them. The committed settings still name v2's hooks
  in that directory, where none exists, so each hook call fails. Then a session adds their rows
  to `ci/docs.manifest.json` and checks WP 0.5's second Done-when in a fresh cloud session.
- The operator: `pending M<n>` holds until M<n>'s exit (n ≥ `Contract.Milestone`), as WP 0.6's row
  says; its brief said n > it, which refuses `pending M0` during M0. Confirm, or rule n > it.
- S1, a developer's laptop: build `tests/Golden/classic-minimal/` against `dist/estate/`, then
  search the estate's `.sqlproj` for `master.dacpac`.
- S3, a developer as the developers' group: DacFx `Script`, `TSqlModel.LoadFromDatabase` and a
  `COUNT_BIG(*)` probe against Dev, and how long `LoadFromDatabase` takes there.
- S5, a lead with read on Dev's platform database: `SELECT TABLE_NAME, COLUMN_NAME FROM
  INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME LIKE 'ossys[_]%';`
- S6, IT: `dotnet --list-sdks` and `docker version` on the team laptops and the hosted agents.
- S7, release engineering: the Octopus step's publish profile and DacFx engine.
- S8, the dev leads on Dev, QA and UAT: `SELECT @@VERSION;` and
  `SELECT compatibility_level FROM sys.databases WHERE name = DB_NAME();`
