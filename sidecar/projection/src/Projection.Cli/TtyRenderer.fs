module Projection.Cli.TtyRenderer

open System
open Spectre.Console
open Projection.Pipeline

/// Tier-3 reporting (`REPORTING_HORIZON.md`; `docs/logging-format.md` §15.3) —
/// the Spectre.Console "channel 2" surface. Now a **consumer of the `View`
/// primitive**: each `build…View` produces a typed document; `View.write`
/// renders it (pretty on a colored console, plain on a `NoColors` one) and
/// `View.toJson` is the same document as structure. The renderers are derived
/// consumers of `LogSink` + the ledger, never a second emit surface.

/// True iff a pretty render is warranted: the operator asked for it AND
/// stderr is a real terminal (not a pipe / file). Per §15.1 — never draw
/// ANSI into a redirected stream.
let shouldRender (prettyRequested: bool) : bool =
    prettyRequested && not Console.IsErrorRedirected

// --- the verdict panel as a View -------------------------------------------

/// Build the terminal verdict panel `View` from the run's `LogSink` state +
/// the ledger. Pure data — `View.write` / `View.toJson` are the lenses.
let buildSummaryView (command: string) (code: int) : View.View =
    let registered, applied, declined = LogSink.transformCounts ()
    let outcome =
        if code = 0 then View.Field("outcome", "SUCCEEDED", View.Ok)
        else View.Field("outcome", "FAILED", View.Bad)
    let canary =
        match LogSink.canaryVerdict () with
        | Some "green" -> View.Field("canary", "green — diff empty", View.Ok)
        | Some "red"   -> View.Field("canary", "RED — divergence", View.Bad)
        | Some other   -> View.Field("canary", other, View.Neutral)
        | None         -> View.Field("canary", "no canary leg", View.Neutral)
    let transforms =
        View.Field(
            "transforms",
            sprintf "%d registered %s %d applied %s %d declined" registered Theme.dot applied Theme.dot declined,
            View.Neutral)
    let edits = LogSink.suggestedConfigEdits ()
    let actionable =
        if edits = 0 then View.Field("actionable", "none", View.Ok)
        else
            // Impact-ranked — name the single biggest lever first.
            match LogSink.topSuggestion () with
            | Some (path, count) ->
                View.Field("actionable", sprintf "%d edit(s) %s top: %s (%d)" edits Theme.dot path count, View.Warn)
            | None -> View.Field("actionable", sprintf "%d edit(s) suggested" edits, View.Warn)
    // Principle #5 — end with the next action.
    let nextAction = if edits > 0 then [ View.Action "projection suggest-config --apply" ] else []
    let cutover =
        match RunLedger.configuredDir () with
        | Some dir ->
            let r = RunLedger.read dir |> RunLedger.readiness
            let gate = if r.Eligible then "ELIGIBLE" else "not yet"
            [ View.Meter(
                "cutover", r.ConsecutiveGreen, r.Threshold,
                sprintf "%d / %d green %s %s" r.ConsecutiveGreen r.Threshold Theme.arrow gate) ]
        | None -> []
    View.Panel(command, [ outcome; canary; transforms; actionable ] @ nextAction @ cutover)

let renderSummaryTo (console: IAnsiConsole) (command: string) (code: int) : unit =
    View.write console (buildSummaryView command code)

/// Render the verdict panel to stderr (channel 2 — the panel is a rendering
/// of events; stdout stays the narration surface).
let renderSummary (command: string) (code: int) : unit =
    let console =
        AnsiConsole.Create(AnsiConsoleSettings(Out = AnsiConsoleOutput(Console.Error)))
    renderSummaryTo console command code

// --- the readiness board as a View -----------------------------------------

/// Build the cutover-readiness board `View` — hero answer first, then the R6
/// meter, the canary-history dots, the run totals, the ledger note.
let buildReadinessView (r: RunLedger.Readiness) (recent: string list) (ledgerPath: string) : View.View =
    let toGo = max 0 (r.Threshold - r.ConsecutiveGreen)
    let hero =
        if r.Eligible then
            View.Hero(View.Ok, sprintf "ELIGIBLE %s %d consecutive green canaries" Theme.dot r.ConsecutiveGreen)
        else
            View.Hero(View.Pending, sprintf "NOT YET %s %d green run(s) to cutover-ready" Theme.dot toGo)
    let history = if List.isEmpty recent then [] else [ View.Dots("history", recent) ]
    let lastCanary = match r.LastCanary with Some c -> c | None -> "—"
    View.Doc(
        [ View.Blank
          hero
          View.Blank
          View.Meter("cutover", r.ConsecutiveGreen, r.Threshold, sprintf "%d / %d green" r.ConsecutiveGreen r.Threshold) ]
        @ history
        @ [ View.Field(
              "runs",
              sprintf "%d total %s %d with a canary %s last %s" r.TotalRuns Theme.dot r.CanaryRuns Theme.dot lastCanary,
              View.Neutral)
            View.Blank
            View.Note(sprintf "ledger    %s" ledgerPath) ])

let renderReadinessBoardTo
    (console: IAnsiConsole)
    (r: RunLedger.Readiness)
    (recent: string list)
    (ledgerPath: string)
    : unit =
    View.write console (buildReadinessView r recent ledgerPath)

let renderReadinessBoard (r: RunLedger.Readiness) (recent: string list) (ledgerPath: string) : unit =
    // The board renders on every `readiness` (not just on a TTY). Pin a width
    // when piped (Spectre's auto-width collapses lines on a non-TTY); it still
    // strips color for the non-terminal sink.
    let console =
        AnsiConsole.Create(AnsiConsoleSettings(Out = AnsiConsoleOutput(Console.Out)))
    if Console.IsOutputRedirected then console.Profile.Width <- 100
    renderReadinessBoardTo console r recent ledgerPath

// --- the answer surface — render any View to stdout (INSTRUMENT slice 1) ----

/// Render any `View` to stdout — the "answer" surface (stdout carries the answer;
/// structured events stay on stderr). Pretty (color) on a TTY; plain when piped
/// (width pinned so lines don't collapse); `--format json` emits the same
/// document as structure (`View.toJson`), so the human and machine lenses are
/// the one value.
let renderAnswer (asJson: bool) (v: View.View) : unit =
    if asJson then
        Console.Out.WriteLine((View.toJson v).ToJsonString())
    else
        let console =
            AnsiConsole.Create(AnsiConsoleSettings(Out = AnsiConsoleOutput(Console.Out)))
        if Console.IsOutputRedirected then console.Profile.Width <- 100
        View.write console v
