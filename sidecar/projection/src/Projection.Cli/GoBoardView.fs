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

let private leaf (status: Status) (label: string) : TreeNode = { Label = label; Status = status; Children = [] }

let private claimNode (c: GoBoard.ScopeClaim) : TreeNode =
    let leaves =
        [ if not (List.isEmpty c.JoinEdges) then yield leaf Neutral (sprintf "← %s" (String.concat ", " c.JoinEdges))
          yield leaf Neutral (sprintf "intent: %s" c.Reason)
          match c.Status with
          | GoBoard.Status.Green _ when c.Guarantee <> "" -> yield leaf Neutral (sprintf "guarantee: %s" c.Guarantee)
          | GoBoard.Status.Red (_, remedy)                -> yield leaf Bad (sprintf "→ %s" remedy)
          | _ -> () ]
    { Label = sprintf "%s %s" c.Relationship c.Table; Status = statusV c.Status; Children = leaves }

/// The References / Dependents claim tree as one fully-expanded `View.Tree` — the
/// shared hierarchical lens both the go board and the live-run report render (the
/// guarantee tree is built ONCE in `SupportingScope.scopeGroups`, so the two
/// surfaces cannot disagree). A `Tree` (not the depth-gated `Disclosure`) because
/// the board renders it fully expanded; Spectre's connector lines read the
/// family → claim → guarantee nesting at a glance.
let scopeTree (headline: string) (status: Status) (groups: GoBoard.ScopeGroup list) (unaccounted: string list) : View =
    let families =
        groups
        |> List.filter (fun g -> not (List.isEmpty g.Claims))
        |> List.map (fun g ->
            { Label = sprintf "%s (%d)" (familyLabel g.Family) (List.length g.Claims)
              Status = groupStatus g.Claims
              Children = g.Claims |> List.map claimNode })
    Tree (headline, status, families @ (unaccounted |> List.map (leaf Bad)))

// -- the decision workbench table (2026-07-10, the manifest program) ----------

/// One escaping reference's answer grid: each row an answer with its exact
/// counts. The grid alone — the consequence prose rides separately (the
/// board's item Detail carries it once; the workbench appends it as notes).
let decisionGrid (t: GoBoard.DecisionTable) : View =
    let headers = [ ""; "answer"; "re-keys"; "drops"; "enters"; "opens" ]
    let row (r: GoBoard.DecisionRow) : (string * Status) list =
        [ (if r.Selected then "●" else ""), (if r.Selected then Ok else Neutral)
          r.Label, Neutral
          string r.Rekeyed, (if r.Rekeyed > 0 then Ok else Neutral)
          string r.Dropped, (if r.Dropped > 0 then Bad else Neutral)
          (if r.Entering > 0 then string r.Entering else ""), Neutral
          (match r.Opens with [] -> "" | names -> String.concat ", " names),
          (if List.isEmpty r.Opens then Neutral else Warn) ]
    Table (headers, t.Rows |> List.map row)

/// The grid plus its consequence sentences — the workbench's per-decision
/// block (statement in the table, substantiation in the notes).
let decisionTable (t: GoBoard.DecisionTable) : View list =
    [ yield decisionGrid t
      for r in t.Rows do
          if r.Consequence <> "" then yield Note r.Consequence ]

// -- item → block ------------------------------------------------------------

let private blockOfItem (i: GoBoard.Item) : View =
    let vstatus0, headline0, remedy = describe i.Status
    // An unverified finding (never a Red line) reads as a Warn block whose
    // headline names it unverified, so the rich lens carries the same honest
    // edge the plain lens states in words.
    let vstatus, headline =
        if i.Unverified && (match i.Status with GoBoard.Status.Red _ -> false | _ -> true)
        then Warn, sprintf "%s — unverified against the live environments" headline0
        else vstatus0, headline0
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
    | GoBoard.ItemBody.Decisions tables ->
        // The grids carry the counts; the item's Detail carries ALL the prose
        // (the escape lines, the probe evidence, the consequence sentences) —
        // one prose source, so the pretty and machine lenses read the same
        // words and nothing the plain twin holds vanishes from the terminal.
        let blocks =
            [ for t in tables do
                if t.Question <> "" then yield Field ("decision", t.Question, Warn)
                if not (List.isEmpty t.Rows) then yield decisionGrid t ]
        Disclosure (sprintf "%s — %s" i.Axis headline, vstatus,
                    blocks
                    @ (i.Detail |> List.map Note)
                    @ (match remedy with Some r -> [ Action r ] | None -> []))
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
            if GoBoard.hasUnverified board then
                Panel ("verdict",
                    [ PanelRow.Labeled ("verdict", sprintf "GREEN — every gate passes; %d finding(s) remain unverified against the live environments (the lines marked above). Read them before authorizing the run." (GoBoard.unverifiedCount board), Warn)
                      PanelRow.Next (sprintf "PROJECTION_ALLOW_EXECUTE=1 projection %s --go" board.Flow) ])
            else
                Panel ("verdict",
                    [ PanelRow.Labeled ("verdict", "GREEN — every gate passes.", Ok)
                      PanelRow.Next (sprintf "PROJECTION_ALLOW_EXECUTE=1 projection %s --go" board.Flow) ])
        else
            Panel ("verdict",
                [ PanelRow.Labeled ("verdict", sprintf "RED — %d open decision(s) / blocking fault(s)." (GoBoard.redCount board), Bad)
                  PanelRow.Next (sprintf "resolve each [STOP] line above, then re-run projection check go %s" board.Flow) ])
    // A titleless Rule underlines the masthead; a `verdict`-titled Rule (tinted by
    // the outcome) opens the closing panel — the section breaks the raw board never
    // had (2026-07-08, the widget-elevation program).
    let verdictStatus =
        if GoBoard.isGreen board then (if GoBoard.hasUnverified board then Warn else Ok) else Bad
    Doc ([ hero; Rule (None, Neutral) ] @ (board.Items |> List.map blockOfItem) @ [ Rule (Some "verdict", verdictStatus); verdict ])

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
