# DYNAMIC_DISPLAY — the operator's time-machine for schema fidelity

**Status:** composed 2026-06-05 (operator-led design pass). **Foundation
shipped this session:** the `LogSink.addSubscriber` push primitive (the one
piece the live leg was missing) + its test. **Decisions locked with the
operator (2026-06-05):** substrate, timeline form, drill depth, diff layout —
see §4. **What remains is the build**, sequenced in §6, each piece a thin
primitive on the substrate the connector layer (`REPORTING_HORIZON.md` §3.5–§3.6)
already finished.

This document is to the *display* what `REPORTING_HORIZON.md` is to *reporting*:
the assembly drawing. `REPORTING_HORIZON.md` named the lenses; this names the one
lens left to build — the **dynamic display**, where the substrate meets a human
directly. Read `REPORTING_HORIZON.md` §1–§3.6 first; this sits one storey up.

---

## §1 The thesis — calm surface, infinite depth

The display is a **time-machine for schema fidelity**. Underneath it the
complexity is effectively unbounded — 300 tables, thousands of decisions, an
open-ended run history — and the entire art is that **the surface stays calm
while the depth stays infinite.** Progressive disclosure is the bridge; the
**timeline is the spine**.

The operator navigates on two axes:

- **Time** — the run history. Past runs are points; the present run
  materializes; the trajectory bends toward the R6 cutover gate. (`RunHistory`
  is the durable sequence; the timeline is its glyph.)
- **Depth** — verdict → module → kind → attribute → decision → evidence. One
  level revealed per keypress; the full tree is never dumped at once.

The single discipline that keeps this on the road: **all three modes are
projections of the `View` ADT** (`src/Projection.Cli/View.fs`). The TUI is a
*View-navigator*, not a new display model. `Theme` is the token layer, `View` is
the structure, the navigator is the new lens. The moment a parallel
representation of the run data appears for the screen, the road is lost.

---

## §2 The two axes, the three modes

The operator lives the display at three tempos — and each is the same `View`
ADT under a different lens:

| Mode | Operator's question | Tempo | The surface |
|---|---|---|---|
| **Glance** | "Am I OK?" | zero interaction | verdict + timeline strip, one screen |
| **Watch** | "Is it progressing? Will it pass?" | live, during a run | a stage tree filling in + per-stage ETA, the verdict panel materializing at the end |
| **Explore** | "Why did this happen? What changed?" | interactive (the TUI) | navigate time (←/→) and depth (↑/↓), filter by axis, search by SsKey; `explain` made navigable; `diff` as a walkable changeset |

Glance is the calm default; Watch is the live leg (the deferred Spectre
live-display); Explore is the TUI (`inspect <runId>`). The lenses already in the
tree — `pretty` / `plain` / `json` over one `View` — gain a fourth:
**`interactive`** (the navigator). Same value, new lens.

---

## §3 The modes in detail

### §3.1 Glance — the calm default

**Question:** *Am I OK?* **Surface:** one screen, zero interaction — the
verdict (`Hero`) + the timeline strip + the R6 meter + the single biggest lever
+ the next action. This is today's readiness board / summary panel
(`TtyRenderer.buildReadinessView` / `buildSummaryView`) **plus the timeline**.

The new element is the **strip**:

```
  ●●●●✕●●●●●●●▸          R6  ███████░░░  7/10
  └ 11 runs · ✕ = run 5 red · ▸ = now · 3 to gate
```

Runs as dots, the present as `▸`, the R6 streak readable as **distance to the
gate**. It is built from `RunHistory.canaryHistory` and `RunHistory.readiness`
— a fold over the one sequence, never bespoke.

**Acceptance:** `projection readiness` leads with the hero verdict, then the
strip + meter; piped, the same facts survive as plain glyphs (no ANSI) and as
`View.toJson`. Every dot is glyph-paired (`●` / `✕`) so the signal survives
`NO_COLOR` and colorblindness.

### §3.2 Watch — live, during a run

**Question:** *Is it progressing, and will it pass?* **Surface:** a stage tree
filling in, per-stage ETA, the verdict panel at the end:

```
  extract  ✓  1.2s
  profile  ⣷  profiling 142/300 kinds · ~8s left
  emit     ○
  canary   ○
```

This is the parked Spectre live-display leg (`REPORTING_HORIZON.md` Tier 3).
It is a **subscriber to the `LogSink` stream**, never a second emit surface
(§13.4 / §13.11 — banned). It draws from `summary.stageCompleted` (the stage
tree) and the terminal `summary.runComplete` (the final panel), and ETA off the
existing per-iteration `Bench` samples (`Bench.streamProbe`).

