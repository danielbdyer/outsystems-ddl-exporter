[<Xunit.Collection("Global-MutableState")>]
module Projection.Tests.ProgressRendererTests

open System
open System.IO
open Xunit
open Spectre.Console.Testing
open Projection.Pipeline
open Projection.Cli

/// The standalone Progress widget (2026-07-08, the row-118 cash-out): the
/// animated Spectre `Progress` bars as a second rendering of the SAME
/// `summary.stageProgress` feed. The interesting logic is the pure fold
/// (`apply` — envelope → model); the live loop mirrors the model onto Spectre
/// tasks and is asserted for the off-thread teardown law (mirroring
/// `WatchInjectionTests`).

let private started (key: string) : LogSink.Envelope =
    LogSink.envelope LogSink.Info LogSink.Transform (key + ".started") Map.empty

let private progress (key: string) (doneN: int) (total: int) : LogSink.Envelope =
    LogSink.envelope LogSink.Info LogSink.Transform "summary.stageProgress"
        (Map.ofList [ "stage", box key; "done", box doneN; "total", box total; "elapsedMs", box 100L ])

let private completed (key: string) : LogSink.Envelope =
    LogSink.envelope LogSink.Info LogSink.Transform "summary.stageCompleted"
        (Map.ofList [ "stage", box key ])

let private fold (envs: LogSink.Envelope list) : ProgressRenderer.Model =
    envs |> List.fold ProgressRenderer.apply ProgressRenderer.empty

// -- the pure fold -----------------------------------------------------------

[<Fact>]
let ``apply: a started stage seeds a leg; progress sets its determinate tally`` () =
    let m = fold [ started "load"; progress "load" 3 10 ]
    Assert.Equal<string list>([ "load" ], m.Order)
    let c = m.Cells.["load"]
    Assert.Equal(3, c.Done)
    Assert.Equal(10, c.Total)          // a real denominator → a determinate bar
    Assert.False(c.Completed)

[<Fact>]
let ``apply: an unknown total stays a spinner (Total <= 0), never a misstated bar`` () =
    let c = (fold [ started "publish"; progress "publish" 42 0 ]).Cells.["publish"]
    Assert.Equal(42, c.Done)
    Assert.Equal(0, c.Total)           // no denominator → the renderer draws a spinner, not a bar

[<Fact>]
let ``apply: completed closes the leg`` () =
    let c = (fold [ started "deploy"; progress "deploy" 5 5; completed "deploy" ]).Cells.["deploy"]
    Assert.True(c.Completed)

[<Fact>]
let ``apply: progress before the start marker seeds the leg (some producers emit no start)`` () =
    let m = fold [ progress "extract" 1 23 ]
    Assert.Equal<string list>([ "extract" ], m.Order)
    Assert.Equal(23, m.Cells.["extract"].Total)

[<Fact>]
let ``apply: first-seen order is preserved across legs; a completed-for-unstarted umbrella is ignored`` () =
    let m = fold [ started "extract"; started "load"; completed "never-started" ]
    Assert.Equal<string list>([ "extract"; "load" ], m.Order)
    Assert.False(m.Cells.ContainsKey "never-started")

// -- the live loop: the off-thread teardown law ------------------------------

[<Fact>]
let ``renderProgressOn runs the body under the live bars and propagates its code`` () =
    let console = new TestConsole()
    console.Profile.Capabilities.Interactive <- true
    let ran = ref false
    let code =
        ProgressRenderer.renderProgressOn console (fun () ->
            LogSink.emit (started "load")
            LogSink.recordStageProgress "load" 5 10 100L
            ran.Value <- true
            7)
    Assert.True(ran.Value, "the body must run inside the progress region")
    Assert.Equal(7, code)

[<Fact>]
let ``renderProgressOn suppresses channel 1 during the body and restores the prior writer`` () =
    use outer = new StringWriter()
    LogSink.setWriter outer
    try
        let console = new TestConsole()
        console.Profile.Capabilities.Interactive <- true
        ProgressRenderer.renderProgressOn console (fun () ->
            LogSink.emit (LogSink.envelope LogSink.Info LogSink.Transform "transform.midRun" Map.empty)
            0)
        |> ignore
        Assert.DoesNotContain("transform.midRun", outer.ToString())   // nulled for the live span
        LogSink.emit (LogSink.envelope LogSink.Info LogSink.Transform "transform.postRun" Map.empty)
        Assert.Contains("transform.postRun", outer.ToString())        // prior writer restored
    finally
        LogSink.setWriter Console.Error

[<Fact>]
let ``renderProgressOn detaches its subscriber on every exit path (teardown law)`` () =
    LogSink.clearSubscribers ()
    let console = new TestConsole()
    console.Profile.Capabilities.Interactive <- true
    ProgressRenderer.renderProgressOn console (fun () -> 0) |> ignore
    Assert.Equal(0, LogSink.subscriberCount ())
    (try ProgressRenderer.renderProgressOn console (fun () -> failwith "boom") |> ignore with _ -> ())
    Assert.Equal(0, LogSink.subscriberCount ())    // cleared even when the body throws

[<Fact>]
let ``renderProgressOn propagates a body exception as itself`` () =
    let console = new TestConsole()
    console.Profile.Capabilities.Interactive <- true
    let boom = InvalidOperationException("body boom")
    let thrown =
        try ProgressRenderer.renderProgressOn console (fun () -> raise boom) |> ignore; None
        with e -> Some e
    match thrown with
    | Some e -> Assert.Same(boom, e)
    | None   -> Assert.True(false, "must propagate the body's exception")
