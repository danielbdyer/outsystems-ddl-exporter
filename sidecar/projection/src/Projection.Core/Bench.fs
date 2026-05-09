namespace Projection.Core

// LINT-ALLOW-FILE: operator-facing performance instrumentation; the
// `Bench` module's purpose is human-readable bench output and JSON
// persistence for cross-session diff. `sprintf` for label suffixes
// and table-render text is the discipline's allowed exception per
// `DECISIONS 2026-05-09 — Built-in obligation` ("operator-facing
// diagnostic surface; format strings fit"). The JSON path itself
// goes through `Utf8JsonWriter`; this exemption is for the human
// surface only.

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Serialization

/// Lightweight performance instrumentation for the V2 pipeline.
/// Per the session-29 operator framing — the canary's flywheel only
/// promotes optimization if the perf surface is *visible* across
/// runs. Bench provides the visibility:
///
///   1. **Decoration.** Every high-leverage entry point declares
///      `use _ = Bench.scope "label"` at its top. The scope records
///      elapsed time on `Dispose` (RAII / F# `use` idiom). One line
///      per function; pay the cost up front; forget about it.
///
///   2. **Roll-up.** Per-label samples accumulate across the
///      process lifetime. `Bench.snapshot` projects the accumulated
///      samples into per-label `Stats` (Count / Total / Min / Mean
///      / P50 / P95 / P99 / Max).
///
///   3. **Visibility.** The CLI's `emit` / `deploy` / `canary`
///      commands print `Bench.renderTable` at command end. Tests
///      can `Bench.persistJson` to a known path so cross-run
///      comparison is mechanical (a future bench-tracker script
///      diffs adjacent runs and alerts on regression).
///
///   4. **Composability.** Scopes nest naturally — a `runWideCanary`
///      scope encloses two `runEphemeral` scopes which each enclose
///      `useContainer` / `createDatabase` / `executeBatch` /
///      `countUserTables` / `read` scopes. Stats roll up at every
///      level.
///
/// **Thread safety.** The accumulator is lock-protected. Concurrent
/// canary tests record into the same per-label samples lists; the
/// lock granularity is per-record (insertion is O(1)), so contention
/// is negligible relative to the actual work being timed.
///
/// **Sample storage.** All samples are retained in memory for
/// percentile calculation. At V2's scale (canary tests run dozens
/// of operations per process, not millions), the memory cost is
/// trivial. If the corpus grows beyond that — operator running
/// `projection canary` against the 300-table fixture corpus, say —
/// promote to HdrHistogram or sparse-percentile sampling.
[<RequireQualifiedAccess>]
module Bench =

    /// Per-label rolling statistics. Sorted by `TotalMs` descending
    /// in `Bench.snapshot` so the most expensive operations surface
    /// at the top of the table.
    type Stats =
        {
            Label : string
            Count : int
            TotalMs : int64
            MinMs : int64
            MaxMs : int64
            MeanMs : float
            P50Ms : int64
            P95Ms : int64
            P99Ms : int64
        }

    let private storage : Dictionary<string, ResizeArray<int64>> = Dictionary()
    let private lockObj : obj = obj ()

    let private record (label: string) (elapsedMs: int64) : unit =
        lock lockObj (fun () ->
            match storage.TryGetValue label with
            | true, samples -> samples.Add elapsedMs
            | false, _ ->
                let samples = ResizeArray<int64>()
                samples.Add elapsedMs
                storage[label] <- samples)

    /// RAII timing scope. Use as the first line of any function /
    /// task you want timed:
    ///
    ///     let runEphemeral (sql: string) : Task<Report> = task {
    ///         use _ = Bench.scope "deploy.runEphemeral"
    ///         // ... existing body ...
    ///     }
    ///
    /// On scope exit (`Dispose`), the elapsed time records into the
    /// per-label sample list. F# `use` and `task { ... use ... }`
    /// both honor the dispose contract.
    ///
    /// Nested scopes work (`runWideCanary` containing `runEphemeral`
    /// containing `executeBatch`); each label rolls up independently.
    let scope (label: string) : IDisposable =
        let sw = Stopwatch.StartNew()
        { new IDisposable with
            member _.Dispose() =
                sw.Stop()
                record label sw.ElapsedMilliseconds }

    // -----------------------------------------------------------------
    // Iterator combinators — record one sample *per element* so per-
    // iteration distribution surfaces in stats. Per session-30 operator
    // framing: "stats that roll up with iterations of cycles of events
    // (anything done in a loop) tracked at intervals with statistical
    // analysis."
    //
    // Semantically equivalent to the explicit `for x in xs do`
    // form-with-`use` decoration, written in point-free shape so callers
    // can apply via `|>` and forget about the timing surface.
    //
    // After decoration, snapshots show per-iteration distribution:
    //
    //     | emit.rawText.kind                |   300 |  450 |   1 | 1.5  |   1 |   3 |  12 |  18 |
    //     | emit.rawText.attribute           |  9000 |  720 |   0 | 0.08 |   0 |   1 |   2 |   8 |
    //
    // Count = #iterations, Mean = per-iteration mean (ms), P95/P99
    // surface slow-tail iterations. The fast-path overhead is one
    // Stopwatch + one dictionary entry per iteration (~µs); negligible
    // at V2's scale (300-table catalog ≈ 10K iterations across all
    // emitter loops).
    // -----------------------------------------------------------------

    /// Time each iteration of a side-effecting body. Use as:
    ///
    ///     catalog.Modules
    ///     |> Bench.iterDo "emit.rawText.module" (renderModule sb catalog)
    let iterDo (label: string) (f: 'a -> unit) (xs: seq<'a>) : unit =
        for x in xs do
            use _ = scope label
            f x

    /// Indexed iter — forwards the iteration index to the body.
    /// Useful for loops whose body branches on first / last element
    /// (e.g., comma-separator logic inside `renderTable`).
    let iteriDo (label: string) (f: int -> 'a -> unit) (xs: 'a list) : unit =
        xs
        |> List.iteri (fun i x ->
            use _ = scope label
            f i x)

    /// Time each iteration of a transformation. Stats per element;
    /// returns the projected list. Drop-in replacement for `List.map`.
    let iterMap (label: string) (f: 'a -> 'b) (xs: 'a list) : 'b list =
        xs
        |> List.map (fun x ->
            use _ = scope label
            f x)

    // -----------------------------------------------------------------
    // Stream-flavored probes — first-class observability for lazy
    // sequences. Per session-34 — Π's canonical output is a
    // `seq<Statement>` and ReadSide's row source is a streaming
    // pipeline; tracking *throughput* (elements / sec) and *backpressure*
    // (per-element transit time) is as load-bearing as scoping
    // synchronous bodies.
    //
    // Two complementary metrics, recorded under separate labels so the
    // bench table shows both:
    //
    //     | <label>          | total elapsed (ms)       — wall time across the full enumeration
    //     | <label>.elements | total element count      — enumeration size at completion
    //
    // Throughput is `elements / (elapsed / 1000)`. The renderer treats
    // both as ordinary samples; operators reading the table compute
    // throughput by inspection. Future bench-tracker scripts can
    // structurally lift the pair into a `rows/sec` column.
    //
    // **Pass-through.** `streamProbe` neither buffers nor materializes;
    // it taps the lazy enumeration and yields each element through.
    // T1 byte-determinism is preserved because element order is
    // unchanged.
    // -----------------------------------------------------------------

    let private recordCount (label: string) (count: int64) : unit =
        record (sprintf "%s.elements" label) count

    /// Wrap a synchronous sequence with throughput instrumentation.
    /// Records `<label>` (wall-time in ms across the full enumeration)
    /// and `<label>.elements` (element count) on completion.
    /// Pass-through; doesn't buffer.
    ///
    /// Use as:
    ///
    ///     statements catalog
    ///     |> Bench.streamProbe "emit.statements"
    ///     |> Render.toText
    ///
    /// Composes naturally with `|>`. Multiple probes on a chain stack
    /// without re-materializing.
    let streamProbe (label: string) (xs: seq<'a>) : seq<'a> =
        seq {
            let sw = Stopwatch.StartNew()
            let mutable count = 0L
            for x in xs do
                count <- count + 1L
                yield x
            sw.Stop()
            record label sw.ElapsedMilliseconds
            recordCount label count
        }

    /// Time the body executed per element of a sequence, including
    /// the upstream production transit (wall-clock between yields).
    /// Use when backpressure analysis matters; for total throughput
    /// `streamProbe` is cheaper.
    let streamTransit (label: string) (xs: seq<'a>) : seq<'a> =
        seq {
            let mutable previous = Stopwatch.GetTimestamp()
            for x in xs do
                let now = Stopwatch.GetTimestamp()
                let ticks = now - previous
                let ms = ticks * 1000L / Stopwatch.Frequency
                record label ms
                previous <- now
                yield x
        }

    /// Surface an external counter to bench. Use for metrics derived
    /// outside the scope/iter/stream-probe surface (e.g., bytes
    /// emitted, rows bulk-copied) that operators want in the same
    /// rollup table.
    let recordSample (label: string) (sample: int64) : unit =
        record label sample

    let private percentile (sorted: int64 array) (p: float) : int64 =
        if sorted.Length = 0 then
            0L
        else
            let raw = float (sorted.Length - 1) * p / 100.0
            let idx = int (Math.Round raw)
            sorted[idx |> max 0 |> min (sorted.Length - 1)]

    /// Snapshot all accumulated stats, sorted by `TotalMs`
    /// descending. Safe to call concurrently with `scope` recordings;
    /// the snapshot copies samples under the lock so subsequent
    /// records don't race.
    let snapshot () : Stats list =
        lock lockObj (fun () ->
            storage
            |> Seq.map (fun kv ->
                let samples = kv.Value.ToArray()
                let sorted = samples |> Array.sort
                let count = samples.Length
                let total = if count > 0 then Array.sum samples else 0L
                let mean = if count > 0 then float total / float count else 0.0
                let minV = if count > 0 then sorted[0] else 0L
                let maxV = if count > 0 then sorted[count - 1] else 0L
                {
                    Label = kv.Key
                    Count = count
                    TotalMs = total
                    MinMs = minV
                    MaxMs = maxV
                    MeanMs = mean
                    P50Ms = percentile sorted 50.0
                    P95Ms = percentile sorted 95.0
                    P99Ms = percentile sorted 99.0
                })
            |> Seq.sortByDescending (fun s -> s.TotalMs)
            |> List.ofSeq)

    /// Reset the accumulator. Useful between independent test
    /// scenarios when tests want isolated stats. The CLI does NOT
    /// reset — it accumulates across the process lifetime so
    /// repeated invocations compound.
    let reset () : unit =
        lock lockObj (fun () -> storage.Clear())

    /// Render a snapshot as a markdown-style table for human
    /// readers. Sorted by `TotalMs` descending; expensive operations
    /// at the top.
    ///
    /// Sample output:
    ///
    ///     | Label                    | Count | Total | Min | Mean   | P50 | P95 | P99 | Max |
    ///     |--------------------------|-------|-------|-----|--------|-----|-----|-----|-----|
    ///     | deploy.useContainer      |     3 | 22107 | 7080| 7369.0 | 7350| 7821| 7821| 7821|
    ///     | deploy.runEphemeral      |     3 | 21850 | 6900| 7283.3 | 7290| 7600| 7600| 7600|
    ///     | readside.read            |     2 |   430 | 200|  215.0 | 230| 230| 230| 230|
    ///     ...
    let renderTable (stats: Stats list) : string =
        if List.isEmpty stats then
            "(bench: no samples recorded)"
        else
            let labelWidth =
                stats
                |> List.map (fun s -> s.Label.Length)
                |> List.max
                |> max 5
            let header =
                sprintf
                    "| %s | Count | Total (ms) | Min | Mean | P50 | P95 | P99 | Max |"
                    ("Label".PadRight labelWidth)
            let separator =
                sprintf
                    "|%s|-------|-----------|-----|------|-----|-----|-----|-----|"
                    (String.replicate (labelWidth + 2) "-")
            let row (s: Stats) =
                sprintf
                    "| %s | %5d | %9d | %3d | %5.1f | %3d | %3d | %3d | %3d |"
                    (s.Label.PadRight labelWidth)
                    s.Count
                    s.TotalMs
                    s.MinMs
                    s.MeanMs
                    s.P50Ms
                    s.P95Ms
                    s.P99Ms
                    s.MaxMs
            let lines = header :: separator :: (stats |> List.map row)
            String.concat "\n" lines

    /// JSON-persistable snapshot envelope. Adds a wall-clock
    /// timestamp + tag so cross-run analysis can sequence them and
    /// segregate by scenario (e.g., `tag = "wide-canary-enterprise"`).
    type Run =
        {
            CapturedAtUtc : DateTime
            Tag : string
            Stats : Stats list
        }

    let private jsonOptions : JsonSerializerOptions =
        let o = JsonSerializerOptions(WriteIndented = true)
        o.Converters.Add(JsonStringEnumConverter())
        o

    /// Persist the current snapshot as JSON at `path`. Creates the
    /// containing directory if needed. Used by the CLI to drop a
    /// per-run JSON into a known path so a future bench-tracker
    /// script can diff adjacent runs and alert on regression.
    ///
    /// Path convention (per session-29 operator framing):
    ///
    ///     bench/<tag>/<utc-iso>.json
    ///
    /// Example: `bench/wide-canary-enterprise/20260509T040305Z.json`.
    /// The bench/ directory at the repo root accumulates runs; the
    /// flywheel reviewer compares the most recent N runs.
    let persistJson (path: string) (tag: string) (stats: Stats list) : unit =
        let run =
            {
                CapturedAtUtc = DateTime.UtcNow
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
        let timestamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ")
        Path.Combine(rootDir, "bench", tag, sprintf "%s.json" timestamp)
