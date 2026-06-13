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
                Column = ColumnRealization.create ("ID") (false) |> Result.value
                IsPrimaryKey = true
                IsMandatory  = true }
        let nameAttr =
            { Attribute.create (mkKey ["Items"; "Name"]) (mkName "Name") Text with
                Column = ColumnRealization.create ("NAME") (true) |> Result.value
                Length = Some 50
                IsMandatory = false }
        let codeAttr =
            { Attribute.create (mkKey ["Items"; "Code"]) (mkName "Code") Text with
                Column = ColumnRealization.create ("CODE") (false) |> Result.value
                Length = Some 10
                IsMandatory = true }
        { Kind.create itemsKey (mkName "Items")
            (TableId.create "dbo" "OSUSR_ING_ITEMS" |> Result.value)
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

    // WP6 step 4 — the full hydration composition end-to-end: a catalog whose
    // Items kind is marked `Static []` (the forward-read shape) is hydrated
    // from the live source by `Hydration.hydrateCatalog` (open a SECOND
    // connection from `model.ossys` → stream the static-marked kinds via
    // Ingestion → graft). Exercised via BOTH `env:` AND `file:` ossys refs —
    // the `file:` form is the operator's predominant one and must hydrate
    // identically. Closes the live-stream gap the pure HydrationTests can't
    // reach.
    [<Fact>]
    member _.``Hydration.hydrateCatalog streams live static rows via BOTH env: and file: ossys refs (WP6 step 4)`` () =
        if not (IngestionFixtures.skipIfNoDocker "hydrate-catalog") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "Hydrate" (fun cnn perDbConn ->
                task {
                    do! Deploy.executeBatch cnn IngestionFixtures.schemaSql
                    do! Deploy.executeBatch cnn IngestionFixtures.seedSql
                    let staticItems = { IngestionFixtures.itemsKind with Modality = [ Static [] ] }
                    let staticCatalog : Catalog =
                        { Modules =
                            [ { SsKey = IngestionFixtures.mkKey ["Module"]
                                Name  = IngestionFixtures.mkName "M"
                                Kinds = [ staticItems ]
                                IsActive = true
                                ExtendedProperties = [] } ]
                          Sequences = [] }
                    let hydratedCountVia (connSpec: string) : System.Threading.Tasks.Task<int> =
                        task {
                            let cfg =
                                { Config.defaultConfig with
                                    Model = { Config.defaultConfig.Model with Ossys = Some connSpec; Path = None } }
                            let! r = Hydration.hydrateCatalog cfg staticCatalog
                            let c = match r with Ok c -> c | Error es -> failwithf "hydrate (%s): %A" connSpec es
                            let k = Catalog.allKinds c |> List.find (fun k -> k.SsKey = IngestionFixtures.itemsKey)
                            return List.length (Kind.staticPopulations k)
                        }
                    // env: ref — the per-DB connection via an out-of-band env var.
                    System.Environment.SetEnvironmentVariable("HYDRATION_TEST_OSSYS", perDbConn)
                    let! viaEnv = hydratedCountVia "env:HYDRATION_TEST_OSSYS"
                    Assert.Equal(3, viaEnv)
                    // file: ref — a file holding the connection string (the
                    // operator's predominant form); must hydrate identically.
                    let connFile = System.IO.Path.GetTempFileName()
                    System.IO.File.WriteAllText(connFile, perDbConn)
                    try
                        let! viaFile = hydratedCountVia (sprintf "file:%s" connFile)
                        Assert.Equal(3, viaFile)
                    finally
                        System.IO.File.Delete connFile
                }))
