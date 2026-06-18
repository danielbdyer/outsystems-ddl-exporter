namespace Projection.Tests

// Slice 1b — Docker-gated end-to-end witness for the LIVE closure walk
// (`ClosureOracle`). Where `ClosureTests` pins the pure engine against a fake
// oracle, this proves the whole machine against a REAL SQL Server: a seeded
// source schema (PK + FK + NOT NULL — a three-level OutSystems-shaped chain
// with eSpace-style physical names), the catalog reconstructed live via
// `Ref.resolveCatalog` → `ReadSide`, key-scoped reads through the actual
// `WHERE <pk> IN (@k…)` SQL, and the closure driven to its referential fixed
// point. The closed set must be exactly the root's transitive parents, with a
// clean closure report.
//
// Soft-skips on `Deploy.Docker.ensureRunning ()` (mirrors `LiveSourceDockerTests`);
// `IClassFixture<EphemeralContainerFixture>` shares one container, each test a
// fresh `<prefix>_<guid>` database.

open Microsoft.Data.SqlClient
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Adapters.Sql

[<Xunit.Collection("Docker-SqlServer")>]
type ClosureOracleDockerTests(fixture: EphemeralContainerFixture) =

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

    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``closure walk extracts a referential closure from a live source`` () =
        if not (skipIfNoDocker "closure-live-walk") then () else
        let orders, users, countries, dangling =
            fixture.WithEphemeralDatabase "ClosureSrc" (fun cnn connStr ->
                task {
                    // A three-level FK chain: Country ← User ← Order. Physical
                    // names are eSpace-style; PK + FK + NOT NULL declared so the
                    // reconstructed catalog carries the edges the walk follows.
                    do! Deploy.executeBatch cnn
                            ("CREATE TABLE [dbo].[OSUSR_CL_COUNTRY] (" +
                             "[ID] INT NOT NULL PRIMARY KEY, [NAME] NVARCHAR(50) NOT NULL);")
                    do! Deploy.executeBatch cnn
                            ("CREATE TABLE [dbo].[OSUSR_CL_USER] (" +
                             "[ID] INT NOT NULL PRIMARY KEY, " +
                             "[COUNTRY_ID] INT NOT NULL " +
                             "CONSTRAINT [FK_CL_USER_COUNTRY] REFERENCES [dbo].[OSUSR_CL_COUNTRY]([ID]));")
                    do! Deploy.executeBatch cnn
                            ("CREATE TABLE [dbo].[OSUSR_CL_ORDER] (" +
                             "[ID] INT NOT NULL PRIMARY KEY, " +
                             "[USER_ID] INT NOT NULL " +
                             "CONSTRAINT [FK_CL_ORDER_USER] REFERENCES [dbo].[OSUSR_CL_USER]([ID]));")
                    do! Deploy.executeBatch cnn
                            ("INSERT INTO [dbo].[OSUSR_CL_COUNTRY] ([ID],[NAME]) VALUES (10,'US'),(20,'CA');")
                    do! Deploy.executeBatch cnn
                            ("INSERT INTO [dbo].[OSUSR_CL_USER] ([ID],[COUNTRY_ID]) VALUES (100,10),(200,20),(300,10);")
                    do! Deploy.executeBatch cnn
                            ("INSERT INTO [dbo].[OSUSR_CL_ORDER] ([ID],[USER_ID]) VALUES (1000,100),(1001,100),(1002,300);")

                    // Reconstruct the SOURCE catalog live (the real logical/
                    // physical bridge), exactly as the CLI resolves a `live:` ref.
                    let! catR = Ref.resolveCatalog (Ref.parse ("live:" + connStr))
                    let catalog = mustOk catR
                    let kindByTable (name: string) : Kind =
                        Catalog.allKinds catalog
                        |> List.find (fun k -> TableId.tableText k.Physical = name)
                    let pkName (k: Kind) : Name = (Kind.primaryKey k |> List.head).Name
                    let orderK = kindByTable "OSUSR_CL_ORDER"

                    // Seed the walk with order 1000 and drive it to the fixed point.
                    let! roots = ClosureOracle.fetchRootsByKey cnn catalog orderK.SsKey (pkName orderK) (Set.ofList [ "1000" ])
                    let! state = ClosureOracle.walk cnn catalog [] [ roots ]
                    let report = Closure.report catalog state

                    let keysOf (tableName: string) : Set<string> =
                        let k = kindByTable tableName
                        Closure.materialize state
                        |> Map.tryFind k.SsKey
                        |> Option.defaultValue []
                        |> List.map (fun r -> StaticRow.valueOrEmpty (pkName k) r)
                        |> Set.ofList

                    return
                        keysOf "OSUSR_CL_ORDER",
                        keysOf "OSUSR_CL_USER",
                        keysOf "OSUSR_CL_COUNTRY",
                        report.DanglingMandatory.Length
                })
            |> fun t -> t.GetAwaiter().GetResult()

        Assert.Equal<Set<string>>(Set.ofList [ "1000" ], orders)   // the root
        Assert.Equal<Set<string>>(Set.ofList [ "100" ], users)     // its parent user, key-scoped (not 200/300)
        Assert.Equal<Set<string>>(Set.ofList [ "10" ], countries)  // transitively, its country (not 20)
        Assert.Equal(0, dangling)                                  // referentially closed
