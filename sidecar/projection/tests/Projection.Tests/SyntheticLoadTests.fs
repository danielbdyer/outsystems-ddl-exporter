namespace Projection.Tests

// THE_SYNTHETIC_DATA_DESIGN §8, slice S2 — the synthetic-load runner proof.
// `Transfer.runSynthetic` generates rows with the pure Core `σ`
// (no source DB) and realizes them through the transfer's write seam onto an
// ephemeral sink. This is the structural-exact (L1) half of the π∘σ canary:
// the load succeeds, volume = profiled RowCount, and FK integrity holds with
// zero orphans (enforced doubly — the generator draws FK values from the
// parent PK pool, and the deployed FK constraint would reject any orphan
// insert). Serial via the Docker-SqlServer collection; blocking wait via
// `TaskSync.run`.

open Xunit
open Projection.Core
open Projection.Pipeline

module private SyntheticLoadFixtures =

    let skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else printfn "SKIP %s: Docker daemon not reachable." label; false

    let name (s: string) : Name = Name.create s |> Result.value
    let value (r: Result<'a>) : 'a = Result.value r
    let kKey (s: string) : SsKey = SsKey.synthesizedComposite "SYN_KIND" [ s ] |> value
    let aKey (parts: string list) : SsKey = SsKey.synthesizedComposite "SYN_ATTR" parts |> value
    let rKey (parts: string list) : SsKey = SsKey.synthesizedComposite "SYN_REF" parts |> value
    let mKey (s: string) : SsKey = SsKey.synthesized "SYN_MOD" s |> value

    /// IDENTITY-PK schema (the OutSystems norm): the sink mints surrogates,
    /// so FK integrity can only hold if `writePlan`'s AssignedBySink capture
    /// re-points each Order's CustomerId through the captured remap.
    let ddl =
        "CREATE TABLE [dbo].[SYN_CUSTOMER] (" +
        "[ID] INT IDENTITY(1,1) NOT NULL PRIMARY KEY, " +
        "[STATUS] NVARCHAR(50) NOT NULL, [SCORE] INT NOT NULL); " +
        "CREATE TABLE [dbo].[SYN_ORDER] (" +
        "[ID] INT IDENTITY(1,1) NOT NULL PRIMARY KEY, [CUSTOMER_ID] INT NOT NULL, " +
        "CONSTRAINT [FK_SynOrder_Customer] FOREIGN KEY ([CUSTOMER_ID]) " +
        "REFERENCES [dbo].[SYN_CUSTOMER] ([ID]));"

    let custKey  = kKey "Customer"
    let custId   = aKey ["Customer"; "Id"]
    let custStat = aKey ["Customer"; "Status"]
    let custScr  = aKey ["Customer"; "Score"]
    let ordKey   = kKey "Order"
    let ordId    = aKey ["Order"; "Id"]
    let ordCust  = aKey ["Order"; "CustomerId"]

    let private attr (key: SsKey) (logical: string) (col: string) (ptype: PrimitiveType) (isPk: bool) (nullable: bool) : Attribute =
        { Attribute.create key (name logical) ptype with
            Column       = ColumnRealization.create col nullable |> value
            IsPrimaryKey = isPk
            IsIdentity   = isPk }

    let customer : Kind =
        Kind.create custKey (name "Customer") (TableId.create "dbo" "SYN_CUSTOMER" |> value)
            [ attr custId  "Id"     "ID"     Integer true  false
              attr custStat "Status" "STATUS" Text    false false
              attr custScr  "Score"  "SCORE"  Integer false false ]

    let order : Kind =
        { Kind.create ordKey (name "Order") (TableId.create "dbo" "SYN_ORDER" |> value)
            [ attr ordId   "Id"         "ID"          Integer true  false
              attr ordCust "CustomerId" "CUSTOMER_ID" Integer false false ] with
            References = [ Reference.create (rKey ["Order"; "Customer"]) (name "Customer") ordCust custKey ] }

    let catalog : Catalog =
        Catalog.create
            [ { SsKey = mKey "M"; Name = name "M"; Kinds = [ customer; order ]
                IsActive = true; ExtendedProperties = [] } ]
            []
        |> value

    let private probe (n: int64) : ProbeStatus =
        ProbeStatus.create (System.DateTimeOffset(2026, 6, 8, 0, 0, 0, System.TimeSpan.Zero)) n Succeeded |> value

    let private col (key: SsKey) (rows: int64) : ColumnProfile =
        ColumnProfile.create key rows 0L (probe rows) |> value

    let custRows = 50L
    let ordRows  = 120L

    /// Profile: row counts drive the volume; a low-card STATUS distribution
    /// is preserved (NOT NULL ⇒ every row carries a real value).
    let profile : Profile =
        { Profile.empty with
            Columns =
                [ col custId custRows; col custStat custRows; col custScr custRows
                  col ordId ordRows; col ordCust ordRows ]
            Distributions =
                [ AttributeDistribution.Categorical
                    (CategoricalDistribution.create custStat
                        [ "Active", 35L; "Inactive", 10L; "Pending", 5L ] 3L false (probe custRows) |> value) ] }

    let countRows (cnn: Microsoft.Data.SqlClient.SqlConnection) (table: string) : System.Threading.Tasks.Task<int> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sprintf "SELECT COUNT_BIG(*) FROM %s;" table
            let! scalar = cmd.ExecuteScalarAsync()
            return int (unbox<int64> scalar)
        }

    let orphanCount (cnn: Microsoft.Data.SqlClient.SqlConnection) : System.Threading.Tasks.Task<int> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <-
                "SELECT COUNT_BIG(*) FROM [dbo].[SYN_ORDER] o " +
                "LEFT JOIN [dbo].[SYN_CUSTOMER] c ON o.[CUSTOMER_ID] = c.[ID] " +
                "WHERE c.[ID] IS NULL;"
            let! scalar = cmd.ExecuteScalarAsync()
            return int (unbox<int64> scalar)
        }


