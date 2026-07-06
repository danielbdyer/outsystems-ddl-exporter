namespace Projection.Tests

// THE GO BOARD, end-to-end against two real mock OutSystems environments
// (2026-07-06, the preview-engine program): the red→green→red story the
// operator validates their setup against.
//
//   1. RED — an unconfigured flow (a subset whose FK escapes, no reconcile):
//      the board exits 5 and names the open decision with the paste-able
//      remedy.
//   2. GREEN — the SAME flow with the proposed reconcile added: every gate
//      passes, the dry-run forecast carries the row counts, exit 0.
//   3. RED AGAIN — a real schema divergence appears on the sink (a metamodel
//      attribute removed): the shape axis stops the board, exit 5.
//
// The board runs the ENGINE DRY RUN (real reads, zero writes) — asserted by
// the sink staying empty throughout. Managed-grant principals on both sides,
// so the forecast itself is proven inside the cloud envelope. Serial via
// Docker-SqlServer; blocking wait via TaskSync.

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Cli.Faces.Transfer

module private GoBoardFixtures =

    let skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else printfn "SKIP %s: Docker daemon not reachable." label; false

    let sourceRows =
        [ "SET IDENTITY_INSERT [dbo].[OSUSR_DEF_CITY] ON; \
           INSERT INTO [dbo].[OSUSR_DEF_CITY] ([ID],[NAME],[ISACTIVE]) VALUES (1, N'Lisbon', 1), (2, N'Porto', 1); \
           SET IDENTITY_INSERT [dbo].[OSUSR_DEF_CITY] OFF; \
           SET IDENTITY_INSERT [dbo].[OSUSR_ABC_CUSTOMER] ON; \
           INSERT INTO [dbo].[OSUSR_ABC_CUSTOMER] ([ID],[EMAIL],[FIRSTNAME],[LASTNAME],[CITYID]) VALUES \
               (10, N'alice@x', N'Alice', N'Almeida', 1), (11, N'bob@x', N'Bob', N'Barbosa', 2); \
           SET IDENTITY_INSERT [dbo].[OSUSR_ABC_CUSTOMER] OFF;" ]

    let sinkCityRows =
        [ "SET IDENTITY_INSERT [dbo].[OSUSR_XDEF_CITY] ON; \
           INSERT INTO [dbo].[OSUSR_XDEF_CITY] ([ID],[NAME],[ISACTIVE]) VALUES (501, N'Lisbon', 1), (502, N'Porto', 1); \
           SET IDENTITY_INSERT [dbo].[OSUSR_XDEF_CITY] OFF;" ]

    let optsWith (tables: string list) (reconcile: string list) : LoadOpts =
        { Declaration = DeclareNone
          Emission    = EmissionMode.Incremental
          Reconcile   = reconcile
          Rekey       = None
          AllowCdc    = false
          Resumable   = false
          Streaming   = false
          Journal     = None
          Atomic      = false
          RevertPolicy = RevertPolicy.Script
          RevertDir   = None
          Store       = None
          Env         = None
          Tables      = tables
          Seed        = None
          Scale       = None
          Correction  = None
          SinkCapability = SinkLoadCapability.structural }

    let countRows (cnn: Microsoft.Data.SqlClient.SqlConnection) (table: string) : System.Threading.Tasks.Task<int> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sprintf "SELECT COUNT_BIG(*) FROM %s;" table
            let! scalar = cmd.ExecuteScalarAsync()
            return int (unbox<int64> scalar)
        }

    let exec (cnn: Microsoft.Data.SqlClient.SqlConnection) (sql: string) : System.Threading.Tasks.Task =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sql
            let! _ = cmd.ExecuteNonQueryAsync()
            return ()
        }

