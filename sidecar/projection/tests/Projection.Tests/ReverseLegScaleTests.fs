namespace Projection.Tests

// LE-3 Tier 4 — scale and the norm (WAVE_6_ALGEBRA T16: the CDC capture
// count is ‖δ‖; "minimum viable data movement" means isometric emission).
// Two witnesses:
//   (a) the per-row INSERT…OUTPUT capture ENVELOPE: an operator-reality-
//       shaped mesh (every kind AssignedBySink, FK chain + mesh edges,
//       thousands of rows) timed end-to-end — the bench evidence the
//       trigger-gated MERGE…OUTPUT set-based follow-on has been waiting
//       for (TRANSFER_ISOMORPHISM_SUBSTANTIATION §3, OPEN-5);
//   (b) the NORM: a first load into empty A is isometric — the CDC
//       capture count equals the inserted row count exactly. CDC tests
//       use `IsolatedContainerFixture` per the CDC-isolation rule.

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Targets.SSDT

module internal ReverseLegScaleFixtures =

    let skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else printfn "SKIP %s: Docker daemon not reachable." label; false

    let value (r: Result<'a>) : 'a = Result.value r

    let nm (s: string) : Name = Name.create s |> Result.value
    let kKey (i: int) : SsKey = SsKey.synthesizedComposite "L3S_KIND" [ string i ] |> Result.value
    let aKey (ki: int) (attr: string) : SsKey = SsKey.synthesizedComposite "L3S_ATTR" [ string ki; attr ] |> Result.value
    let rKey (ki: int) (ri: int) : SsKey = SsKey.synthesizedComposite "L3S_REF" [ string ki; string ri ] |> Result.value

    /// The operator-reality-shaped mesh: `nKinds` kinds, EVERY PK IDENTITY
    /// (so the whole load runs the per-row INSERT…OUTPUT capture path),
    /// kind i carrying a chain FK to kind i-1 and a mesh FK to kind i/2 —
    /// the variegated multi-parent shape of a real OSUSR estate. A Text
    /// business key (Bk) carries the relational-fidelity witness.
    let meshModel (nKinds: int) : Catalog =
        let kindOf (i: int) : Kind =
            let pk =
                { Attribute.create (aKey i "Id") (nm "Id") Integer with
                    Column       = ColumnRealization.create "ID" false |> Result.value
                    IsPrimaryKey = true
                    IsIdentity   = true
                    IsMandatory  = true }
            let bk =
                { Attribute.create (aKey i "Bk") (nm "Bk") Text with
                    Column      = ColumnRealization.create "BK" false |> Result.value
                    IsMandatory = true }
            let chain =
                if i = 0 then []
                else
                    [ { Attribute.create (aKey i "ParentId") (nm "ParentId") Integer with
                          Column      = ColumnRealization.create "PARENT_ID" false |> Result.value
                          IsMandatory = true } ]
            let mesh =
                if i < 2 then []
                else
                    [ { Attribute.create (aKey i "MeshId") (nm "MeshId") Integer with
                          Column      = ColumnRealization.create "MESH_ID" false |> Result.value
                          IsMandatory = true } ]
            let refs =
                (if i = 0 then []
                 else [ Reference.create (rKey i 0) (nm (sprintf "Chain%d" i)) (aKey i "ParentId") (kKey (i - 1)) ])
                @ (if i < 2 then []
                   else [ Reference.create (rKey i 1) (nm (sprintf "Mesh%d" i)) (aKey i "MeshId") (kKey (i / 2)) ])
            { Kind.create (kKey i) (nm (sprintf "Scale%d" i))
                (TableId.create "dbo" (sprintf "OSUSR_SC_T%d" i) |> Result.value)
                (pk :: bk :: chain @ mesh) with
                References = refs }
        Catalog.create
            [ { SsKey = SsKey.synthesizedComposite "L3S_MOD" [ "M" ] |> Result.value
                Name = nm "ScaleMod"; Kinds = [ for i in 0 .. nKinds - 1 -> kindOf i ]
                IsActive = true; ExtendedProperties = [] } ] []
        |> Result.value

    /// B's seed for the mesh (logical names): per kind, `rows` rows with
    /// IDs 1000..1000+rows-1 (colliding key spaces) and FK values spread
    /// across the parent's full ID range.
    let meshSeed (nKinds: int) (rows: int) : string =
        let sb = System.Text.StringBuilder()
        for i in 0 .. nKinds - 1 do
            let table = sprintf "[dbo].[Scale%d]" i
            sb.AppendLine(sprintf "SET IDENTITY_INSERT %s ON;" table) |> ignore
            for batchStart in 0 .. 500 .. rows - 1 do
                let batchEnd = min (batchStart + 499) (rows - 1)
                let values =
                    [ for j in batchStart .. batchEnd ->
                        let id = 1000 + j
                        let bk = sprintf "N'k%d-r%d'" i j
                        let parent = sprintf "%d" (1000 + ((j * 7 + 3) % rows))
                        let meshv = sprintf "%d" (1000 + ((j * 11 + 5) % rows))
                        match (i = 0), (i < 2) with
                        | true, _      -> sprintf "(%d,%s)" id bk
                        | false, true  -> sprintf "(%d,%s,%s)" id bk parent
                        | false, false -> sprintf "(%d,%s,%s,%s)" id bk parent meshv ]
                    |> String.concat ","
                let cols =
                    match (i = 0), (i < 2) with
                    | true, _      -> "([Id],[Bk])"
                    | false, true  -> "([Id],[Bk],[ParentId])"
                    | false, false -> "([Id],[Bk],[ParentId],[MeshId])"
                sb.AppendLine(sprintf "INSERT INTO %s %s VALUES %s;" table cols values) |> ignore
            sb.AppendLine(sprintf "SET IDENTITY_INSERT %s OFF;" table) |> ignore
        sb.ToString()

    /// (row count, order-independent business-key join checksum) for one FK
    /// edge — the at-scale fidelity witness (the keystone canary proves the
    /// same law with exact lists at small scale).
    let edgeChecksum
        (cnn: Microsoft.Data.SqlClient.SqlConnection)
        (childTable: string) (fkCol: string) (parentTable: string)
        (childBk: string) (parentBk: string) (pkCol: string)
        : System.Threading.Tasks.Task<int64 * int> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <-
                sprintf
                    "SELECT COUNT_BIG(*), COALESCE(CHECKSUM_AGG(BINARY_CHECKSUM(c.%s, p.%s)), 0) FROM %s c JOIN %s p ON c.%s = p.%s;"
                    childBk parentBk childTable parentTable fkCol pkCol
            use! reader = cmd.ExecuteReaderAsync()
            let! _ = reader.ReadAsync()
            return reader.GetInt64 0, reader.GetInt32 1
        }


