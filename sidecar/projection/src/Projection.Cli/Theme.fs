module Projection.Cli.Theme

/// The design-system module (`REPORTING_HORIZON.md` — the polish tier). One
/// place for the visual language — glyphs, semantic color, meters, sparklines
/// — so every operator surface (verdict panel, readiness board, digest, diff)
/// reads as one product. Two disciplines ride here:
///   - **Color is meaning, never decoration** — a glyph always accompanies
///     color, so the signal survives a colorblind reader or `NO_COLOR`.
///   - **Universal vs. styled** — glyphs / meters / sparklines are plain
///     Unicode (render anywhere); the `green`/`red`/… helpers emit Spectre
///     markup for the pretty channel only.

// --- Glyphs (universal; the signal that survives color loss) ---------------
let ok      = "✓"
let warn    = "▲"
let bad      = "✕"
let pending = "○"
let arrow   = "→"
let dot     = "·"
let dotFilled = "●"
// Progressive-disclosure affordances (Apple-HIG disclosure triangles): a closed
// node still has depth beneath it; an open node is showing it. Glyph-paired, so
// the affordance survives `NO_COLOR`.
let collapsed = "▸"
let expanded  = "▾"
/// The cursor caret (#23 Navigator / #18 `--open`) — marks the focused node (the
/// `OpenPath = Some []` tip) in a left gutter, so the cursor is visible even on a
/// LEAF (a `Field`/`Hero` has no disclosure marker). Glyph-first (it survives
/// `NO_COLOR`); the pretty lens accents it.
let cursor    = "❯"

/// The breathing spinner frames (#20 follow-on) — a braille cycle the live board's
/// ACTIVE stage line shows in place of the static `▸`, advanced on the drain loop's
/// periodic wake so a long-running stage visibly breathes between events. A glyph
/// cycle (not color), so it reads on `NO_COLOR`; the pretty lens accents it.
let private spinnerFrames = [| "⠋"; "⠙"; "⠹"; "⠸"; "⠼"; "⠴"; "⠦"; "⠧"; "⠇"; "⠏" |]

/// The spinner frame for a render tick — `phase` (a monotonically-advancing counter)
/// maps onto the cycle. Total over any non-negative `phase`.
let spinner (phase: int) : string =
    spinnerFrames.[(abs phase) % spinnerFrames.Length]
/// The width-cap tail (#11): a value truncated to the console's usable columns
/// ends in this, the visible signal that the pretty lens dropped a tail the
/// machine lens (`toJson`) still carries. One column, universal — survives
/// `NO_COLOR` like every glyph here.
let ellipsis  = "…"

// --- Semantic Spectre markup (pretty channel) ------------------------------
let green  (s: string) : string = "[green]"  + s + "[/]"
let yellow (s: string) : string = "[yellow]" + s + "[/]"
let red    (s: string) : string = "[red]"    + s + "[/]"
let muted  (s: string) : string = "[grey]"   + s + "[/]"
let bold   (s: string) : string = "[bold]"   + s + "[/]"
let accent (s: string) : string = "[aqua]"   + s + "[/]"

// --- Meters + trends (universal Unicode) -----------------------------------

/// Humane numerals — `2,140`, not `2140` (`THE_VOICE.md` §12: "the number
/// scales; the sentence does not"). The thousands separator keeps a big count
/// legible at a glance, the same at any size.
let humane (n: int) : string =
    n.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture)

/// A ratio meter — filled vs. empty blocks. `meter 7 10` → `▇▇▇▇▇▇▇░░░`.
/// Used for the R6 cutover gauge.
let meter (filled: int) (total: int) : string =
    let f = max 0 (min total filled)
    String.replicate f "▇" + String.replicate (max 0 (total - f)) "░"

/// A sparkline of a value series, min..max mapped across `▁▂▃▄▅▆▇█`. Used for
/// trends (profile-time over runs, coverage climbing). Flat series renders as
/// the lowest bar.
let sparkline (values: int list) : string =
    match values with
    | [] -> ""
    | _  ->
        let bars = [| "▁"; "▂"; "▃"; "▄"; "▅"; "▆"; "▇"; "█" |]
        let lo = List.min values
        let hi = List.max values
        let span = max 1 (hi - lo)
        values
        |> List.map (fun v -> bars.[(v - lo) * (bars.Length - 1) / span])
        |> String.concat ""

/// Canary history as plain dots — `●` green, `✕` red. Newest last.
let canaryDots (verdicts: string list) : string =
    verdicts
    |> List.map (fun v -> if v = "green" then dotFilled else bad)
    |> String.concat ""

/// Canary history as Spectre-colored dots (pretty channel).
let canaryDotsMarkup (verdicts: string list) : string =
    verdicts
    |> List.map (fun v -> if v = "green" then green dotFilled else red bad)
    |> String.concat ""

/// The cutover timeline strip (universal) — canary history dots (`●` green /
/// `✕` red, newest last) with an optional PRESENT marker flagging the run
/// "you are here": `●●●●✕●●▸●`. The marker is the collapsed-affordance glyph
/// (paired, so it survives `NO_COLOR`); an out-of-range / absent `present`
/// renders the bare strip. The spine of all three display tempos
/// (`DYNAMIC_DISPLAY` §4) — where this run sits on the arc to the R6 gate.
let timeline (verdicts: string list) (present: int option) : string =
    verdicts
    |> List.mapi (fun idx v ->
        let g = if v = "green" then dotFilled else bad
        match present with
        | Some p when p = idx -> g + collapsed
        | _ -> g)
    |> String.concat ""

/// The timeline strip as Spectre-colored markup (pretty channel) — the present
/// marker rides the accent color, the dots their semantic green/red.
let timelineMarkup (verdicts: string list) (present: int option) : string =
    verdicts
    |> List.mapi (fun idx v ->
        let g = if v = "green" then green dotFilled else red bad
        match present with
        | Some p when p = idx -> g + accent collapsed
        | _ -> g)
    |> String.concat ""
