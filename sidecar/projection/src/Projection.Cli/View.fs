module Projection.Cli.View

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
    /// The cutover timeline header — the canary-history strip (`cells`, newest
    /// last) with an optional `present` marker, beside the R6 gate meter
    /// (`filled`/`total`). The spine of the display (`DYNAMIC_DISPLAY` §4): where
    /// this run sits on the arc to cutover. `toJson` carries the strip as
    /// structure so the machine lens keeps the full series + present index.
    | Timeline of label: string * cells: string list * filled: int * total: int * present: int option
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

let private glyphOf =
    function
    | Ok -> Theme.ok | Warn -> Theme.warn | Bad -> Theme.bad
    | Pending -> Theme.pending | Neutral -> ""

let private colorOf status (s: string) : string =
    match status with
    | Ok -> Theme.green s | Warn -> Theme.yellow s | Bad -> Theme.red s
    | Pending -> Theme.yellow s | Neutral -> s

let private statusTag =
    function
    | Ok -> "ok" | Warn -> "warn" | Bad -> "bad"
    | Pending -> "pending" | Neutral -> "neutral"

/// glyph + colored value, as Spectre markup.
let private styled status (value: string) : string =
    let g = glyphOf status
    let body = if g = "" then Markup.Escape value else g + " " + Markup.Escape value
    colorOf status body

// --- The pretty/plain lens (write to any IAnsiConsole) ---------------------

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
    console.Write(panel)

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

/// The disclosure marker for a node with children: open (`▾`) when this level
/// is being revealed, closed (`▸`) when it is collapsed, blank when childless.
let private marker (depth: int) (hasChildren: bool) : string =
    if not hasChildren then " "
    elif depth >= 1 then Theme.expanded
    else Theme.collapsed

/// Truncate a cell's text to the width that remains on its line, with a `…`
/// tail — the width dual of the §12 breadth cap (`laneCap`). `prefix` is the
/// visible characters already spent on the line (indent + label + glyph +
/// gutters); the rest is the budget for `text`. A non-positive budget (an
/// unknown / pinned-narrow width) or a string that already fits is a no-op — so
/// a wide terminal (tests pin 200) is untouched, and a redirected sink (pinned to
/// `plainWidth`) only trims what would have wrapped. Truncation is a pretty-lens
/// concern only; `toJson` always carries the full, untrimmed string.
let private fit (width: int) (prefix: int) (text: string) : string =
    let budget = width - prefix
    if budget <= 0 || text.Length <= budget then text
    elif budget = 1 then "…"
    else text.Substring(0, budget - 1) + "…"

