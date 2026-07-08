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
open Projection.Adapters.OssysSql
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
          ReconcileIgnore = []; SupportingScope = []
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

    let value (r: Result<'a>) : 'a = Result.value r

    /// Run a board and capture what the operator would read (the render
    /// goes to stdout) — the forecast-table / evidence assertions read it.
    let captureBoard (f: unit -> int) : int * string =
        let prior = System.Console.Out
        use sw = new System.IO.StringWriter()
        System.Console.SetOut sw
        try
            let exit = f ()
            exit, sw.ToString()
        finally System.Console.SetOut prior

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

    /// An UNRELATED, UNRESOLVABLE dependency cycle injected into a cell's
    /// metamodel (2026-07-07, the effective-transfer-graph program):
    /// CycleA ↔ CycleB, both FK attributes MANDATORY (non-nullable →
    /// EdgeStrength `Other`, which the asymmetric-2-cycle resolver refuses
    /// to break). Same SS_Keys in both cells so the contracts align; direct
    /// `Referenced_Entity_Id` linkage; no physical tables needed — the pair
    /// stays outside the transferred subset, so no data path touches it.
    let unrelatedCycleBatch =
        [ "INSERT INTO [dbo].[ossys_Entity] ([Id], [Name], [Physical_Table_Name], [Espace_Id], [Is_Active], [Is_System], [Is_External], [Data_Kind], [PrimaryKey_SS_Key], [SS_Key], [Description]) VALUES \
             (98001, N'CycleA', N'OSUSR_CYC_A', 100, 1, 0, 0, N'entity', NULL, '000000c1-0000-4000-8000-00000000000a', NULL), \
             (98002, N'CycleB', N'OSUSR_CYC_B', 100, 1, 0, 0, N'entity', NULL, '000000c1-0000-4000-8000-00000000000b', NULL); \
           INSERT INTO [dbo].[ossys_Entity_Attr] ([Id], [Entity_Id], [Name], [SS_Key], [Data_Type], [Is_Mandatory], [Is_Active], [Is_AutoNumber], [Is_Identifier], [Referenced_Entity_Id], [Delete_Rule], [Physical_Column_Name], [Order_Num]) VALUES \
             (98011, 98001, N'Id',   '000000a1-0000-4000-8000-00000000c1a1', N'Identifier', 1, 1, 1, 1, NULL,  NULL,       N'ID',  1), \
             (98012, 98001, N'BRef', '000000a1-0000-4000-8000-00000000c1a2', N'Identifier', 1, 1, 0, 0, 98002, N'Protect', N'BID', 10), \
             (98021, 98002, N'Id',   '000000a1-0000-4000-8000-00000000c1b1', N'Identifier', 1, 1, 1, 1, NULL,  NULL,       N'ID',  1), \
             (98022, 98002, N'ARef', '000000a1-0000-4000-8000-00000000c1b2', N'Identifier', 1, 1, 0, 0, 98001, N'Protect', N'AID', 10);" ]

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
                        //    The escape's proposals now carry LIVE evidence
                        //    (2026-07-07): each candidate reconcile column
                        //    probed against the actual pair.
                        let red1, redOut = GoBoardFixtures.captureBoard (fun () -> runCheckGo MetadataSnapshotRunner.defaultParameters "golden" "cloud-qa" "cloud-uat" false false (planned (GoBoardFixtures.optsWith [ "Customer" ] [])))
                        Assert.Equal(5, red1)
                        Assert.Contains("evidence: reconcile 'AppCore.City:Name'", redOut)
                        Assert.Contains("sink-unique", redOut)          // Lisbon/Porto are distinct on the sink
                        Assert.Contains("2/2 sampled source value(s) found in the sink", redOut)
                        Assert.Contains("STRONG", redOut)

                        // 2. GREEN — the SAME flow with the proposed
                        //    reconcile; `--sql` writes the planned artifact
                        //    and the forecast carries the before→after table.
                        let sqlPath = System.IO.Path.Combine("go-board", "golden.planned.sql")
                        if System.IO.File.Exists sqlPath then System.IO.File.Delete sqlPath
                        let green, greenOut = GoBoardFixtures.captureBoard (fun () -> runCheckGo MetadataSnapshotRunner.defaultParameters "golden" "cloud-qa" "cloud-uat" false true (planned (GoBoardFixtures.optsWith [ "Customer" ] [ "AppCore.City:Name" ])))
                        Assert.Equal(0, green)
                        // The forecast table (2026-07-08): source AND target
                        // physical names side by side; the declared customer
                        // with its adds; the reconciled city brought along,
                        // matched with no insert; a TOTAL row closes it.
                        Assert.Contains("source (read)", greenOut)
                        Assert.Contains("target (written)", greenOut)
                        Assert.Contains("OSUSR_ABC_CUSTOMER", greenOut)   // the SOURCE physical name
                        Assert.Contains("OSUSR_XABC_CUSTOMER", greenOut)  // the SINK physical name
                        Assert.Contains("OSUSR_DEF_CITY", greenOut)       // source city
                        Assert.Contains("OSUSR_XDEF_CITY", greenOut)      // sink city
                        Assert.Contains("matched to existing target rows, no insert", greenOut)
                        Assert.Contains("brought along by", greenOut)     // City is pulled in by Customer.CityId
                        Assert.Contains("TOTAL", greenOut)
                        // Match drift (2026-07-08): the reconciled City's
                        // matched pairs agree on every compared column
                        // (Name/IsActive match; the PK is excluded) — GREEN.
                        Assert.Contains("carry identical values", greenOut)
                        // The relationships GO now names its OUTBOUND direction.
                        Assert.Contains("every OUTBOUND reference", greenOut)
                        // The planned-SQL artifact: written, and it carries
                        // the sink-side DML shape the plan would realize.
                        Assert.True(System.IO.File.Exists sqlPath, "--sql must write go-board/golden.planned.sql")
                        let plannedSql = System.IO.File.ReadAllText sqlPath
                        Assert.Contains("PLANNED SQL PREVIEW", plannedSql)
                        Assert.Contains("OSUSR_XABC_CUSTOMER", plannedSql)
                        Assert.Contains("alice@x", plannedSql)
                        Assert.DoesNotContain("DELETE FROM", plannedSql)   // Incremental: no wipe section

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
                        let red2 = runCheckGo MetadataSnapshotRunner.defaultParameters "golden" "cloud-qa" "cloud-uat" false false (planned (GoBoardFixtures.optsWith [ "Customer" ] [ "AppCore.City:Name" ]))
                        Assert.Equal(5, red2)
                        return ()
                    }))

    /// The unrelated-cycle witness (2026-07-07): an UNRESOLVABLE dependency
    /// cycle elsewhere in the estate no longer reds a partial transfer's
    /// board — the load-order gate and the dry run's cycle report judge the
    /// EFFECTIVE transfer graph (declared tables + reconciled parents as
    /// isolated nodes), not the whole sink contract. The same flow that goes
    /// green in the red/green/red scenario stays green with CycleA ↔ CycleB
    /// (both FKs mandatory — the resolver refuses it) sitting outside the
    /// subset in BOTH cells.
    [<Fact>]
    member _.``go board: an unrelated estate cycle does not block a partial transfer — load order and cycles judge the transferred set only`` () =
        if not (GoBoardFixtures.skipIfNoDocker "GoBoardUnrelatedCycle") then () else
        TaskSync.run (fun () ->
            MockOutSystemsEnv.withMockEnvPair fixture "GoBoardCyc"
                "" (GoBoardFixtures.sourceRows @ GoBoardFixtures.unrelatedCycleBatch) MockOutSystemsEnv.ManagedDml
                "X" (GoBoardFixtures.sinkCityRows @ GoBoardFixtures.unrelatedCycleBatch) MockOutSystemsEnv.ManagedDml
                (fun src snk ->
                    task {
                        // The cycle is REAL at the whole-estate grain: the
                        // unscoped topology degrades to alphabetical on the
                        // sink contract (the contrast that pins the fix).
                        let! contractsR = PeerTransfer.acquireContracts src.EngineConnStr snk.EngineConnStr
                        let (_, sinkContract) = GoBoardFixtures.value contractsR
                        let whole = (Projection.Core.Passes.TopologicalOrderPass.runWith Projection.Core.TreatAsCycle sinkContract).Value
                        Assert.Equal(Projection.Core.Alphabetical, whole.Mode)
                        // ...and the board still goes GREEN for the subset.
                        let planned opts = PlanAction.TransferPeer (src.EngineConnStr, snk.EngineConnStr, opts, false)
                        let green = runCheckGo MetadataSnapshotRunner.defaultParameters "golden" "cloud-qa" "cloud-uat" false false (planned (GoBoardFixtures.optsWith [ "Customer" ] [ "AppCore.City:Name" ]))
                        Assert.Equal(0, green)
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
                            //    `ConnectionRef.Raw` (DECISIONS 2026-07-06).
                            let srcSub : Substrate = { Environment = Projection.Core.Environment.Qa; Role = SubstrateRole.Source; ConnectionRef = ConnectionRef.Raw src.EngineConnStr }
                            let sinkSub : Substrate = { Environment = Projection.Core.Environment.Uat; Role = SubstrateRole.Sink; ConnectionRef = ConnectionRef.Raw snk.EngineConnStr }
                            let connections = TransferConnections.create srcSub sinkSub true |> Result.value
                            let! runR =
                                Transfer.runReverseLegThroughConnectionsWith
                                    IdentityPolicy.Structural Transfer.Execute EmissionMode.Incremental false true false
                                    [ "Customer" ] connections srcContract sinkContract reconciliation Set.empty Set.empty Set.empty
                                    false (Some undoDir)
                            let report = Result.value runR
                            Assert.Equal(2, report.Kinds |> List.sumBy (fun k -> k.RowsWritten))
                            let! customersAfter = GoBoardFixtures.countRows snk.Admin "[dbo].[OSUSR_XABC_CUSTOMER]"
                            Assert.Equal(2, customersAfter)

                            // 2. The undo artifact exists and names the minted keys.
                            let undoPath = System.IO.Path.Combine(undoDir, "transfer-undo.sql")
                            Assert.True(System.IO.File.Exists undoPath, "the success tail must write transfer-undo.sql")

                            // 3. REVERT through the face: preview (no deletes), then live.
                            let preview = Projection.Cli.Faces.Transfer.runRevertScript undoPath "cloud-uat" ("live:" + snk.EngineConnStr) false false
                            Assert.Equal(0, preview)
                            let! stillThere = GoBoardFixtures.countRows snk.Admin "[dbo].[OSUSR_XABC_CUSTOMER]"
                            Assert.Equal(2, stillThere)
                            let prior = System.Environment.GetEnvironmentVariable "PROJECTION_ALLOW_EXECUTE"
                            System.Environment.SetEnvironmentVariable("PROJECTION_ALLOW_EXECUTE", "1")
                            let live =
                                try Projection.Cli.Faces.Transfer.runRevertScript undoPath "cloud-uat" ("live:" + snk.EngineConnStr) true false
                                finally System.Environment.SetEnvironmentVariable("PROJECTION_ALLOW_EXECUTE", prior)
                            Assert.Equal(0, live)

                            // 4a. THE WRONG-SINK GUARD: the artifact's
                            //     provenance header names the sink database;
                            //     pointing --against at the SOURCE refuses by
                            //     name (exit 7) before any delete.
                            let mismatch = Projection.Cli.Faces.Transfer.runRevertScript undoPath "cloud-qa" ("live:" + src.EngineConnStr) true false
                            Assert.Equal(7, mismatch)

                            // 4. The sink is back to its pre-transfer state:
                            //    minted rows gone, pre-existing city rows intact.
                            let! customersReverted = GoBoardFixtures.countRows snk.Admin "[dbo].[OSUSR_XABC_CUSTOMER]"
                            Assert.Equal(0, customersReverted)
                            let! cities = GoBoardFixtures.countRows snk.Admin "[dbo].[OSUSR_XDEF_CITY]"
                            Assert.Equal(2, cities)
                            return ()
                        finally
                            try System.IO.Directory.Delete(undoDir, true) with _ -> ()
                    }))
