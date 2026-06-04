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
        if code = 0 then "[green]SUCCEEDED[/]" else "[red]FAILED[/]"
    let canary =
        match LogSink.canaryVerdict () with
        | Some "green" -> "[green]green — diff empty[/]"
        | Some "red"   -> "[red]RED — divergence[/]"
        | Some other   -> Markup.Escape other
        | None         -> "[grey](no canary leg)[/]"
    let edits = LogSink.suggestedConfigEdits ()
    let editsCell =
        if edits = 0 then "[grey]0[/]"
        else sprintf "[yellow]%d[/] config edit(s) suggested" edits

    let grid = Grid()
    grid.AddColumn() |> ignore
    grid.AddColumn() |> ignore
    grid.AddRow("outcome", outcome) |> ignore
    grid.AddRow("canary", canary) |> ignore
    grid.AddRow(
        "transforms",
        sprintf "%d registered · %d applied · %d declined" registered applied declined) |> ignore
    grid.AddRow("actionable", editsCell) |> ignore

    (match RunLedger.configuredDir () with
     | Some dir ->
         let r = RunLedger.read dir |> RunLedger.readiness
         let gate =
             if r.Eligible then "[green]ELIGIBLE[/]" else "[yellow]not yet[/]"
         grid.AddRow(
             "R6 gate",
             sprintf "%d/%d consecutive green — %s" r.ConsecutiveGreen r.Threshold gate) |> ignore
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
