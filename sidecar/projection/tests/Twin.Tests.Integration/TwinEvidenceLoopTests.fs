module Twin.Tests.Integration.TwinEvidenceLoopTests

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Xunit
open Projection.Core
open Projection.Pipeline
open Twin.Core
open Twin.Runtime
open Twin.Tests.Integration

// ---------------------------------------------------------------------------
// The M3 evidence lifecycle, end to end: a live logical source with a known
// skewed corpus → import (rich) → derive (shape; law 3 at the file grain) →
// a mint that re-profiles to the source's shape → the coverage board.
// ---------------------------------------------------------------------------

/// Its own twin container; the SOURCE database rides the warm-honoring
/// container acquisition (a per-test database, reaped on dispose).
type TwinEvidenceEstateFixture () =
    inherit TwinEstateFixture ("twin-e2e-evidence", 21733)

[<Collection("Twin-Docker")>]
type TwinEvidenceLoopTests (fixture: TwinEvidenceEstateFixture) =

    let SourceConnVar = "TWIN_E2E_SOURCE_CONN"

    interface IClassFixture<TwinEvidenceEstateFixture>

    member private _.Column (connStr: string) (sql: string) : Task<string list> =
        task {
            use cnn = new SqlConnection(connStr)
            do! cnn.OpenAsync()
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sql
            use! reader = cmd.ExecuteReaderAsync()
            let values = System.Collections.Generic.List<string>()
            let mutable more = true
            while more do
                let! has = reader.ReadAsync()
                if has then values.Add(reader.GetValue(0) |> string) else more <- false
            return List.ofSeq values
        }

    [<Fact>]
    member this.``M3: import → derive → mint re-profiles to the source's shape`` () : Task =
        task {
            // A LOGICAL source: the estate's own schema published to a
            // throwaway database, populated with a known skewed corpus.
            let! handle = Deploy.acquireContainer ()
            let sourceDb = System.String.Concat("TwinEvidenceSrc_", System.Guid.NewGuid().ToString("N").Substring(0, 10))
            try
                match EstateFiles.resolve fixture.Root fixture.Config.Estate with
                | Error es -> failwithf "estate resolve refused: %A" (es |> List.map (fun e -> e.Code))
                | Ok estate ->
                    match EstateModel.buildDacpac estate with
                    | Error es -> failwithf "dacpac refused: %A" (es |> List.map (fun e -> e.Code))
                    | Ok dacpac ->
                        let! published = EstateModel.publishTo handle.MasterConnectionString sourceDb dacpac
                        match published with
                        | Error es -> failwithf "source publish refused: %A" (es |> List.map (fun e -> e.Code, e.Message))
                        | Ok () ->
                            let builder = SqlConnectionStringBuilder handle.MasterConnectionString
                            builder.InitialCatalog <- sourceDb
                            let sourceConn = builder.ConnectionString
                            // Seed the source: statuses, then 50 customers whose
                            // Name vocabulary is low-cardinality and skewed
                            // (Alpha 45 / Beta 15) and whose emails are 60 distinct —
                            // past the cardinality threshold, never to be re-emitted.
                            use cnn = new SqlConnection(sourceConn)
                            do! cnn.OpenAsync()
                            do! Deploy.executeBatch cnn (EstateDefinition.staticData estate |> List.head |> fun f -> f.Content)
                            do! Deploy.executeBatch cnn
                                    """
DECLARE @i INT = 1;
WHILE @i <= 60
BEGIN
    INSERT INTO [dbo].[Customer] ([Name], [Email], [StatusId], [CreatedOn])
    VALUES (CASE WHEN @i <= 45 THEN N'Alpha' ELSE N'Beta' END,
            CONCAT(N'secret', @i, N'@source.example'),
            1 + (@i % 3),
            DATEADD(DAY, @i, '2025-06-01'));
    SET @i = @i + 1;
END
"""
                            // The evidence-configured twin.json variant.
                            System.Environment.SetEnvironmentVariable(SourceConnVar, sourceConn)
                            let configJson =
                                fixture.ConfigJson.Replace(
                                    "\"seed\": 7,",
                                    System.String.Concat(
                                        "\"seed\": 7,\n  \"evidence\": { \"shape\": \"twin/evidence.shape.json\", \"rich\": \"twin/evidence.rich.json\",\n",
                                        "    \"sources\": [ { \"name\": \"src\", \"rendition\": \"logical\", \"conn\": \"env:", SourceConnVar, "\",\n",
                                        "      \"tables\": [\"dbo.Customer\"] } ] },"))
                            fixture.Rewrite "twin.json" configJson
                            let config =
                                match TwinConfig.parse configJson with
                                | Ok c -> c
                                | Error es -> failwithf "evidence config refused: %A" (es |> List.map (fun e -> e.Code, e.Metadata))

                            // Import → the rich pack.
                            let! imported = EvidenceImport.importAll fixture.Root config
                            match imported with
                            | Error es -> failwithf "import refused: %A" (es |> List.map (fun e -> e.Code, e.Message))
                            | Ok report ->
                                Assert.Equal(1, List.length report.Sources)
                                Assert.True(System.IO.File.Exists report.RichPath)
                                let richJson = System.IO.File.ReadAllText report.RichPath
                                Assert.Contains("Alpha", richJson)

                                // Derive → the shape pack; law 3 at the file grain.
                                let! derived = EvidenceImport.derive fixture.Root config
                                match derived with
                                | Error es -> failwithf "derive refused: %A" (es |> List.map (fun e -> e.Code))
                                | Ok shapePath ->
                                    let shapeJson = System.IO.File.ReadAllText shapePath
                                    Assert.DoesNotContain("Alpha", shapeJson)
                                    Assert.DoesNotContain("secret", shapeJson)
                                    Assert.Contains("rowCount", shapeJson)

                                    // Mint with the rich evidence: Customer rides the
                                    // observed volume (50) and the preserved low-card
                                    // Name vocabulary, skewed to Alpha; the
                                    // high-cardinality source emails never re-emit.
                                    let! outcome = Runs.seed fixture.Root config TwinConfig.BaselineScenario
                                    match outcome with
                                    | Error es -> failwithf "evidence mint refused: %A" (es |> List.map (fun e -> e.Code, e.Message))
                                    | Ok _ ->
                                        let twinConn = fixture.TwinConnectionString
                                        let! names = this.Column twinConn "SELECT [Name] FROM [dbo].[Customer];"
                                        Assert.Equal(60, List.length names)
                                        Assert.True(names |> List.forall (fun n -> n = "Alpha" || n = "Beta"),
                                                    "a minted Name fell outside the preserved vocabulary")
                                        let alpha = names |> List.filter (fun n -> n = "Alpha") |> List.length
                                        Assert.True(alpha > 30, sprintf "the 45:15 skew must dominate (alpha=%d)" alpha)
                                        let! emails = this.Column twinConn "SELECT [Email] FROM [dbo].[Customer];"
                                        Assert.True(emails |> List.forall (fun e -> not (e.Contains "@source.example")),
                                                    "a real high-cardinality source email was re-emitted")

                                        // The coverage board.
                                        use twinCnn = new SqlConnection(twinConn)
                                        do! twinCnn.OpenAsync()
                                        let! catalog = Readback.readSchema twinCnn
                                        match catalog with
                                        | Error es -> failwithf "readback refused: %A" (es |> List.map (fun e -> e.Code))
                                        | Ok catalog ->
                                            let verify = EvidenceImport.verify fixture.Root config catalog
                                            Assert.True(verify.RichPresent)
                                            Assert.True(verify.ShapePresent)
                                            Assert.Empty verify.Problems
                                            let customer = verify.Coverage |> List.find (fun c -> c.Table = "dbo.Customer")
                                            Assert.Equal("rich", customer.Tier)
                                            Assert.True(customer.EvidencedColumns > 0)
                                            let order = verify.Coverage |> List.find (fun c -> c.Table = "dbo.Order")
                                            Assert.Equal("none", order.Tier)
            finally
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