[<Xunit.Collection("Docker-SqlServer")>]
type GoBoardDockerTests(fixture: EphemeralContainerFixture) =
    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``go board: RED on the open decision, GREEN with the reconcile, RED again on shape divergence — and the dry run never writes`` () =
        if not (GoBoardFixtures.skipIfNoDocker "GoBoardRedGreen") then () else
        TaskSync.run (fun () ->
            MockOutSystemsEnv.withMockEnvPair fixture "GoBoard"
                "" GoBoardFixtures.sourceRows MockOutSystemsEnv.ManagedDml
                "X" GoBoardFixtures.sinkCityRows MockOutSystemsEnv.ManagedDml
                (fun src snk ->
                    task {
                        let planned opts = PlanAction.TransferPeer (src.EngineConnStr, snk.EngineConnStr, opts, false)

                        // 1. RED — subset [Customer], City escapes, no strategy.
                        let red1 = runCheckGo "golden" "cloud-qa" "cloud-uat" (planned (GoBoardFixtures.optsWith [ "Customer" ] []))
                        Assert.Equal(5, red1)

                        // 2. GREEN — the SAME flow with the proposed reconcile.
                        let green = runCheckGo "golden" "cloud-qa" "cloud-uat" (planned (GoBoardFixtures.optsWith [ "Customer" ] [ "AppCore.City:Name" ]))
                        Assert.Equal(0, green)

                        // The board's dry run NEVER writes: the sink customer
                        // table is still empty after both boards.
                        let! customers = GoBoardFixtures.countRows snk.Admin "[dbo].[OSUSR_XABC_CUSTOMER]"
                        Assert.Equal(0, customers)
                        let! cities = GoBoardFixtures.countRows snk.Admin "[dbo].[OSUSR_XDEF_CITY]"
                        Assert.Equal(2, cities)   // the pre-seeded reconcile targets, untouched

                        // 3. RED again — a REAL shape divergence: the sink
                        // metamodel loses Customer.LastName (source-only
                        // attribute = blocking).
                        do! GoBoardFixtures.exec snk.Admin
                                "DELETE FROM [dbo].[ossys_Entity_Attr] WHERE [Name] = N'LastName' AND [Entity_Id] = 1000;"
                        let red2 = runCheckGo "golden" "cloud-qa" "cloud-uat" (planned (GoBoardFixtures.optsWith [ "Customer" ] [ "AppCore.City:Name" ]))
                        Assert.Equal(5, red2)
                        return ()
                    }))

    /// THE PROVING LOOP (2026-07-06): transfer a small declared subset, prove
    /// it landed, then DELIBERATELY REVERT it — the success-undo artifact
    /// (`transfer-undo.sql`, written by the engine's success tail) executed
    /// through the `projection revert` face restores the sink to its
    /// pre-transfer state; pre-existing rows are never touched.
    [<Fact>]
    member _.``proving loop: transfer a subset, then revert it from the success-undo artifact — the sink returns to its pre-transfer state`` () =
        if not (GoBoardFixtures.skipIfNoDocker "ProvingLoop") then () else
        TaskSync.run (fun () ->
            MockOutSystemsEnv.withMockEnvPair fixture "ProveRevert"
                "" GoBoardFixtures.sourceRows MockOutSystemsEnv.ManagedDml
                "X" GoBoardFixtures.sinkCityRows MockOutSystemsEnv.ManagedDml
                (fun src snk ->
                    task {
                        let undoDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "prove-" + System.Guid.NewGuid().ToString("N").Substring(0, 8))
                        let sinkFile = System.IO.Path.GetTempFileName()
                        try
                            let! contractsR = PeerTransfer.acquireContracts src.EngineConnStr snk.EngineConnStr
                            let (srcContract, sinkContract) = Result.value contractsR
                            let cityKind =
                                Catalog.allKinds sinkContract
                                |> List.find (fun k -> Name.value k.Name = "City")
                            let cityName = cityKind.Attributes |> List.find (fun a -> Name.value a.Name = "Name")
                            let reconciliation = Map.ofList [ cityKind.SsKey, ReconciliationStrategy.MatchByColumn cityName.Name ]

                            // 1. TRANSFER the subset (revert dir threaded so the
                            //    success tail writes transfer-undo.sql).
                            let srcSub : Substrate = { Environment = Projection.Core.Environment.Qa; Role = SubstrateRole.Source; ConnectionRef = ConnectionRef.File sinkFile }
                            // (the source ref file carries the SOURCE conn; reuse one temp file per side)
                            let srcFile = System.IO.Path.GetTempFileName()
                            System.IO.File.WriteAllText(srcFile, src.EngineConnStr)
                            System.IO.File.WriteAllText(sinkFile, snk.EngineConnStr)
                            let srcSub = { srcSub with ConnectionRef = ConnectionRef.File srcFile }
                            let sinkSub : Substrate = { Environment = Projection.Core.Environment.Uat; Role = SubstrateRole.Sink; ConnectionRef = ConnectionRef.File sinkFile }
                            let connections = TransferConnections.create srcSub sinkSub true |> Result.value
                            let! runR =
                                Transfer.runReverseLegThroughConnectionsWith
                                    IdentityPolicy.Structural Transfer.Execute EmissionMode.Incremental false true false
                                    [ "Customer" ] connections srcContract sinkContract reconciliation
                                    false (Some undoDir)
                            let report = Result.value runR
                            Assert.Equal(2, report.Kinds |> List.sumBy (fun k -> k.RowsWritten))
                            let! customersAfter = GoBoardFixtures.countRows snk.Admin "[dbo].[OSUSR_XABC_CUSTOMER]"
                            Assert.Equal(2, customersAfter)

                            // 2. The undo artifact exists and names the minted keys.
                            let undoPath = System.IO.Path.Combine(undoDir, "transfer-undo.sql")
                            Assert.True(System.IO.File.Exists undoPath, "the success tail must write transfer-undo.sql")

                            // 3. REVERT through the face: preview (no deletes), then live.
                            let preview = Projection.Cli.Faces.Transfer.runRevertScript undoPath "cloud-uat" ("file:" + sinkFile) false
                            Assert.Equal(0, preview)
                            let! stillThere = GoBoardFixtures.countRows snk.Admin "[dbo].[OSUSR_XABC_CUSTOMER]"
                            Assert.Equal(2, stillThere)
                            let prior = System.Environment.GetEnvironmentVariable "PROJECTION_ALLOW_EXECUTE"
                            System.Environment.SetEnvironmentVariable("PROJECTION_ALLOW_EXECUTE", "1")
                            let live =
                                try Projection.Cli.Faces.Transfer.runRevertScript undoPath "cloud-uat" ("file:" + sinkFile) true
                                finally System.Environment.SetEnvironmentVariable("PROJECTION_ALLOW_EXECUTE", prior)
                            Assert.Equal(0, live)

                            // 4. The sink is back to its pre-transfer state:
                            //    minted rows gone, pre-existing city rows intact.
                            let! customersReverted = GoBoardFixtures.countRows snk.Admin "[dbo].[OSUSR_XABC_CUSTOMER]"
                            Assert.Equal(0, customersReverted)
                            let! cities = GoBoardFixtures.countRows snk.Admin "[dbo].[OSUSR_XDEF_CITY]"
                            Assert.Equal(2, cities)
                            try System.IO.File.Delete srcFile with _ -> ()
                            return ()
                        finally
                            try System.IO.File.Delete sinkFile with _ -> ()
                            try System.IO.Directory.Delete(undoDir, true) with _ -> ()
                    }))
