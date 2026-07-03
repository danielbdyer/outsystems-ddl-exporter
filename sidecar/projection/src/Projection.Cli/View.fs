module Projection.Cli.View
// LINT-ALLOW-FILE: the operator-console TUI render module — function-local render-loop mutation + terminal Spectre markup composition at the console boundary

open System.Text.Json.Nodes
open Spectre.Console

/// The renderable + queryable document primitive (`REPORTING_HORIZON` —
/// masterful base #3; "one substrate, many lenses" made a type). Every
/// operator surface BUILDS a `View`; one engine renders it to a pretty
/// console (`write` over a colored `IAnsiConsole`), to plain text (`write`
/// over a `NoColors` console), or to a structured tree (`toJson`, which a
/// `--query` then walks). The discriminating predicate: pretty / plain / json
/// all run over the SAME value — the human and machine lenses can never drift,
/// because they are projections of one document, not parallel print paths.
///
/// `Status` drives glyph + color (semantic, never decorative); a `Field`'s
/// `value` is plain data, so `toJson` is clean. `Theme` is the token layer
/// beneath the pretty lens.

type Status =
    | Ok
    | Warn
    | Bad
    | Pending
    | Neutral

/// The closed set of rows a `Panel` lays out in its two-column grid — a labeled
/// value, a ratio gauge, or the next-action line. Typed (not just `View list`) so
/// a non-row block cannot be placed in a panel and then silently dropped by the
/// renderer: the prior `Panel of View list` let `writePanel` render three cases
/// and `| _ -> ()` the rest, while `toJson` kept them — the human and machine
/// lenses diverging on the one value, the precise drift this module exists to
/// forbid. A closed `PanelRow` makes that unrepresentable (the house "closed DU
/// makes a law unforgeable" discipline, CLAUDE.md §6).
/// The cases carry their own names (`Labeled` / `Gauge` / `Next`), distinct from
/// the `View` DU's `Field` / `Meter` / `Action`, so a bare `View.Field` stays
/// unambiguous everywhere. The JSON lens still emits the historical
/// `field` / `meter` / `action` kinds — the wire format is unchanged; only the
/// in-memory constructor names are panel-specific.
[<RequireQualifiedAccess>]
type PanelRow =
    /// A labeled value (`Status` supplies glyph + color); the historical `field`.
    | Labeled of label: string * value: string * Status
    /// A ratio gauge with a trailing suffix; the historical `meter`.
    | Gauge of label: string * filled: int * total: int * suffix: string
    /// The next-action row; the historical `action`.
    | Next of string

type View =
    /// A sequence of blocks, rendered as lines (the board shape).
    | Doc of View list
    /// A bordered panel with a header, containing field/meter/action rows
    /// (the verdict-panel shape). The rows are a closed `PanelRow` so the panel
    /// cannot carry a block the renderer would silently drop (see `PanelRow`).
    | Panel of title: string * PanelRow list
    /// The lead verdict line — glyph + emphasized text.
    | Hero of Status * string
    /// A labeled value. `value` is plain; `Status` supplies glyph + color.
    | Field of label: string * value: string * Status
    /// A ratio gauge (the R6 meter) with a trailing suffix.
    | Meter of label: string * filled: int * total: int * suffix: string
    /// Canary-history dots (green ● / red ✕).
    | Dots of label: string * verdicts: string list
    /// A sparkline of a numeric series — min..max mapped across `▁▂▃▄▅▆▇█`
    /// (`Theme.sparkline`). A trend at a glance (changeset size / coverage /
    /// warnings over runs). `toJson` carries the raw series so the machine lens
    /// keeps the numbers the glyph compresses.
    | Spark of label: string * values: int list
    /// The cutover timeline header — the canary-history strip (`cells`, newest
    /// last) with an optional `present` marker, beside the R6 gate meter
    /// (`filled`/`total`). The spine of the display (`DYNAMIC_DISPLAY` §4): where
    /// this run sits on the arc to cutover. `toJson` carries the strip as
    /// structure so the machine lens keeps the full series + present index.
    | Timeline of label: string * cells: string list * filled: int * total: int * present: int option
    /// An aligned grid — column `headers` over `rows`, each row a list of
    /// (cell-text, `Status`) cells (the cell's status drives its glyph + color, so a
    /// matrix reads on a `NoColors` console too). The capability matrix and the bench
    /// table are tables, not stacked `Field`s. Deliberately NOT a `PanelRow` (a
    /// `Table` is a top-level `Doc` block, never a panel cell), so the §1 drift hole
    /// stays closed. `toJson` carries headers + every cell + its status.
    | Table of headers: string list * rows: (string * Status) list list
    /// A label over an ordered step list (explain's transform trail).
    | Trail of label: string * steps: (string * string option) list
    /// A move-lane: a glyph + label + a status badge (the move's reversibility),
    /// over indented item rows (one move's changes). The changeset groups into
    /// lanes — one per move (`INSTRUMENT` slice 2). Progressively disclosed: the
    /// summary line always shows; the items reveal at render-depth ≥ 1.
    | Lane of glyph: string * label: string * Status * items: string list
    /// A progressive-disclosure node — a one-line status-glyphed `headline` that
    /// OPENS into `detail`, one depth-level per step (Apple-HIG disclosure; the
    /// substrate of the dig — `DYNAMIC_DISPLAY` progressive revealing). Rendered
    /// shallow it shows the headline + a `▸ N more` affordance; rendered deeper
    /// it reveals the detail, each child one level further in. `toJson` ALWAYS
    /// carries the full detail + count — the machine lens never loses the tree
    /// the human collapsed.
    | Disclosure of headline: string * Status * detail: View list
    /// A muted footer note.
    | Note of string
    /// The next-action line (principle #5).
    | Action of string
    /// A blank line.
    | Blank

