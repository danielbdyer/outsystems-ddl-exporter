# Handoff — the operator-facing voice & the storyboard (2026-06-06)

> **2026-06-08 — read the addendum first.** The anchor set below is still the register,
> but the world it described ("an un-started build" on a 16-verb CLI) is two
> re-envisionings out of date. The voice machinery is **built and proven**; the CLI is now
> the **flows surface** (`projection <flow>`); the live work is **wiring the register into
> the rendered TTY**, mapped wave by wave in the new **`THE_VOICE_BACKLOG.md`**. Skip to
> [§ The 2026-06-08 addendum](#the-2026-06-08-addendum--what-actually-faces-you) below, then
> use the original letter as the register-orientation it still is. — *the outgoing agent*

*To the next agent picking up the dynamic-display / voice work.*

You're inheriting a **complete, locked anchor set** for how the instrument speaks and what it shows —
and an **un-started build**. Everything here lives on the `claude/dynamic-display-surface-voice-…`
branch; none of it is in the engine yet. Your job is to execute the build the anchor set specifies, in
the order it specifies, *without re-litigating the register* — it was settled with the operator one
rule at a time, including several corrections you would otherwise re-introduce by instinct.

## Where you are in the spine

The estate has a target (`THE_USE_CASE_ONTOLOGY.md`), a proven engine (per the 2026-06-02 debrief,
`migrate` / `transfer` / verify are LIVE — the engine already *proves* a migration end to end), and
now a **settled voice and surface design**. What is unbuilt is almost entirely **the voice and the
surfaces**: the words themselves, the streaming Watch, the record line, the timeline / ladder, and the
depth layer. That gap is your work. `THE_STORYBOARD.md` §7 is the map of exactly what's LIVE vs LATENT.

## Read these, in this order (≈30–40 min)

1. **`THE_VOICE.md`** — the register. The **twelve rules (§1)** are non-negotiable and were settled
   word by word; do not soften them. Internalize §1 + the banned list (§2.2) before writing a single
   string. The verdicts (§3), moves (§4), gates (§5), proofs (§6), errors (§10), config (§14) are your
   worked examples — *derive, don't invent*.
2. **`THE_STORYBOARD.md`** — the surface, scene by scene. Nine acts × six streams × positive/negative/
   edge (§3); the per-verb call sheets (§4); the concern-movement field (§1); the **build-readiness
   map** (§7 — LIVE/PARTIAL/LATENT, so you build the LATENT); the **P-6 worked proof** (§8 — the
   granularity target, frame by frame).
3. **`THE_VOICE_INTEGRATION.md`** — the build plan. The slice sequence (§7) and the locked decisions
   (§8).

Then skim `DECISIONS 2026-06-06` (the resolved-questions record) and the new `CLAUDE.md` operating-
disciplines row ("Operator-facing voice register").

## What's decided — do not re-open without the operator

- **The register** — authoritative / scientific / mature / humble, under *evidential literalism*; the
  twelve rules; **rule 12 (stative, agentless)** — report states and events, never actions performed
  ("Model read complete", not "Read the model"), gerunds-in-progress excepted. "self-check" was
  renamed **"round-trip verification"** (the "self" reintroduced a subject).
- **Catalog shape** — hybrid (typed `toView` for payload-shaped moves/gates/proofs; a code-keyed
  declarative catalog for flat lifecycle/error/config codes).
- **Placement** — sites carry words via **declare-at-site / harvest-centrally** (the `TransformRegistry`
  pattern), *not* prose welded to control flow. Voice is concern-shaped; it has **no runtime write
  side**.
- **Live surface** — streaming Watch from the start (this raises slice-2 scope).
- **Defer** — the Diagnostics lift (a typed `DiagnosticPayload` DU) behind a real consumer (slice 5).
- **"dig"** retired from the voice vocabulary → "the statement" / "the substantiation" / "Show detail".
  The vision thesis ("one essence, infinitely diggable") is kept.

## Your first action

**Slice 0 is already recorded.** The Event / Aggregate / Voice separation discipline landed this
session in `DECISIONS 2026-06-06` + the `CLAUDE.md` row. **Start at slice 1** (`THE_VOICE_INTEGRATION.md`
§7): stand up the `Voice` seam keyed by the codes that **already exist** (`config.runStart`,
`summary.*`, the `pipeline.config.*` errors), wire `TtyRenderer` to look copy up by code, and add the
`code ⇔ copy` totality test. No new events yet — just voice what's already emitted, derived from
`THE_VOICE.md`. Use `THE_STORYBOARD.md` §7 to know which codes are LIVE (so the totality test has real
events to cover) and which surfaces are LATENT (so you don't author copy for events that don't fire
yet — IR grows under evidence).

## What's NOT decided — yours to resolve, then record

- **The Core-purity sub-call** (`THE_VOICE_INTEGRATION.md` §8 decision 2): may the per-site copy
  declarations live *literally* inside the Core pass modules, or in a 1:1 projection-layer companion?
  The recommendation is the companion (keeps `Projection.Core` free of polished prose); flipping it is
  one line, weighed only against the F#-pure-core commitment. Settle this in slice 1 — it sets the
  pattern — and write it into `DECISIONS` when you do.
- **The `Surface.fs` code rename** (`essence` / `dig` → `statement` / `substantiation`): the voice docs
  use the new names; the code still uses the old. A small code change — land it when you next touch
  `Surface`.

## Disciplines to hold while you build

- **The twelve rules over every string** — run §1 + §2.2 before any line lands. The discipline *is* the
  product here; a line that breaks a rule isn't done. (The operator will notice. A pronoun, a "your", a
  euphemism, or an agentive verb each got caught and corrected this session.)
