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
| **.NET SDK 9.0.305** | `global.json` pins it with `rollForward: disable` — another version fails at load time. | `dotnet --version` → `9.0.305` |
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

The scaffold defines three **environments** (places) and three **flows** (movements):

```jsonc
{
  "model": "model.json",
  "environments": {
    "local":      { "access": "docker" },
    "onprem-dev": { "access": "bundle", "out": "./dist/onprem-dev", "grant": "schema+data" },
    "cloud-dev":  { "access": "direct", "conn": "env:CLOUD_DEV_CONN", "grant": "schema+data" }
  },
  "flows": {
    "try":     { "from": "model", "to": "local" },
    "publish": { "from": "model", "to": "onprem-dev" },
    "dev":     { "from": "model", "to": "cloud-dev" }
  }
}
```

List what is configured (the resolved `source → target` of each flow):

```bash
projection                 # → try: model → local · publish: model → onprem-dev · dev: model → cloud-dev
```

Point `"model"` at an exported `osm_model.json` (or set `"modelOssys": "env:OSSYS_CONN"` to
read OutSystems metadata live), then **preview** the zero-setup flow — it deploys into a
throwaway Docker database and reports the change without touching anything real:

```bash
projection try             # preview (dry-run); nothing is committed
```

A `bundle` flow writes SSDT files for a deployment pipeline (Octopus) — no database needed:

```bash
projection publish         # writes ./dist/onprem-dev/...  (CREATE files + RefactorLog + scripts)
```

---

## 5. Environments and flows — the config reference

`projection.json` (or the file named by `$PROJECTION_CONFIG`) has two blocks. The parser
reads exactly the keys below; **unknown keys are silently ignored**, so spelling matters.

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
- **`grant`** is a gate, not a setting: a flow that changes schema against a `data`-only
  target is a **type mismatch, refused loudly** — never half-applied.

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

**Connection strings never live in `projection.json`.** A `conn` is a *reference*, resolved
at run time. There are exactly two forms:

| Form | Resolves to | Example |
|---|---|---|
| `env:<VAR>` | the environment variable `<VAR>` | `"conn": "env:CLOUD_UAT_CONN"` → reads `$CLOUD_UAT_CONN` |
| `file:<path>` | the first line of a text file (relative to the working dir) | `"conn": "file:./secrets/uat.txt"` |

A value that looks like an inline secret (contains `;`, `password`, `pwd=`) is **refused at
parse time** (`cli.config.envSecretInline`, exit 6). This holds by construction: the
committed file carries only addressing, never credentials.

**Multi-environment** is just one reference per place; you manage the variable inventory
(names are your choice — no convention is enforced):

```jsonc
// projection.json
{
  "environments": {
    "cloud-dev":  { "access": "direct", "conn": "env:CLOUD_DEV_CONN",  "grant": "schema+data" },
    "cloud-qa":   { "access": "direct", "conn": "env:CLOUD_QA_CONN",   "grant": "schema+data" },
    "cloud-uat":  { "access": "direct", "conn": "env:CLOUD_UAT_CONN",  "grant": "data" }
  },
  "flows": {
    "qa":  { "from": "cloud-dev", "to": "cloud-qa" },
    "uat": { "from": "cloud-qa",  "to": "cloud-uat", "rekey": "file:users.csv" }
  }
}
```

```bash
export CLOUD_DEV_CONN="Server=cloud-dev.example;Database=app;User Id=svc;Password=…;Encrypt=true"
export CLOUD_QA_CONN="…"
export CLOUD_UAT_CONN="…"
```

Keep these in your shell profile, a `.env` you do **not** commit, or a secret manager — out
of the repository. (`file:` refs are handy when a secret manager renders a file at deploy time.)

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
projection uat                                   # preview — reads A, shows B ⊖ A, writes nothing
PROJECTION_ALLOW_EXECUTE=1 projection uat --go   # apply — needs BOTH the env var and --go
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

- **`examples/`** — a fuller annotated `projection.sample.json` (a four-environment estate).
- **`THE_CLI.md`** — the surface's design and the full secondary-verb set (`check` / `explain`
  / `seal` / `report`).
- **`THE_DATA_PRODUCERS.md`** — when you feed data up into a cloud environment (`synthetic` /
  `peer` / `legacy`), the `rendition` flag, and the golden user-re-key discipline.
- **`README.md`** — the codebase layout (for contributors).
