# Handoff letter — 2026-05-20 (chapter B.4 CLOSES) — Phase B *structural* exit gate green; full-export CLI + LogSink + actionable diagnostics ship; functional-equivalence arm awaits LiveOssysConnection

---

## 📍 Next-agent orientation — DO THIS FIRST

> **You're picking up V2 after chapter B.4 closed.** All 8 slices shipped; the chapter-close ritual ran; Phase B's *structural* exit gate is green. The functional-equivalence arm waits on `LiveOssysConnection` (operator's V1 corporate-network HEAD; named blocker).
>
> **Read these, in this order (~30 min):**
>
> 1. **This letter** (the latest, at the top) — 5 min. Names what shipped + what's load-bearing + what's deferred + the chapter's open questions.
> 2. **`CHAPTER_B_4_CLOSE.md`** — 10 min. Primary close artifact. Substantive contributions + disciplines codified + chapter-close ritual findings.
> 3. **`CLAUDE.md` operating disciplines table** — 5 min. Two new entries from chapter B.4 (LogSink hand-rolled per §15.2; canary volume-reduction protocol).
> 4. **`DECISIONS Active deferrals — index`** at top of `DECISIONS.md` — 5 min. Faker stays trigger-met-awaiting-promotion; new deferrals (Spectre TtyRenderer micro-chapter; data-twin micro-chapter; LiveOssysConnection cluster) carry named triggers.
> 5. **`V2_DRIVER.md` per-axis stakes table** — 5 min. Confirm where chapter B.4 work landed (Operational-Diagnostics axis L3-X11 + L3-X12 promoted to Bucket A; Schema + Data axes' structural arm green; Identity axis unchanged).
>
> **Then orient on the next chapter** by reading the "Open questions for the next chapter's opening" section at the bottom of `CHAPTER_B_4_CLOSE.md`. Pick ONE to bring forward to the principal-PO as the chapter-open conversation. The natural sequencing is **Chapter C** (operator-config cash-out for the dormant Pipeline.Config sections; 6 slices per the strategic-exploration framing) — Phase B's structural arm is closed but Chapter C lights the operator-facing config surface that lets operators wire tightening, emission, insertion, user-matching axes.
>
> **Branch protocol.** Chapter B.4 worked on `claude/review-handoff-docs-M5RVa` (descended from `claude/chapter-b4-opening-vGe7J` merge). If opening Chapter C, the V2 convention is a new branch (`claude/chapter-c-<slug>`). Confirm with the principal-PO before opening — chapter-open decisions are theirs.

---

## Chapter B.4 summary (8 slices, all DONE)

| Slice | Cash-out | Status |
|---|---|---|
| 1 — `logging-format-contract` | `docs/logging-format.md` ships the V2 logging contract: §3 envelope, §4 levels, §5 sink discipline, §6 categories, §7 codes, §8 classifications, §9 payloads, §10 runSummary, §11 rollup, §12 suggestedConfig, §13 antipatterns, §14 sink, §15 CLI library recommendations. Proposes L3-X11 + L3-X12. | DONE |
| 2 — `capture-retirement` | Retires 5 `capture*` SQL surfaces from LiveProfiler (dead post-slice-6b); 7 tests reshape from SQL-direct to cache-derivation assertions. | DONE |
| 3 — `composite-pk-fk` | Resolved out-of-scope at slice open — principal-PO confirmed composite-PK targets aren't an OS use case; slice-1 `AmbiguousMapping` outcome stands. Documentation-only. | DONE |
| 4 — `module-filter-port` | `ModuleFilter` carbon-copied from V1 to `Projection.Core/ModuleFilter.fs`; ~330 LOC F# consolidating V1's three donor files; 30 tests pass. | DONE |
| 5 — `metadata-contract-overrides` | V1's `MetadataContractOverrides` SQL-extraction column-relaxation surface carbon-copied to `Projection.Adapters.OssysSql/MetadataContractOverrides.fs`; ~290 LOC F#; 25 tests pass. Mechanism shipped; wiring deferred to a follow-up chapter. | DONE |
| 6 — `actionable-diagnostics` | `SuggestedConfig` typed record + `DiagnosticEntry.SuggestedConfig` field; `ActionableDiagnostics.organize` severity-sort + axis-cluster (no occlusion); 28 tests pass. Reshape mid-slice dropped the initial cluster-cap design per principal-operator pushback — "actionability = enrichment + presentation, NOT occlusion." | DONE |
| 6.5 — `logsink-rollup` | `Projection.Pipeline/LogSink.fs` hand-rolled per §15.2; ULID runId; NDJSON envelope to stderr via `Utf8JsonWriter`; §11 roll-up aggregator built ONCE during emission; terminal `summary.runComplete` carrying aggregates + Bench.Stats; 27 property + example tests pass. | DONE |
| 7 — `full-export-cli` | `projection full-export --config <path> [--output <dir>] [--verbose]` CLI subcommand; Argu per §15.2; wraps `Compose.runWithConfig` + slice-6 actionable enrichment + slice-6.5 LogSink emission; 10 integration tests pass; end-to-end CLI smoke green. | DONE |

Plus one hygiene commit during the chapter: **canary volume reduction** (`GenerateSpec.operatorReality` 300 tables × 50k rows → 150 tables × 6.25k rows; wall ~10-12s → ~5s; baseline-canary.json re-recorded; per `DECISIONS 2026-05-20 (canary volume reduction)`).

## What's load-bearing after chapter B.4

**Operator-facing surface (the structural commitment):**
- `projection full-export --config <path>` is the operator-touchable CLI per `V2_PRODUCTION_CUTOVER §7.5`.
- NDJSON event stream to stderr per `docs/logging-format.md` §3-§15. Operators pipe `2>events.log` or `2>&1 | jq` and get a machine-parseable stream.
- `summary.runComplete` is the terminal event on every exit path — operator's scrollback target carrying outcome / stages / artifacts / eventCounts / suggestedConfigEdits / aggregates.

**Emission substrate:**
- `Projection.Pipeline/LogSink.fs` — hand-rolled per §15.2 (no `Microsoft.Extensions.Logging`, no `Serilog`); `System.Text.Json.Utf8JsonWriter` serialization; ULID runIds; lock-protected per-process state; §11 rollup built ONCE during emission per the Big-O constraint.
- `Projection.Core/Diagnostics.fs:SuggestedConfig` — typed actionable-payload primitive routed through every `DiagnosticEntry`.
- `Projection.Targets.OperationalDiagnostics/ActionableDiagnostics.fs` — severity-sort + axis-cluster pure-DataIntent enrichment over emit-bound diagnostics.

**V1 inheritance (carbon-copies under V2 self-containment discipline):**
- `Projection.Core/ModuleFilter.fs` — V1's three donor files consolidated; pillar 9 classification: `OperatorIntent of Selection`.
- `Projection.Adapters.OssysSql/MetadataContractOverrides.fs` — V1's column-relaxation surface; mechanism shipped; wiring lands when consumer demand surfaces.

**Verifiability triangle:**
- **L3-X11** (structured event-stream conformance) and **L3-X12** (actionable events carry suggestedConfig) promoted to Bucket A at chapter close. Tier 1 + Tier 2 respectively. Verifiability: `LogSinkTests` (27) + `FullExportCliTests` (10) + `ActionableDiagnosticsTests` (28).

## What's deferred (Active deferrals — chapter B.4 status)

Verify each at the next chapter close per the ritual. See `DECISIONS.md` Active deferrals index for the canonical list with trigger conditions.

- **Faker emitter promotion** — trigger STRUCTURALLY MET since chapter B.3 close. Chapter B.4 did NOT promote (cutover-window priority routed structural work to the CLI surface). Re-evaluate at chapter B.5 / Chapter C open under concrete consumer demand.
- **`LiveOssysConnection` variant + cluster** (multi-env + UAT-users + axis 10 user reflow + axis 14 extraction knobs) — trigger: operator's V1 corporate-network HEAD becomes accessible. Lights the functional-equivalence arm of Phase B's exit gate.
- **Standalone `projection extract` + `projection profile` subcommands** — dropped at chapter-mid rescope; reserved code paths in §6 event categories still fire from `full-export`'s orchestration. Re-open if operator workflow demands them.
- **`data-twin` CLI verb micro-chapter** — wraps existing `DockerImageEmitter` (chapter 3.x); surfaces when dev-team dockerized-replica workflow demands.
- **Spectre.Console `TtyRenderer` + dual-channel `--json-out` routing micro-chapter** — per §15.3. Trigger: operator reports NDJSON-only stderr as unfriendly for interactive runs.
- **Static-seed parent-handling behavior** — dispersed from the struck `DynamicDataSection`; surfaces under concrete operator demand as an emitter `Options` parameter.
- **CSV adapter for `ManualOverride` (`UserMapLoader`)** — Chapter 4.2 deferral; surfaces under concrete operator workflow demand.
- **Cluster A7 row 34** (Polly transient retry for cloud OSSYS) — ★ CUTOVER-CRITICAL per V1_PARITY_MATRIX. Not blocked on corp-network access; can ship independently with `MockSqlConnection`-driven tests. Recommended candidate for an early Chapter C slice OR a separate hygiene sprint.

## Phase B exit gate — gate status after chapter B.4 close

Per `V2_PRODUCTION_CUTOVER §8.2`, Phase B's exit criterion has TWO arms; chapter B.4 deliberately closes only the **structural** arm:

| Exit criterion | Status post-chapter-B.4 |
|---|---|
| L3 catalog reaches target bucket | ✅ DONE — L3-X11 + L3-X12 promoted to Bucket A |
| V2 `full-export` CLI subcommand exists | ✅ DONE — slice 7 |
| V2 emits SSDT + JSON + Distributions + actionable diagnostics from config | ✅ DONE — slice 7 (via slice 6 actionable enrichment) |
| V2 logging-format contract documented | ✅ DONE — slice 1 |
| V2 emits conforming events end-to-end | ✅ DONE — slice 7 (consuming slice 6.5 LogSink) |
| **Structural arm CLOSED.** | **✅ at chapter B.4.** |
| | |
| V2 `full-export` runs against live OSSYS and produces functionally-equivalent `osm_model.json` to V1 | ⏳ WAITS on `LiveOssysConnection` |
| V2 profile probes produce functionally-equivalent Profile data | ⏳ WAITS on `LiveOssysConnection` |
| ≥1 full end-to-end production dry-run | ⏳ WAITS on operator scheduling |
| **Functional-equivalence arm OPEN.** | **⏳ waits on corp-network access path.** |

## Test baseline at chapter close

- **1779/1779 non-Docker** passing (was 1695 at chapter B.3 close; +84 from chapter B.4 work: ModuleFilter 30 + MetadataContractOverrides 25 + ActionableDiagnostics 28 + Catalog smart-constructor lift adjustments + LogSinkTests 27 + FullExportCliTests 10 — minus reshapes).
- **0 build warnings** under `TreatWarningsAsErrors=true`.
- **Operator-reality canary GREEN** at the new tuned floor (150 tables × 6.25k rows; ~5s warm; 230 labels in `bench/baseline-canary.json`).
- **End-to-end CLI smoke green** — `dotnet projection.dll full-export --config /tmp/fe-config.json` produces 4 artifacts; NDJSON envelopes parse cleanly; `summary.runComplete` carries outcome=succeeded + 74 aggregate entries.

## Best practices the chapter taught (carry forward)

- **Contract IS the spec; tests verify the contract.** Every property test in `LogSinkTests` cites a contract section (`§3 envelope`, `§11 rollup`, `§5 sink`). The test file is itself a contract-conformance checker — a future agent extending the contract adds tests by matching contract sections to test file sections.
- **"Actionability" means enrichment + presentation, NOT occlusion.** The slice-6 reshape lesson per `DECISIONS 2026-05-20 (slice B.4.6 reshape)`: source defects (NULLs in NOT NULL columns; orphaned FKs; duplicate unique candidates) are first-class signal; the diagnostic-emit layer surfaces them faithfully without curated suppression. Per-finding-type emission gates live at the strategy layer (e.g., `NullabilityTighteningConfig.NullBudget`), not at the emit boundary. Chapter C slice C.1 wires operator config to those existing knobs.
- **Big-O constraint cashed at design time.** Slice 6.5's §11 aggregator builds the dictionary ONCE during emission per the contract Big-O constraint — alternative (scan event list at runComplete) would be asymptotically equivalent but constant-factor worse and conceptually slipperier. Structural enforcement matches the contract verbatim.
- **Cost-driver identification is empirical, not assumed.** The canary volume-reduction tuning showed the operator's "reduce row volume by 3/4" framing assumed the wrong cost driver — empirical wall-time measurement showed deploy + reflection (proportional to table count) dominated. Surfacing the empirical finding before re-baselining was the right honest move; the user then redirected to the table-count lever which delivered the additional reduction.
- **Argu per §15.2 sets the F# CLI pattern for Chapter C.** `FullExportArg` closed-DU is the template Chapter C extends with additional subcommands. The existing `emit` / `deploy` / `canary` subcommands stay on raw argv during the transition (no breakage; no Chapter C scope creep into chapter B.4).

## Open questions for the next chapter's opening

Three open questions surfaced at chapter B.4 close; each is a candidate chapter-open conversation:

1. **Chapter C scope** — the 6-slice plan per `DECISIONS 2026-05-19 (chapter B.4 mid-chapter strategic exploration)` wires operator config to (a) tightening axis (C.1 priority slice; cashes the slice-6 reshape lesson via `Policy.TighteningPolicy` + `TighteningOverride`); (b) special-circumstances axis; (c) emission-folders axis; (d) tag-groups-as-closed-DU axis; (e) Argu CLI consolidation (migrate existing emit/deploy/canary to Argu); (f) verbosity flags (`--verbose` / `--debug` per §4). Which order? Which cuts at chapter open per chapter-mid rescope precedent?

2. **Faker emitter promotion** — trigger structurally met since chapter B.3. Cutover-window priority routed chapter B.4 to CLI; chapter B.5 could be the Faker promotion chapter. Or stay deferred until concrete consumer demand surfaces. Principal-PO call.

3. **Cluster A7 row 34 (Polly transient retry)** — the V1_PARITY_MATRIX's one explicit ★ CUTOVER-CRITICAL row that's NOT blocked on corp-network access. Can ship as a small focused slice with `MockSqlConnection`-driven tests. Sequence ahead of Chapter C, or interleave with C.1?

## Reading order for the next agent

1. **This letter** (~5 minutes).
2. **`CHAPTER_B_4_CLOSE.md`** — chapter synthesis + disciplines codified.
3. **`CHAPTER_B_4_OPEN.md`** — the 8-slice plan; all marked DONE; chapter-close-ritual paragraph at the bottom names the 8 items the close ran.
4. **`DECISIONS 2026-05-20 (slice B.4.7 — full-export CLI)`** + the slice-6.5 + canary-tuning entries — substantive contributions.
5. **`docs/logging-format.md`** — the contract; now operative end-to-end.
6. **`V1_PARITY_MATRIX.md` Cluster A7 row 34** — the cutover-critical row not blocked on corp-network.
7. **`AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md §3.4`** — L3-X11 + L3-X12 newly promoted; §10.4 coverage matrix updated.

Chapter B.4 is the substantive contribution: V2 has an operator-touchable CLI subcommand emitting machine-parseable structured events end-to-end; the actionable-diagnostics enrichment routes config-edit suggestions to operators on every applicable finding; the structural arm of Phase B's exit gate closed on schedule; the functional-equivalence arm waits on its named blocker.

Hold the spine. The chapter compounded: slice 1 set the contract; slices 4 + 5 ported V1 mechanisms; slice 6 enriched artifacts; slice 6.5 built the emission substrate; slice 7 wove them into the CLI. Each slice was earned.

— The chapter B.4 architect (chapter close).
