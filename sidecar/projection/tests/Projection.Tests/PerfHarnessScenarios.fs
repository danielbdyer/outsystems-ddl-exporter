/// PERF_HARNESS.md §3 — the before/after measurement fleet (slice 0: the
/// spine + the one Docker-free scenario, `ssdt-emit-only`).
///
/// The workload IS the production function (§3.2 — the "functional curry"):
/// each `PerfScenario.Run` closes over the real production entry point with
/// the scale knob partially applied; the Bench labels live inside the
/// callees, already curried in. Artifacts land at
/// `bench/perf/<name>/<utc>.json` — deliberately NOT `bench/canary/` (§3.3),
/// so the perf-gate's snapshot discovery never picks up harness runs as
/// canary evidence. The fleet is gated behind `PROJECTION_RUN_PERF_HARNESS=1`
/// (the `PROJECTION_RUN_BULK_CANARY` idiom) so `test.sh docker` / CI never
/// pays it by default. Operator/agent entry point: `scripts/perf-harness.sh`.
[<Xunit.Collection("Docker-SqlServer")>]
module Projection.Tests.PerfHarnessScenarios

open System
open System.Threading.Tasks
open Xunit
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Pipeline
open Projection.Targets.SSDT
// ReverseLegScaleFixtures lives in `namespace Projection.Tests` (ReverseLegScaleTests.fs) —
// same namespace as this module; no open needed.

/// What every scenario receives (§3.2). Docker-backed scenarios (slice 1+)
/// receive a `WithDatabase` wired to the warm-honoring ephemeral fixture;
/// pure scenarios never call it.
type ScaleKnob =
    { Rows : int
      Tables : int
      ColumnsPerTable : int }

type ScenarioContext =
    { WithDatabase : string -> (SqlConnection -> Task<unit>) -> Task<unit>
      Scale : ScaleKnob }

/// One measurable activity (§3.2). `Run` closes over the REAL production
/// entry point; `KeyLabels` are what `perf-harness.sh diff` keys on.
type PerfScenario =
    { Name : string
      Tag : string
      KeyLabels : string list
      Run : ScenarioContext -> Task<unit> }

let private gateOpen () : bool =
    match Environment.GetEnvironmentVariable "PROJECTION_RUN_PERF_HARNESS" with
    | "1" -> true
    | _ -> false

let private benchRoot () : string =
    match Environment.GetEnvironmentVariable "PROJECTION_BENCH_DIR" with
    | null
    | "" -> Environment.CurrentDirectory
    | v -> v

/// Reset → run the real path → snapshot → persist under the scenario's tag
/// (§3.3). Scenarios run serially (the Docker collection guarantees it), so
/// per-scenario rollups are clean. Prints the reverse-leg-idiom top-N table.
let private measure (ctx: ScenarioContext) (s: PerfScenario) : unit =
    Bench.reset ()
    TaskSync.runUnit (fun () -> s.Run ctx :> Task)
    let stats = Bench.snapshot ()
    let dir = IO.Path.Combine(benchRoot (), "bench", "perf", s.Name)
    IO.Directory.CreateDirectory dir |> ignore
    // LINT-ALLOW: reified non-determinism boundary at the file-sink layer
    // (the BenchSink.defaultPath precedent; same timestamp shape).
    let stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'")
    let path = IO.Path.Combine(dir, stamp + ".json")
    BenchSink.persistJson path s.Tag stats
    printfn "\nPerfHarness [%s] -> %s" s.Name path
    printfn "Bench (top 25):\n%s" (Bench.renderTable (stats |> List.truncate 25))

/// The pure-pool face of the context: a scenario that asks for a database in
/// this context is a wiring bug, refused by name (Docker-backed scenarios
/// arrive with slice 1+ and wire the warm-honoring fixture here).
let private pureContext (scale: ScaleKnob) : ScenarioContext =
    { Scale = scale
      WithDatabase =
        fun dbName _ ->
            failwithf
                "PerfScenario requested database '%s' in the pure context — Docker-backed scenarios land with PERF_HARNESS §4 slice 1+."
                dbName }

let private runGated (label: string) (scale: ScaleKnob) (scenario: PerfScenario) : unit =
    if not (gateOpen ()) then
        printfn "SKIP perf-harness [%s]: set PROJECTION_RUN_PERF_HARNESS=1 to run." label
    else
        measure (pureContext scale) scenario

// -------------------------------------------------------------------------
// The scenario catalog (§3.2) — curried constructors over production entry
// points. One `// PERF-SCENARIO:` registry line per scenario keeps
// `perf-harness.sh list` honest without a second enumeration surface.
// -------------------------------------------------------------------------

/// Stage-2a emit-only (PERF_HARNESS §1 activity 2a): the full
/// `SsdtDdlEmitter.statements |> Render.toText` path, deploy-free. Exists to
/// PROVE schema emit stays cheap as the estate scales — not because it is
/// suspected hot. Catalog via the `meshModel` idiom (§3.7): seed-free,
/// deterministic, production smart constructors.
// PERF-SCENARIO: ssdt-emit-only 150 | keylabels=render.toText.stream,render.statement
let ssdtEmitOnly (tables: int) : PerfScenario =
    let catalog = ReverseLegScaleFixtures.meshModel tables
    { Name = sprintf "ssdt-emit-only-%d" tables
      Tag = "perf.ssdt.emitOnly"
      KeyLabels = [ "render.toText.stream"; "render.statement" ]
      Run =
        fun _ ->
            task {
                let text = SsdtDdlEmitter.statements catalog |> Render.toText
                if text.Length = 0 then
                    failwith "ssdt-emit-only produced empty DDL"
            } }

[<Fact>]
let ``PerfHarness: ssdt-emit-only 150`` () =
    runGated
        "ssdt-emit-only 150"
        { Rows = 0; Tables = 150; ColumnsPerTable = 4 }
        (ssdtEmitOnly 150)
