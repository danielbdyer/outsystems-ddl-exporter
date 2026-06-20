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

### Shipped next (2026-06-18) — #1 + #6: the substrate drift hole, closed and locked

**The `PanelRow` type (#1).** `Panel of title * View list` let a non-row block be
placed in a panel and then **silently dropped** by `writePanel` (`| _ -> ()`)
while `toJson` kept it — the pretty and JSON lenses diverging on one value, the
exact thing this module exists to forbid. The fix makes it unrepresentable: a
closed `Panel of title * PanelRow list`, where `PanelRow` is `Labeled | Gauge |
Next` (named distinctly from the `View` DU's `Field | Meter | Action` so a bare
`View.Field` stays unambiguous everywhere). `writePanel` is now **total over
`PanelRow`** — the `| _ -> ()` is gone and the compiler is the guarantee. The
JSON wire format is byte-identical (the rows still serialize as
`field`/`meter`/`action` kinds).

**The DU totality lock (#6).** Two new `ViewTests`: one asserts a `Panel` renders
*every* row to both lenses (the drift, pinned); one walks **one instance of every
`View` case** through plain (shallow + deep) and JSON, asserting neither lens
throws and the kind tag matches — exhaustive by construction, so a future case
that forgets a `writeBlock` or `toJson` arm fails in the pure pool, not at the
tail of a run.

> Pure pool green with the warm SQL container wired in
> (`eval "$(scripts/warm-sql.sh start)"`; `fast` does **not** auto-detect it, so
> export `PROJECTION_MSSQL_CONN_STR` when running the pure pool here — several
> "pure" extraction canaries open a live connection).

### Shipped next (2026-06-18) — #4: the `RenderOptions` carrier (the enabling refactor)

**One policy value, not scattered constants.** `defaultDepth`, `laneCap`, and the
width pin were three module `[<Literal>]`s and `writeToDepth` threaded a bare
`int depth`. They are now one `RenderOptions { Depth; LaneCap; Width }` threaded
through a new `writeWith opts console v`; `write` / `writeToDepth` stay as thin
wrappers that default `Width` from the live `console.Profile.Width` — so a real
terminal's width (or the factory's redirected-sink pin) flows into the budget.
`writeBlock` reads `opts.Depth` / `opts.LaneCap`; the `Disclosure` recursion
decrements via `{ opts with Depth = opts.Depth - 1 }`. **No behavior change**: the
machine lens reads none of it (the one-substrate law), and `Width` is threaded but
not yet *read* by the renderer — #11 is its first consumer, the doc-sanctioned
enabling-refactor exception.

**`Color` deliberately cut.** The §4 sketch listed `{ Depth; LaneCap; Width;
Color }`, but the color channel is already resolved once in the console factory
(#5/#7); a `RenderOptions.Color` field would be a zero-consumer duplicate
(CLAUDE.md §5, "carriers reify eagerly, verbs at the second consumer"). It joins
the record at the first render-time color gate, not before one exists — recorded
so the cut is honest and re-openable, the same discipline as the de-scoped #8–#10.

**The test (`ViewTests`).** Two law-citing facts drive `writeWith` directly: a
custom `LaneCap` caps a lane where the module default (12) would not, and `toJson`
keeps the full list (breadth is pretty-only); a deeper `Depth` reveals a nested
leaf the default collapses. The carrier is threaded provably, and the machine lens
is proven blind to it.

### Shipped next (2026-06-18) — #11: responsive width (the width cap, #4's first consumer)

**The dual of the breadth cap.** `laneCap` caps how MANY items a lane shows; #11
caps how WIDE a line may be. A `View.truncateTo budget s` helper tails a value with
`Theme.ellipsis` (`…`) when it overflows the column budget (`RenderOptions.Width`,
defaulted from `console.Profile.Width`), applied to the prose line renderers —
`Field` value, `Hero` / `Note` / `Action` text, the `Disclosure` headline, and the
`Lane` headline (where it truncates the *label* so the load-bearing humane count
never falls off the line). It runs on the RAW value **before** markup
escaping/coloring, so a color tag is never cut — the same fault #2/#3 contain, here
avoided by construction. The panel `Grid` measures its own columns, so panel rows
are untouched (lower blast radius).

**Width is pretty-only.** `toJson` is unchanged — it never truncates, so the
machine lens keeps the full value the human lens tailed (the one-substrate law, the
same the `laneCap` tests pin). Three law-citing `ViewTests`: a long `Field` value
tails with `…` on ONE line at a 40-column console (no wrap) while json keeps it
whole; a long `Lane` label is cut but the count survives; and at 200 columns the
value renders whole — the cap bites only when it must, so every existing
wide-console assertion (board, gate, survey, explain) stands unregressed.

**Deferred.** `Lane`/`Disclosure` *item* lines and `Trail` steps are not yet
width-capped (the latter is #15's territory); the meter stays a fixed gauge (a
gauge, not prose — the doc's call). Recorded so the edge is named.

### Shipped next (2026-06-18) — #17: the `--query` lens (the structured tree, redeemed)

**A walker over the lens already paid for.** `View.toJson` always carried the full
document and the `View.fs` header *promised* "a `--query` walks this" — but nothing
did. Now `Projection.Cli.Query` (`walk` / `render`) is a TOTAL, BOUNDED
JSONPath-subset walker: object keys (`kind`, `blocks`), array index (`blocks[0]`),
the wildcard (`blocks[]`), and one flat equality filter (`blocks[?status=warn]`),
with a key after a filter mapping over the survivors (`[?status=warn].value`). A
bracket-aware split keeps a spaced/dotted filter value whole. Every op is total — a
key miss, an out-of-range index, a key into a scalar each yield `null`, never a
throw, so the walker can't crash the answer it filters. Deliberately NOT a full
JSONPath engine (the §5 scope-creep risk) — it grows at the second real query.

**Global, by a ref — not a threaded arg.** `--query` filters EVERY answer surface
(survey, explain, diff, migrate-preview), so rather than thread a `query` arg
through each verb (the cheat-sheet's literal suggestion), it rides a
`TtyRenderer.queryPath` ref that `Program.main` sets from a `--query <path>` value
flag — the same global-CLI-state pattern as `verboseMode` / `prettyMode`, with zero
per-verb ripple. `renderAnswer` reads it and emits `Query.render path (View.toJson v)`
to stdout (the answer channel). One substrate: the query walks the SAME tree the
json lens emits.

**Nine law-citing `QueryTests`** cover each grammar shape, the total-on-miss
guarantee, the spaced-filter-value split, and the one-substrate tie-in; `--query`
is documented in `--help`.

### Shipped next (2026-06-18) — #3: defensive render (the markup-crash class, contained)

**A render fault can no longer fail the run it describes.** `console.MarkupLine`
THROWS on malformed markup (a stray `[`, an unknown style), and the verdict panel
renders at the very END of an otherwise-successful run — the worst place to take an
exception. `View.safeMarkupLine` wraps the line render: on
`InvalidOperationException` it degrades to the line with its markup stripped
(`Markup.Remove`), or the raw text if even that won't parse — never a throw. Every
`writeBlock` line renderer routes through it; `writePanel`'s panel write is wrapped
to degrade to plain rows under the title. A test feeds it an unknown style + an
unbalanced `[` and asserts the text survives, plain, with no throw.

**The discipline still holds — this is the net, not the fix.** A grep confirms all
28 `Theme.*` color-helper call sites (`View.fs` ×20, `Watch.fs` ×8) already
`Markup.Escape` their data, so there is no LIVE markup bug today; #3 contains the
class against a future slip, and #2 (the `Markup` newtype) would make the escaping
unforgeable at the type level — its ripple is exactly those two files. Kept both, as
the doc directs: #2 prevents the cause, #3 contains the effect.

### Shipped next (2026-06-18) — #22: stage-copy totality (the board can't leak a raw code)

**Derived from the spines, not a hand-list.** `Watch.statementText` falls back to
the RAW event code (`emit.foo.started`) on a Voice miss — a register leak to a
human. The fix is the doc's "cheaper and stronger" option: two `VoiceTotalityTests`
derive the board's stage codes from the declared spines themselves (`Spines` ×
`RunSpine.keys`) and assert every `<stageKey>.started`, plus the frame codes
`summary.stageCompleted` / `watch.stageHalted` / `watch.runTitle` / `watch.runDone`,
is voiced. Because the keys come from the spine SOURCE (not the hand-maintained
`inScopeCodes` set), a new spine stage that forgets its copy fails in the pure pool —
it can never leak `<key>.started` to an operator at render. No production change; the
current set is proven complete.

### Shipped next (2026-06-18) — #16: the collapsed-affordance vocabulary, unified

**A collapsed `Lane` now hints what it holds.** Its arm gains an `elif n > 0` branch
showing `▸ N item(s)` (pluralized), the same `▸`-affordance a collapsed `Disclosure`
shows as `▸ N more` — so the two node kinds speak one collapsed-affordance language.
The Lane headline already carries the count, so the hint reinforces the openable
affordance rather than adding new information (the unification is of FORM, the
deliberate consistency the item asks for — noted honestly, not hidden). A `ViewTests`
assertion pins the hint and the singular grammar (`1 item`, never `1 items`).

### Shipped next (2026-06-18) — #15: `Trail` gets the `Lane` discipline (a long chain isn't a wall)

**The transform chain caps and reveals like a move-lane.** `View.Trail` (explain's
transform chain) rendered flat at any depth — a thousand-step chain a wall. Its
`writeBlock` arm now mirrors `Lane`: the label carries the `▾`/`▸` marker, the steps
reveal at `opts.Depth ≥ 1` capped at `opts.LaneCap` with an `and N more` tail, and a
collapsed trail hints `▸ N step(s)` (the #16 affordance). `toJson` is untouched — the
full chain always rides the machine lens (the one-substrate law). The `ExplainViewTests`
assert the json lens only, so the change was free there; a new `ViewTests` pins the
cap, the collapse, and the full-chain-in-json law. (`LaneCap` kept its name — the #4
rename-to-`BreadthCap` is a deferred cosmetic.)

### Shipped next (2026-06-18) — #12: the `View.Table` primitive (the matrix gets a substrate)

**The first new `View` case since the chapter opened.** `Table of headers *
(string * Status) list list` — aligned columns over rows of (cell-text, status)
cells. `writeBlock` renders a Spectre `Table` with each cell `styled` by its status
(glyph + color, so the matrix reads on a `NoColors` console), wrapped defensively
(#3 — a malformed cell degrades to plain space-joined rows). `toJson` carries the
full grid — headers, every cell, its status — so a `--query` walks
`.rows[][?status=warn]` exactly as the human reads it (one substrate). **Panel-legality
decided** (the #1 cheat-sheet): a `Table` is NOT a `PanelRow`, only a top-level `Doc`
block — the drift hole stays closed by construction. The DU totality test gained
`table` AND `timeline` (the latter a pre-existing hand-list gap). The production
consumer follows in #13 (the bench fold); the `buildSurveyView` column conversion is
a deferred follow-on (its packed value needs a real column rethink).

### Shipped next (2026-06-18) — #13: the bench table folds into the substrate (the Table's first consumer)

**The perf surface joins the one lens.** `TtyRenderer.benchView : Bench.Stats list ->
View` builds a `Doc` (a header `Note` + a `View.Table` #12) over the nine bench columns,
cells `Neutral` (numbers are evidence, not a verdict). `OperatorConsole.dumpBench`'s `-v`
dump renders it through the View engine instead of `printfn`-ing a raw ASCII table — so
the bench gains color on a TTY, plain when piped, and the json / `--query` lens it never
had. Core's `Bench.renderTable` is untouched (still read by `PerfHarnessScenarios` /
`GeneratorScaleTests` / `BenchTests`), so the Core/Cli boundary holds and nothing is
orphaned — the doc's "keep Core's table" honored. This is `View.Table`'s first
PRODUCTION consumer, so the primitive is no longer test-only.

### Shipped next (2026-06-18) — #14: trend surfaces (the dead sparkline gets a producer)

**`Theme.sparkline` finally has a caller.** A `Spark of label * values: int list`
case — `writeBlock` renders the series as `▁▂▃▄▅▆▇█` (accent-colored, plain on
NoColors); `toJson` keeps the raw numbers the glyph compressed (one substrate). Its
consumer is the readiness board: a `changes / run` sparkline beside the canary dots,
of the per-run REGISTERED transform count — a settling model (fewer changes toward
cutover) reads as a falling line, a readiness signal in its own right. **Data note
(the doc's premise was partly off, like #19/#24):** `RunLedger` stores no
profile-time / coverage / warnings — its `LedgerRecord` carries `Registered` /
`Applied` / `Declined` — so the realistic series is the changeset count, not the
metrics the doc imagined. The board signature took one new `series: int list` param
(6 sites threaded, renders only at ≥ 2 points). The DU totality test gained `spark`.

---

## 1 — The map (all 25, by theme)

Status key: **● shipped** · **◐ partial / starved** · **○ not started** ·
**▢ design-only** · **✕ de-scoped (operator direction)**.

| # | Item | Theme | Status |
|---|---|---|---|
| 1 | Close the `Panel` drift hole | A. Substrate integrity | ● |
| 2 | Unforgeable markup (a `Markup` newtype) | A | ○ |
| 3 | Defensive render (markup-fault → plain fallback) | A | ● |
| 4 | A `RenderOptions` record | A | ● |
| 5 | One console/channel factory | A | ● |
| 6 | Totality + property test over the DU | A | ● |
| 7 | Honor `NO_COLOR` / `CLICOLOR_FORCE` | B. Color & accessibility | ● |
| 8 | Sealed `Color` palette | B | ✕ |
| 9 | High-contrast / colorblind theme | B | ✕ |
| 10 | Screen-reader narration lens | B | ✕ |
| 11 | **Responsive width** | B | ● |
| 12 | `View.Table` primitive | C. New primitives | ● |
| 13 | Fold `Bench.renderTable` into the substrate | C | ● |
| 14 | Trend surfaces (use `Theme.sparkline`) | C | ● |
| 15 | `Trail` gets cap-and-name + depth | C | ● |
| 16 | Unify the collapsed-affordance vocabulary | C | ● |
| 17 | Implement `--query` over `toJson` | D. The query lens | ● |
| 18 | Per-node addressing (`ViewPath`) | D | ● |
| 19 | Emit the intra-stage `summary.stageProgress` events | E. The Watch board | ◐ |
| 20 | Move the dwell off the emitting thread | E | ● |
| 21 | Instrument the other runs onto the spine | E | ◐ |
| 22 | Loud fallback for stage copy | E | ● |
| 23 | Build the Explore TUI | F. Explore + history | ● |
| 24 | A `diff <runA> <runB>` verb | F | ● |
| 25 | `explain <ssKey>` provenance drill-down | F | ◐ |
| 27 | **Delta-grade diff** — every channel + before/after evidence, by name | F | ● |
| 26 | **The Threshold** — earned milestone flourishes | G. Earned moments | ▢ |

> **Added (2026-06-18, operator direction — "give the interface a little sparkle"):**
> #26, a new theme. The cutover journey has earned moments (the R6 gate opening, the
> eject freeze) the calm interface leaves unmarked; #26 marks them with a tasteful,
> one-substrate flourish. The brainstorm + spec is §26 below.

> **De-scoped (2026-06-18, operator direction):** #8 sealed palette, #9
> high-contrast theme, #10 screen-reader narration. Recorded here so the cut is
> honest and re-openable, not forgotten. The accessibility ambition this chapter
> carries is **responsive width (#11)** alone; NO_COLOR (#7) already shipped.

---

## A — Substrate integrity (the `View` ADT engine)

### 1 · Close the `Panel` drift hole  ●  *(landed 2026-06-18; see §0)*

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

**As scoped (2026-06-18 — verified, not yet built).** A grep of every `Theme.*`
color-helper call confirms the discipline currently HOLDS: all 28 sites (`View.fs`
×20, `Watch.fs` ×8 — incl. `rowMarkup`'s `Markup.Escape` at line one) already escape
their data or pass a safe glyph. So there is **no live markup bug today**, and #3 now
CONTAINS the crash class regardless — #2 is pure type-level hardening (make the
escaping unforgeable), ripple bounded to those two files. The migration recipe, with
the one real pitfall named:
- A `Markup` private newtype with TWO smart constructors — `Markup.ofText s` (escapes
  raw data) and `Markup.raw s` (trusted: a glyph, or already-composed markup) — plus
  `Markup.value` for the final `safeMarkupLine`.
- `Theme.green`/etc. take and return `Markup` (wrap with `raw`); `colorOf` / `styled`
  compose `Markup`s (the glyph is `raw`, the value is `ofText`).
- **The pitfall:** the existing sites do `Markup.Escape x` → migrate one-for-one to
  `Markup.ofText x`; but a site already escaped and then re-wrapped DOUBLE-escapes,
  and most pure-pool data has no metacharacters, so the pool may NOT catch it. The
  special-char guard (`ViewTests` — `Order[Archive]` rendering its `[` literally,
  escaped ONCE) now exists; migrate under its cover and it stays green.

### 3 · Defensive render  ●  *(landed 2026-06-18; see §0)*

**Problem.** `console.MarkupLine` **throws** on malformed markup. The verdict panel
renders at the very *end* of an otherwise-successful run — the worst place to take
an exception. A display bug today can turn exit 0 into a crash.

**Fix.** Wrap the per-block render in a try that falls back to a plain
(`Markup.Escape`d / unstyled) line on `InvalidOperationException`, so a rendering
fault degrades to plain text and never fails the run it describes. Pairs with #2
(which removes most of the *cause*) — keep both: #2 prevents, #3 contains.

**As shipped.** `View.safeMarkupLine console markup` — try `MarkupLine`, and on
`InvalidOperationException` degrade to `Markup.Remove`d plain text (or the raw line
if even that won't parse). Every `writeBlock` line renderer routes through it, and
`writePanel`'s `console.Write(panel)` is wrapped to degrade to plain rows — the
verdict panel can no longer crash a good run. (The live `Watch` board renders via
Spectre `Live`, not this path, but its cells are escaped-by-construction —
`rowMarkup`'s `Markup.Escape` at the source — so it has no fault to contain.)

### 4 · A `RenderOptions` record  ●  *(landed 2026-06-18; the enabling refactor — see §0)*

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

**As shipped.** `RenderOptions { Depth; LaneCap; Width }` threaded through
`writeWith opts console v`; `write` / `writeToDepth` default `Width` from
`console.Profile.Width` (the live terminal, or the factory's redirected-sink pin).
The `Disclosure` recursion decrements via `{ opts with Depth = opts.Depth - 1 }`.
**`Color` was cut**: the channel is already resolved once in the console factory
(#5/#7), so a field here would be a zero-consumer duplicate (CLAUDE.md §5) — it
joins the record at the first render-time color gate. No behavior change: `Width`
is threaded but unread by the renderer until #11.

### 5 · One console/channel factory  ● *(shipped — see §0)*

### 6 · Totality + property test over the DU  ●  *(landed 2026-06-18; see §0)*

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

### 11 · Responsive width  ●  *(landed 2026-06-18; see §0)*

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

**As shipped.** `View.truncateTo budget s` tails a value with `Theme.ellipsis`
(`…`) when it overflows, run on the RAW value **before** escaping/coloring (so a
markup tag is never cut), at the prose line renderers — `Field` value, `Hero` /
`Note` / `Action` text, `Disclosure` headline, and the `Lane` headline (truncating
the *label*, never the trailing count). Budget = `opts.Width` (#4's
`RenderOptions.Width`, defaulted from `console.Profile.Width`) less the indent,
label, gutters, and glyph. `toJson` is untouched — width never reaches the machine
lens. *Deferred:* `Lane`/`Disclosure` **item** lines and `Trail` steps (the latter
is #15); the meter stays a fixed gauge (a gauge, not prose).

---

## C — New `View` primitives

### 12 · `View.Table`  ●  *(landed 2026-06-18; see §0)*

**Problem.** The capability "matrix" (`TtyRenderer.buildSurveyView`) renders each
environment as a `Field` line, not aligned columns — the doc calls it a matrix but
the substrate has no table. Diff channel-counts and the bench table want the same.

**Fix.** A `Table of headers: string list * rows: (string * Status) list list`
case (with its `toJson` arm and a `writeBlock` arm over a Spectre `Table`/`Grid`).
First consumers: `buildSurveyView`, then #13.

**Cheat sheet.** Mind #1 — a `Table` inside a `Panel` must be a `PanelRow` or it
hits the same drift hole. Decide whether `Table` is panel-legal before building.

**As shipped.** `Table of headers * (string * Status) list list` — `toJson` carries
headers + every cell + its status (a `--query` can walk `.rows`); `writeBlock` renders
an aligned Spectre `Table`, each cell `styled` by its status (so the matrix reads on a
NoColors console), wrapped defensively (#3). **Panel-legality decided:** a `Table` is
NOT a `PanelRow` — it is a top-level `Doc` block — so the §1 drift hole stays closed.
The first PRODUCTION consumer is the bench fold (#13); the `buildSurveyView` conversion
(decomposing its packed reachability/grant/CDC/users value into columns) is a larger
rethink, deferred as a clean follow-on. The DU totality test now also covers `timeline`
(a pre-existing hand-list gap, fixed in passing).

### 13 · Fold `Bench.renderTable` into the substrate  ○

**Problem.** The perf table dumped under `-v` (`OperatorConsole.dumpBench` →
`Bench.renderTable`) is a separate text path — no json, not `--query`-able,
doesn't share `Theme`.

**Fix.** Re-express it as a `View.Table` (#12) so the perf surface joins the one
lens. Watch the `Projection.Core.Bench` boundary — `renderTable` lives in Core;
the `View` projection belongs in `Cli`. Keep Core's table (it's used by the perf
gate's plain dump) and add a `Cli`-side `benchView : Bench.Stats list -> View`.

**As shipped.** `TtyRenderer.benchView : Bench.Stats list -> View` builds a `Doc`
(a header `Note` + a `View.Table` #12) — the nine bench columns, cells `Neutral`
(numbers are evidence, not a verdict). `OperatorConsole.dumpBench`'s `-v` dump now
renders it through the View engine (`View.write (View.consoleTo Console.Out)`), so the
perf surface gains color / json / `--query`. Core's `Bench.renderTable` stays — still
read by the perf scenarios (`PerfHarnessScenarios`, `GeneratorScaleTests`, `BenchTests`)
— so the Core boundary holds and nothing is orphaned. The `View.Table`'s first
production consumer.

### 14 · Trend surfaces — `Theme.sparkline` has zero callers  ●  *(landed 2026-06-18; see §0)*

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

**As shipped.** A `Spark of label * values: int list` case (`toJson` keeps the raw
series; `writeBlock` renders `Theme.sparkline` accent-colored, plain on NoColors).
The readiness board is its consumer — a `changes / run` sparkline beside the canary
dots, of the per-run REGISTERED transform count (a settling model trends down toward
cutover). **Data note (the premise was partly off):** the doc imagined "profile-time
/ coverage / warnings", but `RunLedger`'s `LedgerRecord` stores none of those — only
`Registered` / `Applied` / `Declined` — so the realistic series is the changeset
count. The board signature widened by one `series: int list` param (6 sites,
threaded; renders only at ≥ 2 points). The DU totality test gained `spark`.

### 15 · `Trail` gets cap-and-name + depth  ●  *(landed 2026-06-18; see §0)*

**Problem.** `View.Trail` (explain's transform chain, `RunFaces.explainView`)
renders flat regardless of depth — a long chain is a wall, with none of the §12
`laneCap` / `and N more` discipline `Lane` enforces.

**Fix.** Give `Trail` the same cap-and-name tail and depth-gated reveal `Lane`
has in `writeBlock`. Falls out almost free once #4's `RenderOptions.LaneCap`
(rename → `BreadthCap`) is the shared knob.

**As shipped.** The `Trail` arm now mirrors `Lane`: the label carries the `▾`/`▸`
marker, the steps reveal at `opts.Depth ≥ 1` capped at `opts.LaneCap` with an `and N
more` tail, and a collapsed trail hints `▸ N step(s)` (the #16 affordance). `toJson`
is untouched — the full chain always rides the machine lens. (`LaneCap` kept its
name; the #4 rename-to-`BreadthCap` is a future cosmetic, not done here.) The
`ExplainViewTests` assert the json lens only, so they were unaffected; a new
`ViewTests` pins the cap + collapse + the full-chain-in-json law.

### 16 · Unify the collapsed-affordance vocabulary  ●  *(landed 2026-06-18; see §0)*

**Problem.** A collapsed `Disclosure` shows `▸ N more`; a collapsed `Lane` hides
its items *silently* (the `Lane` arm of `writeBlock` prints only the header at
depth < 1). Inconsistent affordance.

**Fix.** A collapsed `Lane` should also hint `▸ N items`. One-line change in the
`Lane` arm; pin it with a `ViewTests` assertion.

**As shipped.** The collapsed `Lane` arm gains an `elif n > 0` branch hinting
`▸ N item(s)` (pluralized — `1 item`, not `1 items`), the same `▸`-affordance a
collapsed Disclosure shows. *Honest note:* the Lane HEADLINE already carries the
count, so the hint reinforces the openable affordance rather than adding new
information — the unification is of FORM (both collapsed nodes show a secondary `▸`
hint line), the deliberate consistency the item asks for. A `ViewTests` assertion
pins the hint and the singular grammar.

---

## D — The query / structured lens

### 17 · Implement `--query`  ●  *(landed 2026-06-18; see §0)*

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

**As shipped.** A new `Projection.Cli.Query` module — `walk` / `render`, a TOTAL
JSONPath-subset walker over the `JsonNode` `toJson` produces: object keys, array
index (`blocks[0]`), the wildcard (`blocks[]`), and one flat equality filter
(`blocks[?status=warn]`), with a key after a filter mapping over the survivors. A
miss yields `null`, never a throw. The wiring differs from the cheat-sheet's
"add a `query` arg": `--query` is a GLOBAL (it filters every answer regardless of
verb), so it rides a `TtyRenderer.queryPath` ref that `Program.main` sets from the
`--query <path>` flag — the `verboseMode` / `prettyMode` pattern, ZERO per-verb
threading. The ref lives in `TtyRenderer` (not beside the others in
`OperatorConsole`) only because `renderAnswer` — its single reader — compiles first.

### 18 · Per-node addressing (`ViewPath`)  ●  *(shipped 2026-06-18 — the NAVIGATE keystone; `--open` is its first consumer)*

**Problem.** `writeToDepth` decremented a single `int` uniformly, so sibling
`Disclosure`s opened *together*. There was no way to "open just this node, deeply,
leave the rest collapsed" — and the Explore Navigator (#23) needs exactly that.

**Fix (as designed).** A path of child indices, carried in `RenderOptions` (#4),
naming the one open branch; `writeBlock` reveals a node along it.

**As shipped — the refinement: depth and path COEXIST (not depth-as-degenerate-path).**
The sketch folded depth *into* the path (an `int depth` becoming "open everything to
N"). The build kept them as two orthogonal carriers instead: `RenderOptions` gained
`OpenPath: int list option` *alongside* `Depth`, and a container reveals its children
iff `Depth >= 1 OR it is on the open path` (`revealed`, View.fs). This is strictly more
conservative — with `OpenPath = None` (every existing caller) the three new private
helpers reduce to EXACTLY the prior `Depth` gate, so the whole prior render is
byte-identical *without* rewriting depth semantics:
- `revealed` → `opts.Depth >= 1` (the `Option.isSome None` disjunct is `false`).
- `descendInto` → the `Some (h :: rest)` arm is unreachable under `None`, so every
  descent falls to `{ opts with Depth = (if decrement then Depth - 1 else Depth) }` —
  unchanged opts for `Doc` (transparent), `Depth - 1` for `Disclosure`, exactly as before.
- `marker` → glyph-identical at the same `Depth >= 1` boundary.

`OpenPath` force-reveals *along its branch* on top of the ambient `Depth`: the addressed
child keeps the `Depth` (the path is its own reveal budget) and advances the path
(`descendInto` drops the matched head); every sibling leaves the open branch
(`OpenPath = None`) and reverts to the ambient `Depth` — the rest stays calm. `Some []`
opens the addressed node, after which its children fall back to ambient depth (no leak
past the addressed point). `toJson` never sees it — and this is enforced by the *type
system*, not convention: `toJson : View -> JsonNode` has no `RenderOptions` parameter, so
the path is syntactically unreachable from the structured lens (the one-substrate law).

**First consumer — `--open`, the headless half of the dig (so this is NOT a
zero-consumer build).** The cheat sheet warned "a `ViewPath` with no interactive consumer
is a zero-consumer build." It ships *with* one: `projection <verb> --open 1.0`
force-reveals exactly that dotted child-index branch of any pretty answer, the rest at
`--depth` — a focus lens an operator drives today, composing with `--depth` and `--query`.
`TtyRenderer.openPath` carries it; `renderAnswer` threads it into `writeWith` in exactly
one place (the pretty/plain `else`); the `--query` / `--json` branches take the structured
lens and ignore it. The parse (`Program.fs`) is total over a non-throwing `Int32.TryParse`
and accepts only a dotted list of NON-NEGATIVE indices — every other input (empty,
non-numeric, negative, overflow) is malformed → ignored → the answer renders calm, never
failing the run. This is the substrate the #23 Navigator's `→`/`Enter` will drive next —
its SECOND consumer.

**Tests + verification.** Three law-citing `ViewTests` pin the surgical-reveal predicate:
`OpenPath [1]` opens block 1 while block 0 stays collapsed (siblings stay calm); `OpenPath
[0;0]` threads two levels down to a leaf with NO `--depth` bump; `OpenPath [0]` opens an
addressed `Lane`'s items at ambient depth 0. Byte-identity (`OpenPath = None` unchanged) is
pinned by the whole pre-existing suite (worktree pure pool 3546 green, 0 failed). A
five-lens adversarial pass (byte-identity, surgical-reveal, parse-safety, the one-substrate
law, depth/path coexistence) found no defect — the `h >= 0` parse clamp above is the one
hardening it surfaced, taken prospectively for #23 (where a bare-node answer surface could
otherwise see `revealed` fire on a negative root index).

---

## E — The live Watch board

### 19 · Emit the intra-stage `summary.stageProgress` events  ◐  *(the long paths emit; full-export's stages don't)*

**Status correction (2026-06-18 — the premise was stale).** The original problem
statement said *"no producer calls it — grep finds `stageProgress` only in
`RunSpine`, `LogSink`, and `Watch`."* That is **no longer true**, and was the
load-bearing assumption behind the "single highest leverage, one emit site"
framing. The estate-scale paths — where an ETA matters most — now DO emit it:
`TransferRun` calls `LogSink.recordStageProgress "load"` from its bulk-load loops
(and `"retrust"` / `"retrust-skipped"`), and `MigrationRun` calls it `"deploy"`.
The board is live for transfer + migration; `WatchTests` / `MigrationCanaryTests`
pin the producer→fold path. The apparatus is *not* starved.

**The real remaining gap — and why it isn't a quick win.** What still emits no
intra-stage progress is **full-export** (`Pipeline.runWithConfig`'s `extract` /
`profile` / `emit` brackets). But its per-table work is NOT at the Pipeline
boundary the original framing assumed: `profile` probes inside
`Adapters.Sql.LiveProfiler.attach` (adapter layer); `emit`'s per-kind statement
build is in pure Core, and the file write is a *bulk* `Compose.writeWith` staging
move with no per-file loop at the boundary. So lighting full-export is a deeper,
adapter-touching change that needs a live-SQL (Docker) test — not the one-liner the
doc promised. Sequence it WITH #21 (the other runs onto the spine) and the
profile-probe instrumentation, not as a warm-up.

**Cheat sheet (still current).** The `Progress` honesty rule is already enforced in
`Watch.etaText` (no rate ⇒ no estimate) and the unknown-denominator case in
`progressText` (`Total ≤ 0` ⇒ plain count-up). So a producer that can't count ahead
may emit `total = 0` safely. Keep the emit at the Pipeline/adapter boundary, not in
pure Core. The producer pattern to copy is the `TransferRun` "load" loop.

### 20 · Move the dwell off the emitting thread  ●  *(shipped 2026-06-19 — the first concurrency primitive; DECISIONS 2026-06-19)*

**Problem.** `Watch.renderWatch` enforced the dwell with `Thread.Sleep` **on the
emitting thread, holding the `LogSink` lock** — its own doc flagged this safe *only*
because the run is synchronous + single-threaded. The board would serialize any future
concurrent realization stream.

**Fix.** Move the dwell to a drain loop on a render thread (THE_VOICE_BUILD_MAP §4.3):
the subscriber enqueues board transitions; the render thread pops, sleeps the floor
remainder, and `ctx.Refresh()`es. Do this *before* any parallel emitter lands.

**As shipped.** The subscriber is now strictly enqueue-and-return onto an unbounded
`BlockingCollection<Envelope>` (the FIRST concurrency primitive in `src` — DECISIONS
2026-06-19, CLAUDE.md §7), so `emit` never sleeps under the `LogSink` lock; `body` runs on
a `Task.Run` background thread (the producer) and a drain loop INSIDE `Live.Start` (the
ctx-affine thread) folds, sleeps the dwell remainder, and refreshes. The cross-thread
surface is the queue ALONE (`board`/`sw`/`lastRenderAt` stay drain-loop-local), and the
single `LogSink` lock already serialized envelope order, which the FIFO queue preserves.
`LogSink` is UNTOUCHED. Determinism holds because `Live.Start` is synchronous and the
callback joins `body` (`GetAwaiter().GetResult()`, exception-as-itself) after draining
every frame — so the done-frame is never lost and the tests still assert on the final
board (dwell=0). An OUTER `finally` REAPS `body` before clearing subscribers, so even a
render `IOException` (broken pipe / closed terminal) can't orphan the body with the writer
pinned to `Null` — the one fix a 5-lens adversarial concurrency pass surfaced (lock-held +
frame-loss certified clean by happens-before trace). Gate: pure pool 3558/0; the 4
`WatchInjectionTests` unchanged + `emit never sleeps (#20)` (timing, wide margin) +
`propagates a body exception as itself and never hangs` (the teardown path).

**The breathing spinner rides it (shipped same day).** The drain loop's 100ms idle wake now
advances a spinner `phase`: an ACTIVE stage line renders `Theme.spinner phase` (a braille
cycle, glyph-first so it reads on `NO_COLOR`) in place of the static `▸`, so a long-running
stage visibly BREATHES between events. `phase` advances on EVERY render — a folded frame OR
an idle wake — and the idle wake only fires when there's an active stage (no idle churn);
the dwell stays a floor on CHANGES, never added to the pulse. `rowMarkup`/`boardRows`/
`toRenderableWith` thread `phase` (a static render — stored boards, `toRenderable`, tests —
passes 0). Pinned by `Theme.spinner` (cycle + total) and a board-render test (an active line
wears the phase's frame).

**The stall-aware ETA rides it too (shipped same day — #20 breathing complete).** When an
ACTIVE stage goes quiet past `stallThresholdMs` (3s of wall-clock with no new frame, measured
by the drain loop off `lastRenderAt`), the board calls it `stalled`: the estimate degrades
from `~Ns remaining` to `stalled` (`progressTextStalled` — the honest §13 rule, never a
frozen countdown that keeps lying), and the spinner FREEZES + mutes (motion stops, so the
operator sees the stall both ways). `stalled` threads beside `phase` with NO test churn —
`progressText`/`lineText` keep their signatures as `false`-wrappers over new
`progressTextStalled`/`lineTextWith` siblings. Pinned by a stall test (`stalled` replaces the
ETA, the count still shows, it threads into the active line). #20 and its breathing are
DONE: off-thread dwell · spinner · stall-aware ETA.

### 21 · Instrument the other runs onto the spine  ◐

**Problem.** Only `full-export` emits the stage spine, so `--watch` is silent /
empty for Migration / Transfer / Eject / Drift.

**Fix.** Seed each from its `RunSpine` (the `Watch.seededOf` path already exists)
and emit the same `<stage>.started` / `summary.stageCompleted` envelopes from
their run faces. THE_VOICE_BUILD_MAP slice 2's remainder.

### 22 · Loud fallback for stage copy  ●  *(landed 2026-06-18; see §0)*

**Problem.** `Watch.statementText` does `match Voice.lookup code with Some c -> …
| None -> code` — on a miss it shows the **raw event code** (`emit.foo.started`) to
the operator. The answer path already learned this lesson (NM-47's
`Voice.fallbackSurface`); the board never did.

**Fix.** Either a register-correct board fallback, or — cheaper and stronger — a
totality test pinning every stage key's `.started` / `summary.stageCompleted` /
`watch.*` code as voiced, so a new stage can never leak an identifier to a human.
Mirror the existing `code ⇔ copy` totality test.

**As shipped.** The "cheaper and stronger" option: two `VoiceTotalityTests` derive
the board's codes FROM THE SPINES (`Spines` × `RunSpine.keys`) — not a hand-list —
and assert every `<stageKey>.started`, plus `summary.stageCompleted` /
`watch.stageHalted` / `watch.runTitle` / `watch.runDone`, is voiced. A new spine
stage that forgets its copy now fails in the pure pool, never by leaking
`<key>.started` to an operator at render. No production change — `statementText`'s
fallback stays, but the derived test proves it unreachable for board codes (and the
current set IS complete).

---

## F — Explore + run history

### 23 · Build the Explore TUI  ●  *(shipped 2026-06-18 — dig-as-motion + the run-history walk; all three nav axes)*

**Problem.** The interactive inspector (DYNAMIC_DISPLAY §3.3) is named and unbuilt.
Every dependency exists: the `View` engine, `Comparison`, `RunLedger`, and (once
#18 lands) `ViewPath`.

**Fix.** A pure `Navigator.step : ConsoleKey -> Model -> Model` reducer + a pure
`project : Model -> View`, driven by Spectre `Live` + `Console.ReadKey`. Keep the
reducer pure and property-test it (`step` total over `ConsoleKey`, idempotent at
the tree bounds) — the codebase's whole ethos applied to a TUI. `←`/`→` walks run
history (the ledger); `↑`/`↓` moves the cursor; `→`/`Enter` deepens the
`ViewPath`.

**As shipped — the dig-as-motion core (`Navigator.fs` + `buildInspectView`).** The dig
stops being a re-run (`--depth`/`--open`) and becomes a MOTION: `↑`/`↓` move the cursor
among siblings, `→`/`Enter` dig in, `←` retreat. The cursor IS the open path — exactly
one spine open at a time (the dug thread), the rest calm — so the whole TUI is a thin
loop around two PURE functions: `step` and `project : Model -> RenderOptions`, which
feeds the cursor path straight into `OpenPath` (#18) with NO remap (the cursor indices
ARE the render child-indices — `Navigator.children` enumerates the same `Doc`-blocks /
`Disclosure`-detail lists `writeBlock` iterates, so cursor and reveal can never desync).
`step` is property-tested TOTAL over `ConsoleKey` and CLAMPING — the cursor can never
leave the tree (9 `NavigatorTests`; an adversarial pass added an exhaustive BFS over
thousands of trees, including empty `Doc`, leaf roots, empty-detail `Disclosure`s).

**`inspect` joined the substrate (the one-substrate dividend).** `runInspect` was a
`printfn` dump with no machine lens — the very drift this chapter fights. It is now
`buildInspectView : Run.Run -> View` (a `Doc` of the verdict essence over diggable
`Disclosure`s), so `inspect <id>` gains the json / `--query` lens for free
(smoke-verified end-to-end: pretty / `--json` / `--query` all over one value), opens
the Navigator on a real terminal, and renders the same document one-shot when piped /
`--json` / `--query`.

**Two deliberate deviations from the sketch.** (1) CLEAR-and-redraw on `Console.Out`,
NOT a Spectre `Live` region — a `Live` region plus a blocking `ReadKey` is the
terminal-exclusivity hazard; clear-and-redraw is simpler and dodges it. (2) Built
WITHOUT #20 — the cheat sheet said "sequence #20 first," but the Navigator's `ReadKey`
loop is its OWN concurrency shape, independent of the Watch `Channel` path (verified:
it never runs under a board, and the interactive predicate requires stdin+stdout+stderr
all be real terminals before the loop is reachable, so a non-TTY can never hang it).
`Ctrl-C` is delivered as a keypress (`TreatControlCAsInput`) so the loop quits CLEANLY
through `finally`, restoring the terminal.

**The time axis (#10 — shipped same day).** `inspect` with NO id opens the LATEST run and
`PgUp`/`PgDn` scrub older/newer through the ledger (newest-first by ISO `Ts`). The walk is
pure SHELL I/O — each frame re-`buildInspectView`s on demand via a `loadAt` closure the
Navigator stays free of (no `Run` dependency in `Navigator.fs`), so the reducer never sees
a run and `step` stays pure and unchanged; the single-run and history shells share one
`driveLoop` (`run tree` = `driveLoop 1 0 (fun _ -> tree)`). So the three Explore axes the
Fix named are ALL delivered: `↑`/`↓` cursor, `→`/`Enter` dig, run-history walk (on
`PgUp`/`PgDn`, since `←`/`→` are the dig). Headless parity holds (no id → newest run's
document one-shot on pipe / `--json` / `--query`).

**The cursor caret (shipped same day).** The `OpenPath = Some []` tip now wears a
left-gutter `❯` (`Theme.cursor` — accented for the pretty lens but glyph-first, so it
survives `NO_COLOR`), making the cursor visible even on a LEAF `Field`/`Hero` that carries
no disclosure marker. It HUGS the content (replaces the indent's last two columns, so
alignment is unchanged and the width budgets still read `indent.Length`) and improves
`--open` too — the focused node is now marked. Byte-identical without an open path: `lead =
indent` whenever `OpenPath ≠ Some []`, so the whole pure pool stays green (3556/0), and a
`ViewTests` pins both the caret-on-a-leaf and the no-caret-when-calm halves.

**L2 — the read surfaces are control surfaces (shipped same day).** `Navigator.present`
is the one predicate every navigable face shares: on a real terminal it OPENS the dig;
piped / `--json` / `--query` render the same document one-shot through `renderAnswer` (the
headless fallback, byte-unchanged). Both `inspect` and `diff` route through it now — so the
changeset is dug LIVE (the move-lanes scrub under `↑`/`↓`, each focused lane expanding via
`OpenPath`), while a pipe still gets the calm answer (verified: a piped `diff` returns
through the headless path, never hanging on `ReadKey`). `explain` is the obvious next
caller; the move-lanes go richer when delta-grade widens `Lane` to per-item status.

**What rides on this shell next.** The focus/filter (L1). The Navigator holds a **cursor
over data the `View` already carries**, never a second copy of run state (DYNAMIC_DISPLAY
§7 discipline 6) — a cursor axis added to the `Model`, not new state.

### 24 · A `diff <runA> <runB>` verb  ●  *(already reachable via `Ref`; premise stale)*

**Status correction (2026-06-18 — the premise was stale).** The "missing verb" is
NOT missing. `Ref.parse` resolves a `@runId` to the stored run's catalog (`Ref.fs`:
`if s.StartsWith("@") then RunArtifact …`), and the existing `diff <a> <b>`
(`RunFaces.runDiff`) resolves BOTH operands through that same `Ref` machinery before
`Comparison.catalog.Between` and `renderAnswer`. So the run-to-run diff the item asks
for is **`projection diff @runA @runB`** today — the uniform-operand design (`Ref.fs`
header: "every verb becomes `verb <ref>…` and they compose, `diff model.json
@run-9`") already delivers it; a dedicated verb would be redundant. `CompareTests`
exercises the `@runId` operand shape on the sibling `compare`.

**What a dedicated surface would still add — and why it waits.** Only the Explore
time-axis (#23's `←`/`→` over the ledger) is the genuinely-new affordance, and that
is a #23 consumer, not a separate verb. So #24's remaining value folds into #23, not
a standalone build (CLAUDE.md §5 — no zero-consumer verb).

### 25 · `explain <ssKey>` provenance drill-down  ◐

**Problem.** `RunFaces.explainView` already projects one node's transform trail +
findings through `View.Trail`, rendered through the *same* `EventProjection`
lens the event stream uses (so the two can't drift). It stops at the trail.

**Fix.** Extend it into the full provenance dig — every transform, decision,
finding, and suggested fix for one `SsKey`, progressively disclosed (so a deep
trail isn't a wall — needs #15), and walkable inside Explore (#23, #18). The dig,
made interactive.

### 27 · Delta-grade diff  ●  *(shipped 2026-06-19 — the DIFF cluster; the walkable changeset gets every channel + before/after evidence, by name)*

**Problem.** `CatalogDiff` computes a RICH delta — per-kind attribute / reference /
index channels (C1) + the catalog sequence channel + the kind-OWN facets (NM-17),
each Added / Removed / Renamed / Reshaped(facets) — and RETAINS both source + target
catalogs. But `Comparison.renderCatalogLanes` surfaced only KIND moves + attribute
RESHAPES (as facet-names). References, indexes, sequences, attribute add/remove/rename,
and kind-facets all rode through INVISIBLE to the walkable changeset; and a reshape
named WHICH facet moved, never the value it moved between.

**As shipped (Cli-only — `Comparison.fs`; zero Core touch; rides the shipped L2 Navigator).**
- *Every channel reaches the move lanes.* `renderCatalogLanes` folds kinds + columns +
  relationships + indexes + sequences + kind-own facets into the same four HOMOGENEOUS
  move-lanes (rename / reshape / add / remove — the reversibility badge IS the move,
  never per-item, per the operator's "don't widen `Lane` to per-item status"). Items are
  channel-qualified (`column Customer.Email`, `relationship Order.Customer`,
  `index Customer.IX_…`, `sequence SEQ_…`). The Navigator digs the richer lanes live for
  free (`Navigator.present` reads whatever the diff produces).
- *Before/after EVIDENCE on the ALTER surfaces.* Attribute + reference reshapes carry the
  value moved — `column Customer.Qty: type text → integer`, `relationship Order.Customer:
  on delete no action → cascade` — resolved from the retained source/target
  (`attributeEvidence` / `referenceEvidence`). Option-valued facets (default / computed)
  render the presence transition. Index reshapes keep the facet-name form (their facets
  are lists / grouped knobs — a before→after would be a wall); sequence / kind-facet too.
- *Legible by NAME, not SsKey.* `rootOriginal` of an `OssysOriginal` key is a bare GUID
  (`CatalogReader`), so a per-column changeset keyed by `rootOriginal` would be a GUID-wall
  on a real estate. The lanes name by `Name` (a per-side SsKey → Name `nameIndex`, built
  once — discover-once/derive-pure; `rootOriginal` fallback), matching the rename lane that
  already did. **No new `View` case** — the richer items ride the existing `Lane`, so
  `toJson` carries them and the one-substrate law holds by construction.

**Tests (14, `ComparisonTests`).** One per newly-surfaced channel landing in its move-lane;
before/after evidence for type / nullability / FK-on-delete; a render-level test asserting
the full multi-channel document renders BY NAME with evidence (the human-lens side of
one-substrate); a legibility test pinning Name-not-SsKey; + one-substrate json witnesses.
Full pure pool **3575/0**.

**Cheat sheet / the follow-ons named.**
- *Concrete storage width is a Core modulus, not a render gap.* `changedFacets` compares the
  semantic `PrimitiveType` (`Integer`), not the concrete storage type, so `int → bigint`
  produces NO facet — the literal handoff example needs a `CatalogDiff` facet (`AttributeFacet`
  + `changedFacets` + the apply patch), out of this Cli-only scope.
- *Other operator surfaces still show `rootOriginal` (GUIDs).* `explain` / the reconciliation
  reports / the bench dump name entities by `rootOriginal` (`RunFaces`). The diff now diverges
  toward legibility; a shared `SsKey.displayName cat`-style projection unifying every surface
  is the clean follow-on (kept out of scope — it touches every operator face).
- *Before/after for index / sequence / kind-facet reshapes* (the structural channels) is
  deferred — facet-name is the right grain until a consumer wants the list-level diff.

#### Polish (2026-06-19/20 — ten slices on top of #27; `DECISIONS 2026-06-19/20`)

The DIFF cluster's polish wave — more detail, more safety, more scannable, more
scopable. #A–#D + the rollup are pure-`Comparison.fs`; the scoping verbs add a thin
DU/parse thread. No new `View` case for #A–#E (the rollup reuses `Table`); one
substrate throughout (every item rides `toJson`). Pure pool **3589/0** (+18 law-citing
tests across `ComparisonTests` + `MovementSurfaceTests`).

- **#A — the FK name-wall fix (a real defect).** `referenceEvidence` rendered the
  `target` / `source column` facets via `SsKey.rootOriginal` — a bare GUID for
  `OssysOriginal` keys, so an FK retarget read `target <hex> → <hex>`, illegible exactly
  where the operator must read "this FK now points at a DIFFERENT table." Now threaded
  through the per-side name resolvers (`nm srcNames` / `nm tgtNames`), as the qualifier
  already was. Proven with OssysOriginal fixtures (the synthesized-key fixtures can't
  catch it — their `rootOriginal` IS a name).
- **#B — before/after for the structural channels** (closes the §27 follow-on). Index
  `uniqueness` (`not unique → unique` FAILS on apply if dupes exist), sequence scalars
  (`start 1 → 1000` — opposite risk from `1000 → 1`), and kind `active` (`yes → no`, a
  deactivation) now carry the value. The list/grouped facets (index columns, modality,
  triggers, checks, cache) stay facet-name — a before→after there would be a wall.
  Retired the now-dead `channelReshapes`.
- **#C — the data-risk surface + an honest statement.** Pulls the genuinely
  DATA-TOUCHING subset out by name — `dataDrops` (a dropped table/column loses rows) +
  `rewrites` (a type conversion, `null → not null`, a cascade added, a uniqueness
  gained). They drive (1) a "review these first" callout — a single `Bad`-badged lane
  promoted to the TOP of the substantiation (a SEPARATE surface, so the move-lanes stay
  homogeneous), and (2) an honest `catalogStatement` that leads amber on a data-touching
  RESHAPE, not just a removal: a zero-drop migration that adds a NOT NULL column used to
  lead CALM ("no removals") — now `N changes · K may rewrite data · review`.
- **#D — deterministic item ordering.** Lane items `List.sort` in the canonical assembly
  (noun-prefixed, so channel-grouped then name-ordered) — the capped first 12 are now the
  scannable first 12, not an arbitrary SsKey-order 12. Both lenses see one order (T1).
- **#E — the scoping verbs.** At scale an operator scopes rather than scrolls. `--only
  <channel>` scopes the DISPLAY (keeps one channel's lane items + danger callout via
  `keepChannel`/`channelNoun`; the statement + ‖δ‖ panel + rollup stay whole for
  orientation); `--module <name>` scopes the COMPUTATION (keeps the named module's kinds
  before `Between` — a smaller diff). `Comparison` gained `*Scoped` variants (the
  historical functions are `None` wrappers, every test holds); `ExplainDiff` gained
  `channel`/`onlyModule` options threaded parse→dispatch→`runDiff`; `--help` documents both.
- **#F — the per-module "top movers" rollup.** A diff spanning ≥ 2 modules carries a
  churn-sorted `module · changes` `View.Table` (`moduleRollup`, kind→module via
  `Catalog.allModulesKinds`) just before the ‖δ‖ panel — "which module is hot" at a
  glance, pairing with `--module` (see it → dig it). Absent for single-module diffs.

##### Scale wave (2026-06-20 — intentional at ~310 tables / hundreds of concurrent concerns)

The operator named the real target: a ~310-table estate, where one channel can carry
hundreds of FK concerns at once. The principle: every list surface states its true
total, leads with what matters, and offers a path to ALL of it — never a silent
12-item wall.

- **#G — the danger callout scales by risk CATEGORY.** Past a threshold (12) the
  "may rewrite or lose data" callout groups by category (dropped / type change /
  null → not null / primary key change / identity change / cascade delete / uniqueness
  gained) — each a diggable sub-group with its count, the loud total on top — so 347
  concerns read as their risk PROFILE, not 12 arbitrary lines. `dataDrops`/`rewrites`
  now carry `(category, text)`; small sets stay the flat callout lane.
- **#H — the navigable module-grouped move-lane (the at-scale MARQUEE).** A move-lane
  that is LARGE and spans ≥ 2 modules renders as a navigable `Disclosure` TREE grouped
  by module (hottest first; `moduleOfItem` extracts the kind name and resolves via
  `Catalog.allModulesKinds`) — default depth shows the module profile, digging reveals
  the items. **No Navigator change needed:** the tree is `Disclosure`s, which
  `Navigator.children` already nests, so the `OpenPath`=child-index invariant is never
  touched — the in-place navigability the prior follow-on flagged as the hard marquee,
  delivered cleanly. A small / single-module lane stays the flat `Lane`.
- **#I — cross-surface legibility (the displayName chapter, started).** The run/apply
  narration is SsKey-keyed (GUID walls on a real estate). `verify-data` now names
  tables/columns by `Name` (the contract threaded out of the read task as
  `(report, contract)`; the payload build extracted as the pure, testable
  `integrityPayload`). `explain` was already fine (its `rootOriginal` is the match key).
- **#J — the displayName chapter FINISHED.** One shared **`Catalog.nameIndex`** (Core)
  now backs every face — `Comparison.nameIndex` + the `RunFaces` narration delegate to
  it (the three copies consolidated). The **transfer report** names by `Name`: the
  transfer face holds no catalog (the report is built deep in the engine), so
  `TransferReport` gained a `Names : Map<SsKey,string>` index, populated from the
  contract catalog at each construction site, resolved in `narrateTransferReport` /
  `narrateDropExit` (empty ⇒ `rootOriginal` fallback). Additive metadata only — the
  transfer behaviour is unchanged (Docker `TransferCanary` 29/29). `suggest-config`
  names via `report.ReadCatalog`. A4 holds — `nameIndex` is a terminal DISPLAY
  projection; identity stays the SsKey.

**Still-named follow-ons:** the L1 `/`-filter on the Navigator `Model` (very powerful
now that #H gives a grouped tree to filter); concrete storage width (`int → bigint`, a
Core diff-modulus, DECISIONS-gated); a `--stat`-only summary mode (the #F rollup is its
always-on form); resume-from-refusal (operator-deferred — its architecture is in the
older `HANDOFF` letter).

---

## G — Earned moments (the sparkle)

> Added 2026-06-18 on operator direction ("give the interface a little sparkle …
> focus on what vibrancy calls out to you"). The one theme that is about *delight*
> rather than correctness — but held to the same law: a flourish that drifts from the
> record would violate the prime directive, so the flourish IS a record.

### 26 · The Threshold — earned milestone flourishes  ▢

**The idea.** The system shepherds a model toward cutover — a one-way door (the
eject, after which there is no upstream to re-derive from). That journey has *earned
moments*: the R6 gate opening (N consecutive green → eligible), the eject freezing,
the streak climbing. The calm, no-drama interface reports them in the same monotone
as run 2. #26 marks them — once, when the line is *crossed* — with a tasteful,
distinct flourish. The vibrancy is earned CONTRAST: in a muted interface, one earned
moment glows like a summit register, not a party.

**Why it belongs here (the discipline that makes it native).**
- **One substrate — even delight is accountable.** The flourish is a
  `View.Milestone of event: string * Status * lines: string list` case with a
  `toJson` arm, so the machine lens RECORDS it (`{"kind":"milestone",
  "event":"cutover-eligible","atRun":10}` — `--query`-able, ledger-able). The moment
  is real on both lenses; a celebration that drifted from the record would break the
  one law this module keeps. This celebration *is* the record.
- **No drama (THE_VOICE register).** Statement-first ("Ten consecutive green checks.
  This pair is ready for cutover."), semantic-not-decorative, glyph-survives-`NO_COLOR`
  (a `✦` + a `Theme.rule` hairline carries the frame, not color alone). No confetti,
  no exclamation — earned quiet.
- **Fire ONCE on crossing, not every check.** `buildReadinessView` is stateless, so
  the *transition* (run N-1 not-yet, run N eligible) is detected at the `RunFaces`
  ledger-read site that already holds the full `records` list (the same seam #14's
  series threads through) — one predicate, no new state. Idempotent: a re-check while
  already-eligible shows the calm board, not the flourish.

**The earned moments (the trigger set).** The R6 gate opens (`ConsecutiveGreen` first
reaches `Threshold`); the eject seals (the append-forever freeze — arguably the most
sacred moment in the system); optionally a mid-climb streak marker (5 of 10).

**The slices.**
1. *The substrate (clean):* the `Milestone` case + `toJson` + `writeBlock` (the framed
   flourish; a `Theme.rule` token for the hairline) + the DU-totality entry + a
   both-lenses test. Self-contained, low-risk.
2. *The first trigger:* fire-once-on-crossing at the readiness site (the gate opening) —
   the transition predicate + a test pinning the fire-ONCE law.
3. *The second trigger:* the eject freeze (the one-way door) — the moment that most
   earns it.

**Adjacent sparks (same instinct, lighter — fold in or spin out).**
- *Storytelling sparkline:* color #14's `Spark` bars by trend (settling greens,
  churning ambers) — the changeset trend gets a mood. A `Spark` variant + a color map.
- *Horizon on the timeline:* the cutover strip shows where you've been (`●●●✕●●▸`);
  append the distance still to go as faint future dots (`▸●●●○○○`) — a journey wants a
  destination, not just a past.
- *First light on the live board:* when the final stage completes clean, let the Watch
  board's `✓`s settle left-to-right, once. Motion = life. Gated on #20 (the off-thread
  dwell) — a reason to want #20, not a thing to rush.

**Cheat sheet.** Mind #1 — a `Milestone` is a top-level `Doc` block, never a
`PanelRow` (the drift hole stays closed). Mind #11 — the framed lines are prose, so
they truncate like a `Field`; the frame (`Theme.rule`) scales to width. The
`Milestone` event names join the `Voice` register if they carry copy (the `code ⇔
copy` totality, #22) — but the lines can be authored at the call site for slice 1,
voiced later.

---

## 2 — How to run this: the DAG, the contention map, the waves

### 2.1 The dependency DAG

What blocks what. An item with no arrow into it can start *now*.

```
  (close the live bug first — independent of everything)
  #1 PanelRow ──► #6 totality test

  (the substrate enabler — one signature change everything rebases onto)
  #4 RenderOptions ──┬──► #11 responsive width
                     ├──► #15 Trail cap + depth
                     └──► #18 ViewPath ──► #23 Explore TUI ──┬──► #24 diff verb
                                              ▲              └──► #25 explain dig
  #20 dwell off-thread ─────────────────────┘  (the concurrency substrate
                                                 #23's input/render loop reuses)
  #15 ───────────────────────────────────────────────────────► #25 (deep trail ≠ wall)

  (new primitives — serialized only by their shared file, View.fs)
  #12 View.Table ──► #13 bench fold
  #14 trend / Spark ;  #16 collapsed affordance   (each standalone)

  (fully independent — different files entirely, see 2.2)
  #2 unforgeable markup ;  #3 defensive render
  #17 --query ;  #19 stageProgress emit ;  #21 spine ;  #22 stage-copy fallback
```

The **longest chain** (the critical path) is `#4 → #18 → #23 → #25`. Everything
else is shorter and most of it is leaf work. Start the critical path early even
though it's not the highest single-item value — it's the only thing that gates the
marquee (#23).

### 2.2 The file-contention map (why parallelism is bounded)

Parallelism here is limited by **one file, not by logic**: `View.fs` is the
single-writer hot spot — the DU, `writeBlock`, `writePanel`, and `toJson` all live
there, so every substrate/primitive item edits it and they **cannot** be split
across concurrent agents without colliding. The strategy is to recognize that the
*other* lanes touch entirely disjoint files and let those run free.

| Lane | Items | Files touched | Parallel-safe with… |
|---|---|---|---|
| **Substrate** (serial within) | #1 #4 #11 #15 #16 #18 #2 #3 | `View.fs` (+ `Theme.fs` for #2; tests) | everything in other lanes |
| **Primitives** (serial within, shares `View.fs`) | #12 #13 #14 | `View.fs`, `TtyRenderer.fs`, `OperatorConsole.fs` | Watch, Query lanes |
| **Watch / Pipeline** | #19 #20 #21 #22 | `Watch.fs`, `LogSink.fs`, Pipeline producers, `Voice.fs` | Substrate, Primitives, Query |
| **Query** | #17 | `Program.fs`, `TtyRenderer.renderAnswer`, a new walker module | every other lane |
| **Verbs / Explore** | #23 #24 #25 | new `Navigator` module, `RunFaces.fs` | Substrate (after its deps land) |

The Substrate and Primitives lanes both edit `View.fs`, so they are **one serial
queue** in practice. Watch, Query, and (most of) Verbs are genuinely concurrent.

### 2.3 The parallel wave plan (2–3 agents / worktrees)

Run agents in separate git worktrees (`isolation: "worktree"`) so each lane has its
own tree; merge per wave. The split is by *file ownership*, which is what keeps the
merges clean.

**Wave 1 — close the bug, wake the board, redeem the lens (3 lanes, no overlap):**
- *Agent A (View.fs owner):* **#1 + #6** — close the live drift hole, lock it.
- *Agent B (Watch/Pipeline owner):* **#19** — wake the starved ETA board; then **#22** (stage-copy totality) while warm.
- *Agent C (Program/Query owner):* **#17** — implement `--query`.

**Wave 2 — the enabler and its first fruit:**
- *Agent A:* **#4 `RenderOptions`** (the signature change — land it alone, it
  rebases the rest), then **#11 responsive width** (its first consumer) and the
  cheap **#16**.
- *Agent B:* **#20 dwell off-thread** then **#21 spine** (the concurrency substrate
  #23 will reuse).
- *Agent C:* fold back; begin **#18 `ViewPath`** on top of A's `#4` (coordinate the
  `View.fs` handoff — A passes the baton, C does not edit `View.fs` until A merges).

**Wave 3 — primitives and the interactive arc:**
- *Agent A:* **#2 + #3** (markup safety), **#12 → #13** (table + bench fold), **#14**
  (trends), **#15** (Trail).
- *Agent B/C converge:* **#23 Explore TUI** (needs #18, #20) → **#24**, **#25** fall
  out.

> **The one coordination rule:** `View.fs` has exactly one editor per wave. When a
> lane needs it and another holds it, the holder merges first and passes the baton.
> Everything else merges in any order.

## 3 — Value × effort, and the quick-win shortlist

Rough triage to spend a time-box well. Effort: **S** ≈ an hour, **M** ≈ a session,
**L** ≈ multi-session.

| Item | Value | Effort | Note |
|---|---|---|---|
| #1 PanelRow + #6 | **High** | S | A *live* lens-drift bug; the test is the proof. Do first. |
| #19 stageProgress | Med | M–L | **Premise corrected** — transfer/migration already emit; only full-export's stages remain, an adapter-deep change (not the one-liner). |
| #17 `--query` | **High** | M | **● shipped** — `Query` walker over `toJson`, global `--query` flag. |
| #23 Explore TUI | **High** | L | The marquee; gated by #18 + #20. |
| #4 RenderOptions | Med (enabler) | M | Unblocks #11/#15/#18; land alone. |
| #11 responsive width | Med | M | User-visible; the carried accessibility item. |
| #2 markup safety | Med | M–L | Kills a crash class; ripple across call sites. |
| #20 dwell off-thread | Med | M–L | Unblocks concurrency; hardest to test. |
| #14 trends | Med | S–M | `Theme.sparkline` already exists, zero callers. |
| #24 diff verb | — | — | **● already reachable** — `diff @runA @runB` via the `Ref` uniformity; the new part folds into #23. |
| #25 explain dig | Med | M | Needs #15 so a deep trail isn't a wall. |
| #21 spine | Med | M | `--watch` for the other verbs. |
| #12 → #13 table | Med | M / S | #13 is cheap once #12 exists. |
| #18 ViewPath | Med | M | Build *with* #23, not speculatively. |
| #3 defensive render | Med | S | Cheap insurance against a markup crash. |
| #22 stage-copy fallback | Med | S | A totality test; mirrors NM-47. |
| #15 Trail cap | Low | S | Near-free after #4. |
| #16 collapsed affordance | Low | S | One-line + a test. |

**Quick-win shortlist** (S-effort, ship in a sitting, no deep deps): **#1+#6, #16,
#22, #3, #24**. A good "warm up or wind down" set when a larger lane is mid-flight.

## 4 — Commit & PR batching

- **One thematic PR per wave-lane**, not per item — but **one commit per landable
  unit** (feature + its test together; never a "tests later" commit — the test *is*
  the codification here). #628 is the template: factory + doc in one PR.
- **#4 lands in its own commit/PR.** It changes the shared `write`/`writeToDepth`
  signatures; isolating it makes the rebase for every consumer a clean cherry, and
  makes the blast radius reviewable.
- **Land #1 and #6 in the same commit** (the doc and the house law both say so).
- **Write a one-line `DECISIONS.md` entry** for the two items that touch standing
  surface area: **#4** (a shared render-interface change) and, retroactively, the
  **NO_COLOR / CLICOLOR_FORCE** convention shipped in #5/#7 (a new operator-facing
  env contract worth recording). The rest are refinements whose test is their
  codification — no entry needed (CLAUDE.md §6).

## 5 — Risk register

- **#2 (markup newtype) ripple.** Touches every `Theme.*` color call. *Mitigate:*
  migrate incrementally — keep the `string` helpers as thin shims that escape +
  wrap during the transition, delete them once call sites move; let the compiler
  drive the list.
- **#20 (dwell off-thread) is the hardest to test** — it's real concurrency over
  Spectre `Live`. *Mitigate:* keep the queue/drain logic pure and unit-test *that*;
  the thread + `ctx.Refresh()` shell stays thin and is exercised by an integration
  smoke, not asserted frame-by-frame.
- **#23 (TUI input loop).** `Console.ReadKey` blocks and the terminal must be
  restored on exit/exception. *Mitigate:* the reducer is pure and property-tested;
  the impure loop is a thin `try/finally` that restores the console — the same
  discipline as `Watch.renderWatch`'s `finally`.
- **#17 (`--query`) scope creep.** A full JSONPath engine is a rabbit hole.
  *Mitigate:* bound it to the surface-shaped subset (`.blocks[]`, `.<key>`, index,
  one flat `[?status=warn]` filter) and say so in the verb's doc; grow at the
  second real query, not before.
- **#19 (progress emit) perf.** Emitting inside a hot per-row loop can add
  overhead. *Mitigate:* emit every N rows (not per row), and remember a perf-gate
  verdict is **void if anything else runs on the host** (CLAUDE.md §4.13) — measure
  the emit cost solo.

## 6 — Definition of done (every item)

A cut is finished when **all** of these hold — no exceptions, this is the bar:

1. Builds clean under `TreatWarningsAsErrors` (`dotnet build src/Projection.Cli/Projection.Cli.fsproj`).
2. `scripts/test.sh fast` green (launch bare in background; poll `test.sh status`).
3. If it adds or changes a `View` case: it ships its `toJson` arm **and** a test
   asserting the machine lens keeps what the human lens caps/collapses/truncates
   (the `laneCap` / `Disclosure` tests are the template).
4. The test's backticked name **cites the law** it proves.
5. Its row in the §1 map is flipped to ● — **citing symbols, never line numbers**
   (CLAUDE.md §8; a restated line number is a defect).
6. The commit carries the required trailer; the PR is updated.

## 7 — Standing notes for anyone cutting here

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
