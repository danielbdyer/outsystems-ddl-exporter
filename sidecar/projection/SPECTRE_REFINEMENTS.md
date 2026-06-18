# SPECTRE_REFINEMENTS.md — the rendering-layer refinement ledger

> **Opened 2026-06-18** (branch `claude/spectre-improvements-v1fhjq`). The forward
> ledger for the operator-facing render layer — `Projection.Cli`'s `View` / `Theme`
> / `TtyRenderer` / `Watch` / `Comparison` / `Surface`. It is a *backlog with a
> cheat sheet*: every item names the seam to cut at, the gotcha that will bite, and
> the test that pins it. It does not restate the design rationale that
> `REPORTING_HORIZON.md`, `DYNAMIC_DISPLAY.md`, and `THE_VOICE_BUILD_MAP.md` already
> own — it points there and tells the next agent where the code is.
>
> **Citation discipline (CLAUDE.md §8):** items cite *symbols*, never line numbers —
> line numbers drift and a restated one is a defect. Grep the symbol; the code is
> the inventory.

---

## 0 — What this is, and what already landed

This chapter began as a survey: *25 ways to keep improving the Spectre
implementation.* The first slice shipped on this branch; the rest are sequenced
below, each with the technical note to start from.

### Shipped this slice (2026-06-18)

