# PERF_HARNESS — the before/after measurement fleet (architecture + backlog)

**Status (2026-06-11).** Design committed; build not started. This document is
the architectural source of record for the perf harness. The recommendations
in §3 are RESOLVED (home: test project + shell orchestrator; comparison:
single-run deterministic delta with a named promotion trigger) — an agent
building the harness follows §3/§4 without re-litigating them; a re-open
requires a DECISIONS amendment naming what changed.

**Purpose.** Tee up the bottleneck-sweep optimizer (the next agent) with a
*reusable, in-repo, end-to-end* performance harness — one that exercises each
candidate hot path as an **isolated, scale-parameterized scenario**, measured
through the **existing `Bench` surface already curried into the production
functions**, and **orchestrated from a single entry point** that emits one
before/after comparison artifact.

**Anti-pattern this retires.** Ad-hoc throwaway console projects (`/tmp/*.fsx`,
scratch `dotnet run` apps) that *reimplement* a slice of a path to time it.
They drift from production behavior, can't be re-run by the next agent, and
leave no artifact. The reverse-leg already has the right shape
(`ReverseLegScaleTests` / `ReverseLegStreamingTests`: dedicated scale fixtures,
`Bench` rollups, rows/sec extrapolation); every other pipeline stage lacks it.
This harness generalizes that shape across all stages.

