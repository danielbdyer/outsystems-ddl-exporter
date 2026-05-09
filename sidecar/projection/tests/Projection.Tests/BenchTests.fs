module Projection.Tests.BenchTests

open System
open System.IO
open System.Threading
open Xunit
open Projection.Core

/// Smoke tests for `Projection.Core.Bench`. Per session-29 framing,
/// the canary's flywheel rests on Bench surfacing time use across
/// runs; these tests pin the API's contract so future agents
/// extending it (regression assertions, sparse-percentile sampling,
/// HdrHistogram) inherit a stable surface.
///
/// The decoration pattern is exercised end-to-end by the canary
/// tests (CanaryDeployTests / CanaryRoundTripTests) — those are
/// the integration witnesses. These tests are unit-level
/// correctness checks on the Bench primitives themselves.

let private resetThenScope (label: string) (sleepMs: int) =
    use _ = Bench.scope label
    Thread.Sleep(sleepMs)

[<Fact>]
let ``Bench: scope records elapsed time on Dispose`` () =
    Bench.reset ()
    do resetThenScope "test.elapsed" 50
    let stats = Bench.snapshot ()
    let entry = stats |> List.find (fun s -> s.Label = "test.elapsed")
    Assert.Equal(1, entry.Count)
    // 50ms sleep ± system jitter — assert ≥40ms (loose lower bound;
    // CI runners can be slow but rarely drop below half the sleep).
    Assert.True(
        entry.MinMs >= 40L,
        sprintf "expected MinMs >= 40, got %d" entry.MinMs)

[<Fact>]
let ``Bench: nested scopes record independently`` () =
    Bench.reset ()
    (use _ = Bench.scope "test.outer"
     do resetThenScope "test.inner" 20)
    let stats = Bench.snapshot ()
    let outer = stats |> List.find (fun s -> s.Label = "test.outer")
    let inner = stats |> List.find (fun s -> s.Label = "test.inner")
    Assert.Equal(1, outer.Count)
    Assert.Equal(1, inner.Count)
    // Outer always >= inner since outer's scope encloses inner.
    Assert.True(
        outer.TotalMs >= inner.TotalMs,
        sprintf "expected outer (%d) >= inner (%d)" outer.TotalMs inner.TotalMs)

[<Fact>]
let ``Bench: repeated scopes accumulate samples and compute percentiles`` () =
    Bench.reset ()
    for sleepMs in [ 10; 20; 30; 40; 50 ] do
        do resetThenScope "test.percentile" sleepMs
    let stats = Bench.snapshot ()
    let entry = stats |> List.find (fun s -> s.Label = "test.percentile")
    Assert.Equal(5, entry.Count)
    // P50 of [10,20,30,40,50] (with idx = round((5-1) * 0.5) = 2) = 30
    // The actual slept times will be ≥ requested due to OS jitter;
    // assert the relative ordering rather than exact values.
    Assert.True(entry.P50Ms >= entry.MinMs)
    Assert.True(entry.P99Ms >= entry.P50Ms)
    Assert.True(entry.MaxMs >= entry.P99Ms)
    Assert.Equal(entry.MaxMs |> max entry.MinMs, entry.MaxMs)

[<Fact>]
let ``Bench: snapshot is sorted by TotalMs descending`` () =
    Bench.reset ()
    do resetThenScope "test.short" 5
    do resetThenScope "test.long" 80
    do resetThenScope "test.short" 5
    let stats = Bench.snapshot ()
    let labels = stats |> List.map (fun s -> s.Label)
    let longIdx = labels |> List.findIndex (fun l -> l = "test.long")
    let shortIdx = labels |> List.findIndex (fun l -> l = "test.short")
    Assert.True(
        longIdx < shortIdx,
        sprintf "expected test.long (%d) sorted before test.short (%d)" longIdx shortIdx)

[<Fact>]
let ``Bench: reset clears accumulated samples`` () =
    Bench.reset ()
    do resetThenScope "test.willBeCleared" 10
    Bench.reset ()
    let stats = Bench.snapshot ()
    Assert.Empty(stats)

[<Fact>]
let ``Bench: renderTable produces a non-empty markdown-style table for non-empty stats`` () =
    Bench.reset ()
    do resetThenScope "test.render" 20
    let stats = Bench.snapshot ()
    let table = Bench.renderTable stats
    Assert.Contains("test.render", table)
    Assert.Contains("Count", table)
    Assert.Contains("Total", table)
    Assert.Contains("P50", table)

[<Fact>]
let ``Bench: renderTable signals empty snapshot rather than producing a header-only table`` () =
    let table = Bench.renderTable []
    Assert.Contains("no samples", table)

[<Fact>]
let ``Bench: persistJson writes a JSON file at the given path with the tag and stats`` () =
    Bench.reset ()
    do resetThenScope "test.persist" 15
    let stats = Bench.snapshot ()
    let tempDir =
        Path.Combine(Path.GetTempPath(), sprintf "bench-tests-%s" (Guid.NewGuid().ToString "N"))
    let path = Path.Combine(tempDir, "snapshot.json")
    try
        Bench.persistJson path "smoke-tag" stats
        Assert.True(File.Exists path)
        let content = File.ReadAllText path
        Assert.Contains("smoke-tag", content)
        Assert.Contains("test.persist", content)
        Assert.Contains("CapturedAtUtc", content)
    finally
        if Directory.Exists tempDir then
            Directory.Delete(tempDir, recursive = true)

[<Fact>]
let ``Bench: defaultPath produces a sortable timestamped path under bench/<tag>/`` () =
    let path = Bench.defaultPath "/tmp/example" "wide-canary-enterprise"
    Assert.Contains("bench", path)
    Assert.Contains("wide-canary-enterprise", path)
    Assert.EndsWith(".json", path)
    // Filename is yyyyMMddTHHmmssZ.json — sortable.
    let filename = Path.GetFileName path
    Assert.Matches(@"^\d{8}T\d{6}Z\.json$", filename)
