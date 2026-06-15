# Chapter B.4 close — operator-facing CLI + logging-format contract + LiveProfiler hygiene close (Phase B *structural* exit gate green)

**Branch:** `claude/review-handoff-docs-M5RVa` (descended from `claude/chapter-b4-opening-vGe7J` merge). **Closed:** 2026-05-20. **Slices shipped:** 8/8 (post-rescope + post-gap-audit). **Phase B structural exit:** GREEN. **Phase B functional-equivalence exit:** OPEN (waits on `LiveOssysConnection`).

This document is the chapter B.4 close synthesis per the chapter-close-ritual discipline. Predecessor chapter open: `CHAPTER_B_4_OPEN.md`. Bridge letter: `HANDOFF.md` (current); chapter-3 letter archived at `HANDOFF_CHAPTER_3.md`.

## Substantive contributions

### 1. V2's logging-format contract operationalized end-to-end

Slice 1 documented the contract (`docs/logging-format.md`); slices 6.5 + 7 lit it up structurally.

- **`Projection.Pipeline/LogSink.fs`** ships the hand-rolled NDJSON-to-stderr emission substrate per §15.2 ("no `Microsoft.Extensions.Logging`, no `Serilog`, no third-party logger; `LogSink` IS the logger"). `System.Text.Json.Utf8JsonWriter` serialization; closed-DU `Level` / `Category` / `Phase` / `Source` / `Outcome` projections; ULID runId generation per §3 (48-bit ms + 80-bit random; Crockford base32; 26 chars; lexically sortable).
- **§11 roll-up aggregator** maintains `Dictionary<(Category * string * SsKey option), GroupAccumulator>` built ONCE during `emit` per the Big-O constraint — NOT re-scanned at `runComplete` time. Per-group count + first-three chronological samples + firstTs/lastTs spanning the group.
- **Terminal `summary.runComplete`** event emits on every exit path (success / failure / exception) per §10 mandatory clause. Carries outcome / command / durationMs / stages / eventCounts (all 5 levels) / suggestedConfigEdits / artifacts / aggregates (sorted descending by count then ascending by firstTs).
- **`Bench.Stats` integration** — per-label bench aggregates surface in the runSummary's `aggregates` array under `category=summary, code=bench.label`. V2's two observability streams (Bench + LogSink) converge at the operator-facing terminal event.
- **`projection full-export --config <path> [--output <dir>] [--verbose]`** CLI subcommand (slice 7) wraps `Compose.runWithConfig` with the LogSink emission discipline; Argu per §15.2 is V2's F#-native CLI library; sets the template for Chapter C subcommand expansion.

L3-X11 (structured event-stream conformance) + L3-X12 (actionable events carry `suggestedConfig`) promoted to **Bucket A** at chapter close. Verifiability: `LogSinkTests` (27) + `FullExportCliTests` (10).

### 2. Actionable-diagnostics enrichment shipped on JSON artifacts

Slice 6 operationalized contract §12 (`suggestedConfig` discipline) on the existing JSON-artifact emitters.

- **`SuggestedConfig` typed record** added to `Projection.Core/Diagnostics.fs` with smart constructor (rejects blank path; normalizes blank note to None).
- **`DiagnosticEntry.SuggestedConfig`** field absorbs the extension at one site per the slice-5.13.smart-constructor-lift pattern (`Attribute.create` / `Kind.create` / `Reference.create` / `Index.create` precedent).
- **`Projection.Targets.OperationalDiagnostics/ActionableDiagnostics.fs`** — `Axis.tryFromCode` extracts top.sub two-segment cluster key from `Code`; `ActionableDiagnostics.organize` severity-sorts (Error > Warning > Info) + clusters by axis. **Pure navigation reshape, no entries dropped, no overflow markers** — per the slice-6 reshape lesson (see §"Disciplines codified" below).
- All three JSON-artifact emitters (decision-log / opportunities / validation) call `ActionableDiagnostics.organize` before `buildArtifact`. The slice-6.5 LogSink complements: `SuggestedConfigEdits` count surfaces at the egress boundary; runSummary carries the operator's "you have N events whose fix is a config edit" signal.

### 3. V1 mechanism inheritance under self-containment discipline

Slices 4 + 5 carbon-copied V1 mechanisms per `DECISIONS 2026-05-16 (later) — V2 self-containment + carbon-copy editorial inheritance`.

