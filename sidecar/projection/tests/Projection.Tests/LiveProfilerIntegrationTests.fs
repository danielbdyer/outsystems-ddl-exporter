namespace Projection.Tests

// Slice A.4.7'-prelude.live-profiler — Docker-gated integration tests
// for `Projection.Adapters.Sql.LiveProfiler`. Bootstraps an ephemeral
// SQL Server database, creates a small table with known nulls +
// duplicates, runs LiveProfiler.capture, and asserts the captured
// `AttributeReality list` reflects the deployed evidence.
//
// Per matrix row 49 + V2_DRIVER per-axis stakes (DATA-axis
// cutover-blocker). The cutover trigger named the LiveProfiler as
// the gate; this slice ships the adapter + this integration test
// verifies the round-trip end-to-end.
//
// Per pillar 9: all probes carry DataIntent — observation, not
// policy. The test asserts the observation accurately reflects
// the deployed state.
//
// **Fixture-lift (slice A.4.7'-prelude.test-fixture-lift, 2026-05-19).**
// xUnit `IClassFixture<EphemeralContainerFixture>` shares one
// ephemeral container across all 6 tests in this class; per-test
// `WithEphemeralDatabase` lifecycle preserves the per-scenario
// isolation V1 wired. Cluster cost drops from 6 × (container +
// schema) → 1 × container + 6 × (schema + drop).

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Xunit
open Projection.Core
open Projection.Adapters.Sql
open Projection.Pipeline

