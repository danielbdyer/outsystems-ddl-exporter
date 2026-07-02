# PERF_OPPORTUNITIES — structural-perf punch list (produced by slice A.4.7'-prelude.bench-fleet, 2026-05-19)

This document captures the **34 structural-perf opportunities** spotted
by the five-agent bench-fleet dispatch on 2026-05-19. The bench-fleet
slice was strictly **additive instrumentation** (51 new Bench labels);
this document is the queue for the **next** slice — a structural-perf
sweep that ships fixes with before/after data on each affected bench
label.

## 🎯 READ-CONCURRENCY ARC (2026-07-02) — bounded hydrate/profile parallelism shipped; the five follow-ups adjudicated; next-wave discovery ranked

**Shipped:** `emission.dataReadConcurrency` / `profiler.maxConcurrency` (both
default 4) — `Ingestion.collectInOrderForConcurrent` +
`LiveProfiler.captureEvidenceCacheConcurrent`, per-kind pooled connections
behind a `SemaphoreSlim`; serial paths untouched as the concurrency-1 arm
(DECISIONS 2026-07-01). **Measured** (`ReadConcurrencyMeasurementTests`,
100 tables × 375 rows): hydrate ~46% faster and profile ~77% faster at
concurrency 4 on the cold shape; the warm sweep flattens past 4 and the
profile leg inverts at 8 — the evidence for the low explicit defaults.
Row maps / evidence caches value-identical across all legs.

### The five follow-up recommendations, adjudicated (validation fleet, 4 agents)

| Recommendation | Verdict | Substance |
|---|---|---|
| **1. Batch nullability reflection into one schema query** | ✅ FEASIBLE, HIGH VALUE — the next profile-stage cut | Per-kind `INFORMATION_SCHEMA.COLUMNS` (`LiveProfiler.fs` `reflectNullability`, query 2-of-3 in `discoverKind`) becomes one catalog-wide query (the `ReadSide.readColumnRows` precedent — measured ~2× faster than the `sys.*` join). ~300-kind estate: 900 → 601 queries (~33% of profile round-trips). One must-fix hazard: the (schema, table) re-match moves client-side — key case-insensitively or a collation mismatch silently defaults a column to NOT NULL. |
| **2. Pre-warm / rent a bounded connection pool** | ⚠️ SUBSTANTIALLY ALREADY SATISFIED — do only the trivial form | The opener closes over one stable conn string per lane → one SqlClient pool bucket; physical connections ARE reused after the first `concurrency` opens; every `ClearPool` site is test teardown. Residuals: cold-pool handshake burst per lane, per-open `file:` secret re-resolution (a file read per kind — resolve once), `Min Pool Size=0` decay between lanes. Cheapest fix: `Min Pool Size=<concurrency>` + a resolve-once opener. A rented `SqlConnection[]` is NOT justified. |
| **3. Batch-aware row materialization** | ❌ AS STATED, LOW VALUE — the peak is set by the consumer, not the drain | `DataLoadPlan.Loads[].Rows : StaticRow list` and the rendered artifact hold the whole estate regardless; chunking the drain alone changes nothing. Determinism is NOT the blocker (static lane sorts in `NormalizeStaticPopulations`; bootstrap order is pinned by the reader's `ORDER BY <pk>`). The genuine lever is streaming emitters — which crosses the pure-Core boundary; estate-scale data already has the correct mechanism on the transfer leg (`writePlanStreaming`). Act only if peak memory becomes the binding constraint, and then via the streaming realization. |
| **4. Memoize/thread the topological order** | ❌ LOW PRIORITY — micro-win, real staleness hazard | ~5 recomputes/publish but each is sub-ms-to-low-ms at 300 kinds (Kahn/Tarjan already Dictionary-based). The sites legitimately differ: pre-transform vs post-chain vs emission-seam catalogs, and SSDT uses `SkipSelfEdges` while data uses `TreatAsCycle`. Only the chain-step → data-emit reuse is coherent, and it must first prove `UserFkReflowPass` never re-points FK edges after the topo step. Not worth the hazard today. |
| **5. Bench granularity (gate wait / conn open / SQL read / materialization)** | ✅ VALID — the two phases the knob most affects are exactly the unmeasured ones | Gate wait (`gate.WaitAsync`) and connection open both precede the drain stopwatch — MISSING. SQL execute is covered (`readside.readRowsStream.open`) but the per-row `ReadAsync` wire fetch is only the residual of the stream-lifetime label — CONFLATED. Materialization is COVERED (`readside.rowstream.materialize` / `.materializeIr`). Minimal closure: `ingestion.rowDrain.gateWait` / `.connectionOpen`, `readside.rowstream.fetch`, and profiler mirrors + `profile.live.aggregate`. No new double-counting. |

### Execution status (2026-07-02, same session)

**Executed:** rec 1 (batched nullability reflection — ONE catalog-wide
`INFORMATION_SCHEMA.COLUMNS` query in both capture paths, per-kind query
retired, case-insensitive table keys; 3 → 2 queries/kind); rec 5 (the
four-phase Bench labels); rec 2's real residual (`ConnectionSpec.openerFor`
— resolve-once opener; per-open `file:` secret re-reads eliminated;
`Min Pool Size` remains an operator connection-string knob, not forced);
discovery #1 (typed per-column cell formatters in
`ReadSide.readRowsStreamCore`, byte-identical formatting, generic
fallback); discovery #4's options half (`Sql160ScriptGenerator` options
memoized; the generator itself stays per-call — it holds render state).

**Deferred with evidence:** discovery #2 — the per-row `Map` is NOT
throwaway: it is retained as `DataInsertRow.Values` (the artifact
carrier), so fusing saves one ordered re-walk per row while requiring a
render-contract reshape across three emitters; do it with the #3
artifact-streaming decision, not alone. Discovery #5 (composer lane
skip) — a few hundred `Map.tryFind`s; noise next to the fused-string
cost it sits beside. Recs 3 and 4 — adjudicated against, above.

### Next-wave discovery (ranked; the sharpest first)

