# SINGLE_SCAN_PROGRAM — one estate scan, everything derives, emission overlaps

> Opened 2026-07-02 (operator directive: "Let's do all of these! Measure all
> the way with a sizable enough corpus to make it hurt a little when we're
> wrong, but not enough to make us inefficient."). This document is the
> program's durable plan + running measurement ledger — it survives session
> boundaries; update the status boxes and the results tables in place.

## North star

A publish's wall-clock floor is `max(bytes-over-wire, render CPU)` — not
their sum, and not ×2 estate passes. Re-based V1 reconciliation (see
PERF_OPPORTUNITIES.md § CORRECTION): both editions read the whole estate for
Bootstrap; V2's gap is a SECOND full pass (live-profile row scans) +
heavier per-row carriers + more artifacts. Kill the second pass, overlap
the remaining work, slim the carriers.

## The corpus (P0)

"Hurts a little": ~480k rows / 240 tables, variegated, deterministic —
~30% more rows than the reference estate, sized so a wrong turn costs
seconds-to-minutes, not hours.

- 200 × `narrow` (5 cols: int PK, nvarchar(20), int?, bit, date) × 800 rows
- 30 × `medium` (10 cols: + decimal(18,2), datetime, guid, nvarchar(100)?,
  int?) × 4,000 rows
- 10 × `wide` (24 cols: 4×int?, 4×nvarchar(50/200)?, 4×decimal, 4×date/
  datetime, 2×bit, 2×guid, 2×bigint, nvarchar(400)) × 20,000 rows
- NULL density: every 7th nullable cell NULL (deterministic by row index).
- Harness: `tests/Projection.Tests.Integration/PerfCorpusMeasurementTests.fs`,
  env-gated `PERF_CORPUS=1` (never in the normal docker pool / perf-gate;
  the gate's filter stays `Operator-reality`).
- Every optimization leg asserts VALUE IDENTITY against the incumbent path
  (row maps / evidence caches / rendered SQL bytes) before its timing counts.

## Measurement protocol

1. Seed once per run (measured but not gated).
2. Unmeasured warmup pass (JIT, plan cache, pool).
3. Legs, each printed as `[corpus] <leg>: <ms> (<qualifier>)`:
   - hydrate serial (incumbent), hydrate concurrent-4
   - profile live-scan (incumbent captureEvidenceCache*, concurrent-4)
   - profile single-scan-derived (P1; after it lands)
   - emit data lane (StaticSeedsEmitter over grafted corpus)
   - end-to-end acquire+emit pipelined (P2; after it lands)
4. Run solo (survival rule 13); two samples minimum on the moved legs.

## The items

- [x] **P0 corpus + BEFORE** — harness landed; baselines below. Findings,
  as amended by the clean re-samples: ~~local concurrency inversion~~
  **RETRACTED** (finding #1 was rule-13 contamination — two quiet-host
  re-samples show c4 beating serial locally too, 5.2–5.4s vs 7.7–8.3s;
  the BEFORE c4 sample ran under concurrent host load), the measured
  2.3× row-carrier tax (stands — P3 thesis), and emit as the local
  dominator (~3–4MB/s ScriptDom; stands) — so P3 and emit-CPU
  reduction still rank ABOVE P2's overlap at local scale, while P1 +
  concurrency are the wire-dominated (remote) wins.
- [x] **P1 single-scan evidence unification** — LANDED. `EvidenceCache
  .cachedKindOfRows` + `CachedValue.ofRaw` (Core, pure) derive a kind's
  `CachedKind` from hydrated `StaticRow`s; `LiveProfiler
  .captureEvidenceCacheDerived` partitions hydrated-vs-live kinds (named
  Bench counters `profile.live.derived.kinds` / `.fallback`), one global
  nullability reflection replaces per-kind reflection; the pipeline
  threads `bootstrapRows` into the profile stage. Per-kind SQL budget:
  2 queries + 1 stream → **0 queries** (+1 global reflection per
  publish; corpus: 480 aggregate/reflection queries + 240 full streams
  eliminated). Value-identity law held on BOTH corpus samples:
  `Assert.Equal<Map<SsKey, CachedKind>>(liveCache.Kinds, derivedCache
  .Kinds)` across 480k rows. Named equivalence caveats (docstring +
  pure pins in `EvidenceCacheDerivationTests`): full hydration required
  (sampled kinds keep live discovery), and the `""`≡NULL universal
  sentinel (NM-18 / `Tolerance.EmptyTextNormalizedToNull`) means derived
  evidence observes the IR plane (what publish ships) where live
  observes the source plane — equal modulo that already-named erasure.
  Local timing ~equal to the live scan (both ~3.5s — the derivation is
  CPU-shaped like the scan it replaces); the wire win is structural:
  the SECOND full-estate pass is gone, which on remote links is the
  entire live-profile wall-clock, not a ratio of it.
- [x] **P2 acquire→emit pipelining (mechanism + measurement)** — LANDED.
  Three factorizations, each equivalence-by-construction (the batch path
  folds the same per-kind unit) and pinned in the pure pool:
  `DataLoadPlan.loadForWith`/`loadFor` (the per-kind load build),
  `StaticSeedsEmitter.renderLoad` (the per-kind MERGE render), and
  `Ingestion.collectInOrderForConcurrentWith` (the projected drain: a
  per-kind pure computation runs on the drain worker the moment its rows
  land, inside the gate, after the connection pools back). The corpus's
  composed single pass (P2 ∘ P1: drain + render + derive per kind on 4
  workers, rows dropped after projection) measured **26,974ms vs
  44,554ms** two-phase sum (hydrate c4 + derive + emit) — **-39%
  wall-clock** — and BEAT the serial emit leg alone (35,707ms same run):
  the drain workers parallelize the render CPU as a byproduct. Live row
  memory caps at `concurrency` kinds instead of the estate. Identity
  laws held: per-kind rendered text byte-equals the two-phase artifact;
  drain-derived evidence equals the live-scan cache.
  **Production wiring (the follow-on slice, gates named):** the chain's
  catalog-rewriting steps are all profile-INDEPENDENT (`catalogStep`
  builds with `fun _ _`; profile feeds only decision passes), so the
  post-chain catalog is computable BEFORE the wire opens. The
  drain-time render needs three pre-drain facts: the post-chain catalog
  (always available — profile-independent), `CdcAwareness` (a
  reflection, obtainable pre-drain like the nullability batch), and the
  `UserRemapContext` (profile-dependent via `UserFkReflowPass` — gate:
  pipeline only when the remap is empty/config-known; otherwise the
  named refusal keeps the two-phase path). Restructuring
  `runWithConfigCore` to split catalog-steps-pre-drain from
  decision-steps-post-profile is the slice boundary.
- [x] **P3 row-carrier slimming** — MEASURED then LANDED per the item's own
  law. The corpus put the IR rebuild (per-row `Map<Name,string>` mint +
  row-identity synthesis) at **3.35×** the raw positional drain (8,023–
  8,388ms materialized vs 2,377–2,455ms quanta — ~70% of hydrate
  wall-clock is carrier construction, not wire). Positional twins landed:
  `KindColumns.quantumToTypedValues`, `EvidenceCache.cachedKindOfQuanta`,
  `StaticSeedsEmitter.renderQuanta`, `Ingestion
  .collectQuantaForConcurrentWith`; row identity mints through the ONE
  shared `StaticRow.readsideIdentity` (now also the IR boundary's mint),
  so the quanta pass equals the named-row pass at FULL record grain
  (pinned pure + corpus). Honest composed-pass verdict: on the local
  render-CPU-saturated pass the quanta pass is parity-to-slightly-slower
  (two samples: +8.1% / +7.6% — the render dominates and the carrier
  savings shift allocation timing rather than total wall-clock); its
  measured wins are the drain side (3.35×), allocation volume, and any
  wire-dominated or memory-pressured context. Named-row stays the default
  composed pass locally; quanta is the remote-link recommendation.
- [x] **P4 evidence tiering (mechanism)** — LANDED.
  `SqlProfilerOptions.Sampling : SamplingPolicy` (Core: default cap +
  per-kind pins; an explicit `None` pin exempts) replaced the global
  `MaxRowsPerKind`. Under any cap RowCount/NullCounts stay EXACT (the
  aggregate is never capped); sampled kinds keep the live capped
  discovery and are EXCLUDED from single-scan derivation (counted:
  `profile.live.sampled`); every downgrade is named
  (`SamplingDiagnostics.emit`, `profiler.evidence.sampled`, Info — the
  downgrade is operator-requested). Corpus: capping the 10 WID kinds at
  2,000 of 20,000 rows cut the live profile **5,183 → 1,027ms / 5,081 →
  794ms (-80–84%)** with exact counts asserted, 10 named downgrades, and
  `derived ∘ tiered ≡ all-live tiered` exactly. **Follow-on (named):**
  the `profiler.sampling` CONFIG surface — logical `{module, entity}`
  refs resolved via `CatalogResolution.tryKindByLogical`, `<1` caps
  refused by name, diagnostics threaded into the run report.
- [x] **P5 wire efficiency** — LANDED. `ReadSide.readRowsStreamCore`
  opens its reader `CommandBehavior.SequentialAccess` (the pull loop is
  strictly ordinal-ascending with a single visit per column — exactly
  the required access contract; formatter setup reads metadata only).
  Corpus verdict at 5–24-column widths: no regression, no measurable
  win (hydrate legs level with pre-P5 samples); the benefit scales with
  row width (wide nvarchar stops double-buffering) and every identity
  law held under it. Packet-size guidance for remote links: prefer
  `Packet Size=16384` (or 32767) in `PROJECTION_MSSQL_CONN_STR` /
  `model.ossys` conn specs for high-latency links — larger TDS packets
  cut round-trips on the drain's streaming reads; leave default on LAN.

## Results ledger (append rows; never overwrite)

| Date | Leg | ms | Notes |
|---|---|---|---|
| 2026-07-02 | seed | 274,669 | one-time; 240 tables / 480k rows |
| 2026-07-02 | hydrate serial (BEFORE) | 8,641 | incumbent collectInOrderFor |
| 2026-07-02 | hydrate concurrent-4 (BEFORE) | 18,746 | **INVERSION**: local CPU-bound drains contend on 4 cores; the knob wins only when the wire dominates (remote estates) — corpus finding #1 |
| 2026-07-02 | profile live-scan c4 (BEFORE) | 3,835 | full stream per kind, yet 2.3× cheaper per row than hydrate — the row-carrier tax measured (P3 thesis) — corpus finding #2 |
| 2026-07-02 | emit data lane (BEFORE) | 32,326 | 480k rows → 100.4MB SQL ≈ 3MB/s through ScriptDom; emit is the local dominator — corpus finding #3 |
| 2026-07-02 | hydrate serial (s2/s3) | 7,731 / 8,285 | quiet-host re-samples of the incumbent |
| 2026-07-02 | hydrate concurrent-4 (s2/s3) | 5,206 / 5,419 | c4 < serial on a quiet host — finding #1 (the "inversion") RETRACTED: the BEFORE c4 sample was rule-13 contaminated by concurrent host load |
| 2026-07-02 | profile live-scan c4 (s2/s3) | 4,515 / 3,696 | incumbent re-samples alongside the derived leg |
| 2026-07-02 | profile single-scan derived (P1, s2/s3) | 3,461 / 3,459 | identical cache vs live asserted BOTH runs (480k rows); 480 per-kind queries + 240 full streams → 1 global reflection; local CPU ≈ the scan it replaces — the win is the eliminated second estate pass (wire) |
| 2026-07-02 | emit data lane (s2/s3) | 28,243 / 23,308 | quiet-host re-samples; dominator verdict stands (~3.5–4.3MB/s) |
| 2026-07-02 | emit data lane (s4) | 35,707 | same-run comparator for the pipelined leg (host slower this run — cross-run absolute drift; within-run ratios govern) |
| 2026-07-02 | pipelined single pass (P2∘P1, s4) | 26,974 | drain+render+derive on 4 workers vs 44,554 two-phase sum (5,479 hydrate c4 + 3,368 derive + 35,707 emit) = **-39%**; also beats the serial emit alone — render parallelizes across drain workers; byte-identical scripts + identical cache asserted |
| 2026-07-02 | hydrate serial (s5, +SequentialAccess) | 8,141 | P5 flipped on the drain reader — level with pre-P5 samples (7.7–8.6k): no regression, no win at corpus row widths; identity laws all held under it |
| 2026-07-02 | **drain quanta serial (P3 probe, s5)** | **2,429** | the positional carrier WITHOUT the IR rebuild: vs 8,141 materialized = **3.35× carrier tax** — ~70% of hydrate wall-clock is per-row Map mint + row-identity synthesis, not wire. P3's scope decided by this number: quanta-fed render + evidence landed |
| 2026-07-02 | hydrate concurrent-4 (s5) | 5,289 | |
| 2026-07-02 | profile live-scan c4 / single-scan (s5) | 4,810 / 3,333 | identical cache asserted again (also under SequentialAccess) |
| 2026-07-02 | emit data lane (s5) | 23,636 | |
| 2026-07-02 | pipelined single pass (P2∘P1, s5) | 25,333 | vs 32,258 two-phase sum = **-21%** (magnitude tracks emit host speed; direction stable); identity laws held |
| 2026-07-02 | drain quanta serial (s6/s7) | 2,377 / 2,455 | durable-corpus re-samples; the 3.35× carrier-tax verdict stable |
| 2026-07-02 | pipelined single pass (s6/s7) | 26,075 / 25,680 | vs 33,960 / 34,412 two-phase sums (-23% / -25%) |
| 2026-07-02 | pipelined quanta pass (P3∘P2∘P1, s6/s7) | 28,180 / 27,644 | byte-identical scripts + identical cache BOTH samples; reproducibly +8% vs the named-row pass on this render-CPU-saturated host — the carrier win is drain-side/allocations, not composed wall-clock here; recommended for wire-dominated links |
| 2026-07-02 | profile tiered c4 (P4, s6/s7) | 1,027 / 794 | vs 5,183 / 5,081 full live scan = **-80% / -84%**; exact counts preserved under the cap; 10 named downgrades; derived∘tiered ≡ all-live tiered |
| 2026-07-02 | seed (durable reuse, s6/s7) | ~0 | `PERF_CORPUS_DURABLE=1` sentinel-verified reuse — the 4.5min seed paid once |
