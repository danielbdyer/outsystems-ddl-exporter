module Projection.Tests.NavigatorTests

open System
open Xunit
open Projection.Cli

// The interactive inspector's PURE core (#11/#18 — dig-as-motion). The shell is a
// thin ReadKey loop; everything that can be wrong lives in `step` / `project` / the
// tree helpers, and is pinned here. The governing laws: `step` is TOTAL over
// `ConsoleKey` and CLAMPING (the cursor can NEVER leave the tree), and `project`
// feeds the cursor path straight into `OpenPath` (the dig IS the cursor) — so the
// Navigator is a cursor over data the `View` already carries, never a second copy.

// A small tree with mixed leaves and nesting, so the helpers and the reducer are
// exercised against real shape: 3 top-level nodes; [1] nests two deep.
let private tree =
    View.Doc [
        View.Hero (View.Ok, "head")                              // [0]      leaf
        View.Disclosure ("alpha", View.Neutral,                  // [1]      2 children
            [ View.Field ("a1", "A1", View.Ok)                   // [1;0]    leaf
              View.Disclosure ("a2", View.Neutral,               // [1;1]    1 child
                  [ View.Field ("deep", "DEEP", View.Bad) ]) ])  // [1;1;0]  leaf
        View.Field ("tail", "T", View.Neutral) ]                 // [2]      leaf

let private model path =
    { Navigator.Tree = tree; Navigator.Path = path; Navigator.Depth = 0; Navigator.Done = false
      Navigator.Filter = None; Navigator.Editing = false }

// --- The navigable-shape helpers -------------------------------------------

[<Fact>]
let ``Navigator: children/childCount read the Doc/Disclosure nesting, leaves are childless`` () =
    Assert.Equal(3, Navigator.childCount tree [])        // 3 top-level blocks
    Assert.Equal(2, Navigator.childCount tree [ 1 ])     // alpha's detail
    Assert.Equal(1, Navigator.childCount tree [ 1; 1 ])  // a2's detail
    Assert.Equal(0, Navigator.childCount tree [ 0 ])     // Hero — a leaf
    Assert.Equal(0, Navigator.childCount tree [ 2 ])     // tail Field — a leaf

[<Fact>]
let ``Navigator: nodeAt walks a valid path and refuses an out-of-range one`` () =
    Assert.True(Navigator.nodeAt tree [ 1; 1; 0 ] |> Option.isSome)   // the deep leaf
    Assert.True(Navigator.nodeAt tree [ 5 ] |> Option.isNone)         // no 6th top node
    Assert.True(Navigator.nodeAt tree [ 1; 9 ] |> Option.isNone)      // alpha has no 10th child
    Assert.True(Navigator.nodeAt tree [] |> Option.isSome)            // the root itself

// --- The reducer: totality + the in-bounds invariant (the safety law) ------

let private allKeys : ConsoleKey [] =
    Enum.GetValues(typeof<ConsoleKey>) :?> ConsoleKey []