1. **`ReadSide.formatRawValue` per-cell box + `Convert` reparse** (`ReadSide.fs` pull loop) — `GetValue` boxes every scalar then `Convert.To*` re-dispatches, ×375k rows × N columns. Typed accessors (`GetInt64/GetDecimal/…` guarded by the known column type) remove both. Measure via `readside.rowstream.materialize` before/after. **HIGH.**
2. **`KindColumns.rowToTypedValues` throwaway `Map` per row** — builds a `Map<Name, SqlLiteral>` that `typedValuesToSqlLiterals` immediately re-reads in the same attribute order; two tree builds + N lookups + per-cell DU allocs per row. Fuse into one ordered array walk. **HIGH.**
3. **Whole-seed fused in-memory string + retained typed scripts** (`DataEmissionComposer.renderArtifactInTopoOrder` → `File.WriteAllText`) — full typed rows AND full rendered T-SQL held simultaneously; stream per-kind chunks to a `StreamWriter` instead. **HIGH (memory) / MEDIUM (wall).**
4. **Per-call `Sql160ScriptGenerator` + `pinnedOptions()`** — allocated per statement across the SSDT lane and PER ROW on the below-threshold Phase-2 UPDATE path; memoize the options object (generator stays per-call). **MEDIUM-HIGH.**
5. **`composeRenderedBundleWithBootstrap` renders 4 lanes ×2 topo walks** even when 2 lanes are empty under `AllData`; skip empty artifacts, reuse one `toMap`. **MEDIUM.**
6. **Synchronous whole-body artifact writes + `WriteIndented` full-catalog JSON** (`Pipeline.writeAllToStaging`) — bounded-parallel writes + streaming `Utf8JsonWriter`. **MEDIUM.**
7. **~20 full-catalog immutable rewrites in the pass chain** — bounded at ~3k attributes; sum the pass-tier Bench samples before investing. **MEDIUM-LOW.**
8. **`toBundle` transient list copies** — confirmed NO O(n²) (all joins Map-indexed); only transient-allocation trimming remains. **LOW.**
9. **Emit-stage "regression" note:** the slight emit slowdown observed alongside the halved extract/profile is most consistent with GC/ThreadPool spillover from the now-parallel stages (survival rule 13 — a verdict under concurrent load is void); re-measure `stage.emit` solo with a forced GC between stages before optimizing.

## 🎯 PERF-SWEEP ARC RESULTS (2026-05-19)

**Status: top-leverage findings SHIPPED; canary wall-time 3:34 → 2:22 (~34% reduction).**

| Finding | Slice | Status |
|---|---|---|
| **Tarjan/Kahn `Map`/`Set` → `Dictionary`/`HashSet`** (Ranks 3+4 / E1+E2) | `perf-sweep-1` (`80f6185`) | ✅ Shipped (structural Big-O; sub-ms at canary scale) |
| **`Catalog.tryFindKind` → `KindIndex` cache** (Rank 1 / D1) | `perf-sweep-2` (`df03328`) | ✅ Shipped (ConditionalWeakTable-keyed) |
| **`Catalog.tryFindOwningModule` → `KindOwnership` cache** (Rank 7 / D2) | `perf-sweep-2` (`df03328`) | ✅ Shipped (piggybacks on KindIndex) |
| **`Kind.tryFindAttribute` → `AttributeIndex` cache** (Rank 2 / D3) | `perf-sweep-2` (`df03328`) | ✅ Shipped |
| **`TSql160Parser` per-call → `ThreadLocal` cache** (Rank 7 / C1-C3) | `perf-sweep-3` (`57ec251`) | ✅ Shipped (`render.statement` -19% across 504K calls) |
| **Per-segment-size diagnostic** | `perf-sweep-4` (`60ef70f`) | ✅ Shipped (revealed 100×405KB MERGE cluster) |
| **`Deploy.executeBatchParallel` primitive** | `perf-sweep-5` (`d989bd0`, `e616640`) | ✅ Shipped + 3 tests in `ExecuteBatchParallelTests.fs` |
| **`TopologicalOrder.levels` + `composeRenderedLeveled` + parallel data deploy** (Rank N/A — emergent from preflight) | **`perf-sweep-6` (`9fa1d4c`)** | ✅ Shipped — **canary 3:34 → 2:22 (-72s, -34%); the wall-time-moving slice** |
| **`Deploy.resolveParallelism` (DMV → ProcessorCount → static)** | `perf-sweep-7` (`21c2c8b`) | ✅ Shipped (env-adaptive; `PROJECTION_DEPLOY_PARALLELISM` override) |
| **Defensive-hardening (9 audit findings)** | `defensive-hardening` (`f8a7f01`) | ✅ Shipped (DBNull guards, bounded timeouts, cast hardening, pool caps, empty-result diagnostics, UserProfile guard) |

**What remains open** (lower-leverage; not blocking any current path):

