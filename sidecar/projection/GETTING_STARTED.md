# GETTING_STARTED.md — the operator's first hour

This is the **operator-facing** first-run guide: from a fresh clone to previewing and
applying your first flow. It documents what the CLI **does today** (grounded in
`src/Projection.Cli/Program.fs` + `MovementSurface.fs`). For the *design* of the surface
(the algebra, the one-act framing), read `THE_CLI.md` — it is the destination and is
largely implemented; this guide is the how-to.

> **The one idea.** There is one act: **move a model from a source environment to a target
> environment.** The engine reads what is already at the target (*A*) and emits the minimal
> change to make it match (*B ⊖ A*). "Deploy", "migrate", "load", "transfer", "export" are
> the same act with a different target and a different content source. You name the
> variations once, in config, as **flows**; the daily command is `projection <flow>`.

---

## 1. Prerequisites

| Need | Why | Check |
|---|---|---|
| **.NET SDK 9.0.314** | `global.json` pins it with `rollForward: disable` — another version fails at load time. | `dotnet --version` → `9.0.314` |
| **Docker** (running) | For `access: docker` environments and `check canary` (ephemeral SQL Server). | `docker ps` |
| **SQL Server 2019+** reachable | Only for `access: direct` (live writes) or applying a `bundle` via your pipeline. Not needed for the docker/bundle-file paths. | a connection string you can reach |

Everything runs from the project directory: **`sidecar/projection/`**.

---

## 2. Build and invoke

There is no published binary yet — you run from source:

```bash
cd sidecar/projection
dotnet build sidecar/projection/Projection.sln          # or: dotnet build Projection.sln
dotnet run --project src/Projection.Cli -- --help        # the full surface + exit codes
```

Note the `--`: everything after it is passed to `projection`, not to `dotnet`. Throughout
this guide, **`projection <args>`** is shorthand for `dotnet run --project src/Projection.Cli -- <args>`.
(A convenient shell alias: `alias projection='dotnet run --project src/Projection.Cli --'`.)

---

## 3. The 60-second smoke (no config, no database)

Prove the toolchain works before touching any config. This deploys an OutSystems-shaped
schema to a throwaway Docker SQL Server, reads it back, and asserts fidelity:

```bash
projection check canary fixtures/canary-gate.sql
```

Expected: a short report and **exit 0**. If Docker is not running you get **exit 4**
(`Docker unavailable`) — start Docker and retry. This needs no `projection.json`, no model,
and no secrets.

---

## 4. Your first config — `init`, then a flow

```bash
projection init        # writes a starter projection.json (refuses to overwrite an existing one)
```

The scaffold reads the **model live** from your `cloud-dev` environment and defines the
**environments** (places), a **readiness** set (the cloud cells that must resolve to one shape),
and the **flows** (movements):

```jsonc
{
  "model": { "env": "cloud-dev" },
  "environments": {
    "cloud-dev":   { "access": "direct", "conn": "file:./secrets/cloud-dev.conn", "rendition": "physical", "archetype": "managed-dml" },
    "cloud-qa":    { "access": "direct", "conn": "file:./secrets/cloud-qa.conn",  "rendition": "physical", "archetype": "managed-dml" },
    "local":       { "access": "docker" },
    "on-prem-dev": { "access": "bundle", "out": "./dist/on-prem-dev", "grant": "schema+data", "rendition": "logical", "archetype": "full-rights", "store": "./lifecycle/on-prem-dev.json" }
  },
  "readiness": { "confirm": ["cloud-dev", "cloud-qa"] },
  "emission": { "ssdt": true, "dacpac": true },
  "flows": {
    "try":      { "from": "cloud-dev", "to": "local" },
    "skeleton": { "from": "cloud-dev", "to": "local", "shape": "skeleton" },
    "publish":  { "from": "cloud-dev", "to": "on-prem-dev" }
  }
}
```

