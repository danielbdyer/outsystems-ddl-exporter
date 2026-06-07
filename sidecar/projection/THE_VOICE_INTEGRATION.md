# THE VOICE — wiring it into the codebase (a decomposition & plan)

> **Update (2026-06-07) — the operator surface shipped under `THE_CLI.md`.** Slices 0/1/2/4 of
> this plan have landed (the `Voice` seam + `code ⇔ copy` totality, the streaming Watch + dwell
> floor, errors/§5-gates as voice), and the CLI was **re-envisioned to four verbs**
> (`project` / `check` / `explain` / `seal` over one `MovementSpec` — `THE_CLI.md`, shipped
> 2026-06-07). The run-type framing below (§3.1 — `FullExportRun` / `MigrationRun` / …) now
> describes **engine faces behind the one `MovementSpec` executor**, not the dispatch surface; the
> voice machinery and the event spine are unchanged. For the current execution detail and the old
> verb → new face mapping, read **`THE_VOICE_BUILD_MAP.md` §0.5** (the reconciliation) +
> `THE_CLI.md` §7/§13. This document stays canonical for the *decomposition and the locked
> decisions* (§8).

**Status: a plan, not a change.** Nothing here is built yet. This document
decomposes the concerns around making `THE_VOICE.md` a structural part of the
engine — *so the copy reads as if it had always been there* — and sequences the
work. It is grounded in the codebase as it stands today (file:line throughout),
so the plan lands inside the existing grain rather than beside it. **The five
design forks were resolved with the operator on 2026-06-06 — see §8.**

Read `THE_VOICE.md` first (the copy itself) and `THE_INSTRUMENT.md` (the why).
This is the *how it attaches*.

---

## 0 — The one finding

The architecture you're worried about building **already exists, three times
over.** The engine already emits structured events at their sites, already
aggregates them at run boundaries, and already renders them to a TTY surface.
What it does *not* yet have is a single rule about *where the words live* — and
in one place (`Lineage`) it gets that rule right, while in others (`Diagnostics`,
config, exceptions) it conflates prose with logic exactly as you feared.

So this is not a new subsystem. It is **completing a pattern the codebase
already demonstrates**, and naming it as a discipline so it stops drifting.

---

## 1 — The conflation, resolved

Your tension, stated precisely:

> "We ought to declare most of this text near the sites of emission, but that
> seems like a conflation of business logic and observation."

Both halves are true, and the resolution is already in the tree. Compare the two
patterns the codebase runs side by side:

- **`Lineage` — the pattern we want.** A pass emits a `LineageEvent` whose
  `TransformKind` is a **closed DU** (`Renamed | Removed of RemovalReason | …`)
  carrying structured payload and a `Classification` (`DataIntent |
  OperatorIntent`) — *no prose* (`src/Projection.Core/Lineage.fs:287‑293`). The
  words appear only at a terminal boundary: one `RemovalReason.toDiagnosticString`
  per payload type (`Lineage.fs:44‑59`), consumed by `EventProjection` at egress
  (`src/Projection.Pipeline/EventProjection.fs:46‑54`). **Business logic emits
  meaning; copy is a projection of it.**

- **`Diagnostics` — the conflation.** A pass builds a `DiagnosticEntry` carrying
  a structured `Code` (`tightening.nullability.mandatory.nulls`) *and* a
  free-text `Message` authored inline via `sprintf`
  (`src/Projection.Core/Diagnostics.fs:70‑78`). The `Code` is the event; the
  `Message` is the prose welded to the logic — the thing you can feel is wrong.

**The resolution:** the *event* is declared at the site (it's data — local,
typed, discoverable), and the *copy* is declared once at the voice boundary,
**keyed by the event's `Code`**. The seam between them already exists: `Code` is
a field on `ValidationError` (`src/Projection.Core/Result.fs:10‑14`),
`DiagnosticEntry`, and `LogSink.Envelope`. The plan is largely *"stop authoring
the `Message` at the site; key it off the `Code` at the boundary."*

This is the whole idea, in one line:

> **A site emits a code; the voice owns the copy. The code is near the logic; the
> copy is near nothing but the other copy.**

---

## 2 — The three layers (and where they already live)

