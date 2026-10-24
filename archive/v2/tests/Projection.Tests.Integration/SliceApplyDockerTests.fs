namespace Projection.Tests

// Docker-gated end-to-end witness for the LIVE golden apply (`slice-apply --go`
// → `Transfer.runGoldenApply`). Where `SliceApplyTests` pins the pure emit, this
// proves the write actually executes: a golden mapped onto a real target schema
// is applied through the capture-and-hoist write path, and the rows land — FKs
// resolved, in topological order.

open Microsoft.Data.SqlClient
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Targets.Json

[<Xunit.Collection("Docker-SqlServer")>]
type SliceApplyDockerTests(fixture: EphemeralContainerFixture) =

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

    let nm (s: string) : Name = Name.create s |> mustOk

    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``slice-apply live lands the golden rows in the target with FKs resolved`` () =
        if not (skipIfNoDocker "slice-apply-live") then () else
        let userCount, orderRegion =
            fixture.WithEphemeralDatabase "SliceApplyLive" (fun cnn connStr ->
                task {
                    // Empty target schema (a fresh environment to populate). The
                    // three-level FK chain; PKs are non-IDENTITY so the golden's
                    // keys are preserved (the structural write), keeping the
                    // assertion simple.
                    do! Deploy.executeBatch cnn
                            ("CREATE TABLE [dbo].[OSUSR_AP_COUNTRY] (" +
                             "[ID] INT NOT NULL PRIMARY KEY, [NAME] NVARCHAR(50) NOT NULL);")
                    do! Deploy.executeBatch cnn
                            ("CREATE TABLE [dbo].[OSUSR_AP_USER] (" +
                             "[ID] INT NOT NULL PRIMARY KEY, [COUNTRY_ID] INT NOT NULL " +
                             "CONSTRAINT [FK_AP_USER_COUNTRY] REFERENCES [dbo].[OSUSR_AP_COUNTRY]([ID]));")
                    do! Deploy.executeBatch cnn
                            ("CREATE TABLE [dbo].[OSUSR_AP_ORDER] (" +
                             "[ID] INT NOT NULL PRIMARY KEY, [USER_ID] INT NOT NULL " +
                             "CONSTRAINT [FK_AP_ORDER_USER] REFERENCES [dbo].[OSUSR_AP_USER]([ID]), " +
                             "[REGION] NVARCHAR(20) NOT NULL);")

                    let! catR = Ref.resolveCatalog (Ref.parse ("live:" + connStr))
                    let catalog = mustOk catR

                    // The golden (logical; entity = physical table name for the
                    // raw-DB readback). Order 1000 → user 100 → country 10.
                    let golden : GoldenDataset =
                        { Version = 1
                          Entities =
                            [ { Entity = "OSUSR_AP_COUNTRY"; Rows = [ StaticRow.presentValues [ nm "ID", "10"; nm "NAME", "US" ] ] }
                              { Entity = "OSUSR_AP_USER";    Rows = [ StaticRow.presentValues [ nm "ID", "100"; nm "COUNTRY_ID", "10" ] ] }
                              { Entity = "OSUSR_AP_ORDER"
                                Rows = [ StaticRow.presentValues [ nm "ID", "1000"; nm "USER_ID", "100"; nm "REGION", "West" ] ] } ] }

                    let rows = SliceApplyRun.mapToTarget catalog golden |> mustOk
                    let! reportR = Transfer.runGoldenApply Transfer.Mode.Execute false cnn catalog rows
                    let report = mustOk reportR
                    // No unresolved orphans — the slice was referentially self-contained.
                    Assert.Empty(report.SkippedReferences)

                    use cmd = cnn.CreateCommand()
                    cmd.CommandText <- "SELECT COUNT(*) FROM [dbo].[OSUSR_AP_USER]"
                    let! uc = cmd.ExecuteScalarAsync()
                    use cmd2 = cnn.CreateCommand()
                    cmd2.CommandText <- "SELECT [REGION] FROM [dbo].[OSUSR_AP_ORDER] WHERE [ID] = 1000"
                    let! reg = cmd2.ExecuteScalarAsync()
                    return System.Convert.ToInt32 uc, string reg
                })
            |> fun t -> t.GetAwaiter().GetResult()
        Assert.Equal(1, userCount)       // the closed user landed (FK to country resolved)
        Assert.Equal("West", orderRegion) // the root order landed with its data
