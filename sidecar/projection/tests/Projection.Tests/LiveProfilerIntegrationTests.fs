namespace Projection.Tests

// Docker-gated integration tests for `Projection.Adapters.Sql
// .LiveProfiler`. Bootstraps an ephemeral SQL Server database,
// creates small tables with known nulls + duplicates + FK orphans,
// captures an EvidenceCache substrate, derives every Profile axis
// in pure F# via `Cache.deriveX`, and asserts the derivations
// reflect the deployed evidence.
//
// Post-slice-B.4.2.capture-retirement: the per-attribute /
// per-Reference SQL captures retired; every test exercises the
// cache-derive path. Coverage:
//   - AttributeReality (HasNulls / HasDuplicates / IsNullable
//     InDatabase / IsPresentButInactive) via Cache.derive
//     AttributeRealities.
//   - ForeignKeyReality (HasOrphan / OrphanCount per Reference) via
//     Cache.deriveForeignKeyRealities; sibling-composability under
//     `attach`.
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

    // Scenario helpers — post-slice-B.4.2-retirement: each derives
    // the Profile axis from the EvidenceCache substrate (one cache
    // capture, three SQL round-trips per non-static kind).

    let runCaptureScenario () : Task<AttributeReality list> =
        fixture.WithEphemeralDatabase "LiveProfiler" (fun cnn _ -> task {
            do! Deploy.executeBatch cnn schemaSql
            do! Deploy.executeBatch cnn seedSql
            let! cacheR = LiveProfiler.captureEvidenceCache cnn itemsCatalog
            let cache = mustOk cacheR
            return LiveProfiler.Cache.deriveAttributeRealities cache itemsCatalog
        })

    let runForeignKeyScenario (childSeed: string) : Task<ForeignKeyReality list> =
        fixture.WithEphemeralDatabase "LiveProfilerFK" (fun cnn _ -> task {
            do! Deploy.executeBatch cnn schemaSql
            do! Deploy.executeBatch cnn childSchemaSql
            do! Deploy.executeBatch cnn seedSql
            do! Deploy.executeBatch cnn childSeed
            let! cacheR = LiveProfiler.captureEvidenceCache cnn twoKindCatalog
            let cache = mustOk cacheR
            return LiveProfiler.Cache.deriveForeignKeyRealities cache twoKindCatalog
        })

    let findColumn (columns: ColumnProfile list) (key: SsKey) : ColumnProfile =
        columns |> List.find (fun c -> c.AttributeKey = key)

    let runColumnProfileScenario () : Task<ColumnProfile list> =
        fixture.WithEphemeralDatabase "LiveProfilerColumnProfile" (fun cnn _ -> task {
            do! Deploy.executeBatch cnn schemaSql
            do! Deploy.executeBatch cnn seedSql
            let! cacheR = LiveProfiler.captureEvidenceCache cnn itemsCatalog
            let cache = mustOk cacheR
            return LiveProfiler.Cache.deriveColumnProfiles cache itemsCatalog
        })

    let runCompositeUniqueScenario (productsSeed: string) : Task<CompositeUniqueCandidateProfile list> =
        fixture.WithEphemeralDatabase "LiveProfilerCompositeUnique" (fun cnn _ -> task {
            do! Deploy.executeBatch cnn productsSchemaSql
            do! Deploy.executeBatch cnn productsSeed
            let! cacheR = LiveProfiler.captureEvidenceCache cnn productsCatalog
            let cache = mustOk cacheR
            return LiveProfiler.Cache.deriveCompositeUniqueCandidates cache productsCatalog
        })

    let runOrphanSampleScenario (childSeed: string) : Task<DiagnosticEntry list> =
        fixture.WithEphemeralDatabase "LiveProfilerOrphanSample" (fun cnn _ -> task {
            do! Deploy.executeBatch cnn schemaSql
            do! Deploy.executeBatch cnn childSchemaSql
            do! Deploy.executeBatch cnn seedSql
            do! Deploy.executeBatch cnn childSeed
            let! cacheR = LiveProfiler.captureEvidenceCache cnn twoKindCatalog
            let cache = mustOk cacheR
            return LiveProfiler.Cache.deriveForeignKeyOrphanSamples cache twoKindCatalog
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
        // Slice 6b cache-pivot: Distributions populated from cache
        // (Integer PK gets a NumericDistribution; Text columns get
        // CategoricalDistributions where row count permits).
        Assert.NotEmpty p.Distributions

    // ---------------------------------------------------------------
    // Slice B.3.2.column-null-counts — Cache.deriveColumnProfiles
    // tallies null cardinality per attribute from the cache row-stream
    // substrate. Uses the same Items fixture (4 rows; NAME has 1 NULL;
    // CODE has 0 NULLs; ID is PK NOT NULL).
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
    // Slice B.3.1.foreign-key-reality — Cache.deriveForeignKeyRealities
    // observes orphan FK rows from the cache row-stream substrate.
    // Test fixture: Items + Children with a FK and one orphan row.
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

    // ---------------------------------------------------------------
    // Slice B.3.6.evidence-cache — captureEvidenceCache discovery +
    // pure-F# Cache.derive* primitives. Asserts the cache holds the
    // expected rows and derivations produce equivalent IR to the
    // existing SQL-probe captures.
    // ---------------------------------------------------------------

    // ---------------------------------------------------------------
    // Slice B.3.8.fk-correlation — fan-out cardinality + selectivity.
    // Uses the two-kind Items + Children fixture from slice B.3.1.
    // ---------------------------------------------------------------

    [<Fact>]
    member _.``B.3.8.fk-correlation: selectivity captures FK value frequencies (clumping)`` () =
        if not (skipIfNoDocker "live-profiler-fk-selectivity") then () else
        // Re-use the orphan-bearing scenario: 4 child rows with
        // PARENT_ID values [1, 2, 3, 999]. All four values are
        // distinct → DistinctCount = 4; each frequency = 1.
        let p =
            (fixture.WithEphemeralDatabase "LiveProfilerFkSelectivity" (fun cnn _ -> task {
                do! Deploy.executeBatch cnn schemaSql
                do! Deploy.executeBatch cnn childSchemaSql
                do! Deploy.executeBatch cnn seedSql
                do! Deploy.executeBatch cnn childSeedOneOrphanSql
                let! attachResult = LiveProfiler.attach cnn twoKindCatalog Profile.empty
                return mustOk attachResult
            })).GetAwaiter().GetResult()
        Assert.NotEmpty p.ForeignKeySelectivities
        let sel =
            p.ForeignKeySelectivities
            |> List.find (fun s -> s.ReferenceKey = childFkKey)
        Assert.Equal(4L, sel.DistinctCount)
        Assert.False(sel.IsTruncated)
        Assert.Equal(4, List.length sel.Frequencies)
        // All four FK values appear exactly once → all freq = 1.
        sel.Frequencies |> List.iter (fun (_, count) -> Assert.Equal(1L, count))

    [<Fact>]
    member _.``B.3.8.fk-correlation: fan-out cardinality requires >= 5 distinct parents`` () =
        if not (skipIfNoDocker "live-profiler-fk-cardinality-floor") then () else
        // 4 children, 4 distinct parents — below NumericDistribution
        // sample-size floor (5). No cardinality entry emitted.
        let p =
            (fixture.WithEphemeralDatabase "LiveProfilerFkCardFloor" (fun cnn _ -> task {
                do! Deploy.executeBatch cnn schemaSql
                do! Deploy.executeBatch cnn childSchemaSql
                do! Deploy.executeBatch cnn seedSql
                do! Deploy.executeBatch cnn childSeedOneOrphanSql
                let! attachResult = LiveProfiler.attach cnn twoKindCatalog Profile.empty
                return mustOk attachResult
            })).GetAwaiter().GetResult()
        // Below floor → empty cardinality list.
        Assert.Empty p.ForeignKeyCardinalities

    [<Fact>]
    member _.``B.3.8.fk-correlation: fan-out cardinality summarizes child-count-per-parent distribution when >= 5 distinct parents`` () =
        if not (skipIfNoDocker "live-profiler-fk-cardinality") then () else
        // Need 5+ distinct parent values to exceed NumericDistribution
        // SampleSize floor. Seed 10 children across 5 distinct parents
        // with skewed distribution: parent 1 has 4 children, parent 2
        // has 3, parent 3 has 1, parent 4 has 1, parent 5 has 1.
        // Child counts: [4, 3, 1, 1, 1].
        let skewedSeed =
            "INSERT INTO [dbo].[OSUSR_LP_CHILDREN] ([ID], [PARENT_ID]) VALUES " +
            "(10, 1), (11, 1), (12, 1), (13, 1), " +
            "(14, 2), (15, 2), (16, 2), " +
            "(17, 3), (18, 4), (19, 5);"
        let p =
            (fixture.WithEphemeralDatabase "LiveProfilerFkCardinality" (fun cnn _ -> task {
                do! Deploy.executeBatch cnn schemaSql
                do! Deploy.executeBatch cnn childSchemaSql
                do! Deploy.executeBatch cnn seedSql
                do! Deploy.executeBatch cnn skewedSeed
                let! attachResult = LiveProfiler.attach cnn twoKindCatalog Profile.empty
                return mustOk attachResult
            })).GetAwaiter().GetResult()
        Assert.NotEmpty p.ForeignKeyCardinalities
        let card =
            p.ForeignKeyCardinalities
            |> List.find (fun c -> c.ReferenceKey = childFkKey)
        let dist = card.ChildCountDistribution
        // 5 distinct parents → SampleSize = 5.
        Assert.Equal(5L, dist.SampleSize)
        // Min = 1 child (parents 3/4/5); Max = 4 children (parent 1).
        Assert.Equal(1M, dist.Min)
        Assert.Equal(4M, dist.Max)
        // Mean = (4 + 3 + 1 + 1 + 1) / 5 = 2.0.
        match dist.Moments with
        | Some m -> Assert.Equal(2M, m.Mean)
        | None   -> Assert.Fail "Expected Moments populated"

    [<Fact>]
    member _.``B.3.7.sampling: captureEvidenceCacheWith maxRows caps the row stream per kind`` () =
        if not (skipIfNoDocker "live-profiler-sampling-cap") then () else
        let cache =
            (fixture.WithEphemeralDatabase "LiveProfilerSampling" (fun cnn _ -> task {
                do! Deploy.executeBatch cnn schemaSql
                do! Deploy.executeBatch cnn seedSql
                let options =
                    { SqlProfilerOptions.defaults with MaxRowsPerKind = Some 2 }
                let! result = LiveProfiler.captureEvidenceCacheWith options cnn itemsCatalog
                return mustOk result
            })).GetAwaiter().GetResult()
        let cached = cache.Kinds.[itemsKindKey]
        // Sample cap = 2 rows; full table has 4. RowCount aggregate is
        // independent of sampling (full COUNT_BIG over the table).
        Assert.Equal(4L, cached.RowCount)
        // Sampled column-array length reflects the TOP-N cap.
        for column in cached.Columns do
            Assert.Equal(2, column.Values.Length)

    [<Fact>]
    member _.``B.3.7.sampling: default options preserve full-scan behavior (slice 6 baseline)`` () =
        if not (skipIfNoDocker "live-profiler-sampling-default") then () else
        let cache =
            (fixture.WithEphemeralDatabase "LiveProfilerSamplingDefault" (fun cnn _ -> task {
                do! Deploy.executeBatch cnn schemaSql
                do! Deploy.executeBatch cnn seedSql
                let! result =
                    LiveProfiler.captureEvidenceCacheWith
                        SqlProfilerOptions.defaults cnn itemsCatalog
                return mustOk result
            })).GetAwaiter().GetResult()
        let cached = cache.Kinds.[itemsKindKey]
        // No sampling cap → values.Length = RowCount (full scan).
        Assert.Equal(4L, cached.RowCount)
        for column in cached.Columns do
            Assert.Equal(4, column.Values.Length)

    [<Fact>]
    member _.``B.3.6.evidence-cache: captureEvidenceCache holds full-scan row data per kind (column-oriented)`` () =
        if not (skipIfNoDocker "live-profiler-cache-capture") then () else
        let cache =
            (fixture.WithEphemeralDatabase "LiveProfilerCache" (fun cnn _ -> task {
                do! Deploy.executeBatch cnn schemaSql
                do! Deploy.executeBatch cnn seedSql
                let! result = LiveProfiler.captureEvidenceCache cnn itemsCatalog
                return mustOk result
            })).GetAwaiter().GetResult()
        Assert.True(Map.containsKey itemsKindKey cache.Kinds)
        let cached = cache.Kinds.[itemsKindKey]
        Assert.Equal(4L, cached.RowCount)
        Assert.Equal(3, List.length cached.Columns)
        for column in cached.Columns do
            Assert.Equal(4, column.Values.Length)

    [<Fact>]
    member _.``B.3.6.evidence-cache: cache holds exact per-attribute NullCount from aggregate query`` () =
        if not (skipIfNoDocker "live-profiler-cache-nullcounts") then () else
        let cache =
            (fixture.WithEphemeralDatabase "LiveProfilerCacheNulls" (fun cnn _ -> task {
                do! Deploy.executeBatch cnn schemaSql
                do! Deploy.executeBatch cnn seedSql
                let! result = LiveProfiler.captureEvidenceCache cnn itemsCatalog
                return mustOk result
            })).GetAwaiter().GetResult()
        let cached = cache.Kinds.[itemsKindKey]
        Assert.Equal(1L, cached.NullCounts.[nameAttrKey])
        Assert.Equal(0L, cached.NullCounts.[codeAttrKey])
        Assert.Equal(0L, cached.NullCounts.[idAttrKey])

    [<Fact>]
    member _.``B.3.6.evidence-cache: Cache.deriveAttributeRealities populates HasNulls + HasDuplicates + IsNullableInDatabase`` () =
        if not (skipIfNoDocker "live-profiler-cache-attr-shape") then () else
        let derived =
            (fixture.WithEphemeralDatabase "LiveProfilerCacheAttr" (fun cnn _ -> task {
                do! Deploy.executeBatch cnn schemaSql
                do! Deploy.executeBatch cnn seedSql
                let! cacheR = LiveProfiler.captureEvidenceCache cnn itemsCatalog
                let cache = mustOk cacheR
                return LiveProfiler.Cache.deriveAttributeRealities cache itemsCatalog
            })).GetAwaiter().GetResult()
        let keysOf (xs: AttributeReality list) = xs |> List.map (fun r -> r.AttributeKey) |> Set.ofList
        Assert.Equal<Set<SsKey>>(Set.ofList [ idAttrKey; nameAttrKey; codeAttrKey ], keysOf derived)
        let name = derived |> List.find (fun r -> r.AttributeKey = nameAttrKey)
        Assert.True name.HasNulls
        Assert.True name.HasDuplicates
        Assert.True name.IsNullableInDatabase

    [<Fact>]
    member _.``B.3.6.evidence-cache: Cache.deriveColumnProfiles populates RowCount + NullCount per attribute`` () =
        if not (skipIfNoDocker "live-profiler-cache-col-shape") then () else
        let derived =
            (fixture.WithEphemeralDatabase "LiveProfilerCacheCol" (fun cnn _ -> task {
                do! Deploy.executeBatch cnn schemaSql
                do! Deploy.executeBatch cnn seedSql
                let! cacheR = LiveProfiler.captureEvidenceCache cnn itemsCatalog
                let cache = mustOk cacheR
                return LiveProfiler.Cache.deriveColumnProfiles cache itemsCatalog
            })).GetAwaiter().GetResult()
        Assert.Equal(3, List.length derived)
        let name = derived |> List.find (fun c -> c.AttributeKey = nameAttrKey)
        Assert.Equal(4L, name.RowCount)
        Assert.Equal(1L, name.NullCount)

    [<Fact>]
    member _.``B.3.6.evidence-cache: attach via cache pivot composes every Profile axis (AttributeRealities + Columns + UniqueCandidates + ForeignKeys + CompositeUniqueCandidates)`` () =
        if not (skipIfNoDocker "live-profiler-cache-attach-integration") then () else
        let p =
            (fixture.WithEphemeralDatabase "LiveProfilerCacheAttach" (fun cnn _ -> task {
                do! Deploy.executeBatch cnn schemaSql
                do! Deploy.executeBatch cnn childSchemaSql
                do! Deploy.executeBatch cnn seedSql
                do! Deploy.executeBatch cnn childSeedOneOrphanSql
                let! attachResult = LiveProfiler.attach cnn twoKindCatalog Profile.empty
                return mustOk attachResult
            })).GetAwaiter().GetResult()
        // Every cache-derived Profile axis populates (slice B.3.6b
        // + B.4.2 retirement: cache is the sole derivation substrate).
        Assert.NotEmpty p.AttributeRealities
        Assert.NotEmpty p.Columns
        Assert.NotEmpty p.UniqueCandidates
        Assert.NotEmpty p.ForeignKeys
        let fk = findFkReality p.ForeignKeys childFkKey
        Assert.True(fk.HasOrphan)
        Assert.Equal(1L, fk.OrphanCount)

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
        // Slice 6b cache-pivot: Distributions populated from cache
        // (Integer PKs get NumericDistributions; Text columns get
        // CategoricalDistributions where row counts permit).
        Assert.NotEmpty p.Distributions

    // ---------------------------------------------------------------
    // Slice B.3.3.unique-candidates — Cache.deriveCompositeUnique
    // Candidates derives multi-column non-unique evidence from the
    // cache row-stream substrate; UniqueCandidates project from
    // AttributeRealities.HasDuplicates at attach time.
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

    // ---------------------------------------------------------------
    // Slice B.3.4.fk-orphan-samples — Cache.deriveForeignKeyOrphan
    // Samples produces DiagnosticEntry per orphan-bearing FK (pillar
    // 9: operational diagnostics; not Profile axis). Reuses the
    // Items + Children fixture from slice B.3.1.
    // ---------------------------------------------------------------

    [<Fact>]
    member _.``B.3.4.fk-orphan-samples: orphan-bearing FK emits one DiagnosticEntry with sample values`` () =
        if not (skipIfNoDocker "live-profiler-orphan-sample-emit") then () else
        let entries =
            (runOrphanSampleScenario childSeedOneOrphanSql).GetAwaiter().GetResult()
        Assert.Equal(1, List.length entries)
        let entry = List.head entries
        Assert.Equal(DiagnosticSeverity.Warning, entry.Severity)
        Assert.Equal("profiling.foreignKey.orphanSample", entry.Code)
        Assert.Equal(Some childFkKey, entry.SsKey)
        Assert.Equal("1", entry.Metadata |> Map.find "orphanCount")
        Assert.Equal("1", entry.Metadata |> Map.find "sampleSize")
        // The orphan value in childSeedOneOrphanSql is 999.
        Assert.Equal("999", entry.Metadata |> Map.find "sample.0")
        Assert.Equal("PARENT_ID", entry.Metadata |> Map.find "sourceColumn")
        Assert.Equal("ID", entry.Metadata |> Map.find "targetColumn")

    [<Fact>]
    member _.``B.3.4.fk-orphan-samples: clean FK produces empty DiagnosticEntry list`` () =
        if not (skipIfNoDocker "live-profiler-orphan-sample-clean") then () else
        let entries =
            (runOrphanSampleScenario childSeedCleanSql).GetAwaiter().GetResult()
        // No orphans → no DiagnosticEntry emitted.
        Assert.Empty entries

    [<Fact>]
    member _.``B.3.4.fk-orphan-samples: sample respects deterministic ordering and TOP-N limit`` () =
        if not (skipIfNoDocker "live-profiler-orphan-sample-ordering") then () else
        // Build a fixture with 7 orphan rows; default limit is 5.
        let manyOrphansSeed =
            "INSERT INTO [dbo].[OSUSR_LP_CHILDREN] ([ID], [PARENT_ID]) VALUES " +
            "(10, 901), (11, 902), (12, 903), (13, 904), " +
            "(14, 905), (15, 906), (16, 907);"
        let entries =
            (runOrphanSampleScenario manyOrphansSeed).GetAwaiter().GetResult()
        Assert.Equal(1, List.length entries)
        let entry = List.head entries
        Assert.Equal("7", entry.Metadata |> Map.find "orphanCount")
        Assert.Equal("5", entry.Metadata |> Map.find "sampleSize")
        // Determinism: ordered by orphan value ascending.
        Assert.Equal("901", entry.Metadata |> Map.find "sample.0")
        Assert.Equal("902", entry.Metadata |> Map.find "sample.1")
        Assert.Equal("903", entry.Metadata |> Map.find "sample.2")
        Assert.Equal("904", entry.Metadata |> Map.find "sample.3")
        Assert.Equal("905", entry.Metadata |> Map.find "sample.4")
        // Sixth and seventh orphans (906, 907) are below the TOP-N cap.
        Assert.False(entry.Metadata |> Map.containsKey "sample.5")

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
