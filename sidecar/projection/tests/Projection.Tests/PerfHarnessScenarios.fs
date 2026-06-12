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
open Projection.Targets.Data
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

// -------------------------------------------------------------------------
// The scenario catalog (§3.2) — curried constructors over production entry
// points. One `PERF-SCENARIO` registry line per scenario feeds
// `perf-harness.sh list`; the single definition site is `all` (below, H7),
// and the pure-pool totality test (`PerfHarnessCatalogTests.fs`) pins
// registry ⇔ `all` so neither surface can drift from the other.
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

// -------------------------------------------------------------------------
// Slice 1 — the seed-MERGE pair (PERF_HARNESS §1 activities 3b/3c; the
// handoff's prime candidate). The EXECUTE scenario is the cliff probe: it
// captures the >1000-row table-value-constructor outcome as EVIDENCE (the
// BEFORE witness for card H2) rather than asserting — post-fix, the same
// scenario's `perf.seedMerge.execute.cliff` label disappears and `.ok`
// appears, which IS the before/after story. Correctness gating for the fix
// lives in H2's equivalence canary, not here.
// -------------------------------------------------------------------------

let private dockerContext (scale: ScaleKnob) : ScenarioContext =
    { Scale = scale
      WithDatabase =
        fun dbName body ->
            task {
                let! _ =
                    Deploy.withBootstrappedDatabase dbName "SELECT 1;" (fun cnn ->
                        task {
                            do! body cnn
                            return 0
                        })
                return ()
            } }

let private seedPolicy : Policy =
    { Policy.empty with
        Emission =
            { Policy.empty.Emission with
                EmitData = true
                DataComposition = AllRemaining } }

/// AC-X1's blessed static-kind shape (MigrationCanaryTests), parameterized
/// by rows/kind (§3.7: production smart constructors + deterministic values).
/// Built on the F6 single-definition-site fixture builder.
let private staticSeedCatalog (rowsPerKind: int) : Catalog =
    StaticCatalogFixtures.staticCatalog "PERF_SEED" "PerfSeedMod" [ "Lookup" ] "PerfLookup" "PerfLookup"
        [ StaticCatalogFixtures.pk "Id" "ID" Integer
          StaticCatalogFixtures.attr "Code" "CODE" Text
          StaticCatalogFixtures.attr "Label" "LABEL" Text ]
        [ for i in 1 .. rowsPerKind ->
            string i, [ string i; sprintf "C%06d" i; sprintf "Perf label %d" i ] ]

/// Activity 3b: the rendered-MERGE TEXT emission at scale, no Docker.
// PERF-SCENARIO: seed-merge-render 1000|10000 | keylabels=emit.staticSeeds.renderMerge,compose.data.composeRendered
let seedMergeRender (rowsPerKind: int) : PerfScenario =
    let catalog = staticSeedCatalog rowsPerKind
    { Name = sprintf "seed-merge-render-%d" rowsPerKind
      Tag = "perf.seedMerge.render"
      KeyLabels = [ "emit.staticSeeds.renderMerge"; "compose.data.composeRendered" ]
      Run =
        fun _ ->
            task {
                match DataEmissionComposer.composeRendered seedPolicy catalog Profile.empty with
                | Error e -> failwithf "seed-merge-render failed: %A" e
                | Ok rendered when rendered.Length = 0 -> failwith "seed-merge-render produced an empty bundle"
                | Ok _ -> ()
            } }

