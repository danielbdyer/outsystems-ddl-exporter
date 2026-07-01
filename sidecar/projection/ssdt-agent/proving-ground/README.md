# proving-ground — the runbook

This is the agent's runbook for the proving loop. The proving ground is a **hand-authored,
self-contained sample project** (it is data, not a wrapper script) plus a **throwaway copy of
real-shaped data** on a local SQL Server container. You edit a `CREATE`, build a dacpac, and
publish it against the throwaway DB to watch what SSDT's publish engine actually does. **The
veto is the classification.**

You run these commands yourself — there is no orchestration script. The blocks below are the
worked commands; read them, then run them in order.

> **Running in parallel with other executors?** This runbook publishes to one shared
> `ProvingGround` database and edits the authored tree in place — fine for a SINGLE prover
> working interactively. If you are one of MANY subagents proving cases at once, DO NOT use the
> shared DB or edit these files in place: follow **`../self-test/PROTOCOL.md`** instead — copy
> the tree to a private scratch dir and publish to a UNIQUE database
> (`/TargetDatabaseName:PG_<testId>_<rand>`, which overrides the profile's Initial Catalog).
> That is how a hundred provers share the warm container without colliding on the same `.sql`,
> the same `bin/`, or the same DB.

## 0 — The runtime shim (REQUIRED on this machine)

`sqlpackage` is installed as a dotnet tool targeting .NET 8, but this box has .NET 9 at a
non-standard path. Export these in the shell you run `sqlpackage` from, or the tool fails to
start:

```bash
export DOTNET_ROOT="C:/Users/danny/AppData/Local/Microsoft/dotnet"
export DOTNET_ROLL_FORWARD=Major
export MSYS_NO_PATHCONV=1   # Git Bash: keep /Action: switches and /opt/... docker paths intact
```

`sqlpackage` lives at `C:\Users\danny\.dotnet\tools\sqlpackage.exe` (version 170.4.83).
(Alternative to the shim: install the .NET 8 runtime — then the env vars are unnecessary.)

## 1 — Warm the throwaway substrate

Reuse the existing helper (plain bash, already in the repo — do not write a new one):

```bash
scripts/warm-sql.sh start
```

This brings up container `projection-mssql-warm`:

| setting   | value                                                         |
|-----------|---------------------------------------------------------------|
| Server    | `localhost,11433`                                             |
| User      | `sa`                                                          |
| Password  | `Projection@Strong1`                                          |
| Options   | `TrustServerCertificate=True;Encrypt=False`                   |

The host has **no `sqlcmd`** — issue SQL through the container:

```bash
docker exec -i projection-mssql-warm /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Projection@Strong1' -C -Q "<sql>"
```

Create the throwaway database once (drop it first if a previous run left it dirty):

```bash
docker exec -i projection-mssql-warm /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Projection@Strong1' -C -Q \
  "IF DB_ID('ProvingGround') IS NOT NULL BEGIN ALTER DATABASE ProvingGround SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ProvingGround; END; CREATE DATABASE ProvingGround;"
```

> `talk-to-local-sql` owns the substrate details, the conn string, and the data-hash oracle.
> If you see a batch of "Could not open a connection" failures, the warm container died —
> `scripts/warm-sql.sh restart`, do not suspect the change.

## 2 — Build the dacpac

```bash
dotnet build ssdt-agent/proving-ground/SampleCatalog.sqlproj -c Release
# -> ssdt-agent/proving-ground/bin/Release/SampleCatalog.dacpac
```

## 3 — Establish the BEFORE state (once per scenario)

Publish the *current* CREATEs and run the post-deploy seed so the throwaway DB holds the
real-shaped data the proof depends on. The first publish of the unedited project does exactly
this:

```bash
sqlpackage /Action:Publish \
  /SourceFile:ssdt-agent/proving-ground/bin/Release/SampleCatalog.dacpac \
  /Profile:ssdt-agent/proving-ground/profiles/ProvingGround.Strict.publish.xml
```

Now the seed scenarios (NULL emails, orphan order, over-length + duplicate Code) are present.

## 4 — Make the destination edit

Edit the matching `Modules/*.sql` `CREATE` to the destination. **Never write `ALTER`.** Then
rebuild the dacpac (step 2). One edit, one proof.

## 5 — PREVIEW the real delta (changes nothing)

```bash
sqlpackage /Action:Script \
  /SourceFile:ssdt-agent/proving-ground/bin/Release/SampleCatalog.dacpac \
  /Profile:ssdt-agent/proving-ground/profiles/ProvingGround.Strict.publish.xml \
  /OutputPath:ssdt-agent/proving-ground/bin/delta.sql
```

Read `delta.sql`. This is the REAL SSDT-generated change — the thing a raw `sqlcmd` ALTER loop
can never show you. Look for: `DROP`+`CREATE` on a rename (Naked Rename — STOP), a shadow-table
rebuild (table swap), drop-by-absence, `GenerateSmartDefaults` backfills, and — for a
make-mandatory — the **table-has-rows guard**
`IF EXISTS (SELECT TOP 1 1 FROM [dbo].[Customer]) RAISERROR(...,16,127)` placed *above* the
`ALTER COLUMN ... NOT NULL`. That guard inspects row PRESENCE, not the column's NULLs.

## 6 — VETO CHECK (Strict = the veto detector)

```bash
sqlpackage /Action:Publish \
  /SourceFile:ssdt-agent/proving-ground/bin/Release/SampleCatalog.dacpac \
  /Profile:ssdt-agent/proving-ground/profiles/ProvingGround.Strict.publish.xml
```

- **Succeeds clean, no script** → **Mechanism 1 Pure Declarative** (single-phase).
- **Vetoes** (`BlockOnPossibleDataLoss` / NOT NULL on a populated table / truncation / orphan
  FK / duplicate key) → the data flipped the bucket up. The veto text + row counts are your
  proof.

> **make-mandatory caveat (the showcase finding).** The NULL->NOT NULL veto is
> **table-has-rows, not column-has-NULLs.** On a POPULATED table it vetoes even after you
> backfill every NULL away — PROVEN here: 0 NULL emails, Strict STILL vetoed, column stayed
> nullable. Only an EMPTY table publishes clean (Mechanism 1). See step 8 for the corrected
> remedy; do not report the old "backfill -> clean NOT NULL = Mechanism 3" recipe.

## 7 — ON VETO ONLY: the consequence oracle (Permissive) + hash snapshot

Snapshot the data hash, publish Permissively to let the change proceed past the veto, snapshot
again, and diff:

```bash
# BEFORE hash (see talk-to-local-sql for the exact per-row SHA2_256(FOR XML RAW) query)
sqlpackage /Action:Publish \
  /SourceFile:ssdt-agent/proving-ground/bin/Release/SampleCatalog.dacpac \
  /Profile:ssdt-agent/proving-ground/profiles/ProvingGround.Permissive.publish.xml
# AFTER hash; diff BEFORE vs AFTER -> exactly what GenerateSmartDefaults stamped / what truncated
```

A rename changes the hash by design (the column name is part of the row shape) — that diff *is*
the proof, not a bug.

## 8 — Author the remedy, then re-prove Strict clean

Write the real remedy — a `Script.PreDeployment.sql` backfill, a refactorlog entry, a staged
NOCHECK → reconcile → `WITH CHECK CHECK` FK, a pre-deploy dedupe. Rebuild, re-run **step 6**.
**The clean Strict re-run is the proof you hand the developer.**

> **make-mandatory is the exception.** On a populated table a backfill does NOT produce a clean
> Strict re-run — the table-has-rows guard still fires. The corrected, proven remedy is a
> CONSCIOUS, DOCUMENTED decision taken AFTER a verified-zero-NULL backfill (the zero-NULL probe
> is necessary but NOT sufficient): either **(a)** a targeted relaxation of
> `BlockOnPossibleDataLoss` for THIS one change — operationally Mechanism 4 / Script-Only with
> a named, logged gate-relaxation — or **(b)** restructure as Mechanism 5, Multi-Phase. The
> proof packet carries the zero-NULL probe AND the explicit gate-relaxation (or the staged
> phases). The EMPTY-table leg is the clean Mechanism 1 contrast.

## 9 — Reset between scenarios

Each flip starts from the known seed. Drop and recreate `ProvingGround` (step 1) before the
next scenario so one scenario's edits do not contaminate the next. (Parallel executors skip
this — each owns a fresh unique DB per `../self-test/PROTOCOL.md`.)

