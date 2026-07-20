---
name: talk-to-local-sql
description: The disposable local-SQL substrate prove-on-dacpac publishes against. Use whenever prove-on-dacpac needs a warm local SQL Server to publish against, a fresh-or-reset ProvingGround database, ad-hoc sqlcmd queries (row counts, MAX(LEN), NULL counts), or the content-hash check that answers "did the values actually change". Brings up the existing warm-sql container, supplies the connection string, the required runtime shim, and the docker-exec sqlcmd form (the host has NO sqlcmd) — the developer's agent runs the commands. No wrapper script.
---

# Talk to local SQL

> **Why this substrate.** The probe and the hash are the proof: a measured number is evidence, an
> asserted one is a guess — "1,240 rows are blank" stands where "some rows might be blank" cannot.
> The count is taken before the change is classified, so classification rests on the data, not on
> recollection. The strongest result this substrate produces is silence: a no-op redeploy that
> captures 0 rows and returns an unchanged content digest is the proof that a deploy is idempotent,
> and an unchanged digest on a re-run is a positive guarantee, not a non-event. The developer is
> owed the measured form — "0 NULLs remain, and the second deploy moved nothing" — over a
> recollection.

This skill owns the substrate `prove-on-dacpac` publishes against: a disposable SQL Server
database, real-*shaped* but safe to drop, standing in for the Dev database. Nothing here ever
touches production; the database is reset between scenarios and dropped without ceremony.

## The substrate of record: the Twin (deterministic), the sample as fallback

The substrate that earns trust is **the Twin** (`../../../THE_TWIN.md`): one command (`twin up`)
holds a local SQL Server current with the estate's own definitions and fills it with a
**deterministic, masked, distribution-faithful** dataset. Its trust is a property of the *system*,
not of any agent re-running it — `twin check` proves π∘σ≈id, mints are byte-identical (T1), and a
schema edit re-mints only the columns it touches (**S-stable**), so a reviewer regenerates the exact
same base data without re-running the agent. The **evidence-profiling config (`twin.json`) is
version-controlled beside the model and evolves as the schema evolves** — that is the local dev
environment the agent proves changes against. The Twin runs in **its own** container
(`../../../THE_TWIN.md` §6), never the warm projection container the fallback below uses. Do not
restate the Twin's laws here — point.

The hand-authored `ProvingGround` sample on the warm container (the rest of this file) is the
**fallback** — the deterministic fixture the self-test pins and the substrate when the Twin is not
wired. The proving loop is identical on either: build → Script → Strict → Permissive, then the silent
redeploy. When the Twin is present, prefer it — the base dataset is real-estate-shaped and evolves
with the schema; when it is absent, the sample keeps the loop working.

The commands and SQL are scaffolded here; the developer's agent runs them. There is **no wrapper
script** — the existing `scripts/warm-sql.sh` (plain bash, already in the repo) is reused, and
`sqlcmd` (via `docker exec`, see below) / `sqlpackage` run directly.

## The runtime shim (REQUIRED on this machine)

`sqlpackage` targets .NET 8; this box has .NET 9 at a non-standard path. Export these **before**
any `dotnet`/`sqlpackage` call in the session, or the tool fails to start. On Git Bash also export
`MSYS_NO_PATHCONV=1` so the `sqlpackage /Action:` switches **and** the `docker exec /opt/...`
sqlcmd paths below are not mangled:

```bash
# Point DOTNET_ROOT at YOUR local dotnet root — sqlpackage (a .NET 8 tool) needs a runtime there,
# and ROLL_FORWARD lets it use a newer one. `<you>` = your OS user.
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"   # Windows/Git Bash e.g. "C:/Users/<you>/AppData/Local/Microsoft/dotnet"
export DOTNET_ROLL_FORWARD=Major
export MSYS_NO_PATHCONV=1   # Git Bash ONLY: keep /Action: + /SourceFile: + /opt/... paths intact (drop it elsewhere)
```

`sqlpackage` is the dotnet tool `microsoft.sqlpackage`. Install it globally
(`dotnet tool install -g microsoft.sqlpackage`) so it is on PATH as `sqlpackage`, or set
`SQLPACKAGE` to its full path — Windows `C:/Users/<you>/.dotnet/tools/sqlpackage.exe`,
Linux/macOS `$HOME/.dotnet/tools/sqlpackage`. (The durable alternative to the shim is installing
the .NET 8 runtime; then `DOTNET_ROOT` / `ROLL_FORWARD` are unnecessary.) The proving-ground
findings in this tree were captured on sqlpackage 170.4.83 — that version stamp belongs in every
proof, because the guard behaviour is version-bound.

## sqlcmd lives in the CONTAINER, not on the host (IMPORTANT)

**This machine has NO host `sqlcmd`.** Do **not** invoke a bare `sqlcmd -S localhost,11433 ...` —
it does not exist on the PATH and will fail. Issue every SQL round-trip through the warm container
with `docker exec`, using the bundled tools at `/opt/mssql-tools18/bin/sqlcmd`:

