# Toolchain pins — the versions proving must match

The estate's pipeline (Azure DevOps → Octopus) publishes with one specific DacFx
version and one set of flags. Local proving can be trusted only when it runs the
same DacFx version the pipeline runs, because the publish guard behaves
differently across versions. One example: adding a foreign key ends up trusted
automatically on sqlpackage 170.4.83.3, but the same add read as untrusted on
DacFx 162.5.57 (`../FINDINGS_AND_CHANGES.md` Part 5). This file is the one place
the versions are pinned. The web SessionStart hook reads the sqlpackage row
before it installs the tool; skills and CI cite this file instead of repeating
version numbers.

Until a row is pinned, the tool installs at the latest version, the hook's
status line carries `-unpinned`, and every proof record stamps the version it
actually ran.

| tool | pinned version | source of truth | recorded | notes |
|---|---|---|---|---|
| sqlpackage | UNPINNED | the estate pipeline's DacFx publish step — record its version here | — | latest installs while unpinned; align to the pipeline's DacFx before trusting trust-state findings |
| DacFx (estate pipeline) | UNPINNED | the Azure DevOps → Octopus publish task and its XML flags | — | when known: pin here, mirror the flags into `../proving-ground/profiles/ProvingGround.Pipeline.publish.xml`, verify auto-trust once (`is_not_trusted = 0` on a real publish), and resolve the `../FINDINGS_AND_CHANGES.md` Part 5 open item |
| Microsoft.SqlServer.DacFx (Twin corpus) | 162.5.57 | `../../src/Projection.Pipeline/Projection.Pipeline.fsproj`, `../../src/Projection.Targets.SSDT/Projection.Targets.SSDT.fsproj` | 2026-08-22 | the parallel proof corpus's engine; the live sqlpackage engine is authoritative where they diverge |
| Microsoft.Build.Sql (sqlproj SDK) | 2.2.0 | `../proving-ground/SampleCatalog.sqlproj` | 2026-07-02 | bump only on a real build-failure trigger |
| SQL Server image (warm container) | 2022-latest (floating) | the monorepo's `warm-sql.sh` (`WARM_SQL_IMAGE` override) | — | pin to a CU/digest tag at cutover; a floating tag under version-stamped guard evidence is a named risk |

Discipline:

- Change a pin only as part of the change that needs it, not on its own.
  Updating the sqlpackage row is a one-line edit here, which the hook reads at
  the next session. Updating the Twin's DacFx version means editing two
  `.fsproj` files, and that edit has to ship together with a green proof-lane
  run. Either way, add a dated note in this file.
- A finding proven on one version does not automatically hold on another. When
  a pin changes, re-prove or re-stamp the findings that depend on the version
  before any record cites them — especially the trust-state findings in
  `../FINDINGS_AND_CHANGES.md` Part 2.