Put each environment's connection string in `./secrets/<name>.conn` — the model is read
**live** from `cloud-dev` (OutSystems metadata → native `SsKey`; no V1, no exported file).
`model.env` *names* that environment: the connection lives once in `environments.cloud-dev.conn`
and the model reads through it (no duplicated conn-ref). `model.path` (an `osm_model.json` file)
is the configured fallback (use it only when no live connection is available). The cutover gate's
`readiness.schema` defaults to `model.env`, so the agreed shape is that same `cloud-dev` without
restating it. The cloud cells carry `archetype: managed-dml` and the on-prem tiers
`full-rights` — the capability class the engine derives safe interaction from
(`DATABASE_ARCHETYPES.md`). See §6 for the `file:` ref details.

```bash
mkdir -p secrets && chmod 700 secrets
for n in cloud-dev cloud-qa; do cp examples/secret.conn.example "secrets/$n.conn"; done   # then replace each placeholder
```

List what is configured (the resolved `source → target` of each flow):

```bash
projection                 # → try / skeleton: cloud-dev → local · publish: cloud-dev → on-prem-dev
```

**Preview** the `try` flow — it reads the live model and deploys into a throwaway Docker
database, reporting the change without touching anything real:

```bash
projection try             # preview (dry-run); nothing is committed
```

A `bundle` flow writes SSDT files for a deployment pipeline (Octopus) — no database needed:

```bash
projection publish         # writes ./dist/onprem-dev/...  (CREATE files + RefactorLog + scripts)
```

---

## 5. Environments and flows — the config reference

`projection.json` (or the file named by `$PROJECTION_CONFIG`) has a model source plus two
blocks. The parser reads exactly the keys below; **unknown keys are silently ignored**, so
spelling matters.

### Top-level — the model source

| Key | Meaning |
|---|---|
| `modelOssys` | **primary** — a live OSSYS connection ref (`env:<VAR>` / `file:<path>`); the model is read live from your cloud OutSystems environment (native `SsKey`, no V1). |
| `model` | **fallback** — a path to an exported `osm_model.json`. Optional when `modelOssys` is set (kept for cutover safety). When both are present, `modelOssys` wins. |
| `environments` / `flows` | the two config blocks below. |

### Environments — the *places*

| Key | Required when | Values | Meaning |
|---|---|---|---|
| `access` | always | `bundle` \| `direct` \| `docker` | how the place is reached |
| `out` | `access: bundle` | a folder path | where the SSDT bundle is written |
| `conn` | `access: direct` | `env:<VAR>` \| `file:<path>` | the connection **reference** (never a literal string — see §6) |
| `grant` | optional | `schema+data` \| `data` | what may change here (a **refusal gate**) |
| `rendition` | optional (metadata) | `physical` \| `logical` | which rendition of the one model this place bears (cloud-insertion; see `THE_DATA_PRODUCERS.md`) |
| `store` | optional | a file path | the durable episode timeline (`seal` writes it, `report` diffs it) |

- **`access`** decides delivery: `bundle` produces files (always safe; for a CI/CD pipeline);
  `direct` writes to a live connection; `docker` spins up an ephemeral verified database.
- **`grant` is a property of the *sink*, not a tier.** A **source** environment (a flow's
  `from`) is read-only and carries **no grant**. A **sink** carries the grant for what the
  flow changes there: **`data`** (DML only — the schema must already agree) or **`schema+data`**
  (DDL+DML). A schema-changing flow against a `data` sink is a **type mismatch, refused
  loudly** — never half-applied. In the cloud-insertion estate this is the natural cut:
  **schema travels the on-prem `bundle` path (`schema+data`); data lands in the cloud via
  DML-only (`data`) flows** — V2 owns no schema write to a live cloud environment (R6).

### Flows — the *movements*

| Key | Required | Values | Default |
|---|---|---|---|
| `to` | **yes** | an environment name | — |
| `from` | no | an environment name, or `model` / `synthetic` / `none` | `model` |
| `profile` | no | an environment name (for `from: synthetic`) | — |
| `tables` | no | a list of entity names (a subset — "golden" data) | all |
| `rekey` | no | `file:<users.csv>` (user-FK re-key map) | — |

`from` keywords: **`model`** (the authored `osm_model.json` / live OSSYS), **`synthetic`**
(generated data matching a captured profile), **`none`** (schema only, no rows), or an
**environment name** (read from that live place).

---

## 6. Secrets and multi-environment setup (the D9 discipline)

