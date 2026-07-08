module Projection.Cli.GoBoardView

// THE GO BOARD, rendered through the `View` engine (2026-07-08, the
// rendering-elevation program). The pure `GoBoard.Board` (Pipeline) is the ONE
// substrate; this CLI builder is the RICH lens — a Spectre-backed, terminal-
// responsive projection, sibling to `TtyRenderer`'s builders. The forecast
// becomes a `View.Table` (Spectre auto-sizes it to the terminal width); the
// supporting-scope axis becomes a fully-expanded hierarchy (References /
// Dependents → per-claim guarantee + normalized join edges). JSON stays the
// separate `GoBoard.toJsonString` contract — this touches only the human lens.

open Projection.Cli.View
open Projection.Pipeline

/// The disclosure depth the board renders at. `check go` is a one-shot
/// readiness report, so the whole tree (axis → family → claim → guarantee)
/// reveals at once — never collapsed behind `▸ N more`. Generous so the
/// deepest node (a guarantee line under a claim) always shows.
let boardDepth = 6

/// `GoBoard.Status` → the `View.Status` presentation (glyph + color).
let private statusV : GoBoard.Status -> Status =
    function
    | GoBoard.Status.Green _    -> Ok
    | GoBoard.Status.Advisory _ -> Warn
    | GoBoard.Status.Red _      -> Bad

/// The headline + optional remedy an item's status carries.
let private describe (status: GoBoard.Status) : Status * string * string option =
    match status with
    | GoBoard.Status.Green note      -> Ok, note, None
    | GoBoard.Status.Advisory note   -> Warn, note, None
    | GoBoard.Status.Red (reason, r) -> Bad, reason, Some r

// -- the responsive forecast table -------------------------------------------

let private numOpt (v: int64 option) : string = match v with Some n -> string n | None -> "?"

/// The before→after forecast as a `View.Table` — Spectre measures its own
/// column widths against the terminal budget, so the wide physical-name columns
/// reflow instead of clipping. `+add` reads green, `-del` red (the cell status
/// rides the machine lens too); a TOTAL row closes it. The free-text note is
/// NOT a column — it rides as a per-row `Note` beneath, so it never fights the
/// table's width.
let forecastTable (lines: GoBoard.ForecastLine list) : View =
    let headers = [ "source (read)"; "target (written)"; "before"; "+add"; "match"; "-del"; "after" ]
    let row (l: GoBoard.ForecastLine) : (string * Status) list =
        [ l.Source, Neutral
          l.Table, Neutral
          numOpt l.Before, Neutral
          string l.Adds, (if l.Adds > 0L then Ok else Neutral)
          (match l.Matches with Some m -> string m | None -> "-"), Neutral
          string l.Deletes, (if l.Deletes > 0L then Bad else Neutral)
          numOpt (GoBoard.ForecastLine.after l), Neutral ]
    let total (f: GoBoard.ForecastLine -> int64 option) =
        lines |> List.fold (fun acc l -> match acc, f l with Some a, Some v -> Some (a + v) | _ -> None) (Some 0L)
    let totalRow : (string * Status) list =
        [ "", Neutral
          "TOTAL", Neutral
          numOpt (total (fun l -> l.Before)), Neutral
          string (lines |> List.sumBy (fun l -> l.Adds)), Neutral
          string (lines |> List.sumBy (fun l -> l.Matches |> Option.defaultValue 0L)), Neutral
          string (lines |> List.sumBy (fun l -> l.Deletes)), Neutral
          numOpt (total GoBoard.ForecastLine.after), Neutral ]
    Table (headers, (lines |> List.map row) @ [ totalRow ])

// -- the supporting-scope hierarchy ------------------------------------------

let private familyLabel (family: string) : string =
    match family with
    | "references" -> "References — the payload points at these"
    | "dependents" -> "Dependents — these point at the payload"
    | other        -> other

let private groupStatus (claims: GoBoard.ScopeClaim list) : Status =
    if claims |> List.exists (fun c -> match c.Status with GoBoard.Status.Red _ -> true | _ -> false)
    then Bad else Ok

let private claimNode (c: GoBoard.ScopeClaim) : View =
    let detail =
        [ if not (List.isEmpty c.JoinEdges) then yield Note (sprintf "← %s" (String.concat ", " c.JoinEdges))
          yield Note (sprintf "intent: %s" c.Reason)
          match c.Status with
          | GoBoard.Status.Green _ when c.Guarantee <> "" -> yield Note (sprintf "guarantee: %s" c.Guarantee)
          | GoBoard.Status.Red (_, remedy)                -> yield Action remedy
          | _ -> () ]
    Disclosure (sprintf "%s %s" c.Relationship c.Table, statusV c.Status, detail)