// --- Status → presentation -------------------------------------------------

/// The complete presentation of a `Status` — glyph + color-wrap + wire tag —
/// resolved in ONE match (recon #22). Adding or re-tuning a status is a single
/// edit here; `glyphOf` / `colorOf` / `statusTag` are thin projections of it, so
/// every call site is unchanged. (Replaces three parallel `Status` matches that
/// each had to be kept in sync by hand.)
type private Presentation = { Glyph: string; Color: string -> string; Tag: string }

let private presentationOf : Status -> Presentation =
    function
    | Ok      -> { Glyph = Theme.ok;      Color = Theme.green;  Tag = "ok" }
    | Warn    -> { Glyph = Theme.warn;    Color = Theme.yellow; Tag = "warn" }
    | Bad     -> { Glyph = Theme.bad;     Color = Theme.red;    Tag = "bad" }
    | Pending -> { Glyph = Theme.pending; Color = Theme.yellow; Tag = "pending" }
    | Neutral -> { Glyph = "";            Color = id;           Tag = "neutral" }

let private glyphOf (status: Status) : string = (presentationOf status).Glyph

let private colorOf (status: Status) (s: string) : string = (presentationOf status).Color s

let private statusTag (status: Status) : string = (presentationOf status).Tag

/// glyph + colored value, as Spectre markup.
let private styled status (value: string) : string =
    let g = glyphOf status
    let body = if g = "" then Markup.Escape value else g + " " + Markup.Escape value
    colorOf status body

// --- The pretty/plain lens (write to any IAnsiConsole) ---------------------

/// Write a markup line defensively (#3). `MarkupLine` THROWS (an
/// `InvalidOperationException`) on malformed markup — a stray `[`, an unknown style
/// — and the verdict panel renders at the very END of an otherwise-successful run,
/// the worst place to take an exception (a display bug turning exit 0 into a crash).
/// On a markup fault, degrade to the line with its markup stripped (`Markup.Remove`),
/// or the raw text if even that won't parse — so a render fault never fails the run
/// it describes. #2 removes most of the CAUSE (unescaped data reaching a `Theme.*`);
/// this CONTAINS it — keep both (one prevents, one contains).
let safeMarkupLine (console: IAnsiConsole) (markup: string) : unit =
    try console.MarkupLine markup
    with :? System.InvalidOperationException ->
        let plain = try Markup.Remove markup with :? System.InvalidOperationException -> markup
        console.WriteLine plain

