module Projection.Cli.TtyRenderer

open System
open Spectre.Console
open Projection.Pipeline

/// Tier-3 reporting (`REPORTING_HORIZON.md` §3; `docs/logging-format.md`
/// §15.3) — the Spectre.Console "channel 2" renderer. It is a **derived
/// consumer** of the same run state the NDJSON stream projects (it reads
/// `LogSink` accessors + the ledger), never a second emit surface. Gated on
/// `--pretty` AND a real TTY by the caller. The live progress-bar leg (during
/// a run) is the documented follow-on; this renders the "after" verdict panel.

/// True iff a pretty render is warranted: the operator asked for it AND
/// stderr is a real terminal (not a pipe / file). Per §15.1 — never draw
/// ANSI into a redirected stream.
let shouldRender (prettyRequested: bool) : bool =
    prettyRequested && not Console.IsErrorRedirected

/// Build + write the terminal verdict panel to the given console. Pulls the
/// run's outcome / canary verdict / transform counts / actionable-edit count
/// from the `LogSink` accumulator, plus the R6 readiness gauge when a ledger
/// is configured. Parameterized on the console so tests render to a
/// `StringWriter` and assert the panel content (the production wrapper
/// targets stderr).
let renderSummaryTo (console: IAnsiConsole) (command: string) (code: int) : unit =
    let registered, applied, declined = LogSink.transformCounts ()
    let outcome =
        if code = 0 then Theme.green (Theme.ok + " SUCCEEDED")
        else Theme.red (Theme.bad + " FAILED")
    let canary =
        match LogSink.canaryVerdict () with
        | Some "green" -> Theme.green (Theme.ok + " green — diff empty")
        | Some "red"   -> Theme.red (Theme.bad + " RED — divergence")
        | Some other   -> Markup.Escape other
        | None         -> Theme.muted (Theme.dot + " no canary leg")
    let edits = LogSink.suggestedConfigEdits ()
    let editsCell =
        if edits = 0 then Theme.muted (Theme.ok + " none")
        else Theme.yellow (sprintf "%s %d config edit(s) suggested" Theme.warn edits)

    let grid = Grid()
    grid.AddColumn() |> ignore
    grid.AddColumn() |> ignore
    grid.AddRow(Theme.muted "outcome", outcome) |> ignore
    grid.AddRow(Theme.muted "canary", canary) |> ignore
    grid.AddRow(
        Theme.muted "transforms",
        sprintf "%d registered %s %d applied %s %d declined" registered Theme.dot applied Theme.dot declined) |> ignore
    grid.AddRow(Theme.muted "actionable", editsCell) |> ignore

    (match RunLedger.configuredDir () with
     | Some dir ->
         let r = RunLedger.read dir |> RunLedger.readiness
         let gate =
             if r.Eligible then Theme.green (Theme.ok + " ELIGIBLE") else Theme.yellow "not yet"
         grid.AddRow(
             Theme.muted "cutover",
             sprintf "%s  %d / %d green %s %s"
                 (Theme.meter r.ConsecutiveGreen r.Threshold)
                 r.ConsecutiveGreen r.Threshold Theme.arrow gate) |> ignore
     | None -> ())

    let panel = Panel(grid)
    panel.Header <- PanelHeader(sprintf " %s " (Markup.Escape command))
    panel.Border <- BoxBorder.Rounded
    console.Write(panel)

/// Render the verdict panel to stderr (channel 2 — §5: stderr is the events
/// surface; the panel is a rendering of events, stdout stays the narration
/// surface).
let renderSummary (command: string) (code: int) : unit =
    let console =
        AnsiConsole.Create(AnsiConsoleSettings(Out = AnsiConsoleOutput(Console.Error)))
    renderSummaryTo console command code
