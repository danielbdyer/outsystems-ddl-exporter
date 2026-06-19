module Projection.Cli.Navigator

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

// --- The Model + the pure reducer ------------------------------------------

/// The navigator state — the carried `Tree`, the open/cursor `Path` (its tip is
/// the selected, deepest-open node), the calm ambient `Depth` beneath the dug
/// spine, and the quit sentinel. No copy of run state lives here — only a cursor.
type Model =
    { Tree: View.View
      Path: int list
      Depth: int
      Done: bool }

/// Open on the first top-level node (so something is always selected), or the
/// empty path when the tree has no navigable children (a bare leaf).
let init (depth: int) (tree: View.View) : Model =
    { Tree = tree
      Path = (if List.isEmpty (children tree) then [] else [ 0 ])
      Depth = depth
      Done = false }

/// The render policy for the current frame: the dug spine is `OpenPath`, laid over
/// the calm ambient `Depth`. `Width` is the shell's to set (it owns the live
/// console). `OpenPath = Some []` (the empty path) is the all-calm root — exactly
/// the `--open`-less render — so a fresh tree with no dig reads like today's board.
let project (model: Model) : View.RenderOptions =
    { View.defaultOptions with Depth = model.Depth; OpenPath = Some model.Path }

/// The node labels along `Path` — the breadcrumb to the cursor.
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
    walk model.Tree model.Path []

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
    match key with
    | ConsoleKey.UpArrow | ConsoleKey.K ->
        match List.tryLast model.Path with
        | Some tip when tip > 0 -> { model with Path = withTip model.Path (tip - 1) }
        | _ -> model
    | ConsoleKey.DownArrow | ConsoleKey.J ->
        match List.tryLast model.Path with
        | Some tip ->
            let siblings = childCount model.Tree (parentOf model.Path)
            if tip + 1 < siblings then { model with Path = withTip model.Path (tip + 1) } else model
        | None ->
            if childCount model.Tree [] > 0 then { model with Path = [ 0 ] } else model
    | ConsoleKey.RightArrow | ConsoleKey.Enter | ConsoleKey.L ->
        if childCount model.Tree model.Path > 0 then { model with Path = model.Path @ [ 0 ] } else model
    | ConsoleKey.LeftArrow | ConsoleKey.Backspace | ConsoleKey.H ->
        match model.Path with
        | [] -> model
        | _ -> { model with Path = parentOf model.Path }
    | ConsoleKey.Q | ConsoleKey.Escape -> { model with Done = true }
    | _ -> model

// --- The impure shell (the ReadKey loop; thin, the I/O boundary) -----------

let private legend = "↑↓ move   →/Enter dig   ← back   q quit"

/// The footer chrome — the breadcrumb to the cursor, then the key legend, muted.
/// Built as plain markup (not a `View`) so it sits BELOW the rendered tree without
/// joining the navigable structure.
let private footer (console: IAnsiConsole) (model: Model) : unit =
    let crumb = breadcrumb model |> List.map Markup.Escape |> String.concat (" " + Theme.arrow + " ")
    console.WriteLine()
    let line = if crumb = "" then legend else crumb + "    " + Markup.Escape legend
    try console.MarkupLine(Theme.muted line)
    with :? System.InvalidOperationException -> console.WriteLine(legend)

/// Drive the inspector interactively over `tree` at ambient `depth`. CLEAR-and-
/// redraw on `Console.Out` each frame — the pure `step` / `project` do the thinking;
/// this shell only paints and reads a key. Returns 0. The caller owns the
/// interactive/headless choice — `run` assumes a real terminal (it `Console.Clear()`s).
///
/// Terminal restore is honest: cursor visibility is hidden for the loop and restored
/// in `finally`, AND `TreatControlCAsInput` is set so a Ctrl-C is delivered as a
/// KEYPRESS rather than killing the process — the loop catches it and quits CLEANLY
/// THROUGH `finally` (without that, a default Ctrl-C during `ReadKey` would terminate
/// the process with the caret still hidden). Both terminal modes are saved and
/// restored, each guarded (a non-interactive sink throws on get/set — we ignore it).
let run (depth: int) (tree: View.View) : int =
    let console = View.consoleTo Console.Out
    let mutable model = init depth tree
    let priorCursor = try Console.CursorVisible with _ -> true
    let priorCtrlC = try Console.TreatControlCAsInput with _ -> false
    try
        (try Console.CursorVisible <- false with _ -> ())
        (try Console.TreatControlCAsInput <- true with _ -> ())
        let mutable go = true
        while go do
            (try Console.Clear() with _ -> console.WriteLine())
            View.writeWith { project model with Width = console.Profile.Width } console tree
            footer console model
            let key = Console.ReadKey(true)
            if key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key = ConsoleKey.C then
                go <- false   // Ctrl-C quits cleanly — `finally` restores the terminal
            else
                model <- step key.Key model
                if model.Done then go <- false
        0
    finally
        (try Console.TreatControlCAsInput <- priorCtrlC with _ -> ())
        (try Console.CursorVisible <- priorCursor with _ -> ())
        console.WriteLine()
