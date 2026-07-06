namespace Projection.Tests

// The peer (A→A) SsKey-aligned transfer, end-to-end against two REAL
// espace-variant databases (the 2026-07-06 partial-transfer readiness
// program; PARTIAL_TRANSFER_READINESS_LOG.md). This closes the estate's
// headline gap: the espace-invariance canary (`OssysComprehensiveFixtureTests`)
// proved two `OSUSR_`-variant OSSYS cells READ as one shape — no test MOVED
// DATA between them. Here: cell A = the edge-case OSSYS estate (`OSUSR_*`),
// cell B = the same estate with every physical `OSUSR_*` name shifted
// (`OSUSR_X*` — the espace-key shift), contracts read from EACH side's OSSYS
// metamodel (`LiveModelRead` — native GUID SsKeys, the identity that aligns
// across environments), and the contract-pair engine
// (`Transfer.runReverseLegThroughConnections` — the same runCore path the
// peer face drives) executes a declared TABLE SUBSET into the
// differently-named sink:
//   1. subset [City; Customer] — rows land in the `OSUSR_X*` tables, the
//      Customer→City FK re-pointed through the sink-minted keys (the sink's
//      identity is reseeded so a verbatim FK copy CANNOT pass);
//   2. subset [Customer] + City reconciled by NAME — the out-of-subset
//      parent matches rows the sink ALREADY holds (the reconcile-by-key
//      strategy for partial refresh); no city row is written;
//   3. subset replace re-run — WipeAndLoad × 2 is idempotent (counts and
//      the FK join stable; no duplicate rows).
// Serial via Docker-SqlServer; blocking wait via TaskSync.

open Xunit
open Projection.Core
open Projection.Adapters.OssysSql
open Projection.Pipeline

