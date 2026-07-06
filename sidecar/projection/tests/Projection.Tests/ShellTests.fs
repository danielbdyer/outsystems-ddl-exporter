[<Xunit.Collection("Global-MutableState")>]
module Projection.Tests.ShellTests

open System
open System.IO
open Xunit
open Spectre.Console.Testing
open Projection.Pipeline
open Projection.Cli

/// THE OPERATOR SHELL (2026-07-02) — `Shell.execute` is the ONE door every
/// verb's run walks through. These tests pin the behavior matrix on the
/// injected-console core (`Shell.executeOn` — pretty/live arrive resolved, so
/// no real TTY is needed): the live boxed board + verdict panel for a spined
/// run; the static preview frame + suppressed channel 1 for an answer verb;
/// byte-identical bracketed NDJSON for pipes; the notice rollup row on the
/// board; the ledger append on every path.

let private goFrame (command: string) : Shell.Frame =
    { Command = command; Flow = None; Register = Shell.Go }

let private newConsole () =
    let c = new TestConsole()
    c.Profile.Capabilities.Interactive <- true
    c

[<Fact>]
let ``shell: a spined pretty run renders the live boxed board, then the verdict panel`` () =
    Environment.SetEnvironmentVariable("PROJECTION_WATCH_DWELL_MS", "0")
    LogSink.reset ()
    try
        let console = newConsole ()
        let code =
            Shell.executeOn console true true (goFrame "projection check") Shell.Bracket.Bracketed (Some Spines.canary) (fun () ->
                LogSink.emit (LogSink.envelope LogSink.Info LogSink.Canary "canary.started" Map.empty)
                LogSink.recordStageEvent "canary" 5L LogSink.Succeeded
                0)
        Assert.Equal(0, code)
        let out = console.Output
        // the contained box — the Live region wraps in the rounded panel
        Assert.Contains("╭", out)
        Assert.Contains("╰", out)
        // the frame titles the box
        Assert.Contains("projection check", out)
        // the stage line ran
        Assert.Contains("Verifying the round-trip", out)
        // the verdict panel rendered after the board (the resolved final frame)
        Assert.Contains("verdict", out)
    finally
        Environment.SetEnvironmentVariable("PROJECTION_WATCH_DWELL_MS", null)

[<Fact>]
let ``shell: a pretty run without a spine renders the preview frame and suppresses channel 1`` () =
    LogSink.reset ()
    use outer = new StringWriter()
    LogSink.setWriter outer
    try
        let console = newConsole ()
        let frame = { goFrame "projection preview" with Register = Shell.Preview }
        let code =
            Shell.executeOn console true false frame Shell.Bracket.Bracketed None (fun () ->
                LogSink.emit (LogSink.envelope LogSink.Info LogSink.Transform "transform.midRun" Map.empty)
                0)
        Assert.Equal(0, code)
        // the preview frame said so up front
        Assert.Contains("Nothing will be written", console.Output)
        // channel 1 was suppressed for the span (scoped null)
        Assert.DoesNotContain("transform.midRun", outer.ToString())
        // the verdict panel closed the run
        Assert.Contains("verdict", console.Output)
    finally
        LogSink.setWriter Console.Error

[<Fact>]
let ``shell: a non-pretty run is the bare bracket — run envelopes on the wire, no panels`` () =
    LogSink.reset ()
    use wire = new StringWriter()
    LogSink.setWriter wire
    try
        let console = newConsole ()
        let code =
            Shell.executeOn console false false (goFrame "projection check drift") Shell.Bracket.Bracketed None (fun () -> 0)
        Assert.Equal(0, code)
        let ndjson = wire.ToString()
        // the A3 sweep: previously-unbracketed verbs now open and close the envelope
        Assert.Contains("config.runStart", ndjson)
        Assert.Contains("summary.runComplete", ndjson)
        // and NOTHING rendered on the console (a pipe stays clean)
        Assert.Equal("", console.Output)
    finally
        LogSink.setWriter Console.Error

