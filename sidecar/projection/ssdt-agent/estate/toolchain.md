# Toolchain pins — the versions proving must match

The estate pipeline (Azure DevOps → Octopus) publishes with a specific DacFx
configuration; local proving is trustworthy only when its engine matches,
because publish-guard behaviour is version-bound — auto-trust on a declarative
FK add read differently on sqlpackage 170.4.83.3 than on DacFx 162.5.57
(`../FINDINGS_AND_CHANGES.md` Part 5, the open pin item). This file is the
single place the pins live: the web SessionStart hook reads the sqlpackage
row before installing; skills and CI cite this file rather than restating
numbers.

Until a row is pinned, the tool installs at latest, the hook's status line
carries `-unpinned`, and every proof record stamps the version actually run.

| tool | pinned version | source of truth | recorded | notes |
|---|---|---|---|---|
| sqlpackage | UNPINNED | the estate pipeline's DacFx publish step — record its version here | — | latest installs while unpinned; align to the pipeline's DacFx before trusting trust-state findings |
| DacFx (estate pipeline) | UNPINNED | the Azure DevOps → Octopus publish task and its XML flags | — | when known: pin here, mirror the flags into `../proving-ground/profiles/ProvingGround.Pipeline.publish.xml`, verify auto-trust once (`is_not_trusted = 0` on a real publish), and resolve the `../FINDINGS_AND_CHANGES.md` Part 5 open item |
| Microsoft.SqlServer.DacFx (Twin corpus) | 162.5.57 | `../../src/Projection.Pipeline/Projection.Pipeline.fsproj`, `../../src/Projection.Targets.SSDT/Projection.Targets.SSDT.fsproj` | 2026-08-22 | the parallel proof corpus's engine; the live sqlpackage engine is authoritative where they diverge |
| Microsoft.Build.Sql (sqlproj SDK) | 2.2.0 | `../proving-ground/SampleCatalog.sqlproj` | 2026-07-02 | bump only on a real build-failure trigger |
| SQL Server image (warm container) | 2022-latest (floating) | `../../scripts/warm-sql.sh` (`WARM_SQL_IMAGE` override) | — | pin to a CU/digest tag at cutover; a floating tag under version-stamped guard evidence is a named risk |

Discipline:

- A pin change is part of the change that needs it, never a drive-by: the
  sqlpackage row is a one-place edit the hook reads on the next session; the
  Twin DacFx row is a two-fsproj edit that must ride with a green proof-lane
  run; either lands with a dated note here.
- A finding proven on one version does not transfer silently to another.
  When a pin changes, the version-bound findings (`../FINDINGS_AND_CHANGES.md`
  Part 2, the trust-state family especially) are re-proven or re-stamped
  before records cite them.
