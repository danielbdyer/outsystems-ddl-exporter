[<Xunit.Collection("Global-MutableState")>]
module Projection.Tests.NoticeRoutingTests

open System
open System.IO
open Xunit
open Projection.Core
open Projection.Pipeline

/// The notice-flood remediation (2026-07-02): a model read's divergence notices
/// condense to ONE Warn rollup envelope + a detail artifact (`NoticeSink`),
/// with the per-item entries riding channel 1 at Debug — the F9
/// surface-never-discard law kept, the per-item stderr wall retired. These
/// tests pin the pure rollup payload, the sink's merge-dedupe append, and the
/// verbosity split (per-item hidden under Quiet; the rollup always visible).

let private entry code (metadata: (string * string) list) : DiagnosticEntry =
    { DiagnosticEntry.create "adapter:OSSYS" DiagnosticSeverity.Warning code (code + " message")
        with Metadata = Map.ofList metadata }

// ---------------------------------------------------------------------------
// the pure rollup payload (the §12 constant-size law)
// ---------------------------------------------------------------------------

[<Fact>]
let ``noticeRollup counts FACTS per family — the aggregated nullability entry counts its columns`` () =
    let entries =
        [ entry "adapter.ossys.columnReality.nullabilityDivergence" [ "count", "180" ]
          entry "adapter.ossys.columnReality.identityDivergence" []
          entry "adapter.ossys.columnReality.identityDivergence" []
          entry "adapter.ossys.primaryKey.divergence" [] ]
    let payload = LiveModelRead.noticeRollup "notices/model-read/r1.json" entries
    Assert.Equal(box 183, payload.["total"])
    Assert.Equal(box 180, payload.["nullability"])
    Assert.Equal(box 2,   payload.["identity"])
    Assert.Equal(box 1,   payload.["primaryKey"])
    Assert.Equal(box "notices/model-read/r1.json", payload.["artifactPath"])

[<Fact>]
let ``noticeRollup samples the first three messages only — the surface is a constant size`` () =
    let entries = [ for i in 1 .. 40 -> entry "adapter.ossys.columnReality.identityDivergence" [ "n", string i ] ]
    let payload = LiveModelRead.noticeRollup "p.json" entries
    let samples = string payload.["samples"]
    // three samples joined by the separator — two separators, never forty
    Assert.Equal(2, samples.Split(" | ").Length - 1)
    Assert.Equal(box 40, payload.["total"])

[<Fact>]
let ``noticeRollup names an unrecognized family honestly as other`` () =
    let payload = LiveModelRead.noticeRollup "p.json" [ entry "adapter.ossys.somethingNew" [] ]
    Assert.Equal(box 1, payload.["other"])

// ---------------------------------------------------------------------------
// the notice artifact (NoticeSink — merge-deduped across a run's read legs)
// ---------------------------------------------------------------------------

[<Fact>]
let ``NoticeSink.append round-trips notices and dedupes identical re-reads`` () =
    let dir = Path.Combine(Path.GetTempPath(), "proj-notices-" + Guid.NewGuid().ToString("N"))
    try
        let path = NoticeSink.runPath dir "model-read" "01RUN"
        let notices : NoticeSink.Notice list =
            [ { Code = "a.code"; Severity = "warning"; Message = "m1"; Metadata = Map.ofList [ "k", "v" ] }
              { Code = "b.code"; Severity = "info";    Message = "m2"; Metadata = Map.empty } ]
        let first = NoticeSink.append path notices
        // a publish run re-reads the model for the store/load legs — identical
        // facts must not double the artifact
        let second = NoticeSink.append path notices
        let third = NoticeSink.append path [ { Code = "c.code"; Severity = "warning"; Message = "m3"; Metadata = Map.empty } ]
        Assert.Equal(2, List.length first)
        Assert.Equal(2, List.length second)
        Assert.Equal(3, List.length third)
        Assert.True(File.Exists path)
    finally
        (try Directory.Delete(dir, true) with _ -> ())

[<Fact>]
let ``NoticeSink.runPath addresses the artifact by run — notices slash tag slash runId`` () =
    let path = NoticeSink.runPath "/root" "model-read" "01RUN"
    Assert.Equal(Path.Combine("/root", "notices", "model-read", "01RUN.json"), path)

// ---------------------------------------------------------------------------
// the verbosity split (per-item Debug hidden under Quiet; rollup Warn visible)
// ---------------------------------------------------------------------------

[<Fact>]
let ``per-item divergence envelopes are Debug — hidden under Quiet, visible under Verbose; the Warn rollup always rides`` () =
    LogSink.reset ()
    LogSink.clearSubscribers ()
    use quiet = new StringWriter()
    LogSink.withWriter quiet (fun () ->
        LogSink.emit (LogSink.envelope LogSink.Debug LogSink.Extract "adapter.ossys.columnReality.identityDivergence" Map.empty)
        LogSink.emit (LogSink.envelope LogSink.Warn LogSink.Extract LiveModelRead.noticeRollupCode Map.empty))
    Assert.DoesNotContain("identityDivergence", quiet.ToString())
    Assert.Contains(LiveModelRead.noticeRollupCode, quiet.ToString())
    LogSink.reset ()
    LogSink.setVerbosity LogSink.Verbosity.Verbose
    use verbose = new StringWriter()
    try
        LogSink.withWriter verbose (fun () ->
            LogSink.emit (LogSink.envelope LogSink.Debug LogSink.Extract "adapter.ossys.columnReality.identityDivergence" Map.empty))
        Assert.Contains("identityDivergence", verbose.ToString())
    finally
        LogSink.reset ()

// ---------------------------------------------------------------------------
// the voiced rollup line (the copy the board's notice strip renders)
// ---------------------------------------------------------------------------

[<Fact>]
let ``the rollup copy reads as one calm line naming families, and points at the artifact`` () =
    match Projection.Cli.Voice.lookup LiveModelRead.noticeRollupCode with
    | None -> Assert.Fail "the rollup code must be voiced"
    | Some copy ->
        let payload : Map<string, objnull> =
            Map.ofList
                [ "total",        box 214
                  "nullability",  box 180
                  "identity",     box 34
                  "artifactPath", box "notices/model-read/01RUN.json" ]
        match copy.Statement payload with
        | Projection.Cli.View.Note t ->
            Assert.Contains("214", t)
            // Count-first, concrete family labels (2026-07-06 full-voice audit) —
            // and the internal compound "model-reality" never reaches the surface.
            Assert.Contains("180 nullability difference(s)", t)
            Assert.Contains("34 identity-flag difference(s)", t)
            Assert.Contains("no action is required", t)
            Assert.DoesNotContain("model-reality", t)
        | other -> Assert.Fail(sprintf "expected a Note statement, got %A" other)
        match copy.Action payload with
        | Some (Projection.Cli.View.Action a) -> Assert.Contains("notices/model-read/01RUN.json", a)
        | other -> Assert.Fail(sprintf "expected an artifact-pointing action, got %A" other)