/// Activity 3c: how the emitted Bootstrap MERGE EXECUTES — the cliff probe.
/// Witness-only: a SqlException above the TVC cap is the named BEFORE
/// evidence (printed + recorded as `perf.seedMerge.execute.cliff`); success
/// records `perf.seedMerge.execute.ok` with the loaded row count.
// PERF-SCENARIO: seed-merge-execute 1000|2500|10000 | keylabels=deploy.executeBatch,perf.seedMerge.execute.ok,perf.seedMerge.execute.cliff
let seedMergeExecute (rowsPerKind: int) : PerfScenario =
    let catalog = staticSeedCatalog rowsPerKind
    { Name = sprintf "seed-merge-execute-%d" rowsPerKind
      Tag = "perf.seedMerge.execute"
      KeyLabels = [ "deploy.executeBatch"; "perf.seedMerge.execute.ok"; "perf.seedMerge.execute.cliff" ]
      Run =
        fun ctx ->
            task {
                let ddl = SsdtDdlEmitter.statements catalog |> Render.toText
                match DataEmissionComposer.composeRendered seedPolicy catalog Profile.empty with
                | Error e -> failwithf "seed-merge-execute compose failed: %A" e
                | Ok seed ->
                    do!
                        ctx.WithDatabase (sprintf "PerfSeed%d" rowsPerKind) (fun cnn ->
                            task {
                                do! Deploy.executeBatch cnn ddl
                                try
                                    do! Deploy.executeBatch cnn seed
                                    use check = new SqlCommand("SELECT COUNT(*) FROM [dbo].[PerfLookup];", cnn)
                                    let! loaded = check.ExecuteScalarAsync()
                                    Bench.recordSample "perf.seedMerge.execute.ok" (Convert.ToInt64 loaded)
                                with :? SqlException as ex ->
                                    Bench.recordSample "perf.seedMerge.execute.cliff" (int64 ex.Number)
                                    printfn
                                        "CLIFF WITNESS [rows/kind=%d]: SqlException %d — %s"
                                        rowsPerKind
                                        ex.Number
                                        (ex.Message.Split('\n').[0])
                            })
            } }

// -------------------------------------------------------------------------
// Slice 2 — the ReadSide drain (PERF_HARNESS §1 activity 1b + §3.6 label 1).
// Gates the entire R4/Q row-quantum track (CONSTELLATION_BACKLOG stage 5).
// 12 columns to match the 2026-06-11 standalone prior (~8.8 µs/row at
// 100k×12); the new `readside.rowstream.materialize` aggregated sample
// splits end-to-end into wire (ReadAsync) vs carrier-build.
// -------------------------------------------------------------------------

/// 12-column wide static kind (Id Integer PK + 11 mandatory Text columns),
/// rows loaded via the bulk path (StaticPopulationEmitter → executeStream →
/// SqlBulkCopy folding), then drained back through readRowsStream.
let private wideSeedCatalog (rows: int) : Catalog =
    let colNames = [ for c in 1 .. 11 -> sprintf "C%02d" c ]
    StaticCatalogFixtures.staticCatalog "PERF_WIDE" "PerfWideMod" [ "Wide" ] "PerfWide" "PerfWide"
        (StaticCatalogFixtures.pk "Id" "ID" Integer
         :: [ for c in colNames -> StaticCatalogFixtures.attr c (c.ToUpperInvariant()) Text ])
        [ for i in 1 .. rows ->
            string i, (string i :: [ for c in colNames -> sprintf "%s-%06d" c i ]) ]

/// Activity 1b: deploy + bulk-load the fixture once, then drain
/// `ReadSide.readRowsStream` to EOF — the per-row carrier cost isolated by
/// the §3.6 `materialize` sample against the stream's end-to-end probe.
// PERF-SCENARIO: readside-rowstream 100000 | keylabels=readside.readRowsStream.all,readside.rowstream.materialize
let readsideRowStream (rows: int) : PerfScenario =
    let catalog = wideSeedCatalog rows
    let kind = catalog.Modules.Head.Kinds.Head
    { Name = sprintf "readside-rowstream-%d" rows
      Tag = "perf.readside.rowstream"
      KeyLabels = [ "readside.readRowsStream.all"; "readside.rowstream.materialize" ]
      Run =
        fun ctx ->
            task {
                do!
                    ctx.WithDatabase (sprintf "PerfRead%d" rows) (fun cnn ->
                        task {
                            do!
                                Deploy.executeStream
                                    cnn
                                    (seq {
                                        yield! SsdtDdlEmitter.statements catalog
                                        yield! Projection.Targets.Data.StaticPopulationEmitter.statements catalog
                                    })
                            let stream = Projection.Adapters.Sql.ReadSide.readRowsStream cnn kind
                            let mutable drained = 0
                            let mutable go = true
                            while go do
                                let! next = stream ()
                                match next with
                                | Some _ -> drained <- drained + 1
                                | None -> go <- false
                            if drained <> rows then
                                failwithf "readside-rowstream drained %d rows, expected %d" drained rows
                        })
            } }