| Layer | What it is | Already in the codebase as… |
|---|---|---|
| **Event** | A typed fact — *what happened* — with a stable `Code` and structured payload, no prose. | `LogSink.Envelope` (`Code`/`Category`/`Phase`/`SsKey`/`Payload` — `LogSink.fs:129‑141`); `LineageEvent` (`Lineage.fs:287`); `DiagnosticEntry.Code` (`Diagnostics.fs:70`); `ValidationError.Code` (`Result.fs:10`). |
| **Aggregate** | A run-scoped collector that folds many events into a reportable whole — the *domain aggregate*. | `GroupAccumulator` / `RunAccumulator` ((category,code,ssKey) → count + samples, `LogSink.fs:619‑656`); `Compose.RunReport` (`Pipeline.fs:131‑153`); `RunLedger` (cross-run series); **`Episode`** (`Episode.fs:61‑128`) + `LifecycleStore` (`LifecycleStore.fs:29‑96`) — the durable one. |
| **Voice** | The projection `Code → operator copy`, keyed centrally. **`THE_VOICE.md` is this layer's spec.** | `View` DU (`View.fs:19‑60`); `Surface` statement/substantiation (`Surface.fs:8‑16`); `TtyRenderer.buildSummaryView` reading LogSink+Ledger (`TtyRenderer.fs:24‑96`); `Comparison.Render` — the one existing *keyed* domain→View renderer (`Comparison.fs`). |

The triangle is whole *structurally*. The defects are: (a) the Voice layer is
thin and partly inline; (b) some Events carry prose instead of just a code; (c)
some stages emit no event at all. The plan addresses exactly those three.

---

## 3 — The terrain today (grounded)

### 3.1 Run types & their lifecycle stages

Six run types, each an explicit *sequence of orchestration calls* (no state-machine
enum — the stages are implicit in the call order):

| Run | Result type | Stages (the lifecycle the operator lives) |
|---|---|---|
| **FullExportRun** (`FullExportRun.fs`) | `RunOutcome` (`Succeeded\|ConfigInvalid\|RunFailed\|Aborted`, `:29‑33`) | config-validate → compose(extract→profile→pass-chain→emit) → store-leg(diff→record) → diagnostics → artifacts → terminal summary (`:130‑232`) |
| **MigrationRun** (`MigrationRun.fs`) | `MigrationOutcome` (`:69‑75`) | diff-plan → refuse-gates → emit-DDL → emit-refactorlog → execute → CDC-measure → verify-round-trip → record-episode (`:375‑531`) |
| **TransferRun** (`TransferRun.fs`) | `TransferReport` (`:39‑61`) | parse-spec → ingest → build-plan → phase-1 bulk → phase-2 FK-update → reconcile-unmatched → report (`:326‑580`) |
| **EjectRun** (`EjectRun.fs`) | `EjectPackage` (`:12‑26`) | load-store → reconstruct chain → FTC-verify → package (`:33‑65`) |
| **DriftRun** (`DriftRun.fs`) | — | **stub** 🔴 (the P‑8 surface is unbuilt) |
| **Compose** (`Pipeline.fs`) | `RunReport` (`:131‑153`) | pure IR: extract → passes → emitters → outputs |