let private writePanel (console: IAnsiConsole) (title: string) (rows: PanelRow list) : unit =
    let grid = Grid()
    grid.AddColumn() |> ignore
    grid.AddColumn() |> ignore
    // Total over `PanelRow` — every row a panel can hold has a render arm; there
    // is no `| _ -> ()` to silently swallow one (the drift the closed type ended).
    for r in rows do
        match r with
        | PanelRow.Labeled (label, value, st) ->
            grid.AddRow(Theme.muted (Markup.Escape label), styled st value) |> ignore
        | PanelRow.Gauge (label, filled, total, suffix) ->
            grid.AddRow(
                Theme.muted (Markup.Escape label),
                sprintf "%s   %s" (Theme.meter filled total) (Markup.Escape suffix)) |> ignore
        | PanelRow.Next text ->
            grid.AddRow(Theme.muted "next", Theme.accent (Markup.Escape text)) |> ignore
    let panel = Spectre.Console.Panel(grid)
    panel.Header <- PanelHeader(sprintf " %s " (Markup.Escape title))
    panel.Border <- BoxBorder.Rounded
    // Defensive (#3): the cells above are escaped-by-construction, but a future
    // unescaped `Theme.*` in a cell would otherwise crash the verdict panel at the
    // end of a good run; on a markup fault, degrade to plain rows under the title.
    try console.Write(panel)
    with :? System.InvalidOperationException ->
        safeMarkupLine console (Theme.bold (Markup.Escape title))
        for r in rows do
            match r with
            | PanelRow.Labeled (label, value, _) ->
                safeMarkupLine console (sprintf "  %s   %s" (Markup.Escape label) (Markup.Escape value))
            | PanelRow.Gauge (label, filled, total, suffix) ->
                safeMarkupLine console (sprintf "  %s   %s   %s" (Markup.Escape label) (Theme.meter filled total) (Markup.Escape suffix))
            | PanelRow.Next text ->
                safeMarkupLine console (sprintf "  next   %s" (Markup.Escape text))

/// The calm default render depth — "one level open" (the essence + the first
/// level of the dig; deeper levels collapsed behind their affordance). The
/// operator opens further with `--depth` (or `→` in Explore).
[<Literal>]
let defaultDepth = 1

/// The most items a `Lane` renders before capping with an `and N more` tail —
/// the §12 breadth cap (surface the few that matter, name the remainder). The
/// full list always survives on `toJson` (the machine lens never caps).
[<Literal>]
let laneCap = 12

/// The plain-sink width pin, and `RenderOptions`'s default width budget. Spectre
/// auto-sizes to the terminal, but a redirected sink (pipe / file) reports no
/// width and collapses the board / gate grids mid-cell — so the factory pins a
/// redirected console to this. (Previously inlined as a bare `100` at every render
/// site.) `RenderOptions.Width` defaults here too: the factory pin and the
/// truncation budget (#11) share one constant.
[<Literal>]
let plainWidth = 100

/// The render-policy carrier (#4) — the pretty-lens knobs gathered into one value
/// so width (#11), depth, and breadth thread as a record instead of a bare
/// `int depth` plus scattered module constants. The structured lens (`toJson`)
/// reads NONE of these: every field is a pretty-lens concern — the one-substrate
/// law, where a cap or a truncation never reaches the machine tree. `Color` is
/// deliberately absent: the color channel is resolved once, in the console factory
/// (#5/#7), so a field here would be a zero-consumer duplicate (CLAUDE.md §5); it
/// joins this record at the first render-time color gate, not speculatively before
/// one exists.
type RenderOptions =
    { /// Disclosure depth — levels revealed before a node collapses behind `▸ N more`.
      Depth: int
      /// Breadth cap — the most `Lane` items rendered before the `and N more` tail.
      LaneCap: int
      /// Width budget (columns) the pretty lens truncates cells to (#11); the
      /// machine lens ignores it (width is a pretty-only concern).
      Width: int
      /// The surgical-reveal path (#18). `Some path` force-reveals exactly the
      /// branch the child-index `path` names — each element a level down — while every
      /// OTHER branch stays at the ambient `Depth` (the rest stays calm); `Some []`
      /// opens the addressed node itself. `None` (the default every existing caller
      /// passes) is the calm whole-tree-to-`Depth` case, so the render is byte-identical
      /// without it. The dig's `→`/`←` (#11) and the Navigator drive it; `toJson`
      /// ignores it (a path is a pretty-lens concern, like depth).
      OpenPath: int list option }

/// The calm defaults — depth 1, breadth 12, width 100 — each formerly a bare
/// module constant. `write` / `writeToDepth` start from these and override Width
/// from the live console, so a real terminal's width flows into the budget.
let defaultOptions : RenderOptions =
    { Depth = defaultDepth; LaneCap = laneCap; Width = plainWidth; OpenPath = None }