let rec private writeBlock (console: IAnsiConsole) (depth: int) (indent: string) (v: View) : unit =
    match v with
    | Doc blocks -> for b in blocks do writeBlock console depth indent b
    | Blank -> console.WriteLine()
    | Panel (title, fields) -> writePanel console title fields
    | Hero (st, text) ->
        let g = glyphOf st
        let body = fit console.Profile.Width (indent.Length + g.Length + 2) text
        console.MarkupLine(sprintf "%s%s  %s" indent (colorOf st g) (Theme.bold (Markup.Escape body)))
    | Field (label, value, st) ->
        // `styled` prepends `glyph + space` (nothing for Neutral); the label
        // column + its 3-space gutter precede it. Fit the value to what remains.
        let g = glyphOf st
        let glyphPart = if g = "" then 0 else g.Length + 1
        let body = fit console.Profile.Width (indent.Length + label.Length + 3 + glyphPart) value
        console.MarkupLine(sprintf "%s%s   %s" indent (Theme.muted (Markup.Escape label)) (styled st body))
    | Meter (label, filled, total, suffix) ->
        console.MarkupLine(
            sprintf "%s%s   %s   %s"
                indent (Theme.muted (Markup.Escape label)) (Theme.meter filled total) (Markup.Escape suffix))
    | Dots (label, verdicts) ->
        console.MarkupLine(sprintf "%s%s   %s" indent (Theme.muted (Markup.Escape label)) (Theme.canaryDotsMarkup verdicts))
    | Timeline (label, cells, filled, total, present) ->
        // The strip + the R6 gate meter on one line — the present marker rides
        // the dots, the cutover ratio trails (e.g. `●●●●✕●●▸  ▇▇▇▇▇▇▇░░░  7/10`).
        console.MarkupLine(
            sprintf "%s%s   %s   %s   %s"
                indent (Theme.muted (Markup.Escape label))
                (Theme.timelineMarkup cells present)
                (Theme.meter filled total)
                (Theme.muted (sprintf "%d/%d" filled total)))
    | Trail (label, steps) ->
        console.MarkupLine(sprintf "%s%s" indent (Theme.muted (Markup.Escape label)))
        for (step, detail) in steps do
            match detail with
            | Some d -> console.MarkupLine(sprintf "%s%s %s %s %s" indent Theme.arrow (Markup.Escape step) Theme.dot (Markup.Escape d))
            | None   -> console.MarkupLine(sprintf "%s%s %s" indent Theme.arrow (Markup.Escape step))
    | Lane (glyph, label, st, items) ->
        // A lane is a pre-baked disclosure of one move: the summary line always
        // shows (the true count, humane); the items reveal at depth ≥ 1, capped
        // at `laneCap` with an `and N more` tail so a thousand-change lane never
        // becomes a wall (`THE_VOICE.md` §12 — cap the breadth, name the
        // remainder). The full list always rides `toJson` — the machine lens
        // keeps what the human capped.
        let n = List.length items
        let m = marker depth (n > 0)
        console.MarkupLine(
            sprintf "%s%s %s" indent m (colorOf st (Markup.Escape (sprintf "%s %s  %s" glyph label (Theme.humane n)))))
        if depth >= 1 then
            for item in items |> List.truncate laneCap do
                console.MarkupLine(sprintf "%s   %s %s" indent (Theme.muted Theme.dot) (Markup.Escape item))
            if n > laneCap then
                console.MarkupLine(
                    sprintf "%s   %s %s" indent (Theme.muted Theme.collapsed)
                        (Theme.muted (sprintf "and %s more" (Theme.humane (n - laneCap)))))
    | Disclosure (headline, st, detail) ->
        let m = marker depth (not (List.isEmpty detail))
        let g = glyphOf st
        let glyphPart = if g = "" then 0 else g.Length + 1
        let body = fit console.Profile.Width (indent.Length + m.Length + 1 + glyphPart) headline
        console.MarkupLine(sprintf "%s%s %s" indent m (styled st body))
        if depth >= 1 then
            for child in detail do writeBlock console (depth - 1) (indent + "  ") child
        elif not (List.isEmpty detail) then
            console.MarkupLine(
                sprintf "%s  %s %s" indent (Theme.muted Theme.collapsed)
                    (Theme.muted (sprintf "%d more" (List.length detail))))
    | Note text ->
        console.MarkupLine(sprintf "%s%s" indent (Theme.muted (Markup.Escape (fit console.Profile.Width indent.Length text))))
    | Action text ->
        let body = fit console.Profile.Width (indent.Length + Theme.arrow.Length + 1) text
        console.MarkupLine(sprintf "%s%s %s" indent Theme.arrow (Theme.accent (Markup.Escape body)))

/// Render to a chosen disclosure depth — the dig revealed `depth` levels down,
/// deeper nodes collapsed behind their `▸ N more` affordance. The interactive
/// `→`/`←` of Explore and the `--depth` flag both ride this.
let writeToDepth (console: IAnsiConsole) (depth: int) (v: View) : unit =
    writeBlock console depth "  " v

/// Render at the calm default depth — a colored console is the pretty lens, a
/// `NoColors` console is the plain lens. One renderer, two outputs.
let write (console: IAnsiConsole) (v: View) : unit = writeToDepth console defaultDepth v

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

/// The plain-sink width pin. Spectre auto-sizes to the terminal, but a
/// redirected sink (pipe / file) reports no width and collapses the board / gate
/// grids mid-cell — so a redirected console is pinned wide enough to keep them
/// intact. (Previously inlined as a bare `100` at every render site.)
[<Literal>]
let plainWidth = 100

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
     | None       -> ())
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

// --- The query lens (a JSONPath subset over toJson; #17) -------------------
// `toJson` is the structured lens; `--query` walks it. The grammar is the
// surface-shaped subset the operator surfaces actually need — object keys, an
// array index, the `[]` wildcard (map over an array), and a flat equality
// filter `[?k=v]` — not a full JSONPath. It grows at the next real query
// (CLAUDE.md §5 — verbs at the second consumer). The walk is pure over the
// `JsonNode` the document already produces, so the human (`write`) and machine
// (`toJson` / `--query`) lenses stay projections of one value.

