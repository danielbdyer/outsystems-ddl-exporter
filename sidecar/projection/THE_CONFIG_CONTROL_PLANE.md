# THE_CONFIG_CONTROL_PLANE.md — one isomorphic configuration surface

> **Status: design, signed off 2026-06-09.** The build sheet for unifying the two config
> surfaces into a single `projection.json` that is the operator's control plane for the whole
> pipeline — and is an **isomorphic image of the movement space** (every ontology direction is
> a point in it). Grounded in two read-only analyses (the unified-schema/wiring design + the
> directionality/isomorphism design); every file:line below is from the live source.

## 1. The idea

There must be **one configuration system** — a single `projection.json` that controls the
whole pipeline — *broken out into namespaces only where intent genuinely collides*. Today
there are two disjoint surfaces:

- **Movement** (`ProjectionConfig`, `MovementSurface.fs:76-90`): `environments`, `flows`,
  `model` (string path), `modelOssys`, `defaults`. Reached by `projection <flow>`.
- **Model-shaping** (`Config`, `Config.fs:283-293`): `model` (object: env/ossys/path/modules/…),
  `overrides`, `emission`, `policy`, `typeMapping`, `profiler`, `cache`, `output`. Reached
  today only by the `explain` verbs (`Config.fromFile`) and `FullExportRun`. (`model.env` — the
  schema source as an `environments` *reference* — is resolved by the movement surface, the only
  view carrying the registry, into `model.ossys`; see `CROSS_ENVIRONMENT_READINESS.md` §4.)

**The defect:** `resolveFlowSpec` (`MovementSurface.fs:605`) only ever builds
`ModelSource.ModelFile`/`Unspecified` — never `ConfigFile` — so the
`ConfigFile → PublishBundle → runFullExport → Compose.runWithConfig` overlay path is
**dead-wired for flows**. Every `projection <flow>` emission runs the chain with `Policy.empty`
(`Program.fs:345`, `Pipeline.fs:822`), silently dropping every `policy`/`overrides`/`modules`/
`emission` toggle the operator wrote. The model-shaping surface is unreachable from the daily
surface.

## 2. The law — `expressible ⇔ reachable` (A44 candidate)

The config and the engine's resolved `MovementSpec` (`MovementSpec.fs:83-111`) must form a
**total, faithful, direction-derived** correspondence `Φ = resolveFlowSpec`:

1. **Faithful (Φ is a function).** Every config resolves to exactly one `MovementSpec`; the
   same direction-neutral `emit(B ⊖ A)` underlies every action (`WAVE_6_ALGEBRA.md`;
   `MovementSpec.fs:5-10`). Holds by construction.
2. **Total / spanning (Φ is onto the engine-consumable specs).** Every spec the engine can
   consume has a config pre-image. **This is the half that fails today** — see the gaps.
3. **Direction is derived, not stored.** `direction : (sourceRendition, sinkRendition, scope)
   → {A→B, B→A, A→A, mint→A, eject}` is a pure function — the way `reverseLegOf`
   (`MovementSurface.fs:554-570`) already derives B→A from renditions. Never a stored knob
   ("direction is a binding", `THE_DATA_PRODUCERS.md:286-289`).

This is the movement-space instance of T16 / the iso-ladder (`WAVE_6_ALGEBRA.md:168-193`):
where T16's witness is `Ingestion ∘ Projection = id` on states (P-9 / H-050), A44's witness is
`render ∘ resolve = id` on the config⟷spec pair — the adjunction lifted to the operator's
control surface. Enforced by a **`reachable ⇔ expressible` canary** (mirroring
`required ⇔ surveyed`, `CapabilitySurveyTotalityTests.fs:46-60`) that fails today on the
unreachable specs and flips green as each gap closes.

### Directional coverage (the variant enumeration)

13 of 16 ontology variants are reachable from a flow today; 3 gaps are the unification work.
Direction is read off `(src rendition → sink rendition, scope)`; **A** = physical OSUSR cloud,
**B** = logical on-prem.

| Use case | Direction | Scope | Origin | Reach |
|---|---|---|---|---|
| publish to bundle (down-leg) · deploy to docker · migrate · schema preview | A→B | schema/both | model | ✅ |
| synthetic cloud insertion (up) | mint→A | data | synthetic | ✅ |
| peer/golden re-key (up) | A→A | data | `FromTarget` | ✅ (but A→A not derived — G2) |
| legacy reverse leg (up) | **B→A** | data | `FromTarget` (logical src) | ✅ routed (G2) + runnable (J3 closed 2026-06-10: `CatalogRendition` contracts) |
| dev→qa→uat promotion · migrate-with-data · transfer · preview-of-any · eject | various | various | various | ✅ |
| **schema-only down-leg** | A→B | **schema** | model | ❌ G1 |
| **data-only into a schema+data target** | →* | **data** | peer | ❌ G1 |