---

## Seed scenario → bucket-flip map

| seed scenario (in `Data/Seed.sql`)                    | the change you prove          | the flip it demonstrates                                                                 | self-test |
|-------------------------------------------------------|-------------------------------|------------------------------------------------------------------------------------------|-----------|
| Customer rows 3 & 5 have `Email` NULL (table populated) | make-mandatory `Email NOT NULL` | Strict vetoes — guard is **table-has-rows**; backfill clears the NULLs but NOT the veto → gate-relaxation or multi-phase | COL-03    |
| Customer table EMPTY (skip the seed / truncate)       | the SAME `Email NOT NULL`     | no rows → `IF EXISTS` false → ALTER lands → clean **Mechanism 1**, Tier 1                 | COL-03B   |
| Customer re-seeded with ZERO NULL Email (still populated) | the SAME `Email NOT NULL`     | STILL vetoes — zero NULLs is necessary but NOT sufficient; the guard is row-presence      | COL-03C   |
| Customer `ContactPhone` populated, **no refactorlog** | rename `ContactPhone`→`MobileNumber` | delta = DROP+CREATE = Naked Rename data loss → STOP, demand refactorlog                   | COL-08N   |
| `Order` row 4 has `CustomerId = 999` (orphan)         | add FK `Order.CustomerId`→`Customer.Id` | clean FK vetoes on orphan → Script-Only (NOCHECK→reconcile→`WITH CHECK CHECK`)            | KEY-03    |
| `Product` row 3 `Code` = 16 chars                     | narrow `Code` to `NVARCHAR(10)` | over-length → Strict data-loss veto → reconcile first (probe `MAX(LEN)` to predict)       | COL-06    |
| `Status` seed rows unchanged on re-publish            | add lookup value `'Refunded'` | guarded MERGE captures 0 rows on no-op → CDC-silence proof                                | STA-02    |
| `Product` rows 4 & 5 share `Code = 'DUPE'`            | add UNIQUE on `Code`          | unique index build fails on dupe → pre-deploy dedupe                                      | CON-02    |

> **The make-mandatory triple is the spine proof.** COL-03B (empty) MUST publish clean;
> COL-03 (populated, NULLs) and COL-03C (populated, zero NULLs) MUST BOTH still veto. An agent
> that claims a backfill or a zero-NULL re-seed yields a clean Mechanism 1/3 on a POPULATED
> table — without empirically discovering on the proving ground that it STILL vetoes — has
> classified from stale recipe text and the run FAILS. Same edit, three seeds, decided by
> ROW PRESENCE.

## Trap watch (handbook 16 = §19) — catch these in the delta, not after

Naked Rename · Optimistic NOT NULL · Forgotten FK Check · Ambitious Narrowing · CDC Surprise ·
Refactorlog Cleanup · SELECT * View.

## Connector point

The hand-authored `SampleCatalog` can be replaced by the F# engine's
`SqlprojEmitter`/`DacpacEmitter`/`PostDeployEmitter` output from a real OutSystems catalog —
the prove loop above is unchanged, just real schema. See `../CONNECTORS.md` §3.
