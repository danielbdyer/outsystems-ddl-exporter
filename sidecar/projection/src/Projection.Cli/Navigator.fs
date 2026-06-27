module Projection.Cli.Navigator
// LINT-ALLOW-FILE-MUTATION: the interactive navigator is a key-driven TUI loop stepping a local model; sealed function-local imperative state at the terminal boundary

open System
open Spectre.Console

/// The interactive inspector — "dig-as-motion" (#11), ViewPath's (#18) first
/// INTERACTIVE consumer (its `--open` flag was the headless first). The dig stops
/// being a re-run (`--depth N` / `--open a.b`) and becomes a MOTION: `↑`/`↓` move
/// the cursor among siblings, `→`/`Enter` digs into the selected node, `←` retreats.
/// The cursor IS the open path — at any moment exactly one spine is open (the dug
/// thread), every other branch calm at the ambient depth; the deepest open node is
/// where you are. So the whole inspector is a thin loop around two PURE functions
/// over `View` + a cursor path, feeding that path straight into
/// `RenderOptions.OpenPath` (no remap — the cursor indices ARE the render indices).
///
/// The discipline (DYNAMIC_DISPLAY §7.6): the Navigator holds a CURSOR over data the
/// `View` already carries, never a second copy of run state. `step` / `project` are
/// pure and total (property-tested); the impure shell is a clear-and-redraw `ReadKey`
/// loop — NOT a Spectre `Live` region (a live region plus a blocking `ReadKey` is the
/// terminal-exclusivity hazard the board fights), just `Console.Out`.

// --- The pure navigable shape of a View ------------------------------------
// The nav tree is the Doc / Disclosure nesting: a Doc exposes its blocks, a
// Disclosure its detail; every other case is a leaf (you can cursor onto it, but
// it has nothing to dig). The indices ARE the render child-indices, so a cursor
// path feeds straight into `RenderOptions.OpenPath` with no translation.

/// The navigable children of a node — the only two cases that nest.
let children (v: View.View) : View.View list =
    match v with
    | View.Doc blocks -> blocks
    | View.Disclosure (_, _, detail) -> detail
    | _ -> []

/// The node a child-index path addresses, or `None` if the path leaves the tree.
let rec nodeAt (tree: View.View) (path: int list) : View.View option =
    match path with
    | [] -> Some tree
    | i :: rest ->
        let kids = children tree
        if i >= 0 && i < List.length kids then nodeAt (List.item i kids) rest else None

/// How many navigable children the node at `path` has (0 if the path is invalid
/// or the node is a leaf). Drives the `↑`/`↓` clamp and whether `→` can descend.
let childCount (tree: View.View) (path: int list) : int =
    match nodeAt tree path with
    | Some n -> List.length (children n)
    | None -> 0

/// A one-line label for a node — the breadcrumb text (#10 / L5 reuse this).
let labelOf (v: View.View) : string =
    match v with
    | View.Doc _ -> "·"
    | View.Panel (t, _) -> t
    | View.Hero (_, t) -> t
    | View.Field (l, _, _) -> l
    | View.Meter (l, _, _, _) -> l
    | View.Dots (l, _) -> l
    | View.Spark (l, _) -> l
    | View.Timeline (l, _, _, _, _) -> l
    | View.Table _ -> "table"
    | View.Trail (l, _) -> l
    | View.Lane (_, l, _, _) -> l
    | View.Disclosure (h, _, _) -> h
    | View.Note t -> t
    | View.Action t -> t
    | View.Blank -> ""

// --- The filter (L1): a PROJECTION of the tree, never a second copy ---------
// `/` filters the dig to the branches matching a substring — the canonical
// at-scale move (DYNAMIC_DISPLAY §7.6: a cursor over the data the `View` already
// carries, not a copy of state). `filterView` PRUNES the tree to the matching
// branches; the Model holds only the filter STRING, and the filtered tree is
// derived on demand (`effectiveTree`). The cursor then navigates the pruned tree
// exactly as it navigates the full one — the same `step`, no special case.

/// Case-insensitive substring test. An empty query matches everything (so a
/// just-opened filter shows the whole tree until the first character is typed).
let private hits (q: string) (s: string) : bool =
    q = "" || s.IndexOf(q, System.StringComparison.OrdinalIgnoreCase) >= 0

