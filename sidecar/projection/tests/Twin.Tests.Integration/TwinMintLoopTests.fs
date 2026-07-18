module Twin.Tests.Integration.TwinMintLoopTests

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Xunit
open Projection.Core
open Twin.Core
open Twin.Runtime
open Twin.Tests.Integration

// ---------------------------------------------------------------------------
// The M2 mint loop against a real container: the estate's own reference
// data stands (K1 pools), FK integrity holds with zero orphans, and the
// mint is deterministic per seed (T1 at the twin's grain).
// ---------------------------------------------------------------------------

[<Collection("Twin-Docker")>]
type TwinMintLoopTests (fixture: TwinMintEstateFixture) =

    interface IClassFixture<TwinMintEstateFixture>

    member private _.Up () : Task<unit> =
        task {
            let! outcome = Runs.up fixture.Root fixture.Config TwinConfig.BaselineScenario false
            match outcome with
            | Ok _ -> return ()
            | Error es -> return failwithf "up refused: %A" (es |> List.map (fun e -> e.Code, e.Message))
        }

    member private _.Seed () : Task<unit> =
        task {
            let! outcome = Runs.seed fixture.Root fixture.Config TwinConfig.BaselineScenario
            match outcome with
            | Ok _ -> return ()
            | Error es -> return failwithf "seed refused: %A" (es |> List.map (fun e -> e.Code, e.Message))
        }

    member private _.Scalar (sql: string) : Task<int64> =
        task {
            use cnn = new SqlConnection(fixture.TwinConnectionString)
            do! cnn.OpenAsync()
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sql
            let! v = cmd.ExecuteScalarAsync()
            return System.Convert.ToInt64 v
        }

    member private _.Column (sql: string) : Task<string list> =
        task {
            use cnn = new SqlConnection(fixture.TwinConnectionString)
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
    member this.``K1 + L1: the estate's reference data stands and every relationship resolves`` () : Task =
        task {
            do! this.Up()

            // The lane's rows are exactly the estate's own — never wiped,
            // never regenerated (K1).
            let! statusRows = this.Scalar "SELECT COUNT(*) FROM [dbo].[Status];"
            Assert.Equal(3L, statusRows)
            let! statusNames = this.Column "SELECT [Name] FROM [dbo].[Status] ORDER BY [Id];"
            Assert.Equal<string list>([ "Open"; "Closed"; "Pending" ], statusNames)

            // Synthetic mass landed.
            let! customers = this.Scalar "SELECT COUNT(*) FROM [dbo].[Customer];"
            let! orders = this.Scalar "SELECT COUNT(*) FROM [dbo].[Order];"
            let! lines = this.Scalar "SELECT COUNT(*) FROM [dbo].[OrderLine];"
            Assert.Equal(25L, customers)
            Assert.Equal(25L, orders)
            Assert.Equal(25L, lines)

            // Zero orphans, at the database's own grain — the FK draws
            // resolved against real parents, including the lane-seeded
            // Status pool.
            let! customerStatusOrphans =
                this.Scalar "SELECT COUNT(*) FROM [dbo].[Customer] c LEFT JOIN [dbo].[Status] s ON s.[Id] = c.[StatusId] WHERE s.[Id] IS NULL;"
            let! orderCustomerOrphans =
                this.Scalar "SELECT COUNT(*) FROM [dbo].[Order] o LEFT JOIN [dbo].[Customer] c ON c.[Id] = o.[CustomerId] WHERE c.[Id] IS NULL;"
            let! lineOrderOrphans =
                this.Scalar "SELECT COUNT(*) FROM [dbo].[OrderLine] l LEFT JOIN [dbo].[Order] o ON o.[Id] = l.[OrderId] WHERE o.[Id] IS NULL;"
            Assert.Equal(0L, customerStatusOrphans)
            Assert.Equal(0L, orderCustomerOrphans)
            Assert.Equal(0L, lineOrderOrphans)

            // Every FK constraint is trusted or at least consistent: a
            // DBCC-style probe via untrusted counts would be overreach
            // here; the joins above are the law's own statement.
            ()
        }

    [<Fact>]
    member this.``T1: the same seed mints the same rows; the mint is reproducible`` () : Task =
        task {
            do! this.Up()
            let! firstOrders =
                this.Column "SELECT CONCAT([Id], '|', [CustomerId], '|', [StatusId], '|', [Total]) FROM [dbo].[Order] ORDER BY [Id];"
            Assert.Equal(25, List.length firstOrders)

            // Re-mint with everything unchanged — seed forces the mint even
            // when the fingerprint matches.
            do! this.Seed()
            let! secondOrders =
                this.Column "SELECT CONCAT([Id], '|', [CustomerId], '|', [StatusId], '|', [Total]) FROM [dbo].[Order] ORDER BY [Id];"
            Assert.Equal<string list>(firstOrders, secondOrders)
        }

    [<Fact>]
    member this.``M4: a scenario shapes the mint — weights, window, ratio, and pins land`` () : Task =
        task {
            let! outcome = Runs.seed fixture.Root fixture.Config "skewed"
            match outcome with
            | Ok _ -> ()
            | Error es -> failwithf "seed --scenario skewed refused: %A" (es |> List.map (fun e -> e.Code, e.Message))

            // Volumes: explicit rows + the per-parent derivation (2.0 × 40).
            let! orders = this.Scalar "SELECT COUNT(*) FROM [dbo].[Order];"
            let! lines = this.Scalar "SELECT COUNT(*) FROM [dbo].[OrderLine];"
            Assert.Equal(40L, orders)
            Assert.Equal(80L, lines)

            // Weights: the vocabulary is exactly the declared one, skewed to Web.
            let! channels = this.Column "SELECT [Channel] FROM [dbo].[Order];"
            Assert.True(channels |> List.forall (fun c -> c = "Web" || c = "Store"),
                        "a minted Channel fell outside the declared vocabulary")
            let web = channels |> List.filter (fun c -> c = "Web") |> List.length
            let store = channels |> List.filter (fun c -> c = "Store") |> List.length
            Assert.True(web > store, sprintf "the 8:2 weighting must dominate (web=%d store=%d)" web store)

            // The date window (K2): every PlacedOn inside it; late skew puts the
            // median in the window's upper half.
            let! placed = this.Column "SELECT CONVERT(varchar(33), [PlacedOn], 126) FROM [dbo].[Order] ORDER BY [PlacedOn];"
            let parsed = placed |> List.map (fun t -> System.DateTime.Parse(t, System.Globalization.CultureInfo.InvariantCulture))
            let lo = System.DateTime(2026, 1, 1)
            let hi = System.DateTime(2026, 3, 31)
            Assert.True(parsed |> List.forall (fun d -> d >= lo && d <= hi), "a PlacedOn fell outside the window")
            let median = parsed.[parsed.Length / 2]
            let midpoint = lo.AddTicks((hi - lo).Ticks / 2L)
            Assert.True(median > midpoint, "late skew must push the median past the midpoint")

            // The pin: verbatim, present, and referenceable (K1b widened the pool).
            let! pinned = this.Column "SELECT [Name] FROM [dbo].[Customer] WHERE [Id] = 1000;"
            Assert.Equal<string list>([ "Canonical Test Customer" ], pinned)
            let! pinnedEmail = this.Column "SELECT [Email] FROM [dbo].[Customer] WHERE [Id] = 1000;"
            Assert.Equal<string list>([ "canon@example.test" ], pinnedEmail)

            // And the whole shaped mint still lands with zero orphans.
            let! orphans =
                this.Scalar "SELECT COUNT(*) FROM [dbo].[Order] o LEFT JOIN [dbo].[Customer] c ON c.[Id] = o.[CustomerId] WHERE c.[Id] IS NULL;"
            Assert.Equal(0L, orphans)
        }

    [<Fact>]
    member _.``M5: the proof verb holds on a throwaway database`` () : Task =
        task {
            let! report = Check.run fixture.Root fixture.Config "skewed"
            match report with
            | Error es -> failwithf "check refused: %A" (es |> List.map (fun e -> e.Code, e.Message))
            | Ok r ->
                Assert.Equal(r.TablesDefined, r.TablesLive)
                Assert.Equal(1, r.LanesApplied)
                Assert.True(r.TotalRows > 0L)
                Assert.Equal(0L, r.OrphanRows)
                Assert.True(r.DeterministicRemint, "the re-mint must be byte-identical")
                Assert.Empty r.Findings
        }
