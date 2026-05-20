[<Xunit.Collection("Global-MutableState")>]
module Projection.Tests.FullExportCliTests

open System
open System.IO
open System.Text.Json
open Xunit
open Projection.Core
open Projection.Pipeline

/// Chapter B.4 slice 7 — `projection full-export` CLI surface
/// integration coverage. Exercises the orchestration end-to-end against
/// in-memory + temp-file fixtures: config parse → Compose.runWithConfig
/// → artifact write → NDJSON event-stream conformance.
///
/// Per chapter B.4 close gate: verifies (a) the default behavior emits
/// conforming events per `docs/logging-format.md` §3-§13 + §15.1, and
/// (b) the terminal `summary.runComplete` event carries the
/// orchestrated stages + artifacts + roll-up aggregates per §10 + §11.
///
/// Test surface lives in-process (the CLI's `runFullExport` would
/// require a separate process to test) by directly mirroring its
/// orchestration shape: reset LogSink, install a StringWriter to
/// capture stderr-bound envelopes, invoke `Compose.runWithConfig`,
/// emit the terminal envelope, parse the captured NDJSON. This catches
/// the same conformance regressions an out-of-process integration test
/// would catch, at unit-test speed (no Docker; no `dotnet projection
/// full-export` subprocess).

let private v1MinimalFixture : string =
    """{
  "exportedAtUtc": "2026-05-20T00:00:00.0000000+00:00",
  "modules": [
    {
      "name": "AppCore",
      "isSystem": false,
      "isActive": true,
      "entities": [
        {
          "name": "User",
          "physicalName": "OSUSR_APPCORE_USER",
          "isStatic": false,
          "isExternal": false,
          "isActive": true,
          "db_catalog": null,
          "db_schema": "dbo",
          "attributes": [
            {
              "name": "Id",
              "physicalName": "ID",
              "originalName": null,
              "dataType": "Identifier",
              "length": null,
              "precision": null,
              "scale": null,
              "default": null,
              "isMandatory": true,
              "isIdentifier": true,
              "isAutoNumber": true,
              "isActive": true,
              "isReference": 0,
              "refEntityId": null,
              "refEntity_name": null,
              "refEntity_physicalName": null,
              "reference_deleteRuleCode": null,
              "reference_hasDbConstraint": 0,
              "external_dbType": null,
              "physical_isPresentButInactive": 0
            }
          ],
          "relationships": [],
          "indexes": [],
          "triggers": []
        }
      ]
    }
  ]
}"""

let private writeTempJson (content: string) : string =
    let path =
        Path.Combine(
            Path.GetTempPath(),
            sprintf "projection-fullexport-%s.json" (Guid.NewGuid().ToString("N")))
    File.WriteAllText(path, content)
    path

let private writeTempConfig (modelPath: string) (outputDir: string) : string =
    let json =
        sprintf
            """{ "model": { "path": "%s" }, "output": { "dir": "%s" } }"""
            (modelPath.Replace("\\", "\\\\"))
            (outputDir.Replace("\\", "\\\\"))
    writeTempJson json

let private tempOutputDir () : string =
    Path.Combine(
        Path.GetTempPath(),
        sprintf "projection-fullexport-out-%s" (Guid.NewGuid().ToString("N")))

