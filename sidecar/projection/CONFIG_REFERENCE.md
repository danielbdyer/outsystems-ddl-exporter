# CONFIG_REFERENCE.md — the model-shaping namespaces of the unified config

There is **one** configuration surface — a single `projection.json` that is the operator's whole control plane (THE_CONFIG_CONTROL_PLANE.md). It is **one document behind two views**, isomorphic to the movement space:

| View | What it controls | Namespaces |
|---|---|---|
| **movement** | *where* a model/data moves — places + recipes | `environments`, `flows`, `readiness` (the cross-environment cutover gate — `CROSS_ENVIRONMENT_READINESS.md`), `defaults` |
| **model shaping** (this doc) | *what the model IS* before it moves — module/entity scope, entity/table renames, emission toggles, tightening policy | `model`, `overrides`, `emission`, `policy`, `profiler`, `output` |

The shaping namespaces fold in as **sibling top-level keys** of the same `projection.json`. A movement-only file leniently defaults every shaping section (so it never fails `modelNoSource`); a file that authors shaping sees them applied. The strict `Config.parse`/`fromFile` loader (this doc's schema) and the lenient movement loader (`MovementSurface.fs`) read the **same** document. The only genuine collision is the two `model` keys, reconciled into one `model` object (legacy top-level `model: "<path>"` maps to `model.path`; `modelOssys` to `model.ossys`).

> **D9 holds here:** a model source may be a connection *reference* (`ossys: "env:<VAR>"` / `"file:<path>"`), never a literal string. Any property whose *name* matches a credential signature (`password`, `secret`, `connectionString`, `accessToken`, `apiKey`, …) is refused at parse time (`pipeline.config.credentialPropertyForbidden`).

---

## How it is used today

### Flow emissions bake the shaping in (the unified control plane)

A daily `projection <flow>` run **applies the shaping** to its emission — the `overrides`/`emission`/`policy`/`model.modules` you author here flow into the bundle the flow publishes. Two seams carry this (resolved 2026-06-10, S6 of THE_CONFIG_CONTROL_PLANE):

- **Overlay-aware emit.** The `EmitBundle`/Docker/preview/migrate arms route the resolved catalog through `Compose.projectWithConfig` / `applyShapingToCatalog`, so `model.modules` scoping, `overrides.tableRenames`, `emission` toggles, and `policy` tightening all fire on a flow emission. (`Config.defaultConfig` shaping is byte-identical to the un-shaped project — the empty-default invariant.)
- **Publish-with-provenance.** When a flow targets a **store-bearing** place (`environments.<name>.store` set) with a `model` path configured, `resolveFlowSpec` emits `ModelSource.ConfigFile`, firing the `PublishBundle` (folder) / `PublishAndLoad` (live `--go`) arms — the full-export bundle with the provenance/episode store. Store-less targets keep the byte-identical plain-model path.

Two per-flow knobs narrow the global control plane (both opt-in; absent = the global, byte-identical):

- **`flows.<name>.shape`** — `"bundle"` (default) | `"ssdt"` | `"skeleton"`. `skeleton` emits the pre-overlay baseline (the `osm emit --skeleton-only` surface, now flow-expressible).
- **`flows.<name>.shaping`** — a nested object that deep-overlays the global shaping for THIS flow only, at **whole-section granularity** (a section the flow authors replaces the global's; sections the flow leaves silent keep the global's). See DECISIONS 2026-06-10.

### The `explain` verbs

The shaping is also authored and exercised through the **`explain`** verbs — each takes a `projection.json` path and runs the projection pipeline *with the shaping overlays applied*, so you can see the effect before emitting:

```bash
projection explain node    <projection.json> <ssKey>        # every transform + finding for one catalog node
projection explain suggest <projection.json> [--apply <out>] # ranked config edits (highest-leverage first; --apply writes an updated config)
projection explain policy  <projectionA.json> <projectionB.json> # the five-axis structural delta between two configs
```

`explain node` is the workhorse for verifying your shaping: point it at a kind's `SsKey` and confirm the renames / tightening / scope decisions you authored actually fire — and a `publish` flow now bakes those same overlays into the bundle.

---

## The full schema

Every key, with type · required? · default. Unknown keys are ignored; type mismatches are refused (`pipeline.config.typeMismatch`).

### `model` — the source and what's in scope

| Key | Type | Req? | Default | Meaning |
|---|---|---|---|---|
| `env` | string | one of env/ossys/path | — | the **primary-environment reference** — names an entry in `environments` (the canonical source the estate derives from). Resolves to that env's live OSSYS `conn` (espace-safe, native GUIDs), so the connection is **named once**. Unified-config only (needs an `environments` registry). Mutually exclusive with `ossys`. |
| `ossys` | string | one of env/ossys/path | — | a live OSSYS connection **reference** (`env:<VAR>` / `file:<path>`) — a *standalone* primary source for a registry-less config (the model-shaping file). Prefer `env` in the unified `projection.json`. |
| `path` | string | one of env/ossys/path | — | path to an exported `osm_model.json` (the fallback source) |
| `modules` | array | no | `[]` (all) | **in-scope selector.** Each entry is a bare string `"Sales"` (whole module) **or** an object `{ "name": "ServiceCenter", "entities": ["User","Organization"] }` (entity-level filter) |
| `includeSystemModules` | bool | no | `false` | include OutSystems system modules |
| `includeInactiveModules` | bool | no | `false` | include inactive modules |
| `onlyActiveAttributes` | bool | no | `true` | keep only active attributes |

> **`model.env` — the schema source as an environment reference.** Like `flow.from` and
> `readiness.schema`, `env` points into the `environments` registry **by name** rather than
> inlining a connection that would duplicate the environment's own `conn`. It is resolved by the
> movement surface (`ProjectionConfig.parse`), which is the only surface carrying the registry,
> into the same `model.ossys` the live read consumes — so the resolution is transparent (no
> behavioural fork from the explicit-`ossys` form). When a `readiness` block omits its `schema`,
> that **defaults to `model.env`** — the canonical environment named once serves both emission and
> the cutover gate. (Do not confuse the `env` *field*, an environment name, with the `env:<VAR>`
> *conn-ref scheme* used inside `ossys` / `conn` values.) See `CROSS_ENVIRONMENT_READINESS.md` §4.

### `overrides` — naming and structural directives

| Key | Type | Default | Meaning |
|---|---|---|---|
| `tableRenames` | array | `[]` | each `{ "from": <source>, "to": { "schema": "dbo", "table": "NEW" } }`. `from` is **either** logical `{ "module": "M", "entity": "E" }` **or** physical `{ "schema": "S", "table": "T" }` (not both — `renameSourceAmbiguous` / `renameSourceMissing` otherwise) |
| `emissionFolders` | array | `[]` | `{ "ref": { "module": "M", "entity": "E" }, "folder": "Static/Reference" }` — redirect an entity's SSDT `.sql` from `Modules/<M>/` to a custom folder |
| `allowMissingPrimaryKey` | array | `[]` | `[{ "module": "M", "entity": "E" }, …]` — entities exempt from PK enforcement |
| `circularDependencies` | object | — | `{ "allowedCycles": [{ "order": [{ "module": "M", "entity": "E", "position": N }, …] }], "strictMode": bool }` — manual cycle ordering keyed by **logical** `{ module, entity }` (espace-safe; resolved to the kind's SsKey, like the other overrides) |
| `migrationDependencies` | object | — | `{ "path": "overrides/mig.json" }` — a custom migration-dependency graph |
| `staticData` | object | — | `{ "path": "overrides/static.json" }` — static-data seed config |

### `emission` — which artifacts to emit (all default `true`)

`ssdt` · `dacpac` · `json` · `distributions` · `staticSeeds` · `migrationDependencies` · `bootstrap` · `decisionLog` · `opportunities` · `validations` — each a `bool`. Set to `false` to suppress that artifact.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `includePlatformAutoIndexes` | bool | `true` | `false` prunes OutSystems platform-auto indexes from the SSDT bundle and the dacpac at the post-chain seam (reconciliation slice 2; V1's `SsdtManifestOptions.IncludePlatformAutoIndexes`) |
| `identityAnnotations` | bool | `true` | `false` is the NAMED DOWNGRADE (NM-70 / WP5): suppresses the `Projection.SsKey` / `Projection.LogicalName` identity extended properties so they are not written to the SSDT bundle. Other extended properties (Descriptions, authored properties) still emit. Identity recovery degrades to name-derived SsKeys (no persisted SsKey to read back on roundtrip); the run records the `emission.identityAnnotations.omitted` Warning diagnostic. |

### `policy` — the operator overlays

| Key | Type | Default | Meaning |
|---|---|---|---|
| `insertion` | string | `"SchemaOnly"` | data-insertion policy |
| `transformGroups` | array | `[]` (all on) | `[{ "name": "Tightening", "enabled": true }, …]` — toggle whole transform groups |
| `tightening` | object | — | `{ "interventions": [ … ] }` — the nullability / uniqueIndex / foreignKey / categoricalUniqueness rules (below) |

**`tightening.interventions[]`** — each carries `kind` + `id` + kind-specific fields:

- `kind: "nullability"` — `nullBudget` (decimal 0–1), `allowMandatoryRelaxation` (bool), `overrides: [{ "attributeRef": "ServiceCenter.User.MiddleName", "action": "keepNullable" }]`
- `kind: "uniqueIndex"` — `enforceSingleColumnUnique`, `enforceMultiColumnUnique` (bool)
- `kind: "foreignKey"` — `enableCreation`, `allowCrossSchema`, `allowCrossCatalog`, `treatMissingDeleteRuleAsIgnore`, `allowNoCheckCreation` (bool)
- `kind: "categoricalUniqueness"` — `minDistinctCountForUniqueness` (int)

### `profiler` · `output`

| Section | Keys | Default |
|---|---|---|
| `profiler` | `provider` `"fixture"` \| `"live"` (live profiles the source DB via `PROJECTION_MSSQL_CONN_STR` — D9, never the config — so tightening has null-density evidence) | `{ provider: "fixture" }` |
| `output` | `dir` | `out/` |

**Parser refusals** (`pipeline.config.*`): `jsonInvalid`, `fileNotFound`, `fileReadError`, `missingProperty`, `modelNoSource` (none of `model.env` / `model.ossys` / `model.path`), `typeMismatch`, `nullProperty`, `nullArrayElement`, `credentialPropertyForbidden` (D9), `renameSourceAmbiguous`, `renameSourceMissing`.

**`model.env` resolution refusals** (`cli.config.*`, movement surface): `modelEnvAndOssys` (`env` + `ossys` both set — two ways to name the one live source), `modelEnvUnknown` (`env` names an environment absent from `environments`), `modelEnvNotDirect` (`env` names a `bundle`/`docker` place with no live OSSYS connection to read).

---

## A robust worked example

A config that exercises every major axis — module + entity scoping, both rename forms, an emission-folder redirect, a nullability tightening intervention, and the emission toggles. This is the **shaping-only view** shown in isolation — the `model` / `overrides` / `emission` / `policy` sections with no `environments`/`flows` estate. It is a strict **subset of the unified [`examples/projection.sample.json`](examples/projection.sample.json)** (same sections, same loader): a daily run discovers `projection.json`; the analysis verbs (`explain` / `suggest` / `policy diff`) accept either this shaping-only form or the full `projection.json` by path. The `//` annotations are illustrative — strip them for real JSON.

```jsonc
// the model-shaping view in isolation (a subset of projection.json) — annotated; strip comments for real JSON
{
  "model": {
    "ossys": "file:./secrets/cloud-dev.conn",     // a standalone live source (env:/file: ref). In a unified projection.json, prefer `env: "<name>"` (names an environment); the analysis verbs read via ossys/path, not env.
    "path":  "extracted/osm_model.json",          // fallback if no live connection

    "modules": [
      "Sales",                                    // whole module
      { "name": "ServiceCenter", "entities": ["User", "Organization"] }  // entity-level
    ],
    "includeSystemModules": false,
    "includeInactiveModules": false,
    "onlyActiveAttributes": true
  },

  "overrides": {
    "tableRenames": [
      { "from": { "module": "Sales", "entity": "Customer" }, "to": { "schema": "dbo", "table": "Customer" } },
      { "from": { "schema": "dbo", "table": "OSUSR_SAL_ORDER" }, "to": { "schema": "dbo", "table": "OrderHeader" } }
    ],
    "emissionFolders": [
      { "ref": { "module": "Sales", "entity": "Country" }, "folder": "Static/Reference" }
    ],
    "allowMissingPrimaryKey": [
      { "module": "Sales", "entity": "LegacyAuditLog" }
    ],
    "circularDependencies": {
      "allowedCycles": [
        { "order": [
            { "module": "ServiceCenter", "entity": "Organization", "position": 100 },
            { "module": "ServiceCenter", "entity": "User",         "position": 200 }
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
    "insertion": "SchemaOnly",
    "tightening": {
      "interventions": [
        {
          "kind": "nullability",
          "id": "promote-mandatory-where-clean",
          "nullBudget": 0.001,
          "allowMandatoryRelaxation": false,
          "overrides": [
            { "attributeRef": "ServiceCenter.User.MiddleName", "action": "keepNullable" }
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
projection explain node ./projection.json "Sales.Customer"     # confirm the rename + tightening fired
projection explain suggest ./projection.json                    # ranked edits this config is missing
```

The analysis verbs read the **shaping view** of whatever config you point them at — `projection.json` works directly (its `environments`/`flows` are ignored here), or a shaping-only file like the one above. They resolve the model through `model.ossys`/`model.path`, **not** `model.env` (that needs the estate registry, which these registry-less verbs do not load — so a unified `projection.json` exercises them via its `path` fallback or an explicit `ossys`).

(Real connection strings stay in `./secrets/*.conn`, gitignored — `model.ossys` only names the `file:` reference.)
