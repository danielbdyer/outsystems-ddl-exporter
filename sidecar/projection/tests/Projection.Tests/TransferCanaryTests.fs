namespace Projection.Tests

// The data-level canary (Slice C): the data analog of the schema canary
// and the runtime proof of H-050 extended to data. Seed a Source ephemeral
// DB, run a Transfer (ingest → plan → project) onto an empty Sink DB with
// the same schema, read both back, and assert the Sink reproduces the
// Source on `PhysicalSchema` (columns + FKs + per-row hashes). Serial via
// the Docker-SqlServer collection; blocking wait via `TaskSync.run`.

open System.IO
open Xunit
open Projection.Core
open Projection.Adapters.Sql
open Projection.Pipeline

module private TransferCanaryFixtures =

    let skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else printfn "SKIP %s: Docker daemon not reachable." label; false

    /// Acyclic two-table schema: Order has a NOT-NULL FK to Customer, so
    /// FK-safe ordering (Customer before Order) is required.
    let twoTableDdl =
        "CREATE TABLE [dbo].[OSUSR_XF_CUSTOMER] (" +
        "[ID] INT NOT NULL PRIMARY KEY, [NAME] NVARCHAR(50) NULL); " +
        "CREATE TABLE [dbo].[OSUSR_XF_ORDER] (" +
        "[ID] INT NOT NULL PRIMARY KEY, [CUSTOMER_ID] INT NOT NULL, [AMOUNT] INT NULL, " +
        "CONSTRAINT [FK_XfOrder_Customer] FOREIGN KEY ([CUSTOMER_ID]) " +
        "REFERENCES [dbo].[OSUSR_XF_CUSTOMER] ([ID]));"

    let twoTableSeed =
        "INSERT INTO [dbo].[OSUSR_XF_CUSTOMER] ([ID],[NAME]) VALUES (1,N'Alice'),(2,N'Bob'); " +
        "INSERT INTO [dbo].[OSUSR_XF_ORDER] ([ID],[CUSTOMER_ID],[AMOUNT]) VALUES " +
        "(10,1,100),(11,2,200),(12,1,300);"

    /// Self-referential FK (a 1-cycle): MANAGER_ID is nullable, so it
    /// defers — phase 1 inserts with NULL, phase 2 re-points it.
    let selfRefDdl =
        "CREATE TABLE [dbo].[OSUSR_XF_EMP] (" +
        "[ID] INT NOT NULL PRIMARY KEY, [NAME] NVARCHAR(50) NULL, [MANAGER_ID] INT NULL, " +
        "CONSTRAINT [FK_XfEmp_Mgr] FOREIGN KEY ([MANAGER_ID]) " +
        "REFERENCES [dbo].[OSUSR_XF_EMP] ([ID]));"

    // Ordered so each manager exists before the row that references it
    // (the seed itself does not defer; the *Transfer* does).
    let selfRefSeed =
        "INSERT INTO [dbo].[OSUSR_XF_EMP] ([ID],[NAME],[MANAGER_ID]) VALUES " +
        "(1,N'CEO',NULL),(2,N'VP',1),(3,N'Mgr',2);"

    /// User (business-keyed for the test) + Order with a NOT-NULL FK to
    /// User. The reconciling Transfer matches Source users to the
    /// pre-existing Sink users by EMAIL and re-keys every Order's USER_ID.
    let reKeyDdl =
        "CREATE TABLE [dbo].[OSUSR_RC_USER] (" +
        "[ID] INT NOT NULL PRIMARY KEY, [EMAIL] NVARCHAR(100) NULL); " +
        "CREATE TABLE [dbo].[OSUSR_RC_ORDER] (" +
        "[ID] INT NOT NULL PRIMARY KEY, [USER_ID] INT NOT NULL, [AMOUNT] INT NULL, " +
        "CONSTRAINT [FK_RcOrder_User] FOREIGN KEY ([USER_ID]) " +
        "REFERENCES [dbo].[OSUSR_RC_USER] ([ID]));"

    // Dev users 280/281/999; 999 (ghost@x) has no UAT counterpart. Order 12
    // references 999 → it (and only it) is skipped-and-diagnosed.
    let reKeySourceSeed =
        "INSERT INTO [dbo].[OSUSR_RC_USER] ([ID],[EMAIL]) VALUES " +
        "(280,N'alice@x'),(281,N'bob@x'),(999,N'ghost@x'); " +
        "INSERT INTO [dbo].[OSUSR_RC_ORDER] ([ID],[USER_ID],[AMOUNT]) VALUES " +
        "(10,280,100),(11,281,200),(12,999,300);"

    // Pre-existing Sink (UAT) users — same emails, DIFFERENT surrogates.
    let reKeySinkSeed =
        "INSERT INTO [dbo].[OSUSR_RC_USER] ([ID],[EMAIL]) VALUES (18,N'alice@x'),(19,N'bob@x');"

    /// AssignedBySink (§5.2): User's PK is IDENTITY, so the Sink mints new
    /// surrogates at insert time; Order (business PK) has a NOT-NULL FK to
    /// User. The Source seeds Users at 280/281 (via IDENTITY_INSERT) so the
    /// Sink-minted IDs (1,2 in the empty Sink) necessarily DIFFER — the
    /// round-trip can only hold if every Order's USER_ID was re-pointed
    /// through the captured SurrogateRemapContext.
    let assignedBySinkDdl =
        "CREATE TABLE [dbo].[OSUSR_AS_USER] (" +
        "[ID] INT IDENTITY(1,1) NOT NULL PRIMARY KEY, [EMAIL] NVARCHAR(100) NULL); " +
        "CREATE TABLE [dbo].[OSUSR_AS_ORDER] (" +
        "[ID] INT NOT NULL PRIMARY KEY, [USER_ID] INT NOT NULL, [AMOUNT] INT NULL, " +
        "CONSTRAINT [FK_AsOrder_User] FOREIGN KEY ([USER_ID]) " +
        "REFERENCES [dbo].[OSUSR_AS_USER] ([ID]));"

    let assignedBySinkSourceSeed =
        "SET IDENTITY_INSERT [dbo].[OSUSR_AS_USER] ON; " +
        "INSERT INTO [dbo].[OSUSR_AS_USER] ([ID],[EMAIL]) VALUES (280,N'alice@x'),(281,N'bob@x'); " +
        "SET IDENTITY_INSERT [dbo].[OSUSR_AS_USER] OFF; " +
        "INSERT INTO [dbo].[OSUSR_AS_ORDER] ([ID],[USER_ID],[AMOUNT]) VALUES (10,280,100),(11,281,200);"

    let value (r: Result<'a>) : 'a = Result.value r

    /// Count rows in a table on the given connection.
    let countRows (cnn: Microsoft.Data.SqlClient.SqlConnection) (table: string) : System.Threading.Tasks.Task<int> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sprintf "SELECT COUNT_BIG(*) FROM %s;" table
            let! scalar = cmd.ExecuteScalarAsync()
            return int (unbox<int64> scalar)
        }

    /// The (Order ID → User EMAIL) join projection — the identity-
    /// independent relationship the AssignedBySink round-trip must preserve.
    let orderUserEmail (cnn: Microsoft.Data.SqlClient.SqlConnection) : System.Threading.Tasks.Task<(int * string) list> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <-
                "SELECT o.[ID], u.[EMAIL] FROM [dbo].[OSUSR_AS_ORDER] o " +
                "JOIN [dbo].[OSUSR_AS_USER] u ON o.[USER_ID] = u.[ID] ORDER BY o.[ID];"
            use! reader = cmd.ExecuteReaderAsync()
            let acc = System.Collections.Generic.List<int * string>()
            let mutable go = true
            while go do
                let! has = reader.ReadAsync()
                if has then acc.Add(reader.GetInt32 0, reader.GetString 1)
                else go <- false
            return List.ofSeq acc
        }


