# self-test — PROTOCOL (parallel-safe, idempotent, localized execution)

> This protocol is run by MANY executor subagents IN PARALLEL, each taking ONE test entry from
> `prompts.md` / the test matrix and proving it on the proving ground. The authored tree under
> `ssdt-agent/proving-ground/` is **immutable and shared** — read-only for every executor. The
> warm SQL container (`projection-mssql-warm`, `localhost,11433`) is **shared but safely so**:
> each executor owns a **distinct database** and a **distinct scratch copy** of the project, so
> two executors never touch the same `.sql`, the same `bin/`, or the same DB. There is **NO
> wrapper script** — you, the executor, run these commands yourself, in order. The whole point
> of the tree is *classify-by-proving*; this protocol is how a hundred provers run at once
> without colliding.

## Why isolation at all (the three collisions this prevents)

1. **Shared `.sql` edit collision.** Every proof EDITS a `CREATE` (`Modules/*.sql`), or the
   seed (`Data/Seed.sql`), or `Script.PreDeployment.sql`. If two executors edited the authored
   tree's files, executor B would build executor A's half-finished edit into its dacpac and
   prove the wrong thing. **Fix: each executor edits only its OWN scratch copy.** The authored
   tree is never written.
2. **Shared DB publish collision.** `sqlpackage /Action:Publish` mutates a whole database. Two
   executors publishing to one DB would interleave DDL, see each other's block, and corrupt each
   other's BEFORE/AFTER hashes. **Fix: a UNIQUE database name per executor**, passed as
   `/TargetDatabaseName:PG_<testId>_<rand>` on every `sqlpackage` call — this OVERRIDES the
   profile's `Initial Catalog`, so no two executors ever share a DB.
3. **Shared `bin/` race.** `dotnet build` writes `bin/Release/SampleCatalog.dacpac`. Two builds
   into one `bin/` race on the same file and one reads a torn dacpac. **Fix: build inside the
   scratch copy**, which has its own `bin/` — no shared build output.

The warm container itself is shared on purpose (warming it per-executor would be wasteful and
trip survival rule 2). Isolation lives at the **database** and **filesystem-copy** grain, not
the instance grain.

## 0 — The runtime environment (REQUIRED, every shell)

`sqlpackage` is a .NET-8 dotnet tool and must find a runtime before it starts. The block below is
**one developer's box** — a .NET-8 tool on a .NET-9 runtime at a non-standard Windows path,
invoked from Git Bash — kept verbatim as the worked example. Export its equivalents in **every**
shell you call `sqlpackage` from, or it fails to start:

```bash
export DOTNET_ROOT="C:/Users/danny/AppData/Local/Microsoft/dotnet"
export DOTNET_ROLL_FORWARD=Major
export MSYS_NO_PATHCONV=1   # REQUIRED on Git Bash for sqlpackage /Action: args AND docker-exec /opt/... paths
```

`sqlpackage` lives at `C:\Users\danny\.dotnet\tools\sqlpackage.exe`. The host has **no
sqlcmd** — issue SQL through the container:

```bash
docker exec -i projection-mssql-warm /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Projection@Strong1' -C -Q "<sql>"
```

(`MSYS_NO_PATHCONV=1` keeps Git Bash from mangling the `/opt/...` path and the `/Action:` /
`/SourceFile:` switches.)

**The portable form (any box that is not that one).** Three concerns settle the setup — a runtime
for the tool, `sqlpackage` on the path, and Git Bash's path rewriting — and each resolves
differently off Windows. On Linux or macOS:

```bash
export DOTNET_ROOT=/root/.dotnet        # or wherever the local dotnet root is
export DOTNET_ROLL_FORWARD=Major        # still needed: a .NET-8 tool on a newer (.NET-9) runtime
# sqlpackage is expected on PATH — a global `dotnet tool install` puts it there; no absolute .exe path
# no MSYS_NO_PATHCONV off Git Bash — it only stops Git Bash rewriting /Action: and /opt/... paths, and is inert elsewhere
```

Stated plainly: point `DOTNET_ROOT` at the real dotnet root, put `sqlpackage` on PATH, and drop
`MSYS_NO_PATHCONV` on any shell that is not Git Bash. The rest of this protocol — the
`docker exec ... sqlcmd` line, every `/Action:` switch, every `/TargetDatabaseName:"$DB"` — is
identical on every box.

**Reading the result of a publish — never trust `$?` alone.** A blocked `sqlpackage` publish does
not reliably exit non-zero. Piped through `| tail`, or any pipeline, the shell reports the exit
status of the pipe's last stage, so a blocked publish reads as exit 0 and a naive `$?` check calls
it a success. The block lives in the **text**, not the exit code: parse the output for `Could not
deploy package` (and the `Msg` / `BlockOnPossibleDataLoss` lines beneath it), and treat that
string — never `$?` — as the signal that the deployment was blocked. This matters most at §6 step
3 (the block check) and at the make-mandatory gate, where the whole finding turns on whether the
publish was blocked.

