# V1_INPUT_DEPRECATION.md — eliminating V1-produced inputs

**Intent (operator, 2026-06-08):** *"I don't want any inputs from V1."* V2 already
has **zero V1 *code* dependency** (no `ProjectReference`, no V1 assembly on the
classpath — per CLAUDE.md "V2 is self-contained"). This document tracks the
remaining V1 *data* inputs — the file/JSON formats V2 consumes that V1's chain
produced — and the path to retiring each.

The good news, established by code audit (not assumption): the two surfaces
break very differently. The profiling inputs were **dead** and are now removed.
The model input has a **live, V1-free replacement already built** — it just
isn't wired to the operator surface yet.

---

## 1. Inventory of V1 inputs (evidence-based)

| V1 input | Where consumed | Status |
|---|---|---|
| **Profile JSON** (`ProfileSnapshot`) + **distribution JSON** (`ProfileStatistics`) | `Adapters.Sql/ProfileSnapshot.fs`, `ProfileStatistics.fs` | **REMOVED 2026-06-08.** Zero production callers; superseded by `ReadSide` + `LiveProfiler` (V2 reads + profiles the live DB directly). |
| **Model**: `osm_model.json` | `CatalogReader.parse (SnapshotFile/SnapshotJson)` ← `Compose.read` (the production model path for every flow) | **Replaceable now (wiring slice).** A live, V1-free reader already exists — see §3. |
| **Static populations JSON** | `Static.attachStaticPopulations` (`Adapters.Sql/Static.fs`) | Smaller residual; live replacement is `ReadSide.readRowsStream` (reads the actual table rows). See §4. |

V2-native formats (not V1 inputs): `CatalogCodec`, `ProfileCodec`, the lifecycle
store / episodes / change manifests — all V2-produced, round-trip-bound.

---

## 2. Done — the profiling importers are gone (2026-06-08)

`ProfileSnapshot` (V1 profile-JSON → `Profile`) and `ProfileStatistics`
(distribution-JSON → `Profile`) were the Bridge-era profiling inputs. They had
**no production call sites** (only doc comments + tests) and were never in the
transform registry. `LiveProfiler.attach` (reads + profiles a live SQL Server,
composing every `Profile` axis from one `EvidenceCache`) is the canonical V2
profiling path — the same path the synthetic capture verb uses.

Removed: both modules, their two dedicated test files, and the adapter-centric
`RichProfilingEndToEndTests`. `EndToEndDifferentialTests` was rewritten to build
its profile directly in the V2 IR (it had only used the importer as a
convenience). Pure pool stayed green (2870 passed).

---

## 3. The model — live OSSYS is now **primary**, `osm_model.json` the **fallback**

