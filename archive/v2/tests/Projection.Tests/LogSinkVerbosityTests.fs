[<Xunit.Collection("Global-MutableState")>]
module Projection.Tests.LogSinkVerbosityTests

open System.IO
open System.Text.Json
open Xunit
open Projection.Pipeline

/// Chapter C slice C.6 — `LogSink.Verbosity` + `setMutedCategories`
/// coverage. The richer three-way verbosity gate replaces the binary
/// `setVerbose`; per-category filters drop muted envelopes at the
/// egress boundary. Tests share the Global-MutableState collection
/// per the C.2 codified discipline (any test touching LogSink mutable
/// state must serialize via the collection to avoid cross-test
/// interference).

let private captureLines (action: unit -> unit) : string list =
    LogSink.reset ()
    use captured = new StringWriter()
    LogSink.setWriter captured
    try
        action ()
        let text = captured.ToString()
        text.Split([| '\n'; '\r' |], System.StringSplitOptions.RemoveEmptyEntries)
        |> Array.toList
    finally
        LogSink.setWriter System.Console.Error

let private parse (line: string) : JsonElement =
    (JsonDocument.Parse line).RootElement.Clone()

let private getLevel (line: string) : string =
    let elem = parse line
    let mutable v = Unchecked.defaultof<JsonElement>
    if elem.TryGetProperty("level", &v) then
        match v.GetString() with
        | null -> ""
        | s -> s
    else ""

// ----------------------------------------------------------------------
// Verbosity gate — Quiet (default)
// ----------------------------------------------------------------------

[<Fact>]
let ``C.6: Verbosity.Quiet hides Trace and Debug; surfaces Info, Warn, Error`` () =
    let lines = captureLines (fun () ->
        LogSink.setVerbosity LogSink.Verbosity.Quiet
        LogSink.emit (LogSink.envelope LogSink.Trace LogSink.Profile "p.t" Map.empty)
        LogSink.emit (LogSink.envelope LogSink.Debug LogSink.Profile "p.d" Map.empty)
        LogSink.emit (LogSink.envelope LogSink.Info LogSink.Profile "p.i" Map.empty)
        LogSink.emit (LogSink.envelope LogSink.Warn LogSink.Profile "p.w" Map.empty)
        LogSink.emit (LogSink.envelope LogSink.Error LogSink.Profile "p.e" Map.empty))
    Assert.Equal(3, List.length lines)
    let levels = lines |> List.map getLevel |> Set.ofList
    Assert.Equal<Set<string>>(Set.ofList [ "info"; "warn"; "error" ], levels)

// ----------------------------------------------------------------------
// Verbosity gate — Verbose surfaces Debug, still hides Trace
// ----------------------------------------------------------------------

[<Fact>]
let ``C.6: Verbosity.Verbose surfaces Debug, still hides Trace`` () =
    let lines = captureLines (fun () ->
        LogSink.setVerbosity LogSink.Verbosity.Verbose
        LogSink.emit (LogSink.envelope LogSink.Trace LogSink.Profile "p.t" Map.empty)
        LogSink.emit (LogSink.envelope LogSink.Debug LogSink.Profile "p.d" Map.empty)
        LogSink.emit (LogSink.envelope LogSink.Info LogSink.Profile "p.i" Map.empty))
    Assert.Equal(2, List.length lines)
    let levels = lines |> List.map getLevel |> Set.ofList
    Assert.Equal<Set<string>>(Set.ofList [ "debug"; "info" ], levels)

// ----------------------------------------------------------------------
// Verbosity gate — Verbosity.Debug surfaces both Trace and Debug
// ----------------------------------------------------------------------

[<Fact>]
let ``C.6: Verbosity.Debug surfaces Trace AND Debug`` () =
    let lines = captureLines (fun () ->
        LogSink.setVerbosity LogSink.Verbosity.Debug
        LogSink.emit (LogSink.envelope LogSink.Trace LogSink.Profile "p.t" Map.empty)
        LogSink.emit (LogSink.envelope LogSink.Debug LogSink.Profile "p.d" Map.empty)
        LogSink.emit (LogSink.envelope LogSink.Info LogSink.Profile "p.i" Map.empty))
    Assert.Equal(3, List.length lines)
    let levels = lines |> List.map getLevel |> Set.ofList
    Assert.Equal<Set<string>>(Set.ofList [ "trace"; "debug"; "info" ], levels)

// ----------------------------------------------------------------------
// Back-compat shim: setVerbose true → Verbosity.Debug
// ----------------------------------------------------------------------