let private parseLines (text: string) : JsonElement list =
    text.Split([| '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries)
    |> Array.toList
    |> List.map (fun line -> (JsonDocument.Parse line).RootElement.Clone())

let private prop (e: JsonElement) (name: string) : JsonElement =
    let mutable v = Unchecked.defaultof<JsonElement>
    if e.TryGetProperty(name, &v) then v
    else failwithf "missing property %s in %s" name (e.GetRawText())

let private getStr (e: JsonElement) : string =
    nonNull (e.GetString())

/// Mirror of Program.fs `runFullExport` minus the dumpBench surface +
/// process-exit shape. Returns the captured NDJSON text + exit code
/// so tests can assert on both.
let private runFullExportInProcess
    (configPath: string)
    (outputOverride: string option)
    : int * string =
    LogSink.reset ()
    Bench.reset ()
    use captured = new StringWriter()
    LogSink.setWriter captured
    let mutable outcome : LogSink.Outcome = LogSink.Succeeded
    let mutable exitCode = 0
    try
        try
            match Config.fromFile configPath with
            | Error errors ->
                for e in errors do
                    let payload : Map<string, objnull> =
                        Map.ofList [ "code", box e.Code; "reason", box e.Message ]
                    LogSink.emit
                        { LogSink.envelope LogSink.Error LogSink.Config "config.validationFailed" payload with
                            Phase = LogSink.ErrorPhase }
                outcome <- LogSink.Failed
                exitCode <- 6
            | Ok cfg ->
                let effectiveOutput =
                    match outputOverride with
                    | Some dir when not (String.IsNullOrWhiteSpace dir) -> dir
                    | _ -> cfg.Output.Dir
                let cfgForRun = { cfg with Output = { Dir = effectiveOutput } }
                let startPayload : Map<string, objnull> =
                    Map.ofList [
                        "command",    box "projection full-export"
                        "configPath", box configPath
                        "outputDir",  box effectiveOutput
                    ]
                LogSink.emit
                    { LogSink.envelope LogSink.Info LogSink.Config "config.runStart" startPayload with
                        Phase = LogSink.Start }
                let connPayload : Map<string, objnull> =
                    Map.ofList [ "kind", box "SnapshotJson"; "modelPath", box cfg.Model.Path ]
                LogSink.emit
                    { LogSink.envelope LogSink.Info LogSink.Config "config.connectionResolved" connPayload with
                        Phase  = LogSink.Start
                        Source = Some LogSink.Configuration }
                let sw = System.Diagnostics.Stopwatch.StartNew()
                let task = Compose.runWithConfig cfgForRun
                let result = task.GetAwaiter().GetResult()
                sw.Stop()
                match result with
                | Ok report ->
                    LogSink.recordStage "pipeline" sw.ElapsedMilliseconds LogSink.Succeeded
                    let stagePayload : Map<string, objnull> =
                        Map.ofList [
                            "stage", box "pipeline"
                            "durationMs", box sw.ElapsedMilliseconds
                            "outcome", box "succeeded"
                        ]
                    LogSink.emit
                        { LogSink.envelope LogSink.Info LogSink.Summary "summary.stageCompleted" stagePayload with
                            Phase = LogSink.End
                            StepId = Some "pipeline" }
                    // Chapter C slice C.2 — mirror the production
                    // `runFullExport` path: emit each special-circumstances
                    // diagnostic as a `transform.diagnostic` envelope so
                    // the test harness sees the same stream the CLI does.
                    for entry in report.Diagnostics do
                        let level =
                            match entry.Severity with
                            | DiagnosticSeverity.Info    -> LogSink.Info
                            | DiagnosticSeverity.Warning -> LogSink.Warn
                            | DiagnosticSeverity.Error   -> LogSink.Error
                        let basePayload : Map<string, objnull> =
                            Map.ofList [
                                "source",  box entry.Source
                                "code",    box entry.Code
                                "message", box entry.Message
                            ]
                        let withSsKey =
                            match entry.SsKey with
                            | Some k -> basePayload |> Map.add "ssKey" (box (SsKey.rootOriginal k))
                            | None   -> basePayload
                        let payload =
                            entry.Metadata
                            |> Map.fold (fun acc k v -> Map.add k (box v) acc) withSsKey
                        LogSink.emit
                            { LogSink.envelope level LogSink.Transform "transform.diagnostic" payload with
                                Phase = LogSink.End }
                    report.Paths
                    |> List.iter (fun p ->
                        let info = FileInfo p
                        LogSink.recordArtifact {
                            Kind      = Path.GetFileName p |> nonNull
                            Path      = p
                            SizeBytes = Some info.Length
                            FileCount = None
                        })
                    exitCode <- 0
                | Error errors ->
                    LogSink.recordStage "pipeline" sw.ElapsedMilliseconds LogSink.Failed
                    for e in errors do
                        let payload : Map<string, objnull> =
                            Map.ofList [ "code", box e.Code; "message", box e.Message ]
                        LogSink.emit
                            { LogSink.envelope LogSink.Error LogSink.Transform "transform.diagnostic" payload with
                                Phase = LogSink.ErrorPhase }
                    outcome <- LogSink.Failed
                    exitCode <- 2
        with ex ->
            outcome <- LogSink.Failed
            let payload : Map<string, objnull> =
                Map.ofList [ "exception", box (ex.GetType().Name); "message", box ex.Message ]
            LogSink.emit
                { LogSink.envelope LogSink.Error LogSink.Config "config.validationFailed" payload with
                    Phase = LogSink.ErrorPhase }
            exitCode <- 2
    finally
        let stats = Bench.snapshot ()
        LogSink.runComplete outcome "projection full-export" stats |> ignore
        LogSink.setWriter Console.Error
    exitCode, captured.ToString()

let private safeRm (dir: string) : unit =
    if Directory.Exists dir then
        try Directory.Delete(dir, recursive = true)
        with _ -> ()

// ----------------------------------------------------------------------
// Happy path — full-export against the minimal V1 fixture
// ----------------------------------------------------------------------

[<Fact>]
let ``slice 7: full-export against minimal V1 fixture produces artifacts and emits conforming NDJSON event stream`` () =
    let modelPath = writeTempJson v1MinimalFixture
    let outDir = tempOutputDir ()
    let configPath = writeTempConfig modelPath outDir
    try
        let exitCode, text = runFullExportInProcess configPath None
        Assert.Equal(0, exitCode)
        Assert.True(Directory.Exists outDir, "output directory must exist")
        Assert.True(
            File.Exists (Path.Combine(outDir, Compose.ArtifactPath.json)),
            "projection.json must land at output")
        // NDJSON conformance: every line parses as a JSON object.
        let lines = parseLines text
        Assert.True(List.length lines >= 4, sprintf "expected at least 4 events, got %d" (List.length lines))
        // Mandatory envelope fields on every line.
        for env in lines do
            Assert.NotEqual(JsonValueKind.Undefined, (prop env "runId").ValueKind)
            Assert.NotEqual(JsonValueKind.Undefined, (prop env "ts").ValueKind)
            Assert.NotEqual(JsonValueKind.Undefined, (prop env "level").ValueKind)
            Assert.NotEqual(JsonValueKind.Undefined, (prop env "category").ValueKind)
            Assert.NotEqual(JsonValueKind.Undefined, (prop env "code").ValueKind)
            Assert.NotEqual(JsonValueKind.Undefined, (prop env "phase").ValueKind)
            Assert.NotEqual(JsonValueKind.Undefined, (prop env "payload").ValueKind)
    finally
        safeRm outDir
        File.Delete configPath
        File.Delete modelPath

[<Fact>]
let ``slice 7: first event is config.runStart and last event is summary.runComplete`` () =
    let modelPath = writeTempJson v1MinimalFixture
    let outDir = tempOutputDir ()
    let configPath = writeTempConfig modelPath outDir
    try
        let _, text = runFullExportInProcess configPath None
        let lines = parseLines text
        let first = List.head lines
        let last = List.last lines
        Assert.Equal("config.runStart", (prop first "code") |> getStr)
        Assert.Equal("summary.runComplete", (prop last "code") |> getStr)
        Assert.Equal("end", (prop last "phase") |> getStr)
    finally
        safeRm outDir
        File.Delete configPath
        File.Delete modelPath

[<Fact>]
let ``slice 7: runSummary.payload.outcome = succeeded on the happy path`` () =
    let modelPath = writeTempJson v1MinimalFixture
    let outDir = tempOutputDir ()
    let configPath = writeTempConfig modelPath outDir
    try
        let _, text = runFullExportInProcess configPath None
        let lines = parseLines text
        let last = List.last lines
        let outcome = (prop (prop last "payload") "outcome") |> getStr
        Assert.Equal("succeeded", outcome)
    finally
        safeRm outDir
        File.Delete configPath
        File.Delete modelPath

[<Fact>]
let ``slice 7: runSummary.payload.stages contains the pipeline stage with succeeded outcome`` () =
    let modelPath = writeTempJson v1MinimalFixture
    let outDir = tempOutputDir ()
    let configPath = writeTempConfig modelPath outDir
    try
        let _, text = runFullExportInProcess configPath None
        let lines = parseLines text
        let last = List.last lines
        let stages = prop (prop last "payload") "stages"
        Assert.Equal(JsonValueKind.Array, stages.ValueKind)
        Assert.True(stages.GetArrayLength() >= 1)
        let stageOutcomes =
            [ for i in 0 .. stages.GetArrayLength() - 1 ->
                (prop stages.[i] "stage") |> getStr, (prop stages.[i] "outcome") |> getStr ]
        Assert.Contains(("pipeline", "succeeded"), stageOutcomes)
    finally
        safeRm outDir
        File.Delete configPath
        File.Delete modelPath

[<Fact>]
let ``slice 7: runSummary.payload.artifacts lists every artifact path under Output.Dir`` () =
    let modelPath = writeTempJson v1MinimalFixture
    let outDir = tempOutputDir ()
    let configPath = writeTempConfig modelPath outDir
    try
        let _, text = runFullExportInProcess configPath None
        let lines = parseLines text
        let last = List.last lines
        let artifacts = prop (prop last "payload") "artifacts"
        Assert.True(artifacts.GetArrayLength() >= 1, "artifacts array must be non-empty")
        for i in 0 .. artifacts.GetArrayLength() - 1 do
            let path = (prop artifacts.[i] "path") |> getStr
            Assert.True(File.Exists path, sprintf "artifact path '%s' must exist" path)
    finally
        safeRm outDir
        File.Delete configPath
        File.Delete modelPath

// ----------------------------------------------------------------------
// CLI flag handling
// ----------------------------------------------------------------------

[<Fact>]
let ``slice 7: --output override takes precedence over config Output.Dir`` () =
    let modelPath = writeTempJson v1MinimalFixture
    let configOutDir = tempOutputDir ()
    let overrideOutDir = tempOutputDir ()
    let configPath = writeTempConfig modelPath configOutDir
    try
        let exitCode, _ = runFullExportInProcess configPath (Some overrideOutDir)
        Assert.Equal(0, exitCode)
        Assert.True(Directory.Exists overrideOutDir, "override output directory must exist")
        Assert.False(
            Directory.Exists configOutDir,
            "config-supplied output directory must NOT exist when --output overrides it")
    finally
        safeRm overrideOutDir
        safeRm configOutDir
        File.Delete configPath
        File.Delete modelPath

// ----------------------------------------------------------------------
// Failure-path coverage
// ----------------------------------------------------------------------

[<Fact>]
let ``slice 7: missing config file produces exit 6 + config.validationFailed event + runSummary outcome=failed`` () =
    let missing =
        Path.Combine(Path.GetTempPath(), sprintf "no-such-config-%s.json" (Guid.NewGuid().ToString("N")))
    let exitCode, text = runFullExportInProcess missing None
    Assert.Equal(6, exitCode)
    let lines = parseLines text
    let codes = lines |> List.map (fun e -> (prop e "code") |> getStr)
    Assert.Contains("config.validationFailed", codes)
    let last = List.last lines
    Assert.Equal("summary.runComplete", (prop last "code") |> getStr)
    Assert.Equal("failed", (prop (prop last "payload") "outcome") |> getStr)

[<Fact>]
let ``slice 7: malformed config produces exit 6 + structured config.validationFailed events`` () =
    let badPath = writeTempJson "not valid json {"
    try
        let exitCode, text = runFullExportInProcess badPath None
        Assert.Equal(6, exitCode)
        let lines = parseLines text
        let last = List.last lines
        Assert.Equal("failed", (prop (prop last "payload") "outcome") |> getStr)
    finally
        File.Delete badPath

// ----------------------------------------------------------------------
// Roll-up integration
// ----------------------------------------------------------------------

[<Fact>]
let ``slice 7: runSummary.payload.aggregates includes a summary stage entry`` () =
    let modelPath = writeTempJson v1MinimalFixture
    let outDir = tempOutputDir ()
    let configPath = writeTempConfig modelPath outDir
    try
        let _, text = runFullExportInProcess configPath None
        let lines = parseLines text
        let last = List.last lines
        let aggregates = prop (prop last "payload") "aggregates"
        Assert.Equal(JsonValueKind.Array, aggregates.ValueKind)
        Assert.True(aggregates.GetArrayLength() >= 1)
        // At minimum we should have a summary.stageCompleted entry.
        let codes =
            [ for i in 0 .. aggregates.GetArrayLength() - 1 ->
                (prop aggregates.[i] "code") |> getStr ]
        Assert.Contains("summary.stageCompleted", codes)
    finally
        safeRm outDir
        File.Delete configPath
        File.Delete modelPath

[<Fact>]
let ``slice 7: eventCounts.info >= 3 on happy path (runStart + connectionResolved + stageCompleted at minimum)`` () =
    let modelPath = writeTempJson v1MinimalFixture
    let outDir = tempOutputDir ()
    let configPath = writeTempConfig modelPath outDir
    try
        let _, text = runFullExportInProcess configPath None
        let lines = parseLines text
        let last = List.last lines
        let counts = prop (prop last "payload") "eventCounts"
        let infoCount = (prop counts "info").GetInt32()
        Assert.True(infoCount >= 3, sprintf "expected eventCounts.info >= 3, got %d" infoCount)
        Assert.Equal(0, (prop counts "error").GetInt32())
    finally
        safeRm outDir
        File.Delete configPath
        File.Delete modelPath