**The foundation it needed — shipped this session.** `LogSink` wrote envelopes
to a `TextWriter`; nothing could *react* as they emitted. The watch leg needs a
push, so the first craft was `LogSink.addSubscriber : (Envelope -> unit) -> unit`
(+ `clearSubscribers`), fired inside `emit` **and** the terminal `runComplete`,
delivering exactly the envelopes channel 1 writes (post verbosity + mute) so the
renderer mirrors the machine stream rather than a parallel one. See §5.1.

**Channel discipline (§15).** `--pretty` AND `Console.IsErrorRedirected = false`
gates the live render; channel 1 (NDJSON) routes to `--json-out <path>` or is
suppressed — **never both to the same TTY**. Default (no `--pretty`) stays clean
NDJSON; CI's path is untouched.

**Acceptance:** `projection full-export --config c.json --pretty` on a TTY draws
live stages + a final panel; piped, it yields clean NDJSON; `--json-out`
separates the channels; the §15.5 grep-audit still passes (no raw `printfn`
outside `LogSink` / `TtyRenderer`).

### §3.3 Explore — the interactive TUI (`inspect <runId>`)

**Question:** *Why did this happen / what changed?* **Surface:** a full-screen
navigator. This is `explain` (`Program.fs runExplain`) made *navigable*, and
`diff` rendered as a *walkable* changeset.

**Two axes, two keys:**
- **`←` / `→`** move along the timeline (between runs). The cursor rides the
  same strip the Glance mode shows: `●●●●✕●●[●]●●●▸`.
- **`↑` / `↓`** move within the **depth tree**: verdict → module → kind →
  attribute → decision → evidence. **Default landing is shallow** (verdict +
  module rollup); each `↓` / `→` reveals exactly one more level.
- **`/`** filters by axis (e.g. only declines, only a rationale); **`s`** jumps
  by SsKey (the same exact-or-substring match `explain` uses today).
- Between two runs, the changeset view is a **unified, walkable** list (§3.4).

**The shape — a pure reducer + a pure projection, an IO loop.** Per the
substrate decision (§4), the TUI is hand-rolled over Spectre `Live` +
`Console.ReadKey`. The navigation is pure and testable; only the loop touches IO:

```
type Axis  = Time | Depth
type Model = { History: RunHistory; Cursor: Cursor; Filter: Filter; Search: string option }

Navigator.step    : ConsoleKey -> Model -> Model    // PURE reducer  (Key × Model → Model)
Inspect.project   : Model -> View                   // PURE lens     (Model → View)
// the loop (the ONLY IO):  key ← ReadKey · model ← step key model · live.Update(project model)
```

`Navigator.step` and `Inspect.project` are the discriminating surfaces — both
pure, both unit-testable without a terminal (the loop is a five-line shell).
This is the "View-navigator, not a new model" discipline made structural: the
navigator never holds run data the `View` doesn't; it holds a **cursor over**
the history.

**Acceptance:** `projection inspect @run-10` opens the navigator on run 10's
verdict + module rollup; `↓` drills one level; `→`/`←` walk runs; `s OrderId`
jumps to that node's provenance (the `explain` trail + findings, rendered
through the same `EventProjection.transformKindRender`); on a non-TTY,
`inspect --format json @run-10` emits the landed `View` as structure (the same
navigator state, serialized — the machine lens of the TUI).

### §3.4 `diff` — the walkable changeset

