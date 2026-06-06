module Projection.Cli.Surface

/// The essence-first, diggable shape every operator surface shares
/// (`INSTRUMENT_BACKLOG` §3 — the `Surface` abstraction). One `Essence` (the
/// plain lead Hero — *safe? worked? what's next?*) over a `Dig` (the move-typed,
/// proven depth, one keypress down), ending in the next `Action` (principle #5).
/// A new verb becomes a new `Surface`, never a new renderer.
type Surface = {
    /// The lead verdict — a `Hero`, always shown. Status drives the glyph/color.
    Essence : View.View
    /// The proven depth — the dig, progressively disclosed beneath the essence
    /// (a list of `Disclosure` / `Lane` nodes the operator opens on demand).
    Dig     : View.View list
    /// The next move; `None` when the surface ends on its verdict.
    Action  : View.View option
}

/// Project a `Surface` onto the `View` substrate (`Surface.render`) — essence
/// first, a blank, then the dig, then (if present) a blank and the next action.
/// The one assembly every surface reuses, so the essence/dig rhythm is identical
/// across the changeset, the gate, and every later specialization.
let render (s: Surface) : View.View =
    View.Doc (
        [ s.Essence; View.Blank ]
        @ s.Dig
        @ (match s.Action with
           | Some a -> [ View.Blank; a ]
           | None   -> []))
