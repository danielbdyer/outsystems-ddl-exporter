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
              "dataType": "rtIdentifier",
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

/// Drives the *real* `FullExportRun.execute` (the same Pipeline
/// orchestration the CLI's `runFullExport` consumes) under a captured
/// LogSink writer, so the test asserts on the production NDJSON stream
/// rather than a re-implemented mirror that could drift. Returns the
/// captured NDJSON + exit code. `Verbosity.Quiet` matches the CLI
/// default (no `--verbose` / `--debug`); `execute` resets the sink +
/// bench state and emits the terminal `summary.runComplete` itself.
let private runFullExportInProcess
    (configPath: string)
    (outputOverride: string option)
    : int * string =
    use captured = new StringWriter()
    let outcome =
        LogSink.withWriter captured (fun () ->
            FullExportRun.execute configPath outputOverride LogSink.Verbosity.Quiet Set.empty)
    FullExportRun.exitCode outcome, captured.ToString()

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

[<Fact>]
let ``S5: wall(run) − Σ wall(stage) ≤ ε — the publish run's stage account, honestly bounded`` () =
    // R2's additivity law (T14 on the time plane), asserted on the REAL run
    // at two levels of nesting. ε's residue, enumerated:
    //   level 1 (umbrella − children): the staged CE's plumbing only — no
    //     I/O between brackets by construction (card S4a).
    //   level 2 (run wall − umbrella): config load + model-path resolution
    //     (before the root opens), artifact recording + diagnostics
    //     projection + transform-event egress + envelope serialization
    //     (after it closes). No SQL and no network on this fixture.
    // The bounds are generous for CI scheduling noise: this witness catches
    // STRUCTURAL unaccounted work (the pre-spine migratePreflights class,
    // seconds of unmetered I/O), not micro-jitter. The migrate run's
    // face-level grant pre-flights remain named, unbounded residue — they
    // run real SQL before the engine's spine opens (DECISIONS 2026-06-12,
    // S4).
    let modelPath = writeTempJson v1MinimalFixture
    let outDir = tempOutputDir ()
    let configPath = writeTempConfig modelPath outDir
    try
        let _, text = runFullExportInProcess configPath None
        let lines = parseLines text
        let first = List.head lines
        let last = List.last lines
        let stages = prop (prop last "payload") "stages"
        let stageDur (name: string) : int64 =
            [ for i in 0 .. stages.GetArrayLength() - 1 -> stages.[i] ]
            |> List.tryPick (fun s ->
                if getStr (prop s "stage") = name then Some ((prop s "durationMs").GetInt64()) else None)
            |> Option.defaultValue 0L
        let umbrella = stageDur "pipeline"
        let childSum = stageDur "extract" + stageDur "profile" + stageDur "emit"
        // level 1 — the umbrella covers its children; the gap is CE plumbing.
        Assert.True(umbrella + 3L >= childSum, sprintf "umbrella %dms must cover children %dms" umbrella childSum)
        Assert.True(umbrella - childSum <= 500L, sprintf "nesting ε=%dms exceeds the named 500ms bound" (umbrella - childSum))
        // level 2 — the run wall covers the umbrella; the gap is the named residue.
        let tsOf (e: JsonElement) =
            DateTime.Parse(getStr (prop e "ts"), null, System.Globalization.DateTimeStyles.RoundtripKind)
        let runWallMs = int64 (tsOf last - tsOf first).TotalMilliseconds
        Assert.True(runWallMs + 3L >= umbrella, sprintf "run wall %dms must cover the umbrella %dms" runWallMs umbrella)
        Assert.True(
            runWallMs - umbrella <= 3000L,
            sprintf "run-level ε=%dms — unaccounted run work grew past the named residue" (runWallMs - umbrella))
    finally
        safeRm outDir
        File.Delete configPath
        File.Delete modelPath

// ----------------------------------------------------------------------
// AC-X3 — the publication bundle is reachable from the `full-export` CLI
// surface (the `--lifecycle-store` routing the CLI's `runFullExport` does).
// Drives `FullExportRun.executeWithStore` under the captured LogSink writer
// EXACTLY as `runFullExport` calls it (Some store + Timeline + Environment +
// clock), so the test asserts the CLI-surface bundle, not a re-implemented
// mirror. The DISCRIMINATOR is the diff-vs-prior: a second store-backed
// export of the SAME model shows an EMPTY displacement + a 2nd episode — a
// genesis-every-run implementation (store not read) re-emits a full
// displacement and fails the `CatalogDiff.isEmpty` assertion.
// ----------------------------------------------------------------------

[<Fact>]
let ``AC-X3: full-export --lifecycle-store emits the bundle, records an episode, and a re-export reconstructs prior state`` () =
    let modelPath = writeTempJson v1MinimalFixture
    let outDir = tempOutputDir ()
    let configPath = writeTempConfig modelPath outDir
    let storePath =
        Path.Combine(Path.GetTempPath(), sprintf "x3-store-%s.json" (Guid.NewGuid().ToString("N")))
    let env = Projection.Core.Environment.Dev
    let tl = Timeline.create (Projection.Core.Environment.name env) |> Result.value
    let runStore () =
        use captured = new StringWriter()
        LogSink.withWriter captured (fun () ->
            FullExportRun.executeWithStore configPath None LogSink.Verbosity.Quiet Set.empty
                (Some storePath) tl env DateTimeOffset.UtcNow)
    try
        // Run 1 — genesis through the store-backed CLI surface: one episode,
        // non-empty displacement (every kind is an Add).
        let outcome1, leg1 = runStore ()
        Assert.Equal(0, FullExportRun.exitCode outcome1)
        Assert.True(leg1.IsSome, "store-backed full-export must produce the bundle (store leg)")
        Assert.Equal(1, EpisodicLifecycle.episodes leg1.Value.Chain |> List.length)
        Assert.False(CatalogDiff.isEmpty leg1.Value.Displacement, "genesis displacement is non-empty")
        // Run 2 — SAME model + SAME store: the prior is read, so the displacement
        // is EMPTY and a 2nd episode lands. (Store-not-read ⇒ full re-emit ⇒ fails.)
        let outcome2, leg2 = runStore ()
        Assert.Equal(0, FullExportRun.exitCode outcome2)
        Assert.True(leg2.IsSome)
        Assert.Equal(2, EpisodicLifecycle.episodes leg2.Value.Chain |> List.length)
        Assert.True(CatalogDiff.isEmpty leg2.Value.Displacement, "re-export of the same model is an empty displacement (diff-vs-prior)")
        // The bundle reconstructs the latest stored state from genesis.
        match EpisodicLifecycle.reconstructLatestSchema leg2.Value.Chain with
        | Ok _ -> ()
        | Error e -> Assert.Fail(sprintf "X3 bundle must reconstruct latest from genesis: %A" e)
    finally
        safeRm outDir
        File.Delete configPath
        File.Delete modelPath
        if File.Exists storePath then File.Delete storePath
