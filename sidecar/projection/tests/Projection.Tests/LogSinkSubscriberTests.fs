[<Xunit.Collection("Global-MutableState")>]
module Projection.Tests.LogSinkSubscriberTests

open System
open System.IO
open Xunit
open Projection.Pipeline

/// `LogSink.addSubscriber` — the channel-2 push primitive the dynamic
/// display's "watch" leg subscribes to (`REPORTING_HORIZON`; the live-display
/// leg). Discriminating predicate: a subscriber receives **exactly** the
/// envelopes channel 1 writes, in emission order — a Debug envelope suppressed
/// under Quiet, or an envelope in a muted category, reaches NEITHER the writer
/// NOR a subscriber. A naive "fire on every emit call" diverges on the
/// suppressed/muted inputs; a naive "never fire" diverges on the first visible
/// one. The hook also carries the terminal `summary.runComplete` (the renderer
/// draws its final verdict panel off that event).

let private info cat code =
    LogSink.envelope LogSink.Info cat code Map.empty

/// Run `action` with a capturing subscriber + a `StringWriter` channel-1 sink;
/// returns (ndjsonLines, receivedEnvelopes). Brackets `reset` +
/// `clearSubscribers` so the process-global LogSink state cannot leak across
/// the serialized `Global-MutableState` collection.
let private capture (action: unit -> unit) : string list * LogSink.Envelope list =
    LogSink.reset ()
    LogSink.clearSubscribers ()
    let received = ResizeArray<LogSink.Envelope>()
    LogSink.addSubscriber (fun e -> received.Add e)
    use sw = new StringWriter()
    try
        LogSink.withWriter sw action
        let lines =
            sw.ToString().Split([| '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries)
            |> Array.toList
        lines, List.ofSeq received
    finally
        LogSink.clearSubscribers ()

let private codesOf (envs: LogSink.Envelope list) : string list =
    envs |> List.map (fun e -> e.Code)

[<Fact>]
let ``addSubscriber: a subscriber receives every emitted envelope`` () =
    let _, received =
        capture (fun () ->
            LogSink.emit (info LogSink.Config "config.runStart")
            LogSink.emit (info LogSink.Extract "extract.started"))
    Assert.Equal<string list>([ "config.runStart"; "extract.started" ], codesOf received)

[<Fact>]
let ``addSubscriber: delivery order matches emission order`` () =
    let codes = [ "config.runStart"; "extract.started"; "profile.started"; "emit.started" ]
    let _, received =
        capture (fun () ->
            for c in codes do LogSink.emit (info LogSink.Emit c))
    Assert.Equal<string list>(codes, codesOf received)

[<Fact>]
let ``addSubscriber: subscribers mirror channel 1 — Debug under Quiet reaches neither`` () =
    let lines, received =
        capture (fun () ->
            LogSink.setVerbosity LogSink.Verbosity.Quiet
            LogSink.emit (info LogSink.Profile "profile.started")
            LogSink.emit { info LogSink.Profile "profile.cache.populated" with Level = LogSink.Debug })
    // channel 1 wrote one line; the subscriber received the same one — the
    // suppressed Debug envelope is on neither channel.
    Assert.Single(lines) |> ignore
    Assert.Equal<string list>([ "profile.started" ], codesOf received)

[<Fact>]
let ``addSubscriber: a muted category reaches neither channel`` () =
    let lines, received =
        capture (fun () ->
            LogSink.setMutedCategories (Set.ofList [ LogSink.Extract ])
            LogSink.emit (info LogSink.Extract "extract.started")
            LogSink.emit (info LogSink.Emit "emit.started"))
    Assert.Single(lines) |> ignore
    Assert.Equal<string list>([ "emit.started" ], codesOf received)

[<Fact>]
let ``runComplete: the terminal envelope reaches subscribers`` () =
    let _, received =
        capture (fun () ->
            LogSink.emit (info LogSink.Extract "extract.started")
            LogSink.runComplete LogSink.Succeeded "projection test" [] |> ignore)
    Assert.Contains("summary.runComplete", codesOf received)

[<Fact>]
let ``clearSubscribers: detaches — no delivery after clear`` () =
    LogSink.reset ()
    LogSink.clearSubscribers ()
    let received = ResizeArray<LogSink.Envelope>()
    LogSink.addSubscriber (fun e -> received.Add e)
    use sw = new StringWriter()
    try
        LogSink.withWriter sw (fun () ->
            LogSink.emit (info LogSink.Config "config.runStart")
            LogSink.clearSubscribers ()
            LogSink.emit (info LogSink.Extract "extract.started"))
        // only the pre-clear envelope was delivered.
        Assert.Equal<string list>([ "config.runStart" ], codesOf (List.ofSeq received))
    finally
        LogSink.clearSubscribers ()