`diff <refA> <refB>` (and Explore's between-runs view) renders **unified**:

```
  changeset  @run-9 → @run-10
  + table  OSUSR_X.Invoice
  ~ flip   Customer.Email   null → not-null   (lever: NullBudgetEpsilon)
  - index  IX_Order_Stale
  → 3 changes · ↑↓ walk · → evidence
```

added / removed / flipped as change-rows, projected through the existing
`Comparison.Render → View` path (`Comparison.fs` already composes onto `View`).
It reads like `git diff` — the VCS framing the connector layer established
(`Run` = commit, `Comparison` = diff/apply, the ledger = `git log`). In Explore
it is walkable: `↑`/`↓` through the changes, `→` into a change's evidence.

---

## §4 Decisions locked with the operator (2026-06-05)

These four were the genuinely-operator forks the handoff flagged. Taken with the
operator; each is load-bearing for what gets built.

| Fork | Decision | Why |
|---|---|---|
| **TUI substrate** | **Hand-rolled Spectre `Live` + `Console.ReadKey`** | Spectre 0.54 is already a dependency — no new package. The TUI renders the *same* `View` ADT; the navigator is a pure reducer + pure projection (testable). Terminal.Gui would add a heavy dependency *and* its own widget tree — a parallel representation of the run data, the exact trap the handoff warns against. |
| **Timeline form** | **Horizontal strip** `●●●●✕●●●●▸` | One Theme token + a `View.Timeline` case serves all three modes (Glance spine, Watch axis, Explore cursor). Glyph-paired (survives `NO_COLOR`). Sparklines already cover *metric* trends (`Theme.sparkline` + `RunHistory.trend`); a calendar is wrong (runs are event-driven, not daily). |
| **Drill depth** | **Shallow default** (verdict + module rollup); one level per keypress | "Calm by default, infinite on demand." Never the full tree at once — the wall-of-1000-lines failure the §slice-6 cluster-cap discipline already guards against. |
| **Diff layout** | **Unified, walkable changeset** | Projects cleanly through `Comparison → View`; reads like `git diff`; walks/drills naturally in Explore. Side-by-side fights terminal width + the linear `View` ADT; admissible later as a lens if asked. |
| **Voice register** (2026-06-05) | **Dropped — progressive disclosure replaces it** | The slice-4 "plain ↔ expert words" register was a mis-derivation. The newcomer/expert collapse is served by *depth velocity*, not vocabulary (THE_INSTRUMENT: *"Power is not a denser interface — it is fluency at the dig"*). The expert is a **human operator**, not the agent — owed the *same* Apple-style ease. The substrate is `View.Disclosure` (a depth-bearing node; calm default = one level open; `--depth N\|all`; `toJson` carries the full tree regardless of render depth). `Surface` (essence + dig) is kept as the node's seed; `Voice` + `--voice` removed. |
| **Diction** (2026-06-05) | **Apple-clear microcopy is a discipline** (§7.8) | Every operator-facing word is content-team gold: targeted, concrete, simple, calm. The formal proof/notation is the dig, never the essence. See §7 discipline 8. |

---

## §5 The new substrate (discriminating predicate + ≥2 consumers each)

Every piece is an enriched primitive that *supports* the outcome without
*completing* it — the layer's posture (`REPORTING_HORIZON.md` §3.5).

### §5.1 `LogSink.addSubscriber` — the live push · **SHIPPED 2026-06-05**

`addSubscriber : (Envelope -> unit) -> unit` + `clearSubscribers : unit -> unit`,
fired inside `emit` and `runComplete`. *Discriminating predicate:* a subscriber
receives **exactly** the envelopes channel 1 writes, in order — a Debug envelope
suppressed under Quiet, or a muted category, reaches *neither* the writer *nor* a
subscriber (a naive "fire on every emit call" diverges on the suppressed/muted
inputs; a naive "never fire" diverges on the first visible one). Channel 1 is the
contract, so its write happens first; a subscriber is a derived rendering, never
a gate on the NDJSON line. Run-lifecycle-independent (`beginRun` does not clear —
the renderer attaches once, before the run). *Consumers:* the Watch-leg renderer
(§3.2; the real consumer) + the test (`LogSinkSubscriberTests.fs`, 6 facts). The
Explore navigator's live-tail is a third when it lands.

### §5.2 `View.Timeline` + `Theme.timeline` — the strip · *next*

A `View.Timeline of label * dots: string list * present: int * gate: (int * int)`
case (verdicts as `green`/`red`, the present index, the R6 `(filled, threshold)`),
and a `Theme.timeline` token that renders the dots + `▸` present-marker +
gate-distance. *Discriminating predicate:* `toJson` of a `Timeline` carries the
verdict list + present + gate as structure (not a pre-rendered string), so the
machine lens and the human strip cannot drift — and the present-marker lands at
the `present` index, not always last. *Consumers:* Glance (`buildReadinessView`),
Explore (the `←`/`→` cursor strip). Watch reuses it for the time context.

### §5.3 `Navigator` + `Inspect.project` — the fourth lens · *the Explore core*

The pure reducer (`ConsoleKey × Model → Model`) + the pure projection
(`Model → View`) of §3.3. *Discriminating predicate:* `step` is total over
`ConsoleKey` and **idempotent at the bounds** (`↑` at the root, `←` at run 0, `↓`
at a leaf are no-ops, not crashes); `project` of a shallow cursor yields the
module rollup, of a drilled cursor the one-deeper level — never the whole tree.
*Consumers:* the `inspect` TUI loop + the headless `inspect --format json`
(the navigator state serialized) + the navigator's own property tests.

### §5.4 The changeset projection — `Comparison → View` walkable · *with `diff`*

`Comparison.Render` already targets `View`; the changeset adds the **walkable**
shape (each change a row that is itself a doorway to its evidence). *Discriminating
predicate:* `Apply` present iff replayable (already the `Comparison` invariant) —
the changeset marks a flip as reversible (Catalog torsor) vs. observational
(PhysicalSchema quotient). *Consumers:* the `diff <ref> <ref>` verb + Explore's
between-runs view.

---

## §6 The build sequence (the harvest ahead)

Each row is a thin primitive; ship one per commit, the layer's rhythm. The
subscriber hook (row 0) is done.

| # | Ships | On | Acceptance witness |
|---|---|---|---|
| **0** | `LogSink.addSubscriber` (+ test) | — | **shipped** — `LogSinkSubscriberTests` green |
| **1** | `View.Timeline` + `Theme.timeline`; wire into `buildReadinessView` | View / Theme | Glance shows the strip; `toJson` carries verdicts+present+gate; present-marker at `present` index |
| **2** | Watch leg — `TtyRenderer` subscribes via `addSubscriber`; `--pretty` gates; `--json-out` routes channel 1 | row 0 + stage events | `full-export --pretty` draws live stages + final panel; piped = clean NDJSON; §15.5 grep-audit holds |
| **3** | ETA on the long legs (profile / bulk) from `Bench` per-iteration samples | row 2 | profile/emit show a moving ETA; no ETA when no samples (degrade calmly) |
| **4** | `Navigator` + `Inspect.project` (pure) + the `inspect <runId>` loop | View + RunHistory + explain | `inspect @run` opens on verdict+rollup; `↓`/`→`/`←` navigate; `inspect --format json` = navigator state |
| **5** | the walkable changeset; `diff <ref> <ref>` verb | Comparison + Ref + row 4 | `diff @run-9 @run-10` lists added/removed/flipped; Explore walks it; flips marked reversible vs observational |

Rows 1–2 are independent and parallelizable; rows 4–5 are the Explore core and
land after the View/Theme additions (row 1) give the navigator its tokens.

---

## §7 The disciplines this surface carries (the bar is higher, not lower)

This is the first place the substrate meets a human directly, so the posture
tightens:

1. **The TUI is a View-navigator, not a new model.** Every mode is a `View`
   projection; the navigator holds a *cursor over* the history, never a parallel
   copy of the run data. (The handoff's load-bearing rule.)
2. **Color is meaning, never decoration.** Every color rides with a glyph
   (`●`/`✕`, `✓`/`▲`/`✕`/`○`) so the signal survives `NO_COLOR` and
   colorblindness. Accessibility-is-correctness.
3. **Calm by default, infinite on demand.** Shallow landing; one level per
   keypress; the full tree is never dumped (the cluster-cap discipline, made
   interactive).
4. **End every surface with the next action** (principle #5). Glance names the
   biggest lever; Watch ends on the verdict; Explore's footer names the keys.
5. **Channel split by rendering, never by content** (§4 / §15). `--pretty`
   gates the *lens*; the facts are the same events. Never a second emit surface
   (§13.4 / §13.11).
6. **One substrate, many lenses — now four.** `pretty` / `plain` / `json` /
   `interactive` are projections of one `View` value; the human and machine
   lenses cannot drift because they are the same document.
7. **Subtract.** Every glyph earns its place as a doorway; nothing renders that
   the operator's question at that tempo doesn't ask for.
8. **Apple-clear diction — every operator-facing word is content-team gold**
   (operator standard, 2026-06-05). Verdicts, affordances, actions, disclosure
   headlines: **targeted, concrete, simple, calm — as if a content team dwelled
   on the exact right words.** Banned: opaque / mystical wording (the operator's
   worked counterexamples — *"needs your nod"*, *"open a lane"*), drama
   (*"destroys structure"*), negation-framing (*"nothing destroyed"*), and
   **leaked internal identifiers** (a human reads `Country`, never
   `OS_KIND_Country`). The formal proof / notation (`‖δ‖`, exit codes, gate
   labels) is **the dig** — the essence made rigorous, reachable by anyone — and
   never the essence itself. The newcomer and the master read the same plain
   surface; the master simply digs faster. **Progressive disclosure is how the
   expert is served — depth velocity, not a denser vocabulary** (there is no
   "expert mode"; *"Power is not a denser interface — it is fluency at the dig"*).

---

## §8 Cross-reference index

| This doc | Substrate | Code |
|---|---|---|
| §3.1 Glance / §5.2 strip | `RunHistory` (fold), `Theme` tokens | `RunHistory.fs`, `Theme.fs`, `TtyRenderer.buildReadinessView` |
| §3.2 Watch / §5.1 push | `LogSink` two-channel (§15) | `LogSink.addSubscriber` (shipped), `TtyRenderer.fs` |
| §3.3 Explore / §5.3 navigator | `View` ADT, `explain`, `RunHistory` | `View.fs`, `Program.fs runExplain`, new `Navigator` |
| §3.4 diff / §5.4 changeset | `Comparison` (Render→View), `Ref` | `Comparison.fs`, `Ref.fs`, new `diff` verb |
| §4 decisions | the four forks | this doc (operator-confirmed 2026-06-05) |

**Governing references:** `REPORTING_HORIZON.md` §1–§3.6 (the springboard);
`docs/logging-format.md` §13 (banned antipatterns), §15 (two-channel + strict
adapter); the Tier-5 seven principles (`REPORTING_HORIZON.md` §3 Tier 5).