**Connection strings never live in `projection.json`.** A `conn` (and `modelOssys`) is a
*reference* resolved at run time — and the engine reads the secret itself, so no value is ever
committed. Two forms:

| Form | Resolves to | What it needs |
|---|---|---|
| `file:<path>` | the file's **contents** — the connection string, trimmed (`File.ReadAllText().Trim()`) | nothing — read directly off disk at run time |
| `env:<VAR>` | the environment variable `<VAR>` | the var must be exported into the process first |

**Use `file:` — one small file per connection, read straight off disk.** Nothing to source, and
the secret never enters the process environment. A value that *looks* like an inline secret
(contains `;`, `password`, `pwd=`) is **refused at parse time** (`cli.config.envSecretInline`,
exit 6) — the committed config carries only addressing.

### Worked example — the `golden` flow, end to end

`golden` promotes a subset of cloud-QA's data into cloud-UAT, re-keying every user FK to UAT's
own users (no user rows are copied). It touches three live connections — the **model read**
(`modelOssys`), the **source** cell (`cloud-qa`), and the **sink** (`cloud-uat`) — so all three
get a `file:` ref. A realistic `projection.json`:

```jsonc
// projection.json
{
  "modelOssys": "file:./secrets/ossys.conn",
  "environments": {
    "cloud-qa":  { "access": "direct", "conn": "file:./secrets/cloud-qa.conn",  "rendition": "physical" },
    "cloud-uat": { "access": "direct", "conn": "file:./secrets/cloud-uat.conn", "grant": "data", "rendition": "physical" }
  },
  "flows": {
    "golden": {
      "from": "cloud-qa", "to": "cloud-uat",
      "tables": ["Customer", "Order", "OrderLine"],
      "rekey":  "file:./secrets/users.csv"
    }
  }
}
```

Create the secrets it names. Each `*.conn` holds **exactly** one connection string (the *whole
file*, trimmed — no comment lines):

```bash
mkdir -p secrets && chmod 700 secrets
cp examples/secret.conn.example secrets/ossys.conn      # the cloud OutSystems model read
cp examples/secret.conn.example secrets/cloud-qa.conn   # the peer source cell
cp examples/secret.conn.example secrets/cloud-uat.conn  # the UAT sink
$EDITOR secrets/*.conn                                  # replace each REPLACE_ME, e.g.:
#   Server=tcp:cloud-uat.example.com,1433;Database=outsystems;User Id=svc;Password=…;Encrypt=true
chmod 600 secrets/*.conn
```

The `rekey` map is a CSV — `table,sourceKey,assignedKey`, one row per user (the source cell's
User surrogate → the matching UAT User; `table` is the physical User table). A leading `table,`
header line is optional:

```csv
table,source,assigned
OSUSR_ABC_USER,280,18
OSUSR_ABC_USER,281,19
```
```bash
$EDITOR secrets/users.csv     # gitignored under secrets/
```

Then preview, then apply:

```bash
projection golden                                  # preview — reads A from cloud-uat, shows B ⊖ A, writes nothing
PROJECTION_ALLOW_EXECUTE=1 projection golden --go  # apply — re-keys the user FKs; user rows are never copied
```

Every secret is read straight from `./secrets/` at run time — **no `source`, no env vars.** The
committed `projection.json` names only `file:` references; `secrets/` and `*.conn` are
`.gitignore`d, so credentials never enter the repository.

The full six-environment estate (publish / golden / reverse / synth, all `file:` refs) is
`examples/projection.sample.json` — `cp` it to `./projection.json` and create one
`secrets/<name>.conn` per `direct` environment it names.

> Prefer environment variables? Use `env:<VAR>` refs and export them per shell
> (`export CLOUD_UAT_CONN="…"`) — one inventory, but it needs the export step and puts the
> secret in the process environment. `file:` avoids both.

### The environment variables the CLI reads

| Variable | Purpose | Operator-relevant? |
|---|---|---|
| `PROJECTION_ALLOW_EXECUTE=1` | authorizes a live write (paired with `--go`; see §7) | **yes** |
| `PROJECTION_CONFIG` | path to the config file (default `./projection.json`) | yes |
| `<YOUR>_CONN` | each `env:` reference you declare | yes |
| `PROJECTION_MSSQL_CONN_STR` | the source connection for the **live profiler** (`profiler.provider: live`) | when profiling |
| `PROJECTION_LEDGER_DIR` | folder for the run-ledger (`check ready` streak gauge) | optional |