[<Fact>]
let ``shell: a failing body closes the bracket failed and returns its code`` () =
    LogSink.reset ()
    use wire = new StringWriter()
    LogSink.setWriter wire
    try
        let console = newConsole ()
        let code =
            Shell.executeOn console false false (goFrame "projection check data") Shell.Bracket.Bracketed None (fun () -> 8)
        Assert.Equal(8, code)
        Assert.Contains("summary.runComplete", wire.ToString())
    finally
        LogSink.setWriter Console.Error

[<Fact>]
let ``shell: the notice rollup rides the live board as one calm strip row`` () =
    Environment.SetEnvironmentVariable("PROJECTION_WATCH_DWELL_MS", "0")
    LogSink.reset ()
    try
        let console = newConsole ()
        Shell.executeOn console true true (goFrame "projection full-export") Shell.Bracket.SelfBracketed (Some (Spines.publishWith false false)) (fun () ->
            LogSink.emit (LogSink.envelope LogSink.Info LogSink.Extract "extract.started" Map.empty)
            // the producer-aggregated Warn rollup (the flood, condensed)
            LogSink.emit
                (LogSink.envelope LogSink.Warn LogSink.Extract LiveModelRead.noticeRollupCode
                    (Map.ofList
                        [ "total",        box 214
                          "nullability",  box 180
                          "identity",     box 34
                          "artifactPath", box "notices/model-read/01RUN.json" ]))
            LogSink.recordStageEvent "extract" 3L LogSink.Succeeded
            LogSink.recordStageStart "profile"
            LogSink.recordStageEvent "profile" 3L LogSink.Succeeded
            LogSink.recordStageStart "emit"
            LogSink.recordStageEvent "emit" 3L LogSink.Succeeded
            0)
        |> ignore
        let out = console.Output
        // one calm line naming the families — never a wall. (Single-word pins:
        // the boxed board wraps the row at console width, so multi-word
        // substrings can split across lines.)
        Assert.Contains("214", out)
        Assert.Contains("180", out)
        Assert.Contains("nullability", out)
        // and the artifact pointer rides the strip row
        Assert.Contains("notices/model-read/01RUN.json", out)
    finally
        Environment.SetEnvironmentVariable("PROJECTION_WATCH_DWELL_MS", null)

[<Fact>]
let ``shell: under pretty, stdout narration defers and flushes after the verdict panel (2026-07-06)`` () =
    Environment.SetEnvironmentVariable("PROJECTION_WATCH_DWELL_MS", "0")
    LogSink.reset ()
    let priorOut = Console.Out
    use captured = new StringWriter()
    Console.SetOut captured
    try
        let console = newConsole ()
        Shell.executeOn console true true (goFrame "projection full-export") Shell.Bracket.SelfBracketed (Some (Spines.publishWith false false)) (fun () ->
            printfn "42 artifact(s) written to ./dist/full-export."
            LogSink.emit (LogSink.envelope LogSink.Info LogSink.Extract "extract.started" Map.empty)
            LogSink.recordStageEvent "extract" 3L LogSink.Succeeded
            LogSink.recordStageStart "profile"
            LogSink.recordStageEvent "profile" 3L LogSink.Succeeded
            LogSink.recordStageStart "emit"
            LogSink.recordStageEvent "emit" 3L LogSink.Succeeded
            0)
        |> ignore
        // The narration reached the real stdout — deferred, never lost...
        Assert.Contains("42 artifact(s) written", captured.ToString())
        // ...and never the box surface (the board + panel own that console).
        Assert.DoesNotContain("42 artifact(s) written", console.Output)
    finally
        Console.SetOut priorOut
        Environment.SetEnvironmentVariable("PROJECTION_WATCH_DWELL_MS", null)

