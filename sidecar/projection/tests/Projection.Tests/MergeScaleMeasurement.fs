module Projection.Tests.MergeScaleMeasurement

open System
open System.IO
open System.Diagnostics
open Microsoft.Data.SqlClient
open Microsoft.SqlServer.Dac
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Targets.SSDT
open Projection.Targets.Data
open Projection.Tests.StaticCatalogFixtures

// ============================================================================
// Gated UPPER-BOUND probe (set PROJECTION_MEASURE_SCALE=1): how large a single
// static/bootstrap MERGE can we DEPLOY before it cliffs (parse / memory-grant)?
// Escalates rows-per-kind until the first SqlException, recording render time,
// statement size, deploy time, and rows/sec at each step. Each scale runs in a
// FRESH database (per-DB isolation), and the bounded CommandTimeout
// (PROJECTION_COMMAND_TIMEOUT_SEC) turns a RESOURCE_SEMAPHORE stall into a
// recordable cliff rather than an infinite hang. Results also written to
// PROJECTION_MEASURE_OUT (default %TEMP%/merge-scale-measurement.txt).
//
// NOT a CI test — it's off unless the env flag is set. The fixed harness floor
// is `seed-merge-execute-{1000,2500,10000}` (PerfHarnessScenarios); this probe
// is the one-off "how far does it go" companion.
//
// EMPIRICAL FINDING (2026-06-25, 4 GiB warm container, single 4-column kind):
//   25k → PASS (6–8 s, ~3–4k rows/sec, COUNT-verified)
//   35k / 40k → CLIFF 8623 (compile fails after ~12 s)
//   100k → optimizer spun past the 180 s CommandTimeout
// SQL Server ERROR 8623 = "the query processor ran out of internal resources and
// could not produce a query plan." This is a COMPILE-complexity wall — the
// optimizer can't plan a 30k+-row `VALUES` constructor inside a MERGE — NOT a
// runtime memory stall. So for a large static/bootstrap kind it is a hard DEPLOY
// FAILURE, not slowness; the ceiling is ~25–30k rows/kind here (higher on a
// bigger server, but the wall persists — 8623 with large VALUES shows up at scale
// regardless). FIX (the promoted "staged-bulk MERGE" deferral): stage the source
// rows into a #temp table (batched INSERTs / SqlBulkCopy), then run ONE
// `MERGE … USING #temp` — no VALUES ceiling, and the DELETE-arm + two-phase
// semantics stay intact (which naive VALUES-chunking would break).
// ============================================================================

let private scaleCatalog (n: int) : Catalog =
    staticCatalog "SCALE" "ScaleMod" [ "Lookup" ] "ScaleLookup" "SCALE_LOOKUP"
        [ pk "Id" "ID" Integer
          attr "Code" "CODE" Text
          attr "Label" "LABEL" Text ]
        [ for i in 1 .. n -> string i, [ string i; sprintf "C%06d" i; sprintf "Perf label %d" i ] ]

let private staticSeeds (policy: Policy) (catalog: Catalog) : string =
    match
        DataEmissionComposer.composeRenderedBundleWithBootstrap
            policy catalog Profile.empty MigrationDependencyContext.empty Map.empty UserRemapContext.empty
    with
    | Ok b -> b.StaticSeeds
    | Error e -> failwithf "compose failed: %A" e

let private deploySchema (connStr: string) (catalog: Catalog) : unit =
    let dbName = SqlConnectionStringBuilder(connStr).InitialCatalog
    let bytes =
        match DacpacEmitter.emit catalog with
        | Ok b -> b
        | Error es -> failwithf "dacpac emit failed: %A" es
    use stream = new MemoryStream(bytes)
    use package = DacPackage.Load stream
    (DacServices connStr).Deploy(package, dbName, true, DacDeployOptions())

[<Xunit.Collection("Docker-SqlServer")>]
type MergeScaleMeasurement(fixture: EphemeralContainerFixture) =
    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``MEASURE: single-kind static MERGE upper bound across scales`` () =
        if Environment.GetEnvironmentVariable "PROJECTION_MEASURE_SCALE" <> "1" then
            printfn "SKIP scale measurement: set PROJECTION_MEASURE_SCALE=1 to run."
        elif not (Deploy.Docker.ensureRunning ()) then
            printfn "SKIP scale measurement: Docker not reachable."
        else
            let outPath =
                match Environment.GetEnvironmentVariable "PROJECTION_MEASURE_OUT" with
                | null | "" -> Path.Combine(Path.GetTempPath(), "merge-scale-measurement.txt")
                | p -> p
            let emit (line: string) =
                printfn "%s" line
                File.AppendAllText(outPath, line + "\n")
            File.WriteAllText(outPath, "")
            let policy = { Policy.empty with Emission = EmissionPolicy.combined }
            let scales =
                match Environment.GetEnvironmentVariable "PROJECTION_MEASURE_SCALES" with
                | null | "" -> [ 25000; 100000; 250000; 500000; 1000000 ]
                | s ->
                    s.Split(',')
                    |> Array.choose (fun x -> match Int32.TryParse(x.Trim()) with | true, n -> Some n | _ -> None)
                    |> Array.toList
            emit "SCALE_MEASUREMENT_BEGIN"
            emit (sprintf "timeout=%ss" (match Environment.GetEnvironmentVariable "PROJECTION_COMMAND_TIMEOUT_SEC" with null | "" -> "300(default)" | s -> s))
            emit "rows\trenderMs\tstmtMB\tdeployMs\trows_per_sec\tresult"
            let mutable stop = false
            for n in scales do
                if not stop then
                    let catalog = scaleCatalog n
                    let swR = Stopwatch.StartNew()
                    let seeds = staticSeeds policy catalog
                    swR.Stop()
                    let stmtMB = float seeds.Length / 1048576.0
                    let mutable deployMs = 0L
                    let mutable result = "PASS"
                    TaskSync.run (fun () ->
                        fixture.WithEphemeralDatabase (sprintf "Scale%d" n) (fun cnn connStr ->
                            task {
                                deploySchema connStr catalog
                                let sw = Stopwatch.StartNew()
                                try
                                    do! Deploy.executeBatch cnn seeds
                                    let! cnt =
                                        task {
                                            use cmd = cnn.CreateCommand()
                                            cmd.CommandText <- "SELECT COUNT(*) FROM [dbo].[SCALE_LOOKUP];"
                                            let! v = cmd.ExecuteScalarAsync()
                                            return string v
                                        }
                                    if cnt <> string n then result <- sprintf "MISCOUNT %s" cnt
                                with :? SqlException as ex ->
                                    result <- sprintf "CLIFF %d (%s)" ex.Number ((ex.Message.Split('\n')).[0])
                                sw.Stop()
                                deployMs <- sw.ElapsedMilliseconds
                            }))
                    let rps = if deployMs > 0L then int64 n * 1000L / deployMs else 0L
                    emit (sprintf "%d\t%d\t%.1f\t%d\t%d\t%s" n swR.ElapsedMilliseconds stmtMB deployMs rps result)
                    if result.StartsWith "CLIFF" then stop <- true
            emit "SCALE_MEASUREMENT_END"
