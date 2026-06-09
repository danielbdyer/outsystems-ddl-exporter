# CONFIG_REFERENCE.md — the model-shaping configuration

There are **two** configuration surfaces, and they are deliberately separate:

| File | Surface | What it controls | Doc |
|---|---|---|---|
| `projection.json` | **movement** | environments (places) + flows (source→target recipes) — *where* a model/data moves | `GETTING_STARTED.md` |
| a `Config` JSON (this doc) | **model shaping** | *what the model IS* before it moves — which modules/entities are in scope, entity/table renames, emission toggles, tightening policy, type mappings | here |

They are parsed by different loaders (`MovementSurface.fs` vs `Config.fs`), validated independently, and one carries none of the other's keys. This doc is the **model-shaping** config: the build-time specification of the catalog the pipeline projects.

> **D9 holds here too:** a model source may be a connection *reference* (`ossys: "env:<VAR>"` / `"file:<path>"`), never a literal string. Any property whose *name* matches a credential signature (`password`, `secret`, `connectionString`, `accessToken`, `apiKey`, …) is refused at parse time (`pipeline.config.credentialPropertyForbidden`).

---

## How it is used today

This config is authored and exercised through the **`explain`** verbs — each takes a config path and runs the projection pipeline *with this config's overlays applied*, so you can see the effect before emitting:

```bash
projection explain node    <config.json> <ssKey>        # every transform + finding for one catalog node
projection explain suggest <config.json> [--apply <out>] # ranked config edits (highest-leverage first; --apply writes an updated config)
projection explain policy  <configA.json> <configB.json> # the five-axis structural delta between two configs
```

`explain node` is the workhorse for verifying your shaping: point it at a kind's `SsKey` and confirm the renames / tightening / scope decisions you authored actually fire.

