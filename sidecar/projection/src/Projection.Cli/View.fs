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

type View =
    /// A sequence of blocks, rendered as lines (the board shape).
    | Doc of View list
    /// A bordered panel with a header, containing field/meter/action rows
    /// (the verdict-panel shape).
    | Panel of title: string * View list
    /// The lead verdict line — glyph + emphasized text.
    | Hero of Status * string
    /// A labeled value. `value` is plain; `Status` supplies glyph + color.
    | Field of label: string * value: string * Status
    /// A ratio gauge (the R6 meter) with a trailing suffix.
    | Meter of label: string * filled: int * total: int * suffix: string
    /// Canary-history dots (green ● / red ✕).
    | Dots of label: string * verdicts: string list
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

let private writePanel (console: IAnsiConsole) (title: string) (fields: View list) : unit =
    let grid = Grid()
    grid.AddColumn() |> ignore
    grid.AddColumn() |> ignore
    for f in fields do
        match f with
        | Field (label, value, st) ->
            grid.AddRow(Theme.muted (Markup.Escape label), styled st value) |> ignore
        | Meter (label, filled, total, suffix) ->
            grid.AddRow(
                Theme.muted (Markup.Escape label),
                sprintf "%s   %s" (Theme.meter filled total) (Markup.Escape suffix)) |> ignore
        | Action text ->
            grid.AddRow(Theme.muted "next", Theme.accent (Markup.Escape text)) |> ignore
        | _ -> ()   // panels carry field/meter/action rows
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

let rec private writeBlock (console: IAnsiConsole) (depth: int) (indent: string) (v: View) : unit =
    match v with
    | Doc blocks -> for b in blocks do writeBlock console depth indent b
    | Blank -> console.WriteLine()
    | Panel (title, fields) -> writePanel console title fields
    | Hero (st, text) ->
        console.MarkupLine(sprintf "%s%s  %s" indent (colorOf st (glyphOf st)) (Theme.bold (Markup.Escape text)))
    | Field (label, value, st) ->
        console.MarkupLine(sprintf "%s%s   %s" indent (Theme.muted (Markup.Escape label)) (styled st value))
    | Meter (label, filled, total, suffix) ->
        console.MarkupLine(
            sprintf "%s%s   %s   %s"
                indent (Theme.muted (Markup.Escape label)) (Theme.meter filled total) (Markup.Escape suffix))
    | Dots (label, verdicts) ->
        console.MarkupLine(sprintf "%s%s   %s" indent (Theme.muted (Markup.Escape label)) (Theme.canaryDotsMarkup verdicts))
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
        console.MarkupLine(sprintf "%s%s %s" indent m (styled st headline))
        if depth >= 1 then
            for child in detail do writeBlock console (depth - 1) (indent + "  ") child
        elif not (List.isEmpty detail) then
            console.MarkupLine(
                sprintf "%s  %s %s" indent (Theme.muted Theme.collapsed)
                    (Theme.muted (sprintf "%d more" (List.length detail))))
    | Note text -> console.MarkupLine(sprintf "%s%s" indent (Theme.muted (Markup.Escape text)))
    | Action text -> console.MarkupLine(sprintf "%s%s %s" indent Theme.arrow (Theme.accent (Markup.Escape text)))

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
    | Panel (title, fields) -> obj [ "kind", s "panel"; "title", s title; "fields", arr fields ]
    | Hero (st, text) -> obj [ "kind", s "hero"; "status", s (statusTag st); "text", s text ]
    | Field (label, value, st) ->
        obj [ "kind", s "field"; "label", s label; "value", s value; "status", s (statusTag st) ]
    | Meter (label, filled, total, suffix) ->
        obj [ "kind", s "meter"; "label", s label; "filled", i filled; "total", i total; "suffix", s suffix ]
    | Dots (label, verdicts) ->
        let a = JsonArray()
        for x in verdicts do a.Add(s x)
        obj [ "kind", s "dots"; "label", s label; "verdicts", a ]
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
