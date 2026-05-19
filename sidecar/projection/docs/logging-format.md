# V2 Logging-Format Contract

**Slice:** B.4.1 (chapter B.4 opening; axiom-first). **Status:** structural promotion in flight (L3 axiom statement proposed in §17; final placement at chapter close). **Predecessor:** chapter B.3 close letter; chapter B.4 open at `CHAPTER_B_4_OPEN.md`. **Consumers in this chapter (post-rescope; see `DECISIONS 2026-05-19 (slice B.4.{4-7}.rescope)`):** slice 6 (`actionable-diagnostics` — operationalizes §12's `suggestedConfig` discipline on the JSON-artifact emitters) + slice 7 (`full-export` CLI subcommand — only operator-facing CLI surface in this chapter; standalone `extract` + `profile` subcommands dropped at chapter-mid). The CLI's emitted event stream + the JSON artifacts' actionable-payload discipline MUST conform or the chapter close gate fails.

This document defines the operator-visible event stream V2 emits during every CLI run. It is the structural surface every CLI subcommand projects onto when it writes to the operator's terminal. The contract is single-sink, single-format, single-vocabulary — the inverse of V1's three-library mess. The contract names the **envelope**, the **categories**, the **levels**, the **classification taxonomies** (lifted verbatim from V1 where V1 had the right idea but smeared the shape across prose), the **terminal `runSummary` event** that closes every run, the **roll-up collapse algorithm** the runSummary uses, and the **antipatterns banned by construction**. The closing sections name next-step extension cues for the TransformRegistry, the proposed L3 axiom that codifies the contract, and the open questions deferred from this slice.

The contract is informed by a targeted survey of V1's logging surface (`Osm.Cli/Commands/CommandConsole.cs` and the ~55 `_logger.Log*` sites across `Osm.Pipeline/`). The survey is summarized inline where it informs a contract decision; the full audit notes live in the chapter B.4 open doc and the slice's conversation history.

## §1 Scope and frame

What this contract specifies:
- The **wire format** of operator-visible events written to stderr by every V2 CLI subcommand.
- The **envelope** (mandatory per-event shape) and the **levels / categories / codes** taxonomy.
- The **classification taxonomies** (`TighteningRationale`, `ProbeOutcome`, `TransformSource`, `OverlayAxis`) every event carries when the event names an axis where an operator could act.
- The **terminal `runSummary` event** that every successful run MUST emit, and the **roll-up collapse algorithm** that produces its payload from per-event accumulators.
- The **antipatterns banned by construction** — patterns V1 has that this contract refuses.

What this contract does NOT specify:
- The internal F# writer surface. `Projection.Core` already carries `Lineage`, `Diagnostics`, and `Bench` as typed writers; this contract is the **serialization** of those writers, not a replacement. Internal pass authors continue to write to the existing writers; the CLI boundary projects them through the envelope defined here.
- The artifact files V2 writes to disk (`osm_model.json`, the decision log JSON, the opportunities report JSON, the run manifest). Those are **artifacts**, not the event stream. Events about an artifact (e.g., "decision log written to `out/decision-log.json`") are in scope; the artifact's internal schema is not.
- The receiver side (jq filters, log shippers, dashboards). The contract names what V2 emits; downstream consumption is operator-paced.

## §2 Domain insights — V1's mess + V2's existing substrate

### 2.1 The V1 surface, in one sentence

V1 fires **three logging libraries at the same TTY undifferentiated** — `Spectre.Console` for progress bars (`SpectreConsoleProgressService.cs:7`), `Microsoft.Extensions.Logging` via `AddSimpleConsole()` registered at `Program.cs:18` for `ILogger<T>`-style diagnostics across `Osm.Pipeline/*`, and `System.CommandLine.IConsole` for the bulk of operator-facing report text wrapped by `CommandConsole.WriteLine` (`CommandConsole.cs:594-612`). The one structured surface V1 has — `PipelineExecutionLog` / `PipelineLogEntry` (`Osm.Pipeline/Orchestration/PipelineExecutionLog.cs:8-71`) — carries `(TimestampUtc, Step, Message, Metadata: IReadOnlyDictionary<string,string?>)` records, but the structure is destroyed at egress when `EmitPipelineLog.FormatLogEntry` (`CommandConsole.cs:2092-2099`) flattens the metadata dictionary into a trailing `" | key=value, key=value"` string. **Structure exists in memory; it is destroyed at the console boundary.** This is the central failure shape V2 inverts.

### 2.2 The V1 surface, in one diagram

```
┌──────────────────────────────────────────────────────────────────┐
│ V1 pipeline (Osm.Pipeline/*)                                     │
│   ├── log.Record(...) with typed Metadata  ◄── 58 call sites    │
│   ├── _logger.LogInformation/Warning/Error  ◄── ~55 call sites   │
│   └── progress.Increment/Description       ◄── ~12 call sites    │
└──────────────────────────────────────────────────────────────────┘
                       │            │           │
                       ▼            ▼           ▼
            ┌──────────────┐  ┌──────────┐  ┌──────────────┐
            │ PipelineExec │  │ ILogger  │  │ Spectre      │
            │ utionLog     │  │ (Simple  │  │ AnsiConsole  │
            │ (structured) │  │ Console) │  │ .Progress()  │
            └──────────────┘  └──────────┘  └──────────────┘
                       │            │           │
                       ▼            ▼           ▼
                 ┌─ CommandConsole.WriteLine / WriteErrorLine ─┐
                 │  (free-text, emojis, ANSI, section borders) │
                 └────────────────────────────────────────────┘
                                       │
                                       ▼
                              stdout + stderr
                              (undifferentiated)
```

Three independent streams write to two undifferentiated sinks. When piped to a file, you get Spectre ANSI prefix garbage interleaved with logger-formatted lines and naked `WriteLine` strings. Operators cannot `grep`, `jq`, or filter by category — every consumer has to parse prose.

### 2.3 What V1 already has right (lift verbatim)

V1 *almost* solves four problems. The shapes are correct; the sink is wrong. V2 lifts each verbatim:

1. **`TighteningRationales` constants** (`Osm.Validation.Tightening`, consumed at `CommandConsole.cs:2399-2412`): `DataHasNulls`, `NullBudgetEpsilon`, `RemediateBeforeTighten`, `ProfileMissing`, `UniqueDuplicatesPresent`, `CompositeUniqueDuplicatesPresent`, `UniquePolicyDisabled`, `DataHasOrphans`, `DeleteRuleIgnore`, `CrossSchema`, `CrossCatalog`, `ForeignKeyCreationDisabled`, `ForeignKeyNoCheckRecommended`. **This is the closed-DU taxonomy pillar 9's `OperatorIntent.Tightening` classification has been waiting for.** Every tightening-axis decision V2 emits names one of these as its `rationale` payload property.

2. **`ProfilingProbeOutcome` enum** (`CommandConsole.cs:1247-1260`): `Succeeded | TrustedConstraint | FallbackTimeout | Cancelled | AmbiguousMapping | Unknown`. **Two of these are first-class actionable signals**: `FallbackTimeout` ("operator should raise sampling budget") and `AmbiguousMapping` ("operator should disambiguate"). They become enum-typed payload properties on every `profile.probe.*` event.

3. **`ToggleExportValue.Source`** field (`Configuration | Override | Default`), surfaced by `EmitTogglePrecedence` (`CommandConsole.cs:2018-2039`). **This is pillar 9 in embryonic form** — V1 tracks which knobs were operator-supplied vs. defaulted, but only for tightening toggles. V2 generalizes: every `transform.registered` event carries a `source` envelope property identifying provenance.

4. **The dedup-collapse algorithm in `EmitPipelineLog`** (`CommandConsole.cs:2049-2089`): V1 already groups N occurrences of the same `(Step, Message)` into "N occurrence(s) between {first} and {last}" with three sample metadatas. **This is V1's single best roll-up surface.** V2 lifts the algorithm into the `runSummary` event's `aggregates` payload property (see §11).

### 2.4 V2's existing substrate (the writers we serialize)

V2 does not need new writers. The contract is the **serialization** of three writers V2 already carries:

| Writer | Type | Where defined | Role |
|---|---|---|---|
| `Lineage<'a>` | `Projection.Core.Lineage` (`Lineage.fs`) | Carries per-transform `LineageEvent` records with typed `TransformKind` payload (`Touched`, `Renamed`, `Created`, `Removed of RemovalReason`, `Annotated of AnnotationDetail`, `PhysicallyRenamed of PhysicalRename`) | Every pass's decision history; A24 chronological-under-bind discipline |
| `Diagnostics<'a>` | `Projection.Core.Diagnostics` (`Diagnostics.fs`) | Carries `DiagnosticEntry { Source; Severity (Info/Warning/Error); Code; Message; SsKey option; Metadata: Map<string,string> }` records | Observer-relevant findings beyond pure decisions |
| `Bench.Run` / `Bench.Stats` | `Projection.Core.Bench` (`Bench.fs`); persisted via `Projection.Pipeline.BenchSink` (`BenchSink.fs:46`) | Carries per-label `(Count, MeanMs, P50, P95, P99, TotalMs, StdevMs)` aggregates | Iterator-logging perf surface; per-iteration distribution |

A fourth writer is planned but not yet shipped: `TransformRegistry` (per pillar 9 + L3-CC-Transform-Totality + A41 candidate; full-sweep retroactive refactor at A.4.7 per `V2_PRODUCTION_CUTOVER.md §6.4.7`). When it lands, the contract's envelope already accommodates its events (see §18 next steps).

**The contract specifies how all three (and eventually four) writers project onto a single unified envelope at the CLI boundary.** Internal pass code continues to write to the existing typed writers; the boundary is where serialization happens, where the envelope is applied, and where the event reaches the operator.

## §3 The envelope — mandatory per-event shape

Every event V2 emits to the operator stream conforms to this envelope. The envelope is the wire format; F# producers are NOT required to construct envelope records themselves (they write to `Lineage` / `Diagnostics` / `Bench` as today). The envelope is applied at the serialization boundary.

```jsonc
{
  "runId": "01HXJVZK4F6Y2D9CPRN7M2X8AT",    // ULID; one per CLI invocation; mandatory
  "ts": "2026-05-19T14:23:07.482Z",          // RFC 3339; UTC; millisecond precision; mandatory
  "level": "info",                            // trace | debug | info | warn | error; mandatory
  "category": "profile",                      // see §6; mandatory
  "code": "profile.probe.fallbackTimeout",    // dot-separated stable identifier; mandatory; greppable
  "phase": "progress",                        // start | progress | end | error; mandatory
  "source": "operator",                       // operator | configuration | default | derived; optional (mandatory on transform.* codes)
  "ssKey": "OSUSR_FOO.dbo.OrderHeader",       // canonical SsKey identity; optional; present when event names a node
  "stepId": "live-profile.attribute-nulls",   // stable step identifier; optional; correlates progress / end pairs
  "payload": { ... }                          // category-specific structured properties; see §9; mandatory
}
```

**Discipline.**
- One event = one line. UTF-8, line-delimited JSON (`application/x-ndjson`). No multi-line payloads; no pretty-printing on the stream. Pretty-printing is a downstream concern (the contract's job is to be machine-parseable; a human-readable pretty form is `jq -c`).
- `runId` is constructed at CLI entry. ULIDs are chosen over UUIDs because ULIDs are lexically sortable by emit time — `ls -1 bench/<tag>/ | sort` and `grep runId` give the same chronological ordering. ULID format: `[0-9A-HJKMNP-TV-Z]{26}` (26 chars; Crockford base32; no I, L, O, U).
- `ts` is `DateTime.UtcNow` captured at the envelope boundary (per the `BenchSink.persistJson:54` precedent — wall-clock is reified at the file-sink layer, not in Core). Strict RFC 3339 with millisecond precision and trailing `Z`.
- `category` is closed-DU enumerated in §6. Every event names exactly one category.
- `code` is dot-separated. The contract's stable identifiers live in §7. Top-prefix matches `category` (e.g., `profile.*` codes only appear under `category=profile`).
- `phase` correlates pairs: a `start` event with `stepId=X` is closed by a matching `end` (or `error`) event with the same `stepId`. `progress` events name interim ticks. The CLI surface uses this to drive runSummary stage timings without library-internal timing.
- `source` is the V1 `ToggleExportValue.Source` generalization (lifted from `CommandConsole.cs:2018-2039`). MANDATORY on `transform.*` codes; optional elsewhere. Values: `operator` (operator-supplied), `configuration` (config-file resolved), `default` (V2's default), `derived` (computed from another source).
- `ssKey` is the canonical V2 identity (`Original of string | Derived of original × reason` per A1 + A5). Present when the event names a specific catalog node. The contract's serialization renders SsKey as its canonical string form (matching `LineageEvent.SsKey` rendering across existing emitters).
- `stepId` is OPTIONAL but MANDATORY when `phase ∈ {start, progress, end, error}`. The contract reserves no other meaning for `stepId`; it is purely correlation tooling.
- `payload` is the per-category structured surface (§9). No event omits `payload`; events with no structured detail emit `payload: {}`.

**Envelope properties NOT in the contract** (deliberately omitted, with rationale):
- **`traceId` / `spanId` (W3C trace context)**. Not in scope for slice 1. V2's pipeline is single-process synchronous; distributed-trace propagation has no consumer today. Re-open at chapter 5+ if a hosted-V2 surface materializes.
- **`hostName` / `userName` / `processId`**. Not in scope. Multi-tenant operator context is not V2's design point.
- **`commit` / `version`**. Reserved: a single `config.runStart` event (§9.5) carries V2's build identity once per run; per-event repetition is noise.

## §4 Levels

Closed five-way enum. `Trace` and `Debug` are NOT emitted by default; they require explicit operator opt-in (`--verbose` / `--debug` flags at the CLI surface, slice 7).

| Level | When it fires | Default visibility |
|---|---|---|
| `trace` | Function-entry / exit for high-frequency loops; iteration-level bench samples. **Not for default operator use.** | hidden unless `--debug` |
| `debug` | Per-decision detail beyond what `info` carries (e.g., per-column null-fraction). | hidden unless `--debug` |
| `info` | Default operator-facing narration. Progress events, structural decisions, run start / end. | visible |
| `warn` | Actionable findings the operator should review but that do not block the run (e.g., FK orphan detected; sampling fallback triggered; attribute marked Mandatory has nulls). | visible |
| `error` | Run-aborting failures (invariant violation; unreachable config; SQL connection failure). The run terminates after emitting; `runSummary` still fires with `outcome: "failed"`. | visible |

**No `fatal` level.** F# Result discipline + structural-commitment-via-construction-validation means "fatal" is a `Result.Error` propagated to the CLI boundary, which emits a single `error`-level event and exits non-zero. There is no third tier beyond `error`.

## §5 Sink discipline

V2 emits ALL events to **stderr**. stdout is reserved for:
1. Artifact data when the operator pipes (e.g., a future `projection extract --to -` would write osm_model.json to stdout; in chapter B.4 post-rescope the only CLI subcommand is `full-export` writing to a config-supplied output directory, but the channel discipline holds for any future stdout-writing subcommand).
2. Nothing else.

**Rationale.** Operators pipe stdout. If events go to stdout, `extract | jq` mixes events with the artifact data — a parser cannot recover. Events go to stderr; data goes to stdout; the operator's shell handles `2>events.log` separately from `> model.json`.

Concretely, V1 violates this: `CommandConsole.WriteLine` writes to `console.Out` (stdout) at `CommandConsole.cs:601`, mixing operator narration with everything stdout-piped consumers see. V2 inverts: the structured event channel is stderr; stdout is for data.

**No file sinks in the contract.** Operators redirect stderr to a file with `2>` if they want a log file. The contract does NOT define a per-run file path, a rotation policy, or a sink configuration surface — those are operator-paced concerns owned by the shell, not V2.

**`Bench.Run` JSON file persistence** (`BenchSink.persistJson`, `BenchSink.fs:46`) IS in scope as a separate artifact, not as part of the event stream. The CLI surface emits a `summary.benchPersisted` event (§7) naming the path it wrote; the file itself is the artifact, accessed by path. This preserves the chapter 3.6 cash-out structure (Bench at the boundary, not in Core).

## §6 Categories

Closed enum. Eight values, named after the V2 surface that produces the event. Top-prefix of every `code` matches its `category`.

| Category | Producer surface | Examples |
|---|---|---|
| `config` | CLI entry; `Pipeline.Config` resolution | `config.runStart`, `config.connectionResolved`, `config.validationFailed` |
| `extract` | OSSYS catalog read (`Projection.Adapters.Osm.CatalogReader`) | `extract.module.parsed`, `extract.attribute.dropped`, `extract.completed` |
| `profile` | Live SQL probing (`Projection.Adapters.Sql.LiveProfiler`) | `profile.probe.succeeded`, `profile.probe.fallbackTimeout`, `profile.cache.populated` |
| `transform` | Pass execution; OperatorIntent + DataIntent surfaces (pillar 9 + L3-CC-Transform-Totality) | `transform.registered`, `transform.applied`, `transform.declined` |
| `emit` | Sibling-Π emitters (`Projection.Targets.{SSDT,Json,Distributions,Data,OperationalDiagnostics}`) | `emit.ssdt.statementProduced`, `emit.json.completed` |
| `deploy` | `Projection.Pipeline.Deploy.executeStream` (SqlBulkCopy + per-row INSERT realizations) | `deploy.batchSent`, `deploy.bulkCopy.completed` |
| `canary` | `CanaryDeployTests` orchestration; PhysicalSchema diff | `canary.diffEmpty`, `canary.toleranceMatched`, `canary.divergence` |
| `summary` | Terminal `runSummary` event; intermediate stage `*.completed` events | `summary.runComplete`, `summary.stageCompleted`, `summary.benchPersisted` |

A ninth category (`progress`) was considered and rejected: progress IS phase-bearing on every category (a `profile.cache.populated` event with `phase=progress` is the progress signal). A flat `progress` category would force every emitter to choose between its domain category and the cross-cutting progress tag; the contract instead lifts `phase` into the envelope (§3).

## §7 Codes — canonical taxonomy

The code surface is the operator's grep target. Every code is dot-separated, top-prefixed by category, and **stable across V2 versions** (codes are part of the contract; renames require a DECISIONS entry naming the prior code as deprecated). The taxonomy below is the slice 1 baseline; slice 7 (full-export CLI) may extend it under the discipline "additive only; renames require an entry."

### 7.1 `config.*` codes

| Code | Level | Phase | When |
|---|---|---|---|
| `config.runStart` | `info` | `start` | First event of every run. Payload carries `version`, `command`, `configPath`, resolved `OverlayAxes` enabled. |
| `config.connectionResolved` | `info` | `start` | Connection source resolved per D9. Payload carries `kind` (`SnapshotJson` / `SnapshotRowsets` / `LiveOssysConnection`) and connection identity with secrets redacted. |
| `config.validationFailed` | `error` | `error` | Config parse / validate fails. Payload carries `(path, reason, suggestion)` per L3-X9. Run aborts after this event + the `summary.runComplete` event. |
| `config.toggleResolved` | `info` | `start` | One event per resolved tightening toggle (lifted from V1's `EmitTogglePrecedence`). Payload carries `toggle`, `value`, `source`. |

### 7.2 `extract.*` codes

| Code | Level | Phase | When |
|---|---|---|---|
| `extract.started` | `info` | `start` | OSSYS catalog read begins. |
| `extract.module.parsed` | `debug` | `progress` | Per-module parse event. Payload carries `module`, `entityCount`. |
| `extract.attribute.dropped` | `warn` | `progress` | An attribute was filtered out (per `ModuleFilter` or `MetadataContractOverrides`). Payload carries `ssKey`, `reason`, `source=operator`. |
| `extract.warning` | `warn` | `progress` | Adapter-level warning (e.g., metadata field present in V1 input but not in V2 model — L3-X7 silent-V1-drops surface). Payload carries `code` (V1's internal warning code), `message`, `ssKey`. |
| `extract.completed` | `info` | `end` | Catalog read complete. Payload carries `moduleCount`, `entityCount`, `attributeCount`, `outputPath`. |

### 7.3 `profile.*` codes

| Code | Level | Phase | When |
|---|---|---|---|
| `profile.started` | `info` | `start` | Live SQL profiling begins. Payload carries `kindCount` (number of tables to probe). |
| `profile.cache.populated` | `info` | `progress` | EvidenceCache populated for a kind. Payload carries `ssKey`, `rowCount`, `columnCount`. Discovery-then-derive pattern per chapter B.3. |
| `profile.probe.succeeded` | `debug` | `progress` | A probe derived a Profile axis. Payload carries `ssKey`, `axis`, `outcome=Succeeded`. |
| `profile.probe.fallbackTimeout` | `warn` | `progress` | Probe sampling cap engaged. Payload carries `ssKey`, `axis`, `outcome=FallbackTimeout`, `sampledRows`, `suggestedConfig: { "samplingCapRows": <recommended> }`. **Actionable.** |
| `profile.probe.cancelled` | `warn` | `progress` | Probe cancelled (rare; explicit operator interrupt or timeout). Payload carries `ssKey`, `axis`, `outcome=Cancelled`. |
| `profile.probe.ambiguousMapping` | `warn` | `progress` | FK reality probe found a composite-PK target it cannot disambiguate. Payload carries `ssKey`, `axis=foreignKeyReality`, `outcome=AmbiguousMapping`, `suggestedConfig` naming the override shape. **Actionable.** |
| `profile.completed` | `info` | `end` | Profiling complete. Payload carries per-axis derivation counts. |

### 7.4 `transform.*` codes

| Code | Level | Phase | When |
|---|---|---|---|
| `transform.registered` | `debug` | `start` | One event per registered transform at run start (TransformRegistry totality surface; pillar 9). Payload carries `transformId`, `stage` (`Adapter`/`Pass`/`OrderingPolicy`/`Emitter`/`Pipeline`), `intent` (`DataIntent` / `OperatorIntent`), `overlayAxis` (when `OperatorIntent`), `source`. |
| `transform.applied` | `info` | `progress` | A registered transform fired against a node. Payload carries `transformId`, `ssKey`, `decision`, plus the typed outcome payload (e.g., `NullabilityOutcome.toDiagnosticString` rendering). |
| `transform.declined` | `info` | `progress` | A registered transform did NOT fire (named keep-reason; total decisions + named skips discipline). Payload carries `transformId`, `ssKey`, `rationale` (closed-DU enum; see §8.1). |
| `transform.lineage` | `debug` | `progress` | Per-`LineageEvent` projection. Payload carries `passName`, `ssKey`, `transformKind`, typed payload (`removalReason` / `annotationDetail` / `physicalRename`). |
| `transform.diagnostic` | (varies) | `progress` | Per-`DiagnosticEntry` projection. Level matches `DiagnosticEntry.Severity` (Info → `info`, Warning → `warn`, Error → `error`). Payload carries `code`, `message`, `ssKey`, `metadata`. |

### 7.5 `emit.*` codes

| Code | Level | Phase | When |
|---|---|---|---|
| `emit.<target>.started` | `info` | `start` | Per-target start. `<target>` ∈ `ssdt` / `json` / `distributions` / `data` / `operationalDiagnostics`. |
| `emit.<target>.statementProduced` | `trace` | `progress` | Per-statement event for SSDT (one per `Statement` in the typed deterministic stream per A35). High-volume; default-hidden. |
| `emit.<target>.completed` | `info` | `end` | Per-target end. Payload carries `outputPath`, `byteCount`, `statementCount` (where applicable). |

### 7.6 `deploy.*` codes

| Code | Level | Phase | When |
|---|---|---|---|
| `deploy.started` | `info` | `start` | `Deploy.executeStream` begins. |
| `deploy.batchSent` | `debug` | `progress` | Per-batch event (per-row INSERTs grouped or SqlBulkCopy run). Payload carries `ssKey`, `rowCount`, `realization` (`Incremental` / `Bulk`). |
| `deploy.bulkCopy.completed` | `info` | `progress` | SqlBulkCopy batch complete. Payload carries `ssKey`, `rowCount`, `durationMs`. |
| `deploy.completed` | `info` | `end` | Deploy complete. |

### 7.7 `canary.*` codes

| Code | Level | Phase | When |
|---|---|---|---|
| `canary.diffEmpty` | `info` | `end` | PhysicalSchema diff is empty (canary green). Payload carries `tableCount`, `comparedAt`. |
| `canary.toleranceMatched` | `info` | `progress` | A divergence matched a named Tolerance entry (per R6). Payload carries `tolerance`, `ssKey`, `divergence`. |
| `canary.divergence` | `error` | `error` | A divergence did NOT match any tolerance. **Fails the canary.** Payload carries `ssKey`, `axis`, `expected`, `actual`. |
| `canary.cdcSilent` | `info` | `progress` | CDC-silence canary observed no CDC events on idempotent redeploy (per L3-D1 / chapter 4.1.B). |

### 7.8 `summary.*` codes

| Code | Level | Phase | When |
|---|---|---|---|
| `summary.stageCompleted` | `info` | `end` | One per stage (extract / profile / emit / deploy / canary, when run). Payload carries `stage`, `durationMs`, per-stage aggregates. |
| `summary.benchPersisted` | `info` | `progress` | `Bench.Run` JSON written. Payload carries `path`, `tag`, `labelCount`. |
| `summary.runComplete` | `info` | `end` | **Mandatory terminal event.** Payload carries the full run roll-up (see §10). Every run emits this exactly once, even on failure (with `outcome: "failed"`). |

## §8 Classification taxonomies (lifted from V1, carbon-copied)

These taxonomies are payload property values, not envelope fields. They are closed enums; the F# implementations adopt the V1 strings verbatim so audit-trail diff against V1 baselines is byte-clean.

### 8.1 `TighteningRationale` (rationale for `transform.declined`)

Lifted from V1 `Osm.Validation.Tightening.TighteningRationales` constants (referenced from `CommandConsole.cs:2399-2412`). Closed enum:

`DataHasNulls` · `NullBudgetEpsilon` · `RemediateBeforeTighten` · `ProfileMissing` · `UniqueDuplicatesPresent` · `CompositeUniqueDuplicatesPresent` · `UniquePolicyDisabled` · `DataHasOrphans` · `DeleteRuleIgnore` · `CrossSchema` · `CrossCatalog` · `ForeignKeyCreationDisabled` · `ForeignKeyNoCheckRecommended`

**Each rationale maps to an operator action.** The runSummary's `rationaleHistogram` (§10) groups non-tightened axes by rationale; an operator scanning the histogram knows which knob to turn:
- `NullBudgetEpsilon` → "raise the null budget" (config edit).
- `UniqueDuplicatesPresent` → "resolve the duplicates in source data" (data action) OR "disable unique policy for this index" (override).
- `ForeignKeyNoCheckRecommended` → "consider whether to emit WITH NOCHECK" (toggle flip).

The contract reserves the right to **add variants** under the closed-DU expansion empirical-test discipline (`DECISIONS 2026-05-13`). Adding a variant is structural; consumers re-compile (or fail their match coverage tests). Removing a variant requires a DECISIONS entry naming the V1 rationale as historical.

### 8.2 `ProbeOutcome` (outcome for `profile.probe.*`)

Lifted from V1 `ProfilingProbeOutcome` enum (`CommandConsole.cs:1247-1260`). Closed enum:

`Succeeded` · `TrustedConstraint` · `FallbackTimeout` · `Cancelled` · `AmbiguousMapping` · `Unknown`

Each outcome maps to a code (see §7.3): `Succeeded` / `TrustedConstraint` → `profile.probe.succeeded`; `FallbackTimeout` → `profile.probe.fallbackTimeout`; `Cancelled` → `profile.probe.cancelled`; `AmbiguousMapping` → `profile.probe.ambiguousMapping`; `Unknown` → `profile.probe.succeeded` with `outcome=Unknown` (rare; explicit "we don't know whether the probe succeeded" state).

### 8.3 `TransformSource` (envelope `source` property)

Generalization of V1 `ToggleExportValue.Source` (`Configuration | Override | Default`) to V2's full pillar 9 surface. Closed enum:

`operator` · `configuration` · `default` · `derived`

- `operator` — explicit operator argument (CLI flag, config-file entry that the operator wrote). Maps to V1's `Configuration` for tightening toggles + `Override` for downstream rule-level operator-supplied overrides.
- `configuration` — config-file resolved (operator wrote the config; V2 resolved it). Reserved for V2 surfaces where config-resolved differs from operator-argument (e.g., a per-environment override applied through the multi-env policy stack).
- `default` — V2's default. Maps to V1's `Default`.
- `derived` — computed from another source (e.g., a transform whose source is a chained operator decision). Reserved for downstream consumers; not used by any slice-1 code.

### 8.4 `OverlayAxis` (payload property on `transform.registered` and `transform.applied` when `intent=OperatorIntent`)

Already canonical in V2 per pillar 9 + Policy DU. Closed enum:

`Selection` · `Emission` · `Insertion` · `Tightening`

The contract does NOT introduce this — it reserves the property name on transform events. The TransformRegistry full-sweep retroactive refactor at A.4.7 (`V2_PRODUCTION_CUTOVER.md §6.4.7`) carries the full registration surface; slice 1 commits to the property name so when A.4.7 lands the contract is already aligned.

## §9 Payload shapes by category

Each category names the structured payload properties its events emit. The contract specifies property names and value types; the F# producers consume the existing writer surfaces (`Lineage`, `Diagnostics`, `Bench`) and the serialization layer projects them.

### 9.1 `config.*` payload shapes

```jsonc
// config.runStart
{ "version": "2.4.7+aef12c3", "command": "projection extract", "configPath": "/etc/v2/config.json",
  "overlayAxesEnabled": ["Selection", "Tightening"] }

// config.connectionResolved
{ "kind": "SnapshotJson", "identity": "out/osm_model.json", "secretsRedacted": true }
// or
{ "kind": "LiveOssysConnection", "identity": "server=***,db=ossys_uat;application=v2", "secretsRedacted": true }

// config.validationFailed
{ "path": "$.profiling.samplingCap", "reason": "expected integer >= 0; got 'fast'", "suggestion": "set to a non-negative integer such as 10000" }

// config.toggleResolved
{ "toggle": "foreignKeys.allowNoCheckCreation", "value": true, "source": "operator" }
```

### 9.2 `extract.*` payload shapes

```jsonc
// extract.attribute.dropped (actionable; explains why an operator-supplied filter removed a value)
{ "filter": "MetadataContractOverrides", "rule": "exclude-deprecated", "ssKey": "OSUSR_FOO.dbo.OrderHeader.LegacyFlag" }
```

### 9.3 `profile.*` payload shapes

```jsonc
// profile.probe.fallbackTimeout (the actionable shape — note the suggestedConfig)
{ "ssKey": "OSUSR_FOO.dbo.OrderHeader", "axis": "uniqueDuplicates", "outcome": "FallbackTimeout",
  "sampledRows": 10000, "totalRows": 4823917,
  "suggestedConfig": { "path": "$.profiling.perTable[\"OSUSR_FOO.dbo.OrderHeader\"].samplingCap",
                       "value": 100000 } }

// profile.probe.ambiguousMapping (the second actionable shape)
{ "ssKey": "OSUSR_FOO.dbo.OrderLine", "axis": "foreignKeyReality", "outcome": "AmbiguousMapping",
  "candidateTargets": ["OSUSR_BAR.dbo.Product (PK: Sku, Variant)", "OSUSR_BAZ.dbo.Item (PK: ItemKey)"],
  "suggestedConfig": { "path": "$.overrides.foreignKey[\"OSUSR_FOO.dbo.OrderLine.ProductRef\"]",
                       "value": { "target": "OSUSR_BAR.dbo.Product", "tupleKeys": ["Sku", "Variant"] } } }
```

### 9.4 `transform.*` payload shapes

```jsonc
// transform.declined (carries the V1-lifted rationale enum)
{ "transformId": "NullabilityPass.tightenToNotNull", "ssKey": "OSUSR_FOO.dbo.OrderHeader.CustomerId",
  "rationale": "DataHasNulls", "evidence": { "nulls": 47, "rows": 12483, "nullPercent": 0.0038 } }

// transform.registered (one per registered transform at run start)
{ "transformId": "NullabilityPass.tightenToNotNull", "stage": "Pass", "intent": "OperatorIntent",
  "overlayAxis": "Tightening" }
```

### 9.5 Other category payload shapes

`emit.*`, `deploy.*`, `canary.*`, `summary.*` payload shapes follow the same discipline: every numeric goes in `payload`; no f-string composition; SsKey identifies the node when applicable. Examples in the slice 7 (full-export CLI) implementations.

## §10 The terminal `runSummary` event

Every run emits exactly one `summary.runComplete` event as its last line on stderr. This is the operator's scrollback target — when the terminal scrolls past everything, the operator scrolls to the last `summary.runComplete` and reads the roll-up.

```jsonc
{
  "runId": "01HXJVZK4F6Y2D9CPRN7M2X8AT",
  "ts": "2026-05-19T14:25:48.103Z",
  "level": "info",
  "category": "summary",
  "code": "summary.runComplete",
  "phase": "end",
  "payload": {
    "outcome": "succeeded",                    // succeeded | failed | aborted
    "command": "projection full-export",
    "durationMs": 161621,
    "stages": [
      { "stage": "extract",  "durationMs":  5421, "outcome": "succeeded" },
      { "stage": "profile",  "durationMs": 89102, "outcome": "succeeded" },
      { "stage": "emit",     "durationMs": 22441, "outcome": "succeeded" },
      { "stage": "canary",   "durationMs": 44657, "outcome": "succeeded" }
    ],
    "eventCounts": { "info": 1247, "warn": 23, "error": 0, "debug": 0 },
    "rationaleHistogram": {
      "DataHasNulls":               { "count": 187, "samples": ["OSUSR_FOO.dbo.OrderHeader.CustomerId", "OSUSR_FOO.dbo.Customer.MiddleName", "OSUSR_BAR.dbo.Product.DiscontinuedAt"] },
      "NullBudgetEpsilon":          { "count":  12, "samples": ["..."] },
      "RemediateBeforeTighten":     { "count":   4, "samples": ["..."] },
      "UniqueDuplicatesPresent":    { "count":   2, "samples": ["..."] }
    },
    "probeOutcomeHistogram": {
      "Succeeded":         { "count": 2891 },
      "TrustedConstraint": { "count":  102 },
      "FallbackTimeout":   { "count":   17, "samples": ["..."] },
      "AmbiguousMapping":  { "count":    3, "samples": ["..."] }
    },
    "transformSummary": {
      "registered": 47,    "applied": 312,    "declined": 205
    },
    "suggestedConfigEdits": 20,                // count of events that carried payload.suggestedConfig
    "artifacts": [
      { "kind": "osm_model",    "path": "out/osm_model.json",    "bytes":  847122 },
      { "kind": "profile",      "path": "out/profile.json",      "bytes": 1284412 },
      { "kind": "ssdt",         "path": "out/ssdt/",             "files":  287 },
      { "kind": "decisionLog",  "path": "out/decision-log.json", "bytes":  482103 },
      { "kind": "opportunities","path": "out/opportunities.json","bytes":   34218 },
      { "kind": "bench",        "path": "bench/full-export/20260519T142307Z.json", "bytes": 24816 }
    ],
    "aggregates": { ... }                      // see §11 — dedup-collapsed event aggregates
  }
}
```

**Mandatory properties.** `outcome`, `command`, `durationMs`, `stages`, `eventCounts`, `artifacts`. The other properties are conditional (e.g., `rationaleHistogram` empty if no transform.declined events fired; `probeOutcomeHistogram` empty if no profiling ran).

**`outcome` semantics.** `succeeded` (run completed and all stages green); `failed` (an `error`-level event fired and the run aborted; e.g., canary divergence, config validation failure); `aborted` (operator interrupt; SIGINT before run completion).

**Even on failure, runSummary emits.** This is the discipline. The operator running `extract` and hitting `ctrl-c` after 3 minutes should still see a runSummary with `outcome: "aborted"`, stages-so-far durations, and the partial event counts. The CLI surface registers a finalizer that writes runSummary on every exit path.

## §11 Roll-up collapse algorithm

The runSummary's `aggregates` property carries collapsed event groups. Algorithm lifted from V1 `EmitPipelineLog` (`CommandConsole.cs:2049-2089`) with one structural improvement.

**Input.** The list of every event emitted during the run, in chronological order.

**Group key.** `(category, code, ssKey)` — three-tuple. Events sharing the key collapse into one aggregate entry. (V1 used `(Step, Message)` — a two-tuple where `Message` is the full prose. V2's three-tuple includes `ssKey` because most of V2's events are SsKey-bearing; collapsing across SsKey would erase the operator's per-node signal.)

**Output.** Each group becomes one aggregate entry:

```jsonc
{
  "category": "transform",
  "code": "transform.declined",
  "ssKey": null,                              // null when the group spans multiple SsKeys
  "count": 187,
  "firstTs": "2026-05-19T14:24:11.482Z",
  "lastTs":  "2026-05-19T14:24:17.103Z",
  "samples": [
    { "ssKey": "OSUSR_FOO.dbo.OrderHeader.CustomerId",
      "rationale": "DataHasNulls", "evidence": { "nulls": 47, "rows": 12483 } },
    { "ssKey": "OSUSR_FOO.dbo.Customer.MiddleName",
      "rationale": "DataHasNulls", "evidence": { "nulls": 2341, "rows": 3829 } },
    { "ssKey": "OSUSR_BAR.dbo.Product.DiscontinuedAt",
      "rationale": "DataHasNulls", "evidence": { "nulls": 1822, "rows": 1903 } }
  ]
}
```

**Sample selection.** The first three events of the group, by chronological order. (V1 selects the first three by emission order at `CommandConsole.cs:2069-2078`; V2 carries this verbatim. A future refinement may switch to "three with maximum payload diversity" if operator feedback suggests; for slice 1, first-three matches V1 byte-for-byte.)

**Big-O.** O(N) over event count for group construction; O(M log M) over group count for output ordering (descending by `count`, then ascending by first occurrence). M ≤ N. Per the Big-O audit discipline (chapter B.3 contribution), groups are built once into a `Dictionary<(string,string,string option), GroupAccumulator>` during stream emission rather than re-scanning at runSummary time.

**`Bench.Stats` integration.** Per-label bench aggregates already carry `(Count, MeanMs, P50, P95, P99, TotalMs, StdevMs)` — they are aggregates by construction. They surface in runSummary's `aggregates` array under `category=summary, code=bench.label`, one entry per label, with the bench stat structure as the payload.

## §12 Actionable signal patterns — the `suggestedConfig` discipline

V2's contract elevates V1's *single* example of an actionable→config-edit event (`EmitNamingOverrideTemplate` at `CommandConsole.cs:2416-2503`) into a **first-class envelope property** on every event whose remediation is a config edit. The discipline: when V2 detects a condition the operator could fix by editing config, the event carries `payload.suggestedConfig` naming the exact JSON path + value to apply.

```jsonc
"suggestedConfig": {
  "path": "$.profiling.perTable[\"OSUSR_FOO.dbo.OrderHeader\"].samplingCap",
  "value": 100000,
  "note": "raised from default 10000 to capture 1% of 4.8M rows; tune to your I/O budget"
}
```

Events that MUST carry `suggestedConfig`:
- `profile.probe.fallbackTimeout` — suggest a higher samplingCap.
- `profile.probe.ambiguousMapping` — suggest a `foreignKey` override naming the disambiguating target.
- `extract.warning` with code `naming.duplicate-logical-entity` — suggest the V1-equivalent JSON snippet (lifted from `EmitNamingOverrideTemplate`).
- `transform.declined` with rationale ∈ {`NullBudgetEpsilon`, `UniquePolicyDisabled`, `ForeignKeyCreationDisabled`, `ForeignKeyNoCheckRecommended`} — suggest the toggle path + value that would tip the decision.

Events that MAY carry `suggestedConfig` (forward-compat):
- Any `warn`-level event whose remediation is config-editable.

The runSummary's `suggestedConfigEdits` count (§10) is the operator's high-leverage signal: "you have 20 events whose fix is a config edit; here's the file paths to grep." Downstream tooling (a future `v2 suggest-config <runId>` subcommand) consumes the event stream and emits a single merged config patch.

## §13 Antipatterns banned by construction

The contract refuses these patterns. Each refusal is a structural commitment: F# producers cannot accidentally violate the contract because the envelope schema does not have a slot for the antipattern's content.

| # | Pattern | V1 example | Why banned | V2's alternative |
|---|---|---|---|---|
| 1 | **Multiple logging libraries to the same sink** | Spectre + ILogger + IConsole all writing to stdout/stderr (`Program.cs:18-20`) | Output is uninterleaved; consumers cannot parse | Single sink (stderr); single format (NDJSON); single envelope |
| 2 | **Free-text section headers** | "SSDT build summary:" + 11 sub-bullets (`CommandConsole.cs:226-249`) | No grep target; consumer cannot select | Categories + codes (`emit.ssdt.completed` with payload) |
| 3 | **Structured data inlined into prose** | `"Tightening: Columns {0}/{1}, Unique {2}/{3}, Foreign Keys {4}/{5}"` (`CommandConsole.cs:243-248`) | Six numerics in one f-string; consumer must parse prose | Every numeric is a separate `payload` property |
| 4 | **Emoji / ANSI escapes / Unicode borders** | 🔴 ⚠️ 🚨 💡 ⭐ ✓ + 79-char `═══` block (`CommandConsole.cs:1850-1852`) | Contaminates piped output; meaningless in JSON | No emoji in events; ANSI is downstream renderer's choice |
| 5 | **Mixed levels writing to undifferentiated sink** | `AddSimpleConsole()` with no filter (`Program.cs:18`); Debug + Info + Warn + Error all to stdout | Operator cannot distinguish; noise drowns signal | `level` envelope field; `--debug` opt-in for Trace/Debug |
| 6 | **Same event written to two sinks with two formats** | `FullExportPipeline.cs:329` "Full export pipeline completed." + `FullExportCommandFactory.cs:136` "Full export pipeline summary:" | Operator sees the same event twice; cannot correlate | One event per occurrence; correlation by `runId` + `stepId` |
| 7 | **Swallowed errors continuing silently** | `try { … } catch (Exception ex) { WriteErrorLine($"[warning] Failed to open report: {ex.Message}"); }` (`CommandConsole.cs:175-190`) | Operator sees a warning; operator does not see what was supposed to happen next | Every catch site emits an `error` event with `stepId` matching the `start`; runs do not continue past errors silently |
| 8 | **No correlation IDs** | `PipelineLogEntry` (`PipelineExecutionLog.cs:22-26`) carries no run-id; `EmitPipelineLog` matches start/end by string-equality (`CommandConsole.cs:2049-2089`) | Multiple runs interleave indistinguishably; no way to filter | `runId` (ULID) mandatory on every event |
| 9 | **Structured metadata flattened to prose at boundary** | `FormatLogEntry` (`CommandConsole.cs:2092-2099`) joins metadata dict into `" \| key=value, key=value"` string | Structure exists in memory; lost at egress | `payload` is JSON object; serialization preserves shape |
| 10 | **Library-internal chatter on operator path** | "Opening SQL connection." / "SQL connection opened successfully." (`SqlClientAdvancedSqlExecutor.cs:99-104`) | Connection lifecycle is not domain signal | Internal library logs are NEVER promoted to the operator stream; F# adapters wrap libraries silently |
| 11 | **Progress bar contaminating piped output** | `AnsiConsole.Progress()` writes ANSI escape sequences when stdout is redirected (`IProgressRunner.cs:21-26`) | Pipe-to-file produces garbage | Progress IS phase-bearing events; no separate progress-bar library |
| 12 | **Section headers printed unconditionally even when content is empty** | "Tightening Statistics:" header followed by `<none>` (`CommandConsole.cs:528-532`) | Operator scans "<none>" repeatedly | An event with empty payload SHOULD NOT be emitted; emit zero events when there is nothing to say |

## §14 Sink and serialization implementation

**One implementation surface: `Projection.Pipeline.LogSink`** (new module, slice 1 to be implemented; this contract reserves the name). The module exposes:

```fsharp
[<RequireQualifiedAccess>]
module LogSink =
    type Envelope = {
        RunId: string
        Ts: System.DateTime
        Level: Level
        Category: Category
        Code: string
        Phase: Phase
        Source: TransformSource option
        SsKey: SsKey option
        StepId: string option
        Payload: Map<string, obj>     // serialized as JSON object
    }

    /// Emit one event to stderr as NDJSON. Wall-clock capture happens here
    /// (reified non-determinism boundary; same precedent as BenchSink.persistJson:54).
    val emit: Envelope -> unit

    /// Construct an envelope at the boundary; the runId comes from a process-
    /// scoped accumulator initialized at CLI entry.
    val envelope: Category -> string -> Phase -> Map<string, obj> -> Envelope
```

**Projection from existing writers.** Three projection functions in `Projection.Pipeline`:

```fsharp
val LineageProjection.toEnvelope: PassName -> LineageEvent -> Envelope     // transform.lineage
val DiagnosticsProjection.toEnvelope: DiagnosticEntry -> Envelope          // transform.diagnostic
val BenchProjection.toEnvelopes: Bench.Run -> Envelope list                // summary.benchPersisted + aggregates
```

These run at the CLI boundary (`Projection.Cli/Program.fs`); the writer-monad outputs from pass execution flow through them as a final pass before exit. Per the no-I/O-in-Core discipline, none of these live in `Projection.Core`.

**Run accumulator.** A `RunAccumulator` (single-instance, process-scoped, initialized at `config.runStart`, finalized at `summary.runComplete`) holds the running event counts, the per-category dictionary of `(code, ssKey) → GroupAccumulator`, the stage timing list, and the suggested-config edit count. Finalization emits the `summary.runComplete` envelope with the accumulator's roll-up.

**Property tests.** The slice 1 close ships:
- `Logging.envelope-mandatory-fields`: every emitted envelope has runId / ts / level / category / code / phase / payload. FsCheck-generated.
- `Logging.code-categorization`: every emitted code's top-prefix matches its `category`. Property over the union of static codes in §7.
- `Logging.phase-correlation`: every `start` event is paired with exactly one `end` or `error` event sharing `stepId`. Property over a synthesized event stream.
- `Logging.runSummary-mandatory`: every successful run emits exactly one `summary.runComplete`. Property over fixture runs.
- `Logging.runSummary-failure`: every failed/aborted run emits `summary.runComplete` with `outcome ∈ {failed, aborted}`. Property over fixture failure injections.
- `Logging.suggestedConfig-on-required-codes`: every emitted event whose code is in the "MUST carry suggestedConfig" list of §12 carries `payload.suggestedConfig`. Property over fixture events.

## §15 CLI library recommendations

The contract's wire format is library-agnostic — `System.Text.Json` on the F# stdlib suffices to write the envelope. But the CLI shell that wraps the LogSink (argument parsing, optional TTY rendering) is a real surface, and **V1's choice of three competing libraries to undifferentiated sinks (Spectre.Console + Microsoft.Extensions.Logging + System.CommandLine all written from `Osm.Cli/Osm.Cli.csproj:10-13`) is the antipattern this contract refuses**. This section names V2's library choices and the strict adapter discipline that prevents the V1 mess from re-emerging.

### 15.1 The two-channel pattern

```
┌────────────────────────────────────────────┐
│ F# producer (pass / adapter)               │
│   writes to Lineage / Diagnostics / Bench  │
└────────────────────────────────────────────┘
                  │
                  ▼
┌────────────────────────────────────────────┐
│ LogSink (Projection.Pipeline.LogSink)      │
│   serializes envelopes to NDJSON           │
└────────────────────────────────────────────┘
                  │
       ┌──────────┴────────────┐
       ▼                       ▼
 ┌──────────────┐    ┌────────────────────────┐
 │ stderr       │    │ TtyRenderer            │
 │ (NDJSON      │    │ (Spectre.Console under │
 │  primary)    │    │  strict adapter;       │
 │              │    │  opt-in via --pretty;  │
 │              │    │  gated on TTY detect)  │
 └──────────────┘    └────────────────────────┘
```

**Channel 1 (default, always-on): stderr NDJSON.** The structured event stream per §3-§13. Machine-parseable. Operators pipe with `2>events.log` or `2>&1 \| jq`. This channel IS the contract.

**Channel 2 (opt-in, TTY-gated): Spectre.Console pretty rendering.** A Spectre-based renderer that subscribes to the same event stream and draws progress bars / tables / status indicators on stderr. Activates ONLY when (a) the operator passes `--pretty`, AND (b) `Console.IsErrorRedirected = false` (stderr is a real TTY, not a pipe or file). When channel 2 is active, channel 1 routes elsewhere: a `--json-out <path>` flag writes NDJSON to a file; without `--json-out`, channel 1's stderr write is suppressed and NDJSON is unrecoverable for that run. **Never both channels to the same TTY** — that's V1's failure mode (Spectre ANSI escapes interleaved with raw logger output on the same stream).

**Default behavior.** No `--pretty` flag → channel 1 to stderr; Spectre uninvoked. Operator gets clean NDJSON. This is the path slice 7 (full-export CLI) default to; the chapter B.4 close gate is "this default behavior emits conforming events." Channel 2 (`--pretty`) is a quality-of-life feature on top of the structural commitment, never a substitute for it.

### 15.2 Library choices

| Concern | Recommended library | Rationale | Banned alternatives |
|---|---|---|---|
| **Argument parsing** | **Argu** (`fsprojects/Argu`) | F#-native; discriminated-union-based subcommand definition matches V2's closed-DU posture; idiomatic in the F# CLI ecosystem; pairs naturally with `Result<Config, ConfigError>` smart-constructor for parsed config | `System.CommandLine` (V1's choice at `Osm.Cli.csproj:13`; C#-shaped; verbose from F#); raw `argv` parsing (loses type safety) |
| **Structured event serialization** | **`System.Text.Json`** (.NET stdlib) | Already used by `BenchSink.persistJson` (`BenchSink.fs:31-34`); no third-party dependency; envelope is small + stable so no need for richer serializers; UTF-8 by default; performant | `Newtonsoft.Json` (extra dependency; V2 has no precedent); hand-rolled string concatenation (reinvents `JsonSerializer.Serialize` poorly; violates the text-builder-as-first-instinct discipline) |
| **Logger primitives** | **None — `LogSink` is hand-rolled** | The contract IS the logger; introducing `Microsoft.Extensions.Logging` would either (a) require routing every level through `LogSink` (extra indirection over a fundamentally simple sink) or (b) bypass `LogSink` (re-introducing V1's mess where `_logger.LogInformation` writes to stdout alongside `console.Out.Write`) | `Microsoft.Extensions.Logging` + `AddSimpleConsole` (V1's pattern at `Program.cs:18`; the failure mode this contract refuses); `Serilog` (same concern unless strictly routed via `LogSink`); any logger that owns its own sink |
| **TTY pretty rendering (opt-in)** | **Spectre.Console** (`spectreconsole/spectre.console`) — under strict adapter | When a renderer is wanted, Spectre IS the highest-quality choice (progress bars, tables, trees, colored output, robust ANSI handling, TTY-detection helpers). V1's mess was using Spectre AS the event stream, not as a derived rendering consumer. Used as a subscriber to the `LogSink` event stream, gated on `--pretty` + `Console.IsErrorRedirected = false`, Spectre's strengths apply cleanly | Hand-rolled ANSI escape codes (reinvents Spectre poorly); progress libraries that write to stdout (mixes with piped artifact data); Spectre used as the primary emit surface (V1's pattern; banned by §13.4 + §13.11) |

**F# / .NET versioning posture.** V2 targets .NET 9 (carbon-copy inherits from V1's `csproj` settings). `System.Text.Json` is built in; `Argu` and `Spectre.Console` are NuGet packages added at the `Projection.Cli` project boundary only — never referenced from `Projection.Core` (no I/O in Core; CLI-shell libraries are boundary concerns).

### 15.3 The `TtyRenderer` strict adapter

If channel 2 lands (optional for chapter B.4; the chapter close gate does NOT require it — it's a post-chapter slice if operator feedback warrants), the F# adapter shape is:

```fsharp
[<RequireQualifiedAccess>]
module TtyRenderer =
    /// Activates Spectre.Console as a subscriber to the LogSink
    /// event stream. Idempotent; returns IDisposable to detach.
    /// Caller MUST verify (a) operator passed --pretty AND
    /// (b) Console.IsErrorRedirected = false before calling.
    /// While active, LogSink.emit suppresses its default stderr
    /// write (avoids double-rendering). NDJSON can still be
    /// captured via the jsonOutPath supplied at activation.
    val attach: jsonOutPath: string option -> System.IDisposable

    /// Render strategy per category — closed mapping; extensions
    /// require a DECISIONS entry naming the new strategy.
    /// (Defaults: profile.cache.populated -> progress tick;
    ///  transform.declined -> table row append; summary.runComplete
    ///  -> final summary panel; warn/error -> colored log line;
    ///  everything else -> compact log line.)
    val internal renderStrategy: Category -> code: string -> RenderStrategy
```

**The discipline behind "strict adapter."** The adapter has exactly two responsibilities: (1) consume the structured event stream `LogSink` produces; (2) render it via Spectre. The adapter NEVER:
- writes its own events bypassing the LogSink envelope
- accepts pass-author-supplied messages outside the envelope
- emits to stdout (only stderr)
- mixes Spectre rendering with raw `Console.WriteLine` / `printfn` / `eprintfn` (those are banned project-wide outside the LogSink + TtyRenderer modules — see §15.5)

Per pillar 9, the adapter's existence is `OperatorIntent of Emission` — the operator's choice to see pretty output classifies as an emission-axis transform. One `config.toggleResolved` event fires at run start naming `pretty=<bool>` with `source=operator|default`.

### 15.4 What this means for slice 7 (full-export CLI)

| Slice | Implication |
|---|---|
| **7** (full-export) | Adds `Argu`-based command surface for `projection full-export --config <path> [--output <dir>]`. No `--pretty` flag yet (channel 2 is post-chapter); default emit = NDJSON to stderr. The composition's `summary.stageCompleted` events drive Spectre's multi-stage progress rendering when channel 2 lands; `summary.benchPersisted` fires post-run and would render as a Spectre summary panel under channel 2. (Standalone `extract` + `profile` subcommands originally scoped at chapter-open are dropped per chapter-mid rescope.) |
| **post-chapter** | `--pretty` + `TtyRenderer` lands as a follow-up slice if operator feedback shows the NDJSON-only default is unfriendly for interactive runs. Not gating Phase B exit; the structural channel 1 is the deliverable. |

### 15.5 What about `Console.WriteLine` / `printfn` / `eprintfn`?

**Banned outside `LogSink.emit`.** Every operator-visible byte flows through the LogSink envelope. The build-time discipline is grep-auditable: any `Console.WriteLine` / `Console.Write` / `printfn` / `eprintfn` / `printf` outside `LogSink.fs` (or its sibling `TtyRenderer.fs` if Spectre lands) requires a per-line justification comment per the substantive-rationale discipline (CLAUDE.md operating disciplines table). Slice 1's exit-criterion test verifies this: a grep over `sidecar/projection/src/` for the patterns produces only audited (or LogSink-local) results.

Exception: `BenchSink.persistJson` writes JSON file content to disk via `File.WriteAllText` (`BenchSink.fs:63`). That's a file-write boundary, not a console-output boundary; it is not subject to this ban. Similarly, F# Result-error rendering at the unhandled-exception level (the CLI's last-resort `try/with` at `Program.fs` top level) may write to stderr directly with an explicit LINT-ALLOW; the entry-point boundary is its own audited site.

## §16 What about Core's existing writers?

Core's `Lineage<'a>` and `Diagnostics<'a>` writers are NOT touched by this contract. They continue to be the typed in-process surfaces F# passes write to. The serialization layer at `Projection.Pipeline.LogSink` projects them onto the envelope at egress.

This preserves three commitments:
1. **No I/O in Core.** Core's writers accumulate values; the boundary serializes.
2. **A24 (chronological under bind).** Lineage's trail-ordering invariant is preserved during projection (Lineage events project in trail order to `transform.lineage` envelopes).
3. **Writer-fidelity discipline.** `LineageDiagnostics.tellDiagnostics` and `Lineage.ofValueAndEvents` remain the canonical pass-driver primitives. Nothing about how passes log changes.

What changes is the **CLI surface's projection** — instead of writing the writer outputs as artifact JSON only (the current chapter-3.1 cash-out), the CLI also emits per-event envelopes to stderr during execution. The artifact files (decision log, opportunities report) continue to be the post-hoc analyzable forms; the event stream is the in-flight operator surface.

## §17 Proposed L3 axiom additions

The verifiability-triangle audit catalog (`AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md` Part III) currently has no L3 axiom naming the operator-facing event stream's structural commitments. Slice 1 proposes two additions to the L3-X (Diagnostics) section, to land at chapter B.4 close:

**L3-X11: Every CLI run emits a structured event stream conforming to the LOGGING-FORMAT contract.**
Every V2 CLI subcommand emits to stderr as NDJSON; every emitted event conforms to the envelope of §3; every event's `code` is in §7's taxonomy (or an additive extension landed under DECISIONS); every `summary.runComplete` is the last line of stderr.
- Tier 1 (cutover blocker — operator's primary surface).
- Currently named: yes (this document; promoted to Bucket A at chapter B.4 close).
- Verifiability: property tests per §14 + Docker-gated integration tests in slice 7 (full-export CLI).
- Failure mode if violated: operator cannot reliably parse V2's output; downstream tooling (dashboards, alerting) cannot be built on V2's surface.

**L3-X12: Every actionable event carries a `suggestedConfig` payload pointing at the JSON-path edit that would address it.**
Every event whose code is in the "MUST carry suggestedConfig" list of §12 carries `payload.suggestedConfig`; the runSummary's `suggestedConfigEdits` count names the operator's prioritized to-do list.
- Tier 2 (strongly desired — operator's high-leverage shortcut).
- Currently named: yes (this document; promoted to Bucket A at chapter B.4 close).
- Verifiability: property test per §14 (Logging.suggestedConfig-on-required-codes).
- Failure mode if violated: operator gets an actionable warning with no path to the fix; falls back to prose-parsing.

Both axioms are pillar-9-relevant: L3-X11 underwrites the envelope's `source` field (pillar 9 classification); L3-X12 underwrites the operator-actionability arm of pillar 9 (operator intent has a forward path to remediation).

At chapter B.4 close, the catalog entry is added to `AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md` §3.4; the coverage map row (§10.4) lists both axioms as Bucket A.

## §18 Next steps — extension cues for slices 2-7 (post-rescope)

Slice 1 lands the contract. Slices 2-7 consume it. Each subsequent slice adds events to V2's emission surface; the contract's discipline says **additive only**, **closed taxonomies extended via the closed-DU expansion empirical-test discipline**, **renames require DECISIONS entries**.

**Rescope note (2026-05-19):** The chapter-open named eight substantive slices; chapter-mid rescope (`DECISIONS 2026-05-19 (slice B.4.{4-7}.rescope)`) drops standalone `extract` + `profile` subcommands, adds an actionable-diagnostics slice (operationalizes §12 on the JSON-artifact emitters), and rescopes the full-export subcommand. The table below is the post-rescope shape.

| Slice | Surface | Extension cue |
|---|---|---|
| **2** (B.4.2.capture-retirement) — DONE | LiveProfiler post-retirement | The seven `LiveProfilerIntegrationTests` direct-SQL tests reshape to assert on `Cache.derive*` output; the tests now also assert that `profile.cache.populated` and `profile.probe.*` events fire correctly. The structural cleanup reduces the emission surface (5 fewer SQL probes); the event surface is preserved by routing through `Cache.derive*` and emitting one event per derivation. |
| **3** (B.4.3.composite-pk-fk) — DONE (resolved out-of-scope) | FK reality cash-out | No event-stream change (documentation-only; principal-PO answered composite-PK targets are not an OS use case). The slice-1 `AmbiguousMapping` outcome stands as the correct degenerate-case answer; codes unchanged. |
| **4** (B.4.4.module-filter-port) | ModuleFilter | At config resolution, emit one `config.toggleResolved` event per module-filter rule resolved (`source=operator`). During extract (under the slice-7 `full-export` orchestration), every excluded module fires one `extract.module.parsed` with payload `excluded: true`. |
| **5** (B.4.5.metadata-contract-overrides) | MetadataContractOverrides | At config resolution, emit one `transform.registered` per override (`intent=OperatorIntent, overlayAxis=Tightening or Emission`, `source=operator`). During emit, every applied override fires `transform.applied` with the typed outcome. |
| **6** (B.4.6.actionable-diagnostics) — NEW post-rescope | JSON-artifact emitters (decision-log.json / opportunities.json / validation.json) | Operationalizes §12 (`suggestedConfig` discipline) on the artifact side. Every actionable JSON entry whose finding has an addressable config knob carries a `suggestedConfig: { path, currentValue, proposedValue, rationale }` payload. Findings filtered/clustered/capped: sort by severity → cluster by axis → cap top-N per axis with overflow count surfaced. Property tests: `suggestedConfig` non-empty invariant; cluster-cap invariants under FsCheck-generated noisy inputs. This is L3-X12's structural cash-out on the artifact-side surface; pairs with the event-stream side from this contract. |
| **7** (B.4.7.full-export-cli) — RESCOPED from old slice 8 | full-export subcommand | The CLI subcommand emits the full event surface: `config.runStart` first, then `extract.*` (snapshot read; no live OSSYS in this chapter), `profile.*` per kind/derivation, `transform.registered` + `transform.applied` per overlay (slices 4-5), `emit.*` per target (SSDT + seeds + migration; NO dacpac in this chapter), `summary.stageCompleted` per stage, `summary.runComplete` last. The emitted JSON artifacts carry slice-6's actionable payloads. Docker-gated integration test exercises end-to-end snapshot-to-SSDT+seeds+migration+actionable-diagnostics flow and asserts runSummary contains the orchestrated stages. **Phase B *structural* exit gate** runs here; the functional-equivalence arm (V2 vs V1 `osm_model.json` against live OSSYS) waits on a follow-up chapter when `LiveOssysConnection` lands. |

**TransformRegistry cues (post-chapter-B.4; the A.4.7 retroactive refactor).** When `TransformRegistry` lands (per `V2_PRODUCTION_CUTOVER.md §6.4.7`), the `transform.registered` events at run start become the registry's structural surface: every `RegisteredTransform<'In, 'Out>` projects to one `transform.registered` envelope. The registry's totality property — every transformation site is named in the registry — is verifiable from the event stream: the count of `transform.registered` envelopes at any successful run equals the registry's static count. The skeleton-purity property (per pillar 9) is verifiable: a run with `--skeleton-only` should emit zero `transform.registered` envelopes with `intent=OperatorIntent`.

**Open extension surfaces (not in this slice, future-reserve):**
- **`canary.cdcSilent`** — chapter 4.1.B canary's CDC-silence property test surfaces here; slice 1 reserves the code.
- **`deploy.fallbackTimeout`** — when bulk-copy realization falls back to per-row INSERT (out-of-scope today; deferred per chapter 4.1.A close).
- **`transform.classifierShift`** — when a `LineageEvent` classification differs between two runs of the same input (reserved for future regression-detection surface).

## §19 Open questions deferred from this slice

These questions surfaced during slice 1 contract design but did not gate the contract's landing. Each carries an explicit re-open trigger.

1. **Multi-tenant operator context (`hostName` / `userName` / `processId`)** — not in envelope. Re-open when V2 ships under a hosted shell (chapter 5+) or when an operator names a use case requiring per-tenant filtering. Not a slice 1 blocker.

2. **W3C trace context (`traceId` / `spanId`)** — not in envelope. Re-open when V2 grows distributed-trace consumers (e.g., a hosted-V2 surface that fans into OpenTelemetry). Single-process synchronous V2 has no consumer today.

3. **Per-event commit/version stamping** — only on `config.runStart` (once per run). Re-open if operators report difficulty correlating events to V2 versions across mixed-version logs (e.g., concurrent canary runs on different V2 builds).

4. **Bench event projection granularity** — slice 1 projects `Bench.Run` to one `summary.benchPersisted` event + per-label entries in the runSummary `aggregates`. An alternative shape (per-label `summary.benchLabel` events emitted as Bench accumulates) was considered and rejected: Bench is by-construction an end-of-run aggregate (the writer accumulates across the run; the `Run` is a single artifact). Re-open if iterator-logging samples grow numerous enough to warrant in-flight surfacing rather than end-of-run.

5. **Severity escalation for repeated warnings** — if 1000 `profile.probe.fallbackTimeout` events fire in one run, should the cumulative impact escalate to `error`-level? Slice 1: no. The discipline is per-event severity; the runSummary's `suggestedConfigEdits` count IS the cumulative signal. Re-open if operator feedback shows that scrolling through 1000 warnings buries the signal.

6. **`canary.tolerance.matched` vs. `canary.tolerance.applied`** — slice 1 emits `canary.toleranceMatched` (past tense; a divergence matched a known tolerance). A future code `canary.tolerance.applied` could distinguish "the tolerance allowed the canary to pass; here is the divergence that was excused." Conflation acceptable for slice 1; refine at chapter 4.1.A M4 if the tolerance taxonomy needs the distinction.

7. **L3-X11 / L3-X12 axiom placement** — the audit catalog has L3-X1 through L3-X10; the next-available identifiers are L3-X11 and L3-X12. Reserving them here; final catalog entry lands at chapter B.4 close per the verifiability-triangle audit cadence.

8. **TransformRegistry registration shape** — slice 1 reserves the envelope properties (`transformId`, `stage`, `intent`, `overlayAxis`, `source`) but does NOT prescribe the F# type of the registration. The full surface lands at A.4.7 (3-week retroactive refactor per `V2_PRODUCTION_CUTOVER.md §6.4.7`); the contract's discipline is "when A.4.7 lands, every `RegisteredTransform<'In, 'Out>` projects to one `transform.registered` envelope" — no additional contract work required.

---

**Discipline summary.** The contract is structural: F# producers cannot violate it because the envelope schema does not accept the antipattern shapes. The contract is additive: codes and taxonomies extend; renames carry DECISIONS entries. The contract is operator-grounded: every payload property earns its place by being a grep target, a roll-up axis, or a config-edit signal. The contract is V1-informed: every taxonomy carbon-copies V1's strings so audit-trail diff against V1 is byte-clean.

V1 had the right ideas in the wrong sinks. V2's contract puts them in the right one.