- **Declare-at-site, harvest-centrally** — the `registered ⇔ executed` registry is your model; the
  `code ⇔ copy` totality test is its sibling.
- **IR grows under evidence** — voice what's emitted; don't author copy for latent surfaces ahead of
  the events that would carry it.

The anchor is solid. Build inside it, and keep the voice exact.

---

## The 2026-06-08 addendum — what actually faces you

*Written to the agent who picks this up next. The letter above is the register and the
intent; this addendum is the ground truth of where the code is now, so you don't re-derive
two re-envisionings' worth of context.*

### What changed since the letter above

Three things, none of which moves the register:

1. **The voice machinery got built.** Slices 1, 2, and 4 landed. `Voice.fs` (the catalog —
   `all`/`lookup`/`toSurface`/`verdict`/`errorFrame`/`errorSurface`/`errorsSurface`/
   `gateStatement`/`gateSurface`/`stageName`), `Watch.fs` (the streaming stage board with
   the **minimum-dwell floor** — you asked for it; `PROJECTION_WATCH_DWELL_MS`, default
   120 ms), `TtyRenderer.fs` (`renderSummary`/`renderReadinessBoard`/`renderAnswer`/
   `renderVoicedError`/`renderErrorsTo`/`renderGate`), `Surface.fs` (renamed to
   `Statement`/`Substantiation`/`Action`), and the `code ⇔ copy` + gate⇔copy totality tests
   (`VoiceTotalityTests.fs`, with the **mechanical banned-list guard**) are all in and green.
   The Core-purity sub-call resolved to the **1:1 projection-layer companion** (prose lives
   in `Projection.Cli`, never `Projection.Core`).

2. **The CLI was re-envisioned twice.** First the ~16 verbs collapsed to four
   (`project`/`check`/`explain`/`seal`); then those re-grounded into the **flows surface** —
   `projection <flow> [--go] [--fresh] [--allow-drops]`, dispatched through one `runPlan`
   over a `PlanAction` DU (`THE_CLI.md` is the current surface; read it). The voice machinery
   survived both intact — the operator decision each time was "take main on the CLI surface,"
   and the voice layer composed cleanly because it is a *rendering* of codes that did not
   change. The synthetic-data path (`THE_SYNTHETIC_DATA_DESIGN.md`) and live-OSSYS-primary
   model read (`V1_INPUT_DEPRECATION.md`) also landed in that window — both new flows you'll
   eventually voice.

3. **The error surface and the gates got partly wired.** `printErrors` now renders the
   voiced §10/§14 surface across every executor (one edit, total coverage); the six `migrate`
   `REFUSED` shouts were revoiced. But `Voice.gateSurface` — total over the closed
   `Preflight.GateLabel` DU, banned-list-tested — is **still uncalled**: every pre-flight
   refusal hand-writes its string beside the finished renderer.

### Your map: `THE_VOICE_BACKLOG.md`

The work is no longer "build the machinery" — it's **wire the register into the rendered
TTY**, and it is fully inventoried. `THE_VOICE_BACKLOG.md` (new, the masterwork) is your
execution doc: it classifies **every** operator-facing render site in `Program.fs` (1876
lines) as voiced or raw, maps each raw site to its storyboard act + `THE_VOICE.md` section,
names the renderer that voices it, and orders the work into seven waves. It supersedes
`THE_VOICE_BUILD_MAP.md` §6 (the execution layer — that map predates both CLI
re-envisionings); the map's architecture (§1), code inventory (§2), and test blast-radius
(§8) stay accurate and are referenced, not repeated.

### Start here (the wave order, highest-value first)

- **Wave 0 — stop the live breaches.** Four `%A` raw-DU dumps (`Program.fs:1167,1279,575/579,
  1752`) and four system-shout leads (`"canary RED"`, `"DRIFT DETECTED"`, `"verification
  FAILED"`, `"FAILED self-verification"`) are on the operator surface *today* and break §2.2.
  Smallest fixes, highest integrity cost.
- **Wave 1 — spend the renderer that's sitting idle.** Wire `TtyRenderer.renderGate` /
  `Voice.gateSurface` into every migrate/transfer/synthetic pre-flight refusal. Zero new
  infrastructure — the renderer is built, total, and tested; `Preflight.classify` already
  maps code → `GateLabel`. This is the single highest-leverage item. (One sub-decision to
  record: the `--go`/`ALLOW_EXECUTE` intent gate has no `GateLabel` yet — recommend voicing
  it through a flat `gate.intent` `errorSurface` code rather than the engine's closed DU;
  the backlog §2 explains.)
- **Waves 2–6** — the §9 ‖δ‖ preview (reuse `Comparison.renderCatalogDiff`, proven by
  `runDiff`), the §6 proofs, the §13 success narration + header cleanup, the §4 move
  surfaces, the synthetic surface. All in the backlog with file:line anchors.

### Disciplines that still bind (the operator will notice)

Everything in "Disciplines to hold while you build" above still holds. Specifically: the
twelve rules + banned list over **every** string (the `VoiceTotalityTests` guard fails the
build on a violation — extend its scanned set as you add copy); **codes never change, only
copy** (the NDJSON contract is the machine channel — this is a pure rendering project, no
event moves); declare-at-site / harvest-centrally with `code ⇔ copy` totality; pure-Core
holds. Commit to the `claude/handoff-voice-implementation-PzpiS` branch; the register was
settled with the operator one rule at a time — render inside it, don't re-litigate it.

The machinery is whole. The surface is the work, and it's mapped. Hold the voice exact.