/// Whether a container reveals its children here — the calm `Depth` gate, OR a forced
/// open because this node is on the surgical-reveal path (#18). With `OpenPath = None`
/// this reduces to EXACTLY today's `Depth >= 1`, so every existing caller is byte-identical.
let private revealed (opts: RenderOptions) : bool =
    opts.Depth >= 1 || Option.isSome opts.OpenPath

/// The render policy a container hands its child at index `i` (#18). The ON-path child
/// keeps the `Depth` (the path is its own reveal budget) and advances the path; every
/// other child leaves the open branch (`OpenPath = None`) and drops one `Depth` level
/// when the container is depth-decrementing (`decrement = true` for `Disclosure`; `Doc`
/// is transparent and passes `false`). With `OpenPath = None` this is EXACTLY today's
/// `{ opts with Depth = opts.Depth - 1 }` (or, for `Doc`, the unchanged `opts`).
let private descendInto (opts: RenderOptions) (decrement: bool) (i: int) : RenderOptions =
    match opts.OpenPath with
    | Some (h :: rest) when h = i -> { opts with OpenPath = Some rest }
    | _ -> { opts with OpenPath = None; Depth = (if decrement then opts.Depth - 1 else opts.Depth) }

/// The disclosure marker for a node with children: open (`▾`) when this level is being
/// revealed (by depth or by the open path), closed (`▸`) when collapsed, blank when childless.
let private marker (isRevealed: bool) (hasChildren: bool) : string =
    if not hasChildren then " "
    elif isRevealed then Theme.expanded
    else Theme.collapsed

/// The WIDTH cap (#11) — the dual of the §12 breadth cap (`laneCap`). Truncate a
/// plain value to a column budget, tailing it with `…` when it overflows, so a
/// long line tails instead of wrapping mid-cell on a narrow terminal. It runs on
/// the RAW value BEFORE markup escaping/coloring, so a color tag is never cut — the
/// markup stays well-formed by construction (the same fault #2/#3 contain, here
/// avoided outright). The budget is in columns; for the BMP data these surfaces
/// carry, `String.Length` is the column count. A budget below one collapses to the
/// bare tail (the line is already at its margin); an empty value stays empty. The
/// machine lens NEVER sees this — width is a pretty-lens concern only, so `toJson`
/// keeps the full, untruncated value (the one-substrate law the `laneCap` tests pin).
let private truncateTo (budget: int) (s: string) : string =
    if budget < 1 then (if s = "" then "" else Theme.ellipsis)
    elif s.Length <= budget then s
    else s.Substring(0, budget - 1) + Theme.ellipsis

/// Columns a status glyph occupies in `styled` output — the glyph plus its trailing
/// space, or nothing for `Neutral` (which emits no glyph). The width budget
/// subtracts this so a truncated value still fits beside its glyph.
let private glyphCols (st: Status) : int = if glyphOf st = "" then 0 else 2