[<Xunit.Collection("Docker-SqlServer")>]
type SyntheticLoadTests(fixture: EphemeralContainerFixture) =
    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``L1: synthetic load lands the profiled volume with zero FK orphans``() =
        let label = "syntheticLoad"
        if not (SyntheticLoadFixtures.skipIfNoDocker label) then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase label (fun sink _ ->
                task {
                    do! Deploy.executeBatch sink SyntheticLoadFixtures.ddl
                    let! report =
                        Transfer.runSynthetic
                            Transfer.Execute
                            EmissionMode.Incremental
                            true   // allowCdc — the warm container is not CDC-tracked
                            sink
                            SyntheticLoadFixtures.catalog
                            SyntheticLoadFixtures.profile
                            SyntheticConfig.defaultConfig
                            42UL
                            id
                    match report with
                    | Error es -> Assert.True(false, sprintf "runSynthetic failed: %A" es)
                    | Ok _ ->
                        let! custCount = SyntheticLoadFixtures.countRows sink "[dbo].[SYN_CUSTOMER]"
                        let! ordCount  = SyntheticLoadFixtures.countRows sink "[dbo].[SYN_ORDER]"
                        let! orphans   = SyntheticLoadFixtures.orphanCount sink
                        Assert.Equal(int SyntheticLoadFixtures.custRows, custCount)
                        Assert.Equal(int SyntheticLoadFixtures.ordRows, ordCount)
                        Assert.Equal(0, orphans)
                }))

    [<Fact>]
    member _.``DryRun synthetic plans the volume without writing``() =
        let label = "syntheticDryRun"
        if not (SyntheticLoadFixtures.skipIfNoDocker label) then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase label (fun sink _ ->
                task {
                    do! Deploy.executeBatch sink SyntheticLoadFixtures.ddl
                    let! report =
                        Transfer.runSynthetic
                            Transfer.DryRun
                            EmissionMode.Incremental
                            true
                            sink
                            SyntheticLoadFixtures.catalog
                            SyntheticLoadFixtures.profile
                            SyntheticConfig.defaultConfig
                            42UL
                            id
                    match report with
                    | Error es -> Assert.True(false, sprintf "runSynthetic dry-run failed: %A" es)
                    | Ok r ->
                        // The plan reports the generated volume; nothing was written.
                        let custOutcome = r.Kinds |> List.find (fun k -> k.Kind = SyntheticLoadFixtures.custKey)
                        Assert.Equal(int SyntheticLoadFixtures.custRows, custOutcome.RowsIngested)
                        Assert.Equal(0, custOutcome.RowsWritten)
                        let! custCount = SyntheticLoadFixtures.countRows sink "[dbo].[SYN_CUSTOMER]"
                        Assert.Equal(0, custCount)
                }))
