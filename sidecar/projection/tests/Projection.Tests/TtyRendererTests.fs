[<Xunit.Collection("Global-MutableState")>]
module Projection.Tests.TtyRendererTests

open System.IO
open Xunit
open Spectre.Console
open Projection.Pipeline
open Projection.Cli

/// Tier-3 reporting — the Spectre channel-2 verdict panel. The renderer is
/// parameterized on the `IAnsiConsole` (production targets stderr), so the
/// test renders to a `StringWriter` with colors stripped and asserts the
/// panel's text content. The panel is a *projection* of the same `LogSink`
/// run state the NDJSON stream emits — never a second emit surface.

let private renderToString (command: string) (code: int) (setup: unit -> unit) : string =
    LogSink.reset ()
    setup ()
    use sw = new StringWriter()
    let console =
        AnsiConsole.Create(
            AnsiConsoleSettings(
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Out = AnsiConsoleOutput(sw)))
    // A StringWriter console has no terminal width; pin it wide so grid cells
    // aren't wrapped/truncated mid-word in the assertion.
    console.Profile.Width <- 200
    TtyRenderer.renderSummaryTo console command code
    sw.ToString()

[<Fact>]
let ``Tier-3: verdict panel shows outcome + green canary + transform counts`` () =
    let text =
        renderToString "projection canary" 0 (fun () ->
            LogSink.emit (LogSink.envelope LogSink.Debug LogSink.Transform "transform.registered" Map.empty)
            LogSink.emit
                { LogSink.envelope LogSink.Info LogSink.Canary "canary.diffEmpty"
                    (Map.ofList [ "tableCount", box 4 ]) with Phase = LogSink.End })
    Assert.Contains("SUCCEEDED", text)
    Assert.Contains("green", text)
    Assert.Contains("projection canary", text)
    Assert.Contains("registered", text)

[<Fact>]
let ``Tier-3: verdict panel shows a red canary on divergence`` () =
    let text =
        renderToString "projection canary" 5 (fun () ->
            LogSink.emit
                { LogSink.envelope LogSink.Error LogSink.Canary "canary.divergence"
                    (Map.ofList [ "renderedDiff", box "x" ]) with Phase = LogSink.ErrorPhase })
    Assert.Contains("FAILED", text)
    Assert.Contains("RED", text)

[<Fact>]
let ``Tier-3: shouldRender is false when --pretty not requested`` () =
    Assert.False(TtyRenderer.shouldRender false)

let private renderBoard (r: RunLedger.Readiness) (recent: string list) : string =
    use sw = new StringWriter()
    let console =
        AnsiConsole.Create(
            AnsiConsoleSettings(
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Out = AnsiConsoleOutput(sw)))
    console.Profile.Width <- 200
    TtyRenderer.renderReadinessBoardTo console r recent "/x/runs.jsonl"
    sw.ToString()

[<Fact>]
let ``Tier-4 board: leads with ELIGIBLE + full meter + history dots`` () =
    let r : RunLedger.Readiness =
        { TotalRuns = 12; CanaryRuns = 12; ConsecutiveGreen = 10
          LastCanary = Some "green"; Threshold = 10; Eligible = true }
    let text = renderBoard r [ "green"; "green"; "red"; "green" ]
    Assert.Contains("ELIGIBLE", text)
    Assert.Contains("10 / 10 green", text)
    Assert.Contains("▇", text)   // the cutover meter
    Assert.Contains("●", text)   // history dots

[<Fact>]
let ``Tier-4 board: NOT YET names the runs-to-go`` () =
    let r : RunLedger.Readiness =
        { TotalRuns = 8; CanaryRuns = 8; ConsecutiveGreen = 7
          LastCanary = Some "green"; Threshold = 10; Eligible = false }
    let text = renderBoard r [ "green"; "red"; "green" ]
    Assert.Contains("NOT YET", text)
    Assert.Contains("3 green run", text)   // 10 - 7 = 3 to go