/// The References / Dependents claim tree as one `Disclosure` — the shared
/// hierarchical lens both the go board and the live-run report render (the
/// guarantee tree is built ONCE in `SupportingScope.scopeGroups`, so the two
/// surfaces cannot disagree).
let scopeTree (headline: string) (status: Status) (groups: GoBoard.ScopeGroup list) (unaccounted: string list) : View =
    let families =
        groups
        |> List.filter (fun g -> not (List.isEmpty g.Claims))
        |> List.map (fun g ->
            Disclosure (sprintf "%s (%d)" (familyLabel g.Family) (List.length g.Claims),
                        groupStatus g.Claims,
                        g.Claims |> List.map claimNode))
    Disclosure (headline, status, families @ (unaccounted |> List.map Note))

// -- item → block ------------------------------------------------------------

let private blockOfItem (i: GoBoard.Item) : View =
    let vstatus, headline, remedy = describe i.Status
    match i.Body with
    | GoBoard.ItemBody.Forecast (lines, previews) ->
        let noteLines =
            lines
            |> List.filter (fun l -> l.Note <> "")
            |> List.map (fun l -> Note (sprintf "%s — %s" l.Table l.Note))
        Disclosure (sprintf "%s — %s" i.Axis headline, vstatus,
                    (forecastTable lines :: noteLines) @ (previews |> List.map Note))
    | GoBoard.ItemBody.Scope (groups, unaccounted) ->
        scopeTree (sprintf "%s — %s" i.Axis headline) vstatus groups unaccounted
    | GoBoard.ItemBody.Plain ->
        // The remedy is load-bearing (a Red line names the exact next move), so
        // it must survive even when the item carries no detail — the plain lens
        // always prints it. A bare `Field` is reserved for the case with neither
        // a remedy nor detail (a clean pass / advisory with nothing beneath).
        let children =
            (match remedy with Some r -> [ Action r ] | None -> [])
            @ (i.Detail |> List.map Note)
        match children with
        | [] -> Field (i.Axis, headline, vstatus)
        | _  -> Disclosure (sprintf "%s — %s" i.Axis headline, vstatus, children)

/// The whole board as one `View` — a hero, the axis blocks, and the verdict
/// panel with the next move named (green: the `--go` command; red: the re-run
/// imperative).
let ofBoard (board: GoBoard.Board) : View =
    let hero = Hero (Neutral, sprintf "THE GO BOARD — flow '%s'   %s → %s" board.Flow board.From board.To)
    let verdict =
        if GoBoard.isGreen board then
            Panel ("verdict",
                [ PanelRow.Labeled ("verdict", "GREEN — every gate passes.", Ok)
                  PanelRow.Next (sprintf "PROJECTION_ALLOW_EXECUTE=1 projection %s --go" board.Flow) ])
        else
            Panel ("verdict",
                [ PanelRow.Labeled ("verdict", sprintf "RED — %d open decision(s) / blocking fault(s)." (GoBoard.redCount board), Bad)
                  PanelRow.Next (sprintf "resolve each [STOP] line above, then re-run projection check go %s" board.Flow) ])
    Doc ([ hero; Blank ] @ (board.Items |> List.map blockOfItem) @ [ Blank; verdict ])

/// Whether a writer is a non-terminal sink (an in-memory / file writer, or a
/// redirected standard stream) — the same predicate `View.consoleTo` derives
/// internally, replicated here because the board needs it to choose its width
/// policy.
let private redirected (writer: System.IO.TextWriter) : bool =
    if   System.Object.ReferenceEquals(writer, System.Console.Error) then System.Console.IsErrorRedirected
    elif System.Object.ReferenceEquals(writer, System.Console.Out)   then System.Console.IsOutputRedirected
    else true

/// The width a REDIRECTED board renders at — generous so no proof line wraps
/// (the forecast `Table` sizes to its own content, well under this; it is only a
/// ceiling that keeps long evidence/drop/preview lines on one line, matching the
/// predecessor `GoBoard.render` which never wrapped). A pipe or file has no
/// terminal to reflow to, so a wide, un-wrapped capture is the faithful form.
let private redirectedReportWidth = 100000

/// Render a report `View` to a writer through the rich lens (a redirected sink
/// gets the plain NoColors lens; a TTY gets color + its real width).
///
/// Two width channels, resolved independently:
///   - The `View` text cap (`RenderOptions.Width`) is UNBOUND: the board is a
///     REPORT, and its proof lines must print in full — a clipped guarantee is a
///     clipped guarantee.
///   - The console `Profile.Width` governs Spectre's own wrapping + the forecast
///     `Table`'s auto-sizing. A TTY keeps its real width (the table reflows to
///     the terminal — the responsive lens the operator asked for); a redirected
///     sink is widened so long proof lines stay on one line, exactly as the raw
///     `printfn` predecessor emitted them.
let writeView (writer: System.IO.TextWriter) (v: View) : unit =
    let console = consoleTo writer
    if redirected writer then console.Profile.Width <- redirectedReportWidth
    writeWith { defaultOptions with Depth = boardDepth; Width = System.Int32.MaxValue } console v

let write (writer: System.IO.TextWriter) (board: GoBoard.Board) : unit =
    writeView writer (ofBoard board)
