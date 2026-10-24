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
// Slice 3 (H4) — the executeStream batch sweep (PERF_HARNESS §4 slice 3 /
// §1 activity 4a): the same controlled InsertRow stream realized at
// different bulk batch sizes through `Deploy.executeStreamWith` (the
// batch-size sibling the sweep fired). rows/sec per batch size answers
// whether the 5000 default (session-35 bench) still holds.
// -------------------------------------------------------------------------

/// Activity 4a: deploy the schema, then realize the static-population
/// InsertRow stream at an explicit bulk batch size; COUNT(*)-verified.
// PERF-SCENARIO: execute-stream-batch 100000x1000|100000x5000|100000x10000 | keylabels=deploy.executeStream,deploy.bulk.copyRows,deploy.bulk.copyRows.batchSize
// (The sweep's first run priced batchSize=20000 out: its bulk-insert
// memory grant — 539 MB requested, 607 ideal — EXCEEDS the big-query
// resource semaphore (~492 MB) on the 4 GiB warm container and stalls
// on RESOURCE_SEMAPHORE indefinitely. 20000 is not "slower", it is
// grant-infeasible at this container size; the top of the sweep is
// 10000. PERF_HARNESS §5 records the finding.)
let executeStreamBatch (rows: int) (batchSize: int) : PerfScenario =
    let catalog = wideSeedCatalog rows
    { Name = sprintf "execute-stream-batch-%dx%d" rows batchSize
      Tag = "perf.deploy.executeStreamBatch"
      KeyLabels = [ "deploy.executeStream"; "deploy.bulk.copyRows"; "deploy.bulk.copyRows.batchSize" ]
      Run =
        fun ctx ->
            task {
                do!
                    ctx.WithDatabase (sprintf "PerfBatch%d" batchSize) (fun cnn ->
                        task {
                            do! Deploy.executeBatch cnn (SsdtDdlEmitter.statements catalog |> Render.toText)
                            do! Deploy.executeStreamWith batchSize cnn (StaticPopulationEmitter.statements catalog)
                            use check = new SqlCommand("SELECT COUNT(*) FROM [dbo].[PerfWide];", cnn)
                            let! loaded = check.ExecuteScalarAsync()
                            if Convert.ToInt32 loaded <> rows then
                                failwithf "execute-stream-batch loaded %A rows, expected %d" loaded rows
                        })
            } }

// -------------------------------------------------------------------------
// Slice 4 (H5) — the drains: the pure static-population emit (3a), the
// pure PhysicalSchema verify block (X1's cross-stage cost), and the
// profiler discovery leg (1a — the "already optimized, leave it" prior,
// finally measured in isolation).
// -------------------------------------------------------------------------

/// Activity 3a: drain `StaticPopulationEmitter.statements` to a sink
/// WITHOUT deploying — the streamProbe label finally measures pure emit
/// cost (in the canary its wall-time includes the consumer's SQL
/// round-trips between pulls).
// PERF-SCENARIO: static-population-drain 100000 | keylabels=emit.staticPopulation.statements.stream
let staticPopulationDrain (rows: int) : PerfScenario =
    let catalog = wideSeedCatalog rows
    { Name = sprintf "static-population-drain-%d" rows
      Tag = "perf.emit.staticPopulationDrain"
      KeyLabels = [ "emit.staticPopulation.statements.stream" ]
      Run =
        fun _ ->
            task {
                let mutable n = 0
                for _ in StaticPopulationEmitter.statements catalog do
                    n <- n + 1
                if n < rows then
                    failwithf "static-population-drain drained %d statements for %d rows" n rows
            } }

/// Cross-stage X1: the canary's verify block in isolation — ofCatalog
/// (with per-row hashing) twice + diff over identical in-memory rows.
/// No Docker; the ~14 s bulk100k prior becomes attributable.
// PERF-SCENARIO: physical-schema-verify 100000 | keylabels=physicalSchema.ofCatalog,physicalSchema.diff,physicalSchema.rows.hash
let physicalSchemaVerify (rows: int) : PerfScenario =
    let catalog = wideSeedCatalog rows
    { Name = sprintf "physical-schema-verify-%d" rows
      Tag = "perf.physicalSchema.verify"
      KeyLabels = [ "physicalSchema.ofCatalog"; "physicalSchema.diff"; "physicalSchema.rows.hash" ]
      Run =
        fun _ ->
            task {
                let source = PhysicalSchema.ofCatalog catalog
                let target = PhysicalSchema.ofCatalog catalog
                let d = PhysicalSchema.diff source target
                if not (PhysicalSchema.isEqual d) then
                    failwith "physical-schema-verify: identical catalogs diffed unequal"
            } }