type private QuerySeg =
    | Key of string
    | Index of int
    | Wildcard
    | Filter of key: string * value: string

/// Parse a `--query` path (`.blocks[].status`, `.fields[0].value`,
/// `.blocks[?status=warn]`) into its segments, or a legible error. Paths lead
/// with `.` or `[` (the jq convention); a malformed path is named, never a crash.
let private parseQuery (path: string) : Result<QuerySeg list, string> =
    let segs = ResizeArray<QuerySeg>()
    let n = path.Length
    let mutable i = 0
    let mutable err = None
    while i < n && Option.isNone err do
        match path.[i] with
        | '.' ->
            let start = i + 1
            let mutable j = start
            while j < n && path.[j] <> '.' && path.[j] <> '[' do j <- j + 1
            if j = start then err <- Some "empty key after '.'"
            else
                segs.Add(Key(path.Substring(start, j - start)))
                i <- j
        | '[' ->
            match path.IndexOf(']', i) with
            | -1 -> err <- Some "unclosed '[' in query path"
            | close ->
                let inner = path.Substring(i + 1, close - i - 1)
                if inner = "" then segs.Add Wildcard
                elif inner.StartsWith "?" then
                    let body = inner.Substring 1
                    match body.IndexOf '=' with
                    | -1 -> err <- Some "filter needs the form [?key=value]"
                    | eq -> segs.Add(Filter(body.Substring(0, eq), body.Substring(eq + 1)))
                else
                    match System.Int32.TryParse inner with
                    | true, idx -> segs.Add(Index idx)
                    | _          -> err <- Some(sprintf "bad array index '%s'" inner)
                i <- close + 1
        | c -> err <- Some(sprintf "unexpected '%c' (a query path leads with '.' or '[')" c)
    match err with
    | Some e -> Result.Error e
    | None   -> Result.Ok(List.ofSeq segs)

let private matchesFilter (k: string) (value: string) (node: JsonNode) : bool =
    match node with
    | :? JsonObject as o ->
        match o.TryGetPropertyValue k with
        | true, fv ->
            match Option.ofObj fv with
            // Compare the stringified value, with one layer of JSON quoting
            // stripped so `[?status=warn]` matches the string "warn" and
            // `[?filled=7]` matches the number 7.
            | Some nn -> nn.ToJsonString().Trim('"') = value
            | None    -> false
        | _ -> false
    | _ -> false

let private applySeg (seg: QuerySeg) (node: JsonNode) : JsonNode list =
    match seg with
    | Key k ->
        match node with
        | :? JsonObject as o ->
            match o.TryGetPropertyValue k with
            | true, v -> Option.ofObj v |> Option.toList
            | _       -> []
        | _ -> []
    | Index idx ->
        match node with
        | :? JsonArray as a when idx >= 0 && idx < a.Count -> Option.ofObj a.[idx] |> Option.toList
        | _ -> []
    | Wildcard ->
        match node with
        | :? JsonArray as a -> a |> Seq.choose Option.ofObj |> List.ofSeq
        | _ -> []
    | Filter (k, value) ->
        match node with
        | :? JsonArray as a -> a |> Seq.choose Option.ofObj |> Seq.filter (matchesFilter k value) |> List.ofSeq
        | _ -> []

/// Validate a `--query` path's syntax without a document — the boundary
/// (`Program.main`) calls this to refuse a malformed path early with a clean
/// exit, rather than letting it silently match nothing.
let validateQuery (path: string) : Result<unit, string> =
    parseQuery path |> Result.map ignore

/// Walk a `--query` path over a document's `toJson` tree, returning the matched
/// nodes (jq-like: zero, one, or many — a `[]` wildcard or `[?…]` filter fans
/// out). A malformed path is an `Error`; a well-formed path that matches nothing
/// is `Ok []`.
let query (path: string) (root: JsonNode) : Result<JsonNode list, string> =
    match parseQuery path with
    | Result.Error e -> Result.Error e
    | Result.Ok segs -> Result.Ok(segs |> List.fold (fun nodes seg -> nodes |> List.collect (applySeg seg)) [ root ])