> **Relationship to `projection.json` flows (read this).** This config and `projection.json` are **separate files**. A daily `projection <flow>` run resolves its model from the flow surface's `model` / `modelOssys` (a plain model or live OSSYS) — it does **not** layer this config's module-scope / renames / policy onto a flow emission today. So author and validate the shaping here (via `explain`); a `publish` flow emits the model as-resolved. (Wiring this config in as a flow's model — so `projection publish` bakes the overlays into the bundle — is the natural next connection; it is not in place yet.)

---

## The full schema

Every key, with type · required? · default. Unknown keys are ignored; type mismatches are refused (`pipeline.config.typeMismatch`).

### `model` — the source and what's in scope

| Key | Type | Req? | Default | Meaning |
|---|---|---|---|---|
| `path` | string | one of path/ossys | — | path to an exported `osm_model.json` (the fallback source) |
| `ossys` | string | one of path/ossys | — | a live OSSYS connection **reference** (`env:`/`file:`) — the primary source when set |
| `modules` | array | no | `[]` (all) | **in-scope selector.** Each entry is a bare string `"AppCore"` (whole module) **or** an object `{ "name": "ServiceCenter", "entities": ["User","Organization"] }` (entity-level filter) |
| `includeSystemModules` | bool | no | `false` | include OutSystems system modules |
| `includeInactiveModules` | bool | no | `false` | include inactive modules |
| `onlyActiveAttributes` | bool | no | `true` | keep only active attributes |
| `validationOverrides` | object | no | `{}` | `{ "allowMissingSchema": ["Mod::*"] }` — suppress missing-schema validation for the listed schemas |

### `overrides` — naming and structural directives

| Key | Type | Default | Meaning |
|---|---|---|---|
| `tableRenames` | array | `[]` | each `{ "from": <source>, "to": { "schema": "dbo", "table": "NEW" } }`. `from` is **either** logical `{ "module": "M", "entity": "E" }` **or** physical `{ "schema": "S", "table": "T" }` (not both — `renameSourceAmbiguous` / `renameSourceMissing` otherwise) |
| `emissionFolders` | array | `[]` | `{ "ref": { "module": "M", "entity": "E" }, "folder": "Static/Reference" }` — redirect an entity's SSDT `.sql` from `Modules/<M>/` to a custom folder |
| `allowMissingPrimaryKey` | array | `[]` | `[{ "module": "M", "entity": "E" }, …]` — entities exempt from PK enforcement |
| `circularDependencies` | object | — | `{ "allowedCycles": [{ "tableOrdering": [{ "tableName": "T", "position": N }, …] }], "strictMode": bool }` |
| `migrationDependencies` | object | — | `{ "path": "overrides/mig.json" }` — a custom migration-dependency graph |
| `staticData` | object | — | `{ "path": "overrides/static.json" }` — static-data seed config |

### `emission` — which artifacts to emit (all default `true`)

`ssdt` · `dacpac` · `json` · `distributions` · `staticSeeds` · `migrationDependencies` · `bootstrap` · `decisionLog` · `opportunities` · `validations` — each a `bool`. Set to `false` to suppress that artifact.

### `policy` — the operator overlays

| Key | Type | Default | Meaning |
|---|---|---|---|
| `selection` | string | `"IncludeAll"` | kind-selection policy |
| `insertion` | string | `"SchemaOnly"` | data-insertion policy |
| `userMatching` | object | `{ "strategy": "ByEmail", "fallback": "NoFallback" }` | user re-key strategy + fallback |
| `transformGroups` | array | `[]` (all on) | `[{ "name": "Tightening", "enabled": true }, …]` — toggle whole transform groups |
| `tightening` | object | — | `{ "interventions": [ … ] }` — the nullability / uniqueIndex / foreignKey / categoricalUniqueness rules (below) |

**`tightening.interventions[]`** — each carries `kind` + `id` + kind-specific fields:

- `kind: "nullability"` — `nullBudget` (decimal 0–1), `allowMandatoryRelaxation` (bool), `overrides: [{ "attributeRef": "AppCore.User.MiddleName", "action": "keepNullable" }]`
- `kind: "uniqueIndex"` — `enforceSingleColumnUnique`, `enforceMultiColumnUnique` (bool)
- `kind: "foreignKey"` — `enableCreation`, `allowCrossSchema`, `allowCrossCatalog`, `treatMissingDeleteRuleAsIgnore`, `allowNoCheckCreation` (bool)
- `kind: "categoricalUniqueness"` — `minDistinctCountForUniqueness` (int)

### `typeMapping` · `profiler` · `cache` · `output`

| Section | Keys | Default |
|---|---|---|
| `typeMapping` | `path` (rules JSON) · `default` (string) · `overrides` `{ "OutSystemsType": "SqlType" }` | `{}` |
| `profiler` | `provider` `"fixture"` \| `"live"` (live reads `PROJECTION_MSSQL_CONN_STR`) · `mockFolder` | `{ provider: "fixture" }` |
| `cache` | `root` · `refresh` · `ttlSeconds` | `.artifacts/cache` · `false` · `7200` |
| `output` | `dir` | `out/` |

**Parser refusals** (`pipeline.config.*`): `jsonInvalid`, `fileNotFound`, `fileReadError`, `missingProperty`, `modelNoSource` (neither `model.path` nor `model.ossys`), `typeMismatch`, `nullProperty`, `nullArrayElement`, `credentialPropertyForbidden` (D9), `renameSourceAmbiguous`, `renameSourceMissing`.

---

## A robust worked example

A config that exercises every major axis — module + entity scoping, both rename forms, an emission-folder redirect, a nullability tightening intervention, and the emission toggles. The committed, copy-pasteable version is **`examples/model.config.sample.json`** (validated: it parses against the live `Config` loader and `explain node` runs the pipeline over it). The `//` annotations below are illustrative — the real file is comment-free JSON.

```jsonc
// model-shaping config — copy examples/model.config.sample.json (this view is annotated)
{
  "model": {
    "ossys": "file:./secrets/ossys.conn",        // primary: read the model live (env:/file: ref)
    "path":  "extracted/osm_model.json",          // fallback if no live connection

    "modules": [
      "AppCore",                                  // whole module
      { "name": "ServiceCenter", "entities": ["User", "Organization"] }  // entity-level
    ],
    "includeSystemModules": false,
    "includeInactiveModules": false,
    "onlyActiveAttributes": true,
    "validationOverrides": { "allowMissingSchema": ["Mod::*"] }
  },

  "overrides": {
    "tableRenames": [
      { "from": { "module": "AppCore", "entity": "Customer" }, "to": { "schema": "dbo", "table": "CUSTOMER" } },
      { "from": { "schema": "dbo", "table": "OSUSR_ABC_ORDER" }, "to": { "schema": "dbo", "table": "ORDER_HEADER" } }
    ],
    "emissionFolders": [
      { "ref": { "module": "AppCore", "entity": "Country" }, "folder": "Static/Reference" }
    ],
    "allowMissingPrimaryKey": [
      { "module": "AppCore", "entity": "LegacyAuditLog" }
    ],
    "circularDependencies": {
      "allowedCycles": [
        { "tableOrdering": [
            { "tableName": "OSUSR_ABC_ORG",  "position": 100 },
            { "tableName": "OSUSR_ABC_USER", "position": 200 }
        ] }
      ],
      "strictMode": false
    }
  },

  "emission": {
    "ssdt": true, "dacpac": true, "json": true, "staticSeeds": true,
    "distributions": false, "opportunities": false
  },

  "policy": {
    "selection": "IncludeAll",
    "insertion": "SchemaOnly",
    "userMatching": { "strategy": "ByEmail", "fallback": "NoFallback" },
    "tightening": {
      "interventions": [
        {
          "kind": "nullability",
          "id": "promote-mandatory-where-clean",
          "nullBudget": 0.001,
          "allowMandatoryRelaxation": false,
          "overrides": [
            { "attributeRef": "AppCore.User.MiddleName", "action": "keepNullable" }
          ]
        }
      ]
    },
    "transformGroups": [ { "name": "Tightening", "enabled": true } ]
  },

  "output": { "dir": "out/" }
}
```

Verify it before relying on it — `explain node` runs the pipeline with these overlays and reports one node's decisions:

```bash
projection explain node ./model.config.json "AppCore.Customer"   # confirm the rename + tightening fired
projection explain suggest ./model.config.json                    # ranked edits this config is missing
```

(Real connection strings stay in `./secrets/*.conn`, gitignored — `model.ossys` only names the `file:` reference.)