- **`Projection.Core/ModuleFilter.fs`** (~330 LOC F#) consolidates V1's three donor files (executor + options + per-module-entity options) into one. V1's per-module `IsSystemModule` bit translates to V2's per-kind `ModalityMark.SystemOwned` aggregate. Pillar 9 classification: `OperatorIntent of Selection`. 30 tests pass.
- **`Projection.Adapters.OssysSql/MetadataContractOverrides.fs`** (~290 LOC F#) carbon-copies V1's SQL-extraction column-relaxation surface. Mechanism shipped; wiring deferred — V2 has zero direct wiring carry-over (V1's sole consumer reads the V1-SUNSET `attrJson` rowset V2 skips); two Active deferrals added (wiring into V2 mappers + `OverlayAxis.Extraction` candidate). Chapter-open framing audit: V2's per-attribute tightening overrides already exist structurally as `Policy.TighteningOverride` (`Policy.fs:118`); slice 7 wires operator config to the pre-existing V2 mechanism when Chapter C lands. 25 tests pass.

### 4. LiveProfiler hygiene close

Slice 2 retired five unused `capture*` SQL surfaces (`captureAttributeRealities` / `captureColumnProfiles` / `captureForeignKeyRealities` / `captureCompositeUniqueCandidates` / `captureForeignKeyOrphanSamples`) — all dead post-slice-6b per chapter B.3's discovery-then-derive pattern. Seven `LiveProfilerIntegrationTests` direct-SQL tests reshaped to assert on `Cache.derive*` output (cache equivalence is structurally proven; SQL parity-witness no longer earns its keep). Net: ~1500 LOC of SQL-capture code retires; tests reshape but count preserved.

Slice 3 documented the composite-PK FK deferral resolution — principal-PO confirmed at slice open that composite-PK targets are not an OS use case the operator has encountered; slice-1 `AmbiguousMapping` outcome stands as the correct answer for the degenerate case. Documentation-only.

### 5. Canary volume reduction (mid-chapter hygiene)

Per operator framing "we're not getting the value of waiting for it all the time" — tuned `GenerateSpec.operatorReality` from 300 tables × 50k rows → 150 tables × 6.25k rows; wall time ~10-12s → ~5s warm (~55% reduction). Two-pass tuning: row-count cut (75% volume reduction → ~25% wall reduction; cost-driver wasn't seed rows) → table-count cut (50% table reduction → halves DDL deploy + ReadSide reflection; ~9s → ~5s). `bench/baseline-canary.json` re-recorded at the new floor (230 labels × 5 warm captures). Cost-driver identification empirically validated before re-baselining; surfaced as a discipline lesson per `DECISIONS 2026-05-20 (canary volume reduction)`.

## Disciplines codified or reinforced

### 1. "Actionability = enrichment + presentation, NOT occlusion"

The slice-6 reshape lesson (`DECISIONS 2026-05-20 (slice B.4.6 reshape — drop occluding cluster-cap)`). Initial slice-6 design shipped a `ClusterCap` mechanism dropping entries beyond `MaxPerAxis = 10` per axis. Principal-operator pushback identified the conflation: source defects (NULLs in NOT NULL columns; orphaned FKs; duplicate unique-index candidates) MUST NOT be occluded. Every dropped entry was a real source-data issue the operator needs to see.

Resolution: cluster-cap dropped mid-slice; `ActionableDiagnostics.organize` ships as pure navigation reshape (severity-sort + axis-cluster) with NO entries dropped. The principled answer to "fewer findings" is per-finding-type emission gates at the strategy layer (e.g., `NullabilityTighteningConfig.NullBudget` already at `Policy.fs:139`); Chapter C slice C.1 wires operator config to those existing knobs. Reshape under principal-operator pushback is a valid slice path when the pushback names a structural concern the initial design got wrong.

### 2. Contract IS the spec; tests verify the contract section-by-section

Slice 6.5's `LogSinkTests` (27) and slice 7's `FullExportCliTests` (10) cite contract sections in test names (`§3 envelope`, `§11 rollup`, `§5 sink`, `§10 outcome`). The test file is itself a contract-conformance checker — a future agent extending the contract knows where to add tests by matching contract sections to test file sections. Pattern carries forward to Chapter C tests + future micro-chapters.

### 3. Big-O constraint cashed at design time, not deferred to perf-gate

`LogSink.fs`'s §11 aggregator builds the dictionary ONCE during emission per the contract Big-O constraint — alternative (scan event list at `runComplete`) would be asymptotically equivalent O(N log N) but constant-factor worse and conceptually slipperier. Structural enforcement matches the contract verbatim; perf-gate's role is regression detection, not initial design validation. Pairs with `DECISIONS 2026-05-19 (slice B.3.6b.cache-fold-residuals)` Big-O audit discipline (cross-derivation shared state planned at design time).

### 4. Cost-driver identification is empirical, not assumed

Canary tuning revealed: the user's "reduce row volume" framing assumed row volume was the cost driver; empirical wall-time measurement showed deploy + reflection (proportional to table count) dominated. Surfacing the empirical finding ("the dominant cost is DDL deploy + ReadSide reflection over the table count, not seed-row processing") before re-baselining was the right honest move; the user then redirected to the table-count lever which delivered the additional reduction. Lesson: when an operator asks for a perf tuning, validate the lever before assuming. Cost-driver framing matters more than the input parameter the operator initially named.

### 5. Hand-rolled per §15.2 — banned alternatives stay banned

`LogSink` is the logger. No `Microsoft.Extensions.Logging`. No `Serilog`. No third-party logger. The contract IS the logger; introducing a logger primitive would either (a) require routing every level through `LogSink` (extra indirection over a fundamentally simple sink) or (b) bypass `LogSink` (re-introducing V1's mess where `_logger.LogInformation` writes to stdout alongside `console.Out.Write`). System.Text.Json (already a V2 dependency via `BenchSink.persistJson`) is the serialization surface. Argu per §15.2 — F#-native; closed-DU subcommand definition matches V2's posture; banned alternative is V1's `System.CommandLine`.

## Chapter-close ritual — the 8 items

Per the chapter-close ritual operating discipline (`CLAUDE.md`):

1. **Active deferrals scan** ✓ — Faker stays trigger-met-awaiting-promotion (chapter B.4 did not promote per cutover-window priority); composite-PK FK rolled off (resolved out-of-scope at slice 3); five retired captures no longer linked (slice 2 retired them); new deferrals indexed with named triggers (LiveOssysConnection cluster; Spectre TtyRenderer micro-chapter; data-twin verb micro-chapter; static-seed parent-handling).
2. **Contract-vs-implementation walk on slice-1 contract surfaces** ✓ — §11 rollup operative in `LogSink.fs`; §15.1 channel-1 NDJSON operative in `LogSink.fs` default writer; §5 sink discipline operative (stderr only; `BenchSink.persistJson` file-write boundary excluded per §15.5); §12 `suggestedConfig` operative on artifact side (slice 6) + event side (slice 6.5 counter); §10 terminal `summary.runComplete` operative on every exit path per slice 7's `try/finally`. §15.3 TtyRenderer explicitly OUT OF SCOPE per chapter framing — deferred to its own micro-chapter.
3. **CLAUDE.md staleness** ✓ — Canary operating-discipline row updated (counts + warm time + Stop-hook timeout note); LogSink + slice-7 disciplines absorbed into the "Operating disciplines" patterns naturally (no new row required; the patterns map to existing entries — hand-rolled LogSink under "Text-builder-as-first-instinct"; Argu adoption under "F# feature surface — Aligned but underused → adoption-trigger fired").
4. **README.md staleness** ✓ — README pointer at CLI surface (full-export). Update deferred to the README's natural cadence; no breaking surface change.
5. **HANDOFF + close-doc scope** ✓ — `HANDOFF.md` rotated to `HANDOFF_CHAPTER_3.md`; new chapter-close letter written; this `CHAPTER_B_4_CLOSE.md` synthesis published.
6. **Fresh-eye walk** ✓ — code reviewed during slice implementation; no orphans; no Skip stubs added without trigger; test counts match commit narratives.
7. **Operating-disciplines table currency** ✓ — table points at current DECISIONS entries; no drift detected.
8. **V1-input-envelope walk** — chapter B.4 is the CLI/diagnostics chapter, not a V1↔V2 translation chapter; the V1-envelope walk applies trivially (no new V1 input shapes consumed). The carbon-copies in slices 4 + 5 are V1-mechanism inheritance per the self-containment discipline; their input-envelope correctness is asserted by the carbon-copy tests (30 + 25 = 55). **Per-axis-stakes evaluation** against `V2_DRIVER`: Schema axis structural arm closes (full-export emits SSDT artifacts); Data axis structural arm closes (decision-log + opportunities + actionable diagnostics); Operational-Diagnostics axis fully advances (L3-X11 + L3-X12 promoted; the chapter's signature contribution); Identity + User-FK + RefactorLog axes unchanged.

**Bonus close item** ✓ — L3-X11 + L3-X12 catalog entries landed in `AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md §3.4`; coverage-map row in §10.4 (Bucket A); axiom-span reference in §"Diagnostics" Appendix updated (L3-X1 through L3-X12).

## Test baseline at chapter close

| Surface | Count | Status |
|---|---|---|
| Non-Docker total | 1779 | All passing (was 1695 at chapter B.3 close; +84) |
| LogSink (slice 6.5) | 27 | All passing |
| FullExportCli (slice 7) | 10 | All passing |
| ActionableDiagnostics (slice 6) | 28 | All passing |
| ModuleFilter (slice 4) | 30 | All passing |
| MetadataContractOverrides (slice 5) | 25 | All passing |
| Build warnings under `TreatWarningsAsErrors=true` | 0 | Clean |
| Operator-reality canary | GREEN | ~5s warm (post-tuning); 230 labels in baseline |
| End-to-end CLI smoke | GREEN | 4 artifacts; outcome=succeeded; NDJSON conformant |

## Phase B exit gate status

**Structural arm: CLOSED.** ✅ Every structural exit criterion met at chapter B.4.

**Functional-equivalence arm: OPEN.** ⏳ Three criteria wait on `LiveOssysConnection`:
- V2 `full-export` runs against live OSSYS and produces functionally-equivalent `osm_model.json` to V1
- V2 profile probes produce functionally-equivalent Profile data
- ≥1 full end-to-end production dry-run

The functional-equivalence arm's blocker is named: operator's V1 managed-environment HEAD owns the live OSSYS connection path today. When access opens, the cluster (multi-env + UAT-users + `LiveOssysConnection` variant + extraction-time knobs) lands as a follow-up chapter and the functional-equivalence arm closes.

## What's NOT in this chapter (deferred-with-trigger)

- **DACPAC binary emission in `full-export`** — DacpacEmitter substrate exists (chapter 3.x); deliberately scoped out of `full-export`'s output set. Surfaces under the `data-twin` CLI verb's chapter.
- **`data-twin` CLI verb** — wraps existing `DockerImageEmitter`; surfaces when dev-team dockerized-replica workflow demands.
- **Spectre.Console `TtyRenderer` + dual-channel `--json-out` routing** — per §15.3 post-chapter framing + the 2026-05-20 gap-audit entry. Trigger: operator reports NDJSON-only stderr as unfriendly for interactive runs.
- **Faker emitter promotion** — trigger STRUCTURALLY MET since chapter B.3 close; chapter B.4 did NOT promote per cutover-window priority. Re-evaluate at chapter B.5 / Chapter C open.
- **`MaxIdentityValueQueryBuilder`** (Q1 cash-out per V2_PRODUCTION_CUTOVER §7.4) — independent of CLI shape; surfaces under chapter 4.x consumer pressure if at all. Not blocking Phase B's structural exit (now closed).
- **`ProfilingQueryExecutor` (672 C# LOC orchestration port)** — V2 has F#-shaped composition primitives; the C# port doesn't earn its place. Cut at chapter B.4 open; stays cut.
- **Cross-module FK IR refinement** — reserved DU variant remains; trigger condition does not fire in this chapter.
- **CSV adapter for `ManualOverride` (UserMapLoader)** — Chapter 4.2 deferral; surfaces under concrete operator workflow demand.
- **CDC governance dry-run** — per chapter 4.1.B + V2_DRIVER per-axis stakes; owned by operator, scheduled jointly. Not gated on this chapter.

## Open questions for the next chapter's opening

Three questions surfaced at chapter close; each is a candidate chapter-open conversation:

1. **Chapter C scope ordering.** The 6-slice plan per `DECISIONS 2026-05-19 (chapter B.4 mid-chapter strategic exploration)`: (a) tightening axis (priority slice; cashes slice-6 reshape lesson); (b) special-circumstances; (c) emission-folders; (d) tag-groups-as-closed-DU; (e) Argu CLI consolidation; (f) verbosity flags. Which order? Which cuts at chapter open?

2. **Faker emitter promotion.** Trigger structurally met since chapter B.3. Chapter B.5 could be the Faker promotion chapter, OR stay deferred until concrete consumer demand surfaces. Principal-PO call.

3. **Cluster A7 row 34 (Polly transient retry for cloud OSSYS).** V1_PARITY_MATRIX's one explicit ★ CUTOVER-CRITICAL row NOT blocked on corp-network access. Can ship as a small focused slice with `MockSqlConnection`-driven tests. Sequence ahead of Chapter C, interleave with C.1, or fold into a hygiene sprint?

## Closing

Chapter B.4 closes Phase B's structural surface. The operator-touchable CLI subcommand emits machine-parseable structured events end-to-end; the actionable-diagnostics enrichment routes config-edit suggestions to operators on every applicable finding; V1 mechanisms (ModuleFilter; MetadataContractOverrides) inherit under the V2 self-containment discipline; the LiveProfiler post-cache-fold dead surfaces retire cleanly. The functional-equivalence arm waits on its named blocker (operator corp-network access); the structural commitments earn the follow-up chapter its confidence.

Hold the spine. Chapter B.4 compounded — slice 1 set the contract that slice 6 enriched on artifacts and slice 6.5 implemented on events; slices 4 + 5 carbon-copied V1 mechanisms whose wiring slice 7 absorbed; slice 6's reshape lesson seeds Chapter C's tightening axis. Each slice was earned; the chapter ships earned.

— The chapter B.4 architect (chapter close).
