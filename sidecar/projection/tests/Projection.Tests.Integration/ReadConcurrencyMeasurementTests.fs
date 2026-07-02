namespace Projection.Tests

// ---------------------------------------------------------------------------
// Read-concurrency measurement — the analogous baseline for the full-export
// data-read / live-profile parallelism (`emission.dataReadConcurrency` /
// `profiler.maxConcurrency`).
//
// The claim under test: full-export wall clock in the extract/profile stages
// is dominated by hundreds of INDEPENDENT per-table operations executed
// STRICTLY SERIALLY — one row-stream drain per kind, plus one 3-query live
// discovery per non-static kind — so bounded table-level parallelism cuts
// wall-clock roughly in proportion to the overlap, while row coverage stays
// IDENTICAL (acquisition-only concurrency; the rendered load plan stays
// deterministic and dependency-ordered).
//
// This harness mocks that shape at reduced volume (~1/10th of a
// hundreds-of-tables estate): N tables x R rows, then measures the SAME
// code paths the pipeline runs — serial `Ingestion.collectInOrderFor` vs
// `collectInOrderForConcurrent`, and serial
// `LiveProfiler.captureEvidenceCacheWith` vs `captureEvidenceCacheConcurrent`
// — asserting value-identical results and printing the measured timings.
// Timings are environment-relative (a local container has lower per-table
// latency than a remote source, so local speedups UNDERSTATE the remote
// win); the assertions therefore pin EQUALITY of outcomes, and the printed
// measurements substantiate the serial-cost shape.
// ---------------------------------------------------------------------------

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Xunit
open Xunit.Abstractions
open Projection.Core
open Projection.Core.Passes
open Projection.Adapters.Sql
open Projection.Pipeline

module private ReadConcurrencyFixtures =

    let mustOk r = match r with Ok v -> v | Error es -> failwithf "fixture: %A" es
    let mkKey (parts: string list) : SsKey = SsKey.synthesizedComposite "OS_TEST_RCB" parts |> mustOk
    let mkName (s: string) : Name = Name.create s |> mustOk

    let skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else printfn "SKIP %s: Docker daemon not reachable." label; false

    /// The mock estate: `tableCount` kinds, `rowsPerTable` rows each.
    let tableCount   = 100
    let rowsPerTable = 375   // 100 x 375 = 37,500 rows (~1/10th of ~375k)

    let tableName (i: int) : string = sprintf "BENCH_RC_T%03d" i

    let kindOf (i: int) : Kind =
        let label = sprintf "Bench%03d" i
        let idAttr =
            { Attribute.create (mkKey [label; "Id"]) (mkName "Id") Integer with
                Column = ColumnRealization.create ("ID") (false) |> Result.value
                IsPrimaryKey = true
                IsMandatory  = true }
        let codeAttr =
            { Attribute.create (mkKey [label; "Code"]) (mkName "Code") Text with
                Column = ColumnRealization.create ("CODE") (false) |> Result.value
                Length = Some 20
                IsMandatory = true }
        let amountAttr =
            { Attribute.create (mkKey [label; "Amount"]) (mkName "Amount") Integer with
                Column = ColumnRealization.create ("AMOUNT") (true) |> Result.value
                IsMandatory = false }
        { Kind.create (mkKey [label]) (mkName label)
            (TableId.create "dbo" (tableName i) |> Result.value)
            [ idAttr; codeAttr; amountAttr ]
          with References = []; Indexes = []; ColumnChecks = [] }

    let kinds : Kind list = [ for i in 1 .. tableCount -> kindOf i ]

    let catalog : Catalog =
        { Modules =
            [ { SsKey = mkKey ["Module"]
                Name  = mkName "BenchModule"
                Kinds = kinds
                IsActive = true
                ExtendedProperties = [] } ]
          Sequences = [] }

    let schemaSql (i: int) : string =
        sprintf
            "CREATE TABLE [dbo].[%s] ([ID] INT NOT NULL PRIMARY KEY, [CODE] NVARCHAR(20) NOT NULL, [AMOUNT] INT NULL);"
            (tableName i)

    let seedSql (i: int) : string =
        let values =
            [ for r in 1 .. rowsPerTable ->
                sprintf "(%d, N'C%03d_%04d', %s)" r i r (if r % 7 = 0 then "NULL" else string (r * 3)) ]
            |> String.concat ", "
        sprintf "INSERT INTO [dbo].[%s] ([ID], [CODE], [AMOUNT]) VALUES %s;" (tableName i) values

    let totalRows (rows: Map<SsKey, StaticRow list>) : int =
        rows |> Map.fold (fun acc _ rs -> acc + List.length rs) 0


