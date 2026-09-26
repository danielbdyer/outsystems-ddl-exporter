module Projection.Tests.MetadataExtractionSqlTests

open Xunit
open Projection.Adapters.OssysSql

// ---------------------------------------------------------------------------
// Chapter 5.0 slice α — V2's `outsystems_metadata_rowsets.sql` embedded
// resource. Originally carbon-copied from V1's donor (`DECISIONS 2026-05-16
// (later)`: V2 is self-contained; V1 is editorial donor only), V2's extraction
// SQL is now maintained INDEPENDENTLY and no longer tracks V1's
// `src/AdvancedSql/` copy. These tests pin V2's resource shape directly
// (parameters, header sentinel); the V1-comparison fork/line-count pins were
// retired 2026-07-21.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Slice α: embedded SQL declares the five expected parameters`` () =
    let sql = MetadataExtractionSql.read()
    // The five parameters per V1's contract: @ModuleNamesCsv,
    // @IncludeSystem, @IncludeInactive, @OnlyActiveAttributes,
    // @EntityFilterJson. The header comment names them explicitly.
    Assert.Contains("@ModuleNamesCsv", sql)
    Assert.Contains("@IncludeSystem", sql)
    Assert.Contains("@IncludeInactive", sql)
    Assert.Contains("@OnlyActiveAttributes", sql)
    Assert.Contains("@EntityFilterJson", sql)

[<Fact>]
let ``Slice α: embedded SQL contains the goal-header sentinel comment`` () =
    let sql = MetadataExtractionSql.read()
    Assert.Contains("OutSystems → JSON (Two-phase, CTE-free)", sql)

// ---------------------------------------------------------------------------
// Slice β — V1 OSSYS bootstrap fixture (`tests/Fixtures/sql/model.edge-case
// .seed.sql`) carbon-copied to V2 at `Resources/ossys-edge-case.seed.sql`.
// The canary mockup donor: a self-contained synthetic OSSYS schema +
// deterministic edge-case data the rowsets SQL can query against.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Slice β: edge-case seed fixture loads`` () =
    let seed = MetadataExtractionSql.readEdgeCaseSeed()
    Assert.False(System.String.IsNullOrWhiteSpace seed)

[<Fact>]
let ``Slice β: edge-case seed creates the three required OSSYS tables`` () =
    let seed = MetadataExtractionSql.readEdgeCaseSeed()
    Assert.Contains("CREATE TABLE [dbo].[ossys_Espace]", seed)
    Assert.Contains("CREATE TABLE [dbo].[ossys_Entity]", seed)
    Assert.Contains("CREATE TABLE [dbo].[ossys_Entity_Attr]", seed)

[<Fact>]
let ``Slice β: edge-case seed creates physical OSUSR_ABC_CUSTOMER table`` () =
    let seed = MetadataExtractionSql.readEdgeCaseSeed()
    // The synthetic Customer table that ossys_Entity 1000 references.
    Assert.Contains("CREATE TABLE [dbo].[OSUSR_ABC_CUSTOMER]", seed)
    Assert.Contains("FK_OSUSR_ABC_CUSTOMER_OSUSR_DEF_CITY", seed)

[<Fact>]
let ``Slice β: edge-case seed populates three modules (eSpaces)`` () =
    let seed = MetadataExtractionSql.readEdgeCaseSeed()
    // The three INSERT modules (lines 96-99 of V1 fixture).
    Assert.Contains("N'AppCore'", seed)
    Assert.Contains("N'Ops'", seed)
    Assert.Contains("N'SystemUsers'", seed)
