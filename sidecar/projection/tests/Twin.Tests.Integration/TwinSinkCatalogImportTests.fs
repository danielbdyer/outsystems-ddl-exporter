module Twin.Tests.Integration.TwinSinkCatalogImportTests

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Adapters.OssysSql
open Twin.Core
open Twin.Runtime

// ---------------------------------------------------------------------------
// K10 (the data-sink chapter, S15): the Twin imports evidence with the
// CATALOG from a witnessed sink edition — the post-eject rendition map.
// The upstream OSSYS is gone (nothing here reads it at import time); the
// witnessed edition supplies the logical-names-over-physical-realizations
// map (K2 parity: THE catalog the live read gave), and the DATA still
// profiles over the source's live connection. The negative arm: a sink
// ref naming an environment nobody stamped refuses by name, never a
// silent empty pack.
//
// Env-var manipulation is safe here: the Twin-Docker collection
// serializes, and every mutation restores in finally.
// ---------------------------------------------------------------------------

[<Collection("Twin-Docker")>]
type TwinSinkCatalogImportTests () =

    let SourceConnVar = "TWIN_SINKCAT_SOURCE_CONN"

    [<Fact>]
    member _.``K10: evidence imports with the catalog from the witnessed edition; a ghost environment refuses by name`` () : Task =
        task {
            let! handle = Deploy.acquireContainer ()
            let sourceDb = System.String.Concat("TwinSinkCat_", System.Guid.NewGuid().ToString("N").Substring(0, 10))
            let tempStore = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "twin-sinkcat-tests", System.Guid.NewGuid().ToString("N"))
            let tempRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "twin-sinkcat-root", System.Guid.NewGuid().ToString("N"))
            System.IO.Directory.CreateDirectory tempStore |> ignore
            System.IO.Directory.CreateDirectory tempRoot |> ignore
            let priorEstate = System.Environment.GetEnvironmentVariable "PROJECTION_ESTATE_DIR"
            let priorLedger = System.Environment.GetEnvironmentVariable "PROJECTION_LEDGER_DIR"
            let priorConn = System.Environment.GetEnvironmentVariable SourceConnVar
            try
                // A physical source: the lifecycle seed's OSSYS estate.
                do! task {
                    use master = new SqlConnection(handle.MasterConnectionString)
                    do! master.OpenAsync()
                    do! Deploy.executeBatch master (System.String.Concat("CREATE DATABASE [", sourceDb, "];"))
                }
                let builder = SqlConnectionStringBuilder handle.MasterConnectionString
                builder.InitialCatalog <- sourceDb
                let sourceConn = builder.ConnectionString
                use cnn = new SqlConnection(sourceConn)
                do! cnn.OpenAsync()
                do! Deploy.executeBatch cnn (MetadataExtractionSql.readLifecycleSeed ())

                // Witness the edition and STAMP the environment name — the
                // naming act `projection sync cutover` performs; the sink
                // ref below spends it.
                let! snapshotResult = MetadataSnapshotRunner.runAsync cnn MetadataSnapshotRunner.defaultParameters
                let snapshot =
                    match snapshotResult with
                    | Ok s -> s
                    | Error es -> failwithf "acquisition refused: %A" es
                let nowUtc = System.DateTimeOffset.Parse("2026-08-15T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture)
                let witnessed =
                    SinkStore.witnessWith (Some tempStore) nowUtc cnn.DataSource cnn.Database (Some "cutover") []
                        MetadataSnapshotRunner.defaultParameters snapshot
                let _ =
                    match witnessed with
                    | SinkStore.WitnessOutcome.Persisted (1, _, _) -> ()
                    | other -> failwithf "expected the first witnessed sync, got %A" other

                // The import resolves `sink:cutover` through the configured
                // store; the data profiles over the live source connection.
                System.Environment.SetEnvironmentVariable("PROJECTION_ESTATE_DIR", tempStore)
                System.Environment.SetEnvironmentVariable("PROJECTION_LEDGER_DIR", null)
                System.Environment.SetEnvironmentVariable(SourceConnVar, sourceConn)
                let configOf (sinkRef: string) =
                    let json =
                        System.String.Concat(
                            "{ \"estate\": { \"tables\": \"estate/tables/*.sql\" }, ",
                            "\"evidence\": { \"rich\": \"twin/evidence.rich.json\", ",
                            "\"sources\": [ { \"name\": \"post-cutover\", \"rendition\": \"physical\", \"conn\": \"env:",
                            SourceConnVar,
                            "\", \"catalogRef\": \"", sinkRef, "\", \"tables\": [\"dbo.Order\"] } ] } }")
                    match TwinConfig.parse json with
                    | Ok c -> c
                    | Error es -> failwithf "config refused: %A" (es |> List.map (fun e -> e.Code, e.Message))

                let! imported = EvidenceImport.importAll tempRoot (configOf "sink:cutover")
                match imported with
                | Error es -> failwithf "import refused: %A" (es |> List.map (fun e -> e.Code, e.Message))
                | Ok report ->
                    Assert.Equal(1, List.length report.Sources)
                    let src = List.exactlyOne report.Sources
                    Assert.Equal(1, src.Tables)
                    Assert.True(src.Columns > 0, "the witnessed catalog's Order attributes must profile over the live data connection")
                    Assert.True(System.IO.File.Exists report.RichPath)
                    Assert.Contains("dbo.Order", System.IO.File.ReadAllText report.RichPath)

                // The negative arm: nobody stamped 'ghost' — the refusal is
                // named at the sink layer, never a silent empty pack.
                let! ghost = EvidenceImport.importAll tempRoot (configOf "sink:ghost")
                let _ =
                    match ghost with
                    | Ok _ -> failwith "expected the ghost environment to refuse"
                    | Error es ->
                        Assert.True(es |> List.exists (fun e -> e.Code = "sink.envUnknown"),
                                    sprintf "expected sink.envUnknown, got %A" (es |> List.map (fun e -> e.Code)))
                return ()
            finally
                System.Environment.SetEnvironmentVariable("PROJECTION_ESTATE_DIR", priorEstate)
                System.Environment.SetEnvironmentVariable("PROJECTION_LEDGER_DIR", priorLedger)
                System.Environment.SetEnvironmentVariable(SourceConnVar, priorConn)
                try System.IO.Directory.Delete(tempStore, true) with _ -> ()
                try System.IO.Directory.Delete(tempRoot, true) with _ -> ()
                try
                    use master = new SqlConnection(handle.MasterConnectionString)
                    master.Open()
                    use cmd = master.CreateCommand()
                    cmd.CommandText <-
                        System.String.Concat(
                            "IF DB_ID(N'", sourceDb, "') IS NOT NULL BEGIN ALTER DATABASE [", sourceDb,
                            "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [", sourceDb, "]; END")
                    cmd.ExecuteNonQuery() |> ignore
                with _ -> ()
                (handle.DisposeAsync()).GetAwaiter().GetResult()
        }