**Discipline this feeds.** The bench-driven optimization protocol
(`DECISIONS 2026-05-24`): three-candidate / 2-refuted / 1-confirmed with bench
data; refuted swaps documented. The harness IS the measurement substrate that
protocol requires — capture BEFORE, change, capture AFTER, the named labels
move (or don't). It also feeds the iterator-logging discipline: where a
scenario reveals a path whose hot loop has no per-iteration label, adding the
label is part of the scenario slice (§3.6).

---

## §1 The performance-testing activities, enumerated by pipeline stage

Each activity names: the **production entry point** (the real function the
scenario invokes — no reimplementation), the **`Bench` labels** it already
emits, the **scale knob**, the **current coverage** (is there an isolated
scenario today, or is it only entangled inside the round-trip canary?), and
any **measured prior** from the 2026-06-11 investigation (to be re-confirmed
*in-harness*, not trusted as-is).

### Stage 1 — Profiling & data extraction (the read side)

- **1a. LiveProfiler / EvidenceCache (discovery-then-derive).**
  Entry: `LiveProfiler.profile` over a deployed DB (discovers the substrate
  once into `EvidenceCache`; derives every Profile axis in pure F#).
  Labels: `profiling.*`, cache discovery scopes. Scale knob: table count ×
  rows/table × FK fan-in. Coverage: **fires inside the operator-reality canary
  but is not isolated** — no profiler-only bench. Prior: already
  discovery-then-derive optimized (chapter B.3; ~6000→~900 round-trips at 300
  tables). Measure before touching.

- **1b. ReadSide.readRowsStream (per-row carrier construction).**
  Entry: `ReadSide.readRowsStream cnn kind` drained to EOF.
  Labels: `readside.readRowsStream.<schema>.<table>`, `.all`, `.elements`,
  `readside.readRowsStream.open`. Scale knob: rows/table, cols/table.
  Coverage: **fires in canary readback; the per-row cost is entangled in the
  stream wall-time** — no label isolates the per-row `Map<Name,string>` build
  + `SsKey.synthesized` basis from the SqlClient wire read. Priors
  (2026-06-11, 100k rows × 12 cols, warm loopback):
  - SqlClient read floor (`ReadAsync` + `IsDBNull`+`GetValue`/col):
    ~1.9 µs/row; sync `Read` and `GetValues(buf)` within noise of it —
    **the driver strategy axis is a refuted candidate at loopback**.
  - `List.mapi |> Map.ofList<Name,string>` (current carrier build):
    ~1.85 µs/row; `Map.add` loop over pre-extracted arrays: ~1.44 µs/row
    (~22% cheaper); pre-sorted insertion order: ~1.57 (refuted — worse than
    the plain array loop).
  - basis `sprintf "%s.%s.%d"`: ~0.39 µs/row; precomputed-prefix +
    `string i`: ~0.06 µs/row (~6× cheaper).
  - End-to-end bulk100k measured ~8.8 µs/row through `readRowsStream` —
    so the IR-side per-row construction is *comparable to the wire read*
    at scale, with ~3-4 µs/row still unattributed (formatting arms, task
    machinery, GC amplification). **But** a column-array carrier is an
    IR-adjacent change (`StaticRow.Values : Map<Name,string>` is the
    contract; consumers key by `Name` across `SurrogateRemap`,
    `PhysicalSchema`, the emitters); weigh against the contract before
    committing. The harness must add a label isolating the per-row build
    so the before/after is honest (§3.6).

- **1c. OSSYS SQL extraction (`MetadataSnapshotRunner.toBundle`).**
  Entry: the C# rowset extractor. Labels: `adapter.osm.extract.*`. Scale knob:
  model size (entities × attributes). Coverage: **none** — the canary uses
  generated fixtures + ReadSide, never the OSSYS adapter. Gap.
  (PERF_OPPORTUNITIES Agent B: sequential 22-rowset await; 4
  `Map.ofList`→`Dictionary`.)

- **1d. OSSYS JSON parse (`CatalogReader`).**
  Entry: `CatalogReader.parse`. Labels: `adapter.osm.parse.*`. Scale knob:
  model size. Coverage: **none in canary.** Gap. (PERF_OPPORTUNITIES Agent A:
  `resolveIndexColumnAttribute` O(N×M); double-walk index partitions.)

### Stage 2 — SSDT schema emission

- **2a. SsdtDdlEmitter.statements → Render.toText.**
  Entry: `SsdtDdlEmitter.statements catalog |> Render.toText`. Labels:
  `emit.ssdt.statements`, `render.statement`, `emit.scriptDom.build.*`. Scale
  knob: table count × cols/table. Coverage: fires in canary (emit leg);
  **no deploy-free emit-only scenario** (every measurement today also pays
  deploy + readback). Cheap at current scale (`render.statement` ~143 ms /
  2210 calls in operator-reality) — the scenario exists to PROVE it stays
  cheap as the estate scales, not because it is suspected hot.

- **2b. ScriptDomBuild per-statement + TSql160Parser cache.**
  Entry: `ScriptDomBuild.buildCreateTable` / `buildColumnDefinition` /
  `buildCreateIndex`. Labels: `emit.scriptDom.build.*`. Coverage: in canary;
  already optimized (perf-sweep-3 ThreadLocal parser). Measure before touching.

### Stage 3 — Bootstrap / data emission (the INSERT/MERGE scripts) — **highest-gap stage**

- **3a. StaticPopulationEmitter.statements (the typed `InsertRow` bulk stream).**
  Entry: `StaticPopulationEmitter.statements catalog`. Labels:
  `emit.staticPopulation.statements.stream`, `.elements`. Scale knob: rows.
  Coverage: fires in canary — the TOP emit label at scale (500k elements in
  bulk100k). Caveat for the optimizer: this is a **lazy streamProbe** — its
  wall-time includes consumer time between pulls (`executeStream`'s SQL
  round-trips), so its number is NOT pure emit cost. The isolated scenario
  drains the stream into a sink without deploying, which is what makes the
  emit cost attributable at all.

- **3b. StaticSeedsEmitter.renderMerge + MigrationDependenciesEmitter.renderMerge
  (the `Data/seed.sql` MERGE-script TEXT emission).**
  Entry: `DataEmissionComposer.composeRendered` →
  `StaticSeedsEmitter.renderMerge` (ScriptDom `MergeStatement` →
  `ScriptDomGenerate.generateOne`). Labels: `emit.staticSeeds.renderMerge`,
  `emit.staticSeeds.phase2Row`, `emit.migrationDeps.renderMerge`,
  `compose.data.composeRendered`. Scale knob: rows/kind × kind count.
  Coverage: **NOT in the operator-reality canary** (that rides the bulk path);
  fired only in `DataEmissionComposerTests` + the AC-X1 seed test at toy scale
  (2 rows). **Gap.** Named handoff candidate: every row becomes one
  table-value-constructor row inside one MERGE `USING (VALUES …)` — the
  1000-row TVC parse cap that bit the transfer side likely bounds the *emitted*
  script's executability too (see 3c).

- **3c. How the emitted Bootstrap MERGE scripts EXECUTE.**
  Entry: deploy `Data/seed.sql` via `Deploy.executeBatch` (the AC-X1 path:
  `MigrationCanaryTests` "AC-X1: the full-export seed bundle…").
  Labels: `deploy.executeBatch`, `.segment`, `.segment.bytes`,
  `batchSplitter.scriptDom`. Scale knob: rows × cols (→ rendered bytes).
  Coverage: **AC-X1 deploys a 2-row seed; no scale bench of executing a large
  rendered MERGE bundle.** **Gap — the handoff's prime candidate.** Two
  hypotheses the scenario must let the optimizer test:
  1. *The cliff*: a kind whose static population exceeds the ~1000-row
     table-value-constructor cap renders a MERGE that SQL Server may refuse
     outright (the transfer side hit Msg-10738-class behavior on the
     `VALUES`-form MERGE) — a **correctness** finding, not just perf, if
     confirmed against the emitted shape.
  2. *The slope*: below the cliff the single-statement MERGE is parse-bound
     (transfer side measured ~5.7k rows/sec on the VALUES form).
  The candidate alternative shape (for the optimizer, NOT pre-decided): the
  staged-bulk pattern from `SurrogateCapture` adapted to an emitted script —
  `SELECT TOP 0 ISNULL(col,col) … INTO #stage FROM target` (ISNULL strips
  IDENTITY through SELECT INTO; a CASE wrapper does NOT — probed live
  2026-06-10), chunked ≤1000-row `INSERT … VALUES` into `#stage`, then ONE
  `MERGE … USING #stage` — preserving single-MERGE semantics (the
  `DeleteScope` arm CANNOT be naively chunked: a `WHEN NOT MATCHED BY SOURCE`
  arm in a chunk-scoped MERGE deletes the other chunks' rows; any chunked
  multi-MERGE candidate must carry a DeleteScope-correctness witness).

### Stage 4 — Deploy (the write side)

- **4a. Deploy.executeStream (`InsertRow`→`SqlBulkCopy` folding).**
  Entry: `Deploy.executeStream cnn statements`. Labels: `deploy.executeStream`,
  `deploy.executeStream.input`, `deploy.bulk.copyRows`, `.batchSize`. Scale
  knob: rows, **`DefaultBulkBatchSize` (5000, a `[<Literal>]` —
  the scenario parameterizes around it by feeding controlled streams)**.
  Coverage: fires in canary + bulk canary, but never as a batch-size sweep.
  Note: the per-row `values |> List.map (fun v -> v.Column)` shape-list
  allocation + structural comparison inside the fold is a micro-candidate the
  sweep can size.

- **4b. Deploy.executeBatch (segment splitting).**
  Entry: `Deploy.executeBatch cnn sql`. Labels: `deploy.executeBatch`,
  `.segment`, `.segment.bytes`, `batchSplitter.scriptDom`. Scale knob: script
  size. Coverage: in canary (operator-reality: ~666 KB of DDL split + executed
  across two DBs). The server-side DDL execution inside `.segment` is
  host-I/O-bound (soft tier); the code-controlled part is
  `batchSplitter.scriptDom`.

- **4c. Deploy.executeBatchParallel + leveled deploy.** Already shipped
  (perf-sweep-5/6, canary 3:34 → 2:22). Reference, not a fresh target.

### Stage 5 — Transfer (the reverse leg) — the template, already optimized

- **5a. SurrogateCapture lanes / PackedSurrogateRemap / streaming realization.**
  Entry: `Transfer.runStreaming*`. Benches: `ReverseLegScaleTests`,
  `ReverseLegStreamingTests` (rows/sec extrapolation; ~35.5k rows/sec loopback;
  288M ≈ 2.3 h). Coverage: **dedicated scale benches exist — this is the shape
  the harness replicates.** Not a target; the worked example.

### Cross-stage (measured, currently un-owned by the operator's list)

- **X1. PhysicalSchema verification (`ofCatalog` / `rows.hash` /
  `runWideCanary.diff`).** Pure-CPU canary verification machinery; at bulk100k
  it totals ~14 s of the 25.7 s wall (ofCatalog 5.2 s + hash 2.6 s + diff
  6.5 s) — bigger than the read side. Hard-gated labels exist. Not in the
  operator's named sweep list, but it is the largest UNNAMED code-controlled
  block at scale; enumerate it so the optimizer can raise it deliberately
  rather than discover it accidentally.

---

## §2 The coverage-gap map (what the harness must ADD as isolated scenarios)

| Activity | Isolated scenario today? | Harness adds |
|---|---|---|
| 1a Profiler/EvidenceCache | No (entangled in canary) | profiler-only scenario over a pre-deployed DB |
| 1b ReadSide per-row carrier | No (cost hidden in stream wall-time) | read-only drain scenario + an isolating label (§3.6) |
| 1c OSSYS extract | **None** | extract-only scenario over a fixture model |
| 1d OSSYS parse | **None** | parse-only scenario over a fixture JSON |
| 2a SSDT emit | Partial (deploy-coupled) | deploy-free emit-only scenario |
| 3a StaticPopulation stream | Partial (consumer-entangled lazy probe) | drain-to-sink scenario (pure emit cost) |
| 3b Rendered-MERGE emit | **Toy-scale only** | `composeRendered` at N∈{1k,10k,100k} rows |
| 3c Rendered-MERGE EXECUTE | **Toy-scale only** | deploy a large rendered bundle; cliff + slope + alternative shapes |
| 4a executeStream batch boundary | Yes (bulk canary) but not parameterized | batch-size sweep over controlled streams |
| X1 PhysicalSchema verify | Entangled in canary | hash/diff-only scenario over in-memory rows |
| 5 Transfer | **Yes — the template** | (already covered) |

---

## §3 Architecture — RESOLVED recommendations

### 3.1 Home: test project + shell orchestrator (RESOLVED)

The harness lives in `tests/Projection.Tests` as **`PerfHarnessScenarios.fs`**
(the scenario catalog + the `measure` orchestration core) gated behind
`PROJECTION_RUN_PERF_HARNESS=1`, in the **`Docker-SqlServer` collection**, with
**`scripts/perf-harness.sh`** as the operator/agent entry point — a sibling of
`scripts/perf-gate.sh`.

Why this and not the alternatives:

- **CLI subcommand (`projection bench <scenario>`) — rejected for now.** It
  would be operator-runnable without `dotnet test`, but it has to rebuild the
  warm-container acquisition, ephemeral-DB lifecycle, and fixture wiring that
  `EphemeralContainerFixture` + `SourceFixtures` already own in the test
  project — duplicated lifecycle code is exactly the drift the harness exists
  to prevent. Re-open trigger: an operator (not an agent) wants to run
  scenarios on a box without the test tree; at that point the scenario catalog
  is already a library and the CLI verb is a thin face over it.
- **Dedicated in-process F# orchestrator (no shell) — rejected.** One artifact,
  but it diverges from the established orchestration grain: `perf-gate.sh`
  already owns warm-only preconditions, soft-skip rules, and JSON aggregation;
  agents already know `scripts/*.sh` as the way runs are launched and
  baselines recorded. The fleet-runner logic that would live in F# instead
  lives in ~80 lines of bash that match the house pattern.

Constraints the home inherits (do not re-derive these; they are the test-runner
disciplines):
- Docker pool rules apply: never run concurrently with the pure pool;
  `TaskSync.run` for blocking waits (never bare `GetAwaiter().GetResult()`);
  warm container honored via `PROJECTION_MSSQL_CONN_STR`
  (`scripts/warm-sql.sh conn`).
- The env gate (`PROJECTION_RUN_PERF_HARNESS=1`) keeps the fleet OUT of
  `test.sh docker` / CI by default — same idiom as
  `PROJECTION_RUN_BULK_CANARY` / `PROJECTION_RUN_REALISTIC_CANARY`. Ungated,
  the 100k-scale scenarios would put minutes into every Docker-pool run.

### 3.2 The scenario abstraction (the "functional curry")

The measurement is *already* curried into the production code: every hot path
opens with `use _ = Bench.scope "label"` / is wrapped in `Bench.streamProbe` /
`AsyncStream.probe`. The harness adds one thin layer — a **`PerfScenario`**
whose `Run` is a *curried partial application of the real production function*
with a scale knob baked in. No path is reimplemented; the workload IS the
production function.

```fsharp
/// One measurable activity. `Run` closes over the REAL production entry
/// point; the scale knob is partially applied by the scenario builder.
/// The Bench labels live inside Run's callees (already curried in).
type PerfScenario =
    { Name      : string                            // "readside-rowstream-100k"
      Tag       : string                            // bench JSON tag, e.g. "perf.readside.rowstream"
      KeyLabels : string list                       // the labels the comparison keys on
      Run       : ScenarioContext -> Task<unit> }

/// What every scenario receives: the warm connection lifecycle + scale.
/// Wraps EphemeralContainerFixture's WithEphemeralDatabase so scenarios
/// get a fresh DB on the warm container without owning lifecycle code.
type ScenarioContext =
    { WithDatabase : string -> (SqlConnection -> Task<unit>) -> Task<unit>
      Scale        : ScaleKnob }

type ScaleKnob =
    { Rows : int; Tables : int; ColumnsPerTable : int }
```

Scenario builders are curried constructors over the production entry points —
this is the catalog the optimizer extends:

```fsharp
// Stage 1 (read)
let readsideRowStream  (rows: int) : PerfScenario   // deploy fixture once → drain ReadSide.readRowsStream to EOF
let profilerDiscover   (tables: int) (rows: int) : PerfScenario  // LiveProfiler over pre-deployed DB
let ossysParse         (spec: GenerateSpec) : PerfScenario       // CatalogReader.parse over fixture JSON (no Docker)
// Stage 2 (schema emit) — no Docker needed
let ssdtEmitOnly       (tables: int) : PerfScenario  // SsdtDdlEmitter.statements |> Render.toText |> sink
// Stage 3 (bootstrap)
let staticPopulationDrain (rows: int) : PerfScenario // statements |> streamProbe |> Seq.iter ignore (pure emit)
let seedMergeRender    (rowsPerKind: int) : PerfScenario  // composeRendered at scale (no Docker)
let seedMergeExecute   (rowsPerKind: int) : PerfScenario  // render → deploy DDL → executeBatch the bundle
// Stage 4 (deploy)
let executeStreamBatch (rows: int) (batchSize: int) : PerfScenario // controlled InsertRow stream → executeStream
// Cross-stage
let physicalSchemaVerify (rows: int) : PerfScenario  // ofCatalog + rows.hash + diff over in-memory rows (no Docker)
```

Note the split: **pure scenarios** (parse, emit-only, render, verify) need no
Docker and could in principle run in the pure pool — they stay in the same
file/collection anyway so the fleet is one surface, but their `Run` simply
never calls `WithDatabase`. The orchestrator doesn't care.

### 3.3 The orchestration core

```fsharp
/// Reset → run the real path → snapshot → persist under the scenario's tag.
/// bench/perf/<name>/<utc>.json — deliberately NOT bench/canary/ so the
/// perf-gate's snapshot discovery (ls bench/canary/*.json) never picks up
/// harness runs as canary evidence.
let measure (ctx: ScenarioContext) (s: PerfScenario) : Task<Bench.Run>
```

- One gated `[<Theory>]` (or per-scenario `[<Fact>]`s — whichever keeps
  `--filter` ergonomics; the filterable name must carry scenario + scale,
  e.g. `PerfHarness: seed-merge-execute 10k`) iterates the catalog.
- `Bench.reset()` per scenario gives clean per-scenario rollups; scenarios
  must therefore run serially (the Docker collection already guarantees it).
- Each scenario prints its own `Bench.renderTable` top-N at completion (the
  reverse-leg idiom) AND persists the full snapshot JSON.

### 3.4 The shell orchestrator (`scripts/perf-harness.sh`)

Sibling of `perf-gate.sh`; same soft-skip + warm-only preconditions. Contract:

```
scripts/perf-harness.sh list                  # enumerate scenarios (no run)
scripts/perf-harness.sh run [filter]          # run fleet (or filtered subset) → bench/perf/<name>/<utc>.json
scripts/perf-harness.sh capture before [f]    # run + symlink/copy snapshots to bench/perf/<name>/before.json
scripts/perf-harness.sh capture after  [f]    # same → after.json
scripts/perf-harness.sh diff [filter]         # per-scenario before/after table over KeyLabels
```

`diff` output is per-label: `before TotalMs → after TotalMs (Δ%, Count
before/after)` plus derived rows/sec where a `<label>` + `<label>.elements`
pair exists (the streamProbe convention makes this structural). Count drift on
a deterministic scenario is flagged loudly — a count change means the workload
changed, and the timing delta is void.

### 3.5 Comparison method: single-run deterministic delta (RESOLVED)

Per-scenario before/after is **one warm run each, diffed directly on the
scenario's `KeyLabels`**. Rationale: the fixtures are seed-deterministic
(`FixtureGenerator` byte-identical per seed), the scenarios are isolated (one
path, not a whole canary), and the comparison is *self-relative on one host in
one session* — the cross-machine variance that forced `perf-gate.sh` to a μ+σ
model doesn't apply to an optimizer's tight loop. The protocol the optimizer
follows: capture `before`, apply ONE candidate, capture `after`, read the
delta; a candidate is confirmed only if its KeyLabels move beyond ~2× run-to-run
jitter (sanity-check jitter once with two back-to-back `before` captures).

**Named promotion trigger:** a scenario whose back-to-back same-code captures
disagree by more than ~15% on its KeyLabels is NOISY — promote that scenario
to N=5 capture + μ+σ comparison (reusing the `perf-gate.sh` aggregation
python verbatim) rather than trusting single runs. Expected noisy candidates:
anything dominated by server-side execution (3c, 4b) on a contended host.
Do NOT promote pure-CPU scenarios speculatively; the deterministic delta is
the fast lane that keeps the optimization loop tight.

**Interplay with the perf gate (do not confuse the two):** `perf-gate.sh`
remains the REGRESSION gate over the operator-reality canary (μ+σ, committed
baseline, fires on Stop hook). The harness is the OPTIMIZATION instrument —
exploratory, per-scenario, before/after, never gating. A confirmed win that
legitimately moves a canary-visible floor still ends with the existing ritual:
`PERF_GATE_RECORD=1` re-record + DECISIONS amendment. Harness artifacts live
under `bench/perf/` (gitignored except for committed baseline notes in commit
messages); the canary baseline lives where it always has.

### 3.6 In-code label additions the scenarios require (production touches)

The harness is mostly additive test+script code, but honest attribution needs
a few labels INSIDE production functions — these are part of the harness
slices, follow the iterator-logging discipline, and are deliberately tiny:

1. **`readside.rowstream.materialize`** — a scope (or paired sample) isolating
   the per-row carrier build (`List.mapi`+`Map.ofList`+basis+`SsKey`) from the
   wire read inside `ReadSide.readRowsStream.pull`. Without it, 1b's
   before/after can't attribute. (Cost note: a per-row `Bench.scope` is a
   Stopwatch + dict append per row — measure its own overhead in the scenario
   before trusting fine deltas; if it distorts, sample every Nth row or record
   one aggregated sample per stream instead.)
2. **`emit.staticSeeds.renderMerge.rows`** — `recordSample` of row count per
   rendered MERGE, so 3b gets a `<label>`+rows pair → rows/sec derivable.
3. **`profiling.*` top-level scope audit** — verify `LiveProfiler.profile` has
   one enclosing scope + per-kind iteration labels; add the enclosing scope if
   absent so 1a has a root label.
4. **`physicalSchema.diff`** — if `runWideCanary.diff` is the only label, X1
   needs the diff cost scoped where it lives so the scenario doesn't need the
   whole canary.

Anything beyond these (new per-element labels in hot loops) lands only when a
scenario demonstrates the need — labels are also code.

### 3.7 Fixture strategy

- **Docker-backed scenarios** reuse `FixtureGenerator` / `GenerateSpec`
  (seed-deterministic; `BulkSeeds` for fast source loading) and
  `EphemeralContainerFixture.WithEphemeralDatabase` on the warm container.
- **Static-population scenarios (3a/3b/3c)** need catalogs whose kinds carry
  `Modality = [Static rows]` at parameterized row counts — check whether
  `GenerateSpec` can already mint static-entity populations at arbitrary
  rows/kind; if not, the scenario builder constructs the catalog directly via
  the production smart constructors (the `ReverseLegScaleTests.meshModel`
  idiom: `Kind.create` / `Catalog.create` + generated rows). Deterministic
  values only (no `Random` without a fixed seed).
- **OSSYS scenarios (1c/1d)** need a fixture model JSON / rowset bundle at
  scale — check `fixtures/` + existing parity tests for a generator; if only
  small fixtures exist, 1d can synthesize JSON via the codec's own serializer
  (declarative-test-inputs discipline) and 1c stays blocked until a rowset
  fixture generator exists (note it in the scenario as a named skip).

### 3.8 Traps (inherited; do not rediscover)

- FS3511 Release-build shapes in `task` CEs: no `let rec` inside `task`, no
  tuple `let!`, no tuple-pattern `for` (documented 2026-06-10 DECISIONS).
- `[<Literal>]` only on CLR primitives.
- Lazy `streamProbe` wall-times include consumer time between pulls — never
  read a probe on an undrained/consumer-entangled stream as production cost.
- `readside.`/`deploy.`/`fixture.bulkLoader`/`batchSplitter.` prefixes are
  soft-tier in the perf gate (host-variable); inside the harness they are
  still the measurement — the soft tier is a GATING tier, not a "don't
  measure" tier.
- Never `pgrep`-guard runs; stream output to a file, don't pipe through
  `tail` while watching.

---

## §4 Backlog — build order with acceptance criteria

**Slice 0 — the spine.** `PerfHarnessScenarios.fs` (types + `measure` +
env gate + ONE scenario: `ssdtEmitOnly`, the cheapest, Docker-free) +
`scripts/perf-harness.sh` (`list`/`run`/`capture`/`diff`).
*Accept:* `perf-harness.sh capture before ssdt-emit-only` then `capture after`
(no code change) then `diff` shows ~0Δ and the artifact paths are stable;
`test.sh docker` does NOT run the fleet (gate respected).

**Slice 1 — 3c + 3b: the seed-MERGE pair** (the handoff's prime candidate).
`seedMergeRender` + `seedMergeExecute` at 1k/2.5k/10k rows/kind + the §3.6
`renderMerge.rows` sample. *Accept:* the cliff hypothesis is ANSWERED with
evidence (either the >1000-row rendered MERGE executes, or its named failure
is captured in the scenario output as the BEFORE witness); below-cliff rows/sec
recorded; both numbers land in this doc's §5 (priors table) and the optimizer
can re-run them with one command.

**Slice 2 — 1b: ReadSide drain** + the `materialize` isolating label (with the
label-overhead self-check). *Accept:* the harness reproduces the ~8.8 µs/row
end-to-end prior and splits it into wire vs materialize; the 2026-06-11 priors
(Map.add −22%, basis-concat −6×) become re-runnable claims.

**Slice 3 — 4a: executeStream batch sweep** over controlled streams at
batchSize ∈ {1k, 5k, 20k}. *Accept:* rows/sec per batch size, one table.

**Slice 4 — 3a drain + X1 verify + 1a profiler.** *Accept:* each has a root
label and a clean single-run delta; profiler scenario confirms (or refutes)
the "already optimized, leave it" prior with a number.

**Slice 5 — 1d OSSYS parse (1c extract if a fixture generator exists).**
*Accept:* parse-only scenario at ≥1k-entity scale; PERF_OPPORTUNITIES A3/A4
priors become measurable.

Each optimization that later ships against these scenarios carries
before/after numbers in the commit message, per the bench-driven optimization
protocol; the harness run that produced them is named by scenario + scale so
it is one command to reproduce.

---

## §5 Measured priors (2026-06-11 session; re-confirm in-harness before relying on them)

| Path | Measurement | Number |
|---|---|---|
| bulk100k whole canary (warm) | `deploy.runWideCanary` | ~25.7 s |
| ReadSide end-to-end | `readside.readRows` 1M rows | ~8.8–9.0 µs/row |
| SqlClient wire floor (12 cols, loopback) | standalone | ~1.9 µs/row (async/sync/GetValues all ≈) |
| Per-row carrier build (current) | standalone | ~1.85 µs/row |
| Per-row carrier build (Map.add array loop) | standalone | ~1.44 µs/row |
| Per-row basis sprintf → concat | standalone | 0.39 → 0.06 µs/row |
| PhysicalSchema verify block at bulk100k | ofCatalog+hash+diff | ~14 s combined |
| executeStream deploy leg at bulk100k | copyRows-dominated | ~4.9 s / 500k rows |
| operator-reality emit (SSDT) | `render.statement` | ~143 ms / 2210 stmts |
| Transfer streaming (template) | reverse-leg benches | ~35.5k rows/sec loopback |
| VALUES-form MERGE (transfer side, prior session) | parse-bound | ~5.7k rows/sec; 1000-row TVC cap |

The standalone numbers came from throwaway scripts (the anti-pattern); slices
1–2 convert them into harness-reproducible claims or correct them.
