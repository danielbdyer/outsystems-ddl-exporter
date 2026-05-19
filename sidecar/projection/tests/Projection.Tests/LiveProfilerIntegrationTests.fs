namespace Projection.Tests

// Docker-gated integration tests for `Projection.Adapters.Sql
// .LiveProfiler`. Bootstraps an ephemeral SQL Server database,
// creates small tables with known nulls + duplicates + FK orphans,
// runs LiveProfiler captures, and asserts the captured realities
// reflect the deployed evidence.
//
// Slice arc:
//   - A.4.7'-prelude.live-profiler (2026-05-19): AttributeReality
//     coverage (HasNulls / HasDuplicates / IsNullableInDatabase /
//     IsPresentButInactive); the `captureAttributeRealities` surface.
//   - B.3.1.foreign-key-reality (this slice): ForeignKeyReality
//     coverage (HasOrphan / OrphanCount per Reference); the
//     `captureForeignKeyRealities` surface; sibling-composability
//     under `attach`.
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
            let detail =
                es
                |> List.map (fun e -> sprintf "%s: %s" e.Code e.Message)
                |> String.concat " | "
            invalidOp (sprintf "expected Ok; got: %s" detail)

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

    // ---------------------------------------------------------------
    // Two-kind fixture for FK orphan probe — `Items` (parent) +
    // `Children` (child with FK to Items.Id). Test data: 4 Children
    // rows, one referencing a non-existent parent (orphan). Probe
    // should observe HasOrphan = true, OrphanCount = 1.
    // ---------------------------------------------------------------

    let childKindKey      = mkKey ["Children"]
    let childIdAttrKey    = mkKey ["Children"; "Id"]
    let childParentAttrKey = mkKey ["Children"; "ParentId"]
    let childFkKey        = mkKey ["Children"; "FK_Parent"]

    let childKind : Kind =
        let idAttr =
            { Attribute.create childIdAttrKey (mkName "Id") Integer with
                Column       = { ColumnName = "ID"; IsNullable = false }
                IsPrimaryKey = true
                IsMandatory  = true }
        let parentAttr =
            { Attribute.create childParentAttrKey (mkName "ParentId") Integer with
                Column       = { ColumnName = "PARENT_ID"; IsNullable = false }
                IsMandatory  = true }
        let fkReference =
            { Reference.create childFkKey (mkName "FK_Parent") childParentAttrKey itemsKindKey with
                HasDbConstraint = true
                IsConstraintTrusted = true }
        { Kind.create childKindKey (mkName "Children")
            { Schema = "dbo"; Table = "OSUSR_LP_CHILDREN"; Catalog = None }
            [ idAttr; parentAttr ]
          with References = [ fkReference ]; Indexes = []; ColumnChecks = [] }

    let twoKindCatalog : Catalog =
        {
            Modules =
                [ { SsKey = mkKey ["Module"]
                    Name  = mkName "TestModule"
                    Kinds = [ itemsKind; childKind ]
                    IsActive = true
                    ExtendedProperties = [] } ]
            Sequences = []
        }

    let childSchemaSql : string =
        "CREATE TABLE [dbo].[OSUSR_LP_CHILDREN] (" +
        "[ID] INT NOT NULL PRIMARY KEY, " +
        "[PARENT_ID] INT NOT NULL" +
        ");"

    /// Seeds 4 child rows, 3 referencing existing Items.Id values
    /// (1, 2, 3) and 1 referencing a non-existent parent (999).
    /// Expected probe result: HasOrphan = true; OrphanCount = 1L.
    let childSeedOneOrphanSql : string =
        "INSERT INTO [dbo].[OSUSR_LP_CHILDREN] ([ID], [PARENT_ID]) VALUES " +
        "(1, 1), " +
        "(2, 2), " +
        "(3, 3), " +
        "(4, 999);"

    /// Seeds 3 child rows, all referencing existing Items.Id values.
    /// Expected probe result: HasOrphan = false; OrphanCount = 0L.
    let childSeedCleanSql : string =
        "INSERT INTO [dbo].[OSUSR_LP_CHILDREN] ([ID], [PARENT_ID]) VALUES " +
        "(1, 1), " +
        "(2, 2), " +
        "(3, 4);"

    let findFkReality (realities: ForeignKeyReality list) (key: SsKey) : ForeignKeyReality =
        realities |> List.find (fun r -> r.ReferenceKey = key)

    // ---------------------------------------------------------------
    // Composite-unique fixture for slice B.3.3 — `Products` table
    // with (Name, Code) composite non-unique index. Two seeds
    // exercise the HasDuplicate true/false branches.
    // ---------------------------------------------------------------

    let productsKindKey   = mkKey ["Products"]
    let prodIdAttrKey     = mkKey ["Products"; "Id"]
    let prodNameAttrKey   = mkKey ["Products"; "Name"]
    let prodCodeAttrKey   = mkKey ["Products"; "Code"]
    let prodCompositeIxKey = mkKey ["Products"; "IX_Name_Code"]

    let productsKind : Kind =
        let idAttr =
            { Attribute.create prodIdAttrKey (mkName "Id") Integer with
                Column       = { ColumnName = "ID"; IsNullable = false }
                IsPrimaryKey = true
                IsMandatory  = true }
        let nameAttr =
            { Attribute.create prodNameAttrKey (mkName "Name") Text with
                Column      = { ColumnName = "NAME"; IsNullable = false }
                Length      = Some 50
                IsMandatory = true }
        let codeAttr =
            { Attribute.create prodCodeAttrKey (mkName "Code") Text with
                Column      = { ColumnName = "CODE"; IsNullable = false }
                Length      = Some 10
                IsMandatory = true }
        let compositeIx =
            Index.ofKeyColumns prodCompositeIxKey (mkName "IX_Name_Code")
                [ prodNameAttrKey; prodCodeAttrKey ]
        { Kind.create productsKindKey (mkName "Products")
            { Schema = "dbo"; Table = "OSUSR_LP_PRODUCTS"; Catalog = None }
            [ idAttr; nameAttr; codeAttr ]
          with References = []; Indexes = [ compositeIx ]; ColumnChecks = [] }

    let productsCatalog : Catalog =
        {
            Modules =
                [ { SsKey = mkKey ["Module"]
                    Name  = mkName "TestModule"
                    Kinds = [ productsKind ]
                    IsActive = true
                    ExtendedProperties = [] } ]
            Sequences = []
        }

    let productsSchemaSql : string =
        "CREATE TABLE [dbo].[OSUSR_LP_PRODUCTS] (" +
        "[ID] INT NOT NULL PRIMARY KEY, " +
        "[NAME] NVARCHAR(50) NOT NULL, " +
        "[CODE] NVARCHAR(10) NOT NULL" +
        ");"

    /// 4 rows; every (Name, Code) pair is distinct → HasDuplicate=false.
    let productsCleanSql : string =
        "INSERT INTO [dbo].[OSUSR_LP_PRODUCTS] ([ID], [NAME], [CODE]) VALUES " +
        "(1, N'alpha', N'A1'), " +
        "(2, N'alpha', N'A2'), " +
        "(3, N'beta',  N'B1'), " +
        "(4, N'gamma', N'G1');"

    /// 4 rows; rows 1 and 2 share (Name, Code) = ('alpha', 'A1')
    /// → HasDuplicate=true; OrphanCount-equivalent group count > 1.
    let productsDuplicateSql : string =
        "INSERT INTO [dbo].[OSUSR_LP_PRODUCTS] ([ID], [NAME], [CODE]) VALUES " +
        "(1, N'alpha', N'A1'), " +
        "(2, N'alpha', N'A1'), " +
        "(3, N'beta',  N'B1'), " +
        "(4, N'gamma', N'G1');"

    let findCompositeUnique (candidates: CompositeUniqueCandidateProfile list) (kindKey: SsKey) (attrs: SsKey list) : CompositeUniqueCandidateProfile =
        let attrSet = Set.ofList attrs
        candidates
        |> List.find (fun c -> c.KindKey = kindKey && Set.ofList c.AttributeKeys = attrSet)

    let findUnique (candidates: UniqueCandidateProfile list) (key: SsKey) : UniqueCandidateProfile =
        candidates |> List.find (fun c -> c.AttributeKey = key)

