module Twin.Tests.Integration.TwinExternalServerTests

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Xunit
open Projection.Core
open Projection.Pipeline
open Twin.Core
open Twin.Runtime
open Twin.Tests.Integration

// ---------------------------------------------------------------------------
// C8 — the existing-server seam, live: the same estate, the substrate an
// EXISTING server (the warm acquisition standing in for LocalDB or a
// Developer-edition instance). `seed` publishes and mints into the named
// database; `status` reports without managing; `down` is the named no-op;
// `reset` drops only the twin database and the server stands.
// ---------------------------------------------------------------------------

/// Estate files only — the fixture's managed container is never started;
/// every config under test swaps the substrate to an existing server.
type TwinExternalEstateFixture () =
    inherit TwinEstateFixture ("twin-e2e-external", 21945)

[<Collection("Twin-Docker")>]
type TwinExternalServerTests (fixture: TwinExternalEstateFixture) =

    let ConnVar = "TWIN_E2E_EXTERNAL_CONN"
    let BadConnVar = "TWIN_E2E_EXTERNAL_BAD_CONN"
    let UnsetVar = "TWIN_E2E_EXTERNAL_UNSET_CONN"

    /// The fixture config with its container section swapped for a server
    /// section riding `connVar` (and, when given, a named twin database).
    let externalConfig (connVar: string) (database: string option) : TwinConfig =
        let serverSection =
            match database with
            | Some db ->
                System.String.Concat("\"server\": { \"conn\": \"env:", connVar, "\", \"database\": \"", db, "\" },")
            | None ->
                System.String.Concat("\"server\": { \"conn\": \"env:", connVar, "\" },")
        let json =
            fixture.ConfigJson.Replace(
                "\"container\": { \"name\": \"twin-e2e-external\", \"port\": 21945 },",
                serverSection)
        match TwinConfig.parse json with
        | Ok c ->
            Assert.True(c.Server.IsSome, "the substrate swap did not take — the config still rides the container")
            c
        | Error es -> failwithf "external config refused: %A" (es |> List.map (fun e -> e.Code, e.Metadata))

    interface IClassFixture<TwinExternalEstateFixture>

    member private _.Count (connStr: string) (sql: string) : Task<int> =
        task {
            use cnn = new SqlConnection(connStr)
            do! cnn.OpenAsync()
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sql
            let! result = cmd.ExecuteScalarAsync()
            return System.Convert.ToInt32 result
        }

    [<Fact>]
    member this.``C8: the full loop on an existing server — seed, status, the down no-op, the database-only reset`` () : Task =
        task {
            let! handle = Deploy.acquireContainer ()
            let twinDb = System.String.Concat("TwinExt_", System.Guid.NewGuid().ToString("N").Substring(0, 10))
            let dbCountSql =
                System.String.Concat("SELECT COUNT(*) FROM sys.databases WHERE [name] = N'", twinDb, "';")
            try
                System.Environment.SetEnvironmentVariable(ConnVar, handle.MasterConnectionString)
                let config = externalConfig ConnVar (Some twinDb)

                // Seed: publish + mint land in the named database on the
                // existing server; the twin never provisions the engine.
                let! seeded = Runs.seed fixture.Root config TwinConfig.BaselineScenario
                match seeded with
                | Error es -> failwithf "external seed refused: %A" (es |> List.map (fun e -> e.Code, e.Message))
                | Ok (Runs.NothingToApply _) -> failwith "a first seed cannot be a no-op"
                | Ok (Runs.Materialized r) ->
                    Assert.True(r.SchemaPublished, "the first seed publishes the schema")
                    Assert.True(r.TotalRows > 0L, "the mint landed no rows")

                // The twin database answers on the server, under the configured name.
                let twinConn =
                    let b = SqlConnectionStringBuilder handle.MasterConnectionString
                    b.InitialCatalog <- twinDb
                    b.ConnectionString
                let! customers = this.Count twinConn "SELECT COUNT(*) FROM [dbo].[Customer];"
                Assert.Equal(25, customers)

                // Status: current on both planes, and honest about not managing.
                let! status = Runs.status fixture.Root config TwinConfig.BaselineScenario
                match status with
                | Error es -> failwithf "external status refused: %A" (es |> List.map (fun e -> e.Code))
                | Ok s ->
                    Assert.Equal(TwinContainer.Running, s.Container)
                    Assert.False(s.Managed, "an existing server is never the twin's container")
                    Assert.True s.DatabasePresent
                    Assert.Equal(Some true, s.SchemaCurrent)
                    Assert.Equal(Some true, s.DataCurrent)

                // Down: the named no-op — the server is not the twin's to stop.
                let! downed = Runs.down config
                match downed with
                | Ok ExternalServerLeft -> ()
                | Ok ContainerStopped -> failwith "down stopped a container it does not manage"
                | Error es -> failwithf "external down refused: %A" (es |> List.map (fun e -> e.Code))
                let! alive = this.Count handle.MasterConnectionString "SELECT 1;"
                Assert.Equal(1, alive)
                let! stillThere = this.Count handle.MasterConnectionString dbCountSql
                Assert.Equal(1, stillThere)

                // Reset: ONLY the twin database is dropped; the server stands.
                let! resetOutcome = Runs.reset config
                match resetOutcome with
                | Ok (DatabaseDropped db) -> Assert.Equal(twinDb, db)
                | Ok ContainerRemoved -> failwith "reset removed a container it does not manage"
                | Error es -> failwithf "external reset refused: %A" (es |> List.map (fun e -> e.Code, e.Message))
                let! gone = this.Count handle.MasterConnectionString dbCountSql
                Assert.Equal(0, gone)
                let! serverStands = this.Count handle.MasterConnectionString "SELECT 1;"
                Assert.Equal(1, serverStands)
            finally
                System.Environment.SetEnvironmentVariable(ConnVar, null)
                try
                    use master = new SqlConnection(handle.MasterConnectionString)
                    master.Open()
                    use cmd = master.CreateCommand()
                    cmd.CommandText <-
                        System.String.Concat(
                            "IF DB_ID(N'", twinDb, "') IS NOT NULL BEGIN ALTER DATABASE [", twinDb,
                            "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [", twinDb, "]; END")
                    cmd.ExecuteNonQuery() |> ignore
                with _ -> ()
                (handle.DisposeAsync()).GetAwaiter().GetResult()
        }

    [<Fact>]
    member _.``C8: an unreachable server refuses the write path and reports on the read path`` () : Task =
        task {
            System.Environment.SetEnvironmentVariable(
                BadConnVar,
                "Server=localhost,29999;User Id=sa;Password=placeholder;TrustServerCertificate=True;Connect Timeout=2")
            try
                let config = externalConfig BadConnVar None
                // The write path refuses, named.
                let! up = Runs.up fixture.Root config TwinConfig.BaselineScenario false
                match up with
                | Ok _ -> failwith "up must refuse when the server does not answer"
                | Error es -> Assert.Contains("twin.server.unreachable", es |> List.map (fun e -> e.Code))
                // The read path reports the fact instead of refusing.
                let! status = Runs.status fixture.Root config TwinConfig.BaselineScenario
                match status with
                | Error es -> failwithf "status must report, not refuse: %A" (es |> List.map (fun e -> e.Code))
                | Ok s ->
                    Assert.Equal(TwinContainer.Stopped, s.Container)
                    Assert.False s.Managed
            finally
                System.Environment.SetEnvironmentVariable(BadConnVar, null)
        }

    [<Fact>]
    member _.``C8: an unset connection variable refuses, named`` () : Task =
        task {
            System.Environment.SetEnvironmentVariable(UnsetVar, null)
            let config = externalConfig UnsetVar None
            let! status = Runs.status fixture.Root config TwinConfig.BaselineScenario
            match status with
            | Ok _ -> failwith "status cannot resolve an unset connection variable"
            | Error es -> Assert.Contains("twin.server.connUnset", es |> List.map (fun e -> e.Code))
        }