These stage names *are* §13 of `THE_VOICE.md` ("Reading the model / Checking
the data / Building the changes / Verifying the round-trip"). The mapping is direct.

### 3.2 How runs report today — two channels

- **Channel 1 — `LogSink` NDJSON to stderr (always on).** Structured envelopes
  at run boundaries: `config.runStart`, `summary.stageCompleted`,
  `transform.applied`/`.declined`/`.lineage`, `summary.runComplete`. The
  `GroupAccumulator` rolls hundreds of envelopes into per-(category,code,ssKey)
  counts+samples (`LogSink.fs:619‑656`). **This is already the Event+Aggregate
  spine at scale — it just isn't voiced.**
- **Channel 2 — `TtyRenderer` + `View` (opt-in `--pretty`).**
  `buildSummaryView` reads the LogSink state + `RunLedger` into a verdict panel
  (`TtyRenderer.fs:24‑96`); `View` renders identically to pretty / plain / JSON
  (`View.fs`). This is the Voice surface — today thin (outcome + canary +
  transform counts).

### 3.3 Where prose lives today

| Source | `Code` (typed)? | `Message` (prose)? | Verdict |
|---|---|---|---|
| `Lineage` events | ✅ closed DU + Classification | only at one `toDiagnosticString` boundary | **already right** |
| `Diagnostics` | ✅ dot-coded | ❌ inline `sprintf` at the pass | lift to keyed copy |
| `ValidationError` (incl. `configError`) | ✅ `pipeline.config.*` etc. | ❌ inline `sprintf` at site (`Config.fs:360+`) | lift to keyed copy |
| Exceptions (`invalidOp`/`failwith`) | ❌ none | ❌ inline, developer-facing | translate at boundary |
| Argu CLI help / usage | DU-typed surface | ✅ inline `IArgParserTemplate.Usage` | voice the operator-facing strings |
| `LogSink.Envelope` | ✅ `Code`/`Category`/`Phase` | payload, not prose | **already right** — ready to voice |

### 3.4 The aggregation points already present

`Compose.Outputs` (`Pipeline.fs:45‑121`) · `Compose.RunReport` (`:131‑153`) ·
`RunOutcome` (`FullExportRun.fs:29`) · `LogSink.RunAccumulator` (the big fold) ·
`RunLedger` (cross-run → readiness) · `ChangeManifest` (the diff displacement) ·
`MigrationArtifacts` (`MigrationRun.fs:17‑23`) · **`Episode`** (the durable,
five-plane aggregate). Your "domain aggregate for effective reporting" is
**`Episode` for the durable story and `RunAccumulator` for the live one** — both
exist; neither is fully voiced.

---

## 4 — Decomposing the four concerns you named

**(a) Lifecycle events tracking pipeline command states.** Each run's stage
sequence (§3.1) becomes a closed `Code` vocabulary on `LogSink.Envelope`
(`stage.readingModel`, `stage.checkingData`, `stage.building`, `stage.selfCheck`,
`episode.recorded`, …). Stages that already emit `summary.stageCompleted` just
need a stage-identity code; stages that emit nothing get instrumented (§6). The
copy (`THE_VOICE.md` §13) is keyed off those codes at the Voice layer — never at
the orchestrator.

**(b) Errors bubbling from their sites.** The `Code` already rides on
`ValidationError`/`DiagnosticEntry` from the site. The fix is to *stop* carrying
the operator `Message` from the site — the boundary (`printErrors` →
`TtyRenderer`) looks the copy up by `Code` (`THE_VOICE.md` §10/§14). Exceptions,
which carry no code and are developer-facing, get **caught and translated** at
the boundary into a coded event (§14 "would-throw → caught and translated").

**(c) Config / setup concerns.** `configError` already produces structured codes
(`pipeline.config.typeMismatch`, …, `Config.fs:466+`); env-var reads
(`PROJECTION_*`) currently warn or fail implicitly. These map onto
`THE_VOICE.md` §14 (unset→invitation, missing→ask, invalid→located,
would-throw→translated). Same lift: code at site, copy at voice.

**(d) Aggregation for reporting.** The domain aggregate is the seam where copy
becomes *coherent at scale* (`THE_VOICE.md` §12 — big-rocks-first, cluster,
cap-and-name). `RunAccumulator`/`GroupAccumulator` already cluster by
(category,code,ssKey); the Voice layer reads that aggregate (not the raw event
stream) so the surface is one calm screen whether 3 or 3,000 events fired. The
`Episode` is the durable aggregate the timeline/ladder (`THE_VOICE.md` §8) reads.

---

## 5 — The Voice catalog (the one design fork worth deliberating)

Everything above converges on one question: **what does the `Code → copy` map
physically look like, and where does it live?** Three candidate shapes, with the
recommendation and the trade:

1. **Typed-DU projection (the `Lineage` shape), per domain event.** For
   *structured* events whose payload shapes the wording (a removal, a narrowing,
   a re-key), the copy is a `toView`/`toSurface` function next to the DU — like
   `RemovalReason.toDiagnosticString`, but returning a `View`/`Surface` instead
   of a string. **Best for the eleven moves and the gates** (`THE_VOICE.md`
   §4/§5): the payload is rich and the copy varies with it.

2. **Code-keyed catalog, for flat codes.** For the *many* flat codes (config
   errors, stage events, setup), a single `Voice` module in the CLI layer maps
   `Code → copy template` (statement + substantiation + action), filled from the envelope's
   `Payload`/`Metadata`. **Best for §10/§13/§14** — high-volume, low-variance
   strings that want one catalog, not one function each.

3. **Hybrid — CHOSEN (see §8, decision 1).** Use (1) for the move/gate/proof
   surfaces where copy is payload-shaped, and (2) for the lifecycle/error/config
   codes where copy is template-shaped. **Placement (decision 2): sites *declare*
   their copy, *harvested* centrally** — the `TransformRegistry` pattern. The
   declaration is owned by and adjacent to the site, but a **separable
   declarative value**, never `sprintf` in control flow. It lives as close as the
   F#-pure-core commitment allows: in-module at boundary/pipeline/CLI sites; a 1:1
   projection-layer companion (`Voice.NullabilityPass ↔ NullabilityPass`) for
   pure-Core passes. The typed move/gate `toView`s may sit beside their Core DUs
   under the `RemovalReason.toDiagnosticString` terminal-projection precedent.

**The totality guarantee.** The `TransformRegistry` is already the canonical
enumeration of transformation sites (`TransformRegistry.fs:25‑115`, with
`StageBinding` + `TransformSite` + `Classification`) and is held honest by the
`registered ⇔ executed` property test. The Voice layer gets the **same
treatment**: a `code ⇔ copy` totality test — every `Code` the engine can emit has
a Voice entry, and every Voice entry maps to a reachable `Code`. That test is
what makes the copy feel native: it can't drift from the events, by construction.

---

## 6 — Gap inventory (what's missing today)

**Bucket A — inline prose to lift (conflation sites):**
- `DiagnosticEntry.Message` — no typed payload DU; prose authored at each pass.
  *(Largest lift; defer behind a real consumer per the IR-grows-under-evidence
  discipline.)*
- `configError` messages (`Config.fs:360+`) — codes good, messages inline.
- Argu `IArgParserTemplate.Usage` — operator help authored inline.

**Bucket B — sites that emit no event yet:**
- Per-stage boundaries beyond `summary.stageCompleted`: no stage-identity code,
  so a live "Watch" view (`THE_VOICE.md` §13) can't name the current stage.
- `MigrationRun` / `TransferRun` / `EjectRun` emit reports but **don't flow
  through `LogSink`** the way `FullExportRun` does — so they have no envelope
  stream to voice. (Unify them onto the envelope spine.)
- `DriftRun` is a stub — P‑8's surface (`THE_VOICE.md` §5 drift gate, §7) has no
  events to render.
- Live progress / ETA (`THE_VOICE.md` §13 Watch): no streaming stage events;
  today it's terminal-summary only.

**Bucket C — aggregation present but unvoiced:**
- `RunAccumulator`'s clustered counts exist but `buildSummaryView` surfaces only
  outcome+canary+transform counts — the scale story (§12 big-rocks/cluster/cap)
  isn't rendered.
- `Episode`/`LifecycleStore` exist but the timeline + ladder (`THE_VOICE.md` §8)
  read nothing from them yet.
- `ChangeManifest` channel counts aren't rendered as the §4 move surfaces.

---

## 7 — The slice sequence (incremental, no big bang)

Ordered so each slice ships value and nothing waits on a grand refactor:

- **Slice 0 — name the discipline.** A `DECISIONS.md` entry: *Event / Aggregate /
  Voice separation — a site emits a coded event **and declares its copy as a
  separable, harvested value** (the registry pattern); inline prose welded to
  control flow is forbidden; the renderer consumes the harvested catalog keyed by
  code; `code ⇔ copy` totality holds.* Add the Operating-disciplines table row in
  `CLAUDE.md` (Voice as the concern-shaped, write-side-free sibling of
  Lineage/Diagnostics/Bench/Registry). *This is the move that makes it "always
  have been there"* — the rule exists before the code does, like every other
  load-bearing commitment. (Doc-only.)
- **Slice 1 — the Voice seam + the totality test.** Stand up the `Voice` module
  (hybrid §5) keyed by the codes that **already** exist (`config.runStart`,
  `summary.*`, the `pipeline.config.*` errors). Wire `TtyRenderer` to look copy
  up by code. Add the `code ⇔ copy` coverage test. No new events yet — just
  voice what's already emitted, derived from `THE_VOICE.md`.
- **Slice 2 — lifecycle events (streaming; scope raised by decision 3).** Give
  each stage a code; unify Migration / Transfer / Eject onto the `LogSink`
  envelope spine; add the **streaming render path** for live per-stage progress +
  ETA (§13) — not just a terminal summary. Surfaces P‑5/P‑6 verdicts.
- **Slice 3 — aggregate-at-scale.** Render `RunAccumulator`'s clusters as the §12
  surface (big-rocks-first, cluster-by-table, cap-and-name); read `Episode`/Ledger
  into the timeline + ladder (§8). Surfaces the readiness story.
- **Slice 4 — errors & config as voice.** Lift `configError`/`ValidationError`
  copy to the catalog; catch-and-translate exceptions at the boundary (§10/§14).
- **Slice 5 — Diagnostics lift (deferred).** Introduce a typed `DiagnosticPayload`
  DU and move pass prose to keyed copy — **only when a consumer demands it**, per
  IR-grows-under-evidence. Largest, last, optional.

`THE_VOICE.md` is the acceptance reference for slices 1‑5: each rendered surface
must match a section of the doc.

---

## 8 — Decisions (locked 2026-06-06)

All five forks resolved with the operator. These supersede the recommendations in
§5/§7 where they differ.

1. **Catalog shape — Hybrid.** Typed `toView` projections for the payload-shaped
   surfaces (the eleven moves, the gates, the proofs — copy varies with the
   payload); a code-keyed declarative catalog for the flat high-volume codes
   (lifecycle / errors / config). Each mechanism where it fits.

2. **Placement — sites carry words, via *declare-at-site / harvest-centrally*.**
   The operator's standing hunch ("declare the text near its site") wins — in the
   disciplined form the codebase already runs for the `TransformRegistry`: each
   site **declares** its copy as a *separable declarative value* (deletable
   without touching control flow), a `Voice.all` **harvests** them into one
   catalog, the renderer consumes the harvest, and a `code ⇔ copy` totality test
   keeps it complete. This is **not** prose welded into control flow (`sprintf`
   mid-fold) — that stays forbidden. So Voice is concern-*shaped* (declared at
   sites, harvested, totality-tested — a sibling of Lineage / Diagnostics / Bench
   / Registry) yet has **no runtime write side**: the declarations are static copy
   data, projected at render. It is the registry pattern applied to words.
   - **Core-purity consequence + recommendation (reversible).** The *event* (code
     + typed payload) is emitted at the site, pure. The *copy declaration* lives
     as close as the F#-pure-core commitment allows — **in-module** for boundary /
     pipeline / CLI sites; a **1:1 projection-layer companion** for each pure-Core
     pass (`Voice.NullabilityPass ↔ NullabilityPass`), so it stays owned +
     adjacent + harvested without polished operator prose entering
     `Projection.Core`. Typed move/gate `toView`s may sit beside their Core DUs
     under the `RemovalReason.toDiagnosticString` terminal-projection precedent.
     *If the operator prefers the declarations literally inside the Core pass
     modules, this rule flips in one line — F#-pure-core is the only thing weighing
     against it.*

3. **Live surface — streaming Watch from the start.** Per-stage live progress +
   ETA (`THE_VOICE.md` §13) is in scope for the first build, not deferred. This
   *raises slice-2 scope*: unify Migration / Transfer / Eject onto the `LogSink`
   envelope spine, add stage-identity codes, and add a streaming render path —
   not just a terminal summary.

4. **Diagnostics lift — deferred** behind a real consumer (slice 5, optional), per
   IR-grows-under-evidence.

5. **Catalog location — distributed, not central** (resolved by decision 2). The
   catalog is per-site declarations harvested into one logical surface, organized
   1:1 with sites and mirroring `THE_VOICE.md`'s sections at the aggregate — the
   doc and the harvested catalog stay isomorphic.

---

## 9 — Why this will read as if it were always here

- It **completes a pattern the codebase already runs** (`Lineage`'s
  event-at-site / copy-at-boundary), rather than importing a new one.
- It reuses the **existing event spine** (`LogSink`), the **existing aggregates**
  (`RunAccumulator`/`Episode`), and the **existing surface** (`View`/`Surface`/
  `Comparison`) — no parallel machinery.
- It is **named as a discipline first** (slice 0), the way every load-bearing
  commitment in this codebase was — so future code is born compliant.
- It is **held honest by a totality test** (`code ⇔ copy`), the same device that
  keeps `registered ⇔ executed` true — so the copy can't drift from the events.
- The catalog is **isomorphic to `THE_VOICE.md`** (decision 5), so the doc is the
  map of the code and vice-versa.

The result: the words stop being something we *add* to the engine and become
something the engine *projects* — which is exactly what `THE_INSTRUMENT.md` means
by "the instrument disappears."