open LiveProfilerFixtures

// ---------------------------------------------------------------------------
// LiveProfiler integration: NAME column has NULL + duplicate value;
// CODE column has neither. Probe observations should match.
// ---------------------------------------------------------------------------

[<Xunit.Collection("Docker-SqlServer")>]
type LiveProfilerIntegrationTests(fixture: EphemeralContainerFixture) =

    let runCaptureScenario () : Task<AttributeReality list> =
        fixture.WithEphemeralDatabase "LiveProfiler" (fun cnn _ -> task {
            do! Deploy.executeBatch cnn schemaSql
            do! Deploy.executeBatch cnn seedSql
            let! captureResult = LiveProfiler.captureAttributeRealities cnn itemsCatalog
            return mustOk captureResult
        })

    let runForeignKeyScenario (childSeed: string) : Task<ForeignKeyReality list> =
        fixture.WithEphemeralDatabase "LiveProfilerFK" (fun cnn _ -> task {
            do! Deploy.executeBatch cnn schemaSql
            do! Deploy.executeBatch cnn childSchemaSql
            do! Deploy.executeBatch cnn seedSql
            do! Deploy.executeBatch cnn childSeed
            let! result = LiveProfiler.captureForeignKeyRealities cnn twoKindCatalog
            return mustOk result
        })

    let findColumn (columns: ColumnProfile list) (key: SsKey) : ColumnProfile =
        columns |> List.find (fun c -> c.AttributeKey = key)

    let runColumnProfileScenario () : Task<ColumnProfile list> =
        fixture.WithEphemeralDatabase "LiveProfilerColumnProfile" (fun cnn _ -> task {
            do! Deploy.executeBatch cnn schemaSql
            do! Deploy.executeBatch cnn seedSql
            let! result = LiveProfiler.captureColumnProfiles cnn itemsCatalog
            return mustOk result
        })

    let runCompositeUniqueScenario (productsSeed: string) : Task<CompositeUniqueCandidateProfile list> =
        fixture.WithEphemeralDatabase "LiveProfilerCompositeUnique" (fun cnn _ -> task {
            do! Deploy.executeBatch cnn productsSchemaSql
            do! Deploy.executeBatch cnn productsSeed
            let! result = LiveProfiler.captureCompositeUniqueCandidates cnn productsCatalog
            return mustOk result
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
    member _.``A.4.7'-prelude.live-profiler: attach composes captured realities into Profile.AttributeRealities + Profile.Columns`` () =
        if not (skipIfNoDocker "live-profiler-attach-compose") then () else
        let p =
            (fixture.WithEphemeralDatabase "LiveProfilerAttach" (fun cnn _ -> task {
                do! Deploy.executeBatch cnn schemaSql
                do! Deploy.executeBatch cnn seedSql
                let! attachResult = LiveProfiler.attach cnn itemsCatalog Profile.empty
                return mustOk attachResult
            })).GetAwaiter().GetResult()
        // AttributeRealities populated (slice A.4.7'-prelude.live-profiler).
        Assert.NotEmpty p.AttributeRealities
        Assert.Equal(3, List.length p.AttributeRealities)
        // Columns populated by slice B.3.2 — one entry per attribute including PK.
        Assert.NotEmpty p.Columns
        Assert.Equal(3, List.length p.Columns)
        // UniqueCandidates populated by slice B.3.3 — projected from
        // AttributeRealities.HasDuplicates (per-attribute, including PK).
        Assert.NotEmpty p.UniqueCandidates
        Assert.Equal(3, List.length p.UniqueCandidates)
        // No References in the items catalog → ForeignKeys empty.
        Assert.Empty p.ForeignKeys
        // No composite Indexes in itemsCatalog → CompositeUniqueCandidates empty.
        Assert.Empty p.CompositeUniqueCandidates
        // Other sibling Profile axes (filled by other adapters) remain empty.
        Assert.Empty p.Distributions

    // ---------------------------------------------------------------
    // Slice B.3.2.column-null-counts — captureColumnProfiles probes
    // null cardinality per attribute via batched COUNT_BIG query.
    // Uses the same Items fixture (4 rows; NAME has 1 NULL; CODE
    // has 0 NULLs; ID is PK NOT NULL).
    // ---------------------------------------------------------------

    [<Fact>]
    member _.``B.3.2.column-null-counts: NAME column reflects NullCount = 1 (one row with NULL)`` () =
        if not (skipIfNoDocker "live-profiler-col-null-count-name") then () else
        let columns = (runColumnProfileScenario ()).GetAwaiter().GetResult()
        let name = findColumn columns nameAttrKey
        Assert.Equal(4L, name.RowCount)
        Assert.Equal(1L, name.NullCount)

    [<Fact>]
    member _.``B.3.2.column-null-counts: CODE column reflects NullCount = 0 (no NULLs)`` () =
        if not (skipIfNoDocker "live-profiler-col-null-count-code") then () else
        let columns = (runColumnProfileScenario ()).GetAwaiter().GetResult()
        let code = findColumn columns codeAttrKey
        Assert.Equal(4L, code.RowCount)
        Assert.Equal(0L, code.NullCount)

    [<Fact>]
    member _.``B.3.2.column-null-counts: PK column reflects NullCount = 0 by construction`` () =
        if not (skipIfNoDocker "live-profiler-col-null-count-pk") then () else
        let columns = (runColumnProfileScenario ()).GetAwaiter().GetResult()
        let id = findColumn columns idAttrKey
        // PK is NOT NULL by construction → COUNT_BIG of nulls = 0.
        // Profile still emits an entry per attribute (totality).
        Assert.Equal(4L, id.RowCount)
        Assert.Equal(0L, id.NullCount)

    [<Fact>]
    member _.``B.3.2.column-null-counts: ProbeStatus reflects rowCount as SampleSize + Succeeded outcome`` () =
        if not (skipIfNoDocker "live-profiler-col-probe-status") then () else
        let columns = (runColumnProfileScenario ()).GetAwaiter().GetResult()
        let name = findColumn columns nameAttrKey
        Assert.Equal(4L, name.NullCountProbeStatus.SampleSize)
        Assert.Equal(Succeeded, name.NullCountProbeStatus.Outcome)

    [<Fact>]
    member _.``B.3.2.column-null-counts: captured profiles cover every attribute (PK + non-PK)`` () =
        if not (skipIfNoDocker "live-profiler-col-totality") then () else
        let columns = (runColumnProfileScenario ()).GetAwaiter().GetResult()
        let keys = columns |> List.map (fun c -> c.AttributeKey) |> Set.ofList
        Assert.Equal<Set<SsKey>>(
            Set.ofList [ idAttrKey; nameAttrKey; codeAttrKey ],
            keys)

    // ---------------------------------------------------------------
    // Slice B.3.1.foreign-key-reality — captureForeignKeyRealities
    // observes orphan FK rows in the deployed target. Test fixture:
    // Items + Children with a FK and one orphan row.
    // ---------------------------------------------------------------

    [<Fact>]
    member _.``B.3.1.foreign-key-reality: orphan child row produces HasOrphan = true and OrphanCount = 1`` () =
        if not (skipIfNoDocker "live-profiler-fk-orphan-present") then () else
        let realities =
            (runForeignKeyScenario childSeedOneOrphanSql).GetAwaiter().GetResult()
        let fk = findFkReality realities childFkKey
        Assert.True(fk.HasOrphan,
                    sprintf "expected HasOrphan = true (one child row references non-existent parent); got %A" fk)
        Assert.Equal(1L, fk.OrphanCount)

    [<Fact>]
    member _.``B.3.1.foreign-key-reality: clean child rows produce HasOrphan = false and OrphanCount = 0`` () =
        if not (skipIfNoDocker "live-profiler-fk-orphan-absent") then () else
        let realities =
            (runForeignKeyScenario childSeedCleanSql).GetAwaiter().GetResult()
        let fk = findFkReality realities childFkKey
        Assert.False(fk.HasOrphan,
                     sprintf "expected HasOrphan = false (every child's parent exists); got %A" fk)
        Assert.Equal(0L, fk.OrphanCount)

    [<Fact>]
    member _.``B.3.1.foreign-key-reality: ProbeStatus.SampleSize reflects the child-table row count`` () =
        if not (skipIfNoDocker "live-profiler-fk-orphan-sample-size") then () else
        let realities =
            (runForeignKeyScenario childSeedOneOrphanSql).GetAwaiter().GetResult()
        let fk = findFkReality realities childFkKey
        // 4 child rows seeded; SampleSize records source rows examined.
        Assert.Equal(4L, fk.ProbeStatus.SampleSize)
        Assert.Equal(Succeeded, fk.ProbeStatus.Outcome)

    [<Fact>]
    member _.``B.3.1.foreign-key-reality: attach composes AttributeRealities + ForeignKeys + Columns + UniqueCandidates`` () =
        if not (skipIfNoDocker "live-profiler-fk-attach-compose") then () else
        let p =
            (fixture.WithEphemeralDatabase "LiveProfilerFkAttach" (fun cnn _ -> task {
                do! Deploy.executeBatch cnn schemaSql
                do! Deploy.executeBatch cnn childSchemaSql
                do! Deploy.executeBatch cnn seedSql
                do! Deploy.executeBatch cnn childSeedOneOrphanSql
                let! attachResult = LiveProfiler.attach cnn twoKindCatalog Profile.empty
                return mustOk attachResult
            })).GetAwaiter().GetResult()
        // Four axes populated — sibling-composability across captures.
        Assert.NotEmpty p.AttributeRealities
        Assert.NotEmpty p.ForeignKeys
        Assert.NotEmpty p.Columns
        Assert.NotEmpty p.UniqueCandidates
        // Single Reference in twoKindCatalog → exactly one FK reality.
        Assert.Equal(1, List.length p.ForeignKeys)
        let fk = findFkReality p.ForeignKeys childFkKey
        Assert.True(fk.HasOrphan)
        Assert.Equal(1L, fk.OrphanCount)
        // Columns + UniqueCandidates: 3 Items attrs + 2 Children attrs = 5.
        Assert.Equal(5, List.length p.Columns)
        Assert.Equal(5, List.length p.UniqueCandidates)
        // No composite Indexes declared in twoKindCatalog.
        Assert.Empty p.CompositeUniqueCandidates
        // Other sibling Profile axes (filled by other adapters) remain empty.
        Assert.Empty p.Distributions

    // ---------------------------------------------------------------
    // Slice B.3.3.unique-candidates — captureCompositeUniqueCandidates
    // probes multi-column non-unique indexes; UniqueCandidates
    // projected from AttributeRealities.HasDuplicates at attach time.
    // ---------------------------------------------------------------

    [<Fact>]
    member _.``B.3.3.unique-candidates: composite index on distinct (Name, Code) pairs produces HasDuplicate = false`` () =
        if not (skipIfNoDocker "live-profiler-composite-unique-clean") then () else
        let candidates =
            (runCompositeUniqueScenario productsCleanSql).GetAwaiter().GetResult()
        let candidate =
            findCompositeUnique candidates productsKindKey
                [ prodNameAttrKey; prodCodeAttrKey ]
        Assert.False(candidate.HasDuplicate,
                     sprintf "expected HasDuplicate = false on distinct composite values; got %A" candidate)
        Assert.Equal(4L, candidate.ProbeStatus.SampleSize)
        Assert.Equal(Succeeded, candidate.ProbeStatus.Outcome)

    [<Fact>]
    member _.``B.3.3.unique-candidates: composite index on duplicate (Name, Code) pair produces HasDuplicate = true`` () =
        if not (skipIfNoDocker "live-profiler-composite-unique-duplicate") then () else
        let candidates =
            (runCompositeUniqueScenario productsDuplicateSql).GetAwaiter().GetResult()
        let candidate =
            findCompositeUnique candidates productsKindKey
                [ prodNameAttrKey; prodCodeAttrKey ]
        Assert.True(candidate.HasDuplicate,
                    sprintf "expected HasDuplicate = true (two rows share alpha/A1); got %A" candidate)
        Assert.Equal(4L, candidate.ProbeStatus.SampleSize)

    [<Fact>]
    member _.``B.3.3.unique-candidates: UniqueCandidates projects HasDuplicate from AttributeRealities.HasDuplicates (NAME has 'alpha' twice)`` () =
        if not (skipIfNoDocker "live-profiler-unique-projection-name") then () else
        let p =
            (fixture.WithEphemeralDatabase "LiveProfilerUniqueProjection" (fun cnn _ -> task {
                do! Deploy.executeBatch cnn schemaSql
                do! Deploy.executeBatch cnn seedSql
                let! attachResult = LiveProfiler.attach cnn itemsCatalog Profile.empty
                return mustOk attachResult
            })).GetAwaiter().GetResult()
        let nameCandidate = findUnique p.UniqueCandidates nameAttrKey
        Assert.True(nameCandidate.HasDuplicate,
                    sprintf "expected HasDuplicate = true on NAME (two 'alpha' rows); got %A" nameCandidate)

    [<Fact>]
    member _.``B.3.3.unique-candidates: UniqueCandidates projects HasDuplicate = false on CODE (all distinct)`` () =
        if not (skipIfNoDocker "live-profiler-unique-projection-code") then () else
        let p =
            (fixture.WithEphemeralDatabase "LiveProfilerUniqueProjectionCode" (fun cnn _ -> task {
                do! Deploy.executeBatch cnn schemaSql
                do! Deploy.executeBatch cnn seedSql
                let! attachResult = LiveProfiler.attach cnn itemsCatalog Profile.empty
                return mustOk attachResult
            })).GetAwaiter().GetResult()
        let codeCandidate = findUnique p.UniqueCandidates codeAttrKey
        Assert.False(codeCandidate.HasDuplicate,
                     sprintf "expected HasDuplicate = false on CODE (all distinct); got %A" codeCandidate)

    [<Fact>]
    member _.``B.3.3.unique-candidates: attach with composite index populates CompositeUniqueCandidates`` () =
        if not (skipIfNoDocker "live-profiler-composite-attach-compose") then () else
        let p =
            (fixture.WithEphemeralDatabase "LiveProfilerCompositeAttach" (fun cnn _ -> task {
                do! Deploy.executeBatch cnn productsSchemaSql
                do! Deploy.executeBatch cnn productsDuplicateSql
                let! attachResult = LiveProfiler.attach cnn productsCatalog Profile.empty
                return mustOk attachResult
            })).GetAwaiter().GetResult()
        // Composite-uniqueness axis populated with one entry per
        // non-unique multi-column Index.
        Assert.NotEmpty p.CompositeUniqueCandidates
        Assert.Equal(1, List.length p.CompositeUniqueCandidates)
        let composite =
            findCompositeUnique p.CompositeUniqueCandidates productsKindKey
                [ prodNameAttrKey; prodCodeAttrKey ]
        Assert.True(composite.HasDuplicate)
        // Per-attribute UniqueCandidates also populated via projection.
        Assert.Equal(3, List.length p.UniqueCandidates)