**Built (2026-06-08): live-OSSYS-primary with file-fallback, wired for the
synthetic flow.** Per the operator decision ("switch it to primary, keep the
file as an optional configuration fallback for now, don't retire it"):

- **Config:** `projection.json` gains `"modelOssys": "<env-or-conn-ref>"` (a
  live OSSYS connection). When set it is the **primary** model source; `"model"`
  (the `osm_model.json` path) remains the **fallback**.
- **`ModelResolution`** (`src/Projection.Pipeline/ModelResolution.fs`) is the
  policy seam: `chooseOrigin` (pure — live wins when configured; else file;
  neither is a named refusal) + `resolveFromConnection` (the V1-free live read:
  `MetadataSnapshotRunner.runAsync` → `toBundle` → `CatalogReader.parse
  (SnapshotRowsets …)` → `Catalog` with native GUID SsKey) + `resolveCatalog`
  (applies the policy, opening the connection or reading the file).
- **Wired** into the synthetic flow: `SynthesizeAndLoad` carries the
  `modelOssys` ref (filled by `planMovement` from config); `SyntheticLoadRun`
  resolves through `ModelResolution`. So `from: synthetic` reads the model live
  from OSSYS when `modelOssys` is set — **no `osm_model.json` in the loop** —
  and falls back to the file otherwise.
- **Tests:** `ModelResolutionTests` (the pure primary/fallback law),
  `MovementSurfaceTests` (the live ref threads into the action as primary),
  `ModelResolutionDockerTests` (the live read resolves a non-empty Catalog with
  native `OssysOriginal` identity against a bootstrapped OSSYS DB).

**Remaining (the "for now" boundary):** the *other* flow actions
(emit / deploy / migrate / preview) still resolve the model from the file path
(`Compose.read`); only the synthetic flow honors `modelOssys` today. Extending
them is mechanical — route their model read through `ModelResolution.resolveCatalog`
the same way — and is the next slice. Until then, a flow whose action is not
synthetic still needs the `model` file.

---

### Background — why this was a wiring slice, not a chapter

**The capability already exists and is V1-free end to end:**

```
MetadataSnapshotRunner.runAsync (SqlConnection, SnapshotParameters)   -- Adapters.OssysSql
    → OssysRowsetTypes.RowsetBundle                                   -- V2's own carbon-copied rowset SQL
    → CatalogReader.parse (SnapshotRowsets bundle)                    -- Adapters.Osm
    → Catalog                                                         -- native SsKey (OssysOriginal GUID; A1-stable)
```

- `src/Projection.Adapters.OssysSql/MetadataSnapshotRunner.fs` is **V2's own F#
  runner** (not V1 C#): it executes V2's carbon-copied `Resources/outsystems_metadata_rowsets.sql`
  against an open `SqlConnection`, walks the result sets, and assembles a
  `RowsetBundle`. No `MetadataSnapshotRunner`/`SnapshotJsonBuilder` C# from V1; no
  `osm_model.json` file.
- `CatalogReader.SnapshotRowsets` already turns that bundle into a `Catalog`,
  carrying SsKey natively (better than the JSON path, which name-synthesizes
  SsKeys — so this also *improves* A1 identity stability under rename).
- It is exercised today by the canary's bootstrap+extract flow; it is **not**
  referenced by `Projection.Pipeline` / the CLI.

**What's missing is only the operator wiring** (mirrors the synthetic-flow slice
just shipped):

1. `Projection.Pipeline` references `Projection.Adapters.OssysSql` (today it does
   not).
2. A model-source variant — e.g. `ModelSource.LiveOssys of ConnectionRef` (or a
   `from`/config field) — so a flow can name a live OSSYS environment as the
   schema-B source instead of a `model.json` path.
3. `Compose.read` grows a sibling (or `readStep` a second `Read`) that, for the
   live variant, opens the connection → `MetadataSnapshotRunner.runAsync` →
   `CatalogReader.parse (SnapshotRowsets …)`. `SnapshotParameters` (module/entity
   filters) come from config; sensible defaults exist.
4. CLI surface: `projection <flow>` resolves a `LiveOssys` model source the same
   way it resolves a live sink; `projection profile <env>` already opens a live
   connection, so the connection-resolution plumbing is shared.

**Cutover safety (R6).** During transition, a differential test asserts the
live-OSSYS `Catalog` ≈ the `osm_model.json` `Catalog` for the same source
(modulo the SsKey-stability *improvement* — rowsets carry GUIDs, JSON
synthesizes them; the tolerance names that). Retire `SnapshotFile`/`SnapshotJson`
(and the `osm_model.json` artifact) once N consecutive green differential runs +
operator sign-off, per the R6 ladder.

**Effort:** a focused slice (reference + one model-source variant + one
`Compose.read` branch + CLI resolution + a differential test), not a chapter —
because the read path is built and tested. The blocker is operational, not
architectural: it needs a **live OutSystems/OSSYS-shaped database** to point at
(the rowset SQL + `MetadataExtractionSql.readEdgeCaseSeed()` can seed an
ephemeral one for the differential test).

---

## 4. Static populations — `Static.attachStaticPopulations` → `ReadSide.readRowsStream`

Static entity data currently enters via V1's static-data JSON
(`Static.attachStaticPopulations`). The V1-free replacement is already present:
`ReadSide.readRowsStream` streams a table's actual rows as `StaticRow`s. Wiring a
"read static populations from the live source" path (for the kinds the catalog
marks `Static`) removes this input too. Smaller and independent of §3; do it in
the same live-OSSYS slice or just after.

---

## 5. End state

When §3 and §4 land, a flow runs with **no V1 artifact in the loop**: the model
comes from a live OSSYS connection (native SsKey), the data/profile from
`ReadSide` + `LiveProfiler`, and every durable artifact is a V2-native codec
(`CatalogCodec` / `ProfileCodec`). `osm_model.json`, the V1 profile/distribution
JSON, and the V1 static-data JSON all retire. V2 stands alone.
