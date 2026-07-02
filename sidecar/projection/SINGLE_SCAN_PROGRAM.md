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

- [x] **P0 corpus + BEFORE** — harness landed; baselines below. Three
  findings already: local concurrency inversion (CPU-bound), the
  measured 2.3× row-carrier tax, and emit as the local dominator
  (~3MB/s ScriptDom) — the program's emphasis shifts accordingly:
  P3 and emit-CPU reduction rank ABOVE P2's overlap at local scale;
  P1 + concurrency remain the wire-dominated (remote) wins.
- [ ] **P1 single-scan evidence unification** — derive `EvidenceCache` from
  the hydrated rows: `CachedKind` built client-side from `StaticRow` cells
  (RawValueCodec raw-form contract), row/null counts counted during the
  walk, nullability from the landed batched reflection (one global query).
  Per-kind SQL budget: 2 queries + 1 stream → **0 queries + the stream the
  data lanes already paid for** (+1 global reflection per publish).
  Gate: `profiler.provider = live` AND the data lanes hydrate the kind
  (AllData bootstrap ∪ static grafts); kinds outside the hydrated set keep
  the live 3-query discovery (named, counted). Value-identity law:
  derived cache ≡ live-scan cache on the corpus (modulo the aggregate
  query's exact-count vs streamed-count equivalence, which must be exact).
- [ ] **P2 acquire→emit pipelining** — per-kind MERGE render fires as its
  drain lands (bounded producer/consumer, `emission.dataReadConcurrency`
  workers + render on drain completion); assembly stays topo-ordered;
  byte-identical artifact asserted.
- [ ] **P3 row-carrier slimming** — positional cells + per-kind header where
  the consumer allows; measured first via `readside.rowstream.materializeIr`
  vs `.materialize` split on the corpus; scope decided by that number.
- [ ] **P4 evidence tiering** — per-kind sampling policy (operator intent)
  over `MaxRowsPerKind`; config surface + named downgrade diagnostics.
- [ ] **P5 wire efficiency** — `CommandBehavior.SequentialAccess` on the
  drain reader (cell access is already strictly ordinal-ordered);
  packet-size guidance documented for remote links.

## Results ledger (append rows; never overwrite)

| Date | Leg | ms | Notes |
|---|---|---|---|
| 2026-07-02 | seed | 274,669 | one-time; 240 tables / 480k rows |
| 2026-07-02 | hydrate serial (BEFORE) | 8,641 | incumbent collectInOrderFor |
| 2026-07-02 | hydrate concurrent-4 (BEFORE) | 18,746 | **INVERSION**: local CPU-bound drains contend on 4 cores; the knob wins only when the wire dominates (remote estates) — corpus finding #1 |
| 2026-07-02 | profile live-scan c4 (BEFORE) | 3,835 | full stream per kind, yet 2.3× cheaper per row than hydrate — the row-carrier tax measured (P3 thesis) — corpus finding #2 |
| 2026-07-02 | emit data lane (BEFORE) | 32,326 | 480k rows → 100.4MB SQL ≈ 3MB/s through ScriptDom; emit is the local dominator — corpus finding #3 |
