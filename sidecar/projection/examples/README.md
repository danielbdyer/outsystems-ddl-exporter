# examples/ — a worked `projection.json`

`projection.sample.json` is a complete, valid four-tier estate you can adapt. JSON carries
no comments, so every key is explained below. (Start instead from `projection init` for the
minimal scaffold; this sample shows the full surface.) For the step-by-step walkthrough see
`../GETTING_STARTED.md`; for the design see `../THE_CLI.md`.

To use it: copy it to your working directory as `projection.json` (or point
`PROJECTION_CONFIG` at it), then export the connection variables it references (below).

```bash
cp examples/projection.sample.json ./projection.json
export ONPREM_LEGACY_CONN="…"  CLOUD_DEV_CONN="…"  CLOUD_QA_CONN="…"  CLOUD_UAT_CONN="…"
projection                      # list the resolved flows
projection dev                  # preview a flow
```

## Top-level keys

| Key | Meaning |
|---|---|
| `model` | path to an exported `osm_model.json` — the default content source for `from: model` flows. |
| `modelOssys` *(not shown)* | a live OSSYS connection ref (`env:<VAR>`) to read OutSystems metadata directly. When set it **wins** over `model`. Use one or the other. |
| `environments` | the **places** (defined once, with address + permissions). |
| `flows` | the **movements** (named `source → target` recipes). |

## The environments in this sample

| Name | access | grant | rendition | Role |
|---|---|---|---|---|
| `local` | `docker` | — | — | a throwaway ephemeral database for zero-setup previews |
| `onprem-publish` | `bundle` → `./dist/onprem-publish` | `schema+data` | `logical` | produce SSDT files for an Octopus pipeline (no live write) |
| `onprem-legacy` | `direct` (`env:ONPREM_LEGACY_CONN`) | `data` | `logical` | the hosted on-prem model the migration team loads — the **B** source of the reverse leg |
| `cloud-dev` | `direct` (`env:CLOUD_DEV_CONN`) | `schema+data` | `physical` | the cloud development cell |
| `cloud-qa` | `direct` (`env:CLOUD_QA_CONN`) | `schema+data` | `physical` | the cloud QA cell (the `peer` source for golden data) |
| `cloud-uat` | `direct` (`env:CLOUD_UAT_CONN`) | `data` | `physical` | the cloud UAT cell — **data-only** (schema must already agree) |

- **`access`** — `bundle` writes files; `direct` writes to a live connection; `docker` is ephemeral.
- **`grant`** is a refusal gate: a schema-changing flow against a `data` target is refused loudly.
- **`rendition`** is env metadata (not a gate): `physical` = the OSUSR cloud shape (A); `logical`
  = the hosted on-prem shape (B). It marks the renditions the cloud-insertion reverse leg moves
  between (`../THE_DATA_PRODUCERS.md`). The same-rendition surface can omit it.
- **`store`** is the durable episode timeline a place accumulates (`seal` writes it, `report` diffs it).
- **`conn`** is always an `env:<VAR>` or `file:<path>` **reference** — never a literal string (D9).

## The flows in this sample

| Flow | from → to | What it is |
|---|---|---|
| `try` | model → local | preview the model into a throwaway Docker database (zero setup) |
| `publish` | model → onprem-publish | emit the SSDT bundle for the on-prem pipeline |
| `dev` | model → cloud-dev | put the model on the cloud dev cell |
| `qa` | cloud-dev → cloud-qa | promote dev's state to QA |
| `golden` | cloud-qa → cloud-uat | copy a **subset** of tables, **re-keying user FKs** to UAT's own users by email (`rekey`); the `peer` producer |
| `preview` | onprem-legacy → cloud-uat | the **legacy B→A reverse leg**: pipe the migration team's data up from the logical on-prem model into the physical cloud |
| `synth` | synthetic → cloud-uat | generate data matching `cloud-qa`'s profile and load it (no real rows cross) |

Flow keys: `to` (required), `from` (an env name or `model`/`synthetic`/`none`; default
`model`), `profile` (the env to profile for `synthetic`), `tables` (a subset — "golden"
data), `rekey` (a `file:` user-FK map).

## Running them

```bash
projection try                                   # preview into Docker
projection golden                                # preview the cloud→cloud golden copy
PROJECTION_ALLOW_EXECUTE=1 projection golden --go # apply it (both keys required for a live write)
```

A live write to a `direct` target needs **both** `--go` and `PROJECTION_ALLOW_EXECUTE=1`;
otherwise it is refused (exit 7), never silently downgraded. See `../GETTING_STARTED.md` §7.
