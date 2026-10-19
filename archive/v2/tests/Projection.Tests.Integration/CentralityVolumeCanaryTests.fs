namespace Projection.Tests

// H-071 end-to-end — the synthetic volume weighting proven through a REAL load.
// A hub kind is the FK target of three satellites (high FK-graph centrality); the
// satellites are peripheral. Every kind is given the SAME profiled RowCount, so
// any row-count difference after a weighted synthetic load is attributable ONLY to
// centrality weighting. With `weightVolumeByCentrality` on, the hub lands MORE rows
// than its flat baseline and more than a satellite; the satellites stay at baseline.
// Serial via the Docker-SqlServer collection; blocking wait via `TaskSync.run`.

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Xunit
open Projection.Core
open Projection.Pipeline

module private CentralityVolumeFixtures =

    let name (s: string) : Name = Name.create s |> Result.value
    let value (r: Result<'a>) : 'a = Result.value r
    let kKey (s: string) : SsKey = SsKey.synthesizedComposite "CEN_KIND" [ s ] |> value
    let aKey (parts: string list) : SsKey = SsKey.synthesizedComposite "CEN_ATTR" parts |> value
    let rKey (parts: string list) : SsKey = SsKey.synthesizedComposite "CEN_REF" parts |> value
    let mKey (s: string) : SsKey = SsKey.synthesized "CEN_MOD" s |> value

    /// IDENTITY-PK hub + three satellites, each with a NOT NULL FK to the hub.
    let ddl : string =
        "CREATE TABLE dbo.CEN_HUB (ID INT IDENTITY(1,1) NOT NULL PRIMARY KEY, LABEL NVARCHAR(20) NOT NULL);\n"
        + "CREATE TABLE dbo.CEN_SAT1 (ID INT IDENTITY(1,1) NOT NULL PRIMARY KEY, HUB_ID INT NOT NULL FOREIGN KEY REFERENCES dbo.CEN_HUB(ID));\n"
        + "CREATE TABLE dbo.CEN_SAT2 (ID INT IDENTITY(1,1) NOT NULL PRIMARY KEY, HUB_ID INT NOT NULL FOREIGN KEY REFERENCES dbo.CEN_HUB(ID));\n"
        + "CREATE TABLE dbo.CEN_SAT3 (ID INT IDENTITY(1,1) NOT NULL PRIMARY KEY, HUB_ID INT NOT NULL FOREIGN KEY REFERENCES dbo.CEN_HUB(ID));"

    let private attr (key: SsKey) (logical: string) (col: string) (ptype: PrimitiveType) (isPk: bool) (nullable: bool) : Attribute =
        { Attribute.create key (name logical) ptype with
            Column       = ColumnRealization.create col nullable |> value
            IsPrimaryKey = isPk
            IsIdentity   = isPk }

    let private hub : Kind =
        Kind.create (kKey "Hub") (name "Hub") (TableId.create "dbo" "CEN_HUB" |> value)
            [ attr (aKey ["Hub"; "Id"])    "Id"    "ID"    Integer true  false
              attr (aKey ["Hub"; "Label"]) "Label" "LABEL" Text    false false ]

    let private sat (n: string) : Kind =
        let hubFk = aKey ["Sat" + n; "HubId"]
        { Kind.create (kKey ("Sat" + n)) (name ("Sat" + n)) (TableId.create "dbo" ("CEN_SAT" + n) |> value)
            [ attr (aKey ["Sat" + n; "Id"]) "Id"    "ID"     Integer true  false
              attr hubFk                    "HubId" "HUB_ID" Integer false false ] with
            References = [ Reference.create (rKey ["Sat" + n; "Hub"]) (name "Hub") hubFk (kKey "Hub") ] }

    let catalog : Catalog =
        Catalog.create
            [ { SsKey = mKey "M"; Name = name "M"; Kinds = [ hub; sat "1"; sat "2"; sat "3" ]
                IsActive = true; ExtendedProperties = [] } ]
            []
        |> value

    /// Equal baseline volume for every kind — isolates the weighting's effect.
    let baselineRows = 20L

    let private probe (n: int64) : ProbeStatus =
        ProbeStatus.create (System.DateTimeOffset(2026, 6, 8, 0, 0, 0, System.TimeSpan.Zero)) n Succeeded |> value

    let private col (key: SsKey) : ColumnProfile =
        ColumnProfile.create key baselineRows 0L (probe baselineRows) |> value

    let profile : Profile =
        { Profile.empty with
            Columns =
                [ col (aKey ["Hub"; "Id"]); col (aKey ["Hub"; "Label"])
                  col (aKey ["Sat1"; "Id"]); col (aKey ["Sat1"; "HubId"])
                  col (aKey ["Sat2"; "Id"]); col (aKey ["Sat2"; "HubId"])
                  col (aKey ["Sat3"; "Id"]); col (aKey ["Sat3"; "HubId"]) ] }

    /// The `effectiveConfig` SyntheticLoadRun builds when `weightVolumeByCentrality`
    /// is on — replicated here so the canary exercises the exact derivation without
    /// needing on-disk model/profile files.
    let weightedConfig : SyntheticConfig =
        let topo = (Projection.Core.Passes.TopologicalOrderPass.runWith Projection.Core.TreatAsCycle catalog).Value
        let ranking = (Projection.Core.Passes.CentralityPass.registered.Run topo).Value.Value
        let derived = SyntheticVolume.byCentrality 1M 4M SyntheticConfig.defaultConfig.Scale ranking
        { SyntheticConfig.defaultConfig with
            VolumeByKind = SyntheticVolume.mergeUnderOperator Map.empty derived }

    let countRows (conn: SqlConnection) (table: string) : Task<int> =
        task {
            use cmd = conn.CreateCommand()
            cmd.CommandText <- sprintf "SELECT COUNT_BIG(*) FROM %s;" table
            let! r = cmd.ExecuteScalarAsync()
            return int (unbox<int64> r)
        }

[<Xunit.Collection("Docker-SqlServer")>]
type CentralityVolumeCanaryTests(fixture: EphemeralContainerFixture) =
    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``H-071 canary: centrality weighting lands more rows for the central hub than for its satellites`` () =
        let label = "centralityVolumeCanary"
        if not (Deploy.Docker.ensureRunning ()) then
            printfn "SKIP %s: Docker daemon not reachable." label
        else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase label (fun sink _ ->
                task {
                    do! Deploy.executeBatch sink CentralityVolumeFixtures.ddl
                    let! report =
                        Transfer.runSynthetic
                            Transfer.Execute EmissionMode.Incremental true
                            IdentityPolicy.Structural   // K1c — the pre-K1c disposition, byte-identical
                            sink CentralityVolumeFixtures.catalog CentralityVolumeFixtures.profile
                            CentralityVolumeFixtures.weightedConfig 42UL id
                    match report with
                    | Error es -> Assert.True(false, sprintf "runSynthetic failed: %A" es)
                    | Ok _ ->
                        let baseline = int CentralityVolumeFixtures.baselineRows
                        let! hubRows  = CentralityVolumeFixtures.countRows sink "[dbo].[CEN_HUB]"
                        let! satRows  = CentralityVolumeFixtures.countRows sink "[dbo].[CEN_SAT1]"
                        // The hub is the target of three FKs (high centrality) — boosted above baseline.
                        Assert.True(hubRows > baseline, sprintf "central hub should exceed the flat baseline %d, got %d" baseline hubRows)
                        // Peripheral satellites are at or below the mean — held at the baseline.
                        Assert.Equal(baseline, satRows)
                        // And the hub outranks a satellite through a real load.
                        Assert.True(hubRows > satRows, sprintf "hub (%d) should land more rows than a satellite (%d)" hubRows satRows)
                }))
