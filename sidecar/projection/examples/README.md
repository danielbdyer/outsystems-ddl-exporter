# examples/ — a worked `projection.json`

`projection.sample.json` is a realistic **six-environment OutSystems estate**: three managed
OutSystems **cloud** cells (`cloud-dev` / `cloud-qa` / `cloud-uat`) and the three **on-prem**
SQL Server tiers they publish down to (`on-prem-dev` / `on-prem-qa` / `on-prem-uat`). It shows
the daily loop end-to-end: the schema published **down** from cloud to on-prem, production-like
data moved into the cloud sink by the three producers (`golden` / `reverse` / `synth`), and the
**cutover-readiness gate** (`check shape`) that proves the cloud cells are one shape before any
of it runs. JSON carries no comments, so every key is explained below. (Start instead from
`projection init` for the minimal scaffold; this sample is the real shape.) Walkthrough:
`../GETTING_STARTED.md`; designs: `../THE_CLI.md`, `../THE_CONFIG_CONTROL_PLANE.md`,
`../CROSS_ENVIRONMENT_READINESS.md`.

To use it: copy it to your working directory as `projection.json`, then drop one connection
string into each `secrets/<name>.conn` file the `file:` refs name. The engine reads those files
directly at run time — no shell `source`, no env vars.

```bash
cp examples/projection.sample.json ./projection.json
mkdir -p secrets && chmod 700 secrets
# the four DIRECT environments need a live connection; the two bundle tiers do not
for n in cloud-dev cloud-qa cloud-uat on-prem-uat; do
  cp examples/secret.conn.example "secrets/$n.conn"      # then replace the placeholder
done
projection                       # list the resolved flows
projection check shape           # the cutover-readiness gate (read-only)
projection golden                # preview a data flow
```

(Each `secrets/<name>.conn` holds exactly one connection string — the whole file, trimmed, so
no comment lines. `secrets/` and `*.conn` are gitignored; `projection.json` only names the
`file:` references. The `golden` flow's user re-key map is a separate `secrets/uat-users.csv`.
See `../GETTING_STARTED.md` §6. `examples/secret.conn.example` is the committed template.)

## One unified document — movement + shaping behind two views

This sample is one **unified `projection.json`** (THE_CONFIG_CONTROL_PLANE): the movement view
(`environments` / `flows` / `readiness`) and the model-shaping view (`model` / `overrides` /
`emission` / `policy`) are sibling top-level namespaces of the **same** file. A daily
`projection <flow>` run **bakes the shaping into the emission** — the `overrides.tableRenames`,
`emission` toggles, `policy` tightening, and `model.modules` scope flow into the bundle the flow
publishes.

The model is read **live from the cloud development environment** via `model.env: "cloud-dev"`
— the schema source named into the `environments` registry by name (the connection lives once,
in `environments.cloud-dev.conn`). The engine reads OutSystems metadata directly (native GUID
`SsKey`, no V1) and shares that one read across every flow. `model.path` is the `osm_model.json`
fallback (cutover safety; live wins per `ModelResolution.chooseOrigin`).

