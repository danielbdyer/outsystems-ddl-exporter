module Projection.Cli.TtyRenderer

open System
open Spectre.Console
open Projection.Core
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
    // The verdict line is voiced by `Voice`, keyed by the code the run earned:
    // the round-trip-verification proof (`canary.*`, §6) when a canary leg ran,
    // else the terminal outcome (`summary.runComplete`, §3). The copy is no longer
    // authored here — `TtyRenderer` looks it up by code (`THE_VOICE_INTEGRATION.md`
    // slice 1; the `code ⇔ copy` totality test holds it honest).
    let verdict =
        let codeForVerdict, payload : string * Voice.Payload =
            match LogSink.canaryVerdict () with
            | Some "green" -> "canary.diffEmpty", Map.empty
            | Some "red"   -> "canary.divergence", Map.empty
            | _ ->
                "summary.runComplete",
                Map.ofList [ "outcome", box (if code = 0 then "succeeded" else "failed") ]
        match Voice.verdict codeForVerdict payload with
        | Some (st, t) -> View.Field("verdict", t, st)
        | None ->
            View.Field(
                "verdict",
                (if code = 0 then "The run completed without error." else "Stopped before completion."),
                (if code = 0 then View.Ok else View.Bad))
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
    View.Panel(command, [ verdict; transforms; actionable ] @ nextAction @ cutover)

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
    // The timeline read in words — the dots' shape said plainly (§8 / Appendix
    // A.5): how the recent checks landed, and which run is the present one.
    let timeline =
        if List.isEmpty recent then []
        else
            let n = List.length recent
            let diverged = recent |> List.filter (fun v -> v <> "green") |> List.length
            let shape =
                if diverged = 0 then sprintf "the last %d check(s) all passed" n
                else sprintf "the last %d check(s) %s %d passed %s %d diverged" n Theme.dot (n - diverged) Theme.dot diverged
            let here = if r.TotalRuns > 0 then sprintf " %s run %d, the present one" Theme.dot r.TotalRuns else ""
            [ View.Note(shape + here) ]
    let lastCanary = match r.LastCanary with Some c -> c | None -> "—"
    View.Doc(
        [ View.Blank
          hero
          View.Blank
          View.Meter("cutover", r.ConsecutiveGreen, r.Threshold, sprintf "%d / %d green" r.ConsecutiveGreen r.Threshold) ]
        @ history
        @ timeline
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
/// (width pinned so lines don't collapse); the dig is revealed to `depth` levels.
/// `--format json` emits the same document as structure (`View.toJson`) — always
/// the full tree — so the human and machine lenses are the one value.
let renderAnswer (asJson: bool) (depth: int) (v: View.View) : unit =
    if asJson then
        Console.Out.WriteLine((View.toJson v).ToJsonString())
    else
        let console =
            AnsiConsole.Create(AnsiConsoleSettings(Out = AnsiConsoleOutput(Console.Out)))
        if Console.IsOutputRedirected then console.Profile.Width <- 100
        View.writeToDepth console depth v

/// Voice a refusal to STDERR (the §5 channel split — errors never on stdout).
/// The coded `ValidationError` becomes a register-correct `Surface` via
/// `Voice.errorSurface`, rendered through the same `View` engine that draws the
/// answer — so a refusal speaks in the operator register, not raw prose.
let renderVoicedError (error: Projection.Core.ValidationError) : unit =
    let view = Surface.render (Voice.errorSurface error)
    let console =
        AnsiConsole.Create(AnsiConsoleSettings(Out = AnsiConsoleOutput(Console.Error)))
    if Console.IsErrorRedirected then console.Profile.Width <- 100
    View.writeToDepth console View.defaultDepth view

// --- the Gate surface — a refusal as a stop-and-confirm (INSTRUMENT slice 3) -

/// The Gate as a `Surface` (INSTRUMENT slice 3) — the stop-and-confirm a refusal
/// renders. Statement-first: a Hero names the danger in plain words (Bad for a
/// destructive change the operator must declare; Warn for a blocking pre-flight
/// refusal); the substantiation is a `Disclosure` carrying the formal proof —
/// the gate label, the specific detail, and the distinct exit code — open by
/// default but collapsible for the operator who already knows the drop; the next
/// action names how to proceed. Binds `Preflight.GateRefusal` — the structured
/// refusal that otherwise collapses to a single error string.
let buildGateSurface (command: string) (refusal: Preflight.GateRefusal) : Surface.Surface =
    // The §5 gate copy is voiced centrally by `Voice.gateSurface`, keyed by the
    // closed `Preflight.GateLabel` DU (the gate⇔copy totality) — `TtyRenderer`
    // no longer authors the refusal prose.
    Voice.gateSurface command refusal

/// The Gate `View`.
let buildGateView (command: string) (refusal: Preflight.GateRefusal) : View.View =
    Surface.render (buildGateSurface command refusal)

/// Render the Gate to stderr (a refusal is an event surface; stdout stays the
/// answer/narration surface).
let renderGate (command: string) (refusal: Preflight.GateRefusal) : unit =
    let console =
        AnsiConsole.Create(AnsiConsoleSettings(Out = AnsiConsoleOutput(Console.Error)))
    if Console.IsErrorRedirected then console.Profile.Width <- 100
    View.write console (buildGateView command refusal)

// --- the error surface — refusals & errors as voice (slice 4) ---------------

/// Build the `View` for a `ValidationError list` — the §10/§14 frame voiced by
/// `Voice.errorsSurface` (statement-first; the located causes + codes beneath).
let buildErrorsView (errors: ValidationError list) : View.View =
    Surface.render (Voice.errorsSurface errors)

/// Render a `ValidationError list` to a chosen writer as the voiced §10/§14
/// surface — the operator reads a plain statement and the next move; the codes
/// ride in the substantiation, never on the statement line. The structured
/// NDJSON (`config.validationFailed` etc.) remains the machine channel, unchanged.
let renderErrorsTo (writer: System.IO.TextWriter) (errors: ValidationError list) : unit =
    if not (List.isEmpty errors) then
        let console =
            AnsiConsole.Create(AnsiConsoleSettings(Out = AnsiConsoleOutput(writer)))
        // Pin a width when the sink is not a real terminal (piped / file) so the
        // grid cells don't collapse; color is stripped for the non-terminal sink.
        let isStderr = System.Object.ReferenceEquals(writer, Console.Error)
        if (not isStderr) || Console.IsErrorRedirected then
            console.Profile.Width <- 100
        View.write console (buildErrorsView errors)

/// Render a `ValidationError list` to stderr (the common case).
let renderErrors (errors: ValidationError list) : unit =
    renderErrorsTo Console.Error errors

// --- a voiced code's surface to a writer (slice 1 catalog, §3/§6 inline) -----

/// Render a voiced code's §-surface (statement over substantiation, ending on
/// the move) to a writer — the catalog copy (`Voice.surfaceOf`) projected
/// through the same `View` engine that draws the gate and the answer. The
/// structured NDJSON channel (`LogSink.emit`) is unchanged; this is the human
/// lens for a §6 proof / §3 verdict an executor narrates inline. An unvoiced
/// code renders nothing (the caller falls back to its own narration).
let renderVoicedTo (writer: System.IO.TextWriter) (code: string) (payload: Voice.Payload) : unit =
    match Voice.surfaceOf code payload with
    | None -> ()
    | Some surface ->
        let console =
            AnsiConsole.Create(AnsiConsoleSettings(Out = AnsiConsoleOutput(writer)))
        let redirected =
            if System.Object.ReferenceEquals(writer, Console.Error) then Console.IsErrorRedirected
            elif System.Object.ReferenceEquals(writer, Console.Out) then Console.IsOutputRedirected
            else true
        if redirected then console.Profile.Width <- 100
        View.write console (Surface.render surface)