/// Prune a `View` to the branches matching `q`, or `None` when nothing under it
/// matches. A container keeps its matching children (a `Disclosure` / `Lane` /
/// `Trail` whose OWN label matches keeps its full body); a leaf keeps itself iff
/// its text matches. Total over the `View` DU — the renderer's dual. `Blank` never
/// matches (filtering drops the spacers). Pure; the structured lens is untouched
/// (a filter is a pretty/interactive concern, like depth and the cursor).
let rec filterView (q: string) (v: View.View) : View.View option =
    let keptOf (xs: View.View list) = xs |> List.choose (filterView q)
    match v with
    | View.Doc blocks ->
        match keptOf blocks with [] -> None | kept -> Some (View.Doc kept)
    | View.Disclosure (h, st, detail) ->
        if hits q h then Some v
        else match keptOf detail with [] -> None | kept -> Some (View.Disclosure (h, st, kept))
    | View.Lane (g, label, st, items) ->
        if hits q label then Some v
        else match items |> List.filter (hits q) with [] -> None | kept -> Some (View.Lane (g, label, st, kept))
    | View.Trail (label, steps) ->
        if hits q label then Some v
        else
            match steps |> List.filter (fun (s, d) -> hits q s || (match d with Some x -> hits q x | None -> false)) with
            | []   -> None
            | kept -> Some (View.Trail (label, kept))
    | View.Table (headers, rows) ->
        match rows |> List.filter (List.exists (fun (text, _) -> hits q text)) with
        | []   -> None
        | kept -> Some (View.Table (headers, kept))
    | View.Panel (title, rows) ->
        let rowHit =
            function
            | View.PanelRow.Labeled (l, value, _) -> hits q l || hits q value
            | View.PanelRow.Gauge (l, _, _, suffix) -> hits q l || hits q suffix
            | View.PanelRow.Next t -> hits q t
        if hits q title || List.exists rowHit rows then Some v else None
    | View.Hero (_, t)            -> if hits q t then Some v else None
    | View.Field (l, value, _)    -> if hits q l || hits q value then Some v else None
    | View.Note t                 -> if hits q t then Some v else None
    | View.Action t               -> if hits q t then Some v else None
    | View.Meter (l, _, _, suffix)-> if hits q l || hits q suffix then Some v else None
    | View.Dots (l, _)            -> if hits q l then Some v else None
    | View.Spark (l, _)           -> if hits q l then Some v else None
    | View.Timeline (l, _, _, _, _) -> if hits q l then Some v else None
    | View.Blank                  -> None

// --- The Model + the pure reducer ------------------------------------------

/// The navigator state — the carried `Tree`, the open/cursor `Path` (its tip is
/// the selected, deepest-open node), the calm ambient `Depth` beneath the dug
/// spine, the quit sentinel, and the L1 filter axis. `Filter` is a STRING (the
/// substring being matched), never a copy of the tree — the filtered tree is
/// derived (`effectiveTree`). `Editing` is true while the filter is being typed
/// (keystrokes append) vs. navigated (keystrokes move the cursor). No copy of run
/// state lives here — only a cursor and a filter string.
type Model =
    { Tree: View.View
      Path: int list
      Depth: int
      Done: bool
      Filter: string option
      Editing: bool }

/// The first-child path for a tree (or the empty path for a bare leaf) — where the
/// cursor opens, and where it RESETS when the filter changes the tree under it.
let private rootPathOf (tree: View.View) : int list =
    if List.isEmpty (children tree) then [] else [ 0 ]

/// Open on the first top-level node (so something is always selected), or the
/// empty path when the tree has no navigable children (a bare leaf).
let init (depth: int) (tree: View.View) : Model =
    { Tree = tree
      Path = rootPathOf tree
      Depth = depth
      Done = false
      Filter = None
      Editing = false }

/// The tree the cursor navigates and the shell renders: the full `Tree`, or — when
/// a non-empty filter is active — that tree PRUNED to the matching branches
/// (`filterView`), with an honest "no matches" note when the filter excludes
/// everything. An empty / absent filter is the whole tree (no pruning, so the
/// spacers survive). Derived, never stored — the one-substrate discipline for the
/// interactive layer (a filter is a cursor over the data, not a second copy).
let effectiveTree (model: Model) : View.View =
    match model.Filter with
    | None | Some "" -> model.Tree
    | Some q ->
        filterView q model.Tree
        |> Option.defaultValue (View.Doc [ View.Note (sprintf "no matches for \"%s\"" q) ])

