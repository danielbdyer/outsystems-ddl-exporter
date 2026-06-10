module Projection.Tests.DacpacPublishEquivalenceTests

// The dacpac-publish equivalence witness — the live companion to the
// in-memory `DacpacRoundTripTests`. Per the documented decision boundary
// (`DacpacEmitter` header; `WAVE_6_ONTOLOGY.md` §4 "the imperative schema
// ALTER is not the deploy artifact"): the `.dacpac` is the DECLARATIVE
// model whose deployment plan DacFx computes at publish; the SSDT bundle
// is the production deploy artifact realized by V2's own executor. Two
// product paths, one Catalog — they must stand up the SAME physical
// schema. No prior test deploys the package through `DacServices`; this
// witness closes that leg:
//
//   source DDL → ReadSide (the catalog)
//     leg 1: emitted bundle  → Deploy.executeBatch → ReadSide → P₁
//     leg 2: emitted .dacpac → DacServices.Deploy  → ReadSide → P₂
//   assert P₁ = P₂ on `PhysicalSchema` (deployed-vs-deployed, so each
//   leg's shared erasures cancel; a residual diff is a REAL publish
//   divergence between the two paths).

open System.IO
open Xunit
open Microsoft.Data.SqlClient
open Microsoft.SqlServer.Dac
open Projection.Core
open Projection.Pipeline
open Projection.Adapters.Sql
open Projection.Targets.SSDT

[<RequireQualifiedAccess>]
module private DacpacEqFixtures =

    let value (r: Result<'a>) : 'a = Result.value r

    let skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else printfn "SKIP %s: Docker daemon not reachable." label; false

    /// OutSystems-shaped source: IDENTITY PKs, a NOT-NULL FK with a named
    /// constraint, nullable + NOT-NULL text, a unique index — the envelope
    /// both publish paths must reproduce identically.
    let sourceDdl : string =
        "CREATE TABLE [dbo].[OSUSR_DQ_CUSTOMER] (" +
        "[ID] INT IDENTITY(1,1) NOT NULL PRIMARY KEY, " +
        "[NAME] NVARCHAR(50) NOT NULL, " +
        "[EMAIL] NVARCHAR(250) NULL); " +
        "CREATE UNIQUE INDEX [IX_OSUSR_DQ_CUSTOMER_NAME] ON [dbo].[OSUSR_DQ_CUSTOMER]([NAME]); " +
        "CREATE TABLE [dbo].[OSUSR_DQ_ORDER] (" +
        "[ID] INT IDENTITY(1,1) NOT NULL PRIMARY KEY, " +
        "[CUSTOMER_ID] INT NOT NULL, " +
        "[AMOUNT] INT NULL, " +
        "CONSTRAINT [FK_DQ_ORDER_CUSTOMER] FOREIGN KEY ([CUSTOMER_ID]) " +
        "REFERENCES [dbo].[OSUSR_DQ_CUSTOMER]([ID]));"

[<Xunit.Collection("Docker-SqlServer")>]
type DacpacPublishEquivalenceTests(fixture: EphemeralContainerFixture) =
    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``L3-S2: the published dacpac and the deployed bundle stand up the same physical schema`` () =
        if not (DacpacEqFixtures.skipIfNoDocker "DacpacEq") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "DacpacEqSeed" (fun seed _ ->
                task {
                    do! Deploy.executeBatch seed DacpacEqFixtures.sourceDdl
                    let! catalogR = ReadSide.read seed
                    let catalog = DacpacEqFixtures.value catalogR
                    let bundleSql = SsdtDdlEmitter.statements catalog |> Render.toText
                    let dacpacBytes = DacpacEmitter.emit catalog |> DacpacEqFixtures.value
                    return!
                        fixture.WithEphemeralDatabase "DacpacEqBundle" (fun bundleCnn _ ->
                            task {
                                do! Deploy.executeBatch bundleCnn bundleSql
                                let! bundleBackR = ReadSide.read bundleCnn
                                let bundleBack = DacpacEqFixtures.value bundleBackR
                                return!
                                    fixture.WithEphemeralDatabase "DacpacEqPublish" (fun dacCnn dacConnStr ->
                                        task {
                                            // Publish the package into the (empty) ephemeral
                                            // database — DacFx computes and applies the
                                            // deployment plan (the declarative leg's realization).
                                            let dbName = SqlConnectionStringBuilder(dacConnStr).InitialCatalog
                                            use stream = new MemoryStream(dacpacBytes)
                                            use package = DacPackage.Load stream
                                            let services = DacServices(dacConnStr)
                                            services.Deploy(package, dbName, true, DacDeployOptions())
                                            let! dacBackR = ReadSide.read dacCnn
                                            let dacBack = DacpacEqFixtures.value dacBackR
                                            let diff =
                                                PhysicalSchema.diff
                                                    (PhysicalSchema.ofCatalog bundleBack)
                                                    (PhysicalSchema.ofCatalog dacBack)
                                            Assert.True(
                                                PhysicalSchema.isEqual diff,
                                                sprintf
                                                    "dacpac publish and bundle deploy disagree on the physical schema:\n%s"
                                                    (PhysicalSchema.renderDiff diff))
                                        })
                            })
                }))