- **Schema-side level grouping** in `SsdtDdlEmitter` (CREATE TABLE per topological level) — would unlock parallel schema deploy too. Estimated ~50 LOC mirror of the composer-levels pattern. **Trigger:** schema deploy becomes a visible bottleneck (it isn't today — schema is ~14s of the 132s canary deploy time).
- **A4 — `parseRowsetBundle` 8 sequential `Map.ofList`** (Agent A's finding) — would benefit from `Array.Parallel.map`. Low-leverage at current scale.
- **C8 — `schemaObjectFromTableId` per-call allocation** (Agent C) — STRUCTURAL FLOOR (typed-AST library requires per-fragment instances). Documented; no fix.
- **D5 — `Catalog.create` triple-walk over allKindList** (Agent D) — minor; trivial fold collapse.
- **E3 — TopologicalOrderPass `internalEdgesOf` O(|scc|²)** — conditional on real-world SCC sizes; not material on the current canary.

**Per the bench-driven optimization protocol** (`DECISIONS 2026-05-24`): shipped fixes pair with before/after canary bench data captured at production scale (300 tables × 100MB). The slice arc's commit messages embed the per-slice deltas.

## ✅ BASELINE GATE STATUS — RESOLVED

**Per slice A.4.7'-prelude.canary-100pct (commit `a0c21e0`, 2026-05-19):**
The comprehensive operator-reality canary now fires **65/65 = 100%**
of declared new bench labels (fleet 51 + followup 11 + round-2 7) in
a single 17-second deterministic run. The "comprehensive canary
requirement" pre-condition stated at the bottom of this document is
**satisfied**.

**What the perf-sweep can rely on:**
- `ComprehensiveCanaryTests.fs` exercises every code path the 65
  bench labels gate. Same seed → same execution → reproducible
  before/after measurements per label.
- Per-run bench JSON persisted at `bench/canary/<utc>.json` under
  tag `comprehensiveOperatorReality`.
- The perf-sweep slice's first task: optionally record
  `bench/baseline-comprehensive.json` via `PERF_GATE_RECORD=1` with
  the comprehensive filter, OR work directly from the per-run
  snapshots (deterministic seed makes single-run comparison
  reliable enough for slice-level perf decisions; N=5 baseline
  is operator-CI territory).

The remaining 34 perf opportunities below are now the perf-sweep
slice's scope.

---

**Why this document exists.** The per-agent reports were produced as
ephemeral task outputs (`tasks/*.output`). The container is reclaimed
after session inactivity; that content is not durable. DECISIONS.md +
matrix amendment captured the top 5-8 by leverage but elided the
remaining 26+ findings. This document is the durable home for **all
34** so the perf-sweep slice can pick from the full menu.

**Scope discipline.** Every opportunity here was **read-only spotted**
during bench instrumentation — no agent attempted any of these fixes.
Shipping any of them requires:

1. A canary that exercises the affected bench label (so before/after
   data exists)
2. A statistical baseline (`PERF_GATE_RECORD=1`) capturing the
   pre-change μ + σ
3. A bench-driven optimization with at least one alternative
   considered + refuted (per `DECISIONS 2026-05-24 — Bench-driven
   optimization protocol`: three-candidate / 2-refuted / 1-confirmed
   shape on hot-path optimizations)
4. The post-change perf-gate passing (within μ + K×σ) — OR an
   explicit baseline refresh with a DECISIONS entry naming the new
   floor

Before any perf work ships, a **comprehensive canary** that surfaces
every one of the 51 new bench labels needs to land. The current
operator-reality canary fires 6 of 51 (the emit + topological-order
paths); the other 45 labels (adapter parse/extract; tightening passes;
IR construction; ScriptDomBuild per-statement variants;
UserFkReflowPass) need code-path coverage to baseline.

---

## Global leverage ranking (top 10)

Ranked by `(impact on canary wall-time × ease of validation × architectural cleanliness)`:

| Rank | Opportunity | File | Agent | Affected bench labels |
|---|---|---|---|---|
| 1 | `Catalog.tryFindKind` linear scan → `KindIndex : Map<SsKey, Kind>` at `Catalog.create` | `src/Projection.Core/Catalog.fs:1180-1181` | D | All downstream consumers (FK resolution; pass-layer kind lookups) |
| 2 | `Kind.tryFindAttribute` linear scan → per-Kind `AttributeIndex : Map<SsKey, Attribute>` | `src/Projection.Core/Catalog.fs:1109-1110` | D | `emit.scriptDom.build.*`, `pass.fk.reference`, `pass.userFkReflow.candidate` |
| 3 | TopologicalOrderPass Tarjan `Map`/`Set` → function-local `Dictionary` + `HashSet` | `src/Projection.Core/Passes/TopologicalOrderPass.fs:220-249` | E | `pass.topologicalOrder.kind` |
| 4 | TopologicalOrderPass Kahn `Map<SsKey,int>` indegree → function-local `Dictionary<SsKey,int>` | `src/Projection.Core/Passes/TopologicalOrderPass.fs:188-191` | E | `pass.topologicalOrder.kind` |
| 5 | `MetadataSnapshotRunner.toBundle`: 4 `Map.ofList` → `Dictionary<int, _>` | `src/Projection.Adapters.OssysSql/MetadataSnapshotRunner.fs:928-935, 980-983, 1027-1037` | B | `adapter.osm.extract.toBundle` |
| 6 | `resolveIndexColumnAttribute` O(N×M) → per-Kind pre-computed Maps | `src/Projection.Adapters.Osm/CatalogReader.fs:1838-1864` | A | `adapter.osm.parse.rowsetIndex`, `adapter.osm.parse.rowsetKind` |
| 7 | `TSql160Parser` per-call allocation at 3 sites → module-private with per-thread cache | `src/Projection.Targets.SSDT/ScriptDomBuild.fs:207, 298, 944` | C | `emit.scriptDom.build.columnDefinition`, `emit.scriptDom.build.createIndex` |
| 8 | `parseIndex` / `parseIndexRowFor` double-walk filter/map/sort/map → single `List.partition` | `src/Projection.Adapters.Osm/CatalogReader.fs:1159-1179, 1898-1908` | A | `adapter.osm.parse.index`, `adapter.osm.parse.rowsetIndex` |
| 9 | Three-pass `sortedReferences` / `sortedContexts` shape repetition (Null/Unique/FK) → emergent primitive in `Catalog` or `Composition` | `src/Projection.Core/Passes/NullabilityPass.fs:85-92` + `UniqueIndexPass.fs:91-98` + `ForeignKeyPass.fs:100-107` | E | `pass.fk.reference`, `pass.nullability.attribute`, `pass.uniqueIndex.index` |
| 10 | `Catalog.create`: hoist `attrKeys = Set.ofList ...` once per kind (currently rebuilt twice) | `src/Projection.Core/Catalog.fs:1236-1268` | D | `ir.catalog.create` |

The remaining 24 opportunities live in the per-agent sections below,
ranked within each agent's scope.

---

## Agent A — `Projection.Adapters.Osm/CatalogReader.fs`

**Scope:** OSSYS JSON + rowset parsing into V2 Catalog. 2341 LOC; 13
bench labels added under `adapter.osm.parse.*`.

### A1. `parseIndex` JSON path — double-walk filter/map/sort/map chains

- **Location:** `src/Projection.Adapters.Osm/CatalogReader.fs:1159-1179`
- **Current shape:**
  ```fsharp
  let keyCols =
      columnsList
      |> List.filter (fun c -> not (isIncluded c))
      |> List.map extractCol
      |> List.sortBy ord
      |> List.map (fun c -> ... attrKey ...)
  let includedCols =
      columnsList
      |> List.filter isIncluded
      |> List.map extractCol
      |> List.sortBy ord
      |> List.map (fun c -> ... attrKey ...)
  ```
  Each partition materializes 4 intermediate lists; total = 8 over
  the same input.
- **Proposed shape:**
  ```fsharp
  let extracted = columnsList |> List.map extractCol
  let keyCols, includedCols = extracted |> List.partition (fun c -> not (isIncluded c))
  let keyCols     = keyCols     |> List.sortBy ord |> List.map (... attrKey ...)
  let includedCols = includedCols |> List.sortBy ord |> List.map (... attrKey ...)
  ```
- **Expected impact:** ~50% fewer allocations on index columns;
  small absolute (indexes typically ≤8 cols) but multiplied by
  per-table count on 300-table canary.
- **Leverage:** Low-Medium. Trivially safe.

### A2. `parseIndexRowFor` rowset path — same double-walk

- **Location:** `src/Projection.Adapters.Osm/CatalogReader.fs:1898-1908`
- **Current shape:** identical pattern to A1, on the rowset path.
  Both passes share the inverted-predicate filter shape.
- **Proposed shape:** identical to A1 (`List.partition` + sort each).
- **Expected impact:** ~50% fewer allocations on rowset-path
  index assembly.
- **Leverage:** Low-Medium. Pair with A1.

### A3. `resolveIndexColumnAttribute` — O(N×M) lookup per kind (RANK 6 GLOBAL)

- **Location:** `src/Projection.Adapters.Osm/CatalogReader.fs:1838-1864`
- **Current shape:** each `IndexColumnRow` calls `List.tryFind`
  over `entityAttrs` (N attributes) up to 3 times (`humanAttr` /
  `AttrName`, `humanAttr` / `PhysicalCol`, `physColumn` /
  `PhysicalCol`). For M index columns per kind, total O(N×M)
  `String.Equals` calls.
- **Proposed shape:** in `parseKindRow`, build two `Map<string,
  AttributeRow>` per kind once (O(N)) — keyed by `AttrName`
  (case-folded) and `PhysicalCol`. Pass into
  `resolveIndexColumnAttribute` so each lookup is O(log N).
- **Expected impact:** On the 300-table operator-reality canary
  with each table having 10-30 attrs and 2-5 indexes with 1-4 cols
  each, **~30K String.Equals calls collapse to ~3K Map lookups**.
  Moderate impact; **highest single-finding leverage in Agent A's
  scope**.
- **Leverage:** Medium-High. Requires Map construction at
  `parseKindRow` entry — small additive code.

### A4. `parseRowsetBundle` — 8 sequential `List.groupBy >> Map.ofList` constructions

- **Location:** `src/Projection.Adapters.Osm/CatalogReader.fs:2191-2223`
- **Current shape:** 8 separate Map builds from independent
  `bundle.*` lists (`Attributes`, `Kinds`, `References`, `Indexes`,
  `IndexColumns`, `Triggers`, `Modules`, `entityByAttrId`,
  `columnChecksByEntity`). Each is O(N log N).
- **Proposed shape:** parallelize via `Array.Parallel.map` (CPU-
  bound, independent) — already a precedent in
  `PhysicalSchema.toPhysicalRows`. Independent groupings can build
  concurrently.
- **Expected impact:** ~5× faster context-prep on a multi-core box
  when `bundle.Attributes` is ~10K rows (300-table operator-reality).
  Caveat: current shape is already O(N) overall; parallelism wins
  only on large bundles.
- **Leverage:** Low. Defer until bench shows context-prep is
  materially hot.

### A5. `parseKindRow` reference assembly — intermediate list allocations

- **Location:** `src/Projection.Adapters.Osm/CatalogReader.fs:2061-2066`
- **Current shape:**
  ```fsharp
  attrRows
  |> List.collect (fun a ->
      Map.tryFind a.AttrId ctx.ReferencesByAttr
      |> Option.defaultValue []
      |> List.map (parseReferenceRowFor ...))
  ```
  For each attribute, allocates an intermediate list via
  `Option.defaultValue [] |> List.map`.
- **Proposed shape:**
  ```fsharp
  seq {
      for a in attrRows do
          match Map.tryFind a.AttrId ctx.ReferencesByAttr with
          | Some refs -> yield! refs |> Seq.map (parseReferenceRowFor ...)
          | None -> ()
  } |> Seq.toList
  ```
- **Expected impact:** Minor. Saves N intermediate `List<'a>`
  allocations per kind.
- **Leverage:** Low.

---

## Agent B — `Projection.Adapters.OssysSql/MetadataSnapshotRunner.fs`

**Scope:** OSSYS SQL rowset extraction. 1156 LOC; 7 bench labels added
under `adapter.osm.extract.*`.

### B1. Sequential `await` chain across 22 rowsets

- **Location:** `src/Projection.Adapters.OssysSql/MetadataSnapshotRunner.fs:729-761`
- **Current shape:** every `let! x = read ...` and `do! skip ...`
  awaits sequentially through the single `SqlDataReader`.
- **Why it's structural:** `SqlDataReader` with
  `CommandBehavior.SequentialAccess` is single-threaded and
  stream-positioned — the rowsets share a forward cursor. Cannot
  parallelize within one reader.
- **Proposed shape:** split the carbon-copied SQL into independent
  queries dispatched in parallel `SqlCommand`s on multiple
  connections (one per rowset cluster). Architectural.
- **Expected impact:** 22 sequential rowsets at ~50ms each ≈ 1.1s
  wall; 4-way parallel ≈ 280ms. **High** — but requires
  connection-factory + script-split.
- **Leverage:** High in absolute wall time; HIGH cost (architectural
  change). Defer to a dedicated slice.

### B2. `reader.ReadAsync()` per-row allocation

- **Location:** `src/Projection.Adapters.OssysSql/MetadataSnapshotRunner.fs:404`
- **Current shape:** `while hasMore do let! advanced = reader.ReadAsync() ...`
  — every row pays a `Task` allocation + state-machine box.
- **Proposed shape:** Either (a) use `reader.Read()` (sync) for
  rowsets known to fit in already-buffered network packets, OR
  (b) adopt the `ValueTask<bool>` overload added in
  `Microsoft.Data.SqlClient` 5.x to avoid Task boxing on the
  synchronous-completion path.
- **Expected impact:** At 300 tables × ~10 attrs × 22 rowsets ≈
  66K row-reads, ~50ns/row of Task allocation ≈ ~3ms wall but
  **significantly cleaner GC** (fewer Gen-0 collections).
- **Leverage:** Medium.

### B3. Per-rowset materialization to `list` via `ResizeArray` + `List.ofSeq`

- **Location:** `src/Projection.Adapters.OssysSql/MetadataSnapshotRunner.fs:400, 415`
- **Current shape:** `readResultSet` accumulates in
  `ResizeArray<'T>` then `List.ofSeq acc` — full reallocation of
  an `'a list` from the array. Downstream consumer is `toBundle`
  which immediately re-projects via `List.map` and `Map.ofList`.
- **Proposed shape:** return `'T array` (or `IReadOnlyList<'T>`)
  from `readResultSet`; adapt `MetadataSnapshot` fields to match;
  `Map.ofArray` exists.
- **Expected impact:** Drops one full list cons-chain allocation
  per rowset (~22 × N nodes). For 300-table catalog ≈ 100K cons
  cells eliminated.
- **Leverage:** Medium.

### B4. Closure helper `read`/`skip` captures

- **Location:** `src/Projection.Adapters.OssysSql/MetadataSnapshotRunner.fs:711-727`
- **Current shape:** each invocation allocates a closure record
  over four locals (`reader`, `advanceNext`, `report`, `mapper`).
- **Proposed shape:** inline the four-line body at each of the
  ~22 call sites OR hoist the helper to module-private accepting
  all dependencies as parameters.
- **Expected impact:** Minor (~22 closure allocations).
- **Leverage:** Low.

### B5. `Map.ofList` over 4 lookup maps in `toBundle` (RANK 5 GLOBAL)

- **Location:** `src/Projection.Adapters.OssysSql/MetadataSnapshotRunner.fs:928-935, 980-983, 1027-1037`
- **Current shape:** `List.map ... |> Map.ofList` builds intermediate
  tuple list then balanced tree (`Map.ofList` is `Map.ofSeq` is
  O(N log N) for the AVL build).
- **Proposed shape:** `Dictionary<int, _>` for these `EntityId` /
  `AttrId` / `FkObjectId` / `ParentAttrId` lookups — built once,
  read N times, no F# Map semantic value (no structural sharing
  needed since `toBundle` is single-shot).
- **Expected impact:** **O(N log N) → O(N)** construction; **O(log N)
  → O(1)** lookup. At N=300 entities + N=3000 attributes, ~30%
  faster `toBundle`.
- **Leverage:** **Medium-High**, easy win. Easily refutable via
  a Bench.scope before/after.

### B6. `tryParseUniformDataCompression` / `tryParsePartitionColumns` per-call JSON parse

- **Location:** `src/Projection.Adapters.OssysSql/MetadataSnapshotRunner.fs:1084, 1092`
- **Current shape:** every index row JSON-parses its
  `DataCompressionJson` and `PartitionColumnsJson` even when the
  field is `None`. Already short-circuits on `Option.bind` so the
  cost is paid only when JSON is present.
- **Why partial:** `JsonDocument.Parse` allocates and disposes per
  call.
- **Proposed shape:** if N indexes share identical compression-JSON
  strings (common: all-PAGE), a memoization cache keyed on the
  string would halve the cost.
- **Expected impact:** Low unless real-world OSSYS index counts
  exceed ~1000.
- **Leverage:** Low.

### B7. `toBundle` fully eager `List.map` cascade

- **Location:** `src/Projection.Adapters.OssysSql/MetadataSnapshotRunner.fs:937-1145`
- **Current shape:** each axis (modules, kinds, attributes,
  references, indexes, indexColumns, triggers, columnChecks)
  builds a full `list` even though `CatalogReader.RowsetBundle`
  consumers may iterate once.
- **Proposed shape:** lazy `seq` for axes whose downstream consumer
  is single-pass; per A35 ("Π's canonical output is a typed
  deterministic stream"), the rowset bundle could similarly be
  stream-shaped.
- **Expected impact:** Requires `CatalogReader.RowsetBundle`
  field-type changes (cross-surface).
- **Leverage:** Medium if upstream agrees. Flag, don't ship.

---

## Agent C — `Projection.Targets.SSDT/ScriptDomBuild.fs`

**Scope:** ScriptDom typed-AST construction. 1269 LOC; 16 bench labels
added under `emit.scriptDom.build.*`.

### C1-C3. `TSql160Parser` per-call allocation at 3 sites (RANK 7 GLOBAL)

- **Locations:**
  - `parseComputedExpression` — `ScriptDomBuild.fs:207`
  - `checkConstraint` — `ScriptDomBuild.fs:298`
  - `tryParseFilterWithDiagnostics` — `ScriptDomBuild.fs:944`
- **Current shape:**
  ```fsharp
  let parser = TSql160Parser(initialQuotedIdentifiers = false)
  // ... use parser once ...
  ```
  Inside each function — parser allocated per call.
- **Proposed shape:** hoist parser to a module-private `let` OR
  thread-static cache (ScriptDom's `TSql160Parser` is documented as
  **not thread-safe**; per-thread cache is the precedent — e.g.,
  `[<ThreadStatic>] let mutable parser : TSql160Parser option = None`).
- **Expected impact:** parser carries ~tens-of-KB of internal state
  per allocation. On a table with 5 CHECK constraints + 1 computed
  column + 1 filtered index, every CREATE TABLE pays 7 allocations.
  At 300 tables: ~2100 parser allocations on a single emit. **Very
  high** allocation-pressure reduction; modest absolute wall-time
  reduction (parser construction is fast; the GC pressure is the
  real cost).
- **Leverage:** **High** (architecturally clean; one helper at
  module scope; tests don't change).

### C4. `MultiPartIdentifier + bracketed` pattern at 6 sites

- **Location:** `src/Projection.Targets.SSDT/ScriptDomBuild.fs:535-538, 689-692, 761-764, 1009-1012, 1025-1028, 483-487`
- **Current shape:** Six call sites construct
  ```fsharp
  let mid = MultiPartIdentifier()
  mid.Identifiers.Add(bracketed col)
  ```
  from scratch.
- **Why partial:** ScriptDom requires per-fragment instances
  (typed-AST nodes can't be shared by reference into multiple
  parent trees). Hoisting the **instance** is invalid; but
  introducing a helper to consolidate the construction shape is
  valid.
- **Proposed shape:** module-private
  `let singleIdentColumnRef (col: string) : ColumnReferenceExpression`
  that consolidates the 6-line pattern.
- **Expected impact:** Code clarity / locality. Perf neutral (same
  allocations, fewer LOC).
- **Leverage:** Low (perf-wise); Medium (readability).

### C5. `args.PkColumns |> List.map` in `buildMergeStatementCore`

- **Location:** `src/Projection.Targets.SSDT/ScriptDomBuild.fs:654`
- **Current shape:** per-call `List.map` materializes a fresh list
  for the `foldBool` consumer. The list is small (PK column count,
  typically 1-3).
- **Proposed shape:** replace with
  `Bench.iterMap "emit.scriptDom.build.merge.onTerm"` to surface
  per-PK-column timing.
- **Expected impact:** Observability only; perf negligible at the
  cardinality.
- **Leverage:** Low.

### C6. `pkTerms` `List.map` + `List.append` in `buildUpdateStatementCore`

- **Location:** `src/Projection.Targets.SSDT/ScriptDomBuild.fs:858-865`
- **Current shape:** `cells |> List.map ... |> List.append (conditional tail)`
  allocates two lists per UPDATE.
- **Proposed shape:** build via `ResizeArray` accumulator or fold
  (still functional via `List.foldBack`).
- **Expected impact:** Per-UPDATE allocation reduction —
  meaningful when Phase-2 UPDATE count scales with cycle-
  participating row count (100s-1000s in operator-reality canary).
- **Leverage:** Medium.

### C7. `IdentifierOrValueExpression` duplicated in dataspace arms

- **Location:** `src/Projection.Targets.SSDT/ScriptDomBuild.fs:1101, 1106`
- **Current shape:** both `FilegroupDataSpaceSql` and
  `PartitionSchemeDataSpaceSql` arms construct
  ```fsharp
  let ds = FileGroupOrPartitionScheme()
  ds.Name <- IdentifierOrValueExpression()
  ds.Name.Identifier <- bracketed name
  ```
- **Proposed shape:** factor out `let nameRef (name: string) = ...`.
- **Expected impact:** Code clarity only; perf neutral.
- **Leverage:** Low.

### C8. `schemaObjectFromTableId` per-call allocation (structural floor)

- **Location:** `src/Projection.Targets.SSDT/ScriptDomBuild.fs:633-635, 692-694, 758-760`
- **Current shape:** each call allocates a fresh `SchemaObjectName`
  + two `Identifier` instances. For tables that emit MERGE +
  UPDATE + multiple InsertRows in sequence, the
  `TableId` → `SchemaObjectName` projection runs N times.
- **Why structural:** ScriptDom requires per-fragment instances —
  caching is invalid (would create shared-mutable-state bugs).
- **Proposed shape:** **None.** Recording so the next bench-table
  reader knows why these allocations are irreducible. The
  typed-AST library's per-fragment isolation requirement IS the
  cost; load-bearing for parse-roundtrip determinism.
- **Leverage:** N/A — documented as structural floor.

---

## Agent D — `Projection.Core/Catalog.fs` + `Policy.fs`

**Scope:** Core IR construction. 2064 LOC across two files; 9 bench
labels added under `ir.catalog.*` / `ir.policy.*` / `ir.kind.*` /
`ir.module.*`.

### D1. `Catalog.tryFindKind` linear scan (RANK 1 GLOBAL — highest single-finding leverage)

- **Location:** `src/Projection.Core/Catalog.fs:1180-1181`
- **Current shape:**
  ```fsharp
  let tryFindKind (ssKey: SsKey) (c: Catalog) : Kind option =
      c.Modules |> List.tryPick (Module.tryFindKind ssKey)
  ```
  where `Module.tryFindKind` is `List.tryFind`. Per-call
  O(modules × kinds_per_module). Hot at FK resolution / emitter PK
  lookups. At 300 tables × N references that's millions of
  comparisons.
- **Proposed shape:** maintain a `KindIndex : Map<SsKey, Kind>`
  computed lazily-and-cached, or pre-build at `Catalog.create` end
  (already walks every kind for `kindKeySet`).
- **Expected impact:** **O(n²) → O(n log n)** total per emitter
  pass; FK-heavy 300-table corpus likely sees double-digit ms
  shaved across all consumers.
- **Risk:** invalidates if Catalog mutability is introduced (it
  isn't today; immutable record). Document the invariant.
- **Leverage:** **HIGHEST**.

### D2. `Catalog.tryFindOwningModule` linear scan

- **Location:** `src/Projection.Core/Catalog.fs:1184-1186`
- **Current shape:** `List.tryFind` over modules + `List.exists`
  over kinds, O(modules × kinds).
- **Proposed shape:** piggyback on the same `KindIndex` by storing
  `(Module.SsKey × Kind)` pairs, OR a parallel `KindOwnership :
  Map<SsKey, SsKey>` (kind key → owning module key).
- **Expected impact:** Per-call O(n) → O(log n); compounds with
  consumer call frequency.
- **Leverage:** Medium. Pair with D1.

### D3. `Kind.tryFindAttribute` linear scan (RANK 2 GLOBAL)

- **Location:** `src/Projection.Core/Catalog.fs:1109-1110`
- **Current shape:**
  ```fsharp
  let tryFindAttribute (ssKey: SsKey) (k: Kind) : Attribute option =
      k.Attributes |> List.tryFind (fun a -> a.SsKey = ssKey)
  ```
- **Why hot:** called per FK column resolution in
  `StaticSeedsEmitter.deferredColumns`,
  `MigrationDependenciesEmitter`, `UserFkReflowPass` (per inline
  comment in the file). For a kind with 30 attributes × 300 kinds
  × multiple consumers, O(n²) compounds.
- **Proposed shape:** lazy/cached `Kind.AttributeIndex : Map<SsKey,
  Attribute>`, computed at `Kind.create` or memoized at first
  lookup.
- **Expected impact:** Per-call O(n) → O(log n); biggest win in
  tight FK reflow + static-seed paths.
- **Leverage:** **High**. The two-Map-construction cost (Kind +
  Catalog) is one-time; the lookup speedup is per-emitter-pass.

### D4. `Catalog.create` — repeated `Set.ofList` of attrKeys per kind (RANK 10 GLOBAL)

- **Location:** `src/Projection.Core/Catalog.fs:1236-1268`
- **Current shape:** For each kind, builds `attrKeys` Set TWICE —
  once for the reference loop (line 1242), once for the index loop
  (line 1267). Same Set, two allocations per kind.
- **Proposed shape:** hoist
  ```fsharp
  let attrKeys = k.Attributes |> List.map (fun a -> a.SsKey) |> Set.ofList
  ```
  once per kind and reuse for both reference and index validation.
- **Expected impact:** Halves Set construction allocations during
  `Catalog.create`; modest but trivially safe.
- **Leverage:** Low-Medium. **Trivially safe + on the bench label
  `ir.catalog.create`.** Easy first ship in the perf sweep.

### D5. `Catalog.create` — triple-walk over allKindList

- **Location:** `src/Projection.Core/Catalog.fs:1223 + 1237`
- **Current shape:** `allKindList = modules |> List.collect (fun
  m -> m.Kinds)` then `kindKeySet = allKindList |> List.map (fun
  k -> k.SsKey) |> Set.ofList`. Then `kindDupes` re-walks via
  `groupBy`. Three passes over the same kind list when one
  `foldBack` could collect all three (count, key set, duplicates).
- **Proposed shape:** single fold producing `(count, kindKeySet,
  duplicates)` tuple.
- **Expected impact:** Minor; main benefit is reducing allocation
  pressure on a hot constructor.
- **Leverage:** Low.

### D6. `SelectionPolicy.filterCatalog` — rebuilds modules on IncludeAll

- **Location:** `src/Projection.Core/Policy.fs:412-417`
- **Current shape:** Two-level `List.map` + `List.filter`; for
  `IncludeAll` case the entire catalog is rebuilt structurally
  identical (records re-allocated with same content).
- **Proposed shape:** short-circuit `IncludeAll` to return `c`
  unchanged.
- **Expected impact:** Saves O(modules × kinds) record allocations
  for the most common case. **Trivial fix; significant on hot emit
  paths when `Selection = IncludeAll` (which is the default).**
- **Leverage:** Medium. Easy ship.

### D7. `EmissionPolicy.filterPlatformAutoIndexes` — rebuilds every Kind record

- **Location:** `src/Projection.Core/Policy.fs:470-484`
- **Current shape:** Already short-circuits on `true`, but the
  `false` path rebuilds every Kind record. Most kinds will have
  zero platform-auto indexes.
- **Proposed shape:** per-kind short-circuit
  ```fsharp
  if k.Indexes |> List.exists (fun i -> i.IsPlatformAuto)
  then { k with Indexes = k.Indexes |> List.filter (fun i -> not i.IsPlatformAuto) }
  else k
  ```
  to skip record reallocation for unaffected kinds.
- **Expected impact:** Medium — typical V1 catalog has many kinds
  with zero auto indexes; avoid touching them.
- **Leverage:** Medium. Easy ship.

---

## Agent E — `Projection.Core/Passes/*.fs`

**Scope:** 5 pass files. 1634 LOC across NullabilityPass /
UniqueIndexPass / ForeignKeyPass / TopologicalOrderPass /
UserFkReflowPass; 6 bench labels added under `pass.<name>.*`.

### E1. TopologicalOrderPass Kahn's `Map<SsKey, int>` indegree (RANK 4 GLOBAL)

- **Location:** `src/Projection.Core/Passes/TopologicalOrderPass.fs:188-191`
- **Current shape:** `Map<SsKey, int>` indegree backing has
  `O(log n)` per-decrement. For 300-table V2 catalogs and dense
  FK graphs (5-10 refs/kind ≈ 1500-3000 decrements).
- **Proposed shape:** function-local `Dictionary<SsKey, int>`
  mutable backing. **The operating-disciplines table explicitly
  names Tarjan as the worked example for function-local mutables**
  — Kahn falls under the same discipline.
- **Expected impact:** Per-iteration **O(log n) → O(1)** on Kahn's
  hot loop. Bench label `pass.topologicalOrder.kind` would show
  measurable drop on 300-table canary.
- **Risk:** must preserve A24 lineage-trail order. Function-local
  mutable is the canonical pattern.
- **Leverage:** High. Direct ship.

### E2. TopologicalOrderPass Tarjan `Map`/`Set` (RANK 3 GLOBAL)

- **Location:** `src/Projection.Core/Passes/TopologicalOrderPass.fs:220-249`
- **Current shape:** `indices`, `lowlinks`, `onStack` are all
  `Map`/`Set` with `O(log n)` operations on the algorithmic hot
  path.
- **Proposed shape:** `Dictionary<SsKey, int>` + `HashSet<SsKey>`
  function-local. **Tarjan is the explicitly-named codification
  example.**
- **Expected impact:** `O(V log V) → O(V)`; meaningful on 300-table
  catalogs with dense cycles.
- **Leverage:** **High**. Worked-example precedent makes this
  uncontroversial.

### E3. TopologicalOrderPass `internalEdgesOf` — O(|scc|²) lookup

- **Location:** `src/Projection.Core/Passes/TopologicalOrderPass.fs:306-311`
- **Current shape:** for each SCC, nested
  `for a in members do for b in members do Map.tryFind (a,b) classified`.
  `O(|scc|² log |classifiedEdges|)` per SCC.
- **Proposed shape:** pre-compute
  `Map<SsKey, (SsKey * EdgeStrength) list>` keyed on source-of-edge
  (adjacency by classified edge) once per pass, then
  `O(|scc|² log |scc|)` per SCC by filtering pre-grouped children.
- **Expected impact:** Meaningful only when SCCs are large; on
  2-cycle-dominated graphs no change.
- **Leverage:** Low-Medium. Conditional on real-world SCC size.

### E4. ForeignKeyPass `sortedReferences` — double-sort intermediates

- **Location:** `src/Projection.Core/Passes/ForeignKeyPass.fs:100-107`
- **Current shape:**
  ```fsharp
  catalog.Modules
  |> List.collect (fun m -> m.Kinds)
  |> List.sortBy (fun k -> k.SsKey)
  |> List.collect (fun k ->
      k.References
      |> List.sortBy (fun r -> r.SsKey)
      |> List.map ...)
  ```
  Two `List.sortBy` passes each allocate intermediates.
- **Proposed shape:** same, but `Catalog.allKinds c` (smart-
  constructor accessor exists per A39) plus single-pass
  `List.collect` on the inner. Marginal.
- **Expected impact:** Minor.
- **Leverage:** Low. Not a hotspot.

### E5. Three-pass `sortedReferences` / `sortedContexts` shape repetition (RANK 9 GLOBAL)

- **Locations:**
  - `src/Projection.Core/Passes/NullabilityPass.fs:85-92`
  - `src/Projection.Core/Passes/UniqueIndexPass.fs:91-98`
  - `src/Projection.Core/Passes/ForeignKeyPass.fs:100-107`
- **Current shape:** all three pass files share an identical shape
  (`Modules → Kinds → sortBy SsKey → collect (Attributes/Indexes/References sorted)`).
  **At N=3 of the same shape, this hits the two-consumer threshold
  per the operating discipline.**
- **Proposed shape:** helper in `Catalog` or `Composition` —
  `sortedChildrenBy (fun k -> k.Attributes) (fun a -> a.SsKey)` or
  similar.
- **Caveat:** per `DECISIONS 2026-05-14 — opportunityEntry stays
  inlined: N=3 of two distinct shapes, not N=3 of one`, evaluate
  whether all three have the same shape (they DO — single child
  collection per kind) before extracting.
- **Expected impact:** Code clarity primarily; allocation reduction
  secondarily (one helper, three call sites).
- **Leverage:** **Medium.** Earns its place at N=3 of one shape.

### E6. UserFkReflowPass `applyStrategy` recursion

- **Location:** `src/Projection.Core/Passes/UserFkReflowPass.fs:194-220`
- **Current shape:** `FallbackToSystemUser` recursion discards the
  inner strategy's match label and emits the wrapper label.
  Per-candidate cost is constant (1-2 recursion levels typical).
- **Proposed shape:** **None recommended.** Flagged only because
  the per-candidate `pass.userFkReflow.candidate` bench may show
  bimodal distribution under `FallbackToSystemUser` (primary-hit
  vs fallback-fired); operators reading P95/P99 would see the
  second mode.
- **Expected impact:** Observability finding, not perf.
- **Leverage:** N/A.

### E7. TopologicalOrderPass `kahnSort` ready-list maintenance

- **Location:** `src/Projection.Core/Passes/TopologicalOrderPass.fs:182-203`
- **Current shape:** `ready` is repeatedly rebuilt via
  `(child :: ready) |> List.sort` on each decrement-to-zero.
  `O(k log k)` per insertion where k = `|ready|`.
- **Proposed shape:** `SortedSet<SsKey>` function-local (BCL
  `SortedSet<>` is `O(log k)` per insertion) OR a manual
  binary-heap.
- **Expected impact:** On 300-table catalogs with branchy FK graphs
  (large ready frontier), meaningful drop on
  `pass.topologicalOrder.kind` bench label.
- **Leverage:** Medium. Pair with E1+E2 (all three are Topo improvements).

---

## Cross-references

- **Slice that produced this document:** `DECISIONS 2026-05-19
  (slice A.4.7'-prelude.bench-fleet) — Five-agent parallel
  dispatch lifts Bench instrumentation coverage 44% → ~56%`
- **Per-agent task outputs (ephemeral; this document is the
  durable copy):**
  - Agent A: `tasks/a0587db8efef43cc3.output`
  - Agent B: `tasks/abdbe4c55b09d794e.output`
  - Agent C: `tasks/ae34dc5899067aea6.output`
  - Agent D: `tasks/aab2b23926ca547bb.output`
  - Agent E: `tasks/ab8e0e44aa05b0124.output`
- **Operating disciplines this work composes with:**
  - `CLAUDE.md` operating disciplines → "Bench-driven optimization
    protocol" (`DECISIONS 2026-05-24`) — three-candidate /
    2-refuted / 1-confirmed shape for hot-path optimizations
  - `CLAUDE.md` operating disciplines → "IR grows under evidence,
    not speculation" — perf changes need bench evidence, not
    presumption
  - `CLAUDE.md` operating disciplines → "Mutable state only
    function-local for performance-sensitive algorithms (Tarjan
    SCC, ResizeArray accumulators) — never module-level" — direct
    license for E1 + E2
  - `AXIOMS.md` A39 — aggregate-root smart-constructor invariants;
    `Catalog.create` / `Kind.create` are A39 instances; D1 + D3
    + D4 invest there
- **Bench label inventory affected (by opportunity rank):**
  - Rank 1 D1: cross-cutting (all emit + pass consumers)
  - Rank 2 D3: `emit.scriptDom.build.*`, `pass.fk.reference`,
    `pass.userFkReflow.candidate`
  - Rank 3 E2: `pass.topologicalOrder.kind`
  - Rank 4 E1: `pass.topologicalOrder.kind`
  - Rank 5 B5: `adapter.osm.extract.toBundle`
  - Rank 6 A3: `adapter.osm.parse.rowsetIndex`,
    `adapter.osm.parse.rowsetKind`
  - Rank 7 C1-C3: `emit.scriptDom.build.columnDefinition`,
    `emit.scriptDom.build.createIndex`
  - Rank 8 A1+A2: `adapter.osm.parse.index`,
    `adapter.osm.parse.rowsetIndex`
  - Rank 9 E5: `pass.fk.reference`, `pass.nullability.attribute`,
    `pass.uniqueIndex.index`
  - Rank 10 D4: `ir.catalog.create`

## Comprehensive-canary requirement (pre-perf-sweep)

The current operator-reality canary fires **6 of 51** new bench
labels. Before any perf-sweep work ships, a more comprehensive
canary must:

1. Load a catalog **via the OSSYS adapter** (CatalogReader.parse +
   MetadataSnapshotRunner.runAsync) — fires
   `adapter.osm.parse.*` and `adapter.osm.extract.*`
2. Run all four tightening passes with non-empty `Policy` — fires
   `pass.nullability.attribute`, `pass.uniqueIndex.index`,
   `pass.fk.reference`, `pass.userFkReflow.candidate`
3. Build the catalog via `Catalog.create` (not pre-built fixture)
   — fires `ir.catalog.create` + `ir.kind.create` + `ir.module.create`
4. Apply at least one non-empty Policy axis — fires
   `ir.policy.<axis>.create`
5. Exercise the **full data-emission path** (MERGE + UPDATE +
   InsertRow + SetIdentityInsert) — fires
   `emit.scriptDom.build.merge`, `.update`, `.insertRow`,
   `.setIdentityInsert`
6. Exercise the **full DDL-emission path** including
   ColumnCheck + ForeignKey + Index + ExtendedProperty + AlterIndex
   — fires the rest of `emit.scriptDom.build.*`
7. Exercise topological ordering with SCCs present — fires
   `pass.topologicalOrder.scc`

Once that canary lands and reaches a stable baseline, the perf-sweep
slice can pick from this menu with confidence that the bench surface
will show its wins.