| Top-level key | Meaning |
|---|---|
| `model` | the canonical model object. `env` names the **primary** environment — here the **cloud-dev** cell, where the development team authors the schema — so the connection is named once, not inlined (use `ossys` only for a standalone source with no registry); `path` is the fallback; `modules` scopes which modules/entities are in scope. |
| `overrides` | naming + structural directives, all optional: `tableRenames` (logical or physical source → new name), `emissionFolders` (redirect an entity's `.sql`), `allowMissingPrimaryKey` (PK exemptions), `circularDependencies` (cycle ordering). Keyed to the sample's own entities — swap for yours (`../CONFIG_REFERENCE.md`). |
| `emission` | which artifact kinds the bundle emits — `ssdt` (the schema) plus `staticSeeds` / `migrationDependencies` / `bootstrap` (the data lanes). Each defaults true. |
| `policy` | tightening interventions (`foreignKey` / `uniqueIndex` / `categoricalUniqueness`) — **opt-in**, see the `_comment`. (`nullability` coercion is disabled — the model's declared nullability is authoritative.) |
| `profiler` | source-data evidence — `provider: "live"` profiles the source DB (via `PROJECTION_MSSQL_CONN_STR`); `"fixture"` (default) carries none. |
| `output` | `dir` — where the emitted artifacts land (`out/`). |
| `environments` | the **places** (address + permissions + rendition + archetype). |
| `readiness` | the cutover-readiness set — confirm a group of environments resolve to one shape (`check shape`). |
| `flows` | the **movements** (named `source → target` recipes). |
| `synthetic` | how `from: synthetic` generates data — hybrid-by-cardinality (`preserveCardinalityMax`), per-column `preserve`/`synthesize` (by logical name), `scale`, `seed`. The per-flow `correction` (on the `synth` flow) carries the richer per-column PII→Faker intent. (`../THE_SYNTHETIC_DATA_DESIGN.md` §11.) |
| `slices` | data-portability **use cases** — a logical subgraph (`roots` + traversal `directives`) to extract and apply. **Logical** (module/entity, column names) — espace-safe by construction. Run via `slice-extract`. |

## The estate — six environments

| Name | access | rendition | archetype | grant | Role |
|---|---|---|---|---|---|
| `cloud-dev` | `direct` | `physical` | `managed-dml` | *(none)* | the development cloud cell — the **model source** (`model.env`) and the readiness **agreed shape** (the `readiness.schema` default) |
| `cloud-qa` | `direct` | `physical` | `managed-dml` | `data` | the QA cloud cell — the `golden` **peer source** + the `synth` target |
| `cloud-uat` | `direct` | `physical` | `managed-dml` | `data` | the managed-production cloud **sink** — cloud insertion (R6, DML-only); `golden` + `reverse` land here |
| `on-prem-dev` | `bundle` → `./dist/on-prem-dev` (+ `conn`) | `logical` | `full-rights` | `schema+data` | on-prem SSDT delivery target (Octopus applies the bundle); its `conn` is a live **read** source. Carries a `store` (publish-with-provenance) |
| `on-prem-qa` | `bundle` → `./dist/on-prem-qa` (+ `conn`) | `logical` | `full-rights` | `schema+data` | on-prem SSDT delivery target — the **same** schema version, one tier on; also `conn`-readable |
| `on-prem-uat` | `bundle` → `./dist/on-prem-uat` (+ `conn`) | `logical` | `full-rights` | `schema+data` | the live on-prem pre-prod — SSDT delivery target **and** the **reverse-leg read source** (via `conn`) |

> **The write/read tension, resolved.** An on-prem environment is a real database: schema goes
> **down** to it as an SSDT bundle (file production → Octopus, the `out` folder), and data is read
> **up** from it live (the reverse leg, the `conn`). `access: bundle` governs the *write*; the
> optional `conn` is the *read* connection. All three on-prem tiers carry both, so any of them can
> be a reverse-leg source.

### `rendition` and `archetype` — two renditions, two capability classes

- **`rendition`** marks which shape of the one `SsKey`-stable model a place bears
  (`../THE_DATA_PRODUCERS.md` §0): `physical` = the OSUSR cloud rendition (**A**); `logical` =
  the hosted on-prem rendition (**B**). The cloud cells are `physical`; the on-prem tiers
  `logical`. The reverse leg goes **B → A** (logical → physical).
- **`archetype`** names the capability class (`../DATABASE_ARCHETYPES.md`; the J5 verdict made
  reusable): `managed-dml` = the cloud profile (DML-only, no `ALTER`, sink mints identity,
  client-side journal) — every cloud cell; `full-rights` = on-prem (DDL + `IDENTITY_INSERT`,
  key preservation, truncate-refresh). The engine derives safe interaction from the archetype
  and **verifies** it against the live grant.

### Why the grants differ — source vs. sink (not dev vs. prod)

`grant` is a property of the **sink** (what may change *there*), not a tier policy:

- A **source-only** environment is read-only and carries **no grant** — `cloud-dev` (the model
  source) here. (The on-prem tiers are reverse-leg *read* sources too, but they are also bundle
  *write* sinks — schema comes down to them — so they carry `schema+data`.)
- An environment that is **also a sink** carries the grant for what a flow changes there:
  - **`data`** — DML only; the schema must already agree. The cloud cells (`cloud-qa` is a
    `synth` sink; `cloud-uat` is the cloud-insertion sink) are `data`: V2 owns no schema write
    to a live managed cloud cell (R6). A schema-changing flow against a `data` target is
    refused loudly.
  - **`schema+data`** — DDL+DML; the on-prem **bundle** targets, where the SSDT files carry the
    full create/alter for the Octopus pipeline to apply.

So in this estate, **schema travels DOWN the on-prem bundle path; data lands in the cloud via
DML-only flows — and the reverse leg reads data back UP from on-prem live via its `conn`.**
`store` is the durable episode timeline (`seal` writes it, `report` diffs it);
`revert: script` (on `cloud-uat`) emits a SQL revert script for the data leg (the J5 rollback
channel). `conn` is always an `env:<VAR>` / `file:<path>` reference — never a literal (D9).

## The flows in this sample

| Flow | from → to | What it is |
|---|---|---|
| `publish` | cloud-dev → on-prem-dev | emit the SSDT **bundle** — schema + static seeds + migration data + bootstrap, read live from cloud-dev's model — into `./dist/on-prem-dev` for the Octopus pipeline. `on-prem-dev` carries a `store`, so this fires the **publish-with-provenance** path (the full-export bundle + episode store). |
| `publish-qa` | cloud-dev → on-prem-qa | the **same** schema version landing for the QA tier (the cloud cells are kept in sync, so one schema serves all three). |
| `golden` | cloud-qa → cloud-uat | the **peer** producer (A→A): copy a `tables` subset, **re-keying user FKs** to UAT's own users — `reconcile: ["ServiceCenter.User:Email"]` matches by email (logical `Module.Entity:Col`, espace-safe), with `rekey` (a CSV map) for explicit overrides; `scope: data` (no schema), `strategy: replace`. |
| `reverse` | on-prem-uat → cloud-uat | the **legacy B→A reverse leg** (cloud insertion): pipe the migration team's data up from the logical on-prem model — **read live via on-prem-uat's `conn`** — into the physical cloud. `scope: data` (the schema is mirrored ahead + validated at run time, then only data moves); `streaming` + `resumable` + a client-side `journal`; `strategy: merge` (CDC-minimal). |
| `synth` | synthetic → cloud-qa | generate data matching **on-prem-uat**'s data profile and load it into the lower cloud cell — privacy-safe production-shaped data, no real rows cross. Tuned by the `synthetic` block (cardinality / preserve / synthesize / scale / seed) + a per-flow `correction` (per-column PII→Faker). (`../THE_DATA_PRODUCERS.md`; `../THE_SYNTHETIC_DATA_DESIGN.md` §11.) |

Flow keys: `to` (required), `from` (an env name or `synthetic` / `none`), `profile` (the env to
profile for `synthetic`), `tables` (a subset — "golden" data), `rekey` (a `file:` user-FK map),
`reconcile` (`["Module.Entity:Col"]` — match-by-column re-key, logical/espace-safe), `scope`
(`schema`/`data`/`both` — the move's projection, decoupled from `grant`), `strategy`
(`merge`/`replace`/`fresh`), `streaming` / `resumable` / `journal` (the estate-scale reverse-leg
levers), `correction` (a per-column synthetic-intent file), `shape` (`bundle`/`ssdt`/`skeleton`),
`shaping` (a per-flow shaping override).

Beyond flows, the sample carries `synthetic` (the `from: synthetic` tuning baseline — §11) and
`slices` / `sliceFlows` (data-portability use cases: a logical subgraph to extract and apply;
`sliceFlows` endpoints take an environment **name** or a conn-ref). Both are logical/espace-safe.

## The cutover-readiness gate — `check shape`

Before publishing the schema down or moving data up, you need to know the cloud cells are
**one shape**: same entities, same attributes, so the dev-authored schema is *the* schema for
all three and each cell's data lands in the same shape. The `readiness` block names that
question, and `projection check shape` answers it:

```jsonc
"readiness": { "confirm": ["cloud-dev", "cloud-qa", "cloud-uat"] }
```

The agreed shape (`schema`) **defaults to `model.env`** (`cloud-dev`) — name `schema` explicitly
only to point the gate at a different environment than the emission source.

```bash
projection check shape                 # the go/no-go: schema-equivalence + data dealbreakers
projection check shape --format json   # the machine-read sibling → readiness.json
```

For each `confirm` environment it reads the model **via OSSYS** (native GUID identity) and
diffs it against the agreed shape (`schema`, defaulting to `model.env` = `cloud-dev`), then profiles its data against that
shape. The verdict is **espace-safe by construction**: OutSystems espacing means the physical
`OSUSR_{key}_…` table names differ per environment, but the comparison keys on the stable OSSYS
GUID and compares only the *logical* shape — so a same-model cell diffs to **zero** regardless
of its physical names (the espace-invariance law, `../CROSS_ENVIRONMENT_READINESS.md` §2 /
`../AXIOMS.md` A1-corollary). A non-zero delta is a **real** logical divergence and blocks; a
data dealbreaker (NULLs into NOT-NULL, FK orphans, duplicates into UNIQUE, width/type overflow)
pauses. `check shape` is read-only — a gate, not a move (exit 0 = ready, 5 = not ready, 6 = an
environment could not be read).

## The per-publish changelog — `seal` → `report`

Each `publish` targets a `store`-bearing environment (`on-prem-dev`), so a live run records a
durable **episode**. The provenance pair turns that into a changelog of what changed over time:

```bash
projection seal publish                # freeze "this is the published schema now" as an episode
projection report publish              # the change since the last seal — the migration-team bundle
projection explain diff @<prior> @<new>  # ad-hoc: the schema delta between two recorded models
```

`report` emits the refactorlog deltas + change-manifest + move/CDC counts — primarily a textual
hand-off artifact, anchored on the last seal (`../THE_CLI.md` §8).

## Running them

```bash
projection check shape                              # readiness gate first (read-only)
projection publish --go                             # emit the on-prem-dev SSDT bundle (file production)
projection golden                                   # preview the cloud→cloud golden copy
PROJECTION_ALLOW_EXECUTE=1 projection golden --go   # apply it (both keys required for a live write)
PROJECTION_ALLOW_EXECUTE=1 projection reverse --go  # the B→A reverse leg into cloud-uat
```

A live write to a `direct` target needs **both** `--go` and `PROJECTION_ALLOW_EXECUTE=1`;
otherwise it is refused (exit 7), never silently downgraded. A `bundle` flow (`publish`) writes
only files and needs neither. See `../GETTING_STARTED.md` §7.
