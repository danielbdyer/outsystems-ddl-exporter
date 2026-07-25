namespace Projection.Tests

// The deploy-feasibility gate's LIVE witness — the systematic closure of the
// hand-run probe that motivated `bridgeKeyDeclaredUnique`: SQL Server refuses
// `REFERENCES <table>(<col>)` without a declared single-column unique
// index/constraint ("Could not create constraint or index"), a rejection the
// IR cannot see. The laws pinned here, on a real disposable database:
//   - the fixed point is ordering-blind: a child-first bundle (FK before its
//     parent table) applies fully — file order can never manufacture a false
//     rejection;
//   - the FK-to-non-unique-column bundle is REJECTED with the server's own
//     error, named to its file and batch; the same bundle with the UNIQUE
//     constraint declared is green.
// Serial via the Docker-SqlServer collection.

open Xunit
open Projection.Pipeline
open Projection.Tests.Fixtures

[<Xunit.Collection("Docker-SqlServer")>]
module DeployFeasibilityDockerTests =

    let private skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else printfn "SKIP %s: Docker daemon not reachable." label; false

    [<Fact>]
    let ``feasibility: a child-first bundle applies at the fixed point (ordering-blind, live)`` () =
        let label = "DeployFeasOrder"
        if not (skipIfNoDocker label) then () else
        TaskSync.run (fun () ->
            task {
                let files =
                    [ // Deliberately worst-case order: the FK arrives before its parent exists.
                      "Modules/Core/dbo.CHILD.sql",
                      "CREATE TABLE [dbo].[FEAS_CHILD] ([ID] INT NOT NULL PRIMARY KEY, [PARENTREF] INT NULL);\nGO\nALTER TABLE [dbo].[FEAS_CHILD] ADD CONSTRAINT [FK_FEAS] FOREIGN KEY ([PARENTREF]) REFERENCES [dbo].[FEAS_PARENT]([ID]);\nGO\n"
                      "Modules/Core/dbo.PARENT.sql",
                      "CREATE TABLE [dbo].[FEAS_PARENT] ([ID] INT NOT NULL PRIMARY KEY);\nGO\n" ]
                let! report = Deploy.withBootstrappedDatabase label "SELECT 1;" (fun cnn -> DeployFeasibility.applyBundle cnn files)
                Assert.True(DeployFeasibility.Report.green report, sprintf "expected green, got %A" report.Findings)
                Assert.Equal(3, report.BatchesApplied)
            })

    [<Fact>]
    let ``feasibility: the FK-to-non-unique-column bundle is REJECTED live; declaring the unique constraint turns it green`` () =
        let label = "DeployFeasUnique"
        if not (skipIfNoDocker label) then () else
        TaskSync.run (fun () ->
            task {
                let bundle (bridgeDdl: string) =
                    [ "Modules/Core/dbo.BRIDGE.sql", bridgeDdl
                      "Modules/Core/dbo.CHILD2.sql",
                      "CREATE TABLE [dbo].[FEAS_CHILD2] ([ID] INT NOT NULL PRIMARY KEY, [PARTYREF] INT NULL);\nGO\nALTER TABLE [dbo].[FEAS_CHILD2] ADD CONSTRAINT [FK_FEAS2] FOREIGN KEY ([PARTYREF]) REFERENCES [dbo].[FEAS_BRIDGE]([PARTYREF]);\nGO\n" ]
                // No declared uniqueness on the referenced column — the exact
                // green-gate/red-deploy shape the gate exists to catch.
                let nonUnique =
                    "CREATE TABLE [dbo].[FEAS_BRIDGE] ([ID] INT IDENTITY(1,1) NOT NULL PRIMARY KEY, [PARTYREF] INT NOT NULL);\nGO\n"
                let! rejected =
                    Deploy.withBootstrappedDatabase (label + "A") "SELECT 1;" (fun cnn ->
                        DeployFeasibility.applyBundle cnn (bundle nonUnique))
                Assert.False(DeployFeasibility.Report.green rejected)
                let finding = List.exactlyOne rejected.Findings
                Assert.Equal("Modules/Core/dbo.CHILD2.sql", finding.File)
                Assert.Contains("constraint", finding.Error.ToLowerInvariant())
                // The SAME bundle with the unique constraint declared: green.
                let unique =
                    "CREATE TABLE [dbo].[FEAS_BRIDGE] ([ID] INT IDENTITY(1,1) NOT NULL PRIMARY KEY, [PARTYREF] INT NOT NULL, CONSTRAINT [UQ_FEAS_BRIDGE] UNIQUE ([PARTYREF]));\nGO\n"
                let! green =
                    Deploy.withBootstrappedDatabase (label + "B") "SELECT 1;" (fun cnn ->
                        DeployFeasibility.applyBundle cnn (bundle unique))
                Assert.True(DeployFeasibility.Report.green green, sprintf "expected green, got %A" green.Findings)
            })