## 3. The gaps and their closes

- **G1 — scope is grant-derived only.** `resolveFlowSpec:606` infers `Scope` from the sink's
  `grant`, conflating the *refusal gate* (what may change here) with the *move projection*
  (what this move carries — T16's schema leg vs data leg). Schema-only and data-only-into-a-
  `schema+data`-target are unreachable. **Close (DECIDED — decouple):** an optional per-flow
  `"scope": "schema" | "data" | "both"`; `grant` stays the refusal gate; `Scope =
  flow.Scope |> Option.defaultWith (grant-derived default)`. `Scope` already has all three
  variants (`MovementSpec.fs:26-31`) — no codomain change.
- **G2 — direction classified but not routed.** `reverseLegOf:554-570` derives B→A purely, but
  `MovementSpec` carries no direction, so `planMovement:379-412` routes peer (A→A) and legacy
  (B→A) `FromTarget` identically and can't select the reverse-leg runner. **Close:** a *derived*
  `MovementSpec.Direction` (`Down | UpSynthetic | UpPeer | UpLegacy`) computed in
  `resolveFlowSpec` from `(srcRendition, sinkRendition, data)`; `planMovement` routes `UpLegacy`
  to a `RunReverseLeg` action. Derived, never parsed. (The SsKey-aligned-contracts runner
  residual stays J3 — this gap is config-expressibility + routing only.)
- **G3 — `ModelSource.ConfigFile` unreachable from a flow.** The dead wire. **Close:** the S3
  payoff — make flow emissions apply the shaping (below).
  **RESOLVED (2026-06-10, S6.2).** `resolveFlowSpec` emits `ModelSource.ConfigFile` exactly
  when the flow targets a **provenance-bearing place** — the config has a load provenance
  (`ProjectionConfig.SourcePath = Some`, set by `fromFile`), the sink carries a `store`, and a
  `model` path is present. That fires the `PublishBundle` (folder) / `PublishAndLoad` (live
  `--go`) arms; the runner resolves the path through the unified loader (`runFullExport` /
  `runFullExportLoad` → `Compose.runWithConfigAndLoad`), baking the overlays + provenance in.
  Every store-less target and from-string config keeps the byte-identical
  `ModelFile`/`Unspecified` path. Trigger chosen: **store presence** (provenance configured).
- **G4 — no renderer Ψ (MovementSpec → config).** No `render` dual of `parseFlow`. **Close:**
  `ProjectionConfig.renderFlow` (declarative dual) so the law is a round-trip property
  `parse ∘ render = id`.
- **G5 — the per-pair cutover pipeline** (dev→qa→uat as an ordered, per-pair-gated chain) is
  orchestration *above* the movement isomorphism. Out of scope; stays a separate surface.

## 4. The unified schema

One `projection.json`. Movement (`environments`, `flows`, `defaults`) stays top-level; the
shaping `Config` sections fold in as sibling top-level namespaces. The **only** genuine
collisions are the two `model` ones — folded into one `model` namespace.

```jsonc
{
  "environments": {
    "cloud-dev": { "access": "direct", "conn": "file:./secrets/cloud-dev.conn", "rendition": "physical", "archetype": "managed-dml" },
    "cloud-qa":  { "access": "direct", "conn": "file:./secrets/cloud-qa.conn",  "rendition": "physical", "archetype": "managed-dml" },
    "cloud-uat": { "access": "direct", "conn": "file:./secrets/cloud-uat.conn", "grant": "data", "rendition": "physical", "archetype": "managed-dml" }
  },
  "model":     { "env": "cloud-dev", "modules": ["Sales", { "name": "Ops", "entities": ["Order"] }] },
  "overrides": { "tableRenames": [ { "from": { "module": "Sales", "entity": "Cust" }, "to": { "schema": "dbo", "table": "Customer" } } ] },
  "policy":    { "tightening": { "interventions": [ { "kind": "foreignKey", "id": "fk1", "enableCreation": true } ] } },
  "emission":  { "ssdt": true, "dacpac": true },
  "readiness": { "confirm": ["cloud-dev", "cloud-qa", "cloud-uat"] },
  "flows": {
    "golden": { "from": "cloud-qa", "to": "cloud-uat", "scope": "data", "tables": ["Customer"], "rekey": "file:./secrets/users.csv" },
    "audit":  { "from": "cloud-dev", "to": "docker", "scope": "schema", "shaping": { "model": { "modules": ["Ops"] } } }
  }
}
```

**Shaping is global** (applies to every flow's emission — the singular control plane); a flow
MAY carry an opt-in `shaping: {…}` that deep-overrides the global blocks for that flow only
(DECIDED). `scope` is the decoupled per-flow move-projection (DECIDED). The global `model.env`
names the canonical schema source **once** by reference into `environments` (here `cloud-dev`);
`readiness.schema` defaults to it — so the unified example above restates no connection
(DECIDED 2026-06-22).

### Collision reconciliation (the only two)

| Collision | Movement | Shaping | Unified |
|---|---|---|---|
| `model` (string vs object) | `model: "<path>"` | `model: { path, ossys, modules, … }` | the **object**; legacy `model:"<string>"` maps to `model.path` (same intent) |
| `modelOssys` (top vs nested) | top-level `modelOssys` | `model.ossys` | canonical `model.ossys`; legacy top-level maps in |

Everything else (`overrides`/`emission`/`policy`/`profiler`/`cache`/`typeMapping`/`output`,
and `defaults`) is a clean union under distinct section names — no namespacing needed.

## 5. Loader strategy — composition, not rewrite

`Config.fs` (compile-order line 13) precedes `MovementSurface.fs` (line 56), so
`ProjectionConfig.parse` can delegate. Add `Shaping : Config.Config` to `ProjectionConfig`;
`parse` runs a **lenient** `Config.parse` over the shaping namespaces (defaulted-empty sections
when absent, so a movement-only file does not fail `modelNoSource`). The strict
`Config.parse`/`fromFile` stays for the `explain` consumers. The D9 credential pre-scan
(`Config.fs:1286-1288`) runs once over the whole document; `looksLikeSecret` stays on `direct`
conns. Legacy `model`-string/`modelOssys` mapping lives in the loader.

## 6. Flow→shaping wiring (the payoff)

Route flow emissions through the overlay machinery (`Compose.runWithConfig`, which applies
`applyModuleFilter` `Pipeline.fs:1025`, `applyRenames` `:833`, `buildPolicyFromConfig` `:865`,
`EmissionFolders`/`TransformGroups` bindings). Either make `resolveFlowSpec:605` emit
`ConfigFile` when shaping is present (firing the existing `PublishBundle`/`PublishAndLoad`
arms `:370/:410`) **and/or** add overlay-aware catalog runners (`Compose.runFromCatalogWith
cfg.Shaping`) for the Docker/preview/migrate destinations that have no `ConfigFile` arm.

> **Riskiest seam (test it explicitly):** `applyModuleFilter` lives only on the
> `runWithConfig` model-read path (`Pipeline.fs:1029-1051`); the live/docker catalogs come from
> `ModelResolution.resolveCatalog` (`Program.fs:1828`) **unfiltered**. Apply the filter at one
> shared seam for both bundle and live, or a module-scoped config scopes the bundle but not a
> live migrate. The empty-modules default is byte-identical (`ModuleFilterBinding.fs:62-63`) —
> which *masks* the divergence and keeps the canary green — so S3 MUST test with a **non-empty
> `model.modules`** on both a `bundle` flow and a `direct --go` flow.

## 7. Locked decisions (operator, 2026-06-09)

1. **Per-flow `scope` decoupled from `grant`** (G1). `grant` = refusal gate; `scope` = the
   move's projection. Lets schema-only / data-only legs be expressed isomorphically.
2. **Global shaping + opt-in per-flow `shaping` override.** One control plane; a flow may narrow it.
3. **Wire the shaping into flow emissions** (G3) — `projection publish`/deploy/migrate honor
   `policy`/`overrides`/`modules`. (Implied by the singular-control-plane requirement.)
4. **Direction derived + `RunReverseLeg` routing** (G2) — B→A legacy auto-selects the reverse leg.
5. **The config is an isomorphic image of the movement space** — A44; enforced by the
   `reachable ⇔ expressible` canary.

### Residual closure (2026-06-10, S6)

The A44 canary's two remaining `[<Fact(Skip=…)>]` residual stubs are **RESOLVED** — the
named residual set is now **∅** (`residualActions = Set.empty`), the strongest A44 statement
(`expressible = reachable`, every model-bearing arm flow-reachable):

- **`EmitSkeleton` — RESOLVED (S6.1).** Operator decision: **add a flow `shape` field**.
  `flows.<name>.shape` (`bundle` | `ssdt` | `skeleton`; absent = `Bundle`) resolves to
  `MovementSpec.Shape`, so a folder flow with `"shape": "skeleton"` lands on `EmitSkeleton`.
- **`PublishBundle`/`PublishAndLoad` — RESOLVED (S6.2).** Operator decision: **wire ConfigFile
  into flows**, trigger = **store presence** (provenance configured); see G3 above.
- **The LogicalTableEmission physical-`tableRenames` clobber — RESOLVED (S6.3).** A
  physical-form override now survives into the emitted physical table (the `LogicalTableEmission`
  pass skips operator-pinned kinds); it was previously a no-op (the logical name clobbered it).
- **Per-flow `shaping` override (S6.4).** Whole-section-granularity deep-overlay
  (`Config.overlay`); see DECISIONS 2026-06-10.

## 8. Slice plan (each independently shippable + green)

| Slice | Scope | Guards |
|---|---|---|
| **S1** | Unify type/loader behind two views: `ProjectionConfig.Shaping : Config.Config` via a lenient `Config.parse`; keep `Model`/`ModelOssys`. No emission change. | `ConfigTests` (strict) + `MovementSurfaceTests` green; + lenient-parse-of-movement-only-file test |
| **S2** | Reconcile `model`/`modelOssys` into the `model` namespace; `resolveFlowSpec` reads `Shaping.Model`. | `MovementSurfaceTests:482-524` (modelOssys threads) green; + object-`model` parity |
| **S3** | **The payoff** — thread shaping into flow emit (the `ConfigFile`/overlay-aware runners); the shared module-filter seam. | non-empty-modules test on bundle + direct flows; `FullExport*`/`EndToEnd*` + **canary** green |
| **S4** | Per-flow `scope` (G1) + derived `Direction`/`RunReverseLeg` (G2) + per-flow `shaping` override. | new scope/direction tests; `planMovement` totality stays green |
| **S5** | `renderFlow` inverse (G4) + the **A44 `reachable ⇔ expressible` canary** + round-trip property. | the canary is the law's forcing function |
| **S6** | Migrate `explain`/`full-export` to the unified loader; `init` scaffolds the unified shape; docs (`CONFIG_REFERENCE.md`, `THE_CLI.md` §4) + `DECISIONS.md`. | doc + consumer tests |

Regression guards at every slice: `MovementSurfaceTests` (incl. the `planMovement`/`ModelSource`
totality sweeps), `ConfigTests`, `RegisteredAllTransformsBidirectionalTests` (`registered ⇔
executed`), and the operator-reality **canary** (the wide guard for S3).

## 9. The authorization predicates on a flow (`signoff`)

`supportingScope` and `signoff` are BOTH per-flow config arrays, but they are different KINDS
of claim, and the distinction is load-bearing:

- **`supportingScope` is STRUCTURAL** — a claim about the relationship graph (this table is an
  owned-child of that parent; this reference matches the target's own rows). The go board
  VERIFIES it against the graph and reds when the declaration contradicts the topology.
- **`signoff` is AUTHORIZATION** — a claim about intent (the operator understands this
  destructive write and approves it). There is no graph fact to check; the go board reds and the
  engine refuses BY NAME until the mode a run actually performs is greenlit. It is the durable,
  per-flow, auditable member of the `PROJECTION_ALLOW_EXECUTE` / `--go` / `--allow-drops` /
  `--allow-cdc` authorization family — where those are transient per-run flags, `signoff` is a
  standing declaration in `projection.json`.

The `signoff` array enumerates the destructive modes a flow may perform
(`replace/fresh/drops/cdc/identity-insert` transfer-side; `delete-scope` emission-lane — its
gate a named follow-on). Default-on: a destructive Execute is refused until the mode is
greenlit. A declared `tables` scope is VERIFIED to cover the actual wipe (a stale, too-narrow
approval cannot rubber-stamp a wider blast radius). A44 holds — `signoff` renders omit-when-empty,
so `parse ∘ render = id`. See `DECISIONS 2026-07-08 — The write-signoff greenlight`.
