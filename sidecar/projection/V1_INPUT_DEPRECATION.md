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
| **Model**: `osm_model.json` | `CatalogReader.parse (SnapshotFile)` ← `ModelResolution` / `Compose.readConfigModel` | **DEMOTED to optional fallback (2026-06-08).** Live OSSYS is now the primary model source across the whole flow surface + the full-export path; `osm_model.json` is read only as the configured fallback (cutover safety, not retired). See §3. |
| **Static populations JSON** | `Static.attachStaticPopulations` (`Adapters.Sql/Static.fs`) | **REMOVED 2026-06-08.** Zero production callers; static populations arrive via the model read (`Modality.Static`) / `ReadSide`. See §4. |

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
- **Wired across the whole flow surface** (2026-06-08, comprehensive): every
  model-bearing flow action — `EmitBundle` / `EmitSkeleton` (folder),
  `DeployDocker` (docker), `PreviewSchema` / `Migrate` / `MigrateWithData`
  (live), and `SynthesizeAndLoad` (synthetic) — carries the `modelOssys` ref
  (filled by `planMovement` from config) and resolves its `Catalog` through the
  CLI's `needCatalog` → `ModelResolution.resolveCatalog`. The runner cores were
  factored to accept an already-resolved `Catalog` (`Compose.runFromCatalog` /
  `runSkeletonOnlyFromCatalog`, `Deploy.runFromCatalog`), so the model arrives
  resolved regardless of source. So **any flow** reads the model live from OSSYS
  when `modelOssys` is set — no `osm_model.json` in the loop — and falls back to
  the file otherwise. The model file is now **optional** when `modelOssys` is
  set (`hasModel` gates the refusal; an ossys-only config routes cleanly).
- **Tests:** `ModelResolutionTests` (the pure primary/fallback law),
  `MovementSurfaceTests` (the live ref threads into emit / docker / preview /
  migrate / synthetic actions; ossys-only configs route without a file),
  `ModelResolutionDockerTests` (the live read resolves a non-empty Catalog with
  native `OssysOriginal` identity against a bootstrapped OSSYS DB).

**Full-export path also wired (2026-06-08).** The non-flow `project --config`
full-export (`PublishBundle` / `PublishAndLoad`) honors `model.ossys` too:
`Config.ModelSection` gained `Ossys : string option`, and `Compose.readConfigModel`
applies the policy at the three full-export read sites (`runWithConfig`,
`emittedSchema`, `emittedSeed`) via the shared `LiveModelRead` primitive. So every
model-read surface — flow + full-export — is live-OSSYS-primary with the file
fallback.

**Remaining (minor):** the full-export rich config still requires `model.path`
(used as the fallback) even when `model.ossys` is set; making it optional is a
small follow-on. The flow surface already treats the file as optional.

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
- It was already exercised by the canary's bootstrap+extract flow before the
  wiring landed.

**The operator wiring landed (2026-06-08), all of it:**

1. `Projection.Pipeline` references `Projection.Adapters.OssysSql`. ✓
2. The model source is config-driven (`modelOssys` on the flow config;
   `model.ossys` on the rich full-export config) — primary when set; the file is
   the fallback. ✓
3. The shared `LiveModelRead` primitive (compiled first in the Pipeline project)
   opens the connection → `MetadataSnapshotRunner.runAsync` → `toBundle` →
   `CatalogReader.parse (SnapshotRowsets …)`; both `ModelResolution` (flows) and
   `Compose.readConfigModel` (full-export) delegate to it. ✓
4. CLI: `needCatalog` resolves every model-bearing flow action through
   `ModelResolution`; the full-export reads through `Compose.readConfigModel`. ✓

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

## 4. Static populations — removed (2026-06-08)

`Static.attachStaticPopulations` (the V1 static-data JSON importer) had zero
production callers and is **removed**, along with its dedicated
`StaticAdapterDifferentialTests`. Static entity data reaches V2 through the model
read (the rowset / JSON model carries `Modality.Static`) and through `ReadSide`
(`readRowsStream` lifts live rows); the standalone V1-JSON importer was a
Bridge-era ACL with no live consumer. `EndToEndDifferentialTests` builds its
Country populations directly in the V2 IR.

---

## 5. End state — reached (2026-06-08)

A flow (or full-export) now runs with **no V1 artifact in the loop**: the model
comes from a live OSSYS connection (native SsKey) when `modelOssys` / `model.ossys`
is configured, the data/profile from `ReadSide` + `LiveProfiler`, and every
durable artifact is a V2-native codec (`CatalogCodec` / `ProfileCodec`). The V1
profile/distribution JSON and the V1 static-data JSON importers are deleted; the
`osm_model.json` reader remains only as the configured, cutover-safe **fallback**
(per the operator's "switch to primary, keep the file as optional fallback"
decision — not retired). V2 stands alone.
