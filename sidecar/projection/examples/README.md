# examples/ — a worked `projection.json`

`projection.sample.json` is the canonical cloud-insertion estate: the model read **live**
from your cloud OutSystems environment, an on-prem publish path, and the three data
producers (`golden` / `preview` / `synth`) landing in a managed OutSystems environment sink. JSON carries no
comments, so every key is explained below. (Start instead from `projection init` for the
minimal scaffold; this sample is the real shape.) Walkthrough: `../GETTING_STARTED.md`;
design: `../THE_CLI.md`.

To use it: copy it to your working directory as `projection.json`, then drop one connection
string into each `secrets/<name>.conn` file the `file:` refs name. The engine reads those
files directly at run time — no shell `source`, no env vars.

```bash
cp examples/projection.sample.json ./projection.json
mkdir -p secrets && chmod 700 secrets
for n in ossys cloud-qa cloud-uat onprem-legacy; do
  cp examples/secret.conn.example "secrets/$n.conn"      # then replace the placeholder
done
projection                       # list the resolved flows
projection golden                # preview a flow
```

(Each `secrets/<name>.conn` holds exactly one connection string — the whole file, trimmed, so
no comment lines. `secrets/` and `*.conn` are gitignored; `projection.json` only names the
`file:` references. See `../GETTING_STARTED.md` §6. `examples/secret.conn.example` is the
committed template.)

## One unified document — movement + shaping behind two views

This sample is one **unified `projection.json`** (THE_CONFIG_CONTROL_PLANE): the movement
view (`environments`/`flows`) and the model-shaping view (`model`/`overrides`/`emission`/
`policy`/…) are sibling top-level namespaces of the **same** file. A daily `projection <flow>`
run now **bakes the shaping into the emission** — the `overrides.tableRenames`, `emission`
toggles, `policy` tightening, and `model.modules` scope flow into the bundle the flow
publishes. (A movement-only file omits the shaping namespaces and is byte-identical to before.)

The model is read **live from a cloud OutSystems environment** via the canonical `model`
object's `ossys` ref — the engine reads OutSystems metadata directly (native GUID `SsKey`,
no V1) and shares that one read across every flow.

| Top-level key | Meaning |
|---|---|
| `model` | the canonical model object. `ossys` is the **primary** live OSSYS connection ref (`env:<VAR>` / `file:<path>`); `path` is the **fallback** `osm_model.json` (kept for cutover safety; live wins per `ModelResolution.chooseOrigin`); `modules` scopes which modules/entities are in scope. (Legacy top-level `modelOssys` / `model: "<path>"` still map in.) |
| `overrides` | entity/table renames (`tableRenames` — logical `module::entity` OR physical `schema.table` form), emission-folder overrides, etc. (`../CONFIG_REFERENCE.md`). |
| `emission` | which artifact kinds the bundle emits (`ssdt`/`dacpac`/…). |
| `policy` | tightening interventions (nullability / unique / FK budgets). |
| `environments` | the **places** (address + permissions). |
| `flows` | the **movements** (named `source → target` recipes). |

## The environments in this sample

| Name | access | grant | rendition | Role |
|---|---|---|---|---|
| `onprem-dev` / `onprem-qa` / `onprem-uat` | `bundle` → `./dist/...` | `schema+data` | `logical` | emit SSDT files for the on-prem Octopus pipeline (the schema delivery path) |
| `onprem-legacy` | `direct` (`file:./secrets/onprem-legacy.conn`) | *(none)* | `logical` | the hosted on-prem model the migration team loads — the **B** source of the reverse leg |
| `cloud-qa` | `direct` (`file:./secrets/cloud-qa.conn`) | *(none)* | `physical` | the cloud QA cell — the `peer` source for golden data |
| `cloud-uat` | `direct` (`file:./secrets/cloud-uat.conn`) | `data` | `physical` | the managed OutSystems environment **sink** — DML-only cloud insertion (R6) |

### Why the grants differ — source vs. sink (not dev vs. prod)

`grant` is a property of the **sink** (what may change *there*), not a tier policy:

- A **source** environment (a flow's `from`) is read-only and carries **no grant** —
  `cloud-qa` and `onprem-legacy` here. (A grant on a read-only source is meaningless.)
- A **sink** carries the grant for what the flow changes there:
  - **`data`** — DML only; the schema must already agree. The **cloud-insertion sink**
    (`cloud-uat`) is `data`: V2 owns no schema write to a live cloud OutSystems environment
    (R6) — it inserts production-like rows, it does not alter the cloud schema. A
    schema-changing flow against a `data` target is refused loudly.
  - **`schema+data`** — DDL+DML; belongs to the **bundle** publish targets (`onprem-*`),
    where the SSDT files carry the full create/alter for the on-prem pipeline to apply.

So in this estate, **schema travels the on-prem bundle path; data lands in the cloud via
DML-only flows.** That is the variegation — driven by *role and delivery mechanism*, not by
environment tier.

- **`rendition`** marks which shape of the one model a place bears: `physical` = the OSUSR
  cloud rendition (A); `logical` = the hosted on-prem rendition (B). The reverse-leg
  (`preview`) move goes logical → physical (`../THE_DATA_PRODUCERS.md`).
- **`store`** is the durable episode timeline (`seal` writes it, `report` diffs it).
- **`conn`** is always an `env:<VAR>` / `file:<path>` reference — never a literal (D9).

## The flows in this sample

| Flow | from → to | What it is |
|---|---|---|
| `publish` | model → onprem-uat | emit the SSDT bundle (schema) from the live model for the on-prem pipeline. `onprem-uat` carries a `store`, so this fires the **publish-with-provenance** path (`ConfigFile → PublishBundle`) — the full-export bundle with the episode store. |
| `skeleton` | model → onprem-dev | `"shape": "skeleton"` — emit the **pre-overlay baseline** (no operator overlays), the flow-expressible form of `osm emit --skeleton-only`. |
| `audit` | model → onprem-dev | `"shaping": { … }` — a per-flow override that **deep-overlays the global shaping** (here narrowing `model.modules` to `Ops`) for this flow's emission only. |
| `golden` | cloud-qa → cloud-uat | copy a **subset** of tables, **re-keying user FKs** to UAT's own users by email (`rekey`); the `peer` producer |
| `preview` | onprem-legacy → cloud-uat | the **legacy B→A reverse leg**: pipe the migration team's data up from the logical on-prem model into the physical cloud |
| `synth` | synthetic → cloud-uat | generate data matching `cloud-qa`'s profile and load it (no real rows cross) |

Flow keys: `to` (required), `from` (an env name or `model`/`synthetic`/`none`; default
`model`), `profile` (the env to profile for `synthetic`), `tables` (a subset — "golden"
data), `rekey` (a `file:` user-FK map), `scope` (`schema`/`data`/`both` — the move's
projection, decoupled from `grant`), `shape` (`bundle`/`ssdt`/`skeleton` — the bundle
composition), `shaping` (an opt-in per-flow shaping override, deep-overlaid over the global).

## Running them

```bash
projection golden                                # preview the cloud→cloud golden copy
PROJECTION_ALLOW_EXECUTE=1 projection golden --go # apply it (both keys required for a live write)
```

A live write to a `direct` target needs **both** `--go` and `PROJECTION_ALLOW_EXECUTE=1`;
otherwise it is refused (exit 7), never silently downgraded. See `../GETTING_STARTED.md` §7.