[<Xunit.Collection("Docker-SqlServer")>]
type TransferCanaryTests(fixture: EphemeralContainerFixture) =
    interface IClassFixture<EphemeralContainerFixture>

    /// Source ≈ Sink on PhysicalSchema after a Transfer of the given
    /// schema + seed — the data-level canary assertion.
    member private _.RoundTrips (label: string) (ddl: string) (seed: string) =
        if not (TransferCanaryFixtures.skipIfNoDocker label) then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase (label + "Src") (fun src _ ->
                task {
                    do! Deploy.executeBatch src ddl
                    do! Deploy.executeBatch src seed
                    return!
                        fixture.WithEphemeralDatabase (label + "Sink") (fun sink _ ->
                            task {
                                do! Deploy.executeBatch sink ddl   // same schema, no rows

                                // Contract = the Source's reconstructed schema.
                                let! contractR = ReadSide.read src
                                let contract = TransferCanaryFixtures.value contractR

                                let! reportR = Transfer.run Transfer.Execute true src sink contract
                                let report = TransferCanaryFixtures.value reportR
                                Assert.True(report.Kinds |> List.forall (fun k -> k.RowsWritten = k.RowsIngested))

                                // Sink reproduces Source on PhysicalSchema (incl. per-row hashes).
                                let! aR = ReadSide.read src
                                let! bR = ReadSide.read sink
                                let diff =
                                    PhysicalSchema.diff
                                        (PhysicalSchema.ofCatalog (TransferCanaryFixtures.value aR))
                                        (PhysicalSchema.ofCatalog (TransferCanaryFixtures.value bR))
                                Assert.True(PhysicalSchema.isEqual diff, PhysicalSchema.renderDiff diff)
                            })
                }))

    [<Fact>]
    member this.``data canary: multi-table FK chain round-trips with empty PhysicalSchema diff`` () =
        this.RoundTrips "XferTwo" TransferCanaryFixtures.twoTableDdl TransferCanaryFixtures.twoTableSeed

    [<Fact>]
    member this.``data canary: deferred self-referential FK is re-pointed in phase 2`` () =
        this.RoundTrips "XferSelf" TransferCanaryFixtures.selfRefDdl TransferCanaryFixtures.selfRefSeed

    // Wave-3 slice 3.1 — the CDC pre-flight. An Execute transfer against a
    // Sink with CDC-tracked tables is refused unless --allow-cdc. Gracefully
    // skips if the container cannot enable CDC (the metadata flag is what the
    // pre-flight reads; capture jobs need Agent but the flag is set regardless).
    [<Fact>]
    member _.``3.1: CDC pre-flight refuses --execute against a CDC-tracked sink, allow-cdc overrides`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "XferCdc") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "XferCdcSrc" (fun src _ ->
                task {
                    let ddl = "CREATE TABLE dbo.Widget (Id INT NOT NULL PRIMARY KEY, Name NVARCHAR(50) NULL);"
                    do! Deploy.executeBatch src ddl
                    do! Deploy.executeBatch src "INSERT INTO dbo.Widget (Id, Name) VALUES (1, N'a');"
                    return!
                        fixture.WithEphemeralDatabase "XferCdcSink" (fun sink _ ->
                            task {
                                do! Deploy.executeBatch sink ddl
                                let! enabled =
                                    task {
                                        try
                                            do! Deploy.executeBatch sink "EXEC sys.sp_cdc_enable_db;"
                                            do! Deploy.executeBatch sink "EXEC sys.sp_cdc_enable_table @source_schema = N'dbo', @source_name = N'Widget', @role_name = NULL, @supports_net_changes = 0;"
                                            use cmd = sink.CreateCommand()
                                            cmd.CommandText <- "SELECT COUNT(*) FROM sys.tables WHERE is_tracked_by_cdc = 1 AND name = N'Widget'"
                                            let! c = cmd.ExecuteScalarAsync()
                                            return System.Convert.ToInt32 c > 0
                                        with _ -> return false
                                    }
                                if not enabled then
                                    printfn "SKIP 3.1 CDC pre-flight: container did not enable CDC (flag not set)"
                                else
                                    let! contractR = ReadSide.read src
                                    let contract = TransferCanaryFixtures.value contractR
                                    // allowCdc = false → the pre-flight refuses.
                                    let! refusedR = Transfer.run Transfer.Execute false src sink contract
                                    match refusedR with
                                    | Error es ->
                                        Assert.True(
                                            es |> List.exists (fun e -> e.Code = "transfer.cdcTrackedSink"),
                                            sprintf "expected transfer.cdcTrackedSink, got %A" (es |> List.map (fun e -> e.Code)))
                                    | Ok _ -> Assert.Fail("expected CDC pre-flight refusal against a CDC-tracked sink")
                                    // allowCdc = true → the override proceeds and writes.
                                    let! okR = Transfer.run Transfer.Execute true src sink contract
                                    match okR with
                                    | Ok _ -> ()
                                    | Error es -> Assert.Fail(sprintf "--allow-cdc override should proceed; got %A" (es |> List.map (fun e -> e.Code)))
                            })
                }))

    [<Fact>]
    member _.``data canary: reconciling Transfer re-keys FKs to pre-existing Sink identities (Dev->UAT User re-key)`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "XferReKey") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "XferReKeySrc" (fun src _ ->
                task {
                    do! Deploy.executeBatch src TransferCanaryFixtures.reKeyDdl
                    do! Deploy.executeBatch src TransferCanaryFixtures.reKeySourceSeed
                    return!
                        fixture.WithEphemeralDatabase "XferReKeySink" (fun sink _ ->
                            task {
                                do! Deploy.executeBatch sink TransferCanaryFixtures.reKeyDdl
                                do! Deploy.executeBatch sink TransferCanaryFixtures.reKeySinkSeed

                                // Contract = the Source's reconstructed schema.
                                let! contractR = ReadSide.read src
                                let contract = TransferCanaryFixtures.value contractR
                                let kindByTable (t: string) =
                                    Catalog.allModulesKinds contract |> List.map snd |> List.find (fun k -> k.Physical.Table = t)
                                let userKind = kindByTable "OSUSR_RC_USER"
                                let orderKind = kindByTable "OSUSR_RC_ORDER"
                                let emailName =
                                    userKind.Attributes |> List.find (fun a -> a.Column.ColumnName = "EMAIL") |> (fun a -> a.Name)

                                let reconciliation =
                                    Map.ofList [ userKind.SsKey, ReconciliationStrategy.MatchByColumn emailName ]

                                let! reportR = Transfer.runReconciling Transfer.Execute true src sink contract reconciliation
                                let report = TransferCanaryFixtures.value reportR

                                // The reconciled kind skips its insert (its rows are already in the Sink).
                                let userOutcome = report.Kinds |> List.find (fun k -> k.Kind = userKind.SsKey)
                                Assert.Equal(IdentityDisposition.ReconciledByRule, userOutcome.Disposition)
                                Assert.Equal(0, userOutcome.RowsWritten)

                                // ghost@x (Source 999) had no Sink identity; Order 12 referencing it is dropped.
                                Assert.Contains(report.UnmatchedIdentities, fun (k, s) -> k = userKind.SsKey && s = SourceKey.ofString "999")
                                Assert.Contains(report.SkippedReferences, fun (owner, r: UnresolvedReference) ->
                                    owner = orderKind.SsKey && r.Target = userKind.SsKey && r.UnresolvedSource = SourceKey.ofString "999")

                                // Sink holds the 2 pre-existing users (none added) and 2 re-keyed orders.
                                let! users = TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_RC_USER]"
                                let! orders = TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_RC_ORDER]"
                                Assert.Equal(2, users)
                                Assert.Equal(2, orders)

                                // No inserted Order carries a Source surrogate — every FK was re-pointed.
                                let! sourceValued =
                                    TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_RC_ORDER] WHERE [USER_ID] IN (280,281,999)"
                                Assert.Equal(0, sourceValued)
                            })
                }))

    // 6.A.1 — the drop-set is fail-loud, not exit-0. The same Dev->UAT re-key
    // fixture that drops ghost@x's referencing Order (SkippedReferences +
    // UnmatchedIdentities) must map to a non-zero CLI exit, and --allow-drops
    // must downgrade it. The exit-code policy is the pure `exitCodeForReport`
    // that the CLI's runTransfer consumes, so this canary witnesses the same
    // decision the operator's refresh script branches on.
    [<Fact>]
    member _.``data canary: transfer with an unmatched FK exits non-zero (drop is fail-loud, not exit-0)`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "XferDrops") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "XferDropsSrc" (fun src _ ->
                task {
                    do! Deploy.executeBatch src TransferCanaryFixtures.reKeyDdl
                    do! Deploy.executeBatch src TransferCanaryFixtures.reKeySourceSeed
                    return!
                        fixture.WithEphemeralDatabase "XferDropsSink" (fun sink _ ->
                            task {
                                do! Deploy.executeBatch sink TransferCanaryFixtures.reKeyDdl
                                do! Deploy.executeBatch sink TransferCanaryFixtures.reKeySinkSeed

                                let! contractR = ReadSide.read src
                                let contract = TransferCanaryFixtures.value contractR
                                let userKind =
                                    Catalog.allModulesKinds contract |> List.map snd
                                    |> List.find (fun k -> k.Physical.Table = "OSUSR_RC_USER")
                                let emailName =
                                    userKind.Attributes |> List.find (fun a -> a.Column.ColumnName = "EMAIL") |> (fun a -> a.Name)
                                let reconciliation =
                                    Map.ofList [ userKind.SsKey, ReconciliationStrategy.MatchByColumn emailName ]

                                let! reportR = Transfer.runReconciling Transfer.Execute true src sink contract reconciliation
                                let report = TransferCanaryFixtures.value reportR

                                // The drop-set is non-empty (ghost@x's Order vanished).
                                Assert.True(Transfer.hasDrops report)
                                Assert.True(Transfer.droppedRowCount report > 0)

                                // Fail-loud: a dropped run is a distinct non-zero exit, NOT 0.
                                Assert.Equal(Transfer.DroppedReferencesExit, Transfer.exitCodeForReport false report)
                                Assert.NotEqual(0, Transfer.exitCodeForReport false report)

                                // --allow-drops downgrades the declared-acceptable loss to exit 0.
                                Assert.Equal(0, Transfer.exitCodeForReport true report)
                            })
                }))

    // §5.2 Slice E — the data adjunction for AssignedBySink (sink-minted keys).
    // User has an IDENTITY PK, so the Sink mints fresh surrogates per row; the
    // capture (INSERT…OUTPUT) feeds a SurrogateRemapContext that re-points every
    // Order's USER_ID. The round-trip holds MODULO that remap: the surrogate
    // values differ, but the (Order → User-by-email) relationship is identical.
    [<Fact>]
    member _.``data adjunction: AssignedBySink round-trips modulo SurrogateRemapContext`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "XferAssigned") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "XferAssignedSrc" (fun src _ ->
                task {
                    do! Deploy.executeBatch src TransferCanaryFixtures.assignedBySinkDdl
                    do! Deploy.executeBatch src TransferCanaryFixtures.assignedBySinkSourceSeed
                    return!
                        fixture.WithEphemeralDatabase "XferAssignedSink" (fun sink _ ->
                            task {
                                do! Deploy.executeBatch sink TransferCanaryFixtures.assignedBySinkDdl   // same schema, empty

                                let! contractR = ReadSide.read src
                                let contract = TransferCanaryFixtures.value contractR
                                let userKind =
                                    Catalog.allModulesKinds contract |> List.map snd
                                    |> List.find (fun k -> k.Physical.Table = "OSUSR_AS_USER")
                                // Precondition: the reconstructed contract classifies User as
                                // AssignedBySink (its IDENTITY PK survives the ReadSide round-trip).
                                Assert.Equal(IdentityDisposition.AssignedBySink, IdentityDisposition.ofKind userKind)

                                let! reportR = Transfer.run Transfer.Execute true src sink contract
                                let report = TransferCanaryFixtures.value reportR
                                Assert.Empty(report.SkippedReferences)
                                Assert.True(report.Kinds |> List.forall (fun k -> k.RowsWritten = k.RowsIngested))

                                // Row counts preserved on both kinds.
                                let! users  = TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_AS_USER]"
                                let! orders = TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_AS_ORDER]"
                                Assert.Equal(2, users)
                                Assert.Equal(2, orders)

                                // The Sink minted NEW surrogates — the Source IDs (280/281) are gone,
                                // so the FK re-point was load-bearing (not an accidental ID collision).
                                let! sourceIds =
                                    TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_AS_USER] WHERE [ID] IN (280,281)"
                                Assert.Equal(0, sourceIds)

                                // …yet the (Order → User-by-email) relationship is identical:
                                // round-trip holds MODULO the SurrogateRemapContext.
                                let! srcPairs  = TransferCanaryFixtures.orderUserEmail src
                                let! sinkPairs = TransferCanaryFixtures.orderUserEmail sink
                                Assert.Equal<(int * string) list>(srcPairs, sinkPairs)
                                Assert.Equal<(int * string) list>([ (10, "alice@x"); (11, "bob@x") ], sinkPairs)
                            })
                }))

    // Slice 4.2 — the reconcile canary driven THROUGH the TransferConnections
    // apparatus (not caller-opened connections), with a ManualOverride CSV
    // round-tripping into the reconcile path. The ephemeral per-DB connection
    // strings are written to temp files and referenced via ConnectionRef.File
    // (D9: the secret lives out of band); openSubstrate resolves + opens them;
    // runThroughConnections reads the contract from the Source, resolves the
    // CSV-derived ManualOverride against it, and runs. The reconcile outcome
    // matches the MatchByColumn canary above: User reconciled (0 rows written),
    // ghost 999 unmatched, Order 12 skipped, every FK re-pointed.
    [<Fact>]
    member _.``4.2: reconcile driven through TransferConnections + ManualOverride CSV round-trips`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "XferAppar") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "XferApparSrc" (fun src srcConnStr ->
                task {
                    do! Deploy.executeBatch src TransferCanaryFixtures.reKeyDdl
                    do! Deploy.executeBatch src TransferCanaryFixtures.reKeySourceSeed
                    return!
                        fixture.WithEphemeralDatabase "XferApparSink" (fun sink sinkConnStr ->
                            task {
                                do! Deploy.executeBatch sink TransferCanaryFixtures.reKeyDdl
                                do! Deploy.executeBatch sink TransferCanaryFixtures.reKeySinkSeed

                                // D9: write the per-DB connection strings to temp files and
                                // reference them out of band — the apparatus resolves them.
                                let srcFile = Path.GetTempFileName()
                                let sinkFile = Path.GetTempFileName()
                                File.WriteAllText(srcFile, srcConnStr)
                                File.WriteAllText(sinkFile, sinkConnStr)
                                try
                                    let srcSub : Substrate =
                                        { Environment = Projection.Core.Environment.Dev
                                          Role = SubstrateRole.Source
                                          ConnectionRef = ConnectionRef.File srcFile }
                                    let sinkSub : Substrate =
                                        { Environment = Projection.Core.Environment.Uat
                                          Role = SubstrateRole.Sink
                                          ConnectionRef = ConnectionRef.File sinkFile }
                                    let connections =
                                        TransferConnections.create srcSub sinkSub true
                                        |> TransferCanaryFixtures.value
                                    // Reconcile ⇒ the Sink is profiled for identity too.
                                    Assert.Equal(2, connections.ProfiledForIdentity.Length)

                                    // The ManualOverride map arrives as a CSV that is parsed
                                    // and resolved against the reconstructed contract — the
                                    // full round-trip into the reconcile path.
                                    let userMapCsv =
                                        "table,source,assigned\n" +
                                        "OSUSR_RC_USER,280,18\n" +
                                        "OSUSR_RC_USER,281,19"
                                    let resolveReconciliation (contract: Catalog) =
                                        match TransferSpec.parseUserMapCsv userMapCsv with
                                        | Error es -> Error es
                                        | Ok entries -> TransferSpec.resolveAllReconciliation contract [] entries

                                    let! reportR =
                                        Transfer.runThroughConnections Transfer.Execute true connections resolveReconciliation
                                    let report = TransferCanaryFixtures.value reportR

                                    // Map tables → reconstructed SsKeys for the assertions.
                                    let! contractR = ReadSide.read src
                                    let contract = TransferCanaryFixtures.value contractR
                                    let kindByTable (t: string) =
                                        Catalog.allModulesKinds contract |> List.map snd |> List.find (fun k -> k.Physical.Table = t)
                                    let userKind = kindByTable "OSUSR_RC_USER"
                                    let orderKind = kindByTable "OSUSR_RC_ORDER"

                                    let userOutcome = report.Kinds |> List.find (fun k -> k.Kind = userKind.SsKey)
                                    Assert.Equal(IdentityDisposition.ReconciledByRule, userOutcome.Disposition)
                                    Assert.Equal(0, userOutcome.RowsWritten)
                                    Assert.Contains(report.UnmatchedIdentities, fun (k, s) -> k = userKind.SsKey && s = SourceKey.ofString "999")
                                    Assert.Contains(report.SkippedReferences, fun (owner, r: UnresolvedReference) ->
                                        owner = orderKind.SsKey && r.Target = userKind.SsKey && r.UnresolvedSource = SourceKey.ofString "999")

                                    let! users = TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_RC_USER]"
                                    let! orders = TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_RC_ORDER]"
                                    Assert.Equal(2, users)
                                    Assert.Equal(2, orders)
                                    let! sourceValued =
                                        TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_RC_ORDER] WHERE [USER_ID] IN (280,281,999)"
                                    Assert.Equal(0, sourceValued)
                                finally
                                    try File.Delete srcFile with _ -> ()
                                    try File.Delete sinkFile with _ -> ()
                            })
                }))

    [<Fact>]
    member _.``data canary: dry-run reports the plan and writes nothing to the Sink`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "XferDry") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "XferDrySrc" (fun src _ ->
                task {
                    do! Deploy.executeBatch src TransferCanaryFixtures.twoTableDdl
                    do! Deploy.executeBatch src TransferCanaryFixtures.twoTableSeed
                    return!
                        fixture.WithEphemeralDatabase "XferDrySink" (fun sink _ ->
                            task {
                                do! Deploy.executeBatch sink TransferCanaryFixtures.twoTableDdl

                                let! contractR = ReadSide.read src
                                let contract = TransferCanaryFixtures.value contractR

                                let! reportR = Transfer.run Transfer.DryRun true src sink contract
                                let report = TransferCanaryFixtures.value reportR
                                Assert.Equal(Transfer.DryRun, report.Mode)
                                // Rows were ingested + planned, but none written.
                                Assert.True(report.Kinds |> List.exists (fun k -> k.RowsIngested > 0))
                                Assert.True(report.Kinds |> List.forall (fun k -> k.RowsWritten = 0))

                                // The Sink remains empty.
                                let! custCount = TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_XF_CUSTOMER]"
                                let! orderCount = TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_XF_ORDER]"
                                Assert.Equal(0, custCount)
                                Assert.Equal(0, orderCount)
                            })
                }))