---

## 7. Preview → apply: the two-key live write

Preview is the default and is always safe. A live write needs **two** independent keys, so a
config can never commit on its own:

```bash
projection golden                                   # preview — reads A, shows B ⊖ A, writes nothing
PROJECTION_ALLOW_EXECUTE=1 projection golden --go   # apply — needs BOTH the env var and --go
```

- **`--go`** is your intent, stated at the moment (never persisted in a file).
- **`PROJECTION_ALLOW_EXECUTE=1`** is the authorization, set on the shell session.

Miss either and the write is **refused loudly** (exit 7, `gate.intent`) — it does **not**
silently fall back to a dry-run. Two more per-run words, both rare and deliberate:

- **`--fresh`** — wipe-and-load from scratch (ignore *A*; the non-minimal fallback). Use only
  for an empty or intentionally-reset target.
- **`--allow-drops`** — accept a declared destructive loss (a drop / narrow / scoped delete).
  Without it, a change that would drop data is refused (exit 9). Decide each time.

`bundle` targets never need `--go` (they only write files).

---

## 8. Verify it worked

| Command | Asserts |
|---|---|
| `projection check canary <source.sql>` | a schema round-trips with empty fidelity diff (exit 5 on divergence) |
| `projection check drift --model <m.json> --to <conn-ref>` | the deployed schema still matches the model |
| `projection check data --before <ref> --after <ref>` | row / null counts match between two deployments (exit 8 on mismatch) |
| `projection explain diff <a> <b>` | the exact catalog change between two references |

---

## 9. Troubleshooting (by exit code)

The CLI leads every failure with a plain statement; the bracketed `[code]` underneath is the
substantiation. Exit codes (also in `projection --help`):

| Exit | Means | First thing to check |
|---|---|---|
| 1 | argv error (unknown flow / missing input) | `projection` to list flow names; spelling |
| 2 | parse error (model JSON / config schema) | the JSON validates; required keys present |
| 4 | Docker unavailable | `docker ps`; start the daemon |
| 6 | config error (missing/unparseable; D9; conn-ref) | is `projection.json` found? is a `conn` an `env:`/`file:` ref (not a literal)? is `$YOUR_CONN` exported? |
| 7 | gate refusal | `--go` without `PROJECTION_ALLOW_EXECUTE=1`; or the place's `grant` forbids the change; or a missing write permission |
| 9 | refused, fail-loud | a destructive drop without `--allow-drops`; an inexpressible change |

Common first-run snags:

- **`projection <flow>` says the flow is unknown / nothing lists.** Your config has no
  `flows` block the parser recognizes. Re-run `projection init` for a known-good shape, or
  check you are in the directory with `projection.json` (or that `$PROJECTION_CONFIG` points
  at it). Unknown top-level keys are ignored silently.
- **`cli.config.envSecretInline`.** A `conn` looks like a literal connection string. Replace
  it with `env:<VAR>` or `file:<path>` and export the secret out-of-band (§6).
- **A live `--go` did nothing and exited 7.** `PROJECTION_ALLOW_EXECUTE=1` was not set. The
  write is refused, not silently downgraded.
- **A `direct` flow can't connect.** `echo $YOUR_CONN` — is it exported in *this* shell? Is
  the server reachable? Is the connection string valid?

---

## 10. Where to go next

- **`examples/`** — a fuller annotated `projection.sample.json` (a six-environment estate).
- **`CONFIG_REFERENCE.md`** — the *model-shaping* config (separate from `projection.json`):
  modules/entities in scope, entity/table naming overrides, emission toggles, tightening policy.
- **`THE_CLI.md`** — the surface's design and the full secondary-verb set (`check` / `explain`
  / `seal` / `report`).
- **`THE_DATA_PRODUCERS.md`** — when you feed data up into a cloud environment (`synthetic` /
  `peer` / `legacy`), the `rendition` flag, and the golden user-re-key discipline.
- **`README.md`** — the codebase layout (for contributors).
