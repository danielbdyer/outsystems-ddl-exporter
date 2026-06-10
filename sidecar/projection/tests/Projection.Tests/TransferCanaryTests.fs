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
open Projection.Targets.SSDT

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

    // Golden (peer cloud→cloud): every Source user matches a Sink user by email,
    // so the re-key drops nothing — PE-3 isolates the Update-not-re-import proof.
    let goldenSourceSeed =
        "INSERT INTO [dbo].[OSUSR_RC_USER] ([ID],[EMAIL]) VALUES (280,N'alice@x'),(281,N'bob@x'); " +
        "INSERT INTO [dbo].[OSUSR_RC_ORDER] ([ID],[USER_ID],[AMOUNT]) VALUES (10,280,100),(11,281,200);"

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

    /// 6.A.2 — a self-referential IDENTITY kind: PK is IDENTITY
    /// (`AssignedBySink`) and MANAGER_ID is a NULLABLE self-FK, so the
    /// two-phase load would *defer* it to Phase 2. But the Phase-2 re-point
    /// keys on the source PK the Sink replaced — it would match zero rows.
    /// The Transfer must refuse, not emit a no-op UPDATE.
    let cyclicAssignedDdl =
        "CREATE TABLE [dbo].[OSUSR_XF_EMPID] (" +
        "[ID] INT IDENTITY(1,1) NOT NULL PRIMARY KEY, [NAME] NVARCHAR(50) NULL, [MANAGER_ID] INT NULL, " +
        "CONSTRAINT [FK_XfEmpId_Mgr] FOREIGN KEY ([MANAGER_ID]) " +
        "REFERENCES [dbo].[OSUSR_XF_EMPID] ([ID]));"

    let cyclicAssignedSeed =
        "SET IDENTITY_INSERT [dbo].[OSUSR_XF_EMPID] ON; " +
        "INSERT INTO [dbo].[OSUSR_XF_EMPID] ([ID],[NAME],[MANAGER_ID]) VALUES " +
        "(1,N'CEO',NULL),(2,N'VP',1),(3,N'Mgr',2); " +
        "SET IDENTITY_INSERT [dbo].[OSUSR_XF_EMPID] OFF;"

    /// 6.A.3 — a composite IDENTITY PK ([ID] IDENTITY + [TENANT]). `ofKind`
    /// is `AssignedBySink` (the PK carries IDENTITY), but the per-row capture
    /// is single-column and would truncate the composite surrogate. Refuse.
    let compositeAssignedDdl =
        "CREATE TABLE [dbo].[OSUSR_XF_CMP] (" +
        "[ID] INT IDENTITY(1,1) NOT NULL, [TENANT] INT NOT NULL, [NAME] NVARCHAR(50) NULL, " +
        "CONSTRAINT [PK_XfCmp] PRIMARY KEY ([ID],[TENANT]));"

    let compositeAssignedSeed =
        "SET IDENTITY_INSERT [dbo].[OSUSR_XF_CMP] ON; " +
        "INSERT INTO [dbo].[OSUSR_XF_CMP] ([ID],[TENANT],[NAME]) VALUES " +
        "(1,100,N'a'),(2,100,N'b'); " +
        "SET IDENTITY_INSERT [dbo].[OSUSR_XF_CMP] OFF;"

    /// 6.A.4 — a Text column seeded with a genuine empty string, a NULL,
    /// and a non-empty value. `ReadSide.formatRawValue` collapses both the
    /// empty string and NULL to the raw `""`, and `Bulk.parseRaw` maps `""`
    /// back to `DBNull`, so the empty string normalizes to NULL on the sink
    /// (the named tolerance `EmptyTextNormalizedToNull`).
    let emptyTextDdl =
        "CREATE TABLE [dbo].[OSUSR_ET_NOTE] (" +
        "[ID] INT NOT NULL PRIMARY KEY, [BODY] NVARCHAR(50) NULL);"

    let emptyTextSeed =
        "INSERT INTO [dbo].[OSUSR_ET_NOTE] ([ID],[BODY]) VALUES " +
        "(1, N''), (2, NULL), (3, N'hello');"

    /// Per-ID `(BODY value-or-marker, BODY IS NULL)` from a connection.
    let noteBodies (cnn: Microsoft.Data.SqlClient.SqlConnection) : System.Threading.Tasks.Task<(int * bool) list> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <-
                "SELECT [ID], CASE WHEN [BODY] IS NULL THEN 1 ELSE 0 END " +
                "FROM [dbo].[OSUSR_ET_NOTE] ORDER BY [ID];"
            use! reader = cmd.ExecuteReaderAsync()
            let acc = System.Collections.Generic.List<int * bool>()
            let mutable go = true
            while go do
                let! has = reader.ReadAsync()
                if has then acc.Add(reader.GetInt32 0, reader.GetInt32 1 = 1)
                else go <- false
            return List.ofSeq acc
        }

    /// 6.B.1 — a kind with a NULLABLE NOTE column carrying NULL rows. An
    /// `EnforceNotNull` tightening on NOTE would make the two-phase load fail
    /// mid-write; the pre-flight refuses first.
    let tighteningDdl =
        "CREATE TABLE [dbo].[OSUSR_TG_TICKET] (" +
        "[ID] INT NOT NULL PRIMARY KEY, [NOTE] NVARCHAR(50) NULL);"

    let tighteningSeed =
        "INSERT INTO [dbo].[OSUSR_TG_TICKET] ([ID],[NOTE]) VALUES " +
        "(1, N'hi'), (2, NULL), (3, NULL);"

    // 6.B.2 — authored source/sink contracts with STABLE, matching SsKeys so
    // the A→B diff detects the column rename (Email → Contact). The physical
    // column also moves (EMAIL → CONTACT), so a positional/source-name write
    // would mis-map; the rename map re-points by identity. (Authored, not
    // ReadSide-reconstructed: synthesized keys are name-derived and would not
    // thread the rename — see 6.A.7.)
    let private rpName (s: string) : Name = Name.create s |> Result.value
    let private rpKKey (s: string) : SsKey = SsKey.synthesizedComposite "RP_XFER_KIND" [ s ] |> Result.value
    let private rpAKey (s: string) : SsKey = SsKey.synthesizedComposite "RP_XFER_ATTR" [ s ] |> Result.value
    let private rpAttr (key: SsKey) (logical: string) (col: string) (isPk: bool) : Attribute =
        { Attribute.create key (rpName logical) (if isPk then Integer else Text) with
            Column = ColumnRealization.create (col) (not isPk) |> Result.value
            IsPrimaryKey = isPk
            IsMandatory = isPk }
    let private rpContract (emailName: string) (emailCol: string) : Catalog =
        let cust =
            Kind.create (rpKKey "Customer") (rpName "Customer")
                (TableId.create "dbo" "RP_XFER_CUSTOMER" |> Result.value)
                [ rpAttr (rpAKey "Id") "Id" "ID" true
                  rpAttr (rpAKey "Email") emailName emailCol false ]
        Catalog.create
            [ { SsKey = rpKKey "Mod"; Name = rpName "M"; Kinds = [ cust ]; IsActive = true; ExtendedProperties = [] } ] []
        |> Result.value

    /// Source at schema A (Email/EMAIL); sink at schema B (Contact/CONTACT) —
    /// the same kind + attribute SsKeys, so the diff sees a rename.
    let renameSourceContract : Catalog = rpContract "Email" "EMAIL"
    let renameSinkContract : Catalog = rpContract "Contact" "CONTACT"

    // M3 / LE-2 — the legacy B→A reverse leg: ONE SsKey-stable model in two
    // renditions. B (logical) carries clean names ([dbo].[Customer].[Email]);
    // A (physical) carries the OSUSR_* rendition ([dbo].[OSUSR_XF_CUSTOMER].[Contact]).
    // SAME kind + attribute SsKeys (so the A→B diff aligns by identity), differing
    // in BOTH table AND column name — the table-name rendition is resolved per-SsKey
    // against the sink contract; the column-name rendition rides the rename map.
    let private legacyContract (table: string) (emailName: string) (emailCol: string) : Catalog =
        let cust =
            Kind.create (rpKKey "Customer") (rpName "Customer")
                (TableId.create "dbo" table |> Result.value)
                [ rpAttr (rpAKey "Id") "Id" "ID" true
                  rpAttr (rpAKey "Email") emailName emailCol false ]
        Catalog.create
            [ { SsKey = rpKKey "Mod"; Name = rpName "M"; Kinds = [ cust ]; IsActive = true; ExtendedProperties = [] } ] []
        |> Result.value

    /// B (logical on-prem, migration-team-populated) → A (physical cloud OSUSR).
    let legacySourceContract : Catalog = legacyContract "Customer"          "Email"   "EMAIL"
    let legacySinkContract   : Catalog = legacyContract "OSUSR_XF_CUSTOMER" "Contact" "CONTACT"

    /// Scalar string from a connection (for asserting a single cell).
    let scalarStr (cnn: Microsoft.Data.SqlClient.SqlConnection) (sql: string) : System.Threading.Tasks.Task<string> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sql
            let! v = cmd.ExecuteScalarAsync()
            return string v
        }

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

    /// The (Order ID → User EMAIL) join over the RC tables — the identity-
    /// independent relationship the golden cloud→cloud re-key must preserve.
    let orderUserEmailRc (cnn: Microsoft.Data.SqlClient.SqlConnection) : System.Threading.Tasks.Task<(int * string) list> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <-
                "SELECT o.[ID], u.[EMAIL] FROM [dbo].[OSUSR_RC_ORDER] o " +
                "JOIN [dbo].[OSUSR_RC_USER] u ON o.[USER_ID] = u.[ID] ORDER BY o.[ID];"
            use! reader = cmd.ExecuteReaderAsync()
            let acc = System.Collections.Generic.List<int * string>()
            let mutable go = true
            while go do
                let! has = reader.ReadAsync()
                if has then acc.Add(reader.GetInt32 0, reader.GetString 1)
                else go <- false
            return List.ofSeq acc
        }

    /// Resolve `OSUSR_RC_USER` → MatchByColumn EMAIL against a reconstructed
    /// contract — the golden user re-key reconciliation (P-3 ByEmail).
    let resolveUserByEmail (contract: Catalog) : Result<Map<SsKey, ReconciliationStrategy>> =
        let userKind =
            Catalog.allModulesKinds contract |> List.map snd
            |> List.find (fun k -> TableId.tableText k.Physical = "OSUSR_RC_USER")
        let emailName =
            userKind.Attributes
            |> List.find (fun a -> ColumnRealization.columnNameText a.Column = "EMAIL")
            |> fun a -> a.Name
        Ok (Map.ofList [ userKind.SsKey, ReconciliationStrategy.MatchByColumn emailName ])


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
                                    Catalog.allModulesKinds contract |> List.map snd |> List.find (fun k -> TableId.tableText k.Physical = t)
                                let userKind = kindByTable "OSUSR_RC_USER"
                                let orderKind = kindByTable "OSUSR_RC_ORDER"
                                let emailName =
                                    userKind.Attributes |> List.find (fun a -> ColumnRealization.columnNameText a.Column = "EMAIL") |> (fun a -> a.Name)

                                let reconciliation =
                                    Map.ofList [ userKind.SsKey, ReconciliationStrategy.MatchByColumn emailName ]

                                // allowDrops = true: this canary observes the POST-write drop
                                // set (the orphan re-key behavior), so it runs the --allow-drops
                                // path past the AC-I5 pre-write gate. The pre-write halt itself is
                                // witnessed by the pure `validateUserMap` test (fast pool).
                                let! reportR = Transfer.runReconciling Transfer.Execute true true src sink contract reconciliation
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
                                    |> List.find (fun k -> TableId.tableText k.Physical = "OSUSR_RC_USER")
                                let emailName =
                                    userKind.Attributes |> List.find (fun a -> ColumnRealization.columnNameText a.Column = "EMAIL") |> (fun a -> a.Name)
                                let reconciliation =
                                    Map.ofList [ userKind.SsKey, ReconciliationStrategy.MatchByColumn emailName ]

                                // allowDrops = true: this witnesses the POST-write exit-code policy
                                // (AC-D8 drop fail-loud + the --allow-drops downgrade). The AC-I5
                                // pre-write halt (allowDrops = false) is witnessed separately by the
                                // pure `validateUserMap` test.
                                let! reportR = Transfer.runReconciling Transfer.Execute true true src sink contract reconciliation
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

    // 6.A.2 — a cyclic AssignedBySink load (self-ref IDENTITY kind with a
    // nullable self-FK) is refused at Execute, not silently mis-keyed. The
    // Phase-2 re-point would key on the source PK the Sink replaced. The
    // refusal is the pure `executeGate` decision the fast-pool test also
    // witnesses; here the full Transfer.run surfaces it end-to-end. DryRun
    // (no write, no mis-key) proceeds.
    [<Fact>]
    member _.``data canary: cyclic AssignedBySink is refused, not silently mis-keyed`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "XferCyclicAssigned") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "XferCyclicAssignedSrc" (fun src _ ->
                task {
                    do! Deploy.executeBatch src TransferCanaryFixtures.cyclicAssignedDdl
                    do! Deploy.executeBatch src TransferCanaryFixtures.cyclicAssignedSeed
                    return!
                        fixture.WithEphemeralDatabase "XferCyclicAssignedSink" (fun sink _ ->
                            task {
                                do! Deploy.executeBatch sink TransferCanaryFixtures.cyclicAssignedDdl

                                let! contractR = ReadSide.read src
                                let contract = TransferCanaryFixtures.value contractR
                                let empKind =
                                    Catalog.allModulesKinds contract |> List.map snd
                                    |> List.find (fun k -> TableId.tableText k.Physical = "OSUSR_XF_EMPID")
                                // Precondition: the round-tripped contract classifies the kind
                                // AssignedBySink (its IDENTITY PK survives ReadSide).
                                Assert.Equal(IdentityDisposition.AssignedBySink, IdentityDisposition.ofKind empKind)

                                // Execute refuses, fail-loud, with the named code.
                                let! refusedR = Transfer.run Transfer.Execute true src sink contract
                                match refusedR with
                                | Error es ->
                                    Assert.True(
                                        es |> List.exists (fun e -> e.Code = "transfer.cyclicAssignedBySink"),
                                        sprintf "expected transfer.cyclicAssignedBySink, got %A" (es |> List.map (fun e -> e.Code)))
                                | Ok _ -> Assert.Fail("expected the cyclic-AssignedBySink refusal at Execute")

                                // The Sink is untouched (the refusal preceded any write).
                                let! rows = TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_XF_EMPID]"
                                Assert.Equal(0, rows)

                                // DryRun does not write, so it does not mis-key — it reports the plan.
                                let! dryR = Transfer.run Transfer.DryRun true src sink contract
                                match dryR with
                                | Ok report -> Assert.Equal(Transfer.DryRun, report.Mode)
                                | Error es -> Assert.Fail(sprintf "DryRun should not refuse; got %A" (es |> List.map (fun e -> e.Code)))
                            })
                }))

    // 6.A.3 — a composite-IDENTITY AssignedBySink kind is refused, not
    // half-captured: the per-row OUTPUT capture is single-column and would
    // truncate the composite surrogate. The Sink stays empty.
    [<Fact>]
    member _.``data canary: composite-IDENTITY AssignedBySink is refused, not half-captured`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "XferCompositeAssigned") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "XferCompositeAssignedSrc" (fun src _ ->
                task {
                    do! Deploy.executeBatch src TransferCanaryFixtures.compositeAssignedDdl
                    do! Deploy.executeBatch src TransferCanaryFixtures.compositeAssignedSeed
                    return!
                        fixture.WithEphemeralDatabase "XferCompositeAssignedSink" (fun sink _ ->
                            task {
                                do! Deploy.executeBatch sink TransferCanaryFixtures.compositeAssignedDdl

                                let! contractR = ReadSide.read src
                                let contract = TransferCanaryFixtures.value contractR
                                let cmpKind =
                                    Catalog.allModulesKinds contract |> List.map snd
                                    |> List.find (fun k -> TableId.tableText k.Physical = "OSUSR_XF_CMP")
                                // Precondition: composite PK reconstructed (>1 IsPrimaryKey column)
                                // AND classified AssignedBySink (the IDENTITY leg survives ReadSide).
                                Assert.True((cmpKind.Attributes |> List.filter (fun a -> a.IsPrimaryKey) |> List.length) > 1)
                                Assert.Equal(IdentityDisposition.AssignedBySink, IdentityDisposition.ofKind cmpKind)

                                let! refusedR = Transfer.run Transfer.Execute true src sink contract
                                match refusedR with
                                | Error es ->
                                    Assert.True(
                                        es |> List.exists (fun e -> e.Code = "transfer.compositeSurrogateUnsupported"),
                                        sprintf "expected transfer.compositeSurrogateUnsupported, got %A" (es |> List.map (fun e -> e.Code)))
                                | Ok _ -> Assert.Fail("expected the composite-surrogate refusal at Execute")

                                let! rows = TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_XF_CMP]"
                                Assert.Equal(0, rows)
                            })
                }))

    // 6.A.4 — empty-string Text ↔ NULL fidelity. The transfer IR cannot
    // distinguish a genuine empty-string Text value from NULL (ReadSide
    // collapses both to ""), so an empty string normalizes to NULL on the
    // sink. This is the named, CLOSED tolerance `EmptyTextNormalizedToNull`
    // (not a silent drop): the witness asserts the rule explicitly at the
    // sink-DB level (the canary's row-hash can't see it — both sides read "").
    [<Fact>]
    member _.``data canary: empty-string Text normalizes to NULL on transfer (named tolerance)`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "XferEmptyText") then () else
        // The behavior is a named, closed tolerance — not a silent erasure.
        Assert.Contains(ToleratedDivergence.EmptyTextNormalizedToNull, ToleratedDivergence.allKnown)
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "XferEmptyTextSrc" (fun src _ ->
                task {
                    do! Deploy.executeBatch src TransferCanaryFixtures.emptyTextDdl
                    do! Deploy.executeBatch src TransferCanaryFixtures.emptyTextSeed
                    return!
                        fixture.WithEphemeralDatabase "XferEmptyTextSink" (fun sink _ ->
                            task {
                                do! Deploy.executeBatch sink TransferCanaryFixtures.emptyTextDdl

                                let! contractR = ReadSide.read src
                                let contract = TransferCanaryFixtures.value contractR
                                let! reportR = Transfer.run Transfer.Execute true src sink contract
                                let _ = TransferCanaryFixtures.value reportR

                                // Source: row 1 is '' (NOT NULL), row 2 NULL, row 3 'hello'.
                                let! srcBodies = TransferCanaryFixtures.noteBodies src
                                Assert.Equal<(int * bool) list>([ (1, false); (2, true); (3, false) ], srcBodies)

                                // Sink: row 1's empty string normalized to NULL (the tolerance);
                                // NULL stays NULL; 'hello' survives non-null.
                                let! sinkBodies = TransferCanaryFixtures.noteBodies sink
                                Assert.Equal<(int * bool) list>([ (1, true); (2, true); (3, false) ], sinkBodies)
                            })
                }))

    // 6.B.1 — the Decision↔Data pre-flight. A tightening Decision
    // (EnforceNotNull) on a column whose source data carries NULLs is refused
    // (migrate.dataViolatesTightening) BEFORE any write — the coupling becomes
    // a named gate, not a crash mid-load. A clean overlay passes.
    [<Fact>]
    member _.``migrate pre-flight: EnforceNotNull on a NULL-bearing column refuses before writing`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "TighteningPreflight") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "TighteningPreflight" (fun src _ ->
                task {
                    do! Deploy.executeBatch src TransferCanaryFixtures.tighteningDdl
                    do! Deploy.executeBatch src TransferCanaryFixtures.tighteningSeed

                    let! contractR = ReadSide.read src
                    let contract = TransferCanaryFixtures.value contractR
                    let ticketKind =
                        Catalog.allModulesKinds contract |> List.map snd
                        |> List.find (fun k -> TableId.tableText k.Physical = "OSUSR_TG_TICKET")
                    let noteKey =
                        ticketKind.Attributes
                        |> List.find (fun a -> ColumnRealization.columnNameText a.Column = "NOTE")
                        |> (fun a -> a.SsKey)

                    // Clean (empty) overlay → the pre-flight passes.
                    let! clean = Preflight.tighteningPreflight src contract DecisionOverlay.empty
                    match clean with
                    | Ok () -> ()
                    | Error es -> Assert.Fail(sprintf "empty overlay should pass the pre-flight; got %A" (es |> List.map (fun e -> e.Code)))

                    // EnforceNotNull on NOTE (which has NULL rows) → refuse before writing.
                    let overlay = { DecisionOverlay.empty with EnforceNotNull = Set.singleton noteKey }
                    let! refused = Preflight.tighteningPreflight src contract overlay
                    match refused with
                    | Error es ->
                        Assert.True(
                            es |> List.exists (fun e -> e.Code = "migrate.dataViolatesTightening"),
                            sprintf "expected migrate.dataViolatesTightening, got %A" (es |> List.map (fun e -> e.Code)))
                    | Ok () -> Assert.Fail("expected the tightening pre-flight to refuse a NULL-bearing EnforceNotNull")
                }))

    // G7 — the WIRED migrate seam: the overlay is DERIVED from an A→B catalog
    // displacement (`tighteningOverlay` = the verb's exact composition), not
    // hand-built. A target B that narrows NOTE (nullable, NULL-bearing) to NOT
    // NULL refuses BEFORE any DDL; the table is read back UNCHANGED (NOTE still
    // nullable, all three rows intact). Discrimination: against the un-wired
    // code the verb would run the ALTER and crash mid-write on the NULL rows;
    // here the derived overlay is non-empty and the probe refuses first.
    [<Fact>]
    member _.``G7 witness: a migrate narrowing NOTE to NOT NULL on NULL data refuses before any DDL; table unchanged`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "G7Tightening") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "G7Tightening" (fun cnn _ ->
                task {
                    do! Deploy.executeBatch cnn TransferCanaryFixtures.tighteningDdl
                    do! Deploy.executeBatch cnn TransferCanaryFixtures.tighteningSeed

                    // State A — the deployed (NOTE nullable) contract, read live.
                    let! sourceAR = ReadSide.read cnn
                    let sourceA = TransferCanaryFixtures.value sourceAR

                    // State B — the SAME catalog with NOTE narrowed to NOT NULL.
                    let narrowNote (a: Attribute) : Attribute =
                        if ColumnRealization.columnNameText a.Column = "NOTE"
                        then { a with Column = ColumnRealization.create "NOTE" false |> Result.value }
                        else a
                    let target =
                        let mods =
                            sourceA.Modules
                            |> List.map (fun m ->
                                { m with Kinds = m.Kinds |> List.map (fun k -> { k with Attributes = k.Attributes |> List.map narrowNote }) })
                        Catalog.create mods [] |> Result.value

                    // The verb's exact derivation: overlay from the displacement.
                    let overlay = Preflight.tighteningOverlay sourceA target
                    Assert.False(Set.isEmpty overlay.EnforceNotNull, "the A→B displacement must narrow NOTE to NOT NULL")

                    // Wired refusal — probe the live data, refuse before any write.
                    let! refused = Preflight.tighteningPreflight cnn sourceA overlay
                    match refused with
                    | Error es ->
                        Assert.True(
                            es |> List.exists (fun e -> e.Code = "migrate.dataViolatesTightening"),
                            sprintf "expected migrate.dataViolatesTightening, got %A" (es |> List.map (fun e -> e.Code)))
                    | Ok () -> Assert.Fail("the derived overlay must refuse the NULL-bearing tightening before any DDL")

                    // Read back — NO DDL ran: NOTE is still nullable, rows intact.
                    let! isNullable =
                        TransferCanaryFixtures.scalarStr cnn
                            "SELECT IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='OSUSR_TG_TICKET' AND COLUMN_NAME='NOTE';"
                    Assert.Equal("YES", isNullable)
                    let! rowCount = TransferCanaryFixtures.countRows cnn "[dbo].[OSUSR_TG_TICKET]"
                    Assert.Equal(3, rowCount)
                }))

    // G1 — the transfer spanning pre-flight (connection), WIRED into the
    // Execute path. Driving `Transfer.run Execute` against a dead/unreachable
    // sink refuses with `transfer.connectionUnavailable` BEFORE any write.
    // Discrimination: against the un-wired `runCore` (which opened both
    // endpoints and ran straight into ingest/plan/write), the dead sink would
    // surface as a *mid-load* failure carrying some other code — not the named
    // connection refusal. Asserting the SPECIFIC code is what fails the
    // un-wired behavior. The source is read back untouched.
    [<Fact>]
    member _.``G1 witness: Transfer.run Execute refuses a dead sink endpoint with the connection code before any write`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "G1Connection") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "G1Connection" (fun src _ ->
                task {
                    do! Deploy.executeBatch src TransferCanaryFixtures.twoTableDdl
                    do! Deploy.executeBatch src TransferCanaryFixtures.twoTableSeed
                    let! contractR = ReadSide.read src
                    let contract = TransferCanaryFixtures.value contractR

                    // A sink pointed at an unreachable endpoint (a port nothing
                    // listens on), short login timeout so the probe fails fast.
                    use deadSink =
                        new Microsoft.Data.SqlClient.SqlConnection(
                            "Server=127.0.0.1,1;Database=nope;User Id=sa;Password=nope;TrustServerCertificate=true;Connect Timeout=3")
                    // allowCdc=true so the CDC gate is not what fires; the
                    // spanning gate is the one under test.
                    let! result = Transfer.run Transfer.Execute true src deadSink contract
                    match result with
                    | Error es ->
                        Assert.True(
                            es |> List.exists (fun e -> e.Code = "transfer.connectionUnavailable"),
                            sprintf "expected transfer.connectionUnavailable (the wired G1 refusal), got %A" (es |> List.map (fun e -> e.Code)))
                    | Ok _ -> Assert.Fail("Transfer.run Execute must refuse a dead sink endpoint before any write")

                    // The source is unchanged — the gate ran before any mutation.
                    let! custRows = TransferCanaryFixtures.countRows src "[dbo].[OSUSR_XF_CUSTOMER]"
                    Assert.Equal(2, custRows)
                }))

    // 6.B.2 — RefactorLog-aware Transfer end-to-end. The source is at schema A
    // (EMAIL); the sink is at schema B (the column renamed to CONTACT). The
    // rename map (from the A→B diff) re-points each source row's value onto the
    // sink's name BY IDENTITY — so the renamed column carries the source data,
    // not NULL (which a source-name or ordinal write would produce).
    [<Fact>]
    member _.``transfer: a renamed column is re-pointed by the rename map, not matched by ordinal`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "XferRename") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "XferRenameSrc" (fun src _ ->
                task {
                    do! Deploy.executeBatch src (SsdtDdlEmitter.statements TransferCanaryFixtures.renameSourceContract |> Render.toText)
                    do! Deploy.executeBatch src
                            "INSERT INTO [dbo].[RP_XFER_CUSTOMER] ([ID],[EMAIL]) VALUES (1, N'alice@x'), (2, N'bob@x');"
                    return!
                        fixture.WithEphemeralDatabase "XferRenameSink" (fun sink _ ->
                            task {
                                do! Deploy.executeBatch sink (SsdtDdlEmitter.statements TransferCanaryFixtures.renameSinkContract |> Render.toText)

                                let! reportR =
                                    Transfer.runWithRenames Transfer.Execute true src sink
                                        TransferCanaryFixtures.renameSourceContract
                                        TransferCanaryFixtures.renameSinkContract
                                let _ = TransferCanaryFixtures.value reportR

                                // The renamed CONTACT column carries the source's EMAIL values —
                                // re-pointed by the rename map (identity), not lost to NULL.
                                let! c1 = TransferCanaryFixtures.scalarStr sink "SELECT [CONTACT] FROM [dbo].[RP_XFER_CUSTOMER] WHERE [ID]=1;"
                                Assert.Equal("alice@x", c1)
                                let! c2 = TransferCanaryFixtures.scalarStr sink "SELECT [CONTACT] FROM [dbo].[RP_XFER_CUSTOMER] WHERE [ID]=2;"
                                Assert.Equal("bob@x", c2)
                                let! n = TransferCanaryFixtures.countRows sink "[dbo].[RP_XFER_CUSTOMER]"
                                Assert.Equal(2, n)
                            })
                }))

    // M3 / LE-2 — the legacy B→A reverse-leg canary. The migration team's data
    // sits in the LOGICAL on-prem rendition ([dbo].[Customer].[Email]); the engine
    // pipes it UP into the PHYSICAL cloud rendition ([dbo].[OSUSR_XF_CUSTOMER].[Contact]).
    // Same SsKey-stable model — NOT foreign schema, no tolerances. The two-contract
    // path resolves the write target table against the SINK contract by SsKey
    // (table-name rendition) and re-points the column by the rename map (column-name
    // rendition), so CONTACT in the OSUSR sink carries the source's EMAIL data.
    [<Fact>]
    member _.``M3/LE-2 legacy B->A: logical source pipes up into the physical OSUSR sink, data round-trips by SsKey`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "LegacyBA") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "LegacyBASrc" (fun src _ ->
                task {
                    // B (logical): the hosted on-prem model the migration team filled.
                    do! Deploy.executeBatch src (SsdtDdlEmitter.statements TransferCanaryFixtures.legacySourceContract |> Render.toText)
                    do! Deploy.executeBatch src
                            "INSERT INTO [dbo].[Customer] ([ID],[EMAIL]) VALUES (1, N'alice@x'), (2, N'bob@x');"
                    return!
                        fixture.WithEphemeralDatabase "LegacyBASink" (fun sink _ ->
                            task {
                                // A (physical): the frozen OSUSR_* cloud rendition, empty.
                                do! Deploy.executeBatch sink (SsdtDdlEmitter.statements TransferCanaryFixtures.legacySinkContract |> Render.toText)

                                // The up leg (B→A) over the same model.
                                let! reportR =
                                    Transfer.runWithRenames Transfer.Execute true src sink
                                        TransferCanaryFixtures.legacySourceContract
                                        TransferCanaryFixtures.legacySinkContract
                                let _ = TransferCanaryFixtures.value reportR

                                // The physical OSUSR sink's CONTACT column carries the logical
                                // source's EMAIL data — table + column rendition resolved by SsKey,
                                // not lost to NULL (a source-name/ordinal write) or a missing table.
                                let! c1 = TransferCanaryFixtures.scalarStr sink "SELECT [CONTACT] FROM [dbo].[OSUSR_XF_CUSTOMER] WHERE [ID]=1;"
                                Assert.Equal("alice@x", c1)
                                let! c2 = TransferCanaryFixtures.scalarStr sink "SELECT [CONTACT] FROM [dbo].[OSUSR_XF_CUSTOMER] WHERE [ID]=2;"
                                Assert.Equal("bob@x", c2)
                                let! n = TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_XF_CUSTOMER]"
                                Assert.Equal(2, n)
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
                                    |> List.find (fun k -> TableId.tableText k.Physical = "OSUSR_AS_USER")
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

                                    // allowDrops = true: observe the post-write drop set (999 is
                                    // outside the override map) past the AC-I5 pre-write gate.
                                    let! reportR =
                                        Transfer.runThroughConnections Transfer.Execute true true connections resolveReconciliation
                                    let report = TransferCanaryFixtures.value reportR

                                    // Map tables → reconstructed SsKeys for the assertions.
                                    let! contractR = ReadSide.read src
                                    let contract = TransferCanaryFixtures.value contractR
                                    let kindByTable (t: string) =
                                        Catalog.allModulesKinds contract |> List.map snd |> List.find (fun k -> TableId.tableText k.Physical = t)
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

    // PE-3 — the golden cloud→cloud re-key canary (P-REKEY / AC-I2). A `golden`
    // flow copies a subset (`tables = [Order]`) and re-keys the user FK to the
    // Sink's OWN users by EMAIL. User is EXCLUDED from the copied set yet KEPT
    // for reconcile (it builds the email remap; its rows are never written).
    // Proof the re-key is an Update, not a re-import: the (Order→User-by-email)
    // join is identical Source↔Sink, the Source user surrogates are provably
    // ABSENT from the Sink, and the Sink's own user inventory is unchanged.
    [<Fact>]
    member _.``PE-3 golden: cloud->cloud re-key — User excluded, FKs re-keyed by email (P-REKEY/AC-I2)`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "GoldenPE3") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "GoldenPE3Src" (fun src srcConnStr ->
                task {
                    do! Deploy.executeBatch src TransferCanaryFixtures.reKeyDdl
                    do! Deploy.executeBatch src TransferCanaryFixtures.goldenSourceSeed
                    return!
                        fixture.WithEphemeralDatabase "GoldenPE3Sink" (fun sink sinkConnStr ->
                            task {
                                do! Deploy.executeBatch sink TransferCanaryFixtures.reKeyDdl
                                do! Deploy.executeBatch sink TransferCanaryFixtures.reKeySinkSeed

                                // The Order kind's LOGICAL name — the golden `tables` subset
                                // (`resolveLoadSet` matches the declared subset by logical Name).
                                let! contractR = ReadSide.read src
                                let contract = TransferCanaryFixtures.value contractR
                                let kindByTable (t: string) =
                                    Catalog.allModulesKinds contract |> List.map snd
                                    |> List.find (fun k -> TableId.tableText k.Physical = t)
                                let orderKind = kindByTable "OSUSR_RC_ORDER"
                                let userKind  = kindByTable "OSUSR_RC_USER"
                                let orderName = Name.value orderKind.Name

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

                                    // tables = [Order]; reconcile User by email. allowDrops = false:
                                    // every Source user matches, so the AC-I5 gate passes cleanly.
                                    let! reportR =
                                        Transfer.runThroughConnectionsWithEmission
                                            Transfer.Execute EmissionMode.Incremental true false
                                            [ orderName ] connections TransferCanaryFixtures.resolveUserByEmail
                                    let report = TransferCanaryFixtures.value reportR

                                    // (c) User is reconciled, not copied — 0 rows written, no drops.
                                    let userOutcome = report.Kinds |> List.find (fun k -> k.Kind = userKind.SsKey)
                                    Assert.Equal(IdentityDisposition.ReconciledByRule, userOutcome.Disposition)
                                    Assert.Equal(0, userOutcome.RowsWritten)
                                    Assert.Empty(report.UnmatchedIdentities)
                                    Assert.Empty(report.SkippedReferences)

                                    // (a) The (Order→User-by-email) join is identical Source↔Sink.
                                    let! srcPairs = TransferCanaryFixtures.orderUserEmailRc src
                                    let! sinkPairs = TransferCanaryFixtures.orderUserEmailRc sink
                                    Assert.Equal<(int * string) list>(srcPairs, sinkPairs)
                                    Assert.Equal<(int * string) list>([ (10, "alice@x"); (11, "bob@x") ], sinkPairs)

                                    // (b) Source user surrogates ABSENT from the Sink; Sink's own
                                    // 2 users unchanged; every Order FK re-pointed to a Sink surrogate.
                                    let! users  = TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_RC_USER]"
                                    let! orders = TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_RC_ORDER]"
                                    Assert.Equal(2, users)
                                    Assert.Equal(2, orders)
                                    let! srcUsersInSink =
                                        TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_RC_USER] WHERE [ID] IN (280,281)"
                                    let! srcFksInSink =
                                        TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_RC_ORDER] WHERE [USER_ID] IN (280,281)"
                                    Assert.Equal(0, srcUsersInSink)
                                    Assert.Equal(0, srcFksInSink)
                                finally
                                    try File.Delete srcFile with _ -> ()
                                    try File.Delete sinkFile with _ -> ()
                            })
                }))

    // PE-2 — the validate-user-map pre-write halt on the golden flow (AC-I5). A
    // Source user with no Sink email match (ghost 999) makes Order 12 an unmapped
    // FK; with --allow-drops OFF the load refuses BEFORE any DML — no silent NULL,
    // no partial write. The Sink Order table stays empty (untouched).
    [<Fact>]
    member _.``PE-2 golden: an unmapped user FK halts the cloud load before any DML (AC-I5)`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "GoldenPE2") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "GoldenPE2Src" (fun src srcConnStr ->
                task {
                    do! Deploy.executeBatch src TransferCanaryFixtures.reKeyDdl
                    do! Deploy.executeBatch src TransferCanaryFixtures.reKeySourceSeed   // includes ghost 999
                    return!
                        fixture.WithEphemeralDatabase "GoldenPE2Sink" (fun sink sinkConnStr ->
                            task {
                                do! Deploy.executeBatch sink TransferCanaryFixtures.reKeyDdl
                                do! Deploy.executeBatch sink TransferCanaryFixtures.reKeySinkSeed

                                let! contractR = ReadSide.read src
                                let contract = TransferCanaryFixtures.value contractR
                                let orderName =
                                    Catalog.allModulesKinds contract |> List.map snd
                                    |> List.find (fun k -> TableId.tableText k.Physical = "OSUSR_RC_ORDER")
                                    |> fun k -> Name.value k.Name

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

                                    let! reportR =
                                        Transfer.runThroughConnectionsWithEmission
                                            Transfer.Execute EmissionMode.Incremental true false
                                            [ orderName ] connections TransferCanaryFixtures.resolveUserByEmail
                                    // The unmapped 999 halts pre-write: Error transfer.unmappedIdentities.
                                    match reportR with
                                    | Error es ->
                                        Assert.True(
                                            es |> List.exists (fun e -> e.Code = "transfer.unmappedIdentities"),
                                            sprintf "expected transfer.unmappedIdentities, got %A" (es |> List.map (fun e -> e.Code)))
                                    | Ok _ -> Assert.Fail("expected the validate-user-map pre-write halt on the unmapped user FK")

                                    // No DML reached the Sink — the Order table is still empty.
                                    let! orders = TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_RC_ORDER]"
                                    Assert.Equal(0, orders)
                                finally
                                    try File.Delete srcFile with _ -> ()
                                    try File.Delete sinkFile with _ -> ()
                            })
                }))
