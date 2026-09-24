# Next — rewritten by the session that finishes something; never appended
Updated 2026-09-23 by #704.

## In flight
- M0 on `claude/v3-build` (#704): WP 0.1 to 0.5 landed; WP 0.6 (the fast tests over the words,
  the budgets and the dependency laws) is on `wp/0.6`.

## Next
- WP 0.7: the published tool folder, the shared SQL Server fixture, `tests/Golden/classic-minimal/`
  and CI on Ubuntu and Windows, the outbound-deny job included.
- M0 exit 3's `estate doctor` stub (`DEGRADED`, a remedy per missing item) has no row: WP 0.4
  owns it, a verb in its table beside the telemetry opt-out; WP 1.7 replaces it.
- Then M1: WP 1.1, 1.3 and 1.8; then 1.2, 1.5 and 1.6; then 1.4; then 1.7.

## Waiting on a person
- The operator, before M0's exit: install `.claude/settings.json`,
  `.claude/hooks/session-start.sh` and `.claude/hooks/session-end.sh` as `V3_MILESTONES.md` WP 0.5
  and §4 describe them; the permission check refuses an agent's edit to its own settings. Until
  then the settings on the branch name v2's hooks, which moved to `archive/v2/hooks/`. Once they
  land, a session adds their rows to `ci/docs.manifest.json` and checks WP 0.5's second Done-when
  in a fresh cloud session.
- S1, the laptop half (a developer): does the classic build run on a team laptop, and does the
  estate's `.sqlproj` reference `master.dacpac`? After WP 0.7, build
  `tests/Golden/classic-minimal/` against `dist/estate/` on the laptop, then search the estate's
  project for `master.dacpac`.
- S3 (a developer, as the developers' group): do DacFx `Script`, `TSqlModel.LoadFromDatabase` and a
  `COUNT_BIG(*)` probe run against Dev, and how long does `LoadFromDatabase` take there? From M1:
  `estate check drift --target env:dev --at <Dev's deployed tag>`.
- S5 (a lead with read on Dev's platform database): which OutSystems 11 metamodel tables and
  columns hold consumer references and publish times? Start from
  `SELECT TABLE_NAME, COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME LIKE 'ossys[_]%';`
- S6 (IT, in the corporate agent's `STATE.md`): do the team laptops and the hosted agents have the
  .NET 10 SDK and Docker? `dotnet --list-sdks` and `docker version` on each.
- S7 (release engineering): which publish profile and which DacFx engine does the Octopus step
  apply? The profile's options only land in `estate/profiles/pipeline.publish.xml`.
- S8 (the dev leads, read-only on Dev, QA and UAT): which SQL Server version and compatibility
  level? `SELECT @@VERSION;` and
  `SELECT compatibility_level FROM sys.databases WHERE name = DB_NAME();` on each.