let rec private writeBlock (console: IAnsiConsole) (opts: RenderOptions) (indent: string) (v: View) : unit =
    // #23 — the cursor caret: the focused node (the `OpenPath` tip, `Some []`) gets a
    // left-gutter `❯` hugging its content, so the cursor is visible even on a LEAF (a
    // `Field`/`Hero` carries no disclosure marker). The caret REPLACES the indent's last
    // two columns (content stays column-aligned) while the width budgets keep reading
    // `indent.Length` — the caret renders at the same width as the two spaces it took. With
    // `OpenPath ≠ Some []` (every non-cursor node, and every `--open`-less render) `lead =
    // indent`, byte-identical. `lead` prefixes only a node's OWN headline line, never its
    // children (each child computes its own) nor a node's sub-rows (steps / items / hints).
    let lead =
        if opts.OpenPath = Some [] && indent.Length >= 2
        then indent.[0 .. indent.Length - 3] + Theme.accent Theme.cursor + " "
        else indent
    match v with
    | Doc blocks ->
        // #18 — thread the surgical-reveal path through the (transparent) block sequence:
        // the on-path child continues the path, the rest render at the ambient policy.
        // With OpenPath = None this is exactly the old `for b in blocks`.
        blocks |> List.iteri (fun i b -> writeBlock console (descendInto opts false i) indent b)
    | Blank -> console.WriteLine()
    | Panel (title, fields) -> writePanel console title fields
    | Hero (st, text) ->
        // indent + glyph (when statusful) + a 2-space gutter, then the bold text.
        let budget = opts.Width - indent.Length - (if glyphOf st = "" then 0 else 1) - 2
        safeMarkupLine console (sprintf "%s%s  %s" lead (colorOf st (glyphOf st)) (Theme.bold (Markup.Escape (truncateTo budget text))))
    | Field (label, value, st) ->
        // indent + label + a 3-space gutter + the status glyph; the value takes the rest.
        let budget = opts.Width - indent.Length - label.Length - 3 - glyphCols st
        safeMarkupLine console (sprintf "%s%s   %s" lead (Theme.muted (Markup.Escape label)) (styled st (truncateTo budget value)))
    | Meter (label, filled, total, suffix) ->
        safeMarkupLine console (
            sprintf "%s%s   %s   %s"
                lead (Theme.muted (Markup.Escape label)) (Theme.meter filled total) (Markup.Escape suffix))
    | Dots (label, verdicts) ->
        safeMarkupLine console (sprintf "%s%s   %s" lead (Theme.muted (Markup.Escape label)) (Theme.canaryDotsMarkup verdicts))
    | Spark (label, values) ->
        // The series as `▁▂▃▄▅▆▇█` (universal glyphs, safe — no escape); accent-colored
        // for the pretty channel, plain on NoColors. The numbers ride `toJson`.
        safeMarkupLine console (sprintf "%s%s   %s" lead (Theme.muted (Markup.Escape label)) (Theme.accent (Theme.sparkline values)))
    | Timeline (label, cells, filled, total, present) ->
        // The strip + the R6 gate meter on one line — the present marker rides
        // the dots, the cutover ratio trails (e.g. `●●●●✕●●▸  ▇▇▇▇▇▇▇░░░  7/10`).
        safeMarkupLine console (
            sprintf "%s%s   %s   %s   %s"
                lead (Theme.muted (Markup.Escape label))
                (Theme.timelineMarkup cells present)
                (Theme.meter filled total)
                (Theme.muted (sprintf "%d/%d" filled total)))
    | Table (headers, rows) ->
        // An aligned Spectre table; the cell's status drives its glyph + color via
        // `styled` (a Neutral cell is plain text). Spectre measures its own column
        // widths, so #11's width cap doesn't apply here (as for `Panel`). Wrapped
        // defensively (#3): a malformed cell degrades to plain space-joined rows
        // rather than crashing the run.
        let t = Spectre.Console.Table()
        t.Border <- TableBorder.Rounded
        for h in headers do t.AddColumn(Theme.muted (Markup.Escape h)) |> ignore
        for row in rows do
            t.AddRow(row |> List.map (fun (text, st) -> styled st text) |> List.toArray) |> ignore
        try console.Write(t)
        with :? System.InvalidOperationException ->
            safeMarkupLine console (sprintf "%s%s" indent (Theme.muted (Markup.Escape (String.concat "   " headers))))
            for row in rows do
                safeMarkupLine console (sprintf "%s%s" indent (row |> List.map (fun (text, st) -> styled st text) |> String.concat "   "))
    | Trail (label, steps) ->
        // #15 — the transform chain gets the same depth-gated reveal + breadth cap a
        // `Lane` has: the label always shows (with the ▾/▸ marker); the steps reveal
        // at depth ≥ 1, capped at `opts.LaneCap` with an `and N more` tail so a long
        // chain isn't a wall; collapsed (depth < 1) it hints `▸ N steps`. The full
        // chain always rides `toJson` — the machine lens never caps.
        let n = List.length steps
        let m = marker (revealed opts) (n > 0)
        safeMarkupLine console (sprintf "%s%s %s" lead m (Theme.muted (Markup.Escape label)))
        if revealed opts then
            for (step, detail) in steps |> List.truncate opts.LaneCap do
                match detail with
                | Some d -> safeMarkupLine console (sprintf "%s%s %s %s %s" indent Theme.arrow (Markup.Escape step) Theme.dot (Markup.Escape d))
                | None   -> safeMarkupLine console (sprintf "%s%s %s" indent Theme.arrow (Markup.Escape step))
            if n > opts.LaneCap then
                safeMarkupLine console (
                    sprintf "%s   %s %s" indent (Theme.muted Theme.collapsed)
                        (Theme.muted (sprintf "and %s more" (Theme.humane (n - opts.LaneCap)))))
        elif n > 0 then
            safeMarkupLine console (
                sprintf "%s   %s %s" indent (Theme.muted Theme.collapsed)
                    (Theme.muted (sprintf "%s step%s" (Theme.humane n) (if n = 1 then "" else "s"))))
    | Lane (glyph, label, st, items) ->
        // A lane is a pre-baked disclosure of one move: the summary line always
        // shows (the true count, humane); the items reveal at depth ≥ 1, capped
        // at `laneCap` with an `and N more` tail so a thousand-change lane never
        // becomes a wall (`THE_VOICE.md` §12 — cap the breadth, name the
        // remainder). The full list always rides `toJson` — the machine lens
        // keeps what the human capped.
        let n = List.length items
        let m = marker (revealed opts) (n > 0)
        // Width cap (#11): truncate the LABEL, not the whole headline, so the glyph
        // and the load-bearing humane count survive the cut — a long lane name tails
        // with `…`, the count never falls off the line.
        let count = Theme.humane n
        let label' = truncateTo (opts.Width - indent.Length - 2 - (glyph.Length + 1) - 2 - count.Length) label
        safeMarkupLine console (
            sprintf "%s%s %s" lead m (colorOf st (Markup.Escape (sprintf "%s %s  %s" glyph label' count))))
        if revealed opts then
            for item in items |> List.truncate opts.LaneCap do
                safeMarkupLine console (sprintf "%s   %s %s" indent (Theme.muted Theme.dot) (Markup.Escape item))
            if n > opts.LaneCap then
                safeMarkupLine console (
                    sprintf "%s   %s %s" indent (Theme.muted Theme.collapsed)
                        (Theme.muted (sprintf "and %s more" (Theme.humane (n - opts.LaneCap)))))
        elif n > 0 then
            // #16 — a collapsed lane hints what it holds with the same `▸ N items`
            // affordance a collapsed Disclosure shows (`▸ N more`), so the items are
            // not left implied by the header count alone (the collapsed-affordance
            // vocabulary, unified across the two node kinds).
            safeMarkupLine console (
                sprintf "%s   %s %s" indent (Theme.muted Theme.collapsed)
                    (Theme.muted (sprintf "%s item%s" (Theme.humane n) (if n = 1 then "" else "s"))))
    | Disclosure (headline, st, detail) ->
        let rev = revealed opts
        let m = marker rev (not (List.isEmpty detail))
        // marker + space, then the styled headline (glyph + space + text when statusful).
        let budget = opts.Width - indent.Length - 2 - glyphCols st
        safeMarkupLine console (sprintf "%s%s %s" lead m (styled st (truncateTo budget headline)))
        if rev then
            // #18 — the on-path child keeps the open path (descendInto), every sibling
            // drops a Depth level. With OpenPath = None this is exactly the old depth-1
            // recursion, so the existing dig stays byte-identical.
            detail |> List.iteri (fun i child -> writeBlock console (descendInto opts true i) (indent + "  ") child)
        elif not (List.isEmpty detail) then
            safeMarkupLine console (
                sprintf "%s  %s %s" indent (Theme.muted Theme.collapsed)
                    (Theme.muted (sprintf "%d more" (List.length detail))))
    | Note text -> safeMarkupLine console (sprintf "%s%s" lead (Theme.muted (Markup.Escape (truncateTo (opts.Width - indent.Length) text))))
    | Action text -> safeMarkupLine console (sprintf "%s%s %s" lead Theme.arrow (Theme.accent (Markup.Escape (truncateTo (opts.Width - indent.Length - 2) text))))

/// Render with an explicit policy (#4) — the single carrier #11 (width) and #15
/// (breadth) read. `opts.Width` is respected verbatim (the caller owns the
/// budget); `write` / `writeToDepth` below are the thin defaults over it that pull
/// Width from the live console.
let writeWith (opts: RenderOptions) (console: IAnsiConsole) (v: View) : unit =
    writeBlock console opts "  " v

/// Render to a chosen disclosure depth — the dig revealed `depth` levels down,
/// deeper nodes collapsed behind their `▸ N more` affordance. The interactive
/// `→`/`←` of Explore and the `--depth` flag both ride this. Width defaults from
/// the live console (`console.Profile.Width`) — a redirected sink is pinned to
/// `plainWidth` by the factory, a TTY reports its real width — so #11's truncation
/// budget tracks the actual terminal.
let writeToDepth (console: IAnsiConsole) (depth: int) (v: View) : unit =
    writeWith { defaultOptions with Depth = depth; Width = console.Profile.Width } console v

/// Render at the calm default depth — a colored console is the pretty lens, a
/// `NoColors` console is the plain lens. One renderer, two outputs.
let write (console: IAnsiConsole) (v: View) : unit =
    writeWith { defaultOptions with Width = console.Profile.Width } console v

// --- The console factory (one sink → one configured console) ---------------
// The render sites used to each re-derive the same facts about a sink: is it
// redirected (→ pin a width so grids don't collapse), and what does the color
// channel want. That logic lived inlined — a bare `100` and a `ReferenceEquals`
// redirect check — at six call sites in `TtyRenderer` plus `Watch`. It lives
// here once now, and is the single place the published color conventions are
// honored (the contract `Theme` names but nothing read).

/// The published color conventions the `Theme` contract names. `NO_COLOR` (set
/// to any non-empty value, per no-color.org) suppresses color even on a real
/// terminal — the accessibility signal wins; `CLICOLOR_FORCE` (set and not "0")
/// forces color even into a redirected sink. Read at the boundary (rendering is
/// not Core). `Some false` → force plain; `Some true` → force color; `None` →
/// let Spectre detect from the sink. NO_COLOR is checked first, so it wins when
/// both are set — a declared preference for no color is never overridden.
let private envColorOverride () : bool option =
    match System.Environment.GetEnvironmentVariable "NO_COLOR" with
    | null | "" ->
        match System.Environment.GetEnvironmentVariable "CLICOLOR_FORCE" with
        | null | "" | "0" -> None
        | _               -> Some true
    | _ -> Some false

/// Build a configured console for a sink. `redirected` is whether THIS writer is
/// a pipe/file — the caller knows it (`Console.IsErrorRedirected` for stderr,
/// `IsOutputRedirected` for stdout, always true for an in-memory writer). A
/// redirected sink gets its width pinned (and Spectre strips its color of its own
/// accord); `NO_COLOR` / `CLICOLOR_FORCE` then override the color channel per the
/// published conventions.
let consoleFor (writer: System.IO.TextWriter) (redirected: bool) : IAnsiConsole =
    let settings = AnsiConsoleSettings(Out = AnsiConsoleOutput(writer))
    (match envColorOverride () with
     | Some false -> settings.ColorSystem <- ColorSystemSupport.NoColors
     | Some true  -> settings.Ansi <- AnsiSupport.Yes
     | None       ->
         // A redirected sink renders PLAIN (2026-07-02). Spectre cannot detect
         // redirection through an injected `AnsiConsoleOutput` (the prior
         // "Spectre strips its color of its own accord" assumption held only
         // for the DEFAULT console), so ANSI was spraying into pipes and
         // files. `CLICOLOR_FORCE` above still forces color into a pipe when
         // the operator declares it.
         if redirected then
             settings.ColorSystem <- ColorSystemSupport.NoColors
             settings.Ansi <- AnsiSupport.No)
    let console = AnsiConsole.Create settings
    if redirected then console.Profile.Width <- plainWidth
    console

/// Whether a writer is a non-terminal sink: an in-memory / file writer, OR
/// `Console.Out` / `Console.Error` when that standard stream is redirected. The
/// `ReferenceEquals` redirect check the render sites used to inline.
let private redirectedFor (writer: System.IO.TextWriter) : bool =
    if   System.Object.ReferenceEquals(writer, System.Console.Error) then System.Console.IsErrorRedirected
    elif System.Object.ReferenceEquals(writer, System.Console.Out)   then System.Console.IsOutputRedirected
    else true

/// Build a configured console for a writer, deriving the redirect state from the
/// writer itself — the single entry point every render site now calls.
let consoleTo (writer: System.IO.TextWriter) : IAnsiConsole =
    consoleFor writer (redirectedFor writer)

// --- The structured lens (toJson; a --query walks this) --------------------

let rec toJson (v: View) : JsonNode =
    let s (x: string) : JsonNode = nonNull (JsonValue.Create x)
    let i (x: int) : JsonNode = nonNull (JsonValue.Create x)
    let obj (pairs: (string * JsonNode) list) : JsonNode =
        let o = JsonObject()
        for (k, n) in pairs do o.[k] <- n
        o
    let arr (xs: View list) : JsonNode =
        let a = JsonArray()
        for x in xs do a.Add(toJson x)
        a
    match v with
    | Doc blocks -> obj [ "kind", s "doc"; "blocks", arr blocks ]
    | Panel (title, rows) ->
        // The "fields" array keeps the SAME field/meter/action kinds the prior
        // `Panel of View list` emitted — the machine lens is byte-identical; only
        // the in-memory type became closed.
        let a = JsonArray()
        for r in rows do
            let rowObj =
                match r with
                | PanelRow.Labeled (label, value, st) ->
                    obj [ "kind", s "field"; "label", s label; "value", s value; "status", s (statusTag st) ]
                | PanelRow.Gauge (label, filled, total, suffix) ->
                    obj [ "kind", s "meter"; "label", s label; "filled", i filled; "total", i total; "suffix", s suffix ]
                | PanelRow.Next text ->
                    obj [ "kind", s "action"; "text", s text ]
            a.Add(rowObj)
        obj [ "kind", s "panel"; "title", s title; "fields", a ]
    | Hero (st, text) -> obj [ "kind", s "hero"; "status", s (statusTag st); "text", s text ]
    | Field (label, value, st) ->
        obj [ "kind", s "field"; "label", s label; "value", s value; "status", s (statusTag st) ]
    | Meter (label, filled, total, suffix) ->
        obj [ "kind", s "meter"; "label", s label; "filled", i filled; "total", i total; "suffix", s suffix ]
    | Dots (label, verdicts) ->
        let a = JsonArray()
        for x in verdicts do a.Add(s x)
        obj [ "kind", s "dots"; "label", s label; "verdicts", a ]
    | Spark (label, values) ->
        // The raw series rides the machine lens — the sparkline glyph compresses it,
        // `toJson` keeps the numbers (the one-substrate law).
        let a = JsonArray()
        for x in values do a.Add(i x)
        obj [ "kind", s "spark"; "label", s label; "values", a ]
    | Timeline (label, cells, filled, total, present) ->
        // The full series + present index ride the structure regardless of the
        // pretty marker — the machine lens never loses the arc.
        let a = JsonArray()
        for x in cells do a.Add(s x)
        let o = JsonObject()
        o.["kind"] <- s "timeline"
        o.["label"] <- s label
        o.["cells"] <- a
        o.["filled"] <- i filled
        o.["total"] <- i total
        (match present with Some p -> o.["present"] <- i p | None -> ())
        o :> JsonNode
    | Table (headers, rows) ->
        // The full grid rides the structure — headers, every cell, its status — so
        // a `--query` can walk `.rows[][?status=warn]` exactly as the human reads it.
        let h = JsonArray()
        for x in headers do h.Add(s x)
        let rs = JsonArray()
        for row in rows do
            let cells = JsonArray()
            for (text, st) in row do cells.Add(obj [ "text", s text; "status", s (statusTag st) ])
            rs.Add(cells)
        obj [ "kind", s "table"; "headers", h; "rows", rs ]
    | Trail (label, steps) ->
        let a = JsonArray()
        for (step, detail) in steps do
            let stepObj = JsonObject()
            stepObj.["step"] <- s step
            match detail with
            | Some d -> stepObj.["detail"] <- s d
            | None   -> ()
            a.Add(stepObj :> JsonNode)
        obj [ "kind", s "trail"; "label", s label; "steps", a ]
    | Lane (glyph, label, st, items) ->
        let a = JsonArray()
        for x in items do a.Add(s x)
        obj [ "kind", s "lane"; "glyph", s glyph; "label", s label; "status", s (statusTag st); "items", a ]
    | Disclosure (headline, st, detail) ->
        // The full detail rides the structure regardless of render depth — the
        // machine lens never loses the tree the human collapsed.
        let a = JsonArray()
        for x in detail do a.Add(toJson x)
        obj [ "kind", s "disclosure"; "headline", s headline; "status", s (statusTag st)
              "detail", a; "count", i (List.length detail) ]
    | Note text -> obj [ "kind", s "note"; "text", s text ]
    | Action text -> obj [ "kind", s "action"; "text", s text ]
    | Blank -> obj [ "kind", s "blank" ]
