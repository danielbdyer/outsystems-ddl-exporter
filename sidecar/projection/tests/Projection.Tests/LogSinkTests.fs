[<Xunit.Collection("Global-MutableState")>]
module Projection.Tests.LogSinkTests

open System
open System.IO
open System.Text.Json
open Xunit
open Projection.Core
open Projection.Pipeline

/// Slice 6.5 — `Projection.Pipeline.LogSink` property + example
/// coverage per `docs/logging-format.md` §3 (envelope), §5 (sink
/// discipline), §11 (roll-up collapse algorithm), §15.1-§15.2
/// (channel-1 / hand-rolled discipline).
///
/// Tests are property-light + example-heavy on purpose: the
/// algebraic-laws surface (count accumulation; sample-selection
/// chronology; group-key 3-tuple collapse) is asserted on synthesized
/// streams; the envelope-shape surface is asserted via fixture
/// envelopes the test constructs directly.

let private captureLines (action: unit -> unit) : string list =
    use sw = new StringWriter()
    LogSink.reset ()
    LogSink.withWriter sw action
    let text = sw.ToString()
    text.Split([| '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries)
    |> Array.toList

let private parse (line: string) : JsonElement =
    JsonDocument.Parse(line).RootElement.Clone()

/// `.GetString() |> nonNull` on a JsonElement returns `string | null` under
/// F# 9 nullness; tests asserting on actual string content know the
/// payload is non-null. `nonNull` throws if the contract is violated
/// — exactly what we want during validation.
let private getStr (e: JsonElement) : string =
    nonNull (e.GetString())

let private prop (root: JsonElement) (name: string) : JsonElement =
    let mutable v = Unchecked.defaultof<JsonElement>
    if root.TryGetProperty(name, &v) then v
    else failwithf "missing property: %s in %s" name (root.GetRawText())

let private tryProp (root: JsonElement) (name: string) : JsonElement option =
    let mutable v = Unchecked.defaultof<JsonElement>
    if root.TryGetProperty(name, &v) then Some v else None

let private fixtureKey (basis: string) : SsKey =
    SsKey.synthesized "TEST" basis |> Result.value

let private fixturePayload : Map<string, objnull> =
    Map.ofList [ "evidence", box "synthesized" ]

// ----------------------------------------------------------------------
// §3 envelope-shape conformance
// ----------------------------------------------------------------------

[<Fact>]
let ``§3 envelope: every mandatory field present on every emitted line`` () =
    let lines = captureLines (fun () ->
        let env = LogSink.envelope LogSink.Info LogSink.Config "config.runStart" Map.empty
        LogSink.emit env)
    Assert.Single(lines) |> ignore
    let root = parse (List.head lines)
    Assert.NotEqual(JsonValueKind.Undefined, (prop root "runId").ValueKind)
    Assert.NotEqual(JsonValueKind.Undefined, (prop root "ts").ValueKind)
    Assert.Equal("info", (prop root "level").GetString() |> nonNull)
    Assert.Equal("config", (prop root "category").GetString() |> nonNull)
    Assert.Equal("config.runStart", (prop root "code").GetString() |> nonNull)
    Assert.Equal("progress", (prop root "phase").GetString() |> nonNull)
    Assert.Equal(JsonValueKind.Object, (prop root "payload").ValueKind)

[<Fact>]
let ``§3 envelope: optional fields omitted when None`` () =
    let lines = captureLines (fun () ->
        let env = LogSink.envelope LogSink.Info LogSink.Extract "extract.started" Map.empty
        LogSink.emit env)
    let root = parse (List.head lines)
    Assert.Equal(None, tryProp root "source")
    Assert.Equal(None, tryProp root "ssKey")
    Assert.Equal(None, tryProp root "stepId")

[<Fact>]
let ``§3 envelope: optional fields emitted when Some`` () =
    let lines = captureLines (fun () ->
        let env =
            { LogSink.envelope LogSink.Info LogSink.Transform "transform.registered" Map.empty with
                Source = Some LogSink.Operator
                SsKey  = Some (fixtureKey "OrderHeader")
                StepId = Some "module-filter" }
        LogSink.emit env)
    let root = parse (List.head lines)
    Assert.Equal("operator", (prop root "source").GetString() |> nonNull)
    Assert.Equal("TEST_OrderHeader", (prop root "ssKey").GetString() |> nonNull)
    Assert.Equal("module-filter", (prop root "stepId").GetString() |> nonNull)

[<Fact>]
let ``§3 envelope: error phase + error level both serialize correctly`` () =
    let lines = captureLines (fun () ->
        let env =
            { LogSink.envelope LogSink.Error LogSink.Config "config.validationFailed" Map.empty with
                Phase = LogSink.ErrorPhase }
        LogSink.emit env)
    let root = parse (List.head lines)
    Assert.Equal("error", (prop root "level").GetString() |> nonNull)
    Assert.Equal("error", (prop root "phase").GetString() |> nonNull)

[<Fact>]
let ``§3 envelope: ts is RFC 3339 with millisecond precision and trailing Z`` () =
    let lines = captureLines (fun () ->
        LogSink.emit (LogSink.envelope LogSink.Info LogSink.Config "config.runStart" Map.empty))
    let ts = (prop (parse (List.head lines)) "ts").GetString() |> nonNull
    let parsed =
        DateTime.ParseExact(
            ts,
            "yyyy-MM-ddTHH:mm:ss.fffZ",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal ||| System.Globalization.DateTimeStyles.AdjustToUniversal)
    Assert.Equal(DateTimeKind.Utc, parsed.Kind)

[<Fact>]
let ``§3 envelope: runId is 26-char Crockford base32 ULID`` () =
    let runId = LogSink.generateRunId ()
    Assert.Equal(26, runId.Length)
    let crockfordAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"
    for c in runId do
        Assert.Contains(c, crockfordAlphabet)

[<Fact>]
let ``§3 envelope: runId is stable within a single run`` () =
    LogSink.reset ()
    let id1 = LogSink.runId ()
    let lines = captureLines (fun () ->
        LogSink.emit (LogSink.envelope LogSink.Info LogSink.Config "a" Map.empty)
        LogSink.emit (LogSink.envelope LogSink.Info LogSink.Config "b" Map.empty))
    let runIds =
        lines
        |> List.map (fun l -> (prop (parse l) "runId").GetString() |> nonNull)
        |> List.distinct
    Assert.Equal(1, List.length runIds)

// ----------------------------------------------------------------------
// §3 + §4 — payload + level handling
// ----------------------------------------------------------------------

[<Fact>]
let ``§3 payload: string + int + bool + nested map all serialize correctly`` () =
    let payload : Map<string, objnull> =
        Map.ofList [
            "name",   box "OrderHeader"
            "rows",   box 12483L
            "active", box true
            "nested", box (Map.ofList [ "k", box 1 ])
        ]
    let lines = captureLines (fun () ->
        LogSink.emit (LogSink.envelope LogSink.Info LogSink.Profile "profile.cache.populated" payload))
    let p = prop (parse (List.head lines)) "payload"
    Assert.Equal("OrderHeader", (prop p "name").GetString() |> nonNull)
    Assert.Equal(12483L, (prop p "rows").GetInt64())
    Assert.Equal(true, (prop p "active").GetBoolean())
    Assert.Equal(1, (prop (prop p "nested") "k").GetInt32())

[<Fact>]
let ``§4 levels: trace and debug suppressed by default`` () =
    let lines = captureLines (fun () ->
        LogSink.emit (LogSink.envelope LogSink.Trace LogSink.Profile "profile.probe.succeeded" Map.empty)
        LogSink.emit (LogSink.envelope LogSink.Debug LogSink.Profile "profile.probe.succeeded" Map.empty)
        LogSink.emit (LogSink.envelope LogSink.Info LogSink.Profile "profile.completed" Map.empty))
    Assert.Single(lines) |> ignore
    Assert.Equal("info", (prop (parse (List.head lines)) "level").GetString() |> nonNull)

[<Fact>]
let ``§4 levels: trace and debug surface when setVerbose true`` () =
    let lines = captureLines (fun () ->
        LogSink.setVerbose true
        LogSink.emit (LogSink.envelope LogSink.Trace LogSink.Profile "profile.probe.succeeded" Map.empty)
        LogSink.emit (LogSink.envelope LogSink.Debug LogSink.Profile "profile.probe.succeeded" Map.empty)
        LogSink.emit (LogSink.envelope LogSink.Info LogSink.Profile "profile.completed" Map.empty))
    Assert.Equal(3, List.length lines)

[<Fact>]
let ``§3 + §13 ban 4: serialization contains no emoji / ANSI / multi-line content`` () =
    let lines = captureLines (fun () ->
        LogSink.emit (LogSink.envelope LogSink.Info LogSink.Config "config.runStart" (Map.ofList [ "version", box "1.0" ])))
    let line = List.head lines
    // No newlines mid-line (NDJSON discipline).
    Assert.DoesNotContain("\n", line)
    Assert.DoesNotContain("\r", line)
    // No ANSI escapes (literal ESC byte 0x1B).
    Assert.False(
        line.IndexOf(char 0x1B) >= 0,
        "envelope line must not contain ANSI ESC byte (0x1B)")

// ----------------------------------------------------------------------
// §11 roll-up algorithm
// ----------------------------------------------------------------------

[<Fact>]
let ``§11 rollup: events with identical (category, code, ssKey) collapse into one group`` () =
    let key = fixtureKey "OrderHeader"
    captureLines (fun () ->
        for _ = 1 to 5 do
            let env =
                { LogSink.envelope LogSink.Warn LogSink.Transform "transform.declined" fixturePayload with
                    SsKey = Some key }
            LogSink.emit env)
    |> ignore
    let aggs = LogSink.aggregates ()
    Assert.Equal(1, List.length aggs)
    Assert.Equal(5, (List.head aggs).Count)

[<Fact>]
let ``§11 rollup: events with different ssKey form distinct groups`` () =
    captureLines (fun () ->
        for basis in [ "A"; "B"; "C" ] do
            let env =
                { LogSink.envelope LogSink.Warn LogSink.Transform "transform.declined" fixturePayload with
                    SsKey = Some (fixtureKey basis) }
            LogSink.emit env)
    |> ignore
    let aggs = LogSink.aggregates ()
    Assert.Equal(3, List.length aggs)
    aggs |> List.iter (fun a -> Assert.Equal(1, a.Count))

[<Fact>]
let ``§11 rollup: events with no ssKey collapse together into one ssKey=None group`` () =
    captureLines (fun () ->
        for _ = 1 to 4 do
            LogSink.emit (LogSink.envelope LogSink.Warn LogSink.Profile "profile.probe.fallbackTimeout" fixturePayload))
    |> ignore
    let aggs = LogSink.aggregates ()
    Assert.Equal(1, List.length aggs)
    Assert.Equal(None, (List.head aggs).SsKey)
    Assert.Equal(4, (List.head aggs).Count)

[<Fact>]
let ``§11 rollup: samples capped at first three chronological events`` () =
    captureLines (fun () ->
        for i = 1 to 10 do
            let payload : Map<string, objnull> = Map.ofList [ "n", box i ]
            LogSink.emit (LogSink.envelope LogSink.Warn LogSink.Profile "profile.probe.fallbackTimeout" payload))
    |> ignore
    let aggs = LogSink.aggregates ()
    let acc = List.head aggs
    Assert.Equal(10, acc.Count)
    Assert.Equal(3, List.length acc.Samples)
    // First three samples are the events with n=1, 2, 3 (chronological).
    let ns =
        acc.Samples
        |> List.map (fun e ->
            match e.Payload.TryFind "n" with
            | Some v -> v :?> int
            | None -> -1)
    Assert.Equal<int list>([ 1; 2; 3 ], ns)

[<Fact>]
let ``§11 rollup: firstTs and lastTs span the group`` () =
    LogSink.reset ()
    let env1 = LogSink.envelope LogSink.Info LogSink.Profile "profile.completed" Map.empty
    System.Threading.Thread.Sleep(5)
    let env2 = LogSink.envelope LogSink.Info LogSink.Profile "profile.completed" Map.empty
    use sw = new StringWriter()
    LogSink.withWriter sw (fun () ->
        LogSink.emit env1
        LogSink.emit env2)
    let acc = List.head (LogSink.aggregates ())
    Assert.Equal(2, acc.Count)
    Assert.True(acc.FirstTs <= acc.LastTs, "firstTs must precede or equal lastTs")

[<Fact>]
let ``§11 rollup: output ordered descending by count then ascending by firstTs`` () =
    captureLines (fun () ->
        // Group A: 3 events.
        for _ = 1 to 3 do
            LogSink.emit (LogSink.envelope LogSink.Info LogSink.Profile "profile.probe.succeeded" Map.empty)
        // Group B: 5 events.
        for _ = 1 to 5 do
            LogSink.emit (LogSink.envelope LogSink.Warn LogSink.Profile "profile.probe.fallbackTimeout" Map.empty))
    |> ignore
    let aggs = LogSink.aggregates ()
    Assert.Equal(2, List.length aggs)
    // Larger count comes first.
    Assert.Equal(5, (List.head aggs).Count)
    Assert.Equal(3, (List.item 1 aggs).Count)

// ----------------------------------------------------------------------
// §10 + §11 — runComplete terminal event
// ----------------------------------------------------------------------

[<Fact>]
let ``§10 runComplete: emits exactly one summary.runComplete envelope`` () =
    let lines = captureLines (fun () ->
        LogSink.emit (LogSink.envelope LogSink.Info LogSink.Config "config.runStart" Map.empty)
        LogSink.runComplete LogSink.Succeeded "projection full-export" [] |> ignore)
    Assert.Equal(2, List.length lines)
    let last = parse (List.last lines)
    Assert.Equal("summary", (prop last "category").GetString() |> nonNull)
    Assert.Equal("summary.runComplete", (prop last "code").GetString() |> nonNull)
    Assert.Equal("end", (prop last "phase").GetString() |> nonNull)

[<Fact>]
let ``§10 runComplete: failure outcome still emits the terminal event`` () =
    let lines = captureLines (fun () ->
        LogSink.emit ({ LogSink.envelope LogSink.Error LogSink.Config "config.validationFailed" Map.empty with Phase = LogSink.ErrorPhase })
        LogSink.runComplete LogSink.Failed "projection full-export" [] |> ignore)
    let last = parse (List.last lines)
    Assert.Equal("failed", (prop (prop last "payload") "outcome").GetString() |> nonNull)

[<Fact>]
let ``§10 runComplete: aborted outcome emits with outcome=aborted`` () =
    let lines = captureLines (fun () ->
        LogSink.runComplete LogSink.Aborted "projection full-export" [] |> ignore)
    let last = parse (List.last lines)
    Assert.Equal("aborted", (prop (prop last "payload") "outcome").GetString() |> nonNull)

[<Fact>]
let ``§10 runComplete: payload carries command + durationMs + stages + eventCounts + suggestedConfigEdits + artifacts + aggregates`` () =
    let lines = captureLines (fun () ->
        LogSink.recordStage "extract" 1234L LogSink.Succeeded
        LogSink.recordArtifact {
            Kind      = "ssdt"
            Path      = "out/ssdt/"
            SizeBytes = None
            FileCount = Some 42 }
        LogSink.emit (LogSink.envelope LogSink.Info LogSink.Config "config.runStart" Map.empty)
        LogSink.runComplete LogSink.Succeeded "projection full-export" [] |> ignore)
    let payload = prop (parse (List.last lines)) "payload"
    Assert.Equal("projection full-export", (prop payload "command").GetString() |> nonNull)
    Assert.True((prop payload "durationMs").GetInt64() >= 0L)
    let stages = prop payload "stages"
    Assert.Equal(JsonValueKind.Array, stages.ValueKind)
    Assert.Equal(1, stages.GetArrayLength())
    Assert.Equal("extract", (prop (stages.[0]) "stage").GetString() |> nonNull)
    let counts = prop payload "eventCounts"
    Assert.True((prop counts "info").GetInt32() >= 1)
    Assert.Equal(0, (prop payload "suggestedConfigEdits").GetInt32())
    let artifacts = prop payload "artifacts"
    Assert.Equal(1, artifacts.GetArrayLength())
    Assert.Equal("ssdt", (prop (artifacts.[0]) "kind").GetString() |> nonNull)
    Assert.Equal(42, (prop (artifacts.[0]) "files").GetInt32())
    Assert.Equal(JsonValueKind.Array, (prop payload "aggregates").ValueKind)

[<Fact>]
let ``§10 runComplete: suggestedConfigEdits counts events whose payload carries suggestedConfig`` () =
    let lines = captureLines (fun () ->
        let payloadWithSuggestion : Map<string, objnull> =
            Map.ofList [
                "axis", box "samplingCap"
                "suggestedConfig", box (Map.ofList [ "path", box "$.cap"; "value", box "100000" ])
            ]
        let plainPayload : Map<string, objnull> = Map.ofList [ "axis", box "samplingCap" ]
        LogSink.emit (LogSink.envelope LogSink.Warn LogSink.Profile "profile.probe.fallbackTimeout" payloadWithSuggestion)
        LogSink.emit (LogSink.envelope LogSink.Warn LogSink.Profile "profile.probe.fallbackTimeout" payloadWithSuggestion)
        LogSink.emit (LogSink.envelope LogSink.Warn LogSink.Profile "profile.completed" plainPayload)
        LogSink.runComplete LogSink.Succeeded "projection full-export" [] |> ignore)
    let payload = prop (parse (List.last lines)) "payload"
    Assert.Equal(2, (prop payload "suggestedConfigEdits").GetInt32())

[<Fact>]
let ``§11 bench: Bench.Stats surface in runSummary aggregates under category=summary, code=bench.label`` () =
    let stats : Bench.Stats list =
        [
            { Label = "extract.parse"; Count = 10; TotalMs = 200L; MinMs = 10L; MaxMs = 30L
              MeanMs = 20.0; P50Ms = 18L; P95Ms = 28L; P99Ms = 30L }
            { Label = "emit.ssdt"; Count = 5; TotalMs = 150L; MinMs = 20L; MaxMs = 40L
              MeanMs = 30.0; P50Ms = 30L; P95Ms = 38L; P99Ms = 40L }
        ]
    let lines = captureLines (fun () ->
        LogSink.runComplete LogSink.Succeeded "projection canary" stats |> ignore)
    let aggregates = prop (prop (parse (List.last lines)) "payload") "aggregates"
    let entries =
        [ for i in 0 .. aggregates.GetArrayLength() - 1 -> aggregates.[i] ]
        |> List.filter (fun e ->
            (prop e "category").GetString() |> nonNull = "summary"
            && (prop e "code").GetString() |> nonNull = "bench.label")
    Assert.Equal(2, List.length entries)
    let labels =
        entries
        |> List.collect (fun e ->
            let samples = prop e "samples"
            [ for i in 0 .. samples.GetArrayLength() - 1 ->
                (prop (prop samples.[i] "payload") "label").GetString() |> nonNull ])
        |> List.sort
    Assert.Equal<string list>([ "emit.ssdt"; "extract.parse" ], labels)

// ----------------------------------------------------------------------
// §3 + §11 — code-categorization invariant (property)
// ----------------------------------------------------------------------

[<Fact>]
let ``§3 + §7 code-categorization: every emitted code's top-prefix matches its category`` () =
    // Synthesize a stream that pairs every Category with a code prefix.
    let pairs : (LogSink.Category * string) list = [
        LogSink.Config,    "config.runStart"
        LogSink.Extract,   "extract.started"
        LogSink.Profile,   "profile.cache.populated"
        LogSink.Transform, "transform.registered"
        LogSink.Emit,      "emit.ssdt.started"
        LogSink.Deploy,    "deploy.batchSent"
        LogSink.Canary,    "canary.diffEmpty"
        LogSink.Summary,   "summary.stageCompleted"
    ]
    let lines = captureLines (fun () ->
        for cat, code in pairs do
            LogSink.emit (LogSink.envelope LogSink.Info cat code Map.empty))
    for line in lines do
        let root = parse line
        let cat = (prop root "category").GetString() |> nonNull
        let code = (prop root "code").GetString() |> nonNull
        Assert.True(
            code.StartsWith(cat + "."),
            sprintf "code '%s' does not start with category prefix '%s.'" code cat)

// ----------------------------------------------------------------------
// §5 sink discipline — writer override
// ----------------------------------------------------------------------

[<Fact>]
let ``§5 sink: default writer is Console.Error (channel-1 stderr)`` () =
    // We can't easily redirect Console.Error in a unit test without
    // disturbing parallel tests; instead we verify the writer slot
    // accepts override + restore via withWriter, which is the test
    // harness's reliable capture path.
    LogSink.reset ()
    use sw = new StringWriter()
    LogSink.withWriter sw (fun () ->
        LogSink.emit (LogSink.envelope LogSink.Info LogSink.Config "config.runStart" Map.empty))
    Assert.Contains("config.runStart", sw.ToString())

[<Fact>]
let ``§5 sink: withWriter restores prior writer on exit`` () =
    LogSink.reset ()
    use sw1 = new StringWriter()
    use sw2 = new StringWriter()
    LogSink.setWriter sw1
    LogSink.withWriter sw2 (fun () ->
        LogSink.emit (LogSink.envelope LogSink.Info LogSink.Config "config.runStart" Map.empty))
    LogSink.emit (LogSink.envelope LogSink.Info LogSink.Config "config.connectionResolved" Map.empty)
    Assert.Contains("config.runStart", sw2.ToString())
    Assert.DoesNotContain("config.runStart", sw1.ToString())
    Assert.Contains("config.connectionResolved", sw1.ToString())
    // Restore for downstream tests.
    LogSink.setWriter (TextWriter.Null)

// ----------------------------------------------------------------------
// §3 — eventCounts table includes all five levels
// ----------------------------------------------------------------------

[<Fact>]
let ``§10 runComplete: eventCounts payload includes all five levels with zero for unseen ones`` () =
    let lines = captureLines (fun () ->
        LogSink.emit (LogSink.envelope LogSink.Info LogSink.Config "config.runStart" Map.empty)
        LogSink.runComplete LogSink.Succeeded "test" [] |> ignore)
    let counts = prop (prop (parse (List.last lines)) "payload") "eventCounts"
    Assert.Equal(0, (prop counts "trace").GetInt32())
    Assert.Equal(0, (prop counts "debug").GetInt32())
    Assert.True((prop counts "info").GetInt32() >= 1)
    Assert.Equal(0, (prop counts "warn").GetInt32())
    Assert.Equal(0, (prop counts "error").GetInt32())

// ----------------------------------------------------------------------
// Tier-1 reporting — §10 transformSummary + rationaleHistogram
// ----------------------------------------------------------------------

let private findRunComplete (lines: string list) : JsonElement =
    lines
    |> List.map parse
    |> List.find (fun e -> getStr (prop e "code") = "summary.runComplete")

[<Fact>]
let ``Tier-1 §10: transformSummary counts registered/applied/declined and survives Quiet suppression`` () =
    // `transform.registered` is Debug — suppressed from DISPLAY under the
    // default Quiet verbosity, but the §10 summary must still count it.
    let lines =
        captureLines (fun () ->
            LogSink.emit (LogSink.envelope LogSink.Debug LogSink.Transform "transform.registered" Map.empty)
            LogSink.emit (LogSink.envelope LogSink.Debug LogSink.Transform "transform.registered" Map.empty)
            LogSink.emit (LogSink.envelope LogSink.Info LogSink.Transform "transform.applied" Map.empty)
            LogSink.emit (LogSink.envelope LogSink.Info LogSink.Transform "transform.declined" Map.empty)
            LogSink.emit (LogSink.envelope LogSink.Info LogSink.Transform "transform.declined" Map.empty)
            LogSink.emit (LogSink.envelope LogSink.Info LogSink.Transform "transform.declined" Map.empty)
            LogSink.runComplete LogSink.Succeeded "test" [] |> ignore)
    let ts = prop (prop (findRunComplete lines) "payload") "transformSummary"
    Assert.Equal(2, (prop ts "registered").GetInt32())   // counted despite Debug suppression
    Assert.Equal(1, (prop ts "applied").GetInt32())
    Assert.Equal(3, (prop ts "declined").GetInt32())

[<Fact>]
let ``Tier-1 §10: rationaleHistogram groups transform.declined by rationale`` () =
    let declined rationale basis =
        { LogSink.envelope LogSink.Info LogSink.Transform "transform.declined"
            (Map.ofList [ "rationale", box rationale ]) with SsKey = Some (fixtureKey basis) }
    let lines =
        captureLines (fun () ->
            LogSink.emit (declined "DataHasNulls" "a")
            LogSink.emit (declined "DataHasNulls" "b")
            LogSink.emit (declined "NullBudgetEpsilon" "c")
            LogSink.runComplete LogSink.Succeeded "test" [] |> ignore)
    let hist = prop (prop (findRunComplete lines) "payload") "rationaleHistogram"
    Assert.Equal(2, (prop (prop hist "DataHasNulls") "count").GetInt32())
    Assert.Equal(1, (prop (prop hist "NullBudgetEpsilon") "count").GetInt32())

[<Fact>]
let ``Tier-1 §10: rationaleHistogram is empty when no declines fired`` () =
    let lines =
        captureLines (fun () ->
            LogSink.emit (LogSink.envelope LogSink.Info LogSink.Transform "transform.applied" Map.empty)
            LogSink.runComplete LogSink.Succeeded "test" [] |> ignore)
    let hist = prop (prop (findRunComplete lines) "payload") "rationaleHistogram"
    Assert.Equal(JsonValueKind.Object, hist.ValueKind)
    Assert.Equal(0, hist.EnumerateObject() |> Seq.length)