module private LiveProfilerFixtures =

    let mustOk (r: Result<'a>) : 'a =
        match r with
        | Ok v -> v
        | Error es ->
            let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
            invalidOp (sprintf "expected Ok; got: %s" codes)

    let mkKey (parts: string list) : SsKey =
        SsKey.synthesizedComposite "OS_TEST_PROFILER" parts |> mustOk

    let mkName (s: string) : Name = Name.create s |> mustOk

    let skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else
            printfn "SKIP %s: Docker daemon not reachable." label
            false

    // ---------------------------------------------------------------
    // Single-kind fixture: an `Items` table with three columns —
    // Id (PK, INT), Name (NVARCHAR, NULL ALLOWED), Code (NVARCHAR,
    // NOT NULL). Test data: 4 rows where Name has one NULL and one
    // duplicate; Code has no nulls and no duplicates. The probes
    // should observe these states uniformly.
    // ---------------------------------------------------------------

    let itemsKindKey = mkKey ["Items"]
    let idAttrKey    = mkKey ["Items"; "Id"]
    let nameAttrKey  = mkKey ["Items"; "Name"]
    let codeAttrKey  = mkKey ["Items"; "Code"]

    let itemsKind : Kind =
        let idAttr =
            { Attribute.create idAttrKey (mkName "Id") Integer with
                Column       = { ColumnName = "ID"; IsNullable = false }
                IsPrimaryKey = true
                IsMandatory  = true }
        let nameAttr =
            { Attribute.create nameAttrKey (mkName "Name") Text with
                Column      = { ColumnName = "NAME"; IsNullable = true }
                Length      = Some 50
                IsMandatory = false }
        let codeAttr =
            { Attribute.create codeAttrKey (mkName "Code") Text with
                Column      = { ColumnName = "CODE"; IsNullable = false }
                Length      = Some 10
                IsMandatory = true }
        { Kind.create itemsKindKey (mkName "Items")
            { Schema = "dbo"; Table = "OSUSR_LP_ITEMS"; Catalog = None }
            [ idAttr; nameAttr; codeAttr ]
          with References = []; Indexes = []; ColumnChecks = [] }

    let itemsCatalog : Catalog =
        {
            Modules =
                [ { SsKey = mkKey ["Module"]
                    Name  = mkName "TestModule"
                    Kinds = [ itemsKind ]
                    IsActive = true
                    ExtendedProperties = [] } ]
            Sequences = []
        }

    let schemaSql : string =
        "CREATE TABLE [dbo].[OSUSR_LP_ITEMS] (" +
        "[ID] INT NOT NULL PRIMARY KEY, " +
        "[NAME] NVARCHAR(50) NULL, " +
        "[CODE] NVARCHAR(10) NOT NULL" +
        ");"

    let seedSql : string =
        // Row 1: Name = 'alpha' (unique non-null)
        // Row 2: Name = 'alpha' (DUPLICATE of row 1)
        // Row 3: Name = NULL    (NULL present)
        // Row 4: Name = 'gamma' (unique non-null)
        // Code column: all distinct, no nulls — should report HasNulls = false, HasDuplicates = false.
        "INSERT INTO [dbo].[OSUSR_LP_ITEMS] ([ID], [NAME], [CODE]) VALUES " +
        "(1, N'alpha', N'A1'), " +
        "(2, N'alpha', N'A2'), " +
        "(3, NULL, N'A3'), " +
        "(4, N'gamma', N'A4');"

    let findReality (realities: AttributeReality list) (key: SsKey) : AttributeReality =
        realities |> List.find (fun r -> r.AttributeKey = key)

open LiveProfilerFixtures

// ---------------------------------------------------------------------------
// LiveProfiler integration: NAME column has NULL + duplicate value;
// CODE column has neither. Probe observations should match.
// ---------------------------------------------------------------------------

[<Xunit.Collection("Docker-SqlServer")>]
type LiveProfilerIntegrationTests(fixture: EphemeralContainerFixture) =

    let runCaptureScenario () : Task<AttributeReality list> =
        fixture.WithEphemeralDatabase "LiveProfiler" (fun cnn -> task {
            do! Deploy.executeBatch cnn schemaSql
            do! Deploy.executeBatch cnn seedSql
            let! captureResult = LiveProfiler.capture cnn itemsCatalog
            return mustOk captureResult
        })

    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``A.4.7'-prelude.live-profiler: NAME column reflects HasNulls = true (one row with NULL)`` () =
        if not (skipIfNoDocker "live-profiler-has-nulls") then () else
        let realities = (runCaptureScenario ()).GetAwaiter().GetResult()
        let name = findReality realities nameAttrKey
        Assert.True(name.HasNulls,
                    sprintf "expected HasNulls = true (one row has NAME = NULL); got %A" name)

    [<Fact>]
    member _.``A.4.7'-prelude.live-profiler: NAME column reflects HasDuplicates = true (two rows share 'alpha')`` () =
        if not (skipIfNoDocker "live-profiler-has-duplicates") then () else
        let realities = (runCaptureScenario ()).GetAwaiter().GetResult()
        let name = findReality realities nameAttrKey
        Assert.True(name.HasDuplicates,
                    sprintf "expected HasDuplicates = true (two rows have NAME = 'alpha'); got %A" name)

    [<Fact>]
    member _.``A.4.7'-prelude.live-profiler: CODE column reflects HasNulls = false and HasDuplicates = false (all distinct, no nulls)`` () =
        if not (skipIfNoDocker "live-profiler-code-clean") then () else
        let realities = (runCaptureScenario ()).GetAwaiter().GetResult()
        let code = findReality realities codeAttrKey
        Assert.False(code.HasNulls,
                     sprintf "expected HasNulls = false (no row has CODE = NULL); got %A" code)
        Assert.False(code.HasDuplicates,
                     sprintf "expected HasDuplicates = false (all CODE values distinct); got %A" code)

    [<Fact>]
    member _.``A.4.7'-prelude.live-profiler: IsNullableInDatabase reflects sys.columns.is_nullable per column`` () =
        if not (skipIfNoDocker "live-profiler-nullability-reflection") then () else
        let realities = (runCaptureScenario ()).GetAwaiter().GetResult()
        let id   = findReality realities idAttrKey
        let name = findReality realities nameAttrKey
        let code = findReality realities codeAttrKey
        // ID is the PK (NOT NULL).
        Assert.False(id.IsNullableInDatabase)
        // NAME is declared NULL ALLOWED.
        Assert.True(name.IsNullableInDatabase)
        // CODE is declared NOT NULL.
        Assert.False(code.IsNullableInDatabase)

    [<Fact>]
    member _.``A.4.7'-prelude.live-profiler: captured reality list contains an entry per attribute (PK + non-PK)`` () =
        if not (skipIfNoDocker "live-profiler-totality") then () else
        let realities = (runCaptureScenario ()).GetAwaiter().GetResult()
        let keys = realities |> List.map (fun r -> r.AttributeKey) |> Set.ofList
        Assert.Equal<Set<SsKey>>(
            Set.ofList [ idAttrKey; nameAttrKey; codeAttrKey ],
            keys)

    // ---------------------------------------------------------------
    // LiveProfiler attach composes into an existing Profile per the
    // sibling-adapter discipline.
    // ---------------------------------------------------------------

    [<Fact>]
    member _.``A.4.7'-prelude.live-profiler: attach composes captured realities into Profile.AttributeRealities`` () =
        if not (skipIfNoDocker "live-profiler-attach-compose") then () else
        let p =
            (fixture.WithEphemeralDatabase "LiveProfilerAttach" (fun cnn -> task {
                do! Deploy.executeBatch cnn schemaSql
                do! Deploy.executeBatch cnn seedSql
                let! attachResult = LiveProfiler.attach cnn itemsCatalog Profile.empty
                return mustOk attachResult
            })).GetAwaiter().GetResult()
        // The attach output is Profile.empty + realities populated.
        Assert.NotEmpty p.AttributeRealities
        Assert.Equal(3, List.length p.AttributeRealities)
        // Other Profile axes remain empty (sibling-adapter composability).
        Assert.Empty p.Columns
        Assert.Empty p.UniqueCandidates
        Assert.Empty p.ForeignKeys
        Assert.Empty p.Distributions