/// Activity 1a: the profiler's discovery leg over a pre-deployed
/// FK-mesh estate (table-count scaling — the EvidenceCache
/// discover-once cost, chapter-B.3's optimization target). The catalog
/// is `meshModel` (no Static marks, so every kind is profiled — the 4.4
/// trap does not bite); rows/table is zero by design: this scenario
/// isolates DISCOVERY round-trips, the axis the prior speaks to (the
/// row-bearing read path has its own scenarios).
// PERF-SCENARIO: profiler-discover 150 | keylabels=profile.live.captureEvidenceCache,profile.live.discoverKind,profile.cache.deriveColumnProfiles
let profilerDiscover (tables: int) : PerfScenario =
    let catalog = ReverseLegScaleFixtures.meshModel tables
    { Name = sprintf "profiler-discover-%d" tables
      Tag = "perf.profile.discover"
      KeyLabels = [ "profile.live.captureEvidenceCache"; "profile.live.discoverKind"; "profile.cache.deriveColumnProfiles" ]
      Run =
        fun ctx ->
            task {
                do!
                    ctx.WithDatabase (sprintf "PerfProf%d" tables) (fun cnn ->
                        task {
                            do! Deploy.executeStream cnn (SsdtDdlEmitter.statements catalog)
                            match! Projection.Adapters.Sql.LiveProfiler.attach cnn catalog Profile.empty with
                            | Error es -> failwithf "profiler-discover failed: %A" es
                            | Ok _ -> ()
                        })
            } }

// -------------------------------------------------------------------------
// Slice 5 (H6) — the OSSYS JSON parse at estate scale (PERF_HARNESS §1
// activity 1d). The V1-shaped envelope is synthesized via JsonNode
// (typed-AST-first; no string assembly) at N entities × 8 attributes
// × 1 index; `OssysJsonReader.parseJsonString` is the production entry.
// -------------------------------------------------------------------------

/// Deterministic v1-envelope synthesis. GUID-shaped ssKeys derive from
/// the entity/attribute ordinals, so the JSON is byte-stable per scale.
let private ossysEnvelopeJson (entities: int) : string =
    let guidOf (kind: int) (i: int) (j: int) =
        sprintf "%08d-%04d-4%03d-8%03d-%012d" kind (i % 10000) (i % 1000) (j % 1000) (i * 100 + j)
    let node = System.Text.Json.Nodes.JsonObject()
    let modules = System.Text.Json.Nodes.JsonArray()
    let perModule = 100
    let moduleCount = max 1 ((entities + perModule - 1) / perModule)
    let mutable entityIx = 0
    for m in 1 .. moduleCount do
        let entitiesArr = System.Text.Json.Nodes.JsonArray()
        let inModule = min perModule (entities - entityIx)
        for e in 1 .. inModule do
            entityIx <- entityIx + 1
            let i = entityIx
            let attrs = System.Text.Json.Nodes.JsonArray()
            let attrSpec =
                ("Id", "ID", "Identifier", true, true)
                :: [ for c in 1 .. 7 -> (sprintf "C%02d" c, sprintf "C%02d" c, "Text", false, false) ]
            attrSpec
            |> List.iteri (fun j (name, phys, dt, isId, isAuto) ->
                let a = System.Text.Json.Nodes.JsonObject()
                a["ssKey"] <- guidOf 2 i j
                a["name"] <- name
                a["physicalName"] <- phys
                a["dataType"] <- dt
                a["isMandatory"] <- isId
                a["isIdentifier"] <- isId
                a["isAutoNumber"] <- isAuto
                a["isReference"] <- 0
                attrs.Add a)
            let idx = System.Text.Json.Nodes.JsonObject()
            idx["name"] <- sprintf "IX_E%06d" i
            idx["isPrimary"] <- true
            idx["isUnique"] <- true
            let idxCols = System.Text.Json.Nodes.JsonArray()
            let col = System.Text.Json.Nodes.JsonObject()
            col["attribute"] <- "Id"
            col["ordinal"] <- 1
            idxCols.Add col
            idx["columns"] <- idxCols
            let indexes = System.Text.Json.Nodes.JsonArray()
            indexes.Add idx
            let ent = System.Text.Json.Nodes.JsonObject()
            ent["ssKey"] <- guidOf 1 i 0
            ent["name"] <- sprintf "Entity%06d" i
            ent["physicalName"] <- sprintf "OSUSR_P_E%06d" i
            ent["db_schema"] <- "dbo"
            ent["isExternal"] <- false
            ent["isStatic"] <- false
            ent["isActive"] <- true
            ent["attributes"] <- attrs
            ent["indexes"] <- indexes
            entitiesArr.Add ent
        let md = System.Text.Json.Nodes.JsonObject()
        md["ssKey"] <- guidOf 3 m 0
        md["name"] <- sprintf "Module%03d" m
        md["physicalName"] <- sprintf "Module%03d" m
        md["isActive"] <- true
        md["entities"] <- entitiesArr
        modules.Add md
    node["modules"] <- modules
    node.ToJsonString()