/// The render policy for the current frame: the dug spine is `OpenPath`, laid over
/// the calm ambient `Depth`. `Width` is the shell's to set (it owns the live
/// console). `OpenPath = Some []` (the empty path) is the all-calm root — exactly
/// the `--open`-less render — so a fresh tree with no dig reads like today's board.
let project (model: Model) : View.RenderOptions =
    { View.defaultOptions with Depth = model.Depth; OpenPath = Some model.Path }

/// The node labels along `Path` — the breadcrumb to the cursor, over the tree the
/// cursor actually navigates (the filtered tree when a filter is active).
let breadcrumb (model: Model) : string list =
    let rec walk (node: View.View) (path: int list) (acc: string list) =
        match path with
        | [] -> List.rev acc
        | i :: rest ->
            let kids = children node
            if i >= 0 && i < List.length kids then
                let child = List.item i kids
                walk child rest (labelOf child :: acc)
            else List.rev acc
    walk (effectiveTree model) model.Path []

let private parentOf (path: int list) : int list =
    match List.rev path with
    | [] -> []
    | _ :: t -> List.rev t

let private withTip (path: int list) (tip: int) : int list = parentOf path @ [ tip ]

/// The pure reducer — TOTAL over `ConsoleKey` (an unknown key is inert, the
/// default-drop the compiler will not warn on) and CLAMPING at the tree bounds:
/// `↑` at the first sibling, `↓` at the last, `→` on a leaf, and `←` at the root
/// are all no-ops, so the cursor can never leave the tree (property-tested). The
/// vi keys (h/j/k/l) mirror the arrows. `→` descends to the FIRST child (extends
/// the dug spine one level); `←` pops to the parent (retracts it).
let step (key: ConsoleKey) (model: Model) : Model =
    let tree = effectiveTree model              // navigate the filtered tree, same logic
    match key with
    | ConsoleKey.UpArrow | ConsoleKey.K ->
        match List.tryLast model.Path with
        | Some tip when tip > 0 -> { model with Path = withTip model.Path (tip - 1) }
        | _ -> model
    | ConsoleKey.DownArrow | ConsoleKey.J ->
        match List.tryLast model.Path with
        | Some tip ->
            let siblings = childCount tree (parentOf model.Path)
            if tip + 1 < siblings then { model with Path = withTip model.Path (tip + 1) } else model
        | None ->
            if childCount tree [] > 0 then { model with Path = [ 0 ] } else model
    | ConsoleKey.RightArrow | ConsoleKey.Enter | ConsoleKey.L ->
        if childCount tree model.Path > 0 then { model with Path = model.Path @ [ 0 ] } else model
    | ConsoleKey.LeftArrow | ConsoleKey.Backspace | ConsoleKey.H ->
        match model.Path with
        | [] -> model
        | _ -> { model with Path = parentOf model.Path }
    | ConsoleKey.Q -> { model with Done = true }       // q always quits
    | ConsoleKey.Escape ->
        // Escape clears an active filter FIRST (a layered exit), and only quits when
        // there is no filter to clear — so a filtered dig doesn't quit on the first Esc.
        match model.Filter with
        | Some _ -> { model with Filter = None; Editing = false; Path = rootPathOf model.Tree }
        | None   -> { model with Done = true }
    | _ -> model

// --- The filter reducers (pure; the shell routes keystrokes here while typing) --
// Every filter change RESETS the cursor to the root of the resulting (pruned) tree,
// because the prior path may not exist under the new filter — the same safety the
// time-axis walk uses (a fresh tree gets a fresh cursor).

/// The model with the filter set to `q`, the cursor reset to the new tree's root.
let private withFilter (q: string) (model: Model) : Model =
    let m = { model with Filter = Some q }
    { m with Path = rootPathOf (effectiveTree m) }

/// Enter filter-entry: keep any current filter text (so `/` re-edits), start typing,
/// reset the cursor to the resulting root.
let beginFilter (model: Model) : Model =
    { (withFilter (defaultArg model.Filter "") model) with Editing = true }

/// Append a typed character to the live filter.
let typeFilter (c: char) (model: Model) : Model =
    withFilter ((defaultArg model.Filter "") + string c) model

