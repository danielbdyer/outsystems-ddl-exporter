module Projection.Tests.RunTests

open System
open System.IO
open Xunit
open Projection.Pipeline

/// Masterful base #2 — the addressable run aggregate. Discriminating
/// predicate: load(save(run)) = run, and inputDigest depends only on inputs.

let private sample : Run.Run =
    { RunId = "01ABCDEF"; Ts = "2026-06-05T00:00:00Z"; Command = "projection canary"
      InputDigest = "deadbeef"; Outcome = "succeeded"; Canary = Some "green"
      Registered = 42; Applied = 3; Declined = 1
      Events = [ """{"code":"config.runStart"}"""; """{"code":"summary.runComplete"}""" ] }

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