// -------------------------------------------------------------------------
// H7 (CONSTELLATION_BACKLOG plane N9; CONSTELLATION §9.8.11) — the catalog
// as the fifth declare-once system. `all` is the single definition site;
// the gated facts below index into it by name (an undeclared name fails
// the fact); `scripts/perf-harness.sh list` reads the `PERF-SCENARIO`
// registry comments; and the pure-pool totality test
// (`PerfHarnessCatalogTests.fs`) pins registry ⇔ `all`, so the comment
// surface cannot drift from the declared one. `Make` is a thunk: fixture
// catalogs (up to 100k rows) are built only once the env gate is open.
// -------------------------------------------------------------------------

type ScenarioDecl =
    { Name : string   // must equal Make().Name — checked at run, pinned by the totality test
      Docker : bool
      Scale : ScaleKnob
      Make : unit -> PerfScenario }

let all : ScenarioDecl list =
    [ { Name = "ssdt-emit-only-150"
        Docker = false
        Scale = { Rows = 0; Tables = 150; ColumnsPerTable = 4 }
        Make = fun () -> ssdtEmitOnly 150 }
      { Name = "seed-merge-render-1000"
        Docker = false
        Scale = { Rows = 1000; Tables = 1; ColumnsPerTable = 3 }
        Make = fun () -> seedMergeRender 1000 }
      { Name = "seed-merge-render-10000"
        Docker = false
        Scale = { Rows = 10000; Tables = 1; ColumnsPerTable = 3 }
        Make = fun () -> seedMergeRender 10000 }
      { Name = "seed-merge-execute-1000"
        Docker = true
        Scale = { Rows = 1000; Tables = 1; ColumnsPerTable = 3 }
        Make = fun () -> seedMergeExecute 1000 }
      { Name = "seed-merge-execute-2500"
        Docker = true
        Scale = { Rows = 2500; Tables = 1; ColumnsPerTable = 3 }
        Make = fun () -> seedMergeExecute 2500 }
      { Name = "seed-merge-execute-10000"
        Docker = true
        Scale = { Rows = 10000; Tables = 1; ColumnsPerTable = 3 }
        Make = fun () -> seedMergeExecute 10000 }
      { Name = "readside-rowstream-100000"
        Docker = true
        Scale = { Rows = 100000; Tables = 1; ColumnsPerTable = 12 }
        Make = fun () -> readsideRowStream 100000 } ]

/// Run one DECLARED scenario: gate first (no fixture construction on the
/// skip path), then build, then verify the declared name against the
/// built one — drift between the two fails loudly, never silently.
let private runDeclared (name: string) : unit =
    match all |> List.tryFind (fun d -> d.Name = name) with
    | None ->
        failwithf
            "PerfHarness fact references undeclared scenario '%s' — declare it in PerfHarnessScenarios.all."
            name
    | Some d ->
        if not (gateOpen ()) then
            printfn "SKIP perf-harness [%s]: set PROJECTION_RUN_PERF_HARNESS=1 to run." name
        elif d.Docker && not (Deploy.Docker.ensureRunning ()) then
            printfn "SKIP perf-harness [%s]: Docker daemon not reachable." name
        else
            let s = d.Make ()
            if s.Name <> d.Name then
                failwithf
                    "scenario decl '%s' built a scenario named '%s' — the declared name must match the built one."
                    d.Name
                    s.Name
            measure ((if d.Docker then dockerContext else pureContext) d.Scale) s

[<Fact>]
let ``PerfHarness: ssdt-emit-only 150`` () = runDeclared "ssdt-emit-only-150"

[<Fact>]
let ``PerfHarness: seed-merge-render 1000`` () = runDeclared "seed-merge-render-1000"

[<Fact>]
let ``PerfHarness: seed-merge-render 10000`` () = runDeclared "seed-merge-render-10000"

[<Fact>]
let ``PerfHarness: seed-merge-execute 1000`` () = runDeclared "seed-merge-execute-1000"

[<Fact>]
let ``PerfHarness: seed-merge-execute 2500`` () = runDeclared "seed-merge-execute-2500"

[<Fact>]
let ``PerfHarness: seed-merge-execute 10000`` () = runDeclared "seed-merge-execute-10000"

[<Fact>]
let ``PerfHarness: readside-rowstream 100000`` () = runDeclared "readside-rowstream-100000"
