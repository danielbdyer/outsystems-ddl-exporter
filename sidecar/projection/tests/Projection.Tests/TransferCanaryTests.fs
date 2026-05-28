namespace Projection.Tests

// The data-level canary (Slice C): the data analog of the schema canary
// and the runtime proof of H-050 extended to data. Seed a Source ephemeral
// DB, run a Transfer (ingest → plan → project) onto an empty Sink DB with
// the same schema, read both back, and assert the Sink reproduces the
// Source on `PhysicalSchema` (columns + FKs + per-row hashes). Serial via
// the Docker-SqlServer collection; blocking wait via `TaskSync.run`.

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

    let value (r: Result<'a>) : 'a = Result.value r

    /// Count rows in a table on the given connection.
    let countRows (cnn: Microsoft.Data.SqlClient.SqlConnection) (table: string) : System.Threading.Tasks.Task<int> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sprintf "SELECT COUNT_BIG(*) FROM %s;" table
            let! scalar = cmd.ExecuteScalarAsync()
            return int (unbox<int64> scalar)
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

                                let! reportR = Transfer.run Transfer.Execute src sink contract
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

                                let! reportR = Transfer.runReconciling Transfer.Execute src sink contract reconciliation
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

                                let! reportR = Transfer.run Transfer.DryRun src sink contract
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
