# THE VOICE — the build map (the file:line execution detail)

**The grounded execution layer beneath `THE_VOICE_INTEGRATION.md`.** The integration
doc is the *plan* (the slice sequence, the locked decisions); `THE_STORYBOARD.md` is the
*surface* (act × stream × outcome); `THE_VOICE.md` is the *register*. This document is the
**file:line map** — the concrete landing points, code inventories, and gap lists a builder
needs to execute each slice without re-deriving them. It is the consolidated output of a
five-agent research pass (2026-06-06) run while the understanding was fresh.

> **Line-number caveat.** Every `file:line` below is a snapshot from the 2026-06-06 research
> pass. Line numbers drift as code lands — treat them as "look here," then confirm by reading.
> The CLI-layer files (`Surface.fs`, `Comparison.fs`, `TtyRenderer.fs`) shifted slightly when
> **slice 1** landed (the `Surface` rename + the `Voice` seam); references to those files are
> pre-slice-1 unless noted.

> **Discipline reminder (holds across every slice).** The **twelve rules** (`THE_VOICE.md` §1)
> + the banned list (§2.2) over every string — enforced mechanically by the `VoiceTotalityTests`
> banned-list guard. **Codes never change, only copy** — the NDJSON contract is stable, so every
> `LogSink`/`EventProjection`/`Config` *code* assertion is DO-NOT-BREAK (see §6). **Declare-at-site,
> harvest-centrally; `code ⇔ copy` totality.** **IR grows under evidence** — never author copy for
> a latent surface ahead of the event that carries it.

---

## 0.5 — Reconciliation with `THE_CLI.md` (the four-verb surface, shipped 2026-06-07)

**The CLI was re-envisioned after this map was written.** `main` collapsed the ~16 verbs into
**four** — `project` / `check` / `explain` / `seal` — over one `MovementSpec`
(`src/Projection.Pipeline/MovementSpec.fs` + `MovementSurface.fs`; argv → typed `Intent` via
`MovementSurface.parse`; four thin executors in `src/Projection.Cli/Program.fs`). See `THE_CLI.md`
(the operator surface; §7 is the old-verb → new-verb namespace map, §13 the shipped shape) and
`THE_CLI_BACKLOG.md`. **`THE_CLI.md` is a sibling of the voice docs and writes in the `THE_VOICE.md`
register** — it is not a contradiction; it is the surface the voice now speaks on.

**What this means for this map:**

- **The voice machinery is UNCHANGED and still correct.** Main kept and rewired `Voice.fs`,
  `Watch.fs`, `TtyRenderer.fs`, `View.fs`, `Surface.fs`, `Comparison.fs`, and the event spine
  (`LogSink`, `EventProjection`, `Config`, `CatalogDiff`, `Episode`/`RunLedger`/`ChangeManifest`).
  Every Pipeline-/Core-layer `file:line` below (anything **not** in `Program.fs` or the deleted
  Argu files) remains accurate.