[<Fact>]
let ``C.6: setVerbose true preserves prior all-on (Trace + Debug) semantics`` () =
    let lines = captureLines (fun () ->
        LogSink.setVerbose true
        LogSink.emit (LogSink.envelope LogSink.Trace LogSink.Profile "p.t" Map.empty)
        LogSink.emit (LogSink.envelope LogSink.Debug LogSink.Profile "p.d" Map.empty)
        LogSink.emit (LogSink.envelope LogSink.Info LogSink.Profile "p.i" Map.empty))
    Assert.Equal(3, List.length lines)

[<Fact>]
let ``C.6: setVerbose false equivalent to Verbosity.Quiet`` () =
    let lines = captureLines (fun () ->
        LogSink.setVerbose false
        LogSink.emit (LogSink.envelope LogSink.Trace LogSink.Profile "p.t" Map.empty)
        LogSink.emit (LogSink.envelope LogSink.Debug LogSink.Profile "p.d" Map.empty)
        LogSink.emit (LogSink.envelope LogSink.Info LogSink.Profile "p.i" Map.empty))
    Assert.Single(lines) |> ignore
    Assert.Equal("info", getLevel (List.head lines))

// ----------------------------------------------------------------------
// Per-category mute
// ----------------------------------------------------------------------

[<Fact>]
let ``C.6: empty MutedCategories preserves all-category emission`` () =
    let lines = captureLines (fun () ->
        LogSink.setMutedCategories Set.empty
        LogSink.emit (LogSink.envelope LogSink.Info LogSink.Profile "p.i" Map.empty)
        LogSink.emit (LogSink.envelope LogSink.Info LogSink.Config "c.i" Map.empty)
        LogSink.emit (LogSink.envelope LogSink.Info LogSink.Emit "e.i" Map.empty))
    Assert.Equal(3, List.length lines)

[<Fact>]
let ``C.6: muting a single category drops its envelopes; other categories surface`` () =
    let lines = captureLines (fun () ->
        LogSink.setMutedCategories (Set.singleton LogSink.Profile)
        LogSink.emit (LogSink.envelope LogSink.Info LogSink.Profile "p.i" Map.empty)
        LogSink.emit (LogSink.envelope LogSink.Info LogSink.Config "c.i" Map.empty)
        LogSink.emit (LogSink.envelope LogSink.Info LogSink.Emit "e.i" Map.empty))
    Assert.Equal(2, List.length lines)
    let codes =
        lines
        |> List.map (fun l ->
            let mutable v = Unchecked.defaultof<JsonElement>
            (parse l).TryGetProperty("code", &v) |> ignore
            match v.GetString() with null -> "" | s -> s)
        |> Set.ofList
    Assert.DoesNotContain("p.i", codes)
    Assert.Contains("c.i", codes)
    Assert.Contains("e.i", codes)

[<Fact>]
let ``C.6: muting multiple categories drops all their envelopes`` () =
    let lines = captureLines (fun () ->
        LogSink.setMutedCategories (Set.ofList [ LogSink.Profile; LogSink.Emit ])
        LogSink.emit (LogSink.envelope LogSink.Info LogSink.Profile "p.i" Map.empty)
        LogSink.emit (LogSink.envelope LogSink.Info LogSink.Config "c.i" Map.empty)
        LogSink.emit (LogSink.envelope LogSink.Info LogSink.Emit "e.i" Map.empty))
    Assert.Single(lines) |> ignore

// ----------------------------------------------------------------------
// Combined verbosity + per-category mute
// ----------------------------------------------------------------------

[<Fact>]
let ``C.6: per-category mute applies under all verbosity levels`` () =
    let lines = captureLines (fun () ->
        LogSink.setVerbosity LogSink.Verbosity.Debug
        LogSink.setMutedCategories (Set.singleton LogSink.Profile)
        LogSink.emit (LogSink.envelope LogSink.Trace LogSink.Profile "p.t" Map.empty)
        LogSink.emit (LogSink.envelope LogSink.Debug LogSink.Profile "p.d" Map.empty)
        LogSink.emit (LogSink.envelope LogSink.Trace LogSink.Config "c.t" Map.empty))
    Assert.Single(lines) |> ignore
    Assert.Equal("trace", getLevel (List.head lines))

[<Fact>]
let ``C.6: muted-but-error-level event still drops (mute supersedes severity)`` () =
    let lines = captureLines (fun () ->
        LogSink.setMutedCategories (Set.singleton LogSink.Profile)
        LogSink.emit (LogSink.envelope LogSink.Error LogSink.Profile "p.e" Map.empty)
        LogSink.emit (LogSink.envelope LogSink.Error LogSink.Config "c.e" Map.empty))
    Assert.Single(lines) |> ignore
    let mutable v = Unchecked.defaultof<JsonElement>
    (parse (List.head lines)).TryGetProperty("code", &v) |> ignore
    Assert.Equal("c.e", v.GetString())