module private PeerAlignedFixtures =

    let skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else printfn "SKIP %s: Docker daemon not reachable." label; false

    let value (r: Result<'a>) : 'a = Result.value r

    /// Cell A: the edge-case OSSYS estate as shipped (metamodel + physical
    /// `OSUSR_*` tables, physical tables empty — rows are seeded test-locally).
    let seedA () : string = MetadataExtractionSql.readEdgeCaseSeed ()

    /// Cell B: the sibling espace cell — every `OSUSR_*` physical name
    /// shifted to `OSUSR_X*` (metamodel `Physical_Table_Name` AND the
    /// physical DDL move together; GUIDs / logical names / structure held
    /// fixed). The same transform the espace-invariance canary proved reads
    /// as ONE shape.
    let seedB () : string = (seedA ()) |> OssysSeedBuilder.withEspaceKey "X"

    /// Deterministic source rows: two cities (known surrogates 1/2 via
    /// IDENTITY_INSERT) and two customers pointing at them. LEGACYCODE is
    /// deliberately unset (the attribute is Is_Active=0 in the metamodel, so
    /// the OSSYS contract may not carry it).
    let sourceRows =
        "SET IDENTITY_INSERT [dbo].[OSUSR_DEF_CITY] ON; \
         INSERT INTO [dbo].[OSUSR_DEF_CITY] ([ID],[NAME],[ISACTIVE]) VALUES (1, N'Lisbon', 1), (2, N'Porto', 1); \
         SET IDENTITY_INSERT [dbo].[OSUSR_DEF_CITY] OFF; \
         SET IDENTITY_INSERT [dbo].[OSUSR_ABC_CUSTOMER] ON; \
         INSERT INTO [dbo].[OSUSR_ABC_CUSTOMER] ([ID],[EMAIL],[FIRSTNAME],[LASTNAME],[CITYID]) VALUES \
             (10, N'alice@x', N'Alice', N'Almeida', 1), (11, N'bob@x', N'Bob', N'Barbosa', 2); \
         SET IDENTITY_INSERT [dbo].[OSUSR_ABC_CUSTOMER] OFF;"

    /// Shift the sink's identity ranges away from the source's surrogates so
    /// a VERBATIM FK copy cannot accidentally satisfy the join assertions —
    /// only a genuine remap through the sink-minted keys passes.
    let reseedSinkIdentities =
        "DBCC CHECKIDENT ('[dbo].[OSUSR_XDEF_CITY]', RESEED, 500); \
         DBCC CHECKIDENT ('[dbo].[OSUSR_XABC_CUSTOMER]', RESEED, 900);"

    /// Sink cities the reconcile scenario matches against — same NAMEs as the
    /// source's, deliberately different surrogates (501/502 vs 1/2).
    let sinkCityRows =
        "SET IDENTITY_INSERT [dbo].[OSUSR_XDEF_CITY] ON; \
         INSERT INTO [dbo].[OSUSR_XDEF_CITY] ([ID],[NAME],[ISACTIVE]) VALUES (501, N'Lisbon', 1), (502, N'Porto', 1); \
         SET IDENTITY_INSERT [dbo].[OSUSR_XDEF_CITY] OFF;"

    /// The OSSYS-read contract for one cell — the production acquisition path
    /// (`PeerTransfer.acquireContracts` is this, via the conn-spec form).
    let contractOf (cnn: Microsoft.Data.SqlClient.SqlConnection) : System.Threading.Tasks.Task<Result<Catalog>> =
        LiveModelRead.fromConnection cnn

    let countRows (cnn: Microsoft.Data.SqlClient.SqlConnection) (table: string) : System.Threading.Tasks.Task<int> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sprintf "SELECT COUNT_BIG(*) FROM %s;" table
            let! scalar = cmd.ExecuteScalarAsync()
            return int (unbox<int64> scalar)
        }

    /// The identity-independent relationship the transfer must preserve: the
    /// (customer EMAIL → city NAME) join, sorted by email.
    let customerCityJoin (cnn: Microsoft.Data.SqlClient.SqlConnection) (customerTable: string) (cityTable: string) : System.Threading.Tasks.Task<(string * string) list> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <-
                sprintf
                    "SELECT c.[EMAIL], ci.[NAME] FROM [dbo].[%s] c JOIN [dbo].[%s] ci ON ci.[ID] = c.[CITYID] ORDER BY c.[EMAIL];"
                    customerTable cityTable
            let acc = System.Collections.Generic.List<string * string>()
            use! reader = cmd.ExecuteReaderAsync()
            let mutable go = true
            while go do
                let! more = reader.ReadAsync()
                if more then acc.Add(reader.GetString 0, reader.GetString 1) else go <- false
            return List.ofSeq acc
        }

    let kindByLogicalName (contract: Catalog) (name: string) : Kind =
        Catalog.allKinds contract
        |> List.find (fun k -> System.String.Equals(Name.value k.Name, name, System.StringComparison.OrdinalIgnoreCase))

    /// Build the apparatus over the two ephemeral databases (the D9 file-ref
    /// pattern the PE-3 golden canary uses), run the body, clean up.
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