[<Xunit.Collection("Docker-SqlServer")>]
type ReverseLegScaleTests(fixture: EphemeralContainerFixture) =
    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``Tier 4 envelope: a 16-kind all-AssignedBySink mesh at 4000 rows moves B->A with per-row capture — fidelity by edge checksum, throughput reported`` () =
        if not (ReverseLegScaleFixtures.skipIfNoDocker "L3Scale") then () else
        let nKinds = 16
        let rowsPerKind = 250
        let totalRows = nKinds * rowsPerKind
        let model = ReverseLegScaleFixtures.meshModel nKinds
        let logicalContract = CatalogRendition.logical model
        let physicalContract = CatalogRendition.physical model
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "L3ScaleSrc" (fun src _ ->
                task {
                    do! Deploy.executeBatch src (SsdtDdlEmitter.statements logicalContract |> Render.toText)
                    do! Deploy.executeBatch src (ReverseLegScaleFixtures.meshSeed nKinds rowsPerKind)
                    return!
                        fixture.WithEphemeralDatabase "L3ScaleSink" (fun sink _ ->
                            task {
                                do! Deploy.executeBatch sink (SsdtDdlEmitter.statements physicalContract |> Render.toText)

                                let sw = System.Diagnostics.Stopwatch.StartNew()
                                let! reportR =
                                    Transfer.runWithRenames Transfer.Execute true src sink logicalContract physicalContract
                                sw.Stop()
                                let report = ReverseLegScaleFixtures.value reportR
                                Assert.Empty(report.SkippedReferences)
                                Assert.True(report.Kinds |> List.forall (fun k -> k.Disposition = IdentityDisposition.AssignedBySink))
                                Assert.True(report.Kinds |> List.forall (fun k -> k.RowsWritten = rowsPerKind))

                                // The per-row capture envelope — the OPEN-5 bench
                                // evidence (covers ingest + plan + per-row
                                // INSERT…OUTPUT + FK re-point on the whole leg).
                                let rowsPerSec = float totalRows / sw.Elapsed.TotalSeconds
                                Bench.recordSample "transfer.reverseLeg.scale.legMs" sw.ElapsedMilliseconds
                                printfn
                                    "REVERSE-LEG CAPTURE ENVELOPE: %d rows (%d kinds × %d) in %d ms ⇒ %.0f rows/sec end-to-end (set-based MERGE…OUTPUT capture)"
                                    totalRows nKinds rowsPerKind sw.ElapsedMilliseconds rowsPerSec

                                // Every surrogate is sink-minted (no colliding
                                // source key survives) on a spot of tables.
                                for i in [ 0; 1; nKinds / 2; nKinds - 1 ] do
                                    use cmd = sink.CreateCommand()
                                    cmd.CommandText <- sprintf "SELECT COUNT_BIG(*) FROM [dbo].[OSUSR_SC_T%d] WHERE [ID] >= 1000;" i
                                    let! c = cmd.ExecuteScalarAsync()
                                    Assert.Equal(0L, unbox<int64> c)

                                // Relational fidelity per FK edge at scale: the
                                // (count, order-independent business-key join
                                // checksum) pair matches across renditions for
                                // EVERY chain edge and EVERY mesh edge.
                                for i in 1 .. nKinds - 1 do
                                    let! bChain =
                                        ReverseLegScaleFixtures.edgeChecksum src
                                            (sprintf "[dbo].[Scale%d]" i) "[ParentId]" (sprintf "[dbo].[Scale%d]" (i - 1))
                                            "[Bk]" "[Bk]" "[Id]"
                                    let! aChain =
                                        ReverseLegScaleFixtures.edgeChecksum sink
                                            (sprintf "[dbo].[OSUSR_SC_T%d]" i) "[PARENT_ID]" (sprintf "[dbo].[OSUSR_SC_T%d]" (i - 1))
                                            "[BK]" "[BK]" "[ID]"
                                    Assert.Equal(int64 rowsPerKind, fst bChain)
                                    Assert.Equal(bChain, aChain)
                                for i in 2 .. nKinds - 1 do
                                    let! bMesh =
                                        ReverseLegScaleFixtures.edgeChecksum src
                                            (sprintf "[dbo].[Scale%d]" i) "[MeshId]" (sprintf "[dbo].[Scale%d]" (i / 2))
                                            "[Bk]" "[Bk]" "[Id]"
                                    let! aMesh =
                                        ReverseLegScaleFixtures.edgeChecksum sink
                                            (sprintf "[dbo].[OSUSR_SC_T%d]" i) "[MESH_ID]" (sprintf "[dbo].[OSUSR_SC_T%d]" (i / 2))
                                            "[BK]" "[BK]" "[ID]"
                                    Assert.Equal(bMesh, aMesh)
                            })
                }))


    [<Fact>]
    member _.``Tier 4 sustained envelope: a 4-kind chain at 100k rows measures the steady-state set-based capture rate — the number the 288M-row window extrapolates from`` () =
        if not (ReverseLegScaleFixtures.skipIfNoDocker "L3Sustained") then () else
        let nKinds = 4
        let rowsPerKind = 25_000
        let totalRows = nKinds * rowsPerKind
        let model = ReverseLegScaleFixtures.meshModel nKinds
        let logicalContract = CatalogRendition.logical model
        let physicalContract = CatalogRendition.physical model
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "L3SustainedSrc" (fun src _ ->
                task {
                    do! Deploy.executeBatch src (SsdtDdlEmitter.statements logicalContract |> Render.toText)
                    do! Deploy.executeBatch src (ReverseLegScaleFixtures.meshSeed nKinds rowsPerKind)
                    return!
                        fixture.WithEphemeralDatabase "L3SustainedSink" (fun sink _ ->
                            task {
                                do! Deploy.executeBatch sink (SsdtDdlEmitter.statements physicalContract |> Render.toText)

                                let sw = System.Diagnostics.Stopwatch.StartNew()
                                let! reportR =
                                    Transfer.runWithRenames Transfer.Execute true src sink logicalContract physicalContract
                                sw.Stop()
                                let report = ReverseLegScaleFixtures.value reportR
                                Assert.Empty(report.SkippedReferences)
                                Assert.True(report.Kinds |> List.forall (fun k -> k.RowsWritten = rowsPerKind))

                                let rowsPerSec = float totalRows / sw.Elapsed.TotalSeconds
                                Bench.recordSample "transfer.reverseLeg.sustained.legMs" sw.ElapsedMilliseconds
                                printfn
                                    "REVERSE-LEG SUSTAINED ENVELOPE: %d rows (%d kinds × %d) in %d ms ⇒ %.0f rows/sec ⇒ 288M rows ≈ %.1f h at this rate"
                                    totalRows nKinds rowsPerKind sw.ElapsedMilliseconds rowsPerSec
                                    (288_000_000.0 / rowsPerSec / 3600.0)

                                // Fidelity at volume: every chain edge's checksum
                                // matches; every surrogate is sink-minted.
                                for i in 1 .. nKinds - 1 do
                                    let! bChain =
                                        ReverseLegScaleFixtures.edgeChecksum src
                                            (sprintf "[dbo].[Scale%d]" i) "[ParentId]" (sprintf "[dbo].[Scale%d]" (i - 1))
                                            "[Bk]" "[Bk]" "[Id]"
                                    let! aChain =
                                        ReverseLegScaleFixtures.edgeChecksum sink
                                            (sprintf "[dbo].[OSUSR_SC_T%d]" i) "[PARENT_ID]" (sprintf "[dbo].[OSUSR_SC_T%d]" (i - 1))
                                            "[BK]" "[BK]" "[ID]"
                                    Assert.Equal(int64 rowsPerKind, fst bChain)
                                    Assert.Equal(bChain, aChain)
                                // Sink-minted proof at volume: the sink's key
                                // space is the mint range [1, rows], not the
                                // source's colliding [1000, 1000+rows) space.
                                use cmd = sink.CreateCommand()
                                cmd.CommandText <- "SELECT MIN([ID]), MAX([ID]) FROM [dbo].[OSUSR_SC_T3];"
                                use! reader = cmd.ExecuteReaderAsync()
                                let! _ = reader.ReadAsync()
                                Assert.Equal(1, reader.GetInt32 0)
                                Assert.Equal(rowsPerKind, reader.GetInt32 1)
                            })
                }))


