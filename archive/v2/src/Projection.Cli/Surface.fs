module Projection.Cli.Surface

/// The statement-first shape every operator surface shares (`THE_VOICE.md` §1
/// rule 3 — "legible statement, formal substantiation beneath"; `THE_INSTRUMENT`
/// §3 — the `Surface` abstraction). One `Statement` (the plain lead finding the
/// newcomer reads and the master glances — *safe? worked? what's next?*) over a
/// `Substantiation` (the formal proof one level beneath, disclosed on demand —
/// `Show detail`), ending on the next `Action` (rule 2 / "end on the move"). A
/// new verb becomes a new `Surface`, never a new renderer. (Renamed from
/// `Essence`/`Dig` per `DECISIONS 2026-06-06` — the voice vocabulary; "dig" is
/// retired in operator-facing language.)
type Surface = {
    /// The lead verdict — a `Hero`, always shown. Status drives the glyph/color.
    Statement      : View.View
    /// The proof one level beneath — the substantiation, progressively disclosed
    /// beneath the statement (a list of `Disclosure` / `Lane` nodes the operator
    /// opens on demand via `Show detail`).
    Substantiation : View.View list
    /// The next move; `None` when the surface ends on its verdict.
    Action         : View.View option
}

/// Project a `Surface` onto the `View` substrate (`Surface.render`) — statement
/// first, a blank, then the substantiation, then (if present) a blank and the
/// next action. The one assembly every surface reuses, so the
/// statement/substantiation rhythm is identical across the changeset, the gate,
/// and every later specialization.
let render (s: Surface) : View.View =
    View.Doc (
        [ s.Statement; View.Blank ]
        @ s.Substantiation
        @ (match s.Action with
           | Some a -> [ View.Blank; a ]
           | None   -> []))