/// Activity 1d: `OssysJsonReader.parseJsonString` over the synthesized
/// envelope — the PERF_OPPORTUNITIES A3/A4 priors become measurable.
// PERF-SCENARIO: ossys-parse 1000 | keylabels=adapter.osm.parse.attribute,adapter.osm.parse.index
let ossysParse (entities: int) : PerfScenario =
    { Name = sprintf "ossys-parse-%d" entities
      Tag = "perf.adapter.osm.parse"
      KeyLabels = [ "adapter.osm.parse.attribute"; "adapter.osm.parse.index" ]
      Run =
        fun _ ->
            task {
                let json = ossysEnvelopeJson entities
                match Projection.Adapters.Osm.OssysJsonReader.parseJsonString json with
                | Error es -> failwithf "ossys-parse failed: %A" es
                | Ok catalog ->
                    let kinds = Catalog.allKinds catalog |> List.length
                    if kinds <> entities then
                        failwithf "ossys-parse lifted %d kinds, expected %d" kinds entities
            } }

// -------------------------------------------------------------------------
// P2 gate (CONSTELLATION_BACKLOG stage 6) — leveled-parallel vs sequential
// rendered-data deploy at the operator envelope: 150 independent static
// kinds × 42 rows = 6,300 rows (the perf-gate canary's table count and
// volume; independent lookups ARE the static-seed topology, so the leveled
// plan is one ParallelSafe group of 150 members). PAIRED back-to-back legs
// on one container control drift: leg A is today's production shape (the
// aggregated sequential batch `runEphemeral` deploys); leg B is the
// promoted shape (per-level ParallelSafe dispatch under the existing
// `Deploy.resolveParallelism` stack). The label pair IS the gate's
// evidence — P2 wires or stays refused on what this prints.
// -------------------------------------------------------------------------

let private leveledSeedCatalog (kinds: int) (rowsPerKind: int) : Catalog =
    StaticCatalogFixtures.staticCatalogOfKinds "PERF_LVL" "PerfLevelMod"
        [ for k in 0 .. kinds - 1 ->
            { StaticCatalogFixtures.KindKeyParts = [ sprintf "Lookup%03d" k ]
              KindName = sprintf "PerfLevel%03d" k
              PhysicalTable = sprintf "PerfLevel%03d" k
              Attrs =
                [ StaticCatalogFixtures.pk "Id" "ID" Integer
                  StaticCatalogFixtures.attr "Code" "CODE" Text
                  StaticCatalogFixtures.attr "Label" "LABEL" Text ]
              Rows =
                [ for i in 1 .. rowsPerKind ->
                    string i, [ string i; sprintf "C%06d" i; sprintf "Level %d row %d" k i ] ] } ]