/// Delete the last filter character; backspace on an EMPTY filter exits filtering
/// entirely (the natural "undo the slash").
let backspaceFilter (model: Model) : Model =
    match model.Filter with
    | Some s when s.Length > 0 -> withFilter (s.Substring(0, s.Length - 1)) model
    | _ -> { model with Filter = None; Editing = false; Path = rootPathOf model.Tree }

/// Commit the filter — stop typing, KEEP it; the cursor now navigates the pruned
/// tree (the path already sits at the filtered root from the last keystroke).
let commitFilter (model: Model) : Model = { model with Editing = false }

/// Cancel filtering entirely — clear the filter, restore the full tree, reset the cursor.
let cancelFilter (model: Model) : Model =
    { model with Filter = None; Editing = false; Path = rootPathOf model.Tree }

// --- The impure shell (the ReadKey loop; thin, the I/O boundary) -----------

let private navLegend = "↑↓ move   →/Enter dig   ← back   / filter   q quit"

/// Render a styled Spectre-markup line, falling back to plain text on a non-
/// interactive sink (Spectre's `MarkupLine` throws `InvalidOperationException`
/// when there is no ANSI-capable terminal). `styled` is the themed + escaped
/// markup; `plain` is the unmarked text the fallback writes. Extracted from four
/// copies of this idiom (recon #25) — the footer copy had two latent bugs the
/// extraction fixes at the call site: it dropped the breadcrumb on the fallback
/// path (writing only the legend) and left the no-crumb branch unescaped.
let private safeMarkupLine (console: IAnsiConsole) (styled: string) (plain: string) : unit =
    try console.MarkupLine styled
    with :? System.InvalidOperationException -> console.WriteLine plain

/// The position header — only in history mode (#10): where this run sits in the
/// ledger (newest-first) and the time-axis keys. A single run shows nothing here
/// (its identity is the `Hero` in the tree).
let private header (console: IAnsiConsole) (idx: int) (count: int) : unit =
    if count > 1 then
        let line = sprintf "run %d/%d   PgUp newer · PgDn older" (idx + 1) count
        safeMarkupLine console (Theme.muted (Markup.Escape line)) line
        console.WriteLine()

/// The footer chrome — the filter line (when filtering), the breadcrumb to the
/// cursor, then the key legend, muted. Built as plain markup (not a `View`) so it
/// sits BELOW the rendered tree without joining the navigable structure.
let private footer (console: IAnsiConsole) (model: Model) : unit =
    console.WriteLine()
    // The filter line: a live `/foo▌` prompt while typing, or a committed
    // `filter: foo` with the Esc-clears hint once locked. Absent when not filtering.
    (match model.Filter with
     | Some q when model.Editing ->
         let line = sprintf "/%s▌   (Enter keep · Esc clear · Backspace edit)" q
         safeMarkupLine console (Theme.accent (Markup.Escape line)) line
     | Some q ->
         let line = sprintf "filter: %s   (/ edit · Esc clear)" q
         safeMarkupLine console (Theme.accent (Markup.Escape line)) line
     | None -> ())
    // The breadcrumb to the cursor, then the legend. Theme.arrow ("→") carries no
    // markup brackets, so the same join serves both renditions: the styled line
    // escapes each crumb segment + the legend; the plain fallback keeps them raw.
    let crumbs = breadcrumb model
    let styledCrumb = crumbs |> List.map Markup.Escape |> String.concat (" " + Theme.arrow + " ")
    let plainCrumb  = crumbs |> String.concat (" " + Theme.arrow + " ")
    let styledLine = if styledCrumb = "" then Markup.Escape navLegend else styledCrumb + "    " + Markup.Escape navLegend
    let plainLine  = if plainCrumb  = "" then navLegend else plainCrumb + "    " + navLegend
    safeMarkupLine console (Theme.muted styledLine) plainLine

