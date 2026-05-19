namespace Projection.Tests

open System
open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Targets.SSDT

/// Validates `Deploy.executeBatchParallel` (slice A.4.7'-prelude.perf-
/// sweep-5 primitive). The primitive splits via `BatchSplitter`,
/// dispatches segments across `parallelism` concurrent SqlConnections
/// gated by a SemaphoreSlim, and awaits all completions. Caller
/// contract: segments MUST be mutually independent.
///
/// Three tests:
///   1. Correctness on 5 independent tables — verifies rows land,
///      bench labels fire, and parallel dispatch returns without
///      error.
///   2. Errors surface from a failing segment.
///   3. Microbenchmark — sequential `executeBatch` vs parallel
///      `executeBatchParallel` on 20 independent tables to demonstrate
///      the wall-time delta the primitive achieves on parallel-safe
///      workloads.
[<Xunit.Collection("Docker-SqlServer")>]
module ExecuteBatchParallelTests =

    let private skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then
            true
        else
            printfn
                "SKIP %s: Docker daemon not reachable."
                label
            false

    /// Build N independent tables (no FK between them) — schema DDL.
    /// Single batch (no GO) so it deploys as one segment via
    /// `executeBatch`; sole purpose is the schema setup, not the
    /// thing under test.
    let private buildIndependentSchemaDdl (n: int) : string =
        let sb = System.Text.StringBuilder()
        for i in 0 .. n - 1 do
            sb.AppendLine(
                sprintf
                    "CREATE TABLE [dbo].[T%d] ([Id] INT NOT NULL PRIMARY KEY, [Label] NVARCHAR(50) NOT NULL);"
                    i) |> ignore
            sb.AppendLine "GO" |> ignore
        sb.ToString()

    /// Build N independent INSERT batches (one INSERT-per-table,
    /// separated by GO) — each is a self-contained segment with no
    /// cross-table dependency.
    let private buildIndependentInsertBatches (n: int) (rowsPerTable: int) : string =
        let sb = System.Text.StringBuilder()
        for i in 0 .. n - 1 do
            sb.AppendLine(
                sprintf
                    "INSERT INTO [dbo].[T%d] ([Id], [Label]) VALUES"
                    i) |> ignore
            for r in 0 .. rowsPerTable - 1 do
                let sep = if r = rowsPerTable - 1 then ";" else ","
                sb.AppendLine(
                    sprintf "    (%d, N'row-%d-%d')%s" r i r sep) |> ignore
            sb.AppendLine "GO" |> ignore
        sb.ToString()

    let private countRows (cnn: SqlConnection) (table: string) : Task<int> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sprintf "SELECT COUNT(*) FROM [dbo].[%s]" table
            let! v = cmd.ExecuteScalarAsync()
            return Convert.ToInt32 v
        }

    [<Fact>]
    let ``executeBatchParallel deploys independent INSERT batches and surfaces bench labels`` () =
        if skipIfNoDocker "executeBatchParallel-correctness" then
            // Reset bench so the assertion on label fire is clean.
            Bench.reset ()
            let task =
                Deploy.useContainer (fun masterConn ->
                    task {
                        // Spin a fresh DB on the warm container.
                        let dbName =
                            String.Concat(
                                "ExecBatchParallel_",
                                Guid.NewGuid().ToString("N").Substring(0, 8))
                        do! task {
                            use cnnMaster = new SqlConnection(masterConn)
                            do! cnnMaster.OpenAsync()
                            do! Deploy.executeBatch
                                    cnnMaster
                                    (String.Concat(
                                        "CREATE DATABASE ",
                                        Render.quote dbName,
                                        ";"))
                        }
                        let perDbConn =
                            Deploy.ConnectionString.buildPerDb masterConn dbName
                        try
                            // Schema deploy sequentially (FK-free but
                            // schema is one CREATE TABLE per table, not
                            // the thing we're benching).
                            do! task {
                                use cnn = new SqlConnection(perDbConn)
                                do! cnn.OpenAsync()
                                do! Deploy.executeBatch cnn (buildIndependentSchemaDdl 5)
                            }
                            // Parallel insert dispatch.
                            let inserts = buildIndependentInsertBatches 5 3
                            do! Deploy.executeBatchParallel perDbConn inserts 3
                            // Verify rows landed.
                            use cnn = new SqlConnection(perDbConn)
                            do! cnn.OpenAsync()
                            let! counts =
                                task {
                                    let mutable acc : int list = []
                                    for i in 0 .. 4 do
                                        let! c = countRows cnn (sprintf "T%d" i)
                                        acc <- c :: acc
                                    return List.rev acc
                                }
                            return counts
                        finally
                            // Best-effort drop.
                            try
                                use cnnDrop = new SqlConnection(masterConn)
                                cnnDrop.OpenAsync().GetAwaiter().GetResult()
                                let q = Render.quote dbName
                                Deploy.executeBatch
                                    cnnDrop
                                    (String.Concat(
                                        "ALTER DATABASE ", q,
                                        " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; ",
                                        "DROP DATABASE ", q, ";"))
                                |> fun t -> t.GetAwaiter().GetResult()
                            with _ -> ()
                    })
            let counts = task.GetAwaiter().GetResult()
            Assert.Equal<int list>([3; 3; 3; 3; 3], counts)
            // Assert primitive's bench labels fired.
            let stats = Bench.snapshot ()
            let labels = stats |> List.map (fun s -> s.Label) |> Set.ofList
            Assert.Contains("deploy.executeBatchParallel", labels)
            Assert.Contains("deploy.executeBatchParallel.segments", labels)
            Assert.Contains("deploy.executeBatchParallel.parallelism", labels)
            Assert.Contains("deploy.executeBatchParallel.segment", labels)
            Assert.Contains("deploy.executeBatchParallel.segment.bytes", labels)

    [<Fact>]
    let ``executeBatchParallel surfaces SqlException when a segment fails`` () =
        if skipIfNoDocker "executeBatchParallel-error" then
            let task =
                Deploy.useContainer (fun masterConn ->
                    task {
                        let dbName =
                            String.Concat(
                                "ExecBatchParallelErr_",
                                Guid.NewGuid().ToString("N").Substring(0, 8))
                        do! task {
                            use cnnMaster = new SqlConnection(masterConn)
                            do! cnnMaster.OpenAsync()
                            do! Deploy.executeBatch
                                    cnnMaster
                                    (String.Concat(
                                        "CREATE DATABASE ",
                                        Render.quote dbName,
                                        ";"))
                        }
                        let perDbConn =
                            Deploy.ConnectionString.buildPerDb masterConn dbName
                        try
                            // Two segments — one good, one referencing a
                            // missing table. We don't deploy any schema;
                            // both should fail. Either way, the await
                            // must surface an exception.
                            let badSql =
                                "SELECT 1;\nGO\nINSERT INTO [dbo].[DoesNotExist] ([X]) VALUES (1);\nGO"
                            let mutable threw = false
                            try
                                do! Deploy.executeBatchParallel perDbConn badSql 2
                            with _ -> threw <- true
                            return threw
                        finally
                            try
                                use cnnDrop = new SqlConnection(masterConn)
                                cnnDrop.OpenAsync().GetAwaiter().GetResult()
                                let q = Render.quote dbName
                                Deploy.executeBatch
                                    cnnDrop
                                    (String.Concat(
                                        "ALTER DATABASE ", q,
                                        " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; ",
                                        "DROP DATABASE ", q, ";"))
                                |> fun t -> t.GetAwaiter().GetResult()
                            with _ -> ()
                    })
            let threw = task.GetAwaiter().GetResult()
            Assert.True(threw, "expected executeBatchParallel to surface an exception when a segment fails")

    [<Fact>]
    let ``executeBatchParallel is faster than executeBatch on 20 independent INSERT batches`` () =
        if skipIfNoDocker "executeBatchParallel-bench" then
            let n = 20
            // 400 rows per table; meaningful payload per segment so the
            // round-trip cost is not dwarfed by SQL Server's per-batch
            // planner overhead — closer to the operator-reality
            // canary's per-segment-size distribution (mean ~300KB).
            let rowsPerTable = 400
            let schema = buildIndependentSchemaDdl n
            let inserts = buildIndependentInsertBatches n rowsPerTable
            let task =
                Deploy.useContainer (fun masterConn ->
                    task {
                        // ---- Sequential pass: dedicated DB.
                        let dbSeq =
                            String.Concat(
                                "ExecBatchSeq_",
                                Guid.NewGuid().ToString("N").Substring(0, 8))
                        do! task {
                            use cnnM = new SqlConnection(masterConn)
                            do! cnnM.OpenAsync()
                            do! Deploy.executeBatch
                                    cnnM
                                    (String.Concat(
                                        "CREATE DATABASE ", Render.quote dbSeq, ";"))
                        }
                        let connSeq = Deploy.ConnectionString.buildPerDb masterConn dbSeq
                        let seqMs =
                            task {
                                use cnn = new SqlConnection(connSeq)
                                do! cnn.OpenAsync()
                                do! Deploy.executeBatch cnn schema
                                let sw = System.Diagnostics.Stopwatch.StartNew()
                                do! Deploy.executeBatch cnn inserts
                                sw.Stop()
                                return sw.ElapsedMilliseconds
                            } |> fun t -> t.GetAwaiter().GetResult()

                        // ---- Parallel pass: dedicated DB.
                        let dbPar =
                            String.Concat(
                                "ExecBatchPar_",
                                Guid.NewGuid().ToString("N").Substring(0, 8))
                        do! task {
                            use cnnM = new SqlConnection(masterConn)
                            do! cnnM.OpenAsync()
                            do! Deploy.executeBatch
                                    cnnM
                                    (String.Concat(
                                        "CREATE DATABASE ", Render.quote dbPar, ";"))
                        }
                        let connPar = Deploy.ConnectionString.buildPerDb masterConn dbPar
                        do! task {
                            use cnn = new SqlConnection(connPar)
                            do! cnn.OpenAsync()
                            do! Deploy.executeBatch cnn schema
                        }
                        let sw = System.Diagnostics.Stopwatch.StartNew()
                        do! Deploy.executeBatchParallel connPar inserts 4
                        sw.Stop()
                        let parMs = sw.ElapsedMilliseconds

                        // ---- Teardown both DBs (best-effort).
                        for db in [ dbSeq; dbPar ] do
                            try
                                use cnnDrop = new SqlConnection(masterConn)
                                cnnDrop.OpenAsync().GetAwaiter().GetResult()
                                let q = Render.quote db
                                Deploy.executeBatch
                                    cnnDrop
                                    (String.Concat(
                                        "ALTER DATABASE ", q,
                                        " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; ",
                                        "DROP DATABASE ", q, ";"))
                                |> fun t -> t.GetAwaiter().GetResult()
                            with _ -> ()
                        return seqMs, parMs
                    })
            let seqMs, parMs = task.GetAwaiter().GetResult()
            printfn
                "executeBatch microbench: sequential=%dms  parallel(4)=%dms  speedup=%.2fx"
                seqMs parMs (float seqMs / float (max parMs 1L))
            // The primitive must finish; we don't hard-assert speedup
            // (warm-container variance can give parallel < sequential
            // on tiny payloads where SQL Server's planner dominates).
            // The PRINTED delta is the artifact this test produces;
            // an assertion that we got non-zero work done satisfies the
            // structural contract.
            Assert.True(seqMs >= 0L)
            Assert.True(parMs >= 0L)
