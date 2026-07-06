namespace Projection.Tests

// The peer (A→A) transfer UNDER THE MANAGED-CLOUD GRANT (2026-07-06, the
// phase-2 mock-environment program). The first peer suite proved identity
// alignment and FK re-pointing AS ADMIN; this suite proves the same engine
// path — and the permission model itself — under the REAL cloud envelope:
// a principal carrying exactly database-scope SELECT/INSERT/UPDATE/DELETE
// (no ALTER, no CREATE TABLE, no IDENTITY_INSERT), on BOTH sides.
//
//   1. The GRANT-CONFORMANCE PROBE: the mock principal's fn_my_permissions
//      evidence is exactly what the engine's preflight reads; the denied
//      capabilities fail with the documented error classes; #temp + MERGE
//      succeed — "confirm our understanding of the permissions."
//   2. The peer subset happy path, DML-only both sides: rows land in the
//      differently-named sink, FKs re-point through sink-minted keys, and
//      the load needs NO ALTER at all — FK-targeted kinds ride the MERGE
//      capture lane, which validates constraints INLINE (the FK ends
//      enabled AND trusted; the bulk-lane untrusted/descend tolerance
//      belongs to non-FK-targeted kinds — the reverse-leg DML-only
//      canaries own that witness).
//   3. Reconcile-by-key under the grant (SELECT-only touch on the target).
//   4. OSSYS metamodel unreadable on the sink → the NAMED contract
//      acquisition refusal (schema-read axis, exit 6) — never a raw crash.
//   5. Object-scope DENY INSERT mid-subset: the DB-scope preflight is blind
//      to it (G1, PINNED) — raw permission exception mid-load, upstream
//      kinds already landed. The named-gap witness on the peer dispatch,
//      cross-referencing the reserved promotion stub in
//      ReverseLegBoundaryTests.
//
// Serial via Docker-SqlServer; blocking wait via TaskSync.

open Xunit
open Projection.Core
open Projection.Pipeline