// PERF-SCENARIO: leveled-deploy 150x42 | keylabels=perf.leveledDeploy.sequential,perf.leveledDeploy.parallel,deploy.executeBatchParallel
let leveledDeploy (kinds: int) (rowsPerKind: int) : PerfScenario =
    let catalog = leveledSeedCatalog kinds rowsPerKind
    { Name = sprintf "leveled-deploy-%dx%d" kinds rowsPerKind
      Tag = "perf.leveledDeploy"
      KeyLabels = [ "perf.leveledDeploy.sequential"; "perf.leveledDeploy.parallel"; "deploy.executeBatchParallel" ]
      Run =
        fun _ ->
            task {
                let ddl = SsdtDdlEmitter.statements catalog |> Render.toText
                let sequentialSeed =
                    match DataEmissionComposer.composeRendered seedPolicy catalog Profile.empty with
                    | Ok s -> s
                    | Error e -> failwithf "leveled-deploy compose (sequential) failed: %A" e
                let leveled =
                    match
                        DataEmissionComposer.composeRenderedLeveled
                            seedPolicy catalog Profile.empty
                            MigrationDependencyContext.empty UserRemapContext.empty
                    with
                    | Ok l -> l
                    | Error e -> failwithf "leveled-deploy compose (leveled) failed: %A" e
                let expectTotal = int64 (kinds * rowsPerKind)
                let assertLoaded (cnn: SqlConnection) (leg: string) =
                    task {
                        use check =
                            new SqlCommand(
                                "SELECT SUM(p.rows) FROM sys.partitions p JOIN sys.tables t ON p.object_id = t.object_id WHERE p.index_id IN (0,1);",
                                cnn)
                        let! loaded = check.ExecuteScalarAsync()
                        if Convert.ToInt64 loaded <> expectTotal then
                            failwithf "leveled-deploy %s leg loaded %d rows, expected %d" leg (Convert.ToInt64 loaded) expectTotal
                    }
                do!
                    Deploy.useContainer (fun masterConn ->
                        task {
                            // Leg A — today's production shape: one aggregated
                            // batch through sequential executeBatch.
                            do!
                                ContainerFixtureSupport.withEphemeralDatabase masterConn "PerfLvlSeq" (fun cnn _ ->
                                    task {
                                        do! Deploy.executeBatch cnn ddl
                                        do!
                                            task {
                                                use _ = Bench.scope "perf.leveledDeploy.sequential"
                                                do! Deploy.executeBatch cnn sequentialSeed
                                            }
                                        do! assertLoaded cnn "sequential"
                                    })
                            // Leg B — the promoted shape: per-level ParallelSafe
                            // dispatch behind the existing parallelism resolution.
                            do!
                                ContainerFixtureSupport.withEphemeralDatabase masterConn "PerfLvlPar" (fun cnn perDbConn ->
                                    task {
                                        do! Deploy.executeBatch cnn ddl
                                        let! parallelism = Deploy.resolveParallelism perDbConn
                                        Bench.recordSample "perf.leveledDeploy.parallelism" (int64 parallelism)
                                        do!
                                            task {
                                                use _ = Bench.scope "perf.leveledDeploy.parallel"
                                                for level in leveled.Phase1Levels do
                                                    if not (ParallelSafe.isEmpty level) then
                                                        do! Deploy.executeBatchParallel perDbConn level parallelism
                                                for level in leveled.Phase2Levels do
                                                    if not (ParallelSafe.isEmpty level) then
                                                        do! Deploy.executeBatchParallel perDbConn level parallelism
                                            }
                                        do! assertLoaded cnn "parallel"
                                    })
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
        Make = fun () -> readsideRowStream 100000 }
      { Name = "execute-stream-batch-100000x1000"
        Docker = true
        Scale = { Rows = 100000; Tables = 1; ColumnsPerTable = 12 }
        Make = fun () -> executeStreamBatch 100000 1000 }
      { Name = "execute-stream-batch-100000x5000"
        Docker = true
        Scale = { Rows = 100000; Tables = 1; ColumnsPerTable = 12 }
        Make = fun () -> executeStreamBatch 100000 5000 }
      { Name = "execute-stream-batch-100000x10000"
        Docker = true
        Scale = { Rows = 100000; Tables = 1; ColumnsPerTable = 12 }
        Make = fun () -> executeStreamBatch 100000 10000 }
      { Name = "static-population-drain-100000"
        Docker = false
        Scale = { Rows = 100000; Tables = 1; ColumnsPerTable = 12 }
        Make = fun () -> staticPopulationDrain 100000 }
      { Name = "physical-schema-verify-100000"
        Docker = false
        Scale = { Rows = 100000; Tables = 1; ColumnsPerTable = 12 }
        Make = fun () -> physicalSchemaVerify 100000 }
      { Name = "profiler-discover-150"
        Docker = true
        Scale = { Rows = 0; Tables = 150; ColumnsPerTable = 4 }
        Make = fun () -> profilerDiscover 150 }
      { Name = "ossys-parse-1000"
        Docker = false
        Scale = { Rows = 0; Tables = 1000; ColumnsPerTable = 8 }
        Make = fun () -> ossysParse 1000 }
      { Name = "leveled-deploy-150x42"
        Docker = true
        Scale = { Rows = 6300; Tables = 150; ColumnsPerTable = 3 }
        Make = fun () -> leveledDeploy 150 42 } ]

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

[<Fact>]
let ``PerfHarness: execute-stream-batch 100000x1000`` () = runDeclared "execute-stream-batch-100000x1000"

[<Fact>]
let ``PerfHarness: execute-stream-batch 100000x5000`` () = runDeclared "execute-stream-batch-100000x5000"

[<Fact>]
let ``PerfHarness: execute-stream-batch 100000x10000`` () = runDeclared "execute-stream-batch-100000x10000"

[<Fact>]
let ``PerfHarness: static-population-drain 100000`` () = runDeclared "static-population-drain-100000"

[<Fact>]
let ``PerfHarness: physical-schema-verify 100000`` () = runDeclared "physical-schema-verify-100000"

[<Fact>]
let ``PerfHarness: profiler-discover 150`` () = runDeclared "profiler-discover-150"

[<Fact>]
let ``PerfHarness: ossys-parse 1000`` () = runDeclared "ossys-parse-1000"

[<Fact>]
let ``PerfHarness: leveled-deploy 150x42`` () = runDeclared "leveled-deploy-150x42"
