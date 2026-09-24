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
| sqlpackage | UNPINNED | the estate pipeline's DacFx publish step — record its version here | — | latest installs while unpinned; align to the pipeline's DacFx before trusting trust-state findings. Constraint-trust re-confirmed on 170.5.76 (2026-08-28): four declarative FK adds — three on empty children, one over a populated child (`FK_CustomerAddress_Customer_CustomerId`, 5 rows) and one over a populated child in the same release as its pre-deploy reconcile (`FK_Order_Status_StatusId`) — and one CHECK add all landed `is_not_trusted = 0` on live publishes (`../sample-prs/compound/`, `../sample-prs/drop-check.md`). The estate pipeline's own version stays the open item |
| DacFx (estate pipeline) | UNPINNED | the Azure DevOps → Octopus publish task and its XML flags | — | when known: pin here, mirror the flags into `../proving-ground/profiles/ProvingGround.Pipeline.publish.xml`, verify auto-trust once (`is_not_trusted = 0` on a real publish), and resolve the `../FINDINGS_AND_CHANGES.md` Part 5 open item. Partial read 2026-08-28: the pipeline's build side is VS 2022 Enterprise / MSBuild 17.0 (row below) — that does NOT pin this row; the publish guard runs whatever DacFx executes the Octopus deploy step. Where to read it: (a) any recent deploy log — SqlPackage prints its version banner near the top, or the step template names its DacFx path; (b) on the Octopus worker or target, `sqlpackage /version`, or the file version of `Microsoft.SqlServer.Dac.dll` in the step's tool folder; (c) if the deploy runs from the VS 2022 install itself, the bundled engine sits at `Common7\IDE\Extensions\Microsoft\SQLDB\DAC\`. HYPOTHESIS to confirm or refute with that one look: a VS-2022-bundled engine is the 162.x family — the family where a declarative FK add read UNTRUSTED (Part 5, on 162.5.57) — while local proving ran 170.5.76, where the same add lands trusted. If the pipeline publishes on 162.x, re-stamp the trust-state findings on that engine before any record cites them upward |
| MSBuild (pipeline build agent) | 17.0 — VS 2022 Enterprise | the Azure DevOps build definition (owner-observed) | 2026-08-28 | the BUILD side only: it pins the SSDT build-targets era that produces the dacpac. The dacpac is a model artifact — guard behavior, trust semantics, and script generation are decided by the publish-side DacFx (row above), which stays the open pin |
| Microsoft.SqlServer.DacFx (Twin corpus) | 162.5.57 | `../../src/Projection.Pipeline/Projection.Pipeline.fsproj`, `../../src/Projection.Targets.SSDT/Projection.Targets.SSDT.fsproj` | 2026-08-22 | the parallel proof corpus's engine; the live sqlpackage engine is authoritative where they diverge |
| Microsoft.Build.Sql (sqlproj SDK) | 2.2.0 | `../proving-ground/SampleCatalog.sqlproj` | 2026-07-02 | bump only on a real build-failure trigger |
| SQL Server image (warm container) | 2022-latest (floating) | `../../scripts/warm-sql.sh` (`WARM_SQL_IMAGE` override) | — | pin to a CU/digest tag at cutover; a floating tag under version-stamped guard evidence is a named risk |

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