[<Xunit.Collection("Docker-SqlServer")>]
type PeerAlignedTransferDockerTests(fixture: EphemeralContainerFixture) =
    interface IClassFixture<EphemeralContainerFixture>

    /// The headline: a declared table subset moves between two espace-variant
    /// cells — the sink's physical names differ (`OSUSR_X*`), identity aligns
    /// by native GUID SsKey, and the Customer→City FK re-points through the
    /// sink-minted city keys (verbatim copy CANNOT pass — the sink identity
    /// range is reseeded away from the source surrogates).
    [<Fact>]
    member _.``peer A→A: a table subset lands in a differently-named sink with FKs re-pointed (SsKey-aligned contracts)`` () =
        if not (PeerAlignedFixtures.skipIfNoDocker "PeerSubset") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "PeerSubsetSrc" (fun src srcConnStr ->
                task {
                    do! Deploy.executeBatch src (PeerAlignedFixtures.seedA ())
                    do! Deploy.executeBatch src PeerAlignedFixtures.sourceRows
                    return!
                        fixture.WithEphemeralDatabase "PeerSubsetSink" (fun sink sinkConnStr ->
                            task {
                                do! Deploy.executeBatch sink (PeerAlignedFixtures.seedB ())
                                do! Deploy.executeBatch sink PeerAlignedFixtures.reseedSinkIdentities

                                // Contracts from EACH side's OSSYS metamodel — the
                                // production acquisition (native GUID identity).
                                let! srcContractR = PeerAlignedFixtures.contractOf src
                                let! sinkContractR = PeerAlignedFixtures.contractOf sink
                                let srcContract = PeerAlignedFixtures.value srcContractR
                                let sinkContract = PeerAlignedFixtures.value sinkContractR

                                // The identity precondition, asserted where it is
                                // load-bearing: same SsKeys, different physical names.
                                let srcCustomer = PeerAlignedFixtures.kindByLogicalName srcContract "Customer"
                                let sinkCustomer = PeerAlignedFixtures.kindByLogicalName sinkContract "Customer"
                                Assert.Equal(srcCustomer.SsKey, sinkCustomer.SsKey)
                                Assert.Equal("OSUSR_ABC_CUSTOMER", TableId.tableText srcCustomer.Physical)
                                Assert.Equal("OSUSR_XABC_CUSTOMER", TableId.tableText sinkCustomer.Physical)

                                // The shape gate the peer face runs — the pair must
                                // judge as one shape over the subset.
                                let scope = Set.ofList [ srcCustomer.SsKey; (PeerAlignedFixtures.kindByLogicalName srcContract "City").SsKey ]
                                match PeerTransfer.shapeGate (Some scope) srcContract sinkContract with
                                | Error es -> Assert.Fail(sprintf "espace-variant cells must pass the shape gate; got %A" es)
                                | Ok _ -> ()

                                let! report =
                                    PeerAlignedFixtures.throughConnections srcConnStr sinkConnStr false (fun connections ->
                                        task {
                                            let! r =
                                                Transfer.runReverseLegThroughConnections
                                                    Transfer.Execute EmissionMode.Incremental false true false
                                                    [ "City"; "Customer" ] connections srcContract sinkContract Map.empty
                                            return PeerAlignedFixtures.value r
                                        })

                                // Every ingested row landed; nothing dropped.
                                Assert.Empty(report.SkippedReferences)
                                Assert.Empty(report.UnmatchedIdentities)
                                let written (name: string) =
                                    report.Kinds
                                    |> List.find (fun k -> k.Kind = (PeerAlignedFixtures.kindByLogicalName srcContract name).SsKey)
                                    |> fun k -> k.RowsWritten
                                Assert.Equal(2, written "City")
                                Assert.Equal(2, written "Customer")

                                // Rows are IN THE DIFFERENTLY-NAMED tables, and the
                                // FK re-pointed through sink-minted keys (the reseed
                                // puts them in the 500s — a verbatim copy of the
                                // source surrogates 1/2 cannot pass). Note the DBCC
                                // CHECKIDENT quirk: on a never-inserted table the
                                // reseed value IS the next minted value (500), not
                                // 500+1 — so the check is "not the source's keys",
                                // not a >500 boundary.
                                let! cities = PeerAlignedFixtures.countRows sink "[dbo].[OSUSR_XDEF_CITY]"
                                let! customers = PeerAlignedFixtures.countRows sink "[dbo].[OSUSR_XABC_CUSTOMER]"
                                Assert.Equal(2, cities)
                                Assert.Equal(2, customers)
                                let! verbatimFks = PeerAlignedFixtures.countRows sink "[dbo].[OSUSR_XABC_CUSTOMER] WHERE [CITYID] IN (1, 2)"
                                Assert.Equal(0, verbatimFks)
                                let! danglingFks = PeerAlignedFixtures.countRows sink "[dbo].[OSUSR_XABC_CUSTOMER] c WHERE NOT EXISTS (SELECT 1 FROM [dbo].[OSUSR_XDEF_CITY] ci WHERE ci.[ID] = c.[CITYID])"
                                Assert.Equal(0, danglingFks)
                                let! srcJoin = PeerAlignedFixtures.customerCityJoin src "OSUSR_ABC_CUSTOMER" "OSUSR_DEF_CITY"
                                let! sinkJoin = PeerAlignedFixtures.customerCityJoin sink "OSUSR_XABC_CUSTOMER" "OSUSR_XDEF_CITY"
                                Assert.Equal<(string * string) list>(srcJoin, sinkJoin)
                                Assert.Equal<(string * string) list>([ ("alice@x", "Lisbon"); ("bob@x", "Porto") ], sinkJoin)
                            })
                }))

    /// The reconcile-by-key strategy for an out-of-subset parent: only
    /// Customer transfers; City — outside the subset — reconciles by NAME
    /// against the rows the sink ALREADY holds (the partial-refresh shape:
    /// reference data pre-exists in the target under its own surrogates).
    [<Fact>]
    member _.``peer A→A: an out-of-subset FK parent reconciles by business key against the sink's own rows`` () =
        if not (PeerAlignedFixtures.skipIfNoDocker "PeerReconcile") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "PeerReconSrc" (fun src srcConnStr ->
                task {
                    do! Deploy.executeBatch src (PeerAlignedFixtures.seedA ())
                    do! Deploy.executeBatch src PeerAlignedFixtures.sourceRows
                    return!
                        fixture.WithEphemeralDatabase "PeerReconSink" (fun sink sinkConnStr ->
                            task {
                                do! Deploy.executeBatch sink (PeerAlignedFixtures.seedB ())
                                do! Deploy.executeBatch sink PeerAlignedFixtures.reseedSinkIdentities
                                do! Deploy.executeBatch sink PeerAlignedFixtures.sinkCityRows

                                let! srcContractR = PeerAlignedFixtures.contractOf src
                                let! sinkContractR = PeerAlignedFixtures.contractOf sink
                                let srcContract = PeerAlignedFixtures.value srcContractR
                                let sinkContract = PeerAlignedFixtures.value sinkContractR

                                let cityKind = PeerAlignedFixtures.kindByLogicalName sinkContract "City"
                                let cityNameAttr =
                                    cityKind.Attributes
                                    |> List.find (fun a -> ColumnRealization.columnNameText a.Column = "NAME")
                                let reconciliation =
                                    Map.ofList [ cityKind.SsKey, ReconciliationStrategy.MatchByColumn cityNameAttr.Name ]

                                // The escaping-FK detector names exactly this edge —
                                // and the reconcile strategy silences it (the gate's
                                // contract: reconciled targets are strategized).
                                let subsetKeys = Set.ofList [ (PeerAlignedFixtures.kindByLogicalName srcContract "Customer").SsKey ]
                                let bare = PeerTransfer.escapingFks srcContract subsetKeys Set.empty
                                Assert.Equal(1, bare.Length)
                                Assert.Equal("City", Name.value bare.Head.TargetName)
                                let strategized = PeerTransfer.escapingFks srcContract subsetKeys (Set.ofList [ cityKind.SsKey ])
                                Assert.Empty(strategized)

                                let! report =
                                    PeerAlignedFixtures.throughConnections srcConnStr sinkConnStr true (fun connections ->
                                        task {
                                            let! r =
                                                Transfer.runReverseLegThroughConnections
                                                    Transfer.Execute EmissionMode.Incremental false true false
                                                    [ "Customer" ] connections srcContract sinkContract reconciliation
                                            return PeerAlignedFixtures.value r
                                        })

                                // City is re-keyed, never written; every source city
                                // matched a sink row by NAME.
                                let cityOutcome = report.Kinds |> List.find (fun k -> k.Kind = cityKind.SsKey)
                                Assert.Equal(IdentityDisposition.ReconciledByRule, cityOutcome.Disposition)
                                Assert.Equal(0, cityOutcome.RowsWritten)
                                Assert.Empty(report.UnmatchedIdentities)
                                Assert.Empty(report.SkippedReferences)

                                // The sink keeps ITS OWN two cities (501/502) — and
                                // the transferred customers point at THEM.
                                let! cities = PeerAlignedFixtures.countRows sink "[dbo].[OSUSR_XDEF_CITY]"
                                Assert.Equal(2, cities)
                                let! reKeyed = PeerAlignedFixtures.countRows sink "[dbo].[OSUSR_XABC_CUSTOMER] WHERE [CITYID] IN (501, 502)"
                                Assert.Equal(2, reKeyed)
                                let! sinkJoin = PeerAlignedFixtures.customerCityJoin sink "OSUSR_XABC_CUSTOMER" "OSUSR_XDEF_CITY"
                                Assert.Equal<(string * string) list>([ ("alice@x", "Lisbon"); ("bob@x", "Porto") ], sinkJoin)
                            })
                }))

    /// The replace-subset re-run: WipeAndLoad over the declared subset is
    /// idempotent — a second Execute wipes the subset's sink rows child-first
    /// and reloads, so counts and the FK join are stable (no duplicates, the
    /// operator's chosen re-run semantics for today's partial refresh).
    [<Fact>]
    member _.``peer A→A: a WipeAndLoad subset re-run is idempotent (no duplicate rows; the join stable)`` () =
        if not (PeerAlignedFixtures.skipIfNoDocker "PeerReplay") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "PeerReplaySrc" (fun src srcConnStr ->
                task {
                    do! Deploy.executeBatch src (PeerAlignedFixtures.seedA ())
                    do! Deploy.executeBatch src PeerAlignedFixtures.sourceRows
                    return!
                        fixture.WithEphemeralDatabase "PeerReplaySink" (fun sink sinkConnStr ->
                            task {
                                do! Deploy.executeBatch sink (PeerAlignedFixtures.seedB ())
                                do! Deploy.executeBatch sink PeerAlignedFixtures.reseedSinkIdentities

                                let! srcContractR = PeerAlignedFixtures.contractOf src
                                let! sinkContractR = PeerAlignedFixtures.contractOf sink
                                let srcContract = PeerAlignedFixtures.value srcContractR
                                let sinkContract = PeerAlignedFixtures.value sinkContractR

                                let runOnce () =
                                    PeerAlignedFixtures.throughConnections srcConnStr sinkConnStr false (fun connections ->
                                        task {
                                            let! r =
                                                Transfer.runReverseLegThroughConnections
                                                    Transfer.Execute EmissionMode.WipeAndLoad false true false
                                                    [ "City"; "Customer" ] connections srcContract sinkContract Map.empty
                                            return PeerAlignedFixtures.value r
                                        })

                                let! _first = runOnce ()
                                let! firstJoin = PeerAlignedFixtures.customerCityJoin sink "OSUSR_XABC_CUSTOMER" "OSUSR_XDEF_CITY"
                                let! _second = runOnce ()
                                let! cities = PeerAlignedFixtures.countRows sink "[dbo].[OSUSR_XDEF_CITY]"
                                let! customers = PeerAlignedFixtures.countRows sink "[dbo].[OSUSR_XABC_CUSTOMER]"
                                Assert.Equal(2, cities)
                                Assert.Equal(2, customers)
                                let! secondJoin = PeerAlignedFixtures.customerCityJoin sink "OSUSR_XABC_CUSTOMER" "OSUSR_XDEF_CITY"
                                Assert.Equal<(string * string) list>(firstJoin, secondJoin)
                                Assert.Equal<(string * string) list>([ ("alice@x", "Lisbon"); ("bob@x", "Porto") ], secondJoin)
                            })
                }))
