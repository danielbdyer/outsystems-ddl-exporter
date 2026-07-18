module Twin.Tests.Integration.TwinSchemaLoopTests

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Xunit
open Projection.Core
open Twin.Core
open Twin.Runtime
open Twin.Tests.Integration

// ---------------------------------------------------------------------------
// THE_TWIN.md law 1 (convergence) + the M1 schema loop, end to end against a
// real container: up materializes, up again is a no-op, an estate edit
// converges, status tells the truth at every step.
// ---------------------------------------------------------------------------

[<Collection("Twin-Docker")>]
type TwinSchemaLoopTests (fixture: TwinSchemaEstateFixture) =

    interface IClassFixture<TwinSchemaEstateFixture>

    member private _.Up () : Task<Runs.UpOutcome> =
        task {
            let! outcome = Runs.up fixture.Root fixture.Config TwinConfig.BaselineScenario false
            match outcome with
            | Ok o -> return o
            | Error es -> return failwithf "up refused: %A" (es |> List.map (fun e -> e.Code, e.Message))
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

    [<Fact>]
    member this.``law 1: up materializes, converges, and re-converges after an estate edit`` () : Task =
        task {
            // First up — everything from nothing.
            let! first = this.Up()
            match first with
            | Runs.Materialized r ->
                Assert.True(r.SchemaPublished, "the first up publishes the schema")
                Assert.Equal(1, r.LanesApplied)
                Assert.Equal(4, r.DefinedTables)
                Assert.True(r.ProvidedKinds >= 1, "the lane-seeded Status table is a provided pool")
                Assert.True(r.TotalRows > 0L, "the twin holds rows after the first up")
            | Runs.NothingToApply _ -> failwith "a fresh twin cannot be current"

            // Second up — the fingerprint match is the fast no-op.
            let! second = this.Up()
            match second with
            | Runs.NothingToApply (tables, rows, scenario) ->
                Assert.Equal(4, tables)
                Assert.True(rows > 0L)
                Assert.Equal(TwinConfig.BaselineScenario, scenario)
            | Runs.Materialized _ -> failwith "an unchanged estate must be a no-op"

            // Status agrees.
            let! status = Runs.status fixture.Root fixture.Config TwinConfig.BaselineScenario
            match status with
            | Ok s ->
                Assert.Equal(TwinContainer.Running, s.Container)
                Assert.Equal(Some true, s.SchemaCurrent)
                Assert.Equal(Some true, s.DataCurrent)
            | Error es -> failwithf "status refused: %A" (es |> List.map (fun e -> e.Code))

            // An estate edit — one column widens the Order table.
            fixture.Rewrite "Tables/dbo.Order.sql"
                """CREATE TABLE [dbo].[Order] (
    [Id]         INT           IDENTITY(1,1) NOT NULL,
    [CustomerId] INT           NOT NULL,
    [StatusId]   INT           NOT NULL,
    [Total]      DECIMAL(18,2) NOT NULL,
    [PlacedOn]   DATETIME2     NOT NULL,
    [Channel]    NVARCHAR(20)  NULL,
    CONSTRAINT [PK_Order] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id]),
    CONSTRAINT [FK_Order_Status] FOREIGN KEY ([StatusId]) REFERENCES [dbo].[Status] ([Id])
);
"""
            let! statusStale = Runs.status fixture.Root fixture.Config TwinConfig.BaselineScenario
            match statusStale with
            | Ok s -> Assert.Equal(Some false, s.SchemaCurrent)
            | Error es -> failwithf "status refused: %A" (es |> List.map (fun e -> e.Code))

            let! third = this.Up()
            match third with
            | Runs.Materialized r -> Assert.True(r.SchemaPublished, "an edited estate republishes")
            | Runs.NothingToApply _ -> failwith "an edited estate cannot be a no-op"

            // The new column is live.
            let! channel =
                this.Scalar
                    "SELECT COUNT(*) FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id WHERE t.name = N'Order' AND c.name = N'Channel';"
            Assert.Equal(1L, channel)

            // And the twin is current again.
            let! fourth = this.Up()
            match fourth with
            | Runs.NothingToApply _ -> ()
            | Runs.Materialized _ -> failwith "the converged twin must be a no-op"
        }
