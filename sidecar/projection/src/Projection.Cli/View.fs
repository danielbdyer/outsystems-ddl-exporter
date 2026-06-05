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

let rec private writeBlock (console: IAnsiConsole) (v: View) : unit =
    match v with
    | Doc blocks -> for b in blocks do writeBlock console b
    | Blank -> console.WriteLine()
    | Panel (title, fields) -> writePanel console title fields
    | Hero (st, text) ->
        console.MarkupLine(sprintf "  %s  %s" (colorOf st (glyphOf st)) (Theme.bold (Markup.Escape text)))
    | Field (label, value, st) ->
        console.MarkupLine(sprintf "  %s   %s" (Theme.muted (Markup.Escape label)) (styled st value))
    | Meter (label, filled, total, suffix) ->
        console.MarkupLine(
            sprintf "  %s   %s   %s"
                (Theme.muted (Markup.Escape label)) (Theme.meter filled total) (Markup.Escape suffix))
    | Dots (label, verdicts) ->
        console.MarkupLine(sprintf "  %s   %s" (Theme.muted (Markup.Escape label)) (Theme.canaryDotsMarkup verdicts))
    | Trail (label, steps) ->
        console.MarkupLine(sprintf "  %s" (Theme.muted (Markup.Escape label)))
        for (step, detail) in steps do
            match detail with
            | Some d -> console.MarkupLine(sprintf "  %s %s %s %s" Theme.arrow (Markup.Escape step) Theme.dot (Markup.Escape d))
            | None   -> console.MarkupLine(sprintf "  %s %s" Theme.arrow (Markup.Escape step))
    | Note text -> console.MarkupLine(sprintf "  %s" (Theme.muted (Markup.Escape text)))
    | Action text -> console.MarkupLine(sprintf "  %s %s" Theme.arrow (Theme.accent (Markup.Escape text)))

/// Render to the given console — a colored console is the pretty lens, a
/// `NoColors` console is the plain lens. One renderer, two outputs.
let write (console: IAnsiConsole) (v: View) : unit = writeBlock console v

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
    | Note text -> obj [ "kind", s "note"; "text", s text ]
    | Action text -> obj [ "kind", s "action"; "text", s text ]
    | Blank -> obj [ "kind", s "blank" ]
