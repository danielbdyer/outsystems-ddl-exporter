namespace Projection.Pipeline

open System
open System.IO
open System.Text.Json
open System.Text.Json.Serialization
open Projection.Core

/// File-system sink for `Bench.Run` snapshots — chapter-3.6 cash-out
/// of audit Tier-1 #1 (`Bench.persistJson` writes from Core,
/// violating the no-I/O-in-Core discipline). The typed `Run`
/// envelope and `Stats` records stay in `Projection.Core.Bench`
/// (pure values; no I/O); the file write lives here at the
/// boundary, where the Pipeline layer is the home for adapters
/// that emit artifacts.
///
/// The CLI is the primary consumer (every `emit` / `deploy` /
/// `canary` command persists a snapshot). Tests use the same
/// surface.
[<RequireQualifiedAccess>]
module BenchSink =

    let private jsonOptions : JsonSerializerOptions =
        let o = JsonSerializerOptions(WriteIndented = true)
        o.Converters.Add(JsonStringEnumConverter())
        o

    /// Persist a snapshot as JSON at `path`. Creates the containing
    /// directory if needed. Used by the CLI to drop a per-run JSON
    /// into a known path so a future bench-tracker script can diff
    /// adjacent runs and alert on regression.
    ///
    /// Path convention (per session-29 operator framing):
    ///
    ///     bench/<tag>/<utc-iso>.json
    ///
    /// Example: `bench/wide-canary-enterprise/20260509T040305Z.json`.
    let persistJson (path: string) (tag: string) (stats: Bench.Stats list) : unit =
        let run : Bench.Run =
            {
                // BenchSink IS the reified non-determinism boundary
                // for bench persistence — wall-clock capture happens
                // here, at the file-write boundary, not in Core
                // (per the supreme operating discipline + audit
                // Tier-1 #1 cash-out).
                CapturedAtUtc = DateTime.UtcNow  // LINT-ALLOW: reified non-determinism boundary at file-sink layer
                Tag = tag
                Stats = stats
            }
        match Path.GetDirectoryName path with
        | null -> ()
        | dir when System.String.IsNullOrEmpty dir -> ()
        | dir -> Directory.CreateDirectory dir |> ignore
        let json = JsonSerializer.Serialize(run, jsonOptions)
        File.WriteAllText(path, json)

    /// Compose a default `bench/<tag>/<utc-iso>.json` path under
    /// `rootDir`. The UTC timestamp in `yyyyMMddTHHmmssZ` form keeps
    /// filenames sortable. Used by the CLI to choose where to drop
    /// each run's JSON.
    let defaultPath (rootDir: string) (tag: string) : string =
        let timestamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ")  // LINT-ALLOW: reified non-determinism boundary at file-sink layer
        Path.Combine(rootDir, "bench", tag, sprintf "%s.json" timestamp)