```bash
docker exec -i projection-mssql-warm /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Projection@Strong1' -C -Q "<sql>"
```

Notes that matter:

- **From inside the container, the server is `localhost`** (NOT `localhost,11433`) — `11433` is the
  host-side published port; inside the container SQL Server listens on the default `1433`.
- **`-C`** trusts the server certificate; **`-i`** keeps stdin usable for heredocs / piped SQL.
- **`MSYS_NO_PATHCONV=1` is required on Git Bash** or it rewrites `/opt/mssql-tools18/...` into a
  Windows path and the exec fails. (Same flag the `sqlpackage /Action:` switches need.)
- `sqlpackage`, by contrast, runs on the **host** and connects over the published port, so its
  profiles use `localhost,11433`. Two different vantage points: `sqlpackage` from outside
  (`,11433`), `sqlcmd` from inside the container (`localhost`).

## Bring up the warm container

The repo already owns the container lifecycle — do **not** write a new `docker run`. The script is
the single source of truth for the name / port / password.

```bash
scripts/warm-sql.sh start      # start the container (or no-op if already up)
scripts/warm-sql.sh status     # container + readiness check — run this FIRST when in doubt
scripts/warm-sql.sh restart    # clean instance (use after a memory-grant stall / login failures)
```

This yields the container `projection-mssql-warm` listening on **localhost,11433** (host side).

> **Note (from project survival rules):** on Git Bash, `scripts/warm-sql.sh status` can give a
> **false "NOT ready"** due to MSYS path mangling. If `status` looks wrong, confirm with a direct
> `docker exec` sqlcmd round-trip (below) before assuming the container is down.

### Connection parameters

| Field | Value |
|---|---|
| Container | `projection-mssql-warm` |
| Server (from host, e.g. sqlpackage) | `localhost,11433` |
| Server (from inside the container, sqlcmd) | `localhost` (default port 1433) |
| User | `sa` |
| Password | `Projection@Strong1` |
| Options | `TrustServerCertificate=True;Encrypt=False` (or `-C` on sqlcmd) |
| Disposable copy | `ProvingGround` (or a per-executor `PG_<testId>_<rand>`, see PROTOCOL.md) |

Full connection string (what the host-side publish profiles use):

```
Server=localhost,11433;Initial Catalog=ProvingGround;User ID=sa;Password=Projection@Strong1;TrustServerCertificate=True;Encrypt=False
```

## Run sqlcmd against it (via docker exec)

Use `sqlcmd` for the cheap data probes that classification leans on. Confirm connectivity first:

```bash
# liveness round-trip (also the antidote to a false "NOT ready" status)
docker exec -i projection-mssql-warm /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Projection@Strong1' -C -Q "SELECT @@VERSION"
```

(`-C` = trust the server certificate; quote the password for the shell; remember
`MSYS_NO_PATHCONV=1` on Git Bash for the `/opt/...` path.)

The probes that predict whether the change is blocked **before** the dacpac is even built (wrap each
in the same `docker exec ... -Q "<sql>"` form):

```sql
-- make-mandatory: how many rows hold a NULL Email under the new NOT NULL rule?
-- (NOTE: SSDT blocks on table-has-rows, not this NULL count — but the count still proves the
--  backfill cleared it; see prove-on-dacpac's make-mandatory finding.)
SELECT COUNT(*) AS NullRows FROM dbo.Customer WHERE Email IS NULL;

-- narrow (Ambitious Narrowing): does any value exceed the new length?
SELECT MAX(LEN(Code)) AS LongestCode FROM dbo.Product;

-- add-unique: are there duplicates that would fail the unique index?
SELECT Code, COUNT(*) AS n FROM dbo.Product GROUP BY Code HAVING COUNT(*) > 1;

-- create-FK (Forgotten FK Check): are there orphan children with no parent?
SELECT o.Id, o.CustomerId
FROM dbo.[Order] o
LEFT JOIN dbo.Customer c ON c.Id = o.CustomerId
WHERE c.Id IS NULL;
```

Example, fully wired:

```bash
docker exec -i projection-mssql-warm /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Projection@Strong1' -C \
  -Q "SELECT COUNT(*) AS NullRows FROM dbo.Customer WHERE Email IS NULL;"
```

These probes **predict**; the Strict publish in `prove-on-dacpac` **proves**. Run the probe to know
what to look for, then let the blocked publish confirm it.

## Create / reset / drop the disposable database (via docker exec)

Reset between scenarios so each case starts from the known seed. This database is disposable —
dropping and recreating is the normal path, not a last resort.

```bash
# create (idempotent)
docker exec -i projection-mssql-warm /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Projection@Strong1' -C -Q \
  "IF DB_ID('ProvingGround') IS NULL CREATE DATABASE ProvingGround;"

# hard reset: drop + recreate so the next scenario starts clean
docker exec -i projection-mssql-warm /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Projection@Strong1' -C -Q \
  "IF DB_ID('ProvingGround') IS NOT NULL BEGIN ALTER DATABASE ProvingGround SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ProvingGround; END; CREATE DATABASE ProvingGround;"
```