/// The shared CLEAR-and-redraw loop (the only I/O boundary). `count` frames addressed
/// by `loadAt` (0 = newest); the cursor digs ONE frame, `PgUp`/`PgDn` scrub the time
/// axis (#10) — pure shell I/O (the reducer never sees a run; each frame re-inits the
/// cursor at the new tree's root). The pure `step` / `project` do all the thinking.
///
/// Terminal restore is honest: cursor visibility is hidden for the loop and restored in
/// `finally`, AND `TreatControlCAsInput` is set so a Ctrl-C is delivered as a KEYPRESS
/// rather than killing the process — the loop catches it and quits CLEANLY THROUGH
/// `finally` (without that, a default Ctrl-C during `ReadKey` terminates the process with
/// the caret still hidden). Both terminal modes are saved and restored, each guarded (a
/// non-interactive sink throws on get/set — we ignore it). The caller owns the
/// interactive/headless choice; `driveLoop` assumes a real terminal (it `Console.Clear()`s).
let private driveLoop (count: int) (start: int) (loadAt: int -> View.View) : int =
    let console = View.consoleTo Console.Out
    let clamp i = max 0 (min i (count - 1))
    let mutable idx = clamp start
    let mutable model = init 0 (loadAt idx)
    let priorCursor = try Console.CursorVisible with _ -> true
    let priorCtrlC = try Console.TreatControlCAsInput with _ -> false
    try
        (try Console.CursorVisible <- false with _ -> ())
        (try Console.TreatControlCAsInput <- true with _ -> ())
        let mutable go = true
        while go do
            (try Console.Clear() with _ -> console.WriteLine())
            header console idx count
            View.writeWith { project model with Width = console.Profile.Width } console (effectiveTree model)
            footer console model
            let key = Console.ReadKey(true)
            if key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key = ConsoleKey.C then
                go <- false   // Ctrl-C quits cleanly — `finally` restores the terminal
            elif model.Editing then
                // Filter-typing mode: printable chars append, the rest are control.
                match key.Key with
                | ConsoleKey.Enter     -> model <- commitFilter model
                | ConsoleKey.Escape    -> model <- cancelFilter model
                | ConsoleKey.Backspace -> model <- backspaceFilter model
                | _ when not (Char.IsControl key.KeyChar) -> model <- typeFilter key.KeyChar model
                | _ -> ()    // ignore arrows / other control keys while typing
            elif key.KeyChar = '/' then
                model <- beginFilter model                                  // `/` opens the filter
            elif key.Key = ConsoleKey.PageDown && idx < count - 1 then
                idx <- idx + 1                       // down the newest-first list → an OLDER run
                model <- init 0 (loadAt idx)         // fresh cursor at the new run's root
            elif key.Key = ConsoleKey.PageUp && idx > 0 then
                idx <- idx - 1                       // up the list → a NEWER run
                model <- init 0 (loadAt idx)
            else
                model <- step key.Key model
                if model.Done then go <- false
        0
    finally
        (try Console.TreatControlCAsInput <- priorCtrlC with _ -> ())
        (try Console.CursorVisible <- priorCursor with _ -> ())
        console.WriteLine()

/// Drive the inspector over ONE `tree` (the single-run `inspect <id>`).
let run (tree: View.View) : int = driveLoop 1 0 (fun _ -> tree)

/// Drive the inspector over a run HISTORY (#10) — `count` runs addressed by `loadAt`
/// (0 = newest), starting at `start`. `PgUp`/`PgDn` walk the time axis; `loadAt` does
/// the per-frame I/O (a closure the caller owns, so this module stays free of `Run`).
let runHistory (count: int) (start: int) (loadAt: int -> View.View) : int =
    driveLoop count start loadAt

/// Present any answer `View` the live way (L2 — the read surfaces become control
/// surfaces): on a real terminal, OPEN the Navigator (dig-as-motion); otherwise render
/// the SAME document one-shot through `renderAnswer` (so a pipe / `--json` / `--query`
/// still gets the calm answer — the one-substrate fallback, unchanged). The single
/// predicate every navigable face shares: a redirected stdout, a machine format, or a
/// query all mean "no TUI, give me the document" — and `isInteractive` already requires
/// real stdin+stderr, so `ReadKey` can never hit a non-TTY. Verbs call THIS instead of
/// `renderAnswer` to gain interactivity uniformly (inspect / diff today).
let present (asJson: bool) (depth: int) (view: View.View) : int =
    let interactive =
        Intervene.isInteractive ()
        && not Console.IsOutputRedirected
        && not asJson
        && Option.isNone TtyRenderer.queryPath.Value
    if interactive then run view
    else
        TtyRenderer.renderAnswer asJson depth view
        0
