# Decisions — one line each; the reasoning is in the pull request
2026-09-23 · v3 is C#; v2's F# is the specification it is ported from · #703
2026-09-23 · the lifecycle comes first (M1 to M6); the wing (`emit`, `decide`, `move` and the full OSSYS reader) waits for a consumer · #703
2026-09-23 · `predict` joins the lifecycle's verbs and `knowledge` is counted among them; `read --from ossys` moves to the wing · #703
2026-09-23 · git is the only store: every artifact is derived locally from committed inputs and cached by fingerprint, and `twin bake`, `twin restore` and `twin gc` do not exist · #703
2026-09-23 · `net10.0` throughout; the tool is published framework-dependent, and a machine that builds a `.sqlproj` needs the .NET 10 SDK · #703
2026-09-23 · DacFx 170.5.96 is the committed engine until the Octopus step's engine is pinned (S7); every receipt stamps it · #703
2026-09-23 · Docker with the SQL Server image pinned by tag and digest is the substrate; LocalDB is the fallback where Docker is absent, and CDC there reads not provable · #703
2026-09-23 · the SessionStart hook starts Docker and the SQL Server container in remote sessions only, an exception to "no hook starts a daemon" · #703
2026-09-23 · `global.json` pins the 10.0.4xx SDK band with `rollForward: latestPatch` (v1 and v2 pin 9.0.314 with roll-forward disabled) · #704
2026-09-23 · ScriptDom 180.102.0 and Microsoft.Data.SqlClient 6.1.5 are pinned to DacFx 170.5.96's own dependencies · #704
2026-09-23 · `NuGet.config` stays at the root, shared by v1, v2 and v3 · #704
2026-09-23 · `archive/` stays readable until M8, because v2 is the specification ports read; editing it is denied · #704
2026-09-23 · the v2 worktrees under the old sidecar path moved with the archive · #704
2026-09-23 · `Refusal` is a sealed record class, not a struct, because a struct's default would be a refusal with no remedy · #704
2026-09-23 · `Name` compares ordinally with case, so the kernel never loses a case-only difference; a case-insensitive match is the caller's explicit choice · #704
2026-09-23 · `Fingerprint.Of(string)` hashes canonical text (a leading BOM dropped, CRLF and CR to LF, UTF-8); `Of(bytes)` hashes bytes as given · #704
2026-09-23 · `Kernel.Tests` alone runs with invariant globalization off, so a test can show the kernel ignores culture · #704
2026-09-23 · JsonSchema.Net validates the CLI contract's schemas, in tests only · #704
2026-09-23 · `ci/budgets.json` counts physical lines by include globs, `bin/` and `obj/` never; planned figures are data and never fail a build · #704
2026-09-23 · `Contract.Milestone` names the milestone in progress and each exit raises it by one: `pending M<n>` holds until M<n>'s exit (n ≥ it), and `pending W` and the root design documents' exclusion end as M8 starts · #704
2026-09-23 · the reference stub (`mscorlib.dll` and `FrameworkList.xml`) comes from Microsoft.NETFramework.ReferenceAssemblies.net472 1.0.3 through a `PackageDownload` in `cli`, its exact version on the item because central package management cannot pin a `PackageDownload`, and `ci/publish` copies it into `dist/estate/refasm/` · #704
2026-09-23 · `Io.Tests` also runs with invariant globalization off, because Microsoft.Data.SqlClient refuses invariant mode, so the earlier line saying `Kernel.Tests` alone does no longer holds · #704
2026-09-23 · the `estate-sql` container's SA password is generated once per machine into `~/.estate/sql.env`, outside every checkout so all worktrees share one container, and no output of `ci/sql.sh` or `ci/sql.ps1` prints it · #704
2026-09-23 · M0's exit 2 compares counts from a run with nothing else building beside it, since v1's `Osm.Cli.Tests` failed one test more once under a concurrent build and matched on re-runs · #704
2026-09-24 · a refusal's exit is found by its code's area in one table, `Contract.RefusalExits`; io names what it refused and never an exit · #704
2026-09-24 · `io/Ssdt.Build` writes under `.estate/build/<inputs' fingerprint>/` until WP 1.6 names the folder by a ref's commit, and builds with `--no-restore -nodeReuse:false` so no MSBuild process holds `dist/estate/` open · #704
2026-09-24 · the tool folder is the running estate's own when it carries the targets, else `ESTATE_TOOL` (refused when it lacks them), else the nearest `dist/estate/` · #704
2026-09-24 · the deploy scripts are elements `PreDeploymentScript` and `PostDeploymentScript` keyed `[PreDeploy]` and `[PostDeploy]`; a refactorlog entry is a `RefactorLogOperation` keyed by its operation key · #704
2026-09-24 · `Change.Between` takes renames as key pairs io derives from the refactorlog; a read with two elements on one key is refused (`change.duplicate-key`) · #704
2026-09-24 · the proving ground is committed in classic form under `tests/Golden/proving-ground/`, v2's objects and scripts byte for byte; its twin files and extra folders stay in the archive until a work package needs them · #704
2026-09-24 · the fixture's read-only principal is a SQL login holding VIEW DEFINITION and db_datareader on its database, its connection string only in `.estate/principals/` behind a `file:` reference · #704
2026-09-24 · a DacFx package is loaded from a stream, never by path, so a publish does not hold the assemblies beside the dacpac in the calling process · #704
2026-09-24 · the walk reads a module's body (a procedure's, a function's, a trigger's), which DacFx holds in no property, as the element property `Definition` from `TSqlObject.TryGetScript`, for each type with a `BodyDependencies` relationship and no script-typed property · #704
2026-09-24 · VALUES.md X1's "a profile" clause, and M1 exit 7's, reads as WP 1.5 has it: a profile holding `Password=`, as written or as DacFx reads it, or giving a SQLCMD value that is a connection string, is exit 6, while a password-free `TargetConnectionString` or `TargetDatabaseName` is removed before DacFx reads the profile and never refused · #704
2026-09-24 · `Budgets.Tests` also runs with invariant globalization off, because DacFx builds no model in invariant mode and `Register.Refusals` walks one to reach `walk.duplicate-key` and `refactorlog.name`, which WP 1.2 added before WP 1.5 asked every refusal for a way to it · #704
2026-09-24 · `io/Git.At` checks a ref out detached at `.estate/worktrees/<commit>/`, one lock file per holding process; every At first sweeps what no running process holds · #704
2026-09-24 · a build of a ref writes under `.estate/build/<commit>/` · #704
2026-09-24 · git's refusal areas exit as `ref` 1, `origin` 4, `git` 6, `branch` 9 · #704
2026-09-24 · `Git.CommitAndPush` commits on a temporary index and pushes create-only with the caller's own credential helper; a failed push deletes the local branch · #704
2026-09-24 · the git executable is a parameter of `io/Git`, so no test mutates the process's PATH · #704
2026-09-24 · R15 reads each environment's server from its reference whether or not it names a database, refuses a reference SqlClient cannot read and an estate without `estate/posture.json`, and compares hosts by spelling and then by DNS address, this machine being every loopback and local address; an environment whose reference resolves to nothing on this machine goes uncompared; a copy's row in `.estate/copies.json` records its server, and `copy:` resolves only while the substrate is that server · #704
2026-09-24 · the read side's refusal areas exit as target 1, registry 2, server 4, substrate 4, connection 6, twin 6, copy 9 and probe 9 · #704
2026-09-24 · a named environment's SQL Server messages are withheld whatever its classification; a copy's failed statement keeps its message, because a copy's rows are minted · #704
2026-09-24 · beneath the probe allowlist's answer forms only LEN, DATALENGTH, UPPER, LOWER, LTRIM, RTRIM, ISNULL, ABS, COALESCE, NULLIF, TRY_CONVERT and TRY_CAST are admitted; CONVERT and CAST are refused because their errors quote the value · #704
2026-09-24 · an admitted probe runs as ScriptDom writes its checked tree back, so no comment or batch separator reaches SQL Server · #704
2026-09-24 · `io/Substrate` chooses the substrate's server (ESTATE_SQL, the estate-sql container, LocalDB) and the fixture uses its choice; a copy is registered before its database is made, and `copy:` resolves only on the server its row records · #704
2026-09-24 · R15 compares a substrate's host with every environment's server by spelling and then by address, this machine being every loopback and local address · #704