module private ManagedGrantFixtures =

    let skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else printfn "SKIP %s: Docker daemon not reachable." label; false

    let value (r: Result<'a>) : 'a = Result.value r

    /// Deterministic source rows (cell A carries the unshifted `OSUSR_*`
    /// names): two cities, two customers pointing at them.
    let sourceRows =
        [ "SET IDENTITY_INSERT [dbo].[OSUSR_DEF_CITY] ON; \
           INSERT INTO [dbo].[OSUSR_DEF_CITY] ([ID],[NAME],[ISACTIVE]) VALUES (1, N'Lisbon', 1), (2, N'Porto', 1); \
           SET IDENTITY_INSERT [dbo].[OSUSR_DEF_CITY] OFF; \
           SET IDENTITY_INSERT [dbo].[OSUSR_ABC_CUSTOMER] ON; \
           INSERT INTO [dbo].[OSUSR_ABC_CUSTOMER] ([ID],[EMAIL],[FIRSTNAME],[LASTNAME],[CITYID]) VALUES \
               (10, N'alice@x', N'Alice', N'Almeida', 1), (11, N'bob@x', N'Bob', N'Barbosa', 2); \
           SET IDENTITY_INSERT [dbo].[OSUSR_ABC_CUSTOMER] OFF;" ]

    /// Sink identity ranges shifted away from the source surrogates so a
    /// verbatim FK copy cannot pass the join assertions.
    let sinkReseed =
        [ "DBCC CHECKIDENT ('[dbo].[OSUSR_XDEF_CITY]', RESEED, 500); \
           DBCC CHECKIDENT ('[dbo].[OSUSR_XABC_CUSTOMER]', RESEED, 900);" ]

    /// Pre-seeded sink cities under the sink's OWN surrogates — the
    /// reconcile scenario's match targets.
    let sinkCityRows =
        [ "SET IDENTITY_INSERT [dbo].[OSUSR_XDEF_CITY] ON; \
           INSERT INTO [dbo].[OSUSR_XDEF_CITY] ([ID],[NAME],[ISACTIVE]) VALUES (501, N'Lisbon', 1), (502, N'Porto', 1); \
           SET IDENTITY_INSERT [dbo].[OSUSR_XDEF_CITY] OFF;" ]

    let countRows (cnn: Microsoft.Data.SqlClient.SqlConnection) (table: string) : System.Threading.Tasks.Task<int> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sprintf "SELECT COUNT_BIG(*) FROM %s;" table
            let! scalar = cmd.ExecuteScalarAsync()
            return int (unbox<int64> scalar)
        }

    let scalarInt (cnn: Microsoft.Data.SqlClient.SqlConnection) (sql: string) : System.Threading.Tasks.Task<int> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sql
            let! scalar = cmd.ExecuteScalarAsync()
            return System.Convert.ToInt32 scalar
        }

    let exec (cnn: Microsoft.Data.SqlClient.SqlConnection) (sql: string) : System.Threading.Tasks.Task =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sql
            let! _ = cmd.ExecuteNonQueryAsync()
            return ()
        }

    /// Open a raw connection (the restricted engine string).
    let openConn (connStr: string) : System.Threading.Tasks.Task<Microsoft.Data.SqlClient.SqlConnection> =
        task {
            let c = new Microsoft.Data.SqlClient.SqlConnection(connStr)
            do! c.OpenAsync()
            return c
        }

    /// The engine apparatus over two ENGINE connection strings (the D9
    /// file-ref pattern) — under `ManagedDml` these are the restricted
    /// logins, so the whole run pays the cloud envelope.
    let throughConnections
        (srcConnStr: string)
        (sinkConnStr: string)
        (reconcile: bool)
        (body: TransferConnections -> System.Threading.Tasks.Task<'a>)
        : System.Threading.Tasks.Task<'a> =
        task {
            // `ConnectionRef.Raw` (DECISIONS 2026-07-06): the ephemeral-DB
            // string is already in memory — no temp-file round trip.
            let srcSub : Substrate =
                { Environment = Projection.Core.Environment.Qa
                  Role = SubstrateRole.Source
                  ConnectionRef = ConnectionRef.Raw srcConnStr }
            let sinkSub : Substrate =
                { Environment = Projection.Core.Environment.Uat
                  Role = SubstrateRole.Sink
                  ConnectionRef = ConnectionRef.Raw sinkConnStr }
            let connections = TransferConnections.create srcSub sinkSub reconcile |> value
            return! body connections
        }

    let kindByLogicalName (contract: Catalog) (name: string) : Kind =
        Catalog.allKinds contract
        |> List.find (fun k -> System.String.Equals(Name.value k.Name, name, System.StringComparison.OrdinalIgnoreCase))

[<Xunit.Collection("Docker-SqlServer")>]
type PeerManagedGrantTransferDockerTests(fixture: EphemeralContainerFixture) =
    interface IClassFixture<EphemeralContainerFixture>

    /// The permission-understanding probe: the mock managed principal's
    /// grant evidence and denial classes are EXACTLY the documented cloud
    /// profile — pinning the harness itself so every other test's premise
    /// is verified, not assumed.
    [<Fact>]
    member _.``managed grant conformance: fn_my_permissions is exactly the DML set; denied capabilities fail in their documented classes; tempdb and MERGE succeed`` () =
        if not (ManagedGrantFixtures.skipIfNoDocker "GrantConformance") then () else
        TaskSync.run (fun () ->
            MockOutSystemsEnv.withMockEnv fixture "GrantConf" "" [] MockOutSystemsEnv.ManagedDml (fun env ->
                task {
                    use! cnn = ManagedGrantFixtures.openConn env.EngineConnStr
                    // (a) The evidence the engine's preflight reads: exactly
                    // CONNECT + the four DML grants (explicit grants — a
                    // role-based principal would report NONE of these and
                    // false-trip the insufficient-grant refusal).
                    let! perms = DmlPrincipal.selfPermissions cnn
                    // The DML set is PRESENT as database-scope grants (what
                    // `Preflight.captureGrantEvidence` needs to see)…
                    for required in [ "CONNECT"; "SELECT"; "INSERT"; "UPDATE"; "DELETE" ] do
                        Assert.Contains(required, perms)
                    // …and the write-plane-relevant elevated rights are ABSENT.
                    // (SQL Server 2022 also reports two always-granted
                    // VIEW ANY COLUMN * KEY DEFINITION rows to every user —
                    // irrelevant to the engine; the assertion is
                    // presence/absence, not set equality.)
                    for forbidden in [ "ALTER"; "CREATE TABLE"; "CONTROL"; "VIEW DEFINITION"; "REFERENCES"; "EXECUTE" ] do
                        Assert.DoesNotContain(forbidden, perms)
                    // (b) IDENTITY_INSERT is structurally unavailable (the
                    // 1088/8104 class) — the engine must never need it.
                    let! identityInsert =
                        task {
                            try
                                do! ManagedGrantFixtures.exec cnn "SET IDENTITY_INSERT [dbo].[OSUSR_DEF_CITY] ON;"
                                return false
                            with ex -> return DmlPrincipal.isPermissionDenied ex
                        }
                    Assert.True(identityInsert, "SET IDENTITY_INSERT must be denied under the managed grant")
                    // (c) CREATE TABLE denied (error 262) — the G10
                    // sink-resident progress table can never exist here.
                    let! createTable =
                        task {
                            try
                                do! ManagedGrantFixtures.exec cnn "CREATE TABLE dbo.__probe_ct (Id INT);"
                                return false
                            with ex -> return DmlPrincipal.isPermissionDenied ex
                        }
                    Assert.True(createTable, "CREATE TABLE must be denied under the managed grant")
                    // (d) ALTER denied — the FK re-trust must descend, never succeed.
                    let! alter =
                        task {
                            try
                                do! ManagedGrantFixtures.exec cnn "ALTER TABLE [dbo].[OSUSR_ABC_CUSTOMER] WITH CHECK CHECK CONSTRAINT ALL;"
                                return false
                            with ex -> return DmlPrincipal.isPermissionDenied ex
                        }
                    Assert.True(alter, "ALTER must be denied under the managed grant")
                    // (e) The capture lane's transport fits the grant: #temp
                    // SELECT INTO + MERGE…OUTPUT INTO #keymap succeed.
                    do! ManagedGrantFixtures.exec cnn
                            "SELECT TOP 0 [ID] AS [__SRC_KEY], [NAME], [ISACTIVE] INTO #probe_stage FROM [dbo].[OSUSR_DEF_CITY]; \
                             CREATE TABLE #probe_keymap ([__SRC_KEY] BIGINT, [ASSIGNED] BIGINT); \
                             MERGE INTO [dbo].[OSUSR_DEF_CITY] AS T \
                             USING (SELECT [__SRC_KEY], [NAME], [ISACTIVE] FROM #probe_stage) AS S ON 1 = 0 \
                             WHEN NOT MATCHED THEN INSERT ([NAME],[ISACTIVE]) VALUES (S.[NAME], S.[ISACTIVE]) \
                             OUTPUT S.[__SRC_KEY], INSERTED.[ID] INTO #probe_keymap ([__SRC_KEY],[ASSIGNED]); \
                             DROP TABLE #probe_stage; DROP TABLE #probe_keymap;"
                    return ()
                }))

    /// The headline under the grant: subset [City; Customer] between two
    /// espace-variant cells, engine driven by DML-only logins on BOTH sides.
    /// A finding worth its own pin: BOTH kinds here are FK-targeted, so they
    /// ride the MERGE capture lane, which validates constraints INLINE —
    /// the sink FK ends VALID AND TRUSTED with no ALTER ever needed (the
    /// bulk-lane untrusted/descend path belongs to non-FK-targeted kinds;
    /// the reverse-leg DML-only canaries own that witness).
    [<Fact>]
    member _.``peer under managed grant: the subset lands, FKs re-point, and the capture lane needs no ALTER (FK stays enabled and trusted)`` () =
        if not (ManagedGrantFixtures.skipIfNoDocker "PeerManagedHappy") then () else
        TaskSync.run (fun () ->
            MockOutSystemsEnv.withMockEnvPair fixture "PeerMg"
                "" ManagedGrantFixtures.sourceRows MockOutSystemsEnv.ManagedDml
                "X" ManagedGrantFixtures.sinkReseed MockOutSystemsEnv.ManagedDml
                (fun src snk ->
                    task {
                        // Contracts acquired OVER THE RESTRICTED LOGINS — the
                        // OSSYS metamodel read itself is proven inside the grant.
                        let! contractsR = PeerTransfer.acquireContracts src.EngineConnStr snk.EngineConnStr
                        let (srcContract, sinkContract) = ManagedGrantFixtures.value contractsR

                        let! report =
                            ManagedGrantFixtures.throughConnections src.EngineConnStr snk.EngineConnStr false (fun connections ->
                                task {
                                    let! r =
                                        Transfer.runReverseLegThroughConnections
                                            Transfer.Execute EmissionMode.Incremental false true false
                                            [ "City"; "Customer" ] connections srcContract sinkContract Map.empty
                                    return ManagedGrantFixtures.value r
                                })

                        Assert.Empty(report.SkippedReferences)
                        Assert.Empty(report.UnmatchedIdentities)
                        let! cities = ManagedGrantFixtures.countRows snk.Admin "[dbo].[OSUSR_XDEF_CITY]"
                        let! customers = ManagedGrantFixtures.countRows snk.Admin "[dbo].[OSUSR_XABC_CUSTOMER]"
                        Assert.Equal(2, cities)
                        Assert.Equal(2, customers)
                        let! verbatim = ManagedGrantFixtures.countRows snk.Admin "[dbo].[OSUSR_XABC_CUSTOMER] WHERE [CITYID] IN (1, 2)"
                        Assert.Equal(0, verbatim)
                        // The FK survived the load VALID: enabled, TRUSTED
                        // (the MERGE lane validated it inline — no bulk
                        // bypass, no ALTER needed), and zero dangling rows.
                        let! fkState =
                            ManagedGrantFixtures.scalarInt snk.Admin
                                "SELECT COUNT(*) FROM sys.foreign_keys WHERE name = 'FK_OSUSR_XABC_CUSTOMER_OSUSR_XDEF_CITY' AND is_disabled = 0 AND is_not_trusted = 0;"
                        Assert.Equal(1, fkState)
                        let! dangling =
                            ManagedGrantFixtures.countRows snk.Admin
                                "[dbo].[OSUSR_XABC_CUSTOMER] c WHERE NOT EXISTS (SELECT 1 FROM [dbo].[OSUSR_XDEF_CITY] ci WHERE ci.[ID] = c.[CITYID])"
                        Assert.Equal(0, dangling)
                        return ()
                    }))

    /// Reconcile-by-key under the grant: the out-of-subset parent matches
    /// rows the sink already holds; the reconcile leg's sink reads and the
    /// FK re-key fit SELECT/INSERT/UPDATE/DELETE.
    [<Fact>]
    member _.``peer under managed grant: an out-of-subset parent reconciles by business key`` () =
        if not (ManagedGrantFixtures.skipIfNoDocker "PeerManagedRecon") then () else
        TaskSync.run (fun () ->
            MockOutSystemsEnv.withMockEnvPair fixture "PeerMgRec"
                "" ManagedGrantFixtures.sourceRows MockOutSystemsEnv.ManagedDml
                "X" (ManagedGrantFixtures.sinkReseed @ ManagedGrantFixtures.sinkCityRows) MockOutSystemsEnv.ManagedDml
                (fun src snk ->
                    task {
                        let! contractsR = PeerTransfer.acquireContracts src.EngineConnStr snk.EngineConnStr
                        let (srcContract, sinkContract) = ManagedGrantFixtures.value contractsR
                        let cityKind = ManagedGrantFixtures.kindByLogicalName sinkContract "City"
                        let cityName =
                            cityKind.Attributes
                            |> List.find (fun a -> ColumnRealization.columnNameText a.Column = "NAME")
                        let reconciliation = Map.ofList [ cityKind.SsKey, ReconciliationStrategy.MatchByColumn cityName.Name ]

                        let! report =
                            ManagedGrantFixtures.throughConnections src.EngineConnStr snk.EngineConnStr true (fun connections ->
                                task {
                                    let! r =
                                        Transfer.runReverseLegThroughConnections
                                            Transfer.Execute EmissionMode.Incremental false true false
                                            [ "Customer" ] connections srcContract sinkContract reconciliation
                                    return ManagedGrantFixtures.value r
                                })

                        Assert.Empty(report.UnmatchedIdentities)
                        Assert.Empty(report.SkippedReferences)
                        let! cities = ManagedGrantFixtures.countRows snk.Admin "[dbo].[OSUSR_XDEF_CITY]"
                        Assert.Equal(2, cities)   // the sink's OWN rows; none inserted
                        let! reKeyed = ManagedGrantFixtures.countRows snk.Admin "[dbo].[OSUSR_XABC_CUSTOMER] WHERE [CITYID] IN (501, 502)"
                        Assert.Equal(2, reKeyed)
                        return ()
                    }))

    /// The contract-acquisition refusal: the sink's OSSYS metamodel is
    /// unreadable (object-scope DENY SELECT on `ossys_Entity_Attr`) — the
    /// NAMED schema-read refusal (exit 6 class), never a raw crash, and the
    /// sink data untouched.
    [<Fact>]
    member _.``peer under managed grant: an unreadable sink metamodel refuses by name on the schema-read axis`` () =
        if not (ManagedGrantFixtures.skipIfNoDocker "PeerManagedOssysDeny") then () else
        TaskSync.run (fun () ->
            MockOutSystemsEnv.withMockEnvPair fixture "PeerMgDeny"
                "" ManagedGrantFixtures.sourceRows MockOutSystemsEnv.ManagedDml
                "X" [] MockOutSystemsEnv.ManagedDml
                (fun src snk ->
                    task {
                        do! ManagedGrantFixtures.exec snk.Admin
                                (sprintf "DENY SELECT ON [dbo].[ossys_Entity_Attr] TO [%s];" (Option.get snk.Login))
                        match! PeerTransfer.acquireContracts src.EngineConnStr snk.EngineConnStr with
                        | Ok _ -> Assert.Fail "expected the sink metamodel read to refuse"
                        | Error errors ->
                            // The exact code depends on WHERE the read died
                            // (a thrown SqlException wraps as
                            // `source.ossys.readFailed`; an adapter-level
                            // extraction failure carries its own
                            // `adapter.*` code) — the CONTRACT is the axis:
                            // every acquisition failure classifies onto
                            // schema-read, exit 6, never the unclassified 3.
                            let e = List.head errors
                            Assert.Equal((6, Preflight.SchemaReadFailed), Preflight.classify e.Code)
                        return ()
                    }))

    /// G1 PINNED on the peer dispatch: an object-scope DENY INSERT is
    /// invisible to the DB-scope grant preflight — the load crashes RAW
    /// mid-subset with the parent kind already landed (the partial-write
    /// surprise). The promotion trigger lives with the reserved stub in
    /// ReverseLegBoundaryTests (`object-scope DENY`); when an object-scope
    /// probe lands, flip this to a named pre-write refusal.
    [<Fact>]
    member _.``peer under managed grant PINNED GAP: object-scope DENY INSERT crashes raw mid-load with a partial write (G1)`` () =
        if not (ManagedGrantFixtures.skipIfNoDocker "PeerManagedG1") then () else
        TaskSync.run (fun () ->
            MockOutSystemsEnv.withMockEnvPair fixture "PeerMgG1"
                "" ManagedGrantFixtures.sourceRows MockOutSystemsEnv.ManagedDml
                "X" ManagedGrantFixtures.sinkReseed MockOutSystemsEnv.ManagedDml
                (fun src snk ->
                    task {
                        do! ManagedGrantFixtures.exec snk.Admin
                                (sprintf "DENY INSERT ON [dbo].[OSUSR_XABC_CUSTOMER] TO [%s];" (Option.get snk.Login))
                        let! contractsR = PeerTransfer.acquireContracts src.EngineConnStr snk.EngineConnStr
                        let (srcContract, sinkContract) = ManagedGrantFixtures.value contractsR
                        let! outcome =
                            ManagedGrantFixtures.throughConnections src.EngineConnStr snk.EngineConnStr false (fun connections ->
                                task {
                                    try
                                        let! r =
                                            Transfer.runReverseLegThroughConnections
                                                Transfer.Execute EmissionMode.Incremental false true false
                                                [ "City"; "Customer" ] connections srcContract sinkContract Map.empty
                                        return Choice1Of2 r
                                    with ex -> return Choice2Of2 ex
                                })
                        match outcome with
                        | Choice1Of2 _ -> Assert.Fail "expected the object-scope DENY to crash the load (the pinned G1 gap)"
                        | Choice2Of2 ex ->
                            Assert.True(DmlPrincipal.isPermissionDenied ex, sprintf "expected the permission class, got: %s" ex.Message)
                        // The partial write, pinned: the parent kind (City,
                        // topologically first) already landed.
                        let! cities = ManagedGrantFixtures.countRows snk.Admin "[dbo].[OSUSR_XDEF_CITY]"
                        Assert.Equal(2, cities)
                        let! customers = ManagedGrantFixtures.countRows snk.Admin "[dbo].[OSUSR_XABC_CUSTOMER]"
                        Assert.Equal(0, customers)
                        return ()
                    }))
