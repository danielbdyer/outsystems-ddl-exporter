[<Xunit.Collection("Global-MutableState")>]
module Projection.Tests.TtyRendererTests

open System.IO
open Xunit
open Spectre.Console
open Projection.Core
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
    // The verdict line is voiced by `Voice` keyed by code: a green canary leg →
    // the §6 round-trip-verification proof (`canary.diffEmpty`).
    Assert.Contains("matches the model", text)
    Assert.Contains("projection canary", text)
    Assert.Contains("registered", text)

[<Fact>]
let ``Tier-3: verdict panel shows a red canary on divergence`` () =
    let text =
        renderToString "projection canary" 5 (fun () ->
            LogSink.emit
                { LogSink.envelope LogSink.Error LogSink.Canary "canary.divergence"
                    (Map.ofList [ "renderedDiff", box "x" ]) with Phase = LogSink.ErrorPhase })
    // A red canary leg → the §10 round-trip-verification-failed verdict.
    Assert.Contains("diverged", text)

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

[<Fact>]
let ``Tier-4 board: the timeline reads the recent checks in words and names the present run`` () =
    let r : RunLedger.Readiness =
        { TotalRuns = 11; CanaryRuns = 11; ConsecutiveGreen = 6
          LastCanary = Some "green"; Threshold = 10; Eligible = false }
    let text = renderBoard r [ "green"; "green"; "red"; "green"; "green"; "green" ]
    Assert.Contains("the last 6 check(s)", text)
    Assert.Contains("1 diverged", text)
    Assert.Contains("run 11, the present one", text)

[<Fact>]
let ``Tier-4 board: an all-green timeline says so plainly`` () =
    let r : RunLedger.Readiness =
        { TotalRuns = 5; CanaryRuns = 5; ConsecutiveGreen = 5
          LastCanary = Some "green"; Threshold = 10; Eligible = false }
    let text = renderBoard r [ "green"; "green"; "green" ]
    Assert.Contains("all passed", text)

// --- the Gate surface (INSTRUMENT slice 3) ---------------------------------
// Discriminating predicate: a destructive refusal leads Bad and names the
// drop-approval next action (the real --allow-drops / --declare-drop levers);
// a blocking pre-flight refusal leads Warn with none.
// Both carry the gate axis, the detail, and the distinct exit code — not one
// flat error string.

let private renderGateText (command: string) (refusal: Preflight.GateRefusal) : string =
    use sw = new StringWriter()
    let console =
        AnsiConsole.Create(
            AnsiConsoleSettings(
                Ansi = AnsiSupport.No, ColorSystem = ColorSystemSupport.NoColors,
                Out = AnsiConsoleOutput(sw)))
    console.Profile.Width <- 200
    View.write console (TtyRenderer.buildGateView command refusal)
    sw.ToString()

[<Fact>]
let ``Gate: a destructive refusal stops with the loss, the exit, and the declare-loss action`` () =
    let refusal =
        Preflight.refusalOf [ ValidationError.create "migrate.undeclaredDestructiveChange" "dropping index IX_Order_Stale" ]
    let text = renderGateText "projection migrate" refusal
    Assert.Contains("drops a database object", text)       // the statement (Bad hero; the true verb, §5)
    Assert.Contains("undeclared destructive change", text) // the gate axis
    Assert.Contains("dropping index IX_Order_Stale", text) // the detail
    Assert.Contains("9", text)                             // the distinct exit code
    Assert.Contains("--declare-drop", text)                // the next action (the real per-drop lever)
    Assert.Contains("✕", text)                             // Bad glyph — survives NO_COLOR

[<Fact>]
let ``Gate: a blocking pre-flight refusal leads Warn, with no declare-loss action`` () =
    let refusal =
        Preflight.refusalOf [ ValidationError.create "migrate.connectionUnavailable" "could not reach UAT" ]
    let text = renderGateText "projection migrate" refusal
    Assert.Contains("connection unavailable", text)   // the gate axis
    Assert.Contains("could not reach UAT", text)      // the detail
    Assert.DoesNotContain("--declare-drop", text)     // not a declared-loss refusal
    Assert.Contains("▲", text)                        // Warn glyph
