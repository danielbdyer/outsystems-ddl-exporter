[<Xunit.Collection("Global-MutableState")>]
module Projection.Tests.WatchInjectionTests

open System
open System.IO
open Xunit
open Spectre.Console.Testing
open Projection.Pipeline
open Projection.Cli

/// Slice-1 (pretty) testability: `renderWatch` is now split into a
/// console-injectable core (`renderWatchOn`) + a stderr wrapper, mirroring
/// `TtyRenderer.renderSummaryTo` / `renderSummary`. That lets the live board be
/// asserted with a `Spectre.Console.Testing.TestConsole` — no real TTY — and is
/// the foundation for the Intervene↔board bridge (pause the Live region to
/// prompt on the same surface). Dwell is pinned to 0 so the test never sleeps.

[<Fact>]
let ``renderWatchOn runs the body under the live board on an injected console`` () =
    Environment.SetEnvironmentVariable("PROJECTION_WATCH_DWELL_MS", "0")
    try
        let console = new TestConsole()
        console.Profile.Capabilities.Interactive <- true
        let ran = ref false
        let code =
            Watch.renderWatchOn console Spines.pipeline 0L (fun () ->
                ran.Value <- true
                7)
        Assert.True(ran.Value, "the body must run inside the live region")
        Assert.Equal(7, code)
    finally
        Environment.SetEnvironmentVariable("PROJECTION_WATCH_DWELL_MS", null)

[<Fact>]
let ``cutoverStripText renders dots, present marker, R6 meter, and ratio`` () =
    let line = Watch.cutoverStripText [ "green"; "green"; "red" ] 2 10
    Assert.Contains("●", line)   // green canary dot
    Assert.Contains("✕", line)   // red verdict
    Assert.Contains("▸", line)   // present marker (NO_COLOR-safe glyph)
    Assert.Contains("▇", line)   // R6 meter
    Assert.Contains("2/10", line)
    // empty history → no present marker, just an empty strip + the meter
    Assert.DoesNotContain("▸", Watch.cutoverStripText [] 0 10)

[<Fact>]
let ``renderWatchOn shows the cutover timeline header from the ledger's canary history`` () =
    Environment.SetEnvironmentVariable("PROJECTION_WATCH_DWELL_MS", "0")
    let dir = Path.Combine(Path.GetTempPath(), "proj-tl-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(dir) |> ignore
    Environment.SetEnvironmentVariable("PROJECTION_LEDGER_DIR", dir)
    try
        for idx in 1..3 do
            RunLedger.append dir
                { RunId = sprintf "r%d" idx
                  Ts = sprintf "2026-06-17T00:00:0%dZ" idx
                  Command = "projection canary"
                  Outcome = "succeeded"
                  Canary = Some "green"
                  Registered = 0
                  Applied = 0
                  Declined = 0 }
        let console = new TestConsole()
        console.Profile.Capabilities.Interactive <- true
        Watch.renderWatchOn console Spines.canary 0L (fun () -> 0) |> ignore
        let out = console.Output
        Assert.Contains("▇", out)     // the R6 meter rode the header
        Assert.Contains("3/10", out)  // three consecutive green
    finally
        Environment.SetEnvironmentVariable("PROJECTION_LEDGER_DIR", null)
        Environment.SetEnvironmentVariable("PROJECTION_WATCH_DWELL_MS", null)
        (try Directory.Delete(dir, true) with _ -> ())

[<Fact>]
let ``renderWatchOn suppresses channel 1 during the body and restores the prior writer`` () =
    // The composability guarantee behind Slice 1's leak fix: the board nulls
    // channel 1 via the SCOPED `LogSink.withWriter`, so under an outer writer
    // (what `--pretty`/`withRun` installs) the body's NDJSON is suppressed and
    // the PRIOR writer — not a hardcoded `Console.Error` — is restored on exit.
    Environment.SetEnvironmentVariable("PROJECTION_WATCH_DWELL_MS", "0")
    use outer = new StringWriter()
    LogSink.setWriter outer
    try
        let console = new TestConsole()
        console.Profile.Capabilities.Interactive <- true
        Watch.renderWatchOn console Spines.pipeline 0L (fun () ->
            LogSink.emit (LogSink.envelope LogSink.Info LogSink.Transform "transform.midRun" Map.empty)
            0)
        |> ignore
        // mid-run emit was suppressed (writer nulled for the live region)
        Assert.DoesNotContain("transform.midRun", outer.ToString())
        // prior writer restored: a post-run emit lands in `outer`
        LogSink.emit (LogSink.envelope LogSink.Info LogSink.Transform "transform.postRun" Map.empty)
        Assert.Contains("transform.postRun", outer.ToString())
    finally
        LogSink.setWriter Console.Error
        Environment.SetEnvironmentVariable("PROJECTION_WATCH_DWELL_MS", null)
