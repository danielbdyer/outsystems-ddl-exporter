# Next — rewritten by the session that finishes something; never appended
Updated 2026-09-24 by #704, after M1's exit checks ran on the operator's Windows machine at `27db885f`.

## In flight
- M0 and M1 on `claude/v3-build` (#704, the tip unpushed): WP 1.1 to 1.8 landed. Both close when the
  operator's settings land (below) and CI is green on the tip; `Contract.Milestone` stays 0 until then.
- 2026-09-24, here: a clean build, no warnings; fast lane green in 1 m 44 s, fixture lane in 3 m 12 s on
  `estate-sql`; `ci/laws.sh` and `ci/laws.ps1` rewrite `LAWS.md` byte for byte. M1's exits 2 to 8 pass,
  exit 2 also by hand (`estate diff --from ref:main --to ref:HEAD` on a scratch make-mandatory repository).
- CI last ran on `c804ad04` (run 36006435223, every job green, LocalDB included), before WP 1.7: push the
  tip and read `estate.yml`'s jobs, where `DriftTests` and `DiffTests` (exits 2, 3, 6, 7, 8) have never run.

## Next, before `Contract.Milestone` rises past M1
- `VALUES.md` rows still `pending M1` fail `ValuesResolve` then: S2's `Kernel.Tests: "no arm returns success
  by default"` and O11's `Io.Tests: "a large change renders short"` (`truncated`, `full`, `--summary`, in no
  schema yet) are unbuilt; A2's `estate doctor` is built, and its row re-points to `DoctorTests`.
- D3, X5, O2, O10 and L10 still read `pending M0`, though run 36006435223 answered them (both operating
  systems, the outbound-deny job, `timeout-minutes` and elapsed time on every job): re-point each.
- S1's Windows half, answered by `fast (windows-latest)`'s classic build, goes into `estate/ledgers/toolchain.md`,
  which the first answer creates; WP 1.1's `vswhere` route is owed only if S1's laptop half needs it.
- Spikes with no CI job yet: S2 (guards from both engines under `tests/Golden/guards/`, before WP 2.2);
  S4 (a restore timed in the container and in LocalDB, before WP 3.4). Then M2.

## Waiting on a person
- The operator: `.claude/settings.json` with §4's permissions (`Edit(./archive/**)` denied), and the
  SessionStart and SessionEnd hooks at M0 in `.claude/hooks/` (VALUES X6, A2, A6), which no agent may edit;
  today's settings call v2's missing hooks. A session then adds their manifest rows and checks WP 0.5's
  second Done-when in a fresh cloud session.
- The operator: confirm `pending M<n>` holds until M<n>'s exit (n ≥ `Contract.Milestone`), or rule n > it.
- S3 and M1 exit 1, a developer on the corporate network as the developers' group, in a clone of the
  estate: `estate check drift --target env:dev --at <Dev's deployed tag>`, expecting exit 0 "env:dev
  matches <tag>" or 5 naming each object, within a minute (no test times it); and `LoadFromDatabase`'s time.
- S1, a developer's laptop: build `tests/Golden/classic-minimal/` against `dist/estate/`, then search
  the estate's `.sqlproj` for `master.dacpac`.
- S5, a lead with read on Dev's platform database: `SELECT TABLE_NAME, COLUMN_NAME FROM
  INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME LIKE 'ossys[_]%';`
- S6, IT: `dotnet --list-sdks` and `docker version` on the team laptops and the hosted agents.
- S7, release engineering: the Octopus step's publish profile and DacFx engine.
- S8, the dev leads on Dev, QA and UAT: `SELECT @@VERSION;` and
  `SELECT compatibility_level FROM sys.databases WHERE name = DB_NAME();`