After a reset, re-establish the BEFORE state (deploy the current CREATEs + post-deploy seed) per
`proving-ground/README.md` — that seed is the "real-shaped data" every case is measured against.

> **Parallel runs:** when many executors prove cases at once, each owns a **unique** DB
> (`PG_<testId>_<rand>`) created and dropped exactly the same `docker exec` way, and never touches
> the shared `ProvingGround` catalog. The full isolation + idempotent-teardown protocol is
> **`self-test/PROTOCOL.md`** — and it honours survival rule 2 (leaked per-run DBs degrade the warm
> container), so every executor reaps its own DB on exit.

## The content-hash check ("did the values actually change?")

When a Permissive publish proceeds past what the Strict publish blocks, exactly what moved must be
established. The content-hash check is a per-row `SHA2_256` over the row's full shape, combined
**order-independently** so row order is irrelevant, with **NULL kept distinct from `''`**. Snapshot
it **before** and **after** the Permissive publish; an unchanged total means no value changed. (Run
it through the same `docker exec ... -Q` form.)

```sql
-- Content hash of a table, order-independent over rows.
-- (SELECT x.* FOR XML RAW) serializes the full row shape (column names included),
-- so a rename changes the hash BY DESIGN — that is correct, the diff is the proof.
-- NULL stays distinct from '' because FOR XML RAW omits NULL attributes entirely.
-- CORRECTED (proven live, SQL Server 2022): the per-row hash must be hoisted through
-- CROSS APPLY — a subquery placed directly inside SUM fails with Msg 130.
SELECT
  COUNT(*)                                            AS RowCount,
  CONVERT(VARBINARY(8000),
    SUM(CONVERT(BIGINT,
      CONVERT(INT, SUBSTRING(h.hb, 1, 4))              -- 32-bit slice, summed order-independently
    ))
  )                                                   AS ContentDigest
FROM dbo.Customer AS x
CROSS APPLY (SELECT HASHBYTES('SHA2_256', (SELECT x.* FOR XML RAW)) AS hb) AS h;
```

Run it once before the Permissive publish, once after, and compare `RowCount` + `ContentDigest`:

- **digest unchanged** -> no value moved (the change was structural-only, or truly benign).
- **digest changed** -> values moved; the rows that differ are exactly what
  `GenerateSmartDefaults` stamped, or what truncated. **That is the row count the finding states.**
- **digest changed on a rename** -> expected and correct; the column name is part of the row shape.

> The digest is a *change detector*, not a forensic diff: a changed digest reports that values
> moved and how many rows; the specific old-vs-new values come from querying the affected rows
> directly (e.g. `WHERE Email = '' ` after a smart-default stamp). Pair the digest with a targeted
> SELECT when the developer needs the actual values, not just the count.
>
> **The no-op redeploy is the idempotency proof:** publish an unchanged tree twice; the second run
> should produce **zero delta**, the guarded seed MERGE should report **0 rows**, and the digest
> should be **identical** — and on a CDC-tracked table, CDC should capture **0 changes**. That
> silence is the idempotency / CDC-silence guarantee, the strongest result this substrate produces,
> not a non-event.

## Honest limits of this substrate

- It is real-*shaped*, not real-*sized* — it cannot prove production-scale timing or blocking; at
  large row counts (on the order of `>1M rows`) a change may block writes or run long, which is an
  added-scrutiny finding this copy cannot settle (see `classify-mechanism`).
- It is a single instance — concurrency, online index builds, and live-traffic locks are not
  modeled.
- It holds one catalog — External Entities and cross-database / ETL / report consumers are out of
  frame and must be named as dependency scope, not proven here.
- It runs the **forward** publish only — it cannot prove a change is reversible, and it cannot
  prove the running application keeps working against the new shape (see `prove-on-dacpac`'s
  honest-limits section).
- Point profiles and the `docker exec` sqlcmd at **`ProvingGround` (or a per-executor
  `PG_<testId>_<rand>`) only** — never at anything that cannot be safely dropped.

## Hard rules

- **No new wrapper / orchestration script.** Reuse `scripts/warm-sql.sh`; run `sqlcmd` (via
  `docker exec`) / `sqlpackage` directly. The agent runs the commands.
- **Host has no sqlcmd — always `docker exec` into `projection-mssql-warm`**, server `localhost`
  inside the container, with `MSYS_NO_PATHCONV=1` on Git Bash.
- Everything authored for this tree lives under `ssdt-agent/`; the substrate itself reuses the
  existing repo container.

## Connector points

- The disposable substrate reuses the **existing** `scripts/warm-sql.sh` — the connector boundary
  is that this database lives only on that warm container and is disposable. See
  `CONNECTORS.md`.
- A future build could replace the hand-seeded `ProvingGround` with data shaped by the F# engine
  from a real OutSystems catalog (same connection, same content-hash check). Highlighted, not wired.