[<Fact>]
let ``Navigator: step is TOTAL over ConsoleKey and never lets the cursor leave the tree (#11)`` () =
    // Every key, from every representative position (root, leaves, branch tips, the
    // deepest node), keeps the cursor addressing a real node — and never throws.
    let starts = [ []; [ 0 ]; [ 1 ]; [ 1; 0 ]; [ 1; 1 ]; [ 1; 1; 0 ]; [ 2 ] ]
    for start in starts do
        for k in allKeys do
            let m = Navigator.step k (model start)   // must not throw for ANY key
            Assert.True(
                Navigator.nodeAt tree m.Path |> Option.isSome,
                sprintf "key %A from %A left the tree at %A" k start m.Path)

// --- The reducer: clamping at the bounds (every edge move is a no-op) -------

[<Fact>]
let ``Navigator: ↑ at the first sibling and ↓ at the last are no-ops (clamped)`` () =
    Assert.Equal<int list>([ 0 ], (Navigator.step ConsoleKey.UpArrow (model [ 0 ])).Path)    // already first
    Assert.Equal<int list>([ 2 ], (Navigator.step ConsoleKey.DownArrow (model [ 2 ])).Path)  // already last (3 sibs)

[<Fact>]
let ``Navigator: → on a leaf and ← at the root are no-ops (clamped)`` () =
    Assert.Equal<int list>([ 0 ], (Navigator.step ConsoleKey.RightArrow (model [ 0 ])).Path)  // Hero has no children
    Assert.Equal<int list>([], (Navigator.step ConsoleKey.LeftArrow (model [])).Path)         // already at root

// --- The reducer: the dig is a reversible motion ----------------------------

[<Fact>]
let ``Navigator: ↓ moves among siblings, → digs in, ← retreats — and round-trips (#11)`` () =
    let m = Navigator.init 0 tree
    Assert.Equal<int list>([ 0 ], m.Path)                                   // opens on the first node
    let m = Navigator.step ConsoleKey.DownArrow m                           // → alpha [1]
    Assert.Equal<int list>([ 1 ], m.Path)
    let m = Navigator.step ConsoleKey.RightArrow m                          // dig into alpha → [1;0]
    Assert.Equal<int list>([ 1; 0 ], m.Path)
    let m = m |> Navigator.step ConsoleKey.DownArrow |> Navigator.step ConsoleKey.RightArrow  // a2 → deep [1;1;0]
    Assert.Equal<int list>([ 1; 1; 0 ], m.Path)
    // three retreats unwind the dug spine all the way back to the root
    let m = m |> Navigator.step ConsoleKey.LeftArrow |> Navigator.step ConsoleKey.LeftArrow |> Navigator.step ConsoleKey.LeftArrow
    Assert.Equal<int list>([], m.Path)

[<Fact>]
let ``Navigator: q and Esc raise the quit sentinel; other keys never do`` () =
    Assert.True((Navigator.step ConsoleKey.Q (model [ 1 ])).Done)
    Assert.True((Navigator.step ConsoleKey.Escape (model [ 1 ])).Done)
    Assert.False((Navigator.step ConsoleKey.DownArrow (model [ 1 ])).Done)

// --- project: the dig IS the OpenPath (the one-substrate handoff) ------------

[<Fact>]
let ``Navigator: project feeds the cursor path straight into OpenPath (#18)`` () =
    let opts = Navigator.project (model [ 1; 1 ])
    Assert.Equal<int list option>(Some [ 1; 1 ], opts.OpenPath)   // the cursor path, verbatim
    Assert.Equal(0, opts.Depth)                                   // the calm ambient beneath the spine

[<Fact>]
let ``Navigator: the breadcrumb names the nodes along the cursor path`` () =
    // alpha → a2 → deep : the labels of [1], [1;1], [1;1;0]
    Assert.Equal<string list>([ "alpha"; "a2"; "deep" ], Navigator.breadcrumb (model [ 1; 1; 0 ]))
    Assert.Equal<string list>([], Navigator.breadcrumb (model []))   // root: no crumb

// --- L1 filter: a PROJECTION of the tree, never a second copy ---------------
// The governing law: the filtered tree is DERIVED from the carried tree + a filter
// STRING (`effectiveTree`), and the cursor navigates it with the SAME `step` — so
// the in-bounds safety invariant extends to the filtered tree for free.

let private filtered q path =
    { (model path) with Navigator.Filter = Some q }

[<Fact>]
let ``Navigator filter: filterView keeps a branch whose descendant matches and prunes the rest`` () =
    // "deep" matches only the [1;1;0] leaf — so the alpha spine survives, head/tail go.
    match Navigator.filterView "deep" tree with
    | Some (View.Doc [ View.Disclosure ("alpha", _, [ View.Disclosure ("a2", _, [ View.Field ("deep", _, _) ]) ]) ]) -> ()
    | other -> Assert.Fail(sprintf "expected the pruned alpha→a2→deep spine, got %A" other)

[<Fact>]
let ``Navigator filter: a label match keeps the whole subtree`` () =
    // "alpha" matches the disclosure headline ⇒ its full body is kept (not pruned).
    match Navigator.filterView "alpha" tree with
    | Some (View.Doc [ View.Disclosure ("alpha", _, [ View.Field ("a1", _, _); View.Disclosure ("a2", _, _) ]) ]) -> ()
    | other -> Assert.Fail(sprintf "expected alpha's full body kept, got %A" other)

[<Fact>]
let ``Navigator filter: effectiveTree is the full tree unfiltered, the pruned tree filtered, a note on no match`` () =
    Assert.Equal(3, Navigator.childCount (Navigator.effectiveTree (model [])) [])          // unfiltered: 3 blocks
    Assert.Equal(1, Navigator.childCount (Navigator.effectiveTree (filtered "deep" [])) [])  // filtered: 1 spine
    match Navigator.effectiveTree (filtered "zzz" []) with
    | View.Doc [ View.Note n ] -> Assert.Contains("no matches", n)                          // honest empty
    | other -> Assert.Fail(sprintf "expected a no-matches note, got %A" other)

[<Fact>]
let ``Navigator filter: typeFilter accumulates and keeps the cursor on a real node`` () =
    let m =
        Navigator.beginFilter (model [ 1; 1; 0 ])
        |> Navigator.typeFilter 'd' |> Navigator.typeFilter 'e' |> Navigator.typeFilter 'e' |> Navigator.typeFilter 'p'
    Assert.Equal<string option>(Some "deep", m.Filter)
    Assert.True(m.Editing)
    Assert.True(Navigator.nodeAt (Navigator.effectiveTree m) m.Path |> Option.isSome, "cursor stays on a real node in the filtered tree")

[<Fact>]
let ``Navigator filter: backspace on an empty filter exits filtering`` () =
    let m = Navigator.beginFilter (model [ 0 ]) |> Navigator.backspaceFilter
    Assert.Equal<string option>(None, m.Filter)
    Assert.False(m.Editing)

[<Fact>]
let ``Navigator filter: step navigates the FILTERED tree and never leaves it (#11 extends)`` () =
    for start in [ [ 0 ]; [ 0; 0 ] ] do
        for k in allKeys do
            let m = Navigator.step k (filtered "deep" start)
            Assert.True(
                Navigator.nodeAt (Navigator.effectiveTree m) m.Path |> Option.isSome,
                sprintf "key %A from filtered %A left the tree at %A" k start m.Path)

[<Fact>]
let ``Navigator filter: Esc clears an active filter first, then quits (a layered exit)`` () =
    let cleared = Navigator.step ConsoleKey.Escape (filtered "deep" [ 0; 0 ])
    Assert.Equal<string option>(None, cleared.Filter)        // the filter is cleared…
    Assert.False(cleared.Done)                               // …not quit
    Assert.True((Navigator.step ConsoleKey.Escape cleared).Done)   // a second Esc (no filter) quits
