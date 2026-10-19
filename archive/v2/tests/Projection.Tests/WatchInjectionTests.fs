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
            Watch.renderWatchOn console (Watch.seededOf Spines.pipeline) 0L (fun () ->
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
        Watch.renderWatchOn console (Watch.seededOf Spines.canary) 0L (fun () -> 0) |> ignore
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
        Watch.renderWatchOn console (Watch.seededOf Spines.pipeline) 0L (fun () ->
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

[<Fact>]
let ``renderWatchOn keeps the dwell OFF the emitting thread — emit never sleeps (#20)`` () =
    // The #20 contract: an emit only ENQUEUES and returns; it never sleeps the emitting
    // thread (the OLD design slept it INSIDE the LogSink lock). Proof by timing with a wide
    // margin — a 200ms dwell + two board-CHANGING emits. The old inline-sleep design would
    // block the body for ~2 dwells (~400ms); the off-thread design returns the emits in ~0ms
    // (the drain loop, not the body, pays the dwell). The threshold is one FULL dwell, so
    // scheduler jitter can never false-fail (the off-thread body is ~0, an order under it).
    let console = new TestConsole()
    console.Profile.Capabilities.Interactive <- true
    let bodyEmitMs = ref 0L
    Watch.renderWatchOn console (Watch.seededOf Spines.pipeline) 200L (fun () ->
        let sw = System.Diagnostics.Stopwatch.StartNew()
        // each unique "<key>.started" appends a stage → applyEnvelope changed=true → the dwell
        // path. Info/Transform clears the emit filter (same shape as the suppression test).
        for k in [ "alpha"; "beta" ] do
            LogSink.emit (LogSink.envelope LogSink.Info LogSink.Transform (k + ".started") Map.empty)
        sw.Stop()
        bodyEmitMs.Value <- sw.ElapsedMilliseconds
        0)
    |> ignore
    Assert.True(
        bodyEmitMs.Value < 200L,
        sprintf "emit blocked the body for %dms — the dwell must be off the emitting thread (#20)" bodyEmitMs.Value)

[<Fact>]
let ``renderWatchOn propagates a body exception as itself and never hangs (#20 teardown)`` () =
    // The teardown contract behind the off-thread reap: when the body (now on a background
    // task) throws, the call propagates the ORIGINAL exception — `GetAwaiter().GetResult()`
    // unwraps it rather than surfacing an `AggregateException` — and returns (no deadlock, the
    // producer is reaped, subscribers cleared in the finally). Every other test runs a
    // non-throwing body, so this is the only cover for the failure path.
    Environment.SetEnvironmentVariable("PROJECTION_WATCH_DWELL_MS", "0")
    try
        let console = new TestConsole()
        console.Profile.Capabilities.Interactive <- true
        let boom = InvalidOperationException("body boom")
        let thrown =
            try
                Watch.renderWatchOn console (Watch.seededOf Spines.pipeline) 0L (fun () ->
                    LogSink.emit (LogSink.envelope LogSink.Info LogSink.Transform "alpha.started" Map.empty)
                    raise boom)
                |> ignore
                None
            with e -> Some e
        match thrown with
        | Some e -> Assert.Same(boom, e)   // the original exception, NOT an AggregateException wrap
        | None -> Assert.True(false, "renderWatchOn must propagate the body's exception")
    finally
        Environment.SetEnvironmentVariable("PROJECTION_WATCH_DWELL_MS", null)

[<Fact>]
let ``Watch board: an active stage breathes the phase's spinner frame (#20)`` () =
    // The breathing spinner — an Active stage line renders `Theme.spinner phase` in place of
    // the static ▸, so a long-running stage visibly pulses as the drain loop advances `phase`.
    let active, _ = Watch.apply Watch.empty "extract.started" Map.empty   // one Active stage
    let console = new TestConsole()
    console.Write(Watch.toRenderableWith [] 2 None active)   // header, phase 2, no quiet gap
    let out = console.Output
    Assert.Contains(Theme.spinner 2, out)               // the active line wears the phase-2 frame
    Assert.DoesNotContain(Theme.spinner 3, out)         // and only that frame (phase is fixed per render)

[<Fact>]
let ``renderWatchOn: a flood of foreign envelopes never starves the spinner heartbeat (#20 rework)`` () =
    // The starvation defect: the OLD loop only advanced the spinner on a TryTake
    // TIMEOUT, so a pipeline chatting non-board envelopes faster than one per
    // 100ms froze the board for the whole flood. The reworked loop heartbeats on
    // wall clock. The body floods foreign envelopes for ~600ms — the output must
    // carry spinner frames BEYOND the one painted by the stage transition
    // (phase 1), i.e. heartbeat repaints happened DURING the flood.
    Environment.SetEnvironmentVariable("PROJECTION_WATCH_DWELL_MS", "0")
    try
        let console = new TestConsole()
        console.Profile.Capabilities.Interactive <- true
        Watch.renderWatchOn console (Watch.seededOf Spines.pipeline) 0L (fun () ->
            LogSink.emit (LogSink.envelope LogSink.Info LogSink.Transform "extract.started" Map.empty)
            let sw = System.Diagnostics.Stopwatch.StartNew()
            while sw.ElapsedMilliseconds < 600L do
                LogSink.emit (LogSink.envelope LogSink.Info LogSink.Transform "transform.chatter" Map.empty)
            0)
        |> ignore
        let out = console.Output
        Assert.Contains(Theme.spinner 2, out)   // ≥1 heartbeat repaint during the flood
        Assert.Contains(Theme.spinner 3, out)   // ≥2 — the spinner is breathing, not ticking once
        // A chatty pipeline is ALIVE: liveness keys off dequeued envelopes, so the
        // flood must never degrade the line to the quiet `processing` suffix.
        Assert.DoesNotContain("processing", out)
    finally
        Environment.SetEnvironmentVariable("PROJECTION_WATCH_DWELL_MS", null)

[<Fact>]
let ``renderWatchOn: a progress backlog coalesces — no post-run frame replay (#20 rework)`` () =
    // The backlog defect: the OLD loop dwelled 120ms per renderable envelope with
    // no coalescing, so 10,000 progress events queued ≈ 20 MINUTES of replay after
    // the body had already returned. The reworked loop folds the whole backlog into
    // coalesced frames and dwells on stage TRANSITIONS only — the run must complete
    // in seconds, with the dwell floor left at a real value (120ms).
    let console = new TestConsole()
    console.Profile.Capabilities.Interactive <- true
    let sw = System.Diagnostics.Stopwatch.StartNew()
    let code =
        Watch.renderWatchOn console (Watch.seededOf Spines.pipeline) 120L (fun () ->
            LogSink.emit (LogSink.envelope LogSink.Info LogSink.Transform "extract.started" Map.empty)
            for i in 1 .. 10_000 do
                LogSink.recordStageProgress "extract" i 10_000 (int64 i)
            0)
    sw.Stop()
    Assert.Equal(0, code)
    Assert.True(
        sw.ElapsedMilliseconds < 8_000L,
        sprintf "10k progress envelopes took %dms — the backlog must coalesce, never replay one dwelled frame per envelope" sw.ElapsedMilliseconds)

[<Fact>]
let ``renderWatchOn: a quiet progress-less stage renders processing after the threshold (#20 rework, amended 2026-07-06)`` () =
    // The honesty half of the quiet design: a stage with NO progress events (every
    // full-export stage today) that goes quiet past the threshold says `processing`
    // in words — never `stalled` (a verdict the event stream cannot ground; the
    // stage may be quiet-but-working) and never a bare frozen spinner. Threshold
    // pinned low via the env seam.
    Environment.SetEnvironmentVariable("PROJECTION_WATCH_DWELL_MS", "0")
    Environment.SetEnvironmentVariable("PROJECTION_WATCH_STALL_MS", "50")
    try
        let console = new TestConsole()
        console.Profile.Capabilities.Interactive <- true
        Watch.renderWatchOn console (Watch.seededOf Spines.pipeline) 0L (fun () ->
            LogSink.emit (LogSink.envelope LogSink.Info LogSink.Transform "extract.started" Map.empty)
            System.Threading.Thread.Sleep 400
            0)
        |> ignore
        Assert.Contains("processing", console.Output)
        Assert.DoesNotContain("stalled", console.Output)
    finally
        Environment.SetEnvironmentVariable("PROJECTION_WATCH_STALL_MS", null)
        Environment.SetEnvironmentVariable("PROJECTION_WATCH_DWELL_MS", null)

[<Fact>]
let ``renderWatchOn detaches its subscriber on every exit path (#20 teardown)`` () =
    // The teardown law: the board's LogSink subscriber never outlives the render —
    // a leaked subscriber would enqueue into a completed queue forever after. The
    // witness is `LogSink.subscriberCount` (test-only accessor); asserted after a
    // clean body AND after a throwing body.
    Environment.SetEnvironmentVariable("PROJECTION_WATCH_DWELL_MS", "0")
    LogSink.clearSubscribers ()
    try
        let console = new TestConsole()
        console.Profile.Capabilities.Interactive <- true
        Watch.renderWatchOn console (Watch.seededOf Spines.pipeline) 0L (fun () -> 0) |> ignore
        Assert.Equal(0, LogSink.subscriberCount ())
        (try
            Watch.renderWatchOn console (Watch.seededOf Spines.pipeline) 0L (fun () -> failwith "boom") |> ignore
         with _ -> ())
        Assert.Equal(0, LogSink.subscriberCount ())
    finally
        Environment.SetEnvironmentVariable("PROJECTION_WATCH_DWELL_MS", null)