## 1 — Pick your identity (do this first, once)

```bash
TESTID="COL-03"                        # the test-matrix id you are proving
RAND=$(openssl rand -hex 4)            # 8 hex chars; uniqueness across parallel executors
DB="PG_${TESTID//-/_}_${RAND}"         # e.g. PG_COL_03_9f2a1c4e  (no dashes — valid DB identifier)
SCRATCH="$CLAUDE_SCRATCHPAD/pg-${TESTID}-${RAND}"   # your private scratch dir
SRC="C:/Users/danny/code/outsystems-ddl-exporter/sidecar/projection/ssdt-agent/proving-ground"
```

`DB` and `SCRATCH` both carry `TESTID` + `RAND`, so they are globally unique among parallel
executors. **Every** mutating command below targets one of these two — never `$SRC`, never the
profile's default `Initial Catalog`.

**Resolve your identity ONCE and reuse the literal values.** Each command run is a fresh shell
process — exported variables do **not** persist between separate runs. So either run the whole
protocol as ONE shell session, or compute `DB`/`SCRATCH` once and paste the **literal** resolved
names into every later command. Never re-run `openssl rand` in a later step: you would seed one
DB (`PG_X_aaaa`) and prove against another (`PG_X_bbbb`), leaking the first. (When an orchestrator
dispatches you, it will hand you a fixed `DB`/`SCRATCH` to use verbatim — prefer that.)

## 2 — Copy the proving ground to your private scratch (read-only source preserved)

```bash
mkdir -p "$SCRATCH"
cp -R "$SRC/." "$SCRATCH/"
rm -rf "$SCRATCH/bin" "$SCRATCH/obj"   # start from a clean build dir you alone own
```

From here, **ALL edits — `Modules/*.sql`, `Data/Seed.sql`, `Script.PreDeployment.sql`, the
`.refactorlog` — happen in `$SCRATCH` ONLY.** The authored `$SRC` tree is never modified. If
you ever find yourself opening a path under `$SRC` for writing, STOP — that is the collision
this protocol exists to prevent.

## 3 — Create your unique database (idempotent: drop-if-exists first)

```bash
docker exec -i projection-mssql-warm /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Projection@Strong1' -C -Q \
  "IF DB_ID('$DB') IS NOT NULL BEGIN ALTER DATABASE [$DB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DB]; END; CREATE DATABASE [$DB];"
```

Drop-then-create makes step 3 **idempotent**: a re-run from a crashed prior attempt yields the
same fresh DB and leaks nothing.

## 4 — Build the dacpac inside your scratch copy (your own bin/, no shared race)

```bash
dotnet build "$SCRATCH/SampleCatalog.sqlproj" -c Release
# -> $SCRATCH/bin/Release/SampleCatalog.dacpac
```

## 5 — Establish the BEFORE state on YOUR database

Publish the unedited (scratch) project + seed to your unique DB, overriding the profile catalog
with `/TargetDatabaseName:$DB` on **every** call:

```bash
sqlpackage /Action:Publish \
  /SourceFile:"$SCRATCH/bin/Release/SampleCatalog.dacpac" \
  /Profile:"$SCRATCH/profiles/ProvingGround.Strict.publish.xml" \
  /TargetDatabaseName:"$DB"
```

For the seed variant your test needs (empty table, zero-NULL re-seed, extra orphan, dup rows,
CDC-enabled), edit `$SCRATCH/Data/Seed.sql` (or run a one-off `docker exec` SQL against `$DB`)
**before** this publish. The seed shape is part of YOUR scratch state, not shared.

## 6 — The prove loop (all in scratch, all against `$DB`)

1. **Edit the destination** `CREATE` in `$SCRATCH/Modules/*.sql` (never write `ALTER`); rebuild
   (step 4).