[<Xunit.Collection("Docker-SqlServer")>]
type ReadConcurrencyMeasurementTests(fixture: EphemeralContainerFixture, output: ITestOutputHelper) =
    interface IClassFixture<EphemeralContainerFixture>

    member private _.Say (line: string) =
        output.WriteLine line
        printfn "%s" line

    [<Fact>]
    member this.``measurement: serial vs bounded-parallel hydration and profiling over a 100-table mock estate`` () =
        if not (ReadConcurrencyFixtures.skipIfNoDocker "read-concurrency-measurement") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "RcBench" (fun cnn perDbConn ->
                task {
                    // -- Arrange: N tables x R rows.
                    let sw = System.Diagnostics.Stopwatch.StartNew()
                    for i in 1 .. ReadConcurrencyFixtures.tableCount do
                        do! Deploy.executeBatch cnn (ReadConcurrencyFixtures.schemaSql i)
                        do! Deploy.executeBatch cnn (ReadConcurrencyFixtures.seedSql i)
                    sw.Stop()
                    this.Say (sprintf "[rc-bench] arrange: %d tables x %d rows seeded in %dms"
                                ReadConcurrencyFixtures.tableCount ReadConcurrencyFixtures.rowsPerTable sw.ElapsedMilliseconds)

                    let catalog = ReadConcurrencyFixtures.catalog
                    let topo = (TopologicalOrderPass.runWith TreatAsCycle catalog).Value
                    let owned = topo.Order |> Set.ofList

                    let openConnection () : Task<Result<SqlConnection>> =
                        task {
                            let c = new SqlConnection(perDbConn)
                            do! c.OpenAsync()
                            return Result.success c
                        }

                    // -- Warmup (unmeasured): one full serial hydrate + profile
                    //    pass so JIT, plan caches, and the connection pool are
                    //    warm before ANY measured leg — otherwise the first
                    //    (serial) leg would be unfairly penalized.
                    let! _ = Ingestion.collectInOrderFor owned cnn catalog topo
                    let! _ = LiveProfiler.captureEvidenceCacheWith SqlProfilerOptions.defaults cnn catalog

                    // -- Leg 1: serial hydration (the pre-fix shape — one
                    //    connection, one drain at a time).
                    let swSerial = System.Diagnostics.Stopwatch.StartNew()
                    let! serialRows = Ingestion.collectInOrderFor owned cnn catalog topo
                    swSerial.Stop()

                    // -- Leg 2: bounded-parallel hydration (concurrency 4).
                    let swPar = System.Diagnostics.Stopwatch.StartNew()
                    let! parallelRowsR = Ingestion.collectInOrderForConcurrent 4 openConnection owned catalog topo
                    swPar.Stop()
                    let parallelRows = ReadConcurrencyFixtures.mustOk parallelRowsR

                    // -- Knob shape: concurrency 2 and 8 (the win should be
                    //    material at low bounds; scaling flattens as the
                    //    bottleneck moves to pool/server pressure).
                    let swPar2 = System.Diagnostics.Stopwatch.StartNew()
                    let! par2R = Ingestion.collectInOrderForConcurrent 2 openConnection owned catalog topo
                    swPar2.Stop()
                    let par2 = ReadConcurrencyFixtures.mustOk par2R
                    let swPar8 = System.Diagnostics.Stopwatch.StartNew()
                    let! par8R = Ingestion.collectInOrderForConcurrent 8 openConnection owned catalog topo
                    swPar8.Stop()
                    let par8 = ReadConcurrencyFixtures.mustOk par8R
                    Assert.Equal<Map<SsKey, StaticRow list>>(serialRows, par2)
                    Assert.Equal<Map<SsKey, StaticRow list>>(serialRows, par8)

                    // Coverage is IDENTICAL — same keys, same rows, same values.
                    Assert.Equal(ReadConcurrencyFixtures.tableCount, Map.count serialRows)
                    Assert.Equal<Map<SsKey, StaticRow list>>(serialRows, parallelRows)
                    let rowTotal = ReadConcurrencyFixtures.totalRows serialRows
                    Assert.Equal(ReadConcurrencyFixtures.tableCount * ReadConcurrencyFixtures.rowsPerTable, rowTotal)

                    this.Say (sprintf "[rc-bench] hydrate serial:       %6dms  (%d rows over %d tables)"
                                swSerial.ElapsedMilliseconds rowTotal ReadConcurrencyFixtures.tableCount)
                    this.Say (sprintf "[rc-bench] hydrate concurrent-2: %6dms  (identical row map)"
                                swPar2.ElapsedMilliseconds)
                    this.Say (sprintf "[rc-bench] hydrate concurrent-4: %6dms  (identical row map)"
                                swPar.ElapsedMilliseconds)
                    this.Say (sprintf "[rc-bench] hydrate concurrent-8: %6dms  (identical row map)"
                                swPar8.ElapsedMilliseconds)

                    // -- Leg 3: serial live-profile discovery (3 queries per kind).
                    let swProfSerial = System.Diagnostics.Stopwatch.StartNew()
                    let! serialCacheR = LiveProfiler.captureEvidenceCacheWith SqlProfilerOptions.defaults cnn catalog
                    swProfSerial.Stop()
                    let serialCache = ReadConcurrencyFixtures.mustOk serialCacheR

                    // -- Leg 4: bounded-parallel discovery (concurrency 2/4/8).
                    let swProfPar2 = System.Diagnostics.Stopwatch.StartNew()
                    let! parCache2R =
                        LiveProfiler.captureEvidenceCacheConcurrent
                            { SqlProfilerOptions.defaults with MaxConcurrency = 2 }
                            openConnection catalog
                    swProfPar2.Stop()
                    let parCache2 = ReadConcurrencyFixtures.mustOk parCache2R
                    let swProfPar = System.Diagnostics.Stopwatch.StartNew()
                    let! parCacheR =
                        LiveProfiler.captureEvidenceCacheConcurrent
                            { SqlProfilerOptions.defaults with MaxConcurrency = 4 }
                            openConnection catalog
                    swProfPar.Stop()
                    let parCache = ReadConcurrencyFixtures.mustOk parCacheR
                    let swProfPar8 = System.Diagnostics.Stopwatch.StartNew()
                    let! parCache8R =
                        LiveProfiler.captureEvidenceCacheConcurrent
                            { SqlProfilerOptions.defaults with MaxConcurrency = 8 }
                            openConnection catalog
                    swProfPar8.Stop()
                    let parCache8 = ReadConcurrencyFixtures.mustOk parCache8R

                    Assert.Equal(ReadConcurrencyFixtures.tableCount, Map.count serialCache.Kinds)
                    Assert.Equal<Map<SsKey, CachedKind>>(serialCache.Kinds, parCache.Kinds)
                    Assert.Equal<Map<SsKey, CachedKind>>(serialCache.Kinds, parCache2.Kinds)
                    Assert.Equal<Map<SsKey, CachedKind>>(serialCache.Kinds, parCache8.Kinds)

                    this.Say (sprintf "[rc-bench] profile serial:       %6dms  (%d kinds x 3 queries)"
                                swProfSerial.ElapsedMilliseconds ReadConcurrencyFixtures.tableCount)
                    this.Say (sprintf "[rc-bench] profile concurrent-2: %6dms  (identical evidence cache)"
                                swProfPar2.ElapsedMilliseconds)
                    this.Say (sprintf "[rc-bench] profile concurrent-4: %6dms  (identical evidence cache)"
                                swProfPar.ElapsedMilliseconds)
                    this.Say (sprintf "[rc-bench] profile concurrent-8: %6dms  (identical evidence cache)"
                                swProfPar8.ElapsedMilliseconds)
                }))
