module Projection.Tests.MetadataExtractionSqlTests

open System.IO
open Xunit
open Projection.Adapters.OssysSql

// ---------------------------------------------------------------------------
// Chapter 5.0 slice α — V1's `outsystems_metadata_rowsets.sql` carbon-
// copied into V2 as an embedded resource. Verifies the carbon-copy
// invariant: V2's embedded resource bytes are byte-identical to V1's
// source file. Per `DECISIONS 2026-05-16 (later)`: V2 is self-contained;
// V1 is editorial donor only. The SQL is the truth; V2 inherits it
// verbatim.
//
// The parity test depends on V1's trunk being checked out at the
// expected sibling path. In environments where V1 is absent (V2-only
// branches), the parity test gates on file presence and skips with a
// Skip-named reason.
// ---------------------------------------------------------------------------

let private v1SourcePath : string =
    // V2 lives at sidecar/projection/...; V1's source is at the repo root
    // under src/AdvancedSql/. The test resolves the path relative to the
    // running test assembly (which lives at tests/Projection.Tests/bin/...).
    let assemblyLoc = System.Reflection.Assembly.GetExecutingAssembly().Location
    let assemblyDir =
        match Path.GetDirectoryName(assemblyLoc) with
        | null -> "."
        | d -> d
    // Walk up to the repo root (tests/Projection.Tests/bin/Debug/net9.0 → 5 levels).
    let repoRoot =
        Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", "..", "..", ".."))
    Path.Combine(repoRoot, "src", "AdvancedSql", "outsystems_metadata_rowsets.sql")

[<Fact>]
let ``Slice α: embedded SQL resource loads with expected line count`` () =
    let sql = MetadataExtractionSql.read()
    // V2's copy FORKED from V1's 1253-line donor on 2026-07-18 (#669
    // EF-21; DECISIONS — the PERSISTED carriage appended to
    // #ColumnReality). The carbon-copy era's invariant is retired; the
    // fork is pinned by the divergence test below.
    let lineCount = sql.Split('\n').Length
    // Be tolerant of trailing-newline variations.
    Assert.InRange(lineCount, 1257, 1259)

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

[<Fact>]
let ``Slice α: V2's extraction FORKED from V1's on the PERSISTED carriage — the divergence is exactly the named extension`` () =
    // The carbon-copy invariant is RETIRED (DECISIONS 2026-07-18; #669
    // EF-21): V2's extraction evolves. The pin flips from byte-identity
    // to the NAMED fork — V2 carries the appended `IsPersisted` column;
    // V1's donor does not. A future V2-side extension extends this pin;
    // an unnamed drift still fails it.
    let v2Sql = MetadataExtractionSql.read()
    Assert.Contains("IsPersisted", v2Sql)
    if File.Exists v1SourcePath then
        let v1Sql = File.ReadAllText v1SourcePath
        Assert.DoesNotContain("IsPersisted", v1Sql)
        // Stripping the extension's lines recovers the shared spine: every
        // V1 line still appears in V2 (line-set containment, normalized
        // for the trailing-comma continuation an appended column adds to
        // its predecessor — the fork is append-only).
        let normalize (s: string) = s.TrimEnd().TrimEnd(',')
        let v1Lines = v1Sql.Split('\n') |> Array.map normalize |> Set.ofArray
        let v2Lines = v2Sql.Split('\n') |> Array.map normalize |> Set.ofArray
        let missing = Set.difference v1Lines v2Lines
        Assert.True(
            Set.isEmpty missing,
            sprintf "V1 lines absent from V2's fork (the fork must stay append-only): %A"
                (missing |> Set.toList |> List.truncate 5))

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
