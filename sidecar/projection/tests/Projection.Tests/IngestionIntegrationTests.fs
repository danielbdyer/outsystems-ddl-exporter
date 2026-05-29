namespace Projection.Tests

// Docker-gated integration test for the Slice-B `Ingestion` adapter: lift
// a Source substrate's rows back into `StaticRow`s per kind in
// topological order, then compose with the pure `DataLoadPlan.build`.
// Serial via the Docker-SqlServer collection. Per the test-runner
// discipline, the blocking wait is routed through `TaskSync.run`.

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Adapters.Sql
open Projection.Pipeline

module private IngestionFixtures =

    let mustOk r = match r with Ok v -> v | Error es -> failwithf "fixture: %A" es
    let mkKey (parts: string list) : SsKey = SsKey.synthesizedComposite "OS_TEST_INGEST" parts |> mustOk
    let mkName (s: string) : Name = Name.create s |> mustOk

    let skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else printfn "SKIP %s: Docker daemon not reachable." label; false

    let itemsKey = mkKey ["Items"]

    // Mirrors the proven LiveProfiler fixture shape (column order =
    // attribute order, which `ReadSide.readRowsStream` reads positionally).
    let itemsKind : Kind =
        let idAttr =
            { Attribute.create (mkKey ["Items"; "Id"]) (mkName "Id") Integer with
                Column = { ColumnName = "ID"; IsNullable = false }
                IsPrimaryKey = true
                IsMandatory  = true }
        let nameAttr =
            { Attribute.create (mkKey ["Items"; "Name"]) (mkName "Name") Text with
                Column = { ColumnName = "NAME"; IsNullable = true }
                Length = Some 50
                IsMandatory = false }
        let codeAttr =
            { Attribute.create (mkKey ["Items"; "Code"]) (mkName "Code") Text with
                Column = { ColumnName = "CODE"; IsNullable = false }
                Length = Some 10
                IsMandatory = true }
        { Kind.create itemsKey (mkName "Items")
            { Schema = "dbo"; Table = "OSUSR_ING_ITEMS"; Catalog = None }
            [ idAttr; nameAttr; codeAttr ]
          with References = []; Indexes = []; ColumnChecks = [] }

    let catalog : Catalog =
        { Modules =
            [ { SsKey = mkKey ["Module"]
                Name  = mkName "M"
                Kinds = [ itemsKind ]
                IsActive = true
                ExtendedProperties = [] } ]
          Sequences = [] }

    let schemaSql =
        "CREATE TABLE [dbo].[OSUSR_ING_ITEMS] (" +
        "[ID] INT NOT NULL PRIMARY KEY, [NAME] NVARCHAR(50) NULL, [CODE] NVARCHAR(10) NOT NULL);"

    let seedSql =
        "INSERT INTO [dbo].[OSUSR_ING_ITEMS] ([ID], [NAME], [CODE]) VALUES " +
        "(1, N'alpha', N'A1'), (2, NULL, N'A2'), (3, N'gamma', N'A3');"


[<Xunit.Collection("Docker-SqlServer")>]
type IngestionIntegrationTests(fixture: EphemeralContainerFixture) =
    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``Ingestion.collectInOrder lifts Source rows per kind; composes with DataLoadPlan`` () =
        if not (IngestionFixtures.skipIfNoDocker "ingestion-collect-in-order") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "Ingest" (fun cnn _ ->
                task {
                    do! Deploy.executeBatch cnn IngestionFixtures.schemaSql
                    do! Deploy.executeBatch cnn IngestionFixtures.seedSql

                    let topoLineage : Lineage<TopologicalOrder> =
                        TopologicalOrderPass.runWith TreatAsCycle IngestionFixtures.catalog
                    let topo : TopologicalOrder = topoLineage.Value

                    let! rowsByKind = Ingestion.collectInOrder cnn IngestionFixtures.catalog topo

                    // Seam 2: the kind's rows were lifted.
                    let itemRows =
                        Map.tryFind IngestionFixtures.itemsKey rowsByKind |> Option.defaultValue []
                    Assert.Equal(3, itemRows.Length)

                    // Values are keyed by logical attribute name; the Code
                    // column round-trips its three distinct values.
                    let codes =
                        itemRows
                        |> List.choose (fun r -> Map.tryFind (IngestionFixtures.mkName "Code") r.Values)
                        |> Set.ofList
                    Assert.Equal<Set<string>>(Set.ofList [ "A1"; "A2"; "A3" ], codes)

                    // Seam 1 + 2 compose: the ingested rows flow into the plan,
                    // which classifies the (non-identity) PK as PreservedFromSource.
                    let plan = DataLoadPlan.build IngestionFixtures.catalog topo rowsByKind SurrogateRemapContext.empty
                    let load = plan.Loads |> List.find (fun l -> l.Kind = IngestionFixtures.itemsKey)
                    Assert.Equal(3, load.Rows.Length)
                    Assert.Equal(IdentityDisposition.PreservedFromSource, load.Disposition)
                    Assert.True(DataLoadPlan.isSatisfiable plan)
                }))