[<Fact>]
let ``shell: a non-pretty run's stdout narration is untouched (no buffer)`` () =
    LogSink.reset ()
    let priorOut = Console.Out
    use captured = new StringWriter()
    Console.SetOut captured
    try
        let console = newConsole ()
        LogSink.withWriter TextWriter.Null (fun () ->
            Shell.executeOn console false false (goFrame "projection emit") Shell.Bracket.Bracketed None (fun () ->
                printfn "plain narration line"
                0))
        |> ignore
        Assert.Contains("plain narration line", captured.ToString())
    finally
        Console.SetOut priorOut

[<Fact>]
let ``shell: boardOfStored reconstructs the notice row from a stored run (R1e)`` () =
    let payload =
        sprintf
            """{"code":"%s","payload":{"total":214,"nullability":180,"identity":34,"artifactPath":"notices/model-read/01RUN.json"}}"""
            LiveModelRead.noticeRollupCode
    let board =
        Watch.boardOfStored
            (Watch.seededOf (Spines.publishWith false false))
            [ """{"code":"extract.started","payload":{}}"""
              payload ]
    match board.Notices with
    | [ (code, p) ] ->
        Assert.Equal(LiveModelRead.noticeRollupCode, code)
        Assert.Equal(box 214L, p.["total"])
    | other -> Assert.Fail(sprintf "expected one notice row, got %A" other)

[<Fact>]
let ``shell: identical rollups from the run's repeated read legs fold to ONE row — counts never double`` () =
    let payload : Map<string, objnull> = Map.ofList [ "total", box 214 ]
    let b1, f1 = Watch.applyKind Watch.empty LiveModelRead.noticeRollupCode payload
    Assert.Equal(Watch.Fold.Progressed, f1)
    let b2, _ = Watch.applyKind b1 LiveModelRead.noticeRollupCode payload
    Assert.Equal(1, List.length b2.Notices)