**The console factory — `View.consoleFor` / `View.consoleTo` (items #5 + #7).**
Six render sites in `TtyRenderer` plus the live board in `Watch.renderWatch` each
re-derived the same three facts about a sink — *is it redirected* (→ pin a width
so the board/gate grids don't collapse), *what does the color channel want*, and
*which `ReferenceEquals` decides the redirect*. That logic was inlined as a bare
`100` and a copy-pasted `ReferenceEquals` ladder. It now lives once, in `View`:

- `View.consoleFor (writer) (redirected)` — builds the configured `IAnsiConsole`:
  pins `View.plainWidth` when redirected, and resolves the color channel.
- `View.consoleTo (writer)` — the single entry point; derives `redirected` from
  the writer itself (`Console.IsErrorRedirected` for stderr, `IsOutputRedirected`
  for stdout, `true` for any in-memory writer) via the private `redirectedFor`.
- `View.envColorOverride` — honors **`NO_COLOR`** (no-color.org: any non-empty
  value suppresses color *even on a TTY*) and **`CLICOLOR_FORCE`** (set, ≠ `"0"`
  forces color into a redirect). NO_COLOR is checked first, so it wins when both
  are set — a declared preference for no color is never overridden.

This makes `Theme.fs`'s standing promise — *"the signal survives a colorblind
reader or `NO_COLOR`"* — **true**: before this slice nothing actually read the
variable; color was only ever stripped by Spectre's own redirect detection.

Net deletion at the call sites; one behavioral refinement: `renderErrorsTo` /
`renderVoicedTo` no longer pin a width when handed `Console.Out` *as a real TTY*
(the old code pinned any non-stderr writer unconditionally) — strictly more
correct, cosmetic only.

> **Next-step cheat sheet for this seam:** `CLICOLOR_FORCE` is best-effort for a
> *redirected* sink — `settings.Ansi <- Yes` makes Spectre emit ANSI, but a piped
> sink's `ColorSystem` may still detect `NoColors`. If forcing color into a file
> ever becomes a real ask, also set `settings.ColorSystem <- ColorSystemSupport.Standard`
> in the `Some true` arm. The NO_COLOR path (`ColorSystem <- NoColors`) is the
> tested, load-bearing one.

---

## 1 — The map (all 25, by theme)

Status key: **● shipped** · **◐ partial / starved** · **○ not started** ·
**▢ design-only** · **✕ de-scoped (operator direction)**.

| # | Item | Theme | Status |
|---|---|---|---|
| 1 | Close the `Panel` drift hole | A. Substrate integrity | ○ |
| 2 | Unforgeable markup (a `Markup` newtype) | A | ○ |
| 3 | Defensive render (markup-fault → plain fallback) | A | ○ |
| 4 | A `RenderOptions` record | A | ○ |
| 5 | One console/channel factory | A | ● |
| 6 | Totality + property test over the DU | A | ○ |
| 7 | Honor `NO_COLOR` / `CLICOLOR_FORCE` | B. Color & accessibility | ● |
| 8 | Sealed `Color` palette | B | ✕ |
| 9 | High-contrast / colorblind theme | B | ✕ |
| 10 | Screen-reader narration lens | B | ✕ |
| 11 | **Responsive width** | B | ○ |
| 12 | `View.Table` primitive | C. New primitives | ○ |
| 13 | Fold `Bench.renderTable` into the substrate | C | ○ |
| 14 | Trend surfaces (use `Theme.sparkline`) | C | ○ |
| 15 | `Trail` gets cap-and-name + depth | C | ○ |
| 16 | Unify the collapsed-affordance vocabulary | C | ○ |
| 17 | Implement `--query` over `toJson` | D. The query lens | ▢ |
| 18 | Per-node addressing (`ViewPath`) | D | ○ |
| 19 | Emit the intra-stage `summary.stageProgress` events | E. The Watch board | ◐ |
| 20 | Move the dwell off the emitting thread | E | ○ |
| 21 | Instrument the other runs onto the spine | E | ◐ |
| 22 | Loud fallback for stage copy | E | ○ |
| 23 | Build the Explore TUI | F. Explore + history | ▢ |
| 24 | A `diff <runA> <runB>` verb | F | ▢ |
| 25 | `explain <ssKey>` provenance drill-down | F | ◐ |

> **De-scoped (2026-06-18, operator direction):** #8 sealed palette, #9
> high-contrast theme, #10 screen-reader narration. Recorded here so the cut is
> honest and re-openable, not forgotten. The accessibility ambition this chapter
> carries is **responsive width (#11)** alone; NO_COLOR (#7) already shipped.

---

## A — Substrate integrity (the `View` ADT engine)

### 1 · Close the `Panel` drift hole  ○  *(highest-value correctness fix)*

**Problem.** `View.writePanel` matches only `Field` / `Meter` / `Action` and ends
on `| _ -> ()` — every other child is *silently dropped from the pretty lens*. But
`View.toJson` serializes every child faithfully. So a `Panel` carrying a `Note` or
`Disclosure` renders one thing to the human and another to the machine — a direct
violation of the *one-substrate, lenses-cannot-drift* invariant the whole module
is built on (the `ViewTests` premise).

**Fix (type-honest).** Make the panel's children a closed `PanelRow` (Field /
Meter / Action), so a non-row child is *unrepresentable* rather than swallowed —
the house private-constructor discipline (CLAUDE.md §6) applied to layout. The
constructors in `TtyRenderer.buildSummaryView`, `Comparison.renderCatalogDiff`,
and `Comparison.renderPhysicalDiff` already pass only rows, so the ripple is the
DU edit + those three sites + `toJson`/`writePanel`.

**Cheat sheet.** The lighter fix (render the dropped cases instead of typing them
out) fights Spectre's `Grid`, which requires a fixed column count per row — a
`Note`/`Disclosure` doesn't fit two columns. Prefer the type. Land #6 in the same
commit — the property test below *is* the regression proof.

### 2 · Unforgeable markup  ○

**Problem.** `Theme.green/yellow/red/muted/accent/bold` wrap a **raw** string in
`[green]…[/]` with no escaping. `View.styled` escapes before it colorizes, and the
`writeBlock`/`writePanel` sites mostly `Markup.Escape` first — but the discipline
is by-convention, not by-construction. A data-derived string carrying `[` (a table
literally named `Order[Archive]`, an SsKey, a ledger path) that reaches a `Theme.*`
helper un-escaped throws at `MarkupLine` (see #3).

**Fix.** A `Markup` newtype (private ctor, smart constructor escapes on the way
in). `Theme.*` and the `console.MarkupLine` sites take `Markup`, not `string`, so
an unescaped value *cannot* reach the pretty lens. The house derive-macro again.

**Cheat sheet.** Grep for every `Theme.` color helper call and every `MarkupLine`
/ `Markup(` site; the VO-lift compiler-gap note in CLAUDE.md §6 is the same shape
(`String.Concat` accepts `object`; markup accepts any `string`).

### 3 · Defensive render  ○

**Problem.** `console.MarkupLine` **throws** on malformed markup. The verdict panel
renders at the very *end* of an otherwise-successful run — the worst place to take
an exception. A display bug today can turn exit 0 into a crash.

**Fix.** Wrap the per-block render in a try that falls back to a plain
(`Markup.Escape`d / unstyled) line on `InvalidOperationException`, so a rendering
fault degrades to plain text and never fails the run it describes. Pairs with #2
(which removes most of the *cause*) — keep both: #2 prevents, #3 contains.

### 4 · A `RenderOptions` record  ○  *(the enabling refactor)*

**Problem.** `defaultDepth`, `laneCap`, the implicit width, and the color policy
are scattered `[<Literal>]`s and inlined constants. `writeToDepth` threads a bare
`int depth`.

**Fix.** One `RenderOptions { Depth; LaneCap; Width; Color }` threaded through
`write`. This is the carrier that makes #11 (responsive width), #15 (`Trail` cap),
and a future per-surface depth all one-line changes instead of new plumbing. Build
it *before* #11 — #11 is its first consumer.

**Cheat sheet.** Keep `write` / `writeToDepth` as today's thin wrappers over a new
`writeWith (opts) (console) (v)` so existing callers (and every test) are
unbroken; `defaultDepth` / `laneCap` / `plainWidth` become the record's defaults.

### 5 · One console/channel factory  ● *(shipped — see §0)*

### 6 · Totality + property test over the DU  ○

**Problem.** No test asserts that *every* `View` case both renders and serializes
without throwing, nor that `toJson`'s `kind` set is total over the DU. The `| _ ->
()` hole in #1 is exactly what such a test catches.

**Fix.** A generator over `View` (FsCheck, or a hand-rolled exhaustive case list)
asserting: `write` never throws · `toJson` never throws · a round-tripped
`Disclosure`/`Lane` keeps its full child list regardless of render depth (the
existing `ViewTests` "json carries it either way" assertion, generalized). Add a
compile-time totality witness by making `writePanel` match a closed `PanelRow`
(#1) — then the `| _ -> ()` is gone and the compiler is the test.

---

## B — Color & accessibility

> Section scoped to **responsive width** per operator direction (2026-06-18).
> `NO_COLOR` shipped (#7). #8–#10 de-scoped (§1 table).

### 11 · Responsive width  ○

**Problem.** Everything assumes ≥ `View.plainWidth` (100) columns. The meter is a
fixed 10 cells (`Theme.meter`); long `Field` values aren't truncated or wrapped;
on an 80-column terminal the readiness board and gate panel wrap mid-cell. The
§12 breadth cap (`laneCap`) has no *width* sibling.

**Fix.** A width cap as the dual of the breadth cap: truncate `Field` values (and
the `Lane` / `Disclosure` headlines) to the console's usable width with a `…`
tail, reading the width at render time. `writeBlock` already holds the
`IAnsiConsole`, so `console.Profile.Width` is in scope *now* — no new plumbing
needed for the read; the value flows cleanly once #4's `RenderOptions.Width` is
the source of truth.

**Cheat sheet.**
- The width to respect is `console.Profile.Width` — but remember it's **pinned to
  `View.plainWidth` for a redirected sink** (by the factory) and reports the real
  terminal width on a TTY. Tests pin it to 200 (`ViewTests.plainToDepth`,
  `TtyRendererTests.renderToString`), so a naive "truncate to width" must not fire
  at 200 for the existing assertions — cap on `min Width budget`, and prefer
  truncating only when the rendered cell *exceeds* the budget.
- Account for the `indent` prefix and the label column when computing the budget;
  the panel `Grid` measures its own columns, so target the *line* renderers
  (`Hero`/`Field`/`Note`/`Lane` headline) first, where the wrap is ugliest.
- The meter can stay fixed (it's a gauge, not prose) or scale with width — defer
  the scaling; truncation is the user-visible win.
- New tests: a narrow console (`Profile.Width <- 40`) renders a long `Field`
  value with a `…` and *does not* wrap to a second line; the machine lens
  (`toJson`) keeps the **full** untruncated value (width is a pretty-lens concern
  only — the same law `laneCap` obeys in `ViewTests`).

---

## C — New `View` primitives

### 12 · `View.Table`  ○

**Problem.** The capability "matrix" (`TtyRenderer.buildSurveyView`) renders each
environment as a `Field` line, not aligned columns — the doc calls it a matrix but
the substrate has no table. Diff channel-counts and the bench table want the same.

**Fix.** A `Table of headers: string list * rows: (string * Status) list list`
case (with its `toJson` arm and a `writeBlock` arm over a Spectre `Table`/`Grid`).
First consumers: `buildSurveyView`, then #13.

**Cheat sheet.** Mind #1 — a `Table` inside a `Panel` must be a `PanelRow` or it
hits the same drift hole. Decide whether `Table` is panel-legal before building.

### 13 · Fold `Bench.renderTable` into the substrate  ○

**Problem.** The perf table dumped under `-v` (`OperatorConsole.dumpBench` →
`Bench.renderTable`) is a separate text path — no json, not `--query`-able,
doesn't share `Theme`.

**Fix.** Re-express it as a `View.Table` (#12) so the perf surface joins the one
lens. Watch the `Projection.Core.Bench` boundary — `renderTable` lives in Core;
the `View` projection belongs in `Cli`. Keep Core's table (it's used by the perf
gate's plain dump) and add a `Cli`-side `benchView : Bench.Stats list -> View`.

### 14 · Trend surfaces — `Theme.sparkline` has zero callers  ○

**Problem.** `Theme.sparkline` is built and **unused** (grep confirms: defined in
`Theme.fs`, referenced nowhere). `RunLedger` already accumulates the per-run
series.

**Fix.** A `View.Spark of label * values: int list` block, and a surface reading
`RunLedger` history for profile-time creep / coverage climb / warnings-over-time
(REPORTING_HORIZON Tier-4 "Trend surfaces"). The glyph and the data both exist;
only the block + the surface are missing.

**Cheat sheet.** The readiness board (`TtyRenderer.buildReadinessView`) is the
natural host — it already renders `Dots` (canary history); a sparkline of
profile-time over the same runs sits beside it.

### 15 · `Trail` gets cap-and-name + depth  ○

**Problem.** `View.Trail` (explain's transform chain, `RunFaces.explainView`)
renders flat regardless of depth — a long chain is a wall, with none of the §12
`laneCap` / `and N more` discipline `Lane` enforces.

**Fix.** Give `Trail` the same cap-and-name tail and depth-gated reveal `Lane`
has in `writeBlock`. Falls out almost free once #4's `RenderOptions.LaneCap`
(rename → `BreadthCap`) is the shared knob.

### 16 · Unify the collapsed-affordance vocabulary  ○

**Problem.** A collapsed `Disclosure` shows `▸ N more`; a collapsed `Lane` hides
its items *silently* (the `Lane` arm of `writeBlock` prints only the header at
depth < 1). Inconsistent affordance.

**Fix.** A collapsed `Lane` should also hint `▸ N items`. One-line change in the
`Lane` arm; pin it with a `ViewTests` assertion.

---

## D — The query / structured lens

### 17 · Implement `--query`  ▢  *(redeems an already-paid-for lens)*

**Problem.** `View.toJson` exists and the code **promises** "a `--query` walks
this" in `View.fs`'s header and in `RunFaces.explainView`'s doc — but there is no
walker anywhere (grep: `--query` appears only in comments and one prescope doc).
`renderAnswer`'s `asJson` branch is where it attaches.

**Fix.** A small JSONPath-ish walker over the `JsonNode` `toJson` produces, wired
to a `--query <path>` global flag beside `--format json`. Scope it to the subset
the surfaces need: `.blocks[]`, `.fields[]`, `.<key>`, index, and a flat filter
(`[?status=warn]`). The point is the *structured lens redeemed*, not a full
JSONPath engine.

**Cheat sheet.** The flag-strip machinery lives in `Program.main` (the global-flag
strip that already handles `--pretty` / `--watch` / `--format`). `renderAnswer
(asJson) (depth) (v)` is the choke point — add a `query: string option` arg and
walk `View.toJson v` before `WriteLine`. Output is JSON text, so it stays on
stdout (the answer channel).

### 18 · Per-node addressing (`ViewPath`)  ○

**Problem.** `writeToDepth` decrements a single `int` uniformly, so sibling
`Disclosure`s open *together*. There is no way to "open just this node, deeply,
leave the rest collapsed" — and the Explore Navigator (#23) needs exactly that.

**Fix.** A `ViewPath = int list` (child indices) carried in `RenderOptions` (#4)
naming the open branch; `writeBlock` reveals a node iff its path is a prefix of
the open path. The `int depth` becomes a degenerate `ViewPath` (open everything to
N), so existing callers are unbroken.

**Cheat sheet.** This is the prerequisite for #23 — build it as part of the
Navigator slice, not before; a `ViewPath` with no interactive consumer is a
zero-consumer build (CLAUDE.md §5: "verbs at the second consumer").

---

## E — The live Watch board

### 19 · Emit the intra-stage `summary.stageProgress` events  ◐  *(the board is built and starved)*

**Problem.** `Watch.apply` *fully handles* `summary.stageProgress` → `Active
(Progress)` → `progressText` / `etaText`, and `LogSink` even has the emitter
helper. But **no producer calls it** — grep finds `stageProgress` only in
`RunSpine`, `LogSink` (the helper definition), and `Watch` (the consumer). The
entire ETA/progress apparatus renders today but is fed nothing. The hard part is
done; the board shows only `started`/`completed`.

**Fix.** Instrument the per-table loops (the ingestion / bulk / emission loops,
off the existing `Bench.streamProbe` samples) to emit `summary.stageProgress`
with `done` / `total` / `elapsedMs`. This is the single highest leverage item on
the board — render-ready apparatus waiting for one emit site per long stage.

**Cheat sheet.** The `Progress` honesty rule is already enforced in `Watch.etaText`
(no rate ⇒ no estimate) and the unknown-denominator case in `progressText` (`Total
≤ 0` ⇒ plain count-up). So a producer that can't count ahead may emit `total = 0`
safely. Pure work is in Core/Pipeline; keep the emit at the Pipeline boundary, not
in Core.

### 20 · Move the dwell off the emitting thread  ○

**Problem.** `Watch.renderWatch` enforces the dwell with `Thread.Sleep` **on the
emitting thread, holding the `LogSink` lock** — its own doc flags this is safe
*only* because the run is synchronous + single-threaded. The board would serialize
any future concurrent realization stream.

**Fix.** Move the dwell to a drain loop on a render thread (THE_VOICE_BUILD_MAP
§4.3): the subscriber enqueues board transitions; the render thread pops, sleeps
the floor remainder, and `ctx.Refresh()`es. Do this *before* any parallel emitter
lands, not after a hang.

### 21 · Instrument the other runs onto the spine  ◐

**Problem.** Only `full-export` emits the stage spine, so `--watch` is silent /
empty for Migration / Transfer / Eject / Drift.

**Fix.** Seed each from its `RunSpine` (the `Watch.seededOf` path already exists)
and emit the same `<stage>.started` / `summary.stageCompleted` envelopes from
their run faces. THE_VOICE_BUILD_MAP slice 2's remainder.

### 22 · Loud fallback for stage copy  ○

**Problem.** `Watch.statementText` does `match Voice.lookup code with Some c -> …
| None -> code` — on a miss it shows the **raw event code** (`emit.foo.started`) to
the operator. The answer path already learned this lesson (NM-47's
`Voice.fallbackSurface`); the board never did.

**Fix.** Either a register-correct board fallback, or — cheaper and stronger — a
totality test pinning every stage key's `.started` / `summary.stageCompleted` /
`watch.*` code as voiced, so a new stage can never leak an identifier to a human.
Mirror the existing `code ⇔ copy` totality test.

---

## F — Explore + run history

### 23 · Build the Explore TUI  ▢  *(the marquee gap)*

**Problem.** The interactive inspector (DYNAMIC_DISPLAY §3.3) is named and unbuilt.
Every dependency exists: the `View` engine, `Comparison`, `RunLedger`, and (once
#18 lands) `ViewPath`.

**Fix.** A pure `Navigator.step : ConsoleKey -> Model -> Model` reducer + a pure
`project : Model -> View`, driven by Spectre `Live` + `Console.ReadKey`. Keep the
reducer pure and property-test it (`step` total over `ConsoleKey`, idempotent at
the tree bounds) — the codebase's whole ethos applied to a TUI. `←`/`→` walks run
history (the ledger); `↑`/`↓` moves the cursor; `→`/`Enter` deepens the
`ViewPath`.

**Cheat sheet.** The Navigator holds a **cursor over data the `View` already
carries**, never a second copy of run state (DYNAMIC_DISPLAY §7 discipline 6). The
render thread / input loop concurrency is the same shape #20 sets up — sequence
#20 first.

### 24 · A `diff <runA> <runB>` verb  ▢

**Problem.** `Comparison<Catalog, CatalogDiff>` is built and Weyl-proven,
`RunLedger` holds the history, and `Comparison.changeSurface` already renders a
diff statement-first. Only the verb that picks two runs from the ledger and
renders `between` them is missing (REPORTING_HORIZON Tier-4).

**Fix.** A thin `RunFaces` verb: resolve two run ids → load their catalogs →
`Comparison.summary Comparison.catalog a b` → `renderAnswer`. Once #23 lands, this
is the `←`/`→` time-axis of Explore, not a separate surface.

### 25 · `explain <ssKey>` provenance drill-down  ◐

**Problem.** `RunFaces.explainView` already projects one node's transform trail +
findings through `View.Trail`, rendered through the *same* `EventProjection`
lens the event stream uses (so the two can't drift). It stops at the trail.

**Fix.** Extend it into the full provenance dig — every transform, decision,
finding, and suggested fix for one `SsKey`, progressively disclosed (so a deep
trail isn't a wall — needs #15), and walkable inside Explore (#23, #18). The dig,
made interactive.

---

## 2 — Sequencing (the cut order)

The dependency spine, shortest path to the most value:

1. **#4 `RenderOptions`** — the enabling refactor; #11, #15, #18 all consume it.
2. **#1 `PanelRow` + #6 property test** — close the live drift bug, lock the
   substrate (one commit; the test is the proof).
3. **#11 responsive width** — first `RenderOptions` consumer; user-visible.
4. **#2 unforgeable markup + #3 defensive render** — kill the markup-crash class.
5. **#19 `stageProgress` emit** — wake the built-but-starved ETA board (parallel
   track; no dependency on the above).
6. **#17 `--query`** — redeem the structured lens (parallel track).
7. **#20 dwell off-thread → #18 `ViewPath` → #23 Explore TUI** — the interactive
   arc, in order; #24 / #25 fall out once #23 stands.

Items #12–#16, #21, #22 are independent quality cuts, takeable any time a related
surface is already hot (the "audit during the work" discipline, CLAUDE.md §6).

## 3 — Standing notes for anyone cutting here

- **`TreatWarningsAsErrors` + `Nullable=enable`.** `GetEnvironmentVariable` returns
  `string | null`; match `null | "" -> …` (the `envColorOverride` / `Watch.resolveDwellMs`
  pattern). An unused `open` or binding fails the build.
- **`[<Literal>]` only on CLR primitives** (CLAUDE.md §4.6) — `plainWidth = 100` is
  fine; a `decimal`/record literal is a module-load bomb.
- **The one-substrate law is the renderer's prime directive.** Any cap, truncation,
  collapse, or theme is a *pretty-lens* concern; `toJson` always carries the full,
  uncapped, untruncated tree. Every new `View` case ships its `toJson` arm and a
  test asserting the machine lens keeps what the human lost (the `laneCap` /
  `Disclosure` tests are the template).
- **Tests pin `Profile.Width <- 200`** and build explicit `NoColors` consoles —
  they never depend on color being *on*. Env-var color tests (NO_COLOR /
  CLICOLOR_FORCE) must set/restore the variable in a `finally` and live in the
  `Global-MutableState` collection, since env is process-wide and the pure pool
  runs parallel.
- **Run the pure pool only** via `scripts/test.sh fast` (~25s); never `dotnet test`
  the whole solution (OOM, CLAUDE.md §4.1).