/// The norm witness — CDC isolation per the IsolatedContainerFixture rule
/// (`sp_cdc_enable_db` flips instance-wide state; never on the warm
/// container).
[<Xunit.Collection("Docker-SqlServer")>]
type ReverseLegCdcNormTests(fixture: IsolatedContainerFixture) =
    interface IClassFixture<IsolatedContainerFixture>

    [<Fact>]
    member _.``Tier 4 norm: the first reverse-leg load into empty A is ISOMETRIC — the CDC capture count equals the inserted row count exactly`` () =
        if not (ReverseLegScaleFixtures.skipIfNoDocker "L3CdcNorm") then () else
        // A small all-AssignedBySink chain: 3 kinds × 4 rows = 12 inserts.
        let nKinds = 3
        let rowsPerKind = 4
        let model = ReverseLegScaleFixtures.meshModel nKinds
        let logicalContract = CatalogRendition.logical model
        let physicalContract = CatalogRendition.physical model
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "L3CdcNormSrc" (fun src _ ->
                task {
                    do! Deploy.executeBatch src (SsdtDdlEmitter.statements logicalContract |> Render.toText)
                    do! Deploy.executeBatch src (ReverseLegScaleFixtures.meshSeed nKinds rowsPerKind)
                    return!
                        fixture.WithEphemeralDatabase "L3CdcNormSink" (fun sink _ ->
                            task {
                                do! Deploy.executeBatch sink (SsdtDdlEmitter.statements physicalContract |> Render.toText)
                                do! Deploy.executeBatch sink "EXEC sys.sp_cdc_enable_db;"
                                for i in 0 .. nKinds - 1 do
                                    do! Deploy.executeBatch sink
                                            (sprintf
                                                "EXEC sys.sp_cdc_enable_table @source_schema=N'dbo', @source_name=N'OSUSR_SC_T%d', @role_name=NULL, @supports_net_changes=0;"
                                                i)

                                // allowCdc = true: the CDC-tracked sink is the
                                // POINT here (the norm reads the capture), so
                                // the operator's override is the honest path.
                                let! reportR =
                                    Transfer.runWithRenames Transfer.Execute true src sink logicalContract physicalContract
                                let report = ReverseLegScaleFixtures.value reportR
                                Assert.Empty(report.SkippedReferences)

                                do! Deploy.executeBatch sink "EXEC sys.sp_cdc_scan;"
                                let! tracked = Projection.Adapters.Sql.ReadSide.cdcTrackedTables sink
                                let! captureCount = Projection.Adapters.Sql.ReadSide.cdcCaptureCount sink tracked

                                // ‖δ‖ = CDC capture count = row count: the first
                                // load is isometric — every emitted change is an
                                // insert image, nothing extra, nothing hidden.
                                Assert.Equal(nKinds * rowsPerKind, captureCount)
                            })
                }))