2. **Preview the delta** — changes nothing:
   ```bash
   sqlpackage /Action:Script \
     /SourceFile:"$SCRATCH/bin/Release/SampleCatalog.dacpac" \
     /Profile:"$SCRATCH/profiles/ProvingGround.Strict.publish.xml" \
     /TargetDatabaseName:"$DB" \
     /OutputPath:"$SCRATCH/bin/delta.sql"
   ```
   Read `$SCRATCH/bin/delta.sql` — your private artifact. Look for DROP+CREATE on a rename
   (a rename with no refactorlog entry — STOP, that loses the column's data), a shadow-table
   rebuild, the table-has-rows `IF EXISTS(...) RAISERROR` guard before an `ALTER COLUMN ... NOT
   NULL`, drop-by-absence.
3. **Block check (Strict)** — publish to `$DB`. A clean publish means the change ships as a single
   schema change applied in place — no data is read or written. A blocked publish
   (`BlockOnPossibleDataLoss` / NOT NULL on rows / truncation / orphan FK / duplicate key) means
   the data forces a different shape. The block text + row counts are your proof.
4. **On a block only**: snapshot the data-hash (see `talk-to-local-sql` — per-row `SHA2_256(FOR
   XML RAW)` summed order-independently), publish with `ProvingGround.Permissive.publish.xml`
   (still `/TargetDatabaseName:$DB`), snapshot again, diff — that diff is exactly what
   `GenerateSmartDefaults` stamped / what truncated.
5. **Author the remedy** in `$SCRATCH` (a `Script.PreDeployment.sql` backfill, a `.refactorlog`
   entry, a staged NOCHECK->reconcile->`WITH CHECK CHECK`, a pre-deploy dedupe), rebuild, re-run
   the Strict block check. The clean Strict re-run is the proof you hand the developer.

Every command above names `$SCRATCH` for the file and `/TargetDatabaseName:$DB` for the
database. There is no path into shared state.

> **The make-mandatory caveat (COL-03 / COL-03C).** The Strict re-run after a backfill does NOT
> go clean on a populated table — the guard is table-has-rows, not column-has-NULLs. Do not
> treat the persisting block as a protocol failure: re-run the NULL probe to prove 0 NULLs
> remain, confirm the column stayed nullable, and record that the deployment is still blocked.
> THAT pair of facts (0 NULLs AND still-blocked) is the proof the case demands. Then prove the
> chosen remedy — a named `BlockOnPossibleDataLoss` relaxation in a script step
> (Permissive-equivalent scoped to this one change, after the zero-NULL proof) or a multi-phase
> restructure — actually lands the `NOT NULL`. The empty-table case (COL-03B) is the only leg
> that publishes clean as a single in-place schema change.

## 7 — ON EXIT (success OR failure) — tear down, idempotently

Run this **unconditionally** at the end. A `trap ... EXIT` only fires inside the **single shell
process** that registered it — and each command run here is a fresh shell. So either run the
entire prove cycle (steps 3–7) as **one compound command** with the trap, OR make teardown your
**last action regardless of outcome**: always issue the drop-if-exists + `rm -rf` below as the
final step, on success or failure. Do not rely on a trap surviving across separate command runs:

```bash
cleanup() {
  docker exec -i projection-mssql-warm /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U sa -P 'Projection@Strong1' -C -Q \
    "IF DB_ID('$DB') IS NOT NULL BEGIN ALTER DATABASE [$DB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DB]; END;"
  rm -rf "$SCRATCH"
}
trap cleanup EXIT
```

- **Drop your unique DB** (`SET SINGLE_USER WITH ROLLBACK IMMEDIATE` first, so an open
  connection can't block the drop). This honours **CLAUDE.md survival rule 2**: leaked per-run
  databases degrade the shared warm container (one session once leaked 209). Every executor
  reaps its own DB on exit, so accumulation stays at zero.
- **Delete your scratch dir.** No filesystem residue.
- **Idempotent**: re-running the whole protocol yields the same verdict and leaks nothing,
  because every name is `(testId, rand)`-scoped and every create is drop-if-exists.

## 8 — CDC tests are special (isolation is mandatory, not optional)

For any CDC test (`AUD-04`, `AUD-05`, `AUD-07N`, `TRAP-01N`): `sp_cdc_enable_db` flips
**instance-wide** state. Your unique DB already isolates this — enable CDC only inside `$DB`,
never run a database-level CDC enable expecting it to be scoped to a table, and never target the
profile's default catalog. The per-executor unique DB IS the `IsolatedContainerFixture` mindset
from survival rule 1. Tear it down in step 7 like any other.

The unique DB isolates CDC *state*, but the container's single capture/cleanup Agent is a
**shared** resource — under heavy parallel CDC load, capture timing can be non-deterministic
(not a data collision, a throughput one). Serialize the CDC-family cases, or poll-with-timeout
for capture results rather than asserting immediate capture. Do not claim CDC is fully
parallel-safe.

## 9 — If connections start failing across executors

A batch of `Could not open a connection` / pre-login-handshake failures means the **warm
container died or degraded** — NOT a regression and NOT a collision in this protocol. Check
`scripts/test.sh status` first, then `scripts/warm-sql.sh restart`. (Survival rule 2.) Resume
your executor from step 3 (your DB and scratch are recreated idempotently).
