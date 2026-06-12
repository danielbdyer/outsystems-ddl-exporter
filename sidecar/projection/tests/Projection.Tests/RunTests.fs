[<Xunit.Collection("Global-MutableState")>]
module Projection.Tests.RunTests

open System
open System.IO
open Xunit
open Projection.Core
open Projection.Pipeline

/// Masterful base #2 — the addressable run aggregate. Discriminating
/// predicate: load(save(run)) = run, and inputDigest depends only on inputs.

let private sample : Run.Run =
    { RunId = "01ABCDEF"; Ts = "2026-06-05T00:00:00Z"; Command = "projection canary"
      InputDigest = "deadbeef"; Outcome = "succeeded"; Canary = Some "green"
      Registered = 42; Applied = 3; Declined = 1
      Events = [ """{"code":"config.runStart"}"""; """{"code":"summary.runComplete"}""" ]
      Artifacts = Map.ofList [ "catalog.json", """{"modules":[]}"""; "summary.txt", "all green" ]
      Ledgers = []; Bench = None }

[<Fact>]
let ``Run: inputDigest is stable across calls and sensitive to inputs (content-addressed)`` () =
    let d = Run.inputDigest "config-text" "catalog-json"
    Assert.Equal(d, Run.inputDigest "config-text" "catalog-json")     // wall-clock independent
    Assert.NotEqual<string>(d, Run.inputDigest "config-text-2" "catalog-json")
    Assert.NotEqual<string>(d, Run.inputDigest "config-text" "catalog-json-2")

[<Fact>]
let ``Run: save then load round-trips the aggregate including the event stream`` () =
    let dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
    try
        Run.save dir sample
        match Run.load dir sample.RunId with
        | Some loaded -> Assert.Equal(sample, loaded)     // structural equality, events included
        | None -> Assert.Fail "expected to load the saved run"
    finally
        try Directory.Delete(dir, true) with _ -> ()

[<Fact>]
let ``R1a: load(save run) = run over the completed aggregate — ledger refs and bench carried`` () =
    // The codec-totality witness for the two R1a fields: a run that
    // extended both chains and carried a measurement snapshot round-trips
    // structurally (pre-R1a files load with []/None — the additive law).
    let completed =
        { sample with
            RunId = "01R1AAAA"
            Ledgers = [ Run.JournalRef "a1b2c3d4e5f60718"; Run.EpisodeRef ("dev", 3) ]
            Bench =
                Some ({ CapturedAtUtc = System.DateTime(2026, 6, 12, 15, 0, 0, System.DateTimeKind.Utc)
                        Tag = "publish"
                        Stats =
                          [ { Label = "stage.extract"; Count = 1
                              TotalMs = 120L; MinMs = 120L; MaxMs = 120L; MeanMs = 120.0
                              P50Ms = 120L; P95Ms = 120L; P99Ms = 120L } ] } : Bench.Run) }
    let dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
    try
        Run.save dir completed
        match Run.load dir completed.RunId with
        | Some loaded -> Assert.Equal(completed, loaded)
        | None -> Assert.Fail "expected to load the saved run"
    finally
        try Directory.Delete(dir, true) with _ -> ()

[<Fact>]
let ``Run: list enumerates every persisted run`` () =
    let dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
    try
        Run.save dir sample
        Run.save dir { sample with RunId = "01ZZZZ"; Canary = Some "red" }
        let runs = Run.list dir
        Assert.Equal(2, List.length runs)
        Assert.Contains(runs, fun r -> r.RunId = "01ABCDEF")
        Assert.Contains(runs, fun r -> r.RunId = "01ZZZZ" && r.Canary = Some "red")
    finally
        try Directory.Delete(dir, true) with _ -> ()

[<Fact>]
let ``Run: toLedgerEntry projects the index row (subsumes LedgerRecord)`` () =
    let e = Run.toLedgerEntry sample
    Assert.Equal(sample.RunId, e.RunId)
    Assert.Equal(sample.Canary, e.Canary)
    Assert.Equal(sample.Registered, e.Registered)
    Assert.Equal(sample.Outcome, e.Outcome)

[<Fact>]
let ``R1b: every bracketed verb's run is capturable — no orphan RunIds`` () =
    // The bracket owner (RunEnvelope) persists the completed aggregate
    // under PROJECTION_LEDGER_DIR: the stream's runId resolves to a stored
    // Run carrying the events (runStart + the §10 terminal) and the bench
    // snapshot. Opt-in: without the env var, nothing accumulates.
    let dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
    let prior = Environment.GetEnvironmentVariable "PROJECTION_LEDGER_DIR"
    try
        Environment.SetEnvironmentVariable("PROJECTION_LEDGER_DIR", dir)
        use sw = new StringWriter()
        LogSink.reset ()
        let mutable code = -1
        LogSink.withWriter sw (fun () ->
            code <- RunEnvelope.bracket "projection test-verb" ignore Map.empty (fun () -> 0, LogSink.Succeeded))
        Assert.Equal(0, code)
        let runId = LogSink.runId ()
        match Run.load dir runId with
        | Some r ->
            Assert.Equal("projection test-verb", r.Command)
            Assert.Equal("succeeded", r.Outcome)
            Assert.True(r.Bench.IsSome)
            Assert.Contains(r.Events, fun (e: string) -> e.Contains "config.runStart")
            Assert.Contains(r.Events, fun (e: string) -> e.Contains "summary.runComplete")
        | None -> Assert.Fail "expected the captured run aggregate under PROJECTION_LEDGER_DIR"
    finally
        Environment.SetEnvironmentVariable("PROJECTION_LEDGER_DIR", prior)
        try Directory.Delete(dir, true) with _ -> ()

[<Fact>]
let ``Run: capture builds a Run from the live LogSink state + the artifact tree`` () =
    use sw = new StringWriter()
    LogSink.reset ()
    LogSink.withWriter sw (fun () ->
        // registered is Debug (suppressed from display, still counted);
        // applied is Info (displayed + accumulated into the event trail).
        LogSink.emit (LogSink.envelope LogSink.Debug LogSink.Transform "transform.registered" Map.empty)
        LogSink.emit (LogSink.envelope LogSink.Info LogSink.Transform "transform.applied" Map.empty))
    let artifacts = Map.ofList [ "catalog.json", "{}" ]
    let run = Run.capture "projection emit" 0 "digest123" artifacts
    Assert.Equal("succeeded", run.Outcome)
    Assert.Equal("digest123", run.InputDigest)
    Assert.Equal(1, run.Registered)                 // counted despite Debug suppression
    Assert.Equal(1, run.Applied)
    Assert.Equal<Map<string, string>>(artifacts, run.Artifacts)   // the tree is carried
    Assert.NotEmpty(run.RunId)
    Assert.NotEmpty(run.Events)                     // the Info event is in the trail