- **SUPERSEDED by the four-verb surface** (read `THE_CLI.md`, not this map's old refs):
  - **Every `Program.fs` `file:line` ref** — `Program.fs` was rewritten (~650 lines). The old
    `dispatchFullExport` / `runFullExport` / per-verb dispatch trees are gone.
  - **The run-type framing** — `FullExportRun` / `MigrationRun` / `TransferRun` / `EjectRun` /
    `DriftRun` are no longer the dispatch surface; they are engine faces behind the **one**
    `MovementSpec` executor (`THE_CLI.md` §13). The codes they emit are unchanged (the machinery
    is the same); only *which command invokes them* changed (§7 namespace map).
  - **"Run unification" (the slice-2 remainder, §4.2)** is **largely SUBSUMED** — the runs are
    already unified into `MovementSpec`. What remains of slice 2 is narrower: confirm the unified
    executor emits the per-stage `LogSink` stream across the `project`/`check` faces (so `--watch`
    works beyond `project --to <folder> --config`), plus the intra-stage ETA (§4.4).
  - **"Voice the Argu `Usage`" (the slice-4 tail, §6.1)** is obsolete in that form — the Argu arg
    files (`FullExportArgs`/`TransferArgs`/`VerbArgs`/`VerifyDataArgs`) are **deleted**. The help is
    now the hand-rolled `usageLines` in `Program.fs` + `MovementSurface.parse`'s refusal/preview
    notes (which `THE_CLI.md` §5 already specifies in the voice register). Voicing target = those.
- **Where the voice slices now land** (old verb → new face, `THE_CLI.md` §7):
  - the **verdict panel** + **`--watch`** Watch board render on `project` (`--to <folder> --config`
    today; extend to the other faces);
  - **errors as voice** (`Voice.errorsSurface` / `TtyRenderer.renderErrors`) wires into the four
    executors where they currently call `printErrors` / inline refusal prose — **available, not yet
    wired into the new `Program.fs`** (the reconciliation kept main's `Program.fs` verbatim; the
    voice surfaces stay ready to wire deliberately with the team's CLI direction);
  - **§5 gates** (`Voice.gateSurface`) wire into the `--go` / pre-flight refusal path
    (`THE_CLI.md` §5 — "loss is declared, never silent");
  - **`check`** is the home of the §6 proofs (canary / drift / data — mechanism-1, §6.2);
  - **`explain`** is the home of the §4 move surfaces (diff / policy / suggest — mechanism-1).

The slice *substance* (the seam, the `code ⇔ copy` totality, the dwell floor, the §12/§8 rendering,
the mechanism-1 typed `toView`s) is unchanged; only the **dispatch surface** it renders on moved
from sixteen verbs to four.

---

> **Line-number caveat (restated).** Pipeline-/Core-layer `file:line` refs below are accurate
> (a 2026-06-06 snapshot; treat as "look here"). **`Program.fs` refs are superseded** per §0.5 —
> use `THE_CLI.md` §7/§13 for the new dispatch.

---

## Table of contents

0. [Status — what is built](#0--status--what-is-built)
0.5. [Reconciliation with `THE_CLI.md` (the four-verb surface)](#05--reconciliation-with-the_climd-the-four-verb-surface-shipped-2026-06-07)
1. [The architecture (the settled spine)](#1--the-architecture-the-settled-spine)
2. [The emittable-code inventory (the canonical reference set)](#2--the-emittable-code-inventory-the-canonical-reference-set)
3. [Slice 1 — the Voice seam + totality test (LANDED)](#3--slice-1--the-voice-seam--totality-test-landed)
4. [Slice 2 — lifecycle events + streaming Watch](#4--slice-2--lifecycle-events--streaming-watch)
5. [Slice 3 — aggregate-at-scale + the timeline/ladder](#5--slice-3--aggregate-at-scale--the-timelineladder)
6. [Slice 4 — errors & config as voice + mechanism-1 toViews](#6--slice-4--errors--config-as-voice--mechanism-1-toviews)
7. [Slice 5 — the Diagnostics lift (deferred)](#7--slice-5--the-diagnostics-lift-deferred)
8. [The test blast-radius (UPDATE vs DO-NOT-BREAK)](#8--the-test-blast-radius-update-vs-do-not-break)
9. [Open decisions & guardrails](#9--open-decisions--guardrails)

---

## 0 — Status — what is built

| Slice | Scope | Status |
|---|---|---|
| **0** | The discipline (Event/Aggregate/Voice separation) | **RECORDED** (`DECISIONS 2026-06-06`; `CLAUDE.md` operating-disciplines row) |
| **1** | The Voice seam + `code ⇔ copy` totality test + verdict wiring | **LANDED** (commit `dab7b22`; `DECISIONS 2026-06-06 (later)`) |
| **2** | Stage-code vocabulary | **SCAFFOLDED** (the §13 stage codes are voiced in `Voice.all`) |
| **2** | Streaming Watch render path + dwell floor | **LANDED** (`Watch.fs`; `--watch` on `project --to <folder> --config`; `DECISIONS 2026-06-06 (later, slice 2)`) |
| **2** | Run unification onto the spine | **SUBSUMED** by the CLI re-envisioning (one `MovementSpec` executor; §0.5). Remaining: per-stage stream across the `project`/`check` faces |
| **2** | Intra-stage ETA | **PENDING** (this doc §4.4) |
| **3** | Aggregate-at-scale + timeline/ladder | **PENDING** (this doc §5; renders on `project` + `check ready`) |
| **4** | Errors as voice (`Voice.errorsSurface` / `TtyRenderer.renderErrors`) | **WIRED** — `printErrors` (every executor's error path) now renders the voiced §10/§14 surface (`DECISIONS 2026-06-07 (errors wired)`); the six `migrate` `REFUSED` shouts revoiced |
| **4** | configError message lift + the hand-rolled usage | **PENDING** (this doc §6.1; Argu `Usage` is obsolete — files deleted, §0.5) |
| **mech-1** | §5 gates voiced over all 8 `GateLabel`s (`Voice.gateStatement`/`gateSurface`) | **BUILT, not wired** — `gateSurface` exists + tested; wire into the `reportPreviewOutcome`/pre-flight refusal renderers (§0.5; the §5 safety surface — deliberate) |
| **mech-1** | §4 moves (→ `explain`) / §6 proofs (→ `check`) typed `toView` | **PENDING** (this doc §6.2) |
| **5** | Typed `DiagnosticPayload` lift | **DEFERRED** behind a real consumer (this doc §7) |

**Locked sub-decisions** (`DECISIONS 2026-06-06 (later)`): Core-purity → **1:1 projection-layer
companion** (pure-Core passes are never voiced in `Projection.Core`; `View`/`Surface` live
downstream in `Projection.Cli`, so Core cannot reference them). The `Surface.fs` rename
(`essence`/`dig` → `statement`/`substantiation`) is **done**.

---

## 1 — The architecture (the settled spine)

The triangle is whole *structurally* already; the Voice layer is what is thin.

| Layer | What it is | In the codebase as… |
|---|---|---|
| **Event** | a typed fact (`Code` + payload, no prose) | `LogSink.Envelope` (`LogSink.fs:129-141`); `LineageEvent` (`Lineage.fs:287`); `DiagnosticEntry.Code` (`Diagnostics.fs:70`); `ValidationError.Code` (`Result.fs:10`) |
| **Aggregate** | a run-scoped / durable fold of events | `RunAccumulator`/`GroupAccumulator` (`LogSink.fs:619-656`); `Compose.RunReport` (`Pipeline.fs:131-153`); `RunLedger`; **`Episode`** (`Episode.fs:61-128`) + `LifecycleStore` |
| **Voice** | the projection `Code → operator copy`, keyed centrally | **`Voice.fs`** (slice 1); `View`/`Surface`/`TtyRenderer`; `Comparison.Render` (the one pre-existing keyed renderer) |

**Catalog shape — hybrid** (`THE_VOICE_INTEGRATION.md` §8 decision 1): code-keyed declarative
catalog (mechanism 2) for the flat lifecycle/error/config codes; typed `toView` beside the Core
DUs (mechanism 1) for the payload-shaped moves/gates/proofs. **Placement** (decision 2):
declare-at-site / harvest-centrally (the `TransformRegistry` pattern) — `Voice.all` is the harvest;
`code ⇔ copy` totality is the sibling of `registered ⇔ executed`.

---

## 2 — The emittable-code inventory (the canonical reference set)

This is the contract the `code ⇔ copy` totality test holds Voice to. **LIVE** = fires today on a
plain run · **PARTIAL** = data exists, surface unbuilt · **LATENT** = designed, not built. (Source:
`THE_STORYBOARD.md` §7 + the run paths.)

### 2.1 The lifecycle spine (fires on EVERY run)

| Code | Payload keys | Emit site | Status |
|---|---|---|---|
| `config.runStart` | command, configPath, outputDir | `FullExportRun.fs:121`; CLI `withRun` `Program.fs:182` | **LIVE** |
| `summary.runComplete` | outcome, command, durationMs, stages, eventCounts, transformSummary, rationaleHistogram, suggestedConfigEdits, suggestedConfigDigest, artifacts, aggregates | `LogSink.fs:1009-1031` | **LIVE** (terminal, mandatory) |

### 2.2 Config (`LogSink.Config`)

| Code | Payload | Emit site | Status |
|---|---|---|---|
| `config.connectionResolved` | kind, modelPath, source | `FullExportRun.fs:126` | **LIVE** (full-export) |
| `config.validationFailed` | code, reason  /  exception, message | `FullExportRun.fs:63` (config errors), `:213` (exception fallback) | **LIVE** (all) |

**`pipeline.config.*` validation subcodes** (carried in `config.validationFailed.payload.code`, and on
`ValidationError.Code` at the CLI error path): `fileNotFound`, `fileReadError`, `jsonInvalid`,
`missingProperty`, `nullProperty`, `nullArrayElement`, `typeMismatch`, `credentialPropertyForbidden`,
`renameSourceMissing`, `renameSourceAmbiguous` — all from `Config.fs` (the `configError` helper at
`Config.fs:360-361`; sites enumerated in §6.1). Sibling: `pipeline.profiler.connectionMissing`
(`Pipeline.fs:925-930`).

### 2.3 Stages (per-stage Watch markers)

| Code | Payload | Emit site | Status |
|---|---|---|---|
| `extract.started` / `extract.completed` | (completed: moduleCount) | `Pipeline.fs:1010` / `:1018` | **LIVE** |
| `profile.started` / `profile.completed` | — | `Pipeline.fs:1024` / `:1029` | **LIVE** |
| `emit.started` / `emit.completed` | — | `Pipeline.fs:1033` / `:1038` | **LIVE** |
| `summary.stageCompleted` | stage, durationMs, outcome, stepId | `LogSink.fs:663-674` (`recordStageEvent`) | **LIVE** |

> These are emitted only by `FullExportRun` → `Compose.runWithConfig`. The other run types
> (`MigrationRun`/`TransferRun`/`EjectRun`/`DriftRun`) emit **none** of them today — the unification
> gap (§4.2).

### 2.4 Transform (`LogSink.Transform`) — the §4 move stream (mechanism-1, later)

| Code | Payload | Emit site |
|---|---|---|
| `transform.registered` | transformId, domain, stage, status, sites | `EventProjection.fs:217` |
| `transform.applied` | transformId, interventionId, decision | `EventProjection.fs:103` |
| `transform.declined` | transformId, interventionId, rationale | `EventProjection.fs:96` |
| `transform.lineage` | passName, transformKind, classification, detail? | `EventProjection.fs:115` |
| `transform.diagnostic` | source, code, message, metadata, suggestedConfig?, ssKey? | `EventProjection.fs:154`; `FullExportRun.fs:73,101` |

Diagnostic subcodes (the `transform.diagnostic.payload.code` space — slice 5 territory):
`tightening.*` (nullability/uniqueIndex/foreignKey), `profiling.*`, `structural.*`,
`selection.*`, `adapter.osm.*`, `tableId.*`. **Voiced by mechanism 1 (the move surfaces) / slice 5
(the diagnostics), not the flat catalog.**

### 2.5 Canary (`LogSink.Canary`) — the §6 proof

| Code | Payload | Emit site | Status |
|---|---|---|---|
| `canary.diffEmpty` | tableCount | `EventProjection.fs:234` | **LIVE** (canary leg) |
| `canary.divergence` | axisCounts, renderedDiff | `EventProjection.fs:255` | **LIVE** (canary leg) |

### 2.6 Summary / readiness / bench

| Code | Emit site | Status |
|---|---|---|
| `summary.readiness` | `Program.fs:1108` | **PARTIAL** (post-run) |
| `bench.label` | `LogSink.fs:787-792` (synthesized) | **LIVE** |

### 2.7 Gate / preflight codes (Act 3 — voiced by `buildGateSurface`, mechanism 1)

`migrate.dataViolatesTightening` (`Preflight.fs:135`), `migrate.connectionUnavailable` (`:209`),
`migrate.insufficientGrant` (`:291`), `migrate.grantProbeFailed`, `migrate.schemaReadFailed`,
`migrate.undeclaredDestructiveChange`; `transfer.connection.*`, `transfer.insufficientGrant`,
`transfer.cdcTrackedSink`, `transfer.unmappedIdentities`, `transfer.cyclicAssignedBySink`,
`transfer.unbreakableCycleFk`, `transfer.compositeSurrogateUnsupported`, `transfer.renameDiffFailed`.
`Preflight.GateLabel` (`Preflight.fs:383-395`); `Preflight.labelText` (`:398-408`); `classify` (`:418-440`).

### 2.8 Run-type × code-family matrix

| Family | FullExport | Migration | Transfer | Eject | Drift |
|---|:--:|:--:|:--:|:--:|:--:|
| `config.*` | ✓ | (CLI `withRun`) | (CLI `withRun`) | ✗ | ✗ |
| `extract/profile/emit.*` + `summary.stageCompleted` | ✓ | ✗ | ✗ | ✗ | ✗ |
| `transform.*` | ✓ | ✗ | ✗ | ✗ | ✗ |
| `canary.*` | ✓ (canary verb) | ✓ (verify leg) | — | — | — |
| `summary.runComplete` | ✓ | (CLI `withRun`) | (CLI `withRun`) | ✗ | ✗ |
| `migrate.*` / `transfer.*` gates | — | ✓ | ✓ | — | — |

> The "(CLI `withRun`)" runs get only the `config.runStart`/`summary.runComplete` bracket from
> `Program.fs:173-190` — **not** the per-stage stream. Closing that is slice 2.

---

## 3 — Slice 1 — the Voice seam + totality test (LANDED)

Commit `dab7b22`. Files: `src/Projection.Cli/Voice.fs` (new),
`tests/Projection.Tests/VoiceTotalityTests.fs` (new); wired `TtyRenderer.buildSummaryView`; renamed
`Surface` fields + `Comparison.catalogStatement`.

- **`Voice.fs`** — `type Copy = { Code; DocSection; Statement; Substantiation; Action }`; `all`,
  `lookup`, `toSurface`, `surfaceOf`, `verdict`, `stageName`, `errorFrame`, `errorSurface`. Voiced
  codes: the spine (`config.runStart`, `config.connectionResolved`, `summary.runComplete`,
  `config.validationFailed`), the §13 stage stream (`extract/profile/emit.started/.completed`,
  `summary.stageCompleted`), the §6 verdicts (`canary.*`).
- **`VoiceTotalityTests.fs`** — bidirectional `code ⇔ copy`; ⊆ known-emittable; recognized doc
  sections; total `stageName` + `errorFrame`; **the mechanical twelve-rule banned-list guard** over
  every voiced surface (filled + empty payload) and every error frame.
- **Wiring** — `TtyRenderer.buildSummaryView` verdict line is now `Voice.verdict <code> <payload>`
  (canary `§6` proof if a canary leg ran, else `summary.runComplete` `§3`), replacing inline
  `"SUCCEEDED"`/`"green"`/`"RED"`/`"FAILED"`.

**The errorFrame seam** is built but **not yet wired** — `printErrors` still prints `[code] message`.
Wiring it is slice 4 (§6.1).

---

## 4 — Slice 2 — lifecycle events + streaming Watch

> **LANDED (first increment).** The streaming Watch render path over the already-emitted
> `full-export` stage stream shipped as `src/Projection.Cli/Watch.fs` (opt-in `--watch`), with the
> **minimum-dwell floor** (`Watch.dwellMs`, default 120 ms / `PROJECTION_WATCH_DWELL_MS`) — a minimum
> inter-frame interval that holds each stage frame ≥ the floor before the next, adding only the
> remainder below the floor (a stage already past the floor is never delayed). The board subscribes to
> `LogSink` (a rendering, never a second emit surface) and reuses the §13 stage copy in `Voice.all`.
> Tested pure (board transitions + dwell + voiced lines + banned-list) in `WatchTests`. **Still PENDING
> below:** §4.2 (run unification — Migration/Transfer/Eject onto the spine) and §4.4 (intra-stage
> progress + ETA), which need new emit sites.

> **Goal (the remainder).** Per-stage live progress + honest ETA (`THE_VOICE.md` §13;
> `THE_STORYBOARD.md` Act 4), with `MigrationRun`/`TransferRun`/`EjectRun` unified onto the `LogSink`
> envelope spine.
> *Less greenfield than it reads:* `FullExportRun` → `Compose.runWithConfig` already emits a
> per-stage stream, `LogSink` already has the subscriber spine, and `Voice.stageName` already maps
> the stage names. The work is (a) **propagating** the pattern to the runs that bypass it, (b) adding
> a **live render path**, (c) sourcing **ETA**.

### 4.1 Per-run stage → §13-voice mapping

§13 operator names: **Reading the model** / **Checking the data** / **Building the changes** /
**Verifying the round-trip** (gerund-in-progress while live; resultative on completion).

**FullExportRun** → `Compose.runWithConfig` (`Pipeline.fs:1006-1041`) — the only instrumented run:

| Internal stage (emitted) | Where | §13 name |
|---|---|---|
| `extract.started`/`.completed` | `Pipeline.fs:1010/1018` | Reading the model |
| `profile.started`/`.completed` | `Pipeline.fs:1024/1029` | Checking the data |
| `emit.started`/`.completed` | `Pipeline.fs:1033/1038` | Building the changes |
| `pipeline` (coarse `recordStage`) | `FullExportRun.fs:180,201` | (whole run) |

**MigrationRun** (`MigrationRun.fs:375-467`) — **no LogSink**:

| Stage | Where | §13 name |
|---|---|---|
| diff-plan (`Migration.plan`→`preview`) | `:115-118`, `:383` | Reading the model / What changed |
| refuse-gates (declared-loss/schema/CDC/tightening) | `:119-130`, `:397-436` | (Act 3 — `buildGateSurface` already voices) |
| emit-DDL + emit-refactorlog | `:127,132`, `:386-387` | Building the changes |
| execute (rename SQL, ALTER) | `:437-446` | Building the changes (apply) |
| CDC-measure (`executeAndMeasureCdc`) | `:478-493` | The touch (Act 5) |
| verify round-trip | `:450-466` | Verifying the round-trip |
| record-episode (`recordVerified`) | `:506-529` | Recorded (Act 7) |

**TransferRun** (`TransferRun.fs:~499-607`) — **no LogSink**:

| Stage | Where | §13 name |
|---|---|---|
| cdc/spanning gates (G1/G2) | `:515-545` | (Act 0/3 pre-flights) |
| ingest (`Ingestion.collectInOrder`) | `:546-560` | Reading / Checking the data |
| reconcile (`reconcileAgainstSink`) | `:561` | Checking the data |
| build-plan (`DataLoadPlan.build`) | `:566-568` | Building the changes |
| pre-write gate (`validateUserMap`) | `:573-580` | (Act 3 gate) |
| phase-1 bulk + phase-2 FK-update (`writePlan`) | `:582-597` | Making it real (Act 4 — the progress surface) |
| report (`reportKinds`) | `:598-606` | The touch (Act 5) |

**EjectRun** (`EjectRun.fs`) — pure, no LogSink: load-store (`:59-62`) → reconstruct chain
(`:33-44`) → FTC-verify (`:50-54`) → package (`:38-44`). Events land at the CLI boundary `runEject`
(`Program.fs:1066-1079`), today only `printfn`s.

**DriftRun** (`DriftRun.fs:22-34`) — single `Bench.scope` only: read deployed (`:25`) → diff
(`:29-32`). **Missing** the accept/remediate/escalate decision (§5 drift gate) — unbuilt.

**Compose** (`Pipeline.fs:394-456`) — the pure core (`extract → passes → emitters → outputs`);
stage events are emitted by the task wrapper `runWithConfig` (`:1006`), not the pure core.

### 4.2 The LogSink-unification gap

| Run | Report type | Wired? | Landing for LogSink wiring |
|---|---|---|---|
| **MigrationRun** | `MigrationOutcome` (`:69-75`); `MigrationError` (`:26-62`) | ✗ | inside `execute` (`:382-467`): `recordStageEvent` after `preview`(`:385`), each gate(`:405,434`), `executed`(`:447`), verify(`:453`); wrap CLI `runMigrate*` (`Program.fs:1556,1601,1743`) in `withRun` |
| **TransferRun** | `TransferReport` (`:48-61`) | ✗ | inside `runCore` (`:509-607`): after ingest(`:560`), reconcile(`:561`), plan(`:568`), write(`:597`); wrap CLI dispatch in `withRun` |
| **EjectRun** | `EjectPackage` (`:12-26`) | ✗ | EjectRun is Core-pure → emit at CLI `runEject` (`Program.fs:1066-1079`); wrap in `withRun "projection eject"` |
| **DriftRun** | *(returns `PhysicalSchemaDiff`)* | ✗ | needs a drift outcome/decision surface (§5 gate) before it has events worth voicing |

**The pattern to copy:** `FullExportRun.recordStage` (`:48-54`) → `LogSink.recordStageEvent`
(`LogSink.fs:663-674`, which records the stage-table entry AND emits `summary.stageCompleted`).
Cleanest: a **shared helper** (sibling of `Pipeline.emitStageMarker`, `:998-1004`) so the three runs
share one envelope contract, not four drift-prone copies. **Placement:** Migration/Transfer live in
`Projection.Pipeline` (the I/O layer) → LogSink calls go *inside* those modules (like
`Compose.runWithConfig`); EjectRun is Core-pure → its events land at the CLI boundary.

### 4.3 The subscriber / streaming wiring

**Exists** (`LogSink.fs:403-444`): `addSubscriber : (Envelope -> unit) -> unit` (`:429`),
`clearSubscribers` (`:433`), `notifySubscribers` (`:442`, called inside `emit` `:637` and
`runComplete` `:1030`, AFTER the verbosity+mute filter — so a subscriber sees exactly channel-1's
stream). `beginRun` does NOT clear subscribers (attach once, before the run).

**Plan:**
1. **CLI attach** — in `Program.fs main`, when `--pretty`/`--watch` + a TTY, `LogSink.addSubscriber`
   with a closure over a Spectre `Live`/`Status` context BEFORE dispatching the verb (mirrors
   `prettyMode`/`shouldRender`, `Program.fs:171-180`); `clearSubscribers` on teardown.
2. The closure pattern-matches `env.Code`: `*.started` → set stage `⣷` (in-progress);
   `summary.stageCompleted` → `✓ <durationMs>`; `summary.runComplete` → draw the final panel.
3. **Render, never gate** (the `:415-418` contract — the NDJSON write happens first; under
   `--pretty`, channel-1 is already routed to `TextWriter.Null` at `Program.fs:180`, so only the
   subscriber draws).

**The streaming render surface must be added** — `View.fs` has no live variant; `TtyRenderer`
renders post-run only. Add either a `View.StageBoard of (name × Status × trailing) list` variant
(`View.fs:26`) or drive a Spectre `AnsiConsole.Live(...).Start(ctx -> …)` directly in a new
`TtyRenderer.renderWatch`, mutating the table + `ctx.Refresh()` per envelope. Spectre's
`Live`/`Progress`/`Status` APIs are **not referenced anywhere yet** (only `Panel`/`Grid`).

### 4.4 ETA — synthesized, three sources (none wired into stage events yet)

- **Denominator** ("of 300"): from plan totals — `DataLoadPlan` counts (`TransferRun.fs:566`),
  `ChangeManifest` channel counts, or `catalog.Modules` length (already in `extract.completed`
  payload, `Pipeline.fs:1019`).
- **Numerator + elapsed**: there is **no mid-stage progress event today** — `summary.stageCompleted`
  carries only a *terminal* `durationMs`. Add an intra-stage `*.progress`/`stage.applying` envelope
  (`Phase = Progress`) carrying `done`/`total`/`elapsedMs`, emitted from inside the per-table loops
  (`TransferRun.fs:546-597`; `MigrationRun.fs:440-443`).
- **Rate**: `Bench.Stats.MeanMs` per-element (`Bench.fs:90-97`) / `Bench.streamProbe`
  (`Bench.fs:211-242`) → `remaining = (total − done) × meanPerItem`. But the Transfer/Migration loops
  have **zero `Bench.scope`** today — add at the ingest/write sites.
- **Honesty rule** (`THE_VOICE.md:419-420`; Act 4 edge): when no rate can be computed (first run, no
  prior `Bench.Stats`), show the count without an estimate — never a misstating bar.

### 4.5 New stage-identity codes (or extend the existing seam)

The integration doc's preferred form is a `stage.*` namespace (`stage.readingModel`,
`stage.checkingData`, `stage.buildingChanges`, `stage.applying` [the Progress carrier],
`stage.measuring`, `stage.verifying`, `episode.recorded`, `drift.divergence`). **Lower-churn
alternative:** keep the existing `<category>.started`/`.completed` + `summary.stageCompleted{stage}`
codes and extend the `Voice.stageName` map (already the totality-tested seam, already scaffolded in
slice 1). Either reconciles through `Voice.stageName`; the cheaper path extends that one map.

### 4.6 Slice-2 landing summary

- Stage codes + intra-loop progress events: `Pipeline.fs:1006-1041`, `MigrationRun.fs:382-467`,
  `TransferRun.fs:509-607`, `EjectRun`/`Program.fs:1066-1079`, `DriftRun.fs:22-34`.
- LogSink unification: shared `recordStageEvent` helper inside Migration/Transfer; CLI-boundary emit
  for Eject/Drift; route those verbs through `withRun` (`Program.fs:173`).
- New progress payload: extend `summary.stageCompleted`/`stage.applying` (`LogSink.fs:663-674`) with
  `done`/`total`/`elapsedMs` (or a new `recordStageProgress`).
- Subscriber attach: `Program.fs main` (`:171-180`).
- Live render surface (new): `TtyRenderer.renderWatch` + optional `View.StageBoard` (`View.fs:26`).
- Copy: extend `Voice.stageName` to gerund §13 forms + `Voice.all` entries for new `stage.*` codes;
  `VoiceTotalityTests` keeps `code ⇔ copy` total.
- ETA: `Bench.scope`/`streamProbe` at the loops; denominator from `DataLoadPlan`/`ChangeManifest`.

---

## 5 — Slice 3 — aggregate-at-scale + the timeline/ladder

> **Terrain.** Two aggregates already exist and persist; the **View layer reads almost none of them.**
> The §12 scale story is computed-but-unrendered (`LogSink.aggregates()` returns the clusters;
> `buildSummaryView` ignores them). The §8 timeline/ladder reads only `RunLedger.Readiness` (a scalar
> gauge) — nothing from `Episode`/`LifecycleStore`/`ChangeManifest`/`RunHistory`; the few sites that
> touch `EpisodicLifecycle` do so as raw `printfn` (`Program.fs:277-280, 1749-1750`), never via `View`.

### 5.1 The run-scoped aggregate (`LogSink.fs`)

`GroupAccumulator` (`:147-156`) keys on `(Category, Code, SsKey option)` (`RunState.Groups`, `:322`),
accumulating Count, FirstTs, LastTs, first-3 Samples (`updateAccumulator`, `:586-605`).

**Exposed accessors:** `aggregates()` (`:695-702`, sorted **descending by Count** then FirstTs —
already §12 "impact-ranked" at the event-cluster grain), `eventCounts()` (`:705`),
`transformCounts()` (`:733`), `suggestedConfigEdits()` (`:717`), `topSuggestion()` (`:741-756`),
`canaryVerdict()` (`:724`). Two digests built only into the terminal envelope, **not exposed**:
`buildRationaleHistogram` (`:890-907`), `buildSuggestedConfigDigest` (`:916-945`).

**Missing for §12:**
- **Cluster-by-table** (§12 "groups by table before change") — no accessor projects groups →
  per-SsKey totals (`Sales · 412 changes ▸`). Need `clustersByTable() : (SsKey × int) list` folding
  `Groups` on `SsKey` root.
- **Cap-and-name "and N more"** — no group-count-with-remainder accessor. Need
  `topClusters(n) : GroupAccumulator list × int`.
- **Big-rocks by move semantics** (`drops > narrowings > additive > cosmetic`) — `aggregates()` ranks
  by raw event-count, not destructiveness. That rollup lives in `ChangeManifest.Channels`, not
  `LogSink` (§5.2).
- **Humane numbers** (`2,140`) — no formatter; `sprintf "%d"` throughout. Need `Theme.humaneInt`
  (slice 1 added a private `humane` in `Voice.fs` — promote/share it).

### 5.2 `Compose.RunReport` + `ChangeManifest` (the move counts / ‖δ‖)

`Compose.RunReport` (`Pipeline.fs:131-153`) carries `Paths`/`Diagnostics`/`Manifest`/`Trail`/
`PassDiagnostics` — **no channel counts / ‖δ‖** (those ride the displacement leg). The store leg
(`Pipeline.fs:155+`) carries `Displacement : CatalogDiff` + `Manifest : ChangeManifest`.

`ChangeManifest` (`Core/ChangeManifest.fs:13-44`): `Channels : CatalogDiff.ChannelCounts` (19 named
move-channels, `CatalogDiff.fs:931-953`), `SchemaNorm = ‖δ‖`, `CdcCaptureCount`, `RefactorLogRef`,
`ToleranceResidual`, `AppliedTransforms`. Accessors: `between` (`:54-77`), `series` (`:82-90`),
`pathLength` (`:98-101`); `CatalogDiff.channelCounts` (`:955-983`) + `norm` (`:989-995`).

**Missing:** the projection from the 19 flat ints onto the operator's big-rock buckets
(`RemovedKinds+RemovedAttributes+… = drops`; `ChangedAttributes` with a narrowing predicate =
`narrowings`; `Added* = additive`; cosmetic = `ToleranceResidual`). Land
`CatalogDiff.bigRocks : ChannelCounts → { Drops; Narrowings; Additive; Cosmetic }` beside
`channelCounts` (`CatalogDiff.fs:955`), or as a Voice projection.

### 5.3 The durable episode + cross-run series (§8 timeline + ladder)

**Exists:** `Episode` (`Episode.fs:61-85`, Coordinate = Version × Environment × **At : DateTimeOffset**
— the §8 `▸ run 11, just now` marker source); `EpisodicLifecycle` (`:149-244`: `episodes`, `head`,
`latest`, `schemaEvolutionChain`, `netSchemaDiff` — the "runs as dots" sequence); `LifecycleStore`
(durable JSON); `RunLedger` (`LedgerRecord` + **`Readiness`** at `:104-133`: TotalRuns, CanaryRuns,
**ConsecutiveGreen**, LastCanary, **Threshold=10**, **Eligible**); `RunHistory` (chronological `Run`
list: `length`, `latest`, `at index`, **`trend`**, **`canaryHistory`** at `:41-42`, `readiness`).

**`buildReadinessView` today** (`TtyRenderer.fs:76-96`): a Hero (ELIGIBLE / NOT YET), a
`Meter("cutover", ConsecutiveGreen, Threshold)`, optional `Dots("history", recent)`, a run-totals
`Field`, a ledger-path `Note`. Input is `(Readiness, recent: string list, ledgerPath)` — purely
RunLedger-derived; it never sees an `Episode`.

**Gap to §8:**
- **Timeline** — `Dots` (`View.fs:39`, `verdicts: string list`) is too thin: (a) a failed run is not
  *named* (`run 5 failed`); (b) no *present marker* tied to run+time (`▸ run 11, just now`); (c)
  `3 green checks remain` = `Threshold − ConsecutiveGreen` (`toGo` exists at `:77`, just unphrased).
- **Ladder** — the `Meter` + `N of 10` exists; **the one named lever** (`a user-fallback on 3 accounts`)
  has no source — it is the single highest-impact outstanding gate/intervention; Slice 3 must source it
  (from the non-eligibility reason + the blocking move). The largest genuinely-new piece.

### 5.4 The `View` DU mapping

DU (`View.fs:26-60`): `Doc, Panel, Hero, Field, Meter, Dots, Trail, Lane, Disclosure, Note, Action,
Blank`.
- **Cluster by table** → `Disclosure(table)` ⊃ `Lane(move)` (both exist; the *builder* from
  `clustersByTable()` does not). `Disclosure` already renders a `▸ N more` affordance at shallow depth
  (`View.fs:155-158`).
- **Cap-and-name** → `Note("and 1,847 more — Find a table")`.
- **Timeline** → `Dots` enriched (or a sibling `Timeline` case carrying
  `(runId × verdict × isPresent × at) list`) — the one likely DU change.
- **Ladder** → `Meter` + `Action` (the lever).
- **`Surface`** is the right assembly (Statement → Substantiation → Action); `buildGateSurface`
  (`TtyRenderer.fs:141-159`) is the precedent pattern.

### 5.5 New accessors/types needed

1. `LogSink.clustersByTable () : (SsKey × int) list` — fold `Groups` (`:322`) on SsKey root.
2. `LogSink.topClusters (n:int) : GroupAccumulator list × int` — top-n + remainder, over `aggregates()`.
3. `CatalogDiff.bigRocks : ChannelCounts → {Drops;Narrowings;Additive;Cosmetic}` beside `channelCounts`
   (`CatalogDiff.fs:955`); needs a narrowing predicate over `ChangedAttributes`.
4. `Theme.humaneInt : int → string` (thousands separators; share `Voice.humane`).
5. A richer `Dots` payload **or** a `Timeline` View case (`View.fs:39`) — name a failed run, mark the
   present run+time.
6. `buildReadinessView` signature widening (`TtyRenderer.fs:76`) to accept `RunHistory`/
   `EpisodicLifecycle` + a **lever value**; a new accessor naming the single outstanding lever.

**Wiring note:** route the raw-`printfn` `Episode`/`ChangeManifest` consumers (`Program.fs:277-280,
349-350, 1589, 1749-1750`) through new `buildTimelineView`/`buildChangeManifestView` `Surface`s
(precedent: `buildGateSurface`), not extended narration.

---

## 6 — Slice 4 — errors & config as voice + mechanism-1 toViews

### 6.1 PART A — error/config lift inventory

**`configError`/`ValidationError.create` sites** (all `Config.fs`; helper at `:360-361`). Codes are
already structured (`pipeline.config.*`); only the inline `sprintf` **Message** moves to the Voice
catalog (§14 "set-but-invalid → concrete and located"). **These are NOT catch-and-translate** — they
are already coded `Result` errors needing keyed copy:

| Code (`pipeline.config.*`) | Site (Config.fs) |
|---|---|
| `credentialPropertyForbidden` | 440-444 |
| `missingProperty` | 465-466 |
| `nullProperty` | 483-484, 565-566 |
| `typeMismatch` | 487-488, 501-502, 513-514, 525-530, 549-554, 576-577, 591-592, 607-608, 771-772, 803-804, 819-820, 848-850, 887-894, 997-998, 1006, 1015-1028, 1047-1048, 1119-1120, 1148-1154, 1208-1209 (the largest cluster) |
| `nullArrayElement` | 545-546 |
| `renameSourceAmbiguous` / `renameSourceMissing` | 731-734 / 740-743 |
| `jsonInvalid` | 1273-1274 |
| `fileNotFound` (§14 "required → ask") | 1283-1284 |
| `fileReadError` (would-throw, already caught) | 1289-1293 |

Sibling: `pipeline.profiler.connectionMissing` (`Pipeline.fs:925-930`, §14 "required → ask").

**`printErrors` path** (`Program.fs`): `printErrorLine` (`:129-133`, writes `"  [<code>] <message>"`
— **the single wiring point**; today it leaks the code onto the statement line, banned by §10) and
`printErrors` (`:135-136`). The ~48 `printErrors Console.Error` call sites (286, 308, 313, 319, 330,
354, 377, 411, 428, 454, 499, 553, 623, 827, 863, 898, 1005, 1020, 1028, 1130, 1136, 1158, 1164,
1214, 1222, 1275, 1283, 1380, 1384, 1402, 1454, 1469, 1475, 1481, 1508, 1536, 1539, 1547, 1578,
1653, 1665, 1669, 1673, 1681, 1688, 1699, 1723, 1738) each route through the same frame once
`printErrorLine` is voiced — wire it to `Voice.errorSurface` (built in slice 1). The hand-written
header lines immediately above each (e.g. 285, 307, 312, 318, 376, 427, 453, 826, 862) are also
inline prose to voice.

**Catch-and-translate (§14 would-throw → translated):**
- `Pipeline.fs` `invalidOp` at 281, 373, 411, 417, 424, 582 — "unreachable" invariant guards
  (defensive; lower priority).
- `ReadSide.fs` `invalidOp` at 822, 831, 849 (value-codec runtime type), `failwithf` 891 (A39
  invariant), and **`return raise ex` at 965** (re-raises a live SQL-driver exception mid-row-read —
  **the real target**; surfaces during transfer/verify-data/migrate-execute/drift as a stack trace →
  §10 "Cannot reach the server" / "ALTER permission denied").

**Env-var reads (§14):**
- `PROJECTION_ALLOW_EXECUTE` (`Program.fs:834, 1523, 1629`; refusal copy inline at 837-838,
  1524-1525, 1630-1631 — §14 "required → ask").
- `PROJECTION_LEDGER_DIR` (`RunLedger.fs:83`) + `PROJECTION_RUNS_DIR` (`Run.fs:111`) — read silently;
  absence is §14 "optional-unset → recommendation" (the exact `THE_VOICE.md` §14 example). Surfaced
  inline at `Program.fs:1095`.
- Docker-unavailable copy inline at `Program.fs:465, 511-512, 572-573` (§14/§10).

**Argu `IArgParserTemplate.Usage`** (operator help to voice): `FullExportArgs.fs:37-70`,
`TransferArgs.fs:30-62`, `VerifyDataArgs.fs:22-31`; the hand-rolled top-level help `usageLines`
(`Program.fs:23-109`, incl. the exit-code legend 99-108).

**Placement:** the flat code-keyed catalog (mechanism 2) in the CLI `Voice` module; `printErrorLine`
+ the Argu `Usage` members look copy up by code/arg-id. Config codes stay in Pipeline; their copy
declaration is harvested into the catalog (declare-at-site is satisfiable — Config.fs is Pipeline,
not pure-Core). The `code ⇔ copy` test covers every `pipeline.config.*`/`transfer.*`/`migrate.*`.

### 6.2 PART B — mechanism-1 typed toView landing map

For each move/gate/proof carrier: the DU, its existing terminal projection (the
`RemovalReason.toDiagnosticString` precedent), and where the `toView`/`toSurface` companion lands —
respecting **F#-pure-core** (no operator prose in `Projection.Core`).

| Surface | Carrier DU (file:line) | Existing terminal proj. | Companion lands | Rationale |
|---|---|---|---|---|
| **§4 moves (lineage)** | `TransformKind` (`Lineage.fs:228`) + payloads `RemovalReason` (`:26`), `AnnotationDetail` (`:109`), `PhysicalRename` (`:171`), `ColumnRename` (`:204`) | `*.toDiagnosticString` (`Lineage.fs:47/144/186/219`) | beside `EventProjection.transformKindRender` (`EventProjection.fs:46-54`) | Pipeline boundary; already projects these DUs; keeps prose out of Core |
| **§4 moves (diff)** | `ChannelCounts` (`CatalogDiff.fs:931`) | `channelCounts` (`:955`, counts not copy) | EventProjection (new `channelsToView`); replaces inline render at `Program.fs:1347-1365` | Pipeline; §12 big-rocks |
| **§5 gates** | `Preflight.GateLabel` (`Preflight.fs:383`) / `GateRefusal` (`:447`) | `Preflight.labelText` (`:398`) | **`TtyRenderer.buildGateSurface` already exists** (`TtyRenderer.fs:141`) — extend to all 8 labels (today only `UndeclaredDestructiveChange` gets distinct §5 copy; the other 7 collapse) | CLI; `View`/`Surface` are CLI types; the model is built |
| **§6 canary proof** | `PhysicalSchemaDiff` via `canaryEnvelopes` (`EventProjection.fs:232-258`) | `canaryEnvelopes` (→ envelope) | beside `canaryEnvelopes` (new `canarySurface`); replaces inline at `Program.fs:542-550` | Pipeline; one source, two channels |
| **§6 verify / CDC** | `MigrationOutcome.Verified`/`SchemaDiff` (`MigrationRun.fs:69-75`), `cdcDelta` (`:473`) | none (inline only) | EventProjection (new `verifySurface`/`cdcSilenceSurface`); replaces `Program.fs:1587-1607` | Pipeline; payload-shaped §6 statement |
| **flat errors/config/help (PART A)** | `ValidationError.Code` (`Result.fs:11`); Argu `Usage` | `printErrorLine` (`Program.fs:129`, leaks code) | **CLI `Voice` catalog** keyed by code (mechanism 2) | code-keyed catalog |

**Inline prose to retire** (the conflation sites): `reportPreviewOutcome` (`Program.fs:1347-1365`, the
`migrate` move surface), `reportMigrationError` (`:1433-1458`, leaks `REFUSED`/`%A` — banned §2.2),
`reportPreviewOutcome` (`:1330-1367`), `runCanary` (`:542-550`), the verify narration (`:1587-1610`).

**Pure-Core note:** no pure-Core *pass* needs a `Voice.<Pass> ↔ <Pass>` companion in slices 1–4 — the
move/gate/proof carriers are all already boundary-projected (Lineage payloads via EventProjection) or
live in Pipeline/CLI (CatalogDiff channels, Preflight, MigrationRun). The 1:1 projection-layer
companion (the resolved Core-purity form) is the **slice-5** `DiagnosticEntry.Message` lift.

---

## 7 — Slice 5 — the Diagnostics lift (deferred)

A typed `DiagnosticPayload` DU replacing the inline `sprintf` `Message` on `DiagnosticEntry`
(`Diagnostics.fs:70-78`), with the pass prose moved to keyed copy in a **1:1 projection-layer
companion** per pure-Core pass (`Voice.NullabilityPass ↔ NullabilityPass`). **Deferred behind a real
consumer** (`THE_VOICE_INTEGRATION.md` §8 decision 4; IR-grows-under-evidence). The diagnostic subcode
families (`tightening.*`, `profiling.*`, `structural.*`, `selection.*`, `adapter.osm.*`, `tableId.*`)
are the code space this slice voices. Largest, last, optional.

---

## 8 — The test blast-radius (UPDATE vs DO-NOT-BREAK)

The Voice layer changes operator **copy**, not **codes**. Two disjoint test sets:

### 8.1 UPDATE (copy assertions — change with the slice that voices them)

| File:line | Asserted string | Slice |
|---|---|---|
| `TtyRendererTests.fs:41-44` | `"SUCCEEDED"`, `"green"` | **1 (DONE — now `"matches the model"`)** |
| `TtyRendererTests.fs:53-54` | `"FAILED"`, `"RED"` | **1 (DONE — now `"diverged"`)** |
| `TtyRendererTests.fs:78,89` | `"ELIGIBLE"`, `"NOT YET"`, `"10 / 10 green"`, `"3 green run"` | 3 (readiness board → §8 ladder) |
| `TtyRendererTests.fs:114-127` | `"destroys structure"`, `"undeclared destructive change"`, `"--declare-loss"`, `"connection unavailable"` | 4 (gate surface → §5 mechanism-1) |
| `ComparisonTests.fs:41-42,57,66,75` | `"catalog"`, `"norm"`, `"identical"`, `"destroy"`, `"nothing destroyed"` | 4 (Act-2 diff copy → register; note `"nothing destroyed"`/`"destroy"` currently **break the banned list** and must be revoiced) |
| `ViewTests.fs:39-114` | `"ELIGIBLE"`, `"green"`, `"FAILED"`, `"Details"`, `"2 more"`, rename items | mostly View-substrate literals (test-owned) — confirm, likely no change |

### 8.2 DO-NOT-BREAK (code/structure assertions — the NDJSON contract; must stay stable)

| File:line | Asserted | Why |
|---|---|---|
| `LogSinkTests.fs:70,299-300,334,340` | `"config.runStart"`, `"summary.runComplete"`, `"end"`, `"extract"`, `"ssdt"` | envelope codes / phase / stage / artifact-kind |
| `LogSinkSubscriberTests.fs:50,70,80,88,103` | `["config.runStart";"extract.started"]`, `"profile.started"`, `"emit.started"`, `"summary.runComplete"` | event-code sequence |
| `LogSinkVerbosityTests.fs:56,71,86,109,140-141,165` | level sets, codes `"c.i"`/`"e.i"` | verbosity filtering |
| `EventProjectionTests.fs:41-129` | `"transform.applied"`/`.declined`/`.lineage`/`.diagnostic`/`.registered`, classification payloads, code sequence | transform event contract |
| `FullExportCliTests.fs:179-181,278-281,315` | `"config.runStart"`, `"summary.runComplete"`, `"end"`, `"config.validationFailed"`, `"failed"`, `"summary.stageCompleted"` | run-sequence codes + outcome enum |
| `ConfigTests.fs:40-386` | paths, module/entity names, policy enums (`"IncludeAll"`, `"ByEmail"`, …) | config schema contract |
| `RunTests.fs:70-73` | `"succeeded"`, `"digest123"`, counts | Run aggregate contract |

**Rule of thumb:** any assertion on a dot-separated `code`, a `phase`/`level`/`category`/`stage`/
`outcome` enum, or a payload count is DO-NOT-BREAK. Any assertion on a human sentence
(`"SUCCEEDED"`, `"identical"`, `"connection unavailable"`) is UPDATE.

---

## 9 — Open decisions & guardrails

**Resolved** (`DECISIONS 2026-06-06 (later)`): Core-purity → 1:1 projection-layer companion; the
`Surface` rename is done.

**Still open / to settle in the slice that surfaces them:**
- **The stage-code form** (slice 2): the `stage.*` namespace vs. extending `Voice.stageName` over the
  existing `<category>.started/.completed` codes. (Recommendation: extend the existing seam — lower
  churn, already totality-tested.)
- **The §8 lever source** (slice 3): the single outstanding intervention naming the "one item remains"
  — the largest genuinely-new piece; needs a `RunLedger`/`Readiness` accessor that names the blocking
  gate/move.
- **`Dots` enrichment vs a `Timeline` View case** (slice 3): the one likely `View` DU change.
- **DriftRun's decision surface** (slice 2/4): the accept/remediate/escalate §5 gate is unbuilt — Drift
  has no events worth voicing until it exists.

**Guardrails (every slice):** the twelve rules + banned list over every string (the
`VoiceTotalityTests` guard fails the build on a violation); codes never change (only copy);
declare-at-site / harvest-centrally; `code ⇔ copy` totality; IR grows under evidence (no copy for
latent surfaces ahead of their events); pure-Core holds (operator prose never enters
`Projection.Core` — the 1:1 companion is the form when a pure-Core pass is voiced).

---

*Recorded for the executing agent. This is the file:line map; `THE_VOICE_INTEGRATION.md` is the plan,
`THE_STORYBOARD.md` the surface, `THE_VOICE.md` the register. Read this to know exactly where each
slice lands; read the others to know what it must say. Hold the voice exact.*
