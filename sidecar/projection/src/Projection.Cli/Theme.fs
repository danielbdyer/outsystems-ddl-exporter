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
