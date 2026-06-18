namespace Projection.Tests

// Docker-gated end-to-end witness for the CAPSTONE: `slice-run` (flow-binding)
// extracting a use-case slice from a SOURCE environment and applying it to a
// TARGET environment in one go (`SliceFlowRun.run`). Two ephemeral databases —
// the cross-environment data-portability outcome. Only the slice's referential
// closure crosses; the out-of-slice rows stay behind.

open Microsoft.Data.SqlClient
open Xunit
open Projection.Core
open Projection.Pipeline

[<Xunit.Collection("Docker-SqlServer")>]
type SliceRunDockerTests(fixture: EphemeralContainerFixture) =

    let skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else
            printfn "SKIP %s: Docker daemon not reachable." label
            false

    let mustOk (r: Result<'a>) : 'a =
        match r with
        | Ok v -> v
        | Error es ->
            let detail = es |> List.map (fun e -> System.String.Concat(e.Code, ": ", e.Message)) |> String.concat " | "
            invalidOp (System.String.Concat("expected Ok; got: ", detail))

    let schema (cnn: SqlConnection) : System.Threading.Tasks.Task =
        task {
            do! Deploy.executeBatch cnn
                    ("CREATE TABLE [dbo].[OSUSR_SF_COUNTRY] (" +
                     "[ID] INT NOT NULL PRIMARY KEY, [NAME] NVARCHAR(50) NOT NULL);")
            do! Deploy.executeBatch cnn
                    ("CREATE TABLE [dbo].[OSUSR_SF_USER] (" +
                     "[ID] INT NOT NULL PRIMARY KEY, [COUNTRY_ID] INT NOT NULL " +
                     "CONSTRAINT [FK_SF_USER_COUNTRY] REFERENCES [dbo].[OSUSR_SF_COUNTRY]([ID]));")
            do! Deploy.executeBatch cnn
                    ("CREATE TABLE [dbo].[OSUSR_SF_ORDER] (" +
                     "[ID] INT NOT NULL PRIMARY KEY, [USER_ID] INT NOT NULL " +
                     "CONSTRAINT [FK_SF_ORDER_USER] REFERENCES [dbo].[OSUSR_SF_USER]([ID]));")
        } :> System.Threading.Tasks.Task

    let scalarInt (cnn: SqlConnection) (sql: string) : System.Threading.Tasks.Task<int> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sql
            let! o = cmd.ExecuteScalarAsync()
            return System.Convert.ToInt32 o
        }

    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``slice-run extracts a closure from source and applies it to target (cross-environment)`` () =
        if not (skipIfNoDocker "slice-run-cross-env") then () else
        let orders, users, countries =
            fixture.WithEphemeralDatabase "SliceRunTarget" (fun tcnn tconn ->
                task {
                    do! schema tcnn   // empty target, same schema
                    return!
                        fixture.WithEphemeralDatabase "SliceRunSource" (fun scnn sconn ->
                            task {
                                do! schema scnn
                                // Source data: two orders, two users, two countries.
                                do! Deploy.executeBatch scnn
                                        ("INSERT INTO [dbo].[OSUSR_SF_COUNTRY] ([ID],[NAME]) VALUES (10,'US'),(20,'CA');")
                                do! Deploy.executeBatch scnn
                                        ("INSERT INTO [dbo].[OSUSR_SF_USER] ([ID],[COUNTRY_ID]) VALUES (100,10),(300,20);")
                                do! Deploy.executeBatch scnn
                                        ("INSERT INTO [dbo].[OSUSR_SF_ORDER] ([ID],[USER_ID]) VALUES (1000,100),(1002,300);")

                                // The slice: order 1000 and its referential closure.
                                let spec =
                                    SliceSpec.create 1
                                        [ { Entity = EntityCoordinate.ofEntity "OSUSR_SF_ORDER"
                                            Predicate = Predicate.Raw "[ID] = 1000" } ]
                                        []
                                    |> mustOk

                                // Run the cross-environment flow LIVE.
                                let! r = SliceFlowRun.run ("live:" + sconn) spec ("live:" + tconn) true false
                                let report = mustOk r
                                Assert.Empty(report.SkippedReferences)

                                let! o = scalarInt tcnn "SELECT COUNT(*) FROM [dbo].[OSUSR_SF_ORDER]"
                                let! u = scalarInt tcnn "SELECT COUNT(*) FROM [dbo].[OSUSR_SF_USER]"
                                let! c = scalarInt tcnn "SELECT COUNT(*) FROM [dbo].[OSUSR_SF_COUNTRY]"
                                return o, u, c
                            })
                })
            |> fun t -> t.GetAwaiter().GetResult()
        // Only order 1000 → user 100 → country 10 crossed; order 1002 / user 300 /
        // country 20 stayed behind (the slice is referentially-closed, key-scoped).
        Assert.Equal(1, orders)
        Assert.Equal(1, users)
        Assert.Equal(1, countries)