[<Fact>]
let ``shell: the run joins the cross-run ledger on every path (the full-export parity gap, closed)`` () =
    let dir = Path.Combine(Path.GetTempPath(), "proj-shell-ledger-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory dir |> ignore
    Environment.SetEnvironmentVariable("PROJECTION_LEDGER_DIR", dir)
    LogSink.reset ()
    try
        let console = newConsole ()
        // the publish path (SelfBracketed) — the arm that used to skip the ledger
        Shell.executeOn console false false (goFrame "projection full-export") Shell.Bracket.SelfBracketed None (fun () -> 0) |> ignore
        let records = RunLedger.read dir
        Assert.Equal(1, List.length records)
        Assert.Equal("projection full-export", (List.head records).Command)
    finally
        Environment.SetEnvironmentVariable("PROJECTION_LEDGER_DIR", null)
        (try Directory.Delete(dir, true) with _ -> ())

[<Fact>]
let ``shell: titleOf frames a flow with its route and register`` () =
    let flow : Shell.FlowRoute = { Name = "publish"; From = "cloud-dev"; To = "on-prem-dev" }
    let go : Shell.Frame = { Command = "projection publish"; Flow = Some flow; Register = Shell.Go }
    let preview = { go with Register = Shell.Preview }
    Assert.Contains("publish: cloud-dev", Shell.titleOf go)
    Assert.Contains("on-prem-dev", Shell.titleOf go)
    Assert.DoesNotContain("preview", Shell.titleOf go)
    Assert.Contains("— preview", Shell.titleOf preview)
    let readOnly : Shell.Frame = { Command = "projection diff"; Flow = None; Register = Shell.ReadOnly }
    Assert.Equal("projection diff", Shell.titleOf readOnly)

[<Fact>]
let ``shell: a flow frame titles the live board with name, route, and register`` () =
    Environment.SetEnvironmentVariable("PROJECTION_WATCH_DWELL_MS", "0")
    LogSink.reset ()
    try
        let console = newConsole ()
        let frame : Shell.Frame =
            { Command  = "projection publish"
              Flow     = Some { Name = "publish"; From = "cloud-dev"; To = "on-prem-dev" }
              Register = Shell.Go }
        Shell.executeOn console true true frame Shell.Bracket.Bracketed (Some Spines.canary) (fun () ->
            LogSink.emit (LogSink.envelope LogSink.Info LogSink.Canary "canary.started" Map.empty)
            LogSink.recordStageEvent "canary" 2L LogSink.Succeeded
            0)
        |> ignore
        let out = console.Output
        Assert.Contains("publish: cloud-dev", out)
        Assert.Contains("on-prem-dev", out)
    finally
        Environment.SetEnvironmentVariable("PROJECTION_WATCH_DWELL_MS", null)

[<Fact>]
let ``shell: framed preserves the ambient flow frame for a verb arm, and takes the command otherwise`` () =
    let flowFrame : Shell.Frame =
        { Command  = "projection publish"
          Flow     = Some { Name = "publish"; From = "cloud-dev"; To = "on-prem-dev" }
          Register = Shell.Preview }
    Shell.currentFrame.Value <- flowFrame
    try
        // a flow-dispatched verb keeps the flow's words
        Assert.Equal("projection publish", (Shell.framed "projection transfer").Command)
        Assert.Equal(Shell.Preview, (Shell.framed "projection transfer").Register)
        // a direct verb takes its own command
        Shell.currentFrame.Value <- Shell.Frame.ofCommand "projection"
        Assert.Equal("projection diff", (Shell.framed "projection diff").Command)
    finally
        Shell.currentFrame.Value <- Shell.Frame.ofCommand "projection"

[<Fact>]
let ``flow menu: the view renders as a table and its toJson carries every flow (one substrate)`` () =
    let view =
        TtyRenderer.buildFlowMenuView
            [ "publish", "cloud-dev", "on-prem-dev", ""
              "try",     "cloud-dev", "local",       "rekey" ]
    // plain lens
    use sw = new StringWriter()
    let console = View.consoleTo sw
    View.write console view
    let plain = sw.ToString()
    Assert.Contains("publish", plain)
    Assert.Contains("on-prem-dev", plain)
    Assert.Contains("rekey", plain)
    // machine lens — the same document, full
    let json = (View.toJson view).ToJsonString()
    Assert.Contains("publish", json)
    Assert.Contains("local", json)

[<Fact>]
let ``echo-the-fix: a suggestedConfig-bearing envelope ticks the board's live teaser (#6)`` () =
    let payload : Map<string, objnull> =
        Map.ofList [ "suggestedConfig", box (Map.ofList [ "path", box "$.profiling.samplingCap" ] : Map<string, objnull>) ]
    let b1, f1 = Watch.applyKind Watch.empty "transform.diagnostic" payload
    Assert.Equal(Watch.Fold.Progressed, f1)
    Assert.Equal(1, b1.SuggestedEdits)
    let b2, _ = Watch.applyKind b1 "transform.diagnostic" payload
    Assert.Equal(2, b2.SuggestedEdits)
    // the teaser renders through the catalog (one register)
    let console = new TestConsole()
    console.Write(Watch.toRenderableWith [] 0 None b2)
    Assert.Contains("2 optional configuration recommendation(s) gathered", console.Output)

[<Fact>]
let ``stat view: the aggregates table names category, code, and count, most-frequent first`` () =
    LogSink.reset ()
    LogSink.withWriter TextWriter.Null (fun () ->
        for _ in 1 .. 3 do LogSink.emit (LogSink.envelope LogSink.Info LogSink.Transform "transform.applied" Map.empty)
        LogSink.emit (LogSink.envelope LogSink.Warn LogSink.Extract "adapter.ossys.modelRead.noticeRollup" Map.empty))
    let view = TtyRenderer.buildStatView (LogSink.aggregates ())
    use sw = new StringWriter()
    View.write (View.consoleTo sw) view
    let plain = sw.ToString()
    Assert.Contains("transform.applied", plain)
    Assert.Contains("noticeRollup", plain)
    Assert.Contains("3", plain)
